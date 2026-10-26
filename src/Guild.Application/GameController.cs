using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Guild.Domain.Battle;
using Guild.Domain.Definitions;
using Guild.Domain.Numerics;
using Guild.Domain.Randomness;
using Guild.Domain.Serialization;
using Guild.Persistence;

namespace Guild.Application;

public sealed partial class GameController : IDisposable
{
    private readonly SaveStore store;
    private ContentBundle content;
    private PlayerProfile profile = new();
    private MatchSession? session;
    private BattleSimulation? battle;
    private long revision;
    private double accumulator;
    private bool paused;
    private int speed = 1;
    private string status = DisplayText.Get("app.status.initial");
    private List<StoredHistory> history = [];
    private static readonly string[] LevelKinds = ["vitality", "firepower", "rapid", "mobility", "reach", "defense"];
    public string SavePath => store.Path;
    public string? MigrationBackupPath => store.MigrationBackupPath;

    public GameController(string savePath, string contentPath, Action<CommitStage>? saveFault = null)
    {
        content = ContentBundle.Load(contentPath);
        ValidateSupportedContent(content);
        PreflightExistingSave(savePath, content);
        store = new SaveStore(savePath, saveFault);
        try
        {
            Reload();
            if (store.ReadDocument("profile") is null)
            {
                profile = new PlayerProfile { GrowthRng = new Pcg32(20261024, 71).Capture(), GenerationRng = new Pcg32(20261024, 93).Capture() };
                foreach (var name in new[] { DisplayText.Get("app.initial.actor.aoi"), DisplayText.Get("app.initial.actor.ren"), DisplayText.Get("app.initial.actor.mina"), DisplayText.Get("app.initial.actor.kai"), DisplayText.Get("app.initial.actor.yui"), DisplayText.Get("app.initial.actor.haru") }) AddAdventurer(name);
                // Unowned comparison equipment makes the transfer event reviewable without granting currency.
                profile.Weapons.Add(CreateWeapon("guild"));
                InitializeEconomy(profile, content);
                Persist("initialize", "initialize-v1", DisplayText.Get("app.initial.supplied"), []);
            }
        }
        catch { store.Dispose(); throw; }
    }

    private void Reload()
    {
        var profileJson = store.ReadDocument("profile");
        var sessionJson = store.ReadDocument("session");
        (profile, session, battle) = ValidateSavedDocuments(profileJson, sessionJson, content);
        revision = store.Revision;
        if (profileJson is not null && (ReadDocumentVersion(profileJson) < 3 || session is { FormatVersion: < 3 }))
        {
            store.BackupBeforeDocumentMigration(3);
            InitializeEconomy(profile, content);
            profile.FormatVersion = 3;
            if (session is not null) session.FormatVersion = 3;
            var id = "migrate-documents-v3:" + revision;
            var hash = Hash(profileJson + "\n" + sessionJson);
            try
            {
                var migrated = store.Commit(new(revision, id, hash, DisplayText.Get("app.save.migrated"),
                    new Dictionary<string, string> { ["profile"] = Serialize(profile), ["session"] = Serialize(session) }, []));
                revision = migrated.Revision;
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
            {
                if (store.FindCommand(id, hash) is null) throw;
                revision = store.Revision;
            }
        }
        if (session?.Phase == "Battle" && session.WaveStartGrowthRng is { } growthRng)
            profile.GrowthRng = growthRng;
        history = [];
        long after = 0;
        while (true)
        {
            var page = store.ReadHistory(after, 200);
            history.AddRange(page);
            if (page.Count < 200) break;
            after = page[^1].Sequence;
        }
        accumulator = 0;
    }

    private static int ReadDocumentVersion(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(DisplayText.Get("app.error.invalidSaveFormat"));
        return document.RootElement.TryGetProperty("formatVersion", out var version) ? version.GetInt32() : 1;
    }

    private static (PlayerProfile Profile, MatchSession? Session, BattleSimulation? Battle) ValidateSavedDocuments(string? profileJson, string? sessionJson, ContentBundle definitions)
    {
        if (profileJson is not null && ReadDocumentVersion(profileJson) is not (1 or 2 or 3))
            throw new NotSupportedException(DisplayText.Get("app.error.unsupportedProfileFormat"));
        if (profileJson is not null && ReadDocumentVersion(profileJson) == 3)
            RequireEconomyJson(profileJson, profileDocument: true);
        var loadedProfile = profileJson is null ? new PlayerProfile() : Deserialize<PlayerProfile>(profileJson);
        if (profileJson is not null) loadedProfile.FormatVersion = ReadDocumentVersion(profileJson);
        if (profileJson is not null) ValidateProfileOwnership(loadedProfile);
        if (sessionJson is null or "null")
        {
            if (profileJson is not null) ValidateEconomyDocuments(loadedProfile, null, definitions);
            return (loadedProfile, null, null);
        }
        if (profileJson is null) throw new InvalidDataException(DisplayText.Get("app.error.sessionWithoutProfile"));
        if (ReadDocumentVersion(sessionJson) is not (1 or 2 or 3))
            throw new NotSupportedException(DisplayText.Get("app.error.unsupportedSessionFormat"));
        if (ReadDocumentVersion(sessionJson) == 3)
            RequireEconomyJson(sessionJson, profileDocument: false);
        var loadedSession = Deserialize<MatchSession>(sessionJson);
        loadedSession.FormatVersion = ReadDocumentVersion(sessionJson);
        if ((loadedProfile.FormatVersion == 3) != (loadedSession.FormatVersion == 3))
            throw new InvalidDataException("プロフィールと進行中作戦の保存形式が混在しています。データは変更していません。");
        if (loadedSession.Current is null || loadedSession.StartingRanks is null || loadedSession.LevelChoices is null)
            throw new InvalidDataException(DisplayText.Get("app.error.incompleteSession"));
        var ownedIds = loadedProfile.Adventurers.Select(actor => actor.Id).ToHashSet(StringComparer.Ordinal);
        if (!ownedIds.SetEquals(loadedSession.StartingRanks.Keys) || loadedSession.StartingRanks.Values.Any(rank => rank < 1)
            || loadedSession.LevelChoices.Keys.Any(id => !ownedIds.Contains(id)))
            throw new InvalidDataException(DisplayText.Get("app.error.inconsistentSessionActors"));
        if (loadedSession.Phase is not ("Preparation" or "Battle" or "Victory" or "Defeat"))
            throw new InvalidDataException(DisplayText.Get("app.error.unknownSessionPhase"));
        var canonicalRules = new HashSet<string>(StringComparer.Ordinal);
        foreach (var snapshot in new[] { loadedSession.Current, loadedSession.MatchStart, loadedSession.WaveStart }.Where(x => x is not null))
        {
            if (snapshot!.Setup is null || snapshot.Setup.Content is null || snapshot.Setup.Units is null || snapshot.Units is null
                || snapshot.Setup.MatchId != loadedSession.MatchId
                || !ownedIds.SetEquals(snapshot.Setup.Units.Select(unit => unit.Id))
                || snapshot.Setup.Units.Select(unit => unit.Id).Distinct(StringComparer.Ordinal).Count() != ownedIds.Count
                || !ownedIds.SetEquals(snapshot.Units.Where(unit => !unit.IsEnemy).Select(unit => unit.Id))
                || snapshot.Units.Select(unit => unit.Id).Distinct(StringComparer.Ordinal).Count() != snapshot.Units.Count)
                throw new InvalidDataException(DisplayText.Get("app.error.inconsistentBattleRoster"));
            if (snapshot!.Setup.Content.ComputeHash() != loadedSession.ContentHash)
                throw new InvalidDataException(DisplayText.Get("app.error.definitionHashMismatch"));
            ValidateSupportedContent(snapshot.Setup.Content);
            _ = BattleSimulation.Restore(snapshot);
            canonicalRules.Add(snapshot.RuleSetId ?? "defense-v1");
        }
        if (canonicalRules.Count != 1)
            throw new InvalidDataException(DisplayText.Get("app.error.mixedRuleSets"));
        ValidateEconomyDocuments(loadedProfile, loadedSession, definitions);
        var restored = BattleSimulation.Restore(loadedSession.Phase == "Battle"
            ? loadedSession.WaveStart ?? throw new InvalidDataException(DisplayText.Get("app.error.missingWaveStart")) : loadedSession.Current);
        return (loadedProfile, loadedSession, restored);
    }

    private static void ValidateProfileOwnership(PlayerProfile value)
    {
        if (value.Adventurers is null || value.Weapons is null || value.PendingEvents is null || value.DiscoveredRecipes is null
            || value.Adventurers.Count is < 1 or > 50 || value.Capacity < value.Adventurers.Count || value.Capacity > 50
            || value.Resources < 0 || value.GrowthRng is null || value.GenerationRng is null)
            throw new InvalidDataException(DisplayText.Get("app.error.invalidOwnership"));
        var actorIds = value.Adventurers.Select(actor => actor.Id).ToHashSet(StringComparer.Ordinal);
        if (actorIds.Count != value.Adventurers.Count || actorIds.Any(string.IsNullOrWhiteSpace)
            || value.Weapons.Any(weapon => string.IsNullOrWhiteSpace(weapon.Id) || weapon.SkillIds is null
                || weapon.OwnerId != "guild" && !actorIds.Contains(weapon.OwnerId))
            || value.Weapons.Select(weapon => weapon.Id).Distinct(StringComparer.Ordinal).Count() != value.Weapons.Count
            || value.Adventurers.Any(actor => actor.Stats is null || actor.Growth is null || actor.Rank < 1 || actor.Experience < 0
                || actor.Hp < 0 || actor.WeaknessStacks < 0 || actor.Blessings is null || actor.NextMatchChanges is null
                || !value.Weapons.Any(weapon => weapon.Id == actor.WeaponId && weapon.OwnerId == actor.Id))
            || value.PendingEvents.Any(pending => !actorIds.Contains(pending.ActorId)))
            throw new InvalidDataException(DisplayText.Get("app.error.inconsistentOwnership"));
    }

    private static void PreflightExistingSave(string path, ContentBundle definitions)
    {
        if (!File.Exists(path)) return;
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            { DataSource = Path.GetFullPath(path), Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var version = connection.CreateCommand(); version.CommandText = "PRAGMA user_version;";
        if (Convert.ToInt32(version.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > SaveStore.SchemaVersion)
            throw new NotSupportedException(DisplayText.Get("app.error.unsupportedDatabaseFormat"));
        using var tables = connection.CreateCommand(); tables.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='documents';";
        if (Convert.ToInt32(tables.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) == 0) return;
        string? Read(string key)
        {
            using var command = connection.CreateCommand(); command.CommandText = "SELECT value FROM documents WHERE key=$key;";
            command.Parameters.AddWithValue("$key", key); return command.ExecuteScalar() as string;
        }
        _ = ValidateSavedDocuments(Read("profile"), Read("session"), definitions);
    }

    public CommandResult Execute(GameCommand command)
    {
        var commandId = command.CommandId ?? Guid.NewGuid().ToString("N");
        var hash = Hash(JsonSerializer.Serialize(command with { CommandId = null }, DomainJson.Options));
        var originalProfile = Serialize(profile);
        var originalSession = Serialize(session);
        var originalBattle = battle?.Capture();
        var originalPause = paused;
        var originalSpeed = speed;
        try
        {
            var repeated = store.FindCommand(commandId, hash);
            if (repeated is not null)
            {
                if (store.Revision != revision) Reload();
                return new(true, repeated.Message);
            }
            var before = Serialize(profile);
            var eventOperation = command.Type is "BeginEvent" or "ChooseBlessing" or "DiscardBlessings" or "RerollBlessings" or "TransferItem" or "Fuse" or "ResolveRandom" or "DeclineRandom";
            var pendingBefore = eventOperation ? PeekGrowthEvent() : null;
            var historyActorId = pendingBefore?.ActorId ?? command.ActorId;
            var historyRank = pendingBefore?.Rank;
            var message = Dispatch(command);
            if (session is not null && battle is not null) session.Current = battle.Capture();
            var actor = profile.Adventurers.FirstOrDefault(x => x.Id == historyActorId);
            var entries = new List<HistoryEntry> { new(commandId, actor?.Id ?? "player", actor is null ? "0" : (((historyRank ?? actor.Rank) - 1) / 10).ToString(), command.Type,
                before, Serialize(profile), message) };
            if (command.Type == "ReturnToGuild")
            {
                var previous = Deserialize<PlayerProfile>(before);
                foreach (var currentActor in profile.Adventurers.OrderBy(x => x.Id, StringComparer.Ordinal))
                {
                    var oldActor = previous.Adventurers.Single(x => x.Id == currentActor.Id);
                    if (Serialize(oldActor) == Serialize(currentActor)) continue;
                    entries.Add(new(commandId + ":" + currentActor.Id, currentActor.Id, ((currentActor.Rank - 1) / 10).ToString(), "AdventurerResult",
                        Serialize(oldActor), Serialize(currentActor), $"{currentActor.Name}: Rank {oldActor.Rank}→{currentActor.Rank}、HP {oldActor.Hp}→{currentActor.Hp}。"));
                }
            }
            Persist(commandId, hash, message, entries);
            status = message;
            return new(true, message);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or InvalidDataException or IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            // A lost acknowledgement after COMMIT is a completed operation, not a rollback.
            try
            {
                var committed = store.FindCommand(commandId, hash);
                if (committed is not null)
                {
                    revision = store.Revision;
                    RefreshHistory();
                    status = committed.Message;
                    return new(true, status);
                }
            }
            catch (InvalidOperationException) { /* Reusing an ID with a different payload remains an error. */ }
            profile = Deserialize<PlayerProfile>(originalProfile);
            session = originalSession == "null" ? null : Deserialize<MatchSession>(originalSession);
            battle = originalBattle is null ? null : BattleSimulation.Restore(originalBattle);
            paused = originalPause;
            speed = originalSpeed;
            status = exception.Message;
            return new(false, status);
        }
    }

    private void RefreshHistory()
    {
        long after = history.Count == 0 ? 0 : history[^1].Sequence;
        while (true)
        {
            var page = store.ReadHistory(after, 200);
            history.AddRange(page);
            if (page.Count < 200) return;
            after = page[^1].Sequence;
        }
    }

    private string Dispatch(GameCommand command)
    {
        if (command.Type is "Deploy" or "Reposition" or "BuyLevel" or "ChooseLevel" or "Revive" or "SetTarget")
            RequireNotDispatched(command.ActorId);
        var selected = PeekGrowthEvent();
        if (selected?.Kind is not null && command.Type is not ("ChooseBlessing" or "DiscardBlessings" or "RerollBlessings" or "TransferItem" or "Fuse" or "ResolveRandom" or "DeclineRandom"))
            throw new InvalidOperationException(DisplayText.Get("app.error.completeGrowthEvent"));
        return command.Type switch
        {
            "ReserveDispatch" => ReserveDispatch(command), "CancelDispatch" => CancelDispatch(), "Deliver" => Deliver(command),
            "Rename" => Rename(command), "Recruit" => Recruit(), "ExpandRoster" => ExpandRoster(),
            "StartMatch" => StartMatch(command), "Deploy" => Deploy(command, false), "Reposition" => Deploy(command, true),
            "StartWave" => StartWave(), "BuyLevel" => BuyLevel(command), "ChooseLevel" => ChooseLevel(command),
            "Revive" => Revive(command), "SetTarget" => SetTarget(command), "TogglePause" => TogglePause(),
            "SetSpeed" => SetSpeed(command), "Retry" => Retry(), "ReturnToGuild" => ReturnToGuild(),
            "BeginEvent" => BeginEvent(command), "ChooseBlessing" => ChooseBlessing(command),
            "DiscardBlessings" => DiscardBlessings(), "RerollBlessings" => RerollBlessings(),
            "TransferItem" => TransferItem(command), "EquipItem" => EquipItem(command),
            "Fuse" => Fuse(command), "ResolveRandom" => ResolveRandom(), "DeclineRandom" => DeclineRandom(), "BuyWeapon" => BuyWeapon(command),
            _ => throw new InvalidOperationException(DisplayText.Get("app.error.unsupportedCommand"))
        };
    }

    private void Persist(string id, string hash, string message, IReadOnlyList<HistoryEntry> entries)
    {
        var result = store.Commit(new(revision, id, hash, message,
            new Dictionary<string, string> { ["profile"] = Serialize(profile), ["session"] = Serialize(session) }, entries));
        revision = result.Revision;
        if (result.Applied)
        {
            var lastSequence = history.Count == 0 ? 0 : history[^1].Sequence;
            history.AddRange(store.ReadHistory(lastSequence, Math.Max(entries.Count, 1)));
        }
    }

    public void Advance(double elapsedSeconds)
    {
        if (elapsedSeconds < 0 || !double.IsFinite(elapsedSeconds)) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (paused || session?.Phase != "Battle" || battle is null) return;
        accumulator += elapsedSeconds * speed;
        while (accumulator + 1e-12 >= .02 && session.Phase == "Battle")
        {
            accumulator -= .02;
            battle.Step();
            if (battle.State.Defeated || battle.State.Victory || battle.State.WaveCompleted)
            {
                var beforeSession = Serialize(session);
                var beforeProfile = Serialize(profile);
                session.Phase = battle.State.Defeated ? "Defeat" : battle.State.Victory ? "Victory" : "Preparation";
                if (!battle.State.Defeated) battle.GrantWaveExperience();
                PrepareLevelChoices();
                session.Current = battle.Capture();
                var id = session.AttemptId + ":wave:" + battle.State.Wave + ":end";
                var message = battle.IsInvasion
                    ? session.Phase == "Defeat" ? InvasionDefeatDescription(battle.State.DefeatReason) + DisplayText.Get("app.status.chooseRetryOrReturn")
                        : session.Phase == "Victory" ? DisplayText.Get("app.status.invasionVictory") : DisplayText.Format("app.status.sectionClear", battle.State.Wave)
                    : session.Phase == "Defeat" ? DisplayText.Get("app.status.defenseDefeat") : session.Phase == "Victory" ? DisplayText.Get("app.status.defenseVictory") : DisplayText.Format("app.status.waveClear", battle.State.Wave);
                try { Persist(id, Hash(id), message, [new(id, "player", "0", "WaveEnd", beforeSession, Serialize(session), message)]); status = message; }
                catch (Exception exception) when (exception is IOException or Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
                {
                    if (store.FindCommand(id, Hash(id)) is not null)
                    {
                        revision = store.Revision; RefreshHistory(); status = message;
                    }
                    else
                    {
                        profile = Deserialize<PlayerProfile>(beforeProfile);
                        session = Deserialize<MatchSession>(beforeSession);
                        battle = BattleSimulation.Restore(session.WaveStart!);
                        if (session.WaveStartGrowthRng is { } growthRng) profile.GrowthRng = growthRng;
                        paused = true;
                        status = DisplayText.Get("app.error.failedSavePausedPrefix") + exception.Message;
                    }
                }
                accumulator = 0;
            }
        }
    }

    private string Rename(GameCommand command)
    {
        RequireGuild();
        var actor = Actor(command.ActorId);
        var name = command.Value?.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException(DisplayText.Get("app.error.enterName"));
        actor.Name = name;
        return DisplayText.Format("app.rename.completed", name);
    }

    private string Recruit()
    {
        RequireGuild();
        if (profile.Adventurers.Count >= profile.Capacity) throw new InvalidOperationException(DisplayText.Get("app.error.rosterFull"));
        if (profile.Resources < 100) throw new InvalidOperationException(DisplayText.Get("app.error.recruitResources"));
        AddAdventurer(DisplayText.Get("app.actor.newName"));
        profile.Resources -= 100;
        return DisplayText.Get("app.recruit.completed");
    }

    private void AddAdventurer(string name)
    {
        var random = Pcg32.Restore(profile.GenerationRng);
        BigInteger Roll() { var draw = random.NextInt(100); return 10 + (draw < 20 ? -1 : draw < 80 ? 0 : 1); }
        var actor = new AdventurerState { Id = Guid.NewGuid().ToString("N"), Name = name, Stats = new(Roll(), Roll(), Roll(), Roll(), Roll()) };
        actor.Hp = 100 + 10 * actor.Stats.Vit;
        profile.GenerationRng = random.Capture();
        var weapon = CreateWeapon(actor.Id, content.WeaponTemplates[profile.Adventurers.Count % content.WeaponTemplates.Count].Id);
        actor.WeaponId = weapon.Id;
        profile.Weapons.Add(weapon);
        profile.Adventurers.Add(actor);
    }

    private WeaponState CreateWeapon(string owner, string? templateId = null)
    {
        var random = Pcg32.Restore(profile.GenerationRng);
        var generated = WeaponGenerator.Generate(content, random, templateId);
        profile.GenerationRng = random.Capture();
        return new() { Id = Guid.NewGuid().ToString("N"), TemplateId = generated.TemplateId, Name = generated.Name,
            OwnerId = owner, BasePower = generated.BasePower, AttackIntervalMs = generated.AttackIntervalMs,
            RangeCenti = generated.RangeCenti, SkillIds = generated.SkillIds.ToList(), Seed = generated.Seed,
            GeneratorVersion = generated.GeneratorVersion, DefinitionVersion = content.Version };
    }

    private string ExpandRoster()
    {
        RequireGuild();
        if (profile.Capacity >= 50) throw new InvalidOperationException(DisplayText.Get("app.error.rosterLimit"));
        var cost = 200 + 100 * profile.ExpansionCount;
        if (profile.Resources < cost) throw new InvalidOperationException(DisplayText.Format("app.error.expansionResources", cost));
        profile.Resources -= cost; profile.ExpansionCount++; profile.Capacity += 5;
        return DisplayText.Format("app.expansion.completed", profile.Capacity);
    }

    private string StartMatch(GameCommand command)
    {
        RequireGuild();
        var stage = command.TargetId is null ? content.Stages.FirstOrDefault(s => s.Mode != "invasion")
            : content.Stages.FirstOrDefault(s => s.Id == command.TargetId);
        if (stage is null) throw new InvalidOperationException(DisplayText.Get("app.error.chooseStage"));
        List<string>? partyIds = null;
        if (stage.Mode == "invasion")
        {
            partyIds = command.Value?.Split(',').Select(id => id.Trim()).ToList() ?? [];
            if (partyIds.Count is < 1 or > 10 || partyIds.Any(string.IsNullOrWhiteSpace)
                || partyIds.Distinct(StringComparer.Ordinal).Count() != partyIds.Count
                || partyIds.Any(id => profile.Adventurers.All(actor => actor.Id != id)))
                throw new InvalidOperationException(DisplayText.Get("app.error.invalidParty"));
            foreach (var actorId in partyIds) RequireNotDispatched(actorId);
        }
        var id = Guid.NewGuid().ToString("N");
        var attemptId = Guid.NewGuid().ToString("N");
        var units = BuildOwnedBattleSetups(content);
        var economy = PrepareMatchEconomy(command, id);
        battle = new BattleSimulation(new() { MatchId = id, AttemptId = attemptId, Seed = 42,
            Content = content, StageId = stage.Id, InitialResources = 1000 + economy.Contribution, DefenseHp = stage.DefenseHp, Units = units, PartyIds = partyIds });
        ApplyOwnedBattleModifiers(battle, content);
        session = new() { MatchId = id, AttemptId = attemptId, ContentHash = content.ComputeHash(), Current = battle.Capture(),
            StartingRanks = profile.Adventurers.ToDictionary(x => x.Id, x => x.Rank), Economy = economy };
        paused = false; speed = 1; accumulator = 0;
        return battle.IsInvasion ? DisplayText.Get("app.preparation.invasionIntro")
            : DisplayText.Get("app.preparation.defenseIntro");
    }

    private string Deploy(GameCommand command, bool reposition)
    {
        RequirePreparation();
        var actor = Actor(command.ActorId);
        if (battle!.IsInvasion && !battle.State.Setup.PartyIds!.Contains(actor.Id, StringComparer.Ordinal))
            throw new InvalidOperationException(DisplayText.Get("app.error.deployOutsideParty"));
        var unit = battle!.State.Units.Single(x => x.Id == actor.Id);
        var cost = DeploymentPrice(actor);
        if (!unit.Deployed && battle.State.Resources < cost) throw new InvalidOperationException(DisplayText.Get("app.preparation.insufficientDeploymentResources"));
        var previouslyDeployed = unit.Deployed;
        var success = previouslyDeployed || reposition ? battle.Reposition(actor.Id, command.X, command.Y) : battle.Deploy(actor.Id, command.X, command.Y, cost);
        if (!success) throw new InvalidOperationException(DisplayText.Get("app.error.invalidDeploymentCell"));
        return previouslyDeployed ? DisplayText.Format("app.reposition.completed", actor.Name) : DisplayText.Format("app.deployment.completed", actor.Name, cost);
    }

    private string StartWave()
    {
        RequirePreparation();
        if (session!.LevelChoices.Count != 0) throw new InvalidOperationException(DisplayText.Get("app.error.chooseLevel"));
        if (!battle!.State.Units.Any(x => !x.IsEnemy && x.Deployed && (!battle.IsInvasion || x.Hp > 0))) throw new InvalidOperationException(DisplayText.Get("app.error.deployLivingActor"));
        if (session.MatchStart is null) { session.MatchStart = battle.Capture(); session.MatchStartGrowthRng = profile.GrowthRng; }
        if (!battle.BeginWave()) throw new InvalidOperationException(DisplayText.Get("app.error.cannotStartWave"));
        session.Phase = "Battle";
        session.WaveStart = battle.Capture();
        session.WaveStartGrowthRng = profile.GrowthRng;
        accumulator = 0;
        return battle.IsInvasion ? DisplayText.Format("app.status.sectionStart", battle.State.Wave) : DisplayText.Format("app.status.waveStart", battle.State.Wave);
    }

    private string BuyLevel(GameCommand command)
    {
        RequirePreparation();
        if (battle!.IsInvasion && battle.State.Units.FirstOrDefault(x => x.Id == command.ActorId)?.Hp <= 0)
            throw new InvalidOperationException(DisplayText.Get("app.error.reviveBeforeLevelPurchase"));
        if (!battle!.PurchaseLevel(command.ActorId ?? "")) throw new InvalidOperationException(DisplayText.Get("app.error.invalidLevelPurchase"));
        PrepareLevelChoices();
        return DisplayText.Get("app.level.purchased");
    }

    private void PrepareLevelChoices()
    {
        if (session is null || battle is null || session.Phase is "Victory" or "Defeat") return;
        var random = Pcg32.Restore(profile.GrowthRng);
        foreach (var unit in battle.State.Units.Where(x => !x.IsEnemy && x.PendingLevelChoices > 0).OrderBy(x => x.SpawnSequence))
        {
            if (session.LevelChoices.ContainsKey(unit.Id)) continue;
            var pool = LevelKinds.ToList(); var choices = new List<string>();
            for (var i = 0; i < 3; i++) { var index = random.NextInt(pool.Count); choices.Add(pool[index]); pool.RemoveAt(index); }
            session.LevelChoices[unit.Id] = choices;
        }
        profile.GrowthRng = random.Capture();
    }

    private string ChooseLevel(GameCommand command)
    {
        RequirePreparation();
        var id = command.ActorId ?? session!.LevelChoices.Keys.FirstOrDefault() ?? "";
        if (!session!.LevelChoices.TryGetValue(id, out var choices) || !choices.Contains(command.TargetId ?? "")) throw new InvalidOperationException(DisplayText.Get("app.error.chooseOfferedLevel"));
        if (!battle!.GrantLevel(id, command.TargetId!)) throw new InvalidOperationException(DisplayText.Get("app.error.cannotApplyLevel"));
        session.LevelChoices.Remove(id); PrepareLevelChoices();
        return DisplayText.Get("app.level.confirmed");
    }

    private string Revive(GameCommand command)
    {
        RequirePreparation();
        if (!battle!.Revive(command.ActorId ?? "")) throw new InvalidOperationException(DisplayText.Get("app.error.cannotRevive"));
        if (battle.IsInvasion) session!.LevelChoices.Remove(command.ActorId ?? "");
        return DisplayText.Get("app.revive.completed");
    }
    private string SetTarget(GameCommand command) { if (battle is null || !battle.SetTarget(command.ActorId ?? "", command.Value ?? "")) throw new InvalidOperationException(DisplayText.Get("app.error.cannotSetTarget")); return DisplayText.Get("app.target.changed"); }
    private string TogglePause() { if (session?.Phase != "Battle") throw new InvalidOperationException(DisplayText.Get("app.error.pauseOnlyInBattle")); paused = !paused; return paused ? DisplayText.Get("app.status.paused") : DisplayText.Get("app.status.resumed"); }
    private string SetSpeed(GameCommand command) { if (command.Value is not ("1" or "2")) throw new InvalidOperationException(DisplayText.Get("app.error.invalidSpeed")); speed = int.Parse(command.Value); return DisplayText.Format("app.speed.changed", speed); }

    private string Retry()
    {
        if (session?.Phase != "Defeat" || session.MatchStart is null) throw new InvalidOperationException(DisplayText.Get("app.error.retryOnlyAfterDefeat"));
        var snapshot = Clone(session.MatchStart);
        var attempt = Guid.NewGuid().ToString("N");
        snapshot.Setup = snapshot.Setup with { AttemptId = attempt };
        session.AttemptId = attempt; session.Current = snapshot; session.WaveStart = null; session.Phase = "Preparation"; session.LevelChoices.Clear();
        session.WaveStartGrowthRng = null;
        if (session.MatchStartGrowthRng is { } growthRng) profile.GrowthRng = growthRng;
        battle = BattleSimulation.Restore(snapshot); paused = false; accumulator = 0;
        return DisplayText.Get("app.retry.prepared");
    }

    private void RequireGuild() { if (session is not null) throw new InvalidOperationException(DisplayText.Get("app.error.onlyInGuild")); }
    private void RequirePreparation() { if (session?.Phase != "Preparation" || battle is null) throw new InvalidOperationException(DisplayText.Get("app.error.onlyBetweenWaves")); }
    private AdventurerState Actor(string? id) => profile.Adventurers.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException(DisplayText.Get("app.error.chooseActor"));
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, DomainJson.Options);
    private static T Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, DomainJson.Options) ?? throw new InvalidDataException(DisplayText.Get("app.error.emptySavedDocument"));
    private static T Clone<T>(T value) => Deserialize<T>(Serialize(value));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string InvasionDefeatDescription(string? reason) => reason == "time-limit" ? DisplayText.Get("app.defeat.timeLimit") : DisplayText.Get("app.defeat.allDead");
    public void Dispose() => store.Dispose();
}

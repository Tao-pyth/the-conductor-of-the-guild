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
    private string status = "技術検証版 — 冒険者を編成して出撃してください。";
    private List<HistoryView> history = [];
    private static readonly string[] LevelKinds = ["vitality", "firepower", "rapid", "mobility", "reach", "defense"];
    public string SavePath => store.Path;

    public GameController(string savePath, string contentPath, Action<CommitStage>? saveFault = null)
    {
        content = ContentBundle.Load(contentPath);
        ValidateSupportedContent(content);
        store = new SaveStore(savePath, saveFault);
        try
        {
            Reload();
            if (store.ReadDocument("profile") is null)
            {
                profile = new PlayerProfile { GrowthRng = new Pcg32(20261024, 71).Capture(), GenerationRng = new Pcg32(20261024, 93).Capture() };
                foreach (var name in new[] { "アオイ", "レン", "ミナ", "カイ", "ユイ", "ハル" }) AddAdventurer(name);
                // Unowned comparison equipment makes the transfer event reviewable without granting currency.
                profile.Weapons.Add(CreateWeapon("guild"));
                Persist("initialize", "initialize-v1", "初期冒険者6名を支給しました。", []);
            }
        }
        catch { store.Dispose(); throw; }
    }

    private void Reload()
    {
        var profileJson = store.ReadDocument("profile");
        if (profileJson is not null) profile = Deserialize<PlayerProfile>(profileJson);
        if (profile.FormatVersion != 1) throw new NotSupportedException("未対応のプロフィール形式です。保存を変更せず終了します。");
        var sessionJson = store.ReadDocument("session");
        session = sessionJson is null or "null" ? null : Deserialize<MatchSession>(sessionJson);
        if (session is not null && session.FormatVersion != 1) throw new NotSupportedException("未対応のマッチ保存形式です。");
        if (session is not null && session.Current.Setup.Content.ComputeHash() != session.ContentHash)
            throw new InvalidDataException("マッチ開始時の定義と保存ハッシュが一致しません。保存データは変更していません。");
        battle = session is null ? null : BattleSimulation.Restore(session.Phase == "Battle" ? session.WaveStart ?? throw new InvalidDataException("Wave開始点がありません。") : session.Current);
        revision = store.Revision;
        history = [];
        long after = 0;
        while (true)
        {
            var page = store.ReadHistory(after, 200);
            history.AddRange(page.Select(x => new HistoryView(x.Sequence, x.Text)));
            if (page.Count < 200) break;
            after = page[^1].Sequence;
        }
        accumulator = 0;
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
            var pendingBefore = eventOperation ? profile.PendingEvents.OrderBy(x => x.Sequence).FirstOrDefault() : null;
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
            history.AddRange(page.Select(x => new HistoryView(x.Sequence, x.Text)));
            if (page.Count < 200) return;
            after = page[^1].Sequence;
        }
    }

    private string Dispatch(GameCommand command)
    {
        var selected = profile.PendingEvents.FirstOrDefault();
        if (selected?.Kind is not null && command.Type is not ("ChooseBlessing" or "DiscardBlessings" or "RerollBlessings" or "TransferItem" or "Fuse" or "ResolveRandom" or "DeclineRandom"))
            throw new InvalidOperationException("選択済みの成長イベントを完了してください。");
        return command.Type switch
        {
            "Rename" => Rename(command), "Recruit" => Recruit(), "ExpandRoster" => ExpandRoster(),
            "StartMatch" => StartMatch(), "Deploy" => Deploy(command, false), "Reposition" => Deploy(command, true),
            "StartWave" => StartWave(), "BuyLevel" => BuyLevel(command), "ChooseLevel" => ChooseLevel(command),
            "Revive" => Revive(command), "SetTarget" => SetTarget(command), "TogglePause" => TogglePause(),
            "SetSpeed" => SetSpeed(command), "Retry" => Retry(), "ReturnToGuild" => ReturnToGuild(),
            "BeginEvent" => BeginEvent(command), "ChooseBlessing" => ChooseBlessing(command),
            "DiscardBlessings" => DiscardBlessings(), "RerollBlessings" => RerollBlessings(),
            "TransferItem" => TransferItem(command), "EquipItem" => EquipItem(command),
            "Fuse" => Fuse(command), "ResolveRandom" => ResolveRandom(), "DeclineRandom" => DeclineRandom(), "BuyWeapon" => BuyWeapon(command),
            _ => throw new InvalidOperationException("未対応の操作です。")
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
            history.AddRange(store.ReadHistory(lastSequence, Math.Max(entries.Count, 1)).Select(x => new HistoryView(x.Sequence, x.Text)));
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
                var message = session.Phase == "Defeat" ? "防衛に失敗しました。再挑戦か帰還を選んでください。" : session.Phase == "Victory" ? "全Waveを防衛しました。帰還で報酬を確定します。" : $"Wave {battle.State.Wave} を突破しました。";
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
                        paused = true;
                        status = "保存できなかったためWave開始点に戻して一時停止しました: " + exception.Message;
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
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("名前を入力してください。");
        actor.Name = name;
        return $"名前を「{name}」に変更しました。";
    }

    private string Recruit()
    {
        RequireGuild();
        if (profile.Adventurers.Count >= profile.Capacity) throw new InvalidOperationException("所有枠が満員です。資源は消費していません。");
        if (profile.Resources < 100) throw new InvalidOperationException("募集には拠点資源100が必要です。");
        AddAdventurer("新しい冒険者");
        profile.Resources -= 100;
        return "資源100で冒険者を募集しました。最低限の武器を所有しています。";
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
        if (profile.Capacity >= 50) throw new InvalidOperationException("所有上限50名です。資源は消費していません。");
        var cost = 200 + 100 * profile.ExpansionCount;
        if (profile.Resources < cost) throw new InvalidOperationException($"拡張には資源{cost}が必要です。");
        profile.Resources -= cost; profile.ExpansionCount++; profile.Capacity += 5;
        return $"所有枠を{profile.Capacity}名に拡張しました。";
    }

    private string StartMatch()
    {
        RequireGuild();
        var stage = content.Stages[0];
        var id = Guid.NewGuid().ToString("N");
        var attemptId = Guid.NewGuid().ToString("N");
        var units = profile.Adventurers.Select(actor =>
        {
            var weapon = profile.Weapons.Single(x => x.Id == actor.WeaponId && x.OwnerId == actor.Id);
            return new BattleUnitSetup { Id = actor.Id, Name = actor.Name, Stats = EffectiveStats(actor), WeaponTemplateId = weapon.TemplateId,
                BasePower = weapon.BasePower, AttackIntervalMs = weapon.AttackIntervalMs, RangeCenti = weapon.RangeCenti,
                SkillIds = weapon.SkillIds.ToList(), CurrentHp = actor.Hp };
        }).ToList();
        battle = new BattleSimulation(new() { MatchId = id, AttemptId = attemptId, Seed = 42,
            Content = content, StageId = stage.Id, InitialResources = stage.InitialResources, DefenseHp = stage.DefenseHp, Units = units });
        foreach (var actor in profile.Adventurers)
        {
            var unit = battle.State.Units.Single(x => x.Id == actor.Id);
            unit.MaxHpMultiplier = Rational.One - new Rational(actor.WeaknessStacks * 5, 100);
            battle.RecalculateUnit(actor.Id);
            ApplyBattleModifiers(actor, unit, content, battle);
        }
        session = new() { MatchId = id, AttemptId = attemptId, ContentHash = content.ComputeHash(), Current = battle.Capture(),
            StartingRanks = profile.Adventurers.ToDictionary(x => x.Id, x => x.Rank) };
        paused = false; speed = 1; accumulator = 0;
        return "出撃準備です。冒険者を選び、配置マスをクリックしてください。";
    }

    private string Deploy(GameCommand command, bool reposition)
    {
        RequirePreparation();
        var actor = Actor(command.ActorId);
        var unit = battle!.State.Units.Single(x => x.Id == actor.Id);
        var cost = BigInteger.Min(250, 25 * (1 + actor.Rank / 10));
        if (!unit.Deployed && battle.State.Resources < cost) throw new InvalidOperationException("配置資源が不足しています。");
        var previouslyDeployed = unit.Deployed;
        var success = previouslyDeployed || reposition ? battle.Reposition(actor.Id, command.X, command.Y) : battle.Deploy(actor.Id, command.X, command.Y, cost);
        if (!success) throw new InvalidOperationException("このマスには配置できません。");
        return previouslyDeployed ? $"{actor.Name}を再配置しました。" : $"{actor.Name}を配置しました（技術検証用費用{cost}）。";
    }

    private string StartWave()
    {
        RequirePreparation();
        if (session!.LevelChoices.Count != 0) throw new InvalidOperationException("Lv強化を選んでください。");
        if (!battle!.State.Units.Any(x => !x.IsEnemy && x.Deployed)) throw new InvalidOperationException("1名以上を配置してください。");
        if (session.MatchStart is null) session.MatchStart = battle.Capture();
        if (!battle.BeginWave()) throw new InvalidOperationException("Waveを開始できません。");
        session.Phase = "Battle";
        session.WaveStart = battle.Capture();
        accumulator = 0;
        return $"Wave {battle.State.Wave} 開始。";
    }

    private string BuyLevel(GameCommand command)
    {
        RequirePreparation();
        if (!battle!.PurchaseLevel(command.ActorId ?? "")) throw new InvalidOperationException("経験値の一括購入条件を満たしていません。");
        PrepareLevelChoices();
        return "不足経験値を購入しました。Lv強化を選んでください。";
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
        if (!session!.LevelChoices.TryGetValue(id, out var choices) || !choices.Contains(command.TargetId ?? "")) throw new InvalidOperationException("提示中のLv強化を選んでください。");
        if (!battle!.GrantLevel(id, command.TargetId!)) throw new InvalidOperationException("Lv強化を適用できません。");
        session.LevelChoices.Remove(id); PrepareLevelChoices();
        return "Lv強化を確定しました。このマッチ終了時に消失します。";
    }

    private string Revive(GameCommand command) { RequirePreparation(); if (!battle!.Revive(command.ActorId ?? "")) throw new InvalidOperationException("復帰条件を満たしていません。"); return "冒険者が復帰しました。"; }
    private string SetTarget(GameCommand command) { if (battle is null || !battle.SetTarget(command.ActorId ?? "", command.Value ?? "")) throw new InvalidOperationException("標的設定を変更できません。"); return "標的プリセットを変更しました。"; }
    private string TogglePause() { if (session?.Phase != "Battle") throw new InvalidOperationException("戦闘中だけ一時停止できます。"); paused = !paused; return paused ? "一時停止中です。" : "戦闘を再開しました。"; }
    private string SetSpeed(GameCommand command) { if (command.Value is not ("1" or "2")) throw new InvalidOperationException("速度は1か2を指定してください。"); speed = int.Parse(command.Value); return $"戦闘速度を{speed}倍にしました。"; }

    private string Retry()
    {
        if (session?.Phase != "Defeat" || session.MatchStart is null) throw new InvalidOperationException("敗北後に再挑戦できます。");
        var snapshot = Clone(session.MatchStart);
        var attempt = Guid.NewGuid().ToString("N");
        snapshot.Setup = snapshot.Setup with { AttemptId = attempt };
        session.AttemptId = attempt; session.Current = snapshot; session.WaveStart = null; session.Phase = "Preparation"; session.LevelChoices.Clear();
        battle = BattleSimulation.Restore(snapshot); paused = false; accumulator = 0;
        return "同じ開始構成で新しい試行を準備しました。";
    }

    private void RequireGuild() { if (session is not null) throw new InvalidOperationException("拠点で操作してください。"); }
    private void RequirePreparation() { if (session?.Phase != "Preparation" || battle is null) throw new InvalidOperationException("Wave間の準備中だけ操作できます。"); }
    private AdventurerState Actor(string? id) => profile.Adventurers.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("冒険者を選んでください。");
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, DomainJson.Options);
    private static T Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, DomainJson.Options) ?? throw new InvalidDataException("保存内容が空です。");
    private static T Clone<T>(T value) => Deserialize<T>(Serialize(value));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public void Dispose() => store.Dispose();
}

using System.Numerics;
using System.Text.Json;
using Guild.Application;
using Guild.Domain.Battle;
using Guild.Domain.Numerics;
using Guild.Domain.Randomness;
using Guild.Domain.Serialization;
using Guild.Persistence;

namespace Guild.Domain.Tests;

public sealed class InvasionApplicationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "guild-invasion-app-" + Guid.NewGuid().ToString("N"));
    private string Save => Path.Combine(root, "save.sqlite3");
    private string Content => Path.Combine(AppContext.BaseDirectory, "tech-content.json");
    private const string StageId = "tech-first-invasion";

    public InvasionApplicationTests() => Directory.CreateDirectory(root);

    [Fact]
    public void PartyValidationPreviewAndMatchLockDoNotMutateOnInvalidRequests()
    {
        using var game = new GameController(Save, Content);
        var ids = game.View.Adventurers.Select(x => x.Id).ToArray();
        Assert.False(game.Execute(new("StartMatch", TargetId: StageId)).Success);
        Assert.False(game.Execute(new("StartMatch", TargetId: StageId, Value: ids[0] + "," + ids[0])).Success);
        Assert.False(game.Execute(new("StartMatch", TargetId: StageId, Value: "unknown")).Success);
        Assert.Equal("Guild", game.View.Screen);
        Assert.True(game.Execute(new("StartMatch", TargetId: StageId, Value: ids[0] + "," + ids[1])).Success);
        Assert.Equal("invasion", game.View.Mode);
        Assert.Equal(ids.Take(2), game.View.PartyIds);
        Assert.Equal(3, game.View.TotalWaves);
        Assert.Equal(300000, game.View.RemainingTimeMs);
        Assert.Equal(4, game.View.RemainingEnemies);
        Assert.Contains(game.View.Entities, x => x.Kind == "gate");
        Assert.Equal("300", game.View.ObjectiveHp);
        Assert.False(game.Execute(new("StartMatch")).Success);
        Assert.False(game.Execute(new("ReturnToGuild")).Success);
        Assert.False(game.Execute(new("Deploy", ActorId: ids[2], X: 1, Y: 3)).Success);
        Assert.Equal("1000", game.View.MatchResources);
        Assert.True(game.Execute(new("Deploy", ActorId: ids[0], X: 1, Y: 3)).Success);
        Assert.Equal("975", game.View.MatchResources);
        Assert.True(game.Execute(new("Reposition", ActorId: ids[0], X: 2, Y: 3)).Success);
        Assert.Equal("975", game.View.MatchResources);
        Assert.False(game.Execute(new("Deploy", ActorId: ids[1], X: 2, Y: 3)).Success);
        Assert.True(game.Execute(new("StartWave")).Success);
        Assert.False(game.Execute(new("Deploy", ActorId: ids[1], X: 1, Y: 4)).Success);
        Assert.False(game.Execute(new("Reposition", ActorId: ids[0], X: 2, Y: 4)).Success);
    }

    [Fact]
    public void FullSortieReturnsThreeHundredAndOneWeaponExactlyOnceAndSharesInitialBonus()
    {
        SeedProfile(profile =>
        {
            foreach (var actor in profile.Adventurers)
            {
                actor.Stats = new(100, 100, 100, 100, 100); actor.Hp = 1100;
                profile.Weapons.Single(w => w.Id == actor.WeaponId).BasePower = 500;
            }
            profile.FirstClear = true; profile.RerollTickets = 7; profile.PurificationDrops = 8;
            profile.Adventurers[2].NextMatchChanges["str"] = 2;
        });
        var before = ReadProfile();
        using var game = new GameController(Save, Content);
        var ids = game.View.Adventurers.Take(2).Select(x => x.Id).ToArray();
        Assert.True(game.Execute(new("StartMatch", TargetId: StageId, Value: string.Join(',', ids))).Success);
        Assert.True(game.Execute(new("Deploy", ActorId: ids[0], X: 2, Y: 4)).Success);
        Assert.True(game.Execute(new("Deploy", ActorId: ids[1], X: 2, Y: 6)).Success);
        CompleteSortie(game);
        Assert.Equal("Victory", game.View.Screen);
        Assert.Equal(3, game.View.Wave);
        var command = new GameCommand("ReturnToGuild", CommandId: "invasion-return-once");
        Assert.True(game.Execute(command).Success);
        Assert.True(game.Execute(command).Success);
        var after = ReadProfile();
        Assert.Equal(before.Resources + 300, after.Resources);
        Assert.Equal(before.Weapons.Count + 1, after.Weapons.Count);
        Assert.Equal(7, after.RerollTickets); Assert.Equal(8, after.PurificationDrops);
        foreach (var id in ids)
        {
            var actor = after.Adventurers.Single(x => x.Id == id);
            Assert.Equal(new BigInteger(2), actor.Rank); Assert.Equal(new BigInteger(20), actor.Experience);
        }
        Assert.Equal(BigInteger.One, after.Adventurers[2].Rank);
        Assert.Equal(2, after.Adventurers[2].NextMatchChanges["str"]);
        Assert.True(game.Execute(new("StartMatch")).Success);
        Assert.Equal("defense", game.View.Mode);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(2, 10, 100)]
    [InlineData(3, 20, 200)]
    public void DefeatUsesLastAttemptCompletedSectionsAndOnlyDeployedParticipants(int failedSection, int expectedXp, int expectedResources)
    {
        CreateInvasionPreparation();
        var old = ReadProfile();
        var ids = old.Adventurers.Take(2).Select(x => x.Id).ToArray();
        MutateSession(session =>
        {
            session.Phase = "Defeat"; session.Current.Wave = failedSection;
            session.Current.Defeated = true; session.Current.Running = false;
            var unit = session.Current.Units.Single(x => x.Id == ids[0]);
            unit.Deployed = unit.EverDeployed = true; unit.Hp = 0;
        });
        using var game = new GameController(Save, Content);
        Assert.True(game.Execute(new("ReturnToGuild")).Success);
        var after = ReadProfile();
        Assert.Equal(new BigInteger(expectedXp), after.Adventurers[0].Experience);
        Assert.Equal(new BigInteger(expectedResources), after.Resources - old.Resources);
        Assert.Equal(1, after.Adventurers[0].WeaknessStacks);
        Assert.Equal(BigInteger.One, after.Adventurers[0].Hp);
        Assert.Equal(BigInteger.Zero, after.Adventurers[1].Experience);
        Assert.False(after.FirstClear);
    }

    [Fact]
    public void RetryRestoresMatchGrowthRandomAndRevivalCounterInsteadOfPreviousAttempt()
    {
        string actor;
        PcgState expected;
        using (var game = new GameController(Save, Content))
        {
            actor = game.View.Adventurers[0].Id;
            Assert.True(game.Execute(new("StartMatch", TargetId: StageId, Value: actor)).Success);
            Assert.True(game.Execute(new("Deploy", ActorId: actor, X: 1, Y: 3)).Success);
            Assert.True(game.Execute(new("StartWave")).Success);
            expected = ReadSession().MatchStartGrowthRng!;
        }
        MutateSession(session =>
        {
            session.Phase = "Defeat"; session.Current.Wave = 3; session.Current.Running = false; session.Current.Defeated = true;
            var unit = session.Current.Units.Single(x => x.Id == actor); unit.RevivalCount = 2; unit.Hp = 0;
        });
        MutateProfile(profile => profile.GrowthRng = new Pcg32(98765, 71).Capture());
        using (var game = new GameController(Save, Content))
        {
            var previousAttempt = ReadSession().AttemptId;
            Assert.True(game.Execute(new("Retry")).Success);
            Assert.Equal("Preparation", game.View.Screen);
            Assert.NotEqual(previousAttempt, ReadSession().AttemptId);
            Assert.Equal(expected, ReadProfile().GrowthRng);
            Assert.Equal(0, ReadSession().Current.Units.Single(x => x.Id == actor).RevivalCount);
        }
        MutateSession(session => session.Current.Units.Single(x => x.Id == actor).Hp = 0);
        using var restarted = new GameController(Save, Content);
        var resources = BigInteger.Parse(restarted.View.MatchResources);
        Assert.True(restarted.Execute(new("Revive", ActorId: actor)).Success);
        Assert.Equal(resources - 25, BigInteger.Parse(restarted.View.MatchResources));
        Assert.Equal(1, ReadSession().Current.Units.Single(x => x.Id == actor).RevivalCount);
    }

    [Fact]
    public void ReviveDiscardsPendingChoicesAndCannotBuyLevelWhileIncapacitated()
    {
        CreateInvasionPreparation();
        var actor = ReadProfile().Adventurers[0].Id;
        MutateSession(session =>
        {
            var unit = session.Current.Units.Single(x => x.Id == actor);
            unit.Deployed = unit.EverDeployed = true; unit.Hp = 0; unit.Level = 2;
            unit.PendingLevelChoices = 1; unit.LevelExperience = 99;
            session.LevelChoices[actor] = ["vitality", "firepower", "reach"];
        });
        using var game = new GameController(Save, Content);
        Assert.Equal("LevelChoice", game.View.Screen);
        Assert.False(game.Execute(new("BuyLevel", ActorId: actor)).Success);
        Assert.True(game.Execute(new("Revive", ActorId: actor)).Success);
        Assert.Equal("Preparation", game.View.Screen);
        Assert.Empty(ReadSession().LevelChoices);
        var unitAfter = ReadSession().Current.Units.Single(x => x.Id == actor);
        Assert.Equal(2, unitAfter.Level); Assert.Equal(BigInteger.Zero, unitAfter.LevelExperience);
        Assert.Equal(0, unitAfter.PendingLevelChoices);
        Assert.True(unitAfter.Hp > 0);
    }

    [Fact]
    public void BattleRestartRestoresSectionClockPositionAndGrowthRandomCheckpoint()
    {
        PcgState expected;
        using (var game = new GameController(Save, Content))
        {
            var actor = game.View.Adventurers[0].Id;
            Assert.True(game.Execute(new("StartMatch", TargetId: StageId, Value: actor)).Success);
            Assert.True(game.Execute(new("Deploy", ActorId: actor, X: 1, Y: 3)).Success);
            Assert.True(game.Execute(new("StartWave")).Success);
            expected = ReadSession().WaveStartGrowthRng!;
            game.Advance(.2);
            Assert.True(game.View.RemainingTimeMs < 300000);
        }
        MutateProfile(profile => profile.GrowthRng = new Pcg32(34567, 71).Capture());
        using var restored = new GameController(Save, Content);
        Assert.Equal("Battle", restored.View.Screen);
        Assert.Equal(300000, restored.View.RemainingTimeMs);
        Assert.True(restored.Execute(new("TogglePause")).Success);
        Assert.Equal(expected, ReadProfile().GrowthRng);
        Assert.Equal(ReadSession().WaveStart!.TimeMs, ReadSession().Current.TimeMs);
    }

    [Fact]
    public void LegacyProfileAndActiveDefenseMigrateTogetherWithBackupAndStableContentHash()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "v0.2.0");
        var profileJson = File.ReadAllText(Path.Combine(fixture, "battle-profile.json"));
        var sessionJson = File.ReadAllText(Path.Combine(fixture, "battle-session.json"));
        WriteDocuments(profileJson, sessionJson);
        var beforeProfile = JsonSerializer.Deserialize<PlayerProfile>(profileJson, DomainJson.Options)!;
        var beforeSession = JsonSerializer.Deserialize<MatchSession>(sessionJson, DomainJson.Options)!;
        string backup;
        using (var game = new GameController(Save, Content))
        {
            backup = Assert.IsType<string>(game.MigrationBackupPath);
            Assert.True(File.Exists(backup));
            Assert.Equal("defense", game.View.Mode); Assert.Equal("Battle", game.View.Screen);
            Assert.Equal(2, ReadProfile().FormatVersion); Assert.Equal(2, ReadSession().FormatVersion);
            Assert.Equal(beforeSession.ContentHash, ReadSession().ContentHash);
            Assert.Equal(Json(beforeProfile.Weapons), Json(ReadProfile().Weapons));
            Assert.Equal(Json(beforeProfile.Adventurers), Json(ReadProfile().Adventurers));
        }
        using (var store = new SaveStore(backup))
            Assert.Equal(1, JsonSerializer.Deserialize<PlayerProfile>(store.ReadDocument("profile")!, DomainJson.Options)!.FormatVersion);
        using var reopened = new GameController(Save, Content);
        Assert.Null(reopened.MigrationBackupPath);
        Assert.Single(Directory.GetFiles(root, "*.before-documents-v2-*.backup"));
    }

    [Theory]
    [InlineData(CommitStage.AfterDocuments)]
    [InlineData(CommitStage.BeforeCommit)]
    public void FailedLogicalMigrationRollsBackBothDocumentsAndLeavesReadableBackup(CommitStage failure)
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "v0.2.0");
        var profileJson = File.ReadAllText(Path.Combine(fixture, "battle-profile.json"));
        var sessionJson = File.ReadAllText(Path.Combine(fixture, "battle-session.json"));
        WriteDocuments(profileJson, sessionJson);
        Assert.Throws<IOException>(() => new GameController(Save, Content, stage => { if (stage == failure) throw new IOException("migration fault"); }));
        using var store = new SaveStore(Save);
        Assert.Equal(profileJson, store.ReadDocument("profile")); Assert.Equal(sessionJson, store.ReadDocument("session"));
        Assert.Equal("ok", store.IntegrityCheck());
        Assert.Single(Directory.GetFiles(root, "*.before-documents-v2-*.backup"));
    }

    [Fact]
    public void LogicalMigrationRecoversLostAcknowledgementWithoutRepeatingIt()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "v0.2.0");
        WriteDocuments(File.ReadAllText(Path.Combine(fixture, "profile-profile.json")), "null");
        using (var game = new GameController(Save, Content, stage => { if (stage == CommitStage.AfterCommit) throw new IOException("lost migration ack"); }))
            Assert.Equal("Guild", game.View.Screen);
        Assert.Equal(2, ReadProfile().FormatVersion);
        using var reopened = new GameController(Save, Content);
        Assert.Single(Directory.GetFiles(root, "*.before-documents-v2-*.backup"));
    }

    [Fact]
    public void FutureDocumentIsRejectedBeforeAnyDatabaseOrBackupMutation()
    {
        using (var game = new GameController(Save, Content)) { }
        MutateProfile(profile => profile.FormatVersion = 99);
        var before = File.ReadAllBytes(Save);
        Assert.Throws<NotSupportedException>(() => new GameController(Save, Content));
        Assert.Equal(before, File.ReadAllBytes(Save));
        Assert.Empty(Directory.GetFiles(root, "*.backup"));
    }

    [Fact]
    public void CorruptLegacyHashIsRejectedBeforeMigration()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "v0.2.0");
        var profileJson = File.ReadAllText(Path.Combine(fixture, "battle-profile.json"));
        var session = JsonSerializer.Deserialize<MatchSession>(File.ReadAllText(Path.Combine(fixture, "battle-session.json")), DomainJson.Options)!;
        session.ContentHash = "corrupt";
        WriteDocuments(profileJson, Json(session));
        var before = File.ReadAllBytes(Save);
        Assert.Throws<InvalidDataException>(() => new GameController(Save, Content));
        Assert.Equal(before, File.ReadAllBytes(Save));
        Assert.Empty(Directory.GetFiles(root, "*.backup"));
    }

    [Theory]
    [InlineData("ownership")]
    [InlineData("duplicate-id")]
    [InlineData("missing-rank")]
    [InlineData("missing-combatant")]
    [InlineData("future-session")]
    [InlineData("future-rules")]
    public void UnsupportedOrBrokenOwnershipAndMatchDataIsRejectedBeforeMigration(string corruption)
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "v0.2.0");
        var profile = JsonSerializer.Deserialize<PlayerProfile>(File.ReadAllText(Path.Combine(fixture, "battle-profile.json")), DomainJson.Options)!;
        var session = JsonSerializer.Deserialize<MatchSession>(File.ReadAllText(Path.Combine(fixture, "battle-session.json")), DomainJson.Options)!;
        switch (corruption)
        {
            case "ownership": profile.Weapons.Single(w => w.Id == profile.Adventurers[0].WeaponId).OwnerId = "missing-adventurer"; break;
            case "duplicate-id": profile.Adventurers[1].Id = profile.Adventurers[0].Id; break;
            case "missing-rank": session.StartingRanks.Remove(profile.Adventurers[0].Id); break;
            case "missing-combatant": session.Current.Units.RemoveAll(unit => unit.Id == profile.Adventurers[0].Id); break;
            case "future-session": session.FormatVersion = 99; break;
            case "future-rules": session.Current.RuleSetId = "future-defense-v999"; break;
        }
        WriteDocuments(Json(profile), Json(session));
        var before = File.ReadAllBytes(Save);
        if (corruption is "future-session" or "future-rules")
            Assert.Throws<NotSupportedException>(() => new GameController(Save, Content));
        else Assert.Throws<InvalidDataException>(() => new GameController(Save, Content));
        Assert.Equal(before, File.ReadAllBytes(Save));
        Assert.Empty(Directory.GetFiles(root, "*.backup"));
    }

    private void CreateInvasionPreparation()
    {
        using var game = new GameController(Save, Content);
        Assert.True(game.Execute(new("StartMatch", TargetId: StageId, Value: string.Join(',', game.View.Adventurers.Take(2).Select(x => x.Id)))).Success);
    }

    private static void CompleteSortie(GameController game)
    {
        for (var i = 0; i < 10000 && game.View.Screen is not ("Victory" or "Defeat"); i++)
        {
            if (game.View.Screen == "Preparation") Assert.True(game.Execute(new("StartWave")).Success);
            else if (game.View.Screen == "LevelChoice") Assert.True(game.Execute(new("ChooseLevel", ActorId: game.View.EventActorId, TargetId: game.View.Choices[0].Id)).Success);
            else game.Advance(.2);
        }
    }

    private void SeedProfile(Action<PlayerProfile> mutate)
    {
        using (var game = new GameController(Save, Content)) { }
        MutateProfile(mutate);
    }
    private PlayerProfile ReadProfile() { using var store = new SaveStore(Save); return JsonSerializer.Deserialize<PlayerProfile>(store.ReadDocument("profile")!, DomainJson.Options)!; }
    private MatchSession ReadSession() { using var store = new SaveStore(Save); return JsonSerializer.Deserialize<MatchSession>(store.ReadDocument("session")!, DomainJson.Options)!; }
    private void MutateProfile(Action<PlayerProfile> mutate) { var profile = ReadProfile(); mutate(profile); using var store = new SaveStore(Save); Commit(store, new() { ["profile"] = Json(profile) }); }
    private void MutateSession(Action<MatchSession> mutate) { var session = ReadSession(); mutate(session); using var store = new SaveStore(Save); Commit(store, new() { ["session"] = Json(session) }); }
    private void WriteDocuments(string profile, string session) { using var store = new SaveStore(Save); Commit(store, new() { ["profile"] = profile, ["session"] = session }); }
    private static void Commit(SaveStore store, Dictionary<string, string> documents) => store.Commit(new(store.Revision, Guid.NewGuid().ToString("N"), "fixture", "fixture", documents, []));
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, DomainJson.Options);
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
}

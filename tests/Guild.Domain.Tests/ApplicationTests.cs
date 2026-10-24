using System.Numerics;
using System.Text.Json;
using Guild.Application;
using Guild.Domain.Definitions;
using Guild.Domain.Numerics;
using Guild.Domain.Serialization;
using Guild.Persistence;

namespace Guild.Domain.Tests;

public sealed class ApplicationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "guild-app-" + Guid.NewGuid().ToString("N"));
    private string Save => Path.Combine(root, "save.sqlite3");
    private string Content => Path.Combine(AppContext.BaseDirectory, "tech-content.json");
    public ApplicationTests() => Directory.CreateDirectory(root);

    [Fact]
    public void InitialGrantAndRenameSurviveRestartWithoutReroll()
    {
        string actorId; string weaponId;
        using (var game = new GameController(Save, Content))
        {
            Assert.Equal(6, game.View.Adventurers.Count);
            Assert.Equal("0", game.View.GuildResources);
            actorId = game.View.Adventurers[0].Id;
            weaponId = game.View.Items.First(x => x.OwnerId == actorId).Id;
            Assert.True(game.Execute(new("Rename", ActorId: actorId, Value: "日本語の冒険者")).Success);
        }
        using var restored = new GameController(Save, Content);
        Assert.Equal(6, restored.View.Adventurers.Count);
        Assert.Equal("日本語の冒険者", restored.View.Adventurers.Single(x => x.Id == actorId).Name);
        Assert.Contains(restored.View.Items, x => x.Id == weaponId && x.OwnerId == actorId);
    }

    [Fact]
    public void FailedRecruitDoesNotConsumeOrCreateAnything()
    {
        using var game = new GameController(Save, Content);
        var initial = ReadProfile();
        Assert.False(game.Execute(new("Recruit")).Success);
        Assert.Equal(Json(initial), Json(ReadProfile()));
    }

    [Fact]
    public void SameCommandDoesNotChargeOrRecruitTwice()
    {
        SeedProfile(profile => profile.Resources = 500);
        using var game = new GameController(Save, Content);
        var command = new GameCommand("Recruit", CommandId: "same-recruit");
        Assert.True(game.Execute(command).Success);
        Assert.True(game.Execute(command).Success);
        Assert.Equal(7, game.View.Adventurers.Count);
        Assert.Equal("400", game.View.GuildResources);
        Assert.False(game.Execute(command with { Type = "ExpandRoster" }).Success);
    }

    [Fact]
    public void LostCommitAcknowledgementKeepsCommittedStateAndAllowsNextCommand()
    {
        SeedProfile(profile => profile.Resources = 500);
        var interrupt = false;
        using var game = new GameController(Save, Content, stage =>
        {
            if (interrupt && stage == CommitStage.AfterCommit) { interrupt = false; throw new IOException("acknowledgement lost"); }
        });
        interrupt = true;
        var command = new GameCommand("Recruit", CommandId: "ack-lost");
        Assert.True(game.Execute(command).Success);
        Assert.Equal("400", game.View.GuildResources);
        Assert.True(game.Execute(command).Success);
        Assert.Equal(7, game.View.Adventurers.Count);
        Assert.True(game.Execute(new("Recruit")).Success);
        Assert.Equal("300", game.View.GuildResources);
        Assert.Equal(8, game.View.Adventurers.Count);
    }

    [Fact]
    public void DeploymentChargesOnceAndRepositionIsFree()
    {
        using var game = new GameController(Save, Content);
        Assert.True(game.Execute(new("StartMatch")).Success);
        var actor = game.View.Adventurers[0].Id;
        Assert.True(game.Execute(new("Deploy", ActorId: actor, X: 4, Y: 4)).Success);
        Assert.Equal("975", game.View.MatchResources);
        Assert.True(game.Execute(new("Reposition", ActorId: actor, X: 5, Y: 4)).Success);
        Assert.Equal("975", game.View.MatchResources);
    }

    [Fact]
    public void GrowthHistoryUsesResolvedActorAndEventRankBlock()
    {
        SeedProfile(profile => profile.PendingEvents.Add(new() { Id = "rank-24", Sequence = 1, ActorId = profile.Adventurers[0].Id, Rank = 24 }));
        using var game = new GameController(Save, Content);
        var actor = game.View.Adventurers[0].Id;
        Assert.True(game.Execute(new("BeginEvent", Value: "blessing", CommandId: "begin-24")).Success);
        Assert.True(game.Execute(new("ChooseBlessing", TargetId: game.View.Choices[0].Id, CommandId: "choose-24")).Success);
        using var store = new SaveStore(Save);
        Assert.All(store.ReadHistory().Where(x => x.EventId is "begin-24" or "choose-24"), entry => Assert.Equal(actor, entry.ActorId));
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Save + ";Pooling=False");
        connection.Open(); using var query = connection.CreateCommand(); query.CommandText = "SELECT rank_block FROM history WHERE event_id='choose-24';";
        Assert.Equal("2", query.ExecuteScalar());
    }

    [Fact]
    public void RandomBlessingWaitsForExplicitChoiceAndDoesNotChangeOrdinaryBonus()
    {
        SeedProfile(profile =>
        {
            var actor = profile.Adventurers[0]; actor.DiscardBonus = 3;
            profile.PendingEvents.Add(new() { Id = "random-choice", Sequence = 1, ActorId = actor.Id, Rank = 2, Kind = "random",
                DefinitionJson = ContentBundle.Load(Content).ToJson(), FixedRandomResult = "{\"kind\":\"blessing\",\"id\":\"tech-strength\",\"stat\":null,\"delta\":0}" });
        });
        using var game = new GameController(Save, Content);
        Assert.True(game.Execute(new("ResolveRandom")).Success);
        Assert.Single(game.View.Choices);
        Assert.Equal(1, game.View.PendingEvents);
        Assert.Empty(ReadProfile().Adventurers[0].Blessings);
        Assert.True(game.Execute(new("ChooseBlessing", TargetId: "tech-strength")).Success);
        Assert.Equal(3, ReadProfile().Adventurers[0].DiscardBonus);
        Assert.Equal(0, game.View.PendingEvents);
    }

    [Fact]
    public void BattleRestartRestoresCurrentWaveNotCurrentInstant()
    {
        using (var game = new GameController(Save, Content))
        {
            Assert.True(game.Execute(new("StartMatch")).Success);
            DeployAll(game);
            Assert.True(game.Execute(new("StartWave")).Success);
            game.Advance(.1);
            Assert.Contains(game.View.Entities, x => x.Kind == "enemy" && x.X > 0);
        }
        using var restored = new GameController(Save, Content);
        Assert.Equal("Battle", restored.View.Screen);
        var session = ReadSession();
        Assert.Equal(session.WaveStart!.Setup.AttemptId, session.AttemptId);
        Assert.Equal(session.WaveStart.Wave, restored.View.Wave);
        Assert.All(restored.View.Entities.Where(x => x.Kind == "enemy"), entity => Assert.Equal(0, entity.X));
    }

    [Fact]
    public void ActiveMatchKeepsDefinitionPayloadAndNextMatchUsesNewContent()
    {
        using (var game = new GameController(Save, Content)) Assert.True(game.Execute(new("StartMatch")).Success);
        var original = ContentBundle.Load(Content);
        var updated = original with { Version = "tech-updated" };
        var updatedFile = Path.Combine(root, "updated.json"); File.WriteAllText(updatedFile, updated.ToJson());
        using var restored = new GameController(Save, updatedFile);
        Assert.Equal(original.Version, ReadSession().Current.Setup.Content.Version);
        Assert.Equal(original.ComputeHash(), ReadSession().ContentHash);
        DeployAll(restored);
        CompleteBattle(restored);
        Assert.True(restored.Execute(new("ReturnToGuild")).Success);
        Assert.True(restored.Execute(new("StartMatch")).Success);
        Assert.Equal(updated.Version, ReadSession().Current.Setup.Content.Version);
    }

    [Fact]
    public void WaveCommitFailureRestoresGrowthRandomStateBeforeRetry()
    {
        var interrupt = false;
        using var game = new GameController(Save, Content, stage =>
        {
            if (interrupt && stage == CommitStage.BeforeCommit) throw new IOException("wave save failed");
        });
        Assert.True(game.Execute(new("StartMatch")).Success); DeployAll(game);
        for (var wave = 0; wave < 2; wave++)
        {
            Assert.True(game.Execute(new("StartWave")).Success);
            for (var i = 0; game.View.Screen == "Battle" && i < 1000; i++) game.Advance(.2);
            Assert.Equal("Preparation", game.View.Screen);
        }
        Assert.True(game.Execute(new("StartWave")).Success);
        var expected = ReadProfile().GrowthRng;
        interrupt = true;
        for (var i = 0; !game.View.Paused && i < 1000; i++) game.Advance(.2);
        Assert.True(game.View.Paused);
        interrupt = false;
        Assert.True(game.Execute(new("TogglePause")).Success);
        Assert.Equal(expected, ReadProfile().GrowthRng);
    }

    [Fact]
    public void FrameCadenceAndDoubleSpeedProduceTheSameBattleState()
    {
        using (var initial = new GameController(Save, Content)) { }
        var otherSave = Path.Combine(root, "other.sqlite3");
        using (var source = new SaveStore(Save))
        using (var destination = new SaveStore(otherSave))
            destination.Commit(new(destination.Revision, "copy-fixture", "copy", "fixture",
                new Dictionary<string, string> { ["profile"] = source.ReadDocument("profile")! }, []));
        using var normal = new GameController(Save, Content);
        using var fast = new GameController(otherSave, Content);
        foreach (var game in new[] { normal, fast })
        {
            Assert.True(game.Execute(new("StartMatch")).Success); DeployAll(game);
            Assert.True(game.Execute(new("StartWave")).Success);
        }
        Assert.True(fast.Execute(new("SetSpeed", Value: "2")).Success);
        for (var frame = 0; frame < 600; frame++) normal.Advance(1.0 / 120);
        for (var frame = 0; frame < 75; frame++) fast.Advance(1.0 / 30);
        Assert.Equal(normal.View.Wave, fast.View.Wave);
        Assert.Equal(normal.View.DefenseHp, fast.View.DefenseHp);
        Assert.Equal(normal.View.MatchResources, fast.View.MatchResources);
        Assert.Equal(Json(normal.View.Entities), Json(fast.View.Entities));
        Assert.Equal(Json(normal.View.Adventurers), Json(fast.View.Adventurers));
    }

    [Fact]
    public void RankCandidatesAndSelectedPhaseAreFixedAcrossRestart()
    {
        SeedProfile(profile => profile.PendingEvents.Add(new() { Id = "growth-fixed", Sequence = 1, ActorId = profile.Adventurers[0].Id, Rank = 2 }));
        string[] choices;
        using (var game = new GameController(Save, Content))
        {
            Assert.True(game.Execute(new("BeginEvent", Value: "blessing")).Success);
            choices = game.View.Choices.Select(x => x.Id).ToArray();
            Assert.NotEmpty(choices);
            Assert.False(game.Execute(new("StartMatch")).Success);
        }
        using var restored = new GameController(Save, Content);
        Assert.Equal("RankEvent", restored.View.Screen);
        Assert.Equal(choices, restored.View.Choices.Select(x => x.Id));
        Assert.True(restored.Execute(new("ChooseBlessing", TargetId: choices[0])).Success);
        Assert.Equal(0, restored.View.PendingEvents);
        Assert.Single(ReadProfile().Adventurers[0].Blessings);
    }

    [Fact]
    public void CandidateZeroKeepsRankEventAndDiscardBonus()
    {
        SeedProfile(profile =>
        {
            var actor = profile.Adventurers[0]; actor.DiscardBonus = 2;
            actor.Blessings = ContentBundle.Load(Content).Blessings.Where(x => x.Acquisition == "normal").Select(x => new OwnedBlessing { Id = x.Id, Rank = 3 }).ToList();
            profile.PendingEvents.Add(new() { Id = "no-candidates", ActorId = actor.Id, Rank = 2, Sequence = 1 });
        });
        using var game = new GameController(Save, Content);
        Assert.False(game.Execute(new("BeginEvent", Value: "blessing")).Success);
        var profile = ReadProfile();
        Assert.Single(profile.PendingEvents);
        Assert.Null(profile.PendingEvents[0].Kind);
        Assert.Equal(2, profile.Adventurers[0].DiscardBonus);
    }

    [Fact]
    public void FusionPersistsExactParentComponentsAndConsumesOneEvent()
    {
        SeedProfile(profile =>
        {
            var actor = profile.Adventurers[0];
            actor.Blessings = [new() { Id = "tech-strength", Rank = 3 }, new() { Id = "tech-vitality", Rank = 3 }];
            profile.PendingEvents.Add(new() { Id = "fusion-once", ActorId = actor.Id, Rank = 2, Sequence = 1 });
        });
        using var game = new GameController(Save, Content);
        Assert.True(game.Execute(new("BeginEvent", Value: "fusion")).Success);
        Assert.DoesNotContain("双", game.View.Choices[0].Description);
        Assert.True(game.Execute(new("Fuse", TargetId: "tech-fusion", CommandId: "fuse-once")).Success);
        Assert.True(game.Execute(new("Fuse", TargetId: "tech-fusion", CommandId: "fuse-once")).Success);
        var profile = ReadProfile();
        Assert.Empty(profile.PendingEvents);
        var fusion = Assert.Single(profile.Adventurers[0].Blessings);
        Assert.Equal("tech-fused", fusion.Id);
        Assert.Equal(1, fusion.Rank);
        Assert.NotEmpty(fusion.InheritedModifiers!);
        Assert.Contains("tech-fusion", profile.DiscoveredRecipes);
    }

    [Fact]
    public void TenWaveCycleCanReturnGrowRecruitAndDepartAgain()
    {
        using var game = new GameController(Save, Content);
        Assert.True(game.Execute(new("StartMatch")).Success); DeployAll(game);
        var iterations = 0;
        while (game.View.Screen is not ("Victory" or "Defeat") && iterations++ < 10000)
        {
            if (game.View.Screen == "LevelChoice")
                Assert.True(game.Execute(new("ChooseLevel", ActorId: game.View.EventActorId, TargetId: game.View.Choices[0].Id)).Success);
            else if (game.View.Screen == "Preparation") Assert.True(game.Execute(new("StartWave")).Success);
            else game.Advance(.2);
        }
        Assert.True(iterations < 10000, "The battle failed to reach a result.");
        Assert.Equal("Victory", game.View.Screen);
        Assert.Equal(10, game.View.Wave);
        Assert.True(game.Execute(new("ReturnToGuild", CommandId: "settle-once")).Success);
        Assert.True(game.Execute(new("ReturnToGuild", CommandId: "settle-once")).Success);
        Assert.Equal("1000", game.View.GuildResources);
        Assert.True(game.View.PendingEvents > 0);
        while (game.View.PendingEvents > 0)
        {
            Assert.True(game.Execute(new("BeginEvent", Value: "blessing")).Success);
            Assert.True(game.Execute(new("ChooseBlessing", TargetId: game.View.Choices[0].Id)).Success);
        }
        Assert.True(game.Execute(new("Recruit")).Success);
        Assert.Equal(7, game.View.Adventurers.Count);
        Assert.True(game.Execute(new("StartMatch")).Success);
        Assert.Equal("Preparation", game.View.Screen);
        Assert.NotEmpty(game.View.History);
    }

    private static void DeployAll(GameController game)
    {
        var positions = new[] { (4, 4), (6, 4), (8, 4), (4, 6), (6, 6), (8, 6) };
        foreach (var pair in game.View.Adventurers.Select((actor, i) => (actor, i)))
            Assert.True(game.Execute(new("Deploy", ActorId: pair.actor.Id, X: positions[pair.i].Item1, Y: positions[pair.i].Item2)).Success);
    }

    private static void CompleteBattle(GameController game)
    {
        var iterations = 0;
        while (game.View.Screen is not ("Victory" or "Defeat") && iterations++ < 10000)
        {
            if (game.View.Screen == "LevelChoice") Assert.True(game.Execute(new("ChooseLevel", ActorId: game.View.EventActorId, TargetId: game.View.Choices[0].Id)).Success);
            else if (game.View.Screen == "Preparation") Assert.True(game.Execute(new("StartWave")).Success);
            else game.Advance(.2);
        }
        Assert.Equal("Victory", game.View.Screen);
    }

    private void SeedProfile(Action<PlayerProfile> mutate)
    {
        using (var game = new GameController(Save, Content)) { }
        using var store = new SaveStore(Save);
        var profile = JsonSerializer.Deserialize<PlayerProfile>(store.ReadDocument("profile")!, DomainJson.Options)!;
        mutate(profile);
        store.Commit(new(store.Revision, "seed-" + Guid.NewGuid().ToString("N"), "seed", "test fixture", new Dictionary<string, string> { ["profile"] = Json(profile) }, []));
    }
    private PlayerProfile ReadProfile() { using var store = new SaveStore(Save); return JsonSerializer.Deserialize<PlayerProfile>(store.ReadDocument("profile")!, DomainJson.Options)!; }
    private MatchSession ReadSession() { using var store = new SaveStore(Save); return JsonSerializer.Deserialize<MatchSession>(store.ReadDocument("session")!, DomainJson.Options)!; }
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, DomainJson.Options);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}

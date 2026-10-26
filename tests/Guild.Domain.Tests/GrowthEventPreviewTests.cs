using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Guild.Application;
using Guild.Domain.Definitions;
using Guild.Domain.Serialization;
using Guild.Persistence;

namespace Guild.Domain.Tests;

public sealed class GrowthEventPreviewTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "guild-growth-preview-" + Guid.NewGuid().ToString("N"));
    private string Save => Path.Combine(root, "save.sqlite3");
    private string Content => Path.Combine(AppContext.BaseDirectory, "tech-content.json");
    public GrowthEventPreviewTests() => Directory.CreateDirectory(root);

    [Fact]
    public void PreviewUsesSequenceRecipientAndHistoricalEventRankBeforeKindSelection()
    {
        Seed(profile =>
        {
            profile.Adventurers[0].Rank = 4;
            profile.Adventurers[0].Blessings.Add(new() { Id = "tech-strength", Rank = 1 });
            profile.PendingEvents.Add(new() { Id = "later", Sequence = 20, ActorId = profile.Adventurers[1].Id, Rank = 3 });
            profile.PendingEvents.Add(new() { Id = "first", Sequence = 10, ActorId = profile.Adventurers[0].Id, Rank = 2 });
        });
        var profile = Profile();
        using var game = new GameController(Save, Content);
        var preview = Assert.IsType<GrowthEventPreviewView>(game.View.NextGrowthEvent);
        Assert.Equal("Guild", game.View.Screen);
        Assert.Equal("first", preview.EventId); Assert.Equal(10, preview.Sequence);
        Assert.Equal(profile.Adventurers[0].Id, preview.ActorId);
        Assert.Equal(profile.Adventurers[0].Name, preview.ActorName);
        Assert.Equal("2", preview.EventRank); Assert.Equal("4", preview.CurrentRank);
        Assert.Equal(2, preview.PendingCount); Assert.Null(preview.Kind);
        Assert.Equal(profile.Weapons.Single(w => w.Id == profile.Adventurers[0].WeaponId).Name, preview.WeaponName);
        Assert.Contains(preview.Blessings, text => text.Contains("tech-strength", StringComparison.Ordinal));
        Assert.Equal(new[] { "blessing", "transfer", "fusion", "random" }, preview.Options.Select(option => option.Kind));
        var transfer = new GameCommand("BeginEvent", ActorId: preview.ActorId, TargetId: preview.EventId, Value: "transfer", CommandId: "begin-first");
        Assert.True(game.Execute(transfer).Success);
        Assert.Equal("first", game.View.NextGrowthEvent!.EventId);
        Assert.Equal(preview.ActorId, game.View.EventActorId);
        Assert.All(game.View.NextGrowthEvent.Options, option => Assert.False(option.CanBegin));
        var item = profile.Weapons.First(w => w.OwnerId == "guild");
        Assert.True(game.Execute(new("TransferItem", ActorId: preview.ActorId, TargetId: item.Id, CommandId: "transfer-first")).Success);
        Assert.Equal("later", game.View.NextGrowthEvent!.EventId);
        Assert.Equal(preview.ActorId, Profile().Weapons.Single(w => w.Id == item.Id).OwnerId);
        using var store = new SaveStore(Save);
        Assert.All(store.ReadHistory().Where(entry => entry.EventId is "begin-first" or "transfer-first"), entry => Assert.Equal(preview.ActorId, entry.ActorId));
    }

    [Fact]
    public void SelectedCanonicalEventLocksCommandsEvenWhenTheStoredListIsNotSorted()
    {
        Seed(profile =>
        {
            profile.PendingEvents.Add(new() { Id = "later", Sequence = 20, ActorId = profile.Adventurers[1].Id, Rank = 2 });
            profile.PendingEvents.Add(new() { Id = "selected-first", Sequence = 10, ActorId = profile.Adventurers[0].Id,
                Rank = 2, Kind = "transfer", DefinitionJson = ContentBundle.Load(Content).ToJson() });
        });
        using var game = new GameController(Save, Content);
        var before = Snapshot();
        Assert.Equal("RankEvent", game.View.Screen);
        Assert.Equal("selected-first", game.View.NextGrowthEvent!.EventId);
        Assert.False(game.Execute(new("Rename", ActorId: Profile().Adventurers[1].Id, Value: "wrongly unlocked")).Success);
        Assert.Equal(before, Snapshot());
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("event")]
    [InlineData("same-actor-next-event")]
    [InlineData("empty-event")]
    public void StaleOrMismatchedIdsAreRejectedBeforeDrawingWithoutAnyConsumption(string mismatch)
    {
        Seed(profile =>
        {
            profile.PendingEvents.Add(new() { Id = "first", Sequence = 1, ActorId = profile.Adventurers[0].Id, Rank = 2 });
            profile.PendingEvents.Add(new() { Id = "second", Sequence = 2, ActorId = profile.Adventurers[0].Id, Rank = 3 });
        });
        using var game = new GameController(Save, Content);
        var displayed = game.View.NextGrowthEvent!;
        var actor = mismatch == "actor" ? Profile().Adventurers[1].Id : displayed.ActorId;
        var eventId = mismatch switch { "event" => "missing", "same-actor-next-event" => "second", "empty-event" => "", _ => displayed.EventId };
        var before = Snapshot();
        Assert.False(game.Execute(new("BeginEvent", ActorId: actor, TargetId: eventId, Value: "random")).Success);
        Assert.Equal(before, Snapshot());
        Assert.All(Profile().PendingEvents, pending => { Assert.Null(pending.Kind); Assert.Null(pending.FixedRandomResult); Assert.Empty(pending.Candidates); });
    }

    [Fact]
    public void RepeatedCommandReturnsPreviousResponseWhileANewStaleCommandCannotStartTheNextEvent()
    {
        Seed(profile =>
        {
            profile.PendingEvents.Add(new() { Id = "first", Sequence = 1, ActorId = profile.Adventurers[0].Id, Rank = 2 });
            profile.PendingEvents.Add(new() { Id = "second", Sequence = 2, ActorId = profile.Adventurers[0].Id, Rank = 3 });
        });
        using var game = new GameController(Save, Content);
        var target = game.View.NextGrowthEvent!;
        var command = new GameCommand("BeginEvent", ActorId: target.ActorId, TargetId: target.EventId, Value: "blessing", CommandId: "bound-first");
        var response = game.Execute(command);
        Assert.True(response.Success);
        Assert.True(game.Execute(new("ChooseBlessing", TargetId: game.View.Choices[0].Id)).Success);
        Assert.Equal("second", game.View.NextGrowthEvent!.EventId);
        var before = Snapshot();
        Assert.Equal(response, game.Execute(command));
        Assert.Equal(before, Snapshot());
        Assert.False(game.Execute(command with { CommandId = "new-stale-first" }).Success);
        Assert.Equal(before, Snapshot());
        Assert.Null(Profile().PendingEvents.Single().Kind);
    }

    [Theory]
    [InlineData("blessing")]
    [InlineData("transfer")]
    [InlineData("fusion")]
    [InlineData("fusion-result-maxed")]
    public void UnavailableOptionsExplainTheSamePreconditionTheMutationEnforces(string blocked)
    {
        Seed(profile =>
        {
            profile.PendingEvents.Add(new() { Id = "first", Sequence = 1, ActorId = profile.Adventurers[0].Id, Rank = 2 });
            if (blocked == "blessing")
                profile.Adventurers[0].Blessings = ContentBundle.Load(Content).Blessings.Where(b => b.Acquisition == "normal").Select(b => new OwnedBlessing { Id = b.Id, Rank = 3 }).ToList();
            if (blocked == "transfer") profile.Weapons.RemoveAll(weapon => weapon.OwnerId == "guild");
            if (blocked == "fusion-result-maxed")
                profile.Adventurers[0].Blessings = [new() { Id = "tech-strength", Rank = 3 }, new() { Id = "tech-vitality", Rank = 3 }, new() { Id = "tech-fused", Rank = 3 }];
        });
        using var game = new GameController(Save, Content);
        var target = game.View.NextGrowthEvent!;
        var kind = blocked == "fusion-result-maxed" ? "fusion" : blocked;
        var option = target.Options.Single(item => item.Kind == kind);
        Assert.False(option.CanBegin); Assert.False(string.IsNullOrWhiteSpace(option.UnavailableReason));
        Assert.True(target.Options.Single(item => item.Kind == "random").CanBegin);
        var before = Snapshot();
        var result = game.Execute(new("BeginEvent", ActorId: target.ActorId, TargetId: target.EventId, Value: kind));
        Assert.False(result.Success); Assert.Equal(option.UnavailableReason, result.Message);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void NoPendingEventProducesNoPreviewAndAnUnavailableBeginDoesNotPersist()
    {
        using var game = new GameController(Save, Content);
        var before = Snapshot();
        Assert.Null(game.View.NextGrowthEvent);
        Assert.False(game.Execute(new("BeginEvent", Value: "random")).Success);
        Assert.Equal(before, Snapshot());
    }

    [Theory]
    [InlineData("blessing")]
    [InlineData("random")]
    public void RepeatedInspectionDoesNotAlterDocumentsHistoryOrNextRandomOutcome(string kind)
    {
        Seed(profile => profile.PendingEvents.Add(new() { Id = "first", Sequence = 1, ActorId = profile.Adventurers[0].Id, Rank = 2 }));
        var controlSave = Path.Combine(root, "control.sqlite3"); CopyDocuments(Save, controlSave);
        using var inspected = new GameController(Save, Content);
        using var control = new GameController(controlSave, Content);
        var before = Snapshot();
        for (var i = 0; i < 100; i++)
        {
            var view = inspected.View;
            Assert.Equal("first", view.NextGrowthEvent!.EventId);
            var json = Json(view.NextGrowthEvent);
            Assert.DoesNotContain("fixedRandomResult", json); Assert.DoesNotContain("candidates", json);
        }
        Assert.Equal(before, Snapshot());
        Assert.True(inspected.Execute(new("BeginEvent", Value: kind)).Success);
        Assert.True(control.Execute(new("BeginEvent", Value: kind)).Success);
        var actual = Profile(); var expected = Profile(controlSave);
        Assert.Equal(expected.GrowthRng, actual.GrowthRng);
        Assert.Equal(expected.GenerationRng, actual.GenerationRng);
        Assert.Equal(Json(expected.PendingEvents), Json(actual.PendingEvents));
    }

    [Fact]
    public void InspectionDuringBattleDoesNotAlterAnyCheckpointOrRngWhenTheStateIsPersisted()
    {
        Seed(profile => profile.PendingEvents.Add(new() { Id = "first", Sequence = 1, ActorId = profile.Adventurers[0].Id, Rank = 2 }));
        var actor = Profile().Adventurers[0].Id;
        using (var setup = new GameController(Save, Content))
        {
            Assert.True(setup.Execute(new("StartMatch")).Success);
            Assert.True(setup.Execute(new("Deploy", ActorId: actor, X: 8, Y: 4)).Success);
            Assert.True(setup.Execute(new("StartWave")).Success);
        }
        var controlSave = Path.Combine(root, "battle-control.sqlite3"); CopyDocuments(Save, controlSave);
        using var inspected = new GameController(Save, Content);
        using var control = new GameController(controlSave, Content);
        var before = Snapshot();
        for (var i = 0; i < 100; i++)
            Assert.All(inspected.View.NextGrowthEvent!.Options, option => { Assert.False(option.CanBegin); Assert.Contains("拠点", option.UnavailableReason!); });
        Assert.Equal(before, Snapshot());
        Assert.True(inspected.Execute(new("TogglePause")).Success);
        Assert.True(control.Execute(new("TogglePause")).Success);
        Assert.Equal(Json(Profile(controlSave)), Json(Profile()));
        Assert.Equal(Document(controlSave, "session"), Document(Save, "session"));
    }

    [Fact]
    public void SelectedRandomEventRestartsWithTheSameRecipientWithoutRevealingItsStoredOutcome()
    {
        Seed(profile => profile.PendingEvents.Add(new() { Id = "random-first", Sequence = 1, ActorId = profile.Adventurers[0].Id, Rank = 2 }));
        string actor;
        string stored;
        using (var game = new GameController(Save, Content))
        {
            actor = game.View.NextGrowthEvent!.ActorId;
            Assert.True(game.Execute(new("BeginEvent", ActorId: actor, TargetId: "random-first", Value: "random")).Success);
            stored = Json(Profile().PendingEvents.Single());
        }
        using var reopened = new GameController(Save, Content);
        var preview = reopened.View.NextGrowthEvent!;
        Assert.Equal("random-first", preview.EventId); Assert.Equal(actor, preview.ActorId); Assert.Equal("random", preview.Kind);
        Assert.All(preview.Options, option => Assert.False(option.CanBegin));
        Assert.DoesNotContain("fixedRandomResult", Json(preview));
        Assert.Empty(reopened.View.Choices);
        Assert.Equal(stored, Json(Profile().PendingEvents.Single()));
    }

    [Fact]
    public void LegacyCommandPayloadHashStillReplaysItsStoredAcknowledgement()
    {
        Seed(profile => profile.PendingEvents.Add(new() { Id = "old-random", Sequence = 1, ActorId = profile.Adventurers[0].Id,
            Rank = 2, Kind = "random", DefinitionJson = ContentBundle.Load(Content).ToJson(), FixedRandomResult = "{\"kind\":\"equipment\",\"id\":null,\"stat\":null,\"delta\":0}" }));
        // Extracted from the published Windows v0.3.0+b7da687 DLL: property order, nulls and CRLF are part of the hash.
        var legacyPayload = "{\n  \"type\": \"BeginEvent\",\n  \"actorId\": null,\n  \"targetId\": null,\n  \"x\": 0,\n  \"y\": 0,\n  \"value\": \"random\",\n  \"commandId\": null\n}".Replace("\n", "\r\n", StringComparison.Ordinal);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacyPayload)));
        Assert.Equal("4C53357F09CD7E0CB3765E0F34B2E50B4877371C45988923CC6E0079EC00DE7F", hash);
        Assert.Equal(legacyPayload, JsonSerializer.Serialize(new GameCommand("BeginEvent", Value: "random"), DomainJson.Options));
        using (var store = new SaveStore(Save)) store.Commit(new(store.Revision, "v030-ack", hash, "v0.3.0 cached acknowledgement", new Dictionary<string, string>(), []));
        using var game = new GameController(Save, Content);
        var before = Snapshot();
        var result = game.Execute(new("BeginEvent", Value: "random", CommandId: "v030-ack"));
        Assert.True(result.Success, result.Message); Assert.Equal("v0.3.0 cached acknowledgement", result.Message);
        Assert.Equal(before, Snapshot());
    }

    [Theory]
    [InlineData("current")]
    [InlineData("matchStart")]
    [InlineData("waveStart")]
    public void KnownButMixedCheckpointRulesAreRejectedBeforeAnyWrite(string checkpoint)
    {
        var (profile, session) = LegacyDocuments();
        var loaded = JsonSerializer.Deserialize<MatchSession>(session, DomainJson.Options)!;
        SelectSnapshot(loaded, checkpoint).RuleSetId = "defense-v2";
        WriteDocuments(profile, Json(loaded));
        var before = File.ReadAllBytes(Save);
        Assert.Throws<InvalidDataException>(() => new GameController(Save, Content));
        Assert.Equal(before, File.ReadAllBytes(Save));
        Assert.Empty(Directory.GetFiles(root, "*.backup"));
    }

    [Theory]
    [InlineData("current")]
    [InlineData("matchStart")]
    [InlineData("waveStart")]
    public void UnknownCheckpointRuleIsRejectedByDomainRestoreBeforeAnyWrite(string checkpoint)
    {
        var (profile, session) = LegacyDocuments();
        var loaded = JsonSerializer.Deserialize<MatchSession>(session, DomainJson.Options)!;
        SelectSnapshot(loaded, checkpoint).RuleSetId = "future-defense-v99";
        WriteDocuments(profile, Json(loaded));
        var before = File.ReadAllBytes(Save);
        Assert.Throws<NotSupportedException>(() => new GameController(Save, Content));
        Assert.Equal(before, File.ReadAllBytes(Save));
        Assert.Empty(Directory.GetFiles(root, "*.backup"));
    }

    [Fact]
    public void NullAndDefenseV1AreTheSameCanonicalLegacyRuleWithoutRewritingThem()
    {
        var (profile, session) = LegacyDocuments();
        var loaded = JsonSerializer.Deserialize<MatchSession>(session, DomainJson.Options)!;
        loaded.MatchStart!.RuleSetId = "defense-v1";
        WriteDocuments(profile, Json(loaded));
        using var game = new GameController(Save, Content);
        Assert.Equal("Battle", game.View.Screen);
        using var store = new SaveStore(Save);
        var migrated = JsonSerializer.Deserialize<MatchSession>(store.ReadDocument("session")!, DomainJson.Options)!;
        Assert.Null(migrated.Current.RuleSetId); Assert.Null(migrated.WaveStart!.RuleSetId);
        Assert.Equal("defense-v1", migrated.MatchStart!.RuleSetId);
        Assert.Equal(loaded.ContentHash, migrated.ContentHash);
    }

    private static Guild.Domain.Battle.BattleSnapshot SelectSnapshot(MatchSession session, string checkpoint)
        => checkpoint switch { "current" => session.Current, "matchStart" => session.MatchStart!, _ => session.WaveStart! };
    private static (string Profile, string Session) LegacyDocuments()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "v0.2.0");
        return (File.ReadAllText(Path.Combine(path, "battle-profile.json")), File.ReadAllText(Path.Combine(path, "battle-session.json")));
    }
    private void Seed(Action<PlayerProfile> mutate)
    {
        using (var setup = new GameController(Save, Content)) { }
        var profile = Profile(); mutate(profile); WriteDocuments(Json(profile), Document(Save, "session") ?? "null");
    }
    private PlayerProfile Profile(string? path = null) => JsonSerializer.Deserialize<PlayerProfile>(Document(path ?? Save, "profile")!, DomainJson.Options)!;
    private static string? Document(string path, string key) { using var store = new SaveStore(path); return store.ReadDocument(key); }
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, DomainJson.Options);
    private sealed record StorageSnapshot(long Revision, string? Profile, string? Session, string History);
    private StorageSnapshot Snapshot()
    {
        using var store = new SaveStore(Save);
        return new(store.Revision, store.ReadDocument("profile"), store.ReadDocument("session"), Json(store.ReadHistory(0, 1000)));
    }
    private void WriteDocuments(string profile, string session)
    {
        using var store = new SaveStore(Save);
        store.Commit(new(store.Revision, Guid.NewGuid().ToString("N"), "fixture", "fixture", new Dictionary<string, string> { ["profile"] = profile, ["session"] = session }, []));
    }
    private static void CopyDocuments(string source, string target)
    {
        using var from = new SaveStore(source); using var to = new SaveStore(target);
        to.Commit(new(to.Revision, "copy", "fixture", "fixture", new Dictionary<string, string>
            { ["profile"] = from.ReadDocument("profile")!, ["session"] = from.ReadDocument("session") ?? "null" }, []));
    }
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
}

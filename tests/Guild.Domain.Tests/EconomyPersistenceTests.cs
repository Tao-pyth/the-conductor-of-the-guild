using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Guild.Application;
using Guild.Domain.Randomness;
using Guild.Domain.Serialization;
using Guild.Persistence;

namespace Guild.Domain.Tests;

public sealed class EconomyPersistenceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "guild-economy-save-" + Guid.NewGuid().ToString("N"));
    private string Save => Path.Combine(root, "save.sqlite3");
    private static string Content => Path.Combine(AppContext.BaseDirectory, "tech-content.json");

    public EconomyPersistenceTests() => Directory.CreateDirectory(root);

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void LegacyDocumentsMigrateTogetherWithOneReadableBackupAndNoRetroactiveEconomy(int version, bool active)
    {
        var legacy = CreateLegacy(version, active);
        var before = JsonSerializer.Deserialize<PlayerProfile>(legacy.Profile, DomainJson.Options)!;
        var frozen = active ? JsonSerializer.Deserialize<MatchSession>(legacy.Session, DomainJson.Options)! : null;
        string backup;
        using (var game = new GameController(Save, Content))
        {
            backup = Assert.IsType<string>(game.MigrationBackupPath);
            var migrated = ReadProfile();
            Assert.Equal(3, migrated.FormatVersion);
            Assert.Equal(before.Resources, migrated.Resources);
            Assert.Equal(Json(before.Weapons), Json(migrated.Weapons));
            Assert.Equal(Json(before.Adventurers), Json(migrated.Adventurers));
            Assert.Equal(before.GenerationRng, migrated.GenerationRng);
            Assert.Equal(before.GrowthRng, migrated.GrowthRng);
            Assert.Equal(4, migrated.Economy.Materials.Count);
            Assert.All(migrated.Economy.Materials.Values, amount => Assert.Equal(BigInteger.Zero, amount));
            Assert.Equal(4, migrated.Economy.Factions.Count);
            Assert.Equal(5, migrated.Economy.OfferedEquipment.Count);
            if (active)
            {
                var session = ReadSession();
                Assert.Equal(3, session.FormatVersion);
                Assert.Null(session.Economy);
                Assert.Equal(frozen!.ContentHash, session.ContentHash);
                Assert.Equal(Json(frozen.Current), Json(session.Current));
            }
        }
        using (var store = new SaveStore(backup))
        {
            Assert.Equal(legacy.Profile, store.ReadDocument("profile"));
            Assert.Equal(legacy.Session, store.ReadDocument("session"));
            Assert.Equal("ok", store.IntegrityCheck());
        }
        var offers = Json(ReadProfile().Economy.OfferedEquipment);
        using var reopened = new GameController(Save, Content);
        Assert.Null(reopened.MigrationBackupPath);
        Assert.Single(Directory.GetFiles(root, "*.before-documents-v3-*.backup"));
        Assert.Equal(offers, Json(ReadProfile().Economy.OfferedEquipment));
    }

    [Theory]
    [InlineData(CommitStage.AfterDocuments)]
    [InlineData(CommitStage.BeforeCommit)]
    public void InterruptedV3MigrationRollsBackBothDocumentsAndKeepsItsBackup(CommitStage failure)
    {
        var legacy = CreateLegacy(2, true);
        Assert.Throws<IOException>(() => new GameController(Save, Content,
            stage => { if (stage == failure) throw new IOException("economy migration fault"); }));
        using var store = new SaveStore(Save);
        Assert.Equal(legacy.Profile, store.ReadDocument("profile"));
        Assert.Equal(legacy.Session, store.ReadDocument("session"));
        Assert.Equal("ok", store.IntegrityCheck());
        var backup = Assert.Single(Directory.GetFiles(root, "*.before-documents-v3-*.backup"));
        using var backed = new SaveStore(backup);
        Assert.Equal(legacy.Profile, backed.ReadDocument("profile"));
        Assert.Equal(legacy.Session, backed.ReadDocument("session"));
    }

    [Fact]
    public void V3MigrationRecoversCommitAcknowledgementLossWithoutRegeneratingEquipment()
    {
        CreateLegacy(2, false);
        using (var game = new GameController(Save, Content,
            stage => { if (stage == CommitStage.AfterCommit) throw new IOException("lost migration ack"); }))
            Assert.Equal("Guild", game.View.Screen);
        var profile = ReadProfile();
        Assert.Equal(3, profile.FormatVersion);
        using var reopened = new GameController(Save, Content);
        Assert.Null(reopened.MigrationBackupPath);
        Assert.Equal(Json(profile.Economy), Json(ReadProfile().Economy));
        Assert.Single(Directory.GetFiles(root, "*.before-documents-v3-*.backup"));
    }

    [Theory]
    [InlineData("negative-material")]
    [InlineData("unknown-material")]
    [InlineData("missing-material")]
    [InlineData("unknown-delivery")]
    [InlineData("negative-completion")]
    [InlineData("invalid-faction")]
    [InlineData("even-rng")]
    [InlineData("missing-offer")]
    [InlineData("offer-owner")]
    [InlineData("offer-already-owned")]
    [InlineData("offer-unknown-skill")]
    [InlineData("offer-wrong-template")]
    [InlineData("unknown-mission")]
    [InlineData("unknown-member")]
    [InlineData("duplicate-member")]
    [InlineData("no-member-left")]
    public void CorruptEconomyIsRejectedBeforeDatabaseOrBackupMutation(string corruption)
    {
        CreateCurrent();
        var profile = ReadProfile();
        var delivery = profile.Economy.OfferedEquipment.Keys.First();
        switch (corruption)
        {
            case "negative-material": profile.Economy.Materials["wood"] = -1; break;
            case "unknown-material": profile.Economy.Materials["future-material"] = 1; break;
            case "missing-material": profile.Economy.Materials.Remove("wood"); break;
            case "unknown-delivery": profile.Economy.DeliveryCompletions["future-delivery"] = 1; break;
            case "negative-completion": profile.Economy.DeliveryCompletions[delivery] = -1; break;
            case "invalid-faction": profile.Economy.Factions["C"].Trust = 101; break;
            case "even-rng": profile.Economy.DispatchRng = new PcgState(42, 2); break;
            case "missing-offer": profile.Economy.OfferedEquipment.Remove(delivery); break;
            case "offer-owner": profile.Economy.OfferedEquipment[delivery].OwnerId = profile.Adventurers[0].Id; break;
            case "offer-already-owned": profile.Economy.OfferedEquipment[delivery].Id = profile.Weapons[0].Id; break;
            case "offer-unknown-skill": profile.Economy.OfferedEquipment[delivery].SkillIds = ["future-skill"]; break;
            case "offer-wrong-template": profile.Economy.OfferedEquipment[delivery].TemplateId = "future-template"; break;
            case "unknown-mission": profile.Economy.Reservation = new() { MissionId = "future-route", ActorIds = [profile.Adventurers[0].Id] }; break;
            case "unknown-member": profile.Economy.Reservation = new() { MissionId = "recovery", ActorIds = ["missing"] }; break;
            case "duplicate-member": profile.Economy.Reservation = new() { MissionId = "recovery", ActorIds = [profile.Adventurers[0].Id, profile.Adventurers[0].Id] }; break;
            case "no-member-left": profile.Economy.Reservation = new() { MissionId = "recovery", ActorIds = profile.Adventurers.Select(x => x.Id).ToList() }; break;
        }
        WriteDocuments(Json(profile), "null");
        AssertInvalidWithoutMutation<InvalidDataException>();
    }

    [Theory]
    [InlineData("economy")]
    [InlineData("materials")]
    [InlineData("dispatchRng")]
    public void MissingRequiredV3StateIsNotSilentlyDefaulted(string field)
    {
        CreateCurrent();
        var node = JsonNode.Parse(ReadDocument("profile"))!.AsObject();
        if (field == "economy") node.Remove("economy");
        else node["economy"]!.AsObject().Remove(field);
        WriteDocuments(node.ToJsonString(), "null");
        AssertInvalidWithoutMutation<InvalidDataException>();
    }

    [Fact]
    public void FutureNestedEconomyVersionIsRejectedBeforeAnyMutation()
    {
        CreateCurrent();
        var profile = ReadProfile();
        profile.Economy.Version = 99;
        WriteDocuments(Json(profile), "null");
        AssertInvalidWithoutMutation<NotSupportedException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonemptyEconomyCannotHideInsideLegacyDocumentsAndBypassPreflight(bool operation)
    {
        CreateCurrent(active: operation);
        var profile = ReadProfile();
        profile.FormatVersion = 2;
        if (!operation)
        {
            profile.Economy.Materials["wood"] = -1;
            WriteDocuments(Json(profile), "null");
        }
        else
        {
            profile.Economy = new();
            var session = ReadSession();
            session.FormatVersion = 2;
            WriteDocuments(Json(profile), Json(session));
        }
        AssertInvalidWithoutMutation<InvalidDataException>();
    }

    [Fact]
    public void MixedCurrentAndLegacyDocumentVersionsAreRejectedWithoutMigration()
    {
        CreateCurrent(active: true);
        var profile = ReadDocument("profile");
        var session = AsLegacy(ReadDocument("session"), 2);
        WriteDocuments(profile, session);
        AssertInvalidWithoutMutation<InvalidDataException>();
    }

    [Fact]
    public void HugeNonnegativeBalancesSurviveReopenAndCurrentSavesDoNotMigrate()
    {
        CreateCurrent();
        var profile = ReadProfile();
        profile.Resources = BigInteger.Pow(10, 100);
        profile.Economy.Materials["ore"] = BigInteger.Pow(10, 120);
        WriteDocuments(Json(profile), "null");
        using var game = new GameController(Save, Content);
        Assert.Null(game.MigrationBackupPath);
        Assert.Equal(profile.Resources, BigInteger.Parse(game.View.GuildResources));
        Assert.Equal(profile.Economy.Materials["ore"], ReadProfile().Economy.Materials["ore"]);
        Assert.Empty(Directory.GetFiles(root, "*.backup"));
    }

    [Theory]
    [InlineData("negative-contribution")]
    [InlineData("missing-material")]
    [InlineData("unknown-material")]
    [InlineData("wrong-start-budget")]
    public void CorruptOperationEconomyIsRejectedBeforeAnyMutation(string corruption)
    {
        CreateCurrent(active: true);
        var session = ReadSession();
        Assert.NotNull(session.Economy);
        switch (corruption)
        {
            case "negative-contribution": session.Economy.Contribution = -1; break;
            case "missing-material": session.Economy.VictoryMaterials.Remove("wood"); break;
            case "unknown-material": session.Economy.VictoryMaterials["future-material"] = 1; break;
            case "wrong-start-budget": session.Economy.Contribution = 50; break;
        }
        WriteDocuments(ReadDocument("profile"), Json(session));
        AssertInvalidWithoutMutation<InvalidDataException>();
    }

    [Theory]
    [InlineData("wrong-match")]
    [InlineData("invalid-roll")]
    [InlineData("wrong-chance")]
    [InlineData("unknown-member")]
    [InlineData("wrong-xp")]
    [InlineData("deployed-member")]
    public void CorruptFrozenDispatchIsRejectedBeforeAnyMutation(string corruption)
    {
        using (var game = new GameController(Save, Content))
        {
            var id = game.View.Adventurers[0].Id;
            Execute(game, new("ReserveDispatch", TargetId: "recovery", Value: id));
            Execute(game, new("StartMatch"));
        }
        var session = ReadSession();
        var assignment = Assert.IsType<DispatchAssignmentState>(session.Economy!.Dispatch);
        var member = assignment.Members[0];
        switch (corruption)
        {
            case "wrong-match": assignment.MatchId = "other-match"; break;
            case "invalid-roll": assignment.Roll = 100; break;
            case "wrong-chance": assignment.SuccessPercent = 1; break;
            case "unknown-member": member.ActorId = "missing"; break;
            case "wrong-xp": assignment.RankExperience[member.ActorId]++; break;
            case "deployed-member": session.Current.Units.Single(x => x.Id == member.ActorId).Deployed = true; break;
        }
        WriteDocuments(ReadDocument("profile"), Json(session));
        AssertInvalidWithoutMutation<InvalidDataException>();
    }

    private (string Profile, string Session) CreateLegacy(int version, bool active)
    {
        CreateCurrent(active);
        var profile = AsLegacy(ReadDocument("profile"), version);
        var session = active ? AsLegacy(ReadDocument("session"), version) : "null";
        WriteDocuments(profile, session);
        return (profile, session);
    }

    private static string AsLegacy(string json, int version)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        node["formatVersion"] = version;
        node.Remove("economy");
        return node.ToJsonString();
    }

    private void CreateCurrent(bool active = false)
    {
        using var game = new GameController(Save, Content);
        if (active) Execute(game, new("StartMatch"));
    }

    private void AssertInvalidWithoutMutation<T>() where T : Exception
    {
        var before = File.ReadAllBytes(Save);
        Assert.Throws<T>(() => new GameController(Save, Content));
        Assert.Equal(before, File.ReadAllBytes(Save));
        Assert.Empty(Directory.GetFiles(root, "*.backup"));
    }

    private static void Execute(GameController game, GameCommand command)
    {
        var result = game.Execute(command);
        Assert.True(result.Success, command.Type + ": " + result.Message);
    }

    private string ReadDocument(string key) { using var store = new SaveStore(Save); return store.ReadDocument(key)!; }
    private PlayerProfile ReadProfile() => JsonSerializer.Deserialize<PlayerProfile>(ReadDocument("profile"), DomainJson.Options)!;
    private MatchSession ReadSession() => JsonSerializer.Deserialize<MatchSession>(ReadDocument("session"), DomainJson.Options)!;
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, DomainJson.Options);
    private void WriteDocuments(string profile, string session)
    {
        using var store = new SaveStore(Save);
        store.Commit(new(store.Revision, Guid.NewGuid().ToString("N"), "economy-save-fixture", "fixture",
            new Dictionary<string, string> { ["profile"] = profile, ["session"] = session }, []));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var resolved = Path.GetFullPath(root);
        var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(resolved), parent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected test fixture directory.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
    }
}

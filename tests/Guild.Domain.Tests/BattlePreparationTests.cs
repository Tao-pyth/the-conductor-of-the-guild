using System.Numerics;
using System.Text.Json;
using Guild.Application;
using Guild.Domain.Battle;
using Guild.Domain.Definitions;
using Guild.Domain.Numerics;
using Guild.Domain.Serialization;
using Guild.Persistence;

namespace Guild.Domain.Tests;

public sealed class BattlePreparationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "guild-preparation-" + Guid.NewGuid().ToString("N"));
    private string SavePath => Path.Combine(directory, "save.sqlite3");
    private string ContentPath => Path.Combine(directory, "content.json");
    private static ContentBundle Fixture() => ContentBundle.Load(Path.Combine(AppContext.BaseDirectory, "tech-content.json"));

    public BattlePreparationTests() => Directory.CreateDirectory(directory);

    private void WriteModifiers(params ModifierDefinition[] modifiers)
    {
        var content = Fixture();
        content = content with { Blessings = content.Blessings.Select(b => b.Id == "tech-strength" ? b with { Modifiers = modifiers } : b).ToArray() };
        File.WriteAllText(ContentPath, content.ToJson());
    }

    private string SeedActor(Action<AdventurerState>? customize = null)
    {
        using (var game = new GameController(SavePath, ContentPath)) { }
        using var store = new SaveStore(SavePath);
        var profile = JsonSerializer.Deserialize<PlayerProfile>(store.ReadDocument("profile")!, DomainJson.Options)!;
        var actor = profile.Adventurers[0];
        actor.Stats = BattleStats.Standard;
        actor.Blessings = [new() { Id = "tech-strength", Rank = 1 }];
        var weapon = profile.Weapons.Single(w => w.Id == actor.WeaponId);
        weapon.TemplateId = "tech-sword";
        weapon.BasePower = 20;
        weapon.AttackIntervalMs = 1100;
        weapon.SkillIds = ["tech-slash", "tech-slash"];
        customize?.Invoke(actor);
        store.Commit(new(store.Revision, "seed-profile", "seed", "検証状態", new Dictionary<string, string>
        {
            ["profile"] = JsonSerializer.Serialize(profile, DomainJson.Options)
        }, []));
        return actor.Id;
    }

    private MatchSession ReadSession()
    {
        using var store = new SaveStore(SavePath);
        return JsonSerializer.Deserialize<MatchSession>(store.ReadDocument("session")!, DomainJson.Options)!;
    }

    [Fact]
    public void PermanentDerivedModifiersAreCachedExactlyWithoutChangingGeneratedBaseValues()
    {
        WriteModifiers(
            new() { Id = "hp", ParameterId = "max-hp", Stage = ModifierStage.Fixed, Amount = new Rational(100, 3) },
            new() { Id = "physical", ParameterId = "physical-defense", Stage = ModifierStage.Fixed, Amount = new Rational(4, 3) },
            new() { Id = "magical", ParameterId = "magical-defense", Stage = ModifierStage.Additive, Amount = new Rational(1, 5) },
            new() { Id = "power", ParameterId = "attack-power", Stage = ModifierStage.Fixed, Amount = new Rational(7, 3) },
            new() { Id = "interval", ParameterId = "attack-interval-ms", Stage = ModifierStage.Fixed, Amount = new Rational(1, 3) },
            new() { Id = "movement", ParameterId = "move-centi-per-second", Stage = ModifierStage.Additive, Amount = new Rational(1, 10) });
        var actorId = SeedActor(actor => actor.Hp = 233);
        using var game = new GameController(SavePath, ContentPath);
        Assert.Equal("233", game.View.Adventurers.Single(a => a.Id == actorId).MaxHp);
        Assert.True(game.Execute(new("StartMatch")).Success);
        var unit = ReadSession().Current.Units.Single(u => u.Id == actorId);
        Assert.Equal(new Rational(700, 3), unit.PreparedMaxHp);
        Assert.Equal((Rational)20, unit.BasePower);
        Assert.Equal((Rational)1100, unit.BaseAttackIntervalMs);
        Assert.Equal(new Rational(106, 3), unit.PreparedAttackPower);
        Assert.Equal(new Rational(3001, 3), unit.PreparedAttackIntervalMs);
        Assert.Equal((Rational)385, unit.PreparedMoveCentiPerSecond);
        Assert.Equal(new Rational(34, 3), unit.PhysicalDefense);
        Assert.Equal((Rational)12, unit.MagicalDefense);
        Assert.Equal(new BigInteger(233), unit.MaxHp);
        Assert.Equal(new BigInteger(233), unit.Hp);
        Assert.Equal(BattleStats.Standard, unit.Stats);
        Assert.Equal(new Rational(106, 3), CombatMath.Attack(unit));
    }

    [Fact]
    public void WeaknessAndVitalityShareOnlyTheTemporaryStageAfterPermanentHp()
    {
        WriteModifiers(new ModifierDefinition { Id = "hp", ParameterId = "max-hp", Stage = ModifierStage.Fixed, Amount = 100 });
        var actorId = SeedActor(actor => { actor.Hp = 300; actor.WeaknessStacks = 21; });
        using var game = new GameController(SavePath, ContentPath);
        Assert.True(game.Execute(new("StartMatch")).Success);
        var simulation = BattleSimulation.Restore(ReadSession().Current);
        var unit = simulation.State.Units.Single(u => u.Id == actorId);
        Assert.Equal((Rational)300, unit.PreparedMaxHp);
        Assert.Equal(new BigInteger(3), unit.MaxHp);
        unit.PendingLevelChoices = 1;
        Assert.True(simulation.GrantLevel(actorId, "vitality"));
        Assert.Equal(new BigInteger(15), unit.MaxHp);
        Assert.Equal(new Rational(-1, 20), unit.MaxHpMultiplier);
        Assert.Equal((Rational)300, unit.PreparedMaxHp);
    }

    [Fact]
    public void SkillAndRealSlotTargetsUseOrMatchingAndDeduplicateWithinTheSharedPipeline()
    {
        WriteModifiers(
            new() { Id = "common", ParameterId = "attack-power", Stage = ModifierStage.Fixed, Amount = 10 },
            new() { Id = "both", ParameterId = "attack-power", Stage = ModifierStage.Fixed, Amount = 5, SkillId = "tech-slash", Slot = "S1" },
            new() { Id = "slot-two", ParameterId = "attack-power", Stage = ModifierStage.Additive, Amount = new Rational(1, 10), Slot = "S2" });
        var actorId = SeedActor();
        using var game = new GameController(SavePath, ContentPath);
        Assert.True(game.Execute(new("StartMatch")).Success);
        var unit = ReadSession().Current.Units.Single(u => u.Id == actorId);
        Assert.Equal((Rational)43, unit.PreparedAttackPower);
        Assert.Equal(new Rational(48, 43), CombatMath.SkillPower(unit.SkillModifiers, "tech-slash", "S1"));
        Assert.Equal(new Rational(264, 215), CombatMath.SkillPower(unit.SkillModifiers, "tech-slash", "S2"));
        Assert.Equal(Rational.One, CombatMath.SkillPower(unit.SkillModifiers, "tech-slash", "S1", healing: true));
        Assert.Equal(2, unit.SkillModifiers.Count);
    }

    [Fact]
    public void RestartKeepsPreparedValuesAndActualOldDefinitions()
    {
        WriteModifiers(new ModifierDefinition { Id = "power", ParameterId = "attack-power", Stage = ModifierStage.Fixed, Amount = new Rational(7, 3) });
        var actorId = SeedActor();
        using (var game = new GameController(SavePath, ContentPath)) Assert.True(game.Execute(new("StartMatch")).Success);
        WriteModifiers(new ModifierDefinition { Id = "power", ParameterId = "attack-power", Stage = ModifierStage.Fixed, Amount = 100 });
        using var restored = new GameController(SavePath, ContentPath);
        var frozen = ReadSession().Current;
        Assert.Equal(new Rational(106, 3), frozen.Units.Single(u => u.Id == actorId).PreparedAttackPower);
        Assert.Equal(new Rational(7, 3), frozen.Setup.Content.Blessings.Single(b => b.Id == "tech-strength").Modifiers[0].Amount);
    }

    [Theory]
    [InlineData("resources", null, null)]
    [InlineData("str", "hp-low", null)]
    [InlineData("max-hp", null, "tech-slash")]
    public void UnsupportedActionsAreRejectedBeforeCreatingASave(string parameterId, string? condition, string? skillId)
    {
        WriteModifiers(new ModifierDefinition { Id = "unsupported", ParameterId = parameterId, Condition = condition, SkillId = skillId,
            Stage = ModifierStage.Fixed, Amount = 1 });
        Assert.Throws<InvalidDataException>(() => new GameController(SavePath, ContentPath));
        Assert.False(File.Exists(SavePath));
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}

using System.Numerics;
using System.Text.Json;
using Guild.Application;
using Guild.Domain.Battle;
using Guild.Domain.Definitions;
using Guild.Domain.Numerics;
using Guild.Domain.Serialization;
using Guild.Persistence;

namespace Guild.Domain.Tests;

public sealed class DecisionInformationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "guild-decisions-" + Guid.NewGuid().ToString("N"));
    private string Save => Path.Combine(directory, "save.sqlite3");
    private string Content => Path.Combine(directory, "content.json");
    private static ContentBundle Fixture() => ContentBundle.Load(Path.Combine(AppContext.BaseDirectory, "tech-content.json"));

    public DecisionInformationTests()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Content, Fixture().ToJson());
    }

    private string Seed(Action<PlayerProfile>? customize = null)
    {
        using (var game = new GameController(Save, Content)) { }
        using var store = new SaveStore(Save);
        var profile = JsonSerializer.Deserialize<PlayerProfile>(store.ReadDocument("profile")!, DomainJson.Options)!;
        var actor = profile.Adventurers[0];
        actor.Stats = BattleStats.Standard with { Vit = 9 };
        actor.Growth = new(0, 0, 0, 0, 0);
        actor.Blessings = [];
        actor.Hp = 190;
        var weapon = profile.Weapons.Single(w => w.Id == actor.WeaponId);
        weapon.TemplateId = "tech-sword"; weapon.BasePower = 20;
        weapon.AttackIntervalMs = 1101; weapon.RangeCenti = 345;
        weapon.SkillIds = ["tech-slash", "tech-mend"];
        customize?.Invoke(profile);
        store.Commit(new(store.Revision, "seed", "seed", "検証状態", new Dictionary<string, string>
        {
            ["profile"] = JsonSerializer.Serialize(profile, DomainJson.Options)
        }, []));
        return actor.Id;
    }

    private MatchSession Session()
    {
        using var store = new SaveStore(Save);
        return JsonSerializer.Deserialize<MatchSession>(store.ReadDocument("session")!, DomainJson.Options)!;
    }

    private void EditSession(Action<MatchSession> customize)
    {
        var session = Session(); customize(session);
        using var store = new SaveStore(Save);
        store.Commit(new(store.Revision, "session-" + store.Revision, "session", "検証準備", new Dictionary<string, string>
        {
            ["session"] = JsonSerializer.Serialize(session, DomainJson.Options)
        }, []));
    }

    private (long Revision, string Profile, string? Session, string History) SavedState()
    {
        using var store = new SaveStore(Save);
        return (store.Revision, store.ReadDocument("profile")!, store.ReadDocument("session"),
            JsonSerializer.Serialize(store.ReadHistory(), DomainJson.Options));
    }

    [Fact]
    public void GuildReferenceMatchesActualSortieIncludingHpFloorSavedWeaponAndNextEffect()
    {
        var id = Seed(p =>
        {
            p.Adventurers[0].WeaknessStacks = 1;
            p.Adventurers[0].NextMatchChanges["str"] = 7;
        });
        using var game = new GameController(Save, Content);
        var before = SavedState();
        var reference = game.View.Adventurers.Single(a => a.Id == id);
        var referencePerformance = Assert.IsType<AdventurerPerformanceView>(reference.Performance);
        Assert.Equal("180", reference.MaxHp); Assert.Equal("180", reference.Hp);
        Assert.Equal("40.00", referencePerformance.AttackPower);
        Assert.Equal("1.01", referencePerformance.AttackIntervalSeconds);
        Assert.Equal("3.50", referencePerformance.MovementTilesPerSecond);
        Assert.Equal("3.45", referencePerformance.WeaponRange);
        Assert.Contains("STR17", referencePerformance.AttributeSummary);
        Assert.Contains("STR10", referencePerformance.BaseAttributeSummary);
        Assert.Contains("次出撃", referencePerformance.Context);
        Assert.Null(reference.PreparationInfo); Assert.Null(reference.RangeGuide); Assert.Null(game.View.NextEncounter);
        Assert.Equal(before, SavedState());
        Assert.True(game.Execute(new("StartMatch")).Success);
        var actual = game.View.Adventurers.Single(a => a.Id == id);
        var actualPerformance = Assert.IsType<AdventurerPerformanceView>(actual.Performance);
        Assert.Equal(reference.MaxHp, actual.MaxHp); Assert.Equal(reference.Hp, actual.Hp);
        Assert.Equal(referencePerformance with { Context = actualPerformance.Context, SkillDetails = actualPerformance.SkillDetails }, actualPerformance);
        Assert.Equal(referencePerformance.SkillDetails, actualPerformance.SkillDetails);
        Assert.Contains("実値", actualPerformance.Context);
        Assert.All(game.View.Stages, stage => Assert.Equal("1000", stage.InitialResources));
    }

    [Fact]
    public void ActualRepeatedSlotsKeepSeparatePowerAndCooldownWithoutInventingTemplateSkills()
    {
        var definitions = Fixture();
        definitions = definitions with { Blessings = definitions.Blessings.Select(b => b.Id == "tech-strength"
            ? b with { Modifiers = [new() { Id = "slot2", ParameterId = "attack-power", Slot = "S2", Stage = ModifierStage.Additive, Amount = 1 }] }
            : b).ToArray() };
        File.WriteAllText(Content, definitions.ToJson());
        var id = Seed(p =>
        {
            p.Adventurers[0].Blessings = [new() { Id = "tech-strength", Rank = 1 }];
            p.Weapons.Single(w => w.Id == p.Adventurers[0].WeaponId).SkillIds = ["tech-slash", "tech-slash"];
        });
        using (var game = new GameController(Save, Content)) Assert.True(game.Execute(new("StartMatch")).Success);
        EditSession(s => s.Current.Units.Single(u => u.Id == id).Skills[1].Cooldown = new() { FixedReductionMs = 1000 });
        using var loaded = new GameController(Save, Content);
        var skills = loaded.View.Adventurers.Single(a => a.Id == id).Performance!.SkillDetails;
        Assert.Equal(new[] { "S1", "S2" }, skills.Select(s => s.Slot));
        Assert.All(skills, s => { Assert.Equal("tech-slash", s.SkillId); Assert.Equal("1.60", s.Range); Assert.Contains("敵1体", s.Target); });
        Assert.Contains("46.20", skills[0].Effect); Assert.Contains("92.40", skills[1].Effect);
        Assert.Equal("3.00", skills[0].CooldownSeconds); Assert.Equal("2.00", skills[1].CooldownSeconds);
    }

    [Fact]
    public void MatchReadsFrozenSkillForecastAndActualTemporaryCombatModifiers()
    {
        var id = Seed();
        using (var game = new GameController(Save, Content)) Assert.True(game.Execute(new("StartMatch")).Success);
        EditSession(s =>
        {
            var unit = s.Current.Units.Single(u => u.Id == id);
            unit.LevelAttackMultiplier = new Rational(27, 25);
            unit.LevelMoveMultiplier = new Rational(11, 10);
            unit.LevelIntervalMultiplier = new Rational(19, 20);
            unit.Statuses.Add(new() { Kind = "slow", Strength = new Rational(1, 4), ExpiresAtMs = 10000 });
        });
        var replacement = Fixture();
        replacement = replacement with
        {
            Skills = replacement.Skills.Select(s => s with { Name = "changed", RangeCenti = 999 }).ToArray(),
            Stages = replacement.Stages.Select(s => s.Mode == "invasion" ? s : s with
            { Waves = s.Waves.Select(w => w with { EnemyHp = 999 }).ToArray() }).ToArray()
        };
        File.WriteAllText(Content, replacement.ToJson());
        using var loaded = new GameController(Save, Content);
        var actor = loaded.View.Adventurers.Single(a => a.Id == id);
        var simulation = BattleSimulation.Restore(Session().Current);
        var exact = simulation.DescribeUnit(id);
        Assert.Equal(exact.AttackPower.ToDisplayString(), actor.Performance!.AttackPower);
        Assert.Equal((exact.MovementCentiPerSecond / 100).ToDisplayString(), actor.Performance.MovementTilesPerSecond);
        Assert.Equal("2.89", actor.Performance.MovementTilesPerSecond);
        Assert.Equal((exact.AttackIntervalMs / 1000).ToDisplayString(), actor.Performance.AttackIntervalSeconds);
        Assert.Equal("1.60", actor.Performance.SkillDetails[0].Range);
        Assert.DoesNotContain(actor.Performance.SkillDetails, s => s.Name == "changed");
        Assert.Equal(Fixture().Stages.First(s => s.Mode != "invasion").Waves[0].EnemyHp.Floor().ToString(), loaded.View.NextEncounter!.EnemyHp);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void DeadLevelPurchaseAvailabilityRetainsModeSpecificRule(bool invasion, bool canBuy)
    {
        var id = Seed();
        using (var game = new GameController(Save, Content))
        {
            Assert.True(game.Execute(invasion ? new("StartMatch", TargetId: "tech-first-invasion", Value: id) : new("StartMatch")).Success);
        }
        EditSession(s =>
        {
            var unit = s.Current.Units.Single(u => u.Id == id);
            unit.Deployed = unit.EverDeployed = true; unit.Hp = 0;
            unit.Level = 3; unit.LevelExperience = 12; unit.RevivalCount = 2;
            s.Current.Resources = 1000;
        });
        using var loaded = new GameController(Save, Content);
        var info = loaded.View.Adventurers.Single(a => a.Id == id).PreparationInfo!;
        Assert.Equal("275", info.RequiredLevelXp); Assert.Equal("263", info.MissingLevelXp);
        Assert.Equal("526", info.PurchaseCost); Assert.Equal(4, info.PurchaseTargetLevel);
        Assert.Equal(canBuy, info.CanPurchaseLevel); Assert.Equal("225", info.RevivalCost); Assert.True(info.CanRevive);
        Assert.False(loaded.View.Adventurers.Single(a => a.Id == id).RangeGuide!.Available);
        Assert.Equal(canBuy, loaded.Execute(new("BuyLevel", ActorId: id)).Success);
    }

    [Theory]
    [InlineData(249, false)]
    [InlineData(250, true)]
    public void PurchaseShowsExactMissingXpCostAndAffordabilityBoundary(int resources, bool expected)
    {
        var id = Seed();
        using (var game = new GameController(Save, Content)) Assert.True(game.Execute(new("StartMatch")).Success);
        EditSession(s =>
        {
            s.Current.Units.Single(u => u.Id == id).Deployed = true;
            s.Current.Resources = resources;
        });
        using var game2 = new GameController(Save, Content);
        var info = game2.View.Adventurers.Single(a => a.Id == id).PreparationInfo!;
        Assert.Equal("0", info.CurrentLevelXp); Assert.Equal("125", info.RequiredLevelXp);
        Assert.Equal("250", info.PurchaseCost); Assert.Equal(expected, info.CanPurchaseLevel);
        Assert.Equal(expected, game2.Execute(new("BuyLevel", ActorId: id)).Success);
    }

    [Fact]
    public void LevelCapPendingChoiceDeploymentAndPartyReasonsAreExplicit()
    {
        var id = Seed(p => p.Adventurers[0].Rank = BigInteger.Pow(10, 80));
        using (var game = new GameController(Save, Content)) Assert.True(game.Execute(new("StartMatch", TargetId: "tech-first-invasion", Value: id)).Success);
        EditSession(s =>
        {
            var unit = s.Current.Units.Single(u => u.Id == id);
            unit.Deployed = true; unit.Level = 10; unit.LevelExperience = BigInteger.Pow(10, 80);
        });
        using var loaded = new GameController(Save, Content);
        var actor = loaded.View.Adventurers.Single(a => a.Id == id);
        var cap = actor.PreparationInfo!;
        Assert.True(cap.IsLevelCap); Assert.Null(cap.PurchaseCost); Assert.Null(cap.PurchaseTargetLevel);
        Assert.False(cap.CanPurchaseLevel); Assert.Contains("上限", cap.PurchaseUnavailableReason);
        Assert.Equal("上限", actor.LevelPurchaseCost); Assert.Equal("250", cap.DeploymentCost);
        Assert.Equal(BigInteger.Pow(10, 80).ToString(), cap.CurrentLevelXp);
        var outside = loaded.View.Adventurers.First(a => a.Id != id);
        Assert.False(outside.PreparationInfo!.CanDeploy); Assert.Contains("遠征編成", outside.PreparationInfo.DeployUnavailableReason);
        Assert.False(outside.RangeGuide!.Available);
    }

    [Fact]
    public void RangeGuideSeparatesActualPositionAnchorAndHealingRangeAndMarksLegacy()
    {
        var definitions = Fixture();
        File.WriteAllText(Content, (definitions with
        { Skills = definitions.Skills.Select(s => s.Id == "tech-mend" ? s with { RangeCenti = 550 } : s).ToArray() }).ToJson());
        var id = Seed();
        using (var game = new GameController(Save, Content)) Assert.True(game.Execute(new("StartMatch")).Success);
        EditSession(s =>
        {
            var unit = s.Current.Units.Single(u => u.Id == id);
            unit.Deployed = true; unit.X = 9; unit.Y = 4; unit.AnchorX = 8; unit.AnchorY = 4;
            s.Current.RuleSetId = null;
        });
        using var loaded = new GameController(Save, Content);
        var guide = loaded.View.Adventurers.Single(a => a.Id == id).RangeGuide!;
        Assert.True(guide.Available); Assert.True(guide.IsLegacyDefense);
        Assert.Equal(9, guide.ActualX); Assert.Equal(8, guide.AnchorX);
        Assert.Equal(3, guide.ActionRadius); Assert.Equal(4, guide.WatchRadius); Assert.Equal(3.45, guide.AttackRadius);
        Assert.Equal(5.5, guide.SkillRadii.Single(s => s.IsHealing).Radius);
        Assert.Contains("必ず攻撃", guide.Explanation); Assert.Contains("回復", guide.Explanation);
        Assert.True(loaded.Execute(new("StartWave")).Success);
        Assert.All(loaded.View.Adventurers, a => Assert.Null(a.RangeGuide));
        Assert.Null(loaded.View.NextEncounter);
        Assert.True(loaded.Execute(new("TogglePause")).Success);
        Assert.All(loaded.View.Adventurers, a => Assert.Null(a.RangeGuide));
    }

    [Theory]
    [InlineData(0, "43", false)]
    [InlineData(1, "1", true)]
    public void DefenseForecastIsOnlyTheNextWaveAndUsesSpawnHpFloor(int completedWaves, string hp, bool attacks)
    {
        var definitions = Fixture();
        File.WriteAllText(Content, (definitions with { Stages = definitions.Stages.Select(s => s.Mode == "invasion" ? s : s with
        { Waves = s.Waves.Select((w, i) => w with { EnemyHp = i == 0 ? new Rational(439, 10) : new Rational(1, 2), AttacksAdventurers = i != 0 }).ToArray() }).ToArray() }).ToJson());
        Seed();
        using (var game = new GameController(Save, Content)) Assert.True(game.Execute(new("StartMatch")).Success);
        EditSession(s => s.Current.Wave = completedWaves);
        using var loaded = new GameController(Save, Content);
        var forecast = loaded.View.NextEncounter!;
        Assert.Equal(completedWaves + 1, forecast.Number); Assert.Equal(hp, forecast.EnemyHp);
        Assert.Equal(attacks, forecast.AttacksAdventurers);
        Assert.Equal(definitions.Stages.First(s => s.Mode != "invasion").Waves[completedWaves].EnemyCount, forecast.EnemyCount);
    }

    [Fact]
    public void InvasionForecastUsesRolesAndTimeLimitWithoutDefenseFields()
    {
        var id = Seed();
        using var game = new GameController(Save, Content);
        Assert.True(game.Execute(new("StartMatch", TargetId: "tech-first-invasion", Value: id)).Success);
        var forecast = game.View.NextEncounter!;
        var section = Fixture().Stages.Single(s => s.Id == "tech-first-invasion").Sections![0];
        Assert.Equal(section.Enemies.Count, forecast.EnemyCount);
        Assert.Equal(section.Enemies.Count(e => e.Role == "guard"), forecast.GuardCount);
        Assert.Equal(section.Enemies.Count(e => e.Role == "turret"), forecast.TurretCount);
        Assert.Equal(section.Enemies.Count(e => e.Role == "gate"), forecast.GateCount);
        Assert.Equal(section.Enemies.Count(e => e.Role == "core"), forecast.CoreCount);
        Assert.Equal(new Rational(section.TimeLimitMs, 1000).ToDisplayString(), forecast.TimeLimitSeconds);
        Assert.Null(forecast.EnemyHp); Assert.Null(forecast.AttacksAdventurers);
    }

    [Fact]
    public void PendingLevelChoiceExplainsTheBlockAndResultsHidePreparationGuides()
    {
        var id = Seed();
        using (var game = new GameController(Save, Content)) Assert.True(game.Execute(new("StartMatch")).Success);
        EditSession(s =>
        {
            var unit = s.Current.Units.Single(u => u.Id == id);
            unit.Deployed = true; unit.PendingLevelChoices = 1;
            s.LevelChoices[id] = ["vitality", "rapid", "reach"];
        });
        using (var loaded = new GameController(Save, Content))
        {
            var info = loaded.View.Adventurers.Single(a => a.Id == id).PreparationInfo!;
            Assert.False(info.CanPurchaseLevel); Assert.Contains("未選択", info.PurchaseUnavailableReason);
            Assert.False(loaded.Execute(new("BuyLevel", ActorId: id)).Success);
        }
        EditSession(s =>
        {
            s.Phase = "Victory"; s.Current.Victory = true;
            s.LevelChoices.Clear(); s.Current.Units.Single(u => u.Id == id).PendingLevelChoices = 0;
        });
        using var result = new GameController(Save, Content);
        Assert.Null(result.View.NextEncounter);
        Assert.All(result.View.Adventurers, a =>
        {
            Assert.Null(a.RangeGuide); Assert.False(a.PreparationInfo!.CanPurchaseLevel);
            Assert.False(a.PreparationInfo.CanDeploy); Assert.False(a.PreparationInfo.CanRevive);
        });
    }

    [Fact]
    public void RepeatedGuildAndPreparationReadsPreserveDocumentsHistoryRngAndFutureChoices()
    {
        var id = Seed(p => p.Adventurers[0].NextMatchChanges["str"] = 2);
        using (var game = new GameController(Save, Content))
        {
            var before = SavedState();
            for (var i = 0; i < 100; i++) _ = game.View;
            Assert.Equal(before, SavedState());
            Assert.True(game.Execute(new("StartMatch")).Success);
            Assert.True(game.Execute(new("Deploy", ActorId: id, X: 8, Y: 4)).Success);
        }
        var comparison = Path.Combine(directory, "comparison.sqlite3");
        File.Copy(Save, comparison);
        using var reference = new GameController(comparison, Content);
        using var read = new GameController(Save, Content);
        var prepared = SavedState();
        for (var i = 0; i < 100; i++) _ = read.View;
        Assert.Equal(prepared, SavedState());
        Assert.True(read.Execute(new("BuyLevel", ActorId: id)).Success);
        Assert.True(reference.Execute(new("BuyLevel", ActorId: id)).Success);
        Assert.Equal(reference.View.Choices, read.View.Choices);
        using var comparisonStore = new SaveStore(comparison);
        using var readStore = new SaveStore(Save);
        var referenceProfile = JsonSerializer.Deserialize<PlayerProfile>(comparisonStore.ReadDocument("profile")!, DomainJson.Options)!;
        var readProfile = JsonSerializer.Deserialize<PlayerProfile>(readStore.ReadDocument("profile")!, DomainJson.Options)!;
        Assert.Equal(referenceProfile.GrowthRng, readProfile.GrowthRng);
        Assert.Equal(referenceProfile.GenerationRng, readProfile.GenerationRng);
        Assert.Equal(2, readProfile.Adventurers.Single(a => a.Id == id).NextMatchChanges["str"]);
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}

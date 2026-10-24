using System.Numerics;
using System.Text.Json;
using Guild.Domain.Battle;
using Guild.Domain.Definitions;
using Guild.Domain.Numerics;
using Guild.Domain.Serialization;

namespace Guild.Domain.Tests;

public sealed class InvasionDomainTests
{
    private static ContentBundle Fixture() => ContentBundle.Load(Path.Combine(AppContext.BaseDirectory, "tech-content.json"));
    private static BattleSimulation Battle(ContentBundle? content = null, int count = 2, Rational? power = null)
    {
        content ??= Fixture();
        var units = Enumerable.Range(0, count).Select(i => new BattleUnitSetup
        {
            Id = "a" + i, Name = "冒険者" + i, WeaponTemplateId = "tech-sword",
            Stats = BattleStats.Standard, BasePower = power ?? 100000,
            AttackIntervalMs = 250, SkillIds = []
        }).ToList();
        return new(new BattleSetup
        {
            MatchId = "invasion-match", AttemptId = "attempt-1", Content = content,
            StageId = "tech-first-invasion", PartyIds = units.Select(u => u.Id).ToList(), Units = units,
            InitialResources = 10000
        });
    }

    private static ContentBundle OneSection(int duration = 300000)
    {
        var content = Fixture();
        var stage = content.Stages.Single(s => s.Mode == "invasion");
        var section = stage.Sections![2] with { Number = 1, TimeLimitMs = duration };
        return content with { Stages = [stage with { Sections = [section] }] };
    }

    private static void Deploy(BattleSimulation battle, int count = 2)
    {
        for (var i = 0; i < count; i++) Assert.True(battle.Deploy("a" + i, 2, i + 4));
    }

    private static void FinishSection(BattleSimulation battle)
    {
        for (var i = 0; i < 30000 && battle.State.Running; i++) battle.Step();
        Assert.False(battle.State.Running);
    }

    [Fact]
    public void FixtureDefinesThreeReachableSectionsAndTwoDistinctModes()
    {
        var content = Fixture();
        Assert.Empty(content.Validate());
        Assert.Single(content.Stages, s => s.Mode is null or "defense");
        var stage = Assert.Single(content.Stages, s => s.Mode == "invasion");
        Assert.Equal(3, stage.Sections!.Count);
        Assert.Equal(10, stage.DeploymentCells!.Count);
        Assert.Equal(new[] { 4, 5, 7 }, stage.Sections.Select(s => s.Enemies.Count));
        Assert.All(stage.Sections, s => Assert.Equal(300000, s.TimeLimitMs));
    }

    [Fact]
    public void LegacyStageSerializationDoesNotAcquireInvasionDefaultProperties()
    {
        var original = Fixture();
        var legacy = original with { Stages = [original.Stages.Single(s => s.Mode is null)] };
        var json = legacy.ToJson();
        Assert.DoesNotContain("\"mode\"", json);
        Assert.DoesNotContain("\"sections\"", json);
        Assert.DoesNotContain("\"deploymentCells\"", json);
        Assert.Equal(legacy.ComputeHash(), ContentBundle.FromJson(json).ComputeHash());
    }

    [Fact]
    public void ExpeditionSelectionAndDeploymentCellsAreEnforcedWithoutConsumingResources()
    {
        var battle = Battle();
        var snapshot = battle.Capture();
        snapshot.Setup = snapshot.Setup with { PartyIds = ["a0"] };
        battle = BattleSimulation.Restore(snapshot);
        var resources = battle.State.Resources;
        Assert.False(battle.Deploy("a1", 2, 4));
        Assert.False(battle.Deploy("a0", 7, 4));
        Assert.False(battle.BeginWave());
        Assert.Equal(resources, battle.State.Resources);
        Assert.True(battle.Deploy("a0", 2, 5));
        Assert.True(battle.BeginWave());
        Assert.False(battle.Deploy("a1", 2, 4));
    }

    [Fact]
    public void AllThreeSectionsAdvanceAndReturnUnitsToTheirAssignedDeploymentCells()
    {
        var battle = Battle();
        Deploy(battle);
        for (var section = 1; section <= 3; section++)
        {
            foreach (var unit in battle.State.Units.Where(u => !u.IsEnemy))
                while (unit.PendingLevelChoices > 0) Assert.True(battle.GrantLevel(unit.Id, "firepower"));
            Assert.True(battle.BeginWave());
            FinishSection(battle);
            Assert.False(battle.State.Defeated);
            Assert.Equal(section, battle.State.Wave);
            Assert.True(battle.State.WaveCompleted);
            Assert.All(battle.State.Units.Where(u => !u.IsEnemy && u.Deployed), u =>
            {
                Assert.Equal((Rational)u.DeploymentX, u.X);
                Assert.Equal((Rational)u.DeploymentY, u.Y);
            });
        }
        Assert.True(battle.State.Victory);
        Assert.All(battle.State.Units.Where(u => !u.IsEnemy), u =>
        {
            Assert.Equal(2, u.Level);
            Assert.Equal(new BigInteger(25), u.LevelExperience);
        });
    }

    [Fact]
    public void SightDoesNotFreezeMarchingBeforeAnAttackPositionCanBeReached()
    {
        var battle = Battle(OneSection(), count: 1);
        Deploy(battle, 1);
        Assert.True(battle.BeginWave());
        var unit = battle.State.Units.Single(u => !u.IsEnemy);
        for (var i = 0; i < 1500 && battle.State.Running; i++) battle.Step();
        Assert.True(unit.X > 2 || battle.State.Victory);
        Assert.False(battle.State.Defeated);
        FinishSection(battle);
        Assert.True(battle.State.Victory);
    }

    [Fact]
    public void SimultaneousAnnihilationAndFinalTargetDestructionIsDefeatEvenWithReserves()
    {
        var battle = Battle(OneSection());
        Deploy(battle, 1);
        Assert.True(battle.BeginWave());
        foreach (var unit in battle.State.Units.Where(u => u.IsEnemy || u.Deployed)) unit.Hp = 0;
        battle.Step();
        Assert.True(battle.State.Defeated);
        Assert.Equal("all-dead", battle.State.DefeatReason);
        Assert.False(battle.State.Victory);
        Assert.False(battle.State.WaveCompleted);
    }

    [Fact]
    public void ClearWinsAgainstTimeoutButTimeoutWithoutClearDefeats()
    {
        var clear = Battle(OneSection(20));
        Deploy(clear);
        Assert.True(clear.BeginWave());
        foreach (var enemy in clear.State.Units.Where(u => u.IsEnemy)) enemy.Hp = 0;
        clear.Step();
        Assert.True(clear.State.Victory);
        var timeout = Battle(OneSection(20));
        Deploy(timeout);
        Assert.True(timeout.BeginWave());
        timeout.Step();
        Assert.True(timeout.State.Defeated);
        Assert.Equal("time-limit", timeout.State.DefeatReason);
    }

    [Fact]
    public void StructuresRejectStatusesWhileGuardAcceptsAndOnlyGuardPaysKillResources()
    {
        var battle = Battle(OneSection());
        Deploy(battle);
        Assert.True(battle.BeginWave());
        var guard = battle.State.Units.First(u => u.Role == "guard");
        var structure = battle.State.Units.First(u => u.Role == "turret");
        Assert.False(battle.ApplyStatus(structure.Id, "burn", 10000, 1000, 20));
        Assert.Empty(structure.Statuses);
        Assert.True(battle.ApplyStatus(guard.Id, "burn", 10000, 1000, 20));
        var before = battle.State.Resources;
        battle.Step();
        Assert.Equal(before + guard.EnemyLevel * 2, battle.State.Resources);
        Assert.Equal(BigInteger.Zero, guard.Hp);
    }

    [Fact]
    public void SectionTransitionKeepsAbsoluteStatusClockAndCooldownProgress()
    {
        var battle = Battle();
        Deploy(battle);
        Assert.True(battle.BeginWave());
        for (var i = 0; i < 3; i++) battle.Step();
        var unit = battle.State.Units.First(u => !u.IsEnemy);
        Assert.True(battle.ApplyStatus(unit.Id, "slow", new Rational(1, 4), 2020));
        foreach (var enemy in battle.State.Units.Where(u => u.IsEnemy)) enemy.Hp = 0;
        battle.Step();
        var time = battle.State.TimeMs;
        var expires = Assert.Single(unit.Statuses).ExpiresAtMs;
        Assert.Equal((Rational)2000, expires - time);
        Assert.True(battle.BeginWave());
        Assert.Equal(time, battle.State.TimeMs);
        Assert.Equal(time, battle.State.SectionStartTimeMs);
        Assert.Equal((Rational)2000, Assert.Single(unit.Statuses).ExpiresAtMs - battle.State.TimeMs);
        Assert.Equal(300000, battle.RemainingSectionTimeMs);
    }

    [Fact]
    public void IncapacitatedLevelPurchaseIsRejectedAndRevivalClearsPendingStrengthening()
    {
        var battle = Battle();
        Deploy(battle);
        var unit = battle.State.Units.First(u => !u.IsEnemy);
        unit.Hp = 0; unit.Level = 2; unit.LevelExperience = 75; unit.PendingLevelChoices = 1;
        Assert.False(battle.PurchaseLevel(unit.Id));
        Assert.True(battle.GrantLevel(unit.Id, "vitality"));
        Assert.Equal(BigInteger.Zero, unit.Hp);
        unit.PendingLevelChoices = 1;
        Assert.True(battle.Revive(unit.Id));
        Assert.Equal(2, unit.Level);
        Assert.Equal(BigInteger.Zero, unit.LevelExperience);
        Assert.Equal(0, unit.PendingLevelChoices);
        Assert.Equal(new BigInteger(20), unit.Hp);
    }

    [Fact]
    public void RestoringMidCombatReproducesExactlyTheSameFurtherBattle()
    {
        var battle = Battle();
        Deploy(battle);
        Assert.True(battle.BeginWave());
        for (var i = 0; i < 100; i++) battle.Step();
        var restored = BattleSimulation.Restore(battle.Capture());
        for (var i = 0; i < 1000 && battle.State.Running; i++) { battle.Step(); restored.Step(); }
        Assert.Equal(JsonSerializer.Serialize(battle.Capture(), DomainJson.Options), JsonSerializer.Serialize(restored.Capture(), DomainJson.Options));
    }

    [Fact]
    public void InvalidSectionsAndUnreachableEquipmentFailBeforeStarting()
    {
        var content = OneSection();
        var stage = content.Stages[0];
        Assert.Contains((content with { Stages = [stage with { Sections = [] }] }).Validate(), e => e.Contains("missing invasion sections"));
        var section = stage.Sections![0];
        Assert.Contains((content with { Stages = [stage with { Sections = [section with { TimeLimitMs = 0 }] }] }).Validate(), e => e.Contains("invalid invasion section"));
        var unreachable = section.Enemies.Select(e => e.Role == "core" ? e with { X = 15, Y = 0 } : e).ToArray();
        Assert.Contains((content with { Stages = [stage with { Sections = [section with { Enemies = unreachable }] }] }).Validate(), e => e.Contains("unreachable invasion attack target"));
    }

    [Fact]
    public void FutureRulesAndCorruptSectionClockAreRejectedWithoutReinterpretingTheSave()
    {
        var snapshot = Battle().Capture();
        snapshot.RuleSetId = "invasion-v999";
        Assert.Throws<NotSupportedException>(() => BattleSimulation.Restore(snapshot));
        snapshot.RuleSetId = "invasion-v1";
        snapshot.SectionStartTimeMs = 100;
        Assert.Throws<InvalidDataException>(() => BattleSimulation.Restore(snapshot));
        snapshot.SectionStartTimeMs = 0;
        snapshot.Wave = 99;
        Assert.Throws<InvalidDataException>(() => BattleSimulation.Restore(snapshot));
    }

    private static (BattleSimulation Battle, CombatantState Source, CombatantState[] Targets) TargetFixture()
    {
        var battle = Battle(OneSection(), count: 1, power: 1);
        Deploy(battle, 1); Assert.True(battle.BeginWave());
        var source = battle.State.Units.Single(u => !u.IsEnemy);
        source.X = 8; source.Y = 5; source.MarchPathIndex = 8; source.MarchJoined = true;
        source.RangeCenti = 400; source.AttackReadyAtMs = long.MaxValue;
        foreach (var enemy in battle.State.Units.Where(u => u.IsEnemy))
        {
            enemy.X = 15; enemy.Y = 9; enemy.AttacksAdventurers = false;
            enemy.EnemySpeedCentiPerSecond = 0;
        }
        var guards = battle.State.Units.Where(u => u.Role == "guard").ToArray();
        var turret = battle.State.Units.First(u => u.Role == "turret");
        guards[0].X = 9; guards[0].Y = 5; guards[0].Hp = 100;
        guards[1].X = 10; guards[1].Y = 5; guards[1].Hp = 20;
        turret.X = 11; turret.Y = 5; turret.Hp = 50;
        return (battle, source, [guards[0], guards[1], turret]);
    }

    [Theory]
    [InlineData("nearest", 0)]
    [InlineData("low-hp", 1)]
    [InlineData("high-hp", 0)]
    [InlineData("structures", 2)]
    public void InvasionTargetPresetsChooseTheirIntendedEligibleOpponent(string preset, int expectedIndex)
    {
        var (battle, source, targets) = TargetFixture();
        Assert.True(battle.SetTarget(source.Id, preset));
        battle.Step();
        Assert.Equal(targets[expectedIndex].Id, source.CurrentTargetId);
    }

    [Fact]
    public void EqualHitPointsPreferDistanceThenStableGenerationOrder()
    {
        var (battle, source, targets) = TargetFixture();
        targets[0].Hp = targets[1].Hp = 10;
        targets[1].X = 8; targets[1].Y = 6;
        Assert.True(battle.SetTarget(source.Id, "low-hp"));
        battle.Step();
        Assert.Equal(targets[0].Id, source.CurrentTargetId);
        targets[0].X = 10;
        Assert.True(battle.SetTarget(source.Id, "low-hp"));
        battle.Step();
        Assert.Equal(targets[1].Id, source.CurrentTargetId);
    }

    [Fact]
    public void StructurePriorityDoesNotSelectEquipmentOutsideAcquisitionRange()
    {
        var (battle, source, targets) = TargetFixture();
        targets[2].X = 14;
        Assert.True(battle.SetTarget(source.Id, "structures"));
        battle.Step();
        Assert.Equal(targets[0].Id, source.CurrentTargetId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitialJoiningAndReturningToMarchFinishBeforeAcquiringNewAttackTargets(bool returning)
    {
        var (battle, source, targets) = TargetFixture();
        source.RangeCenti = 150;
        source.X = returning ? 5 : 2; source.Y = 4;
        source.MarchPathIndex = returning ? 4 : 2;
        source.MarchJoined = returning; source.ReturningToMarch = returning;
        source.IsEngaged = false;
        targets[0].X = 6; targets[0].Y = 4;
        static Rational Abs(Rational value) => value < 0 ? -value : value;
        var before = Abs(source.X - (returning ? 4 : 2)) + Abs(source.Y - 5);
        battle.Step();
        var after = Abs(source.X - (returning ? 4 : 2)) + Abs(source.Y - 5);
        Assert.True(after < before);
        Assert.Null(source.CurrentTargetId);
        Assert.False(source.IsEngaged);
    }

    [Fact]
    public void EnemyHitPointsMustRemainPositiveAfterIntegerConversion()
    {
        var content = OneSection();
        var stage = content.Stages[0];
        var section = stage.Sections![0];
        var invalid = section.Enemies.Select(e => e.Role == "core" ? e with { Hp = new Rational(1, 2) } : e).ToArray();
        Assert.Contains((content with { Stages = [stage with { Sections = [section with { Enemies = invalid }] }] }).Validate(),
            e => e.Contains("invalid invasion enemy values"));
    }
}

using System.Numerics;
using System.Text.Json;
using Guild.Domain.Battle;
using Guild.Domain.Definitions;
using Guild.Domain.Numerics;
using Guild.Domain.Serialization;

namespace Guild.Domain.Tests;

[Collection("Performance")]
public sealed class BattleTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper output;
    public BattleTests(Xunit.Abstractions.ITestOutputHelper output) => this.output = output;

    private static ContentBundle Fixture() => ContentBundle.Load(Path.Combine(AppContext.BaseDirectory, "tech-content.json"));

    private static BattleSimulation Create(int enemyCount = 1, int speedCenti = 20, Rational? hp = null,
        bool withSkills = true, int allies = 1, int waves = 1)
    {
        var content = Fixture();
        var first = content.Stages[0];
        content = content with
        {
            Stages = [first with
            {
                Waves = Enumerable.Range(1, waves).Select(number => new WaveDefinition
                {
                    Number = number, EnemyCount = enemyCount, EnemyHp = hp ?? 10000, EnemyPower = 5,
                    EnemyDefense = 0, EnemySpeedCentiPerSecond = speedCenti, SpawnIntervalMs = 20
                }).ToArray()
            }]
        };
        return new(new BattleSetup
        {
            MatchId = "match", AttemptId = "attempt", StageId = first.Id, Content = content, InitialResources = 10000,
            Units = Enumerable.Range(1, allies).Select(index => new BattleUnitSetup
            {
                Id = "ally-" + index, Name = "冒険者" + index, Stats = BattleStats.Standard,
                WeaponTemplateId = "tech-sword", SkillIds = withSkills ? ["tech-slash", "tech-slash"] : []
            }).ToList()
        });
    }

    [Fact]
    public void DuplicateSkillIdsHaveIndependentActualSlotCooldowns()
    {
        var simulation = Create();
        Assert.True(simulation.Deploy("ally-1", 1, 4));
        Assert.True(simulation.BeginWave());
        simulation.Step();
        var unit = simulation.Units.Single(u => !u.IsEnemy);
        Assert.Equal(["S1", "S2"], unit.Skills.Select(s => s.Slot));
        Assert.Equal(unit.Skills[0].SkillId, unit.Skills[1].SkillId);
        Assert.Equal(Rational.Zero, unit.Skills[0].Progress);
        Assert.Equal(Rational.One, unit.Skills[1].Progress);
        Assert.NotNull(unit.Action);
        Assert.Equal("S1", unit.Action.Slot);
    }

    [Fact]
    public void CooldownChangesAndOrdinaryWaveStartKeepProgress()
    {
        var simulation = Create();
        simulation.Deploy("ally-1", 1, 4);
        var unit = simulation.Units[0];
        unit.Skills[0].Progress = new Rational(1, 3);
        Assert.True(simulation.SetCooldownModifiers(unit.Id, "S1", new() { RecoveryDelta = 2 }));
        Assert.Equal(new Rational(1, 3), unit.Skills[0].Progress);
        Assert.Equal(new Rational(1000), unit.Skills[0].EffectiveCooldownMs);
        Assert.True(simulation.BeginWave());
        Assert.Equal(new Rational(1, 3), unit.Skills[0].Progress);
    }

    [Fact]
    public void SavedWaveStateRestoresTheSameFutureCombatAndRandomState()
    {
        var original = Create(enemyCount: 8, speedCenti: 35);
        original.Deploy("ally-1", 2, 4);
        original.BeginWave();
        for (var i = 0; i < 75; i++) original.Step();
        var json = JsonSerializer.Serialize(original.Capture(), DomainJson.Options);
        var restored = BattleSimulation.Restore(JsonSerializer.Deserialize<BattleSnapshot>(json, DomainJson.Options)!);
        for (var i = 0; i < 1000; i++) { original.Step(); restored.Step(); }
        Assert.Equal(JsonSerializer.Serialize(original.Capture(), DomainJson.Options),
            JsonSerializer.Serialize(restored.Capture(), DomainJson.Options));
    }

    [Fact]
    public void SmallMovementIsAccumulatedWithoutPerTickTruncation()
    {
        var simulation = Create(speedCenti: 1, withSkills: false);
        simulation.Deploy("ally-1", 14, 3);
        simulation.BeginWave();
        for (var i = 0; i < 25; i++) simulation.Step();
        Assert.Equal(new Rational(1, 200), simulation.Units.Single(u => u.IsEnemy).X);
    }

    [Fact]
    public void SegmentCollisionFindsTheFirstEnemyEvenAcrossAnEntireTile()
    {
        Assert.Equal(new Rational(2, 5), CombatMath.SegmentCircleEntry(0, 0, 2, 0, 1, 0, new Rational(1, 5)));
        Assert.Null(CombatMath.SegmentCircleEntry(0, 0, 2, 0, 1, 1, new Rational(1, 5)));
        var simulation = Create(enemyCount: 2, speedCenti: 1, withSkills: false);
        simulation.Deploy("ally-1", 14, 3); simulation.BeginWave(); simulation.Step(); simulation.Step();
        var enemies = simulation.Units.Where(u => u.IsEnemy).OrderBy(u => u.SpawnSequence).ToArray();
        enemies[0].X = 1; enemies[1].X = 2;
        simulation.State.Projectiles.Add(new()
        {
            Id = "swept", Sequence = 1, X = 0, Y = 5, VelocityX = 200, VelocityY = 0,
            RemainingRange = 6, ExpiresAtMs = 10000, AttackSnapshot = 10, SourceId = "ally-1"
        });
        var firstHp = enemies[0].Hp; var secondHp = enemies[1].Hp;
        simulation.Step();
        Assert.True(enemies[0].Hp < firstHp);
        Assert.Equal(secondHp, enemies[1].Hp);
        Assert.Empty(simulation.Projectiles);
    }

    [Fact]
    public void BurnTicksFourTimesAndStillExpiresDuringStun()
    {
        var simulation = Create(speedCenti: 1, withSkills: false);
        simulation.Deploy("ally-1", 14, 3); simulation.BeginWave();
        var before = simulation.Units[0].Hp;
        Assert.True(simulation.ApplyStatus("ally-1", "burn", 8, 4000, 1000));
        Assert.True(simulation.ApplyStatus("ally-1", "stun", 0, 500));
        for (var i = 0; i < 200; i++) simulation.Step();
        Assert.Equal(before - 32, simulation.Units[0].Hp);
        Assert.Empty(simulation.Units[0].Statuses);
    }

    [Fact]
    public void SameStatusKeepsStrongestIntensityAndLongestRemainingDuration()
    {
        var simulation = Create(withSkills: false);
        simulation.Deploy("ally-1", 14, 3);
        simulation.ApplyStatus("ally-1", "slow", new Rational(1, 4), 3000);
        simulation.ApplyStatus("ally-1", "slow", new Rational(1, 2), 1000);
        var status = Assert.Single(simulation.Units[0].Statuses);
        Assert.Equal(new Rational(1, 2), status.Strength);
        Assert.Equal(new Rational(3000), status.ExpiresAtMs);
    }

    [Fact]
    public void RevivalKeepsLevelAndPersistentBonusesButResetsTemporaryState()
    {
        var simulation = Create();
        simulation.Deploy("ally-1", 1, 4);
        var unit = simulation.Units[0];
        unit.Level = 4; unit.LevelExperience = 23; unit.PendingLevelChoices = 1; unit.Hp = 0;
        unit.AttackMultiplier = new Rational(6, 5); unit.LevelAttackMultiplier = new Rational(29, 25);
        unit.Statuses.Add(new() { Kind = "burn", Strength = 8, ExpiresAtMs = 4000 });
        unit.Skills[0].Progress = new Rational(1, 5);
        simulation.State.RefundGuards.Add("already-applied");
        var resources = simulation.State.Resources;
        Assert.True(simulation.Revive(unit.Id));
        Assert.Equal(4, unit.Level); Assert.Equal(BigInteger.Zero, unit.LevelExperience);
        Assert.Equal(0, unit.PendingLevelChoices); Assert.Equal(resources - 100, simulation.State.Resources);
        Assert.Equal(new Rational(6, 5), unit.AttackMultiplier);
        Assert.Equal(Rational.One, unit.LevelAttackMultiplier);
        Assert.Equal(new Rational(unit.MaxHp, 10).Ceiling(), unit.Hp);
        Assert.Empty(unit.Statuses); Assert.Equal(Rational.One, unit.Skills[0].Progress);
        Assert.Contains("already-applied", simulation.State.RefundGuards);
    }

    [Fact]
    public void RefundIsLimitedToOneQuarterRemainingAndCannotRepeatOrComeFromDerivedHit()
    {
        var simulation = Create();
        var unit = simulation.Units[0];
        unit.Skills[0].Progress = new Rational(1, 2);
        Assert.False(simulation.TryRefundCooldown(unit.Id, "S1", "derived", 1, derived: true));
        Assert.True(simulation.TryRefundCooldown(unit.Id, "S1", "cast-1", 1));
        Assert.Equal(new Rational(5, 8), unit.Skills[0].Progress);
        Assert.False(simulation.TryRefundCooldown(unit.Id, "S1", "cast-1", 1));
        Assert.Equal(new Rational(5, 8), unit.Skills[0].Progress);
    }

    [Fact]
    public void ModifierMatchingIdAndSlotIsAppliedOnlyOnce()
    {
        var component = new SkillModifier { Id = "same-component", SkillId = "skill", Slot = "S1", PowerMultiplier = 2 };
        Assert.Equal(new Rational(2), CombatMath.SkillPower([component, component], "skill", "S1"));
        var attackOnly = component with { AttackOnly = true };
        Assert.Equal(Rational.One, CombatMath.SkillPower([attackOnly], "skill", "S1", healing: true));
    }

    [Fact]
    public void WaveExperienceIsIdempotentAndIncludesIncapacitatedDeployedUnitsOnly()
    {
        var simulation = Create(allies: 2);
        simulation.Deploy("ally-1", 1, 4);
        simulation.State.Wave = 1; simulation.State.WaveCompleted = true;
        simulation.Units[0].Hp = 0;
        simulation.GrantWaveExperience(); simulation.GrantWaveExperience();
        Assert.Equal(new BigInteger(50), simulation.Units[0].LevelExperience);
        Assert.Equal(BigInteger.Zero, simulation.Units[1].LevelExperience);
    }

    [Fact]
    public void PurchasePaysOnlyMissingExperienceAndRequiresOneOfSixBonuses()
    {
        var simulation = Create();
        simulation.Deploy("ally-1", 1, 4);
        var unit = simulation.Units[0]; unit.LevelExperience = 50;
        var before = simulation.State.Resources;
        Assert.True(simulation.PurchaseLevel(unit.Id));
        Assert.Equal(before - 150, simulation.State.Resources);
        Assert.Equal(2, unit.Level); Assert.Equal(1, unit.PendingLevelChoices);
        Assert.False(simulation.BeginWave());
        Assert.False(simulation.GrantLevel(unit.Id, "invalid"));
        Assert.True(simulation.GrantLevel(unit.Id, "vitality"));
        Assert.True(simulation.BeginWave());
    }

    [Fact]
    public void CompletedStagePaysWaveRewardAndDeclaresVictory()
    {
        var simulation = Create(hp: 1, speedCenti: 1, withSkills: false);
        simulation.Deploy("ally-1", 1, 4); simulation.BeginWave();
        var before = simulation.State.Resources;
        for (var i = 0; i < 500 && simulation.State.Running; i++) simulation.Step();
        Assert.True(simulation.State.Victory);
        Assert.Equal(before + 102, simulation.State.Resources);
        Assert.Equal(new BigInteger(50), simulation.Units[0].LevelExperience);
    }

    [Fact]
    public void IncomingDefinitionsCannotMutateStartedMatch()
    {
        var simulation = Create();
        var original = simulation.State.Setup.Content.ComputeHash();
        var snapshot = simulation.Capture();
        snapshot.Setup = snapshot.Setup with { Content = snapshot.Setup.Content with { Version = "new-content" } };
        Assert.Equal(original, simulation.State.Setup.Content.ComputeHash());
        Assert.NotEqual(snapshot.Setup.Content.ComputeHash(), simulation.State.Setup.Content.ComputeHash());
    }

    [Fact]
    public void UnknownSimulationVersionIsRejectedWithoutImplicitConversion()
    {
        var snapshot = Create().Capture(); snapshot.SimulationVersion++;
        Assert.Throws<NotSupportedException>(() => BattleSimulation.Restore(snapshot));
    }

    [Fact]
    public void PlacementConsumesTheProvidedCostExactlyOnceAndRejectsNegativeCost()
    {
        var simulation = Create(withSkills: false);
        var before = simulation.State.Resources;
        Assert.False(simulation.Deploy("ally-1", 1, 4, -1));
        Assert.Equal(before, simulation.State.Resources);
        Assert.True(simulation.Deploy("ally-1", 1, 4, 50));
        Assert.Equal(before - 50, simulation.State.Resources);
        Assert.False(simulation.Deploy("ally-1", 2, 4, 50));
        Assert.Equal(before - 50, simulation.State.Resources);
        Assert.Empty(simulation.Units[0].Skills);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyEnemiesWithTheAttackBehaviorCanAttackAdventurers(bool attacks)
    {
        var simulation = Create(withSkills: false);
        simulation.Deploy("ally-1", 14, 3); simulation.BeginWave(); simulation.Step();
        var ally = simulation.Units.Single(u => !u.IsEnemy);
        var enemy = simulation.Units.Single(u => u.IsEnemy);
        enemy.X = 14; enemy.Y = 3; enemy.EnemySpeedCentiPerSecond = 0; enemy.AttacksAdventurers = attacks;
        var before = ally.Hp;
        for (var i = 0; i < 50; i++) simulation.Step();
        Assert.Equal(attacks, ally.Hp < before);
    }

    [Fact]
    public void PreparedPermanentValuesStaySeparateFromTemporaryModifiers()
    {
        var simulation = Create(); var unit = simulation.Units[0];
        unit.PreparedMaxHp = 600; unit.PreparedAttackPower = 100;
        unit.MaxHpMultiplier = new Rational(19, 20); unit.LevelMaxHpMultiplier = new Rational(11, 10);
        simulation.RecalculateUnit(unit.Id);
        Assert.Equal(new BigInteger(630), unit.MaxHp);
        Assert.Equal(new Rational(100), CombatMath.Attack(unit));
    }

    [Fact]
    public void NormalAttackCancelsAnInvalidTargetWithoutRetargetingItsWindup()
    {
        var simulation = Create(withSkills: false);
        simulation.Deploy("ally-1", 1, 4); simulation.BeginWave(); simulation.Step();
        var ally = simulation.Units.Single(u => !u.IsEnemy);
        Assert.NotNull(ally.Action);
        var enemy = simulation.Units.Single(u => u.IsEnemy); enemy.X = 10;
        simulation.Step();
        Assert.Null(ally.Action);
        Assert.True(ally.HardLockUntilMs <= simulation.State.TimeMs);
        Assert.True(ally.AttackReadyAtMs > simulation.State.TimeMs);
    }

    [Fact]
    public void NormalProjectileSnapshotsOffenseAtReleaseAndDefenseAtImpact()
    {
        var simulation = Create(withSkills: false, speedCenti: 1);
        simulation.Deploy("ally-1", 1, 4); simulation.BeginWave(); simulation.Step();
        var ally = simulation.Units.Single(u => !u.IsEnemy);
        var enemy = simulation.Units.Single(u => u.IsEnemy);
        ally.ProjectileSpeedCentiPerSecond = 600;
        ally.AttackMultiplier = 2;
        var launchedPower = CombatMath.Attack(ally);
        for (var i = 0; i < 10; i++) simulation.Step();
        var projectile = Assert.Single(simulation.Projectiles);
        Assert.Equal(launchedPower, projectile.AttackSnapshot);
        projectile.CritChance = 0;
        ally.AttackMultiplier = 3;
        enemy.PhysicalDefense = 100;
        var before = enemy.Hp;
        for (var i = 0; i < 20; i++) simulation.Step();
        Assert.Equal(before - CombatMath.Damage(launchedPower, 100, false), enemy.Hp);
        Assert.Empty(simulation.Projectiles);
    }

    [Fact]
    public void MeasuresTenAdventurersTwoHundredEnemiesAndFourHundredProjectiles()
    {
        var result = BattleBenchmark.Measure(Fixture(), 100);
        Assert.Equal(10, result.InitialAdventurers);
        Assert.Equal(200, result.InitialEnemies);
        Assert.Equal(400, result.InitialProjectiles);
        Assert.True(result.ElapsedMilliseconds > 0);
        output.WriteLine($"LOAD_FIXTURE ticks={result.Ticks}, elapsedMs={result.ElapsedMilliseconds:F3}, msPerTick={result.MillisecondsPerTick:F3}");
        foreach (var phase in result.PhaseMilliseconds!) output.WriteLine($"PHASE {phase.Key}={phase.Value:F3}ms");
    }
}

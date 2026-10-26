using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Guild.Domain.Battle;
using Guild.Domain.Definitions;
using Guild.Domain.Numerics;
using Guild.Domain.Serialization;

namespace Guild.Domain.Tests;

public sealed class DefenseRangeV2Tests
{
    private static ContentBundle Fixture() => ContentBundle.Load(Path.Combine(AppContext.BaseDirectory, "tech-content.json"));

    private static (BattleSimulation Battle, CombatantState Enemy, CombatantState[] Allies) Scenario(
        string? rule = "defense-v2", int allyCount = 1, IReadOnlyList<GridPoint>? path = null, bool attacks = true)
    {
        var content = Fixture();
        var original = content.Stages.First(s => s.Mode != "invasion");
        var stage = original with
        {
            Path = path ?? original.Path,
            Waves = [new WaveDefinition { Number = 1, EnemyCount = 1, EnemyHp = 10000, EnemyPower = 5,
                EnemyDefense = 0, EnemySpeedCentiPerSecond = 90, SpawnIntervalMs = 900, AttacksAdventurers = attacks }]
        };
        var battle = new BattleSimulation(new BattleSetup
        {
            Content = content with { Stages = [stage] }, StageId = stage.Id,
            MatchId = "range-v2", AttemptId = "attempt-1", InitialResources = 10000,
            Units = Enumerable.Range(0, allyCount).Select(i => new BattleUnitSetup
            {
                Id = "a" + i, Name = "対象" + i, WeaponTemplateId = "tech-bow", SkillIds = [],
                Stats = BattleStats.Standard
            }).ToList()
        });
        Assert.Equal("defense-v2", battle.State.RuleSetId);
        if (rule != "defense-v2")
        {
            var snapshot = battle.Capture(); snapshot.RuleSetId = rule;
            battle = BattleSimulation.Restore(snapshot);
        }
        for (var i = 0; i < allyCount; i++) Assert.True(battle.Deploy("a" + i, 8 + i, 4));
        Assert.True(battle.BeginWave());
        var allies = battle.State.Units.Where(u => !u.IsEnemy).ToArray();
        foreach (var ally in allies)
        {
            ally.PreparedMoveCentiPerSecond = 0;
            ally.AttackReadyAtMs = long.MaxValue;
        }
        var enemy = battle.State.Units.Single(u => u.IsEnemy);
        enemy.X = new Rational(799, 100); enemy.Y = 5; enemy.PathIndex = 8;
        return (battle, enemy, allies);
    }

    [Fact]
    public void OnlyNewDefenseSelectsV2WhileMissingSnapshotRuleRemainsLegacy()
    {
        Assert.Null(new BattleSnapshot().RuleSetId);
        var (battle, _, _) = Scenario();
        Assert.Equal("defense-v2", battle.Capture().RuleSetId);
        var snapshot = battle.Capture(); snapshot.RuleSetId = null;
        Assert.Null(BattleSimulation.Restore(snapshot).Capture().RuleSetId);
        snapshot.RuleSetId = "defense-v1";
        Assert.Equal("defense-v1", BattleSimulation.Restore(snapshot).Capture().RuleSetId);
        snapshot.RuleSetId = "defense-v999";
        Assert.Throws<NotSupportedException>(() => BattleSimulation.Restore(snapshot));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    public void TangentCrossingStopsAndStartsAnAttackWithoutIncreasingRange(int x)
    {
        var (battle, enemy, allies) = Scenario();
        allies[0].X = x; enemy.X = x - new Rational(1, 100); enemy.PathIndex = x;
        var hp = allies[0].Hp;
        battle.Step();
        Assert.Equal((Rational)x, enemy.X); Assert.Equal((Rational)5, enemy.Y);
        Assert.Equal(100, enemy.RangeCenti);
        Assert.Equal(x + 1, enemy.PathIndex);
        Assert.NotNull(enemy.Action); Assert.Equal("attack", enemy.Action.Kind);
        Assert.Equal((Rational)220, enemy.Action.ReleaseAtMs);
        Assert.Equal(hp, allies[0].Hp);
        for (var i = 0; i < 10; i++) battle.Step();
        Assert.Equal(hp - 4, allies[0].Hp);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("defense-v1")]
    public void LegacyPointRulesKeepTheirOriginalTangentMiss(string? rule)
    {
        var (battle, enemy, _) = Scenario(rule);
        battle.Step();
        Assert.Equal(new Rational(1001, 125), enemy.X); // 7.99 + 0.018
        Assert.Null(enemy.Action);
        Assert.Equal(rule, battle.State.RuleSetId);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void OneMillionthOutsideIsRejectedWhileBoundaryAndInsideAreAccepted(int offset, bool contact)
    {
        var (battle, enemy, allies) = Scenario();
        allies[0].Y = 4 + new Rational(offset, 1000000);
        battle.Step();
        Assert.Equal(contact, enemy.Action is not null);
        if (contact)
            Assert.True(CombatMath.DistanceSquared(enemy.X, enemy.Y, allies[0].X, allies[0].Y) <= 1);
        else Assert.Equal(new Rational(1001, 125), enemy.X);
    }

    [Fact]
    public void BothEndpointsOutsideStillDetectsTheInteriorCrossing()
    {
        var (battle, enemy, allies) = Scenario();
        enemy.X = 8; enemy.PathIndex = 9; enemy.EnemySpeedCentiPerSecond = 5000;
        allies[0].X = new Rational(17, 2); allies[0].Y = new Rational(401, 100);
        Assert.True(CombatMath.DistanceSquared(8, 5, allies[0].X, allies[0].Y) > 1);
        Assert.True(CombatMath.DistanceSquared(9, 5, allies[0].X, allies[0].Y) > 1);
        battle.Step();
        Assert.True(enemy.X > 8 && enemy.X < 9);
        Assert.True(CombatMath.DistanceSquared(enemy.X, enemy.Y, allies[0].X, allies[0].Y) <= 1);
        Assert.NotNull(enemy.Action);
    }

    [Fact]
    public void ExtremelyShortTangentUsesAnExactInRangeStop()
    {
        var (battle, enemy, allies) = Scenario();
        enemy.X = 8 - new Rational(1, 1000000000);
        enemy.EnemySpeedCentiPerSecond = new Rational(1, 100000);
        battle.Step();
        Assert.Equal((Rational)8, enemy.X);
        Assert.Equal((Rational)1, CombatMath.DistanceSquared(enemy.X, enemy.Y, allies[0].X, allies[0].Y));
        Assert.NotNull(enemy.Action);
    }

    [Fact]
    public void ReverseTraversalAlsoStopsAtTheContact()
    {
        var path = Enumerable.Range(0, 16).Reverse().Select(x => new GridPoint(x, 5)).ToArray();
        var (battle, enemy, _) = Scenario(path: path);
        enemy.X = new Rational(801, 100); enemy.PathIndex = 7;
        battle.Step();
        Assert.Equal((Rational)8, enemy.X);
        Assert.Equal(8, enemy.PathIndex);
        Assert.NotNull(enemy.Action);
    }

    [Fact]
    public void ContactOnTheFirstCornerEdgeDiscardsRemainingMovement()
    {
        var path = Enumerable.Range(0, 9).Select(x => new GridPoint(x, 5)).Concat([new GridPoint(8, 6), new(9, 6)]).ToArray();
        var (battle, enemy, _) = Scenario(path: path);
        enemy.EnemySpeedCentiPerSecond = 10000;
        battle.Step();
        Assert.Equal((Rational)8, enemy.X); Assert.Equal((Rational)5, enemy.Y);
        Assert.Equal(9, enemy.PathIndex);
    }

    [Fact]
    public void ACornerDoesNotUseAnArtificialDiagonalContactSegment()
    {
        var path = Enumerable.Range(0, 9).Select(x => new GridPoint(x, 5)).Concat([new GridPoint(8, 6), new(9, 6)]).ToArray();
        var (battle, enemy, allies) = Scenario(path: path);
        enemy.X = 7; enemy.PathIndex = 8; enemy.RangeCenti = 20; enemy.EnemySpeedCentiPerSecond = 10000;
        allies[0].X = new Rational(15, 2); allies[0].Y = new Rational(11, 2);
        battle.Step();
        Assert.Equal((Rational)8, enemy.X); Assert.Equal((Rational)6, enemy.Y);
        Assert.Null(enemy.Action);
    }

    [Fact]
    public void TheFirstEligibleEdgeWinsBeforeALaterTargetAlongThePath()
    {
        var (battle, enemy, allies) = Scenario(allyCount: 2);
        allies[1].X = 9;
        enemy.EnemySpeedCentiPerSecond = 10000;
        battle.Step();
        Assert.Equal((Rational)8, enemy.X);
        Assert.Equal(allies[0].Id, Assert.Single(enemy.Action!.TargetIds));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualContactKeepsAnExistingValidTargetOtherwiseUsesGenerationOrder(bool existingTarget)
    {
        var (battle, enemy, allies) = Scenario(allyCount: 2);
        allies[1].X = 8; allies[1].Y = 6;
        if (existingTarget) enemy.CurrentTargetId = allies[1].Id;
        battle.Step();
        Assert.Equal((Rational)8, enemy.X);
        Assert.Equal(allies[existingTarget ? 1 : 0].Id, Assert.Single(enemy.Action!.TargetIds));
    }

    [Theory]
    [InlineData("incapacitated")]
    [InlineData("undeployed")]
    [InlineData("same-faction")]
    public void IneligibleUnitsDoNotStopTheMovingEnemy(string excluded)
    {
        var (battle, enemy, allies) = Scenario(allyCount: 2);
        allies[1].X = 14; allies[1].Y = 3;
        if (excluded == "incapacitated") allies[0].Hp = 0;
        if (excluded == "undeployed") allies[0].Deployed = false;
        if (excluded == "same-faction")
        {
            allies[0].IsEnemy = true;
            allies[0].HardLockUntilMs = long.MaxValue;
        }
        battle.Step();
        Assert.Equal(new Rational(1001, 125), enemy.X);
        Assert.Null(enemy.Action);
    }

    [Theory]
    [InlineData("stun")]
    [InlineData("hardlock")]
    [InlineData("action")]
    public void StunHardlockAndExistingActionsKeepTheirOriginalMovementPriority(string reason)
    {
        var (battle, enemy, allies) = Scenario();
        if (reason == "stun") Assert.True(battle.ApplyStatus(enemy.Id, "stun", 0, 500));
        if (reason == "hardlock") enemy.HardLockUntilMs = 500;
        if (reason == "action") enemy.Action = new() { Kind = "skill", SkillId = "tech-slash", ReleaseAtMs = 1000, TargetIds = [allies[0].Id] };
        battle.Step();
        Assert.Equal(new Rational(799, 100), enemy.X);
    }

    [Fact]
    public void NonAttackingWavesDoNotAcquireTheNewStopBehavior()
    {
        var (battle, enemy, _) = Scenario(attacks: false);
        battle.Step();
        Assert.Equal(new Rational(1001, 125), enemy.X);
        Assert.Null(enemy.Action);
    }

    [Fact]
    public void AnAlreadyInRangeTargetStopsWithoutAdditionalMovement()
    {
        var (battle, enemy, _) = Scenario();
        enemy.X = 8;
        battle.Step();
        Assert.Equal((Rational)8, enemy.X);
        Assert.NotNull(enemy.Action);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATargetKilledAfterMovementOrLeavingDuringWindupIsNotGuaranteedDamage(bool leaves)
    {
        var (battle, enemy, allies) = Scenario();
        if (!leaves)
            battle.State.Projectiles.Add(new ProjectileState { Id = "kill", Sequence = 1, SourceId = allies[0].Id,
                IsEnemy = true, X = 8, Y = 4, RemainingRange = 1, Speed = 1, ExpiresAtMs = 1000, AttackSnapshot = 10000 });
        battle.Step();
        if (!leaves)
        {
            Assert.Equal(BigInteger.Zero, allies[0].Hp); Assert.Null(enemy.Action);
            battle.Step(); Assert.True(enemy.X > 8);
        }
        else
        {
            var hp = allies[0].Hp;
            Assert.NotNull(enemy.Action);
            allies[0].Y = 3;
            battle.Step();
            Assert.Null(enemy.Action); Assert.Equal(hp, allies[0].Hp);
        }
    }

    [Fact]
    public void EndOfPathArrivalStillPrecedesStartingANewAttack()
    {
        var (battle, enemy, allies) = Scenario();
        allies[0].X = 15;
        enemy.X = new Rational(1499, 100); enemy.PathIndex = 15;
        var defense = battle.State.DefenseHp;
        battle.Step();
        Assert.Equal(defense - 1, battle.State.DefenseHp);
        Assert.Null(enemy.Action);
        Assert.Equal(new BigInteger(200), allies[0].Hp);
    }

    [Fact]
    public void V2SnapshotReplayKeepsContactTimersRandomnessAndPositions()
    {
        var (battle, _, _) = Scenario();
        battle.Step();
        var restored = BattleSimulation.Restore(battle.Capture());
        for (var i = 0; i < 500; i++) { battle.Step(); restored.Step(); }
        Assert.Equal("defense-v2", restored.State.RuleSetId);
        Assert.Equal(JsonSerializer.Serialize(battle.Capture(), DomainJson.Options), JsonSerializer.Serialize(restored.Capture(), DomainJson.Options));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("defense-v1")]
    public void PublishedV030GoldenRetainsEveryLegacyFieldAcrossAllTenWaves(string? rule)
    {
        // Generated with the published 0.3.0+b7da6871f44f12807a879f92434b63ec6c9f4e65
        // assemblies, using six newly initialized test adventurers, never player saves.
        // The released Domain selected firepower for each pending natural level.
        // Do not regenerate this expectation with the implementation under test.
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "v0.3.0");
        using var session = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "battle-session.json")));
        var snapshot = JsonSerializer.Deserialize<BattleSnapshot>(session.RootElement.GetProperty("waveStart"), DomainJson.Options)!;
        Assert.Equal("5cb04688d790554e6478309b40d7b13c796c228ecde7cf0991926cdcea55ed1d", snapshot.Setup.Content.ComputeHash());
        snapshot.RuleSetId = rule;
        var battle = BattleSimulation.Restore(snapshot);
        for (var ticks = 0; !battle.State.Victory && !battle.State.Defeated && ticks < 30000; ticks++)
        {
            if (!battle.State.Running)
            {
                foreach (var ally in battle.State.Units.Where(u => !u.IsEnemy))
                    while (ally.PendingLevelChoices > 0) Assert.True(battle.GrantLevel(ally.Id, "firepower"));
                Assert.True(battle.BeginWave());
            }
            battle.Step();
        }
        Assert.True(battle.State.Victory); Assert.Equal(10, battle.State.Wave);
        Assert.Equal(80160, battle.State.TimeMs); Assert.Equal(20, battle.State.DefenseHp);
        var expectedNode = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "battle-golden.json")))!;
        // The explicit v1 alias changes only its identifier, not any recorded physics.
        expectedNode["ruleSetId"] = rule;
        using var expected = JsonDocument.Parse(expectedNode.ToJsonString());
        using var actual = JsonDocument.Parse(JsonSerializer.Serialize(battle.Capture(), DomainJson.Options));
        EqualLegacyFields(expected.RootElement, actual.RootElement, "snapshot");
    }

    private static void EqualLegacyFields(JsonElement expected, JsonElement actual, string path)
    {
        Assert.True(expected.ValueKind == actual.ValueKind, path + ": JSON kind changed.");
        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in expected.EnumerateObject())
                {
                    Assert.True(actual.TryGetProperty(property.Name, out var value), path + "." + property.Name + ": missing legacy field.");
                    EqualLegacyFields(property.Value, value, path + "." + property.Name);
                }
                break;
            case JsonValueKind.Array:
                Assert.Equal(expected.GetArrayLength(), actual.GetArrayLength());
                for (var i = 0; i < expected.GetArrayLength(); i++) EqualLegacyFields(expected[i], actual[i], path + "[" + i + "]");
                break;
            default:
                Assert.True(expected.ToString() == actual.ToString(), path + ": public legacy value changed.");
                break;
        }
    }
}

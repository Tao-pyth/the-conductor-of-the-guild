using System.Diagnostics;
using Guild.Domain.Battle;
using Guild.Domain.Definitions;
using Guild.Domain.Numerics;

namespace Guild.Domain.Tests;

[CollectionDefinition("Performance", DisableParallelization = true)]
public sealed class PerformanceCollection;

[Collection("Performance")]
public sealed class CooldownLoadTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void RepeatedFractionalCooldownChangesKeepExactProgressAndCanBeMeasured()
    {
        var content = ContentBundle.Load(Path.Combine(AppContext.BaseDirectory, "tech-content.json"));
        var stage = content.Stages[0];
        content = content with { Stages = [stage with { Waves = [stage.Waves[0] with
            { EnemyCount = 1, EnemyHp = 1_000_000, EnemySpeedCentiPerSecond = 1 }] }] };
        var simulation = new BattleSimulation(new()
        {
            MatchId = "cooldown-load", AttemptId = "cooldown-load-attempt", Content = content, StageId = stage.Id,
            Units = [new() { Id = "ally", Name = "技能負荷検証", WeaponTemplateId = "tech-sword", SkillIds = ["tech-slash", "tech-slash"] }]
        });
        Assert.True(simulation.Deploy("ally", 1, 4)); Assert.True(simulation.BeginWave());
        var unit = simulation.Units.Single(x => !x.IsEnemy);
        var watch = Stopwatch.StartNew();
        long maximumBits = 0;
        for (var i = 0; i < 1000; i++)
        {
            var before = unit.Skills[0].Progress;
            Assert.True(simulation.SetCooldownModifiers("ally", "S1", new() { FixedExtensionMs = new Rational(i, 17) }));
            Assert.Equal(before, unit.Skills[0].Progress);
            simulation.Step();
            maximumBits = Math.Max(maximumBits, unit.Skills[0].Progress.Denominator.GetBitLength());
            Assert.True(unit.Skills[0].Progress >= Rational.Zero && unit.Skills[0].Progress <= Rational.One);
        }
        watch.Stop();
        Assert.True(simulation.State.Running);
        var restored = BattleSimulation.Restore(simulation.Capture());
        Assert.Equal(unit.Skills[0].Progress, restored.Units.Single(x => !x.IsEnemy).Skills[0].Progress);
        output.WriteLine($"COOLDOWN_LOAD changes=1000 elapsedMs={watch.Elapsed.TotalMilliseconds:F3} maximumDenominatorBits={maximumBits}");
    }
}

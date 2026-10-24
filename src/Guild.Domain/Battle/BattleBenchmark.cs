using System.Diagnostics;
using Guild.Domain.Definitions;
using Guild.Domain.Numerics;

namespace Guild.Domain.Battle;

public sealed record BattleBenchmarkResult(int Ticks, int InitialAdventurers, int InitialEnemies,
    int InitialProjectiles, double ElapsedMilliseconds, double MillisecondsPerTick,
    IReadOnlyDictionary<string, double>? PhaseMilliseconds = null);

public static class BattleBenchmark
{
    /// <summary>A reproducible load fixture, not a limit on game content or a frame-rate guarantee.</summary>
    public static BattleBenchmarkResult Measure(ContentBundle content, int ticks = 100)
    {
        if (ticks < 1) throw new ArgumentOutOfRangeException(nameof(ticks));
        var stage = content.Stages[0];
        var fixture = content with
        {
            Stages = [stage with
            {
                Waves = [new WaveDefinition
                {
                    Number = 1, EnemyCount = 200, EnemyHp = 1_000_000, EnemyDefense = 10,
                    EnemyPower = 1, EnemySpeedCentiPerSecond = 1, SpawnIntervalMs = 20
                }]
            }]
        };
        var simulation = new BattleSimulation(new()
        {
            MatchId = "benchmark", AttemptId = "benchmark-attempt", Content = fixture, StageId = stage.Id,
            InitialResources = 10000,
            Units = Enumerable.Range(0, 10).Select(i => new BattleUnitSetup
            {
                Id = "benchmark-ally-" + i, Name = "負荷検証", Stats = BattleStats.Standard,
                WeaponTemplateId = content.WeaponTemplates[0].Id, SkillIds = []
            }).ToList()
        });
        for (var i = 0; i < 10; i++) simulation.Deploy("benchmark-ally-" + i, i + 2, 3);
        simulation.BeginWave();
        simulation.State.Units.RemoveAll(unit => unit.IsEnemy);
        simulation.State.SpawnedInWave = 200;
        for (var i = 0; i < 200; i++)
        {
            simulation.State.Units.Add(new CombatantState
            {
                Id = "benchmark-enemy-" + i, Name = "負荷検証敵", IsEnemy = true,
                SpawnSequence = simulation.State.NextEntitySequence++, Hp = 1_000_000, MaxHp = 1_000_000,
                Stats = new(0, 0, 0, 0, 0), X = 1 + new Rational(i % 140, 10), Y = 5,
                PathIndex = 1 + (i % 140) / 10 + 1, EnemySpeedCentiPerSecond = 1,
                Deployed = true, PhysicalDefense = 10, MagicalDefense = 10
            });
        }
        // Projectiles cross empty cells: this deliberately measures the broad-phase
        // workload while keeping all 400 active for the entire sample interval.
        for (var i = 0; i < 400; i++) simulation.State.Projectiles.Add(new()
        {
            Id = "benchmark-projectile-" + i, Sequence = i, X = new Rational(i % 140, 10), Y = new Rational(3, 2),
            VelocityX = new Rational(1, 10), VelocityY = 0, RemainingRange = 30,
            ExpiresAtMs = 60000, AttackSnapshot = 20, SourceId = "benchmark-ally-0"
        });
        // Warm the same methods before timing; restore the initial snapshot so the
        // measured sample still starts with exactly the advertised entity counts.
        var initial = simulation.Capture();
        for (var i = 0; i < 20; i++) simulation.Step();
        simulation = BattleSimulation.Restore(initial);
        var timer = Stopwatch.StartNew();
        simulation.MeasurePhases = true;
        for (var i = 0; i < ticks; i++) simulation.Step();
        timer.Stop();
        return new(ticks, initial.Units.Count(unit => !unit.IsEnemy), initial.Units.Count(unit => unit.IsEnemy),
            initial.Projectiles.Count, timer.Elapsed.TotalMilliseconds, timer.Elapsed.TotalMilliseconds / ticks,
            simulation.PhaseElapsedTimestamps.ToDictionary(p => p.Key, p => p.Value * 1000.0 / Stopwatch.Frequency));
    }
}

using System.Numerics;
using Guild.Domain.Definitions;
using Guild.Domain.Numerics;

namespace Guild.Domain.Battle;

public sealed partial class BattleSimulation
{
    private readonly Dictionary<(long Unit, Rational X, Rational Y, Rational CenterX, Rational CenterY, bool Restricted), ReachableCells> invasionReachableCache = [];
    private static bool IsStructure(CombatantState unit) => unit.Role is "turret" or "gate" or "core";

    private bool ActionTargetAllowed(CombatantState source, CombatantState target)
        => IsInvasion ? InvasionWithinCenter(source, source.X, source.Y)
            : source.IsEnemy || WithinAnchor(source, target.X, target.Y);

    private int NearestPathPoint(Rational x, Rational y) => Enumerable.Range(0, stage.Path.Count)
        .OrderBy(i => CombatMath.DistanceSquared(x, y, stage.Path[i].X, stage.Path[i].Y)).ThenBy(i => i).First();

    private void SetInvasionDeployment(CombatantState unit, int x, int y)
    {
        unit.DeploymentX = unit.AnchorX = x; unit.DeploymentY = unit.AnchorY = y;
        unit.X = x; unit.Y = y;
        unit.MarchPathIndex = NearestPathPoint(x, y);
        var point = stage.Path[unit.MarchPathIndex];
        unit.MarchJoined = x == point.X && y == point.Y;
        unit.IsEngaged = false; unit.ReturningToMarch = false; unit.EngagementX = x; unit.EngagementY = y;
        unit.CurrentTargetId = null; unit.Action = null;
    }

    private bool BeginInvasionSection()
    {
        if (State.Running || State.Defeated || State.Victory || State.Wave >= TotalWaves
            || !State.Units.Any(u => !u.IsEnemy && Active(u))
            || State.Units.Any(u => !u.IsEnemy && u.PendingLevelChoices > 0)) return false;
        State.Wave++;
        State.Running = true; State.WaveCompleted = false; State.DefeatReason = null;
        State.SectionStartTimeMs = State.TimeMs;
        State.Projectiles.Clear(); State.Units.RemoveAll(u => u.IsEnemy);
        foreach (var unit in State.Units.Where(u => u.Deployed))
        {
            SetInvasionDeployment(unit, unit.DeploymentX, unit.DeploymentY);
            unit.HardLockUntilMs = unit.AttackReadyAtMs = State.TimeMs;
        }
        foreach (var initial in stage.Sections![State.Wave - 1].Enemies)
        {
            var sequence = State.NextEntitySequence++;
            State.Units.Add(new CombatantState
            {
                Id = initial.Id, Name = initial.Name, Role = initial.Role, IsEnemy = true,
                SpawnSequence = sequence, Stats = new(0, 0, 0, 0, 0), WeaponType = "enemy",
                BasePower = initial.Power, BaseAttackIntervalMs = initial.AttackIntervalMs,
                RangeCenti = initial.RangeCenti, ProjectileSpeedCentiPerSecond = initial.ProjectileSpeedCentiPerSecond,
                EnemySpeedCentiPerSecond = initial.MoveSpeedCentiPerSecond,
                PhysicalDefense = initial.Defense, MagicalDefense = initial.Defense,
                Hp = initial.Hp.Floor(), MaxHp = initial.Hp.Floor(), EnemyLevel = initial.Level,
                X = initial.X, Y = initial.Y, AnchorX = initial.X, AnchorY = initial.Y,
                PathIndex = NearestPathPoint(initial.X, initial.Y), Deployed = true,
                AttacksAdventurers = initial.Role is "guard" or "turret", TargetPreset = "nearest"
            });
        }
        State.SpawnedInWave = stage.Sections[State.Wave - 1].Enemies.Count;
        Emit("section-start", "", "", $"区画 {State.Wave}：{stage.Sections[State.Wave - 1].Name}", 0);
        return true;
    }

    private void StepInvasion()
    {
        if (!State.Running) return;
        invasionReachableCache.Clear();
        State.TimeMs = checked(State.TimeMs + TickMs);
        foreach (var unit in State.Units.OrderBy(u => u.SpawnSequence))
        {
            if (!Active(unit)) continue;
            AdvanceCooldowns(unit); MoveInvasion(unit);
        }
        AdvanceProjectiles(); ResolveStatuses(); ResolveActions();
        foreach (var unit in State.Units.OrderBy(u => u.SpawnSequence))
            if (Active(unit)) ChooseAction(unit);
        ResolveActions();

        // Finish the complete fixed update before judging mutually simultaneous results.
        if (!State.Units.Any(u => !u.IsEnemy && Active(u))) FailInvasion("all-dead", "出撃部隊が全滅しました。");
        else if (!State.Units.Any(u => u.IsEnemy && Active(u))) CompleteInvasionSection();
        else if (State.TimeMs - State.SectionStartTimeMs >= stage.Sections![State.Wave - 1].TimeLimitMs)
            FailInvasion("time-limit", "区画の制限時間を超えました。");
        State.Rng = random.Capture();
    }

    private void FailInvasion(string reason, string message)
    {
        State.Defeated = true; State.Running = false; State.DefeatReason = reason;
        Emit("defeat", "", "", message, 0);
    }

    private void CompleteInvasionSection()
    {
        State.Running = false; State.WaveCompleted = true;
        State.Resources += State.Wave * 100;
        State.Projectiles.Clear(); GrantWaveExperience();
        State.Victory = State.Wave >= TotalWaves;
        foreach (var unit in State.Units.Where(u => !u.IsEnemy && u.Deployed))
        {
            SetInvasionDeployment(unit, unit.DeploymentX, unit.DeploymentY);
            unit.HardLockUntilMs = unit.AttackReadyAtMs = State.TimeMs;
        }
        Emit(State.Victory ? "victory" : "section-clear", "", "",
            State.Victory ? "最終拠点の攻略に成功しました。" : $"区画 {State.Wave} 突破", State.Wave * 100);
    }

    private (Rational X, Rational Y) InvasionCenter(CombatantState unit)
        => unit.IsEnemy ? (unit.AnchorX, unit.AnchorY)
            : unit.IsEngaged ? (unit.EngagementX, unit.EngagementY) : (unit.X, unit.Y);

    private bool InvasionWithinCenter(CombatantState unit, Rational x, Rational y)
    {
        if (IsStructure(unit)) return true;
        var center = InvasionCenter(unit);
        return CombatMath.DistanceSquared(center.X, center.Y, x, y) <= 9;
    }

    private Rational InvasionMoveDistance(CombatantState unit)
    {
        var speed = unit.IsEnemy ? unit.EnemySpeedCentiPerSecond
            : (unit.PreparedMoveCentiPerSecond ?? (300 + new Rational(5) * unit.Stats.Agi))
                * Rational.Max(0, unit.MoveMultiplier + unit.LevelMoveMultiplier - 1);
        var slow = unit.Statuses.Where(s => s.Kind == "slow" && s.ExpiresAtMs > State.TimeMs)
            .Select(s => s.Strength).DefaultIfEmpty().Max();
        return speed * new Rational(TickMs, 100000) * Rational.Clamp(1 - slow, 0, 1);
    }

    private void MoveInvasion(CombatantState unit)
    {
        if (Stunned(unit) || unit.Action is not null || State.TimeMs < unit.HardLockUntilMs || IsStructure(unit)) return;
        var available = InvasionMoveDistance(unit);
        if (!unit.IsEnemy && (!unit.MarchJoined || unit.ReturningToMarch))
        {
            var joinPoint = stage.Path[unit.MarchPathIndex];
            available = FollowInvasionGrid(unit, joinPoint, available, false);
            if (unit.X != joinPoint.X || unit.Y != joinPoint.Y) return;
            unit.MarchJoined = true; unit.ReturningToMarch = false;
        }
        var target = unit.CurrentTargetId is null ? null : Unit(unit.CurrentTargetId);
        if (target is null || !Active(target) || !InRange(unit, target, 400)
            || (!InRange(unit, target, EffectiveRange(unit)) && FindInvasionAttackCell(unit, target, EffectiveRange(unit)) is null))
        {
            target = SelectInvasionTarget(unit);
            unit.CurrentTargetId = target?.Id;
        }
        if (target is not null)
        {
            if (!unit.IsEnemy && !unit.IsEngaged)
            {
                unit.IsEngaged = true; unit.EngagementX = unit.X; unit.EngagementY = unit.Y;
            }
            if (InRange(unit, target, EffectiveRange(unit))) return;
            var attackCell = FindInvasionAttackCell(unit, target, EffectiveRange(unit));
            if (attackCell is not null) FollowInvasionGrid(unit, attackCell, available, true);
            return;
        }
        if (unit.IsEnemy)
        {
            FollowInvasionGrid(unit, new(unit.AnchorX, unit.AnchorY), available, true);
            return;
        }
        if (unit.IsEngaged) { unit.IsEngaged = false; unit.ReturningToMarch = true; }
        var point = stage.Path[unit.MarchPathIndex];
        if (!unit.MarchJoined || unit.ReturningToMarch)
        {
            available = FollowInvasionGrid(unit, point, available, false);
            if (unit.X != point.X || unit.Y != point.Y) return;
            unit.MarchJoined = true;
            unit.ReturningToMarch = false;
        }
        var nextGoal = State.Units.Where(u => u.IsEnemy && Active(u)).Select(u => u.PathIndex)
            .DefaultIfEmpty(stage.Path.Count - 1).Min();
        while (available > 0 && unit.MarchPathIndex != nextGoal)
        {
            var nextIndex = unit.MarchPathIndex + Math.Sign(nextGoal - unit.MarchPathIndex);
            point = stage.Path[nextIndex];
            available = MoveAxisAligned(unit, point.X, point.Y, available);
            if (unit.X == point.X && unit.Y == point.Y) unit.MarchPathIndex = nextIndex;
            else break;
        }
    }

    private CombatantState? SelectInvasionTarget(CombatantState source, int? explicitRange = null)
    {
        if (source.Role is "gate" or "core"
            || (!source.IsEnemy && (!source.MarchJoined || source.ReturningToMarch))) return null;
        var range = explicitRange ?? (source.Role == "turret" ? source.RangeCenti : 400);
        range = Math.Min(400, range);
        var candidates = Opponents(source).Where(target => InRange(source, target, range)
            && (InRange(source, target, explicitRange ?? EffectiveRange(source))
                || FindInvasionAttackCell(source, target, explicitRange ?? EffectiveRange(source)) is not null));
        var preset = source.IsEnemy ? "nearest" : source.TargetPreset;
        return preset switch
        {
            "low-hp" => candidates.OrderBy(t => t.Hp).ThenBy(Distance).ThenBy(t => t.SpawnSequence).FirstOrDefault(),
            "high-hp" => candidates.OrderByDescending(t => t.Hp).ThenBy(Distance).ThenBy(t => t.SpawnSequence).FirstOrDefault(),
            "structures" => candidates.OrderBy(t => IsStructure(t) ? 0 : 1).ThenBy(Distance).ThenBy(t => t.SpawnSequence).FirstOrDefault(),
            _ => candidates.OrderBy(Distance).ThenBy(t => t.SpawnSequence).FirstOrDefault()
        };
        Rational Distance(CombatantState target) => CombatMath.DistanceSquared(source.X, source.Y, target.X, target.Y);
    }

    private GridPoint? FindInvasionAttackCell(CombatantState source, CombatantState target, int range)
    {
        var reachable = BuildInvasionReachable(source, true);
        var squaredRange = new Rational(range * (long)range, 10000);
        return reachable.Distances.Where(pair => CombatMath.DistanceSquared(pair.Key.X, pair.Key.Y, target.X, target.Y) <= squaredRange)
            .OrderBy(pair => pair.Value).ThenBy(pair => CombatMath.DistanceSquared(pair.Key.X, pair.Key.Y, target.X, target.Y))
            .ThenBy(pair => pair.Key.X).ThenBy(pair => pair.Key.Y).Select(pair => pair.Key).FirstOrDefault();
    }

    private sealed record ReachableCells(Dictionary<GridPoint, Rational> Distances, Dictionary<GridPoint, GridPoint?> Previous);

    private ReachableCells BuildInvasionReachable(CombatantState unit, bool restricted)
    {
        var center = InvasionCenter(unit);
        var cacheKey = (unit.SpawnSequence, unit.X, unit.Y, center.X, center.Y, restricted);
        if (invasionReachableCache.TryGetValue(cacheKey, out var cached)) return cached;
        bool Allowed(GridPoint p) => p.X >= 0 && p.X < stage.MapWidth && p.Y >= 0 && p.Y < stage.MapHeight
            && (!restricted || InvasionWithinCenter(unit, p.X, p.Y));
        var distance = new Dictionary<GridPoint, Rational>();
        var previous = new Dictionary<GridPoint, GridPoint?>();
        var queue = new PriorityQueue<GridPoint, (Rational Cost, long Sequence)>();
        long sequence = 0;
        var roots = new[]
        {
            new GridPoint((int)unit.X.Floor(), (int)unit.Y.Floor()),
            new GridPoint((int)unit.X.Ceiling(), (int)unit.Y.Ceiling())
        };
        foreach (var root in roots.Distinct())
        {
            if (!Allowed(root)) continue;
            var cost = Abs(root.X - unit.X) + Abs(root.Y - unit.Y);
            distance[root] = cost; previous[root] = null; queue.Enqueue(root, (cost, sequence++));
        }
        while (queue.TryDequeue(out var point, out var priority))
        {
            if (priority.Cost != distance[point]) continue;
            foreach (var next in new[] { new GridPoint(point.X + 1, point.Y), new(point.X, point.Y + 1), new(point.X - 1, point.Y), new(point.X, point.Y - 1) })
            {
                if (!Allowed(next)) continue;
                var cost = priority.Cost + 1;
                if (distance.TryGetValue(next, out var known) && known <= cost) continue;
                distance[next] = cost; previous[next] = point; queue.Enqueue(next, (cost, sequence++));
            }
        }
        var result = new ReachableCells(distance, previous);
        invasionReachableCache[cacheKey] = result;
        return result;
    }

    private Rational FollowInvasionGrid(CombatantState unit, GridPoint destination, Rational available, bool restricted)
    {
        while (available > 0 && (unit.X != destination.X || unit.Y != destination.Y))
        {
            var map = BuildInvasionReachable(unit, restricted);
            if (!map.Distances.ContainsKey(destination)) return 0;
            var next = destination;
            while (map.Previous[next] is { } predecessor && (unit.X != predecessor.X || unit.Y != predecessor.Y)) next = predecessor;
            var before = available;
            available = MoveAxisAligned(unit, next.X, next.Y, available);
            if (available == before) return 0;
        }
        return available;
    }
}

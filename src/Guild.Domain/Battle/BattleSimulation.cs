using System.Numerics;
using System.Diagnostics;
using System.Text.Json;
using Guild.Domain.Definitions;
using Guild.Domain.Numerics;
using Guild.Domain.Randomness;
using Guild.Domain.Serialization;

namespace Guild.Domain.Battle;

/// <summary>Authoritative, single-threaded combat independent of Godot and frame timing.</summary>
public sealed class BattleSimulation
{
    public const int SimulationVersion = 1;
    public const int TickMs = 20;
    private const int MaximumRecentEvents = 512;
    private readonly List<BattleEvent> events = [];
    private readonly Dictionary<string, SkillDefinition> skills;
    private readonly StageDefinition stage;
    private readonly Rational[] remainingPathDistance;
    private readonly Dictionary<long, Rational> arrivalCache = [];
    private readonly Dictionary<Rational, Rational> moveDistanceCache = [];
    private Pcg32 random;
    private long phaseStarted;
    public bool MeasurePhases { get; set; }
    public Dictionary<string, long> PhaseElapsedTimestamps { get; } = [];

    public BattleSnapshot State { get; }
    public IReadOnlyList<CombatantState> Units => State.Units;
    public IReadOnlyList<ProjectileState> Projectiles => State.Projectiles;
    public int TotalWaves => stage.Waves.Count;

    public BattleSimulation(BattleSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        setup.Content.EnsureValid();
        if (setup.Units.Count is < 1 or > 50) throw new ArgumentException("A battle requires an owned roster.");
        if (setup.Units.Select(u => u.Id).Distinct(StringComparer.Ordinal).Count() != setup.Units.Count)
            throw new ArgumentException("Adventurer IDs must be unique.");
        var frozen = Clone(setup);
        stage = frozen.Content.Stages.Single(s => s.Id == frozen.StageId);
        remainingPathDistance = BuildRemainingPathDistance(stage);
        skills = frozen.Content.Skills.ToDictionary(s => s.Id, StringComparer.Ordinal);
        random = new Pcg32(frozen.Seed, 1);
        State = new BattleSnapshot
        {
            Setup = frozen, Resources = frozen.InitialResources, DefenseHp = frozen.DefenseHp,
            Rng = random.Capture()
        };
        foreach (var initial in frozen.Units)
        {
            var template = frozen.Content.WeaponTemplates.Single(w => w.Id == initial.WeaponTemplateId);
            var unit = new CombatantState
            {
                Id = initial.Id, Name = initial.Name, Stats = initial.Stats,
                SpawnSequence = State.NextEntitySequence++, WeaponTemplateId = template.Id,
                WeaponType = template.WeaponType, BasePower = initial.BasePower ?? template.BasePower,
                BaseAttackIntervalMs = initial.AttackIntervalMs ?? template.AttackIntervalMs,
                RangeCenti = initial.RangeCenti ?? template.RangeCenti,
                ProjectileSpeedCentiPerSecond = template.ProjectileSpeedCentiPerSecond,
                PhysicalDefense = Rational.Max(1, initial.Stats.Vit),
                MagicalDefense = Rational.Max(1, initial.Stats.Int)
            };
            Recalculate(unit, false);
            unit.Hp = initial.CurrentHp.HasValue ? BigInteger.Clamp(initial.CurrentHp.Value, 0, unit.MaxHp) : unit.MaxHp;
            var assigned = initial.SkillIds ?? template.SkillIds.ToList();
            var normalIndex = 0;
            foreach (var id in assigned)
            {
                var definition = skills[id];
                var slot = definition.IsUltimate ? "ULT" : "S" + ++normalIndex;
                if (normalIndex > 2 || unit.Skills.Any(s => s.Slot == slot))
                    throw new ArgumentException("Invalid actual skill slot assignment.");
                unit.Skills.Add(new SkillSlotState
                {
                    Slot = slot, SkillId = id, Progress = definition.IsUltimate ? 0 : 1,
                    EffectiveCooldownMs = CooldownMath.Calculate(definition.CooldownMs, definition.IsUltimate, new())
                });
            }
            State.Units.Add(unit);
        }
    }

    private BattleSimulation(BattleSnapshot snapshot)
    {
        if (snapshot.SimulationVersion != SimulationVersion)
            throw new NotSupportedException("This combat snapshot requires a different simulation implementation.");
        State = Clone(snapshot);
        State.Setup.Content.EnsureValid();
        stage = State.Setup.Content.Stages.Single(s => s.Id == State.Setup.StageId);
        remainingPathDistance = BuildRemainingPathDistance(stage);
        skills = State.Setup.Content.Skills.ToDictionary(s => s.Id, StringComparer.Ordinal);
        random = Pcg32.Restore(State.Rng);
    }

    public static BattleSimulation Restore(BattleSnapshot snapshot) => new(snapshot);
    public BattleSnapshot Capture()
    {
        State.Rng = random.Capture();
        return Clone(State);
    }

    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(
        JsonSerializer.Serialize(value, DomainJson.Options), DomainJson.Options)!;

    public IReadOnlyList<BattleEvent> DrainEvents()
    {
        var result = events.ToArray();
        events.Clear();
        return result;
    }

    public bool Deploy(string id, int x, int y, BigInteger? placementCost = null)
    {
        var unit = Ally(id);
        if (unit is null || unit.Deployed || unit.Hp <= 0 || State.Running || State.Defeated || State.Victory
            || State.Units.Count(u => !u.IsEnemy && u.Deployed) >= 10 || !CanPlace(x, y, id)) return false;
        // The coefficient is a technical fixture; the production balancing value remains open.
        var cost = placementCost ?? 25;
        if (cost < 0) return false;
        if (State.Resources < cost) return false;
        State.Resources -= cost;
        unit.Deployed = unit.EverDeployed = true;
        unit.X = unit.AnchorX = x; unit.Y = unit.AnchorY = y;
        Emit("deploy", id, id, $"{unit.Name}を配置", cost);
        return true;
    }

    public bool Reposition(string id, int x, int y)
    {
        var unit = Ally(id);
        if (State.Running || State.Defeated || State.Victory || unit is null || !unit.Deployed || !CanPlace(x, y, id)) return false;
        unit.X = unit.AnchorX = x; unit.Y = unit.AnchorY = y;
        unit.CurrentTargetId = null; unit.Action = null;
        Emit("reposition", id, id, $"{unit.Name}の配置位置を変更", 0);
        return true;
    }

    private bool CanPlace(int x, int y, string id) => x >= 0 && x < stage.MapWidth && y >= 0 && y < stage.MapHeight
        && !stage.Path.Any(p => p.X == x && p.Y == y)
        && !State.Units.Any(u => !u.IsEnemy && u.Deployed && u.Id != id && u.AnchorX == x && u.AnchorY == y);

    public bool BeginWave()
    {
        if (State.Running || State.Defeated || State.Victory || State.Wave >= TotalWaves
            || !State.Units.Any(u => !u.IsEnemy && u.Deployed)
            || State.Units.Any(u => !u.IsEnemy && u.PendingLevelChoices > 0)) return false;
        State.Wave++;
        State.Running = true; State.WaveCompleted = false; State.SpawnedInWave = 0;
        State.NextSpawnAtMs = State.TimeMs;
        State.Projectiles.Clear();
        State.Units.RemoveAll(u => u.IsEnemy);
        Emit("wave-start", "", "", $"Wave {State.Wave} 開始", 0);
        SpawnDueEnemies();
        return true;
    }

    public void Step()
    {
        if (!State.Running) return;
        if (MeasurePhases) phaseStarted = Stopwatch.GetTimestamp();
        State.TimeMs = checked(State.TimeMs + TickMs);
        SpawnDueEnemies();
        MarkPhase("spawn");
        arrivalCache.Clear();
        foreach (var unit in State.Units.OrderBy(u => u.SpawnSequence))
        {
            if (!Active(unit)) continue;
            AdvanceCooldowns(unit);
            Move(unit);
        }
        arrivalCache.Clear();
        MarkPhase("movement-and-cooldowns");
        AdvanceProjectiles();
        MarkPhase("projectiles");
        ResolveStatuses();
        ResolveActions();
        ResolveDefeatedAndArrived();
        MarkPhase("existing-effects");
        if (!State.Running) { State.Rng = random.Capture(); return; }
        foreach (var unit in State.Units.OrderBy(u => u.SpawnSequence))
        {
            if (Active(unit)) ChooseAction(unit);
        }
        MarkPhase("action-selection");
        // Zero-cast actions release in their start update without starting another action.
        ResolveActions();
        ResolveDefeatedAndArrived();
        var wave = stage.Waves[State.Wave - 1];
        if (State.Running && State.SpawnedInWave >= wave.EnemyCount && !State.Units.Any(u => u.IsEnemy && u.Hp > 0))
            CompleteWave();
        State.Rng = random.Capture();
        MarkPhase("completion");
    }

    private void MarkPhase(string name)
    {
        if (!MeasurePhases) return;
        var now = Stopwatch.GetTimestamp();
        PhaseElapsedTimestamps[name] = PhaseElapsedTimestamps.GetValueOrDefault(name) + now - phaseStarted;
        phaseStarted = now;
    }

    private void SpawnDueEnemies()
    {
        var wave = stage.Waves[State.Wave - 1];
        while (State.SpawnedInWave < wave.EnemyCount && State.NextSpawnAtMs <= State.TimeMs)
        {
            var sequence = State.NextEntitySequence++;
            var first = stage.Path[0];
            State.Units.Add(new CombatantState
            {
                Id = $"enemy-{State.Wave}-{sequence}", Name = "侵攻者", IsEnemy = true,
                SpawnSequence = sequence, Stats = new(0, 0, 0, 0, 0), WeaponType = "enemy",
                BasePower = wave.EnemyPower, BaseAttackIntervalMs = 1200, RangeCenti = 100,
                EnemySpeedCentiPerSecond = wave.EnemySpeedCentiPerSecond,
                PhysicalDefense = wave.EnemyDefense, MagicalDefense = wave.EnemyDefense,
                Hp = BigInteger.Max(1, wave.EnemyHp.Floor()), MaxHp = BigInteger.Max(1, wave.EnemyHp.Floor()),
                X = first.X, Y = first.Y, PathIndex = 1, EnemyLevel = wave.EnemyLevel,
                Deployed = true, AttacksAdventurers = wave.AttacksAdventurers
            });
            State.SpawnedInWave++;
            State.NextSpawnAtMs += Math.Max(TickMs, wave.SpawnIntervalMs);
        }
    }

    private static bool Active(CombatantState unit) => unit.Deployed && unit.Hp > 0;
    private CombatantState? Ally(string id) => State.Units.FirstOrDefault(u => !u.IsEnemy && u.Id == id);
    private CombatantState? Unit(string id) => State.Units.FirstOrDefault(u => u.Id == id);
    private bool Stunned(CombatantState unit)
    {
        foreach (var status in unit.Statuses)
            if (status.Kind == "stun" && status.ExpiresAtMs > State.TimeMs) return true;
        return false;
    }

    private void AdvanceCooldowns(CombatantState unit)
    {
        foreach (var slot in unit.Skills)
        {
            var definition = skills[slot.SkillId];
            slot.EffectiveCooldownMs = CooldownMath.Calculate(definition.CooldownMs, definition.IsUltimate, slot.Cooldown);
            var previous = slot.Progress;
            slot.Progress = Rational.Min(1, slot.Progress + new Rational(TickMs) / slot.EffectiveCooldownMs);
            if (previous < 1 && slot.Progress == 1) slot.ReadySinceMs = State.TimeMs;
        }
    }

    private void Move(CombatantState unit)
    {
        if (Stunned(unit) || unit.Action is not null || State.TimeMs < unit.HardLockUntilMs) return;
        Rational slow = 0;
        foreach (var status in unit.Statuses)
            if (status.Kind == "slow" && status.ExpiresAtMs > State.TimeMs) slow = Rational.Max(slow, status.Strength);
        var centiSpeed = unit.IsEnemy ? unit.EnemySpeedCentiPerSecond
            : (unit.PreparedMoveCentiPerSecond ?? (300 + new Rational(5) * unit.Stats.Agi))
                * Rational.Max(0, unit.MoveMultiplier + unit.LevelMoveMultiplier - 1);
        if (!moveDistanceCache.TryGetValue(centiSpeed, out var remaining))
        {
            remaining = centiSpeed * new Rational(TickMs, 100000);
            moveDistanceCache[centiSpeed] = remaining;
        }
        if (slow > 0) remaining *= Rational.Clamp(1 - slow, 0, 1);
        if (unit.IsEnemy)
        {
            if (unit.AttacksAdventurers && Opponents(unit).Any(t => InRange(unit, t, unit.RangeCenti))) return;
            while (remaining > 0 && unit.PathIndex < stage.Path.Count)
            {
                var next = stage.Path[unit.PathIndex];
                remaining = MoveAxisAligned(unit, next.X, next.Y, remaining);
                if (unit.X == next.X && unit.Y == next.Y) unit.PathIndex++; else break;
            }
            return;
        }
        var target = unit.CurrentTargetId is null ? null : Unit(unit.CurrentTargetId);
        if (target is null || !Active(target) || !WithinAnchor(unit, target.X, target.Y))
        {
            target = SelectTarget(unit);
            unit.CurrentTargetId = target?.Id;
        }
        if (target is not null && InRange(unit, target, EffectiveRange(unit))) return;
        var destination = target is null ? (unit.AnchorX, unit.AnchorY) : NearestReachableCell(unit, target);
        FollowGridPath(unit, destination.Item1, destination.Item2, remaining);
    }

    private static Rational MoveAxisAligned(CombatantState unit, Rational targetX, Rational targetY, Rational available)
    {
        if (unit.Y == targetY && unit.X != targetX)
        {
            var positive = targetX > unit.X;
            var next = positive ? unit.X + available : unit.X - available;
            if (positive ? next < targetX : next > targetX) { unit.X = next; return 0; }
            unit.X = targetX;
            return Abs(next - targetX);
        }
        if (unit.X == targetX && unit.Y != targetY)
        {
            var positive = targetY > unit.Y;
            var next = positive ? unit.Y + available : unit.Y - available;
            if (positive ? next < targetY : next > targetY) { unit.Y = next; return 0; }
            unit.Y = targetY;
            return Abs(next - targetY);
        }
        var dx = targetX - unit.X;
        var xAmount = Rational.Min(Abs(dx), available);
        if (dx != 0) { unit.X += dx > 0 ? xAmount : -xAmount; available -= xAmount; }
        var dy = targetY - unit.Y;
        var yAmount = Rational.Min(Abs(dy), available);
        if (dy != 0) { unit.Y += dy > 0 ? yAmount : -yAmount; available -= yAmount; }
        return available;
    }

    private static Rational Abs(Rational value) => value < 0 ? -value : value;
    private bool WithinAnchor(CombatantState unit, Rational x, Rational y)
        => x >= unit.AnchorX - 3 && x <= unit.AnchorX + 3 && y >= unit.AnchorY - 3 && y <= unit.AnchorY + 3
            && CombatMath.DistanceSquared(unit.AnchorX, unit.AnchorY, x, y) <= 9;

    private (int, int) NearestReachableCell(CombatantState unit, CombatantState target)
    {
        var roundedX = (int)(target.X + new Rational(1, 2)).Floor();
        var roundedY = (int)(target.Y + new Rational(1, 2)).Floor();
        var dx = roundedX - unit.AnchorX; var dy = roundedY - unit.AnchorY;
        if (roundedX >= 0 && roundedX < stage.MapWidth && roundedY >= 0 && roundedY < stage.MapHeight
            && dx * dx + dy * dy <= 9) return (roundedX, roundedY);
        return Enumerable.Range(Math.Max(0, unit.AnchorX - 3), Math.Min(stage.MapWidth - 1, unit.AnchorX + 3) - Math.Max(0, unit.AnchorX - 3) + 1)
            .SelectMany(x => Enumerable.Range(Math.Max(0, unit.AnchorY - 3), Math.Min(stage.MapHeight - 1, unit.AnchorY + 3) - Math.Max(0, unit.AnchorY - 3) + 1)
                .Select(y => (X: x, Y: y)))
            .Where(p => WithinAnchor(unit, p.X, p.Y))
            .OrderBy(p => CombatMath.DistanceSquared(p.X, p.Y, target.X, target.Y))
            .ThenBy(p => p.X).ThenBy(p => p.Y).Select(p => (p.X, p.Y)).First();
    }

    private void FollowGridPath(CombatantState unit, int targetX, int targetY, Rational remaining)
    {
        if (unit.X.Denominator != 1 || unit.Y.Denominator != 1)
        {
            // Finish the current grid edge before selecting another four-direction path.
            var nextX = unit.X.Denominator == 1 ? (int)unit.X.Floor()
                : targetX >= unit.X ? (int)unit.X.Ceiling() : (int)unit.X.Floor();
            var nextY = unit.Y.Denominator == 1 ? (int)unit.Y.Floor()
                : targetY >= unit.Y ? (int)unit.Y.Ceiling() : (int)unit.Y.Floor();
            if (WithinAnchor(unit, nextX, nextY)) remaining = MoveAxisAligned(unit, nextX, nextY, remaining);
            if (unit.X.Denominator != 1 || unit.Y.Denominator != 1) return;
        }
        while (remaining > 0 && (unit.X != targetX || unit.Y != targetY))
        {
            var start = ((int)unit.X.Floor(), (int)unit.Y.Floor());
            var queue = new Queue<(int X, int Y)>();
            var visited = new HashSet<(int X, int Y)> { start };
            var previous = new Dictionary<(int X, int Y), (int X, int Y)>();
            queue.Enqueue(start);
            while (queue.Count > 0 && !visited.Contains((targetX, targetY)))
            {
                var at = queue.Dequeue();
                foreach (var next in new[] { (at.X + 1, at.Y), (at.X, at.Y + 1), (at.X - 1, at.Y), (at.X, at.Y - 1) })
                {
                    if (next.Item1 < 0 || next.Item1 >= stage.MapWidth || next.Item2 < 0 || next.Item2 >= stage.MapHeight
                        || !WithinAnchor(unit, next.Item1, next.Item2) || !visited.Add(next)) continue;
                    previous[next] = at; queue.Enqueue(next);
                }
            }
            if (!visited.Contains((targetX, targetY))) return;
            var step = (targetX, targetY);
            while (previous.TryGetValue(step, out var predecessor) && predecessor != start) step = predecessor;
            if (step == start) return;
            remaining = MoveAxisAligned(unit, step.Item1, step.Item2, remaining);
            if (unit.X.Denominator != 1 || unit.Y.Denominator != 1) return;
        }
    }

    private int EffectiveRange(CombatantState unit) => Math.Max(1,
        (int)(unit.RangeCenti * (unit.RangeMultiplier + unit.LevelRangeMultiplier - 1)).Floor());
    private static bool InRange(CombatantState source, CombatantState target, int centi)
        => CombatMath.DistanceSquared(source.X, source.Y, target.X, target.Y) <= new Rational(centi * (long)centi, 10000);
    private IEnumerable<CombatantState> Opponents(CombatantState source)
        => State.Units.Where(u => u.IsEnemy != source.IsEnemy && Active(u));

    private CombatantState? SelectTarget(CombatantState source, int? explicitRange = null)
    {
        CombatantState? best = null;
        Rational bestDistance = 0, bestArrival = 0;
        var range = source.IsEnemy ? explicitRange ?? source.RangeCenti : Math.Min(400, explicitRange ?? 400);
        Rational rangeSquared = new Rational(range * (long)range, 10000);
        foreach (var candidate in State.Units)
        {
            if (candidate.IsEnemy == source.IsEnemy || !Active(candidate)) continue;
            if (!source.IsEnemy && !WithinAnchor(source, candidate.X, candidate.Y)) continue;
            var distance = CombatMath.DistanceSquared(source.X, source.Y, candidate.X, candidate.Y);
            if (distance > rangeSquared) continue;
            var arrival = source.TargetPreset is "low-hp" or "lowest" or "high-hp" or "highest" or "nearest"
                ? Rational.Zero : RemainingArrivalTime(candidate);
            var comparison = best is null ? -1 : source.TargetPreset switch
            {
                "low-hp" or "lowest" => candidate.Hp.CompareTo(best.Hp),
                "high-hp" or "highest" => best.Hp.CompareTo(candidate.Hp),
                "nearest" => distance.CompareTo(bestDistance),
                _ => arrival.CompareTo(bestArrival)
            };
            if (comparison == 0) comparison = distance.CompareTo(bestDistance);
            if (comparison == 0) comparison = candidate.SpawnSequence.CompareTo(best!.SpawnSequence);
            if (comparison < 0) { best = candidate; bestDistance = distance; bestArrival = arrival; }
        }
        return best;
    }

    private Rational RemainingArrivalTime(CombatantState enemy)
    {
        if (arrivalCache.TryGetValue(enemy.SpawnSequence, out var cached)) return cached;
        if (!enemy.IsEnemy || enemy.PathIndex >= stage.Path.Count) return 0;
        var next = stage.Path[enemy.PathIndex];
        var distance = Abs(next.X - enemy.X) + Abs(next.Y - enemy.Y) + remainingPathDistance[enemy.PathIndex];
        var slow = enemy.Statuses.Where(s => s.Kind == "slow" && s.ExpiresAtMs > State.TimeMs)
            .Select(s => s.Strength).DefaultIfEmpty().Max();
        var speed = enemy.EnemySpeedCentiPerSecond * Rational.Max(new Rational(1, 10000), 1 - slow);
        var result = speed <= 0 ? new Rational(long.MaxValue) : distance * 100 / speed;
        arrivalCache[enemy.SpawnSequence] = result;
        return result;
    }

    private static Rational[] BuildRemainingPathDistance(StageDefinition stage)
    {
        var result = new Rational[stage.Path.Count];
        for (var i = stage.Path.Count - 2; i >= 0; i--)
            result[i] = result[i + 1] + Math.Abs(stage.Path[i + 1].X - stage.Path[i].X)
                + Math.Abs(stage.Path[i + 1].Y - stage.Path[i].Y);
        return result;
    }

    private void ChooseAction(CombatantState unit)
    {
        if (unit.IsEnemy && !unit.AttacksAdventurers) return;
        if (Stunned(unit) || unit.Action is not null || State.TimeMs < unit.HardLockUntilMs) return;
        foreach (var slot in unit.Skills.Where(s => s.Progress >= 1)
            .OrderByDescending(s => skills[s.SkillId].Priority)
            .ThenBy(s => s.ReadySinceMs).ThenBy(s => s.Slot == "ULT" ? 0 : s.Slot == "S1" ? 1 : 2))
        {
            var definition = skills[slot.SkillId];
            var targets = SkillTargets(unit, definition);
            if (targets.Count == 0) continue;
            slot.Progress = 0;
            unit.Action = MakeAction(unit, "skill", targets, definition.CastMs, definition.Id, slot.Slot);
            unit.Action.PowerMultiplier = CombatMath.SkillPower(unit.SkillModifiers, definition.Id, slot.Slot);
            unit.Action.HealingPowerMultiplier = CombatMath.SkillPower(unit.SkillModifiers, definition.Id, slot.Slot, healing: true);
            unit.HardLockUntilMs = State.TimeMs + definition.CastMs + Math.Max(250, definition.RecoveryMs);
            Emit("cast", unit.Id, targets[0].Id, $"{unit.Name}：{definition.Name}", 0, unit.Action.CastId);
            return;
        }
        if (State.TimeMs < unit.AttackReadyAtMs) return;
        var target = unit.CurrentTargetId is null ? null : Unit(unit.CurrentTargetId);
        if (target is null || !Active(target) || !InRange(unit, target, EffectiveRange(unit)))
        {
            target = SelectTarget(unit, EffectiveRange(unit));
            unit.CurrentTargetId = target?.Id;
        }
        if (target is null || (!unit.IsEnemy && !WithinAnchor(unit, target.X, target.Y))) return;
        var interval = unit.IsEnemy ? unit.BaseAttackIntervalMs
            : (unit.PreparedAttackIntervalMs ?? (unit.BaseAttackIntervalMs * 100 / (100 + Rational.Max(1, unit.Stats.Agi))))
                * Rational.Max(new Rational(1, 10), unit.IntervalMultiplier + unit.LevelIntervalMultiplier - 1);
        interval = Rational.Max(250, interval);
        unit.Action = MakeAction(unit, "attack", [target], 200);
        unit.AttackReadyAtMs = State.TimeMs + interval;
        unit.HardLockUntilMs = unit.AttackReadyAtMs;
    }

    private PendingAction MakeAction(CombatantState source, string kind, List<CombatantState> targets,
        int castMs, string? skillId = null, string? slot = null) => new()
    {
        Kind = kind, CastId = $"{State.Setup.AttemptId}:cast:{State.NextCastSequence++}",
        SkillId = skillId, Slot = slot, TargetIds = targets.Select(t => t.Id).ToList(),
        ReleaseAtMs = State.TimeMs + castMs, AttackSnapshot = CombatMath.Attack(source),
        HealingSnapshot = 5 + source.Stats.Int, CritChance = CombatMath.CritChance(source)
    };

    private List<CombatantState> SkillTargets(CombatantState source, SkillDefinition definition)
    {
        if (definition.Effects.Any(e => e.Kind == "heal"))
            return State.Units.Where(u => u.IsEnemy == source.IsEnemy && Active(u) && u.Hp < u.MaxHp
                && InRange(source, u, definition.RangeCenti))
                .OrderBy(u => new Rational(u.Hp, u.MaxHp)).ThenBy(u => u.SpawnSequence).Take(1).ToList();
        if (definition.Effects.All(e => e.Kind == "status" && e.Status is "defenseup" or "attackup"))
            return [source];
        var target = SelectTarget(source, definition.RangeCenti);
        return target is null ? [] : [target];
    }

    private void ResolveActions()
    {
        foreach (var source in State.Units.OrderBy(u => u.SpawnSequence))
        {
            var action = source.Action;
            if (action is null) continue;
            if (!Active(source) || Stunned(source)) { source.Action = null; continue; }
            if (action.Kind == "attack")
            {
                var lockedTarget = action.TargetIds.Select(Unit).FirstOrDefault(t => t is not null && Active(t));
                if (lockedTarget is null || !InRange(source, lockedTarget, EffectiveRange(source))
                    || (!source.IsEnemy && !WithinAnchor(source, lockedTarget.X, lockedTarget.Y)))
                {
                    source.Action = null; source.CurrentTargetId = null; source.HardLockUntilMs = State.TimeMs;
                    continue;
                }
            }
            if (action.ReleaseAtMs > State.TimeMs) continue;
            source.Action = null;
            if (action.Kind == "attack")
            {
                var target = action.TargetIds.Select(Unit).FirstOrDefault(t => t is not null && Active(t));
                if (target is null || !InRange(source, target, EffectiveRange(source))
                    || (!source.IsEnemy && !WithinAnchor(source, target.X, target.Y))) continue;
                // Normal projectiles capture offensive values at release, not at
                // windup start. Their target's defense remains an impact-time value.
                action.AttackSnapshot = CombatMath.Attack(source);
                action.CritChance = CombatMath.CritChance(source);
                if (source.ProjectileSpeedCentiPerSecond > 0) LaunchProjectile(source, target, action, "attack", action.AttackSnapshot);
                else ApplyDamage(source.Id, target, action.AttackSnapshot, action.CritChance,
                    source.WeaponType is "staff" or "杖", action.CastId);
                continue;
            }
            var definition = skills[action.SkillId!];
            foreach (var component in definition.Effects)
            {
                if (!action.AppliedComponents.Add(component.Id)) continue;
                foreach (var targetId in action.TargetIds)
                {
                    var target = Unit(targetId);
                    if (target is null || !Active(target)) continue;
                    switch (component.Kind)
                    {
                        case "damage":
                            ApplyDamage(source.Id, target, action.AttackSnapshot * component.Amount * action.PowerMultiplier,
                                component.Derived ? 0 : action.CritChance, source.WeaponType is "staff" or "杖", action.CastId);
                            break;
                        case "heal":
                            var healing = BigInteger.Max(0, (action.HealingSnapshot * component.Amount * action.HealingPowerMultiplier).Floor());
                            var restored = BigInteger.Min(healing, target.MaxHp - target.Hp);
                            target.Hp += restored;
                            Emit("heal", source.Id, target.Id, $"{target.Name}を回復", restored, action.CastId);
                            break;
                        case "status":
                            var kind = NormalizeStatus(component.Status ?? "");
                            var strength = kind is "burn" or "poison" ? action.AttackSnapshot * component.Amount
                                : component.Amount;
                            ApplyStatus(target.Id, kind, strength, component.DurationMs, component.TickMs, source.Id, action.CastId);
                            break;
                    }
                }
            }
        }
    }

    private void LaunchProjectile(CombatantState source, CombatantState target, PendingAction action, string componentId, Rational power)
    {
        var distance = CombatMath.Distance(source.X, source.Y, target.X, target.Y);
        if (distance == 0)
        {
            ApplyDamage(source.Id, target, power, action.CritChance, source.WeaponType is "staff" or "杖", action.CastId);
            return;
        }
        var speed = new Rational(source.ProjectileSpeedCentiPerSecond, 100);
        var sequence = State.NextProjectileSequence++;
        State.Projectiles.Add(new ProjectileState
        {
            Id = $"projectile-{sequence}", Sequence = sequence, SourceId = source.Id, CastId = action.CastId,
            ComponentId = componentId, IsEnemy = source.IsEnemy, Magical = source.WeaponType is "staff" or "杖",
            X = source.X, Y = source.Y, VelocityX = (target.X - source.X) * speed / distance,
            VelocityY = (target.Y - source.Y) * speed / distance, Speed = speed, RemainingRange = new Rational(EffectiveRange(source), 100),
            ExpiresAtMs = State.TimeMs + 10000, AttackSnapshot = power, CritChance = action.CritChance
        });
    }

    private void AdvanceProjectiles()
    {
        // A spatial broad phase keeps the 400-projectile/200-enemy fixture from
        // doing a fraction-heavy narrow-phase collision for every possible pair.
        var grid = new Dictionary<(bool Enemy, int X, int Y), List<CombatantState>>();
        foreach (var target in State.Units.Where(Active))
        {
            var key = (target.IsEnemy, (int)target.X.Floor(), (int)target.Y.Floor());
            if (!grid.TryGetValue(key, out var cell)) grid[key] = cell = [];
            cell.Add(target);
        }
        foreach (var projectile in State.Projectiles.OrderBy(p => p.Sequence).ToArray())
        {
            if (State.TimeMs > projectile.ExpiresAtMs || projectile.RemainingRange <= 0)
            { State.Projectiles.Remove(projectile); continue; }
            Rational delta = new Rational(TickMs, 1000);
            if (projectile.Speed <= 0) projectile.Speed = CombatMath.Distance(0, 0, projectile.VelocityX, projectile.VelocityY);
            var length = projectile.Speed * delta;
            if (length > projectile.RemainingRange) delta *= projectile.RemainingRange / length;
            var endX = projectile.X + projectile.VelocityX * delta;
            var endY = projectile.Y + projectile.VelocityY * delta;
            CombatantState? hit = null;
            Rational? first = null;
            var candidates = new List<CombatantState>();
            var minX = Math.Max(-1, (int)(Rational.Min(projectile.X, endX) - new Rational(1, 5)).Floor());
            var maxX = Math.Min(stage.MapWidth, (int)(Rational.Max(projectile.X, endX) + new Rational(1, 5)).Floor());
            var minY = Math.Max(-1, (int)(Rational.Min(projectile.Y, endY) - new Rational(1, 5)).Floor());
            var maxY = Math.Min(stage.MapHeight, (int)(Rational.Max(projectile.Y, endY) + new Rational(1, 5)).Floor());
            for (var x = minX; x <= maxX; x++)
                for (var y = minY; y <= maxY; y++)
                    if (grid.TryGetValue((!projectile.IsEnemy, x, y), out var cell)) candidates.AddRange(cell);
            foreach (var target in candidates.Where(Active).OrderBy(u => u.SpawnSequence))
            {
                var entry = CombatMath.SegmentCircleEntry(projectile.X, projectile.Y, endX, endY,
                    target.X, target.Y, new Rational(1, 5));
                if (entry.HasValue && (!first.HasValue || entry.Value < first.Value)) { first = entry; hit = target; }
            }
            if (hit is not null)
            {
                ApplyDamage(projectile.SourceId, hit, projectile.AttackSnapshot, projectile.CritChance,
                    projectile.Magical, projectile.CastId);
                State.Projectiles.Remove(projectile);
                continue;
            }
            projectile.X = endX; projectile.Y = endY;
            projectile.RemainingRange -= Rational.Min(length, projectile.RemainingRange);
            if (projectile.RemainingRange <= 0) State.Projectiles.Remove(projectile);
        }
    }

    private void ApplyDamage(string sourceId, CombatantState target, Rational power, Rational critChance, bool magical, string castId)
    {
        if (!Active(target)) return;
        var critical = critChance > 0 && random.NextInt(10000) < (critChance * 10000).Floor();
        var amount = CombatMath.Damage(power, CombatMath.Defense(target, magical), critical);
        target.Hp = BigInteger.Max(0, target.Hp - amount);
        Emit("damage", sourceId, target.Id, critical ? "会心" : "命中", amount, castId);
        if (target.Hp == 0) DefeatUnit(target, sourceId, castId);
    }

    private void DefeatUnit(CombatantState target, string sourceId, string castId)
    {
        target.Action = null; target.CurrentTargetId = null;
        if (target.IsEnemy)
        {
            var reward = target.EnemyLevel * 2;
            State.Resources += reward;
            Emit("enemy-defeated", sourceId, target.Id, "敵を撃破", reward, castId);
        }
        else Emit("incapacitated", sourceId, target.Id, $"{target.Name}が戦闘不能", 0, castId);
    }

    private void ResolveDefeatedAndArrived()
    {
        foreach (var enemy in State.Units.Where(u => u.IsEnemy && u.Hp > 0 && u.PathIndex >= stage.Path.Count)
            .OrderBy(u => u.SpawnSequence))
        {
            State.DefenseHp = Math.Max(0, State.DefenseHp - 1);
            enemy.Hp = 0;
            Emit("arrival", enemy.Id, "defense", "防衛対象に到達", 1);
        }
        // Dead enemies are not retained in the active simulation; their spawn sequence and effects stay in events.
        State.Units.RemoveAll(u => u.IsEnemy && u.Hp <= 0);
        if (State.DefenseHp <= 0)
        {
            State.Defeated = true; State.Running = false;
            Emit("defeat", "", "", "防衛失敗", 0);
        }
    }

    private void CompleteWave()
    {
        State.Running = false; State.WaveCompleted = true;
        State.Resources += State.Wave * 100;
        State.Projectiles.Clear();
        GrantWaveExperience();
        State.Victory = State.Wave >= TotalWaves;
        Emit(State.Victory ? "victory" : "wave-clear", "", "", State.Victory ? "防衛成功" : $"Wave {State.Wave} 突破", State.Wave * 100);
    }

    public IReadOnlyList<string> GrantWaveExperience()
    {
        if (!State.WaveCompleted || State.LastExperienceAwardedWave >= State.Wave) return [];
        State.LastExperienceAwardedWave = State.Wave;
        var raised = new List<string>();
        foreach (var unit in State.Units.Where(u => !u.IsEnemy && u.EverDeployed).OrderBy(u => u.SpawnSequence))
        {
            if (unit.Level >= 10) continue;
            unit.LevelExperience += 50;
            while (unit.Level < 10 && unit.LevelExperience >= LevelCost(unit.Level))
            {
                unit.LevelExperience -= LevelCost(unit.Level);
                unit.Level++; unit.PendingLevelChoices++; raised.Add(unit.Id);
            }
        }
        return raised;
    }

    public static BigInteger LevelCost(int level) => 125 + 75 * (level - 1);

    public bool PurchaseLevel(string id)
    {
        var unit = Ally(id);
        if (State.Running || State.Defeated || State.Victory || unit is null || !unit.Deployed
            || unit.Level >= 10 || unit.PendingLevelChoices > 0) return false;
        var missing = BigInteger.Max(0, LevelCost(unit.Level) - unit.LevelExperience);
        var cost = missing * 2;
        if (State.Resources < cost) return false;
        State.Resources -= cost; unit.LevelExperience = 0; unit.Level++; unit.PendingLevelChoices++;
        Emit("level-purchase", id, id, $"{unit.Name}の不足経験値を購入", cost);
        return true;
    }

    public bool GrantLevel(string id, string kind)
    {
        var unit = Ally(id);
        if (State.Running || unit is null || unit.PendingLevelChoices <= 0) return false;
        switch (kind)
        {
            case "vitality": unit.LevelMaxHpMultiplier += new Rational(1, 10); Recalculate(unit, true); break;
            case "firepower": unit.LevelAttackMultiplier += new Rational(2, 25); break;
            case "rapid": unit.LevelIntervalMultiplier -= new Rational(1, 20); unit.LevelIntervalMultiplier = Rational.Max(new Rational(1, 10), unit.LevelIntervalMultiplier); break;
            case "mobility": unit.LevelMoveMultiplier += new Rational(1, 10); break;
            case "reach": unit.LevelRangeMultiplier += new Rational(1, 20); break;
            case "defense": unit.LevelDefenseMultiplier += new Rational(1, 20); break;
            default: return false;
        }
        unit.PendingLevelChoices--;
        Emit("level-choice", id, id, $"{unit.Name}の一時強化：{kind}", 0);
        return true;
    }

    public bool Revive(string id)
    {
        var unit = Ally(id);
        if (State.Running || State.Defeated || State.Victory || unit is null || !unit.Deployed || unit.Hp > 0) return false;
        var cost = (unit.RevivalCount + 1) * unit.Level * 25;
        if (State.Resources < cost) return false;
        State.Resources -= cost; unit.RevivalCount++;
        unit.Statuses.Clear(); unit.Action = null; unit.CurrentTargetId = null;
        unit.LevelAttackMultiplier = unit.LevelIntervalMultiplier = unit.LevelMoveMultiplier = unit.LevelRangeMultiplier = unit.LevelDefenseMultiplier = unit.LevelMaxHpMultiplier = 1;
        unit.LevelExperience = 0;
        unit.PendingLevelChoices = 0;
        Recalculate(unit, false);
        unit.Hp = BigInteger.Max(1, new Rational(unit.MaxHp, 10).Ceiling());
        unit.X = unit.AnchorX; unit.Y = unit.AnchorY;
        unit.HardLockUntilMs = unit.AttackReadyAtMs = State.TimeMs;
        foreach (var slot in unit.Skills)
        {
            slot.Progress = slot.Slot == "ULT" ? 0 : 1;
            slot.ReadySinceMs = State.TimeMs;
        }
        Emit("revive", id, id, $"{unit.Name}が復帰", cost);
        return true;
    }

    public bool SetTarget(string id, string preset)
    {
        var unit = Ally(id);
        if (unit is null || preset is not ("arrival" or "low-hp" or "high-hp" or "nearest")) return false;
        unit.TargetPreset = preset;
        if (unit.Action is null) unit.CurrentTargetId = null;
        return true;
    }

    public bool SetCooldownModifiers(string id, string slotName, CooldownModifiers modifiers)
    {
        var slot = Ally(id)?.Skills.FirstOrDefault(s => s.Slot == slotName);
        if (slot is null) return false;
        var definition = skills[slot.SkillId];
        slot.EffectiveCooldownMs = CooldownMath.Calculate(definition.CooldownMs, definition.IsUltimate, modifiers);
        slot.Cooldown = modifiers;
        return true;
    }

    public bool TryRefundCooldown(string id, string slotName, string castId, Rational amount, bool derived = false)
    {
        if (derived || amount <= 0) return false;
        var slot = Ally(id)?.Skills.FirstOrDefault(s => s.Slot == slotName);
        if (slot is null || slot.Progress >= 1) return false;
        var key = castId + ":" + id + ":" + slotName;
        if (!State.RefundGuards.Add(key)) return false;
        slot.Progress += Rational.Min(amount, (1 - slot.Progress) / 4);
        if (slot.Progress >= 1) slot.ReadySinceMs = State.TimeMs;
        return true;
    }

    public bool RecalculateUnit(string id, bool healIncrease = false)
    {
        var unit = Ally(id);
        if (unit is null) return false;
        Recalculate(unit, healIncrease);
        return true;
    }

    private static void Recalculate(CombatantState unit, bool healIncrease)
    {
        var previous = unit.MaxHp;
        var reference = unit.PreparedMaxHp ?? (100 + 10 * Rational.Max(1, unit.Stats.Vit));
        var minimum = BigInteger.Max(1, (reference / 100).Ceiling());
        unit.MaxHp = BigInteger.Max(minimum, (reference * (unit.MaxHpMultiplier + unit.LevelMaxHpMultiplier - 1)).Floor());
        unit.Hp = BigInteger.Min(unit.Hp, unit.MaxHp);
        if (healIncrease && unit.Hp > 0 && unit.MaxHp > previous) unit.Hp = BigInteger.Min(unit.MaxHp, unit.Hp + unit.MaxHp - previous);
    }

    public bool ApplyStatus(string targetId, string kind, Rational strength, int durationMs,
        int tickMs = 0, string sourceId = "", string castId = "")
    {
        var target = Unit(targetId);
        kind = NormalizeStatus(kind);
        if (target is null || !Active(target) || durationMs <= 0 || kind is not ("burn" or "poison" or "slow" or "stun" or "defenseup" or "attackup")) return false;
        strength = Rational.Max(0, strength);
        var interval = tickMs > 0 ? tickMs : kind == "burn" ? 1000 : kind == "poison" ? 2000 : 0;
        var existing = target.Statuses.FirstOrDefault(s => s.Kind == kind);
        if (existing is not null)
        {
            if (strength > existing.Strength)
            {
                existing.Strength = strength; existing.SourceId = sourceId; existing.CastId = castId;
            }
            existing.ExpiresAtMs = Rational.Max(existing.ExpiresAtMs, State.TimeMs + durationMs);
        }
        else target.Statuses.Add(new StatusState
        {
            Kind = kind, Strength = strength, ExpiresAtMs = State.TimeMs + durationMs,
            TickIntervalMs = interval, NextTickAtMs = State.TimeMs + interval, SourceId = sourceId, CastId = castId
        });
        if (kind == "stun") target.Action = null;
        Emit("status", sourceId, targetId, $"{target.Name}：{kind}", 0, castId);
        return true;
    }

    private static string NormalizeStatus(string kind) => kind.ToLowerInvariant() switch
    {
        "炎上" => "burn", "毒" => "poison", "鈍足" => "slow", "気絶" => "stun", var value => value
    };

    private void ResolveStatuses()
    {
        foreach (var unit in State.Units.OrderBy(u => u.SpawnSequence))
        {
            foreach (var status in unit.Statuses.ToArray())
            {
                while (Active(unit) && status.TickIntervalMs > 0 && status.NextTickAtMs <= State.TimeMs
                    && status.NextTickAtMs <= status.ExpiresAtMs)
                {
                    status.NextTickAtMs += status.TickIntervalMs;
                    if (status.Kind is not ("burn" or "poison")) continue;
                    var amount = BigInteger.Max(0, status.Strength.Floor());
                    unit.Hp = BigInteger.Max(0, unit.Hp - amount);
                    Emit("dot", status.SourceId, unit.Id, status.Kind, amount, status.CastId);
                    if (unit.Hp == 0) DefeatUnit(unit, status.SourceId, status.CastId);
                }
                if (status.ExpiresAtMs <= State.TimeMs) unit.Statuses.Remove(status);
            }
        }
    }

    private void Emit(string kind, string source, string target, string text, BigInteger amount, string castId = "")
    {
        events.Add(new(State.NextEventSequence++, State.TimeMs, kind, source, target, text, amount, castId));
        if (events.Count > MaximumRecentEvents) events.RemoveRange(0, events.Count - MaximumRecentEvents);
    }
}

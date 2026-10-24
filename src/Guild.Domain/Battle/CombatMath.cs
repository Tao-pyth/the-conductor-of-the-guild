using System.Numerics;
using Guild.Domain.Numerics;

namespace Guild.Domain.Battle;

public static class CooldownMath
{
    public static Rational Calculate(Rational baseMs, bool ultimate, CooldownModifiers modifiers)
    {
        if (baseMs <= 0) throw new ArgumentOutOfRangeException(nameof(baseMs));
        var floor = Rational.Max(ultimate ? 10000 : 1000,
            baseMs * (ultimate ? new Rational(1, 4) : new Rational(1, 10)));
        var ceiling = baseMs * 4;
        if (floor > ceiling) throw new ArgumentException("Cooldown floor exceeds the permitted ceiling.");
        var first = Rational.Clamp(baseMs - modifiers.FixedReductionMs + modifiers.FixedExtensionMs, floor, ceiling);
        var reduction = Rational.Clamp(modifiers.ShorteningRate, 0, new Rational(4, 5));
        var extension = Rational.Clamp(modifiers.ExtensionRate, 0, 3);
        var second = Rational.Clamp(first * (1 - reduction + extension), floor, ceiling);
        var speed = Rational.Clamp(1 + modifiers.RecoveryDelta, new Rational(1, 4), 3);
        return Rational.Clamp(second / speed, floor, ceiling);
    }
}

public static class CombatMath
{
    public static Rational Attack(CombatantState unit)
    {
        Rational bonus = unit.WeaponType.ToLowerInvariant() switch
        {
            "sword" or "剣" => unit.Stats.Str + new Rational(3, 10) * unit.Stats.Dex,
            "spear" or "槍" => new Rational(7, 10) * unit.Stats.Str + new Rational(3, 5) * unit.Stats.Dex,
            "hammer" or "槌" => new Rational(13, 10) * unit.Stats.Str,
            "bow" or "弓" => new Rational(1, 5) * unit.Stats.Str + unit.Stats.Dex,
            "staff" or "杖" => new Rational(11, 10) * unit.Stats.Int + new Rational(1, 10) * unit.Stats.Dex,
            _ => 0
        };
        var boost = unit.Statuses.Where(s => s.Kind == "attackup").Select(s => s.Strength).DefaultIfEmpty().Max();
        return Rational.Max(0, (unit.PreparedAttackPower ?? (unit.BasePower + bonus))
            * Rational.Max(0, unit.AttackMultiplier + unit.LevelAttackMultiplier - 1 + boost));
    }

    public static Rational CritChance(CombatantState unit) => unit.IsEnemy ? 0
        : Rational.Clamp(new Rational(1, 20) + new Rational(unit.Stats.Dex, 1000), 0, 1);

    public static Rational Defense(CombatantState unit, bool magical)
    {
        var basis = magical ? unit.MagicalDefense : unit.PhysicalDefense;
        var boost = unit.Statuses.Where(s => s.Kind == "defenseup").Select(s => s.Strength).DefaultIfEmpty().Max();
        return Rational.Max(0, basis * (unit.DefenseMultiplier + unit.LevelDefenseMultiplier - 1 + boost));
    }

    public static BigInteger Damage(Rational attack, Rational defense, bool critical)
    {
        if (attack <= 0) return BigInteger.Zero;
        var result = attack * 100 / (100 + Rational.Max(0, defense));
        if (critical) result *= new Rational(3, 2);
        return BigInteger.Max(1, result.Floor());
    }

    public static Rational DistanceSquared(Rational x1, Rational y1, Rational x2, Rational y2)
    {
        var dx = x1 - x2; var dy = y1 - y2;
        return dx * dx + dy * dy;
    }

    // Geometry alone uses a deterministic 1e-6 tile approximation for irrational roots.
    // Stats, movement accumulation, cooldowns and final damage retain exact rational arithmetic.
    public static Rational Distance(Rational x1, Rational y1, Rational x2, Rational y2)
        => Sqrt(DistanceSquared(x1, y1, x2, y2));

    public static Rational Sqrt(Rational value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        var scaled = (value * 1_000_000_000_000L).Floor();
        return new Rational(IntegerSqrt(scaled), 1_000_000);
    }

    private static BigInteger IntegerSqrt(BigInteger value)
    {
        if (value <= 1) return value;
        var root = BigInteger.One << (int)((value.GetBitLength() + 1) / 2);
        while (true)
        {
            var next = (root + value / root) / 2;
            if (next >= root) return root;
            root = next;
        }
    }

    public static Rational? SegmentCircleEntry(Rational startX, Rational startY,
        Rational endX, Rational endY, Rational targetX, Rational targetY, Rational radius)
    {
        var dx = endX - startX; var dy = endY - startY;
        var ox = startX - targetX; var oy = startY - targetY;
        var a = dx * dx + dy * dy;
        var c = ox * ox + oy * oy - radius * radius;
        if (c <= 0) return Rational.Zero;
        if (a == 0) return null;
        var projection = Rational.Clamp(-(ox * dx + oy * dy) / a, 0, 1);
        if (DistanceSquared(startX + projection * dx, startY + projection * dy, targetX, targetY) > radius * radius)
            return null;
        var b = 2 * (ox * dx + oy * dy);
        var discriminant = b * b - 4 * a * c;
        if (discriminant < 0) return null;
        var entry = (-b - Sqrt(discriminant)) / (2 * a);
        return Rational.Clamp(entry, 0, 1);
    }

    public static Rational SkillPower(IEnumerable<SkillModifier> components, string skillId, string slot, bool healing = false)
    {
        var applied = new HashSet<string>(StringComparer.Ordinal);
        Rational factor = 1;
        foreach (var component in components.OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            if (healing && component.AttackOnly) continue;
            if ((component.SkillId == skillId || component.Slot == slot) && applied.Add(component.Id))
                factor *= component.PowerMultiplier;
        }
        return Rational.Clamp(factor, new Rational(1, 10), 5);
    }
}

using System.Numerics;
using Guild.Domain.Numerics;

namespace Guild.Domain.Definitions;

public static class ParameterCalculator
{
    /// <summary>Callers select active effects and deduplicate provenance before evaluating a parameter.</summary>
    public static Rational Evaluate(Rational baseValue, ParameterDefinition parameter, IEnumerable<ModifierDefinition> modifiers)
    {
        var applicable = modifiers.Where(m => m.ParameterId == parameter.Id).ToArray();
        var fixedSum = applicable.Where(m => m.Stage == ModifierStage.Fixed).Aggregate(Rational.Zero, (a, m) => a + m.Amount);
        var additiveSum = applicable.Where(m => m.Stage == ModifierStage.Additive).Aggregate(Rational.Zero, (a, m) => a + m.Amount);
        var product = applicable.Where(m => m.Stage == ModifierStage.Multiplicative).Aggregate(Rational.One, (a, m) => a * m.Amount);
        var fixedValue = Clamp(baseValue + fixedSum, parameter);
        var additiveValue = Clamp(fixedValue * (Rational.One + additiveSum), parameter);
        return Clamp(additiveValue * product, parameter);
    }

    private static Rational Clamp(Rational value, ParameterDefinition definition)
        => definition.Maximum is { } maximum ? Rational.Clamp(value, definition.Minimum, maximum) : Rational.Max(value, definition.Minimum);
}

public static class GameFormulas
{
    public static BigInteger RankExperienceForNext(BigInteger rank)
    {
        if (rank < 1) throw new ArgumentOutOfRangeException(nameof(rank));
        return 100 + 25 * (rank - 1);
    }

    public static int LevelExperienceForNext(int level)
    {
        if (level is < 1 or >= 10) throw new ArgumentOutOfRangeException(nameof(level));
        return 125 + 75 * (level - 1);
    }

    public static Rational MaximumHp(BigInteger vitality) => 100 + 10 * new Rational(vitality);
    public static Rational HealingPower(BigInteger intelligence) => 5 + new Rational(intelligence);
    public static Rational MovementCentiPerSecond(BigInteger agility) => 300 + 5 * new Rational(agility);
    public static Rational AttackInterval(Rational weaponIntervalMs, BigInteger agility)
        => weaponIntervalMs * 100 / (100 + new Rational(agility));

    public static Rational AttackPower(string weaponType, Rational weaponPower, BigInteger str, BigInteger dex, BigInteger intelligence)
    {
        Rational contribution = weaponType switch
        {
            "sword" => new Rational(str) + new Rational(dex) * new Rational(3, 10),
            "spear" => new Rational(str) * new Rational(7, 10) + new Rational(dex) * new Rational(6, 10),
            "hammer" => new Rational(str) * new Rational(13, 10),
            "bow" => new Rational(str) * new Rational(2, 10) + new Rational(dex),
            "staff" => new Rational(intelligence) * new Rational(11, 10) + new Rational(dex) * new Rational(1, 10),
            _ => throw new ArgumentException("Unknown weapon type.", nameof(weaponType))
        };
        return weaponPower + contribution;
    }

    public static Rational DefenseAdjustedDamage(Rational attack, Rational defense) => attack * 100 / (100 + defense);

    public static Rational FusionScale(int resultingBlessingRank) => resultingBlessingRank switch
    {
        1 => new Rational(2, 3),
        2 => Rational.One,
        3 => new Rational(4, 3),
        _ => throw new ArgumentOutOfRangeException(nameof(resultingBlessingRank))
    };

    /// <summary>Source is the parent's current Rank III component, not its original Rank I value.</summary>
    public static Rational InheritFusionComponent(Rational sourceRankThreeValue, ModifierStage stage, int resultingBlessingRank)
    {
        var scale = FusionScale(resultingBlessingRank);
        return stage == ModifierStage.Multiplicative
            ? Rational.One + (sourceRankThreeValue - Rational.One) * scale
            : sourceRankThreeValue * scale;
    }
}

using System.Numerics;
using System.Text.Json;
using Guild.Domain.Definitions;
using Guild.Domain.Numerics;
using Guild.Domain.Serialization;

namespace Guild.Domain.Tests;

public sealed class NumericsTests
{
    [Fact]
    public void DefaultIsNormalizedZeroAndFractionsAreExact()
    {
        Rational zero = default;
        Assert.Equal(BigInteger.Zero, zero.Numerator);
        Assert.Equal(BigInteger.One, zero.Denominator);
        Assert.Equal(Rational.Zero, new Rational(0, -27));
        Assert.Equal(new Rational(-2, 3), new Rational(10, -15));
        Assert.Equal(Rational.One, new Rational(1, 3) * 3);
        Assert.Equal(new Rational(5, 6), new Rational(1, 2) + new Rational(1, 3));
        Assert.Equal(new Rational(-1, 6), new Rational(1, 3) - new Rational(1, 2));
        Assert.Equal(new Rational(2, 3), new Rational(4, 7) / new Rational(6, 7));
        Assert.Throws<DivideByZeroException>(() => new Rational(1, 0));
        Assert.Throws<DivideByZeroException>(() => Rational.One / Rational.Zero);
    }

    [Theory]
    [InlineData("1/3", "0.34", 0, 1)]
    [InlineData("-1/3", "-0.33", -1, 0)]
    [InlineData("-1.234", "-1.23", -2, -1)]
    [InlineData("1.234", "1.24", 1, 2)]
    [InlineData("-0.001", "0.00", -1, 0)]
    [InlineData("2", "2.00", 2, 2)]
    public void DisplayCeilsOnlyTheDisplayedUnit(string raw, string expected, int floor, int ceiling)
    {
        var rational = Rational.Parse(raw);
        Assert.Equal(expected, rational.ToDisplayString());
        Assert.Equal(new BigInteger(floor), rational.Floor());
        Assert.Equal(new BigInteger(ceiling), rational.Ceiling());
        Assert.Equal(rational, Rational.Parse(raw));
    }

    [Fact]
    public void HugeIntegersAndFractionsSurviveStrictJson()
    {
        var huge = BigInteger.Pow(10, 100) + 123456789;
        var expected = new NumericPayload(huge, new Rational(huge, 7));
        var json = JsonSerializer.Serialize(expected, DomainJson.Options);
        Assert.Contains("\"numerator\": \"", json);
        Assert.Contains("\"denominator\": \"7\"", json);
        Assert.Equal(expected, JsonSerializer.Deserialize<NumericPayload>(json, DomainJson.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<BigInteger>("123", DomainJson.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Rational>("{\"numerator\":\"1\",\"denominator\":\"0\"}", DomainJson.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Rational>("{\"numerator\":1,\"denominator\":\"2\"}", DomainJson.Options));
        Assert.Equal(1.0, new Rational(huge, huge).ToDouble());
        Assert.Equal(100.0, new Rational(BigInteger.Pow(10, 400), BigInteger.Pow(10, 398)).ToDouble(), 10);
        Assert.Equal(100.0, new Rational(BigInteger.Pow(10, 400) + 1, BigInteger.Pow(10, 398) + 3).ToDouble(), 10);
        Assert.Equal(0.5, new Rational(BigInteger.Pow(10, 500) + 1, 2 * BigInteger.Pow(10, 500) + 3).ToDouble(), 10);
        Assert.Equal(double.Epsilon, new Rational(1, BigInteger.One << 1074).ToDouble());
    }

    [Fact]
    public void CommonParameterPipelineClampsEachStageAndKeepsFractionalPrecision()
    {
        var parameter = new ParameterDefinition { Id = "hp", Unit = "hp", Minimum = 1, Maximum = 150 };
        ModifierDefinition[] modifiers =
        [
            new() { Id = "flat", ParameterId = "hp", Stage = ModifierStage.Fixed, Amount = 20 },
            new() { Id = "rate", ParameterId = "hp", Stage = ModifierStage.Additive, Amount = new Rational(1, 2) },
            new() { Id = "factor", ParameterId = "hp", Stage = ModifierStage.Multiplicative, Amount = new Rational(1, 2) }
        ];
        Assert.Equal((Rational)75, ParameterCalculator.Evaluate(100, parameter, modifiers));
        Assert.Equal(new Rational(7, 3), ParameterCalculator.Evaluate(new Rational(7, 3), parameter, []));
        Assert.Equal((Rational)1, ParameterCalculator.Evaluate(-100, parameter, []));
    }

    [Fact]
    public void GrowthAndFusionDoNotAcquireIntegerLimitsOrPrematureRounding()
    {
        var rank = BigInteger.Pow(10, 50);
        Assert.Equal(100 + 25 * (rank - 1), GameFormulas.RankExperienceForNext(rank));
        Assert.Equal(125, GameFormulas.LevelExperienceForNext(1));
        Assert.Equal(725, GameFormulas.LevelExperienceForNext(9));
        Assert.Throws<ArgumentOutOfRangeException>(() => GameFormulas.LevelExperienceForNext(10));
        Assert.Equal(new Rational(2, 9), GameFormulas.InheritFusionComponent(new Rational(1, 3), ModifierStage.Fixed, 1));
        Assert.Equal(new Rational(17, 15), GameFormulas.InheritFusionComponent(new Rational(6, 5), ModifierStage.Multiplicative, 1));
        Assert.Equal(Rational.One, GameFormulas.InheritFusionComponent(Rational.One, ModifierStage.Multiplicative, 3));
    }

    [Fact]
    public void ApprovedWeaponContributionsAndDerivedValuesAreExact()
    {
        Assert.Equal((Rational)33, GameFormulas.AttackPower("sword", 20, 10, 10, 10));
        Assert.Equal((Rational)33, GameFormulas.AttackPower("spear", 20, 10, 10, 10));
        Assert.Equal((Rational)33, GameFormulas.AttackPower("hammer", 20, 10, 10, 10));
        Assert.Equal((Rational)32, GameFormulas.AttackPower("bow", 20, 10, 10, 10));
        Assert.Equal((Rational)32, GameFormulas.AttackPower("staff", 20, 10, 10, 10));
        Assert.Equal((Rational)200, GameFormulas.MaximumHp(10));
        Assert.Equal((Rational)350, GameFormulas.MovementCentiPerSecond(10));
        Assert.Equal(new Rational(10000, 11), GameFormulas.AttackInterval(1000, 10));
        Assert.Equal(new Rational(100, 3), GameFormulas.DefenseAdjustedDamage(50, 50));
    }

    public sealed record NumericPayload(BigInteger Integer, Rational Fraction);
}

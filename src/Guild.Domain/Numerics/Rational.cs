using System.Globalization;
using System.Numerics;

namespace Guild.Domain.Numerics;

/// <summary>An exact, normalized fraction. A default value is a valid zero.</summary>
public readonly struct Rational : IComparable<Rational>, IEquatable<Rational>
{
    private readonly BigInteger numerator;
    private readonly BigInteger denominator;

    public Rational(BigInteger integer) : this(integer, BigInteger.One) { }

    public Rational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero) throw new DivideByZeroException("A rational denominator cannot be zero.");
        if (denominator.Sign < 0) { numerator = -numerator; denominator = -denominator; }
        var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        this.numerator = numerator / gcd;
        this.denominator = denominator / gcd;
    }

    public BigInteger Numerator => numerator;
    public BigInteger Denominator => denominator.IsZero ? BigInteger.One : denominator;
    public static Rational Zero => default;
    public static Rational One => new(BigInteger.One);

    public BigInteger Floor()
    {
        var quotient = BigInteger.DivRem(Numerator, Denominator, out var remainder);
        return remainder.Sign < 0 ? quotient - 1 : quotient;
    }

    public BigInteger Ceiling()
    {
        var quotient = BigInteger.DivRem(Numerator, Denominator, out var remainder);
        return remainder.Sign > 0 ? quotient + 1 : quotient;
    }

    /// <summary>Lossy conversion for drawing only. Gameplay calculations remain exact.</summary>
    public double ToDouble()
    {
        if (Numerator.IsZero) return 0;
        var absolute = BigInteger.Abs(Numerator);
        var numeratorShift = Math.Max(0L, absolute.GetBitLength() - 54);
        var denominatorShift = Math.Max(0L, Denominator.GetBitLength() - 54);
        var n = (double)(absolute >> checked((int)numeratorShift));
        var d = (double)(Denominator >> checked((int)denominatorShift));
        var exponent = numeratorShift - denominatorShift;
        if (exponent > int.MaxValue) return Numerator.Sign * double.PositiveInfinity;
        if (exponent < int.MinValue) return Numerator.Sign * 0.0;
        return Numerator.Sign * Math.ScaleB(n / d, (int)exponent);
    }

    /// <summary>Rounds toward positive infinity in the displayed unit; never changes stored precision.</summary>
    public string ToDisplayString(int decimals = 2)
    {
        if (decimals < 0 || decimals > 100) throw new ArgumentOutOfRangeException(nameof(decimals));
        var scale = BigInteger.Pow(10, decimals);
        var displayed = (this * new Rational(scale)).Ceiling();
        if (decimals == 0) return displayed.ToString(CultureInfo.InvariantCulture);
        var digits = BigInteger.Abs(displayed).ToString(CultureInfo.InvariantCulture).PadLeft(decimals + 1, '0');
        return (displayed.Sign < 0 ? "-" : "") + digits[..^decimals] + "." + digits[^decimals..];
    }

    public static Rational Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        text = text.Trim();
        var slash = text.IndexOf('/');
        if (slash >= 0)
        {
            if (text.LastIndexOf('/') != slash) throw new FormatException("Invalid fraction.");
            return new Rational(BigInteger.Parse(text[..slash], CultureInfo.InvariantCulture),
                BigInteger.Parse(text[(slash + 1)..], CultureInfo.InvariantCulture));
        }
        var exponent = 0;
        var exponentIndex = text.IndexOfAny(['e', 'E']);
        if (exponentIndex >= 0)
        {
            exponent = int.Parse(text[(exponentIndex + 1)..], CultureInfo.InvariantCulture);
            if (exponent is < -10000 or > 10000) throw new FormatException("Decimal exponent is too large.");
            text = text[..exponentIndex];
        }
        var dot = text.IndexOf('.');
        var fractionalDigits = dot < 0 ? 0 : text.Length - dot - 1;
        if (dot >= 0) text = text.Remove(dot, 1);
        var value = BigInteger.Parse(text, CultureInfo.InvariantCulture);
        var power = exponent - fractionalDigits;
        return power >= 0 ? new Rational(value * BigInteger.Pow(10, power))
            : new Rational(value, BigInteger.Pow(10, -power));
    }

    public int CompareTo(Rational other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
    public bool Equals(Rational other) => Numerator == other.Numerator && Denominator == other.Denominator;
    public override bool Equals(object? obj) => obj is Rational other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);
    public override string ToString() => Denominator.IsOne ? Numerator.ToString(CultureInfo.InvariantCulture)
        : Numerator.ToString(CultureInfo.InvariantCulture) + "/" + Denominator.ToString(CultureInfo.InvariantCulture);

    public static Rational operator +(Rational left, Rational right)
    {
        var gcd = BigInteger.GreatestCommonDivisor(left.Denominator, right.Denominator);
        return new Rational(left.Numerator * (right.Denominator / gcd) + right.Numerator * (left.Denominator / gcd),
            left.Denominator * (right.Denominator / gcd));
    }
    public static Rational operator -(Rational left, Rational right) => left + -right;
    public static Rational operator -(Rational value) => new(-value.Numerator, value.Denominator);
    public static Rational operator *(Rational left, Rational right)
    {
        var a = BigInteger.GreatestCommonDivisor(BigInteger.Abs(left.Numerator), right.Denominator);
        var b = BigInteger.GreatestCommonDivisor(BigInteger.Abs(right.Numerator), left.Denominator);
        return new Rational((left.Numerator / a) * (right.Numerator / b), (left.Denominator / b) * (right.Denominator / a));
    }
    public static Rational operator /(Rational left, Rational right)
    {
        if (right.Numerator.IsZero) throw new DivideByZeroException();
        return left * new Rational(right.Denominator, right.Numerator);
    }
    public static bool operator ==(Rational left, Rational right) => left.Equals(right);
    public static bool operator !=(Rational left, Rational right) => !left.Equals(right);
    public static bool operator <(Rational left, Rational right) => left.CompareTo(right) < 0;
    public static bool operator >(Rational left, Rational right) => left.CompareTo(right) > 0;
    public static bool operator <=(Rational left, Rational right) => left.CompareTo(right) <= 0;
    public static bool operator >=(Rational left, Rational right) => left.CompareTo(right) >= 0;
    public static implicit operator Rational(int value) => new(value);
    public static implicit operator Rational(long value) => new(value);
    public static implicit operator Rational(BigInteger value) => new(value);
    public static Rational Min(Rational left, Rational right) => left <= right ? left : right;
    public static Rational Max(Rational left, Rational right) => left >= right ? left : right;
    public static Rational Clamp(Rational value, Rational minimum, Rational maximum)
    {
        if (minimum > maximum) throw new ArgumentException("The minimum exceeds the maximum.");
        return Max(minimum, Min(maximum, value));
    }
}

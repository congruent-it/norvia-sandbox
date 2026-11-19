using System.Globalization;

namespace Norvia.Docs.Diagrams;

/// <summary>
/// A number that remembers whether it was written as a whole number or as a fraction, and prints that way: <c>40</c> and <c>40.0</c> are
/// different in the generated files, and the checked-in diagrams keep the difference. Whole numbers stay whole under + - * and become
/// fractions under /, as they do in the drawings' specs.
/// </summary>
public readonly struct Num : IEquatable<Num>, IComparable<Num>
{
    private readonly double _value;

    private Num(double value, bool isFraction)
    {
        _value = value;
        IsFraction = isFraction;
    }

    public double Value => _value;

    public bool IsFraction { get; }

    public static implicit operator Num(int whole) => new(whole, false);

    public static implicit operator Num(double fraction) => new(fraction, true);

    public static Num operator +(Num a, Num b) => new(a._value + b._value, a.IsFraction || b.IsFraction);

    public static Num operator -(Num a, Num b) => new(a._value - b._value, a.IsFraction || b.IsFraction);

    public static Num operator *(Num a, Num b) => new(a._value * b._value, a.IsFraction || b.IsFraction);

    public static Num operator /(Num a, Num b) => new(a._value / b._value, true);

    public static Num operator -(Num a) => new(-a._value, a.IsFraction);

    public static bool operator <(Num a, Num b) => a._value < b._value;

    public static bool operator >(Num a, Num b) => a._value > b._value;

    public static bool operator <=(Num a, Num b) => a._value <= b._value;

    public static bool operator >=(Num a, Num b) => a._value >= b._value;

    public static bool operator ==(Num a, Num b) => a._value == b._value;

    public static bool operator !=(Num a, Num b) => a._value != b._value;

    /// <summary>The larger of two, the first when they are equal (so a whole 94 beats a fractional 94.0 that comes second).</summary>
    public static Num Max(Num a, Num b) => b > a ? b : a;

    /// <summary>The smaller of two, the first when they are equal.</summary>
    public static Num Min(Num a, Num b) => b < a ? b : a;

    public static Num Max(IEnumerable<Num> values) => values.Aggregate(Max);

    public static Num Min(IEnumerable<Num> values) => values.Aggregate(Min);

    public static Num Abs(Num a) => new(Math.Abs(a._value), a.IsFraction);

    /// <summary>The whole number part, towards zero.</summary>
    public Num Truncate() => new(Math.Truncate(_value), false);

    public static Num Sum(IEnumerable<Num> values) => values.Aggregate((Num)0, (a, b) => a + b);

    public bool Equals(Num other) => _value == other._value;

    public override bool Equals(object? obj) => obj is Num other && Equals(other);

    public override int GetHashCode() => _value.GetHashCode();

    public int CompareTo(Num other) => _value.CompareTo(other._value);

    public override string ToString() => IsFraction ? FormatFraction(_value) : ((long)_value).ToString(CultureInfo.InvariantCulture);

    /// <summary>The shortest text that reads back as the same number: <c>1.0</c>, <c>0.57</c>, <c>1e-05</c>, <c>1e+16</c>.</summary>
    private static string FormatFraction(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d))
        {
            throw new ArgumentOutOfRangeException(nameof(d), d, "A drawing cannot hold a number that is not finite.");
        }

        if (d == 0)
        {
            return double.IsNegative(d) ? "-0.0" : "0.0";
        }

        // Shortest round-trip digits, then lay them out the way a fraction is written: positional from 1e-4 up to 1e16, else with an exponent.
        var digits = ShortestDigits(d, out var pointPosition);
        var sign = d < 0 ? "-" : "";
        if (pointPosition > -4 && pointPosition <= 16)
        {
            if (pointPosition <= 0)
            {
                return sign + "0." + new string('0', -pointPosition) + digits;
            }

            if (pointPosition >= digits.Length)
            {
                return sign + digits + new string('0', pointPosition - digits.Length) + ".0";
            }

            return sign + digits[..pointPosition] + "." + digits[pointPosition..];
        }

        var exponent = pointPosition - 1;
        var mantissa = digits.Length == 1 ? digits : digits[..1] + "." + digits[1..];
        return sign + mantissa + "e" + (exponent < 0 ? "-" : "+") + Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>The significant digits of the shortest round-trip form, and where the decimal point falls: the value is 0.DIGITS x 10^position.</summary>
    private static string ShortestDigits(double d, out int pointPosition)
    {
        var text = Math.Abs(d).ToString("R", CultureInfo.InvariantCulture);
        var exponent = 0;
        var e = text.IndexOfAny(['E', 'e']);
        if (e >= 0)
        {
            exponent = int.Parse(text[(e + 1)..], CultureInfo.InvariantCulture);
            text = text[..e];
        }

        var point = text.IndexOf('.', StringComparison.Ordinal);
        var intPart = point < 0 ? text : text[..point];
        var fracPart = point < 0 ? "" : text[(point + 1)..];
        var all = intPart + fracPart;
        var leading = all.Length - all.TrimStart('0').Length;
        var digits = all.TrimStart('0').TrimEnd('0');
        pointPosition = intPart.Length - leading + exponent;
        return digits;
    }
}

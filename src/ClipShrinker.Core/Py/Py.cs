using System.Globalization;
using System.Numerics;

namespace ClipShrinker.Core.Py;

/// <summary>
/// Python semantics the 1.0.8 logic depends on: float formatting, rounding, min/max, divmod, sum, float().
/// Everything here is culture-independent.
/// </summary>
public static class Py
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Python f"{x:.Nf}" (exact rounding of the binary value, like .NET Core 3.0+).</summary>
    public static string F(double x, int digits)
    {
        if (double.IsNaN(x)) return "nan";
        if (double.IsInfinity(x)) return x > 0 ? "inf" : "-inf";
        return x.ToString("F" + digits.ToString(Inv), Inv);
    }

    /// <summary>Python f"{x:g}" (6 significant digits, trailing zeros removed, 2-digit exponent).</summary>
    public static string G(double x, int precision = 6)
    {
        if (double.IsNaN(x)) return "nan";
        if (double.IsInfinity(x)) return x > 0 ? "inf" : "-inf";
        if (x == 0) return double.IsNegative(x) ? "-0" : "0";
        string e = x.ToString("E" + (precision - 1).ToString(Inv), Inv);   // e.g. -1.23450E+004
        int ei = e.IndexOf('E');
        int exp = int.Parse(e[(ei + 1)..], NumberStyles.AllowLeadingSign, Inv);
        if (exp >= -4 && exp < precision)
            return StripZeros(x.ToString("F" + (precision - 1 - exp).ToString(Inv), Inv));
        return StripZeros(e[..ei]) + "e" + (exp < 0 ? "-" : "+") + Math.Abs(exp).ToString("00", Inv);
    }

    static string StripZeros(string s) => s.Contains('.') ? s.TrimEnd('0').TrimEnd('.') : s;

    /// <summary>Python str(int).</summary>
    public static string I(long x) => x.ToString(Inv);

    /// <summary>Python round(x) for a float: half to even, returns an int.</summary>
    public static long Round(double x) => (long)Math.Round(x, MidpointRounding.ToEven);

    /// <summary>Python round(x, n) for a float: the exact binary value rounded to n decimals, ties to even.</summary>
    public static double RoundTo(double x, int n)
    {
        if (!double.IsFinite(x)) return x;
        var (num, den) = PyFraction.FromDouble(x);
        var scaled = num * System.Numerics.BigInteger.Pow(10, n);
        var q = System.Numerics.BigInteger.DivRem(System.Numerics.BigInteger.Abs(scaled), den, out var r);
        var twice = r * 2;
        if (twice > den || (twice == den && !q.IsEven)) q += 1;
        if (scaled.Sign < 0) q = -q;
        double res = double.Parse(q.ToString() + "e-" + n.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return res == 0 && x < 0 ? -0.0 : res;
    }

    /// <summary>Python int(x) for a float: truncates toward zero.</summary>
    public static long Int(double x) => (long)Math.Truncate(x);

    /// <summary>Python a // b for ints (floor division).</summary>
    public static long FloorDiv(long a, long b)
    {
        long q = a / b;
        if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
        return q;
    }

    /// <summary>Python max(a, b): the first argument unless the second is strictly larger.</summary>
    public static double Max(double a, double b) => b > a ? b : a;

    /// <summary>Python min(a, b): the first argument unless the second is strictly smaller.</summary>
    public static double Min(double a, double b) => b < a ? b : a;

    /// <summary>Python divmod(x, y) for floats (CPython float_divmod).</summary>
    public static (double div, double mod) DivMod(double vx, double wx)
    {
        double mod = vx % wx;                 // C fmod
        double div = (vx - mod) / wx;
        if (mod != 0)
        {
            if ((wx < 0) != (mod < 0)) { mod += wx; div -= 1.0; }
        }
        else mod = Math.CopySign(0.0, wx);
        double floordiv;
        if (div != 0)
        {
            floordiv = Math.Floor(div);
            if (div - floordiv > 0.5) floordiv += 1.0;
        }
        else floordiv = Math.CopySign(0.0, vx / wx);
        return (floordiv, mod);
    }

    /// <summary>Python 3.12 sum() of floats (Neumaier compensated summation).</summary>
    public static double Sum(IEnumerable<double> xs)
    {
        double f = 0.0, c = 0.0;
        foreach (double x in xs)
        {
            double t = f + x;
            if (Math.Abs(f) >= Math.Abs(x)) c += (f - t) + x;
            else c += (x - t) + f;
            f = t;
        }
        if (c != 0 && double.IsFinite(c)) f += c;
        return f;
    }

    /// <summary>Python float(text): null when Python would raise ValueError.</summary>
    public static double? ParseFloat(string? text)
    {
        if (text is null) return null;
        string s = text.Trim().Replace("_", "");
        string l = s.ToLowerInvariant();
        string body = l.TrimStart('+', '-');
        bool neg = l.StartsWith('-');
        if (l.Length - body.Length <= 1)
        {
            if (body == "nan") return double.NaN;
            if (body is "inf" or "infinity") return neg ? double.NegativeInfinity : double.PositiveInfinity;
        }
        if (s.Length == 0 || l.Contains("inf") || l.Contains("nan")) return null;
        const NumberStyles st = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;
        return double.TryParse(s, st, Inv, out double v) ? v : null;
    }

    /// <summary>Python str.title().</summary>
    public static string Title(string s)
    {
        var chars = s.ToCharArray();
        bool prevCased = false;
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (char.IsLetter(c))
            {
                chars[i] = prevCased ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c);
                prevCased = true;
            }
            else prevCased = false;
        }
        return new string(chars);
    }
}

/// <summary>Python fractions.Fraction(float).limit_denominator(max).</summary>
public static class PyFraction
{
    public static (BigInteger num, BigInteger den) FromDouble(double x)
    {
        if (!double.IsFinite(x)) throw new ArgumentException("cannot convert NaN or infinity to a fraction");
        long bits = BitConverter.DoubleToInt64Bits(x);
        bool neg = bits < 0;
        int exp = (int)((bits >> 52) & 0x7FF);
        long man = bits & 0xFFFFFFFFFFFFFL;
        if (exp == 0) exp = 1; else man |= 1L << 52;
        exp -= 1075;
        BigInteger n = man, d = BigInteger.One;
        if (exp > 0) n <<= exp; else d <<= -exp;
        var g = BigInteger.GreatestCommonDivisor(n, d);
        if (!g.IsZero && !g.IsOne) { n /= g; d /= g; }
        if (n.IsZero) d = BigInteger.One;
        return (neg ? -n : n, d);
    }

    static BigInteger FloorDiv(BigInteger a, BigInteger b)
    {
        var q = BigInteger.DivRem(a, b, out var r);
        if (!r.IsZero && ((r.Sign < 0) != (b.Sign < 0))) q -= 1;
        return q;
    }

    public static (long num, long den) LimitDenominator(double x, long maxDenominator = 1001)
    {
        var (num, den) = FromDouble(x);
        if (den <= maxDenominator) return ((long)num, (long)den);
        BigInteger p0 = 0, q0 = 1, p1 = 1, q1 = 0, n = num, d = den;
        while (true)
        {
            var a = FloorDiv(n, d);
            var q2 = q0 + a * q1;
            if (q2 > maxDenominator) break;
            (p0, q0, p1, q1) = (p1, q1, p0 + a * p1, q2);
            (n, d) = (d, n - a * d);
        }
        var k = FloorDiv(maxDenominator - q0, q1);
        if (2 * d * (q0 + k * q1) <= den) return ((long)p1, (long)q1);
        return ((long)(p0 + k * p1), (long)(q0 + k * q1));
    }

    /// <summary>"N/D" as the reference writes Fraction(fps).limit_denominator(1001).</summary>
    public static string Ratio(double x)
    {
        var (n, d) = LimitDenominator(x);
        return Py.I(n) + "/" + Py.I(d);
    }
}

/// <summary>Python random.Random(int seed): Mersenne Twister with Python's seeding, random(), uniform(), choice().</summary>
public sealed class PyRandom
{
    const int N = 624, M = 397;
    readonly uint[] mt = new uint[N];
    int mti = N + 1;

    public PyRandom(long seed)
    {
        var abs = BigInteger.Abs(seed);
        var key = new List<uint>();
        while (!abs.IsZero) { key.Add((uint)(abs & 0xFFFFFFFF)); abs >>= 32; }
        if (key.Count == 0) key.Add(0);
        InitByArray(key.ToArray());
    }

    void InitGenrand(uint s)
    {
        mt[0] = s;
        for (mti = 1; mti < N; mti++)
            mt[mti] = unchecked(1812433253u * (mt[mti - 1] ^ (mt[mti - 1] >> 30)) + (uint)mti);
    }

    void InitByArray(uint[] key)
    {
        InitGenrand(19650218u);
        int i = 1, j = 0;
        int k = Math.Max(N, key.Length);
        unchecked
        {
            for (; k > 0; k--)
            {
                mt[i] = (mt[i] ^ ((mt[i - 1] ^ (mt[i - 1] >> 30)) * 1664525u)) + key[j] + (uint)j;
                i++; j++;
                if (i >= N) { mt[0] = mt[N - 1]; i = 1; }
                if (j >= key.Length) j = 0;
            }
            for (k = N - 1; k > 0; k--)
            {
                mt[i] = (mt[i] ^ ((mt[i - 1] ^ (mt[i - 1] >> 30)) * 1566083941u)) - (uint)i;
                i++;
                if (i >= N) { mt[0] = mt[N - 1]; i = 1; }
            }
        }
        mt[0] = 0x80000000u;
    }

    public uint NextUInt32()
    {
        uint y;
        uint[] mag01 = { 0x0u, 0x9908b0dfu };
        if (mti >= N)
        {
            int kk;
            for (kk = 0; kk < N - M; kk++)
            {
                y = (mt[kk] & 0x80000000u) | (mt[kk + 1] & 0x7fffffffu);
                mt[kk] = mt[kk + M] ^ (y >> 1) ^ mag01[y & 0x1u];
            }
            for (; kk < N - 1; kk++)
            {
                y = (mt[kk] & 0x80000000u) | (mt[kk + 1] & 0x7fffffffu);
                mt[kk] = mt[kk + (M - N)] ^ (y >> 1) ^ mag01[y & 0x1u];
            }
            y = (mt[N - 1] & 0x80000000u) | (mt[0] & 0x7fffffffu);
            mt[N - 1] = mt[M - 1] ^ (y >> 1) ^ mag01[y & 0x1u];
            mti = 0;
        }
        y = mt[mti++];
        y ^= y >> 11;
        y ^= (y << 7) & 0x9d2c5680u;
        y ^= (y << 15) & 0xefc60000u;
        y ^= y >> 18;
        return y;
    }

    public double Random()
    {
        uint a = NextUInt32() >> 5, b = NextUInt32() >> 6;
        return (a * 67108864.0 + b) * (1.0 / 9007199254740992.0);
    }

    public double Uniform(double a, double b) => a + (b - a) * Random();

    public uint GetRandBits(int k) => k == 0 ? 0 : NextUInt32() >> (32 - k);

    int RandBelow(int n)
    {
        int k = 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)n);   // n.bit_length()
        uint r = GetRandBits(k);
        while (r >= n) r = GetRandBits(k);
        return (int)r;
    }

    public T Choice<T>(IReadOnlyList<T> seq) => seq[RandBelow(seq.Count)];
}

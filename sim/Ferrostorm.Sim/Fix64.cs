namespace Ferrostorm.Sim;

/// <summary>
/// Q32.32 fixed-point number. The ONLY numeric type permitted for continuous
/// quantities inside the simulation (CLAUDE.md determinism rules, ADR-002).
/// Backed by a signed 64-bit raw value; 32 fractional bits.
/// All operations are integer arithmetic and therefore bit-identical across
/// platforms, runtimes, and compilers.
/// </summary>
public readonly struct Fix64 : IEquatable<Fix64>, IComparable<Fix64>
{
    public const int FracBits = 32;
    public readonly long Raw;

    public static readonly Fix64 Zero = new(0L);
    public static readonly Fix64 One = new(1L << FracBits);
    public static readonly Fix64 Half = new(1L << (FracBits - 1));
    public static readonly Fix64 MaxValue = new(long.MaxValue);
    public static readonly Fix64 MinValue = new(long.MinValue);

    public Fix64(long raw) => Raw = raw;

    public static Fix64 FromInt(int v) => new((long)v << FracBits);

    /// <summary>Parse "a/b" style rationals at data-load time, e.g. tuning values. Deterministic.</summary>
    public static Fix64 FromFraction(int numerator, int denominator)
    {
        if (denominator == 0) throw new DivideByZeroException("Fix64.FromFraction");
        return new Fix64((long)(((System.Int128)numerator << FracBits) / denominator));
    }

    public int ToIntFloor() => (int)(Raw >> FracBits);
    public int ToIntRound() => (int)((Raw + (1L << (FracBits - 1))) >> FracBits);

    public static Fix64 operator +(Fix64 a, Fix64 b) => new(a.Raw + b.Raw);
    public static Fix64 operator -(Fix64 a, Fix64 b) => new(a.Raw - b.Raw);
    public static Fix64 operator -(Fix64 a) => new(-a.Raw);

    public static Fix64 operator *(Fix64 a, Fix64 b)
        => new((long)(((System.Int128)a.Raw * b.Raw) >> FracBits));

    public static Fix64 operator /(Fix64 a, Fix64 b)
    {
        if (b.Raw == 0) throw new DivideByZeroException("Fix64 division");
        return new Fix64((long)(((System.Int128)a.Raw << FracBits) / b.Raw));
    }

    public static bool operator ==(Fix64 a, Fix64 b) => a.Raw == b.Raw;
    public static bool operator !=(Fix64 a, Fix64 b) => a.Raw != b.Raw;
    public static bool operator <(Fix64 a, Fix64 b) => a.Raw < b.Raw;
    public static bool operator >(Fix64 a, Fix64 b) => a.Raw > b.Raw;
    public static bool operator <=(Fix64 a, Fix64 b) => a.Raw <= b.Raw;
    public static bool operator >=(Fix64 a, Fix64 b) => a.Raw >= b.Raw;

    public static Fix64 Abs(Fix64 v) => v.Raw < 0 ? new Fix64(-v.Raw) : v;
    public static Fix64 Min(Fix64 a, Fix64 b) => a.Raw < b.Raw ? a : b;
    public static Fix64 Max(Fix64 a, Fix64 b) => a.Raw > b.Raw ? a : b;
    public static Fix64 Clamp(Fix64 v, Fix64 lo, Fix64 hi) => Max(lo, Min(hi, v));

    /// <summary>
    /// Integer Newton-Raphson square root on the raw representation.
    /// Deterministic; needed for distances. Input must be non-negative.
    /// Initial guess = 2^ceil(bits/2) which is always >= the true root, so the
    /// descent converges to the identical floor value as a naive full-width
    /// start but in ~6 iterations instead of ~90 (ADR-002 optimisation note).
    /// </summary>
    public static Fix64 Sqrt(Fix64 v)
    {
        if (v.Raw < 0) throw new ArgumentOutOfRangeException(nameof(v), "Fix64.Sqrt of negative");
        if (v.Raw == 0) return Zero;
        // sqrt(raw * 2^32) of the Q32.32 value: work in Int128 to keep precision.
        System.Int128 n = (System.Int128)v.Raw << FracBits;
        ulong hi = (ulong)(n >> 64), lo = (ulong)n;
        int bits = hi != 0
            ? 64 + System.Numerics.BitOperations.Log2(hi) + 1
            : System.Numerics.BitOperations.Log2(lo) + 1;
        System.Int128 x = (System.Int128)1 << ((bits + 1) >> 1);
        System.Int128 y = (x + n / x) >> 1;
        while (y < x)
        {
            x = y;
            y = (x + n / x) >> 1;
        }
        return new Fix64((long)x);
    }

    /// <summary>Squared distance in Fix64 space - prefer this to Sqrt in hot paths.</summary>
    public static Fix64 DistSq(Fix64 dx, Fix64 dy) => dx * dx + dy * dy;

    public bool Equals(Fix64 other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Fix64 f && f.Raw == Raw;
    public override int GetHashCode() => Raw.GetHashCode();
    public int CompareTo(Fix64 other) => Raw.CompareTo(other.Raw);

    /// <summary>Debug display only. Never feed the result back into the sim.</summary>
    public override string ToString()
    {
        long ip = Raw >> FracBits;
        ulong fp = (ulong)(Raw & 0xFFFFFFFFL) * 1_000_000UL >> FracBits;
        return $"{ip}.{fp:D6}";
    }
}

namespace Ferrostorm.Sim;

/// <summary>
/// The single PRNG owned by the simulation (CLAUDE.md bans the framework RNG in /sim).
/// splitmix64: tiny, fast, well-distributed, and trivially portable - the whole
/// state is one ulong, which makes save/replay serialisation and desync
/// forensics straightforward.
/// </summary>
public sealed class DeterministicRandom
{
    private ulong _state;

    public DeterministicRandom(ulong seed) => _state = seed;

    /// <summary>Expose state for snapshots/saves; restoring it resumes the exact sequence.</summary>
    public ulong State { get => _state; set => _state = value; }

    public ulong NextUlong()
    {
        _state += 0x9E3779B97F4A7C15UL;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform in [0, maxExclusive). Rejection-sampled to avoid modulo bias.</summary>
    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        ulong bound = (ulong)maxExclusive;
        ulong threshold = (0UL - bound) % bound;
        while (true)
        {
            ulong r = NextUlong();
            if (r >= threshold) return (int)(r % bound);
        }
    }

    public Fix64 NextFix64Unit() // [0, 1)
        => new((long)(NextUlong() >> (64 - Fix64.FracBits)));
}

/// <summary>
/// FNV-1a 64-bit running hash over simulation state. Every field that can
/// affect gameplay must be fed in each tick; two clients disagreeing on this
/// value have desynced (TDD s4, US1.2).
/// </summary>
public struct StateHash
{
    private const ulong OffsetBasis = 0xCBF29CE484222325UL;
    private const ulong Prime = 0x100000001B3UL;

    private ulong _hash;

    public static StateHash Create() => new() { _hash = OffsetBasis };

    public ulong Value => _hash;

    public void Add(long v)
    {
        unchecked
        {
            for (int i = 0; i < 8; i++)
            {
                _hash ^= (byte)(v >> (i * 8));
                _hash *= Prime;
            }
        }
    }

    public void Add(int v) => Add((long)v);
    public void Add(ulong v) => Add(unchecked((long)v));
    public void Add(bool v) => Add(v ? 1L : 0L);
    public void Add(Fix64 v) => Add(v.Raw);
}

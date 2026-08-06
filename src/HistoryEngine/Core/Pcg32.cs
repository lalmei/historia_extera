namespace HistoryEngine.Core;

/// <summary>
/// PCG32 (XSH-RR) random stream.
/// </summary>
/// <remarks>
/// <para><b>Why not <see cref="System.Random"/>:</b> its algorithm is an implementation
/// detail that has already changed once between .NET versions. This engine's whole
/// premise is that seed + config reproduces a history, so the generator has to be
/// ours and it has to be integer-only.</para>
///
/// <para><b>Why forking matters more than the generator choice.</b> The obvious design
/// is one RNG threaded through the whole simulation. It is deterministic, but it is
/// also brittle in exactly the way this project will be stressed: every draw shifts
/// the position of every later draw. Add one die roll to the war system and every
/// name, birth and battle downstream of it changes. Golden-hash tests then fail on
/// every unrelated change, which trains you to regenerate them without reading them,
/// which is the same as not having them.</para>
///
/// <para><see cref="Fork"/> derives a child stream from the parent's immutable
/// <see cref="Seed"/> rather than its live state. Substreams are therefore independent
/// of each other's consumption: deepening the war system cannot perturb naming. Effects
/// are still ordered by the system list — only the number draws are decoupled.</para>
/// </remarks>
public sealed class Pcg32 : IRng
{
    private const ulong Multiplier = 6364136223846793005UL;

    private ulong _state;
    private readonly ulong _increment;

    public ulong Seed { get; }

    public Pcg32(ulong seed)
    {
        Seed = seed;

        // The increment must be odd for the LCG to reach full period.
        _increment = (Hash.SplitMix64(seed ^ 0x5851F42D4C957F2DUL) << 1) | 1UL;

        // Standard PCG init: zero the state, step, add the seed, step again.
        _state = 0UL;
        NextUInt();
        unchecked { _state += Hash.SplitMix64(seed); }
        NextUInt();
    }

    public uint NextUInt()
    {
        unchecked
        {
            ulong old = _state;
            _state = old * Multiplier + _increment;

            uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
        }
    }

    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxExclusive), maxExclusive, "Bound must be positive.");
        }

        // Rejection sampling. Modulo alone would bias low values, and a biased die
        // roll is the kind of thing that quietly skews a whole simulation's output
        // (slightly too many hamlets, slightly too few wars) without ever looking
        // like a bug.
        uint bound = (uint)maxExclusive;
        uint threshold = (uint)-(int)bound % bound;

        while (true)
        {
            uint r = NextUInt();
            if (r >= threshold)
            {
                return (int)(r % bound);
            }
        }
    }

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxExclusive), maxExclusive, "Upper bound must exceed lower bound.");
        }

        return minInclusive + NextInt(maxExclusive - minInclusive);
    }

    public double NextDouble()
    {
        // 53 significant bits from two draws. Scaling by a power of two is exact in
        // IEEE 754, so this is bit-identical everywhere.
        ulong hi = (ulong)(NextUInt() >> 5);  // 27 bits
        ulong lo = (ulong)(NextUInt() >> 6);  // 26 bits
        return ((hi << 26) | lo) * (1.0 / 9007199254740992.0);
    }

    public double NextDouble(double min, double max) => min + (NextDouble() * (max - min));

    public bool Chance(double probability)
    {
        if (probability <= 0.0) return false;
        if (probability >= 1.0) return true;
        return NextDouble() < probability;
    }

    public T Pick<T>(IReadOnlyList<T> items)
    {
        if (items.Count == 0)
        {
            throw new ArgumentException("Cannot pick from an empty collection.", nameof(items));
        }

        return items[NextInt(items.Count)];
    }

    public IRng Fork(string purpose, long discriminator = 0)
    {
        ulong childSeed = Hash.Combine(
            Hash.Combine(Seed, Hash.OfString(purpose)),
            Hash.SplitMix64(unchecked((ulong)discriminator)));

        return new Pcg32(childSeed);
    }
}

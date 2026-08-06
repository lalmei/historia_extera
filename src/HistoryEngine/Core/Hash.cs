namespace HistoryEngine.Core;

/// <summary>
/// Deterministic hashing primitives.
/// </summary>
/// <remarks>
/// Every hash here is defined purely in terms of integer arithmetic with explicit
/// wrapping, so it produces identical results on every runtime, platform and
/// process.
///
/// <para>
/// Never use <see cref="object.GetHashCode"/> — and especially not
/// <see cref="string.GetHashCode()"/> — anywhere in the engine. String hashing in
/// .NET is randomised per process, so a seed derived from it would produce a
/// different history on every run. That is the single easiest way to silently
/// break determinism, and it fails in a way that looks like a simulation bug
/// rather than a hashing bug.
/// </para>
/// </remarks>
public static class Hash
{
    /// <summary>SplitMix64 finaliser. Strong avalanche, used to derive seeds from seeds.</summary>
    public static ulong SplitMix64(ulong x)
    {
        unchecked
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }

    /// <summary>FNV-1a over the UTF-16 code units of <paramref name="text"/>.</summary>
    public static ulong OfString(string text)
    {
        unchecked
        {
            ulong h = 0xCBF29CE484222325UL;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                h = (h ^ (byte)(c & 0xFF)) * 0x100000001B3UL;
                h = (h ^ (byte)(c >> 8)) * 0x100000001B3UL;
            }

            return h;
        }
    }

    /// <summary>Order-dependent combination of two hash values.</summary>
    public static ulong Combine(ulong a, ulong b) => SplitMix64(a ^ SplitMix64(b));

    /// <summary>Hash of a 2D lattice coordinate under a seed. Used by the noise field.</summary>
    public static ulong OfCoord(ulong seed, int x, int z)
    {
        unchecked
        {
            // Spread the two coordinates into distinct bit ranges before mixing so
            // that (x, z) and (z, x) do not collide.
            ulong k = ((ulong)(uint)x * 0xD6E8FEB86659FD93UL) ^ ((ulong)(uint)z * 0xA2AA033B68542571UL);
            return SplitMix64(seed ^ k);
        }
    }
}

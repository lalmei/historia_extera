namespace HistoryEngine.Core;

/// <summary>
/// A deterministic random number stream.
/// </summary>
/// <remarks>
/// The engine never shares one global stream. Instead, callers <see cref="Fork"/>
/// a named substream for each independent concern. See <see cref="Pcg32"/> for why
/// that matters.
/// </remarks>
public interface IRng
{
    /// <summary>The immutable seed this stream was constructed from.</summary>
    ulong Seed { get; }

    /// <summary>Next raw 32 bits.</summary>
    uint NextUInt();

    /// <summary>Uniform in [0, <paramref name="maxExclusive"/>). Rejection-sampled, so unbiased.</summary>
    int NextInt(int maxExclusive);

    /// <summary>Uniform in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
    int NextInt(int minInclusive, int maxExclusive);

    /// <summary>Uniform in [0, 1) with 53 bits of precision.</summary>
    double NextDouble();

    /// <summary>Uniform in [<paramref name="min"/>, <paramref name="max"/>).</summary>
    double NextDouble(double min, double max);

    /// <summary>True with probability <paramref name="probability"/>.</summary>
    bool Chance(double probability);

    /// <summary>Uniformly picks one element. Throws if the list is empty.</summary>
    T Pick<T>(IReadOnlyList<T> items);

    /// <summary>
    /// Picks one element in proportion to its weight. Throws if the list is empty.
    /// </summary>
    /// <remarks>
    /// Zero and negative weights are treated as none. If every weight is none, this falls
    /// back to a uniform pick rather than failing — a caller that filtered badly still
    /// produces a person, not a crash in a year that happened to have no soldiers.
    /// </remarks>
    T PickWeighted<T>(IReadOnlyList<T> items, Func<T, double> weightOf);

    /// <summary>
    /// Derives an independent substream from this stream's <em>seed</em> — never from
    /// its current position — so forking is free of ordering effects.
    /// </summary>
    /// <param name="purpose">
    /// A stable label for what the substream is for, e.g. <c>"settlement-founding"</c>.
    /// </param>
    /// <param name="discriminator">
    /// Distinguishes substreams sharing a purpose, normally an entity id, and a year
    /// as well when one entity needs a fresh stream each tick.
    /// </param>
    IRng Fork(string purpose, long discriminator = 0);
}

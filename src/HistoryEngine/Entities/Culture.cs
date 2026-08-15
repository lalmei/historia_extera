using HistoryEngine.Core;

namespace HistoryEngine.Entities;

/// <summary>How a civilization's leadership is chosen and titled.</summary>
public enum GovernmentForm
{
    Chiefdom = 0,
    Monarchy = 1,
    Theocracy = 2,
    Oligarchy = 3,
    Republic = 4,
}

/// <summary>
/// A set of dispositions, each in [0, 1].
/// </summary>
/// <remarks>
/// <para>These are the dials that make one civilization behave unlike another. They are read by
/// the systems rather than hard-coded into them, so "this civ expands relentlessly and
/// rarely fights" is data, not a branch.</para>
///
/// <para><b>A ruler's own inclinations use this same record</b> — see <see cref="Disposition"/>.
/// That is the decision the whole reign-aware layer hangs off: a separate ruler-trait vocabulary
/// would need every consuming system to learn a mapping from traits onto behaviour, and there
/// are thirty such systems to disagree with each other. Sharing the shape means blending a
/// person into a people is one function and no system needs a new branch.</para>
/// </remarks>
public sealed record CultureValues(
    double Aggression,
    double Expansionism,
    double Piety,
    double Tradition,
    double Mercantile,
    double Learning)
{
    public static CultureValues Roll(IRng rng) => new(
        Aggression: rng.NextDouble(),
        Expansionism: rng.NextDouble(),
        Piety: rng.NextDouble(),
        Tradition: rng.NextDouble(),
        Mercantile: rng.NextDouble(),

        // Drawn from a substream rather than from the next position in this one. A sixth draw
        // here would shift the government-form roll that follows in WorldBuilder and rewrite
        // every world ever generated — see Pcg32.Fork, which derives from the seed rather than
        // the live position and so consumes nothing.
        Learning: rng.Fork("learning").NextDouble());

    /// <summary>
    /// This set moved <paramref name="t"/> of the way toward <paramref name="other"/>.
    /// </summary>
    /// <remarks>
    /// How a reign displaces a people. <paramref name="t"/> is the latitude one person has to
    /// bend the culture they govern, never one: a ruler is an argument, not a replacement.
    /// </remarks>
    public CultureValues BlendToward(CultureValues other, double t) => new(
        DetMath.Lerp(Aggression, other.Aggression, t),
        DetMath.Lerp(Expansionism, other.Expansionism, t),
        DetMath.Lerp(Piety, other.Piety, t),
        DetMath.Lerp(Tradition, other.Tradition, t),
        DetMath.Lerp(Mercantile, other.Mercantile, t),
        DetMath.Lerp(Learning, other.Learning, t));

    /// <summary>Mean absolute distance across the dials, in [0, 1].</summary>
    /// <remarks>
    /// Backs both the electorate's affinity for a candidate and the divergence between a reign
    /// and the people it governs — the measure an unrest system would read.
    /// </remarks>
    public double DistanceTo(CultureValues other) =>
        (Math.Abs(Aggression - other.Aggression)
         + Math.Abs(Expansionism - other.Expansionism)
         + Math.Abs(Piety - other.Piety)
         + Math.Abs(Tradition - other.Tradition)
         + Math.Abs(Mercantile - other.Mercantile)
         + Math.Abs(Learning - other.Learning)) / 6.0;
}

/// <summary>
/// A distinct culture: its naming language, its values, and how it governs.
/// </summary>
/// <remarks>
/// Culture is separate from <see cref="Civilization"/> because they have different lifetimes.
/// A civilization can fall while its culture persists in successor states, and two
/// civilizations can share a culture — which is what makes a civil war between kin read
/// differently from a war against strangers. Nothing in Milestone 1 exploits that yet, but
/// merging the two would be difficult to unpick once dynasties and succession depend on it.
///
/// <para><see cref="LanguageSeed"/> is the handle for Milestone 3's Markov naming: the
/// per-culture blend of corpora and its phoneme mutations all derive from it, so a culture's
/// names stay coherent across every entity it ever names.</para>
/// </remarks>
public sealed class Culture
{
    public Culture(
        EntityId id,
        string name,
        ulong languageSeed,
        CultureValues values,
        GovernmentForm government)
    {
        Id = id;
        Name = name;
        LanguageSeed = languageSeed;
        Values = values;
        Government = government;
    }

    public EntityId Id { get; }

    public string Name { get; }

    /// <summary>Seed for this culture's naming language. Stable for the culture's whole life.</summary>
    public ulong LanguageSeed { get; }

    public CultureValues Values { get; }

    public GovernmentForm Government { get; }

    /// <summary>How this culture chooses the next holder of a throne.</summary>
    public SuccessionLaw Succession => SuccessionLaws.For(Government, Values);

    /// <summary>Years a ruler serves before standing down, or zero if the office is held for life.</summary>
    public int TermYears => SuccessionLaws.TermYears(Government);

    /// <summary>The title this culture gives its head of state.</summary>
    public string RulerTitle => Government switch
    {
        GovernmentForm.Chiefdom => "Chief",
        GovernmentForm.Monarchy => "King",
        GovernmentForm.Theocracy => "Hierarch",
        GovernmentForm.Oligarchy => "Archon",
        GovernmentForm.Republic => "Consul",
        _ => "Ruler",
    };

    public override string ToString() => $"{Id} {Name} ({Government})";
}

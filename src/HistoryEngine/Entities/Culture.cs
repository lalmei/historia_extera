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

    /// <summary>
    /// These values as a realm's recent past leaves them.
    /// </summary>
    /// <remarks>
    /// <para>The third layer, applied after a ruler has been blended in: what a people is, what
    /// this one wants, and then what has lately happened to them all.</para>
    ///
    /// <para>Pulls are expressed as fractions of the distance to 0 or to 1 rather than as sums, so
    /// no shift can leave the range and none of them needs clamping. It also gives the right
    /// shape: a realm that is already barely warlike has little aggression left for a defeat to
    /// take, where an additive term would drive it through the floor and be clipped.</para>
    ///
    /// <para>Only four of the six move. Tradition and Learning are what a people is rather than
    /// how it feels this decade — a plague does not make anyone less attached to their ancestral
    /// sites, and a lost battle does not make them less literate.</para>
    /// </remarks>
    public CultureValues ShiftedBy(RealmFortunes fortunes) => new(
        // A beaten realm turns defensive; an aggrieved one turns vengeful; a winning one presses.
        Aggression: Toward1(
            Toward0(Aggression, WearinessDampsAggression * fortunes.Weariness),
            (GrievanceSpursAggression * fortunes.Grievance)
            + (TriumphSpursAggression * fortunes.Triumph)),

        // Nobody founds colonies in a plague year, and success is its own argument for more.
        Expansionism: Toward1(
            Toward0(Expansionism, CalamityDampsExpansion * fortunes.Calamity),
            TriumphSpursExpansion * fortunes.Triumph),

        // Catastrophe drives people to the temple. This is the first consequence a disaster in
        // this engine has ever had beyond the people it killed.
        Piety: Toward1(Piety, CalamityDrivesPiety * fortunes.Calamity),

        Tradition: Tradition,

        // A realm too spent to fight goes back to trading with the neighbours it was fighting.
        Mercantile: Toward1(Mercantile, WearinessDrivesTrade * fortunes.Weariness),

        Learning: Learning);

    private const double WearinessDampsAggression = 0.35;
    private const double GrievanceSpursAggression = 0.30;
    private const double TriumphSpursAggression = 0.12;
    private const double CalamityDampsExpansion = 0.45;
    private const double TriumphSpursExpansion = 0.20;
    private const double CalamityDrivesPiety = 0.35;
    private const double WearinessDrivesTrade = 0.20;

    /// <summary>Moves a dial the given fraction of its remaining distance to zero.</summary>
    private static double Toward0(double value, double amount) =>
        value * (1.0 - DetMath.Clamp01(amount));

    /// <summary>Moves a dial the given fraction of its remaining distance to one.</summary>
    private static double Toward1(double value, double amount) =>
        value + ((1.0 - value) * DetMath.Clamp01(amount));

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

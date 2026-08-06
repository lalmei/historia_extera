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
/// A culture's dispositions, each in [0, 1].
/// </summary>
/// <remarks>
/// These are the dials that make one civilization behave unlike another. They are read by
/// the systems rather than hard-coded into them, so "this civ expands relentlessly and
/// rarely fights" is data, not a branch.
/// </remarks>
public sealed record CultureValues(
    double Aggression,
    double Expansionism,
    double Piety,
    double Tradition,
    double Mercantile)
{
    public static CultureValues Roll(IRng rng) => new(
        Aggression: rng.NextDouble(),
        Expansionism: rng.NextDouble(),
        Piety: rng.NextDouble(),
        Tradition: rng.NextDouble(),
        Mercantile: rng.NextDouble());
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

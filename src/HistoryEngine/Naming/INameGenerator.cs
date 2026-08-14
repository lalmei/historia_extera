using System.Globalization;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.World;

namespace HistoryEngine.Naming;

/// <summary>
/// Produces names for everything history refers to.
/// </summary>
/// <remarks>
/// <para><b>No <see cref="IRng"/> parameter.</b> Every name is a pure function of the entity's
/// id and its culture's language seed, so naming is completely independent of the order in which
/// names are requested. An implementation drawing from a passed-in stream would be deterministic
/// too, but only as long as call order never changed — and it would couple naming to every
/// unrelated reordering elsewhere in the simulation, which is exactly what
/// <see cref="IRng.Fork"/> exists to avoid.</para>
///
/// <para><b>Names are therefore not guaranteed unique.</b> Deduplicating would mean the name of
/// settlement 40 depended on which names settlements 1–39 had already taken, reintroducing the
/// order-dependence this design removes. Collisions are rare across a culture's few dozen
/// settlements, real geography is full of repeated place names, and the viewer distinguishes
/// entities by id regardless. Order-independence is worth more.</para>
/// </remarks>
public interface INameGenerator
{
    /// <summary>
    /// The language seed for a culture, derived from the world seed and the culture's id.
    /// </summary>
    /// <remarks>
    /// Defined here so there is exactly one formula. <see cref="Culture.LanguageSeed"/> stores the
    /// result, and every name that culture ever produces derives from it.
    /// </remarks>
    ulong LanguageSeedFor(EntityId cultureId);

    /// <summary>The culture's own name for itself.</summary>
    string ForCulture(EntityId id);

    string ForCivilization(EntityId id, Culture culture);

    string ForSettlement(EntityId id, Culture culture);

    string ForFigure(EntityId id, Culture culture);

    /// <summary>
    /// A ruling house's name.
    /// </summary>
    /// <remarks>
    /// Drawn from the same people-name model as its members rather than from place names, because
    /// houses are named for an ancestor far more often than for a seat, and a house whose name
    /// sounds like its kings is what makes the two read as one family.
    /// </remarks>
    string ForDynasty(EntityId id, Culture culture);

    /// <summary>
    /// A faith's name.
    /// </summary>
    /// <remarks>
    /// Drawn from the people-name model of the culture it arose among, so a faith sounds like the
    /// tongue that first preached it and keeps that sound after it has spread somewhere else — the
    /// same reason a house keeps its name in a realm it married into. Naming a faith "the Faith of
    /// Aigionanvos" would have been easier and would undercut the invented language exactly where
    /// the chronicle repeats it most.
    /// </remarks>
    string ForReligion(EntityId id, Culture culture);

    /// <summary>The proper place-name used in a holy site's composed title.</summary>
    string ForHolySite(EntityId id, Culture culture);

    /// <summary>
    /// A geographic name.
    /// </summary>
    /// <remarks>
    /// Regions are generated in bulk before any culture exists, so they are named in a
    /// world-level language of their own rather than by whoever eventually claims them. That is
    /// also the more truthful model: a river valley has a name older than the realm that holds it.
    /// </remarks>
    string ForRegion(EntityId id, Biome biome);
}

/// <summary>
/// Numbered labels, for tests and for the pre-naming milestones.
/// </summary>
/// <remarks>
/// Deliberately unmistakable for real output, and retained after Milestone 3 because it makes
/// simulation tests read clearly — a test asserting something about <c>Settlement 7</c> is easier
/// to follow than one about <c>Kirethholm</c>, and it is far faster than training Markov models.
/// </remarks>
public sealed class PlaceholderNameGenerator : INameGenerator
{
    public ulong LanguageSeedFor(EntityId cultureId) =>
        Hash.SplitMix64((ulong)cultureId.ToDiscriminator());

    public string ForCulture(EntityId id) => Label("Culture", id);

    public string ForCivilization(EntityId id, Culture culture) => Label("Civ", id);

    public string ForSettlement(EntityId id, Culture culture) => Label("Settlement", id);

    public string ForFigure(EntityId id, Culture culture) => Label("Figure", id);

    public string ForDynasty(EntityId id, Culture culture) => Label("House", id);

    public string ForReligion(EntityId id, Culture culture) => Label("Faith", id);

    public string ForHolySite(EntityId id, Culture culture) => Label("Holy Site", id);

    public string ForRegion(EntityId id, Biome biome) =>
        biome.ToString() + " " + id.Index.ToString(CultureInfo.InvariantCulture);

    private static string Label(string prefix, EntityId id) =>
        prefix + " " + id.Index.ToString(CultureInfo.InvariantCulture);
}

using System.Globalization;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.World;

namespace HistoryEngine.Naming;

/// <summary>
/// Produces names for everything history refers to.
/// </summary>
/// <remarks>
/// <para>Every method takes the <see cref="EntityId"/> being named. That is deliberate: it
/// lets an implementation derive a name from the id alone, so naming is independent of the
/// order in which names are requested. An implementation holding an internal counter would
/// be deterministic too, but only as long as call order never changed — and it would couple
/// naming to every unrelated reordering elsewhere in the simulation, which is exactly what
/// <see cref="IRng.Fork"/> exists to avoid.</para>
///
/// <para><b>Milestone 3</b> replaces <see cref="PlaceholderNameGenerator"/> with per-culture
/// Markov chains trained on a blend of public-domain corpora, keyed on
/// <see cref="Culture.LanguageSeed"/>. This interface is the seam, and it is why the slice
/// can ship with obviously-fake names without any of the simulation caring.</para>
/// </remarks>
public interface INameGenerator
{
    string ForCulture(EntityId id, IRng rng);

    string ForCivilization(EntityId id, Culture culture, IRng rng);

    string ForSettlement(EntityId id, Culture culture, IRng rng);

    string ForFigure(EntityId id, Culture culture, IRng rng);

    string ForRegion(EntityId id, Biome biome);
}

/// <summary>
/// Milestone 1's stand-in: numbered labels derived from entity ids.
/// </summary>
/// <remarks>
/// Deliberately unmistakable for real output. Placeholder names that look plausible are worse
/// than obviously fake ones, because they hide the fact that the naming milestone has not
/// happened yet.
/// </remarks>
public sealed class PlaceholderNameGenerator : INameGenerator
{
    public string ForCulture(EntityId id, IRng rng) => Label("Culture", id);

    public string ForCivilization(EntityId id, Culture culture, IRng rng) => Label("Civ", id);

    public string ForSettlement(EntityId id, Culture culture, IRng rng) => Label("Settlement", id);

    public string ForFigure(EntityId id, Culture culture, IRng rng) => Label("Figure", id);

    /// <summary>Regions read as their biome plus an index, so territory events stay legible.</summary>
    public string ForRegion(EntityId id, Biome biome) =>
        biome.ToString() + " " + id.Index.ToString(CultureInfo.InvariantCulture);

    private static string Label(string prefix, EntityId id) =>
        prefix + " " + id.Index.ToString(CultureInfo.InvariantCulture);
}

using System.Globalization;
using HistoryEngine.Naming;

namespace HistoryEngine.World;

/// <summary>Whether this history is set on a world of its own, or on someone else's moon.</summary>
public enum WorldKind
{
    Planet = 0,
    Moon = 1,
}

/// <summary>
/// The world's own name, and whether it is a planet or a moon.
/// </summary>
/// <remarks>
/// <para>Rolled once from the seed, before any civilization is founded, and never revised. It is
/// flavour — it does not change the land, the people, or any die the simulation later throws —
/// but it is how a reader tells one history from another in a list of them, which a seed number
/// alone does not.</para>
///
/// <para><b>A pure function of the seed.</b> The same seed always produces the same designation,
/// including when the run is longer, shorter, or asked for a different number of civilizations.
        /// The name generator's world language supplies the proper nouns. Planet versus moon, and
        /// which satellite is habitable, come from <see cref="WorldCosmology"/> so the designation
        /// cannot claim an eighth moon when the giant only has three. Names stay derived from the
        /// seed rather than from <c>WorldState.Root</c>, so adding flavour cannot shift a founding.</para>
///
/// <para>The designation is what the overview and the chronicle print: "The planet Borion",
/// "The 3rd moon of Endor", "Ithil, the 3rd moon of Endor". The seed still travels beside it in
/// the export, because that is what you type to get the same history again.</para>
/// </remarks>
public sealed record WorldFlavour(
    WorldKind Kind,
    string Name,
    string Designation,
    string? ParentName,
    int? MoonIndex,
    WorldCosmology Cosmology)
{
    /// <summary>
    /// Chance the world is a moon rather than a planet. High enough that compound names show up
    /// in a handful of seeds, low enough that most histories are still a named planet.
    /// </summary>
    internal const double MoonChance = WorldCosmology.MoonChance;

    /// <summary>
    /// Composes the world's identity from the seed and the name generator already used for its
    /// geography.
    /// </summary>
    public static WorldFlavour From(ulong seed, INameGenerator names)
    {
        WorldCosmology cosmology = WorldCosmology.From(seed);

        string body = names.ForWorld(WorldNameRole.Body);
        string other = names.ForWorld(WorldNameRole.Parent);

        if (cosmology.Kind == WorldKind.Planet)
        {
            // Two proper nouns when they differ, so "The planet Borion" still happens and
            // "The planet Borion of the Vathri system" is what keeps two seeds apart when
            // the Markov draw would otherwise repeat a popular root.
            string designation = body.Equals(other, StringComparison.OrdinalIgnoreCase)
                ? "The planet " + body
                : "The planet " + body + " of the " + other + " system";

            return new WorldFlavour(
                WorldKind.Planet,
                body,
                designation,
                ParentName: null,
                MoonIndex: null,
                cosmology);
        }

        string parent = other;
        int index = cosmology.HabitableMoonIndex ?? 1;
        string designationMoon = body + ", the " + Ordinal(index) + " moon of " + parent;

        return new WorldFlavour(WorldKind.Moon, body, designationMoon, parent, index, cosmology);
    }

    /// <summary>
    /// English ordinal in invariant digits: 1st, 2nd, 3rd, 11th.
    /// </summary>
    /// <remarks>
    /// Invariant rather than current-culture so a Turkish-locale machine cannot change the
    /// exported bytes. The words themselves are English because the designation's frame —
    /// "planet", "moon of" — is English; the proper names inside it are not.
    /// </remarks>
    internal static string Ordinal(int n)
    {
        int lastTwo = n % 100;
        int last = n % 10;
        string suffix = lastTwo is 11 or 12 or 13
            ? "th"
            : last == 1 ? "st"
            : last == 2 ? "nd"
            : last == 3 ? "rd"
            : "th";

        return n.ToString(CultureInfo.InvariantCulture) + suffix;
    }
}

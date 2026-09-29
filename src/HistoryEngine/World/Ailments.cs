using HistoryEngine.Core;
using HistoryEngine.Entities;

namespace HistoryEngine.World;

/// <summary>
/// What the record names when an ordinary death is put down to sickness.
/// </summary>
/// <remarks>
/// <para><b>Flavour, not a disease model.</b> Mortality is decided by age alone in
/// <see cref="Systems.FigureLifecycleSystem"/>, and nothing here changes who dies or when. What
/// it changes is the line: "died at the age of 2, of illness" appeared nearly fifteen hundred
/// times in a 400-year world, from the cradle to middle age, and no parish register ever wrote
/// "illness". A register wrote what the family called it, and what they called it depended on
/// how old the dead were and where they lived.</para>
///
/// <para><b>Named the way the living named them</b>, not by modern diagnosis: a cradle fever, the
/// croup, a flux, an ague, the quinsy, a wasting sickness. Brackets follow who actually died of
/// what in pre-modern registers — infants of fevers and fluxes, children of the eruptive and
/// throat diseases, adults of fevers, fluxes and festering wounds, the ageing of palsies, dropsies
/// and failing hearts. Ground adds a little: ague on wet ground, the gaol fever in a city, the
/// miner's cough where there is a mine.</para>
///
/// <para><b>Its own stream.</b> Forked from the root by figure and year, so naming a death draws
/// nothing from any stream a decision is made on. Adding a name to one of these lists changes
/// which name a death gets and nothing else.</para>
/// </remarks>
public static class Ailments
{
    private static readonly string[] Cradle =
    {
        "a fever in the cradle",
        "the croup",
        "a flux of the bowels",
        "a wasting in the cradle",
        "convulsions",
        "a chill of the lungs",
    };

    private static readonly string[] Childhood =
    {
        "the spotted fever",
        "the throat distemper",
        "the whooping cough",
        "the bloody flux",
        "a chill of the lungs",
        "a fever",
        "the red rash",
    };

    private static readonly string[] Prime =
    {
        "a fever",
        "the bloody flux",
        "a lung fever",
        "a wasting sickness",
        "a festering wound",
        "a sickness of the belly",
        "the quinsy",
        "a putrid throat",
        "a fever of the blood",
    };

    private static readonly string[] Ageing =
    {
        "a palsy",
        "the dropsy",
        "a failing of the heart",
        "a wasting sickness",
        "a lung fever",
        "the stone",
        "a fever",
    };

    /// <summary>The named sickness a death at this age, in this place, is put down to.</summary>
    public static string For(WorldState world, Figure figure, int year)
    {
        int age = figure.AgeIn(year);
        IRng pick = world.Root
            .Fork("ailment", figure.Id.ToDiscriminator())
            .Fork("year", year);

        string[] pool = age < 2 ? Cradle
            : age < 13 ? Childhood
            : age < 45 ? Prime
            : Ageing;

        // Where they lived adds to the pool rather than replacing it: an estuary town still loses
        // people to the flux, it just also loses them to the ague.
        string? local = age >= 2 ? Local(world, figure, age) : null;
        if (local is not null && pick.Chance(0.35)) return local;

        return pool[pick.NextInt(pool.Length)];
    }

    private static string? Local(WorldState world, Figure figure, int age)
    {
        EntityId home = world.ResidenceOf(figure);
        if (!world.Settlements.Contains(home)) return null;

        Settlement town = world.Settlements[home];
        return town.Site switch
        {
            SiteCharacter.Estuary or SiteCharacter.Confluence or SiteCharacter.Riverside
                => "the marsh ague",
            SiteCharacter.Mine or SiteCharacter.Quarry when age >= 13
                => "the miner's cough",
            _ when town.Tier == SettlementTier.City => "the gaol fever",
            _ => null,
        };
    }

    /// <summary>
    /// How the obituary gives an age: "in infancy", "as a child of 6", "at the age of 50".
    /// </summary>
    /// <remarks>
    /// The number stays in its own data key for every page that reads it; this is only the words.
    /// A death in the first year given as "at the age of 0" is the kind of line that makes a
    /// reader stop trusting the rest.
    /// </remarks>
    public static string AgePhrase(int age) => age switch
    {
        < 1 => "in infancy",
        1 => "in infancy, not yet two",
        < 13 => "as a child of " + age.ToString(System.Globalization.CultureInfo.InvariantCulture),
        < 18 => "at only " + age.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => "at the age of " + age.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}

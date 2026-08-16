using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Terrain;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Claims neighbouring regions and founds new settlements in them.
/// </summary>
/// <remarks>
/// Expansion pressure comes from a civilization's own success — population per settlement —
/// multiplied by how expansionist its culture is. So a crowded, restless civilization spreads
/// and a small or sedentary one does not, without either being scripted.
///
/// <para>Candidate regions are found by walking region adjacency outward from owned territory,
/// which means expansion follows the shape of the land: a civilization hemmed in by mountains
/// or coast runs out of room, and two civilizations growing toward each other eventually
/// contend for the same regions. That contention is the seam Milestone 6's diplomacy and war
/// plugs into.</para>
/// </remarks>
public sealed class ExpansionSystem : ISystem
{
    /// <summary>
    /// Baseline chance of founding a settlement in one open season, before culture and pressure.
    /// </summary>
    /// <remarks>
    /// The conversion of the 0.06 a year this was before expansion left the year:
    /// <c>1 − (1 − 0.06)^¼ = 0.0153</c>. It is scaled up again for a frontier that is not open all
    /// year, so the yearly total is the same wherever the frontier is — see <see cref="Tick"/>.
    /// </remarks>
    private const double BaseChance = 0.0153;

    /// <summary>Population per existing settlement at which pressure is considered full.</summary>
    private const double PressureReference = 2200.0;

    /// <summary>Starting population of a new settlement.</summary>
    private const int SettlerCount = 70;

    /// <summary>
    /// People a departure point keeps whatever else happens.
    /// </summary>
    /// <remarks>
    /// A settlement does not colonise itself out of existence. Without a floor, a realm reduced to
    /// one struggling village would send parties out of it until the abandonment threshold
    /// finished it — and expansion, which is supposed to be a sign of success, would become a way
    /// for a failing realm to kill itself.
    /// </remarks>
    private const int MinimumLeftBehind = 140;

    /// <summary>A party smaller than this is not worth sending, and would not survive.</summary>
    private const int MinimumParty = 25;

    /// <summary>
    /// Candidate sites per axis. The same as a capital's, which it did not used to be.
    /// </summary>
    /// <remarks>
    /// This was 4 on the reasoning that a colony is a smaller bet than a capital and deserves a
    /// cheaper decision. M10 measured what that bought: at four sites per axis a 128-unit region is
    /// refined every 32 units, and the ground a settlement stands on simply is not visible at that
    /// spacing. Colonies were sited on a grade steeper than 1-in-2 21.1% of the time against a
    /// capital's 10.9%, and the worst site in eight worlds stood on a 2.9 grade — a cliff. At the
    /// same spacing as a capital the colony figure falls to 13.0% and the worst site in eight
    /// worlds to 1.2.
    ///
    /// <para>The bet is smaller; the ground is the same ground. Nine in ten settlements in a
    /// finished history are colonies, so this was never a cheap decision made rarely — it was the
    /// decision, made at half the resolution of the one nobody minded paying for.</para>
    /// </remarks>
    private const int SitesPerAxis = 8;

    public string Name => "expansion";

    public Cadence Cadence => Cadence.Seasonal;

    /// <summary>
    /// Sends out the season's settling parties.
    /// </summary>
    /// <remarks>
    /// <para><b>A party sets out for ground it can arrive on</b>, read from the region being
    /// settled rather than the realm doing the settling: what stops a colony is the frontier being
    /// frozen when they get there, not the capital being cold when they leave.</para>
    ///
    /// <para><b>The season decides when, not how often, and that is the difference from war.</b> A
    /// war is running continuously and a closed season genuinely removes chances to fight, so the
    /// north fights less over a year. Colonising is a decision of state taken when pressure builds,
    /// and pressure does not evaporate over the winter — the settlers wait for spring. So the
    /// yearly weight is preserved and only its timing moves: a frontier open two seasons in four
    /// gets half as many chances at twice the odds, and a realm pushing north founds as many
    /// colonies as a tropical one, in summer.</para>
    ///
    /// <para><b>Ground that never thaws is not made unreachable.</b> The first attempt at this
    /// gated on the season alone, which quietly stalled any realm whose best frontier was
    /// permanently frozen: it waited for a spring that never came, seed 7 lost 22% of its colonies,
    /// and the population that should have gone to them concentrated into cities until a third of
    /// its settlements were one. A frontier with no open season has no better season to wait for,
    /// so it is settled whenever it is settled.</para>
    /// </remarks>
    public void Tick(WorldState world, Stamp now)
    {
        int year = now.Year;
        Calendar calendar = world.Config.Calendar;
        int season = now.Day / calendar.DaysPerSeason;

        IRng rng = world.Root.Fork(Name, calendar.AbsoluteDay(now) / calendar.DaysPerSeason);

        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            int settlementCount = CountActive(world, civilization);
            if (settlementCount == 0) continue;

            Culture culture = world.CultureOf(civilization);

            // Found before the roll rather than after it, because how open the target is decides
            // what the roll has to be. It draws no numbers, so nothing else moves for asking early
            // — which is also what lets the need be decided here rather than after the roll.
            FrontierChoice? frontier = Colonisation.Frontier(world, civilization);
            if (frontier is null) continue;

            Region target = frontier.Value.Region;

            int open = Seasons.OpenSeasons(target, calendar.SeasonsPerYear, world.Config.WorldSize);

            if (open > 0)
            {
                if (!Seasons.Campaigning(target, season, calendar.SeasonsPerYear, world.Config.WorldSize))
                {
                    continue;
                }
            }
            else if (season != 0)
            {
                // Frozen the year round: one chance a year, in the opening season, rather than
                // four at a season's odds.
                continue;
            }

            double pressure = DetMath.Clamp01(
                civilization.Population / (settlementCount * PressureReference));

            // Founding a colony is a decision of state, so it is the realm's effective
            // expansionism rather than its culture's: a plague year suppresses it, a run of
            // victories encourages it, and an unambitious king declines to do it at all.
            double chance = BaseChance
                * (open > 0 ? calendar.SeasonsPerYear / (double)open : calendar.SeasonsPerYear)
                * (0.25 + world.ValuesFor(civilization).Expansionism)
                * pressure;
            if (!rng.Chance(chance)) continue;

            Claim(world, civilization, culture, frontier.Value, year, rng);
        }
    }

    private static int CountActive(WorldState world, Civilization civilization)
    {
        int count = 0;
        foreach (EntityId id in civilization.SettlementIds)
        {
            if (world.Settlements[id].IsActive) count++;
        }

        return count;
    }

    /// <remarks>
    /// Which region, and what for, is <see cref="Colonisation"/>'s decision — this claims what it
    /// named. The need reaches the ground twice: once in <see cref="SiteSelection"/>, which scores
    /// the candidates for what the party came to do, and once in the character the site keeps, which
    /// is what lets the chronicle say why they went.
    /// </remarks>
    private static void Claim(
        WorldState world,
        Civilization civilization,
        Culture culture,
        FrontierChoice frontier,
        int year,
        IRng rng)
    {
        Region target = frontier.Region;

        target.Owner = civilization.Id;
        civilization.TerritoryRegionIds.Add(target.Id);

        world.Chronicle.Record(
            year, EventKind.RegionClaimed, target.Id, obj: civilization.Id);

        SiteChoice site = SiteSelection.Best(world, target, SitesPerAxis, frontier.Need);

        // Settlers come out of somewhere. Conjuring them made expansion free, so a realm could
        // seed a continent without ever feeling it; taking them from the nearest town means a
        // frontier is paid for by the places behind it.
        Settlement? parent = Departure(world, civilization, target);
        int settlers = SettlerCount;

        if (parent is not null)
        {
            settlers = Math.Min(SettlerCount, parent.Population - MinimumLeftBehind);
            if (settlers < MinimumParty) return;

            parent.Population -= settlers;
        }

        // A cadet ranked out of the line is precisely who is sent: the heir is needed at home,
        // and a fourth son with no prospect of a throne has every reason to take one. This is the
        // loop the offices design named and did not build — a house planted in a colony, whose
        // children are born there, is where a breakaway realm eventually comes from.
        List<Figure> spare = Offices.Courtiers(world, civilization, year);
        Figure? leader = spare.Count == 0 ? null : rng.Pick(spare);

        WorldBuilder.FoundSettlement(
            world, civilization, culture, target, site, year, settlers, rng, parent, leader);
    }

    /// <summary>
    /// The settlement a founding party comes out of: the realm's nearest active town to the site.
    /// </summary>
    /// <remarks>
    /// Nearest rather than largest, because a party walks. Distance goes through
    /// <see cref="WorldState.Distance"/> so it takes the short way across the seam on a periodic
    /// world, and ties break on id — two settlements equidistant from a third is not rare on a grid
    /// and <see cref="List{T}.Sort"/> would otherwise order them unpredictably.
    /// </remarks>
    private static Settlement? Departure(
        WorldState world, Civilization civilization, Region target)
    {
        Settlement? nearest = null;
        double best = double.PositiveInfinity;

        foreach (Settlement candidate in world.ActiveSettlementsOf(civilization))
        {
            if (candidate.Population <= MinimumLeftBehind) continue;

            double distance = world.Distance(
                candidate.X, candidate.Z, target.CenterX, target.CenterZ);

            if (distance < best
                || (distance == best && nearest is not null && candidate.Id.CompareTo(nearest.Id) < 0))
            {
                best = distance;
                nearest = candidate;
            }
        }

        return nearest;
    }
}

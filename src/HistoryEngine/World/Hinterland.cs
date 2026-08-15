using HistoryEngine.Core;
using HistoryEngine.Entities;

namespace HistoryEngine.World;

/// <summary>
/// How much of the surrounding country each settlement feeds itself from, when neighbours are
/// close enough to want the same fields.
/// </summary>
/// <remarks>
/// <para><b>Why the land has to be contested.</b> Carrying capacity used to read a settlement's
/// region and stop, and every settlement stands alone in its own region — so each of them drew on
/// a whole region's fertility as though nobody else existed. Two towns eight hundred units apart
/// were each fed by land the other was also being fed by, and the same acre was counted twice.
/// There was consequently no mechanism anywhere in the model by which a settlement could be
/// <em>kept</em> small. It could only be killed, and the abandonment machinery duly killed the
/// ones that failed — leaving a world of survivors that were all the same size.</para>
///
/// <para><b>What that produced</b>, over thousand-year runs on five seeds: settlement hierarchies
/// with the shape upside down, and worse the more successful the world was. Seed 42 finished with
/// 66% of its settlements towns against 17% villages and 8% hamlets. Seed 7 finished with
/// <em>75% of every settlement in the world a city</em> — 301 settlements holding 1.6 million
/// people, median population 5,711 — and seed 101 with 47% cities. The tier ladder was reporting a
/// world of capitals because the model had no way to tell one place from another.</para>
///
/// <para><b>What this is.</b> Central-place geography in its smallest useful form: a settlement's
/// share of the ground within reach is its own pull over the total pull on that ground, so a
/// village beside a city keeps a village's worth of fields and a village alone on a frontier keeps
/// all of them. Nothing here kills anything. It is the missing way to be permanently, stably
/// small, which is what the great majority of settlements in a real landscape are.</para>
///
/// <para><b>Pull is the square root of population, and that exponent is the whole calibration.</b>
/// The mechanism is preferential attachment — a bigger place takes more land, which lets it grow
/// bigger — and taken linearly it runs away: a city of 4,900 beside a hamlet of 100 would take 98%
/// of what lay between them and the hamlet would be gone in a decade, so the world would converge
/// on one settlement per neighbourhood rather than on a hierarchy. Square-rooted, the same pair
/// splits it 88/12, which suppresses the hamlet permanently without extinguishing it. A tail needs
/// the feedback to be positive and sublinear; either one alone gives a hump.</para>
///
/// <para>Samples no terrain. It reads coordinates and populations the simulation already holds,
/// which is what lets it run for every settlement every year.</para>
/// </remarks>
public sealed class Hinterland
{
    /// <summary>
    /// How far a settlement draws its food from, in world units.
    /// </summary>
    /// <remarks>
    /// <para>Shorter than a plague's casual-contact jump (950) and much shorter than trade's reach
    /// (1600), because this is the distance a cart goes to market and comes back the same day.
    /// Anything beyond it arrives as trade instead, which the capacity model already pays for
    /// separately through <see cref="Specializations.ImportReliance"/>.</para>
    ///
    /// <para>Swept against <see cref="Systems.PopulationSystem"/>'s fertility scale over
    /// thousand-year runs. Both ends fail in the same direction and for the same reason: at 300 a
    /// settlement competes only with the region it already stands alone in, so the hump comes
    /// straight back (46% of settlements between 1,500 and 4,000, against 29% here), and much past
    /// 800 the neighbourhood is large enough that everything in it competes with everything else
    /// equally, which is a uniform tax rather than a hierarchy.</para>
    /// </remarks>
    private const double Reach = 700.0;

    /// <summary>Pull attributed to a settlement with nobody living in it yet.</summary>
    /// <remarks>
    /// A colony founded with seventy people has a real claim on the ground it was planted in, and
    /// starting its pull at zero would let an established neighbour take all of it and strangle
    /// every colony founded anywhere near an existing town.
    /// </remarks>
    private const double MinimumPull = 6.0;

    private readonly List<Reading> _readings;

    private Hinterland(List<Reading> readings) => _readings = readings;

    /// <summary>Takes one consistent picture of who is where, and how large.</summary>
    public static Hinterland Survey(WorldState world)
    {
        var readings = new List<Reading>();

        foreach (Settlement settlement in world.Settlements)
        {
            if (!settlement.IsActive) continue;
            readings.Add(new Reading(settlement.Id, settlement.X, settlement.Z, Pull(settlement.Population)));
        }

        return new Hinterland(readings);
    }

    /// <summary>
    /// This settlement's share of the country within reach, in (0, 1].
    /// </summary>
    /// <remarks>
    /// Distance-weighted, so a neighbour at the edge of the reach barely competes and one in the
    /// next valley competes hard. Squared falloff rather than linear: the fields a settlement
    /// actually works are concentrated near it, so the contested ground is too.
    /// </remarks>
    public double ShareFor(WorldState world, Settlement settlement)
    {
        double own = Pull(settlement.Population);
        double rivals = 0.0;

        foreach (Reading other in _readings)
        {
            if (other.Id == settlement.Id) continue;

            double distance = world.Distance(settlement.X, settlement.Z, other.X, other.Z);
            if (distance >= Reach) continue;

            double nearness = 1.0 - DetMath.InverseLerp(0.0, Reach, distance);
            rivals += other.Pull * nearness * nearness;
        }

        return own / (own + rivals);
    }

    private static double Pull(int population) =>
        Math.Max(MinimumPull, DetMath.Sqrt(Math.Max(0, population)));

    /// <summary>One settlement as the survey found it, before anybody grew this year.</summary>
    private readonly record struct Reading(EntityId Id, int X, int Z, double Pull);
}

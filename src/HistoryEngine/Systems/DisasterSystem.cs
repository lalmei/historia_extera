using System.Globalization;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>What befell a place. Explicit values — written into the chronicle as prose.</summary>
public enum DisasterKind
{
    Earthquake = 0,
    Eruption = 1,
    Flood = 2,
    Storm = 3,
    Wildfire = 4,
    Blizzard = 5,
}

/// <summary>
/// The land turning on the people living off it.
/// </summary>
/// <remarks>
/// <para><b>Every disaster is drawn from the terrain the settlement actually sits on.</b> A town
/// only floods if its region has a river, only burns if the region is dry, and only feels an
/// eruption where the geology is violent — which means the map explains the chronicle. A world
/// where a coastal trading city is wrecked by storms twice a century and the mining town in the
/// mountains is shaken instead is a world whose disasters are readable as consequences of where
/// people chose to live, and site selection is a decision the engine already makes.</para>
///
/// <para><b>It costs nothing to ask.</b> Every trait consulted — geologic activity, rainfall,
/// temperature, river, coast, biome — is a region statistic derived once at world creation, so a
/// system whose entire subject is terrain adds not one sample to the budget.</para>
///
/// <para>Disasters are deliberately smaller than plagues and rarer than famines. They are the
/// third and least of the ways a settlement can be hurt, and they exist mainly so that decline
/// has a cause that is neither weather nor war: a town knocked down twice in a decade is one the
/// lifecycle can then finish, without this system needing a rule for ruin.</para>
/// </remarks>
public sealed class DisasterSystem : IYearSystem
{
    /// <summary>Yearly chance per settlement, before the region's hazard scales it.</summary>
    private const double BaseChance = 0.0075;

    /// <summary>Loss below which nothing is recorded — a bad storm is not a disaster.</summary>
    private const int NotableLoss = 8;

    /// <summary>
    /// Share of a disaster's population severity inherited as risk by each member of the court.
    /// </summary>
    /// <remarks>
    /// Figures have a realm residence but not a continuously simulated street address. The court is
    /// therefore exposed only when the capital itself is struck. At 0.22, an ordinary ten-percent
    /// disaster gives each courtier about a two-percent risk: enough for a calamity to reach the
    /// dynasty, far below turning every damaged capital into a mass extinction.
    /// </remarks>
    private const double CourtExposure = 0.22;

    public string Name => "disaster";

    public void Tick(WorldState world, int year)
    {
        IRng rng = world.Root.Fork(Name, year);

        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            foreach (Settlement settlement in world.ActiveSettlementsOf(civilization))
            {
                Region region = world.Regions[settlement.RegionId];

                DisasterKind kind = Worst(region, out double hazard);
                if (hazard <= 0.0) continue;

                if (!rng.Chance(BaseChance * hazard)) continue;

                Strike(world, settlement, region, kind, year, rng);
            }
        }
    }

    /// <summary>
    /// The disaster this ground is most capable of, and how capable it is.
    /// </summary>
    /// <remarks>
    /// One kind per place rather than a weighted draw across all six. A region's hazards are not
    /// interchangeable — the valley floods and the volcano erupts — and picking the characteristic
    /// one means a settlement's disasters read as the same recurring danger over centuries rather
    /// than as a lottery, which is what makes them feel like geography.
    /// </remarks>
    private static DisasterKind Worst(Region region, out double hazard)
    {
        DisasterKind worst = DisasterKind.Storm;
        hazard = 0.0;

        foreach (DisasterKind candidate in Candidates)
        {
            double score = Hazard(candidate, region);

            // Strictly greater, so the fixed candidate order breaks exact ties.
            if (score > hazard)
            {
                hazard = score;
                worst = candidate;
            }
        }

        return worst;
    }

    /// <summary>Fixed order, so ties resolve reproducibly.</summary>
    private static readonly DisasterKind[] Candidates =
    {
        DisasterKind.Eruption,
        DisasterKind.Earthquake,
        DisasterKind.Flood,
        DisasterKind.Storm,
        DisasterKind.Wildfire,
        DisasterKind.Blizzard,
    };

    private static double Hazard(DisasterKind kind, Region region) => kind switch
    {
        // Violent geology and height. Rare, and the most destructive thing here.
        DisasterKind.Eruption =>
            region.GeologicActivity < 0.72 || region.MeanHeight < 700.0
                ? 0.0
                : DetMath.InverseLerp(0.72, 1.0, region.GeologicActivity) * 0.9,

        DisasterKind.Earthquake =>
            region.GeologicActivity < 0.4
                ? 0.0
                : DetMath.InverseLerp(0.4, 1.0, region.GeologicActivity) * 0.8,

        // Needs a river to overtop, and wet years to fill it.
        DisasterKind.Flood =>
            !region.HasRiver ? 0.0 : 0.25 + (DetMath.InverseLerp(0.35, 0.9, region.Rainfall) * 0.6),

        DisasterKind.Storm =>
            !region.IsCoastal ? 0.0 : 0.3 + (DetMath.InverseLerp(0.3, 0.85, region.Rainfall) * 0.35),

        // Dry, warm, and something to burn.
        DisasterKind.Wildfire =>
            !Burnable(region.Biome)
                ? 0.0
                : DetMath.InverseLerp(0.55, 0.12, region.Rainfall)
                  * DetMath.InverseLerp(0.35, 0.85, region.Temperature)
                  * 0.75,

        DisasterKind.Blizzard =>
            region.Temperature > 0.3
                ? 0.0
                : DetMath.InverseLerp(0.3, 0.0, region.Temperature) * 0.55,

        _ => 0.0,
    };

    private static bool Burnable(Biome biome) => biome switch
    {
        Biome.TemperateForest => true,
        Biome.TropicalForest => true,
        Biome.Taiga => true,
        Biome.Savanna => true,
        Biome.Grassland => true,
        Biome.Steppe => true,
        _ => false,
    };

    private static void Strike(
        WorldState world,
        Settlement settlement,
        Region region,
        DisasterKind kind,
        int year,
        IRng rng)
    {
        int before = settlement.Population;
        double severity = Severity(kind, rng);
        int lost = (int)(before * severity);

        if (lost > 0)
        {
            settlement.Population = Math.Max(0, before - lost);

            Civilization owner = world.Civilizations[settlement.CivilizationId];
            owner.Population = Math.Max(0, owner.Population - lost);
        }

        Realms.Suffered(world, settlement, lost);

        // Walls come down where the ground moves, which leaves the place easier to take — the
        // same coupling a sacking has, arrived at from the other direction.
        if (Levels(kind) && settlement.IsFortified && rng.Chance(0.5)) settlement.IsFortified = false;

        // Fire and lava take what a treasury was holding. A flood or a gale does not.
        if (Burns(kind) && rng.Chance(0.35))
        {
            Treasures.LoseOne(world, settlement, year, Label(kind), rng);
        }

        List<Figure> courtDead = CourtCasualties(world, settlement, severity, year, rng);

        if (lost < NotableLoss && settlement.Tier < SettlementTier.Village && courtDead.Count == 0)
        {
            return;
        }

        var data = Chronicle.Data(("kind", Label(kind)));
        if (lost >= NotableLoss) data["lost"] = lost.ToString(CultureInfo.InvariantCulture);

        world.Chronicle.Record(
            year,
            EventKind.DisasterStruck,
            settlement.Id,
            obj: settlement.CivilizationId,
            location: region.Id,
            extra: courtDead.Count == 0 ? null : courtDead.Select(figure => figure.Id).ToArray(),
            data: data);

        // The cause precedes its named casualties in the append-only chronicle.
        foreach (Figure figure in courtDead)
        {
            Houses.Die(world, figure, year, DeathCause.Disaster, Label(kind));
        }
    }

    /// <summary>
    /// Lets a calamity at the seat of government reach the named people the chronicle follows.
    /// </summary>
    /// <remarks>
    /// <para>Losses anywhere else remain population losses. Treating every figure in a realm as
    /// present at every village disaster would fabricate a precision the residence model does not
    /// have; the capital is the one settlement at which a whole court can honestly be placed.</para>
    ///
    /// <para><b>Except for those who genuinely live elsewhere.</b> Since offices, a governor has a
    /// street address — the town they govern — and so is exposed to what happens there while the
    /// rest of the court is not. That is the payoff of a residence finer than a realm, and it is
    /// why a governorship is a real position rather than a line on a figure's page: it is the one
    /// office in this engine that can get its holder killed by geography.</para>
    /// </remarks>
    private static List<Figure> CourtCasualties(
        WorldState world,
        Settlement settlement,
        double severity,
        int year,
        IRng rng)
    {
        Civilization owner = world.Civilizations[settlement.CivilizationId];
        var dead = new List<Figure>();

        bool isSeat = owner.CapitalId == settlement.Id;

        double mortality = DetMath.Clamp01(severity * CourtExposure);
        IRng court = rng.Fork("court-casualties", settlement.Id.ToDiscriminator());

        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive || figure.CivilizationId != owner.Id) continue;
            if (figure.AgeIn(year) < 0) continue;

            // At the seat, everyone the chronicle follows. Elsewhere, only those posted here.
            bool present = figure.ResidenceSettlementId == settlement.Id
                || (isSeat && figure.ResidenceSettlementId.IsNone);

            if (!present) continue;

            IRng fate = court.Fork("figure", figure.Id.ToDiscriminator());
            if (fate.Chance(mortality))
            {
                dead.Add(figure);
            }
        }

        return dead;
    }

    /// <summary>Fraction of a settlement's people one event of this kind carries off.</summary>
    private static double Severity(DisasterKind kind, IRng rng) => kind switch
    {
        DisasterKind.Eruption => rng.NextDouble(0.14, 0.42),
        DisasterKind.Earthquake => rng.NextDouble(0.05, 0.20),
        DisasterKind.Wildfire => rng.NextDouble(0.05, 0.18),
        DisasterKind.Flood => rng.NextDouble(0.04, 0.16),
        DisasterKind.Blizzard => rng.NextDouble(0.03, 0.14),
        _ => rng.NextDouble(0.02, 0.11),
    };

    private static bool Levels(DisasterKind kind) =>
        kind is DisasterKind.Earthquake or DisasterKind.Eruption;

    private static bool Burns(DisasterKind kind) =>
        kind is DisasterKind.Wildfire or DisasterKind.Eruption;

    /// <summary>Reads straight into "X was struck by …".</summary>
    public static string Label(DisasterKind kind) => kind switch
    {
        DisasterKind.Earthquake => "an earthquake",
        DisasterKind.Eruption => "an eruption",
        DisasterKind.Flood => "a great flood",
        DisasterKind.Storm => "a storm off the sea",
        DisasterKind.Wildfire => "wildfire",
        DisasterKind.Blizzard => "a killing winter",
        _ => "calamity",
    };
}

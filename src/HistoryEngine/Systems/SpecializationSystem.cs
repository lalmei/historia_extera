using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Terrain;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Decides what a settlement becomes known for, once it is more than a hamlet.
/// </summary>
/// <remarks>
/// <para>This is where geography gets a second-order effect on history. Terrain already decided
/// <em>where</em> settlements appear; specialization decides <em>what they are</em>, and
/// <see cref="Specializations"/> then feeds that back into how large they grow and which years kill
/// them. A coastal town and an inland one on identical soil now diverge.</para>
///
/// <para><b>Culture is a thumb on the scale, not the decision.</b> A pious realm founds more
/// shrines and a mercantile one more trading towns, but neither can put a fishing village inland.
/// Terrain proposes, culture disposes — which keeps the world legible: if a city is a mining town,
/// there are mountains there.</para>
///
/// <para><b>Established once and kept.</b> A settlement's character does not churn from year to
/// year on a marginal scoring difference, because the chronicle would fill with meaningless
/// reclassifications. It is set when the settlement outgrows a hamlet and left alone.</para>
///
/// <para>Samples no terrain: regional statistics come from the cached grid, and the one per-site
/// value it needs comes from <see cref="TerrainAtlas.SampleCoarse"/>, which interpolates the
/// already-primed lattice for free.</para>
/// </remarks>
public sealed class SpecializationSystem : IYearSystem
{
    public string Name => "specialization";

    public void Tick(WorldState world, int year)
    {
        IRng rng = world.Root.Fork(Name, year);

        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            Culture culture = world.CultureOf(civilization);

            foreach (Settlement settlement in world.ActiveSettlementsOf(civilization))
            {
                if (settlement.Specialization != SettlementSpecialization.None) continue;
                if (settlement.Tier < SettlementTier.Village) continue;

                Region region = world.Regions[settlement.RegionId];
                SettlementSpecialization chosen = Choose(world, culture, settlement, region, rng);

                settlement.Specialization = chosen;
                settlement.SpecializedYear = year;

                world.Chronicle.Record(
                    year,
                    EventKind.SettlementSpecialized,
                    settlement.Id,
                    obj: settlement.CivilizationId,
                    location: region.Id,
                    data: Chronicle.Data(("trade", Specializations.Label(chosen))));
            }
        }
    }

    /// <summary>
    /// Scores every specialization against the site and picks the best.
    /// </summary>
    /// <remarks>
    /// A small random term breaks near-ties, so two identical sites in the same realm do not both
    /// inevitably become farms. It is drawn from a stream forked on the settlement's id, so the
    /// outcome does not depend on how many settlements were scored before it.
    /// </remarks>
    private static SettlementSpecialization Choose(
        WorldState world, Culture culture, Settlement settlement, Region region, IRng rng)
    {
        TerrainSample site = world.Terrain.SampleCoarse(settlement.X, settlement.Z);
        Hydrology hydrology = world.Terrain.Hydrology;

        bool onRiver = hydrology.IsRiver(settlement.X, settlement.Z) || region.HasRiver;
        bool onCoast = hydrology.IsCoast(settlement.X, settlement.Z) || region.IsCoastal;

        IRng jitter = rng.Fork("site", settlement.Id.ToDiscriminator());

        SettlementSpecialization best = SettlementSpecialization.Farming;
        double bestScore = double.NegativeInfinity;

        foreach (SettlementSpecialization candidate in Candidates)
        {
            double score = Score(candidate, culture, region, site, onRiver, onCoast, settlement)
                           + jitter.NextDouble(0.0, 0.18);

            // Strictly greater, so the fixed candidate order breaks exact ties.
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>Fixed order, so ties resolve reproducibly.</summary>
    private static readonly SettlementSpecialization[] Candidates =
    {
        SettlementSpecialization.Farming,
        SettlementSpecialization.Pastoral,
        SettlementSpecialization.Fishing,
        SettlementSpecialization.Mining,
        SettlementSpecialization.Trade,
        SettlementSpecialization.Crafts,
        SettlementSpecialization.Shrine,
    };

    private static double Score(
        SettlementSpecialization candidate,
        Culture culture,
        Region region,
        TerrainSample site,
        bool onRiver,
        bool onCoast,
        Settlement settlement)
    {
        CultureValues values = culture.Values;

        return candidate switch
        {
            // Good soil, and the default for unremarkable inland ground.
            SettlementSpecialization.Farming =>
                0.30 + (region.Fertility * 0.75),

            // Dry open country that will not take a plough.
            SettlementSpecialization.Pastoral =>
                0.20
                + (DetMath.InverseLerp(0.55, 0.12, region.Fertility) * 0.55)
                + (DetMath.InverseLerp(0.45, 0.12, region.Rainfall) * 0.30),

            // Requires water. Without it, impossible rather than merely unlikely.
            SettlementSpecialization.Fishing =>
                onCoast ? 0.55 + (DetMath.InverseLerp(0.0, 0.6, region.Fertility) * 0.15) : -1.0,

            // Requires geology, and rewards the highlands nobody wants to farm.
            SettlementSpecialization.Mining =>
                region.GeologicActivity < 0.35
                    ? -1.0
                    : 0.25
                      + (region.GeologicActivity * 0.65)
                      + (DetMath.InverseLerp(500.0, 1900.0, site.Height) * 0.25),

            // Wants to be on a route, and a mercantile culture makes more of one.
            SettlementSpecialization.Trade =>
                (onRiver || onCoast)
                    ? 0.22 + (values.Mercantile * 0.55) + (onRiver && onCoast ? 0.20 : 0.0)
                    : -1.0,

            // Urban work. Needs a town's worth of people before it means anything.
            SettlementSpecialization.Crafts =>
                settlement.Tier < SettlementTier.Town
                    ? -1.0
                    : 0.30 + (values.Mercantile * 0.25) + (values.Tradition * 0.25),

            // Dramatic ground and a devout people.
            SettlementSpecialization.Shrine =>
                0.05
                + (values.Piety * 0.70)
                + (DetMath.InverseLerp(900.0, 2200.0, site.Height) * 0.30)
                + (region.GeologicActivity * 0.15),

            _ => -1.0,
        };
    }
}

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
/// <para><b>Established once and kept, but not at the first possible moment.</b> A settlement's
/// character does not churn from year to year on a marginal scoring difference, because the
/// chronicle would fill with meaningless reclassifications. It is still set once and left alone —
/// but a village whose ground argues for nothing in particular is left unlabelled and asked again
/// as it grows, rather than being handed the best of a weak field the year it stops being a hamlet.
/// See <see cref="FarmingBar"/>.</para>
///
/// <para>Samples no terrain: regional statistics come from the cached grid, and the one per-site
/// value it needs comes from <see cref="TerrainAtlas.SampleCoarse"/>, which interpolates the
/// already-primed lattice for free.</para>
/// </remarks>
public sealed class SpecializationSystem : ISystem
{
    /// <summary>
    /// What being founded to work a deposit is worth when a village is asked what it does.
    /// </summary>
    /// <remarks>
    /// <para><b>Without it the errand is invisible here, and that is measured rather than
    /// assumed.</b> Across eight seeds, of the camps a realm sent out specifically for ore and
    /// which lived to be asked, <b>5.8%</b> were recorded as mining towns and <b>81%</b> as farming
    /// villages — because farming opens at 0.30 plus three quarters of the region's fertility and
    /// mining has to climb there from geology alone. The scorer could not see the difference
    /// between a mining camp and a farm, so the map said one thing and the chronicle said
    /// another.</para>
    ///
    /// <para><b>A prior, not a lock, and the size is what makes it one.</b> At 0.35 the camps come
    /// out 72% mining, 24% farming and 4% market towns; at 0.55 they come out 96% mining, which is
    /// the character dictating the trade under another name. The two facts are deliberately kept
    /// apart — <see cref="Entities.SiteCharacter"/> is why they stood there and specialization is
    /// what the place became known for — and a seam that ran out while the valley turned out to
    /// grow wheat is a history worth being able to have.</para>
    /// </remarks>
    private const double MineCampPrior = 0.35;

    /// <summary>
    /// What being founded to hold a frontier is worth when the settlement is asked what it does.
    /// </summary>
    /// <remarks>
    /// <para>The twin of <see cref="MineCampPrior"/>, and deliberately the same size. A frontier
    /// post carries <see cref="SiteCharacter.Strategic"/> because a realm sent a party out to stand
    /// on that ground, and without the errand in the score the garrison is invisible for the same
    /// reason the mine was: the 51 strategic sites that reached the question across the five-seed
    /// panel sat on a median regional fertility of 0.82, where farming opens at 0.92 and a martial
    /// curve reading culture alone reaches 0.67.</para>
    ///
    /// <para><b>Ground alone does not do it, and should not.</b>
    /// <see cref="SiteCharacter.Pass"/> is in the gate because a pass is defensible ground, but the
    /// ten passes in the panel sit on a median fertility of 0.86 and stay farming villages, which is
    /// the right answer: a rich valley with the road through it is a farm that happens to be astride
    /// a pass. Military is what a realm posted there, not what the contour lines say.</para>
    /// </remarks>
    private const double FrontierPostPrior = 0.35;

    /// <summary>
    /// The score a farm must reach before a village is called a farming village.
    /// </summary>
    /// <remarks>
    /// <para><b>What it is for.</b> The question used to be asked once — the first year a
    /// settlement was a <see cref="SettlementTier.Village"/> — and answered unconditionally,
    /// whatever won the argmax and however weakly it won. Across the five-seed panel at a thousand
    /// years that put 786 of 787 decisions at village tier and exactly one at town, so
    /// <see cref="SettlementSpecialization.Crafts"/>, which needs a town's worth of people before it
    /// means anything, was chosen <b>zero</b> times in ten worlds. The fault was never the craft
    /// gate. It was asking at the earliest possible moment regardless of whether the answer could be
    /// right, and then never asking again.</para>
    ///
    /// <para><b>Why farming alone.</b> Every other candidate either passes a hard gate — a coast, a
    /// deposit, a route, a position a realm chose to hold — or reads a specific terrain signature
    /// that dry open country has and ordinary country does not. Farming is the declared default for
    /// unremarkable inland ground, so it is the one winner that can mean the field said nothing.
    /// Holding <em>any</em> weak winner was measured and is worse: at a bar of 0.75 applied to all
    /// seven trades, herding fell from 12 settlements to 2 and market towns from 32 to 22 across the
    /// panel. That swallows the small trades to fix a different bug.</para>
    ///
    /// <para><b>Why 0.85.</b> Farming opens at 0.30 and reads three quarters of regional fertility,
    /// so this is fertility 0.73 — comfortably under the 0.84 median of the ground settlements
    /// actually stand on. It leaves 23 villages in 890 unlabelled at a thousand years and produces
    /// 25 craft towns; at 0.75 it is 6 and 5, at 0.90 it is 39 and 34. The gated trades barely move
    /// at any of them, which is the point.</para>
    ///
    /// <para><b>A delay, not a veto.</b> The bar applies below <see cref="SettlementTier.Town"/>
    /// only. A place that grows into a town is decided outright on whatever the field offers,
    /// because four thousand people are known for something even if the ground never said what — and
    /// that is the moment craftwork becomes an available answer. No town or city is left unlabelled
    /// in any panel run.</para>
    /// </remarks>
    private const double FarmingBar = 0.85;

    /// <summary>
    /// What a town's population is worth to craftwork, and what a city's is worth instead.
    /// </summary>
    /// <remarks>
    /// Craftwork is the one trade whose qualification is people rather than ground, so it is the one
    /// trade whose score reads the tier. Without these terms it tops out at 0.80 against a median
    /// winning score of 0.94 and would stay unreachable even once it is asked: a village held back
    /// by <see cref="FarmingBar"/> carries a farm worth up to 0.85, and 0.30 plus a quarter each of
    /// mercantile and traditional feeling does not reach it. The town term is what lets a place that
    /// grew be something other than the mediocre farm it was held back from.
    /// </remarks>
    private const double TownCraftBonus = 0.15;

    /// <inheritdoc cref="TownCraftBonus"/>
    private const double CityCraftBonus = 0.30;

    /// <summary>The random stream this system draws from. Named once, so <see cref="Choose"/>
    /// can fork it without an instance.</summary>
    private const string Stream = "specialization";

    public string Name => Stream;

    public Cadence Cadence => Cadence.Annual;

    public void Tick(WorldState world, Stamp now)
    {
        int year = now.Year;

        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            Culture culture = world.CultureOf(civilization);

            foreach (Settlement settlement in world.ActiveSettlementsOf(civilization))
            {
                if (settlement.Specialization != SettlementSpecialization.None) continue;
                if (settlement.Tier < SettlementTier.Village) continue;

                Region region = world.Regions[settlement.RegionId];
                SettlementSpecialization chosen = Choose(world, culture, settlement, region);

                // Nothing here yet argues for anything. Ask again next year, when the place may be
                // larger than it is now.
                if (chosen == SettlementSpecialization.None) continue;

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
    /// Scores every specialization against the site and picks the best, or nothing yet.
    /// </summary>
    /// <remarks>
    /// <para>A small random term breaks near-ties, so two identical sites in the same realm do not
    /// both inevitably become farms. It is drawn from a stream forked on the settlement's id, so the
    /// outcome does not depend on how many settlements were scored before it.</para>
    ///
    /// <para><b>The jitter is fixed for the life of the settlement</b>, not redrawn each year. It
    /// used to hang off the annual stream, which did not matter while the question was asked once;
    /// now that a village can be asked seventy times on its way to being a town, a per-year draw
    /// would turn <see cref="FarmingBar"/> into a lottery that any village passes if it waits long
    /// enough. The bar reads the site's own score for that reason, before the tie-break is added.
    /// </para>
    /// </remarks>
    private static SettlementSpecialization Choose(
        WorldState world, Culture culture, Settlement settlement, Region region)
    {
        TerrainSample site = world.Terrain.SampleCoarse(settlement.X, settlement.Z);
        Hydrology hydrology = world.Terrain.Hydrology;

        bool onRiver = hydrology.IsRiver(settlement.X, settlement.Z) || region.HasRiver;
        bool onCoast = hydrology.IsCoast(settlement.X, settlement.Z) || region.IsCoastal;

        // How good the water is, not merely that there is some. A fishing village wants water it
        // can put a boat back into, and the trades that grow at a river mouth or a meeting of two
        // rivers are not the trades that grow on an ordinary reach.
        double shelter = hydrology.ShelterAt(settlement.X, settlement.Z);
        bool onJunction = hydrology.IsConfluence(settlement.X, settlement.Z)
                          || hydrology.IsEstuary(settlement.X, settlement.Z);

        IRng jitter = world.Root.Fork(Stream, settlement.Id.ToDiscriminator());

        SettlementSpecialization best = SettlementSpecialization.Farming;
        double bestScore = double.NegativeInfinity;
        double bestGround = double.NegativeInfinity;

        foreach (SettlementSpecialization candidate in Candidates)
        {
            double ground = Score(candidate, culture, region, site, onRiver, onCoast, shelter, onJunction, settlement);
            double score = ground + jitter.NextDouble(0.0, 0.18);

            // Strictly greater, so the fixed candidate order breaks exact ties.
            if (score > bestScore)
            {
                bestScore = score;
                bestGround = ground;
                best = candidate;
            }
        }

        // A town is known for something whatever the field offers. A village is not, and saying so
        // is what lets a place become what it grew into rather than what it started as.
        if (settlement.Tier < SettlementTier.Town
            && best == SettlementSpecialization.Farming
            && bestGround < FarmingBar)
        {
            return SettlementSpecialization.None;
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
        SettlementSpecialization.Military,
    };

    private static double Score(
        SettlementSpecialization candidate,
        Culture culture,
        Region region,
        TerrainSample site,
        bool onRiver,
        bool onCoast,
        double shelter,
        bool onJunction,
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

            // Requires water. Without it, impossible rather than merely unlikely. Sheltered water
            // makes the difference between a fleet and a few boats hauled up a beach.
            SettlementSpecialization.Fishing =>
                onCoast
                    ? 0.45
                      + (shelter * 0.25)
                      + (DetMath.InverseLerp(0.0, 0.6, region.Fertility) * 0.15)
                    : -1.0,

            // Requires geology, and rewards the highlands nobody wants to farm. A camp that was
            // sent out for the deposit is already standing on it, and comes to the question with
            // that behind it — a prior rather than a lock, because a mine camp that turns out to
            // sit on a road is a market town with a mine, which is a better history than one
            // decided at year zero. The gate stays where it is: geology the realm went looking for
            // has to be geology specialization would recognise.
            SettlementSpecialization.Mining =>
                region.GeologicActivity < Specializations.OreThreshold
                    ? -1.0
                    : 0.25
                      + (region.GeologicActivity * 0.65)
                      + (DetMath.InverseLerp(500.0, 1900.0, site.Height) * 0.25)
                      + (settlement.Site == SiteCharacter.Mine ? MineCampPrior : 0.0),

            // Wants to be on a route, and a mercantile culture makes more of one. A river mouth or
            // a meeting of two rivers is where a route becomes a junction rather than a passing
            // place, which is what actually makes a market out of a town.
            SettlementSpecialization.Trade =>
                (onRiver || onCoast)
                    ? 0.22
                      + (values.Mercantile * 0.55)
                      + (onRiver && onCoast ? 0.20 : 0.0)
                      + (onJunction ? 0.18 : 0.0)
                      + (shelter * 0.12)
                    : -1.0,

            // Urban work. Needs a town's worth of people before it means anything, and reads that
            // population as the qualification rather than reading the ground — the only trade here
            // that does, because it is the only one whose raw material is other people's trades.
            SettlementSpecialization.Crafts =>
                settlement.Tier < SettlementTier.Town
                    ? -1.0
                    : 0.30
                      + (values.Mercantile * 0.25)
                      + (values.Tradition * 0.25)
                      + (settlement.Tier >= SettlementTier.City ? CityCraftBonus : TownCraftBonus),

            // Dramatic ground and a devout people.
            SettlementSpecialization.Shrine =>
                0.05
                + (values.Piety * 0.70)
                + (DetMath.InverseLerp(900.0, 2200.0, site.Height) * 0.30)
                + (region.GeologicActivity * 0.15),

            // Ground a realm decided to hold. Requires a position worth holding — the pass, the
            // defensible height, the post planted on a frontier — and then mostly turns on whether
            // the realm was the sort to garrison it. A post sent out to stand on the border comes to
            // the question with that behind it, exactly as a mine camp does.
            SettlementSpecialization.Military =>
                settlement.Site is SiteCharacter.Strategic or SiteCharacter.Pass or SiteCharacter.Fortress
                    ? 0.28
                      + (values.Aggression * 0.40)
                      + (settlement.Site == SiteCharacter.Strategic ? FrontierPostPrior : 0.0)
                    : -1.0,

            _ => -1.0,
        };
    }
}

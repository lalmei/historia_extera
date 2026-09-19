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
    /// villages — because farming opened at 0.30 plus three quarters of the region's fertility (the
    /// floor issue #275 later cut to 0.10; see <see cref="FarmingFloor"/>) and mining has to climb
    /// there from geology alone. The scorer could not see the difference
    /// between a mining camp and a farm, so the map said one thing and the chronicle said
    /// another.</para>
    ///
    /// <para><b>A prior, not a lock, and the size is what makes it one.</b> At 0.35 the camps come
    /// out 72% mining, 24% farming and 4% market towns; at 0.55 they come out 96% mining, which is
    /// the character dictating the trade under another name. The two facts are deliberately kept
    /// apart — <see cref="Entities.SiteCharacter"/> is why they stood there and specialization is
    /// what the place became known for — and a seam that ran out while the valley turned out to
    /// grow wheat is a history worth being able to have.</para>
    ///
    /// <para><b>Cut from 0.35 to 0.25 alongside <see cref="FarmingFloor"/>'s rebalance.</b> This
    /// prior was sized against a Farming floor of 0.30; cutting that floor to 0.10 made every
    /// gated trade's fixed terms, this one included, pull harder relative to Farming everywhere,
    /// not only on the ore-camp ground the prior was meant for. The eight-seed colonisation panel
    /// (<see cref="Tests.ColonisationTests.MineCampsAreUsuallyButNotAlwaysKnownForTheirOre"/>)
    /// measured the consequence directly: at 0.35, camps that lived to be asked came out 93.0%
    /// mining — past the 92% figure this docstring already flags as "the character dictating the
    /// trade under another name" — up from 72% before the floor cut, entirely as a side effect of
    /// Farming's ground competing less everywhere, not because the ore camps themselves changed.
    /// At 0.25 the same panel measured 78.7% mining (37 of 47 camps), back inside the historical
    /// 72–92% band this prior was calibrated to hold, so it is still a prior a good farm can
    /// occasionally overcome rather than geology repeated under another name.</para>
    /// </remarks>
    private const double MineCampPrior = 0.25;

    /// <summary>
    /// What being founded to hold a frontier is worth when the settlement is asked what it does.
    /// </summary>
    /// <remarks>
    /// <para>Originally the twin of <see cref="MineCampPrior"/>, and deliberately the same size,
    /// though the two have since diverged: <see cref="MineCampPrior"/> was cut to 0.25 to bring
    /// mine camps' mining share back inside its historical band once
    /// <see cref="FarmingFloor"/> dropped (see there for the measured numbers), while no equivalent
    /// regression was measured for frontier posts, so this constant was left at its original size
    /// rather than moved on the strength of an assumed symmetry alone. A frontier post carries
    /// <see cref="SiteCharacter.Strategic"/> because a realm sent a party out to stand on that
    /// ground, and without the errand in the score the garrison is invisible for the same reason
    /// the mine was: the 51 strategic sites that reached the question across the five-seed panel
    /// sat on a median regional fertility of 0.82, where farming opened at 0.92 under the floor
    /// issue #275 later cut (see <see cref="FarmingFloor"/>; 0.72 under the current one) and a
    /// martial curve reading culture alone reaches 0.67.</para>
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
    /// <para><b>Why 0.695, recalibrated twice.</b> This bar is compared against Farming's own
    /// ground score, so it encodes a fertility threshold, not an absolute one — 0.85 against the
    /// old floor (0.30 + fertility × 0.75) meant fertility 0.733. When the floor was first cut to
    /// 0.10, the bar was moved to 0.65 to carry that same 0.733 threshold forward unchanged
    /// (0.10 + 0.733 × 0.75 = 0.65). That preserved the threshold faithfully, but the panel then
    /// caught a second-order effect the threshold-preservation argument does not see: the same
    /// rebalance that cut the floor also raised Fishing's and Mining's effective reach, so far
    /// more marginal ground is now won outright by a gated trade at village tier, before it ever
    /// becomes a weak Farming argmax this bar could catch. The village-tier "held back" pool that
    /// feeds craft towns collapsed from 39 (old floor) to essentially none (2–3 across the panel
    /// at 0.65) as a direct result — starving <see cref="SettlementSpecialization.Crafts"/> of
    /// the only supply route it has (see <see cref="TownCraftBonus"/>). 0.695 raises the encoded
    /// fertility threshold slightly further, to about 0.793, holding back a few more
    /// borderline-fertile villages that a strict 0.733 reading would have released to Farming
    /// immediately.</para>
    ///
    /// <para><b>This deliberately re-couples the two concerns issue #275 tried to keep apart</b> —
    /// the fertility threshold and the craft supply — because measurement showed the craft supply
    /// could not be restored without it. Raising <see cref="TownCraftBonus"/> and
    /// <see cref="CityCraftBonus"/> alone (holding this bar at 0.65) took Crafts from 11 to only 13
    /// settlements at a thousand years across the panel — the held-back pool reaching Town tier
    /// was simply too small for a bigger bonus to matter. Widening the bar alone (holding the
    /// bonuses at their old size) got closer — 36 Crafts settlements at a thousand years, 8 at
    /// three hundred — but three hundred years still fell short of the 10-settlement target that
    /// stood before the rebalance. Only moving both did: at 0.695 together with the bonuses below,
    /// the five-seed panel measured 10 Crafts settlements at three hundred years and 34 at a
    /// thousand (pre-rebalance floor: 10 and 28), while Farming's founding share held at 45.2%
    /// (300y) and 50.6% (1000y) — inside the 45–55% band issue #275 set — and every seed kept at
    /// least one Fishing settlement at both horizons. Sweeping the bar past 0.75 overshoots badly
    /// (Farming founding share fell to 38.1% at a thousand years, Crafts rose to 109) and is not
    /// safe; 0.695 is the smallest widening the panel found that meets both the craft-town floor
    /// and the distribution band at once, not a value chosen for its own sake.</para>
    ///
    /// <para><b>A delay, not a veto.</b> The bar applies below <see cref="SettlementTier.Town"/>
    /// only. A place that grows into a town is decided outright on whatever the field offers,
    /// because four thousand people are known for something even if the ground never said what — and
    /// that is the moment craftwork becomes an available answer. No town or city is left unlabelled
    /// in any panel run.</para>
    /// </remarks>
    private const double FarmingBar = 0.695;

    /// <summary>
    /// What ordinary, unremarkable ground is worth to a farm before fertility is even read.
    /// </summary>
    /// <remarks>
    /// <para><b>The bias this closes.</b> At the old floor of 0.30 the worst ground in the world
    /// still scored 0.30 — above <see cref="SettlementSpecialization.Pastoral"/>'s own floor
    /// (0.20) and only 0.15 below <see cref="SettlementSpecialization.Fishing"/>'s coastal
    /// opening (0.45), before that trade's own terms add anything. A five-seed, thousand-year
    /// panel (issue #275) measured the consequence directly: Farming was 74.5% of every
    /// settlement's founding trade, decided the year each place was founded, before a single one
    /// had a chance to grow or die. Two of the five seeds — real, growing coastline included —
    /// produced not one fishing village in a thousand years, because the unconditional floor
    /// alone was already enough for Farming to beat Fishing's coastal opening on middling
    /// ground, and the margin analysis in that panel showed the gap was not jitter-sized: a
    /// median of 0.223 against a jitter width of 0.18, several times too large for widening the
    /// tie-break to touch.</para>
    ///
    /// <para><b>Why 0.10, not zero.</b> A floor of exactly zero would make the worst possible
    /// ground (fertility 0) score below every gated trade's floor even when none of those gates
    /// are open, which is wrong in the other direction: ordinary inland ground with no coast, no
    /// ore and no route still has to be a farm, because nothing else can grow there. 0.10 keeps
    /// Farming's absolute floor below Pastoral's (<see cref="PastoralFloor"/>, 0.20) and well
    /// below Fishing's coastal opening (0.45), so those trades can win the ground they are
    /// actually suited for, while leaving Farming a slope steep enough
    /// (<see cref="FarmingSlope"/>) that good soil still wins convincingly — this is a floor cut,
    /// not a farming nerf.</para>
    /// </remarks>
    private const double FarmingFloor = 0.10;

    /// <summary>
    /// How much of Farming's score regional fertility buys, on top of <see cref="FarmingFloor"/>.
    /// </summary>
    /// <remarks>
    /// Left at 0.75, unchanged from before the rebalance. The floor was the bias — the worst
    /// ground scoring as high as it did regardless of fertility — not the slope: fertile ground
    /// (median settled fertility 0.817) already produced a farm that comfortably out-scored every
    /// ungated rival, and the panel's margin data showed no case where a good farm was wrongly
    /// losing. Widening the slope on top of lowering the floor would only reopen the same
    /// unconditional-floor problem at the top end; narrowing it would make good farmland
    /// competitive with trades it should not be, which the panel gave no reason to do.
    /// </remarks>
    private const double FarmingSlope = 0.75;

    /// <summary>
    /// What a fishing village is worth the moment it is founded on a coast, before shelter or
    /// hinterland fertility add anything.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this moved, when the diagnosis was about Farming.</b> Cutting
    /// <see cref="FarmingFloor"/> and flipping the fertility term (see
    /// <see cref="FishingFertilityCeiling"/>) fixed Fishing's competition with Farming, but issue
    /// #275's target — every seed with settled coastline produces at least one fishing settlement
    /// — was still failing after both of those changes alone: two of the five panel seeds have
    /// dominant cultures with measured Mercantile around 0.85–0.97, and <see
    /// cref="SettlementSpecialization.Trade"/> shares Fishing's exact coastal gate with a ceiling
    /// (0.22 + Mercantile × 0.55, plus route bonuses) that a Fishing floor of 0.45 could not
    /// reach even at maximum shelter. That is a real, separate imbalance the diagnosis flagged
    /// (finding 3, "fixing farming alone will hand most of the poor coast to Trade, not to
    /// Fishing") and measuring it directly, rather than assuming the Farming fix would cover it,
    /// is what caught it.</para>
    ///
    /// <para><b>Why 0.60, not higher.</b> Raised from 0.45 in step with
    /// <see cref="FishingShelterWeight"/> so the two absorb the same-sized correction rather than
    /// one doing all of it. 0.60 keeps Fishing's floor below <see cref="FarmingSlope"/>'s
    /// contribution on any fertile ground and below Trade's floor once Mercantile clears roughly
    /// 0.7 — it does not attempt to make Fishing beat a highly mercantile culture's best trade
    /// ports outright, only to give it a foothold. Measured against the five-seed panel, this
    /// value (with the shelter weight below) took Fishing from 3 settlements founded in a
    /// thousand years, zero in three of five seeds, to 56 founded with at least one in every
    /// seed at both horizons — the acceptance criterion issue #275 set.</para>
    /// </remarks>
    private const double FishingFloor = 0.60;

    /// <summary>
    /// How much of Fishing's score sheltered water buys, on top of <see cref="FishingFloor"/>.
    /// </summary>
    /// <remarks>
    /// Raised from 0.25 to 0.30 together with <see cref="FishingFloor"/> — see there for why
    /// Fishing needed more than the Farming and fertility-direction fixes alone. Read together
    /// they move Fishing's ceiling from 0.85 to 1.05, matching Farming's own ceiling
    /// (<see cref="FarmingFloor"/> + <see cref="FarmingSlope"/>) rather than leaving Fishing
    /// permanently capped below every trade with a culture-scaled term.
    /// </remarks>
    private const double FishingShelterWeight = 0.30;

    /// <summary>
    /// The fertility below which a coastal hinterland is poor enough that Fishing's fertility
    /// term pays out in full, tapering to nothing at or above it.
    /// </summary>
    /// <remarks>
    /// <para><b>The bug this fixes.</b> The term used to be
    /// <c>InverseLerp(0.0, 0.6, region.Fertility) * 0.15</c> — it <em>increased</em> with
    /// fertility, so it paid Fishing the most exactly where the hinterland could already feed the
    /// town from Farming alone, and paid it the least on the poor coast where fishing boats are
    /// the only sensible answer. Issue #275 measured this directly: on coastal ground, Farming's
    /// ground score ran [0.300, 1.008] (median 0.869) while Fishing's ran [0.494, 0.789] (median
    /// 0.659) on the very same sites — Fishing at its best could not reach Farming at its
    /// median, and the term responsible for that was rewarding Farming's own best ground a
    /// second time.</para>
    ///
    /// <para><b>Why the same threshold, only flipped.</b> 0.6 is kept as the ceiling so the
    /// term's shape and weight (0.15) are unchanged — this is a direction fix, not a re-tune of
    /// the bonus's size, and the panel gave no evidence the size itself was wrong. Flipped, a
    /// coastal hinterland at fertility 0 (cannot farm at all) gets the full 0.15; at 0.6 or above
    /// (as good as the median settled farmland) it gets none, which reads the hinterland's
    /// inability to feed the town the way <see cref="SettlementSpecialization.Pastoral"/>'s own
    /// terms already read dry ground — as the condition that makes the alternative trade the
    /// right answer, not as a bonus for having both.</para>
    /// </remarks>
    private const double FishingFertilityCeiling = 0.6;

    /// <summary>
    /// What dry open country is worth to a herder before either dryness term pays out anything.
    /// </summary>
    /// <remarks>
    /// Unchanged at 0.20. Issue #275's own acceptance criterion — every seed with settled dry
    /// country produces at least one Pastoral settlement — was still failing after the Farming
    /// and Fishing fixes above, but the floor was never the part at fault: at the dry boundary
    /// itself (fertility 0.55, rainfall 0.45) the ramps below correctly pay out nothing, so the
    /// floor alone is what a barely-dry site scores, and 0.20 there already sits below Farming's
    /// score on the same ground (about 0.51) — which is the right answer at the boundary. The
    /// fault was the ramps below saturating on ground almost nothing settles; see
    /// <see cref="PastoralFertilityRamp"/> for the measured gap and the fix.
    /// </remarks>
    private const double PastoralFloor = 0.20;

    /// <summary>
    /// The fertility below which dry ground pays Pastoral's fertility term in full, tapering to
    /// nothing at <see cref="Systems.SpecializationTests.EveryCoastGetsAFisherAndEveryDryCountryGetsAHerder"/>'s
    /// own dry threshold of 0.55.
    /// </summary>
    /// <remarks>
    /// <para><b>The bug this fixes.</b> A five-seed, thousand-year panel found zero Pastoral
    /// settlements among dry, Village-tier-or-larger ground in seed 2 — six candidate sites, four
    /// coastal (lost to Fishing, by margins as small as 0.05 and once by more than Fishing itself
    /// scored, purely to the tie-break jitter) and two inland (lost to a pious culture's Shrine).
    /// Pastoral's own ground score at the two inland sites was 0.574 and 0.670 — already ahead of
    /// Farming there (0.562, 0.508) — so the fix did not need to make Pastoral beat Farming on
    /// this ground, only to widen its margin over the competing trades enough to stop losing
    /// close, jitter-sized races it should structurally be winning more often.</para>
    ///
    /// <para><b>Moved from 0.12 to 0.20 — the upper bound (0.55) stays put on purpose.</b> The
    /// upper bound is the definition of "dry" the acceptance test itself reads as
    /// <c>DryFertility</c>, so moving it would change what the test calls dry country and its
    /// docstring along with it; issue #275 does not ask for that. The lower bound is only where
    /// the term saturates, which is free to move. At 0.12, full credit was reserved for fertility
    /// this world rarely generates; at fertility 0.4 (mid-range dry ground) the old ramp paid out
    /// <c>(0.55-0.4)/(0.55-0.12)=0.35</c> of its 0.55 weight, for a total Pastoral score of about
    /// 0.44 against Farming's 0.40 there — a four-point margin too thin to survive the 0.18-wide
    /// tie-break jitter reliably. At 0.20, the same site draws <c>(0.55-0.4)/(0.55-0.20)=0.43</c>
    /// of the weight, for a total of about 0.46 — a modest widening, deliberately kept small: it
    /// was measured against the five-seed panel to leave Farming's founding share at 45.50%/300y
    /// and 50.27%/1000y (was 45.50%/50.20%) and Crafts at 10 founded/300y and 36/1000y (was
    /// 10/37, both still at or above the 10/30 floor issue #275 already set), while lifting
    /// dry-country Pastoral counts in the panel from 2/1/0 to 5/5/1 (seeds 11, 42, 2 at a thousand
    /// years) and turning seed 2's zero into one. A wider move (tried and measured, not merely
    /// argued: floor 0.25 with both ramps at 0.30) fixed the same seed but dropped 300-year
    /// Farming share to 44.89% — a tenth of a point under this file's own 45% floor — purely by
    /// pulling previously-unspecialized ("None") hamlets into Pastoral rather than by taking
    /// anything from Farming itself (Farming's absolute count did not move); this smaller step
    /// avoids that side effect entirely while still closing seed 2's gap.</para>
    /// </remarks>
    private const double PastoralFertilityRamp = 0.20;

    /// <summary>
    /// The rainfall below which dry ground pays Pastoral's rainfall term in full, tapering to
    /// nothing at the same 0.45 the acceptance test reads as dry.
    /// </summary>
    /// <remarks>
    /// Moved from 0.12 to 0.20 for the same reason and by the same reasoning as
    /// <see cref="PastoralFertilityRamp"/> — the upper bound (0.45) is <c>DryRainfall</c> in the
    /// acceptance test and stays fixed; only the saturation point moves, by the same fraction of
    /// its range as the fertility ramp, so neither term dominates the other any more than it did
    /// before. Moved in step with the fertility ramp rather than measured in isolation, since the
    /// two terms are added and only their sum has ever been checked against a target.
    /// </remarks>
    private const double PastoralRainfallRamp = 0.20;

    /// <summary>
    /// What a town's population is worth to craftwork, and what a city's is worth instead.
    /// </summary>
    /// <remarks>
    /// <para>Craftwork is the one trade whose qualification is people rather than ground, so it is
    /// the one trade whose score reads the tier. Without these terms it tops out at 0.80 against a
    /// median winning score of 0.94 and would stay unreachable even once it is asked: a village
    /// held back by <see cref="FarmingBar"/> carries a farm worth up to the bar itself, and 0.30
    /// plus a quarter each of mercantile and traditional feeling does not reach it. The town term
    /// is what lets a place that grew be something other than the mediocre farm it was held back
    /// from.</para>
    ///
    /// <para><b>Raised from 0.15/0.30, alongside <see cref="FarmingBar"/>.</b> Issue #275's
    /// rebalance cut craft towns from 28 to 9 at a thousand years by routing far more marginal
    /// ground to Fishing, Mining and Trade outright, which starved the held-back-village pool this
    /// bonus needs to have anything to convert. Measured in isolation — this bonus alone, bar left
    /// at 0.65 — raising it from 0.15 to 0.30 (and 0.30 to 0.45) moved Crafts from 11 to only 13
    /// settlements at a thousand years across the five-seed panel: the pool reaching Town tier was
    /// too small for a bigger bonus to matter much. The bonus only pays off once
    /// <see cref="FarmingBar"/> is also widened to rebuild that pool — see there for the combined
    /// result (10 Crafts settlements at three hundred years, 34 at a thousand, both at or above
    /// the pre-rebalance floor) and the honest accounting of why one constant alone was not
    /// enough.</para>
    /// </remarks>
    private const double TownCraftBonus = 0.30;

    /// <inheritdoc cref="TownCraftBonus"/>
    private const double CityCraftBonus = 0.45;

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
            // Good soil, and the default for unremarkable inland ground. The floor used to be
            // 0.30 regardless of fertility, which meant the worst ground in the world still beat
            // Pastoral's own floor (0.20) and sat only 0.15 below Fishing's coastal opening — so
            // farming won marginal ground not because it was a good farm but because every
            // candidate for that ground had to climb over a bar farming started past. See
            // FarmingFloor for the size argument.
            SettlementSpecialization.Farming =>
                FarmingFloor + (region.Fertility * FarmingSlope),

            // Dry open country that will not take a plough.
            SettlementSpecialization.Pastoral =>
                PastoralFloor
                + (DetMath.InverseLerp(0.55, PastoralFertilityRamp, region.Fertility) * 0.55)
                + (DetMath.InverseLerp(0.45, PastoralRainfallRamp, region.Rainfall) * 0.30),

            // Requires water. Without it, impossible rather than merely unlikely. Sheltered water
            // makes the difference between a fleet and a few boats hauled up a beach. The
            // fertility term rewards poor hinterland rather than rich hinterland: a coast backed
            // by good farmland does not need fishermen and Farming already wins that ground
            // outright, but a coast backed by land too poor to feed the town is exactly where a
            // fishing village is the sensible answer, and the old term paid out backwards — most
            // where Farming already could not lose, least where Fishing was the only sensible
            // trade. See FishingFertilityCeiling for the size argument.
            SettlementSpecialization.Fishing =>
                onCoast
                    ? FishingFloor
                      + (shelter * FishingShelterWeight)
                      + (DetMath.InverseLerp(FishingFertilityCeiling, 0.0, region.Fertility) * 0.15)
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

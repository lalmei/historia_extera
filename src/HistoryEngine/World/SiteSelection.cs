using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Terrain;

namespace HistoryEngine.World;

/// <summary>Where a settlement stands, and what it was put there for.</summary>
public readonly record struct SiteChoice(Point2 At, SiteCharacter Character);

/// <summary>
/// Chooses where inside a region a settlement should stand.
/// </summary>
/// <remarks>
/// <para>The one place in the simulation that spends real terrain samples, and the reason it is
/// centralised: every caller goes through <see cref="TerrainAtlas.RefinedPoints"/> at a stride
/// it declares up front, so the cost of a siting decision is always
/// <c>sitesPerAxis²</c> samples and always visible at the call site. Sites already refined by
/// an earlier decision are served from cache.</para>
///
/// <para><b>This ranks sites within a region; it does not choose the region.</b> That is
/// <see cref="Region.Habitability"/>'s job, and the split is what lets the terms below be as
/// decisive as they are. Soil still settles which country a realm wants — but inside a 128-unit
/// patch soil is very nearly constant, so what should settle the spot is water, buildable ground
/// and what the place commands.</para>
///
/// <para><b>Two resolutions, each asked what it can answer.</b> Landscape questions — how far to
/// fresh water, is that harbour sheltered, is this a pass — come from
/// <see cref="TerrainAtlas.Hydrology"/> and <see cref="TerrainAtlas.Landform"/>, which are derived
/// once on a grid the world already paid for. Questions about the ground itself — how steep is it,
/// does it stand above what surrounds it — are answered from the refinement this decision performs
/// anyway, four times finer than that grid. Neither costs a sample beyond the
/// <c>sitesPerAxis²</c> already budgeted.</para>
/// </remarks>
public static class SiteSelection
{
    /// <summary>Distance within which water counts as being at a settlement's door.</summary>
    /// <remarks>
    /// Only used to decide what a site is <em>called</em>. How much water is worth is
    /// <see cref="Hydrology.RiverAccess"/>, which region habitability reads too.
    /// </remarks>
    private const double WaterAtHand = 64.0;

    /// <summary>Shelter at which a shore is remembered as a harbour rather than as a coast.</summary>
    private const double ShelteredEnough = 0.5;

    /// <summary>Grades between which ground stops being worth building on.</summary>
    /// <remarks>
    /// <para>Measured rather than assumed, twice over. Before this term existed the engine put
    /// 19.6% of settlements on ground steeper than 1-in-2 and the worst on 2:1, because nothing
    /// ever asked. The first version then asked, and barely helped — because the range was set
    /// from what sounded steep rather than from what the candidates are. Across two seeds a
    /// candidate's grade runs a median of 0.49 and a p90 of 1.9, so a penalty saturating at 0.8
    /// handed the same maximum to a third of every field it was ranking and stopped
    /// discriminating exactly where it mattered.</para>
    ///
    /// <para><b>The response is quadratic, and that turned out to matter more than the range.</b>
    /// A linear penalty has to choose between ignoring cliffs and punishing hillsides, because it
    /// treats the step from 0.2 to 0.5 exactly like the step from 1.5 to 1.8. Both linear versions
    /// tried moved the median site grade by hundredths and left the worst site in eight worlds on a
    /// grade of 2.8. Squaring it separates the two questions: ordinary slope costs almost nothing,
    /// and genuinely unbuildable ground costs more than any other term can return. Which is the
    /// actual claim — a hill town is a real thing, a town on a cliff is not.</para>
    /// </remarks>
    private const double BuildableGrade = 0.20;

    private const double UnbuildableGrade = 1.00;

    // What each term can contribute. Soil is the largest single weight, but the site terms
    // together outweigh it — deliberately, because within one region soil barely varies and
    // everything else does. See the class remarks.
    private const double SoilWeight = 1.00;
    private const double FreshWaterWeight = 0.30;
    private const double ConfluenceBonus = 0.14;
    private const double HarbourWeight = 0.28;
    private const double PassBonus = 0.12;
    private const double SlopePenalty = 0.90;
    private const double ThinAirPenalty = 0.30;

    // What changes when the party was sent for the deposit rather than for the soil. Four weights
    // move and nothing else does: the water terms are untouched because miners drink, and the
    // slope penalty stays a penalty because a town on a cliff is still a bad town — see
    // FoundingNeed.Ore below.
    private const double OreSoilWeight = 0.35;
    private const double OreWeight = 0.85;
    private const double OreSlopePenalty = 0.55;
    private const double OreThinAirPenalty = 0.10;

    // There is no defensibility term, and that is a finding rather than an omission. See
    // "Defensibility was tried four ways and cut" below.

    /// <summary>
    /// The best position for a settlement within <paramref name="region"/>, and what it was chosen for.
    /// </summary>
    /// <param name="sitesPerAxis">
    /// Candidates per axis. Costs this squared in terrain samples on first evaluation of a
    /// region, and nothing thereafter.
    /// </param>
    /// <param name="need">
    /// What the party was sent for. <see cref="FoundingNeed.Land"/> is the ordinary case and the
    /// default — the score below is the one the engine has always used.
    /// </param>
    public static SiteChoice Best(
        WorldState world, Region region, int sitesPerAxis, FoundingNeed need = FoundingNeed.Land)
    {
        int stride = Math.Max(8, region.Bounds.Width / Math.Max(1, sitesPerAxis));

        Hydrology hydrology = world.Terrain.Hydrology;
        Landform landform = world.Terrain.Landform;

        // Materialised rather than streamed, because a candidate is scored partly on how it stands
        // against its neighbours and those neighbours are the other candidates. This is the
        // refinement the decision already pays for, read a second time for free.
        //
        // The dictionary is a lookup and never an iteration order: Dictionary enumerates in an
        // order that depends on hash codes, and this engine builds on net7 and tests on net10, so
        // ranking candidates in that order would make which site wins a tie depend on the runtime.
        // The ordered list is what gets walked, and it comes out of RefinedPoints in grid order.
        var candidates = new Dictionary<Point2, TerrainSample>();
        var ordered = new List<Point2>();

        foreach (KeyValuePair<Point2, TerrainSample> candidate
                 in world.Terrain.RefinedPoints(region.Bounds, stride))
        {
            if (candidates.TryAdd(candidate.Key, candidate.Value)) ordered.Add(candidate.Key);
        }

        Point2 best = new(region.CenterX, region.CenterZ);
        double bestScore = double.NegativeInfinity;
        SiteCharacter bestCharacter = SiteCharacter.Plain;

        foreach (Point2 at in ordered)
        {
            TerrainSample sample = candidates[at];
            if (sample.IsSubmerged || sample.Water != WaterKind.None) continue;

            double grade = SteepestGrade(candidates, at, sample, stride);
            double score = Score(sample, at, grade, hydrology, landform, need);

            // Strictly greater, so grid order breaks exact ties.
            if (score > bestScore)
            {
                bestScore = score;
                best = at;
                bestCharacter = Characterise(at, sample, hydrology, landform, need);
            }
        }

        return new SiteChoice(best, bestCharacter);
    }

    /// <summary>
    /// The steepest grade onto this spot, from the candidates touching it.
    /// </summary>
    /// <remarks>
    /// <para>The steepest neighbour rather than the mean, because what makes ground unbuildable is
    /// its worst side rather than its average one.</para>
    ///
    /// <para><b>Defensibility was tried four ways and cut.</b> M10 set out to score defensible
    /// ground from real slope, and every formulation of it cost more buildability than the label
    /// was worth. Prominence and steepness are not merely correlated — at the resolution a siting
    /// decision can see, they are the same measurement, so a reward for one is a reward for the
    /// other with extra steps. Across eight seeds, settlements standing on a grade steeper than
    /// 1-in-2:</para>
    ///
    /// <list type="table">
    ///   <item><description>no defensibility term — <b>8.4%</b>, median site grade 0.231</description></item>
    ///   <item><description>rise above the touching ring — 12.8%, median 0.262, and <em>no</em> site
    ///   ever notable enough to be called a spur</description></item>
    ///   <item><description>rise above the ring beyond it, so a flat crown could qualify — 14.8%,
    ///   median 0.283, 16 spurs</description></item>
    ///   <item><description>the same, gated on the ground being flat enough to build on — 13.1%,
    ///   median 0.283, 7 spurs</description></item>
    /// </list>
    ///
    /// <para>So the best case bought a label on 3% of settlements for six points of exactly the
    /// defect this milestone exists to remove. Held to the bar the design set — a term that does
    /// not earn its place is cut rather than kept for flavour — that is not a close decision.
    /// Defensibility as a property of <em>ground</em> is what failed here; walls and garrisons are
    /// a settlement's own doing and remain open to whatever eventually reads
    /// <c>Settlement.IsFortified</c>.</para>
    /// </remarks>
    private static double SteepestGrade(
        Dictionary<Point2, TerrainSample> candidates, Point2 at, TerrainSample sample, int stride)
    {
        double diagonal = DetMath.Sqrt(2.0) * stride;
        double steepest = 0.0;

        for (int d = 0; d < 8; d++)
        {
            var neighbour = new Point2(at.X + (OffsetX[d] * stride), at.Z + (OffsetZ[d] * stride));
            if (!candidates.TryGetValue(neighbour, out TerrainSample other)) continue;
            if (other.IsSubmerged) continue;

            double distance = (OffsetX[d] != 0 && OffsetZ[d] != 0) ? diagonal : stride;
            double grade = Math.Abs(sample.Height - other.Height) / distance;
            if (grade > steepest) steepest = grade;
        }

        return steepest;
    }

    private static readonly int[] OffsetX = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] OffsetZ = { 0, 1, 1, 1, 0, -1, -1, -1 };

    /// <summary>
    /// How good a specific spot is to build on, for what the party came to do.
    /// </summary>
    /// <remarks>
    /// <para>Polynomial only, per the determinism rules in <see cref="DetMath"/>.</para>
    ///
    /// <para><b>A party sent for ore reads the same ground with four weights moved.</b> Soil is
    /// worth about a third of what it is worth to farmers, because they are not there to farm it,
    /// and the geologic activity under the candidate is worth nearly what soil is worth to
    /// everybody else. That measure comes from the refinement this decision already performs — the
    /// deposit is scored at 16 units, four times finer than the region field that sent them here,
    /// so the camp stands on the best of the ore rather than merely inside the patch containing
    /// it.</para>
    ///
    /// <para><b>Slope stays buildability, and is never inverted.</b> Miners live on a terrace and
    /// walk to the face; a camp on a cliff is as impossible as a town on one. The penalty is
    /// relaxed rather than removed — from 0.90 to 0.55 — which lets a mine take ground a farming
    /// party would have refused without letting it take ground nobody could stand on. Thin air is
    /// relaxed much further, because altitude is a thing mining country simply is.</para>
    /// </remarks>
    private static double Score(
        TerrainSample sample,
        Point2 at,
        double grade,
        Hydrology hydrology,
        Landform landform,
        FoundingNeed need)
    {
        bool forOre = need == FoundingNeed.Ore;

        double score = sample.Fertility * (forOre ? OreSoilWeight : SoilWeight);
        if (forOre) score += sample.GeologicActivity * OreWeight;

        // Fresh water, graded by how far it is rather than whether the cell containing this
        // point happens to be flagged. A confluence is worth more than a reach because two
        // valleys meet there, which is a junction of routes as well as of water.
        score += hydrology.RiverAccess(at.X, at.Z) * FreshWaterWeight;
        if (hydrology.IsConfluence(at.X, at.Z)) score += ConfluenceBonus;

        // The sea is worth what its shelter is worth. An exposed shore is a place to watch for
        // ships, not to bring them into.
        score += hydrology.SeaAccess(at.X, at.Z)
                 * hydrology.ShelterAt(at.X, at.Z)
                 * HarbourWeight;

        if (landform.IsPass(at.X, at.Z)) score += PassBonus;

        double steepness = DetMath.InverseLerp(BuildableGrade, UnbuildableGrade, grade);
        score -= steepness * steepness * (forOre ? OreSlopePenalty : SlopePenalty);

        score -= DetMath.InverseLerp(900.0, 2200.0, sample.Height)
                 * (forOre ? OreThinAirPenalty : ThinAirPenalty);

        return score;
    }

    /// <summary>
    /// What the chosen ground is, in the order a chronicle would think of it.
    /// </summary>
    /// <remarks>
    /// <para>Rarest and most consequential first, so a town at a river mouth in a sheltered bay is
    /// recorded as an estuary rather than as a harbour that also happens to have a river. The
    /// thresholds are read from the same measures the score used, so the character can never
    /// describe a site the score did not actually pick for that reason.</para>
    ///
    /// <para><b>Why the errand comes first.</b> A camp put on a mountain for the ore under it is
    /// remembered for the ore even when there is a river beside it, because that is what it was
    /// for — the character is why they stood there, and for a purpose founding the reason is not in
    /// doubt. It still has to be true: a party can be sent to a region for its geology and find
    /// that the best ground in it is not over the deposit, and then the site is recorded as
    /// whatever it actually is. That is the same rule as everything below it, applied to the one
    /// character an errand rather than a search produces.</para>
    /// </remarks>
    private static SiteCharacter Characterise(
        Point2 at, TerrainSample sample, Hydrology hydrology, Landform landform, FoundingNeed need)
    {
        if (need == FoundingNeed.Ore && sample.GeologicActivity >= Specializations.OreThreshold)
        {
            return SiteCharacter.Mine;
        }

        bool nearRiver = hydrology.RiverDistance(at.X, at.Z) <= WaterAtHand;
        bool nearSea = hydrology.CoastDistance(at.X, at.Z) <= WaterAtHand;

        if (hydrology.IsConfluence(at.X, at.Z)) return SiteCharacter.Confluence;
        if (hydrology.IsEstuary(at.X, at.Z)) return SiteCharacter.Estuary;
        if (nearSea && hydrology.ShelterAt(at.X, at.Z) >= ShelteredEnough) return SiteCharacter.Harbour;
        if (landform.IsPass(at.X, at.Z)) return SiteCharacter.Pass;
        if (nearRiver) return SiteCharacter.Riverside;
        if (nearSea) return SiteCharacter.Coastal;

        return SiteCharacter.Plain;
    }
}

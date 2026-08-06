using HistoryEngine.Core;
using HistoryEngine.Terrain;

namespace HistoryEngine.World;

/// <summary>
/// Chooses where inside a region a settlement should stand.
/// </summary>
/// <remarks>
/// The one place in the simulation that spends real terrain samples, and the reason it is
/// centralised: every caller goes through <see cref="TerrainAtlas.RefinedPoints"/> at a stride
/// it declares up front, so the cost of a siting decision is always
/// <c>sitesPerAxis²</c> samples and always visible at the call site. Sites already refined by
/// an earlier decision are served from cache.
///
/// <para>Phase 2 is expected to grow this considerably — river confluences, mountain passes,
/// harbour quality, defensibility from real slope — which is another reason for it to be one
/// function rather than logic spread through the systems that need it.</para>
/// </remarks>
public static class SiteSelection
{
    /// <summary>
    /// The best position for a settlement within <paramref name="region"/>.
    /// </summary>
    /// <param name="sitesPerAxis">
    /// Candidates per axis. Costs this squared in terrain samples on first evaluation of a
    /// region, and nothing thereafter.
    /// </param>
    public static Point2 Best(WorldState world, Region region, int sitesPerAxis)
    {
        int stride = Math.Max(8, region.Bounds.Width / Math.Max(1, sitesPerAxis));

        Point2 best = new(region.CenterX, region.CenterZ);
        double bestScore = double.NegativeInfinity;
        Hydrology hydrology = world.Terrain.Hydrology;

        foreach (KeyValuePair<Point2, TerrainSample> candidate
                 in world.Terrain.RefinedPoints(region.Bounds, stride))
        {
            TerrainSample sample = candidate.Value;
            if (sample.IsSubmerged || sample.Water != WaterKind.None) continue;

            double score = Score(sample, candidate.Key, hydrology);

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate.Key;
            }
        }

        return best;
    }

    /// <summary>
    /// How good a specific spot is to build on.
    /// </summary>
    /// <remarks>
    /// Fertility carries most of the weight, with the premiums that historically put towns on
    /// rivers and coasts — fresh water, transport, trade — and a penalty for thin highland air.
    /// Polynomial only, per the determinism rules in <see cref="DetMath"/>.
    /// </remarks>
    private static double Score(TerrainSample sample, Point2 at, Hydrology hydrology)
    {
        double score = sample.Fertility;

        if (hydrology.IsRiver(at.X, at.Z)) score += 0.25;
        if (hydrology.IsCoast(at.X, at.Z)) score += 0.15;

        score -= DetMath.InverseLerp(900.0, 2200.0, sample.Height) * 0.3;

        return score;
    }
}

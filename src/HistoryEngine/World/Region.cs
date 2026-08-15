using HistoryEngine.Core;
using HistoryEngine.Terrain;

namespace HistoryEngine.World;

/// <summary>
/// A square patch of the world — the unit of territory, adjacency and land quality.
/// </summary>
/// <remarks>
/// Regions exist so the simulation can reason about space without reasoning about
/// coordinates. Territory is a set of region ids, expansion walks region adjacency, and
/// land quality is a per-region score. Settlements still carry exact positions, because
/// those become real map locations in Phase 2 and real world coordinates in Phase 3, but
/// nothing in diplomacy or expansion needs that precision.
///
/// <para>Every field here is derived from the already-primed terrain lattice, so building
/// the region grid costs no samples.</para>
/// </remarks>
public sealed class Region
{
    public Region(
        EntityId id,
        TerrainBounds bounds,
        Biome biome,
        double fertility,
        double meanHeight,
        double rainfall,
        double temperature,
        double geologicActivity,
        bool isLand,
        bool hasRiver,
        bool isCoastal,
        double riverAccess,
        double harbourQuality,
        double ruggedness)
    {
        RiverAccess = riverAccess;
        HarbourQuality = harbourQuality;
        Ruggedness = ruggedness;
        Id = id;
        Bounds = bounds;
        Biome = biome;
        Fertility = fertility;
        MeanHeight = meanHeight;
        Rainfall = rainfall;
        Temperature = temperature;
        GeologicActivity = geologicActivity;
        IsLand = isLand;
        HasRiver = hasRiver;
        IsCoastal = isCoastal;
        AdjacentRegions = new List<EntityId>();
    }

    public EntityId Id { get; }

    public TerrainBounds Bounds { get; }

    public int CenterX => Bounds.CenterX;

    public int CenterZ => Bounds.CenterZ;

    public Biome Biome { get; }

    /// <summary>Mean crop potential across the region, in [0, 1].</summary>
    public double Fertility { get; }

    public double MeanHeight { get; }

    public double Rainfall { get; }

    public double Temperature { get; }

    /// <summary>Mean geologic activity, in [0, 1]. Drives mining specialization.</summary>
    public double GeologicActivity { get; }

    public bool IsLand { get; }

    public bool HasRiver { get; }

    public bool IsCoastal { get; }

    /// <summary>
    /// Fresh water at the best spot in the region, in [0, 1].
    /// </summary>
    /// <remarks>
    /// The best spot rather than the average, because a region is only ever settled at one point
    /// and siting will put the town wherever the water is. Averaging would rank a region with one
    /// excellent riverside corner below a uniformly mediocre one, and then the town would be built
    /// on the river anyway.
    /// </remarks>
    public double RiverAccess { get; }

    /// <summary>Sheltered water at the best spot in the region, in [0, 1]. Zero inland.</summary>
    public double HarbourQuality { get; }

    /// <summary>How broken the country is, in [0, 1]. Averaged — this one is about the whole patch.</summary>
    public double Ruggedness { get; }

    /// <summary>Four-way neighbours. Populated once when the grid is built.</summary>
    public List<EntityId> AdjacentRegions { get; }

    /// <summary>The civilization claiming this region, or <see cref="EntityId.None"/>.</summary>
    public EntityId Owner { get; set; } = EntityId.None;

    /// <summary>
    /// How attractive this region is to settle, in [0, 1].
    /// </summary>
    /// <remarks>
    /// <para>Fertility dominates, with water adding the premium that historically put cities on it
    /// — fresh water, transport, trade — and habitability gating the whole thing. This is the score
    /// expansion sorts on, and therefore the thing that decides which country a realm wants;
    /// <see cref="SiteSelection"/> only decides where inside it to stand.</para>
    ///
    /// <para><b>Graded rather than flagged, since M10.</b> The premiums used to be a flat 0.18 for
    /// touching a river and 0.12 for touching the sea, which made a region with a great river
    /// through its middle indistinguishable from one clipping a headwater at its corner, and an
    /// enclosed bay indistinguishable from an exposed cliff coast. Both distinctions are now
    /// measured, so the ranking expansion sorts on says what it means.</para>
    /// </remarks>
    public double Habitability
    {
        get
        {
            if (!IsLand || !BiomeClassifier.IsHabitable(Biome)) return 0.0;

            double score = Fertility * 0.7;
            score += RiverAccess * 0.18;
            score += HarbourQuality * 0.12;

            // Harsh ground is survivable but not attractive — height for the thin air, and broken
            // country for everything a valley wall costs to farm, build on and walk across.
            double exposure = DetMath.Lerp(
                1.0, 0.55, DetMath.InverseLerp(1200.0, 2100.0, MeanHeight));
            double footing = DetMath.Lerp(1.0, 0.70, Ruggedness);

            return DetMath.Clamp01(score * exposure * footing);
        }
    }

    public override string ToString() => $"{Id} {Biome} hab={Habitability:F2}";
}

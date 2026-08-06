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
        bool isLand,
        bool hasRiver,
        bool isCoastal)
    {
        Id = id;
        Bounds = bounds;
        Biome = biome;
        Fertility = fertility;
        MeanHeight = meanHeight;
        Rainfall = rainfall;
        Temperature = temperature;
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

    public bool IsLand { get; }

    public bool HasRiver { get; }

    public bool IsCoastal { get; }

    /// <summary>Four-way neighbours. Populated once when the grid is built.</summary>
    public List<EntityId> AdjacentRegions { get; }

    /// <summary>The civilization claiming this region, or <see cref="EntityId.None"/>.</summary>
    public EntityId Owner { get; set; } = EntityId.None;

    /// <summary>
    /// How attractive this region is to settle, in [0, 1].
    /// </summary>
    /// <remarks>
    /// Fertility dominates, with rivers and coastlines adding the premium that historically
    /// put cities on them — fresh water, transport, trade — and habitability gating the
    /// whole thing. This is the score expansion sorts on.
    /// </remarks>
    public double Habitability
    {
        get
        {
            if (!IsLand || !BiomeClassifier.IsHabitable(Biome)) return 0.0;

            double score = Fertility * 0.7;
            if (HasRiver) score += 0.18;
            if (IsCoastal) score += 0.12;

            // Harsh ground is survivable but not attractive.
            double exposure = DetMath.Lerp(
                1.0, 0.55, DetMath.InverseLerp(1200.0, 2100.0, MeanHeight));

            return DetMath.Clamp01(score * exposure);
        }
    }

    public override string ToString() => $"{Id} {Biome} hab={Habitability:F2}";
}

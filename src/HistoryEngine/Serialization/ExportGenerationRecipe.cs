using HistoryEngine.World;

namespace HistoryEngine.Serialization;

/// <summary>
/// Versioned rerun eligibility. Unsupported inputs remain identified by ConfigHash and
/// TerrainSource; the viewer must never replace them with procedural defaults.
/// </summary>
public sealed record ExportGenerationRecipe(
    int Version,
    int InitialCivilizations,
    string TerrainSource,
    string? UnsupportedReason)
{
    public static ExportGenerationRecipe FromConfig(WorldConfig config)
    {
        var supported = new WorldConfig
        {
            Seed = config.Seed,
            Years = config.Years,
            InitialCivilizations = config.InitialCivilizations,
            WorldSize = config.WorldSize,
            EastWestPeriodic = config.EastWestPeriodic,
        };

        string? reason = config.TerrainSource.Length > 0
            ? "This world uses external terrain. Rerun the original CLI command with the same terrain files."
            : config != supported
                ? "This world uses custom configuration. Rerun the original CLI command or library invocation."
                : null;

        return new(1, config.InitialCivilizations, config.TerrainSource, reason);
    }
}

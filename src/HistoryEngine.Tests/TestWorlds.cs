using HistoryEngine.World;

namespace HistoryEngine.Tests;

/// <summary>Points the source-scanning guard tests at the engine project.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class EngineSourcePathAttribute : Attribute
{
    public EngineSourcePathAttribute(string path) => Path = path;

    public string Path { get; }
}

/// <summary>Shared world configurations, so tests exercise the same shapes.</summary>
internal static class TestWorlds
{
    /// <summary>Small and fast — for tests where the shape of a run matters, not its scale.</summary>
    public static WorldConfig Small(ulong seed = 7) => new()
    {
        Seed = seed,
        Years = 60,
        WorldSize = 1024,
        RegionSize = 128,
        TerrainStride = 128,
        InitialCivilizations = 3,
        MapRasterResolution = 32,
    };

    /// <summary>The shape the milestone targets: a few centuries, several civilizations.</summary>
    public static WorldConfig Standard(ulong seed = 42) => new()
    {
        Seed = seed,
        Years = 300,
        WorldSize = 4096,
        RegionSize = 128,
        TerrainStride = 256,
        InitialCivilizations = 8,
        MapRasterResolution = 64,
    };

    /// <summary>
    /// A full millennium, for properties that only settle once the world has matured.
    /// </summary>
    /// <remarks>
    /// The settlement hierarchy is the case this exists for. Three centuries is not long enough for
    /// it: settlements founded late are still climbing toward their ceilings, so the size
    /// distribution is dominated by how recently each place was founded rather than by what the
    /// land and the roads will eventually support. The distributional failures this suite now
    /// guards against were only visible at a thousand years.
    /// </remarks>
    public static WorldConfig Long(ulong seed = 42) => new()
    {
        Seed = seed,
        Years = 1000,
        WorldSize = 4096,
        RegionSize = 128,
        TerrainStride = 256,
        InitialCivilizations = 8,
        MapRasterResolution = 64,
    };
}

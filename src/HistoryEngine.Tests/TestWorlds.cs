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
}

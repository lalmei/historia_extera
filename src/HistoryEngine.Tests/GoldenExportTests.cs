using HistoryEngine.Serialization;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// Pins the exact output of a known seed, across processes and across time.
/// </summary>
/// <remarks>
/// <para><see cref="DeterminismTests.SameSeedProducesByteIdenticalExport"/> proves the engine agrees
/// with itself within one process. This proves it agrees with a value committed to the repository —
/// which is the guarantee users actually care about, and the only one that catches drift from a
/// runtime upgrade, a platform difference, or a refactor that changes behaviour by accident.</para>
///
/// <para><b>When this fails.</b> It means the history for this seed changed. That is not
/// automatically a bug — deliberately altering a growth rate, a system's order, or a scoring curve
/// changes every history, and the golden must be regenerated. But it must be a decision, and the
/// regeneration must be a reviewable diff, which is why the expected value lives in a file rather
/// than being written back automatically. If the fingerprint changes when you did not intend to
/// change simulation behaviour, that is the bug this test exists to find.</para>
///
/// <para>Regenerate with: <c>dotnet run --project src/HistoryEngine.Cli -- --fingerprint</c></para>
/// </remarks>
public sealed class GoldenExportTests
{
    private const string GoldenFileName = "standard-seed42.sha256";

    [Fact]
    public void StandardWorldMatchesItsCommittedFingerprint()
    {
        string path = Path.Combine(EngineSource.Root, "..", "HistoryEngine.Tests", "Goldens", GoldenFileName);
        path = Path.GetFullPath(path);

        Assert.True(
            File.Exists(path),
            $"Missing golden file {path}. Generate it with:\n" +
            "  dotnet run --project src/HistoryEngine.Cli -- --fingerprint > " +
            $"src/HistoryEngine.Tests/Goldens/{GoldenFileName}");

        string expected = File.ReadAllText(path).Trim();
        string actual = WorldExporter.Fingerprint(HistoryRun.Execute(Golden()).ToExport());

        Assert.True(
            expected == actual,
            $"The history for seed 42 changed.\n  expected {expected}\n  actual   {actual}\n\n" +
            "If you meant to change simulation behaviour, regenerate the golden and review the " +
            "diff. If you did not, something has become non-deterministic.");
    }

    /// <summary>
    /// The pinned configuration. Must never change without regenerating the golden.
    /// </summary>
    /// <remarks>
    /// Written out in full rather than reusing <c>TestWorlds.Standard</c>, so that tuning a shared
    /// test fixture cannot silently invalidate the golden.
    /// </remarks>
    public static WorldConfig Golden() => new()
    {
        Seed = 42,
        Years = 300,
        StartYear = 1,
        WorldSize = 4096,
        RegionSize = 128,
        TerrainStride = 256,
        InitialCivilizations = 8,
        MapRasterResolution = 64,
        Terrain = new Terrain.TerrainSettings(),
    };
}

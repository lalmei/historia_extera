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
/// <para><b>Regenerate with the whole command, arguments and all:</b></para>
/// <code>
/// dotnet run --project src/HistoryEngine.Cli -- \
///     --seed 42 --years 300 --civs 8 --size 4096 --raster 64 --fingerprint
/// </code>
///
/// <para>Every argument is load-bearing, and the reason to say so here is that the shorter command
/// this used to document — <c>--fingerprint</c> alone — runs successfully and prints a hash for a
/// different world. The CLI defaults to seed 1 and a 256-resolution raster against the seed 42 and
/// 64 this pins, so the value it prints has never matched and never will. Written into the golden
/// it silently replaces the pin with a fingerprint of something nobody is testing, and the next
/// real regression passes.</para>
///
/// <para>The failure mode is the one this whole file exists to prevent, arriving through the
/// instructions rather than through the code: a golden regenerated for a reason that looked fine.
/// Anything that changes <see cref="Golden"/> has to change this command to match.</para>
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
            "  dotnet run --project src/HistoryEngine.Cli -- --seed 42 --years 300 --civs 8 " +
            "--size 4096 --raster 64 --fingerprint > " +
            $"src/HistoryEngine.Tests/Goldens/{GoldenFileName}\n" +
            "Every argument matters: without them the CLI fingerprints its own default world " +
            "and prints a hash that has never matched this pin.");

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

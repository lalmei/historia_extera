using System.Diagnostics;
using HistoryEngine.Serialization;
using HistoryEngine.Systems;
using HistoryEngine.Terrain;
using HistoryEngine.World;

namespace HistoryEngine;

/// <summary>
/// The engine's front door: config in, simulated world and export out.
/// </summary>
/// <remarks>
/// Exists so the CLI and the test suite drive the engine through exactly the same path. If tests
/// assembled worlds by hand while the CLI used a different sequence, the determinism suite would
/// be verifying something nobody ships.
///
/// <para>The terrain sampler is always wrapped in a <see cref="CountingTerrainSampler"/>, so
/// every run — including every test run — measures what it would cost inside Vintage Story.
/// Making that measurement unconditional is what turns the Phase 3 budget from a thing to
/// remember into a thing that is simply always known.</para>
/// </remarks>
public sealed class HistoryRun
{
    private HistoryRun(
        WorldState world,
        Simulator simulator,
        CountingTerrainSampler counter,
        long simulationSamples,
        TimeSpan elapsed)
    {
        World = world;
        Simulator = simulator;
        Counter = counter;
        SimulationSamples = simulationSamples;
        Elapsed = elapsed;
    }

    public WorldState World { get; }

    public Simulator Simulator { get; }

    public CountingTerrainSampler Counter { get; }

    /// <summary>
    /// Terrain samples spent on worldgen and simulation, excluding the export raster.
    /// </summary>
    /// <remarks>Captured before the raster is built. This is the figure under budget.</remarks>
    public long SimulationSamples { get; }

    public TimeSpan Elapsed { get; }

    /// <summary>Builds a world and simulates every configured year.</summary>
    /// <param name="inner">
    /// Terrain backend. Defaults to Phase 1's noise sampler; Phase 2 and 3 pass their own here,
    /// and that is the entire integration surface.
    /// </param>
    /// <param name="simulator">
    /// The system list to run. Defaults to the standard order, which is what every real run uses;
    /// tests pass a subset to measure what one system is responsible for. Note that the system
    /// order is folded into the run's identity, so a custom list produces a legitimately
    /// different history rather than a comparable one.
    /// </param>
    public static HistoryRun Execute(
        WorldConfig config, ITerrainSampler? inner = null, Simulator? simulator = null)
    {
        config.Validate();

        inner ??= new ProceduralTerrainSampler(config.Seed, config.Bounds, config.Terrain);
        var counter = new CountingTerrainSampler(inner);

        var stopwatch = Stopwatch.StartNew();

        WorldState world = WorldBuilder.Create(config, counter);
        simulator ??= new Simulator();
        simulator.Run(world);

        stopwatch.Stop();

        return new HistoryRun(world, simulator, counter, counter.SampleCount, stopwatch.Elapsed);
    }

    /// <summary>Builds the export document, including the map raster.</summary>
    public WorldExport ToExport() =>
        WorldExporter.Build(World, Simulator, SimulationSamples, Counter);
}

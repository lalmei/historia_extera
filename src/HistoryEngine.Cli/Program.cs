using System.Globalization;
using HistoryEngine;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Serialization;
using HistoryEngine.Terrain;
using HistoryEngine.World;

namespace HistoryEngine.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h" || args[0] == "help"))
            {
                PrintUsage();
                return 0;
            }

            var options = CliOptions.Parse(args);
            return Generate(options);
        }
        catch (CliException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            Console.Error.WriteLine();
            PrintUsage();
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Generate(CliOptions options)
    {
        var config = new WorldConfig
        {
            Seed = options.Seed,
            Years = options.Years,
            WorldSize = options.WorldSize,
            InitialCivilizations = options.Civilizations,
            MapRasterResolution = options.RasterResolution,
        };

        if (options.EmitTerrain is not null) return EmitTerrain(config, options);

        RasterTerrainSampler? raster = null;

        if (options.Terrain is not null)
        {
            raster = RasterTerrainSampler.Load(options.Terrain);

            if (options.SizeGiven && options.WorldSize != raster.Bounds.Width)
            {
                Console.Error.WriteLine(
                    $"note: --size {options.WorldSize} ignored; the raster set covers " +
                    $"{raster.Bounds.Width} units.");
            }

            // Records both the extent the rasters cover and their content digest, so the
            // exported config hash identifies the terrain this history was run over.
            config = config.WithTerrain(raster);
        }

        if (options.FingerprintOnly)
        {
            // Nothing but the digest on stdout, so the output can be redirected straight into the
            // golden file that GoldenExportTests pins.
            Console.WriteLine(
                WorldExporter.Fingerprint(HistoryRun.Execute(config, raster).ToExport()));
            return 0;
        }

        Console.WriteLine($"Generating {config.Years} years, seed {config.Seed}, config {config.ConfigHash}");

        HistoryRun run = HistoryRun.Execute(config, raster);
        WorldExport export = run.ToExport();

        string json = WorldExporter.ToJson(export, options.Pretty);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(options.Output));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(options.Output, json);

        PrintSummary(run, export, json.Length, options.Output);

        if (options.SampleEvents > 0) PrintSampleEvents(run, options.SampleEvents);

        return 0;
    }

    /// <summary>
    /// Bakes the configured world out as a raster set and simulates nothing.
    /// </summary>
    /// <remarks>
    /// The other half of the Phase 2 route. <c>--terrain</c> reads rasters some other generator
    /// produced; this writes a set from Phase 1's own noise world, which gives anyone wiring up a
    /// real generator a reference manifest to compare theirs against, and gives the test suite a
    /// world it can reach by two entirely different paths.
    /// </remarks>
    private static int EmitTerrain(WorldConfig config, CliOptions options)
    {
        config.Validate();

        var sampler = new ProceduralTerrainSampler(config.Seed, config.Bounds, config.Terrain);
        string manifest = TerrainRasterBake.Write(sampler, options.EmitTerrain!, options.TerrainResolution);

        int perAxis = options.TerrainResolution;

        Console.WriteLine(
            $"Baked seed {config.Seed} at {perAxis}x{perAxis} " +
            $"({config.WorldSize / (double)perAxis:F1} units per pixel)");
        Console.WriteLine($"  written  {manifest}");
        Console.WriteLine();
        Console.WriteLine($"Run a history over it with:  legends --terrain {manifest}");

        return 0;
    }

    private static void PrintSummary(HistoryRun run, WorldExport export, int jsonLength, string path)
    {
        WorldState world = run.World;

        int activeCivs = 0;
        int fallenCivs = 0;
        foreach (Civilization civilization in world.Civilizations)
        {
            if (civilization.IsActive) activeCivs++;
            else fallenCivs++;
        }

        int activeSettlements = 0;
        int abandoned = 0;
        int cities = 0;
        foreach (Settlement settlement in world.Settlements)
        {
            if (settlement.IsActive)
            {
                activeSettlements++;
                if (settlement.Tier == SettlementTier.City) cities++;
            }
            else
            {
                abandoned++;
            }
        }

        int living = 0;
        foreach (Figure figure in world.Figures)
        {
            if (figure.IsAlive) living++;
        }

        int standingHouses = 0;
        int extinctHouses = 0;
        foreach (Dynasty house in world.Dynasties)
        {
            if (house.IsExtinct) extinctHouses++;
            else standingHouses++;
        }

        ExportSampleStats sampling = export.Meta.TerrainSampling;

        Console.WriteLine();
        Console.WriteLine("── Terrain ──────────────────────────────");
        Console.WriteLine($"  backend        {Backend(world.Config)}");
        Console.WriteLine($"  extent         {world.Terrain.Bounds.Width} x {world.Terrain.Bounds.Height}");
        Console.WriteLine($"  measured       {Fields(world.Terrain.Capabilities)}");

        // The fields no backend measured are modelled from elevation and latitude. Printing
        // them is the point of the capability flags: a world built on a bare heightmap should
        // say so rather than present six fields as if they were all observed.
        TerrainCapabilities modelled = TerrainCapabilities.Standard & ~world.Terrain.Capabilities;
        if (modelled != TerrainCapabilities.None)
        {
            Console.WriteLine($"  modelled       {Fields(modelled)}");
        }

        Console.WriteLine();
        Console.WriteLine("── History ──────────────────────────────");
        Console.WriteLine($"  years          {world.StartYear}–{world.EndYear}");
        Console.WriteLine($"  events         {world.Chronicle.Count:N0}");
        Console.WriteLine($"  civilizations  {activeCivs} standing, {fallenCivs} fallen");
        Console.WriteLine($"  settlements    {activeSettlements} active ({cities} cities), {abandoned} abandoned");
        Console.WriteLine($"  figures        {world.Figures.Count} recorded, {living} living");
        Console.WriteLine($"  houses         {standingHouses} standing, {extinctHouses} died out");
        Console.WriteLine($"  regions        {world.Regions.Count}");
        Console.WriteLine();
        Console.WriteLine("── Terrain sampling ─────────────────────");
        Console.WriteLine(
            $"  simulation     {sampling.SimulationSamples:N0} samples " +
            $"(≈{sampling.EstimatedGameSecondsSimulation:F1}s in Vintage Story)");
        Console.WriteLine(
            $"  map raster     {sampling.RasterSamples:N0} samples " +
            $"(≈{sampling.EstimatedGameSecondsRaster:F1}s, presentation only)");
        Console.WriteLine();
        Console.WriteLine("── Output ───────────────────────────────");
        Console.WriteLine($"  elapsed        {run.Elapsed.TotalMilliseconds:N0} ms");
        Console.WriteLine($"  size           {jsonLength / 1024.0 / 1024.0:F2} MB");
        Console.WriteLine($"  written        {path}");
    }

    private static string Backend(WorldConfig config) =>
        config.TerrainSource.Length == 0
            ? "procedural (Phase 1 noise)"
            : config.TerrainSource;

    private static string Fields(TerrainCapabilities capabilities) =>
        capabilities == TerrainCapabilities.None ? "none" : capabilities.ToString();

    /// <summary>
    /// Prints a spread of narrated events across the whole timespan.
    /// </summary>
    /// <remarks>
    /// Sampling evenly rather than taking the first N, because the opening years are all
    /// foundings and say nothing about whether the middle of a history reads well.
    /// </remarks>
    private static void PrintSampleEvents(HistoryRun run, int count)
    {
        IReadOnlyList<HistoryEvent> events = run.World.Chronicle.Events;
        if (events.Count == 0) return;

        Console.WriteLine();
        Console.WriteLine("── Chronicle sample ─────────────────────");

        int step = Math.Max(1, events.Count / count);
        for (int i = 0; i < events.Count && i / step < count; i += step)
        {
            HistoryEvent entry = events[i];
            Console.WriteLine($"  {entry.Year,5}  {run.World.Narrate(entry)}");
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("legends — generate a procedural world history");
        Console.WriteLine();
        Console.WriteLine("usage: legends [options]");
        Console.WriteLine();
        Console.WriteLine("  --seed <n>        master seed (default 1)");
        Console.WriteLine("  --years <n>       years to simulate (default 300)");
        Console.WriteLine("  --civs <n>        starting civilizations (default 8)");
        Console.WriteLine("  --size <n>        world side length in units (default 4096)");
        Console.WriteLine("  --raster <n>      map raster resolution per axis (default 256)");
        Console.WriteLine("  --out <path>      output file (default viewer/public/worlds/world.json)");
        Console.WriteLine("  --pretty          indent the JSON (not the canonical form)");
        Console.WriteLine("  --sample <n>      print n narrated events (default 12, 0 to disable)");
        Console.WriteLine("  --fingerprint     print only the export digest, write nothing");
        Console.WriteLine();
        Console.WriteLine("Phase 2 terrain — run a history over rasters from another generator:");
        Console.WriteLine();
        Console.WriteLine("  --terrain <path>  terrain manifest (JSON) to sample instead of noise;");
        Console.WriteLine("                    sets the world size and is recorded in the config hash");
        Console.WriteLine("  --emit-terrain <dir>");
        Console.WriteLine("                    bake this seed's noise world into a raster set and exit");
        Console.WriteLine($"  --terrain-res <n> resolution per axis when baking (default {TerrainRasterBake.DefaultResolution})");
        Console.WriteLine();
        Console.WriteLine("Only a height layer is required. Any field no raster supplies is modelled");
        Console.WriteLine("from elevation and latitude, and left out of the declared capabilities.");
        Console.WriteLine();
        Console.WriteLine("Identical --seed and config always produce an identical file.");
    }
}

internal sealed class CliException : Exception
{
    public CliException(string message) : base(message)
    {
    }
}

internal sealed record CliOptions
{
    public ulong Seed { get; init; } = 1;

    public int Years { get; init; } = 300;

    public int Civilizations { get; init; } = 8;

    public int WorldSize { get; init; } = 4096;

    public int RasterResolution { get; init; } = 256;

    public string Output { get; init; } = Path.Combine("viewer", "public", "worlds", "world.json");

    public bool Pretty { get; init; }

    public int SampleEvents { get; init; } = 12;

    /// <summary>Print only the export fingerprint. For regenerating the golden determinism file.</summary>
    public bool FingerprintOnly { get; init; }

    /// <summary>Terrain manifest to run over, instead of Phase 1's noise sampler.</summary>
    public string? Terrain { get; init; }

    /// <summary>Directory to bake a raster set into. Simulates nothing.</summary>
    public string? EmitTerrain { get; init; }

    public int TerrainResolution { get; init; } = TerrainRasterBake.DefaultResolution;

    /// <summary>Whether <c>--size</c> was given, so a raster set silently overriding it can be flagged.</summary>
    public bool SizeGiven { get; init; }

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();

        for (int i = 0; i < args.Length; i++)
        {
            string flag = args[i];

            switch (flag)
            {
                case "--pretty":
                    options = options with { Pretty = true };
                    break;

                case "--fingerprint":
                    options = options with { FingerprintOnly = true };
                    break;

                case "--seed":
                    options = options with { Seed = ParseULong(flag, Next(args, ref i)) };
                    break;

                case "--years":
                    options = options with { Years = ParseInt(flag, Next(args, ref i)) };
                    break;

                case "--civs":
                    options = options with { Civilizations = ParseInt(flag, Next(args, ref i)) };
                    break;

                case "--size":
                    options = options with
                    {
                        WorldSize = ParseInt(flag, Next(args, ref i)),
                        SizeGiven = true,
                    };
                    break;

                case "--terrain":
                    options = options with { Terrain = Next(args, ref i) };
                    break;

                case "--emit-terrain":
                    options = options with { EmitTerrain = Next(args, ref i) };
                    break;

                case "--terrain-res":
                    options = options with { TerrainResolution = ParseInt(flag, Next(args, ref i)) };
                    break;

                case "--raster":
                    options = options with { RasterResolution = ParseInt(flag, Next(args, ref i)) };
                    break;

                case "--sample":
                    options = options with { SampleEvents = ParseInt(flag, Next(args, ref i)) };
                    break;

                case "--out":
                    options = options with { Output = Next(args, ref i) };
                    break;

                default:
                    throw new CliException($"unknown option '{flag}'");
            }
        }

        return options;
    }

    private static string Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length) throw new CliException($"'{args[i]}' needs a value");
        return args[++i];
    }

    private static int ParseInt(string flag, string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new CliException($"'{flag}' needs a whole number, got '{text}'");

    private static ulong ParseULong(string flag, string text) =>
        ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong value)
            ? value
            : throw new CliException($"'{flag}' needs a non-negative whole number, got '{text}'");
}

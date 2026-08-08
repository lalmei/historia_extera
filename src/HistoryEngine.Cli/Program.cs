using System.Globalization;
using HistoryEngine;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Serialization;
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

        if (options.FingerprintOnly)
        {
            // Nothing but the digest on stdout, so the output can be redirected straight into the
            // golden file that GoldenExportTests pins.
            Console.WriteLine(WorldExporter.Fingerprint(HistoryRun.Execute(config).ToExport()));
            return 0;
        }

        Console.WriteLine($"Generating {config.Years} years, seed {config.Seed}, config {config.ConfigHash}");

        HistoryRun run = HistoryRun.Execute(config);
        WorldExport export = run.ToExport();

        string json = WorldExporter.ToJson(export, options.Pretty);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(options.Output));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(options.Output, json);

        PrintSummary(run, export, json.Length, options.Output);

        if (options.SampleEvents > 0) PrintSampleEvents(run, options.SampleEvents);

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
                    options = options with { WorldSize = ParseInt(flag, Next(args, ref i)) };
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

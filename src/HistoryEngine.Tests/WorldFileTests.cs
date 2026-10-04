using HistoryEngine.Serialization;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// Compression is a property of the file on disk, never of the document it holds.
/// </summary>
public sealed class WorldFileTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateTempSubdirectory("historia-worldfile-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void GzippedWorldReadsBackAsTheCanonicalDocumentAndKeepsItsFingerprint()
    {
        WorldExport export = HistoryRun.Execute(TestWorlds.Small()).ToExport();
        string json = WorldExporter.ToJson(export);

        string plain = Path.Combine(_directory, "world.json");
        string packed = Path.Combine(_directory, "world.json.gz");
        long plainBytes = WorldFile.Write(plain, json);
        long packedBytes = WorldFile.Write(packed, json);

        Assert.Equal(json, WorldFile.Read(plain));
        Assert.Equal(json, WorldFile.Read(packed));
        Assert.Equal(
            WorldExporter.Fingerprint(export),
            WorldExporter.Fingerprint(WorldExporter.FromJson(WorldFile.Read(packed))));

        // Not a ratio to tune against — only proof the .gz branch compressed at all.
        Assert.True(packedBytes * 4 < plainBytes, $"{packedBytes} gzipped against {plainBytes} plain");
    }

    [Fact]
    public void ReadingTrustsTheBytesNotTheName()
    {
        string json = """{"schemaVersion":1}""";
        string packed = Path.Combine(_directory, "renamed.gz");
        WorldFile.Write(packed, json);

        string misnamed = Path.Combine(_directory, "renamed.json");
        File.Move(packed, misnamed);

        Assert.Equal(json, WorldFile.Read(misnamed));
    }
}

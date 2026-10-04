using System.IO.Compression;
using System.Text;

namespace HistoryEngine.Serialization;

/// <summary>
/// Puts an export on disk and takes it back off, plain or gzipped.
/// </summary>
/// <remarks>
/// <para>A world is the same few hundred strings repeated, so it compresses to about a tenth of its
/// size with gzip — 272 MB of JSON for a millennium and a half becomes 30 MB. That is the
/// difference between a library of saved worlds and a full disk.</para>
///
/// <para><b>The path decides.</b> A file named <c>*.gz</c> is written gzipped; anything else is
/// written as plain JSON, so <c>grep</c> and <c>jq</c> still work on a world asked for as
/// <c>world.json</c>. Reading never trusts the name: it looks for the gzip magic bytes, so a
/// renamed file opens either way.</para>
///
/// <para>gzip rather than zstd because the browser inflates gzip natively, through
/// <c>Content-Encoding</c> or <c>DecompressionStream</c>, and zstd would mean shipping a decoder
/// in the viewer. The bytes on disk are what the dev server serves, so one codec end to end.</para>
///
/// <para>Compression is a property of the file, never of the document:
/// <see cref="WorldExporter.Fingerprint"/> digests the canonical serialisation, and the schema
/// version does not change.</para>
/// </remarks>
public static class WorldFile
{
    /// <summary>The extension that asks for a compressed file.</summary>
    public const string CompressedExtension = ".gz";

    /// <summary>Whether a world written to <paramref name="path"/> is gzipped.</summary>
    public static bool IsCompressed(string path) =>
        path.EndsWith(CompressedExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Writes <paramref name="json"/> to <paramref name="path"/> and returns the bytes written.</summary>
    public static long Write(string path, string json)
    {
        if (!IsCompressed(path))
        {
            File.WriteAllText(path, json);
            return new FileInfo(path).Length;
        }

        using (FileStream file = File.Create(path))
        using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
        using (var writer = new StreamWriter(gzip, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.Write(json);
        }

        return new FileInfo(path).Length;
    }

    /// <summary>Reads a world file as JSON text, inflating it when it is gzipped.</summary>
    public static string Read(string path)
    {
        using FileStream file = File.OpenRead(path);
        Stream source = IsGzip(file) ? new GZipStream(file, CompressionMode.Decompress) : file;
        using var reader = new StreamReader(source, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Peeks the two-byte gzip signature and rewinds.</summary>
    private static bool IsGzip(FileStream file)
    {
        Span<byte> magic = stackalloc byte[2];
        int read = file.Read(magic);
        file.Position = 0;
        return read == 2 && magic[0] == 0x1f && magic[1] == 0x8b;
    }
}

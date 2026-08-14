using System.Globalization;
using System.Text;

namespace HistoryEngine.Terrain;

/// <summary>
/// Reads and writes greyscale netpbm rasters — PGM, magic <c>P2</c> and <c>P5</c>.
/// </summary>
/// <remarks>
/// <para><b>Why this format and not PNG.</b> The engine has no NuGet dependencies by design, so
/// that the assembly which eventually loads into Vintage Story cannot conflict with the game or
/// another mod — and the BCL decodes no image format at all. PGM is the one raster format that
/// is a hundred lines to parse: a five-token ASCII header and a plane of big-endian samples.
/// Every tool a map generator's output is likely to pass through already writes it, so
/// "export a heightmap and point the engine at it" stays a one-line <c>convert</c> rather than
/// a dependency decision.</para>
///
/// <para><b>16-bit on write.</b> Eight bits over a 3,300-metre range quantises height to 13-metre
/// steps, which is coarse enough to flatten the coastal gradient that decides where a town goes.
/// Two bytes per sample costs nothing at these resolutions and puts quantisation below anything
/// the simulation can notice. Both depths are accepted on read, because a generator's export is
/// not ours to choose.</para>
/// </remarks>
public static class Netpbm
{
    /// <summary>Full-scale value written by <see cref="Write(Stream, RasterGrid)"/>.</summary>
    public const int WriteMaxValue = 65535;

    public static RasterGrid ReadFile(string path)
    {
        try
        {
            return Read(File.ReadAllBytes(path));
        }
        catch (FormatException ex)
        {
            throw new FormatException($"{path}: {ex.Message}", ex);
        }
    }

    /// <summary>Parses a PGM. Accepts both the ASCII (<c>P2</c>) and binary (<c>P5</c>) encodings.</summary>
    public static RasterGrid Read(byte[] data)
    {
        int cursor = 0;

        string magic = ReadMagic(data, ref cursor);
        bool binary = magic switch
        {
            "P5" => true,
            "P2" => false,
            _ => throw new FormatException(
                $"Expected a greyscale PGM (magic P2 or P5), got '{magic}'. Colour PPM and " +
                "bitmap PBM are not read: a terrain plane is one measurement per point."),
        };

        int width = ReadInteger(data, ref cursor);
        int height = ReadInteger(data, ref cursor);
        int maxValue = ReadInteger(data, ref cursor);

        if (width <= 0 || height <= 0)
        {
            throw new FormatException($"Raster dimensions must be positive, got {width}x{height}.");
        }

        if (maxValue is <= 0 or > 65535)
        {
            throw new FormatException($"Maximum value must be in 1..65535, got {maxValue}.");
        }

        var values = new float[width * height];
        double scale = 1.0 / maxValue;

        if (binary)
        {
            // Exactly one whitespace byte separates the header from the plane.
            cursor++;

            int bytesPerSample = maxValue > 255 ? 2 : 1;
            long needed = (long)values.Length * bytesPerSample;

            if (data.Length - cursor < needed)
            {
                throw new FormatException(
                    $"Truncated raster: a {width}x{height} plane at {bytesPerSample} byte(s) per " +
                    $"sample needs {needed:N0} bytes, {data.Length - cursor:N0} remain.");
            }

            for (int i = 0; i < values.Length; i++)
            {
                // Netpbm is big-endian, regardless of the machine reading it.
                int raw = bytesPerSample == 2
                    ? (data[cursor] << 8) | data[cursor + 1]
                    : data[cursor];

                cursor += bytesPerSample;
                values[i] = (float)(raw * scale);
            }
        }
        else
        {
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = (float)(ReadInteger(data, ref cursor) * scale);
            }
        }

        return new RasterGrid(width, height, values);
    }

    public static void WriteFile(string path, RasterGrid grid)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using FileStream stream = File.Create(path);
        Write(stream, grid);
    }

    /// <summary>Writes a binary 16-bit PGM.</summary>
    public static void Write(Stream stream, RasterGrid grid)
    {
        string header = string.Create(
            CultureInfo.InvariantCulture,
            $"P5\n{grid.Width} {grid.Height}\n{WriteMaxValue}\n");

        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
        stream.Write(headerBytes, 0, headerBytes.Length);

        ReadOnlySpan<float> values = grid.Values;
        var plane = new byte[values.Length * 2];

        for (int i = 0; i < values.Length; i++)
        {
            double normalised = values[i] < 0f ? 0.0 : values[i] > 1f ? 1.0 : values[i];
            int quantised = (int)((normalised * WriteMaxValue) + 0.5);

            plane[i * 2] = (byte)(quantised >> 8);
            plane[(i * 2) + 1] = (byte)(quantised & 0xFF);
        }

        stream.Write(plane, 0, plane.Length);
    }

    private static string ReadMagic(byte[] data, ref int cursor)
    {
        SkipToToken(data, ref cursor);

        int start = cursor;
        while (cursor < data.Length && !IsWhitespace(data[cursor])) cursor++;

        if (cursor == start) throw new FormatException("Empty file: no netpbm magic number.");

        return Encoding.ASCII.GetString(data, start, cursor - start);
    }

    private static int ReadInteger(byte[] data, ref int cursor)
    {
        SkipToToken(data, ref cursor);

        if (cursor >= data.Length)
        {
            throw new FormatException("Raster ended while a number was expected.");
        }

        int value = 0;
        int digits = 0;

        while (cursor < data.Length && data[cursor] >= (byte)'0' && data[cursor] <= (byte)'9')
        {
            value = (value * 10) + (data[cursor] - '0');
            cursor++;
            digits++;

            if (value > 0x00FF_FFFF)
            {
                throw new FormatException("Raster header contains an implausibly large number.");
            }
        }

        if (digits == 0)
        {
            throw new FormatException(
                $"Expected a number at byte {cursor}, found '{(char)data[cursor]}'.");
        }

        return value;
    }

    /// <summary>Advances past whitespace and <c>#</c> comments to the next token.</summary>
    private static void SkipToToken(byte[] data, ref int cursor)
    {
        while (cursor < data.Length)
        {
            if (IsWhitespace(data[cursor]))
            {
                cursor++;
            }
            else if (data[cursor] == (byte)'#')
            {
                while (cursor < data.Length && data[cursor] != (byte)'\n') cursor++;
            }
            else
            {
                return;
            }
        }
    }

    private static bool IsWhitespace(byte b) =>
        b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or 0x0B or 0x0C;
}

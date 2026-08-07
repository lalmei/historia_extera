using System.Reflection;

namespace HistoryEngine.Naming;

/// <summary>
/// One name family's training data, loaded from an embedded corpus file.
/// </summary>
/// <remarks>
/// <para><b>A family is not a culture.</b> These are phonological palettes, not peoples. No
/// generated world contains "the Norse civilization" — it contains civilizations whose names
/// lean on a blend of one to three of these palettes, then mutate. Keeping the two ideas
/// separate is what stops eight civilizations from reading as a tour of real-world history.</para>
///
/// <para>Corpora are embedded resources rather than loose files, so the assembly that
/// eventually loads into Vintage Story carries its own training data and cannot be shipped
/// half-configured. All eight files together are a few tens of kilobytes.</para>
/// </remarks>
public sealed class NameCorpus
{
    /// <summary>
    /// Every family available for blending.
    /// </summary>
    /// <remarks>
    /// Adding a family means dropping a file in <c>Corpora/</c> and adding its stem here. Eight
    /// families give 8 solo blends, 28 pairs and 56 triples — 92 distinct palettes, ample for the
    /// five to fifteen civilizations a world starts with.
    /// </remarks>
    public static readonly string[] FamilyNames =
    {
        "celtic", "finnic", "hellenic", "latin", "norse", "semitic", "slavic", "turkic",
    };

    private static readonly Dictionary<string, NameCorpus> Cache = new(StringComparer.Ordinal);
    private static readonly object CacheLock = new();

    private NameCorpus(
        string family,
        IReadOnlyList<string> given,
        IReadOnlyList<string> places,
        IReadOnlyList<string> placeSuffixes,
        IReadOnlyList<string> peopleSuffixes)
    {
        Family = family;
        Given = given;
        Places = places;
        PlaceSuffixes = placeSuffixes;
        PeopleSuffixes = peopleSuffixes;
    }

    public string Family { get; }

    public IReadOnlyList<string> Given { get; }

    public IReadOnlyList<string> Places { get; }

    /// <summary>Place-forming suffixes, hyphen stripped.</summary>
    public IReadOnlyList<string> PlaceSuffixes { get; }

    /// <summary>Ethnonym-forming suffixes, hyphen stripped.</summary>
    public IReadOnlyList<string> PeopleSuffixes { get; }

    /// <summary>
    /// Loads a family, memoised for the process.
    /// </summary>
    /// <remarks>
    /// The cache is keyed on family name and its contents never vary, so memoising it cannot
    /// affect determinism — the parsed result is a pure function of a file baked into the
    /// assembly. Locking is only for the case of parallel test execution.
    /// </remarks>
    public static NameCorpus Load(string family)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(family, out NameCorpus? cached)) return cached;

            NameCorpus corpus = Parse(family, ReadResource(family));
            Cache[family] = corpus;
            return corpus;
        }
    }

    public static IReadOnlyList<NameCorpus> LoadAll()
    {
        var all = new List<NameCorpus>(FamilyNames.Length);
        foreach (string family in FamilyNames) all.Add(Load(family));
        return all;
    }

    private static string ReadResource(string family)
    {
        Assembly assembly = typeof(NameCorpus).Assembly;
        string resource = $"HistoryEngine.Naming.Corpora.{family}.txt";

        using Stream? stream = assembly.GetManifestResourceStream(resource);
        if (stream is null)
        {
            string available = string.Join(", ", assembly.GetManifestResourceNames());
            throw new InvalidOperationException(
                $"Corpus resource '{resource}' is missing. Available: {available}");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Parses corpus text. Public so malformed-input handling can be tested without a resource.
    /// </summary>
    public static NameCorpus Parse(string family, string text)
    {
        var given = new List<string>();
        var places = new List<string>();
        var placeSuffixes = new List<string>();
        var peopleSuffixes = new List<string>();

        List<string>? section = null;

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();

            if (line.Length == 0 || line[0] == '#') continue;

            if (line[0] == '[')
            {
                section = line switch
                {
                    "[given]" => given,
                    "[place]" => places,
                    "[placesuffix]" => placeSuffixes,
                    "[peoplesuffix]" => peopleSuffixes,
                    _ => throw new InvalidOperationException(
                        $"Corpus '{family}' has an unknown section '{line}'."),
                };
                continue;
            }

            // A suffix line leads with a hyphen; strip it so callers concatenate directly.
            section?.Add(line[0] == '-' ? line.Substring(1) : line);
        }

        if (given.Count == 0 || places.Count == 0)
        {
            throw new InvalidOperationException(
                $"Corpus '{family}' must supply both [given] and [place] names.");
        }

        return new NameCorpus(family, given, places, placeSuffixes, peopleSuffixes);
    }
}

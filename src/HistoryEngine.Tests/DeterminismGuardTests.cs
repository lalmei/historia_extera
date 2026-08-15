using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// Scans engine source for constructs that are deterministic-looking but are not.
/// </summary>
/// <remarks>
/// <para>A source scan is a blunt instrument, chosen deliberately. The failures it guards against
/// are invisible in behaviour: a run using <c>Math.Exp</c> or <c>DateTime.UtcNow</c> passes every
/// functional test, produces a perfectly plausible history, and reproduces correctly on the
/// machine it was written on. The divergence only shows up on a different runtime, a different
/// platform, or a different locale — which is to say, on someone else's machine, months later,
/// reported as "the same seed gives me a different world".</para>
///
/// <para>Each rule has an escape hatch: a trailing <c>// det:ok</c> comment on the offending line.
/// The point is not to forbid these constructs forever but to make using one a deliberate,
/// annotated decision rather than an accident.</para>
/// </remarks>
public sealed class DeterminismGuardTests
{
    /// <summary>Explicit opt-out marker. Checked against the original text, before comments are stripped.</summary>
    private const string Waiver = "det:ok";

    /// <summary>
    /// Functions with no cross-runtime bit-exactness guarantee.
    /// </summary>
    /// <remarks>
    /// IEEE 754 requires correct rounding for <c>+ - * /</c> and <c>sqrt</c> only. The
    /// transcendentals may legitimately differ by an ULP between runtimes and hardware, and one
    /// ULP next to a <c>Chance(score)</c> comparison forks the entire history from that year on.
    /// This engine compiles for net7.0 and runs its tests on net10.0, so the risk is live.
    /// </remarks>
    private static readonly string[] NonReproducibleMath =
    {
        "Math.Sin", "Math.Cos", "Math.Tan",
        "Math.Asin", "Math.Acos", "Math.Atan", "Math.Atan2",
        "Math.Pow", "Math.Exp", "Math.Log", "Math.Log2", "Math.Log10",
        "Math.Cbrt", "Math.Sinh", "Math.Cosh", "Math.Tanh",
        "MathF.",
    };

    /// <summary>Sources of per-process or per-machine variation.</summary>
    private static readonly string[] AmbientState =
    {
        "DateTime.Now", "DateTime.UtcNow", "DateTimeOffset.Now", "DateTimeOffset.UtcNow",
        "Guid.NewGuid",
        "new Random(", "Random.Shared",
        "Environment.TickCount",
        ".GetHashCode()",
        "Parallel.For", "Task.Run", ".AsParallel()",
    };

    /// <summary>
    /// Culture-sensitive comparisons and conversions.
    /// </summary>
    /// <remarks>
    /// <c>Comparer&lt;string&gt;.Default</c> and a bare <c>string.CompareTo</c> order differently
    /// under different locales, so a sorted structure keyed by string would change the export's
    /// byte layout depending on the machine's culture. Passes on an invariant-culture machine and
    /// on CI; fails on somebody's laptop.
    /// </remarks>
    private static readonly string[] CultureSensitive =
    {
        "ToLower()", "ToUpper()",
        "Comparer<string>.Default",
        "StringComparison.CurrentCulture",
    };

    [Fact]
    public void EngineAvoidsNonReproducibleMath() => AssertAbsent(NonReproducibleMath);

    [Fact]
    public void EngineAvoidsAmbientState() => AssertAbsent(AmbientState);

    [Fact]
    public void EngineAvoidsCultureSensitiveOperations() => AssertAbsent(CultureSensitive);

    /// <summary>
    /// Systems, and the queue they schedule through, must not hold a
    /// <see cref="Dictionary{TKey,TValue}"/>.
    /// </summary>
    /// <remarks>
    /// Dictionary enumeration order depends on insertion history, capacity and collisions. It is
    /// usually stable within a process, which is what makes it dangerous — a simulation built on
    /// <c>foreach (var kv in dictionary)</c> passes its determinism tests until an unrelated change
    /// alters an insertion sequence, and then produces a different history for reasons nobody can
    /// trace. <c>DetMap</c> and <c>EntityTable</c> are the ordered alternatives.
    ///
    /// <para>Scoped to <c>Systems/</c> and to <c>World/Docket.cs</c> because elsewhere a dictionary
    /// is sometimes the right tool used correctly: <c>TerrainAtlas</c> keeps one as a pure cache it
    /// never iterates, and <c>WorldExporter</c> builds one then sorts it into a
    /// <c>SortedDictionary</c> with an explicit ordinal comparer. The docket is in scope because it
    /// decides what runs next, which makes its iteration order a simulation decision wherever it
    /// physically lives.</para>
    /// </remarks>
    [Fact]
    public void SystemsUseOrderedCollections()
    {
        string systems = Path.Combine(EngineSource.Root, "Systems");
        var offenders = new List<string>();

        var scanned = new List<string>(Directory.GetFiles(systems, "*.cs"))
        {
            Path.Combine(EngineSource.Root, "World", "Docket.cs"),
        };

        foreach (string file in scanned)
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(Waiver, StringComparison.Ordinal)) continue;

                string code = EngineSource.StripComments(lines[i]);
                if (code.Contains("Dictionary<", StringComparison.Ordinal) ||
                    code.Contains("HashSet<", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Systems must use ordered collections (DetMap, EntityTable, List) so iteration " +
            "order cannot drift. Offending lines: " + string.Join(", ", offenders));
    }

    private static void AssertAbsent(IReadOnlyList<string> patterns)
    {
        var offenders = new List<string>();

        foreach (string file in EngineSource.AllFiles())
        {
            string[] lines = File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                // The waiver is checked against the raw line, since it lives in a comment.
                if (lines[i].Contains(Waiver, StringComparison.Ordinal)) continue;

                string code = EngineSource.StripComments(lines[i]);

                foreach (string pattern in patterns)
                {
                    if (code.Contains(pattern, StringComparison.Ordinal))
                    {
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1} — {pattern}");
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Found constructs that break reproducibility across runtimes, platforms or locales. " +
            "Use the DetMath equivalent, or annotate the line with '// det:ok' if it genuinely " +
            "cannot affect simulation output.\n  " + string.Join("\n  ", offenders));
    }
}

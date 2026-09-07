using System.Globalization;
using HistoryEngine.Biography.Evidence;
using HistoryEngine.Biography.Selection;

namespace HistoryEngine.Biography.Interpretation;

/// <summary>
/// Rounded semantic statements for parity with the viewer's TypeScript twin.
/// </summary>
public static class BiographySignatures
{
    public static string Of(BiographyInterpretation interpretation)
    {
        string dials = string.Join(",",
            interpretation.Dials
                .OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
                .Select(pair =>
                    $"{pair.Key}:{pair.Value.ToString(CultureInfo.InvariantCulture)}"));
        string evidence = string.Join("+",
            interpretation.Evidence
                .Select(e => e.Kind.ToString())
                .OrderBy(kind => kind, StringComparer.Ordinal));
        return $"{interpretation.Theme}|{dials}|{interpretation.Outcome}|{evidence}|"
            + interpretation.Score.ToString("F3", CultureInfo.InvariantCulture);
    }

    public static IReadOnlyList<string> For(BiographyContext context, int maxCount = 3)
    {
        IReadOnlyList<BiographyEvidence> evidence = EvidenceExtractor.Extract(context);
        IReadOnlyList<BiographyInterpretation> selected = InterpretationSelector.Select(
            InterpretationEngine.Evaluate(context, evidence, InclinationRules.All),
            maxCount);
        return selected.Select(Of).ToList();
    }
}

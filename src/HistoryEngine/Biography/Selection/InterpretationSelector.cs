namespace HistoryEngine.Biography.Selection;

using HistoryEngine.Biography.Interpretation;

/// <summary>Selects diverse biography interpretations from scored candidates.</summary>
public static class InterpretationSelector
{
    public static IReadOnlyList<BiographyInterpretation> Select(
        IReadOnlyList<BiographyInterpretation> candidates,
        int maxCount)
    {
        if (maxCount <= 0 || candidates.Count == 0) return Array.Empty<BiographyInterpretation>();

        var selected = new List<BiographyInterpretation>();
        var usedGroups = new HashSet<BiographyThemeGroup>();

        foreach (BiographyInterpretation candidate in candidates.OrderByDescending(c => c.Score))
        {
            BiographyThemeGroup group = BiographyThemeGroups.Of(candidate.Theme);
            if (usedGroups.Contains(group)) continue;
            if (selected.Any(existing => AreTooSimilar(existing.Theme, candidate.Theme))) continue;

            selected.Add(candidate);
            usedGroups.Add(group);
            if (selected.Count >= maxCount) break;
        }

        return selected;
    }

    private static bool AreTooSimilar(BiographyTheme a, BiographyTheme b)
    {
        if (a == b) return true;
        if (BiographyThemeGroups.Of(a) == BiographyThemeGroups.Of(b)) return true;

        return (a, b) switch
        {
            (BiographyTheme.Rootedness, BiographyTheme.Isolation) => true,
            (BiographyTheme.Isolation, BiographyTheme.Rootedness) => true,
            (BiographyTheme.CommercialSuccess, BiographyTheme.CommercialFailure) => true,
            (BiographyTheme.CommercialFailure, BiographyTheme.CommercialSuccess) => true,
            (BiographyTheme.ConsolidationOfPower, BiographyTheme.InstitutionalService) => true,
            (BiographyTheme.InstitutionalService, BiographyTheme.ConsolidationOfPower) => true,
            _ => false,
        };
    }
}

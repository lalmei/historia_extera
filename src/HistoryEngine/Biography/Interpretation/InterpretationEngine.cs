using HistoryEngine.Biography.Evidence;
using HistoryEngine.Biography.Interpretation;
using HistoryEngine.Core;
using HistoryEngine.Entities;

namespace HistoryEngine.Biography.Interpretation;

/// <summary>Scores biography rules against extracted evidence and disposition dials.</summary>
public static class InterpretationEngine
{
    public static IReadOnlyList<BiographyInterpretation> Evaluate(
        BiographyContext context,
        IReadOnlyList<BiographyEvidence> evidence,
        IReadOnlyList<BiographyRule> rules)
    {
        var candidates = new List<BiographyInterpretation>();

        foreach (BiographyRule rule in rules)
        {
            double score = ScoreRule(rule, context, evidence);
            if (score <= 0) continue;

            IReadOnlyList<BiographyEvidence> matched = MatchingEvidence(rule, evidence);
        BiographyOutcome outcome = rule.OutcomeResolver?.Invoke(context, matched)
            ?? DefaultOutcome(rule.Theme, matched);

            candidates.Add(new BiographyInterpretation(
                rule.Theme,
                rule.Dials,
                score,
                matched,
                outcome));
        }

        return candidates;
    }

    internal static double ScoreRule(
        BiographyRule rule,
        BiographyContext context,
        IReadOnlyList<BiographyEvidence> evidence)
    {
        double dialProduct = 1.0;
        foreach ((BiographyDial dial, double min) in rule.Dials)
        {
            double value = context.Dial(dial);
            if (value < min) return 0;
            dialProduct *= value;
        }

        if (!HasRequired(rule, evidence)) return 0;

        IReadOnlyList<BiographyEvidence> matching = MatchingEvidence(rule, evidence);
        double evidenceStrength = matching.Count == 0
            ? 0
            : matching.Average(e => e.Strength);

        double durationFactor = DurationFactor(matching, context.Year);
        double recencyFactor = RecencyFactor(matching, context.Year);
        double distinctiveness = DistinctivenessFactor(rule.Theme, matching);

        double score = rule.BaseWeight
            * dialProduct
            * (0.5 + (0.5 * evidenceStrength))
            * durationFactor
            * recencyFactor
            * distinctiveness;

        if (rule.ScoreModifier is not null)
            score *= rule.ScoreModifier(context, matching);

        return score;
    }

    private static bool HasRequired(BiographyRule rule, IReadOnlyList<BiographyEvidence> evidence)
    {
        if (rule.RequiredEvidence is null) return true;
        foreach (EvidenceKind required in rule.RequiredEvidence)
        {
            if (!evidence.Any(e => e.Kind == required)) return false;
        }

        return true;
    }

    private static IReadOnlyList<BiographyEvidence> MatchingEvidence(
        BiographyRule rule,
        IReadOnlyList<BiographyEvidence> evidence)
    {
        var kinds = new HashSet<EvidenceKind>(rule.RequiredEvidence ?? Array.Empty<EvidenceKind>());
        if (rule.OptionalEvidence is not null)
            kinds.UnionWith(rule.OptionalEvidence);
        return evidence.Where(e => kinds.Contains(e.Kind)).ToList();
    }

    private static double DurationFactor(IReadOnlyList<BiographyEvidence> evidence, int year)
    {
        if (evidence.Count == 0) return 1.0;
        double avgSpan = evidence.Average(e => Math.Max(1, e.EndYear - e.StartYear));
        return DetMath.Clamp01(0.6 + (avgSpan / 50.0));
    }

    private static double RecencyFactor(IReadOnlyList<BiographyEvidence> evidence, int year)
    {
        if (evidence.Count == 0) return 1.0;
        double avgEnd = evidence.Average(e => e.EndYear);
        double yearsAgo = Math.Max(0, year - avgEnd);
        return DetMath.Clamp01(1.0 - (yearsAgo / 60.0));
    }

    private static double DistinctivenessFactor(
        BiographyTheme theme,
        IReadOnlyList<BiographyEvidence> evidence)
    {
        if (evidence.Count == 0) return 1.0;
        double peak = evidence.Max(e => e.Strength);
        return DetMath.Clamp01(0.7 + (peak * 0.3));
    }

    private static BiographyOutcome DefaultOutcome(
        BiographyTheme theme,
        IReadOnlyList<BiographyEvidence> evidence)
    {
        if (theme == BiographyTheme.CommercialSuccess
            && evidence.Any(e => e.Kind == EvidenceKind.TradeSuccess)
            && !evidence.Any(e => e.Kind == EvidenceKind.TradeFailure))
        {
            return BiographyOutcome.Positive;
        }

        if (theme == BiographyTheme.CommercialFailure
            && evidence.Any(e => e.Kind == EvidenceKind.TradeFailure)
            && !evidence.Any(e => e.Kind == EvidenceKind.TradeSuccess))
        {
            return BiographyOutcome.Negative;
        }

        return BiographyOutcome.Neutral;
    }
}

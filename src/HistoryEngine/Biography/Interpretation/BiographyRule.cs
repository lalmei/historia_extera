using HistoryEngine.Biography.Evidence;

namespace HistoryEngine.Biography.Interpretation;

/// <summary>One declarative rule mapping evidence and dials to a biography theme.</summary>
public sealed record BiographyRule(
    IReadOnlyDictionary<BiographyDial, double> Dials,
    BiographyTheme Theme,
    EvidenceKind[] RequiredEvidence,
    EvidenceKind[]? OptionalEvidence,
    double BaseWeight,
    Func<BiographyContext, IReadOnlyList<BiographyEvidence>, BiographyOutcome>? OutcomeResolver = null,
    Func<BiographyContext, IReadOnlyList<BiographyEvidence>, double>? ScoreModifier = null);

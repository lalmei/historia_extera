using HistoryEngine.Biography.Evidence;

namespace HistoryEngine.Biography.Interpretation;

/// <summary>A scored semantic statement about a life, before prose rendering.</summary>
public sealed record BiographyInterpretation(
    BiographyTheme Theme,
    IReadOnlyDictionary<BiographyDial, double> Dials,
    double Score,
    IReadOnlyList<BiographyEvidence> Evidence,
    BiographyOutcome Outcome);

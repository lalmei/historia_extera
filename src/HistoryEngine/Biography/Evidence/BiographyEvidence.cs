using HistoryEngine.Core;

namespace HistoryEngine.Biography.Evidence;

/// <summary>One grounded fact about a life, suitable for rule matching and rendering.</summary>
public sealed record BiographyEvidence(
    EvidenceKind Kind,
    double Strength,
    int StartYear,
    int EndYear,
    EntityId RelatedFigureId = default,
    EntityId PlaceId = default,
    string? Tag = null);

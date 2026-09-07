using HistoryEngine.Biography.Evidence;
using HistoryEngine.Biography.Interpretation;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.Biography.Interpretation;

/// <summary>Declarative biography rules keyed to disposition dials and life evidence.</summary>
public static class InclinationRules
{
    public static IReadOnlyList<BiographyRule> All { get; } = Build();

    private static readonly EvidenceKind[] None = Array.Empty<EvidenceKind>();

    private static IReadOnlyList<BiographyRule> Build()
    {
        var rules = new List<BiographyRule>
        {
            Rule(
                BiographyTheme.Rootedness,
                BiographyDial.Tradition,
                0.60,
                new[] { EvidenceKind.LongResidence },
                new[] { EvidenceKind.LongOccupation, EvidenceKind.Friendship },
                1.0),

            Rule(
                BiographyTheme.EnduringLoyalty,
                BiographyDial.Tradition,
                0.65,
                new[] { EvidenceKind.Friendship },
                None,
                0.8),

            Rule(
                BiographyTheme.TransmissionOfKnowledge,
                BiographyDial.Learning,
                0.60,
                new[] { EvidenceKind.Mentorship },
                new[] { EvidenceKind.LongOccupation, EvidenceKind.Study },
                1.1),

            Rule(
                BiographyTheme.ViolentConflict,
                BiographyDial.Aggression,
                0.55,
                new[] { EvidenceKind.Feud },
                new[] { EvidenceKind.Battle, EvidenceKind.Killing },
                1.0),

            Rule(
                BiographyTheme.ScholarlyLife,
                BiographyDial.Learning,
                0.60,
                new[] { EvidenceKind.Study },
                new[] { EvidenceKind.LongOccupation },
                0.95),

            Rule(
                BiographyTheme.ReligiousDevotion,
                BiographyDial.Piety,
                0.60,
                new[] { EvidenceKind.ReligiousOffice },
                new[] { EvidenceKind.Pilgrimage },
                1.0),

            Rule(
                BiographyTheme.CommercialSuccess,
                BiographyDial.Mercantile,
                0.55,
                new[] { EvidenceKind.TradeSuccess },
                new[] { EvidenceKind.TradeJourney },
                1.0,
                CommercialOutcome),

            Rule(
                BiographyTheme.CommercialFailure,
                BiographyDial.Mercantile,
                0.55,
                new[] { EvidenceKind.TradeFailure },
                new[] { EvidenceKind.TradeJourney },
                0.9,
                CommercialOutcome),

            Rule(
                BiographyTheme.ConsolidationOfPower,
                BiographyDial.Centralism,
                0.60,
                new[] { EvidenceKind.HeldOffice },
                None,
                1.0),

            Rule(
                BiographyTheme.ResistanceToAuthority,
                BiographyDial.Independence,
                0.60,
                new[] { EvidenceKind.RejectedOffice },
                new[] { EvidenceKind.Revolt },
                1.0),

            Rule(
                BiographyTheme.InstitutionalService,
                BiographyDial.Centralism,
                0.55,
                new[] { EvidenceKind.HeldOffice },
                new[] { EvidenceKind.LongOccupation },
                0.85),

            Rule(
                BiographyTheme.FrontierLife,
                BiographyDial.Independence,
                0.55,
                new[] { EvidenceKind.Migration },
                new[] { EvidenceKind.LongResidence },
                0.9),

            Rule(
                BiographyTheme.TerritorialAmbition,
                BiographyDial.Expansionism,
                0.60,
                new[] { EvidenceKind.Battle },
                new[] { EvidenceKind.HeldOffice },
                0.85),

            Rule(
                BiographyTheme.Isolation,
                BiographyDial.Independence,
                0.65,
                new[] { EvidenceKind.Migration },
                None,
                0.75),

            new BiographyRule(
                new Dictionary<BiographyDial, double>
                {
                    [BiographyDial.Learning] = 0.60,
                    [BiographyDial.Piety] = 0.60,
                },
                BiographyTheme.ReligiousScholarship,
                new[] { EvidenceKind.ReligiousOffice, EvidenceKind.Study },
                new[] { EvidenceKind.Mentorship },
                1.25),

            Rule(
                BiographyTheme.Betrayal,
                BiographyDial.Tradition,
                0.55,
                new[] { EvidenceKind.Betrayal },
                new[] { EvidenceKind.Friendship },
                1.0,
                (_, _) => BiographyOutcome.Negative),
        };

        return rules;
    }

    private static BiographyRule Rule(
        BiographyTheme theme,
        BiographyDial dial,
        double min,
        EvidenceKind[] required,
        EvidenceKind[] optional,
        double weight,
        Func<BiographyContext, IReadOnlyList<BiographyEvidence>, BiographyOutcome>? outcome = null)
        => new(
            new Dictionary<BiographyDial, double> { [dial] = min },
            theme,
            required,
            optional,
            weight,
            outcome);

    private static BiographyOutcome CommercialOutcome(
        BiographyContext ctx,
        IReadOnlyList<BiographyEvidence> evidence)
    {
        bool gain = evidence.Any(e => e.Kind == EvidenceKind.TradeSuccess);
        bool loss = evidence.Any(e => e.Kind == EvidenceKind.TradeFailure);
        if (gain && !loss) return BiographyOutcome.Positive;
        if (loss && !gain) return BiographyOutcome.Negative;
        if (gain && loss) return BiographyOutcome.Mixed;
        return BiographyOutcome.Neutral;
    }
}

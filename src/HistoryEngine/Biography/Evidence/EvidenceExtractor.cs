using HistoryEngine.Biography.Evidence;
using HistoryEngine.Biography.Interpretation;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.Biography.Evidence;

/// <summary>Converts figure state at a year into biography-friendly evidence records.</summary>
public static class EvidenceExtractor
{
    private const int LongSpanYears = 20;
    private const int FriendshipMinYears = 10;

    public static IReadOnlyList<BiographyEvidence> Extract(BiographyContext context)
    {
        Figure figure = context.Figure;
        int year = context.Year;
        var evidence = new List<BiographyEvidence>();

        ExtractResidences(figure, year, evidence);
        ExtractOccupation(context, evidence);
        ExtractAffinities(figure, year, evidence);
        ExtractBetrayals(figure, year, evidence);
        ExtractMentorships(figure, year, evidence);
        ExtractJourneys(figure, year, evidence);
        ExtractOffices(figure, year, evidence);
        ExtractDisputes(figure, year, evidence);
        ExtractCampaigns(figure, year, evidence);
        ExtractUndertakings(figure, year, evidence);
        ExtractPlots(figure, year, evidence);
        ExtractStudy(figure, year, evidence);
        ExtractDiscovery(figure, year, evidence);

        return evidence;
    }

    private static void ExtractResidences(Figure figure, int year, List<BiographyEvidence> evidence)
    {
        if (figure.Residences.Count == 0) return;

        Residence? longest = null;
        int longestSpan = 0;

        for (int i = 0; i < figure.Residences.Count; i++)
        {
            Residence residence = figure.Residences[i];
            if (residence.FromYear > year) continue;

            int endYear = i + 1 < figure.Residences.Count
                ? Math.Min(figure.Residences[i + 1].FromYear, year)
                : year;
            int span = endYear - residence.FromYear;
            if (span > longestSpan)
            {
                longestSpan = span;
                longest = residence;
            }

            if (residence.Reason is ResidenceReason.Flight or ResidenceReason.Settled
                && residence.FromYear <= year)
            {
                evidence.Add(new BiographyEvidence(
                    EvidenceKind.Migration,
                    DetMath.Clamp01(span / 40.0),
                    residence.FromYear,
                    endYear,
                    PlaceId: residence.SettlementId,
                    Tag: residence.Reason.ToString()));
            }
        }

        if (longest is not null && longestSpan >= LongSpanYears)
        {
            int end = year;
            for (int i = 0; i < figure.Residences.Count; i++)
            {
                if (figure.Residences[i].SettlementId != longest.SettlementId) continue;
                if (figure.Residences[i].FromYear > year) break;
                end = i + 1 < figure.Residences.Count
                    ? Math.Min(figure.Residences[i + 1].FromYear, year)
                    : year;
            }

            evidence.Add(new BiographyEvidence(
                EvidenceKind.LongResidence,
                DetMath.Clamp01(longestSpan / 40.0),
                longest.FromYear,
                end,
                PlaceId: longest.SettlementId));
        }
    }

    private static void ExtractOccupation(BiographyContext context, List<BiographyEvidence> evidence)
    {
        Figure figure = context.Figure;
        int year = context.Year;
        if (figure.Occupation == Occupation.None) return;

        int startYear = OccupationStartYear(context);
        int span = year - startYear;
        if (span >= LongSpanYears)
        {
            evidence.Add(new BiographyEvidence(
                EvidenceKind.LongOccupation,
                DetMath.Clamp01(span / 40.0),
                startYear,
                year,
                Tag: figure.Occupation.ToString()));
        }

        if (figure.Occupation == Occupation.Clergy
            || figure.Offices.Any(o => o.Kind == OfficeKind.HighPriest && o.FromYear <= year
                && (o.ToYear is null || o.ToYear >= year)))
        {
            OfficeHolding? priest = figure.Offices
                .LastOrDefault(o => o.Kind == OfficeKind.HighPriest && o.FromYear <= year);
            int from = priest?.FromYear ?? startYear;
            evidence.Add(new BiographyEvidence(
                EvidenceKind.ReligiousOffice,
                0.75,
                from,
                year,
                PlaceId: priest?.ScopeId ?? EntityId.None));
        }
    }

    private static void ExtractAffinities(Figure figure, int year, List<BiographyEvidence> evidence)
    {
        foreach (FigureAffinity affinity in figure.Affinities)
        {
            if (affinity.StartYear > year) continue;

            int endYear = affinity.EndYear is int end && end <= year ? end : year;
            int span = endYear - affinity.StartYear;

            AffinityStage stageAtYear = AffinityStage.Acquaintance;
            foreach (AffinityAct act in affinity.Acts)
            {
                if (act.Year <= year && act.Stage > stageAtYear)
                    stageAtYear = act.Stage;
            }

            if (stageAtYear >= AffinityStage.Friendship && span >= FriendshipMinYears)
            {
                evidence.Add(new BiographyEvidence(
                    EvidenceKind.Friendship,
                    DetMath.Clamp01(span / 40.0),
                    affinity.StartYear,
                    endYear,
                    RelatedFigureId: affinity.Other(figure.Id),
                    PlaceId: affinity.PlaceId));
            }
        }
    }

    private static void ExtractBetrayals(Figure figure, int year, List<BiographyEvidence> evidence)
    {
        foreach (FigureBetrayal betrayal in figure.Betrayals)
        {
            if (betrayal.Year > year) continue;
            EntityId other = betrayal.BetrayerId == figure.Id
                ? betrayal.BetrayedId
                : betrayal.BetrayerId;
            evidence.Add(new BiographyEvidence(
                EvidenceKind.Betrayal,
                0.85,
                betrayal.Year,
                betrayal.Year,
                RelatedFigureId: other,
                PlaceId: betrayal.PlaceId,
                Tag: betrayal.BetrayerId == figure.Id ? "Turned" : "Suffered"));
        }

        foreach (FigureAffinity affinity in figure.Affinities)
        {
            if (affinity.Outcome != AffinityOutcome.Betrayed) continue;
            if (affinity.EndYear is not int end || end > year) continue;
            evidence.Add(new BiographyEvidence(
                EvidenceKind.Betrayal,
                0.80,
                end,
                end,
                RelatedFigureId: affinity.Other(figure.Id),
                PlaceId: affinity.PlaceId,
                Tag: affinity.BetrayerId == figure.Id ? "Turned" : "Suffered"));
        }
    }

    private static void ExtractMentorships(Figure figure, int year, List<BiographyEvidence> evidence)
    {
        foreach (FigureMentorship mentorship in figure.Mentorships)
        {
            if (mentorship.StartYear > year) continue;
            EntityId other = mentorship.MentorId == figure.Id
                ? mentorship.ApprenticeId
                : mentorship.MentorId;
            evidence.Add(new BiographyEvidence(
                EvidenceKind.Mentorship,
                DetMath.Clamp01((year - mentorship.StartYear + 10) / 40.0),
                mentorship.StartYear,
                year,
                RelatedFigureId: other,
                PlaceId: mentorship.LocationId,
                Tag: mentorship.CareerFamily.ToString()));
        }
    }

    private static void ExtractJourneys(Figure figure, int year, List<BiographyEvidence> evidence)
    {
        foreach (Journey journey in figure.Journeys)
        {
            if (journey.Year > year) continue;

            if (journey.Kind == JourneyKind.Pilgrimage)
            {
                evidence.Add(new BiographyEvidence(
                    EvidenceKind.Pilgrimage,
                    0.70,
                    journey.Year,
                    (journey.ReturnYear ?? year) <= year ? journey.ReturnYear ?? year : year,
                    PlaceId: journey.ToSettlementId));
            }

            if (journey.Kind == JourneyKind.Trade)
            {
                evidence.Add(new BiographyEvidence(
                    EvidenceKind.TradeJourney,
                    0.55,
                    journey.Year,
                    (journey.ReturnYear ?? year) <= year ? journey.ReturnYear ?? year : year,
                    PlaceId: journey.ToSettlementId));
            }
        }
    }

    private static void ExtractOffices(Figure figure, int year, List<BiographyEvidence> evidence)
    {
        foreach (OfficeHolding title in figure.Offices)
        {
            if (title.FromYear > year) continue;
            if (title.ToYear is int to && to < year) continue;
            evidence.Add(new BiographyEvidence(
                EvidenceKind.HeldOffice,
                OfficeWeight(title.Kind),
                title.FromYear,
                title.ToYear is int ended && ended <= year ? ended : year,
                PlaceId: title.ScopeId,
                Tag: title.Kind.ToString()));
        }
    }

    private static void ExtractDisputes(Figure figure, int year, List<BiographyEvidence> evidence)
    {
        foreach (FigureDispute dispute in figure.Disputes)
        {
            if (dispute.StartYear > year) continue;

            int endYear = dispute.EndYear is int end && end <= year ? end : year;

            if (dispute.Cause == DisputeCause.PassedOverForOffice
                || dispute.Cause == DisputeCause.SuccessionPassedOver)
            {
                evidence.Add(new BiographyEvidence(
                    EvidenceKind.RejectedOffice,
                    0.75,
                    dispute.StartYear,
                    endYear,
                    RelatedFigureId: dispute.Other(figure.Id),
                    PlaceId: dispute.PlaceId));
            }

            evidence.Add(new BiographyEvidence(
                EvidenceKind.Feud,
                DetMath.Clamp01((endYear - dispute.StartYear + 5) / 30.0),
                dispute.StartYear,
                endYear,
                RelatedFigureId: dispute.Other(figure.Id),
                PlaceId: dispute.PlaceId));

            if (dispute.Outcome == DisputeOutcome.Killed && dispute.EndYear is int killed && killed <= year)
            {
                evidence.Add(new BiographyEvidence(
                    EvidenceKind.Killing,
                    0.90,
                    killed,
                    killed,
                    RelatedFigureId: dispute.Other(figure.Id),
                    PlaceId: dispute.PlaceId));
            }
        }
    }

    private static void ExtractCampaigns(Figure figure, int year, List<BiographyEvidence> evidence)
    {
        foreach (CampaignMemory campaign in figure.Campaigns)
        {
            if (campaign.Year > year) continue;
            evidence.Add(new BiographyEvidence(
                EvidenceKind.Battle,
                0.65,
                campaign.Year,
                campaign.Year,
                PlaceId: campaign.BattleId,
                Tag: campaign.Role.ToString()));
        }
    }

    private static void ExtractUndertakings(Figure figure, int year, List<BiographyEvidence> evidence)
    {
        foreach (FigureUndertaking undertaking in figure.Undertakings)
        {
            if (undertaking.StartYear > year) continue;

            int endYear = undertaking.EndYear is int end && end <= year ? end : year;

            if (undertaking.Kind == UndertakingKind.TradeVenture)
            {
                if (undertaking.State == UndertakingState.Succeeded && undertaking.EndYear is int success
                    && success <= year)
                {
                    evidence.Add(new BiographyEvidence(
                        EvidenceKind.TradeSuccess,
                        0.80,
                        undertaking.StartYear,
                        success,
                        PlaceId: undertaking.DestinationId));
                }

                if (undertaking.State == UndertakingState.Failed && undertaking.EndYear is int failed
                    && failed <= year)
                {
                    evidence.Add(new BiographyEvidence(
                        EvidenceKind.TradeFailure,
                        0.80,
                        undertaking.StartYear,
                        failed,
                        PlaceId: undertaking.DestinationId));
                }
            }

            if (undertaking.Kind == UndertakingKind.Pilgrimage
                && undertaking.State == UndertakingState.Succeeded
                && undertaking.EndYear is int pilgrimageEnd
                && pilgrimageEnd <= year)
            {
                evidence.Add(new BiographyEvidence(
                    EvidenceKind.Pilgrimage,
                    0.85,
                    undertaking.StartYear,
                    pilgrimageEnd,
                    PlaceId: undertaking.DestinationId));
            }
        }
    }

    private static void ExtractPlots(Figure figure, int year, List<BiographyEvidence> evidence)
    {
        foreach (FigurePlot plot in figure.Plots)
        {
            if (plot.StartYear > year) continue;
            if (plot.LeaderId != figure.Id
                && !plot.Members.Any(m => m.FigureId == figure.Id && m.Witting))
            {
                continue;
            }

            int endYear = plot.EndYear is int end && end <= year ? end : year;
            evidence.Add(new BiographyEvidence(
                EvidenceKind.Revolt,
                0.85,
                plot.StartYear,
                endYear,
                RelatedFigureId: plot.TargetId,
                PlaceId: plot.PlaceId,
                Tag: plot.Objective.ToString()));
        }
    }

    private static void ExtractStudy(Figure figure, int year, List<BiographyEvidence> evidence)
    {
        bool scribe = figure.Occupation == Occupation.Scribe;
        bool observations = figure.Observations.Any(o => o.Year <= year);
        bool claims = figure.Claims.Any();

        if (!scribe && !observations && !claims) return;

        int start = figure.BirthYear;
        if (scribe) start = Math.Max(start, figure.BirthYear + 18);
        evidence.Add(new BiographyEvidence(
            EvidenceKind.Study,
            scribe ? 0.75 : 0.60,
            start,
            year,
            Tag: scribe ? "Scribe" : observations ? "Observation" : "Claim"));
    }

    private static void ExtractDiscovery(Figure figure, int year, List<BiographyEvidence> evidence)
    {
        var years = new HashSet<int>();
        foreach (SkyObservation seen in figure.Observations)
        {
            if (seen.Year > year) continue;
            bool first = seen.PriorYear is null;
            bool great = seen.Grade == ApparitionGrade.Great;
            if (!first && !great) continue;

            evidence.Add(new BiographyEvidence(
                EvidenceKind.Discovery,
                great ? 0.85 : 0.70,
                seen.Year,
                seen.Year,
                PlaceId: seen.SettlementId,
                Tag: great ? "Great" : "First"));
            years.Add(seen.Year);
        }

        foreach (SalientMemory memory in figure.Memories)
        {
            if (memory.Kind != MemoryKind.Wonder) continue;
            if (memory.Year > year) continue;
            if (years.Contains(memory.Year)) continue;

            evidence.Add(new BiographyEvidence(
                EvidenceKind.Discovery,
                DetMath.Clamp01(memory.Intensity),
                memory.Year,
                memory.Year,
                PlaceId: memory.LocationId,
                Tag: "Wonder"));
        }
    }

    private static int OccupationStartYear(BiographyContext context)
    {
        int latest = -1;
        foreach (int taken in context.OccupationTakenYears)
        {
            if (taken <= context.Year && taken > latest) latest = taken;
        }

        foreach (SalientMemory memory in context.Figure.Memories)
        {
            if (memory.SourceKind != EventKind.OccupationTaken) continue;
            if (memory.Year <= context.Year && memory.Year > latest) latest = memory.Year;
        }

        return latest >= 0 ? latest : context.Figure.BirthYear;
    }

    private static double OfficeWeight(OfficeKind kind) => kind switch
    {
        OfficeKind.Ruler => 1.0,
        OfficeKind.Regent => 0.9,
        OfficeKind.HighPriest => 0.85,
        OfficeKind.Marshal => 0.8,
        OfficeKind.Governor => 0.75,
        _ => 0.6,
    };
}

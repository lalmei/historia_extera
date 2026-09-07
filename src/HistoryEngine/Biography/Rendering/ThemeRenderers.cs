using HistoryEngine.Biography.Evidence;
using HistoryEngine.Biography.Interpretation;
using HistoryEngine.Core;
using HistoryEngine.Entities;

namespace HistoryEngine.Biography.Rendering;

/// <summary>Theme-keyed sentence builders for biography interpretations.</summary>
internal static class ThemeRenderers
{
    public static string Render(
        BiographyContext context,
        BiographyInterpretation interpretation,
        int variant)
    {
        return interpretation.Theme switch
        {
            BiographyTheme.Rootedness => RenderRootedness(context, interpretation, variant),
            BiographyTheme.EnduringLoyalty => RenderLoyalty(context, interpretation, variant),
            BiographyTheme.TransmissionOfKnowledge => RenderTransmission(context, interpretation, variant),
            BiographyTheme.ViolentConflict => RenderConflict(context, interpretation, variant),
            BiographyTheme.ScholarlyLife => RenderScholarship(context, interpretation, variant),
            BiographyTheme.ReligiousDevotion => RenderFaith(context, interpretation, variant),
            BiographyTheme.ReligiousScholarship => RenderReligiousScholarship(context, interpretation, variant),
            BiographyTheme.CommercialSuccess => RenderCommerce(context, interpretation, variant, true),
            BiographyTheme.CommercialFailure => RenderCommerce(context, interpretation, variant, false),
            BiographyTheme.ConsolidationOfPower => RenderAuthority(context, interpretation, variant, true),
            BiographyTheme.InstitutionalService => RenderAuthority(context, interpretation, variant, false),
            BiographyTheme.ResistanceToAuthority => RenderResistance(context, interpretation, variant),
            BiographyTheme.FrontierLife => RenderFrontier(context, interpretation, variant),
            BiographyTheme.TerritorialAmbition => RenderAmbition(context, interpretation, variant),
            BiographyTheme.Isolation => RenderIsolation(context, interpretation, variant),
            _ => string.Empty,
        };
    }

    private static string RenderRootedness(
        BiographyContext context,
        BiographyInterpretation interpretation,
        int variant)
    {
        BiographyEvidence residence = First(interpretation, EvidenceKind.LongResidence);
        string place = context.PlaceName(residence.PlaceId);
        string name = context.Name;
        string possessive = context.Possessive;

        if (residence.Strength > 0.8)
        {
            return variant switch
            {
                0 => $"{name} spent most of {possessive} life rooted in {place}.",
                1 => $"{name} rarely strayed far from {place}, where the record first found {context.Object}.",
                _ => $"Familiar places and established ways remained important to {name}, above all at {place}.",
            };
        }

        return variant switch
        {
            0 => $"{name} remained closely tied to {place} for much of {possessive} life.",
            1 => $"{name} kept returning to {place} as though {possessive} life had never truly left it.",
            _ => $"The town at {place} shaped {name} more than any office {possessive} years later brought.",
        };
    }

    private static string RenderLoyalty(
        BiographyContext context,
        BiographyInterpretation interpretation,
        int variant)
    {
        BiographyEvidence friendship = First(interpretation, EvidenceKind.Friendship);
        string friend = context.FigureName(friendship.RelatedFigureId);
        int years = friendship.EndYear - friendship.StartYear;
        string name = context.Name;
        string possessive = context.Possessive;

        return variant switch
        {
            0 => $"{name} and {friend} stood by one another for {years} years, a tie the record never forgot.",
            1 => $"Through {years} years, {name} kept faith with {friend} when lesser ties cooled.",
            _ => $"{friend} remained at the centre of {name}'s life for {years} years — loyalty the chronicle could name.",
        };
    }

    private static string RenderTransmission(
        BiographyContext context,
        BiographyInterpretation interpretation,
        int variant)
    {
        BiographyEvidence mentorship = First(interpretation, EvidenceKind.Mentorship);
        bool mentor = mentorship.RelatedFigureId != EntityId.None
            && context.Figure.Mentorships.Any(m =>
                m.MentorId == context.Figure.Id && m.ApprenticeId == mentorship.RelatedFigureId);
        string other = context.FigureName(mentorship.RelatedFigureId);
        string name = context.Name;

        if (mentor)
        {
            return variant switch
            {
                0 => $"{name} passed down what {context.Possessive} trade had taught {context.Object} to {other}.",
                1 => $"Much of what {other} knew came through {name}'s teaching.",
                _ => $"{name} shaped {other}'s path as a mentor the record still names.",
            };
        }

        return variant switch
        {
            0 => $"{name} learned the craft under {other}, and carried it forward.",
            1 => $"What {name} became owed a great deal to {other}'s instruction.",
            _ => $"{other}'s teaching left a lasting mark on {name}'s life.",
        };
    }

    private static string RenderConflict(
        BiographyContext context,
        BiographyInterpretation interpretation,
        int variant)
    {
        BiographyEvidence feud = First(interpretation, EvidenceKind.Feud);
        string rival = context.FigureName(feud.RelatedFigureId);
        string name = context.Name;

        if (interpretation.Evidence.Any(e => e.Kind == EvidenceKind.Killing))
        {
            return variant switch
            {
                0 => $"{name}'s quarrel with {rival} ended in blood the record could not soften.",
                1 => $"Violence between {name} and {rival} closed a feud that had long been gathering.",
                _ => $"{name} and {rival} settled their grievance at the edge of a blade.",
            };
        }

        return variant switch
        {
            0 => $"{name} carried a long quarrel with {rival} through the public life of the record.",
            1 => $"A feud with {rival} shadowed {name}'s years more than any single battle.",
            _ => $"{name} and {rival} remained locked in a dispute the chronicle followed for years.",
        };
    }

    private static string RenderScholarship(
        BiographyContext context,
        BiographyInterpretation interpretation,
        int variant)
    {
        string name = context.Name;
        string possessive = context.Possessive;

        return variant switch
        {
            0 => $"{name} lived among books, observations, and the work of making sense of them.",
            1 => $"Learning ran through {name}'s life — not as ornament, but as habit.",
            _ => $"The record remembers {name} as one who studied, copied, and kept what others let pass.",
        };
    }

    private static string RenderFaith(
        BiographyContext context,
        BiographyInterpretation interpretation,
        int variant)
    {
        string name = context.Name;
        string possessive = context.Possessive;
        bool pilgrimage = interpretation.Evidence.Any(e => e.Kind == EvidenceKind.Pilgrimage);

        if (pilgrimage)
        {
            BiographyEvidence trip = First(interpretation, EvidenceKind.Pilgrimage);
            string place = context.PlaceName(trip.PlaceId);
            return variant switch
            {
                0 => $"Faith led {name} to {place} on pilgrimage, and the journey stayed in the record.",
                1 => $"{name} sought the holy at {place}, and the chronicle kept the road.",
                _ => $"Pilgrimage to {place} marked {name}'s devotion in a way office alone could not.",
            };
        }

        return variant switch
        {
            0 => $"{name} served the faith in office, and the record treated that service as {possessive} life's spine.",
            1 => $"Religious duty shaped {name}'s public years more than trade or arms.",
            _ => $"{name} held the faith's work close through the offices {possessive} life accumulated.",
        };
    }

    private static string RenderReligiousScholarship(
        BiographyContext context,
        BiographyInterpretation interpretation,
        int variant)
    {
        string name = context.Name;
        return variant switch
        {
            0 => $"{name} joined religious office to scholarly habit — doctrine learned and kept.",
            1 => $"The record shows {name} as both servant of the faith and student of its teaching.",
            _ => $"{name} read, served, and copied within the same devout life.",
        };
    }

    private static string RenderCommerce(
        BiographyContext context,
        BiographyInterpretation interpretation,
        int variant,
        bool success)
    {
        string name = context.Name;
        string possessive = context.Possessive;

        if (success)
        {
            return variant switch
            {
                0 => $"Trade brought {name} considerable prosperity.",
                1 => $"{name}'s ventures on the road returned with gain the record could count.",
                _ => $"Mercantile work rewarded {name} more often than it failed {context.Object}.",
            };
        }

        return variant switch
        {
            0 => $"{name} repeatedly sought {possessive} fortune through trade, though little of it endured.",
            1 => $"The road took more from {name}'s ventures than it ever gave back.",
            _ => $"Trade occupied {name} for years, but the record remembers more loss than gain.",
        };
    }

    private static string RenderAuthority(
        BiographyContext context,
        BiographyInterpretation interpretation,
        int variant,
        bool consolidation)
    {
        string name = context.Name;
        string possessive = context.Possessive;

        if (consolidation)
        {
            return variant switch
            {
                0 => $"{name} gathered power into {possessive} own hands rather than leave it scattered.",
                1 => $"Office after office, {name} made authority personal.",
                _ => $"The record shows {name} bending institutions toward a single will.",
            };
        }

        return variant switch
        {
            0 => $"{name} served the institutions of the realm long and faithfully.",
            1 => $"Public office defined {name}'s mature years more than private ambition.",
            _ => $"{name} held posts the chronicle could list and did not hurry to leave them.",
        };
    }

    private static string RenderResistance(
        BiographyContext context,
        BiographyInterpretation interpretation,
        int variant)
    {
        string name = context.Name;
        bool revolt = interpretation.Evidence.Any(e => e.Kind == EvidenceKind.Revolt);

        if (revolt)
        {
            return variant switch
            {
                0 => $"{name} plotted against authority the record later exposed.",
                1 => $"Conspiracy marked {name}'s years when office alone could not satisfy {context.Object}.",
                _ => $"{name} turned from subject to conspirator when power closed its doors.",
            };
        }

        return variant switch
        {
            0 => $"{name} resented the offices passed to others and did not hide it.",
            1 => $"Being passed over left a wound in {name}'s public life the record followed.",
            _ => $"{name} chafed under authority that never quite admitted {context.Object}.",
        };
    }

    private static string RenderFrontier(
        BiographyContext context,
        BiographyInterpretation interpretation,
        int variant)
    {
        BiographyEvidence migration = First(interpretation, EvidenceKind.Migration);
        string place = context.PlaceName(migration.PlaceId);
        string name = context.Name;

        return variant switch
        {
            0 => $"{name} made a life at the edge of the known world, settling at {place}.",
            1 => $"Migration brought {name} to {place}, and there {context.Possessive} story stayed.",
            _ => $"{name} left the old seats behind and rooted {context.Object}self anew at {place}.",
        };
    }

    private static string RenderAmbition(
        BiographyContext context,
        BiographyInterpretation interpretation,
        int variant)
    {
        string name = context.Name;
        return variant switch
        {
            0 => $"{name} pressed outward — in war, office, and the reach of {context.Possessive} ambitions.",
            1 => $"The record remembers {name} on campaign and in command more than at rest.",
            _ => $"Territory and victory mattered to {name} in a way peace alone never could.",
        };
    }

    private static string RenderIsolation(
        BiographyContext context,
        BiographyInterpretation interpretation,
        int variant)
    {
        string name = context.Name;
        return variant switch
        {
            0 => $"{name} kept apart from the centres where others sought favour.",
            1 => $"Distance and independence marked {name}'s path more than courtly tie.",
            _ => $"{name} lived at a remove from the seats that shaped most lives around {context.Object}.",
        };
    }

    private static BiographyEvidence First(
        BiographyInterpretation interpretation,
        EvidenceKind kind) =>
        interpretation.Evidence.First(e => e.Kind == kind);
}

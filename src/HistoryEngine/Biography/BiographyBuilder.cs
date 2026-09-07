using HistoryEngine.Biography.Evidence;
using HistoryEngine.Biography.Interpretation;
using HistoryEngine.Biography.Rendering;
using HistoryEngine.Biography.Selection;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;

namespace HistoryEngine.Biography;

/// <summary>The semantic and linguistic output of the biography pipeline.</summary>
public sealed record FigureBiography(
    EntityId FigureId,
    int Year,
    IReadOnlyList<BiographyEvidence> Evidence,
    IReadOnlyList<BiographyInterpretation> Interpretations,
    string Prose);

/// <summary>Orchestrates evidence extraction, interpretation, selection, and rendering.</summary>
public static class BiographyBuilder
{
    public static FigureBiography Build(
        BiographyContext context,
        IRng? rng = null,
        int maxInterpretations = 3)
    {
        IReadOnlyList<BiographyEvidence> evidence = EvidenceExtractor.Extract(context);
        IReadOnlyList<BiographyInterpretation> candidates =
            InterpretationEngine.Evaluate(context, evidence, InclinationRules.All);
        IReadOnlyList<BiographyInterpretation> selected =
            InterpretationSelector.Select(candidates, maxInterpretations);
        string prose = BiographyRenderer.Render(context, selected, rng);
        return new FigureBiography(
            context.Figure.Id,
            context.Year,
            evidence,
            selected,
            prose);
    }

    public static FigureBiography Build(
        WorldState world,
        Figure figure,
        int year,
        int maxInterpretations = 3)
    {
        int standingYear = BiographyContext.StandingYear(figure, year);
        var takenYears = new List<int>();
        foreach (HistoryEvent ev in world.Chronicle.Events)
        {
            if (ev.Kind == EventKind.OccupationTaken && ev.Subject == figure.Id && ev.Year <= standingYear)
                takenYears.Add(ev.Year);
        }

        var context = new BiographyContext(
            figure,
            standingYear,
            world.NameOf,
            id => world.Figures.Contains(id) ? world.Figures[id].Sex : null,
            takenYears);
        IRng rng = world.Root.Fork("biography", figure.Id.ToDiscriminator());
        return Build(context, rng, maxInterpretations);
    }
}

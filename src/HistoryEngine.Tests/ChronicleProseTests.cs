using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// Reads whole chronicles for the defects a reader notices first, across a seed panel.
/// </summary>
/// <remarks>
/// Every check here was a real line in a real world before it was fixed: a pilgrimage told three
/// times in one year, "after 0 years", "Governor of Ardagheraun at Ardagheraun", "died at the age
/// of 41, of for the death of Drusius", and fifteen hundred deaths "of illness". Template tests
/// cannot catch these, because each template was grammatical — the fault was in what the engine
/// put into it.
/// </remarks>
public sealed class ChronicleProseTests
{
    private static readonly ulong[] Seeds = { 2, 7, 11 };

    private static readonly System.Text.RegularExpressions.Regex ZeroYears = new(@"\b0 years");

    private static readonly List<(WorldState World, List<string> Lines)> Panel = Build();

    private static List<(WorldState, List<string>)> Build()
    {
        var panel = new List<(WorldState, List<string>)>();
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;
            panel.Add((world, world.Chronicle.Events.Select(entry => world.Narrate(entry)).ToList()));
        }

        return panel;
    }

    [Fact]
    public void NoSpanIsGivenAsZeroYears()
    {
        foreach ((_, List<string> lines) in Panel)
        {
            Assert.DoesNotContain(lines, line => ZeroYears.IsMatch(line));
        }
    }

    [Fact]
    public void NoCauseIsAReasonInTheWrongSlot()
    {
        foreach ((_, List<string> lines) in Panel)
        {
            Assert.DoesNotContain(lines, line => line.Contains(", of for ", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void AnOfficeOverATownDoesNotNameTheTownTwice()
    {
        int checkedCount = 0;
        foreach ((WorldState world, _) in Panel)
        {
            foreach (HistoryEvent entry in world.Chronicle.Events)
            {
                if (entry.Kind != EventKind.OfficeGranted || entry.Object != entry.Location) continue;
                if (entry.Location.IsNone) continue;

                checkedCount++;
                string prose = world.Narrate(entry);
                string town = world.NameOf(entry.Location);
                Assert.Equal(
                    prose.IndexOf(town, StringComparison.Ordinal),
                    prose.LastIndexOf(town, StringComparison.Ordinal));
            }
        }

        Assert.True(checkedCount > 0, "No office over a town was granted across the panel.");
    }

    [Fact]
    public void OrdinarySicknessHasAName()
    {
        foreach ((WorldState world, _) in Panel)
        {
            foreach (Figure figure in world.Figures)
            {
                if (figure.DeathCause != DeathCause.Illness) continue;
                Assert.False(string.IsNullOrEmpty(figure.DeathDetail), $"{figure.Id} died of nothing named.");
            }
        }
    }

    /// <summary>
    /// A journey that begins or finishes an errand says so in its own line, and the errand writes
    /// none for the same leg: a one-leg pilgrimage is one line, not three.
    /// </summary>
    [Fact]
    public void AJourneyAndTheErrandItServesAreToldOnce()
    {
        int folded = 0;
        foreach ((WorldState world, _) in Panel)
        {
            foreach (HistoryEvent entry in world.Chronicle.Events)
            {
                if (entry.Kind == EventKind.UndertakingCompleted)
                {
                    Assert.NotEqual("journey", entry.DataValue(Narration.VoiceDataKey));
                }

                if (entry.Kind == EventKind.JourneyMade
                    && entry.DataValue(Narration.VoiceDataKey) is { } voice
                    && voice != "campaign")
                {
                    folded++;
                }
            }

            foreach (Figure figure in world.Figures)
            {
                foreach (FigureUndertaking undertaking in figure.Undertakings)
                {
                    if (undertaking.State != UndertakingState.Succeeded) continue;
                    if (undertaking.Kind == UndertakingKind.Revenge) continue;

                    // The leg that finished it is in the chronicle, and it says it finished it.
                    Assert.Contains(world.Chronicle.Events, entry =>
                        entry.Kind == EventKind.JourneyMade
                        && entry.Subject == figure.Id
                        && entry.Year == undertaking.EndYear
                        && entry.DataValue(Narration.VoiceDataKey) is { } v
                        && (v.EndsWith("close", StringComparison.Ordinal)
                            || v is "pilgrimage" or "trade" or "mission" or "embassy"));
                }
            }
        }

        Assert.True(folded > 0, "No journey line opened or closed an errand across the panel.");
    }

    /// <summary>
    /// The engine and viewer agree on the selection rule, so the viewer can only be as varied as
    /// the table. The kinds a reader meets most should not all be told in one sentence.
    /// </summary>
    [Fact]
    public void TheCommonestLinesAreNotAllTheSameSentence()
    {
        foreach (EventKind kind in new[]
                 {
                     EventKind.JourneyMade, EventKind.FigureDied, EventKind.FigureBorn,
                     EventKind.FigureMoved, EventKind.OccupationTaken, EventKind.OfficeGranted,
                 })
        {
            (WorldState world, _) = Panel[0];
            var openings = new HashSet<string>();
            foreach (HistoryEvent entry in world.Chronicle.Events)
            {
                if (entry.Kind != kind) continue;

                // The first word after the name is where one wording differs from the next.
                string prose = world.Narrate(entry);
                string rest = prose.Substring(Math.Min(prose.Length, world.NameOf(entry.Subject).Length)).TrimStart();
                openings.Add(rest.Split(' ')[0]);
            }

            Assert.True(openings.Count >= 2, $"{kind} is told in only one way: {string.Join(", ", openings)}.");
        }
    }
}

using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;
using Xunit;
using Xunit.Abstractions;

namespace HistoryEngine.Tests;

/// <summary>
/// What a guild mastery must be true of: it is over a trade, the trade is practised in the town
/// that elected it, one person holds it at a time, and a town may hold several at once.
/// </summary>
public sealed class GuildTests
{
    private static readonly ulong[] Panel = { 2, 7, 11, 42, 99 };

    private readonly ITestOutputHelper _out;

    public GuildTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Every mastery names a trade, and the person holding it practises that trade.
    /// </summary>
    /// <remarks>
    /// The acceptance rule — no mastery for a craft nobody in the town practises — asked of the
    /// holder rather than of the town's roster, and deliberately. A company's membership moves:
    /// people die, migrate and are levied, so a mastery granted in 140 to a town with four weavers
    /// in it is not falsified by that town having none in 300. What cannot move is that the master
    /// was himself one of them, which is the strongest form of the claim that survives time.
    /// </remarks>
    [Fact]
    public void EveryMasteryIsOverATradeItsHolderPractises()
    {
        int checked_ = 0;

        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                foreach (OfficeHolding held in figure.Offices)
                {
                    if (held.Kind != OfficeKind.GuildMaster) continue;

                    checked_++;
                    Assert.NotEqual(Craft.None, held.Craft);
                    Assert.Equal(figure.Craft, held.Craft);
                    Assert.True(
                        world.Settlements.Contains(held.ScopeId),
                        $"seed {seed}: {figure.Name} is master of a trade in no town");
                    Assert.Contains(Crafts.Company(held.Craft), held.Title, StringComparison.Ordinal);
                }
            }
        }

        Assert.True(checked_ > 0, "the panel elected no guild masters at all");
        _out.WriteLine($"{checked_} masteries across the panel, every one over a trade its holder practised");
    }

    /// <summary>One company, one master, at any one time.</summary>
    /// <remarks>
    /// Per town <em>and</em> per trade, which is the whole point of the change: two masteries that
    /// overlap in one town are correct when they are the smiths' and the weavers', and a fault when
    /// they are both the smiths'.
    /// </remarks>
    [Fact]
    public void ACompanyHasOneMasterAtATime()
    {
        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            var spans = new Dictionary<(EntityId Town, Craft Trade), List<(int From, int To, string Who)>>();

            foreach (Figure figure in world.Figures)
            {
                foreach (OfficeHolding held in figure.Offices)
                {
                    if (held.Kind != OfficeKind.GuildMaster) continue;

                    var key = (held.ScopeId, held.Craft);
                    if (!spans.TryGetValue(key, out var held_)) spans[key] = held_ = new();
                    held_.Add((held.FromYear, held.ToYear ?? world.Year, figure.Name));
                }
            }

            foreach (((EntityId town, Craft trade), var held_) in spans)
            {
                held_.Sort((a, b) => a.From.CompareTo(b.From));

                for (int i = 1; i < held_.Count; i++)
                {
                    Assert.True(
                        held_[i].From >= held_[i - 1].To,
                        $"seed {seed}: {world.NameOf(town)}'s {Crafts.Company(trade)} had "
                        + $"{held_[i - 1].Who} and {held_[i].Who} at once");
                }
            }
        }
    }

    /// <summary>
    /// A town's guilds are more than one body, and the panel says how many.
    /// </summary>
    /// <remarks>
    /// The measurement the issue asked for. It is a floor rather than an equality because the
    /// number is an outcome — how many trades a world's towns recorded practitioners of — and
    /// pinning it would make every unrelated change to crafts, levies or migration fail here
    /// instead of where it happened.
    /// </remarks>
    [Fact]
    public void ATownMayHoldSeveralMasteriesAtOnce()
    {
        int townsWithSeveral = 0;

        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            var ever = new Dictionary<EntityId, HashSet<Craft>>();
            var standing = new Dictionary<EntityId, HashSet<Craft>>();
            int masteries = 0;

            foreach (Figure figure in world.Figures)
            {
                foreach (OfficeHolding held in figure.Offices)
                {
                    if (held.Kind != OfficeKind.GuildMaster) continue;

                    masteries++;
                    if (!ever.TryGetValue(held.ScopeId, out var trades)) ever[held.ScopeId] = trades = new();
                    trades.Add(held.Craft);

                    if (held.ToYear is not null) continue;
                    if (!standing.TryGetValue(held.ScopeId, out var open)) standing[held.ScopeId] = open = new();
                    open.Add(held.Craft);
                }
            }

            int several = ever.Count(town => town.Value.Count >= 2);
            townsWithSeveral += several;

            _out.WriteLine(
                $"seed {seed}: {masteries} masteries in {ever.Count} towns; {several} towns held two or "
                + $"more trades' masteries; {standing.Values.Sum(t => t.Count)} standing at the last year");
        }

        Assert.True(
            townsWithSeveral >= 20,
            $"only {townsWithSeveral} towns across the panel ever held more than one mastery");
    }

    /// <summary>The same seed always elects the same masters.</summary>
    [Fact]
    public void MasteriesAreDeterministic()
    {
        foreach (ulong seed in new ulong[] { 7, 42 })
        {
            WorldState first = HistoryRun.Execute(TestWorlds.Standard(seed)).World;
            WorldState second = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in first.Figures)
            {
                Figure twin = second.Figures[figure.Id];
                List<OfficeHolding> mine = figure.Offices
                    .FindAll(office => office.Kind == OfficeKind.GuildMaster);
                List<OfficeHolding> theirs = twin.Offices
                    .FindAll(office => office.Kind == OfficeKind.GuildMaster);

                Assert.Equal(mine.Count, theirs.Count);
                for (int i = 0; i < mine.Count; i++)
                {
                    Assert.Equal(mine[i].Craft, theirs[i].Craft);
                    Assert.Equal(mine[i].ScopeId, theirs[i].ScopeId);
                    Assert.Equal(mine[i].FromYear, theirs[i].FromYear);
                    Assert.Equal(mine[i].ToYear, theirs[i].ToYear);
                    Assert.Equal(mine[i].Title, theirs[i].Title);
                }
            }
        }
    }

    /// <summary>
    /// A guild chose its own, so a mastery has no grantor and does not die with a reign.
    /// </summary>
    /// <remarks>
    /// The rule that separates a trade body from a court post. Every mandated office lapses when
    /// the ruler who granted it stops governing; if a mastery had a grantor it would lapse on the
    /// same pass, and a guild would turn over with the crown — which is exactly backwards.
    /// </remarks>
    [Fact]
    public void AMasteryHasNoGrantorAndOutlivesAReign()
    {
        int survivedAReign = 0;

        foreach (ulong seed in Panel)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                foreach (OfficeHolding held in figure.Offices)
                {
                    if (held.Kind != OfficeKind.GuildMaster) continue;

                    Assert.True(
                        held.GrantedBy.IsNone,
                        $"seed {seed}: {figure.Name}'s mastery was granted by somebody");

                    // Held across a change of ruler somewhere in its span.
                    int from = held.FromYear;
                    int to = held.ToYear ?? world.Year;
                    if (to - from > 0
                        && world.Chronicle.Events.Any(e =>
                            e.Kind == EventKind.RulerCrowned && e.Year > from && e.Year < to))
                    {
                        survivedAReign++;
                    }
                }
            }
        }

        Assert.True(
            survivedAReign > 0,
            "no mastery in the panel outlived a coronation, so the rule is untested");
        _out.WriteLine($"{survivedAReign} masteries were held across a coronation");
    }
}

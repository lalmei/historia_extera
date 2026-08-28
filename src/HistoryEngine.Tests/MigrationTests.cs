using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.World;
using Xunit;
using Xunit.Abstractions;

namespace HistoryEngine.Tests;

/// <summary>
/// Journeys that end in staying: who emigrates, and what it costs to let them.
/// </summary>
/// <remarks>
/// The engine moved people for administrative reasons only — a marriage, a posting, a recall, an
/// accession, a regency — so nobody ever left home because the trade at the far end was better. The
/// questions here are whether the people who stay are ones who plausibly could, and whether the
/// share stays small enough that a merchant's page still reads as a life with a home in it.
/// </remarks>
public sealed class MigrationTests
{
    private static readonly ulong[] Seeds = { 2, 7, 11, 42, 99 };

    private readonly ITestOutputHelper _output;

    public MigrationTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Every stay is the end of the journey that caused it, and the mover could afford to make it.
    /// </summary>
    /// <remarks>
    /// The issue's first acceptance, and the guards are the whole design. An office is a tether and
    /// <c>Offices</c> owns where its holders live; a household moves as a household; and a claim on
    /// a throne is as much a tether as an office, because changing realm takes a person out of the
    /// succession pool that may be counting on them.
    /// </remarks>
    [Fact]
    public void EveryStayIsCausedByItsJourneyAndByNobodyWhoWasTethered()
    {
        int stays = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                foreach (Journey journey in figure.Journeys)
                {
                    if (journey.Outcome != JourneyOutcome.Stayed) continue;

                    // A visit is a visit. A guest of an allied court goes home.
                    Assert.NotEqual(JourneyKind.Visit, journey.Kind);

                    // The residence it caused is in the same year and at the destination.
                    Residence settled = Assert.Single(
                        figure.Residences,
                        residence => residence.Reason == ResidenceReason.Settled
                            && residence.FromYear == journey.Year
                            && residence.SettlementId == journey.ToSettlementId);

                    Assert.True(world.Settlements.Contains(settled.SettlementId));
                    Assert.Equal(journey.ToSettlementId, journey.ReturnSettlementId);
                    Assert.True(figure.AgeIn(journey.Year) >= Succession.MajorityAge);

                    // Nobody held an office at the time. Strictly before, not at or before: the
                    // system order runs `travel` ahead of `household`, `succession` and `office`,
                    // so an office beginning in the journey's own year was granted after the
                    // traveller had already gone — which is how somebody stays as a free merchant
                    // in the spring and is a consort by the winter.
                    foreach (OfficeHolding held in figure.Offices)
                    {
                        bool open = held.ToYear is null || held.ToYear >= journey.Year;
                        Assert.False(
                            open && held.FromYear < journey.Year,
                            $"Seed {seed}: {figure.Id} stayed at {journey.ToSettlementId} in "
                            + $"{journey.Year} while holding {held.Kind} since {held.FromYear}.");
                    }

                    stays++;
                }
            }
        }

        Assert.True(stays > 0, "No journey in the panel ever ended in staying.");
    }

    /// <summary>
    /// A stay actually moves somebody, rather than writing a move the resolver declines to honour.
    /// </summary>
    /// <remarks>
    /// The design question the issue left open, asserted rather than argued.
    /// <see cref="WorldState.ResidenceOf"/> resolves a figure back to their own realm's capital when
    /// their recorded address is not held by their realm — so settling somebody abroad without
    /// moving their membership would record an emigration the engine then silently undoes, which is
    /// the exact failure the residence work existed to remove. Membership follows residence.
    ///
    /// Asserted on the living, because the fallback is legitimate for the dead: measured across the
    /// panel, every divergence between a stayer's recorded and resolved address belongs to somebody
    /// whose town was abandoned or changed hands after they died.
    /// </remarks>
    [Fact]
    public void MembershipFollowsTheMoveSoTheMoveIsHonoured()
    {
        int abroad = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                Residence? settled = null;
                foreach (Residence residence in figure.Residences)
                {
                    if (residence.Reason == ResidenceReason.Settled) settled = residence;
                }

                if (settled is null) continue;
                if (figure.Residences[^1] != settled) continue;

                // The living only. A dead figure's last address is where they died, and a town
                // abandoned or ceded in the centuries after does not move a corpse — so the
                // resolver's fallback diverges for them and correctly so. Measured across the
                // panel, that is the only case where it diverges at all: every living stayer's
                // address is honoured.
                if (!figure.IsAlive) continue;

                Assert.Equal(settled.SettlementId, figure.ResidenceSettlementId);
                Assert.Equal(
                    settled.SettlementId,
                    world.ResidenceOf(figure));

                Settlement destination = world.Settlements[settled.SettlementId];
                Assert.Equal(destination.CivilizationId, figure.CivilizationId);

                if (world.Settlements.Contains(figure.BirthSettlementId)
                    && world.Settlements[figure.BirthSettlementId].CivilizationId
                        != destination.CivilizationId)
                {
                    abroad++;
                }
            }
        }

        Assert.True(abroad > 0, "Nobody in the panel ever settled outside the realm they were born in.");
    }

    /// <summary>
    /// Staying is rare, and it reaches every kind of journey that can produce it.
    /// </summary>
    /// <remarks>
    /// The bound is the point. A world in which a tenth of journeys ended in emigration would have
    /// no homes in it, and the whole reason a journey is not a move is that the overwhelming
    /// majority of travellers come back.
    /// </remarks>
    [Fact]
    public void StayingIsRareAndReachesEveryKindThatAllowsIt()
    {
        var kinds = new SortedSet<JourneyKind>();

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            int journeys = 0;
            int stays = 0;
            var stayers = new HashSet<EntityId>();

            foreach (Figure figure in world.Figures)
            {
                foreach (Journey journey in figure.Journeys)
                {
                    journeys++;
                    if (journey.Outcome != JourneyOutcome.Stayed) continue;

                    stays++;
                    stayers.Add(figure.Id);
                    kinds.Add(journey.Kind);
                }
            }

            double share = stays * 100.0 / Math.Max(journeys, 1);
            _output.WriteLine(
                $"seed {seed}: {stays} of {journeys} journeys ended in staying ({share:F2}%), "
                + $"{stayers.Count} people");

            Assert.True(stays > 0, $"Seed {seed}: nobody ever stayed.");
            Assert.True(
                share < 5.0,
                $"Seed {seed}: {share:F2}% of journeys ended in emigration, which is a world "
                + "without homes in it rather than a world people occasionally leave.");
        }

        // Trade, mission and pilgrimage all reach it; a visit never does.
        Assert.Equal(3, kinds.Count);
        Assert.DoesNotContain(JourneyKind.Visit, kinds);
    }

    /// <summary>
    /// Some people now die somewhere they were neither born nor posted, and the page says why.
    /// </summary>
    /// <remarks>
    /// The issue's third acceptance, and the measurable point of the whole change: before it, the
    /// only reason anybody died away from their birthplace was administrative.
    /// </remarks>
    [Fact]
    public void SomePeopleDieWhereTheyChoseToLive()
    {
        int died = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;
            int seedDeaths = 0;

            foreach (Figure figure in world.Figures)
            {
                if (figure.IsAlive || figure.Residences.Count == 0) continue;

                Residence last = figure.Residences[^1];
                if (last.Reason != ResidenceReason.Settled) continue;
                if (last.SettlementId == figure.BirthSettlementId) continue;

                // The journey that put them there is on their own page, in the year they moved.
                Assert.Contains(
                    figure.Journeys,
                    journey => journey.Outcome == JourneyOutcome.Stayed
                        && journey.Year == last.FromYear
                        && journey.ToSettlementId == last.SettlementId);

                seedDeaths++;
            }

            _output.WriteLine($"seed {seed}: {seedDeaths} died in a town they had settled in");
            died += seedDeaths;
        }

        Assert.True(died > 0, "Nobody in the panel died in a town they had chosen.");
    }
}

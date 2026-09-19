using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Serialization;
using HistoryEngine.World;
using Xunit;
using Xunit.Abstractions;

namespace HistoryEngine.Tests;

/// <summary>
/// Where a recorded person lived, and when they moved.
/// </summary>
/// <remarks>
/// Residence used to be one assignable field with no history, changed in six places and exported
/// only at its final value. The questions here are whether the history is complete — nothing may
/// move somebody without writing it down — and whether it stays cheap enough to keep.
/// </remarks>
public sealed class ResidenceTests
{
    private static readonly ulong[] Seeds = { 2, 7, 11, 42, 99 };

    private readonly ITestOutputHelper _output;

    public ResidenceTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A residence history starts at the birthplace and only ever goes forward.
    /// </summary>
    /// <remarks>
    /// The invariant that makes "where did this person live in year N" answerable by walking to the
    /// last entry at or before N. If the list could go backwards in time, or start somewhere other
    /// than where the person was born, that walk would have to become a search with a tie-break.
    /// </remarks>
    [Fact]
    public void EveryHistoryStartsAtBirthAndIsMonotonic()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                if (figure.Residences.Count == 0)
                {
                    // Only somebody whose birth settlement is not a real place, which is the
                    // raised adult introduced straight into an office.
                    Assert.False(world.Settlements.Contains(figure.BirthSettlementId));
                    continue;
                }

                Residence first = figure.Residences[0];
                Assert.Equal(ResidenceReason.Birth, first.Reason);
                Assert.Equal(figure.BirthSettlementId, first.SettlementId);
                Assert.Equal(figure.BirthYear, first.FromYear);

                int year = int.MinValue;
                EntityId previous = EntityId.None;
                foreach (Residence residence in figure.Residences)
                {
                    Assert.True(
                        residence.FromYear >= year,
                        $"Seed {seed}: {figure.Id} moved backwards in time.");
                    Assert.True(
                        residence.SettlementId != previous,
                        $"Seed {seed}: {figure.Id} moved to where they already lived.");
                    Assert.True(world.Settlements.Contains(residence.SettlementId));

                    year = residence.FromYear;
                    previous = residence.SettlementId;
                }

                // The field and the history are the same fact, which is the point of routing every
                // move through one helper.
                Assert.Equal(
                    figure.Residences[^1].SettlementId, figure.ResidenceSettlementId);
            }
        }
    }

    /// <summary>
    /// Nothing moves anybody without writing it down.
    /// </summary>
    /// <remarks>
    /// The regression that matters most as the engine grows. A seventh site that assigns residence
    /// directly would be invisible in review and would silently reintroduce the gap this work
    /// closed, so the assertion is that every address a figure is seen at is one their own history
    /// accounts for.
    /// </remarks>
    [Fact]
    public void EveryAddressAFigureIsSeenAtIsInTheirOwnHistory()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                if (figure.Residences.Count == 0) continue;

                var lived = new HashSet<EntityId>();
                foreach (Residence residence in figure.Residences)
                {
                    lived.Add(residence.SettlementId);
                }

                Assert.Contains(figure.ResidenceSettlementId, lived);
            }
        }
    }

    /// <summary>
    /// A siege endured is preceded by an arrival, unless the resolver placed them there.
    /// </summary>
    /// <remarks>
    /// <para>The issue's own acceptance, and the readability problem that motivated the work: a
    /// page that says somebody endured a siege at a town they were never recorded arriving at reads
    /// as though they appeared there from nowhere.</para>
    ///
    /// <para><b>It does not hold unconditionally, and the issue did not know that.</b> Presence is
    /// not always a recorded move: <see cref="WorldState.ResidenceOf"/> falls back to the realm's
    /// capital when a figure's recorded address is no longer held by their realm, so a border
    /// moving under somebody changes where the engine places them without anybody travelling. This
    /// work does not fix that and does not claim to — a cession is not a removal. So the assertion
    /// is the strongest true one: where the fallback is not in play, an arrival must exist.</para>
    /// </remarks>
    [Fact]
    public void ASiegeEnduredHasAnArrivalBehindIt()
    {
        int checkedSieges = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                foreach (CampaignMemory memory in figure.Campaigns)
                {
                    if (memory.Role != CampaignRole.EnduredSiege) continue;
                    if (!world.Battles.Contains(memory.BattleId)) continue;

                    EntityId where = world.Battles[memory.BattleId].SettlementId;
                    if (where.IsNone || figure.Residences.Count == 0) continue;

                    bool arrived = false;
                    foreach (Residence residence in figure.Residences)
                    {
                        if (residence.FromYear > memory.Year) break;
                        if (residence.SettlementId == where) arrived = true;
                    }

                    if (arrived)
                    {
                        checkedSieges++;
                        continue;
                    }

                    // The one honest alternative. `WorldState.ResidenceOf` places a figure at
                    // their realm's capital when their recorded address is no longer held by
                    // their realm, so a border moving under somebody changes where the engine
                    // thinks they are without anybody going anywhere. Where that fallback is not
                    // in play there is no such excuse, and an arrival must exist.
                    Assert.True(
                        world.ResidenceOf(figure) != figure.ResidenceSettlementId,
                        $"Seed {seed}: {figure.Id} endured a siege at {where} in {memory.Year} "
                        + "having never been recorded arriving there, and their recorded address "
                        + "is live, so the resolver's fallback cannot explain it.");
                }
            }
        }

        Assert.True(checkedSieges > 0, "No endured siege was ever checked against an arrival.");
    }

    /// <summary>
    /// The export alone answers where somebody lived in a given year.
    /// </summary>
    [Fact]
    public void ResidenceIsReconstructableFromTheExportAlone()
    {
        WorldExport export = HistoryRun.Execute(TestWorlds.Standard(42)).ToExport();
        int walked = 0;

        foreach (ExportFigure figure in export.Figures)
        {
            if (figure.Residences.Count < 2) continue;

            ExportResidence last = figure.Residences[^1];
            Assert.Equal(last.SettlementId, Where(figure, last.FromYear));

            // And the year before the final move is the address before it. Two moves can land in
            // one year — married in the spring and posted in the autumn — so the comparison is
            // against the last entry that genuinely precedes that year, not against the one
            // before it in the list.
            ExportResidence? before = null;
            foreach (ExportResidence residence in figure.Residences)
            {
                if (residence.FromYear >= last.FromYear) break;
                before = residence;
            }

            if (before is null) continue;

            Assert.Equal(before.SettlementId, Where(figure, last.FromYear - 1));
            walked++;
        }

        Assert.True(walked > 0, "Nobody in the export ever moved.");

        static EntityId? Where(ExportFigure figure, int year)
        {
            EntityId? at = null;
            foreach (ExportResidence residence in figure.Residences)
            {
                if (residence.FromYear > year) break;
                at = residence.SettlementId;
            }

            return at;
        }
    }

    /// <summary>
    /// Removals do not reach the spine at all.
    /// </summary>
    /// <remarks>
    /// The issue proposed Notable where an office or a throne caused the move, and the measurement
    /// it asked for in the same breath refused it: postings and recalls alone put 744 removals into
    /// a 16,430-event timeline. They are redundant there as well as numerous — the office grant,
    /// the recall and the accession are each already on the spine and each already say where the
    /// person went. This asserts the stricter rule that followed from measuring.
    /// </remarks>
    [Fact]
    public void RemovalsStayOffTheSpineUnlessAnOfficeCausedThem()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            int moves = 0;
            int notable = 0;
            foreach (HistoryEvent entry in world.Chronicle.Events)
            {
                if (entry.Kind != EventKind.FigureMoved) continue;

                moves++;
                if (entry.Significance == Significance.Notable) notable++;
            }

            int total = world.Chronicle.Events.Count;
            _output.WriteLine(
                $"seed {seed}: {moves} removals ({notable} notable) of {total} events "
                + $"({notable * 100.0 / total:F2}% on the spine)");

            Assert.True(moves > 0, $"Seed {seed}: nobody ever moved.");
            Assert.Equal(0, notable);
        }
    }

    /// <summary>
    /// A household is not left in two places.
    /// </summary>
    /// <remarks>
    /// The rule that used to live in one caller and was missing from the others. A governor
    /// recalled to court leaving his wife in a provincial town is not merely untidy — the two
    /// halves of the household are then exposed to different sieges, plagues and famines.
    /// </remarks>
    [Fact]
    public void AHouseholdMovesTogether()
    {
        int couples = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                if (!figure.IsAlive || !world.Figures.Contains(figure.SpouseId)) continue;

                Figure spouse = world.Figures[figure.SpouseId];
                if (!spouse.IsAlive) continue;

                // Two people who each hold a seat of their own keep their own courts, which is
                // the one case the household rule deliberately does not apply to.
                if (Succession.HoldsAThrone(world, figure)
                    || Succession.HoldsAThrone(world, spouse))
                {
                    continue;
                }

                if (figure.CurrentOffice is not null || spouse.CurrentOffice is not null) continue;

                // A couple the border has divided. Marriage puts both in one realm, but a cession
                // or a secession can take one of them out of it afterwards, and the resolver then
                // places each at their own realm's capital. Nobody moved; measured across the
                // panel this is four couples in five worlds, all of them cross-realm.
                if (figure.CivilizationId != spouse.CivilizationId) continue;

                Assert.Equal(
                    world.ResidenceOf(figure), world.ResidenceOf(spouse));
                couples++;
            }
        }

        Assert.True(couples > 0, "No ordinary married couple was ever compared.");
    }

    /// <summary>A permanent border change carries living residents into the town's new realm.</summary>
    /// <remarks>
    /// Treaty cessions once moved the region and settlement tables but skipped the resident pass
    /// used by revolts and defections. A living person then kept the ceded town as their recorded
    /// address while <see cref="WorldState.ResidenceOf"/> silently placed them at the loser's
    /// capital. Temporary occupation is different: ownership has not changed, so it is excluded.
    /// </remarks>
    [Fact]
    public void PermanentTerritoryTransfersDoNotStrandLivingResidents()
    {
        int checkedResidents = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                if (!figure.IsAlive) continue;
                if (!world.Settlements.Contains(figure.ResidenceSettlementId)) continue;

                Settlement residence = world.Settlements[figure.ResidenceSettlementId];
                if (!residence.IsActive || residence.IsOccupied) continue;

                checkedResidents++;
                Assert.Equal(
                    figure.CivilizationId,
                    residence.CivilizationId);
            }
        }

        Assert.True(checkedResidents > 100, $"Only {checkedResidents} living residents were checked.");
    }

    /// <summary>
    /// A spouse who shared the mover's old address follows on a move that is not the marriage
    /// itself.
    /// </summary>
    /// <remarks>
    /// Exercises <see cref="Houses.Settle"/> directly with <c>withHousehold: true</c>, the way a
    /// recall, an accession, a regency, a flight or a settled journey now all call it, rather than
    /// waiting for one to occur in a full run. A governor recalled to court leaving his wife behind
    /// in the province is the exact failure the household pass exists to prevent.
    /// </remarks>
    [Fact]
    public void ASpouseFollowsOnANonMarriageMove()
    {
        WorldState world = WorldBuilder.Create(TestWorlds.Small());
        Civilization civilization = world.Civilizations[0];
        Culture culture = world.Cultures[civilization.CultureId];
        EntityId destination = OtherCapital(world, civilization);

        Figure figure = Houses.NewFigure(world, civilization, culture, Sex.Male, birthYear: 1);
        Figure spouse = Houses.NewFigure(world, civilization, culture, Sex.Female, birthYear: 1);
        figure.SpouseId = spouse.Id;
        figure.SpouseIds.Add(spouse.Id);
        spouse.SpouseId = figure.Id;
        spouse.SpouseIds.Add(figure.Id);

        bool moved = Houses.Settle(
            world, figure, destination, ResidenceReason.Recall, year: 40, withHousehold: true);

        Assert.True(moved);
        Assert.Equal(destination, figure.ResidenceSettlementId);
        Assert.Equal(destination, spouse.ResidenceSettlementId);
    }

    /// <summary>
    /// A married child keeps the address of the household they founded rather than the one they
    /// grew up in.
    /// </summary>
    /// <remarks>
    /// Dragging a married child along splits the household they founded: they would arrive at the
    /// parent's new town with their own spouse left behind, which is the failure
    /// <see cref="Houses.Settle"/> already documents one generation down.
    /// </remarks>
    [Fact]
    public void AMarriedChildDoesNotFollow()
    {
        WorldState world = WorldBuilder.Create(TestWorlds.Small());
        Civilization civilization = world.Civilizations[0];
        Culture culture = world.Cultures[civilization.CultureId];
        EntityId home = civilization.CapitalId;
        EntityId destination = OtherCapital(world, civilization);

        Figure figure = Houses.NewFigure(world, civilization, culture, Sex.Male, birthYear: 1);
        Figure child = Houses.NewFigure(world, civilization, culture, Sex.Female, birthYear: 20);
        Figure childSpouse = Houses.NewFigure(world, civilization, culture, Sex.Male, birthYear: 20);
        figure.ChildIds.Add(child.Id);
        child.SpouseId = childSpouse.Id;
        child.SpouseIds.Add(childSpouse.Id);
        childSpouse.SpouseId = child.Id;
        childSpouse.SpouseIds.Add(child.Id);

        Houses.Settle(world, figure, destination, ResidenceReason.Recall, year: 40, withHousehold: true);

        Assert.Equal(destination, figure.ResidenceSettlementId);
        Assert.Equal(home, child.ResidenceSettlementId);
    }

    /// <summary>
    /// A child posted to govern a town of their own does not follow a parent's move into a
    /// different one.
    /// </summary>
    /// <remarks>
    /// The failure <see cref="Houses.PostedElsewhere"/> exists to prevent: a court can post a
    /// father to one town in the same decade it posts his unmarried son to another, and without
    /// this guard the engine would end up with a governor who lives somewhere he does not govern.
    /// </remarks>
    [Fact]
    public void AChildWithTheirOwnPostingDoesNotFollow()
    {
        WorldState world = WorldBuilder.Create(TestWorlds.Small());
        Civilization civilization = world.Civilizations[0];
        Culture culture = world.Cultures[civilization.CultureId];
        EntityId home = civilization.CapitalId;
        EntityId destination = OtherCapital(world, civilization);
        EntityId thirdTown = ThirdSettlement(world, civilization, home, destination);

        Figure figure = Houses.NewFigure(world, civilization, culture, Sex.Male, birthYear: 1);
        Figure child = Houses.NewFigure(world, civilization, culture, Sex.Female, birthYear: 20);
        figure.ChildIds.Add(child.Id);
        child.Offices.Add(
            new OfficeHolding(OfficeKind.Governor, "Governor", civilization.Id, 30, null)
            {
                ScopeId = thirdTown,
            });

        Houses.Settle(world, figure, destination, ResidenceReason.Recall, year: 40, withHousehold: true);

        Assert.Equal(destination, figure.ResidenceSettlementId);
        Assert.Equal(home, child.ResidenceSettlementId);
    }

    /// <summary>
    /// An established adult child — of majority age and already carrying a trade of their own —
    /// sometimes stays behind and sometimes follows, but the same way every time for the same
    /// seed, id and year.
    /// </summary>
    /// <remarks>
    /// <para>This is the determinism guarantee the whole feature stands or falls on: the choice is
    /// forked from <see cref="WorldState.Root"/> keyed on the child's id and the year, the same
    /// idiom <see cref="Undertakings"/> already uses for a person's own choices, so it must be
    /// reproducible and must not depend on draws any other household happens to make.</para>
    ///
    /// <para>One child per seed is not reliable enough on its own — a single coin flip is exactly
    /// as likely to land the same way five times as not — so this samples twenty established
    /// children per seed to make "some stay and some go" a near-certainty, and separately rebuilds
    /// one seed's scenario twice to check that a single, specific outcome repeats exactly.</para>
    /// </remarks>
    [Fact]
    public void AnEstablishedAdultChildSometimesStaysAndSometimesGoesButIsDeterministicPerSeed()
    {
        bool anyStayed = false;
        bool anyWent = false;

        foreach (ulong seed in Seeds)
        {
            bool[] outcome = SettleWithEstablishedChildren(seed, childCount: 20);
            foreach (bool stayed in outcome)
            {
                if (stayed) anyStayed = true;
                else anyWent = true;
            }
        }

        Assert.True(anyStayed, "No established adult child ever stayed behind across five seeds.");
        Assert.True(anyWent, "No established adult child ever followed along across five seeds.");

        // Determinism: rebuilding the identical scenario — same seed, same construction order, so
        // the same figure ids and the same year — must reach the identical answer for every child.
        bool[] first = SettleWithEstablishedChildren(42, childCount: 20);
        bool[] second = SettleWithEstablishedChildren(42, childCount: 20);
        Assert.Equal(first, second);
    }

    /// <summary>
    /// Builds one parent with <paramref name="childCount"/> established adult children sharing the
    /// parent's address, settles the parent with the household, and reports which children stayed.
    /// </summary>
    private static bool[] SettleWithEstablishedChildren(ulong seed, int childCount)
    {
        WorldState world = WorldBuilder.Create(TestWorlds.Standard(seed));
        Civilization civilization = world.Civilizations[0];
        Culture culture = world.Cultures[civilization.CultureId];
        EntityId home = civilization.CapitalId;
        EntityId destination = OtherCapital(world, civilization);

        const int year = 60;
        Figure figure = Houses.NewFigure(world, civilization, culture, Sex.Male, birthYear: 1);
        var children = new Figure[childCount];
        for (int i = 0; i < childCount; i++)
        {
            // Well past majority (16) and already carrying a trade, so every one of them is
            // "established" and free to choose, rather than a minor who has no choice at all.
            Figure child = Houses.NewFigure(
                world, civilization, culture, Sex.Female, birthYear: year - 30);
            child.Occupation = Occupation.Merchant;
            figure.ChildIds.Add(child.Id);
            children[i] = child;
        }

        Houses.Settle(world, figure, destination, ResidenceReason.Recall, year, withHousehold: true);

        var outcome = new bool[childCount];
        for (int i = 0; i < childCount; i++)
        {
            outcome[i] = children[i].ResidenceSettlementId == home;
        }

        return outcome;
    }

    /// <summary>A settlement flight sweeps the whole household even an established child would
    /// otherwise be free to sit out, because there is no town left to stay behind in.</summary>
    [Fact]
    public void FlightTakesAnEstablishedChildRegardlessOfTheCoin()
    {
        int checkedChildren = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = WorldBuilder.Create(TestWorlds.Standard(seed));
            Civilization civilization = world.Civilizations[0];
            Culture culture = world.Cultures[civilization.CultureId];
            EntityId home = civilization.CapitalId;
            EntityId destination = OtherCapital(world, civilization);

            const int year = 60;
            Figure figure = Houses.NewFigure(world, civilization, culture, Sex.Male, birthYear: 1);
            var children = new Figure[20];
            for (int i = 0; i < children.Length; i++)
            {
                Figure child = Houses.NewFigure(
                    world, civilization, culture, Sex.Female, birthYear: year - 30);
                child.Occupation = Occupation.Merchant;
                figure.ChildIds.Add(child.Id);
                children[i] = child;
            }

            // One call for the whole household, exactly as a real evacuation does it: every
            // established child's coin is rolled in the same pass, and every one of them must
            // still come out on the other side, because the alternative is a resident of a town
            // that no longer exists.
            Houses.Settle(world, figure, destination, ResidenceReason.Flight, year, withHousehold: true);

            foreach (Figure child in children)
            {
                Assert.Equal(destination, child.ResidenceSettlementId);
                checkedChildren++;
            }
        }

        Assert.True(checkedChildren > 0, "No established child was ever checked against a flight.");
    }

    private static EntityId OtherCapital(WorldState world, Civilization civilization)
    {
        foreach (Civilization other in world.Civilizations)
        {
            if (other.Id != civilization.Id) return other.CapitalId;
        }

        throw new InvalidOperationException("Test world has only one civilization.");
    }

    private static EntityId ThirdSettlement(
        WorldState world, Civilization civilization, EntityId home, EntityId destination)
    {
        foreach (Settlement settlement in world.Settlements)
        {
            if (settlement.Id != home && settlement.Id != destination) return settlement.Id;
        }

        throw new InvalidOperationException("Test world has no third settlement.");
    }
}

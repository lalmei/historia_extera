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
}

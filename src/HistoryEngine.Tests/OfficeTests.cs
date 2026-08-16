using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// The offices below a throne: who holds them, on whose authority, and at what cost.
/// </summary>
/// <remarks>
/// The premise of the whole milestone is that offices are a <em>use</em> for people the world
/// already breeds rather than a reason to breed more, so several of these are cost assertions
/// rather than behaviour ones. An appointment model that works perfectly and doubles the figure
/// table every century has failed at the thing it was built to do.
/// </remarks>
public sealed class OfficeTests
{
    private static readonly ulong[] Seeds = { 2, 7, 11, 42, 99 };

    /// <summary>Every office is reached, and by both routes into one.</summary>
    /// <remarks>
    /// A world where every seat is filled by crown appointment has its culture inputs wired to
    /// nothing, and one where none is has the same bug from the other side.
    /// </remarks>
    [Fact]
    public void EveryOfficeAndBothFillModesAreReached()
    {
        var kinds = new HashSet<OfficeKind>();
        int mandated = 0;
        int internally = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                foreach (OfficeHolding held in figure.Offices)
                {
                    kinds.Add(held.Kind);

                    if (held.Kind is OfficeKind.Ruler or OfficeKind.Regent) continue;

                    if (held.GrantedBy.IsNone) internally++;
                    else mandated++;
                }
            }
        }

        foreach (OfficeKind kind in Enum.GetValues<OfficeKind>())
        {
            Assert.Contains(kind, kinds);
        }

        Assert.True(mandated > 20, $"Only {mandated} offices were filled by a crown.");
        Assert.True(internally > 20, $"Only {internally} offices were filled by the body itself.");
    }

    /// <summary>
    /// An office is held by one living adult of the realm it belongs to, and by one only.
    /// </summary>
    /// <remarks>
    /// Two holders of one seat is the failure this system is most likely to produce quietly: the
    /// lapse pass and the fill pass disagree about whether a seat is vacant, and the chronicle
    /// records two marshals for a century without anything looking wrong.
    /// </remarks>
    [Fact]
    public void EveryOpenOfficeHasExactlyOneQualifiedHolder()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;
            int endYear = world.EndYear;

            var seats = new HashSet<string>();

            foreach (Figure figure in world.Figures)
            {
                foreach (OfficeHolding held in figure.Offices)
                {
                    if (held.ToYear is not null) continue;

                    Assert.True(
                        figure.IsAlive,
                        $"{figure.Name} still holds {held.Title} having died in {figure.DeathYear}.");

                    // A crown is inherited, not served: a child on a throne under a regent is the
                    // model working, not a breach of it. Every other office is entered into by
                    // someone who has to be capable of discharging it.
                    if (held.Kind != OfficeKind.Ruler)
                    {
                        Assert.True(
                            figure.AgeIn(endYear) >= Offices.ServiceAge,
                            $"{figure.Name} holds {held.Title} at {figure.AgeIn(endYear)}.");
                    }

                    // The office belongs to the realm its holder lives in. An office that could
                    // move someone across a border would reintroduce the M5 class of bug that left
                    // three realms governed by a corpse.
                    Assert.True(
                        figure.CivilizationId == held.CivilizationId,
                        $"{figure.Name} ({figure.Id}) lives in {figure.CivilizationId} but holds "
                        + $"{held.Kind} {held.Title} of {held.CivilizationId} "
                        + $"(scope {held.ScopeId}, from {held.FromYear}).");

                    string seat = held.Kind + ":" + held.CivilizationId + ":" + held.ScopeId;
                    Assert.True(seats.Add(seat), $"Two holders of {seat} in seed {seed}.");
                }
            }
        }
    }

    /// <summary>
    /// Everyone the chronicle follows is somewhere, and somewhere their own realm holds.
    /// </summary>
    /// <remarks>
    /// <para>Residence used to be recorded only for office-holders, so "at court" was inferred
    /// from the absence of an address — which answers the question only for as long as nothing
    /// needs to know where an ordinary figure is. It now follows a birth, a marriage, a crown and
    /// a posting.</para>
    ///
    /// <para>The assertion is on the resolved answer rather than the stored field, because the
    /// stored one is allowed to go stale: a town can be abandoned or taken with people living in
    /// it, and requiring every system that moves a settlement to chase its residents is exactly
    /// the coupling <see cref="WorldState.ResidenceOf"/> exists to avoid.</para>
    /// </remarks>
    [Fact]
    public void EveryLivingFigureLivesSomewhereTheirRealmHolds()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            int placed = 0;

            foreach (Figure figure in world.Figures)
            {
                if (!figure.IsAlive) continue;
                if (!world.Civilizations.Contains(figure.CivilizationId)) continue;
                if (!world.Civilizations[figure.CivilizationId].IsActive) continue;

                EntityId where = world.ResidenceOf(figure);

                Assert.True(
                    world.Settlements.Contains(where),
                    $"{figure.Name} of a standing realm lives nowhere.");

                Settlement home = world.Settlements[where];

                Assert.True(home.IsActive, $"{figure.Name} lives in abandoned {home.Name}.");
                Assert.Equal(figure.CivilizationId, home.CivilizationId);

                placed++;
            }

            Assert.True(placed > 0, $"Seed {seed} had no living figure in a standing realm.");
        }
    }

    /// <summary>A governor lives in the town they govern, which is what exposes them to it.</summary>
    [Fact]
    public void GovernorsResideInTheirOwnSettlements()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard(42)).World;
        int found = 0;

        foreach (Figure figure in world.Figures)
        {
            OfficeHolding? held = figure.OpenOffice(OfficeKind.Governor);
            if (held is null) continue;

            found++;
            Assert.Equal(held.ScopeId, figure.ResidenceSettlementId);

            Settlement place = world.Settlements[held.ScopeId];
            Assert.True(place.IsActive);
            Assert.False(place.IsCapital);
        }

        Assert.True(found > 0, "No settlement in a 300-year world had a governor.");
    }

    /// <summary>
    /// Invented notables hold offices and never enter a nursery.
    /// </summary>
    /// <remarks>
    /// This is the bound on the whole system. A body that cannot find a courtier invents a local,
    /// and a local who could marry and breed would make the figure table grow with the number of
    /// seats times the number of generations. The guard already existed — <c>HouseholdSystem</c>
    /// refuses to match anyone of no house — and this asserts the office path has not reached
    /// around it.
    /// </remarks>
    [Fact]
    public void InventedNotablesNeverBreed()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                if (!figure.DynastyId.IsNone) continue;

                // Only the three appointed offices. Someone married into a house has no house of
                // their own, and legitimately holds two others: a consort's style, and a regency
                // for their own child — the queen mother, whom ChooseRegent prefers precisely
                // because her interest in the reign is not also a claim to replace it.
                bool appointed = false;
                foreach (OfficeHolding held in figure.Offices)
                {
                    if (held.Kind is OfficeKind.Marshal
                        or OfficeKind.HighPriest
                        or OfficeKind.Governor)
                    {
                        appointed = true;
                    }
                }

                if (!appointed) continue;

                Assert.Empty(figure.ChildIds);
                Assert.Equal(EntityId.None, figure.SpouseId);
            }
        }
    }

    /// <summary>
    /// The figure table still grows with the number of reigns, not with the number of seats.
    /// </summary>
    /// <remarks>
    /// Offices raise the population of the record once — every seat needs somebody — and must not
    /// change its growth rate. Doubling the run should roughly double the count, as it did before
    /// offices existed.
    /// </remarks>
    [Fact]
    public void OfficesRaiseTheFigureCountWithoutBendingItsCurve()
    {
        int shortRun = HistoryRun.Execute(TestWorlds.Standard(42)).World.Figures.Count;
        int longRun = HistoryRun.Execute(TestWorlds.Standard(42) with { Years = 600 }).World.Figures.Count;

        double ratio = (double)longRun / shortRun;

        Assert.True(
            ratio is > 1.6 and < 2.6,
            $"Twice the years produced {ratio:F2}x the figures ({shortRun} then {longRun}).");
    }

    /// <summary>
    /// Every office changes what some other system does.
    /// </summary>
    /// <remarks>
    /// The standard the design set itself, and the one worth a test rather than a comment: an
    /// office nothing reads is a title generator. Each of the three appointed offices is asserted
    /// through its consumer rather than through its own record — a marshal by the fields he takes,
    /// a governor by dying somewhere the court is not, a high priest by the faiths he starts.
    /// </remarks>
    [Fact]
    public void OfficesChangeWhatOtherSystemsDo()
    {
        int marshalCommands = 0;
        int governorsKilledByGeography = 0;
        int faithsFromClergy = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Battle battle in world.Battles)
            {
                foreach (EntityId id in new[] { battle.AttackerCommanderId, battle.DefenderCommanderId })
                {
                    if (!world.Figures.Contains(id)) continue;
                    if (HeldAt(world.Figures[id], OfficeKind.Marshal, battle.Year)) marshalCommands++;
                }
            }

            foreach (Figure figure in world.Figures)
            {
                if (figure.DeathYear is not int died) continue;

                // Killed where they were posted rather than where the court is. Plague is
                // deliberately excluded even though governors die of it: it is modelled at the
                // realm level and reached them before offices existed, so counting it here would
                // let this assertion pass on a mechanism it is not testing — which is exactly
                // what it did until a review noticed that no governor had ever died of the one
                // cause the residence model is responsible for.
                if (figure.DeathCause is not (DeathCause.Disaster or DeathCause.Battle)) continue;
                if (figure.DeathDetail?.StartsWith("in the sack of ", StringComparison.Ordinal) != true
                    && figure.DeathCause != DeathCause.Disaster)
                {
                    continue;
                }

                if (HeldAt(figure, OfficeKind.Governor, died)) governorsKilledByGeography++;
            }

            foreach (Religion faith in world.Religions)
            {
                if (!world.Figures.Contains(faith.FounderId)) continue;
                if (HeldAt(world.Figures[faith.FounderId], OfficeKind.HighPriest, faith.FoundedYear))
                {
                    faithsFromClergy++;
                }
            }
        }

        Assert.True(marshalCommands > 0, "No marshal ever took the field.");
        Assert.True(
            governorsKilledByGeography > 0,
            "No governor was ever reached by a calamity in the town they governed.");
        Assert.True(faithsFromClergy > 0, "No faith was ever preached by a realm's own high priest.");
    }

    /// <summary>Whether a figure held a given office in a given year.</summary>
    private static bool HeldAt(Figure figure, OfficeKind kind, int year)
    {
        foreach (OfficeHolding held in figure.Offices)
        {
            if (held.Kind != kind || held.FromYear > year) continue;
            if (held.ToYear is null || held.ToYear >= year) return true;
        }

        return false;
    }

    /// <summary>
    /// Someone raised from the ordinary population arrives with a career behind them.
    /// </summary>
    /// <remarks>
    /// <para>Every office used to recruit at 26–45 whatever it was, so a high priest and a town's
    /// headman were the same age on average and neither had done anything to get there. The
    /// assertion worth making is not that the ages are in range — that is arithmetic — but that
    /// the ladders differ: a temple is the slowest of them, and if that stops being true the bands
    /// have been flattened back into one guess.</para>
    ///
    /// <para>There is deliberately no birth <em>event</em> to check. The chronicle is append-only
    /// in non-decreasing year order, so a birth forty years before the appointment cannot be
    /// inserted; the birth year on the figure is the whole of what can honestly be recorded.</para>
    /// </remarks>
    [Fact]
    public void RaisedFiguresArriveWithAnAgeTheirOfficeExplains()
    {
        var ages = new Dictionary<OfficeKind, List<int>>();
        int withOrigin = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Figure figure in world.Figures)
            {
                // A dynast's origin is their house, and a consort's the marriage that brought
                // them in. Only those an office raised out of the population carry one.
                if (!figure.DynastyId.IsNone)
                {
                    Assert.Equal(FigureOrigin.Unrecorded, figure.Origin);
                    continue;
                }

                if (figure.Origin != FigureOrigin.Unrecorded) withOrigin++;

                foreach (OfficeHolding held in figure.Offices)
                {
                    if (held.Kind is not (OfficeKind.Marshal
                        or OfficeKind.HighPriest
                        or OfficeKind.Governor))
                    {
                        continue;
                    }

                    int age = held.FromYear - figure.BirthYear;

                    // Nobody takes an office before they could have earned it, and nobody is
                    // raised into one at an age they would not survive holding.
                    Assert.InRange(age, 16, 75);

                    if (!ages.TryGetValue(held.Kind, out List<int>? seen))
                    {
                        seen = new List<int>();
                        ages[held.Kind] = seen;
                    }

                    seen.Add(age);
                }
            }
        }

        Assert.True(withOrigin > 50, $"Only {withOrigin} figures were raised with a recorded origin.");

        double priests = Mean(ages[OfficeKind.HighPriest]);
        double governors = Mean(ages[OfficeKind.Governor]);
        double marshals = Mean(ages[OfficeKind.Marshal]);

        Assert.True(
            priests > governors && priests > marshals,
            $"A temple should be the slowest ladder; priests {priests:F1}, "
            + $"governors {governors:F1}, marshals {marshals:F1}.");
    }

    private static double Mean(List<int> values)
    {
        long total = 0;
        foreach (int value in values) total += value;
        return (double)total / values.Count;
    }

    /// <summary>
    /// Appointments are a bounded share of the chronicle.
    /// </summary>
    /// <remarks>
    /// The volume risk named in the design. Offices are interesting exactly as long as they do not
    /// crowd out what a realm did with them; a chronicle whose commonest entry is an appointment
    /// has become an administrative record.
    /// </remarks>
    [Fact]
    public void AppointmentsDoNotCrowdOutTheChronicle()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard(42)).World;

        int appointments = 0;
        foreach (Events.HistoryEvent entry in world.Chronicle.Events)
        {
            if (entry.Kind is Events.EventKind.OfficeGranted or Events.EventKind.OfficeRevoked)
            {
                appointments++;
            }
        }

        double share = (double)appointments / world.Chronicle.Count;

        Assert.True(appointments > 0, "No office was ever granted.");
        Assert.True(share < 0.20, $"Appointments are {share:P0} of the chronicle.");
    }
}

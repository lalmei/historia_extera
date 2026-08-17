using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// That grievance the engine already measured turns into lawlessness and revolt, and that a
/// rising resolves rather than looping.
/// </summary>
/// <remarks>
/// Written the way <c>WarTests</c> and <c>OfficeTests</c> are: asserting that the outcomes
/// <em>happen</em> across several seeds rather than that the code paths exist, because the whole
/// point of the unrest system is that the pressure feeding it was present and unread for a long
/// time before anything consumed it. A test that only proved <c>UnrestSystem.Tick</c> runs would
/// have passed on the day the pressure went nowhere.
/// </remarks>
public sealed class UnrestTests
{
    /// <summary>Seeds sampled where the question is a rate; a wider net for the rare events.</summary>
    private static readonly ulong[] Seeds = { 2, 7, 11, 42, 99 };

    private static readonly ulong[] WideSeeds = { 2, 7, 11, 42, 99, 123, 777, 2024 };

    /// <summary>
    /// Discontent reaches the chronicle: brigandage on the roads and risings in the towns.
    /// </summary>
    [Fact]
    public void GrievanceProducesUnrest()
    {
        int brigandage = 0;
        int risings = 0;

        foreach (ulong seed in WideSeeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (HistoryEvent entry in world.Chronicle.Events)
            {
                if (entry.Kind == EventKind.BrigandageWorsened) brigandage++;
                if (entry.Kind == EventKind.RevoltBroke) risings++;
            }
        }

        Assert.True(brigandage > 0, "No brigandage ever troubled the roads.");
        Assert.True(risings > 0, "No town ever rose in revolt.");
    }

    /// <summary>
    /// Every rising ends the year it began: a <see cref="EventKind.RevoltBroke"/> is always
    /// answered, and never left standing to be resolved again next spring.
    /// </summary>
    /// <remarks>
    /// The guard against the failure occupation had before it was fixed — the same walls fought over
    /// year after year with nothing changing. A revolt that did not resolve would show up here as a
    /// break with no matching conclusion in the same year and settlement.
    /// </remarks>
    [Fact]
    public void EveryRisingResolves()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (HistoryEvent broke in world.Chronicle.Events)
            {
                if (broke.Kind != EventKind.RevoltBroke) continue;

                bool resolved = false;
                foreach (HistoryEvent other in world.Chronicle.Events)
                {
                    if (other.Year != broke.Year || other.Subject != broke.Subject) continue;

                    // A crush, a defection or a garrison thrown off — the three ways a rising ends.
                    if (other.Kind is EventKind.RevoltCrushed
                        or EventKind.RevoltPrevailed
                        or EventKind.SettlementRestored)
                    {
                        resolved = true;
                        break;
                    }
                }

                Assert.True(
                    resolved,
                    $"A rising in {world.NameOf(broke.Subject)} in {broke.Year} was never resolved.");
            }
        }
    }

    /// <summary>
    /// A settlement that changes hands to a revolt is never orphaned: every standing town answers
    /// to a realm that is still standing, however it got there.
    /// </summary>
    /// <remarks>
    /// A defection moves a town's allegiance outside the war and cession machinery, so it is the one
    /// path that could leave a settlement pointing at a realm that has since fallen. Asserting the
    /// invariant over the finished world is what proves the peacetime transfer keeps the ownership
    /// graph whole.
    /// </remarks>
    [Fact]
    public void RebelTownsKeepAValidOwner()
    {
        foreach (ulong seed in WideSeeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Settlement settlement in world.Settlements)
            {
                if (!settlement.IsActive) continue;

                Assert.True(
                    world.Civilizations.Contains(settlement.CivilizationId),
                    $"{settlement.Name} answers to no known realm.");
                Assert.True(
                    world.Civilizations[settlement.CivilizationId].IsActive,
                    $"{settlement.Name} answers to a fallen realm.");
            }
        }
    }

    /// <summary>
    /// An occupied town can rise against the garrison holding it, not only against its own crown —
    /// and a rising it wins gives the town back to the realm that lost it.
    /// </summary>
    [Fact]
    public void OccupiedTownsRiseAgainstTheGarrison()
    {
        int garrisonRisings = 0;

        foreach (ulong seed in WideSeeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (HistoryEvent entry in world.Chronicle.Events)
            {
                if (entry.Kind != EventKind.RevoltBroke) continue;

                // A garrison revolt names an adversary that is not the town's own realm.
                Settlement town = world.Settlements[entry.Subject];
                if (entry.Object != town.CivilizationId) garrisonRisings++;
            }
        }

        Assert.True(garrisonRisings > 0, "No occupied town ever rose against its garrison.");
    }
}

using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// Guards on what a settlement can be known for, and when it is asked.
/// </summary>
/// <remarks>
/// These exist because the failure they cover was invisible for a milestone. Every trade in
/// <see cref="SettlementSpecialization"/> carried five economic curves and three of them —
/// craftwork, garrison towns and quarries — had never once been chosen in any world. Two were
/// missing from the candidate list and the third was gated on a tier the question was never asked
/// at. Nothing failed; the distribution was simply short three trades, and no test looked.
/// </remarks>
public sealed class SpecializationTests
{
    /// <summary>How many extra worlds the reachability search may open before giving up.</summary>
    /// <remarks>
    /// Sized against the measured rates rather than guessed. Over twenty-four standard worlds the
    /// scarcest trades appear in a fifth to a third of them — pastoral in 5, fishing in 6, a shrine
    /// town in 8 — so a trade that is genuinely reachable is found within a handful of seeds and
    /// one that is missing from twenty is not a sampling accident.
    /// </remarks>
    private const int ReachabilitySeeds = 20;

    /// <summary>Every trade the engine costs must be one a world can actually produce.</summary>
    /// <remarks>
    /// <para><b>Reachability is a property of the model, not of one world.</b> This used to ask a
    /// single millennium whether it had produced all eight, which conflates "the engine cannot make
    /// a shrine town" with "this particular world did not happen to". The distinction is the whole
    /// point of the test — it exists because three trades carried full economic curves and had
    /// never once been chosen — and a one-world sample cannot draw it.</para>
    ///
    /// <para><b>So it searches, and stops as soon as the question is answered.</b> The millennium
    /// world is still asked first and still carries the weight: it is the mature world, and the
    /// trades gated on town size are the ones that need it. Only what that world missed sends the
    /// search on to further seeds, and it stops at the first world that completes the set — so the
    /// ordinary case costs exactly what it always did, and the failing case costs a few short worlds
    /// rather than a false alarm.</para>
    ///
    /// <para>This replaces a pinned single seed. Every change that moves the world's histories
    /// reshuffles which trades land in seed 42's millennium, and repinning after each one would be
    /// maintenance paid to keep a weaker question alive.</para>
    /// </remarks>
    [Fact]
    public void EveryCandidateTradeIsReachable()
    {
        HashSet<SettlementSpecialization> seen = TradesIn(HistoryRun.Execute(TestWorlds.Long()).World);

        int wanted = Enum.GetValues<SettlementSpecialization>().Length - 1;
        int opened = 0;

        for (ulong seed = 1; seen.Count < wanted && opened < ReachabilitySeeds; seed++, opened++)
        {
            seen.UnionWith(TradesIn(HistoryRun.Execute(TestWorlds.Standard(seed)).World));
        }

        foreach (SettlementSpecialization trade in Enum.GetValues<SettlementSpecialization>())
        {
            if (trade == SettlementSpecialization.None) continue;

            Assert.True(
                seen.Contains(trade),
                $"No settlement is known for {Specializations.Label(trade)} in a thousand years or "
                + $"in {opened} worlds after it, but it carries a full set of capacity curves. "
                + "Either it needs a way to be chosen or its numbers should come out.");
        }
    }

    /// <summary>Every trade any settlement of a finished world is known for.</summary>
    private static HashSet<SettlementSpecialization> TradesIn(WorldState world)
    {
        HashSet<SettlementSpecialization> seen = new();

        foreach (Settlement settlement in world.Settlements)
        {
            if (settlement.Specialization != SettlementSpecialization.None)
            {
                seen.Add(settlement.Specialization);
            }
        }

        return seen;
    }

    /// <summary>
    /// A place may wait to be asked, but a town has an answer.
    /// </summary>
    /// <remarks>
    /// The waiting is the whole mechanism: a village whose only answer is a poor farm is left
    /// unlabelled and asked again as it grows, which is the only way craftwork — gated on town size
    /// — ever gets to be an answer. The guard is that waiting stops at a town. If a city can go
    /// through a thousand years without a trade, the bar has become a veto.
    /// </remarks>
    [Fact]
    public void NoTownOrCityIsLeftWithoutATrade()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Long()).World;

        foreach (Settlement settlement in world.Settlements)
        {
            if (!settlement.IsActive) continue;
            if (settlement.Tier < SettlementTier.Town) continue;

            Assert.True(
                settlement.Specialization != SettlementSpecialization.None,
                $"{settlement.Name} is a {settlement.Tier} of {settlement.Population} and is known " +
                "for nothing.");
        }
    }

    /// <summary>
    /// Established once. Asking repeatedly must not become reclassifying repeatedly.
    /// </summary>
    [Fact]
    public void ATradeIsEstablishedOnceAndNeverRevised()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Long()).World;

        Dictionary<EntityId, int> declarations = new();

        foreach (HistoryEvent recorded in world.Chronicle.Events)
        {
            if (recorded.Kind != EventKind.SettlementSpecialized) continue;

            declarations[recorded.Subject] = declarations.GetValueOrDefault(recorded.Subject) + 1;
        }

        Assert.NotEmpty(declarations);

        foreach ((EntityId id, int times) in declarations)
        {
            Assert.True(times == 1, $"{id} was given a trade {times} times.");
        }
    }

    /// <summary>
    /// A craft town is a place that grew into its trade, not one founded as it.
    /// </summary>
    /// <remarks>
    /// The measurement this replaces: before the question could be asked more than once, craft
    /// towns were chosen zero times across ten worlds, because the decision was always taken at
    /// village tier where craftwork scores -1.0.
    /// </remarks>
    [Fact]
    public void CraftTownsGrewIntoTheirTrade()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Long()).World;

        int crafts = 0;

        foreach (Settlement settlement in world.Settlements)
        {
            if (settlement.Specialization != SettlementSpecialization.Crafts) continue;

            crafts++;

            Assert.True(
                settlement.PeakPopulation
                >= SettlementTiers.PopulationThresholds[(int)SettlementTier.Town],
                $"{settlement.Name} does craftwork without ever having been a town.");

            Assert.NotNull(settlement.SpecializedYear);
            Assert.True(
                settlement.SpecializedYear > settlement.FoundedYear,
                $"{settlement.Name} was a craft town the year it was founded.");
        }

        Assert.True(crafts > 0, "A thousand years produced no craft town at all.");
    }

    /// <summary>A garrison town stands somewhere worth garrisoning.</summary>
    [Fact]
    public void MilitaryTownsHoldAPosition()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Long()).World;

        int held = 0;

        foreach (Settlement settlement in world.Settlements)
        {
            if (settlement.Specialization != SettlementSpecialization.Military) continue;

            held++;

            Assert.True(
                settlement.Site is SiteCharacter.Strategic or SiteCharacter.Pass
                    or SiteCharacter.Fortress,
                $"{settlement.Name} is a garrison town on {SiteCharacters.Label(settlement.Site)}.");
        }

        Assert.True(held > 0, "A thousand years produced no garrison town at all.");
    }
}

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
    /// <summary>Every trade the engine costs must be one a world can actually produce.</summary>
    [Fact]
    public void EveryCandidateTradeIsReachable()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Long()).World;

        HashSet<SettlementSpecialization> seen = new();

        foreach (Settlement settlement in world.Settlements)
        {
            if (settlement.Specialization != SettlementSpecialization.None)
            {
                seen.Add(settlement.Specialization);
            }
        }

        foreach (SettlementSpecialization trade in Enum.GetValues<SettlementSpecialization>())
        {
            if (trade == SettlementSpecialization.None) continue;

            Assert.True(
                seen.Contains(trade),
                $"No settlement in a thousand years is known for {Specializations.Label(trade)}, " +
                "but it carries a full set of capacity curves. Either it needs a way to be chosen " +
                "or its numbers should come out.");
        }
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

using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>The durable character state left behind by ordinary historical events.</summary>
public sealed class LifeStoryTests
{
    [Fact]
    public void EveryBondInALongWorldHasAReciprocalBond()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard(42)).World;

        foreach (Figure figure in world.Figures)
        {
            foreach (FigureBond bond in figure.Bonds)
            {
                Assert.True(world.Figures.Contains(bond.OtherId));

                Figure other = world.Figures[bond.OtherId];
                FigureBond? reciprocal = LifeStories.BondTo(other, figure.Id);

                Assert.NotNull(reciprocal);
                Assert.InRange(bond.Affection, -1.0, 1.0);
                Assert.InRange(bond.Trust, -1.0, 1.0);
                Assert.InRange(bond.Obligation, 0.0, 1.0);
                Assert.InRange(bond.Fear, 0.0, 1.0);
                Assert.InRange(bond.Grievance, 0.0, 1.0);
            }
        }
    }

    [Fact]
    public void FamilyPatronageMentorshipAndRivalryAllLeaveCausalState()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard(7)).World;

        Assert.Contains(world.Figures, figure =>
            figure.Bonds.Exists(bond => bond.Kinds.HasFlag(BondKind.Spouse)));
        Assert.Contains(world.Figures, figure =>
            figure.Bonds.Exists(bond => bond.Kinds.HasFlag(BondKind.Parent)));
        Assert.Contains(world.Figures, figure =>
            figure.Bonds.Exists(bond => bond.Kinds.HasFlag(BondKind.Client)));
        Assert.Contains(world.Figures, figure =>
            figure.Bonds.Exists(bond => bond.Kinds.HasFlag(BondKind.Apprentice)));
        Assert.Contains(world.Figures, figure =>
            figure.Bonds.Exists(bond => bond.Kinds.HasFlag(BondKind.Rival)));

        Assert.Contains(world.Figures, figure =>
            figure.Memories.Exists(memory => memory.Kind == MemoryKind.Bereavement));
        Assert.Contains(world.Figures, figure =>
            figure.Memories.Exists(memory => memory.Kind == MemoryKind.Mentorship));
        Assert.Contains(world.Figures, figure =>
            figure.Memories.Exists(memory => memory.Kind == MemoryKind.Humiliation));
    }

    [Fact]
    public void MemoryIsBoundedAndReinforcementKeepsAFormativeExperience()
    {
        var figure = new Figure(
            EntityId.Figure(0), EntityId.Civilization(0), EntityId.Culture(0),
            "Alda", Sex.Female, 0);

        for (int i = 0; i < LifeStories.MemoryCapacity; i++)
        {
            LifeStories.Remember(
                figure,
                MemoryKind.Journey,
                i,
                EventKind.JourneyMade,
                EntityId.Figure(i + 1),
                intensity: 0.4);
        }

        LifeStories.Remember(
            figure,
            MemoryKind.Journey,
            20,
            EventKind.JourneyMade,
            EntityId.Figure(1),
            intensity: 0.9);

        LifeStories.Remember(
            figure,
            MemoryKind.Bereavement,
            21,
            EventKind.FigureDied,
            EntityId.Figure(99),
            intensity: 0.8);

        Assert.Equal(LifeStories.MemoryCapacity, figure.Memories.Count);
        Assert.Contains(figure.Memories, memory => memory.AboutId == EntityId.Figure(1));
        Assert.Contains(figure.Memories, memory => memory.AboutId == EntityId.Figure(99));
    }
}

using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.World;
using Xunit;
using Xunit.Abstractions;

namespace HistoryEngine.Tests;

/// <summary>
/// The armourer (#255): harness is made, by armourers, in towns that could keep one, and it
/// does not crowd the blade or the jewel out of a world's treasuries.
/// </summary>
public sealed class ArmourerTests
{
    private readonly ITestOutputHelper _out;

    public ArmourerTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void ArmourIsMadeByArmourersAndStaysRarerThanTheBlade()
    {
        var kinds = new Dictionary<ArtifactKind, int>();
        int named = 0;
        int changedHands = 0;

        // One millennium on one seed, as the other long-horizon tests run: three centuries make a
        // handful of blades across the whole five-seed panel, too few to split. The five-seed
        // volumes were measured once and are recorded in the commit that added this test.
        const ulong seed = 42;
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Long(seed)).World;

            foreach (Artifact artifact in world.Artifacts)
            {
                kinds[artifact.Kind] = kinds.GetValueOrDefault(artifact.Kind) + 1;
                if (artifact.Kind != ArtifactKind.Armor) continue;

                if (artifact.Provenance.Count > 1) changedHands++;
                if (artifact.CreatorId.IsNone) continue;

                // The maker is the armourer's alone: Makers lists no other craft for harness, so a
                // smith or a goldsmith here would be a maker found outside the model.
                Figure maker = world.Figures[artifact.CreatorId];
                Assert.True(
                    maker.Craft == Craft.Armourer,
                    $"seed {seed}: {artifact.Name} names a {Crafts.Label(maker.Craft)}");
                named++;
            }
        }

        int armour = kinds.GetValueOrDefault(ArtifactKind.Armor);
        int blades = kinds.GetValueOrDefault(ArtifactKind.Weapon);
        int jewels = kinds.GetValueOrDefault(ArtifactKind.Jewel);

        foreach ((ArtifactKind kind, int count) in kinds.OrderByDescending(entry => entry.Value))
        {
            _out.WriteLine($"{kind,-10}{count,6}");
        }

        _out.WriteLine($"armour named {named} of {armour}, changed hands {changedHands}");

        Assert.True(armour > 0, "the panel made no armour");
        Assert.True(named > 0, "no armour in the panel names its maker");
        Assert.True(blades > armour, $"armour ({armour}) crowded out the blade ({blades})");
        Assert.True(jewels > armour, $"armour ({armour}) crowded out the jewel ({jewels})");
    }
}

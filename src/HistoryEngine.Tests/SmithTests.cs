using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Systems;
using HistoryEngine.World;
using Xunit;
using Xunit.Abstractions;

namespace HistoryEngine.Tests;

/// <summary>
/// The smith (#254): where iron and fuel reach, down to the village, and nowhere they do not.
/// </summary>
public sealed class SmithTests
{
    private static readonly ulong[] Panel = { 2, 7, 11, 42, 99 };

    private readonly ITestOutputHelper _out;

    public SmithTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Nobody is bound to the forge, and no blade is made, in a place iron and fuel do not reach.
    /// </summary>
    /// <remarks>
    /// <para>Asked at the moment of each decision rather than of the finished world. Supply moves
    /// under a town — a route closes, a mine camp takes another trade — so a smith standing in a
    /// town that has since lost its road is a true record, and the end of the run cannot tell him
    /// from a violated gate. <see cref="Forges"/> runs last in every step and reads the smiths
    /// and weapons that step produced while the world is still the one they were produced in.</para>
    ///
    /// <para>The anonymous blades are reported rather than asserted. A town that could hold a
    /// forge may still have hit the levy's ceiling, and anonymity there is the result
    /// <see cref="Levies"/> argues for; what may not happen is a blade from a town that could never
    /// have had anyone to make it.</para>
    /// </remarks>
    [Fact]
    public void NoSmithOrBladeWhereNeitherIronNorFuelReaches()
    {
        int smiths = 0;
        int blades = 0;
        int named = 0;

        foreach (ulong seed in Panel)
        {
            Forges watch = Watch(seed);

            Assert.True(
                watch.Violations.Count == 0,
                $"seed {seed}: " + string.Join("; ", watch.Violations.Take(5)));

            smiths += watch.Smiths;
            blades += watch.Blades;
            named += watch.NamedBlades;
        }

        _out.WriteLine($"smiths bound {smiths}, blades made {blades}, naming a smith or armourer {named}");

        Assert.True(smiths > 0, "the panel bound nobody to the forge");
        Assert.True(blades > 0, "the panel made no blade");
    }

    /// <summary>
    /// The forge reaches below the town; and where it stands among the crafts, written out.
    /// </summary>
    /// <remarks>
    /// <para>The one craft the issue asks to exist below town tier, and the floor the levy stands
    /// on. A tier gate added to the smith's weight by mistake would pass every other test here.</para>
    ///
    /// <para><b>Reported, not asserted: whether the forge is the commonest trade.</b> #254 asks for
    /// it to be, and on the ground-or-road gate it is not — second by headcount behind the weaver,
    /// and behind the weaver below a town as well, counted where each craft was taken. The weaver's
    /// lead is the one #245 calibrated on purpose. Closing the gap needs either a gate wider than
    /// the issue's own model or a weaver retuned outside this craft's issue, which is a decision
    /// rather than a fix; the table keeps the gap measured until it is made.</para>
    /// </remarks>
    [Fact]
    public void ReportWhereTheForgeStands()
    {
        var village = new Dictionary<Craft, int>();
        var heads = new Dictionary<Craft, int>();
        int held = 0;

        foreach (ulong seed in Panel)
        {
            Forges watch = Watch(seed);

            foreach ((Craft craft, int count) in watch.VillageByCraft)
            {
                village[craft] = village.GetValueOrDefault(craft) + count;
            }

            foreach ((Craft craft, int count) in watch.ByCraft)
            {
                heads[craft] = heads.GetValueOrDefault(craft) + count;
                held += count;
            }
        }

        _out.WriteLine($"{"craft",-16}{"village",8}{"all",6}{"share",8}");
        foreach ((Craft craft, int count) in heads.OrderByDescending(entry => entry.Value))
        {
            _out.WriteLine(
                $"{Crafts.Label(craft),-16}{village.GetValueOrDefault(craft),8}{count,6}{(double)count / held,8:P1}");
        }

        Assert.True(village.GetValueOrDefault(Craft.Smith) > 0, "no village in the panel bound a smith");
    }

    private static Forges Watch(ulong seed)
    {
        var watch = new Forges();
        var systems = new List<ISystem>(Simulator.DefaultSystems()) { watch };

        HistoryRun.Execute(TestWorlds.Standard(seed), simulator: new Simulator(systems));

        return watch;
    }

    /// <summary>
    /// Reads each step's new smiths and new blades after every other system has run.
    /// </summary>
    /// <remarks>
    /// Draws nothing and writes nothing, so appending it leaves the world the default order would
    /// have produced; the systems that fork their streams do so on their own names.
    /// </remarks>
    private sealed class Forges : ISystem
    {
        private int _events;
        private int _artifacts;

        public string Name => "test-forges";

        public Cadence Cadence => Cadence.Seasonal;

        public List<string> Violations { get; } = new();

        public int Smiths { get; private set; }

        public Dictionary<Craft, int> ByCraft { get; } = new();

        public Dictionary<Craft, int> VillageByCraft { get; } = new();

        public int Blades { get; private set; }

        public int NamedBlades { get; private set; }

        public void Tick(WorldState world, Stamp now)
        {
            IReadOnlyList<HistoryEvent> events = world.Chronicle.Events;
            for (; _events < events.Count; _events++)
            {
                HistoryEvent entry = events[_events];
                if (entry.Kind != EventKind.CraftTaken) continue;
                if (!world.Settlements.Contains(entry.Location)) continue;

                Craft craft = Taken(entry);
                Settlement town = world.Settlements[entry.Location];

                ByCraft[craft] = ByCraft.GetValueOrDefault(craft) + 1;
                if (town.Tier <= SettlementTier.Village)
                {
                    VillageByCraft[craft] = VillageByCraft.GetValueOrDefault(craft) + 1;
                }

                if (craft != Craft.Smith) continue;
                Smiths++;

                if (!Crafts.Supports(world, town, Craft.Smith))
                {
                    Violations.Add($"a smith bound in {town.Name} in {entry.Year}");
                }
            }

            for (; _artifacts < world.Artifacts.Count; _artifacts++)
            {
                Artifact artifact = world.Artifacts[_artifacts];
                if (artifact.Kind != ArtifactKind.Weapon) continue;

                Settlement town = world.Settlements[artifact.OriginSettlementId];
                Blades++;

                if (!Crafts.Supports(world, town, Craft.Smith))
                {
                    Violations.Add($"a blade made in {town.Name} in {artifact.CreatedYear}");
                }

                if (!artifact.CreatorId.IsNone
                    && world.Figures[artifact.CreatorId].Craft is Craft.Smith or Craft.Armourer)
                {
                    NamedBlades++;
                }
            }
        }

        /// <summary>The trade a <see cref="EventKind.CraftTaken"/> entry names, by its label.</summary>
        private static Craft Taken(HistoryEvent entry)
        {
            string? label = entry.DataValue("craft");
            foreach (Craft craft in World.Crafts.All)
            {
                if (World.Crafts.Label(craft) == label) return craft;
            }

            return Craft.None;
        }
    }
}

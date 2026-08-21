using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.Terrain;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// That the trade network's physical layer is a network, follows the ground, and costs nothing
/// per year.
/// </summary>
/// <remarks>
/// The assertions here are about outcomes rather than about code paths, because every failure
/// this layer can have is a property of a whole world: a road under every line on the map, a road
/// through the sea, a river route that ignores its river, or a path search that quietly runs once
/// a year instead of once per road.
/// </remarks>
public sealed class RoadTests
{
    private static readonly ulong[] Seeds = { 2, 7, 11, 42, 99 };

    /// <summary>
    /// Roads are built for sustained traffic, on land, and to a minority of the links.
    /// </summary>
    /// <remarks>
    /// The upper bound is the assertion that matters. A threshold low enough to road most of the
    /// network produces a map on which no corridor stands out from any other — the failure the
    /// route system's own degree cap was written to avoid, reintroduced one layer up. Measured
    /// across these five seeds the pooled roaded share of land routes is 0.32; per world it runs
    /// from 0.00 to 0.38, the zero being a sea world whose six land routes never carried enough
    /// to earn one.
    /// </remarks>
    [Fact]
    public void RoadsAreBuiltOnlyForTheLinksThatEarnedThem()
    {
        int roadsSeen = 0;
        int landSeen = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            int land = 0;
            int roaded = 0;
            int built = 0;

            foreach (HistoryEvent entry in world.Chronicle.Events)
            {
                if (entry.Kind == EventKind.RoadBuilt) built++;
            }

            foreach (TradeRoute route in world.TradeRoutes)
            {
                if (route.Mode != TradeRouteMode.Coastal) land++;

                if (route.Road is not { } road) continue;

                roaded++;
                roadsSeen++;

                Assert.NotEqual(TradeRouteMode.Coastal, route.Mode);
                Assert.True(
                    route.PeakTraffic >= Roads.BuildThreshold,
                    $"Route {route.Id} carries a road on peak traffic {route.PeakTraffic:F3}.");
                Assert.True(road.BuiltYear >= route.FoundedYear);

                if (road.Grade == RoadGrade.Paved)
                {
                    Assert.True(route.PeakTraffic >= Roads.PaveThreshold);
                    Assert.NotNull(road.PavedYear);
                    Assert.True(road.PavedYear >= road.BuiltYear);
                }
                else
                {
                    Assert.Null(road.PavedYear);
                }
            }

            Assert.Equal(built, roaded);
            Assert.True(land > 0, $"Seed {seed} produced no land routes to road.");

            // The ceiling is per world, because roading most of one map is the failure this
            // guards. The floor is pooled below: seed 2 is a sea world with six land routes in
            // total, and a share over six items has a resolution of one sixth — asserting it
            // clears 5% is a claim about that world's traffic, not about the road system.
            Assert.True(
                roaded <= land * 0.55,
                $"Seed {seed} roaded {roaded} of {land} land routes. Road most of a network and "
                + "no corridor stands out from any other.");

            landSeen += land;
        }

        Assert.True(roadsSeen > 50, $"Only {roadsSeen} roads were checked across {Seeds.Length} worlds.");
        Assert.InRange(roadsSeen / (double)landSeen, 0.05, 0.55);
    }

    /// <summary>
    /// A road runs between its two towns, over dry land, and is never shorter than the direct way.
    /// </summary>
    /// <remarks>
    /// <para>Interior vertices are grid cells the search chose, and the search refuses submerged
    /// cells everywhere except at the endpoints — so this is the invariant that says an overland
    /// road cannot be drawn across a strait. Endpoints themselves are the settlements' own
    /// coordinates, which is why they are asserted rather than sampled.</para>
    ///
    /// <para>Reading the vertices back costs no terrain: they are nodes of the grid hydrology
    /// already sampled at world creation, so <see cref="TerrainAtlas.SampleExact"/> serves them
    /// from the memo.</para>
    /// </remarks>
    [Fact]
    public void ARoadRunsOverTheGroundBetweenItsTwoTowns()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (TradeRoute route in world.TradeRoutes)
            {
                if (route.Road is not { } road) continue;

                Settlement a = world.Settlements[route.SettlementAId];
                Settlement b = world.Settlements[route.SettlementBId];

                Assert.True(road.Points.Count >= 2);
                Assert.Equal(new RoadPoint(a.X, a.Z), road.Points[0]);
                Assert.Equal(new RoadPoint(b.X, b.Z), road.Points[^1]);

                for (int i = 1; i < road.Points.Count - 1; i++)
                {
                    RoadPoint point = road.Points[i];
                    Assert.True(
                        world.Terrain.SampleExact(point.X, point.Z).Height >= 0f,
                        $"Road on route {route.Id} passes through water at ({point.X}, {point.Z}).");
                }

                double straight = world.Distance(a.X, a.Z, b.X, b.Z);
                Assert.True(
                    road.Length >= straight - 0.001,
                    $"Road on route {route.Id} is shorter than the straight line between its ends.");
            }
        }
    }

    /// <summary>
    /// The way round is taken when the direct line would go through the water.
    /// </summary>
    /// <remarks>
    /// <para>Without this, the previous test is satisfiable by drawing a straight line between
    /// every pair, since most pairs have dry ground between them anyway. So this asks the search
    /// the question it exists to answer: two points of land with a bay between them.</para>
    ///
    /// <para>Asked of the cost model rather than of the routes a run happened to produce, because
    /// which towns end up trading across a bay is a fact about a seed. The pair is found on the
    /// same grid the search uses — measuring the water with the lattice's interpolated height
    /// instead would be a second instrument disagreeing with the first about where the shore
    /// is.</para>
    /// </remarks>
    [Fact]
    public void ARoadGoesRoundWaterTheDirectLineWouldCross()
    {
        int exercised = 0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            var land = new List<RoadPoint>();
            for (int z = world.Config.Bounds.MinZ; z < world.Config.Bounds.MaxZ; z += 64)
            {
                for (int x = world.Config.Bounds.MinX; x < world.Config.Bounds.MaxX; x += 64)
                {
                    if (world.Terrain.SampleExact(x, z).Height >= 0f) land.Add(new RoadPoint(x, z));
                }
            }

            for (int i = 0; i < land.Count && exercised < 5; i += 7)
            {
                for (int j = i + 1; j < land.Count && exercised < 5; j += 11)
                {
                    double apart = world.Distance(land[i].X, land[i].Z, land[j].X, land[j].Z);
                    if (apart < 300.0 || apart > 900.0) continue;
                    if (!CrossesWater(world, land[i], land[j])) continue;

                    Road? road = world.Roadbed.Cut(
                        land[i].X, land[i].Z, land[j].X, land[j].Z,
                        TradeRouteMode.Overland, RoadGrade.Track, 100);

                    // No way round at all is a legitimate answer — the two shores may belong to
                    // different islands — and the caller records no road in that case.
                    if (road is null) continue;

                    exercised++;

                    for (int k = 1; k < road.Points.Count - 1; k++)
                    {
                        RoadPoint point = road.Points[k];
                        Assert.True(
                            world.Terrain.SampleExact(point.X, point.Z).Height >= 0f,
                            $"A road cut across water in seed {seed} at ({point.X}, {point.Z}).");
                    }

                    Assert.True(
                        road.Length > apart,
                        $"A road across a bay in seed {seed} is the length of the direct line, " +
                        "so it did not go round anything.");
                }
            }
        }

        Assert.True(
            exercised > 0,
            "No pair of shores with water between them was found in any seed, so the diversion " +
            "this test exists to check was never exercised.");
    }

    /// <summary>Whether the direct line between two grid nodes passes over a submerged one.</summary>
    private static bool CrossesWater(WorldState world, RoadPoint from, RoadPoint to)
    {
        int steps = (int)(world.Distance(from.X, from.Z, to.X, to.Z) / 64.0);

        for (int s = 1; s < steps; s++)
        {
            double t = s / (double)steps;

            // Snapped to the grid, so every reading is a memoised point and the walk costs no
            // terrain samples.
            int x = (int)Math.Round((from.X + ((to.X - from.X) * t)) / 64.0) * 64;
            int z = (int)Math.Round((from.Z + ((to.Z - from.Z) * t)) / 64.0) * 64;

            if (world.Terrain.SampleExact(x, z).Height < 0f) return true;
        }

        return false;
    }

    /// <summary>
    /// A river route's way stays nearer the water than the same journey cut overland.
    /// </summary>
    /// <remarks>
    /// <para>The claim "river routes hug rivers" is about the cost model rather than about any
    /// particular pair of towns, so it is asked of the cost model: the same endpoints are cut twice
    /// and the two lines compared. Asking it only of the routes the simulation happens to produce
    /// would test a handful of one-cell hops in some seeds and nothing at all in others.</para>
    ///
    /// <para>Per-pair the river line is nearer the water four times in five; the mean over a
    /// seed's pairs is nearer in every seed measured, by 3–24%. The aggregate is what is asserted,
    /// because a single pair whose river runs the wrong way is a fact about that valley and not a
    /// fault in the model.</para>
    /// </remarks>
    [Fact]
    public void ARiverRouteFollowsTheWater()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;
            Hydrology hydrology = world.Terrain.Hydrology;

            var river = new List<RoadPoint>();
            for (int z = world.Config.Bounds.MinZ; z < world.Config.Bounds.MaxZ; z += 64)
            {
                for (int x = world.Config.Bounds.MinX; x < world.Config.Bounds.MaxX; x += 64)
                {
                    if (hydrology.IsRiver(x, z)) river.Add(new RoadPoint(x, z));
                }
            }

            double byRiverSum = 0.0;
            double overlandSum = 0.0;
            int pairs = 0;

            for (int i = 0; i < river.Count && pairs < 12; i++)
            {
                for (int j = i + 1; j < river.Count && pairs < 12; j++)
                {
                    double apart = world.Distance(river[i].X, river[i].Z, river[j].X, river[j].Z);
                    if (apart < 400.0 || apart > 900.0) continue;

                    Road? byRiver = world.Roadbed.Cut(
                        river[i].X, river[i].Z, river[j].X, river[j].Z,
                        TradeRouteMode.River, RoadGrade.Track, 0);
                    Road? overland = world.Roadbed.Cut(
                        river[i].X, river[i].Z, river[j].X, river[j].Z,
                        TradeRouteMode.Overland, RoadGrade.Track, 0);

                    if (byRiver is null || overland is null) continue;

                    pairs++;
                    byRiverSum += MeanRiverDistance(world, hydrology, byRiver.Points);
                    overlandSum += MeanRiverDistance(world, hydrology, overland.Points);
                }
            }

            Assert.True(pairs > 0, $"Seed {seed} offered no river pair to compare.");
            Assert.True(
                byRiverSum < overlandSum,
                $"In seed {seed} a river route's path averaged {byRiverSum / pairs:F1} units from " +
                $"the nearest river against the overland cut's {overlandSum / pairs:F1}. The river " +
                "toll is not buying anything, so the mode is a label on a line that ignores it.");
        }
    }

    /// <summary>
    /// Paving keeps the route, keeps the road's founding, and never lengthens the way.
    /// </summary>
    /// <remarks>
    /// The identity half is the point: an upgrade is a change to the surface of an existing
    /// relationship, so the route's id, its founding year, its traffic record and the year its
    /// first way was cut all have to survive it. A road rebuilt as a new entity would silently
    /// throw away the century the track was in use.
    /// </remarks>
    [Fact]
    public void PavingKeepsTheRouteAndItsHistory()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard()).World;

        TradeRoute? tracked = null;
        foreach (TradeRoute route in world.TradeRoutes)
        {
            if (route.Road is { Grade: RoadGrade.Track }) tracked = route;
        }

        Assert.NotNull(tracked);

        EntityId id = tracked!.Id;
        int foundedYear = tracked.FoundedYear;
        double peak = tracked.PeakTraffic;
        Road track = tracked.Road!;

        Road? paved = Roads.Cut(world, tracked, RoadGrade.Paved, track.BuiltYear + 40);
        Assert.NotNull(paved);
        tracked.Road = paved;

        Assert.Equal(id, tracked.Id);
        Assert.Equal(foundedYear, tracked.FoundedYear);
        Assert.Equal(peak, tracked.PeakTraffic);
        Assert.Equal(track.BuiltYear, paved!.BuiltYear);
        Assert.Equal(track.BuiltYear + 40, paved.PavedYear);
        Assert.Equal(RoadGrade.Paved, paved.Grade);

        // Bridges and cuttings buy a line that is never worse than the one they replace. On short
        // roads over easy ground they buy nothing at all, and the way is identical — which is the
        // honest answer, not a failure.
        Assert.True(paved.Length <= track.Length + 0.001);
    }

    /// <summary>
    /// Paving shortens the way, and never sends it wandering.
    /// </summary>
    /// <remarks>
    /// <para><b>Length is not what a road minimises.</b> <c>Roadbed.Cut</c> searches for the
    /// cheapest line, and paving lowers the price of slope and of fording rather than the price
    /// of distance. So the engineered line is usually shorter and can legitimately be a little
    /// longer, when a few more metres reach ground that is enough cheaper to pay for them. An
    /// exact "never longer" would be asserting that the cost function is distance, which it is
    /// not, and it held for as long as it did by luck: it broke the moment depression filling
    /// moved the rivers and with them the fords.</para>
    ///
    /// <para><b>What is asserted instead, and why these numbers.</b> Across 85 paved roads over
    /// five seeds and six centuries, 17 are shorter — one by 29% — 67 are identical, and exactly
    /// one is longer, by 1.6%. So the population must shorten on net, and no single road may
    /// wander: the 5% ceiling is triple the largest overshoot measured and still far below any
    /// detour that would mean the search had gone wrong, which is what this test is for.</para>
    /// </remarks>
    [Fact]
    public void PavingShortensTheWayWithoutSendingItWandering()
    {
        int compared = 0;
        double pavedTotal = 0.0;
        double trackTotal = 0.0;

        foreach (ulong seed in Seeds)
        {
            // Longer than the standard run on purpose. Paving is generational — a track must stand
            // a quarter-century or more before anyone bridges it — so three centuries leave only a
            // handful of paved roads, and most of those in whichever seed happens to trade most.
            // The invariant here is geometric, not a rate, so it wants samples rather than speed.
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed) with { Years = 600 }).World;

            foreach (TradeRoute route in world.TradeRoutes)
            {
                if (route.Road is not { Grade: RoadGrade.Paved } paved) continue;

                Settlement a = world.Settlements[route.SettlementAId];
                Settlement b = world.Settlements[route.SettlementBId];

                Road? track = world.Roadbed.Cut(
                    a.X, a.Z, b.X, b.Z, route.Mode, RoadGrade.Track, paved.BuiltYear);

                Assert.NotNull(track);
                compared++;
                pavedTotal += paved.Length;
                trackTotal += track!.Length;

                Assert.True(
                    paved.Length <= (track.Length * 1.05) + 0.001,
                    $"The paved way on route {route.Id} is {paved.Length / track.Length:P1} of " +
                    "the track it replaced. Engineering may buy a slightly longer line to reach " +
                    "cheaper ground; it should not go wandering.");
            }
        }

        Assert.True(compared > 10, $"Only {compared} paved roads were available to compare.");
        Assert.True(
            pavedTotal <= trackTotal,
            $"Paving lengthened the road network overall — {pavedTotal:F0} against " +
            $"{trackTotal:F0} units of track. Individually a road may go slightly round; the " +
            "population may not.");
    }

    /// <summary>
    /// Cutting roads samples no terrain at all.
    /// </summary>
    /// <remarks>
    /// The whole design rests on this: the grid a road is searched over is the one hydrology
    /// already paid for at world creation, so construction reads memoised points and never reaches
    /// the backend. Against Vintage Story's sampler a road that sampled its own corridor would cost
    /// seconds apiece. Both the derivation of the planes and the searches themselves are inside the
    /// measured window.
    /// </remarks>
    [Fact]
    public void CuttingARoadSamplesNoTerrain()
    {
        var counter = new CountingTerrainSampler(
            new ProceduralTerrainSampler(42, TerrainBounds.Square(4096)));
        var atlas = new TerrainAtlas(counter, stride: 256, hydrologyStride: 64);

        atlas.EnsurePrimed();
        _ = atlas.Hydrology;
        long primed = counter.SampleCount;

        Roadbed roadbed = Roadbed.Build(atlas);

        int cut = 0;
        for (int i = 0; i < 20; i++)
        {
            int from = 256 + (i * 64);
            Road? road = roadbed.Cut(
                from, 1024, from + 512, 1536, TradeRouteMode.Overland, RoadGrade.Track, 100);
            if (road is not null) cut++;
        }

        Assert.True(cut > 0, "No road could be cut, so nothing was measured.");
        Assert.Equal(primed, counter.SampleCount);
    }

    /// <summary>Two runs of one seed lay the same roads down to the last vertex.</summary>
    /// <remarks>
    /// A path search is the kind of code that reproduces on the machine it was written on and
    /// diverges elsewhere, because a heap's tie-breaking is not part of any framework's contract.
    /// The frontier is keyed to make every pop unique for exactly that reason; this is the check
    /// that the keying works.
    /// </remarks>
    [Fact]
    public void RoadsAreIdenticalAcrossRuns()
    {
        WorldState first = HistoryRun.Execute(TestWorlds.Small()).World;
        WorldState second = HistoryRun.Execute(TestWorlds.Small()).World;

        Assert.Equal(first.TradeRoutes.Count, second.TradeRoutes.Count);

        for (int i = 0; i < first.TradeRoutes.Count; i++)
        {
            Road? one = first.TradeRoutes[i].Road;
            Road? other = second.TradeRoutes[i].Road;

            if (one is null)
            {
                Assert.Null(other);
                continue;
            }

            Assert.NotNull(other);
            Assert.Equal(one.Grade, other!.Grade);
            Assert.Equal(one.BuiltYear, other.BuiltYear);
            Assert.Equal(one.Points, other.Points);
        }
    }

    /// <summary>Mean distance to the nearest river along a polyline, sampled evenly.</summary>
    private static double MeanRiverDistance(
        WorldState world, Hydrology hydrology, IReadOnlyList<RoadPoint> points)
    {
        double sum = 0.0;
        int count = 0;

        foreach (RoadPoint point in Densify(world, points))
        {
            sum += hydrology.RiverDistance(point.X, point.Z);
            count++;
        }

        return count == 0 ? 0.0 : sum / count;
    }

    /// <summary>
    /// Points every 32 units along a polyline, so two lines of different shape compare fairly.
    /// </summary>
    /// <remarks>
    /// Comparing the stored vertices alone would weight a line by how often it turns rather than by
    /// where it runs — a road with one corner and a road with eight would be judged on one and
    /// eight readings of the ground.
    /// </remarks>
    private static List<RoadPoint> Densify(WorldState world, IReadOnlyList<RoadPoint> points)
    {
        var dense = new List<RoadPoint>();

        for (int i = 1; i < points.Count; i++)
        {
            RoadPoint from = points[i - 1];
            RoadPoint to = points[i];
            int steps = Math.Max(1, (int)(world.Distance(from.X, from.Z, to.X, to.Z) / 32.0));

            for (int s = 0; s <= steps; s++)
            {
                double t = s / (double)steps;
                dense.Add(new RoadPoint(
                    (int)(from.X + ((to.X - from.X) * t)),
                    (int)(from.Z + ((to.Z - from.Z) * t))));
            }
        }

        return dense;
    }
}

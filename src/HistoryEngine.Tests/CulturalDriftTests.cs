using System.Collections.Generic;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Systems;
using HistoryEngine.World;
using Xunit;
using Xunit.Abstractions;

namespace HistoryEngine.Tests;

/// <summary>
/// The worlds the drift properties are measured over, simulated once for the whole suite.
/// </summary>
/// <remarks>
/// Four of these properties are read off the same five finished worlds, and a standard run is the
/// most expensive thing in this test project. Running them per-test would simulate twenty centuries
/// to assert four things about five. The worlds are only ever read here.
/// </remarks>
public sealed class DriftWorlds
{
    public static readonly ulong[] Seeds = { 2, 7, 11, 42, 99 };

    public DriftWorlds()
    {
        var worlds = new List<WorldState>(Seeds.Length);
        foreach (ulong seed in Seeds)
        {
            worlds.Add(HistoryRun.Execute(TestWorlds.Standard(seed)).World);
        }

        Worlds = worlds;
    }

    public IReadOnlyList<WorldState> Worlds { get; }
}

/// <summary>
/// The slow half of the disposition loop: a people's own baseline moving over the centuries.
/// </summary>
/// <remarks>
/// The failure this suite guards against is the same one <see cref="DispositionTests"/> names — a
/// mechanism that composes correctly and moves nothing. A drift system whose baselines never leave
/// their founding culture has cost a system in the order and changed no history. So the tests assert
/// the outcomes appear: realms move, contact is what moves them, the map does not collapse to one
/// culture, and war leaves its mark.
/// </remarks>
public sealed class CulturalDriftTests : IClassFixture<DriftWorlds>
{
    private readonly DriftWorlds _worlds;
    private readonly ITestOutputHelper _out;

    public CulturalDriftTests(DriftWorlds worlds, ITestOutputHelper output)
    {
        _worlds = worlds;
        _out = output;
    }

    /// <summary>How far a realm's baseline has moved from the culture it was founded as.</summary>
    private static double Drift(WorldState world, Civilization civilization) =>
        civilization.BaseValues.DistanceTo(world.CultureOf(civilization).Values);

    /// <summary>
    /// Distance on the four traits contact spreads — expansionism, tradition, mercantile, learning.
    /// </summary>
    /// <remarks>
    /// Aggression and piety are left out on purpose: those are what war and faith drive a realm's
    /// own way, so measuring convergence over them would mix the social pull with the two forces
    /// meant to pull against it.
    /// </remarks>
    private static double AdoptedDistance(CultureValues a, CultureValues b) =>
        System.Math.Abs(a.Expansionism - b.Expansionism)
        + System.Math.Abs(a.Tradition - b.Tradition)
        + System.Math.Abs(a.Mercantile - b.Mercantile)
        + System.Math.Abs(a.Learning - b.Learning);

    /// <summary>Baselines actually leave their founding culture. The inertness test.</summary>
    [Fact]
    public void PeoplesDriftFromWhatTheyWereFounded()
    {
        int realms = 0;
        int moved = 0;
        double sum = 0.0;
        double widest = 0.0;

        foreach (WorldState world in _worlds.Worlds)
        {
            foreach (Civilization civilization in world.ActiveCivilizations())
            {
                // DistanceTo is the mean per-dimension difference, in [0, 1]: 0.10 is a real shift.
                double drift = Drift(world, civilization);
                realms++;
                if (drift >= 0.10) moved++;
                sum += drift;
                widest = System.Math.Max(widest, drift);
            }
        }

        Assert.True(
            moved >= 15,
            $"Only {moved} of {realms} realms across {DriftWorlds.Seeds.Length} seeds drifted a tenth of the "
            + "way from their founding culture. The drift system is composing but not biting.");

        Assert.True(sum / realms > 0.08, $"Mean drift was only {sum / realms:F3} — barely moving.");

        // But nobody's baseline is unrecognisable — drift is generational, not a re-roll.
        Assert.True(widest < 0.6, $"A realm's baseline moved {widest:F2} of the [0, 1] range from founding.");
    }

    /// <summary>
    /// Contact converges: two peoples of different founding cultures who are neighbours and not at
    /// war end up more alike than their cultures began.
    /// </summary>
    /// <remarks>
    /// The central claim, and the one that separates drift from noise. The war and faith pulls move
    /// every realm whether it has neighbours or not, so a realm's distance from its own founding is
    /// the wrong thing to look at. The social pull's signature is that <em>neighbours grow alike</em>
    /// — measured only across different-culture friendly pairs, because same-culture pairs start
    /// identical and a hostile pair is meant to hold apart.
    /// </remarks>
    [Fact]
    public void NeighboursGrowAlike()
    {
        double foundingSum = 0.0;
        double endSum = 0.0;
        int pairs = 0;

        foreach (WorldState world in _worlds.Worlds)
        {
            var civilizations = new List<Civilization>(world.ActiveCivilizations());
            for (int i = 0; i < civilizations.Count; i++)
            {
                for (int j = i + 1; j < civilizations.Count; j++)
                {
                    Civilization a = civilizations[i];
                    Civilization b = civilizations[j];

                    // Different founding cultures, in contact, and not at war.
                    if (a.CultureId == b.CultureId) continue;
                    if (Diplomacy.Proximity(world, a, b) > Diplomacy.ContactRange) continue;
                    if (Diplomacy.AtWar(world, a.Id, b.Id)) continue;

                    foundingSum += AdoptedDistance(world.CultureOf(a).Values, world.CultureOf(b).Values);
                    endSum += AdoptedDistance(a.BaseValues, b.BaseValues);
                    pairs++;
                }
            }
        }

        _out.WriteLine(
            $"friendly cross-culture neighbour pairs {pairs}: founding apart "
            + $"{foundingSum / System.Math.Max(1, pairs):F3}, ended apart {endSum / System.Math.Max(1, pairs):F3}");

        Assert.True(pairs >= 10, $"Only {pairs} friendly cross-culture neighbour pairs to measure.");
        Assert.True(
            endSum < foundingSum,
            $"Friendly neighbours of different cultures did not grow alike: they began "
            + $"{foundingSum / pairs:F3} apart and ended {endSum / pairs:F3} apart.");
    }

    /// <summary>
    /// Convergence does not become homogenisation: peoples of different founding cultures stay
    /// recognisably distinct.
    /// </summary>
    /// <remarks>
    /// A pull toward the neighbours, left unchecked, ends with every realm holding one culture. The
    /// roots anchor is what stops it, and this is the assertion that it does: two peoples founded
    /// different remain measurably different at the end, even where they are neighbours. Measured
    /// only across different founding cultures — two realms of one culture growing alike is not
    /// homogenisation, it is the same people.
    /// </remarks>
    [Fact]
    public void DifferentCulturesStayDistinct()
    {
        double sum = 0.0;
        int pairs = 0;

        foreach (WorldState world in _worlds.Worlds)
        {
            var civilizations = new List<Civilization>(world.ActiveCivilizations());
            for (int i = 0; i < civilizations.Count; i++)
            {
                for (int j = i + 1; j < civilizations.Count; j++)
                {
                    if (civilizations[i].CultureId == civilizations[j].CultureId) continue;

                    sum += civilizations[i].BaseValues.DistanceTo(civilizations[j].BaseValues);
                    pairs++;
                }
            }
        }

        // DistanceTo is mean per-dimension; well above zero means the peoples have not merged.
        Assert.True(
            pairs > 0 && sum / pairs > 0.10,
            $"Realms of different founding cultures ended only {sum / System.Math.Max(1, pairs):F3} "
            + "apart on average. Convergence has homogenised the world.");
    }

    /// <summary>
    /// A long war leaves a people lastingly more warlike than the same people left at peace.
    /// </summary>
    /// <remarks>
    /// <para>The same world twice, and the only difference is what happened to one realm: a
    /// controlled experiment rather than a correlation over surviving realms, because a realm's
    /// fortunes have faded by the end of a run and the drift they caused has not.</para>
    ///
    /// <para>Only the drift system is ticked, so nothing else moves the world between the two runs
    /// — and the fortunes persist rather than fading, because the crown system that fades them is
    /// not running. That is the intended shape: a realm under sustained war stress.</para>
    /// </remarks>
    [Fact]
    public void WarLeavesAPeopleWarlike()
    {
        double atPeace = Aggression(warring: false);
        double atWar = Aggression(warring: true);

        Assert.True(
            atWar > atPeace + 0.05,
            $"A realm ground down by a century of war ended at aggression {atWar:F3}, against "
            + $"{atPeace:F3} for the same realm at peace. War is not marking the people.");
    }

    /// <summary>The first realm's aggression after a century of drift, with or without a war on.</summary>
    private static double Aggression(bool warring)
    {
        WorldState world = WorldBuilder.Create(TestWorlds.Standard(42));
        var drift = new CulturalDriftSystem();

        Civilization realm = world.Civilizations[0];

        if (warring)
        {
            // Not faded between years: the crown system does that, and it is not running here.
            realm.Fortunes.LostABattle();
            realm.Fortunes.TownSacked();
            realm.Fortunes.LandLost();
        }

        for (int year = 0; year < 100; year++)
        {
            drift.Tick(world, new Stamp(year, 0));
        }

        return realm.BaseValues.Aggression;
    }

    /// <summary>
    /// A culture is never written to — drift moves the realm's baseline, not its people's identity.
    /// </summary>
    /// <remarks>
    /// The same constitutional guarantee <see cref="DispositionTests.CulturesAreNotMovedByTheirRulers"/>
    /// makes for the ruler layer, restated for drift: <see cref="Culture.Values"/> is the immutable
    /// founding seed, and only <see cref="Civilization.BaseValues"/> moves.
    /// </remarks>
    [Fact]
    public void TheFoundingCultureIsNeverMoved()
    {
        WorldConfig config = TestWorlds.Standard(42);

        WorldState opening = WorldBuilder.Create(config);
        var founding = new List<CultureValues>();
        foreach (Culture culture in opening.Cultures) founding.Add(culture.Values);

        WorldState ended = HistoryRun.Execute(config).World;

        Assert.Equal(founding.Count, ended.Cultures.Count);
        for (int i = 0; i < founding.Count; i++)
        {
            Assert.Equal(founding[i], ended.Cultures[i].Values);
        }
    }
}

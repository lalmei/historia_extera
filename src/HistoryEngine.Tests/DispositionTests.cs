using HistoryEngine.Entities;
using HistoryEngine.World;
using Xunit;

namespace HistoryEngine.Tests;

/// <summary>
/// The reign-aware layer: a people, the person governing them, and what has lately happened.
/// </summary>
/// <remarks>
/// The failure this suite mostly guards against is not a crash but a feature that is present and
/// inert. Effective values that never differ from cultural ones produce a world identical to the
/// one before them, every test still passes, and the only symptom is that no chronicle ever reads
/// unlike another — which is exactly the condition the layer was built to fix.
/// </remarks>
public sealed class DispositionTests
{
    private static readonly ulong[] Seeds = { 2, 7, 11, 42, 99 };

    /// <summary>Every dial a realm is governed by stays a probability.</summary>
    /// <remarks>
    /// Three layers compose here, two of which can push in the same direction, and every consumer
    /// feeds the result straight into a <c>Lerp</c> or a <c>Chance</c>. A value outside [0, 1] is
    /// not a crash — it is a silently impossible or certain event.
    /// </remarks>
    [Fact]
    public void EffectiveValuesRemainProbabilities()
    {
        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Civilization civilization in world.Civilizations)
            {
                CultureValues values = world.ValuesFor(civilization);

                Assert.InRange(values.Aggression, 0.0, 1.0);
                Assert.InRange(values.Expansionism, 0.0, 1.0);
                Assert.InRange(values.Piety, 0.0, 1.0);
                Assert.InRange(values.Tradition, 0.0, 1.0);
                Assert.InRange(values.Mercantile, 0.0, 1.0);
                Assert.InRange(values.Learning, 0.0, 1.0);
            }
        }
    }

    /// <summary>
    /// Reigns actually move their realms, and not all in the same direction.
    /// </summary>
    /// <remarks>
    /// The inertness test. A layer that composes correctly and always lands within a hair of the
    /// culture's own value has cost a schema version and changed nothing.
    /// </remarks>
    [Fact]
    public void RealmsAreGovernedUnlikeTheirCultures()
    {
        int moved = 0;
        int upward = 0;
        int downward = 0;
        double widest = 0.0;

        foreach (ulong seed in Seeds)
        {
            WorldState world = HistoryRun.Execute(TestWorlds.Standard(seed)).World;

            foreach (Civilization civilization in world.ActiveCivilizations())
            {
                double cultural = world.CultureOf(civilization).Values.Aggression;
                double governed = world.ValuesFor(civilization).Aggression;
                double gap = governed - cultural;

                if (Math.Abs(gap) >= 0.10) moved++;
                if (gap >= 0.10) upward++;
                if (gap <= -0.10) downward++;

                widest = Math.Max(widest, Math.Abs(gap));
            }
        }

        Assert.True(
            moved >= 5,
            $"Only {moved} realms across {Seeds.Length} seeds were governed materially unlike "
            + "their culture. The reign layer is composing but not biting.");

        // Both directions, or the shifts are a one-way bias rather than a ruler's own character.
        Assert.True(upward > 0, "No realm was ever governed more aggressively than its people.");
        Assert.True(downward > 0, "No realm was ever governed less aggressively than its people.");

        // And nobody replaces their people wholesale. The cap is 0.6 of the distance to the
        // ruler's own value, and fortunes may add to that, but a full swing means a bug.
        Assert.True(widest < 0.85, $"A realm diverged from its culture by {widest:F2}.");
    }

    /// <summary>
    /// A culture is never written to. This is the constitutional guarantee.
    /// </summary>
    /// <remarks>
    /// Succession law derives from government form and <see cref="CultureValues.Tradition"/>. If a
    /// reign could move the culture's own values rather than only the realm's effective ones, a
    /// ruler with opinions would change how their own successor is chosen — agnatic one year and
    /// absolute the next — which is a constitutional change arriving as a side effect of a
    /// personality. The blend deliberately produces a new record rather than mutating one.
    /// </remarks>
    [Fact]
    public void CulturesAreNotMovedByTheirRulers()
    {
        WorldConfig config = TestWorlds.Standard(42);

        WorldState opening = WorldBuilder.Create(config);
        var laws = new List<SuccessionLaw>();
        var values = new List<CultureValues>();

        foreach (Culture culture in opening.Cultures)
        {
            laws.Add(culture.Succession);
            values.Add(culture.Values);
        }

        WorldState ended = HistoryRun.Execute(config).World;

        Assert.Equal(values.Count, ended.Cultures.Count);

        for (int i = 0; i < values.Count; i++)
        {
            Assert.Equal(values[i], ended.Cultures[i].Values);
            Assert.Equal(laws[i], ended.Cultures[i].Succession);
        }
    }

    /// <summary>A memory dims. Any fortune, however severe, fades to nothing given quiet years.</summary>
    [Fact]
    public void FortunesFadeToNothing()
    {
        var fortunes = new RealmFortunes();

        for (int i = 0; i < 20; i++)
        {
            fortunes.LostABattle();
            fortunes.TownSacked();
            fortunes.LandLost();
            fortunes.WonABattle();
            fortunes.Suffered(500, 1000);
        }

        Assert.Equal(1.0, fortunes.Weariness, 3);
        Assert.Equal(1.0, fortunes.Grievance, 3);

        // A century of quiet. Grievance is the slowest of the four by design, so it sets the bar.
        for (int year = 0; year < 100; year++) fortunes.Fade();

        Assert.True(fortunes.Weariness < 0.01, $"weariness {fortunes.Weariness}");
        Assert.True(fortunes.Calamity < 0.01, $"calamity {fortunes.Calamity}");
        Assert.True(fortunes.Triumph < 0.01, $"triumph {fortunes.Triumph}");
        Assert.True(fortunes.Grievance < 0.10, $"grievance {fortunes.Grievance}");
    }

    /// <summary>
    /// Weariness and grievance pull opposite ways, which is the reason both exist.
    /// </summary>
    /// <remarks>
    /// A single "war fatigue" scalar has to choose between "we lost, so we will never fight again"
    /// and "we lost, so we will fight until we have it back". Both are real; a model with one
    /// number can only express one of them, and this asserts the pair has not collapsed into that.
    /// </remarks>
    [Fact]
    public void BeingBeatenCalmsARealmAndBeingHumiliatedAngersIt()
    {
        var values = new CultureValues(0.5, 0.5, 0.5, 0.5, 0.5, 0.5);

        var beaten = new RealmFortunes();
        beaten.LostABattle();
        beaten.LostABattle();
        beaten.TownSacked();

        var aggrieved = new RealmFortunes();
        aggrieved.LandLost();
        aggrieved.LandLost();

        Assert.True(
            values.ShiftedBy(beaten).Aggression < values.Aggression,
            "A realm that has been bled should not press harder.");

        Assert.True(
            values.ShiftedBy(aggrieved).Aggression > values.Aggression,
            "A realm that has lost ground should want it back.");

        // And the realm too spent to fight turns to trade instead.
        Assert.True(values.ShiftedBy(beaten).Mercantile > values.Mercantile);
    }

    /// <summary>
    /// Catastrophe drives a people to the temple and stops them founding colonies.
    /// </summary>
    /// <remarks>
    /// Before this layer, a plague or a disaster had no consequence at all beyond the people it
    /// killed: the realm's behaviour the following spring was identical to a realm that had been
    /// spared. This is the assertion that the deaths now mean something to the survivors.
    /// </remarks>
    [Fact]
    public void CalamityTurnsARealmInwardAndUpward()
    {
        var values = new CultureValues(0.5, 0.5, 0.5, 0.5, 0.5, 0.5);

        var struck = new RealmFortunes();
        struck.Suffered(300, 2000);

        Assert.True(values.ShiftedBy(struck).Piety > values.Piety);
        Assert.True(values.ShiftedBy(struck).Expansionism < values.Expansionism);

        // What a people is, rather than how it feels this decade, does not move.
        Assert.Equal(values.Tradition, values.ShiftedBy(struck).Tradition);
        Assert.Equal(values.Learning, values.ShiftedBy(struck).Learning);
    }

    /// <summary>
    /// Rulers vary from their people in both directions and by a real amount.
    /// </summary>
    /// <remarks>
    /// The spread has to be wide against a narrow latitude rather than the reverse. Both produce
    /// the same average displacement, and only the first produces distinguishable people: a world
    /// of near-identical rulers each moving their realm a long way reads as noise with no author.
    /// </remarks>
    [Fact]
    public void DispositionsSpreadAroundTheirCulture()
    {
        WorldState world = HistoryRun.Execute(TestWorlds.Standard(42)).World;

        int above = 0;
        int below = 0;
        double widest = 0.0;

        foreach (Figure figure in world.Figures)
        {
            double cultural = world.CultureOf(figure).Values.Aggression;
            double gap = figure.Disposition.Values.Aggression - cultural;

            if (gap > 0.0) above++;
            if (gap < 0.0) below++;
            widest = Math.Max(widest, Math.Abs(gap));
        }

        Assert.True(above > 100 && below > 100, $"above {above}, below {below}");
        Assert.True(widest > 0.4, $"widest divergence from culture was only {widest:F2}");
    }
}

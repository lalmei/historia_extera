using System.Globalization;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Ages rulers, kills them, and crowns their successors.
/// </summary>
/// <remarks>
/// Deliberately coarse: one ruler per civilization, arriving as an adult with no family. That
/// is the brief's choice — simulating every citizen is what makes a history generator take
/// minutes instead of seconds — and Milestone 5 replaces it with dynasties, marriages and
/// heirs traced through a family tree.
///
/// <para>The mortality curve is the one thing worth getting roughly right even now, because it
/// sets the rhythm of the whole chronicle. Rulers who never die produce a history of nothing
/// but settlement growth; rulers who die too often produce noise. A curve that is flat through
/// adulthood and steepens after fifty gives reigns of a plausible length, which means
/// succession events land at a readable rate.</para>
/// </remarks>
public sealed class FigureLifecycleSystem : IYearSystem
{
    public string Name => "figure-lifecycle";

    public void Tick(WorldState world, int year)
    {
        IRng rng = world.Root.Fork(Name, year);

        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            Culture culture = world.CultureOf(civilization);

            // An interregnum — first year, or a ruler lost with no successor yet.
            if (civilization.CurrentRulerId.IsNone)
            {
                EnsureCapital(world, civilization, year);
                WorldBuilder.CrownNewRuler(world, civilization, culture, year, rng);
                continue;
            }

            Figure ruler = world.Figures[civilization.CurrentRulerId];
            if (!ruler.IsAlive) continue;

            int age = ruler.AgeIn(year);
            if (!rng.Chance(AnnualMortality(age))) continue;

            Die(world, ruler, year, age >= 55 ? DeathCause.OldAge : DeathCause.Illness);

            civilization.CurrentRulerId = EntityId.None;
            EnsureCapital(world, civilization, year);
            WorldBuilder.CrownNewRuler(world, civilization, culture, year, rng);
        }
    }

    /// <summary>
    /// Yearly chance of death at a given age.
    /// </summary>
    /// <remarks>
    /// Low and flat through adulthood, then rising quadratically past fifty-five and certain by
    /// the late nineties. Quadratic rather than the exponential a real Gompertz curve would use,
    /// because <c>Math.Exp</c> is not guaranteed bit-identical across runtimes and a single
    /// differing ULP next to this comparison would fork the entire history — see
    /// <see cref="DetMath"/>.
    /// </remarks>
    public static double AnnualMortality(int age)
    {
        if (age < 20) return 0.004;
        if (age < 55) return 0.009;

        double t = DetMath.InverseLerp(55.0, 97.0, age);
        return DetMath.Clamp(DetMath.Lerp(0.012, 0.85, t * t), 0.0, 1.0);
    }

    private static void Die(WorldState world, Figure figure, int year, DeathCause cause)
    {
        figure.DeathYear = year;
        figure.DeathCause = cause;
        figure.EndCurrentTitle(year);

        world.Chronicle.Record(
            year,
            EventKind.FigureDied,
            figure.Id,
            obj: figure.CivilizationId,
            data: Chronicle.Data(
                ("age", figure.AgeIn(year).ToString(CultureInfo.InvariantCulture)),
                ("cause", CauseLabel(cause))));
    }

    /// <summary>
    /// Repoints a civilization's capital if the current one is gone.
    /// </summary>
    /// <remarks>
    /// A coronation names the place it happened, so a civilization whose capital has been
    /// abandoned needs a new seat before the next ruler can be crowned there.
    /// </remarks>
    private static void EnsureCapital(WorldState world, Civilization civilization, int year)
    {
        bool capitalStands = !civilization.CapitalId.IsNone
            && world.Settlements[civilization.CapitalId].IsActive;

        if (capitalStands) return;

        Settlement? replacement = null;
        foreach (Settlement candidate in world.ActiveSettlementsOf(civilization))
        {
            // Largest surviving settlement, id breaking ties.
            if (replacement is null
                || candidate.Population > replacement.Population
                || (candidate.Population == replacement.Population
                    && candidate.Id.CompareTo(replacement.Id) < 0))
            {
                replacement = candidate;
            }
        }

        if (replacement is null) return;

        if (!civilization.CapitalId.IsNone && world.Settlements.Contains(civilization.CapitalId))
        {
            world.Settlements[civilization.CapitalId].IsCapital = false;
        }

        replacement.IsCapital = true;
        civilization.CapitalId = replacement.Id;

        world.Chronicle.Record(
            year,
            EventKind.CapitalMoved,
            civilization.Id,
            location: replacement.Id);
    }

    private static string CauseLabel(DeathCause cause) => cause switch
    {
        DeathCause.OldAge => "old age",
        DeathCause.Illness => "illness",
        DeathCause.Battle => "wounds taken in battle",
        DeathCause.Assassination => "assassination",
        DeathCause.Accident => "misadventure",
        DeathCause.Execution => "execution",
        _ => "unknown causes",
    };
}

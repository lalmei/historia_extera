using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Ages everyone the chronicle is following, and kills them.
/// </summary>
/// <remarks>
/// <para>Before Milestone 5 this system aged one ruler per civilization and crowned their
/// replacement in the same breath, because a ruler was the only person who existed. Now it ages
/// every member of every house — children, cadets, consorts — and crowning has moved to
/// <see cref="SuccessionSystem"/>, which runs immediately after it. That split is what makes a
/// death and the succession it causes two separate events written by two systems, rather than one
/// system quietly doing both.</para>
///
/// <para><b>The mortality curve sets the rhythm of the whole chronicle</b>, and now that children
/// are in it the young end matters as much as the old one. A curve that is flat from birth means
/// every heir born survives to inherit, so no house ever fails and no throne ever passes sideways;
/// the succession machinery would be correct and never exercised. Pre-modern infant mortality is
/// what makes an heir predeceasing their father — the engine of most interesting successions —
/// happen at a believable rate.</para>
/// </remarks>
public sealed class FigureLifecycleSystem : IYearSystem
{
    public string Name => "figure-lifecycle";

    public void Tick(WorldState world, int year)
    {
        IRng rng = world.Root.Fork(Name, year);

        // Id order, which is birth order — stable regardless of who is currently near a throne.
        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive) continue;

            int age = figure.AgeIn(year);
            if (age < 0 || !rng.Chance(AnnualMortality(age))) continue;

            Houses.Die(world, figure, year, CauseAt(age));
        }
    }

    /// <summary>
    /// Yearly chance of death at a given age.
    /// </summary>
    /// <remarks>
    /// Steep through infancy, low and flat through adulthood, then rising quadratically past
    /// fifty-five and near-certain by the late nineties. Quadratic rather than the exponential a
    /// real Gompertz curve would use, because <c>Math.Exp</c> is not guaranteed bit-identical
    /// across runtimes and a single differing ULP next to this comparison would fork the entire
    /// history — see <see cref="DetMath"/>.
    ///
    /// <para>The infant figures are deliberately harsh: roughly a fifth in the first year and a
    /// quarter before five, which is where pre-modern populations actually sat. Softening them
    /// makes every royal nursery produce a surplus of adult heirs and the line of succession stops
    /// mattering.</para>
    ///
    /// <para>The adult rate is the dial that sets reign length, and through it the whole event
    /// volume of a chronicle. At 1.3% a year someone who reaches twenty can expect roughly another
    /// forty, which puts a typical reign at thirty-odd years and — more to the point — makes a
    /// ruler predeceasing their heir's majority happen often enough that regencies are a feature of
    /// the history rather than a curiosity in it.</para>
    /// </remarks>
    public static double AnnualMortality(int age)
    {
        if (age < 1) return 0.19;
        if (age < 5) return 0.055;
        if (age < 15) return 0.012;
        if (age < 55) return 0.013;

        double t = DetMath.InverseLerp(55.0, 97.0, age);
        return DetMath.Clamp(DetMath.Lerp(0.012, 0.85, t * t), 0.0, 1.0);
    }

    /// <summary>
    /// What the record says carried someone off.
    /// </summary>
    /// <remarks>
    /// Age alone, with no roll: a chronicle that attributes a two-year-old's death to old age
    /// reads as a bug even when the simulation underneath is correct.
    /// </remarks>
    private static DeathCause CauseAt(int age) => age >= 55 ? DeathCause.OldAge : DeathCause.Illness;
}

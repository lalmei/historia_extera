using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Swears soldiers in, and raises the ones a realm has a place for.
/// </summary>
/// <remarks>
/// <para><b>The other half of <see cref="OfficeSystem"/>.</b> That one fills the seats a court
/// decides; this one fills the rungs an army decides, which is nearly everybody the seats never
/// reach. Of the figures who take to arms, one in a generation is ever made marshal — the rest had
/// no military history at all beyond the battles they happened to be drawn to, and a chronicle
/// that follows a soldier for forty years and cannot say he was promoted is not following him.</para>
///
/// <para><b>Runs after the offices</b>, so a marshal seated this spring is already standing at the
/// top of the ladder when the establishment is counted, and the captain who would otherwise have
/// been raised into that place waits for the seat to empty. The reverse order gives a realm two
/// commanders in the year it appoints one.</para>
///
/// <para><b>And after the war</b>, by a long way, which is the point: renown earned at a battle
/// this summer is a qualification by the autumn, so a hard campaign visibly produces officers in
/// the same year it produces casualties. See <see cref="Ranks.PromotionOdds"/>.</para>
///
/// <para>One stream per realm per year, and one fork per soldier within it, so how many soldiers a
/// realm has cannot shift what another realm's army does and a soldier born this year cannot
/// reshuffle the promotions of everyone already serving.</para>
///
/// <para>Samples no terrain.</para>
/// </remarks>
public sealed class MilitarySystem : ISystem
{
    public string Name => "ranks";

    public Cadence Cadence => Cadence.Annual;

    public void Tick(WorldState world, Stamp now)
    {
        int year = now.Year;

        IRng rng = world.Root.Fork(Name, year);

        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            Culture culture = world.CultureOf(civilization);
            IRng host = rng.Fork("host", civilization.Id.ToDiscriminator());

            List<Figure> muster = Ranks.Muster(world, civilization, year);
            if (muster.Count == 0) continue;

            foreach (Figure soldier in muster)
            {
                Ranks.Enlist(world, civilization, culture, soldier, year);
            }

            Promote(world, civilization, culture, muster, year, host);
        }
    }

    /// <summary>
    /// Raises whoever is due, best first, into whatever places the realm has.
    /// </summary>
    /// <remarks>
    /// <para><b>From the top rung down.</b> A vacancy at the top is what opens the one below it,
    /// so filling upward would leave a captaincy standing empty for a year every time a commander
    /// died — and would fill it with a serjeant raised before the captain above him had moved.</para>
    ///
    /// <para><b>Best first, not oldest first.</b> Where several soldiers are due for one place it
    /// goes to the one the field has noticed — see <see cref="Ranks.Merit"/>, which records what
    /// offering the places in id order did to this model. Each candidate still has to make his own
    /// yearly roll, so a realm does not promote its whole officer corps the year a war ends; a
    /// soldier who fails it leaves the place for the next man rather than holding it.</para>
    ///
    /// <para>The establishment is re-counted at every rung rather than once at the top, since a
    /// raise into a rung fills a place at every rung below it as well.</para>
    /// </remarks>
    private static void Promote(
        WorldState world,
        Civilization civilization,
        Culture culture,
        List<Figure> muster,
        int year,
        IRng host)
    {
        for (MilitaryRank rung = Ranks.Top; rung >= MilitaryRank.Soldier; rung--)
        {
            int places = Ranks.Establishment(rung, muster.Count)
                - Ranks.Standing(muster, rung);
            if (places <= 0) continue;

            List<(Figure Soldier, double Merit)> due = Due(muster, rung, year, host);
            if (due.Count == 0) continue;

            foreach ((Figure soldier, double _) in due)
            {
                if (places <= 0) break;

                IRng board = host.Fork("promotion", soldier.Id.ToDiscriminator());
                if (!board.Chance(Ranks.PromotionOdds(soldier, year))) continue;

                Ranks.Raise(
                    world,
                    civilization,
                    culture,
                    soldier,
                    rung,
                    Ranks.Citation(world, soldier, soldier.CurrentRank!.Year),
                    year);
                places--;
            }
        }
    }

    /// <summary>
    /// Everyone of a muster who is due for this rung, best first.
    /// </summary>
    /// <remarks>
    /// Sorted by merit and then by id, which is a total order — so the queue does not depend on the
    /// order the muster happened to be walked in. Merit is drawn once per soldier before the sort
    /// rather than inside the comparison, which would draw it a varying number of times depending
    /// on how the sort partitioned the list.
    /// </remarks>
    private static List<(Figure Soldier, double Merit)> Due(
        List<Figure> muster, MilitaryRank rung, int year, IRng host)
    {
        var due = new List<(Figure Soldier, double Merit)>();
        foreach (Figure soldier in muster)
        {
            if (soldier.CurrentRank is null) continue;
            if (Ranks.Next(soldier.Rank) != rung) continue;
            if (Ranks.PromotionOdds(soldier, year) <= 0.0) continue;

            due.Add((soldier, Ranks.Merit(soldier, host)));
        }

        due.Sort((left, right) =>
        {
            int byMerit = right.Merit.CompareTo(left.Merit);
            return byMerit != 0 ? byMerit : left.Soldier.Id.CompareTo(right.Soldier.Id);
        });

        return due;
    }
}

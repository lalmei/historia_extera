using HistoryEngine.Core;

namespace HistoryEngine.World;

/// <summary>
/// How long a year is, and how many parts it has.
/// </summary>
/// <remarks>
/// <para>Twelve months of thirty. A season is then ninety days and both divisions are exact, which
/// is the whole reason for the number: a calendar whose seasons do not divide its year makes every
/// seasonal rate a rounding argument, and there is no astronomical fidelity to be bought here that
/// would pay for one.</para>
///
/// <para><b>Config, and therefore hashed.</b> The calendar decides how many steps a year has and
/// how far anything gets in one of them, so two worlds that count their days differently are two
/// different histories and must not claim the same provenance. It is folded into
/// <see cref="WorldConfig.ConfigHash"/> only when it is not this default — see the reasoning
/// there.</para>
///
/// <para><b>A season is local, not global.</b> Nothing here says when spring is. This world already
/// has latitude-driven temperature and a region knows its own climate, so the campaigning season is
/// a property of the ground being fought over rather than of the world clock — winter in the north
/// is high summer in the south. This type only says how the year is divided.</para>
///
/// <para><b>Phase 3.</b> Vintage Story configures its year as months × days-per-month, and its
/// default year is materially shorter than 360 days. The point of <see cref="DaysPerYear"/> being a
/// field is that matching the game's calendar is configuration rather than a change to the
/// model.</para>
/// </remarks>
public sealed record Calendar(int DaysPerYear = 360, int SeasonsPerYear = 4)
{
    /// <summary>The calendar every world has unless it says otherwise.</summary>
    public static readonly Calendar Default = new();

    /// <summary>Exact by construction — see <see cref="Validate"/>.</summary>
    public int DaysPerSeason => DaysPerYear / SeasonsPerYear;

    /// <summary>
    /// Days elapsed since year zero, day zero.
    /// </summary>
    /// <remarks>
    /// <para>Returns <see cref="long"/> rather than <see cref="int"/> because the multiplication is
    /// the one place in this type where a plausible configuration can overflow: a world starting at
    /// year ten million on a 360-day calendar passes 2³¹ before it has simulated anything. The
    /// cost of the wider type is nothing, and the cost of the overflow would be a docket that
    /// silently sorts backwards.</para>
    ///
    /// <para>A <see cref="Stamp"/> whose day has run past the end of its year is answered honestly
    /// rather than rejected — <c>(year 3, day 400)</c> lands in year four — because "forty days
    /// from now" is exactly the arithmetic that produces one, and the docket that consumes this
    /// needs it to sort where it belongs rather than where its year field says.</para>
    /// </remarks>
    public long AbsoluteDay(Stamp stamp) => ((long)stamp.Year * DaysPerYear) + stamp.Day;

    /// <summary>
    /// The stamp <paramref name="days"/> after <paramref name="from"/>, with the year carried.
    /// </summary>
    /// <remarks>
    /// <para><b>Scheduling arithmetic must go through here rather than adding to
    /// <see cref="Stamp.Day"/>.</b> <see cref="AbsoluteDay"/> deliberately answers a day past the
    /// end of its year honestly, so a docket sorts <c>(year 3, day 400)</c> where its days put it —
    /// but that tolerance is for the queue's ordering, not for the record. An event stamped
    /// <c>(3, 400)</c> claims to have happened in year three when it happened in year four, and the
    /// chronicle then appends it after year four's events while saying it is older than them.</para>
    ///
    /// <para>Found exactly that way: chaining a plague's next step off the last one overflowed the
    /// day within a couple of years, and six tests failed on consequences of the wrong year rather
    /// than on the arithmetic — a reign outlasting its holder, land held by a realm that had ended.
    /// </para>
    /// </remarks>
    public Stamp Plus(Stamp from, int days)
    {
        long absolute = AbsoluteDay(from) + days;

        return new Stamp((int)(absolute / DaysPerYear), (int)(absolute % DaysPerYear));
    }

    public void Validate()
    {
        if (DaysPerYear <= 0)
        {
            throw new InvalidOperationException("DaysPerYear must be positive.");
        }

        if (SeasonsPerYear <= 0)
        {
            throw new InvalidOperationException("SeasonsPerYear must be positive.");
        }

        if (DaysPerYear % SeasonsPerYear != 0)
        {
            throw new InvalidOperationException(
                $"A year of {DaysPerYear} days does not divide into {SeasonsPerYear} seasons. " +
                "Seasons must be whole days, or every seasonal rate in the engine becomes an " +
                "argument about rounding.");
        }
    }
}

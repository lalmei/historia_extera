using System.Diagnostics;
using System.Globalization;

namespace HistoryEngine.Core;

/// <summary>
/// A point in simulated time: a year, and the day within it.
/// </summary>
/// <remarks>
/// <para>The year stays the spine of this engine — the unit of the harvest, of growth, of a
/// figure's age — and <see cref="Day"/> is the detail a year cannot hold. It exists so that
/// something can be dated, and so that something can be scheduled for forty days out without forty
/// ticks being run to reach it.</para>
///
/// <para><b>Two integers rather than one absolute day.</b> An absolute day would make comparison a
/// single subtraction, and it would also send every existing call site through a calendar to
/// recover the number it already had: <c>AgeIn(year)</c>, <c>QualityAt(region, year)</c>, the year
/// index the viewer's timeline is built on. Keeping the year directly addressable is what lets the
/// great majority of the engine go on reading in years while the parts that need a date acquire
/// one. The absolute day is derived where it is actually needed, by <see cref="World.Calendar"/>,
/// which is the only thing that knows how long a year is.</para>
///
/// <para>Ordering is <see cref="Year"/> then <see cref="Day"/>, deliberately independent of any
/// calendar: two stamps compare the same way whatever <c>DaysPerYear</c> is, so a comparison can
/// never disagree with itself because two worlds count their days differently. The one place that
/// wants the calendar's arithmetic instead is <see cref="World.Docket"/>, and it says why.</para>
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
public readonly record struct Stamp(int Year, int Day) : IComparable<Stamp>
{
    /// <summary>The first day of a year.</summary>
    /// <remarks>
    /// Every stamp in the engine is this until seasons land: a system ticked once a year has
    /// nowhere finer to claim it acted, and claiming the middle of the year would be inventing a
    /// date the model has not earned.
    /// </remarks>
    public static Stamp Opening(int year) => new(year, 0);

    public int CompareTo(Stamp other)
    {
        int byYear = Year.CompareTo(other.Year);
        return byYear != 0 ? byYear : Day.CompareTo(other.Day);
    }

    public static bool operator <(Stamp left, Stamp right) => left.CompareTo(right) < 0;

    public static bool operator <=(Stamp left, Stamp right) => left.CompareTo(right) <= 0;

    public static bool operator >(Stamp left, Stamp right) => left.CompareTo(right) > 0;

    public static bool operator >=(Stamp left, Stamp right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Compact form for debugger windows and assertion messages, as <c>year.day</c>.
    /// </summary>
    /// <remarks>
    /// Formatted invariantly rather than by string concatenation, which would take the negative
    /// sign and the digits from the current culture — the back door <c>DetMap</c> already documents
    /// for string comparison, arriving through <see cref="int.ToString()"/> instead.
    /// </remarks>
    public override string ToString() =>
        Year.ToString(CultureInfo.InvariantCulture) + "." + Day.ToString(CultureInfo.InvariantCulture);
}

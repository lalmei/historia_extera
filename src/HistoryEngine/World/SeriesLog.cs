using HistoryEngine.Core;

namespace HistoryEngine.World;

/// <summary>How a measure should be read: a headcount, or a dial in [0, 1].</summary>
public enum MeasureUnit
{
    Count,
    Fraction,
}

/// <summary>
/// One thing worth sampling every year, described well enough for a viewer to plot it blind.
/// </summary>
/// <remarks>
/// <see cref="Group"/> is what lets four measures share one chart without the viewer knowing
/// what any of them mean — the same trick the narration templates play with event kinds. A
/// measure added in a later milestone appears as a curve with no viewer change at all.
/// </remarks>
/// <param name="Name">Its key in the export, and the label a viewer falls back to.</param>
/// <param name="Group">Measures sharing a group are drawn together, or empty to stand alone.</param>
public sealed record Measure(string Name, string Group, MeasureUnit Unit);

/// <summary>
/// Every measure that moves, sampled once a year.
/// </summary>
/// <remarks>
/// <para><b>Why the engine records these rather than the viewer deriving them.</b> Territory and
/// congregation are replayed from the chronicle, and that is sound because the engine guarantees
/// the event log reproduces them — <c>TerritoryTests</c> fails the build the day it stops being
/// true. Nothing guarantees the same for a realm's mood or its population. Fortunes are written
/// by whichever system caused them, and reconstructing them outside the engine means copying the
/// decay constants across a language boundary to produce a second opinion about what happened.
/// Sampling the number the simulation actually used is cheaper and cannot disagree.</para>
///
/// <para><b>One contiguous run per measure, and only while there is something to measure.</b> A
/// realm's series stops in the year it fell rather than trailing zeroes that claim it was still
/// there; a settlement's stops when it is abandoned. What that costs is a per-series start year,
/// which is a great deal cheaper than the years it saves carrying.</para>
///
/// <para><b>Not a snapshot of the world per year.</b> That is the thing the timeline replay
/// exists to avoid, and this is not it: eleven numbers per living realm, one per standing
/// settlement and one per open route, against a thousand regions and every entity they contain.
/// The rule for adding to it is that a measure earns a track by moving — anything fixed at
/// worldgen is already in the export as a field, and a flat line is not history.</para>
/// </remarks>
public sealed class SeriesLog
{
    private readonly List<Series> _series = new();
    private readonly Dictionary<(EntityId, string), Series> _byKey = new();

    /// <summary>Every track, in the order it was first recorded.</summary>
    /// <remarks>
    /// Insertion order rather than a sort: recording walks the entity tables, which are dense and
    /// therefore already in id order, so the export's byte layout follows from the tables without
    /// a comparer anyone has to keep correct.
    /// </remarks>
    public IReadOnlyList<Series> All => _series;

    /// <summary>
    /// Records one reading. Called at most once per subject, per measure, per year.
    /// </summary>
    /// <remarks>
    /// A repeated year overwrites rather than appends, so a caller that samples twice cannot
    /// silently slide a whole series a year out of true.
    /// </remarks>
    public void Record(EntityId entity, Measure measure, int year, double value)
    {
        if (!_byKey.TryGetValue((entity, measure.Name), out Series? series))
        {
            series = new Series(entity, measure, year);
            _byKey.Add((entity, measure.Name), series);
            _series.Add(series);
        }

        series.Write(year, value);
    }

    /// <summary>One measure of one entity, year by year.</summary>
    public sealed class Series
    {
        private readonly List<double> _values = new();

        internal Series(EntityId entity, Measure measure, int fromYear)
        {
            Entity = entity;
            Measure = measure;
            FromYear = fromYear;
        }

        public EntityId Entity { get; }

        public Measure Measure { get; }

        /// <summary>The year <see cref="Values"/> starts at. One entry per year from there.</summary>
        public int FromYear { get; }

        public IReadOnlyList<double> Values => _values;

        internal void Write(int year, double value)
        {
            int index = year - FromYear;

            if (index < _values.Count)
            {
                _values[index] = value;
                return;
            }

            // A year nobody sampled holds the last reading rather than reading as a zero. Gaps
            // are not expected — nothing here comes back to life once it stops being sampled —
            // but inventing a collapse is a worse way to find out than repeating a number.
            while (_values.Count < index) _values.Add(_values[^1]);

            _values.Add(value);
        }
    }
}

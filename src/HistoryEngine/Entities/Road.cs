namespace HistoryEngine.Entities;

/// <summary>How much has been spent on the ground a route runs over.</summary>
/// <remarks>
/// Explicit values — part of the export format. Two grades and not more: the difference that
/// earns its place is whether the way was merely worn or was engineered, because only the second
/// buys the right to cut through country the first has to walk around. A third tier would have to
/// change the line it cuts to be worth exporting, and nothing in the model would make it.
/// </remarks>
public enum RoadGrade
{
    /// <summary>A worn way. It goes where the walking is easy, however far round that is.</summary>
    Track = 0,

    /// <summary>An engineered road: cuttings through broken ground, bridges over the fords.</summary>
    Paved = 1,
}

/// <summary>One vertex of a road, in world coordinates.</summary>
/// <remarks>
/// A local struct rather than <c>Terrain.Point2</c> so that <c>Entities</c> keeps depending on
/// <c>Core</c> alone. The entity layer is what the export and the viewer read; it should not have
/// to know that terrain exists in order to record where a road runs.
/// </remarks>
public readonly record struct RoadPoint(int X, int Z);

/// <summary>
/// The physical way a trade route takes over the ground.
/// </summary>
/// <remarks>
/// <para><b>Attached to a route, never a route of its own.</b> The commercial relationship is the
/// entity with a history; this is a fact about how that relationship is physically served. So an
/// upgrade replaces the path and keeps <see cref="BuiltYear"/> — the route's id, its founding, its
/// traffic record and its chronicle are untouched by anything that happens to the surface.</para>
///
/// <para><b>Computed once, when it is built.</b> A road costs a path search when construction
/// happens and nothing at all in the years between, which is the same bargain a settlement's exact
/// coordinate makes. Recomputing per tick would put a graph search inside the yearly loop for a
/// line that has not moved.</para>
///
/// <para>The polyline runs from one settlement's exact coordinate to the other's, with a vertex
/// wherever the way changes direction. Straight runs carry no interior points, so a road across
/// open country is two points and not forty.</para>
/// </remarks>
public sealed class Road
{
    public Road(
        IReadOnlyList<RoadPoint> points,
        RoadGrade grade,
        int builtYear,
        int? pavedYear,
        double length)
    {
        Points = points;
        Grade = grade;
        BuiltYear = builtYear;
        PavedYear = pavedYear;
        Length = length;
    }

    /// <summary>The way itself, endpoint to endpoint, in world coordinates.</summary>
    public IReadOnlyList<RoadPoint> Points { get; }

    public RoadGrade Grade { get; }

    /// <summary>The year the first way was cut. Survives an upgrade.</summary>
    public int BuiltYear { get; }

    /// <summary>The year the way was engineered, if it ever was.</summary>
    public int? PavedYear { get; }

    /// <summary>Length along the polyline in world units — longer than the direct distance.</summary>
    public double Length { get; }
}

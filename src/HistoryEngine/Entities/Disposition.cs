using HistoryEngine.Core;

namespace HistoryEngine.Entities;

/// <summary>
/// One person's own inclinations: the dials their culture has, as they hold them.
/// </summary>
/// <remarks>
/// <para>Every figure gets one; it matters only for those who come to govern. Before this, a
/// realm's decisions were read entirely off its culture, which is fixed at worldgen and never
/// changes — so a warlike people declared war at the same rate in its first century and its
/// ninth, under thirty different rulers, having won every war or lost every one. No reign read
/// unlike the reign before it except by accident of dice.</para>
///
/// <para><b>Rolled around the culture's own values, not freely.</b> A free roll makes a line of
/// succession read as uncorrelated noise; an anchored one reads as "this people centralises, and
/// this king unusually so", which is the sentence worth being able to write. The spread is
/// deliberately wide and the latitude a ruler is given over their realm deliberately narrow —
/// both together set how far a reign can drift, and a wide spread against a small latitude
/// produces distinct rulers who move their realms a little, where the reverse produces
/// indistinguishable rulers who move them a lot.</para>
///
    /// <para><see cref="Independence"/> is the follower–rebel axis: how far this person lets that
    /// culture actually govern their choices. Followers are the common case and rebels the tail,
    /// because a people that produced them in equal number would not hold together. A child's
    /// occupation is chosen from a blend of people and person.</para>
/// </remarks>
public sealed record Disposition(CultureValues Values, double Centralism, double Independence = 0.5)
{
    /// <summary>
    /// Inclinations that pull nowhere. The default on <see cref="Figure.Disposition"/>.
    /// </summary>
    /// <remarks>
    /// A null-object rather than a nullable field: a ruler with no disposition would otherwise be
    /// a case every consumer has to test for, and the answer in each one is "behave as the culture
    /// does", which is exactly what blending toward the midpoint at low latitude produces.
    /// </remarks>
    public static readonly Disposition Neutral =
        new(new CultureValues(0.5, 0.5, 0.5, 0.5, 0.5, 0.5), 0.5, 0.5);

    /// <summary>How far a person's dial may sit from their culture's, before clamping.</summary>
    private const double Spread = 0.5;

    /// <summary>How far a person's <see cref="Centralism"/> may sit from their government's norm.</summary>
    private const double CentralismSpread = 0.25;

    /// <summary>Lowest independence anyone is raised with — still a follower, not a blank.</summary>
    private const double IndependenceFloor = 0.04;

    /// <summary>How far the rebel tail can reach, in a restless people and a customary one.</summary>
    private const double IndependenceRangeRestless = 0.78;

    private const double IndependenceRangeCustomary = 0.48;

    /// <summary>
    /// Rolls inclinations for someone born into <paramref name="culture"/>.
    /// </summary>
    /// <param name="rng">
    /// Must be forked on the figure's own id, so a person's character cannot depend on how many
    /// people were born before them.
    /// </param>
    /// <param name="faith">
    /// The teaching they were raised in, if any. Colours the roll without consuming it — see
    /// <see cref="TintedBy"/>.
    /// </param>
    public static Disposition Roll(Culture culture, IRng rng, FaithCharacter? faith = null)
    {
        CultureValues theirs = culture.Values;

        var rolled = new Disposition(
            new CultureValues(
                Aggression: Dial(theirs.Aggression, rng),
                Expansionism: Dial(theirs.Expansionism, rng),
                Piety: Dial(theirs.Piety, rng),
                Tradition: Dial(theirs.Tradition, rng),
                Mercantile: Dial(theirs.Mercantile, rng),
                Learning: Dial(theirs.Learning, rng)),
            Centralism: DetMath.Clamp01(
                CentralismNorm(culture.Government)
                + rng.NextDouble(-CentralismSpread, CentralismSpread)),
            Independence: RollIndependence(culture, rng));

        return faith is null ? rolled : rolled.TintedBy(faith);
    }

    /// <summary>
    /// This person's inclinations as their faith leaves them.
    /// </summary>
    /// <remarks>
    /// A thumb on the scale, not a replacement. The pull is weighted by the piety already
    /// rolled, so a worldly person keeps more of their cultural character and a devout one is
    /// shaped by what they were taught. No further dice: the faith's teaching is a fact of the
    /// faith, and how far it takes this person is a fact of the person.
    /// </remarks>
    public Disposition TintedBy(FaithCharacter faith)
    {
        double pull = 0.16 + (Values.Piety * 0.24);

        return new Disposition(
            Values.BlendToward(faith.Inclines(), pull),
            DetMath.Clamp01(DetMath.Lerp(Centralism, faith.OfficeInclination(), pull)),
            Independence);
    }

    /// <summary>
    /// This person's own reading of the culture's dials, pulled as far as they will go.
    /// </summary>
    /// <remarks>
    /// <see cref="Independence"/> is the blend: a follower keeps what their people hold, a rebel
    /// answers with their own inclinations. Occupation is chosen this way, so the dial is not a
    /// label on a page.
    /// </remarks>
    public CultureValues Decides(CultureValues cultural) =>
        cultural.BlendToward(Values, Independence);

    private static double Dial(double cultural, IRng rng) =>
        DetMath.Clamp01(cultural + rng.NextDouble(-Spread, Spread));

    /// <summary>
    /// How much a given office inclines its holder to decide things personally.
    /// </summary>
    /// <remarks>
    /// Government form rather than culture, because this is about what machinery a ruler has to
    /// hand rather than what their people are like. A chief has few instruments to appoint with
    /// and a monarch or hierarch has many; a republic distributes decisions by construction, and
    /// a consul who wants to centralise is fighting his own constitution.
    /// </remarks>
    public static double CentralismNorm(GovernmentForm government) => government switch
    {
        GovernmentForm.Chiefdom => 0.35,
        GovernmentForm.Monarchy => 0.60,
        GovernmentForm.Theocracy => 0.65,
        GovernmentForm.Oligarchy => 0.40,
        GovernmentForm.Republic => 0.30,
        _ => 0.45,
    };

    /// <summary>
    /// How independently a person of this culture is raised, on average.
    /// </summary>
    /// <remarks>
    /// The mean of the skewed roll, not a centre people are scattered around. Tradition is the
    /// pressure to conform, so a customary people sits further toward the follower end; this is
    /// where the tick on a figure's dial belongs.
    /// </remarks>
    public static double IndependenceNorm(Culture culture) =>
        IndependenceFloor + (IndependenceRange(culture) / 3.0);

    /// <summary>
    /// One person's independence: most of the mass toward the follower end, a long tail of rebels.
    /// </summary>
    /// <remarks>
    /// A uniform scatter around a midpoint would make rebels as common as followers, which is
    /// not how a people holds together. Squaring the roll is the cheapest skew that stays
    /// bit-identical — no transcendental — and still reaches a real rebel in a restless culture.
    /// </remarks>
    private static double RollIndependence(Culture culture, IRng rng)
    {
        double roll = rng.NextDouble();
        return DetMath.Clamp01(IndependenceFloor + (IndependenceRange(culture) * roll * roll));
    }

    private static double IndependenceRange(Culture culture) =>
        DetMath.Lerp(IndependenceRangeRestless, IndependenceRangeCustomary, culture.Values.Tradition);
}

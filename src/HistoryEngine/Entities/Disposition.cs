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
/// </remarks>
public sealed record Disposition(CultureValues Values, double Centralism)
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
        new(new CultureValues(0.5, 0.5, 0.5, 0.5, 0.5, 0.5), 0.5);

    /// <summary>How far a person's dial may sit from their culture's, before clamping.</summary>
    private const double Spread = 0.5;

    /// <summary>How far a person's <see cref="Centralism"/> may sit from their government's norm.</summary>
    private const double CentralismSpread = 0.25;

    /// <summary>
    /// Rolls inclinations for someone born into <paramref name="culture"/>.
    /// </summary>
    /// <param name="rng">
    /// Must be forked on the figure's own id, so a person's character cannot depend on how many
    /// people were born before them.
    /// </param>
    public static Disposition Roll(Culture culture, IRng rng)
    {
        CultureValues theirs = culture.Values;

        return new Disposition(
            new CultureValues(
                Aggression: Dial(theirs.Aggression, rng),
                Expansionism: Dial(theirs.Expansionism, rng),
                Piety: Dial(theirs.Piety, rng),
                Tradition: Dial(theirs.Tradition, rng),
                Mercantile: Dial(theirs.Mercantile, rng),
                Learning: Dial(theirs.Learning, rng)),
            Centralism: DetMath.Clamp01(
                CentralismNorm(culture.Government)
                + rng.NextDouble(-CentralismSpread, CentralismSpread)));
    }

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
}

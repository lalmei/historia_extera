using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// The slow half of the disposition loop: a people's own values moving over the centuries.
/// </summary>
/// <remarks>
/// <para><b>What this closes.</b> A ruler bends a realm's values for a reign (<c>CrownSystem</c>) and
/// its recent past shifts them for a decade (<c>RealmFortunes</c>), but until now the baseline both
/// worked from — <see cref="Civilization.BaseValues"/> — never moved, so a people was exactly what it
/// was founded as under thirty rulers and eight wars. This is where a people itself changes: a
/// conquered or trade-linked realm drifts toward its neighbours, a war-worn one militarises, a devout
/// realm's piety follows its faith, and a realm with nobody in reach stays where it began.</para>
///
/// <para><b>Four pulls, each toward a target, so none can run away.</b> Every term is a small
/// fraction of the distance to something, clamped to [0, 1] — never a sum that has to be clipped.
/// The rate is the point: at a few percent of the gap a year, drift is a matter of generations, not
/// reigns.</para>
///
/// <list type="bullet">
/// <item><description><b>Social.</b> For each realm this one is in contact with — its own
/// <see cref="Civilization.Relations"/>, which are exactly the realms it has met — pull every value
/// toward that neighbour's baseline, weighted by how near they are
/// (<see cref="Diplomacy.Pressure"/>) and how large (population, taken as a square root so a giant
/// does not simply overwrite a small neighbour). Contact converges by default (<see cref="ContactBias"/>)
/// and only an active war reverses it into a people defining itself against its enemy. Normalised by
/// the total weight, so the step is bounded however many neighbours there are. An isolated realm has
/// almost no pull and keeps its founding character — which is how two realms of one culture come to
/// differ: not because the isolated one moves, but because the connected one does.</description></item>
/// <item><description><b>Roots.</b> A weak pull back toward the founding culture, so convergence is
/// partial. Without it a crowded map ends as one culture; with it the equilibrium sits partway and a
/// frontier becomes a region of related-but-distinct peoples.</description></item>
/// <item><description><b>War.</b> Aggression eases toward a war target set by sustained weariness and
/// grievance: a realm ground down by a long war turns lastingly warlike, a realm long at rest eases
/// back toward a moderate temperament rather than toward zero.</description></item>
/// <item><description><b>Faith.</b> A realm with a state religion has its piety pulled toward that
/// faith's own fervour.</description></item>
/// </list>
///
/// <para><b>Reads state, draws no random numbers, samples no terrain</b> — the same as
/// <c>CrownSystem</c>. It runs late in the year, after diplomacy, war, trade and religion have left
/// this year's relations, wars and faiths in place, and writes the baseline that next year's crown
/// reads first. Applied collect-then-apply so a realm drifts against the world as this year left it,
/// not against neighbours already moved earlier in the same tick.</para>
/// </remarks>
public sealed class CulturalDriftSystem : ISystem
{
    /// <summary>The most of the gap to its neighbours a realm closes in a year.</summary>
    /// <remarks>A few percent: convergence with a time constant of a generation, not a reign.</remarks>
    private const double SocialRate = 0.03;

    /// <summary>
    /// How convergent plain contact is, before opinion is added in.
    /// </summary>
    /// <remarks>
    /// Culture spreads down a shared frontier and a trade road whether or not the two realms are
    /// fond of each other — most neighbours in this engine hold each other in mild suspicion, and if
    /// only warm opinion converged them, contact would drive peoples apart, which is the opposite of
    /// what a frontier does. So contact is convergent by default and opinion only tilts it; an
    /// <em>active war</em> is what reverses it into a people defining itself against its enemy.
    /// </remarks>
    private const double ContactBias = 0.4;

    /// <summary>How fast aggression follows the war target.</summary>
    private const double WarRate = 0.02;

    /// <summary>Where aggression eases to under a lasting peace — moderate, not pacifist.</summary>
    private const double PeaceAggression = 0.35;

    /// <summary>How fast a people's piety follows its state faith's fervour.</summary>
    private const double FaithRate = 0.02;

    /// <summary>
    /// How strongly a people is held to the culture it was founded as.
    /// </summary>
    /// <remarks>
    /// Convergence with no counterweight ends every realm in reach of another holding one identical
    /// culture — a small densely-settled world becomes a monoculture in a few centuries. A people
    /// drifts toward its neighbours but does not forget what it is: this anchor pulls it back toward
    /// its founding, so the equilibrium sits partway between the two and different foundings stay
    /// distinguishable. It is why a frontier of many realms becomes a region of related-but-distinct
    /// cultures rather than one.
    /// </remarks>
    private const double RootsRate = 0.006;

    public string Name => "cultural-drift";

    public Cadence Cadence => Cadence.Annual;

    public void Tick(WorldState world, Stamp now)
    {
        // Collect-then-apply: every realm drifts against the same snapshot, so the result does not
        // depend on which realm the loop reaches first.
        var civilizations = new List<Civilization>(world.ActiveCivilizations());
        var drifted = new CultureValues[civilizations.Count];

        for (int i = 0; i < civilizations.Count; i++)
        {
            drifted[i] = Drifted(world, civilizations[i], now.Year);
        }

        for (int i = 0; i < civilizations.Count; i++)
        {
            civilizations[i].BaseValues = drifted[i];
        }
    }

    /// <summary>This realm's baseline after one year of drift.</summary>
    private static CultureValues Drifted(WorldState world, Civilization civilization, int year)
    {
        CultureValues b = civilization.BaseValues;

        // Diplomacy resolved who is within reach of whom earlier this year, and proximity — every
        // settlement against every settlement — is the most expensive question in the engine. Read
        // its answer where there is one; compute our own where the system list has no diplomacy.
        DetMap<EntityId, double>? reach = world.ReachOf(civilization.Id, year);

        double dAggression = 0.0;
        double dExpansionism = 0.0;
        double dPiety = 0.0;
        double dTradition = 0.0;
        double dMercantile = 0.0;
        double dLearning = 0.0;

        // ---- Social: the weighted pull of the realms this one is actually in contact with ----
        double totalWeight = 0.0;
        foreach (KeyValuePair<EntityId, double> relation in civilization.Relations)
        {
            if (!world.Civilizations.Contains(relation.Key)) continue;

            Civilization other = world.Civilizations[relation.Key];
            if (!other.IsActive || other.Population <= 0) continue;

            // Persisted-but-distant relations fall to zero pressure and drop out on their own, so a
            // realm that has drifted out of reach stops pulling without needing to be removed. A
            // pair the reach map does not name is beyond contact, which is that same zero.
            double proximity = reach is not null
                ? reach.GetOrDefault(relation.Key, double.PositiveInfinity)
                : Diplomacy.Proximity(world, civilization, other);

            double pressure = Diplomacy.Pressure(proximity);
            if (pressure <= 0.0) continue;

            double affinity = Diplomacy.AtWar(world, civilization.Id, other.Id)
                ? -1.0
                : DetMath.Clamp(ContactBias + relation.Value, -1.0, 1.0);

            double weight = pressure * DetMath.Sqrt(other.Population);
            double signed = weight * affinity;
            totalWeight += weight;

            CultureValues o = other.BaseValues;
            dAggression += signed * (o.Aggression - b.Aggression);
            dExpansionism += signed * (o.Expansionism - b.Expansionism);
            dPiety += signed * (o.Piety - b.Piety);
            dTradition += signed * (o.Tradition - b.Tradition);
            dMercantile += signed * (o.Mercantile - b.Mercantile);
            dLearning += signed * (o.Learning - b.Learning);
        }

        if (totalWeight > 0.0)
        {
            double k = SocialRate / totalWeight;
            dAggression *= k;
            dExpansionism *= k;
            dPiety *= k;
            dTradition *= k;
            dMercantile *= k;
            dLearning *= k;
        }

        // ---- Roots: the founding culture a people never quite forgets, so convergence is
        // partial and a crowded frontier keeps its distinct-but-related cultures ----
        CultureValues roots = world.CultureOf(civilization).Values;
        dAggression += RootsRate * (roots.Aggression - b.Aggression);
        dExpansionism += RootsRate * (roots.Expansionism - b.Expansionism);
        dPiety += RootsRate * (roots.Piety - b.Piety);
        dTradition += RootsRate * (roots.Tradition - b.Tradition);
        dMercantile += RootsRate * (roots.Mercantile - b.Mercantile);
        dLearning += RootsRate * (roots.Learning - b.Learning);

        // ---- War: a realm worn down by war turns lastingly warlike; peace eases it back ----
        RealmFortunes fortunes = civilization.Fortunes;
        double warStress = DetMath.Clamp01(fortunes.Weariness + (0.5 * fortunes.Grievance));
        double warTarget = DetMath.Lerp(PeaceAggression, 1.0, warStress);
        dAggression += WarRate * (warTarget - b.Aggression);

        // ---- Faith: the state religion pulls a people's piety toward its own fervour ----
        if (world.Religions.Contains(civilization.StateReligionId))
        {
            double fervour = world.Religions[civilization.StateReligionId].Fervour;
            dPiety += FaithRate * (fervour - b.Piety);
        }

        return new CultureValues(
            DetMath.Clamp01(b.Aggression + dAggression),
            DetMath.Clamp01(b.Expansionism + dExpansionism),
            DetMath.Clamp01(b.Piety + dPiety),
            DetMath.Clamp01(b.Tradition + dTradition),
            DetMath.Clamp01(b.Mercantile + dMercantile),
            DetMath.Clamp01(b.Learning + dLearning));
    }
}

using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Exceptional personal deaths whose causes are neither biological mortality nor a mass event.
/// </summary>
/// <remarks>
/// <para><b>A cause is provenance, not decoration.</b> This system does not replace some share of
/// deaths rolled by <see cref="FigureLifecycleSystem"/> with more colourful labels. It makes the
/// hazardous event itself, and whoever survives it remains available to the ordinary mortality
/// pass later in the year.</para>
///
/// <para><b>Political violence needs a political target.</b> Rulers, regents and the strongest
/// adult resident claimant are exposed; remote cousins are not. The chance rises with the court's
/// aggression and while the realm is at war. No culprit is named because the model has no evidence
/// or intrigue state from which to choose one honestly.</para>
///
/// <para><b>Accidents are broad and rare.</b> Adult figures may die by misadventure, with travel and
/// martial cultures somewhat more exposed. A separate per-figure random stream means adding a
/// claimant to one court cannot change another person's fate.</para>
/// </remarks>
public sealed class FigureIncidentSystem : IYearSystem
{
    private const double PoliticalRiskFloor = 0.0010;
    private const double PoliticalRiskFromAggression = 0.0025;
    private const double WartimePoliticalMultiplier = 1.25;
    private const double RegentRiskMultiplier = 1.25;
    private const double ClaimantRiskMultiplier = 0.55;

    private const double PoisoningFloor = 0.25;
    private const double PoisoningFromRestraint = 0.35;

    private const double AccidentRisk = 0.00065;

    private static readonly string[] AccidentDetails =
    {
        "a riding accident",
        "a hunting accident",
        "a fall",
        "a fire",
        "drowning at sea",
    };

    public string Name => "figure-incidents";

    public void Tick(WorldState world, int year)
    {
        IRng rng = world.Root.Fork(Name, year);

        PoliticalViolence(world, year, rng);
        Accidents(world, year, rng);
    }

    private static void PoliticalViolence(WorldState world, int year, IRng rng)
    {
        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            Culture culture = world.CultureOf(civilization);
            List<Figure> claimants = Succession.Claimants(
                world, civilization, culture, EntityId.None);

            Figure? claimant = null;
            foreach (Figure candidate in claimants)
            {
                if (candidate.CivilizationId != civilization.Id) continue;
                if (candidate.AgeIn(year) < Succession.MajorityAge) continue;

                claimant = candidate;
                break;
            }

            // Without a credible adult alternative there is no court faction with anything to gain.
            if (claimant is null) continue;

            double risk = PoliticalRiskFloor
                + (PoliticalRiskFromAggression * culture.Values.Aggression);

            if (AtWar(world, civilization.Id)) risk *= WartimePoliticalMultiplier;

            IRng court = rng.Fork("court", civilization.Id.ToDiscriminator());

            if (world.Figures.Contains(civilization.CurrentRulerId))
            {
                Attempt(
                    world,
                    world.Figures[civilization.CurrentRulerId],
                    year,
                    risk,
                    culture,
                    court);
            }

            if (world.Figures.Contains(civilization.RegentId))
            {
                Attempt(
                    world,
                    world.Figures[civilization.RegentId],
                    year,
                    risk * RegentRiskMultiplier,
                    culture,
                    court);
            }

            Attempt(
                world,
                claimant,
                year,
                risk * ClaimantRiskMultiplier,
                culture,
                court);
        }
    }

    private static void Attempt(
        WorldState world,
        Figure target,
        int year,
        double risk,
        Culture culture,
        IRng court)
    {
        if (!target.IsAlive || target.AgeIn(year) < Succession.MajorityAge) return;

        IRng fate = court.Fork("target", target.Id.ToDiscriminator());
        if (!fate.Chance(DetMath.Clamp01(risk))) return;

        double poisonChance = PoisoningFloor
            + (PoisoningFromRestraint * (1.0 - culture.Values.Aggression));

        DeathCause cause = fate.Chance(poisonChance)
            ? DeathCause.Poisoning
            : DeathCause.Assassination;

        Houses.Die(world, target, year, cause);
    }

    private static void Accidents(WorldState world, int year, IRng rng)
    {
        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive || figure.AgeIn(year) < Succession.MajorityAge) continue;
            if (!world.Civilizations.Contains(figure.CivilizationId)) continue;

            Civilization civilization = world.Civilizations[figure.CivilizationId];
            if (!civilization.IsActive) continue;

            Culture culture = world.CultureOf(figure);
            double exposure = (culture.Values.Aggression + culture.Values.Mercantile) * 0.5;
            double risk = AccidentRisk * DetMath.Lerp(0.70, 1.40, exposure);

            IRng fate = rng.Fork("accident", figure.Id.ToDiscriminator());
            if (!fate.Chance(risk)) continue;

            Houses.Die(world, figure, year, DeathCause.Accident, fate.Pick(AccidentDetails));
        }
    }

    private static bool AtWar(WorldState world, EntityId civilizationId)
    {
        foreach (War war in world.ActiveWars())
        {
            if (war.Involves(civilizationId)) return true;
        }

        return false;
    }
}

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
public sealed class FigureIncidentSystem : ISystem
{
    private const double PoliticalRiskFloor = 0.0010;
    private const double PoliticalRiskFromAggression = 0.0025;
    private const double WartimePoliticalMultiplier = 1.25;
    private const double RegentRiskMultiplier = 1.25;
    private const double ClaimantRiskMultiplier = 0.55;

    private const double PoisoningFloor = 0.25;
    private const double PoisoningFromRestraint = 0.35;

    private const double AccidentRisk = 0.00065;

    /// <summary>Years after a disgrace during which a court may still settle accounts.</summary>
    private const int GrudgeYears = 5;

    private const double DisgraceRiskFloor = 0.012;

    private const double DisgraceRiskFromAggression = 0.045;

    private static readonly string[] AccidentDetails =
    {
        "a riding accident",
        "a hunting accident",
        "a fall",
        "a fire",
        "drowning at sea",
    };

    public string Name => "figure-incidents";

    public Cadence Cadence => Cadence.Annual;

    public void Tick(WorldState world, Stamp now)
    {
        int year = now.Year;

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

            Reckoning(world, civilization, year, culture, court);
        }
    }

    /// <summary>
    /// A court settling accounts with someone it lately stripped of an office.
    /// </summary>
    /// <remarks>
    /// <para>Political violence needed a political target, and before offices the only one this
    /// engine had was a claimant — so <see cref="DeathCause.Execution"/> was reachable exclusively
    /// by losing a succession. A marshal dismissed while the war was going badly, or a governor
    /// whose town emptied under him, is a man with enemies and no position, which is the other
    /// way people historically ended up on a scaffold.</para>
    ///
    /// <para>Bounded to the years just after the disgrace. A court that executes a man it dismissed
    /// forty years ago is not settling accounts, it is holding a grudge nothing in this model
    /// represents.</para>
    /// </remarks>
    private static void Reckoning(
        WorldState world, Civilization civilization, int year, Culture culture, IRng court)
    {
        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive || figure.CivilizationId != civilization.Id) continue;
            if (figure.DisgracedYear is not int disgraced) continue;
            if (year - disgraced > GrudgeYears) continue;

            IRng fate = court.Fork("reckoning", figure.Id.ToDiscriminator());

            double risk = DisgraceRiskFloor
                + (DisgraceRiskFromAggression * world.ValuesFor(civilization).Aggression);

            if (!fate.Chance(DetMath.Clamp01(risk))) continue;

            Houses.Die(
                world, figure, year, DeathCause.Execution, "for the loss of their office");
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

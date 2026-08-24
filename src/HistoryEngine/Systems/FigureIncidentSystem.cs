using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.World;

namespace HistoryEngine.Systems;

/// <summary>
/// Exceptional personal hazards whose causes are neither biological mortality nor a mass event.
/// </summary>
/// <remarks>
/// <para><b>A cause is provenance, not decoration.</b> This system does not replace some share of
/// deaths rolled by <see cref="FigureLifecycleSystem"/> with more colourful labels. It makes the
/// hazardous event itself, and whoever survives it remains available to the ordinary mortality
/// pass later in the year.</para>
///
/// <para><b>Political violence develops through conspiracies and quarrels.</b> Bonds, grievances,
/// access and undertakings determine who joins a plot, how it advances and whether the court
/// exposes it; the same bonds carry personal disputes up the ladder from a grudge to a meeting, or
/// back down to a settlement. Which of the two an anger can take is decided by rank — see
/// <see cref="World.Disputes"/>. This system only schedules both processes and resolves later
/// consequences for disgraced or accused residents.</para>
///
/// <para><b>Accidents are broad and rare.</b> Adult figures may die by misadventure, with travel and
/// martial cultures somewhat more exposed. A separate per-figure random stream means adding a
/// claimant to one court cannot change another person's fate.</para>
/// </remarks>
public sealed class FigureIncidentSystem : ISystem
{
    private const double AccidentRisk = 0.00065;

    /// <summary>Years after a disgrace during which a court may still settle accounts.</summary>
    private const int GrudgeYears = 5;

    private const double DisgraceRiskFloor = 0.012;

    private const double DisgraceRiskFromAggression = 0.045;

    /// <summary>
    /// Higher than disgrace: the court already named them, so following through is the common
    /// end of an accusation rather than a rare one.
    /// </summary>
    private const double AccusedRiskFloor = 0.04;

    private const double AccusedRiskFromAggression = 0.10;

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
        Undertakings.Tick(world, year);
        Conspiracies.Tick(world, year, rng.Fork("conspiracies"));
        Disputes.Tick(world, year);

        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            Culture culture = world.CultureOf(civilization);
            IRng court = rng.Fork("court", civilization.Id.ToDiscriminator());
            Reckoning(world, civilization, year, culture, court);
            Accusations(world, civilization, year, court);
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

    /// <summary>
    /// A court that named someone in a murder may later put them to death for it.
    /// </summary>
    /// <remarks>
    /// Only residents. A foreign ruler named as the hand behind a wartime poisoning is a
    /// diplomatic accusation, not a man this scaffold can reach.
    /// </remarks>
    private static void Accusations(
        WorldState world, Civilization civilization, int year, IRng court)
    {
        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive || figure.CivilizationId != civilization.Id) continue;
            if (figure.AccusedYear is not int accused) continue;
            if (year - accused > GrudgeYears) continue;

            IRng fate = court.Fork("accused", figure.Id.ToDiscriminator());

            double risk = AccusedRiskFloor
                + (AccusedRiskFromAggression * world.ValuesFor(civilization).Aggression);

            if (!fate.Chance(DetMath.Clamp01(risk))) continue;

            string detail = "for the murder";
            if (world.Figures.Contains(figure.AccusedOfId))
            {
                detail = "for the death of " + world.Figures[figure.AccusedOfId].FullName;
            }

            Houses.Die(world, figure, year, DeathCause.Execution, detail);
        }
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

    private static readonly string[] AccidentDetails =
    {
        "a riding accident",
        "a hunting accident",
        "a fall",
        "a fire",
        "drowning at sea",
    };

}

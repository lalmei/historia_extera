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
/// aggression and while the realm is at war. A murder names a suspect only when the court already
/// had someone with something to gain — the claimant, a lately disgraced officer, or an enemy
/// ruler in a war being fought — and even then the chronicle sometimes records unknown hands.
/// Living spouse, parents, children and siblings are indexed on the death so it appears in their
/// own record, and they carry a blood-debt that keeps the same plot dangerous to them for a few
/// years. A named suspect at the same court may later be executed for it.</para>
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

    /// <summary>
    /// How much of the court's murderousness falls on the household of someone already slain.
    /// </summary>
    /// <remarks>
    /// Lower than a claimant's, because finishing a family is rarer than striking once. High
    /// enough that a widow or an heir can die of the same plot, which is the whole reason the
    /// blood-debt exists.
    /// </remarks>
    private const double KinRiskMultiplier = 0.35;

    private const double PoisoningFloor = 0.25;
    private const double PoisoningFromRestraint = 0.35;

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

    /// <summary>How often a murder the court has evidence for actually names someone.</summary>
    private const double NameSuspectChance = 0.65;

    private static readonly string[] AssassinationDetails =
    {
        "a knife in the dark",
        "a blade at court",
        "unknown hands in the palace",
        "an ambush on the road",
    };

    private static readonly string[] PoisoningDetails =
    {
        "poison in the cup",
        "poison at the feast",
        "a draught prepared for them",
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
            List<Figure> suspects = Suspects(world, civilization, claimant, year);

            if (world.Figures.Contains(civilization.CurrentRulerId))
            {
                Attempt(
                    world,
                    world.Figures[civilization.CurrentRulerId],
                    year,
                    risk,
                    culture,
                    court,
                    suspects);
            }

            if (world.Figures.Contains(civilization.RegentId))
            {
                Attempt(
                    world,
                    world.Figures[civilization.RegentId],
                    year,
                    risk * RegentRiskMultiplier,
                    culture,
                    court,
                    suspects);
            }

            Attempt(
                world,
                claimant,
                year,
                risk * ClaimantRiskMultiplier,
                culture,
                court,
                suspects);

            BloodDebt(world, civilization, year, risk * KinRiskMultiplier, culture, court, suspects);
            Reckoning(world, civilization, year, culture, court);
            Accusations(world, civilization, year, court);
        }
    }

    /// <summary>
    /// People this court already has a reason to name, not a cast drawn from the whole realm.
    /// </summary>
    /// <remarks>
    /// The claimant benefits if the sitting person dies; a lately disgraced officer has a
    /// grievance the court itself created; an enemy ruler is already at war. Naming anyone else
    /// would be flavour in the shape of evidence.
    /// </remarks>
    private static List<Figure> Suspects(
        WorldState world, Civilization civilization, Figure claimant, int year)
    {
        var suspects = new List<Figure>();
        var seen = new bool[world.Figures.Count];

        void Consider(Figure? figure)
        {
            if (figure is null || !figure.IsAlive) return;
            if (figure.AgeIn(year) < Succession.MajorityAge) return;
            if (seen[figure.Id.Index]) return;

            seen[figure.Id.Index] = true;
            suspects.Add(figure);
        }

        Consider(claimant);

        foreach (Figure figure in world.Figures)
        {
            if (figure.CivilizationId != civilization.Id) continue;
            if (figure.DisgracedYear is not int disgraced) continue;
            if (year - disgraced > GrudgeYears) continue;

            Consider(figure);
        }

        foreach (War war in world.ActiveWars())
        {
            if (!war.Involves(civilization.Id)) continue;

            EntityId otherId = war.AggressorId == civilization.Id ? war.DefenderId : war.AggressorId;
            if (!world.Civilizations.Contains(otherId)) continue;

            EntityId enemyRuler = world.Civilizations[otherId].CurrentRulerId;
            if (world.Figures.Contains(enemyRuler)) Consider(world.Figures[enemyRuler]);
        }

        suspects.Sort((a, b) => a.Id.CompareTo(b.Id));
        return suspects;
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
    /// The household of someone already murdered, while the plot is still warm.
    /// </summary>
    private static void BloodDebt(
        WorldState world,
        Civilization civilization,
        int year,
        double risk,
        Culture culture,
        IRng court,
        List<Figure> suspects)
    {
        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive || figure.CivilizationId != civilization.Id) continue;
            if (figure.KinMurderedYear is not int slain) continue;
            if (year - slain > GrudgeYears) continue;

            Attempt(world, figure, year, risk, culture, court, suspects);
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

    private static void Attempt(
        WorldState world,
        Figure target,
        int year,
        double risk,
        Culture culture,
        IRng court,
        List<Figure>? suspects = null)
    {
        if (!target.IsAlive || target.AgeIn(year) < Succession.MajorityAge) return;

        IRng fate = court.Fork("target", target.Id.ToDiscriminator());
        if (!fate.Chance(DetMath.Clamp01(risk))) return;

        double poisonChance = PoisoningFloor
            + (PoisoningFromRestraint * (1.0 - culture.Values.Aggression));

        DeathCause cause = fate.Chance(poisonChance)
            ? DeathCause.Poisoning
            : DeathCause.Assassination;

        string detail = cause == DeathCause.Poisoning
            ? fate.Pick(PoisoningDetails)
            : fate.Pick(AssassinationDetails);

        Figure? suspect = NameSuspect(target, year, fate, suspects);
        List<Figure> family = Succession.ImmediateFamily(world, target);
        var extra = new List<EntityId>(family.Count + 1);
        var data = new DetMap<string, string>();

        foreach (Figure kin in family)
        {
            extra.Add(kin.Id);

            // The named hand is not also the household the plot now endangers.
            if (suspect is null || kin.Id != suspect.Id) kin.KinMurderedYear = year;
        }

        if (suspect is not null)
        {
            extra.Add(suspect.Id);
            suspect.AccusedYear = year;
            suspect.AccusedOfId = target.Id;
            world.NamePerson(data, "suspect", suspect.Id);
        }

        Houses.Die(world, target, year, cause, detail, extra, data);

        if (world.Civilizations.Contains(target.CivilizationId))
        {
            world.Civilizations[target.CivilizationId].Fortunes.MurderAtCourt();
        }
    }

    private static Figure? NameSuspect(
        Figure target, int year, IRng fate, List<Figure>? suspects)
    {
        if (suspects is null || suspects.Count == 0) return null;
        if (!fate.Chance(NameSuspectChance)) return null;

        var candidates = new List<Figure>(suspects.Count);

        foreach (Figure figure in suspects)
        {
            if (!figure.IsAlive || figure.Id == target.Id) continue;
            if (figure.AgeIn(year) < Succession.MajorityAge) continue;

            candidates.Add(figure);
        }

        return candidates.Count == 0 ? null : fate.Pick(candidates);
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

    private static bool AtWar(WorldState world, EntityId civilizationId)
    {
        foreach (War war in world.ActiveWars())
        {
            if (war.Involves(civilizationId)) return true;
        }

        return false;
    }
}

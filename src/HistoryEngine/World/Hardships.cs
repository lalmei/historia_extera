using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.World;

/// <summary>Which kind of bad year reached a town.</summary>
/// <remarks>
/// Split by how it arrives rather than by what caused it, because that is what decides the shape of
/// the consequence. A famine and a plague are slow: they are survived or they are not, and there is
/// nothing to be wounded by. A sack and an earthquake are sudden: they break people who live
/// through them, which is what the wound lifecycle is for.
/// </remarks>
public enum HardshipKind
{
    Famine = 0,
    Plague = 1,
    Sack = 2,
    Disaster = 3,
}

/// <summary>One recorded episode, waiting for the year's journeys to be known.</summary>
/// <param name="Severity">Share of the settlement's people the episode cost.</param>
/// <param name="About">The battle for a sack; the settlement for everything else.</param>
public readonly record struct PendingHardship(
    EntityId SettlementId,
    HardshipKind Kind,
    double Severity,
    int Year,
    EventKind Source,
    EntityId About,
    IReadOnlyList<EntityId>? Extra);

/// <summary>
/// The join between a town's bad year and the recorded people standing in it.
/// </summary>
/// <remarks>
/// <para>Famine, plague, disaster, sack and siege were all modelled as facts about settlements. The
/// people living in those settlements were not told: a scribe could be resident through four plague
/// years and carry nothing but a bereavement from a decade earlier. This type is the missing join,
/// and it deliberately invents nothing — the episodes, the residence, the memory model, the wound
/// lifecycle and the central death path all already existed.</para>
///
/// <para><b>It is not a second mortality model.</b> Population loss stays where it is. Two of the
/// four families already kill residents through their own path — a sack through
/// <c>Warfare.ResidentCasualties</c>, a disaster through <c>DisasterSystem.CourtCasualties</c> —
/// and this pass runs after them, sees the dead as dead, and does not roll them again. What it adds
/// for those two is what happens to everyone who lived: a wound, or the memory of it. For famine
/// and plague, which reached no recorded person at all, it adds the whole chain.</para>
///
/// <para><b>Severity is one scale across all four.</b> Every caller already computes the share of
/// the town's people the episode cost, so that share is what is passed in; a bad year and a
/// catastrophic one cannot produce the same thing, and a worse episode can never produce a lower
/// risk for an otherwise identical resident.</para>
/// </remarks>
public static class Hardships
{
    /// <summary>Below this share of a town lost, an episode leaves no mark on anybody.</summary>
    /// <remarks>
    /// A settlement that lost one person in fifty had a bad year, not a formative one. Without a
    /// floor the commonest famine in the world — the one that just clears the chronicle's own
    /// recording bar — would put a memory on every resident of every affected town, which is the
    /// failure mode this system most needs to avoid.
    /// </remarks>
    private const double Floor = 0.04;

    /// <summary>Ceiling on any one resident's chance of dying in one episode.</summary>
    private const double MaxMortality = 0.14;

    /// <summary>Ceiling on any one resident's chance of being hurt in one episode.</summary>
    private const double MaxInjury = 0.22;

    /// <summary>
    /// Lets one recorded episode reach the recorded people who were living through it.
    /// </summary>
    /// <param name="severity">
    /// Share of the settlement's people the episode cost, which every caller already has.
    /// </param>
    /// <param name="source">The event kind the chronicle wrote, carried onto the memory.</param>
    /// <param name="about">
    /// The entity the memory names. The settlement for a famine, plague or disaster; the battle for
    /// a sack, so the memory points at the storming rather than at the town in general.
    /// </param>
    public static void Record(
        WorldState world,
        Settlement settlement,
        HardshipKind kind,
        double severity,
        int year,
        EventKind source,
        EntityId about = default,
        IReadOnlyList<EntityId>? extra = null)
    {
        severity = DetMath.Clamp01(severity);
        if (severity < Floor) return;
        if (!settlement.IsActive) return;

        world.PendingHardships.Add(new PendingHardship(
            settlement.Id, kind, severity, year, source, about, extra));
    }

    /// <summary>
    /// Resolves every episode recorded this year, once the year's journeys are known.
    /// </summary>
    /// <remarks>
    /// Drained in the order the episodes were recorded, which is the order the systems that write
    /// them run in. Each episode's dice hang off its own settlement, year and family rather than
    /// off its position in this list, so a famine recorded before a sack cannot change what the
    /// sack did.
    /// </remarks>
    public static void ResolveYear(WorldState world, int year)
    {
        foreach (PendingHardship pending in world.PendingHardships)
        {
            if (!world.Settlements.Contains(pending.SettlementId)) continue;
            Endure(world, world.Settlements[pending.SettlementId], pending, year);
        }

        world.PendingHardships.Clear();
    }

    private static void Endure(
        WorldState world, Settlement settlement, PendingHardship pending, int year)
    {
        HardshipKind kind = pending.Kind;
        double severity = pending.Severity;
        EventKind source = pending.Source;
        IReadOnlyList<EntityId>? extra = pending.Extra;
        EntityId subject = pending.About.IsNone ? settlement.Id : pending.About;

        // Forked from the world root rather than from the caller's stream, so that reaching the
        // residents cannot shift the sequence the episode itself was drawn from. Keyed on the
        // settlement, the year and the family, which is what makes an episode: two plagues cannot
        // reach the same town in the same year, and a famine and a sack in one year are separate
        // things that happened to the same people.
        IRng episode = world.Root
            .Fork("hardship", settlement.Id.ToDiscriminator())
            .Fork("year", year)
            .Fork("kind", (int)kind);

        foreach (Figure figure in Residents(world, settlement, year))
        {
            IRng fate = episode.Fork("figure", figure.Id.ToDiscriminator());

            if (Kills(kind))
            {
                double mortality = Mortality(kind, severity, figure.AgeIn(year));
                if (fate.Fork("fatal").Chance(mortality))
                {
                    Houses.Die(
                        world,
                        figure,
                        year,
                        DeathCause.Illness,
                        Detail(kind, settlement),
                        Extra(settlement, extra));
                    continue;
                }
            }
            else if (!Slow(kind))
            {
                double injury = Injury(kind, severity, figure.AgeIn(year));
                if (fate.Fork("hurt").Chance(injury))
                {
                    LifeStories.Injure(
                        world,
                        figure,
                        subject,
                        source,
                        settlement.Id,
                        year,
                        fate.Fork("wound"),
                        Extra(settlement, extra),
                        record: true,

                        // A storming party is violence and the ground giving way is not, and the
                        // wound should not be described the same way in both.
                        cause: kind == HardshipKind.Sack
                            ? InjuryCause.Violence
                            : InjuryCause.Calamity);
                    continue;
                }
            }

            // Came through it, which is the commonest outcome and still a fact about the person.
            // Drawn from its own fork so that "nothing happened" is as reproducible as the rest,
            // and gated so that a world does not end up with everybody traumatised.
            if (fate.Fork("mark").Chance(Recall(severity)))
            {
                LifeStories.Remember(
                    figure,
                    MemoryKind.Hardship,
                    year,
                    source,
                    subject,
                    settlement.Id,
                    Intensity(kind, severity));
            }
        }
    }

    /// <summary>Whether the family kills slowly rather than breaking people on the day.</summary>
    private static bool Slow(HardshipKind kind) =>
        kind is HardshipKind.Famine or HardshipKind.Plague;

    /// <summary>
    /// Whether this pass is the one that decides who dies of this family.
    /// </summary>
    /// <remarks>
    /// Only famine, and the reason is the constraint this system was built under: there is to be no
    /// second mortality model. The other three families already reach recorded people through paths
    /// that predate this one — a plague through <c>PlagueSystem.Cull</c>, a sack through
    /// <c>Warfare.ResidentCasualties</c>, a disaster through <c>DisasterSystem.CourtCasualties</c>
    /// — and rolling for a death here as well would quietly double the chance of dying in exactly
    /// the years the world is most dangerous. Famine is the one bad year that could not kill a
    /// named person at all, so it is the one this pass is allowed to.
    /// </remarks>
    private static bool Kills(HardshipKind kind) => kind == HardshipKind.Famine;

    /// <summary>
    /// The living, recorded people this episode can honestly reach, in stable id order.
    /// </summary>
    /// <remarks>
    /// <para>Residence is resolved through <see cref="WorldState.ResidenceOf"/> rather than read
    /// raw, so a governor whose town has just changed hands is at court and not in a city that is
    /// no longer theirs — the same rule the sack and the disaster already use.</para>
    ///
    /// <para><b>Somebody still away at year end was not there.</b> A journey's dated return now
    /// distinguishes a short trip from one that winters over. This is the one exclusion the
    /// residence field cannot express on its own, and leaving it out would put a famine memory on
    /// the page of a man who demonstrably spent the close of that year elsewhere.</para>
    /// </remarks>
    private static List<Figure> Residents(WorldState world, Settlement settlement, int year)
    {
        var present = new List<Figure>();
        Stamp yearEnd = new(year, world.Config.Calendar.DaysPerYear - 1);

        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive) continue;
            if (figure.AgeIn(year) < 0) continue;
            if (!world.IsPresentAt(figure, settlement.Id, yearEnd)) continue;

            present.Add(figure);
        }

        present.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        return present;
    }

    /// <summary>
    /// How likely a famine is to kill this particular resident.
    /// </summary>
    /// <remarks>
    /// Monotonic in severity by construction, so the regression that a worse episode never lowers
    /// anybody's risk holds by reading rather than by measurement. The age term is the one place
    /// the two slow families differ in shape: a famine falls hardest on the very young and the old
    /// because it is a question of reserves, while a plague is flatter and kills adults in their
    /// strength in numbers a famine does not.
    /// </remarks>
    internal static double Mortality(HardshipKind kind, double severity, int age)
    {
        double baseline = kind == HardshipKind.Plague ? 0.42 : 0.26;
        return DetMath.Clamp(severity * baseline * Frailty(kind, age), 0.0, MaxMortality);
    }

    /// <summary>How likely a sudden episode is to leave this resident hurt rather than untouched.</summary>
    internal static double Injury(HardshipKind kind, double severity, int age)
    {
        double baseline = kind == HardshipKind.Sack ? 0.38 : 0.30;
        return DetMath.Clamp(severity * baseline * Frailty(kind, age), 0.0, MaxInjury);
    }

    /// <summary>Age's multiplier on exposure, by family.</summary>
    private static double Frailty(HardshipKind kind, int age)
    {
        bool child = age < 15;
        bool elder = age >= 60;

        return kind switch
        {
            HardshipKind.Famine => child ? 1.7 : elder ? 1.5 : 0.8,
            HardshipKind.Plague => child ? 1.3 : elder ? 1.25 : 1.0,

            // Being caught by a falling building or a storming party is much less a question of
            // reserves than a famine is, but the very young and the very old are still the ones
            // who cannot get out of the way.
            _ => child ? 1.25 : elder ? 1.2 : 1.0,
        };
    }

    /// <summary>
    /// How likely someone who came through it is to carry it afterwards.
    /// </summary>
    /// <remarks>
    /// Deliberately short of certainty even for a catastrophe. A world in which every survivor of
    /// every bad year carries a formative memory of it is as uninformative as one in which none
    /// does, and the memory model's own eviction would then spend its twelve slots on weather.
    /// </remarks>
    internal static double Recall(double severity) =>
        DetMath.Clamp(0.18 + (1.6 * severity), 0.18, 0.80);

    private static double Intensity(HardshipKind kind, double severity)
    {
        double sharp = Slow(kind) ? 0.0 : 0.08;
        return DetMath.Clamp(0.36 + (1.1 * severity) + sharp, 0.36, 0.92);
    }

    private static string Detail(HardshipKind kind, Settlement settlement) => kind switch
    {
        HardshipKind.Famine => "in the famine at " + settlement.Name,
        HardshipKind.Plague => "of the plague at " + settlement.Name,
        HardshipKind.Sack => "in the sack of " + settlement.Name,
        _ => "in the calamity at " + settlement.Name,
    };

    private static EntityId[] Extra(Settlement settlement, IReadOnlyList<EntityId>? extra)
    {
        if (extra is null || extra.Count == 0) return new[] { settlement.Id };

        var all = new List<EntityId>(extra.Count + 1) { settlement.Id };
        foreach (EntityId id in extra)
        {
            if (!all.Contains(id)) all.Add(id);
        }

        return all.ToArray();
    }
}

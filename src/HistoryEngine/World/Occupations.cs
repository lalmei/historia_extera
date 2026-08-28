using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.World;

/// <summary>
/// Choosing a life for someone the chronicle has started to follow.
/// </summary>
/// <remarks>
/// <para>Raised notables arrive with the career the office implies; children choose, once, when
/// they come of age. The choice is weighted by a blend of their people's values and their own
/// — <see cref="Disposition.Decides"/> — so a follower of a pious people takes holy orders and
/// a rebel of the same people may not. A parent's trade pulls the same way, harder on a
/// follower than on a rebel, which is what makes a marshal's child likelier to serve than a
/// merchant's without making it certain.</para>
///
/// <para>An office is a posting, not a new birth. Taking one puts them in the career the seat
/// is — clergy, arms, office — and laying it down (alive) puts them back. Death leaves them
/// as they were in the post. The title itself stays on the holding; occupation is what they
/// were doing that year.</para>
///
/// <para>Forked on the figure's own id, so the year they turn sixteen cannot change what they
/// become, and adding a later child cannot reshuffle an earlier one.</para>
/// </remarks>
public static class Occupations
{
    /// <summary>How hard a parent's trade pulls, at no independence.</summary>
    private const double FamilyPull = 0.7;

    /// <summary>How much more a matching career weighs when a court is filling a seat.</summary>
    private const double OfficeMatch = 1.0;

    /// <summary>How much a mismatch still weighs — never zero, or a realm with no soldiers
    /// could never name a marshal from its own house.</summary>
    private const double OfficeMismatch = 0.28;

    public static Occupation ForOffice(OfficeKind office) => office switch
    {
        OfficeKind.Marshal => Occupation.Soldiery,
        OfficeKind.HighPriest => Occupation.Clergy,
        OfficeKind.Governor => Occupation.Official,
        OfficeKind.GuildMaster => Occupation.Guild,
        OfficeKind.Merchant => Occupation.Merchant,
        _ => Occupation.Court,
    };

    public static Occupation FromOrigin(FigureOrigin origin) => origin switch
    {
        FigureOrigin.Soldiery => Occupation.Soldiery,
        FigureOrigin.Clergy => Occupation.Clergy,
        FigureOrigin.Townsfolk => Occupation.Townsfolk,
        FigureOrigin.Guild => Occupation.Guild,
        FigureOrigin.Merchant => Occupation.Merchant,
        _ => Occupation.None,
    };

    /// <summary>
    /// Puts this person in the career their open offices require, or restores the one they
    /// had before, unless they died in the post.
    /// </summary>
    public static void Sync(WorldState world, Figure figure, int year, bool died = false)
    {
        if (died) return;

        OfficeHolding? posting = CareerOffice(figure);
        if (posting is not null)
        {
            Occupation implied = ForOffice(posting.Kind);
            if (figure.Occupation == implied) return;

            RememberPrior(figure);
            Take(world, figure, implied, year);
            return;
        }

        Occupation home = figure.PriorOccupation != Occupation.None
            ? figure.PriorOccupation
            : FromOrigin(figure.Origin);

        figure.PriorOccupation = Occupation.None;

        // A priesthood that would not have married them will not take them back. Holding an
        // office overwrites the career, so a cleric who was crowned reads as Court for the length
        // of the reign — long enough to marry without the vow noticing — and this restore was
        // handing them their orders again on the way out. They keep the court instead, which is
        // where the marriage put them.
        if (home == Occupation.Clergy && BarredFromOrders(world, figure))
        {
            home = Occupation.Court;
        }

        if (home == Occupation.None)
        {
            if (figure.AgeIn(year) < Succession.MajorityAge)
            {
                figure.Occupation = Occupation.None;
            }
            else if (figure.Occupation == Occupation.Official)
            {
                // A posting is not a life. A cadet too remote for the household pass to give
                // them a trade can still be seated, and without this they would leave office
                // still wearing it. Dynasts go back to court; FromOrigin cannot help them,
                // because they arrived Unrecorded.
                Take(world, figure, Occupation.Court, year);
            }

            return;
        }

        if (home != figure.Occupation)
        {
            Take(world, figure, home, year);
        }
    }

    /// <summary>
    /// Gives this figure a career if they are old enough and do not already have one.
    /// </summary>
    public static void Ensure(WorldState world, Figure figure, int year)
    {
        if (figure.Occupation != Occupation.None) return;

        int age = figure.IsAlive ? figure.AgeIn(year) : figure.AgeAtDeath ?? 0;
        if (age < Succession.MajorityAge) return;

        IRng rng = world.Root.Fork("occupation", figure.Id.ToDiscriminator());
        Take(world, figure, Choose(world, figure, rng), year);
    }

    /// <summary>How the chronicle names the trade, in the case the template expects.</summary>
    public static string Phrase(Occupation occupation) => occupation switch
    {
        Occupation.Soldiery => "arms",
        Occupation.Clergy => "holy orders",
        Occupation.Townsfolk => "the standing of the town",
        Occupation.Guild => "a craft",
        Occupation.Merchant => "trade",
        Occupation.Court => "the court",
        Occupation.Official => "office",
        Occupation.Scribe => "letters",
        _ => "a trade",
    };

    /// <summary>How strongly this person fits a seat, for a court choosing among courtiers.</summary>
    public static double Affinity(Figure figure, OfficeKind office)
    {
        if (figure.Occupation == Occupation.None) return OfficeMismatch;
        if (figure.Occupation == ForOffice(office)) return OfficeMatch;

        // A governorship looks for someone of the town; holding it is office.
        if (office == OfficeKind.Governor && figure.Occupation == Occupation.Townsfolk)
        {
            return OfficeMatch;
        }

        return OfficeMismatch;
    }

    /// <summary>
    /// Whether a vow of celibacy stands between this figure and holy orders.
    /// </summary>
    /// <remarks>
    /// The other half of <c>HouseholdSystem.VowedToCelibacy</c>, which refuses the marriage of
    /// someone already in orders. Without this the rule fires in one direction only, and the
    /// chronicle records a man marrying and taking holy orders in the same year in a faith whose
    /// own scripture forbids it — which is exactly what it did.
    ///
    /// <para>Asked of the faith the figure professes, and of the faith of the place they live in
    /// when they profess none. The fall-back is not a nicety: a spouse invented for a wedding is
    /// raised into the record with no faith at all and is given a career in the same pass, so
    /// asking only what they professed let exactly the person this rule exists for through the
    /// door — married in one line, ordained in the next, and holding the town's celibate faith by
    /// the following spring. Whoever ordains them is the temple standing where they live, and that
    /// temple's vow is the one that binds. A married figure in a place of no faith, or of a faith
    /// that permits it, is barred from nothing.</para>
    /// </remarks>
    public static bool BarredFromOrders(WorldState world, Figure figure)
    {
        if (!figure.IsMarried) return false;

        EntityId faith = figure.ReligionId;

        if (faith.IsNone)
        {
            EntityId home = world.ResidenceOf(figure);
            if (world.Settlements.Contains(home)) faith = world.Settlements[home].ReligionId;
        }

        // Residence, then the realm — the same two questions, in the same order, that
        // ReligionSystem.ConvertTheFaithless will ask when it hands this person a faith. Asking
        // only the first left the door open for exactly one person: someone ordained while
        // professing nothing, in a town professing nothing, in a realm whose own faith is
        // celibate. They broke no rule at any single moment and were a married priest of a
        // celibate faith by the time the realm's faith reached them.
        if (faith.IsNone && world.Civilizations.Contains(figure.CivilizationId))
        {
            faith = world.Civilizations[figure.CivilizationId].StateReligionId;
        }

        return !faith.IsNone
            && world.Religions.Contains(faith)
            && world.Religions[faith].Character.CelibateClergy;
    }

    public static Occupation Choose(WorldState world, Figure figure, IRng rng)
    {
        double[] weights = Weights(world, figure);
        var options = new[]
        {
            Occupation.Soldiery,
            Occupation.Clergy,
            Occupation.Townsfolk,
            Occupation.Guild,
            Occupation.Merchant,
            Occupation.Court,
            Occupation.Scribe,
        };

        return rng.PickWeighted(options, occupation => weights[IndexOf(occupation)]);
    }

    /// <summary>The complete, inspectable pull on a first career before its one random draw.</summary>
    internal static double[] Weights(
        WorldState world,
        Figure figure,
        bool includeMentor = true,
        bool includeSiege = true)
    {
        Culture culture = world.CultureOf(figure);
        CultureValues decided = figure.Disposition.Decides(culture.Values);
        double independence = figure.Disposition.Independence;

        var weights = new[]
        {
            0.08 + (decided.Aggression * 1.10) + (decided.Expansionism * 0.40),
            0.08 + (decided.Piety * 1.20),
            0.08 + (decided.Tradition * 0.70),
            0.08 + (decided.Learning * 1.10),
            0.08 + (decided.Mercantile * 1.20),
            0.06 + (figure.Disposition.Centralism * 0.25),
            0.05 + (decided.Learning * 1.15) + (decided.Tradition * 0.25),
        };

        if (!figure.DynastyId.IsNone)
        {
            weights[5] += 0.12 + ((1.0 - independence) * 0.35);
        }

        PullToward(world, figure.MotherId, independence, weights);
        PullToward(world, figure.FatherId, independence, weights);
        if (includeMentor) PullTowardMentor(world, figure, weights);
        if (includeSiege) PullFromSiege(figure, weights);

        // A vow taken after a wedding is not a vow. Zeroing the weight rather than removing the
        // option keeps the array — and therefore the number of draws this roll makes — the same
        // for everyone, which is what stops a married figure's existence from shifting the
        // careers of everyone chosen after them.
        if (BarredFromOrders(world, figure)) weights[IndexOf(Occupation.Clergy)] = 0.0;

        return weights;
    }

    private static void Take(WorldState world, Figure figure, Occupation occupation, int year)
    {
        if (figure.Occupation == occupation) return;

        figure.Occupation = occupation;
        if (occupation == Occupation.None || !figure.IsAlive) return;

        world.Chronicle.Record(
            year,
            EventKind.OccupationTaken,
            figure.Id,
            location: world.ResidenceOf(figure),
            data: Chronicle.Data(("occupation", Phrase(occupation))),
            significance: Significance.Routine);
    }

    private static void RememberPrior(Figure figure)
    {
        if (figure.PriorOccupation != Occupation.None) return;
        if (figure.Occupation is Occupation.None or Occupation.Official) return;

        figure.PriorOccupation = figure.Occupation;
    }

    private static OfficeHolding? CareerOffice(Figure figure)
    {
        // Newest first: a figure who already held one seat and was then given another is doing the
        // later one, so a marshal appointed over a sitting governor reads as arms, not office.
        for (int i = figure.Offices.Count - 1; i >= 0; i--)
        {
            OfficeHolding held = figure.Offices[i];
            if (held.ToYear is not null) continue;
            if (held.Kind == OfficeKind.Consort) continue;

            return held;
        }

        return null;
    }

    private static void PullToward(
        WorldState world, EntityId parentId, double independence, double[] weights)
    {
        if (!world.Figures.Contains(parentId)) return;

        Occupation parent = world.Figures[parentId].Occupation;
        if (parent is Occupation.None or Occupation.Official) return;

        weights[IndexOf(parent)] += (1.0 - independence) * FamilyPull;
    }

    private static void PullTowardMentor(WorldState world, Figure figure, double[] weights)
    {
        foreach (FigureBond bond in figure.Bonds)
        {
            if (!bond.Kinds.HasFlag(BondKind.Apprentice)) continue;
            if (!world.Figures.Contains(bond.OtherId)) continue;

            Figure mentor = world.Figures[bond.OtherId];
            double pull = 0.32 + (Math.Max(0.0, bond.Trust) * 0.48);

            switch (Upbringings.FamilyOf(mentor.Occupation))
            {
                case CareerFamily.Arms:
                    weights[IndexOf(Occupation.Soldiery)] += pull;
                    break;
                case CareerFamily.Faith:
                    weights[IndexOf(Occupation.Clergy)] += pull;
                    break;
                case CareerFamily.TradeCraft:
                    weights[IndexOf(Occupation.Townsfolk)] += pull * 0.24;
                    weights[IndexOf(Occupation.Guild)] += pull * 0.30;
                    weights[IndexOf(Occupation.Merchant)] += pull * 0.30;
                    if (mentor.Occupation is Occupation.Townsfolk or Occupation.Guild or Occupation.Merchant)
                    {
                        weights[IndexOf(mentor.Occupation)] += pull * 0.36;
                    }
                    break;
                case CareerFamily.LettersOffice:
                    weights[IndexOf(Occupation.Court)] += pull * 0.42;
                    weights[IndexOf(Occupation.Scribe)] += pull * 0.58;
                    break;
            }
        }
    }

    /// <summary>A childhood siege changes later risk preference without dictating a career.</summary>
    private static void PullFromSiege(Figure figure, double[] weights)
    {
        foreach (SalientMemory memory in figure.Memories)
        {
            if (memory.Kind != MemoryKind.Siege) continue;

            double intensity = LifeStories.EffectiveIntensity(memory, figure.BirthYear + Succession.MajorityAge);
            double bold = figure.Disposition.Values.Aggression;
            weights[IndexOf(Occupation.Soldiery)] += intensity * bold * 0.46;
            weights[IndexOf(Occupation.Townsfolk)] += intensity * (1.0 - bold) * 0.24;
        }
    }

    private static int IndexOf(Occupation occupation) => occupation switch
    {
        Occupation.Soldiery => 0,
        Occupation.Clergy => 1,
        Occupation.Townsfolk => 2,
        Occupation.Guild => 3,
        Occupation.Merchant => 4,
        Occupation.Court => 5,
        Occupation.Scribe => 6,
        _ => 5,
    };
}

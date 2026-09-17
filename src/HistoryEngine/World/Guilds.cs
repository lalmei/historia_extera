using HistoryEngine.Core;
using HistoryEngine.Entities;

namespace HistoryEngine.World;

/// <summary>
/// The guilds a town holds, and who speaks for each of them.
/// </summary>
/// <remarks>
/// <para><b>A guild is the guild of something.</b> <see cref="OfficeKind.GuildMaster"/> has been
/// declared since offices landed and nothing has ever granted it, so a town has had neither one
/// guild nor several — it has had none. What was missing was not the seat but the thing the seat
/// is over: until a guildsman could be told from another guildsman (#245), every mastery in a town
/// would have been the same undifferentiated post, filled from the same pool, saying nothing.
/// A craft on a figure is what makes two masteries in one town two different facts.</para>
///
/// <para><b>The record decides which guilds exist, not the ground.</b> A mastery is created where
/// somebody in the record practises that trade in that town, and nowhere else. This is a
/// deliberately stricter gate than <see cref="Crafts.Supports"/>, which says what a place
/// <em>could</em> hold: a guild that existed wherever the inputs did would put a mastery in every
/// town with clay in it and leave the seat unfillable. Reading the practitioners instead means the
/// question "does this town have a weavers' guild" has exactly one answer and it is the same answer
/// as "is there a weaver here" — which is also what makes the seat fillable by construction.</para>
///
/// <para><b>The body chooses its own.</b> No grantor: a guild elected its master, and the export
/// already distinguishes that from a crown appointment by the absence of one
/// (<see cref="OfficeHolding.GrantedBy"/>). So a mastery does not lapse when a ruler dies, which is
/// the rule that keeps every mandated seat turning over — a guild outlives the reign, and that is
/// the whole difference between a trade body and a court post.</para>
///
/// <para><b>Seniority, because that is what a guild voted on.</b> The candidate pool is weighted by
/// years in the trade. A guild admitted masters and then chose among them by standing, and standing
/// was time: a man who had kept a shop for thirty years was not passed over for one who had kept
/// one for three. The draw remains a draw, so the eldest does not always take it, and whoever
/// stood nearest and lost is handed to <see cref="Offices.NotePassedOver"/> — the one wrong in the
/// engine that occurs between equals, and two men of the same guild are exactly that.</para>
///
/// <para>Forked per settlement and per craft, so a town's weavers cannot reshuffle its smiths and
/// adding a town cannot shift the guilds of the others.</para>
/// </remarks>
public static class Guilds
{
    /// <summary>
    /// How likely a vacant mastery is to be filled in any one year.
    /// </summary>
    /// <remarks>
    /// Lower than <c>OfficeSystem.FillChance</c>. A court with no marshal is a realm with a hole in
    /// it and fills the post at the first opportunity; a guild whose master has died goes on
    /// weaving, and the year the company troubles itself to elect another is not the year after.
    /// The delay is also what keeps a mastery from reading as an automatic property of having a
    /// craftsman in town.
    /// </remarks>
    private const double ElectionChance = 0.22;

    /// <summary>Years in the trade above which seniority stops adding standing.</summary>
    /// <remarks>
    /// A ceiling rather than a slope without end, or a guild would simply always return its oldest
    /// member and the draw would be decoration. Thirty years is a full working life in a craft:
    /// past it, two masters are both simply established, and what separates them is no longer time.
    /// </remarks>
    private const int SeniorityCeiling = 30;

    /// <summary>What this culture calls the master of this trade.</summary>
    /// <remarks>
    /// Two halves from two places, and the split is the point. The word is the culture's — a
    /// republic's Alderman and a chiefdom's Elder are the same seat — and the company is the town's,
    /// because which trades have a body to speak for them is a fact about the place.
    /// </remarks>
    public static string Title(Culture culture, Craft craft, Sex sex) =>
        culture.TitleFor(OfficeKind.GuildMaster, sex) + " of the " + Crafts.Company(craft);

    /// <summary>
    /// Whether a mastery of this craft still has a company under it in this town.
    /// </summary>
    /// <remarks>
    /// Asked of a seat already held, by the release pass, and of a seat about to be created. The
    /// holder is himself a practitioner, so a standing mastery answers yes while its master lives —
    /// which means this only ever ends a mastery for the reasons that are about the town rather
    /// than about the man: it was abandoned, or it changed hands.
    /// </remarks>
    public static bool Stands(
        WorldState world, Civilization civilization, Figure holder, OfficeHolding held)
    {
        if (held.Craft == Craft.None) return false;
        if (!world.Settlements.Contains(held.ScopeId)) return false;

        Settlement place = world.Settlements[held.ScopeId];
        if (!place.IsActive || place.CivilizationId != civilization.Id) return false;

        // And he has to still be here. A company elects one of its own, so a master who has moved
        // to another town is no longer one of them — the alternative is a mastery that follows a
        // man out of the town that gave it to him, which would leave the company he left with
        // nobody able to speak for it and no way to elect anybody.
        return holder.ResidenceSettlementId == held.ScopeId;
    }

    /// <summary>
    /// Elects masters for the trades this realm's towns practise and have nobody speaking for.
    /// </summary>
    /// <remarks>
    /// <para><b>One pass over the figures, not one per town.</b> The obvious shape — ask each town
    /// who lives in it — is the figure table read once per settlement per year, which on the
    /// panel's largest world was 8% of the whole engine run and grows with the number of towns as
    /// well as the number of people. Bucketing once and then walking the towns costs the same read
    /// whether a realm holds three settlements or ninety.</para>
    ///
    /// <para>The streams are unchanged by that: a fork is addressed by its town and its trade
    /// rather than drawn in sequence, so gathering first and deciding afterwards elects exactly
    /// the same masters — which the seed-42 fingerprint is the proof of.</para>
    /// </remarks>
    public static void Fill(
        WorldState world,
        Civilization civilization,
        Culture culture,
        int year,
        IRng court)
    {
        // Figure order, which is append-ordered and therefore stable. A craft with no living
        // practitioner in a town never reaches the loop below, which is the acceptance rule — no
        // mastery exists for a craft nobody in the town practises — holding by construction rather
        // than by a check somebody has to remember to write.
        var companies = new Dictionary<EntityId, List<Figure>?[]>();
        var spoken = new Dictionary<EntityId, bool[]>();

        foreach (Figure figure in world.Figures)
        {
            if (figure.Craft == Craft.None) continue;
            if (!figure.IsAlive) continue;

            // Asked of every living craftsman rather than only of this realm's, and that is the
            // whole of the bug this guards. A standing mastery is a fact about the seat, not about
            // where its holder sleeps or whose subject he now is: he holds it until the release
            // pass takes it off him, and a town that counted only its own residents elected a
            // second master of a company that already had one.
            OfficeHolding? mastery = figure.OpenOffice(OfficeKind.GuildMaster);
            if (mastery is not null)
            {
                if (mastery.Craft == figure.Craft && !mastery.ScopeId.IsNone)
                {
                    if (!spoken.TryGetValue(mastery.ScopeId, out bool[]? taken))
                    {
                        spoken[mastery.ScopeId] = taken = new bool[Crafts.All.Length];
                    }

                    taken[Slot(figure.Craft)] = true;
                }

                continue;
            }

            // A craftsman holding any office is not a candidate: Offices.Available says so for
            // every other seat, and one office at a time is what the release pass assumes.
            if (figure.CurrentOffice is not null) continue;

            if (figure.CivilizationId != civilization.Id) continue;
            if (figure.AgeIn(year) < Offices.ServiceAge) continue;
            if (figure.Id == civilization.CurrentRulerId || figure.Id == civilization.RegentId)
            {
                continue;
            }

            EntityId home = figure.ResidenceSettlementId;
            if (home.IsNone) continue;

            if (!companies.TryGetValue(home, out List<Figure>?[]? byCraft))
            {
                companies[home] = byCraft = new List<Figure>?[Crafts.All.Length];
            }

            int slot = Slot(figure.Craft);
            byCraft[slot] ??= new List<Figure>();
            byCraft[slot]!.Add(figure);
        }

        // The realm's own settlement order, which is append-ordered, rather than the dictionary's.
        foreach (Settlement settlement in world.ActiveSettlementsOf(civilization))
        {
            if (!companies.TryGetValue(settlement.Id, out List<Figure>?[]? byCraft)) continue;

            spoken.TryGetValue(settlement.Id, out bool[]? taken);

            // Forked per settlement, so a realm's twelfth town is not decided by how its first
            // eleven went, and adding a town cannot shift the guilds of the others.
            IRng local = court.Fork("guilds", settlement.Id.ToDiscriminator());

            // Crafts.All order, the one declared order every weighted draw over trades uses.
            for (int slot = 0; slot < Crafts.All.Length; slot++)
            {
                if (taken is not null && taken[slot]) continue;

                List<Figure>? members = byCraft[slot];
                if (members is null) continue;

                Craft craft = Crafts.All[slot];

                // Forked on the trade as well as the town: the year a town's smiths elect nobody
                // must not decide anything about its weavers.
                IRng company = local.Fork("guild", (long)craft);
                if (!company.Chance(ElectionChance)) continue;

                Elect(world, civilization, culture, settlement, craft, members, year, company);
            }
        }
    }

    /// <summary>This craft's place in <see cref="Crafts.All"/>.</summary>
    private static int Slot(Craft craft) => (int)craft - 1;

    /// <summary>Puts the company's choice in the seat, and records who stood nearest and lost.</summary>
    private static void Elect(
        WorldState world,
        Civilization civilization,
        Culture culture,
        Settlement settlement,
        Craft craft,
        List<Figure> members,
        int year,
        IRng company)
    {
        Figure master = company.PickWeighted(members, member => Standing(member, year));
        Figure? runnerUp = Nearest(members, master, year);

        Offices.Grant(
            world,
            civilization,
            culture,
            master,
            OfficeKind.GuildMaster,
            settlement.Id,
            EntityId.None,
            "by the vote of the " + Crafts.Company(craft) + " of " + settlement.Name,
            year,
            craft);

        if (runnerUp is not null)
        {
            OfficeHolding held = master.Offices[^1];
            Offices.NotePassedOver(world, runnerUp, master, held.Title, held.Claim!, year);
        }
    }

    /// <summary>How much standing this member brings to the vote.</summary>
    /// <remarks>
    /// Years in the trade, capped, and never zero — a guildsman admitted last year is a long shot
    /// rather than an impossibility, because a company of three has to elect one of the three.
    /// </remarks>
    private static double Standing(Figure member, int year)
    {
        int served = Math.Clamp(year - member.CraftYear, 0, SeniorityCeiling);

        return 0.20 + ((double)served / SeniorityCeiling);
    }

    /// <summary>
    /// The member who had the best claim of everyone who did not get it.
    /// </summary>
    /// <remarks>
    /// The senior loser, not a second draw. <see cref="Offices.NotePassedOver"/> takes only
    /// somebody who actually had a claim, and in a body that votes on standing that is precisely
    /// the man the standing pointed at. Ties break on figure id, which is append-ordered.
    /// </remarks>
    private static Figure? Nearest(List<Figure> members, Figure master, int year)
    {
        Figure? nearest = null;
        double best = 0.0;

        foreach (Figure member in members)
        {
            if (member.Id == master.Id) continue;

            double standing = Standing(member, year);
            if (nearest is null || standing > best)
            {
                nearest = member;
                best = standing;
            }
        }

        return nearest;
    }
}

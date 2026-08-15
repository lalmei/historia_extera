using System.Globalization;
using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Events;

namespace HistoryEngine.World;

/// <summary>
/// Writes the contents of a tome from history that already exists when it is made.
/// </summary>
/// <remarks>
/// <para><b>A book is a historical snapshot.</b> Text is composed once and stored on the artifact,
/// rather than reconstructed during export. A chronicle written during a war therefore says the
/// outcome was uncertain even when the final export knows how that war ended.</para>
///
/// <para><b>Subject before prose.</b> The generator first chooses among kinds for which the town
/// has real material: its rulers, commanders, treasures, faith, or its own annals. Only then does
/// it choose a subject and write sections from entity state. The independent artifact-id stream
/// makes richer contents unable to perturb whether another town creates an artifact that year.</para>
///
/// <para>Religious rites and teachings are invented, because no event can record a doctrine, but
/// they are keyed to the religion rather than to the book. Two codices of one faith therefore
/// agree about its observance instead of inventing a fresh religion each time.</para>
/// </remarks>
public static class Tomes
{
    /// <summary>Additional settlements that can receive one work before it ceases to be scarce.</summary>
    private const int MaximumCopies = 4;

    private sealed class CampaignRecord
    {
        public CampaignRecord(Figure figure, War war)
        {
            Figure = figure;
            War = war;
            Battles = new List<Battle>();
        }

        public Figure Figure { get; }
        public War War { get; }
        public List<Battle> Battles { get; }
    }

    private sealed record CopyRoute(Settlement Source, Settlement Destination, double Score);

    /// <summary>Composes one tome from facts known in <paramref name="year"/>.</summary>
    public static TomeContents Compose(
        WorldState world,
        Settlement settlement,
        Civilization civilization,
        EntityId artifactId,
        int year)
    {
        IRng rng = world.Root.Fork("tome.contents", artifactId.ToDiscriminator());

        List<Figure> figures = NotableFigures(world, civilization, year);
        List<CampaignRecord> campaigns = Campaigns(world, civilization, year);
        List<Artifact> artifacts = ArtifactSubjects(world, settlement, civilization, year);
        Religion? religion = ReligionAt(world, settlement, year);

        var possible = new List<TomeContentKind>(5);
        if (figures.Count > 0) possible.Add(TomeContentKind.Biography);
        if (campaigns.Count > 0) possible.Add(TomeContentKind.Campaign);

        if (religion is not null)
        {
            possible.Add(TomeContentKind.ReligiousRite);
            possible.Add(TomeContentKind.ReligiousTeaching);
        }

        // Every town has a history even when it has no recorded ruler or faith.
        possible.Add(TomeContentKind.Annals);

        // Artifact histories are a minority form rather than another equal slot in the main
        // draw. This leaves lives, campaigns, faith and annals common while still making a
        // treasury important enough to attract its own account from time to time.
        TomeContentKind kind = artifacts.Count > 0
            && rng.Fork("artifact-history").Chance(0.10)
                ? TomeContentKind.ArtifactHistory
                : rng.Pick(possible);

        TomeContents contents = kind switch
        {
            TomeContentKind.Biography => Biography(world, rng.Pick(figures), year),
            TomeContentKind.Campaign => Campaign(world, rng.Pick(campaigns), year),
            TomeContentKind.ArtifactHistory => ArtifactHistory(
                world, rng.Fork("artifact-subject").Pick(artifacts), year),
            TomeContentKind.ReligiousRite => ReligiousRite(world, religion!, year),
            TomeContentKind.ReligiousTeaching => ReligiousTeaching(world, religion!, year),
            _ => Annals(world, settlement, civilization, year),
        };

        contents.CopyLimit = CopyLimit(world, settlement, civilization, artifactId, kind);
        return contents;
    }

    /// <summary>
    /// Gives one written work a chance to acquire one additional settlement copy this year.
    /// </summary>
    /// <remarks>
    /// <para>The ceiling is fixed when the work is composed. Most reproducible works allow one or
    /// two copies; a long tail reaches four, while some remain unique. The annual step is separate:
    /// a work with wide potential still needs years and a real route before that potential is met.</para>
    ///
    /// <para>Copies are records attached to the work, not new artifacts. The original can be
    /// looted or lost without multiplying the world's inventory of famous objects, while a copy in
    /// an active settlement can still serve as the exemplar for the next one.</para>
    /// </remarks>
    public static void Distribute(WorldState world, int year)
    {
        foreach (Artifact artifact in world.Artifacts)
        {
            TomeContents? contents = artifact.TomeContents;
            if (contents is null
                || contents.CopyLimit <= contents.Copies.Count
                || year <= artifact.CreatedYear)
            {
                continue;
            }

            IRng rng = world.Root
                .Fork("tome.distribution", artifact.Id.ToDiscriminator())
                .Fork("year", year);

            List<Settlement> sources = Sources(world, artifact, contents);
            if (sources.Count == 0 || !rng.Chance(AnnualCopyChance(contents, sources))) continue;

            List<CopyRoute> routes = Routes(world, contents, sources, rng);
            if (routes.Count == 0) continue;

            CopyRoute route = routes[0];
            contents.CopyTo(year, route.Destination.Id, route.Source.Id);

            world.Chronicle.Record(
                year,
                EventKind.ArtifactCopied,
                artifact.Id,
                obj: route.Source.Id,
                location: route.Destination.Id);
        }
    }

    /// <summary>How far this particular work can spread, separate from how quickly it does so.</summary>
    private static int CopyLimit(
        WorldState world,
        Settlement settlement,
        Civilization civilization,
        EntityId artifactId,
        TomeContentKind kind)
    {
        CultureValues values = world.CultureOf(civilization).Values;
        IRng rng = world.Root.Fork("tome.circulation", artifactId.ToDiscriminator());

        double eligibility = 0.62
            + (values.Tradition * 0.10)
            + (values.Mercantile * 0.08);

        if (IsReligious(kind)) eligibility += 0.08;
        if (IsBookHub(settlement)) eligibility += 0.06;
        if (settlement.Tier == SettlementTier.City) eligibility += 0.04;
        if (settlement.IsCapital) eligibility += 0.03;

        if (!rng.Chance(DetMath.Clamp(eligibility, 0.50, 0.88))) return 0;

        int limit = 1;
        if (rng.Chance(0.48 + (values.Mercantile * 0.12))) limit++;
        if (rng.Chance(0.18 + (values.Tradition * 0.12))) limit++;
        if (rng.Chance(0.05 + (IsReligious(kind) ? 0.05 : 0.0))) limit++;
        return Math.Min(limit, MaximumCopies);
    }

    /// <summary>Current settlements from which a scribe could inspect an exemplar.</summary>
    private static List<Settlement> Sources(
        WorldState world, Artifact artifact, TomeContents contents)
    {
        var sources = new List<Settlement>();

        if (artifact.IsExtant
            && !artifact.HolderId.IsNone
            && world.Settlements.Contains(artifact.HolderId)
            && world.Settlements[artifact.HolderId].IsActive)
        {
            sources.Add(world.Settlements[artifact.HolderId]);
        }

        foreach (TomeCopy copy in contents.Copies)
        {
            if (!world.Settlements.Contains(copy.SettlementId)) continue;

            Settlement settlement = world.Settlements[copy.SettlementId];
            if (settlement.IsActive && !ContainsSettlement(sources, settlement.Id))
            {
                sources.Add(settlement);
            }
        }

        return sources;
    }

    private static double AnnualCopyChance(TomeContents contents, IReadOnlyList<Settlement> sources)
    {
        double chance = 0.13 + (IsReligious(contents.Kind) ? 0.03 : 0.0);

        foreach (Settlement source in sources)
        {
            if (IsBookHub(source)) chance += 0.04;
            if (source.IsCapital) chance += 0.02;
            if (source.Tier == SettlementTier.City) chance += 0.02;
            if (chance >= 0.24) break;
        }

        return DetMath.Clamp(chance, 0.10, 0.24);
    }

    /// <summary>Reachable destinations, strongest historical route first.</summary>
    private static List<CopyRoute> Routes(
        WorldState world,
        TomeContents contents,
        IReadOnlyList<Settlement> sources,
        IRng rng)
    {
        var routes = new List<CopyRoute>();

        foreach (Settlement destination in world.Settlements)
        {
            if (!destination.IsActive
                || destination.Tier < SettlementTier.Town
                || ContainsSettlement(sources, destination.Id)
                || HasCopyAt(contents, destination.Id))
            {
                continue;
            }

            CopyRoute? best = null;
            foreach (Settlement source in sources)
            {
                double distance = world.Distance(source.X, source.Z, destination.X, destination.Z);
                bool sameRealm = source.CivilizationId == destination.CivilizationId;
                bool faithRoute = IsReligious(contents.Kind)
                    && source.ReligionId == contents.SubjectId
                    && destination.ReligionId == contents.SubjectId
                    && distance <= Diplomacy.ContactRange * 1.5;
                TradeRoute? tradeRoute = TradeRoutes.Between(world, source.Id, destination.Id);

                if (!sameRealm && !faithRoute && tradeRoute is null) continue;

                double score = (sameRealm ? 4.0 : 0.0)
                    + (faithRoute ? 3.0 : 0.0)
                    + (tradeRoute is not null ? 1.0 + tradeRoute.Traffic : 0.0)
                    + (destination.Tier == SettlementTier.City ? 0.7 : 0.0)
                    + (destination.IsCapital ? 0.4 : 0.0)
                    + (IsBookHub(destination) ? 0.5 : 0.0)
                    + (1.0 - DetMath.InverseLerp(
                        0.0, Diplomacy.ContactRange * 1.5, distance))
                    + rng.NextDouble(0.0, 0.35);

                var route = new CopyRoute(source, destination, score);
                if (best is null
                    || route.Score > best.Score
                    || (route.Score == best.Score && route.Source.Id.CompareTo(best.Source.Id) < 0))
                {
                    best = route;
                }
            }

            if (best is not null) routes.Add(best);
        }

        routes.Sort(static (a, b) =>
        {
            int score = b.Score.CompareTo(a.Score);
            return score != 0 ? score : a.Destination.Id.CompareTo(b.Destination.Id);
        });

        return routes;
    }

    private static bool HasCopyAt(TomeContents contents, EntityId settlementId)
    {
        foreach (TomeCopy copy in contents.Copies)
        {
            if (copy.SettlementId == settlementId) return true;
        }

        return false;
    }

    private static bool ContainsSettlement(IReadOnlyList<Settlement> settlements, EntityId id)
    {
        foreach (Settlement settlement in settlements)
        {
            if (settlement.Id == id) return true;
        }

        return false;
    }

    private static bool IsReligious(TomeContentKind kind) =>
        kind is TomeContentKind.ReligiousRite or TomeContentKind.ReligiousTeaching;

    private static bool IsBookHub(Settlement settlement) =>
        settlement.Specialization is SettlementSpecialization.Trade
            or SettlementSpecialization.Crafts
            or SettlementSpecialization.Shrine;

    // -----------------------------------------------------------------------
    // Subjects
    // -----------------------------------------------------------------------

    private static List<Figure> NotableFigures(
        WorldState world, Civilization civilization, int year)
    {
        var figures = new List<Figure>();

        foreach (EntityId id in civilization.RulerIds)
        {
            if (!world.Figures.Contains(id)) continue;

            Figure figure = world.Figures[id];
            if (figure.BirthYear <= year && !figures.Contains(figure)) figures.Add(figure);
        }

        // A realm can briefly have no usable ruler record after its house fails. A titled local
        // figure is still somebody its scribes could write a life of.
        if (figures.Count == 0)
        {
            foreach (Figure figure in world.Figures)
            {
                if (figure.CivilizationId == civilization.Id
                    && figure.BirthYear <= year
                    && figure.Offices.Count > 0)
                {
                    figures.Add(figure);
                }
            }
        }

        return figures;
    }

    private static List<CampaignRecord> Campaigns(
        WorldState world, Civilization civilization, int year)
    {
        var records = new List<CampaignRecord>();

        foreach (War war in world.Wars)
        {
            if (war.StartYear > year || !war.Involves(civilization.Id)) continue;

            foreach (EntityId battleId in war.BattleIds)
            {
                if (!world.Battles.Contains(battleId)) continue;

                Battle battle = world.Battles[battleId];
                if (battle.Year > year) continue;

                if (battle.AttackerId == civilization.Id)
                {
                    AddCampaign(records, world, war, battle, battle.AttackerCommanderId);
                }

                if (battle.DefenderId == civilization.Id)
                {
                    AddCampaign(records, world, war, battle, battle.DefenderCommanderId);
                }
            }
        }

        return records;
    }

    private static void AddCampaign(
        List<CampaignRecord> records,
        WorldState world,
        War war,
        Battle battle,
        EntityId commanderId)
    {
        if (commanderId.IsNone || !world.Figures.Contains(commanderId)) return;

        CampaignRecord? record = null;
        foreach (CampaignRecord candidate in records)
        {
            if (candidate.Figure.Id == commanderId && candidate.War.Id == war.Id)
            {
                record = candidate;
                break;
            }
        }

        if (record is null)
        {
            record = new CampaignRecord(world.Figures[commanderId], war);
            records.Add(record);
        }

        record.Battles.Add(battle);
    }

    private static Religion? ReligionAt(WorldState world, Settlement settlement, int year)
    {
        if (settlement.ReligionId.IsNone || !world.Religions.Contains(settlement.ReligionId))
        {
            return null;
        }

        Religion religion = world.Religions[settlement.ReligionId];
        return religion.FoundedYear <= year ? religion : null;
    }

    /// <summary>Treasures this settlement's scribes could plausibly know enough to describe.</summary>
    private static List<Artifact> ArtifactSubjects(
        WorldState world, Settlement settlement, Civilization civilization, int year)
    {
        var artifacts = new List<Artifact>();

        foreach (Artifact artifact in world.Artifacts)
        {
            if (artifact.CreatedYear > year) continue;

            EntityId holderId = HoldingAt(artifact, year);
            bool heldLocally = holderId == settlement.Id;
            bool heldInRealm = !holderId.IsNone
                && world.Settlements.Contains(holderId)
                && world.Settlements[holderId].CivilizationId == civilization.Id;

            bool madeInRealm = world.Settlements.Contains(artifact.OriginSettlementId)
                && (world.Settlements[artifact.OriginSettlementId].FoundedBy == civilization.Id
                    || world.Settlements[artifact.OriginSettlementId].CivilizationId == civilization.Id);

            bool sharedFaith = !settlement.ReligionId.IsNone
                && artifact.ReligionId == settlement.ReligionId;

            if (heldLocally || heldInRealm || madeInRealm || sharedFaith) artifacts.Add(artifact);
        }

        return artifacts;
    }

    // -----------------------------------------------------------------------
    // Lives and campaigns
    // -----------------------------------------------------------------------

    private static TomeContents Biography(WorldState world, Figure figure, int year)
    {
        var sections = new List<TomeSection>();
        var origins = new List<EntityId> { figure.Id };

        string born = figure.FullName + " was born in "
                      + figure.BirthYear.ToString(CultureInfo.InvariantCulture);
        if (!figure.BirthSettlementId.IsNone && world.Settlements.Contains(figure.BirthSettlementId))
        {
            born += " at " + world.NameOf(figure.BirthSettlementId);
            origins.Add(figure.BirthSettlementId);
        }

        born += ".";

        if (!figure.DynastyId.IsNone && world.Dynasties.Contains(figure.DynastyId))
        {
            born += " " + Subject(figure) + " was born to the "
                    + world.NameOf(figure.DynastyId) + " house.";
            origins.Add(figure.DynastyId);
        }

        List<EntityId> parents = Existing(world, figure.Parents(), f => f.BirthYear <= year);
        if (parents.Count > 0)
        {
            born += " The record names " + Names(world, parents) + " as "
                    + (parents.Count == 1 ? "a parent." : "the parents.");
            origins.AddRange(parents);
        }

        if (figure.DeathYear is int death && death <= year)
        {
            born += " " + Subject(figure) + " died in "
                    + death.ToString(CultureInfo.InvariantCulture) + ", of "
                    + (figure.DeathDetail ?? Houses.CauseLabel(figure.DeathCause)) + ".";
        }

        sections.Add(Section("Origins", born, origins));

        List<OfficeHolding> titles = new();
        foreach (OfficeHolding title in figure.Offices)
        {
            if (title.FromYear <= year) titles.Add(title);
        }

        if (titles.Count > 0)
        {
            var offices = new List<string>(titles.Count);
            var officeRefs = new List<EntityId> { figure.Id };

            foreach (OfficeHolding title in titles)
            {
                int? ended = title.ToYear is int to && to <= year ? to : null;
                string span = ended is int end
                    ? title.FromYear.ToString(CultureInfo.InvariantCulture) + "–"
                      + end.ToString(CultureInfo.InvariantCulture)
                    : "from " + title.FromYear.ToString(CultureInfo.InvariantCulture)
                      + " through the writing of this account";

                offices.Add(
                    title.Title + " of " + world.NameOf(title.CivilizationId) + " (" + span + ")");
                officeRefs.Add(title.CivilizationId);
            }

            sections.Add(Section(
                "Offices",
                Subject(figure) + " held " + Join(offices) + ".",
                officeRefs));
        }

        List<EntityId> spouses = Existing(
            world, figure.SpouseIds, spouse => spouse.BirthYear <= year);
        List<EntityId> children = Existing(
            world, figure.ChildIds, child => child.BirthYear <= year);

        if (spouses.Count > 0 || children.Count > 0)
        {
            string household = spouses.Count == 0
                ? "No spouse is named in the surviving record."
                : Subject(figure) + " married " + Names(world, spouses) + ".";

            household += children.Count == 0
                ? " No children are recorded by this date."
                : " The children recorded by this date were " + Names(world, children) + ".";

            sections.Add(Section(
                "Household",
                household,
                References(new[] { figure.Id }.Concat(spouses).Concat(children))));
        }

        List<CampaignRecord> service = CampaignsForFigure(world, figure, year);
        if (service.Count > 0)
        {
            int engagements = 0;
            var wars = new List<EntityId>();
            foreach (CampaignRecord campaign in service)
            {
                engagements += campaign.Battles.Count;
                wars.Add(campaign.War.Id);
            }

            string deeds = Subject(figure) + " commanded "
                           + Count(engagements, "recorded engagement", "recorded engagements")
                           + " in " + Names(world, wars) + ".";

            sections.Add(Section("Wars", deeds, References(new[] { figure.Id }.Concat(wars))));
        }

        return new TomeContents(
            TomeContentKind.Biography,
            figure.Id,
            EntityId.None,
            sections);
    }

    private static TomeContents Campaign(WorldState world, CampaignRecord record, int year)
    {
        Figure figure = record.Figure;
        War war = record.War;
        List<Battle> battles = record.Battles;
        Battle first = battles[0];

        EntityId firstRealm = SideLedBy(first, figure.Id);
        string opening = "The " + war.Name + " began in "
                         + war.StartYear.ToString(CultureInfo.InvariantCulture) + ", "
                         + Warfare.CauseLabel(war.Cause) + ". " + figure.FullName
                         + " first took the field for " + world.NameOf(firstRealm) + " at the "
                         + first.Name + " in " + first.Year.ToString(CultureInfo.InvariantCulture) + ".";

        var sections = new List<TomeSection>
        {
            Section("The war", opening, figure.Id, firstRealm, war.Id, first.Id),
        };

        int wins = 0;
        int ownLosses = 0;
        int firstYear = int.MaxValue;
        int lastYear = int.MinValue;

        foreach (Battle battle in battles)
        {
            EntityId side = SideLedBy(battle, figure.Id);
            if (battle.VictorId == side) wins++;

            ownLosses += battle.AttackerCommanderId == figure.Id
                ? battle.AttackerLosses
                : battle.DefenderLosses;

            firstYear = Math.Min(firstYear, battle.Year);
            lastYear = Math.Max(lastYear, battle.Year);
        }

        int losses = battles.Count - wins;
        string service = Subject(figure) + " commanded "
                         + Count(battles.Count, "engagement", "engagements") + " between "
                         + firstYear.ToString(CultureInfo.InvariantCulture) + " and "
                         + lastYear.ToString(CultureInfo.InvariantCulture) + ": "
                         + Count(wins, "victory", "victories") + " and "
                         + Count(losses, "defeat", "defeats") + ". "
                         + Count(ownLosses, "soldier", "soldiers")
                         + " under " + Possessive(figure).ToLowerInvariant() + " command were lost.";

        sections.Add(Section(
            "Service",
            service,
            References(new[] { figure.Id, war.Id }.Concat(battles.Select(b => b.Id)))));

        var entries = new List<string>();
        var entryRefs = new List<EntityId> { figure.Id, war.Id };
        int shown = Math.Min(3, battles.Count);

        for (int i = 0; i < shown; i++)
        {
            Battle battle = battles[i];
            bool ledAttackers = battle.AttackerCommanderId == figure.Id;
            EntityId side = ledAttackers ? battle.AttackerId : battle.DefenderId;
            int strength = ledAttackers ? battle.AttackerStrength : battle.DefenderStrength;
            int dead = ledAttackers ? battle.AttackerLosses : battle.DefenderLosses;
            string result = battle.VictorId == side ? "prevailed" : "was defeated";

            entries.Add(
                "At the " + battle.Name + " in "
                + battle.Year.ToString(CultureInfo.InvariantCulture) + ", "
                + Subject(figure).ToLowerInvariant() + " led "
                + strength.ToString("N0", CultureInfo.InvariantCulture) + " and " + result
                + "; " + dead.ToString("N0", CultureInfo.InvariantCulture)
                + " of the force were lost"
                + (battle.Sacked ? ", and the settlement was sacked" : string.Empty));

            entryRefs.Add(battle.Id);
            entryRefs.Add(battle.RegionId);
            if (!battle.SettlementId.IsNone) entryRefs.Add(battle.SettlementId);
        }

        sections.Add(Section("Recorded engagements", string.Join(". ", entries) + ".", entryRefs));

        string aftermath;
        var aftermathRefs = new List<EntityId> { war.Id };

        if (war.EndYear is int end && end <= year)
        {
            int dead = war.AttackerLosses + war.DefenderLosses;
            aftermath = "The war ended in " + end.ToString(CultureInfo.InvariantCulture) + ", "
                        + Warfare.OutcomeLabel(war.Outcome) + ". It cost "
                        + Count(dead, "recorded life", "recorded lives") + " and transferred "
                        + Count(war.CededRegionIds.Count, "region", "regions") + ".";
            aftermathRefs.AddRange(war.CededRegionIds);
        }
        else
        {
            aftermath = "The outcome was still unsettled when this account was written in "
                        + year.ToString(CultureInfo.InvariantCulture) + ".";
        }

        sections.Add(Section("Aftermath", aftermath, aftermathRefs));

        return new TomeContents(TomeContentKind.Campaign, figure.Id, war.Id, sections);
    }

    private static List<CampaignRecord> CampaignsForFigure(
        WorldState world, Figure figure, int year)
    {
        var records = new List<CampaignRecord>();

        foreach (War war in world.Wars)
        {
            if (war.StartYear > year) continue;

            CampaignRecord? record = null;
            foreach (EntityId battleId in war.BattleIds)
            {
                if (!world.Battles.Contains(battleId)) continue;

                Battle battle = world.Battles[battleId];
                if (battle.Year > year
                    || (battle.AttackerCommanderId != figure.Id
                        && battle.DefenderCommanderId != figure.Id))
                {
                    continue;
                }

                record ??= new CampaignRecord(figure, war);
                record.Battles.Add(battle);
            }

            if (record is not null) records.Add(record);
        }

        return records;
    }

    // -----------------------------------------------------------------------
    // Treasures
    // -----------------------------------------------------------------------

    /// <summary>Describes why an artifact was made and what was known of it at writing time.</summary>
    private static TomeContents ArtifactHistory(WorldState world, Artifact artifact, int year)
    {
        var references = new List<EntityId> { artifact.Id, artifact.OriginSettlementId };
        string making = artifact.Name + " was made in "
                        + artifact.CreatedYear.ToString(CultureInfo.InvariantCulture)
                        + " at " + world.NameOf(artifact.OriginSettlementId) + ". "
                        + ArtifactPurpose(world, artifact, references);

        var known = new List<ArtifactHolding>();
        foreach (ArtifactHolding holding in artifact.Provenance)
        {
            if (holding.Year > year) break;
            known.Add(holding);
        }

        var journey = new List<string>();
        EntityId lastPlace = EntityId.None;
        int moves = 0;

        foreach (ArtifactHolding holding in known)
        {
            if (!holding.SettlementId.IsNone)
            {
                if (lastPlace.IsNone)
                {
                    journey.Add(
                        "The first record places it at " + world.NameOf(holding.SettlementId)
                        + " in " + holding.Year.ToString(CultureInfo.InvariantCulture) + ".");
                }
                else
                {
                    moves++;
                    journey.Add(
                        "In " + holding.Year.ToString(CultureInfo.InvariantCulture)
                        + " it came to " + world.NameOf(holding.SettlementId)
                        + ", " + holding.How + ".");
                }

                lastPlace = holding.SettlementId;
                references.Add(holding.SettlementId);
            }
            else if (!lastPlace.IsNone)
            {
                journey.Add(
                    "In " + holding.Year.ToString(CultureInfo.InvariantCulture)
                    + " it was lost at " + world.NameOf(lastPlace)
                    + ", " + holding.How + ".");
            }
        }

        ArtifactHolding last = known[^1];
        string lastRecord;
        if (last.SettlementId.IsNone)
        {
            lastRecord = artifact.Name + " had been lost by the writing of this account. "
                         + "Its last recorded location was " + world.NameOf(lastPlace)
                         + "; it was lost there in "
                         + last.Year.ToString(CultureInfo.InvariantCulture)
                         + ", " + last.How + ".";
        }
        else
        {
            lastRecord = "When this account was written in "
                         + year.ToString(CultureInfo.InvariantCulture) + ", "
                         + artifact.Name + " was last recorded at "
                         + world.NameOf(last.SettlementId) + ". The record preserved "
                         + Count(moves, "change of hands", "changes of hands") + ".";
        }

        return new TomeContents(
            TomeContentKind.ArtifactHistory,
            artifact.Id,
            EntityId.None,
            new[]
            {
                Section("Making", making, references),
                Section("Recorded journey", string.Join(" ", journey), references),
                Section("Last record", lastRecord, references),
            });
    }

    private static string ArtifactPurpose(
        WorldState world, Artifact artifact, List<EntityId> references)
    {
        if (!artifact.CreatorId.IsNone && world.Figures.Contains(artifact.CreatorId))
        {
            references.Add(artifact.CreatorId);
        }

        if (!artifact.ReligionId.IsNone && world.Religions.Contains(artifact.ReligionId))
        {
            references.Add(artifact.ReligionId);
        }

        return artifact.Kind switch
        {
            ArtifactKind.Regalia when !artifact.CreatorId.IsNone
                => "It was made for " + world.NameOf(artifact.CreatorId)
                   + " as a visible sign of the right to rule.",
            ArtifactKind.Regalia
                => "It was made as a visible sign of its ruler's authority.",
            ArtifactKind.Weapon
                => "It was fashioned as a weapon for war and defence.",
            ArtifactKind.Relic when !artifact.ReligionId.IsNone
                => "It was made for veneration by followers of the "
                   + world.NameOf(artifact.ReligionId) + ".",
            ArtifactKind.Relic
                => "It was preserved as a sacred object for veneration.",
            ArtifactKind.Idol when !artifact.ReligionId.IsNone
                => "It was made as an image of worship for followers of the "
                   + world.NameOf(artifact.ReligionId) + ".",
            ArtifactKind.Idol
                => "It was made as an image for worship.",
            ArtifactKind.Jewel
                => "It was made to display wealth and the skill of its makers.",
            ArtifactKind.Tome when artifact.TomeContents is TomeContents contents
                => WrittenPurpose(world, contents, references),
            ArtifactKind.Tome
                => "It was written to preserve an account for later readers.",
            _ => "Its recorded form explains the purpose for which it was made.",
        };
    }

    private static string WrittenPurpose(
        WorldState world, TomeContents contents, List<EntityId> references)
    {
        references.Add(contents.SubjectId);
        if (!contents.ContextId.IsNone) references.Add(contents.ContextId);

        string form = contents.Kind switch
        {
            TomeContentKind.Biography => "a life",
            TomeContentKind.Campaign => "a campaign account",
            TomeContentKind.ReligiousRite => "a body of rites",
            TomeContentKind.ReligiousTeaching => "religious teaching",
            TomeContentKind.ArtifactHistory => "the history of another artifact",
            _ => "local annals",
        };

        return "It was written to preserve " + form + " concerning "
               + world.NameOf(contents.SubjectId) + ".";
    }

    /// <summary>The artifact's holder at a given year, or none once its loss was recorded.</summary>
    private static EntityId HoldingAt(Artifact artifact, int year)
    {
        EntityId holder = EntityId.None;

        foreach (ArtifactHolding holding in artifact.Provenance)
        {
            if (holding.Year > year) break;
            holder = holding.SettlementId;
        }

        return holder;
    }

    // -----------------------------------------------------------------------
    // Faith
    // -----------------------------------------------------------------------

    private static readonly string[] RiteNames =
    {
        "Vigil of Lamps",
        "Rite of the Open Hand",
        "Procession of Ash",
        "Keeping of Names",
        "Washing at First Light",
        "Feast of Returning",
    };

    private static readonly string[] RiteTimes =
    {
        "at first light",
        "after sunset",
        "at the turning of each season",
        "on the anniversary of the faith's first preaching",
        "before a household begins a journey",
    };

    private static readonly string[] RiteActions =
    {
        "stand in a circle while the names of their dead are spoken",
        "wash their hands and touch the threshold of the sanctuary",
        "walk three times around the gathering place in silence",
        "light one lamp from another until the assembly is illuminated",
        "share bread before reciting the promises of the community",
    };

    private static readonly string[] RiteOfferings =
    {
        "bread and salt",
        "oil for the sanctuary lamps",
        "a ribbon bearing the name of an ancestor",
        "the first cup of the season's drink",
        "flowers gathered outside the settlement walls",
    };

    private static readonly string[] RitePurposes =
    {
        "to remember obligations between the living and the dead",
        "to ask safe passage through uncertainty",
        "to reconcile neighbours before witnesses",
        "to mark gratitude for what the community has preserved",
        "to renew the promises made at the faith's founding",
    };

    private static TomeContents ReligiousRite(WorldState world, Religion religion, int year)
    {
        IRng lore = world.Root.Fork("religion.rite", religion.Id.ToDiscriminator());
        string name = lore.Pick(RiteNames);
        string observance = "The " + name + " is observed " + lore.Pick(RiteTimes)
                            + ". Worshippers " + lore.Pick(RiteActions) + ".";
        string offering = "An offering of " + lore.Pick(RiteOfferings) + " is made "
                          + lore.Pick(RitePurposes) + ".";

        return new TomeContents(
            TomeContentKind.ReligiousRite,
            religion.Id,
            EntityId.None,
            new[]
            {
                ReligionOrigins(world, religion, year),
                Section(name, observance, religion.Id),
                Section("Offering and purpose", offering, religion.Id),
            });
    }

    private static readonly string[] Teachings =
    {
        "Memory binds the living to those who came before.",
        "A promise witnessed by the community is sacred.",
        "Power is held in trust and is judged by what it preserves.",
        "The stranger and the neighbour are owed the same honest measure.",
        "Loss must be named before renewal can begin.",
        "Wisdom is proved by restraint when vengeance is possible.",
    };

    private static readonly string[] Instructions =
    {
        "Followers are instructed to settle disputes before sharing a ceremonial meal.",
        "Each household is to keep the names of its dead and recite them once each year.",
        "Travellers are to be offered water before they are asked their business.",
        "A leader must hear a grievance in public before passing judgment.",
        "Debts of food are forgiven after a failed harvest, but debts of violence require witness.",
        "One day in each season is reserved for repairing a work held in common.",
    };

    private static TomeContents ReligiousTeaching(
        WorldState world, Religion religion, int year)
    {
        IRng lore = world.Root.Fork("religion.teaching", religion.Id.ToDiscriminator());
        string authority = religion.Fervour >= 0.65
            ? "The text commands: "
            : "The text teaches: ";

        return new TomeContents(
            TomeContentKind.ReligiousTeaching,
            religion.Id,
            EntityId.None,
            new[]
            {
                ReligionOrigins(world, religion, year),
                Section("First principle", authority + lore.Pick(Teachings), religion.Id),
                Section("Instruction", lore.Pick(Instructions), religion.Id),
            });
    }

    private static TomeSection ReligionOrigins(WorldState world, Religion religion, int year)
    {
        var references = new List<EntityId> { religion.Id, religion.OriginSettlementId };
        string text = "The " + religion.Name + " was first preached in "
                      + religion.FoundedYear.ToString(CultureInfo.InvariantCulture) + " at "
                      + world.NameOf(religion.OriginSettlementId);

        if (!religion.FounderId.IsNone && world.Figures.Contains(religion.FounderId))
        {
            text += " by " + world.NameOf(religion.FounderId);
            references.Add(religion.FounderId);
        }

        text += ".";

        if (!religion.ParentId.IsNone && world.Religions.Contains(religion.ParentId))
        {
            text += " It broke from the " + world.NameOf(religion.ParentId) + ".";
            references.Add(religion.ParentId);
        }

        if (religion.EndedYear is int ended && ended <= year)
        {
            text += " Its last congregation was gone by "
                    + ended.ToString(CultureInfo.InvariantCulture) + ".";
        }

        return Section("Origins", text, references);
    }

    // -----------------------------------------------------------------------
    // Local annals
    // -----------------------------------------------------------------------

    private static TomeContents Annals(
        WorldState world, Settlement settlement, Civilization civilization, int year)
    {
        string founding = settlement.Name + " was founded in "
                          + settlement.FoundedYear.ToString(CultureInfo.InvariantCulture) + " by "
                          + civilization.Name + " in " + world.NameOf(settlement.RegionId) + ".";

        string character = "At the writing of these annals it was a "
                           + settlement.Tier.ToString().ToLowerInvariant() + " of "
                           + settlement.Population.ToString("N0", CultureInfo.InvariantCulture)
                           + " people, known for "
                           + Specializations.Label(settlement.Specialization) + ".";

        var characterRefs = new List<EntityId> { settlement.Id, civilization.Id };
        if (!settlement.ReligionId.IsNone && world.Religions.Contains(settlement.ReligionId))
        {
            character += " Its people followed the " + world.NameOf(settlement.ReligionId) + ".";
            characterRefs.Add(settlement.ReligionId);
        }

        var entries = new List<HistoryEvent>();
        foreach (HistoryEvent entry in world.Chronicle.Events)
        {
            if (entry.Year <= year && entry.References().Contains(settlement.Id)) entries.Add(entry);
        }

        int first = Math.Max(0, entries.Count - 4);
        var lines = new List<string>();
        var eventRefs = new List<EntityId> { settlement.Id };

        for (int i = first; i < entries.Count; i++)
        {
            HistoryEvent entry = entries[i];
            lines.Add(entry.Year.ToString(CultureInfo.InvariantCulture) + ": " + world.Narrate(entry));
            eventRefs.AddRange(entry.References());
        }

        if (lines.Count == 0) lines.Add("No event beyond the settlement's founding was recorded.");

        return new TomeContents(
            TomeContentKind.Annals,
            settlement.Id,
            EntityId.None,
            new[]
            {
                Section("Foundation", founding, settlement.Id, civilization.Id, settlement.RegionId),
                Section("The place", character, characterRefs),
                Section("Selected entries", string.Join(" ", lines), eventRefs),
            });
    }

    // -----------------------------------------------------------------------
    // Prose and references
    // -----------------------------------------------------------------------

    private static EntityId SideLedBy(Battle battle, EntityId figureId) =>
        battle.AttackerCommanderId == figureId ? battle.AttackerId : battle.DefenderId;

    private static string Subject(Figure figure) => figure.Sex == Sex.Female ? "She" : "He";

    private static string Possessive(Figure figure) => figure.Sex == Sex.Female ? "Her" : "His";

    private static TomeSection Section(
        string heading, string text, params EntityId[] references) =>
        new(heading, text, References(references));

    private static TomeSection Section(
        string heading, string text, IEnumerable<EntityId> references) =>
        new(heading, text, References(references));

    private static EntityId[] References(IEnumerable<EntityId> ids)
    {
        var references = new List<EntityId>();
        foreach (EntityId id in ids)
        {
            if (!id.IsNone && !references.Contains(id)) references.Add(id);
        }

        return references.ToArray();
    }

    private static List<EntityId> Existing(
        WorldState world,
        IEnumerable<EntityId> ids,
        Func<Figure, bool> include)
    {
        var existing = new List<EntityId>();
        foreach (EntityId id in ids)
        {
            if (world.Figures.Contains(id) && include(world.Figures[id])) existing.Add(id);
        }

        return existing;
    }

    private static string Names(WorldState world, IReadOnlyList<EntityId> ids)
    {
        var names = new List<string>(ids.Count);
        foreach (EntityId id in ids) names.Add(world.NameOf(id));
        return Join(names);
    }

    private static string Join(IReadOnlyList<string> items)
    {
        if (items.Count == 0) return string.Empty;
        if (items.Count == 1) return items[0];
        if (items.Count == 2) return items[0] + " and " + items[1];

        return string.Join(", ", items.Take(items.Count - 1)) + ", and " + items[^1];
    }

    private static string Count(int value, string singular, string plural) =>
        value.ToString("N0", CultureInfo.InvariantCulture) + " " + (value == 1 ? singular : plural);
}

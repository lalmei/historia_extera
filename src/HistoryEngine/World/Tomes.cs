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
/// they are keyed to the faith's character rather than to the book. Two codices of one religion
/// therefore agree about its gods, its sins and its observance instead of inventing a fresh
/// religion each time.</para>
/// </remarks>
public static class Tomes
{
    /// <summary>Additional settlements that can receive one work before it ceases to be scarce.</summary>
    private const int MaximumCopies = 5;

    /// <summary>How far a later scribe may misremember an event, in years, at no learning.</summary>
    private const int MemoryDriftYears = 12;

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
        int year,
        Figure? patron = null,
        TomeContentKind? requested = null,
        EntityId requestedSubject = default,
        EntityId requestedContext = default)
    {
        IRng rng = world.Root.Fork("tome.contents", artifactId.ToDiscriminator());

        List<Figure> figures = NotableFigures(world, civilization, year);
        List<CampaignRecord> campaigns = Campaigns(world, civilization, year);
        List<Artifact> artifacts = ArtifactSubjects(world, settlement, civilization, year);
        Religion? religion = ReligionAt(world, settlement, year);
        List<HolySite> dedications = DedicationSubjects(world, settlement, year);
        double learning = Scholarliness(world, civilization, patron);

        TomeContents contents = requested is TomeContentKind kind
            ? Requested(
                world, settlement, civilization, rng, year, learning, kind,
                requestedSubject, requestedContext, figures, campaigns, artifacts, religion, dedications)
            : Choose(
                world, settlement, civilization, rng, year, learning, figures, campaigns,
                artifacts, religion, dedications);

        contents.CopyLimit = CopyLimit(world, settlement, civilization, artifactId, contents.Kind, patron);
        return contents;
    }

    /// <summary>
    /// Continues local and realm chronicles when enough has happened since they were last opened.
    /// </summary>
    /// <remarks>
    /// The engine's own event log is append-only. These are the in-world books: a later court
    /// adds a dated gathering of recent entries rather than rewriting what an earlier scribe set
    /// down, so a reader can see both the original account and the update.
    /// </remarks>
    public static void Revise(WorldState world, int year)
    {
        foreach (Artifact artifact in world.Artifacts)
        {
            TomeContents? contents = artifact.TomeContents;
            if (contents is null
                || !artifact.IsExtant
                || contents.Kind is not (TomeContentKind.Annals or TomeContentKind.RealmChronicle))
            {
                continue;
            }

            int last = Math.Max(artifact.CreatedYear, contents.LatestYear);
            if (year < last + 12) continue;

            IRng rng = world.Root
                .Fork("tome.revise", artifact.Id.ToDiscriminator())
                .Fork("year", year);

            if (!world.Settlements.Contains(artifact.HolderId)
                || !world.Settlements[artifact.HolderId].IsActive)
            {
                continue;
            }

            Settlement seat = world.Settlements[artifact.HolderId];
            if (!world.Civilizations.Contains(seat.CivilizationId)) continue;

            Civilization civilization = world.Civilizations[seat.CivilizationId];
            Figure? patron = LivingPatron(world, civilization, artifact);
            double learning = Scholarliness(world, civilization, patron);
            if (!rng.Chance(0.10 + (learning * 0.18))) continue;

            TomeSection? continuation = Continuation(
                world, contents, last, year, rng, learning);
            if (continuation is null) continue;

            contents.Continue(continuation);

            world.Chronicle.Record(
                year,
                EventKind.ArtifactRevised,
                artifact.Id,
                obj: patron?.Id ?? EntityId.None,
                location: seat.Id);
        }
    }

    /// <summary>
    /// A ruler, high priest or house may pay for a particular work this year.
    /// </summary>
    public static void Commission(WorldState world, int year)
    {
        foreach (Civilization civilization in world.ActiveCivilizations())
        {
            if (civilization.CapitalId.IsNone
                || !world.Settlements.Contains(civilization.CapitalId))
            {
                continue;
            }

            Settlement seat = world.Settlements[civilization.CapitalId];
            if (!seat.IsActive || seat.Tier < SettlementTier.Town) continue;
            if (HeldByTown(world, seat.Id) >= 3) continue;

            IRng rng = world.Root.Fork("tome.commission", civilization.Id.ToDiscriminator())
                .Fork("year", year);

            PatronKind who = rng.NextInt(5) switch
            {
                0 => PatronKind.Priest,
                1 => PatronKind.House,
                2 => PatronKind.Scribe,
                3 => PatronKind.Merchant,
                _ => PatronKind.Ruler,
            };

            TryCommission(world, civilization, seat, year, rng.Fork("patron"), who);
        }
    }

    private static TomeContents Choose(
        WorldState world,
        Settlement settlement,
        Civilization civilization,
        IRng rng,
        int year,
        double learning,
        List<Figure> figures,
        List<CampaignRecord> campaigns,
        List<Artifact> artifacts,
        Religion? religion,
        List<HolySite> dedications)
    {
        var possible = new List<TomeContentKind>(6);
        if (figures.Count > 0) possible.Add(TomeContentKind.Biography);
        if (campaigns.Count > 0) possible.Add(TomeContentKind.Campaign);
        if (Travellers(figures, year).Count > 0) possible.Add(TomeContentKind.Itinerary);

        if (religion is not null)
        {
            possible.Add(TomeContentKind.ReligiousRite);
            possible.Add(TomeContentKind.ReligiousTeaching);
        }

        possible.Add(TomeContentKind.Annals);

        TomeContentKind kind;
        if (dedications.Count > 0 && rng.Fork("dedication").Chance(0.10))
        {
            kind = TomeContentKind.Dedication;
        }
        else if (religion is not null && rng.Fork("cosmology").Chance(0.08 + (learning * 0.08)))
        {
            kind = TomeContentKind.Cosmology;
        }
        else if (rng.Fork("realm").Chance(0.12))
        {
            kind = TomeContentKind.RealmChronicle;
        }
        else if (artifacts.Count > 0 && rng.Fork("artifact-history").Chance(0.10 + (learning * 0.08)))
        {
            kind = TomeContentKind.ArtifactHistory;
        }
        else
        {
            kind = rng.Pick(possible);
        }

        return Write(
            world, settlement, civilization, rng, year, learning, kind,
            EntityId.None, EntityId.None, figures, campaigns, artifacts, religion, dedications);
    }

    private static TomeContents Requested(
        WorldState world,
        Settlement settlement,
        Civilization civilization,
        IRng rng,
        int year,
        double learning,
        TomeContentKind kind,
        EntityId subject,
        EntityId context,
        List<Figure> figures,
        List<CampaignRecord> campaigns,
        List<Artifact> artifacts,
        Religion? religion,
        List<HolySite> dedications) =>
        Write(
            world, settlement, civilization, rng, year, learning, kind,
            subject, context, figures, campaigns, artifacts, religion, dedications);

    private static TomeContents Write(
        WorldState world,
        Settlement settlement,
        Civilization civilization,
        IRng rng,
        int year,
        double learning,
        TomeContentKind kind,
        EntityId subject,
        EntityId context,
        List<Figure> figures,
        List<CampaignRecord> campaigns,
        List<Artifact> artifacts,
        Religion? religion,
        List<HolySite> dedications)
    {
        return kind switch
        {
            TomeContentKind.Biography when PickFigure(world, rng, subject, figures) is Figure figure
                => Biography(world, figure, year, rng, learning),
            TomeContentKind.Campaign when PickCampaign(world, rng, subject, context, campaigns) is CampaignRecord campaign
                => Campaign(world, campaign, year, rng, learning),
            TomeContentKind.ArtifactHistory when PickTreasure(world, rng, subject, artifacts) is Artifact treasure
                => ArtifactHistory(world, treasure, year),
            TomeContentKind.ReligiousRite when religion is not null
                => ReligiousRite(world, religion, year),
            TomeContentKind.ReligiousTeaching when religion is not null
                => ReligiousTeaching(world, religion, year),
            TomeContentKind.Cosmology when religion is not null
                => Cosmology(world, religion, year, rng, learning),
            TomeContentKind.Dedication when PickDedication(world, rng, subject, dedications) is HolySite site
                => Dedication(world, site, year),
            TomeContentKind.Itinerary when PickTraveller(world, rng, subject, figures, year) is Figure traveller
                => Itinerary(world, traveller, year),
            TomeContentKind.RealmChronicle
                => RealmChronicle(world, civilization, year, rng, learning),
            _ => Annals(world, settlement, civilization, year),
        };
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
            if (sources.Count == 0 || !rng.Chance(AnnualCopyChance(world, contents, sources))) continue;

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
        TomeContentKind kind,
        Figure? patron)
    {
        CultureValues values = world.CultureOf(civilization).Values;
        IRng rng = world.Root.Fork("tome.circulation", artifactId.ToDiscriminator());
        double learning = Scholarliness(world, civilization, patron);

        double eligibility = 0.62
            + (values.Tradition * 0.10)
            + (values.Mercantile * 0.08)
            + (learning * 0.08);

        if (IsReligious(kind)) eligibility += 0.08;
        if (IsBookHub(world, settlement)) eligibility += 0.06;
        if (HasScriptorium(world, settlement)) eligibility += 0.10;
        if (settlement.Tier == SettlementTier.City) eligibility += 0.04;
        if (settlement.IsCapital) eligibility += 0.03;
        if (KnowsByWriting(world, settlement)) eligibility += 0.06;

        if (!rng.Chance(DetMath.Clamp(eligibility, 0.50, 0.92))) return 0;

        int limit = 1;
        if (rng.Chance(0.48 + (values.Mercantile * 0.12))) limit++;
        if (rng.Chance(0.18 + (values.Tradition * 0.12))) limit++;
        if (rng.Chance(0.05 + (IsReligious(kind) ? 0.05 : 0.0))) limit++;
        if (HasScriptorium(world, settlement) && rng.Chance(0.45 + (learning * 0.25))) limit++;
        if (patron is not null && patron.Disposition.Values.Learning >= 0.72 && rng.Chance(0.40))
        {
            limit++;
        }

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

    private static double AnnualCopyChance(
        WorldState world, TomeContents contents, IReadOnlyList<Settlement> sources)
    {
        double chance = 0.13 + (IsReligious(contents.Kind) ? 0.03 : 0.0);

        foreach (Settlement source in sources)
        {
            if (IsBookHub(world, source)) chance += 0.04;
            if (HasScriptorium(world, source)) chance += 0.06;
            if (source.IsCapital) chance += 0.02;
            if (source.Tier == SettlementTier.City) chance += 0.02;
            if (chance >= 0.32) break;
        }

        return DetMath.Clamp(chance, 0.10, 0.32);
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
                    + (IsBookHub(world, destination) ? 0.5 : 0.0)
                    + (HasScriptorium(world, destination) ? 0.8 : 0.0)
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
        kind is TomeContentKind.ReligiousRite
            or TomeContentKind.ReligiousTeaching
            or TomeContentKind.Cosmology
            or TomeContentKind.Dedication;

    private static bool IsBookHub(WorldState world, Settlement settlement) =>
        settlement.Specialization is SettlementSpecialization.Trade
            or SettlementSpecialization.Crafts
            or SettlementSpecialization.Shrine
        || HasScriptorium(world, settlement);

    /// <summary>A monastery at or beside this town, where copying is ordinary work.</summary>
    public static bool HasScriptorium(WorldState world, Settlement settlement) =>
        ScriptoriumAt(world, settlement) is not null;

    /// <summary>
    /// The monastery that makes this town a place to fetch copies from, if there is one.
    /// </summary>
    /// <remarks>
    /// The same question <see cref="HasScriptorium"/> asks, answered with the house rather than
    /// with a yes. A scribe sent for copies is sent to a particular monastery and the chronicle
    /// should be able to name it; every caller that only wants the yes goes through the predicate
    /// above, which is now one line over this.
    /// </remarks>
    public static HolySite? ScriptoriumAt(WorldState world, Settlement settlement)
    {
        foreach (HolySite site in world.HolySites)
        {
            if (site.Kind != HolySiteKind.Monastery || site.FoundedYear > world.Now.Year)
            {
                continue;
            }

            if (site.SettlementId == settlement.Id) return site;
            if (site.SettlementId.IsNone && site.RegionId == settlement.RegionId) return site;
        }

        return null;
    }

    private static bool KnowsByWriting(WorldState world, Settlement settlement)
    {
        if (settlement.ReligionId.IsNone || !world.Religions.Contains(settlement.ReligionId))
        {
            return false;
        }

        return world.Religions[settlement.ReligionId].Character.Dogma == DogmaEmphasis.Knowledge;
    }

    private enum PatronKind
    {
        Ruler = 0,
        Priest = 1,
        House = 2,
        Scribe = 3,
        Merchant = 4,
    }

    private static void TryCommission(
        WorldState world,
        Civilization civilization,
        Settlement seat,
        int year,
        IRng rng,
        PatronKind who)
    {
        Figure? patron = who switch
        {
            PatronKind.Priest => Offices.HolderOf(world, civilization, OfficeKind.HighPriest),
            PatronKind.House => HouseHead(world, civilization, year),
            PatronKind.Scribe => LivingOf(world, civilization, year, Occupation.Scribe),
            PatronKind.Merchant => LivingOf(world, civilization, year, Occupation.Merchant),
            _ => LivingRuler(world, civilization),
        };

        if (patron is null) return;

        double learning = patron.Disposition.Values.Learning;
        double piety = patron.Disposition.Values.Piety;
        double chance = who switch
        {
            PatronKind.Priest => 0.006 + (piety * 0.012) + (learning * 0.006),
            PatronKind.House => 0.004 + (learning * 0.008),
            PatronKind.Scribe => 0.008 + (learning * 0.016),
            PatronKind.Merchant => 0.005 + (patron.Disposition.Values.Mercantile * 0.010),
            _ => 0.005 + (learning * 0.014),
        };

        if (!rng.Chance(chance)) return;

        TomeContentKind kind;
        EntityId subject = EntityId.None;
        EntityId context = EntityId.None;

        if (who == PatronKind.Priest)
        {
            Religion? faith = ReligionAt(world, seat, year);
            List<HolySite> sites = DedicationSubjects(world, seat, year);
            if (faith is not null && rng.Chance(0.35 + (learning * 0.15)))
            {
                kind = TomeContentKind.Cosmology;
                subject = faith.Id;
            }
            else if (sites.Count > 0 && rng.Chance(0.45))
            {
                HolySite site = rng.Pick(sites);
                kind = TomeContentKind.Dedication;
                subject = site.Id;
                context = site.Description.DedicateeId;
            }
            else if (faith is not null)
            {
                kind = rng.Chance(0.5) ? TomeContentKind.ReligiousTeaching : TomeContentKind.ReligiousRite;
                subject = faith.Id;
            }
            else
            {
                return;
            }
        }
        else if (who == PatronKind.Merchant)
        {
            if (HasJourney(patron, year))
            {
                kind = TomeContentKind.Itinerary;
                subject = patron.Id;
            }
            else
            {
                kind = TomeContentKind.Biography;
                subject = patron.Id;
            }
        }
        else if (who == PatronKind.Scribe)
        {
            Figure? traveller = FirstTraveller(world, civilization, year);
            if (traveller is not null && rng.Chance(0.45))
            {
                kind = TomeContentKind.Itinerary;
                subject = traveller.Id;
            }
            else
            {
                kind = TomeContentKind.Biography;
                subject = HouseSubject(world, patron);
            }
        }
        else if (who == PatronKind.House && !patron.DynastyId.IsNone)
        {
            kind = TomeContentKind.Biography;
            subject = HouseSubject(world, patron);
        }
        else if (learning >= 0.70 && rng.Chance(0.40))
        {
            List<CampaignRecord> oldWars = Campaigns(world, civilization, year);
            if (oldWars.Count > 0 && rng.Chance(0.5))
            {
                CampaignRecord campaign = OldestCampaign(oldWars);
                kind = TomeContentKind.Campaign;
                subject = campaign.Figure.Id;
                context = campaign.War.Id;
            }
            else
            {
                kind = TomeContentKind.RealmChronicle;
                subject = civilization.Id;
            }
        }
        else if (rng.Chance(0.45) && !patron.Id.IsNone)
        {
            kind = TomeContentKind.Biography;
            subject = patron.Id;
        }
        else if (Campaigns(world, civilization, year).Count > 0 && rng.Chance(0.5))
        {
            CampaignRecord campaign = rng.Pick(Campaigns(world, civilization, year));
            kind = TomeContentKind.Campaign;
            subject = campaign.Figure.Id;
            context = campaign.War.Id;
        }
        else
        {
            kind = TomeContentKind.RealmChronicle;
            subject = civilization.Id;
        }

        EntityId preview = world.Artifacts.NextId;
        TomeContents contents = Compose(
            world, seat, civilization, preview, year, patron, kind, subject, context);

        Treasures.Create(
            world,
            seat,
            ArtifactKind.Tome,
            patron.Id,
            IsReligious(kind) ? seat.ReligionId : EntityId.None,
            year,
            patron.Id,
            contents);
    }

    private static int HeldByTown(WorldState world, EntityId settlementId) =>
        Treasures.HeldBy(world, settlementId).Count;

    private static Figure? LivingRuler(WorldState world, Civilization civilization)
    {
        if (civilization.CurrentRulerId.IsNone || !world.Figures.Contains(civilization.CurrentRulerId))
        {
            return null;
        }

        Figure ruler = world.Figures[civilization.CurrentRulerId];
        return ruler.IsAlive ? ruler : null;
    }

    private static Figure? LivingOf(
        WorldState world, Civilization civilization, int year, Occupation occupation)
    {
        Figure? found = null;
        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive || figure.CivilizationId != civilization.Id) continue;
            if (figure.AgeIn(year) < Succession.MajorityAge) continue;
            if (figure.Occupation != occupation) continue;
            if (found is null || figure.Id.CompareTo(found.Id) < 0) found = figure;
        }

        return found;
    }

    private static Figure? FirstTraveller(WorldState world, Civilization civilization, int year)
    {
        Figure? found = null;
        foreach (Figure figure in world.Figures)
        {
            if (!figure.IsAlive || figure.CivilizationId != civilization.Id) continue;
            if (!HasJourney(figure, year)) continue;
            if (found is null || figure.Id.CompareTo(found.Id) < 0) found = figure;
        }

        return found;
    }

    private static Figure? LivingPatron(WorldState world, Civilization civilization, Artifact artifact)
    {
        if (!artifact.OwnerId.IsNone
            && world.Figures.Contains(artifact.OwnerId)
            && world.Figures[artifact.OwnerId].IsAlive)
        {
            return world.Figures[artifact.OwnerId];
        }

        return LivingRuler(world, civilization);
    }

    private static Figure? HouseHead(WorldState world, Civilization civilization, int year)
    {
        if (civilization.RulingDynastyId.IsNone
            || !world.Dynasties.Contains(civilization.RulingDynastyId))
        {
            return null;
        }

        Dynasty house = world.Dynasties[civilization.RulingDynastyId];
        Figure? eldest = null;
        foreach (EntityId id in house.MemberIds)
        {
            if (!world.Figures.Contains(id)) continue;
            Figure member = world.Figures[id];
            if (!member.IsAlive || member.AgeIn(year) < Succession.MajorityAge) continue;
            if (member.Id == civilization.CurrentRulerId) continue;
            if (eldest is null || member.BirthYear < eldest.BirthYear) eldest = member;
        }

        return eldest;
    }

    private static EntityId HouseSubject(WorldState world, Figure patron)
    {
        if (!world.Dynasties.Contains(patron.DynastyId)) return patron.Id;

        EntityId founder = world.Dynasties[patron.DynastyId].FounderId;
        return world.Figures.Contains(founder) ? founder : patron.Id;
    }

    private static CampaignRecord OldestCampaign(List<CampaignRecord> campaigns)
    {
        CampaignRecord oldest = campaigns[0];
        foreach (CampaignRecord campaign in campaigns)
        {
            if (campaign.War.StartYear < oldest.War.StartYear) oldest = campaign;
        }

        return oldest;
    }

    private static TomeSection? Continuation(
        WorldState world,
        TomeContents contents,
        int since,
        int year,
        IRng rng,
        double learning)
    {
        EntityId subject = contents.SubjectId;
        var entries = new List<HistoryEvent>();
        foreach (HistoryEvent entry in world.Chronicle.Events)
        {
            if (entry.Year <= since || entry.Year > year) continue;
            if (!entry.References().Contains(subject)) continue;
            entries.Add(entry);
        }

        if (entries.Count == 0) return null;

        int first = Math.Max(0, entries.Count - 4);
        var lines = new List<string>();
        var refs = new List<EntityId> { subject };

        for (int i = first; i < entries.Count; i++)
        {
            HistoryEvent entry = entries[i];
            string line = entry.Year.ToString(CultureInfo.InvariantCulture) + ": " + world.Narrate(entry);
            if (Misremembers(rng, learning, year - entry.Year))
            {
                line += " The later hand is uncertain of the lesser names.";
            }

            lines.Add(line);
            refs.AddRange(entry.References());
        }

        return Section(
            "Continuation, " + year.ToString(CultureInfo.InvariantCulture),
            string.Join(" ", lines),
            refs) with { Year = year };
    }

    private static double Scholarliness(WorldState world, Civilization civilization, Figure? patron)
    {
        double court = world.ValuesFor(civilization).Learning;
        return patron is null
            ? court
            : DetMath.Clamp01((court * 0.45) + (patron.Disposition.Values.Learning * 0.55));
    }

    private static bool Misremembers(IRng rng, double learning, int age)
    {
        if (age < 25) return false;

        double fidelity = 0.58 + (learning * 0.38) - (Math.Min(age, 180) * 0.0018);
        return !rng.Fork("memory", age).Chance(DetMath.Clamp(fidelity, 0.22, 0.92));
    }

    private static Figure? PickFigure(
        WorldState world, IRng rng, EntityId subject, List<Figure> figures)
    {
        if (!subject.IsNone && world.Figures.Contains(subject)) return world.Figures[subject];
        return figures.Count == 0 ? null : rng.Pick(figures);
    }

    private static Figure? PickTraveller(
        WorldState world, IRng rng, EntityId subject, List<Figure> figures, int year)
    {
        if (!subject.IsNone && world.Figures.Contains(subject) && HasJourney(world.Figures[subject], year))
        {
            return world.Figures[subject];
        }

        List<Figure> travellers = Travellers(figures, year);
        return travellers.Count == 0 ? null : rng.Pick(travellers);
    }

    private static List<Figure> Travellers(List<Figure> figures, int year)
    {
        var travellers = new List<Figure>();
        foreach (Figure figure in figures)
        {
            if (HasJourney(figure, year)) travellers.Add(figure);
        }

        return travellers;
    }

    private static bool HasJourney(Figure figure, int year)
    {
        foreach (Journey journey in figure.Journeys)
        {
            if (journey.Year <= year) return true;
        }

        return false;
    }

    private static List<Journey> JourneysBy(Figure figure, int year)
    {
        var trips = new List<Journey>();
        foreach (Journey journey in figure.Journeys)
        {
            if (journey.Year <= year) trips.Add(journey);
        }

        return trips;
    }

    private static CampaignRecord? PickCampaign(
        WorldState world, IRng rng, EntityId subject, EntityId context, List<CampaignRecord> campaigns)
    {
        CampaignRecord? requested = SubjectCampaign(world, subject, context, campaigns);
        if (!subject.IsNone || !context.IsNone) return requested;
        return campaigns.Count == 0 ? null : rng.Pick(campaigns);
    }

    private static Artifact? PickTreasure(
        WorldState world, IRng rng, EntityId subject, List<Artifact> artifacts)
    {
        if (!subject.IsNone && world.Artifacts.Contains(subject)) return world.Artifacts[subject];
        return artifacts.Count == 0 ? null : rng.Fork("artifact-subject").Pick(artifacts);
    }

    private static HolySite? PickDedication(
        WorldState world, IRng rng, EntityId subject, List<HolySite> dedications)
    {
        if (!subject.IsNone && world.HolySites.Contains(subject)) return world.HolySites[subject];
        return dedications.Count == 0 ? null : rng.Pick(dedications);
    }

    private static CampaignRecord? SubjectCampaign(
        WorldState world, EntityId subject, EntityId context, List<CampaignRecord> campaigns)
    {
        foreach (CampaignRecord campaign in campaigns)
        {
            if ((subject.IsNone || campaign.Figure.Id == subject)
                && (context.IsNone || campaign.War.Id == context))
            {
                return campaign;
            }
        }

        if (!subject.IsNone && world.Figures.Contains(subject) && !context.IsNone && world.Wars.Contains(context))
        {
            var record = new CampaignRecord(world.Figures[subject], world.Wars[context]);
            foreach (EntityId battleId in world.Wars[context].BattleIds)
            {
                if (!world.Battles.Contains(battleId)) continue;
                Battle battle = world.Battles[battleId];
                if (battle.AttackerCommanderId == subject || battle.DefenderCommanderId == subject)
                {
                    record.Battles.Add(battle);
                }
            }

            return record.Battles.Count == 0 ? null : record;
        }

        return campaigns.Count == 0 ? null : campaigns[0];
    }

    private static List<HolySite> DedicationSubjects(
        WorldState world, Settlement settlement, int year)
    {
        var sites = new List<HolySite>();
        foreach (HolySite site in world.HolySites)
        {
            if (site.FoundedYear > year) continue;
            if (site.ReligionId != settlement.ReligionId && site.SettlementId != settlement.Id)
            {
                continue;
            }

            sites.Add(site);
        }

        return sites;
    }

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

    private static TomeContents Biography(
        WorldState world, Figure figure, int year, IRng rng, double learning)
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

        List<Journey> trips = JourneysBy(figure, year);
        if (trips.Count > 0)
        {
            var places = new List<EntityId> { figure.Id };
            var legs = new List<string>(trips.Count);
            foreach (Journey trip in trips)
            {
                legs.Add(world.NameOf(trip.ToSettlementId) + " in "
                         + trip.Year.ToString(CultureInfo.InvariantCulture));
                places.Add(trip.ToSettlementId);
            }

            sections.Add(Section(
                "Travels",
                Subject(figure) + " was recorded on the road to " + Join(legs) + ".",
                places));
        }

        if (figure.DeathYear is int died
            && year - died >= 40
            && Misremembers(rng, learning, year - died))
        {
            sections.Add(Section(
                "Later memory",
                "Scribes writing long after "
                    + figure.FullName
                    + "'s death disagree about the lesser offices "
                    + Subject(figure).ToLowerInvariant()
                    + " held, and some place events a handful of years from where the older record puts them.",
                figure.Id));
        }

        return new TomeContents(
            TomeContentKind.Biography,
            figure.Id,
            EntityId.None,
            sections,
            year);
    }

    private static TomeContents Itinerary(WorldState world, Figure figure, int year)
    {
        List<Journey> trips = JourneysBy(figure, year);
        var refs = new List<EntityId> { figure.Id };
        var lines = new List<string>();

        foreach (Journey trip in trips)
        {
            string purpose = trip.Kind switch
            {
                JourneyKind.Trade => "on trade",
                JourneyKind.Pilgrimage => "on pilgrimage",
                JourneyKind.Mission => "on a clerical errand",
                _ => "as a guest",
            };

            lines.Add(
                "In " + trip.Year.ToString(CultureInfo.InvariantCulture) + " "
                + Subject(figure).ToLowerInvariant() + " went from "
                + world.NameOf(trip.FromSettlementId) + " to "
                + world.NameOf(trip.ToSettlementId) + ", " + purpose + ".");
            refs.Add(trip.FromSettlementId);
            refs.Add(trip.ToSettlementId);
            if (!trip.ViaId.IsNone) refs.Add(trip.ViaId);
        }

        if (lines.Count == 0)
        {
            lines.Add(Subject(figure) + " kept a book of travels, though no journey had yet been entered.");
        }

        return new TomeContents(
            TomeContentKind.Itinerary,
            figure.Id,
            EntityId.None,
            new[]
            {
                Section(
                    "Itinerary",
                    "This is a book of the roads " + figure.FullName + " took. "
                    + string.Join(" ", lines),
                    refs),
            },
            year);
    }

    private static TomeContents Campaign(
        WorldState world, CampaignRecord record, int year, IRng rng, double learning)
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

            int toldYear = battle.Year;
            int toldStrength = strength;
            int toldDead = dead;
            if (Misremembers(rng, learning, year - battle.Year))
            {
                toldYear += rng.NextInt(-MemoryDriftYears, MemoryDriftYears + 1);
                if (toldYear < war.StartYear) toldYear = war.StartYear;
                if (toldYear > year) toldYear = year;
                toldStrength = Math.Max(1, (int)(strength * rng.NextDouble(0.7, 1.45)));
                toldDead = Math.Max(0, (int)(dead * rng.NextDouble(0.6, 1.55)));
            }

            entries.Add(
                "At the " + battle.Name + " in "
                + toldYear.ToString(CultureInfo.InvariantCulture) + ", "
                + Subject(figure).ToLowerInvariant() + " led "
                + toldStrength.ToString("N0", CultureInfo.InvariantCulture) + " and " + result
                + "; " + toldDead.ToString("N0", CultureInfo.InvariantCulture)
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

        return new TomeContents(TomeContentKind.Campaign, figure.Id, war.Id, sections, year);
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
            },
            year);
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
            TomeContentKind.Cosmology => "an account of the heavens",
            TomeContentKind.Dedication => "a dedication",
            TomeContentKind.RealmChronicle => "a chronicle of the realm",
            TomeContentKind.Itinerary => "an itinerary",
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
    // Faith, cosmos and dedications
    // -----------------------------------------------------------------------

    private static TomeContents ReligiousRite(WorldState world, Religion religion, int year)
    {
        IRng lore = world.Root.Fork("religion.rite", religion.Id.ToDiscriminator());
        FaithCharacter faith = religion.Character;
        string name = lore.Pick(RiteNames(faith));
        string observance = "The " + name + " is observed " + lore.Pick(RiteTimes(faith))
                            + ". Worshippers " + lore.Pick(RiteActions(faith)) + ".";
        string offering = "An offering of " + lore.Pick(RiteOfferings(faith)) + " is made "
                          + lore.Pick(RitePurposes(faith)) + ".";

        return new TomeContents(
            TomeContentKind.ReligiousRite,
            religion.Id,
            EntityId.None,
            new[]
            {
                ReligionOrigins(world, religion, year),
                Section(name, observance, religion.Id),
                Section("Offering and purpose", offering, religion.Id),
            },
            year);
    }

    private static string[] RiteNames(FaithCharacter faith) => faith.Festival switch
    {
        FestivalSeason.Spring => new[] { "Vigil of First Green", "Rite of the Open Hand", "Washing at First Light" },
        FestivalSeason.Summer => new[] { "Feast of High Sun", "Procession of Ash", "Keeping of Names" },
        FestivalSeason.Autumn => new[] { "Feast of Returning", "Vigil of Lamps", "Rite of the Stored Harvest" },
        _ => new[] { "Vigil of the Long Night", "Keeping of Names", "Procession of Ash" },
    };

    private static string[] RiteTimes(FaithCharacter faith) => faith.Prayer switch
    {
        PrayerCadence.Daily => new[]
        {
            "at first light each day",
            "after sunset each day",
            "before a household begins its work",
        },
        PrayerCadence.Weekly => new[]
        {
            "after sunset on the gathering day",
            "at first light on the gathering day",
            "before a household begins a journey",
        },
        _ => new[]
        {
            "at the turning of each season",
            "on the anniversary of the faith's first preaching",
            "in " + FaithCharacters.Label(faith.Festival) + ", at the great gathering",
        },
    };

    private static string[] RiteActions(FaithCharacter faith) => faith.Deity switch
    {
        DeityStructure.Animistic => new[]
        {
            "leave a portion of food at the edge of the wood",
            "walk three times around the gathering place in silence",
            "wash their hands in running water and speak the names of the local spirits",
        },
        DeityStructure.Pantheistic => new[]
        {
            "stand facing the open sky until the assembly is still",
            "light one lamp from another until the gathering is illuminated",
            "touch the ground and the threshold of the sanctuary in turn",
        },
        DeityStructure.Monotheistic => new[]
        {
            "kneel while the names of the dead are spoken",
            "share bread before reciting the promises of the community",
            "wash their hands and touch the threshold of the sanctuary",
        },
        _ => new[]
        {
            "stand in a circle while the names of their dead are spoken",
            "light one lamp from another until the assembly is illuminated",
            "share bread before reciting the promises of the community",
        },
    };

    private static string[] RiteOfferings(FaithCharacter faith) => faith.Diet switch
    {
        DietaryRule.TabooFlesh => new[]
        {
            "bread and salt",
            "oil for the sanctuary lamps",
            "flowers gathered outside the settlement walls",
        },
        DietaryRule.TabooIntoxicants => new[]
        {
            "bread and salt",
            "water drawn at first light",
            "a ribbon bearing the name of an ancestor",
        },
        DietaryRule.Fasting => new[]
        {
            "a cup of water after the fast is lifted",
            "oil for the sanctuary lamps",
            "the first grain of the season, uneaten",
        },
        _ => new[]
        {
            "bread and salt",
            "oil for the sanctuary lamps",
            "the first cup of the season's drink",
            "a ribbon bearing the name of an ancestor",
        },
    };

    private static string[] RitePurposes(FaithCharacter faith) => faith.Afterlife switch
    {
        Afterlife.Ancestral => new[]
        {
            "to remember obligations between the living and the dead",
            "to feed those who still sit among their people",
            "to renew the promises made at the faith's founding",
        },
        Afterlife.Judgement => new[]
        {
            "to ask to be weighed honestly",
            "to renew the promises made at the faith's founding",
            "to reconcile neighbours before witnesses",
        },
        Afterlife.Rebirth => new[]
        {
            "to mark gratitude for what the community has preserved",
            "to ask a kinder birth for those who have gone",
            "to renew the promises made at the faith's founding",
        },
        Afterlife.Union => new[]
        {
            "to remember that the self is not the last word",
            "to mark gratitude for what the community has preserved",
            "to ask safe passage through uncertainty",
        },
        _ => new[]
        {
            "to remember the names that would otherwise be lost",
            "to ask safe passage through uncertainty",
            "to reconcile neighbours before witnesses",
        },
    };

    private static TomeContents ReligiousTeaching(
        WorldState world, Religion religion, int year)
    {
        IRng lore = world.Root.Fork("religion.teaching", religion.Id.ToDiscriminator());
        FaithCharacter faith = religion.Character;
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
                Section("First principle", authority + lore.Pick(Teachings(faith)), religion.Id),
                Section("Instruction", lore.Pick(Instructions(faith)), religion.Id),
            },
            year);
    }

    private static string[] Teachings(FaithCharacter faith) => faith.Dogma switch
    {
        DogmaEmphasis.Honour => new[]
        {
            "A slight unanswered is a slight invited.",
            "A promise witnessed by the community is sacred.",
            "Power is held in trust and is judged by what it preserves.",
        },
        DogmaEmphasis.Mercy => new[]
        {
            "Wisdom is proved by restraint when vengeance is possible.",
            "The stranger and the neighbour are owed the same honest measure.",
            "Loss must be named before renewal can begin.",
        },
        DogmaEmphasis.Purity => new[]
        {
            "What is kept clean is kept holy.",
            "A promise witnessed by the community is sacred.",
            "Memory binds the living to those who came before.",
        },
        DogmaEmphasis.Knowledge => new[]
        {
            "What is not written will be rewritten by whoever speaks next.",
            "Power is held in trust and is judged by what it preserves.",
            "Loss must be named before renewal can begin.",
        },
        DogmaEmphasis.Dominion => new[]
        {
            "The faithful are owed the ground they can hold.",
            "A slight unanswered is a slight invited.",
            "Power is held in trust and is judged by what it preserves.",
        },
        _ => new[]
        {
            "The stranger and the neighbour are owed the same honest measure.",
            "Travellers are to be received before they are judged.",
            "A promise witnessed by the community is sacred.",
        },
    };

    private static string[] Instructions(FaithCharacter faith)
    {
        var lines = new List<string>
        {
            faith.Dogma switch
            {
                DogmaEmphasis.Hospitality =>
                    "Travellers are to be offered water before they are asked their business.",
                DogmaEmphasis.Knowledge =>
                    "Each household is to keep the names of its dead and recite them once each year.",
                DogmaEmphasis.Honour =>
                    "A leader must hear a grievance in public before passing judgment.",
                DogmaEmphasis.Dominion =>
                    "A leader must hear a grievance in public before passing judgment.",
                DogmaEmphasis.Purity =>
                    "Followers are instructed to settle disputes before sharing a ceremonial meal.",
                _ => "Debts of food are forgiven after a failed harvest, but debts of violence require witness.",
            },
            "One day in each " + FaithCharacters.Label(faith.Festival)
            + " is reserved for the great gathering of the faithful.",
        };

        if (faith.Diet != DietaryRule.None)
        {
            lines.Add(faith.Diet switch
            {
                DietaryRule.Fasting => "A fast is kept before the gathering, and broken together.",
                DietaryRule.TabooFlesh => "Flesh is not eaten on days of observance.",
                _ => "No intoxicant is taken inside a house of worship.",
            });
        }

        if (faith.Dress != DressCode.None)
        {
            lines.Add(faith.Dress switch
            {
                DressCode.Modest => "The faithful cover the body when they enter a sacred place.",
                DressCode.ClericalColour => "Those who serve at the altar wear the colour of the faith.",
                _ => "The faithful mark themselves so that a stranger can name the church they keep.",
            });
        }

        if (faith.CelibateClergy)
        {
            lines.Add("Those who take holy office do not marry.");
        }

        return lines.ToArray();
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

        text += ". It is a " + FaithCharacters.Label(religion.Character.Deity)
                + " faith, teaching " + FaithCharacters.Label(religion.Character.Soul)
                + " and " + FaithCharacters.Label(religion.Character.Afterlife) + ".";

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

    /// <summary>
    /// What the sky's near bodies look like from the ground, as a faith writes them down: the
    /// moons that cross this world's nights, and the ringed giant a good eye can resolve.
    /// </summary>
    /// <remarks>
    /// A ring is the one thing in the rolled system that a pre-telescopic people cannot quite
    /// account for, so a learned faith records it as an oddity and a careless one records it
    /// wrongly — which is the same bargain the rest of a tome makes with what it knows.
    /// </remarks>
    private static string Wanderers(
        WorldCosmology sky, IRng rng, double learning, int year, Religion religion)
    {
        CompanionPlanet? ringed = null;
        foreach (CompanionPlanet body in sky.Companions)
        {
            if (body.Ring is not null)
            {
                ringed = body;
                break;
            }
        }

        IReadOnlyList<SystemMoon> nightly = sky.Kind == WorldKind.Moon ? sky.Moons : sky.HomeMoons;
        int companions = sky.Kind == WorldKind.Moon
            ? Math.Max(0, nightly.Count - 1)
            : nightly.Count;

        string moons = companions switch
        {
            0 => "The scribes record no lesser light at all: the nights here are stars and nothing nearer.",
            1 => "One lesser light crosses the nights, and the rite counts its months.",
            _ => "The scribes count " + companions.ToString(CultureInfo.InvariantCulture)
                 + " lesser lights crossing the nights, each on its own month.",
        };

        if (ringed is null)
        {
            return moons;
        }

        if (Misremembers(rng, learning, Math.Max(8, year - religion.FoundedYear)))
        {
            return moons + " Of the great wanderer the copies disagree: some draw a girdle about it, "
                   + "and later hands take the girdle for an error of the pen and strike it out.";
        }

        return moons + " The greatest wanderer is drawn girdled — a band of "
               + ringed.Ring!.CompositionLabel
               + " standing off the body itself, which the scribes hold to be no part of it.";
    }

    private static TomeContents Cosmology(
        WorldState world, Religion religion, int year, IRng rng, double learning)
    {
        WorldFlavour flavour = world.Flavour;
        WorldCosmology sky = flavour.Cosmology;
        FaithCharacter faith = religion.Character;
        var references = new List<EntityId> { religion.Id };

        string seat = flavour.Kind == WorldKind.Moon
            ? flavour.Name + " is taught as a lesser body circling " + (flavour.ParentName ?? "a greater world")
            : flavour.Name + " is taught as a world set in its own course";

        string lights = "The greater light returns in "
                        + sky.OrbitalPeriodDays.ToString("0", CultureInfo.InvariantCulture)
                        + " days, and the faithful mark "
                        + FaithCharacters.Label(faith.Festival)
                        + " by that turning.";

        if (Misremembers(rng, learning, Math.Max(8, year - religion.FoundedYear)))
        {
            lights = "Later teachers disagree about the count of days in the greater turning, "
                     + "and some name a second light that the older rite never counted.";
        }

        string order = faith.Deity switch
        {
            DeityStructure.Monotheistic =>
                "The " + religion.Name + " holds that one will set the lights in their places.",
            DeityStructure.Pantheistic =>
                "The " + religion.Name + " holds that the world and the lights are of one substance.",
            DeityStructure.Animistic =>
                "The " + religion.Name + " holds that each light has its own spirit, as woods and rivers do.",
            _ =>
                "The " + religion.Name + " holds that several powers share the keeping of the sky.",
        };

        string wanderers = Wanderers(sky, rng, learning, year, religion);

        HostGalaxy galaxy = sky.Galaxy;
        string host = galaxy.Blueprint.IsElliptical
            ? "The scribes place the world in a round gathering of lights, far from the crowded heart."
            : galaxy.Location.InSpiralArm
                ? "The scribes place the world in a winding arm of the " + galaxy.Blueprint.MorphologyLabel + " host, among the denser lights."
                : "The scribes place the world in a quiet reach between the winding arms of the " + galaxy.Blueprint.MorphologyLabel + " host.";

        return new TomeContents(
            TomeContentKind.Cosmology,
            religion.Id,
            EntityId.None,
            new[]
            {
                ReligionOrigins(world, religion, year),
                Section("The world", seat + ". " + world.Flavour.Designation + " is the name the scribes use.", references),
                Section("The host", host, religion.Id),
                Section("The lights", lights, religion.Id),
                Section("The wanderers", wanderers, religion.Id),
                Section("Teaching", order, religion.Id),
            },
            year);
    }

    private static TomeContents Dedication(WorldState world, HolySite site, int year)
    {
        HolySiteDescription described = site.Description;
        var references = new List<EntityId> { site.Id, site.ReligionId };

        string raised = site.Name + " was established in "
                        + site.FoundedYear.ToString(CultureInfo.InvariantCulture)
                        + " for the " + world.NameOf(site.ReligionId)
                        + ". It is a " + site.Kind.ToString().ToLowerInvariant()
                        + ", raised for " + described.Dedication + ".";

        if (!described.DedicateeId.IsNone && world.Figures.Contains(described.DedicateeId))
        {
            references.Add(described.DedicateeId);
            Figure dedicatee = world.Figures[described.DedicateeId];
            raised += " The living record names " + dedicatee.FullName + ".";
        }

        if (site.IsWithinSettlement)
        {
            references.Add(site.SettlementId);
            raised += " It stands at " + world.NameOf(site.SettlementId) + ".";
        }

        string observance = described.Atmosphere + " " + described.Offering;

        return new TomeContents(
            TomeContentKind.Dedication,
            site.Id,
            described.DedicateeId,
            new[]
            {
                Section("The place", raised, references),
                Section("Observance", observance, site.Id, site.ReligionId),
            },
            year);
    }

    private static TomeContents RealmChronicle(
        WorldState world, Civilization civilization, int year, IRng rng, double learning)
    {
        string founding = civilization.Name + " was founded in "
                          + civilization.FoundedYear.ToString(CultureInfo.InvariantCulture)
                          + ".";
        var refs = new List<EntityId> { civilization.Id };
        if (!civilization.CurrentRulerId.IsNone)
        {
            refs.Add(civilization.CurrentRulerId);
            founding += " At the writing of this chronicle its ruler was "
                        + world.NameOf(civilization.CurrentRulerId) + ".";
        }

        var entries = new List<HistoryEvent>();
        foreach (HistoryEvent entry in world.Chronicle.Events)
        {
            if (entry.Year <= year && entry.References().Contains(civilization.Id)) entries.Add(entry);
        }

        int first = Math.Max(0, entries.Count - 5);
        var lines = new List<string>();
        var eventRefs = new List<EntityId> { civilization.Id };

        for (int i = first; i < entries.Count; i++)
        {
            HistoryEvent entry = entries[i];
            string line = entry.Year.ToString(CultureInfo.InvariantCulture) + ": " + world.Narrate(entry);
            if (Misremembers(rng, learning, year - entry.Year))
            {
                int drifted = entry.Year + rng.NextInt(-3, 4);
                if (drifted < civilization.FoundedYear) drifted = civilization.FoundedYear;
                if (drifted > year) drifted = year;
                line = drifted.ToString(CultureInfo.InvariantCulture)
                       + ": " + world.Narrate(entry)
                       + " Later copies disagree about the year.";
            }

            lines.Add(line);
            eventRefs.AddRange(entry.References());
        }

        if (lines.Count == 0) lines.Add("No event beyond the realm's founding was recorded.");

        return new TomeContents(
            TomeContentKind.RealmChronicle,
            civilization.Id,
            EntityId.None,
            new[]
            {
                Section("The realm", founding, refs),
                Section("Selected entries", string.Join(" ", lines), eventRefs),
            },
            year);
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
            },
            year);
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

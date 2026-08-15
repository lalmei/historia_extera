using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Naming;

namespace HistoryEngine.World;

/// <summary>
/// Composes the appearance and observance of a holy place from history that already exists
/// when it is raised.
/// </summary>
/// <remarks>
/// <para><b>Subject before prose.</b> The generator first chooses a dedication the congregation
/// actually has material for — a dead king, a martyr, the faith's founder — and only invents a
/// legendary dedicatee when the chronicle has no one to name. Invented names are drawn from the
/// culture's own language, so a stave church and a marble temple do not honour the same gods in
/// the same tongue.</para>
///
/// <para><b>Composed once.</b> Text is stored on the site rather than reconstructed at export. A
/// sanctuary raised beside a fishing village keeps the iron fire-bowl after the village has
/// become a city, and every later reference is worded identically.</para>
///
/// <para><b>The faith is the subject, not the scenery.</b> Architectural tradition and the
/// ground still colour the house, but they do not get to name a second religion inside it.
/// Kind, dedication, offering and the words that describe them are admitted by the
/// congregation's own character — an animism does not raise a church to a saint, and a
/// faith that forbids wine does not leave it on the altar.</para>
/// </remarks>
public static class HolySites
{
    private enum Setting
    {
        Sea,
        River,
        Marsh,
        Height,
        Dry,
        Wood,
        Town,
    }

    /// <summary>Writes the description of one newly founded holy place.</summary>
    public static HolySiteDescription Compose(
        WorldState world,
        EntityId siteId,
        HolySiteKind kind,
        Religion faith,
        Culture culture,
        Settlement settlement,
        bool independent,
        int year)
    {
        IRng lore = world.Root.Fork("holy-site.description", siteId.ToDiscriminator());
        Region region = world.Regions[settlement.RegionId];
        SacredTradition tradition = TraditionOf(world, culture, region, lore);
        Setting setting = SettingOf(settlement, region, independent);

        HolySiteDedicationKind dedicationKind = ChooseDedication(
            world, settlement, faith, culture, kind, tradition, year, lore);

        Figure? dedicatee = ChooseDedicatee(
            world, settlement, faith, dedicationKind, year, lore);

        string dedicateeName = dedicatee is not null
            ? world.NameOf(dedicatee.Id)
            : InventedName(world, culture, siteId, lore);

        string dedication = DedicationProse(
            world, faith, culture, dedicationKind, dedicatee, dedicateeName, setting, lore);

        HolySiteScale scale = ChooseScale(kind, settlement, independent, lore);
        bool hasStatue = lore.Chance(StatueChance(tradition, kind, dedicationKind));
        Setting dressed = tradition == SacredTradition.Forest && kind == HolySiteKind.Sanctuary
            ? Setting.Wood
            : setting;

        return new HolySiteDescription(
            tradition,
            dedicationKind,
            dedication,
            StyleProse(tradition, kind, dressed, lore),
            lore.Pick(Atmospheres(tradition, dressed)),
            scale,
            lore.Pick(Capacities(tradition, kind, scale)),
            hasStatue,
            FocalProse(tradition, kind, dressed, hasStatue, faith.Character, lore),
            lore.Pick(Offerings(tradition, dressed, kind, faith.Character)),
            dedicatee?.Id ?? EntityId.None);
    }

    // -----------------------------------------------------------------------
    // Tradition and setting
    // -----------------------------------------------------------------------

    /// <summary>
    /// The architectural tradition this congregation builds in.
    /// </summary>
    /// <remarks>
    /// The culture's naming language is the primary vote, because a people brings its sacred
    /// carpentry with it. Climate is allowed a smaller say so a desert congregation of a
    /// northern tongue still raises something that can stand in the wind.
    /// </remarks>
    private static SacredTradition TraditionOf(
        WorldState world, Culture culture, Region region, IRng lore)
    {
        int nordic = 0;
        int classical = 0;
        int steppe = 0;
        int forest = 0;

        foreach (NamingLanguage.CorpusWeight source in LanguageOf(world, culture).Sources)
        {
            switch (source.Family)
            {
                case "norse":
                case "finnic":
                    nordic += source.Weight;
                    break;
                case "hellenic":
                case "latin":
                    classical += source.Weight;
                    break;
                case "turkic":
                case "semitic":
                    steppe += source.Weight;
                    break;
                default:
                    forest += source.Weight;
                    break;
            }
        }

        switch (region.Biome)
        {
            case Biome.Taiga:
            case Biome.Tundra:
            case Biome.Glacier:
                nordic += 2;
                break;
            case Biome.Desert:
            case Biome.Steppe:
                steppe += 2;
                break;
            case Biome.Wetland:
            case Biome.TemperateForest:
                forest += 2;
                break;
            case Biome.Savanna:
            case Biome.TropicalForest:
                classical += 2;
                break;
        }

        int best = nordic;
        if (classical > best) best = classical;
        if (steppe > best) best = steppe;
        if (forest > best) best = forest;

        var tied = new List<SacredTradition>(4);
        if (nordic == best) tied.Add(SacredTradition.Nordic);
        if (classical == best) tied.Add(SacredTradition.Classical);
        if (steppe == best) tied.Add(SacredTradition.Steppe);
        if (forest == best) tied.Add(SacredTradition.Forest);

        return lore.Pick(tied);
    }

    private static Setting SettingOf(Settlement settlement, Region region, bool independent)
    {
        if (settlement.Site is SiteCharacter.Harbour or SiteCharacter.Coastal or SiteCharacter.Estuary
            || region.IsCoastal)
        {
            return Setting.Sea;
        }

        if (region.Biome == Biome.Wetland) return Setting.Marsh;

        if (region.Biome is Biome.Desert or Biome.Steppe or Biome.Savanna) return Setting.Dry;

        if ((independent && region.Ruggedness > 0.55) || region.MeanHeight > 1400.0)
        {
            return Setting.Height;
        }

        if (settlement.Site is SiteCharacter.Riverside or SiteCharacter.Confluence || region.HasRiver)
        {
            return Setting.River;
        }

        if (region.Biome is Biome.TemperateForest or Biome.Taiga or Biome.TropicalForest)
        {
            return Setting.Wood;
        }

        return Setting.Town;
    }

    private static NamingLanguage LanguageOf(WorldState world, Culture culture) =>
        world.Names is MarkovNameGenerator markov
            ? markov.LanguageOf(culture)
            : NamingLanguage.Derive(culture.LanguageSeed);

    // -----------------------------------------------------------------------
    // Dedication
    // -----------------------------------------------------------------------

    private static HolySiteDedicationKind ChooseDedication(
        WorldState world,
        Settlement settlement,
        Religion faith,
        Culture culture,
        HolySiteKind kind,
        SacredTradition tradition,
        int year,
        IRng lore)
    {
        var weights = new List<(HolySiteDedicationKind Kind, int Weight)>(16);

        switch (kind)
        {
            case HolySiteKind.Shrine:
                Add(HolySiteDedicationKind.NatureSpirit, 4);
                Add(HolySiteDedicationKind.CosmicForce, 3);
                Add(HolySiteDedicationKind.Saint, 2);
                Add(HolySiteDedicationKind.God, 2);
                break;
            case HolySiteKind.Temple:
                Add(HolySiteDedicationKind.God, 4);
                Add(HolySiteDedicationKind.AncientGod, 3);
                Add(HolySiteDedicationKind.LivingKing, 2);
                Add(HolySiteDedicationKind.DivineConcept, 2);
                break;
            case HolySiteKind.Church:
                Add(HolySiteDedicationKind.Saint, 4);
                Add(HolySiteDedicationKind.Martyr, 3);
                Add(HolySiteDedicationKind.God, 2);
                Add(HolySiteDedicationKind.AncestralKing, 1);
                break;
            case HolySiteKind.Monastery:
                Add(HolySiteDedicationKind.Sage, 4);
                Add(HolySiteDedicationKind.DivineConcept, 3);
                Add(HolySiteDedicationKind.Saint, 2);
                Add(HolySiteDedicationKind.God, 1);
                break;
            default:
                Add(HolySiteDedicationKind.AncestralKing, 3);
                Add(HolySiteDedicationKind.Martyr, 3);
                Add(HolySiteDedicationKind.NatureSpirit, 2);
                Add(HolySiteDedicationKind.AncientGod, 2);
                break;
        }

        switch (tradition)
        {
            case SacredTradition.Nordic:
                Add(HolySiteDedicationKind.God, 2);
                Add(HolySiteDedicationKind.AncestralKing, 3);
                Add(HolySiteDedicationKind.AncientGod, 1);
                break;
            case SacredTradition.Classical:
                Add(HolySiteDedicationKind.DivineConcept, 3);
                Add(HolySiteDedicationKind.LivingKing, 2);
                Add(HolySiteDedicationKind.Martyr, 2);
                break;
            case SacredTradition.Steppe:
                Add(HolySiteDedicationKind.CosmicForce, 3);
                Add(HolySiteDedicationKind.Sage, 2);
                Add(HolySiteDedicationKind.Martyr, 2);
                break;
            default:
                Add(HolySiteDedicationKind.NatureSpirit, 3);
                Add(HolySiteDedicationKind.Saint, 2);
                Add(HolySiteDedicationKind.AncientGod, 2);
                break;
        }

        if (culture.Government == GovernmentForm.Theocracy)
        {
            Add(HolySiteDedicationKind.DivineConcept, 4);
            Add(HolySiteDedicationKind.LivingKing, 3);
        }
        else if (culture.Government == GovernmentForm.Chiefdom)
        {
            Add(HolySiteDedicationKind.AncestralKing, 2);
            Add(HolySiteDedicationKind.NatureSpirit, 2);
            Add(HolySiteDedicationKind.AncientGod, 1);
        }

        if (culture.Values.Learning >= 0.65) Add(HolySiteDedicationKind.Sage, 2);
        if (culture.Values.Tradition >= 0.65) Add(HolySiteDedicationKind.AncestralKing, 2);
        if (faith.Fervour >= 0.7) Add(HolySiteDedicationKind.God, 2);

        foreach (HolySiteDedicationKind dedication in Enum.GetValues<HolySiteDedicationKind>())
        {
            int bias = faith.Character.DedicationBias(dedication);
            if (bias > 0) Add(dedication, bias);
        }

        if (HasCandidate(world, settlement, faith, HolySiteDedicationKind.AncestralKing, year))
        {
            Add(HolySiteDedicationKind.AncestralKing, 3);
        }

        if (HasCandidate(world, settlement, faith, HolySiteDedicationKind.Martyr, year))
        {
            Add(HolySiteDedicationKind.Martyr, 4);
        }

        if (HasCandidate(world, settlement, faith, HolySiteDedicationKind.Sage, year)
            || HasCandidate(world, settlement, faith, HolySiteDedicationKind.Saint, year))
        {
            Add(HolySiteDedicationKind.Saint, 2);
            Add(HolySiteDedicationKind.Sage, 2);
        }

        for (int i = weights.Count - 1; i >= 0; i--)
        {
            if (!faith.Character.AdmitsDedication(weights[i].Kind)) weights.RemoveAt(i);
        }

        if (weights.Count == 0)
        {
            weights.Add((faith.Character.PreferredDedication(), 1));
        }

        return Weighted(weights, lore);

        void Add(HolySiteDedicationKind dedication, int weight)
        {
            if (weight > 0) weights.Add((dedication, weight));
        }
    }

    private static Figure? ChooseDedicatee(
        WorldState world,
        Settlement settlement,
        Religion faith,
        HolySiteDedicationKind kind,
        int year,
        IRng lore)
    {
        if (kind is HolySiteDedicationKind.God
            or HolySiteDedicationKind.AncientGod
            or HolySiteDedicationKind.NatureSpirit
            or HolySiteDedicationKind.CosmicForce
            or HolySiteDedicationKind.DivineConcept)
        {
            // A fervent congregation may raise its founder to the altar, but the rest of these
            // dedications are legendary and must not steal a living person's name.
            if (kind == HolySiteDedicationKind.God
                && !faith.FounderId.IsNone
                && world.Figures.Contains(faith.FounderId)
                && lore.Chance(0.28))
            {
                Figure founder = world.Figures[faith.FounderId];
                if (founder.BirthYear < year) return founder;
            }

            return null;
        }

        List<Figure> candidates = Candidates(world, settlement, faith, kind, year);
        return candidates.Count == 0 ? null : lore.Pick(candidates);
    }

    private static bool HasCandidate(
        WorldState world,
        Settlement settlement,
        Religion faith,
        HolySiteDedicationKind kind,
        int year) =>
        Candidates(world, settlement, faith, kind, year).Count > 0;

    private static List<Figure> Candidates(
        WorldState world,
        Settlement settlement,
        Religion faith,
        HolySiteDedicationKind kind,
        int year)
    {
        var found = new List<Figure>();

        foreach (Figure figure in world.Figures)
        {
            if (figure.BirthYear >= year) continue;
            if (!Relevant(figure, settlement, faith, year)) continue;

            bool matches = kind switch
            {
                HolySiteDedicationKind.AncestralKing
                    => Held(figure, settlement.CivilizationId, OfficeKind.Ruler, year)
                       && !figure.IsAlive,
                HolySiteDedicationKind.LivingKing
                    => Held(figure, settlement.CivilizationId, OfficeKind.Ruler, year),
                HolySiteDedicationKind.Martyr
                    => figure.DeathYear is int death
                       && death < year
                       && figure.DeathCause is DeathCause.Execution
                           or DeathCause.Assassination
                           or DeathCause.Battle
                           or DeathCause.Poisoning,
                HolySiteDedicationKind.Saint
                    => figure.Origin == FigureOrigin.Clergy
                       || Held(figure, settlement.CivilizationId, OfficeKind.HighPriest, year)
                       || figure.Origin == FigureOrigin.Townsfolk,
                HolySiteDedicationKind.Sage
                    => figure.Origin == FigureOrigin.Clergy
                       || Held(figure, settlement.CivilizationId, OfficeKind.HighPriest, year)
                       || figure.Id == faith.FounderId,
                _ => false,
            };

            if (matches) found.Add(figure);
        }

        return found;
    }

    private static bool Relevant(Figure figure, Settlement settlement, Religion faith, int year)
    {
        if (figure.Id == faith.FounderId) return true;
        if (figure.CivilizationId == settlement.CivilizationId) return true;
        if (figure.CultureId == faith.CultureId) return true;
        if (figure.BirthSettlementId == settlement.Id) return true;

        foreach (OfficeHolding office in figure.Offices)
        {
            if (office.CivilizationId == settlement.CivilizationId && office.FromYear < year)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Held(Figure figure, EntityId civilizationId, OfficeKind kind, int year)
    {
        foreach (OfficeHolding office in figure.Offices)
        {
            if (office.Kind == kind
                && office.CivilizationId == civilizationId
                && office.FromYear < year)
            {
                return true;
            }
        }

        return false;
    }

    private static string InventedName(WorldState world, Culture culture, EntityId siteId, IRng lore)
    {
        NamingLanguage language = LanguageOf(world, culture);
        IRng names = lore.Fork("dedicatee", siteId.ToDiscriminator());
        return language.Person(names);
    }

    private static string DedicationProse(
        WorldState world,
        Religion faith,
        Culture culture,
        HolySiteDedicationKind kind,
        Figure? dedicatee,
        string dedicateeName,
        Setting setting,
        IRng lore)
    {
        string faithName = faith.Name;

        if (dedicatee is not null)
        {
            return kind switch
            {
                HolySiteDedicationKind.AncestralKing =>
                    "Built for an Ancestral " + culture.RulerTitle
                    + ". Built to honour " + culture.RulerTitle + " " + dedicateeName
                    + ", " + AncestralDeed(dedicatee, lore) + ".",
                HolySiteDedicationKind.LivingKing =>
                    "Built for a living " + culture.RulerTitle
                    + ". Honouring " + culture.RulerTitle + " " + dedicateeName
                    + ", " + lore.Pick(LivingKingDeeds) + ".",
                HolySiteDedicationKind.Martyr =>
                    "Built for a Martyr. Dedicated to "
                    + MartyrStyle(world, culture, dedicatee, dedicateeName)
                    + ", " + MartyrDeed(dedicatee, lore) + ".",
                HolySiteDedicationKind.Saint =>
                    "Built for a Saint of the Common Folk. Dedicated to Saint " + dedicateeName
                    + ", " + lore.Pick(SaintDeeds) + ".",
                HolySiteDedicationKind.Sage =>
                    "Built for a Sage. Dedicated to " + dedicateeName
                    + ", " + lore.Pick(SageDeeds) + ".",
                _ =>
                    "Built for a God. Dedicated to " + dedicateeName
                    + ", raised to divinity by those who first preached the " + faithName + ".",
            };
        }

        string title = lore.Pick(Titles(kind, setting));
        string epithet = lore.Pick(Epithets(kind, setting));
        string domain = lore.Pick(Domains(kind, setting, faith.Character.Deity));

        return kind switch
        {
            HolySiteDedicationKind.God =>
                lore.Chance(0.42)
                    ? "Built for a God. Dedicated to " + title + ", " + domain + "."
                    : "Built for a God. Dedicated to " + dedicateeName + " " + epithet + ", " + domain + ".",
            HolySiteDedicationKind.AncientGod =>
                "Built for an Ancient God. Dedicated to " + title
                + ", a primordial god of " + domain
                + " that predates all kingdoms in the region.",
            HolySiteDedicationKind.NatureSpirit =>
                "Built for a Nature Spirit. Erected to appease " + title + " (" + dedicateeName
                + "), a fickle nature spirit who " + lore.Pick(SpiritDeeds(setting)) + ".",
            HolySiteDedicationKind.CosmicForce =>
                "Built for a Cosmic Force. Dedicated to " + title
                + ", the ultimate, formless entity representing " + domain + ".",
            HolySiteDedicationKind.DivineConcept =>
                "Built for a Divine Concept. Dedicated to " + title
                + ", " + domain + ", " + ConceptKeepers(faith) + ".",
            HolySiteDedicationKind.AncestralKing =>
                "Built for an Ancestral " + culture.RulerTitle + ". Built to honour "
                + culture.RulerTitle + " " + dedicateeName + " " + epithet + ", "
                + lore.Pick(LegendaryKingDeeds) + ".",
            HolySiteDedicationKind.LivingKing =>
                "Built for a living " + culture.RulerTitle + ". Honouring "
                + culture.RulerTitle + " " + dedicateeName + " " + epithet + ", "
                + lore.Pick(LivingKingDeeds) + ".",
            HolySiteDedicationKind.Martyr =>
                "Built for a Martyr. Dedicated to " + title + ", " + lore.Pick(LegendaryMartyrDeeds) + ".",
            HolySiteDedicationKind.Saint =>
                "Built for a Saint of the Common Folk. Dedicated to Saint " + dedicateeName
                + ", " + lore.Pick(SaintDeeds) + ".",
            _ =>
                "Built for a Sage. Dedicated to " + title + ", " + lore.Pick(SageDeeds) + ".",
        };
    }

    private static string ConceptKeepers(Religion faith) => faith.Character.Authority switch
    {
        AuthorityType.Hierarchical =>
            "worshipped by the ruling orders of the " + faith.Name,
        AuthorityType.Monastic =>
            "kept by the house that studies under the " + faith.Name,
        _ =>
            "kept by whoever tends the place for the " + faith.Name,
    };

    private static string AncestralDeed(Figure figure, IRng lore)
    {
        if (figure.DeathCause == DeathCause.Battle)
        {
            return "a unifier who fell in war and is sworn to rise if the people face extinction";
        }

        return lore.Pick(LegendaryKingDeeds);
    }

    private static string MartyrStyle(WorldState world, Culture culture, Figure figure, string name)
    {
        if (Held(figure, figure.CivilizationId, OfficeKind.Marshal, figure.DeathYear ?? int.MaxValue))
        {
            return culture.TitleFor(OfficeKind.Marshal, figure.Sex) + " " + name;
        }

        if (Held(figure, figure.CivilizationId, OfficeKind.HighPriest, figure.DeathYear ?? int.MaxValue))
        {
            return culture.TitleFor(OfficeKind.HighPriest, figure.Sex) + " " + name;
        }

        return name;
    }

    private static string MartyrDeed(Figure figure, IRng lore)
    {
        string cause = figure.DeathDetail ?? Houses.CauseLabel(figure.DeathCause);
        return lore.Chance(0.55)
            ? "who died of " + cause + " and is remembered for standing when others fled"
            : lore.Pick(LegendaryMartyrDeeds);
    }

    private static HolySiteDedicationKind Weighted(
        List<(HolySiteDedicationKind Kind, int Weight)> weights, IRng lore)
    {
        int total = 0;
        for (int i = 0; i < weights.Count; i++) total += weights[i].Weight;

        int roll = lore.NextInt(total);
        for (int i = 0; i < weights.Count; i++)
        {
            roll -= weights[i].Weight;
            if (roll < 0) return weights[i].Kind;
        }

        return weights[weights.Count - 1].Kind;
    }

    // -----------------------------------------------------------------------
    // Appearance
    // -----------------------------------------------------------------------

    private static HolySiteScale ChooseScale(
        HolySiteKind kind, Settlement settlement, bool independent, IRng lore)
    {
        int score = settlement.Tier switch
        {
            SettlementTier.City => 2,
            SettlementTier.Town => 1,
            _ => 0,
        };

        if (independent) score += 1;

        score += kind switch
        {
            HolySiteKind.Monastery => 1,
            HolySiteKind.Sanctuary when independent => 1,
            HolySiteKind.Shrine => -1,
            _ => 0,
        };

        if (lore.Chance(0.18)) score += lore.Chance(0.5) ? 1 : -1;

        if (score <= 0) return HolySiteScale.Small;
        if (score == 1) return HolySiteScale.Medium;
        return HolySiteScale.Large;
    }

    private static double StatueChance(
        SacredTradition tradition, HolySiteKind kind, HolySiteDedicationKind dedication)
    {
        double chance = tradition switch
        {
            SacredTradition.Classical => 0.72,
            SacredTradition.Steppe => 0.52,
            SacredTradition.Forest => 0.38,
            _ => 0.28,
        };

        chance += kind switch
        {
            HolySiteKind.Temple => 0.12,
            HolySiteKind.Sanctuary => -0.18,
            HolySiteKind.Shrine => -0.12,
            _ => 0.0,
        };

        // A formless force or a local spirit is not a person to carve. A god, an old god or a
        // living king is.
        chance += dedication switch
        {
            HolySiteDedicationKind.CosmicForce or HolySiteDedicationKind.DivineConcept => -0.28,
            HolySiteDedicationKind.NatureSpirit => -0.12,
            HolySiteDedicationKind.God or HolySiteDedicationKind.AncientGod
                or HolySiteDedicationKind.LivingKing => 0.10,
            _ => 0.0,
        };

        return DetMath.Clamp01(chance);
    }

    private static string StyleProse(
        SacredTradition tradition, HolySiteKind kind, Setting setting, IRng lore)
    {
        bool timber = TimberForm(tradition, kind);
        return lore.Pick(Forms(tradition, kind)) + " " + lore.Pick(Dressings(tradition, setting, timber));
    }

    /// <summary>Whether this tradition's usual fabric for the kind is timber rather than stone or brick.</summary>
    private static bool TimberForm(SacredTradition tradition, HolySiteKind kind) => tradition switch
    {
        SacredTradition.Classical => false,
        SacredTradition.Steppe => false,
        SacredTradition.Forest => kind != HolySiteKind.Sanctuary,
        _ => kind != HolySiteKind.Temple,
    };

    private static string FocalProse(
        SacredTradition tradition,
        HolySiteKind kind,
        Setting setting,
        bool hasStatue,
        FaithCharacter faith,
        IRng lore)
    {
        if (hasStatue) return "Yes. " + lore.Pick(Fitting(Statues(tradition, kind), faith, Unpictured));
        return "No statue. " + lore.Pick(Fitting(Foci(tradition, setting, kind), faith, Unpictured));
    }

    // -----------------------------------------------------------------------
    // Tables — dedication
    // -----------------------------------------------------------------------

    private static readonly string[] LivingKingDeeds =
    {
        "who declared themself the living avatar of the sun before a mysterious ascension",
        "who claimed the heavens as a personal mandate and built this house to prove it",
        "crowned as the voice of the celestial order while still walking among the living",
        "who took the title of living law and required every oath to be sworn in this precinct",
    };

    private static readonly string[] LegendaryKingDeeds =
    {
        "a legendary unifier who swore to rise from the earth if the people faced extinction",
        "who bound warring clans under one roof and was buried sitting upright to keep the watch",
        "remembered for walking the borders barefoot so no field would be left unclaimed",
        "who vanished into the ground after a last winter and is said to sleep beneath this hall",
    };

    private static readonly string[] LegendaryMartyrDeeds =
    {
        "an ancient priestess executed by an invading empire; the ground is still said to remember the blood",
        "a general who stood alone at the gates so the people could escape an encroaching army",
        "local weavers who refused to renounce the faith and were bound into their own looms",
        "a defender cut down on the threshold, whose last order was that the doors stay open to the poor",
        "three siblings who would not name their teachers and were left for the crows at this spot",
    };

    private static readonly string[] SaintDeeds =
    {
        "a humble fisher said to have shared a catch with an entire starving province during a cursed winter",
        "a healer who walked from house to house in a plague year and asked no payment but water",
        "a miller who opened the granaries when the tax-men had already sealed them",
        "a shepherd who led a lost caravan out of a white storm by the sound of a single bell",
        "a widow who kept a lamp burning in every empty doorway until the missing came home",
    };

    private static readonly string[] SageDeeds =
    {
        "a philosopher who spent forty years in absolute silence to understand the nature of the wind",
        "an astronomer-monk who mapped the wandering stars from this courtyard and would not leave it",
        "a teacher who would answer any question except the one about their own name",
        "an ascetic who counted every grain of sand in a day's walk and called the number a prayer",
        "a master of whispers whose students still copy the same three sentences onto wet clay",
    };

    private static string[] SpiritDeeds(Setting setting) => setting switch
    {
        Setting.Marsh => new[]
        {
            "guides lost travellers out of the bogs — or pulls them under",
            "answers a truthful question with a clear path, and a boast with a thicket",
            "keeps the frogs singing provided the first bread of the season is given back",
        },
        Setting.Sea => new[]
        {
            "calms the water if remembered, and wrecks the boastful",
            "keeps the fish running provided the first catch of the season is given back",
            "walks the tideline at dusk and will not be named twice in one night",
        },
        Setting.River => new[]
        {
            "calms the river in spring if remembered, and floods it if forgotten",
            "keeps the fish running provided the first catch of the season is given back",
            "answers a truthful question with a clear ford, and a lie with a drowning",
        },
        Setting.Wood => new[]
        {
            "walks the treeline at dusk and will not be named twice in one night",
            "guides the hunter home if the first kill is shared, and loses them if it is not",
            "answers a truthful question with a clear path, and a boast with a thicket",
        },
        _ => new[]
        {
            "keeps the threshold if remembered, and misplaces the door if forgotten",
            "answers a truthful question with a clear path, and a boast with a thicket",
            "walks the edge of the settlement at dusk and will not be named twice in one night",
        },
    };

    private static string[] Titles(HolySiteDedicationKind kind, Setting setting) => kind switch
    {
        HolySiteDedicationKind.God when setting == Setting.Sea => new[]
        {
            "The Drowned One", "The Storm-Keeper", "The Salt Father", "The Wave-Crowned",
        },
        HolySiteDedicationKind.God => new[]
        {
            "The Hearth-Lord", "The Hammer of Winter", "The Law-Giver", "The Unsleeping Eye",
        },
        HolySiteDedicationKind.AncientGod => new[]
        {
            "The Horned Hunter", "The First Fire", "The Bone Mother", "The World-Serpent",
        },
        HolySiteDedicationKind.NatureSpirit when setting == Setting.Marsh => new[]
        {
            "The Marsh Mother", "The Reed-Walker", "The Green Mouth",
        },
        HolySiteDedicationKind.NatureSpirit when setting == Setting.Sea => new[]
        {
            "The Tide-Daughter", "The Foam-Haired", "The Cliff-Sleeper",
        },
        HolySiteDedicationKind.NatureSpirit => new[]
        {
            "The Tree that Listens", "The River Bride", "The Hill-Watcher", "The Ash-Sister",
        },
        HolySiteDedicationKind.CosmicForce => new[]
        {
            "The Blue Sky", "The Turning Path", "The Unwritten Wind", "The Measure of Days",
        },
        HolySiteDedicationKind.DivineConcept => new[]
        {
            "The Eternal Order", "The Just Proportion", "The Open Ledger", "The Unbroken Circle",
        },
        HolySiteDedicationKind.Martyr => new[]
        {
            "The Three Sisters", "The Gate-Keeper", "The Silent Choir", "The Last Witness",
        },
        HolySiteDedicationKind.Sage => new[]
        {
            "The Master of Whispers", "The Quiet Geometer", "The Sand-Scribe", "The Listener",
        },
        _ => new[]
        {
            "The Sleeper", "The Unforgotten", "The First of the Line",
        },
    };

    private static string[] Epithets(HolySiteDedicationKind kind, Setting setting) => kind switch
    {
        HolySiteDedicationKind.God when setting == Setting.Sea => new[]
        {
            "Great-Hammer", "the Wave-Crowned", "Salt-Beard", "of the Deep Keel",
        },
        HolySiteDedicationKind.God => new[]
        {
            "the Unsleeping", "of the Long Winter", "Law-Hand", "the Far-Seeing",
        },
        HolySiteDedicationKind.AncestralKing => new[]
        {
            "the Sleeper", "the Unifier", "of the Hidden Hall", "the Earth-Sworn",
        },
        HolySiteDedicationKind.LivingKing => new[]
        {
            "the Radiant", "the Self-Crowned", "of the High Frieze", "the Ascended",
        },
        HolySiteDedicationKind.Saint => new[]
        {
            "the Pure", "of the Shared Catch", "Lamp-Bearer", "the Open Door",
        },
        _ => new[]
        {
            "the Remembered", "of the Old Vow", "the Steadfast", "the Quiet",
        },
    };

    private static string[] Domains(
        HolySiteDedicationKind kind, Setting setting, DeityStructure deity) => kind switch
    {
        HolySiteDedicationKind.God when setting == Setting.Sea => deity == DeityStructure.Monotheistic
            ? new[]
            {
                "the keeper of nets, wrecks, and the names of those who did not come home",
                "the watcher of winter storms and of those who still have to cross them",
                "the one asked for safe passage, and blamed when the passage fails",
            }
            : new[]
            {
                "the elemental god of winter storms and coastal protection",
                "a mercurial sea deity blamed for shipwrecks and worshipped for safe passage",
                "the keeper of nets, wrecks, and the names of those who did not come home",
            },
        HolySiteDedicationKind.God => deity == DeityStructure.Monotheistic
            ? new[]
            {
                "the watcher of thresholds, asked to keep night outside the door",
                "the judge of harvests, whose favour is counted in stored grain",
                "the one before whom oaths are sworn and hearths are first lit",
            }
            : new[]
            {
                "the god of oaths, hearths, and the first iron of the year",
                "the watcher of thresholds, asked to keep night outside the door",
                "the judge of harvests, whose favour is counted in stored grain",
            },
        HolySiteDedicationKind.AncientGod => new[]
        {
            "beasts, cycles, and survival",
            "stone, blood-memory, and the turning year",
            "the hunt, the den, and the first fire stolen from a mountain",
        },
        HolySiteDedicationKind.CosmicForce => new[]
        {
            "destiny, vastness, and weather control in the open country",
            "the path of caravans, the hour of departure, and the luck of wells",
            "the measure of years, written in the wander of stars",
        },
        HolySiteDedicationKind.DivineConcept => new[]
        {
            "a mathematical and celestial framework",
            "the doctrine that light, law, and proportion are one substance",
            "the calendar by which every public act is timed",
        },
        _ => new[]
        {
            "memory between the living and the dead",
            "the keeping of names and the mending of roofs",
            "safe passage through uncertainty",
        },
    };

    // -----------------------------------------------------------------------
    // Tables — fabric
    // -----------------------------------------------------------------------

    private static string[] Forms(SacredTradition tradition, HolySiteKind kind) =>
        (tradition, kind) switch
        {
            (SacredTradition.Nordic, HolySiteKind.Church) => new[]
            {
                "A stave church with steep, layered roofs.",
                "A tar-black wooden church raised on a stone footing, its gables stacked like a ship's prow.",
                "A high-timbered hall-church with dragon-headed ridge beams.",
            },
            (SacredTradition.Nordic, HolySiteKind.Temple) => new[]
            {
                "A cliffside stone monolith cut into the crags.",
                "A ring of weather-split standing stones around a shallow pit.",
                "A low stone temple with a turf roof, half-swallowed by the slope.",
            },
            (SacredTradition.Nordic, HolySiteKind.Sanctuary) => new[]
            {
                "A sunken earthen longhouse hidden beneath a grassy mound.",
                "A turf-roofed hall dug into the hillside, its doorway a whalebone arch.",
                "A subterranean chamber reached by a descending timber stair.",
            },
            (SacredTradition.Nordic, HolySiteKind.Monastery) => new[]
            {
                "A timber cloister around a wind-scoured courtyard.",
                "A cluster of tarred cells and a single tall bell-stave.",
                "A cliff-perched wooden monastery lashed to the rock with iron bands.",
            },
            (SacredTradition.Nordic, HolySiteKind.Shrine) => new[]
            {
                "A wayside stave-shrine no taller than a loaded cart.",
                "A roofed pillar-shrine standing alone on a rise.",
                "A small wooden kiosk carved with interlocking beasts.",
            },
            (SacredTradition.Classical, HolySiteKind.Temple) => new[]
            {
                "A high-classical marble temple atop a jagged hill.",
                "An open colonnade of fluted columns under a paint-faded frieze.",
                "A peripteral shrine of white stone, its pediment cracked by heat.",
            },
            (SacredTradition.Classical, HolySiteKind.Monastery) => new[]
            {
                "A terraced white limestone complex overlooking the country below.",
                "An open-air cloister of colonnades and water-channels.",
                "A hillside monastery of stacked terraces, each a garden and a walk.",
            },
            (SacredTradition.Classical, HolySiteKind.Sanctuary) => new[]
            {
                "A hidden grotto built around a natural spring.",
                "A cut-stone nymphaeum set into the living rock.",
                "A sunken court of mosaic floors opening onto a pool.",
            },
            (SacredTradition.Classical, HolySiteKind.Church) => new[]
            {
                "A basilica of pale stone with a timber roof painted in fading reds.",
                "A long hall of columns leading to a raised apse.",
                "A civic temple-church whose porch still carries an older frieze.",
            },
            (SacredTradition.Classical, HolySiteKind.Shrine) => new[]
            {
                "A roadside aedicule of white stone, just large enough to stand in.",
                "A small open shrine of two columns and a shallow pediment.",
                "A marble niche set into a retaining wall above the path.",
            },
            (SacredTradition.Steppe, HolySiteKind.Shrine) => new[]
            {
                "A single-room, domed wayside shrine of fired brick.",
                "A small turquoise-tiled kiosk at the edge of the road.",
                "A brick lantern-shrine whose dome catches the last of the day's light.",
            },
            (SacredTradition.Steppe, HolySiteKind.Temple) => new[]
            {
                "A fortified mud-brick compound with massive, tapering watchtowers.",
                "A courtyard temple behind heavy wooden gates faced with copper.",
                "A brick sanctuary whose four iwans open onto a dusty square.",
            },
            (SacredTradition.Steppe, HolySiteKind.Monastery) => new[]
            {
                "A low-slung courtyard monastery built from river stones.",
                "A quiet cloister of cells around a garden of wild grasses.",
                "A caravanserai-monastery whose rooms face an inner well.",
            },
            (SacredTradition.Steppe, HolySiteKind.Church) => new[]
            {
                "A brick prayer-hall under a pointed dome, its portal a deep arch.",
                "A long mud-brick nave with a single high window to the east.",
                "A fortified church whose outer walls double as the town's refuge.",
            },
            (SacredTradition.Steppe, HolySiteKind.Sanctuary) => new[]
            {
                "A walled oasis-sanctuary around a well and a stand of trees.",
                "A sunken brick court open to the sky, its floor packed clay.",
                "A caravan sanctuary of shade-walls and a single deep cistern.",
            },
            (SacredTradition.Forest, HolySiteKind.Church) => new[]
            {
                "A high-towered wooden church with onion-shaped shingle domes.",
                "A tall, narrow timber church drawing the eye upward into gloom.",
                "A lakeside stone chapel with a steeply pitched copper roof.",
            },
            (SacredTradition.Forest, HolySiteKind.Shrine) => new[]
            {
                "An elevated wooden pavilion resting on stilts.",
                "A tiny platform-shrine jutting out over the water.",
                "A roofed wooden walk ending in a single sacred post.",
            },
            (SacredTradition.Forest, HolySiteKind.Sanctuary) => new[]
            {
                "A megalithic stone ring deep within old-growth trees.",
                "A moss-covered circle of pillars that seem to grow from the wood.",
                "A forest clearing bounded by recumbent stones and a single lintel.",
            },
            (SacredTradition.Forest, HolySiteKind.Temple) => new[]
            {
                "A timber temple raised on a log crib, its walls of split planks.",
                "A forest temple of dark logs and a shingled, many-hipped roof.",
                "A stone-and-timber hall whose porch is a pair of carved tree trunks.",
            },
            (SacredTradition.Forest, HolySiteKind.Monastery) => new[]
            {
                "A wooden cloister around a vegetable garden and a well.",
                "A river-stone monastery of low cells and a single onion-domed hall.",
                "A forest monastery hidden behind a palisade of unpeeled logs.",
            },
            _ => new[]
            {
                "A house of worship built from whatever the country could spare.",
            },
        };

    private static string[] Dressings(SacredTradition tradition, Setting setting, bool timber)
    {
        if (!timber)
        {
            if (tradition == SacredTradition.Nordic)
            {
                return setting == Setting.Sea
                    ? new[]
                    {
                        "Crudely carved pillars worn smooth by heavy sea spray.",
                        "The stones are tarred at the joints and carved with sea serpents.",
                        "Iron nails driven in patterns of waves, rusting to the colour of dried blood.",
                    }
                    : new[]
                    {
                        "The stones are packed with turf and carved with interlocking beasts.",
                        "Whalebone wedges hold the joints; a single smoke-hole lets in a coin of cold light.",
                        "The walls are packed earth faced with split stone, sweating in summer.",
                    };
            }

            if (tradition == SacredTradition.Forest)
            {
                return new[]
                {
                    "Moss-covered pillars that seem to blend perfectly into giant trees.",
                    "Lichen has taken the north faces; the geometry is the forest's own.",
                    "The stones are recumbent, half-swallowed by roots and old leaves.",
                };
            }
        }

        return (tradition, setting) switch
        {
            (SacredTradition.Nordic, Setting.Sea) => new[]
            {
                "Dark, tar-treated wood panels carved with sea serpents.",
                "Crudely carved pillars worn smooth by heavy sea spray.",
                "Iron nails driven in patterns of waves, rusting to the colour of dried blood.",
            },
            (SacredTradition.Nordic, Setting.Height) => new[]
            {
                "Whalebone archways support a turf ceiling.",
                "The walls are packed earth faced with split stone, sweating in summer.",
                "A single smoke-hole lets in a coin of cold light.",
            },
            (SacredTradition.Nordic, _) => new[]
            {
                "The beams are carved with interlocking beasts and old iron bosses.",
                "Turf on the roof, pine-resin in the joints, and a threshold of blackened oak.",
                "Every doorpost is a ship's timber reused, still smelling of pitch.",
            },
            (SacredTradition.Classical, Setting.Sea) => new[]
            {
                "Open-air colonnades draped in purple bougainvillea.",
                "White limestone against a blue bay, the steps worn concave by salt and feet.",
                "Capitals crusted with dried spray, the fluting still sharp in the shade.",
            },
            (SacredTradition.Classical, Setting.Marsh) => new[]
            {
                "Mosaic floors depicting ancient hunts and mythical beasts.",
                "The walls weep mineral water; laurel has taken the cracks.",
                "A humid grotto-finish of pebbles, shell, and painted plaster.",
            },
            (SacredTradition.Classical, _) => new[]
            {
                "Fluted columns holding up a crumbling, paint-faded frieze.",
                "Marble that takes the afternoon heat and gives it back after dusk.",
                "Bronze fittings gone green, and a pediment whose gods have lost their faces.",
            },
            (SacredTradition.Steppe, Setting.Dry) => new[]
            {
                "Covered in vibrant turquoise tiles that shift their shadows throughout the day.",
                "Intricate geometric brickwork, the only ornament the sun will not bleach away.",
                "Heavy wooden gates reinforced with hammered copper plates.",
            },
            (SacredTradition.Steppe, _) => new[]
            {
                "River-stone walls and a garden of apricot trees and wild steppe grasses.",
                "The brick is laid in spinning stars; dust fills every joint by noon.",
                "A dome the colour of old copper, patched where caravans have taken tiles for luck.",
            },
            (SacredTradition.Forest, Setting.Marsh) => new[]
            {
                "Thousands of faded silk ribbons tied to the overhead beams.",
                "The stilts are stained to the flood-line; frogs live in the bracing.",
                "Planks silvered by damp, and a floor that flexes over black water.",
            },
            (SacredTradition.Forest, Setting.River) => new[]
            {
                "Green, oxidized copper trim that mirrors the murky water.",
                "Driftwood shingles and a door that still smells of the river in rain.",
                "The foundations are packed clay and river cobble, always a little wet.",
            },
            (SacredTradition.Forest, Setting.Wood) => new[]
            {
                "Moss-covered pillars that seem to blend perfectly into giant trees.",
                "The shingles are wooden scales; lichen has taken the north face.",
                "Carved eaves of running beasts, their paint long since gone to weather.",
            },
            (SacredTradition.Forest, _) => new[]
            {
                "An ornate iconostasis painted in rich reds and golds inside.",
                "Dark timber, beeswax-polished, with silvered halos catching the lamp-light.",
                "Onion domes clad in wooden shingles, each a different weathered grey.",
            },
            _ => new[]
            {
                "The work is plain, honest, and already older than it looks.",
            },
        };
    }

    private static string[] Atmospheres(SacredTradition tradition, Setting setting) =>
        (tradition, setting) switch
        {
            (SacredTradition.Nordic, Setting.Sea) => new[]
            {
                "Cold, windy, and constantly echoing with crashing waves.",
                "Smells of pine resin, old iron, and wet rope.",
                "The air tastes of salt; gulls argue in the rafters.",
            },
            (SacredTradition.Nordic, Setting.Height) => new[]
            {
                "Dimly lit by whale-oil lamps with a quiet, subterranean chill.",
                "A packed-earth silence, broken only by drip-water.",
                "The mound holds the day's heat out and the old cold in.",
            },
            (SacredTradition.Nordic, _) => new[]
            {
                "Smells of pine resin and old iron.",
                "A tar-and-smoke gloom, even at noon.",
                "Wind finds every joint; the lamps never quite stand still.",
            },
            (SacredTradition.Classical, Setting.Sea) => new[]
            {
                "Blindingly bright, peaceful, filled with the sound of cicadas.",
                "Hot stone, a salt breeze, and the glitter of the bay below.",
                "The colonnade throws hard stripes of shade; doves live in the eaves.",
            },
            (SacredTradition.Classical, Setting.Marsh) => new[]
            {
                "Humid, smelling of sulfur, minerals, and sweet laurel leaves.",
                "A private heat, like breath held in a cave.",
                "Steam beads on the mosaics; the water talks constantly.",
            },
            (SacredTradition.Classical, _) => new[]
            {
                "Regal, exposed to the elements, baking in afternoon heat.",
                "White glare and the dry click of cicadas in the scrub.",
                "The marble is warm to the hand long after the sun has moved.",
            },
            (SacredTradition.Steppe, Setting.Dry) => new[]
            {
                "A quiet oasis of shade amidst burning desert winds.",
                "Dust in the throat, tile-cool under the dome.",
                "The wind hisses at the threshold and cannot come in.",
            },
            (SacredTradition.Steppe, _) => new[]
            {
                "Bustling with merchants, smelling of woodsmoke and roasted spices.",
                "Serene, meditative, punctuated by the rhythmic ringing of bronze bells.",
                "Shade, brick-dust, and the creak of a well-wheel.",
            },
            (SacredTradition.Forest, Setting.Marsh) => new[]
            {
                "Mystical, damp, filled with the croaking of frogs.",
                "A green gloom over black water; midges in every shaft of light.",
                "The pavilion smells of wet silk and rotting lilies.",
            },
            (SacredTradition.Forest, Setting.River) => new[]
            {
                "Melancholic, foggy, and isolated from the outside world.",
                "Mist off the water; the copper roof ticks as it cools.",
                "A lakeside hush, broken by a fish jumping and the drip from the eaves.",
            },
            (SacredTradition.Forest, Setting.Wood) => new[]
            {
                "Dead silent, dappled green sunlight, thick with forest loam.",
                "The wood drinks sound; even footsteps come back softened.",
                "Birds stop at the ring of stones, as if the air had a border.",
            },
            (SacredTradition.Forest, _) => new[]
            {
                "Heavy with the scent of burning beeswax and frankincense.",
                "Tall, dim, and sweet with old incense.",
                "The icons watch from the dark; wax has run in pale rivers down the stands.",
            },
            _ => new[]
            {
                "Quiet enough that a visitor hears their own breathing.",
            },
        };

    private static string[] Capacities(
        SacredTradition tradition, HolySiteKind kind, HolySiteScale scale) =>
        scale switch
        {
            HolySiteScale.Small when kind == HolySiteKind.Shrine => new[]
            {
                "Small. A tiny wooden platform, or a single room, that only fits a handful at a time.",
                "Small. A cramped, narrow place; two people must turn sideways to pass.",
                "Small. A wayside cell meant for one traveller and one lamp.",
            },
            HolySiteScale.Small when kind == HolySiteKind.Sanctuary && tradition == SacredTradition.Forest => new[]
            {
                "Small. A tight ring of stones; a dozen people fill it.",
                "Small. An intimate clearing, not a hall.",
            },
            HolySiteScale.Small when tradition == SacredTradition.Classical => new[]
            {
                "Small. An intimate, private grotto.",
                "Small. A niche and a step, not a hall.",
            },
            HolySiteScale.Small => new[]
            {
                "Small. A cramped, narrow cleft in the fabric of the place.",
                "Small. Holds a handful of people, and only if they stand.",
            },
            HolySiteScale.Medium when kind == HolySiteKind.Temple && tradition == SacredTradition.Nordic => new[]
            {
                "Medium. An open ring that holds a clan if they stand.",
                "Medium. Holds about fifty people comfortably, if they keep to the stones.",
            },
            HolySiteScale.Medium when tradition == SacredTradition.Classical => new[]
            {
                "Medium. An open-air structure that feels larger due to the high columns.",
                "Medium. A nave that holds a town congregation without crowding the altar.",
            },
            HolySiteScale.Medium when tradition == SacredTradition.Nordic => new[]
            {
                "Medium. Holds about fifty people comfortably.",
                "Medium. A hall of benches and a central fire, full on feast days.",
            },
            HolySiteScale.Medium => new[]
            {
                "Medium. A quiet courtyard surrounded by simple cells, or a tall narrow nave.",
                "Medium. Large enough for a congregation, too small for a market.",
            },
            HolySiteScale.Large when kind == HolySiteKind.Monastery && tradition == SacredTradition.Nordic => new[]
            {
                "Large. A cloister of cells, a hall, and an outer yard that can take a whole clan.",
                "Large. Workshops, dormitories, and a wind-scoured court around the bell-stave.",
            },
            HolySiteScale.Large when tradition == SacredTradition.Classical => new[]
            {
                "Large. A massive complex with housing, libraries, and gardens.",
                "Large. Terraces, cells, and a processional court that can swallow a festival.",
            },
            HolySiteScale.Large when tradition == SacredTradition.Nordic => new[]
            {
                "Large. A sprawling subterranean hall with hidden side rooms.",
                "Large. A mound-hall that can feast a clan and still have a dark to spare.",
            },
            HolySiteScale.Large when tradition == SacredTradition.Steppe => new[]
            {
                "Large. A fortified multi-building compound that doubles as a refuge.",
                "Large. Courts, wells, and guest-rooms enough for a delayed caravan.",
            },
            _ => new[]
            {
                "Large. A vast, open ring or a many-roomed house of worship deep in the country.",
                "Large. Pilgrims can sleep in the outer rooms without disturbing the centre.",
            },
        };

    private static string[] Statues(SacredTradition tradition, HolySiteKind kind) =>
        (tradition, kind) switch
        {
            (SacredTradition.Classical, HolySiteKind.Temple) => new[]
            {
                "A hollow bronze statue of a warrior, now green with age, standing in the inner sanctum.",
                "A pristine, twice-lifesize white marble statue of a goddess holding a sundial.",
                "A painted cult-image behind a grille, the ivory of the face cracked like drought-earth.",
            },
            (SacredTradition.Classical, _) => new[]
            {
                "A marble figure whose raised hand still casts a sundial's shadow at noon.",
                "A bronze votive, smaller than life, left green where pilgrims have touched it.",
            },
            (SacredTradition.Steppe, HolySiteKind.Temple) => new[]
            {
                "Four massive stone lions guard the four cardinal directions in the central courtyard.",
                "A seated, clay-moulded figure of an ancient sage, painted in faded blue and gold.",
            },
            (SacredTradition.Steppe, _) => new[]
            {
                "A seated, clay-moulded figure of an ancient sage, painted in faded blue and gold.",
                "A weathered stone rider, the horse's legs worn smooth by passing hands.",
            },
            (SacredTradition.Forest, _) => new[]
            {
                "A carved driftwood figure of a weeping figure looking out across the water.",
                "A dark wooden saint with a silver halo, the face rubbed pale by kisses.",
                "A horned wooden image bound in ivy, its eyes two river pebbles.",
            },
            _ => new[]
            {
                "A crude stone idol, heavily worn, the face already half returned to weather.",
                "A wooden figure tarred against rot, its many faces turned to the door, the fire, and the sea.",
            },
        };

    private static string[] Foci(SacredTradition tradition, Setting setting, HolySiteKind kind)
    {
        if (tradition == SacredTradition.Nordic && setting == Setting.Height)
        {
            return new[]
            {
                "The focal point is a frozen, sacred underground waterfall.",
                "A packed-earth throne nobody sits in, left for the sleeper under the mound.",
            };
        }

        if (tradition == SacredTradition.Nordic && kind == HolySiteKind.Temple)
        {
            return new[]
            {
                "The tallest stone is the image: uncarved, but dressed in tar and iron rings.",
                "A shallow pit at the centre holds the fire; no figure is set up beside it.",
            };
        }

        if (tradition == SacredTradition.Nordic)
        {
            return new[]
            {
                "A massive, carved wooden pillar at the centre depicts a multi-faced deity.",
                "The roof-tree itself is the image: a ship's mast stepped in the floor, hung with iron rings.",
            };
        }

        if (tradition == SacredTradition.Classical && kind == HolySiteKind.Sanctuary)
        {
            return new[]
            {
                "The sacred thermal spring itself is considered the presence of the divine.",
                "The pool is the altar; no image is allowed to stand in the water.",
            };
        }

        if (tradition == SacredTradition.Steppe)
        {
            return new[]
            {
                "The centre contains a beautifully illuminated, giant silk tapestry.",
                "A blank niche faces the open country; the sky is the image.",
            };
        }

        if (tradition == SacredTradition.Forest && setting == Setting.Marsh)
        {
            return new[]
            {
                "A sacred, ancient willow grows right through the centre of the floorboards.",
                "The water visible between the planks is the presence; nothing else is installed.",
            };
        }

        if (tradition == SacredTradition.Forest && kind == HolySiteKind.Sanctuary)
        {
            return new[]
            {
                "The architecture relies entirely on the natural geometry of the megaliths.",
                "No image is set up; the ring of stones is the congregation and the god.",
            };
        }

        if (tradition == SacredTradition.Forest)
        {
            return new[]
            {
                "The walls are lined with dark, somber painted icons of saints with silver halos.",
                "A bare icon-stand holds a single darkened panel, kissed until the paint is gone.",
            };
        }

        return new[]
        {
            "The empty niche is the point: whatever is worshipped here will not be pictured.",
            "A plain standing stone, uncarved, takes the place of an image.",
        };
    }

    private static string[] Offerings(
        SacredTradition tradition, Setting setting, HolySiteKind kind, FaithCharacter faith)
    {
        string[] lines = (tradition, setting, kind) switch
        {
            (SacredTradition.Nordic, Setting.Sea, _) => new[]
            {
                "A heavy iron fire bowl where hunters throw animal fat to keep the sacred flames roaring.",
                "Flat, salt-crusted stone ledges where people leave smooth beach stones and fish hooks.",
            },
            (SacredTradition.Nordic, Setting.Height, _) => new[]
            {
                "A deep water basin at the base of the ice where silver coins are tossed.",
                "A stone trough of meltwater; rings and small knives are left to rust in it.",
            },
            (SacredTradition.Nordic, _, _) => new[]
            {
                "A heavy iron fire bowl where hunters throw animal fat to keep the sacred flames roaring.",
                "A tarred post hung with iron rings; each visitor adds a nail.",
            },
            (SacredTradition.Classical, Setting.Marsh, _) => new[]
            {
                "Woven wicker baskets hung from the cave ceiling for flower garlands and written prayers.",
                "The spring's rim, where folded lead sheets scratched with petitions are slipped under stones.",
            },
            (SacredTradition.Classical, _, HolySiteKind.Temple) => new[]
            {
                "A raised marble altar at the steps where ears of wheat and summer fruits are left to bake in the sun.",
                "Sunken limestone troughs where visitors pour fresh olive oil and wine.",
            },
            (SacredTradition.Classical, _, _) => new[]
            {
                "Sunken limestone troughs where monks and visitors pour fresh olive oil and wine.",
                "A shallow dish of seawater and honey, renewed at first light.",
            },
            (SacredTradition.Steppe, _, HolySiteKind.Temple) => new[]
            {
                "Deep iron bins at the main gates where travellers deposit copper coins for the city's poor.",
                "A low bronze table meant for burning sweet desert herbs and pinon resin.",
            },
            (SacredTradition.Steppe, _, HolySiteKind.Monastery) => new[]
            {
                "A wide flat stone slab covered in fine sand where pilgrims draw prayers with their fingers.",
                "A bronze bowl of water; a visitor writes a name on the surface and waits for it to still.",
            },
            (SacredTradition.Steppe, _, _) => new[]
            {
                "A low bronze table meant for burning sweet desert herbs and pinon resin.",
                "A niche for a pinch of grain, a drop of oil, and a whispered destination.",
            },
            (SacredTradition.Forest, Setting.Marsh, _) => new[]
            {
                "The water beneath the platform; visitors slip breadcrumbs and polished glass into the swamp.",
                "Ribbons and small mirrors tied to the beams, each a question left for the spirit.",
            },
            (SacredTradition.Forest, Setting.River, _) => new[]
            {
                "A small copper bowl near the door where fishers leave fish scales for a safe voyage.",
                "A step into the water; the first fish of the day is given back unhooked.",
            },
            (SacredTradition.Forest, Setting.Wood, _) => new[]
            {
                "A flat, mossy sacrificial stone at the exact centre, often stained with old wine or milk offerings.",
                "A hollow in the lintel-stone where antlers, honey, and dark bread are left overnight.",
            },
            (SacredTradition.Forest, _, HolySiteKind.Church) => new[]
            {
                "A massive iron candelabra where hundreds of thin beeswax candles are lit and left to melt together.",
                "A sand-tray of candle-stubs before the darkest icon, renewed every vigil.",
            },
            (SacredTradition.Forest, _, _) => new[]
            {
                "A massive iron candelabra where hundreds of thin beeswax candles are lit and left to melt together.",
                "A wooden bowl of salt, bread, and river water, replaced at dusk.",
            },
            _ => new[]
            {
                "A plain stone where whatever the visitor can spare is left without ceremony.",
            },
        };

        return Fitting(lines, faith, PlainOffering);
    }

    private const string PlainOffering =
        "A plain stone where whatever the visitor can spare is left without ceremony.";

    private const string Unpictured =
        "The empty niche is the point: whatever is worshipped here will not be pictured.";

    /// <summary>
    /// Drops lines that contradict the congregation: wine on a dry altar, saints in an animism,
    /// a many-faced god in a monotheism. Falls back to <paramref name="fallback"/> if every line
    /// would lie.
    /// </summary>
    private static string[] Fitting(string[] lines, FaithCharacter faith, string fallback)
    {
        var kept = new List<string>(lines.Length);
        foreach (string line in lines)
        {
            if (Fits(line, faith)) kept.Add(line);
        }

        if (kept.Count > 0) return kept.ToArray();

        return new[] { fallback };
    }

    private static bool Fits(string line, FaithCharacter faith)
    {
        if (faith.Diet == DietaryRule.TabooIntoxicants && Has(line, "wine")) return false;

        if (faith.Diet == DietaryRule.TabooFlesh
            && Has(line, "animal fat", "fish hooks", "fish scales", "first fish", "antlers"))
        {
            return false;
        }

        if (!faith.AdmitsDedication(HolySiteDedicationKind.Saint)
            && Has(line, "saint", "saints", "silver halo", "silver halos"))
        {
            return false;
        }

        if (faith.Deity == DeityStructure.Monotheistic && Has(line, "multi-faced")) return false;

        return true;
    }

    private static bool Has(string line, params string[] needles)
    {
        foreach (string needle in needles)
        {
            if (line.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }

        return false;
    }
}

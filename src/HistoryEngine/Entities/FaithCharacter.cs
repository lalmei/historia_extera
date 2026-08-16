using HistoryEngine.Core;

namespace HistoryEngine.Entities;

/// <summary>How many gods a faith admits. Explicit values — part of the export format.</summary>
public enum DeityStructure
{
    Monotheistic = 0,
    Polytheistic = 1,
    Pantheistic = 2,
    Animistic = 3,
}

/// <summary>What the dead become. Explicit values — part of the export format.</summary>
public enum Afterlife
{
    /// <summary>Death is an ending. Memory is the only continuation.</summary>
    None = 0,

    /// <summary>The dead remain among their people, named and fed.</summary>
    Ancestral = 1,

    /// <summary>A weighing, and a reward or a punishment after it.</summary>
    Judgement = 2,

    /// <summary>The soul returns in another life.</summary>
    Rebirth = 3,

    /// <summary>The self dissolves back into the divine whole.</summary>
    Union = 4,
}

/// <summary>What a person is, besides a body. Explicit values — part of the export format.</summary>
public enum SoulDoctrine
{
    /// <summary>Breath that ends with the body.</summary>
    MortalBreath = 0,

    /// <summary>A spark that outlasts the flesh.</summary>
    ImmortalSpark = 1,

    /// <summary>A local eddy in a world that is itself alive.</summary>
    WorldSpirit = 2,

    /// <summary>A traveller that wears many lives.</summary>
    Transmigrating = 3,
}

/// <summary>Who speaks for the faith. Explicit values — part of the export format.</summary>
public enum AuthorityType
{
    /// <summary>A ranked clergy, one seat at the top.</summary>
    Hierarchical = 0,

    /// <summary>Local holy people, no centre that can bind the rest.</summary>
    Decentralized = 1,

    /// <summary>Houses of study and withdrawal, authority by learning rather than rank.</summary>
    Monastic = 2,
}

/// <summary>Who may take holy office. Explicit values — part of the export format.</summary>
public enum ClergyAdmission
{
    Open = 0,
    MaleOnly = 1,
    FemaleOnly = 2,

    /// <summary>The office runs in families. Invented clergy are a last resort.</summary>
    Bloodline = 3,
}

/// <summary>How the church lives. Explicit values — part of the export format.</summary>
public enum WealthPractice
{
    /// <summary>A share of the harvest, paid as duty.</summary>
    Tithes = 0,

    /// <summary>Land, houses, endowments.</summary>
    Landed = 1,

    /// <summary>Vowed poverty. The sacred place is a camp, not an estate.</summary>
    Mendicant = 2,
}

/// <summary>The virtue the faith will not trade away. Explicit values — part of the export format.</summary>
public enum DogmaEmphasis
{
    Honour = 0,
    Mercy = 1,
    Purity = 2,
    Knowledge = 3,
    Dominion = 4,
    Hospitality = 5,
    Power = 6,
    Justice = 7,
    Warfare = 8,
    Wealth = 9,
}

/// <summary>How often the ordinary faithful are called to pray. Explicit values — part of the export format.</summary>
public enum PrayerCadence
{
    Seasonal = 0,
    Weekly = 1,
    Daily = 2,
}

/// <summary>What the table forbids. Explicit values — part of the export format.</summary>
public enum DietaryRule
{
    None = 0,
    Fasting = 1,
    TabooFlesh = 2,
    TabooIntoxicants = 3,
}

/// <summary>How the faithful are marked in public. Explicit values — part of the export format.</summary>
public enum DressCode
{
    None = 0,
    Modest = 1,
    ClericalColour = 2,
    SacredMarks = 3,
}

/// <summary>
/// Which season the great gathering falls in. Explicit values — part of the export format.
/// </summary>
/// <remarks>
/// Four seasons because the world's calendar has four. The festival is named in the faith's
/// books; a later calendar tick that actually moved harvest, trade or happiness would read this
/// rather than invent a second one.
/// </remarks>
public enum FestivalSeason
{
    Spring = 0,
    Summer = 1,
    Autumn = 2,
    Winter = 3,
}

/// <summary>
/// What a faith is, besides how hard it presses outwards.
/// </summary>
/// <remarks>
/// <para>Rolled once at founding and never revised, the same way a culture's values are. A faith
/// that changed its gods because it took a new city would be a conversion wearing the old name.
/// Schism is how doctrine changes in this engine: a child faith, with a parent, and a congregation
/// that walked.</para>
///
/// <para><b>Every field either moves an outcome now or is the identity tomes already promised.</b>
/// Dials and authority change conversion, schism, holy-site form and who may hold a temple.
/// Cosmology, dogma and observance are what two codices of one religion agree about — previously
/// a random pick forked on the faith's id, now a pick from the table that id actually describes.
/// Terms that would need a new system (tithes on the harvest, festival trade, hereditary priesthood
/// as a true office succession) are stored so those systems can read them rather than invent a
/// second vocabulary.</para>
/// </remarks>
public sealed record FaithCharacter(
    DeityStructure Deity,
    Afterlife Afterlife,
    SoulDoctrine Soul,
    AuthorityType Authority,
    ClergyAdmission Clergy,
    bool CelibateClergy,
    WealthPractice Wealth,
    DogmaEmphasis Dogma,
    PrayerCadence Prayer,
    DietaryRule Diet,
    DressCode Dress,
    FestivalSeason Festival,
    double Fervour,
    double Zealotry,
    double Tolerance,
    double SchismProneness,
    double Syncretism)
{
    /// <summary>
    /// A new faith, coloured by the people it arose among.
    /// </summary>
    /// <remarks>
    /// <paramref name="fervour"/> is passed in rather than drawn here so the founding stream can
    /// keep rolling it first, exactly as it did when fervour was the faith's only dial. Everything
    /// else comes from a forked character stream and cannot shift that number.
    /// </remarks>
    public static FaithCharacter Roll(IRng rng, CultureValues culture, double fervour)
    {
        DeityStructure deity = Weighted(rng, new[]
        {
            (DeityStructure.Monotheistic, 2 + Weight(culture.Piety, 4) + Weight(culture.Tradition, 1)),
            (DeityStructure.Polytheistic, 3 + Weight(1.0 - culture.Piety, 2)),
            (DeityStructure.Pantheistic, 1 + Weight(culture.Learning, 4)),
            (DeityStructure.Animistic, 2 + Weight(culture.Tradition, 3) + Weight(1.0 - culture.Learning, 1)),
        });

        Afterlife afterlife = deity switch
        {
            DeityStructure.Animistic => Weighted(rng, new[]
            {
                (Afterlife.Ancestral, 5),
                (Afterlife.None, 2),
                (Afterlife.Union, 1),
            }),
            DeityStructure.Pantheistic => Weighted(rng, new[]
            {
                (Afterlife.Union, 5),
                (Afterlife.Rebirth, 2),
                (Afterlife.None, 1),
            }),
            DeityStructure.Monotheistic => Weighted(rng, new[]
            {
                (Afterlife.Judgement, 5),
                (Afterlife.Union, 2),
                (Afterlife.Ancestral, 1),
            }),
            _ => Weighted(rng, new[]
            {
                (Afterlife.Ancestral, 3),
                (Afterlife.Judgement, 2),
                (Afterlife.Rebirth, 2),
                (Afterlife.None, 1),
            }),
        };

        SoulDoctrine soul = afterlife switch
        {
            Afterlife.None => SoulDoctrine.MortalBreath,
            Afterlife.Rebirth => SoulDoctrine.Transmigrating,
            Afterlife.Union => Weighted(rng, new[]
            {
                (SoulDoctrine.WorldSpirit, 3),
                (SoulDoctrine.ImmortalSpark, 1),
            }),
            Afterlife.Ancestral => Weighted(rng, new[]
            {
                (SoulDoctrine.ImmortalSpark, 3),
                (SoulDoctrine.WorldSpirit, 2),
            }),
            _ => SoulDoctrine.ImmortalSpark,
        };

        AuthorityType authority = Weighted(rng, new[]
        {
            (AuthorityType.Hierarchical, 2 + Weight(culture.Piety, 3) + Weight(culture.Tradition, 1)),
            (AuthorityType.Decentralized, 2 + Weight(1.0 - culture.Piety, 3)),
            (AuthorityType.Monastic, 1 + Weight(culture.Learning, 4)),
        });

        ClergyAdmission clergy = Weighted(rng, new[]
        {
            (ClergyAdmission.Open, 5),
            (ClergyAdmission.MaleOnly, 2 + Weight(culture.Tradition, 2)),
            (ClergyAdmission.FemaleOnly, 1),
            (ClergyAdmission.Bloodline, 1 + Weight(culture.Tradition, 3)),
        });

        bool celibate = authority == AuthorityType.Monastic
            ? rng.Chance(0.72)
            : rng.Chance(0.18 + (culture.Piety * 0.22));

        WealthPractice wealth = authority switch
        {
            AuthorityType.Monastic => Weighted(rng, new[]
            {
                (WealthPractice.Mendicant, 3),
                (WealthPractice.Landed, 3),
                (WealthPractice.Tithes, 1),
            }),
            AuthorityType.Decentralized => Weighted(rng, new[]
            {
                (WealthPractice.Mendicant, 3),
                (WealthPractice.Tithes, 2),
                (WealthPractice.Landed, 1),
            }),
            _ => Weighted(rng, new[]
            {
                (WealthPractice.Tithes, 4),
                (WealthPractice.Landed, 3),
                (WealthPractice.Mendicant, 1),
            }),
        };

        DogmaEmphasis dogma = Weighted(rng, new[]
        {
            (DogmaEmphasis.Honour, 2 + Weight(culture.Aggression, 2)),
            (DogmaEmphasis.Mercy, 2 + Weight(1.0 - culture.Aggression, 3)),
            (DogmaEmphasis.Purity, 2 + Weight(culture.Piety, 3)),
            (DogmaEmphasis.Knowledge, 1 + Weight(culture.Learning, 4)),
            (DogmaEmphasis.Dominion, 1 + Weight(culture.Expansionism, 3)),
            (DogmaEmphasis.Hospitality, 2 + Weight(culture.Mercantile, 3)),
            (DogmaEmphasis.Power, 1 + Weight(culture.Expansionism, 3)),
            (DogmaEmphasis.Justice, 1 + Weight(culture.Piety, 3)),
            (DogmaEmphasis.Warfare, 1 + Weight(culture.Aggression, 3)),
            (DogmaEmphasis.Wealth, 1 + Weight(culture.Mercantile, 3)),
        });

        PrayerCadence prayer = Weighted(rng, new[]
        {
            (PrayerCadence.Seasonal, 3 + Weight(1.0 - culture.Piety, 2)),
            (PrayerCadence.Weekly, 3),
            (PrayerCadence.Daily, 1 + Weight(culture.Piety, 4)),
        });

        DietaryRule diet = Weighted(rng, new[]
        {
            (DietaryRule.None, 4),
            (DietaryRule.Fasting, 2 + Weight(culture.Piety, 2)),
            (DietaryRule.TabooFlesh, 2),
            (DietaryRule.TabooIntoxicants, 1 + Weight(culture.Tradition, 2)),
        });

        DressCode dress = Weighted(rng, new[]
        {
            (DressCode.None, 4),
            (DressCode.Modest, 2 + Weight(culture.Piety, 2)),
            (DressCode.ClericalColour, 2 + Weight(culture.Tradition, 1)),
            (DressCode.SacredMarks, 1 + Weight(culture.Tradition, 2)),
        });

        var festival = (FestivalSeason)rng.NextInt(4);

        return new FaithCharacter(
            deity,
            afterlife,
            soul,
            authority,
            clergy,
            celibate,
            wealth,
            dogma,
            prayer,
            diet,
            dress,
            festival,
            DetMath.Clamp01(fervour),
            DetMath.Clamp01(rng.NextDouble(0.12, 0.88) + (culture.Piety * 0.12)),
            DetMath.Clamp01(rng.NextDouble(0.12, 0.88) - (culture.Aggression * 0.12) + (culture.Mercantile * 0.08)),
            DetMath.Clamp01(rng.NextDouble(0.10, 0.80) + ((1.0 - culture.Tradition) * 0.18)),
            DetMath.Clamp01(rng.NextDouble(0.10, 0.80) + (culture.Mercantile * 0.16) + (culture.Learning * 0.08)));
    }

    /// <summary>
    /// A splinter: the parent's bones, shifted where a schism actually argues.
    /// </summary>
    /// <remarks>
    /// Cosmology usually survives a split — people leave a church over who may speak for it, not
    /// over whether the soul transmigrates. Authority and dogma are the seats of the argument, and
    /// the dials drift by how schismatic the parent already was. A syncretic parent keeps its
    /// children closer, which is what syncretism is for.
    /// </remarks>
    public static FaithCharacter FromParent(
        FaithCharacter parent, IRng rng, CultureValues culture, double fervour)
    {
        FaithCharacter fresh = Roll(rng.Fork("fresh"), culture, fervour);

        double keep = DetMath.Clamp01(0.55 + (parent.Syncretism * 0.35));
        double drift = DetMath.Clamp01(0.22 + (parent.SchismProneness * 0.40) - (parent.Syncretism * 0.18));

        AuthorityType authority = rng.Chance(0.58) ? FlipAuthority(parent.Authority, rng) : parent.Authority;
        DogmaEmphasis dogma = rng.Chance(0.42) ? fresh.Dogma : parent.Dogma;
        ClergyAdmission clergy = rng.Chance(keep) ? parent.Clergy : fresh.Clergy;
        WealthPractice wealth = rng.Chance(keep) ? parent.Wealth : fresh.Wealth;

        return new FaithCharacter(
            rng.Chance(keep) ? parent.Deity : fresh.Deity,
            rng.Chance(keep) ? parent.Afterlife : fresh.Afterlife,
            rng.Chance(keep) ? parent.Soul : fresh.Soul,
            authority,
            clergy,
            rng.Chance(keep) ? parent.CelibateClergy : fresh.CelibateClergy,
            wealth,
            dogma,
            rng.Chance(keep) ? parent.Prayer : fresh.Prayer,
            rng.Chance(keep) ? parent.Diet : fresh.Diet,
            rng.Chance(keep) ? parent.Dress : fresh.Dress,
            rng.Chance(keep) ? parent.Festival : fresh.Festival,
            DetMath.Clamp01(fervour),
            Drift(parent.Zealotry, fresh.Zealotry, drift),
            Drift(parent.Tolerance, fresh.Tolerance, drift),
            Drift(parent.SchismProneness, fresh.SchismProneness, drift),
            Drift(parent.Syncretism, fresh.Syncretism, drift));
    }

    /// <summary>
    /// How hard this faith presses a settlement that does not yet follow it, in the same units
    /// fervour used to occupy alone.
    /// </summary>
    /// <remarks>
    /// Fervour is still the engine. Tolerance damps the press only against a congregation that
    /// already believes something else — a missionary faith still takes empty country, and a
    /// tolerant one would rather coexist than overwrite a neighbour. Occupied-target damping is
    /// what stops every high-fervour faith reading as a crusade.
    /// </remarks>
    public double OutwardPressure(bool targetAlreadyBelieves)
    {
        double press = 0.45 + (Fervour * 0.85);
        if (!targetAlreadyBelieves) return press;

        return press * DetMath.Lerp(1.0, 0.70, Tolerance);
    }

    /// <summary>
    /// How readily a congregation of this faith gives itself up, in [0, 1].
    /// </summary>
    /// <remarks>
    /// Multiplies the cultural resistance already computed from tradition and entrenchment.
    /// Zealotry is the defence; syncretism is the door left open.
    /// </remarks>
    public double Holdfast() =>
        DetMath.Clamp01(DetMath.Lerp(1.0, 0.52, Zealotry) * DetMath.Lerp(1.0, 1.22, Syncretism));

    /// <summary>
    /// Yearly schism chance multiplier around 1.
    /// </summary>
    public double SchismWeight()
    {
        double weight = DetMath.Lerp(0.42, 1.85, SchismProneness);
        return Authority switch
        {
            AuthorityType.Hierarchical => weight * 0.72,
            AuthorityType.Decentralized => weight * 1.38,
            _ => weight,
        };
    }

    /// <summary>
    /// Whether two faiths would recognise each other as kin, from doctrine alone.
    /// </summary>
    /// <remarks>
    /// Matching deity-structure is kinship only where syncretism is high enough to treat a
    /// neighbour's pantheon as a dialect of one's own — otherwise two monotheisms are still two
    /// churches. Parentage is a separate test, on the faiths themselves.
    /// </remarks>
    public bool DoctrinallyCloseTo(FaithCharacter other) =>
        Deity == other.Deity && (Syncretism + other.Syncretism) >= 1.1;

    /// <summary>A stranger of this sex may hold holy office.</summary>
    public bool Admits(Sex sex) => Clergy switch
    {
        ClergyAdmission.MaleOnly => sex == Sex.Male,
        ClergyAdmission.FemaleOnly => sex == Sex.Female,
        _ => true,
    };

    /// <summary>The sex an invented cleric is, when the rule names one.</summary>
    public Sex ClericSex(IRng rng) => Clergy switch
    {
        ClergyAdmission.MaleOnly => Sex.Male,
        ClergyAdmission.FemaleOnly => Sex.Female,
        _ => rng.Chance(0.5) ? Sex.Male : Sex.Female,
    };

    /// <summary>
    /// How readily the crown names this faith's high priest, as a multiplier on the existing
    /// HighPriest mandate term.
    /// </summary>
    /// <remarks>
    /// A ranked church has its own centre and resents a royal appointment. A monastic one even
    /// more so. A faith of local holy people has no centre to resent with, so the crown's hand
    /// lands more often — which is the difference between a pope and a shaman, from the throne's
    /// point of view.
    /// </remarks>
    public double PriestlyMandate() => Authority switch
    {
        AuthorityType.Hierarchical => 0.55,
        AuthorityType.Monastic => 0.35,
        _ => 1.15,
    };

    /// <summary>Weights for the form a new holy place takes. A zero weight is a refusal.</summary>
    /// <remarks>
    /// <para>"Church" is the one form that names a congregation's house in a way that implies
    /// saints, a nave, and a single god on the altar. An animistic or pantheistic faith that
    /// raised one would contradict itself in the first word of the site's name. The other forms
    /// are broader — a temple, shrine, monastery or sanctuary can stand for many theologies — and
    /// stay available.</para>
    ///
    /// <para>Weights may be zero. Callers must skip those entries rather than treating a church
    /// as the leftover 1 that <c>Max(1, …)</c> used to force.</para>
    /// </remarks>
    public (HolySiteKind Kind, int Weight)[] HolySiteWeights()
    {
        int shrine = 2;
        int temple = 2;
        int church = AdmitsKind(HolySiteKind.Church) ? 2 : 0;
        int monastery = AdmitsKind(HolySiteKind.Monastery) ? 2 : 0;
        int sanctuary = 2;

        switch (Authority)
        {
            case AuthorityType.Hierarchical:
                if (church > 0) church += 4;
                temple += 2;
                shrine -= 1;
                break;
            case AuthorityType.Monastic:
                if (monastery > 0) monastery += 5;
                sanctuary += 1;
                if (church > 0) church -= 1;
                break;
            default:
                shrine += 4;
                sanctuary += 2;
                if (church > 0) church -= 1;
                break;
        }

        switch (Wealth)
        {
            case WealthPractice.Landed:
                temple += 2;
                if (church > 0) church += 1;
                if (monastery > 0) monastery += 1;
                break;
            case WealthPractice.Mendicant:
                shrine += 2;
                sanctuary += 3;
                if (church > 0) church -= 1;
                temple -= 1;
                break;
        }

        return new[]
        {
            (HolySiteKind.Shrine, Math.Max(0, shrine)),
            (HolySiteKind.Temple, Math.Max(0, temple)),
            (HolySiteKind.Church, Math.Max(0, church)),
            (HolySiteKind.Monastery, Math.Max(0, monastery)),
            (HolySiteKind.Sanctuary, Math.Max(0, sanctuary)),
        };
    }

    /// <summary>Whether this faith would raise a house of this form.</summary>
    public bool AdmitsKind(HolySiteKind kind) => kind switch
    {
        HolySiteKind.Church => Deity is DeityStructure.Monotheistic
            || (Deity == DeityStructure.Polytheistic && Authority == AuthorityType.Hierarchical),
        HolySiteKind.Monastery => Authority != AuthorityType.Decentralized
            || Deity != DeityStructure.Animistic,
        _ => true,
    };

    /// <summary>
    /// Whether a house of this faith can be raised for this kind of presence.
    /// </summary>
    /// <remarks>
    /// Kind and tradition still nominate, but they used to nominate saints for an animism and a
    /// primordial god for a monotheism. Those are not local colour; they are a second religion
    /// standing inside the first. A martyr, sage or ancestral king can be honoured by almost
    /// anyone. A God, an Ancient God, a Nature Spirit and a Saint cannot.
    /// </remarks>
    public bool AdmitsDedication(HolySiteDedicationKind kind) => Deity switch
    {
        DeityStructure.Monotheistic => kind is not (
            HolySiteDedicationKind.AncientGod or HolySiteDedicationKind.NatureSpirit),
        DeityStructure.Pantheistic => kind is not (
            HolySiteDedicationKind.God
            or HolySiteDedicationKind.AncientGod
            or HolySiteDedicationKind.Saint),
        DeityStructure.Animistic => kind is not (
            HolySiteDedicationKind.God
            or HolySiteDedicationKind.AncientGod
            or HolySiteDedicationKind.DivineConcept
            or HolySiteDedicationKind.Saint
            or HolySiteDedicationKind.LivingKing),
        _ => true,
    };

    /// <summary>The dedication a house falls back on when every other nomination was refused.</summary>
    public HolySiteDedicationKind PreferredDedication() => Deity switch
    {
        DeityStructure.Monotheistic => HolySiteDedicationKind.God,
        DeityStructure.Pantheistic => HolySiteDedicationKind.CosmicForce,
        DeityStructure.Animistic => HolySiteDedicationKind.NatureSpirit,
        _ => HolySiteDedicationKind.AncientGod,
    };

    /// <summary>
    /// The inclinations this faith teaches, on the same dials a person holds.
    /// </summary>
    /// <remarks>
    /// What a doctrine argues for, not what any one believer is. A figure's own disposition is
    /// rolled around their culture and then pulled toward this — see
    /// <see cref="Disposition.TintedBy"/>. Stored nowhere; derived from the character that was
    /// already rolled at founding, so a new consumer cannot disagree with conversion about what
    /// this church is.
    /// </remarks>
    public CultureValues Inclines()
    {
        double aggression = 0.50;
        double expansionism = 0.50;
        double piety = 0.50;
        double tradition = 0.50;
        double mercantile = 0.50;
        double learning = 0.50;

        switch (Dogma)
        {
            case DogmaEmphasis.Honour:
                aggression = 0.62;
                tradition = 0.72;
                break;
            case DogmaEmphasis.Mercy:
                aggression = 0.28;
                piety = 0.68;
                break;
            case DogmaEmphasis.Purity:
                piety = 0.78;
                tradition = 0.72;
                break;
            case DogmaEmphasis.Knowledge:
                learning = 0.82;
                tradition = 0.38;
                break;
            case DogmaEmphasis.Dominion:
                expansionism = 0.80;
                aggression = 0.62;
                break;
            case DogmaEmphasis.Hospitality:
                mercantile = 0.76;
                aggression = 0.34;
                break;
            case DogmaEmphasis.Power:
                expansionism = 0.74;
                aggression = 0.70;
                break;
            case DogmaEmphasis.Justice:
                piety = 0.70;
                tradition = 0.62;
                break;
            case DogmaEmphasis.Warfare:
                aggression = 0.84;
                expansionism = 0.70;
                break;
            case DogmaEmphasis.Wealth:
                mercantile = 0.84;
                learning = 0.38;
                break;
        }

        // How hard the teaching is pressed, not a second dogma. Tolerance cools a warlike
        // church; a monastic one reads; a syncretic one trades.
        piety = DetMath.Clamp01(piety + (Fervour * 0.12) + (Zealotry * 0.10));
        aggression = DetMath.Clamp01(aggression - (Tolerance * 0.10));
        tradition = DetMath.Clamp01(tradition + (Zealotry * 0.08) - (SchismProneness * 0.08));
        learning = DetMath.Clamp01(learning + (Authority == AuthorityType.Monastic ? 0.12 : 0.0));
        mercantile = DetMath.Clamp01(mercantile + (Syncretism * 0.08));

        return new CultureValues(
            aggression, expansionism, piety, tradition, mercantile, learning);
    }

    /// <summary>
    /// How much this church's shape inclines a believer to decide things personally.
    /// </summary>
    /// <remarks>
    /// Authority rather than dogma, because this is about who may speak for the faith, not
    /// what they say. A ranked church has a centre; a faith of local holy people does not.
    /// </remarks>
    public double OfficeInclination() => Authority switch
    {
        AuthorityType.Hierarchical => 0.70,
        AuthorityType.Monastic => 0.42,
        _ => 0.28,
    };

    /// <summary>Extra weight on a dedication kind, from what this faith actually worships.</summary>
    public int DedicationBias(HolySiteDedicationKind kind)
    {
        if (!AdmitsDedication(kind)) return 0;

        return (Deity, kind) switch
        {
            (DeityStructure.Monotheistic, HolySiteDedicationKind.God) => 5,
            (DeityStructure.Monotheistic, HolySiteDedicationKind.DivineConcept) => 2,
            (DeityStructure.Polytheistic, HolySiteDedicationKind.AncientGod) => 4,
            (DeityStructure.Polytheistic, HolySiteDedicationKind.God) => 2,
            (DeityStructure.Pantheistic, HolySiteDedicationKind.CosmicForce) => 5,
            (DeityStructure.Pantheistic, HolySiteDedicationKind.DivineConcept) => 3,
            (DeityStructure.Animistic, HolySiteDedicationKind.NatureSpirit) => 5,
            (DeityStructure.Animistic, HolySiteDedicationKind.AncestralKing) => 2,
            _ => 0,
        };
    }

    /// <summary>
    /// How much more likely an independent sanctuary is, on top of the piety term already there.
    /// </summary>
    public double IndependentSiteBias() => Wealth switch
    {
        WealthPractice.Mendicant => 0.22,
        WealthPractice.Landed => -0.08,
        _ => 0.0,
    };

    private static AuthorityType FlipAuthority(AuthorityType current, IRng rng) => current switch
    {
        AuthorityType.Hierarchical => rng.Chance(0.55)
            ? AuthorityType.Decentralized
            : AuthorityType.Monastic,
        AuthorityType.Monastic => rng.Chance(0.55)
            ? AuthorityType.Hierarchical
            : AuthorityType.Decentralized,
        _ => rng.Chance(0.55) ? AuthorityType.Hierarchical : AuthorityType.Monastic,
    };

    private static double Drift(double from, double toward, double t) =>
        DetMath.Clamp01(DetMath.Lerp(from, toward, t));

    private static int Weight(double dial, int span) =>
        (int)Math.Round(DetMath.Clamp01(dial) * span);

    private static T Weighted<T>(IRng rng, (T Value, int Weight)[] options)
    {
        int total = 0;
        foreach ((T _, int weight) in options)
        {
            if (weight > 0) total += weight;
        }

        if (total <= 0) return options[0].Value;

        int roll = rng.NextInt(total);
        int acc = 0;
        foreach ((T value, int weight) in options)
        {
            if (weight <= 0) continue;
            acc += weight;
            if (roll < acc) return value;
        }

        return options[options.Length - 1].Value;
    }
}

/// <summary>Readable labels for a faith's structure. The chronicle and the viewer share these.</summary>
public static class FaithCharacters
{
    public static string Label(DeityStructure deity) => deity switch
    {
        DeityStructure.Monotheistic => "monotheistic",
        DeityStructure.Polytheistic => "polytheistic",
        DeityStructure.Pantheistic => "pantheistic",
        _ => "animistic",
    };

    public static string Label(Afterlife afterlife) => afterlife switch
    {
        Afterlife.None => "no afterlife",
        Afterlife.Ancestral => "an ancestral afterlife",
        Afterlife.Judgement => "a judged afterlife",
        Afterlife.Rebirth => "rebirth",
        Afterlife.Union => "union with the divine",
        _ => "an afterlife",
    };

    public static string Label(SoulDoctrine soul) => soul switch
    {
        SoulDoctrine.MortalBreath => "a mortal breath",
        SoulDoctrine.ImmortalSpark => "an immortal spark",
        SoulDoctrine.WorldSpirit => "a world-spirit",
        _ => "a transmigrating soul",
    };

    public static string Label(AuthorityType authority) => authority switch
    {
        AuthorityType.Hierarchical => "hierarchical",
        AuthorityType.Decentralized => "decentralized",
        _ => "monastic",
    };

    public static string Label(ClergyAdmission clergy) => clergy switch
    {
        ClergyAdmission.MaleOnly => "men only",
        ClergyAdmission.FemaleOnly => "women only",
        ClergyAdmission.Bloodline => "a sacred bloodline",
        _ => "open",
    };

    public static string Label(WealthPractice wealth) => wealth switch
    {
        WealthPractice.Tithes => "tithes",
        WealthPractice.Landed => "landed",
        _ => "mendicant",
    };

    public static string Label(DogmaEmphasis dogma) => dogma switch
    {
        DogmaEmphasis.Honour => "honour",
        DogmaEmphasis.Mercy => "mercy",
        DogmaEmphasis.Purity => "purity",
        DogmaEmphasis.Knowledge => "knowledge",
        DogmaEmphasis.Dominion => "dominion",
        DogmaEmphasis.Power => "power",
        DogmaEmphasis.Justice => "justice",
        DogmaEmphasis.Warfare => "warfare",
        DogmaEmphasis.Wealth => "wealth",
        _ => "hospitality",
    };

    public static string Label(PrayerCadence prayer) => prayer switch
    {
        PrayerCadence.Seasonal => "seasonal prayer",
        PrayerCadence.Weekly => "weekly prayer",
        _ => "daily prayer",
    };

    public static string Label(DietaryRule diet) => diet switch
    {
        DietaryRule.Fasting => "fasting",
        DietaryRule.TabooFlesh => "a taboo on flesh",
        DietaryRule.TabooIntoxicants => "a taboo on intoxicants",
        _ => "no dietary rule",
    };

    public static string Label(DressCode dress) => dress switch
    {
        DressCode.Modest => "modest dress",
        DressCode.ClericalColour => "a clerical colour",
        DressCode.SacredMarks => "sacred marks",
        _ => "no dress code",
    };

    public static string Label(FestivalSeason season) => season switch
    {
        FestivalSeason.Spring => "spring",
        FestivalSeason.Summer => "summer",
        FestivalSeason.Autumn => "autumn",
        _ => "winter",
    };
}

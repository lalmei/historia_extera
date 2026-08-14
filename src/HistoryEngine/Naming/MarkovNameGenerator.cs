using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.World;

namespace HistoryEngine.Naming;

/// <summary>
/// Names everything from per-culture Markov languages.
/// </summary>
/// <remarks>
/// <para>Each culture gets a <see cref="NamingLanguage"/> derived from its language seed, and every
/// name is a pure function of that language plus the entity's id. Two consequences worth being
/// explicit about:</para>
///
/// <list type="bullet">
///   <item><description>A culture's names are <b>coherent</b> because they all come from one
///   blended model with one set of sound shifts — its kings, cities and dynasties sound related
///   without any of them being told to.</description></item>
///   <item><description>Names are <b>stable</b> under unrelated change. Adding a system, or
///   founding one more settlement earlier in the run, cannot alter any existing name, because no
///   name depends on anything but its own id.</description></item>
/// </list>
///
/// <para>Both the languages and the generated names are memoised. That is pure memoisation of a
/// deterministic function, so it cannot affect reproducibility — and it matters in practice
/// because <c>WorldState.NameOf</c> is called repeatedly per entity while narrating and exporting,
/// and there are over a thousand regions in a default world.</para>
/// </remarks>
public sealed class MarkovNameGenerator : INameGenerator
{
    private readonly ulong _worldSeed;
    private readonly Dictionary<ulong, NamingLanguage> _languages = new();
    private readonly Dictionary<EntityId, string> _names = new();

    /// <summary>
    /// The language geography is named in.
    /// </summary>
    /// <remarks>
    /// Derived from the world seed rather than any culture's, so region names are older than and
    /// independent of every realm that comes to hold them.
    /// </remarks>
    private readonly Lazy<NamingLanguage> _worldLanguage;

    public MarkovNameGenerator(ulong worldSeed)
    {
        _worldSeed = worldSeed;
        _worldLanguage = new Lazy<NamingLanguage>(
            () => NamingLanguage.Derive(Hash.Combine(worldSeed, Hash.OfString("lang.world"))));
    }

    public ulong LanguageSeedFor(EntityId cultureId) =>
        Hash.Combine(_worldSeed, Hash.SplitMix64((ulong)cultureId.ToDiscriminator()));

    /// <summary>The language a culture speaks. Exposed so the export can describe it.</summary>
    public NamingLanguage LanguageOf(Culture culture) => LanguageFor(culture.LanguageSeed);

    public NamingLanguage LanguageFor(ulong languageSeed)
    {
        if (_languages.TryGetValue(languageSeed, out NamingLanguage? cached)) return cached;

        NamingLanguage language = NamingLanguage.Derive(languageSeed);
        _languages[languageSeed] = language;
        return language;
    }

    public string ForCulture(EntityId id) =>
        Memoise(id, () =>
        {
            NamingLanguage language = LanguageFor(LanguageSeedFor(id));
            return language.People(StreamFor(language.Seed, "culture", id));
        });

    /// <summary>
    /// A civilization's name.
    /// </summary>
    /// <remarks>
    /// An ethnonym — civilizations are named for their people. A place name or an English
    /// construction like "the Vethric Empire" would undercut the invented language at the most
    /// visible point in the whole chronicle.
    /// </remarks>
    public string ForCivilization(EntityId id, Culture culture) =>
        Memoise(id, () =>
        {
            NamingLanguage language = LanguageOf(culture);
            return language.People(StreamFor(language.Seed, "civilization", id));
        });

    public string ForSettlement(EntityId id, Culture culture) =>
        Memoise(id, () =>
        {
            NamingLanguage language = LanguageOf(culture);
            return language.Place(StreamFor(language.Seed, "settlement", id));
        });

    public string ForFigure(EntityId id, Culture culture) =>
        Memoise(id, () =>
        {
            NamingLanguage language = LanguageOf(culture);
            return language.Person(StreamFor(language.Seed, "figure", id));
        });

    public string ForRegion(EntityId id, Biome biome) =>
        Memoise(id, () =>
        {
            NamingLanguage language = _worldLanguage.Value;
            return language.Place(StreamFor(language.Seed, "region", id));
        });

    public string ForDynasty(EntityId id, Culture culture) =>
        Memoise(id, () =>
        {
            NamingLanguage language = LanguageOf(culture);
            return language.Dynasty(StreamFor(language.Seed, "dynasty", id));
        });

    /// <summary>
    /// A faith's name.
    /// </summary>
    /// <remarks>
    /// From the people-name model rather than the place model: faiths are called after the people
    /// who first held them far more often than after the town it started in, and an ethnonym is
    /// what lets the chronicle say "the Semnoi" the way it says "the Vethric".
    /// </remarks>
    public string ForReligion(EntityId id, Culture culture) =>
        Memoise(id, () =>
        {
            NamingLanguage language = LanguageOf(culture);
            return language.People(StreamFor(language.Seed, "religion", id));
        });

    /// <summary>
    /// The stream one name is drawn from: language seed, purpose, and entity id.
    /// </summary>
    /// <remarks>
    /// A fresh stream per name is what makes each name depend on nothing but its own identity.
    /// Threading one stream through a naming pass would be cheaper and would make every name
    /// depend on how many were requested before it.
    /// </remarks>
    private static IRng StreamFor(ulong languageSeed, string purpose, EntityId id) =>
        new Pcg32(languageSeed).Fork("name." + purpose, id.ToDiscriminator());

    private string Memoise(EntityId id, Func<string> generate)
    {
        if (_names.TryGetValue(id, out string? cached)) return cached;

        string name = generate();
        _names[id] = name;
        return name;
    }
}

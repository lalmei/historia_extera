using HistoryEngine.Core;
using HistoryEngine.Entities;
using HistoryEngine.Naming;
using Xunit;

namespace HistoryEngine.Tests;

public sealed class CorpusTests
{
    [Fact]
    public void EveryFamilyLoads()
    {
        foreach (string family in NameCorpus.FamilyNames)
        {
            NameCorpus corpus = NameCorpus.Load(family);

            Assert.Equal(family, corpus.Family);

            // Order-3 Markov needs a reasonable amount of text or it degenerates into recall.
            Assert.True(corpus.Given.Count >= 40, $"{family} has only {corpus.Given.Count} given names");
            Assert.True(corpus.Places.Count >= 30, $"{family} has only {corpus.Places.Count} places");
            Assert.NotEmpty(corpus.PlaceSuffixes);
            Assert.NotEmpty(corpus.PeopleSuffixes);
        }
    }

    /// <summary>Suffixes are written with a leading hyphen and must be stripped on load.</summary>
    [Fact]
    public void SuffixesAreStrippedOfTheirHyphen()
    {
        foreach (NameCorpus corpus in NameCorpus.LoadAll())
        {
            foreach (string suffix in corpus.PlaceSuffixes.Concat(corpus.PeopleSuffixes))
            {
                Assert.False(suffix.StartsWith('-'), $"{corpus.Family}: '{suffix}' kept its hyphen");
                Assert.NotEmpty(suffix);
            }
        }
    }

    [Fact]
    public void NamesAreCleanSingleTokens()
    {
        foreach (NameCorpus corpus in NameCorpus.LoadAll())
        {
            foreach (string name in corpus.Given.Concat(corpus.Places))
            {
                Assert.Equal(name.Trim(), name);
                Assert.DoesNotContain('#', name);
                Assert.True(name.Length >= 3, $"{corpus.Family}: '{name}' is too short to train on");
            }
        }
    }

    [Fact]
    public void UnknownSectionsAreRejected() =>
        Assert.Throws<InvalidOperationException>(
            () => NameCorpus.Parse("bogus", "[nonsense]\nFoo\n"));

    [Fact]
    public void MissingSectionsAreRejected() =>
        Assert.Throws<InvalidOperationException>(
            () => NameCorpus.Parse("bogus", "[given]\nFoo\n"));
}

public sealed class MarkovNameModelTests
{
    /// <summary>
    /// Generated names must never appear verbatim in the training data.
    /// </summary>
    /// <remarks>
    /// The reason this is a correctness requirement rather than a nicety: the corpora are modelled
    /// on the historical record, so a reproduced training name can be a real person's name. At
    /// order 3 on lists of this size, it happens often enough to matter without the check.
    /// </remarks>
    [Fact]
    public void GeneratedNamesAreNeverTrainingNames()
    {
        foreach (NameCorpus corpus in NameCorpus.LoadAll())
        {
            MarkovNameModel model = MarkovNameModel.Train(new[] { ((IReadOnlyList<string>)corpus.Given, 1) });
            IRng rng = new Pcg32(Hash.OfString(corpus.Family));

            for (int i = 0; i < 400; i++)
            {
                string generated = model.Generate(rng);
                Assert.False(
                    model.IsTrainingName(generated),
                    $"{corpus.Family} reproduced the training name '{generated}'");
            }
        }
    }

    [Fact]
    public void SameStreamProducesSameNames()
    {
        NameCorpus corpus = NameCorpus.Load("norse");
        MarkovNameModel model = MarkovNameModel.Train(new[] { ((IReadOnlyList<string>)corpus.Given, 1) });

        var first = new List<string>();
        var second = new List<string>();

        IRng a = new Pcg32(11);
        IRng b = new Pcg32(11);

        for (int i = 0; i < 50; i++)
        {
            first.Add(model.Generate(a));
            second.Add(model.Generate(b));
        }

        Assert.Equal(first, second);
    }

    /// <summary>
    /// Blend order must not change the model.
    /// </summary>
    /// <remarks>
    /// The property that makes blending safe. Transition tables are built from a dictionary, so
    /// without the sort at build time the sampling layout would depend on which corpus happened to
    /// be accumulated first — and the same culture would name itself differently depending on an
    /// implementation detail of the blend loop.
    /// </remarks>
    [Fact]
    public void BlendIsOrderIndependent()
    {
        NameCorpus norse = NameCorpus.Load("norse");
        NameCorpus latin = NameCorpus.Load("latin");

        MarkovNameModel forward = MarkovNameModel.Train(new[]
        {
            ((IReadOnlyList<string>)norse.Given, 5), ((IReadOnlyList<string>)latin.Given, 2),
        });

        MarkovNameModel reversed = MarkovNameModel.Train(new[]
        {
            ((IReadOnlyList<string>)latin.Given, 2), ((IReadOnlyList<string>)norse.Given, 5),
        });

        Assert.Equal(forward.ContextCount, reversed.ContextCount);

        IRng a = new Pcg32(7);
        IRng b = new Pcg32(7);

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(forward.Generate(a), reversed.Generate(b));
        }
    }

    [Fact]
    public void GeneratedNamesStayWithinReasonableLength()
    {
        foreach (NameCorpus corpus in NameCorpus.LoadAll())
        {
            MarkovNameModel model = MarkovNameModel.Train(new[] { ((IReadOnlyList<string>)corpus.Places, 1) });
            IRng rng = new Pcg32(3);

            for (int i = 0; i < 200; i++)
            {
                string name = model.Generate(rng);
                Assert.InRange(name.Length, 3, 18);
            }
        }
    }

    [Fact]
    public void EmptyTrainingDataIsRejected() =>
        Assert.Throws<ArgumentException>(
            () => MarkovNameModel.Train(new[] { ((IReadOnlyList<string>)Array.Empty<string>(), 1) }));
}

public sealed class NamingLanguageTests
{
    /// <summary>
    /// A shift must not fire on text already in its target form.
    /// </summary>
    /// <remarks>
    /// The concrete bug this guards: <c>s→sh</c> applied to a name already containing <c>sh</c>
    /// used to emit <c>shh</c>, producing "Vladishhovovo". Invisible in the rule table, and only
    /// discoverable by reading generated output.
    /// </remarks>
    [Fact]
    public void ShiftDoesNotApplyToItsOwnOutput()
    {
        var rules = new[] { new MutationRule("s", "sh") };

        Assert.Equal("shlav", NamingLanguage.ApplyShifts("slav", rules));
        Assert.Equal("shovo", NamingLanguage.ApplyShifts("shovo", rules));
        Assert.Equal("vladishovo", NamingLanguage.ApplyShifts("vladishovo", rules));
    }

    [Fact]
    public void ShiftsApplyInOneNonCascadingPass()
    {
        // th→v then v→w must not turn "th" into "w".
        var rules = new[] { new MutationRule("th", "v"), new MutationRule("v", "w") };

        Assert.Equal("vor", NamingLanguage.ApplyShifts("thor", rules));
        Assert.Equal("wik", NamingLanguage.ApplyShifts("vik", rules));
    }

    [Fact]
    public void SameSeedGivesSameLanguage()
    {
        NamingLanguage a = NamingLanguage.Derive(9876);
        NamingLanguage b = NamingLanguage.Derive(9876);

        Assert.Equal(
            a.Sources.Select(s => (s.Family, s.Weight)),
            b.Sources.Select(s => (s.Family, s.Weight)));
        Assert.Equal(a.Mutations, b.Mutations);

        IRng ra = new Pcg32(5);
        IRng rb = new Pcg32(5);

        for (int i = 0; i < 40; i++)
        {
            Assert.Equal(a.Person(ra), b.Person(rb));
        }
    }

    [Fact]
    public void BlendDrawsEachFamilyAtMostOnce()
    {
        for (ulong seed = 1; seed <= 200; seed++)
        {
            NamingLanguage language = NamingLanguage.Derive(seed);
            var families = language.Sources.Select(s => s.Family).ToArray();

            Assert.InRange(families.Length, 1, 3);
            Assert.Equal(families.Length, families.Distinct().Count());
            Assert.All(language.Sources, s => Assert.True(s.Weight > 0));
        }
    }

    /// <summary>
    /// A civilization's name has to stay readable — it is the most-repeated string in the chronicle.
    /// </summary>
    /// <remarks>
    /// Ethnonyms are built from place roots, which in these corpora are often already compounds.
    /// Before the root was capped, this produced names like "Lundfjordalilaiset".
    /// </remarks>
    [Fact]
    public void EthnonymsStayReadable()
    {
        for (ulong seed = 1; seed <= 60; seed++)
        {
            NamingLanguage language = NamingLanguage.Derive(seed);
            IRng rng = new Pcg32(seed);

            for (int i = 0; i < 20; i++)
            {
                string name = language.People(rng);
                Assert.InRange(name.Length, 4, 14);
            }
        }
    }

    [Fact]
    public void NamesAreCapitalisedAndAlphabetic()
    {
        NamingLanguage language = NamingLanguage.Derive(4242);
        IRng rng = new Pcg32(1);

        for (int i = 0; i < 100; i++)
        {
            foreach (string name in new[] { language.Person(rng), language.Place(rng), language.People(rng) })
            {
                Assert.NotEmpty(name);
                Assert.True(char.IsUpper(name[0]), $"'{name}' is not capitalised");
                Assert.All(name, c => Assert.True(char.IsLetter(c), $"'{name}' contains '{c}'"));
            }
        }
    }

    /// <summary>
    /// Names must be coherent within a culture and distinct across cultures.
    /// </summary>
    /// <remarks>
    /// <para>The whole point of the milestone, and the hardest thing to assert. The proxy: measure
    /// character-trigram overlap. A culture's own names should share substantially more trigrams
    /// with each other than with a differently-seeded culture's names, because they come from one
    /// blended model under one set of sound shifts.</para>
    ///
    /// <para>This is a statistical claim, so the threshold is loose — it is here to catch a
    /// regression that breaks coherence outright (a language that draws fresh corpora per name, or
    /// sound shifts that stop being applied consistently), not to police subtle quality.</para>
    /// </remarks>
    [Fact]
    public void NamesAreCoherentWithinACultureAndDistinctAcross()
    {
        NamingLanguage first = NamingLanguage.Derive(1001);
        NamingLanguage second = NamingLanguage.Derive(2002);

        List<string> a = Sample(first, 1);
        List<string> b = Sample(first, 2);
        List<string> other = Sample(second, 3);

        double within = TrigramOverlap(a, b);
        double across = TrigramOverlap(a, other);

        Assert.True(
            within > across,
            $"Within-culture trigram overlap {within:F3} should exceed cross-culture {across:F3}. " +
            "Either naming has stopped being culture-coherent, or two languages have collapsed " +
            "onto the same corpus blend.");
    }

    private static List<string> Sample(NamingLanguage language, ulong streamSeed)
    {
        IRng rng = new Pcg32(streamSeed);
        var names = new List<string>(60);

        for (int i = 0; i < 30; i++)
        {
            names.Add(language.Person(rng));
            names.Add(language.Place(rng));
        }

        return names;
    }

    private static double TrigramOverlap(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        HashSet<string> a = Trigrams(left);
        HashSet<string> b = Trigrams(right);

        if (a.Count == 0 || b.Count == 0) return 0.0;

        int shared = a.Count(b.Contains);
        return shared / (double)Math.Min(a.Count, b.Count);
    }

    private static HashSet<string> Trigrams(IReadOnlyList<string> names)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);

        foreach (string name in names)
        {
            string lower = name.ToLowerInvariant();
            for (int i = 0; i + 3 <= lower.Length; i++) set.Add(lower.Substring(i, 3));
        }

        return set;
    }
}

public sealed class MarkovNameGeneratorTests
{
    /// <summary>
    /// A name must depend on nothing but its own id.
    /// </summary>
    /// <remarks>
    /// The property that keeps names stable under unrelated change: requesting names in a
    /// different order, or requesting more of them, cannot alter any existing name. Without it,
    /// founding one extra settlement early in a run would rename everything after it.
    /// </remarks>
    [Fact]
    public void NamesDependOnlyOnEntityId()
    {
        var forward = new MarkovNameGenerator(31);
        var backward = new MarkovNameGenerator(31);

        Culture culture = MakeCulture(forward);

        var ids = Enumerable.Range(0, 40).Select(EntityId.Settlement).ToArray();

        var inOrder = ids.Select(id => forward.ForSettlement(id, culture)).ToArray();

        // Same ids, requested back to front.
        var reversed = ids.Reverse().Select(id => backward.ForSettlement(id, culture)).Reverse().ToArray();

        Assert.Equal(inOrder, reversed);
    }

    [Fact]
    public void RepeatedRequestsReturnTheSameName()
    {
        var generator = new MarkovNameGenerator(77);
        Culture culture = MakeCulture(generator);

        string first = generator.ForFigure(EntityId.Figure(12), culture);
        string again = generator.ForFigure(EntityId.Figure(12), culture);

        Assert.Equal(first, again);
    }

    [Fact]
    public void DifferentWorldSeedsGiveDifferentNames()
    {
        var a = new MarkovNameGenerator(1);
        var b = new MarkovNameGenerator(2);

        Assert.NotEqual(
            a.ForCulture(EntityId.Culture(0)),
            b.ForCulture(EntityId.Culture(0)));
    }

    [Fact]
    public void RegionsAreNamedInAWorldLanguageNotACultureOne()
    {
        var generator = new MarkovNameGenerator(555);

        // Regions are named before any culture exists, so this must not throw or depend on one.
        var names = Enumerable.Range(0, 50)
            .Select(i => generator.ForRegion(EntityId.Region(i), World.Biome.Grassland))
            .ToArray();

        Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name)));
        Assert.True(names.Distinct().Count() > 25, "Region names are barely varying.");
    }

    [Fact]
    public void WorldBodyNamesDependOnlyOnTheSeedAndRole()
    {
        var forward = new MarkovNameGenerator(31);
        var backward = new MarkovNameGenerator(31);

        string body = forward.ForWorld(WorldNameRole.Body);
        string parent = backward.ForWorld(WorldNameRole.Parent);
        string bodyAgain = backward.ForWorld(WorldNameRole.Body);
        string parentAgain = forward.ForWorld(WorldNameRole.Parent);

        Assert.Equal(body, bodyAgain);
        Assert.Equal(parent, parentAgain);
        Assert.NotEqual(body, parent);
    }

    private static Culture MakeCulture(MarkovNameGenerator generator)
    {
        EntityId id = EntityId.Culture(0);
        return new Culture(
            id,
            generator.ForCulture(id),
            generator.LanguageSeedFor(id),
            CultureValues.Roll(new Pcg32(1)),
            GovernmentForm.Monarchy);
    }
}

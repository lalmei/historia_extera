using HistoryEngine.Core;

namespace HistoryEngine.Naming;

/// <summary>A phoneme substitution applied after generation.</summary>
public readonly record struct MutationRule(string From, string To)
{
    public override string ToString() => $"{From}→{To}";
}

/// <summary>
/// One culture's invented language: a corpus blend, a set of sound shifts, and the
/// morphology to build names of every kind the chronicle needs.
/// </summary>
/// <remarks>
/// <para><b>The problem this solves.</b> Training a Markov model on a single corpus gives you a
/// culture that reads as Norse, or Latin, or Turkic. That is worse than it sounds: eight
/// civilizations then read as a tour of real-world history rather than as an invented world, and
/// you are capped at one culture per corpus.</para>
///
/// <para><b>Two mechanisms fix it.</b> Blending one to three corpora by weight produces an
/// intermediate phonology — see <see cref="MarkovNameModel.Train"/> for why blending counts
/// rather than outputs matters. Then a small set of <see cref="MutationRule"/> sound shifts is
/// applied to every generated name, which pushes the result off any real language while staying
/// internally consistent, because the same shifts apply to every name the culture ever produces.
/// A culture that turns every <c>th</c> into <c>v</c> does so for its kings, its cities and its
/// dynasties alike, and that consistency is what reads as a language.</para>
///
/// <para><b>Everything derives from <see cref="Culture.LanguageSeed"/>.</b> The blend, the
/// weights, the mutations and the morphology are all forked from it, so a culture's language is
/// fixed the moment it is founded and identical on every run.</para>
/// </remarks>
public sealed class NamingLanguage
{
    /// <summary>
    /// Candidate sound shifts.
    /// </summary>
    /// <remarks>
    /// Curated rather than generated so the results stay pronounceable. An unconstrained
    /// substitution table happily produces <c>tzq</c> from <c>a</c>; every pair here maps a sound
    /// to a plausible neighbour, mostly along the lines real sound changes actually travel —
    /// fricative to stop, voiced to unvoiced, vowel to adjacent vowel.
    /// </remarks>
    private static readonly MutationRule[] MutationPool =
    {
        new("th", "v"), new("th", "d"), new("th", "s"),
        new("kh", "k"), new("k", "kh"), new("k", "g"),
        new("ph", "f"), new("f", "v"), new("v", "w"),
        new("sh", "s"), new("s", "sh"), new("z", "s"),
        new("gh", "g"), new("g", "j"), new("j", "y"),
        new("b", "p"), new("p", "b"), new("d", "t"),
        new("ll", "l"), new("nn", "n"), new("rr", "r"),
        new("ae", "e"), new("oe", "o"), new("ou", "u"),
        new("ei", "i"), new("au", "o"), new("ia", "ya"),
        new("os", "as"), new("us", "os"), new("an", "en"),
    };

    /// <summary>Longest root a place suffix may attach to.</summary>
    private const int MaxRootLength = 9;

    /// <summary>
    /// Longest root an ethnonym suffix may attach to.
    /// </summary>
    /// <remarks>
    /// Tighter than <see cref="MaxRootLength"/> because a civilization's name is the most-repeated
    /// string in the chronicle, so it has to stay readable at a glance.
    /// </remarks>
    private const int MaxEthnonymRootLength = 7;

    private readonly MarkovNameModel _people;
    private readonly MarkovNameModel _places;
    private readonly IReadOnlyList<string> _placeSuffixes;
    private readonly IReadOnlyList<string> _peopleSuffixes;
    private readonly double _placeSuffixChance;

    private NamingLanguage(
        ulong seed,
        IReadOnlyList<CorpusWeight> sources,
        IReadOnlyList<MutationRule> mutations,
        MarkovNameModel people,
        MarkovNameModel places,
        IReadOnlyList<string> placeSuffixes,
        IReadOnlyList<string> peopleSuffixes,
        double placeSuffixChance)
    {
        Seed = seed;
        Sources = sources;
        Mutations = mutations;
        _people = people;
        _places = places;
        _placeSuffixes = placeSuffixes;
        _peopleSuffixes = peopleSuffixes;
        _placeSuffixChance = placeSuffixChance;
    }

    public ulong Seed { get; }

    /// <summary>The corpus blend behind this language. Exported so the viewer can show it.</summary>
    public IReadOnlyList<CorpusWeight> Sources { get; }

    /// <summary>The sound shifts applied to every generated name.</summary>
    public IReadOnlyList<MutationRule> Mutations { get; }

    /// <summary>Which corpus families feed this language, and how strongly.</summary>
    public readonly record struct CorpusWeight(string Family, int Weight);

    /// <summary>
    /// Derives a culture's language from its language seed.
    /// </summary>
    public static NamingLanguage Derive(ulong languageSeed)
    {
        // Forked by purpose so that changing how mutations are chosen cannot shift the corpus
        // blend, and vice versa.
        IRng blendRng = new Pcg32(Hash.Combine(languageSeed, Hash.OfString("lang.blend")));
        IRng mutationRng = new Pcg32(Hash.Combine(languageSeed, Hash.OfString("lang.mutation")));
        IRng shapeRng = new Pcg32(Hash.Combine(languageSeed, Hash.OfString("lang.shape")));

        IReadOnlyList<CorpusWeight> sources = ChooseBlend(blendRng);
        IReadOnlyList<MutationRule> mutations = ChooseMutations(mutationRng);

        var givenSources = new List<(IReadOnlyList<string>, int)>(sources.Count);
        var placeSources = new List<(IReadOnlyList<string>, int)>(sources.Count);
        var placeSuffixes = new List<string>();
        var peopleSuffixes = new List<string>();

        foreach (CorpusWeight source in sources)
        {
            NameCorpus corpus = NameCorpus.Load(source.Family);
            givenSources.Add((corpus.Given, source.Weight));
            placeSources.Add((corpus.Places, source.Weight));

            // Suffixes are pooled rather than blended, and mutated like everything else, so a
            // culture's places end in shapes drawn from its own sources only.
            foreach (string suffix in corpus.PlaceSuffixes) placeSuffixes.Add(ApplyShifts(suffix, mutations));
            foreach (string suffix in corpus.PeopleSuffixes) peopleSuffixes.Add(ApplyShifts(suffix, mutations));
        }

        return new NamingLanguage(
            languageSeed,
            sources,
            mutations,
            MarkovNameModel.Train(givenSources),
            MarkovNameModel.Train(placeSources),
            placeSuffixes,
            peopleSuffixes,
            // How readily this culture compounds its place names. Some cultures name towns with
            // bare roots, others suffix nearly everything.
            placeSuffixChance: shapeRng.NextDouble(0.15, 0.7));
    }

    /// <summary>
    /// Picks one to three corpora with weights.
    /// </summary>
    /// <remarks>
    /// Weighted toward two-corpus blends. A single corpus stays recognisably one real tradition;
    /// three tends to average out into something bland. Two gives the most distinctive results —
    /// clearly coherent, clearly not any real language.
    /// </remarks>
    private static IReadOnlyList<CorpusWeight> ChooseBlend(IRng rng)
    {
        int count = rng.NextInt(100) switch
        {
            < 20 => 1,
            < 75 => 2,
            _ => 3,
        };

        // Sample without replacement from a shuffled copy, so a family cannot appear twice.
        var pool = new List<string>(NameCorpus.FamilyNames);
        var chosen = new List<CorpusWeight>(count);

        for (int i = 0; i < count && pool.Count > 0; i++)
        {
            int index = rng.NextInt(pool.Count);
            string family = pool[index];
            pool.RemoveAt(index);

            // The first pick dominates, so a blend has a clear primary character rather than
            // being a uniform mush of its inputs.
            int weight = i == 0 ? rng.NextInt(5, 9) : rng.NextInt(1, 4);
            chosen.Add(new CorpusWeight(family, weight));
        }

        return chosen;
    }

    private static IReadOnlyList<MutationRule> ChooseMutations(IRng rng)
    {
        int count = rng.NextInt(1, 4);
        var chosen = new List<MutationRule>(count);

        var pool = new List<MutationRule>(MutationPool);

        for (int i = 0; i < count && pool.Count > 0; i++)
        {
            int index = rng.NextInt(pool.Count);
            MutationRule rule = pool[index];
            pool.RemoveAt(index);

            // Drop any rule whose output another chosen rule consumes. Chained shifts compound
            // into unrecognisable output and, worse, make the result depend on rule order.
            bool conflicts = false;
            foreach (MutationRule existing in chosen)
            {
                if (existing.From == rule.To || existing.To == rule.From)
                {
                    conflicts = true;
                    break;
                }
            }

            if (!conflicts) chosen.Add(rule);
        }

        return chosen;
    }

    /// <summary>A personal name.</summary>
    public string Person(IRng rng) => Finish(_people.Generate(rng));

    /// <summary>
    /// A settlement or geographic name, sometimes compounded with a suffix.
    /// </summary>
    public string Place(IRng rng)
    {
        string root = _places.Generate(rng);

        if (_placeSuffixes.Count > 0 && rng.Chance(_placeSuffixChance))
        {
            root = Join(Shorten(root, MaxRootLength), rng.Pick(_placeSuffixes));
        }

        return Finish(root);
    }

    /// <summary>
    /// A people's name, for civilizations — an ethnonym built from a root plus a suffix.
    /// </summary>
    /// <remarks>
    /// Civilizations are named after their people rather than given a place name or an English
    /// construction like "the Vethric Empire", which would undercut the invented-language effect
    /// at exactly the most visible point in the chronicle.
    /// </remarks>
    public string People(IRng rng)
    {
        string root = Shorten(_places.Generate(rng), MaxEthnonymRootLength);

        if (_peopleSuffixes.Count > 0)
        {
            root = Join(root, rng.Pick(_peopleSuffixes));
        }

        return Finish(root);
    }

    /// <summary>A dynasty name, built from a personal root.</summary>
    public string Dynasty(IRng rng) => Finish(_people.Generate(rng));

    /// <summary>Applies this language's sound shifts and normalises capitalisation.</summary>
    private string Finish(string raw)
    {
        string mutated = ApplyShifts(raw, Mutations);
        return Capitalise(mutated);
    }

    /// <summary>
    /// Applies every rule in one left-to-right pass. Public so the guard against
    /// self-application can be tested directly.
    /// </summary>
    /// <remarks>
    /// One pass, and a rule never re-examines text it has already written, so shifts cannot
    /// cascade — <c>th→v</c> followed by <c>v→w</c> would otherwise turn <c>th</c> into <c>w</c>
    /// and make the result depend on rule ordering. <see cref="ChooseMutations"/> also filters
    /// conflicting pairs, so this is belt and braces on a property worth being sure of.
    /// </remarks>
    public static string ApplyShifts(string text, IReadOnlyList<MutationRule> rules)
    {
        if (rules.Count == 0 || text.Length == 0) return text;

        var result = new System.Text.StringBuilder(text.Length + 4);

        int i = 0;
        while (i < text.Length)
        {
            bool matched = false;

            foreach (MutationRule rule in rules)
            {
                if (rule.From.Length == 0 || i + rule.From.Length > text.Length) continue;

                // Skip when the text here is already the rule's output. Without this, a shift
                // whose target contains its source — s→sh is the obvious one — fires on text that
                // already reads "sh" and emits "shh". The failure is invisible in the rule table
                // and only shows up in generated names.
                if (Matches(text, i, rule.To)) continue;

                if (string.Compare(
                        text, i, rule.From, 0, rule.From.Length,
                        StringComparison.OrdinalIgnoreCase) == 0)
                {
                    result.Append(rule.To);
                    i += rule.From.Length;
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                result.Append(text[i]);
                i++;
            }
        }

        return result.ToString();
    }

    private static bool Matches(string text, int at, string candidate) =>
        candidate.Length > 0
        && at + candidate.Length <= text.Length
        && string.Compare(text, at, candidate, 0, candidate.Length,
            StringComparison.OrdinalIgnoreCase) == 0;

    /// <summary>
    /// Trims a root to a length that leaves room for a suffix, cutting at a vowel.
    /// </summary>
    /// <remarks>
    /// Ethnonyms are built from place roots, and place roots from these corpora are often already
    /// compounds. Suffixing an untrimmed root produces civilization names like
    /// <c>Lundfjordalilaiset</c> — eighteen characters, and the most prominent name in the whole
    /// chronicle. Cutting back to a vowel keeps the shortened root pronounceable rather than
    /// ending it mid-cluster.
    /// </remarks>
    private static string Shorten(string root, int max)
    {
        if (root.Length <= max) return root;

        for (int cut = max; cut >= 3; cut--)
        {
            if (IsVowel(root[cut - 1])) return root.Substring(0, cut);
        }

        return root.Substring(0, max);
    }

    /// <summary>Drops a repeated letter across a root/suffix seam, which reads as a typo.</summary>
    private static string Join(string root, string suffix)
    {
        if (root.Length == 0 || suffix.Length == 0) return root + suffix;

        // Trailing vowel meeting a leading vowel is a hiatus; a repeated letter is a stutter.
        // Both are fixed by dropping the root's last character.
        bool repeated = char.ToLowerInvariant(root[root.Length - 1]) ==
                        char.ToLowerInvariant(suffix[0]);
        bool hiatus = IsVowel(root[root.Length - 1]) && IsVowel(suffix[0]);

        if ((repeated || hiatus) && root.Length > 2)
        {
            root = root.Substring(0, root.Length - 1);
        }

        // A three-consonant pile-up at the seam is unpronounceable; insert a linking vowel.
        if (root.Length >= 2 && !IsVowel(root[root.Length - 1])
            && !IsVowel(root[root.Length - 2]) && !IsVowel(suffix[0]))
        {
            root += "a";
        }

        return root + suffix;
    }

    private static string Capitalise(string text)
    {
        if (text.Length == 0) return text;

        // Invariant casing: ToUpper() without a culture would apply Turkish dotted-I rules on a
        // Turkish-locale machine and change the exported bytes.
        char first = char.ToUpperInvariant(text[0]);
        return text.Length == 1
            ? first.ToString()
            : first + text.Substring(1).ToLowerInvariant();
    }

    private static bool IsVowel(char c) =>
        char.ToLowerInvariant(c) is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';
}

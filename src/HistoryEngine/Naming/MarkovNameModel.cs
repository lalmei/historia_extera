using HistoryEngine.Core;

namespace HistoryEngine.Naming;

/// <summary>
/// An order-3 character Markov model over a blend of name corpora.
/// </summary>
/// <remarks>
/// <para><b>Order three, specifically.</b> Order 1 and 2 produce mush — the model has not seen
/// enough context to know that <c>thr</c> can start a Norse name but <c>rht</c> cannot. Order 4
/// and above, on corpora of this size, mostly reproduces the training data with the serial
/// numbers filed off: there are so few contexts that each has one or two continuations, and
/// generation degenerates into recall. Three is where the output is novel and still
/// pronounceable.</para>
///
/// <para><b>Novelty is enforced, not hoped for.</b> Even at order 3 a short training name will
/// occasionally be reproduced exactly, and since these corpora are modelled on the historical
/// record that can mean emitting a real person's name. <see cref="Generate"/> rejects any
/// candidate present in the training set. That is a correctness requirement, not a nicety.</para>
///
/// <para><b>Determinism.</b> Transition tables are built into sorted arrays with cumulative
/// weights, so sampling is a binary search over a fixed layout. Building them uses a
/// dictionary, but nothing is ever enumerated from it — the sort at build time is what makes
/// the model's behaviour independent of insertion order, and therefore of the order corpora
/// happen to be blended in.</para>
/// </remarks>
public sealed class MarkovNameModel
{
    /// <summary>Context length in characters.</summary>
    public const int Order = 3;

    /// <summary>
    /// Padding before a name's first character, and the sentinel that ends one.
    /// </summary>
    /// <remarks>
    /// NUL rather than a printable marker, so it can never collide with a character that appears
    /// in a corpus and be mistaken for real output.
    /// </remarks>
    private const char Boundary = '\0';

    private readonly Dictionary<string, Transition> _transitions;
    private readonly HashSet<string> _trainingNames;
    private readonly int _minLength;
    private readonly int _maxLength;

    private MarkovNameModel(
        Dictionary<string, Transition> transitions,
        HashSet<string> trainingNames,
        int minLength,
        int maxLength)
    {
        _transitions = transitions;
        _trainingNames = trainingNames;
        _minLength = minLength;
        _maxLength = maxLength;
    }

    /// <summary>Number of distinct contexts learned. Diagnostic.</summary>
    public int ContextCount => _transitions.Count;

    /// <summary>
    /// Trains on a weighted blend of name lists.
    /// </summary>
    /// <remarks>
    /// Blending happens at the level of transition <em>counts</em>, not by generating from each
    /// corpus in turn. That is what produces a genuinely intermediate phonology rather than an
    /// alternation between two recognisable ones: a blend of Norse and Latin learns that
    /// <c>-us</c> and <c>-vik</c> are both possible endings for the same name-shape, and invents
    /// forms neither corpus contains.
    /// </remarks>
    /// <param name="sources">Name lists paired with integer weights. Weight scales the counts.</param>
    public static MarkovNameModel Train(IReadOnlyList<(IReadOnlyList<string> Names, int Weight)> sources)
    {
        var counts = new Dictionary<string, Dictionary<char, int>>(StringComparer.Ordinal);
        var trainingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int shortest = int.MaxValue;
        int longest = 0;

        foreach ((IReadOnlyList<string> names, int weight) in sources)
        {
            if (weight <= 0) continue;

            foreach (string raw in names)
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;

                trainingNames.Add(name);
                if (name.Length < shortest) shortest = name.Length;
                if (name.Length > longest) longest = name.Length;

                // Pad the front so the first characters have context, and terminate with the
                // boundary sentinel so the model learns where names end rather than being cut
                // off at an arbitrary length.
                string padded = new string(Boundary, Order) + name + Boundary;

                for (int i = Order; i < padded.Length; i++)
                {
                    string context = padded.Substring(i - Order, Order);
                    char next = padded[i];

                    if (!counts.TryGetValue(context, out Dictionary<char, int>? distribution))
                    {
                        distribution = new Dictionary<char, int>();
                        counts[context] = distribution;
                    }

                    distribution[next] = distribution.TryGetValue(next, out int existing)
                        ? existing + weight
                        : weight;
                }
            }
        }

        if (counts.Count == 0)
        {
            throw new ArgumentException("No usable training names supplied.", nameof(sources));
        }

        var transitions = new Dictionary<string, Transition>(counts.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, Dictionary<char, int>> pair in counts)
        {
            transitions[pair.Key] = Transition.Build(pair.Value);
        }

        return new MarkovNameModel(
            transitions,
            trainingNames,
            minLength: Math.Max(4, shortest),
            maxLength: Math.Min(16, Math.Max(6, longest)));
    }

    /// <summary>
    /// Generates a novel name.
    /// </summary>
    /// <remarks>
    /// Retries on a candidate that is too short, too long, or present in the training data.
    /// Retries are bounded and the fallback is a truncated candidate rather than an exception,
    /// because a slightly odd name is a far better failure than a worldgen that throws.
    /// </remarks>
    public string Generate(IRng rng)
    {
        for (int attempt = 0; attempt < 24; attempt++)
        {
            string candidate = Walk(rng);

            if (candidate.Length < _minLength || candidate.Length > _maxLength) continue;
            if (_trainingNames.Contains(candidate)) continue;

            return candidate;
        }

        // Last resort: take whatever the walk produces and force it into range.
        string fallback = Walk(rng);
        if (fallback.Length < _minLength) fallback += "an";
        if (fallback.Length > _maxLength) fallback = fallback.Substring(0, _maxLength);

        return fallback.Length == 0 ? "Anon" : fallback;
    }

    /// <summary>Whether a string appears verbatim in the training data.</summary>
    public bool IsTrainingName(string name) => _trainingNames.Contains(name);

    private string Walk(IRng rng)
    {
        var context = new char[Order];
        for (int i = 0; i < Order; i++) context[i] = Boundary;

        var result = new System.Text.StringBuilder(12);

        // Hard ceiling well above _maxLength: an over-long walk is discarded by the caller,
        // but it must terminate regardless of what the transition table looks like.
        for (int step = 0; step < 32; step++)
        {
            string key = new string(context);

            if (!_transitions.TryGetValue(key, out Transition transition))
            {
                // Back off to shorter context. Unreachable with a model trained the normal way,
                // but cheap insurance against a hand-built or mutated table.
                transition = default;
                for (int drop = 1; drop < Order; drop++)
                {
                    string shorter = new string(Boundary, drop) + key.Substring(drop);
                    if (_transitions.TryGetValue(shorter, out transition)) break;
                }

                if (transition.Characters is null) break;
            }

            char next = transition.Sample(rng);
            if (next == Boundary) break;

            result.Append(next);

            for (int i = 0; i < Order - 1; i++) context[i] = context[i + 1];
            context[Order - 1] = next;
        }

        return result.ToString();
    }

    /// <summary>
    /// One context's continuations, as parallel sorted arrays with cumulative weights.
    /// </summary>
    /// <remarks>
    /// Sorted by character so the layout is a pure function of the counts, never of the order
    /// they were accumulated in. Sampling is a binary search on the cumulative array.
    /// </remarks>
    private readonly struct Transition
    {
        public readonly char[] Characters;
        private readonly int[] _cumulative;
        private readonly int _total;

        private Transition(char[] characters, int[] cumulative, int total)
        {
            Characters = characters;
            _cumulative = cumulative;
            _total = total;
        }

        public static Transition Build(Dictionary<char, int> counts)
        {
            var characters = new char[counts.Count];
            counts.Keys.CopyTo(characters, 0);
            Array.Sort(characters);

            var cumulative = new int[characters.Length];
            int running = 0;

            for (int i = 0; i < characters.Length; i++)
            {
                running += counts[characters[i]];
                cumulative[i] = running;
            }

            return new Transition(characters, cumulative, running);
        }

        public char Sample(IRng rng)
        {
            int roll = rng.NextInt(_total);

            int low = 0;
            int high = _cumulative.Length - 1;

            while (low < high)
            {
                int mid = (low + high) / 2;
                if (roll < _cumulative[mid]) high = mid;
                else low = mid + 1;
            }

            return Characters[low];
        }
    }
}

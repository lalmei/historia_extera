using System.Collections;

namespace HistoryEngine.Core;

/// <summary>
/// A small key/value map whose enumeration order is always sorted by key.
/// </summary>
/// <remarks>
/// <para><b>Why this type exists.</b> <see cref="Dictionary{TKey,TValue}"/> enumerates in
/// an order that depends on insertion history, capacity and collisions. It is usually
/// stable within a single process, which is what makes it dangerous: a simulation
/// built on <c>foreach (var kv in dictionary)</c> passes its determinism tests for
/// months and then reorders when an unrelated change alters an insertion sequence.</para>
///
/// <para>Reaching for <c>.OrderBy(kv =&gt; kv.Key)</c> at each use site is not a fix
/// either. It is easy to forget, and LINQ's sort is only a partial order unless the
/// key comparison is total — so equal keys stay in dictionary order. Making the
/// ordered type the default one available removes the choice.</para>
///
/// <para>Backed by a sorted array with binary search, which is intended for the small
/// maps the simulation actually needs: per-civilization diplomatic relations, per-title
/// holders, event indices. Do not use it for tens of thousands of entries.</para>
/// </remarks>
public sealed class DetMap<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
    where TKey : IComparable<TKey>
{
    /// <summary>
    /// Ordinal for string keys, <see cref="Comparer{T}.Default"/> otherwise.
    /// </summary>
    /// <remarks>
    /// <see cref="string.CompareTo(string)"/> is <em>culture-sensitive</em>, so
    /// <c>Comparer&lt;string&gt;.Default</c> orders keys differently under different
    /// locales. A sorted map keyed by string would therefore enumerate in a
    /// machine-dependent order — the exact failure this type exists to prevent, arriving
    /// through the back door. Only a machine with a non-invariant culture would show it,
    /// which is to say: not this one, and not CI, but somebody's.
    /// </remarks>
    private static readonly IComparer<TKey> DefaultComparer =
        typeof(TKey) == typeof(string)
            ? (IComparer<TKey>)StringComparer.Ordinal
            : Comparer<TKey>.Default;

    private readonly IComparer<TKey> _comparer;
    private readonly List<TKey> _keys;
    private readonly List<TValue> _values;

    public DetMap()
        : this(DefaultComparer)
    {
    }

    public DetMap(IComparer<TKey> comparer)
    {
        _comparer = comparer;
        _keys = new List<TKey>();
        _values = new List<TValue>();
    }

    public DetMap(DetMap<TKey, TValue> source)
    {
        _comparer = source._comparer;
        _keys = new List<TKey>(source._keys);
        _values = new List<TValue>(source._values);
    }

    public int Count => _keys.Count;

    public IReadOnlyList<TKey> Keys => _keys;

    public IReadOnlyList<TValue> Values => _values;

    public TValue this[TKey key]
    {
        get => TryGetValue(key, out TValue? value)
            ? value!
            : throw new KeyNotFoundException($"Key '{key}' is not present.");
        set
        {
            int i = _keys.BinarySearch(key, _comparer);
            if (i >= 0)
            {
                _values[i] = value;
            }
            else
            {
                int insert = ~i;
                _keys.Insert(insert, key);
                _values.Insert(insert, value);
            }
        }
    }

    public bool ContainsKey(TKey key) => _keys.BinarySearch(key, _comparer) >= 0;

    public bool TryGetValue(TKey key, out TValue? value)
    {
        int i = _keys.BinarySearch(key, _comparer);
        if (i >= 0)
        {
            value = _values[i];
            return true;
        }

        value = default;
        return false;
    }

    public TValue GetOrDefault(TKey key, TValue fallback) =>
        TryGetValue(key, out TValue? value) ? value! : fallback;

    /// <summary>Adds <paramref name="delta"/> to the value at <paramref name="key"/>, treating a missing key as zero.</summary>
    public void Add(TKey key, double delta, Func<TValue, double, TValue> combine, TValue zero)
    {
        TValue current = GetOrDefault(key, zero);
        this[key] = combine(current, delta);
    }

    public bool Remove(TKey key)
    {
        int i = _keys.BinarySearch(key, _comparer);
        if (i < 0) return false;

        _keys.RemoveAt(i);
        _values.RemoveAt(i);
        return true;
    }

    public void Clear()
    {
        _keys.Clear();
        _values.Clear();
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        for (int i = 0; i < _keys.Count; i++)
        {
            yield return new KeyValuePair<TKey, TValue>(_keys[i], _values[i]);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Structural equality over keys and values in order.
    /// </summary>
    /// <remarks>
    /// Necessary because this type is carried inside <c>HistoryEvent</c>, which is a record and so
    /// advertises value equality. Without this, a reference comparison would make two events with
    /// identical content compare unequal — which is silently wrong in exactly the places it matters
    /// most: deduplication, and any test asserting that two runs produced the same history.
    /// </remarks>
    public bool Equals(DetMap<TKey, TValue>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_keys.Count != other._keys.Count) return false;

        for (int i = 0; i < _keys.Count; i++)
        {
            if (!EqualityComparer<TKey>.Default.Equals(_keys[i], other._keys[i])) return false;
            if (!EqualityComparer<TValue>.Default.Equals(_values[i], other._values[i])) return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as DetMap<TKey, TValue>);

    /// <summary>
    /// For hash-based containers only.
    /// </summary>
    /// <remarks>
    /// Built from the runtime's own element hash codes, which for strings are randomised per
    /// process. That is fine here and nowhere else: this value must never reach a simulation
    /// decision or the export. Use <see cref="Hash"/> for anything that has to be reproducible.
    /// </remarks>
    public override int GetHashCode() // det:ok — never feeds simulation output
    {
        var hash = new HashCode();
        hash.Add(_keys.Count);

        for (int i = 0; i < _keys.Count; i++)
        {
            hash.Add(_keys[i]);
            hash.Add(_values[i]);
        }

        return hash.ToHashCode();
    }
}

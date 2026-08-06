using System.Collections;

namespace HistoryEngine.Core;

/// <summary>
/// The store for one kind of entity: a dense list where an entity's
/// <see cref="EntityId.Index"/> is its position.
/// </summary>
/// <remarks>
/// Dense indexing makes lookup a bounds-checked array read, and makes iteration order
/// identical to id order for free — no sorting, no comparer, nothing to forget. It is
/// also why ids are never recycled: an entity that leaves history (a ruler dies, a
/// settlement is abandoned) keeps its slot and is marked inactive, so every event that
/// referenced it still resolves. A chronicle whose references decay is not a chronicle.
/// </remarks>
public sealed class EntityTable<T> : IReadOnlyList<T>
    where T : class
{
    private readonly List<T> _items = new();

    public EntityTable(EntityKind kind) => Kind = kind;

    public EntityKind Kind { get; }

    public int Count => _items.Count;

    public T this[int index] => _items[index];

    public T this[EntityId id]
    {
        get
        {
            if (id.Kind != Kind)
            {
                throw new ArgumentException(
                    $"Expected a {Kind} id but got '{id}'.", nameof(id));
            }

            return _items[id.Index];
        }
    }

    /// <summary>Appends <paramref name="item"/> and returns the id it was assigned.</summary>
    public EntityId Add(T item)
    {
        var id = new EntityId(Kind, _items.Count);
        _items.Add(item);
        return id;
    }

    /// <summary>The id the next <see cref="Add"/> will assign. Useful when an entity needs to know its own id.</summary>
    public EntityId NextId => new(Kind, _items.Count);

    public bool Contains(EntityId id) =>
        id.Kind == Kind && id.Index >= 0 && id.Index < _items.Count;

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

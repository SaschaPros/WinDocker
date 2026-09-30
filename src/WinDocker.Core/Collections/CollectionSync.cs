using System.Collections.ObjectModel;

namespace WinDocker.Core.Collections;

/// <summary>Brings an observable collection in line with a fresh snapshot without resetting it.</summary>
public static class CollectionSync
{
    /// <summary>
    /// Makes <paramref name="target"/> match <paramref name="source"/> in place: items whose key is gone are removed,
    /// the remaining ones are updated, new ones are inserted at their position in the source, and items are moved only
    /// when their relative order changed. The collection is never cleared, so the item instances survive (a list view
    /// keeps its selection) and observers see the smallest possible set of changes.
    /// When <paramref name="source"/> repeats a key, the first entry wins.
    /// </summary>
    public static void Sync<TItem, TSource, TKey>(
        ObservableCollection<TItem> target,
        IReadOnlyList<TSource> source,
        Func<TSource, TKey> sourceKey,
        Func<TItem, TKey> itemKey,
        Func<TSource, TItem> create,
        Action<TItem, TSource> update)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceKey);
        ArgumentNullException.ThrowIfNull(itemKey);
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(update);

        var keys = new HashSet<TKey>();
        var wanted = new List<(TKey Key, TSource Entry)>(source.Count);
        foreach (var entry in source)
        {
            var key = sourceKey(entry);
            if (keys.Add(key))
            {
                wanted.Add((key, entry));
            }
        }

        for (var index = target.Count - 1; index >= 0; index--)
        {
            if (!keys.Contains(itemKey(target[index])))
            {
                target.RemoveAt(index);
            }
        }

        // Invariant: target[..index] already equals wanted[..index].
        for (var index = 0; index < wanted.Count; index++)
        {
            var (key, entry) = wanted[index];
            var current = IndexOf(target, itemKey, key, index);
            if (current < 0)
            {
                target.Insert(index, create(entry));
                continue;
            }

            if (current != index)
            {
                target.Move(current, index);
            }

            update(target[index], entry);
        }

        // Left over: items that repeat the key of an earlier one.
        while (target.Count > wanted.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    private static int IndexOf<TItem, TKey>(ObservableCollection<TItem> target, Func<TItem, TKey> itemKey, TKey key, int start)
        where TKey : notnull
    {
        var comparer = EqualityComparer<TKey>.Default;
        for (var index = start; index < target.Count; index++)
        {
            if (comparer.Equals(itemKey(target[index]), key))
            {
                return index;
            }
        }

        return -1;
    }
}

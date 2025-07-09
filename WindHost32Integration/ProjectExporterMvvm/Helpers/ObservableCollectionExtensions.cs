using System.Collections.ObjectModel;

namespace ProjectExporterMvvm.Helpers;

public static class ObservableCollectionExtensions
{
    /// <summary>
    /// Updates collection efficiently by adding/removing items to match target
    /// </summary>
    public static void UpdateWith<T>(this ObservableCollection<T> collection, IEnumerable<T> newItems)
    {
        if (newItems == null)
        {
            collection.Clear();
            return;
        }

        var newList = newItems.ToList();

        // Remove items not in new list
        for (int i = collection.Count - 1; i >= 0; i--)
        {
            if (!newList.Contains(collection[i]))
            {
                collection.RemoveAt(i);
            }
        }

        // Add new items
        foreach (var item in newList)
        {
            if (!collection.Contains(item))
            {
                collection.Add(item);
            }
        }
    }

    /// <summary>
    /// Adds range of items efficiently
    /// </summary>
    public static void AddRange<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        if (items == null) return;

        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    /// <summary>
    /// Sorts collection in place
    /// </summary>
    public static void Sort<T>(this ObservableCollection<T> collection, Comparison<T> comparison)
    {
        var sorted = collection.OrderBy(x => x, Comparer<T>.Create(comparison)).ToList();

        for (int i = 0; i < sorted.Count; i++)
        {
            var currentIndex = collection.IndexOf(sorted[i]);
            if (currentIndex != i)
            {
                collection.Move(currentIndex, i);
            }
        }
    }
}
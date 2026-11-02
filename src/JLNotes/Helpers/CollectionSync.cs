using System.Collections.ObjectModel;

namespace JLNotes.Helpers;

public static class CollectionSync
{
    /// <summary>
    /// Makes a bound list match <paramref name="wanted"/> with the fewest changes.
    /// Clearing and refilling would make a ComboBox drop its selection and rebuild
    /// every row, which loses an open editor. Items must be distinct.
    /// </summary>
    public static void SyncTo<T>(this ObservableCollection<T> list, IReadOnlyList<T> wanted) where T : notnull
    {
        var keep = new HashSet<T>(wanted);
        for (var i = list.Count - 1; i >= 0; i--)
            if (!keep.Contains(list[i]))
                list.RemoveAt(i);

        for (var i = 0; i < wanted.Count; i++)
        {
            if (i < list.Count && EqualityComparer<T>.Default.Equals(list[i], wanted[i]))
                continue;
            var from = list.IndexOf(wanted[i]);
            if (from >= 0)
                list.Move(from, i);
            else
                list.Insert(i, wanted[i]);
        }
    }
}

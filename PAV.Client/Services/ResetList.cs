using System.Collections.Specialized;

namespace PAV.Client.Services;

/// <summary>
/// ObservableCollection that can replace its contents with a single Reset event.
/// Per-item Add notifies the DataGrid once per row and is the main UI stall on load.
/// </summary>
public class ResetList<T> : ObservableCollection<T>
{
    public void ReplaceAll(IReadOnlyList<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void ReplaceOrAdd(T item, Func<T, bool> match)
    {
        for (var i = 0; i < Count; i++)
        {
            if (match(this[i]))
            {
                SetItem(i, item);
                return;
            }
        }
        Add(item);
    }

    public bool RemoveWhere(Func<T, bool> match)
    {
        for (var i = 0; i < Count; i++)
        {
            if (!match(this[i])) continue;
            RemoveAt(i);
            return true;
        }
        return false;
    }
}

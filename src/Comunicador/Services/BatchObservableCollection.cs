using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Comunicador.Services;

internal sealed class BatchObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceWith(IEnumerable<T> items)
    {
        var replacement = items as IReadOnlyCollection<T> ?? items.ToArray();
        CheckReentrancy();
        Items.Clear();
        foreach (var item in replacement) Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

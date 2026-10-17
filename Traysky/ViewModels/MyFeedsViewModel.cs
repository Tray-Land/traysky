using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.ViewModels;

/// <summary>
/// The account's saved feeds: pin or unpin, move the pinned ones up and down, or drop one. Each
/// action is stored before the list is redrawn, so what shows is what the account has; if the
/// service refuses, the list stays as it was and says so.
/// </summary>
public sealed partial class MyFeedsViewModel : ObservableObject
{
    private Dictionary<string, SavedFeedLabel> _labels = [];

    public ObservableCollection<SavedFeedRow> Rows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool ShowEmpty => !IsLoading && Rows.Count == 0 && !HasError;

    public async Task LoadAsync()
    {
        Error = null;
        IsLoading = true;
        try
        {
            List<SavedFeedEntry>? entries = await SavedFeedsService.LoadAsync();
            if (entries is null)
            {
                Error = "Couldn't load your feeds.";
                return;
            }

            _labels = await SavedFeedsService.DescribeAsync(entries);
            Rebuild(entries);
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ShowEmpty));
        }
    }

    public Task PinOrUnpinAsync(SavedFeedRow row) =>
        ApplyAsync(entries => row.IsPinned
            ? SavedFeedPolicy.Unpin(entries, row.Entry.Type, row.Entry.Value)
            : SavedFeedPolicy.Pin(entries, row.Entry.Type, row.Entry.Value, SavedFeedPolicy.NewId()),
            $"Couldn't {(row.IsPinned ? "unpin" : "pin")} {row.Name}.");

    public Task MoveAsync(SavedFeedRow row, int delta) =>
        ApplyAsync(entries => SavedFeedPolicy.MovePinned(entries, row.Entry.Id, delta), $"Couldn't move {row.Name}.");

    public Task RemoveAsync(SavedFeedRow row) =>
        ApplyAsync(entries => SavedFeedPolicy.Remove(entries, row.Entry.Id), $"Couldn't remove {row.Name}.");

    private async Task ApplyAsync(Func<List<SavedFeedEntry>, List<SavedFeedEntry>> edit, string failure)
    {
        if (IsBusy)
            return;

        IsBusy = true;
        Error = null;
        try
        {
            List<SavedFeedEntry>? stored = await SavedFeedsService.UpdateAsync(edit);
            if (stored is null)
            {
                Error = failure + " Try again.";
                return;
            }

            Rebuild(stored);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(ShowEmpty));
        }
    }

    private void Rebuild(IReadOnlyList<SavedFeedEntry> entries)
    {
        Rows.Clear();
        foreach (SavedFeedEntry entry in entries)
        {
            _labels.TryGetValue(entry.Value, out SavedFeedLabel? label);
            Rows.Add(new SavedFeedRow(
                entry,
                label,
                SavedFeedPolicy.CanMovePinned(entries, entry.Id, -1),
                SavedFeedPolicy.CanMovePinned(entries, entry.Id, 1)));
        }
    }

    [RelayCommand]
    private Task Retry() => LoadAsync();
}

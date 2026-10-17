using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using idunno.Bluesky.Actor;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.ViewModels;

/// <summary>
/// The accounts whose bell is on. Pages in; "Turn off" removes the row straight away and puts it
/// back if the server refuses.
/// </summary>
public sealed partial class ActivitySubscriptionsViewModel : ObservableObject
{
    private const int PageSize = 50;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _cursor;

    public ObservableCollection<ActorItem> Accounts { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool HasMore => !string.IsNullOrEmpty(_cursor);

    public async Task LoadAsync()
    {
        Error = null;
        Accounts.Clear();
        _cursor = null;

        if (PreviewMode.IsEnabled)
        {
            foreach (ActorItem item in PreviewMode.SampleActivitySubscriptions())
                Accounts.Add(item);
            return;
        }

        if (!BlueskySessionService.Instance.IsSignedIn)
            return;

        IsLoading = true;
        try
        {
            await LoadPageAsync(reset: true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public Task LoadMoreAsync() => HasMore ? LoadPageAsync(reset: false) : Task.CompletedTask;

    private async Task LoadPageAsync(bool reset)
    {
        if (!await _gate.WaitAsync(0))
            return;

        try
        {
            var result = await BlueskySessionService.Instance.Agent.ListActivitySubscriptions(limit: PageSize, cursor: reset ? null : _cursor);
            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("Activity", $"List failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                Error = result.StatusCode == 0 ? "Couldn't reach Bluesky. Check your connection." : "Couldn't load this list.";
                return;
            }

            if (reset)
                Accounts.Clear();

            foreach (ProfileView profile in result.Result)
                Accounts.Add(FeedMapper.ToSubscribedActorItem(profile));

            _cursor = result.Result.Cursor;
        }
        catch (Exception ex)
        {
            LogService.Warn("Activity", $"List threw: {ex.Message}");
            Error = "Couldn't load this list.";
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Turns the bell off for <paramref name="item"/>; the row leaves at once and returns if the server refuses.</summary>
    public async Task TurnOffAsync(ActorItem item)
    {
        int index = Accounts.IndexOf(item);
        if (index < 0)
            return;

        Error = null;
        Accounts.RemoveAt(index);

        if (await ActivitySubscriptionService.SetAsync(item.Did, ActivitySubscriptionLevel.Off))
            return;

        Accounts.Insert(ManagedAccountsPolicy.RestoreIndex(index, Accounts.Count), item);
        Error = $"Couldn't turn off notifications for @{item.Handle}. Try again.";
    }

    [RelayCommand]
    private Task Retry() => LoadAsync();
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using idunno.AtProto;
using idunno.Bluesky.Graph;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Models;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.ViewModels;

/// <summary>The accounts in one starter pack, paged from the pack's underlying list.</summary>
public sealed partial class StarterPackViewModel : ObservableObject
{
    private const int PageSize = 50;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private StarterPackArgs? _args;
    private string? _cursor;

    public ObservableCollection<ActorItem> Members { get; } = [];

    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    public partial string Description { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ByLine { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool HasLoaded { get; private set; }

    public bool HasDescription => Description.Length > 0;

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool IsEmpty => HasLoaded && Members.Count == 0 && !HasError;

    public bool HasMore => !string.IsNullOrEmpty(_cursor);

    public async Task LoadAsync(StarterPackArgs args)
    {
        _args = args;
        Name = args.Name;
        Description = args.Description;
        ByLine = args.CreatorHandle.Length > 0 ? $"by @{args.CreatorHandle}" : string.Empty;
        Members.Clear();
        _cursor = null;
        Error = null;
        HasLoaded = false;

        if (PreviewMode.IsEnabled)
        {
            foreach (ActorItem actor in PreviewMode.SampleSearchActors())
                Members.Add(actor);
            HasLoaded = true;
            OnPropertyChanged(nameof(IsEmpty));
            return;
        }

        if (!BlueskySessionService.Instance.IsSignedIn || args.ListUri.Length == 0)
        {
            HasLoaded = true;
            return;
        }

        await LoadPageAsync(reset: true);
    }

    public Task LoadMoreAsync() => HasMore ? LoadPageAsync(reset: false) : Task.CompletedTask;

    private async Task LoadPageAsync(bool reset)
    {
        if (_args is null || !await _gate.WaitAsync(0))
            return;

        IsLoading = true;
        try
        {
            await LabelService.Instance.EnsureLoadedAsync();
            AtProtoHttpResult<ListViewWithItems> result = await BlueskySessionService.Instance.Agent.GetList(
                new AtUri(_args.ListUri),
                limit: PageSize,
                cursor: reset ? null : _cursor,
                subscribedLabelers: LabelService.Instance.Subscribed);

            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("StarterPack", $"GetList failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                if (reset)
                {
                    Error = result.StatusCode == 0
                        ? "Couldn't reach Bluesky. Check your connection."
                        : "Couldn't load this starter pack.";
                }
                return;
            }

            foreach (ListItemView item in result.Result)
                Members.Add(FeedMapper.ToActorItem(item.Subject));

            _cursor = result.Result.Cursor;
        }
        catch (Exception ex)
        {
            LogService.Warn("StarterPack", $"GetList threw: {ex.Message}");
            if (reset)
                Error = "Couldn't load this starter pack.";
        }
        finally
        {
            IsLoading = false;
            HasLoaded = true;
            OnPropertyChanged(nameof(IsEmpty));
            _gate.Release();
        }
    }

    [RelayCommand]
    private Task Retry() => _args is null ? Task.CompletedTask : LoadAsync(_args);

    [RelayCommand]
    private Task OpenInBrowser() => _args is null || _args.WebUrl.Length == 0
        ? Task.CompletedTask
        : RichTextBuilder.OpenAsync(_args.WebUrl);
}

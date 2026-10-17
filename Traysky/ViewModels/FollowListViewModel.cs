using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using idunno.AtProto;
using idunno.Bluesky;
using idunno.Bluesky.Actor;
using idunno.Bluesky.Graph;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Traysky.Models;
using Traysky.Services;
using Traysky.ViewModels.Items;

namespace Traysky.ViewModels;

/// <summary>
/// An account's followers and the accounts it follows. Both lists load when the page opens and
/// page in independently as each is scrolled.
/// </summary>
public sealed partial class FollowListViewModel : ObservableObject
{
    private const int PageSize = 50;

    private readonly SemaphoreSlim _followersGate = new(1, 1);
    private readonly SemaphoreSlim _followsGate = new(1, 1);
    private AtIdentifier? _actor;
    private FollowListArgs? _args;
    private string? _followersCursor;
    private string? _followsCursor;

    public ObservableCollection<ActorItem> Followers { get; } = [];
    public ObservableCollection<ActorItem> Following { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool HasMoreFollowers => !string.IsNullOrEmpty(_followersCursor);
    public bool HasMoreFollowing => !string.IsNullOrEmpty(_followsCursor);

    /// <summary>The page shows only the followers you also follow, with no Followers / Following tabs.</summary>
    public bool KnownOnly => _args?.KnownOnly == true;

    public string FollowersHeader
    {
        get
        {
            string label = KnownOnly ? "Followers you know" : "Followers";
            return _args is { FollowersCount: > 0 } a ? $"{label} ({a.FollowersCount})" : label;
        }
    }

    public string FollowingHeader => _args is { FollowsCount: > 0 } a ? $"Following ({a.FollowsCount})" : "Following";

    public async Task LoadAsync(FollowListArgs args)
    {
        _args = args;
        OnPropertyChanged(nameof(KnownOnly));
        OnPropertyChanged(nameof(FollowersHeader));
        OnPropertyChanged(nameof(FollowingHeader));

        Error = null;
        Followers.Clear();
        Following.Clear();
        _followersCursor = null;
        _followsCursor = null;

        if (PreviewMode.IsEnabled)
        {
            if (args.KnownOnly)
            {
                foreach (ActorItem actor in PreviewMode.SampleSearchActors())
                    Followers.Add(actor);
            }
            return;
        }

        if (!BlueskySessionService.Instance.IsSignedIn || string.IsNullOrWhiteSpace(args.Actor))
            return;

        _actor = args.Actor.StartsWith("did:", StringComparison.OrdinalIgnoreCase)
            ? new Did(args.Actor)
            : new Handle(args.Actor);

        IsLoading = true;
        try
        {
            await (args.KnownOnly
                ? LoadFollowersAsync(reset: true)
                : Task.WhenAll(LoadFollowersAsync(reset: true), LoadFollowsAsync(reset: true)));
        }
        finally
        {
            IsLoading = false;
        }
    }

    public Task LoadMoreFollowersAsync() => HasMoreFollowers ? LoadFollowersAsync(reset: false) : Task.CompletedTask;

    public Task LoadMoreFollowingAsync() => HasMoreFollowing ? LoadFollowsAsync(reset: false) : Task.CompletedTask;

    private async Task LoadFollowersAsync(bool reset)
    {
        if (_actor is null || !await _followersGate.WaitAsync(0))
            return;

        try
        {
            BlueskyAgent agent = BlueskySessionService.Instance.Agent;
            string? cursor = reset ? null : _followersCursor;
            AtProtoHttpResult<Followers> result = KnownOnly
                ? await agent.GetKnownFollowers(_actor, limit: PageSize, cursor: cursor, subscribedLabelers: LabelService.Instance.Subscribed)
                : await agent.GetFollowers(_actor, limit: PageSize, cursor: cursor);

            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("FollowList", $"GetFollowers failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                Error = LoadError(result.StatusCode);
                return;
            }

            if (reset)
                Followers.Clear();

            foreach (ProfileView profile in result.Result)
                Followers.Add(FeedMapper.ToActorItem(profile));

            _followersCursor = result.Result.Cursor;
        }
        catch (Exception ex)
        {
            LogService.Warn("FollowList", $"GetFollowers threw: {ex.Message}");
            Error = "Couldn't load followers.";
        }
        finally
        {
            _followersGate.Release();
        }
    }

    private async Task LoadFollowsAsync(bool reset)
    {
        if (_actor is null || !await _followsGate.WaitAsync(0))
            return;

        try
        {
            AtProtoHttpResult<Follows> result = await BlueskySessionService.Instance.Agent.GetFollows(
                _actor,
                limit: PageSize,
                cursor: reset ? null : _followsCursor);

            if (!result.Succeeded || result.Result is null)
            {
                LogService.Warn("FollowList", $"GetFollows failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                Error = LoadError(result.StatusCode);
                return;
            }

            if (reset)
                Following.Clear();

            foreach (ProfileView profile in result.Result)
                Following.Add(FeedMapper.ToActorItem(profile));

            _followsCursor = result.Result.Cursor;
        }
        catch (Exception ex)
        {
            LogService.Warn("FollowList", $"GetFollows threw: {ex.Message}");
            Error = "Couldn't load who this account follows.";
        }
        finally
        {
            _followsGate.Release();
        }
    }

    private static string LoadError(System.Net.HttpStatusCode status) => status == 0
        ? "Couldn't reach Bluesky. Check your connection."
        : "Couldn't load this list.";

    [RelayCommand]
    private Task Retry() => _args is null ? Task.CompletedTask : LoadAsync(_args);
}

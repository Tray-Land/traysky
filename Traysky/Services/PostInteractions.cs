using idunno.AtProto;
using idunno.AtProto.Repo;
using System;
using System.Threading.Tasks;
using Traysky.ViewModels.Items;

namespace Traysky.Services;

/// <summary>
/// Like / repost toggles with optimistic UI: the card flips immediately, and flips back if
/// the request fails. Shared by every list that shows a <see cref="PostItem"/>.
/// </summary>
public static class PostInteractions
{
    /// <summary>Raised after a bookmark was added or removed on the server, so the Bookmarks page can follow along.</summary>
    public static event EventHandler<PostItem>? BookmarkChanged;

    public static async Task ToggleLikeAsync(PostItem post)
    {
        if (post.IsBusy || !BlueskySessionService.Instance.IsSignedIn)
            return;

        post.IsBusy = true;
        bool wasLiked = post.IsLiked;
        string? previousLikeUri = post.LikeUri;

        post.IsLiked = !wasLiked;
        post.LikeCount = Math.Max(0, post.LikeCount + (wasLiked ? -1 : 1));

        try
        {
            if (wasLiked)
            {
                if (previousLikeUri is null)
                    return; // nothing to undo server-side; local state was already wrong

                AtProtoHttpResult<DeleteResult> result = await BlueskySessionService.Instance.Agent.DeleteLike(new AtUri(previousLikeUri));
                if (result.Succeeded)
                {
                    post.LikeUri = null;
                    return;
                }
                Revert();
                LogService.Warn("Interact", $"Unlike failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
            }
            else
            {
                AtProtoHttpResult<CreateRecordResult> result = await BlueskySessionService.Instance.Agent.Like(post.Reference);
                if (result.Succeeded && result.Result is not null)
                {
                    post.LikeUri = result.Result.Uri.ToString();
                    return;
                }
                Revert();
                LogService.Warn("Interact", $"Like failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
            }
        }
        catch (Exception ex)
        {
            Revert();
            LogService.Warn("Interact", $"Like threw: {ex.Message}");
        }
        finally
        {
            post.IsBusy = false;
        }

        void Revert()
        {
            post.IsLiked = wasLiked;
            post.LikeUri = previousLikeUri;
            post.LikeCount = Math.Max(0, post.LikeCount + (wasLiked ? 1 : -1));
        }
    }

    /// <summary>Deletes one of the signed-in user's own posts. True when the server accepted it.</summary>
    public static async Task<bool> DeletePostAsync(PostItem post)
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return false;

        try
        {
            AtProtoHttpResult<DeleteResult> result = await BlueskySessionService.Instance.Agent.DeletePost(new AtUri(post.AtUri));
            if (result.Succeeded)
                return true;

            LogService.Warn("Interact", $"Delete post failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
        }
        catch (Exception ex)
        {
            LogService.Warn("Interact", $"Delete post threw: {ex.Message}");
        }
        return false;
    }

    /// <summary>Mutes or unmutes the post's thread, flipping <see cref="PostItem.IsThreadMuted"/> on success. True when the server accepted it.</summary>
    public static async Task<bool> ToggleThreadMuteAsync(PostItem post)
    {
        if (post.IsBusy || !BlueskySessionService.Instance.IsSignedIn || post.ThreadRootUri.Length == 0)
            return false;

        post.IsBusy = true;
        bool wasMuted = post.IsThreadMuted;
        try
        {
            AtUri root = new(post.ThreadRootUri);
            var result = wasMuted
                ? await BlueskySessionService.Instance.Agent.UnmuteThread(root)
                : await BlueskySessionService.Instance.Agent.MuteThread(root);

            if (result.Succeeded)
            {
                post.IsThreadMuted = !wasMuted;
                return true;
            }
            LogService.Warn("Interact", $"Thread mute toggle failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
        }
        catch (Exception ex)
        {
            LogService.Warn("Interact", $"Thread mute toggle threw: {ex.Message}");
        }
        finally
        {
            post.IsBusy = false;
        }
        return false;
    }

    public static async Task ToggleBookmarkAsync(PostItem post)
    {
        if (post.IsBusy || !BlueskySessionService.Instance.IsSignedIn)
            return;

        post.IsBusy = true;
        bool wasBookmarked = post.IsBookmarked;
        post.IsBookmarked = !wasBookmarked;

        try
        {
            var result = wasBookmarked
                ? await BlueskySessionService.Instance.Agent.DeleteBookmark(new AtUri(post.AtUri))
                : await BlueskySessionService.Instance.Agent.CreateBookmark(post.Reference);

            if (!result.Succeeded)
            {
                post.IsBookmarked = wasBookmarked;
                LogService.Warn("Interact", $"Bookmark toggle failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                return;
            }

            BookmarkChanged?.Invoke(null, post);
        }
        catch (Exception ex)
        {
            post.IsBookmarked = wasBookmarked;
            LogService.Warn("Interact", $"Bookmark toggle threw: {ex.Message}");
        }
        finally
        {
            post.IsBusy = false;
        }
    }

    public static async Task ToggleRepostAsync(PostItem post)
    {
        if (post.IsBusy || !BlueskySessionService.Instance.IsSignedIn)
            return;

        post.IsBusy = true;
        bool wasReposted = post.IsReposted;
        string? previousRepostUri = post.RepostUri;

        post.IsReposted = !wasReposted;
        post.RepostCount = Math.Max(0, post.RepostCount + (wasReposted ? -1 : 1));

        try
        {
            if (wasReposted)
            {
                if (previousRepostUri is null)
                    return;

                AtProtoHttpResult<DeleteResult> result = await BlueskySessionService.Instance.Agent.DeleteRepost(new AtUri(previousRepostUri));
                if (result.Succeeded)
                {
                    post.RepostUri = null;
                    return;
                }
                Revert();
                LogService.Warn("Interact", $"Un-repost failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
            }
            else
            {
                AtProtoHttpResult<CreateRecordResult> result = await BlueskySessionService.Instance.Agent.Repost(post.Reference);
                if (result.Succeeded && result.Result is not null)
                {
                    post.RepostUri = result.Result.Uri.ToString();
                    return;
                }
                Revert();
                LogService.Warn("Interact", $"Repost failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
            }
        }
        catch (Exception ex)
        {
            Revert();
            LogService.Warn("Interact", $"Repost threw: {ex.Message}");
        }
        finally
        {
            post.IsBusy = false;
        }

        void Revert()
        {
            post.IsReposted = wasReposted;
            post.RepostUri = previousRepostUri;
            post.RepostCount = Math.Max(0, post.RepostCount + (wasReposted ? 1 : -1));
        }
    }
}

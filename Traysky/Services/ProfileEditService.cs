using idunno.AtProto;
using idunno.AtProto.Repo;
using idunno.Bluesky.Actor;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Traysky.Services;

/// <summary>The signed-in account's profile as the editor first shows it.</summary>
public sealed record OwnProfile(string DisplayName, string Bio, Uri? AvatarUri, Uri? BannerUri);

/// <summary>An avatar or banner chosen in the editor, already shrunk to fit the blob limit.</summary>
public sealed record ProfileImageUpload(byte[] Bytes, string MimeType);

/// <summary>
/// Reads and writes the account's own profile record. A save re-reads the record first and changes
/// only the fields the editor owns, so pronouns, website, the pinned post, self-labels and anything
/// a newer client has added survive. Each call returns null / false and logs the reason on failure.
/// </summary>
public static class ProfileEditService
{
    public static async Task<OwnProfile?> LoadAsync()
    {
        if (PreviewMode.IsEnabled)
        {
            (string name, string bio) = PreviewMode.SampleProfileText();
            return new OwnProfile(name, bio, null, null);
        }

        if (!BlueskySessionService.Instance.IsSignedIn || BlueskySessionService.Instance.Did is not { } did)
            return null;

        try
        {
            var result = await BlueskySessionService.Instance.Agent.GetProfile(new Did(did));
            if (result.Succeeded && result.Result is { } p)
                return new OwnProfile(p.DisplayName ?? string.Empty, p.Description ?? string.Empty, p.Avatar, p.Banner);

            LogService.Warn("ProfileEdit", $"GetProfile failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
        }
        catch (Exception ex)
        {
            LogService.Warn("ProfileEdit", $"GetProfile threw: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Stores the new text and image changes. <paramref name="avatar"/> / <paramref name="banner"/> are only
    /// read for <see cref="ProfileImageChange.Replace"/>. Returns false, with an explanation in
    /// <paramref name="error"/>, when nothing was saved.
    /// </summary>
    public static async Task<(bool Saved, string? Error)> SaveAsync(
        string? displayName,
        string? bio,
        ProfileImageChange avatarChange,
        ProfileImageUpload? avatar,
        ProfileImageChange bannerChange,
        ProfileImageUpload? banner)
    {
        if (PreviewMode.IsEnabled)
            return (true, null);

        if (!BlueskySessionService.Instance.IsSignedIn)
            return (false, "You're signed out.");

        if (ProfileEditPolicy.Validate(displayName, bio) is { } invalid)
            return (false, invalid);

        try
        {
            var agent = BlueskySessionService.Instance.Agent;

            var current = await agent.GetProfileRecord(CancellationToken.None);
            if (!current.Succeeded || current.Result is null)
            {
                LogService.Warn("ProfileEdit", $"GetProfileRecord failed: {(int)current.StatusCode} {current.AtErrorDetail?.Error}");
                return (false, current.StatusCode == 0
                    ? "Couldn't reach Bluesky. Check your connection."
                    : "Couldn't read your current profile, so nothing was changed.");
            }

            AtProtoRepositoryRecord<Profile> record = current.Result;
            record.Value.DisplayName = ProfileEditPolicy.Normalize(displayName);
            record.Value.Description = ProfileEditPolicy.Normalize(bio);

            if (!await ApplyImageAsync(agent, avatarChange, avatar, blob => record.Value.Avatar = blob, "avatar")
                || !await ApplyImageAsync(agent, bannerChange, banner, blob => record.Value.Banner = blob, "banner"))
            {
                return (false, "Couldn't upload the picture. Nothing was changed.");
            }

            var result = await agent.UpdateProfile(record);
            if (result.Succeeded)
                return (true, null);

            LogService.Warn("ProfileEdit", $"UpdateProfile failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
            return (false, result.StatusCode == 0
                ? "Couldn't reach Bluesky. Check your connection."
                : "Bluesky didn't accept the changes. Try again.");
        }
        catch (Exception ex)
        {
            LogService.Warn("ProfileEdit", $"Save threw: {ex.Message}");
            return (false, "Couldn't save your profile.");
        }
    }

    /// <summary>Uploads and sets, clears, or leaves an image field; false only when an upload failed.</summary>
    private static async Task<bool> ApplyImageAsync(
        idunno.Bluesky.BlueskyAgent agent,
        ProfileImageChange change,
        ProfileImageUpload? upload,
        Action<Blob?> set,
        string what)
    {
        switch (change)
        {
            case ProfileImageChange.Remove:
                set(null);
                return true;

            case ProfileImageChange.Replace when upload is not null:
                var uploaded = await agent.UploadBlob(upload.Bytes, upload.MimeType);
                if (!uploaded.Succeeded || uploaded.Result is null)
                {
                    LogService.Warn("ProfileEdit", $"UploadBlob ({what}) failed: {(int)uploaded.StatusCode} {uploaded.AtErrorDetail?.Error}");
                    return false;
                }

                set(uploaded.Result);
                return true;

            default:
                return true;
        }
    }
}

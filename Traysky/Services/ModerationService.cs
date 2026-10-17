using idunno.AtProto;
using idunno.AtProto.Admin;
using idunno.AtProto.Repo;
using idunno.Bluesky.Moderation;
using System;
using System.Threading.Tasks;

namespace Traysky.Services;

/// <summary>
/// Mute, block and report. Each call returns whether the server accepted it (or, for block, the
/// new block record's URI) and logs the reason when it didn't, so callers only decide how to
/// show the outcome.
/// </summary>
public static class ModerationService
{
    /// <summary>Bluesky's own moderation service, the labeler reports are filed with.</summary>
    private static readonly Did BlueskyModerationService = new("did:plc:ar7c4by46qjdydhdevvrndac");

    public static async Task<bool> SetMutedAsync(string did, bool muted)
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return false;

        try
        {
            var agent = BlueskySessionService.Instance.Agent;
            var result = muted ? await agent.Mute(new Did(did)) : await agent.Unmute(new Did(did));
            if (result.Succeeded)
                return true;

            LogService.Warn("Moderation", $"{(muted ? "Mute" : "Unmute")} failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
        }
        catch (Exception ex)
        {
            LogService.Warn("Moderation", $"Mute toggle threw: {ex.Message}");
        }
        return false;
    }

    /// <summary>Blocks the account; returns the block record's AT URI (needed to undo it), or null on failure.</summary>
    public static async Task<string?> BlockAsync(string did)
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return null;

        try
        {
            var result = await BlueskySessionService.Instance.Agent.Block(new Did(did));
            if (result.Succeeded && result.Result is not null)
                return result.Result.Uri.ToString();

            LogService.Warn("Moderation", $"Block failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
        }
        catch (Exception ex)
        {
            LogService.Warn("Moderation", $"Block threw: {ex.Message}");
        }
        return null;
    }

    public static async Task<bool> UnblockAsync(string blockUri)
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return false;

        try
        {
            var result = await BlueskySessionService.Instance.Agent.DeleteBlock(new AtUri(blockUri));
            if (result.Succeeded)
                return true;

            LogService.Warn("Moderation", $"Unblock failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
        }
        catch (Exception ex)
        {
            LogService.Warn("Moderation", $"Unblock threw: {ex.Message}");
        }
        return false;
    }

    public static Task<bool> ReportAccountAsync(string did, ReportType reason, string details) =>
        ReportAsync(new RepoReference { Did = new Did(did) }, reason, details);

    public static Task<bool> ReportPostAsync(StrongReference post, ReportType reason, string details) =>
        ReportAsync(post, reason, details);

    private static async Task<bool> ReportAsync(idunno.AtProto.Moderation.SubjectType subject, ReportType reason, string details)
    {
        if (!BlueskySessionService.Instance.IsSignedIn)
            return false;

        try
        {
            var result = await BlueskySessionService.Instance.Agent.CreateModerationReport(BlueskyModerationService, subject, reason, details);
            if (result.Succeeded)
                return true;

            LogService.Warn("Moderation", $"Report failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
        }
        catch (Exception ex)
        {
            LogService.Warn("Moderation", $"Report threw: {ex.Message}");
        }
        return false;
    }
}

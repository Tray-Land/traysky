using idunno.AtProto;
using System;
using System.Threading.Tasks;

namespace Traysky.Services;

/// <summary>
/// The per-account bell: get a notification when someone posts (or posts and replies). Returns
/// whether the server accepted the change and logs the reason when it didn't.
/// </summary>
public static class ActivitySubscriptionService
{
    public static async Task<bool> SetAsync(string did, ActivitySubscriptionLevel level)
    {
        if (PreviewMode.IsEnabled)
            return true;

        if (!BlueskySessionService.Instance.IsSignedIn)
            return false;

        try
        {
            (bool posts, bool replies) = ActivitySubscriptionPolicy.ToFlags(level);
            var result = await BlueskySessionService.Instance.Agent.SetActivitySubscription(new Did(did), posts, replies);
            if (result.Succeeded)
                return true;

            LogService.Warn("Activity", $"Set subscription failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
        }
        catch (Exception ex)
        {
            LogService.Warn("Activity", $"Set subscription threw: {ex.Message}");
        }
        return false;
    }
}

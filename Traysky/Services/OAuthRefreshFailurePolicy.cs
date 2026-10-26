using System;
using System.Collections.Generic;

namespace Traysky.Services;

public static class OAuthRefreshFailurePolicy
{
    public static bool RequiresSignIn(string? error) =>
        string.Equals(error, "invalid_grant", StringComparison.Ordinal);

    public static string ReadError(object? state)
    {
        if (state is IEnumerable<KeyValuePair<string, object?>> values)
        {
            foreach (KeyValuePair<string, object?> value in values)
            {
                if (value.Key == "error" && value.Value is string error)
                {
                    // Never forward arbitrary SDK text: descriptions and formatted logs can contain tokens.
                    return error switch
                    {
                        "invalid_grant" or "invalid_client" or "unauthorized_client" or
                        "invalid_scope" or "invalid_request" or "unsupported_grant_type" or
                        "server_error" or "temporarily_unavailable" or
                        "use_dpop_nonce" or "invalid_dpop_proof" => error,
                        _ => "unknown_error"
                    };
                }
            }
        }

        return "unknown_error";
    }
}

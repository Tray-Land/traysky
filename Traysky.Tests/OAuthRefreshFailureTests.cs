using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using Traysky.Services;

namespace Traysky.Tests;

[TestClass]
public class OAuthRefreshFailureTests
{
    [TestMethod]
    [DataRow("invalid_grant", true)]
    [DataRow("server_error", false)]
    [DataRow("temporarily_unavailable", false)]
    [DataRow("use_dpop_nonce", false)]
    [DataRow("invalid_dpop_proof", false)]
    [DataRow("invalid_client", false)]
    [DataRow("unknown_error", false)]
    [DataRow(null, false)]
    public void OnlyRejectedGrantsRequireSignIn(string? error, bool expected)
    {
        Assert.AreEqual(expected, OAuthRefreshFailurePolicy.RequiresSignIn(error));
    }

    [TestMethod]
    [DataRow("invalid_grant")]
    [DataRow("server_error")]
    [DataRow("temporarily_unavailable")]
    [DataRow("use_dpop_nonce")]
    public void ReadsStructuredRefreshErrors(string error)
    {
        Assert.AreEqual(error, OAuthRefreshFailurePolicy.ReadError(State(error)));
    }

    [TestMethod]
    public void DoesNotForwardArbitraryTextOrSecrets()
    {
        Assert.AreEqual("unknown_error", OAuthRefreshFailurePolicy.ReadError(State("secret-token")));
        Assert.AreEqual("unknown_error", OAuthRefreshFailurePolicy.ReadError("invalid_grant secret-token"));
        Assert.AreEqual("unknown_error", OAuthRefreshFailurePolicy.ReadError(null));
        Assert.AreEqual("unknown_error", OAuthRefreshFailurePolicy.ReadError(new[]
        {
            new KeyValuePair<string, object?>("errorDescription", "invalid_grant secret-token")
        }));
    }

    [TestMethod]
    public void BridgesTheSdkRefreshEventWithoutFormattingSensitiveMessages()
    {
        List<string> errors = [];
        using OAuthRefreshLoggerProvider provider = new(errors.Add);
        ILogger logger = provider.CreateLogger("idunno.AtProto.Authentication.OAuthClient");

        logger.Log(LogLevel.Error, new EventId(611), State("invalid_grant"), null,
            (_, _) => throw new InvalidOperationException("The formatter must never be invoked."));

        CollectionAssert.AreEqual(new[] { "invalid_grant" }, errors);
    }

    [TestMethod]
    public void IgnoresOtherCategoriesLevelsAndEvents()
    {
        List<string> errors = [];
        using OAuthRefreshLoggerProvider provider = new(errors.Add);
        ILogger logger = provider.CreateLogger("idunno.AtProto.Authentication.OAuthClient");
        ILogger other = provider.CreateLogger("Duende.IdentityModel.OidcClient.OidcClient");

        logger.Log(LogLevel.Debug, new EventId(611), State("invalid_grant"), null, (_, _) => "secret");
        logger.Log(LogLevel.Error, new EventId(612), State("invalid_grant"), null, (_, _) => "secret");
        other.Log(LogLevel.Error, new EventId(611), State("invalid_grant"), null, (_, _) => "secret");

        Assert.IsEmpty(errors);
    }

    private static KeyValuePair<string, object?>[] State(string error) =>
    [
        new("error", error),
        new("errorDescription", "Session expired; secret-token must not be forwarded"),
        new("{OriginalFormat}", "OAuthClient refresh failed with {error} {errorDescription}")
    ];
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;

namespace Traysky.Services;

public sealed class OAuthRefreshLoggerProvider(Action<string> refreshFailed) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) =>
        categoryName == "idunno.AtProto.Authentication.OAuthClient"
            ? new RefreshLogger(refreshFailed)
            : NullLogger.Instance;

    public void Dispose()
    {
    }

    private sealed class RefreshLogger(Action<string> refreshFailed) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // idunno 8's OAuth refresh error (611) is not included in TokenRefreshFailedEventArgs.
            if (IsEnabled(logLevel) && eventId.Id == 611)
                refreshFailed(OAuthRefreshFailurePolicy.ReadError(state));
        }
    }
}

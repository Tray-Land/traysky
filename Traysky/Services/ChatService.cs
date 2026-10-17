using CommunityToolkit.Mvvm.ComponentModel;
using idunno.AtProto;
using idunno.Bluesky.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Traysky.ViewModels.Items;

namespace Traysky.Services;

/// <summary>
/// Whether this session can use direct messages, and how many conversations are unread.
/// Bluesky only lets a session into chat when it was granted DM access (an app password made
/// with "Allow access to your direct messages", or an OAuth grant with the chat scope), and
/// there is no way to ask for the grant up front - so this probes with a real listConvos call
/// on sign-in and whenever the flyout reopens, and the shell shows the Messages button only
/// once that call has worked. Observable properties change on the UI thread.
/// </summary>
public sealed partial class ChatService : ObservableObject
{
    private static readonly Lazy<ChatService> _instance = new(() => new ChatService());

    public static ChatService Instance => _instance.Value;

    /// <summary>Reopening the flyout re-checks at most this often.</summary>
    private static readonly TimeSpan RecheckAfter = TimeSpan.FromSeconds(30);

    /// <summary>One sync follows the log's cursor at most this many pages, so a long absence can't spin.</summary>
    private const int MaxLogPages = 5;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _logGate = new(1, 1);
    private string? _logCursor;
    private string? _logCursorDid;
    private DateTimeOffset _checkedAtUtc = DateTimeOffset.MinValue;

    private ChatService()
    {
        BlueskySessionService.Instance.SignedIn += (_, _) => _ = CheckAsync(force: true);
        BlueskySessionService.Instance.SignedOut += (_, _) => Reset();
    }

    /// <summary>True once a chat call has succeeded for this session.</summary>
    [ObservableProperty]
    public partial bool CanUseChat { get; private set; }

    /// <summary>Accepted, unmuted conversations with unread messages - what the Messages badge shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnread))]
    public partial int UnreadCount { get; private set; }

    public bool HasUnread => UnreadCount > 0;

    /// <summary>
    /// Probes chat access and refreshes the unread count. Skipped when the last check was
    /// recent, unless <paramref name="force"/> is set.
    /// </summary>
    public async Task CheckAsync(bool force = false)
    {
        BlueskySessionService session = BlueskySessionService.Instance;
        if (!session.IsSignedIn)
            return;

        if (!force && DateTimeOffset.UtcNow - _checkedAtUtc < RecheckAfter)
            return;

        if (!await _gate.WaitAsync(0))
            return;

        try
        {
            if (PreviewMode.IsEnabled)
            {
                CanUseChat = true;
                UnreadCount = PreviewMode.SampleConversations().Count(c => c.IsUnread && !c.IsRequest);
                _checkedAtUtc = DateTimeOffset.UtcNow;
                return;
            }

            if (!await session.EnsureAuthenticatedAsync())
                return;

            AtProtoHttpResult<UnreadConversationCounts> result = await session.Agent.GetUnreadCounts(includeGroupChats: true);

            ChatAccess access = ChatAccessPolicy.FromProbe(result.Succeeded && result.Result is not null, (int)result.StatusCode, result.AtErrorDetail?.Error);
            if (access == ChatAccess.Unknown)
            {
                // Offline or a server hiccup: keep whatever the last good check said.
                LogService.Warn("Chat", $"Chat check inconclusive: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                return;
            }

            _checkedAtUtc = DateTimeOffset.UtcNow;

            if (access == ChatAccess.Denied)
            {
                if (CanUseChat)
                    LogService.Info("Chat", "Chat is no longer available to this session");
                else
                    LogService.Info("Chat", $"No direct message access for this session ({(int)result.StatusCode} {result.AtErrorDetail?.Error})");
                CanUseChat = false;
                UnreadCount = 0;
                return;
            }

            if (!CanUseChat)
                LogService.Info("Chat", "Direct messages are available");
            CanUseChat = true;
            UnreadCount = ChatConversationMenuPolicy.BadgeCount(result.Result!.UnreadAcceptedConversations);
        }
        catch (Exception ex)
        {
            LogService.Warn("Chat", $"Chat check threw: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Everything that happened in chat since the last call, oldest first, followed from a cursor
    /// saved per account so the next launch resumes where this one stopped. The first ever call
    /// has nothing to resume from and returns nothing but a starting point. Null means the call
    /// couldn't be made (offline, signed out, another sync already running); try again later.
    /// </summary>
    public async Task<IReadOnlyList<LogBase>?> SyncLogAsync()
    {
        BlueskySessionService session = BlueskySessionService.Instance;
        if (!session.IsSignedIn || PreviewMode.IsEnabled || session.Did is not { Length: > 0 } did)
            return null;

        if (!await _logGate.WaitAsync(0))
            return null;

        try
        {
            if (!await session.EnsureAuthenticatedAsync())
                return null;

            if (_logCursorDid != did)
            {
                _logCursor = SettingsService.GetChatLogCursor(did);
                _logCursorDid = did;
            }

            bool baseline = _logCursor is null;
            List<LogBase> entries = [];

            for (int page = 0; page < MaxLogPages; page++)
            {
                AtProtoHttpResult<Logs> result = await session.Agent.GetConversationLog(_logCursor);
                if (!result.Succeeded || result.Result is null)
                {
                    LogService.Warn("Chat", $"Chat log failed: {(int)result.StatusCode} {result.AtErrorDetail?.Error}");
                    // A cursor the server no longer accepts would fail forever: start again from now.
                    if ((int)result.StatusCode is >= 400 and < 500 and not 401 and not 429)
                        Remember(did, null);
                    return entries.Count > 0 ? entries : null;
                }

                string? next = ChatThreadPolicy.NextLogCursor(_logCursor, result.Result.Cursor);
                entries.AddRange(result.Result);
                bool moved = !string.Equals(next, _logCursor, StringComparison.Ordinal);
                Remember(did, next);

                if (!moved || result.Result.Count == 0)
                    break;
            }

            return baseline ? [] : entries;
        }
        catch (Exception ex)
        {
            LogService.Warn("Chat", $"Chat log threw: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
        finally
        {
            _logGate.Release();
        }
    }

    /// <summary>
    /// False until the log has handed back a cursor. Callers fall back to reading messages
    /// directly meanwhile, since a log call without a cursor to resume from can miss events.
    /// </summary>
    public bool HasLogCursor => _logCursor is not null;

    private void Remember(string did, string? cursor)
    {
        _logCursor = cursor;
        SettingsService.SetChatLogCursor(did, cursor);
    }

    /// <summary>The Messages list has just loaded; its first page is a fresher count than the last probe.</summary>
    public void UpdateUnreadFrom(IEnumerable<ConversationItem> conversations)
    {
        UnreadCount = conversations.Count(c => c.IsUnread && !c.IsMuted && !c.IsRequest);
    }

    /// <summary>A conversation that had unread messages was just opened and marked read.</summary>
    public void ConversationRead(ConversationItem conversation)
    {
        if (conversation.IsUnread && !conversation.IsMuted && !conversation.IsRequest && UnreadCount > 0)
            UnreadCount--;
    }

    /// <summary>A conversation was just muted or unmuted; whether it counts toward the badge is the server's call, so ask again.</summary>
    public void MuteChanged(ConversationItem conversation) => _ = CheckAsync(force: true);

    private void Reset()
    {
        CanUseChat = false;
        UnreadCount = 0;
        _checkedAtUtc = DateTimeOffset.MinValue;
    }
}

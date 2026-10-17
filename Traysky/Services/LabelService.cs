using idunno.AtProto;
using idunno.AtProto.Labels;
using idunno.Bluesky;
using idunno.Bluesky.Actor;
using idunno.Bluesky.Labeler;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Traysky.Services;

/// <summary>
/// Community labels: which labelers the signed-in account subscribes to (plus Bluesky's own
/// moderation service), what each labeler's label values mean, and the account's per-label
/// "ignore" settings. The AppView only attaches labels to profiles and posts for the labelers a
/// request names, so every fetch passes <see cref="Subscribed"/>; <see cref="EnsureLoadedAsync"/>
/// is awaited first so the very first page already carries them. Loads once per sign-in.
/// </summary>
public sealed class LabelService
{
    private static readonly Lazy<LabelService> _instance = new(() => new LabelService());

    public static LabelService Instance => _instance.Value;

    /// <summary>After a failed load, further callers wait this long before the next attempt.</summary>
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    private static readonly IReadOnlyList<LabelDisplay> NoLabels = [];

    private readonly object _lock = new();
    private Task? _loading;
    private DateTimeOffset _failedAtUtc = DateTimeOffset.MinValue;
    private int _generation;
    private State _state = State.Empty;

    private LabelService()
    {
        BlueskySessionService.Instance.SignedIn += (_, _) => Reset();
        BlueskySessionService.Instance.SignedOut += (_, _) => Reset();
    }

    /// <summary>
    /// The labelers to ask for labels from, or null before they are known (a request then simply
    /// comes back without labels).
    /// </summary>
    public IReadOnlyList<Did>? Subscribed => _state.Subscribed;

    /// <summary>Completes once the subscriptions and definitions are known, or the attempt failed. Never throws.</summary>
    public Task EnsureLoadedAsync()
    {
        if (PreviewMode.IsEnabled || !BlueskySessionService.Instance.IsSignedIn)
            return Task.CompletedTask;

        lock (_lock)
        {
            if (_loading is null)
            {
                if (DateTimeOffset.UtcNow - _failedAtUtc < RetryAfter)
                    return Task.CompletedTask;

                _loading = LoadAsync(_generation);
            }

            return _loading;
        }
    }

    /// <summary>The labels worth showing next to an account's name; empty for almost everyone.</summary>
    public IReadOnlyList<LabelDisplay> ForAccount(ProfileViewBasic? profile)
    {
        if (profile?.Labels is not { Count: > 0 } labels)
            return NoLabels;

        State state = _state;
        string subject = profile.Did.ToString();

        return LabelDisplayPolicy.Resolve(
            subject,
            labels.Select(l => new LabelInput(l.Source.ToString(), l.Value, l.IsNegationLabel, l.CreationTimestamp)),
            state.DefinitionFor,
            state.IgnoredPreference,
            state.SourceName);
    }

    private void Reset()
    {
        lock (_lock)
        {
            _generation++;
            _loading = null;
            _failedAtUtc = DateTimeOffset.MinValue;
            _state = State.Empty;
        }
    }

    private async Task LoadAsync(int generation)
    {
        try
        {
            BlueskyAgent agent = BlueskySessionService.Instance.Agent;

            AtProtoHttpResult<Preferences> prefs = await agent.GetPreferences(includeBlueskyModerationLabeler: true);
            if (!prefs.Succeeded || prefs.Result is null)
            {
                LogService.Warn("Labels", $"GetPreferences failed: {(int)prefs.StatusCode}");
                Failed(generation);
                return;
            }

            List<Did> subscribed = [.. prefs.Result.SubscribedLabelers];

            Dictionary<string, bool> ignored = [];
            foreach (ContentLabelPreference pref in prefs.Result.ContentLabelPreferences)
            {
                if (pref.Visibility != LabelVisibility.Unknown)
                    ignored[PreferenceKey(pref.LabelerDid?.ToString() ?? string.Empty, pref.Label)] = pref.Visibility == LabelVisibility.Ignore;
            }

            Dictionary<(string, string), LabelText> definitions = [];
            Dictionary<string, string> names = [];

            if (subscribed.Count > 0)
            {
                AtProtoHttpResult<ICollection<LabelerView>> services = await agent.GetLabelerServices(subscribed, getDetailedViews: true);
                if (services.Succeeded && services.Result is not null)
                {
                    foreach (LabelerView view in services.Result)
                    {
                        string labeler = view.Creator.Did.ToString();
                        names[labeler] = FeedMapper.DisplayNameOf(view.Creator);

                        if (view is LabelerViewDetailed { Policies.LabelValueDefinitions: { } defs })
                        {
                            foreach (LabelValueDefinition def in defs)
                                definitions[(labeler, def.Identifier)] = ToText(def);
                        }
                    }
                }
                else
                {
                    // Labels still show, just under their raw values.
                    LogService.Warn("Labels", $"GetLabelerServices failed: {(int)services.StatusCode}");
                }
            }

            lock (_lock)
            {
                if (generation == _generation)
                    _state = new State(subscribed, definitions, names, ignored);
            }
        }
        catch (Exception ex)
        {
            LogService.Warn("Labels", $"Loading label settings threw: {ex.Message}");
            Failed(generation);
        }
    }

    private void Failed(int generation)
    {
        lock (_lock)
        {
            if (generation != _generation)
                return;

            _loading = null;
            _failedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    private static LabelText ToText(LabelValueDefinition def)
    {
        LabelValueDefinitionStrings? strings = def.Locales?.FirstOrDefault(s => s.Lang.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            ?? def.Locales?.FirstOrDefault();

        return new LabelText(
            string.IsNullOrWhiteSpace(strings?.Name) ? LabelDisplayPolicy.Humanize(def.Identifier) : strings.Name.Trim(),
            strings?.Description?.Trim() ?? string.Empty,
            LabelDisplayPolicy.ParseSeverity(def.Severity),
            IgnoredByDefault: string.Equals(def.DefaultSetting, "ignore", StringComparison.OrdinalIgnoreCase));
    }

    private static string PreferenceKey(string labeler, string label) => labeler + "|" + label;

    /// <summary>An immutable snapshot, swapped whole so readers on any thread never see a half-built one.</summary>
    private sealed class State
    {
        public static readonly State Empty = new(null, [], [], []);

        private readonly Dictionary<(string, string), LabelText> _definitions;
        private readonly Dictionary<string, string> _names;
        private readonly Dictionary<string, bool> _ignored;

        public State(
            IReadOnlyList<Did>? subscribed,
            Dictionary<(string, string), LabelText> definitions,
            Dictionary<string, string> names,
            Dictionary<string, bool> ignored)
        {
            Subscribed = subscribed;
            _definitions = definitions;
            _names = names;
            _ignored = ignored;
        }

        public IReadOnlyList<Did>? Subscribed { get; }

        public LabelText? DefinitionFor(string source, string value) =>
            _definitions.TryGetValue((source, value), out LabelText? text) ? text : null;

        /// <summary>A setting for this labeler's label wins over the account-wide one for a global value.</summary>
        public bool? IgnoredPreference(string source, string value)
        {
            if (_ignored.TryGetValue(PreferenceKey(source, value), out bool specific))
                return specific;

            return _ignored.TryGetValue(PreferenceKey(string.Empty, value), out bool global) ? global : null;
        }

        public string SourceName(string source) => _names.TryGetValue(source, out string? name) ? name : string.Empty;
    }
}

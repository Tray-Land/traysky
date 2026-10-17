# General enhancements

Remaining idunno.Bluesky API surface worth wiring up, grouped into chunks that can each ship on their own.
Work one chunk at a time, in order; tick the chunk heading (`[ ]` → `[x]`) and its tasks when done.

Conventions for every chunk (see CLAUDE.md / PLAN.md):
- Pure decision logic goes in `*Policy` / helper classes with no WinRT dependency, linked into `Traysky.Tests`, with tests.
- Add sample data to `PreviewMode` for any new screen so it renders without an account.
- Don't run `dotnet build/test/restore` unless asked.
- Commit on `main` when a chunk is finished.

---

## [x] Chunk 1 — Post menu: delete and mute thread
The "…" menu already has moderation and bookmarks; these are the obvious gaps. Both live in `PostInteractions` / `PostCard`.

- [x] `DeletePost` — own posts only (author DID == session DID), confirm dialog, the card collapses in place (works in every list since they all use `PostCard`).
- [x] `MuteThread` / `UnmuteThread` — menu item toggles by thread-mute state (`viewer.threadMuted`); update `PostItem` so the label flips.
- [x] `*Policy` for "which menu items apply to this post" (own vs. other, muted vs. not) + tests.

## [x] Chunk 2 — Post context: quotes
- [x] `GetQuotes` — "Quotes" entry next to the likes / reposts button; reuse `PostEngagementPage` (add a Quotes mode that lists quote posts as `PostCard`s rather than profiles).
- [x] Show the quote count on `PostItem` if the API returns it.

## [x] Chunk 3 — Manage blocks and mutes
- [x] `GetBlocks` and `GetMutes` — a screen (reachable from Settings) with two sections/tabs, cursor paging, reusing the `FollowListPage` row template.
- [x] Inline unblock / unmute with optimistic removal and undo on failure.
- [x] Hook into whatever `ModerationService` already caches so profile pages reflect changes immediately. (`ModerationService` caches nothing and profile pages read `viewer.muted` / `viewer.blocking` fresh on open, so there was nothing to invalidate.)

## [x] Chunk 4 — Search page
Today only hashtags have a search-ish surface (`HashtagPage`). Build a real search page.
- [x] `SearchPosts` (top / latest toggle), `SearchActors`, `SearchStarterPacks` as three tabs of one `SearchPage` + `SearchViewModel`.
- [x] Entry point: search box / button in the shell or Explore; submitting a `#tag` still routes to `HashtagPage`. (Search box at the top of Explore; multi-word trending topics now open the search page instead of bsky.app. `SearchQueryPolicy.AsHashtag` does the routing.)
- [x] Debounced typeahead for actors (`SearchActorsTypeahead` if available), remembered recent queries (settings).
- [x] Starter pack row → open pack detail (members list; a new `StarterPackPage` over `GetList` rather than `FollowListPage`, which is bound to followers/following). Follow-all is not done: the API has no batch follow, so it would be N `Follow` calls with partial-failure handling; left for a follow-up if wanted.

## [x] Chunk 5 — Profile enrichment
Builds on `ProfilePage` / `ProfileViewModel`.
- [x] `GetKnownFollowers` — "Followed by people you follow" line with avatar stack; click opens a `FollowListPage` variant. (The summary line and avatars come from `viewer.knownFollowers` on the profile we already fetch; `GetKnownFollowers` pages the full list on the `KnownOnly` variant of `FollowListPage`. Text built by `KnownFollowersPolicy`.)
- [x] `GetActorLikes` — Likes tab (own profile only; the API rejects other actors). (`ProfileTabPolicy` gates it.)
- [x] `GetActorFeeds` — Feeds tab (reuse the Explore feed row). (Row mapping moved to `FeedMapper.ToFeedSuggestionItem`, shared with Explore; opens bsky.app like Explore does.)
- [x] `GetActorStarterPacks` — Starter packs tab. (Rows open `StarterPackPage`.)
- [x] `GetSuggestedFollowsByActor` — "Similar accounts" section/expander under the header. (Loads on first expand; hidden on your own profile.)
- [x] `GetRelationships` / `GetProfiles` — batch lookups to replace per-actor calls where we currently loop. (Nothing loops: the only per-actor call is the single `GetProfile` for the profile being opened, and follow lists, notifications and search results are already hydrated by the list calls, so there was nothing to replace.)

## [x] Chunk 6 — List management
Only reading lists is wired up today.
- [x] `GetLists` — "My lists" screen (Profile → Lists tab for self). (A Lists tab on every profile, not just your own: `GetLists` works for any actor, and other people's lists open read-only. Reference lists are filtered out; they belong to starter packs.)
- [x] `CreateList`, `UpdateList`, `DeleteList` — name, description, purpose (curate vs. moderation), avatar optional. (New-list button on your profile, edit/delete in the list page's menu. Edit reads the record and writes it back with only the text changed, so an avatar set elsewhere survives. Purpose can't change after creation. Setting an avatar from Traysky is not done. Description links aren't made clickable: facets aren't extracted.)
- [x] `AddToList`, `DeleteFromList` — member management from the list page. (`ListPage`: typeahead box to add, Remove button per row with optimistic removal and undo on failure.)
- [x] `GetListsWithMembership` — "Add to list…" item in the profile "…" menu showing checkboxes per list. (Each tick applies immediately and reverts on failure. `ListPolicy` holds the validation, name limit (64 UTF-8 bytes), permissions and membership diff.)

## [x] Chunk 7 — Compose extras
- [x] Reply and quote controls: `AddThreadGate` / `AddPostGate` on post (everyone / nobody / followers / mentioned / following / specific lists), `UpdateThreadGate` / `UpdatePostGate` and `DeleteThreadGate` / `DeletePostGate` from the post menu on own posts. `*Policy` maps UI choices ⇄ gate rules + tests. (`InteractionGatePolicy` + `GatesService`. A shield button in the compose box sets them before posting; the gate records are written right after the post goes through, and a failure is reported without losing the post. "Reply and quote settings…" on own posts edits them later; reply controls only on thread roots. Updates keep hidden replies and detached quotes. Specific lists = your curation lists.)
- [x] Drafts: `CreateDraft`, `GetDrafts`, `UpdateDraft`, `DeleteDraft` — "Save draft" on closing a non-empty compose box, drafts list in the compose UI, restore text + attachments metadata (attachments that can't round-trip are noted, not silently dropped). (`DraftPolicy` + `DraftsService`. There is no "closing" event on the compose box, so a Drafts button opens a flyout with Save draft / Save and clear, the list, restore and delete. A draft keeps text, a quoted post and the reply / quote controls; picked images and video aren't stored, and the save and restore notes say so. Restoring then saving updates the same draft, and posting it deletes it.)

## [x] Chunk 8 — Notification control
- [x] `GetNotificationPreferences` / `PutNotificationPreferences` — Settings section mirroring the server-side per-type toggles (likes, reposts, follows, replies, mentions, quotes…). (`NotificationPreferencesPage` from a "Bluesky notification settings" card: per type, In your list / Push / Only from people I follow. Each toggle saves at once, sending back the loaded record with only these types changed so chat settings survive; a refused save puts the toggles back. `NotificationPreferencePolicy` holds the rows. Chat preferences and Unverified are left alone.)
- [x] `SetActivitySubscription` / `ListActivitySubscriptions` — bell button on profiles to get notified about an account's posts/replies; management list in Settings. (Bell on other people's profiles with Off / Posts / Posts and replies; `ActivitySubscriptionPolicy` decides when it shows, from the profile's `allowSubscriptions` and the follow state, and keeps it visible while subscribed so it can be turned off. `ActivitySubscriptionsPage` lists them with a Turn off button, optimistic removal and undo.)
- [x] Make sure `NotificationPollService` / `NotificationAnnouncer` handle the new activity-subscription notification reason (grouping policy + toast text). (`NotificationPollService` only reads the unread count so needed nothing. `SubscribedPost` was already a kind and never groups; it now gets its own toast ("Alice posted" plus the text, new "bell" toast setting, on by default), is hydrated like a mention in the Notifications list, and `NotificationGroupingPolicy.ToastVerb` holds the toast wording.)

## [x] Chunk 9 — Profile editing and pinned feeds
Both rewrite a whole record, so read-modify-write and preserve unknown fields.
- [x] `GetProfile` + `UpdateProfile` / `SetProfile` — edit display name, bio, avatar, banner from Settings (avatar/banner via `UploadBlob`; reuse `ImageCompressor`). (`EditProfilePage` from a Settings "Profile" card. Save re-reads the record with `GetProfileRecord` and changes only name, bio, avatar and banner, so pronouns, website, pinned post and self-labels survive. Pictures that are PNG/JPEG within 1 MB upload as picked; anything else is re-encoded down `ProfileEditPolicy.AvatarSteps` / `BannerSteps`. Each picture can be replaced, removed or undone. `ProfileEditPolicy` holds the grapheme/byte limits and the "anything changed" check. An account with no profile record at all can't be edited yet: `SetProfile` for that case isn't wired.)
- [x] `PutPreferences` — "Add to my tabs" / pin on feeds from Explore and the profile Feeds tab; reorder/unpin in Settings. Fetch current preferences first and only touch the saved-feeds entry so other preference types survive. (`SavedFeedsService` reads the preferences, replaces only the `SavedFeedPreferencesV2` entry and sends the rest back as read. Pin / Unpin button on every feed row; "Your feeds" page from Settings can pin, unpin, move pinned feeds up and down, and remove a saved one. An account with only the legacy saved-feeds record gets a v2 record derived from it. `SavedFeedPolicy` does the list edits, leaving unknown entry types untouched.)
- [x] Refresh `FeedsService` tab list after a change. (`SavedFeedsService.UpdateAsync` calls `FeedsService.RefreshAsync` after every successful save.)

## [x] Chunk 10 — Chat extras
Extends `ChatService` / `MessagesPage` / `ConversationPage`.
- [x] `MuteConversation` / unmute, `LeaveConversation` in the conversation menu. (A "..." button in the conversation header; `ChatConversationMenuPolicy` decides the items. Mute shows at once and reverts on failure; leave confirms, then drops the row and goes back.)
- [x] `ListConversationRequests` — "Requests" section in Messages with accept/decline. (Not a separate call: the list already includes requests, labelled, and `ListConversationRequests` is only needed to see group join requests, which are Chunk 11. Request rows got Accept / Decline buttons; decline is `LeaveConversation`, with optimistic removal and undo.)
- [x] `DeleteMessageForSelf` — message context menu. (The options button now ends your own messages too; "Delete for me" removes at once and puts it back on failure.)
- [x] `GetUnreadCounts` — badge on the Messages tab (and fold into the tray unread badge if `UnreadBadgePolicy` allows). (The tray fold was already in; the count and the access probe now come from `GetUnreadCounts` instead of paging `ListConversations`. The server decides whether muted conversations count.)
- [x] `GetConversationLog` — replace message polling with incremental log sync (cursor persisted per session); keep `ChatThreadPolicy` as the reducer for log events + tests. (`ChatService.SyncLogAsync` follows the saved cursor; `ChatThreadPolicy.Reduce` folds a batch into add / remove / mute / left / reactions / inbox-changed. Falls back to reading messages until the log hands back a cursor. Nothing here has been compiled or run yet: the log entry classes (`CreateMessage.Message` etc.) were taken from the library's XML docs.)

## [x] Chunk 11 — Group chats
Large API; do last and only after Chunk 10's log-based sync is in.
- [x] `CreateGroup`, `AddMembersToGroup`, remove member, rename, leave, lock/unlock as supported. (New-group button in the Messages header (name + people picker); `GroupInfoPage` from the conversation menu or by tapping the header: members with Remove, rename and Add people for the owner. Leave was already in the conversation menu. Lock / unlock: the library has no call for it, so a locked group is shown as locked (send box disabled, banner on the info page) but can't be changed from Traysky. `GroupChatPolicy` holds the name rules, owner permissions and member limit; `GroupChatService` the calls.)
- [x] `CreateJoinGroupLink` / join via link, deep-link handling in `DeepLinkPolicy`. (Owner: join link switch, approve-each-person, only-people-I-follow, copy code, and a Waiting to join list with Approve / Decline. Anyone: a Join a group button in the Messages header takes a pasted link or bare code, previews the group, then joins or requests to join. `traysky:join/{code}` opens the same dialog. Bluesky's own join-link address format isn't documented in the library, so the owner copies the code rather than a URL, and the paste box takes the last path segment of any address.)
- [x] Group rendering in `ChatMapper` (member avatars, system messages for joins/leaves). (System messages for add / remove / join / leave / rename / lock / join-link changes show as centred lines in the thread and in the list preview, "you" for the signed-in account. The group row keeps the name-initial picture; member avatars are on the info page, not stacked on the row. Group log events refresh the open group's name, size and lock state through `ChatThreadPolicy`'s new `GroupChanged`.)

Nothing in this chunk has been compiled or run. Library result types for the group calls weren't in the XML docs, so `GroupChatService` reads only `Succeeded` / `StatusCode` and re-fetches the group with `GetConversation` instead of unwrapping their results; if a signature differs, that file is where it will show.

---

## Deferred / low value
Not planned unless a need shows up.
- `GetTrendingTopics`, `GetSuggestions`, `GetTaggedSuggestions` — overlap what Explore already uses.
- Raw record methods (`GetBlueskyRecord`, `ListBlueskyRecords`, `CreateBlueskyRecord`, the `Get*Record` family) — plumbing.
- `StartUpload`, `UploadPart`, `FinishUpload`, `AbortUpload` — the steps beneath `UploadVideo`.
- [x] `BlockModList`, `MuteModList`, `UnblockModList`, `UnmuteModList` — Mute list / Block list buttons on the list page of any moderation list (yours or someone else's), optimistic with revert on failure and a confirm before blocking. State comes from the list's `viewer` (`ListPolicy` holds labels and wording). Not compiled or run: `ListView.Viewer` and the four agent calls were taken from the library's XML docs. Profile Lists tab rows don't show the muted / blocked state.

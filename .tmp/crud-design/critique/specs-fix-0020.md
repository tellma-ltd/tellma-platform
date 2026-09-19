# Fix record — spec 0020

Review A has no "## Spec 0020" section; its only 0020 mention (Cross-spec, `core.NotificationPreferences` cascade) reports no drift in 0020 (0011 §4.1 lags).

## Applied

- B-0020-1 (§2.3 type-key grammar): replaced the regex with spec 0019's `^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+$`; §6.3 keeps citing §2.3, so one grammar now governs types and event names.
- B-0020-2, index name only (§2.1): `IX_Notifications_UserId_CreatedAt` renamed `IX_Notifications_User` to match seams.md §15.1 (names must equal the seam).
- B-0020-3 (§6.3 `cache.changed` publisher): row reworded to name 0012's epilogue contributor and its `Publish(batch, …)` call, which 0012 §2.5 does state; the "drop the row" alternative rejected because the publication exists.

## Rejected / no change in 0020

- B-0020-2, remaining items (`Seen() -> AffectedResult`, `NotificationPreferencesService.Get`, `NotificationsOptions`, `TellmaRealtimeOptions`, `RealtimeTelemetryNames`, `NotificationTypeView`, `NotificationPreferencesResult`, `ClientEventEnvelope`, the two registries): owner additions with no consumer restating them; the fix is seams.md §14/§15 absorbing them, as the reviewer directs.
- B-0020-4 (§6.2 `t{tenantId}.s{subject}` / `x{sessionKey}` groups, §6.5 close rules): required by spec 0010's listener signatures; fix is seams.md §20, per the reviewer.
- Cross-spec B-0015-3 (`notification-preferences/get` missing from 0015 §2.4): 0020 §4.3/§8 already list it; change belongs to 0015 and seams.md §13.1.
- Cross-spec B-0017-10 (`core.user.added` arguments, duplicate registration): 0020's catalogue `{ tenantName, actorName }` and its `CoreFeature` declaration stand; 0017 passes `actorName` and drops its registration line.
- Cross-spec B-0018-4 (export/import argument names): 0020 owns the catalogue and keeps `{ fileName, rowCount }`, `{ fileName, rowCount, errorCount }`, `{ fileName, errorCode }`; 0018 passes those names.
- Cross-spec B-0019-5 (no-op `INotifier`): 0020 §1.1/§6.4 already define only `NullClientEventPublisher`; 0019 restates the composition.

# Follow-up 4 (spec 0026 feedback) — carriers outside spec 0026

Scope: docs/specs/0010–0035 except 0026, plus ARCHITECTURE.md. Spec 0026 was not touched (edited in
parallel). Nothing staged or committed. Edited files stay LF, no BOM (`git ls-files --eol`: w/lf).

## Changes

### docs/specs/0017-blob-storage.md
- §1.3 Core kinds table: row `support-bundle` / `SupportRequest.BundleId` → `support-screenshot` /
  `SupportRequest.ScreenshotId` (spec 0026 §12.4), preset `Attachment`, `OwnerRead` (F-3).
- §1.3 narrowing sentence: "spec 0026 §12.4 narrows `support-screenshot`'s policy". Still "two
  kinds are narrowed"; the narrowing details (image types, 8 MiB, thumbnail kept) stay in 0026.
- §5.2 read-access bullet: `support-bundle` → `support-screenshot`; the rest (OwnerRead through
  `core.SupportRequest`, submitter and `× Read` holders) unchanged.

### docs/specs/0016-crud-web-api.md
- §2.1 route table, blob upload row: "the `support-screenshot` kind included (spec 0026 §12.4)".
- The `support-requests/{**operation}` row is unchanged: `query`, `get`, `get-by-ids`, `delete`,
  `delete-by-query`, `submit` is still the operation set (see "Dump read" below — no new action).

### docs/specs/0011-distribution-host-and-multitenancy.md
- §5.9 admin route row: "The support-request reads (spec 0026 §12.7): the list, a request's dump
  and its screenshot; each read is logged as `SupportRequestRead`." (was "each detail or file read",
  which named a bundle-era detail/file split; F-3 says each read logs it).
- §9 Alerts bullet: the checked-in alert queries include "the client alerts of spec 0026 §6.4 and
  its support-request alert (spec 0026 §12.3)" (F-1).
- Dependency list: "spec 0026 §6, §12.3 and §12.7 — the client telemetry relay, the
  support-request alert (§9) and the admin support-request routes `MapTellma` maps (§5.1, §5.9)".

### docs/specs/0021-notifications-inbox-and-hub.md
- Goals: "the eleven Core types" → "the twelve Core types".
- §2.3 `Category` row: Core categories now `jobs`, `files`, `users`, `reports`, `support`.
- §2.3 Core catalogue: new row after `core.scheduler.gap`:
  `core.support.requested` | `support` | false | `Warning` | true | `core.SupportRequest` |
  "spec 0026 §12.3's `submit`, for the tenant's administrators other than the sender
  (`DedupKey = 'core.SupportRequest:<id>'`)" | `{ id, actorName }`.
  The table has no `Inbox` column (default true), so `Inbox = true` is implicit; `Suppress` is the
  default policy, so it is not written (matches the `core.dashboard.shared` row).
- §2.3 paragraph after the table: the raisers' list gains "spec 0026 §12.3" (duplicate policy and
  withdrawal are the raiser's). Rewrapped to 100 columns. No change to "The one exception is
  `core.user.added`": `core.support.requested` is declared by the code that raises it (Core's
  `submit`, contributed by `CoreFeature`), as `core.dashboard.shared` and `core.report.shared` are.

## Verified, no change needed

- **spec 0014** §2 securable constant `SupportRequest = "core.SupportRequest"; // Read, Delete;
  spec 0026 §12.6` — still true. §10 `IAdministratorDirectory.GetAdministratorIdsAsync()` exists
  (active individual members of role 1, Invited or Joined, ordered by Id, capped at 20), so F-1's
  recipient rule cites a real contract.
- **spec 0016** §10.1: "logs carry tenant ids; metrics never do" — confirms F-1's
  no-tenant-tag rule. §1.3 `MaxJsonBodyBytes = 8 * 1024 * 1024` (8 MB) on the `Web` group, so a
  2 MiB dump inline in `submit` fits (F-3 "verify"). `support-requests/submit` stays in the
  never-retried list and the action table.
- **spec 0018** §6.2 (`Tellma:Support:Enabled = true` in Acme Development) and §6.x
  InitialTenant table list (`SupportRequests` named without columns) — nothing stale; the new
  columns ride the same table.
- **spec 0020** §14.4 retention: `core.file-retention` deletes expired `SupportRequest` rows
  through `DeleteByIdsAsync`, so the `[BlobReference]` release effect runs — that now releases the
  screenshot, and the dump goes with the row. Wording is generic and stays correct. §16 test
  bullet likewise.
- **spec 0024** §1 lazy-group row (`/support | dump, screenshot, support dialog`), §11 5xx dialog
  ("Copy diagnostics", "Report this problem" via `TmSupport.open({ error })`), the on-demand chunk
  row — no bundle or page-data-default wording.
- **spec 0027** §10.1 session menu row "Report a problem" → `TmSupport.open()` and Decision 11 —
  unchanged.
- **spec 0028** §10.3 Notification settings page: one card per category "in the metadata's order",
  titled `NotificationCategory_<Category>` — generic; no category list, so `support` needs no
  entry. Notification rows open their target via `TmUrls.target(…)` (§10.1), generic — no
  per-resource route table to extend.
- **spec 0029** §2.3: `[JsonColumn]` properties appear with `queryable: false`, "for details
  forms" — consistent with `DumpJson`.
- **spec 0035** §16 masking line "Snapshots without page data mask them (spec 0026 §9.6)" —
  neutral about the default, still true.
- **spec 0025** "page data" hits are about page-data fetching, unrelated.
- **ARCHITECTURE.md**: "support requests" in the Tellma.Core row, "support dump" in the Page
  context glossary row and line ~883, the meter-naming paragraph (lists `Tellma.Core` prefixes
  with "…") and the client-telemetry paragraph ("Client items carry no tenant content and no free
  text" — about telemetry items, still true) — nothing stale.

## Dump read via the stack (F-3 "verify", for the spec 0026 agent)

Spec 0015 §5.3 step 2: the details read loads `EntityQuery { Select = every mapped property, … }`,
and spec 0016 §3.4 says `entities` carry "every property". So a plain `get` (details read) returns
`DumpJson` with no `select` at all; an explicit `select=DumpJson` would only feed the row echo
(step 4), which goes through the Queryex schema that omits `[JsonColumn]` (spec 0012 §2.6) and
would fail. Precedent: spec 0020's `Schedule.ArgumentsJson` — "`[DefaultSelect]` of every column
but `ArgumentsJson` … the details read carries it (spec 0015 §5.3)". Conclusion: no read action is
needed; the details page's `get` carries the dump, and `query` (the list) can never select it.
The spec 0016 route row therefore needs no new operation.

## Greps run (docs/specs/001*–003*, ARCHITECTURE.md, excluding 0026)

| Pattern | Result |
|---|---|
| `support-bundle` | clean |
| `support bundle` | clean |
| `BundleId` / `bundleId` | clean |
| `HasScreenshot` | clean |
| `SupportRequest` | 0011, 0014, 0017, 0018, 0020, 0021 — all current (admin route, securable, kind row, table list, retention, new catalogue row) |
| `support-requests` / `SupportRequests` | 0011 admin route, 0016 route + action tables, 0018 table list — current |
| `core.SupportRequest` | 0014, 0017, 0021 — current |
| `Include the data shown` | clean |
| `page data` | 0024 (request cancellation table, unrelated), 0025 (unrelated), 0035 (masking, neutral) |
| `TmSupport` / `tm-support` | 0024 §11 and 0027 §10.1 only, unchanged API calls; `tm-support` clean |
| `support dialog` | 0024 lazy-group row only, current |
| `NotificationCategory_` / `Core categories` | 0021 (updated), 0028 (generic) |
| `NotificationType_`, `Notification_core_` | 0013 §10.1 lists only report/dashboard notification keys (area list, not a catalogue); 0022 its own; no change |
| `eleven` / Core-type counts | 0021 Goals updated; others unrelated |
| `TelemetryNames`, `tellma.client.`, `tellma.users.invites` | no aggregated Core instrument table exists outside each owner spec; nothing to add |
| `infra/monitoring` | each spec lists its own instruments' alerts; only 0011 §9 names spec 0026's alerts — updated |
| `bundle` (word, near support/dump/screenshot) | clean |
| `16 MiB`, `16_777_216`, `10.7 MiB` | clean outside 0026 |

## Deviations

- Cited spec 0026 §12.3 for both the meter's alert (0011 §9) and the notification row (0021).
  At the time of this pass spec 0026 had not yet been updated by the parallel agent; if the meter
  or alert lands in another subsection (e.g. a new §12.x or Appendix A), the 0011 citation needs
  that number.
- 0011 §5.9 row reworded beyond a token swap: it now names the three reads, since the
  "detail or file" split it described no longer exists.

## Notes for Ahmad

1. **Resource audit gap (pre-existing).** Spec 0013's resource-audit convention list (§12
   Testing, "`Notification_`, `NotificationType_`, `NotificationChannel_`, …") does not list
   `NotificationCategory_`, so `NotificationCategory_jobs` etc. — and now
   `NotificationCategory_support` — would be orphans unless they appear as literals. Not caused by
   this round; left alone.
2. **Withdrawal on delete.** Deleting a support request (administrator Delete, or retention) leaves
   any unread `core.support.requested` rows pointing at a gone `core.SupportRequest`. Whether
   `delete` / `core.file-retention` should `Withdraw` them is spec 0026's call (spec 0021 says the
   withdrawal trigger is the raiser's); flagged for the 0026 agent / you.
3. **Spec 0013 area list.** Spec 0013 §10.1 lists the report and dashboard notification keys but
   not spec 0026's (`Notification_core_support_requested`, `NotificationCategory_support`,
   `SupportRequests.*`). The list is not exhaustive (spec 0022's keys live in 0022 too), so 0026
   owning its keys is consistent; not changed.

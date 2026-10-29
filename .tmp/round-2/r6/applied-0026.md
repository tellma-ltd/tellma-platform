# Applied: spec 0026 feedback (F-1, F-2, F-3)

File edited: `docs/specs/0026-client-observability-and-support.md` only. 1240 → 1308 lines; the
growth is the new notification table, the meter sketch, the Sent-text variants and the alert
bullets (new requirements), plus Decision 11.

## Changes by section

- **Context.** The "no tenant content leaves the tab" sentence restated: tenant content and free
  text leave only in a support request, page data only while the box stays ticked. Builds-on list
  adds spec 0021. Carrier list: spec 0011 §9 now also "the alerts"; spec 0017 entry renamed to the
  `support-screenshot` kind; new entry "spec 0021 §2.3 — the `core.support.requested` type and the
  `support` category".
- **Goals.** "Support in one send" → one row, a blob only for the screenshot, opt-in, the people
  who act on it alerted.
- **§1 Placement.** `TmSupportBundleSummary` → `TmSupportDumpSummary`; `SupportTelemetryNames` added
  to `Tellma.Core.Abstractions.Api`; "two admin support routes" → three; `Tellma.Core` row: the
  `support-screenshot` transform, the `core.support.requested` type,
  `Notification_core_support_requested`, `NotificationCategory_support`. Vocabulary: **Bundle**
  entry removed.
- **§10 intro.** The dump is stored in the support request's row (§12.3).
- **§10.1.** `screenshot` member removed from `TmSupportDump`. Example caption "page data left out".
- **§10.2.** Rewritten: on by default; the box starts ticked on every open, never remembered;
  Ticked / Unticked bullets; "Download diagnostic file" outside the dialog never includes page
  data; Download instead follows the box.
- **§10.3.** "not counting `screenshot`" removed; the cap is also enforced by `submit`.
- **§12 intro.** Screenshot (when present) is the only blob; the row holds the dump;
  administrators get a bell notification, the provider an alert.
- **§12.2.** Compose form: the box "ticked on open (§10.2)". Warning rewritten for both
  `providerAccess` variants: names the page data, tells the user to untick or not send; the
  page-data clause drops while unticked. Sent row: "Your request number is 1042." plus who was
  alerted; new "The Sent text" bullet with the four variants (both / administrators only /
  `providerAccess` only / neither). Send flow: (1) build dump, (2) upload `support-screenshot` only
  when there is a screenshot, (3) `submit` with the dump inline and `screenshotId`; Try again
  re-sends only the failed step; staged screenshot attachable 24 h. Example submission: `dump`,
  `screenshotId`, `includesPageData: true`; receipt `{ id, administratorsNotified }`.
- **§12.3.** `[ApiResource]` comment updated. `[DefaultSelect]` names `ScreenshotId` instead of
  `HasScreenshot`. Entity: `HasScreenshot` and `BundleId` removed; `[JsonColumn] DumpJson` and
  `[BlobReference("support-screenshot", MaxSizeBytes: 8_388_608)] int? ScreenshotId` added.
  `SupportRequestSubmission(..., JsonElement Dump, int? ScreenshotId)`.
  `SupportRequestReceipt(int Id, bool AdministratorsNotified)`. New `SupportTelemetryNames`
  sketch (`MeterName = "Tellma.Core"`, `Requests`, `ScreenshotTag`, `PageDataTag`). Bullets:
  `submit` stores the dump's raw text, the 8 MB JSON body limit admits it; "Reading the dump" (the
  JsonColumn answer below); one transaction, now confirming the screenshot; administrators
  notified in that transaction; after-commit meter and `SupportRequestSubmitted` log; the
  provider's alert under `infra/monitoring/`. New notification-type table. Codes: `Required` row
  removed; `SupportRequests.DumpInvalid` and `SupportRequests.DumpTooLarge` added at `dump`;
  `Blob.NotAttachable`/`Blob.DuplicateReference` moved to `screenshotId`.
- **§12.4.** Retitled `support-screenshot`: declared by `ScreenshotId`, `Attachment`, 8 MiB,
  `OwnerRead`; transform narrows to PNG/JPEG/WebP and keeps the thumbnail; "always fits" via
  §11.1's redraw; upload route renamed.
- **§12.5.** List column `HasScreenshot` → `Screenshot` (the blob navigation); see deviation D-3.
  Details: buttons Download diagnostic file and Download screenshot. "Bundle summary" →
  "Dump summary" (`tm-support-dump-summary`, `TmSupportDumpSummary`): parses the `get`'s
  `dumpJson`, loads the screenshot thumbnail through the blob GET.
- **§12.6.** "The bundle" → "The screenshot".
- **§12.7.** Three routes (list, `/dump`, `/screenshot`); `AdminSupportRequest` drops
  `BundleSize`, gains `long DumpBytes` (DATALENGTH), keeps `HasScreenshot` (computed). Reads
  bullet rewritten (see D-4); 404 for a request without a screenshot on the screenshot route;
  paging example reads the dump and the screenshot.
- **§12.8.** Screenshots released; the dump goes with the row.
- **§13.** Capture/support dialog row: box ticked on every open, send with/without screenshot,
  warning per `providerAccess` and the box, Sent text per `administratorsNotified` and
  `providerAccess`. C# support row: dump stored as sent, screenshot attach and submit without one,
  details `get` carries `DumpJson` and a query cannot select it, notification recipients (sender
  excluded, sole-administrator case), `administratorsNotified` both ways, the meter with both tags
  once per committed submit, `SupportRequestSubmitted`, rolled-back submit records nothing, three
  admin routes and the screenshot 404, retention releases screenshots. Playwright: an
  administrator's bell shows the notification.
- **§14.** Carrier list adds spec 0021.
- **Decisions.** 5 rewritten (dump in the row, screenshot alone in a blob); 8 rewritten (page data
  on unless unticked; download outside the dialog never; bare path without it); new 11 (alerting).
- **Review flags.** 2: on by default; alternative off; flips on a privacy review or a customer that
  forbids it. 5: dump in the row, screenshot in a blob; alternative the dump as a second blob;
  flips if dumps grow the tenant database noticeably.
- **Appendix A.** No change needed: `tm.support.sent`/`send-failed` keep `stage ∈ upload, submit`
  (the upload stage now exists only with a screenshot).

## The JsonColumn question: can a details `get` read `DumpJson`?

Yes, with no `select` and no read action. A `select=DumpJson` is impossible and unnecessary.

- spec 0015 §5.3 step 2: the details plan's entity query selects "every mapped property"; `select`
  on a `get` is only the row echo (step 4), a Queryex text.
- spec 0016 §3.3: `GetRequest(Id, Select, Include)`, `Select` is the row echo; §3.6: `entities` are
  "whole entities in wire shape: every property".
- spec 0012 §2.6 and §11.1: `[JsonColumn]` strings are absent from the Queryex schema, so a
  `select`, a query, or a `[DefaultSelect]` naming `DumpJson` fails validation (spec 0015 §2.6
  checks `[DefaultSelect]` texts against the schema at startup). Lists therefore never carry it.
- spec 0020 §(Schedule) "`[DefaultSelect]` of every column but `ArgumentsJson`, which
  `[JsonColumn]` keeps out of the Queryex schema (spec 0012 §2.6); the details read carries it
  (spec 0015 §5.3)" — the same pattern, already pinned.
- spec 0029 §2.3: `[JsonColumn]` properties appear in client metadata with `queryable: false`,
  "for details forms".
- spec 0033: `DefinitionJson` has no separate read path; it rides the stack's details read (and
  the engine's server-side load, §8.3 step 1).

Design: the details page's `get` carries `dumpJson`; `TmSupportDumpSummary` parses it. The admin
`/dump` route uses the details read too.

## Deviations from decisions.md

- **D-1 (F-1, notification raise site).** decisions.md says "raised through `ActionContext.Notify`".
  `submit` is an `[ApiAction]` with an enlisted self-save (spec 0015 §11.3, §13.3); it has no
  `ActionContext` (that belongs to `[EntityAction]`s, spec 0015 §11.2). The pipeline's notify seam
  for this path is `PersistContext.Notify` in the stack's `ContributeAsync` hook, which runs for
  the enlisted save's group with final ids (spec 0015 §13.1, §13.3). Written that way: `submit`
  resolves the administrators before it enlists, the hook raises the request on the persist batch.
  Same transaction and after-commit `inbox.changed`, as pinned.
- **D-2 (F-1, meter site).** decisions.md says "from the batch's post-commit callback". Written as
  "once `PersistAsync` returns": for an `[ApiAction]` host, `PersistAsync` returns after commit
  (spec 0015 §13.3), so a rolled-back submit throws before reaching it. Same guarantee, simpler; the
  log event and `AdministratorsNotified` come from the same place.
- **D-3 (F-3, list column).** decisions.md says the list's column becomes a screenshot thumbnail
  column. Spec 0029 §4.3 ends a blob navigation as `image` only when its policy re-encodes images
  (`policy.image` set); the `Attachment` preset has no `Image`, so `Screenshot` is a `file` column
  (spec 0029 §6.1: the file name as a download link). The spec says so; the thumbnail shows on the
  details page's summary instead. See Notes.
- **D-4 (F-3, admin list).** decisions.md keeps "the stack's query" implicitly and adds
  `DumpBytes` (`DATALENGTH`). Queryex cannot reach `DumpJson`, so the stack's query cannot produce
  `DumpBytes`. Written as: the list is one read statement; the dump route uses the details read;
  the screenshot route uses `ResolveAsync`.
- **D-5 (F-3, transform).** decisions.md has the transform set `MaxSize` 8 MiB as well. The
  attribute's `MaxSizeBytes: 8_388_608` already narrows `MaxSize` (spec 0017 §2.3), so the
  transform only narrows `AllowedContentTypes` and keeps the thumbnail.
- **D-6 (F-1, arguments).** "actorName = the sender's name, following the `core.dashboard.shared`
  pattern": that pattern (spec 0035 §7.2 → spec 0033 §7) sends one request per content slot of the
  recipients' languages. Written that way, "one request per slot (as spec 0033 §7)".
- **D-7 (F-2, warning).** The warning's "the data shown on the page and" clause drops while the box
  is unticked, so the text never claims page data that is not being sent.
- **Not done here:** spec 0021 §2.3's row and `support` category, spec 0017, 0016 and 0011 carrier
  edits were outside this task's one-file scope; they are already present in the working tree
  (another step's edits) and match the names used here (`support-screenshot`, `ScreenshotId`,
  `core.support.requested`, `{ id, actorName }`, the `/dump` and `/screenshot` routes, spec 0011
  §9's support-request alert). Spec 0028 §10.3 reads categories from metadata, so it needs no list
  change. Spec 0020 §14.4 needs no change (it already deletes `SupportRequest` rows through the
  stack, so the screenshot release effect runs).

## Notes for Ahmad

1. **Thumbnail in the list (D-3).** If you want the list cell to show a thumbnail, either give the
   kind an `ImagePolicy` (re-encoding screenshots, e.g. PNG at a large max dimension), or amend
   spec 0029 so a `file` navigation whose policy has `thumbnail: true` renders the thumbnail. The
   second is the general fix; I'd lean to it.
2. **Cross-spec gap: entity queries and `[JsonColumn]`.** Spec 0012 §11.4 builds entity queries by
   running Queryex `DiscoverQuery` on the select text, yet spec 0015 §5.3's details plan selects
   "every mapped property", which includes `[JsonColumn]` strings the Queryex schema omits. Spec
   0020 (`Schedule.ArgumentsJson`), 0033 (`DefinitionJson`), 0035 (`ParametersJson`,
   `SettingsJson`) and now 0026 (`DumpJson`) all rely on the details read carrying them. Spec 0012
   should state how the entity query loads mapped columns outside the Queryex schema.
3. **Recipients reaching the hook (D-1).** `submit` resolves recipients (async) before enlisting,
   and the `ContributeAsync` hook (append-only, no I/O) raises the request. The spec leaves the
   hand-off between the two to the service's scoped state. If you want an explicit seam, an
   `OnPersist` on `EnlistSaveOptions` (as `SaveOptions.OnPersist`) would let the `[ApiAction]`
   append `INotifier.Notify` directly; that is a spec 0015 change.
4. **`fileName: 'screenshot.png'`** is used for attached JPEG/WebP images too. The content type is
   sniffed (spec 0017 §3.3), so storage is correct; only the download name's extension can be
   wrong. Say if you want `screenshot.<ext>` by type.
5. **`DumpBytes` is `DATALENGTH`,** i.e. UTF-16 bytes of `nvarchar(max)`, roughly twice the UTF-8
   size the 2 MiB cap measures. Fine for triage; say if you want the UTF-8 size stored instead.

## Fix pass

Files edited: `docs/specs/0026-client-observability-and-support.md` (1308 → 1330 lines; growth is
the delete-withdraws bullet, two truncation steps and the scope/state rule of §12.7) and
`docs/specs/0011-distribution-host-and-multitenancy.md` (§3.3 state table and §4.4: the admin
support-request reads are an `allowNonActive` caller). ARCHITECTURE.md needed nothing.

| Finding | Verdict | What / why |
|---|---|---|
| V-R-1, X-F4 | applied (merged) | `NotificationType_core_support_requested` ("Problem reports") added to §1 and the Text row; resource home is Core's `Strings` (as spec 0022 §9, spec 0021 §4.3). |
| V-R-2, X-F5, S-S1-F2 | applied (merged) | `ActorUserId` is the sender. Data source per S-S1-F2, not V-R-2's extra read: `ValidateAsync` adds `ByIds<User, int>` over recipients + sender (`Id,Name,Name2,Name3,PreferredLanguage`) to the enlisted save's validation round (spec 0015 §7.2, §13.3: enlisted validators join the round), so "two round trips besides the administrator lookup" stays true. The hook groups by content slot (slot 1 when unset). |
| V-R-3, S-S7-F13 | applied (merged) | New bullet "A delete withdraws": the hook on `DeletePersistContext` withdraws `core.SupportRequest:<id>` (spec 0021 §3.5); covers retention because spec 0020 §14.4 deletes through `DeleteByIdsAsync`. §12.8 and the C# test row updated. |
| V-R-4, X-F1, S-S4-F7 | applied (merged) | Ticked: "…untick 'Include the data shown on the page' or don't send." Unticked: the page-data clause drops and the continuation becomes "If the screenshot shows something you may not share, remove it or don't send." Nested label uses single quotes. |
| V-R-5, X-F2 | applied (merged) | §10.2: "Without the box" a download never has page data (Diagnostics panel, Download only state); the compose form's View, Download and Download instead follow the box. Download only row says so. §12.5 button is "(the stored dump)". Decision 8: "downloaded without the box". |
| V-R-6, X-F3 | applied (merged) | Context: telemetry never carries tenant content or free text; they leave only through the support dialog, sent or downloaded there; page data only while ticked. |
| V-R-7, S-S1-F17 | applied (merged) | §11.1: redrawn in its own type at half the dimensions, repeatedly, until ≤ 8 MiB. §12.4 "Always fits" points at it. Capture test: oversize needing two halvings. |
| V-R-8, S-S1-F1 | applied (merged) | `<ext>` (`png`/`jpg`/`webp` by type) defined once in §11.2's preview download; §12.2 upload uses `'screenshot.<ext>'`. Dialog test row names it. |
| V-R-9 | applied | `DumpInvalid` also when the dump's `includesPageData` differs from the submission's. |
| V-R-10 | applied | `id` is an invariant-culture string, so ICU does not group it. |
| V-R-11, X-F7 | applied (merged) | §12.5 and §12.7 rationale trimmed; `DumpJson` comment "capped by §10.3"; "The Sent text continues with…"; compose-form bullet rewrapped. |
| X-F6 | applied | Sending row: upload progress only when there is a screenshot. |
| S-S1-F3 | applied | Receipt comment "true: recipients besides the sender"; the bullet says `AdministratorsNotified` is computed from the recipient list. Not the post-commit `OUTPUT` variant. |
| S-S2-F4 | rejected | The four Sent variants are Ahmad's pinned copy (F-1). The alert ships with the platform's `infra/monitoring/` and `providerAccess` is the pinned proxy; softening it would undo the decision's intent ("someone will be alerted"). Noted for Ahmad below. |
| S-S3-F5 | applied | Try again re-uploads the screenshot first when `submit` is refused at `screenshotId` (expired staging). Failed row: errors through `TmServerText.error`, else the problem title (spec 0024 §7.5). Test row names the re-upload. |
| S-S3-F6 | applied (alternate) | The daily count is a loader `Count` in `ValidateAsync` on the same validation round (no extra round trip), with "midnight in the tenant zone (spec 0016 §6.4)". Not a persist-time `THROW` assertion: a validator is the pipeline's place for a rule answered at root, and a race of two concurrent submits past a soft daily limit is harmless. |
| S-S4-F8 | rejected (superseded) | V-R-9 enforces the agreement in one table cell without changing the wire shape; the `PageUrl` query rule is enforced server-side in the "builds one row" bullet (S-S4-F18). Removing two members while `UiVersion`, `RouteTemplate`, `TraceIds` also duplicate the dump would be inconsistent. |
| S-S6-F10 | applied | Screenshot route reads `ScreenshotId` through the details read, then `ResolveAsync`; the system user's `Read` is unrestricted (`UserAccess.System`, spec 0014 §5.1). |
| S-S6-F11 | rejected | No wrong-request failure: the `/dump` and `/screenshot` routes take `requestId` directly and 404 a deleted request, and the list row carries its `Id`, so a `before=1043&take=1` answer of 1041 is visible. A fourth route adds surface for no gain. |
| S-S6-F12 | applied | §12.7: scopes opened with `allowNonActive: true`, so `ReadOnly` tenants stay readable; `Suspended` 403 `tenant-suspended`; `Retired`/unknown 404. Spec 0011 §4.4 names these reads as the second `allowNonActive` caller; §3.3's table says "an `allowNonActive` caller". The existing §10 factory test (each state, with and without the flag) already covers it. |
| S-S8-F14 | applied (alternate) | No runtime visibility hook exists for a `tmEntity` entry (spec 0027 §3.2 is permission-only). §12.5 tells a distribution that keeps support off to `hide: ['entry:core.SupportRequest']` (spec 0024 §2.3). See note. |
| S-S9-F15 | applied | §10.3 gains step 3 (the rest of the page data: contexts with `includeData: false`, bare `page.url`, `includesPageData` false) and step 4 (`sections`); "What remains after step 4 is bounded well under the cap." No new ceilings pinned in §9.6. Dump test: the four steps in order. |
| S-S9-F16 | applied | §10.3: measured as UTF-8 bytes of the compact JSON sent; §12.3: the raw text as received is what is stored and measured. |
| S-S4-F18 | applied | `UserAgent` cut to 512; `PageUrl` loses its query when `IncludesPageData` is false or it is over 2,048, then is cut. C# test row names it. |

Greps over docs/specs/0010–0035 and ARCHITECTURE.md for `support-bundle`, `BundleId`, `bundleId`,
`HasScreenshot`, `support bundle`, `BundleSize`, `TmSupportBundleSummary`: clean except
`AdminSupportRequest.HasScreenshot` (spec 0026 §12.7), which decisions.md F-3 keeps on purpose.
No BOM, no CRLF in any of those files.

### Notes for Ahmad (fix pass)

1. **Automatic entry hiding (S-S8-F14).** The Support requests entry still shows when the
   distribution has support off unless it `hide`s it. An automatic rule would need a runtime
   visibility condition on navigation entries (spec 0027 §3.2), a cross-spec change; say if you
   want it.
2. **Provider "has been notified" (S-S2-F4).** Kept as you worded it. It is true only where the
   `infra/monitoring/` alert is deployed; an on-premises operator who maps the admin surface but
   routes no alert makes the copy optimistic. The softer alternative is "…can see it."
3. **Step 3 of the truncation** flips `includesPageData` to false on a box the user left ticked;
   the request then shows "Includes page data: no", which is accurate but may surprise. The dump's
   `truncated` names `pageData`.
4. **Daily limit is soft:** counted in validation, so two concurrent submits at the limit can both
   pass. Acceptable for a 20-a-day abuse guard; a persist-time assertion would make it exact.

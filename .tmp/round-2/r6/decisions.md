# Spec 0026 feedback (round 2, review round 1, follow-up 4) — pinned decisions

Ahmad's feedback, verbatim:

1. Section 12: When the user reports a problem, someone should be alerted. Add a
   tellma.support.requests meter, also send a bell notification to the tenant administrators.
   Users intuitively expect that "Report a problem" implies someone will be alerted.
2. Review flag 2: Include data ON by default.
3. Review flag 5: Neither, only the screenshot goes in the blob, the rest of the bundle is stored
   in a SupportRequests column (nvarchar(max) JSON) that is kept off the default select.

Below are the orchestrator's pinned designs. Apply them; where a detail below conflicts with a
fact you verify in another spec, follow the verified fact and report the deviation.

## F-1. Alerting

### The meter (provider side)

- `tellma.support.requests`: counter, unit `{request}`, on the `Tellma.Core` meter.
- Constants: `SupportTelemetryNames` in `Tellma.Core.Abstractions.Api`, beside `SupportOptions`
  (follow the shape of the other `*TelemetryNames` classes: `MeterName`, the instrument name, the
  tag-key constants).
- Tags (closed sets): `screenshot` ∈ `true | false`, `page_data` ∈ `true | false`. No tenant or
  user tag (spec 0016 §10.1's rule: metrics never carry tenant ids).
- Recorded once per committed `submit`, from the batch's post-commit callback, so a rolled-back
  submit counts nothing.
- Log event `SupportRequestSubmitted(TenantId, RequestId)` at Information, after commit. The
  logger scope already carries the tenant (spec 0011 §9). This is what lets the provider's support
  team go from an alert to "request 1042, tenant 17".
- Alert query under `infra/monitoring/`: any increase of `tellma.support.requests` notifies the
  provider's support team. It belongs with the other client alerts (§6.4) and spec 0011 §9's
  alerts. On-premises, the operator owns the alert's routing.

### The bell notification (tenant side)

- A new Core notification type, declared by `CoreFeature` and raised by `submit`:
  - Key `core.support.requested`; Category `support` (a new Core category; label resource
    `NotificationCategory_support`); `Mutable = false`; `Severity = Warning`; `Toast = true`;
    `Inbox = true`; `TargetResource = core.SupportRequest`, `TargetId` = the request's id.
  - Arguments `{ id, actorName }` (actorName = the sender's name, following the
    `core.dashboard.shared` pattern). Never the message.
  - Resource `Notification_core_support_requested` in Core's resx, e.g.
    "{actorName} reported a problem (request {id})."
  - `DedupKey = 'core.SupportRequest:<id>'` (spec 0021's "activity about a record" key), default
    `Suppress`.
  - Recipients: `IAdministratorDirectory.GetAdministratorIdsAsync()` (spec 0014 §10) minus the
    sender. Empty → no request appended.
  - Raised through `ActionContext.Notify` on the persist batch, so the row commits with the
    support request and `inbox.changed` fires after commit (spec 0021 §3.4, §3.5). Not
    `InboxOnly`: other channels follow each recipient's preference (email off by default).
- Spec 0021 §2.3: add the row to the Core catalogue table and `support` to the list of Core
  categories. Check spec 0028's Notification settings page for any category list that needs it.

### What the sender sees

- `SupportRequestReceipt(int Id, bool AdministratorsNotified)`: true when at least one
  administrator other than the sender was a recipient.
- The Sent state's text says who was alerted, varying by `AdministratorsNotified` and
  `providerAccess`:
  - both: "Your request number is 1042. Your administrators and the {displayName} support team
    have been notified."
  - administrators only: "… Your administrators have been notified."
  - provider only: "… The {displayName} support team has been notified."
  - neither: "… Quote it when you talk to support."
  The `tm-reference` and Close stay. Keep the copy short; the receipt example in §12.2 updates.

## F-2. Page data on by default

- The support dialog's checkbox "Include the data shown on the page" starts ticked every time the
  dialog opens (never remembered). Unticking leaves page data out.
- "Download diagnostic file" outside the dialog still never includes page data (no warning is shown
  there). "Download instead" inside the dialog follows the checkbox.
- The warning says plainly that the page's data is included, and tells the user to untick it (or
  not send) if the page shows something they may not share. Rewrite both variants (with and without
  `providerAccess`).
- Rewrite every statement that says page data is off by default or leaves only when ticked: the
  Context paragraph ("No tenant content or free text leaves the tab unless the user ticks…" — keep
  the principle for telemetry, state the support-request exception correctly), §10.2, §9.6 if it
  says so, Decision 8, Review flag 2 (now: on by default; alternative off by default; flips on a
  privacy review or a customer that forbids it), and anything in other specs.

## F-3. Storage: screenshot in a blob, dump in the row

- No more "bundle". Remove the vocabulary entry and every use (§10 intro, §10.1's `screenshot`
  member, §12 intro, §12.2 send flow, §12.4, §12.5, §12.7, §12.8, §13, §14, Decision 5, Review flag
  5, Appendix A, and the carriers in other specs).
- `TmSupportDump` loses its `screenshot` member; the dump never embeds the image.
- `SupportRequest`:
  - remove `BundleId` and `HasScreenshot`;
  - add `[JsonColumn] public string DumpJson { get; set; }` — the dump (§10), required at submit,
    at most `TM_SUPPORT_DUMP_MAX_BYTES` (2 MiB UTF-8), not in `[DefaultSelect]`;
  - add `[BlobReference("support-screenshot", MaxSizeBytes: 8_388_608)] public int? ScreenshotId`.
  - `[DefaultSelect]` names `ScreenshotId` instead of `HasScreenshot`, never `DumpJson`.
- Blob kind `support-screenshot` replaces `support-bundle`: preset `Attachment`, narrowed by
  `CoreFeature`'s `BlobKind` transform to `AllowedContentTypes = [image/png, image/jpeg,
  image/webp]` and `MaxSize` 8 MiB; the `Attachment` thumbnail is kept, so the list can show one;
  read access `OwnerRead` through `core.SupportRequest`.
- `SupportRequestSubmission(string? Message, string UiVersion, string? RouteTemplate, string?
  PageUrl, IReadOnlyList<string> TraceIds, bool IncludesPageData, JsonElement Dump, int?
  ScreenshotId)`. The server stores the dump's raw text. New codes: `SupportRequests.DumpInvalid`
  (`dump` not an object, or its `format` is not `tellma-support-dump`) and
  `SupportRequests.DumpTooLarge` (over 2 MiB UTF-8). The `Required` row for `bundleId` goes;
  `Blob.NotAttachable`/`Blob.DuplicateReference` move to `screenshotId`. The JSON body limit (spec
  0016's `MaxJsonBodyBytes`, 8 MB) admits the dump; verify.
- Send flow: (1) with a screenshot, `TmApi.upload('support-screenshot', blob, { fileName:
  'screenshot.png', signal, onProgress })` → a staged `BlobDescriptor`; (2) `submit` with the dump
  inline and `screenshotId`. Without a screenshot, `submit` alone. Try again re-sends only the
  failed step.
- The details page reads the dump only when shown: verify whether a stack `get` with an explicit
  `select=DumpJson` can read a `[JsonColumn]` (spec 0012 says it is absent from the Queryex schema;
  spec 0029 §2.3 says such properties appear `queryable: false` "for details forms"; check spec
  0015/0016's select rules and how spec 0033 reads `DefinitionJson`). If it can, the details page
  selects it; if not, add a read action. Report which.
- `tm-support-bundle-summary` becomes `TmSupportDumpSummary` (`tm-support-dump-summary`): it reads
  the dump as above and the screenshot through the blob GET.
- The list's `HasScreenshot` column becomes the screenshot thumbnail column (`Screenshot`/
  `ScreenshotId`, whichever the CRUD frameworks name a blob reference column).
- Admin surface (§12.7): `AdminSupportRequest` drops `BundleSize`, gains `long DumpBytes`
  (`DATALENGTH`) and keeps `bool HasScreenshot` (computed from `ScreenshotId`). Routes: the list;
  `GET …/support-requests/{requestId}/dump` (the dump as a JSON attachment); `GET
  …/support-requests/{requestId}/screenshot` (the image as an attachment; 404 `not-found` when
  none). Each read logs `SupportRequestRead`.
- Retention (§12.8, spec 0020 §14.4): deleting an expired row releases its screenshot; the dump
  goes with the row.
- Decision 5 and Review flag 5 rewritten: the dump in the row (one write, no second blob, readable
  by the stack, off the default select so lists stay light); the screenshot alone in a blob (image
  policy, thumbnail). Flag 5's alternative now: the dump as a second blob; flips if dumps grow the
  tenant database noticeably.

## Out of scope

Specs 0001–0009 are frozen; don't touch them. Don't stage or commit. Don't touch anything outside
`docs/specs/`, `ARCHITECTURE.md` and `.tmp/round-2/r6/`.

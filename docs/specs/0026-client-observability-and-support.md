# Spec: Client Observability, Support and Page Context

- **Author:** Ahmad Akra
- **Date:** 20 October 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

Support and operations need to see what happened in a user's browser, and a future embedded
assistant needs to know what the user is looking at. This spec ships three parts:

- **Observability:** a telemetry API and its relay into the host's OpenTelemetry pipeline, error
  logging, page and request measurements, the UI version, and a request log of trace ids.
- **Support:** Copy diagnostics, the state dump, the screenshot, and an opt-in support request.
- **Page context:** a registry where every page and dialog publishes what it shows, what is wrong
  and what can be done. Toolbars, menus, shortcuts, the dump and the assistant read it.

Telemetry never carries tenant content or free text. Those leave the tab only through the support
dialog, in a request the user sends or a file downloaded there, and page data only while "Include
the data shown on the page" stays ticked (§10.2). Nothing here blocks, retries or throws into the
user's path. The design builds on spec 0024's HTTP layer, spec 0011's OpenTelemetry pipeline and
admin surface, and specs 0015, 0017 and 0021.

Other specs that carry parts of this design:

- spec 0011 §5.1, §5.3, §5.5, §5.9, §8, §9 — the relay's mapping, the reserved prefix, the
  no-renewal rule, `DistributionInfo.support` and `.telemetry`, the admin reads, the configuration
  keys, inbound `traceparent` and the alerts;
- spec 0014 §2, §5.3 — the `core.SupportRequest` securable and its self-scope;
- spec 0016 §2.1, §2.2, §8.2, §10.1 — the routes, the retry class, the `tellma-telemetry` rate
  policy and the meter;
- spec 0017 §1.3, §5.2 — the `support-screenshot` blob kind;
- spec 0018 §6.2 — Acme turns support on;
- spec 0020 §14.4 — retention;
- spec 0021 §2.3 — the `core.support.requested` type and the `support` category;
- spec 0023 §3.2 — physical-key matching;
- spec 0024 §4.4, §8.4, §10.1–§10.3, §11 — page scopes, the sign-out flush, `send` and `upload`,
  `begin` and `end`, and the 5xx dialog's support actions;
- spec 0027 Appendix A, §10.1, §11 — the reserved shortcut, "Report a problem", About and
  Diagnostics.

## Goals / Non-goals

**Goals**

- **Nothing to write for the common case.** The platform provides errors, timings, the request
  log, the version, the dump, the screenshot, support requests and framework pages' contexts.
- **One relay, no browser SDK.** Items are pseudonymous and use closed names. They join the
  server's records by trace id, in Application Insights or any Serilog sink.
- **Every call quotable.** A slow, failed or hanging call's trace id is two keystrokes away.
- **Support in one send:** one row, a blob only for the screenshot, opt-in per distribution, and
  the people who act on it alerted.
- **One model of page context and actions** for toolbars, menus, shortcuts, the dump and the
  future assistant.

**Non-goals (explicitly out of scope)**

- **Headers, `traceparent` and the error dialogs** — spec 0024 §10.2, §11.
- **About, Shortcuts and the session menu** — spec 0027 §10, §11.
- **The toast policy** — spec 0028 §8.
- **Each page kind's full context** — the owners listed in §9.3.
- **Help articles; the assistant panel and its AG-UI adapter** — later specs (§9.8, §9.7).
- **Session replay, interaction latency (INP), layout shift and RUM dashboards** — later, built on
  the relay.

## 1. Placement

The client half lives in the `@tellma/core` primary, with the heavy support tools in the lazy
`/support` entry point. The server half is the relay in `Tellma.Core.AspNetCore` and the
support-request stack in `Tellma.Core`.

Contract blocks are TypeScript and C# sketches: names and shapes are normative; imports,
documentation comments, bodies and cancellation tokens are omitted.

| Where | Holds | Loaded |
|---|---|---|
| `@tellma/core` primary, folders `observe/`, `requests/`, `context/`, `support/` | `TmTelemetry`, `TmErrorHandler`, `TmRequestLog`, `TmAppVersion`, `TmPageContexts`, `TmPageActions`, `provideTmPageScope`, `tmSerializePageContext`, `TmSupport`, `TM_HELP_ARTICLES` | eager; this spec's share ≤ 6 KB gzip |
| `@tellma/core` primary, dynamic chunk | the load-metrics collector (§5.2) | at idle, after first render |
| `@tellma/core/support` | `TmSupportDialog`, `TmScreenCapture`, `TM_DISPLAY_MEDIA`, `tmBuildSupportDump`, `TmDiagnosticsPanel` (`tm-diagnostics`), `TmSupportDumpSummary`, `TmSupportRequestDetails` | on demand, worker group `on-demand` (spec 0024 §5.3); ≤ 25 KB gzip |
| `@tellma/core-testing` | `TmFakeTelemetry` (records items, sends nothing), `tmFakeDisplayMedia()` (a canvas `captureStream`), `TmPageContextHarness` (reads the registry, invokes actions) | tests |
| `@tellma/core-tools/eslint` | `tm/telemetry-names`: literal event names and action ids match §3.2 and §9.3 | lint |
| `Tellma.Core.Abstractions.Api` | `ClientTelemetryBatch`, `ClientTelemetryItem`, `ClientTelemetryOptions`, `ClientTelemetryNames`, `SupportOptions`, `SupportTelemetryNames` | — |
| `Tellma.Core.AspNetCore` | `POST /api/client-telemetry`; the three admin support routes | `MapTellma` (spec 0011 §5.1) |
| `Tellma.Core` | `SupportRequest`, `SupportRequestService`, `SupportRequestAccessCriteria`, the `support-screenshot` transform, the `core.support.requested` type, resx `SupportRequests.*`, `Notification_core_support_requested`, `NotificationType_core_support_requested` and `NotificationCategory_support` | `CoreFeature` |

**Vocabulary**

- **Item** — one telemetry record.
- **Batch** — the items that one relay call carries.
- **Visit** — one routed page, from its navigation start to the next navigation start or page hide.
- **Slow request** — a call that reached 3 s, whether it has settled or is still pending.
- **Dump** — the JSON describing the tab at a moment (§10).
- **Page data** — what a page shows from the tenant: rows, form values, error messages, the user's
  search and filter text, and the URL's query.
- **Overlay** — a context-bearing dialog above the routed page (`overlay: true`, §9.1).
- **Page scope** — the injector region of a page or overlay that owns one context (§9.4).
- **Top context** — the topmost overlay's context, else the routed page's.
- **Origin** — who invoked an action: `user`, `shortcut`, `launcher` or `assistant`.

## 2. What a distribution writes

A distribution writes nothing for the common case. Three escape hatches cover the rest, each a few
lines:

```ts
// 1. An event of its own; the scope is the feature key
inject(TmTelemetry).event('acme.credit-exposure.run', { kind: 'summary', rows: 120 });

// 2. A page action: a toolbar button, a shortcut and, later, an assistant tool
inject(TmPageActions).register({
  id: 'acme.credit-review.suggest-limits', labelKey: 'Acme_CreditReview_SuggestLimits', icon: 'wand',
  group: 'primary', shortcut: 'Mod+Shift+A',
  invoke: async () => { await this.suggestLimits(); return { outcome: 'done' }; },
});                                              // unregisters with the caller's DestroyRef

// 3. A tmPage with richer context than the route host's default (a title and an empty state)
inject(TmPageContexts).register({
  kind: 'custom', routeTemplate: '/:t/credit-review', title: this.title, status: this.status,
  state: computed(() => ({ threshold: this.threshold(), overLimit: this.overLimit() })),
  errors: signal([]), actions: signal([]),
});                                              // replaces the route host's custom context (§9.2)
```

- **Its own overlay** provides `provideTmPageScope()` and registers its context with `overlay: true`.
- **A handled failure worth counting** goes to `TmTelemetry.error(err, context)`.

## 3. Telemetry API and pipeline

`TmTelemetry` takes items from any code. It adds the envelope, samples, queues, and sends the items
to the relay (§6) in batches. Nothing awaits it, and it never throws in production.

```ts
type TmTelemetryProps = Readonly<Record<string, string | number | boolean>>;
declare class TmTelemetry {                                              // primary
  event(name: string, props?: TmTelemetryProps): void;
  timing(name: string, ms: number, props?: TmTelemetryProps): void;      // ms becomes the property `ms`
  error(error: unknown, context?: Readonly<Record<string, string>>): void; // one error item (§4)
  flush(): void;                                                         // send now: sign-out, tests
  readonly recent: Signal<readonly TmTelemetryItem[]>;                   // the last 100 items, sent or not
}
interface TmTelemetryItem { readonly kind: 'event' | 'timing' | 'error'; readonly name: string;
  readonly at: number; readonly traceId?: string; readonly props: TmTelemetryProps } // at: epoch ms
```

### 3.1 The envelope

| Member | Where | Source |
|---|---|---|
| `version` | batch | `TmAppVersion.ui` (§7) |
| `tabId` | batch | `TmTabBus.tabId` (spec 0025 §6.1) |
| `tenantId` | batch | `TmUrls.tenantId()` (spec 0024 §9.4); null on tenantless pages |
| `sentAt` | batch | the client clock at send, for skew correction (§6.3) |
| `route`, `pageKind` | every item, reserved properties | the routed page's template (`/:t/centers/:id`, never a URL) and `TmRouteData.kind` (spec 0024 §4.4) |
| `traceId` | request-linked items | the request's own trace id (§8) |
| `recentTraceIds` | error items only | the last three request trace ids, comma-joined |

### 3.2 Names and properties

- **Names** have the shape `<scope>.<area>.<event>[.<more>]`, such as `tm.api.slow` or
  `acme.credit-exposure.run`. They match `^[a-z][a-z0-9]*(\.[a-z0-9][a-z0-9-]*){2,5}$` and are at
  most 64 characters. Scope `tm` is the platform's; any other scope is a composed feature key
  (`acme`, `gl`).
- **Owners.** Each spec lists its own events. Appendix A.2 maps each `tm.<area>` to its owning
  spec, and a new area is added there.
- **Properties.** An item carries at most 16, the reserved ones included. Keys match
  `^[a-z][a-zA-Z0-9]{0,31}$`.
- **Values** are numbers, booleans, or strings of at most 64 characters matching
  `^[A-Za-z0-9_.:/@+-]*$`. That admits codes, kinds, route templates, entity names and versions.
- **Error items** alone also carry `message`, `stack` and `recentTraceIds`, redacted by §4.3.
- **The guard.** In development, a bad name or property throws `TmTelemetryError`. In production,
  a bad name drops the item and a bad value drops the property. Each drop is counted into the next
  `tm.telemetry.dropped`. So a value with a space or a non-Latin letter never leaves the tab.
- **Never in telemetry:** URLs, query strings, search or filter text, field values, display names,
  e-mail addresses, problem `message`s, user ids, and time-zone ids or offsets, which reveal where
  the user is. The server adds the session's `sub` (§6.3).

### 3.3 Queue and flush

1. Each call stamps `at` and adds the envelope. The item always enters the `recent` ring. It
   enters the send queue unless sampled out (§3.5) or disabled (§3.4).
2. The queue flushes at 50 items, 30 s after its first item, or 1 s after an `error` item
   (debounced). It also flushes on `visibilitychange` to `hidden`, on `pagehide`, on `flush()`,
   and on a tenant switch, since a batch holds one tenant's items.
3. A flush cuts the queue into batches of at most 16 KB and 100 items. Each batch goes through
   `TmApi.send('/api/client-telemetry', batch, { background: true })` (spec 0024 §10.1). The call
   is `keepalive` and unread, and marked background so it never keeps a session alive.

- **On `pagehide`,** a flush sends one batch at most. It shares the 64 KiB keepalive budget with
  spec 0025 §9.4's flush.
- **Queue cap 200.** Overflow drops the oldest non-error item first. The next batch carries
  `tm.telemetry.dropped { count, reason: 'overflow' }`.

### 3.4 When nothing is sent

- **Offline** (`TmApi.online()` false): the queue holds within its cap and flushes when online.
- **No active session** (`TmSession.state` not `active`): the queue holds. Sign-out calls `flush()`
  before `state` turns `signing-out` and before `/bff/logout` (spec 0024 §8.4). The changed-user
  path (spec 0024 §8.7) drops the queue.
- **Disabled** (`DistributionInfo.telemetry.enabled` false): nothing is queued, but the `recent`
  ring still fills.
- **Cold launch.** Before the `distribution` document is known (spec 0025 §3.1), items queue under
  the defaults: enabled, rate 1.
- **No retry:** a lost batch is lost. Relay calls never enter the request log or the summaries.

### 3.5 Sampling

- **One decision per tab:** at first injection, `sampled = Math.random() < sampleRate`, where
  `sampleRate` is `DistributionInfo.telemetry.sampleRate` (0 to 1, default 1).
- **Sampled:** `tm.page.*`, `tm.vitals.*`, `tm.action.*` and every non-`tm` event. Each carries
  the property `sampleRate`.
- **Always sent:** errors and every other `tm.*` event, so error counts, launch timings and slow
  requests stay exact.

### 3.6 Development

Development builds also write every item to `console.debug('[tm]', name, props)`. In unit tests,
`TmFakeTelemetry` replaces the service and records items for assertions.

## 4. Error logging

Every uncaught error, unhandled rejection and reported failure reaches `TmErrorHandler`. It logs the
error to the console, keeps a record for the dump, and queues one `tm.error.captured` item.

```ts
declare class TmErrorHandler implements ErrorHandler { handleError(error: unknown): void } // provided by provideTellma (spec 0024 §2.2)
interface TmErrorRecord {                            // the primary keeps the last 50 for the dump
  readonly source: 'api' | 'render' | 'global';      // §4.1
  readonly errorClass: string; readonly fingerprint: string; // errorClass: 'TypeError', 'ChunkLoadError'; fingerprint: §4.2
  readonly kind?: TmApiErrorKind; readonly code?: string; readonly status?: number; readonly traceId?: string; // from a TmApiError
  readonly routeTemplate: string; readonly stack: string; readonly message?: string; // stack and message redacted (§4.3)
  readonly at: number; readonly recentTraceIds: readonly string[]; // at: epoch ms; recentTraceIds: the last three (§3.1)
}
```

### 4.1 Sources

| `source` | How it arrives | Reported |
|---|---|---|
| `render` | Angular's `ErrorHandler` (template, effect and lifecycle errors), and `TmTelemetry.error` from app code | always |
| `global` | `provideBrowserGlobalErrorListeners()` in `bootstrapTellma` (spec 0024 §2.2) forwards window `error` and `unhandledrejection` | unless filtered |
| `api` | `TmErrorPresenter` (spec 0024 §11), when it shows the 5xx dialog and for a failed `background` read | kinds `server`, `unavailable`, `partial-failure`, and `offline` with code `outcome-unknown` |

- **A failed dynamic import** ("Failed to fetch dynamically imported module") is recorded with
  `errorClass` `ChunkLoadError`. The route host also runs `TmUpdates.check()` (spec 0024 §4.4).
- **Filtered out:**
  - a rejection with a `TmApiError` of kind `cancelled` or `unauthenticated`;
  - `ResizeObserver loop` notices;
  - an error whose top frame is not a same-origin script, such as an extension's. It counts as
    `tm.telemetry.dropped { reason: 'foreign' }`.
- **Other API errors are outcomes.** The server logs validation, conflict, forbidden and not-found
  (spec 0016 §10.1). The client counts them only in the request summaries (§5.3).

### 4.2 Deduplication

- **Fingerprint:** `errorClass`, `@`, then the first same-origin frame as §4.3 reduces it. For
  example: `TypeError@chunk-K3J7Q2M9.js:1:2345`.
- **Throttle.** Per tab, each fingerprint is sent at most once per 10 minutes. All error items
  together are capped at 30 per 10 minutes. Suppressed items count as
  `tm.telemetry.dropped { reason: 'throttled' }`.

### 4.3 Redaction of messages and stacks

- **`TmApiError`** never ships its `message`, which can quote field values or filter text. Its
  `errorClass`, `kind`, `code`, `status` and `traceId` say everything.
- **Other messages** pass five masks in order, then a cap of 200 characters:
  1. URLs → `<url>`.
  2. E-mail addresses → `<email>`.
  3. Quoted text (`'…'`, `"…"`, `“…”`, `«…»`) that is not an identifier → `"…"`. An identifier
     matches `^[A-Za-z_$][A-Za-z0-9_$]{0,63}$`.
  4. Each run of digits → `#`.
  5. Each run of non-ASCII characters → `…`.

**Example —**

| Raw message | Shipped |
|---|---|
| `Cannot read properties of undefined (reading 'Name')` | unchanged: `'Name'` is an identifier |
| `Invalid value "Farah Nasser" at row 42` | `Invalid value "…" at row #` |
| `Failed to load https://acme.app.tellma.com/17/centers?q=jed` | `Failed to load <url>` |
| `القيمة غير صالحة` | `…` |

- **Stacks** keep at most 30 frames and 4 KB. Each frame's URL is reduced to its file name
  (`chunk-K3J7Q2M9.js:1:2345`).
- **Source maps are hidden.** CI archives them per UI version (spec 0024 §2.6), and support
  symbolicates stacks against that archive.

## 5. Platform measurements

This spec measures what every page shares: visit timings, the hard load's paint metrics, request
outcomes and action usage. Page specs add only domain events; Appendix A.1 lists the properties.

### 5.1 Page visits (`tm.page.visit`, sampled)

A visit starts at the router's `NavigationStart`; for a document's first route, it starts at
`performance.timeOrigin`. It ends at the next `NavigationStart` or at `pagehide`, and emits one
timing:

| Property | Meaning |
|---|---|
| `ms` (data time) | navigation start → data on screen, or the error state: the first `status` change away from `loading` (§9.1) |
| `chromeMs` | navigation start → the page context registered (§9.2) and painted: the `afterNextRender` after it, then one `requestAnimationFrame` |
| `visitS` | the visit's length in seconds |
| `entry` | `load` (a document's first route) or `navigation` |
| `outcome` | `ready`, `error`, or `left` (navigated away before the data) |

### 5.2 The hard load (`tm.vitals.load`, sampled)

The load-metrics collector emits one event per document, when LCP finalises: at the first input,
or at `visibilitychange` to `hidden`. Launch timings belong to spec 0024.

- **Metrics:** TTFB (`responseStart`), FCP, LCP, the navigation type, and whether a service worker
  served the page. A metric the browser does not expose is omitted, never sent as zero.
- **No lost entries.** The collector observes with `buffered: true`, so loading it at idle loses
  nothing.

### 5.3 Request outcomes

- **`tm.api.requests`** (always sent) summarises each flush window. It emits one event per API
  route (`centers/query`, `/bff/user`), for the 20 busiest routes. Each event carries counts by
  outcome, the sum and maximum durations, the counts over 1 s and over 3 s, and the background
  calls.
- **`tm.api.slow`** (always sent, at most 20 per tab per 10 minutes) is one event per slow request,
  with its `traceId`. A slow request still pending at `pagehide` reports `outcome: 'pending'`.
- **Blob GETs** through `TM_BLOB_FETCHER` stay out of the summaries and the request log.

### 5.4 Usage

`tm.action.invoked` (sampled) is the usage signal for actions; the dispatcher emits it (§9.5).
`tm.page.visit` is the usage signal for pages. Domain usage events belong to their own specs.

## 6. The relay: `POST /api/client-telemetry`

The relay accepts batches from signed-in browsers, validates every item and adds what only the
server knows. It writes each item into the host's OpenTelemetry pipeline, which exports to
Application Insights when configured and to Serilog's sinks always.

### 6.1 Contract

```csharp
// Tellma.Core.Abstractions.Api
public sealed record ClientTelemetryBatch(string Version, string TabId, int? TenantId, DateTimeOffset SentAt,
    IReadOnlyList<ClientTelemetryItem> Items);
public sealed record ClientTelemetryItem(string Kind, string Name, DateTimeOffset At, string? TraceId,
    IReadOnlyDictionary<string, JsonElement>? Properties);        // Kind: event | timing | error
public sealed class ClientTelemetryOptions                         // section Tellma:ClientTelemetry (spec 0011 §8)
{
    public bool Enabled { get; set; } = true;                      // false: the relay answers 204 and discards
    [Range(0.0, 1.0)] public double SampleRate { get; set; } = 1.0;
}                                                                  // both reach the SPA (§3.4, §3.5)
```

- **Route:** `POST /api/client-telemetry` in `Tellma.Core.AspNetCore`, mapped by `MapTellma`
  (spec 0011 §5.1).
- **Endpoint setup:** `RequireAuthorization(TellmaPolicies.Web)`,
  `AsTenantlessEndpoint("client-telemetry")`, rate policy `tellma-telemetry`, and a request size
  limit of 32 KB (§6.2). It is excluded from request tracing and never renews the session (spec
  0011 §5.5, §9).
- **Answer:** `204 No Content` whenever the batch parses, however many items were dropped.

**Example —** a batch of two items, answered `204`:

```json
{ "version": "1.4.0+3f9c2ab1e5d4c0a7b6f8e9d2c1b0a9f8e7d6c5b4",
  "tabId": "k3J7q2M9xA", "tenantId": 17, "sentAt": "2026-10-20T11:03:12.512Z",
  "items": [
    { "kind": "timing", "name": "tm.page.visit", "at": "2026-10-20T11:02:41.020Z",
      "properties": { "route": "/:t/centers", "pageKind": "search", "ms": 412, "chromeMs": 38, "outcome": "ready" } },
    { "kind": "event", "name": "tm.api.slow", "at": "2026-10-20T11:02:55.300Z",
      "traceId": "0af7651916cd43dd8448eb211c80319c",
      "properties": { "route": "/:t/centers", "pageKind": "search", "api": "centers/query", "durationMs": 4810 } } ] }
```

### 6.2 Validation and limits

| Check | Rule | On failure |
|---|---|---|
| CSRF | spec 0011 §5.4: `Tellma-Client` with name `web`, a JSON body, same origin | 403 `csrf-rejected` / 415 |
| Session | the `Tellma.Web` cookie policy | 401 `unauthenticated` |
| Body | at most 32 KB | 413 `limit-exceeded` |
| `Version` | equals the version part of `Tellma-Client` | 400 `bad-request` |
| `TabId` | matches `^[A-Za-z0-9_-]{8,40}$` | 400 `bad-request` |
| `TenantId` | null or ≥ 1 | 400 `bad-request` |
| `Items` | at most 100 | 400 `bad-request` |
| `Kind` | `event`, `timing` or `error` | item dropped (`kind`) |
| `Name` | §3.2's grammar; exactly `tm.error.captured` for errors | item dropped (`name`) |
| `At` | between `SentAt − 24 h` and `SentAt + 1 min` | item dropped (`time`) |
| `TraceId` | 32 lowercase hex, not all zeros | member ignored |
| `Properties` | at most 16; keys per §3.2; numbers, booleans, strings of at most 128 characters (`stack` 4,096, `message` 256); nothing nested | property dropped (`prop`) |

- **Backstop masks.** The relay re-applies §4.3's URL and e-mail masks to `message` and `stack`.
- **Drop log.** The relay logs one Information record per batch that dropped items:
  `Client telemetry dropped {Count} items`.
- **Rate policy `tellma-telemetry`** (spec 0016 §8.2): ten open tabs use a third of it.

### 6.3 What the server adds

- **Client dimensions:** `Subject` (the session's `sub`, never an e-mail), `UserAgent`,
  `ClientVersion`, `ClientOutdated`, `ClientTabId`, `ClientTenantId`, `ClientRoute`,
  `ClientPageKind` and `SampleRate`. `ClientOutdated` is true when `ClientVersion` differs from
  `DistributionVersion`.
- **Server dimensions:** spec 0011 §9's enrichers and resource (`PlatformVersion`,
  `DistributionVersion`, `service.version`, `tellma.deployment.id`).
- **`ClientAt`** is the item's `At` corrected by the batch's clock skew (`ReceivedAt − SentAt`).
- **The tenant id is the client's claim,** for filtering only. The relay opens no tenant scope and
  touches no database. It never uses the id for authorisation or as a metric tag.

### 6.4 Emission

| Item | Record | Application Insights |
|---|---|---|
| `event`, `timing` | `LogInformation("{microsoft.custom_event.name}", name)` | `customEvents`, properties in `customDimensions` |
| `error` | `LogWarning("Client error {ErrorClass} on {ClientRoute}", …)` | `traces`, severity Warning |

- **Logger scope.** Each record's logger scope holds §6.3's dimensions and the item's properties.
  Logs export with `IncludeScopes` (spec 0011 §9), which puts them in `customDimensions`.
- **Error extras.** An error's scope also holds `ErrorSource`, `ErrorKind`, `ErrorCode`, `Status`,
  `Fingerprint`, `Stack`, `Message` and `RecentTraceIds`.
- **Trace correlation.** An item with a `TraceId` is logged while an unexported `Activity` is
  current, parented to `(TraceId, a random span id)`. The record's `operation_Id` is then the
  request's trace id. So a trace id copied from a dialog finds the server request, its SQL and the
  client's record together. The activity belongs to no registered `ActivitySource`, so no span is
  exported.
- **Two counters** sit on the `Tellma.Core.AspNetCore` meter (below). A tag value outside its
  closed set maps to `other`, so no client string becomes a tag. Alerts over them live in
  `infra/monitoring/` beside the server's (spec 0011 §9), with operational thresholds.

```csharp
// Tellma.Core.Abstractions.Api
public static class ClientTelemetryNames
{
    public const string MeterName = "Tellma.Core.AspNetCore";
    public const string LoggerCategory = "Tellma.ClientTelemetry";  // a deployment can route the category
    public const string Errors = "tellma.client.errors";              // counter; source ∈ api | render | global, kind ∈ TmApiErrorKind | none
    public const string SlowRequests = "tellma.client.requests.slow"; // counter; outcome ∈ ok | error | cancelled | pending
    public const string SourceTag = "source", KindTag = "kind", OutcomeTag = "outcome";
}
```

## 7. UI version identity

The UI version is the distribution's informational version: the same value the server reports in
`Tellma-Build`, because client and server ship as one build (spec 0024 §3.1).

```ts
declare class TmAppVersion {                       // primary
  readonly ui: string;                             // '1.4.0+3f9c2ab1e5d4…' = metadata.distribution.version
  readonly display: string;                        // '1.4.0': the session menu's About row
  readonly label: string;                          // '1.4.0 (3f9c2ab)' for the About dialog; '1.4.0' without a commit
  readonly commit: string | null;                  // the 40 hex after '+'
  readonly platform: string;                       // metadata.distribution.platformVersion
  readonly features: readonly { readonly key: string; readonly version: string }[]; // metadata.features
  readonly server: Signal<string | null>;          // the last Tellma-Build seen (§8.1)
  readonly serverNewer: Signal<boolean>;           // server() is set and compare(server()) is 1
  compare(build: string): -1 | 0 | 1 | null;       // build against ui: 1 newer, -1 older, 0 the same build, null unordered
}
```

- **Format:** `X.Y.Z[-pre]+<sha>` from `Directory.Build.props` and SourceLink
  (`IncludeSourceRevisionInInformationalVersion`). So `git show <sha>` finds any report's commit.
- **Order.** `compare` uses SemVer 2.0 precedence, which ignores the `+<sha>` build metadata.
  Two builds of the same `X.Y.Z[-pre]` with different commits are unordered (`null`), and an
  identical string is `0`. Spec 0024 §5.6 uses it to judge a `412`.

  | `ui` | `build` | `compare(build)` |
  |---|---|---|
  | `1.4.0+3f9c2ab…` | `1.5.0+77e01d4…` | `1` |
  | `1.4.0+3f9c2ab…` | `1.4.0-rc.2+77e01d4…` | `-1` |
  | `1.4.0+3f9c2ab…` | `1.4.0+77e01d4…` | `null` |
  | `1.4.0+3f9c2ab…` | `1.4.0+3f9c2ab…` | `0` |

- **Where it shows:**
  - the session menu shows `display` (spec 0027 §10.1);
  - the About dialog (spec 0027 §11.1) shows `label` for the UI, the raw versions of the server,
    platform and features, a Reload alert when `serverNewer()`, and a Copy button for `ui`.
- **Where it travels:** the `Tellma-Client: web/<ui>` header (spec 0024 §10.2), the batch's
  `version`, the dump's `versions.ui` and `SupportRequest.UiVersion`.

## 8. Request log and trace ids

The client generates each `TmApi` call's trace id before sending it (spec 0024 §10.2), and the
server adopts it (spec 0011 §9). The request log keeps the last 100 calls with their trace ids. So
a slow, failed, cancelled or hanging call can always be quoted.

### 8.1 `TmRequestLog`

```ts
type TmRequestOutcome = 'pending' | 'ok' | 'cancelled' | 'error';
interface TmRequestRecord {
  readonly traceId: string; readonly route: string;            // route: 'centers/query', as TmApi received it
  readonly tenantId: number | null; readonly method: 'POST' | 'GET'; readonly background: boolean;
  readonly startedAt: number; readonly durationMs: number | null; // startedAt: epoch ms; durationMs: null while pending
  readonly outcome: TmRequestOutcome; readonly status?: number; readonly code?: string; // status: HTTP; code: the TmApiError's
  readonly slow: boolean;                                       // 3 s reached, pending or not
}
type TmRequestStart = Pick<TmRequestRecord, 'traceId' | 'route' | 'tenantId' | 'method' | 'background'>;
interface TmRequestHandle {
  end(result: { outcome: Exclude<TmRequestOutcome, 'pending'>; status?: number; code?: string; serverBuild?: string }): void;
}
declare class TmRequestLog {                                    // primary
  readonly recent: Signal<readonly TmRequestRecord[]>;          // newest first, ring of 100
  diagnosticsText(count?: number): string;                      // §8.3; default 10 requests; synchronous
  copy(): void;                                                 // clipboard.writeText(diagnosticsText()) inside the gesture
  begin(start: TmRequestStart): TmRequestHandle;                // TmApi only
}
```

- **`TmApi` calls `begin` at send and `end` at settle** (spec 0024 §10.3, steps 3 and 5). So a
  hanging call shows as `pending`, and its trace id can be copied while it hangs.
- **Recorded:** every `read`, `write`, `download` and `upload`; not `send`, which has no outcome.
- **`slow`** turns true at 3 s, even while the call is pending. Each pending call has one timer,
  which `end` clears. A slow record feeds `tm.api.slow` (§5.3) when it ends.
- **`end`** passes the response's `Tellma-Build` to `TmAppVersion.server` (§7).

### 8.2 Slow requests

A slow request shows no UI of its own beyond its loading state, and production writes nothing to
the console. Its trace id is reachable through the Diagnostics panel (§8.4) and Copy diagnostics
(§8.3). Support, working without the user, finds its `tm.api.slow` item by `Subject` and time.

### 8.3 Copy diagnostics

`TmRequestLog.diagnosticsText()` builds a plain-text block for support: versions, the session, the
page and the last requests. It is English with Latin digits in every UI language, because support
reads it.

**Example —** Farah, working in Arabic, presses the shortcut on a details page whose save hangs:

```text
Acme diagnostics · 2026-10-20 14:03:12 +03:00
UI 1.4.0+3f9c2ab1e5d4 · server 1.4.0+3f9c2ab1e5d4 · platform 0.9.0+77e01d4
Tenant 17 · tab k3J7q2M9 · ar-SA · uq · online · leader, hub connected
Page /:t/centers/:id (details) · status ready · 1 error on screen
14:03:05  pending 6.2 s  slow   POST centers/save        4bf92f3577b34da6a3ce929d0e0e4736
14:02:58  4.81 s         slow   POST centers/query       0af7651916cd43dd8448eb211c80319c  200
14:02:41  0.31 s         cancel POST centers/query       a1b2c3d4e5f60718293a4b5c6d7e8f90
14:02:40  0.12 s                POST client/sync         9e8d7c6b5a4938271605f4e3d2c1b0a9  200
```

- **Lines.** Times are local, with the offset in the header. Each request line starts with its
  start time. Leader and hub status come from `TmLeader` (spec 0025 §7.1).
- **Triggers:** the global shortcut `Mod+Alt+Shift+D` (`tm.support.copy-diagnostics`, spec 0027
  Appendix A), the Diagnostics panel, and the 5xx dialog (spec 0024 §11).
- **Matching.** The shortcut goes through spec 0027 §4's registry, which matches it with
  `tmKeysMatch` (spec 0023 §3.2), so it works on an Arabic layout. It runs in editable fields and
  over modals, under spec 0027 §4.2's rules.
- **Clipboard:** `writeText` runs synchronously in the key or click handler, because Safari and
  Firefox require transient activation. Success and failure each toast once (spec 0028 §8.1).

### 8.4 The Diagnostics panel (`tm-diagnostics`)

The About dialog's Diagnostics tab (spec 0027 §11.1) renders `tm-diagnostics` from `/support`. It
is `@defer`red until the tab is selected.

- **Header:** the UI version with Copy, the server version, tenant, tab, online, leader and hub
  status, and the worker's version (`TmUpdates.version`, spec 0024 §5.5).
- **Requests:** a read-only `tm-grid` over `TmRequestLog.recent`, with the columns Time, Duration,
  Outcome, Request and Reference, and a "Slow and failed only" switch.
- **Grid behaviour.** The grid is one tab stop with arrow keys. Enter or a click copies the row's
  line (row activation, spec 0010 §C.1). Reference is a `tm-reference` chip (spec 0023 §3.3). A
  pending row's duration ticks each second.
- **Narrow screens.** Below `md`, Time and Outcome fold into the Duration cell's second line.
  Below `sm`, the dialog is full screen (spec 0010 §D.2).
- **Buttons:** Copy diagnostics, Download diagnostic file (§10.4) and Report a problem (§12.2).
- **Offline.** When the `/support` chunk cannot load offline, the tab shows "Diagnostics aren't
  available offline. Press {keys} to copy them." `tm-kbd` renders the keys. This text ships in the
  primary, since the chunk that holds the panel did not load.

## 9. Page context and page actions — the assistant seam

Every page and context-bearing dialog publishes one `TmPageContext`: what it is, what it shows,
what is wrong and what can be done there. Frameworks publish it from state they already hold, so
layouts write nothing. Toolbars, menus, shortcuts, the dump and the future assistant read it.

### 9.1 Contracts

```ts
type TmPageKind = 'home' | 'search' | 'details' | 'report' | 'studio' | 'dashboard' | 'settings' | 'custom';
interface TmPageContext {
  readonly kind: TmPageKind; readonly entity?: string; // entity: 'gl.Center'
  readonly routeTemplate: string; readonly overlay?: boolean; // '/:t/centers/:id' or 'panel:bell'; overlay true: a dialog's context
  readonly title: Signal<string>; readonly helpKey?: string;   // title: localized, as displayed; helpKey: reserved (§9.8)
  readonly status: Signal<'loading' | 'ready' | 'error'>;     // the page's data state
  readonly state: Signal<TmJson>;                    // kind-specific (§9.3)
  readonly visible?: Signal<TmVisibleData | undefined>;       // what grids and lists display
  readonly form?: TmFormContext; readonly errors: Signal<readonly TmContextError[]>; // form: in edit and create modes
  readonly actions: Signal<readonly TmPageAction[]>; // the publisher's own; the registry merges the scope's (§9.4)
}
interface TmVisibleData {
  readonly columns: readonly { readonly path: string; readonly label: string }[]; // ≤ 50, as displayed
  readonly rows: readonly { readonly id: TmId | null; readonly cells: Readonly<Record<string, TmJson>> }[]; // ≤ 200; TmId: spec 0024 §10.1
  readonly total: number | null; readonly truncated: boolean; // total: the count when known; truncated: more rows shown than carried
}
interface TmContextError {
  readonly source: 'validation' | 'request' | 'render' | 'client'; readonly code: string; // code: 'Required', 'query-invalid'
  readonly message: string; readonly path?: string; readonly traceId?: string; // message: localized; path: 'Lines[2].Quantity'
}
interface TmFormContext {
  readonly fields: Signal<readonly TmFormField[]>; readonly dirty: Signal<boolean>;
  readonly assisted: Signal<ReadonlySet<string>>;    // paths written by propose and not edited since
  propose(path: string, value: TmJson): boolean;     // writes through the field; false when unknown or read-only; never saves
}
interface TmFormField { readonly path: string; readonly labelKey: string; readonly label: string;
  readonly value: TmJson; readonly errors: readonly string[]; readonly editable: boolean }
```

- **Cells** hold what the cell shows, as plain JSON (spec 0024 §10.4):
  - a decimal is its string;
  - a navigation is its display name, with the id under `<path>.Id`;
  - a date is `yyyy-MM-dd`;
  - an instant is ISO 8601 in the display zone's offset (`2026-10-20T10:42:00+03:00`);
  - an enum is its code.

  **Example —** a Centers row:
  `{ "id": 5, "cells": { "Name": "Jeddah", "Parent": "West", "Parent.Id": 2 } }`.
- **Paths** are PascalCase with indexes. Column and field labels come from the entity descriptors
  (spec 0029 §4).
- **`status`** tells "still loading" apart from "empty".
- **`propose`** marks the path in `assisted`, and spec 0031 §6.2 shows the mark until the user edits
  the field. Nothing in this release calls `propose` outside tests.

### 9.2 The registry

```ts
declare class TmPageContexts {                       // primary
  readonly current: Signal<TmPageContext | null>;    // the routed page's context, actions merged (§9.4)
  readonly overlays: Signal<readonly TmPageContext[]>; // open context-bearing dialogs, bottom to top
  readonly top: Signal<TmPageContext | null>;        // the topmost overlay, else current
  register(context: TmPageContext, destroyRef?: DestroyRef): void; // destroyRef defaults to the injection context's
}
declare function provideTmPageScope(): Provider[];
```

- **Who registers:**
  - the route host gives every page a scope, and registers a `custom` context on `tmPage` routes
    (spec 0024 §4.4);
  - framework hosts register their own kind (§9.3);
  - the shared error dialogs register an overlay with their `TmContextError` (spec 0024 §11);
  - a layout never registers.
- **One routed context per scope.** A second non-overlay `register` in a scope replaces the first.
  It keeps the replaced context's own actions, after its own. So a `page` hatch that publishes its
  own kind keeps the host's actions (spec 0031 §3.7).
- **Order.** Overlays never replace each other and are ordered by registration. `current` is the
  newest live non-overlay context.
- **Lifetime.** A destroyed scope or `DestroyRef` removes its context at once. Hosts register in
  their constructors (see `chromeMs`, §5.1). Signals are read lazily, so registering costs nothing.

### 9.3 What each page kind publishes

The table gives each kind's minimum `state`; the owning spec completes the context and its actions.

| Kind | `state` (minimum) | `visible` | `form` | Owner |
|---|---|---|---|---|
| `home` | `{ hatch: boolean }` | the assignment lists' rows | — | spec 0035 §16 |
| `search` | `{ query: TmSearchQuery, view, list, selectedIds, selectedCount }` | grid or tile rows | — | spec 0030 §19 |
| `details` | `{ id: TmId \| null, mode: 'view' \| 'edit' \| 'create', dirty: boolean, tab?, child? }` | the open child grid's rows | edit, create | spec 0031 §16 |
| `report` | `{ report: number \| string, parameters, reportKind }` | rendered rows | — | spec 0034 §13 |
| `studio` | `{ report: number \| null, root: string, reportKind }` | preview rows | — | spec 0034 §13 |
| `dashboard` | `{ dashboard: number \| null, mode, parameters, widgets: [{ key, type, title, status, executedAt }] }` | — | — | spec 0035 §16 |
| `settings` | `{ category, mode, dirty }` | — | edit | spec 0032 §7, §8 with `category: 'profile'`; spec 0028 §10.3 with `category: 'notifications'` |
| `custom` | the page's own; default `{}` | the page's own | the page's own | the route host, or the page (§2) |

- **`search`** carries the effective `view` and `list`, and at most 200 `selectedIds`.
- **`state` holds ids, codes, modes, flags and counts.** User text goes only where §9.6 masks it:
  the query's `q`, `filter`, `columnFilters` and `custom`, and `parameters`.
- **Action ids** match `^[a-z][a-z0-9]*(\.[a-z0-9][a-z0-9-]*){1,4}$`:
  - framework actions use the prefixes `search`, `details`, `report`, `studio`, `dashboard`,
    `settings`, `support`, `attention`, `notifications`, `admin` and `home`;
  - entity actions are `<schema>.<entity-kebab>.<action>` (`gl.center.activate`);
  - distribution actions are `<feature>.<area>.<verb>` (`acme.credit-review.suggest-limits`).

### 9.4 Page actions

```ts
interface TmPageAction {
  readonly id: string; readonly labelKey: string;    // id: §9.3's grammar, such as 'details.save', 'gl.center.activate'
  readonly descriptionKey?: string; readonly icon?: string; // descriptionKey: for the assistant; icon: a registry name (spec 0023 §3.1)
  readonly group: 'primary' | 'menu'; readonly order?: number; // group: toolbar, or the Actions menu and overflow; order: default 100
  readonly enabled: Signal<boolean>; readonly destructive: boolean; // enabled includes permissions (spec 0029 §7)
  readonly confirm?: { readonly titleKey: string; readonly messageKey: string; readonly args?: Signal<TmJsonObject> }; // required when destructive
  readonly shortcut?: TmKeys; readonly params?: TmJsonSchema; // shortcut: live while its context is top; params: the arguments' schema
  invoke(args: TmJson, origin: TmActionOrigin): Promise<TmActionResult>;
}
type TmActionOrigin = 'user' | 'shortcut' | 'launcher' | 'assistant';
type TmJsonSchema = TmJsonObject;                    // a JSON Schema 2020-12 object schema
type TmActionResult = { readonly outcome: 'done'; readonly data?: TmJson } | { readonly outcome: 'cancelled' } // declined
  | { readonly outcome: 'refused'; readonly reason: 'not-found' | 'disabled' | 'busy' }
  | { readonly outcome: 'failed'; readonly error: unknown };
type TmPageActionInput = Omit<TmPageAction, 'group' | 'enabled' | 'destructive'> &
  Partial<Pick<TmPageAction, 'group' | 'enabled' | 'destructive'>>; // defaults: 'menu', always enabled, false
declare class TmPageActions {                        // primary
  register(action: TmPageActionInput, destroyRef?: DestroyRef): void;
  invoke(id: string, args: TmJson, origin: TmActionOrigin): Promise<TmActionResult>; // §9.5
  readonly running: Signal<ReadonlySet<string>>;     // ids being invoked: views render them busy
}
```

- **Scope.** `TmPageActions` injected in a page scope adds to that scope's context, even before the
  context registers. Outside any scope, it adds to `current`. `destroyRef` unregisters.
- **The merged list.** `current().actions()` and each overlay's `actions()` hold the publisher's
  own actions, then the scope's. They are ordered by `group`, then `order`, then registration.
- **Views.** `primary` actions are toolbar buttons (specs 0030, 0031). `menu` actions fill the
  Actions menu (spec 0031 §10.1) or the toolbar's overflow. The Shortcuts dialog lists the top
  context's actions that have a `shortcut` (spec 0027 §11.2).
- **Shortcuts.** While its context is `top`, the registry registers each `shortcut` with
  `TmShortcuts` (scope `page`, spec 0027 §4.1). Pressing it calls `invoke(id, {}, 'shortcut')`.
  An action whose `params` require members takes no shortcut; declaring one throws in development.

### 9.5 The dispatcher (`TmPageActions.invoke`)

Every origin goes through the dispatcher, so the permission, double-submit and confirmation rules
hold in one place. Views call `invoke(action.id, {}, 'user')`, never `action.invoke`. They render
`running` as the button's busy state in the click's frame.

1. Find `id` in `top`, then the lower overlays, then `current`; absent → `refused: not-found`.
2. `enabled()` false → `refused: disabled`. Permissions are already part of `enabled`.
3. Already in `running` → `refused: busy`, so nothing is submitted twice.
4. `destructive` → spec 0023's `TmConfirm` with the `confirm` keys and a danger choice. This holds
   for every origin, the assistant included. Declined → `cancelled`. A destructive action without
   `confirm` throws in development.
5. Add the id to `running`, `await action.invoke(args, origin)`, then remove it. A throw becomes
   `failed`; the action still presents its own `TmApiError` through `TmErrorPresenter`.
6. Emit `tm.action.invoked` (sampled).

### 9.6 Snapshots

`tmSerializePageContext(context, { includeData })` reads every signal once and returns plain JSON.
It is the one serialisation that the dump and the future assistant share.

```ts
declare function tmSerializePageContext(context: TmPageContext, options: { includeData: boolean }): TmPageContextSnapshot;
interface TmPageContextSnapshot {
  readonly kind: TmPageKind; readonly routeTemplate: string; readonly overlay: boolean; readonly entity?: string;
  readonly title: string; readonly helpKey?: string; readonly status: 'loading' | 'ready' | 'error'; readonly state: TmJson;
  readonly visible?: { columns: readonly string[]; rowCount: number; total: number | null; rows?: TmVisibleData['rows'] };
  readonly form?: { dirty: boolean; fields: readonly { path: string; editable: boolean; errorCount: number; value?: TmJson }[] };
  readonly errors: readonly { source: string; code: string; path?: string; traceId?: string; message?: string }[];
  readonly actions: readonly { id: string; enabled: boolean; destructive: boolean; group: string; hasParams: boolean }[];
}
```

- **With page data,** the snapshot carries `visible.rows`, form `value`s and error `message`s.
- **Without it,** those are omitted and user text is masked. The string values of
  `state.query.q`, `.filter`, `.columnFilters`, `.custom` and of `state.parameters` become `"…"`.
  A `details` title, which is a record's display name, becomes the entity's singular label (see
  the example in §10.1).

### 9.7 AG-UI mapping (future; nothing ships now)

A later `@tellma/core/assistant` adapter maps the registry onto AG-UI. In AG-UI, the client's
facts reach the agent in `RunAgentInput` when a run starts.

- **Context:** a `RunAgentInput.context` entry, `{ description: 'tellma.page', value }`. Its
  value holds snapshots of `current` and `overlays`, plus the tenant, language, locale, calendar,
  display time zone, device time zone and UI version. The zones let the agent read and write
  instants in the user's time.
- **Tools:** the top context's enabled actions, each named by its id with `.` replaced by `__`,
  and described by its label, description and `params`. A call runs as
  `invoke(id, args, 'assistant')`. One more tool, `tellma__propose_values`, is added.
- **Never applied to a page:** the agent's `StateSnapshot` and `StateDelta` stay in its panel.

### 9.8 Help seams (reserved)

- **`helpKey`** appears on page contexts, spec 0027's navigation entries, `tmEntity` (spec 0029
  §3) and `tm-field` (spec 0031 §6.2). It matches `^[a-z][a-z0-9-]*(\.[a-z0-9][a-z0-9-]*)*$`,
  such as `gl.center` or `gl.center.fields.code`. Frameworks pass a declaration's `helpKey`
  through; nothing derives one.
- **`TM_HELP_ARTICLES`** (primary), an `InjectionToken<(key: string, language: string) =>
  Promise<TmLazyComponent | null>>`, is declared and never provided here. The help spec provides it
  and fills spec 0027's `help` slot (spec 0027 §7.2).

## 10. The state dump

The dump is one JSON document describing the tab at a moment: versions, environment, session,
contexts, requests, errors, telemetry and contributed sections. It is always downloadable, and a
support request stores it in its row (§12.3).

### 10.1 Contents

```ts
interface TmSupportDump {
  readonly format: 'tellma-support-dump'; readonly formatVersion: 1; readonly createdAt: string; // ISO 8601 with offset
  readonly includesPageData: boolean; readonly truncated: readonly string[];         // §10.2, §10.3
  readonly reported?: { traceId: string; kind: TmApiErrorKind; code: string };        // the error the dialog opened from
  readonly versions: { ui: string; platform: string; features: readonly { key: string; version: string }[];
    server: string | null; worker: string | null };                                  // worker: TmUpdates.version
  readonly distribution: { slug: string; displayName: string };
  readonly session: { sub: string | null; tenantId: number | null; tabId: string; state: string; online: boolean };
  readonly locale: { language: string; region: string; digits: 'Latin' | 'Native' | null; locale: string;
    dir: 'ltr' | 'rtl'; calendar: string; timeZone: string; deviceTimeZone: string; zoneAuto: boolean;
    theme: string; effectiveTheme: 'light' | 'dark' };                              // spec 0024 §7.1, §7.7, §7.8
  readonly environment: { userAgent: string; languages: readonly string[]; viewport: { width: number; height: number; dpr: number };
    standalone: boolean; reducedMotion: boolean; cookies: boolean; storage: { usage: number; quota: number; persisted: boolean } | null };
  readonly page: { url: string; routeTemplate: string; historyLength: number };       // url per §10.2
  readonly contexts: { current: TmPageContextSnapshot | null; overlays: readonly TmPageContextSnapshot[] };
  readonly requests: readonly TmRequestRecord[];     // ≤ 100
  readonly errors: readonly TmErrorRecord[];         // ≤ 50
  readonly telemetry: readonly TmTelemetryItem[];    // ≤ 100, sent or not
  readonly sections: Readonly<Record<string, TmJson>>; // contributed (below)
}
```

- **Contributed sections.** Other specs add a block through `TmSupport.registerDumpSection(name,
  read, destroyRef?)` (§12.1). Spec 0025 adds its `state` block (spec 0025 §15), and spec 0028 its
  attention block.
- **`name`** matches `^[a-z][a-z0-9-]{0,31}$` and is unique; a duplicate throws in development.
- **`read`** runs synchronously while the dump is built. It returns names, tags, counts and states,
  never page data.
- **Failures.** A throwing reader yields `{ "error": "<errorClass>" }`. A section over 64 KB
  becomes `{ "truncated": true }`, and its name joins `truncated`.
- **Never in a dump:** cookies, the profile cookie's name and e-mail, storage contents, launch
  document bodies, drafts, and request or response bodies.

**Example —** an abridged dump, page data left out: Farah's Center 5 in edit mode, with Code
missing.

```json
{ "format": "tellma-support-dump", "formatVersion": 1, "createdAt": "2026-10-20T14:03:12.512+03:00",
  "includesPageData": false, "truncated": [], "page": { "url": "/17/centers/5", "routeTemplate": "/:t/centers/:id" },
  "contexts": { "overlays": [], "current": { "kind": "details", "entity": "gl.Center", "title": "Center",
      "status": "ready", "state": { "id": 5, "mode": "edit", "dirty": true },
      "form": { "dirty": true, "fields": [{ "path": "Code", "editable": true, "errorCount": 1 }] },
      "errors": [{ "source": "validation", "code": "Required", "path": "Code" }] } },
  "requests": [{ "traceId": "4bf92f3577b34da6a3ce929d0e0e4736", "route": "centers/save", "outcome": "error", "status": 422 }],
  "sections": { "state": { "leader": { "isLeader": true, "hubStatus": "connected" } } } }
```

### 10.2 Page data

A support request includes page data by default. The support dialog's checkbox "Include the data
shown on the page" starts ticked each time the dialog opens, unless the size cap drops the page
data (§12.2), and its state is never remembered.

- **Ticked:** the dump carries page data, and `page.url` keeps its query:
  `/17/customers?q=al%20faris`.
- **Unticked:** contexts are serialised with `includeData: false` (§9.6), and `page.url` is the
  path alone: `/17/customers`.
- **Without the box,** a downloaded dump never includes page data: the Diagnostics panel's
  "Download diagnostic file" (§8.4, `TmSupport.downloadDump()`) and the Download only state
  (§12.2). The compose form's View, Download and Download instead follow the box.

### 10.3 Size cap

The cap is 2 MiB (`TM_SUPPORT_DUMP_MAX_BYTES`), measured as the UTF-8 bytes of the compact JSON
sent; `submit` also enforces it (§12.3). Over the cap, the builder drops these in order, stopping
once the dump fits, and names each step in `truncated`:

1. every context's `visible.rows` (`visibleRows`);
2. `telemetry` (`telemetry`);
3. the rest of the page data: contexts serialised with `includeData: false`, `page.url` the path
   alone and `includesPageData` false (`pageData`);
4. `sections` (`sections`).

What remains after step 4 is bounded well under the cap. A typical dump is 30–80 KB, or about
300 KB with 200 rows of 20 columns. The support dialog tells the user when step 1 or 3 runs
(§12.2).

### 10.4 Building and downloading

1. On request, `TmSupport` snapshots the contexts synchronously, before the support dialog opens.
2. It then imports `/support`, whose `tmBuildSupportDump(snapshot, { includeData })` adds the rest.
   Its only await is `navigator.storage.estimate()`, time-boxed to 200 ms.
3. The support dialog builds its dump from that snapshot before the compose form replaces the
   placeholder, and rebuilds it when the box changes. View, Download, Download instead and Send
   use the dump it holds, waiting for a rebuild in flight, so what the form shows is what Send
   submits.
4. **Download** goes through a temporary `<a download>` and a revoked object URL. The file is named
   in local time with Latin digits: `<slug>-diagnostics-<yyyyMMdd-HHmmss>`, such as
   `acme-diagnostics-20261020-140312.json`.

## 11. The screenshot

The screenshot is one frame of a screen-capture stream, taken after the browser's own consent
prompt. Where capture does not exist, the user attaches an image.

```ts
interface TmScreenshot { readonly blob: Blob; readonly width: number; readonly height: number;
  readonly surface: 'browser' | 'window' | 'monitor' | 'file' }   // file: attached by the user
declare class TmScreenCapture {                    // @tellma/core/support
  readonly supported: boolean;                     // isSecureContext and navigator.mediaDevices?.getDisplayMedia exists
  capture(): Promise<TmScreenshot | null>;         // call inside a click handler; null: the user declined
  fromFile(file: File): Promise<TmScreenshot>;     // the attach fallback: png, jpeg, webp
}
declare const TM_DISPLAY_MEDIA: InjectionToken<(options: DisplayMediaStreamOptions) => Promise<MediaStream>>; // test seam
```

### 11.1 Capture

1. The user presses "Take screenshot". In that click handler, before any `await`, the dialog enters
   `capturing`. Its panel and backdrop get `visibility: hidden`, so no layout moves.
2. Still in the handler, the dialog calls `getDisplayMedia`, which requires transient activation.
   It passes `{ video: { displaySurface: 'browser' }, audio: false, preferCurrentTab: true,
   selfBrowserSurface: 'include', surfaceSwitching: 'exclude', monitorTypeSurfaces: 'exclude' }`.
   Each hint applies only where the browser recognises it.
3. **Granted:** a muted, off-screen `<video>` plays the stream. After two video frames
   (`requestVideoFrameCallback`, else two `requestAnimationFrame`s after `loadeddata`), the frame
   is drawn to a canvas of `videoWidth × videoHeight`. Every track stops, and `toBlob` makes a PNG.
4. The dialog leaves `capturing` and shows the preview.

- **Declined** (`NotAllowedError`): the dialog returns as it was.
- **No frame within 3 s, or any other error:** "Couldn't take a screenshot. You can attach one
  instead."
- **Oversize.** An image over 8 MiB, captured or attached, is redrawn in its own type at half the
  dimensions, repeatedly, until it is at most 8 MiB.
- **Policy.** `index.html` carries `Permissions-Policy: display-capture=(self)` (spec 0024 §6).
  The CSP already admits `blob:` and `data:` images. DOM rasterising (html2canvas) is approximate
  and is not used.

### 11.2 Support matrix and the fallback

| Browser | Capture | What the user sees |
|---|---|---|
| Chromium desktop (Chrome, Edge) | the current tab, preselected | one prompt to share this tab |
| Firefox and Safari desktop | a window or a screen | the browser's picker |
| Everything else (Android, iOS) | none | "Take screenshot" hidden; "Attach image" only |

- **"Attach image"** is always offered: a file input (`accept="image/png,image/jpeg,image/webp"`)
  with the hint "Use your device's screenshot keys to capture the whole screen."
- **The preview** adds "Check the screenshot. It may show other windows." when the surface is
  `window` or `monitor`, and offers Download (`<slug>-screenshot-<yyyyMMdd-HHmmss>.<ext>`, `<ext>`
  being `png`, `jpg` or `webp` by the image's type).

## 12. Support requests

When the distribution opts in, the user can send the dump and screenshot from inside the app. The
dialog uploads the screenshot, when there is one, as a tenant blob, and one `submit` writes a
`core.SupportRequests` row that holds the dump. The tenant's administrators get a bell notification
and the provider's support team an alert. The sender and administrators see the request on a stock
page. The provider's support team reads it over the admin surface.

### 12.1 Opt-in and `TmSupport`

`Tellma:Support:Enabled` (default false) reaches the SPA as `DistributionInfo.support { enabled,
providerAccess }` (spec 0011 §5.9). `providerAccess` is true when the admin surface is mapped.

```ts
declare class TmSupport {                          // primary facade
  readonly canSend: Signal<boolean>;               // below
  open(options?: { error?: TmApiError }): void;    // the support dialog (§12.2); never throws
  copyDiagnostics(): void;                         // TmRequestLog.copy(), inside the gesture
  downloadDump(): void;                            // §10.4, without page data
  registerDumpSection(name: string, read: () => TmJson, destroyRef?: DestroyRef): void; // §10.1
}
```

- **`canSend`** is true when support is enabled, the route is a tenant route, the session is an
  active member's, `TmApi.online()` is true, and the tenant is not read-only.
- **Entry points:** "Report a problem" in the session menu (spec 0027 §10.1) and in the
  Diagnostics panel (§8.4), and the 5xx dialog's "Report this problem" (spec 0024 §11).
- **From the 5xx dialog,** `open` receives the error. The dump records it as `reported`, and its
  trace id heads `traceIds`. The 5xx dialog stays open beneath, so it is in the snapshot and the
  screenshot.
- **Instant open:** `open` shows the `TmModal` in the click's frame with a final-geometry
  placeholder, snapshots the contexts, then imports `/support`.

### 12.2 The support dialog

The dialog has six states. Its notices are spec 0005's `tm-alert` (spec 0005 §13).

| State | What it shows |
|---|---|
| Compose (`canSend`) | The compose form (below) |
| Download only | No message and no Send: "Download the diagnostic file and screenshot, then send them to your support contact.", with the reason when one applies ("You're offline.", "This tenant is read-only."); the file never includes page data (§10.2) |
| Sending | The fields lock; Send shows busy, with the screenshot upload's progress when there is one |
| Sent | "Your request number is 1042." and who was alerted (below); a `tm-reference` "Request" copying "{displayName} support request 1042, tenant 17"; Close |
| Failed | An inline `tm-alert` with the problem's errors (`TmServerText.error`), else its title (`TmServerText.problem`, spec 0024 §7.5); its trace id chip; Try again, Download instead |
| Unavailable | The `/support` import failed: "This isn't available offline. Press {keys} to copy diagnostics." `tm-kbd` renders the keys, which still work from the primary; Close |

- **The compose form,** top to bottom: the title "Report a problem"; the optional message "What
  happened?"; "What's included", with the diagnostic information (View, Download) and the
  screenshot slot; the checkbox "Include the data shown on the page", ticked on open (§10.2); the
  page-data notice; the warning; and the buttons Download instead (link style), Cancel and Send
  (primary).
- **The screenshot slot** offers Take screenshot and Attach image, or shows the preview with
  Remove, Retake and Download. It is a fixed 16:9 box.
- **The page-data notice** is one `tm-alert` (`kind="info"`, `live="polite"`) that shows when the
  size cap cut page data from a ticked box's dump (§10.3), worded by the last step that ran. After
  step 1 it reads "The rows shown on the page are too large to send, so they're left out." After
  step 3 it reads "The data shown on the page is too large to send, so it's left out.", and the
  checkbox shows unticked and disabled, so the warning takes its unticked variant.
- **The warning** has tone warning and `role="note"`. With `providerAccess`, it reads "Your
  administrators and the {displayName} support team will see what you send, including the data
  shown on the page and any screenshot." Without, it reads "Your administrators will see what you
  send, including the data shown on the page and any screenshot." While the box is ticked, both
  continue "If the page shows something you may not share, untick 'Include the data shown on the
  page' or don't send." While it is unticked, "the data shown on the page and" drops out, and both
  continue "If the screenshot shows something you may not share, remove it or don't send."
  `{displayName}` is the localized display name, `TmLocalization.displayName` (spec 0024 §3.1).
- **The Sent text** continues with who was alerted, from the receipt's `administratorsNotified` and
  `providerAccess`:
  - both: "Your administrators and the {displayName} support team have been notified."
  - administrators only: "Your administrators have been notified."
  - `providerAccess` only: "The {displayName} support team has been notified."
  - neither: "Quote it when you talk to support."
- **Fields.** The message holds at most 2,000 characters, with `dir="auto"` and a counter from
  1,800. View shows the dump as read-only LTR monospace JSON.
- **Keyboard and ARIA.** `role="dialog"` is labelled by the title, and focus starts in the message.
  Ctrl+Enter sends. Escape and Cancel abort an upload in flight, but a sent `submit` is awaited.
  Capture progress is announced politely.
- **Layout.** Below `sm`, the dialog is full screen (spec 0010 §D.2). It mirrors in RTL, with JSON
  and trace ids LTR-isolated.

**Send flow:**

1. The dialog takes the dump it holds for the box's state (§10.4).
2. With a screenshot, `TmApi.upload('support-screenshot', screenshot.blob, { fileName:
   'screenshot.<ext>', signal, onProgress })` (spec 0024 §10.1; `<ext>` as in §11.2) returns a
   staged `BlobDescriptor` (spec 0017 §3.1). Without one, this step is skipped.
3. `TmApi.write('support-requests/submit', submission)`, with the dump inline, `includesPageData`
   copied from the dump, and the staged `screenshotId`, answers the receipt.

On a failure, Try again re-sends only the failed step, except that a `submit` refused at
`screenshotId` re-uploads the screenshot first. A staged screenshot stays attachable for 24 h
(`StagingTtl`, spec 0017 §1.3).

**Example —** the submission and its answer. `traceIds` lists the trace ids of the dump's errors
and slow requests first, then those of the newest requests, at most 20.

```json
{ "message": "Saving a center hangs for a minute, then fails.", "uiVersion": "1.4.0+3f9c2ab1e5d4…",
  "routeTemplate": "/:t/centers/:id", "pageUrl": "/17/centers/5",
  "traceIds": ["4bf92f3577b34da6a3ce929d0e0e4736", "0af7651916cd43dd8448eb211c80319c"],
  "includesPageData": true, "dump": { "format": "tellma-support-dump", "formatVersion": 1, … }, "screenshotId": 9001 }
→ 200 { "id": 1042, "administratorsNotified": true }
```

### 12.3 Server contract

```csharp
// Tellma.Core — table core.SupportRequests; resource core.SupportRequest (spec 0014 §2)
// [Stack(Operations = Query | Details | Delete | Enlist, DefaultOrderBy = "Id desc")]
[ApiResource(Segment = "support-requests", Mcp = McpExposure.Hidden)]        // dumps and screenshots can hold other users' page data
[DefaultSelect("Id,Message,UiVersion,ServerVersion,RouteTemplate,ScreenshotId,IncludesPageData,ExpiresAt,CreatedAt,CreatedById")]
public sealed class SupportRequest : TopLevelEntity                            // Id and the four audit columns
{
    [MaxLength(2000), Searchable] public string? Message { get; set; }
    [MaxLength(64)] public string UiVersion { get; set; } = "";
    [MaxLength(64)] public string ServerVersion { get; set; } = "";           // DistributionVersion at submit
    [MaxLength(256)] public string? RouteTemplate { get; set; }
    [MaxLength(2048)] public string? PageUrl { get; set; }                    // the path; its query only when IncludesPageData
    [MaxLength(700)] public string? TraceIds { get; set; }                    // comma-separated, at most 20
    [MaxLength(512)] public string? UserAgent { get; set; }                   // the request header
    public bool IncludesPageData { get; set; }
    [JsonColumn] public string DumpJson { get; set; } = "";                   // the dump (§10), capped by §10.3
    [BlobReference("support-screenshot", MaxSizeBytes: 8_388_608)] public int? ScreenshotId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }                             // CreatedAt + RetentionDays
}
public sealed record SupportRequestSubmission(string? Message, string UiVersion, string? RouteTemplate,
    string? PageUrl, IReadOnlyList<string> TraceIds, bool IncludesPageData, JsonElement Dump, int? ScreenshotId);
public sealed record SupportRequestReceipt(int Id, bool AdministratorsNotified); // true: recipients besides the sender
public sealed class SupportRequestService : EntityService<SupportRequest>, IEnlists<SupportRequest>
{
    [ApiAction("submit", MemberOnly = true, Mutation = true, Idempotent = false, Mcp = McpExposure.Hidden)]
    public Task<SupportRequestReceipt> SubmitAsync(SupportRequestSubmission submission);
}
public sealed class SupportRequestAccessCriteria : IAccessCriteriaProvider; // core.SupportRequest; CreatedById = me() for Read, reason self
public sealed class SupportOptions { public bool Enabled { get; set; } [Range(1, 365)] public int RetentionDays { get; set; } = 30;
    [Range(1, 1000)] public int MaxRequestsPerUserPerDay { get; set; } = 20; }  // Tellma:Support (spec 0011 §8); Enabled defaults to false
public static class SupportTelemetryNames                                 // Tellma.Core.Abstractions.Api
{
    public const string MeterName = "Tellma.Core";
    public const string Requests = "tellma.support.requests";             // counter, {request}; screenshot, page_data ∈ true | false
    public const string ScreenshotTag = "screenshot", PageDataTag = "page_data";
}
```

- **`submit` builds one row,** filling `ServerVersion`, `UserAgent` (the header, cut to 512
  characters) and `ExpiresAt` on the server, and `DumpJson` with the dump's raw text as received,
  which is also what the cap measures. `PageUrl` loses its query when `IncludesPageData` is false
  or the URL is over 2,048 characters, then is cut to 2,048. The JSON body limit
  (`MaxJsonBodyBytes`, 8 MB, spec 0016 §8.1) admits a full dump inline.
- **Reading the dump.** `[JsonColumn]` keeps `DumpJson` out of the Queryex schema (spec 0012 §2.6),
  so no query, list or `[DefaultSelect]` can name it. The details `get` carries it, because its
  entity query asks for every mapped property (spec 0012 §11.4, spec 0015 §5.3).
- **One transaction.** `submit` enlists its insert with the action frame's `IOpenWriteHost` and
  awaits `PersistAsync` (spec 0015 §13.3). So spec 0017 §4.1's attach rule confirms a screenshot in
  the same transaction: kind `support-screenshot`, staged by the caller, unexpired, used once. It
  takes two round trips besides the administrator lookup.
- **Administrators are notified in that transaction.** Before it enlists, `submit` reads
  `IAdministratorDirectory.GetAdministratorIdsAsync()` (spec 0014 §10), drops the sender, and keeps
  the recipients in the service's scoped state; `AdministratorsNotified` is computed from them. The
  service's `ValidateAsync` adds two loads to the enlisted save's validation round (spec 0015
  §7.2): `ByIds<User, int>` over the recipients and the sender
  (`Id,Name,Name2,Name3,PreferredLanguage`), and the daily limit's `Count` of the caller's
  requests since midnight in the tenant zone (spec 0016 §6.4). Its `ContributeAsync` hook raises
  `core.support.requested` (below) on the persist context (`context.Notify`, spec 0015 §13.1), one
  request per content slot of the recipients' languages (slot 1 when unset), so the rows commit
  with the request and `inbox.changed` fires after commit (spec 0021 §3.4). No recipients, no
  request. It is not `InboxOnly`, so every other channel follows each recipient's preference.
- **A delete withdraws.** On a `DeletePersistContext`, the same hook withdraws
  `core.SupportRequest:<id>` for each deleted id (`context.Withdraw`, spec 0021 §3.5), whether an
  administrator or retention (§12.8) deletes.
- **After commit,** once `PersistAsync` returns, `submit` adds one to `tellma.support.requests`,
  tagged by `ScreenshotId` being set and by `IncludesPageData`, and logs
  `SupportRequestSubmitted(TenantId, RequestId)` at Information; the logger scope carries the
  tenant (spec 0011 §9). A rolled-back submit records neither. No tenant or user tag (spec 0016
  §10.1).
- **The provider's alert.** An alert query under `infra/monitoring/` notifies the provider's
  support team of any increase of `tellma.support.requests`, beside §6.4's client alerts and spec
  0011 §9's. On-premises, the operator routes it.
- **Read-only tenant.** A `ReadOnly` tenant refuses with 403 `tenant-read-only` (spec 0011 §3.3).

The notification type, declared by `CoreFeature` (spec 0021 §2.3):

| Member | Value |
|---|---|
| `Key`; `Category` | `core.support.requested`; `support`, a Core category labelled by `NotificationCategory_support` |
| `Mutable`, `Severity`, `Toast`, `Inbox` | `false`, `Warning`, `true`, `true` |
| Target | `core.SupportRequest`, `TargetId` = the request's id |
| Arguments | `{ id, actorName }`: `id` is the request number as an invariant-culture string; `actorName` is the sender's name in the request's content slot, falling back to slot 1 (as spec 0033 §7); never the message. `ActorUserId` is the sender |
| Text | `Notification_core_support_requested` in Core's `Strings`: "{actorName} reported a problem (request {id})."; the settings label `NotificationType_core_support_requested`: "Problem reports" (spec 0021 §4.3) |
| Duplicates | `DedupKey = 'core.SupportRequest:<id>'`, `OnDuplicate = Suppress` |

`submit` fails with these codes:

| Code (resx `SupportRequests.*` in Core) | Path | Rule |
|---|---|---|
| `SupportRequests.Disabled` | root | `Tellma:Support:Enabled` is false |
| `SupportRequests.DailyLimit` | root | the caller already sent `MaxRequestsPerUserPerDay` requests since tenant midnight |
| `SupportRequests.TraceId` | `traceIds[i]` | not 32 lowercase hex, or more than 20 ids |
| `SupportRequests.DumpInvalid` | `dump` | not an object, its `format` is not `tellma-support-dump`, or its `includesPageData` differs from `includesPageData` |
| `SupportRequests.DumpTooLarge` | `dump` | over 2 MiB of UTF-8 |
| `Blob.NotAttachable`, `Blob.DuplicateReference` | `screenshotId` | spec 0017 §4.2 |

### 12.4 Blob kind `support-screenshot`

- **Declared** by `SupportRequest.ScreenshotId` (spec 0017 §1.3): preset `Attachment`, `MaxSize`
  8 MiB, read access `OwnerRead` through `core.SupportRequest` (spec 0017 §5.2).
- **Images only.** `CoreFeature` narrows it with `BlobKind("support-screenshot", p => p with {
  AllowedContentTypes = ["image/png", "image/jpeg", "image/webp"] })`. The preset's thumbnail
  stays, so the details page shows one (§12.5).
- **Always fits.** §11.1's redraw keeps every screenshot within 8 MiB.
- **Upload route:** `POST /{t}/blobs/support-screenshot?fileName=` (spec 0017 §8.1).

### 12.5 The Support requests page (`/{t}/support-requests`)

The page is a stock search and details page on the CRUD frameworks, declared in the implicit
`core` feature (spec 0024 §2.3):

```ts
export const supportRequests = tmEntity('core.SupportRequest', {
  section: 'administration', order: 100, icon: 'life-buoy',
  visibleWhen: ({ distribution }) => distribution.support.enabled,
  columns: ['Id', 'CreatedAt', 'CreatedBy', 'Message', 'RouteTemplate', 'Screenshot'],
  details: () => import('@tellma/core/support').then(m => m.TmSupportRequestDetails),
});
```

- **Search.** There is no Create, because the stack has no `Save`. Free text searches `Message`.
  Delete needs `core.SupportRequest × Delete`. `Screenshot` is the blob navigation of
  `ScreenshotId`, a file column (spec 0029 §6.1), blank when there is none.
- **Launcher entry and context.** While support is enabled, the entry shows for every member,
  since the self-scope grants `Read` (§12.6); its `visibleWhen` hides it otherwise (spec 0027
  §3.1). The page context is the frameworks' (§9.3).
- **Details** (markup only, `TmDetailsKit`) shows Number, Sent, From, Page (an in-app link), the UI
  and server versions with a "differs" badge, Includes page data and Expires. Then come the message
  (`dir="auto"`), the trace ids as `tm-reference` chips, and the buttons Download diagnostic file
  (the stored dump) and Download screenshot.
- **Dump summary.** `tm-support-dump-summary` (`TmSupportDumpSummary`) parses the `get`'s
  `dumpJson` (§12.3) and loads the screenshot's thumbnail through the blob GET (spec 0017 §5.3),
  opening the full image on click. It shows the screenshot, errors, slow requests, browser and
  locale.

### 12.6 Access

- **Self-scope:** a member reads the requests they sent (`CreatedById = me()`, spec 0014 §5.3).
- **Administrators** read and delete every request through roles holding `core.SupportRequest ×
  Read` and `× Delete` (spec 0014 §2). The built-in administrator role holds both.
- **Blobs.** The securable is not sensitive. The screenshot follows "who can read the owner can read
  its blobs" (spec 0017 §5.2).

### 12.7 The admin surface for the provider's support team

Three read routes join spec 0011 §5.9's admin surface:

```csharp
// Tellma.Core.AspNetCore — under /api/admin/v1, policy Tellma.ControlPlane, mapped with the admin surface
//   GET /api/admin/v1/tenants/{id}/support-requests?before={requestId}&take={1..100}  → AdminSupportRequest[], newest first
//   GET /api/admin/v1/tenants/{id}/support-requests/{requestId}/dump                 → the dump, as a JSON attachment
//   GET /api/admin/v1/tenants/{id}/support-requests/{requestId}/screenshot           → the screenshot, as an attachment
public sealed record AdminSupportRequest(int Id, DateTimeOffset CreatedAt, string CreatedByName, string CreatedByEmail,
    string? Message, string UiVersion, string ServerVersion, string? RouteTemplate, string? PageUrl,
    IReadOnlyList<string> TraceIds, string? UserAgent, bool IncludesPageData, bool HasScreenshot, long DumpBytes,
    DateTimeOffset ExpiresAt);                    // HasScreenshot: ScreenshotId is set; DumpBytes: DATALENGTH(DumpJson)
```

- **Reads** open a tenant scope as the system user with `allowNonActive: true` (spec 0011 §4.4), so
  a `ReadOnly` tenant's requests stay readable. The list is one read statement. The dump route uses
  the details read (spec 0015 §5.3); the screenshot route reads the row's `ScreenshotId` the same
  way and resolves it with spec 0017's `ResolveAsync`, whose `Read` check the system user passes
  unrestricted (`UserAccess.System`, spec 0014 §5.1). A `Suspended` tenant is 403
  `tenant-suspended`. A `Retired` or unknown tenant, an unknown request, or a request without a
  screenshot on the screenshot route is 404 `not-found`.
- **Audit.** Every call logs `SupportRequestRead(TenantId, RequestIds, ClientId)` at Information.
- **Paging is by key,** with no paging record: `Id < before` (all when absent), `take` 50 by
  default. For "request 1042, tenant 17", support reads `?before=1043&take=1`, then the dump and
  the screenshot.

### 12.8 Retention

`ExpiresAt` is the submit time plus `RetentionDays` (30). Spec 0020's daily `core.file-retention`
deletes expired rows as it does exports (spec 0020 §14.4), so their screenshots are released and
their notifications withdrawn (§12.3); the dump goes with the row.

## 13. Testing

Every suite runs on each PR, except the manual capture check, which runs per release.

| Suite | Tier | Pins |
|---|---|---|
| `TmTelemetry` | unit, PR | the envelope; the guard (development throw, production drop and count); flush triggers on a fake clock; 16 KB and 100-item batches; one batch on `pagehide`; the 200 cap and its overflow order; offline and session holds; sampling at rates 0 and 1 |
| Redaction, `TmErrorHandler` | unit, PR | §4.3's table plus e-mail, URL, Arabic and digit vectors; stack reduction; each source and filter; fingerprint stability; once per fingerprint per 10 minutes; the 30-item ceiling |
| Measurements, `TmRequestLog`, `TmAppVersion` | unit in browser mode, PR | visit boundaries on navigation and `pagehide`; the `chromeMs` and `ms` stamps; absent metrics stay absent; summaries per window; slow at 3 s while pending; the ring of 100; `diagnosticsText` goldens in `en`, `ar` and tenantless; `compare` vectors (newer, older, pre-release, another commit, the same build) and `serverNewer` |
| Registry and dispatcher | unit and harness, PR | scope resolution (page, overlay, root); replacement with actions carried over; merged order; dispatcher steps 1–6 for each origin; shortcuts live only while `top`; snapshots with and without page data, masks included |
| Dump | unit, PR | §10.1's contents; a section over its cap and a throwing reader; the four truncation steps in order; nothing of the "never" list with seeded storage and cookies; `page.url` per §10.2 |
| Capture, support dialog | harness, PR | `TM_DISPLAY_MEDIA` fakes (granted, declined, no frame in 3 s, oversize needing two halvings); the dialog hidden during capture; the file fallback and its extension in the upload's file name; every state of §12.2; the page-data box ticked on every open; the page-data notice after step 1 and after step 3, then the box unticked and disabled and the unticked warning, and the submitted dump and its `includesPageData` matching it; a send with and without a screenshot; a retry of only the failed step, and a re-upload when `submit` refuses the screenshot; the warning per `providerAccess` and the box; the Sent text per `administratorsNotified` and `providerAccess`; the other-windows note |
| C# relay | integration, PR | §6.2 row by row; 204 with dropped items; 429 from the rate policy; skew correction; no session renewal; an in-memory exporter sees custom events named by `microsoft.custom_event.name`, Warning traces, the item's trace id on the record and no exported span; counter tags only from the closed sets |
| C# support | integration on real SQL, PR | `submit`'s codes; the dump stored as sent; `UserAgent` and `PageUrl` cut to length; the screenshot attach and a submit without one; the daily limit; 403 on a read-only tenant; the self-scope; a details `get` carries `DumpJson` and a query cannot select it; `core.support.requested` to every administrator but the sender, one request per content slot, none when the sender is the only one, and `administratorsNotified` each way; a delete, by an administrator or by retention, withdraws it; `tellma.support.requests` once per committed submit with both tags, and `SupportRequestSubmitted`; a rolled-back submit notifies, counts and logs nothing; all three admin routes on an `Active` and a `ReadOnly` tenant, the screenshot route's 404, and `SupportRequestRead`; retention deletes rows and releases screenshots |
| Playwright on Acme | e2e, PR | the shortcut copies diagnostics (clipboard read in Chromium); a 5xx → Report this problem → fake capture → Send → the Support requests page shows the request and its screenshot, and an administrator's bell shows the notification; the `pagehide` flush reaches the relay; offline hold, then flush; telemetry disabled sends nothing; the dialog and panel at 390, 768, 992 and 1440 px in `en` and `ar` |
| Manual | per release | real capture on Chrome, Edge, Firefox and Safari desktop, recorded |

## 14. Definition of done

- **Ships.** Every part of §1 ships with its API golden and within its budget. Every §13 suite is
  green on Ubuntu and Windows. Specs 0011, 0014, 0016, 0017, 0018, 0020 and 0021 carry the
  sections listed under Context.

| Rule | How this design meets it | Pinning test |
|---|---|---|
| Instant warm launch | nothing here awaits the network; the load collector loads at idle; telemetry settings come from the cached `distribution` document | spec 0024's `warm-launch.spec` with `/api/client-telemetry` held open |
| Instant feedback | the support dialog opens in the click's frame (§12.1); action views render `running` at once (§9.5); Copy diagnostics is synchronous (§8.3) | `support-dialog.spec` (the dialog on the next frame); unit: `diagnosticsText()` under 2 ms for 100 records |
| Non-blocking | telemetry is `keepalive`, unread, never awaited; capture and dump building never block navigation; closing the dialog aborts its upload | `telemetry.spec`: a hung relay changes nothing on screen; navigation during a send works |
| Stable layout | capture hides the dialog with `visibility`; the screenshot slot is a fixed 16:9 box; the request grid has fixed column widths | `layout-shift.spec`: zero shift across capture, preview and send |
| RTL | the dialog and panel mirror; JSON, trace ids and the diagnostics text are LTR-isolated; the shortcut matches by physical key | every §13 e2e also in `ar`; unit: `Mod+Alt+Shift+D` on an Arabic layout |
| WCAG AA | the dialog is labelled, with focus start and return and Escape; polite announcements; the request grid is one tab stop with arrow keys | axe on every dialog state, the panel and the Support requests pages (light, dark, LTR, RTL); `keyboard-support.spec` |

- **Page context:** the frameworks' contexts on the Support requests pages (§12.5). The support
  dialog registers none, because its snapshot is taken before it opens.
- **Telemetry:** Appendix A.1.
- **What Acme adds:** support on in development (spec 0018 §6.2); the credit-review `tmPage` with
  its own context and one `primary` action with a shortcut (§2); and `acme.credit-exposure.run`.

## Decisions record

1. **Collect errors, launch and visit timings, load paint metrics, request outcomes, slow requests
   by trace id, and action usage** — guided by: no tenant text leaves the tab, closed names,
   bounded volume, joined context, never blocking. Considered full RUM with replay (privacy,
   weight) and errors only (blind to "slow") (§3, §5).
2. **Send `keepalive` batches to one relay that writes OpenTelemetry log records and two counters;
   route errors through `TmErrorHandler`** — considered the Application Insights web SDK (75 KB,
   a connection string in the bundle, no on-premises story), `sendBeacon` (no `Tellma-Client`, so
   it fails CSRF) and browser OTLP (another endpoint) (§4, §6).
3. **Quote slow requests by client trace id: a log that shows pending calls, Copy diagnostics on
   `Mod+Alt+Shift+D`, and `tm.api.slow`** — Teams uses `Ctrl+Alt+Shift+1`. Considered console
   logging (never opened) and a toast per slow call (§8).
4. **A screenshot: one `getDisplayMedia` frame with the dialog hidden, else an attached image** —
   considered DOM rasterising (approximate), Element Capture (Chromium) — (§11).
5. **Where support data goes: always downloadable; when enabled, one `core.SupportRequests` row
   holding the dump, and the screenshot alone in a blob** — the dump is one write with no second
   blob, the details read carries it, and a query cannot select it, so lists stay light; the
   screenshot gets an image-only policy and a thumbnail. Considered the distribution's blob storage
   alone (devops only) and a control-plane upload (credentials in every distribution, no
   tenant-admin view) — (§12.3, §12.4).
6. **The UI version: `X.Y.Z+<sha>`, equal to `Tellma-Build`, ordered by SemVer precedence** — one
   number per build, and the client tells a newer server from an older one without asking the
   worker. Considered a separate SPA build id (two numbers for one build) and a bare hash
   (unreadable, unordered) — (§7).
7. **Request-linked client records are logged under the request's trace id** — one `operation_Id`
   search finds both sides — (§6.4).
8. **A support request includes page data unless the user unticks it, and a file downloaded where
   the box is not shown never does** — most problems need the rows to reproduce, and the warning
   tells the user who will see them — (§9.6, §10.2, §12.2).
9. **One dispatcher; actions belong to the nearest page scope** — the rules hold once for every
   origin, and a dialog's action never lands on the page beneath — (§9.4, §9.5).
10. **The context reaches an agent as AG-UI run input and frontend tools** — the agent's state
    events flow to the client, so they never write a page — (§9.7).
11. **A support request alerts people: a bell notification to the tenant's administrators and a
    `tellma.support.requests` alert for the provider's support team** — users expect "Report a
    problem" to reach someone, and the Sent state says who it reached — (§12.2, §12.3).

## Review flags

1. **A relay instead of the Application Insights web SDK** (§6). Alternative: the SDK. Flips if
   session replay or browser-side dependency tracking is needed.
2. **Page data on by default in a support request** (§10.2). Alternative: off by default. Flips on
   a privacy review, or a customer that forbids it.
3. **Request outcomes aggregated per flush window** (§5.3). Alternative: one item per request.
   Flips if per-request latency analysis is needed and the volume is affordable.
4. **Client errors as Warning traces, not the `exceptions` table** (§6.4). Alternative: a synthetic
   exception so errors appear in the Failures blade. Flips if the team lives in that blade.
5. **The dump in the row, the screenshot alone in a blob** (§12.3, §12.4). Alternative: the dump as
   a second blob. Flips if dumps grow the tenant database noticeably.
6. **Per-tenant admin reads, no cross-tenant index** (§12.7). Alternative: a catalog index row per
   request. Flips if support must triage requests nobody quoted.
7. **30-day retention; distribution-level opt-in only** (§12.8, §12.1). Alternatives: 90 days; a
   tenant setting that turns support off. Flips on a privacy policy, or a customer that forbids
   sending screens to the provider.
8. **`Mod+Alt+Shift+D` for Copy diagnostics** (§8.3). Alternative: Teams' `Mod+Alt+Shift+1`, or no
   shortcut. Flips on a conflict in a target market's keyboard layouts.
9. **Per-tab sampling, default 1, and no user opt-out** (§3.5). Alternatives: per-item sampling; a
   preference that turns telemetry off. Flips if ingestion cost grows, or on a legal review.
10. **`confirm` carries a title key and a message key with arguments** (§9.4). Alternative: one
    message key under a generic title. Flips if every page's confirmations fit one title.

## Appendix A — Telemetry catalogue

### A.1 This spec's items

Every item also carries `route` and `pageKind` (§3.1); sampled items carry `sampleRate`.

| Name | Kind | Sampled | Emitted | Properties |
|---|---|---|---|---|
| `tm.error.captured` | error | no | §4 | `source`, `errorClass`, `fingerprint`, `kind`, `code`, `status`, `message`, `stack`, `recentTraceIds`; the item's `traceId` |
| `tm.page.visit` | timing | yes | §5.1 | `ms`, `chromeMs`, `visitS`, `entry`, `outcome` |
| `tm.vitals.load` | event | yes | §5.2 | `ttfbMs`, `fcpMs`, `lcpMs`, `navigationType` (`navigate`, `reload`, `back_forward`, `prerender`), `controlled` |
| `tm.api.requests` | event | no | §5.3 | `api`, `count`, `ok`, `failed`, `cancelled`, `network`, `sumMs`, `maxMs`, `over1s`, `over3s`, `background` |
| `tm.api.slow` | event | no | §5.3 | `api`, `durationMs`, `outcome`, `status`, `code`; the item's `traceId` |
| `tm.action.invoked` | event | yes | §9.5 | `action`, `origin`, `outcome`, `ms` |
| `tm.telemetry.dropped` | event | no | §3, §4 | `count`; `reason` ∈ `overflow`, `name`, `prop`, `foreign`, `throttled` |
| `tm.support.diagnostics-copied` | event | no | §8.3 | `via` ∈ `shortcut`, `button`, `dialog` |
| `tm.support.dump-downloaded` | event | no | §10.4 | `pageData` |
| `tm.support.screenshot` | event | no | §11 | `outcome` ∈ `taken`, `declined`, `failed`, `attached`; `surface` |
| `tm.support.sent`, `tm.support.send-failed` | event | no | §12.2 | `screenshot`, `pageData`; `stage` ∈ `upload`, `submit`, and `kind` |

### A.2 Area owners

| Owner | Areas |
|---|---|
| this spec | `tm.error`, `tm.page`, `tm.vitals`, `tm.api.requests`, `tm.api.slow`, `tm.action`, `tm.telemetry`, `tm.support` |
| spec 0024 | `tm.launch`, `tm.boot`, `tm.config`, `tm.sw`, `tm.api` (other names), `tm.urls`, `tm.session`, `tm.l10n` |
| spec 0025 | `tm.store`, `tm.sync`, `tm.realtime`, `tm.prefs`, `tm.drafts` |
| spec 0027 | `tm.shell`, `tm.launcher`, `tm.shortcut` |
| spec 0028 | `tm.attention` |
| spec 0029 | `tm.entity`, `tm.queryex` |
| spec 0030 | `tm.search` |
| spec 0031 | `tm.details` |
| spec 0032 | `tm.admin` |
| spec 0034 | `tm.report`, `tm.studio` |
| spec 0035 | `tm.dashboard`, `tm.home` |
| a feature | its key (`gl`, `acme`) |

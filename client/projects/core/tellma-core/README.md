# @tellma/core

The Tellma app framework: the runtime a distribution's single-page app imports. It splits into
entry points along its lazy-loading lines, so the warm-launch bundle carries only what every page
needs, and each lazy boundary (a page family, the hub, the support tools) is an entry point of its
own.

| Entry point | Holds | Loaded |
|---|---|---|
| `@tellma/core` | The distribution API: `provideTellma`, `tmFeature`, the declaration functions (`tmEntity`, `tmPage`, `tmCodedReport`, `tmWidget`, `tmOverride`) and `TM_APP_DECLARATIONS`, the merged declarations; the client metadata's types, its loaders (`tmMetadataLoaders`) and the loaded Core chunk (`TM_CLIENT_METADATA`); the HTTP layer (`TmApi`, `TmApiError`, `TmProblem`), its plain-JSON value types (`TmJson`, `TmDecimal`, `TmId`) and the exact decimal helpers `TmDecimals`; the seams through which telemetry (`TmTelemetry`), the request log (`TmRequestLog`) and page scopes (`provideTmPageScope`) are provided; the session (`TmSession`, `tmSessionGuard`, `tmParseProfileCookie`), the launch mark (`TmLaunch`) and the single-membership rule (`tmSingleLiveTenant`); the boot script's result types and the app clock (`TM_NOW`) | eager |
| `@tellma/core/config-check` | `tmCheckAppConfig`, the check of an app's configuration against the client metadata, which the build runs in Node and a development app at startup | lazy |
| `@tellma/core/private` | Wiring shared by the platform's own entry points and packages; no API goldens, no stability guarantee, never imported by app code | — |

The other entry points of the framework arrive with the features they hold, each in the folder of
its name. The package also ships two plain assets: `boot/tm-boot.template.js`, the boot script's
template, and `styles/tm-boot-page.css`, the boot page's styles.

## Boot

Before Angular starts, a blocking classic script, `tm-boot.js`, decides the session, the language,
the direction, the theme and the launch target with no network. Its logic is the pure function
`tmBoot(env)` in `src/boot/`, over an injected environment; `src/boot/tm-boot.script.ts` calls it
with the real globals. The package build bundles that entry with esbuild (one IIFE, ES2017, so a
browser too old for the app still parses it and shows the unsupported page) into
`boot/tm-boot.template.js`, generated and not checked in. Its first statement is
`var TM_BOOT=/*TM_BOOT*/null;`: a distribution's build replaces the marker with its boot
configuration (the offered languages and each one's boot strings, which Core's resources carry as
`Boot_*`) and serves the result as `tm-boot.js`, the first script of `index.html`. Rendered with
the platform's English strings, which every distribution embeds as the fallback, it stays within
3 KB gzipped, and each further string table may add 0.4 KB; a test in `client/tools/tests/` holds
English-only, English and Arabic, and Arabic-only configurations to that budget.

In order, the script:

1. shows the boot page (`unsupported`) when `navigator.locks`, `BroadcastChannel` or IndexedDB is
   missing;
2. runs `/signed-out` with no session;
3. without a valid profile cookie: shows the boot page when cookies are off (`cookies`) or when a
   sign-in came back without a session less than 60 s ago (`sign-in-failed`); deletes an expired
   user's stored data (their key range in `tm-app` and the `tm-images-v1` cache, in a 1 s box,
   never creating the database) after clearing the boot record's user; then writes the sign-in mark
   (`sessionStorage['tm.login']`) and leaves for `/bff/login`;
4. with one: consumes the mark, restores the fragment no server hop returns, decides warm or cold
   against the boot record (`localStorage['tm.boot']`), picks the language, rewrites a warm `/` to
   the last tenant, and works out whether the entry is automatic.

It sets `<html lang dir data-theme>` and the title before the first paint, and leaves its decision
on `window.__tmBoot`, which the app reads once. The boot page is built with DOM calls only, so a
Trusted Types policy needs nothing, inside `<tm-app>`.

The profile cookie parser, the sign-in mark and the boot record are shared by the script and the
app, so both agree on what counts as a session. The client store's frozen layout
(`src/state/store/`) is pinned by `tm-store-layout-vectors.json`, which the expiry delete and the
client store test against.

## Session and launch

`TmSession` starts as the user the boot script read from the profile cookie, and reads the cookie
again on every `check()`, never the network: `same`, `missing` (the expiry path) or `changed` (the changed-user path, which restarts
the app at `/`). `tmSessionGuard` runs the check on every route but `/signed-out`. `signIn()` writes
the sign-in mark and leaves for `/bff/login` in the document's language; a step-up adds
`acr_values` and `max_age`. After a sign-in it consumes the mark the boot script reported: it
reports an expiry that led there and drops a previous user's drafts. Until the tab bus and the
client store arrive, the paths beyond the tab's own state are tab-local: an expiry deletes the
stale profile cookie and reloads, so the boot leads to the sign-in and the user's data leaves the
screen. An expiry within a minute of coming back from a sign-in shows the sign-in-failed boot page
instead, so a server that keeps refusing fresh sessions cannot loop the tab through sign-ins.

`TmLaunch` holds the tenant this document entered without the user choosing it, seeded from the
boot script and set by the launch redirect; it ends at the tenant's first 2xx, at a route into
another tenant, or when a `404 tenant-not-found` takes it. `tmSingleLiveTenant` is the
single-membership rule, run over `src/launch/launch-landing-vectors.json`, which the server's
landing test links too.

## The HTTP layer

Every request the app makes goes through `TmApi`, over `fetch`; an upload, the one call that
reports its progress, goes over `XMLHttpRequest`. A route such as `centers/query` goes to the
routed tenant's API (`/17/api/web/centers/query`), or to the tenant a call names; a route with a
leading `/` is tenantless and sent as is. A tenant route with no tenant throws in development.

- **Reads** take a signal and are retried: a network failure twice (1 s, then 3 s, ±25 %), a `429`
  and a `503` with `Retry-After` twice after the wait it asks for (capped at 10 s and 30 s), and a
  build mismatch blamed on the server once after 30 s. An abort ends a call or a wait at once. A
  read of a route the Contracts chunk describes but does not list as idempotent throws in
  development and runs once in production.
- **Writes** are never aborted or retried, a write while the browser is offline is not sent, and
  `writesInFlight` counts the unsettled ones.
- **`download`** streams the body, reporting bytes; **`upload`** posts the raw file to
  `/{t}/blobs/{kind}`; **`send`** is a keepalive `POST` of at most 16 KB whose answer is never read;
  **`fingerprint`** names a route's contract fingerprint for drafts.
- **Every call** carries `Tellma-Client`, `Accept-Language` and a fresh `traceparent`; tenant calls
  add `Tellma-Calendar` and, for described routes, `Tellma-Contract`. Reads may add
  `Tellma-Activity: background` and `Tellma-Priority: low`, writes `Tellma-Confirm`.
- **Around every call** the profile cookie is checked before the send and again on the answer:
  no session runs the expiry path and rejects `unauthenticated`; another user runs the
  changed-user path and rejects `cancelled`, discarding the answer. A `401` for a session the
  cookie no longer names ends nothing. While the tab signs out, only the sign-out write and
  sends go out, and the reads in flight are aborted.
- **A watchdog** ends a call that hangs for 60 s, or 6 min for a long route, as `offline`
  (`outcome-unknown`); a download's watchdog restarts at each chunk.
- **Every refusal** is a `TmApiError` whose `kind` says how the platform presents it, whose `code`
  names the cause and whose `traceId` is the reference support quotes. A `412 client-outdated` or a
  `404 route-not-found` is judged by comparing the server's `Tellma-Build` with the UI version by
  SemVer precedence; when the builds do not say, the worker's update check decides, capped at 3 s.
- **`online`** is false while the browser is offline or after a network failure; a visible tab then
  probes `GET /health/live` every 5 s, backing off to 30 s, until any answer.

`provideTellma` puts `tm-image`'s blob fetcher over the same layer, so an image request gets the
user check, the session's headers and the caller's signal.

## Features and the merge rule

A distribution's app is one `provideTellma` call: the emitted metadata loaders, the languages it
ships client strings for, and its features, packs first and the distribution last. Each feature is
plain data. The merge rule unites the features in order after the platform's implicit `core`
feature, and `TM_APP_DECLARATIONS` holds the result:

- each declaration has one key: an entity's name, a page's path, a section's or entry's id, a
  settings tab's category, a coded report's or widget type's key; adding a taken key is a problem;
- a later feature changes an earlier key only through `overrides`, member by member (a member it
  sets replaces the earlier one whole, arrays included), and `hide`, which drops a page, section,
  launcher entry, settings tab or app-bar slot;
- coded reports are the server's: every `tmCodedReport` patch merges into the server's entry of its
  key in feature order, and every entry appears in `TM_APP_DECLARATIONS.reports`, patched or not.

```ts
export const acmeFeature = tmFeature({
  key: 'acme',
  entities: [customers],
  pages: [tmPage('credit-review', { component: () => import('./credit-review'), titleKey: 'Acme_CreditReview' })],
  overrides: [tmOverride('section:administration', { order: 10 })],
  hide: ['entry:core.Role'],
});
```

`tmCheckAppConfig` (in `@tellma/core/config-check`) merges the same features against the emitted
Core and Entities chunks and returns every problem: duplicate and dangling keys, page paths outside
the grammar or on a reserved word, declarations naming what the server lacks, and label
fallbacks, which are warnings. It reads declarations as data and never calls a lazy member, so it
runs in Node; `tmAppConfigOf` takes the config back out of an `ApplicationConfig`.

## Decimals

`decimal` and `long` values travel as JSON strings and stay strings: a `TmDecimal` is the value's
exact wire text, so nothing is lost to a binary double. Arithmetic and comparison go through
`TmDecimals`; the lint rule `tm/no-decimal-operators` refuses `+`, `+=`, `<`, `<=`, `>` and `>=` on a
`TmDecimal`, which would join or compare its text.

```ts
const total = TmDecimals.add(line.amount, line.tax);
if (TmDecimals.compare(total, creditLimit) > 0) { … }
```

## Seams

`TmTelemetry` and `TmRequestLog` are abstract classes provided in root with quiet defaults that
keep nothing and send nothing, and `provideTmPageScope()` returns no providers yet. Code records
against them today; the observability features replace the defaults without changing a call
site. Tests replace them through `@tellma/core-testing`, which also puts `TmFakeApi` behind
`TmApi`.

The HTTP layer reaches three services that arrive later through internal seams in `src/api/`, each
with a default that is correct until its owner replaces it: the request locale (the document's
language and no calendar, until the localization service), the cache refresher (nothing cached, so
nothing to revalidate), and the worker's update check (`disabled`, as no worker controls the
page yet).

## Import rules

Static imports between entry points follow one graph, always through `@tellma/core/<entry point>`
and never a relative path: the primary imports no other entry point, `/attention` only the
primary, `/shell` the primary and `/attention`, and no eager entry point imports a lazy one.
Dynamic `import()` and `import type` cross any boundary. The lint rule `tm/core-import-boundary`
enforces the graph, and ng-packagr refuses a cycle between entry points.

## Budgets

Each entry point's gzipped weight has a ceiling in `package.json` (`"tellma".budgetsInKb`),
checked by `pnpm run size:check`; every raise carries a note beside it.

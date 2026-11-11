# @tellma/core

The Tellma app framework: the runtime a distribution's single-page app imports. It splits into
entry points along its lazy-loading lines, so the warm-launch bundle carries only what every page
needs, and each lazy boundary (a page family, the hub, the support tools) is an entry point of its
own.

| Entry point | Holds | Loaded |
|---|---|---|
| `@tellma/core` | The distribution API: `provideTellma`, `tmFeature`, the declaration functions (`tmEntity`, `tmPage`, `tmCodedReport`, `tmWidget`, `tmOverride`) and `TM_APP_DECLARATIONS`, the merged declarations; the client metadata's types, its loaders (`tmMetadataLoaders`) and the loaded Core chunk (`TM_CLIENT_METADATA`); the plain-JSON value types of the HTTP layer (`TmJson`, `TmDecimal`, `TmId`) and the exact decimal helpers `TmDecimals`; the seams through which telemetry (`TmTelemetry`), the request log (`TmRequestLog`) and page scopes (`provideTmPageScope`) are provided | eager |
| `@tellma/core/config-check` | `tmCheckAppConfig`, the check of an app's configuration against the client metadata, which the build runs in Node and a development app at startup | lazy |
| `@tellma/core/private` | Wiring shared by the platform's own entry points and packages; no API goldens, no stability guarantee, never imported by app code | — |

The other entry points of the framework arrive with the features they hold, each in the folder of
its name.

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
site. Tests replace them through `@tellma/core-testing`.

## Import rules

Static imports between entry points follow one graph, always through `@tellma/core/<entry point>`
and never a relative path: the primary imports no other entry point, `/attention` only the
primary, `/shell` the primary and `/attention`, and no eager entry point imports a lazy one.
Dynamic `import()` and `import type` cross any boundary. The lint rule `tm/core-import-boundary`
enforces the graph, and ng-packagr refuses a cycle between entry points.

## Budgets

Each entry point's gzipped weight has a ceiling in `package.json` (`"tellma".budgetsInKb`),
checked by `pnpm run size:check`; every raise carries a note beside it.

# @tellma/core

The Tellma app framework: the runtime a distribution's single-page app imports. It splits into
entry points along its lazy-loading lines, so the warm-launch bundle carries only what every page
needs, and each lazy boundary (a page family, the hub, the support tools) is an entry point of its
own.

| Entry point | Holds | Loaded |
|---|---|---|
| `@tellma/core` | The plain-JSON value types of the HTTP layer (`TmJson`, `TmDecimal`, `TmId`) and the exact decimal helpers `TmDecimals`; the seams through which telemetry (`TmTelemetry`), the request log (`TmRequestLog`) and page scopes (`provideTmPageScope`) are provided | eager |
| `@tellma/core/private` | Wiring shared by the platform's own entry points and packages; no API goldens, no stability guarantee, never imported by app code | — |

The other entry points of the framework arrive with the features they hold, each in the folder of
its name.

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

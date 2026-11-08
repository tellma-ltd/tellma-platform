# Contributing to the Tellma Platform

## Test tiers

Tests run in two tiers. The **PR tier** gates every pull request and every push to one
(`.github/workflows/ci.yml`). The **nightly tier** holds what is too slow, too broad or too
timing-sensitive for that: full viewport and language matrices, Firefox and WebKit, performance
budgets, soaks and long waits (`.github/workflows/nightly-tier.yml`). Tests reach the nightly tier
only through the tags below; everything untagged is PR tier.

Before tagging a slow test nightly, try to make it fast: a wall-clock wait (a watchdog, a poll
interval, an expiry) is driven by a fake clock or a test `TimeProvider` wherever that is possible.

### Tags

| Where | Tag | Meaning |
|---|---|---|
| .NET (xUnit) | `[Trait("Category", "Nightly")]` | Runs in the nightly tier only. Add it beside the test's own category (`Integration`, `E2E`), never instead of it. |
| Playwright | `@nightly` | Runs in the nightly tier only, on Chromium. |
| Playwright | `@perf` | A timing or performance budget. Runs in the nightly tier only, on Chromium. |
| Playwright | `@cross-engine` | Runs on Chromium in the PR tier, and on Chromium, Firefox and WebKit in the nightly tier. Combine it with `@nightly` or `@perf` to run a nightly-only test on every engine. |

A Playwright tag goes at the start of the test or `describe` title (`test('@cross-engine pastes …')`,
which tags every test inside a tagged `describe`) or in the `tag` option
(`test('pastes …', { tag: '@nightly' }, …)`). The tier filters match these names as text, so no
other tag or title word may begin with `@nightly` or `@perf`.

Suites marked `[Trait("Live", "true")]` talk to real third-party services and belong to neither
tier: they need credentials and run in `.github/workflows/nightly.yml`.

### PR tier

Run these from the repository root before opening or updating a pull request. CI runs the same
commands with `-c Release`, the unit suites on Linux and Windows, and the integration suites in a
single solution-wide run.

```bash
dotnet build Tellma.slnx
```

```bash
dotnet format Tellma.slnx --verify-no-changes
```

```bash
dotnet test Tellma.slnx --no-build --filter "Category!=Integration&Category!=E2E&Category!=Nightly&Live!=true"
```

The integration suites need Docker (they bring up SQL Server with Testcontainers). Run them one
test project at a time:

```bash
dotnet test <test project> --no-build --filter "Category=Integration&Category!=Nightly&Live!=true"
```

The identity browser scenarios need Playwright's Chromium for .NET, installed once with
`pwsh test/apps/Tellma.Identity.E2E/bin/Debug/net10.0/playwright.ps1 install chromium`:

```bash
dotnet test test/apps/Tellma.Identity.E2E --no-build --filter "Category=E2E&Category!=Nightly"
```

The client gates run from `client/` after `pnpm install --frozen-lockfile`. Run
`pnpm run tokens:build-css` first after switching branches: the tokens CSS is generated, not
committed.

```bash
pnpm run lint
pnpm run typecheck
pnpm run lint:test
pnpm run tokens:check
pnpm run build
pnpm run test
pnpm run api:check
pnpm run docs:build
pnpm run size:check
pnpm run build:showcase
```

The PR-tier end-to-end suite runs on the Chromium engine only, desktop and touch, with the
nightly-tier tags excluded. Install the browser once with `pnpm exec playwright install chromium`.

```bash
node scripts/e2e.mjs --project chromium --project touch --grep-invert "@nightly|@perf"
```

### Nightly tier

`nightly-tier.yml` runs each night on the `platform-core` branch, and can be started by hand with
`gh workflow run nightly-tier.yml --ref platform-core`. Its two commands also run locally; an empty
tier passes.

```bash
dotnet test Tellma.slnx --filter "Category=Nightly&Live!=true"
```

CI runs that command on Linux, and on Windows runs the nightly tests that are neither integration
nor E2E tests, as the PR tier does for the unit suites:

```bash
dotnet test Tellma.slnx --filter "Category=Nightly&Category!=Integration&Category!=E2E&Live!=true"
```

From `client/`, with all three browsers installed (`pnpm exec playwright install chromium firefox webkit`):

```bash
node scripts/e2e.mjs --grep "@nightly|@cross-engine|@perf" --pass-with-no-tests
```

## Contributor License Agreement

Before we can merge your contribution, you must sign the
[Tellma Contributor License Agreement](CLA.md). This is a one-time
requirement that confirms you have the right to contribute your work
and grants Tellma Ltd the license described in the agreement.

By submitting a pull request, you agree to the terms in the CLA.

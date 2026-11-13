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
| Playwright | `@quarantine` | A flaky test taken out of the PR tier. Runs in the nightly tier, on Chromium, so the flake stays visible. See [Flaky tests](#flaky-tests). |
| Playwright | `@cross-engine` | Runs on Chromium in the PR tier, and on Chromium, Firefox and WebKit in the nightly tier. Combine it with `@nightly` or `@perf` to run a nightly-only test on every engine. |

A Playwright tag goes at the start of the test or `describe` title (`test('@cross-engine pastes …')`,
which tags every test inside a tagged `describe`) or in the `tag` option
(`test('pastes …', { tag: '@nightly' }, …)`). The tier filters match these names as text, so no
other tag or title word may begin with `@nightly`, `@perf` or `@quarantine`.

Suites marked `[Trait("Live", "true")]` talk to real third-party services and belong to neither
tier: they need credentials and run in `.github/workflows/nightly.yml`.

### Flaky tests

CI fails a run in which any Playwright test fails first and passes on retry: a flaky test never
turns a red run green. When a test proves flaky, quarantine it with the `@quarantine` tag (in the
`tag` option, so its title stays stable) and a comment saying how it failed. That takes it out of
the PR tier. The nightly tier runs every quarantined test ten times in a job of its own, which
never decides the nightly verdict, and lists each one in that job's summary with how many runs
passed, flaked or failed. That list is the quarantine's record: every test on it is tracked there
each night, and one that fails every run is no longer a flake but broken.

A quarantined test is fixed, never left: the fix removes the tag in the same change, and the tag
is not a way to retire a test that is no longer wanted.

Most flakes are a test that reads, or acts on, a state the page has not reached yet. A slow runner
widens that gap from a frame to hundreds of milliseconds. Wait on the page's own signals, never on
the clock:

- Assert with web-first assertions (`expect(locator)…`, `expect.poll`), not one-shot reads.
- Call `appStable(page)` (`client/e2e/support/settle.ts`) before reading or acting on state that
  the next render updates but the current DOM cannot tell apart from the previous one. Examples:
  the model dump, a highlight that two successive queries share, and focus that a pending render
  could still move. The grid and picker model helpers already call it.
- Call `animationsSettled(locator)` before measuring an element that has an enter animation.
- Keep an in-flight state (a spinner, a pending cell, a panel measured while loading) on screen
  with the story's held-response gate (`client/e2e/support/held-responses.ts`) until it has been
  asserted. Do not give the request a latency that the assertion then has to race.
- After a gesture that opens a list, wait for the rows before you press a key that moves through
  them.
- To test input that arrives faster than the page renders (a barcode scanner, a text expander, a
  stalled main thread), dispatch the burst in one task with `pressInOneTask(page, keys)`
  (`client/e2e/support/keyboard-burst.ts`). Never rely on pressing keys quickly: whether a render
  slips in between two Playwright key presses is itself a matter of timing.
- A gesture that must fit inside a time window is never two separate Playwright actions. For a
  window the browser measures (a double-tap), dispatch the touch events over CDP with explicit
  `timestamp`s, as `tapTwice()` in `grid-touch.spec.ts` does. For a window the page measures
  with a timer (a hover grace, a long-press delay), pause Playwright's clock (`page.clock`) around
  the gesture and advance it explicitly.
- Run one axe scan per test (`expectNoAxeViolations`). A test that walks through several states
  replays the steps to each state in a test of its own, so no test carries several scans against
  its timeout.

### PR tier

Run these from the repository root before opening or updating a pull request. CI runs the same
commands with `-c Release`, the unit suites on Linux and Windows, and the integration suites in a
single solution-wide run on Linux; on Windows it runs the two distribution-host suites
(`Tellma.Core.IntegrationTests` and `Tellma.Distro.Acme.IntegrationTests`) against LocalDB. The client gates run on Linux and Windows too; the client e2e suite runs
on Linux.

```bash
dotnet build Tellma.slnx
```

```bash
dotnet format Tellma.slnx --verify-no-changes
```

```bash
dotnet test Tellma.slnx --no-build --filter "Category!=Integration&Category!=E2E&Category!=Nightly&Live!=true"
```

On Linux, CI runs this command with `TELLMA_REQUIRE_PINNED_ICU=1`. The test that regenerates the
pinned culture data (`eng/culture-data`) needs the ICU release the data came from and skips on any
other; with the variable set it fails instead, so a CI runner that moves to another ICU release is
noticed. Leave the variable unset on a development machine.

The integration suites need Docker (they bring up SQL Server with Testcontainers), or
`TELLMA_TEST_SQL` set to a connection string to an existing server, such as LocalDB
(`Server=(localdb)\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True`); a run
against an existing server drops the databases it created. Run them one test project at a time:

```bash
dotnet test <test project> --no-build --filter "Category=Integration&Category!=Nightly&Live!=true"
```

The identity browser scenarios need Playwright's Chromium for .NET, installed once with
`pwsh test/apps/Tellma.Identity.E2E/bin/Debug/net10.0/playwright.ps1 install chromium`:

```bash
dotnet test test/apps/Tellma.Identity.E2E --no-build --filter "Category=E2E&Category!=Nightly"
```

The client gates run from the repository root, which is the root of the one pnpm workspace:
`pnpm-workspace.yaml` lists `client/` (the Angular workspace of every `@tellma/*` package), the
Node packages inside it and every distribution's Client project, and one lockfile
(`pnpm-lock.yaml`) resolves them all, with a single copy of Angular. Install once at the root, with
the lockfile as committed:

```bash
pnpm install --frozen-lockfile
```

Change a dependency only through the `catalog` in `pnpm-workspace.yaml` (a package names it with
the specifier `catalog:`), then run `pnpm install` at the root and commit the regenerated
`pnpm-lock.yaml`; never edit the lockfile by hand. The root `package.json` scripts forward to
`client/`, so each gate below works from either folder. Run `pnpm run assets:generate` first after
switching branches: the tokens CSS and `@tellma/core`'s boot script template are generated, not
committed (`pnpm run tokens:build-css` writes the tokens CSS alone).

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
nightly-tier tags excluded. Install the browser once with
`pnpm --dir client exec playwright install chromium`. Call the script directly, so the grep
pattern reaches Playwright without a second round of shell parsing; it runs from any folder, and
relative path arguments resolve against `client/`.

```bash
node client/scripts/e2e.mjs --project chromium --project touch --grep-invert "@nightly|@perf|@quarantine"
```

### Nightly tier

`nightly-tier.yml` runs each night on the `platform-core` branch, and can be started by hand with
`gh workflow run nightly-tier.yml --ref platform-core`. Its commands also run locally; an empty tier
passes.

```bash
dotnet test Tellma.slnx --filter "Category=Nightly&Live!=true"
```

CI runs that command on Linux, and on Windows runs the nightly tests that are neither integration
nor E2E tests, as the PR tier does for the unit suites:

```bash
dotnet test Tellma.slnx --filter "Category=Nightly&Category!=Integration&Category!=E2E&Live!=true"
```

From the repository root, with all three browsers installed
(`pnpm --dir client exec playwright install chromium firefox webkit`):

```bash
node client/scripts/e2e.mjs --grep "@nightly|@cross-engine|@perf" --grep-invert "@quarantine" --pass-with-no-tests
```

The quarantined tests, ten runs each:

```bash
node client/scripts/e2e.mjs --grep "@quarantine" --repeat-each 10 --pass-with-no-tests
```

## Contributor License Agreement

Before we can merge your contribution, you must sign the
[Tellma Contributor License Agreement](CLA.md). This is a one-time
requirement that confirms you have the right to contribute your work
and grants Tellma Ltd the license described in the agreement.

By submitting a pull request, you agree to the terms in the CLA.

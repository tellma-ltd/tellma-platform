# Tellma.Distro.Acme.E2E

The Acme distribution's browser suite: Playwright flows over the **Acme e2e host**, the fixture every
client spec's browser tests run on. The host is the real deployment shape on one machine: a migrated
SQL Server database, the client app built with `tellma-client build`, and the Acme web host in
Development with the in-process identity engine, serving the built app (its service worker, its
production content security policy and its hashed names) on `https://localhost:<port>`.

It is a pnpm workspace package (`tellma-distro-acme-e2e`), not a .NET project: the flows use
`@tellma/core-testing/e2e`, the platform's Playwright helpers, which resolve to their sources through
`tsconfig.json`'s path mapping.

```
Tellma.Distro.Acme.E2E/
├── playwright.config.ts     # Chromium in English and in Arabic; webServer starts the e2e host
├── scripts/e2e.mjs          # picks the host's port and runs Playwright
├── host/acme-e2e-host.mjs   # SQL Server, migrate, client build, the web host
├── host/containers.mjs      # which SQL Server containers a starting run may remove, and its tests
├── support/acme.ts          # the fixture (the CSP watch), the casts and texts, shared page probes
├── support/global-setup.ts  # once the host is up: the Arabic project's accounts read Arabic
└── specs/                   # the flows
```

## Running it

Install Chromium once, then run the pull-request tier from any folder:

```bash
pnpm --dir distributions/acme/test/Tellma.Distro.Acme.E2E exec playwright install chromium
node distributions/acme/test/Tellma.Distro.Acme.E2E/scripts/e2e.mjs --grep-invert "@nightly|@perf|@quarantine"
```

The nightly tier runs the tagged gates instead:

```bash
node distributions/acme/test/Tellma.Distro.Acme.E2E/scripts/e2e.mjs --grep "@nightly|@perf" --grep-invert "@quarantine"
```

Arguments pass through to `playwright test`; `--project chromium` or `--project chromium-ar` runs
one language. The report and traces go to `.artifacts/`, beside the host's log
(`.artifacts/host/host.log`), its mail drop and blob root, and the kept sign-ins. CI passes
`--global-timeout` below its step's limit, the host's start included, so a run out of time still
writes its report for the upload.
`pnpm --dir distributions/acme/test/Tellma.Distro.Acme.E2E run typecheck` and `run lint` check the
sources without a host, and `run test` runs the host's own unit tests; `run format` formats the
sources with the client's Prettier settings.

## The e2e host

`host/acme-e2e-host.mjs`, which Playwright's `webServer` starts and stops:

1. **SQL Server.** `TELLMA_E2E_SQL` picks it: `localdb` (the Windows default), `container` (the
   default elsewhere: a throwaway `mcr.microsoft.com/mssql/server:2022-latest` container through the
   Docker CLI, or `TELLMA_TEST_SQL_IMAGE`), or a connection string to a server. Unset, the
   `TELLMA_TEST_SQL` the integration suites read is used when it is set. The databases are
   `Tellma.e2e.<DB_SUFFIX>.acme.*`, with the worktree's `DB_SUFFIX` from `.dev-ports.local`, never
   the development host's `Tellma.dev.acme.*`. A server that outlives the run keeps them, so a rerun
   migrates nothing; a container is fresh every run. Each container carries the labels
   `tellma-e2e=<DB_SUFFIX>`, `tellma-e2e.pid` (its host's process id) and `tellma-e2e.started`.
   A starting run removes only the stale ones: those whose host process is gone (a killed run's)
   and those older than twelve hours. A concurrent run's container stays (`host/containers.mjs`,
   tested by `run test`).
2. **Client build.** `tellma-client build` of `Tellma.Distro.Acme.Client`, about five minutes cold.
   `TELLMA_E2E_WEBROOT` names an app already built instead (CI passes the published `wwwroot`); a
   relative path is taken from the folder the suite was started in.
3. **Migrate.** The Migrator's `migrate`, which creates the catalog and the identity store and
   provisions the two seed tenants with their development data.
4. **The web host.** `dotnet run --no-launch-profile` of `Tellma.Distro.Acme.Web` (so the SPA proxy
   is off) with `--webroot` set to the built app and `Tellma__PublicOrigin` set to the host's own
   origin, which also moves the in-process identity authority. Email goes to the **development mail
   drop** (`Email:Provider` = `development-drop`), which writes one JSON file per message. The
   API's request rates per address and per user are raised: every worker reaches the host from one
   loopback address, and parallel tests share the principals. It serves HTTPS on the ASP.NET
   development certificate, which the host creates. The web host's sign-in fetches the identity
   engine's discovery document from its own origin, so .NET must trust that certificate: on Linux
   the host runs `dotnet dev-certs https --trust`, which needs no prompt there; on Windows,
   where trusting asks, trust it once yourself with the same command. The browsers need no trust:
   the config ignores certificate errors per context and launches Chromium with
   `--ignore-certificate-errors`, without which Chromium refuses to register the service worker
   on Linux, where its own certificate store does not hold the development certificate.

`TELLMA_E2E_CONFIGURATION` sets the .NET build configuration (Debug by default). The port is
`ACME_WEB_E2E` from `.dev-ports.local`, else one the OS assigns, so the suite never collides with
the development host on `ACME_WEB` or with another worktree.

## Writing flows

- Import `test` and `expect` from `support/acme.ts`. Every context, the test's own and each one
  `newContext()` opens, runs under a watch of the content security policy, and a test fails on any
  `securitypolicyviolation`.
- Sign in with `signInAs(page, email)` for a ready session, or `tmSignInToIdentityAs(context, email)`
  to sign in through the app's own round trip. The identity engine sends at most ten codes per
  address an hour, so each principal spends one code per host at most: its identity sign-in is kept
  in `.artifacts/state` and every later sign-in completes from it.
- Every flow runs twice, in the `chromium` project (an English browser) and in `chromium-ar` (an
  Arabic one), except those that pick their languages themselves (`responsive`, `rtl-boot`) and the
  nightly gates. Sign in as the `cast` fixture's principals and match the `text` fixture's texts, so
  a flow reads the project's language. The English cast is the administrator, with the sandbox
  tester as the other user; the Arabic cast is the sales representative, with the center reader as
  the other user, both of whose accounts the global setup sets to Arabic (the seed creates every
  account in English). Both casts share the sandbox tester, whose account reads English.
- The principals are `ACME_PRINCIPALS`: the administrator (tenants 1 and 2), the sales
  representative and the center reader (every tenant), and the sandbox tester (tenant 2 alone).
- Wait on observable state (a heading, the boot result, the store, a held request), never on the
  clock. `warmUp` waits for the service worker's shell cache and the stored launch documents.
- A flow that moves the browser's time zone does it per tab, with the DevTools protocol's
  `Emulation.setTimezoneOverride`, and brings the tab into view, as the device-zone flows do. A
  flow that changes a user's stored preferences runs its tests serially, and starts and ends with
  the preference cleared (the device-zone flows reset the time zone through the page, as My
  settings does), so a later run starts from the same state even after one was killed.
- A CPU-bound timing budget is a quiet machine's, times `ACME_PERF_MULTIPLIER`
  (`TM_PERF_MULTIPLIER`, a positive number, else 2 on CI), and judges the `median` of
  `ACME_TIMING_SAMPLES` measurements. The cold launch's network-bound budget takes no multiplier.
- Tag a test `@nightly` or `@perf` for the nightly tier, `@cross-engine` once a Firefox and WebKit
  project joins the config, and `@quarantine` to take a flaky test out of the pull-request tier.
- A flow whose surface a later spec ships is a `test.fixme`, with a comment naming that spec. A
  known gap in a shipped surface is a `test.fail()` with a comment, so its fix shows.
- Format with `run format`; CI runs `typecheck`, `lint` and `test` on both of its legs.

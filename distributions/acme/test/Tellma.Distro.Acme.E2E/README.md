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
├── playwright.config.ts     # one Chromium project; webServer starts the e2e host
├── scripts/e2e.mjs          # picks the host's port and runs Playwright
├── host/acme-e2e-host.mjs   # SQL Server, migrate, client build, the web host
├── support/acme.ts          # the fixture (the CSP watch), the principals, shared page probes
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

Arguments pass through to `playwright test`. The report and traces go to `.artifacts/`, beside the
host's log (`.artifacts/host/host.log`), its mail drop and blob root, and the kept sign-ins.

## The e2e host

`host/acme-e2e-host.mjs`, which Playwright's `webServer` starts and stops:

1. **SQL Server.** `TELLMA_E2E_SQL` picks it: `localdb` (the Windows default), `container` (the
   default elsewhere: a throwaway `mcr.microsoft.com/mssql/server:2022-latest` container through the
   Docker CLI, or `TELLMA_TEST_SQL_IMAGE`), or a connection string to a server. Unset, the
   `TELLMA_TEST_SQL` the integration suites read is used when it is set. The databases are
   `Tellma.e2e.<DB_SUFFIX>.acme.*`, with the worktree's `DB_SUFFIX` from `.dev-ports.local`, never
   the development host's `Tellma.dev.acme.*`. A server that outlives the run keeps them, so a rerun
   migrates nothing; a container is fresh every run.
2. **Client build.** `tellma-client build` of `Tellma.Distro.Acme.Client`, about five minutes cold.
   `TELLMA_E2E_WEBROOT` names an app already built instead (CI passes the published `wwwroot`).
3. **Migrate.** The Migrator's `migrate`, which creates the catalog and the identity store and
   provisions the two seed tenants with their development data.
4. **The web host.** `dotnet run --no-launch-profile` of `Tellma.Distro.Acme.Web` (so the SPA proxy
   is off) with `--webroot` set to the built app and `Tellma__PublicOrigin` set to the host's own
   origin, which also moves the in-process identity authority. Email goes to the **development mail
   drop** (`Email:Provider` = `development-drop`), which writes one JSON file per message. The
   API's request rates per address and per user are raised: every worker reaches the host from one
   loopback address, and parallel tests share the principals.

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
- The principals are `ACME_PRINCIPALS`: the administrator (tenants 1 and 2), the sales
  representative and the center reader (every tenant), and the sandbox tester (tenant 2 alone).
- Wait on observable state (a heading, the boot result, the store, a held request), never on the
  clock. `warmUp` waits for the service worker's shell cache and the stored launch documents.
- Tag a test `@nightly` or `@perf` for the nightly tier, `@cross-engine` once a Firefox and WebKit
  project joins the config, and `@quarantine` to take a flaky test out of the pull-request tier.
- A flow whose surface a later spec ships is a `test.fixme`, with a comment naming that spec. A
  known gap in a shipped surface is a `test.fail()` with a comment, so its fix shows.
- Format with the client's Prettier settings:
  `pnpm --dir client exec prettier --config .prettierrc --write ../distributions/acme/test/Tellma.Distro.Acme.E2E`.

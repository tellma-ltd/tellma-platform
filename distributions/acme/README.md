# Acme: the reference distribution

Acme is a fictional customer whose distribution lives in the platform repository, so the platform is
always built, run and tested in the shape a real distribution repository has. Its deployment,
`acme.app.tellma.com`, is the platform's smoke deployment.

```
distributions/acme/
├── src/
│   ├── Tellma.Distro.Acme/            # AcmeComposition and AcmeFeature: the composition both hosts run, Acme's entities and sample data
│   ├── Tellma.Distro.Acme.Web/        # the web host: three platform calls, configuration, launch profile, SPA proxy
│   ├── Tellma.Distro.Acme.Client/     # the Angular app (an esproj): feature, languages, manifest, icons, theme
│   └── Tellma.Distro.Acme.Migrator/   # the migrator: one call, the design-time factory, tenant migrations
└── test/
    ├── Tellma.Distro.Acme.Web.Tests/          # unit: composition, audit, dependencies, configuration
    ├── Tellma.Distro.Acme.IntegrationTests/   # Category=Integration: migrate, provision, sign-in, sessions
    └── Tellma.Distro.Acme.E2E/                # Playwright flows over the Acme e2e host (a pnpm package)
```

The slug is `acme`: the OIDC client is `acme` (its service client `acme-svc`), the Data Protection
application name is `tellma-acme`, and development databases are `Tellma.dev.acme.*`. Every Acme
project takes its version from `Directory.Build.props` here: the composition library's is the
distribution version the client metadata carries and the web host reports in `Tellma-Build`.

## Running it locally

From a fresh clone, on Windows with LocalDB (or with the catalog connection string pointed at
another SQL Server through `Tellma__Catalog__ConnectionString`), with Node and pnpm:

```bash
pnpm install
dotnet run --project distributions/acme/src/Tellma.Distro.Acme.Migrator -- migrate
dotnet run --project distributions/acme/src/Tellma.Distro.Acme.Web
```

1. `migrate` creates `Tellma.dev.acme.catalog` with the identity engine's `idsvr` schema, then
   registers and provisions the seed tenants of `Tellma:Seed:Tenants`: tenant 1, "Acme", and its
   sandbox, tenant 2, "Acme Sandbox", with `admin@localhost` as their administrator.
   Each also takes Acme's development data: GL's centers as Acme's 40-node tree, currencies,
   customers, the `Sales` role and 20 sales invoices, and the end-to-end principals
   `sales@localhost` and `centers@localhost` (with `sandbox@localhost` in tenant 2 alone), who
   sign in with an email code like the administrator.
2. The web host serves `/api/distribution-info`, `/health/live`, `/health/ready` and the BFF
   (`/bff/login`, `/bff/user`, `/bff/logout`), with the identity engine in process under `/id`. Its
   launch profile turns on the SPA proxy, which starts the client's dev server (`pnpm start` in
   `Tellma.Distro.Acme.Client`, `tellma-client serve`) and sends the browser to it.
3. The browser opens `https://localhost:4200/`, the dev server, which forwards the server's own
   paths to the web host. Sign in through `https://localhost:4200/bff/login` as `admin@localhost`
   with the code the web host's console prints.

`dotnet run --no-launch-profile` runs the web host without the proxy; start the dev server yourself
with `pnpm start` in the client project.

A web host started before `migrate` prints that command instead of a stack trace.

### Ports

The web host listens on `https://localhost:7052` and the client's dev server on
`https://localhost:4200`, which is the public origin: the browser only ever sees the dev server, so
the in-proc identity engine's issuer and every sign-in redirect live under it. A worktree that runs
beside others sets its own ports in the gitignored `.dev-ports.local` at the repository root:

```
ACME_WEB=7120
ACME_CLIENT=4220
```

Every reader takes its port from that file: in Development the web host listens on `ACME_WEB` and
takes `https://localhost:<ACME_CLIENT>` as its public origin; the dev server listens on
`ACME_CLIENT` and forwards to `ACME_WEB`; and the web project's build points the SPA proxy at
`ACME_CLIENT` (rebuild after changing it). Environment variables and the command line still win over
the web host's derived values: `Tellma__PublicOrigin` and `--urls` override what the file derives.

### Provisioning another tenant

`dotnet run --project distributions/acme/src/Tellma.Distro.Acme.Migrator -- provision --name "Acme 2" --category Live`
registers and provisions a live tenant with the next free id; `--category Sandbox --live-tenant <id>`
registers a sandbox of that live tenant instead. `provision --tenant <id>` resumes a provisioning that
stopped part way. In Development, a tenant the admin surface
registers (`Tellma:Admin:Enabled`) is provisioned by running the same migrator from this checkout.

## Configuration

The web project's `appsettings.json` holds what every environment shares, and
`appsettings.Development.json` the local defaults; the migrator reads the same two files. A
deployment supplies its origin, catalog, SQL profiles, allowed servers and identity authority
through its own configuration, and every secret through its secret store: no tracked file holds a
secret, and the web suite fails if one does.

## Publishing

```bash
dotnet publish distributions/acme/src/Tellma.Distro.Acme.Web -c Release
```

Publishing builds the client app through its esproj (`tellma-client build`, which needs Node and the
repository's `pnpm install`) and copies its output into the web host's `wwwroot`. The web host
publishes framework-dependent, never single-file or self-contained: the PDF thumbnail worker runs
through `dotnet exec` on the host's runtime configuration.

## Regenerating the tenant migrations

The tenant model's migrations live in the Migrator project under `Migrations/Tenant`. A change to the
model, from the platform or from the distribution, adds a migration; run from the repository root
after `dotnet tool restore`:

```bash
dotnet ef migrations add <Name> --project distributions/acme/src/Tellma.Distro.Acme.Migrator --startup-project distributions/acme/src/Tellma.Distro.Acme.Migrator --namespace Tellma.Distro.Acme.Migrator.Migrations.Tenant --output-dir Migrations/Tenant
```

Then remove the byte-order mark from the generated files: the format gate refuses it. The tool
writes the model snapshot under `Tellma/Distro/Acme/Migrator/Migrations/Tenant/`, the folder its
namespace spells, and finds it there on the next run. Scaffolding never connects to a database.

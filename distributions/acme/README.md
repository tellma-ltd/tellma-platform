# Acme: the reference distribution

Acme is a fictional customer whose distribution lives in the platform repository, so the platform is
always built, run and tested in the shape a real distribution repository has. Its deployment,
`acme.app.tellma.com`, is the platform's smoke deployment.

```
distributions/acme/
├── src/
│   ├── Tellma.Distro.Acme/            # AcmeComposition: the slug and the composition both hosts run
│   ├── Tellma.Distro.Acme.Web/        # the web host: three platform calls, configuration, launch profile
│   └── Tellma.Distro.Acme.Migrator/   # the migrator: one call, the design-time factory, tenant migrations
└── test/
    ├── Tellma.Distro.Acme.Web.Tests/          # unit: composition, audit, dependencies, configuration
    ├── Tellma.Distro.Acme.IntegrationTests/   # Category=Integration: migrate, provision, sign-in, sessions
    └── Tellma.Distro.Acme.E2E/                # Playwright, scaffolded with no tests until the client ships
```

The slug is `acme`: the OIDC client is `acme` (its service client `acme-svc`), the Data Protection
application name is `tellma-acme`, and development databases are `Tellma.dev.acme.*`. The Angular
client project (`Tellma.Distro.Acme.Client`) joins with the client workspace.

## Running it locally

From a fresh clone, on Windows with LocalDB (or with the catalog connection string pointed at
another SQL Server through `Tellma__Catalog__ConnectionString`):

```bash
dotnet run --project distributions/acme/src/Tellma.Distro.Acme.Migrator -- migrate
dotnet run --project distributions/acme/src/Tellma.Distro.Acme.Web
```

1. `migrate` creates `Tellma.dev.acme.catalog` with the identity engine's `idsvr` schema, then
   registers and provisions the seed tenants of `Tellma:Seed:Tenants`: tenant 1, "Acme", and its
   sandbox, tenant 2, "Acme Sandbox", with `admin@localhost` as their administrator.
2. The web host serves `/api/distribution-info`, `/health/live`, `/health/ready` and the BFF
   (`/bff/login`, `/bff/user`, `/bff/logout`), with the identity engine in process under `/id`.
3. Sign in as `admin@localhost` with the code the web host's console prints.

A web host started before `migrate` prints that command instead of a stack trace.

### Ports

The web host listens on `https://localhost:7052`, and its public origin is
`https://localhost:4200`, the Angular dev server's. A worktree that runs beside others sets its own
in the gitignored `.dev-ports.local` at the repository root:

```
ACME_WEB=7120
ACME_CLIENT=4220
```

In Development the web host reads that file at startup. Environment variables and the command line
still win: `Tellma__PublicOrigin` and `--urls` override what the file derives.

### Provisioning another tenant

`dotnet run --project distributions/acme/src/Tellma.Distro.Acme.Migrator -- provision --name "Acme 2"`
registers and provisions a tenant with the next free id. In Development, a tenant the admin surface
registers (`Tellma:Admin:Enabled`) is provisioned by running the same migrator from this checkout.

## Configuration

The web project's `appsettings.json` holds what every environment shares, and
`appsettings.Development.json` the local defaults; the migrator reads the same two files. A
deployment supplies its origin, catalog, SQL profiles, allowed servers and identity authority
through its own configuration, and every secret through its secret store: no tracked file holds a
secret, and the web suite fails if one does.

## Publishing

The web host publishes framework-dependent, never single-file or self-contained: the PDF thumbnail
worker runs through `dotnet exec` on the host's runtime configuration.

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

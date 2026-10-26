# F11 — ARCHITECTURE.md, the client workspace, and the server layout

Digest for the round-2 design pass. ARCHITECTURE.md has no numbered sections, so references are
written "ARCH §<heading>" (e.g. "ARCH §Frontend → UI component library"). Workspace facts cite the
file they come from, relative to the repo root. Facts were checked against the files on
2026-10-20 (branch `specs-backup`).

## 1. Sources read

| Source | Lines | Coverage |
|---|---|---|
| `ARCHITECTURE.md` | 1178 | All of it |
| `client/README.md` | 43 | All |
| `client/angular.json` | 188 | All |
| `client/package.json` | 77 | All |
| `client/pnpm-workspace.yaml` | 8 | All |
| `client/tsconfig.json` | 66 | All (+ every `tsconfig.lib.json` / `.spec.json` / `.app.json` of the libraries and showcase) |
| `client/eslint.config.mjs` | 433 | All |
| `client/stylelint.config.mjs` | 44 | All |
| `client/e2e/playwright.config.ts` | 102 | All; `e2e/support/*.ts` skimmed; `e2e/specs/` listed (39 specs) |
| `client/scripts/*.mjs` | 874 (8 files) | `build`, `test`, `bundle-size`, `ports`, `e2e`, `inject-font-preloads` read; `serve`, `api-extractor` skimmed |
| `client/tools/workspace.mjs` | 115 | All |
| `client/tools/docs/*.mts` | 574 (3 files) | `build-docs`, `components-schema` read; `extract-components` grepped |
| `client/tools/eslint/`, `tools/stylelint/`, `tools/tokens/`, `tools/fonts/`, `tools/tests/` | — | Listed; `tm-prefix-exports.mjs`, `build-css.mts`, `copy-identity-assets.mts` read |
| `client/api/*.api.md` | 29 files | Listed |
| `client/projects/**` | — | Every `package.json`, `ng-package.json`; core-ui `README.md` (70), `public-api.ts`, `cache/`, `providers/`, `i18n/tm-active-locale.ts`, `image/tm-blob-fetcher.ts`, entity-picker search type; showcase `app.config.ts`, `app.routes.ts`, `index.html`, `README.md`; MCP server tool names |
| `src/`, `test/`, `eng/`, `build/`, `templates/`, `samples/`, `.config/`, `.github/workflows/ci.yml` (190), `Tellma.slnx`, `global.json` | — | Project lists; `src/core/Tellma.Core/Tellma.Core.csproj` read |
| `docs/specs/0011-distribution-host-and-multitenancy.md` | — | §1.2 (lines 140–212) and §11 only, to check the distro client layout |
| `~/.claude/.../memory/client-workspace-phase1.md` | 41 | All (97 days old; verified against code where cited) |

## 2. Key facts

### 2.1 Glossary and the package family (ARCH §Glossary)

- `Tellma.Core` = "the one runtime library every distribution references (`Tellma.Core` C# NuGet,
  `@tellma/core` npm package)". The npm `@tellma/core` is named, never specified.
- `@tellma/core-ui` family = Core-layer npm packages `@tellma/core-ui`, `-tokens`, `-testing`,
  `-mcp`; "Greenfield on `@angular/cdk` + `@angular/aria`; `tm-`-prefixed; signal-first".
- **Stack** = "a table (with optional paired UDTT) generated from a C# entity class, plus the
  services, controllers/APIs, and **Angular page** that capture a full CRUD or report feature".
- **Capability interface** (e.g. `IActivatable`) declares a stack capability once: operations,
  securables, gates, filters (`IActivatable` → `activate`/`deactivate`, the `Activate` securable,
  the activatable conjunct `IncludeInactive` lifts). The client's per-entity feature detection
  (create/import/export/activate buttons) has to come from this declaration.
- **Queryex** powers "within-stack CRUD, row-level security criteria, and tier-1 reports".
- `Tellma.Core.Testing` is "the C# sibling of `@tellma/core-ui-testing`".
- **Tenant** states: `Provisioning`, `Active`, `ReadOnly`, `Suspended`, `Retired`.
- **Landing page** (`tellma.com`) and the **control plane** are separate deployables; the landing
  page is deferred in Phase 1 (ARCH §Rollout & Phasing).

### 2.2 Guiding principles that bind the front end (ARCH §Guiding Principles)

Performance and Responsiveness ("not aspirations — constraints"; a violation is "an architectural
regression"):
- **Initial UI load is fast.** "Bundles are split per feature; nothing unneeded for the landing
  route loads upfront."
- **Subsequent UI loads are instant, even days after last use.** "Aggressive client-side caching of
  static assets, API responses, definitions, and translations. Stale-while-revalidate for anything
  mutable. The user should never see a cold-spinner on a return visit."
- **Instant feedback (hard UI rule).** "Optimistic UI for mutations, skeleton placeholders rather
  than blank waits, local commit before server reconciliation. Non-negotiable."
- Server API: private bounded in-memory caches per kind, validated by per-tenant version tags (no
  `HybridCache`); output caching only for anonymous, tenant-independent GETs; response compression.
- **All I/O bulkified**: save endpoints accept arrays; reports return aggregated data; the one
  exception is single-file raw-body blob uploads (0017).
- "`Tellma.Core` provides the primitives that make these the defaults — caching wrappers,
  bulk-aware repository bases, **optimistic-mutation helpers in the Angular library** — and
  distributions inherit them. A feature that ships without these is incomplete."

Parallel local development (ARCH §Parallel Local Development):
- Local-dev tooling never mutates tracked files; worktrees are top-level siblings.
- Per-worktree app processes (ASP.NET host with in-proc identity, and `ng serve`) bind to
  first-run-cached free ports in gitignored `.dev-ports.local`, written by
  `dotnet tellma setup-worktree` (the CLI does not exist yet).
- Target: `npm start` "uses a `bin` script in `@tellma/core` that reads `.dev-ports.local` and passes
  `--port` to `ng serve`". Today that logic is `client/scripts/ports.mjs` (workspace-local).
- "If it cannot be run twice in parallel against the same repo in different worktrees, it is
  broken."

### 2.3 Naming conventions (ARCH §Naming Conventions, §Package naming)

- Repos: `tellma-platform`, `tellma-<slug>`. Subdomain `<slug>.app.tellma.com`. Slug: lowercase
  letters, digits, hyphens; starts with a letter; max 15 chars.
- Reserved slugs: `core`, `landing`, `identity`, `platform`, `tellma`, `www`, `app`, `api`,
  `admin`, `auth`, `login`, `secure`, `support`, `help`, `training`, `status`, `demo`, `test`,
  `staging`, `sample`, `dev`, `acme` (held in `taxonomy.json` `reservedSlugs`; the file does not
  exist yet).
- .NET distro prefix `Tellma.Distro.<slug>` (e.g. `Tellma.Distro.Etpharma.Web`); "never published
  as a package and never lives in the platform repo" — but Phase 1 puts `distributions/acme/` in
  the platform repo (ARCH §Rollout & Phasing; see §5).
- npm: "npm packages stay lowercase as npm requires"; `client/` "project folders match npm package
  kebab-case" (ARCH §Platform repo layout): `@tellma/core` → `projects/core/tellma-core/`,
  `@tellma/module-sales` → `projects/module/tellma-module-sales/`, `@tellma/industry-pharma-sales`,
  `@tellma/compliance-sa`, `@tellma/connector-zatca`, `@tellma/locale-ar`; landing SPA at
  `projects/apps/tellma-landing/`.
- The C# positional naming (`Tellma.Module.<m>`, `Tellma.Industry.<i>[.<m>]`,
  `Tellma.Compliance.<c>[.<i>][.<m>]`, `Tellma.Connector.<vendor>[...].Adapter`, `Tellma.Locale.<id>`)
  has an npm mirror only by example: `@tellma/<dimension>-<segments-kebab>`. No npm rule for
  `.Abstractions` companions exists.
- Modules registry currently `Gl` (in `taxonomy.json`, which does not exist yet); Compliance
  registry empty.

### 2.4 Library architecture as it applies to client packages (ARCH §Library architecture)

- Five dimensions: **Module** (~10–20, owns most entities), **Industry** (~50–100), **Compliance**
  (~50–100), **Connector** (100s, no entities), **Locale** (~50–100; "`IAmountToText`,
  language-specific text utilities and client-side assets; calendars are Core's, never a Locale
  concern").
- Abstractions / implementation split: "Every layer that declares extension points consumed by other
  layers ships as two packages" — written for NuGet only. Rule 2: "`Tellma.Module.<m>` never
  references `Tellma.Core`"; modules reference only Abstractions. Nothing says whether
  `@tellma/module-gl` may depend on `@tellma/core` / `@tellma/core-ui` directly; the only
  client-side contract split is the `@tellma/core-ui/contracts` entry point.
- Rule 8: distributions may reference any platform package; the reference composition
  `Tellma.Distro.Acme` references `Tellma.Core`, `Tellma.Defaults.Azure`, `Tellma.Module.Gl`
  (+ `.Abstractions`); its Web project adds `Tellma.Core.AspNetCore`, `Tellma.Core.Mcp`,
  `Tellma.Defaults.Azure.AspNetCore`, `Tellma.Identity` (in-proc).
- Per-dimension contents (ARCH §Per-dimension contents): Module ships "entity classes, services,
  controllers, default workflows, …, baseline notification templates, baseline permissions and
  roles"; Industry ships "industry-specific reports"; Locale ships "a locale's UI strings and font
  subset". No dimension's list mentions Angular pages, report definitions or dashboards explicitly.
- Entity placement that matters for the stock pages: `User`, `Role` are Core default leaves in
  `Tellma.Core.Abstractions`; `Center : ActivatableTreeEntity` is in `Tellma.Module.Gl.Abstractions`
  (ARCH §The entity class hierarchy). So by the package rules the Centers UI belongs to a GL-module
  npm package, not to Core.

### 2.5 Distribution repo layout (ARCH §Distribution repo layout)

The diagram shows **two** client locations at once:
- `client/` "Angular workspace" with `projects/app/` ("distribution SPA — imports @tellma/core"),
  `angular.json`, `package.json`, `tsconfig.json`.
- `src/Tellma.Distro.<Slug>.Client/`: "the Angular application — an esproj
  (Microsoft.VisualStudio.JavaScript.Sdk) scaffolded from a current Angular CLI workspace (vitest,
  pnpm)", with `src/`, `proxy.conf.js` ("forwards every non-SPA prefix (/bff, /api, /id,
  /signin-oidc, /signout-callback-oidc, /health, /.well-known, /{tenantId}/api|hub|blobs|mcp) to
  the Web project in Development"), `aspnetcore-https.js`, `Tellma.Distro.<Slug>.Client.esproj`.
- Spec 0011 §1.2 uses only the second (`distributions/acme/src/Tellma.Distro.Acme.Client/` with its
  own `angular.json / package.json / tsconfig*.json`; `src/` "arrives with the UI phase") and adds
  `/openapi` to the proxy list. 0011 sets `Tellma:PublicOrigin` = `https://localhost:4200` in
  `appsettings.Development.json`; `dotnet publish` copies `dist/<app>/browser` into `wwwroot`,
  served by `MapTellma` with the SPA fallback of 0011 §5.1.
- Web project: references the Client esproj with `ReferenceOutputAssembly=false` and
  `Microsoft.AspNetCore.SpaProxy`; csproj carries `SpaRoot`, `SpaProxyServerUrl`,
  `SpaProxyLaunchCommand`. The Migrator never builds the esproj and needs no Node.
- `test/Tellma.Distro.<Slug>.E2E/` — Playwright (in 0011: "scaffolded with no tests until the UI
  specs ship").
- Every distribution exposes a "distribution contract surface", e.g. `GET /api/distribution-info`.
- The distribution's own `CLAUDE.md` is "short; points at the package-shipped guidance channels".

### 2.6 Platform repo layout as documented (ARCH §Platform repo layout) vs as built

Documented client tree: `client/projects/core/{tellma-core, tellma-core-ui, tellma-core-ui-tokens,
tellma-core-ui-testing, tellma-core-ui-mcp}`, `module/tellma-module-sales`,
`industry/tellma-industry-pharma-sales`, `compliance/tellma-compliance-sa`,
`connector/tellma-connector-zatca`, `locale/tellma-locale-ar`, `apps/tellma-landing` ("built into
`src/apps/Tellma.Landing/wwwroot`").

As built (`client/projects/`):
- `core/tellma-core-ui`, `core/tellma-core-ui-tokens`, `core/tellma-core-ui-testing`,
  `core/tellma-core-ui-mcp`, `locale/tellma-locale-ar`, `internal/showcase`.
- **Absent**: `core/tellma-core` (`@tellma/core`), every `module/`, `industry/`, `compliance/`,
  `connector/` package, `apps/tellma-landing`.
- **Present but undocumented in ARCH**: `internal/showcase` (the dev host/e2e target).

Documented repo items that do not exist: `distributions/` (and all `Tellma.Distro.Acme.*`
projects), `taxonomy.json`, `changelog.d/`, `migrations/`, `releases/`, `CHANGELOG.md`,
`.github/workflows/release.yml`, `changelog-fragment.yml`, `.github/dependabot.yml`,
`templates/tellma-distribution/` (only `.gitkeep`), `build/Tellma.Core.targets` (`build/` has only
`.gitkeep`), `src/tooling/Tellma.Cli`, `src/apps/Tellma.Landing`, `Tellma.Core.AspNetCore`,
`Tellma.Core.Mcp`, `Tellma.Core.Migrator`, `Tellma.Core.Imaging`, `Tellma.Core.Analyzers`,
`src/defaults/*`, `src/module/*`, `src/industry/*`, `src/compliance/*`, `src/locale/*`,
`src/connector/azure-blobs`, `src/connector/zatca`.

Present but not in the ARCH tree: `src/apps/Tellma.Identity.Migrations`,
`eng/identity-ui-inspection` (`README.md`, `inspect.ps1`, `ui-inspection-guide.md`), `samples/`
(empty), `SECURITY.md`, `test/shared/Tellma.Queryex.Testing`, `test/shared/Tellma.Testing.Support`,
`test/apps/assets/Tellma.Identity.TestInProcHost`, `test/core/assets/*MigrationsHost*`.

### 2.7 Server and test projects as built

`src/` (with `.csproj`):
- core: `Tellma.Core` (**empty**: the csproj only references `Tellma.Core.Abstractions`; no source
  files), `Tellma.Core.Abstractions` (folders `Email/`, `Hosting/DeploymentIdentity.cs`,
  `TableTypes/`, `Tenancy/ISandboxContext.cs` + `SandboxContext.cs`, `Webhooks/`),
  `Tellma.Core.Email`, `Tellma.Core.EntityFrameworkCore`, `Tellma.Core.EntityFrameworkCore.Design`,
  `Tellma.Core.Queryex`, `Tellma.Core.Testing`, `Tellma.Core.Webhooks`.
- apps: `Tellma.Identity`, `Tellma.Identity.Web`, `Tellma.Identity.Migrations`.
- connector: `acs-email/Tellma.Connector.AcsEmail.Adapter`, `marmin-ae/Tellma.Connector.MarminAe`,
  `sendgrid/Tellma.Connector.SendGrid` + `.Adapter`, `smtp/Tellma.Connector.Smtp.Adapter`.
- eng: `eng/queryex-inspection/Tellma.Queryex.Inspection`.

`test/`: `apps/Tellma.Identity.{Tests, IntegrationTests, E2E, TestSupport}`,
`apps/assets/Tellma.Identity.TestInProcHost`; `core/Tellma.Core.{Abstractions.Tests, Email.Tests,
EntityFrameworkCore.Tests, EntityFrameworkCore.Design.Tests, EntityFrameworkCore.IntegrationTests,
Queryex.Tests, Queryex.IntegrationTests, Testing.Tests, Webhooks.Tests}`,
`core/assets/Tellma.Core.EntityFrameworkCore.MigrationsHost(.Package)`; `connector/<vendor>/*Tests`
(7 projects); `shared/Tellma.Queryex.Testing`, `shared/Tellma.Testing.Support`
(`Http/`, `Options/`, `Tenancy/`).

- **No `Tellma.Distro.Acme` project of any kind exists** (no `distributions/` folder, no
  `Tellma.Distro.Acme.Client`, no Acme tests). No `Tellma.Core.Tests` (0011's `taxonomy.json` test
  lives there).
- `global.json`: SDK `10.0.300`, `rollForward: latestFeature`. `.config/dotnet-tools.json`:
  `dotnet-ef` `10.0.9` only.
- Implemented-spec reality: 0001 (table types), 0002/0004/0005/0006 (client), 0003 (identity),
  0007 (email), 0008 (Queryex), 0009 (Marmin). Everything the SPA will talk to (BFF, CSRF, tenant
  middleware, `/{tenantId}/api/web/...`, `TellmaHub`, blobs, settings, permissions) is
  spec-only (0011–0022).

### 2.8 Multi-tenancy, identity and endpoint facts the SPA builds on

- Catalog schema `catalog` holds `Tenants`, `TenantMemberships`, `Sessions`, `CatalogState`
  (ARCH §Multi-tenancy). "`catalog.TenantMemberships` is a navigation hint (which tenants a subject
  may be shown), never an authorization source; permissions live in each tenant database." This is
  the tenant picker's source (brief cites 0011 §3.8).
- Every tenant is Live or Sandbox; sandbox tenants produce no external side effects; seam
  `ISandboxContext`.
- Endpoints are generated: "a POST-only private surface at
  `/{tenantId}/api/web/{resource}/{operation}` (`query`, `get`, `save`, `delete`, `activate`, tree
  and custom operations each an operation name, never a verb)". Public API:
  `/{tenantId}/api/v{version:apiVersion}`, bearer policy `Tellma.Api`, hand-written by the
  distribution. "The blob `GET` is the one `GET` on the tenant surface." (ARCH §The composition root
  and the two gates.)
- Startup "endpoint audit": an endpoint without an authentication policy is a startup failure.
- Identity (ARCH §Identity): each distribution is a confidential OIDC client using the BFF pattern
  (`client_id = <slug>`, secret in Key Vault); exact redirect URIs
  `https://<slug>.app.tellma.com/signin-oidc` and `…/signout-callback-oidc`; "The distribution owns
  its BFF": session store `catalog.Sessions`, compare-and-set token refresh, back-channel logout.
  Step-up authentication reads the distribution's **sensitive securables** (Core default set: user
  and role administration, …), not reachable through the tenant MCP server.
- In-proc identity mode via `Tellma:Identity:Mode`; store = catalog DB, schema `idsvr`; the
  reference distribution runs in-proc in Development.
- Identity amendments (ARCH §Identity, 0010): `Distribution` seed-client kind, per-tenant resources
  for `<origin>/{tenantId}/mcp`, CIMD, `tellma_kind` claim, `existingOnly` on bulk invite,
  versioned management API routes. The ARCH link still points at
  `docs/specs/0010-identity-server-amendments.md` (the round-2 README renames it).
- Tenant MCP server `tellma-tenant` (`Tellma.Core.Mcp`) at `/{tenantId}/mcp`; individual users only.

### 2.9 Reports — three tiers (ARCH §Reports — three tiers)

| Tier | Lives in | When |
|---|---|---|
| 1 — Queryex-expressible | Queryex text compiled to parameterized SQL at runtime | "List reports, GROUP BY aggregations, simple pivots — most reports" |
| 2 — complex aggregation | C# in the owning pack, raw SQL through `IDataBatch.Sql` | "Trial balance, P&L, cost-center rollups, complex pivots beyond Queryex's expressiveness" |
| 3 — analytics, BI, **dashboards** | "Separate analytical store fed by within-stack exports — the display-shape Excel export of any stack is the within-stack export tier (0019)" | "Out of scope for the OLTP module's contract; designed when needed" |

- Tier 1 "has one authoring path": Queryex compiles text from "what a user types into a filter,
  **what a stored report definition holds**, what a permission criterion says". So stored report
  definitions are already anticipated in tier 1.
- Tier 2 SQL reaches the DB only via `IDataBatch.Sql` with declared writes, ScriptDom-checked by
  `Tellma.Core.Analyzers`; EF query/`SaveChanges` over `TellmaDbContext` banned outside
  `Tellma.Core` and migrators.
- What-lives-where row: "Reports and auto-generation — C# — owning pack assembly, raw SQL only
  through `IDataBatch.Sql`".
- Schema-shape test (ARCH §Validation): every report/auto-gen entry point is instantiated against
  each registered leaf, SQL rendered, `sp_describe_first_result_set` run.
- Capability interfaces gate per-feature columns: a report needing columns beyond an entity's
  universal shape declares an interface (`IEmployeeForSalaryBandReport` example) used as a generic
  constraint; packs can also contribute `RequiresShape<TDefault, TShape>()`.
- Nothing about report definitions storage, sharing, renderers, the studio, or dashboards beyond the
  tier-3 row.

### 2.10 Frontend decisions (ARCH §Frontend)

- "Angular for every distribution SPA and the landing page; Blazor is not used." Deciding
  constraint: low-end devices and constrained networks (initial bundle size, cold start, memory).
- "The reusable client primitives ship as the `@tellma/core` npm package plus per-pack `@tellma/*`
  libraries"; "each distribution composes them in its own Angular workspace".
- **No SSR, no hydration** — for distributions and the deferred landing page. "repeat loads are
  served from the service-worker/PWA cache"; "Instant first paint comes from a static, SW-cached
  **app shell** — which is not SSR and needs no render server."
- That is the entire SW/PWA statement in ARCH: no manifest, update policy, cache naming, install
  criteria, or Lighthouse gate.

UI component library (ARCH §Frontend → UI component library):
- Ships as a `core-*` family under `client/projects/core/`, "part of the bare-minimum Core layer
  every distribution references and family-major versioned".
- Greenfield on `@angular/cdk` + `@angular/aria`; never Material/PrimeNG; "Angular-only,
  permanently". `@angular/aria` and Signal Forms are stable in v22; aria "pinned to the platform's
  Angular minor, tracking only the latest stable release (no preview/next tags)".
- Package table: `@tellma/core-ui` (the `tm-*` components + `/contracts` with `SignalLike`,
  `WritableSignalLike`, `TmFormFieldControl`, `TmCellEditor`, `TmCellDisplay`),
  `@tellma/core-ui-tokens`, `@tellma/core-ui-testing` (harnesses "for tests and agents"),
  `@tellma/core-ui-mcp` (data from generated `components.json`).
- "There is **no separate per-component headless 'pattern' layer**"; a headless engine is "reserved
  for the future editable data grid … would ship in its own package" (it shipped as an entry point;
  see §5).
- **Conventions**: prefix `tm-` / `Tm…` "on every selector, class, token, and provider, enforced by
  an ESLint selector rule"; public API signal-first (`input()`/`model()`/`output()`); zoneless,
  OnPush is the v22 default and not set explicitly; inline templates for small components; static
  slots via attribute-selector `ng-content` (`[tmXxx]`); data slots via typed `ng-template`
  contexts with `ngTemplateContextGuard`; config via per-feature injection tokens + `provideTm*()`;
  `hostDirectives` over inheritance; "anything pluggable is an adapter". The Angular CLI MCP's
  `get_best_practices` "is the source of truth for framework conventions".
- **Theming**: tokens in three tiers (primitive → semantic → component); `TmTokens` → JSON Schema;
  presets validated at build (schema + missing-ref); contrast via axe CI gate; runtime-switchable
  via CSS variables + `@layer`; dark mode by selector; "**no theme-builder UI**, now or later";
  forced-colors first-class; density ladder `sm`/`md`/`lg` defaulted by `TM_FORM_FIELD_DEFAULTS`,
  **default `sm`**; runtime density switch and type-scale axis deferred-but-designed-for; base and
  default-theme CSS generated at build; "each distribution's token deltas are emitted at build
  time into a static stylesheet baked into its `index.html`"; themes authored by agents via MCP
  `generate_theme` / `validate_theme` tools.
- **Accessibility & i18n**: CDK a11y + aria; axe-core CI gate; RTL via CDK `Directionality`, CSS
  logical properties, RTL-aware keyboard nav. **Transloco** is the runtime i18n library; library
  strings via `TM_UI_TRANSLATE` (Transloco default, zero config); `/contracts` never imports
  Transloco. "Server-originated text reaches the SPA as resource keys and raw arguments"; the host
  serves each offered language's `.resx` strings as one **string pack** merged into Transloco
  (0013), so a language switch re-renders "without a request". Only English library strings ship in
  core; Arabic/Amharic ship as Locale packs (`@tellma/locale-ar` ships with the foundation;
  `fallbackLang` English). "Dates, numbers, and currency format through swappable `TmDateAdapter` /
  `TmNumberAdapter` / `TmCurrencyAdapter` — e.g. a Hijri calendar provided by a Locale pack"
  (stale; see §5). Fonts self-hosted, in the app's `styles` array, `unicode-range`,
  `font-display: swap`, metric-adjusted fallback faces; only Latin in core; preload is a
  post-build step injecting `<link rel="preload">` into the built `index.html`.
- **Forms**: Signal Forms only (no CVA); `FormValueControl<T>` / `FormCheckboxControl`
  (`tm-checkbox`, `checked`, no `value`); bound field authoritative for `disabled`/`required`;
  **validation messages are an anchored popover, never a row under the field**, listing every
  error while focused; invalid border + in-field glyph out of focus; messages also in a permanent
  visually-hidden `aria-live` region in `aria-describedby`; the grid's active-cell error renders the
  same bubble. `provideTellmaForms()` under the `provideTellmaUi()` umbrella ("calls
  `provideTellmaUi()` once and writes no further config").
- **Icons**: SVG only; Lucide, 1.75 stroke, 24 viewBox; "`tm-icon` + a `TmIconRegistry` (default
  set Lucide, sanitized/Trusted Types, swappable per distribution) arrive with the first component
  that accepts consumer-supplied icons" (not built yet).
- **Quality & docs**: API goldens per entry point (API Extractor + "an `approve-api` CI gate");
  every `@deprecated` paired with "an enforced `@breaking-change <version>`"; CI runs unit +
  harness + axe + bundle-size budget; ESLint flat config + prettier; docs generated from source
  (typed inputs + JSDoc + co-located `*.examples.ts` → API Extractor + thin extractor →
  **`components.json`**, feeding the showcase, `llms.txt`, MCP server, scaffold/validate tooling,
  goldens).
- **MCP topology**: Tellma Developer MCP `tellma-dev` (`@tellma/core-ui-mcp` scoped and versioned
  with the library, `npx @tellma/core-ui-mcp@<pinned>`; `dotnet tellma mcp` umbrella federates
  pinned servers and hosts cross-layer scaffolding such as "entity-to-screen"); Tellma Tenant MCP
  `tellma-tenant` (`Tellma.Core.Mcp`, `/{tenantId}/mcp`).

### 2.11 Versioning, releases, guidance (ARCH §Versioning & Upgrades and below)

- SemVer; one family major across all NuGet **and npm** packages ("and the matching npm packages");
  minors/patches independent within a major; distributions pin individual packages.
- Platform: release-branch model (`main`, `release/<major>.x`, immutable tags `vX.Y.Z`); distros:
  trunk-based.
- News fragments `changelog.d/<id>.<type>.md`, `<type>` ∈ `feature | fix | breaking | security |
  chore | convention`; frontmatter `prs`, `breaking`, `affects` (example `[csharp]`), `migration`/
  `recipe` with `kind` (code-mod, SQL script, manual steps, config change), `obligation: required |
  recommended`. Release CI aggregates into `CHANGELOG.md` + `releases/v<X.Y.Z>.json` shipped in the
  `Tellma.Core` NuGet and `@tellma/core` npm package under `docs/` ("node_modules\@tellma\core\docs\").
- Mandatory conventions ship as analyzer/**ESLint rule** + code-mod + `convention` fragment.
- Logic vs configuration (ARCH §Logic vs. configuration): "logic lives in the published packages and
  propagates on version bump; configuration is scaffolded once and owned by the distribution. Dev
  tooling counts as logic" — e.g. "`bin` scripts in `@tellma/core`". Distribution-owned:
  "Distribution name, slug, branding, region; CI/CD workflow YAML; `.gitignore`; README".
- Phase 1: all this machinery is dormant; one repo-root `CLAUDE.md` + `guidance/` (the
  `guidance/` folder does not exist).
- Local dev loop: npm packages published with sourcemaps + original `.ts`; local npm registry
  (e.g. Verdaccio) via `dotnet tellma publish-local`; "no project-reference rewiring or
  `npm link`ing".

### 2.12 Hosting, CI and observability (ARCH §Hosting on Azure, §CI/CD, §Observability)

- No Front Door / CDN / WAF; TLS at App Service via managed cert; "a static-asset CDN for users far
  from the hosting region" is a deferred optimization (ARCH §Open Questions "Edge ingress").
- App Service `Always On`; `WEBSITES_CONTAINER_STOP_TIME_LIMIT = 30`; slot-swap deploys with
  automatic rollback on health probe failure (N−1 schema compatibility). **During a rollout and
  after a rollback, an N and an N−1 web app can both serve, so a cached SPA bundle may talk to
  either version.**
- Real-time: Azure SignalR Service (hub-only token), one instance per environment shared and
  isolated by application name; Redis only for on-prem multi-instance (0021).
- Config sections under `Tellma:` — `Sql`, `Tenancy`, `Identity`, `Blobs`, `Excel`, `ScratchPath`,
  `Jobs`, `Notifications`, `Outbox`, `Realtime`, `Seed` — each `ValidateOnStart`. No client-related
  section.
- CI (ARCH §CI/CD): required on every distribution PR — unit, integration against real SQL, "the
  distribution's E2E project (Playwright smoke against the deployed staging slot; the project runs
  empty until the UI specs ship)". .NET traits `Category=Integration`, `Live=true`.
- Observability is **server-only**: App Insights per distribution → shared Log Analytics;
  cross-distribution dashboard (health, usage, error rate, `Tellma.Core` version drift); meters via
  `IMeterFactory` named after the emitting package (`Tellma.Core.AspNetCore` for the host and
  `tellma.realtime.*`); instrument names lowercase dot-separated under `tellma.`; telemetry names
  are `const`s in `.Abstractions`; tag cardinality bounded; `tellma.tenant.id` on traces/logs,
  never metrics; alert KQL in `infra/monitoring/` cross-checked by tests. **Nothing on client
  telemetry, UI error logging, RUM, or trace-id surfacing.**

### 2.13 Rollout & phasing (ARCH §Rollout & Phasing)

- Phase 1: distributions live in `distributions/<slug>/` "in exactly the shape of a distribution
  repo", depend on platform projects "via `<ProjectReference>` (C#) and **TypeScript path mappings
  (Angular)**", one-way dependency enforced by an architecture test/analyzer.
- No landing page; users go directly to `<slug>.app.tellma.com`; cross-distribution discovery
  deferred.
- Fixed names/ports: dev DBs `Tellma.dev.<slug>.*`; tracked `launchSettings.json` with fixed ports;
  `dotnet run --project distributions/acme/src/Tellma.Distro.Acme.Migrator -- migrate` then
  `dotnet run --project distributions/acme/src/Tellma.Distro.Acme.Web`.

### 2.14 Open questions in ARCH that touch round 2 (ARCH §Open Questions)

- "UI component-library v1 scope: the v1 ERP component set (basic inputs, editable data-grid,
  entity-picker, tree-table, layouts, **app shell**, plus the form-field wrapper,
  field-array/line-items editor, **filter/query-builder**, **description list**, and
  **toast/confirm services** …) needs finalizing and prioritizing."
- "UI component-library foundation" — answered by 0002 (aria + Signal Forms stable, no theme
  builder, runtime density deferred).
- "MCP federation mechanics" — open.
- "Edge ingress / WAF trigger" (incl. static-asset CDN) — open.
- "Distribution discovery" — end-user routing deferred with the landing page.

## 3. The client workspace as built (`client/`)

### 3.1 Versions and toolchain (`client/package.json`, `pnpm-workspace.yaml`)

- `"private": true`, `"type": "module"` (ESM everywhere; each library manifest also
  `"type": "module"`), `packageManager: pnpm@11.9.0`, `engines.node: >=24`.
- Runtime deps: `@angular/{common,compiler,core,forms,platform-browser,router}` **22.0.5**,
  `@angular/aria` + `@angular/cdk` **22.0.3**, `@jsverse/transloco` + `transloco-messageformat`
  **8.4.0**, `@internationalized/date` 3.12.2, `rxjs ~7.8.0`, `tslib ^2.3.0`.
- Dev deps: `@angular/build`/`cli`/`compiler-cli` 22.0.5, `ng-packagr` 22.0.1, `typescript ~6.0.2`,
  `vitest` 4.1.9 + `@vitest/browser-playwright` 4.1.9, `@playwright/test` 1.61.1, `axe-core` +
  `@axe-core/playwright` 4.12.1, `eslint` 10.6.0, `angular-eslint` 22.0.0, `typescript-eslint`
  8.62.1, `stylelint` 17.14.0, `prettier ^3.8.1`, `@microsoft/api-extractor` 7.58.9, `ts-morph`
  28.0.0, `tsx` 4.22.4, `esbuild` 0.28.1, `zod` 4.4.3, `@modelcontextprotocol/sdk` 1.29.0, `jsdom`
  29.1.1, fonts `@fontsource-variable/noto-sans`, `-noto-sans-arabic`, `@fontsource/noto-sans-mono`
  5.2.10, `@capsizecss/metrics`.
- **Not present**: `@angular/service-worker`, `@microsoft/signalr`, any charting library,
  `@angular/cdk/drag-drop` usage (CDK is present; only `cdk/overlay` ×12 and `cdk/scrolling` ×1 are
  imported by core-ui), `@angular/animations`.
- `pnpm-workspace.yaml`: `packages: ["projects/core/tellma-core-ui-mcp"]` only (the ng-packagr
  libraries are not pnpm workspace packages); `allowBuilds` for `@parcel/watcher`, `esbuild`,
  `lmdb`, `msgpackr-extract`. pnpm 11 config lives here, not in `package.json` (memory note).

### 3.2 Projects (`client/angular.json`, `client/README.md`)

| angular.json project | Type | Root | npm name | Version |
|---|---|---|---|---|
| `core-ui` | library (`@angular/build:ng-packagr`; test `@angular/build:unit-test`, `browsers: ["chromiumHeadless"]`) | `projects/core/tellma-core-ui` | `@tellma/core-ui` | 0.1.0 |
| `core-ui-tokens` | library | `projects/core/tellma-core-ui-tokens` | `@tellma/core-ui-tokens` | 0.1.0 |
| `core-ui-testing` | library | `projects/core/tellma-core-ui-testing` | `@tellma/core-ui-testing` | 0.1.0 |
| `locale-ar` | library | `projects/locale/tellma-locale-ar` | `@tellma/locale-ar` | 0.1.0 |
| `showcase` | application (`@angular/build:application`, prefix `app`) | `projects/internal/showcase` | — (never published) | — |
| (none) | Node package, `tsc -b` | `projects/core/tellma-core-ui-mcp` | `@tellma/core-ui-mcp` (bin `tellma-core-ui-mcp`) | 0.1.0 |

- `angular.json` `cli.packageManager: pnpm`, `analytics: false`; every library `prefix: "tm"`.
  Library prod builds use `tsconfig.lib.prod.json` (`compilationMode: "partial"`); output
  `dist/<short>` (e.g. `dist/core-ui`).
- `tsconfig.json`: `strict`, `noPropertyAccessFromIndexSignature`, `noImplicitOverride`,
  `isolatedModules`, `target ES2022`, `module preserve`; Angular `strictTemplates`,
  `strictInjectionParameters`, `strictInputAccessModifiers`, `typeCheckHostBindings`. `paths` map
  `@tellma/core-ui` → `projects/core/tellma-core-ui/public-api.ts`, `@tellma/core-ui/*` →
  `…/*/public-api.ts`, `@tellma/core-ui-tokens`, `@tellma/core-ui-testing`, `@tellma/locale-ar` to
  source. The showcase compiles from **source** via these paths ("dist/ is output, never input",
  `scripts/serve.mjs`).
- Library-on-library builds: `locale-ar/tsconfig.lib.json` re-maps `@tellma/core-ui(/*)` to
  `../../../dist/core-ui(/*)` (source mappings would leak foreign sources into `rootDir`; memory
  note). `baseUrl` is a TS 6 error.

### 3.3 Entry-point convention (`projects/core/tellma-core-ui/README.md`, `tools/workspace.mjs`)

- "The package root is the code root": no `src/` in the ng-packagr libraries; `public-api.ts` at
  each package root; every subfolder with its own `ng-package.json` is a secondary entry point; any
  other folder belongs to the primary entry point.
- "Entry points import shared code via `@tellma/core-ui` only — never a relative `../` path (each
  entry point is its own compilation unit)." Shared-but-unstable wiring goes in `./private`
  (no API goldens, "never for app code").
- "Component hosts must be `display: block`."
- Usage examples in co-located `*.examples.ts` (dependency-free template objects), compile-checked by
  `docs-examples.spec.ts`.
- `@tellma/core-ui` has **26 secondary entry points + the primary**: `alert`, `button`,
  `calendar-ethiopic`, `calendar-umalqura`, `checkbox`, `contracts`, `date-picker`,
  `entity-picker`, `file-preview`, `files`, `form-field`, `grid`, `grid-engine`, `image`, `input`,
  `l10n`, `menu`, `modal`, `number`, `popover`, `private`, `select`, `spinner`, `tabs`, `tooltip`,
  `tree-grid`.
- The **primary** entry point is component-free by rule: `provideTellmaUi()`,
  `provideTellmaForms()`, `TM_UI_TRANSLATE`, `TM_UI_MESSAGE_CONTEXT`, `TM_ACTIVE_LOCALE`, the
  `TmL10n` facade, `TM_CALENDAR` + `provideTmCalendar()`, `TmClientCache`, `tmMinDate`/`tmMaxDate`,
  field-error resolution, `fonts/fonts.css`. Folders: `cache/`, `i18n/`, `forms/`,
  `l10n-facade/`, `providers/`, `fonts/`, `styles/`.
- Bare directives (`tmButton`, `tmInput`, `tmNumber`) ship global stylesheets the app must add to
  `styles` (`styles/tm-button.css`, `tm-input.css`, `tm-number.css`; authoritative list in
  `package.json` `"tellma".docs.globalStyles`).

### 3.4 DI seams and client-side state that round 2 inherits (core-ui source)

- Injection tokens exported: `TM_FORM_FIELD_CONTROL`, `TM_ERROR_DISPLAY`, `TM_FORM_FIELD_DEFAULTS`,
  `TM_CELL_EDITOR_HOST`, `TM_GRID_CONTEXT`, `TM_ACTIVE_LOCALE`, `TM_UI_MESSAGE_CONTEXT`,
  `TM_UI_TRANSLATE`, `TM_BLOB_FETCHER`, `TM_MODAL_DATA`, `TM_CALENDAR`.
- `provideTellmaUi(options: TmUiOptions)` (`providers/provide-tellma-ui.ts`): `provideTransloco`
  with `defaultLang: 'en'`, `fallbackLang: 'en'`, `availableLangs` (default `['en']`, must be
  non-empty), `missingHandler.useFallbackTranslation: true`, `reRenderOnLangChange: true`; ICU
  transpiler `ɵTmMessageFormatTranspiler`; merges English strings under `TM_UI_I18N_SCOPE`; calls
  `provideTellmaForms(options.forms)`. **No `TranslocoLoader` is configured** — app strings and
  server string packs need a loader the platform does not yet provide. "Font preloading is a
  distribution-shell concern and is not wired here."
- `TM_ACTIVE_LOCALE` default = the live Transloco language (bare tag, e.g. `ar`); without Transloco,
  `LOCALE_ID`. The showcase overrides it to map `ar` → `ar-SA`, `en` → `en-US` because "bare 'ar'
  resolves to Latin digits in ICU, ar-SA to Arabic-Indic ones" (`showcase/src/app/app.config.ts`).
- `TM_CALENDAR`: `Signal<TmCalendar>`, default Gregorian; `provideTmCalendar(calendar | signal)`;
  opt-in `tmUmalquraCalendar()` and `tmEthiopicCalendar()` entry points. Backing values stay ISO.
- `TmClientCache.clearAll()` (`cache/tm-client-cache.ts`): deletes every **CacheStorage** cache whose
  name starts with `tm-` (today only `tm-images-v1`, `image/internal/blob-cache.ts`). Doc says "the
  same convention will bind any future IndexedDB database or web-storage key" but the code only
  touches CacheStorage. "A distribution calls `clearAll` on logout. Tenant switching clears nothing:
  cache keys carry tenant identity by construction (the tenant id is part of every API URL)."
- `TM_BLOB_FETCHER`: `(url, { ifNoneMatch? }) => Promise<{ status, blob, etag }>`, default over
  `HttpClient` GET ("the app's interceptors apply — auth headers (and later BFF cookies)"). **No
  `AbortSignal` parameter.**
- Entity picker search seam `TmEntitySearchFn<T> = (query: string, signal: AbortSignal) =>
  TmEntitySearchResult<T> | Promise<…>`; `TmEntitySearchResult<T> = readonly T[] | { items,
  hasMore? }`; the signal fires when superseded, closed or destroyed (a grid cell host takes over).
  Sync results render without a spinner. `TmEntityPick { id, label, item? }`.
- Dark mode: the emitter writes light vars on `:root` and dark under `[data-theme=dark]` inside
  `@layer tm.base` (`core-ui-tokens/emit/emit-css.ts`); no `prefers-color-scheme` rule, so a
  "System" theme option needs app logic setting `data-theme`.
- `tools/tokens/build-css.mts` emits only `projects/core/tellma-core-ui-tokens/css/tellma-default.css`
  (gitignored, generated by `tokens:build-css`, run by `prestart`/`pree2e`/`build`). There is no
  distribution token-delta pipeline.
- The identity server consumes the same tokens: `tools/fonts/copy-identity-assets.mts` copies the
  emitted tokens CSS and fonts into `src/apps/Tellma.Identity/wwwroot/{css,fonts}`; `tokens:check`
  fails when the committed copy drifts.

### 3.5 Scripts and gates (`client/package.json` scripts, `client/scripts/`)

| Script | What it does |
|---|---|
| `start` | `node scripts/serve.mjs showcase` (port from `.dev-ports.local` key `CLIENT_SHOWCASE`, else OS-assigned); `prestart` builds token CSS |
| `build` | `scripts/build.mjs`: `tokens:check`, `tokens:build-css`, then every package in dependency order (Kahn sort over declared `dependencies`/`peerDependencies`/`devDependencies`, name tie-break); ng-packagr via `ng build <short>`, Node packages via `tsc -b` |
| `build:showcase` | `ng build showcase && node scripts/inject-font-preloads.mjs dist/showcase/browser` |
| `test` | `scripts/test.mjs`: every angular.json project with a `test` target and ≥1 `*.spec.ts`, `ng test <name> --watch=false` |
| `lint` | `eslint . && stylelint "projects/**/*.css" --allow-empty-input` |
| `typecheck` | `tsc --noEmit` over core-ui `tsconfig.lib.json`, `tools/tsconfig.json`, `e2e/tsconfig.json` (the only compile of `*.examples.ts`) |
| `lint:test` | `vitest run --config tools/vitest.config.mts` (custom lint rules, extractor, MCP smoke) |
| `e2e` | `scripts/e2e.mjs`: port key `CLIENT_SHOWCASE_E2E` → env `SHOWCASE_PORT`; locally the projects `chromium`, `firefox`, `webkit`, `touch` run sequentially, CI runs one parallel invocation |
| `tokens:build-css` / `tokens:check` | Token CSS emission / zod schema + missing-ref gate (contrast is axe's job) |
| `api:check` / `api:approve` | `scripts/api-extractor.mjs` goldens |
| `docs:build` | `components.json` + schema + `llms.txt` into `projects/core/tellma-core-ui-mcp/generated/` |
| `golden:approve` | `tools/goldens/generate-format-golden.mts` (number/date format golden) |
| `size:check` | `scripts/bundle-size.mjs` per-entry-point gzip budgets |
| `identity:copy-assets` | Refresh the identity server's copied tokens/fonts |

- "No hardcoded ports" (`client/README.md`): every server binds a port from `.dev-ports.local` or an
  OS-assigned free port (`scripts/ports.mjs`, `getPort(name)`); "Never pass a literal port in
  configs or scripts."
- `scripts/serve.mjs --prod` serves the production build as static files (CI e2e uses it because the
  dev bundle's latency made keyboard tests flaky).

### 3.6 Discovery rules a new package must satisfy (`client/tools/workspace.mjs`)

- A **library** is any `projects/<area>/<name>` folder (exactly two levels) with both `package.json`
  and `ng-package.json`; a **Node package** has `package.json` without `ng-package.json`; "Apps carry
  no package.json, so they never appear here."
- `short` = folder name minus a leading `tellma-`; the build runs `ng build <short>`, so the
  angular.json project key must equal it (folder `tellma-core` → project `core`).
- Built FESM path assumed: `dist/<short>/fesm2022/<folder>[-<sub>].mjs`; d.ts
  `dist/<short>/types/<folder>[-<sub>].d.ts`; golden file `<short>[.<sub>].api.md`.
- Per-package **policy** lives in the library's `package.json` `"tellma"` field:
  `budgetsInKb` (`{ ".": n, "./sub": n }`), `budgetNotes`, `apiGoldens.exclude`,
  `docs.globalStyles`.

### 3.7 Bundle budgets

- Per entry point (`scripts/bundle-size.mjs`, `"tellma".budgetsInKb`): gzipped **self-weight** of
  each built FESM bundled by esbuild with `@angular/*`, `rxjs`, `tslib`, `@jsverse/*` and every
  `@tellma/*` external. Coverage is bidirectional (every entry point needs a ceiling; every ceiling
  must match an entry point); a library with no block is exempt. "Ceilings are ratchets. Any raise
  belongs here with its reason; a raise with no note is a bug."
- Current core-ui ceilings (KB gz): `.` 5, `alert` 2, `button` 2, `calendar-ethiopic` 2,
  `calendar-umalqura` 5, `contracts` 1, `date-picker` 14, `entity-picker` 12, `file-preview` 8,
  `files` 5, `grid-engine` 15, `grid` 38, `image` 10, `tree-grid` 8, `input` 4, `l10n` 8, `menu` 6,
  `modal` 6, `number` 4, `popover` 4, `tooltip` 3, `checkbox` 4, `form-field` 5, `private` 4,
  `select` 8, `spinner` 1, `tabs` 4. `core-ui-tokens` `.` 8. `core-ui-testing` and `locale-ar`
  have no budgets (exempt). Budget note on `grid`: spec 0004 set 24 KB; the ceiling "has climbed
  34 -> 35 -> 36 -> 37" (now 38) — "Worth a dedicated pass".
- App level (`angular.json`, showcase `production` only): `initial` warn **1MB** / error **2MB**
  (raw, not gzip); `anyComponentStyle` warn 12kB / error 13kB; `outputHashing: "all"`. No budget
  exists for any real application, and no gzip/transfer-size measure of an app's initial load.

### 3.8 API goldens (`scripts/api-extractor.mjs`, `client/api/`)

- API Extractor runs over ng-packagr's flattened `.d.ts`, one `*.api.md` per public entry point in
  `client/api/` (29 files: 26 core-ui incl. primary minus `private`, + `core-ui-tokens`,
  `core-ui-testing`, `locale-ar`). `--check` fails on drift; `--approve` regenerates. Stale goldens
  for deleted/excluded entry points fail the check. The Node MCP package is out of scope.
- The script restores the `@packageDocumentation` header from the source `public-api.ts`, so every
  entry point's `public-api.ts` must start with a `/** … @packageDocumentation */` block (it throws
  otherwise). `ae-forgotten-export` and `ae-unresolved-link` are silenced.

### 3.9 Docs pipeline (`tools/docs/`)

- `docs:build` → `extractComponents()` → zod-validated `components.json` (`schemaVersion` "2.0.0";
  `components[]` with `name`, `kind: component|directive`, `group`, `selector`, `entryPoint`,
  `formControl: FormValueControl|FormCheckboxControl|null`, `description`, `inputs`, `outputs`,
  `slots`, `tokens`, `a11y { roles, keyboard, notes }`, `examples`, `harness`, `status:
  stable|experimental|deprecated`, `deprecation?`) + `components.schema.json` + `llms.txt`.
- JSDoc tags `@tmGroup` (default `form-control`) and `@tmStatus` (default `stable`). A harness is
  advertised only if `class <Name>Harness` exists in `@tellma/core-ui-testing`.
- The build **fails if any component doc contains "§" or "spec NNNN"** (public text must be
  self-contained).
- The extractor scans **only `projects/core/tellma-core-ui`** (`uiDir`, `library.name ===
  '@tellma/core-ui'`); components in any other package are invisible to `components.json`,
  `llms.txt` and the MCP server.
- MCP server (`core-ui-mcp/src/server.ts`) registers three tools: `list`, `describe`, `example`. No
  `generate_theme` / `validate_theme`.

### 3.10 Lint rules (`client/eslint.config.mjs`, `stylelint.config.mjs`, `tools/eslint/`)

- `projects/**/*.ts`: `@angular-eslint/directive-selector` attribute, prefix `tm`, camelCase;
  `component-selector` element, prefix `tm`, kebab-case. `projects/internal/**` overrides to `app`
  (directives) and `app`/`showcase` (components).
- `tm/prefix-exports` (files `projects/core/**/*.ts` and `projects/locale/**/*.ts`, excluding specs,
  examples, `*testing.util.ts`, the MCP package): types/classes `Tm…`, SCREAMING constants `TM_…`,
  functions/values `tm…` or `provideTellma…`; leading `ɵ` is transparent; reviewed allowlist
  `SignalLike`, `WritableSignalLike`, `fontPreloadLinks`, `PreloadLink`, `provideTmCalendar`.
- Boundary rules (`no-restricted-imports` / `-globals` / `-syntax`): `contracts` (no Angular, no
  `@tellma/*`, no rxjs/`@jsverse`, no `../`); `grid-engine` (signals-only `@angular/core`, only
  `/contracts`, DOM-free incl. `localStorage`/`sessionStorage`, no `InjectionToken`); `l10n` and
  `calendar-*` (Angular-free, DOM-free, DI-free, only `/l10n` + `/contracts`).
- Templates: `angular.configs.templateRecommended` + `tm/no-state-bindings-on-form-field`.
- Stylelint for `projects/core/tellma-core-ui/**`, `tellma-core-ui-tokens/**`, `projects/locale/**`:
  `selector-class-pattern ^tm-`; no `outline: none|0`; `tm/no-hardcoded-sizing` (allow `1px`). The
  showcase is exempt.
- Prettier: `printWidth 100`, `singleQuote`, Angular parser for HTML — but **no script or CI step
  runs prettier**. No rule enforces `@deprecated` + `@breaking-change` pairing.

### 3.11 Test runners and e2e

- Unit: `@angular/build:unit-test` (vitest). core-ui runs in real Chromium (`chromiumHeadless`,
  `@vitest/browser-playwright`); other libraries use the default runner environment. `tsconfig.spec.json`
  types `vitest/globals`. 56 `*.spec.ts` files under `projects/`.
- Memory-note gotchas (verified still relevant): specs are AOT-compiled (dynamic `@Component({
  template })` fails NG1010; use `TestBed.overrideComponent`); `fixture.whenStable()` hangs on a
  pending `resource()`; `TranslocoService` is `providedIn: 'root'` so probe `TRANSLOCO_TRANSPILER`
  instead of optional-injecting it.
- E2E (`e2e/playwright.config.ts`): `testDir ./specs` (39 specs), `baseURL
  http://localhost:${SHOWCASE_PORT}`, throws if `SHOWCASE_PORT` unset; projects `chromium` (Desktop
  Chrome, all but touch specs), `firefox`/`webkit` (only `@cross-engine`-tagged tests, 60 s
  timeout), `touch` (Pixel 7; `grid-touch|tooltip-touch`); CI retries 2; `webServer` runs
  `scripts/serve.mjs showcase` (`--prod` on CI); reports in `../.artifacts/e2e/`.
- `e2e/support/axe.ts` `expectNoAxeViolations(page, selector?)`: tags `wcag2a`, `wcag2aa`,
  `wcag21a`, `wcag21aa`; excludes `.tm-select__value--placeholder`.
- `e2e/support/story-map.ts` `storyUrl(id, { dir?, theme? })` → `/story/<id>?dir=&theme=`; the
  harness is built around the showcase's `/story/:id` routes. There is no e2e setup for an app with
  auth, a backend, or a service worker.

### 3.12 Service worker / PWA status

- **No service worker anywhere**: no `@angular/service-worker` dependency, no `ngsw-config.json`, no
  `provideServiceWorker`, no `manifest.webmanifest`, no app icons (showcase has only `favicon.ico`).
  A grep over `client/projects`, `src` for `service-worker|ngsw|serviceWorker|manifest.webmanifest`
  finds nothing.
- The showcase `index.html` is the CLI default (`<base href="/">`, viewport meta, favicon); no
  `theme-color`, no manifest link.

### 3.13 CI client job (`.github/workflows/ci.yml` job `client`)

- ubuntu-latest, 30 min, `working-directory: client`; pnpm from `packageManager`; Node 24;
  `pnpm install --frozen-lockfile`; `playwright install chromium firefox webkit --with-deps`; then
  in order: `lint`, `typecheck`, `lint:test`, `tokens:check`, `build`, `test`, `api:check`,
  `docs:build`, `size:check`, `build:showcase`, `e2e`.
- Gated by `all-checks-passed` with `build-test` (Ubuntu + Windows .NET matrix), `format`
  (`dotnet format --verify-no-changes`), `integration`, `design-e2e`, `identity-e2e`.
- No client job runs on Windows (the AGENTS.md rule says scripts must build and run on both).
- No Lighthouse/PWA gate, no app-level bundle gate beyond the showcase's `angular.json` budget.

### 3.14 Showcase as the reference consumer wiring (`projects/internal/showcase`)

- `app.config.ts`: `provideBrowserGlobalErrorListeners()`, `provideRouter(routes)`,
  `provideHttpClient()` ("Backs tm-image's default TM_BLOB_FETCHER"), `provideTellmaUi({ forms: {
  formFieldDefaults: { size } } })`, `provideTellmaLocaleAr()`, `TM_CALENDAR` from a
  `ShowcaseCalendar` service, and the hand-written `TM_ACTIVE_LOCALE` (`ar` → `ar-SA`).
- `angular.json` `styles`: `tellma-core-ui-tokens/css/tellma-default.css`,
  `tellma-core-ui/fonts/fonts.css`, `styles/tm-button.css`, `tm-input.css`, `tm-number.css`,
  `tellma-locale-ar/fonts/fonts.css`, the app's `styles.css`. `allowedCommonJsDependencies:
  ["@messageformat/core"]`.
- Routes: `''` → `StoryIndex`, `story/:id` → `StoryPage`. The shell sets `dir`/`lang`/`data-theme`
  on `<html>`; the live CDK `Directionality` comes from a `<main [dir]>` wrapper (memory note: the
  root `Directionality` reads `<html dir>` only once).

### 3.15 Memory-note conventions that bind round 2 (`client-workspace-phase1.md`)

- Storybook removed; the internal app is "showcase", never "sandbox" (sandbox means tenants).
- Fonts ride the regular build; `TM_FONT_SUBSETS` / `fontPreloadLinks` no longer exist (yet
  `fontPreloadLinks`, `PreloadLink` remain on the ESLint allowlist). Contrast is axe's job only.
- Typography never keys on direction: one multi-script `--font-ui` stack; per-language leading via
  `[lang]:lang(x)` rules from `semantic.leadingByLang`.

## 4. Constraints a new client package must obey (as built)

1. Folder `client/projects/<area>/tellma-<name>/` with `package.json` (`name` `@tellma/<name>`,
   `"type": "module"`, `sideEffects: false`, Apache-2.0) and `ng-package.json`
   (`lib.entryFile: public-api.ts`, `dest: ../../../dist/<name>`); angular.json project key
   `<name>` with `build` (ng-packagr, prod tsconfig partial compilation) and `test` (unit-test)
   targets; a `tsconfig.json` `paths` entry; a `references` entry.
2. Package root is the code root; secondary entry points are subfolders with `ng-package.json`;
   no `../` imports across entry points; shared internals in a `private` entry point excluded from
   goldens.
3. Depending on another workspace library: declare it in `peerDependencies` (drives build order)
   and map it to `dist/` in that library's `tsconfig.lib.json` `paths`.
4. Every `public-api.ts` starts with a `@packageDocumentation` TSDoc block; every public entry point
   gets a committed `client/api/<name>[.<sub>].api.md`.
5. Every export carries `Tm`/`TM_`/`tm`/`provideTellma` (only enforced under `projects/core/**` and
   `projects/locale/**` today); every selector `tm` prefix (`projects/**` except `internal`);
   CSS classes `tm-`, no hardcoded sizing, no `outline: none` (only core-ui, tokens, locale CSS
   today).
6. A `"tellma".budgetsInKb` block covering every entry point, or a visible exemption by omission.
7. TSDoc on every declaration (AGENTS.md); no `§` / "spec NNNN" in public text (enforced by
   `docs:build` for core-ui components).
8. No literal ports; scripts run on Windows and Linux.
9. Components with a host must be `display: block`; signal-first API; zoneless; Signal Forms.
10. Harnesses for new components go in `@tellma/core-ui-testing` (`<Name>Harness`) if they are to
    be advertised in `components.json` — but the extractor only covers `@tellma/core-ui`.

## 5. Gaps for round 2

- **No `@tellma/core` npm package** — ARCH names it as the cross-cutting client runtime and the home
  of `bin` scripts and `docs/releases/`; nothing exists, so the HTTP layer, state, shell and
  telemetry need a home decided from scratch.
- **No service worker, manifest, or PWA wiring** — ARCH asserts a "SW-cached app shell" with no
  design; dependency, `ngsw-config`, update policy and Lighthouse criteria are all new.
- **No distribution client exists** — no `distributions/`, no `Tellma.Distro.Acme.*`; 0011's
  esproj client is spec-only; the reference app round 2 validates against must be created.
- **No server the SPA can call** — `Tellma.Core` is an empty csproj; `Tellma.Core.AspNetCore` (BFF,
  hub, endpoints) is absent; e2e against a real backend has no host yet.
- **No app-level bundle budget** — only the showcase's raw 1MB/2MB initial budget; the brief's
  "main bundle monitored for slow connections" needs a real app budget (preferably transfer-size).
- **No SignalR client dependency** — `@microsoft/signalr` (or equivalent) must be added for the
  leader-tab connection.
- **No chart library** — reports/dashboards need one (ECharts recommended by the 2026-07 research per
  memory index); nothing installed.
- **Missing UI primitives the shell and pages need** — no `tm-icon`/`TmIconRegistry`, toast/banner,
  confirm dialog service, skeleton, card/panel layout, splitter, drag-and-drop, tiles view, app bar,
  pinned bar, description list, filter builder (ARCH's own v1-scope open question lists app shell,
  layouts, toast/confirm, filter/query-builder, description list).
- **Client observability absent from ARCH** — §Observability is server-only; no client telemetry
  transport, error logging, or trace-id UX is decided.
- **No client storage abstraction** — `TmClientCache.clearAll()` covers CacheStorage only, though its
  doc promises IndexedDB/web-storage; warm-launch caches need a store and a sweep that fits it.
- **`TM_BLOB_FETCHER` is not cancellable** — no `AbortSignal`, against brief rule 2C ("every
  readonly request is cancellable").
- **ar-SA formatting is not platform plumbing** — `TM_ACTIVE_LOCALE` defaults to the bare language
  tag; the showcase hand-maps `ar` → `ar-SA`; the brief's reference distro needs it as config, not
  code.
- **No Transloco loader** — `provideTellmaUi()` sets no `TranslocoLoader`; app strings and the server
  string pack (0013) have no fetch/cache path, and warm launch needs them cached.
- **"System" theme needs logic** — dark mode is `[data-theme=dark]` only; no `prefers-color-scheme`
  handling or persisted theme preference exists.
- **No distribution token-delta pipeline** — ARCH says deltas are emitted at build into a stylesheet
  baked into `index.html`; only `tellma-default.css` is emitted, into the tokens package.
- **Docs/MCP coverage stops at `@tellma/core-ui`** — new packages' components are invisible to
  `components.json`, `llms.txt`, MCP unless the extractor is generalised.
- **Lint scoping is area-specific** — `tm/prefix-exports` and stylelint rules cover `projects/core/**`
  and `projects/locale/**` only; a `module/` or `apps/` area is unguarded, and an in-workspace app
  would trip the `tm` selector rule.
- **E2E harness is showcase-only** — `SHOWCASE_PORT`, `storyUrl`, `/story/:id`; no Playwright setup
  for an authenticated app, a backend, multiple tabs (cross-tab sync), offline/SW, or Lighthouse.
- **No UI version identity** — packages are all `0.1.0`; no build-time commit-hash stamping exists
  (brief Supportability 1, 4).
- **Font preloads for Arabic-first distros are a workspace script** — `inject-font-preloads.mjs`
  (`… arabic`) lives in `client/scripts`, not in a shipped package, so a distro in its own repo
  cannot run it (ARCH §Logic vs. configuration says dev tooling is logic that ships in packages).
- **npm-side abstractions rule is undefined** — whether pack npm packages may depend on
  `@tellma/core` / `@tellma/core-ui` directly (C# rule 2 forbids Module → Core) is unstated.
- **No Windows client CI** — AGENTS.md requires Windows+Linux; the `client` job is Ubuntu-only.

## 6. Likely change points

- **ARCH §Frontend** (opening paragraphs): add the PWA/SW decision (cache scope, update check,
  manifest, Lighthouse), what `@tellma/core` contains versus `@tellma/core-ui`, the client state/
  warm-launch cache, the HTTP layer (cancellation, contract fingerprints), and the client shell.
- **ARCH §Frontend → UI component library**: the package table (any new `@tellma/*` client package,
  e.g. a shell package or `@tellma/core`), the stale `TmDateAdapter`/`TmNumberAdapter`/
  `TmCurrencyAdapter` sentence, the "engine in its own package" claim, MCP tool names, `tm-icon`
  arrival, toast/confirm services.
- **ARCH §Reports — three tiers**: dashboards leave tier 3 (they are built from tier-1 ad-hoc
  reports); add report definitions (stored JSON, kinds, sharing/visibility) and the
  hard-coded-report escape hatch (UI + API, a tier-2-plus concept).
- **ARCH §Guiding Principles → Performance**: reconcile "optimistic UI for mutations / local commit
  before server reconciliation" and "never … a cold-spinner on a return visit" with the brief's
  full-page save spinner and instant data-loading state; name the 7-day warm-launch window.
- **ARCH §Observability**: add client telemetry (transport to App Insights, UI error logging,
  trace-id exposure, version identity), keeping the names-as-constants and cardinality rules.
- **ARCH §Platform repo layout**: `client/projects/` additions (`core/tellma-core`, a
  `module/tellma-module-gl` for Centers, possibly a shell package), and the actual Phase-1 client
  location `distributions/acme/src/Tellma.Distro.Acme.Client`.
- **ARCH §Distribution repo layout**: remove the duplicate client location (`client/projects/app/`
  vs `src/Tellma.Distro.<Slug>.Client/`) and state what a distro client contains (round 2's "what a
  distribution writes", 0024).
- **ARCH §Rollout & Phasing**: replace "TypeScript path mappings" with a mechanism that avoids two
  copies of Angular (e.g. adding the Acme app to the `client/` workspace or a pnpm workspace that
  spans both).
- **ARCH §Hosting on Azure** / **§Open Questions → Edge ingress**: static-asset caching headers and
  SW cache-busting; how N/N−1 web apps coexist with a cached bundle (links to 0016 §3.10).
- **ARCH §CI/CD & Quality Gates**: client gates for the distro app (bundle budget, Lighthouse PWA,
  e2e against a real host, multi-tab tests), Windows client job.
- **ARCH §Glossary**: new terms (app shell, launcher, pinned bar, assignment, report definition,
  dashboard, page context) and the "Stack … Angular page" wording.
- **ARCH §Open Questions → UI component-library v1 scope**: resolve as round 2 picks components.
- **ARCH §Library architecture → Package naming**: an npm naming rule for pack client packages and
  their dependency direction.
- **ARCH §Identity**: sign-out from every tab and cache clearing, the tenantless shell/tenant picker
  (if ARCH is to record it).
- **Workspace files** (implementation, not spec): `angular.json` (app project, budgets,
  `serviceWorker`), `package.json` deps, `eslint.config.mjs` scoping, `tools/docs/
  extract-components.mts` scope, `tools/workspace.mjs` (apps under `projects/apps/` have no
  `package.json`), `pnpm-workspace.yaml`, `e2e/playwright.config.ts`.

## 7. Contradictions and surprises

1. **ARCH's distribution repo layout has two client locations** — `client/projects/app/` (an Angular
   workspace at repo root) and `src/Tellma.Distro.<Slug>.Client/` (an esproj with its own
   workspace). Spec 0011 §1.2 and the brief use only the esproj. A designer copying ARCH could put
   the app in the wrong place.
2. **Phase-1 "TypeScript path mappings" into `client/projects/*` from a separate workspace** at
   `distributions/acme/src/Tellma.Distro.Acme.Client` would resolve `@angular/*`, `@jsverse/*` and
   `rxjs` from two different `node_modules` trees (bare imports resolve from the importing file's
   location), giving two Angular/Transloco instances and broken DI identity. The workspace itself
   avoids this only because the showcase lives inside `client/`. The mechanism needs an explicit
   decision (0024).
3. **Post-build `index.html` rewriting conflicts with the Angular service worker.**
   `inject-font-preloads.mjs` edits the built `index.html` after `ng build`; `ngsw.json` records
   file hashes at build time, so a stale hash for `index.html` makes the SW reject the version
   unless `ngsw-config` is re-run after the injection (or preloads move into the build).
4. **`TmClientCache.clearAll()` deletes every CacheStorage cache named `tm-*` on logout.** A
   platform SW or app-shell cache named with the `tm-` prefix would be wiped by sign-out; the
   convention also promises IndexedDB/web-storage coverage the code does not have.
5. **The private API is POST-only** (`/{tenantId}/api/web/{resource}/{operation}`; the blob GET is
   the only GET). Angular SW `dataGroups` cache GETs only, so warm-launch data and definition
   caching must be app-managed (IndexedDB/localStorage), not SW-managed.
6. **ARCH tier 3 lumps dashboards with BI** ("Separate analytical store … Out of scope … designed
   when needed"); the brief's dashboards are grids of tier-1 ad-hoc reports on the OLTP database.
   ARCH must change.
7. **ARCH performance rules conflict with the brief**: "Optimistic UI for mutations … local commit
   before server reconciliation" versus the brief's blocking full-page spinner on save; "never …
   a cold-spinner on a return visit" versus the brief's accepted instant loading state for page
   data.
8. **ARCH's i18n paragraph is stale**: `TmDateAdapter` / `TmNumberAdapter` / `TmCurrencyAdapter` do
   not exist (the built seam is the `TmL10n` facade + `TM_ACTIVE_LOCALE` + `TM_CALENDAR`), and
   "a Hijri calendar provided by a Locale pack" contradicts ARCH's own Locale paragraph ("calendars
   are Core's") and the built `@tellma/core-ui/calendar-umalqura` entry point.
9. **Smaller ARCH-vs-workspace drift**: the grid engine "would ship in its own package" but is the
   `@tellma/core-ui/grid-engine` entry point; the MCP has `list`/`describe`/`example`, not
   `generate_theme`/`validate_theme`; the gate is `api:check`/`api:approve`, not `approve-api`;
   ARCH says `npm start`/`npm install`, the workspace is pnpm 11; ARCH says `@deprecated` +
   `@breaking-change` is enforced and prettier is part of lint, but neither is gated.
10. **Centers is a GL-module entity** (`Center : ActivatableTreeEntity` in
    `Tellma.Module.Gl.Abstractions`). Under ARCH's package rules its pages belong to a GL client
    package (e.g. `@tellma/module-gl`), not to Core with Users/Roles, which the brief and the
    0032 scope line group together.
11. **Ports**: `client/README.md` forbids literal ports, but 0011 fixes
    `Tellma:PublicOrigin = https://localhost:4200` and ARCH Phase 1 tracks fixed ports in
    `launchSettings.json`. The distro client's dev server port has to reconcile the two.
12. **The ESLint selector rule applies `tm` to every `projects/**/*.ts` except `internal/`.** A
    distribution app placed inside `client/projects/` (e.g. under `apps/`) needs its own override,
    and pack packages inherit `tm-` selectors unless round 2 decides otherwise.
13. **`tools/workspace.mjs` assumes exactly `projects/<area>/<name>`, a `tellma-` folder prefix, and
    angular.json key = folder minus `tellma-`**; "Apps carry no package.json", so an app is never
    built by `pnpm run build` or budgeted by `size:check`.
14. **The brief's ar-SA requirement is not met by defaults**: bare `ar` formats with Latin digits;
    only the showcase's hand-written provider produces Arabic-Indic digits.
15. **ARCH lists many repo items that do not exist** (`taxonomy.json`, `changelog.d/`, `release.yml`,
    `templates/tellma-distribution/`, `Tellma.Cli`, `build/Tellma.Core.targets`, every
    module/industry/compliance client package, the landing SPA) — and omits ones that do
    (`Tellma.Identity.Migrations`, `eng/identity-ui-inspection`, `samples/`, `test/shared/*`, the
    showcase). Designs should cite what exists, not the ARCH tree.
16. **`@tellma/core` is overloaded in ARCH**: the client runtime ("cross-cutting services &
    helpers"), the dev-tool `bin` scripts (port reading for `ng serve`), and the agent `docs/`
    channel. Putting dev tooling in a runtime package the SPA imports is a packaging smell to
    resolve.
17. **Slot-swap rollouts run N and N−1 web apps side by side** (ARCH §CI/CD, §Migrations): a cached
    SPA bundle may face either version for minutes, which the contract-fingerprint mechanism
    (0016 §3.10, cited by the brief) must cover; the SW update check alone does not.
18. **The identity server already consumes the design tokens and fonts** (committed copies in
    `src/apps/Tellma.Identity/wwwroot`, drift-gated by `tokens:check`), so token changes in round 2
    ripple into the identity UI's committed assets.
19. **Specs 0011–0022 are unimplemented and `Tellma.Core` is empty**, so every server contract the
    SPA consumes can still change freely (per the round-2 README), but nothing can be verified
    against running code.

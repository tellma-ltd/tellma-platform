# Spec: Client App Foundation

- **Author:** Ahmad Akra
- **Date:** 20 October 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

Every Tellma distribution ships an Angular single-page app (SPA). This spec ships the framework
under it: the `@tellma/core` packages, build tool and declaration API (§1, §2), the client metadata
(§3), the boot script (§4), the service worker and updates (§5), SPA hosting (§6), localization and
theme (§7), the browser session (§8), routes (§9), the HTTP layer (§10) and error presentation
(§11). It builds on specs 0002, 0011, 0013 and 0016; specs 0025–0035 build on it.

Distribution code must stay small and mechanical, because coding agents write it at scale. A
distribution scaffolds about 110 lines once, then only declares data; routes, launcher entries,
deep links, pickers, page contexts and telemetry are derived. A warm launch renders the shell and
page chrome with no network wait, and every abandoned read is cancelled.

Other specs that carry parts of this design:

- spec 0010 §A.9, §A.10, §B.2, §B.3, §D.4–§D.6 — the account URL and sign-in language, `TmTheme`,
  the translations seam, live `Directionality`, `TM_TIME_ZONE`, `clearAll({ keep })`, the fetcher;
- spec 0011 §5.1–§5.5, §5.10, §6.1 — SPA endpoints and audit, BFF and profile cookie v2,
  `ITellmaLinks`, `emit-client-assets`;
- spec 0012 §6.1, spec 0015 §14.1 — a client abort cancels reads; a sent persist completes;
- spec 0013 §1.2, §10.4, spec 0014 §10, spec 0020 §3.7, spec 0021 §2.5 — metadata sections and
  string packs;
- spec 0013 §7.2, §9.2, spec 0018 §3.5 — regions, digits and the `users/me/locale` write;
- spec 0015 §2.3, §14.1 — the `Long` and `Idempotent` flags and the `forbidden` reasons;
- spec 0016 §2.2, §3.2, §3.10, §6, §7.5 — retry classes, decimals as strings, fingerprints,
  headers and problem codes;
- spec 0017 §8.1 — the upload route; spec 0018 §1.4 — Acme's server feature.

## Goals / Non-goals

**Goals**

- A distribution client is about 110 scaffolded lines plus one declaration per entity, page and
  report: no route file, interceptor, guard, store, proxy or worker configuration.
- Deployment facts ship in the bundle; no initializer, guard or resolver waits on the network.
- One HTTP layer: reads are cancellable and retried by class, writes are never aborted, decimals
  stay exact, and a user check wraps every call.
- One error table for inline states and the shared dialogs.
- The browser side of sign-in, step-up, sign-out of every tab, expiry and a changed user.
- Formats that follow each user's language, region and digits, and date-times that say their zone
  when it is not the device's.
- An installable PWA whose updates never interrupt; one route grammar; Node tooling that runs on
  Windows and Linux.

**Non-goals (explicitly out of scope)**

- Client state, tab bus, leader tab, hub, preferences and drafts: spec 0025.
- Telemetry, request log, page context and support: spec 0026.
- Shells, tenant picker, launcher, session menu and shortcuts: spec 0027.
- Banners and toasts: spec 0028. The entity model: spec 0029. Search and details: specs 0030, 0031.
- Generic components: spec 0023. Breakpoints, `TmTheme`, live direction and identity changes:
  spec 0010.
- Offline writes: a write needs the network, and drafts keep the work (spec 0025 §11).
- Scaffolding: a later distribution-tooling spec owns the `dotnet new tellma-distribution`
  template, which writes server and client together, and its parity test. §2.1 lists the client
  files a distribution owns.
- A server switch that removes a pack's entity, widget type or coded report from a distribution: a
  later spec. Until then, permissions and `hide: ['entry:…']` cover the need (§2.3).
- Web Push: a later spec, which wraps Angular's worker (§5.4).

## 1. Placement

This spec ships three packages and the distribution app project. `@tellma/core` splits into entry
points along the lazy-loading lines, and one import rule keeps lazy code out of the eager bundle.

Contract blocks are TypeScript and C# sketches: names and shapes are normative; imports,
documentation comments, bodies and cancellation tokens are omitted.

| Package | Folder | Holds |
|---|---|---|
| `@tellma/core` | `client/projects/core/tellma-core/` | The app framework. Peers: `@angular/{core,common,router,platform-browser,service-worker}`, `@angular/cdk`, `@jsverse/transloco`, `@tellma/core-ui`, `rxjs`. |
| `@tellma/core-testing` | `client/projects/core/tellma-core-testing/` | Test providers, fakes and Playwright helpers (below) |
| `@tellma/core-tools` | `client/projects/core/tellma-core-tools/` | Node ESM, bin `tellma-client` (§2.6); presets `/eslint`, `/tsconfig.json`, `/stylelint` (with spec 0010 §B.1's breakpoint rule) |
| The app | `distributions/<slug>/src/Tellma.Distro.<Slug>.Client/` | A member of the repo-root pnpm workspace: one Angular instance for platform and app |

`@tellma/core-testing` holds `provideTellmaTesting()` (a fake `TmApi`, a fake clock in place of
`TM_NOW`, an in-memory store, test metadata), `TmFakeApi` and `tmTestMetadata()`. Its Node entry
point `/e2e`, with the optional peer `@playwright/test`, holds `warmUp`, `holdNetwork`, `goOffline`,
`openTabs`, `signInAs`, `fakeProblem` and `expectNoAxeViolations`.

| `@tellma/core` entry point | Holds (owner) | Loaded |
|---|---|---|
| primary | this spec's API; `tmEntity` (0029), `tmCodedReport` (0034), `tmWidget` (0035), `TmTileDeclaration`, `TmCustomFilterDeclaration`, type-only `TmSearchSnapshot` (0030); the primary services of 0025–0029 | eager |
| `/shell`, `/attention` | shells and app bar (0027); bell, tray, toast policy (0028) | eager, modulepreloaded |
| `/realtime` | `TmHub` (0025) | lazy, by the leader tab |
| `/support` | dump, screenshot, support dialog (0026) | lazy, on demand |
| `/entity` | descriptor data, client Queryex, editors, filter controls, picker binding, `TmEntityActions` (0029) | lazy, shared |
| `/search`, `/details` | the search and details hosts (0030, 0031) | lazy routes |
| `/admin`, `/attention-pages` | stock pages (0032); attention pages (0028) | lazy routes |
| `/reports`, `/report-studio`, `/dashboards` | report pages, studio (0034); dashboards, home (0035) | lazy routes |
| `/private` | shared unstable wiring; no API goldens; never for app code | — |

Import rules:

- **Static imports follow one graph**, always as `@tellma/core/<ep>`, never `../`: the primary
  imports no other entry point, `/attention` only the primary, `/shell` the primary and
  `/attention`. No eager entry point imports a lazy one. An ESLint boundary rule and the package
  build enforce it; the build also refuses cycles.
- **Dynamic imports may cross the graph:** route loaders, declaration members (`details`, `search`,
  `component`, `home`), the picker binding's pages, the `/realtime` loader and the time-zone
  dialog (§7.8). `import type` crosses any boundary. `/shell` and `/attention` load through
  `import()` with `modulepreload` links (§2.6).
- **Packs:** a pack's primary entry point holds declarations only; layouts ship in secondary entry
  points (`@tellma/module-gl/centers`).

**Vocabulary**

- **Declaration** — a value a feature lists: an entity, page, coded report, widget type, launcher
  section or entry, settings tab or app-bar slot. **Feature** — one `tmFeature` value (§2.2).
- **Pack** — a reusable package of features, such as `@tellma/module-gl`. **String pack** — one
  language's server strings, `strings-<lang>-<h>.json` (§3.2; 0013 §10.4).
- **Client metadata** — the hashed JSON chunks the build emits from the server composition (§3).
- **Boot record** — spec 0025's `localStorage['tm.boot']`, read and patched here as `boot` (0025
  §2.1). **Profile cookie** — `tellma.profile`, the script-readable mirror of the session (§8.1).
- **Launch documents** — the tenant documents the chrome renders from, such as `me` and `settings`
  (0025 §3).
- **Warm** — the worker holds the running version's `shell` group, the cookie's `sub` equals the
  boot record's, and the tenant's launch documents are current (0025 §3.2); else **cold**.
- **Read**, **write** — the two `TmApi` call kinds: a read is cancellable and retried, a write
  neither (§10.1). **Route host** — `TmRouteHost` (§4.4). **Error view** — `TmErrorView` (§11).
- **Automatic entry** — a tenant entry the platform chose, not the user: the launch redirect, the
  BFF's landing or an expired session's return (§9.3).
- **Display name** — the localized display name, written `{displayName}` in copy (§3.1).
- **Region**, **digits** — the formats axis beside the language (§7.7). **Device zone** — the
  browser's resolved time zone (§7.8).

## 2. What a distribution writes

A distribution writes data: one feature listing its declarations, plus scaffolded boilerplate.
Everything else is derived, and each uncommon need has a named escape hatch (§2.5).

### 2.1 File tree and scaffold

The table lists the client files a distribution owns. A distribution template scaffolds them once,
beside the server projects (Non-goals). Acme's files are the reference.

| Path (in `distributions/acme/src/Tellma.Distro.Acme.Client/`) | Scaffolded content |
|---|---|
| `public/` | `manifest.webmanifest` (§5.1), `icons/`, optional `brand/` overrides (§7.6) |
| `src/` | `index.html` (§4.1), `main.ts`, `app.config.ts` (§2.2), `styles.css`, `app/acme.feature.ts` (§2.3) and the layouts |
| `generated/` | gitignored; written by the emit (§3.2) |
| `main.ts` | `bootstrapTellma(appConfig)` |
| `styles.css` | imports of `@tellma/core/styles.css` and `@tellma/locale-ar/styles.css`, then Acme's one delta `@layer tm.theme { :root { --color-primary: var(--teal-700); } }` |
| `package.json` | scripts `start: tellma-client serve`, `build: tellma-client build`, `test: ng test`, `lint: eslint . && stylelint "src/**/*.css"`; `"tellma": { "migrator": "../Tellma.Distro.Acme.Migrator" }` |
| `tsconfig.json` | extends `@tellma/core-tools/tsconfig.json` (strict, strict templates) |
| absent on purpose | `app.routes.ts`, `proxy.conf.js`, `aspnetcore-https.js`, `ngsw-config.json`, `i18n/*.json`, interceptors, guards, stores |

- **`angular.json`** has one application on `@angular/build:application`, prefix `acme`: assets
  `public/**` and `generated/assets/**` (output `tm-metadata/`), `serviceWorker: false` (§5.3),
  `outputHashing: all`, a `test` target on `@angular/build:unit-test`, and no budgets (§12).
- **The esproj** uses `Microsoft.VisualStudio.JavaScript.Sdk`, with `BuildCommand`
  `pnpm run build` and `BuildOutputFolder` `dist/acme/browser`. Its `ProjectReference` to the
  Migrator sets `ReferenceOutputAssembly=false`, so the build runs with `--no-dotnet-build` (spec
  0011 §1.2).
- **`eslint.config.mjs`** re-exports `@tellma/core-tools/eslint`. Its distribution rules are
  `tm/no-raw-http` (no `fetch`, `XMLHttpRequest` or `HttpClient`), `tm/no-web-storage`,
  `tm/lazy-layouts`, `tm/literal-declaration-keys`, `tm/no-decimal-operators`, the slug as
  selector prefix and typescript-eslint's `restrict-plus-operands` (§10.4).
  `tm/lazy-layouts` requires `details`, `page`, `create`, `search`, `component`, `display`,
  `dialog`, `home` and `settingsEditor` to be `() => import(…)`.

### 2.2 The distribution API

`provideTellma` takes the metadata loaders, the languages and the features; `bootstrapTellma`
starts the app.

```ts
interface TmAppConfig {
  metadata: TmMetadataLoaders;                   // generated/tellma-metadata.ts (§3.2)
  languages: readonly TmLanguageDefinition[];    // first = the distribution default
  features: readonly TmFeature[];                // packs first, the distribution last
  calendars?: readonly TmCalendarDefinition[];   // only a calendar the distribution registered on the server
}
interface TmLanguageDefinition {
  code: string; dir: 'ltr' | 'rtl';                          // 'ar', 'rtl'; the server owns the region (§7.7)
  strings: () => Promise<TmStringTable>;                     // the client tables (§7.3)
}
interface TmStringTable { readonly tm: TmStringTree; readonly tmUi: TmStringTree }  // one language's client scopes
type TmStringTree = { readonly [key: string]: string | TmStringTree };
interface TmCalendarDefinition { code: string; labelKey: string; load: () => Promise<TmCalendar> } // @tellma/core-ui/l10n
declare function tmEnglish(): TmLanguageDefinition;          // 'en'
declare function provideTellma(config: TmAppConfig): EnvironmentProviders;
declare function bootstrapTellma(appConfig: ApplicationConfig): Promise<ApplicationRef>;

interface TmFeatureDefinition {
  key: string;                                         // 'gl', 'acme': the server feature's name
  sections?: readonly TmNavSection[];                  // launcher sections (0027 §3.1)
  entries?: readonly TmNavEntry[];                     // entries beyond entities, pages, reports (0027 §3.1)
  entities?: readonly TmEntityDeclaration[];           // 0029 §3
  pages?: readonly TmPageDeclaration[];                // §2.4
  reports?: readonly TmCodedReportDeclaration[];       // 0034 §2.2
  widgets?: readonly TmWidgetTypeDeclaration[];        // 0035 §4.3
  settingsTabs?: readonly TmSettingsTabDeclaration[];  // 0032 §7.5
  appBar?: readonly TmShellSlot[];                     // 0027 §7.2
  icons?: Readonly<Record<string, TmIconSource>>;      // added to 0023's TmIconRegistry
  overrides?: readonly TmDeclarationPatch[];           // patches of earlier keys (§2.3)
  hide?: readonly TmDeclarationKey[];                  // earlier keys to drop (§2.3)
  home?: TmLazyComponent;                              // 0035 §2.3
  providers?: readonly (Provider | EnvironmentProviders)[]; // the "anything Angular" hatch
}
type TmDeclarationKey = `${'page' | 'section' | 'entry' | 'settingsTab' | 'appBar'}:${string}`; // client-only kinds (§2.3)
type TmDeclarationPatch = TmEntityDeclaration | TmPageDeclaration | TmCodedReportDeclaration | TmWidgetTypeDeclaration | TmOverride;
declare function tmFeature(definition: TmFeatureDefinition): TmFeature;
declare function tmOverride(key: TmDeclarationKey, members: Readonly<Record<string, unknown>>): TmOverride; // members replace the key's
type TmLazyComponent = () => Promise<Type<unknown> | { default: Type<unknown> }>;
type TmSecurableRef = readonly [resource: string, action: string];
declare const TM_APP_DECLARATIONS: InjectionToken<TmAppDeclarations>; // the merged declarations of every kind
interface TmAppDeclarations {                    // merged, hide-applied (§2.3)
  entities: ReadonlyMap<string, TmEntityDeclaration>; pages: readonly TmPageDeclaration[];
  reports: readonly TmCodedReportDeclaration[]; widgets: readonly TmWidgetTypeDeclaration[];
  sections: readonly TmNavSection[]; entries: readonly TmNavEntry[];
  settingsTabs: readonly TmSettingsTabDeclaration[]; appBar: readonly TmShellSlot[];
  features: readonly string[];                   // the feature keys, core first
  home: TmLazyComponent | null;                  // the winning home
}
```

- **`TM_APP_DECLARATIONS`** is the one token through which shells and hosts read declarations.
- **`bootstrapTellma`** calls `bootstrapApplication(TmApp, …)` with zoneless change detection,
  `provideBrowserGlobalErrorListeners()` and `appConfig.providers`. It never settles while
  `tm-boot.js` redirects or shows the boot page (§4.3).
- **`provideTellma`** provides:
  - the derived router (§9.2);
  - `provideTellmaUi` with the translation loader (§7.3) and the theme seed (§7.6);
  - `TM_ACTIVE_LOCALE`, `TM_CALENDAR`, `TM_TIME_ZONE` and `TM_DEVICE_TIME_ZONE` (§7.1),
    `TM_NOW` (`Date.now`), `TM_BLOB_FETCHER` (§10.5), `TM_CLIENT_METADATA` and
    `TM_APP_DECLARATIONS`;
  - `TmErrorHandler` as Angular's `ErrorHandler` (0026 §4.1);
  - `provideTellmaToasts({ maxVisible: 3, overflow })`, with spec 0028 §8.1's overflow action;
  - the platform's icon names and each feature's `icons`;
  - the boot initializers (§4.3) and the worker (§5.4);
  - each feature's `providers`, in order, then `TmTitleStrategy` (§4.4).
- **The "anything Angular" hatch:** providers listed after `provideTellma(…)` win.
- **Configuration problems:** every rule of §2.2–§2.4 and §3.5 has a code in Appendix A, checked
  by §3.6.

**Example —** Acme's whole `app.config.ts` is one call:

```ts
provideTellma({
  metadata: tellmaMetadata,
  languages: [tmEnglish(), tmArabic()],
  features: [tmGlFeature(), acmeFeature],
})
```

### 2.3 The merge rule

Features merge in order, and each declaration has one key. A later feature changes an earlier key
only through `overrides` and `hide`. Every violation is a configuration problem (Appendix A).

- **Order.** The implicit `core` feature comes first, then `features` in array order. `core` holds
  the stock pages and sections of specs 0026, 0028, 0032, 0034 and 0035.
- **Keys.** An entity is keyed by name, a page by path, a coded report or widget type by key, a
  section or entry by id, a settings tab by category and an app-bar slot by id. The launcher entry
  derived from an entity, page or report takes its key as id (`entry:acme.Customer`). Adding an
  existing key is `duplicate-key`.
- **Overrides** patch an earlier key member by member. A member the patch sets replaces the
  inherited one whole (arrays are replaced, never concatenated); an unset member inherits. A
  `tmEntity`, `tmPage`, `tmCodedReport` or `tmWidget` value patches its own key; `tmOverride`
  patches a section, entry, settings tab or app-bar slot. Patching a missing key is
  `dangling-override`.
- **Hide** drops a client declaration by kind. `entry:` drops only the launcher entry, so the pages
  stay reachable. `section:` drops a section and its entries from the launcher. `page:`,
  `settingsTab:` and `appBar:` drop the declaration. Hiding a missing key is `dangling-hide`.
- **Server facts are not hidden.** The server decides which entities, widget types and coded
  reports exist, and the client follows the metadata (§9.2). To keep an entity from users, a
  distribution grants no role its permissions (spec 0014). To drop only its launcher entry, it
  hides `entry:`.
- **`home` and `appBar`.** The last feature that sets `home` wins. All features together have at
  most three app-bar slots, and `help` and `assistant` are reserved ids (0027 §7.2).

**Example —** in `acmeFeature`:

- `hide: ['entry:core.Role']` removes the Roles launcher entry, for a distribution with one fixed
  role set. `/{t}/roles` still opens.
- `overrides: [centersOverride, tmOverride('section:administration', { order: 10 })]` patches the
  Centers declaration (spec 0032 §6). It also moves the Administration section (order 900, spec
  0027 §3.2) to the front of the launcher.

### 2.4 `tmPage`

`tmPage` declares a page with no entity behind it. The platform gives it a route, a launcher entry,
a title, a page context and the permission gate.

```ts
declare function tmPage(path: string, page: {
  component: TmLazyComponent; titleKey: string;
  section?: string; order?: number; icon?: string; synonymsKey?: string; // launcher members (0027 §3.1)
  securable?: TmSecurableRef;                                             // the permission gate
}): TmPageDeclaration;
```

- **`path`** is kebab-case segments, each matching `^[a-z][a-z0-9]*(-[a-z0-9]+)*$`, optionally
  ending in `:name` parameters (`credit-review/:customerId`). The first segment is no reserved
  word (§9.1) or `new`, and is an entity segment only when the same feature declares that entity
  (§9.2). A page with parameters takes no `section`, since its launcher entry would have no URL.
- **The route** is `/{t}/{path}`, with skeleton `blank` and page kind `custom` (§4.4). Parameters
  reach the component as inputs.
- **`securable`.** When `TmPermissions.can(resource, action)` (0029 §7) is false, the route renders
  the `forbidden` error view in place, with reason `no-grant` and the declared pair (§11). It
  never redirects. On a cold entry, the route host checks it only after the wait for the launch
  documents (§4.4).

**Example —** Acme's `tmPage('credit-review', { component, titleKey, section: 'financials' })`
gives the route `/17/credit-review` and a launcher entry under Financials.

### 2.5 Escape hatches owned here

| Need | Hatch |
|---|---|
| A page with no entity | `tmPage(path, …)` (§2.4) |
| A hand-mapped endpoint | `inject(TmApi).read(…)` or `.write(…)` (§10.1) |
| A calendar the platform lacks | `calendars: [{ code, labelKey, load }]` in `provideTellma` |
| Relabel, reorder or hide a platform item | `overrides` with `tmOverride`, and `hide` (§2.3) |
| Branding | the manifest, `public/icons/`, `public/brand/`, `@layer tm.theme`, the `Distribution_*` server keys (§3.1, §5.1, §7.6) |
| Anything Angular | `providers` on `tmFeature`, or providers after `provideTellma` |

The other hatches belong to their members' owners: 0027–0032, 0034 and 0035, each in its §2.

### 2.6 `tellma-client` commands

Three Node commands cover the developer loop and the build. The exit code is 0 for success, 1 for
a usage error, 2 for a build failure and 3 for a build problem (Appendix A). Each problem prints as
one `code key feature` line, where `key` is the declaration or the file concerned.

| Command | Does |
|---|---|
| `emit [--configuration c] [--no-dotnet-build]` | Runs the Migrator's `emit-client-assets generated` (spec 0011 §6.1). |
| `serve [--port n]` | Runs the development server (below). |
| `build [--no-dotnet-build]` | Runs the production build and its checks (below). |

**`serve`** runs `emit` and exports the ASP.NET Core development certificate. It serves on
`<SLUG>_CLIENT` from `.dev-ports.local`, else 4200, and proxies the server words of
`metadata.routing`, websockets included, to `<SLUG>_WEB`, else 7052. It sets no CSP and registers
no worker. In Acme, it first runs the platform's generated-asset steps (`tokens:build-css`).

**`build`** runs these steps in order:

1. `emit`; then the source checks, which read only sources: the config check (§3.6), the
   `index.html` rules (§4.1), the manifest (§5.1) and the brand files (§7.6); then
   `ng build --stats-json`;
2. copies the brand files (§7.6), writes `tm-boot.js` (§4.2) and adds the localized names to the
   manifest (§5.1);
3. injects preloads: fonts for Latin and each offered language's script, from the fragment map
   that moves from `client/scripts/inject-font-preloads.mjs` into `@tellma/core-tools`;
   `<link rel="preload" as="fetch" crossorigin>` for the Core chunk and the default language's
   string pack; `<link rel="modulepreload">` for `/shell`, `/attention` and their static imports;
4. writes the worker config (§5.3);
5. moves the hidden source maps to `dist/<slug>/sourcemaps/`, outside the deployable output; CI
   archives them per UI version (0026 §4);
6. runs the output checks, which read the output: the budgets (§12) and hashed names (§6).

A problem in step 1 stops the build before `ng build`, so a source mistake fails in seconds.

## 3. Client metadata

The client metadata are the deployment facts the SPA needs before any request. The Migrator emits
them at build time as hashed JSON chunks. This spec owns the container, the emit and the checks;
each section's shape belongs to its domain spec.

### 3.1 The container

```ts
interface TmClientMetadata {                                // the Core chunk
  formatVersion: 1;
  distribution: { slug: string; displayName: string; copyright: string | null; version: string; platformVersion: string };
  features: readonly { key: string; version: string }[];    // composition order
  languages: readonly TmLanguageMetadata[];                 // 0013 §1.2; [0] is the default
  calendars: readonly string[];                             // 0013 §1.2; [0] is the default; 'gc', 'uq', 'et'
  entities: readonly TmEntitySummary[];                     // 0029 §2.2
  securables: readonly TmSecurableMetadata[];               // 0014 §10
  settingKeys: readonly TmSettingKeyMetadata[]; settingCategories: readonly TmSettingCategoryMetadata[]; // 0013 §5.3
  notificationTypes: readonly TmNotificationTypeMetadata[]; // 0021 §2.5, with the next two
  notificationChannels: readonly { key: string; defaultEnabled: boolean }[]; clientEvents: readonly string[];
  jobHandlers: readonly TmJobHandlerMetadata[];             // 0020 §3.7
  widgetTypes: readonly string[];                           // 0035 §4.1
  routing: {                                                // TellmaRoutes (§3.2)
    serverFirstSegments: readonly string[]; serverTenantSegments: readonly string[];
    spaFirstSegments: readonly string[]; spaTenantSegments: readonly string[];
  };
}
interface TmLanguageMetadata {                              // one offered language (0013 §1.2) and its formats (§7.7)
  readonly code: string; readonly nativeName: string; readonly dir: 'ltr' | 'rtl';
  readonly region: string;                                  // the language's default region: 'SA'
  readonly regions: readonly string[];                      // the regions offered with it, ordinal order
  readonly nativeDigits: string | null;                     // 'arab'; null when its digits are Latin
}
interface TmMetadataLoaders {                               // the emitted module
  core: () => Promise<TmClientMetadata>;
  entities: () => Promise<readonly TmEntityMetadata[]>;     // 0029 §2.2
  contracts: () => Promise<TmContracts>;                    // §3.4
  strings: Readonly<Record<string, () => Promise<Readonly<Record<string, string>>>>>; // one string pack per language
}
interface TmContracts {                                     // the Contracts chunk (spec 0016 §3.10)
  fingerprints: Readonly<Record<string, string>>;           // route template → fingerprint
  long: readonly string[];                                  // route templates the server runs under tellma-long
  idempotent: readonly string[];                            // POST route templates in the retried class
}
declare const TM_CLIENT_METADATA: InjectionToken<TmClientMetadata>;
```

- **`distribution.version`** is the UI version (0026 §7): the composition library's informational
  version, `X.Y.Z+<sha>`. Each distribution has one `Version`, in
  `distributions/<slug>/Directory.Build.props`, which makes the Web host's `Tellma-Build` equal it.
- **`platformVersion`** is `Tellma.Core`'s version; each `features[].version` is its feature
  assembly's.
- **`displayName` and `copyright`** come from `Tellma:DisplayName` and `Tellma:Copyright` in the
  Migrator's `appsettings.json` (spec 0011 §8). Four optional server keys in the distribution's
  string packs localize the brand:
  - `Distribution_DisplayName`, the name users see (the localized display name, below);
  - `Distribution_ShortName`, the installed app's short name, falling back to the manifest's
    `short_name` (§5.1);
  - `Distribution_Copyright`, the About dialog's copyright line, in the active language, else
    `copyright`;
  - `Distribution_About`, the About dialog's description, with no fallback (spec 0027 §11.1).
- **The localized display name** is `Distribution_DisplayName` in the active language, else
  `displayName`. `TmLocalization.displayName` (§7.1) holds it, and every user-facing string of the
  platform says `{displayName}`, never "Tellma". `DistributionInfo` (spec 0011 §5.9) is the
  runtime source for anonymous callers.
- **Every section is tenant-independent.** `TmEntities` gates the entity sections by the tenant's
  shape (0029 §4.2).

### 3.2 The emit command (C#)

```csharp
// Tellma.Core.Abstractions.Client — each section owner implements one
public interface IClientAssetContributor { void Contribute(ClientAssetContext context); }

public sealed class ClientAssetContext
{
    public IServiceProvider Services { get; }        // the realised composition; no I/O
    public void Section(ClientMetadataChunk chunk, string name, Action<Utf8JsonWriter> write); // one member of a chunk
    public void File(string relativePath, string content);  // generated code under generated/ (0029 §15)
}

public enum ClientMetadataChunk { Core, Entities, Contracts }

// Tellma.Core.Abstractions.Composition — the one source of reserved route words (§9.1)
public static class TellmaRoutes
{
    public static IReadOnlyList<string> ServerFirstSegments { get; }   // api, bff, health, id, mcp, openapi, signin-oidc, signout-callback-oidc, .well-known
    public static IReadOnlyList<string> ServerTenantSegments { get; }  // api, hub, blobs, mcp
    public static IReadOnlyList<string> SpaFirstSegments { get; }      // tenants, signed-out
    public static IReadOnlyList<string> SpaTenantSegments { get; }     // settings, me, list
    public const string NewSegment = "new";
}
```

`dotnet run --project <Migrator> -- emit-client-assets <dir>` realises the composition with no
database, hosted service or connection (spec 0011 §6.1). It writes:

- `generated/tellma-metadata.ts`, the loaders module:
  `tmMetadataLoaders({ core: 'tm-metadata/core-K3J7Q2M9X1BA.json', … })`;
- `generated/tellma-entities.d.ts` (0029 §15);
- `generated/assets/{core,entities,contracts}-<h>.json` and `strings-<lang>-<h>.json`, where `<h>`
  is the first 12 base32 characters of SHA-256 over the file.

Three rules govern these files:

- **Deterministic output.** Sorted keys, invariant culture, `\n` and no timestamps keep bytes and
  names stable, so an unchanged build busts no cache.
- **Loaders.** A loader fetches with `fetch(url, { credentials: 'same-origin' })` and
  `JSON.parse`. A failure rejects, and the boot page shows (§4.3).
- **Who writes what.** This spec's `CoreFeature` contributor writes `distribution`, `features` and
  `routing`. Every other section and the string packs come from the owners named in §3.1's sketch;
  the Entities chunk and the `.d.ts` come from 0029 §2.1.

### 3.3 Loading

| When | What |
|---|---|
| before first render | the Core chunk and the string packs of the active language and English (§7.3); `/signed-out` loads neither (§6) |
| before the first API call | the Contracts chunk, awaited once by `TmApi` |
| on entity routes | the Entities chunk, through `TmEntities.load()` (0029 §4.1), awaited by the route host beside the page chunk (§4.4) |

Every chunk sits in the worker's `shell` group, so a warm launch reads it from CacheStorage. On a
cold launch, the preload links fetch Core and the default language's string pack with the
JavaScript. `TM_CLIENT_METADATA` is the loaded Core chunk; injecting it earlier throws in
development.

### 3.4 Contracts

The Contracts chunk carries three facts per projected route: its fingerprint, whether it is long,
and whether a retry is safe. A route template looks like `POST /{tenantId}/api/web/centers/query`.
The chunk holds the same data as the checked-in `contracts.json`, and spec 0016 §3.10 pins that
both match the endpoints.

- **`fingerprints`.** `TmApi` sends the call's fingerprint as `Tellma-Contract` (§10.2).
- **`long`** lists the routes the server runs under `tellma-long` (spec 0016 §8.3): the actions
  declared `Long = true` (spec 0015 §2.3), spec 0019's Excel operations among them. `TmApi` gives
  them the 6-minute watchdog with no `long` option from the caller (§10.3).
- **`idempotent`** lists the `POST` routes of spec 0016 §2.2's retried class: the stack reads and
  the actions declared `Idempotent = true`. A `GET` route is always idempotent. `read` refuses any
  other mapped route (§10.1).
- **Routes outside the map.** Hand-mapped routes carry no contract metadata (spec 0016 §3.10), so
  the caller passes `long` for them, and `read` trusts the caller.

### 3.5 Drift rules

`tellma-client build` fails with exit 3 when the declarations disagree with the emitted server
facts. Appendix A lists every code. Drift means any of:

- a language or calendar without a client definition;
- a client feature the server does not compose;
- a declaration naming an unknown entity, path, action, setting category or widget type;
- a page path that takes a reserved word.

### 3.6 The config check

One function checks the merged configuration against the metadata. It runs in Node during the
build, and at startup in development.

```ts
interface TmConfigProblem {
  readonly code: TmConfigProblemCode; readonly severity: 'error' | 'warning'; // Appendix A's config codes
  readonly key: string; readonly feature: string; readonly message: string;   // 'page:centers/map', 'acme', the text
}
declare function tmCheckAppConfig(config: TmAppConfig, metadata: TmClientMetadata, entities: readonly TmEntityMetadata[]): readonly TmConfigProblem[];
declare function tmAppConfigOf(appConfig: ApplicationConfig): TmAppConfig | null; // the config provideTellma received
```

- **In the build.** After the emit, a Node step loads `src/app.config.ts` through `tsx` and takes
  the config with `tmAppConfigOf`. It reads the Core and Entities chunks and calls
  `tmCheckAppConfig`, which also runs 0029's `tmCheckEntityDeclaration`. Declarations are plain
  objects whose lazy members are never called, so no browser and no Angular compiler run. Errors
  fail the build; `label-missing` warnings (0029 §2.4) print and pass.
- **At startup.** In development, the boot initializer runs the same function and throws
  `TmConfigError` listing every problem. In production it emits each problem as
  `tm.config.problem { code }`.

## 4. Boot

Before Angular starts, a blocking script reads the profile cookie and the boot record. With no
network, it decides the session, language, direction, theme and launch target. Angular then awaits
only local reads before its first render.

### 4.1 `index.html`

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover" />
    <meta name="color-scheme" content="light dark" /> <meta name="theme-color" content="#001722" />
    <title>Acme</title>
    <link rel="manifest" href="manifest.webmanifest" /> <link rel="icon" href="icons/favicon.svg" type="image/svg+xml" />
    <script src="tm-boot.js"></script>
  </head>
  <body><tm-app></tm-app></body>
</html>
```

The scaffold carries the `tm-boot.js` tag, and `build` generates the file (§4.2). The `<title>`
is the fallback until `tm-boot.js` sets the localized display name. `build` fails with
`index-html-invalid` when `tm-boot.js` is not the first script in `<head>`, when `<tm-app>`, the
manifest link or the `color-scheme` meta is missing, or when any inline script exists.

### 4.2 `tm-boot.js`

`tm-boot.js` is a blocking classic script of at most 3 KB gzip. `tellma-client build` generates it
from `@tellma/core/boot/tm-boot.template.js` by replacing one marker with the JSON literal
`TM_BOOT` below. Its logic is a pure function `tmBoot(env)` over an injected environment (cookie
string, storages, location, navigator facts); the generated file calls it with the real globals.

```js
var TM_BOOT = {"v":1,"languages":[{"code":"en","dir":"ltr"},{"code":"ar","dir":"rtl"}],"defaultLanguage":"en",
  "strings":{"en":{"Distribution_DisplayName":"Acme","Boot_Title":"{displayName} couldn't start", …},"ar":{…}}};  // per language
```

The script reads:

- `TM_BOOT`;
- the profile cookie, treated as absent when it fails to decode, has `v ≠ 2` or has an empty `sub`;
- the boot record, treated as absent when it is invalid JSON or has `v ≠ 1`;
- the sign-in mark `sessionStorage['tm.login']` (below);
- `location`, `navigator.languages`, `navigator.cookieEnabled` and `prefers-color-scheme`;
- the feature gate: `navigator.locks`, `BroadcastChannel` and `indexedDB`.

The script writes:

- `<html lang dir data-theme>` before first paint, where `data-theme` is `light` or `dark`;
- `document.title`: the boot language's display name;
- a fresh boot record `{ v: 1, theme, lang, dir, sub: null, lastTenantId: null }` when none exists
  or its `sub` differs from the profile's; the app writes `sub` later (§8.1);
- `window.__tmBoot = { state: 'run' | 'redirect' | 'page', profile, record, cold, auto, at }`,
  which `bootstrapTellma` reads. `auto` is the tenant of an automatic entry, else null (§9.3).

The decision steps run in order; the first that applies ends the script:

| Step | When | Then |
|---|---|---|
| 1 | a gated feature is missing | the boot page, reason `unsupported` |
| 2 | the path is `/signed-out` | apply `lang`, `dir` and `theme` from the record, else step 4's cold choice; state `run` (no cookie needed) |
| 3a | no valid cookie, and cookies are disabled | the boot page, reason `cookies` |
| 3b | no valid cookie, and the sign-in mark was set less than 60 s ago | the boot page, reason `sign-in-failed` |
| 3c | no valid cookie, and the record names a `sub` (the session expired) | keep copies of the record's `sub` and `lastTenantId`, then the expiry delete (below); continue to 3d |
| 3d | no valid cookie | write the sign-in mark and `location.replace('/bff/login?returnUrl=' + encodeURIComponent(returnUrl) + '&ui_locales=' + lang)`, where `lang` is the boot language; state `redirect` |
| 4 | a valid cookie | read, then remove, the sign-in mark; choose warm or cold, the language, the theme, the target and `auto` (below); state `run` |

The sign-in mark guards the sign-in loop and records who chose the target:

```ts
interface TmLoginMark {     // sessionStorage 'tm.login'; written by step 3d and TmSession.signIn (§8.3)
  at: number;               // Date.now(), for step 3b
  expired: boolean;         // the session expired (telemetry, §8.6)
  returnUrl: string;        // what /bff/login received
  auto: boolean;            // the platform chose returnUrl
  sub: string | null;       // whose session this was: the expired record's sub (3d), or the user the tab ran as (§8.3)
}
```

- **The boot language.** Steps 1–3 use `record.lang` when a record exists, else the first offered
  `navigator.languages` entry, else the default. Step 4 uses its own language choice (below). The
  boot page, `document.title` and 3d's `ui_locales` use the boot language.
- **The expiry delete (3c).** The script deletes that user's data by spec 0025 §2.2's frozen
  layout, taking at most 1 s in all. It opens `tm-app` without a version, aborting when
  `onupgradeneeded` reports `oldVersion = 0`. It deletes the user's key range and the
  `tm-images-v1` cache. Then it nulls the record's `sub` and `lastTenantId`.
- **`returnUrl` (3d)**, from the copies 3c kept:
  - at `/` with a last tenant: `/{lastTenantId}`, `auto: true`, so an expired session returns to
    the last tenant;
  - at `/` without one: `/`, `auto: true`, and the BFF may pick (§9.3);
  - any other path: the path and query, `auto: false`.
- **A valid cookie (4).** The launch is cold without a record or when `record.sub ≠ profile.sub`.
  - Language, warm: `record.lang`.
  - Language, cold: the profile's `locale` matched by language subtag against the offered list,
    else `record.lang`, else the first offered `navigator.languages` entry, else the default.
  - `dir` follows the language. Theme: `record.theme`, else `system`.
  - Warm at `/` with a `lastTenantId`: `history.replaceState` to `/{lastTenantId}`, and `auto` is
    that tenant.
  - Under `/{t}`, `auto` is `t` in two cases. Either the mark's `auto` is true and the path is
    exactly `/{t}`. Or the mark's `sub` is set and differs from the profile's, so another user's
    session chose the path.

**Example —** two expired sessions:

- Farah returns to `/17/centers` after nine days. Step 3c deletes her data, and 3d replaces the
  page with `/bff/login?returnUrl=%2F17%2Fcenters&ui_locales=ar`.
- The installed app opens `/` after nine days. Step 3d sends `returnUrl=%2F17` with `auto: true`.
  After sign-in Farah lands on `/17`, a cold entry (§4.4), not on the picker.

**The boot page** is one page for every reason. The script builds it at `DOMContentLoaded` inside
`<tm-app>` with DOM APIs and no HTML strings, so Trusted Types need nothing; `.tm-boot-page` styles
it. A `main` landmark holds the `h1` "{displayName} couldn't start", one sentence and a Try again
button, with focus on the heading. It renders in the boot language with English fallback, with
`lang` and `dir` set.

| Reason | Sentence | Try again |
|---|---|---|
| `unsupported` | "Update your browser to a current version of Chrome, Edge, Firefox or Safari." | reloads |
| `cookies` | "Turn on cookies for this site, then try again." | reloads |
| `sign-in-failed` | "We couldn't sign you in. Try again in a moment." | re-runs 3d with the old mark's `returnUrl`, `auto`, `expired` and `sub`, writing a fresh mark |
| `load-failed` | "Check your connection and try again." | reloads |

Core's resx carries these strings as `Boot_Title`, `Boot_Unsupported`, `Boot_Cookies`,
`Boot_SignInFailed`, `Boot_LoadFailed` and `Boot_TryAgain`. `build` copies them from the emitted
string packs into `TM_BOOT`. It adds each language's `Distribution_DisplayName` (§3.1), and the
config's `displayName` fills a missing one.

### 4.3 The startup sequence

1. **Gate.** `bootstrapTellma` returns a promise that never settles unless `__tmBoot.state` is
   `run`.
2. **Initializers.** `bootstrapApplication(TmApp, …)` runs its initializers in parallel:
   - the Core chunk;
   - the active language's tables and string pack, and English's (§7.3); on `/signed-out` only the
     active language's tables load (§6);
   - `TmSession` parses the cookie;
   - `TmClientStore` opens and runs its boot step's one store read: the device partition, the user
     partition and, on a tenant path, the tenant partition (0025 §2.6).

   A cached calendar other than `gc` imports its entry point next.
3. **First render.** Initial navigation, then first render: `tm-app`, the shell (0027), the route
   host's skeleton, then the page. On a cold entry the page comes after the launch documents (§4.4).
4. **After first render:** worker registration (§5.4), the Contracts chunk, and idle preloads
   through `TmUrls.preload`. Spec 0027 calls `preload` for pins, recents and home.

- **No network before a warm render.** No initializer, guard or resolver awaits `TmApi`, so a warm
  launch has no network wait.
- **At most two waits when cold,** each under a skeleton: the launch redirect at `/` (§9.3) and the
  cold tenant entry (§4.4). A single-tenant user who signs in at `/` has one: the BFF lands them in
  the tenant (§9.3).
- **Load failure.** A Core or string load that rejects shows the boot page with reason
  `load-failed` and emits `tm.boot.load-failed { chunk }`.
- **Launch timing.** Each launch emits `tm.launch.cold` or `tm.launch.warm`, with the time from
  navigation start to chrome rendered and the `routeKind`.

### 4.4 `TmApp` and the route host

The route host is the component of every page route. It draws the page's skeleton in the click's
frame, loads the page and gives it a page scope.

```ts
interface TmRouteData {
  readonly kind: TmPageKind;                         // 0026 §9.1
  readonly skeleton: 'search' | 'details' | 'report' | 'dashboard' | 'home' | 'blank';
  readonly load?: TmLazyComponent;                   // static pages; entity routes resolve their host
  readonly titleKey?: string; readonly securable?: TmSecurableRef;
}
```

- **`tm-app`.** `TmApp` renders the `<router-outlet>`, the session-ended dialog (§8.6) and the
  signing-out overlay (§8.4); shells are route components (0027). `<html dir>` is the live
  direction host (0010 §B.3), so `tm-app` wraps nothing in `[dir]`.
- **Skeleton.** The host draws it in the click's frame, on 0023's `page-header` layout, with the
  final geometry of the host that replaces it; the page specs pin zero shift. Then the host loads
  the page and creates it with `NgComponentOutlet`.
- **Cold entry.** On a tenant route while `TmLaunchCache.warm()` is false, the host keeps the
  skeleton and awaits the cold refresh of 0025 §3.2 before it creates the page. The page chunk and
  `TmEntities.load()` load meanwhile; the page's data read starts after the refresh. So no page,
  `tmPage` permission gate (§2.4) or home ever runs before the first `me` and `settings`. A failed
  refresh follows 0025 §3.2's failure rows, and a warm entry awaits nothing. A
  `404 tenant-not-found` on an automatic entry swaps the skeleton for the picker with no notice
  (§11).
- **Entity routes.** The host finds the entity with `TmEntities.bySegment` and awaits
  `TmEntities.load()` beside the page chunk (cache reads when warm). It then loads `TmSearchHost`
  or `TmDetailsHost`, which host the declaration's `search` and `details` hatches (0030 §2.5, 0031
  §3.7). The tenant root loads 0035's `TmHomeHost`, which picks the `home` hatch or the default.
- **Page scope.** Each page gets `provideTmPageScope()` (0026 §9.2). On a `tmPage` route the host
  adds a `custom` context, unless the page registers its own: the title comes from `titleKey`, and
  `status` is `ready` once rendered.
- **Failure.** A chunk that fails to load runs `TmUpdates.check()` (§5.5). On `ready` or `failed`
  the host shows the error view `client-outdated` inline, and on any other result `offline` (§11).
- **Title and focus.** `TmTitleStrategy` sets `document.title` to `<page title> · {displayName}`.
  On a path change, never a query-only change, focus moves to the page's `[tmPageTitle]` heading
  (`tabindex="-1"`) once it renders, else to `main`.

**Example —** Farah opens a link to `/17/centers/5` from an email. Her launch documents for tenant
17 are not current, so the entry is cold:

1. The details skeleton shows in the first frame.
2. The details chunk and the Entities chunk load while 0025's cold refresh fetches `me` and
   `settings`.
3. Only after the refresh does the host create the details page, which starts its read. Farah never
   sees a forbidden flash or a missing column.

## 5. PWA, service worker and updates

The app is an installable PWA. Angular's service worker caches the whole app except three on-demand
sets, so a warm launch reads everything from CacheStorage. Updates download in the background and
apply at the next reload.

### 5.1 The manifest

The scaffolded `public/manifest.webmanifest` sets:

- `id`, `start_url` and `scope` to `/`, so `start_url` runs the launch redirect (§9.3);
- `name` and `short_name` to "Acme";
- `display: standalone`, `dir: auto` and `lang`;
- `background_color` and `theme_color` to `#001722`;
- the icons `icons/icon-192.png`, `icon-512.png` and `icon-maskable-512.png`
  (`purpose: maskable`).

`build` then localizes the names and checks the file:

- **Localized names.** `build` adds `name_localized` and `short_name_localized` to the output
  manifest, keyed by language code, from each offered language's `Distribution_DisplayName` and
  `Distribution_ShortName` (§3.1). A language without a key gets no entry, and a browser without
  them shows `name` and `short_name`.
- **Checks.** `build` fails with `manifest-invalid` on a missing member, a `short_name` or
  localized short name over 12 characters, an icon whose PNG header disagrees with its stated
  size, or `prefer_related_applications: true`.

### 5.2 The installability gate

A nightly Playwright run in Chromium against Acme's production build is the gate. It requires an
empty list from the DevTools protocol's `Page.getInstallabilityErrors` and no errors from
`Page.getAppManifest`. Lighthouse has no PWA category; the install prompt needs the `fetch` handler
Angular's worker has.

### 5.3 The generated worker config

`tellma-client build` writes `ngsw-config.json` and runs `ngsw-config` (the `@angular/service-worker`
bin) over the final output. It runs after every file is in place, so every hash matches.

| Setting | Value |
|---|---|
| `appData`, strategy | `{ version, platformVersion }`; `navigationRequestStrategy: performance` |
| data groups | none: the API is POST-only, and blobs use the HTTP cache and `tm-images-v1` |
| `navigationUrls` | derived from the `routing` words: `/**` minus `/**/*.*`, `/**/*__*`, `/**/*__*/**`, `/bff/**`, `/api/**`, `/id/**`, `/signin-oidc`, `/signout-callback-oidc`, `/health/**`, `/.well-known/**`, `/openapi/**` and `/*/{api,hub,blobs,mcp}/**` |
| `on-demand` (`lazy`, `lazy`) | the chunks reachable only through the ECharts engine's dynamic import, `@tellma/core/report-studio` or `@tellma/core/support`; the manifest and icons |
| `shell` (`prefetch`, `prefetch`) | everything else: every other chunk (read from `stats.json`), styles, the Latin and offered-script fonts, `brand/*`, every `tm-metadata/*` file |

The metadata chunks need the session cookie (§6), and the first install runs after first render,
signed in. An update attempted while signed out fails on them and retries at the next check.
Angular never activates a half-cached version.

### 5.4 Registration

```ts
provideServiceWorker('ngsw-worker.js', { enabled: !isDevMode(), registrationStrategy: () => tmRegisterWorker() });
```

`tmRegisterWorker()` waits for first render and `ApplicationRef.whenStable()`, at most 30 s. It then
calls `navigator.serviceWorker.register(url, { scope: '/', updateViaCache: 'none' })` with a
`TrustedScriptURL`, and returns an observable that never emits. So Angular never registers with a
plain string, which enforced Trusted Types refuse.

- **The `tellma` policy.** `@tellma/core` creates the Trusted Types policy `tellma` lazily, with
  `createScriptURL` only. It admits exactly the same-origin `/ngsw-worker.js` and throws otherwise.
- **No worker.** Without `navigator.serviceWorker` nothing registers, and every launch is cold. A
  rejected registration emits `tm.sw.register-failed`.
- **Web Push, later.** The push spec registers a wrapper script that loads `ngsw-worker.js` through
  `importScripts` and adds the `push` and `notificationclick` handlers. Registering it at the same
  scope updates the registration, and the `tellma` policy then admits that script instead.

### 5.5 `TmUpdates`

```ts
type TmUpdateCheck = 'ready' | 'none' | 'failed' | 'unreachable' | 'disabled';
declare class TmUpdates {
  readonly enabled: boolean;                        // a worker controls this page
  readonly version: Signal<string | null>;          // its appData.version
  readonly available: Signal<string | null>;        // a newer version, ready to apply
  readonly checking: Signal<boolean>;               // a check() or update() is running
  check(): Promise<TmUpdateCheck>;                  // asks the worker; settles when its check does
  update(): Promise<TmUpdateCheck>;                 // check(); reloads on 'ready' or 'disabled', else resolves with the result
  reload(): void;                                   // location.reload(); drafts survive (0025 §11)
}
```

Angular's `checkForUpdate()` resolves `false` for three different outcomes, so `check()` reads the
event that arrives during the check:

| `check()` result | Detected by |
|---|---|
| `ready` | `available()` is already set, or `VERSION_READY` arrives |
| `none` | `NO_NEW_VERSION_DETECTED` arrives |
| `failed` | `VERSION_INSTALLATION_FAILED` arrives: the new version did not download |
| `unreachable` | `false` with no event: the worker could not fetch `ngsw.json` |
| `disabled` | no worker controls the page |

| Event | Behaviour |
|---|---|
| checks | the worker checks `ngsw.json` at start and on navigations; `TmUpdates` also checks when the tab becomes visible after an hour or more hidden, every 6 hours while visible, and on demand (§5.6) |
| `VERSION_READY` | sets `available` and shows the banner `tm.update` (0028 §9.5); running tabs keep their version, and a reload or a new tab gets the new one; emits `tm.sw.update-ready { fromVersion }` |
| `unrecoverable` | reloads at once, drafts being in session storage; emits `tm.sw.unrecoverable` |
| `VERSION_INSTALLATION_FAILED` | emits `tm.sw.install-failed`; the worker serves new loads from the network until the next check retries |

### 5.6 An outdated client (412 and route-not-found)

A changed endpoint refuses an old tab's call with `412 client-outdated` before anything runs. A
route the server no longer maps answers `404 route-not-found` (spec 0016 §3.10, §7.5). On either,
`TmApi` decides at once which side is old from the response's `Tellma-Build`, and asks the worker
only when the versions do not say. §11 owns the copy.

`TmAppVersion.compare` (spec 0026 §7) orders the server's build against the UI version by SemVer
precedence, so the `+<sha>` build metadata never counts:

| Server build | Code | How it is decided |
|---|---|---|
| newer | `client-outdated` | at once: an old tab after a deployment |
| older | `server-outdated` | at once: a new tab reached an older instance during a slot swap |
| unordered: the same `X.Y.Z[-pre]`, another sha | the worker's answer: `ready` → `client-outdated`; `none` → `server-outdated`; any other result, or past the cap, `client-outdated`, the likelier cause | `TmUpdates.check()`, capped at 3 s |
| the same build | kind `server`, code `contract-mismatch` | at once: a bug, reported to telemetry |

| Code | A read | A write |
|---|---|---|
| `client-outdated` | inline, with Reload | the 5xx dialog with Reload (§11); the draft survives the reload |
| `server-outdated` | retried once after 30 s (§10.5), then inline | the 5xx dialog with Try again; never retried on its own |

- **A rollback** leaves the server older for good, so `server-outdated` would repeat forever. To
  break the tie, a second `server-outdated` verdict for the same server build asks the worker
  first, under the same 3 s cap. The worker treats any different manifest as an update:
  - `none` keeps `server-outdated`;
  - `ready`, any other result or the cap gives `client-outdated`, and Reload finishes the download.
- **Reload** runs `TmUpdates.update()`, so it downloads the new version before reloading (§11).

Telemetry: `tm.api.client-outdated { verdict, source: 'build' | 'worker' }`.

## 6. SPA hosting rules

These rules fix what the server answers to every browser request that is not an API call. Spec
0011 §5.1 implements them in `MapTellma`.

| Response | `Cache-Control` | Access |
|---|---|---|
| `index.html`, as a file or the fallback | `no-cache`, with an ETag | anonymous |
| Hashed JS, CSS, fonts, string packs (`tm-metadata/strings-*`) | `public, max-age=31536000, immutable` | anonymous |
| Metadata chunks (`tm-metadata/core-*`, `entities-*`, `contracts-*`) | `private, max-age=31536000, immutable` | the session cookie (`Tellma.Web`); 401 without |
| `ngsw.json`, `ngsw-worker.js`, `safety-worker.js`, `tm-boot.js`, `manifest.webmanifest`, icons, `brand/*` | `no-cache` | anonymous |

- **Static files** are endpoint-routed through `MapStaticAssets`, with Brotli and gzip variants
  built at publish and ETags. They are stamped `TenantlessEndpointMetadata("spa-assets")`.
- **Hashed names.** A name is hashed when a `-` and at least eight `[A-Z0-9]` precede the
  extension. `build` fails a JS, CSS, font or `tm-metadata` file without one (`unhashed-asset`).
  Three scripts are exempt, since the table's last row serves them `no-cache`: `ngsw-worker.js`,
  `safety-worker.js` and `tm-boot.js`.
- **Metadata behind the cookie.** The metadata chunks describe every entity, action, securable and
  setting key of the distribution. `private` keeps shared caches from serving them to anyone else.
  `/signed-out` loads none of them: it renders from the bundle's strings and the display name
  `tm-boot.js` set as `document.title` (§4.2).
- **The fallback.** The server answers with `index.html` only for a `GET` or `HEAD` that has
  `Sec-Fetch-Mode: navigate`, or lacks that header and accepts `text/html`. The first segment must
  not be in `ServerFirstSegments`, and under an integer first segment the second must not be in
  `ServerTenantSegments`. Any other unmatched request gets a problem body: `404 route-not-found`
  under an integer first segment, else `404 not-found`.
- **`index.html` headers:** `Referrer-Policy: strict-origin-when-cross-origin`,
  `X-Content-Type-Options: nosniff`, `Cross-Origin-Opener-Policy: same-origin`,
  `Permissions-Policy: camera=(), microphone=(), geolocation=(), display-capture=(self)`, and the
  CSP below.
- **CSP variations.** `connect-src` adds Azure SignalR's origins when
  `Azure:SignalR:ConnectionString` is set. `form-action` adds the authority only in Standalone
  identity mode. Inline styles are allowed because Angular inserts component styles and a cached
  `index.html` cannot carry a nonce.

```text
default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:;
font-src 'self'; connect-src 'self' <Azure SignalR https:// and wss:// origins>; frame-src 'self' blob:;
frame-ancestors 'none'; form-action 'self' <authority origin>; base-uri 'self'; object-src 'none';
worker-src 'self'; manifest-src 'self'; require-trusted-types-for 'script'; trusted-types angular angular#bundler tellma
```

## 7. Localization and theme

`TmLocalization` decides the active language, locale, direction, calendar, content slot and display
time zone from the launch documents. It switches them in place, with no reload and no request.
String tables ship in the bundle; the theme is a device setting.

### 7.1 `TmLocalization`

```ts
declare class TmLocalization {
  readonly language: Signal<string>;                           // 'ar'
  readonly region: Signal<string>;                             // 'SA' (§7.7)
  readonly digits: Signal<'Latin' | 'Native' | null>;          // null: the region's default (§7.7)
  readonly locale: Signal<string>;                             // 'ar-SA', 'ar-SA-u-nu-latn' (§7.7)
  readonly dir: Signal<'ltr' | 'rtl'>;
  readonly calendar: Signal<TmCalendar>;
  readonly calendarCode: Signal<string>;                       // 'gc' | 'uq' | 'et' | …
  readonly contentSlot: Signal<1 | 2 | 3>;                     // which Name slot the active language reads
  readonly timeZone: Signal<string>;                           // IANA; the zone instants display in
  readonly deviceTimeZone: Signal<string>;                     // the browser's resolved zone (§7.8)
  readonly displayName: Signal<string>;                        // the localized display name (§3.1)
  readonly languages: Signal<readonly TmLanguageDefinition[]>; // what the language switch offers here
  readonly calendars: Signal<readonly string[]>;               // what the calendar switch offers here
  setLanguage(code: string): void; setCalendar(code: string): void;
}
```

| Value | Tenantless shell | In a tenant |
|---|---|---|
| `language` | `boot.lang` if offered, else the default | `me.PreferredLanguage` if offered → `boot.lang` if offered → the tenant's primary language → the default |
| `languages` | the offered languages | the tenant's content languages, plus the active one when it is not among them |
| `region`, `digits`, `locale` | the language's default region, no digits choice | the user's, else the tenant's (§7.7) |
| `calendarCode` | `gc` | `me.PreferredCalendar` if in the tenant's pair, else the tenant's primary |
| `calendars` | `['gc']` | the tenant's pair |
| `contentSlot` | 1 | the content language whose language subtag equals the active one's, else 1 (0013 §9.2) |
| `timeZone` | the browser's resolved zone | `me.PreferredTimeZone`, else the settings' `TimeZone` |

- **Inputs follow the launch documents** (0025 §3), so a language changed in another tab
  converges here.
- **Tokens mirror the signals.** `TM_ACTIVE_LOCALE` is `locale`, `TM_CALENDAR` is `calendar`,
  `TM_TIME_ZONE` is `timeZone` and `TM_DEVICE_TIME_ZONE` is `deviceTimeZone` (0010 §D.4).
  Transloco's active language is `language`.
- **Native names.** The language switch shows the native names of `TM_CLIENT_METADATA.languages`.

### 7.2 A language switch

1. **Load.** `setLanguage(code)` refuses a code outside `languages()`, throwing in development.
   The language's strings load if absent (a cache read). Nothing changes until they arrive, so no
   key flashes.
2. **Apply, in one change-detection pass:** Transloco's active language, `<html lang dir>`,
   `locale` and `contentSlot`. Labels, messages, formats and display names re-render in place, and
   open overlays follow the live direction (0010 §B.3).
3. **Save.** `boot.patch({ lang, dir })` (0025 §2.1). In a tenant, also
   `TmApi.write('users/me/locale', { language })` (spec 0018 §3.5); the switch passes its
   `MeLocaleResult` to `TmCacheRefresher.adopt(t, { locale })` (0025 §5.3, §9.5). If the write
   fails, the switch stands, and the write is retried once when `TmApi.online` turns true.

`setCalendar` follows the same steps with the calendar's entry point (§7.4) and `{ calendar }`. A
region or digits change applies as step 2 alone, with no load and no request of its own (§7.7).
Telemetry: `tm.l10n.language-changed` and `tm.l10n.calendar-changed`, each `{ from, to }`.

### 7.3 Strings

- **Sources.** Each language has the emitted string pack (root keys, `PascalCase_Underscored`)
  and the definition's `tmUi` and `tm` tables. `tmEnglish()` ships English for both scopes, and
  `tmArabic()` (`@tellma/locale-ar`) ships Arabic. A string pack key that begins `tm.` or `tmUi.`
  is `string-key-collision`.
- **Loading.** The active language and English load at boot (§4.3); another language loads on its
  first switch.
- **The seam.** `provideTellmaUi({ translations })` (0010 §B.3) receives a loader that merges a
  language's dictionary: the string pack at the root, then `tmUi`, then `tm`. `availableLangs` is
  the offered list.
- **Fallback.** Each key falls back to English. A server code missing from every pack renders the
  server's `message` (§7.5). Any other missing key renders the key and warns in development.

### 7.4 Calendars

`gc` is built in. `uq` and `et` import `@tellma/core-ui/calendar-umalqura` and
`@tellma/core-ui/calendar-ethiopic` on first use. A distribution calendar comes from
`TmAppConfig.calendars`. All sit in the `shell` group.

Built-in calendars use the label keys `tm.calendar.<code>`. `Tellma-Calendar` carries
`calendarCode` on tenant calls (§10.2).

### 7.5 Server text

```ts
declare class TmServerText {                              // server codes through the string packs (0013 §10.4)
  problem(problem: TmProblem): Signal<{ title: string; detail: string }>;  // Problem_<code>[_Detail] + arguments
  error(error: TmProblemError, entity?: string): Signal<string>;           // the code with . → _ + arguments, else message
  message(key: string, args?: Readonly<Record<string, TmJson>>): Signal<string>; // any server key with raw arguments
  has(key: string): boolean;                                               // the active or English pack holds the key
}
```

Arguments are raw values, formatted in the active locale, calendar and zone. `arguments.property`
resolves to `<Schema>_<Entity>_<Property>` for the entity (0016 §7.3). A language switch re-renders
every message on screen without a request.

### 7.6 Theme and branding

- **Theme.** `TmTheme` (0010 §B.2) is seeded from `boot.theme` without writing back. An effect
  writes `boot.patch({ theme })` when `TmTheme.setting` changes, and another tab's change arrives
  through `boot.changes` (0025 §2.1). `tm-boot.js` already painted the theme, so nothing flashes.
- **`theme-color`.** An effect sets `<meta name="theme-color">` to `--tm-theme-color` under
  `TmTheme.effective`. In both Tellma themes that is the ink app bar's color; `@layer tm.theme` may
  change it.
- **Styles.** `@tellma/core/styles.css` imports the tokens CSS, the core-ui fonts, the directive
  stylesheets (0023's `styles/tm-description-list.css` among them) and the boot-page styles.
  Locale packages such as `@tellma/locale-ar` add their fonts. Distribution deltas live in
  `@layer tm.theme`.
- **Brand files.** `brand/{wordmark,mark}-on-{dark,light}.svg` default to `@tellma/core/brand/`,
  and a same-named file in `public/brand/` replaces one. 0027 renders them. `build` fails with
  `brand-file-invalid` on a file without a `viewBox`, or with `<script>` or an event-handler
  attribute.
- **Fonts.** `build` preloads them (§2.6); metric-adjusted fallbacks keep the swap shift-free (spec
  0002).

### 7.7 Regional formats

Formats are their own axis beside the UI language. A user and a tenant each choose a region and
digits, and `TmLocalization` builds one locale string from them. The server resolves the same
values in the same order (spec 0013 §9.2), so both ends format alike.

- **Region** — an ISO 3166-1 alpha-2 code (`SA`). It picks date and number patterns, separators
  and the first day of the week.
- **Digits** — `Latin` or `Native`, the language's own digit set (Arabic-Indic for `ar`). Unset
  means the region's default.
- **Where they live.** `me.PreferredRegion` and `me.PreferredDigits` (spec 0014 §3.1), and
  `settings.Region` and `settings.Digits` (spec 0013 §5.6). Each `TmLanguageMetadata` (§3.1)
  carries the language's default region, offered regions and native digits (spec 0013 §7.2).

The client resolves them in this order:

1. **Language** — §7.1.
2. **Region** — the first of `me.PreferredRegion`, `settings.Region` and the language's default
   region that is offered with the active language. Outside a tenant: the default region.
3. **Digits** — `me.PreferredDigits`, else `settings.Digits`, else unset. Outside a tenant: unset.
4. **The string** — `<language>-<region>`, then `-u-nu-latn` for `Latin`, or `-u-nu-<system>` for
   `Native` when the language has native digits. The extension is added only when it changes the
   region's default digits, so the string stays canonical.
5. **The browser check** — once per `<language>-<region>`, `TmLocalization` compares it with the
   `resolvedOptions().locale` of `Intl.DateTimeFormat` and `Intl.NumberFormat`.
   - A miss falls back to the language's default region, then to the default language's locale.
     It emits `tm.l10n.locale-fallback { language }` once per launch.
   - The fallback changes only `locale`, the string the client formats with. `Accept-Language`
     keeps step 4's string (§10.2).

**Example —** Acme offers `en` (default region `GB`) and `ar` (default region `SA`):

| Language | User region | Tenant region | Digits (user, else tenant) | Locale |
|---|---|---|---|---|
| `ar` | — | — | — | `ar-SA` |
| `ar` | — | `AE` | — | `ar-AE` |
| `ar` | — | — | `Latin` | `ar-SA-u-nu-latn` |
| `ar` | `AE` | `SA` | `Native` | `ar-AE-u-nu-arab` |
| `en` | `SA` | — | `Latin` | `en-GB`: `en-SA` is not offered, and Latin is `en`'s default |
| `en` | `AE` | — | — | `en-AE` |

- **Live switching.** `TmLocalization` computes `locale` from `me`, `settings` and the metadata.
  A My profile save adopts `me`, and a Settings save adopts `settings` (0025 §5.3). Other tabs
  follow `store.changed`. The new locale applies as §7.2's step 2: grids, date pickers
  (`tmFirstDayOfWeek`) and server-text arguments re-render in one pass. Telemetry:
  `tm.l10n.locale-changed { from, to }`, with locale tags only.
- **Where they are edited.** Three surfaces edit them, and the fields' `scope` (spec 0032 §4.1,
  §4.2) sets which regions each lists:
  - My profile, with `tm-region-field` and `tm-digits-field` (spec 0032 §4.2): the regions
    offered with the active language;
  - the Users page, with the same fields: every region offered with any offered language;
  - the Settings General tab's Formats card (spec 0032 §7.5): every region offered with any
    offered language.

  The session menu has no format switch: formats are set once, not switched like the language.
- **Server text.** The SPA sends step 4's string as `Accept-Language` (§10.2). Server text always
  uses Latin digits (spec 0013 §9.2), so a `Native` user sees Arabic-Indic digits in the app and
  Latin ones in emails.

### 7.8 The device time zone

Absolute date-times carry a GMT offset label when the display zone's offset differs from the
device's. When the device's zone changes, a one-time dialog offers to switch the user's time zone,
and a per-device option makes later switches automatic. A zone that merely differs never prompts.

The display zone is `timeZone` (§7.1). Offsets are compared at the instant shown, never by zone id,
so `Asia/Kuwait` and `Asia/Riyadh` (both +3) never get a label. Spec 0010 §D.4 formats the label,
with the modes `'auto'`, `'column'` and `'none'`.

| Surface | Label |
|---|---|
| A details field, `tm-value` in view mode, an absolute time, the tooltip of `tm-relative-time` | appended when the offsets differ at that instant: "20/10/2026, 10:42 GMT+3" |
| A grid or report column of instants | once, in the header, when the offsets differ now: "Created at (GMT+3)" (spec 0029 §12.1); cells stay bare |
| A cell in such a column whose offset differs from the header's (a DST change between rows) | its own label: "12/01/2026, 10:42 GMT+2" |
| Relative time ("3 hr ago"), dates, wall-clock `datetime` values, a format without a time | never |
| Server text and Excel | never |

- **Offsets, never abbreviations.** "IST" and "CST" are ambiguous; "GMT+5:30" is not, and it
  localizes ("غرينتش+٣").
- **Re-read.** `deviceTimeZone` re-reads the browser's zone on `visibilitychange` to visible and on
  `pageshow` with `persisted`. No browser event reports a zone change.

**Detecting a change.** `TmDeviceZone` checks after first render at idle, on each tenant entry and
on `visibilitychange` to visible. It acts only in a visible tab of a tenant, once that tenant's
`me` and `settings` are present. It remembers one document in the device partition of spec 0025's
store:

```ts
interface TmZoneDoc {                                  // device partition, document 'zone' (0025 §2.3)
  readonly users: Readonly<Record<string, {            // keyed by sub; at most 20 users, the oldest `at` dropped
    readonly auto: boolean;                            // "change it automatically" on this device
    readonly seen: Readonly<Record<string, string>>;   // tenant id → the device zone last handled in that tenant
    readonly at: number;                               // the last write, for the cap
  }>>;
}
declare class TmDeviceZone {                           // @tellma/core primary; its dialog is a deferred chunk
  readonly auto: Signal<boolean>;                      // this user's option on this device
  setAuto(on: boolean, source: 'prompt' | 'profile'): Promise<void>; // patches the 'zone' document
  check(): Promise<void>;                              // the steps below
}
```

- **The device partition**, because the option is per user and per device, and user partitions die
  at sign-out and expiry (0025 §2.6).
- **Per tenant**, because `PreferredTimeZone` lives on each tenant's user row, so each tenant
  notices the change on its next entry.

The check, where `device` is the device zone and `seen` is this tenant's entry:

1. **First run.** No `seen`: record `device` and stop. A new device or tenant never prompts.
2. **No change.** `device` equals `seen`: stop.
3. **Already matching.** `device` equals the display zone: record and stop.
4. **Read-only tenant.** Record and stop: `users/me/locale` would answer 403.
5. **Automatic.** The option is on: run the automatic update (below), record and stop.
6. **Same offset now.** Record and stop: nothing visible would change.
7. **Prompt.**
   1. Take the Web Lock `tm:<slug>:<sub>:zone-prompt` with `ifAvailable`. Without it, stop:
      another tab asks.
   2. Wait for `TmInputIdle.whenIdle()` (spec 0028 §9.3) and for no open modal.
   3. Re-run steps 2–6, then open the dialog.
   4. Record when the user answers or dismisses. A tab closed before that records nothing, so the
      next check asks again.

**The prompt** is a `TmModal` of size `sm`, with `role: 'dialog'` and the message as its
description.

| Part | Copy |
|---|---|
| Title | "Use your device's time zone?" |
| Message | "Your device is now on {deviceZone} ({deviceOffset}). {displayName} shows times in {displayZone} ({displayOffset})." |
| Checkbox and hint | "Change it automatically next time" / "Only on this device. You can turn this off in My profile." |
| Primary | "Change time zone" |
| Secondary | "Keep current" (also Escape and the close button) |
| `Users.TimeZoneUnknown` | an inline `tm-alert`: "{displayName} doesn't support your device's time zone. Choose one in My profile." |

- **Names.** Zones come from `tmFormatZoneName` and offsets from `tmFormatZoneOffset` (0010 §D.4).
- **Change time zone** turns busy in the click's frame and writes `users/me/locale { timeZone }`
  (spec 0018 §3.5). The result is adopted (0025 §5.3), so every tab re-renders its times and the
  labels vanish. With the box ticked, it also turns the option on. A failure other than
  `Users.TimeZoneUnknown` shows `TmErrorPresenter.describe(error, { kind: 'save' })` inline, and
  the dialog stays.
- **Keep current** never turns the option on, even with the box ticked.

**The option and the automatic update:**

- **My profile** shows `tm-auto-time-zone` under the Time zone field (spec 0032 §8): the checkbox
  "Change my time zone automatically when this device's time zone changes", hint "Saved on this
  device only." It applies at once and is not part of the form's Save. The Users page never
  shows it.
- **The update** (step 5) writes `users/me/locale { timeZone: device }` in the background and
  adopts the result. A toast then says "Times now show in {zone} ({offset})." with Change, which
  opens My profile (spec 0028 §8.1).
- **Failures.** `offline` retries once when `TmApi.online` turns true. `Users.TimeZoneUnknown`
  records the zone and stops. Any other failure records nothing, so the next check retries.
- **Change, not difference.** Only a detected change triggers the update. A user who sets another
  zone in My profile keeps it until the device moves again, so two devices never ping-pong.

**Example —** Farah flies from Riyadh to London:

1. Her laptop switches to Europe/London. She returns to the Acme tab, and the device zone is
   re-read.
2. `seen` for tenant 17 is Asia/Riyadh, the option is off, and +1 differs from +3.
3. After 2 s without input, the dialog asks: "Your device is now on United Kingdom Time (GMT+1).
   Acme shows times in Arabian Standard Time (GMT+3)."
4. She ticks the box and chooses Change time zone. Her preference becomes Europe/London, every tab
   re-renders, and `seen` becomes Europe/London.
5. A week later, back in Riyadh, the change applies by itself, with the toast.

Telemetry carries no zone id or offset, since both reveal where the user is:

- `tm.l10n.zone-change { action }`, where `action` is `first-run`, `matches`, `read-only`,
  `same-offset`, `auto` or `prompt`;
- `tm.l10n.zone-prompt { answer, auto }`, where `answer` is `change`, `keep` or `dismiss`;
- `tm.l10n.zone-auto { outcome }`, where `outcome` is `ok`, `offline` or `rejected`;
- `tm.l10n.zone-option { on, source }`.

## 8. Session

The profile cookie is the browser's synchronous signed-in signal. `TmSession` reads it around every
call and whenever the tab returns. Sign-in and step-up are same-tab redirects that drafts survive;
sign-out and expiry reach every tab of the user.

### 8.1 `TmSession` and the profile cookie

```ts
interface TmProfile { v: 2; sub: string; sh: string; name: string; email: string; locale: string | null }
interface TmStepUpChallenge { readonly acr: string; readonly maxAge: number | null }  // from WWW-Authenticate
declare class TmSession {
  readonly profile: Signal<TmProfile | null>;
  readonly state: Signal<'active' | 'expired' | 'signing-out'>;
  signIn(options?: { returnUrl?: string; stepUp?: TmStepUpChallenge }): Promise<never>;
  signOut(): Promise<never>;                 // rejects with a TmApiError when the BFF could not be reached (§8.4)
  check(): 'same' | 'missing' | 'changed';   // reads the cookie now
}
declare const tmSessionGuard: CanActivateFn;
declare function tmParseProfileCookie(cookies: string): TmProfile | null;
```

**Example —** the cookie is spec 0011 §5.5's unpadded `base64url` of UTF-8 JSON, readable by script:

```text
tellma.profile=eyJ2IjoyLCJzdWIiOiIwMDAwMDAwMC0wMDAwLTAwMDAtMDAwMC0wMDAwMDAwMDAwMDEiLCJzaCI6Ilk0…
→ { "v": 2, "sub": "00000000-0000-0000-0000-000000000001", "sh": "Y4kQ1x…", "name": "Farah Nasser", "email": "farah@acme.example", "locale": "ar" }
```

- **`sh` is the session handle.** It names the session partition (0025 §2.1) and is never a
  credential: no endpoint accepts it in place of the session cookie.
- **Lifetime.** The cookie expires with the session's idle expiry, capped by the absolute one, and
  is re-issued with the session cookie. Background requests, the hub route group and the telemetry
  relay renew neither (spec 0011 §5.5). So a present cookie means a live server session, because
  the session cookie is persistent (spec 0011 §5.5).
- **`sub` in the boot record.** `TmSession` writes `boot.patch({ sub })` after the store's boot
  step (0025 §2.6).

### 8.2 The guard

`tmSessionGuard` sits on every route except `/signed-out` and never awaits the network. It calls
`check()`: `same` allows; `missing` runs the expiry path (§8.6) and denies; `changed` runs the
changed-user path (§8.7) and denies.

### 8.3 Sign-in and step-up

`signIn()` writes the sign-in mark (§4.2) and calls `location.assign('/bff/login?returnUrl=' +
encodeURIComponent(returnUrl ?? current path and query) + '&ui_locales=' + language)`. Sign-in
renders in that language (0010 §A.10).

- **The mark's `auto`** is true only for `returnUrl: '/'`, the signed-out page's Sign in (0027
  §6.3).
- **The mark's `sub`** is the user the tab ran as, which the expiry path keeps (§8.6). So another
  user signing in from the session-ended dialog makes an automatic entry (§4.2, Review flag 20).
- **A step-up** adds `&acr_values=<acr>&max_age=<s>`.

A step-up (`401 step-up-required`) affects only the calling tab:

1. `TmErrorPresenter` asks "Confirm it's you" / "This needs you to sign in again. You'll come back
   to this page, and your changes are kept." with Continue and Cancel.
2. Continue calls `signIn({ returnUrl: current, stepUp })`. The draft is already in session storage
   (0025 §11), and the details host restores edit mode on return (0031 §11.2).
3. Back on the page, the user presses Save again: a failed write is never replayed.

`TmPermissions.sensitive()` (0029 §7) lets a page warn before the user starts. Telemetry:
`tm.session.step-up`.

### 8.4 Sign-out (every tab)

```mermaid
sequenceDiagram
  participant A as Tab A (Sign out)
  participant O as Other tabs
  participant BFF as BFF
  participant IdS as Identity server
  A->>A: overlay at once (immediate): "Signing out…"
  loop every second while a tab is busy, at most 60 s
    A->>O: drafts.query
    O-->>A: drafts.reply {dirty, busy}: label "Waiting for a save to finish…"
  end
  opt a draft is still dirty, or a save still runs
    A->>A: overlay closes; the confirm asks once
  end
  A->>A: TmTelemetry.flush(), then state = signing-out
  A->>O: session.signing-out: state = signing-out, abort reads, show "Signing out…"
  A->>BFF: POST /bff/logout {scope: global}
  BFF-->>A: 200 {redirectUrl}, profile cookie cleared, Clear-Site-Data: "cache"
  A->>A: session clean-up (0025 §2.6), drafts cleared
  A->>O: session.signed-out: clear drafts, replace to /signed-out
  A->>IdS: location.assign(redirectUrl) → /signout-callback-oidc → /signed-out
```

Sign-out waits for running saves first, then asks only about what is still at risk.

1. **At once.** The acting tab runs `TmBusyOverlay.run(…, { label, immediate: true })` (0023
   §5.4), where `label` is a signal that starts at "Signing out…". The scrim, spinner and label
   show in the click's frame, since no trigger stays on screen. It posts `drafts.query`. Every
   tab answers within 300 ms with
   `{ dirty: TmDrafts.dirtyCount(), busy: TmApi.writesInFlight() > 0 }` (0025 §6.2).
2. **Wait.** While any tab is busy, the label reads "Waiting for a save to finish…", and the tab
   re-asks every second, for at most the 60 s write watchdog (§10.3). The saving tab is not
   blocked, so it shows its save's outcome. The wait has no Cancel, and Escape does nothing.
3. **Decide on the fresh answers.**
   - Nothing dirty and nothing busy: sign out with no question.
   - A dirty draft, or a save still running after 60 s: the overlay closes and the confirm opens.
     A failed save shows up here as a dirty draft.
4. **Sign out.** The overlay shows "Signing out…" again. A confirmed sign-out never asks again: a
   save that starts while the confirm is open is waited for, with no second question. Then the
   acting tab, as the diagram shows:
   1. calls `TmTelemetry.flush()`;
   2. sets `state` to `signing-out` and posts `session.signing-out`;
   3. posts `/bff/logout`.

   So `state` stays `active` during the wait, and the tab's own store writes pass (0025 §2.5).

- **The confirm** is asked once, with Sign out and Cancel:

  | Still at risk | Title | Message |
  |---|---|---|
  | dirty drafts | "Sign out and discard unsaved changes?" | "You have unsaved changes in {count, plural, one {# tab} other {# tabs}}." |
  | a save still running | "Sign out while a save is running?" | "A save is still running. If you sign out now, you won't see whether it finished." |

  With both, the title is the first row's, and the message joins both sentences.
- **Signing out.** On `session.signing-out`, every tab of that `sub` sets `state` to
  `signing-out`, aborts its reads and shows the same overlay, with `immediate: true`. `TmApi`
  rejects new calls with `cancelled`, except the acting tab's `/bff/logout` write and `send`
  calls.
- **Failure.** On no answer or a non-200, `state` returns to `active`, a toast says "Couldn't sign
  out. Check your connection and try again." (0028 §8.1), and `signOut()` rejects. Other tabs
  re-check the cookie and lift their overlay after 15 s without `session.signed-out`.
- **Clean-up.** Spec 0025 §2.6's session clean-up, then `TmDrafts.clearAll(sub)`.

Telemetry: `tm.session.signed-out { origin: 'self' | 'other-tab' | 'elsewhere' }`.

### 8.5 Signed out elsewhere

The hub's `session.ended { session-terminated }` reaches the leader tab (0025 §8.5), which posts
`session.signed-out`. Every tab then runs §8.4's clean-up (the store sweeps once), deletes the
stale profile cookie (`tellma.profile=; Max-Age=0; Path=/; Secure`) and replaces to `/signed-out`.

### 8.6 Expiry path

1. **Trigger:** `401 unauthenticated`, or `check()` returns `missing` mid-session.
2. **The tab** runs spec 0025 §2.6's session clean-up for the expiry path, deletes a stale profile
   cookie, sets `state` to `expired` and posts `session.expired`. The expiry path keeps the drafts
   and the boot record's `sub` and `lastTenantId`; only sign-out clears those two (§8.4, §8.5). So
   when a tab closes at the session-ended dialog, a later launch at `/` still returns to the last
   tenant (§4.2 step 3c).
3. **Every tab of that `sub`** keeps its drafts and shows the session-ended dialog (`alertdialog`,
   focus on the button): "Your session ended" / "Sign in to continue. Your work is kept." with a
   Sign in button.
4. **Sign in** calls `signIn()` from that tab. Back from sign-in, its entry is cold (§4.4), since
   the clean-up swept the launch documents. It restores its draft (0031 §11) and posts
   `session.started`. The other tabs of that `sub` reload and restore theirs.

Telemetry: `tm.session.expired { source: 'boot' | 'cookie' | '401' }`. For `boot`, `tm-boot.js`
writes the sign-in mark with `expired: true` (§4.2), and the app reports it after the next
sign-in.

### 8.7 Changed-user path

This path runs when the profile cookie's `sub` differs from the tab's: `check()` sees it, or
`session.started` or `session.changed` announces it. The tab discards what it was receiving, clears
its drafts, posts `session.changed` and calls `location.replace('/')`. The boot then sweeps every
other user's data (0025 §2.6). Telemetry: `tm.session.changed-user`.

### 8.8 Re-check triggers

`check()` runs on `visibilitychange` to visible, and on `pageshow` with `persisted`, since the
back-forward cache gets no broadcast. It also runs around every authenticated `TmApi` call (§10.3)
and before every hub negotiate (0025 §8.2).

## 9. Routes and URLs

One route grammar serves the router, `TmUrls`, pins, recents, notification targets and server-built
links. `provideTellma` derives the routes from the declarations. `/tenants` is the picker, `/` the
launch redirect and `/{t}` the tenant home.

### 9.1 Grammar rules

| Rule | Detail |
|---|---|
| Reserved words | one source, `TellmaRoutes` (§3.2), emitted into `metadata.routing`; the router, `tellma-client` (collision check, proxy, worker config) and spec 0011 §5.3's audit read it |
| `new` | reserved below an entity segment |
| Parameters | `{t}` is a positive integer without leading zeros (`/017/centers` is the tenantless not-found page); `{id}` is a positive integer; `{segment}` is the entity's API resource segment |
| Canonical form | lowercase literal segments, no trailing slash (`/17/` replaces to `/17`), no case folding (`/17/Centers` is not found) |
| No dots | the worker skips a last segment with an extension, so page paths and coded-report keys are kebab-case, and a coded-report key contains a hyphen |
| Not in the URL | edit mode and import. Create mode is in the URL: `/{t}/{segment}/new` is a deep link like any other, pinnable and restored on reload. |

**Example —** in Acme's tenant 17, `/17/centers` searches Centers, `/17/centers/5` opens Center 5,
`/17/centers/new` creates one, and `/17/credit-review` is a `tmPage`.

### 9.2 Route assembly

`provideTellma` builds one route table at provider time, from the declarations alone. Segment
lookups happen at match time, after the boot initializers have loaded the metadata.

```text
''              TmLaunchRedirect              canMatch runs §9.3; renders nothing
'tenants'       /shell: tenant picker         0027 §6.2
'signed-out'    /shell: signed-out page       0027 §6.3; the one route without tmSessionGuard
':t'            /shell: tenant shell          a positive integer; tmSessionGuard
  ''              TmHomeHost                    0035 §15
  'settings', 'settings/:category', 'me', 'me/notifications'   0032 §7, §8; 0028 §10.3
  'list/:segment'                              the drill-through list: the search host with a fixed filter (0030, 0034 §8.3); a stack or child segment
  'reports/:key'                               coded reports (0034 §2.2)
  <page paths>                                 tmPage routes, in feature order (§2.4): 'reports/:id/studio' (0034), 'notifications/:id/open' (0028 §10.2), …
  ':segment'                                   search (0030); canMatch: TmEntities.bySegment(segment)?.kind === 'stack'
  ':segment/new', ':segment/:id'               details: create, view (0031), with the same canMatch; ?clone=, ?child=Collection:id
  '**'            the tenant not-found page
'**'            the tenantless not-found page
```

- **Page routes.** Every page route has `TmRouteHost` as its component, a `TmRouteData` (§4.4),
  and a `canDeactivate` that runs the leave checks registered in its page scope (`tmLeaveGuard`,
  0031 §11.3).
- **Platform pages** such as Jobs, Notifications, Reports and Dashboards are entity routes whose
  declarations set members and hatches (0028, 0032, 0034, 0035).
- **Under an entity segment**, the coded reports come before the generic routes. So do the pages a
  feature declares under the segment of an entity it declares itself (`reports/:id/studio`,
  `notifications/:id/open`). Any other page path starting with an entity segment is
  `reserved-path`.
- **Router options:** shells by `loadComponent: () => import('@tellma/core/shell')`;
  `withComponentInputBinding()`; `paramsInheritanceStrategy: 'always'`;
  `canceledNavigationResolution: 'computed'`, so a cancelled Back restores the URL;
  `TmTitleStrategy`; `withNavigationErrorHandler`, which sends chunk errors to §4.4; and no
  blocking initial navigation.
- **`lastTenantId`.** Entering a tenant route writes `boot.patch({ lastTenantId })`. A
  `404 tenant-not-found` from that tenant clears it (§11).

### 9.3 The launch redirect (`/`) and automatic entries

The platform picks the tenant whenever the user did not: the BFF after a sign-in to `/`,
`tm-boot.js` on a warm `/` or after an expiry (§4.2), and the SPA at `/`. Each pick marks the entry
automatic, so a tenant that no longer exists leads to the picker without a notice.

- **After a sign-in to `/`**, the BFF applies the single-membership rule below and redirects
  straight to `/{id}` (spec 0011 §5.5). Deep links, `/tenants` and step-up returns pass through.
- **Angular reaches `/`** on a cold launch with no last tenant, on an in-app link to `/`, or after a
  changed user (§8.7). `TmLaunchRedirect` then decides:

| Condition | Result |
|---|---|
| `boot.lastTenantId` set and `boot.sub` equals the profile's `sub` | replace to `/{lastTenantId}` |
| otherwise | read 0025's `tenants` document (`/bff/user`, stale while revalidating) under the tenantless shell's skeleton list |
| `tmSingleLiveTenant(tenants)` names a tenant | replace to `/{id}` |
| none, several, or `/bff/user` failed | replace to `/tenants`; the picker shows its own error state |

```ts
declare function tmSingleLiveTenant(tenants: readonly TmMembership[]): number | null; // the one Live, Active, isActive membership; sandboxes never count
```

- **One rule, two places.** The BFF's `TenantLanding.SingleLiveTenant` (spec 0011 §5.5) and
  `tmSingleLiveTenant` read one vector file, `launch-landing-vectors.json` in `@tellma/core`
  (`src/launch/`), which the C# test project links. Each row is `{ name, tenants, expected }`.
- **Marking.** When `TmLaunchRedirect` replaces `/` with a tenant, by either replace row, it calls
  `TmLaunch.markAutomatic`.

`TmLaunch` holds the mark for the current document:

```ts
declare class TmLaunch {                     // @tellma/core primary; seeded from __tmBoot.auto (§4.2)
  readonly automatic: Signal<number | null>; // the tenant this document entered without the user choosing it
  markAutomatic(tenantId: number): void;     // TmLaunchRedirect, when it replaces '/' with a tenant
  settle(tenantId: number): void;            // TmApi, on the first 2xx from that tenant (§10.3)
  takeAutomatic(tenantId: number): boolean;  // TmErrorPresenter on tenant-not-found: true once, then cleared
}
```

| Where the mark lives | How long | Who consumes it |
|---|---|---|
| the sign-in mark in session storage | across the sign-in round trip, until the next boot's step 4 | `tm-boot.js` (§4.2) |
| `__tmBoot.auto` | one document load | `TmLaunch`, at its first injection |
| `TmLaunch.automatic` | until the first 2xx from that tenant, a route into another tenant, or a 404 | `TmErrorPresenter` (§11) |

A `404 tenant-not-found` on an automatic entry goes to the picker silently, and on any other entry
the picker shows its notice (§11). The fallback is always the picker, which never redirects, so a
stale membership hint cannot loop.

**Example —** the flows:

| Case | Lands on | Automatic |
|---|---|---|
| Warm launch at `/`, last tenant 17 | `/17`, by `tm-boot.js`'s rewrite | yes |
| Cold launch at `/`, valid cookie, no last tenant | one live tenant → `/1`; else `/tenants` | yes |
| Expired at `/`, last tenant 17 | sign-in, then `/17` | yes |
| Expired at `/`, no last tenant, one live tenant | sign-in; the BFF lands on `/1` | yes |
| Expired at `/17/centers` | sign-in, then `/17/centers` | no; yes when another user signs in |
| Sara signs in on a shared device after Farah's session expired at `/` | `/17`, Farah's last tenant, when Sara is a member; else the picker, silently | yes |
| A followed link to `/99` (email, pin, typed URL) or a reload | `/99`; a 404 shows the notice | no |
| The signed-out page's Sign in | `/`; the BFF may land on a tenant | when it does |

An explicit sign-out clears `lastTenantId` (0025 §2.6), because the device may change hands. Only
an expiry returns to the last tenant.

### 9.4 `TmUrls`

```ts
interface TmTenantUrls {                                          // the builders of one tenant's routes
  home(): string;                                                 // '/17'
  search(entity: string, query?: TmSearchQuery): string;          // '/17/centers?q=jed'
  details(entity: string, id: TmId, child?: { collection: string; id: TmId }): string; // '/17/centers/5?child=Lines:42'
  create(entity: string, options?: { clone?: TmId }): string;     // '/17/centers/new?clone=5'
  page(path: string): string;                                     // page('me') → '/17/me'; page('settings/general')
  target(resource: string, id: TmId): string | null;              // the details page when the entity has one
  blob(kind: string, id: number, options?: { variant?: string; download?: boolean }): string; // '/17/blobs/customer-image/88?variant=thumb'
}
declare class TmUrls implements TmTenantUrls {                    // its builders use the routed tenant
  readonly tenantId: Signal<number | null>;                       // the tenant the routed page shows
  forTenant(tenantId: number): TmTenantUrls;                      // links into any tenant: forTenant(18).home() → '/18'
  parseSearch(params: URLSearchParams): TmSearchQuery;
  picker(options?: { notice?: 'tenant-not-found'; from?: number }): string; // '/tenants?notice=tenant-not-found&from=17'
  signedOut(): string;                                            // '/signed-out'
  describe(url: string): TmRouteMatch | null;                     // classifies a stored URL (pins, recents) by §9.2's routes
  preload(url: string): void;                                     // loads the route's chunks at idle (hover, focus, pins)
}
interface TmRouteMatch {
  readonly kind: 'launch' | 'picker' | 'signed-out' | 'home' | 'search' | 'details' | 'create' | 'page' | 'core' | 'report' | 'list';
  readonly tenantId?: number; readonly entity?: string; readonly id?: TmId;
  readonly template: string;                                      // '/:t/centers/:id'
  readonly pinnable: boolean;                                     // every tenant route except 'list'
}
```

- **One tenant rule.** Every builder of `TmUrls` uses `tenantId()`. A link into another tenant,
  such as the picker's Connect or a sandbox's "Open {live name}", uses `forTenant(id)`.
- **Outside a tenant**, where `tenantId()` is null, a `TmUrls` builder throws in development. In
  production it returns `/tenants` and emits `tm.urls.no-tenant`.
- **Entity names map to segments** through the Core chunk's summaries. An unknown entity throws in
  development and yields the tenant home in production.
- **`target`** follows the server's `ITellmaLinks.Target` rule (spec 0011 §5.10), so a
  notification opens the same page in the app and from an email.

### 9.5 The `TmSearchQuery` codec

Spec 0030 §4 owns what a search page's query state means and how it combines with page state and
preferences. This codec owns its URL. No spec adds a search-page parameter outside `x.{key}`.

```ts
interface TmSearchQuery {
  tab?: string; q?: string;
  columnFilters?: Readonly<Record<string, string>>;  // the f.{path} parameters: path → '{op}:{value}'
  filter?: string; inactive?: boolean;
  custom?: Readonly<Record<string, string>>;         // the x.{key} parameters
  order?: string; cols?: readonly string[];
  page?: number; size?: number; view?: 'grid' | 'tiles'; list?: boolean;
}
```

| Parameter | Meaning | Example |
|---|---|---|
| `tab` | the page's tab (0030 §2) | `tab=mine` |
| `q` | search text | `q=jed` |
| `f.{path}` | one column filter, `{op}:{value}` | `f.CenterType=in:Service,Sale` |
| `filter` | advanced Queryex text | `filter=CreditLimit%20%3E%20100000` |
| `inactive` | include inactive rows | `inactive=1` |
| `x.{key}` | a distribution's custom filter | `x.overdue=1` |
| `order` | the sort column, `:desc` for descending | `order=Name:desc` |
| `cols` | columns; a navigation means its display name, a multilingual group its active slot | `cols=Code,Name,Parent` |
| `page`, `size` | 1-based page, page size | `page=2&size=100` |
| `view`, `list` | grid or tiles; a tree shown as a flat list | `view=tiles&list=1` |

- **Canonical order.** Parameters follow the table's order, with `f.*` sorted by path and `x.*` by
  key, so a query yields one URL. Defaults are omitted: `page=1`, `inactive=0`, `list=0`, an empty
  `q`, and `tab`, `view`, `cols` and `size` when they equal the page's default.
- **Encoding.** `encodeURIComponent`, then `,`, `:` and `@` restored. Spaces are `%20`, and
  decoding accepts `+`. Values are culture-free: Latin digits, `.` decimals, ISO `yyyy-MM-dd` dates
  in every calendar, `true` and `false`, and ids for navigations.
- **`f.{path}`.** `path` matches `^[A-Za-z][A-Za-z0-9]*(\.[A-Za-z][A-Za-z0-9]*)*$`. `op` is one
  of `contains`, `starts-with`, `ends-with`, `eq`, `ne`, `lt`, `le`, `gt`, `ge`, `between`, `in`,
  `before`, `after`, `is`; 0029 §13 says which apply to which type. Text operators take the whole
  remainder; `in` and `between` split on `,`, with `\,` and `\\` as escapes.
- **Parsing is lenient.** An invalid entry is dropped and reported in development. An invalid
  `filter` is kept, and the server reports it inline (0030 §5.3).

**Example —** parsing three query strings:

| URL query | `TmSearchQuery` |
|---|---|
| `q=jed&f.Name=contains:a,b&order=Code` | `{ q: 'jed', columnFilters: { Name: 'contains:a,b' }, order: 'Code' }` |
| `tab=mine&f.CreatedAt=between:2026-01-01,2026-03-31&view=tiles&list=1` | `{ tab: 'mine', columnFilters: { CreatedAt: 'between:2026-01-01,2026-03-31' }, view: 'tiles', list: true }` |
| `page=0&view=cards` | `{}` (both dropped) |

### 9.6 Server-built links

Server code builds every link it sends through `ITellmaLinks` (spec 0011 §5.10). `Tenant(t)`
gives `{PublicOrigin}/{t}`, and `Target(t, resource, id)` gives §9.2's details route, else the
tenant home. Invitations (spec 0018 §3.3) and notification emails (spec 0022 §4) use them.

## 10. The HTTP layer

Every request the SPA makes goes through `TmApi`, over `fetch`, except uploads over
`XMLHttpRequest`. Reads are cancellable and retried by class; writes are never aborted or retried.
A user check before and after each call keeps one user's data out of another's session.

### 10.1 `TmApi`

```ts
interface TmReadOptions {
  signal: AbortSignal;        // required: every read is cancellable
  background?: boolean;       // sends Tellma-Activity: background; no dialog
  tenantId?: number;          // default: the routed tenant (§9.4)
  method?: 'GET';             // GET for '/bff/user', '/api/distribution-info'
  long?: boolean;             // only for a route outside the Contracts chunk: the watchdog allows 6 min (§3.4)
}
interface TmWriteOptions {    // no signal: a write is never aborted
  tenantId?: number;          // default: the routed tenant (§9.4)
  long?: boolean;             // only for a route outside the Contracts chunk: the watchdog allows 6 min (§3.4)
}
interface TmUploadOptions { fileName: string; signal: AbortSignal; onProgress?: (fraction: number) => void; tenantId?: number }
interface TmDownload { readonly fileName: string; readonly contentType: string; readonly blob: Blob }
declare class TmApi {
  read<T>(route: string, body: unknown, options: TmReadOptions): Promise<T>;
  write<T>(route: string, body: unknown, options?: TmWriteOptions): Promise<T>;
  download(route: string, body: unknown, options: TmReadOptions & { onProgress?: (bytes: number) => void }): Promise<TmDownload>;
  upload(kind: string, file: Blob, options: TmUploadOptions): Promise<TmBlobDescriptor>; // 0017 §3.1's BlobDescriptor; its long size is a TmDecimal
  send(route: string, body: unknown, options?: { background?: boolean }): void;         // keepalive, unread, ≤ 16 KB
  readonly online: Signal<boolean>;
  readonly writesInFlight: Signal<number>;                                              // this tab's unsettled writes
}
type TmJson = null | boolean | number | string | readonly TmJson[] | TmJsonObject;
type TmJsonObject = { readonly [key: string]: TmJson };
declare const tmDecimalBrand: unique symbol;
type TmDecimal = string & { readonly [tmDecimalBrand]: true }; // a decimal or long as its exact wire text: '123456789012.3456'
type TmId = number | TmDecimal;                                 // any entity's id: an int key is a number, a bigint key a TmDecimal
declare const TmDecimals: {
  parse(text: string): TmDecimal | null;                        // canonical text ('1e3' → '1000'); null when not a number
  isDecimal(value: unknown): value is TmDecimal;
  add(a: TmDecimal, b: TmDecimal): TmDecimal; subtract(a: TmDecimal, b: TmDecimal): TmDecimal; // exact, on the text
  compare(a: TmDecimal, b: TmDecimal): -1 | 0 | 1;
  toNumber(value: TmDecimal): number;                           // lossy, for charts
  fromNumber(value: number): TmDecimal;                         // an editor's value, within 15 significant digits
};
type TmApiErrorKind = 'cancelled' | 'offline' | 'unauthenticated' | 'step-up' | 'forbidden' | 'not-found'
  | 'tenant-not-found' | 'tenant-unavailable' | 'read-only' | 'validation' | 'query-invalid' | 'bad-request'
  | 'conflict' | 'limit' | 'client-outdated' | 'unavailable' | 'partial-failure' | 'server';
declare class TmApiError extends Error {
  readonly kind: TmApiErrorKind; readonly status: number;          // 0 for client-side kinds
  readonly code: string; readonly traceId: string; readonly problem: TmProblem | null; readonly retryAfterMs: number | null;
}
type TmProblem = TmJsonObject & { readonly code: string; readonly instance: string; readonly status: number }; // 0016 §7.2's body
type TmProblemError = TmJsonObject & { readonly code: string; readonly message: string };            // one errors[] item (0016 §7.3)
```

- **Routes.** `'centers/query'` becomes `/{t}/api/web/centers/query`. A route with a leading `/`
  is tenantless and sent as is. Every call is a `POST`, except a `read` with `method: 'GET'`.
- **No tenant.** A tenant route with no `tenantId` and no routed tenant is a programming error. It
  throws in development; in production it rejects with kind `server`, code `no-tenant`, without
  sending.
- **`read` or `write`.** `read` serves spec 0016 §2.2's retried class: a `GET` route, or a route
  the Contracts chunk lists as `idempotent` (§3.4). `read` on any other mapped route throws in
  development and runs without retry in production. `write` serves everything else. A read route
  called through `write` works, without retry or abort.
- **`upload`** is the client's one `XMLHttpRequest`, because `fetch` reports no upload progress.
  It posts the raw file to `/{t}/blobs/{kind}?fileName=` (spec 0017 §8.1) with `Tellma-Client` and
  `traceparent`, and maps `upload.onprogress` to `onProgress`. It aborts on the signal, applies the
  same user check and status mapping, and never retries.
- **`download`** streams the `.xlsx` body, reports bytes, and names the file from
  `Content-Disposition` (`filename*` first).
- **`send`** is `fetch` with `keepalive: true`, for the `pagehide` preference flush and telemetry
  (0026 §3.3). `background` adds `Tellma-Activity: background`. Closing tabs share a 64 KiB
  keepalive budget, so a body over 16 KB throws in development and is dropped in production.

### 10.2 Headers

| Header | Value | On |
|---|---|---|
| `Tellma-Client` | `web/<metadata.distribution.version>` | every call |
| `Tellma-Contract` | the route's fingerprint from the Contracts chunk | tenant calls whose route is in the map |
| `Accept-Language` | §7.7 step 4's string from the `language`, `region` and `digits` signals (`ar-SA`, `ar-SA-u-nu-latn`), never the browser check's fallback | every call |
| `Tellma-Calendar` | `TmLocalization.calendarCode()` | tenant calls |
| `traceparent` | `00-<32 hex>-<16 hex>-01`, fresh per call (`crypto.getRandomValues`) | every call |
| `Tellma-Activity` | `background` | `background` reads and sends |
| `Content-Type` | `application/json`; an upload sends the file's type | calls with a body |

- **No `Tellma-Time-Zone`.** The server's preference-then-tenant rule decides the zone (0016 §6.1),
  and the client formats instants in the same zone (§7.1).
- **The trace id exists before the response**, so a cancelled or failed call still has a reference
  (0026 §8.1).

### 10.3 One call, step by step

1. **User check.** For an authenticated call, `TmSession.check()` runs first. On `missing`, the
   call is not sent, the expiry path runs (§8.6) and the call rejects `unauthenticated`. On
   `changed`, the call is not sent, the changed-user path runs (§8.7) and the call rejects
   `cancelled`.
2. **Offline write.** A write while `navigator.onLine` is false is not sent. It rejects `offline`
   with code `not-sent`.
3. **Send.** `fetch` runs with `credentials: 'same-origin'`. One `AbortController` follows the
   caller's signal and the watchdog: 60 s, or 6 min for a long route (§3.4) or with `long`.
   `TmRequestLog.begin` records the call as pending (0026 §8.1). The watchdog ends a black-holed
   call: on a write the outcome is unknown, so it rejects `offline` with code `outcome-unknown`,
   while a persist that reached the server completes there (spec 0012 §6.1).
4. **Response check.** `check()` runs again. On `missing`, the expiry path runs and the call
   rejects `unauthenticated`. On `changed`, the body is discarded, the changed-user path runs and
   the call rejects `cancelled`. Only a checked response continues.
5. **Observers.** The request-log handle's `end` records the outcome, status, code and
   `Tellma-Build`. The first 2xx from a tenant calls `TmLaunch.settle(t)` (§9.3).
   `Tellma-Version-Tags` go to `TmCacheRefresher.observe(t, tags)` (0025 §5.3) one task after step
   6 delivers the body, so a write's caller adopts its own result first.
6. **Status.** The status maps to a kind per §10.6; a success body parses per §10.4.

### 10.4 Plain JSON

Bodies parse with `JSON.parse` and serialize with `JSON.stringify`, with no reviver and no
replacer. Spec 0016 §3.2 sends every value a JavaScript number cannot hold exactly as a JSON
string, so nothing is lost.

| Wire type | JSON | Client type |
|---|---|---|
| `int`, `short`, `byte`, `double` | number | `number` |
| `decimal`, `long` | string at full scale, never an exponent: `"1234.5000"`, `"9007199254740993"` | `TmDecimal` |
| dates, instants, GUIDs | string, never converted: `modifiedAt` stays the raw string, and no `Date` appears | `string` |
| an absent member | — (spec 0016 omits nulls) | `null` |

- **Outbound.** A `TmDecimal` serializes as its string. The server also accepts a number for a
  `decimal` or `long` (spec 0016 §3.2), so a script may send `1234.5`.
- **Arithmetic.** `+` on a `TmDecimal` would join text, and `<` would compare it as text.
  TypeScript already refuses `-`, `*` and `/` on a string. Two lint rules cover the rest, in
  platform and distribution code (§2.1, §12):
  - `tm/no-decimal-operators` (type-aware, in `@tellma/core-tools/eslint`) refuses `+`, `+=`,
    `<`, `<=`, `>` and `>=` when an operand is a `TmDecimal`, so `total + amount` fails;
  - typescript-eslint's `restrict-plus-operands` refuses a string added to a number.

  Sums use `TmDecimals.add`, and comparisons `TmDecimals.compare`.
- **Display.** `Intl.NumberFormat.prototype.format` formats the string exactly (spec 0029 §12).
  Editors hold a `number` within spec 0005's 15 digits and convert with `TmDecimals`.
- **Storage.** Strings survive `structuredClone`, `postMessage` and IndexedDB, so the store,
  drafts and the tab bus need no special case.

### 10.5 Cancellation by type and retries

| Request | Signal | Cancelled by | Retried (0016 §2.2) |
|---|---|---|---|
| page data (`resource()`) | its `abortSignal` | navigation away; a new query (text, filter, sort, page) | yes |
| keystroke reads (search field, picker, `queryex/discover`) | one per search the field fires (0023 §6.2) | the next one | yes, unless superseded |
| long reads (export download, report run) | the page's and a visible Cancel | Cancel, navigation | yes |
| background reads (polling, refresh) | the owner's lifetime | the owner's destruction | yes |
| uploads | the field's | removing the file, navigation | never |
| writes, persist round trips, `send` | none | never | never |

Only reads retry, on the failures below. An abort ends any wait at once, the page stays in its
loading state meanwhile, and each retry emits
`tm.api.retry { reason: network | 429 | 503 | server-outdated }`.

| Read failure | Wait before each retry | Retries |
|---|---|---|
| network failure, while `navigator.onLine` | 1 s, then 3 s (±25 % jitter) | up to 2 |
| `429` | `Retry-After`, capped at 10 s | up to 2 |
| `503` with `Retry-After` | `Retry-After`, capped at 30 s | up to 2 |
| `412` or `404 route-not-found` judged `server-outdated` (§5.6) | 30 s | 1 |

- **An abort's path:** `abort()` → `fetch` aborted → Kestrel's `RequestAborted` → the batch's
  token → a SqlClient attention (0016 §8.3). The client rejects `cancelled`, shows nothing and
  never retries.
- **`online`** is false while `navigator.onLine` is false or after a network-level failure. A
  visible tab then probes `GET /health/live` every 5 s, backing off to 30 s; any HTTP answer (even
  a 429) or the next successful call sets it true. 0028 §9.5 shows the `tm.offline` banner from it.
- **`TM_BLOB_FETCHER`** is a `fetch` `GET` with `If-None-Match`, the caller's signal (0010 §D.6)
  and §10.3's user check. It is never retried; blob URLs come from `TmUrls.blob`.

### 10.6 Status to kind

| Response | `kind` (`code`) | Presented by |
|---|---|---|
| `fetch` aborted by the caller | `cancelled` | nothing |
| network failure; the watchdog fired | `offline` (`network`; on a write or the watchdog: `outcome-unknown`) | a read: inline; a write: 5xx dialog |
| 400 `query-invalid` | `query-invalid` | inline (0030 §5.3) |
| 400 `bad-request`; 403 `tenant-read-only`; 413 `limit-exceeded` | `bad-request`; `read-only`; `limit` | 400 dialog |
| 401 `unauthenticated` | `unauthenticated` | the expiry path (§8.6) |
| 401 `step-up-required` | `step-up`, the challenge parsed | the step-up dialog (§8.3) |
| 403 `forbidden` | `forbidden` (`arguments.reason`: `no-grant`, `outside-filter` or `restricted-path`) | a read: inline; a write: 400 dialog |
| 403 `tenant-suspended`; 404 `tenant-not-found` | `tenant-unavailable`; `tenant-not-found` | the tenant-state page; the picker with a notice (§11) |
| 404 `route-not-found`, 412 `client-outdated` | `client-outdated` (`client-outdated` or `server-outdated`); `server` (`contract-mismatch`) when the build is the same | §5.6 |
| 404 `not-found` | `not-found` | a read: inline (0031 §3.5); a details save: the deletion dialog (0031 §9.2); another write: 400 dialog |
| 409 `concurrency-conflict`, `count-mismatch` | `conflict` | 0031 §9's dialog, else 400 dialog |
| 422 `validation`; `blob-rejected` at any status | `validation` | 0031 §8.5's fields or the uploading field, else 400 dialog |
| 429 `rate-limited`; 503 (any code); 504 `timeout` | `unavailable` (that code) | a read: inline; a write: 5xx dialog |
| 502 `partial-failure` | `partial-failure` | the caller, else 400 dialog |
| 403 `csrf-rejected`, `individual-required`; 405; 415; 500; anything else | `server` | 5xx dialog |

- **Trace id:** `Tellma-Trace-Id`, else `problem.instance`, else the client's `traceparent` id.
- **Non-JSON bodies.** A body that is not JSON (a proxy's HTML page) is `server` with code
  `unexpected-response`.
- **Who knows HTTP.** Only `TmApi` and `TmErrorPresenter`: `TmApi` classifies 401, step-up, 412 and
  503, and `TmErrorPresenter` presents them. Every other component sees only `TmApiError`.

## 11. Error presentation

One table turns a `TmApiError` into an error view. Page frameworks render the view inline for reads;
`TmErrorPresenter` renders it as one of two shared dialogs for writes and actions (§10.6's last
column). Page specs cite this section for these states and define only their own.

```ts
interface TmErrorView {
  readonly tone: 'error' | 'warning' | 'info'; readonly icon: string;      // a registry name (0023 §3)
  readonly title: Signal<string>; readonly message: Signal<string>;
  readonly items: Signal<readonly string[]>;               // server errors, rendered by TmServerText
  readonly traceId: string | null;                         // "Reference …" with Copy
  readonly actions: Signal<readonly ('retry' | 'reload' | 'reload-anyway' | 'sign-in' | 'continue' | 'switch-tenant' | 'close')[]>;
}
interface TmErrorContext { readonly kind: 'read' | 'save' | 'action'; readonly entity?: string }
declare class TmErrorPresenter {
  describe(error: TmApiError, context: TmErrorContext): TmErrorView;
  present(error: TmApiError, context: TmErrorContext & { retry?: () => void }): Promise<void>;
}
// <tm-error-state [view]="…" (retry)="…" /> — the inline state: 0023's tm-empty-state with a tm-reference
```

| Kind (code or reason) | Title | Message | Actions |
|---|---|---|---|
| `offline` (`network`) | "You're offline" | "Check your connection. We'll keep trying." | retry |
| `offline` (`not-sent`) | "You're offline" | "Your changes are kept. Save again when you're back online." | close |
| `offline` (`outcome-unknown`) | "We couldn't confirm this" | "The connection dropped before {displayName} answered. Check whether it went through before you try again." | close |
| `forbidden` (`no-grant`) | "You don't have permission for this" | "It needs “{permission}”. Ask your administrator to grant it." | — |
| `forbidden` (`outside-filter`) | "Your access doesn't cover this record" | "Your “{permission}” permission covers only some records. Ask your administrator if you need this one." | — |
| `forbidden` (`restricted-path`) | "You can't see {field} here" | "It needs full “{resource}: Read” access. Ask your administrator to grant it." | — |
| `not-found` | "This {entity} doesn't exist or you can't see it" | — | — |
| `unavailable` | "{displayName} is busy" | "Try again in a moment." | retry |
| `client-outdated` (`client-outdated`) | "{displayName} was updated" | "Reload to continue. Your changes are kept." | reload |
| `client-outdated` (`server-outdated`) | "{displayName} is being updated" | "Try again in a minute." | retry |
| `server` | "Something went wrong" | "{displayName} couldn't finish this. Try again. If it keeps happening, send this reference to support." | retry |

- **`forbidden` names the permission** from the 403's `arguments` (spec 0016 §7.1).
  - `{permission}` is `TmPermissions.label(resource, action)` (0029 §7), such as "Sales invoices:
    Create and edit". It is the label the Access tab shows (0032 §4.6).
  - `{resource}` is the resource's label alone.
  - `{field}` is the label of `arguments.path`, the related column beyond the projection. The path
    starts at the query's root, which is the context's `entity`. So `{field}` is
    `TmEntities.resolve(entity, path).label` (0029 §4.3), a slash label. Without an `entity`, or
    when the path does not resolve, the raw path shows.
  - The details and the inline reference line add the raw pair in mono, isolated LTR, for support:
    `acme.SalesInvoice · Save`.
  - Roles, filters and other users never appear. `access/check` (spec 0014 §10) answers those.
- **Reload** on `client-outdated` runs `TmUpdates.update()` (§5.5):
  1. The button turns busy in the click's frame, labelled "Getting the update…" at a fixed width.
  2. `ready`, or `disabled` with no worker in the way, reloads at once.
  3. Any other result, usually `failed` or `unreachable`, keeps the dialog or state with the
     message "Couldn't download the update. Check your connection and try again."
  4. After a second failure, a "Reload anyway" action does a plain `reload()`, which the degraded
     worker sends to the network. Drafts survive every reload (0025 §11).
- **`present` on reads** handles only the kinds that cross pages. Other read kinds return without
  UI, and the page renders `tm-error-state` from `describe`.
  - `tenant-not-found` runs `TmClientStore.sweep({ tenant: t })` (0025 §2.6), clears a matching
    `lastTenantId`, and replaces to the picker:
    - on an automatic entry (`TmLaunch.takeAutomatic(t)`, §9.3), to `TmUrls.picker()`, with no
      notice;
    - otherwise to `TmUrls.picker({ notice: 'tenant-not-found', from: t })`, whose notice 0027
      §6.2 renders.

    A warm entry's 404 follows the same rule, and the picker's badges explain a removed
    membership. Telemetry: `tm.launch.tenant-gone { automatic }`.
  - `tenant-unavailable` shows the tenant-state page "{tenant} is suspended" / "Contact your
    administrator." with Switch tenant through `TmShellMain.replace(…)` (0027 §7.5).
- **Silent:** `cancelled`, `unauthenticated`, and any error from a `background` call.
  `TmErrorPresenter` calls `TmTelemetry.error` for the kinds 0026 §4.1 lists.
- **The 5xx dialog** is 0023's `TmErrorDialog` with the table's title and message. It is an
  `alertdialog`: focus starts on the primary button, Escape closes it, and focus returns to the
  invoker.
  - `reference`: the full 32-hex trace id; Copy turns to "Copied" for 2 s, announced politely.
  - `details`: status, code, time, route template, UI version.
  - `actions`: "Copy diagnostics" (`TmSupport.copyDiagnostics()`) and, when `TmSupport.canSend()`,
    "Report this problem" (`TmSupport.open({ error })`), both 0026 §12.1.
  - Buttons follow the view's `actions`: Reload and, after a second failure, Reload anyway for
    `client-outdated`; Try again (with `retry`) for the others; then Close.
- **The 400 dialog** shows:
  - the table's title and message for a kind the table lists, else the title from the problem
    (`Problem_<code>`), else "Couldn't complete this";
  - the items through `TmServerText`;
  - the reference, small, with Copy;
  - one Close button.
- **One at a time.** A second error while a dialog is open replaces its content with the latest;
  every error still reaches telemetry and the request log. Dialogs are full width below `sm` (0010
  §D.2), and references stay LTR inside RTL text.
- **Assistant seam.** An open error dialog registers an overlay page context with its
  `TmContextError` (0026 §9.1, §9.2), so the support dump and the future assistant see it.

## 12. Budgets and gates

This spec owns the app's bundle budgets and CI gates. Each package keeps its own entry-point
budgets (`"tellma".budgetsInKb`, spec 0002).

| Budget (gzip level 9) | Warn | Error |
|---|---|---|
| Initial JavaScript, with the modulepreloaded `/shell` and `/attention` | 230 KB | 260 KB |
| Initial CSS, fonts excluded | — | 40 KB |
| The search route beyond the initial set (`/search`, `/entity`, layout chunks) | — | 110 KB |
| The details route beyond the initial set (`/details`, `/entity`, layout chunks) | — | 80 KB |
| The ECharts engine chunk of `@tellma/core-ui/chart` | — | 170 KB |
| The worker's `shell` group | 1.5 MB | — |
| The `@tellma/core` primary's own weight | — | 30 KB |

- **Measuring.** `build`'s last step measures the budgets from `stats.json` and the output, and
  reports Brotli beside them. A figure over its error level is `budget-exceeded`, and over its warn
  level the warning `budget-warning`. A route's figure is the closure of the route host's dynamic
  import plus the distribution's layouts. A `@defer`red chunk is budgeted on its own line.
- **Raising a budget** takes a note in `package.json`, for example
  `"tellma": { "budgets": { "initialJs": { "error": 280, "note": "…" } } }`. A raise without a note
  is `budget-note-missing`.

| Gate | Runs | Pins |
|---|---|---|
| Lint (ESLint presets with the boundary rule, `tm/no-decimal-operators` and `restrict-plus-operands`; stylelint), typecheck, API goldens of every `@tellma/core` entry point | PR | the public API; the import rule (§1); no text arithmetic or comparison on a `TmDecimal` (§10.4) |
| `tellma-client build` on Acme; unit, harness and Playwright suites on the Acme host (Chromium; `@cross-engine` subsets on Firefox and WebKit), with axe | PR | §3.5, §3.6, §5.1, §6, the budgets; §13 |
| Installability (§5.2); Lighthouse cold on "Slow 4G" (LCP ≤ 4 s, warn); warm chrome ≤ 300 ms in Playwright with 4× CPU throttling, since Lighthouse resets storage | nightly | §5.2, warm launch |
| Staging smoke | every deployment | abort propagation (the server's cancelled counter moves), headers and CSP per response class, a worker update across a slot swap |

The client job runs on Ubuntu and Windows.

## 13. Testing

| Suite | Tier | Pins |
|---|---|---|
| `tmBoot(env)` vectors | unit, PR | every step of §4.2: the gate, cookies off, the loop guard, the expiry delete with its 1 s box and `oldVersion = 0` abort, 3d's three `returnUrl` cases, the `sign-in-failed` retry keeping the mark's `returnUrl`, step 4's two `auto` cases, the cold language order, the warm `/` rewrite and its mark, the display name as `document.title`, `/signed-out` |
| Profile cookie parser | unit, PR | v1, v2, padding, bad base64url, an empty `sub` |
| `tmCheckAppConfig`; the Node step; the build checks | unit; integration, PR | every config code of Appendix A, merge order, arrays replaced, each `hide` kind, `tmOverride`; Acme's `app.config.ts` loads through `tsx` with no browser; each source and output check code on an injected problem, with exit 3; a source problem stops the build before `ng build` |
| Route assembly, launch redirect, `TmUrls`, `TmSearchQuery` | unit, PR | §9.2's table; §9.3's conditions; `tmSingleLiveTenant` over `launch-landing-vectors.json`, which spec 0011's BFF tests share; `TmLaunch`: each way the mark ends; `forTenant`, the routed default and the no-tenant rule; `/017`, `/17/`, `/17/Centers`; §9.5's tables; `search(parseSearch(x))` is canonical `x` |
| Plain JSON, `TmDecimals` | unit, PR | §10.4's table through `JSON.parse` and `JSON.stringify` (`"9007199254740993"` stays exact); `parse` of `1e3`, `-0.0` and 30 digits; `add`, `subtract` and `compare` at 28 digits; `toNumber`; `fromNumber`; `tm/no-decimal-operators` refuses each of its operators on a `TmDecimal` and passes them on numbers and plain strings |
| `TmApi` | unit, PR | headers; the user check at both ends; §10.6 row by row (fake `fetch`); retries, the one 30 s retry on `server-outdated`, and an aborted wait (fake clock); the watchdog, and 6 min for a long route without the option; `read` on a non-idempotent route (development throw, production without retry); a tenant route with no tenant; an offline write; `send`'s cap; `upload` progress and abort (fake XHR); `begin`/`end`; `settle` on the first 2xx; `observe` one task after the body |
| `TmLocalization`, `TmUpdates` | unit, PR | §7.1's table; §7.7's resolution and locale strings, and the browser check's fallback, which leaves `Accept-Language` unchanged; switch atomicity; the locale write's adoption and retry; `check()` per result and `update()` with a fake `SwUpdate`; §5.6's precedence cases, the unordered case with its 3 s cap, and the rollback tie-break under the same cap |
| `TmDeviceZone`, the zone dialog | unit; harness, PR | each step of §7.8's check with a fake clock, locks and store: two tabs and one dialog, Keep with the box ticked, each failure; the dialog in `en` and `ar`, both themes, 390 and 1440 px, focus, Escape and axe |
| `TmErrorPresenter` | unit, PR | each `forbidden` reason with its labels and raw pair; Reload's busy state, the download failure and "Reload anyway"; `tenant-not-found` on an automatic entry and on a chosen one |
| Offered pairs | unit, `@cross-engine`, PR | every offered (language, region) pair resolves exactly in each engine, apart from the known gap list (`om`, `ti` and `so` on Chromium) |
| C# emit and hosting | unit; integration, PR | deterministic bytes and names with no database; `Tellma-Build` equals `distribution.version`; headers per response class; metadata chunks 401 without the cookie and `private` with it; the fallback matrix (navigate or fetch, reserved words, `/17/api/x` → `route-not-found`) |
| Playwright, Acme host | e2e, PR | the flows listed below |
| Responsive and RTL | e2e, PR | the boot page, dialogs and overlays at 390, 768, 992 and 1440 px in `en` and `ar` |
| Installability, Lighthouse, warm timing; staging smoke | nightly; deployment | §12 |

The Playwright flows on the Acme host:

- **Launch:** cold launch (one live tenant → `/1`; two → `/tenants`); a cold deep link; warm
  launch; the theme before first paint; the boot page with `navigator.locks` removed; `/signed-out`
  fetching no metadata chunk.
- **Automatic entries:**
  - expired at `/` with one live tenant → `/1`, and the tenantless skeleton never renders;
  - expired with a last tenant among two → that tenant;
  - an in-app expiry whose tab closes at the session-ended dialog, then a launch at `/` → the last
    tenant;
  - a shared device whose second user is not in it → the picker without a notice;
  - a typed `/99` → the notice.
- **Session:** the expiry delete; signed out elsewhere; a changed user beside a hidden tab; a
  step-up save. Sign-out across three tabs:
  - a running save that succeeds signs out with no question;
  - a save that fails shows the confirm;
  - a dirty idle tab shows the confirm at once;
  - every tab shows the overlay in the click's frame;
  - the telemetry batch leaves before `/bff/logout`.
- **Updates and network:** 412 with a newer and with an older server build; a 412 with the update
  download blocked shows "Couldn't download the update", then "Reload anyway"; the update banner;
  the offline banner; a hung request that does not block navigation.
- **Language, formats and errors:**
  - a language switch;
  - error dialogs through `fakeProblem`, one per `forbidden` reason;
  - `regional-formats.spec`: Acme in `ar` with Latin digits shows "1,234,567.89" and a
    Sunday-first date picker, and a My profile save re-renders every tab with one request.
- **Time zones:**
  - `device-zone.spec` (Chromium, the zone moved by CDP `Emulation.setTimezoneOverride`): a silent
    first run, a prompt after the idle gate, every tab re-rendered, the automatic toast;
  - `zone-labels.spec` (`@cross-engine`, `timezoneId` London against a Riyadh preference): labels
    present or absent per §7.8's table.

## 14. Definition of done

- **Packages.** `@tellma/core`, `@tellma/core-testing` and `@tellma/core-tools` build on Ubuntu and
  Windows with API goldens and §12's budgets; every §13 suite is green and the nightly gates pass.
- **Acme.** `tellma-client build` passes with zero problems.
- **Server pieces** this spec uses exist:
  - spec 0011's SPA endpoints (§5.1), post-sign-in landing (§5.5) and `emit-client-assets`
    (§6.1);
  - spec 0013's regions and digits;
  - spec 0015's `Long`, `Idempotent` and `ForbiddenException.Reason`;
  - spec 0016's string decimals (§3.2) and `IRouteContracts` (§3.10);
  - every contributor of §3.2.

| Rule | How this design meets it | Pinning test |
|---|---|---|
| Instant warm launch | the `shell` group holds every chunk, metadata file and string pack; `tm-boot.js` decides session, language, theme and target locally; initializers await only local reads (§4.3) | `warm-launch.spec`: after `warmUp()`, every route family shows chrome within 1 s with every API, BFF and hub request held, and offline; the first request starts after the shell renders |
| Instant feedback | the route skeleton in the click's frame (§4.4); hover and idle preloads; language, calendar, region and theme switch in place; the sign-out overlay and Reload's busy state in the click's frame (§8.4, §11); the outdated-client verdict from `Tellma-Build` at once (§5.6); dialogs open with no request | `route-feedback.spec` (the next frame shows the skeleton); `language-switch.spec` (no request, under 100 ms); the sign-out and 412 flows of §13 |
| Non-blocking | every read carries a signal (§10.5); aborts end retry waits; writes block navigation only during a save; the sign-out wait ends within 60 s; the zone prompt waits for 2 s without input | `cancel.spec` (staging: the cancelled counter moves); unit: an aborted retry wait ends at once; `device-zone.spec` |
| Stable layout | final-geometry skeletons; a cold entry holds the route skeleton until the launch documents arrive (§4.4); a single-tenant sign-in paints the tenant shell with no shell swap (§9.3); zone labels sit once in grid headers (§7.8); error states inside the content box; dialogs and overlays float; this spec moves no banner (0028 gates them) | `layout-shift.spec`: zero shift from skeleton to chrome and on error states; `cold-deep-link.spec`: a cold deep link to a details page renders the record, never a forbidden flash |
| RTL | `dir` before first paint; `<html dir>` flips live; references isolated LTR; logical CSS in the boot page and dialogs | every §13 e2e also runs in `ar`; `rtl-boot.spec`: the first paint is `dir=rtl` |
| WCAG AA | `alertdialog`s with focus start, return and Escape; focus to the title on a path change; the boot page is a landmark with a heading | axe on dialogs, the boot page and overlays (light, dark, LTR, RTL); `keyboard-errors.spec` |

- **Page context.** The route host publishes a `custom` context for `tmPage` routes (§4.4), and an
  open error dialog an overlay context (§11); every other context is its framework's.
- **Telemetry.** Request outcomes and error records are 0026's, fed by §10.3. This spec's own
  events are defined where they fire: `tm.launch.cold`, `tm.launch.warm` and `tm.boot.load-failed`
  (§4.3), `tm.launch.tenant-gone` (§11), `tm.config.problem` (§3.6), `tm.sw.*` (§5.4, §5.5),
  `tm.api.*` (§5.6, §10.5), `tm.urls.no-tenant` (§9.4), `tm.session.*` (§8.3–§8.7) and
  `tm.l10n.*` (§7.2, §7.7, §7.8).
- **What Acme adds** (its server side is spec 0018 §1.4):
  - `tmEnglish()` and `tmArabic()`, with the server's default regions `GB` and `SA`; tenant 1 on
    `gc` and `uq`, tenant 2 a sandbox (spec 0011 §5.7);
  - `acmeFeature` with the `sales` section, customers, sales invoices, the credit-exposure coded
    report and the Centers override from `@tellma/module-gl/centers`. The report's route
    `acme-reports/credit-exposure` is a projected `[ApiAction]` with `Long = true` (spec 0033 §11),
    so the Contracts chunk lists it and no call passes `long`;
  - `tmPage('credit-review')`, a work queue of customers near or over their credit limit, with the
    preference key `acme.creditReview.threshold` (0025 §9);
  - one `@layer tm.theme` delta and the platform's default brand files.

## Decisions record

| # | Decision | Why | § |
|---|---|---|---|
| 1 | **Routes are derived from declarations** | a route file per distribution drifts; runtime registration by features has order bugs | §9.2 |
| 2 | **Deployment facts are hashed JSON chunks the client fetches, emitted through the Migrator** | fast to parse, outside the JS budgets, never waiting on a route; the Migrator never references the Web project, so the esproj references it without a cycle | §3 |
| 3 | **One config check: a Node step in the build, a startup check in development** | declarations are plain data; a headless browser in `dotnet publish` and an AST scan were the alternatives | §3.6 |
| 4 | **Kind-qualified keys for `hide` and `overrides`; `hide` covers client-only kinds** | names, paths, keys and ids can collide, and patching sections and entries keeps the ids pins store; hiding an entity, widget type or coded report on the client alone is not access control, and the API, MCP and the assistant would still offer it | §2.3 |
| 5 | **A generated `tm-boot.js` that rewrites a warm `/`, with one boot page and a sign-in mark that guards the loop and records who chose the target** | it needs the offered languages before Angular, the CSP forbids inline script, and the first route is the real page | §4.2 |
| 6 | **`decimal` and `long` travel as JSON strings, and bodies parse with plain `JSON.parse`; the gate is Web Locks, `BroadcastChannel`, IndexedDB** | exact for every consumer, scripts and agents included, with no parser to keep and native parse speed, as RFC 7493 recommends; a lossless number parser was the alternative | §10.4 |
| 7 | **The worker registers through a `TrustedScriptURL`** | Angular's registration passes a string, which Trusted Types refuse; a `default` policy would pass every sink | §5.4 |
| 8 | **Read or write is the caller's method; reads require a signal; uploads use `XMLHttpRequest`; the Contracts chunk lists long and idempotent routes** | the compiler enforces cancellability, and `fetch` reports no upload progress; the server already decides which routes are long or safe to retry, and a call site that forgets `long` would fail only with a slow report in production | §3.4, §10.1 |
| 9 | **A 60 s write watchdog (6 min on a long route) ends in `outcome-unknown`** | a black-holed connection must not hold a spinner forever | §10.3 |
| 10 | **One error table serves inline states and both dialogs; `forbidden` names the permission and the reason** | a copy change is one edit; users ask for the right grant, and a single message would be wrong for two of the three causes | §11 |
| 11 | **Step-up asks once, then redirects; sign-out needs the BFF, waits for running saves first, then asks only about what is still at risk** | the user learns why the page leaves; a local sign-out would hide a live session; a saving tab still holds its draft, so an earlier question would warn about a save about to succeed | §8.3, §8.4 |
| 12 | **English's default region is `GB`; the preferred language wins when offered, even if not a content language** | dates read "19 Jun 2026", not "Jun 19, 2026"; language and content language are separate axes | §7.1, §7.7 |
| 13 | **The picker is `/tenants`, and `/` is the launch redirect** | "open the app" and "show me the picker" are different URLs; the picker at `/` with a `?pick=1` bypass would give one URL two meanings | §9.3 |
| 14 | **The tenant home is `/{t}`** | shorter, the wordmark's target, and what invitations and emails link to; `/{t}/home` would be a second spelling and a new reserved word | §9.2 |
| 15 | **One source of reserved route words, `TellmaRoutes`; the display name and copyright emitted at build, localized through optional string keys** | one list for router, build, proxy and audit; titles need the name offline, and a distribution with an Arabic brand needs its own name | §3.1, §3.2 |
| 16 | **Metadata chunks require the session cookie and are cached `private`** | they disclose the distribution's data model; string packs stay anonymous | §6 |
| 17 | **A cold tenant entry creates the page only after the launch documents arrive** | pages read access and the tenant's shape from them, so an earlier page would render forbidden or drop columns; a warm entry waits for nothing | §4.4 |
| 18 | **No scaffolding command in `tellma-client`; one distribution template writes server and client** | the client files point at server files, and one tool keeps the slug and the paths in step | §2.1 |
| 19 | **No `check` command: `build` runs the source checks first and the output checks last** | nothing runs the checks alone, and source checks fail before the slow `ng build` | §2.6 |
| 20 | **Angular's service worker, though it takes security fixes only** | it covers prefetch groups, hash-checked per-tab versions and safe mode; Workbox or a hand-rolled worker must rebuild the per-tab version model; its MIT code is cheap to fork | §5 |
| 21 | **The outdated-client verdict comes from `Tellma-Build` by SemVer precedence, at once; the worker settles only unordered versions and rollbacks** | waiting for the whole download would leave a Save spinning, and a failed download would read as "being updated"; Reload downloads first and offers a plain reload after a repeated failure | §5.6 |
| 22 | **After a sign-in to `/`, the BFF lands a single-tenant user; the SPA applies the same rule to an in-app `/`; one vector file keeps both in step** | the tenant shell paints first, with no tenantless skeleton and no shell swap | §9.3 |
| 23 | **An expired session returns to the last tenant, and `tenant-not-found` on an automatic entry goes to the picker silently** | users come back where they were; a notice about a tenant the user never asked for confuses | §4.2, §9.3 |
| 24 | **Every `TmUrls` builder uses the routed tenant; `forTenant(id)` builds into another** | one signature shape for coding agents, and no `/null/…` URL outside a tenant | §9.4 |
| 25 | **Region and digits are preferences with tenant defaults, separate from the language** | one distribution serves Saudi and UAE tenants with different formats, and many Saudi users want Latin digits; the formats follow the server's offered pairs | §7.7 |
| 26 | **Absolute date-times show the GMT offset when the display zone differs from the device's; a change of the device zone asks once, with a per-device option to follow it** | a silent mismatch misleads travelling users; offsets are unambiguous where abbreviations are not; only a change prompts, so two devices never ping-pong; the prompt is a dialog because a toast times out without focus and a banner holds one action | §7.8 |
| 27 | **No user-facing copy names the platform; it says the localized display name** | a distribution's users know its brand, not the platform's | §3.1, §11 |

## Review flags

| # | The call (§) | Plausible alternative | What would flip it |
|---|---|---|---|
| 1 | **Metadata chunks behind the session cookie**, cached `private` so no shared cache serves them anonymously (§6) | anonymous immutable assets, simpler for boot and `/signed-out` | an anonymous OpenAPI document anyway, or a signed-out page needing metadata |
| 2 | **The client keeps `decimal` and `long` values as `TmDecimal` strings, parsed by plain `JSON.parse` and guarded by two lint rules** (§10.4); the wire format is spec 0016's flag 2 | a decimal class created from the strings at the wire edge | pages needing arithmetic beyond `TmDecimals`, or decimal bugs the lint rules miss |
| 3 | **Config check as a Node step** (§3.6) | startup checks plus e2e only | `tsx` failing to load a pack's declarations without the Angular compiler |
| 4 | **Write watchdog 60 s / 6 min** (§10.3) | no client timeout | legitimate saves longer than 60 s |
| 5 | **(a) Step-up dialog before the redirect; (b) sign-out needs the BFF; (c) the sign-out wait has no Cancel** (§8.3, §8.4) | (a) redirect at once; (b) sign out locally and retry later; (c) a Cancel on the wait, through a 0023 `busy-overlay` option | (a) users finding the click noisy; (b) a need for offline sign-out, such as kiosks; (c) users stuck behind slow saves near the 60 s cap |
| 6 | **(a) Custom worker registration; (b) updates by banner and natural reload; (c) Angular's service worker, which takes security fixes only** (§5) | (a) a `default` policy; (b) activating on the next clean navigation; (c) fork its MIT worker into `@tellma/core-tools`, or hand-roll one | (a) Angular registering with a `TrustedScriptURL` itself; (b) users ignoring the banner for days; (c) Angular refusing a fix we need, deprecating the package, or Web Push needing code a wrapper script cannot hold |
| 7 | **The profile cookie expires with the idle session** (§8.1) | a 90-day cookie with a client idle stamp | identity-mode limits on re-issuing cookies |
| 8 | **Locale defaults:** (a) `en-GB`; (b) each language's default region, overridable per tenant and per user (`ar` formats as `ar-SA` in Acme); (c) content languages plus the active one in the menu; (d) `PreferredTimeZone`, else the tenant's, labelled with its offset when the device's differs; (e) a per-device theme (§7.1, §7.6–§7.8) | (a) `en-US`; (b) the browser's region; (c) all offered languages; (d) the browser's zone; (e) a synced theme | market preferences, travelling users finding labels and the prompt not enough, or users expecting one theme everywhere |
| 9 | **The display name and copyright emitted at build, localized through string packs** (§3.1) | runtime only, from `DistributionInfo` | deployments renaming a distribution without a rebuild |
| 10 | **Session expiry deletes stored data in `tm-boot.js`** (§4.2) | keep the launch documents for a warm relaunch | the relaunch after a week away judged too slow |
| 11 | **(a) Entity routes are API segments; (b) the worker prefetches all but three sets; (c) one repo-root workspace; (d) no npm abstraction packages** (§9.1, §5.3, §1) | (a) client slugs; (b) lazy route caching; (c) Acme inside `client/`; (d) abstraction packages | (a) vanity URLs; (b) metered links; (c) a spike failing to prove one Angular instance; (d) a pack that must build without `@tellma/core` |
| 12 | **Unverified hosting facts:** abort propagation on App Service, Azure SignalR with the raised client timeout (§12) | — | the staging smoke's results |
| 13 | **One error dialog at a time, the latest wins** (§11) | queue the dialogs | users missing an earlier error that mattered |
| 14 | **A cold tenant entry waits for the launch documents before the page** (§4.4) | start the page's read beside the sync, and have pages re-check access and re-plan when `me` and `settings` land | cold-launch timings where the extra round trip costs more than the re-plan |
| 15 | **No format switch in the session menu**: region and digits are edited on My profile, the Users page and the Settings Formats card (§7.7) | a "Format" row in the session menu | users switching formats often |
| 16 | **The zone prompt is a dialog** (§7.8) | a level-5 banner whose action opens the dialog | users finding the dialog intrusive after travel |
| 17 | **`seen` per tenant, so a multi-tenant user is asked once per tenant** (§7.8) | one `seen` per user | multi-tenant users finding repeated prompts noisy |
| 18 | **The device partition keeps a `sub`, a flag and zones after sign-out** (§7.8) | the user partition, forgotten at sign-out; or a hashed `sub` | a privacy review |
| 19 | **The silent fallback goes to `/tenants`, not `/`** (§9.3) | `/`, re-running the single-tenant rule | single-tenant users landing on the picker; `/` would need a loop guard |
| 20 | **A path chosen by another user's session counts as automatic** (§4.2) | the notice for every deep link | support confusion on shared devices |
| 21 | **Chromium's missing `om`, `ti` and `so` data falls back to the default language's locale for client formatting** (§7.7) | ship ICU data for them | an Oromo-first distribution |

## Appendix A — Build problem codes

Every code is an error that fails `tellma-client build` (exit 3), except the warnings
`label-missing` and `budget-warning`, which print and pass. The source checks, the config check
(§3.6) among them, run in the build's first step, and the output checks in its last (§2.6).

**Config check** (the codes of `TmConfigProblemCode`):

| Code | When | Rule |
|---|---|---|
| `duplicate-key`, `dangling-override`, `dangling-hide` | a declaration adds an existing key; an override or `hide` names no earlier declaration | §2.3 |
| `unknown-section` | a `section` member names no section | §2.3 |
| `app-bar-overflow`, `app-bar-reserved-id` | more than three app-bar slots in all features; a slot with the id `help` or `assistant` | §2.3, 0027 §7.2 |
| `invalid-path` | a page path outside the grammar, or a page with parameters and a `section` | §2.4 |
| `reserved-path` | a page path or coded-report key whose first segment is a routing word, `new`, or the segment of an entity another feature declares | §9.1, §9.2 |
| `invalid-report-key` | a coded-report key that is not kebab-case with a hyphen | §9.1 |
| `coded-report-source` | a coded report whose sources are neither exactly one of `route`, `run` and `component` nor `component` with `route` | 0034 §2.2 |
| `feature-not-composed` | a client feature key absent from `metadata.features` (`core` excepted) | §3.5 |
| `unknown-entity` | a declaration or override naming an entity absent from the metadata | §3.5 |
| `language-missing`, `language-not-offered` | an offered language with no `TmLanguageDefinition`; a definition for a language the server does not offer | §3.5 |
| `default-language-mismatch` | `languages[0].code` differs from the server's default | §3.5 |
| `calendar-missing` | a registered calendar with neither a platform entry point (`gc` built in, `uq`, `et`) nor a `calendars` definition | §7.4 |
| `string-key-collision` | a string pack key beginning `tm.` or `tmUi.` | §7.3 |
| `unknown-setting-category` | a settings tab naming no setting category | 0032 §7.5 |
| `widget-missing`, `widget-not-registered` | a server widget type with no client declaration; a client declaration with no server type | 0035 §4.3 |
| `unknown-path`, `unknown-action` | a `columns`, `orderBy`, `tile` or `picker.description` path that does not resolve; an `actions[].name` the metadata does not list | 0029 §3 |
| `operation-missing`, `entity-not-routable` | a member whose operation the entity lacks; `section` on an entity with no segment or no `query` | 0029 §3 |
| `label-missing` (warning) | the emit wrote a label fallback | 0029 §2.4 |

**The other source checks, and the output checks** (each rule is stated in its section):

| Code | When | Rule |
|---|---|---|
| `index-html-invalid` | an `index.html` rule fails | §4.1 |
| `manifest-invalid` | a manifest check fails | §5.1 |
| `brand-file-invalid` | a brand SVG check fails | §7.6 |
| `budget-exceeded`, `budget-warning` (warning), `budget-note-missing` | a figure over its error level; over its warn level; a raised budget without a note | §12 |
| `unhashed-asset` | a file that must have a hashed name lacks one | §6 |

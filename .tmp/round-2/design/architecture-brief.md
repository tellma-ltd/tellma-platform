# Architecture pass brief

The architecture pass produces `.tmp/round-2/design/00-architecture.md`: the cross-cutting
skeleton that thirteen spec designers will build on. It decides what must be decided once, for
everyone, so the specs fit together. It is a design note, not a spec: decision-dense, short
rationale, sketches over prose. Target 700–1,000 lines.

## Read first

1. `.tmp/round-2/README.md` (task, spec map, rules) and `.tmp/round-2/style-guide.md` (how the
   final specs will read; the skeleton itself may be terser).
2. `.tmp/round-2-brief.md` (the requirements; read all of it) and `.tmp/round-2-specs-breakdown.md`.
3. Every digest in `.tmp/round-2/facts/` (f01–f11). They summarize specs 0002–0022, the research,
   the design files, ARCHITECTURE.md and the code. Open a source only to confirm a detail.
4. The sketches in `.tmp/images/*.png` (look at each with the Read tool).

## The skeleton's sections

1. **Spec map.** The final list of specs (0010, 0023–0035): number, file name, title, what it
   owns, what it consumes, and its dependencies. Adjust the README's map if the design is better
   served; say why in one line.
2. **Client packages.** The npm packages and secondary entry points the platform ships for the app
   layer (beside the existing `@tellma/core-ui*` family), what each holds, the dependency graph,
   lazy-loading boundaries, and where pack client code lives (for example the GL module's Centers
   pages). Respect ARCHITECTURE.md's naming (`@tellma/core`, per-pack `@tellma/*`) unless there is
   a strong reason; say so if you deviate.
3. **What a distribution writes.** The `Tellma.Distro.Acme.Client` file tree, its `app.config.ts`,
   how it registers an entity's search and details pages, adds a launcher section, adds a coded
   report, and overrides a stock page. Show the code (short TypeScript sketches) and count the
   lines. List the escape hatches (rule 1: common → simple, uncommon → escape hatch).
4. **Concept ownership map.** A table: concept → owning spec and section (new or existing) →
   consumers. Every shared concept appears exactly once. Include server concepts the front end
   needs (entity metadata for the client, client permission summary, display names, aggregate
   queries, assignments, report definitions, dashboards, preferences keys, telemetry ingestion).
5. **Shared client seams.** For each cross-cutting service, token or contract: its name, a short
   TypeScript sketch, its owner spec, and its consumers. Examples: app configuration, the API
   client, the client store (IndexedDB), the tab bus and leader, preferences, page state, page
   context (assistant seam), telemetry, shortcuts, navigation registry, entity registry and
   descriptors, display names, client permissions, error presentation, breakpoints.
6. **Server additions and edits.** New endpoints (route, verb, purpose), new entities and tables,
   and every edit to an existing spec 0010–0022 (target section, delta, why, which round-2 spec
   needs it). Amendments to frozen specs 0001–0009 (target, delta, why), destined for spec 0010.
7. **Data flows.** Short numbered flows (mermaid where it helps) for: cold launch after sign-in;
   warm launch within 7 days (prove no network wait before chrome renders); a stale cache
   detected; `cache.changed` arriving over the hub; a new deployment (service worker update and
   the contract fingerprint 412); sign-out in one tab signing out all tabs; a 401 mid-session.
8. **Route grammar.** A table of every route: tenantless (sign-in landing, tenant picker), tenant
   home, search, details, create, clone, settings, reports catalog, report view, report studio,
   dashboards, assignments page, notifications page, my jobs, and how query state (search,
   filter, columns, order, page) is encoded in URLs. Answer the tenant-picker route questions.
9. **Breakpoints and layout constants.** The closed set of width thresholds every component uses,
   the app bar and pinned bar heights, and which spec owns them (a new token set in the component
   library is an amendment to spec 0002 through spec 0010).
10. **Open questions.** A table listing EVERY open question in the brief, its owner spec, the
    recommended answer in one or two lines, and your confidence. The inventory:
    - Tenant picker Q1 (base URL vs `/tenants`), Q2 (`/<tenant-id>/home` vs `/<tenant-id>`).
    - Search page Q1 (row click vs link column). Trees Q1 (Excel export `select` vs displayed
      columns).
    - Details Q1 (boilerplate-free lifecycle with custom layouts), Q2 (display name), Q3 (refresh
      vs leaving the page).
    - SignalR Q1 (leader tab also refreshes caches on `cache.changed`?).
    - Stock pages Q1 (picker per entity vs one generic picker).
    - Reports intro: the term "hardcoded"; whether hardcoded reports get rows in the definitions
      table.
    - Non-aggregate Q1 (drill-through to a child row inside a parent's custom page).
    - Visibility Q1 (names of the four ad-hoc kinds), Q2 (drill-through destination), Q3
      (drill-through columns), Q4 (Queryex changes for server-side pivot aggregation), Q5
      (running totals), Q6 (a published report whose author deletes it).
    - Dashboards Q1 (dashboard-level parameters).
    - Observability Q1 (what to collect, guiding principles), Q2 (transport to Application
      Insights, UI error logging).
    - Supportability Q1 (slow-request trace ids), Q2 (browser screenshot capability), Q3 (where
      the state dump and screenshot go), Q4 (the UI version value).
    - User help (out of scope this round): the seams to reserve so it is not fought later.
    - Changes to existing specs: the full list (section 6).
11. **Conventions.** Naming (`tm-` selectors, `Tm` types, `inject…`/`provide…` functions), signals
    and zoneless, `@defer` and lazy routes, error handling, i18n keys, telemetry event naming,
    test tiers (unit, harness, Playwright end-to-end, axe, bundle and Lighthouse checks), the
    reference distribution's role.
12. **Risks and review-flag candidates.** The judgment calls Ahmad should check.
13. **Brief coverage.** Every feature and requirement in the brief (walk it heading by heading)
    mapped to its owning spec, so nothing falls between specs. Mark anything you deliberately
    defer, and why.

## Rules

- **Design rules first.** Every choice must serve the brief's design rules (plumbing in the
  platform; instant warm launch; instant feedback; non-blocking; stable layout; full RTL; WCAG
  AA; AI-native). Say how, briefly, where it is not obvious.
- **Existing specs.** 0001–0009 are frozen (amend via 0010). 0010–0022 may change; when the
  best design changes one, say so in section 6. Never design a workaround around them.
- **The brief is requirements, not gospel.** Where it is wrong or outdated (for example a browser
  or tool fact), say so and propose the fix. Verify browser-platform facts the design depends on
  with a web search (Web Locks, BroadcastChannel, the Navigation API, `beforeunload` limits,
  `getDisplayMedia` and `preferCurrentTab`, Lighthouse's PWA checks, Angular service worker,
  IndexedDB eviction and `navigator.storage.persist()`, Application Insights and OpenTelemetry
  browser SDK sizes, SignalR client size). Cite URLs in a short "Verified facts" list.
- **One owner per concept.** No concept may be owned twice.
- **Concrete.** Names you choose here are binding on the designers. Prefer names that read well.
- Write the output file early and extend it as you go.

## Must-address findings from the digests

The digests (`facts/`, sections 3 and 5 of each) found gaps the skeleton must settle or assign.
Every item below needs an owner in the ownership map and, where it is cross-cutting, a decision.

- **Component library.** About 25 generic components are missing (f07 §3.1) → spec 0023 owns
  them. Changes to existing components are 0010 amendments (f07 §3.2): grid row activation and an
  external active-row API, sort indicator and `aria-sort`, header filter affordance, cross-page
  selection, row classes, tree expansion persistence and bulk restore, `scrollbar-gutter`,
  scroll-to-row, the grid budget; menu triggers, submenus, headings, checkable and link items;
  modal drawer variant and live direction; popover sizing; multi-select and option groups;
  `TmClientCache` sweeping IndexedDB and web storage; the System theme; a Transloco loader seam in
  `provideTellmaUi` for app strings and server string packs; breakpoints; possibly making private
  overlay helpers public. Decide which feature components stay in feature specs (launcher list,
  tiles, pivot, chart, dashboard grid, expression editor).
- **Unowned pages.** Give each an owner: the full Notifications page and notification preferences
  page; My jobs (and the Jobs and Schedules admin pages); the Outgoing emails admin page;
  Imports/Exports history if any; a tenant-side "My profile" page (name, photo, preferred
  language and calendar, signature; `me/save`, f06 §3). Keep them cheap by using the CRUD
  framework.
- **0012 §11.2 amends frozen spec 0008 in place** (seven amendments, none implemented; f02, f08).
  They move into spec 0010's Queryex part with this round's Queryex amendments.
- **BFF and hosting gaps** (f01 §3, §5): the post-sign-in redirect vs `/tenants` and the warm
  launch; logout cannot be driven from the browser as specified (JSON + CSRF, redirect); no
  cross-tab sign-out signal; `tellma.profile` lacks `sub` and `email` and outlives the 7-day
  session; no SPA hosting rules (cache headers for `index.html`, bundles, service worker; CSP;
  anonymous static assets; the catch-all answering 200 for a mistyped API path); no telemetry
  ingestion (and `sendBeacon` cannot send the CSRF header; `fetch` with `keepalive` can); no
  "My account" URL.
- **Metadata and permissions for the client** (f02, f03, f04): no client entity-metadata
  endpoint; no display-name concept (closest: the related projection, 0012 §2.5); the column
  editor can 403 a whole query through a filtered navigation (0015 §5.2 step 4); `Name2`/`Name3`
  exist only for tenants with those languages; no securables catalogue for the role editor; no
  `IsSensitive` for anticipating step-up; the user-level permissions tag never reaches the browser.
- **Queries and reports** (f02, f04, f08): aggregates have no subtotals, rollups or grand totals;
  no API to query child entities (invoice lines); the emitter has no GROUPING SETS/ROLLUP/OVER;
  optional parameters compare as false when null; no default-expression evaluation; navigation
  properties don't expand (`Center` alone is QX3002); no column aliases; no Validate/Discover web
  endpoint for the studio; only the `gc` calendar is implemented (Umm al-Qura gives QX3101 today);
  `get-by-parent-ids` cannot return roots plus expanded nodes in one call.
- **Export** (f05): the export `Select` is "exactly as the grid uses it", so `Id` and unpacked
  navigation terms become columns, and nothing picks the language slot → 0019 §2.1/§4.1 change.
  There is no import-template operation (it is `export-for-import` with `Ids = []`).
- **Hub and attention** (f06): the hub has four events (`inbox.changed`, `job.changed`,
  `cache.changed`, `session.ended`) on one client method `event`; none for assignments,
  permissions or preferences; toasts and badges assume every tab connects; jobs have no human
  label and coarse progress; `job.changed` goes only to the requester.
- **Settings and preferences** (f03): an entries-only settings save needs `General` read access;
  every save concurrency-checks the typed row (cross-category 409s); `save` returns no new stamp;
  the language list conflict (content languages vs offered languages); every `migrate` bumps
  every tenant tag (every browser refetches after a deploy); `cache.changed` carries the tag name,
  not its value; the "unchanged" response of `settings/client` is undefined; the preference-key
  grammar rejects keys like `grid.gl.Center.columns`; the write-behind flush policy is unstated.
- **Workspace** (f11): nothing of the app exists yet (no `@tellma/core`, no service worker, no
  Acme distribution); ARCHITECTURE.md places the distribution client in two places and links it
  through path mappings (two copies of Angular); `inject-font-preloads.mjs` edits `index.html`
  after the build (breaks the service worker's hash check); tooling only knows `@tellma/core-ui`.
- **Design files vs brief** (f10 §5): URL `app.tellma.com/b/101/...` (not authoritative for
  architecture); save errors as an inline banner vs the brief's 422 modal; Clone and Delete as
  buttons vs an Actions menu; 5 and 2 home rows vs 10; report kinds List / Summary / Pivot /
  Chart; designed but unbriefed features (sandbox and read-only markers, banners with
  precedence, toast timing rules, the attention badge, the job ring, a page-size selector, a
  concurrency-conflict dialog). Decide which win: behavior follows the brief unless the design is
  clearly better; visual style follows the design.
- **Research vs brief** (f09 §5): the brief's warm launch lands on home, the shell research's D12
  restores the last page; bar heights 40 + 32 (brief) vs 48 + 40 (research); dashboard sharing
  models differ between the home and sharing research docs. ARCHITECTURE.md puts dashboards in
  tier 3. Decide, and note the ARCHITECTURE.md edits.

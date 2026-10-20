# Front-end specs breakdown

This file divides the [round-2 brief](./round-2-brief.md) into eleven specs, numbered 0023–0033,
and widens spec 0010 to carry the amendments to specs 0001–0009. Read the brief first; this file
decides only *which spec owns what*, not the design itself.

## Ground rules for every spec

- **Size.** Aim for 1,000–1,500 lines. If a spec overflows, raise it as a review flag instead of
  silently splitting it; the split candidates are listed under [Overflow plan](#overflow-plan).
- **Structure.** Follow the skeleton of specs 0011–0022: header (Author, Date, Status), Context,
  Goals / Non-goals, numbered design sections, Testing, Definition of done, Decisions record,
  Review flags.
- **One owner per concept.** Every shared concept below has exactly one owning spec. Other specs
  consume it by reference and never redefine it. If you need a change to a concept another spec
  owns, write it as a review flag addressed to that spec.
- **Open questions.** Every open question in the brief is assigned to exactly one spec below. The
  owning spec answers it in its Decisions record, with the alternatives considered.
- **Existing specs.**
  - Specs 0010–0022 are not implemented. Edit them in place when a front-end design needs a
    change, and list every such edit in your own spec's Context section.
  - Specs 0001–0009 are implemented and frozen. Never edit them. Record each change you need under
    a heading **Amendments requested** in your own spec, naming the target spec and section, the
    delta, and why. A consolidation pass moves these into spec 0010 (see
    [Spec 0010](#spec-0010-widened-to-amendments)).
- **Foundation already built.** The component library is implemented and is the base for every
  page in this round:
  - 0002 (foundation, tokens, size ladder)
  - 0004 (datagrid and tree grid)
  - 0005 (basics: inputs, dates and calendars, images, files)
  - 0006 (entity picker)
  - 0008 (Queryex)

  Build on them; don't redesign them.
- **Research inputs.** Start from these documents where they apply, and say where you deviate:
  - `docs/research/erp-shell-design.md`
  - `docs/research/home-page-customization.md`
  - `docs/research/in-app-notifications.md`
  - `docs/research/report-sharing.md`
  - `docs/research/charting-library-analysis.md`
  - `docs/research/angular-component-library-analysis.md`
- **The design rules in the brief bind every spec.** In each spec's Definition of done, state how
  the design meets instant warm launch, instant feedback, non-blocking queries, stable layout, RTL
  and WCAG AA, with tests where they can be tested.
- **Assistant seam.** Each page spec includes a short section stating what it exposes to the
  future embedded assistant: page identity, visible data and errors, and the actions it can take.
  Use the context contract that 0023 defines.
- **Telemetry.** Each spec lists the client events and metrics it emits, using the API that 0023
  defines.
- **Reference distribution.** Every feature is demonstrated in `Tellma.Distro.Acme.Client`
  (English and Arabic, Gregorian and Umm Al-Qura, ar-SA). Each spec states what the reference
  distribution adds for it.

## The specs

### 0023 — Client app foundation

The app and the plumbing every page assumes.

- **App project and packaging:**
  - The split between platform library packages and the distribution app, and what a
    distribution must author versus what it gets for free.
  - The app's public configuration API (design rule 1).
- **Delivery:**
  - The PWA manifest and installability criteria (every Lighthouse installability check).
  - The service worker: cache strategy for bundles, fonts, images and translations; the update
    check after instant serve, and how an update is applied.
  - Bundle budgets, enforced in CI.
- **Responsiveness:** the closed set of width breakpoints that every component uses.
- **Localization and theming:**
  - RTL and translation loading.
  - Theme switching (light, dark, system).
- **Authentication over the BFF:**
  - The guard on `tellma.profile`, and redirecting to `/bff/login` on 401 or a missing cookie
    (spec 0011 §5.5).
  - Sign-out, including signing out every tab.
- **Routing:**
  - The route grammar for tenantless and tenant-scoped routes.
  - The launch redirect logic (last tenant, picker).
  - Amendments to spec 0011 §3.1 and §5.5 as needed.
- **HTTP layer:**
  - Cancelling abandoned read requests end to end.
  - Handling contract fingerprints (spec 0016 §3.10).
  - The error envelope and trace IDs; slow-request capture.
- **Observability and supportability:**
  - Client telemetry API, transport to Application Insights, error logging.
  - UI version identity; access to trace IDs.
  - State dump, screenshot, and sending both to support (opt-in per distribution).
- **Assistant context contract:** the registry through which pages publish their context.

**Owns open questions:**
- Tenant picker Q1 and Q2 (routes).
- Observability Q1 and Q2.
- Supportability Q1–Q4.

### 0024 — Client state, caching and cross-tab sync

Where client state lives and how it stays fresh.

- **Warm-launch cache:**
  - Settings, permissions, definitions and Queryex schema, stored per user and per tenant in
    IndexedDB.
  - Version tags and staleness (spec 0013 version tags).
  - Clearing on sign-out.
  - Treatment of sensitive data on shared machines.
- **User preferences client:**
  - Read-through and write-behind over `core.UserPreferences` (spec 0014).
  - The typed preference keys other specs register: columns, pinned pages, theme, and so on.
- **Page-state cache:** a bounded least-recently-used cache of page state (query, data, scroll
  position, selected tab), keyed by route. 0028 and 0029 consume it.
- **Session-scoped persistence:**
  - Edit drafts that survive a tab refresh but not a new tab.
  - Tree expansion state.
- **Cross-tab coordination:**
  - Leader election per user and tenant, with failover when the leader tab closes or crashes.
  - Broadcast to the other tabs.
  - Client for the SignalR hub (spec 0021), owned by the leader tab.
  - Fan-out of `cache.changed` and notification events.
- **Recently visited pages:** the store that both the launcher and the home page read.

**Owns open questions:** SignalR Q1.

### 0025 — Shell, launcher and tenant picker

Everything around the page.

- **Tenantless shell and tenant picker page:**
  - The picker reads `GET /bff/user`, shows multilingual tenant names, and offers search.
  - The tenantless shell shows the wordmark, the user's name and email, and a language selector.
- **Tenant app bar:**
  - Wordmark and icon by breakpoint.
  - Menu button.
  - Back and forward buttons in installed mode.
  - Session info that collapses by breakpoint.
  - Reserved slots for help and the assistant (not built).
- **Launcher and global search:**
  - Sections of top-level pages; synonyms; multilingual matching.
  - Ctrl/Cmd-K and the full keyboard model.
  - Cached search results.
  - A search provider seam, so records, settings and help can be added later.
- **Pinned bar:**
  - Pins and one level of folders; pinning any deep link.
  - Customization UI.
  - The preference to hide the bar.
- **Session menu:**
  - Switch tenant; my account.
  - Language, calendar and theme toggles.
  - Shortcuts dialog; About dialog with versions; sign-out.
- **Keyboard shortcut registry:** a platform-wide registry with conflict rules, used by every page
  and feeding the shortcuts dialog.
- **Navigation contract:** how a distribution and its packs contribute launcher entries and
  sections.

**Out of scope:** user help and documentation. The brief marks it out of scope for v1. Reserve
the app bar slot and a drawer host only, so the later design doesn't fight this one. The help open
questions have no owner this round.

### 0026 — Assignments and attention surfaces

The full assignments design, which the in-app notifications research left out, plus the front end
for notifications.

- **The assignments model:**
  - `core.Assignments`: entity, lifecycle, DataBatch plumbing.
  - API and SignalR events.
  - Tests at both the service and web levels.
  - Edits to specs 0014, 0015, 0016 and 0021 in place as needed.
- **Assignments tray:** contents, badges, seen state.
- **Notifications bell panel:** grouping, read and unread, withdrawn items.
- **Toasts and banners:** timings, stacking, accessibility (live regions), and suppression when
  focused.
- **Cross-tab consistency:** delivered through 0024's leader tab.

**Owns open questions:** whatever the in-app notifications research left open for presentation and
assignments.

### 0027 — Client entity model and CRUD primitives

The shared vocabulary between pages and reports.

- **Client entity descriptors:**
  - Fields, types, navigation properties, tree-ness and supported operations, derived from server
    metadata.
  - How a distribution or pack extends them.
- **Display names:** how any entity renders as a string in the current locale. Used by titles,
  pickers, navigation-property cells and report dimensions.
- **Multilingual navigation-property expansion:**
  - A navigation property selects Id plus its three name columns.
  - Switching language relabels without a round trip.
- **Client Queryex builder:** typed construction of select, filter, orderby, expand and aggregate
  expressions, plus parsing for display.
- **Path tree picker:** the lazily expanding, searchable tree of fields and navigation properties.
  Used by the column editor (0028) and the report studio's field list (0032), including drag
  sources.
- **Filter controls by data type:** string, numeric, date, enum, bool, navigation.
- **Generic entity picker binding:** wiring spec 0006 to any entity from its descriptor.

**Owns open questions:** Details Q2 (display name) and Stock CRUD pages Q1 (picker per entity or
generic).

### 0028 — Search page

The list half of the CRUD front end.

- **Layout:** the three rows and their controls.
- **Grid and tiles:**
  - Grid view, built on spec 0004.
  - Tiles view: a lattice that collapses to a list, with the per-entity tile layout contract
    (image and four corner values).
  - Defaults by breakpoint.
- **Search field:**
  - Debounce against key auto-repeat.
  - Type-to-search capture rules (only when focus isn't in an editable control).
  - Row highlight and the keyboard model.
- **Ordering:** by a single column.
- **Filtering:**
  - Column filters (controls from 0027).
  - The advanced filter popup: Queryex text with server validation errors, active-only, and
    custom filters contributed by a distribution or pack.
- **Column editor:**
  - The UI over 0027's path tree, reordering, and slash-separated labels.
  - Code-defined defaults; persisting the choice in preferences.
- **Selection:**
  - Selection across pages; bulk actions menu.
  - Create, import, export and refresh entry points.
- **State:**
  - URL encoding of search, filter, columns and order.
  - Page-state restore through 0024.
  - Precedence of URL parameters over preferences.
- **Trees:**
  - Tree and list toggle; lazy expansion.
  - Remembered expansion.
  - Search results shown with their ancestors and the matches marked.
  - Tree of tiles on narrow screens.
- **Distribution contract:** what a distribution writes for a new entity's search page; the target
  is a few lines.

**Owns open questions:**
- Search page Q1 (row click versus link column).
- Trees Q1 (Excel export `select`, amending spec 0019 or 0016 as needed).

### 0029 — Details page framework

The record half of the CRUD front end.

- **Lifecycle:**
  - View, edit, create and clone.
  - Instant switch to edit mode.
  - Create state held in the URL.
  - Save, delete and deactivate flows.
  - Server validation errors mapped onto fields, including in child collections.
- **Layout:**
  - Header and toolbar rows.
  - The card and panel layout language and its reflow.
  - Extras panels and their data loading.
- **Paging through search results:**
  - Paging over 0028's cached result set, including loading the next page at the edge.
  - Returning to the search page on the new page.
- **State:**
  - Restoring scroll position, selected tab and panel scroll.
  - Edit drafts that survive a refresh (storage from 0024).
  - The "Leave without saving?" guard.
- **Child-row deep link:** a contract to open the tab that holds a child row, scroll to the row
  and flash it. Report drill-through (0032) consumes it.
- **Distribution contract:** layouts are custom per entity; the platform supplies the state
  machine and the primitives. Show a minimal page and a rich page.

**Owns open questions:**
- Details Q1 (boilerplate-free lifecycle) and Q3 (refresh versus leaving the page).
- Non-aggregate Q1 (drilling through to a child row).

### 0030 — Stock pages: Users, Roles, Centers and Settings

The platform-owned pages. They are the first real test of 0027–0029.

- **Users** (spec 0018 §3):
  - Search and details pages.
  - Invite and invitation status.
  - Issuing credentials.
  - Role memberships.
  - User image (spec 0017).
- **Roles** (spec 0018 §4):
  - Search and details pages.
  - The permissions editor: securables, actions and row-level filters (spec 0014).
  - Members.
- **Centers** (spec 0018 §5):
  - The tree entity, with search and details pages.
  - Center types.
- **Settings:**
  - One tabbed page with one tab per settings category, showing only the categories the user can
    read (spec 0013: `core.Settings.<Category>` securables, the `settings/details` view, and
    `Save` per category).
  - The typed general settings, which get a custom layout.
  - Declared setting keys, rendered from their descriptors: value type, visibility, nullability.
  - A seam for a distribution to give a category a custom tab.
  - The effects of saving on the warm-launch cache and on other tabs (through 0024).

### 0031 — Report definitions and query engine (back end)

- **`ReportDefinitions`:**
  - The entity and its stack.
  - JSON definition schema with a version and validation.
  - The four report kinds.
  - Parameters: key, labels, requirement level, default expression, type override, inference
    from expressions.
  - Columns of the drill-through list.
- **Hard-coded reports:**
  - The term to use for them.
  - Whether they also get built-in rows in the definitions table.
  - The contract a hard-coded report implements.
- **Sharing and visibility:**
  - Owner, user shares and role publishing.
  - "My reports", "Shared with me" and "All reports".
  - The admin view.
  - Answering "who can see this report and why".
  - The notification sent on a share (through spec 0021).
  - Starting point: `report-sharing.md`.
- **Queryex extensions** (amendments to 0008 go to spec 0010):
  - Pivot aggregation at every level, server-side.
  - Rollups for tree dimensions with the parent's own values (the phantom child).
  - Totals and grand totals; dimension attributes; navigation-property label expansion.
  - Running totals, if adopted.
- **Execution and export:**
  - The execution API, with cancellation.
  - Excel export for all four kinds: the pivot fully expanded, a chart's data as a flat table
    (spec 0019).

**Owns open questions:**
- Reports intro Q1 and Q2 (the term "hardcoded"; built-in rows).
- Visibility model Q1 (names of the four report kinds), Q3 (drill-through columns), Q4 (Queryex
  aggregation), Q5 (running totals) and Q6 (a published report whose author deletes it).

### 0032 — Report renderers and studio (front end)

- **Renderers:**
  - Non-aggregate list.
  - Aggregate flat table.
  - Pivot: collapsible row and column trees, auto-expand, frozen headers, three or four styles of
    totals.
  - Charts, behind a Tellma facade over the library chosen from `charting-library-analysis.md`.
- **Parameters and drill-through:**
  - The parameter bar.
  - Drill-through from rows, cells and data points: to the drill-through list or search page, and
    to child rows through 0029.
- **Hosting:** as a page, in an entity card, or in the assistant. Covers the component contract
  and how state lives in each host.
- **Report studio:**
  - Definition panel and preview, with a resizable divider.
  - The field list from 0027's path tree, with drag targets: dimensions, measures (Sum by
    default), and inserting at the cursor in the filter and having expressions.
  - Live inference of parameters.
  - Validation feedback.
- **Reports launcher entry:** the searchable menu of reports with its categories, and pinning a
  report.

**Owns open questions:** Visibility model Q2 (drill-through destination: the entity's search page
or a dynamic list).

### 0033 — Dashboards and home page

- **Dashboards:**
  - The entity: titles, refresh rate, the report placements, a refresh override per placement.
  - A grid layout that reflows on narrow screens.
  - Validation of hidden parameters.
  - Silent refresh with the last-refreshed time; forced refresh.
  - Dashboard-level parameters.
  - Sharing, through 0031's model.
- **Home page:**
  - Home is a dashboard; start from `home-page-customization.md`.
  - The time-of-day greeting with the date in the user's calendar.
  - The default "work first" layout: assignments from 0026, recently visited pages from 0024.
  - Landing on home after a warm launch; the wordmark link.
  - Choosing a home page per role and per user: design the seam now, even if the UI comes later.

**Owns open questions:** Dashboards Q1.

## Spec 0010 widened to amendments

Spec 0010 becomes the single amendments spec for every implemented, frozen spec. It is renamed
along the lines of *Amendments to Implemented Specs*.

- **Parts.** The existing identity-server deltas stay as Part A (amending 0003). Add one part per
  target spec, such as 0002, 0004, 0005, 0006 and 0008.
- **Each amendment** names:
  - the target section;
  - the delta;
  - the spec that needs it;
  - the reason.
- **Definition of done per part.** The identity-server part blocks spec 0011 and is needed early;
  the front-end parts are needed only when their consumers are built. Each part therefore gets its
  own Definition of done, so implementing one doesn't wait on the others.
- **One writer.** Designer agents never edit 0010 directly; parallel edits to one file will
  conflict. Each spec collects its needs under **Amendments requested**, and one consolidation
  pass, after the last spec is drafted, writes them into 0010. That pass de-duplicates the
  requests and resolves conflicts between specs.

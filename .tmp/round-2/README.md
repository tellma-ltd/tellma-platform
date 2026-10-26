# Round 2 working folder

This folder holds the working files of the round-2 design pass. Nothing here is a spec; the specs
live in `docs/specs/`. Every agent working on round 2 reads this file first, then
[style-guide.md](./style-guide.md).

## The task

Turn Ahmad's brain dump into professional, implementation-ready specs for the front end of the
Tellma platform and the back-end pieces it needs, designed as one coherent whole.

- **Specs 0001–0009** are implemented and frozen. They are never edited. Any change to them is an
  amendment written into spec 0010, which this round widens from "identity server amendments" to
  "Amendments to Implemented Specs" (one part per target spec).
- **Specs 0010–0022** (round 1) are designed but not implemented. They may change in any way that
  serves the round-2 design. When a round-2 design is best served by changing one of them, change
  it in place. Never work around an existing spec with a hack.
- **Specs 0023 onward** are this round's new specs.
- Every modification to an existing spec (0010–0022) and to `ARCHITECTURE.md` is logged in
  `.tmp/round-2-spec-changes.md` (the change report for Ahmad).

## Inputs (all paths relative to the repo root `C:\Users\ahmad\workspace\tellma\tellma-platform`)

| Input | What it is | Authority |
|---|---|---|
| `.tmp/round-2-brief.md` | Ahmad's brain dump: requirements, design rules, open questions | The requirements. Not final; may contain mistakes; flag them. |
| `.tmp/images/*.png` | Screenshots from a Claude Design project | Authoritative for theme and visual style only, not architecture |
| `.tmp/ui-design/` | The full Claude Design project (HTML design canvases, design-system tokens) | Same as the PNGs |
| `.tmp/round-2-specs-breakdown.md` | Ahmad's proposal for dividing ownership across specs | A proposal; this round revises it (see the spec map below) |
| `docs/research/*.md` | Market research done by earlier agents | Evidence; deviate with a reason |
| `ARCHITECTURE.md` | The living architecture document | Current state of mind, not an authority |
| `docs/specs/0001–0022` | Existing specs | 0001–0009 frozen; 0010–0022 changeable |

## The design rules to live by (from the brief; they bind every spec)

1. **Plumbing lives in the platform.** Distribution code is written by coding agents at scale, so
   it must be simple and mechanical. Common requirement → straightforward; uncommon requirement →
   an escape hatch exists. Every line in a distribution repo must earn its keep.
2. **Fast and smooth UI:**
   - A. Instant warm launch: within 7 days of a sign-in, the shell and page chrome render from
     cache with no network wait, ever. Only the page's data waits, and its loading state shows
     instantly.
   - B. Instant feedback on every click and key press.
   - C. Non-blocking: abandoned or superseded queries are cancelled, never block the user.
   - D. Stable layout: nothing shifts or wobbles on click or type.
   - E. Full RTL.
   - F. WCAG AA, especially keyboard-only paths.
3. **AI-native.** A future embedded assistant (AG-UI protocol) will see the session's full context
   and act in the UI. Today's design must not fight that.

## The spec map for this round

The breakdown proposed 0023–0033. This round revises it in two ways, giving thirteen new specs:

- **A component library additions spec (0023).** The existing library lacks about 25 generic
  components that several round-2 specs need (icon, badge, avatar, toast host, drawer, splitter,
  segmented control, radio, switch, chips and multi-select, skeletons, pager, search field, card
  and page-header layout, busy overlay, confirm and error dialogs, empty state, date range,
  relative time, reorderable list, tree, drag and drop, key hints, accordion, progress bar). One
  spec owns them, so no feature spec defines a generic primitive. Changes to EXISTING library
  components (grid row activation, menu features, modal direction, ...) are amendments in 0010.
- **The breakdown's 0023 is split in two:** the app foundation (0024) and client observability,
  supportability and page context (0026).

The architecture pass (`design/00-architecture.md`) may still adjust scope lines; that file is the
latest truth once it exists. Note: `.tmp/round-2-specs-breakdown.md` uses the OLD numbering
(its 0023 is our 0024, and so on); the digests in `facts/` use the NEW numbering below.

| Spec | File | Scope (short) |
|---|---|---|
| 0010 | `docs/specs/0010-amendments-to-implemented-specs.md` (renamed from `0010-identity-server-amendments.md`) | Part A: identity server (existing text). New parts: one per frozen spec amended this round (0002, 0004, 0005, 0006, 0008, …), including the amendments to 0008 that spec 0012 §11.2 currently states in place |
| 0023 | `docs/specs/0023-component-library-additions.md` | New generic components in the `@tellma/core-ui` family (see above), with their harnesses, budgets and strings |
| 0024 | `docs/specs/0024-client-app-foundation.md` | The app project and packages, what a distribution writes, PWA and service worker, bundle budgets, breakpoints, localization and theme loading, BFF authentication and sign-out, routes and the launch redirect, the HTTP layer (cancellation, contract fingerprints, error envelope and the shared error dialogs), SPA hosting rules |
| 0025 | `docs/specs/0025-client-state-and-cross-tab-sync.md` | Warm-launch cache, user preferences client, page-state cache, session drafts, tree expansion memory, cross-tab leader election, the SignalR client, recently visited pages |
| 0026 | `docs/specs/0026-client-observability-and-support.md` | Client telemetry API and transport, error logging, UI version identity, request log and trace ids, state dump, screenshot, send to support, the page-context registry (the assistant seam) |
| 0027 | `docs/specs/0027-shell-launcher-and-tenant-picker.md` | Tenantless shell and tenant picker, the tenant app bar, launcher and global search, pinned bar, session menu, keyboard shortcut registry, navigation contributions |
| 0028 | `docs/specs/0028-assignments-and-attention-surfaces.md` | `core.Assignments` back end end-to-end, the assignments tray, the notifications bell panel, toasts and banners, the notifications and my-jobs pages |
| 0029 | `docs/specs/0029-client-entity-model.md` | Entity metadata for the client, display names, multilingual navigation expansion, client permissions, the client Queryex builder and expression editor, the path tree picker, typed filter controls, generic entity picker binding |
| 0030 | `docs/specs/0030-search-page.md` | The search page: grid and tiles, search field, ordering, column and advanced filters, column editor, selection and bulk actions, state and URL, trees |
| 0031 | `docs/specs/0031-details-page.md` | The details page framework: view/edit/create/clone lifecycle, layouts and primitives, paging through search results, state, drafts, leave guard, child-row deep links |
| 0032 | `docs/specs/0032-stock-pages.md` | Users, Roles, Centers and Settings pages, plus the other platform-owned admin pages |
| 0033 | `docs/specs/0033-report-definitions-and-engine.md` | Report definitions back end: entity, JSON schema, kinds, parameters, built-in reports, sharing and visibility, Queryex extensions, execution and Excel export |
| 0034 | `docs/specs/0034-report-renderers-and-studio.md` | Report renderers (list, summary, pivot, chart), parameters, drill-through, hosting, the report studio, the reports launcher entry |
| 0035 | `docs/specs/0035-dashboards-and-home-page.md` | Dashboards, the home page and its seams |

## Folder layout

- `facts/` — digests of the existing material (specs, research, design files, code). Read these
  instead of re-reading 22,000 lines of specs; open the source only to confirm a detail.
- `design/` — `00-architecture.md` (the cross-cutting skeleton: ownership map, packages, seams,
  routes, open-question owners) and one design note per spec (`0023.md` … `0035.md`, `0010.md`).
- `reviews/` — integration and consistency review findings.
- `edits/` — consolidated edit lists for existing specs, one file per target.

## Ownership rules

- **One owner per concept.** Every shared concept has exactly one owning spec. Other specs cite it
  ("spec 0024 §3.2") and never restate it.
- **Open questions.** Every open question in the brief has exactly one owning spec, which answers
  it in its Decisions record with the alternatives considered.
- **Existing specs.** A change to 0010–0022 is made in that spec (and logged). A change to
  0001–0009 is an amendment in 0010.
- **Ahmad decides.** Judgment calls that a reasonable lead engineer could make differently go in
  the owning spec's Review flags, with the alternative and what would flip it.

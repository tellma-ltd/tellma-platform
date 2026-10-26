# F09 — Research docs digest (shell, home page, notifications, report sharing, charting)

Digest of the market-research documents that feed round 2. Read this instead of the sources; open a
source only to confirm a detail. Section references use the source's own numbering (e.g. "shell
§2.4", "home D5", "notif §3.3", "sharing D7", "chart §4").

## 1. Sources read

| Source | Lines | Read |
|---|---|---|
| `docs/research/erp-shell-design.md` (abbrev. **shell**) | 792 | fully |
| `docs/research/home-page-customization.md` (abbrev. **home**) | 780 | fully |
| `docs/research/in-app-notifications.md` (abbrev. **notif**) | 716 | fully |
| `docs/research/report-sharing.md` (abbrev. **sharing**) | 574 | fully |
| `docs/research/charting-library-analysis.md` (abbrev. **chart**) | 453 | fully |
| `docs/research/angular-component-library-analysis.md` (abbrev. **complib**) | 393 | skimmed |
| memory `home-page-customization-research.md` | 29 | fully |
| memory `in-app-notifications-research.md` | 40 | fully |
| memory `report-sharing-research.md` | 27 | fully |
| memory `charting-library-research.md` | 43 | fully |

Feeds (spec map in `.tmp/round-2/README.md`): shell → 0027 (+0025 recents/last-page, 0024 routes);
home → 0035 (+0014 preferences, 0018 roles); notif → 0028 (+0015/0016/0011/0014 already-applied
parts, 0025 SignalR client); sharing → 0033 (+0034 studio share UI, 0035 dashboards); chart → 0034
(+0024 bundle budgets, design tokens).

## 2. Key facts

### 2.1 ERP shell design (shell) — feeds 0027, 0025, 0024

Status line: "Research and recommendation. Not a spec... where they conflict with a later spec, the
spec wins." Confidence markers: Firm / Leaning / Open (shell, intro table).

**Market facts (shell §1.2–§1.5):** every product keeps a persistent bar with home, search,
notifications and the user menu (C1). Always-visible nav is user-curated (C2). Recents are a peer of
favourites, "the clearest gap in the old Tellma shell" (C3). The full tree opens as an overlay (C4).
Search is hotkeyed and covers pages and actions (C5). Wide-grid ERPs (Odoo, Fiori, BC, Intacct,
NetSuite) use a top bar. No web ERP ships in-app tabs. Odoo 19 did NOT move to a sidebar (§1.5).
Research used: NN/g hidden nav costs +39% task time on desktop, and combo nav scores best; ≤3
levels; badges show a count when the number matters, a dot otherwise, `99+`, hidden at zero, with an
`aria-label`; theme stored as `system | light | dark`.

**Decision index (shell §2.0).** Firm: D1 one top bar in two rows, no permanent left column; D2
unified Launcher overlay (empty = browse, typed = search), dropping below both bars; D3 ≤3 menu
levels (Section → Group → Page), all rendered at once; D4 the pins row is the only permanent nav;
D5 user-owned pins with one level of groups, role defaults a one-time seed, shell-owned pin
control; D7 avatar + user name + tenant name always visible; D9 three-state theme stored as a
preference; D10 bell with an unseen badge (`99+`), per-item unread, "View all" page; D12 last-page
restore once per boot; D14 no in-app tabs; D18 RTL via logical properties, numerals and logo LTR;
D19 landmarks, skip link, focus management. Leaning: D6 recents (route template, dwell); D8 no quick
settings in the bar; D11 home = Launcher browse content + start-page override; D13 `Ctrl/Cmd+K` +
access keys; D15 breadcrumbs per page; D16 mobile = same shell, no bottom tab bar; D17 chrome ≤96px
desktop / ≤100px mobile; D20 ≤3 bar slots + 1 banner slot. Open: D21 pinnable left panel.

**Layout (shell §2.1, D1):** Row 1 global bar **48px**: wordmark (links home), Menu button + search
input "welded into one grouped control", inbox bell, session button. Row 2 pins bar **40px**: pins,
group chips, overflow "More", shell-owned pin control. Menu button labelled "Menu" in words (not a
bare hamburger); "defensible alternative is to drop it" (Q10). Reasons against a sidebar: grid width
(~15% of 1600px), cohort, tie-break to old shell, simplicity.

**Launcher (shell §2.2, D2):**
- Overlays, never navigates; drops below both bars over a scrim; `Esc` / click-away dismiss and
  return focus to the trigger.
- Desktop: no pinned band inside it; pinned pages are **starred in place** in section columns.
  Mobile: the pinned band returns (full-screen Launcher, §2.11).
- Browse state: first band **RECENT**, then section columns.
- Typed state: ranked buckets in order **Pages, Records, Actions, Settings & Help**. Pages matched
  client-side from the permission-filtered menu index (instant); Records server-backed, debounced.
- "New X" is an inline `+ New` affordance on its list page's result row, not a separate row/action.
- Prefix scoping, fixed small set: `/` pages, `>` actions, `#` records, `?` help.
- Permission-filtered at the source; every result a real anchor with `href`.
- Actions bucket tiers: (1) shell commands — sign out, switch tenant, change language/calendar/theme,
  mark all notifications read ("Ship these"); (2) current-page commands — post, export, print, clear
  filters — **conditional on a declarative page action model** (Q11). "If neither tier survives
  review, drop the bucket."

**Menu depth (shell §2.3, D3):** Section → Group → Page; sections as columns with headings, groups
as sub-headings; zero clicks of depth. Authoring guardrails (not runtime limits): warn above 10
sections; warn above 30 pages in a group.

**Pins (shell §2.4, D4/D5):**
- User-owned, drag-reorderable; one level of groups (chip with caret → short menu); overflow into
  "More"; soft cap ~20 with a nudge, no hard limit.
- Shell-owned pin control at end of pins row: `☆ Pin`, filled when pinned, keyboard shortcut,
  tooltip naming the page; zero page cooperation.
- Optional page-state contribution "through a service the shell queries" (pin captures filters/sort).
- Revoked pin: chip renders disabled with tooltip and "remove" affordance; never 404s silently.
- Role defaults = seed: on first sign-in and on explicit "reset to defaults", ordered de-duplicated
  union of every held role's default set, ordered by an authored ordinal per role set then pin
  ordinal, filtered by effective permissions, **cap ~12**, materialised as the user's own list;
  nothing recomputes. New role later → optional dismissible nudge. "Shipping v1 with no role
  defaults at all is a legitimate option."

**Recents (shell §2.5, D6):** first band of Launcher browse + top of zero-query ranking;
page-level, de-duplicated by **route template** (not URL, not record); committed after a **~2–3 s
dwell** (timer on route change; a dwell gate, explicitly not a trailing debounce); move-to-front,
**cap 10**, batch the persistence; not in the top bar; scoped **per user and per tenant**.
Record-level recents not in v1 (if added: ≤~3 per route type).

**Session menu (shell §2.6, D7–D9):** always visible: avatar, display name, tenant display name
("a safety control"). Menu order: 1 identity block (avatar, name, email); 2 tenant + switcher
("Switching resets page state and navigates to that tenant's last page"); 3 My account (profile
modal); 4 Language by endonym (flips direction); 5 Calendar; 6 Theme System · Light · Dark; 7
Density only if a runtime switch exists; 8 Help, keyboard shortcuts, about (version); 9 Sign out.

**Inbox (shell §2.7, D10):** badge = unseen count since panel last opened, `99+`, hidden at zero;
opening the panel clears unseen. Unread per item; "Mark all read". Unread-but-nothing-new → plain
dot. `aria-label` e.g. "Inbox: 9 new, 24 unread". "View all" opens a real, pinnable inbox page.
Counters computed server-side and pushed.

**Home and landing (shell §2.8, D11/D12):**
- Home renders exactly the Launcher browse content (Recent band + section columns, pins starred).
  "home is a rarely-seen surface, and investment in it has poor returns".
- Escape hatch: **user-settable start page** (any page incl. a dashboard renders at the tenant home
  route); admins may seed per role.
- Tenant home route = **`/<tenant-id>`**; site root `/` is either a switcher or a redirect (Q8).
  A switcher renders outside the shell with minimal chrome: identity and sign-out only.
- Last-page restore (D12, Firm): evaluated exactly once, on the app instance's first navigation after
  boot, only when the resolved route is the tenant home; deep link wins; per user + per tenant;
  falls back to home with a toast if gone/revoked/missing; never restores into a modal or unsaved
  draft; restore is a **replace**, not a push. Q9: server-side vs per-device storage is a coin-flip.

**Keyboard, tabs (shell §2.9–§2.10, D13–D15):** persona A gets `Alt`-based access keys + hold `Alt`
to reveal (Odoo); personas B/D get `Ctrl/Cmd+K`; the shell must not claim grid keys (fixed,
published list in "Keyboard shortcuts"; `Cmd` on macOS). No in-app tabs; every navigable thing is a
real `<a href>`; breadcrumbs are per page.

**Responsive (shell §2.11, D16/D17):** breakpoints ≥1280 / 1024–1280 / 768–1024 / <768 with a
collapse order (1024–1280: session drops user name, keeps tenant; 768–1024: search becomes an icon,
pins a scrollable chip strip; <768: `☰ · wordmark · ⌕ · bell · avatar`, `☰` leads ahead of the
wordmark, Launcher full-screen, session menu a bottom sheet). No bottom tab bar.

**RTL / a11y / theming (shell §2.12, D18/D19):** whole shell mirrors; numerals in badges and the
wordmark stay LTR. `<header>` banner; pins `<nav aria-label="Pinned pages">`; Launcher tree
`<nav aria-label="Main menu">`; one `<main>`; skip link first (WCAG 2.4.1); active pin
`aria-current="page"`; overlays trap focus; axe CI gate. Shell defines no colours.

**Extension slots (shell §2.13, D20):** bar slot ≤3 items (right of row 1, before the inbox; icon +
label + badge + panel); one banner slot below the bar, one at a time by priority (impersonation,
offline/stale, subscription/environment), counts toward the chrome budget. Nothing else. Not adopted
(§2.14) includes an AI assistant panel ("Reserve the slot conceptually").

**Implementation shape (shell §2.15):** `tm-app-shell`, `tm-launcher` (mounted in three places:
overlay, home route, palette-with-query — "the single biggest implementation lever"), `tm-pin-bar`,
`tm-session-menu`, `tm-inbox-button` + panel, a shell state service (menu index, pins, recents,
last-page memory, preferences). Two named seams: optional **page-state contributor** and optional
**page-action publisher**.

**Part 3 questions the brief did not ask (shell):** offline/stale banner (6); page action bar sticky
region and z-order against overlays/banner (7); URL contract (8); print hides chrome (9); shell
telemetry: menu-open rate, palette adoption, pin counts, navigation source share (10).

**Open questions (shell Part 4):** Q1 re-hostable left panel (threshold ~5 menu opens/session); Q2
bottom bar of top-4 pins; Q3 pinned records; Q4 1366×768 check; Q5 promote theme; Q6 record vs page
ranking; Q7 second pin-group level; Q8 site root switcher vs redirect; Q9 last-page server vs device;
Q10 Menu button survival; Q11 Actions tier 2.

### 2.2 Home page customization (home) — feeds 0035 (+0014, 0018, 0027)

Authored 2026-09-15 (memory), **before the 2026-10-02 renumbering**: every spec number in this doc
is the OLD number. Map: old 0011 → 0012 data access, old 0012 → 0013 settings (`SettingKey<T>`,
tags), old 0013 → 0014 users/roles (`User` §3.1, bag §3.4), old 0016 → 0017 blobs, old 0017 →
0018 reference stacks (`me/*`), old 0020 → 0021 notifications (`inbox.changed`). The memory note
uses the NEW numbers. Status: "Not a spec... where they conflict with a later spec, the spec wins."

**Market facts (home §1.1–§1.6):** home is a dashboard, or a fixed-catalogue widget grid (C1);
defaults reach users by pointer until the user edits content (C3); multi-role conflicts need an
authored order (C4: Grafana's accidental highest-team-id rule, Frappe's first role in DB order); lock
levels come in three (C5, NetSuite); reset to default is universal (C6); editing is an explicit mode
(C7). <5% of users change any setting (Spool). WCAG 2.2 SC 2.5.7 requires a non-drag twin for every
drag. Tellma 1.0 had `DashboardDefinitions` (`Code`, `Title×3`, `AutoRefreshPeriodInMinutes`, menu
placement), `DashboardDefinitionWidgets` (`ReportDefinitionId`, `OffsetX/OffsetY/Width/Height`,
`Title×3`, per-widget refresh, `Index`) and `DashboardDefinitionRoles`. Its home was the launcher.
Its "My assignments"/"Assigned by me" were the Inbox (`DocumentAssignments`) and Outbox
(`DocumentAssignmentsHistory`) over documents.

**Decision index (home §2.0).** Firm: D1 one engine — home is a dashboard; D2 `Dashboard` +
`DashboardWidget` rows, one `[JsonColumn]` per widget, no layout blob; D3 widget type registry
(Core + packs); D5 standalone `core.Dashboards`, `Users.HomeDashboardId`, ordered
`core.HomeAssignments`, nothing on `Roles`; D6 pointer not copy, content edits fork with
`BasedOnId`; D8 reset, nudge, admin bulk reset, no silent overwrite; D11 explicit Customize mode
with a non-drag twin for every drag; D15 resolution pointer → rule by ordinal → tenant-wide rule →
platform default, live, server-side; D18 measure the home render source. Leaning: D4 12-column
grid, `X/Y/W/H` + `Ordinal`, compaction; D7 lock levels via `AllowPersonalization` + `IsRequired`;
D9 catalogue v1 (Menu, Recents, Inbox, Text, Links; later Report, assignments, Image); D10 platform
default (assignments row, Recents, Menu); D12 hub-push refresh, polling only while visible, off on
home; D13 dashboard parameters; D14 sharing via `OwnerId` / role visibility / `IsPublic` +
Publish; D16 one column on mobile; D17 revise shell D11 (Menu widget; start page dropped).

**Storage sketch (home §2.2)** — names "illustrative; a spec settles them":
- `Dashboard : TopLevelEntity, IActivatable` — `[Table("Dashboards", Schema = "core"), TableType,
  Temporal, ApiResource]`: `Name/Name2/Name3` (`[Multilingual, Searchable]`), `Code`
  (`[NaturalKey]`, "shared dashboards only"), `Description`, `OwnerId int?` (FK Users; **null =
  shared**), `[ServerOwned] BasedOnId int?` (FK Dashboards), `[ServerOwned] ForkedAt
  DateTimeOffset?`, `IsPublic bool`, `AllowPersonalization bool = true`, `Columns int = 12`,
  `AutoRefreshSeconds int?` (null = off; floor-validated), `[ServerOwned] IsActive = true`;
  collections `Widgets`, `Parameters`, `Roles`.
- `DashboardWidget : ChildEntity` (`core.DashboardWidgets`, Temporal): `DashboardId`, `Type`
  (registry key, e.g. `core.report`, `core.myAssignments`, `core.text`), `Ordinal` (recomputed from
  `(Y, X)` on save), `X` (logical start column, 0-based), `Y`, `W` (1..Columns), `H` (row units),
  `Title/Title2/Title3`, `IsRequired`, `AutoRefreshSeconds int?`, `ReportDefinitionId int?` (FK),
  `[JsonColumn] Config string?`.
- `DashboardRole : ChildEntity` (`core.DashboardRoles`): `DashboardId`, `RoleId` — "visibility
  only, never home assignment".
- `DashboardParameter : ChildEntity` (`core.DashboardParameters`): `DashboardId`, `Key`,
  `Label/Label2/Label3`, `Type` ("the report studio's parameter type vocabulary"),
  `[JsonColumn] DefaultJson`, `Ordinal`.
- Rules: no entity reference and no layout in `Config`; the Text widget's multilingual body lives in
  `Config` (accepted deviation), sanitized, raw HTML never accepted. Rows chosen for FK
  enforcement (a report-definition delete fails naming the dashboards), per-widget server auth,
  temporal history, child sync (null = untouched, `[]` = delete all), and because
  `MaxPreferenceValueBytes` (32 KB default) would cap a bag value.

**Widget registry (home §2.3, D3):** `WidgetTypeDescriptor(Key, LabelKey, ConfigType?,
DefaultSize (W,H), MinSize (W,H), Securable?, Subject)`; key grammar
`^[a-z][a-z0-9]*(\.[a-z][a-zA-Z0-9]*)+$`, `core.*` reserved; `WidgetSubject = None |
ReportDefinition | Image`; `IWidgetTypeRegistry { Types; Find(key) }`; registered through
`FeatureContribution.WidgetTypes(typeof(Holder))`; startup gate checks grammar and uniqueness.
Unregistered `Type` (pack removed) renders a placeholder with Remove. Client: one Angular component
per type, resolved through an injection token keyed by `Type`.

**Where things live (home §2.4, D5):** add `[SelfEditable] int? HomeDashboardId` to `User` (FK;
null = follow the rules; validator: target visible to the caller) — explicitly NOT a bag key,
because the server must read it. New `HomeAssignment : TopLevelEntity` (`core.HomeAssignments`,
Temporal, ApiResource, own securable): `RoleId int?` (null = tenant-wide rule, at most one),
`DashboardId` (must be shared and visible to the role), `Ordinal` (lowest matching wins). Admin
page: list in evaluation order, drag-to-order, "Preview as role" (admin's own data, says so), count
of users each rule reaches.

**Resolution (home D15, §2.4):** server-side for every home render, never cached across a role
change: (1) `Users.HomeDashboardId` if active and visible → Source = Personal (if no longer
visible: fall through, one-time toast, clear the pointer); (2) first `HomeAssignments` row by
`Ordinal` whose `RoleId` is null or among the caller's active roles, dashboard active → Source =
Rule; (3) platform default → Source = Platform. The result "travels with the profile the SPA
already fetches", keyed by `PreferencesTag`, `PermissionsTag` and a tenant-level tag the dashboards
feature bumps.

**Pointer vs copy (home §2.5, D6):** admin assigns (one rule row; live); user "Use as my home"
(pointer; live); user edits content of a home they do not own → private copy `OwnerId = user`,
`BasedOnId = source`, `ForkedAt = now`, widgets and parameters copied, roles not, pointer set to the
copy (stops following). Delta-merge rejected (a grid has no stable anchors).

**Lock levels (home §2.6, D7):** Locked = `AllowPersonalization = false`; Add/Move =
`AllowPersonalization = true` + some `IsRequired`; Unlocked = none required. `IsRequired` is copied
into the fork as data (later admin changes to that widget do not propagate; Q4).

**Reset/nudge/push (home §2.7, D8):** user Reset deletes the private copy only when `BasedOnId` is
set, and clears the pointer; nudge strip when `BasedOn.ModifiedAt > ForkedAt` (dismissal is a bag
key); admin bulk reset scoped to a role, a rule or selected users, with a count ("41 of 63 users
..."), logged.

**Grid (home §2.8, D4/D16):** 12 columns; row unit from size-ladder tokens; compaction on save;
server validation `0 ≤ X`, `X + W ≤ Columns`, `W ≥ MinSize.W`, `H ≥ MinSize.H`, no overlap,
`MaxWidgetsPerDashboard` (40); `X` logical (RTL mirrors via logical properties); below the `md`
breakpoint one column by `Ordinal`, natural heights; optional tablet band halving `W` (Q2).

**Catalogue (home §2.9, D9/D10):** `core.menu` (shell; Launcher browse content; replaces the shell
D11 home mounting), `core.recents` (shell D6), `core.inbox` (notifications spec; "View all" → the
inbox page), `core.text` (multilingual markdown; announcements), `core.links` (route tiles; drops
entries the viewer cannot open), `core.report` (table, chart or KPI-tile mode over a report
definition; parameter bindings in `Config`), `core.myAssignments` / `core.assignedByMe` ("the
module that owns document assignment"; depends on "a documents / workflow spec"), `core.image`
(waits for blob kinds on child entities). Not in v1: embedded URL, calendar, tasks. Platform
default wireframe: My assignments 6×4 + Assigned by me 6×4, Recent pages 12×2, All pages 12×N,
"⚙ Customize"; without a documents module the first row is the Inbox widget alone. Q6: seed it as
a shared dashboard row (`Code = home-default`) instead of composing in code — "Leaning toward the
seeded row".

**Permissions (home §2.10, D14):** securable `core.Dashboard`; Read row criterion "`OwnerId = me`
or `IsPublic` or a `DashboardRoles` row for one of my roles" (a bespoke criterion); Save/Delete on
shared dashboards need the grant; own private dashboards are written through a self-service `me/*`
path (no admin grant needed to fork). **Publish (private → shared) clears `OwnerId`** and requires
Save. `core.HomeAssignment` is its own securable; bulk reset is an action under it. Per-widget auth
at render: "Not available to you" placeholder. Impersonation sees the resolved home read-only.

**Edit mode (home §2.11, D11):** "Customize home" header with [Reset to default] [Cancel] [Save],
[+ Add widget]; the first Save on a shared home performs the fork in the same request; per-widget
menu Move (Up/Down/Left/Right/To…), Resize presets, Title…, Remove (disabled when required);
keyboard: arrows move, `Shift`+arrows resize, `Enter` opens the menu; live-region announcement;
gallery has a Reports tab of readable definitions dropped at `DefaultSize`; session undo stack; the
admin designer is the same component plus `AllowPersonalization` and the per-widget lock.

**Refresh/parameters (home §2.12, D12/D13):** built-ins refresh by hub push (`inbox.changed`; an
`assignments.changed` "when documents land"); report widgets poll only when `AutoRefreshSeconds` is
set, only while visible (Page Visibility API), paused after inactivity, never under a server floor
(30 s in the sketch; a setting); off by default on home. Parameters bind through `Config`, shape
`{"bindings": {"FromDate": "$p.from"}}`.

**Shell revision (home §2.13, D17):** home renders the resolved dashboard; the Launcher is mounted in
two places (overlay + palette), not three; the start-page override is dropped from v1 (Q5: a bag key
later); pins and home defaults "should use one mechanism" — the rule table could serve pins
(`RoleId?`, ordinal, a pin set); flagged, not decided.

**Misalignments (home §2.14):** ARCHITECTURE tier 3; shell D11; `User` column set; pins default;
blob kinds on child entities (now 0017) block the image widget. See §4–§5 below.

**Implementation shape and Part 3 (home §2.15, Part 3):** server `DashboardService`, `HomeService`
(`home` → resolved dashboard with `Source` and `BaseUpdated`; `home/personalize`; `home/reset`),
`HomeAssignmentService`; client `tm-dashboard`, `tm-widget-host` (resolves by `Type`, placeholder
policy, owns refresh), gallery, home route wrapper; seeds by `Code`. Home rides with the profile
fetch or is one cacheable call; widgets load independently with skeletons; "no widget may block
another"; a report-widget failure is a placeholder.

**Open questions (home Part 4):** Q1 12 vs 24 columns / row unit; Q2 per-breakpoint layouts; Q3
widget-level role targeting (`DashboardWidgetRoles`); Q4 live-link required widgets; Q5 reinstate
start-page override; Q6 platform default as a seeded row; Q7 parameterised home pages; Q8 where
private dashboards surface in navigation; Q9 one fork per user.

### 2.3 In-app notifications (notif) — feeds 0028 (+0021, 0025, 0027, 0014, 0011)

Dated 13 October 2026 (after the renumbering: spec numbers are current). Scope: in-app surfaces only
(icons, badges, drawers, pages, toasts, banners); ops telemetry out of scope. The detailed state of
spec 0021 after round 27 is in `f06-notify-stock.md`; this section records the research and what
of it landed.

**Market facts (notif §1.2–§1.6):** assigned work is a live query with its own surface (C1). Copying
work into the bell drifts (C2: Oracle runs a sync job every 2 h). Seen and read are separate states
(C3). Jobs get a job page plus one notice at the end (C4). Broadcasts are their own object, shown as
a banner (C5: GitHub banners must expire or be dismissible). Toasts last 4–8 s, pause on hover, at
most 3–4 visible, never auto-dismiss a critical one, and use a polite live region from page load
(C6). Retention is bounded and evicts rather than blocks (C8). No platform models a tenant-wide
announcement as feed rows. Novu colours the bell by the highest-severity unread item.

**Decision index (notif §2.0).** Firm: D1 bucket by lifecycle (work, operations, activity,
conditions, broadcasts); D2 four surfaces — tray, bell, banner, toast; D3 assignments derived from
a platform `core.Assignments` table, never a bell row; D6 one badge number, unseen, coloured by the
highest unseen severity; D9 one row per condition (`Ongoing`), withdrawn for everyone; D11
announcements a separate record shown as a banner. Leaning: D4 tray accent + "new" dots + arrival
toast, email via a channel-only type; D5 running jobs in the bell's "In progress", terminal notices
only; D7 activity `DedupKey = <resource>:<id>` + `Replace`; D8 verbs read / read all / clear; D10
type `Severity` + `Toast`; D12 tenant markers (sandbox, read-only) vs one banner by precedence.

**Buckets → surfaces (notif §2.1, §2.2):**

| Bucket | Goes away when | Surface | Counter |
|---|---|---|---|
| Work (assigned/returned to me) | work done or handed on | Tray | open count (derived) |
| Operations (my export at 40%) | it ends, leaving a result notice | Bell "In progress" | none; ring on the bell |
| Activity (approved/posted, report shared with me, added to tenant, export ready, import failed) | read, cleared, aged out | Bell | unseen |
| Conditions (balance below threshold, email failing, scheduler gap) | condition clears | Bell, withdrawn on clear | unseen |
| Broadcasts and passing conditions (maintenance, connection lost, new version) | window ends / condition passes | Banner | none |
| Tenant state (sandbox, read-only) — not a notification | tenant state changes | App-bar marker | none |

Surfaces: Tray in the app bar beside the bell; Bell; Banner = "one slot directly under the app bar,
above the pinned bar"; Toast = "top inline-end corner, under the app bar (mirrors in right-to-left)".
"The home page and the launcher reuse the tray's data ('My assignments', 'Assigned by me')." Page
messages (validation, "locked by another user") are inline message bars, not notifications.

**Assignments model (notif §2.3, D3/D4) — DEFERRED, NOT IN ANY SPEC YET:**
- Sketch `Assignment : Entity<int>` (`core.Assignments`; "system-written; Query | Details"):
  `Resource string` (securable resource of the target, e.g. `"gl.Document"`), `TargetId long`
  (unique with `Resource`: one open assignment per target), `AssigneeId int` (FK Users),
  `AssignedById int?` (null = system), `AssignedAt DateTimeOffset` (refreshed on every
  reassignment), `Comment string?`, `ArgumentsJson string?` (caption data, rendered like a
  notification).
- `IAssignments` (scoped; rides the caller's batch): `Assign(IDataBatch, AssignmentRequest)` (upsert
  on `(Resource, TargetId)`), `Release(IDataBatch, string resource, IReadOnlyList<long> ids)` (target
  reached a terminal state).
- Reads mirror the inbox: `assignments/summary` → open count + new count (assigned after
  `UserStamps.AssignmentsSeenAt`), both capped, + latest ten; `assignments/seen` sets the stamp; full
  list = standard search over `core.Assignment` under self-scope `AssigneeId = me() OR AssignedById =
  me()`, widenable by a stored grant for supervisors. After commit, `Assign`/`Release` publish
  `assignments.changed` to the old and new assignee.
- D4 UX: tray count = open assignments, never reset by opening; tray accent while any is new since
  last opened (opening calls `assignments/seen`); new rows dot + sort first; reassignment back
  refreshes `AssignedAt` (returned doc is new again, with the returner's comment); arrival toast
  unless self-assigned ("PO-0042 assigned to you by Sara — *Please check the freight line*", Open
  action; several collapse into "3 documents assigned to you"); ageing shown as age ("3 days"),
  colouring later; email/push via a **channel-only type** (`Inbox = false`).
- Out: pooled / role-routed assignments (one-assignee invariant; later = a `RoleId` alternative).

**Jobs, bell, alerts (notif §2.4–§2.6, D5–D9; mostly landed in 0021, see f06):**
- The bell dropdown opens with "In progress": the user's own Pending, Running and Held jobs (name,
  progress bar, message, cancel), with a progress ring on the icon. Scheduled runs are excluded.
- Terminal notices are ordinary bell rows: `core.export.ready`, `core.import.completed`,
  `core.import.failed`, `core.job.failed`.
- Export flow (client part deferred): the 202 raises the toast "Export started. We'll let you know
  when it's ready", whose Track link opens the bell; completion adds an unseen row plus a toast.
- Badge is unseen and turns the attention colour while an unseen Warning/Error exists. Verbs: read,
  read all, clear.
- Alerts are keyed by rule + anchor (`gl.balance-below:account:1042`) and withdrawn when the
  condition clears. The rule engine is "the rule-engine spec's to decide".

**Severity and toasts (notif §2.7, D10):** severities Info, Success, Warning, Error. Toast table:
assignment to me (not self) yes 6 s; my job's result yes 6 s; my job failed / Error alert yes,
**no auto-dismiss**; Warning alert yes 6 s; activity no; feedback on my own action yes **4 s**. Rules:
pause on hover and focus; ≤3 visible, rest "N more — open notifications"; never the only route;
polite live region present from load (assertive for errors only), never steals focus; **shown in
the visible tab only**; per-user preference turns arrival toasts off.

**Announcements (notif §2.8, D11) — DEFERRED, NOT IN ANY SPEC YET:**
- Sketch `Announcement` stored in the **catalog** database: `Id`, `TenantId int?` (null = every
  tenant of the distribution), `Severity` (`AnnouncementSeverity`: Info | Warning | Critical),
  `StartsAt`, `EndsAt` (required), `Dismissible` (Critical never dismissible), `MessageJson` (text per
  language code `{ "en": ..., "ar": ... }`), `LinkUrl?`, `UpdatedAt` (edits update the same record).
- Authoring by Tellma operators from the control plane through `/api/admin/v1/announcements`.
  Tenant-admin announcements (same record in the tenant DB with role audience) "later, not now".
- Display: client fetches active + upcoming (next 24 h) at shell load, on hub reconnect and every
  **15 minutes**; shows each from `StartsAt` to `EndsAt` by its own clock (no push).
- Dismissal permanent per user in a write-behind preference key holding dismissed ids (named
  `announcements.dismissed` in §3.2), pruned as they end; Critical not dismissible.
- Not in v1: must-acknowledge with audit.

**System state (notif §2.8, D12):** tenant markers beside the tenant name: **"Sandbox"** chip;
**"Read-only"** chip in warning colour with explanation ("You can view data but not change it.
Contact your administrator."); a tenant turning read-only mid-session raises one toast and disables
save buttons with the same reason; narrow screens shrink markers to icons. Banner precedence, one at
a time: (1) "Connection lost. Reconnecting…" (hub down more than a few seconds; critical, not
dismissible, self-clearing); (2) Critical announcements (not dismissible); (3) "A new version of
Tellma is available" (0016's `412 client-outdated`) with Reload, dismissible for the session; (4)
Warning announcements; (5) Info announcements. Within a level, earliest start first; others via a
"1 of 3" pager; a later lower-precedence banner never takes the slot.

**Left out (notif §2.9):** inline Approve/Reject, archive/saved/snooze, watching, digests, browser/OS
push.

**Part 3 proposal (notif §3.1–§3.3):** the 0021/0020/0022/0019/0018 edits below, plus the
deferred items listed after them. §3.3 proposes a NEW spec, "In-app attention: assignments,
announcements and banners", owning the assignment and announcement models, banner precedence,
tenant markers, and the tray and banner client protocols.

**What round 27 actually applied (2026-10-16; verified in the specs):**
- 0021: Context now presents the bell as a badge, a dropdown of latest items and running jobs, a
  notifications page, and read and clear verbs; "Work assigned to a user and an operator's
  announcements are not notifications; a later spec owns both" (Context; Decision 19).
  `NotificationSeverity { Info, Success, Warning, Error }`; descriptor
  `(Key, Category, Mutable, TargetResource, Severity = Info, Toast = false, Inbox = true)` (§2.3);
  row `Severity` stored as **`varchar(8)`** (not `tinyint`) ranked by `CASE`;
  `LOCK_ESCALATION = DISABLE` on `core.Notifications`; `DuplicatePolicy { Suppress, Replace,
  Ongoing }`; `Withdraw`/`WithdrawAsync` + `WithdrawRequest(DedupKey, RecipientIds = null)` (§3.1);
  `IX_Notifications_Dedup`; `InboxSummary(Unseen, MaxUnseenSeverity, Unread, …, Running)` (§5.3);
  `inbox/clear` (`IdsRequest` → `AffectedResult`) and `inbox/clear-read` (§5.2, Decision 22);
  catalogue at tenantless anonymous **`GET /api/notification-catalogue?v=`** (§2.5, Decision 23;
  `NotificationPreferencesResult` removed); badge shows one number (Decision 20); running jobs ride
  the summary (Decision 24); `Ongoing` reserved for re-evaluated conditions — **no Core raiser uses
  it** (Decision 25); key convention `<resource>:<id>` or the type key (Decision 26).
- 0020 §10: `core.job.failed` Error/toast, withdrawn never; `core.job.held` and `core.scheduler.gap`
  Warning/toast, **`Suppress`** with type-key `DedupKey`, withdrawn by `resume`/`cancel` of the last
  held row; `core.schedule.paused` Warning/toast, `DedupKey = 'core.schedule.paused:<scheduleId>'`,
  withdrawn by **`activate` and delete** (not `take-over`). Index `IX_Jobs_RequestedActive
  (RequestedById, CreatedAt DESC, Id DESC) INCLUDE (ScheduledFor) WHERE Status IN ('Pending',
  'Running', 'Held') AND RequestedById IS NOT NULL` (0020 §2.1). Job actions publish `job.changed`.
- 0022: `core.email.failed` keeps **`Replace`**, never withdrawn (§9.3, Decision 9).
- 0011/0016: `/api/notification-catalogue` mapped by `MapTellma()` (0011) and listed among tenantless
  anonymous routes (0016); inbox routes include `inbox/clear`, `inbox/clear-read` (0016).
- 0015: `Notify` and `Withdraw` on `PersistContext`/`ActionContext`.
- 0019: registers `core.export.ready`/`core.import.completed`/`core.import.failed`, whose
  descriptors (severities, toasts) are 0021's catalogue rows.
- 0018: `core.user.added` (descriptor is 0021's) Info, no toast.

**Deferred by round 27 (not in any spec; grep confirms):** `core.Assignments`, `IAssignments`,
`assignments/summary`, `assignments/seen`, `assignments.changed`, `UserStamps.AssignmentsSeenAt`
(0014), the `announcements.dismissed` key (0014), the catalog `Announcements` table, the 0011
admin routes `GET`/`PUT`/`DELETE /api/admin/v1/announcements[/{id}]` and the member-only
`announcements/active` (proposed in-process cache of 1 min), banner slot precedence,
tenant markers, toast timings and toast rules as a client protocol, the 0019 "Export started" toast,
and the `.tmp/` front-end notes. Ahmad's scope rule: "assignments spec and front-end later".

**Open questions (notif Part 4):** (1) read on visit; (2) actor exclusion (raiser convention vs
descriptor flag); (3) generic `core.job.succeeded` for requested non-scheduled jobs whose handler
declares none; (4) assignment ageing threshold and overdue reminders; (5) tenant-admin
announcements now or later; (6) tab title / favicon count prefix; (7) confirm role-routed
assignments stay out.

**Misalignments (notif Part 5):** the 0021 items were fixed in round 27; the front-end notes'
"unread" badge should read "unseen"; the architecture overview should name assignment captions and
rule-based alert types as module contributions.

### 2.4 Report sharing (sharing) — feeds 0033 (+0034 share UI, 0035 dashboards, 0027 launcher)

Dated 13 October 2026; current spec numbers. Assumes 0014's model: many-to-many roles, public roles,
permissions `(Resource, Action, Filter)` with a Queryex filter, bespoke criteria from
`IAccessCriteriaProvider`, one `AccessDecision` per securable whose `FilterTree` the pipeline
conjoins into every read (sharing Part 2 preamble).

**Market facts (sharing §1.2–§1.6):** live reports run under the viewer's security everywhere
(C1: "the same report shows different numbers to different people"); the shared thing is a pointer,
a copy is an explicit "Save as" (C2); role-wide distribution is a stronger privilege (C3); admins
see private content (C4); ownership transfer is an admin function (C5); the leak is scheduled
delivery, fixed by per-recipient rendering (C6). Dashboard → report propagation splits the field;
the non-propagating products (Salesforce, Dataverse, Metabase) report "dashboard shared but its
reports are not" as their top failure. Accidental data grants come from a second query path
(§1.4). Tellma 1.0: admin-authored `ReportDefinitions`, `ReportDefinitionRoles` for menu
visibility (`ShowInMainMenu`), viewer permissions on run.

**Decision index (sharing §2.0).** Firm: D1 sharing shares the definition, never the data (no
run-as-owner, no unrestricted flag, no editor bypass); D2 one model for reports and dashboards; D3
owner + user/role grants at View or Edit + `IsPublic`, pointer not copy, "Save as" copies without
grants; D6 admins act through ordinary grants, others' private hidden until asked; D9 content
outlives its author, bulk transfer, delete blocked while in use; D11 scheduled delivery rendered per
recipient. Leaning: D4 author / share / publish are three grants; D5 warn, don't block, on a
grantee who cannot read the root entity; D7 hub + in-context + Launcher + pins + dashboards; D8
dashboard access reads through to widget definitions; D10 pack-shipped definitions ownerless and
read-only; D12 no interactive data grants in v1.

**D1 consequences (sharing §2.1):** rows are the viewer's; aggregates computed over the viewer's
rows (access filter in the `WHERE`) — the spec "should pin this with a test: a summary's grand
total equals the sum of the detail rows the same viewer sees"; navigations follow 0014's
`[RelatedSelect]` projection bounds; drill-down agrees with the summary; `me()` in a definition
means the viewer; Administrator sees everything only because its grant is unfiltered. Residue: the
definition text itself (filter literals, titles) is exposed to grantees.

**D3 sketch (sharing §2.3)** — "names illustrative":
- `ReportDefinition : TopLevelEntity, IActivatable` — `[Table("ReportDefinitions", Schema =
  "core"), TableType, Temporal, ApiResource]`: `OwnerId int?` (FK Users; **null = standard
  pack-shipped**, D10), `IsPublic bool` (View for every active user, like `Role.IsPublic`),
  `[ServerOwned] BasedOnId int?` (lineage of a "Save as" copy), `Shares` collection. Title, root
  entity, Queryex measures/dimensions/filter, chart, parameters left as "...".
- `ReportDefinitionShare : ChildEntity` (`core.ReportDefinitionShares`, Temporal):
  `ReportDefinitionId`, `UserId int?`, `RoleId int?` (exactly one of the two; a public role means
  everyone), `Access ShareAccess`.
- `enum ShareAccess { View = 1, Edit = 2 }`. "The dashboard entity takes the same three members and
  the same child shape."
- Rules: private by default (owner, no shares, `IsPublic = false`); View runs + copies, Edit also
  changes the definition; only owner and admin-grant holders change shares, transfer, delete;
  **Edit does not confer re-sharing**; `IsPublic` is View only; editing a shared definition changes
  it for everyone; "Save as" = private copy, `BasedOnId` set, **no shares copied**.
- Reading via a bespoke `IAccessCriteriaProvider` criterion: `Read` = "`OwnerId = me()` or
  `IsPublic` or a share to me or one of my roles"; `Save` = "`OwnerId = me()` or an Edit share to me
  or one of my roles"; role permissions add to it.

**D4 grants (sharing §2.4):**

| Ability | Grant | Typical holder |
|---|---|---|
| Author private reports, edit own | `Save` on the report securable, filter `OwnerId = me()` | a "Report author" role, or every user via a public role |
| Share with individual users | `Share` action | report authors |
| Share with roles, make public, place in the menu | `Publish` action | admins, module leads |

Sharing changes are **entity actions (`share`, `publish`)**, not child-collection writes inside a
save ("the spec decides the exact shape"). Open: can a filtered `Publish` say "only roles I belong
to" (Part 4 Q3).

**D5 share-target check (sharing §2.5):** the share dialog asks the server which grantees' `Read`
decision on the report's root entity is `Denied`; shows "Hana cannot read Invoices and will see an
empty report"; for a role, counts denied members. **Needs its own narrow server check** (allowed vs
denied only) because 0014's `access/check` for another user requires user- and role-read grants an
author lacks and returns role-editor info.

**D6 admins (sharing §2.6):** unfiltered `Read` sees every definition (Administrator `*/*`
includes it); unfiltered `Share`/`Publish`; a **`transfer` action** reassigns ownership singly or in
bulk by current owner; hub shows the same scopes plus an "Include others' private reports" toggle
and an "Owned by inactive users" filter; running another's private report shows the admin's data;
"see what a user sees" = impersonation (separate, consented, audited).

**D7 surfaces (sharing §2.7):** (1) reports hub page with tabs Mine / Shared with me / All /
Favorites, filters owner, root entity, module, search; (2) in-context "Saved reports" menu on every
stack's list page (readable definitions rooted at that entity); (3) Launcher — published definitions
with a menu placement (section, sort key, icon — 1.0's `ShowInMainMenu`/`MainMenuSection`, "now a
`Publish` property") appear in browse content for the roles published to; typed search finds every
readable definition under a **Reports bucket**; (4) pins (revoked → disabled with remove); (5)
dashboards and home. A share to an individual posts an inbox notification ("Ahmad shared *Overdue
receivables* with you"); role grants and publishing do not.

**D8 dashboards (sharing §2.8):** owner, `IsPublic`, user and role shares at View/Edit. A dashboard
viewer may render every report widget and drill from it (read-only) even if the definition was never
shared with them; the read-through is authorised by the dashboard, not added to the report's share
list (so it does not appear under "Shared with me"). Widget whose root-entity `Read` is `Denied`:
**viewers see it hidden and the grid reflows; editors see a "Not available to some viewers"
placeholder**; `Filtered` renders normally.

**D9 lifecycle (sharing §2.9):** deactivating a user changes nothing for their shared definitions;
transfer recommended in the deactivation flow ("This user owns 14 reports shared with others —
transfer them?"); delete of a definition used by a dashboard is blocked (FK) with a validation
naming the dashboards, deactivation offered; deleting a shared definition warns with its reach
("shared with 3 users and 2 roles").

**D10 standard definitions (sharing §2.10):** pack seeds by `Code`, `OwnerId = null`, read-only in
the tenant; customise by "Save as"; visibility via `Publish` grants and menu placement seeded with
the pack's baseline roles; copying a published standard report offers to carry its role grants.

**D11/D12 (sharing §2.11–§2.12):** a subscription renders per recipient (Tellma users or roles,
membership resolved at each run); "send my view" would be a separate admin-held securable. The
regional-totals need is met by a role permission on a fact entity, or a **tier-2 report** (reviewed
C# with its own securable), which depends on tier-2 semantics not yet specified.

**Misalignments (sharing §2.13):** ARCHITECTURE tier 3 says nothing on ownership/sharing; the home
research's dashboard model (§5 item 18 below); widget degradation, where "one rule should win".
Part 3: no folders (tags later); audit via temporal history of the share actions.

**Open questions (sharing Part 4):** (1) can an Edit grantee change shares ("Manage" level?); (2)
does publishing to a role move ownership to the system (home research) or keep the author (this
doc); (3) can `Publish` be scoped to "roles I am a member of"; (4) hide or placeholder for viewers;
(5) aggregate-only access beyond tier-2; (6) share cap per definition (Salesforce 25 in the UI).

### 2.5 Charting library (chart) — feeds 0034 (+0024 bundle budgets, tokens in 0002 via 0010)

Research of July 2026, 18 options, "not the authoritative design... A later charting spec will
supersede it". Anchors (chart preamble): Angular-only on `@angular/cdk` + `@angular/aria`;
permissive engine licence (MIT/BSD/ISC/Apache-2.0; copyleft and commercial/OEM disqualify);
signals-first, zoneless, standalone, **"SSR/hydration"**, CI bundle budget; typed tokens as CSS
custom properties, WCAG-contrast-validated at build; axe CI gate; RTL first-class; agent-first
(MCP server + `components.json` + goldens).

**Requirement as researched (chart Part 0):** v1 types Bar (grouped/stacked/normalized, vertical +
horizontal), Pie/Doughnut, Line (regular/stacked/normalized, ± markers), Area
(regular/stacked/normalized), Heatmap, Treemap, Histogram, Gauge; data = **one measure column + 0/1/2
dimension columns** + metadata (colour scheme, gauge thresholds, formatting); click a mark →
drill-through or dashboard cross-filter; performance target **up to 100,000 data points**.

**Field (chart Part 1–3):** SVG collapses at ~3–5k marks, so the renderer decides 100k. **Apache
ECharts**: Apache-2.0, canvas + SVG, LTTB + progressive, 7/8 types native (histogram needs `ecStat`
or manual binning; 100%-normalized stacking not built in), ~110–170 KB gzip tree-shaken (368 KB
full), v6.1.0, ASF; RTL not first-class (#19609); keyboard-nav gap (#18585); click payload carries
`seriesName/seriesIndex/name/value/data/dataIndex`. uPlot: MIT, ~22 KB, Cartesian only. AntV G2:
fallback, no Angular wrapper, no RTL. Disqualified on licence: AG Charts, ApexCharts (proprietary
since 2025-07-09), Highcharts, Google Charts. SVG-only, so rejected as primary: Carbon, ngx-charts,
Unovis (the only Apache-2.0 lib with a first-party A22 wrapper), Observable Plot, Vega.

**Recommendation (chart Part 5):**
- Adopt **Apache ECharts**, consumed as **`echarts/core` + explicit `use([...])`** — "never the
  umbrella import, never `ngx-echarts`" — behind a **Tellma-owned facade** and an in-house
  **~200-line signals/zoneless Angular-22 adapter** (instantiate in `afterNextRender`, outside
  change detection, update via `effect()` on input signals).
- Public API = Tellma's own declarative, JSON-serializable **`TmChartSpec`** (data array +
  measure/dimension roles + typed metadata: palette tokens, gauge thresholds, formatting) with a
  **published JSON Schema**, not an ECharts option passthrough.
- One **`EngineAdapter`** interface (`render` / `update` / `resize` / `dispose` / `export`); ECharts
  adapter first; **uPlot adapter** as proof of the seam and real-time escape hatch; **AntV G2** the
  designated fallback engine.
- Facade owns everything engine-neutral: semantic aggregation (histogram binning, heatmap rollups,
  group-by, top-N + "other"), the **visually-hidden a11y data `<table>`** (doubles as CSV export),
  **roving-tabindex keyboard navigation** over data points, **RTL mirroring + bidi + Arabic-Indic
  numerals** via `Intl` (`numberingSystem: 'arab'`), the CSS-token → theme bridge
  (`getComputedStyle` on `--tm-*`, re-render on theme switch), `Intl` formatting injected into every
  formatter hook, PNG/SVG/PDF/CSV export from the data model, one debounced `ResizeObserver` + a
  `matchMedia('(resolution)')` DPR listener.
- Conventions: `tm-` signal-input components (`<tm-bar-chart>`, `<tm-chart [spec]>`), harnesses,
  co-located `*.examples.ts` feeding docs/MCP, an API-Extractor golden per entry point, an MCP tool
  that validates a `TmChartSpec`.

**Cross-cutting (chart Part 4):**
- The engine owns pixel decimation (`sampling: 'lttb'`).
- a11y: ECharts `aria`, decal patterns, CVD-safe palettes (Okabe-Ito/Wong; Viridis/Cividis).
- Tests: facade → engine-spec unit tests, plus SVG-renderer Playwright visual tests.
- Security: labels are untrusted, so never use HTML-string tooltip formatters.
- **RTL is explicit facade code**: "canvas charts inherit nothing" from logical properties.
- **Palette gap**: the token contract has one brand ramp + a grey ramp only.
- **Lazy-load charts off the initial route.**

**Open questions (chart Part 6):** (1) numeric CI budget + route-level lazy loading; (2) **is 100k
raw client-side points real, or does the server aggregate?** ("the single highest-leverage unasked
question"; if the server aggregates, SVG libraries re-enter); (3) ECharts-v6 + A22 zoneless +
SSR-to-SVG PoC; (4) cross-filter orchestration; (5) agent-DSL bootstrapping (schema + few-shot +
validate/repair loop); (6) currency/decimal correctness of aggregation; (7) add a validated
data-viz palette to the token contract; (8) SBOM/licence gate for bundled assets; (9) optional
Unovis head-to-head if (2) says "server aggregates".

### 2.6 Angular component library analysis (complib, skimmed) — background for 0024/0027

Marked **"Superseded"** by spec 0002. Locked decisions (complib Part 3): D1 CDK + Aria only; D2
`core-*` packages in `client/projects/core/`; D3 `tm-` prefix; D4 `TmXxxPattern` + styled split; D5
signals, zoneless; D6 typed tokens as CSS variables; D7 axe gate, RTL via logical properties; D8
`TmUiStrings` adapter and date/number/currency adapters; D10 Lucide icons behind `TmIconRegistry`;
D11 API goldens, harnesses, bundle budget; D12 docs from source (mentions Storybook, since removed);
D13 scoped MCP. Its v1 working list (Part 4) already names an app shell with "version-refresh banner
+ announcements banner", toast and confirm services, drawer, breadcrumbs, KPI tile, keyboard-shortcut
service, unsaved-changes guard, theme toggle, "global error → toast" and basic charting.

## 3. Gaps for round 2

Capabilities the round-2 designs need that neither these docs nor specs 0011–0022 provide.

**Assignments and attention (0028)**
- **No assignments model exists in any spec.** `core.Assignments`, `IAssignments`,
  `assignments/summary|seen`, `assignments.changed` (via `FeatureContribution.ClientEvent`; 0021
  §6.3 lists four events, per f06) and `UserStamps.AssignmentsSeenAt` are notif §2.3 sketches only.
- **Nothing produces assignments.** No platform entity has a workflow or a non-terminal state (0018's
  Users/Roles/Centers do not). The tray and the brief's home lists need a writer: a reference-distro
  document, a fixture, or a generic "assign this record" action. The "one-assignee invariant" the
  notif doc relies on came from an earlier brief.
- **"Assigned by me" loses history.** The sketch upserts on `(Resource, TargetId)`, overwrites
  `AssignedById` on reassignment and ends the row on `Release`. So "open tasks assigned by the
  current user" shows only rows where the user is still the latest assigner. 1.0's Outbox read
  `DocumentAssignmentsHistory`.
- **Announcements have no owner.** Everything was deferred in round 27 (table, admin routes,
  `announcements/active`, dismissal key). The spec map gives 0028 "toasts and banners" only.
- **Client protocols with no spec:** banner precedence, tenant markers and toasts. The library has no
  toast component (f07), and 0011 exposes no client-facing sandbox signal (f01).
- **Impersonation** (shell D20 banner; home §2.10; sharing D6) is in no tenant-side spec (only
  0003/0020 mention it).

**Shell (0027, 0025)**
- **Recents and last-page storage are undecided** (shell D6 batch persistence; Q9 server vs
  device). 0014 §3.4 already lists "the pages last visited" as a write-behind bag key. Cross-tab
  merge and per-tenant keying are unspecified.
- **Role-seeded pins have no data model** (shell D5 authored ordinals). 0014 §3.4 has only a
  tenant-setting default merged when `nav.pinned` is absent. Confirm the setting key exists (f03).
- **The page-state contributor and the page-action publisher seams** (shell §2.15) are unspecified.
  They overlap 0026's page-context registry and 0031's action model.
- **The Launcher index must merge code entries** (navigation contributions) **with data entries**
  (published reports with section/sort/icon per sharing D7; private dashboards per home Q8). It must
  still be warm-launch cacheable.
- **Missing pieces:** access keys and the reviewed shortcut list (shell D13). Breakpoints are also
  missing — f07: "Breakpoints: none defined". The shell assumes 768/1024/1280 bands, and home §2.8's
  "`md` breakpoint" is a size-ladder rung today.

**Report definitions (0033, 0034)**
- **The entity is only a sharing-sketch skeleton** (§2.3: "..."). All the brief's fields are new
  design: Code, Title×3, Description×3, parameters (Key, Label×3, requirement, default expression,
  type override), select terms with Label×3 and format overrides, pivot options.
- **"Which users can see it, and why" is not designed.** D5 lists only denied grantees, and
  `access/check` covers one user and needs grants an author lacks.
- **No route design** for the share-target check, `share`/`publish`/`transfer`, the hub scopes and
  toggles, the per-entity "Saved reports" query, or Favorites storage (D7 names a tab, no store).
- **Tier-2 ("hardcoded") reports have no securable/registry design.** Sharing D12 depends on it, and
  the brief asks whether they get read-only rows.

**Dashboards and home (0035)**
- **No dashboard tables, widget registry, `HomeAssignments` or home endpoints exist.**
  `User.HomeDashboardId` is absent from 0014 §3.1. `[SelfEditable]` does not exist: self edits go
  through 0018 §3.1 `[ApiAction("me/save", MemberOnly = true)]` with `MeSaveRequest`, mirrored by
  0014 §7.1 `UserProfile`.
- **Home resolution vs warm launch.** Home §2.4 resolves "on the server for every home render, never
  cached across a change of role membership". Rule 2A forbids waiting on the network for chrome.
  0035 needs a cached resolution under `PreferencesTag`/`PermissionsTag` plus a dashboards tag, and
  a stale-then-refresh policy.
- **Refresh has no scheduler.** The brief wants minutes, a silent swap, "last refreshed" and a force
  refresh that clears; home D12 adds visibility-only polling, an inactivity pause and a server floor.
  There is no cross-tab rule and no setting key for the floor.
- **Cross-filter orchestration** is "entirely Tellma's to design" (chart Part 4 §15, Q4).

**Charts (0034; tokens via 0010's amendment of 0002)**
- **No chart component, data-viz palette or shared `Intl` formatting service exists** (f07; chart
  Part 4 §16).
- **No server binning.** f08 shows no binning function in Queryex, yet the brief aggregates in SQL
  and chart Part 5 bins in the client facade.
- **The charts bundle budget is unquantified** (chart Q1). ECharts is ~110–170 KB gzip as a lazy
  chunk.

## 4. Likely change points

- **0021 (Context, §2.3, §5.4, §6.3)**: 0028 registers `assignments.changed`, places the tray beside
  the bell, and adds an "assignment arrived" toast that is not a bell row (an optional
  `Inbox = false` email type is already allowed by §2.3).
- **0014 §3.3 / §3.4 / §3.1**: `UserStamps.AssignmentsSeenAt`; the `announcements.dismissed` key;
  the `nav.pinned` default (tenant setting vs role seeds); recents/last-page keys if the bag is
  chosen; `HomeDashboardId` (plus 0018 §3.1 `MeSaveRequest`) only if home personalization ships —
  the brief says "for now the default is used for everyone".
- **0011 (admin surface §5.9, catalog, tenant data to the client)**: the announcements table and
  routes, if 0028 owns them. Expose tenant `Category` (sandbox) and `State` (`ReadOnly`) for the
  markers.
- **0016 route catalogue**: assignment routes, `announcements/active`, the report-definition actions
  (`share`, `publish`, `transfer`, share-target check), and the dashboard/home routes.
- **0020 §10**: `core.job.succeeded`, if 0028 answers notif Q3 "always notify".
- **0019**: the deferred "Export started" toast; the 202 answer must carry what Track needs.
- **ARCHITECTURE.md "Reports — three tiers"** (line ~709): tier 3 still reads "analytics, BI,
  dashboards ... Out of scope". 0033/0035 contradict it and must log the change.
- **Not editable**: the research docs are frozen (docs/AGENTS.md). The shell D11 revision that home
  §2.13 asks for must be stated in 0027/0035, not applied to `erp-shell-design.md`.

## 5. Contradictions and surprises

**Spec-number and naming traps**
1. **The home doc uses pre-2026-10-02 spec numbers.** Its "spec 0013 §3.1/§3.4" is today's 0014;
   "0012 `SettingKey<T>`" is 0013; "0020 `inbox.changed`" is 0021; "0016 blob kinds" is 0017;
   "0017 `me/*`" is 0018. The notif, sharing and memory notes use current numbers.
2. **The home doc's `[SelfEditable]` does not exist** (§3, Dashboards). **The brief's link to the
   chart research is misspelled** (`chaating-library-analysis.md`).

**Shell research vs the brief**
3. **Bar heights.** The brief has a 40px app bar + 32px pinned bar. Shell D1/D17 has 48 + 40 within
   a ≤96px budget.
4. **Warm-launch landing.** The brief says a warm launch of the base URL lands on
   `/<tenant-id>/home` of the last tenant. Shell D12 (Firm) restores the last *page* on the first
   navigation after boot to the tenant home. Home §2.13 keeps D12 "untouched". Both cannot hold.
5. **Routes.** The brief asks `/<tenant-id>/home` vs `/<tenant-id>` (Q2), and `/tenants` vs the base
   URL (Q1, which changes 0011 §5.5). Shell §2.8 uses `/<tenant-id>` as home and leaves `/` as
   switcher-or-redirect (Q8).
6. **Tenantless chrome.** The brief has wordmark, user email, identity-server user name and a
   language selector. Shell §2.8 has "identity and sign-out only".
7. **Row 1 contents.** The brief's PWA-only back/forward buttons, assignments tray, help button and
   assistant launcher are absent from the shell doc (D15 makes back a page concern; §2.14 only
   "reserve[s] the slot" for AI). D20 caps distribution bar items at 3.
8. **Launcher scope.** Brief v1 searches pages only (multilingual, per-screen synonyms), keeps the
   text on Esc and caches the results. Shell D2 has Pages/Records/Actions/Settings & Help buckets
   with `/ > # ?` prefixes, and sharing D7 adds a Reports bucket. No doc mentions synonyms.
9. **Session menu.**
   - "My account": the brief links to identity-server account management; the shell has a profile
     modal "as in the old shell".
   - "Switch Tenant": the brief goes back to the picker; the shell switches in the menu to that
     tenant's last page.
   - Density: shell-only, "if and when a runtime density switch exists". Runtime density is
     deferred (ARCHITECTURE), so omit it.
   - About: the brief's modal (component versions, copyright, ToS/privacy) is richer than the
     shell's "about (version)".
10. **Pins.** The brief lets the user hide the pinned bar on any screen; the shell makes chips
    "user-dismissible" on mobile only. Defaults: shell D5 (per-role seed, cap ~12) vs 0014 §3.4 (one
    tenant setting). Home §2.13 proposes one rule mechanism for both.

**Home research vs the brief**
11. **Default home.** The brief: a greeting ("Good morning, Adam" + weekday and date), 10 assigned to
    me, 10 open assigned by me, top recents. Home D10: My assignments, Assigned by me, Recent pages,
    an **All pages (`core.menu`)** widget and "⚙ Customize". No doc has the greeting. Shell D11 has
    the launcher content only.
12. **Scope.** The brief: "for now the default is used for everyone"; per-role and per-user
    swapping is future. The home doc designs it all now (rules, forks, locks, bulk reset). 0035
    decides what ships.
13. **Dashboard fields.**
    - Brief: `Code`, `Title×3`, refresh **in minutes**; per report `Title×3` + `Description×3` +
      size/position + a refresh override.
    - Home sketch: `Name×3`, `Code`, one `Description`, **`AutoRefreshSeconds`**, widget `Title×3`
      only, 12 columns.
    - The brief's dashboard is "1 or more reports"; home widgets include many non-report types.
14. **Parameters and refresh.**
    - Home D13 answers the brief's Q1 (same key vs mapping) with a mapping
      (`{"bindings": {"FromDate": "$p.from"}}`).
    - The brief's "required parameter without default = validation error" is absent from the home
      doc.
    - The brief wants silent auto-refresh; home D12 makes refresh **off by default on home** and
      polls only while visible.

**Sharing research vs the brief**
15. **Hub surfaces.** The brief's tabs are "My reports / Shared with me / All reports". Sharing D7
    adds Favorites and an in-context "Saved reports" menu on every list page.
16. **Grants.** The brief's "every user can create / can share with any user" becomes the D4
    grants (`Save` filtered `OwnerId = me()`, `Share`), which a tenant may give everyone through a
    public role.
17. **Unanswered and partly answered.** The admin question ("which users can see it, and why") is
    unanswered. Brief Q6 (author deletes a published report) is partly answered: delete is blocked
    while a dashboard uses it, with a reach warning. Ownership on publish is open (sharing Part 4
    Q2).

**Home research vs sharing research** (flagged in both memory notes; undecided)
18. **Sharing model.**

    | | Home research (D14, §2.10) | Sharing research (D3, D8, §2.13) |
    |---|---|---|
    | Shared dashboard | `OwnerId = null` | keeps its `OwnerId` |
    | Role visibility | `DashboardRoles` (visibility only) | share table `UserId` xor `RoleId`, View or Edit |
    | Publish | clears `OwnerId` | a separate grant; the author stays owner |
    | `OwnerId = null` means | shared | pack-shipped standard |

    Sharing recommends replacing `DashboardRoles` with the share table. Home assignments and forks
    are unaffected.
19. **Report-widget authorization is opposite.** Home §2.2: "A `core.report` widget renders only if
    the viewer may read that report definition". Sharing D8: dashboard access *reads through* to the
    definition.
20. **Degradation.** Home §2.10 shows "Not available to you" to every viewer. Sharing D8 hides the
    widget from viewers (the grid reflows) and shows editors "Not available to some viewers".
21. **Own-dashboard write path.** Home: a self-service `me/*` path. Sharing: a bespoke `Save`
    criterion on the ordinary securable plus a `Save` grant filtered `OwnerId = me()`.
22. **Seeded content.** Home Q6: a seeded shared `home-default` row "admins then edit like any
    other". Sharing D10: pack-shipped ownerless content is **read-only, copy to customise**, and
    dashboards take "the same model".

**Notifications research vs shell research**
23. **Banner position.** Shell D20 puts it "below the bar" (the two-row bar). Notif D2 puts it
    "directly under the app bar, above the pinned bar".
24. **Banner contents.**
    - Shell D20: impersonation, offline/stale data, "subscription or environment warnings".
    - Notif D12: sandbox and read-only are **app-bar markers, not banners**. Its precedence list
      (connection lost, critical announcement, new version via `412 client-outdated`, warning,
      info) has no impersonation entry.
    - 0011's open "sandbox banner" note (f01) conflicts with the marker.
25. **Badge.** Shell D10 shows the unseen count plus a plain dot when there is unread but nothing
    new; it calls the bell "Inbox". Notif D6 and 0021 Decision 20 show one unseen number coloured by
    severity, with no dot rule.
26. **Tray.** The shell doc has no tray. Notif says "the home page and the launcher reuse the tray's
    data", but the shell's Launcher has no assignments band.

**Charting research vs the brief and the platform**
27. **The brief answers chart Q2.** Charts show "the result of an aggregate query", and the pivot is
    aggregated in SQL ("the client does not perform any aggregations of its own"). This weakens the
    100k-raw-point premise that excluded the SVG libraries (chart Q9: Unovis re-enters), and
    collides with the facade owning semantic aggregation (binning, rollups, group-by, top-N).
28. **The SSR rationale is moot.** ARCHITECTURE: "No server-side rendering ... No distribution ...
    uses SSR or hydration" (f07 agrees). SVG still serves print, export and visual tests.
29. **Palette contrast.** Chart Q7 wants the palette WCAG-validated at build. ARCHITECTURE says
    contrast "is measured by the axe CI gate over the rendered components, not by token
    arithmetic".
30. **Chart types and tooling.** The 8 types (incl. Heatmap, Treemap, Histogram, Gauge) are the
    chart doc's own list; the brief says only "0, 1, or 2 dimensions, and exactly 1 numeric
    measure" plus simple customizations, so 0034 picks the v1 set. The chart doc and complib D12
    assume Storybook, which was removed 2026-07-04; examples feed `showcase` and `components.json`.

**Round 27 vs the notif doc** (easy to cite wrongly)
31. **What the specs actually say:**
    - Row `Severity` is `varchar(8)`, not `tinyint`.
    - Core platform conditions use `Suppress` + withdrawal, and no Core raiser uses `Ongoing`.
    - `core.email.failed` keeps `Replace` and is never withdrawn.
    - `core.schedule.paused` is withdrawn on `activate` and delete, not `take-over`.
    - The catalogue is `GET /api/notification-catalogue?v=`, and `NotificationPreferencesResult`
      is gone.
    - `IX_Jobs_RequestedActive` adds `Id DESC`, `INCLUDE (ScheduledFor)` and
      `RequestedById IS NOT NULL`.
32. **"Assigned work is never a bell row" is spec text** (0021 Decision 19). 0028 may toast an
    assignment, or send a channel-only (`Inbox = false`) email type, but must not insert a
    notification row.

# Home Page Customization — Market Analysis and Recommendation

**Purpose:** Settle how the Tellma 2.0 home page — the page behind the wordmark and the tenant home
route — is composed, who configures it, how a user's own customization relates to administrator
defaults under a many-to-many user↔role model, how all of it is stored, and whether "home page" and
"dashboard" are one concept or two.

**Status:** Research and recommendation. Not a spec. The recommendations are inputs to a future
dashboards-and-home spec; where they conflict with a later spec, the spec wins.

**Relationship to the shell research.** [ERP Shell Design](erp-shell-design.md) decided in D11
(Leaning) that home renders the Launcher's browse content, with a user-settable start page as the
escape hatch for executives. This document revises D11 — see
[§2.13](#213-what-changes-in-the-shell-research--d17) — and leaves D12 (last-page restore)
untouched. D6 (recents) becomes one of the built-in widgets.

**Method.** Nineteen products were examined through vendor documentation and design guidance. Where
the decisive rule was not documented — what happens when a user holds several roles — the shipping
source was read (Grafana's preference store, Frappe's home-page resolver). Published UX research on
customization behaviour, defaults and dashboards was reviewed, and Tellma 1.0's own dashboard schema
was re-read. Cells that rest only on secondary sources are marked *(2°)*. Confidence markers are
those of the shell document:

| Marker | Meaning |
|---|---|
| **Firm** | Well supported by evidence and by platform conventions already in force. Changing it should require new information, not a preference. |
| **Leaning** | The better of two defensible options. Worth revisiting if implementation or usage data pushes back. |
| **Open** | A genuine coin-flip, or something that needs telemetry or a usability test. Listed in [Part 4](#part-4--open-questions). |

---

## The five questions, answered in brief

| # | Question | Short answer | Detail |
|---|---|---|---|
| 1 | Storage format — a JSON bag? | **Rows, with one JSON column per widget.** A `Dashboard` entity with `DashboardWidget` child rows. Grid position, ordinal, title and every reference to another entity (report definition, image) are typed columns with real foreign keys; only the widget type's own settings live in one opaque `[JsonColumn]`. A whole-layout JSON blob is rejected. | [§2.2](#22-storage--d2), [§2.3](#23-the-widget-type-registry--d3) |
| 2 | On Users, on Roles, or a standalone table? | **Standalone.** `core.Dashboards` (+ widgets, parameters, role visibility). Users hold one nullable pointer, `HomeDashboardId`. Roles hold nothing; an ordered rule table maps roles to default home dashboards. A user's personalized home is itself a private dashboard row that remembers what it forked from. | [§2.4](#24-where-the-pieces-live--d5), [§2.5](#25-pointer-not-copy--d6) |
| 3 | Bulk changes for many users; which of several roles? | **Edit the shared dashboard the rule points at** — everyone who has not forked sees it immediately, because assignment is a pointer. Ties between roles are broken by an **explicit, authored ordinal** on the rule table, never by inference. Union-of-layouts is rejected for a 2-D grid. Plus: preview-as-role, and an explicit bulk "reset personalized home pages". | [§1.5](#15-the-accidental-tie-breaks), [§2.4](#24-where-the-pieces-live--d5), [§2.7](#27-reset-nudge-and-push--d8) |
| 4 | Is it fine that a customizing user is disconnected forever? | **Yes, with four mitigations** that the field converged on: only *content edits* fork (choosing a different home does not); the fork keeps its lineage so "Reset to default" and "the default changed since you customized" are one query away; admin-**required** widgets travel with the fork and cannot be removed; the admin can **lock** a default and can **bulk-reset** forks. Delta-merging a fork against a changed base is rejected for grids. | [§2.5](#25-pointer-not-copy--d6), [§2.6](#26-lock-levels--d7), [§2.7](#27-reset-nudge-and-push--d8) |
| 5 | Reuse the dashboard concept for home? | **Yes — one engine.** The home page *is* a dashboard rendered at the home route. What makes it feel like a home page is the widget catalogue (viewer-relative widgets such as My assignments, Recents, the menu) and two rendering policies (no parameter bar by default, a built-in fallback), not a second model. No product examined maintains two layout engines. | [§1.2](#12-what-the-field-has-converged-on), [§2.1](#21-one-engine--d1) |

---

## Part 1 — Market analysis

### 1.1 The field at a glance

| Product | What "home" is | Who authors the default | How defaults reach users | Rule when a user holds several roles | What the user may change | Pointer or copy | Reset |
|---|---|---|---|---|---|---|---|
| **SAP S/4HANA Cloud — My Home** | Fixed-catalogue page: To-Dos, Pages, Apps (favourites, recents), Insights tiles and cards, News *(2°)* | Key user, via Adapt UI → Personalize My Home *(2°)* | Launchpad content is assigned by business role; a user with several roles sees the union of their spaces — duplicate space names included *(2°)* | Union | Show, hide and reorder sections; add and hide insight cards *(2°)* | Layered: personal changes on top of the key-user layer *(2°)* | Yes *(2°)* |
| **D365 Business Central** | The Role Center — a page per profile | Administrator customizes pages per profile | One profile per user; a **default profile** for the unassigned; users may switch role in My Settings | Avoided by the model: one profile at a time | Personalization (show, hide, move); can be disabled per profile | Layered: personalization is a delta that "isn't associated with a profile" | User: Clear personalization. Admin: clear per user, or per page |
| **D365 Finance & Operations** | Default dashboard of workspace tiles; a per-user **Initial page** option | Administrator, on the Personalization admin page: copy personalizations to users, import for users | Saved views are published to **security roles**; personalizations are copied to named users | Union of published views *(2°)* | Full toolbar: hide, move, add tiles, resize; explicit save to a view | Copy | Clear tool; admin clears per user |
| **Oracle NetSuite** | A dashboard — one per role; switching role switches dashboard *(2°)* | Administrator publishes a dashboard to roles of the same center | Per role, at next login. "Override existing user's settings" defaults to **No** — new users only | Avoided: the user works under one role at a time, with a default role for login *(2°)* | Depends on **Mode**: Locked / Add-Move Content (cannot remove published portlets) / Unlocked | Copy on publish; personal changes are local until an explicit override re-push | The override re-push |
| **Oracle Fusion (Redwood)** | Home with Ask Oracle | Administrator picks the home layout in the theme *(2°)* | Theme-wide *(2°)* | — | Minimal *(2°)* | — | — |
| **Salesforce Lightning** | A Lightning page assembled from components | Administrator, in App Builder | Org default → app default → app + profile; the most specific assignment wins | Avoided: one profile per user | **None** for end users — a standing IdeaExchange request *(2°)* | Pointer | n/a |
| **ServiceNow (Next Experience)** | Landing / start page | Administrator: ordered homepage destination rules by role or user criteria; workspace landing pages ordered by an Order field *(2°)* | Rule order *(2°)* | First matching rule by order *(2°)* | User sets a "Current start page" — a dashboard qualifies — stored as a user preference *(2°)* | Pointer | Clear the preference |
| **Workday** | Home of cards | Administrator: Manage Configurable Cards, published to hubs | Security groups and condition rules per card; at most **10** published cards on a hub overview page | Union, card by card | Rearrange and pin apps; cards themselves are not user-editable *(2°)* | Pointer | — |
| **Odoo** | The first app, or the user's **Home Action** — any action, a dashboard included *(2°)* | — | — | — | Pointer to any action *(2°)* | Pointer | Clear the field |
| **Xero (2025 homepage)** | Widget dashboard, up to four columns | No admin layer: "your setup is unique to you and won't change how others … see their homepage" | — | — | Show, hide, resize and reorder; a layout is saved per screen size *(2°)* | Per user only | Reset to Default *(2°)* |
| **Acumatica** | Role-based dashboard | Users with the dashboard designer role edit the "stock" dashboard | Dashboards are visible to roles *(2°)* | — | If the owner ticks "Allow users to personalize": **Create user copy**, then edit | Copy; later stock changes do not reach copies; a copy cannot be shared | Revert to stock (drops the copy) |
| **Grafana** | Home dashboard — any dashboard | Server config → organization → team → user preference | "The lowest level always takes precedence"; the user's choice "overrides all home dashboards set at higher levels" | **Undocumented.** In source the rows are read `ORDER BY user_id ASC, team_id ASC` and the last row wins, so among several teams the one with the **highest id** wins | Pick any dashboard; edit shared dashboards if permitted | Pointer | Clear the preference |
| **Tableau** | Start page | Administrator: "Set as Start Page … for Users on this Site" or "for All Server Users" | Site- or server-wide | — | A user's own start page overrides the administrator's | Pointer | Reset to Default in account settings |
| **HubSpot** | Default dashboard | Super Admin **presets** per user group *(2°)* | Presets | — | Users override presets with their own default *(2°)* | Pointer | — |
| **Jira Cloud** | The system dashboard until the user picks another *(2°)* | Administrator edits the system dashboard, which cannot be deleted | Everyone | — | Users create dashboards and share them with groups, projects or people | Pointer | — |
| **Frappe / ERPNext** | `home_page` on Role; else portal setting, hooks, site setting; the user's default workspace overrides at the end | Per role | Roles are scanned and the **first** with a home page wins, in whatever order `get_roles()` returns them | First role in database order — unspecified | Default workspace; private workspaces *(2°)* | Pointer | Clear |
| **Splunk** | Default app per role; user override | Per role | — | **Alphabetical by app name**, observed by users; undocumented *(2°)* | User setting | Pointer | — |
| **Notion** *(reference class)* | Fixed widget page: Recents, Upcoming events, My tasks, database views *(2°)* | — | — | — | Show or hide widgets; choose the start page (Home, last visited, AI, top sidebar page) *(2°)* | Pointer | — |
| **Microsoft Viva Connections** *(reference class)* | Dashboard of cards | Author / administrator | Each card audience-targeted to up to 50 Entra groups; readers see the filtered set, authors see everything in edit mode *(2°)* | Union, card by card | None | Pointer | — |

### 1.2 What the field has converged on

**C1 — Home is a dashboard, or a dashboard in disguise.** Half the field makes the home page a
dashboard outright, chosen from the same catalogue as every other dashboard: NetSuite, Acumatica,
Grafana, Jira, HubSpot, Tableau, D365 F&O, Splunk, ServiceNow, Odoo. The other half ships a
"home page" with a fixed first-party widget catalogue — SAP's My Home, Workday's cards, Salesforce's
Lightning Home, Xero's homepage, Notion's Home — and every one of those is a grid of independently
positioned, individually configurable panels. The only real difference between the two camps is
whether the widget catalogue is open (any report) or closed (first-party widgets), and both camps are
drifting toward the middle: SAP added Insights cards (report cards) to My Home; Jira ships an
"Assigned to me" gadget for its dashboards. Nobody maintains two layout engines.

**C2 — The administrator's default is the product; user customization is the exception it tolerates.**
Salesforce and Workday allow end users no layout editing at all. Notion allows show/hide only.
NetSuite ships three lock levels. Acumatica gates personalization behind an owner-set flag. Business
Central lets an administrator disable personalization per profile. The research in §1.4 explains why
the products are right to weight it this way.

**C3 — Defaults reach users by pointer, not by copy — until the user edits content.** Grafana,
Tableau, HubSpot, Jira, Salesforce, ServiceNow, Odoo and Notion all treat "your home" as a
*reference* to a page; editing the shared page updates everyone who points at it. Copies appear only
where users may edit the content of a shared page (Acumatica's user copy, NetSuite's personal
changes), and both products then had to bolt on a repair mechanism — Acumatica's revert-to-stock,
NetSuite's override re-push — because a copy silently stops following the default.

**C4 — Conflicts between several roles are resolved by an explicit order, or avoided by the model;
where neither was done, behaviour became accidental.** Business Central, Salesforce and NetSuite
avoid the question by construction: one profile or one role at a time. ServiceNow and Salesforce use
an ordered or most-specific rule. Union works only for *list-shaped* content — Fiori's spaces, F&O's
published views, Viva's cards — and even there SAP documents the cost (duplicate spaces). The
products that never decided are the cautionary tales of §1.5.

**C5 — Lock levels come in three.** NetSuite names them: Locked (no changes), Add/Move Content (users
may add and rearrange but "not the predefined ones or those added as part of the published
dashboard"), Unlocked. Business Central's "Disable personalization", Acumatica's "Allow users to
personalize" and Workday's admin-only cards are the same ladder with fewer rungs.

**C6 — Reset to default is universal wherever customization exists, and the administrator has a bulk
form of it.** Tableau, Acumatica, Xero, SAP, F&O's Clear tool, Business Central's Clear
personalization — all per user. Business Central additionally clears per user or per page from the
admin side; F&O has an organization-wide personalization admin page; NetSuite's override re-push is
the bulk reset in disguise.

**C7 — Editing is an explicit mode with a gallery, then Save.** Xero's "Edit homepage" with "Add
widget"; SAP's "Personalize My Home"; NetSuite's "Personalize"; F&O's Personalization toolbar, which
requires an explicit save when views are on; Notion's Show/Hide Widgets menu. Nobody edits a home
page in place by accident.

### 1.3 Where products genuinely disagree

**Open catalogue versus fixed catalogue.** Discussed under C1; the recommendation takes the open
catalogue with first-party widget types in it, which is where both camps are heading.

**One layout with reflow versus a layout per breakpoint.** Xero saves a layout per screen size *(2°)*
and its own product-ideas forum carries requests to "simplify layout" and cap the column count —
users found several layouts of the same page confusing. Grafana and react-grid-layout's default
stack panels into one column on narrow screens from a single layout; Metabase does the same. The
recommendation is one layout and a deterministic reflow (D4, D16).

**Delta versus copy.** Business Central is the only product that layers personalization as a *delta*
over the profile's customization — and it can, because its pages are structured forms with named
controls and placement verbs (`moveafter`), so a delta has stable anchors. Every grid-based product
copies. §2.5 explains why a grid cannot take the Business Central route.

**Columns versus a free grid.** Jira's dashboards are column stacks (`column_number`,
`positionseq`); NetSuite's portlets sit in two or three columns; Grafana, Metabase, Superset and
Xero are free 2-D grids with compaction. The trend is the free grid; Tellma 1.0 already had one
(`OffsetX`, `OffsetY`, `Width`, `Height`).

### 1.4 What the published research says

**Almost nobody changes defaults.** Jared Spool collected several hundred users' Microsoft Word
settings files: fewer than 5% had changed *any* setting, and interviewees assumed the shipped
configuration was a recommendation. Wendy Mackay's CHI '91 study of 51 workstation users found
customization rare and social — two-thirds cited lack of time, a third did not know how, and a few
local experts customized on everyone's behalf.

**Role-based personalization works; individual customization mostly does not get used.** Nielsen
Norman Group's intranet-portal research (Jakob Nielsen, 2011) concluded that "role-based
personalization worked well, individualized personalization less so", that busy professionals "often
see the need to mess with UI preferences as an annoyance", and that the right compromise is a
*My Page* "populated through role-based personalization, with additional customization available"
— while warning that many users "will simply use the default My Page without customizing it". The
2016 NN/g piece on customization versus personalization adds the mechanism: customization carries a
higher interaction cost, suits users who already know their goals, and — like personalization —
should "enhance an already good experience, rather than try to fix a poor one".

**Dashboards are for glancing, not exploring.** NN/g's dashboard guidance: information "consumed
fast, with a minimum of interaction or cognitive processing"; encode quantities with length and
position; and "it's the designer's job to prioritize the ideas and create a tight base-level design
that can subsequently be augmented through personalization and customization". Cards are the right
container for a page that mixes content types.

**Drag-and-drop now has a compliance floor.** WCAG 2.2 (Level AA) added Success Criterion 2.5.7
Dragging Movements: every function operated by dragging must also be achievable by a single pointer
without dragging, unless dragging is essential. An edit mode that moves and resizes widgets only by
drag fails it; a per-widget menu with Move and Resize entries, and keyboard equivalents for 2.1.1,
satisfies it.

**Consequence for the design.** The administrator-authored default and the platform default *are*
the feature for roughly 95% of users; the customizing 5% must be served well but never at the
expense of the first group. Every investment decision below follows that ordering: platform default
first, role defaults second, user customization third.

### 1.5 The accidental tie-breaks

Three products let a user hold several roles or teams, let each role carry a home page, and never
wrote down the rule. All three now have a rule — one that nobody chose:

- **Grafana.** The documentation promises only that lower levels win. The store reads the
  organization row, the team rows and the user row in one query ordered `user_id ASC, team_id ASC`,
  and `GetWithDefaults` loops over the rows letting each non-empty value overwrite the previous one.
  The user row wins, correctly; between two teams, the one with the **higher team id** — the more
  recently created team — wins. Issue #51610 ("Home Dashboard not working anymore for Teams") is one
  visible consequence.
- **Frappe.** `get_home_page()` fetches `home_page` for all the user's roles with one `get_values`
  call and takes the first non-null — in whatever order the roles came back from the database.
- **Splunk.** Users report that with several roles carrying different default apps, the winner is the
  alphabetically first app name.

The lesson is not that pick-one is wrong — it is the dominant model — but that the order **has to be
authored data**, visible on an admin page, with the tenant-wide fallback last. The shell research
reached the same conclusion for role-seeded pins (D5: "roles carry no inherent priority, so the
ordinal has to be authored, not inferred").

### 1.6 Where Tellma 1.0 stood

The 1.0 schema already had the shape this document recommends, minus home:

- `DashboardDefinitions` (`Code`, `Title×3`, `AutoRefreshPeriodInMinutes`, main-menu placement),
  `DashboardDefinitionWidgets` (`ReportDefinitionId`, `OffsetX`, `OffsetY`, `Width`, `Height`,
  `Title×3`, per-widget refresh, `Index`), and `DashboardDefinitionRoles` for visibility. Rows, not
  a blob; a real FK to the report definition; a free 2-D grid.
- The home page was the launcher; nothing on it was configurable.
- "My assignments" and "Assigned by me" existed as the **Inbox** (`DocumentAssignments`) and
  **Outbox** (`DocumentAssignmentsHistory`) views over documents — the data these two widgets need
  once a 2.0 documents spec exists.

What 1.0 lacked: private dashboards, dashboard parameters, any home assignment, any personalization.

---

## Part 2 — Recommendation for Tellma

### 2.0 Decision summary

| # | Decision | Choice | Firmness |
|---|---|---|---|
| D1 | Home versus dashboard | **One engine.** Home is a dashboard rendered at the home route | Firm |
| D2 | Storage | `Dashboard` entity + `DashboardWidget` child rows; typed layout and reference columns; **one `[JsonColumn]` per widget** for type-specific settings; no layout blob | Firm |
| D3 | Widget types | A **registry** contributed by Core and packs: key, config schema, default and minimum size, optional securable | Firm |
| D4 | Grid | 12 columns, fixed row unit, `X/Y/W/H` + `Ordinal`; vertical compaction; server-validated bounds and no overlap | Leaning |
| D5 | Where things live | `core.Dashboards` standalone; `Users.HomeDashboardId` pointer; `core.HomeAssignments` ordered rules; nothing on `Roles` | Firm |
| D6 | Pointer, not copy | Choosing or assigning never copies; **editing content forks** into a private dashboard with `BasedOnId` | Firm |
| D7 | Lock levels | `AllowPersonalization` on the dashboard, `IsRequired` on the widget — NetSuite's three modes with two flags | Leaning |
| D8 | Reset, nudge, push | User Reset to default; "updated since you customized" nudge; admin **bulk reset** with a count preview; no silent overwrite | Firm |
| D9 | Widget catalogue v1 | Menu, Recents, Inbox, Text, Links now; Report, My assignments, Assigned by me, Image as their dependencies land | Leaning |
| D10 | Platform default home | Assignments (or Inbox) row, Recents, Menu — composed from the registry when the tenant has configured nothing | Leaning |
| D11 | Edit mode | Explicit Customize mode, widget gallery, explicit Save / Cancel, per-widget menu and keyboard for every drag | Firm |
| D12 | Refresh | Built-ins update by hub push; report widgets poll only while visible, with a server-enforced floor; **off by default on home** | Leaning |
| D13 | Parameters | Dashboards may declare them; home renders defaults and shows the bar only when declared | Leaning |
| D14 | Sharing model | `OwnerId` (private) / role visibility / `IsPublic`; **Publish** promotes a private dashboard to shared | Leaning |
| D15 | Home resolution | User pointer → first matching rule by ordinal → tenant-wide rule → platform default; evaluated live, server-side | Firm |
| D16 | Mobile | One column by `Ordinal`; no per-breakpoint layouts in v1 | Leaning |
| D17 | Shell research | Revise D11: the Launcher's browse content becomes the **Menu widget**; the start-page override is dropped from v1 | Leaning |
| D18 | Measurement | Source of each home render (Personal / Rule / Platform), customize-mode entries, resets, widget usage | Firm |

### 2.1 One engine — D1

**The home page is a dashboard.** Concretely: the tenant home route renders `tm-dashboard` with
whatever dashboard D15 resolves, plus a "Customize" affordance. There is no `HomePage` entity, no
home-specific layout format, and no home-specific editor.

Three things distinguish the *experience* of home from a report dashboard, and all three are
policies of the renderer or entries in the widget catalogue:

1. **Viewer-relative widgets.** My assignments, Assigned by me, Recents, Inbox and the Menu show the
   *viewer's* data and are permission-filtered per viewer. They are ordinary widget types (D3); an
   "Accounting team" dashboard may carry a My assignments widget and each member sees their own.
2. **No parameter bar unless declared** (D13). A home page is a cockpit, not a query.
3. **A built-in fallback** (D10). A tenant that has configured nothing still gets a useful page.

Why not two concepts: every product with both (§1.2, C1) ended up with one grid engine, and the
cost of a second one is not the grid — it is the second editor, the second permission model, the
second storage shape, and the eternal "why can't I put this dashboard widget on my home page"
request. Reusing the dashboard also means the report studio's output lands on home for free the day
it exists.

### 2.2 Storage — D2

**Rows, with one JSON column per widget.** Sketch, in the specs' contract notation (names are
illustrative; a spec settles them):

```csharp
// Tellma.Core.Abstractions.Dashboards
[Table("Dashboards", Schema = "core"), TableType, Temporal, ApiResource]
public class Dashboard : TopLevelEntity, IActivatable
{
    [Multilingual, Searchable] public string Name { get; set; }
    public string? Name2 { get; set; }
    public string? Name3 { get; set; }
    [NaturalKey] public string? Code { get; set; }               // shared dashboards only: seeds and import
    public string? Description { get; set; }
    public int? OwnerId { get; set; }                            // FK -> core.Users; null = shared
    [ServerOwned] public int? BasedOnId { get; set; }            // FK -> core.Dashboards; the shared dashboard a private copy forked from
    [ServerOwned] public DateTimeOffset? ForkedAt { get; set; }  // when the copy was taken; compared with the base's ModifiedAt
    public bool IsPublic { get; set; }                           // visible to every active member; else via Roles or ownership
    public bool AllowPersonalization { get; set; } = true;       // false = users who land on it as home cannot fork it
    public int Columns { get; set; } = 12;
    public int? AutoRefreshSeconds { get; set; }                 // null = off; validated against a floor
    [ServerOwned] public bool IsActive { get; set; } = true;
    [NotMapped] public IReadOnlyList<DashboardWidget> Widgets { get; set; }
    [NotMapped] public IReadOnlyList<DashboardParameter> Parameters { get; set; }
    [NotMapped] public IReadOnlyList<DashboardRole> Roles { get; set; }
}

[Table("DashboardWidgets", Schema = "core"), TableType, Temporal]
public class DashboardWidget : ChildEntity
{
    [ParentKey] public int DashboardId { get; set; }
    public string Type { get; set; }                             // registry key: core.report, core.myAssignments, core.text, ...
    public int Ordinal { get; set; }                             // reading order = the single-column order; recomputed from (Y, X) on save
    public int X { get; set; }                                   // logical start column, 0-based
    public int Y { get; set; }
    public int W { get; set; }                                   // 1..Columns
    public int H { get; set; }                                   // row units
    [Multilingual] public string? Title { get; set; }            // overrides the type's or the report's title
    public string? Title2 { get; set; }
    public string? Title3 { get; set; }
    public bool IsRequired { get; set; }                         // cannot be removed from a fork (D7)
    public int? AutoRefreshSeconds { get; set; }                 // overrides the dashboard's
    public int? ReportDefinitionId { get; set; }                 // FK -> the report studio's definition; the core.report widget's subject
    [JsonColumn] public string? Config { get; set; }             // type-specific; validated against the type's JsonTypeInfo
}

[Table("DashboardRoles", Schema = "core"), TableType, Temporal]
public class DashboardRole : ChildEntity
{
    [ParentKey] public int DashboardId { get; set; }
    public int RoleId { get; set; }                              // FK -> core.Roles; visibility only, never home assignment
}

[Table("DashboardParameters", Schema = "core"), TableType, Temporal]
public class DashboardParameter : ChildEntity
{
    [ParentKey] public int DashboardId { get; set; }
    public string Key { get; set; }                              // bound by widgets through Config
    [Multilingual] public string Label { get; set; }
    public string? Label2 { get; set; }
    public string? Label3 { get; set; }
    public string Type { get; set; }                             // the report studio's parameter type vocabulary
    [JsonColumn] public string? DefaultJson { get; set; }
    public int Ordinal { get; set; }
}
```

**Why rows and not a layout blob.** The platform's conventions decide most of this before taste
enters:

- **Cross-table references are FK-enforced** (ARCHITECTURE, Data Layer). A blob cannot hold a
  foreign key. With rows, a report definition cannot be deleted while a widget shows it — the delete
  fails with a message that names the dashboards, which is a query the relational model answers.
  Superset stores layouts as `position_json` and has a dedicated fix for dashboards that "500 on
  inconsistent position_json"; Grafana stores JSON and is midway through a v1→v2 schema migration.
- **Per-widget authorization is server-side.** A `core.report` widget renders only if the viewer may
  read that report definition (§2.10). The server can apply that check per row; against a blob it
  would have to parse first.
- **Temporal history for free.** Who changed the accounting home page, and when, is the question the
  first support ticket asks. `[Temporal]` answers it; a blob column's history is a diff nobody reads.
- **The CRUD stack's child synchronization** (null = untouched, `[]` = delete all, list =
  synchronise — the `RoleMemberships` pattern) gives the designer's save for nothing. So do
  Queryex reachability ("which dashboards use report X"), the Excel codec, and the UDTT bulk path.
- **Size.** `core.UserPreferences` caps a value at `MaxPreferenceValueBytes` (32 KB by default); a
  dashboard with a dozen configured widgets is near that line and a shared one authored in Excel is
  over it. Rows have no such cliff.

**Why one JSON column anyway.** A KPI tile wants a threshold and a colour rule; a text widget wants
markdown in three languages; a links widget wants a list of routes with icons; a report widget wants
parameter bindings. These shapes vary by type and evolve independently, and the server never
interprets them — it validates them against the type's declared `JsonTypeInfo` (D3) and passes them
through. That is exactly the spec-0012 `SettingKey<T>` pattern: typed at the edge, opaque in the
middle. Two rules keep the column honest: **no entity reference lives in `Config`** (it goes in a
typed column, with an FK), and **no layout lives in `Config`**.

**Multilingual body text** (the Text widget) is the one accepted deviation: it lives in `Config`
under the tenant's three language slots rather than as typed `Body/Body2/Body3` columns, because
those columns would be nullable dead weight on every other widget row. Rendering sanitizes; raw HTML
is never accepted.

### 2.3 The widget type registry — D3

Widget types are declared by the platform and by packs, the way notification types (spec 0020) and
setting keys (spec 0012) are:

```csharp
// Tellma.Core.Abstractions.Dashboards
public sealed record WidgetTypeDescriptor(
    string Key,                         // ^[a-z][a-z0-9]*(\.[a-z][a-zA-Z0-9]*)+$; core.* reserved for the platform, packs use their slug
    string LabelKey,                    // resolved by ILabelProvider, like setting labels
    Type? ConfigType,                   // null = no settings; else validated through the declaring package's JsonTypeInfo
    (int W, int H) DefaultSize,
    (int W, int H) MinSize,
    string? Securable,                  // null = any member may add it; else the securable × Read the viewer needs
    WidgetSubject Subject);             // None | ReportDefinition | Image — which typed column the widget populates

public interface IWidgetTypeRegistry
{
    IReadOnlyList<WidgetTypeDescriptor> Types { get; }
    WidgetTypeDescriptor? Find(string key);
}
```

Registered through `FeatureContribution.WidgetTypes(typeof(Holder))` at composition; the startup
gate checks the key grammar and uniqueness. A dashboard row whose `Type` is not registered in this
composition (a pack was removed) renders as a placeholder, never as an error page, and the designer
offers Remove.

The client side mirrors it: an Angular component per type, resolved through an injection token
keyed by `Type`, so a pack ships its widget's component with its widget's descriptor.

### 2.4 Where the pieces live — D5

Nothing about layouts goes on `Users` or `Roles`. Two small additions and one new table carry the
whole model:

```csharp
// one column added to spec 0013's User (§3.1), a typed preference like PreferredLanguage:
[SelfEditable] public int? HomeDashboardId { get; set; }        // FK -> core.Dashboards; null = follow the rules

// the ordered rule table — an admin-only entity, its own securable
[Table("HomeAssignments", Schema = "core"), TableType, Temporal, ApiResource]
public class HomeAssignment : TopLevelEntity
{
    public int? RoleId { get; set; }                             // FK -> core.Roles; null = every member (the tenant-wide rule; at most one)
    public int DashboardId { get; set; }                         // FK -> core.Dashboards; must be shared and visible to the role
    public int Ordinal { get; set; }                             // the lowest matching row wins
}
```

**Resolution (D15)**, evaluated on the server for every home render, never cached across a change of
role membership:

1. If `Users.HomeDashboardId` is set and that dashboard is active and visible to the caller → it.
   *Source = Personal.* If it is no longer visible (a role was removed), fall through with a one-time
   toast and clear the pointer.
2. Else the first `HomeAssignments` row by `Ordinal` whose `RoleId` is null or among the caller's
   active roles, and whose dashboard is active → it. *Source = Rule.*
3. Else the platform default (D10). *Source = Platform.*

The result travels with the profile the SPA already fetches, keyed by the caller's `PreferencesTag`
(the pointer), `PermissionsTag` (role memberships) and a tenant-level tag the dashboards feature
bumps (spec 0012's mechanism), so a rule change or a shared-dashboard edit reaches every open client
on its next request.

**Why a typed column and not a `home.dashboardId` key in the preference bag.** Spec 0013 is explicit
that "the server never interprets a value" in the bag and that it is written only by self-service.
The home pointer *must* be read by the server (resolution, bulk reset, FK integrity when a dashboard
is deleted), and the bag would leave it dangling. The same spec keeps `PreferredLanguage` as a typed
column for the same reason — "background work addresses a user in their language without a request
in flight" — and marks it `[SelfEditable]`, so `me/save` writes it and a validator checks the target
is visible to the caller. The bag remains the right home for client-only crumbs: dismissed nudges,
collapsed widget states, the last customize-mode view.

**Why a standalone rule table and not columns on `Roles`.** The ordering is *across* roles — it is a
property of the tenant's home policy, not of any one role — and the admin page that edits it is a
single ordered list with drag-to-order and a preview, exactly ServiceNow's destination rules. Two
columns on `Roles` (`HomeDashboardId`, `HomePriority`) would express the same thing but scatter the
policy across role cards and couple spec 0013's `Role` to the dashboards feature. The rule table also
carries the tenant-wide default (`RoleId = null`) without a special case.

**Why not union the roles' layouts instead of picking one.** A union of 2-D layouts is not a layout:
two role defaults each put their KPI row at `Y = 0`, and the merge has to invent positions, leave
holes, or compact — and then explain to the admin why the accountant-who-is-also-a-buyer sees
something no one designed. Union works for list-shaped content (§1.2, C4) and is available later at
the widget level if a tenant needs it (Part 4, Q3); at the page level, pick-one with an authored
order is what every 2-D product does, and it is what an admin can reason about ("sales users get
*this* page").

**The many-to-many model, in practice.** Most tenants will author two to five role home pages plus
the tenant-wide one. The admin page shows the list in evaluation order, a "Preview as role" that
renders the resolved home for a chosen role (with the admin's own data, and says so), and a count of
users each rule currently reaches — the number that makes the ordinal feel like a decision rather
than a form field.

### 2.5 Pointer, not copy — D6

**Three operations, only one of which copies:**

| Operation | Effect on data | Follows later admin edits? |
|---|---|---|
| Admin assigns a dashboard to a role (rule) | One `HomeAssignments` row | Yes — every user without a personal pointer resolves to it live |
| User picks any visible dashboard as home ("Use as my home") | `Users.HomeDashboardId` = that id | Yes — it is a reference |
| User **edits the content** of a home they do not own | A private copy: `OwnerId = user`, `BasedOnId = source`, `ForkedAt = now`; widgets and parameters copied, roles not; pointer set to the copy | No — until reset |

The first two cover the 95%. The third is the fork the brief worries about, and it is unavoidable in
some form: a user who moves a widget has expressed a preference that a later admin edit must not
silently destroy, and a user whose layout is silently rearranged overnight is the failure mode the
shell research already refused for pins ("an admin edits one role and forty people's toolbars
silently rearrange"). What the field adds — and what makes the fork acceptable — is that the fork
**keeps its lineage** (`BasedOnId`, `ForkedAt`), which is what powers everything in §2.7.

**Why not a delta over the base, Business-Central style.** A personalization delta needs stable
anchors to apply against a changed base. Business Central has them: named page controls and relative
verbs ("move Balance after Phone No."). A grid has coordinates. If the admin inserts a KPI row at the
top and the user's delta says "widget 7 at (0, 2)", the two now overlap, and no merge rule produces
a page anyone designed. Deltas also make the rendered page a function of two rows and a merge
algorithm, which is the one thing a support engineer cannot look at. Copy, keep lineage, reset — the
Acumatica and NetSuite answer — is honest about what it can promise.

### 2.6 Lock levels — D7

Two flags reproduce NetSuite's three modes, which are the most complete statement of the ladder in
the field:

| NetSuite mode | Tellma | Effect for a user whose home resolves to this dashboard |
|---|---|---|
| Locked | `AllowPersonalization = false` | Customize is unavailable; "Use as my home" still works for other dashboards the user may see |
| Add/Move Content | `AllowPersonalization = true`, some widgets `IsRequired` | The fork carries required widgets; the editor lets them be moved and resized, never removed |
| Unlocked | `AllowPersonalization = true`, none required | Full editing of the fork |

`IsRequired` is copied into the fork as data, so a required widget in a fork is a copy like any
other; a later admin change to *that* widget does not propagate. That is a deliberate simplicity —
live-linking required widgets to their base is Q4 in Part 4 — and it is why the bulk reset in D8
exists. The honest framing for the admin UI: "Required widgets cannot be removed by users. Changes
you make later reach users who have not personalized; use Reset to reach the rest."

### 2.7 Reset, nudge, and push — D8

- **Reset to default** (user): deletes the private copy the pointer references (only when
  `BasedOnId` is set — a hand-made private dashboard is never deleted by a reset) and clears the
  pointer. Home resolves by rule again.
- **Updated since you customized** (user): when the resolved source is Personal and
  `BasedOn.ModifiedAt > ForkedAt`, a quiet, dismissible strip on the home page: "The default home
  page for your role changed on 12 Sep. Reset to see it." The dismissal is a bag key. Nothing moves
  by itself.
- **Reset personalized home pages** (admin, bulk): scope = a role, a rule, or selected users; the
  dialog shows the count ("41 of 63 users in Accounting have personalized their home page; all 41
  will be reset") and the action is logged. This is NetSuite's override re-push made explicit, and
  the D6 fork model is what makes the count computable (`BasedOnId = this dashboard`).
- **No silent overwrite, ever.** The admin's edit reaches non-forked users live (pointer), and
  forked users only through the nudge or the explicit bulk reset. Both paths are visible.

### 2.8 The grid — D4, D16

- **12 columns**, a fixed row unit expressed in the size ladder's tokens, `X`/`Y`/`W`/`H` in cell
  units, `Ordinal` recomputed from `(Y, X)` on save. Twelve is the react-grid-layout and Bootstrap
  default and divides into halves, thirds, quarters and sixths, which is every layout an admin
  actually draws; Grafana's 24 buys finer KPI tiles at the cost of a fiddlier editor (Part 4, Q1).
- **Vertical compaction on save**, no floating gaps — Grafana's and react-grid-layout's default, and
  what makes "insert a row at the top" push everything down predictably.
- **Server validation**: `0 ≤ X`, `X + W ≤ Columns`, `W ≥ MinSize.W`, `H ≥ MinSize.H`, no two
  widgets overlap, at most `MaxWidgetsPerDashboard` (40 is generous) rows. The overlap check is
  quadratic in the widget count and irrelevant at 40.
- **`X` is logical.** In an RTL tenant the renderer mirrors through logical properties (shell D18);
  the stored layout is direction-neutral.
- **Below the `md` breakpoint: one column, by `Ordinal`, natural heights.** No per-breakpoint
  layouts in v1 (§1.3). A tablet band may scale `W` by half, rounded up, if Q2 says it is worth it.
  Because `Ordinal` is a stored column and not a derived order, the mobile order is stable across
  saves and reportable.

### 2.9 The widget catalogue — D9, D10

| Key | Shipped by | Viewer-relative | Depends on | Notes |
|---|---|---|---|---|
| `core.menu` | the shell | yes (permission-filtered) | shell spec | The Launcher's browse content — sections, groups, pages, pins starred in place. Replaces shell D11's home mounting |
| `core.recents` | the shell | yes | shell D6 | The dwell-gated, per-tenant recents list |
| `core.inbox` | Core | yes | spec 0020 | Latest notifications with unread state; "View all" → the inbox page |
| `core.text` | Core | no | — | Multilingual markdown; announcements. Sanitized on render |
| `core.links` | Core | no (entries the viewer cannot open are dropped) | — | Tiles of page routes with icon and label — Fiori tiles, Workday apps. Also the natural home of a distribution's "Getting started" |
| `core.report` | Core | data via the report's own permissions | the report studio spec | Table, chart or KPI-tile mode over a report definition; parameter bindings in `Config` |
| `core.myAssignments`, `core.assignedByMe` | the module that owns document assignment | yes | a documents / workflow spec | The 1.0 Inbox and Outbox as widgets; counts by state, newest first, one-click open |
| `core.image` | Core | no | spec 0016's lift of blob kinds to child entities | A widget is a child entity, and spec 0016 defers blob kinds on children to the weak-entity work. Until then: deferred, or an image referenced through a top-level media entity |

Deliberately not in v1: an embedded-URL widget (CSP and clickjacking surface; every tenant will ask;
say no until a concrete need), calendar, tasks.

**The platform default (D10)** is composed in code from whatever the registry holds, so a Core-only
distribution and a full ERP distribution both get a sensible page:

```
┌ My assignments (6×4) ───────────────┐ ┌ Assigned by me (6×4) ──────────────┐
│ 3 awaiting your approval            │ │ 7 with others · 2 overdue          │
│ INV-2026-0431  Acme Ltd   AED 12,400│ │ PO-2026-0092   Beta Co   pending   │
│ …                                   │ │ …                                  │
└─────────────────────────────────────┘ └────────────────────────────────────┘
┌ Recent pages (12×2) ──────────────────────────────────────────────────────┐
│ Journal Entries · Customer Invoices · Trial Balance · Bank Reconciliation │
└───────────────────────────────────────────────────────────────────────────┘
┌ All pages (12×N) ─────────────────────────────────────────────────────────┐
│ FINANCIALS          SALES              PURCHASING        ADMINISTRATION   │
│  General Ledger     ★Customers         Suppliers         Users            │
│ ★Journal Entries     Sales Invoices     Purchase Invoices Roles           │
│  …                   …                  …                 …               │
└───────────────────────────────────────────────────────────────────────────┘
                                                              ⚙ Customize
```

Without a documents module the first row is the Inbox widget alone. Whether this default should
instead be a *seeded shared dashboard row* that admins edit like any other is Q6 in Part 4.

### 2.10 Permissions and visibility — D14

- Securable `core.Dashboard` with the standard actions. **Read** carries the row criterion
  "`OwnerId = me` or `IsPublic` or a `DashboardRoles` row for one of my roles" — the bespoke-criterion
  shape spec 0013 §5 reserves for "documents assigned to me". **Save / Delete on shared dashboards**
  need the grant; a member's own private dashboards are written through a self-service path
  (spec 0017's `me/*` pattern), so a user needs no admin grant to fork or to build a private page.
- **Publish** (private → shared): clears `OwnerId`, requires the Save grant, and is the answer to
  Mackay's finding that customization is carried by local experts — a power user builds the page and
  an admin publishes it, rather than rebuilding it.
- `core.HomeAssignment` is its own securable; the bulk reset is an action under it.
- **Per-widget authorization at render.** A `core.report` widget whose report the viewer may not
  read renders a "Not available to you" placeholder; a widget type with a `Securable` the viewer
  lacks renders the same. The page never breaks, and the editor offers Remove.
- **Impersonation (persona D)** sees the user's resolved home read-only; nothing is customized on a
  user's behalf except through the logged bulk reset.

### 2.11 Edit mode — D11

```
┌ Customize home ────────────────────────────── [Reset to default] [Cancel] [Save] ┐
│ [＋ Add widget ▾]                                                                 │
│ ┌─────────────────────────┐ ┌─────────────────────────┐                          │
│ │ ⠿ My assignments   🔒 ⋮ │ │ ⠿ Assigned by me      ⋮ │   ⋮ = Move ▸ Up / Down /  │
│ │                         │ │                         │       Left / Right / To…  │
│ │                     ◢   │ │                     ◢   │       Resize ▸ presets    │
│ └─────────────────────────┘ └─────────────────────────┘       Title…             │
│ ┌───────────────────────────────────────────────────┐         Remove (disabled   │
│ │ ⠿ Recent pages                                  ⋮ │         when required)     │
│ └───────────────────────────────────────────────────┘                            │
└───────────────────────────────────────────────────────────────────────────────────┘
```

- **Explicit mode, explicit Save.** Entering Customize on a page you stare at all day must be a
  decision, and leaving it must be reversible. Cancel discards; Save persists; the first Save on a
  shared home performs the fork (§2.5) in the same request.
- **Every drag has a non-drag twin** (WCAG 2.2 SC 2.5.7): the per-widget menu moves by one cell or
  "to position…", and resizes to presets. **Keyboard**: focus the handle, arrows move by a cell,
  `Shift`+arrows resize, `Enter` opens the menu. Announce the new position (`"Moved to row 2,
  column 7"`) through a live region.
- **The gallery** lists registry types the viewer may add, and — under a Reports tab — report
  definitions the viewer may read, each dropped at its `DefaultSize` below the last row.
- **Undo** within the session (a small command stack) is cheap and prevents most Cancel-and-redo.
- The admin's designer for shared dashboards is the **same component** with two extra controls:
  `AllowPersonalization` on the page and the lock toggle per widget.

### 2.12 Refresh and parameters — D12, D13

**Refresh.** A home page is parked in a tab all day by hundreds of users; polling it is a server
load multiplier with no user in front of it. Rules:

- Built-in widgets refresh by **hub push** (spec 0020's `ClientEvent` — `inbox.changed` today; an
  `assignments.changed` when documents land). No timers.
- Report widgets poll only when the dashboard or widget sets `AutoRefreshSeconds`, only while the
  document is visible (Page Visibility API), paused after a period of no interaction, and never
  under a **server-validated floor** (30 s in the sketch; a setting). Tellma 1.0's default of five
  minutes was already generous.
- **Off by default on home.** A tenant that wants a live ticker sets it on the shared dashboard.

**Parameters.** Dashboards keep 1.0's idea of top-level parameters — a date range, a business unit —
bound to widget report parameters through `Config` bindings (`{"bindings": {"FromDate": "$p.from"}}`
is the shape; the report studio's parameter vocabulary is the type set). Home renders with defaults
and shows the parameter bar only when the resolved dashboard declares parameters. Nothing prevents
an admin from making a parameterised dashboard the sales team's home; the wireframe just does not
recommend it.

### 2.13 What changes in the shell research — D17

The shell document's D11 (Leaning) rested on one premise: "because the app restores the last page,
home is a rarely-seen surface, and investment in it has poor returns." The premise holds for the
data-entry persona and stays true under this design — D12 is untouched. What it under-weighted is
that the brief now includes three built-in widgets (assignments, assigned-by-me, recents) that have
no other natural surface, and a report studio whose output executives want on the landing page. The
revision:

- **Home renders the resolved dashboard** (D1, D15). The Launcher's browse content survives as the
  `core.menu` widget and is part of the platform default, so a tenant that never touches any of
  this gets the page D11 described, plus assignments and recents above it.
- **The Launcher is mounted in two places, not three** (shell §2.15): the overlay and the palette.
  The home mounting becomes the widget.
- **The start-page override is dropped from v1.** Its stated purpose — executives landing on a
  dashboard — is now the default behaviour of a home rule. If persona A asks to land on a specific
  list page, D12 already restores it; a bag key can reinstate the override later at no structural
  cost (Part 4, Q5).
- **Pins and home defaults should use one mechanism.** Shell D5 seeds pins from an ordered union of
  role default sets; spec 0013 §3.4 instead describes "an administrator-managed default … a tenant
  setting the client merges when the key is absent" — a single tenant-wide default, not per role.
  The ordered rule table of D5 here would serve pins as well (`RoleId?`, ordinal, a pin set). This is
  a misalignment between the shell research and spec 0013 to settle when the shell spec is written;
  it is flagged here, not decided.

### 2.14 Misalignments to resolve

Per the repository's rule that ARCHITECTURE.md is a living document, the following should be
brought in line one way or the other:

| Where | What it says | What this document assumes | Suggested change |
|---|---|---|---|
| ARCHITECTURE.md, *Reports — three tiers* | Tier 3 — "analytics, BI, **dashboards**" — lives in a separate analytical store, "out of scope for the OLTP module's contract" | Dashboards here are a **presentation surface** over tier-1 and tier-2 reports inside the OLTP app; nothing analytical is stored | Reword tier 3 as "analytics / BI (external store)" and add a sentence that dashboards — grids of tier-1/2 report widgets and platform widgets — are a Core presentation feature |
| erp-shell-design.md, D11 and §2.15 | Home = the Launcher's browse content; Launcher mounted in three places; a user-settable start page | D1, D15, D17 above | Revise D11 to point here; two mountings; start page becomes Q5 |
| spec 0013 §3.1 | The `User` column set | One added `[SelfEditable] int? HomeDashboardId` FK | An amendment when the dashboards spec lands |
| spec 0013 §3.4 vs shell D5 | Pins default = one tenant setting vs per-role ordered seeds | One rule mechanism for both | Decide in the shell spec |
| spec 0016 | Blob kinds on child entities deferred | The image widget needs one | Sequence the image widget after that lift, or reference a top-level media entity |

### 2.15 Implementation shape

- **Server**: `DashboardService` (the CRUD stack over `Dashboard` with its three child collections),
  `HomeService` (`home` — the resolved dashboard with `Source` and `BaseUpdated`;
  `home/personalize` — fork and point; `home/reset`), `HomeAssignmentService` (the rule table, the
  preview, the bulk reset), `IWidgetTypeRegistry`, a validator for grid invariants and for `Config`
  against the type's `JsonTypeInfo`, a validator that `HomeDashboardId` is visible to the caller.
- **Client**: `tm-dashboard` (grid renderer, one-column reflow, edit mode, keyboard model),
  `tm-widget-host` (resolves the component by `Type`, applies the placeholder policy, owns refresh),
  a widget gallery, the home route wrapper (Customize affordance, the nudge strip), the admin pages
  (dashboard designer = `tm-dashboard` in admin mode; home rules with preview and counts).
- **Seeds**: a distribution ships shared dashboards by `Code` and home rules by role `Code` through
  the migrator's seeding (spec 0011), so "Accounting home" exists on day one of a new tenant.
- **Telemetry (D18)**: `tellma.home.renders{source}`, customize-mode entries and saves, forks and
  resets (user and bulk), widget renders by type, report-widget query cost. These settle Q1–Q4.

---

## Part 3 — Questions the brief did not ask

1. **What does home cost on every boot?** For most users home is the first paint after sign-in. The
   resolved dashboard must ride with the profile fetch or be one cacheable call; widgets load
   independently with skeletons; built-ins render from cached client state; **no widget may block
   another**, and a report widget's failure is a placeholder, not a page error.
2. **What happens when a widget's subject disappears?** A deleted report definition is prevented by
   the FK until the widgets are detached (the delete error lists the dashboards). Lost permission
   renders the placeholder of §2.10. A removed pack leaves an unregistered `Type`, rendered as a
   placeholder with Remove.
3. **Who can see whose private dashboards?** The owner; and administrators with the unfiltered Read
   grant, for support. The bulk reset enumerates forks by `BasedOnId` and never needs to read their
   content.
4. **Multi-tenant.** Dashboards live in the tenant database; the pointer is on the tenant's `Users`
   row; nothing crosses tenants — consistent with shell D12's per-tenant memory.
5. **Roles referenced by rules.** `HomeAssignments.RoleId` is `NO ACTION`, like every FK in the
   platform; the role delete recipe removes its rules first, or the delete explains why it cannot.
6. **Seeds versus registry.** A seeded default may reference a widget type only a pack registers;
   seeds are per distribution, so the distribution knows its packs. A seed that names an
   unregistered type fails the migrator's validation, not the user's page.
7. **Print.** Print the grid in one column by `Ordinal`, chrome hidden. Cheap; universally forgotten.
8. **Audit.** Temporal tables answer "who changed the shared page"; `ForkedAt` versus the base's
   `ModifiedAt` answers "is this user behind"; the bulk reset is logged with its count.

---

## Part 4 — Open questions

| # | Question | How to settle it |
|---|---|---|
| Q1 | **12 or 24 columns, and what row unit?** Finer KPI tiles versus a fiddlier editor. | Prototype the three commonest layouts at 1366×768 and at the compact density; pick the coarsest grid that draws all three. |
| Q2 | **Per-breakpoint layouts** (Xero) or a tablet scaling band? | Only if telemetry shows tablet use of home above a few percent. The one-column reflow needs no data to ship. |
| Q3 | **Widget-level role targeting** (Viva / Fiori style) as a refinement of pick-one? | Watch how many home rules tenants author and whether admins duplicate dashboards to vary one widget. If they do, add `DashboardWidgetRoles` and a compaction rule for hidden widgets. |
| Q4 | **Live-link required widgets** to their base instead of copying them? | Costs a join at render and a two-source page; wins admin control without resets. Decide from bulk-reset counts after a few tenants. |
| Q5 | **Reinstate the start-page override** (any page, not only a dashboard)? | A bag key if persona A asks. D12 restore covers the reported cases. |
| Q6 | **Materialize the platform default as a seeded shared dashboard row** (`Code = home-default`) rather than composing it in code? | Leaning toward the seeded row — admins then edit it like any other and the registry check happens at seed time. Confirm once the seeding pattern of spec 0011 is applied to a Core entity. |
| Q7 | **Parameterised home pages** — allowed and encouraged, or allowed and discouraged? | Decide with the report studio: it depends on whether parameters can carry sensible per-user defaults. |
| Q8 | **Where private dashboards surface in navigation** — a "My dashboards" band in the Launcher, a page, both? | A shell question; the data model does not care. |
| Q9 | **Is one fork per user enough?** The model allows one *home* fork but any number of private dashboards; a user could keep several homes by switching the pointer. | Ship one; watch whether users ask for "home profiles". |

---

## Appendix — Sources

**Verified directly against shipping source**

- Grafana — `pkg/services/preference/prefimpl/pref.go` (`GetWithDefaults`: last non-empty row wins)
  and `xorm_store.go` (`List`: `ORDER BY user_id ASC, team_id ASC`), `grafana/grafana`, branch
  `main`.
- Frappe — `frappe/website/utils.py` (`get_home_page`, `get_home_page_via_hooks`),
  `frappe/frappe`, branch `develop`.
- Tellma 1.0 — `Tellma.Database.Application`: `dbo.DashboardDefinitions.sql`,
  `dbo.DashboardDefinitionWidgets.sql`, `dbo.DashboardDefinitionRole.sql`, `map.Inbox.sql`,
  `map.Outbox.sql` (local checkout).

**Vendor documentation and design systems**

- Grafana — [Manage user preferences](https://grafana.com/docs/grafana/latest/administration/user-management/user-preferences/), [Organization preferences](https://grafana.com/docs/grafana/latest/administration/organization-preferences/), [issue #51610](https://github.com/grafana/grafana/issues/51610), [Dashboard JSON model](https://grafana.com/docs/grafana/latest/visualizations/dashboards/build-dashboards/view-dashboard-json-model/)
- NetSuite — [Assigning published dashboards to users](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N634109.html), [Applying changes to published dashboards](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N634404.html), [Dashboard personalization (modes)](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N578457.html), [Sikich — how to publish a NetSuite dashboard](https://www.sikich.com/insight/how-to-publish-netsuite-dashboard/)
- Business Central — [Customizing pages for roles](https://learn.microsoft.com/en-us/dynamics365/business-central/ui-personalization-manage), [Manage users and roles (profiles)](https://learn.microsoft.com/en-us/dynamics365/business-central/admin-users-profiles-roles), [Designing profiles](https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/developer/devenv-design-profiles), [Role customization layers](https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/developer/devenv-role-customization)
- Dynamics 365 Finance & Operations — [Personalize the user experience](https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/get-started/personalize-user-experience), [Saved views — general availability](https://learn.microsoft.com/en-us/previous-versions/dynamics365-release-plan/2020wave2/finance-operations/finance-operations-crossapp-capabilities/saved-views--general-availability)
- Salesforce — [Assign custom home pages to specific apps and profiles](https://help.salesforce.com/s/articleView?id=xcloud.admin_home_lex_app_assign.htm&language=en_US&type=5), [Trailblazer community: allow users to customize their Home](https://trailhead.salesforce.com/trailblazer-community/feed/0D54S00000A8OQXSA3)
- ServiceNow — [Dashboards as default home page in Next Experience](https://www.servicenow.com/community/next-experience-forum/can-we-set-dashboards-as-home-page-by-default-in-the-next/m-p/2864907), [Default landing page based on role](https://www.servicenow.com/community/developer-forum/how-to-set-default-landing-page-based-on-role/m-p/2649138), [Journey from homepage to dashboard](https://www.servicenow.com/community/next-experience-articles/journey-from-homepage-to-dashboard-with-creating-default-landing/ta-p/2331941)
- SAP — [My Home in SAP S/4HANA Cloud Public Edition](https://help.sap.com/docs/r/4fc8d03390c342da8a60f8ee387bca1a/2508.500/en-US/8a60279e8d2041b5ad8d3455fab0f3ef.html), [Insights cards on My Home](https://community.sap.com/t5/enterprise-resource-planning-blog-posts-by-sap/insights-cards-on-s-4hana-cloud-my-home/ba-p/13880888), [SAP UX Q3/2025 update](https://community.sap.com/t5/technology-blog-posts-by-sap/sap-ux-q3-2025-update-part-2-sap-s-4hana-cloud-public-edition-2508-and-sap/ba-p/14171291), [Composite roles in launchpad content](https://community.sap.com/t5/enterprise-resource-planning-blog-posts-by-sap/sap-fiori-for-sap-s-4hana-composite-roles-in-launchpad-content-and-layout/ba-p/13680080)
- Workday — [Create configurable cards](https://doc.workday.com/admin-guide/en-us/manage-workday/tenant-configuration/navigation/create-configurable-cards.html)
- Oracle Fusion — [AI-powered homepage for Fusion Apps](https://blogs.oracle.com/fusioninsider/new-ai-powered-homepage-for-fusion-apps), [Apply the Redwood look to the home page](https://docs.oracle.com/en/cloud/saas/sales/fasqa/how-do-i-apply-the-redwood-look-to-my-application.html)
- Odoo — [Dashboards (19.0)](https://www.odoo.com/documentation/19.0/applications/productivity/dashboards.html), [Forum: dashboard as first page (Home Action)](https://www.odoo.com/forum/help-1/the-first-page-that-appears-to-be-the-dashboard-odoo-community-258738)
- Xero — [Your new Xero homepage is here](https://blog.xero.com/product-updates/new-xero-homepage/), [Customise your Xero homepage](https://central.xero.com/0/article/Customise-your-Xero-dashboard), [Product ideas: simplify layout](https://productideas.xero.com/forums/966063-new-homepage/suggestions/50098983-homepage-simplify-layout), [Product ideas: cap the number of columns](https://productideas.xero.com/forums/966063-new-homepage/suggestions/50789081-homepage-ability-to-cap-fix-the-number-of-colu)
- Acumatica — [Create user copy of dashboard](https://community.acumatica.com/reports-and-generic-inquires-115/create-user-copy-of-dashboard-32297), [Feedback needed: dashboard personalization](https://community.acumatica.com/reports-and-generic-inquires-115/feedback-needed-dashboard-personalization-29538)
- Tableau — [Set the default start page for all users](https://help.tableau.com/current/server/en-us/maintenance_startpg.htm), [Manage your account settings](https://help.tableau.com/current/pro/desktop/en-us/useracct.htm)
- HubSpot — [Manage user preferences with user groups (presets)](https://knowledge.hubspot.com/user-management/manage-user-preferences-with-user-groups)
- Jira — [Configure the default dashboard](https://support.atlassian.com/jira-cloud-administration/docs/configure-the-default-dashboard/), [Manage shared dashboards](https://support.atlassian.com/jira-cloud-administration/docs/manage-shared-dashboards/), [Dashboards and gadgets in the Data Center schema](https://support.atlassian.com/jira/kb/how-to-list-dashboards-and-gadgets-definitions-in-jira-data-center/)
- Splunk — [Multiple roles with default app set](https://community.splunk.com/t5/Security/Multiple-Splunk-roles-with-default-App-set-how-is-the-default/m-p/344904)
- Notion — [Notion Home: everything you need to know](https://thomasjfrank.com/notion-home-everything-you-need-to-know/)
- Microsoft Viva Connections — [Use audience targeting](https://learn.microsoft.com/en-us/viva/connections/use-audience-targeting-in-viva-connections)
- Metabase — [Introduction to dashboards](https://www.metabase.com/docs/latest/dashboards/introduction); Apache Superset — [fix: don't 500 on inconsistent position_json](https://github.com/apache/superset/pull/43575); react-grid-layout — [README](https://github.com/react-grid-layout/react-grid-layout)

**UX research and standards**

- [Jared Spool — Do users change their settings?](https://archive.uie.com/brainsparks/2011/09/14/do-users-change-their-settings/) (UIE, 2011)
- [Wendy E. Mackay — Triggers and barriers to customizing software](https://www.lri.fr/~mackay/pdffiles/CHI91.Triggers.pdf) (CHI '91)
- [NN/g — Intranet portals: personalization hot, mobile weak, governance essential](https://www.nngroup.com/articles/intranet-portals-personalization/) (Jakob Nielsen, 2011)
- [NN/g — Customization vs. personalization in the user experience](https://www.nngroup.com/articles/customization-personalization/) (Amy Schade, 2016)
- [NN/g — Dashboards: making charts and graphs easier to understand](https://www.nngroup.com/articles/dashboards-preattentive/), [Cards: UI-component definition](https://www.nngroup.com/articles/cards-component/)
- [W3C — Understanding SC 2.5.7 Dragging Movements](https://www.w3.org/WAI/WCAG22/Understanding/dragging-movements.html), [F108](https://www.w3.org/WAI/WCAG22/Techniques/failures/F108)

**Secondary sources treated with caution.** Claims marked *(2°)* above rest on community threads,
partner blogs or vendor marketing pages rather than on primary documentation or source; none of the
Firm decisions depends on one of them.

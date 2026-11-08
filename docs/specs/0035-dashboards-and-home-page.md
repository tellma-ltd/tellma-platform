# Spec: Dashboards and Home Page

- **Author:** Ahmad Akra
- **Date:** 20 October 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

A dashboard is a grid of reports that refresh themselves. Users build one in the app from stored
and coded reports. They give it a refresh rate and optional dashboard-level parameters, and share it
the way they share a report. Each tile swaps in new data silently and says when its numbers were
computed. A forced refresh clears every tile and shows its spinner. Present mode shows the
dashboard alone, full screen, for a wall display.

The tenant home page at `/{t}` uses the same grid. It greets the user and shows today's date in
their calendar. Below, a work-first default lists the assignments waiting on the user, the ones they
handed out, and the pages they visited last. Everything renders from stored documents, so a warm
launch makes no request for it.

Other specs that carry parts of this design:

- spec 0010 §D.4, §D.8 — instant formats; the `weekday` date style.
- spec 0011 §2.1, §5.5 — `FeatureContribution.WidgetType` and `WidgetTypeContributionItem`; the
  session cookie's renewal.
- spec 0012 §2.2 — `[Defaulted]`.
- spec 0013 §10.1 — the `Dashboard.*` codes, `Notification_core_dashboard_shared`,
  `Core_Dashboard_Synonyms`, `Core_Dashboard_DefaultTitle`.
- spec 0014 §2, §5.3 — the resource `core.Dashboard`; dashboards among the sharing criteria.
- spec 0015 §2.3, §5.2, §5.3 — the actions' `Long` and `Idempotent` flags; named filters; the
  `permissions` extra and computed extras, the form of the `view` extra.
- spec 0016 §2.4, §4.3, §6.1, §8.2, §8.3 — the dashboards routes; `Tellma-Activity` and
  `Tellma-Priority`; `tellma-reports` and its lanes, and `tellma-long` on `dashboards/audience`.
- spec 0018 §7 — the three dashboard tables in Acme's initial migration.
- spec 0021 §2.3 — the `core.dashboard.shared` catalogue row.
- spec 0023 §7.1, §7.2 — the card's `[tmCardSubtitle]` slot; the page header's
  `[tmPageHeaderBand]`.
- spec 0024 §2.2, §3.1, §3.2, §4.4, §10.1, Appendix A — `widgets` and `home` on a feature,
  `widgetTypes` and its contributor, the `home` and `dashboard` skeletons, `TmReadOptions.priority`,
  the drift codes.
- spec 0025 §10, §13, Appendix A — page state, recents, the `dashboard.<id>.params` preference.
- spec 0026 §9.3 — the `home` and `dashboard` rows of the page-context table.
- spec 0027 §3.2, §4.3, §5.1 — the Dashboards entry under Analysis; the Escape shortcut;
  `TmShell.setImmersive`.
- spec 0028 §5.2, §7.3, §7.4 — `TmAttention.refresh`, `tm-assignment-list`, the home lists.
- spec 0029 §3 — `details.header: 'compact'` and tabs with `named`.
- spec 0031 §3.7, §4.1, §5.4 — the `page` hatch with its own context kind and `refresh: 'page'`;
  `can` from the `permissions` extra; the compact header and its action bar.
- spec 0033 §4.6, §5, §6, §8.2, §11 — parameter displays; the sharing model, the `shares` extra
  and `shared-with-me`; the audience; `IDashboardReadThrough` and read-through runs; coded reports
  and `ICodedReportRegistry`.
- spec 0034 §3, §4.1, §4.4, §8.2, §10.4 — the report host's `widget` mode, the parameter field and
  codec, the drill's return, the share dialog.

## Goals / Non-goals

**Goals**

- **No code for the common case.** Stored dashboards need none. A new widget type is one
  declaration on each side, and one hatch replaces the home page (§2).
- **Stable layout.** Tile heights never depend on data. Below `md` the grid becomes one column
  through CSS alone, with no resize script (§8).
- **Quiet refresh.** Scheduled refreshes are silent, run only while the page is visible and, outside
  present mode, keep no session alive. A forced refresh clears every tile (§11).
- **Reports first.** Tile runs yield to a report page's runs on the server (§11.2).
- **Parameters by key.** Dashboard parameters reach reports by key, with no configuration (§10).
- **Instant home.** A warm launch renders the home page with no request (§15).

**Non-goals (explicitly out of scope)**

- **Owned by other specs.** Stored and coded reports, runs, sharing and the audience (spec 0033).
  Renderers, the parameter field and the share dialog (spec 0034). Assignment data and rows (spec
  0028). Recording recents (spec 0025). The launcher entry and pins (spec 0027).
- **Deferred.** Per-role and per-user home pages (§15.5 names the seam); seeded dashboards (§7.3);
  a per-device sign-in for wall displays (§12.4); scaling a tall dashboard to fit a screen.
- **More widgets and features.** Cross-widget filtering; KPI, text, link and image widgets;
  scheduled delivery; favourites. They come later, added through the widget registry (§4).
- **Assistant-only page actions.** They wait for the assistant spec, as spec 0034's do.

## 1. Placement and vocabulary

The server half lives in `Tellma.Core.Abstractions` and `Tellma.Core`. The client half lives in the
`@tellma/core` primary entry point and the lazy `@tellma/core/dashboards` entry point. The spec adds
no dependency between projects or packages.

Contract blocks are TypeScript and C# sketches: names and shapes are normative; imports,
documentation comments, bodies and cancellation tokens are omitted.

### 1.1 Packages and entry points

| Part | Holds | Loaded | Budget (gzip) |
|---|---|---|---|
| `@tellma/core` primary | the widget contracts; the `core.Dashboard` declaration and the four Core widget types (§12.1) | eager | 0.6 KB |
| `@tellma/core/dashboards`, main module | the home page, the grid and tile, and the layout and binding functions | lazy route | 9 KB |
| page chunk | `TmDashboardPage`, the parameter bar and present mode | through `tmDashboardPage()` (§12.1) | 15 KB |
| report widget chunk | the `core.report` body (§9.1) | through that type's `component` | 2 KB |
| editor chunk | `TmDashboardEditor`, the gallery, the widget and dashboard settings dialogs | on Edit, prefetched on its hover and focus | 16 KB |
| `@tellma/core-testing` | `TmDashboardGridHarness`, `TmDashboardPageHarness`, `TmHomePageHarness`, `tmFakeDashboard()` | — | — |

- **Primary exports.** `tmWidget`, `TmWidgetDefinition`, `TmWidgetTypeDeclaration`, `TmWidgetSize`,
  `TmDashboardWidgetModel`, `TmTextSlots`, `TmDashboardParameter` and the type `TmDashboardView`.
- **Main module exports.** `TmHomeHost`, `TmHomePage`, `TmHomeGreeting`, `TM_HOME_WIDGETS`,
  `TmDashboardGrid`, `TmDashboardTile`, `injectTmWidget`, `TmWidgetContext`, `tmPackDashboard`,
  `tmDashboardModel`, `TmTileState`, `TmPlacement`, `tmBindWidgetArguments` and `tmDashboardPage`.
- **Imports.** The main module imports the primary and `/attention` (`TmAssignmentList`,
  `TmAttention`). The page chunk imports `/details`. The page and report widget chunks reach
  `/reports` only through `import()`, which ng-packagr emits as its own file, and the share and
  transfer dialogs load from `/sharing` through the primary's `tmShareDialog` and
  `tmTransferDialog` (spec 0034 §1). The home route therefore never loads `/details`, `/entity`,
  `/reports`, `/sharing` or `core-ui/chart`.
- **Folders.** `client/projects/core/tellma-core/src/dashboards/` (primary members) and
  `client/projects/core/tellma-core/dashboards/`. Telemetry areas are `tm.home` and `tm.dashboard`.
- **Server.** Contracts live in `Tellma.Core.Abstractions.Dashboards`. The runtime lives in
  `Tellma.Core/Dashboards/`: `DashboardService`, `DashboardValidator`, `DashboardViewContributor`,
  `DashboardReadThrough`, `WidgetTypeRegistry` and `WidgetTypeClientAssetContributor`. Tests live
  under `Dashboards/` in `Tellma.Core.Tests` and `Tellma.Core.IntegrationTests`.
- **Composition.** `CoreFeature` composes `Entity<Dashboard, DashboardService>()`,
  `Shareable<Dashboard, DashboardShare>("core.dashboard.shared")` (spec 0033 §5.1), the view
  contributor, the widget types and `DashboardOptions`.

### 1.2 Vocabulary

- *Widget* — one `core.DashboardWidgets` row, or one code-defined `TmDashboardWidgetModel`.
- *Widget type* — a widget's registered key (`core.report`, `acme.cash-position`). *Report widget* —
  type `core.report`. *Built-in widget* — one of the other three Core types (§9.2).
- *Tile* — a widget's rendered frame: header, status line, menu and body. *Cell* — one column by one
  row of the grid.
- *Wide* and *narrow layout*, *reading order* — §8.2.
- *Content slot* — slot 1, 2 or 3 of a multilingual value: `Title`, `Title2`, `Title3` (spec 0013
  §7.5).
- *Hatch* — a feature-supplied component that replaces a platform page or page body: the `home`
  hatch (§2.3), the details page's `page` hatch (§12).
- *Dashboard parameter* — an input the dashboard declares (§10.1). *Binding* — how a report
  parameter gets its value (§10.2). A dashboard parameter *feeds* a tile when a binding sends its
  value to that tile's report.
- *Silent run* — a scheduled refresh that swaps data in place (§11.2). *Forced refresh* — the
  dashboard's Refresh command, which clears every tile first (§11.3).
- *Held result* — what a tile shows while a newer run is pending or has failed (spec 0034 §3.4).
- *Denied tile* — a tile whose data the viewer lacks the permission to read (§6.2).
- *Present mode* — the dashboard page at `?present`: the dashboard alone, for a wall display
  (§12.4).

## 2. What a distribution writes

Stored dashboards need no code: the core feature declares `core.Dashboard` and the four Core widget
types (§12.1). A new widget type is one declaration on the server and one on the client. A
different home page is one hatch.

### 2.1 The common case: nothing

Acme writes no dashboard code; its e2e suite creates the dashboard it tests (§18).

### 2.2 A widget type of its own

**Example —** a cash-position tile. Acme ships none; the harness registers a fixture type this way.

```csharp
// composition (server): the key and the settings the server validates (spec 0011 §2.1)
contribution.WidgetType<CashPositionSettings>("acme.cash-position");
public sealed record CashPositionSettings(int AccountId, bool ShowTrend);
```

```ts
// app/widgets/cash-position.widget.ts (client), listed in acmeFeature.widgets
export const cashPosition = tmWidget('acme.cash-position', {
  labelKey: 'Acme_Widget_CashPosition', icon: 'wallet', status: true,
  component: () => import('./cash-position'),                 // fetches through TmApi; reads injectTmWidget() (§9.3)
  settingsEditor: () => import('./cash-position-settings'),   // edit mode only
  defaultSize: { w: 4, h: 4 }, minSize: { w: 3, h: 3 },
  requires: ['acme.CashPosition', 'Read'],
});
```

`tellma-client build` fails with `widget-missing` when the server registers a key that no client
declaration has, and with `widget-not-registered` for the reverse (spec 0024 Appendix A).

### 2.3 A different home page

```ts
// acme.feature.ts
export const acmeFeature = tmFeature({ key: 'acme', home: () => import('./home/acme-home') /* , … */ });

// home/acme-home.ts: the greeting, the default widgets, and the credit exposure report below them
@Component({
  selector: 'acme-home',
  imports: [TmHomeGreeting, TmDashboardGrid],
  template: `<tm-home-greeting /><tm-dashboard-grid [widgets]="widgets" label="Your work" />`,
})
export default class AcmeHome {
  protected readonly widgets: readonly TmDashboardWidgetModel[] = [...TM_HOME_WIDGETS,
    { key: 'credit-exposure', type: 'core.report', report: { key: 'acme-credit-exposure' },
      x: 0, y: 18, w: 12, h: 9 }];
}
```

- **The hatch replaces the body only.** The route, skeleton, page context and telemetry stay with
  `TmHomeHost` (§15.1).
- **Several hatches.** The last feature that sets `home` wins (spec 0024 §2.3).
- **No read-through.** A code-defined grid has no stored dashboard. Its report widgets therefore
  run under the viewer's own access, and embed coded reports by key.
- **Denied tiles hide.** One layout serves every role, so the grid's `denied` input keeps its
  default `'hide'` (§6.2): a cashier's home never shows a locked "Credit exposure" tile.
- **Acme** keeps the default home; the harness covers the hatch.

## 3. The dashboard tables

`core.Dashboards` holds one row per dashboard, `core.DashboardWidgets` its tiles and
`core.DashboardShares` its grants, beside it. The stack's API segment is `dashboards` and its
resource is `core.Dashboard`. Ownership and sharing follow spec 0033 §5, applied by `Shareable<,>()`.

### 3.1 Classes and columns

```csharp
// Tellma.Core.Abstractions.Dashboards
[Table("Dashboards", Schema = "core"), TableType, Temporal]
[ApiResource(Segment = "dashboards"), Stack(Operations = Query | Details | Save | Delete)]
[DisplayNameSource(nameof(Title))]
public class Dashboard : TopLevelEntity, IActivatable, IShareable
{
    [NaturalKey] public string? Code { get; set; }                        // optional; ≤ 50
    [Multilingual, Searchable, Defaulted] public string Title { get; set; } // + Title2, Title3; ≤ 255; empty: the default title
    public int? RefreshMinutes { get; set; }                              // null: no automatic refresh (§11.1)
    [JsonColumn] public string? ParametersJson { get; set; }              // DashboardParameter[] (§10.1); ≤ 32 KB
    [Derived] public int? OwnerId { get; set; }                           // IShareable (spec 0033 §5.1)
    [WriteOnce] public int? BasedOnId { get; set; }                       // IShareable; the dashboard a "Save as" copied
    [ServerOwned] public bool IsActive { get; set; } = true;
    [NotMapped, MaxChildren(24)] public IReadOnlyList<DashboardWidget> Widgets { get; set; }
}

[Table("DashboardWidgets", Schema = "core"), TableType]                    // temporal with its parent
public class DashboardWidget : ChildEntity
{
    [ParentKey] public int DashboardId { get; set; }
    public string Type { get; set; }                                      // a registered widget type (§4)
    public int? ReportId { get; set; }                                    // core.report: a stored report, or
    public string? ReportKey { get; set; }                                // a coded report's key ('acme-credit-exposure')
    [Multilingual] public string? Title { get; set; }                     // + Title2, Title3; null: the report's or type's
    [Multilingual] public string? Description { get; set; }               // + Description2, Description3; ≤ 1,024
    public int X { get; set; } public int Y { get; set; }                 // first column (0-based, from inline-start), first row
    public int W { get; set; } public int H { get; set; }                 // columns 1–12, rows 1–24
    public int? RefreshMinutes { get; set; }                              // null: the dashboard's
    [JsonColumn] public string? SettingsJson { get; set; }                // the type's settings (§4.2); ≤ 8 KB
}

[Table("DashboardShares", Schema = "core"), TableType, Temporal]            // no [ApiResource], no [Stack]
public class DashboardShare : Entity<int>, IShareRow                       // spec 0033 §2.1's ReportShare, member for member
{ public int DashboardId { get; set; } /* FK → core.Dashboards; UserId, RoleId, Access, SharedById, SharedAt */ }
```

- **History.** `core.Dashboards` is temporal (`core.DashboardsHistory`), and its widgets are
  temporal with it. `core.DashboardShares` is temporal on its own (`core.DashboardSharesHistory`).
- **Ids.** Each table has its own sequence (`core.sq_Dashboards`, `core.sq_DashboardWidgets`,
  `core.sq_DashboardShares`) and `CK_<Table>_Id CHECK ([Id] > 0)`.
- **Column types** follow the sketch: `int`; `bit` (`IsActive` `DF 1`); `nvarchar` at the stated
  lengths, titles being content slots (spec 0013 §7.5); `nvarchar(max)` for JSON; `varchar(64)` for
  `Type` and `ReportKey`; the audit set and period of spec 0012 §4.2.
- **Default title.** `Title` is `[Defaulted]` (spec 0012 §2.2), so a new dashboard saves without a
  visit to its settings. `PreprocessAsync` fills an empty `Title`, and every other empty configured
  slot, with `Core_Dashboard_DefaultTitle`, "Untitled dashboard", each in that slot's tenant
  language, by spec 0033 §2.1's slot rule. A slot the user typed keeps its text.
- **An inactive dashboard still opens.** Deactivation hides it from the dashboards page, whose
  `query` skips inactive rows unless it sets `IncludeInactive`. Opening it by id, its tiles'
  read-through runs (spec 0033 §8.2), pins and links keep working.

| Constraint or index | Definition |
|---|---|
| `UX_Dashboards_Code` | `(Code) WHERE [Code] IS NOT NULL` |
| `CK_Dashboards_RefreshMinutes`, `CK_DashboardWidgets_RefreshMinutes` | `CHECK ([RefreshMinutes] BETWEEN 1 AND 1440)` |
| `OwnerId` | `FK → core.Users`, with `IX_Dashboards_OwnerId (OwnerId) INCLUDE (IsActive)` |
| `BasedOnId` | no foreign key: lineage only |
| `DashboardWidgets.DashboardId` | `FK → core.Dashboards` with `IX_DashboardWidgets_DashboardId` |
| `FK_DashboardWidgets_ReportId` | `→ core.Reports`, `NO ACTION` (spec 0033 §5.6) |
| `IX_DashboardWidgets_ReportId` | `(ReportId) INCLUDE (DashboardId) WHERE [ReportId] IS NOT NULL` |
| `CK_DashboardWidgets_OneReport` | `CHECK ([ReportId] IS NULL OR [ReportKey] IS NULL)` |
| `CK_DashboardWidgets_Grid` | `CHECK ([X] >= 0 AND [W] BETWEEN 1 AND 12 AND [X] + [W] <= 12 AND [Y] BETWEEN 0 AND 1000 AND [H] BETWEEN 1 AND 24)` |
| `CK_DashboardShares_Target` | `CHECK ([UserId] IS NULL OR [RoleId] IS NULL)`: a user, a role, or neither for everyone |
| `CK_DashboardShares_EveryoneView` | `CHECK ([UserId] IS NOT NULL OR [RoleId] IS NOT NULL OR [Access] = 'View')` |
| `UX_DashboardShares_Everyone` | `(DashboardId) WHERE [UserId] IS NULL AND [RoleId] IS NULL`: at most one Everyone grant |

- **`core.DashboardShares`** copies spec 0033 §2.1's `core.ReportShares`, with `DashboardId` for
  `ReportId`: `FK → core.Dashboards` with `IX_DashboardShares_DashboardId (DashboardId) INCLUDE
  (UserId, RoleId, Access)`, `UX_DashboardShares_User`, `UX_DashboardShares_Role`, the `UserId` and
  `RoleId` foreign keys and indexes, and the three constraints above.
- **Shares live beside the row,** as spec 0033 §2.1's do: no route, no child query surface and no
  client metadata. A save never reads or writes them, and sharing never stamps the dashboard.
- **Not included.** No version tag, since the client stores no dashboard on disk. No `Export`,
  `Import` or `Enlist`.
- **Service.** `DashboardService` derives from `ShareableService<Dashboard, DashboardShare>`, which
  applies spec 0033 §5.1's ownership rules.

### 3.2 Routes and securables

Every route is under `/{t}/api/web/dashboards/`. Retry classes are spec 0016 §2.2's.

| Route | Kind | Securable (`core.Dashboard` ×) | Retried |
|---|---|---|---|
| `query`, `get`, `get-by-ids` | standard; `query` takes the named filter `shared-with-me` (§14); `get` answers the `permissions`, `shares` and `view` extras | `Read` | yes |
| `save`; `delete`, `delete-by-query` | standard | `Save`; `Delete` | never |
| `activate`, `deactivate` | built-in actions | `Activate` | never |
| `share`, `publish`, `transfer` | `[EntityAction]`s over `IShareCommands<Dashboard>` | `Share`, `Publish`, `Transfer` | never |
| `audience` | `[ApiAction]`, `Mutation = false`, `Idempotent = true`, `Long = true`; `tellma-long`, `tellma-reports` (spec 0016 §8.2, §8.3) | `Share` | yes |

- **The four actions** (`share`, `publish`, `transfer`, `audience`) copy the declarations that
  spec 0033 §5.4 and §6.1 put on `ReportService`, over `ActionContext<Dashboard, int>`. Their
  descriptions name dashboards, for example "Shares dashboards with users."
- **Who may do what.** Spec 0033 §5.2's table, read for `core.Dashboard`. The row criteria are
  spec 0033 §5.3's, over `core.DashboardShare` and its `Dashboard` navigation.
- **No widget data here.** Widget data comes from `reports/run` with `DashboardId` (spec 0033
  §8.2), a coded report's route, or a widget type's own calls.

### 3.3 Limits

- **`DashboardOptions`** binds `Tellma:Dashboards` and is validated at startup. Its one member,
  `MinRefreshMinutes` (default 1, range 1–60), is the lowest refresh interval a save accepts.
- **Constants.** 24 widgets; 20 dashboard parameters; `SettingsJson` 8 KB; `ParametersJson`
  32 KB; `RefreshMinutes` ≤ 1,440.
- **24 widgets fit the server's queue.** A dashboard's first runs fit the per-user batch lane of
  `tellma-reports`, 6 running and 24 queued (§11.1), so none meets a `429` from that lane. A queued
  run holds no `tellma-tenant` permit (spec 0016 §8.2), so the tenant's 64 permits bound only the
  runs in flight.

## 4. Widget types

A widget type is a key registered on the server and declared on the client. The server validates
the key and the settings; the client renders the body.

### 4.1 Server registry

```csharp
// Tellma.Core.Abstractions.Dashboards; realised from spec 0011 §2.1's WidgetTypeContributionItem
public interface IWidgetTypeRegistry { IReadOnlyList<WidgetTypeDescriptor> Types { get; } WidgetTypeDescriptor? Find(string key); }
public sealed record WidgetTypeDescriptor(
    string Key,                                   // 'core.report', 'acme.cash-position'
    string Feature,                               // the contributing feature's key
    Type? SettingsType,                           // null: the type has no settings
    WidgetSubject Subject);
public enum WidgetSubject { None, Report }      // Report: exactly one of ReportId and ReportKey
```

- **Key grammar.** `^[a-z][a-z0-9]*\.[a-z][a-z0-9]*(-[a-z0-9]+)*$`. The first segment is the
  contributing feature's key (`core` for the platform). Startup refuses a duplicate key, or a key
  whose first segment is not its contributor's key.
- **Settings.** The server deserializes them strictly with System.Text.Json, so an unmapped member
  is refused. It then validates their data annotations. Only `core.report` has `Subject = Report`.
- **The client emit.** `WidgetTypeClientAssetContributor` owns the `widgetTypes` section of the Core
  metadata chunk (spec 0024 §3.1, §3.2). It writes every registered key there, so the build can
  check drift (§2.2).

### 4.2 Core types

| Key | Subject | Settings | Default / min size | Body |
|---|---|---|---|---|
| `core.report` | a stored or a coded report | `ReportWidgetSettings` | 6×9 / 3×5 | §9.1 |
| `core.assignments-to-me`, `core.assignments-by-me` | — | none | 7×9 / 4×5 | §9.2 |
| `core.recents` | — | none | 5×9 / 3×4 | §9.2 |

```csharp
public sealed record ReportWidgetSettings(IReadOnlyDictionary<string, WidgetBinding>? Bindings); // key: a report parameter key
public sealed record WidgetBinding(string? Parameter, JsonElement? Value, bool? Unbound); // exactly one JSON member present, a null `value` included; Unbound only as true (§5.3)
```

### 4.3 Client declaration

```ts
// @tellma/core (primary)
interface TmWidgetSize { readonly w: number; readonly h: number }     // w: 1–12 columns; h: 1–24 rows
interface TmWidgetDefinition {
  readonly labelKey: string;                      // the gallery entry and the default tile title
  readonly descriptionKey?: string;               // the gallery's second line
  readonly icon: string;                          // a registry name (spec 0023 §3.1)
  readonly component: TmLazyComponent;            // the body; reads injectTmWidget() (§9.3)
  readonly settingsEditor?: TmLazyComponent;      // edit mode; absent: the type has no settings
  readonly defaultSize: TmWidgetSize;             // the size of a newly added tile
  readonly minSize: TmWidgetSize;                 // the smallest size the editor allows
  readonly requires?: TmSecurableRef;             // when TmPermissions.can fails: a denied tile (§6.2), absent from the gallery
  readonly status?: boolean;                      // true: the tile shows the status line (§8.5); default false
}
declare function tmWidget(key: string, definition: TmWidgetDefinition): TmWidgetTypeDeclaration;
interface TmWidgetTypeDeclaration { readonly key: string; readonly [member: string]: unknown } // frozen; what `widgets` merges
```

- **Merging.** `TmFeatureDefinition.widgets` merges declarations by key (spec 0024 §2.3). The
  `tm/lazy-layouts` lint rule requires `component` and `settingsEditor` to be `() => import(…)`.
- **A missing type.** A stored widget can name a type that no declaration knows, for example after
  a pack was removed. It renders "This widget isn't available." (§8.6), never an error page. Edit
  mode offers Remove.

## 5. Validation (save)

`DashboardValidator` runs in the save's first round trip and reports every failure at a path. Spec
0031 §8.4's summary then shows each failure next to the right control (§13.6). Every code is a `422`
listed in Appendix A.

### 5.1 The dashboard

1. **Title and refresh.** An empty `Title` takes the default (§3.1). `RefreshMinutes` is null or
   between `MinRefreshMinutes` and 1,440 (`Dashboard.RefreshOutOfRange`, argument `min`).
2. **Parameter list.** `ParametersJson` parses as a `DashboardParameter[]` whose `Type` is in spec
   0033 §4.1's vocabulary (`Dashboard.ParametersInvalid`). It has at most 20 items
   (`Dashboard.TooManyParameters`).
3. **Parameter keys and entities.** Keys follow spec 0033 §4.1's grammar
   (`Dashboard.ParameterKeyInvalid`) and are unique ignoring case (`Dashboard.ParameterKeyDuplicate`).
   `Entity` sits only on a `Numeric` parameter and names a queryable entity
   (`Dashboard.ParameterEntityInvalid`). These errors carry the argument `index`.
4. **Parameter displays.** `Display` and `Format` follow spec 0033 §4.6, checked by the report
   validator's own rules (`Dashboard.ParameterDisplayInvalid`, arguments `index` and `rule`: one
   of `display`, `enum-source`, `unknown-enum` and `choice`, after spec 0033 §3.4 rule 11).

### 5.2 Each widget

Errors carry the path `Widgets[i]` or one of its members.

1. **Type and subject.** `Type` is registered (`Dashboard.WidgetTypeUnknown`). The subject matches
   the type (`Dashboard.WidgetSubjectInvalid`): `core.report` holds exactly one of `ReportId` and
   `ReportKey`, and other types hold neither.
2. **Report key.** `ReportKey` follows spec 0024 §9.1's coded-report key rule. When this save adds
   or changes it, it also names a report in `ICodedReportRegistry` (spec 0033 §11.2)
   (`Dashboard.ReportKeyInvalid`). A kept key whose report a later deployment removed stays valid
   and renders "This widget isn't available." (§8.6).
3. **Report access.** When this save adds or changes a `ReportId`, the saver must read that report
   directly; when it adds or changes a `ReportKey`, the saver must hold the coded report's securable
   (`Dashboard.ReportInvisible`). A reference kept from the stored dashboard stays valid, even if
   the saver reads it only through this dashboard.
4. **Grid.** `X + W ≤ 12`, `W` 1–12, `H` 1–24, `Y` 0–1,000 (`Dashboard.WidgetOutOfGrid`). No two
   widgets overlap (`Dashboard.WidgetsOverlap`, argument `other`: the other widget's index).
5. **Refresh and settings.** `RefreshMinutes` follows §5.1 rule 1. `SettingsJson` deserializes into
   the type's settings within 8 KB (`Dashboard.SettingsInvalid`).

**Example —** Omar shares a dashboard with Farah at Edit. One tile shows his "Margins" report,
which Farah reads only through this dashboard. Farah can move or resize that tile and save. Adding
a second "Margins" tile, or pointing another tile at it, fails with `Dashboard.ReportInvisible`.

### 5.3 Report widget bindings

A report widget is checked against its report's parameters: a stored report's (spec 0033 §4.1), or
a coded report's from `ICodedReportRegistry` (spec 0033 §11.2), whose `HasDefault` stands for a
default. Errors sit at `Widgets[i].SettingsJson`.

1. **Known keys.** Every binding key names a report parameter, and every `parameter` binding names
   a dashboard parameter (`Dashboard.BindingUnknown`, argument `key` or `parameter`). Each
   binding's JSON object holds exactly one of the members `parameter`, `value` (a JSON `null`
   included) and `unbound: true`, checked on the JSON, since the record reads a null `value` as
   absent (`Dashboard.SettingsInvalid`).
2. **Matching types.** A dashboard parameter that feeds a report parameter, explicitly or by the
   same key (§10.2), has the same `Type`. For an entity it also has the same `Entity`
   (`Dashboard.BindingTypeMismatch`). An `unbound` report parameter is fed by nothing, so a same-key
   dashboard parameter of another type is no mismatch. A fixed value converts to the report
   parameter's type (`Dashboard.BindingValueInvalid`).
3. **A required report parameter with no default** is either bound to a non-null fixed value or
   fed by a required dashboard parameter. Otherwise the save fails with
   `Dashboard.WidgetParameterRequired` (arguments `key`, `label`).

**Example —** an "Overdue invoices" report requires `AsOf` and has no default:

| Dashboard parameters | Widget bindings | Result |
|---|---|---|
| none | none | `Dashboard.WidgetParameterRequired` (`AsOf`) |
| `AsOf` (Date, optional) | none: the same key feeds it | `Dashboard.WidgetParameterRequired`: an unset optional parameter sends nothing |
| `AsOf` (Date, required) | none | valid; the tile waits for a value (§10.3) |
| none | `"AsOf": { "value": "2026-12-31" }` | valid |
| `Period` (Date, required) | `"AsOf": { "parameter": "Period" }` | valid |

- **A report changed later,** a stored one by an edit or a coded one by a deployment, can break a
  saved dashboard. The tile's run then answers `422 Report.ArgumentRequired`, and the report host
  shows its waiting state (§8.6).

## 6. Reading a dashboard

`dashboards/get` returns the row and its widgets, and three extras: `permissions` (spec 0015
§5.3), `shares` (spec 0033 §5.2), which only the share dialog asks for, and the eager `view`. The
`view` extra tells the page which tiles the caller may see and what each report is, before any
widget runs.

### 6.1 The `view` extra

```csharp
// DashboardViewContributor : IDetailsContributor<Dashboard>, extra "view" (eager)
public sealed record DashboardView(IReadOnlyList<DashboardWidgetView> Widgets);
public sealed record DashboardWidgetView(int WidgetId,
    SecurableRef? MissingAccess,                      // stored reports: the pair the caller lacks (§6.2); null for every other tile, which the client decides
    WidgetReportSummary? Report);
public sealed record WidgetReportSummary(                                // stored reports only
    int Id, string Title, string? Title2, string? Title3, string? Description, string? Description2, string? Description3,
    ReportKind Kind, string Root,                     // Root: the entity the report queries
    IReadOnlyList<ReportParameter> Parameters, bool IsActive,
    bool Readable);                                   // the caller reads the report directly: "Open report" shows
```

- **No access filter on summaries.** They load by id without the stored reports' own access filter,
  because reading the dashboard authorized them (spec 0033 §8.2). `Readable` comes from the same
  ids under the caller's `Read` filter.
- **No extra round trip.** The extra is two statements on the details batch, shaped in C# as a
  computed extra (`DetailsPlan.AddComputedExtra`, spec 0015 §5.3). Root decisions come from the
  cached permission set.
- **Coded tiles** get no summary: the client holds their title, kind and parameters from the
  `codedReports` metadata (spec 0033 §11.4).
- **Inactive reports** still run (spec 0033 §2.1).

### 6.2 Denied tiles

A tile whose data the viewer may not read keeps its place on a stored dashboard and names the
permission it needs.

- **Stored reports.** The server sets `MissingAccess` to `(Root, Read)` when the caller's `Read`
  decision on the summary's `Root` is `Denied`. A child root composes its owner's decision (spec
  0014 §5.5), and the pair named is the one the decision lacks. A `Filtered` decision runs: the
  report shows the caller's rows.
- **Coded reports and other types.** The client derives the same pair. A tile is denied when
  `TmPermissions.can(...)` (spec 0029 §7) fails on the coded report's `securable` from the
  `codedReports` metadata, or on the type's `requires`.
- **What a denied tile does.** It shows §8.6's no-access state, in view and edit mode alike. It
  sends no run, shows no status line, Refresh or Export, and joins no scheduler cycle (§11.1).
  Nothing reflows when access changes, and an editor never removes by accident a tile they cannot
  see.
- **Code-defined grids hide them.** `TmDashboardGrid.denied` is `'hide'` by default (§8.1): the
  grid lays out only the tiles that may run, before the first render, and compaction closes the gap
  (§8.2). `TmDashboardPage` passes `'show'`.
- **A re-read applies its `view`.** When a forced refresh (§11.3) or a stale return (§11.4)
  re-reads the dashboard, a tile whose access changed switches state in place. A newly denied tile
  drops its held result, so revoked data never lingers.

### 6.3 `IDashboardReadThrough`

This spec implements spec 0033 §5.7's interface.

- **`ReadThroughAsync(dashboardParameter)`** returns spec 0033 §5.7's tree, rooted at
  `core.Report`, with the caller's `Read` decision on `core.Dashboard` inside its `Via`. The
  decision comes from the cached permission set, so building the tree costs no I/O. The result is
  memoized per request.
  - *`Unrestricted`* drops the `Via`; *`Denied`* returns null. A member's decision is never
    `Denied`, since spec 0033 §5.3's criteria give every member a `Filtered` one.
  - *The parameter.* The dashboard id stays the parameter the caller names (`@d`), never a literal,
    so every user with the same permission shape shares one plan (spec 0014 §5.2).
- **`DashboardsUsing(reportIds)`** seeks `IX_DashboardWidgets_ReportId` without the caller's filter.
  It returns at most 20 `(ReportId, Id, Title)` rows per report, with titles in the request's
  content slot and a null title for a dashboard the caller's `Read` decision does not admit.
  Spec 0033 §5.6 and §6.2 decide what to show of them.

## 7. Sharing, notification, seeds

Dashboards use the platform's sharing model (spec 0033 §5): an owner; user, role and Everyone
grants, the Everyone grant at View only; `publish` and `transfer` as role grants; and spec 0034
§10.4's share dialog. This section adds only what is dashboard-specific.

### 7.1 Audience

- **The query.** `audience` answers `IAudienceResolver.ResolveAsync` (spec 0033 §6.1) with
  `AudienceQuery("core.Dashboard", id, data, null, null)`. `data` lists the distinct data
  securables of the dashboard's report tiles, at most 24, in the order of each one's first tile by
  widget `Id`:
  - a stored tile gives `(Root, Read)`, from its report row;
  - a coded tile gives its securable from `ICodedReportRegistry` (spec 0033 §11.2);
  - a tile whose report no longer exists, and every other type, gives none.
- **Per-tile counts.** `ShareAudience.CannotRead` and each `AudienceGrant.CannotRead` count, per
  securable, the audience users who lack it. The share dialog (`tmShareDialog('dashboards')`,
  §12.1) builds the same list from the `view` extra and the `codedReports` metadata, and names the
  tiles behind each index. Its warnings never block: "2 of 5 viewers can't see Sales by center.",
  and for a coded tile "3 viewers can't see Credit exposure (built-in; access through roles)."
- **Coded tiles stay role-gated.** Read-through never applies to them (spec 0033 §5.7), so sharing
  a dashboard never hands out a coded report: its viewers see the denied tile instead (§6.2).

### 7.2 `core.dashboard.shared`

A newly added user grant notifies the grantee, through `Shareable<,>("core.dashboard.shared")`.
The one request with its `LocalizedText` arguments, the dedup key `'core.Dashboard:<id>'` and the
withdrawal follow spec 0033 §7; the catalogue row is spec 0021 §2.3's.

- **Text.** `Notification_core_dashboard_shared` is "{actorName} shared the dashboard {title} with
  you"; in Arabic "شارك {actorName} معك لوحة المعلومات {title}". It opens `/{t}/dashboards/{id}`.

### 7.3 Seeded (standard) dashboards

v1 seeds no dashboard. Report seeds ship (spec 0033 §13); a dashboard seed waits for a pack that
needs one. Every v1 dashboard has an owner. The stack still applies spec 0033 §5.1's rule for a row
without an owner, so a later seed step needs no change here.

## 8. The grid

`tm-dashboard-grid` lays widgets on a 12-column grid of fixed 30 px rows. From `md` up it keeps the
stored positions; below `md` it stacks the tiles in one column. Tile heights never depend on data.

### 8.1 Model and geometry

```ts
// @tellma/core (primary): one widget shape, stored or code-defined
type TmTextSlots = readonly [string | null, string | null, string | null];   // content slots 1–3
interface TmDashboardWidgetModel {
  readonly key: string;                           // tile identity: 'w42' (row 42), 'n3' (an unsaved row), or a code key
  readonly type: string;                          // a widget type key
  readonly report?: TmReportRef;                  // core.report: { id } or { key } (spec 0034 §3.1)
  readonly title?: TmTextSlots; readonly titleKey?: string;            // stored slots, or a string key in code
  readonly description?: TmTextSlots; readonly descriptionKey?: string;
  readonly x: number; readonly y: number; readonly w: number; readonly h: number; // as the stored columns (§3.1)
  readonly refreshMinutes?: number | null;        // null: the dashboard's interval
  readonly settings?: TmJson;                     // the parsed SettingsJson
}

// @tellma/core/dashboards
@Component({ selector: 'tm-dashboard-grid' })
declare class TmDashboardGrid {
  readonly widgets: InputSignal<readonly TmDashboardWidgetModel[]>;
  readonly label: InputSignal<string>;                          // the region's accessible name
  readonly parameters: InputSignal<TmReportArgs>;               // supplied dashboard arguments (§10.3); default {}
  readonly dashboardParameters: InputSignal<readonly TmDashboardParameter[]>; // §10.1; default []
  readonly dashboardId: InputSignal<number | undefined>;        // read-through; absent on a code-defined grid
  readonly refreshMinutes: InputSignal<number | null>;          // the dashboard's interval; default null
  readonly view: InputSignal<TmDashboardView | undefined>;      // §6.1's extra; absent: the client's checks only
  readonly denied: InputSignal<'show' | 'hide'>;                // denied tiles (§6.2); default 'hide'
  readonly presenting: InputSignal<boolean>;                    // present mode (§12.4); default false
  readonly tiles: Signal<readonly TmTileState[]>;               // reading order, for the page context
  refreshAll(): void;                                           // §11.3
}
interface TmTileState { readonly key: string; readonly title: string; readonly executedAt: string | null; // §8.5
  readonly status: 'idle' | 'loading' | 'ready' | 'waiting' | 'denied' | 'unavailable' | 'error' } // unavailable: §8.6's not available
```

- **Geometry.** A CSS grid with `grid-template-columns: repeat(12, minmax(0, 1fr))`,
  `grid-auto-rows: var(--tm-dashboard-row)` (30 px) and `gap: var(--tm-dashboard-gap)` (10 px). A
  tile `h` rows tall is `30h + 10(h − 1)` px, so 9 rows give 350 px, close to spec 0034's 360 px card.
- **Surface.** The grid sits on `--surface-subtle` under a `--border-subtle` top rule, padded
  14×16 px (11×10 below `sm`).
- **Width cap.** The token `--tm-dashboard-max-inline` (1600 px) caps the tiles' width on large
  monitors. The grid itself has no cap, so a hatch region sizes it freely. The dashboard page
  (§12.3) and the home page (§15.1) wrap their header, band and grid in one column of
  `max-inline-size: calc(var(--tm-dashboard-max-inline) + 2 × the page padding)`, centred with
  `margin-inline: auto`, so the header's actions line up with the last tile column. The surface
  outside the column stays `--surface-subtle`. The grid's container queries (§8.2) still read its
  own width.
- **Stored rows** map to the model through `tmDashboardModel(entity)`: key `w{id}`, the title
  slots, `report` from `reportId` or `reportKey`, and `settings` parsed. Invalid JSON renders the
  dashboard with no parameters, and the affected tiles as "This widget isn't available."

### 8.2 Two layouts

| Part | Rule |
|---|---|
| Wide | the grid's own width is 768 px (`md`) or more: tiles keep their stored `x` and `w`, and rows compact upward, so a hidden tile (§6.2) leaves no hole |
| Narrow | below 768 px: one column, in reading order, each tile at its stored `h`; `@media print` uses it too |
| Container, not viewport | the grid declares `container-type: inline-size` and `container-name: tm-dashboard`, so a grid in a narrow hatch region goes narrow on its own width |
| No script on resize | `tmPackDashboard` computes both arrangements once per widget set, and two `@container` rules apply one or the other |

Each tile carries its placement as `--tm-col`, `--tm-span`, `--tm-row`, `--tm-rows` and
`--tm-row-narrow` (1-based).

```ts
interface TmPlacement { readonly key: string; readonly col: number; readonly span: number; readonly row: number;
  readonly rows: number; readonly rowNarrow: number }   // col to rows: the wide placement; rowNarrow: the narrow first row
declare function tmPackDashboard(widgets: readonly TmDashboardWidgetModel[]): readonly TmPlacement[]; // the tiles laid out in; reading order out
```

The function applies two rules; unit vectors pin the details (§17).

1. **Compaction (wide).** In `(y, x)` order, each tile moves up one row at a time while the row
   above it is free in its own columns.
2. **Reading order.** Tiles group into bands, read top to bottom. A band starts at the topmost
   unread tile and takes every tile that starts above its bottom edge; each tile that joins can
   push that edge down. Within a band, tiles read by `x`, then by `y`. In the narrow layout, each
   tile starts below the previous one.

**Example —** the home default (§15.3), and a `home` hatch's grid where Omar cannot run widget B:

| Grid | Stored `(x, y, w, h)` | Wide `col/span · row/rows` | Reading order | Narrow row |
|---|---|---|---|---|
| home | to-me `(0,0,7,9)`, recents `(7,0,5,9)`, by-me `(0,9,7,9)` | 1/7·1/9, 8/5·1/9, 1/7·10/9 | to-me, recents, by-me | 1, 10, 19 |
| Omar's hatch | A `(0,0,6,9)`, B `(6,0,6,9)` hidden, C `(6,9,6,9)` | A 1/6·1/9, C 7/6·1/9 | A, C | 1, 10 |

### 8.3 Reading order is the DOM order

Tiles render in reading order. Tab, screen readers, the narrow layout and the first runs (§11.1)
therefore share one order (WCAG 1.3.2, 2.4.3), and CSS placement never reorders focus. `x` counts
from the inline-start edge. The wide layout thus mirrors in RTL with no arithmetic, and a stored
layout is direction-neutral.

### 8.4 The tile

`tm-dashboard-tile` is a `section` built on `tm-card`. Its `h2` (13/600, one line with ellipsis)
labels it, and its description describes it. The card's `flush` body fills the tile and scrolls
inside.

| Part | Rule |
|---|---|
| Slots | the status line (§8.5) is `[tmCardSubtitle]` (spec 0023 §7.1); the menu, or a list type's "View all", is `[tmCardActions]`; a list type's count is the card's `count` |
| Header | 33 px, or 51 px with the status line; neither depends on data |
| Description | ellipsized at the start of the status line; the full text is the section's description and the line's tooltip. List types show theirs only in edit mode |

- **Title** — the first that exists: (1) the widget's own title, in the active content slot, else
  slot 1; (2) its `titleKey`; (3) the report's title: the summary's slots, the coded report's
  `titleKey` from metadata, or `result.title`; (4) the type's `labelKey`.
- **Menu** — a `tm-menu` named "Widget options for {title}":
  - view mode: Refresh; Open report (a report widget whose summary is `Readable`, or a coded one);
    Export to Excel (when the host can export). A denied tile has none of these, and in present
    mode no tile shows a menu;
  - edit mode: Edit widget…, Size ▸, Move ▸, Remove (§13).

### 8.5 The status line

Report widgets, and types that declare `status: true`, show "Updated {time}". The time is the
run's `executedAt`, when the server computed the numbers (spec 0033 §9.1), formatted by spec 0034
§3.6's absolute rule.

- **Before the first result,** a skeleton bar of the text's final width holds its place.
- **When the first run fails** (`status: 'error'` with no `executedAt`, §9.3), the skeleton bar
  goes and the line stays empty. The body shows the failure (§8.6).
- **After a silent run fails** over held data, spec 0034 §3.4's stale marker follows the time until
  the next success.
- **Omitted totals.** When the body reports `totalsOmitted` (§9.3), a short note follows the time:
  "Updated 10:42 · Totals not shown".
  - The report widget reports it from its summary result's `table.totalsOmitted` (spec 0034 §5.3).
  - The line's tooltip adds spec 0034 §5.3's footer sentence after the description.
  - The header keeps its height. The description ellipsizes to make room.

### 8.6 Tile states

The header never changes with state. The table lists the tile's own states. Every other state
belongs to the body, shown as `tm-empty-state size="panel"`: the report host's states (spec 0034
§3.3, including its `widget`-mode "This widget needs a value for {label}."), spec 0024 §11's read
failures, and §9.2's list states.

| State | When | Shows |
|---|---|---|
| waiting | a required dashboard parameter that feeds the tile is unset (§10.2) | `sliders-horizontal`, "Fill in {label} above", and "Fill in", which focuses the bar's field; no run |
| not available | an unknown widget type, or a coded key that `codedReports` lacks | `puzzle`, "This widget isn't available."; edit mode adds Remove |
| no access | a denied tile shown (§6.2), in either mode | `lock`, spec 0024 §11's `forbidden` (`no-grant`) text: "You don't have permission for this", then "It needs “Sales invoice lines: Read”. Ask your administrator to grant it." with the pair's `TmPermissions.label`; no run |
| invalid | a save error at `Widgets[i]` (§13.6) | the body as it was, an error outline and a `circle-alert` after the title |

## 9. Widgets

Each widget type renders its body inside the tile. The platform ships one report widget and three
built-in lists. A distribution's own type reads everything it needs from one context,
`injectTmWidget()` (§9.3).

### 9.1 The report widget

```html
<!-- the body of core.report inside the tile (the report widget chunk) -->
<tm-report-host mode="widget" refresh="silent" [report]="model.report" [parameters]="bound().args"
                [declarations]="declarations()" [dashboardId]="dashboardId()" [held]="held()" [stateScope]="scope()"
                [drillable]="!presenting()" />
```

- **Arguments** are §10.2's. Widget mode has no parameter bar, so the host sends exactly them.
- **No report read.** Widget mode never reads `reports/get` (spec 0034 §3.1). The title and
  parameters come from the `view` extra, or from `codedReports` for a coded tile, so a 12-widget
  dashboard costs one `get` and twelve runs. `declarations()` is the `view` summary's
  `Parameters`, or the `codedReports` entry's `parameters` for a coded tile, so the host shows its
  waiting state without a run (spec 0034 §4.3).
- **State scope.** `scope()` is `d{dashboardId}.{key}` on a stored dashboard and `home.{key}` on a
  code-defined grid, so two tiles of one report keep their own grid state (spec 0034 §3.1).
- **Runs.** Each tick calls `host.run({ clear, background })` with the tick's flags (§9.3, §11.2).
  Widget mode sends `Tellma-Priority: low` on every run (spec 0034 §3.1).
- **Drill-through** navigates in view mode (spec 0034 §8), and the list's crumb returns to the
  dashboard (spec 0034 §8.2). Edit mode makes the body `inert`, and present mode turns drill-through
  off (§12.4).
- **Totals are the report's.**
  - *Pivot.* A pivot tile's subtotals and totals cover only the cells it shows (spec 0033 §8.6).
  - *Summary.* A summary tile shows its totals row only when its first page holds every row (spec
    0034 §5.3). Otherwise the status line notes it (§8.5), and the tile shows no notice strip.
- **Menu commands.** Open report goes to `/{t}/reports/{id}` or `/{t}/reports/{key}`, with the
  bound arguments as `p.*` (`tmReportArgsToUrl`, spec 0034 §4.4). Export to Excel calls
  `host.export()` (spec 0034 §9).

### 9.2 Built-in widgets

| Type | Header | Rows (≤ 10) | Empty | Offline, no document yet |
|---|---|---|---|---|
| `core.assignments-to-me` | "My assignments", count `open`, "View all" to `TmUrls.search('core.Assignment')` | `tm-assignment-list variant="home" party="to-me"` over `latest` | `inbox`, "Nothing is waiting on you" | `wifi-off`, "You're offline. Your assignments show when you reconnect." |
| `core.assignments-by-me` | "Assigned by me", count `openByMe`, "View all" with `{ tab: 'by-me' }` | the same over `latestByMe`, `party="by-me"` | `send`, "Nothing you assigned is still open" | as above |
| `core.recents` | "Recently visited" | `TmRecents.pages()` as links: the entry's icon, else by kind (`list`, `chart-column`, `layout-dashboard`, `settings`, `file`) | `history`, "Pages you visit show up here" | — |

- **Rows and keyboard** follow spec 0028 §7.3. Each assignment list is one roving `ul`; recents is
  a `ul` of links. Before the document exists, a list passes `loading`: five skeleton rows.
- **Live, not polled.** The assignment lists follow `TmAttention.assignments()` (spec 0028 §5.2).
  Recents follow `nav.recent` across tabs (spec 0025 §13). Only a forced refresh ticks them (§11.3).
  A row arriving or leaving changes only its own list.
- **Opening a row.** An assignment row opens `TmUrls.target(resource, targetId)`; a recents row
  opens its URL. Hover and focus call `TmUrls.preload`.

### 9.3 A widget type's body

```ts
// @tellma/core/dashboards
interface TmWidgetContext {
  readonly key: string; readonly type: string;
  readonly settings: Signal<TmJson | null>;
  readonly parameters: Signal<TmReportArgs>;          // the dashboard's supplied arguments (§10.3)
  readonly mode: Signal<'view' | 'edit'>;
  readonly size: Signal<TmWidgetSize>;                // the tile's cells
  readonly tick: Signal<{                             // a new value asks the body to refresh (§11)
    readonly generation: number; readonly clear: boolean; readonly background: boolean }>;
  report(state: {                                     // the body's run state, for the status line and the scheduler
    readonly status: 'loading' | 'ready' | 'error';
    readonly executedAt?: string | null; readonly error?: TmApiError | null;
    readonly totalsOmitted?: boolean }): void;        // a summary whose totals row was omitted (§8.5)
}
declare function injectTmWidget(): TmWidgetContext;
```

- **Refreshing.** A body keys its `resource()` on `tick().generation` and passes `background` to
  `TmApi.read`. When `clear` is set, it drops what it shows first. A body that runs reports may also
  pass `priority: 'low'` (spec 0024 §10.1).
- **Reporting.** Its `report` calls feed the status line (§8.5), and the scheduler times the next
  run from the end of the reported one (§11.1). A body that never reports is treated as live: only
  a forced refresh ticks it.

## 10. Dashboard parameters

A dashboard may declare parameters, shown in a bar above the grid. Each report widget's parameters
take them by key unless the widget binds them otherwise.

### 10.1 Declaration

```csharp
public sealed record DashboardParameter(              // one item of ParametersJson
    string Key,                                       // spec 0033 §4.1's grammar; unique ignoring case
    string? Label, string? Label2, string? Label3, string? LabelKey,   // default label: the key
    bool Required,                                    // bound tiles wait until it has a value
    string Type,                                      // spec 0033 §4.1's vocabulary
    string? Entity,                                   // a picker; Numeric only
    ReportDisplay? Display, ReportFormat? Format);    // spec 0033 §4.6: the control and the text of its value
```

```ts
// @tellma/core (primary): the record in camelCase; spec 0034's field and codec take it unchanged
type TmDashboardParameter = Omit<TmReportParameter, 'default' | 'hasDefault' | 'integer'>;
```

- **No default; two states.** A dashboard parameter is supplied or unset; clearing its field unsets
  it. Unset, it sends nothing, so each bound report applies its own default.
- **"No restriction".** A widget that needs no restriction where the report has a default binds
  the fixed value `null` (§10.2).
- **Editor prefill.** The editor prefills `Type`, `Entity`, `Display` and `Format` from the first
  report parameter bound to the dashboard parameter. A binding compares only `Type` and `Entity`
  (§5.3), so a dashboard may present a parameter differently from its reports.

### 10.2 Binding

`tmBindWidgetArguments` resolves each parameter a report declares (the `view` summary's, or the
`codedReports` entry's), in order:

1. **Explicit binding** — `settings.bindings[key]`, matched ignoring case:
   - `{ "value": v }` sends `v`. A fixed `null` sends `null`, read as omitted (spec 0033 §4.4).
   - `{ "parameter": "d" }` takes dashboard parameter `d`.
   - `{ "unbound": true }` sends nothing and skips step 2, so a same-key dashboard parameter does
     not feed it.
2. **Same key** — the dashboard parameter whose key equals the report parameter's, ignoring case.
3. **Unbound** — nothing is sent; the report applies its default or no restriction.

In steps 1 and 2, a dashboard parameter gives its supplied value, or nothing when unset. An unset
required one makes every tile it feeds wait (§8.6). Only the report's own keys are sent.

```ts
declare function tmBindWidgetArguments(
  reportParameters: readonly { readonly key: string; readonly required: boolean }[], // the report's declared parameters
  settings: TmJson | null,                        // the widget's settings, holding its bindings
  declared: readonly TmDashboardParameter[],      // the dashboard's parameters (§10.1)
  args: TmReportArgs,                             // the supplied dashboard arguments (§10.3)
): { readonly args: TmReportArgs; readonly waitingFor: string | null };   // waitingFor: the unset required dashboard parameter's key
```

**Example —** Acme's "Sales overview" declares `Customer` (Numeric, `acme.Customer`, optional) and
`Year` (Numeric, required). The URL is `/17/dashboards/3?p.Year=2026`:

| Widget | Report parameters | Bindings | Sent |
|---|---|---|---|
| Credit exposure (coded `acme-credit-exposure`) | `AsOf` (`[ReportParameter(required: true, hasDefault: true)]`), `Customer` | none | `{}`: `Customer` is unset; the C# applies `AsOf` |
| Sales by center | `Year` (required, default `year(today())`) | none: same key | `{ "Year": 2026 }` |
| Last year by center | `Year` | `"Year": { "value": 2025 }` | `{ "Year": 2025 }` |
| Sales by customer | `From` (required, defaulted), `To` | none | `{}`: `From` takes its default, `To` is omitted |

With `?p.Year=2026&p.Customer=1012`, Credit exposure gets `{ "Customer": 1012 }`. With no `Year`
from the URL or the remembered values (§10.3), Sales by center waits for Year, and Last year by
center still runs.

### 10.3 The bar, the URL and remembered values

- **The bar** shows only when the dashboard declares parameters, in the header band (§12.2). It
  holds one `tm-report-parameter-field` per parameter (spec 0034 §4.1), in spec 0034 §4.2's layout,
  each with its parameter's display.
- **Unset display.** When every bound tile's last result echoes the same value with source
  `Default` (spec 0033 §9.1), the field shows that value as `effective`, formatted through its
  display and hinted "Default". Otherwise the field is empty, with the placeholder "Report
  default".
- **A commit** re-runs every tile the changed key feeds, as an argument change: the held output
  dims (spec 0034 §3.4). It also resets their due times (§11.1).
- **URL.** The bar writes `?p.{Key}=` through spec 0034 §4.4's codec, over the dashboard's
  parameters. A supplied value is written and an unset one is absent; `p.Key=` reads as unset.
  Changes use `replaceUrl`.
- **Precedence.** The URL first, where any `p.*` key defines every parameter. Then page state
  (§11.4), then the `dashboard.<id>.params` preference (spec 0025 Appendix A), else unset.
  Code-defined grids (the home) have no parameters.

**Example —** Farah last left "Sales overview" with Customer 1012. She now opens a shared link
`/17/dashboards/3?p.Year=2026`. Customer is unset, since the URL defines every parameter.

## 11. Refresh

One scheduler per grid refreshes each tile on its interval, silently, while the page is visible.
A forced refresh clears every tile and shows its spinner.

### 11.1 The scheduler

| Rule | Detail |
|---|---|
| Interval | the tile's `refreshMinutes`, else the dashboard's `RefreshMinutes`; with neither, the tile never refreshes on its own, in present mode too. The built-in lists are never ticked (§9.2), and denied tiles never run (§6.2) |
| Due time | the end of the tile's last run, success or failure, plus the interval, so a tile's runs never overlap |
| Conditions | a due tile runs only while the document is visible, `TmApi.online()` holds and the page is not in edit mode. Otherwise it waits; when the conditions hold again, every overdue tile runs at once, in reading order |
| First runs | start together when the page opens, in reading order. The client caps nothing: tile runs on `reports/run` and on coded report routes take the batch lane of `tellma-reports`, which runs 6 per user and queues 24 (spec 0016 §8.2), so the top tiles settle first |
| One timer | one `setTimeout` to the earliest due time, re-armed after every run and every change of the conditions; `TM_NOW` drives it in tests |

**Example —** a dashboard with `RefreshMinutes = 5` and one tile set to 1 minute: that tile
refreshes every minute and the others every 5, silently. Farah switches tabs at 10:00 and returns at
10:20. Nothing ran meanwhile; on return every tile runs once, and the status lines move to 10:20.

### 11.2 Priority, silent runs and the session

- **Low priority.** Every tile run sends `Tellma-Priority: low` (spec 0016 §6.1): first runs,
  scheduled runs, forced refreshes and present mode alike. On `reports/run` and on a coded report's
  route the server runs it on the batch lane of `tellma-reports` (spec 0016 §8.2), so a dashboard
  never takes a permit that a report page's run needs. A tile's Export to Excel is the user's own
  request and stays on the interactive lane.
- **Background runs.** A scheduled run is `host.run()` under `refresh="silent"`. Outside present
  mode it sends `Tellma-Activity: background` (spec 0034 §3.4, spec 0024 §10.2), so it stamps no
  activity and renews no cookie (spec 0016 §6.1). A dashboard left open on a desk therefore never
  keeps a session alive. Opening the page, a parameter commit, a forced refresh and a tile's
  Refresh are foreground runs.
- **Present mode keeps the session alive.** While presenting, every tile run, first runs and
  scheduled runs alike, is sent without `Tellma-Activity: background` (the tick's `background` is
  false). Each one stamps activity and renews the session cookie (spec 0011 §5.5), so a display
  that refreshes stays signed in until the session's absolute lifetime. A dashboard without an
  interval sends nothing after its first runs, so its session ages as usual.
- **Errors** never open a dialog (spec 0024 §11):
  - a tile with data keeps it and shows the stale marker (§8.5);
  - a `401` follows spec 0024 §8.6's expiry path for the whole tab;
  - `403` and `404` clear the held data (spec 0034 §3.4), so revoked data never lingers.
- **No inactivity pause.** A visible dashboard refreshes until its session expires, then shows spec
  0024 §8.6's "Your session ended" dialog.

### 11.3 Forced refresh and a tile's Refresh

- **Refresh** is the page's `dashboard.refresh` (`refresh: 'page'`, spec 0031 §3.7). One click
  calls `injectTmDetails().refresh()`, which re-reads the dashboard, and `refreshAll()`, which
  clears every tile:
  - report tiles call `run({ clear: true })` and show their skeleton at once;
  - other types get a `tick` with `clear` set, and denied tiles stay as they are;
  - the assignment lists show `loading` and call `TmAttention.refresh('assignments')` (spec 0028
    §5.2).

  A polite status says "Refreshing the dashboard", then "Dashboard updated" once every tile has
  settled.
- **A tile's Refresh** clears and re-runs that tile only. Both commands reset due times; a run in
  flight is aborted and restarted through the host's `resource()` key.

### 11.4 Returning to a dashboard

- **On destroy** the page stores `{ args, held, scroll }` under
  `tmPageStateKey('/{t}/dashboards/{id}/tiles')` (spec 0025 §10). This entry is separate from the
  details host's own snapshot. `held` maps each tile key to `{ result, endedAt, expansion?,
  asTable? }`: a pivot tile's expansion and a chart tile's table view.
- **On a return within the app,** the page renders every held result at once through the host's
  `held` input, restores each tile's expansion and table view through the host's `expansion` and
  `asTable` models (spec 0034 §3.1), and restores the scroll. Tiles whose due time has passed then
  run silently; a tile without an interval runs when its result is older than 5 minutes. Entries
  for removed keys are dropped.
- **A stale entry** (`isStale`, spec 0025 §10) renders the same way, then silently runs every tile
  that has a held result, whatever its due time. The details host's own snapshot is stale too, so
  the dashboard re-reads (spec 0031 §13) and the grid applies the new `view` (§6.2).

## 12. The dashboard page

`/{t}/dashboards/{id}` is the `core.Dashboard` details page with `TmDashboardPage` as its `page`
hatch (spec 0031 §3.7), under the compact header (`details.header: 'compact'`, spec 0029 §3, spec
0031 §5.4). The host owns the header, modes, drafts and save. The hatch renders the parameter bar in
the header band, the grid in view mode, and the editor in edit and create modes. `?present` turns
the page into present mode (§12.4).

### 12.1 The declaration

```ts
// @tellma/core primary, in the implicit core feature (spec 0024 §2.3)
export const tmDashboards = tmEntity('core.Dashboard', {
  section: 'analysis', order: 20, icon: 'layout-dashboard', synonymsKey: 'Core_Dashboard_Synonyms',
  columns: ['Title', 'Owner', 'RefreshMinutes', 'ModifiedAt'], orderBy: 'Title',
  tabs: [{ key: 'all', labelKey: 'tm.dashboard.tabAll' },
         { key: 'mine', labelKey: 'tm.dashboard.tabMine', queryex: 'OwnerId = me()' },
         { key: 'shared', labelKey: 'tm.dashboard.tabShared', named: 'shared-with-me' }],  // §14
  details: { page: () => import('@tellma/core/dashboards').then((m) => m.tmDashboardPage()), // view, edit and create
             refresh: 'page', header: 'compact' },                          // Refresh is dashboard.refresh (§12.2)
  clone: (copy, source) => ({ ...copy, code: null, basedOnId: source.id }),
  actions: [{ name: 'share', visible: () => false, bulk: false, dialog: tmShareDialog('dashboards') }, // opened by dashboard.share
            { name: 'publish', visible: () => false, bulk: false },                          // the share dialog covers it
            { name: 'transfer', dialog: tmTransferDialog() }],
});
// @tellma/core/dashboards, main module
declare function tmDashboardPage(): Promise<Type<unknown>>;   // imports the page chunk (§1.1)
```

| Member | Rule |
|---|---|
| widget types | the core feature's `widgets` lists §4.2's four types; `core.report`'s `component` loads the report widget chunk, and §13.4's widget dialog edits its settings |
| wire shapes | rows are `TmEntityType<'core.Dashboard'>` (spec 0029 §15); `TmDashboardView` mirrors §6.1 in camelCase; `parametersJson` and `settingsJson` parse through `tmDashboardModel` (§8.1) |
| abilities | `can.edit`, `can.delete` and `can.activate` read the `permissions` extra (spec 0015 §5.3, spec 0031 §4.1); Share reads its `refused['action:share']` and `refused['action:publish']`. A viewer of a shared dashboard sees neither Edit nor Share |

### 12.2 Header and actions

The page uses the host's compact header (spec 0031 §5.4): row 1 holds the crumb, the title and its
badges at the start and the action bar at the end. There is no toolbar row and no record pager.

| Control | When | Action id (group, order) |
|---|---|---|
| crumb "Dashboards"; `h1`, the title in the active slot; the host's badges (spec 0031 §5.4) | always | — |
| Share (§7) | view mode, unless `action:share` and `action:publish` are both refused | `dashboard.share` (primary, 10) |
| Present (`presentation`, §12.4) | view mode | `dashboard.present` (primary, 20) |
| Refresh (`refresh-cw`, §11.3) | view mode | `dashboard.refresh` (primary, 25: the host's slot, spec 0031 §3.7) |
| Edit | `can.edit` | `details.edit` (menu, first) |
| New, Clone, Copy link, Change owner, Activate or Deactivate, Delete | per ability | `details.create`, `details.clone`, `dashboard.copy-link`, `core.dashboard.transfer`, `details.activate` or `.deactivate`, `details.delete` (menu) |
| Add widget; Dashboard settings; Cancel; Save | edit and create modes | `dashboard.add-widget`, `dashboard.settings`, `details.cancel`, `details.save` (primary) |

- **The band.** The parameter bar (§10.3) renders in `[tmPageHeaderBand]` (spec 0023 §7.2), only
  when the dashboard declares parameters. It stays in edit mode, so the author can test bindings,
  and it never scrolls away.
- **Clone** is the dashboard's "Save as" (§13.6). **Copy link** copies the canonical URL with its
  `p.*`, and announces "Link copied".
- **Delete** is the host's (spec 0031 §10.2). It ends on the dashboards page and announces
  "Dashboard deleted." politely (spec 0028 §8.1). The reports the dashboard showed stay.
- **Pins and recents.** Pins (spec 0027 §8) keep `p.*`. The page counts as a recent of kind
  `dashboard` (spec 0025 §13).

### 12.3 Body and states

The body is the grid. The header, the band and the grid share §8.1's centred column, and the body
region scrolls under the header.

| State | Shows |
|---|---|
| loading (`get`) | spec 0024 §4.4's `dashboard` skeleton, in the compact geometry: the header row, then one body block that holds no control, so the swap moves none. The band appears when `get` lands, since its height depends on the dashboard, not on data |
| ready | the grid, every tile in its state (§8.6) |
| no widgets | `tm-empty-state`, `layout-dashboard`, "This dashboard has no widgets yet", and "Add widgets" (enters edit mode) when the user can edit |
| not found, forbidden, offline, failed | spec 0031 §3.5's states |

### 12.4 Present mode

Present mode shows the dashboard alone, for a TV or a kiosk. `?present` defines it: a layout
without chrome, which browser full screen enhances when the browser allows it.

- **Entering.** `dashboard.present` navigates to `?present`, keeping `p.*`, and pushes history, so a
  remote's Back exits. The component stays, so held tiles do not re-run. In the same click it
  calls `document.documentElement.requestFullscreen()` and requests a screen wake lock. Full screen
  targets the root, so CDK's overlays (modals, toasts and the "Your session ended" dialog) stay
  visible. A cold-loaded `?present` URL, such as a kiosk's bookmark, has no user gesture, so it
  renders without chrome and without full screen.
- **Layout.** `TmShell.setImmersive(true, destroyRef)` (spec 0027 §5.1) hides the shell's header
  rows. The page keeps the compact header (spec 0031 §5.4) on one row: its band collapses to spec
  0034 §4.2's compact "Parameters" button and summary, and the page registers only two actions,
  "Full screen" (only when the document is not full screen and `document.fullscreenEnabled`) and
  "Exit presentation" (`x`). The column cap is lifted
  (`--tm-dashboard-max-inline: none`). The pointer hides after 3 s without movement, unanimated.
- **Display only.** The grid gets `presenting`: tiles show no menu and drill-through is off, so a
  stray tap never strands a lobby screen on a list page.
- **Refresh and session.** Tiles refresh at their intervals (§11.1), and their runs keep the session
  alive (§11.2). An unattended display should sign in as a dedicated user whose roles only read,
  because anyone at the screen can press Exit and use the app as that user.
- **Wake lock.** It is requested again when the document becomes visible, and released on exit. A
  refusal, such as on low battery, is silent.
- **Leaving.** Exit, Back, or Escape with no overlay open (a shortcut, spec 0027 §4.3). Present mode
  entered with full screen also ends when full screen does (`fullscreenchange`). Leaving exits full
  screen and releases the wake lock.

## 13. Edit mode

Edit mode rearranges, resizes, adds, configures and removes widgets in place, on the details host's
working copy. Save writes the dashboard and its widgets in one `dashboards/save`.

### 13.1 Entering and leaving

- **Entering.** Edit is the host's `edit()`; the hatch swaps the grid for `TmDashboardEditor`.
  Drafts, the leave guard and the conflict dialog are spec 0031's (§9, §11), so a reload restores
  edit mode.
- **Tiles keep their data.** A move or resize re-runs nothing; a new or reconfigured report widget
  runs once. The scheduler pauses, and tile bodies are `inert`.
- **Tile keys.** Tiles track by `w{id}`. A row without an id gets `n{k}` when it enters the
  editor, kept in a key list parallel to `widgets`.

### 13.2 The canvas

The canvas always shows the wide layout. From `md` up it is as wide as the grid's column (§8.1);
below `md` it is a fixed 768 px, in a region that scrolls horizontally.

- **Tile controls.** Each tile gains a header grip, which is a `button` named "Move or resize
  {title}". It also gains a pointer-only resize handle at its block-end, inline-end corner. Both
  take `touch-action: none`.
- **While dragging or resizing,** twelve dashed column guides show, and the tile follows the
  pointer. A move translates it on a raised layer; a resize grows its frame while the body stays
  clipped at its starting size and lays out once, on drop. A placeholder, a dashed
  `--border-strong` box, marks the cells where the tile lands. The other tiles show the pushed and
  compacted preview, each change in one step.
- **Nothing animates.** The placeholder, the other tiles, the drop and an Escape cancel all apply in
  one frame, under every motion preference.

### 13.3 Moving and resizing

```ts
declare function tmMoveWidget(widgets: readonly TmDashboardWidgetModel[], key: string, x: number, y: number): readonly TmDashboardWidgetModel[];
declare function tmResizeWidget(widgets: readonly TmDashboardWidgetModel[], key: string, w: number, h: number): readonly TmDashboardWidgetModel[];
```

Each operation clamps the target into the grid and to the type's `minSize`. It then pushes every
tile the target overlaps down below it, and compacts (§8.2 rule 1). The result is what Save sends.
Every operation writes through `injectTmDetails().update(…)`, so drafts follow.

- **A drag previews from its start.** At each new snap cell, in one `requestAnimationFrame`, the
  editor applies `tmMoveWidget` or `tmResizeWidget` to the layout as it was when the drag began,
  never cumulatively, so a tile dragged down and back up restores every tile it pushed.
- **A drop writes once.** The working copy gets one `update()` on drop, so drafts and the dirty flag
  follow the result, not the path. Each keyboard step writes on its own.
- **Physical to logical.** The pointer is tracked in physical pixels and converted to logical `x`
  when snapping; in RTL, dragging left increases `x`.
- **Auto-scroll.** With the pointer within 40 px of a canvas edge, the canvas scrolls, faster the
  deeper the pointer goes, so a tile can travel past the fold.

| Input | Move | Resize |
|---|---|---|
| pointer | drag the grip (pointer capture, snapping to cells; Escape cancels) | drag the corner handle |
| keyboard, focus on the grip | Up, Down; Left and Right follow the visual direction (in RTL, Left is inline-end) | Shift with the arrows: Up and Down change `h`; the visual arrows grow or shrink `w` |
| single pointer, no drag (WCAG 2.5.7) | tile menu Move ▸ Up, Down, To start, To end | tile menu Size ▸ Quarter (3), Third (4), Half (6), Two thirds (8), Full width (12), Taller (+3), Shorter (−3) |

Each change announces politely: "Sales by center: row 3, column 7, 6 columns by 9 rows." A key that
cannot move the tile announces "Can't move further."

### 13.4 Adding, configuring, removing

- **Add widget** opens the gallery: a `TmModal` (`md`, full screen below `sm`) with a
  `tm-segmented` of two lists.
  - **Reports** holds two labelled groups, never merged. "Built-in" lists the `codedReports` entries
    whose `securable` passes `TmPermissions.can`, by title. "Saved reports" is a search over
    `reports/query` (spec 0033 §5.5), every stored report the user can read.
  - **Other** holds the registered types whose `requires` passes.
- **A chosen widget** is appended at `x = 0`, below the lowest tile, at its `defaultSize`. The
  editor compacts, focuses the new tile's grip and announces "Added Monthly sales." If a required
  parameter is left unbound (§5.3), the widget dialog opens at once.
- **Edit widget…** opens the widget dialog, a `TmModal` (`md`). It holds Title and Description per
  content slot, and "Refresh": a `tm-segmented` of "Dashboard default ({n} min)" or "Every …
  minutes". Other types add their `settingsEditor`, which writes `settings`.
- **Parameters** (report widgets). The dialog shows one row per report parameter: its label and a
  `tm-select` of "Same as dashboard parameter {X}" (when a same-key parameter exists), "Dashboard
  parameter…", "Fixed value" (a `tm-report-parameter-field`) and "Report default". "Report default"
  is refused inline for a required parameter without a default, a coded one's `hasDefault` included
  (§5.3 rule 3). The four choices store, in order: no binding; `parameter`; `value`; and
  `unbound: true`, or no binding when no same-key dashboard parameter exists (§10.2).
- **Remove** is in the tile menu, or Delete on the grip. It happens at once, unconfirmed, since
  Cancel restores everything. Focus moves to the next tile's grip, else the previous one, else Add
  widget.

### 13.5 Dashboard settings

- **Fields.** The dialog is a `TmModal` (`md`) with Title per content slot, whose placeholder is the
  default title (§3.1), Code, and "Refresh every … minutes". Empty means never; the server checks
  the floor (§3.3).
- **Parameters.** A `tm-reorder-list` (spec 0023 §8.4) below the fields. Each item holds Key, Label
  per slot, Type, Entity, Required, and Display with its format fields (spec 0033 §4.6). Entity is
  an entity picker over queryable entities, for `Numeric` only.
- **Removing a bound parameter.** Before a parameter that widgets bind is removed, the dialog names
  those tiles.

### 13.6 Save, errors, create, Save as

- **Save** is the host's `save()`: one `dashboards/save` with `widgets` synchronized; shares are not
  part of the save (spec 0033 §5.1). New rows go without an id; removed rows are absent.
- **Errors.** The editor registers a field (spec 0031 §4.1's `registerField`) for every path it can
  show, so spec 0031 §8.4's summary works as is:
  - `Title`, `Code`, `RefreshMinutes` and `ParametersJson` open the settings dialog at the control;
  - `Widgets[i]` marks tile *i* invalid (§8.6) and focuses its grip;
  - `Widgets[i]`'s `Title`, `RefreshMinutes` and `SettingsJson` open that widget's dialog.
- **Create.** `/{t}/dashboards/new` ("New dashboard" on the search page) opens the editor on an
  empty canvas showing "Add your first widget". A dashboard saved untitled takes the default title
  (§3.1).
- **Save as** is the host's clone (`?clone={id}`). It copies the widgets, applies the declaration's
  `clone` (§12.1), and makes the caller the owner. A widget whose stored report the caller reads
  only through the source, or whose coded report's securable the caller lacks, fails
  `Dashboard.ReportInvisible` (§5.2 rule 3), so a copy never widens access. The user removes those
  tiles.

## 14. The dashboards page

`/{t}/dashboards` is the plain `core.Dashboard` search page (spec 0030), built from §12.1's
declaration. It is the launcher's Dashboards entry under Analysis (spec 0027 §3.2).

- **Rows** are the dashboards the caller may read (spec 0033 §5.3). Columns: Title (the link),
  Owner (avatar and name), Refresh, Modified. The Owner avatar's `colorKey` is the record key
  (spec 0029 §12.2).
- **Tabs.** All dashboards is the default and is absent from the URL. My dashboards (`tab=mine`)
  adds the client leaf `OwnerId = me()`. Shared with me (`tab=shared`) sends the named filter
  `shared-with-me` (spec 0033 §5.5), which `Shareable<,>()` contributes to this stack too. A tab
  change is a query change (spec 0030 §2.4).
- **New dashboard** shows for every member, since every member's `Save` is filtered.
- **Bulk actions** derive from the entity actions: Activate, Deactivate, Delete, Change owner. Share
  and publish stay on the dashboard page.
- **Empty states.** All: "No dashboards yet". Mine: "You haven't created a dashboard yet", with New
  dashboard. Shared: "Nothing has been shared with you yet".

## 15. The home page

`/{t}` shows a greeting and date line (§15.2) over a code-defined grid of the three built-in
widgets (§15.3). Every part renders from stored documents, with no request on a warm launch.

### 15.1 Route and host

- **Route.** `''` under `:t` (spec 0024 §9.2), with `TmRouteData { kind: 'home', skeleton: 'home',
  load: () => import('@tellma/core/dashboards').then((m) => m.TmHomeHost) }`.
- **`home` skeleton.** Two greeting lines over §15.3's three tile frames. Each frame has its header
  and five skeleton rows, at final geometry.
- **`TmHomeHost`** renders the winning `home` hatch from `TM_APP_DECLARATIONS` (spec 0024 §2.2),
  else `TmHomePage`, through `NgComponentOutlet`. It registers the `home` page context (§16), sets
  the title "Home" (`document.title` "Home · Acme") and records `tm.home.rendered`.
- **`TmHomePage`** is `<tm-home-greeting />` over `<tm-dashboard-grid [widgets]="TM_HOME_WIDGETS" />`,
  labelled "Your work", in §8.1's centred column.

### 15.2 Greeting and date

```ts
// @tellma/core/dashboards
@Component({ selector: 'tm-home-greeting' }) declare class TmHomeGreeting {}   // the h1 [tmPageTitle] and the date line
```

- **Time of day.** The greeting reads the hour of `TM_NOW` in `TmLocalization.timeZone()`, with
  `Intl.DateTimeFormat` (`hourCycle: 'h23'`, that zone):

  | Hours | English | Arabic |
  |---|---|---|
  | 05:00–11:59 | "Good morning, {name}" | "صباح الخير، {name}" |
  | 12:00–16:59 | "Good afternoon, {name}" | "مساء الخير، {name}" |
  | otherwise | "Good evening, {name}" | "مساء الخير، {name}" |

- **Name.** The user's `Name` from `me` (spec 0016 §3.4), whole, in the active content slot, else
  slot 1, rendered in `<bdi>`: "Good morning, Farah Nasser", "صباح الخير، عبد الرحمن الشهري". A long
  name ellipsizes with the heading.
- **Date line.** Today, in the display zone and the user's calendar:
  `l10n.formatDateTime(new Date(now).toISOString(), { dateStyle: 'weekday', timeStyle: 'none' })`
  (spec 0010 §D.8). It gives "Tuesday, 20 Oct 2026", or "الثلاثاء، ٩ جمادى الأولى ١٤٤٨" in Arabic
  with Umm al-Qura. No location suffix follows the date; a `home` hatch can add one.
- **Layout.** Padded 14×16 px (11×10 below `sm`), aligned with the grid's start edge. First a
  16/600 heading on one line with ellipsis: the page's `h1` (`[tmPageTitle]`, the focus target on
  arrival, spec 0024 §4.4). Then the 12 px secondary date line, in a `p`.
- **Live.** One timer re-renders at the next time-of-day boundary or at midnight in the zone. It
  re-renders again when the document becomes visible, since hidden tabs throttle timers. A
  language, calendar or zone change re-renders in place, and the one-line heading moves nothing.

### 15.3 The default widgets

```ts
// @tellma/core/dashboards
export const TM_HOME_WIDGETS: readonly TmDashboardWidgetModel[] = [
  { key: 'home.to-me',   type: 'core.assignments-to-me', x: 0, y: 0, w: 7, h: 9 },
  { key: 'home.recents', type: 'core.recents',           x: 7, y: 0, w: 5, h: 9 },
  { key: 'home.by-me',   type: 'core.assignments-by-me', x: 0, y: 9, w: 7, h: 9 },
];
```

- **Layout.** Wide shows My assignments over Assigned by me, with Recently visited beside them
  (7 : 5). §8.2's example gives both layouts.
- **Ten rows.** Each list holds up to 10 rows (spec 0028 §7.4) in a 350 px tile. From `sm` up, a
  33 px header and ten 31 px rows fit, so a full list never scrolls and an empty one keeps its box.
  Below `sm`, rows are 44 px: seven show, and the list scrolls inside its tile.
- **Fixed in v1.** Home has no customizing, report widgets or parameters (§15.5 names the seam).

### 15.4 States

| Situation | Greeting | Assignment lists | Recents |
|---|---|---|---|
| warm launch | from the stored `me` | from the stored `assignments` document; no request | from the stored `nav.recent` (spec 0025 §13) |
| cold, before `client/sync` (the route host's `home` skeleton, spec 0024 §4.4) | two skeleton lines of final geometry | five skeleton rows each | five skeleton rows |
| nothing assigned or visited | as usual | §9.2's empty states | §9.2's empty state |
| offline with no document | as usual | §9.2's offline state | as stored |
| tenant `ReadOnly` or `Frozen` | as usual | as usual: spec 0028 §5.4 hides only verbs | as usual |

An arriving assignment updates its list and count in place; no other tile moves.

### 15.5 The seam for per-role and per-user home pages

v1 renders `TM_HOME_WIDGETS` for everyone. A later design adds, without moving anything v1 ships:

1. **Server:** a per-user home dashboard pointer and ordered role rules, resolved to one dashboard.
2. **Client:** `TmHomeHost` renders the resolved model through the same `tm-dashboard-grid`, with
   `dashboardId` for read-through, else `TM_HOME_WIDGETS`. That design decides how the model
   reaches a warm launch. The preference segment `home` is reserved (spec 0025 Appendix A).
3. **Precedence:** a feature's `home` hatch still wins.

v1 keeps the seam open: home renders only through the grid and the widget model, and no widget
type is home-specific.

## 16. Page contexts, actions and telemetry

Both pages publish to the page-context registry (spec 0026 §9) from state they already hold.

| Kind | Registered by | `state` | `visible` |
|---|---|---|---|
| `home` (route `/:t`, title "Home") | `TmHomeHost` | `{ hatch: boolean }` | the three lists' rows, at most 30, with columns `list`, `title`, `detail`, `when`; a row's id is the assignment id, null for recents |
| `dashboard` (route `/:t/dashboards/:id`, entity `core.Dashboard`) | `TmDashboardPage`, replacing the host's `details` (spec 0031 §3.7) | `{ dashboard: number \| null, mode, present: boolean, parameters, widgets: [{ key, type, title, status, executedAt }] }` from `TmDashboardGrid.tiles` | — |

- **Actions.** `home` has none ("View all" is a link). `dashboard` lists §12.2's own actions, then
  the host's `details.*` and entity actions, which the registry keeps (spec 0026 §9.2).
- **Masking.** The `dashboard` state's `parameters` hold the supplied arguments. Snapshots without
  page data mask them (spec 0026 §9.6).

**Telemetry** (closed properties, spec 0026 §3.2):

| Event | Kind | Properties |
|---|---|---|
| `tm.home.rendered` | timing: route start to the greeting and three tiles painted | `warm: bool`, `hatch: bool`, `lists: data \| skeleton` |
| `tm.home.item-opened` | event | `list: to-me \| by-me \| recents`, `position: 1 \| 2-3 \| 4-10` |
| `tm.home.view-all` | event | `list: to-me \| by-me` |
| `tm.dashboard.opened` | timing: route start to every tile settled | `widgets: 1 \| 2-5 \| 6-12 \| 13-24`, `parameters: bool`, `denied: 0 \| 1+`, `outcome` |
| `tm.dashboard.refreshed` | event | `trigger: auto \| force \| tile \| resume \| parameter`, `tiles` (the same buckets) |
| `tm.dashboard.tile-state` | event, once per tile per visit | `state: waiting \| not-available \| denied \| stale` |
| `tm.dashboard.presented` | event, on entering present mode | `fullscreen: bool`, `wakeLock: bool` |
| `tm.dashboard.edited` | event, once per `op` and `via` per edit session | `op: add \| remove \| move \| resize \| configure`, `via: pointer \| keyboard \| menu` |
| `tm.dashboard.saved` | event | `isNew: bool`, `widgets` (buckets), `outcome: ok \| invalid \| conflict \| error` |

Widget runs also emit spec 0034's `tm.report.run` with `mode: widget`. The server adds no instrument.

## 17. Testing

| Suite | Tier | Pins |
|---|---|---|
| Validator (C#) | unit, PR | every Appendix A code at its path; §5.3's table row by row, for a stored and a coded report (`hasDefault`); coded keys against the registry; bounds and overlap; strict settings; the kept-reference rule; a parameter display vector per `rule` |
| Dashboards (C#, real SQL) | integration, PR | the list below |
| Web (C#) | integration, PR | routes and retry classes, with `dashboards/audience` in the Contracts chunk's `long` and `idempotent` lists; `reports/run` with `DashboardId` end to end; `Tellma-Priority: low` taking the batch lane; a background run stamps no activity; `core.dashboard.shared` raised and withdrawn |
| Layout (vitest) | unit, PR | `tmPackDashboard` vectors (compaction with hidden tiles, bands, narrow rows); `tmMoveWidget` and `tmResizeWidget` (clamp, cascading push, compaction); a drag preview from its start snapshot, down and back up restoring every tile; arrow mapping in LTR and RTL |
| Bindings and greeting (vitest) | unit, PR | `tmBindWidgetArguments` over §10.2's table, fixed `null`, `unbound` beside a same-key parameter, waiting; time-of-day greetings with the full name and the date line across zones and calendars with a fake `TM_NOW` |
| Scheduler (vitest, fake clock) | unit, PR | completion-based due times; tile overrides; the visibility, online and edit conditions and the catch-up; denied tiles never run; forced and tile refresh, the forced one through `dashboard.refresh` with no `details.refresh` registered, and a stale return clearing no tile; foreground ticks in present mode; the page-state return with expansion and table view; an `isStale` entry running every tile with a held result while the built-in lists stay untouched |
| Harnesses (`@tellma/core-testing`) | PR | grid, page and home: every state of §8.6, §9.2, §12.3 and §15.4; `denied` shown and hidden; §8.5's status line, with its omitted-totals note and the empty line after a failed first run; editor operations and dialogs; the gallery's two groups; present mode; a fixture widget type; a `home` hatch through `TmHomeHost`; two tiles of one summary report with no grid-state collision |
| Playwright on Acme | e2e, PR | §18's journeys at 390, 768, 992, 1440, 1920 and 2560 px in `en` and `ar`; the grid's column 1,600 px wide and centred at 1920 and 2560, and full width in present mode; by request interception, `Tellma-Priority: low` on every tile run, and `Tellma-Activity: background` on scheduled runs except in present mode |
| axe, budgets | PR | home, dashboard view, present mode, edit mode, gallery and dialogs, in light and dark, LTR and RTL; §1.1's budgets; the home route's transfer excludes `/details`, `/entity`, `/reports`, `/sharing` and `core-ui/chart` |

The **Dashboards** suite pins:

- CRUD with widget sync, shares not part of the save and never stamping it; the default title per
  slot; the foreign key blocking a stored report's delete with `Report.InUse`, naming a dashboard
  the caller reads and counting one it does not;
- `ReadThroughAsync` as filter vectors (shown, not shown, dashboard invisible, an `Unrestricted`
  dashboard decision), with a differential test against a two-step reference query, and
  `DashboardsUsing`;
- the `view` extra: summaries without a share, `Readable`, `MissingAccess` for a denied root and a
  child root, and no extra round trip;
- spec 0033's sharing suite parameterized for `core.Dashboard`: the Everyone row, `shared-with-me`
  and the `shares` extra; and `audience` with per-tile `CannotRead` for a stored and a coded tile.

## 18. Definition of done

- **Server.** §3–§7 ship with §17's server suites green. The parts carried by the specs listed under
  Context are in place.
- **Client.** The entry point and its chunks stay within §1.1's budgets, with API goldens.
- **UI rules:**

| Rule | How this design meets it | Pinning test |
|---|---|---|
| Instant warm launch | home renders the greeting, the date, both lists and recents from stored documents with no request; the dashboard route draws its skeleton from cached chunks, and only `get` and the runs wait | spec 0024's `warm-launch.spec` on `/17` and `/17/dashboards/3` with every API request held: home fully rendered, the dashboard's header and skeleton within 1 s |
| Instant feedback | Refresh clears tiles in the click's frame; a dragged tile follows the pointer in the same frame; moves and resizes apply synchronously; the editor chunk is prefetched on hover and focus of Edit; a parameter commit dims bound tiles at once | `dashboard-feedback.spec`: a frame probe after Refresh, a pointer drag (one draft write per drop), a keyboard move and a commit |
| Non-blocking | every run is its tile's cancellable `resource()`; leaving aborts them all; tile runs take the batch lane, so they never delay a report page's run | `refresh-scheduler.spec` (abort on destroy, supersession); `dashboard-hang.spec`: hung runs never delay navigation |
| Stable layout | fixed tile heights; a denied tile keeps its place, and a code-defined grid's layout is computed before the first render with hidden tiles removed; the width cap; the status line is reserved; silent swaps happen in place; arriving assignments stay inside their tile | `dashboard-geometry.spec`: tile boxes equal across skeleton, data, silent refresh, failure and a language switch, zero layout shift; `home-geometry.spec`: an assignment arrives over the hub and no other tile moves |
| RTL | logical columns; direction-mapped arrows and drags; `<bdi>` names; `ar-SA` digits; the Umm al-Qura date line | every e2e above in `ar`; `tmPackDashboard` vectors are direction-free |
| WCAG AA | sections labelled by their `h2`; DOM order is reading order; grip buttons with keyboard and menu twins (2.1.1, 2.5.7); polite announcements; `aria-busy` while loading; no animation, only direct manipulation | §17's axe battery; `dashboard-keyboard.spec`: move, resize, add and remove by keyboard only; `dashboard-feedback.spec`: no running transitions on the other tiles under either `prefers-reduced-motion` value |

- **Page-context registry and telemetry** are §16's.
- **Acme** adds no dashboard code (§2.1). Its e2e suite creates "Sales overview" with §10.2's
  parameters, Sales by center, the coded credit exposure report and My assignments. It then covers:
  - home, warm and cold, and an assignment arriving;
  - `p.Customer`, a silent refresh under Playwright's clock, and Refresh;
  - pointer and keyboard edits, a binding, save and a `409`;
  - a share whose bell shows it, with a per-tile audience warning;
  - present mode, entered and left;
  - a viewer without `acme.SalesInvoiceLine` access, who sees the pivot tile's no-access state.

## Decisions record

1. **One grid for dashboards and home; home is a code model** — no read stands between a warm launch
   and home (§8, §15). Alternative: a seeded shared dashboard as home.
2. **Columns for layout and references; JSON for settings and parameters** — references get foreign
   keys; a parameter list is read whole (§3.1). Alternatives: a parameters table; a layout blob.
3. **12 columns of 30 px rows, fixed heights, two layouts, a derived reading order** — nothing moves
   when data lands; position has one truth (§8). Alternatives: a third layout for medium widths;
   content heights; an `Ordinal` column.
4. **Widget types are registered on the server and declared on the client, checked at build** —
   the server refuses an API or MCP typo (§4). Alternative: client-only keys.
5. **A report widget is spec 0034's host in `widget` mode inside this spec's tile** — one report
   pipeline (§9.1). Alternative: a second, dashboard-only renderer.
6. **Same-key binding by default, with explicit bindings to another dashboard parameter, a fixed
   value or nothing; dashboard parameters carry no default** — zero configuration in the common
   case, one client rule for stored and coded reports, and each report gets only its own keys (§10).
   Alternatives: one shared key only (no 2025 beside 2026); a mandatory mapping per widget
   (configuration the key already answers); dashboard default expressions (stale after midnight on a
   wall display); binding inside `reports/run` (couples spec 0033's run to these tables).
7. **A required report parameter with no default is bound to a fixed value or a required dashboard
   parameter** — a hidden parameter can never be left empty (§5.3).
8. **The scheduler is completion-based, runs only while the page is visible, online and not in edit
   mode, and leaves capping to the server** (§11.1). Alternatives: fixed intervals, which overlap;
   a leader-tab scheduler; a client viewport gate.
9. **A user's refresh always clears** — the click shows its effect at once (§11.3). Alternative: a
   silent tile refresh.
10. **On a stored dashboard, a tile whose data the viewer cannot read keeps its place and names the
    permission it needs; code-defined grids hide it** — one layout for every viewer, nothing
    reflows when access changes, and no one asks where a tile went, while a home built for every
    role shows no locked tiles (§6.2). Alternative: hide denied tiles everywhere.
11. **The dashboard page is spec 0031's `page` hatch under the compact header, and edit mode edits
    its working copy** — one lifecycle for drafts, conflicts and leaving, with the dashboard, not
    record chrome, filling the page (§12, §13). Alternatives: an own lifecycle; the standard header
    with its toolbar row and pager.
12. **Moves and resizes by pointer, keyboard and menu; the dragged tile follows the pointer;
    collisions push down, then compact** — users expect drag that tracks the hand, and WCAG 2.5.7
    needs a single-pointer path (§13.2, §13.3). Alternatives: free placement; cell-by-cell jumps.
13. **Home lists show up to 10 rows each, with "View all"** (§15.3). Alternative: 5 and 2 rows.
14. **The greeting uses the user's full name; the date is today in the user's calendar and display
    zone** (§15.2) — a first-name rule is wrong on compound names in every language, and the
    browser's zone can disagree with the app's. Alternatives: a first-token rule; a
    `User.PreferredName` field.
15. **Coded report widgets are checked on save from the server registry** — key, access and
    bindings get the same checks as a stored report's, so a coded tile never breaks unnoticed
    (§5.2, §5.3). Alternative: checks in the editor only.
16. **Present mode is a URL state without chrome; browser full screen is a bonus of the click** — a
    kiosk's cold-loaded bookmark has no gesture to enter full screen, and the root, not the
    dashboard, goes full screen so overlays stay visible (§12.4). Alternative: present mode as
    browser full screen only.
17. **Present mode keeps the session alive; other dashboard refreshes never do** — a wall display
    should not sign out every seven days, and a desk left open should (§11.2). Alternative:
    background runs everywhere, with the display signed out at the idle lifetime.
18. **Tile runs mark themselves low priority and take their own lane** — the server cannot see a
    dashboard in the request before its limiter runs, and a separate lane means a dashboard never
    holds a permit a report page needs (§11.2). Alternative: one shared queue with reserved
    permits.

## Review flags

1. **No seeded dashboards in v1** (§7.3); Acme's e2e creates its dashboard. Alternative: seed
   `acme.sales-overview` as an example dashboard. Flips on wanting a seeded dashboard in every new
   tenant.
2. **Dashboard parameters have no default and two states** (§10.1). Alternatives: defaults
   evaluated by `dashboards/get`; a "cleared" state that sends `null`. Flips if authors need a
   dashboard-wide default unlike their reports', or a "no restriction" switch on the bar.
3. **Fixed heights; no medium-width layout** (§8). Alternatives: content heights; halved widths at
   `md`. Flips on a sparse home reading as broken, or cramped tablet tiles.
4. **Refresh floor 1 minute; no inactivity pause** (§3.3, §11.2). Alternatives: a 5-minute floor; a
   pause after 30 idle minutes. Flips on server load from wall displays.
5. **The client caps no runs; the server queues them** (§11.1). Alternative: a viewport gate with a
   cap in flight. Flips if long phone dashboards paint their top tiles late, or if two open
   24-tile dashboards meet `429`s.
6. **At most 24 widgets** (§3.3). Alternative: 40. Flips if real dashboards hit the cap.
7. **Edit mode at every width, on a 768 px canvas below `md`; no undo** (§13.2, §13.4).
   Alternatives: Edit from `md` only; a session command stack. Flips if phone editing proves
   unusable, or on lost-work reports.
8. **Delete confirms without reading the audience** (§12.2). Alternative: read the audience first
   and name the reach, as spec 0034's review flag 10 weighs for reports. Flips if users delete
   shared dashboards by surprise.
9. **Assistant-only page actions wait** (§16): no per-tile or parameter actions now. Alternative:
   publish them with this spec. Flips if the assistant spec starts before dashboards ship.

## Appendix A — Codes and string keys

**Validation codes** (all `422`, §5; server keys `Dashboard_<Name>` in Core's `Strings.resx`, spec
0013 §10.1):

| Code | Arguments | Code | Arguments |
|---|---|---|---|
| `Dashboard.RefreshOutOfRange` | `min` | `Dashboard.ReportKeyInvalid` | — |
| `Dashboard.ParametersInvalid` | `index` | `Dashboard.ReportInvisible` | — |
| `Dashboard.TooManyParameters` | `max` | `Dashboard.WidgetOutOfGrid` | — |
| `Dashboard.ParameterKeyInvalid` | `index` | `Dashboard.WidgetsOverlap` | `other` |
| `Dashboard.ParameterKeyDuplicate` | `index` | `Dashboard.SettingsInvalid` | — |
| `Dashboard.ParameterEntityInvalid` | `index` | `Dashboard.BindingUnknown` | `key` or `parameter` |
| `Dashboard.ParameterDisplayInvalid` | `index`, `rule` | `Dashboard.BindingTypeMismatch` | `key` |
| `Dashboard.WidgetTypeUnknown` | `type` | `Dashboard.BindingValueInvalid` | `key` |
| `Dashboard.WidgetSubjectInvalid` | — | `Dashboard.WidgetParameterRequired` | `key`, `label` |

Ownership, sharing and standard-row refusals are spec 0033's `Sharing.*` codes.

**Other keys.** Server: `Notification_core_dashboard_shared` (§7.2); `Core_Dashboard_DefaultTitle`
("Untitled dashboard" / "لوحة معلومات بلا عنوان", §3.1); `Core_Dashboard_Synonyms` ("dashboard,
overview, KPIs" / "لوحة، لوحة معلومات، مؤشرات"); entity and member labels by spec 0029 §2.4. Client
(`tm.home.*`, `tm.dashboard.*`; Arabic in `@tellma/locale-ar`): the greetings, `tm.home.title`
"Home" and the grid label "Your work" (§15); the copy of §8, §9.2, §12.3 and §12.4; the tabs
`tm.dashboard.tabAll`, `tabMine` and `tabShared` and their empty states (§14); the gallery's
"Built-in" and "Saved reports" (§13.4); the labels of §13; the announcements of §11.3 and §13.3.

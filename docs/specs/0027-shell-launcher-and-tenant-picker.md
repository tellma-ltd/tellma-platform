# Spec: Shell, Launcher and Tenant Picker

- **Author:** Ahmad Akra
- **Date:** 20 October 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

This spec ships the frame around every page of the Tellma client:

- **The tenantless shell** frames the tenant picker and the signed-out page.
- **The tenant shell** frames every `/{t}/…` page. It has an app bar, a pinned bar, a launcher with
  global search, and a session menu.
- **Two registries** that every page calls: navigation entries and keyboard shortcuts.

The shell builds on the app foundation (spec 0024), the client state (spec 0025) and the component
library (specs 0023 and 0010). A warm launch renders it from stored documents, the boot record and
the bundle, so the chrome never waits on the network. A distribution writes almost nothing here.
Its declarations become launcher entries, and a synonym is one resx line per language.

**Other specs that carry parts of this design**

- spec 0024 §2.2–§2.4 — the feature members `sections`, `entries` and `appBar`; the `section:`,
  `entry:` and `appBar:` keys of `overrides` and `hide`; `tmPage`'s launcher members.
- spec 0029 §3, spec 0034 §2.2 — the launcher members of `tmEntity` and `tmCodedReport`.
- spec 0024 §2.6, §9.2 — the shell routes, loaded through `import()` and preloaded by the build.
- spec 0024 §7.2 — the language switch.
- spec 0024 §9.4 — `TmUrls.describe` and its `pinnable` flag.
- spec 0024 Appendix A — the codes `unknown-section`, `app-bar-overflow` and `app-bar-reserved-id`.
- spec 0025 §13 and Appendix A — recents, and the keys `nav.pinned` and `nav.pinnedBarVisible`.
- spec 0010 §C.10, §C.12 — the menu entry kinds and the grid's key map `TM_GRID_KEYS`.
- spec 0018 §3.1, §3.5 — the `users/me/locale` action.
- spec 0011 §5.9 — `DistributionInfo.links`.
- spec 0028 §6, §7, §9.4 — the bell, the tray and the banner host, which the shell places.

## Goals / Non-goals

**Goals**

- A warm launch renders the whole chrome with no request.
- One derived navigation registry, and one shortcut registry with reserved keys and conflict rules.
- A pinned bar of the user's own pages and one-level folders, with a one-click pin control.
- A launcher that browses sections and finds pages instantly in Arabic and English, by keyboard.
- A session menu, a tenant picker with names in every tenant language, and a signed-out page.
- Seams for the help drawer and the assistant that move nothing when they arrive.

**Non-goals (explicitly out of scope)**

- Routes, the launch redirect, the session, sign-out and the language switch — spec 0024.
- Recents — spec 0025 §13.
- Bell and tray panels, badges, banners and the toast policy — spec 0028.
- The home page — spec 0035.
- Page contexts and page actions — spec 0026 §9.
- Help content and the assistant panel — later specs; their seams are here (§5.1, §7.2).
- Records, settings and help results in global search — later specs, through §9.6's seam.
- Default pins per role or tenant — deferred (Review flag 2).

## 1. Placement and vocabulary

The shell ships in `@tellma/core/shell`. The two registries every page calls, and the few types a
feature or another entry point needs, ship in the `@tellma/core` primary entry point.

Contract blocks are TypeScript and C# sketches: names and shapes are normative; imports,
documentation comments, bodies and cancellation tokens are omitted.

| Entry point | Area | Members |
|---|---|---|
| `@tellma/core` (primary) | Navigation (§3) | `TmNavigation`, `TmNavSection`, `TmNavEntry`, `TmNavItem`, `TmNavSectionView` |
| | Shortcuts (§4) | `TmShortcuts`, `TmShortcut`, `TM_RESERVED_SHORTCUTS`, `TM_BROWSER_KEYS` |
| | Shell seams (§5.1, §7.2, §7.5) | `TmShellSlot`, `TmShellMain`, `TM_SHELL_LAYOUT` |
| | Launcher seam (§9.4, §9.6) | `TM_LAUNCHER_PROVIDERS`, `TmLauncherProvider`, `TmLauncherItem`, `TmLauncherSearchContext`, `tmSearchNormalize` |
| `@tellma/core/shell` | Components and services | `TmTenantlessShell`, `TmTenantPicker`, `TmSignedOutPage`, `TmTenantShell`, `TmAppBar`, `TmPinnedBar`, `TmPins`, `TmLauncher`, `TmSessionMenu` |
| | Deferred chunks | the launcher panel; the About, Shortcuts and Manage pins dialogs |
| `@tellma/core-testing` | Harnesses | `TmTenantShellHarness`, `TmLauncherHarness`, `TmPinnedBarHarness`, `TmSessionMenuHarness`, `TmTenantPickerHarness`, `tmTestShellSlot()` |

- **Loading.** Spec 0024's route table loads both shells with `loadComponent: () =>
  import('@tellma/core/shell')`. `tellma-client build` adds `modulepreload` links for `/shell` and
  `/attention` (spec 0024 §2.6, §9.2).
- **Imports.** `/shell` imports the primary and `/attention`, and reaches `/support` (the
  Diagnostics tab) only through `import()`.
- **Why some members sit in the primary.** Features provide `TM_LAUNCHER_PROVIDERS` without
  importing `/shell`. Spec 0024 calls `TmShellMain`. Specs 0029 §11.3 and 0034 §11 reuse
  `tmSearchNormalize`.
- **Budgets (gzip).** `/shell` eager code 16 KB; the launcher panel 6 KB; the dialogs 6 KB; this
  spec's share of the primary 5 KB.
- **Folders.** `client/projects/core/tellma-core/src/nav/` (primary); `…/tellma-core/shell/` with
  `frame/`, `tenantless/`, `app-bar/`, `pins/`, `launcher/`, `session/` and `dialogs/`.

**Vocabulary**

- **Tenant shell** — the frame of every `/{t}/…` route. **Tenantless shell** — the frame of
  `/tenants` and `/signed-out`.
- **Chrome** — the shell's rows around `main`: the app bar, the banner row and the pinned bar.
- **App bar** — the 40 px first row.
- **Pinned bar** — the 32 px row of pins.
- **Banner row** — the 32 px row between them while a banner shows (spec 0028 §9).
- **Slot** — an app-bar icon button with a badge and a panel (§7.2).
- **Marker** — the Sandbox or Read-only chip beside the session button (§7.4).
- **Pin** — a saved tenant-relative URL in the pinned bar. **Folder** — a named group of pins.
- **Launcher** — the overlay that the Menu button, the search field and `Mod+K` open.
- **Browse state** — the launcher with empty text. **Typed state** — the launcher with text.
- **Entry** — one item of the navigation registry.
- **Section** — a named group of entries, shown as one launcher column.
- **Search-only entry** — an entry without a section.
- **Provider** — a source of launcher results other than pages (§9.6). **Bucket** — one
  provider's group of results in the typed state.
- **Installed mode** — the app runs in its own window, without the browser's buttons (§7.3).
- **Warm and cold launch** — a launch with the tenant's documents already stored, or without them
  (spec 0025 §3.2).
- **In the click's frame** — painted in the same frame as the click or key that caused it, with no
  animation and no wait.
- **`Mod`** — Ctrl on Windows and Linux, ⌘ on Apple (spec 0023 §3.2).
- **Breakpoints** `xs` to `xxl` — spec 0010 §B.1's widths; §7.1 lists them.

## 2. What a distribution writes

The common case writes nothing: entities, pages and coded reports become launcher entries from
their own declarations. A section is one line in the feature, and a synonym list is one resx line.

**Example —** Acme's only navigation code:

```ts
// acme.feature.ts
export const acmeFeature = tmFeature({
  key: 'acme',
  sections: [{ id: 'sales', labelKey: 'Acme_Nav_Sales', icon: 'users', order: 20 }],
  entities: [customers, salesInvoices],                 // each declares section: 'sales' (spec 0029 §3)
  pages: [tmPage('bank-reconciliation', { component: () => import('./bank/bank-page'),
    titleKey: 'Acme_BankReconciliation', section: 'financials' })], // a section tmGlFeature() declares
  reports: [agingReport],                               // declares section: 'sales' (spec 0034 §2.2)
});
```

```xml
<!-- Strings.resx: synonyms, comma-separated, matched but never shown -->
<data name="Acme_Customer_Plural_Synonyms"><value>Clients, Buyers, Debtors</value></data>
<!-- Strings.ar.resx: the same key in Arabic -->
<data name="Acme_Customer_Plural_Synonyms"><value>زبائن، مشترين، مدينون</value></data>
```

Anything beyond that takes one of these:

| Need | Write | § |
|---|---|---|
| A launcher link that is not an entity, page or report | `entries: [{ id, section, labelKey, url: (t) => …, requires? }]` | §3.1 |
| Relabel or reorder a platform section or entry | `overrides: [tmOverride('section:administration', { labelKey: 'Acme_Nav_Admin' })]` (spec 0024 §2.3) | §3.2 |
| Remove a launcher entry and keep its route | `hide: ['entry:core.Role']` (spec 0024 §2.3) | §3.2 |
| An app-bar item | `appBar: [{ id, icon, labelKey, badge?, panel }]`, at most three in all features | §7.2 |
| A launcher bucket (records, help) | `providers: [{ provide: TM_LAUNCHER_PROVIDERS, multi: true, useClass: … }]` on the feature | §9.6 |
| A page shortcut | a `TmPageAction` with `shortcut` (spec 0026 §9.4), or `inject(TmShortcuts).register(…)` | §4 |

## 3. The navigation registry

`TmNavigation` turns the merged declarations into the permission-filtered, ordered sections and
entries that the launcher, pins and recents render. It is derived, so a distribution never lists
its pages twice.

### 3.1 Contract

```ts
interface TmNavSection { id: string; labelKey: string; icon: string; order: number } // order: §3.2's order bands
interface TmNavEntry {
  id: string;                                   // 'core.settings'; a derived entry takes its declaration's key
  section?: string;                             // absent: a search-only entry (§3.2)
  labelKey: string;
  url: (t: number) => string;                   // the canonical path (spec 0024 §9.1): t => `/${t}/settings`
  icon?: string;                                // a registered icon name; default per kind (§3.2)
  order?: number;                               // within the section; default 100; ties by localized label
  synonymsKey?: string;                         // default `${labelKey}_Synonyms` (§3.3)
  requires?: TmSecurableRef | readonly TmSecurableRef[]; // visible when any one passes
  helpKey?: string;                             // reserved for the help drawer (spec 0026 §9.8)
}
interface TmNavItem extends TmNavEntry {        // a resolved, visible entry
  readonly kind: 'entity' | 'page' | 'report' | 'entry';
  readonly href: string;                        // url(routed tenant): '/17/customers'
  readonly entity?: string;                     // kind 'entity': 'acme.Customer'
  readonly createHref: string | null;           // '/17/customers/new' when the user may create (§3.2)
}
interface TmNavSectionView extends TmNavSection { readonly entries: readonly TmNavItem[] }
declare class TmNavigation {                    // primary, root
  readonly sections: Signal<readonly TmNavSectionView[]>; // ordered; sections with no visible entry dropped
  readonly items: Signal<readonly TmNavItem[]>;           // every visible entry, search-only ones included
  byHref(href: string): TmNavItem | undefined;            // the exact canonical path, no query (pins, recents)
}
```

- **Source.** `TmNavigation` reads the merged declarations of `TM_APP_DECLARATIONS`, after `hide`
  applies (spec 0024 §2.2). It resolves them for the routed tenant (`TmUrls.tenantId`). Tenantless
  routes have no items.
- **Recomputed** when the `me` document's access, the language or the tenant changes. Never per
  keystroke.

### 3.2 Where entries come from

| Declaration | Entry id | Label key | URL | Visible when | Default icon | `createHref` |
|---|---|---|---|---|---|---|
| `tmEntity` with a `query` operation | the entity name | `<Schema>_<Entity>_Plural` | `/{t}/{segment}` | `TmPermissions.allows(name, 'query')` | `file-text` | `/{t}/{segment}/new` when `allows(name, 'save')` and the summary lists `details` |
| `tmPage` without parameters | the path | `titleKey` | `/{t}/{path}` | its `securable` passes, or it has none | `square-dashed` | — |
| `tmCodedReport` | the key | `titleKey` | `/{t}/reports/{key}` | its `securable` passes | `chart-column` | — |
| a feature's `entries` | `id` | `labelKey` | `url(t)` | any `requires` passes, or it has none | `link` | — |

- **Members.** `section`, `order`, `icon` and `synonymsKey` sit flat on `tmEntity`, `tmPage`,
  `tmCodedReport` and `entries`. An entry without `section` is search-only. A `section` that names
  no declared section is the error `unknown-section` (spec 0024 Appendix A).
- **Passes** means `TmPermissions.can(resource, action)`. `allows` adds the operation check (spec
  0029 §7).
- **Parameterized pages.** A `tmPage` with path parameters gets no entry, because it has no fixed
  URL.
- **Stored report definitions** never become entries. The Reports entry opens the reports page
  (spec 0034 §11), and pins and recents reach a stored report (Review flag 12).

**The core feature's navigation (binding).** Each declaration's owner sets these values. The two
core sections:

| Section | Label key, English | Order | Icon |
|---|---|---|---|
| `analysis` | `tm.nav.analysis`, "Analysis" | 800 | `chart-column` |
| `administration` | `tm.nav.administration`, "Administration" | 900 | `settings` |

Their entries, each with its `order`, and the core's search-only entries:

| Section | Entries | Declared by |
|---|---|---|
| `analysis` | Reports `core.ReportDefinition` 10 | spec 0034 |
| `analysis` | Dashboards `core.Dashboard` 20 | spec 0035 |
| `administration` | Users 10, Roles 20, Schedules 50, Exports 70, Imports 80, Outgoing emails 90 | spec 0032 |
| `administration` | Centers `gl.Center` 30 | `@tellma/module-gl` |
| `administration` | Settings `core.settings` 40 | this spec |
| `administration` | Jobs 60 | spec 0028 |
| `administration` | Support requests 100 | spec 0026 |
| none: search-only | Notifications, Assignments | spec 0028 |
| none: search-only | My profile `core.me`, Notification settings `core.me.notifications` | this spec |

- **Core entries.** This spec declares three:
  - `core.settings` links `/{t}/settings`. It is visible with `Read` on any
    `core.Settings.<category>` (spec 0032 §7.1).
  - `core.me` links `/{t}/me`, and `core.me.notifications` links `/{t}/me/notifications`. Both are
    visible to every member.
- **Jobs, Exports and Imports** show to every member, because their stacks return a member's own
  rows (Review flag 1).
- **Order bands.** Distribution and pack sections use 1–700 (`financials` 10, Acme's `sales` 20).
  Analysis (800) and Administration (900) therefore always close the launcher.

### 3.3 Labels and synonyms

- **Labels** resolve in the active language at render (spec 0024 §7.1). In development, a missing
  key renders the key itself and warns once.
- **Synonyms** come from the string `synonymsKey ?? labelKey + '_Synonyms'`. The registry splits it
  on `,` and `،` (the Arabic comma), trims each part and drops empty ones. A missing key means no
  synonyms.
- **Synonyms are matched, never shown.** Matching uses them in the active language and in English
  (§9.4).

**Example —** `Acme_Customer_Plural` is "Customers" and "العملاء", with §2's synonyms. In Arabic,
"clients", "زبائن" and "عملاء" all find Customers; in English, "clients" and "buyers" do.

## 4. The shortcut registry

`TmShortcuts` is the one keyboard listener for app-level shortcuts. It owns the reserved keys,
refuses conflicts, and lists every live shortcut for the Shortcuts dialog. Components that own
keys, such as the grid, editors and menus, handle them locally and win.

### 4.1 Contract

```ts
interface TmShortcut {
  readonly id: string;                       // 'tm.launcher.open', 'details.save', 'acme.bank-reconciliation.match-next'
  readonly keys: TmKeys;                     // spec 0023 §3.2's grammar: 'Mod+K', 'Alt+Shift+P', 'F2'
  readonly labelKey: string;                 // the Shortcuts dialog row
  readonly scope: 'global' | 'page';         // the dialog's General or This page section (§11.2)
  readonly inFields?: boolean;               // fires in an editable field; default true with Mod or Ctrl, else false
  readonly overModals?: boolean;             // fires while a modal is open; default false
  run(event: KeyboardEvent): void | false;   // false: not handled, the event continues
}
declare class TmShortcuts {                  // primary, root
  register(shortcut: TmShortcut, destroyRef?: DestroyRef): void; // unregisters on destroy; default: the injection context's
  readonly active: Signal<readonly TmShortcut[]>;                 // the live registrations
}
declare const TM_RESERVED_SHORTCUTS: readonly { readonly id: string; readonly keys: TmKeys; readonly owner: string }[];
declare const TM_BROWSER_KEYS: readonly TmKeys[];                // both listed in Appendix A
```

**Example —** a distribution page binds one key. Most pages instead give a `TmPageAction` a
`shortcut`, which spec 0026 §9.4 registers while that page's context is on top.

```ts
inject(TmShortcuts).register({ id: 'acme.bank-reconciliation.match-next', keys: 'Mod+Shift+M',
  labelKey: 'Acme_BankReconciliation_MatchNext', scope: 'page', run: () => this.matchNext() });
```

### 4.2 Dispatch

1. **One listener** for `keydown` on `document`, in the bubble phase, so a focused grid, editor,
   menu or dialog sees the key first.
2. **Skip.** The registry ignores an event that is `defaultPrevented`, part of a composition
   (`isComposing`, or `key` is `Process`), or a `repeat`. It ignores every key while
   `TmBusyOverlay.active()` (spec 0023 §5.4) or while the session is not `active`.
3. **Match** with `tmKeysMatch` (spec 0023 §3.2). It compares physical keys on Arabic layouts and
   under Option.
4. **Editable targets** are `input`, `textarea`, `select`, `[contenteditable]` and a grid whose cell
   editor is open. In one of them, a shortcut runs only with `inFields`. It never runs when
   `event.key` is a printable character other than its own letter, such as `∏` from Option+Shift+P.
5. **Modals.** While a `TmModal` is open, only `overModals` shortcuts run, plus the page shortcuts
   of the dialog's own context (spec 0026 registers those only while on top).
6. **Run.** Unless `run` returns `false`, the registry calls `preventDefault()` and
   `stopPropagation()`, and emits `tm.shortcut.used { id }`.

**Example —** a details page registers `F2` for Edit. In a grid cell, `F2` opens the cell editor
and the grid prevents the event, so step 2 skips it. On the page title, `F2` enters edit mode.

### 4.3 Reserved keys and conflict rules

The platform and pages draw from disjoint key families, so a page can never take a shell key. The
registry refuses every combination outside these families. Appendix A holds the closed lists.

| Family | Keys | Registered by |
|---|---|---|
| Platform | `Mod+K`, `Mod+/`, every `Alt+Shift+<letter or digit>`, every `Mod+Alt+Shift+<key>` | the `tm.` ids of Appendix A only |
| Page | `Mod+<key>` and `Mod+Shift+<key>` (except `Mod+K` and `Mod+/`), `Mod+Enter`, and the admitted `F2`, `Alt+PageUp`, `Alt+PageDown` | pages, page frameworks, distributions |
| The page's own | every unmodified printable key | never the registry: the page listens itself (type-to-search, spec 0030 §6.2) |
| Components | `Escape`, `Tab`, `Enter`, `Space`, arrows, `Home`, `End`, `PageUp`, `PageDown` without a modifier | the focused component |

- **Page keys in use.** `Mod+S` saves a details page and prevents the browser's "Save page" (spec
  0031 §8). `Alt+PageUp` and `Alt+PageDown` step through records (spec 0031 §12.2).
- **Refused registrations.** The registry refuses:
  - keys outside the caller's family: a `tm.` id not in Appendix A, or any other id outside the
    page family;
  - a `TM_GRID_KEYS` entry with a modifier (spec 0010 §C.12);
  - a `TM_BROWSER_KEYS` entry;
  - a combination that is already live.
- **On refusal,** development throws `TmShortcutConflictError`. Production drops the later
  registration and emits `tm.shortcut.conflict { id }`.
- **Why a modifier.** WCAG 2.1.4 forbids single-character shortcuts without an off switch. `F2` is
  not a character key, and a focused grid wins it (§4.2).
- **Labels.** `tm-kbd` and `tmFormatKeys` render "Ctrl K" on Windows and Linux and "⌘ K" on Apple
  (spec 0023 §3.2). Menus render shortcuts the same way, through `TmMenuItem.shortcut`.
  `tmAriaKeyShortcuts` sets `aria-keyshortcuts` on the launcher input and the pin control.

## 5. The shell frame: layout, landmarks, focus, print

Both shells are a fixed-height CSS grid in which only `main` scrolls. The shell publishes its
chrome height once, as `--tm-shell-top`, so no page computes it.

### 5.1 Layout constants

| Row | Height | Present |
|---|---|---|
| App bar | 40 px | always |
| Banner row | 32 px | while `TmBanners.visible()` (spec 0028 §9.1) |
| Pinned bar | 32 px | in the tenant shell, unless the user hides it (§8.11) |
| `main` | the rest | always; `overflow: auto`, `scrollbar-gutter: stable` |

```css
/* @tellma/core/shell, frame.css: both shells */
.tm-shell { display: grid; block-size: 100dvh;
  grid-template-rows: var(--tm-app-bar-height) var(--tm-banner-height) var(--tm-pinned-bar-height) minmax(0, 1fr);
  grid-template-columns: minmax(0, 1fr) var(--tm-aside-width); }
:root { --tm-app-bar-height: 40px; --tm-banner-height: 0px; --tm-pinned-bar-height: 32px; --tm-aside-width: 0px;
  --tm-shell-top: calc(var(--tm-app-bar-height) + var(--tm-banner-height) + var(--tm-pinned-bar-height));
  --toast-offset-block-start: calc(var(--tm-shell-top) + 8px); }   /* read by the toast host, spec 0023 §5.1 */
```

- **Who sets what.** The shell writes two variables, and pages fill `main` with
  `block-size: 100%`:
  - `--tm-banner-height`: 32 px or 0, from `TmBanners.visible`. Spec 0028 §9.3's idle gate times
    when the banner row enters and leaves.
  - `--tm-pinned-bar-height`: 0 in the tenantless shell and while the bar is hidden.
- **`TM_SHELL_LAYOUT`** `= { appBar: 40, banner: 32, pinnedBar: 32 }` mirrors the heights in
  TypeScript.
- **The aside column** is the help drawer's reserved host. It is empty and 0 wide in this release.
  The help spec opens it 360 px wide from `lg` up, and uses a `TmModal` with `placement: 'end'`
  below `lg` (spec 0010 §D.2).
- **Viewport.** `100dvh` follows the iOS toolbar. In installed mode, `env(safe-area-inset-*)` pads
  the app bar (`viewport-fit=cover`, spec 0024 §4.1).

### 5.2 Landmarks and skip link

Both shells render, in this order:

1. **The skip link** "Skip to content" (`href="#tm-main"`), the first focusable element. It shows
   on focus and focuses `main` (WCAG 2.4.1).
2. **`header`,** holding the app bar, `<tm-banner-host />` (spec 0028 §9.4) and, in the tenant
   shell, `nav aria-label="Pinned pages"`.
3. **`main#tm-main`** with `tabindex="-1"`.
4. **The empty, hidden `aside`.**

- **One `main`, one `header`, no nested landmarks.** The launcher, menus and dialogs render in
  overlays outside them.

### 5.3 Focus rules

| Event | Focus goes to |
|---|---|
| A route path change, including a launcher choice | the page title (spec 0024 §4.4) |
| The launcher opened by a click, by focus on the field, or by `Mod+K` | the launcher input; below `md`, the Menu icon focuses Close instead (§9.1) |
| The launcher closed by Escape or an outside click | the element that had focus when `Mod+K` opened it, else the trigger |
| A menu, popover or dialog closed | its trigger (spec 0005, spec 0010 §C.10) |
| A pin removed from the bar | the next pin, else the previous, else the pin control |
| A banner appears | nowhere; spec 0028 announces it |

### 5.4 Print and reduced motion

- **Print** hides the header and the aside. `main` becomes `overflow: visible`, `block-size: auto`.
- **No shell surface animates.** The launcher, menus, popovers and dialogs appear in the click's
  frame. The bell's job ring belongs to spec 0028 and stops under `prefers-reduced-motion`.

## 6. The tenantless shell, the tenant picker and the signed-out page

The tenantless shell frames the two routes without a tenant. The picker lists the user's
memberships, with names in every tenant language. The signed-out page renders with no request.

### 6.1 The tenantless app bar

| Item, inline start to end | Content | Below `md` |
|---|---|---|
| Wordmark | `brand/wordmark-on-dark.svg`, 14 px tall, `alt` = the display name; a link to `/` | the 18 px mark, `mark-on-dark.svg` |
| Language | a menu button with the active language's native name ("English", "العربية"); radio items of `TmLocalization.languages()`; absent with one language | the `globe` icon; the name joins its accessible name |
| Account | the user's name over the email (12 px) from `TmSession.profile()`; a menu with My account (§10.1 row 3) and Sign out | the 24 px `tm-avatar` with initials |

- **The same ink bar** as the tenant shell (40 px, `--ink-900`), with a spacer after the wordmark.
  Spec 0028's banners show below it.
- **A language change** re-renders in place with no request (spec 0024 §7.2).
- **On `/signed-out`** the bar shows the wordmark only.

### 6.2 The tenant picker (`/tenants`)

- **Data.** The picker reads `TmLaunchCache.tenants` (spec 0025 §3.1), which holds `/bff/user`'s
  memberships (spec 0011 §5.5). `revalidateUser()` refreshes it on entry.
- **No redirect.** The picker never redirects; only `/` does (spec 0024 §9.3).
- **Heading** `h1` "Choose a tenant" (`[tmPageTitle]`); `document.title` "Tenants · {display name}".

A row is one `li` of a `ul`, with these parts:

- **Name,** bold, `dir="auto"`, in the active language. The picker finds the active language's
  position in the tenant's `languages`, by language subtag, and shows `name`, `name2` or `name3` at
  that position. Without a match it shows `name`.
- **Second line:** the other non-empty names, each in a `span` with its own `lang`, joined by " · ",
  then "Tenant {id}".
- **Badges** (`tm-badge`), with their tone: Last used (neutral; `boot.lastTenantId`, spec 0025
  §2.1), Sandbox (accent), Read-only (warning), Suspended (error), Being set up (neutral; for
  `Provisioning`) and Deactivated (neutral; `isActive` is false).
- **Connect:** `<a tmButton href="/{id}">`, a real link named "Connect to {name}". Hover and focus
  call `TmUrls.preload('/{id}')`.
- **Connect, unavailable:** a disabled button described by its reason: "Suspended. Contact your
  administrator.", "This tenant is being set up." or "Your access to this tenant was removed."

**Example —** tenant 1 has `languages` `en`, `ar`, `name` "Acme" and `name2` "أكمي". In Arabic its
row shows "أكمي" in bold, then "Acme · Tenant 1". In English it shows "Acme", then "أكمي · Tenant 1".

- **Order.** Live tenants come first, sorted by name (`Intl.Collator`). Each is followed by its own
  sandboxes (by `liveTenantId`), indented. Standalone sandboxes come last. Recency never moves a
  row, so the layout stays stable.
- **Search** shows from seven memberships (Review flag 14). A `tm-search-field` "Search tenants"
  filters on every keystroke (its `value`, spec 0023 §6.2). It matches the names and the id through
  `tmSearchNormalize`, and keeps the text in `?q=` with `replaceUrl`.
- **While searching,** Enter follows the only enabled match, and a matching sandbox keeps its live
  parent as a dimmed row. `Mod+K` focuses the field, because the picker registers
  `tm.launcher.open` itself.

The picker's states:

| State | What renders |
|---|---|
| A cached list | the list in the first frame, updated by the revalidation; a failed revalidation shows nothing |
| No cached list | four skeleton rows with the final geometry until the read answers |
| The read failed, nothing cached | `tm-error-state` from `TmErrorPresenter.describe(error, { kind: 'read' })` with Retry (spec 0024 §11) |
| No memberships | `tm-empty-state` "You're not a member of any tenant yet" / "Ask your administrator to invite you. You're signed in as {email}." with Sign out |
| No match | "No tenants match “{q}”." |
| `?notice=tenant-not-found&from=17` (spec 0024 §11) | an info `tm-alert` (`role="status"`), below |

- **The not-found notice** reads "You can no longer open {name}." when tenant 17 is in the list.
  Otherwise it reads "You can no longer open tenant 17." Dismissing it drops both parameters.

### 6.3 The signed-out page (`/signed-out`)

- **Content.** `h1` "You signed out", then "You're signed out of {display name} on this device.",
  then a primary "Sign in" button. It calls `TmSession.signIn({ returnUrl: '/' })` (spec 0024 §8.3).
- **With a valid profile cookie,** the page says "You're signed in as {name}." and offers
  "Continue" to `/`.
- **No metadata, no request** (spec 0024 §6). The page uses the bundle's strings, in the boot
  record's language, direction and theme. The display name comes from `index.html`'s `<title>`.
  That is why the page has no language menu.

## 7. The tenant shell and its app bar

The app bar is one 40 px ink row in both themes. Its items collapse by breakpoint in a fixed order,
and every item has a fixed box, so nothing moves when counts, names or states change.

### 7.1 Composition by breakpoint

Items run from inline start to end. Icon buttons are 30 × 30 px. Gaps are 8 px, or 6 px below
`md`.

| Item | `xs`–`sm` (< 768) | `md` (768–991) | `lg` (992–1199) | `xl` (1200–1439) | `xxl` (≥ 1440) |
|---|---|---|---|---|---|
| Wordmark, a link to `/{t}` | the 18 px mark | the 14 px wordmark | wordmark | wordmark | wordmark |
| Menu | icon button | grouped: "Menu" + icon | grouped | grouped | grouped |
| Search | icon button, after the spacer | grouped: icon button | grouped: field ≤ 280 px | field ≤ 440 px, `Ctrl K` keycap | field ≤ 560 px, keycap |
| Back, forward (installed mode) | hidden | 30 px buttons | same | same | same |
| Spacer | | | | | |
| Distribution slots (≤ 3) | session-menu rows | icon buttons | same | same | same |
| Bell, tray (spec 0028) | icon buttons | same | same | same | same |
| `help`, `assistant` (reserved) | zero width | zero width | zero width | zero width | zero width |
| Markers | 24 px icon circles | chips | chips | chips | chips |
| Session button | avatar | avatar + tenant name | same | avatar + user name · tenant name | same |

- **The grouped control** joins Menu and search in one bordered box: 30 px, radius 6, fill
  `rgba(255,255,255,.07)`, hairline `.12`. Focus inside lifts the fill to `.10` and rings the group.
- **Wordmark and mark** are spec 0024 §7.6's brand files, in a link named "{display name} home".
  The wordmark never mirrors.
- **Media queries** use spec 0010 §B.1's values.
- **The session button** is at most 260 px. Names truncate with an ellipsis, the tenant name last,
  because it is the safety signal. The user name has weight 400, the tenant name 600.

### 7.2 Slots

```ts
interface TmShellSlot {
  id: string;                                   // 'acme-approvals'; 'help' and 'assistant' are reserved
  icon: string; labelKey: string;
  badge?: () => Signal<number | null>;          // called once in the shell's injection context
  panel: TmLazyComponent;                       // opened on click
}
```

- **Rendering.** Each slot is an icon button (`tm-icon`, 16 px). Its badge is a `tm-counter`
  overlaid on the icon and haloed in the bar colour (spec 0023 §4.1).
- **ARIA.** The button is named "{label}", or "{label}, {n}" with a count. It carries
  `aria-haspopup="dialog"` and `aria-expanded`.
- **The panel** is a `tm-popover` of size `md` from `lg` up, and a `TmModal` with
  `placement: 'end'` below `lg` (spec 0010 §D.2, §D.3). It loads on first open and preloads on
  hover and focus. It closes on Escape or an outside click, returning focus to the button.
- **Order.** Distribution slots come in feature order, before the bell and tray (spec 0028 §6.1,
  §7.1). §7.1's table gives the full order.
- **Reserved ids.** `help` and `assistant` are registered later, by their own specs, through this
  type. A distribution using either fails with `app-bar-reserved-id`. Until then they take no
  width. Slots come from declarations at boot, so nothing moves the bar later.

**Example —** an approvals slot:

```ts
appBar: [{ id: 'acme-approvals', icon: 'stamp', labelKey: 'Acme_Approvals',
  badge: () => inject(AcmeApprovals).pendingCount, panel: () => import('./approvals/approvals-panel') }],
```

### 7.3 Back and forward in installed mode

- **Detecting installed mode.** The shell uses `matchMedia('(display-mode: standalone),
  (display-mode: fullscreen), (display-mode: window-controls-overlay)')`, or `navigator.standalone`
  on iOS, with a `change` listener. `minimal-ui` keeps the browser's buttons, so it does not count.
- **Buttons** "Back" and "Forward" (`arrow-left`, `arrow-right`, mirrored in RTL). Each is natively
  `disabled` when history cannot move that way.
- **State with the Navigation API.** The buttons read `canGoBack` and `canGoForward`, re-read on
  `currententrychange`. Clicks call `navigation.back()` and `forward()`.
- **State without it** (Safari before 26.2). The shell keeps a depth counter: a push adds 1, and a
  back `popstate` subtracts 1. Back is enabled above 0. Forward is enabled after a Back, until the
  next push. A reload resets the counter (Review flag 15).
- **Query changes replace history** (spec 0030 §4.4), so Back never steps through keystrokes.

### 7.4 The session button and the tenant markers

- **The session button** is a `button[tmMenuTriggerFor]` (spec 0010 §C.10). It shows a 24 px
  `tm-avatar` and the names §7.1 lists for the breakpoint.
- **The avatar** is `TmUrls.blob('user-image', me.user.imageId, { variant: 'thumb' })`, else the
  initials.
- **The button's accessible name** is "Account: {user name}, {tenant name}", plus ", sandbox" or
  ", read-only".
- **The user name** is `me.user`'s name in the active content slot (spec 0024 §7.1), else `name`.
  On a cold launch it is the profile cookie's `name`.
- **The tenant name** is `settings.names` (spec 0013 §5.6's `Names`) at the content slot, else the
  first. On a cold launch it is a 96 px `tm-skeleton` line.

Markers come from `TmLaunchCache.docs.tenant` (spec 0025 §3.1):

| Marker | Shows when | Chip (≥ `md`) | Its popover (280 px) |
|---|---|---|---|
| Sandbox | `category` is `Sandbox` | `flask-conical` + "Sandbox", accent tint | "You're in a sandbox. Changes here don't affect live data." and "Open {live name}" to `/{liveTenantId}` when that tenant is in the user's list |
| Read-only | `state` is `ReadOnly` | `eye` + "Read-only", warning tint | "{tenant} is read-only. You can view data but not change it. Contact your administrator." |

- **Each marker** is a `button` with `aria-haspopup="dialog"`. From `md` up it is a 20 px pill;
  below `md` it is a 24 px icon circle named by its label.
- **Placement.** Both markers may show, beside the session button, never inside it.
- **For a live, active tenant** no marker shows, and the markers take no width.
- **Updates.** Markers change with the next `client/sync` (spec 0025 §5).

### 7.5 Shell states

| State | App bar | Pinned bar | `main` |
|---|---|---|---|
| Warm (spec 0025 §3.2) | complete, from the documents | the pins | the route host |
| Cold, sync pending | names from the cookie, the tenant name as a skeleton, badges reserved, no markers | three 64 px skeleton chips | the route host's skeleton until the sync answers, then the page (spec 0024 §4.4) |
| Cold, sync failed `offline` or `unavailable` | as pending | empty | `tm-error-state` "Couldn't open {tenant name, or 'tenant 17'}" with Retry, which reruns the sync |
| `tenant-unavailable` | as warm | as warm | spec 0024 §11's tenant-state page, through `TmShellMain` |
| `tenant-not-found` | — | — | spec 0024 §11 navigates to the picker with its notice |

```ts
declare class TmShellMain {                     // primary; the tenant shell renders it in `main`
  // what `main` shows instead of the route host; null shows the route host
  readonly override: Signal<{ readonly component: Type<unknown>; readonly inputs?: Readonly<Record<string, unknown>> } | null>;
  replace(component: Type<unknown>, inputs?: Readonly<Record<string, unknown>>): void; // called by spec 0024 §11's presenter
  restore(): void;                              // back to the route host; also runs on the next path change
}
```

**Switching tenants** happens through `/tenants` or a deep link. The shell closes its overlays and
clears the launcher text, and `TmLaunchCache` reads the new tenant's partition.

## 8. The pinned bar and the pins model

The pinned bar is the user's own navigation: the pages and folders they pinned, in their order, one
click away. The shell owns the pin control, so no page writes pinning code.

### 8.1 Anatomy

```text
≥ md:  [Sales invoices] [Customers]̲ [Trial balance] │ [Month end ▾] [··· More]              [📌]
< md:  (Customers) (Sales invoices) (···)                                                 [📌]
```

- **Row.** 32 px, `--ink-800`, `padding-inline` 8, a hairline top border.
- **Pin chip** (from `md` up). 24 px, `padding-inline` 9, radius 6, 12 px `#C6D4D8` text, hover
  fill `.08`. The label truncates at 120 px, with a tooltip.
- **Current page's chip.** Full height, weight 500, teal-300, with a 2 px block-end bar and
  `aria-current="page"`.
- **Folder chip:** its name and `chevron-down`, a menu button (§8.8), after a 1 × 16 px separator.
- **More** (`ellipsis` + "More") shows past §8.7's count.
- **Pin control** at the inline end: a 24 px `pin`, filled on a pinned page. It is named "Pin this
  page" or "Edit pin", with `aria-keyshortcuts` `Alt+Shift+P`.
- **Below `md`:** 24 px pills, the current one teal-tinted and first.
- **Empty:** "Nothing pinned yet. Use the pin button to pin this page."; below `sm`, "Nothing pinned
  yet."

### 8.2 The pins model

```ts
interface TmPinsDoc { readonly v: 1; readonly items: readonly TmPinItem[] }  // the value of nav.pinned
type TmPinItem = TmPin | TmPinFolder;
interface TmPin {
  readonly id: string;                 // 8 base32 characters, generated by the client
  readonly url: string;                // tenant-relative, no leading slash: 'centers?f.CenterType=in:Service'
  readonly title: string;              // the page title when pinned, in that language; ≤ 80
  readonly label?: string;             // the user's own name, ≤ 40; wins over every derived label
}
interface TmPinFolder { readonly id: string; readonly folder: string; readonly items: readonly TmPin[] } // folder: its name, ≤ 40
declare const tmPinsKey: TmPreferenceKey<TmPinsDoc>;          // 'nav.pinned', curated, fallback { v: 1, items: [] }
declare const tmPinnedBarVisibleKey: TmPreferenceKey<boolean>; // 'nav.pinnedBarVisible', curated, fallback true
```

**Example —** a stored value:

```json
{ "v": 1, "items": [
  { "id": "k3j7q2m9", "url": "sales-invoices", "title": "Sales invoices" },
  { "id": "x1ba7hd2", "url": "customers?f.CustomerType=in:Corporate", "title": "Customers", "label": "Corporate customers" },
  { "id": "p0d9wqlm", "folder": "Month end", "items": [
    { "id": "z8rt5nb3", "url": "reports/acme-aging?p.AsOf=2026-09-30", "title": "Aging" },
    { "id": "vj4e2fhd", "url": "reports/42", "title": "Monthly sales by region" } ] } ] }
```

- **Per tenant.** Pins are stored per tenant (spec 0014 §3.4). Tenant-relative URLs fit a live
  tenant and its sandbox alike.
- **What a pin captures:** the routed path after `/{t}/` and its query, minus `page` (spec 0024
  §9.5). Filters, columns, order, `view`, `p.*` and `?child=` stay. For example,
  `/17/customers?f.CustomerType=in:Corporate&page=3` pins as `customers?f.CustomerType=in:Corporate`.
- **Mode.** A pin opens view mode, since edit mode is never in a URL. A `…/new` pin opens create
  mode.
- **Pinnable** is `TmUrls.describe(url).pinnable` (spec 0024 §9.4): every tenant route but the
  drill-through list. Elsewhere the control is disabled: "This page can't be pinned".
- **Caps:** 30 pins, 10 folders, one level, `url` ≤ 512 characters. These keep the value under the
  32 KB value cap, even with worst-case Arabic labels. At the cap, the tooltip is "You've pinned 30
  pages. Unpin one to add another." and a click does nothing.
- **Validation on read** (`validate`, spec 0025 §9.1) checks shape, ids, lengths, caps and the
  one-level rule. A failing item leaves the view but stays stored until the next write. A failing
  value reads as the fallback.
- **No defaults.** A user without the key starts empty. An administrator with `core.User ×
  Preferences` may write it through `users/preferences/set` (spec 0018 §3.5); this spec adds no UI
  for that.

### 8.3 Resolving a pin

Each pin resolves on render from `TmUrls.describe('/' + t + '/' + url)` (spec 0024 §9.4):

| State | When |
|---|---|
| `unavailable` | no route matches: the entity, page or report is gone |
| `no-access` | the route's permission check fails (below) |
| `ok` | otherwise |

- **Permission checks** for `no-access`:
  - a search or details URL: `TmPermissions.allows(entity, 'query')`;
  - a create URL: `allows(entity, 'save')`;
  - a page, coded report or core entry: its `securable` or `requires`.
- **Stored reports and dashboards** count as `ok`. Their page reports a refusal.
- **Label.** A chip shows the pin's `label`. Without one, a URL without a query shows the label of
  the entry `TmNavigation.byHref` finds, in the active language. Otherwise the chip shows `title`.
  Chips show no icons.
- **Current pin.** It is the pin whose path and query equal the routed URL's, minus `page`. Failing
  that, it is the pin with the same path and no query. At most one chip is current.

### 8.4 `TmPins`

```ts
interface TmPinView {
  readonly id: string;
  readonly href: string;                                   // '/17/customers?…'
  readonly folderId: string | null;                        // null: at the top level
  readonly label: Signal<string>;                          // §8.3's label rule
  readonly state: Signal<'ok' | 'no-access' | 'unavailable'>; // §8.3
  readonly current: Signal<boolean>;                       // §8.3's current-pin rule
}
interface TmPinFolderView { readonly id: string; readonly folder: string; readonly items: readonly TmPinView[] }
declare class TmPins {                                     // @tellma/core/shell, root
  readonly items: Signal<readonly (TmPinView | TmPinFolderView)[]>;
  readonly current: Signal<TmPinView | null>;              // the routed page's pin
  readonly canPinCurrent: Signal<boolean>;                 // a pinnable route, under the cap
  readonly visible: Signal<boolean>;                       // nav.pinnedBarVisible
  pinCurrent(): TmPinView;                                 // appends at the top level
  update(id: string, patch: { label?: string | null; folderId?: string | null }): void;
  move(id: string, to: { folderId: string | null; index: number }): void;
  remove(id: string): void;
  setVisible(visible: boolean): void;
  addFolder(name: string): string;                         // returns the new folder's id
  removeFolder(id: string): void;                          // the folder's pins take its place
  replace(doc: TmPinsDoc): void;                           // the Manage pins dialog's Save
}
```

- **Writes.** Every mutation calls `TmPreferences.set(tmPinsKey, next)`. The bar changes in the same
  frame, the curated write goes out at once, and other tabs follow (spec 0025 §9.3).
- **Preloading.** After the first render, the shell calls `TmUrls.preload(href)` for every `ok` pin.

### 8.5 Pinning the current page

1. The user clicks the pin control, or presses `Alt+Shift+P`, on an unpinned page.
2. `TmPins.pinCurrent()` appends the pin; its chip appears and the control fills in the click's frame.
3. A `tm-popover` of size `sm`, titled "Pinned", opens on the control. It holds:
   - a name field, pre-filled and selected;
   - a folder select: "No folder", the folders, and "New folder…", which reveals a name field;
   - Remove and Done.
4. Enter or Done applies the changes. Escape or an outside click keeps the pin as created. Focus
   returns to the control.

- **On a pinned page** the control opens the same popover, titled "Edit pin". `Alt+Shift+P`
  instead unpins at once and announces "Unpinned {label}".
- **When the new chip is not visible** (the bar is hidden, or the pin lands in More), no popover
  opens. Instead of announcing, the shell shows the toast "Pinned {label}." once (spec 0028 §8.1),
  and the toast host announces it.

### 8.6 Keyboard and ARIA

- **One tab stop.** The bar is a `ul role="list"` of links in `nav aria-label="Pinned pages"`, with a
  roving `tabindex`. Tab enters on the current pin, else the first, and leaves to the pin control.
- **Moving.** Left and Right (direction-mapped, spec 0010 §B.3), Home and End.
- **Opening.** Enter follows a link, and Ctrl, ⌘ or middle-click open tabs natively. Enter, Space
  and Down open a folder chip's menu.
- **Announcements** (polite): "Pinned {label}", "Unpinned {label}", "Moved {label} to {folder}",
  "Moved {label}, position {i} of {n}".

### 8.7 Overflow

The bar shows a fixed number of leading items per breakpoint, and the rest go into More. The count
never depends on label widths, hover, focus or the current page, so nothing reflows.

| Width | ≥ `xxl` | `xl` | `lg` | `md` | < `md` |
|---|---|---|---|---|---|
| Items before More | 10 | 8 | 6 | 4 | 2 |

- **Items** are top-level pins and folders, in stored order. Chips of the full 120 px still fit each
  band's count.
- **More** lists the hidden items, then "Manage pins…". Pins are link items, folders are submenus
  (spec 0010 §C.10), and revoked pins are locked, disabled rows.
- **The current pin in More.** The More chip takes the current-page styling, and the pin's item
  gets `aria-current="page"`.

### 8.8 Folders

- **A folder chip** opens a `tm-menu` of its pins, with a lock on revoked ones, then "Manage
  pins…". An empty folder shows "No pages in this folder".
- **One level.** Every control that moves a pin offers one level only.
- **Creating** a folder: "New folder…" in the popover, or New folder in the dialog.
- **Names** are trimmed, 1–40 characters, and unique per user ignoring case. A duplicate shows "A
  folder with this name exists".

### 8.9 Drag to reorder

- **Sources.** Pins and folders in the bar are `tmDragSource`s of kind `tm.pin` (spec 0023 §8.3).
- **Feedback.** A 2 px accent line marks the insertion point. Hovering a folder chip for 500 ms
  opens it, so a pin can drop inside.
- **Drop.** A drop calls `TmPins.move`.
- **The non-drag path** (WCAG 2.5.7) is the Manage pins dialog and the popover's folder select.

### 8.10 The Manage pins dialog

- **Frame.** A `TmModal` of size `md`, full screen below `sm`, titled "Manage pins", deferred.
- **Body.** A `tm-reorder-list` of the top-level items (spec 0023 §8.4) with its grab, move and drop
  keys. A folder row expands to its own `tm-reorder-list` that `accepts` `tm.pin`.
- **Each row:** an icon (the entry's, else the route kind's), the label (`dir="auto"`), Rename (an
  inline field), a "Move to" menu and Remove. Revoked rows show a lock and "No access".
- **Footer:** New folder, Cancel and Save, under a `tm-switch` "Show the pinned bar". The switch
  applies at once.
- **Save and Cancel.** The dialog edits a copy. Save calls `TmPins.replace(copy)`, overwriting any
  change made meanwhile. Cancel and Escape discard the copy.

### 8.11 Hiding the bar

- **Effect.** When `nav.pinnedBarVisible` is false, the shell sets `--tm-pinned-bar-height` to 0
  and removes the `nav`, so `main` grows by 32 px.
- **Toggles.** The session menu (§10.1) and the Manage pins dialog toggle it.
- **Without the bar,** the launcher's browse state starts with a Pinned band (§9.3), and
  `Alt+Shift+P` still pins.

### 8.12 Revoked and unavailable pins

- **`no-access`.** The chip is dimmed with a `lock` and stays focusable. Activating it opens a menu
  with "You no longer have access to this page." and Unpin. It is never a link, so it never leads
  to a 403.
- **`unavailable`.** The same, with "This page no longer exists."
- **Never removed automatically.** A role edit can restore access.

## 9. The launcher and global search

The launcher is one overlay with two states. The browse state shows recent pages and every
section. The typed state lists matching pages at once, with provider buckets below them. The
launcher never waits on the network for pages, never navigates on its own, and keeps its text.

### 9.1 Surfaces

| Width | Opened from | Surface | Its input |
|---|---|---|---|
| ≥ `lg` | the Menu button, the app-bar field (focus or click), `Mod+K` | an overlay panel below the shell rows, full width | the app-bar field |
| `md` | the Menu button, the search icon, `Mod+K` | the same overlay panel | a field at the top of the panel |
| < `md` | the Menu icon, the search icon, `Mod+K` | a `TmModal` of size `lg`, full screen below `sm`, labelled "Menu" | a 44 px field in its header, beside Close |

- **The overlay panel** is a `popover="manual"` element at `inset-block-start: var(--tm-shell-top)`,
  inline 0 to 0, with `max-block-size: calc(100dvh - var(--tm-shell-top) - 24px)`. Its body
  scrolls.
- **The scrim** (`rgba(0,23,34,.34)`) covers only the page, so a pin click still navigates.
- **Below `md`** the Menu icon focuses Close, so no on-screen keyboard appears. The search icon
  focuses the field.
- **Escape.** Opening pushes `tmPushEscapeDismissal` (spec 0010 §D.1).

### 9.2 State and transitions

```ts
declare class TmLauncher {                    // @tellma/core/shell, root
  readonly state: Signal<'closed' | 'browse' | 'typed'>;
  readonly query: Signal<string>;             // kept across close and navigation in this tab
  show(via: 'menu' | 'field' | 'shortcut' | 'icon'): void;      // via: what opened it, as in tm.launcher.opened
  close(reason: 'escape' | 'outside' | 'tab' | 'chosen' | 'route'): void;
  setQuery(text: string): void;
}
```

| Event | With empty text | With text |
|---|---|---|
| Menu click, field focus or click, `Mod+K` | opens in browse; focuses the input | opens in typed; selects all the text; shows the results |
| Typing; clearing the text | browse → typed; results update on every keystroke | typed → browse when cleared |
| Menu click while open | closes | closes |
| `Mod+K` while open | focuses the input and selects the text | same |
| Escape, an outside click, Tab | closes; the text stays | same |
| An option chosen | navigates (§9.5) and closes; the text stays | same |
| A route change, a tenant switch, a dialog opening | closes; a tenant switch also clears the text | same |

**The kept text** is selected on the next open, so typing replaces it and Enter repeats the last
choice (Review flag 10). It never reaches storage or telemetry.

### 9.3 Browse state

1. **Pinned band**, only below `md` or while the bar is hidden. It shows the pins as chips, then the
   options "Pin this page" (or "Edit pin") and "Manage pins…".
2. **Recently visited band:** up to ten chips from `TmRecents.pages` (spec 0025 §13), if any.
   Each shows its `icon`, else spec 0035 §9.2's icon for its kind. Typed rows use the same icon.
3. **Sections** as CSS columns (`break-inside: avoid`): one column below `md`, three at `md`–`lg`,
   four from `xl` up.
   - A heading shows the icon (14 px) and the label (12 px, weight 600, underlined).
   - Entries are 13 px links, at least 22 px tall (44 px below `sm`).
   - A pinned entry shows a `pin` glyph and has "pinned" in its accessible name.
   - The current page's entry shows selected (teal tint) without being the active option.
4. **Footer** (from `md` up, `aria-hidden`): "Type to search · ↑↓ to move · Enter to open · Esc to
   close".

### 9.4 Typed state: matching and ranking

The typed state matches the query against an index of normalized terms, then ranks the matches.
Both the query and the terms go through `tmSearchNormalize`.

**`tmSearchNormalize(text): string`**:

1. NFKD; remove combining marks (U+0300–U+036F) and Arabic marks (U+064B–U+065F, U+0670,
   U+06D6–U+06ED); `toLowerCase`.
2. Arabic letters: remove tatweel (U+0640); أ إ آ ٱ → ا; ى ی ئ → ي; ة → ه; ؤ → و; ک → ك.
3. Arabic-Indic and Extended Arabic-Indic digits → 0–9.
4. Punctuation and symbols → space; collapse spaces; trim.

**Example —** `tmSearchNormalize('Café — الفاتورة ١٢')` is `'cafe الفاتوره 12'`.

| Candidate | Terms, normalized when the index builds |
|---|---|
| every `TmNavItem` | its label and synonyms, in the active language and in English |
| every `ok` pin whose URL is no entry's `href` (a filtered pin always counts) | its `label` and `title` |
| every recent page whose URL is no entry's and no pin's | its title |

- **English terms** are read through Transloco's `translate(key, {}, 'en')`. English loads at boot
  (spec 0024 §7.3).
- **Word variants.** An Arabic word of four or more letters starting with "ال" is also indexed
  without it, so "عملاء" finds "العملاء".
- **Match classes.** Matching uses at most five query tokens. A term matches in one of two classes,
  and a candidate's best term decides its class:
  - **prefix** — every token starts a word of the term, in any order ("cust", "inv sal");
  - **contains** — every token of two or more characters occurs in the term.
- **Order.** Prefix matches come before contains matches. Within a class come entries first, by
  section order (search-only entries last), entry order and label. Then come pins, then recent
  pages by label. At most 50 rows show; the first is active, so Enter opens it.
- **Create rows.** A prefix-class entity row with a `createHref` is followed by "{singular label} ·
  Create" (`plus` icon). At most three create rows show per query.
- **A row** is 28 px: an icon, the label, and a muted description (the section, "Pinned" or
  "Recently visited"). Prefix-matched words in the label sit in `<mark>`, built as DOM nodes, never
  through `innerHTML`.
- **No row** shows "Nothing matched “{q}”." under the Pages heading.

**Example —** Farah works in Arabic and has the pin "Invoices to chase":

| She types | Rows, in order |
|---|---|
| `inv` | فواتير المبيعات (Sales invoices: prefix on the English label); فاتورة مبيعات · إنشاء (its create row); Invoices to chase (her pin) |
| `فوات` | فواتير المبيعات (prefix on the Arabic label); its create row |
| `voice` | فواتير المبيعات (contains, on the English label) |
| `زبائن` | العملاء (Customers: prefix on an Arabic synonym); its create row |

### 9.5 Keyboard and ARIA

- **The APG combobox** (the ARIA Authoring Practices Guide pattern). The input keeps DOM focus.
  It carries `role="combobox"`, `aria-expanded`, `aria-controls`, `aria-autocomplete="list"`,
  `aria-activedescendant` and `aria-keyshortcuts`. Its `aria-describedby` points at hidden
  instructions.
- **The listbox.** One `listbox` holds a `group` per band, section and bucket, each labelled by its
  heading. Rows are `<a role="option" href>`; the active one has `aria-selected`.

| Key | Browse | Typed |
|---|---|---|
| Down, Up | the next or previous option in reading order; from none, Down is the first and Up the last | the same over the rows |
| Right, Left (direction-mapped; empty input only) | in a band, the next or previous chip; in sections, the same index in the next or previous section, clamped | move the caret |
| PageDown, PageUp | the first option of the next or previous group | the same |
| Home, End | the first or last option | move the caret |
| Enter | opens the active option here (`Router.navigateByUrl`); a command option runs | same |
| Mod+Enter, Shift+Enter | opens it in a new tab (`window.open(href, '_blank', 'noopener')`) | same |
| Escape | closes, keeping the text | same |
| Tab | closes | same |

- **Pointer.** Hover activates a row. A click navigates, and Ctrl, ⌘ or middle-click open new tabs.
- **Announcements** are polite, 500 ms after the last keystroke: "{n} pages", "Nothing matched",
  and "{bucket}: {n} results" per provider answer.
- **RTL.** The panel mirrors.

### 9.6 The provider seam

```ts
interface TmLauncherItem {                                // one result row
  readonly id: string;                                    // unique per provider
  readonly icon?: string;
  readonly href: string;                                  // canonical (TmUrls)
  readonly label: string;                                 // localized: 'INV-2026-0431'
  readonly description?: string;                          // localized: 'Rawabi Trading · AED 12,400'
}
interface TmLauncherSearchContext {
  readonly tenantId: number;
  readonly language: string;
  readonly limit: number;                                 // 5
  readonly signal: AbortSignal;                           // aborted by the next keystroke, close or navigation
}
interface TmLauncherProvider {
  readonly id: string;                                    // 'records', 'settings', 'help': kebab-case
  readonly labelKey: string;                              // the bucket heading
  readonly order: number;                                 // > 0; Pages is 0
  readonly minLength?: number;                            // normalized characters; default 2
  search(query: string, context: TmLauncherSearchContext): Promise<readonly TmLauncherItem[]>;
}
declare const TM_LAUNCHER_PROVIDERS: InjectionToken<readonly TmLauncherProvider[]>; // multi, primary
```

- **Timing.** Pages recompute on every input event (`TmSearch.value`). Providers run on the
  debounced `search` output (spec 0023 §6.2), and each run is aborted when superseded.
- **Placement.** Buckets append below Pages, in `order`, as answers arrive, so the active row never
  moves. A search that returns nothing or fails renders no bucket.
- **No provider ships here.** Later specs register providers (Review flag 7).

### 9.7 Caching

The launcher keeps no result cache. The text survives (§9.2), and page results recompute from it in
under 2 ms for 300 entries. Provider results are not cached in this release.

### 9.8 Loading

- **The panel and the index** sit in a deferred chunk prefetched at idle, so the first open renders
  in the click's frame. A user who opens it before the chunk arrives sees a skeleton with the final
  geometry.
- **The index** builds on first open (≤ 5 ms for 300 entries). It rebuilds when the registry, the
  language or the pins change.
- **Preloading.** Hovering an option, or making it active, calls `TmUrls.preload(href)`.

## 10. The session menu

The session button opens a `tm-menu` (spec 0010 §C.10), 268 px wide, anchored at the inline end. It
holds who and where the user is, the culture and view switches, help about the app, and sign out.

### 10.1 Rows

| # | Row | Icon | Entry kind (spec 0010 §C.10) | `detail` | Does | Shown when |
|---|---|---|---|---|---|---|
| — | avatar (28 px), name, email | — | `tmMenuHeader` | — | — | always |
| 1 | the tenant card (§10.2) | `building-2` | item with `description` | "Switch" | navigates to `/tenants` | always |
| 2 | My profile | `id-card` | link with `action` | — | `/{t}/me` (spec 0032 §8) | always |
| 3 | My account | `user-round` | link, `target: '_blank'` | — | the identity account page (below) | the distribution document is cached |
| 4 | Language | `globe` | submenu of radio items with `lang` | the native name | `TmLocalization.setLanguage(code)` | two or more `languages()` |
| 5 | Calendar | `calendar-days` | submenu of radio items | the calendar's name | `TmLocalization.setCalendar(code)` | two or more `calendars()` |
| 6 | Theme: System, Light, Dark | — | a group of three radio items, `keepOpen` | — | `TmTheme.set` (spec 0010 §B.2) | always |
| 7 | Show pinned bar | `pin` | checkbox item, `keepOpen` | — | `TmPins.setVisible` | always |
| 8 | Keyboard shortcuts | `keyboard` | item, `shortcut: 'Mod+/'` | — | the Shortcuts dialog (§11.2) | always |
| 9 | About | `info` | item | `TmAppVersion.display` ("1.4.0") | the About dialog (§11.1) | always |
| 10 | Report a problem | `life-buoy` | item | — | `TmSupport.open()` (spec 0026 §12.1) | always |
| — | each distribution slot | its icon | item | its badge | opens its panel as a drawer | below `sm` |
| 11 | Sign out, after a separator | `log-out`, mirrored | item | — | `TmSession.signOut()` (§10.3) | always |

- **The My account link** is `identity.accountUrl` + `&return_url=` + the current URL, encoded, +
  `&ui_locales=` + the language (spec 0010 §A.9).
- **Names.** Languages show their native names (spec 0024 §7.1). Calendars show
  `tm.calendar.<code>` ("Gregorian", "Umm al-Qura", "Ethiopian") or a distribution calendar's
  `labelKey` (spec 0024 §7.4).
- **In place.** Choosing a language or calendar closes the menu and re-renders the page. The only
  request is the `users/me/locale` write (§12.1).
- **The theme** is a device setting (spec 0024 §7.6). Choosing one keeps the menu open.

### 10.2 The tenant card

- **Label:** the tenant name (§7.4).
- **Description:** "Tenant {id}", then " · Sandbox" and " · Read-only" when they apply. For
  example, "Tenant 2 · Sandbox".
- **Accessible name:** "{tenant name}, Switch tenant".
- **Activation** clears nothing, so returning to the tenant is a warm launch.

### 10.3 Sign out

`TmSession.signOut()` runs spec 0024 §8.4: confirmation, broadcast, overlay, failure toast, redirect.

### 10.4 Keyboard, ARIA, responsive

- **Keys** are spec 0010 §C.10's. Enter, Space and Down open the menu on the first item. Escape and
  an activation return focus to the trigger.
- **Submenus** open toward the inline start, away from the screen's edge.
- **Below `sm`** the menu is full width minus 8 px gutters, with 44 px rows (spec 0010 §C.10).

## 11. The About and Shortcuts dialogs

Both are `TmModal` dialogs of size `md`, full screen below `sm`, in deferred chunks. They open in
the click's frame, with a placeholder in the final geometry until the chunk loads. They read only
local data, so they work offline.

### 11.1 About

Two tabs (`tm-tab-group`, spec 0005): About and Diagnostics. The About tab, top to bottom:

1. **Wordmark**, 20 px: `brand/wordmark-on-light.svg`, or `-on-dark` in the dark theme.
2. **Description:** the server key `Distribution_About` when `TmServerText.has` it (spec 0024
   §7.5). Otherwise Core's new server key `Tellma_About`, which reads "{display name} is built on
   Tellma, an open platform for enterprise resource planning."
3. **Versions**, a `dl[tmDescriptionList]` (spec 0023 §7.1) of LTR-isolated mono values:

| Term | Value |
|---|---|
| {display name} | `TmAppVersion.label` ("1.4.0 (3f9c2ab)", spec 0026 §7), with a `tm-copy-button` copying `ui` |
| Server | `TmAppVersion.server()`, else the cached `distributionVersion` (spec 0011 §5.9), else "—" |
| Tellma platform | `TmAppVersion.platform` |
| each feature but `core` | its `TmAppVersion.features[].version`, the term being its key (`gl`, `acme`) |

4. **Server differs:** when `TmAppVersion.serverDiffers()`, an info `tm-alert` "The server runs
   {server}. Reload to update." with Reload (`TmUpdates.reload()`, spec 0024 §5.5).
5. **Copyright** (`distribution.copyright`, spec 0024 §3.1), then the links "Terms of service" and
   "Privacy" (§12.2), which open in new tabs. Each is omitted when absent, and nothing is awaited.

The Diagnostics tab hosts spec 0026 §8.4's `tm-diagnostics`, imported on first selection.

### 11.2 Shortcuts

The dialog is titled "Keyboard shortcuts". A `tm-search-field` "Filter shortcuts" filters the rows
through `tmSearchNormalize`. Each section is a two-column table (description, `tm-kbd` keys) with
`th` headers. The dialog itself registers no shortcut.

1. **General** — the `global` registrations of `TmShortcuts.active`.
2. **This page** — the `page` registrations at opening, including page actions with a `shortcut`
   (spec 0026 §9.4). Hidden when empty.
3. **Lists and grids** — `TM_GRID_KEYS` (spec 0010 §C.12). Entries that share a label key form one
   row.
4. **Launcher** — Up and Down, Enter, Mod+Enter, Escape (§9.5).

## 12. Server additions

The shell uses one server action and one distribution-info member, both defined by their owners.

### 12.1 `users/me/locale`

The session menu's language and calendar rows (§10.1 rows 4 and 5) call spec 0024 §7.2's switch.
The switch writes `users/me/locale` (spec 0018 §3.5) and adopts the result (spec 0025 §5.3). If a
My profile form was open before the switch, saving it shows the concurrency dialog (spec 0032 §8)
instead of restoring the old language.

### 12.2 `DistributionInfo.links`

The About dialog reads `links.terms` and `links.privacy` of `DistributionInfo` (spec 0011 §5.9)
from `TmLaunchCache.distribution` (spec 0025 §3.1), so legal pages move without a rebuild.

## 13. Testing

Every automated suite runs on each PR; the manual checks run once per release.

- **`TmNavigation`** (unit): §3.2's table; section versus search-only; any-of `requires`; order
  bands; `hide` and `tmOverride`; the language re-sort; `byHref`.
- **Matching** (unit): `tmSearchNormalize` vectors (Latin diacritics, Arabic letters, tashkeel,
  tatweel, digits); the "ال" variant; both classes and the order rule; synonyms split on `,` and
  `،`; create rows; the 50-row cap; a 300-entry benchmark (index ≤ 5 ms, query ≤ 2 ms).
- **`TmShortcuts`** (unit): §4.2's steps (bubble phase, `defaultPrevented`, composition, repeat,
  editable targets, Option-typed characters, modals); every refusal of §4.3; labels.
- **`TmPins`** (unit): validation and caps; the capture minus `page`; the current-pin rule; §8.3's
  states; each mutation's document; the fallback.
- **Harnesses** (harness):
  - `TmTenantShellHarness`: each §7.5 state;
  - `TmLauncherHarness`: §9.2's table, §9.5's keys, bucket placement;
  - `TmPinnedBarHarness`: roving focus, §8.7's counts, folders, revoked pins;
  - `TmSessionMenuHarness`: the rows per condition;
  - `TmTenantPickerHarness`: order, names, badges, search threshold, notice, empty, error.
- **Playwright** (e2e, on the Acme host, in `en` and `ar`):
  - launch: a cold launch to the picker with tenants 1 and 2, then Connect; a warm shell with every
    request held; the signed-out page;
  - launcher: `Mod+K` from a grid cell and from a text field; arrows across sections in LTR and
    RTL; "clients" in Arabic finds Customers; Escape keeps the text;
  - pins: pinning a filtered search into a folder, then a reload; a revoked pin after a role change;
    pinning from the launcher band with the bar hidden; a keyboard reorder in Manage pins;
  - menu and dialogs: a language switch (one `me/locale` write, no `client/sync`); About offline;
  - installed mode: back and forward (Chromium `--app`).
- **Responsive** (e2e): the app bar, pinned bar, launcher and session menu at 390, 576, 768, 992,
  1200 and 1440 px, §7.1 cell by cell.
- **axe** (e2e): light and dark, LTR and RTL, with the launcher, the session menu, a marker popover,
  the pin popover and each dialog open.
- **Manual** (per release): back and forward in an iOS home-screen app; `Mod+K` beating the
  browser's binding in Chrome, Edge, Firefox and Safari.

## 14. Definition of done

- **Built and gated.** `/shell` and the primary members pass their API goldens and §1's budgets,
  which sit within spec 0024 §12's. Every §13 suite passes on Ubuntu and Windows.
- **Instant warm launch.** The chrome renders from the store, the cookie and the bundle. No shell
  code awaits `TmApi`. Pinned by `shell-warm.spec`: no request before the first shell paint.
- **Instant feedback.** Overlays, a new pin and a theme change appear in the click's frame.
  Pinned by `shell-feedback.spec` (a frame probe per click or key) and `launcher-perf.spec`.
- **Non-blocking.** Page results are synchronous, providers abort when superseded, and writes never
  block. Pinned by `launcher-hang.spec`: a hung provider delays nothing.
- **Stable layout.** Fixed boxes, overlaid counters, zero-width reserved slots, overflow by
  count, buckets below the active row. Pinned by `shell-layout.spec` (boxes unchanged across a badge
  from 9 to 10, a banner, a pin into More and a provider answer) and CLS 0 on a warm launch.
- **RTL.** Logical properties, an unmirrored wordmark, direction-mapped keys, Arabic
  normalization, `lang` on tenant names. Pinned by every shell e2e running in `ar`.
- **WCAG AA.** Landmarks, a skip link, one tab stop per bar, the APG combobox and menu patterns,
  no character-key shortcut, a non-drag path for every drag. Pinned by axe and `keyboard-shell.spec`.
- **Page context** (spec 0026 §9.3):
  - tenant pages take their contexts from their page frameworks;
  - the picker registers a `custom` context: `/tenants`, "Choose a tenant", state
    `{ count, query }`, and `visible` Name and Id over at most 200 rows;
  - the signed-out page registers a `custom` context for `/signed-out`;
  - `TmNavigation.items` is the assistant's map of reachable pages.
- **Telemetry** uses closed properties, and never query text, labels or URLs:

| Event | Properties | Sampled |
|---|---|---|
| `tm.launcher.opened` | `via: menu \| field \| shortcut \| icon`, `state: browse \| typed` | yes |
| `tm.launcher.chosen` | `bucket: pages \| create \| pinned \| recents \| <provider id>`, `position: 1 \| 2-3 \| 4-10 \| 11+`, `queryLength: 0 \| 1-2 \| 3-5 \| 6+`, `newTab` | yes |
| `tm.launcher.no-results` | `queryLength` | yes |
| `tm.shell.navigated` | `source: pin \| launcher \| recent \| wordmark \| back \| forward` | yes |
| `tm.shell.pins-changed` | `op: add \| remove \| move \| rename \| folder \| replace`, `via: control \| shortcut \| popover \| drag \| menu \| dialog`, `count: 0 \| 1-5 \| 6-15 \| 16+` | no |
| `tm.shell.pinned-bar-toggled` | `visible` | no |
| `tm.shell.picker-shown` | `count: 0 \| 1 \| 2-6 \| 7+`, `notice` | no |
| `tm.shortcut.used`; `tm.shortcut.conflict` | `id`, a code constant | yes; no |

- **The reference distribution.** Acme adds the `sales` section (customers, sales invoices, the
  aging report), §2's synonyms, and the `bank-reconciliation` page under `financials`. Its tenant 2,
  a sandbox of tenant 1, shows the marker and the picker's grouping. `tmTestShellSlot()` covers
  slots.

## Decisions record

1. **Shells load from `/shell` by `import()`, preloaded** — the primary may not import them
   statically, and the preload keeps the cold path parallel — (§1).
2. **Every entity, page and coded report gets an entry, with flat launcher members and synonyms by
   key convention; `section` decides only whether an entry shows in the browse state** — one rule,
   one spelling, one resx line per synonym list; personal pages stay findable — (§3.2, §3.3).
3. **Two navigation levels** — the launcher shows flat sections of up to 30 entries — (§3.1).
4. **Client-side prefix-then-contains matching over the active language and English** — instant
   and offline. Rejected: server search (a request per keystroke); a ranked engine over every
   language (cost without a measured need) — (§9.4).
5. **One APG combobox, focus kept in the input** — one focus point for typing, arrows and Enter.
   Rejected: a `nav` of links (two keyboard models) — (§9.5).
6. **The text survives; results recompute from it** — repeating a navigation is `Mod+K`, then
   Enter; at 2 ms a query, no cache is needed — (§9.2, §9.7).
7. **A separate Create row; provider buckets below Pages** — an option holds one control, and the
   active row never moves — (§9.4, §9.6).
8. **Pins are tenant-relative URLs with the query, minus `page`; `TmUrls.describe` decides
   pinnability** — any deep link pins with its filters, in a tenant and its sandbox — (§8.2).
9. **One-click pinning, then rename and folder** — instant feedback. Rejected: a dialog first —
   (§8.5).
10. **30 pins, 10 folders, one level; no default pins** — the caps fit the 32 KB value cap; defaults
    would need a settings editor for one convenience — (§8.2).
11. **Overflow by count per breakpoint; one tab stop for the bar** — no measuring and no reflow; ten
    pins are not ten tab stops — (§8.6, §8.7).
12. **Disjoint key families, a modifier on every key but `F2`, `Mod+/` for Shortcuts, bubble-phase
    dispatch** — no page takes a shell key; WCAG 2.1.4; a focused grid wins. `Alt+Shift` collides
    only with Firefox access keys, which Tellma never sets — (§4.2, §4.3).
13. **Slot order: distribution slots, bell, tray, `help`, `assistant`, markers, session button** —
    the reserved slots sit between fixed boxes, after the attention icons, so they arrive without
    moving those — (§7.2).
14. **Markers beside the session button** — a button inside a button is invalid — (§7.4).
15. **Stock `tm-menu` entries, the theme as radio items** — one menu engine and one Escape stack —
    (§10).
16. **Back and forward in installed mode only, with a counter fallback** — the browser has its own
    buttons elsewhere, and the Navigation API is Baseline only from 2026 — (§7.3).
17. **"Tenant {id}" as the card's sub-line; no language menu when signed out** — the core has no
    currency or location, and no metadata loads on `/signed-out` — (§10.2, §6.3).
18. **User help, its format and any merge with the assistant are deferred; the seams ship now** —
    adjacent `help` and `assistant` slots (a later merge is a one-slot change), the aside, `helpKey`,
    `Alt+Shift+H`, `Alt+Shift+I`, the provider seam. Rejected: a help button without content (dead
    pixels); merging into the assistant slot now (prejudges the merge) — (§5.1, §7.2, §9.6).

## Review flags

1. **No personal section** (§3.2). Jobs, Exports and Imports sit under Administration for every
   member; Notifications, Assignments and My profile are search-only. Alternative: a `personal`
   section. Flips if a two-section launcher is preferred for non-administrators.
2. **No default pins** (§8.2). Alternative: a `navigation.defaultPins` setting, used while
   `nav.pinned` is absent. Flips if administrators of less technical users ask for it.
3. **30 pins, 10 folders** (§8.2). Alternative: 50, with shorter labels. Flips if users hit the cap.
4. **Overflow by count** (§8.7). Alternative: measured overflow. Flips if long Arabic labels truncate
   while the row has room.
5. **`Mod+/`, not `?`** (§4.3). Alternative: `?` with a single-key switch. Flips if users bring a
   strong `?` habit from other products.
6. **The `Alt+Shift` family** (§4.3). Alternative: per-key review. Flips if a market's input-method
   switcher uses `Alt+Shift+<letter>`.
7. **No Actions bucket** (§9.6). Alternative: a ~1 KB provider over the top page's actions (origin
   `launcher`, spec 0026 §9.4). Flips if page commands are wanted in search now.
8. **Two levels** (§3.1). Alternative: an optional `group` on entries. Flips past ~30 entries.
9. **"Tenant {id}"** (§10.2). Alternative: the tenant's currency and location via a feature hook.
   Flips if that sub-line is wanted.
10. **The text survives navigation** (§9.2). Alternative: clear it after a choice. Flips if users
    find leftover text confusing.
11. **Eleven menu rows** (§10.1), including My profile, Show pinned bar and Report a problem.
    Alternative: the header as the profile link, and the toggle only in Manage pins. Flips if the
    menu feels long.
12. **Stored report definitions are never entries** (§3.2). Alternative: cached entries under a
    `reports` tag. Flips if users want published reports in sections.
13. **Markers beside the session button** (§7.4). Alternative: inside it, non-interactive. Flips on
    design review.
14. **Picker search from seven memberships** (§6.2). Alternative: always show it. Flips on user
    requests.
15. **A history counter without the Navigation API** (§7.3). Alternative: hide the buttons. Flips if
    it misleads on iOS before 26.2.
16. **Slots in the session menu below `sm`** (§7.1). Alternative: keep one in the bar. Flips if a
    slot is used constantly on phones.
17. **The active language and English only** (§9.4). Alternative: every offered language. Flips if
    users of three-language tenants search in a language they are not using.

## Appendix A — Reserved and browser keys

`TM_RESERVED_SHORTCUTS`, the platform family's registrations:

| Id | Keys (Apple) | Does | In fields | Over modals | Owner |
|---|---|---|---|---|---|
| `tm.launcher.open` | `Mod+K` (⌘K) | opens the launcher, selecting its text (§9.2); on `/tenants`, focuses the search | yes | no | this spec |
| `tm.shortcuts.open` | `Mod+/` (⌘/) | opens the Shortcuts dialog | yes | no | this spec |
| `tm.pins.toggle` | `Alt+Shift+P` (⌥⇧P) | pins or unpins the current page (§8.5) | no | no | this spec |
| `tm.toasts.focus` | `Alt+Shift+T` (⌥⇧T) | `TmToasts.focus()` (spec 0023 §5.1) | yes | yes | this spec |
| `tm.attention.notifications` | `Alt+Shift+N` | toggles the bell panel | yes | no | spec 0028 §6.1 |
| `tm.attention.assignments` | `Alt+Shift+A` | toggles the tray | yes | no | spec 0028 §7.1 |
| `tm.help.open` | `Alt+Shift+H` | reserved: the help drawer | — | — | a later spec |
| `tm.assistant.open` | `Alt+Shift+I` | reserved: the assistant | — | — | a later spec |
| `tm.support.copy-diagnostics` | `Mod+Alt+Shift+D` | copies diagnostics (spec 0026 §8.3) | yes | yes | spec 0026 |

`TM_BROWSER_KEYS`, combinations a page cannot take from the browser: `Mod+T`, `Mod+W`, `Mod+N`,
`Mod+Shift+N`, `Mod+Shift+T`, `Mod+Shift+W`, `Mod+Tab`, `Mod+L`, `Mod+R`, `Mod+Q`, `Mod+H`, `Mod+M`,
`Mod+P`, `Mod+Plus`, `Mod+Minus`, `Mod+0`.

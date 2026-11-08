# ERP Shell Design — Market Analysis and Recommendation

**Purpose:** Settle the layout and behaviour of the Tellma ERP v2.0 application shell — the persistent
chrome that carries navigation, pinned pages, global search, the inbox, session identity, and quick
settings — by first establishing what the widely-used ERP and business-software products actually do,
then recommending a design with an explicit confidence level on every decision.

**Status:** Research and recommendation. Not a spec. The recommendations here are inputs to a future
shell spec; where they conflict with a later spec, the spec wins.

**Method.** Each product's shell was examined through vendor documentation, design-system guidance,
and release notes, supplemented by published UX research. Where a claim could be checked directly
against a product's shipping source, it was — see [§1.5](#15-a-correction-to-a-widely-repeated-claim)
for one case where the secondary literature is simply wrong. Cells in the comparison table that rest
only on secondary sources are marked *(2°)*.

**How confidence is marked.** Every decision in Part 2 carries one of:

| Marker | Meaning |
|---|---|
| **Firm** | Well supported by evidence and by the "stay close to the old shell" tie-break. Changing it should require new information, not a preference. |
| **Leaning** | The better of two defensible options. Worth revisiting if implementation or usage data pushes back. |
| **Open** | A genuine coin-flip, or something that needs a usability test or telemetry to settle. Listed in [Part 4](#part-4--open-questions). |

---

## Part 1 — Market analysis

### 1.1 The field at a glance

Eighteen products, chosen for market weight, for a visible investment in end-user experience, or for
being the reference class Tellma's own shell borrowed from.

| Product | Permanent chrome | Where the full menu lives | Menu depth | User shortcuts | Recents | Global search | Home page |
|---|---|---|---|---|---|---|---|
| **Odoo 19** | Top bar only | Apps dropdown in the bar; Enterprise adds a full-page launcher | App → section → nested section → item | None built in (large third-party market) | No | `Ctrl+K` palette with `/ @ # ?` namespaces; `Alt+Q` | Enterprise: launcher of app tiles + fuzzy search |
| **SAP S/4HANA (Fiori)** | Shell bar (top), always visible | Nav menu in the shell bar; launchpad spaces → pages → sections | Space → page → section → tile | Favourites in My Home | Recently-used apps in My Home | In the shell bar | "My Home": To-Dos, News, Pages, Apps (favourite / most-used / recent), Insight tiles |
| **D365 Business Central** | Top nav menu + top-right utilities | Root menu items expanding into submenus | ~3 (root → submenu → page) | Bookmarks, surfaced on the Role Center | Recent searches in Tell Me *(2°)* | "Tell Me", `Alt+Q` — pages, reports, current-page actions, help | Role Center (role-based dashboard + nav) |
| **D365 Finance & Operations** | Top command bar + left nav pane | Left pane, four sections: Favourites / Recent / Workspaces / Modules | Module → menu group → page | Favourites section | Recent section | In the command bar | Dashboard of workspace tiles; initial page is user-configurable |
| **Oracle NetSuite** | Top header + centre tabs | Centre tabs with submenus | Centre → tab → submenu | Shortcuts menu | Recent Records (last 12) | Global search, `Alt+G`, record-type prefixes (`cus: abc`) | Dashboard of portlets |
| **Oracle Fusion (Redwood)** | Global header | "Product map" on the home experience | Product map → feature → landing page *(2°)* | *(2°)* | *(2°)* | Search box on Home with Ask Oracle | Home with Ask Oracle: search, product map, notifications |
| **Salesforce Lightning** | Two rows: global header + app nav bar | App Launcher (waffle): first ~7 apps, then "View All" with search over apps, objects and items | App → tab → record | Favourites dropdown (star); user-personalisable tabs | Recent Items in the utility bar | In the global header | App-specific Home; utility bar pinned at the bottom (desktop only) |
| **ServiceNow (Next Experience)** | Unified navigation header (top) | "All" overlay panel; also Favourites, History, Workspaces, Admin | Application → module | Favourites, with bookmark **groups** | History menu | In the header | Configurable landing page |
| **Workday** | Top bar + persistent left global-nav sidebar | Sidebar app menu (hideable, reopened via a Menu icon) | App → task/report | Up to 4 pinned apps + a Saved section (max 10 shortcuts) | Recent items | Top search bar, lowercase prefixes | Home page with sidebar |
| **Xero** | Top nav + right-hand panel | Top nav sections (Sales and Purchases split out in the 2025 redesign) | 2 | — | — | In the right-hand panel, alongside help, notifications, apps and the AI assistant | New widget dashboard (drag-and-drop), in beta |
| **QuickBooks Online** | Left nav | Left nav, one consolidated menu (Business/Accountant views merged) | 2 | Bookmarks, moved to the top of the menu | — | Top search | Dashboard |
| **Sage Intacct (Action UI)** | Top application bar | Applications dropdown → hierarchical per-app menus; left Action nav bar with **Favorites** and **All** tabs | App → menu → page | Favourites menu (star), user-specific, drag-to-reorder | — | Top search | Dashboard |
| **Acumatica** | Left module menu (collapsible) | Left menu by module | Module → group → page *(2°)* | Favourites *(2°)* | — | Top search *(2°)* | Role-based dashboard |
| **Zoho Books / Zoho One** | Left sidebar + top utility strip | Left sidebar modules | 2 | Zoho One universal sidebar supports pinning | Recently-used apps (Zoho One) | Top search | Dashboard |
| **ERPNext / Frappe v16** | Left sidebar of workspaces | Workspaces grouped by category (Modules, Domains, Places, Administration) | Category → workspace → shortcut/list | Workspace shortcuts | — | "Awesome Bar" | Workspace |
| **Shopify Admin** | Left sidebar | Left sidebar | 2 | — | — | Top search | Home |
| **Stripe Dashboard** | Left sidebar | Left sidebar (deliberately shortened link list) | 2 | **Shortcuts** section merging pinned **and** recently-visited pages | Merged into Shortcuts | Global search across all object types | Home |
| **Linear / Notion / Slack** *(reference class, not ERP)* | Left sidebar (~224px in Notion) | Sidebar | 2–3, infinitely nestable in Notion | Favourites pinned at the top | Notion: recents | `Cmd+K` palette | Workspace home |

### 1.2 Seven things the field has converged on

**C1 — Every product has a persistent global bar, and it always carries the same four things.**
Home/brand, search, notifications, and the user menu. Even the products that put primary navigation in
a left sidebar (Workday, Zoho, QuickBooks, Shopify, Stripe) keep a top strip for these. There is no
serious product where identity and notifications live in the left column alone. SAP's shell bar is the
most explicit statement of the pattern: a universal header offering global functions plus
context-specific ones.

**C2 — The always-visible navigation is user-curated, not the full tree.** Favourites, pins, bookmarks
and shortcuts appear in every product examined except stock Odoo Community — and Odoo's absence has
generated a whole third-party module market to fill it. Several products deliberately *cap* the
curated set: Workday allows 4 pinned apps and 10 shortcuts; Salesforce's own guidance warns that too
many pins make the nav bar "feel heavy instead of helpful". Tellma's existing pinned top bar is not an
oddity — it is the mainstream answer, arrived at early.

**C3 — Recents are treated as a peer of favourites, not an afterthought.** D365 F&O gives Recent its
own section beside Favourites; NetSuite keeps the last 12 records one hover away; SAP's My Home shows
favourite, most-used and recently-used apps side by side; Stripe merges pinned and recently-visited
into a single "Shortcuts" list; Business Central's 2026 wave adds recent searches to Tell Me. **This is
the clearest gap in the old Tellma shell.** Pins capture what you *always* need; recents capture what
you needed *this week* — month-end close, an audit, a migration — and no pinning ritual covers that.

**C4 — The full tree is reached through a surface that overlays the current page.** Fiori's navigation
menu, ServiceNow's "All" panel, Salesforce's App Launcher, Odoo's apps dropdown, Sage Intacct's
Applications dropdown — all of them drop over the page you are on and leave it intact. The exceptions
are Odoo Enterprise's full-page home menu and the old Tellma launcher, both of which make you *leave*
your work to find another page. For a user who works across many screens at once — Tellma's persona B,
the accountant reconciling — that round trip is the single most repeated cost in the shell.

**C5 — Search is the fast path, it is typed, it is scoped, and it has a hotkey.** `Alt+Q` in Business
Central, `Alt+G` in NetSuite, `Ctrl+K` in Odoo, `Cmd+K` across the modern reference class. Crucially,
the scope everywhere now includes *pages and actions*, not just records: Tell Me searches pages,
reports, actions available on the current page, and help content. Scoping syntax is standard —
NetSuite's `cus: abc`, Workday's lowercase prefixes, Odoo's `/ @ # ?` namespaces. The palette is
explicitly understood as a power-user layer: it "augments your navigation, it does not replace it",
and does nothing for a user on day one.

**C6 — Quick settings are consolidated into a single user menu, and they are session-scoped.** Business
Central's My Settings is the canonical list: role, company, work date, region, language, time zone,
notifications — all switchable without a reload. Fiori has the user actions menu; Odoo, Zoho and
Salesforce have the same construct. Nobody surfaces language or locale as top-level chrome.

**C7 — Mobile is the same shell, collapsed — not a different information architecture.** Fiori
publishes an explicit collapse order (search field collapses first; further actions move to an
overflow; the user menu and product switcher never move). Xero states cross-device consistency as a
design goal of its 2025 redesign. Odoo's mobile shell is the same navbar with the apps menu turned
into a left slide-in drawer, complete with swipe-to-close.

### 1.3 Where products genuinely disagree

**Top bar versus left sidebar.** The split is real, and it correlates with screen shape rather than
with taste:

- **Left sidebar** — QuickBooks, Zoho, Shopify, Stripe, Acumatica, Workday, Frappe, ServiceNow
  (optionally, by pinning an overlay open). These products either have narrow content (forms,
  settings, small tables) or serve users who navigate frequently between a modest number of areas.
- **Top bar** — Odoo, SAP Fiori, Business Central, Sage Intacct, NetSuite. These are the wide-grid ERP
  suites, where a 240–260px permanent column is 15% of a 1600px screen taken away from columns of data.

Tellma is unambiguously in the second group: dense grids at `sm` density are the product's centre of
gravity.

**Launcher versus role dashboard as the home page.** Odoo Enterprise and Salesforce treat home as a
launcher. Business Central, D365 F&O, NetSuite, Acumatica and (as of the 2025 beta) Xero treat home as
a role-based dashboard. The direction of travel is toward *both*: a dashboard-shaped home whose
initial page is user-overridable (Business Central's Role Center field, D365 F&O's Initial Page
setting, Odoo's "set home screen" in third-party themes).

**In-app tabs.** Essentially nobody in web ERP ships a multiple-document interface. Desktop-era SAP GUI
had parallel sessions; the web generation relies on real browser tabs plus breadcrumbs. Shopify's 2025
navigation work went the other way — it added breadcrumb trails and dynamic back buttons rather than
tabs.

### 1.4 What the published UX research says

The research is unusually consistent on the questions this brief asks.

**Hidden navigation carries a measurable penalty — bigger on desktop than mobile.** Nielsen Norman
Group's study of hidden versus visible versus combo navigation found that on desktop, hidden
(hamburger) navigation was used in 27% of cases against 48–50% for visible and combo; tasks took 39%
longer; content discoverability dropped over 20%; and perceived task difficulty rose 21%. On mobile the
penalty is smaller but the ranking holds: combo navigation was used 86% of the time against 57% for
fully hidden. The recommendation is explicit — show top-level options on desktop, and prefer combo
navigation on mobile.

**Left vertical navigation scales better than horizontal for broad, growing hierarchies.** The same
group's analysis notes that users look at the left half of the screen 80% of the time, that vertical
lists are scanned more efficiently than horizontal ones, that adding a category does not force a
redesign, and that the pattern adapts to mobile with few changes. Top horizontal navigation is
characterised as suited to five to seven main sections. The stated drawback is exactly the one that
matters here: it consumes space and lowers the content-to-chrome ratio.

**Breadth beats depth, and three levels is the practical ceiling.** Menu-structure research
consistently finds moderately broad, shallow structures outperform narrow, deep ones; roughly 16
ungrouped top-level links feeding two or three further levels is about the efficiency optimum; static
menus should stay near 7±2 items and navigation depth should not exceed three levels, because error
rates climb with depth. SAP's own launchpad guidance lands in the same place from the other direction:
3–7 apps per section and at most 25 apps per page.

**Mobile primary navigation wants 3–5 fixed destinations.** Both platform guidelines and the current
practitioner consensus put bottom tab bars at 3–5 core sections, with drawers reserved for long or
secondary lists. An ERP with hundreds of pages and no small fixed set of top-level destinations does
not fit the bottom-bar shape.

**Density is a real functional variable, not a style choice.** Comfortable tables show roughly five
rows at 50–52px in the vertical space where a dense table shows fifteen at 28–32px; users who live in
a tool for hours choose density nearly every time. Every pixel of permanent chrome is a fraction of a
row.

**Badges: one signal type per badge, and cap the number.** Established guidance is to use a count where
the number matters and a plain dot where only "something changed" matters, to abbreviate long counts
(`99+`), to hide the badge entirely at zero, and to give the badge an `aria-label` because a coloured
dot is invisible to a screen reader.

**Theme preference is three-valued.** The 2026 practitioner debate settles on storing
`system | light | dark` and computing the effective theme at runtime; the disagreement is only about
whether the *control* shows two states or three. Three states are the clearer choice when the control
lives inside a settings surface rather than in the page's main flow.

**RTL means logical properties, not a mirrored stylesheet.** The consistent advice is `dir` on the
root, CSS logical properties throughout, and deliberate exceptions — numerals, media transport
controls, and certain brand assets stay LTR even in an RTL layout.

### 1.5 A correction to a widely-repeated claim

A cluster of 2026 articles states that **Odoo 19 replaced its top navigation bar with a collapsible
sidebar**, that the "NavBar component is deprecated", and that a new Sidebar component owns app
switching. This is worth addressing directly, because Odoo is the product the old Tellma shell was
modelled on, and a genuine reversal there would be strong evidence.

**It is not true.** Checked against Odoo's shipping 19.0 branch:

- `addons/web/static/src/webclient/navbar/navbar.js` and `navbar.xml` exist and define
  `web.NavBar` → `<header class="o_navbar">` → `<nav class="o_main_navbar">`, containing, in order:
  the apps-menu dropdown (`data-hotkey="h"`), the current app's brand, a breadcrumbs container, the
  current app's section menus, and a right-aligned systray region.
- There is no `webclient/sidebar/` module in 19.0.
- The only sidebar in the file is `web.NavBar.AppsMenu.Sidebar`, rendered **only when
  `this.ui.isSmall`** — the mobile hamburger drawer, with swipe-to-close handlers.
- The Odoo Apps Store still sells 19.0 modules whose selling point is replacing the top navigation bar
  with a sidebar, and the core feature request for a left sidebar remains an open GitHub issue.

Confirmed by the same inspection, and genuinely relevant:

- Odoo 19 ships a command palette at `core/commands/command_palette.js`, bound to `control+k`, with a
  namespace mechanism; the documented namespaces are `/` (menus, apps, modules), `@` (users),
  `#` (channels) and `?` (knowledge articles).
- Documented shortcuts include `Alt+Q` search, `Alt+H` home, `Alt+S` save, `Alt+C` new, `Alt+J`
  discard, `Alt+N`/`Alt+P` next/previous — and holding `Alt` reveals every access key on screen.

The dark-mode claims are similarly overstated: what is documented for Odoo 19 is a Point-of-Sale dark
mode, not a system-wide theme.

**Consequence for Tellma:** the reference product has *not* abandoned the top bar. The pressure toward
a sidebar in the Odoo ecosystem is real and persistent, but it comes from users and third parties, and
it is aimed at a shell that has no pinned-pages row at all. Tellma already answers that pressure a
different way.

---

## Part 2 — Recommendation for Tellma

### 2.0 Decision summary

| # | Decision | Choice | Firmness |
|---|---|---|---|
| D1 | Overall layout | One persistent top bar, two rows on desktop; **no permanent left column** | Firm |
| D2 | Menu + search | **Unified "Launcher" overlay**: empty = browse, typed = search. One component, three entry points; drops below **both** bars so pins stay live | Firm |
| D3 | Menu depth | Model ≤3 levels (Section → Group → Page), 2 preferred; **all levels rendered at once**, no click-through | Firm |
| D4 | Permanently visible nav | The **pinned-pages row** only | Firm |
| D5 | Pins | User-owned, ordered, optional one level of groups; role defaults are a one-time **seed**, not a live overlay; **the shell owns the pin control** | Firm |
| D6 | Recents | Add. Page-level, de-duplicated by route, committed after a short **dwell**, not on every navigation | Leaning |
| D7 | Session control | Avatar + user name + **tenant name**, always visible; everything else inside the menu | Firm |
| D8 | Quick settings visibility | None promoted to the bar; all inside the session menu | Leaning |
| D9 | Theme control | Three-state System / Light / Dark, stored as a preference | Firm |
| D10 | Inbox | Bell + **unseen** badge (`99+` cap); panel shows per-item **unread**; "View all" → a real page | Firm |
| D11 | Home page | The Launcher's browse content, unchanged; plus a user-settable **start page** override | Leaning |
| D12 | Landing behaviour | Keep last-page restore; evaluated **once per boot** on the tenant home route, so an installed PWA launch benefits from it | Firm |
| D13 | Keyboard | `Ctrl/Cmd+K` opens the Launcher; access keys for shell controls, chosen to avoid grid collisions | Leaning |
| D14 | Multi-screen work | **No in-app tabs.** Real anchors, real deep links, real browser tabs | Firm |
| D15 | Breadcrumbs / back | Page-level concern, inside the content area — not shell chrome | Leaning |
| D16 | Mobile | Same shell collapsed; scrollable pin chips; **no bottom tab bar** | Leaning |
| D17 | Chrome budget | ≤ 96px total on desktop, ≤ 100px on mobile | Leaning |
| D18 | RTL | Whole shell mirrors via logical properties; numerals and logo stay LTR | Firm |
| D19 | Accessibility | Landmarks, skip link, single `main`, focus management, badge labels, `aria-current` | Firm |
| D20 | Extension slots | Small typed registry: ≤3 bar items + one full-width banner slot | Leaning |
| D21 | Pinnable left panel | **Deferred**, with the Launcher built so it can be re-hosted without rework | Open |

### 2.1 Layout — D1

**One persistent top bar, in two rows on desktop. No permanent left column.**

```
┌─────────────────────────────────────────────────────────────────────────────────────────┐
│ TELLMA │ ☰ Menu ⌕ Search pages, records, actions…  Ctrl+K │   ⌂ 9  │ AA Ahmad · Acme ▾   │  48px
├─────────────────────────────────────────────────────────────────────────────────────────┤
│ Invoices  Journal Entries  Trial Balance │ Payroll ▾ │ Fixed Assets ▾ │ ⋯ More │ ☆ Pin   │  40px
└─────────────────────────────────────────────────────────────────────────────────────────┘
```

- **Row 1 — the global bar (48px).** Wordmark linking home, then the Menu button and the global search
  input **welded into one grouped control**, then the inbox bell and the session button. This is
  Salesforce's two-row shape and Fiori's shell-bar content list, and it is a superset of the old
  Tellma bar.
- **Row 2 — the pins bar (40px).** The user's pinned pages as one-click targets, plus group chips,
  plus overflow, plus the shell-owned pin control for the current page (D5).

**Menu and search sit together because they are the same surface.** Both open the Launcher (D2) — the
Menu button in browse mode, the input in search mode — so separating them with the wordmark would put
a control and its twin on opposite sides of a divider. The wordmark keeps the leftmost slot because
top-left-is-home is a convention too strong to trade away, and because it is what the old shell did.

**The Menu button is a discoverability affordance, not a second feature.** Focusing the empty input
already shows the browse surface, so the button is strictly redundant to a user who knows the product.
It earns its pixels only for personas A and C, who will never press `Ctrl+K` and for whom a search box
does not read as "browse everything". A defensible alternative is to drop it and widen the placeholder
to "Search or browse all pages".

**Why not a left sidebar.** Four reasons, in descending weight:

1. **Grid width is a functional property of this product.** Tellma is a dense-grid ERP whose default
   density is the compact rung of the size ladder specifically so that more rows fit on screen. A
   permanent 240–260px column costs ~15% of a 1600px viewport permanently, on every screen, for every
   persona — including the data-entry user who never opens the menu.
2. **The cohort agrees.** The wide-grid ERP suites (Odoo, Fiori, Business Central, Sage Intacct,
   NetSuite) are all top-bar products. The sidebar cohort is narrower-content software.
3. **The tie-break.** The brief says: when on the fence, stay near the shell users already know.
4. **Simplicity.** One bar with two rows is materially less layout, less state, and fewer responsive
   permutations than a bar plus a resizable, collapsible, pinnable column.

**Honest counter-evidence, and the mitigation.** The NN/g finding that hidden navigation costs 39% in
task time is real and points at a sidebar. Three things blunt it here:

- That study compares hidden navigation against *visible top-level navigation*. Tellma's shell is not
  the hidden condition — it is the **combo** condition, which scored best in that study: visible
  curated nav (the pins row) plus a fuller hidden menu behind an affordance.
- The affordance is labelled **"Menu"** in words, not a bare hamburger glyph. NN/g's guidance on
  vertical nav applies equally here: a word beats an icon.
- The full menu is one click *or* one keystroke away and never replaces the current page.

**What is deferred rather than rejected:** the pinnable left panel — see D21.

### 2.2 The Launcher — D2

**One overlay surface serves as both the menu and the search results.** Empty query renders the
grouped page directory — the old launcher, unchanged in spirit. Typing filters and re-ranks across
buckets. It opens from three entry points and is the same component in all three, plus a fourth
mounting as the home route.

```
Browse  (☰ Menu, or clicking search, or Ctrl+K)
┌─────────────────────────────────────────────────────────────────────────────────────────┐
│ TELLMA │ ☰ Menu ⌕ |                                            Esc │  ⌂ 9  │ AA Ahmad ▾ │
├─────────────────────────────────────────────────────────────────────────────────────────┤
│ Invoices  Journal Entries  Trial Balance │ Payroll ▾ │ Fixed Assets ▾ │ ⋯ More │ ☆ Pin   │
├─────────────────────────────────────────────────────────────────────────────────────────┤
│ RECENT    Journal Entries · Customer Invoices · Trial Balance · Bank Reconciliation      │
│ ─────────────────────────────────────────────────────────────────────────────────────── │
│ FINANCIALS           SALES                 PURCHASING            ADMINISTRATION          │
│  General Ledger      ★Customers            Suppliers             Users                   │
│ ★Journal Entries      Sales Invoices        Purchase Invoices     Roles & Permissions    │
│ ★Trial Balance        Sales Orders          Purchase Orders       Settings               │
│  …                    …                     …                     …                      │
└─────────────────────────────────────────────────────────────────────────────────────────┘

Typed
│ ⌕ inv|                                                                            Esc    │
│ PAGES     Sales Invoices                                            ↵ open    + New      │
│           Purchase Invoices                                                   + New      │
│           Invoice Settings                                                               │
│ RECORDS   INV-2026-0431  Acme Ltd  AED 12,400   ·   INV-2026-0430  Beta Co  AED 3,100    │
```

Design rules:

- **Overlays, never navigates away.** It drops below **both** bars over a scrim, so the pins row stays
  visible and clickable while it is open; `Esc` and click-away dismiss it, returning focus to the
  trigger. This is the single most valuable divergence from the old shell for the reconciling
  accountant (C4).
- **No pinned band on desktop.** The pins row is right there, one row above. Instead, pinned pages are
  **starred in place** inside the section columns, so the user sees what is pinned in context rather
  than in a duplicate list. On mobile the band returns, because the chip strip is truncated and the
  Launcher is full-screen (§2.11).
- **Ranked buckets, in this order:** Pages, Records, Actions, Settings & Help. Pages are matched
  client-side from the permission-filtered menu index and are instant; Records are server-backed and
  debounced, so page results never wait on the network.
- **"New X" is a page, not an action.** A create route is a page in a particular mode, so it renders as
  an inline `+ New` affordance on its list page's result row — not as a separate row, and not in the
  Actions bucket. Doubling the page index with a "New …" twin of every entry is noise.
- **Prefix scoping**, following NetSuite/Workday/Odoo: a small fixed set only — `/` pages, `>` actions,
  `#` records, `?` help. Resist growing it.
- **Permission-filtered at the source.** A user never sees a page they cannot open, in any of the four
  surfaces.
- **Every result is a real anchor** with a real `href` (see D14).

**What the Actions bucket actually contains.** Things that *do* rather than *go*, in two tiers:

1. **Shell commands** — sign out, switch tenant, change language, change calendar, switch theme, mark
   all notifications read. Genuinely commands, enumerable by the shell itself, cheap. Ship these.
2. **Current-page commands** — post, export, print, clear filters. This is the valuable half and the
   one Business Central emphasises: Tell Me searches pages, reports, help, *and the actions available
   on the page you are standing on*. It is the discoverability answer for a buried toolbar.

Tier 2's cost sits outside the shell. If a page's action bar is **declarative**, the shell harvests its
commands from the same declaration for nothing; if pages hand-roll their toolbars, every page must
publish its actions and the feature stops being free. So tier 2 is **conditional on the action model
the CRUD stack settles on**, not asserted here. If neither tier survives review, drop the bucket —
Pages / Records / Settings & Help is a complete set on its own.

**Why unify menu and search.** They query the same index and produce the same kind of result; splitting
them produces two overlays, two keyboard models, and a user decision ("is this a search thing or a
menu thing?") with no right answer. Odoo effectively demonstrates the split — a home menu *and* a
command palette — and the palette is the one power users actually use.

### 2.3 Menu depth — D3

- **Model:** at most three levels — **Section → Group → Page**. Two is the recommendation for most
  distributions; the third exists for genuinely large domains (e.g. Financials → Accounts Payable →
  Vendor Invoices).
- **Presentation:** all three levels render **simultaneously** in the Launcher — sections as columns
  with headings, groups as sub-headings, pages as links. **Zero clicks of depth.** Depth lives in the
  data model for organisation; it must not become a click-through in the UI.
- **Permanently visible:** none of the tree. The permanent navigation is the pins row (D4).

This is what the breadth-over-depth research recommends (§1.4) and what the launcher pattern already
did well in the old shell. Three levels also means a distribution with 300 pages fits in roughly
6–10 sections × 3–6 groups × 5–15 pages, comfortably inside SAP's own "3–7 per section" heuristic
without any single list becoming unscannable.

**Scale guardrails to publish with the shell:** warn above 10 sections; warn above 30 pages in a group.
These are authoring guidance for distributions, not runtime limits.

### 2.4 Pinned pages — D4, D5

Keep the pinned-pages row as Tellma's primary navigation. It is the old shell's best idea and the
market's consensus answer (C2).

- **User-owned and reorderable** (drag, like Sage Intacct's favourites and Workday's pinned apps).
- **One level of groups.** A group renders as a chip with a caret; clicking opens a short menu of its
  pages. Deeper nesting is rejected — it recreates the menu inside the shortcut bar.
- **Overflow, not truncation.** Beyond what fits, collapse into a "More" menu. Soft-cap the total
  around 20 with a gentle nudge rather than a hard limit — the Salesforce guidance that too many pins
  make the bar heavy is worth heeding, but a hard block will annoy persona D.
- **The shell owns the pin control**, at the end of the pins row: `☆ Pin` for the current route,
  flipping to a filled state when it is already pinned, plus a keyboard shortcut. The shell already
  knows the current route, so this needs **zero cooperation from any page** and is guaranteed
  identical everywhere. A per-page star in a page header would make correctness depend on every page
  author remembering the same convention, which is exactly the kind of convention that decays. The
  cost of the shell-owned placement is distance from the page title; pay it back with a label or
  tooltip naming the page being pinned.
- **Pinning list state is the page's business, optionally.** If a pin should capture filters and sort
  rather than a bare route, the page contributes that state **through a service the shell queries** —
  not through a control it renders. Shell renders; page optionally contributes.
- **A revoked pin degrades gracefully:** if permission is lost or the page disappears, the chip renders
  disabled with a tooltip and a "remove" affordance — it never 404s silently.

**Role defaults are a seed, not a live overlay.** Salesforce's two-layer model — admins guide the
majority, users personalise — is the right shape, but Tellma's user↔role relationship is many-to-many,
so "the defaults for your role" is not a well-defined set. Resolve it once rather than continuously:

1. On first sign-in, and on an explicit "reset to defaults", compute the **ordered, de-duplicated union**
   of the default pin sets of every role the user holds.
2. Order by an explicit ordinal on each role's default set, then by pin ordinal within the set. Roles
   carry no inherent priority, so the ordinal has to be authored, not inferred.
3. **Filter by the user's effective permissions**, so the union never seeds a pin the user cannot open.
4. Cap at ~12 and **materialise the result as that user's own pin list**. Nothing recomputes afterwards.

Treating defaults as a live overlay instead would produce the failure mode that actually hurts: an
admin edits one role and forty people's toolbars silently rearrange overnight. The one thing the seed
model gives up is that a user who *gains* a role later does not inherit its pins — handle that with a
dismissible nudge ("your new Payroll role suggests 3 pages — add them?") or accept it.

**Shipping v1 with no role defaults at all is a legitimate option**, and the cheapest one: an empty
pins bar with an affordance that teaches pinning. The seeding above can land later with no rework,
because it only ever writes into the user's own list.

### 2.5 Recents — D6

Add a recents list. It is the one substantive capability gap against the field (C3).

- Surface it as the **first band of the Launcher's browse state**, and as the top of the palette's
  zero-query ranking.
- **Page-level, de-duplicated by route template** — not by URL, and not by record. Paging through
  invoices with a next-arrow stays one entry, "Sales invoices", however fast the user clicks. Most of
  the flooding problem disappears by choosing the right unit.
- **Committed after a dwell, not on navigation.** Start a timer on route change and record the visit
  only if that route is still current after ~2–3 seconds. A pass-through does not count. A dwell gate
  is the right primitive here and a trailing debounce is not: a debounce keys on quiet periods, while
  what actually matters is whether the user settled on the page.
- Move-to-front on repeat, cap at 10, and **batch the persistence** — never a write per navigation.
- **Do not put it in the top bar.** It is a browse-time aid, not a one-click target; the bar's pixels
  belong to pins.
- Scoped per user **and per tenant**, like the last-page memory (D12).

If record-level recents are ever added — NetSuite keeps the last 12 records — the same dwell gate plus
a per-route cap (at most ~3 records of any one type) keeps a paging spree from flushing the list. Not
in v1.

Firmness is **Leaning** rather than Firm only because it is genuinely new relative to the old shell —
but every comparable product has it, and Stripe merges it with pins into one list precisely because
users do not distinguish "a page I always use" from "a page I used an hour ago" at the moment of
navigating.

### 2.6 Session, tenant and quick settings — D7, D8, D9

**Always visible in row 1:** avatar, user display name, **and the tenant's display name** (the company
the user is working in). Both, always. The tenant name is a safety control, not decoration: a workspace
switcher "decides which reality every other screen renders", and a user who posts a journal entry into
the wrong company has been failed by the shell.

**Inside the session menu**, in this order:

1. **Identity block** — avatar, display name, email.
2. **Tenant** — current tenant, and a switcher when the user has access to more than one. Switching
   resets page state and navigates to that tenant's last page (D12).
3. **My account** — the personal profile modal (name, picture, …), as in the old shell.
4. **Language** — listed by endonym in its own script. Switching flips the shell's direction (D18).
5. **Calendar** — Gregorian / Hijri / Ethiopic / …, drawn from the locale packs the distribution ships.
6. **Theme** — a three-option control: **System · Light · Dark**, stored as a preference and resolved at
   runtime. A two-state toggle cannot express "follow the system" once the user has touched it (D9,
   **Firm**).
7. **Density** — if and when a runtime density switch exists; otherwise omit rather than fake it.
8. **Help, keyboard shortcuts, about (version).**
9. **Sign out.**

**Nothing else is promoted to the bar (D8, Leaning).** Language, calendar and theme are set once a
session at most, usually once ever. Business Central, Fiori, Odoo, Zoho and Salesforce all bury them
in exactly this menu. The one case worth a distribution-level opt-in slot is a genuinely bilingual
workplace where users flip language many times a day; that is what the extension slot in D20 is for.

### 2.7 Inbox — D10

- **Badge shows the unseen count** — items that arrived since the panel was last opened — capped at
  `99+`, hidden entirely at zero. Opening the panel clears unseen.
- **Unread is a per-item state** shown inside the panel, cleared per item on read, with a "Mark all
  read" action. If unread items exist but nothing is new, show a **plain dot** rather than a count.
  One badge, one signal type: the count means "new", the dot means "outstanding".
- **`aria-label` spells it out** — "Inbox: 9 new, 24 unread" — because a coloured badge is invisible to
  a screen reader.
- **The panel is a preview; the inbox is a page.** "View all" opens a real, pinnable, deep-linkable
  inbox page. The panel should never become the only place notifications live.
- Both counters are computed server-side and pushed; the shell renders, it does not derive them.

### 2.8 Home page and landing behaviour — D11, D12

**Is a launcher sufficient? Yes — with one addition, and one escape hatch.**

The home page renders exactly the Launcher's browse content (D2): the Recent band, then the section
columns with pinned pages starred in place. No separate pinned band — the pins row is two rows above
it here just as it is behind the overlay. That is the whole home page. The reasoning the old shell used
still holds and is now stronger: **because the app restores
the last page, home is a rarely-seen surface, and investment in it has poor returns.** Building
dashboards into the shell's home would be building a feature that most users see once a week.

The escape hatch handles persona C. Senior managers and executives live in reports and dashboards, and
for them a launcher *is* a wasted landing. The answer used by Business Central (Role Center), D365 F&O
(Initial Page) and Odoo themes ("set home screen") is a **user-settable start page**: any page in the
system, including a dashboard, becomes what the tenant home route renders. Admins may seed it per role.
Executives get a dashboard home without the shell growing a dashboard engine, and the launcher stays
available behind the Menu button.

**Two routes, and only one of them is home.** The shell's home is the **tenant home**,
`/<tenant-id>` — every page in the product lives under a tenant segment. The site root, `/`, is a
different thing: either a tenant-switcher landing page or a redirect into the user's live tenant. If it
resolves to a switcher, that switcher is **the one route in the product that renders outside this
shell** — it cannot show a pins bar, an inbox or a tenant name, because no tenant is selected yet. It
therefore needs its own minimal chrome carrying identity and sign-out only, and the shell must not
assume its landmarks, skip link or preference plumbing are present there. Which of the two `/` resolves
to is still open (Q8), and it changes only the switcher's existence, not any rule below.

**Last-page restore (D12, Firm) — keep it, with rules:**

- **Evaluated exactly once, on the app instance's first navigation after boot, and only when the
  resolved route is the tenant home.** Every later navigation to the tenant home renders home normally.
- A cold load on a deep link (`/<tenant-id>/sales-invoices/431`) does not restore — the URL wins.
- Remembered **per user and per tenant**, never globally.
- Falls back to home with a toast if the page is gone, permission was revoked, or the record no longer
  exists.
- Never restores into a modal or an unsaved-draft state.
- Must not fight the browser's back button: the restore is a replace, not a push.

**Why the first rule is written that way.** The requirement it protects is narrow: clicking the wordmark
must go home *and stay home* — the restore must never bounce a deliberate navigation. Phrasing that as
"only on a new session or tab" would break the case that matters most. An installed PWA always launches
at its `start_url`, so a session-scoped rule would skip the restore for precisely the daily heavy users
who bothered to install the app. Keying on **the first navigation after boot** instead of on session
novelty fixes that with no manifest tricks and no `?source=pwa` marker: a cold PWA launch *is* the first
navigation after boot, whether it lands on the tenant home directly or arrives there via the root
redirect (the redirect hop resolves before the rule is evaluated).

**Why per user and per tenant.** Per tenant, because a page remembered in one tenant is at best
meaningless in another and at worst renders a stale or wrong-scoped view straight after a switch — so
the memory key must include the tenant. Per user, because workstations get shared, especially by
persona A, and one clerk's working context must not greet the next.

### 2.9 Keyboard model — D13

Two distinct populations need two distinct mechanisms, and conflating them is the usual mistake.

- **Persona A (data entry, all day, few screens)** needs *access keys* on the controls they touch and
  an interaction model that never steals a key the grid needs. Odoo's approach is worth copying wholesale:
  a small set of `Alt`-based access keys, plus **hold `Alt` to reveal every shortcut on screen**.
- **Personas B and D (many screens, exploratory)** need the palette. `Ctrl+K` / `Cmd+K` opens the
  Launcher in search mode; clicking the search input does the same thing; `☰ Menu` opens it in browse
  mode. `Esc` closes and restores focus.

Rules:

- The shell **must not** claim keys the data grid needs for cell navigation and editing. The shell's
  key list is fixed, small, published, and reviewed against the grid's before shipping.
- Every shortcut is discoverable from "Keyboard shortcuts" in the session menu, and shown as a hint in
  the search input (`Ctrl+K`).
- Shortcut labels adapt to platform (`Cmd` on macOS).

### 2.10 Multi-screen work, and why there are no in-app tabs — D14, D15

Persona B — the accountant with eight things open during a close — is the persona most likely to
generate a request for in-app tabs. **Reject it.**

- No comparable web ERP ships one (§1.3).
- It duplicates a facility the browser already provides better, including window management across
  monitors, which is what a reconciling accountant actually uses.
- It is a large, stateful, permanently-owned subsystem: per-tab scroll, per-tab form state, per-tab
  dirty tracking, restoration, memory pressure with a dozen grids live.
- It breaks deep-linkability and sharing, which an ERP needs for approvals and support.

**What to build instead**, which costs almost nothing:

- **Every navigable thing is a real `<a href>`** with a real URL — pins, menu entries, palette results,
  breadcrumbs. Middle-click and `Ctrl`-click then just work, and the accountant builds their own tab
  set in seconds.
- **Recents** (D6) makes returning cheap when they did not open a tab.
- **The Launcher overlays** rather than navigates (D2), so exploring never costs the current page.

**Breadcrumbs and back (D15, Leaning):** page-level, rendered inside the content area, not in the shell.
Shopify's 2025 work shows breadcrumbs are the right answer for drill-down context — but they are
per-page structure, and putting them in shared chrome (as Odoo does) couples the shell to every page's
navigation model. The shell provides Home; the page provides its own trail.

### 2.11 Responsive behaviour — D16, D17

**Chrome budget (D17):** ≤ 96px total on desktop (48 + 40 + border), ≤ 100px on mobile. State it as a
budget and hold it, because at compact density every 40px of chrome is roughly one row of data.

**Breakpoints and collapse order**, following Fiori's explicit ordering:

| Width | Row 1 | Row 2 |
|---|---|---|
| ≥ 1280px | Wordmark, Menu (icon + label) + full search input as one control, bell, avatar + name + tenant | Pins with labels, group chips, More, Pin control |
| 1024–1280px | Search input narrows; session button drops the user's name, keeps the tenant | Pins begin overflowing into More |
| 768–1024px | Search collapses to an icon; Menu keeps its label | Pins become a horizontally scrollable chip strip; Pin control becomes an icon |
| < 768px | `☰` · wordmark · `⌕` · bell · avatar | Scrollable chips, user-dismissible |

```
< 768px
┌────────────────────────────────┐
│ ☰   TELLMA        ⌕   ⌂9   AA │
├────────────────────────────────┤
│ ‹ Invoices  Journal  Trial Bal…│
└────────────────────────────────┘
```

- `☰` opens the Launcher **full-screen** (browse); `⌕` opens the same surface focused in search mode.
- **`☰` leads here, ahead of the wordmark** — the inverse of desktop (D1), deliberately. The
  hamburger-first convention on phones is strong, and the adjacency argument that put Menu next to
  search on desktop does not apply once search has collapsed to its own icon on the opposite side.
- **The pinned band returns inside the Launcher here**, unlike desktop (D2): the chip strip is
  truncated and the full-screen Launcher covers it, so pins genuinely are not reachable otherwise.
- The session menu becomes a bottom sheet.
- **No bottom tab bar (D16, Leaning).** Bottom bars are for 3–5 fixed destinations; an ERP with
  hundreds of pages and per-user page sets has no such set. Keeping the same shell across breakpoints
  is also the explicit consistency goal Xero adopted, and it is one implementation rather than two.
  *The alternative worth testing later:* a bottom bar whose tabs are the user's top four pins. It is
  appealing for persona A on a phone, but it forks the model across breakpoints — hence Open, not
  rejected.

### 2.12 RTL, accessibility, theming — D18, D19

**RTL (D18, Firm).** The whole shell mirrors: the menu button and wordmark move to the right, the
session and inbox to the left, the pins strip scrolls the other way, the Launcher's columns reverse.
Implemented with logical properties and direction-aware keyboard handling, never a mirrored stylesheet.
Deliberate exceptions stay LTR: numerals in badges and counters, and the wordmark.

**Accessibility (D19, Firm).**

- `<header>` as the banner landmark; the pins row is a `<nav aria-label="Pinned pages">`; the Launcher's
  tree is a `<nav aria-label="Main menu">`; exactly one `<main>`; no nested landmarks.
- A **skip link** as the first focusable element, jumping to `main` — WCAG 2.4.1 is Level A and a
  two-row bar plus a pin strip is exactly the repeated block it exists for.
- The active pin carries `aria-current="page"`.
- Overlays trap focus, close on `Esc`, and return focus to the trigger.
- Badges carry text labels (D10); nothing is signalled by colour alone.
- The whole shell is in the axe CI gate alongside the component library.

**Theming.** The shell consumes the same semantic tokens as everything else; it defines no colours of
its own. The three-state theme preference (D9) sets the root attribute the token layer already keys on.

### 2.13 Extension slots — D20

Distributions will want to contribute to the shell. Give them a small, typed, capped surface rather
than letting them reach into it:

- **Bar slot** — up to **three** items, right side of row 1, before the inbox. Icon + label + badge +
  panel. This is Odoo's systray registry, with a cap.
- **Banner slot** — one full-width strip below the bar, for shell-level states, one at a time by
  priority: impersonation/support session (persona D), offline or stale data, subscription or
  environment warnings. It participates in the chrome budget when present.
- Nothing else. No arbitrary injection into rows, no left-side contributions, no reordering of the
  built-ins.

### 2.14 Deliberately not adopted

| Pattern | Seen in | Why not |
|---|---|---|
| Permanent left navigation column | Workday, Stripe, QBO, Acumatica | Costs ~15% of viewport width permanently in a dense-grid product; the cohort Tellma belongs to does not do it. Revisit as D21. |
| In-app tab strip (MDI) | Third-party Odoo modules | Duplicates the browser badly; large permanent subsystem; breaks deep links. |
| Bottom utility bar | Salesforce | Desktop-only by the vendor's own admission; overlaps with the Launcher and the inbox panel. |
| Admin-curated "spaces and pages" | SAP Fiori | Powerful, but shifts a large configuration burden onto whoever sets up each distribution. Per-role seeded pins (D5) captures most of the benefit at a fraction of the cost. |
| Four-section navigation pane | D365 F&O | Favourites / Recent / Workspaces / Modules as four peer accordions is a lot of chrome for what the Launcher's bands express in one surface. |
| AI assistant panel in the shell | Xero, Oracle | Reserve the slot conceptually; do not design the shell around it yet. |

### 2.15 Implementation shape

The recommendation is deliberately small. Roughly:

- `tm-app-shell` — the two-row bar, landmarks, skip link, banner slot, responsive collapse.
- `tm-launcher` — one component: index, filter, ranked buckets, keyboard model. Mounted in three
  places (overlay, home route, palette-with-query). **The single biggest implementation lever in this
  document is that the menu, the search results and the home page are one component.**
- `tm-pin-bar` — chips, groups, drag-reorder, overflow, and the pin/unpin control for the current
  route (D5), so no page carries any pinning code.
- `tm-session-menu` — identity, tenant switch, the quick settings, sign out.
- `tm-inbox-button` + panel.
- A shell state service: menu index (permission-filtered), pins, recents (dwell gate), last-page
  memory, preferences.

Everything else is existing library work: overlays, menus, badges, tokens, and the size ladder.

Two seams reach outside the shell and should be named early: an optional **page-state contributor**
(so a pin can capture filters rather than a bare route, D5) and an optional **page-action publisher**
(so the Launcher's Actions bucket can list current-page commands, D2). Both are pages opting *in*
through a service; neither is something a page must render, and the shell works correctly with neither
present.

---

## Part 3 — Questions the brief did not ask

Ordered by how much they would cost to get wrong.

1. **How do users work across many screens at once?** The persona-B question the brief implies but does
   not state. Answered in D14: no in-app tabs, real anchors, recents, non-destructive Launcher.
2. **Who owns the menu, and who owns the pins?** The menu is structural and permission-filtered (the
   distribution owns it); pins are personal with admin-seeded per-role defaults. Getting this wrong
   produces either an unmaintainable menu or pins nobody discovers.
3. **What happens when a pinned or restored page is no longer permitted?** Both are user-held state
   pointing at server-controlled resources, and both will go stale. Defined in D5 and D12.
4. **What does the shell do when the user belongs to more than one tenant?** Tenant name always
   visible, switching resets state, recents and last-page are per-tenant, and the site root may resolve
   to a tenant switcher that renders outside the shell entirely. See D7 and D12.
5. **What does the shell look like for IT support (persona D) acting on behalf of a user?** An
   unmistakable impersonation banner in the banner slot (D20), present for the whole session.
6. **What does the shell show when offline or serving stale cached data?** The app shell renders from
   cache with the last known identity; a connectivity state uses the banner slot. Otherwise a cached
   shell silently lies about freshness.
7. **Where does the page's own action bar (save / new / delete) live, and how does it stack?** Not shell
   chrome, but the shell must define the sticky region, the z-index order against overlays and the
   banner, and the scroll behaviour — or every page will invent its own.
8. **What is the URL contract?** Every page addressable, every list state expressible, so links survive
   being pasted into an approval email. This is a precondition for D14, not an extra.
9. **What prints?** All shell chrome hides on print. Cheap; universally forgotten.
10. **How is the shell measured after launch?** Menu-open rate per persona, palette adoption, pin
    counts, share of navigations from pins vs recents vs search vs menu. These are the numbers that
    settle D21 and D16.

---

## Part 4 — Open questions

| # | Question | How to settle it |
|---|---|---|
| Q1 | **D21 — should the Launcher be re-hostable in a pinnable left panel?** ServiceNow lets users pin overlay panels into persistent side panels; that is the cheapest way to serve both cohorts. | Build the Launcher's data and item components independent of their container in v1. Ship the panel only if telemetry shows personas B and D opening the menu more than ~5 times per session. |
| Q2 | **D16 — bottom tab bar on phones, populated by the user's top pins?** | Prototype and test with persona A on a phone. Fork the model only if the win is large. |
| Q3 | Should pinned **records** (not just pages) be allowed — a specific customer, a specific open period? | Watch how users misuse page pins; if they pin filtered list pages to fake it, that is the signal. |
| Q4 | Does the two-row bar hold on a 1366×768 laptop, the realistic low end? | Measure rows-visible against the current grid at compact density before locking D17. |
| Q5 | Should the theme control be promoted out of the session menu? | Only if telemetry shows repeated same-day toggling. |
| Q6 | How should record search rank against page search when a query matches both strongly? | Needs a small relevance study once real data exists; the bucket order in D2 is the starting point, not the answer. |
| Q7 | Is one level of pin groups enough for persona D, who may pin twenty-plus pages? | Observe. Do not pre-emptively add a second level. |
| Q8 | **Does the site root resolve to a tenant switcher, or redirect into the live tenant?** A switcher would be the only route rendering outside this shell, needing its own minimal identity-and-sign-out chrome. | A product/hosting decision, not a shell one — but the shell must be told which, because it determines whether a shell-less route exists at all. Every rule in D12 holds either way. |
| Q9 | Is the last-page memory stored server-side (keyed by user + tenant) or per device? | Server-side gives cross-device continuity — leave the desktop mid-close, open the phone, land in the same place. Per-device treats a phone and a desktop as different contexts, which some users prefer. A genuine coin-flip; server-side is marginally cheaper since the shell already fetches preferences on boot. |
| Q10 | Does the Menu button survive, or does the search field alone carry browse mode? | Instrument it. If Menu-button opens are dominated by `Ctrl+K` and input focus across all four personas, the button is dead pixels. |
| Q11 | Does the Actions bucket get its second tier (current-page commands)? | Depends entirely on whether the CRUD stack's action bars are declarative enough to harvest for free. Settle it when that model is settled, not before. |

---

## Appendix — Sources

**Verified directly against shipping source** — Odoo 19.0 (`odoo/odoo`, branch `19.0`):
`addons/web/static/src/webclient/navbar/navbar.xml`, `navbar.js`, and
`addons/web/static/src/core/commands/` (`command_service.js`, `default_providers.js`).

**Vendor documentation and design systems**

- [SAP Fiori launchpad — shell bar usage](https://www.sap.com/design-system/fiori-design-web/v1-145/ui-elements/shell-bar/usage), [launchpad spaces](https://www.sap.com/design-system/fiori-design-web/v1-84/foundations/integration-and-services/sap-fiori-launchpad/sap-fiori-launchpad-spaces), [My Home](https://www.sap.com/design-system/fiori-design-web/foundations/integration-and-services/sap-fiori-launchpad/sap-fiori-launchpad-my-home)
- [Business Central — adding menus to the navigation area](https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/developer/devenv-adding-menus-to-navigation-pane), [designing Role Centers](https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/developer/devenv-designing-role-centers), [finding pages and information (Tell Me)](https://learn.microsoft.com/en-ca/dynamics365/business-central/ui-search), [change basic settings for the current user](https://learn.microsoft.com/en-us/dynamics365/business-central/ui-change-basic-settings)
- [Dynamics 365 Finance & Operations — navigation concepts](https://learn.microsoft.com/en-us/dynamics365/fin-ops-core/dev-itpro/user-interface/page-navigation)
- [Microsoft Fluent 2 — Nav usage](https://fluent2.microsoft.design/components/web/react/core/nav/usage)
- [ServiceNow Horizon — unified navigation](https://horizon.servicenow.com/guidelines/unified-navigation), [Next Experience UI overview](https://www.servicenow.com/community/next-experience-articles/next-experience-ui-overview/ta-p/2332080), [grouping favorites](https://www.servicenow.com/community/developer-articles/grouping-favorites/ta-p/2296248)
- [Salesforce — App Launcher](https://help.salesforce.com/s/articleView?id=sf.identity_app_launcher.htm), [personalized navigation considerations](https://help.salesforce.com/s/articleView?id=sf.user_userdisplay_tabs_lex_considerations.htm), [Trailhead: navigate Lightning Experience effectively](https://trailhead.salesforce.com/content/learn/modules/lightning-experience-for-salesforce-classic-users/navigate-around)
- [NetSuite — keyboard shortcuts](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N474926.html)
- [Sage Intacct — all about navigating](https://www.intacct.com/ia/docs/en_US/help_action/Intacct_basics/New_to_Sage_Intacct/all-about-navigating-intacct.htm), [company navigation](https://www.intacct.com/ia/docs/en_US/help_action/Intacct_basics/Navigating_Intacct/Company-nav.htm)
- [Zoho Books — navigating Zoho Books](https://www.zoho.com/us/books/help/getting-started/navigating-zoho-books.html), [Zoho One universal sidebar](https://www.zoho.com/one/videos/save-time-with-the-zoho-one-universal-sidebar.html)
- [Odoo 19 — keyboard shortcuts](https://www.odoo.com/documentation/19.0/applications/essentials/keyboard_shortcuts.html), [release notes](https://www.odoo.com/odoo-19-release-notes), [open request for a left sidebar](https://github.com/odoo/odoo/issues/180817)
- [Shopify — app navigation](https://shopify.dev/docs/apps/design/navigation), [improved admin navigation changelog](https://changelog.shopify.com/posts/improved-admin-navigation)
- [Stripe — dashboard update, May 2024](https://support.stripe.com/questions/dashboard-update-may-2024)
- [Frappe Framework v16](https://frappe.io/framework/version-16), [Frappe UI sidebar](https://ui.frappe.io/docs/components/sidebar)
- [Xero — new homepage and navigation](https://www.xero.com/us/campaign/new-homepage-navigation/)
- [QuickBooks Online — consolidating Business and Accountant views](https://quickbooks.intuit.com/ca/resources/accountants/business-and-accountant-view-consolidation/)
- [Workday global navigation sidebar (institutional quick-reference)](https://uada.edu/employees/business-office/human-resources/new-employee/QRG_ESS01_Global%20Navigation%20Sidebar.pdf)
- [PatternFly — notification badge guidelines](https://www.patternfly.org/components/notification-badge/design-guidelines/)

**UX research and analysis**

- [NN/g — Hamburger menus and hidden navigation hurt UX metrics](https://www.nngroup.com/articles/hamburger-menus/)
- [NN/g — Left-side vertical navigation on desktop](https://www.nngroup.com/articles/vertical-nav/)
- [NN/g — Horizontal attention leans left](https://www.nngroup.com/articles/horizontal-attention-leans-left/)
- [Human Factors International — breadth vs. depth](https://www.humanfactors.com/Newsletters/breadth_vs_depth_we_revisit_this_question.asp)
- [Effects of breadth, depth and number of responses on computer menu search performance](https://www.sciencedirect.com/science/article/abs/pii/S0020737388800683)
- [SaaSUI — SaaS navigation UX patterns](https://www.saasui.design/blog/saas-navigation-ux-patterns), [search and command-palette UX patterns](https://www.saasui.design/blog/saas-search-command-palette-ux-patterns)
- [UXPin — mobile navigation design patterns](https://www.uxpin.com/studio/blog/mobile-navigation-examples/)
- [Setproduct — badge UI design](https://www.setproduct.com/blog/badge-ui-design), [data table UI design reference](https://www.setproduct.com/blog/data-table-ui-design)
- [Lea Verou — dark mode toggles: two states are enough](https://lea.verou.me/blog/2026/dark-mode-toggles/), [Bram.us — the case for tri-state dark mode toggles](https://www.bram.us/2026/08/18/the-case-for-tri-state-dark-mode-toggles/)
- [SimpleLocalize — RTL design guide for developers](https://simplelocalize.io/blog/posts/rtl-design-guide-developers/)
- [TestParty — WCAG 2.4.1 bypass blocks](https://testparty.ai/blog/wcag-2-4-1-bypass-blocks-2025-guide)

**Secondary sources treated with caution.** Several 2026 articles claiming Odoo 19 replaced its navbar
with a sidebar — see [§1.5](#15-a-correction-to-a-widely-repeated-claim). They are not relied on here.

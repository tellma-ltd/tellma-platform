# F10 — UI design digest (Claude Design project + screenshots)

Digest of the Claude Design project in `.tmp/ui-design/` and the PNGs in `.tmp/images/`. The
designs are authoritative for theme and visual style only, not architecture. The canvases have no
§ numbers, so references use `File` + frame id or style-object name, e.g. "ERP 3b" is frame 3b of
`ERP Pages.dc.html`, "ERP `dRow1`" is the style object `dRow1` in that file's script, and
"str `s_note`" is the key `s_note` in `strings.js` (EN dictionary).

Metric conventions: all px values are CSS px at 1x. The PNG screenshots are rendered at ~1.5x
(app bar 60 px tall in the PNG = 40 px CSS), so never measure from a PNG.

## 1. Sources read

| Path (under `.tmp/`) | Size | Read |
|---|---|---|
| `ui-design/CLAUDE.md` | 4 lines | fully (the no-all-caps rule) |
| `ui-design/Shell.dc.html` | 215 lines, 27.5 KB | fully (the shell component, props-driven) |
| `ui-design/ERP Pages.dc.html` | 2,220 lines, 250 KB | fully (tabs 01 shell, 02 home, 03 search, 04 details, 05 report studio) |
| `ui-design/SearchPage.dc.html` | 206 lines, 18.7 KB | fully (the search-page component embedded in shell frames) |
| `ui-design/ReportStudio.dc.html` | 540 lines, 52 KB | fully |
| `ui-design/Component Library.dc.html` | 1,421 lines, 144 KB | sections 08–20 and the sheet chrome fully; 01–07 (form controls, already in specs 0005/0006) via their strings and notes; script metrics by search |
| `ui-design/Identity Server.dc.html` | 952 lines, 83 KB | skimmed |
| `ui-design/strings.js` | 1,018 lines, 117 KB | EN dictionary read for all ERP keys; AR skimmed |
| `ui-design/support.js` | 1,911 lines, 69 KB | skimmed (design-canvas runtime) |
| `ui-design/_ds/tellma-design-system-…/readme.md` + `tokens/*.css` + `styles.css` + `fonts.css` | 122 + 319 lines | fully |
| `ui-design/uploads/library-components.md`, `uploads/identity-server-pages.md` | 876 + 415 lines | skimmed for ERP-relevant rules |
| `images/*.png` (17 files) | — | every image viewed |
| `ui-design/uploads/*.png` (5 files) | — | viewed: red-pen markup on older library sheets (form-field error popup, grid header checkbox, date-picker views; those sheets still used all-caps mono labels), an older session-menu/inbox iteration, a studio crop |
| `docs/specs/0011`, `0020`, `0021` | — | grepped only, to check route shape, job states and inbox verbs for §4–§5 |

## 2. Key facts

### 2.1 Design system tokens (`_ds/…/tokens/*.css`, `readme.md`)

Colors (`tokens/colors.css`, `:root`):
- Ink: `--ink-900 #001722` (canonical; wordmark, primary text, dark app bar), `--ink-800 #0A2530`,
  `--ink-700 #163542`.
- Teal ramp: `--teal-50 #EAF4F7`, `-100 #CBE6EC`, `-200 #A3D3DD`, `-300 #74BACA`, `-400 #4CA0B6`
  (canonical logo teal, brand only), `-500 #3E899D`, `-600 #316E80`, `-700 #265767`,
  `-800 #1B3F4B`, `-900 #0E2832`.
- Neutrals (cool blue-grey): `--white #FEFEFE` (never pure #FFF), `--grey-25 #F7FAFB`,
  `-50 #EEF3F4`, `-100 #E1E8EA`, `-200 #CBD6D9`, `-300 #A8B7BC`, `-400 #7C8E94`, `-500 #56686F`,
  `-600 #3C4E55`, `-700 #283A41`, `-800 #14262E`, `-900 = --ink-900`.
- Semantic: `--success #2E7D5B` / `-bg #E5F2EC` / `-border #B6DDC9`; `--warning #B7791F` /
  `#FBF1DF` / `#ECD5A6`; `--error #C0392B` / `#FAE8E6` / `#EEC2BC`; `--info = teal-500`,
  `--info-bg = teal-50`, `--info-border = teal-100`.

Semantic roles (`tokens/semantic.css`):
- `--accent = teal-400` (non-text only: focus, dots, active bars, tints). `--color-primary =
  teal-600` (any teal surface carrying text; white text 5.67:1), hover `teal-700`, active
  `teal-800`, `--color-on-primary = --white`.
- Text: `--text-strong ink-900`, `--text-body grey-700`, `--text-secondary grey-500`,
  `--text-muted grey-400`, `--text-on-dark white`, `--text-link teal-600`.
- Surfaces: `--surface-page white`, `--surface-subtle grey-25` (app canvas, zebra),
  `--surface-sunken grey-50` (table heads), `--surface-card white`, `--surface-inverse ink-900`,
  `--surface-hover grey-25`, `--surface-selected teal-50`.
- Borders: `--border-subtle grey-100`, `--border-default grey-200`, `--border-strong grey-300`,
  `--divider grey-100`.
- Form-field group: `--field-bg white`, `--field-bg-disabled grey-50`, `--field-bg-filled grey-25`,
  `--field-border grey-200`, `-hover grey-300`, `-focus teal-500`, `-invalid error`,
  `--field-text ink-900`, `--field-placeholder grey-400`, `--field-icon grey-400`,
  `--field-radius radius-sm`, `--field-height 38px`, `--field-height-sm 30px`,
  `--field-height-lg 46px`, `--field-padding-x 12px`, `-y 8px`, `--field-font-size text-sm (14)`.
- Legacy aliases kept: `--color-text`, `--color-text-secondary`, `--color-bg`, `--color-bg-subtle`,
  `--color-border`.

Typography (`tokens/typography.css`):
- Families: `--font-sans 'Noto Sans'`, `--font-arabic 'Noto Sans Arabic'`, `--font-mono 'Noto Sans
  Mono'` (code/API only, never amounts or tables). Weights 400/500/600/700.
- Scale: `--text-xs 12`, `-sm 14`, `-base 16`, `-lg 18`, `-xl 22`, `-2xl 28`, `-3xl 36`,
  `-4xl 48`, `-5xl 60`. Minimum UI text 12 px (readme); the canvases nonetheless use 11.5 px and
  12.5 px in `Shell.dc` and `SearchPage.dc` (see §5).
- Leading: tight 1.2, snug 1.35, body 1.6, arabic 1.9. Tracking: tight -0.02em, normal 0, wide
  0.04em (the project rule overrides wide tracking: use ~.02em).
- `--arabic-step 1.08` (Arabic ~1 step larger). `[dir="rtl"]` switches to `--font-arabic` and
  `line-height 1.9`. `.tnum` utility = `font-variant-numeric: tabular-nums`.
- `fonts.css` loads from Google Fonts CDN; readme: production must self-host `.woff2` (on-prem,
  offline).

Spacing, radii, elevation, motion, focus (`tokens/spacing.css`):
- 4 px base: `--space-0..24` = 0, 4, 8, 12, 16, 20, 24, 32, 40, 48, 64, 80, 96.
- Radii: `--radius-xs 4`, `-sm 6` (inputs, buttons), `-md 10` (cards, panels, popovers),
  `-lg 16` (modals), `-xl 24`, `-full 999`.
- Shadows (ink-tinted, overlays only): `--shadow-xs 0 1px 2px rgba(0,23,34,.05)`; `-sm`, `-md
  0 4px 12px …08 + 0 1px 3px …05`, `-lg 0 12px 32px …12 + 0 2px 6px …06`.
- Motion: `--duration-fast 120ms`, `-normal 180ms`, `-slow 280ms`; `--ease-standard
  cubic-bezier(0.2,0,0,1)`, `--ease-out`, `--ease-in-out`. No bounce; honor reduced motion.
- Focus: `--focus-ring: 0 0 0 2px var(--white), 0 0 0 4px var(--teal-400)` (2 px halo, white gap).

Dark mode (`tokens/dark.css`):
- Opt-in scope `[data-theme=dark]` on `<html>` or any ancestor (the canvases set it on the root
  `div`); sets `color-scheme: dark`. No `prefers-color-scheme` media query exists in the DS: the
  "System" theme option (session menu) must be implemented by the app.
- Strategy: invert the neutral ramp (`--white #16252D` … `--grey-900 #F2F7F8`), then refine:
  `--surface-page/subtle #0D181E`, `--surface-sunken #0A1418`, `--surface-card = --white`,
  `--surface-inverse #1C2C34`, `--surface-hover #20313A`, `--surface-selected rgba(76,160,182,.18)`;
  borders become white at alpha .07/.12/.20; status hues lightened (`--success #5FC79A`, `--warning
  #E0A93E`, `--error #E06A5C`) with rgba fills; `--teal-50/100` become rgba tints, `--teal-700
  #8FD0DE`; `--text-link teal-300`.
- Action teal flips: `--color-primary teal-400`, hover `teal-300`, active `teal-200`,
  `--color-on-primary ink-900` (ink on teal-400 = 6.12:1). `--field-border-focus teal-300`.
- Brand ink is never inverted: logo, app icon and the dark app bar stay ink in both themes.
- Rule: components must color text with `--text-strong/--text-body/--field-text`, not raw
  `--ink-900`, or dark mode breaks.

Other DS rules (readme): Lucide icons only (outline, stroke 1.75, SVG, `currentColor`,
self-hosted `assets/vendor/lucide.min.js`; Tabler only as finance fallback), no icon fonts, no
emoji, no gradients, no colored left-border accent cards, hairline borders, flat cards
(`--shadow-sm` only when lifting), dialog scrim = ink at low alpha. Interaction states: hover →
`--surface-hover`; selected → `--surface-selected` + optional teal inline-start marker; disabled
→ grey-50 bg + grey-400 text. RTL via CSS logical properties only.

### 2.2 Project rule and copy conventions

- `ui-design/CLAUDE.md`: no all-caps anywhere (no `text-transform: uppercase`, no caps copy) for
  labels, headings, table headers, menu items, eyebrows; currency/locale codes (AED, SAR, EN) are
  fine; small labels keep ~.02em tracking.
- Readme content rules: sentence case everywhere ("Add journal entry"); no em-dashes in
  customer-facing copy; avoid semicolons; Oxford comma; no exclamation marks; address the user as
  "you"; amounts right-aligned, tabular, ISO code prefix (`SAR 1,250,000.00`); dates `19 Jun 2026`;
  Arabic thousands separator is a plain `,` (never U+066C); Arabic copy has no tashkeel.
- Microcopy patterns: empty "No journal entries yet. Add one to get started."; destructive
  confirm "Delete this grant allocation? This cannot be undone."; success toast terse ("Invoice
  posted."); errors state cause + remedy.
- Observed copy in the canvases follows this: "3 items selected", "Leave without saving?",
  "Save failed", "You're all caught up", "Nothing is waiting on you", "Reference 8f2c-41ae".
  Exceptions that slip: the assignment titles use Title Case for document types ("Purchase Order
  PO-00218", "Journal Voucher JV-00912") while the launcher uses sentence case ("Purchase
  orders"); the checkbox specimen prints `TRUE / FALSE` (str `cb_true`/`cb_false`) in caps; the
  canvases' own sheet chrome uses small mono labels (not product UI).

### 2.3 Breakpoints and canvas mechanics

- One closed set of frame widths everywhere (`Shell.dc`, `SearchPage.dc`, `ERP Pages`):
  `xs 390`, `sm 576`, `md 768`, `lg 992`, `xl 1200`, `xxl 1440` (labels Phone, Small tablet,
  Tablet, Laptop, Desktop, Wide desktop). These match Bootstrap-style min-widths 576/768/992/1200
  plus a 1440 "wide" and a 390 phone specimen.
- Derived flags used in the logic: `isTiny W<576`, `isCompact W<768`, `isDrawer W<992`,
  `isWide W>=992`. Touch sizing: below 576 the control height `CH` becomes 44 px (menus rows,
  buttons, inputs) vs 30 px otherwise (ERP script `CH`, `menuRow`).
- Density is fixed at "small" for the ERP sheet (str `e_dens_fixed`, "this is a screen people
  read for eight hours"): root sets `--fh:30px; --ffs:12.5px; --fpx:9px`. The component library
  has a Small/Medium/Large density toggle; the ERP pages do not.
- Canvases are React "design components" (`<x-dc>`, `dc-import`, `sc-if`, `sc-for`) run by
  `support.js`; not production code. Each frame is one frozen state at one breakpoint.
- The sheet toolbar drives Theme (Light/Dark), Language (EN/العربية, flips `dir`), Breakpoint and
  Calendar (Gregorian, Umm al-Qura, Ethiopian). The ERP sheet persists its tab and scroll position
  in `localStorage` keys `tm-erp-pages-tab`, `tm-erp-pages-y<n>` (sheet chrome only).

### 2.4 Shell (`Shell.dc.html`, ERP tab 01 "Application shell", `images/shell.png`)

Structure (str `e_shell_d`, `g_shell_note`): "no side column, two rows totalling 72px, and one
launcher overlay that both the Menu button and the search field open". Row two is primary
navigation carrying the user's own pins, "rather than a fixed product taxonomy", and "the shell
owns the pin control for the current page". The tenant name, not the user name, is the bold line
in the session button "because posting into the wrong company is the failure this shell must
prevent". Section header code chip shows `app.tellma.com/b/101`.

Row 1, app bar (`g1`): `min-height 40`, `padding-inline 10`, `gap 9`, bottom border
`rgba(255,255,255,.08)`; background `--ink-900` (prop `inkTop`, default true; a light variant
uses `--surface-card` and is forced off in dark theme, so dark theme is always ink). Contents in
order (inline-start → end):
- Wordmark `assets/tellma-wordmark-white.svg` (ink bar) or `-color.svg` (light bar), `height 14`,
  `margin-inline 6`. Below 768 the mark `assets/tellma-mark.png` (`height 18`) replaces it.
- Grouped control (`gGroupBox`): one bordered box `height var(--field-height-sm)` = 30,
  radius 6, fill `rgba(255,255,255,.07)`, hairline `rgba(255,255,255,.12)`; `flex:1` with
  `max-width 440` at ≥1200, `280` at 992, none below. Inside: the Menu button (`gMenuBtn`: Lucide
  `Menu` 17 + label "Menu", 12 px/500, fill .10, seam border) then the search field (`Search` 14,
  placeholder "Search pages and records", 12 px, `#7C939B`) and, at ≥1200 only, a `Ctrl K` key cap
  (`gKbdKey` 17 px tall, radius 4, 11.5 px). Focused/typed: fill .10, focus border, halo
  `0 0 0 3px rgba(76,160,182,.22)` (ERP copy `gGroupOn`: `0 0 0 2px ink, 0 0 0 4px teal-400`),
  caret teal-400.
- 768–991: the field collapses to a 30 px search icon button inside the group (`gIconSearch`).
  Below 768: mark + standalone `Menu` icon button (28×28), flex spacer, standalone search icon.
- Right cluster (`gRight`, `margin-inline-start:auto`, `gap 16`): bell (`gBell` 30×30, `Bell` 16),
  tray (`gTasks` 30×30, `margin-inline-start -8`; icon `Inbox` when `tasksGlyph=tray` [ERP
  default] or `ListChecks` [Shell.dc default]), session button.
- Session button (`gSess` height 30, `gap 8`, `max-width 260`, 420 with markers): avatar circle
  24 px (`--teal-400` fill, ink initials 11.5–12 px/600 on the ink bar); then at ≥1200
  "Farah Nasser" (12 px/400, `#A8BBC1`) · "Banan AE" (12 px/600, `#F2F7F8`); 768–1199 tenant name
  only; below 768 avatar only. No caret. Matches the brief's progressive collapse.
- Tenant markers (ERP "Tenant markers" tile, str `tm_*`): chip "Sandbox" (`FlaskConical`, teal
  tint) and chip "Read-only" (`Eye`, warning tint, `role=button aria-haspopup=dialog`), 20 px
  pills after the tenant name; below 768 they shrink to 24 px icon circles with tooltips.
  Clicking Read-only opens a 280 px popover: "{tenant} is read-only. You can view data but not
  change it. Contact your administrator." Both can show at once; "Sandbox and read-only are
  app-bar markers, not banners" (str `ban_note`).
- No back/forward buttons appear in any frame; only strings exist (`e_pwa_h` "Installed app",
  `e_pwa_d` "Back and forward appear only when the browser chrome is hidden") plus icons
  `icBack/icFwd`. No help button and no assistant launcher are drawn.

Badges (ERP "App-bar badges" tile, str `b_note`, `ar_*`):
- Badge (`bdg`): absolute, `inset-block-start -1`, `inset-inline-start 15`, `min-width 16`,
  `height 16`, `padding-inline 4`, pill, `600 12px/16px`, `direction ltr`, 2 px halo in the bar
  color. Caps at "99+".
- Bell: no badge when zero; accent (`--color-primary`, "N new"); attention (`--error` fill,
  "needs attention"); job running = spinning 30 px SVG ring around the bell (r 13.5, stroke 1.5,
  teal-400 dash `22 63`, 1.6 s linear; static under reduced motion), combinable with a count.
- Tray: neutral count (ink-800 fill + hairline) when all seen; accent count when one is unseen.
- Accessible names: "Notifications, 3 new, needs attention, 1 job running"; "My assignments,
  4 open, 1 new" (separator ", ").
- Rule: "Opening the bell clears its count. The tray keeps its count and only turns neutral."

Row 2, pinned bar (`g2`): `min-height 32`, `padding-inline 8`, `gap 3`, `overflow hidden`;
background `--ink-800` when `inkPinsRow` (ERP default true; Shell.dc default false = white bar).
- Pin (`gPin`): `height 24`, `padding-inline 9`, radius 6, 12 px, `#C6D4D8`. Current page
  (`gPinOn`): full height, 500 weight, teal-300 `#7EC8DC` text + 2 px bottom border.
- Folder pin ("Month end" + `ChevronDown`) after a 1×16 separator; overflow item "··· More"
  (`Ellipsis`) when pins do not fit (ERP `gOverflow` W<1200).
- Inline-end control: one `Pin` icon (or `Star`, prop `pinGlyph`, default `pin`), 24 px. Strings
  exist for "Pin this page", "Pinned to Daily", "Manage pins", "New group", "Rename", "Unpin",
  "Drag to reorder or move between groups", "Pin"/"Pinned" (str `e_pin_*`, `g_pin_*`) but no
  manage-pins panel, add-pin popover or pin-folder dropdown is drawn.
- Pin states (ERP "Pins row · states"): Current; Hover (fill .08); Focus (2 px ink gap + 2 px
  teal-400 ring); Moving (dashed border + grip); No access (lock icon, dimmed); empty row
  "Nothing pinned yet. Pin one from the launcher." Overflow menu (190 px) lists hidden pins; a
  no-access pin shows lock + "No access".
- Below 768: the pins row shows at most two rounded chips (24 px pill, 11.5 px; active chip teal
  tint + teal-400 border), the active pin moved first.

Banners (ERP "Banners" tile, `Shell.dc` prop `banner`): one row between the app bar and the
pinned bar (`banRow`: `padding 7px 12px`, 12 px, tone `-bg` fill + `-border` bottom).
- Kinds: `offline` (error, `WifiOff`, "Connection lost. Reconnecting…", no dismiss);
  `critical` (error, `Wrench`, "Maintenance in progress. Tellma is read-only until 15:00 UTC.");
  `update` (info, `RefreshCw`, "A new version of Tellma is available." + "Reload" + dismiss);
  `maint` (warning, scheduled maintenance + "Learn more" + dismiss); info ("The new VAT return
  format is now available." + dismiss).
- Precedence: 1 offline, 2 critical maintenance, 3 update, 4 scheduled maintenance, 5 info.
  "One banner at a time, under the app bar and above the pinned bar, pushing content down. Within
  a level the earliest goes first and the pager reaches the rest" (pager "1 of 3", 24 px
  prev/next). Dismissing shows the next; a lower banner arriving later only updates the count.
- ARIA: error tone `role=alert aria-live=assertive`; others `role=status aria-live=polite`.
- Unused strings: `g_imp` "Acting as Farah Nasser / Changes are logged under your own account. /
  Stop" (impersonation banner), `g_offline` "You are offline, please check your connection. /
  Retry", `e_ver_h` "A new version of Tellma is ready. / Refresh to load it. Unsaved work is
  kept. / Refresh".

Abandoned shell options: the ERP script still carries styles for options A–F (dark sidebar
216 px + 244 px drawer, 44 px icon rail + flyout, horizontal nav + mega menu, browser-like page
tabs + "Go to page" command palette, two-tier rail, light sidebar + bottom tab bar). Only the
two-row option (styles `g*`, labelled "A · two rows") is rendered. The DS readme still says
"App shell: dark ink sidebar" (stale).

### 2.5 Launcher and global search (ERP 1a, `launcher.png`, `launcher-typed.png`)

- One overlay (`gLch`) under the shell rows, full width, `max-height 96%` (full height below
  768), card surface, bottom border, `--shadow-lg`; scrim `rgba(0,23,34,.34)` over the page.
  Opened by the Menu button and by focusing the search field.
- Browse state: band "Recently visited" + chips (`gChip` 24 px pills, 12 px, subtle fill); below
  768 an extra "Pinned" band of teal chips first. Divider. Section columns (`gLchCols`): 1 column
  <768, 3 at 768–1199, 4 at ≥1200 (the 4th, Administration, only renders at ≥1200 in the frame),
  `gap 0 26px`, `padding 12px 14px 14px`, scrolls.
- Section header (`gSecH`): optional 14 px icon (prop `launcherSectionIcons`, default true:
  `Scale` Financials, `Users` Sales, `ShoppingCart` Purchasing, `Settings` Administration),
  12 px/600, underline. Link (`gLink`): `min-height 22`, 13 px. Highlighted (`gLinkOn`):
  `--surface-selected` + 2 px inline-start primary marker + 500 weight.
- Sections drawn: Financials (Sales invoices, Journal vouchers, Payments, Trial balance, Chart of
  accounts, Bank reconciliations), Sales (Customers, Items, Stock on hand, Goods receipts),
  Purchasing (Purchase orders, Purchase invoices, Goods receipts, Employees, Payroll),
  Administration (Users, Centers, Settings, Invoice numbering). Strings also define Inventory and
  People. No "Reports" launcher entry is drawn.
- Footer (`gLchFoot`, subtle fill, 12 px): "Type to search · ↑↓ to move · Enter to open · Esc to
  close". Unused: `g_lch_pinhint` "Marked pages are pinned" + style `gStar` (a pin mark on
  launcher links).
- Typed state ("inv"): flat list grouped by bucket labels (`gBkt` 12 px/500): **Pages** (Sales
  invoices [highlighted, trailing "↵ Enter"], Purchase invoices, "Purchase invoice · Create" with
  `Plus`), **Records** (`INV-2026-0431  Rawabi Trading · AED 12,400`, `INV-2026-0429  Al Faris
  Group · AED 78,400`), **Settings and help** (Invoice numbering `Hash`, Keyboard shortcuts
  `Keyboard`). Rows `min-height 28`, `padding-inline 14`; title max 60 % + 12 px description.
- Strings not drawn: bucket "Actions" ("Post invoice batch", "Import invoices from file"),
  "Comments" bucket with a quoted comment result ("Omar Halabi on INV-01199"), loading
  "Searching records and comments", partial failure "Record search failed. Pages are still
  listed." + "Retry", empty "Nothing matched “rawabi ltd”.", a "New" pill (`gResNew`).

### 2.6 Session menu (ERP 1a, `session-menu.png`)

- Panel `panelSm` under the app bar at the inline end, `width 268`, radius 10, `--shadow-md`,
  `padding-block 4`. Rows (`menuRow`): `min-height 30` (44 below 576), `padding 6px 10px`, 13 px,
  14 px muted icons.
- Order: user header (28 px avatar, name 13/500, email `farah.nasser@banan.ae`) · tenant card
  (`gTenRow`: selected-teal fill + accent border, `Building2`, "Banan AE" 13/600, sub-line
  "AED · Abu Dhabi" = functional currency + location, "Switch" link) · My account (`UserRound`,
  trailing `ExternalLink`; str `e_myaccount_d` "Opens the identity server in a new tab") ·
  Language (`Globe`, value "English" + chevron = submenu) · Calendar (`CalendarDays`,
  "Gregorian" + chevron) · Theme (`Settings` icon, inline segmented System / Light / Dark,
  20 px segments) · Shortcuts (`Keyboard`, trailing "?" = its key) · About (`Info`, trailing
  "v2.14.0") · separator · Sign out (`LogOut`).
- Strings not drawn: `e_prefs` "Preferences", `e_signout_d` "Clears the offline cache",
  `e_cult_fmt` "Number and date format" with values "English (Saudi Arabia)" / "العربية
  (السعودية)" (a locale selector separate from language).
- No Shortcuts dialog, no About dialog and no language/calendar submenu are designed.

### 2.7 Notifications panel, assignments tray, toasts (ERP 1a tiles, PNGs)

Panels open as a sheet under the app bar (`gSheet`: `inset-block-start 41`, full width, its own
`--shadow-lg`). Only 374 px phone-width mini frames exist; wide-screen anchoring and width are not
designed. In `notifications-panel.png` the bell shows the job ring while the panel is open.

Notifications panel (`nPanel`):
- Header (`nHead`, min-height 44): "Notifications" 14/600 · "Mark all as read" link 12/500 ·
  `Ellipsis` 28 px button opening a 220 px menu: "Clear read notifications", "Notification
  settings".
- "In progress" section (`nProg`, subtle fill, `gap 12`): label + "My jobs" link; job = title
  13/500 + percent 12/500 tabular + 4 px bar (`--color-primary` on `--border-subtle`) + detail
  "5,580 of 9,000 rows". States: running (determinate), waiting ("Waiting to start",
  indeterminate bar `tmind` 1.4 s, static under reduced motion), held ("Held" warning chip +
  "Paused after a service interruption"). Hover/focus shows "Cancel job" (`X` + ink tooltip) on
  waiting and running jobs. "At most 3 jobs show", then "+2 more in My jobs".
- Rows (`nRow`: `padding 10px 12px`, `gap 10`): severity icon 15 px (`CircleCheck` success,
  `TriangleAlert` warning, `CircleX` error, `Info` info) · title 13/600 ellipsis (read rows
  400 + secondary color) · detail 12 px · optional action link 12/600 ("Download") · meta:
  relative time ("2 min", "20 min", "1 h", "Yesterday") + 6 px unread dot. Hover/focus shows a
  "Clear" `X` button.
- Footer (`pnFoot`, subtle fill): "See all notifications" (that page is not designed).
- Empty: `CheckCheck` 22 px + "You're all caught up" (the jobs section still shows above it).
- Rules (str `n_note`): "Opening the bell clears its badge. Clicking a row marks it read and opens
  its target. A later event about the same document replaces its row, and an alert leaves once
  its condition clears."
- Sample kinds: export ready (+Download), balance alert, import failed ("Sales invoices.xlsx ·
  12 errors"), approval info ("Payment PAY-00341 was approved / Approved by Layla Sabri").

Assignments tray (`my-assignments.png`):
- Header "My assignments" + count pill "4 open" (`pnOpen`). No mark-all, no menu.
- Rows: document-type icon (`ShoppingCart` PO, `Wallet` payroll, `BookOpen` JV, `FileText`
  invoice) · title "Purchase Order PO-00218" (unseen = 600 + "New" tag 12/600 primary; seen =
  500) · line 2 "Sara Ahmed: For your kind approval" (assigner: comment) or "Returned by Omar
  Haddad: corrected cost center" or a document summary "Al Faris Group · SAR 12,400.00" · age
  ("1 h", "2 d", "3 d", "5 d") + unseen dot.
- Footer "See all assignments". Empty: `Inbox` 22 px + "Nothing is waiting on you".
- Rules (str `a_note`): "Opening the tray marks items as seen, so its badge turns neutral. The
  count stays until the work is done or handed on. See all opens My assignments, with Assigned to
  me and Assigned by me tabs." That page is not drawn.
- Unused older strings (`e_tk*`): due labels "Today", "Thu", "Overdue" (`pnDueOver` error color),
  "Next week", "4 not done", "See all tasks", "Completed".

Toasts (ERP "Toasts" tiles, str `tst_note`):
- Stack (`tStack`) at `inset-block-start 48` (under the app bar), `inset-inline 8` in the phone
  frame, `gap 8`. Toast (`tst`): card, radius 10, `--shadow-lg`, padding `10/12/6`, icon + title
  13/600 + detail 12 px + optional action 12/600 + 28 px close `X`.
- "Newest on top, at most 3", then a "2 more · Open notifications" pill. "Timers pause on hover or
  focus, and toasts never take focus. Errors stay until closed and are announced assertively, the
  rest politely. Approvals and shares never pop a toast."
- Variants: "PO-00218 assigned to you" (accent `Inbox`, "Open"); "3 documents assigned to you ·
  View" (6 s); warning alert (6 s, no action); own-action feedback "Export started / We'll let
  you know when it's ready / Track" (4 s); error "Import failed · View" (sticky).

### 2.8 Home page (ERP tab 02 frame 2a "Work first, default", `home-page.png`)

- Section chip `/b/101` (the tenant base URL is the home page; no `/home` segment). Intro: "Where
  the wordmark takes you. Tenant and user configurable, so the default layout has to be the
  strongest one." Strings name two more variants never drawn: `e_home_b` "B · dashboard",
  `e_home_c` "C · launcher".
- Greeting block (`homeBody` padding 14/16, 11/10 below 576): h3 "Good morning, Farah" 16/600;
  sub-line 12 px "Tuesday, 19 Jun 2026 · Riyadh plant" (weekday, date, then a center or location
  name; the source of "Riyadh plant" is not explained).
- Body (`hmCols`): full-bleed band on `--surface-subtle` with top border; grid `1.35fr 1fr`
  (single column below 768), `gap 10`, padding 14/16.
- Main column: card "My assignments" + count pill 12 (`cardCount` 16 px teal pill) + "View all"
  link; card "Assigned by me" + 3 + "View all". Card (`card`): radius 10, hairline, header
  `padding 8px 11px` 13/600. Queue row (`qRow`): `min-height 30`, 13 px, icon 14 muted ·
  document number 13/500 link color (`INV-01284`) · description 12 px secondary ("Rawabi
  Trading · Riyadh plant", "Returned to you with a comment") · amount `flex 0 0 120px` tabular
  right-aligned ("SAR 78,400.00") · age (min-width 34, "2 min", "1 h", "Yesterday"). Assigned-by-me
  rows use a `Send` icon. Str `e_aw_d` "Oldest first" (sort hint, not drawn), `e_sent_n` "3
  documents".
- Side column: 2×2 KPI tiles (`kpi`: padding 10/11, label 12 px, value 16/600 tabular, sub 12 px):
  Cash on hand "SAR 4,182,900 / +2.4% vs last month"; Receivables "SAR 1,264,500 / 18 days
  average"; Revenue, month to date "SAR 2,905,140 / 64% of target"; Overdue invoices "7" (error
  color) / "SAR 318,200 past due". Then card "Recently visited" with rows (`scRow`): page icon +
  page name (Sales invoices, Trial balance, Centers, Customers). The brief says to ignore the
  report widgets (KPI tiles) for v1.
- Spec note (str `e_home_note`): "The home page draws from one widget vocabulary: a queue list, a
  counter tile, a chart card, and a shortcut grid. A tenant rearranges them; it does not get new
  components." A 12-bar "Revenue by month / Last 12 months, functional currency" chart card and a
  "Shortcuts" chip grid / launch-card grid (`launchCard`, dashed "add" card) are styled in the
  script but not rendered.
- Difference from the brief: the brief lists 10 assigned, 10 assigned-by-me and recently visited;
  the design shows 5 and 2 rows plus counts (12, 3), with "View all" links.

### 2.9 Search page (ERP tab 03, `SearchPage.dc.html`, search-page PNGs)

URL chip `/b/101/customers`; intro: "One page per top-level entity: search, filter, sort, page,
select, and get to a record. Grid on wide screens, tiles on narrow, tree when the entity is
hierarchical."

Layout (`content`: `padding 12px 14px` [10 below 576], `gap 9`, page surface):
- Row 1 (`s3Row1`, min-height 30): title h3 "Customers" (16/600 in ERP, 17 px [15 below 576] in
  SearchPage.dc) · spacer · search field (`srchQ`: `flex 0 1 230–240px`, min 160, height 26–30,
  focus ring when active; full width below 576) · "Filters" button (`Filter` icon + label +
  `ChevronDown`; icon-only below the label breakpoint).
- Row 2 (`s3Row2`, min-height 30, wraps): primary "+ New customer" (`btnPri` 28–30 px, label
  hidden below the label breakpoint → "+" square) · "Export" (`Download` + `ChevronDown` menu:
  "Export grid", "Export for import"; str `s_exp_title`) · "Import" (`Upload`) · below 576 the
  Export/Import pair collapses into one `Ellipsis` icon button · spacer · "Columns" (`Columns3`) ·
  refresh icon button (`RefreshCw`) · paging text "1-20 / 55" (tabular, `direction ltr`) · prev
  (disabled style on page 1) and next 26–30 px squares · page-size menu "20 per page" (label
  "20" at 992–1199 in ERP; str `s_pgsize_title` "Page size") · 1×18 separator · segmented
  Grid (`Table2`) / Tiles (`LayoutGrid`) toggle (labels only at ≥1200).
- Label breakpoints differ between files: ERP inline frames show labels at ≥992 and full labels
  at ≥1200; `SearchPage.dc` shows labels at ≥576 and full labels at ≥1200.
- The brief's "import template" export item and a Tree|List toggle button are not drawn (strings
  `s_view_list` "List", `s_view_tree` "Tree" exist).

Grid (`sgBoxFill`): full-bleed (negative margins), top border, fills the remaining height and
scrolls in both axes (`overflow auto`, thin scrollbar) so the scrollbar never spans rows 1–2.
- Columns (`SG7`): `28px 84px minmax(0,1.5fr) minmax(0,1fr) 74px 106px 56px` (fixed widths
  below 576: `28 84 150 110 74 106 56`, horizontal scroll). Checkbox column 28 px.
- Header (`sgHead`): `--surface-sunken`, bottom `--border-default`; cells height 30, 12 px/500
  (11.5 in SearchPage.dc) secondary text, padding-inline 8. Sorted column (`sgHeadCellOn`):
  600, strong text, `--surface-selected` fill (ERP; SearchPage.dc omits the fill) + `ArrowUp` 12
  primary. Filterable columns show a `Filter` 14 icon after the label: muted when inactive,
  primary when a filter is applied (Type column in `shell.png`). Numeric headers right-aligned,
  boolean centered.
- Rows: height 28, 13 px (12.5 in SearchPage.dc), zebra (`--surface-card` / `--surface-subtle`),
  1 px `--divider`. Cell kinds: code (secondary, tabular, ltr-isolated), name link (500, primary
  teal), enum tag (`sgTag` 19 px sunken pill), muted currency code, number (right, tabular,
  ltr-isolated), boolean centered `Check` 13 success-green (empty when false).
- Row states: selected (`sgRowSel`: `--surface-selected` + 2 px inline-start primary marker);
  "Came back from this record" (`sgRowReturn`: `teal-50` + teal-400 marker; first row in
  `shell.png`); keyboard focus (`sgRowFocus`: 2 px focus-color outline, offset -2). Hint string
  `s_kbd_hint` "↑↓ to move · Enter to open · Space to select" (not drawn).
- Checkbox 13×13, radius 3; checked = primary fill + `Check` 10.
- The name column is styled as a link though the brief's default is click-anywhere (brief open
  question A vs B); the design is compatible with both.

Selection state (ERP 3b, `search-page-typed.png`): row 2's leading group is replaced by
"3 items selected" (12/500 strong) · "Actions" menu button · `X` button (clear). Header checkbox
checked. Actions menu (`s3SelMenu`, 190 px, radius 10, `--shadow-md`): Export (`Download`),
Export for import (`Download`), Activate (`Power`), Deactivate (`Power`), separator, Delete
(`Trash2`). Columns, refresh, paging and page size remain; the Grid/Tiles toggle is absent in the
selection frame.

Tiles (ERP 3c, `search-page-tiles.png`, `-narrow.png`; `SearchPage.dc` `tlBox`):
- Grid `repeat(auto-fill, minmax(220px, 1fr))`, `gap 10`, padding 14 on `--surface-subtle`;
  below 576 a single column, no gaps, tiles separated by bottom dividers (a list).
- Tile: `padding 9px 11px`, `gap 10`, card fill, 1 px `--border-subtle`, **radius 0** in
  SearchPage.dc (the ERP `s3Tile` style uses radius 6; the PNGs show square tiles). Image box
  40×40 (placeholder `Image` 16 icon, radius 0); body: name 13/500 primary (top-start), code 12 px
  secondary (bottom-start), status badge (bottom-end): "Active" success pill
  (`--success-bg`/`-border`, 11.5/500) or "Inactive" sunken pill. "Came back" tile: selected
  fill + `teal-200` border. Only 3 of the brief's 4 corners are used (top-end empty); ERP `s3TR`
  style (tabular amount top-end) exists.

Tree view (ERP 3d, Centers): columns `28px minmax(0,1fr) 56px` (checkbox, Name, Active).
Twisty `ChevronDown`/`ChevronRight` 13 px (mirrored in RTL) with 14 px indent per level; leaves
get an empty 14 px slot. Search "ware" state: matches show a 5 px teal-400 dot + 600 strong text;
ancestors show secondary text ("Ancestor of a match"); legend under the grid. Spec note
(str `s_note`): "the toggle is sticky per entity per user, so a manual choice outlives the
breakpoint. Sorting is what arbitrates tree versus list: any order-by drops a tree entity into
list view and clearing it returns to the tree, unless list was chosen by hand. Filtering keeps the
tree shape and shows ancestors as context … ancestors stay muted and unselectable-looking."

Empty and error states (ERP "Empty and error states"; `s3State` centered, 22 px icon, title
14/600, detail 12 px max 42ch, buttons):
- No data: `Inbox` + "No customers yet" / "Add one to get started." + primary "New customer".
- No matches: `SearchX` + "No customers match this search" / "Clear the filters or widen the
  search." + "Clear filters".
- Invalid filter (client/validation error): search box shows red border with `Filter` icon and
  the bad expression `CreditLimt > 100000`; state `CircleAlert` + "This filter expression is not
  valid" + mono error chip `CreditLimt` + "Unknown property CreditLimt. Check the spelling and try
  again." + "Edit filter". So the advanced-filter expression is echoed into the search-field slot.
- Server error: `CircleX` + "The server could not complete this search" / "Try again. If it keeps
  failing, send this reference to support." + mono chip "Reference 8f2c-41ae" + "Try again".
- No loading/skeleton state for the grid is drawn (skeleton bars `sk*` exist only as page filler).

Column filter popovers (ERP "Column filters", `search-grid-filters.png`): the header cell gets a
2 px primary bottom border while open; popover `fPop` 248 px, padding 10, `gap 8`, radius 10,
`--shadow-md`; footer `Clear` (secondary) + `Apply` (primary small), divider above.
- Text: operator select ("Contains" default; strings: Starts with, Ends with, Contains, Equals)
  + text input. The brief's "not equals" has no string.
- Number and date: operator "Between" + From/To labeled inputs (right-aligned tabular). Strings:
  Less than, At most, Greater than, At least, Between, Equals; no before/after wording for dates.
- Enum: checkbox list (Service, Operation, Sale) — multi-select.
- Boolean: radio True / False.
- No entity-picker (navigation property) filter is drawn.
- Unused alternative style `fpop*` (216 px, operator chips) exists.

Advanced filters: the "Filters ▾" button and strings exist (`s_adv` "Filters", `s_adv_open`
"Advanced filters, opened", `s_active` "Active only", `s_qx_ph` "Filter expression", `s_qx`
"CreditLimit > 100000"), styles `advRow`, `srchQx`; the popup itself is **not drawn**.

Columns editor (ERP "Columns", `choose-columns.png`; `cePop` 520 px, radius 10, `--shadow-md`):
- Header "Choose columns" 14/600 + "5 shown" count; search field "Search columns".
- Two panes (`ceBody` 1fr 1fr): "Available" tree (rows 30 px: twisty 13 px, checkbox, name;
  navigation properties carry a type pill, e.g. "Parent center" + "Center", expandable lazily to
  Code, Name, Manager [itself expandable]); "Shown" list (grip `GripVertical` + checked box +
  name; dragging row lifted with border + `--shadow-sm`).
- Footer: "Reset to default" link + primary "Done". No custom-expression entry is drawn (brief
  asks for one). Strings mention related columns "Center name", "Center manager", "Currency name"
  (label convention unclear vs the brief's "Center / Code").
- An older alternative `cc*` column chooser (add/remove icons, path separators) is styled but
  not rendered.

### 2.10 Details page (ERP tab 04)

Intro: "One entity, fully editable. The same header, action row and record navigation carry every
entity type; the body is what changes." Spec note (str `d_note`): "View and edit modes share one
grid, so fields stay in place when the mode flips. Record navigation, dirty state and the action
row sit in the same two-row budget as the search page." URL chips: `/b/101/centers/482`,
`/b/101/users/97`, `/b/101/sales-invoices/1284`.

- Row 1 (`dRow1`, min-height 30, nowrap): back crumb = entity plural "Centers" (16/500 primary,
  a link back to the search page) · `ChevronRight` separator (mirrored) · record title h3 16/600
  ellipsis ("Jeddah sales") · state badge: "Active" (`dBadgeOn` teal pill with `Check`),
  "Inactive" (`dBadgeOff`), workflow state "Posted" (`dStepOn` solid primary pill), or
  "Unsaved changes" (`dDirty` warning pill with 6 px dot) in edit mode.
- Row 2 (`dRow2`, min-height 30, gap 7), view mode: primary "Edit" (`PenLine`) · "New" (`Plus`) ·
  "Clone" (`Copy`) · "Delete" (`Trash2`) · "More" (`Ellipsis`) · spacer · refresh · "3 / 293" ·
  prev/next. Labels hide below 1200 (icons only) and secondary buttons collapse into More below
  992. User page: Edit · New · "Deactivate" (`Power`) · More. Invoice page: primary is the next
  workflow action "Close document" (`CircleCheck`), then Edit · New · Clone · More. Inactive
  record: primary "Activate" + Edit.
- Row 2, edit mode (4b): primary "Save" (`Check`) · "Cancel" · spacer · "3 / 293" with both
  arrows disabled; refresh hidden.
- Body (`dBody`): full-bleed `--surface-subtle` band with top border, padding 14 (10 below 576),
  cards `gap 10`. Card (`dCard`): radius 10, hairline, no shadow. Field grid (`dGrid`): 2 columns
  (`1fr 1fr`, gap `10px 18px`, padding 11), 1 column below 768. Field: label 12/500 secondary
  (+ optional `CircleHelp` 12 help icon or `Lock` 12 for read-only), value 13 px strong
  (`min-height 26`); link values (navigation properties) 500 primary; muted values secondary.
- Edit inputs (`dInput`): `min-height 30` (44 below 576), padding-inline 8, radius 6, 1 px
  `--border-default`; focused = focus border + `--focus-ring`; select/picker = trailing
  `ChevronDown`; invalid = 1 px `--error` border. Help text under a field (`dHelpTip` 12 px):
  "Used by import, export and the API. Must be unique."
- Collapsible section card "Audit info" (twisty; collapsed in view mode, shown in the edit frame):
  Created by (link), Created at "19 Jun 2026, 09:14", Modified by, Modified at.
- Tabs inside the main card (`dTabs`, 28 px, active = primary text + 2 px underline; tab counts
  12 px muted): User: Details, Role memberships, Notifications, Permission check. Invoice: "Invoice
  lines 3", "Attachments 2".
- User page (4c): 72×72 image box (radius 6) with edit/delete overlay buttons (20 px, ink 72 %
  fill); fields Name (E), Name (ع), Sign-in email (locked, muted, `farah.nasser@tellma.com`),
  Contact mobile, Contact email, Last active. Role memberships table (Role link, Notes).
  Notifications tab = matrix (Notification × Email / SMS / Push checkboxes; rows "Assigned to
  you", "Comment on your document", "Document returned"). Permission check tab = pick
  "Sales invoices · Read" → Result "Granted" + reasons ("Role · Sales supervisor", "Filter · Center
  = Jeddah sales", "Public permission · no filter").
- Invoice page (4d): workflow chips row (Draft ✓, Requested ✓, Approved ✓, Posted [current],
  Closed [future]) joined by 14×1 bars; header fields (Serial number, Sales date, Sales center
  link, Memo); lines grid (`dLineHead` `minmax(180px,1.6fr) 86 78 96 118 100 118`: Item link,
  Quantity, Unit, Unit price, Amount (VAT excl.), VAT, Line total; zebra; horizontal scroll);
  totals row (Amount, VAT, Total, tabular). Inline-end aside "History" (`hAside` 272 px, card
  surface, own scroll; below 768 it becomes a card under the form): items with 24 px round
  icons (signature/state = teal tint; others sunken): "Posted by Farah Nasser 2 min ago",
  "Layla Sabri assigned to Farah Nasser", a comment with quoted body, "Line 2 quantity 40 → 48",
  "Memo changed"; comment box "Add a comment" at the bottom. Extra cards: "Related documents"
  (`Link2`; links "Purchase order PO-00218", "Payment PAY-00341") and "General ledger" (Account,
  Center, Debit, Credit, Memo). Strings also name "Signatures", "State history", "Edit
  history", "Attachments" ("signed-po-00218.pdf", "delivery-note.jpg", "Add file").
- Dialogs and states (4e; dialog `dDlg` `min(320px,100%)`, padding 14, radius 16, `--shadow-lg`,
  scrim `rgba(0,23,34,.32)`, title 13/600, body 12 px, footer right-aligned):
  - "Leave without saving?" / "This record has unsaved changes. They are lost if you leave now."
    → ghost "Discard changes", primary "Stay".
  - Concurrency conflict "This record changed while you were editing" / "Layla Sabri saved a newer
    version 3 minutes ago. Reload to take theirs, or overwrite with yours." → ghost "Overwrite",
    primary "Reload theirs".
  - "Delete this center?" / "Jeddah sales and its two child centers are removed. This cannot be
    undone." → "Cancel", danger "Delete" (`--error` fill).
  - Save failed: an inline error banner at the top of the page (`dBanner`: error tint, title
    "Save failed", body "Code must be unique. C-OPS-JED already belongs to another center.") and
    the offending input with a red border. Not a modal (the brief specifies a modal for 422).
  - Missing record: centered state "This center no longer exists" / "It was deleted by another
    user. Return to the list to pick another." + primary "Centers".
  - Inactive record: "Inactive" badge, primary "Activate".
- Not drawn: create mode, clone result, full-page save spinner, 5xx dialog with trace id,
  400 message modal, 422 capped error list modal, child-row deep-link flash, paging loading.

### 2.11 Report studio and report renderers (ERP tab 05, `ReportStudio.dc.html`)

Placement: rendered inside the normal two-row shell (pin "Grant spending" active), URL chip
`/b/101/report-studio/37` (a numeric definition id). Intro (str `r_d`): "Author a report on the
left, see it run on the right. The far left lists every field on the root entity, with navigation
properties as sub-trees that load when expanded. Drag a field onto rows, columns or dimensions to
add its path, onto measures to add its sum, or into filter or having to paste its path at the
pointer. Drag the divider to resize." Frames: 5a pivot (dragging a measure), 5b list, 5c summary
(dragging into Having), 5d chart (dragging a dimension).

Studio top bar (`bar`, light card surface, `min-height 46`, padding 8/14, wraps): back arrow 28 px
· title "Grant spending by project" 15/600 (14 below 576) · meta "Unsaved changes" 12 px · spacer
· below 768 a Definition / Preview segmented toggle · "Share" (`Share2`, hidden below 576) ·
"Discard" · primary "Save" (28 px buttons, 12.5 px).

Split (`split`): dark definition panel | 7 px divider | preview.
- Dark panel palette `D` (anchored ink, does not flip with theme): bg `#001722`, field `#0A2530`,
  line `#163542`, strong `#F2F7F8`, body `rgba(242,247,248,.80)`, muted `.58`, expression text
  `#A3D3DD` (teal-200), teal `#4CA0B6`, selected `rgba(76,160,182,.16)`. Expressions use Noto Sans
  Mono 11.5/1.5, always `direction ltr`.
- Width: default 560 at ≥1200, 500 below; clamped `max(420, min(w, 66% of W))` and
  `max-width calc(100% - 340px)`; preview `min-width 320`. Divider: `role=separator
  aria-orientation=vertical`, pointer drag (RTL-aware), `col-resize`, 3×28 grip. Below 768 no
  divider; the segmented toggle swaps panel and preview.
- Fields tree column (`tree`, bg `#00121B`): width 200 (≥1200), 180, 136 (below 576), collapsible
  to a 36 px rail with vertical label "Fields" (`PanelLeftClose/Open`). Head: "Fields" title,
  "Root entity" select ("Journal entry lines"), "Search fields" box. Rows 26 px, 12.5 px, 14 px
  indent per depth, type icons (`Hash` number, `Type` text, `Calendar` date, `Link2` navigation in
  teal); navigation rows have twisties (lazy). The dragged field shows a teal outline + grip.
  Sample fields: Id, Value, Direction, Quantity, Notes, Document › (Code, Posting date, Memo,
  Created by ›), Account ›, Center ›, Currency ›, Agent ›, Resource ›.
- Definition form column (collapsible to a rail "Definition"): "Report type" segmented control
  with four kinds and icons: **List** (`List`), **Summary** (`Sigma`), **Pivot** (`Grid3x3`),
  **Chart** (`ChartColumn`) — the design's names for the brief's non-aggregate / aggregate flat /
  aggregate pivot / chart.
- Sections (`pSec`, padding 12/14/14, header icon teal + title 12.5/600 + count + 22 px "+"
  add button). Items (`itBase`): grip + label (12.5 px) over mono expression + tags on the end
  (18 px pills). Drop target state: section tinted `rgba(76,160,182,.07)` with a 2 px inline-start
  teal bar and a dashed "Drop to add" item.
  - Pivot: Rows (Account `Account` [Tree, Auto-expand]; nested attribute Code `Account.Code`
    [Attribute]) + toggle "Show rows total"; Columns (Project `Center` [Auto-expand]) + toggle
    "Show columns total"; Measures (Amount `Sum(Value * Direction)` [SAR · 0 decimals]; drop
    "Quantity → Sum(Quantity)"); Filter (code block); Having (empty "None").
  - List: Select (Voucher `Document.Code`, Date `Document.PostingDate`, Account `Account`,
    Project `Center`, Amount `Value * Direction` [SAR · 2 decimals]); Filter; Order by
    `Document.PostingDate desc`; Top `500`.
  - Summary: Select (Account, Project, Amount `Sum(Value * Direction)` [Aggregate, SAR · 0
    decimals], Lines `Count(Id)` [Aggregate]); Filter; Having `Sum(Value * Direction) > 100000`
    (drag frame pastes `Sum(«Quantity»|)` at the caret: "Dropping pastes the field path where the
    pointer lands."); Order by `… desc`; Top `50`.
  - Chart: Chart type chips Column / Bar / Line / Pie; Dimensions (Month
    `Month(Document.PostingDate)` + "Add a second dimension" hint, or dropping Center); Measure
    (single, no add); Options toggles "Show legend", "Show data labels" (26×15 switches); Filter.
  - Filter sample: `Account.Type = 'Expense' and Document.PostingDate >= @FromDate and
    Document.PostingDate <= @ToDate` (`@params` rendered bold strong).
  - Parameters section (`AtSign` icon) with note "Added automatically as you type @ in an
    expression." Cards: key `@FromDate` (mono 600) + type tag "Date" + "Required"; rows Label
    "From date", Default `StartOfYear(Today())`; `@ToDate` default `Today()`. No Label2/Label3,
    no requirement toggle UI, no type-override UI drawn.
- Not drawn in the panel: Code, Title/Title2/Title3, Description(s), per-term Label2/Label3,
  number-format editor (only the tag), order direction per dimension (string "Descending" exists),
  flat-vs-tree option for tree dimensions (tag "Tree" only), sharing UI ("Share" button only).

Preview (`prev`, `--surface-subtle`, scrolls): header band (card surface, padding 14/18/16): title
17/600, description 12.5 px max 72ch ("Expense accounts by project for the selected period.
Select a cell to see its journal lines."), parameter fields (168 px each, label 12/500 with red
`*` for required, 30 px box with `Calendar` icon, "1 Jan 2026"/"30 Sep 2026") and a "Refresh"
button (`RefreshCw`). Output band below (padding-block 14, edge-to-edge table, 1 px block borders).
Footer strings exist ("9 rows · 3 columns · updated 10:42", "8 of 1,214 lines · updated 10:42")
but are not rendered. No Export to Excel control anywhere.

Pivot renderer (`pv*`):
- Columns `minmax(190px,1.7fr) 60px repeat(4, minmax(96px,1fr))` (fixed below 576). Two sticky
  header rows (top 0 and 30): row 1 = corner (span 2) + column-dimension caption "Project ▾"
  (span 3, centered, 600) + total column; row 2 = "Account ▾" (sticky inline-start) · "Code"
  (attribute column) · Education · Health · Water · "Total". Header cells 30 px, 12/500 on sunken.
- Body cells 29 px, 12.5 px, padding-inline 10, numbers tabular right-aligned ltr-isolated.
  Row label column sticky at inline-start; indent 18 px per depth; group rows get `ChevronDown`.
- Level styles (`LV`): group (sunken bg, 600, strong), leaf (card, 400, body), own-postings
  phantom child "Program costs (direct)" (card, 400, secondary color), grand total (sunken, 600,
  2 px `--border-strong` top border). Row-total column: 600, 1 px inline-start border, subtle bg
  on leaf rows. Only 3 total styles are used (group, own, grand). Spec note (str `r_note`):
  "Pivot totals are computed on the server for every row and column pair; a parent row shows its
  own postings as a direct child so it always equals the sum of what sits under it."
- Drill affordance: hovered cell = hover fill, primary text, underline (offset 3), 1 px accent
  inset ring.

List and summary renderers (`tb*`): sticky header row; zebra rows 29 px; List columns `96px 108px
minmax(200px,1.6fr) minmax(110px,1fr) 120px` (Voucher link, Date, Account "5101  Field staff
salaries", Project, Amount); sorted header shown 600 strong (no arrow icon). Summary columns
`minmax(220px,1.6fr) minmax(120px,1fr) 130px 80px` (Account, Project, Amount, Lines); hovered
row = `--surface-hover` (drill target). Summary shows no totals row.

Chart renderer (`ch*`): plain bar chart drawn in CSS (not a library): plot 250 px (200 below 576),
dashed `--divider` gridlines at 0/100k/…/400k, axis labels "100k" muted 12 px; bars `max-width 44`,
teal-400, top radius 3, hovered bar `--color-primary` with tooltip card ("Aug 2026 / SAR 365,000",
`--shadow-md`); month labels under the plot. The chart area forces `direction ltr` (time axis does
not mirror in RTL) while labels follow `rtl`. Legend/data labels off. No color palette beyond
teal (DS: "No rainbow categorical palettes") — multi-series or pie colors are undefined.

### 2.12 Dashboards, settings, users/roles/centers, tenant picker

- Dashboards: **not designed**. Only the home page's KPI tiles and an unrendered "Revenue by
  month" chart card style hint at widget visuals. No dashboard grid, refresh indicator ("last
  refreshed"), dashboard parameters or force-refresh states exist.
- Settings page (tabbed categories): **not designed**. "Settings" appears only as a launcher link.
- Users: details page designed (ERP 4c, see §2.10). Users search page not drawn (Customers is the
  stand-in). Roles: no page; only "Role memberships" tab and the "Permission check" tab on Users.
  Centers: view/edit/dialog states (ERP 4a, 4b, 4e) and tree search (ERP 3d).
- Tenant picker, tenantless shell (wordmark + email + user name + language selector): **not
  designed** in the ERP sheet. The session menu's tenant card + "Switch" link is the only tenant
  switching UI. (Identity server pages, §2.14, have their own centered-card layout.)
- About dialog, Shortcuts dialog, error dialogs for 400/5xx with trace id + copy button, a full
  page save spinner, "send to support"/state dump/screenshot UI, a request log: **not designed**.
  The only support affordance is the "Reference 8f2c-41ae" chip on the search server-error state.

### 2.13 Component library canvas (`Component Library.dc.html`, `uploads/library-components.md`)

Specimen sheet of the already-specified `@tellma/core-ui` components (selectors `tm-form-field`,
`tmInput`, `tmNumber`, `tm-date-picker`, `tm-entity-picker`, `tm-select`, `tm-checkbox`,
`tmButton`, `tm-spinner`, `tm-alert`, `TmModal`, `tm-popover`, `tmTooltip`, `tm-menu`,
`tm-tab-group`, `tm-image`, `tm-dropzone`/`tmFilePicker`, `TmFilePreview`, `tm-grid`,
`tm-tree-grid`). Facts the round-2 pages lean on:
- Density knob (library only): sm `fh 30 / ffs 13 / fpx 10`, md `38 / 14 / 12` (default), lg
  `46 / 15 / 14`; menu/list row heights 26/30/34. The ERP sheet pins its own small density at
  `30 / 12.5 / 9` (not identical to library sm).
- Button intents primary, secondary, ghost, danger × sm/md/lg (30/38/46 px). Pending keeps its
  size, is not disabled, swallows activation (str `bt_note`). Disabled primary = `--grey-200` fill
  + muted text. Icon-only buttons are square at field height.
- Validation messages are popups anchored to the focused control, never rows under the field;
  out of focus the field keeps a red border and an in-field `CircleAlert` icon (str `ff_popnote`;
  Ahmad circled this option in `uploads/draw-05aa…png`). Only the first error shows.
- Alert tones info/success/warning/error with optional heading; hidden severity prefix ("Error:",
  "Warning:"). Icon semantics (str `al_iconnote`): `CircleX` = an operation failed or was refused
  (nothing to fix in place), `CircleAlert` = fixable input, `TriangleAlert` = an undecided
  decision (destructive confirm).
- Modal (`TmModal`): sizes small (confirm) / medium (record editor) / large (viewport minus a
  margin); all go full screen on narrow viewports; header 11/12/11/16 padding + divider, footer
  pinned, subtle fill, buttons at the end (`Cancel` then `Delete`); destructive confirm shows a
  26 px error-tinted `TriangleAlert` disc. Nested modals allowed (≤2 levels advised); a guard can
  veto dismissal (the unsaved-changes pattern). Panel shadow `0 16px 40px -12px rgba(0,23,34,.4)`
  (not a DS token).
- Popover: content lazy (created on open, destroyed on close); focus moves in; Escape, outside
  click, tab-out close. Popover shadow `0 8px 24px -8px rgba(0,23,34,.28), …` (not a DS token).
- Tooltip: plain text, `--surface-inverse` fill, 11.5 px, max 200 px; delay on hover, immediate on
  focus, long-press on touch; always the host's description.
- Menu: 30 px rows, reserved icon slot, trailing shortcut hint (shown as `⌘X`, `⌘C`, `⌘V`), dismiss
  on outside press; Escape innermost-first across layers (library doc §1.4).
- Tabs: active = strong text + 2 px `--accent` underline (ERP details tabs use primary text +
  primary underline instead); overflowing strip scrolls with an edge fade, no pager; only the
  active panel exists unless the tab preserves its content; vertical variant.
- Grid modes: "Ruled" (editable line entry: row headers, vertical rules, range, cut, find match,
  resolving cell, new row, error popover, find bar "3 of 17", status bar 30 px with "3 errors",
  "2 cells resolving", selection count) and "Plain" (readonly list screens, no vertical rules,
  zebra). Loading overlay "Loading…" with spinner; empty "No records to display". Readonly grid is
  a single tab stop (str `gr_note`). The search page grid follows the Plain mode.
- Tree grid: twisty pointer-only (keyboard Alt+→ / Alt+←), reserved spinner slot for lazy loads,
  "load failed" chip per node.
- Library doc rules: digits follow the locale (Arabic-Indic where appropriate); stored dates are
  always Gregorian ISO; ICU plurals; overlays in the top layer with flip.

### 2.14 Identity server canvas (skim) and the PNG inventory

- `Identity Server.dc.html` designs spec 0003's pages at `identity.tellma.com/Account/*`,
  `/Authorization/Consent`, `/DeviceVerification/Verify`, `/Error/Error` and the self-service
  `/Manage/Index|Passkeys|Sessions|ExternalLogins|EnableAuthenticator`. Two sign-in shells (A
  split-screen ink panel, B centred card); its responsive break is **900 px** (not in the ERP
  set). The `/Manage` shell has a top bar with a language selector and "Back to Tellma", side nav,
  sign out in its footer. The session menu's "My account" lands here.
- PNGs in `.tmp/images/` map 1:1 to ERP frames: `shell.png` (ERP 1a default, xl), `launcher.png`
  (browse), `launcher-typed.png` ("inv"), `session-menu.png` (374 px mini frame), `notifications-
  panel.png`, `notifications-and-badges.png` (jobs/row states, empty states, app-bar badges),
  `my-assignments.png`, `home-page.png` (2a, no browser chrome), `search-page.png` (3a),
  `search-page-typed.png` (3b selection + Actions menu), `search-page-tiles.png` (3c xl),
  `search-page-tiles-narrow.png` (3c xs), `search-grid-filters.png`, `choose-columns.png`,
  `tellma-icon-dark.png` (128 px transparent mark: white "a" + teal dot, for dark grounds),
  `tellma-wordmark-dark.png` (white letters, for dark grounds), `tellma-wordmark-light.png` (ink
  letters, for light grounds). Design assets: `assets/tellma-wordmark-white.svg`,
  `-color.svg`, `tellma-mark.png`, `tellma-app-icon.png`.
- `uploads/pasted-1788667942577-0.png` is an older iteration: notifications as an "Inbox" dropdown
  anchored at the inline end on a wide screen (~400 px, "3 new" error-red pill, "See all"), the
  only wide-screen anchoring evidence for the panels.

## 3. Gaps for round 2

Pages, panels and states the round-2 specs need that the design does not draw (each must be
designed in the spec's own words, consistent with §2 tokens):
- **Tenant picker and tenantless shell** (brief: wordmark, email, user name, language selector,
  per-tenant "Connect", search, names in all tenant languages): not designed at all.
- **About dialog** (versions of SPA, backend, platform, packs; copyright; terms/privacy links) and
  **Shortcuts dialog**: only menu rows exist ("About v2.14.0", "Shortcuts ?").
- **Language and calendar submenus** (chevrons drawn, contents not); sign-out confirmation and
  cross-tab sign-out state not drawn.
- **Back/forward buttons** for installed-PWA mode: strings only, no placement.
- **Help button and assistant launcher** slots in the app bar: absent (brief says "comes later";
  the right cluster has no reserved slot).
- **Pin management**: "Pin this page" popover, folder (group) dropdown contents, manage-pins
  editor, rename, drag-to-reorder between groups, and the setting to hide the pinned bar.
- **Wide-screen panel anchoring** for notifications, assignments and session menu (only 374 px
  frames; the session menu width 268 is given, the two panels are not).
- **"See all notifications" page, "My jobs" page, "See all assignments" page** (with "Assigned
  to me" / "Assigned by me" tabs): named, not designed.
- **Assignment details beyond the tray**: due dates (strings only), reassign/hand-on, done,
  comment threads; the assignments back end data the tray needs (document type icon, number,
  assigner, comment, age, seen state, "returned by").
- **Home page**: greeting rules (time-of-day variants beyond "Good morning"), the "· Riyadh
  plant" sub-line source, empty states for each card, loading skeletons, per-role/user swapping.
- **Dashboards**: everything (grid layout, reflow, silent refresh, "last refreshed" line, force
  refresh, dashboard parameters, report title overrides).
- **Settings page** (tabbed categories, permission-filtered tabs): not designed.
- **Roles page** (search + details), **Users search page**, **Centers search page in list mode**.
- **Search page**: advanced-filter popup (Queryex text + "Active only" + distro custom filters,
  server validation errors), the "Import template" export item, the Tree|List toggle control,
  date "before/after" operators and "not equals", entity-picker column filter, custom-expression
  entry in the columns editor, grid loading state, paging-in-progress state, cancelled-query
  feedback, keyboard type-to-search focus state.
- **Details page**: create mode and its URL state, clone, full-page save spinner, 400 modal, 422
  capped-errors modal (the design shows an inline banner instead), 5xx dialog with trace id and
  copy button, child-row drill-through flash, loading state, extras panels' scroll restoration.
- **Report studio**: Code, Title/Title2/Title3, Description(s), per-term Label2/Label3, format and
  type overrides, parameter requirement/type override editing, flat-vs-tree toggle for tree
  dimensions, order direction, totals styles beyond 3, sharing dialog (users/roles, "who can see
  it and why"), publish to roles, the "Reports" launcher entry with My reports / Shared with me /
  All reports, Excel export control, drill-through list page, studio validation errors.
- **Report hosting**: report as a top-level page (not the studio), as an embedded card on a
  details page, and on a dashboard; parameters strip collapse; chart types other than column
  (bar, line, pie, two-dimension series) have no visual, and the one-accent rule leaves
  multi-series colors undefined.
- **Supportability UI**: UI version copy, slow-request trace ids, request log, state dump,
  screenshot, send-to-support consent dialog. Only a "Reference …" chip exists.
- **Offline / stale cache states** beyond the "Connection lost. Reconnecting…" banner and the
  "A new version of Tellma is available. Reload" banner (e.g. contract-fingerprint mismatch from
  spec 0016 §3.10, warm-launch-from-cache indicator if any).
- **Loading states** generally: no skeletons for page data (brief rule 2A requires an instant
  loading state); only spinner specimens and grey skeleton bars used as filler.
- **RTL specimens of the chart**: the CSS chart forces `ltr` for the plot; whether time axes
  mirror is undecided.

## 4. Likely change points

Where a round-2 design will probably have to edit an existing spec or choose between the design
and the brief:
- **Spec 0011 §5.5 / routes (owner 0024, 0027)**: the design's URL shape `app.tellma.com/b/101/…`
  (a `/b/` segment, no distro subdomain) conflicts with the brief's
  `<distro-slug>.app.tellma.com/<tenant-id>/…` and spec 0011's `/{tenantId}/api|hub|blobs|mcp`
  and `PublicOrigin https://acme.app.tellma.com`. Home is drawn at `/b/101` (no `/home`), which
  bears on brief open question 2. Route slugs drawn: `/customers`, `/centers/482`, `/users/97`,
  `/sales-invoices/1284`, `/report-studio/37`.
- **Spec 0021 (owner 0028)**: the panel design needs per-row "Clear", "Clear read notifications",
  "Mark all as read", "Notification settings", unseen dot vs read weight, severity icons, an
  action link per row ("Download"), "replaces its row" dedup, alerts that leave when their
  condition clears, running jobs with % and row counts, waiting/held states, Cancel job, a "My
  jobs" link, max 3 jobs then "+N more". Spec 0021 already has `inbox/summary` (unseen, max
  severity, latest 10, running jobs), `seen`, `read`, `read-all`, `clear`, `clear-read`,
  `OnDuplicate Replace/Ongoing`, `SummaryCountCap 100`, `SummaryLatestCount 10`; check that
  per-row action links, job progress text ("5,580 of 9,000 rows") and a per-job cancel from the
  panel are covered (spec 0020 has `ProgressPercent`, `ProgressKey`, `cancel`, `Held`).
- **Toast rules (owner 0028)**: "Approvals and shares never pop a toast" conflicts with the brief's
  "the other user gets a notification" for report shares only if a toast was intended; the bell
  still gets the row.
- **Tray badge semantics (0028)**: count = open assignments (persists until done or handed on),
  accent when any unseen, neutral when all seen; requires a seen-at stamp for assignments distinct
  from the inbox's `InboxSeenAt`.
- **Spec 0014 / 0018 / 0032 (stock pages)**: the Users details design shows a Notifications
  channel matrix (Email/SMS/Push per notification type), a Permission check tab with reasons
  (role, filter, public permission), and "Sign-in email" locked; Center has "Center type"
  (Service, Operation, Sale), Parent, Code with uniqueness help. Verify those fields/endpoints
  exist; the design implies a "permission check" API not in the brief.
- **Spec 0015/0016 save errors (owner 0031)**: design shows an inline "Save failed" banner + field
  borders, and an optimistic-concurrency dialog ("Reload theirs" / "Overwrite"), and "This center
  no longer exists" (404 on refresh). The brief specifies modals for 422/400 and does not mention
  concurrency; an overwrite path needs a version/ETag in the save contract.
- **Delete of tree entities (0018/0031)**: "Jeddah sales and its two child centers are removed"
  implies cascade delete of children, which may contradict the CRUD stack's tree rules.
- **Search page bulk actions (0030, 0016)**: Actions menu = Export, Export for import, Activate,
  Deactivate, Delete on a cross-page selection; needs bulk endpoints by id list; "Export for
  import" appears both in the Export menu and the selection menu.
- **Page size (0030)**: a "20 per page" selector is drawn; the brief does not mention page size.
- **Tree vs list (0030)**: the design rule "any order-by drops a tree entity into list view" and
  "toggle is sticky per entity per user" are designed behavior to adopt or reject.
- **Reports (0033/0034)**: kind names List / Summary / Pivot / Chart answer brief open question 1
  at the UI level; the studio URL is per definition id; Studio top bar has Share / Discard / Save
  and an "Unsaved changes" state; parameters are typed `@Key` with Default expressions like
  `StartOfYear(Today())` and `Today()` (Queryex functions to confirm in spec 0008); attribute
  columns (`Account.Code`) only under a single row dimension; "own postings" phantom child row.
- **Launcher data (0027)**: sections with icons, "Recently visited" chips, typed buckets Pages /
  Records / Settings and help (+ Actions, Comments in strings) and a "Purchase invoice · Create"
  command row: the brief scopes v1 search to pages only, so records/settings buckets are future.
- **Breakpoints (0024)**: the design's closed set 390/576/768/992/1200/1440 and the 44 px touch
  height below 576 are the natural answer to the brief's "fixed and closed set of width
  thresholds"; the identity server uses 900.
- **Theme (0024/0025)**: DS has `[data-theme=dark]` only; "System" needs a
  `prefers-color-scheme` listener; the app bar is ink in both themes.

## 5. Contradictions and surprises

- **URL shape**: every frame shows `app.tellma.com/b/101/...` (no distro subdomain, a `/b/`
  prefix, numeric tenant 101). The brief and spec 0011 use `<distro>.app.tellma.com/<tenant-id>/`.
  Treat the design URL as illustrative only.
- **Brief image captions swapped**: the brief labels `tellma-wordmark-light.png` "on a dark
  background", but that file has ink letters (for light grounds); `tellma-wordmark-dark.png` has
  white letters (for dark grounds). The shell uses the white wordmark on the ink bar.
- **Tray icon disagrees across files**: ERP Pages default `tasksGlyph=tray` (Lucide `Inbox`, as in
  the PNGs); `Shell.dc` default `checklist` (`ListChecks`). The PNGs are authoritative: `Inbox`.
- **Pinned bar color disagrees**: ERP default `inkPinsRow=true` (ink-800 bar, as in the PNGs);
  `Shell.dc` default `pinsInk=false` (white bar). PNGs: ink.
- **Session button**: the brief says user name collapses first, then tenant name; design matches
  (≥1200 both, 768–1199 tenant only, <768 avatar only) and makes the tenant name bold. The design
  also adds tenant markers (Sandbox, Read-only) the brief does not mention.
- **Notifications for the badge vs brief**: brief (via research) separates bell and tray; design
  adds "attention" red badge for alert severity and a spinning ring for running jobs, plus banners
  (offline, maintenance, update, info) with precedence and a pager, and toasts with timing rules,
  none of which the brief describes.
- **Assignment tray copy uses Title Case document types** ("Purchase Order PO-00218") while the
  rest is sentence case; the readme rule (sentence case except proper nouns) suggests "Purchase
  order".
- **Minimum text size**: DS readme says minimum 12 px, but Shell.dc and SearchPage.dc use 11.5 px
  (session line, kbd cap, chips, grid headers, tile badges) and 12.5 px cells; ERP Pages copies
  use 12/13. The two component files and the ERP inline frames disagree on several metrics
  (title 16 vs 17 px, cells 12.5 vs 13 px, buttons 26/28 vs 30 px, label breakpoint 576 vs 992).
- **`--white` value**: tokens say `#FEFEFE`; the readme's Cards paragraph says `#FBFBFB`.
- **Readme says "dark ink sidebar"** for the app shell; the chosen shell has no sidebar.
- **Search grid name column is a link** (teal, 500) although the brief's default is
  click-anywhere-on-row (open question A vs B) — visually compatible with both; tiles also render
  the name as a link.
- **Search page "Filters" vs brief**: brief places the filter button in row 1 (matches) and the
  grid/tiles toggle, columns and paging in row 2 (matches) but the design adds a page-size menu
  and, in the selection state, hides create/export/import and the view toggle.
- **Selection label**: brief "N rows selected"; design "3 items selected".
- **Details page actions**: brief puts Deactivate, Delete and Clone under an Actions menu; design
  shows Clone and Delete (or Deactivate) as visible buttons plus a "More" menu, and a workflow
  primary action ("Close document"). Brief title "Customers / Olive Inc." vs design crumb
  "Centers › Jeddah sales" with a chevron and a state badge.
- **Details save errors**: brief says a 422 shows a capped modal then field borders; design shows
  an inline page banner ("Save failed …") and field border, no modal. Design also adds a
  concurrency-conflict dialog and a deleted-record state the brief does not mention.
- **Validation popups**: field errors are popups on focus (library rule), so "adds the errors of
  every field to that field" means red border + in-field icon, with text only on focus.
- **Delete copy implies cascading** child-center deletion ("and its two child centers are
  removed").
- **Home page**: shows 5 + 2 rows with "View all" (brief: top 10 each), KPI tiles the brief says to
  ignore, and a location/center after the date ("· Riyadh plant") the brief does not mention.
  Home URL is the tenant root `/b/101`.
- **Launcher shows Records and Settings-and-help buckets and a create command** ("Purchase invoice
  · Create"), but the brief says v1 searches only launcher pages. The footer hint "Type to search ·
  ↑↓ to move · Enter to open · Esc to close" matches the brief's keyboard model.
- **Report kinds**: design names them List / Summary / Pivot / Chart (brief: non-aggregate,
  aggregate flat, aggregate pivot, chart). The pivot sample uses label "Project" for expression
  `Center`, i.e. labels are free text over paths.
- **Report studio lives inside the tenant shell** (with pins) and has its own 46 px top bar: total
  chrome above the studio is 72 + 46 = 118 px.
- **Chart colors**: the DS forbids rainbow categorical palettes, but pie and multi-series charts
  need several hues; no palette exists. The chart is hand-drawn CSS, so it fixes only the look
  (teal bars, dashed gridlines, tooltip card), not the library.
- **Arabic digits**: AR strings use Arabic-Indic digits (`١-٢٠ / ٥٥`, `SAR ٤,١٨٢,٩٠٠`) while grid
  cells in the same frames use Latin digits; AR paging strings even disagree in order
  (`e_paging '٢٠-١ / ٥٥'` vs `s_paging '١-٢٠ / ٥٥'`). The digit policy for ar-SA must be decided.
- **Umm al-Qura in the canvases** is a tabular-Islamic approximation (str file comment "within a
  day"), and an Ethiopian calendar option appears although the reference distro uses Gregorian +
  Umm al-Qura only.
- **Shortcut hints mix platforms**: "Ctrl K" in the app bar, `⌘X/⌘C/⌘V` in the menu specimen,
  "?" for the shortcuts dialog.
- **Shadows**: component specimens use ad-hoc shadows (`0 16px 40px -12px …`, `0 8px 24px -8px
  …`) outside the DS ladder `--shadow-xs..lg`.
- **RTL slip**: `SearchPage.dc` `rowReturn` uses `inset 3px 0 0` (physical left) instead of the
  logical marker helper `MK()` used in ERP Pages; RTL must use the logical variant.
- **Leftover options**: ERP script still contains six abandoned shell options (A–F) and unused
  home variants; only the rendered frames are design intent.


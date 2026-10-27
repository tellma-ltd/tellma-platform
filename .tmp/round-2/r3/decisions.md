# Review round 1 — decisions (binding)

Ahmad reviewed specs 0010, 0023 and 0024 and decided every item below on 2026-10-26. The analyses
behind each item, with their edit plans, are in `.tmp/round-2/review-r1/` (g1 = 0010, g2 = 0023,
g3/g4/g5 = 0024). Where this file and an analysis disagree, this file wins. Two items need a design
first; their design notes are `.tmp/round-2/r3/design-measures.md` and
`.tmp/round-2/r3/design-locale.md`, and they win over the analyses on their topics.

Apply every decision in every spec it touches (new specs 0023–0035, spec 0010, existing specs
0011–0022 and `ARCHITECTURE.md`). Log every change to 0010–0022 and ARCHITECTURE.md for the change
report.

## Spec 0010 Part F and the report engine

1. **Leaf grain in SQL, every rollup in C#** (g1 item 1). Cut §F.8 (grouping sets) and §F.9 (tree
   level navigations) and everything that exists only for them: `TreeLevels`, `Level{j}` paths,
   `Query.LevelNotSelectable`, `Entity_Level`, QX5008, the report `levels` field, `CubeTruncation`,
   the truncation notice, `TooManyLevels`. Trees always show full depth.
   - **Expressions over aggregates stay** (`sum(a) / sum(b)`, `1 - avg(x)`): Ahmad's ruling.
     Aggregate calls are extracted from every measure, their decomposable partials are queried in
     SQL at leaf grain, and the surrounding expression is evaluated in C# per cell. The supported
     subset of Queryex for the C# evaluator, its numeric semantics, the engine API that exposes it
     (a new Part F item) and the save-time refusal of anything outside it are defined in
     `design-measures.md`.
   - **`min`/`max` over text and GUIDs stay**, with a practical C# comparison (exact for GUIDs,
     approximate for text) defined in `design-measures.md`.
   - Non-decomposable aggregates (distinct count, median, percentile) are out of scope; none exists
     in Queryex today.
2. **`'uq'` emission: three forms** (g1 item 2). Row-grain operands keep the lateral seek;
   path-free operands become a hoisted prologue variable (0008 §12.4's mechanism); aggregated
   operands and constant statements keep the scalar form. QX3104 goes once goldens confirm. The
   join-shape benchmark runs before Part F is implemented (flag: flip if the variable is > 1.5×
   slower than a bound parameter).
3. **`having` on the finest cells, visual totals** (g1 item 3): `having` is a plain SQL `HAVING` on
   leaf rows; every subtotal and total is the measure recomputed over the surviving leaf rows;
   members with no surviving rows disappear; the phantom child exists only when the node's own row
   and something below survived. Subtotal drills may include facts hidden by `having` (0033 flag 5).
4. **Summary totals** (Ahmad's ruling): a summary's totals row is computed in C# with the same
   evaluator over the rows that come back after both `top` and `having`, and only when the whole
   result fits without paging. Otherwise the summary shows no totals row (the renderer says why).
   The exact rule is in `design-measures.md`.
5. **Final Part F numbering.** §F.0 frame; §F.1–§F.7 unchanged; §F.8 omitted parameters (was
   §F.10); §F.9 constant statements `EvaluateConstant` (was §F.11); §F.10 `BindParameters` (was
   §F.12); §F.11 `'uq'` (was §F.13); §F.12 `FilterTree.Exists` (was §F.14); §F.13
   `ParameterUse.ComparedWith` (was §F.15); §F.14 and on: the new measure-decomposition item(s)
   from `design-measures.md`. Every citation in every spec moves to these numbers.

## Spec 0023 and the component-library amendments

6. **Icons** (g2 item 1, as recommended): every library glyph draws through `tm-icon` with `lucide`
   data and `mirrorInRtl` for directional glyphs; exceptions `tm-spinner` and the grid's per-cell
   check and dash masks. New amendments §B.7, §C.13, §D.11, §E.5; 0023 §3.1 pins stroke 1.75 and
   drawing in the creating pass; §C.10's menu icon type gains `TmIconSource`.
7. **Keys** (g2 item 2, as recommended): fix `kbd` first (punctuation fallback, AltGraph guard,
   Shift not compared for symbols, `tmKeysText`), then the grid's keymap, find bar and hints use
   it (§C.12, §C.10, Part C tests); a lint rule refuses platform sniffing outside `kbd`.
8. **Avatar colours** (g2 item 3, as recommended): `tm-avatar` `colorKey`, `tmAvatarTone` (FNV-1a,
   8 tinted tones), the platform passes the record key `{entity}:{id}`; a semantic `categorical`
   token group (new §B.8) that 0034's chart palette also reads. A different colour per tenant is
   accepted (close 0023 flag 10; no question remains).
9. **Busy overlay** (g2 item 4, option B, Ahmad: "dim with a 200 ms delay as long as there is
   another instant feedback"): at 0 ms the page is inert with `cursor: progress` and the caller
   shows its trigger busy in the same frame; callers with no visible trigger pass `immediate:
   true` (scrim, spinner and label at 0 ms); otherwise the scrim follows at 200 ms. Sign-out and
   closing action dialogs use `immediate`.
10. **Search feedback** (g2 item 5 plus Ahmad's ruling): the search field fires on the first key
    after a pause at once (leading edge) and debounces the following keys (trailing 50 ms), as
    spec 0006 does; the progress bar starts when the search fires, holds while the result is
    stale, and stays on screen at least 250 ms. 0034's reloading bar follows the same rule.

## Spec 0024 and its cascades

11. **No `tellma-client init`.** A `dotnet new tellma-distribution` template will scaffold server and
    client together; it belongs to a later distribution-tooling spec (a non-goal here, named as
    such in 0024 and 0011 §1.2; ARCHITECTURE.md says so). 0024 lists the files a distribution
    owns; the template parity test moves to the tooling spec.
12. **No `check` command**: `build` runs the source checks in its first step and the output checks
    in its last, each with a problem code (Appendix A becomes "Build problem codes").
13. **No client-only `hide` of `entity:`, `widget:` or `report:`.** Which entities, widget types and
    coded reports exist is the server's; the client follows the metadata. A server-side switch to
    remove a pack entity is a later non-goal.
14. **Stay on Angular's service worker**, on the record: a Decisions row, a review flag (fork
    fallback; flips if Angular refuses a needed fix or drops the package), and the Web Push path
    (a later wrapper script that imports `ngsw-worker.js`, cited from 0021's push flag).
15. **Sign-out waits first** (g3 item 5): overlay at once; "Waiting for a save to finish…" while any
    tab is busy (≤ 60 s); then ask only if a draft is still dirty or a save still runs; a confirmed
    sign-out never asks again; no Cancel button.
16. **Launch redirects** (g4 item 6 plus Ahmad's item 9):
    - After sign-in with target `/`, the BFF applies the single-membership rule and redirects to
      `/{id}`; the SPA keeps the same rule for an in-app `/`; both test one shared vector file.
    - An expired session ALWAYS returns to the last tenant: `tm-boot.js` builds
      `returnUrl=/{lastTenantId}` before it clears the record.
    - An automatic redirect that meets `404 tenant-not-found` goes to the tenant picker silently;
      a link the user followed still shows the notice. The mechanism is in `design-locale.md`.
17. **`TmUrls`** (g4 item 7): every builder uses the routed tenant; `forTenant(id)` returns a scoped
    builder; outside a tenant a builder throws in development and returns `/tenants` with
    telemetry in production; `TmApi` refuses a tenant route with no tenant.
18. **`decimal` and `long` travel as JSON strings** (g4 item 8, both): full scale, no exponent;
    inbound accepts a string or a number; the lossless parser and `TmDecimal` class go; the client
    type is a branded decimal string with helpers; `restrict-plus-operands` lint. Reverses 0016
    Decision 5 (RFC 7493).
19. **Time zones** (g4 item 9 plus Ahmad's item 10): absolute date-times show the GMT offset label
    when the display zone's offset differs from the device's (grid headers once; relative times
    bare); plus a one-time prompt when the device's zone CHANGES, offering to set Tellma's zone to
    it, with a "do this automatically" checkbox remembered per user per device, reversible on My
    profile next to the time-zone field. With the option on, the client updates the server
    automatically when the device zone changes. Design in `design-locale.md`.
20. **Long routes and retry classes in the contracts chunk** (g4 item 10 plus Ahmad's item 11): a
    `Long` flag on `[EntityAction]`/`[ApiAction]` drives the server's `tellma-long` policy and the
    emitted list; the chunk also carries each route's retry class (idempotent or not), so `TmApi`
    picks the watchdog itself and refuses `read` on a non-idempotent route (throws in development,
    never retries in production). The `long` call option stays only for hand-mapped routes.
21. **`forbidden` names the permission and the reason** (g5 item 11): `ForbiddenException` gains
    `Reason` (`no-grant`, `outside-filter`, `restricted-path` with `Path`); the 403 carries them;
    0024 §11 shows the three messages with the permission's labels and the raw pair for support;
    `TmPermissions.label`. No roles, filters or other users in the error.
22. **412 and a failed update download** (g5 item 12): decide from `Tellma-Build` by SemVer
    precedence at once (newer → client-outdated; older → server-outdated; same version other sha →
    ask the worker, 3 s cap); `check()` gains `failed` and `unreachable`; Reload runs `update()`
    with a busy state, "Couldn't download the update…", then "Reload anyway"; the rollback
    tie-break; `TmAppVersion.compare`.
23. **Acme's examples become credit control** (g5 item 13): the rename table in g5 applies at every
    listed place (page `credit-review`, coded report `acme-credit-exposure`, and the rest).
24. **Regional formats** (g5 item 14 plus Ahmad's item 13): a per-user Region and a tenant default
    Region, plus a digits choice so Saudi formats with Latin digits are supported (per user, with
    a tenant default). Design in `design-locale.md`.
25. **White labelling** (g5 item 15 plus Ahmad's item 14): optional resx keys
    `Distribution_DisplayName`, `Distribution_ShortName`, `Distribution_Copyright` with the config
    values as fallbacks; boot strings and `name_localized` in the manifest; and NO user-facing copy
    in any spec says "Tellma": it says `{displayName}` (the localized display name). The About
    dialog's credit line is the distribution's own `Distribution_About` text.

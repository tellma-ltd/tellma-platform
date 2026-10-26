# Holistic quality review of the round-2 design

Reviewer: principal-engineer pass over `design/00-architecture.md` (incl. §14 rulings) and the
fifteen design notes, read in full and checked against `.tmp/round-2-brief.md`, the sketches in
`.tmp/images`, the style guide and the facts digests. Doubtful browser claims were verified by web
search on 2026-10-20 (see Q-35 to Q-38).

Findings are numbered Q-<n>. Severity: **blocker** (the thirteen specs cannot be written
consistently, or a brief rule is broken), **major** (a wrong, missing or clearly over-built piece
that an author would otherwise copy into a spec), **minor** (fix while writing). Each finding names
the notes and sections, the problem, the evidence and a concrete fix. The **Cut list** at the end is
per note.

Overall verdict: the design is coherent, complete against the brief and unusually well verified.
Its risk is not gaps but mass: the notes average 1,330 lines, 0033 is 1,814, and several of them
specify mechanisms (one-round-trip defaults, arrival engines, drag paths, telemetry metrics) that a
v1 does not need and that the style guide's 600–1,200-line target cannot hold. Two cross-cutting
seams are unresolved and block consistent authoring (Q-1, Q-2).

Counts: 2 blockers, 15 major, 24 minor (41 findings). Estimated cut: ≈ 2,800 lines across the
fifteen notes (≈ 14 %).

---

## 1. Design-rule violations and seams that block authoring

### Q-1 — blocker — The entry-point import rule contradicts five notes; nobody owns the ruling

- **Notes:** A§2.2 (import rule), 0024 §9.2 (route table), 0025 §8.1 + challenge 8 (`TM_REALTIME`),
  0027 decision 1 + challenge 1 (`/shell` via `import()`), 0029 §14.4 + challenge 5
  (`TM_ENTITY_PAGES`), 0032 §1 + challenge 5 (declarations `import()` `/admin`), 0035 decision 24.
- **Problem.** A§2.2 says "the primary imports no other entry point" and "no eager entry point
  statically imports a lazy one". The primary's route table must load `/shell`, `/search`,
  `/details`, `/admin`, `/attention-pages`, `/reports`, `/dashboards`; the primary's entity
  declarations name `/admin` layouts; the picker binding in `/entity` must open `/search` and
  `/details` pages. Each note invents its own workaround (two injection tokens filled by the tenant
  shell, a "dynamic-import exception", a build preload), and four of them ask for "one ruling". No
  ruling exists, so five placement sections will be written against different rules.
- **Evidence.** 0025 challenge 8: "one answer … should serve both"; 0029 challenge 5: "one ruling
  should cover all three"; 0032 challenge 5: "One ruling should cover all"; 0027 challenge 1 proposes
  the text.
- **Fix.** Add one sentence to A§2.2 and cite it from the five notes: *"The rule governs static
  imports. A dynamic `import()` of a lazy entry point from the primary (route loaders, declaration
  `details`/`search`/`component` members, the `/entity` picker pages, the `/realtime` loader) is
  allowed; the build fails on a static import that violates the DAG."* Then delete `TM_REALTIME`
  (0025 §8.1: `TmLeader` imports `@tellma/core/realtime` dynamically itself) and `TM_ENTITY_PAGES`
  (0029 §14.4: the binding imports `/search` and `/details` dynamically). `TmLazyPickerPage` stays.
  Saves ≈ 25 lines and two tokens; removes a seam the tenant shell should never have owned.

### Q-2 — blocker — One URL grammar, four spellings for the same parameter; `view` is split in one note and not in its owner

- **Notes:** A§8 (`?mine=1` for Jobs, `?tab=to-me|by-me` for Assignments, `?tab=mine|shared|all`
  for Reports, `view: grid|tiles|tree|list`), 0024 §9.5 (the codec, `view` four-valued),
  0030 §4.1 + challenge 1 + R4 (`view` + `list=1`), 0030 §2.4 + R15 (`x.mine=1`),
  0028 challenge 3 + §10.1 (`tabs.param`, `?tab=mine|all`), 0034 decision 19 + R3 (`x.tab=`, a
  `hidden` custom filter with no `queryex`), 0035 §12.1 (`?p.*`).
- **Problem.** 0024 owns `TmSearchQuery` and its codec, which pins, recents, `TmUrls.target`,
  notification links and server-built e-mail links all depend on. Three consumers change its
  grammar in incompatible ways: a bare `tab`/`mine` parameter (A§8, 0028), a custom-filter spelling
  `x.mine` / `x.tab` (0030, 0034) and a `tabs.param` option that makes the spelling per entity
  (0028). 0030 also needs `view` split into `view` and `list`, but 0024's sketch still carries the
  four-valued `view`. A pin written by one spec would be parsed differently by another.
- **Evidence.** 0030 §4.1: "A§8's `view` … cannot say 'tree of tiles'"; 0030 R15 asks 0028 for
  `x.mine=1`; 0028 challenge 3 proposes `?tab=mine|all`; 0034 §11 writes `x.tab=…` and asks 0030
  for options "without `queryex`" and `hidden: true` (R3(b)).
- **Fix.** Decide once in 0024 §9.5: (1) `TmSearchQuery` gains `tab?: string` (URL `tab=`), the
  one spelling for page tabs; 0030's `tabs` declaration member has no `param`; the Jobs page uses
  `tab=mine`; 0034 uses `tab=mine|shared|all`; delete `x.tab`, `?mine=1` and the `hidden` custom
  filter. (2) Split `view?: 'grid' | 'tiles'` and `list?: boolean` as 0030 asks; update A§8's table
  and 0025 Appendix A. (3) State in 0024 that every other spec cites the codec and adds no
  parameter outside `x.{key}` and `p.{Key}`.

### Q-3 — major — Rule 1 is not met for the one thing every distribution will write often: an entity action with arguments

- **Notes:** 0031 §10.4 + review flag 12; 0029 §2.2 (`TmActionMetadata.arguments: TmJsonSchema`);
  A§3.6.
- **Problem.** The metadata already carries each action's argument schema (JSON Schema 2020-12,
  exported by `JsonSchemaExporter`), yet an action with arguments "never appears" unless the
  distribution hand-writes a dialog (≈ 17 lines each, 0031 §2). Workflow entities will have many
  such actions (assign with assignee + comment, reject with reason, set limit). A§3.6 lists the
  dialog as *the* path, not the hatch. This is plumbing pushed into distribution repos.
- **Evidence.** 0031 §10.4 "actions with arguments but no dialog never appear (a development
  warning names them)"; 0031 flag 12 names the generated dialog as the alternative; 0029 §12
  already maps every scalar type and entity reference to a control (`tm-value-editor`,
  `tm-entity-field`).
- **Fix.** 0031 §10.4: the default dialog is generated from `arguments` (one `tm-value-editor` per
  schema property, `entity` hint from `ComparedWith`-style metadata or a `[EntityRef]` attribute on
  the C# arguments record, `required` from the schema, 422 mapped by path). `dialog` becomes the
  hatch. Add the attribute to 0015 §11.2 (edit) and the `entity` member to 0029's action metadata.
  ≈ 20 lines in 0031, saves ≈ 17 lines per action in every distribution.

### Q-4 — minor — Rule 2D: three places push content down on a click

- **Notes:** 0031 §8.4 (error summary inserted at the top of the main column after Save),
  0034 §3.3 (`cube.truncated` alert "above the pivot"), 0031 §11.2 (the "restored" notice).
- **Problem.** The brief's rule is "nothing shifts on click or type". A Save that fails inserts a
  summary above the cards, moving every field under the pointer; the truncated-levels alert
  reflows the pivot.
- **Fix.** Reserve the slot: the summary and notices render inside the header's second row's
  reserved band or as a fixed-height strip under the toolbar (one row, scrolling internally when
  it has more than one item), never inside the scrolling band. One sentence each.

### Q-5 — minor — Small brief deviations without a recorded reason

- **Notes:** 0027 §8.2 (not pinnable: `…/new`), 0027 challenge 5 (help/assistant slot order),
  0030 decision 13 (default order shows no indicator), 0027 §6.2 (picker search only from seven
  memberships).
- **Problem.** The brief says "any deep link in the SPA can be pinned" and that the create state is
  in the URL so a refresh lands on create; 0027 excludes `/new` with the reason "edit mode is never
  in a URL", which does not apply to create. The brief lists help and assistant before the bell;
  the skeleton puts them after the tray (both fine, but the brief should be told). The other two
  are judgment calls recorded as flags; fine.
- **Fix.** Make `/{t}/{segment}/new` pinnable (a "New sales invoice" pin is a plausible use);
  keep the drill list and tenantless routes excluded. Record the slot order in 0027's decisions
  with the reason (reserved slots must sit between fixed boxes).

### Q-6 — minor — The brief's "report as another search-page view" seam is not named

- **Notes:** brief "Reports" (studio reusability "as another view in the search page (grid, tiles,
  pivot)"), 0030 §2.5, 0034 §3.6.
- **Problem.** Both hosts exist (`search` hatch; `TmReportHost` with a `draft`), but no note says
  the seam is covered.
- **Fix.** One line in 0034 §3.6: a future pivot view on a search page is `TmReportHost` in
  `card` mode fed a `draft` built from the page's query; nothing else is reserved.

---

## 2. Over-engineering (concrete cuts; see also the Cut list)

### Q-7 — major — One-round-trip parameter defaults cost two engine amendments

- **Notes:** 0033 §4.3, decision 11, review flag 6; 0010-F F.11 (parameter expressions), F.12
  (constant statements).
- **Problem.** To save one round trip when a report applies a default such as `today()`, the
  engine gains per-occurrence expression substitution (F.11) *and* a constant-statement entry
  point (F.12), and the compiler cache key grows by both. Dashboard refreshes are background and
  every other run is a user click; a second round trip is invisible.
- **Fix.** Keep F.12 only. The run executes the constant statement first (one Read batch, two
  round trips only when defaults apply), binds the values as ordinary parameters and echoes them.
  Delete F.11 (0010-F ≈ 30 lines, 0033 §4.3 ≈ 15 lines, plus engine work and cache-key rules).

### Q-8 — major — Client telemetry and support are built like a product, not a v1 relay

- **Notes:** 0026 §5.1 (per-visit INP, layout shift), §4.2 (SHA-1 fingerprints, 3-per-10-min
  budgets), §3.5 (FNV sampling), §6.4 (six metric instruments, alert queries), §10.3 (five-step
  truncation ladder), §12.7 (admin surface with paging records), §11.2 (per-browser capture
  matrix).
- **Problem.** The brief asked for principles, a transport, UI error logging, a request log with
  trace ids, a dump, a screenshot and an opt-in send. The note adds a web-vitals clone, a dedup
  algorithm, a sampling scheme, a metrics catalogue with alert rules and a paged admin API. Every
  one of these is correct and none is needed to ship; together they are ≈ 250 lines and weeks of
  work the relay does not need.
- **Fix.** Keep: envelope, name guard, batching, offline hold, relay with validation, logs as custom
  events, `TmErrorHandler` with redaction, request log with `begin/end`, Copy diagnostics, dump,
  screenshot, support request. Cut: INP and layout shift (keep `chromeMs`, `dataMs`); fingerprint →
  `errorClass + first frame` string, cap 30 error items per 10 min; sampling → one boolean per tab
  from `sampleRate`; metrics → two counters (`errors`, `requests.slow`), alerts out of the spec;
  truncation → one cap with one rule (drop `visible.rows` then `telemetry`); admin surface → two
  routes without a paging record; capture matrix → "Chromium: tab; others: picker; none: attach".

### Q-9 — major — Pins and launcher carry three edit surfaces, a ranking engine and caches for providers that do not exist

- **Notes:** 0027 §8.5–§8.10 (popover, context menu, Manage pins dialog, drag from the launcher,
  measured overflow), §9.4 (match classes A–D, source ranks, recency), §9.6–§9.7 (provider seam
  with placeholders, failure rows, per-provider LRU), §4.3 (reserved and browser key lists).
- **Problem.** The brief's pinned bar is "pinned pages and one-level folders, with a pin-current
  button". The note gives pins a popover, a context menu, a dialog, pointer drag within the bar
  and from the launcher, and a measured overflow with an offscreen copy. Typed search has a
  four-class ranking with per-language source ranks; v1 ships no provider, yet §9.7 specifies a
  2-minute LRU per provider.
- **Fix.** Pins: keep the one-click pin + popover (rename, folder) and the Manage pins dialog
  (reorder-list already gives keyboard and pointer reorder and folder moves); cut the context menu
  (§8.6), the launcher-to-bar drag (§8.9) and the measured overflow (§8.7 → "pins overflow into
  More from the `xl` rule in A§9"). Launcher: ranking = prefix match, then contains, over the
  normalized label and synonyms in the active language and English; order by section then label;
  cut source ranks and recency. Providers: keep the interface and the "append below Pages" rule;
  cut placeholders, failure copy and caching until a provider ships. ≈ 150 lines.

### Q-10 — major — The chart facade is a charting product; v1 needs six chart types and a table

- **Notes:** 0034 §7 (ten types incl. heatmap, treemap, gauge; decals; an Angular tooltip and
  legend with pixel hit-testing; per-point keyboard navigation; a hidden 500-point table and a
  table view; normalized-stack math; `TmChartSpec` schema and validator), review flags 7–8, 12–13.
- **Problem.** The brief: "0, 1, or 2 dimensions and exactly 1 numeric measure + simple chart
  specific customizations". The chart section is ≈ 140 lines and the heaviest build item in the
  round; two of its decisions (Angular tooltip over zrender hit-testing, decal cycling past eight
  series) are fragile.
- **Fix.** Types: `column`, `bar`, `line`, `area`, `pie`, `doughnut` and `card` (gauge, heatmap,
  treemap later; the vocabulary in 0033 §3.1 stays). Tooltip: ECharts' `richText` renderer (canvas,
  no `innerHTML`, no Trusted Types issue) instead of an Angular element; legend: ECharts' own with
  `aria` decals. Accessibility: the "Show as table" view is the keyboard path (it is a real
  `tm-grid`); cut per-point arrow navigation and the hidden table. Keep the spec and validator
  (the assistant needs them). ≈ 70 lines in 0034 and most of the chart engineering.

### Q-11 — major — Dashboards ship seeds, a scheduler with viewport gating, per-widget audience warnings and a pointer layout engine

- **Notes:** 0035 §7.1 (widget warnings via a multi-securable `EvaluateOutcomesAsync` overload),
  §7.3 (dashboard seeds and a provisioning step), §11.1 (IntersectionObserver gate, six in flight,
  jitter, completion-based due times), §13.3 (pointer drag with cascade push and compaction),
  §15.6 (home seam), §15.2 (`tmFirstName` prefix lists).
- **Problem.** The brief's dashboard is a grid of reports with a refresh rate, silent refresh, a
  "last refreshed" line and force refresh. Sharing and read-through come from the research (fine);
  seeds, audience warnings per widget and a viewport-gated scheduler do not. Pointer drag plus the
  menu and keyboard paths is three implementations of one operation.
- **Fix.** Cut dashboard seeds (report seeds stay; a seeded dashboard can follow when a pack needs
  one); cut §7.1 (the share dialog's existing root warnings suffice); scheduler → "a due tile runs
  when the document is visible and online; runs are capped by the `tellma-reports` policy; a
  forced refresh clears"; drop the IntersectionObserver and the six-run cap; pointer drag stays
  (users expect it) but §13.3's cascade becomes "push down, then compact" in one sentence and the
  algorithm goes to code; `tmFirstName` keeps only the Arabic `عبد/أبو` rule. ≈ 120 lines.

### Q-12 — major — Deferred extras need a new standard operation on every stack; prerendered tabs need a 0005 amendment; "Save as new" is a third conflict choice

- **Notes:** 0031 §6.8 + E1–E3 (`extras` operation, `DeferredExtras`), §6.6 + the Part D request
  (`tm-tab-group prerender: 'idle'`), §9.2 ("Save as new"), review flags 9, 14, 16.
- **Problem.** No consumer in this round has a heavy extra (Acme's `history` is invented to
  exercise it). Pre-rendering every tab hidden and `inert` on Edit costs time on large documents
  to reach an error in an unvisited tab, which the summary can reach by opening the tab on
  activation. "Save as new" after a deletion is a path users will rarely hit and support will
  have to explain.
- **Fix.** All extras eager in v1 (cut E1–E3 and §6.8's deferred rows; keep `*tmDetailsExtra`); an
  error in an unrendered tab is listed as text and activating it opens the tab, then focuses; the
  deletion dialog offers Discard only. ≈ 90 lines, one server operation and one amendment fewer.

### Q-13 — major — Client-state micro-mechanisms that a simpler rule covers

- **Notes:** 0025 §5.3 (superseded-tag sets, 100 ms settle window), §9.2 + §9.5 (write-behind
  envelope `{at, v}`, eviction of the oldest 64 keys), §2.4 (daily `savedAt` confirmation, 30-day
  housekeeping), §7.4 (shared `visible` lock), §9.3 (later-intent rule with foreign-overlay expiry).
- **Problem.** Each exists to avoid one wasted sync or one lost column choice. Together they are
  ≈ 120 lines of bookkeeping that every state bug will be traced through.
- **Fix.** (1) Own-write detection: `TmApi` delivers the response body to the caller *before*
  `observe()`; a flush adopts the returned tag under the sync lock first, so the header never
  mismatches; drop the superseded set and the settle window. (2) Preferences: plain JSON for both
  kinds; on `Users.TooManyPreferenceKeys` drop the write with telemetry (the cap is 1,024 keys;
  review flag 10 already expects it to be ample). (3) `savedAt`: written on `put` only; the idle
  rule reads `sync.at` for the tenant partition; no daily re-put, no 30-day sweep (the expiry sweep
  already removes user data). (4) Degraded polling: the leader polls while it is visible or when a
  `leader.probe` arrives (a tab becoming visible already probes); drop the `visible` lock.
  (5) Foreign overlays: last `prefs.changed` wins until the stored bag catches up; drop the `at`
  comparison and the 60 s expiry. ≈ 110 lines.

### Q-14 — major — The attention surfaces add an arrival engine, read-on-visit and job heartbeat alerts

- **Notes:** 0028 §8.2–§8.3 (arming, per-document diffs, baselines, toast keys), §6.5 (read on
  visit), §10.5 + E9 (`jobs/status`, `TmJobsAlerts`), §9.3 ("Connection restored." closing
  message), §11.3 caption rendering.
- **Problem.** The toast rule needs three sentences: a focused tab toasts a row whose `CreatedAt`
  (or `AssignedAt`) is newer than the previous stored copy's newest, never on the first copy after
  load. `jobs/status` and the heartbeat banner are an operations feature the brief did not ask
  for (admins can filter `Status in (Held)`). Read-on-visit writes on reads.
- **Fix.** Replace §8.2–§8.3 with the three-sentence rule plus "a replaced row toasts again";
  cut §6.5, E9 and `TmJobsAlerts`; cut the restored message. ≈ 70 lines.

### Q-15 — minor — Stock pages: a signature pad, a live delivery drill-down and a new endpoint for drift

- **Notes:** 0032 §4.7 + R13 (`tm-signature-pad`), §4.5 (invitation delivery on render), §5.5 +
  E10 (`roles/problems`), review flags 10, 12.
- **Fix.** Signature: upload only (the pad is a 0023 component no other spec uses). Delivery: a
  "Check delivery" button. Drift: an eager details extra `problems` on the Role stack (0031 extras
  exist; no new action, no new route row). ≈ 50 lines.

### Q-16 — minor — Boot pages, a browser-based config check in the build, and a Cookie Store accelerator

- **Notes:** 0024 §4.2 (four boot pages, `tm.login` loop guard), §3.6 + review flag 2 (config spec
  through `ng test` inside `tellma-client build`), §8.8 (Cookie Store `change` events).
- **Fix.** One boot page ("Tellma couldn't start" with Try again) covers unsupported, cookies and
  load failure; keep the loop guard (it prevents a redirect loop). Run `tmCheckAppConfig` as a
  Node step over the emitted Core chunk and the compiled declarations (they are plain objects;
  `tsx` loads `app.config.ts`), not through a headless browser in `dotnet publish`. Drop the Cookie
  Store listener (visibility and per-call checks suffice). ≈ 40 lines and 10–20 s per build.

### Q-17 — minor — Library additions with no consumer or whose detail belongs in code

- **Notes:** 0023 §3.1/§3.7 (`accordion`: no note composes it), §3.6 (`TmSearchField.error` for
  "0030's invalid-filter echo", which 0030 flag 16 says does not happen), §3.9 (token values),
  §3.10 (every Arabic string), §3.11 (story list).
- **Fix.** Cut `accordion` (29 entry points), the `error` input, the token *values* (keep group
  names; values live in `presets/tellma-default.ts`), the Arabic column (the parity gate pins it;
  the Arabic lives in `@tellma/locale-ar`), and the story list. ≈ 110 lines.

### Q-18 — minor — Restated grammar and caret mechanics in the entity model

- **Notes:** 0029 §8.1 (restates 0008 §4's lexical grammar "exactly"), §10.4 (`insertFromDrop`,
  `execCommand` fallbacks, undo pinning), §11.3 (breadth-first search to depth 3, context rows),
  §12.2 (rows for `guid`, `time`).
- **Fix.** Cite 0008 §4 for the lexer; state the drop rule in one sentence (native text drop, then
  `tmQxInsert` re-spaces); path search = "filter the visible tree by label, expanding matches'
  ancestors"; fold rare types into "other: text, read-only". ≈ 50 lines.

### Q-19 — minor — Search-page rules that cost more than they save

- **Notes:** 0030 §4.3 (`cols` reconciliation with expression columns), §6.2 (dead-key clause),
  §13.2 decision 18 (export path by capped count plus a 413 fallback), §12.3 (failure wording),
  §2.4 (custom filter `component` hatch with `injectTmCustomFilter`).
- **Fix.** Expression columns apply only when the URL carries no `cols` (one rule); drop dead-key
  handling (accent lost is acceptable, flag 14 already); export: try synchronous, fall back to
  `export/start` on 413 (one rule, the note's own alternative); the custom-filter `component` hatch
  waits for a consumer (`switch` and `options` cover Acme). ≈ 50 lines.

### Q-20 — minor — Copy and summary sections repeated across the page specs

- **Notes:** 0030 §5.3, 0031 §3.5, 0034 §3.3, 0035 §8.6 (state tables restating 0024 §11's
  kinds and copy); 0030 §18, 0031 §17, 0032 §14, 0034 §14, 0035 §15.5 ("Accessibility, RTL and
  responsive summary") beside each note's UI-rules table.
- **Problem.** The style guide's test: a change to the offline copy would need five edits.
- **Fix.** Each state table keeps only the page-specific rows (no data, no matches, invalid
  filter, waiting) and cites 0024 §11 for `offline`, `forbidden`, `server`, `client-outdated`.
  Drop the summary sections; the Definition of done's UI-rules table carries the same facts.
  ≈ 90 lines.

### Q-21 — minor — The audience resolver answers more than the admin's question

- **Notes:** 0033 §6 (`RootRead` per member through a new bulk `EvaluateOutcomesAsync`, E2;
  dashboard reasons one level deep; `Grants` with counts), 0035 R3(d) (a multi-securable overload).
- **Fix.** Members with reasons and the highest access answer "who and why"; `RootRead` becomes a
  per-grant count computed from the grant's role (the share dialog's warning needs no per-member
  outcome). Drop E2 and the overload; dashboards contribute a reason by id only. ≈ 40 lines and one
  0014 edit fewer.

### Q-22 — minor — `'uq'` emission has three grains; one is enough for v1

- **Notes:** 0010-F F.14 (lateral seek, prologue variables, QX3104), review flags 5–6.
- **Fix.** Emit the lateral seek for row-grain operands and QX3104 for anything else (path-free
  operands come only from F.11, which Q-7 removes). ≈ 15 lines.

### Q-23 — minor — The notes already exceed the length the style guide sets for the specs

- **Notes:** all; style guide "aim for 600–1,200 lines. A long spec is a smell".
- **Problem.** Sections 3, 8, 9, 10 and the decisions and flags of each note become the spec. By
  that measure 0033 lands near 1,500 lines, 0027/0031/0034/0035 near 1,200–1,300.
- **Fix.** Apply the Cut list; give each author a target (0033 ≤ 1,200; the rest ≤ 1,100) and
  move any closed list over 20 rows to an appendix.

---

## 3. Under-engineering

### Q-24 — major — `TmApi.upload` and `TmUrls.blob` are consumed by five notes and defined by none; `fetch` cannot report upload progress

- **Notes:** 0026 R(2), 0029 §16 + R(4)(5), 0030 §13.3, 0031 §6.3 + R7, 0032 §4.7; 0024 §10.1
  (owner; no `upload`, no `blob`); 0010-BE challenge 7 (no GET on `TmApi`).
- **Problem.** Image fields, attachments, the import dialog, signatures and the support dialog all
  call `TmApi.upload(kind, blob, { fileName, signal, onProgress })` and render progress bars and
  rings from `onProgress`. 0024 defines neither the method nor the URL helper. The HTTP layer is
  "over `fetch`, not `HttpClient`" (A§5.3) and `fetch` exposes no upload progress in any browser
  (request streams are Chromium-only and still give no progress events); only `XMLHttpRequest`
  does. The multipart body also sits outside 0011 §5.4's CSRF rule ("JSON body").
- **Fix.** 0024 §10 adds: `upload(kind, file, { fileName, signal, onProgress? }): Promise<TmBlobDescriptor>`
  over `XMLHttpRequest` (the one XHR in the client: `upload.onprogress`, abort on signal, the
  same user check and headers), the CSRF header on the multipart POST (0017 edit: the blob upload
  route requires `Tellma-Client`), and `TmUrls.blob(kind, id, { variant?, download? })`. Consumers
  cite it. If XHR is refused, state that uploads show an indeterminate ring and delete
  `onProgress` from the five notes.

### Q-25 — major — Settings and My profile depend on a details-host seam that 0031 does not own

- **Notes:** 0032 §12 + R1 (`TmDetailsSource`, `tm-details-page`), R4–R8 (`actions[].visible`,
  `run(args, { keepOpen, ids })`, `close()`, a `selected` model on `tm-details-tabs`, a `/private`
  registry); 0031 §3–§4 (no source abstraction; the host reads `{segment}/get` and writes
  `{segment}/save`).
- **Problem.** The brief's Settings page (single tabbed interface) and My profile are built on
  0031's host through a source the 0031 note never mentions. If 0031 is written as is, 0032 cannot
  be written.
- **Fix.** 0031 §3 defines the host over `TmDetailsSource<T>` (0032 §12's sketch, with the entity
  route as the default source `tmEntitySource(descriptor, id)`), accepts R4 (`visible`), R5
  (`run` options and `close`), R6 (`selected` model) and replaces R7 with a public registration
  API on `TmDetailsContext` (`registerField(path)`, `registerCollection(name)`); no `/private`.
  ≈ 40 lines in 0031, which removes 0032 §12.

### Q-26 — major — 0028 asks 0030 for six declaration members; 0030 answers none

- **Notes:** 0028 §10.1 + request to 0030 (`tabs`, computed columns with `select`, `rowLink`,
  `onActivate`, `pageActions`, `bulkActions`, `refreshRows`, a Refresh "dot"); 0030 §2 (members:
  `columns`, `orderBy`, `tile`, `filters`, `search`), §7 (requests to 0028 only about toasts and
  `x.mine`).
- **Problem.** Notifications, Assignments and Jobs are "declarations on the CRUD framework" only
  if 0030 adds those members; otherwise they need the `search` hatch and custom code, which
  0028 decision 19 rejects. Whether the members are general is a 0030 decision it has not made.
- **Fix.** 0030 adopts `tabs` (with Q-2's `tab=` spelling) and `rowLink` as general members and a
  `refresh()` on `injectTmSearch` (no `refreshRows`); computed columns are the existing
  `{ expression, labelKey, display }` with `select` as an optional alias list; `onActivate`,
  `pageActions` and `bulkActions` are not members: 0028 registers them through `TmPageActions` from
  a 15-line page component that also owns the Refresh dot. Record it in both notes.

### Q-27 — minor — `details` hatch shapes and page-context ownership differ between three notes

- **Notes:** 0031 §3.7 (`details: { page }`; "the page context stays the host's"), 0034 §2.3 +
  R8(c) (`details: { page, create }`), 0035 §12.1 + challenge 2 (`details: { page }` for view,
  edit and create; the hatch registers its own `dashboard` kind and asks for
  `TmDetailsContext.actions` to keep `details.*`).
- **Fix.** 0031 §3.7 decides: `details: { page: TmLazyComponent; create?: TmLazyComponent }`; a
  hatch may register its own kind (`report`, `dashboard`); the host exposes `actions` so the
  hatch's context lists them. One shape, cited by 0034 and 0035.

### Q-28 — minor — No member-wise override for sections and entries

- **Notes:** 0027 §2 hatch table ("`overrides` are entity-only … `hide` then redeclare under a new
  id"), 0024 §2.3.
- **Problem.** Relabelling or reordering a platform section means hiding it and redeclaring every
  entry under a new id, which also breaks pins and recents keyed by entry id.
- **Fix.** `overrides` accept `section:` and `entry:` keys with member-wise patching, like
  entities (0024 §2.3, three lines).

### Q-29 — minor — Cacheable lists that answer 403 leave pickers undefined

- **Notes:** 0025 §3.3 ("A `403` stores nothing and the signal stays `undefined`"), 0029 §14.2
  step 3 ("a promise only while the list first loads").
- **Fix.** 0029 §14.2: when `entitySet()` settles with no list, the binding falls back to the
  server search; a read-only field when the user may not query the target (already stated for
  `allows`).

### Q-30 — minor — Sign-out while a write is in flight loses the outcome

- **Notes:** 0024 §8.4 (step 3 aborts reads; writes are never aborted; step 7 replaces every tab
  to `/signed-out`), 0031 §8.7 (the overlay blocks navigation during a save).
- **Problem.** A tab in the middle of a save receives `session.signing-out` from another tab and
  leaves; the save commits server-side with nobody to see the result or the 422.
- **Fix.** 0024 §8.4 step 2: a tab replies `drafts.reply` with `dirty` *and* `busy`; a busy tab
  makes the confirm read "A save is still running in another tab"; sign-out waits for `busy` tabs
  up to the watchdog before step 4.

---

## 4. Security and privacy

### Q-31 — major — The entity model, securables catalogue, setting keys and action names ship as anonymous static assets

- **Notes:** A§6.6 ("Hashed JS, CSS, fonts, `generated/*` chunks: anonymous"), 0024 §6 (static
  files anonymous), 0029 §2 + review flag 8 + challenge 7, 0032 §7.3 (`settingKeys` incl. secret
  key names such as `acme.integration.marminApiKey`), §13.4 (every securable).
- **Problem.** Anyone who can reach the origin can download the distribution's full data model:
  every entity, property, enum, child collection, action and its argument schema, every securable
  and which are sensitive, every setting key and its default, every notification type. For a
  single-tenant customer that is their business model and integration surface; it also hands an
  attacker the exact action names and argument shapes to probe.
- **Evidence.** 0029 flag 8 records it as a judgment call; nothing in the skeleton's §12 risk
  table lists it.
- **Fix.** Serve `tm-metadata/entities-*.json`, `core-*.json` and `contracts-*.json` with the
  session cookie required (`RequireAuthorization(TellmaPolicies.Web)` on the `spa-assets` endpoint
  for that folder; keep `immutable` caching). The warm launch is unaffected: the service worker
  prefetches the `shell` group *after* first render, when the cookie exists, and the SW's fetches
  carry cookies. Only the string packs stay anonymous. One row in A§6.6 and 0024 §6.

### Q-32 — minor — The dump and the support request carry the page URL with its query text

- **Notes:** 0026 §10.1 (`page.url`, "Never in a dump: … user text of the query"), §12.3
  (`SupportRequest.PageUrl`, 2048), §9.6 (`query.q`, `filter` masked to `"…"`).
- **Problem.** `/17/customers?q=al faris&filter=CreditLimit > 100000` is user text; it is stored
  for 30 days and readable by tenant admins and Tellma support even when "Include the data shown on
  the page" is off.
- **Fix.** `page.url` and `PageUrl` carry the path only unless `includesPageData` is true.

### Q-33 — minor — `audience` lists role members to any report owner

- **Notes:** 0033 §6.2 step 2 ("every stored permission of an active role … with its role's
  members"), review flag 9.
- **Problem.** Every member can author a report and holds `Share` on it, so every member can call
  `audience` and enumerate the members of the Administrator role and of any role with a
  `core.ReportDefinition` or `*` grant.
- **Fix.** Members are listed only when the caller holds `core.Role × Read`; otherwise the
  resolver answers grants with counts (the flag's alternative). One sentence.

### Q-34 — minor — Shared devices keep seven days of cached user data; no deployment knob

- **Notes:** A§5.5, A§12 #26, 0025 §2.4 (7-day idle window), §2.3 (`inbox`, `assignments`
  summaries with captions, amounts and counterparties; `me` with e-mail and access; cacheable
  lists), 0011 §5.5 edit (cookie expires with the idle session).
- **Problem.** A user who closes the browser without signing out on a shared PC (a common pattern
  in the brief's markets) leaves `me`, badges, assignment captions and reference lists on disk until
  the idle session expires. The design accepts this; it offers no way for a deployment to shorten
  it.
- **Fix.** Add `Tellma:Client:WarmDays` (default 7, published in `DistributionInfo`); 0025 uses it
  as the idle window and 0011's idle expiry follows it. One row in 0011 §8 and one sentence in
  0025 §2.4. Record the trade-off as a review flag in 0025.

### Q-35 — minor — Hardening checklist (each is one line in the owning spec)

- **0023 §3.3:** `TmIconRegistry.register` refuses attribute names that start with `on`, `href`
  and `xlink:href` (distribution icon data is trusted today; the check costs nothing).
- **0027 §9.4:** the `<mark>` wrapping of matched text is built with DOM nodes, never `innerHTML`.
- **0024 §8.1:** state that `sh` (the session handle in a JS-readable cookie) is accepted by no
  endpoint as a credential; 0011 §5.5 should say the same.
- **0033 §10:** child query surfaces inherit the owner's `McpExposure` and the startup audit lists
  every child surface it projected (they are default-on).
- **0032 §10.2:** the e-mail body frame is a good design; add `csp: "sandbox"` is not needed; keep
  `referrerpolicy="no-referrer"`. (No change; recorded as reviewed.)
- **0026 §12.2:** the screenshot preview is shown before Send so the user sees what leaves; add
  "the browser's picker may capture other windows" to the warning text.

---

## 5. Feasibility (verified)

### Q-36 — major — The browser floor on `JSON.rawJSON` blocks older iPhones for a feature a regex pre-pass replaces

- **Notes:** 0024 decision 6, §4.2 step 1 ("A missing feature → boot page `unsupported`"),
  §10.4, review flag 1 (floor Safari 18.4 / Firefox 135 / Chrome 114).
- **Evidence.** MDN lists `JSON.rawJSON` and reviver source access as Baseline 2025 "newly
  available" since March 2025 (https://developer.mozilla.org/docs/Web/JavaScript/Reference/Global_Objects/JSON/rawJSON).
  iPhones that cannot run iOS 18.4 (iPhone X and earlier, still common in Sudan, Ethiopia and
  Lebanon) are therefore refused at boot, for a parser detail that matters only when a JSON number
  has more than 15 significant digits.
- **Fix.** Keep the fast path. Where `JSON.rawJSON` is absent, run the existing 16-digit regex as a
  pre-pass that quotes long numeric literals (outside strings) and parse with a reviver that turns
  those strings into `TmDecimal`; stringify writes `TmDecimal.text` through a post-pass. Remove
  `JSON.rawJSON` from the feature gate; keep `navigator.locks`, `BroadcastChannel`, `indexedDB`.
  ≈ 10 lines; drops the floor to Safari 15.4.

### Q-37 — minor — `overflow-anchor` is not in any stable Safari; 0028's "Baseline 2026" claim is wrong

- **Notes:** 0028 §5.3 step 4 ("CSS scroll anchoring (`overflow-anchor`, Baseline 2026)"),
  verified facts row 1.
- **Evidence.** Can I Use lists Safari 26.2–26.4 as not supported, Technology Preview only
  (https://caniuse.com/css-overflow-anchor); WebKit bug 171099 is the open implementation ticket.
- **Fix.** Insert new rows at the top of an open panel only while the list is scrolled to its top;
  otherwise show "{n} new" as a pinned header the user activates. Remove the Baseline claim.

### Q-38 — minor — `getDisplayMedia` on iOS Safari is not available in any shipped version

- **Notes:** 0026 §11.2 ("Safari iOS 27+: screen"), verified facts.
- **Evidence.** Can I Use lists iOS Safari through 26.5 as not supported
  (https://caniuse.com/mdn-api_mediadevices_getdisplaymedia); the WebKit bug is resolved in trunk
  with no shipping version recorded.
- **Fix.** Drop the iOS row; iOS uses "Attach image". The feature-detected fallback already covers
  it.

### Q-39 — minor — Safari has no Page Lifecycle `freeze`; a suspended leader keeps its lock

- **Notes:** 0025 §7.2 steps 3–4 (release on `freeze`, re-request on `resume`), §7.3.
- **Problem.** WebKit does not fire `freeze`/`resume`. A hidden Safari tab that iOS suspends keeps
  the hub lock with a dead socket; the design still recovers through the hung-leader rule (a
  visible follower's probe goes unanswered for 5 s), but the note presents `freeze` as the
  mechanism.
- **Fix.** State that the probe path is the recovery on engines without Page Lifecycle, and add a
  WebKit case to the multi-tab e2e (`@cross-engine`). Confirmed facts relied on elsewhere
  (Angular v22 with stable Signal Forms, released June 2026; Firefox `execCommand('insertText')`
  in form controls fixed in Firefox 89) check out.

---

## 6. Seams that will not fit (beyond name mismatches)

### Q-40 — major — Three specs change the `tmEntity` declaration shape that 0029 owns, in different directions

- **Notes:** 0029 §3 (`launcher: { order, icon, synonymsKey, labelKey }`, `picker`, `helpKey`),
  0027 §3.2 (`section`, `navOrder`, `icon`, `synonymsKey` as top-level members), 0032 challenge 7,
  0031 R15 (`defaults`, typed `validate`), 0032 R4 (`actions[].visible`), 0034 R3 (`searchRead`),
  0028 request (`tabs`, `rowLink`, …), 0030 §2 (`filters` shapes).
- **Problem.** The merge rule, the typed codegen and `tmCheckEntityDeclaration` all key on this
  shape; the authors will write seven partial versions.
- **Fix.** 0029 §3 publishes the final shape before the specs are written, with one line per
  member naming the meaning's owner: `section`, `launcher`, `columns`, `orderBy`, `tile`,
  `filters`, `tabs`, `rowLink`, `search`, `searchRead` (or not, per Q-26), `details`, `defaults`,
  `clone`, `validate`, `beforeSave`, `actions[{ name, dialog?, icon?, button?, visible? }]`,
  `picker`, `helpKey`. 0027 adopts `launcher.order`.

### Q-41 — minor — Name and signature mismatches to settle in one pass

- `TM_APP_DECLARATIONS` (0027 R(3)) vs `TM_ENTITY_DECLARATIONS` (0029 §1): one token holding the
  merged, `hide`-applied declarations of every kind.
- `TmServerText.problem/error` (0024 §7.5) vs `message(key, args)/has(key)` (0028 R(1)): add the
  two generic members to 0024.
- `TmApi.send(route, body)` (0024) vs `send(route, body, { background })` (0026 R(9)): telemetry
  always sets the header; add the option.
- `TmRequestLog` one record at settle (0024 §10.3 step 5) vs `begin/end` (0026 §8.1): adopt
  `begin/end`.
- Dashboard ids `long` (0033 §8.1, §5.7) vs `int` (0035 R3(a)): `int`.
- `TmSearchField.error` input (0023) has no consumer (0030 flag 16): delete.
- `tm-pager` `disabled` (0030 R3), `tm-tiles` driver target and scroll (0030 R1–R2), `tm-card`
  subtitle slot (0035 R7), `tm-chips` free entry (0032 R14), `tm-kbd` physical-key matching (0026,
  0027): 0023 must list them; none is in its note.
- `reports/browse` + `searchRead` (0033 §5.5, 0034 R3(a)): keep, but 0030 §2 must own the member
  and 0034 cite it; it is the only consumer, so mark it a platform-internal hatch.

---

## Cut list

Estimated lines are what the future *spec* saves (the notes' sections 3, 8, 9, 10, decisions and
flags); "why" says what the design loses, which is nothing a v1 needs. Total ≈ 2,800 lines.

| Note | Cut, merge or simplify | Lines | Why the design does not need it |
|---|---|---|---|
| **0023** | Drop `accordion` (no consumer); drop `TmSearchField.error`; §3.9 keep group names, drop values; §3.10 drop the Arabic column; §3.11 drop the story list; merge the per-component keyboard tables into §3.2's one table | ≈ 110 | tokens and translations live in code and are gated; no page composes an accordion |
| **0024** | One boot page instead of four (Q-16); config check as a Node step (Q-16); drop the Cookie Store listener; §2.6 commands → one table row each; §12 cite A§11's budgets instead of restating; §5.3 group assignment → two sentences; §8.4–§8.7 keep the sequence diagram, cut the prose steps it repeats | ≈ 120 | the boot contract (§4.2 tables) and the status table (§10.6) are the value; the rest is restatement |
| **0025** | Q-13's five mechanisms; §6.1 versioning and bfcache → two sentences; §13 recents' `n` rule → "title as displayed"; §2.7 quota eviction → "drop the write, telemetry"; §8.2 code block → prose | ≈ 160 | one sync per browser per tenant and adoption of own writes are the invariants; the bookkeeping around them is speculative |
| **0026** | Q-8 (INP, fingerprints, sampling, metrics, truncation ladder, admin paging, capture matrix); §9.7 AG-UI table → a five-line note; decisions 18 → 10; §12.2 dialog copy → the states only | ≈ 230 | a relay plus a dump plus a request is what the brief asked; the analytics layer can grow later |
| **0027** | Q-9 (context menu, launcher drag, measured overflow, ranking engine, provider caching); §4.3 reserved/browser key lists → an appendix table; §7.1 pixel detail → the collapse order only; §12.1 server steps → cite 0018; the research-deviations table → decisions; §6.2 picker prose → a table | ≈ 260 | pins, launcher and session menu are small UI; the spec should read like one |
| **0028** | Q-14 (arrival engine, read-on-visit, `jobs/status`, alerts, restored message); §10.3 settings page ASCII + auto-save prose → a table; §10.5 column list → cite the declaration; decisions 21 → 12; §4.8 telemetry → three counters | ≈ 190 | the server design (§2–§4) is the keeper; the client sections repeat 0023's components |
| **0029** | Q-18 (lexer restatement, drop mechanics, path search, rare types); §14.4 page loading → cite Q-1's ruling; §16 → the policy sketch only; decisions 17 → 12, flags 14 → 10 | ≈ 150 | `queryex/discover` is the one authority; the client needs no second description of the grammar |
| **0030** | Q-19 (cols reconciliation, dead keys, export rule, component filter hatch); Q-20 (state copy, §18); §13.3 import steps → cite 0019's `ImportPlan` shapes; §3.2 responsive table → the four rows that differ; §7.1 and §9.1 markup → one example | ≈ 160 | the page is a composition of 0029 and 0023 parts; restating their shapes is the bulk |
| **0031** | Q-12 (extras operation, tab prerender, Save as new); Q-20 (§3.5, §17); §6.3 upload details → cite Q-24's method; §6.4 BigInt and height formula → code; §2 keep the minimal example only; decisions 20 → 14 | ≈ 200 | the host's lifecycle and the kit are the value; one new server operation for no consumer is not |
| **0032** | Q-15 (signature pad, delivery drill-down, `roles/problems` route); §4.3 invite outcome table → three rows; §4.6 Access tab → one paragraph; §9 schedules → the layout and the built-in rule; §13.1 save steps → cite 0013 §6.3 after the edit; §14; decisions 25 → 15 | ≈ 170 | stock pages are declarations plus layouts; every extra mechanism here is a page-specific exception |
| **0033** | Q-7 (F.11 and one-round-trip defaults); Q-21 (`RootRead`, E2, dashboard depth); §3.5 four examples → two; §5.3 SQL example → cite 0010-F F.15; §9.3 JSON example → the walk-through table only; §12.3–§12.4 Excel pivot layout and writer → "fully expanded compact form, four styles" + cite 0019; §8.10 caching → code; decisions 24 → 16, flags 16 → 12 | ≈ 330 | the document, the compile, the cube and the sharing criteria are the design; SQL goldens and Excel styling are code |
| **0034** | Q-10 (chart types, tooltip, keyboard, hidden table, palette table); §12.6 kind-switch seeds → "shared sections stay; terms are re-entered"; §8.2 8,000-character fallback; §4.5 remembered parameters + `report.<id>.params` key; `report.studioPanel`; §6.2–§6.4 pivot pixel layout → the compact-form rule and the four styles; §7.3 engine internals → code; decisions 29 → 18, flags 18 → 12 | ≈ 300 | four renderers and a studio over a self-describing result; the facade's product features can follow usage |
| **0035** | Q-11 (seeds, widget warnings, viewport gating, six-run cap, `tmFirstName` English list, §15.6 → five lines); §8.2 packing algorithm → code with the two rules stated; §13.4 gallery → a paragraph; §9.2 built-in widgets → cite 0028's list component; decisions 24 → 15 | ≈ 260 | a grid, a scheduler and the home default; the layout engine's rules belong in tests |
| **0010-BE** | C.11 budget table → ceilings only; D.4 drop the `age` form (one relative style); B.4 and C.10 examples → one each; per-item test bullets → the Definition of done; flags 16 → 12 | ≈ 90 | the amendments are precise and small; the surrounding measurement prose is not spec text |
| **0010-F** | F.11 (Q-7); F.14 grains (Q-22); F.0.3 cache-key table → one rule ("every new option is a key component"); F.4 and F.8 examples → one each; flags 11 → 8 | ≈ 60 | the engine amendments stand; the compiler's cache internals are implementation |

**Total estimated cut: ≈ 2,800 lines** (0023 110, 0024 120, 0025 160, 0026 230, 0027 260,
0028 190, 0029 150, 0030 160, 0031 200, 0032 170, 0033 330, 0034 300, 0035 260, 0010-BE 90,
0010-F 60), with two engine amendments (F.11, the three-grain `'uq'`), one standard operation
(`extras`), two injection tokens, one library entry point (`accordion`), one new component
(`tm-signature-pad`), two server routes (`jobs/status`, `roles/problems`) and one 0014 edit (E2)
removed.

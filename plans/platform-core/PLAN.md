# Platform core implementation plan (specs 0010–0035)

Prepared 2026-11-07 for the autonomous implementation run. The run implements specs
`docs/specs/0010` to `0035` on the `platform-core` branch. Specs 0001–0009 are implemented and
frozen except where spec 0010 amends them.

This folder is a **living working folder for the run**: the ledger, reviews and decisions change
daily. That is why it sits outside `docs/`, whose documents are frozen once merged. Remove or
archive it before `platform-core` merges into `main`.

| File | Read by | Purpose |
|---|---|---|
| [PROCESS.md](PROCESS.md) | orchestrator, every agent | How a checkpoint is built, reviewed, committed and integrated; pacing; limits; recovery |
| [CHECKPOINTS.md](CHECKPOINTS.md) | implementers, reviewers | One entry per checkpoint: sections, gates, deliverables, tests, done-when, planner notes |
| [DEPS.md](DEPS.md) | orchestrator, `tools/check_deps.py` | The gating graph (authoritative readiness) |
| [WATCHLIST.md](WATCHLIST.md) | implementers, reviewers | Unverified leads per spec and the analysts' cross-spec ordering notes |
| [LEDGER.md](LEDGER.md) | orchestrator | Live status, in-flight agents, usage log, calibration |
| [DECISIONS.md](DECISIONS.md) | orchestrator, Ahmad | Non-obvious rulings and process decisions for Ahmad's review |
| [SETUP.md](SETUP.md) | Ahmad | Pre-flight checklist, Claude Code configuration, answers to the open questions |
| [KICKOFF.md](KICKOFF.md) | Ahmad | The prompt that starts the run |
| `tools/` | orchestrator | `check_deps.py` (graph check and ready list), `usage_window.py` (transcript cost meter) |

---

## 1. Goal

Ship specs 0010–0035 at production quality, in priority order, so that whatever exists when the
budget runs out is a coherent product:

1. **Tier 1 (MVP)**: a launchable, humanly testable product. That means the multitenant
   distribution host with in-proc identity, data access, settings, users, roles and permissions,
   the CRUD pipeline and web API, blobs, the core and GL reference stacks, background jobs, and
   the client: foundation, shell, launcher, home, entity model, and the search, details and stock
   admin pages. It ends with the MVP gate (PC-140), which writes the human-testing guide.
2. **Tier 2 (completion)**: notification delivery and the realtime hub, the email outbox, the
   scheduler, the bell and attention surfaces, pinned bar, diagnostics, the full home page, picker
   modal pages, the remaining e2e and axe batteries.
3. **Tier 3 (spills)**: Excel import and export, reports (engine, renderers, studio),
   dashboards, assignments, support requests, the MCP server, client ID metadata documents, and
   housekeeping jobs.

**Target dates (central forecast, §3, with Ahmad's two review levers applied, §3.3).** Tier 1
lands early in the Nov 24 window, around Wed–Thu Nov 25–26. Nov 20 is reachable only if the
first calibration comes in well under the priors. Tier 2 follows around Dec 2, and Tier 3 ends in
the Dec 8 window. Human testing starts at the MVP gate.

## 2. Shape of the work

- 26 specs, 3.6 MB of spec text: 4.6× the nine implemented specs, which produced about 197k LOC
  including tests and goldens.
- The planning analysis (12 Opus agents, each reading one to four specs in full) produced the
  checkpoints, and an adversarial plan review (5 agents) corrected them. That gives **218
  checkpoints and about 1.04M LOC including tests**, consistent with the 250 LOC per KB of spec
  seen in specs 0002–0009.
- A checkpoint is 2k–9k LOC (median about 4.5k): one coherent, buildable, testable slice that one
  Opus agent implements in one sitting and two reviewers can review in depth.

| Phase | Ids | Tier | Checkpoints | Est. LOC |
|---|---|---|---|---|
| 1 Foundations and amendments (CI tiers, 0011-a, 0010 Parts A/F/G, core-ui seams) | PC-000–014 | 1 | 15 | 60k |
| 2 Contracts and data-access core (contract seed, 0012-a…c, 0013-a…c, 0011-b/c, 0023 components) | PC-015–031 | 1 | 17 | 89k |
| 3 Queries, caching, access control, grid amendments | PC-032–061 | 1 | 30 | 140k |
| 4 CRUD pipeline, web API, blobs, client package skeleton | PC-062–084 | 1 | 23 | 118k |
| 5 Distribution, reference stacks, jobs, discovery | PC-085–102 | 1 | 18 | 90k |
| 6 Client foundation, shell, entity model, home scaffold | PC-103–123 | 1 | 21 | 96k |
| 7 Search, details and stock pages, MVP gate | PC-124–140 | 1 | 17 | 79k |
| 8 Completion | PC-141–172 | 2 | 32 | 129k |
| 9a Excel import and export | PC-173–180 | 3 | 8 | 44k |
| 9b Reports (+ the MCP server, which 0033 exports through) | PC-181–201 | 3 | 21 | 131k |
| 9c Dashboards | PC-202–205 | 3 | 4 | 27k |
| 9d Assignments, support, housekeeping, document clients | PC-206–217 | 3 | 12 | 43k |
| **Total** | | | **218** | **≈1.04M** |

Tier 1 is 141 checkpoints and about 670k LOC (363k backend, 308k client or both). Appendix A is
the priority order; CHECKPOINTS.md details each entry, and DEPS.md holds the gates. The
orchestrator always starts the highest-priority ready checkpoint (PROCESS.md §5).

## 3. Budget and forecast

### 3.1 Calibration (measured on this machine, 2026-11-07)

**Cost units** are price-weighted tokens: input + 1.25 × cache write + 0.1 × cache read + 5 ×
output, with Fable at 2.5× Opus (1.25× on cache reads). `tools/usage_window.py` computes them from
transcripts. The token count an agent's completion reports is its final context size, not its
consumption, so it is never used for calibration.

- **Weekly window.** The 12-agent planning analysis used 17.4M units, and the weekly all-models
  meter moved 38% → 40%. That gives **≈ 8.7M units per point, ≈ 870M per week**. The meter reports
  whole percentages, so the true figure lies between 5.8M and 17M per point. The orchestrator
  recalibrates once its window spans 10 points (PROCESS.md §6.2).
- **Cost per LOC** in earlier implementation sessions of this repo. These figures are
  implementation only: spec authoring, critiques and design work done in the same worktrees are
  excluded. Each session ran one review-and-fix round, which was about 30% of its cost.

  | Session | LOC | Cost units | Per 1k LOC |
  |---|---|---|---|
  | Queryex (0008) | 33.6k | 98M | 2.9M |
  | Marmin (0009) | 13.0k | 33M | 2.5M |
  | Email (0007) | 19.6k | 50M | 2.6M |
  | Basics (0005, UI) | 26.7k | 121M | 4.5M |
  | Entity picker (0006, UI) | 10.5k | 136M | 12.9M (heavy e2e rework) |

- **Priors, in points per 1k LOC for a full checkpoint cycle** (two review rounds):
  - **Backend k ≈ 0.36**: 2.7M per kLOC is 0.31 points, ×1.27 for the second round and ×0.9 for
    smaller per-checkpoint contexts.
  - **Client k ≈ 0.80**: the LOC-weighted client average including the entity picker is 0.79
    points, ×1.14; Basics alone would give 0.60.

### 3.2 Forecast

| Scope | LOC (backend / client) | Checkpoint cycles | Batch and gate reviews | Orchestrator | Central | Range |
|---|---|---|---|---|---|---|
| Tier 1 | 670k (363k / 308k) | 316 pts | 64 pts (B01–B07, MVP gate) | 20 pts | **≈ 4.0 weeks** | 2.9–5.5 |
| Tier 2 | 129k (44k / 85k) | 68 | 8 | 5 | **≈ 0.8** | 0.6–1.2 |
| Tier 3 | 244k (120k / 124k) | 115 | 28 | 7 | **≈ 1.5** | 1.1–2.1 |
| **All** | **1.04M** | 499 | 100 | 32 | **≈ 6.3 weeks** | 4.5–8.5 |

The cycle column assumes the two-review cycle only for the PROCESS.md §3 two-review list (about 38
Tier-1 checkpoints and 44 in all), and the one-review cycle (0.82 × p) for the rest. Batch reviews
run once per phase (§8.2).

**Available by Fri Nov 20: ≈ 3.6 weeks.** That is what remains of this window (≈ 59 points
after planning), the window from Nov 10, the one-time manual reset, and the window from Nov 17
used by Friday.

**Reading.** Tier 1 needs about 0.4 weeks more than is available by Nov 20, so the central
landing is early in the Nov 24 window (around Wed–Thu Nov 25–26). This is Ahmad's Option B, and
the plan is built for it. Tiers are strict, every checkpoint leaves `platform-core` green, and the MVP gate writes
the human-testing guide the moment Tier 1 completes. Option A (a second subscription) is not
recommended.

**Early warning.** After 15 integrated checkpoints, and again after 40, the orchestrator
publishes a calibrated forecast and notifies Ahmad (PROCESS.md §9). The first should arrive
around Sun Nov 8 or Mon Nov 9.

### 3.3 Review levers (applied by Ahmad, 2026-11-08)

| Lever | Saves (T1) | Trade-off, and how it is contained |
|---|---|---|
| Review 2 only for security-bound or complex checkpoints (the PROCESS.md §3 two-review list, 44 checkpoints) | ≈ 60 pts | Less adversarial depth on routine UI and plumbing. Any other checkpoint is escalated to Review 2 when Review 1 finds a blocking issue or many majors, or when fix 1 rewrites much of it (PROCESS.md §4.4) |
| Cross-checkpoint batch reviews once per phase (7 in Tier 1) instead of per group (11) | ≈ 10 pts | Larger review scope per batch; the finders work at `xhigh` |
| Both | ≈ 0.7 weeks | Tier 1 central moves from about Dec 1 to about Nov 25–26 |

### 3.4 Throughput vs budget

Tokens, not wall-clock time, bound this run. Three concurrent cycles burn about 15–25M units per
hour, so a weekly window lasts about 35–58 hours. Each window empties early and the run then
waits for the next reset. More parallelism would only lengthen the waits, and it raises merge
friction and CPU contention. So: **3 concurrent cycles by default, and 4 only while the ready
queue spans backend and client work** (PROCESS.md §5.1).

Expected rhythm:

- From kickoff until about Sun Nov 8 night (≈ 59 points), then idle until Tue Nov 10, 20:00 UTC.
- From then until about Thu Nov 12 or Fri Nov 13, then the manual reset (apply it by **Sat Nov 14,
  20:00 UTC** at the latest, SETUP.md §4).
- Until about Sun Nov 15, then idle until Tue Nov 17, 20:00 UTC.
- Until about Thu Nov 19 or Fri Nov 20, then idle until Tue Nov 24.

Idle stretches are the time to read DECISIONS.md and the batch-review summaries.

### 3.5 Optimisations that cost no quality

- Only PR-tier tests run inside a cycle. Long matrices run nightly on GitHub Actions (§6): the
  repo is public, so minutes are free, and no agent waits on a 40-minute suite.
- Agents build and test the touched projects first and run the exact full gate of PROCESS.md §11
  once at the end. Integration suites run one at a time per slot.
- Reviewers get the exact spec sections and commit range, not the corpus.
- The orchestrator keeps its context lean. Agents return one-line summaries and write details to
  files, and auto-compaction at 400k (SETUP.md) caps the cost of each turn.
- The same implementer, resumed, handles its fix round(s). It knows why each decision was made, so
  its pushback is informed and nothing is re-derived.
- Adjacent checkpoints of the same spec under 3k LOC may be merged when both are ready, which
  saves a review cycle.

## 4. Tiers and the spill line

Tier assignment comes from the analysts' MVP classification, corrected for dependencies. Where a
Tier-1 checkpoint needed a Tier-2 one, that one was promoted: 0010-a3/a5/b2/c2/c5/c6/d3/e2/f4/f5,
0012-g, 0013-g, 0016-e, 0020-d, 0023-f/g, 0024-k, 0025-c, 0029-f/g, 0030-d and 0031-f. A Tier-1
home scaffold was split out of 0035-a (0035-a1). Other dependencies on later tiers became seams,
and each is named in its checkpoint's "Later seams" and "Planner note".

**Strict tier order.** No Tier-2 checkpoint starts while a Tier-1 checkpoint is ready and a slot
is free, and likewise for Tier 3. A lower tier may start only when no higher-tier checkpoint is
ready, a slot would otherwise idle, *and* the window would otherwise expire with points unused.

**Tier 3 order (decision for Ahmad, defaulted):** Excel (9a), then reports (9b), then dashboards
(9c), then the rest (9d). Excel comes first because importing real data makes human testing far
more productive. To change it, reorder Appendix A and DEPS.md before kickoff and re-run
`tools/check_deps.py`.

## 5. Ordering: the traps the order handles

The full analyst notes are in WATCHLIST.md §Cross-spec ordering notes.

1. **Contract seed (PC-015).** Specs 0011–0014 consume types owned by 0015, 0016 and 0024:
   exceptions, validation contracts, attributes, endpoint metadata, `IClientAssetContributor` and
   `TellmaJson`. They are created once, under the owners' names, before 0012. Later checkpoints
   extend them and never re-declare them. `StepUpRequiredException` belongs to 0011 §4.6.
2. **0015's own contracts (PC-062)** also create, contract-first, the Tier-3 types they name:
   0019's Excel row-source types and 0028's `BatchAssignments`. That keeps Tier 1 free of Tier-3
   code.
3. **0013-a before 0011-c and 0012-c1.** `RequestContext` and `IDataBatch` name 0013 contract
   types.
4. **The 0012 ↔ 0013 ↔ 0014 triangle.** The order is: the executor with seams and fake
   contributors (0012-c1/c2), then access tables in the fixture (0014-a), version tags (0013-d),
   securables (0014-b), Decide (0014-c), the cache (0013-e), tenant settings (0013-f) and the
   connect prologue (0014-d). 0013-f ships a stub prologue, which 0014-d replaces. 0014-e ships
   the `IAccessGuards` contract with a no-op default, so 0015-g can call it before 0014-f
   implements it.
5. **0010-a1 first touches every identity fixture** and owns `DatabaseCollations`. Every SQL
   fixture runs a bare `CREATE DATABASE`, which the new store check refuses.
6. **Queryex amendments (0010 Part F) before 0012-d**, including `CalendarMap` (0010-f5). 0010 §F.14
   measures are Tier 3: 0012-d and 0029-f ship without them, and 0010-f6 adds them to both.
7. **Imaging (0017-b) before blob kinds (0017-c1).** The FK exemption for `[BlobReference]` is
   owned by 0017-c2.
8. **Users and roles registration.** 0014-f ships the `UserService`/`RoleService` shells and the
   only `Entity` lines; 0018-c and 0018-e fill them.
9. **Client primary seams.** 0024-a/0024-g create seam tokens and placeholder routes, and later
   checkpoints fill them. The banner host ships with 0028-c, and the update banner source with
   0024-i.
10. **The core-UI chain.** 0023-a (icon, kbd), then 0010-b1, 0010-d1 (public overlay), 0010-d2
    (l10n), 0023-b…f, then 0010 Parts C and E. 0010-b2 (multi-select) and 0023-g (tiles) are
    Tier 1 for the pages.
11. **CSP and ICU.** 0010-b1 first checks whether the ICU path compiles messages with `new
    Function`, which the production CSP forbids, and replaces it if so. 0024-k asserts that no
    page violates the CSP.
12. **A real server early.** The Acme e2e host (0024-k) lands before the shell and pages. Each
    shell, launcher, home or page checkpoint whose Planner note names a "PR-tier Acme e2e" adds
    that one Chromium test, so wire mismatches surface per checkpoint, not at the MVP gate. The
    full batteries stay in Tier 2.
13. **Serialized migrations.** The shared fixture migrations and the Acme tenant migration are
    regenerated, never hand-merged, by one checkpoint at a time (PROCESS.md §5.1). 0018-h's
    InitialTenant omits the reporting entities, and Tier 3 regenerates it (no deployed database
    exists).

## 6. Test tiers

| Tier | Runs | Contents |
|---|---|---|
| **PR tier** | Inside every cycle (locally), and in GitHub CI (Linux and Windows) on every integration push | build (warnings are errors), format, unit tests, the integration suites of touched projects (PLAN §7.1 item 4), client gates for touched packages, e2e **Chromium only**, en and ar at one width (two where an assertion needs it), an axe smoke subset |
| **Nightly tier** | `nightly-tier.yml` on `platform-core`, dispatched each night by the trampoline on `main` (SETUP.md §1); the orchestrator triages the result on each wake | full viewport × language matrices, Firefox/WebKit `@cross-engine`, frame and rAF probes, `@perf` budgets, full axe batteries, Lighthouse and installability, multi-tab soaks (0025's 15-minute hidden leader), drain timeouts, the read-only database, the drift scanner on an intermediate schema, 0010 §F.15's differential if it exceeds 2 minutes |
| **Deferred** | Not run in this effort; tracked in LEDGER.md | Azure staging smokes (0024 §12, 0025 §14, 0030 §19); live suites needing secrets (SendGrid, ACS, Azure SignalR, MCP `Live=true`); the human-only Excel desktop check (0019 §15), with synthetic fixtures committed as stand-ins; 0018's smoke deployment; 0033 §15's 1M-row pivot performance (nightly once 0033 lands) |

Tags: dotnet `[Trait("Category", "Nightly")]`; Playwright `@nightly`, `@cross-engine`, `@perf`.
PC-000 makes CI exclude them from PR runs. When a spec says a matrix runs "on every PR", the
PR-tier subset plus the nightly full matrix satisfies it, and the split is recorded as a
deviation. Timing-sensitive assertions are `@perf` with a CI multiplier. Wall-clock waits (60 s
watchdogs, 75 s polls, a one-hour membership) are driven by fake clocks or a test `TimeProvider`
hook. Only when that is impossible do they go to nightly.

## 7. Definition of done

### 7.1 Every checkpoint

1. Every deliverable and planner note in the checkpoint's CHECKPOINTS.md entry is met. Anything
   left out is justified in the commit message under **Deviations**.
2. `dotnet build Tellma.slnx` prints **Build succeeded** with 0 warnings. A failed build leaves
   `dotnet test` running stale binaries.
3. `dotnet format Tellma.slnx --verify-no-changes` is clean: no BOMs, LF where the repo uses LF.
4. The PR-tier unit filter passes, and so do the checkpoint's own tests and the integration suites
   it touches. **"Touched"** means every test project under `test/` or `distributions/*/test/`
   whose ProjectReference closure includes a changed project. If the checkpoint changes
   `Directory.Packages.props`, `Directory.Build.*`, `Tellma.Core.Abstractions` or `test/shared/*`,
   every integration suite counts as touched. These run with
   `--filter "Category=Integration&Category!=Nightly&Live!=true"`, one suite at a time. The client
   gates for touched packages must also pass: lint, typecheck, lint:test, tokens:check, build,
   test, api:check (with intended `api:approve` diffs committed), docs:build, size:check, and the
   Chromium PR-tier e2e. The commit message lists the suites run, and reviewers check that list.
5. AGENTS.md and docs/AGENTS.md hold in full: XML docs and TSDoc on everything, inline section
   comments, a README per project, Windows and Linux. No spec, §, `docs/` or PC-id references in
   XML docs, TSDoc, error messages, logs or test names. Inline code comments may cite specs, but
   never PC ids, `plans/` paths, review-finding ids or decision ids. Nothing under `docs/` is
   edited; a spec error is fixed in code and stated under Deviations.
6. ARCHITECTURE.md is updated in the same checkpoint when the code settles something it describes
   differently. The implementer reports this in its return value, and only the orchestrator writes
   DECISIONS.md.
7. Every review round is closed. Every finding is fixed or pushed back with reasons in
   `reviews/PC-nnn.md`.
8. The checkpoint is integrated: merged into `platform-core` and pushed (status `integrated`). It
   becomes `done` when the CI run for that sha is green (PROCESS.md §5.3). A red run is a
   stop-the-line fix.

### 7.2 MVP gate (PC-140)

PC-140 is gated on every Tier-1 checkpoint and on batch reviews PC-B01…B07. Its done-when: the
nightly tier is green or triaged, and `HUMAN-TESTING.md` exists with a one-command local smoke
that Ahmad can run on a clean clone. Then a Fable scenario review walks the main journeys end to
end: sign in, open a tenant, land on home, create a user, invite them, assign a role with a
filtered permission, edit settings, browse centers in a tree, search, open details, edit, hit a
save conflict, deactivate. Its findings are fixed before the gate closes, and Ahmad is notified.

## 8. Reviews

### 8.1 Per checkpoint (PROCESS.md §4)

**Every checkpoint:** implement → commit 1 → **Review 1** → fix or push back → commit 2 →
integrate. **Checkpoints on the two-review list** (security-bound or complex, PROCESS.md §3) add
**Review 2** → fix or push back → commit 3 before integration. Every commit is pushed when it is
made. A checkpoint off the list is escalated to Review 2 when Review 1 reports a blocking finding
or four or more majors, or when fix 1 rewrites about a third of the checkpoint (PROCESS.md §4.4).

- **Review 1**: inherit (Opus), `high`. It checks commit 1's spec conformance section by section,
  test adequacy, correctness, and the AGENTS.md rules.
- **Review 2**: Fable for the security-bound checkpoints, inherit at `xhigh` for the complex ones.
  It reviews commits 1+2 adversarially for bugs, concurrency, security and cross-spec contracts,
  and judges the implementer's pushbacks.

### 8.2 Batch reviews (one per phase)

A batch review starts when every checkpoint of its phase is `done`, and it runs alongside the lanes as
a Workflow (PROCESS.md §13). Three read-only finders walk cross-checkpoint concerns (Fable while
the Fable meter is under 90%, otherwise Opus at `xhigh`). An Opus fixer verifies each finding
against the code, fixes it or pushes back, and integrates `PC-Bnn: batch review fixes`. Ahmad asked
for a reviewer that fixes. This split into finders and a fixer follows the lesson of the spec
rounds: Fable finds cross-spec gaps that grep-style checks miss, but per-file verification and
editing go better on Opus. It is recorded in DECISIONS.md.

| Batch | Phase (members: PLAN.md Appendix A) |
|---|---|
| PC-B01 | Phase 1, foundations and amendments: PC-000…PC-014 |
| PC-B02 | Phase 2, contracts and data-access core: PC-015…PC-031 |
| PC-B03 | Phase 3, queries, caching, access control, grid amendments: PC-032…PC-061 |
| PC-B04 | Phase 4, CRUD pipeline, web API, blobs, client package skeleton: PC-062…PC-084 |
| PC-B05 | Phase 5, distribution, reference stacks, jobs, discovery: PC-085…PC-102 |
| PC-B06 | Phase 6, client foundation, shell, entity model, home scaffold: PC-103…PC-123 |
| PC-B07 | Phase 7, search, details and stock pages: PC-124…PC-139 (followed by the MVP-gate scenario review, PC-140) |
| PC-B08 | Phase 8, Tier 2: PC-141…PC-172 |
| PC-B09…B12 | Tier 3: Excel (9a), reports (9b), dashboards (9c), the rest (9d) |

A phase spans backend and client work, so the finders of a large phase review each layer's
cross-checkpoint seams and the seams between the layers. When the gates allow, a batch may begin
on the phase's backend or client half as soon as that half is `done`. Its fix commit then covers
that half, and the batch closes when both halves have been reviewed.

## 9. Decisions and inputs from Ahmad

1. **Brand files** (0024 §7.6, 0027), settled in pre-flight: `tellma-brand` now has
   `tellma-wordmark-color-cropped.svg`, `tellma-wordmark-white.svg`, `tellma-mark-color.svg` and
   `tellma-mark-white.svg`. The mark is cropped from the wordmark's "a" and teal dot, and a designer
   should confirm it. **No spec mentions a watermark.**
2. **Vendor callback URIs** (0010 §A.4). By default the claude-web and codex-web seed entries are
   left out, because their callback URIs are not given. Supply them to include the entries.
3. **Tier 3 order** (§4): Excel first, confirmed by Ahmad.
4. **Review levers** (§3.3): both applied by Ahmad.
5. **Carried open question:** should `core.user.added`'s `actorName` become `LocalizedText`? By
   default the spec stands as written.

## 10. Risks

| Risk | Mitigation |
|---|---|
| Forecast error (±35%) | Strict tiers; calibrated forecasts after 15 and 40 checkpoints; Option B by design |
| Hitting a limit with agents in flight | Usage-gated launches and steps, then drain, park and wake (PROCESS.md §6–7). Every step commits and pushes, and the slot keeps uncommitted files |
| Orchestrator context loss over weeks of compaction | All state on disk (LEDGER.md, DEPS.md, git, reviews/). Recovery procedure in PROCESS.md §10 |
| Auto mode pausing after classifier blocks (3 in a row or 20 total) | One sanctioned command form (`cd <slot> && …` in Bash) matching the allowlist; blocks are reported, never retried as variants; Ahmad is notified at once (PROCESS.md §9) |
| A command run in the wrong checkout | Every brief pins absolute slot paths and the `cd <slot> &&` form; agents verify the branch before every commit; only the orchestrator writes in the main checkout |
| Circular contracts discovered late | Contract seed; 0015 contract-first types; implementers may add a missing contract early under its owner's name and say so |
| Merge conflicts between slots | Slots branch from the latest `platform-core`; integration merges, rebuilds and retests; lockfiles, API goldens, EF snapshots and migrations are regenerated, never hand-merged |
| Local contention (SQL containers, ports, dev databases, Playwright) | At most 4 slots; slot-unique ports and database suffixes (PROCESS.md §2, §11); integration suites one at a time per slot; e2e Chromium only |
| Windows-only flakiness (clipboard, WebKit timing) | Linux CI is the arbiter for known Windows-local failures (PROCESS.md §11) |
| A new Opus model mid-run | No Opus id is pinned; switching the session model applies to every new agent (PROCESS.md §12) |
| Unattended machine (updates, sleep) | SETUP.md: Windows Update paused, sleep off, Docker on login; cron wake-ups |

---

## Appendix A — checkpoint order

Priority order. Tier 1 = MVP, 2 = completion, 3 = may spill. "Est. LOC" includes tests.

| ID | Checkpoint | Spec | Tier | Est. LOC |
|---|---|---|---|---|
| | **Phase 1 Foundations and amendments** | | | |
| PC-000 | Run infrastructure: PR and nightly test tiers in CI, tag conventions | plan | 1 | 500 |
| PC-001 | Package pins and the composition root with both gates | 0011 | 1 | 4,000 |
| PC-002 | Identity store on the platform collation + Distribution seed kind | 0010-A | 1 | 5,000 |
| PC-003 | Queryex host-composition basics: rename, zones, restrictions, level, Prologue/Body, paging slots, versioned leaves, undeclared columns | 0010-F | 1 | 5,500 |
| PC-004 | icon and kbd foundations | 0023 | 1 | 3,500 |
| PC-005 | Column collations on class-derived standalone table types | 0010-G | 1 | 2,000 |
| PC-006 | Management API v1, tellma_kind, existingOnly | 0010-A | 1 | 2,500 |
| PC-007 | FilterTree.Via and FilterTree.Exists | 0010-F | 1 | 4,500 |
| PC-008 | Foundation seams: breakpoints, theme, translations and live direction, touch sizes, colour groups | 0010-B | 1 | 4,500 |
| PC-009 | Resource grant evaluator, control-plane audiences, ThirdParty seeds | 0010-A | 1 | 3,500 |
| PC-010 | Omitted parameters and constant statements | 0010-F | 1 | 3,500 |
| PC-011 | Public overlay, drawer and alert dialogs, popover sizes, button links and reasons, tab instantiate | 0010-D | 1 | 5,500 |
| PC-012 | Calendars: the 'uq' month table, startOfQuarter, 'et' | 0010-F | 1 | 6,500 |
| PC-013 | l10n: instants, zone labels, period styles and names, bidi-free text, grouping | 0010-D | 1 | 6,000 |
| PC-014 | Account entry URL and sign-in language | 0010-A | 1 | 3,000 |
| | **Phase 2 Contracts and data-access core** | | | |
| PC-015 | Cross-spec contract seed (wire seed, CRUD/validation contracts, endpoint and asset contracts) | 0015/0016/0014/0024 | 1 | 6,500 |
| PC-016 | Caching/settings/localization contracts and the three calendar systems | 0013 | 1 | 5,500 |
| PC-017 | Blob primitives, content sniffing and the file-system store | 0017 | 1 | 3,200 |
| PC-018 | Entity contract, annotations, conventions, table-type binder and the TellmaDbContext model | 0012 | 1 | 6,000 |
| PC-019 | Language catalogue, value sets and negotiation | 0013 | 1 | 5,000 |
| PC-020 | Status and identity indicators | 0023 | 1 | 5,500 |
| PC-021 | Entity metadata, startup validation, natural keys, display-name metadata and fingerprints | 0012 | 1 | 4,500 |
| PC-022 | ICU localizer factory, LocalizationScope and Core resources | 0013 | 1 | 5,000 |
| PC-023 | Chips and feedback surfaces | 0023 | 1 | 5,500 |
| PC-024 | Catalog database, registry, connection resolution and catalog write side | 0011 | 1 | 6,500 |
| PC-025 | Request context, holder, access guard, scope factory and server links | 0011 | 1 | 3,000 |
| PC-026 | Dialogs and layout | 0023 | 1 | 4,500 |
| PC-027 | Data contracts, batch assembly, contributor stages, schema guard and the shared SQL fixture | 0012 | 1 | 6,000 |
| PC-028 | Persist frame, error taxonomy, retry, commit probe, instruments and fault injection | 0012 | 1 | 6,000 |
| PC-029 | Access tables, entities, seeds and fixture migrations | 0014 | 1 | 4,000 |
| PC-030 | Form controls | 0023 | 1 | 6,500 |
| PC-031 | Pager, tree, drag-drop and reorder list | 0023 | 1 | 6,000 |
| | **Phase 3 Queries, caching and access control** | | | |
| PC-032 | Web host: pipeline, route groups, tenant middleware, audit, CSRF and tenantless surfaces | 0011 | 1 | 7,500 |
| PC-033 | Queryex host integration: schema provider, Rows/Count/constants, entity queries, materializer, display names | 0012 | 1 | 7,000 |
| PC-034 | The migrator: commands, provisioning steps, grants and database creation | 0011 | 1 | 6,500 |
| PC-035 | Notification and realtime contracts, registries and null publisher | 0021 | 1 | 3,000 |
| PC-036 | Jobs contracts | 0020 | 1 | 2,500 |
| PC-037 | Id allocator and the save emitter | 0012 | 1 | 7,000 |
| PC-038 | Version tags: registry, prelude/guard/bump contributors, snapshots, migrator seed | 0013 | 1 | 6,000 |
| PC-039 | Securables registry, startup audit and securables metadata section | 0014 | 1 | 3,500 |
| PC-040 | Analyzers TELLMA0001–0006 and their packaging | 0012 | 1 | 4,000 |
| PC-041 | Rich data-driven menu | 0010-C | 1 | 4,500 |
| PC-042 | Update and delete statements and the tree statements | 0012 | 1 | 5,500 |
| PC-043 | Evaluation core: UserAccess.Decide, composition, owners, drift classification | 0014 | 1 | 5,500 |
| PC-044 | VersionedCache and [Cacheable] entities | 0013 | 1 | 4,500 |
| PC-045 | Grid key map through kbd and grid glyphs through tm-icon | 0010-C | 1 | 3,000 |
| PC-046 | String pack, label provider and resource audit | 0013 | 1 | 5,000 |
| PC-047 | Sessions, BFF, back-channel logout, timers and in-proc identity mode | 0011 | 1 | 6,500 |
| PC-048 | List-page grid features: activation, driver, sort, select-all, row classes, gutter | 0010-C | 1 | 5,000 |
| PC-049 | Tenant settings: tables, keys, store, cold load, provisioning step, negotiation initializer | 0013 | 1 | 7,000 |
| PC-050 | Connect prologue, caches, guarded runner and the Order-100 initializer | 0014 | 1 | 7,500 |
| PC-051 | Header navigation, header actions, row-end column | 0010-C | 1 | 4,000 |
| PC-052 | IAccessEvaluator, deny re-check, witness, EvaluateFor and the holders resolver | 0014 | 1 | 4,500 |
| PC-053 | Tellma.Core.Imaging: SkiaSharp processor and PDF thumbnails | 0017 | 1 | 3,000 |
| PC-054 | scrollToRow and remembered tree expansion | 0010-C | 1 | 3,000 |
| PC-055 | Identity-server client | 0018 | 1 | 2,500 |
| PC-056 | Popup cell editors and final grid ceilings | 0010-C | 1 | 3,500 |
| PC-057 | Two-line options, page context, grid cell options, picker glyphs | 0010-E | 1 | 2,500 |
| PC-058 | tm-entity-multi-picker | 0010-E | 1 | 4,000 |
| PC-059 | Client-cache sweep, blob cancellation, basics glyphs | 0010-D | 1 | 3,000 |
| PC-060 | tm-multi-select, option groups, foundation glyphs through tm-icon | 0010-B | 1 | 4,000 |
| PC-061 | Tiles | 0023 | 1 | 4,500 |
| | **Phase 4 CRUD pipeline and web API** | | | |
| PC-062 | Pipeline contracts: Crud, Validation, Errors namespaces and API golden | 0015 | 1 | 4,500 |
| PC-063 | Stack registry, descriptor derivation, realizers, startup checks, securable contributor | 0015 | 1 | 5,500 |
| PC-064 | Row-set and related converters, key-typed entity envelope, route model and fingerprints | 0016 | 1 | 3,500 |
| PC-065 | Repo-root workspace, package skeletons, lint presets and decimals | 0024 | 1 | 4,000 |
| PC-066 | Validation engine: context loader, parking scheduler, confirmations, attribute walker | 0015 | 1 | 4,000 |
| PC-067 | Declaration API, metadata contract and the config check | 0024 | 1 | 4,000 |
| PC-068 | Pipeline skeleton and the query path (stacks and child surfaces) | 0015 | 1 | 5,500 |
| PC-069 | tm-boot.js, boot page, session and launch mark | 0024 | 1 | 4,000 |
| PC-070 | Details plan, extras, record permissions, get-by-parent-ids, all cached | 0015 | 1 | 4,000 |
| PC-071 | TmApi HTTP layer | 0024 | 1 | 5,000 |
| PC-072 | Save pipeline, pre-persist: ownership, preprocessing, ids, child sync, RT1 and validation | 0015 | 1 | 6,000 |
| PC-073 | tellma-client CLI: emit, serve, build and checks | 0024 | 1 | 6,000 |
| PC-074 | Save pipeline, persist and commit: assembly, post-check, read-back, re-entry, translation | 0015 | 1 | 6,500 |
| PC-075 | Client store and tab bus | 0025 | 1 | 4,500 |
| PC-076 | Deletes, activate/deactivate fast path and the general action path | 0015 | 1 | 5,500 |
| PC-077 | IApiActionInvoker, write frames and enlisted writes | 0015 | 1 | 6,500 |
| PC-078 | Write side: guards, validators, companion insert, preference bag, bootstrap, drift scanner | 0014 | 1 | 7,000 |
| PC-079 | SettingsService: client, entity-tags, details, save, refresh-caches | 0013 | 1 | 5,500 |
| PC-080 | Endpoint projection, binding, endpoint metadata, contract middleware, audit rules | 0016 | 1 | 6,500 |
| PC-081 | Problem details, exception mapping, headers and the telemetry filter | 0016 | 1 | 5,000 |
| PC-082 | Limits, rate and concurrency policies, timeouts, compression, caching, OpenAPI | 0016 | 1 | 3,500 |
| PC-083 | Blob entity, kind registry, options, startup check and staging | 0017 | 1 | 5,500 |
| PC-084 | Blob references: validator, effect, attach/release, resolve, reconcile and the FK exemption | 0017 | 1 | 5,500 |
| | **Phase 5 Distribution, reference stacks and jobs** | | | |
| PC-085 | Reference distribution Acme and the Azure defaults bundles | 0011 | 1 | 5,500 |
| PC-086 | Server emit: client entity model, enums, labels, action fields, .d.ts | 0029 | 1 | 5,500 |
| PC-087 | GL module: Center, CenterService, GlFeature and sample centers | 0018 | 1 | 4,000 |
| PC-088 | Users stack: hooks, invite, issue-credentials, invitation status, side effects | 0018 | 1 | 6,500 |
| PC-089 | Blob web surface, Azure adapter and the Azure defaults | 0017 | 1 | 5,000 |
| PC-090 | Self-service: me, me/save, me/locale, preference bags and the activity extra | 0018 | 1 | 4,500 |
| PC-091 | Roles stack extras and AccessService | 0018 | 1 | 2,500 |
| PC-092 | AcmeFeature: entities, actions, settings and development data | 0018 | 1 | 5,000 |
| PC-093 | InitialTenant migration, deployed bootstrap and distribution parity | 0018 | 1 | 3,500 |
| PC-094 | Acme client app and C# metadata contributors | 0024 | 1 | 3,000 |
| PC-095 | Web integration suite over the acme reference distribution and snapshot gates | 0016 | 1 | 4,500 |
| PC-096 | client/sync service, TmCacheRefresher and TmLaunchCache | 0025 | 1 | 5,500 |
| PC-097 | Job schema, handler registry and enqueue | 0020 | 1 | 5,000 |
| PC-098 | Lease statements, outcomes and the completion transaction | 0020 | 1 | 8,000 |
| PC-099 | Notifier, notifications schema and rendering | 0021 | 1 | 7,000 |
| PC-100 | JobWorker hosting, polling, drain and the gap hold | 0020 | 1 | 7,500 |
| PC-101 | Printer-based entry points and discovery additions | 0010-F | 1 | 4,000 |
| PC-102 | queryex/discover server endpoint | 0029 | 1 | 3,000 |
| | **Phase 6 Client foundation, shell and entity model** | | | |
| PC-103 | Bootstrap, derived routes, URLs, query codec and route host | 0024 | 1 | 5,500 |
| PC-104 | Localization, strings, server text and theme | 0024 | 1 | 5,000 |
| PC-105 | Primary entity registry: tmEntity, TmEntities, TmDisplayNames, TmPermissions | 0029 | 1 | 6,000 |
| PC-106 | Shortcut registry, tmSearchNormalize and shell seams (primary) | 0027 | 1 | 3,500 |
| PC-107 | Preferences, page state, drafts, tree expansion and recents | 0025 | 1 | 4,500 |
| PC-108 | Leader election, leader documents and degraded polling | 0025 | 1 | 3,000 |
| PC-109 | Primary attention seams: TmBanners, TmInputIdle, platform banners, attention documents | 0028 | 1 | 2,500 |
| PC-110 | Service worker updates, error presentation and session paths | 0024 | 1 | 5,500 |
| PC-111 | Page-context registry, page actions and dispatcher (primary) | 0026 | 1 | 3,500 |
| PC-112 | TmTelemetry pipeline, TmErrorHandler and redaction | 0026 | 1 | 4,000 |
| PC-113 | TmRequestLog, TmAppVersion, Copy diagnostics text and platform measurements | 0026 | 1 | 3,500 |
| PC-114 | Acme e2e host and the foundation's Playwright flows | 0024 | 1 | 5,000 |
| PC-115 | Navigation registry, core entries and the pins model | 0027 | 1 | 3,500 |
| PC-116 | /shell entry point: frame, tenantless shell, tenant picker, signed-out page | 0027 | 1 | 4,500 |
| PC-117 | /entity entry point: select plans, formats, typed values and blob checks | 0029 | 1 | 5,000 |
| PC-118 | Tenant shell, app bar, markers, session menu, About and Shortcuts dialogs | 0027 | 1 | 6,500 |
| PC-119 | Generic entity picker binding, tm-entity-field and tm-entity-multi-field | 0029 | 1 | 4,000 |
| PC-120 | Client Queryex (lexer, builder, printer) and column filter controls | 0029 | 1 | 5,500 |
| PC-121 | Expression editor and path tree picker | 0029 | 1 | 5,500 |
| PC-122 | Launcher and global search | 0027 | 1 | 5,500 |
| PC-123 | Home page scaffold: /dashboards entry point, grid and tile, recents widget, TmHomeHost | 0035 | 1 | 4,000 |
| | **Phase 7 Search, details and stock pages** | | | |
| PC-124 | Search query model, URL canonicalization and request builder (pure logic) | 0030 | 1 | 3,500 |
| PC-125 | Shared entity-action runner, generated action dialog and the confirmation loop (@tellma/core/entity) | 0031 | 1 | 4,000 |
| PC-126 | Search host, grid view, read lifecycle, states, paging, Create and Refresh | 0030 | 1 | 6,500 |
| PC-127 | Details host, sources, context, form, modes, loading and the header | 0031 | 1 | 6,000 |
| PC-128 | Search field, type-to-search, highlight driver, pending Enter and the undo anchor | 0030 | 1 | 3,000 |
| PC-129 | Layout kit: cards, fields, tabs, aside, audit, extras and validation schemas | 0031 | 1 | 4,500 |
| PC-130 | Images, files, child grids and child files | 0031 | 1 | 4,000 |
| PC-131 | Tiles view, view toggle, tabs, column filters and the Filters popup | 0030 | 1 | 5,500 |
| PC-132 | Page state return and trees (browse, expansion memory, search tree, fallback) | 0030 | 1 | 4,000 |
| PC-133 | Save flow, error summary, server-error mapping, conflict/deletion dialogs and the leave guard | 0031 | 1 | 5,500 |
| PC-134 | Page actions, the Actions menu, delete, activate and entity actions on the page | 0031 | 1 | 3,500 |
| PC-135 | Drafts and reconciliation, state restoration and the child-row deep link | 0031 | 1 | 4,500 |
| PC-136 | /admin scaffold, stock declarations, the GL pack with Centers, and the five locale fields | 0032 | 1 | 4,500 |
| PC-137 | Users: fields, memberships with window editor, Sign-in tab, invite and credentials dialogs, signature | 0032 | 1 | 6,500 |
| PC-138 | Settings: settings source, metadata-driven tabs, tm-setting-field, General tab, save diff and cache effects | 0032 | 1 | 6,500 |
| PC-139 | Roles: securables on the client, permissions editor with live filter checks, members, drift | 0032 | 1 | 5,500 |
| PC-140 | MVP gate: human-testing readiness | plan | 1 | 1,500 |
| | **Phase 8 Completion (Tier 2)** | | | |
| PC-141 | Acme tenant-data verdict assertions after the CRUD stack lands | 0011 | 2 | 2,000 |
| PC-142 | StackConformanceTests base, instrument pins, docs | 0015 | 2 | 3,500 |
| PC-143 | Notification surfaces of the users stack | 0018 | 2 | 2,500 |
| PC-144 | Scheduler and the Schedule stack | 0020 | 2 | 6,500 |
| PC-145 | Job admin surface, labels and platform maintenance handlers | 0020 | 2 | 5,000 |
| PC-146 | Inbox, preferences, delivery and retention handlers | 0021 | 2 | 7,000 |
| PC-147 | TellmaHub, SignalR publisher and hosting modes | 0021 | 2 | 6,500 |
| PC-148 | Outbox row, enqueue and the email notification channel | 0022 | 2 | 6,000 |
| PC-149 | Dispatch handler | 0022 | 2 | 4,500 |
| PC-150 | Server relay POST /api/client-telemetry | 0026 | 2 | 3,000 |
| PC-151 | Cross-cutting accessibility and stability gates | 0023 | 2 | 2,500 |
| PC-152 | Device time zone detection and prompt | 0024 | 2 | 2,500 |
| PC-153 | TmHub realtime connection | 0025 | 2 | 3,000 |
| PC-154 | /support entry point: TmSupport facade, state dump, download and Diagnostics panel | 0026 | 2 | 3,500 |
| PC-155 | /attention model: TmAttention, TmArrivals, TmAttentionText, banner host | 0028 | 2 | 4,500 |
| PC-156 | Bell, tray, panels and tm-assignment-list | 0028 | 2 | 5,500 |
| PC-157 | Notifications section of My settings | 0028 | 2 | 3,000 |
| PC-158 | Column editor, expression columns and the columns preference | 0030 | 2 | 3,000 |
| PC-159 | Selection, the selection bar, bulk actions and declared page actions | 0030 | 2 | 2,500 |
| PC-160 | Picker mode, the drill-through list mode and the `search` hatch | 0030 | 2 | 2,500 |
| PC-161 | Paging through search results, picker create/edit pages and page/form context | 0031 | 2 | 4,000 |
| PC-162 | The page hatch, compact header and TmPageActionBar | 0031 | 2 | 2,500 |
| PC-163 | Pinned bar, TmPins and the Manage pins dialog | 0027 | 2 | 6,000 |
| PC-164 | Notifications, Assignments and Jobs pages, open route and job details | 0028 | 2 | 5,500 |
| PC-165 | Access tab, the /access page and another user's Preferences dialog | 0032 | 2 | 4,500 |
| PC-166 | My settings hub: Profile source page, Language & region, Display, Notifications frame | 0032 | 2 | 4,000 |
| PC-167 | Home page completion: assignment widgets, full home geometry and home e2e | 0035 | 2 | 7,500 |
| PC-168 | Entity-model e2e, geometry and RTL specs on Acme | 0029 | 2 | 2,500 |
| PC-169 | Shell e2e, responsive and axe battery on Acme | 0027 | 2 | 3,000 |
| PC-170 | Acme search declarations, e2e, axe and budget suites | 0030 | 2 | 3,000 |
| PC-171 | Acme details layouts, e2e, axe, geometry and budget suites | 0031 | 2 | 3,500 |
| PC-172 | Acme extensions, fixtures, e2e, axe and budget suites for the stock pages | 0032 | 2 | 3,500 |
| | **Phase 9a Excel import and export (Tier 3)** | | | |
| PC-173 | Excel contracts, cell codec, number formats and the workbook/table writer | 0019 | 3 | 6,500 |
| PC-174 | Excel row source | 0015 | 3 | 2,500 |
| PC-175 | Exports: planners, manifest and the synchronous export operations | 0019 | 3 | 6,000 |
| PC-176 | Import intake, reader and mapping; Export/Import entities; inspect-import | 0019 | 3 | 6,500 |
| PC-177 | Import semantics and the synchronous import | 0019 | 3 | 7,500 |
| PC-178 | Background exports and chunked, resumable background imports | 0019 | 3 | 7,500 |
| PC-179 | Export, the import dialog and TmImportResultView | 0030 | 3 | 4,000 |
| PC-180 | Exports and Imports pages with job following and pending confirmations | 0032 | 3 | 3,000 |
| | **Phase 9b Reports (Tier 3)** | | | |
| PC-181 | Measures, partial columns and the C# measure evaluator | 0010-F | 3 | 7,500 |
| PC-182 | Report stack and the generic sharing model | 0033 | 3 | 7,500 |
| PC-183 | Audience resolver | 0033 | 3 | 3,500 |
| PC-184 | Definition document, structural validator, default display and parameters vocabulary | 0033 | 3 | 6,000 |
| PC-185 | Report compiler: planning for list, summary and chart; compile check and parameter inference | 0033 | 3 | 6,500 |
| PC-186 | Run engine for list, summary and chart; results, defaults, drill and measure errors | 0033 | 3 | 7,000 |
| PC-187 | Pivot: leaf statement, member statements and the cube builder | 0033 | 3 | 7,000 |
| PC-188 | Coded reports: declaration, registry, interceptor, IReportResults, metadata, Acme credit exposure | 0033 | 3 | 5,000 |
| PC-189 | MCP server: host, authentication, request filter, read tools | 0016 | 3 | 5,000 |
| PC-190 | MCP write tools, write safety and MCP integration suite | 0016 | 3 | 5,000 |
| PC-191 | Export: layout, Excel and Markdown writers, sync and background routes | 0033 | 3 | 6,500 |
| PC-192 | Seeded standard reports, child-root coverage, Acme wiring and nightly performance suite | 0033 | 3 | 3,500 |
| PC-193 | tm-pivot core-ui entry point | 0034 | 3 | 7,500 |
| PC-194 | tm-chart core-ui entry point (ECharts facade) | 0034 | 3 | 7,000 |
| PC-195 | @tellma/core reports: declarations, wire mirrors, host core, list and summary renderers | 0034 | 3 | 8,000 |
| PC-196 | Parameter field, bar, URL codec, precedence and page state | 0034 | 3 | 5,500 |
| PC-197 | Pivot and Chart renderers, drill-through and Excel export | 0034 | 3 | 7,000 |
| PC-198 | Report page, coded report page, reports page and sharing dialogs | 0034 | 3 | 7,500 |
| PC-199 | Report studio: hosting, layout, sections, drops and kind switching | 0034 | 3 | 8,000 |
| PC-200 | Report studio: live parameter inference, validation feedback and overlay preview | 0034 | 3 | 6,000 |
| PC-201 | Acme end-to-end, geometry, keyboard, visual, axe and budgets | 0034 | 3 | 4,500 |
| | **Phase 9c Dashboards (Tier 3)** | | | |
| PC-202 | Dashboard server: tables, service, validator, view extra, read-through and audience | 0035 | 3 | 7,500 |
| PC-203 | Dashboard page (view mode), report widget, parameters, scheduler and present mode | 0035 | 3 | 8,000 |
| PC-204 | Dashboard editor: canvas, move/resize, gallery, dialogs and save | 0035 | 3 | 7,500 |
| PC-205 | Dashboards e2e on Acme, geometry, feedback, axe and budgets | 0035 | 3 | 3,500 |
| | **Phase 9d Assignments, support and operations (Tier 3)** | | | |
| PC-206 | Assignments write side: table, IAssignments, statements, [Assignable] realizer | 0028 | 3 | 5,000 |
| PC-207 | Assignments read side: stack, summary, seen, trail extra, access | 0028 | 3 | 3,500 |
| PC-208 | Attention e2e and axe battery on Acme | 0028 | 3 | 2,500 |
| PC-209 | Support requests server stack | 0026 | 3 | 5,000 |
| PC-210 | Screenshot capture and the support dialog | 0026 | 3 | 4,500 |
| PC-211 | Support requests stock pages and the 0026 e2e battery | 0026 | 3 | 3,000 |
| PC-212 | Sweep, reconcile, built-in schedules, blobs verify and alert queries | 0017 | 3 | 3,000 |
| PC-213 | core.file-retention | 0020 | 3 | 1,300 |
| PC-214 | Delivery events, verbs, retention and telemetry | 0022 | 3 | 4,000 |
| PC-215 | Schedules and Outgoing emails pages (incl. the sandboxed email body viewer) | 0032 | 3 | 3,500 |
| PC-216 | Multi-tab e2e scenarios and the hidden-leader soak | 0025 | 3 | 2,500 |
| PC-217 | Client ID metadata documents (document clients) | 0010-A | 3 | 5,500 |

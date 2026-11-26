# PC-F86: Session-guard spec failure under the import delay

## Implementer notes

### Root cause: the delay harness, not the spec or the product

The guard spec's changed-user test waits on one thing: `TmSessionPaths.#load()`, the
`import('./tm-session-flows')` that every session path chains on. The chunk is already in the
spec's static graph (`tm-session.testing.util` imports `TmSessionFlows`), so without the
injection the wait is a few tasks, and under the 300 ms injection it is 300 ms plus a task. It
measured 305-374 ms in every delayed full run below, and in PC-F85's seven. The failing run
(4,769 ms) means the page's main thread did not run vitest's 1 s timer until about 4.5 s: the
page was starved for several seconds. Nothing in the path waits on a real timer or a fetch.

The starvation comes from the harness the integrations use (`inject_lazy_delay.py`, PC-114's
26-site run). Each wrapped import spins `await new Promise(tick => { const channel = new
MessageChannel(); ... })` until 300 ms pass. It creates a new channel per tick and never closes
it. A port with an `onmessage` handler stays alive while its document lives, so every 300 ms wait
leaks about 10,000 channels (measured: 8,455-10,768 ticks per wait). A spec file with many
wrapped calls (the per-call imports in `tm-http-api`, `tm-api-seams`, `tm-entity-pickers`) leaks
hundreds of thousands to millions, and a spec under a bare `vi.useFakeTimers()` spins forever,
because Vitest 4 fakes `performance.now()` by default and the spin's deadline never comes.

Measured in a throwaway probe spec (not committed), on an idle page:

- Twenty 300 ms waits in a row: the leaky spin overran by 1,791 and 958 ms (two runs), with a
  single main-thread gap of 1,010 and 836 ms. One channel per wait, closed at the end (PC-F85's
  form): overrun 5-6 ms, longest gap 7-18 ms. A real timer: overrun 23-31 ms, longest gap 12-47 ms.
- A same-origin frame holding 189,000 leaked channels stalled the parent page for 1,099 ms while
  it lived. Removing a frame holding 2.27 million (what the runner does to the previous spec file's
  frame) blocked the main thread for 4,488 ms.

The runner gives each of its 12 parallel pages its own browser context and renderer, so a stall
inside one page does not stop another directly. But with every page running its own leaky spins,
garbage collections (with their helper threads) and frame teardowns at once, the run saturates
the machine's 20 logical CPUs, and a page with little work of its own, such as the guard spec, can
be starved for seconds. The full runs show it (the same tree, pc-2 at fdafa02b, the core project
in full, 2,587 tests):

| Harness | Runs | Failed | Sum of test durations | Tests over 2 s | Guard (changed user) |
|---|---|---|---|---|---|
| leaky spin (the integration harness) | 3 | 0, 5, 1 | 500, 541, 507 s | 25, 36, 28 | 370, 356, 362 ms |
| one channel per wait (PC-F85's form) | 3 | 0, 0, 0 | 422, 436, 436 s | 11, 9, 9 | 374, 306, 365 ms |
| real setTimeout, taken at module load | 3 | 2, 2, 2 (see below) | 391, 397, 412 s | 7, 7, 7 | 369, 367, 361 ms |

Five of the six leaky-spin failures are starvation. Three waits on state (`TmArrivals`,
`injectTmQueryexDraft`, `TmErrorHandler`) report the condition did not hold "within 5,445 ms",
"4,144 ms" and "5,890 ms": the loop checks its deadline before each step, so one step (a
`setTimeout(0)` and a render) took over a second. Two `vi.waitFor` checks (`TmEntityField`,
`TmDisplayNames`) ran out of their 1 s. None of the five recurs under either leak-free form. The
sixth, `TmZoneCheck`, is the spec bug described below. I did not reproduce the guard
failure itself in three leaky runs (it was 1 in several before), but its symptom, a timer
seconds late in a page with almost no work, is the one the leaky runs show elsewhere.

### PC-F93 (tm-arrivals): the same cause

PC-F93's failure is the same class: a `tmSettleUntil` that timed out at 4,109 ms with the toast
not yet shown. In the leaky runs the `TmArrivals` tests peaked at 517, 6,961 and 1,668 ms (sum
6.7-15.3 s, and run 2 failed "toast a replaced row again under its key" with "2 toasts shown" not
holding within 5,445 ms). Under both leak-free forms their maximum is 390-429 ms (sum 5.8-6.4 s),
and the PC-F93 test itself 351-377 ms. The harness fix below explains PC-F93; no spec change is
needed for it.

### The fix to the harness (a test-time tool, not committed)

Wait on the browser's own `setTimeout`, taken when the module loads, before any spec fakes the
timers: `(new Promise<void>((done) => lazyDelayRealSetTimeout(done, 300))).then(() => import(X))`
with `const lazyDelayRealSetTimeout = globalThis.setTimeout;` at the top of each wrapped file.
That is what a slow chunk does in a browser: the main thread is idle while it downloads. Faked
timers leave it alone, it leaks nothing, and it spins nothing. A drop-in replacement for
`inject_lazy_delay.py`/`remove_lazy_delay.py` with the same argument and scope is in the session
scratchpad as `PC-F86-inject_lazy_delay.py` and `PC-F86-remove_lazy_delay.py` (checked: inject
then remove leaves a copy of client/projects/core byte-identical; 35 sites in 25 files on
fdafa02b). A checked-in tool is not needed: the harness rewrites sources for a local run and is
reverted before any commit.

The timer form exposes one pre-existing spec bug deterministically: `tm-zone-check.spec`
"checks on each entry into a tenant, and not on a navigation within it" and "records nothing
after any other failure, so the next check tries again" fail in every timer run, alone and in full
runs, and one of them failed once in the leaky runs too. Their setUp settles a fixed number of
turns while the startup check still waits on the delayed Entities chunk (`tm-entities`), so the
startup check writes after the spec changes the stored zone. That is the turn-counting class
CONTRIBUTING.md forbids. It is outside this item: it needs its own fix item before the timer form
gates an integration.

### What changed in the repo

- `client/projects/core/tellma-core/src/session/tm-session-guard.spec.ts`: both denials wait with
  `tmSettleUntil` on the flows having run a path, then assert the exact call. These waits cross the
  flows chunk's load, which CONTRIBUTING.md says a spec waits on with the 4 s named deadline; the
  1 s `vi.waitFor` was the one shape it does not allow. This is alignment, not the fix: the fix is
  the harness, and a 4 s budget alone would not have saved the 4,769 ms run.
- `client/projects/core/tellma-core/src/session/tm-session.spec.ts`: the same change for the five
  `TmSessionPaths` waits that cross the same load (expiry, the bus routing, the four paths, and
  the two fallbacks).
- `client/projects/core/tellma-core/src/routes/tm-app.testing.util.ts`: `tmWaitUntil` measured its
  deadline with `performance.now()`, documented as left alone by faked timers. Vitest 4's bare
  `vi.useFakeTimers()` fakes it too, so under it the wait could never fail and ran into the test
  timeout. It now takes the page's own `performance.now` at module load, as the file already does
  for `setTimeout`, and the TSDoc says so.
- `client/projects/core/tellma-core/src/routes/tm-app.testing.util.spec.ts`: the faked-timers
  deadline test uses a bare `vi.useFakeTimers()`, which fakes the clock too. Before the util fix it
  timed out at 8 s (checked); after it, it passes.
- `CONTRIBUTING.md`: one clause, that `vi.waitFor` with its 1 s budget is not the wait the rule
  means.

No product code changed.

### Tests run

- Spec alone, no delay: tm-session-guard, tm-session and tm-app.testing.util specs, 28/28.
- Spec alone under the delay: guard and session specs 25/25 twice under the timer form and once
  under the leaky spin (guard 307-322 ms).
- Full core (`ng test core`) under the 300 ms delay, with this change: three runs under the timer
  form, 2585/2587 each, the two failures being the tm-zone-check pair above; guard 309-314 ms.
  Before this change: the nine runs in the table.
- Client gates: eslint on the four files, lint:test, typecheck. `dotnet build Tellma.slnx` and
  `dotnet format --verify-no-changes` (no .NET change).

### Open risks and what to examine hardest

- The attribution of the guard's own 4,769 ms run rests on the class of failure, not on a
  reproduction of that test: three leaky runs failed six other tests by the same starvation and
  not the guard. Judge whether the evidence (the probe, the A/B/C runs, PC-F93's numbers) carries
  it.
- Whether the tm-session.spec changes are in scope. They are the same wait over the same load;
  I changed them so the session folder follows one rule.
- The tm-zone-check pair: the timer form makes it fail every time. Check my reading of its cause
  before an item is opened.

## Review 1

### Findings

Reviewed 7b8cd2c8 against the PC-F86 and PC-F93 ledger rows (PC-F86 has no CHECKPOINTS.md
entry: it is a fix item, and no spec section is in scope), PLAN.md §6-§7, CONTRIBUTING.md's rule
for waits across a chunk load, and the live DECISIONS.md (D-294). I read the harness files and
did not edit them. Everything below ran in pc-2 at 7b8cd2c8. The tree was clean before and after,
and every injection was reverted.

- Harness coverage and revert, on two copies of `client/projects/core` (node_modules and dist
  left out): both injectors report 35 sites in 25 files, with the same file list and counts.
  Each remover leaves its copy byte-identical (`diff -r` against the original prints nothing).
  On the real tree, each remover on `git diff --name-only` removed 35 sites and `git status`
  came back empty. All 25 files start with the copyright comment and are LF, so no `///`
  directive or BOM sits where the timer form puts its first-line head. The regex the two forms
  share misses no live `import(`: the only unmatched ones in non-spec sources are in comments.
- (a) The stall, reproduced outside the runner: a Playwright probe (scratchpad only) ran twenty
  300 ms waits on an idle about:blank page with a 5 ms heartbeat.

  | Form | Overrun (two rounds) | Longest main-thread gap |
  |---|---|---|
  | leaky spin | 221 and 2,017 ms | 631 and 2,127 ms |
  | real setTimeout | 61 and 46 ms | 8 and 7 ms |

- The affected specs: guard, session, app.testing.util, zone-check and arrivals, plus
  session-flows under the timer form.
  - No delay: guard, session and util 28/28.
  - Leaky spin, twice: 72/73, then 73/73. The failure was the zone-check test "checks on each
    entry into a tenant": `expected [London] to deeply equal []`.
  - Timer form, twice: 94/96 both times. The failures were the zone-check pair and nothing else.
- One full core run (`ng test core --watch=false`) under each form, with this change:
  - Leaky spin: 2586/2587, wall 126 s, sum of test durations 526 s. The one failure is
    starvation: `injectTmQueryexDraft` "drops an answer once the root, ..." reported "The
    condition did not hold within 4245 ms: the draft is checked" (the test took 7,109 ms).
  - Timer form: 2585/2587, wall 75 s, sum 417 s. The two failures are the zone-check pair.
- (e) Checked against the fake-timers code bundled in Vitest 4.1.9.
  - A bare `vi.useFakeTimers()` fakes every timer except nextTick and queueMicrotask, and
    `performance` is among them.
  - `hijackMethod` replaces the global `performance` with the clock's own object. The
    `performance.now.bind(performance)` taken at module load stays bound to the real object's
    native clock, so `realNow` is correct under any `toFake` set.
  - The util spec's changed test passes with it. Before the change, the loop could never reach
    its deadline: the clock moves only on an advance, and the step advances 0.
- (c) Cause confirmed in the code. `TmZoneCheck.#ready()` awaits `TmEntities.load()`, whose
  `import('./tm-entity-model')` is a wrapped site, and reads the stored `seen` zone only after
  that. `setUp` and the tests settle a fixed `tmSettle()` before acting. R1-3 covers the second
  test, which fails by a different mechanism.
- (d) PC-F93. From a stored copy to `TmToasts.show`, the chain is `TmArrivals.#arrive`, one
  wrapped import per TmArrivals instance (`setUp` preloads the chunk itself), then the show.
  Nothing else in it waits on a timer or a fetch.
  - So a 4,109 ms `tmSettleUntil` timeout on that chain can only mean the page ran no task for
    seconds. The leaky runs show that class of failure, here and in the implementer's table, and
    the leak-free runs do not.
  - arrivals passed in all four affected-spec runs and in both full runs.
  - As with the guard, this attributes by class and does not reproduce that test. I find the
    evidence enough: the probe, the A/B/C table and my two full runs all agree.
- Whether the session spec changes are needed: they align the specs with the rule; they are not
  the fix.
  - Under the timer form the flows wait is 300 ms plus a task, well inside `vi.waitFor`'s 1 s.
  - The 4 s deadline would not have saved a 4.5 s starvation either.
  - They do follow CONTRIBUTING.md's rule for a wait across a chunk load, so keeping them is
    right. R1-1 covers the one place the same reasoning was not carried through.
- The implementer's probe left an ignored screenshot,
  `client/projects/core/tellma-core/src/session/__screenshots__/pcf86-probe.spec.ts/`. The probe
  spec itself is gone. I found no tracked or other file affected.

R1-1 (minor) The session folder does not yet follow "one rule", and the new CONTRIBUTING clause
widens what breaks it.

- Where: `client/projects/core/tellma-core/src/session/tm-session-flows.spec.ts:168, 181, 196,
  204, 224, 236, 239, 248, 259`, and the later `vi.waitFor` calls in the file.
- What: each waits with `vi.waitFor`'s 1 s budget on work that starts from
  `rig.paths.expired(...)` or a bus message. That work reaches `TmSessionPaths.#load()`, the
  same wrapped `import('./tm-session-flows')` that this commit treats as a chunk load in
  tm-session.spec and tm-session-guard.spec.
- The review notes say "I changed them so the session folder follows one rule", but the
  folder's largest spec still uses the shape the commit rules out.
- CONTRIBUTING.md:23-24 now says outright that `vi.waitFor` "is not such a wait". 21 core spec
  files use it, and some of those calls cross chunk loads.
- Failure scenario: under starvation in any heavy full run, these 1 s waits fail the way the
  guard did. The new sentence also makes these files violations that no item tracks.
- Suggested fix, either of:
  - convert the flows spec's waits that cross the flows load to
    `tmSettleUntil(condition, description)` here;
  - or record the gap and add "vi.waitFor across a lazy import" to PC-F97's sweep, which
    already covers turn-counting past a lazy import in core.

R1-2 (minor) The proposed timer form's "faked timers leave it alone" fails for a wrapped file
that is itself a lazy chunk.

- Where: `C:/Users/ahmad/AppData/Local/Temp/claude/e8cd5d88a1e5fac2/55d8a508-95e7-4eec-9239-0e94c12f0353/scratchpad/PC-F86-inject_lazy_delay.py:22-23`.
- What: the head `const lazyDelayRealSetTimeout = globalThis.setTimeout;` runs when its own file
  is first evaluated, not when the spec starts. These wrapped files are reached only through
  `import()`:
  - `tm-zone-check.ts`, through `TM_ZONE_CHECK_LOAD`;
  - `tm-action-run.ts`, through `import('./tm-action-run')`;
  - `tm-tenant-picker.ts`, through the shell's tenantless chunk;
  - the details files, through `@tellma/core/details`;
  - `tm-leadership.ts`, through `@tellma/core/leader`.
- If such a file is first evaluated while a spec fakes `setTimeout`, it captures the fake. Its
  own wrapped import then waits on the fake clock, and stays bound to that clock for the rest of
  the file, even after `vi.useRealTimers()`.
- Example: `provide-tellma.spec.ts:102` fakes `setTimeout` and `performance` while it starts the
  app, which loads the leader and the zone check lazily.
- Failure scenario: such an import resolves only if the spec advances 300 ms. Otherwise a wait
  on its result fails, or a later test in the same file hangs on a dead clock.
- How likely: neither my full run nor the implementer's three hit it, so it is latent. The old
  form is worse in the same spot, since it spins forever under a faked `performance`. Still,
  the docstring and D-294's "faked timers leave it alone" do not hold in general.
- Suggested fix, either of:
  - use a delay no fake-timer installs: `scheduler.postTask(() => import(X), { delay: 300 })`.
    That is Chromium's Scheduler, which `@sinonjs/fake-timers` does not touch, and the unit
    runner is Chromium only. It needs no head line, and the remover keys on its one prefix;
  - or narrow the docstring to "files evaluated before the spec fakes the timers".

R1-3 (minor) The zone-check pair also fails under the current harness, and the second test
fails by a different mechanism than the notes give. PC-F97 should carry both facts.

- The claim: the notes, D-294 and the PC-F97 row say the pair passes under the current harness
  "only because its leaky spin changes the timing".
- What I saw: in my two leaky affected-spec runs, "checks on each entry into a tenant" failed
  once. The implementer's leaky runs also failed one of the pair once.
- So the pair already turns red at random under the harness in force today, not only once the
  timer form replaces it.
- Why the timer form fails every time: the route chunk's and the entities chunk's 300 ms timers
  fire in the order they were set, while under the spin the two race.
- The two mechanisms:
  - "checks on each entry into a tenant": as the notes describe. The startup check reads `seen`
    after the spec's `setSeen(rig, 'Asia/Riyadh')`, so it writes. It fails at
    `tm-zone-check.spec.ts:304` with `[London]` against `[]`.
  - "records nothing after any other failure" (`tm-zone-check.spec.ts:395-407`): the spec
    changes no stored zone. The startup check, still in its entities wait, writes after the spec
    has scripted the 500. The explicit `rig.zone.check()` queues behind it, since
    `TmZoneCheck.check()` chains on `#tail`. The count ends at 3 writes, not 2, and the test
    fails at line 407.
- One fix covers both: wait for the startup check to finish instead of on `tmSettle()`. For
  example, `await rig.zone.check()`, or `tmSettleUntil` on its telemetry, at the end of `setUp`
  or in each test before it acts.
- Suggested fix, with no code change in this item:
  - reword the PC-F97 row and D-294 to "intermittent under the current harness, deterministic
    under the timer form";
  - name the second test's mechanism so that PC-F97's fix covers it;
  - consider raising PC-F97's priority, since it can already turn an integration red.

### Responses

- **R1-1: fixed in fix 1.** All 21 `vi.waitFor` waits in `tm-session-flows.spec.ts` now use
  `tmSettleUntil(condition, description)`, each followed by the exact assertion the wait held
  before (the two `{ timeout: 2_000 }` and `{ timeout: 3_000 }` budgets go too: the 4 s deadline
  covers them). Correction at integration: the session folder still has three `vi.waitFor` waits,
  in `tm-session-tabs.spec.ts:149, 153, 156`. They predate this item, and my earlier claim that
  none remained was wrong. Per the ruling, they and the other core specs' waits are left to
  PC-F97's sweep.
- **R1-2: fixed in fix 1 (harness only, not committed).** The scratchpad's
  `PC-F86-inject_lazy_delay.py` and `PC-F86-remove_lazy_delay.py` are replaced. Each site now waits
  on `scheduler.postTask(() => undefined, { delay: 300 })`, which Vitest 4.1.9's fake timers do
  not touch (its bundled fake-timers code has no `scheduler` or `postTask`). It falls back to
  `globalThis.setTimeout` only where `scheduler` is missing. There is no head line, so the
  remover keys on one prefix.
  - Proof, with a throwaway probe spec (deleted): under a bare `vi.useFakeTimers()` the probe
    first imports `tm-tenant-picker.ts` (reached only by `import()`, so first evaluated under the
    fakes), then calls its `TM_PICKER_ERROR_VIEWS` loader, a wrapped site, without advancing time.
    - Old head form: "stuck after 3017 ms". The import never resolved.
    - New form: "loaded after 458 ms".
  - `provide-tellma.spec.ts`, with its line-102 test (fakes `setTimeout`, `Date` and
    `performance` while it starts the app), passes 20/20 under both forms.
  - Inject then remove on a fresh copy of `client/projects/core` at 7b8cd2c8: 35 sites in 25
    files, the same file list as before. The removal reports 35 and leaves the copy with no
    differences. On the slot, removal left only this fix's own changes.
- **R1-3: no code change (ruled).** The orchestrator corrects the ledger. In this round's full run
  under the new form, the pair failed again, and nothing else failed.
- The probe's leftover screenshot folder,
  `client/projects/core/tellma-core/src/session/__screenshots__/pcf86-probe.spec.ts/`, is deleted,
  named exactly. So is this round's `pcf86-probe2.spec.ts` folder, and the then-empty
  `__screenshots__` folder.
- Tests for fix 1:
  - No delay: tm-session-guard, tm-session, tm-session-flows and tm-app.testing.util specs, 51/51.
  - The same four under the new harness: 51/51, guard 314-321 ms.
  - One full core run (`ng test core`) under the new harness: 2585/2587. The two failures are the
    tm-zone-check pair, and nothing else failed. Sum of test durations 394 s; 7 tests over 2 s.
  - eslint on the five touched files, lint:test, typecheck, and tsc on tellma-core's
    tsconfig.spec.json (only the existing 13 rootDir notices). dotnet format --verify-no-changes
    is clean.

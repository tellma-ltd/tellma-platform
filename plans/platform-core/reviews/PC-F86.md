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

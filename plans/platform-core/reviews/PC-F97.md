# PC-F97: zone-check turn counting and lazy-chunk waits

## Implementer notes

PC-F97 has no CHECKPOINTS.md entry. It is a fix item, scoped by its LEDGER row, D-294, D-295,
D-297, D-302 and the brief. No spec section is in scope, and no product code changed.

### The zone-check pair: both mechanisms reproduced, then fixed

Before the change, under PC-F86's postTask harness, `tm-zone-check.spec.ts` alone failed 2/28:

- "checks on each entry into a tenant…": `expected [ { timeZone: 'Europe/London' } ] to deeply
  equal []`. setUp's `tmSettle()` ends while the startup check still waits in `#ready()` on
  `TmEntities.load()`, whose `import('./tm-entity-model')` is a delayed site. The spec's
  `setSeen(rig, 'Asia/Riyadh')` then lands first, and the startup check writes.
- "records nothing after any other failure…": `expected [ … (3) ] to have a length of 2 but got
  3`. The startup check, still in its entities wait, writes after the spec scripted the 500. The
  spec's `rig.zone.check()` queues behind it on `#tail` and writes again, and the second explicit
  check makes 3.

The fix, all in `client/projects/core/tellma-core/src/l10n/tm-zone-check.spec.ts`:

- `startup(rig)` is the named wait for the startup check. It calls `rig.zone.ɵstart()`, which is
  what the app's idle callback does (both are idempotent), waits with `tmSettleUntil` for the
  first `TmZoneCheck.check` call ("the startup check started"), and then awaits the promise that
  call returned. setUp records the calls with `vi.spyOn(TmZoneCheck.prototype, 'check')` before
  navigating. The spec starts the triggers itself because an idle callback has no timeout: under
  the leaky harness, a first version that waited on the app's own idle callback failed once in
  three runs ("did not hold within 4125 ms: the startup check started"). The first test,
  "records a first run's zone at idle after the first render", still waits on the app's own idle
  start, so that path stays covered.
- Every test that changes what the check reads after setUp now waits for `startup(rig)` first,
  or scripts the change in setUp before the app starts:
  - `startup(rig)` replaces `tmSettle()` in the entry, burst, outcome-unknown and both lock tests,
    and the `seen === 'Asia/Riyadh'` wait in the visibility test.
  - The second test scripts the 500 through setUp's `respond`, waits for the startup check (write 1,
    rejected, reported once), and then the next check writes again (write 2). This is the test's
    claim, now made by the startup check and one explicit check.
  - The "outside the server's set" and "refusal" tests script their failure through `respond`, so
    the startup check, whenever it runs, meets the same answer.
  - The offline test gets a new `offline` option, so the server is unreachable before the startup
    check can write. Its first wait is `startup(rig)`.
  - The hidden-tab test hides the tab before setUp, so the startup check finds it hidden too.
  - The toast test spies on `TmToasts.prototype.show` before setUp, so a toast from the startup
    check is counted.
- The entry test's last wait is on the recorded zone, not the write count. The record lands after
  the write, so the old `writes === 1` wait could read `seen` before it changed.
- The two idle-gate tests wait on `idleWaits > 0` ("the check waits for the user to be idle"),
  counted by the scripted `TmInputIdle`, instead of `tmSettle()`. Under the delay, their old
  negative assertions passed before the check had even loaded the dialog's chunk.

### The sweep (CONTRIBUTING.md's rule for waits across a chunk load)

I read every `vi.waitFor` in client/projects/core, 23 files, against the 35 delayed sites (the
lazy `import()`s the harness wraps).

Moved to `tmSettleUntil` (or the spec's own `tmWaitUntil` wrapper under fake timers), each with a
named condition and followed by the exact assertion the wait used to make:

- `src/api/tm-http-api.spec.ts`, 6 waits: the session flows' `import('./tm-session-flows')`.
  The spec runs under fake timers, so these use a new `flowsRan()` over the spec's `flushUntil`,
  which now takes a description.
- `src/entity/tm-display-names.spec.ts`, 4 waits: `ɵtmReadDisplayNames` is in
  `import('./tm-entity-model')`, which each read awaits.
- `src/session/tm-session-tabs.spec.ts`, 3 waits (D-302): each tab's flows chunk. PC-F86's note
  that "the session folder now has no vi.waitFor" missed this file.

Left alone, because their waits cross no lazy chunk:

- tm-fake-locks, tm-locks, tm-client-store: Web Locks, BroadcastChannel and IndexedDB.
- tm-grid-header-nav: a long-press timer.
- tm-create-hub: the hub's own timers.
- tm-error-presenter: TmConfirm is static.
- provide-tellma: the spec supplies the loaders as plain promises.
- tm-localization: the store's boot.
- tm-updates: registration once the app is stable.
- tm-cache-refresher: syncs.
- tm-route-error: the spec's own `import('@tellma/core/errors')`, which it already loaded.
- tm-blob-fetcher: tellma-core-ui, which has no `tmSettleUntil`. Its `import('@angular/common/http')`
  is in the spec's static graph, so there is no first fetch.

Skipped by rule. Other items own these files, or pc/PC-118 or pc/PC-126 change them against
origin/platform-core:

- PC-F86: tm-session-flows.spec, tm-session-guard.spec, tm-session.spec.
- PC-F87: tm-tab-bus.spec.
- Excluded by name: tm-arrivals.spec.
- pc/PC-126: entity/src/tm-entity-field.spec.ts and entity/src/tm-entity-pickers.spec.ts (both
  renamed R100 into entity-editing), entity/src/tm-value.spec.ts, errors/src/tm-error-state.spec.ts,
  src/routes/tm-route-host.spec.ts, and the other spec files on its list.
  - Three of these hold `vi.waitFor` across a chunk load: tm-entity-field (display names),
    tm-entity-pickers (display names, and the search's read) and tm-value.
  - tm-entity-pickers "names a chosen id once its row is read" failed under the leaky harness in my
    first full run, from its 1 s `vi.waitFor`. They need converting after PC-126 lands.
- pc/PC-118: its spec files (the shell's, tm-route-host, tm-menu-rich, tm-counter, tm-initials,
  docs-examples) hold no `vi.waitFor`, and I left them untouched.

Turn counting past a lazy import, beyond the zone-check spec: I checked the fixed-turn helpers
(bootstrap-tellma, tm-locale-write, tm-leadership and others) against the delayed sites, and
none counts turns across one before a positive assertion. Both full runs under the postTask
harness, where every delayed call waits 300 ms, passed 2589/2589, which bears this out.

A remaining weak spot, not a flake: tm-display-names keeps three `setTimeout(50)` waits before
negative assertions ("never asked again"). Under the delay, those pass before the read could
have gone out. A negative claim has no state to wait on, and the later positive waits in two of
the three tests catch a stray query anyway.

### Runs

Each delayed run was reverted after use: the remover took out 35 sites each time, and git status
then showed only the four specs. Logs are in the scratchpad as PC-F97-*.log.

- No delay: zone-check alone 28/28. The four specs 232/232 after the final change.
- postTask harness (PC-F86's):
  - Zone-check before the change: 26/28, the pair.
  - After, first version: zone-check 28/28 four times, and the four specs 232/232 once.
  - Final version: the four specs 232/232 three times.
  - Full core twice: 2589/2589 and 2589/2589 (the second on the final spec).
- Leaky MessageChannel harness (the current gate):
  - The four specs: 3/3 on the final spec. On the first version, 2/3: the idle-callback failure
    above, fixed.
  - Full core, first version: 2588/2589. The one failure is tm-entity-pickers "names a chosen id…"
    (3,054 ms), a 1 s `vi.waitFor` in a PC-126-renamed file.
  - Full core, final version: 2588/2589. The one failure is tm-route-error "gets the update on
    Reload…" (10,619 ms), "did not hold within 9245 ms". The loop checks its deadline before each
    step, so one step stalled about 5 s. That is the harness's starvation (D-294), and it never
    happened in the two leak-free full runs.
  - The zone-check pair passed in both full runs.
- Orchestrator evidence (D-302): TmErrorHandler "marks an unhandled rejection global" failed in a
  full core run under the leaky harness ("did not hold within 5573 ms: the pipeline took a
  call"). It passes alone and never failed in ten leak-free runs. It is the same starvation class,
  and the spec is left alone. It passed in all four of my full runs.
- Gates: eslint on the four files, `tsc -p tellma-core/tsconfig.spec.json` (only the existing 13
  TS6059 rootDir notices), lint:test, `dotnet build Tellma.slnx` and `dotnet format
  --verify-no-changes`. No .NET change.

### Deviations

None from a spec. Test-only changes: the spec starts the zone check's triggers itself
(`ɵstart()`) in the tests that wait for the startup check, and the zone-check setUp gains an
`offline` option.

### Open risks and what to examine hardest

- `startup(rig)` relies on the first `TmZoneCheck.check` call being the startup's. That holds
  because every test calls it before any explicit check. Check that no test calls `startup` after
  its own check.
- The `TmZoneCheck.prototype` spy and the `TmToasts.prototype` spy: vitest wraps the prototype
  method, the instance's private fields still resolve, and `vi.restoreAllMocks()` in afterEach
  undoes both.
- The rewritten second test claims "the next check tries again" through the startup check plus
  one explicit check, where the old test made two explicit checks. Judge whether that keeps the
  claim.
- The remaining chunk-crossing `vi.waitFor`s are in PC-126's renamed files (above). They stay
  exposed to the leaky harness until converted.

## Review 1

Scope: commit 04114076 only. It is test-only: four core specs and this file. No spec section is
in scope. Spec 0024 §7.8 ("Detecting a change") is the behaviour the zone-check spec must still
prove.

### Findings

**R1-1 (minor): "records nothing when the dialog closes without an answer…" still races the
startup check, and it hangs if the startup check comes first.**
`client/projects/core/tellma-core/src/l10n/tm-zone-check.spec.ts:662-673`.

- The scenario. The test calls `rig.zone.check()` (B) right after `setUp`, without waiting for the
  app's own startup check (A), which the idle callback starts. It does not change anything the
  check reads, so the fix's rule left it alone. But both checks prompt here (`seen` is
  Asia/Riyadh, the device is Europe/London, the option is off), and `check()` runs them one after
  the other on `#tail`.
  - **A first** (its `check()` called before B's): B queues behind A. The prompt the test sees is
    A's, and `closeAll()` ends A with nothing recorded. B then runs, finds the same change and
    opens a second prompt that nothing answers. `await done` (line 668) never settles, and the
    test fails at the 5 s test timeout, not with a named wait.
  - **A last**, the usual order (D-304's CI failure shows the startup check landing after setUp
    and five tasks): B prompts, `closeAll()` ends it, and A then prompts. The last claim, "the next
    check asks again" (lines 671-672), is then met by A's prompt before the test's own check has
    run. The claim still holds, but by the startup check rather than the check the test makes.
- How reachable is "A first"? The idle callback is queued at the first render, inside
  `tmNavigate`. The entry trigger calls `check()` on the tick after NavigationEnd, which can fall
  inside `RouterTestingHarness.navigateByUrl`. That needs an idle period during the navigation:
  - In the file's first tests, the shell chunk's delayed load gives one.
  - Later in the file, `tm-route-hosts.ts` keeps the loaded component, so navigation is fast.
  I did not see it fail: 30 runs of the file (10 with no delay, 10 under postTask, 10 under the
  leaky harness) and two full core runs all passed. So this is latent. But it is the same race
  class as the pair, an explicit check racing an unawaited startup check, and D-304 shows that
  race is real in CI with no harness at all.
- Suggested fix: make the startup check this test's first check, so the order is fixed and the
  last claim is the test's own:
  ```ts
  const rig = await setUp();
  rig.zone.ɵstart();
  await tmSettleUntil(() => rig.checks.mock.calls.length > 0, 'the startup check started');
  const done = rig.checks.mock.results[0]!.value;
  await tmSettleUntil(() => prompt() !== null);
  TestBed.inject(Dialog).closeAll();
  await done;
  // …the same assertions, then the explicit check that asks again.
  ```
  An alternative is to assert, after `await done`, that only one check has run
  (`rig.checks.mock.calls.length === 1`), so the last prompt is known to come from the explicit
  check.

No blocking or major findings.

### What I verified

**(a) Both causes, and D-304's CI signature.**

- Both causes:
  - The implementer's before-log (scratchpad `PC-F97-zc-posttask-before.log`) shows both
    signatures under postTask: `expected [ { timeZone: 'Europe/London' } ] to deeply equal []`,
    and `expected [ … (3) ] to have a length of 2 but got 3`.
  - The code agrees. `#ready()` reads `seen` only after `await this.#entities.load()`, whose
    `import('./tm-entity-model')` is a delayed site. And a `check()` made while the startup
    check runs queues on `#tail` rather than sharing it.
- The fix. `startup(rig)` awaits the promise of the first recorded `TmZoneCheck.check` call, which
  is the entry trigger's. That trigger starts with `ɵstart` and runs `check()` only once
  `ɵpartition` and the launch documents are present. So the check has read the device document
  before the spec's `setSeen`.
  - Nothing else then reads that change. A second `ɵstart` (the app's idle callback) is a no-op
    through `#started`, and the entry effect re-runs only for a new partition key.
- So D-304's CI failure has the same signature as the entry test's harness failure, and both come
  from one mechanism: the startup check reads after the spec's change. That mechanism does not
  depend on the harness, and the fix removes it.
- Each test that calls `startup()` calls it before any explicit check, so the first recorded call
  is always the startup's.
- The 500 test keeps its claim. The startup check makes write 1 (rejected, reported once, nothing
  recorded) and an explicit check makes write 2. "The next check tries again" is still proven.

**(b) Calling ɵstart in the tests.**

- `startup()` calls `TmDeviceZone.ɵstart()`. That is the method the app's idle callback calls
  (`tm-app-init.ts:217`), and it is idempotent. So the tests that use it run the same trigger
  code (the entry effect and the visibility listener) that the product registers.
- The one product step they skip is tm-app-init's `requestIdleCallback(load)` → `ɵstart()`. The
  first test, "records a first run's zone at idle after the first render", covers that step:
  - nothing in the spec calls `ɵstart` or `check` there;
  - its `first-run` record can only come from the app's idle start running the entry trigger's
    "check now".
  So spec 0024 §7.8's three triggers stay covered: idle after the first render (test 1), tenant
  entry and visibility (the trigger tests). No coverage of the product's trigger is lost.
- D-304's question: test 1 cannot fail with the CI signature. It changes nothing after `setUp`,
  and it asserts the record that the startup check itself makes.
  - Its only exposure is its 4 s deadline, if the idle callback is held back that long. The
    implementer saw that once, for the idle-waiting variant, under the leaky harness, whose
    MessageChannel spin keeps the main thread busy.
  - It passed in all 30 file runs and both full runs here, under all three conditions. It is the
    price of covering the real idle path, and it ends when the postTask harness replaces the
    leaky one.

**(c) The sweep.**

- The 13 converted waits:
  - tm-http-api: 6 waits, through `flowsRan()` over `flushUntil` under fake timers.
  - tm-display-names: 4 waits.
  - tm-session-tabs: 3 waits.
  Each is a named `tmSettleUntil` or `flushUntil` condition, followed by the exact assertion the
  old `vi.waitFor` made. None weakens a test:
  - the conditions are `length > 0` or `!== null`, so a wrong value still fails the assertion
    that follows;
  - the session-tabs waits move from 3 s to the shared 4 s deadline.
- The 20 files that still hold `vi.waitFor` match the implementer's list. I read the ones outside
  the skip list against the 35 delayed sites: tm-updates, tm-error-presenter, tm-cache-refresher,
  tm-create-hub, provide-tellma, tm-route-error, tm-localization, tm-locks, tm-client-store,
  tm-fake-locks, tm-grid-header-nav and tm-blob-fetcher.
  - None crosses a delayed `import()`. updates/src, errors/src and realtime/src have no
    `import()` at all.
  - tm-route-error's waits follow the spec's own loader.
  - tm-localization's wait is the store's boot.
  - provide-tellma's loaders are plain promises.
- The skip list is right. `git diff --name-only origin/platform-core...origin/pc/PC-126` holds
  entity/src/tm-value.spec.ts, errors/src/tm-error-state.spec.ts and the two specs renamed into
  entity-editing (tm-entity-field, tm-entity-pickers). pc/PC-118's spec list holds no
  `vi.waitFor`. The session specs and tm-tab-bus belong to PC-F86 and PC-F87. D-303 sends the
  three chunk-crossing PC-126 files to PC-F99.
- Merge checks: `git merge-tree` of 04114076 with origin/platform-core and with origin/pc/PC-F86
  (368e684e) is clean.
- Lint: eslint on the four files is clean.
- The three `setTimeout(50)` negative waits in tm-display-names are unchanged, and the
  implementer noted them. Under the delay they pass before a stray read could go out, but they
  are not a regression from this change.

**(d) Runs.** Each harness was applied with its own inject script and reverted with its remove
script: 35 sites in 25 files each time. `git status` was clean after each revert. I also removed
the one failure screenshot my leaky run wrote, by name. Logs are in the scratchpad as
`r1-F97-*.log` and `r1-F97-*-summary.txt`.

| Condition | tm-zone-check.spec ×10 | Full core |
|---|---|---|
| No delay | 10/10 at 28/28 | not run |
| PC-F86's postTask harness | 10/10 at 28/28 | 2589/2589 |
| Current leaky harness | 10/10 at 28/28 | 2588/2589 |

- The one leaky failure was tm-session-flows.spec "waits for a running save, says so, and signs
  out with no question once it ends": `expected 'Signing out…' to be 'Waiting for a save to
  finish…'` at line 374, followed by an NG0205 unhandled rejection from the flows that outlived
  the test.
  - Root cause: line 374 is a 1 s `vi.waitFor` across the session flows' chunk (PC-F86 R1-1),
    which starved under the leaky harness. pc-f2 branched at 1e7c28cc, before PC-F86.
  - On origin/pc/PC-F86 that wait is `tmSettleUntil(…, 'the overlay says it waits for a save')`,
    and the file has no `vi.waitFor` left. So it is PC-F86's, already fixed, and not this change.
- The zone-check pair passed in every run.

### Responses

- **R1-1: fixed in fix 1, as ruled (D-309).** "records nothing when the dialog closes without an
  answer…" now makes the startup check its first check:
  - it calls `rig.zone.ɵstart()`, waits for the first recorded check ("the startup check started")
    and takes `checks.mock.results[0].value` as `done`;
  - it waits for that check's prompt, closes it with `closeAll()`, and awaits `done`;
  - it asserts `toHaveBeenCalledOnce()`, so no other check ran, plus the existing "nothing
    recorded" assertions;
  - it waits for the dialog to close, makes its own `rig.zone.check()`, and waits for "the next
    check asks again", asserting that two checks have now run.
  "records a first run's zone at idle" stays the idle-path test (spec 0024 §7.8) and is unchanged.
- Gate runs of tm-zone-check.spec.ts after the fix. Each harness was applied with its own inject
  script and reverted with its remove script, 35 sites each time.

  | Condition | Runs at 28/28 |
  |---|---|
  | No delay | 10/10 |
  | PC-F86's postTask harness | 10/10 |
  | Current leaky harness, first ten | 8/10 |
  | Current leaky harness, ten more | 10/10 |

  - The two leaky failures were late prompt-section tests, each timing out its named wait:
    - "shows a zone outside the server's set inline" (unchanged by this fix): `() => prompt() !==
      null` did not hold within 4,061 ms;
    - the R1-1 test: "the next check asks again" did not hold within 5,666 ms.
  - The second message proves a single step of the wait loop took at least 1.67 s, because the
    loop checks its deadline before each step. That is the leaky harness's starvation (D-294),
    which grows as a file's leaked channels pile up. The first message cannot tell a stall from
    an absent prompt.
  - To tell them apart, I ran ten more leaky runs with a temporary probe in `tmWaitUntil` that
    puts the step count and the longest step into the deadline message. All ten passed, so the
    probe never fired. The probe is reverted.
  - Neither test failed in 20 leak-free runs (here and in R1's 20), in 10 more leaky runs, or in
    R1's 10 leaky runs. I find no spec mechanism: each test's checks run in order on `#tail`, and
    the R1-1 test now fixes which check asks.
  - My reading: these are the starvation class D-294 and D-302 assign to the leaky harness, made
    likelier here by load from other slots on the machine. I could not confirm the load, because
    process listings are not allowed. They go away once the postTask harness becomes the gate.
- Gates: eslint on the spec, `pnpm run typecheck`, `tsc -p tellma-core/tsconfig.spec.json`
  (only the 13 existing TS6059 notices), lint:test, and `dotnet format --verify-no-changes`.
  No .NET change. The failure screenshots my runs wrote are deleted by name.

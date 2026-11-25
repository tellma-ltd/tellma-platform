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

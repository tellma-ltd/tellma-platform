# PC-F14

## Review 1

Commit d24366d. The diagnosis holds against the installed `@angular/aria` 22.0.3
(`fesm2022/private.mjs`: the open trigger's key bindings only call `keyboardEventRelay.set(e)`,
`keyboardEventRelayEffect` dispatches whatever the signal holds after the next render, and nothing
clears or queues it). It also reproduces: in the showcase, with `relayPromptly` replaced by a no-op
on the component instance, Home and Space dispatched in one task (or with microtask yields between
them, as the new unit tests do) leave the multi-select's value empty, exactly as in the CI run;
with the fix the value is "Service | Sale". The same disabled-fix probe moves one option instead of
two for ArrowDown, ArrowDown on `tm-select`, so both new unit tests would fail without the fix.
They would also fail on a double dispatch or if the listener order the fix relies on changed.
The fix was disabled at run time only; the source was not reverted, so the unit specs themselves
were not run against the old code.

Runs, all on d24366d against the dev-server showcase: the select unit specs (5 files, 59 tests)
passed twice, the second time under 14 busy loops. `select.spec.ts` on Chromium passed 32/32 with
4 workers; two earlier runs at 6 workers failed 5 and 6 axe tests on `page.goto` and first-click
timeouts while the machine was busy, none in a keyboard test. The four keyboard tests (`:121`,
`:152`, `:339`, `:379`) passed 80/80 at `--repeat-each 20` under 14 busy loops, `:379` 20/20.
`grid-editing`, `grid-a11y-editable`, `modal` and `glyphs` passed 56/56. The production build CI
serves was not run.

### Findings

**R1-1 (major) — after a relayed Enter or Space commits, later keys in the same render window go
to the closed panel, and a character is dispatched twice.**
`client/projects/core/tellma-core-ui/select/tm-select-base.ts:476-485` (the guard at `:480`),
with `tm-select.ts:292`.

`TmSelect.commitFromListbox` closes with `this.expanded.set(false)` alone. The `[(expanded)]`
binding pushes that to aria's own `expanded` model only on the next change detection. Before this
commit the commit ran from aria's after-render effect, and the render right behind it pushed the
close before another key could arrive. Now the commit runs inside the keydown, so until the next
render aria's `keydown` bindings and `pattern.isExpanded()` still say "open" while
`this.expanded()` says "closed". Measured in the showcase (`select-country`, panel open, nothing
selected, Saudi Arabia active; keys dispatched on the trigger in one task):

- `ArrowDown, Enter, ArrowDown, Enter`: `selectionChange` emits twice (United Arab Emirates, then
  Ethiopia) and the value ends as Ethiopia. The second ArrowDown and Enter were relayed into the
  listbox of the panel the first Enter had closed. Key by key the result is United Arab Emirates
  with one emission (ArrowDown on a closed select only opens it).
- `ArrowDown, Enter, e` (the same with Space for Enter): the listbox receives `e` twice. Once from
  `relayPromptly`, because aria still stored it as an open-panel key; once from the closed-trigger
  seed, because `this.expanded()` is already false at `:444`, so `:455-456` take it as a character
  typed on a closed trigger. The typeahead query becomes "ee" and the panel reopens on United Arab
  Emirates instead of Ethiopia.
- `ArrowDown, Enter, ArrowDown`: the reopen is swallowed; the arrow moves the highlight of the
  closed panel.

Without the fix these bursts lose every key but the last, so no working case regresses. The second
commit through a closed panel and the double dispatch are new, though, and they belong to the
class this fix exists to close. `tm-multi-select` is not affected: its activations never close.

Suggested fix: when the select itself opens or closes the panel, write aria's model together with
its own, as `TmEntityPicker.setExpanded` does (`entity-picker/tm-entity-picker.ts:1558`, whose
comment describes this hazard) and as the readonly effect here already does (`:331-336`). One base
`setExpanded(open)` used by `commitFromListbox`, `commit()`, `cancel()`, `open()`, `seed()` and the
closed-trigger branch of `onTriggerKeydown`. I tried this in the same probe by patching the
instance's `expanded.set` to write both: each burst then acts once per key (one emission, United
Arab Emirates; `e` once, Ethiopia active; ArrowDown reopens), and Home, Space on the multi-select
is unchanged. That was a runtime patch, not a source change, so the unit suite has not run against
it. Writing only the close is not enough: the seed's reopen then leaves aria closed under an open
overlay. Pin it with unit tests in the style of the two new ones: Enter then a character reaches
the listbox once; Enter, ArrowDown, Enter emits `selectionChange` once. `onTriggerKeydown`'s TSDoc
still describes only the closed-trigger typeahead and should say it relays first.

**R1-2 (minor) — keys pressed between the opening key and the panel's render are still lost.**
`tm-select-base.ts:478` (`listbox === undefined`, "left to aria").

Measured on a closed, focused trigger, one task, identical with and without the fix:
`ArrowDown, Home, Space` on the multi-select opens the panel and nothing else (the listbox receives
no key); `ArrowDown, ArrowDown` on `tm-select` only opens; `u, n` loses the `n`. aria's relay effect
runs in the first after-render pass, before the popup has registered its listbox, finds no target
and never re-runs; a character after the opening character is not even stored, because aria is
still collapsed and `:444` returns. So "keys pressed within one render all reach the listbox" holds
for a rendered panel only, and "left to aria" means dropped. The starved runner that lost Home can
lose these too. No current e2e test is exposed: every keyboard test in `select.spec.ts` and the
grid enum-cell tests wait for the panel, the rows or `appStable` before the next key.

Suggested fix: hold such keys in the base while `expanded()` is true and no row has rendered, and
replay them after the seed in the after-render effect (`:389-395`), clearing aria's signal. If that
is out of scope for a stop-the-line fix, say in the TSDoc that these keys are dropped and record
the limit, so new e2e tests keep waiting for the panel before they press keys.

**R1-3 (minor) — PLAN §7.1 item 4: the commit message's gate list is short.**
It lists lint, typecheck, build, test, api:check, size:check and `select.spec.ts`. It omits
lint:test, tokens:check and docs:build, and the Chromium PR-tier e2e beyond `select.spec.ts`.
`TmSelectBase` also drives the grid's enum cell editor (`grid-editing.spec.ts` "enum cells
(built-in tm-select editor)"), `grid-a11y-editable.spec.ts`, `modal.spec.ts` and
`glyphs.spec.ts`, where the commit now happens inside the keydown instead of after the render.
Those four passed here (56/56, Chromium, dev server); the rest of the Chromium PR tier and the
three omitted gates were not run in this review. Run them and list them in the commit message.

### Responses

- R1-1: fixed in the review 1 fixes commit. `TmSelectBase.setExpanded(open)` writes aria's
  combobox model together with the control's own, as `TmEntityPicker.setExpanded` does. Every open
  and close goes through it: the readonly fold-back, closed-trigger typeahead, `open()`, and
  tm-select's `commitFromListbox`, `commit()`, `cancel()` and `seed()`. Also, `onTriggerKeydown`
  now returns once `relayPromptly` has taken a key, so a key can never be both relayed and seeded.
  Its TSDoc now says it relays first. New unit tests in tm-select.spec.ts, "keys pressed within one
  render", run each burst against the same keys pressed one at a time with every render settled,
  and expect identical value, emissions, open state and highlight:
  - `ArrowDown, Enter, e`: one emission (United Arab Emirates), reopened on Ethiopia.
  - `ArrowDown, Enter, ArrowDown, Enter`: the arrow reopens, and Enter recommits once, exactly as
    stepwise. This pins the stepwise behavior rather than the "once" in the finding: a reopened
    select's Enter is a same-value reselection, which emits by design.
  - `ArrowDown, Enter, ArrowDown`: reopened, one emission.
  With the aria write removed, all three fail (and so do the two readonly tests).
- R1-2: fixed in the review 1 fixes commit, by queueing. `relayPromptly` now takes every key aria
  stores for the open panel. It clears aria's signal and dispatches the key at once, unless the
  rows have not rendered yet or earlier keys are still held. In those cases it holds the key in
  order. The after-render effect replays the held keys behind the typeahead seed, from the same
  anchored default state the seed uses, inside `untracked`. Keys still held when the panel closes
  are dropped together with the seed. A `pendingInput` signal makes that effect re-run whenever a
  seed or a held key starts waiting: a close and a reopen within one render leave the rows in
  place, and a waiting seed would otherwise sit until the next unrelated render. Closed-trigger
  characters also reach aria while it is open now, because R1-1 writes its model synchronously.
  New unit tests:
  - tm-select: `ArrowDown, ArrowDown` from a closed trigger, and `u, ArrowDown`, each matching
    stepwise.
  - tm-multi-select: `ArrowDown, Home, Space` from a closed trigger gives Service | Sale.
  With the queue replaced by the old "left to aria", both fail.
- R1-3: fixed in the review 1 fixes commit. The commit message lists every client gate: lint,
  typecheck, lint:test, tokens:check, build, test, api:check, docs:build and size:check. It also
  lists the full Chromium and touch PR-tier e2e run, which covers select, grid-editing,
  grid-a11y-editable, modal, glyphs and every other spec. The .NET items of PLAN §7.1 were re-run
  at the merge with platform-core, since PC-F14 changes no .NET project.

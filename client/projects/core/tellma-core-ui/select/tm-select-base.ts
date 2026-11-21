// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterRenderEffect,
  booleanAttribute,
  computed,
  contentChildren,
  Directive,
  effect,
  ElementRef,
  inject,
  input,
  type ModelSignal,
  output,
  type Signal,
  signal,
  untracked,
  viewChild,
  viewChildren,
} from '@angular/core';
import { Combobox } from '@angular/aria/combobox';
import { Listbox, Option } from '@angular/aria/listbox';
import { CdkConnectedOverlay } from '@angular/cdk/overlay';
import type { ConnectedPosition } from '@angular/cdk/overlay';
import type { ValidationError } from '@angular/forms/signals';
import { Check, ChevronDown } from 'lucide';

import type { TmFieldError, TmFormFieldControl } from '@tellma/core-ui/contracts';
import {
  TM_CELL_EDITOR_HOST,
  TM_ERROR_DISPLAY,
  TM_FORM_FIELD_DEFAULTS,
  TM_UI_TRANSLATE,
  tmResolveFieldErrors,
  ɵtmControlSize,
  type TmControlSize,
} from '@tellma/core-ui';
import { TmFormField } from '@tellma/core-ui/form-field';
import { tmCreateAnchoredOverlay } from '@tellma/core-ui/overlay';

import { TmOption } from './tm-option';
import type { TmOptionGroup } from './tm-option-group';

let nextUniqueId = 0;

/**
 * One run of the panel's rows: the options under one group heading, or a
 * run of ungrouped options between groups.
 */
export interface TmSelectEntry<T> {
  /**
   * A track key that survives option turnover: the group itself, or the
   * ungrouped run's place among the groups, so inserting an option never
   * re-creates the rows around it.
   */
  readonly key: TmOptionGroup | string;
  /** The heading over the run, or `null` for ungrouped options. */
  readonly group: TmOptionGroup | null;
  /** The run's options, in declaration order. */
  readonly options: readonly TmOption<T>[];
}

/**
 * The shared shell of `tm-select` and `tm-multi-select`: the form-control
 * plumbing both carry, the option and group definitions, the anchored
 * panel, the value bridge into aria's listbox, closed-trigger typeahead and
 * the label-less typeahead fallback. Selector-less by design — each select
 * extends it with its own model, template and commit path. It is exported
 * because its members are the two selects' public API; it is not meant to
 * be extended outside this library.
 *
 * @sealed
 *
 * Value integrity, shared by both: the subclass's `value` model is the
 * single source of truth. It is mirrored into aria's listbox as
 * `valueKey`-mapped stable keys and RE-APPLIED whenever the option set
 * changes, defeating aria's unmatched-value auto-prune; the subclass
 * commits on activation events only (click, Enter, Space), never on the
 * listbox's own value change, so a prune can never wipe the form value.
 */
@Directive()
export abstract class TmSelectBase<T, V> implements TmFormFieldControl {
  /** The library string resolver (placeholders, error messages). */
  protected readonly translate = inject(TM_UI_TRANSLATE);
  private readonly errorDisplay = inject(TM_ERROR_DISPLAY);
  /** The workspace-wide control defaults. */
  protected readonly defaults = inject(TM_FORM_FIELD_DEFAULTS);
  /** The enclosing grid cell's registration sink, if any — absent standalone. */
  protected readonly cellHost = inject(TM_CELL_EDITOR_HOST, { optional: true });
  /** The enclosing form field, if any — decides whether the size ladder is inherited. */
  private readonly field = inject(TmFormField, { optional: true });

  // ---- FormValueControl<V> + optional state inputs ----
  /** The selected domain value — THE source of truth. */
  abstract readonly value: ModelSignal<V>;
  /** Non-form usage only — the bound field is authoritative when bound via [formField]. */
  readonly disabled = input(false, { transform: booleanAttribute });
  /** Readonly state for non-form usage — the bound field is authoritative when bound via [formField]. */
  readonly readonly = input(false, { transform: booleanAttribute });
  /** Required state for non-form usage — the bound field is authoritative when bound via [formField]. */
  readonly required = input(false, { transform: booleanAttribute });
  /** Validity state for non-form usage — the bound field is authoritative when bound via [formField]. */
  readonly invalid = input(false, { transform: booleanAttribute });
  /** Touched state for non-form usage — the bound field is authoritative when bound via [formField]. */
  readonly touched = input(false, { transform: booleanAttribute });
  /** Dirty state for non-form usage — the bound field is authoritative when bound via [formField]. */
  readonly dirty = input(false, { transform: booleanAttribute });
  /** Async-validation-pending state — the bound field is authoritative when bound via [formField]. */
  readonly pending = input(false, { transform: booleanAttribute });
  /** The raw framework errors, bound by [formField] and localized into `localizedErrors`. */
  readonly errors = input<readonly ValidationError.WithOptionalFieldTree[]>([]);
  /** Emits when the trigger blurs — touch reporting for the bound field. */
  readonly touch = output<void>();

  // ---- Own API ----
  /**
   * Maps a domain value to a STABLE primitive key (aria selects with `===`).
   * Unneeded for primitive values.
   */
  readonly valueKey = input<((value: T) => string | number) | undefined>(undefined);
  /**
   * Resolves a value's label without a materialized option — required in
   * practice for prepopulated/async lists.
   */
  readonly displayWith = input<((value: T) => string) | undefined>(undefined);
  /** Trigger text while nothing is selected; defaults to the localized built-in placeholder. */
  readonly placeholder = input<string | undefined>(undefined);
  /** Accessible name for a select used WITHOUT tm-form-field. */
  readonly ariaLabel = input<string | null>(null, { alias: 'aria-label' });
  /**
   * Author-supplied describedby ids (space-separated), relocated to the
   * TRIGGER and preserved — the enclosing field's hint/error ids are merged
   * AFTER them, never over them.
   */
  readonly ariaDescribedby = input<string | null>(null, { alias: 'aria-describedby' });
  /**
   * Size step. Unset, a select inside a `tm-form-field` follows the FIELD's
   * size (the field publishes its ladder as inheritable locals), and a bare
   * select follows the workspace-wide control default — the `lg` touch
   * height below the `sm` breakpoint.
   */
  readonly size = input<TmControlSize | undefined>(undefined);
  /** Emits when the options panel opens. */
  readonly opened = output<void>();
  /** Emits when the options panel closes. */
  readonly closed = output<void>();

  /** A bare select's size: `size`, else the responsive workspace default. */
  private readonly standaloneSize = ɵtmControlSize(this.size);

  /**
   * The size step the host classes apply. Deliberately `undefined` for an
   * unsized select inside a field: a size class would re-declare the ladder
   * locals on this host, and an element's own custom property always beats
   * an inherited one — exactly what must NOT happen when the field's size
   * is meant to reach the trigger and its dropdown rows. A select editing a
   * grid cell keeps the workspace default at every width: grids are not
   * subject to the touch size, and its rows must match the grid's.
   */
  protected readonly effectiveSize = computed(() =>
    this.field !== null
      ? this.size()
      : this.cellHost !== null
        ? (this.size() ?? this.defaults.size)
        : this.standaloneSize(),
  );

  /** Stable generated id of the trigger — the target for the field's aria wiring. */
  readonly controlId = signal(`tm-select-${nextUniqueId++}`).asReadonly();

  /** Whether the options panel is open. */
  protected readonly expanded = signal(false);
  /** aria's listbox value — an ARRAY of keys; never the source of truth. */
  protected readonly listboxValue = signal<unknown[]>([]);
  /**
   * Every `tm-option` in declaration order, grouped or not — the flat
   * order navigation, typeahead and the value follow.
   */
  protected readonly options = contentChildren(TmOption<T>, { descendants: true });

  /**
   * The panel's rows as runs: each group's options under its heading, and
   * the ungrouped options between groups. A select without groups is one
   * ungrouped run, drawn exactly as a flat list.
   */
  protected readonly entries = computed<readonly TmSelectEntry<T>[]>(() => {
    const entries: {
      key: TmOptionGroup | string;
      group: TmOptionGroup | null;
      options: TmOption<T>[];
    }[] = [];
    let groupsSeen = 0;
    for (const option of this.options()) {
      const last = entries.at(-1);
      if (last !== undefined && last.group === option.group) {
        last.options.push(option);
        continue;
      }
      if (option.group !== null) {
        groupsSeen++;
      }
      entries.push({
        key: option.group ?? `ungrouped-${groupsSeen}`,
        group: option.group,
        options: [option],
      });
    }
    return entries;
  });

  /** The trigger caret's icon data. */
  protected readonly caretIcon = ChevronDown;
  /** The check's icon data: an option's selected mark, and a checkbox row's tick. */
  protected readonly checkIcon = Check;

  /** Overlay positions: below the trigger, flipping above when space runs out. */
  protected readonly positions: ConnectedPosition[] = [
    { originX: 'start', originY: 'bottom', overlayX: 'start', overlayY: 'top' },
    { originX: 'start', originY: 'top', overlayX: 'start', overlayY: 'bottom' },
  ];

  private readonly combobox = viewChild(Combobox);
  private readonly overlay = viewChild(CdkConnectedOverlay);
  /** aria's listbox, once the panel has rendered. */
  protected readonly listbox = viewChild(Listbox);
  private readonly optionRows = viewChildren('optionRow', { read: ElementRef });
  /** aria's rendered `[ngOption]` directives — activation guards read active/disabled. */
  protected readonly ariaOptions = viewChildren(Option);

  /**
   * The shared anchored-overlay wiring (config + macrotask re-measure —
   * aria's DeferredContent inserts the panel one render pass after the CDK
   * attaches and measures, so flip-up would otherwise measure a zero-height
   * panel and never flip).
   */
  protected readonly anchored = tmCreateAnchoredOverlay({
    overlay: () => this.overlay(),
    origin: () => this.combobox()?.element ?? null,
    positions: this.positions,
    matchWidth: true,
    remeasure: 'macrotask',
  });

  /**
   * A printable character typed on the CLOSED trigger, held until the panel
   * renders so it can seed aria's typeahead (the panel content inserts one
   * render pass after the overlay attaches).
   */
  protected pendingTypeaheadKey: string | null = null;

  /**
   * Keys pressed on the open trigger before the listbox could take them
   * (between the opening key and the rows' first render), in order — replayed
   * after the typeahead seed once the rows land, and dropped on close.
   */
  private pendingRelays: KeyboardEvent[] = [];

  /**
   * Bumped whenever a seed or a held key starts waiting, so the after-render
   * effect that consumes them re-runs even when nothing it renders changed
   * (a close and a reopen within one render leave the rows in place).
   */
  private readonly pendingInput = signal(0);

  /** Holds a closed-trigger character until the rows render, to seed the typeahead with it. */
  protected holdTypeaheadKey(key: string): void {
    this.pendingTypeaheadKey = key;
    this.pendingInput.update((n) => n + 1);
  }

  /**
   * Holds a whole text for the typeahead until the rows render: its first
   * character seeds it, and the rest follow in order as if typed, so the
   * active option is the one typing the text would have reached. Internal:
   * the grid's cell editor seeds a select with text typed before it mounted.
   */
  protected ɵholdTypeaheadText(text: string): void {
    const [first, ...rest] = Array.from(text);
    if (first === undefined) {
      return;
    }
    this.holdTypeaheadKey(first);
    this.pendingRelays.push(...rest.map((key) => new KeyboardEvent('keydown', { key, bubbles: true })));
  }

  // ---- TmFormFieldControl ----
  /** Renders its own trigger chrome; the field adds only label/hint/error. */
  readonly ownsChrome = true;
  private readonly fieldDescribedBy = signal<readonly string[]>([]);
  /** Every exposed describedby id: author-supplied first, then the field's hint/error ids. */
  readonly describedByIds: Signal<readonly string[]> = computed(() => [
    ...(this.ariaDescribedby()?.split(/\s+/).filter(Boolean) ?? []),
    ...this.fieldDescribedBy(),
  ]);
  private readonly labelIdFromField = signal<string | null>(null);
  /** Already-localized error messages resolved from `errors` — read by the enclosing field. */
  readonly localizedErrors: () => readonly TmFieldError[] = tmResolveFieldErrors(
    this.errors,
    this.translate,
  );

  /** The merged aria-describedby attribute value, or null when no ids apply. */
  protected readonly describedByAttr = computed(() => this.describedByIds().join(' ') || null);
  /** The field-provided label id, bound as aria-labelledby on the trigger. */
  protected readonly ariaLabelledBy = computed(() => this.labelIdFromField());

  /** Whether the enclosing field displays an error this control must mark (see `setFieldError`). */
  private readonly fieldError = signal(false);

  /**
   * Whether invalidity is surfaced (aria-invalid) — follows the
   * error-display policy, or the enclosing field's own displayed error: the
   * field's plain `error` input never reaches this control's bound state,
   * and the select owns the only chrome there is to mark.
   */
  protected readonly showsInvalid = computed(
    () =>
      this.fieldError() ||
      this.errorDisplay({
        invalid: this.invalid(),
        touched: this.touched(),
        dirty: this.dirty(),
        pending: this.pending(),
      }),
  );

  /** The localized built-in placeholder this select falls back to. */
  protected abstract readonly defaultPlaceholder: Signal<string>;

  /** The placeholder in effect: the `placeholder` input, or the localized default. */
  protected readonly effectivePlaceholder = computed(
    () => this.placeholder() ?? this.defaultPlaceholder(),
  );

  /**
   * The listbox keys the current `value` selects — what the value bridge
   * mirrors into aria's listbox.
   */
  protected abstract selectedKeys(): readonly unknown[];

  /** Wires the value bridge, the panel lifecycle outputs and the panel's after-render work. */
  constructor() {
    // The ONE-DIRECTIONAL value bridge: mirror the model into aria's
    // listbox (as stable keys), re-applied whenever the option set changes
    // — aria's afterRenderEffect prunes any selected key without a rendered
    // option, and by re-asserting here a prepopulated value SURVIVES async
    // option arrival. Reading listboxValue also re-asserts right after a
    // prune. Never treat aria's value as authoritative.
    effect(() => {
      const desired = this.selectedKeys();
      this.options(); // re-apply on option turnover
      const current = this.listboxValue();
      if (current.length !== desired.length || desired.some((key, i) => current[i] !== key)) {
        this.listboxValue.set([...desired]);
      }
    });

    // A readonly select shows its value and never opens: aria's own opens
    // (a trigger click, ArrowDown, Enter, Space) are folded back here,
    // before the panel renders, as `open()` and closed typing refuse to.
    // aria's own model is set too (see setExpanded): it holds the open it
    // applied, and an echo of `false` back through the binding would change
    // nothing it sees.
    effect(() => {
      if (this.readonly() && this.expanded()) {
        this.setExpanded(false);
      }
    });

    // Panel lifecycle outputs.
    let wasExpanded = false;
    effect(() => {
      const isExpanded = this.expanded();
      if (isExpanded !== wasExpanded) {
        wasExpanded = isExpanded;
        if (isExpanded) {
          this.opened.emit();
        } else {
          // A seed the panel never got to consume must not leak into the
          // NEXT open (e.g. Escape racing the panel render), and neither
          // must the keys held behind it.
          this.pendingTypeaheadKey = null;
          this.pendingRelays = [];
          this.closed.emit();
        }
      }
    });

    // Typeahead textContent fallback: after the rows render, feed each
    // label-less option its rendered text. The rows render in option order
    // (groups only wrap runs of them), so row i is option i.
    afterRenderEffect(() => {
      this.pendingInput(); // re-run when a seed or a held key starts waiting
      const rows = this.optionRows();
      const options = this.options();
      for (let i = 0; i < rows.length && i < options.length; i++) {
        if (untracked(() => options[i].label()) === undefined) {
          const text =
            (rows[i].nativeElement as HTMLElement)
              .querySelector('.tm-option__content')
              ?.textContent?.trim() ?? '';
          options[i].derivedText.set(text);
        }
      }
      // Closed-trigger typeahead, second half: the character that opened the
      // panel is forwarded once the listbox exists (the panel content
      // inserts a render pass after the overlay attaches — and after the
      // derivedText loop above, so label-less options are searchable too).
      // Everything happens HERE, before the highlight ever renders — any
      // deferral (a macrotask, even a scheduler race) lets a frame paint
      // with the DEFAULT highlight first, a visible jump to the match. The
      // options are already registered (DI, during change detection), but
      // the listbox initializes its active state only in ITS OWN
      // after-render callbacks, which run after this one — so the default
      // state is initialized imperatively to anchor the search, and the
      // dispatched keydown marks the listbox as interacted, which turns its
      // own later initialization into a no-op instead of a clobber
      // (version-locked internal seam, guarded by the tests).
      // Gated on RENDERED ROWS, not just the listbox: the panel mounts in
      // stages, and on the earliest run the listbox shell exists with zero
      // registered options — consuming the seed there loses it (an empty
      // listbox swallows the keydown entirely). This effect re-runs when
      // the rows land (optionRows() is tracked above).
      const listbox = this.listbox();
      if (listbox && this.pendingTypeaheadKey !== null && rows.length > 0) {
        const key = this.pendingTypeaheadKey;
        this.pendingTypeaheadKey = null;
        listbox._pattern.setDefaultState();
        listbox.element.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
      }
      // Keys pressed before the rows existed (see relayPromptly), replayed
      // in order behind the seed, from the same anchored default state —
      // the first replayed keydown, like the seed, turns the listbox's own
      // later initialization into a no-op. Untracked: the handlers the
      // replay runs read signals this effect must not re-run on.
      if (listbox && this.pendingRelays.length > 0 && rows.length > 0) {
        const keys = this.pendingRelays;
        this.pendingRelays = [];
        untracked(() => {
          if (!listbox._pattern.hasBeenInteracted()) {
            listbox._pattern.setDefaultState();
          }
          for (const key of keys) {
            listbox.element.dispatchEvent(new KeyboardEvent(key.type, key));
          }
        });
      }
      listbox?.scrollActiveItemIntoView();
    });
  }

  /** Receives whether the enclosing field displays an error (its plain `error` input included). */
  setFieldError(showsError: boolean): void {
    this.fieldError.set(showsError);
  }

  /** Domain value → the stable primitive key aria selects on. */
  keyOf(value: T): unknown {
    if (value === undefined || value === null) {
      return value;
    }
    const keyFn = this.valueKey();
    return keyFn ? keyFn(value) : value;
  }

  /** The option whose value has `value`'s key, if one is declared. */
  protected optionOf(value: T): TmOption<T> | undefined {
    const key = this.keyOf(value);
    return this.options().find((o) => this.keyOf(o.value()) === key);
  }

  /**
   * A value's label: `displayWith` first, else the matching option's label,
   * else the empty string.
   */
  protected labelOf(value: T): string {
    const displayWith = this.displayWith();
    if (displayWith) {
      return displayWith(value);
    }
    return this.optionOf(value)?.effectiveLabel() ?? '';
  }

  /**
   * The trigger's keydown, after aria's own handling. First, a key aria
   * forwards to the open panel is handed to the listbox at once (see
   * `relayPromptly`). Then closed-trigger typeahead (the APG select-only
   * combobox behavior): a printable character on the CLOSED trigger opens
   * the panel and seeds the typeahead with that character — the active
   * option moves to the first match, and committing stays an explicit
   * activation. (Deliberately NOT the native <select> behavior of changing
   * the value silently while closed: a keystroke must never mutate the
   * model.) aria's own collapsed handling covers only ArrowDown/Enter/Space,
   * all plain opens.
   */
  protected onTriggerKeydown(event: KeyboardEvent): void {
    if (this.relayPromptly(event)) {
      return;
    }
    if (
      this.expanded() ||
      this.disabled() ||
      this.readonly() ||
      event.key.length !== 1 ||
      event.key === ' ' || // aria's own binding: space opens, never searches
      event.ctrlKey ||
      event.metaKey ||
      event.altKey
    ) {
      return;
    }
    this.holdTypeaheadKey(event.key);
    this.setExpanded(true);
  }

  /**
   * Opens or closes the panel, writing aria's combobox model as well as this
   * control's. The two are joined by a two-way binding, but a binding
   * pushes only changes, once per render: a key handled before that render
   * would meet aria still open under a closed panel (and relay into it, or
   * be taken twice) or still closed under an open one (and be dropped).
   * Writing both keeps them in step whatever the timing.
   */
  protected setExpanded(open: boolean): void {
    this.expanded.set(open);
    untracked(() => this.combobox())?.expanded.set(open);
  }

  /**
   * Hands an open panel's key to the listbox before the next key can arrive,
   * and reports whether it took the key. aria's combobox relays the keys it
   * forwards (arrows, Home, End, Enter, Space, printables) by storing the
   * event in a signal that one of its after-render effects dispatches on the
   * listbox, so every key pressed within one render window but the last is
   * silently dropped — Home then Space toggled the default active option
   * instead of the first one. aria's host listener runs before the
   * template's, so the event is already stored here: the signal is cleared
   * so aria's effect has nothing left to relay (version-locked internal
   * seam, guarded by the tests), and the same clone aria would send is
   * dispatched now — or, while the rows have not rendered yet (the keys
   * right after the opening one) or earlier keys still wait, held for the
   * after-render effect to replay in order. Keys aria handles itself are
   * left to aria.
   */
  private relayPromptly(event: KeyboardEvent): boolean {
    const pattern = untracked(() => this.combobox())?._pattern;
    if (
      pattern === undefined ||
      untracked(() => pattern.keyboardEventRelay()) !== event ||
      !untracked(() => pattern.isExpanded())
    ) {
      return false;
    }
    pattern.keyboardEventRelay.set(undefined);
    const listbox = untracked(() => this.listbox());
    const ready =
      listbox !== undefined &&
      untracked(() => this.optionRows()).length > 0 &&
      this.pendingRelays.length === 0;
    if (ready) {
      listbox.element.dispatchEvent(new KeyboardEvent(event.type, event));
    } else {
      this.pendingRelays.push(event);
      this.pendingInput.update((n) => n + 1);
    }
    return true;
  }

  /**
   * Whether a typeahead query is in flight on the open listbox — aria then
   * routes Space into the search string rather than selecting. The same
   * predicate aria keys that behavior on (version-locked internal seam;
   * the unit tests guard it).
   */
  protected isTyping(): boolean {
    return this.listbox()?._pattern.listBehavior.isTyping() ?? false;
  }

  /** Whether the listbox's active option is a disabled one (still navigable: softDisabled). */
  protected activeOptionDisabled(): boolean {
    return (
      this.ariaOptions()
        .find((o) => o.active())
        ?.disabled() ?? false
    );
  }

  // ---- TmFormFieldControl plumbing ----
  /** Receives the field's hint/error ids and exposes them via aria-describedby. */
  setDescribedByIds(ids: readonly string[]): void {
    this.fieldDescribedBy.set(ids);
  }

  /** The <div> trigger is not labelable — the field hands us its label id. */
  setLabelId(id: string | null): void {
    this.labelIdFromField.set(id);
  }

  /** Focuses the trigger when the user clicks the field's container chrome. */
  onContainerClick(): void {
    this.focus();
  }

  /** Focuses the trigger; Signal Forms calls this when asked to focus the field. */
  focus(options?: FocusOptions): void {
    untracked(() => this.combobox())?.element.focus(options);
  }

  /**
   * Opens the options panel programmatically — also how a grid host
   * translates Enter / Alt+ArrowDown on a `tm-select` cell into the dropdown
   * opening. Focuses the trigger first: the combobox keeps its popup open
   * only while it owns focus. A no-op while disabled or readonly.
   */
  open(): void {
    if (!this.disabled() && !this.readonly()) {
      this.focus();
      this.setExpanded(true);
    }
  }
}

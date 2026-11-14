// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  booleanAttribute,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  input,
  model,
  output,
  signal,
  untracked,
  viewChild,
  type Signal,
} from '@angular/core';
import type { ValidationError } from '@angular/forms/signals';

import type { TmFieldError, TmFormFieldControl } from '@tellma/core-ui/contracts';
import {
  TM_ACTIVE_LOCALE,
  TM_ERROR_DISPLAY,
  TM_UI_TRANSLATE,
  type TmControlSize,
  ɵtmControlSize,
} from '@tellma/core-ui';
import { TmCounter } from '@tellma/core-ui/badge';
import { TmChip, TmChipSet } from '@tellma/core-ui/chip';
import { TM_FORM_FIELD_CONTROL, TmFormField } from '@tellma/core-ui/form-field';

import { TmEntityPicker } from './tm-entity-picker';
import type {
  TmEntityId,
  TmEntityMultiPick,
  TmEntityPicked,
  TmEntityPickerPage,
  TmEntitySearchFn,
} from './tm-entity-picker-types';

/**
 * Angular's development-mode flag (a production build replaces it with
 * `false`, which removes the development warnings and their text from the
 * bundle — `isDevMode()`, a call into @angular/core, cannot be folded away).
 * Read through `typeof`, so code running where nothing defines it never
 * throws.
 */
declare const ngDevMode: unknown;

let nextUniqueId = 0;

/**
 * A count the badge's hidden probe shows: above the counter's `max`, so the
 * probe always renders the widest form ("99+") and the badge never changes
 * width as the count does.
 */
const WIDEST_COUNT = 1000;

/**
 * Several foreign keys in one field: the chosen entities as removable chips,
 * in pick order, followed by an input that searches exactly as
 * `tm-entity-picker` does — it IS one, in a mode where a commit hands the
 * pick over instead of keeping it.
 *
 * A pick from the list or by Enter adds a chip, empties the input and keeps
 * the dropdown open for the next pick; picking a row that is already chosen
 * (its row shows the check) removes it again. Typed text that resolves on
 * departure or Tab adds its entity, but never removes one. The advanced
 * search page edits the whole selection at once, and Create… adds the
 * entity it created. `maxItems` caps the selection: further picks are
 * refused with a message.
 *
 * The field stays one line at the size ladder's fixed height however many
 * entities are chosen: the chips scroll sideways (wheel, trackpad and the
 * arrow keys), and a fixed-width badge after them shows the total.
 *
 * Chip labels resolve through `displayWith` (reactive), then the label the
 * pick carried, then `String(id)` with a development warning.
 *
 * @tmGroup form-control
 * @tmA11yNotes The input is the combobox, with an `aria-multiselectable`
 *   listbox whose chosen rows are `aria-selected`. The chips are a list
 *   with one tab stop before the input; each remove button is named
 *   "Remove {label}". ArrowLeft (ArrowRight in RTL) at the start of the
 *   input, or Backspace in the empty input, moves to the last chip; there
 *   Backspace or Delete removes it, and the arrows move between chips and
 *   back to the input. The input is described by a hidden summary of the
 *   selection ("2 selected: Jeddah, Riyadh"); additions, removals and a
 *   refused pick are announced through the input's polite live region.
 */
@Component({
  selector: 'tm-entity-multi-picker',
  imports: [TmChip, TmChipSet, TmCounter, TmEntityPicker],
  providers: [{ provide: TM_FORM_FIELD_CONTROL, useExisting: TmEntityMultiPicker }],
  template: `
    <!-- The chips: one tab stop before the input, scrolling sideways in one
         line. Kept rendered while empty (and hidden), so the set is still
         there to hand focus back when its last focused chip goes. -->
    <tm-chip-set
      #chipSet
      class="tm-entity-multi-picker__chips"
      overflow="scroll"
      [class.tm-entity-multi-picker__chips--empty]="chips().length === 0"
      (exit)="onChipExit($event)"
    >
      @for (chip of chips(); track chip.id) {
        <tm-chip
          [label]="chip.label"
          [size]="chipSize()"
          [removable]="editable()"
          (remove)="remove(chip.id)"
        />
      }
    </tm-chip-set>
    <!-- The total, at the inline end of the chips. The visible count and a
         hidden "99+" probe share one grid cell, so the badge is as wide as
         it can ever get. Hidden from assistive technology: the summary
         below carries the count. -->
    @if (chips().length > 0) {
      <span class="tm-entity-multi-picker__count" aria-hidden="true">
        <tm-counter class="tm-entity-multi-picker__count-value" [value]="chips().length" />
        <tm-counter class="tm-entity-multi-picker__count-probe" [value]="widestCount" />
      </span>
    }
    <tm-entity-picker
      #picker
      class="tm-entity-multi-picker__search"
      [ɵcommitMode]="'emit'"
      [ɵchosenIds]="chosen()"
      [ɵmaxItems]="maxItems()"
      [ɵnotice]="notice()"
      [search]="search()"
      [itemId]="itemId()"
      [itemLabel]="itemLabel()"
      [itemDescription]="itemDescription()"
      [itemLabelParts]="itemLabelParts()"
      [advancedSearch]="advancedSearch()"
      [create]="create()"
      [createLabel]="createLabel()"
      [placeholder]="chips().length === 0 ? placeholder() : ''"
      [searchDebounce]="searchDebounce()"
      [disabled]="disabled()"
      [readonly]="readonly()"
      [required]="required()"
      [dirty]="dirty()"
      [invalid]="fieldInvalid()"
      [touched]="touched()"
      [pending]="fieldPending()"
      [errors]="errors()"
      [aria-label]="ariaLabel()"
      [aria-describedby]="pickerDescribedBy()"
      (picked)="onPicked($event)"
      (ɵdeselect)="onDeselect($event)"
      (ɵmultiPick)="onMultiPick($event)"
      (touch)="onTouch()"
    />
    <span [id]="summaryId" hidden>{{ summaryText() }}</span>
  `,
  styleUrl: './tm-entity-multi-picker.css',
  host: {
    class: 'tm-entity-multi-picker',
    // The accessible name and description live on the input; strip both
    // from the role-less host.
    '[attr.aria-label]': 'null',
    '[attr.aria-describedby]': 'null',
    // Standalone, the host draws the field's box itself (the dropdown
    // anchors to it through the attribute).
    '[class.tm-entity-multi-picker--standalone]': 'standalone',
    '[attr.data-tm-field-box]': 'standalone ? "" : null',
    '[attr.data-size]': 'standalone ? chipSize() : null',
    '[class.tm-entity-multi-picker--disabled]': 'disabled()',
    '[class.tm-entity-multi-picker--readonly]': 'readonly() && !disabled()',
    '[class.tm-entity-multi-picker--invalid]': 'showsInvalid()',
    '(focusout)': 'onFocusout($event)',
    '(click)': 'onHostClick($event)',
  },
})
export class TmEntityMultiPicker<
  T,
  Id extends TmEntityId = TmEntityId,
> implements TmFormFieldControl {
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly errorDisplay = inject(TM_ERROR_DISPLAY);
  private readonly locale = inject(TM_ACTIVE_LOCALE);
  private readonly hostElement = inject(ElementRef).nativeElement as HTMLElement;
  /** The enclosing field, if any: it sizes the chips and draws the box. */
  private readonly formField = inject(TmFormField, { optional: true });
  /** Whether no field encloses this control, so it draws its own box. */
  protected readonly standalone = this.formField === null;

  // ---- FormValueControl<readonly Id[]> + the optional state inputs ----
  /** The chosen ids, in pick order and never duplicated (the FormValueControl model). */
  readonly value = model<readonly Id[]>([]);
  /** Non-form usage only — the bound field is authoritative when bound via [formField]. */
  readonly disabled = input(false, { transform: booleanAttribute });
  /** Readonly state: the chips stay, without their remove buttons, and nothing searches. */
  readonly readonly = input(false, { transform: booleanAttribute });
  /**
   * Required state for non-form usage — the bound field is authoritative
   * when bound via [formField]. It sets `aria-required` on the input and
   * never raises an error by itself: Signal Forms' `required()` counts an
   * empty array as a value, so a form that needs at least one entity pairs
   * it with a length check (`validate` returning `requiredError()` for an
   * empty list).
   */
  readonly required = input(false, { transform: booleanAttribute });
  /** Dirty state for non-form usage — the bound field is authoritative when bound via [formField]. */
  readonly dirty = input(false, { transform: booleanAttribute });
  // The same three aliased state inputs as tm-entity-picker, for the same
  // reason: the input's own unresolved-text state merges into them.
  /**
   * The bound field's validity state. Aliased to the public name `invalid`;
   * the `invalid` member ORs it with the input's unresolved-text state.
   */
  // eslint-disable-next-line @angular-eslint/no-input-rename -- see above
  readonly fieldInvalid = input(false, { alias: 'invalid', transform: booleanAttribute });
  /**
   * The bound field's touched state. Aliased to the public name `touched`;
   * the `touched` member ORs it with the control's own departure tracking.
   */
  // eslint-disable-next-line @angular-eslint/no-input-rename -- see above
  readonly fieldTouched = input(false, { alias: 'touched', transform: booleanAttribute });
  /**
   * The bound field's async-validation-pending state. Aliased to the public
   * name `pending`; the `pending` member ORs it with the input's text
   * resolution.
   */
  // eslint-disable-next-line @angular-eslint/no-input-rename -- see above
  readonly fieldPending = input(false, { alias: 'pending', transform: booleanAttribute });
  /** The raw framework errors, bound by [formField] and localized into `localizedErrors`. */
  readonly errors = input<readonly ValidationError.WithOptionalFieldTree[]>([]);
  /** Touch reporting on real departure — `debounce('blur')` relies on it. */
  readonly touch = output<void>();

  // ---- The data seam (as tm-entity-picker) ----
  /** The consumer's search facility — the only data source. */
  readonly search = input.required<TmEntitySearchFn<T>>();
  /** Maps a search result to its id. */
  readonly itemId = input.required<(item: T) => Id>();
  /** Maps a search result to its display text (reactive, as in tm-entity-picker). */
  readonly itemLabel = input.required<(item: T) => string>();
  /** Maps a search result to a second line under its label (as in tm-entity-picker). */
  readonly itemDescription = input<((item: T) => string | null) | undefined>(undefined);
  /** Maps a search result to the parts of its label (as in tm-entity-picker). */
  readonly itemLabelParts = input<((item: T) => readonly string[] | null) | undefined>(undefined);
  /**
   * Resolves a chosen id to its chip label (reactive); `null` or the empty
   * string defers to the label the pick carried, then to `String(id)` with
   * a development warning.
   */
  readonly displayWith = input<((id: Id) => string | null) | undefined>(undefined);
  /**
   * The advanced-search page; absent ⇒ no magnifier and no footer row. It
   * opens with `multiple`, the chosen `ids` and `maxItems` in its data, and
   * closes with a `TmEntityMultiPick` whose ids replace the value.
   */
  readonly advancedSearch = input<TmEntityPickerPage | undefined>(undefined);
  /** The create page; absent ⇒ no Create… footer row. The created entity is added. */
  readonly create = input<TmEntityPickerPage | undefined>(undefined);
  /** Overrides the localized Create… footer-row caption. */
  readonly createLabel = input<string | undefined>(undefined);
  /** The most entities the selection may hold; further picks are refused. Unset, no cap. */
  readonly maxItems = input<number | undefined>(undefined);
  /** Placeholder text for the input while nothing is chosen. */
  readonly placeholder = input('');
  /** The search coalescing window in milliseconds (as tm-entity-picker). */
  readonly searchDebounce = input(50);
  /** Emits each id added to the selection, whatever added it. */
  readonly picked = output<TmEntityPicked<T, Id>>();
  /** Emits each id removed from the selection, whatever removed it. */
  readonly removed = output<Id>();

  /** Accessible name for a control used WITHOUT tm-form-field. */
  readonly ariaLabel = input<string | null>(null, { alias: 'aria-label' });
  /**
   * Author-supplied describedby ids (space-separated). Preserved — the
   * enclosing field's hint/error ids and the selection summary follow them.
   */
  readonly ariaDescribedby = input<string | null>(null, { alias: 'aria-describedby' });

  // ---- Internal state ----
  /** The search input — the composed single picker. */
  private readonly picker = viewChild<TmEntityPicker<T, Id>>('picker');
  /** The chips' list. */
  private readonly chipSet = viewChild.required<TmChipSet>('chipSet');
  /** Labels the picks carried, by id — the chip label fallback after `displayWith`. */
  private readonly memo = signal<ReadonlyMap<Id, string>>(new Map());
  /** One-shot guard for the missing-label dev warning. */
  private warnedMissingLabel = false;
  /** Whether the control itself has seen a real departure. */
  private readonly touchedSelf = signal(false);
  /** The field's hint/error ids, pushed through `setDescribedByIds`. */
  private readonly fieldDescribedBy = signal<readonly string[]>([]);
  /** Whether the enclosing field displays an error (see `setFieldError`). */
  private readonly fieldError = signal(false);
  /** The id of the hidden selection summary the input is described by. */
  protected readonly summaryId = `tm-entity-multi-picker-summary-${nextUniqueId++}`;
  /** The hidden probe's count (see `WIDEST_COUNT`). */
  protected readonly widestCount = WIDEST_COUNT;

  /** Whether the chips can be removed and the selection changed. */
  protected readonly editable = computed(() => !this.disabled() && !this.readonly());

  /**
   * The chip size step: the enclosing field's, else the workspace default
   * (`lg` on a phone) — the same resolution the field applies to itself.
   */
  protected readonly chipSize: Signal<TmControlSize> = ɵtmControlSize(
    computed(() => this.formField?.size()),
  );

  /**
   * The chosen ids without duplicates, first occurrence first. The control's
   * own writes never duplicate, but a value written from outside (a filter
   * restored from a URL) may; every chip, count and check reads this.
   */
  protected readonly chosen = computed(() => [...new Set(this.value())]);

  /** The chips, in value order, with their resolved labels. */
  protected readonly chips = computed(() =>
    this.chosen().map((id) => ({ id, label: this.labelFor(id) })),
  );

  /** The refusal message at the cap, or `null` while there is room. */
  private readonly capMessage = computed(() => {
    const max = this.maxItems();
    return max === undefined || this.chosen().length < max
      ? null
      : this.translate('entityPicker.maxItems', { max })();
  });

  /** The panel's status line: the cap message while the selection is full. */
  protected readonly notice = this.capMessage;

  /** The labels as one locale-formatted list ("Jeddah, Riyadh"). */
  private readonly labelList = computed(() =>
    this.formatList(this.chips().map((chip) => chip.label)),
  );

  /** The hidden summary the input is described by ("2 selected: Jeddah, Riyadh"). */
  protected readonly summaryText = computed(() =>
    this.chips().length === 0
      ? ''
      : this.translate('entityPicker.selectedSummary', {
          count: this.chips().length,
          labels: this.labelList(),
        })(),
  );

  /** The input's describedby: author ids, the field's ids, then the summary while chips exist. */
  protected readonly pickerDescribedBy = computed(() => {
    const ids = [...this.describedByIds()];
    if (this.chips().length > 0) {
      ids.push(this.summaryId);
    }
    return ids.join(' ') || null;
  });

  // ---- TmFormFieldControl ----
  /** The field draws the bordered box around the chips and the input. */
  readonly ownsChrome = false;
  /** The input draws the invalid glyph itself, before its magnifier. */
  readonly ownsErrorIcon = true;
  /** The input's id — the `<label for>` target. */
  readonly controlId: Signal<string> = computed(() => this.picker()?.controlId() ?? '');
  /** Every exposed describedby id: author-supplied first, then the field's hint/error ids. */
  readonly describedByIds: Signal<readonly string[]> = computed(() => [
    ...(this.ariaDescribedby()?.split(/\s+/).filter(Boolean) ?? []),
    ...this.fieldDescribedBy(),
  ]);
  /**
   * The validity the field reads: the bound field's own state OR the
   * input's unresolved text (typed text that names no entity).
   */
  readonly invalid: Signal<boolean> = computed(
    () => this.picker()?.invalid() ?? this.fieldInvalid(),
  );
  /** The touched state the field reads: the bound field's OR a real departure. */
  readonly touched: Signal<boolean> = computed(() => this.fieldTouched() || this.touchedSelf());
  /** The pending state the field reads: the bound field's OR the input's text resolution. */
  readonly pending: Signal<boolean> = computed(
    () => this.picker()?.pending() ?? this.fieldPending(),
  );
  /**
   * Already-localized error messages: the bound field's errors and the
   * input's own (a typed query that resolved to nothing, or to more than
   * one entity).
   */
  readonly localizedErrors: Signal<readonly TmFieldError[]> = computed(
    () => this.picker()?.localizedErrors() ?? [],
  );

  /** Whether the standalone box draws the invalid border. */
  protected readonly showsInvalid = computed(
    () =>
      this.fieldError() ||
      (this.localizedErrors().length > 0 &&
        this.errorDisplay({
          invalid: this.invalid(),
          touched: this.touched(),
          dirty: this.dirty(),
          pending: this.pending(),
        })),
  );

  constructor() {
    // The field's displayed-error state reaches the input, which draws the
    // invalid glyph and aria-invalid for both of them.
    effect(() => {
      const picker = this.picker();
      const showsError = this.fieldError();
      untracked(() => picker?.setFieldError(showsError));
    });

    // The keys that cross between the input and the chips, captured here
    // before any handler inside sees them: aria's combobox stops the
    // propagation of the arrows while the dropdown is open, so a listener
    // the event bubbles to would never hear them.
    this.hostElement.addEventListener('keydown', this.onCaptureKeydown, { capture: true });
    inject(DestroyRef).onDestroy(() => {
      this.hostElement.removeEventListener('keydown', this.onCaptureKeydown, { capture: true });
    });
  }

  // ---- Labels ----
  /**
   * A chip's label: `displayWith` when it has one, else the label the pick
   * carried, else `String(id)` with a one-shot development warning.
   */
  private labelFor(id: Id): string {
    const displayWith = this.displayWith();
    if (displayWith !== undefined) {
      const text = displayWith(id);
      if (text !== null && text !== '') {
        return text;
      }
    }
    const memoized = this.memo().get(id);
    if (memoized !== undefined) {
      return memoized;
    }
    if (typeof ngDevMode !== 'undefined' && ngDevMode && !this.warnedMissingLabel) {
      this.warnedMissingLabel = true;
      console.warn(
        `tm-entity-multi-picker: no label for chosen id "${String(id)}" — provide ` +
          `[displayWith] so chips render their labels (falling back to String(id)).`,
      );
    }
    return String(id);
  }

  /** Records the labels picks carried, for the chips `displayWith` cannot name. */
  private remember(entries: readonly { readonly id: Id; readonly label: string }[]): void {
    if (entries.length === 0) {
      return;
    }
    const next = new Map(untracked(this.memo));
    for (const entry of entries) {
      next.set(entry.id, entry.label);
    }
    this.memo.set(next);
  }

  /** Joins labels as the active locale writes a short list. */
  private formatList(labels: readonly string[]): string {
    return new Intl.ListFormat(this.locale(), { type: 'unit', style: 'short' }).format(labels);
  }

  // ---- Selection changes ----
  /**
   * A pick from the input. An id already chosen keeps its chip (only an
   * explicit list pick removes, and that arrives as a deselect); a pick at
   * the cap is refused with the cap message; anything else is appended.
   */
  protected onPicked(event: TmEntityPicked<T, Id>): void {
    const ids = untracked(this.chosen);
    if (ids.includes(event.id)) {
      return;
    }
    const refusal = untracked(this.capMessage);
    if (refusal !== null) {
      this.announce(refusal);
      return;
    }
    this.remember([event]);
    this.value.set([...ids, event.id]);
    this.announceKey(
      'entityPicker.announce.added',
      untracked(() => this.labelFor(event.id)),
    );
    this.picked.emit(event);
  }

  /** An explicit list pick on a chosen row: it comes off. */
  protected onDeselect(id: Id): void {
    this.remove(id);
  }

  /** Removes one chosen id (a chip's remove, or a toggled row). */
  protected remove(id: Id): void {
    const ids = untracked(this.chosen);
    if (!ids.includes(id) || !untracked(this.editable)) {
      return;
    }
    const label = untracked(() => this.labelFor(id));
    this.value.set(ids.filter((chosen) => chosen !== id));
    this.announceKey('entityPicker.announce.removed', label);
    this.removed.emit(id);
  }

  /**
   * The advanced-search page's whole selection replaces the value — an
   * empty one included — and the difference is emitted: `picked` for each
   * added id, `removed` for each removed one. The page's picks seed the
   * chip labels.
   */
  protected onMultiPick(pick: TmEntityMultiPick<Id, T>): void {
    const next = [...new Set(pick.ids)];
    const max = untracked(this.maxItems);
    if (typeof ngDevMode !== 'undefined' && ngDevMode && max !== undefined && next.length > max) {
      console.warn(
        `tm-entity-multi-picker: the advanced-search page returned ${next.length} ids, ` +
          `more than maxItems (${max}) — the page should refuse more than maxItems.`,
      );
    }
    this.remember(pick.picks.filter((entry) => next.includes(entry.id)));
    const before = untracked(this.chosen);
    const added = next.filter((id) => !before.includes(id));
    const removed = before.filter((id) => !next.includes(id));
    // Compared with the value as written, so a duplicated one is rewritten.
    const current = untracked(this.value);
    if (next.length !== current.length || next.some((id, i) => id !== current[i])) {
      this.value.set(next);
    }
    // One message for the whole outcome: what came on, what came off, or —
    // when both happened — the selection as it now stands.
    untracked(() => {
      if (added.length > 0 && removed.length > 0) {
        this.announce(this.summaryText());
      } else if (added.length > 0) {
        this.announceKey(
          'entityPicker.announce.added',
          this.formatList(added.map((id) => this.labelFor(id))),
        );
      } else if (removed.length > 0) {
        this.announceKey(
          'entityPicker.announce.removed',
          this.formatList(removed.map((id) => this.labelFor(id))),
        );
      }
    });
    for (const id of added) {
      const entry = pick.picks.find((p) => p.id === id);
      this.picked.emit({
        id,
        label: entry?.label ?? untracked(() => this.labelFor(id)),
        item: entry?.item,
        source: 'advanced',
      });
    }
    for (const id of removed) {
      this.removed.emit(id);
    }
  }

  // ---- Keyboard and focus ----
  /**
   * The input's way into the chips: ArrowLeft (ArrowRight in RTL) with the
   * caret at the start of the input, or Backspace in the empty input, moves
   * focus to the last chip. The second Backspace is the chip's own: it
   * removes the chip.
   */
  private onInputKeydown(event: KeyboardEvent, target: HTMLInputElement): void {
    if (
      !untracked(this.editable) ||
      untracked(this.chips).length === 0 ||
      event.altKey ||
      event.ctrlKey ||
      event.metaKey ||
      event.shiftKey ||
      event.isComposing ||
      event.defaultPrevented
    ) {
      return;
    }
    const atStart = target.selectionStart === 0 && target.selectionEnd === 0;
    const rtl = getComputedStyle(this.hostElement).direction === 'rtl';
    const backward = rtl ? 'ArrowRight' : 'ArrowLeft';
    if (
      (event.key === 'Backspace' && target.value === '' && !event.repeat) ||
      (event.key === backward && atStart)
    ) {
      event.preventDefault();
      this.chipSet().focusLast();
    }
  }

  /**
   * The host's capture-phase keydown (see the constructor): the input's way
   * into the chips, and the chips' guard against held keys.
   */
  private readonly onCaptureKeydown = (event: KeyboardEvent): void => {
    const target = event.target;
    if (!(target instanceof Element)) {
      return;
    }
    // The search input: the backward arrow and Backspace into the chips.
    if (
      target instanceof HTMLInputElement &&
      target.closest('.tm-entity-multi-picker__search') !== null
    ) {
      this.onInputKeydown(event, target);
      return;
    }
    // Held keys never mass-remove chips: Backspace in the empty input moves
    // to the last chip, and the key's auto-repeat — which then lands on the
    // chip — must not remove it and every chip before it. One press, one
    // removal, so the repeats are swallowed before the chip's own handler.
    if (
      event.repeat &&
      (event.key === 'Backspace' || event.key === 'Delete') &&
      target.closest('.tm-entity-multi-picker__chips') !== null
    ) {
      event.preventDefault();
      event.stopPropagation();
    }
  };

  /**
   * An arrow past the last chip, or the last chip removed with nothing left
   * to focus, returns to the input; an arrow past the first chip leaves
   * focus where it is.
   */
  protected onChipExit(edge: 'start' | 'end'): void {
    if (edge === 'end') {
      this.focus();
    }
  }

  /** The input reported a departure (to a chip, or out of the field). */
  protected onTouch(): void {
    this.markTouched();
  }

  /**
   * A departure from the chips to somewhere outside the control. Leaving
   * the input is the input's own report (see `onTouch`), which knows to
   * stand down while a page it launched holds focus.
   */
  protected onFocusout(event: FocusEvent): void {
    const from = event.target as Element;
    const to = event.relatedTarget as Node | null;
    if (from.closest('.tm-entity-multi-picker__chips') === null) {
      return;
    }
    if (to !== null && this.hostElement.contains(to)) {
      return;
    }
    this.markTouched();
  }

  /** Records a departure and reports it. */
  private markTouched(): void {
    this.touchedSelf.set(true);
    this.touch.emit();
  }

  /** A press on the standalone box itself (not a chip, not the input) focuses the input. */
  protected onHostClick(event: MouseEvent): void {
    if (this.standalone && event.target === this.hostElement) {
      this.focus();
    }
  }

  // ---- Announcements ----
  /** Announces a localized `{label}` message (see `announce`). */
  private announceKey(key: string, label: string): void {
    this.announce(untracked(this.translate(key, { label })));
  }

  /**
   * Announces a message through the inner picker's polite live region — one
   * region for the whole control, so a result count and an addition never
   * compete in two.
   */
  private announce(message: string): void {
    untracked(this.picker)?.ɵannounce(message);
  }

  // ---- TmFormFieldControl plumbing ----
  /** Receives the field's hint/error ids and exposes them via the input's aria-describedby. */
  setDescribedByIds(ids: readonly string[]): void {
    this.fieldDescribedBy.set(ids);
  }

  /** Receives whether the enclosing field displays an error (its plain `error` input included). */
  setFieldError(showsError: boolean): void {
    this.fieldError.set(showsError);
  }

  /** Focuses the input when the user clicks the field's container chrome. */
  onContainerClick(): void {
    this.focus();
  }

  /** Focuses the input; Signal Forms calls this when asked to focus the field. */
  focus(options?: FocusOptions): void {
    this.picker()?.focus(options);
  }

  /** Opens the dropdown on the browse list. */
  openDropdown(): void {
    this.picker()?.openDropdown();
  }
}

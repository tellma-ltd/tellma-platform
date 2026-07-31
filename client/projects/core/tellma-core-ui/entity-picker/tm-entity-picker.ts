// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterRenderEffect,
  booleanAttribute,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  input,
  isDevMode,
  model,
  output,
  signal,
  untracked,
  viewChild,
  viewChildren,
  type Signal,
  type Type,
} from '@angular/core';
import { Combobox, ComboboxPopup, ComboboxWidget } from '@angular/aria/combobox';
import { Listbox, Option } from '@angular/aria/listbox';
import { CdkConnectedOverlay, OverlayModule } from '@angular/cdk/overlay';
import { transformedValue, type ParseResult, type ValidationError } from '@angular/forms/signals';

import type {
  SignalLike,
  TmCellEditor,
  TmFieldError,
  TmFormFieldControl,
} from '@tellma/core-ui/contracts';
import {
  TM_CELL_EDITOR_HOST,
  TM_ERROR_DISPLAY,
  TM_UI_TRANSLATE,
  tmResolveFieldErrors,
} from '@tellma/core-ui';
import { TM_FORM_FIELD_CONTROL, TmFormField } from '@tellma/core-ui/form-field';
import { TmModal, type TmModalRef, type TmModalSize } from '@tellma/core-ui/modal';
import { tmCreateAnchoredOverlay, tmLogicalPositions } from '@tellma/core-ui/private';
import { TmSpinner } from '@tellma/core-ui/spinner';

import type {
  TmEntityId,
  TmEntityPick,
  TmEntityPicked,
  TmEntityPickerPage,
  TmEntityPickerPageData,
  TmEntitySearchFn,
  TmEntitySearchResult,
} from './tm-entity-picker-types';

let nextUniqueId = 0;

/**
 * Footer-row sentinel values inside the listbox. Symbols can never collide
 * with a consumer's `string | number` entity ids, so the activation
 * dispatcher can tell a command row from an entity row by value alone.
 */
const ACTION_ADVANCED: unique symbol = Symbol('tm-entity-picker:advanced');
const ACTION_CREATE: unique symbol = Symbol('tm-entity-picker:create');
const ACTION_EDIT: unique symbol = Symbol('tm-entity-picker:edit');

/** A search result set normalized to the object form. */
interface NormalizedResult<T> {
  /** The result items, in the order the consumer returned them. */
  readonly items: readonly T[];
  /** Whether the consumer flagged the set as server-truncated. */
  readonly hasMore: boolean;
}

/** The dropdown's search lifecycle state. */
type SearchState<T> =
  | { readonly kind: 'idle' }
  | { readonly kind: 'loading'; readonly query: string }
  | {
      readonly kind: 'results';
      readonly query: string;
      readonly items: readonly T[];
      readonly hasMore: boolean;
    }
  | { readonly kind: 'empty'; readonly query: string }
  | { readonly kind: 'error'; readonly query: string };

/** An outstanding async search request. */
interface InFlightSearch<T> {
  /** The query the request was issued for. */
  readonly query: string;
  /** Aborts the request when it is superseded. */
  readonly controller: AbortController;
  /** Resolves to the normalized outcome; never rejects. */
  readonly settled: Promise<NormalizedResult<T> | 'failed'>;
}

/** A recorded text-resolution failure — the no-match/ambiguous/failed error states. */
interface ResolutionFailure {
  /** The exact text the failure was recorded for. */
  readonly text: string;
  /** Which localized message the failure carries. */
  readonly kind: 'noMatch' | 'ambiguous' | 'searchFailed';
}

/** Whether a search return is a promise (async) or a plain result (sync). */
function isThenable<T>(
  result: TmEntitySearchResult<T> | Promise<TmEntitySearchResult<T>>,
): result is Promise<TmEntitySearchResult<T>> {
  return typeof (result as { then?: unknown } | null | undefined)?.then === 'function';
}

/**
 * The server-searched foreign-key selector: an editable combobox
 * (`ngCombobox` on the input) over a consumer-supplied search function,
 * with an optional advanced-search magnifier and Create…/Edit… footer rows
 * that launch consumer modal pages.
 *
 * The text is a query surface, never the value: the committed value is the
 * entity id, written only by picks (list rows, modal pages, unique-match
 * auto-resolution), by an empty commit, and by a failed resolution (which
 * writes `null`, keeps the text for correction, and raises a localized
 * error). While typed text is unresolved the field carries a `parse`-kind
 * error, so a racing submit can never save a phantom value.
 *
 * Display text for a committed id resolves through `displayWith` (reactive
 * — an implementation reading the ambient locale re-renders in place), then
 * the pick-time label memo, then `String(id)` with a dev-mode warning.
 *
 * @tmGroup form-control
 * @tmA11yNotes Editable combobox with a listbox popup: DOM focus stays on
 *   the input while aria-activedescendant tracks the highlighted option
 *   across the overlay portal. The magnifier button is not a tab stop; its
 *   keyboard equivalent is the Advanced search… footer row. Async status
 *   (result counts, no results, failure, auto-resolution outcome) is
 *   announced through a polite live region on fetch completion only.
 */
@Component({
  selector: 'tm-entity-picker',
  imports: [Combobox, ComboboxPopup, ComboboxWidget, Listbox, Option, OverlayModule, TmSpinner],
  providers: [{ provide: TM_FORM_FIELD_CONTROL, useExisting: TmEntityPicker }],
  template: `
    <input
      #textInput
      ngCombobox
      #cb="ngCombobox"
      type="text"
      class="tm-input tm-entity-picker__input"
      autocomplete="off"
      spellcheck="false"
      dir="auto"
      [id]="controlId()"
      [class.tm-input--in-field]="!!formField"
      [(expanded)]="expanded"
      [(value)]="comboText"
      [disabled]="disabled() || readonly()"
      [softDisabled]="readonly() && !disabled()"
      [required]="required()"
      [placeholder]="placeholder()"
      [attr.aria-label]="ariaLabel()"
      [attr.aria-describedby]="describedByAttr()"
      [attr.aria-invalid]="showsInvalid() ? 'true' : null"
      [attr.aria-busy]="pending() ? 'true' : null"
      (input)="onInput()"
      (click)="onInputClick()"
      (focusin)="onFocusin()"
      (focusout)="onFocusout($event)"
      (keydown)="onInputKeydown($event)"
    />
    @if (advancedSearch() !== undefined) {
      <button
        type="button"
        class="tm-entity-picker__magnifier tm-form-field__trailing-icon"
        tabindex="-1"
        [disabled]="disabled() || readonly()"
        [attr.aria-label]="magnifierLabel()"
        (pointerdown)="$event.preventDefault()"
        (click)="onMagnifierClick()"
      >
        <svg viewBox="0 0 16 16" fill="none" aria-hidden="true">
          <circle cx="7" cy="7" r="4.25" stroke="currentColor" stroke-width="1.5" />
          <path
            d="M10.5 10.5L14 14"
            stroke="currentColor"
            stroke-width="1.5"
            stroke-linecap="round"
          />
        </svg>
      </button>
    }
    <span class="tm-entity-picker__live" aria-live="polite" aria-atomic="true">{{
      liveText()
    }}</span>

    <ng-template
      [cdkConnectedOverlay]="anchored.overlayConfig()"
      [cdkConnectedOverlayOpen]="expanded()"
      (attach)="anchored.handleAttach()"
      (detach)="anchored.handleDetach()"
      (overlayOutsideClick)="onOutsideClick()"
    >
      <ng-template ngComboboxPopup [combobox]="cb">
        <div
          #panel
          class="tm-entity-picker__panel"
          [attr.aria-busy]="statusKind() === 'loading' ? 'true' : null"
          (pointerdown)="onPanelPointerdown($event)"
        >
          <ul
            ngListbox
            ngComboboxWidget
            #lb="ngListbox"
            class="tm-entity-picker__listbox"
            [tabindex]="-1"
            focusMode="activedescendant"
            selectionMode="explicit"
            [(value)]="listboxValue"
            [activeDescendant]="lb.activeDescendant()"
            (click)="onListboxClick($event)"
            (keydown.enter)="onListboxEnter()"
          >
            @for (item of resultItems(); track idOf(item)) {
              <li
                ngOption
                class="tm-entity-picker__option"
                [value]="idOf(item)"
                [label]="labelOf(item)"
              >
                <span class="tm-entity-picker__option-label">{{ labelOf(item) }}</span>
                <svg
                  class="tm-entity-picker__check"
                  viewBox="0 0 16 16"
                  fill="none"
                  aria-hidden="true"
                >
                  <polyline
                    points="3.5,8.5 6.5,11.5 12.5,4.5"
                    stroke="currentColor"
                    stroke-width="2"
                    stroke-linecap="round"
                    stroke-linejoin="round"
                  />
                </svg>
              </li>
            }
            @if (showsHasMore()) {
              <li class="tm-entity-picker__hint" aria-hidden="true">{{ moreResultsText() }}</li>
            }
            @if (statusKind() !== null) {
              <li class="tm-entity-picker__status" aria-hidden="true">
                @switch (statusKind()) {
                  @case ('loading') {
                    <tm-spinner class="tm-entity-picker__status-spinner" />
                  }
                  @case ('empty') {
                    <span>{{ noResultsText() }}</span>
                  }
                  @case ('error') {
                    <span>{{ searchFailedText() }}</span>
                  }
                }
              </li>
            }
            @if (hasFooterRows()) {
              <li class="tm-entity-picker__separator" aria-hidden="true"></li>
            }
            @if (advancedSearch() !== undefined) {
              <li
                ngOption
                class="tm-entity-picker__option tm-entity-picker__action"
                [value]="actionAdvanced"
                [label]="advancedRowLabel()"
              >
                <span class="tm-entity-picker__option-label">{{ advancedRowLabel() }}</span>
              </li>
            }
            @if (create() !== undefined) {
              <li
                ngOption
                class="tm-entity-picker__option tm-entity-picker__action"
                [value]="actionCreate"
                [label]="createRowLabel()"
              >
                <span class="tm-entity-picker__option-label">{{ createRowLabel() }}</span>
              </li>
            }
            @if (edit() !== undefined && value() !== null) {
              <li
                ngOption
                class="tm-entity-picker__option tm-entity-picker__action"
                [value]="actionEdit"
                [label]="editRowLabel()"
              >
                <span class="tm-entity-picker__option-label">{{ editRowLabel() }}</span>
              </li>
            }
          </ul>
        </div>
      </ng-template>
    </ng-template>
  `,
  styleUrl: './tm-entity-picker.css',
  host: {
    class: 'tm-entity-picker',
    // The accessible name and description live on the input; strip both
    // from the role-less host.
    '[attr.aria-label]': 'null',
    '[attr.aria-describedby]': 'null',
    '[class.tm-entity-picker--disabled]': 'disabled()',
    '[class.tm-entity-picker--open]': 'expanded()',
  },
})
export class TmEntityPicker<T, Id extends TmEntityId = TmEntityId>
  implements TmFormFieldControl, TmCellEditor<Id | null>
{
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly errorDisplay = inject(TM_ERROR_DISPLAY);
  private readonly modal = inject(TmModal);
  private readonly destroyRef = inject(DestroyRef);
  private readonly hostElement = inject(ElementRef).nativeElement as HTMLElement;
  /** The enclosing grid cell's registration sink, if any — absent standalone. */
  private readonly cellHost = inject(TM_CELL_EDITOR_HOST, { optional: true });
  /** The enclosing field, if any — flags `--in-field` chrome dissolution. */
  protected readonly formField = inject(TmFormField, { optional: true });

  // ---- FormValueControl<Id | null> + the optional state inputs ----
  /** The committed foreign-key id (the FormValueControl model) — THE source of truth. */
  readonly value = model<Id | null>(null);
  /** Non-form usage only — the bound field is authoritative when bound via [formField]. */
  readonly disabled = input(false, { transform: booleanAttribute });
  /** Readonly state — also suppresses the dropdown, the search, and the magnifier. */
  readonly readonly = input(false, { transform: booleanAttribute });
  /** Required state for non-form usage — the bound field is authoritative when bound via [formField]. */
  readonly required = input(false, { transform: booleanAttribute });
  /** Validity state for non-form usage — the bound field is authoritative when bound via [formField]. */
  readonly invalid = input(false, { transform: booleanAttribute });
  /** Touched state for non-form usage — the bound field is authoritative when bound via [formField]. */
  readonly touched = input(false, { transform: booleanAttribute });
  /** Dirty state for non-form usage — the bound field is authoritative when bound via [formField]. */
  readonly dirty = input(false, { transform: booleanAttribute });
  /**
   * The bound field's async-validation-pending state. Aliased to the public
   * name `pending` so `[formField]` binds it; the contract's `pending`
   * member merges it with the picker's own resolution state.
   */
  // Signal Forms binds field state by PUBLIC input name ('pending'), while
  // the form-field contract needs a `pending` MEMBER merging that state with
  // the picker's own resolution pending — one name, two channels, so the
  // input must live under an alias.
  // eslint-disable-next-line @angular-eslint/no-input-rename -- see above
  readonly fieldPending = input(false, { alias: 'pending', transform: booleanAttribute });
  /** The raw framework errors, bound by [formField] and localized into `localizedErrors`. */
  readonly errors = input<readonly ValidationError.WithOptionalFieldTree[]>([]);
  /** Touch reporting on real departure — `debounce('blur')` relies on it. */
  readonly touch = output<void>();

  // ---- The data seam ----
  /** The consumer's search facility — the picker's only data source. */
  readonly search = input.required<TmEntitySearchFn<T>>();
  /** Maps a search result to its id. */
  readonly itemId = input.required<(item: T) => Id>();
  /**
   * Maps a search result to its display text. Called in a reactive context:
   * an implementation reading signals (the ambient locale, an entity cache)
   * re-renders every visible label in place when those signals change.
   */
  readonly itemLabel = input.required<(item: T) => string>();
  /**
   * Resolves a committed id to display text (reactive, like `itemLabel`);
   * `null` defers to the pick-time label memo, then to `String(id)` with a
   * dev-mode warning.
   */
  readonly displayWith = input<((id: Id) => string | null) | undefined>(undefined);
  /** The advanced-search page; absent ⇒ no magnifier and no footer row. */
  readonly advancedSearch = input<TmEntityPickerPage | undefined>(undefined);
  /** The create page; absent ⇒ no Create… footer row. */
  readonly create = input<TmEntityPickerPage | undefined>(undefined);
  /**
   * The edit page; absent ⇒ no Edit… footer row. The row shows only while a
   * value is committed, opens the page on that value, and honors the page's
   * `close(null)` by clearing the field (the entity no longer exists).
   */
  readonly edit = input<TmEntityPickerPage | undefined>(undefined);
  /** Overrides the localized Create… footer-row caption. */
  readonly createLabel = input<string | undefined>(undefined);
  /** Overrides the localized Edit… footer-row caption. */
  readonly editLabel = input<string | undefined>(undefined);
  /** Placeholder text for the empty input. */
  readonly placeholder = input('');
  /**
   * The search coalescing window in milliseconds (leading + trailing): the
   * first change after idle fires immediately, subsequent changes inside
   * the window coalesce into one trailing request. `0` disables coalescing.
   * The window is skipped while the source is known-synchronous.
   */
  readonly searchDebounce = input(50);
  /** Emits every committed selection, whatever produced it. */
  readonly picked = output<TmEntityPicked<T, Id>>();
  /**
   * Grid-host activation: emits on pointer/Enter option activation and on
   * modal-pick application — never on Tab-commits (the grid's own
   * commit-and-move handles those) and never on auto-resolution picks.
   * @internal
   */
  readonly ɵcellActivate = output<void>();

  /** Accessible name for a picker used WITHOUT tm-form-field. */
  readonly ariaLabel = input<string | null>(null, { alias: 'aria-label' });
  /**
   * Author-supplied describedby ids (space-separated). Preserved — the
   * enclosing field's hint/error ids are merged AFTER them, never over them.
   */
  readonly ariaDescribedby = input<string | null>(null, { alias: 'aria-describedby' });

  /** Stable generated id of the input — the `<label for>` and aria wiring target. */
  readonly controlId = signal(`tm-entity-picker-${nextUniqueId++}`).asReadonly();

  // ---- Internal state ----
  /** Whether the dropdown is open (aria's combobox expanded model). */
  protected readonly expanded = signal(false);
  /** The combobox text model — aria mirrors it into the native input. */
  protected readonly comboText = signal('');
  /** aria's listbox value — an ARRAY of keys; never the source of truth. */
  protected readonly listboxValue = signal<unknown[]>([]);
  /** Whether the input currently has focus (gates display rewrites). */
  private readonly focused = signal(false);
  /** Cell-editor revert baseline: the value `cancel()` returns to. */
  private lastCommitted: Id | null = null;
  /** Marks this control's own model writes (see the baseline effect). */
  private selfWrite: { readonly value: Id | null } | null = null;
  /** Picker-authored text with its known value; see `setCanonicalText`. */
  private displayOverride: { readonly text: string; readonly value: Id | null } | null = null;
  /** The recorded resolution failure, keyed to its exact text. */
  private readonly resolutionFailure = signal<ResolutionFailure | null>(null);
  /** Whether a blur/Enter resolution is pending — drives `pending` + aria-busy. */
  private readonly resolving = signal(false);
  /**
   * Monotonic supersession token for SEARCHES: whoever holds the latest
   * token owns the result rendering; anything older is discarded on
   * arrival. Honoring the AbortSignal is an optimization — this discard is
   * the correctness guarantee.
   */
  private searchEpoch = 0;
  /**
   * Monotonic supersession token for RESOLUTIONS, deliberately separate
   * from the search token: an external value write must kill a pending
   * resolution WITHOUT discarding an in-flight search (the grid's
   * open-editor sequence writes the value and then seeds a search in the
   * same task — one shared token would let the write's deferred effect
   * strand that search's spinner forever).
   */
  private resolutionEpoch = 0;
  /** Pick-time labels by id — the display fallback when `displayWith` is absent. */
  private readonly memo = new Map<Id, string>();
  /** One-shot guard for the missing-display dev warning. */
  private warnedMissingDisplay = false;
  /** True once destroy ran — guards late async continuations. */
  private destroyed = false;
  /** Suppresses blur handling while a picker-launched modal owns focus. */
  private suppressBlur = false;
  /** The picker-launched modal currently open, if any (destroy closes it). */
  private openModal: TmModalRef<TmEntityPick<Id, T> | null> | null = null;

  // ---- Search lifecycle state ----
  /** The dropdown's search state machine. */
  private readonly searchState = signal<SearchState<T>>({ kind: 'idle' });
  /** The outstanding async request, if any. */
  private inFlight: InFlightSearch<T> | null = null;
  /** The abort controller of a resolution-issued request, if any. */
  private resolutionController: AbortController | null = null;
  /** The debounce window timer. */
  private debounceTimer: ReturnType<typeof setTimeout> | undefined;
  /** Whether the debounce window is currently open. */
  private windowOpen = false;
  /** The query coalesced into the trailing edge of the window. */
  private pendingQuery: string | undefined;
  /** Whether the previous search invocation returned synchronously. */
  private lastSearchWasSync = false;
  /**
   * The pending highlight-protocol application. A signal (not a one-shot
   * render hook) because the popup mounts across MULTIPLE render passes —
   * the overlay attaches first, aria's deferred content renders the listbox
   * a pass later — and the request must survive until the listbox exists.
   */
  private readonly highlightRequest = signal<{
    readonly query: string;
    readonly count: number;
    readonly seq: number;
  } | null>(null);
  /** Monotonic sequence for highlight requests (each fresh result set re-applies). */
  private highlightSeq = 0;

  /** The raw text channel over the value model. */
  private readonly rawText = transformedValue<Id | null, string>(this.value, {
    parse: (text) => this.parseText(text),
    // Fully untracked: a tracked read here would re-run the linked signal's
    // computation on consumer-signal changes and clobber in-progress typing;
    // display reactivity is delivered by the explicit reformat effect.
    format: (id) => untracked(() => this.displayFor(id)),
  });

  // ---- View queries ----
  private readonly textInput = viewChild.required<ElementRef<HTMLInputElement>>('textInput');
  private readonly overlay = viewChild(CdkConnectedOverlay);
  private readonly listbox = viewChild(Listbox);
  private readonly panelElement = viewChild<ElementRef<HTMLElement>>('panel');
  /** aria's rendered `[ngOption]` directives — activation reads active state. */
  private readonly ariaOptions = viewChildren(Option);

  /**
   * The shared anchored-overlay wiring. The origin is the CHROME the user
   * reads as the field — the bordered box when wrapped, the grid cell's
   * editor host when mounted in a cell, the bare host standalone.
   */
  protected readonly anchored = tmCreateAnchoredOverlay({
    overlay: () => this.overlay(),
    origin: () =>
      this.hostElement.closest<HTMLElement>('.tm-form-field__box, [data-tm-editor]') ??
      this.hostElement,
    positions: tmLogicalPositions('block-end', 'start'),
    matchWidth: true,
    remeasure: 'macrotask',
  });

  // ---- Template helpers ----
  /** Footer-row sentinel exposed to the template. */
  protected readonly actionAdvanced = ACTION_ADVANCED;
  /** Footer-row sentinel exposed to the template. */
  protected readonly actionCreate = ACTION_CREATE;
  /** Footer-row sentinel exposed to the template. */
  protected readonly actionEdit = ACTION_EDIT;

  /** The entity rows currently rendered (empty outside the results state). */
  protected readonly resultItems: Signal<readonly T[]> = computed(() => {
    const state = this.searchState();
    return state.kind === 'results' ? state.items : [];
  });
  /** Whether the truncation hint renders (results flagged `hasMore`). */
  protected readonly showsHasMore = computed(() => {
    const state = this.searchState();
    return state.kind === 'results' && state.hasMore;
  });
  /** Which status row renders: spinner, no-results, failure — or none. */
  protected readonly statusKind = computed<'loading' | 'empty' | 'error' | null>(() => {
    const kind = this.searchState().kind;
    return kind === 'loading' || kind === 'empty' || kind === 'error' ? kind : null;
  });
  /** Whether any footer row is configured (renders the separator). */
  protected readonly hasFooterRows = computed(
    () =>
      this.advancedSearch() !== undefined ||
      this.create() !== undefined ||
      (this.edit() !== undefined && this.value() !== null),
  );

  /** The localized magnifier aria-label. */
  protected readonly magnifierLabel = this.translate('entityPicker.magnifier');
  /** The localized "No results" status text. */
  protected readonly noResultsText = this.translate('entityPicker.noResults');
  /** The localized search-failure status text. */
  protected readonly searchFailedText = this.translate('entityPicker.searchFailed');
  /** The localized truncation hint. */
  protected readonly moreResultsText = this.translate('entityPicker.moreResults');
  private readonly defaultAdvancedLabel = this.translate('entityPicker.advancedSearch');
  private readonly defaultCreateLabel = this.translate('entityPicker.create');
  private readonly defaultEditLabel = this.translate('entityPicker.edit');
  /** The Advanced search… footer-row caption. */
  protected readonly advancedRowLabel = computed(() => this.defaultAdvancedLabel());
  /** The Create… footer-row caption (consumer override wins). */
  protected readonly createRowLabel = computed(() => this.createLabel() ?? this.defaultCreateLabel());
  /** The Edit… footer-row caption (consumer override wins). */
  protected readonly editRowLabel = computed(() => this.editLabel() ?? this.defaultEditLabel());

  /** The live-region text — announcements on fetch completion only. */
  protected readonly liveText = signal('');

  // ---- TmFormFieldControl ----
  /** The field renders the bordered box around this bare anatomy. */
  readonly ownsChrome = false;
  private readonly fieldDescribedBy = signal<readonly string[]>([]);
  /** Every exposed describedby id: author-supplied first, then the field's hint/error ids. */
  readonly describedByIds: Signal<readonly string[]> = computed(() => [
    ...(this.ariaDescribedby()?.split(/\s+/).filter(Boolean) ?? []),
    ...this.fieldDescribedBy(),
  ]);
  /** Already-localized error messages resolved from `errors` — read by the enclosing field. */
  readonly localizedErrors: () => readonly TmFieldError[] = tmResolveFieldErrors(
    this.errors,
    this.translate,
  );
  /**
   * The pending state the field reads: the bound field's own pending OR the
   * picker's text resolution — the field shows its trailing spinner and the
   * error-display policy holds errors until the resolution lands.
   */
  readonly pending: Signal<boolean> = computed(() => this.fieldPending() || this.resolving());

  /** The merged aria-describedby attribute value, or null when no ids apply. */
  protected readonly describedByAttr = computed(() => this.describedByIds().join(' ') || null);
  /** aria-invalid follows the error-DISPLAY policy; own parse errors count. */
  protected readonly showsInvalid = computed(() =>
    this.errorDisplay({
      invalid: this.invalid() || this.rawText.parseErrors().length > 0,
      touched: this.touched() || this.touchedSelf(),
      dirty: this.dirty(),
      pending: this.pending(),
    }),
  );
  private readonly touchedSelf = signal(false);

  constructor() {
    this.cellHost?.register(this);

    // Effect 1: a control that goes disabled/readonly while its dropdown is
    // open must not stay interactive.
    effect(() => {
      if (this.disabled() || this.readonly()) {
        untracked(() => this.expanded.set(false));
      }
    });

    // Effect 2: external value writes (form resets, grid loads) move the
    // revert baseline and supersede any pending search/resolution; the
    // control's own parse-driven writes (marked via `selfWrite`) do not.
    effect(() => {
      const value = this.value();
      const self = this.selfWrite;
      this.selfWrite = null;
      if (self !== null && Object.is(self.value, value)) {
        return;
      }
      this.lastCommitted = value;
      untracked(() => {
        // A pin from a previous pick must not claim re-typed old text after
        // an external write; a pending resolution must not land over one.
        // An in-flight SEARCH is deliberately left alone (its own token
        // still stands): the write may be the grid installing the value
        // right before seeding a search, and killing it here would strand
        // the dropdown's spinner.
        this.displayOverride = null;
        ++this.resolutionEpoch;
        this.resolutionController?.abort();
        this.resolutionController = null;
        this.resolving.set(false);
        this.resolutionFailure.set(null);
      });
    });

    // Effect 3: reflect the raw text into the combobox text model — only
    // while unfocused (the user's text wins while focused). aria mirrors
    // the combobox model into the native input after render.
    effect(() => {
      const text = this.rawText();
      if (!this.focused() && untracked(this.comboText) !== text) {
        this.comboText.set(text);
      }
    });

    // Effect 4: locale/displayWith switches re-render the committed display
    // and kept error messages in place; the model never changes.
    effect(() => {
      const id = this.value();
      // Tracked reads: the consumer's displayWith closure (and whatever
      // signals it reads — the ambient locale, an entity cache) plus the
      // LIVE error message itself, so a locale switch that translates it
      // re-triggers the re-issue below. `resolving` is tracked too: while a
      // resolution is pending the model still holds the OLD id behind
      // unresolved text, and a reformat here would clobber that text AND
      // clear the submit-blocking error — the effect stands down until the
      // outcome lands (and re-evaluates the display then).
      const resolving = this.resolving();
      const display = id === null ? '' : this.displayFor(id);
      const failure = this.resolutionFailure();
      if (failure !== null) {
        this.failureMessage(failure)();
      } else {
        this.translate('entityPicker.errors.unresolved')();
      }
      untracked(() => {
        // The pin is keyed to the display context that produced it: retire
        // it on EVERY path — a stale pin would bind fresh text to a dead
        // context's value.
        this.displayOverride = null;
        if (this.focused() || resolving) {
          return;
        }
        const raw = this.rawText();
        const hasLiveError = this.rawText.parseErrors().length > 0 || failure !== null;
        if (id === null && hasLiveError) {
          // Kept error text is never erased by a locale switch — but its
          // message must re-render in the new locale, so the parse is
          // re-issued for the same text (the parser runs unconditionally).
          this.rawText.set(raw);
          return;
        }
        if (raw !== display) {
          this.setCanonicalText(display, id);
        }
      });
    });

    // Effect 5: the ONE-DIRECTIONAL value bridge — mirror the committed id
    // into aria's listbox, re-applied whenever the option set changes.
    // Defeats aria's unmatched-value auto-prune AND corrects the bare
    // listbox's Enter-toggleOne on footer rows before paint; commits ride
    // activation only (see the mouse-bug guard around angular/components#32504).
    effect(() => {
      const id = this.value();
      this.resultItems(); // re-apply on option turnover
      this.hasFooterRows(); // …including footer-row turnover
      const current = this.listboxValue();
      const desired: unknown[] = id === null ? [] : [id];
      if (current.length !== desired.length || current[0] !== desired[0]) {
        this.listboxValue.set(desired);
      }
    });

    // Effect 6: dropdown open/close transitions.
    let wasExpanded = false;
    effect(() => {
      const isExpanded = this.expanded();
      if (isExpanded === wasExpanded) {
        return;
      }
      wasExpanded = isExpanded;
      untracked(() => (isExpanded ? this.onPopupOpened() : this.onPopupClosed()));
    });

    // Effect 7: disarm aria's default-highlight machinery on every (per-
    // open) listbox instance. setDefaultStateEffect would otherwise
    // activate the selected/first option on items change — the picker owns
    // highlighting exclusively (typed queries highlight the first result;
    // pristine browse lists, empty sets, and footer rows highlight
    // nothing). Version-locked internal seam, guarded by unit tests.
    effect(() => {
      const listbox = this.listbox();
      if (listbox !== undefined) {
        untracked(() => {
          listbox._pattern.hasBeenInteracted.set(true);
        });
      }
    });

    // The highlight protocol, applied after the fresh rows render. A
    // persistent effect tracking the request AND the listbox: the listbox
    // mounts a render pass after the overlay attaches (aria's deferred
    // content), so a one-shot hook registered at result time would fire too
    // early and find no listbox. aria's own default-activation machinery is
    // disarmed (see the hasBeenInteracted effect), so this effect is the
    // only highlight writer besides the user's arrow keys.
    afterRenderEffect({
      write: () => {
        const request = this.highlightRequest();
        const listbox = this.listbox();
        if (request === null || listbox === undefined || !untracked(this.expanded)) {
          return;
        }
        // Untracked: gotoFirst() READS aria's item/active signals internally —
        // tracked, this effect would re-run on every arrow-key move and snap
        // the highlight back to the first result.
        untracked(() => {
          if (request.query !== '' && request.count > 0) {
            listbox.gotoFirst();
            listbox.scrollActiveItemIntoView();
          } else {
            listbox._pattern.listBehavior.unfocus();
          }
        });
      },
    });

    // The capture-phase keyboard layer (see onCaptureKeydown).
    this.hostElement.addEventListener('keydown', this.onCaptureKeydown, { capture: true });

    this.destroyRef.onDestroy(() => {
      this.destroyed = true;
      ++this.searchEpoch;
      ++this.resolutionEpoch;
      this.inFlight?.controller.abort();
      this.inFlight = null;
      this.resolutionController?.abort();
      this.resolutionController = null;
      this.cancelDebounce();
      this.openModal?.close();
      this.hostElement.removeEventListener('keydown', this.onCaptureKeydown, { capture: true });
    });
  }

  // ---- Display resolution ----
  /**
   * The display text for a committed id: `displayWith` when provided and
   * non-null (the reactive, recommended path); else the memoized pick-time
   * label; else `String(id)` with a one-shot dev warning — visible and
   * honest, never a silently empty field that claims to hold a value.
   */
  private displayFor(id: Id | null): string {
    if (id === null) {
      return '';
    }
    const displayWith = this.displayWith();
    if (displayWith !== undefined) {
      const text = displayWith(id);
      if (text !== null) {
        return text;
      }
    }
    const memoized = this.memo.get(id);
    if (memoized !== undefined) {
      return memoized;
    }
    if (isDevMode() && !this.warnedMissingDisplay) {
      this.warnedMissingDisplay = true;
      console.warn(
        `tm-entity-picker: no display text for committed id "${String(id)}" — ` +
          `provide [displayWith] so committed values render their labels ` +
          `(falling back to String(id)).`,
      );
    }
    return String(id);
  }

  /** The entity id of a search result (template + track helper). */
  protected idOf(item: T): Id {
    return this.itemId()(item);
  }

  /** The display label of a search result — reactive via the template. */
  protected labelOf(item: T): string {
    return this.itemLabel()(item);
  }

  // ---- The text channel ----
  /**
   * The raw→model parse. Typed text NEVER writes the model (the parse
   * omits `value`); writes happen only through the pin (picks, canonical
   * reformat), the pristine identity, and a recorded resolution failure
   * (which is the moment `null` lands).
   */
  private parseText(text: string): ParseResult<Id | null> {
    // (1) The pin: picker-authored canonical text with a known value —
    // identity by construction, a lossy re-parse can never corrupt the model.
    const pin = this.displayOverride;
    if (pin !== null && pin.text === text) {
      this.selfWrite = { value: pin.value };
      return { value: pin.value };
    }
    // (2) Pristine text: exactly the committed value's display text (the
    // empty string over a null model included) — identity to the CURRENT
    // model; clears any stale unresolved error.
    const current = untracked(this.value);
    if (text === untracked(() => this.displayFor(current))) {
      this.selfWrite = { value: current };
      return { value: current };
    }
    // (3) A recorded resolution failure for exactly this text: NOW the null
    // write happens, with the specific localized message (§5.2).
    const failure = untracked(this.resolutionFailure);
    if (failure !== null && failure.text === text) {
      this.selfWrite = { value: null };
      return {
        value: null,
        error: {
          kind: 'parse',
          message: untracked(this.failureMessage(failure)),
        } as ValidationError.WithoutFieldTree,
      };
    }
    // (4) Anything else is an unresolved query: model UNTOUCHED (omitted
    // value), error carried so a racing submit is blocked from the first
    // divergent keystroke (§5.1/§5.2). The display policy keeps it
    // invisible until touched/dirty.
    return {
      error: {
        kind: 'parse',
        message: untracked(this.translate('entityPicker.errors.unresolved')),
      } as ValidationError.WithoutFieldTree,
    };
  }

  /** The localized message for a recorded resolution failure. */
  private failureMessage(failure: ResolutionFailure): Signal<string> {
    switch (failure.kind) {
      case 'noMatch':
        return this.translate('entityPicker.errors.noMatch', { text: failure.text });
      case 'ambiguous':
        return this.translate('entityPicker.errors.ambiguous', { text: failure.text });
      case 'searchFailed':
        return this.translate('entityPicker.errors.searchFailed');
    }
  }

  /**
   * Writes picker-authored display text whose VALUE is already known —
   * picks, empty commits, locale reformat. The pin makes the accompanying
   * parse an identity by construction.
   */
  private setCanonicalText(text: string, value: Id | null): void {
    this.displayOverride = { text, value };
    this.rawText.set(text);
    this.comboText.set(text);
  }

  /**
   * Records a resolution failure: the model goes `null` THROUGH the parse
   * (a direct model write would reset the raw text and wipe the error),
   * the text is kept for correction, and the specific localized message
   * goes live.
   */
  private failResolution(kind: ResolutionFailure['kind'], text: string): void {
    this.resolutionFailure.set({ text, kind });
    this.rawText.set(text);
    this.resolving.set(false);
  }

  /**
   * Applies a pick from any source: memoizes the label, resolves the
   * canonical display text (`displayWith` wins over the pick label), writes
   * the id through the pin, and emits `picked` LAST — a host may treat the
   * pick as a finished edit and tear this control down, so nothing may
   * touch instance state after the emits.
   */
  private applyPick(
    id: Id,
    label: string,
    item: T | undefined,
    source: TmEntityPicked<T, Id>['source'],
    opts?: { readonly suppressCellActivate?: boolean },
  ): void {
    this.memo.set(id, label);
    this.resolutionFailure.set(null);
    this.resolving.set(false);
    const text = untracked(() => this.displayWith()?.(id) ?? null) ?? label;
    this.setCanonicalText(text, id);
    const emitActivate =
      this.cellHost !== null && source !== 'auto' && opts?.suppressCellActivate !== true;
    this.picked.emit({ id, label, item, source });
    if (emitActivate) {
      this.ɵcellActivate.emit();
    }
  }

  // ---- The search lifecycle ----
  /** Whether `text` is a browse intent: empty, or the committed display text. */
  private isBrowseText(text: string): boolean {
    return text === '' || text === untracked(() => this.displayFor(untracked(this.value)));
  }

  /** The query a text maps to: pristine text browses with the empty query. */
  private queryFor(text: string): string {
    return this.isBrowseText(text) ? '' : text;
  }

  /**
   * Queues a search for `query` through the leading+trailing coalescing
   * window. Every call supersedes the outstanding request and clears the
   * stale highlight immediately; the window exists solely to absorb
   * key-autorepeat and paste bursts against async sources.
   */
  private queueSearch(query: string): void {
    ++this.searchEpoch; // a late response of the superseded request must not land
    // A fresh search RESUMES OWNERSHIP from any pending resolution too: the
    // abort below may kill the very request a resolution is awaiting, and
    // without this supersession the abort would surface as a spurious
    // "search failed" that nulls the model (reopen-while-resolving).
    ++this.resolutionEpoch;
    this.resolving.set(false);
    this.inFlight?.controller.abort();
    this.inFlight = null;
    this.clearHighlight();
    const debounceMs = this.searchDebounce();
    if (debounceMs <= 0 || this.lastSearchWasSync) {
      this.executeSearch(query);
      return;
    }
    if (!this.windowOpen) {
      this.executeSearch(query); // leading edge — single keystrokes pay zero latency
      this.windowOpen = true;
      this.debounceTimer = setTimeout(() => {
        this.windowOpen = false;
        this.debounceTimer = undefined;
        const pending = this.pendingQuery;
        this.pendingQuery = undefined;
        if (pending !== undefined) {
          this.queueSearch(pending);
        }
      }, debounceMs);
    } else {
      this.pendingQuery = query;
      // Immediate spinner + immediate stale-row clear even while coalescing:
      // what is on screen always corresponds to the text in the field.
      this.searchState.set({ kind: 'loading', query });
    }
  }

  /** Cancels the debounce window and any coalesced trailing query. */
  private cancelDebounce(): void {
    if (this.debounceTimer !== undefined) {
      clearTimeout(this.debounceTimer);
      this.debounceTimer = undefined;
    }
    this.windowOpen = false;
    this.pendingQuery = undefined;
  }

  /** Issues one search request; sync returns render in the same turn. */
  private executeSearch(query: string): void {
    const token = ++this.searchEpoch;
    const controller = new AbortController();
    let result: TmEntitySearchResult<T> | Promise<TmEntitySearchResult<T>>;
    try {
      result = this.search()(query, controller.signal);
    } catch {
      // A synchronous throw is a search failure (§4.2).
      this.lastSearchWasSync = true;
      this.searchState.set({ kind: 'error', query });
      this.announceKey('entityPicker.announce.searchFailed');
      return;
    }
    if (isThenable(result)) {
      this.lastSearchWasSync = false;
      this.searchState.set({ kind: 'loading', query });
      const settled = this.settle(result);
      this.inFlight = { query, controller, settled };
      void settled.then((outcome) => {
        if (token !== this.searchEpoch) {
          return; // superseded — discard on arrival, whatever the signal did
        }
        this.inFlight = null;
        if (!untracked(this.expanded)) {
          return; // closed meanwhile — a pending resolution consumes `settled` itself
        }
        if (outcome === 'failed') {
          this.searchState.set({ kind: 'error', query });
          this.announceKey('entityPicker.announce.searchFailed');
        } else {
          this.applyResults(query, outcome);
        }
      });
    } else {
      // Synchronous fast path: results render in the same turn — no
      // spinner, no flicker, and the next keystroke skips the window.
      this.lastSearchWasSync = true;
      this.applyResults(query, this.normalizeResult(result));
    }
  }

  /** Wraps a search promise: resolves to the normalized outcome, never rejects. */
  private settle(promise: Promise<TmEntitySearchResult<T>>): Promise<NormalizedResult<T> | 'failed'> {
    return promise.then(
      (result) => this.normalizeResult(result),
      () => 'failed' as const,
    );
  }

  /** Normalizes both result forms to the object form. */
  private normalizeResult(result: TmEntitySearchResult<T>): NormalizedResult<T> {
    if (Array.isArray(result)) {
      return { items: result as readonly T[], hasMore: false };
    }
    // Array.isArray does not narrow the readonly-array member out of the
    // union in the false branch, so the object form is asserted.
    const objectForm = result as { readonly items: readonly T[]; readonly hasMore?: boolean };
    return { items: objectForm.items, hasMore: objectForm.hasMore === true };
  }

  /** Renders a fresh result set and schedules the highlight protocol. */
  private applyResults(query: string, result: NormalizedResult<T>): void {
    const items = result.items;
    if (isDevMode() && items.length > 200) {
      console.warn(
        `tm-entity-picker: the search returned ${items.length} results — the search ` +
          `contract expects the implementation to impose a result limit (the dropdown ` +
          `has no virtual scroll).`,
      );
    }
    if (items.length === 0) {
      this.searchState.set({ kind: 'empty', query });
      this.announceKey('entityPicker.announce.noResults');
    } else {
      this.searchState.set({ kind: 'results', query, items, hasMore: result.hasMore });
      this.announceKey(
        result.hasMore ? 'entityPicker.announce.resultsMore' : 'entityPicker.announce.results',
        { count: items.length },
      );
    }
    this.scheduleHighlight(query, items.length);
  }

  /**
   * Requests the highlight protocol for a fresh result set: a typed
   * (non-pristine) query auto-highlights the FIRST result so type-then-
   * Enter is the zero-arrow happy path; a pristine browse list and a fresh
   * empty set highlight NOTHING (open-then-Tab must never change the
   * selection; footer rows are never the auto-highlight target). Applied by
   * the constructor's after-render effect once the rows exist.
   */
  private scheduleHighlight(query: string, count: number): void {
    this.highlightRequest.set({ query, count, seq: ++this.highlightSeq });
  }

  /** Clears the active option immediately (stale results must not commit). */
  private clearHighlight(): void {
    untracked(() => this.listbox()?._pattern.listBehavior.unfocus());
  }

  /** The dropdown just opened: run the initial (browse or seeded) search. */
  private onPopupOpened(): void {
    if (untracked(this.searchState).kind === 'idle') {
      this.queueSearch(this.queryFor(this.inputElement.value));
    }
  }

  /** The dropdown just closed: cancel the window, abort, reset the state. */
  private onPopupClosed(): void {
    this.cancelDebounce();
    if (!untracked(this.resolving)) {
      // Closing aborts the in-flight search — EXCEPT when a blur
      // resolution owns it as its input (§5.2), where the request is KEPT
      // so a later destroy/supersession can still abort it.
      ++this.searchEpoch;
      this.inFlight?.controller.abort();
      this.inFlight = null;
    }
    this.searchState.set({ kind: 'idle' });
    // A request left over from this open must not re-apply against the
    // NEXT open's fresh listbox (it would auto-highlight whatever renders
    // first — footer rows included — before the browse results arrive).
    this.highlightRequest.set(null);
  }

  // ---- Resolution (§5.2) — form path only, never in a cell ----
  /**
   * Maps the current text to at most one entity, reusing the freshest
   * search: on-screen results for exactly this text (synchronous, no
   * request), else the in-flight request (awaited, NOT aborted), else one
   * fresh request. Exactly one → auto-pick; zero/many/failed → keep the
   * text, write `null`, raise the specific localized error.
   */
  private async resolveText(text: string, keepPopup: boolean): Promise<void> {
    const token = ++this.resolutionEpoch;
    // The resolution owns the outcome now: silence the awaited search's own
    // continuation (it must not double-render what this method consumes)…
    ++this.searchEpoch;
    // …and cancel the coalescing window — a trailing query firing mid-await
    // would supersede this commit and silently drop it (with `resolving`
    // stuck true).
    this.cancelDebounce();
    this.resolving.set(true);
    let outcome: NormalizedResult<T> | 'failed';
    const state = untracked(this.searchState);
    if ((state.kind === 'results' || state.kind === 'empty') && state.query === text) {
      outcome =
        state.kind === 'results'
          ? { items: state.items, hasMore: state.hasMore }
          : { items: [], hasMore: false };
    } else if (this.inFlight !== null && this.inFlight.query === text) {
      outcome = await this.inFlight.settled;
    } else {
      outcome = await this.issueResolutionRequest(text);
    }
    if (token !== this.resolutionEpoch) {
      // Superseded: refocus+edit, a fresh search, external write, destroy,
      // modal launch. A superseder that left the popup showing THIS
      // resolution's loading state (an external write issues no search of
      // its own) must not strand the spinner — nothing else will ever
      // resolve it.
      const current = untracked(this.searchState);
      if (untracked(this.expanded) && current.kind === 'loading' && current.query === text) {
        this.searchState.set({ kind: 'idle' });
        this.inFlight = null;
      }
      return;
    }
    // The awaited request is consumed (its own continuation was superseded
    // by the search-epoch bump above and will not run these repairs).
    this.inFlight = null;
    this.resolutionController = null;
    if (outcome === 'failed') {
      this.failResolution('searchFailed', text);
      if (untracked(this.expanded)) {
        // The Enter path keeps the popup open: the status must not stay
        // frozen on the loading spinner.
        this.searchState.set({ kind: 'error', query: text });
        this.announceKey('entityPicker.announce.searchFailed');
      }
      return;
    }
    if (outcome.items.length === 1) {
      const item = outcome.items[0];
      this.resolving.set(false);
      this.applyPick(
        untracked(this.itemId)(item),
        untracked(this.itemLabel)(item),
        item,
        'auto',
      );
      if (keepPopup) {
        this.expanded.set(false); // the Enter path: a pick closes the popup
      } else {
        // Blur path: announce the auto-resolution outcome (§8).
        this.announceKey('entityPicker.announce.picked', {
          label: untracked(this.itemLabel)(item),
        });
      }
      return;
    }
    this.failResolution(outcome.items.length === 0 ? 'noMatch' : 'ambiguous', text);
    if (untracked(this.expanded)) {
      // The Enter path keeps the popup open as the recovery surface: show
      // the fetched candidates (or the honest empty status) instead of a
      // spinner that nothing will ever resolve.
      this.applyResults(text, outcome);
    }
  }

  /** Issues a fresh request for a resolution with no current search. */
  private issueResolutionRequest(text: string): Promise<NormalizedResult<T> | 'failed'> {
    const controller = new AbortController();
    this.resolutionController = controller;
    try {
      const result = this.search()(text, controller.signal);
      return isThenable(result)
        ? this.settle(result)
        : Promise.resolve(this.normalizeResult(result));
    } catch {
      return Promise.resolve('failed' as const);
    }
  }

  // ---- Event handlers ----
  /** The rendered input element. */
  private get inputElement(): HTMLInputElement {
    return this.textInput().nativeElement;
  }

  /**
   * The capture-phase keyboard layer on the host — the only reliable
   * pre-emption point against aria's own input listeners:
   *
   * - Grid mode, dropdown closed, plain vertical arrows: aria would
   *   unconditionally expand on ArrowDown (no opt-out input exists), but in
   *   a grid plain arrows belong to the grid's commit-and-move model — the
   *   original is stopped and an identical clone is re-dispatched ABOVE the
   *   host so it reaches the grid unconsumed.
   *   TODO: replace this re-dispatch with a plain pass-through once
   *   @angular/aria ships an input to suppress the collapsed
   *   ArrowDown-expands behavior.
   * - Dropdown open, plain Home/End: aria's relay would consume them and
   *   move the highlight; the APG editable-combobox model gives them to the
   *   CARET, so the relay is suppressed and the native caret motion runs.
   */
  private readonly onCaptureKeydown = (event: KeyboardEvent): void => {
    if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) {
      return;
    }
    const isExpanded = untracked(this.expanded);
    if (
      this.cellHost !== null &&
      !isExpanded &&
      (event.key === 'ArrowDown' || event.key === 'ArrowUp')
    ) {
      event.stopPropagation();
      this.hostElement.parentElement?.dispatchEvent(new KeyboardEvent(event.type, event));
      return;
    }
    if (
      this.cellHost !== null &&
      isExpanded &&
      event.key === 'Enter' &&
      untracked(() => this.ariaOptions().find((o) => o.active())) === undefined
    ) {
      // Enter with no highlight in a cell is the GRID's commit — aria would
      // consume it for the (empty) relay, and the grid's dropdown gate
      // stands down only once the dropdown is closed, so close it first and
      // hand the grid a clean clone.
      event.stopPropagation();
      this.expanded.set(false);
      this.hostElement.parentElement?.dispatchEvent(new KeyboardEvent(event.type, event));
      return;
    }
    if (isExpanded && (event.key === 'Home' || event.key === 'End')) {
      event.stopPropagation();
    }
  };

  /** Mirrors keystrokes into the raw channel and queues the search. */
  protected onInput(): void {
    const text = this.inputElement.value;
    // Editing resumes ownership from any pending resolution.
    ++this.resolutionEpoch;
    this.resolving.set(false);
    const failure = untracked(this.resolutionFailure);
    if (failure !== null && failure.text !== text) {
      this.resolutionFailure.set(null);
    }
    this.rawText.set(text);
    this.queueSearch(this.queryFor(text));
  }

  /** Pointer open: a click on the input opens the browse list (never toggles). */
  protected onInputClick(): void {
    if (this.disabled() || this.readonly()) {
      return;
    }
    if (!untracked(this.expanded)) {
      this.expanded.set(true);
    }
  }

  /** Focus bookkeeping. */
  protected onFocusin(): void {
    this.focused.set(true);
  }

  /**
   * Real-departure detection: presses inside the popup or on the magnifier
   * keep focus in the input (they preventDefault their pointerdown) and are
   * their own interactions; a focusout whose target is still inside the
   * picker or its panel is not a departure. A real departure reports touch
   * and — standalone only — triggers empty-commit / re-canonicalization /
   * text resolution.
   */
  protected onFocusout(event: FocusEvent): void {
    const next = event.relatedTarget as Element | null;
    if (
      next !== null &&
      (this.hostElement.contains(next) ||
        (this.panelElement()?.nativeElement.contains(next) ?? false))
    ) {
      return;
    }
    if (this.suppressBlur) {
      return;
    }
    this.focused.set(false);
    this.touchedSelf.set(true);
    this.touch.emit();
    if (this.cellHost !== null) {
      return; // the grid owns commit; the picker's resolution never runs in a cell
    }
    const text = this.inputElement.value;
    if (text === '') {
      // Empty commit: clearing the text clears the value (required is the
      // field's concern). Trivial for an already-null model.
      this.setCanonicalText('', null);
      return;
    }
    if (text === untracked(() => this.displayFor(untracked(this.value)))) {
      // Pristine: re-canonicalize (clears any transient unresolved error
      // from re-typed pristine text); no request.
      this.setCanonicalText(text, untracked(this.value));
      return;
    }
    void this.resolveText(text, false);
  }

  /**
   * Input keyboard: `Alt+ArrowDown` opens the dropdown (both modes); plain
   * `ArrowUp` opens it standalone (plain `ArrowDown` is aria's own
   * collapsed binding); Tab commits a highlighted entity row without being
   * consumed. Esc and Enter-while-open are aria's.
   */
  protected onInputKeydown(event: KeyboardEvent): void {
    if (this.disabled() || this.readonly()) {
      return;
    }
    if (event.key === 'ArrowDown' && event.altKey && !event.ctrlKey && !event.metaKey) {
      event.preventDefault();
      this.openDropdown();
      return;
    }
    if (
      event.key === 'ArrowUp' &&
      !event.altKey &&
      !event.ctrlKey &&
      !event.metaKey &&
      this.cellHost === null &&
      !untracked(this.expanded) &&
      !event.defaultPrevented
    ) {
      event.preventDefault();
      this.openDropdown();
      return;
    }
    if (event.key === 'Tab') {
      this.handleTab();
    }
  }

  /**
   * Tab: a highlighted ENTITY row commits (the key is NOT consumed — focus
   * proceeds, or the grid performs its commit-and-move); a highlighted
   * footer row or no highlight just closes (blur resolution covers typed
   * text; Tab never launches a modal).
   */
  private handleTab(): void {
    if (!untracked(this.expanded)) {
      return;
    }
    const active = untracked(() => this.ariaOptions().find((o) => o.active()));
    if (active !== undefined) {
      const key = active.value();
      if (typeof key !== 'symbol') {
        const item = untracked(this.resultItems).find((i) => untracked(this.itemId)(i) === key);
        if (item !== undefined) {
          this.applyPick(key as Id, untracked(this.itemLabel)(item), item, 'list', {
            suppressCellActivate: true,
          });
        }
      }
    }
    this.expanded.set(false);
  }

  /** Commits when an option row is clicked; other panel clicks do nothing. */
  protected onListboxClick(event: MouseEvent): void {
    const row = (event.target as Element).closest('[ngOption]');
    if (row !== null && row.getAttribute('aria-disabled') !== 'true') {
      this.activateActive();
    }
  }

  /** The relayed Enter on the listbox — activation plus the no-highlight fallbacks. */
  protected onListboxEnter(): void {
    this.activateActive({ enterFallbacks: true });
  }

  /**
   * One activation path for click and Enter (never `valueChange` — the
   * aria-in-overlay mouse-bug posture): an active footer row opens its
   * modal; an active entity row commits; Enter with no highlight falls back
   * to resolution (loading), fail-fast (fresh empty typed set), or a plain
   * close (pristine).
   */
  private activateActive(opts?: { readonly enterFallbacks?: boolean }): void {
    const active = untracked(() => this.ariaOptions().find((o) => o.active()));
    if (active !== undefined) {
      const key = active.value();
      if (key === ACTION_ADVANCED) {
        const page = untracked(this.advancedSearch);
        if (page !== undefined) {
          void this.launchPage(page, 'advanced');
        }
        return;
      }
      if (key === ACTION_CREATE) {
        const page = untracked(this.create);
        if (page !== undefined) {
          void this.launchPage(page, 'create');
        }
        return;
      }
      if (key === ACTION_EDIT) {
        const page = untracked(this.edit);
        if (page !== undefined) {
          void this.launchPage(page, 'edit');
        }
        return;
      }
      const item = untracked(this.resultItems).find(
        (i) => untracked(this.itemId)(i) === key,
      );
      if (item !== undefined) {
        this.focus();
        this.applyPick(key as Id, untracked(this.itemLabel)(item), item, 'list');
        this.expanded.set(false);
      }
      return;
    }
    if (opts?.enterFallbacks !== true || this.cellHost !== null) {
      return;
    }
    const text = this.inputElement.value;
    const state = untracked(this.searchState);
    if (text === '' && untracked(this.value) !== null) {
      // Enter with cleared text is a commit gesture: clearing the text
      // clears the value, exactly like the blur empty commit.
      this.touchedSelf.set(true);
      this.touch.emit();
      this.setCanonicalText('', null);
      this.expanded.set(false);
      return;
    }
    if (!this.isBrowseText(text) && (state.kind === 'loading' || state.kind === 'error')) {
      // Enter while results are loading (or after a failure — retry):
      // request resolution with focus retained; touch is reported so a
      // failure displays without waiting for blur.
      this.touchedSelf.set(true);
      this.touch.emit();
      void this.resolveText(text, true);
      return;
    }
    if (!this.isBrowseText(text) && state.kind === 'empty' && state.query === text) {
      // Enter on a fresh empty set for a typed query: fail fast to the
      // no-match error; the popup stays open with its footer rows as the
      // recovery path.
      this.touchedSelf.set(true);
      this.touch.emit();
      this.failResolution('noMatch', text);
      return;
    }
    // Pristine text, no highlight: Enter simply closes the popup.
    this.expanded.set(false);
  }

  /**
   * Panel presses on rows keep focus in the input — a press inside the
   * popup is its own interaction (a pick, a modal launch), never a blur
   * departure. Presses on the panel/scrollbar itself fall through to the
   * listbox's tabindex="-1", which aria's widget-focus tracking absorbs.
   */
  protected onPanelPointerdown(event: PointerEvent): void {
    const target = event.target as Element;
    if (
      target.closest(
        '[ngOption], .tm-entity-picker__hint, .tm-entity-picker__status, .tm-entity-picker__separator',
      ) !== null
    ) {
      event.preventDefault();
    }
  }

  /** An outside pointer press closes the dropdown (the text is kept). */
  protected onOutsideClick(): void {
    this.expanded.set(false);
  }

  /** The magnifier (pointer/AT path; never a tab stop) opens advanced search. */
  protected onMagnifierClick(): void {
    if (this.disabled() || this.readonly()) {
      return;
    }
    const page = untracked(this.advancedSearch);
    if (page !== undefined) {
      void this.launchPage(page, 'advanced');
    }
  }

  // ---- The modal pages (§6) ----
  /**
   * Launches a consumer page in a modal: advanced search defaults to size
   * `lg`, create and edit to `md`; the title comes from the page config,
   * else the localized default. The dropdown closes and the in-flight
   * search is cancelled; the input's text survives for the page's `query`
   * payload and for the user's return.
   */
  private async launchPage(
    page: TmEntityPickerPage,
    source: 'advanced' | 'create' | 'edit',
  ): Promise<void> {
    const config: { component: Type<unknown>; size?: TmModalSize; title?: string } =
      typeof page === 'function' ? { component: page } : page;
    const text = this.inputElement.value;
    const data: TmEntityPickerPageData<Id> = {
      // Pristine text is a browse intent, not a query — a create page must
      // not prefill the OLD entity's label as the new entity's name.
      query: this.isBrowseText(text) ? '' : text,
      ...(source === 'edit' ? { id: untracked(this.value) ?? undefined } : {}),
    };
    // Supersede the search AND any pending resolution; the modal outcome
    // governs from here.
    ++this.searchEpoch;
    ++this.resolutionEpoch;
    this.inFlight?.controller.abort();
    this.inFlight = null;
    this.cancelDebounce();
    this.resolving.set(false);
    this.suppressBlur = true; // focus moves into the dialog — not a departure
    this.expanded.set(false);
    const titleKey =
      source === 'advanced'
        ? 'entityPicker.advancedTitle'
        : source === 'create'
          ? 'entityPicker.createTitle'
          : 'entityPicker.editTitle';
    const size: TmModalSize = config.size ?? (source === 'advanced' ? 'lg' : 'md');
    const ref = this.modal.open<TmEntityPick<Id, T> | null>(config.component, {
      size,
      title: config.title ?? untracked(this.translate(titleKey)),
      data,
    });
    this.openModal = ref;
    const result = await ref.closed;
    this.openModal = null;
    this.suppressBlur = false;
    if (this.destroyed) {
      return;
    }
    if (result.via !== 'api') {
      // Close-button, backdrop, or Esc: a strict no-op — text, value, and
      // error state stay exactly as they were; the modal's focus restore
      // returns focus to the picker.
      return;
    }
    if (result.value === undefined) {
      // A bare close() — the page chose not to report anything: a no-op.
      return;
    }
    const pick = result.value;
    if (pick === null) {
      if (source === 'edit') {
        // The entity no longer exists (deleted/deactivated by the page):
        // clear to null with empty text and focus the input.
        this.setCanonicalText('', null);
        this.focus();
      }
      return;
    }
    this.applyPick(pick.id, pick.label, pick.item, source);
    this.focus();
  }

  // ---- Announcements ----
  /** Announces a localized message through the polite live region. */
  private announceKey(key: string, params?: Record<string, unknown>): void {
    if (!untracked(this.expanded) && !key.endsWith('.picked')) {
      return; // count/failure announcements belong to the open popup only
    }
    const message = untracked(this.translate(key, params));
    if (untracked(this.liveText) === message) {
      // Re-announce an identical message: clear first, restore a beat later
      // (an unchanged live region announces nothing).
      this.liveText.set('');
      setTimeout(() => {
        if (!this.destroyed) {
          this.liveText.set(message);
        }
      });
      return;
    }
    this.liveText.set(message);
  }

  // ---- TmFormFieldControl plumbing ----
  /** Receives the field's hint/error ids and exposes them via aria-describedby. */
  setDescribedByIds(ids: readonly string[]): void {
    this.fieldDescribedBy.set(ids);
  }

  /** Focuses the input when the user clicks the field's container chrome. */
  onContainerClick(): void {
    this.focus();
  }

  /** Focuses the input; Signal Forms calls this when asked to focus the field. */
  focus(options?: FocusOptions): void {
    this.inputElement.focus(options);
  }

  // ---- TmCellEditor<Id | null> ----
  /**
   * The committed-text view the grid commits by: `null` while the VALUE
   * channel is authoritative (the text is picker-authored canonical text or
   * the pristine display of the set value — the grid then commits the id),
   * else the raw unresolved text (the grid resolves it through the column's
   * label-resolution pipeline).
   */
  readonly text: SignalLike<string | null> = () => {
    const raw = untracked(this.rawText);
    const pin = this.displayOverride;
    if (pin !== null && pin.text === raw) {
      return null;
    }
    const current = untracked(this.value);
    if (raw === untracked(() => this.displayFor(current))) {
      return null;
    }
    return raw;
  };

  /**
   * Accepts the edit: in a cell, a fresh unique on-screen result for the
   * current unresolved text auto-picks synchronously first (no request —
   * anything else is the grid's resolver's job), then the value baseline
   * moves and the dropdown closes.
   */
  commit(): void {
    if (this.cellHost !== null) {
      const raw = untracked(this.rawText);
      if (raw !== '' && this.text() !== null) {
        const state = untracked(this.searchState);
        if (state.kind === 'results' && state.query === raw && state.items.length === 1) {
          const item = state.items[0];
          this.applyPick(
            untracked(this.itemId)(item),
            untracked(this.itemLabel)(item),
            item,
            'auto',
          );
        }
      }
    }
    this.lastCommitted = untracked(this.value);
    this.expanded.set(false);
  }

  /** Reverts to the last committed value (a grid host's second Esc). */
  cancel(): void {
    this.resolutionFailure.set(null);
    // Restore the display text explicitly (through the pin): when the value
    // itself never changed, the model write alone would not reset the raw
    // text the session's typing left behind.
    this.setCanonicalText(
      untracked(() => this.displayFor(this.lastCommitted)),
      this.lastCommitted,
    );
    this.expanded.set(false);
  }

  /**
   * Type-to-edit seed: replaces the content with `text` (caret at the end),
   * opens the dropdown, and searches the seed immediately.
   */
  seed(text: string): void {
    if (this.disabled() || this.readonly()) {
      return;
    }
    this.displayOverride = null;
    this.resolutionFailure.set(null);
    this.rawText.set(text);
    this.comboText.set(text);
    const element = this.inputElement;
    element.value = text;
    element.setSelectionRange(text.length, text.length);
    this.expanded.set(true);
    this.queueSearch(this.queryFor(text));
  }

  /**
   * Quiet text install for a grid host: replaces the content (caret at the
   * end) WITHOUT searching and WITHOUT opening the dropdown — edit-mode
   * opens show the display text (or an errored cell's raw text) with the
   * dropdown closed.
   * @internal
   */
  ɵsetCellText(text: string): void {
    this.displayOverride = null;
    this.rawText.set(text);
    this.comboText.set(text);
    const element = this.inputElement;
    element.value = text;
    element.setSelectionRange(text.length, text.length);
  }

  /** Whether the dropdown is open — a grid host's dropdown gate. */
  isDropdownOpen(): boolean {
    return untracked(this.expanded);
  }

  /** Opens the dropdown (the grid's `Alt+ArrowDown` path calls this too). */
  openDropdown(): void {
    if (this.disabled() || this.readonly()) {
      return;
    }
    this.focus();
    this.expanded.set(true);
  }
}

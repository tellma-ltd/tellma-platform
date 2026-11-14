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
  model,
  output,
  PendingTasks,
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
import { Check, CircleAlert, Search } from 'lucide';

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
import { TmIcon } from '@tellma/core-ui/icon';
import { TmModal, type TmModalRef, type TmModalSize } from '@tellma/core-ui/modal';
import {
  tmCreateAnchoredOverlay,
  tmLogicalPositions,
  type TmOverlaySide,
} from '@tellma/core-ui/overlay';
import { TmSpinner } from '@tellma/core-ui/spinner';

import type {
  ɵTmEntityAdoptedSearch,
  TmEntityId,
  TmEntityMultiPick,
  TmEntityPick,
  TmEntityPicked,
  TmEntityPickerPage,
  TmEntityPickerPageData,
  TmEntitySearchFn,
  TmEntitySearchResult,
} from './tm-entity-picker-types';
import { ɵtmNormalizeEntityResult } from './tm-entity-search';

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

/**
 * Marks a validation error as a state the user is passing THROUGH rather
 * than a mistake they made: it counts for validity (the form cannot be
 * saved on it) but is never rendered as an inline message. Carried on the
 * error object so it survives the round trip through a bound field's error
 * list, which spreads the original error.
 */
const TRANSIENT = 'ɵtmTransient';

/** Whether a validation error is a pass-through state, not a mistake. */
function isTransient(error: ValidationError.WithOptionalFieldTree): boolean {
  return (error as unknown as Record<string, unknown>)[TRANSIENT] === true;
}

/**
 * What a launched page can close with: a single pick, a whole selection (a
 * page opened from a multi-picker), `null` (an edit page's "the entity is
 * gone"), or nothing at all.
 */
type PageOutcome<Id extends TmEntityId, T> = TmEntityPick<Id, T> | TmEntityMultiPick<Id, T> | null;

/** Whether a page outcome is a whole selection rather than a single pick. */
function isMultiPick<Id extends TmEntityId, T>(
  outcome: TmEntityPick<Id, T> | TmEntityMultiPick<Id, T>,
): outcome is TmEntityMultiPick<Id, T> {
  return Array.isArray((outcome as { readonly ids?: unknown }).ids);
}

/** The gap the panel keeps from the viewport edge when its height is capped. */
const VIEWPORT_MARGIN = 8;
/** Panel chrome outside the scrolling list (border + listbox padding). */
const PANEL_CHROME = 10;
/** Panel cap assumed when the token stylesheet is absent (unit environments). */
const FALLBACK_PANEL_MAX_HEIGHT = 280;
/** The clamp never goes below this — a panel too short to read helps nobody. */
const MIN_PANEL_HEIGHT = 96;

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
 * Result rows can carry a second line (`itemDescription`) that tells
 * same-named entities apart, and a label drawn in parts (`itemLabelParts`)
 * that keeps a code beside a name in another script in order. Every line,
 * and every part, is its own bidirectional run.
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
  imports: [
    Combobox,
    ComboboxPopup,
    ComboboxWidget,
    Listbox,
    Option,
    OverlayModule,
    TmIcon,
    TmSpinner,
  ],
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
      [class.tm-input--in-field]="!!formField || emits()"
      [expanded]="expanded()"
      (expandedChange)="onExpandedChange($event)"
      [(value)]="comboText"
      [disabled]="disabled()"
      [softDisabled]="readonly() && !disabled()"
      [readOnly]="readonly()"
      [required]="required() && !emits()"
      [attr.aria-required]="required() && emits() ? 'true' : null"
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
    <!-- Drawn HERE, not by the enclosing field: appended after this whole
         control it would sit past the button below and shove it sideways
         every time an error came and went. -->
    @if (showsInvalid()) {
      <tm-icon class="tm-form-field__error-icon" [svg]="glyphs.error" />
    }
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
        <tm-icon [svg]="glyphs.search" />
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
          (focusin)="onPanelFocusin()"
        >
          <ul
            ngListbox
            ngComboboxWidget
            #lb="ngListbox"
            class="tm-entity-picker__listbox"
            [style.max-block-size]="listboxMaxHeight()"
            [tabindex]="-1"
            focusMode="activedescendant"
            selectionMode="explicit"
            [multi]="emits()"
            [(value)]="listboxValue"
            [activeDescendant]="lb.activeDescendant()"
            (click)="onListboxClick($event)"
          >
            @for (item of resultItems(); track idOf(item)) {
              <!-- An entity row: the label line, then the description line (see descriptionOf). -->
              @let description = descriptionOf(item);
              <li
                ngOption
                class="tm-entity-picker__option"
                [class.tm-entity-picker__option--two-line]="twoLine()"
                [value]="idOf(item)"
                [label]="labelOf(item)"
              >
                <span class="tm-entity-picker__option-label">
                  @if (labelPartsOf(item); as parts) {
                    @for (part of parts; track $index) {
                      @if (!$first) { - }
                      <bdi>{{ part }}</bdi>
                    }
                  } @else {
                    <bdi>{{ labelOf(item) }}</bdi>
                  }
                  @if (description) {
                    <span class="tm-entity-picker__option-separator">, </span>
                  }
                </span>
                @if (twoLine()) {
                  <span class="tm-entity-picker__option-description"
                    ><bdi>{{ description }}</bdi></span
                  >
                }
                <tm-icon class="tm-entity-picker__check" [svg]="glyphs.check" />
              </li>
            }
            @if (showsHasMore()) {
              <li class="tm-entity-picker__hint" aria-hidden="true">{{ moreResultsText() }}</li>
            }
            @if (noticeText(); as notice) {
              <li class="tm-entity-picker__hint tm-entity-picker__notice" aria-hidden="true">
                {{ notice }}
              </li>
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
            @if (footerRows().length > 0) {
              <li class="tm-entity-picker__separator" aria-hidden="true"></li>
              @for (row of footerRows(); track row.key) {
                <li
                  ngOption
                  class="tm-entity-picker__option tm-entity-picker__action"
                  [value]="row.key"
                  [label]="row.label"
                >
                  <span class="tm-entity-picker__option-label">{{ row.label }}</span>
                </li>
              }
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
    '[class.tm-entity-picker--invalid]': 'showsInvalid()',
  },
})
export class TmEntityPicker<T, Id extends TmEntityId = TmEntityId>
  implements TmFormFieldControl, TmCellEditor<Id | null>
{
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly errorDisplay = inject(TM_ERROR_DISPLAY);
  private readonly modal = inject(TmModal);
  private readonly destroyRef = inject(DestroyRef);
  /** Holds the app unstable while a repeated announcement waits to be restored. */
  private readonly pendingTasks = inject(PendingTasks);
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
  /** Dirty state for non-form usage — the bound field is authoritative when bound via [formField]. */
  readonly dirty = input(false, { transform: booleanAttribute });
  // Three state inputs live under an alias for one reason: Signal Forms binds
  // field state by PUBLIC input name, while the form-field contract reads
  // same-named MEMBERS — and for these three the picker has state of its own
  // to merge in (an unresolved query is invalid, a real departure is a touch,
  // a running resolution is pending) that a bound field cannot know about.
  // Without the merge, a picker used WITHOUT [formField] would keep
  // unresolvable text with no visible error at all.
  /**
   * The bound field's validity state. Aliased to the public name `invalid`;
   * the `invalid` member ORs it with the picker's own unresolved-text state.
   */
  // eslint-disable-next-line @angular-eslint/no-input-rename -- see above
  readonly fieldInvalid = input(false, { alias: 'invalid', transform: booleanAttribute });
  /**
   * The bound field's touched state. Aliased to the public name `touched`;
   * the `touched` member ORs it with the picker's own departure tracking.
   */
  // eslint-disable-next-line @angular-eslint/no-input-rename -- see above
  readonly fieldTouched = input(false, { alias: 'touched', transform: booleanAttribute });
  /**
   * The bound field's async-validation-pending state. Aliased to the public
   * name `pending`; the `pending` member ORs it with the picker's own text
   * resolution.
   */
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
   * Maps a search result to a second line under its label — a code and a
   * parent, say, so two entities with the same name can be told apart.
   * Bound, every result row shows two lines at one fixed height (a row whose
   * description is `null` included), the description in smaller secondary
   * ink, and each row's accessible name becomes "label, description".
   * Unbound, rows are one line. Reactive, like `itemLabel`.
   */
  readonly itemDescription = input<((item: T) => string | null) | undefined>(undefined);
  /**
   * Maps a search result to the parts of its label — a code and a name,
   * say — which the row draws as separately isolated runs joined by " - ",
   * so a code in one script keeps its place beside a name in another in
   * either page direction. Joined by " - ", the parts must equal
   * `itemLabel`, which stays the row's accessible name and the text a pick
   * commits; parts that do not join to it are ignored (with a development
   * warning) and the row draws `itemLabel` as one run, as it does for `null`
   * or no parts. Reactive, like `itemLabel`.
   */
  readonly itemLabelParts = input<((item: T) => readonly string[] | null) | undefined>(undefined);
  /**
   * Resolves a committed id to display text (reactive, like `itemLabel`);
   * `null` — or the empty string, which is not a label — defers to the
   * pick-time label memo, then to `String(id)` with a dev-mode warning.
   */
  readonly displayWith = input<((id: Id) => string | null) | undefined>(undefined);
  /** The advanced-search page; absent ⇒ no magnifier and no footer row. */
  readonly advancedSearch = input<TmEntityPickerPage | undefined>(undefined);
  /** The create page; absent ⇒ no Create… footer row. */
  readonly create = input<TmEntityPickerPage | undefined>(undefined);
  /**
   * The edit page; absent ⇒ no Edit… footer row. The row shows only while
   * the field NAMES an entity — a committed value AND the text that goes
   * with it, so a box the user has cleared stops offering to edit what it
   * has stopped showing. It opens the page on that value and honors the
   * page's `close(null)` by clearing the field (the entity no longer
   * exists).
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

  // ---- The emit-mode seam (a host that keeps the selection itself) ----
  /**
   * What a commit does: `'value'` (the default) writes the picked id into
   * `value`; `'emit'` leaves `value` at `null`, emits `picked`, and returns
   * the input to empty and pristine for the next pick. Search, debounce,
   * abort, resolution and the footer rows are the same in both modes.
   * @internal
   */
  readonly ɵcommitMode = input<'value' | 'emit'>('value');
  /**
   * Emit mode: the ids the host already holds. Their rows show the check and
   * `aria-selected`, and an explicit list pick on one (a pointer press, or
   * Enter on its highlighted row) emits `ɵdeselect` instead of `picked`.
   * @internal
   */
  readonly ɵchosenIds = input<readonly Id[]>([]);
  /**
   * Emit mode: the most entities the host may hold, handed to the
   * advanced-search page with the chosen ids.
   * @internal
   */
  readonly ɵmaxItems = input<number | undefined>(undefined);
  /**
   * A status line the panel shows under the result rows (a host's reached
   * cap), or `null` for none. Presentational: the host announces it.
   * @internal
   */
  readonly ɵnotice = input<string | null>(null);
  /**
   * Emit mode: an explicit list pick landed on a chosen row — the host
   * removes that id.
   * @internal
   */
  readonly ɵdeselect = output<Id>();
  /**
   * Emit mode: the advanced-search page closed with the whole selection it
   * edited — the host replaces its ids with these.
   * @internal
   */
  readonly ɵmultiPick = output<TmEntityMultiPick<Id, T>>();

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
  /** Whether the dropdown is open; kept in step with aria's combobox model (see `setExpanded`). */
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
  /**
   * The last text this control AUTHORED, and the value it authored it for.
   *
   * Deliberately separate from `displayOverride`, which answers a different
   * question for a different reader. The pin is consulted by `parseText`, so
   * it must be retired the moment the display context moves — otherwise
   * re-typed old text resurrects a dead value. This record is read ONLY by
   * the departure check, which asks the narrower question "is the text on
   * screen still the text I wrote, for the value the model still holds?" —
   * true regardless of how the display context has moved since, and false
   * the moment the user edits. Nothing can resurrect through it, because
   * the parse never sees it.
   */
  private authoredText: { readonly text: string; readonly value: Id | null } | null = null;
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
  /** One-shot guard for the label-parts-drift dev warning. */
  private warnedLabelPartsDrift = false;
  /** True once destroy ran — guards late async continuations. */
  private destroyed = false;
  /** Cancels a repeated announcement still waiting to be restored, if any. */
  private cancelAnnounceRestore: (() => void) | null = null;
  /** Suppresses blur handling while a picker-launched modal owns focus. */
  private suppressBlur = false;
  /**
   * Whether the user has typed since focus arrived. A visit that typed
   * nothing must not resolve on the way out: the text on screen is the
   * picker's own, and the reformat effect — which stands down while the
   * field is focused — is what owes it a refresh, not a search.
   */
  private editedSinceFocus = false;
  /** The picker-launched modal currently open, if any (destroy closes it). */
  private openModal: TmModalRef<PageOutcome<Id, T>> | null = null;
  /**
   * Whether the next browse result set lands without its count announcement:
   * set by an emit-mode commit that keeps the panel open, whose host
   * announces the pick itself — a count read over it would bury the news.
   */
  private quietBrowse = false;

  // ---- Search lifecycle state ----
  /** The dropdown's search state machine. */
  private readonly searchState = signal<SearchState<T>>({ kind: 'idle' });
  /** The outstanding async request, if any. */
  private inFlight: InFlightSearch<T> | null = null;
  /** The abort controller of a resolution-issued request, if any. */
  private resolutionController: AbortController | null = null;
  /**
   * The most recent SETTLED result set and the query that produced it.
   * Outlives the popup: the search state machine resets when the dropdown
   * closes, but a cell commit still has to be able to say "I already know
   * the answer for this exact text" without paying for it twice.
   */
  private settledResult: {
    readonly query: string;
    readonly items: readonly T[];
    readonly hasMore: boolean;
    /**
     * The `search` function that produced it. A consumer swapping the
     * closure — a different tenant, a different filter — is a different
     * question, and its answer must not be served from the old one.
     */
    readonly source: TmEntitySearchFn<T>;
  } | null = null;
  /** The debounce window timer. */
  private debounceTimer: ReturnType<typeof setTimeout> | undefined;
  /** Whether the debounce window is currently open. */
  private windowOpen = false;
  /** The query coalesced into the trailing edge of the window. */
  private pendingQuery: string | undefined;
  /** Whether the previous search invocation returned synchronously. */
  private lastSearchWasSync = false;
  /**
   * The pending highlight-protocol application: the option a fresh result
   * set or an arrow key has decided to highlight (`key`: an entity id or a
   * footer-row sentinel, or `undefined` for no highlight). A signal (not a
   * one-shot render hook) because the popup mounts across MULTIPLE render
   * passes — the overlay attaches first, aria's deferred content renders
   * the listbox a pass later — and the request must survive until the
   * listbox exists.
   */
  private readonly highlightRequest = signal<{
    readonly key: unknown;
    readonly seq: number;
  } | null>(null);
  /** Monotonic sequence for highlight requests (each one is applied once). */
  private highlightSeq = 0;
  /**
   * The sequence of the request the after-render effect last applied. A
   * request with any other sequence is still PENDING: its rows have not
   * rendered, so aria's active option still describes the previous ones.
   */
  private appliedHighlightSeq = 0;
  /**
   * Whether the popup's latest open or close has not been acted on yet: an
   * open whose initial search has not been issued, or a close whose search
   * state has not been reset. See `onExpandedChange`.
   */
  private popupPending = false;

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
  /** aria's combobox directive on the input, whose `expanded` model mirrors ours. */
  private readonly combobox = viewChild(Combobox);

  /**
   * The shared anchored-overlay wiring. The origin is the CHROME the user
   * reads as the field — the bordered box when wrapped, the grid cell's
   * editor host when mounted in a cell, the bare host standalone.
   */
  protected readonly anchored = tmCreateAnchoredOverlay({
    overlay: () => this.overlay(),
    origin: () => this.originElement(),
    // ONE position, decided per open (see `placement`) — never a flip pair.
    // The panel's content arrives in stages (spinner, then results, then a
    // different result set), and a position list would let the CDK re-pick
    // the side on every one of those measurements: the panel would jump
    // above and below the field while the user types.
    positions: () => [tmLogicalPositions(this.placement().side, 'start')[0]],
    matchWidth: true,
    remeasure: 'macrotask',
  });

  /**
   * Where the panel opens and how tall it may be, decided ONCE per open
   * against the anchor's room in the viewport and the panel's own maximum
   * height — never against the content, which is not there yet at attach
   * time and changes repeatedly afterwards.
   *
   * This is what makes the panel stable: the CDK's own flip logic measures
   * the overlay it is about to attach, which (aria renders the popup one
   * pass later) is empty — so it always says "below fits", paints there,
   * and the post-attach re-measure yanks it up a frame later. Deciding from
   * the anchor instead means the first paint is already right, and content
   * growing afterwards can never re-open the question: the chosen side has
   * room for the panel at its cap, and the height below clamps to the room
   * that is actually there, so the list scrolls instead of overflowing.
   */
  private readonly placement = computed<{ side: TmOverlaySide; maxHeightPx: number }>(() => {
    this.expanded(); // re-measure on every open, and only then
    return untracked(() => this.measurePlacement());
  });

  /** The listbox's fitted max height — the clamp half of `placement`. */
  protected readonly listboxMaxHeight = computed(() => `${this.placement().maxHeightPx}px`);

  /**
   * The chrome the panel anchors to: the field's bordered box, or the grid
   * cell's editor host. Falls back to this bare host standalone.
   */
  private originElement(): HTMLElement {
    // `data-tm-field-box` marks a host control that draws the field's box
    // around this picker itself (a standalone multi-picker).
    return (
      this.hostElement.closest<HTMLElement>(
        '.tm-form-field__box, [data-tm-editor], [data-tm-field-box]',
      ) ?? this.hostElement
    );
  }

  /**
   * Picks the side and the height cap from the anchor's room in the
   * viewport. Below is preferred (it reads with the field) and taken
   * whenever it can hold a full-height panel or simply beats above;
   * otherwise the panel opens upward. The height then clamps to the room
   * actually there, so the chosen side fits BY CONSTRUCTION and a long
   * result set scrolls inside the list rather than running off-screen.
   */
  private measurePlacement(): { side: TmOverlaySide; maxHeightPx: number } {
    const rect = this.originElement().getBoundingClientRect();
    const cap = this.panelMaxHeightToken();
    const below = window.innerHeight - rect.bottom - VIEWPORT_MARGIN - PANEL_CHROME;
    const above = rect.top - VIEWPORT_MARGIN - PANEL_CHROME;
    const side: TmOverlaySide = below >= cap || below >= above ? 'block-end' : 'block-start';
    const room = side === 'block-end' ? below : above;
    return { side, maxHeightPx: Math.max(MIN_PANEL_HEIGHT, Math.min(cap, room)) };
  }

  /** The panel's height cap, read from its component token. */
  private panelMaxHeightToken(): number {
    const raw = getComputedStyle(this.hostElement).getPropertyValue(
      '--entity-picker-panel-max-height',
    );
    const parsed = Number.parseFloat(raw);
    return Number.isFinite(parsed) && parsed > 0 ? parsed : FALLBACK_PANEL_MAX_HEIGHT;
  }

  // ---- Template helpers ----
  /** The built-in glyphs: the invalid mark, the magnifier and the chosen row's check. */
  protected readonly glyphs = { error: CircleAlert, search: Search, check: Check } as const;
  /**
   * Whether result rows draw two lines. Decided by the binding, never by
   * the data: every row of a two-line picker takes the two-line height, so
   * the panel never re-measures as result sets with and without
   * descriptions replace one another.
   */
  protected readonly twoLine = computed(() => this.itemDescription() !== undefined);
  /** Whether commits emit rather than write the value (see `ɵcommitMode`). */
  protected readonly emits = computed(() => this.ɵcommitMode() === 'emit');
  /** The host's status line under the rows (see `ɵnotice`); templates cannot read `ɵ` names. */
  protected readonly noticeText = computed(() => this.ɵnotice());
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
  /**
   * Whether the field currently NAMES an entity — as opposed to merely
   * holding one in the value channel.
   *
   * Emptying the text does not write `null` until the commit gesture, so the
   * model still holds the old id while the box reads empty. Everything that
   * tells the user "this is your selection" — the Edit… command and the
   * check glyph — follows what the field names, because otherwise a cleared
   * field offers to edit an entity it is no longer showing, with a tick
   * beside a row the user just removed.
   */
  protected readonly namesEntity = computed(() => this.value() !== null && this.rawText() !== '');

  /**
   * The command footer rows, in the order they render: each row's option
   * value (a footer-row sentinel) and its caption. The ONE list of them: the
   * template draws its rows from it and the keyboard steps over it (see
   * `optionKeys`), so the two cannot disagree.
   *
   * The rows are configured-and-shown in every popup state EXCEPT loading:
   * while the spinner is up the panel says one thing only ("results are
   * coming"), and the commands — which would arrive, shift, and re-order
   * under the user's cursor a moment later — stay out of it. They return
   * the instant the state settles (results, empty, or failure), so "no
   * results → Create…" is still one arrow away. Edit… shows only while the
   * field names an entity.
   */
  // §4.1 states the loading exclusion as the design; the reasoning above is
  // why. Note the cost it accepts: while a search is loading there is no
  // keyboard path to Advanced search…, the magnifier not being a tab stop.
  // The window closes as soon as the state settles.
  protected readonly footerRows = computed(() => {
    const rows: { readonly key: symbol; readonly label: string }[] = [];
    if (this.statusKind() === 'loading') {
      return rows;
    }
    if (this.advancedSearch() !== undefined) {
      rows.push({ key: ACTION_ADVANCED, label: this.defaultAdvancedLabel() });
    }
    if (this.create() !== undefined) {
      // The consumer's caption wins over the localized default.
      rows.push({ key: ACTION_CREATE, label: this.createLabel() ?? this.defaultCreateLabel() });
    }
    if (this.edit() !== undefined && this.namesEntity()) {
      rows.push({ key: ACTION_EDIT, label: this.editLabel() ?? this.defaultEditLabel() });
    }
    return rows;
  });

  /** The localized magnifier aria-label. */
  protected readonly magnifierLabel = this.translate('entityPicker.magnifier');
  /** The localized "No results" status text. */
  protected readonly noResultsText = this.translate('entityPicker.noResults');
  /** The localized search-failure status text. */
  protected readonly searchFailedText = this.translate('entityPicker.searchFailed');
  /** The localized truncation hint, naming how many results are shown. */
  protected readonly moreResultsText = computed(() =>
    this.translate('entityPicker.moreResults', { count: this.resultItems().length })(),
  );
  /** The localized Advanced search… footer-row caption. */
  private readonly defaultAdvancedLabel = this.translate('entityPicker.advancedSearch');
  /** The localized Create… footer-row caption, when the consumer gives none. */
  private readonly defaultCreateLabel = this.translate('entityPicker.create');
  /** The localized Edit… footer-row caption, when the consumer gives none. */
  private readonly defaultEditLabel = this.translate('entityPicker.edit');

  /** The live-region text — announcements on fetch completion only. */
  protected readonly liveText = signal('');

  // ---- TmFormFieldControl ----
  /** The field renders the bordered box around this bare anatomy. */
  readonly ownsChrome = false;
  /**
   * The picker draws the invalid glyph itself, before its own magnifier
   * button, so the button never shifts when an error appears.
   */
  readonly ownsErrorIcon = true;
  private readonly fieldDescribedBy = signal<readonly string[]>([]);
  /** Every exposed describedby id: author-supplied first, then the field's hint/error ids. */
  readonly describedByIds: Signal<readonly string[]> = computed(() => [
    ...(this.ariaDescribedby()?.split(/\s+/).filter(Boolean) ?? []),
    ...this.fieldDescribedBy(),
  ]);
  // Both error channels drop transient entries before localization: an
  // in-progress query must not put a message under the field on every
  // keystroke. The bound channel is filtered too — the same transient error
  // comes back through `errors` once a field is bound, carrying its marker.
  private readonly fieldErrors: () => readonly TmFieldError[] = tmResolveFieldErrors(
    computed(() => this.errors().filter((error) => !isTransient(error))),
    this.translate,
  );
  private readonly ownErrors: () => readonly TmFieldError[] = tmResolveFieldErrors(
    computed(() => this.rawText.parseErrors().filter((error) => !isTransient(error))),
    this.translate,
  );
  /**
   * Already-localized error messages read by the enclosing field: the bound
   * field's errors PLUS the picker's own text errors (an unresolved query,
   * a failed resolution). Without the second source, a picker used without
   * `[formField]` would keep unresolvable text with nothing to show for it —
   * the field's message comes from here. When a field IS bound the same
   * error arrives through both channels, so identical entries collapse.
   */
  readonly localizedErrors: Signal<readonly TmFieldError[]> = computed(() => {
    const merged = [...this.fieldErrors()];
    for (const own of this.ownErrors()) {
      if (!merged.some((e) => e.kind === own.kind && e.message === own.message)) {
        merged.push(own);
      }
    }
    return merged;
  });
  /**
   * The validity the field reads: the bound field's own state OR the
   * picker's unresolved text (typed text that names no entity is invalid
   * whether or not a form is watching).
   */
  readonly invalid: Signal<boolean> = computed(
    () => this.fieldInvalid() || this.rawText.parseErrors().length > 0,
  );
  /** The touched state the field reads: the bound field's OR a real departure. */
  readonly touched: Signal<boolean> = computed(() => this.fieldTouched() || this.touchedSelf());
  /**
   * The pending state the field reads: the bound field's own pending OR the
   * picker's text resolution — the field shows its trailing spinner and the
   * error-display policy holds errors until the resolution lands.
   */
  readonly pending: Signal<boolean> = computed(() => this.fieldPending() || this.resolving());

  /** The merged aria-describedby attribute value, or null when no ids apply. */
  protected readonly describedByAttr = computed(() => this.describedByIds().join(' ') || null);
  /** Whether the enclosing field displays an error this control must mark (see `setFieldError`). */
  private readonly fieldError = signal(false);
  /**
   * aria-invalid (and the standalone invalid border) follow the display
   * policy AND the presence of something worth showing — a query still
   * being typed is invalid for the form's purposes but must not paint the
   * control red while the user works. The enclosing field's own displayed
   * error counts too: its plain `error` input never reaches this control's
   * bound state, and the field's glyph is suppressed here (ownsErrorIcon),
   * so without it the picker would show no mark for it at all.
   */
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
  private readonly touchedSelf = signal(false);

  /** Receives whether the enclosing field displays an error (its plain `error` input included). */
  setFieldError(showsError: boolean): void {
    this.fieldError.set(showsError);
  }

  constructor() {
    this.cellHost?.register(this);

    // Effect 1: a control that goes disabled/readonly while its dropdown is
    // open must not stay interactive.
    effect(() => {
      if (this.disabled() || this.readonly()) {
        untracked(() => this.setExpanded(false));
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
        // Neither text record may outlive the value they describe: a pin
        // from a previous pick must not claim re-typed old text after an
        // external write, and the authored-text record must not tell the
        // departure check that a value someone else replaced is still the
        // one on screen. A pending resolution must not land over one either.
        // An in-flight SEARCH is deliberately left alone (its own token
        // still stands): the write may be the grid installing the value
        // right before seeding a search, and killing it here would strand
        // the dropdown's spinner.
        this.displayOverride = null;
        this.authoredText = null;
        this.supersedeResolution();
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
      // `focused` is TRACKED: the reformat below stands down for a focused
      // field, and something has to run it once focus leaves — otherwise a
      // display context that changed mid-visit (a warmed entity cache, a
      // rename, a locale switch) leaves the older rendering on screen
      // indefinitely.
      const focused = this.focused();
      const display = id === null ? '' : this.displayFor(id);
      const failure = this.resolutionFailure();
      if (failure !== null) {
        this.failureMessage(failure)();
      } else {
        this.translate('entityPicker.errors.unresolved')();
      }
      untracked(() => {
        // The pin is keyed to the display context that produced it, and it
        // is retired on EVERY path — a stale pin would bind fresh text to a
        // dead context's value. It cannot be kept across the stand-down
        // below on the theory that "the pin's value is still the model's":
        // a fail-fast Enter writes `null` through the parse, whose own
        // self-write marker stops the echo-guard effect from retiring it,
        // so the pin outlives the value it names. Re-typing its text would
        // then resurrect that value with no search and no `picked`. What
        // keeps a PICK safe across this retirement is the separate
        // `authoredText` record, which the parse never reads.
        this.displayOverride = null;
        if (focused || resolving) {
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
    // (in emit mode, the host's chosen ids) into aria's listbox, re-applied
    // whenever the option set changes. Defeats aria's unmatched-value
    // auto-prune AND corrects any selection the bare listbox toggles on its
    // own (a press on a footer row, or on a row the host then refuses)
    // before paint; commits ride activation only (see the mouse-bug guard
    // around angular/components#32504).
    effect(() => {
      this.resultItems(); // re-apply on option turnover
      this.footerRows(); // …including footer-row turnover
      const current = this.listboxValue();
      const desired = this.listboxSelection();
      if (current.length !== desired.length || current.some((key, i) => key !== desired[i])) {
        this.listboxValue.set([...desired]);
      }
    });

    // Effect 6: dropdown open/close transitions. Every transition is
    // recorded the moment it happens (see onExpandedChange); this effect
    // acts on the latest one once the task that made it is over. It reads
    // the record rather than comparing against what it saw last time: input
    // delivered faster than the page renders can open the popup (a
    // keystroke) and close it again (a pick) between two runs, and the
    // search state that keystroke built must still be reset, or the next
    // open would re-apply the stale rows and their highlight. A key that
    // needs the open's search sooner runs the same step itself.
    effect(() => {
      this.expanded();
      untracked(() => this.syncPopup());
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
    // disarmed (see the hasBeenInteracted effect) and the arrow keys issue
    // requests of their own (see onCaptureKeydown), so this effect is the
    // only writer that SETS a highlight, besides aria's pointer press.
    afterRenderEffect({
      write: () => {
        const request = this.highlightRequest();
        const listbox = this.listbox();
        if (request === null || listbox === undefined || !untracked(this.expanded)) {
          return;
        }
        // Untracked: the option lookup and goto() READ aria's item/active
        // signals — tracked, this effect would re-run whenever the highlight
        // or the rows changed and re-apply a request that is long spent.
        untracked(() => {
          const option = this.ariaOptions().find((o) => o.value() === request.key);
          if (option === undefined) {
            listbox._pattern.listBehavior.unfocus();
          } else {
            listbox._pattern.listBehavior.goto(option._pattern);
            listbox.scrollActiveItemIntoView();
          }
        });
        // From here on aria's active option is the truth (see keyboardActiveKey).
        this.appliedHighlightSeq = request.seq;
      },
    });

    // Keep the highlighted row on screen. DOM focus never leaves the input
    // (the activedescendant model), so nothing scrolls the list on its own:
    // arrowing past the fold would walk the highlight out of sight and
    // leave the user scrolling by hand to see what they are selecting.
    // The active descendant is read TRACKED — that is the whole point, and
    // it is why this cannot live in the highlight effect above, whose body
    // must stay untracked to avoid re-clamping the highlight it just set.
    afterRenderEffect({
      write: () => {
        const listbox = this.listbox();
        if (listbox === undefined || listbox.activeDescendant() === undefined) {
          return;
        }
        untracked(() => listbox.scrollActiveItemIntoView());
      },
    });

    // Two attributes ngCombobox host-binds wrongly for this control. Its
    // host bindings run AFTER every template binding, so a template
    // override loses; the correction has to come after the render.
    //
    // - `aria-autocomplete`: aria derives it from the LIVE popup, so a
    //   closed picker would announce a combobox with no autocomplete at all
    //   — the opposite of what typing here does.
    // - `readonly`: aria owns that attribute too, and clears it whenever
    //   its own `disabled` input is false. This control deliberately does
    //   NOT route readonly through that input (it would announce the value
    //   as unavailable), so the native read-only state is ours to assert.
    //
    // Tracked reads = every transition that can make aria rewrite either
    // one: the popup registering and unregistering, and the state inputs.
    afterRenderEffect({
      write: () => {
        this.expanded();
        this.listbox();
        const readonly = this.readonly();
        this.disabled();
        untracked(() => {
          const element = this.inputElement;
          element.setAttribute('aria-autocomplete', 'list');
          if (element.readOnly !== readonly) {
            element.readOnly = readonly;
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
      this.cancelAnnounceRestore?.();
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
      // The empty string is not a label. Taking it as one would blank a
      // field that holds a value — and empty text is this control's own
      // clear-the-value gesture, so the next departure would commit the
      // clear. It defers to the memo exactly as `null` does.
      if (text !== null && text !== '') {
        return text;
      }
    }
    const memoized = this.memo.get(id);
    if (memoized !== undefined) {
      return memoized;
    }
    if (typeof ngDevMode !== 'undefined' && ngDevMode && !this.warnedMissingDisplay) {
      this.warnedMissingDisplay = true;
      console.warn(
        `tm-entity-picker: no display text for committed id "${String(id)}" — ` +
          `provide [displayWith] so committed values render their labels ` +
          `(falling back to String(id)).`,
      );
    }
    return String(id);
  }

  /**
   * The keys aria's listbox should hold selected: the host's chosen ids in
   * emit mode; otherwise the committed id while the field names it (a
   * cleared field ticks nothing — the model still holds the old id until
   * the commit gesture, but the box no longer names it).
   */
  private listboxSelection(): readonly unknown[] {
    if (this.emits()) {
      return this.ɵchosenIds();
    }
    const id = this.value();
    return id === null || !this.namesEntity() ? [] : [id];
  }

  /** The entity id of a search result (template + track helper). */
  protected idOf(item: T): Id {
    return this.itemId()(item);
  }

  /** The display label of a search result — reactive via the template. */
  protected labelOf(item: T): string {
    return this.itemLabel()(item);
  }

  // The row's anatomy is explained here rather than in the template: template
  // text, comments included, ships in the bundle.
  /**
   * A search result's second line, or `null` when it has none (or when no
   * `itemDescription` is bound) — reactive via the template.
   *
   * An entity row draws its label line (one isolated run, or one per label
   * part joined by " - "), then — with a description bound — the
   * description line, every row at the one two-line height. A visually
   * hidden ", " closes the label line when there is a description, which
   * gives the row the accessible name "label, description".
   */
  protected descriptionOf(item: T): string | null {
    return this.itemDescription()?.(item) ?? null;
  }

  /**
   * A search result's label parts, or `null` to draw the label as one run
   * (no `itemLabelParts` bound, it gave no parts for this result, or its
   * parts joined by " - " are not the result's `itemLabel`).
   */
  protected labelPartsOf(item: T): readonly string[] | null {
    const parts = this.itemLabelParts()?.(item) ?? null;
    if (parts === null || parts.length === 0) {
      return null;
    }
    // The parts stand in for the label only where they ARE the label: the
    // row's accessible name comes from its content, and a pick commits
    // itemLabel, so parts that drift from it (a stale code, another
    // separator) would show and name one thing and commit another.
    const label = this.labelOf(item);
    if (parts.join(' - ') !== label) {
      if (typeof ngDevMode !== 'undefined' && ngDevMode && !this.warnedLabelPartsDrift) {
        this.warnedLabelPartsDrift = true;
        console.warn(
          `tm-entity-picker: itemLabelParts do not join (" - ") to itemLabel "${label}"; ` +
            `drawing itemLabel.`,
        );
      }
      return null;
    }
    return parts;
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
      this.markSelfWrite(pin.value);
      return { value: pin.value };
    }
    // (2) Pristine text: exactly the committed value's display text (the
    // empty string over a null model included) — identity to the CURRENT
    // model; clears any stale unresolved error.
    const current = untracked(this.value);
    if (text === untracked(() => this.displayFor(current))) {
      return { value: current }; // identity — nothing to mark, nothing will echo
    }
    // (3) A recorded resolution failure for exactly this text: NOW the null
    // write happens, with the specific localized message (§5.2).
    const failure = untracked(this.resolutionFailure);
    if (failure !== null && failure.text === text) {
      this.markSelfWrite(null);
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
    // divergent keystroke (§5.1/§5.2).
    //
    // TRANSIENT: half-typed text is not a mistake, it is a query in
    // progress, and every keystroke on the way to a valid pick passes
    // through this branch. It must therefore never reach an inline
    // message — the marker below keeps it out of `localizedErrors` while
    // leaving it in the validity channel, so the form still cannot be
    // saved on it. What the user eventually sees is the RESOLUTION
    // outcome (branch 3), recorded only once they try to commit.
    return {
      error: {
        kind: 'parse',
        message: untracked(this.translate('entityPicker.errors.unresolved')),
        [TRANSIENT]: true,
      } as ValidationError.WithoutFieldTree,
    };
  }

  /**
   * Arms the echo guard for a parse-driven model write — but ONLY when the
   * write can actually change the model. A marker armed for a value the
   * model already holds is never consumed (the echo-guard effect re-runs on
   * value CHANGES), so it would sit there and swallow the next genuinely
   * external write instead, leaving the revert baseline behind.
   */
  private markSelfWrite(value: Id | null): void {
    if (!Object.is(value, untracked(this.value))) {
      this.selfWrite = { value };
    }
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
    this.authoredText = { text, value };
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
   *
   * In emit mode the value is not written: the input returns to empty and
   * pristine (the popup staying open on the browse list when `keepOpen`),
   * and `picked` hands the pick to the host.
   */
  private applyPick(
    id: Id,
    label: string,
    item: T | undefined,
    source: TmEntityPicked<T, Id>['source'],
    opts?: { readonly suppressCellActivate?: boolean; readonly keepOpen?: boolean },
  ): void {
    this.memo.set(id, label);
    this.resolutionFailure.set(null);
    this.resolving.set(false);
    if (untracked(this.emits)) {
      this.resetForNextPick(opts?.keepOpen === true);
      this.picked.emit({ id, label, item, source });
      return;
    }
    // Same rule as `displayFor`: `null` AND the empty string defer to the
    // label the pick itself carries.
    const resolved = untracked(() => this.displayWith()?.(id) ?? null);
    const text = resolved === null || resolved === '' ? label : resolved;
    this.setCanonicalText(text, id);
    const emitActivate =
      this.cellHost !== null && source !== 'auto' && opts?.suppressCellActivate !== true;
    this.picked.emit({ id, label, item, source });
    if (emitActivate) {
      this.ɵcellActivate.emit();
    }
  }

  /**
   * Emit mode's post-commit step: the value stays `null`, the text returns
   * to empty and pristine, and the answer settled for the old text is
   * retired. With `keepOpen` (a list pick or an Enter commit, focus still in
   * the input) the popup stays open on the browse list — the rows already
   * on screen when they ARE the browse list, highlight included, so the
   * next pick is one key away; otherwise the popup closes.
   */
  private resetForNextPick(keepOpen: boolean): void {
    this.settledResult = null;
    this.setCanonicalText('', null);
    if (!keepOpen) {
      this.setExpanded(false);
      return;
    }
    const state = untracked(this.searchState);
    if (state.kind === 'results' && state.query === '') {
      return;
    }
    this.quietBrowse = true;
    this.queueSearch('');
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
    this.supersedeResolution();
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
      this.armDebounceWindow(debounceMs);
    } else {
      this.pendingQuery = query;
      // Immediate spinner + immediate stale-row clear even while coalescing:
      // what is on screen always corresponds to the text in the field.
      this.searchState.set({ kind: 'loading', query });
      // The window SLIDES: it must go quiet for a full interval before the
      // coalesced query fires. A fixed window would re-fire on every
      // expiry, so a held key — repeating faster than the window is long —
      // would cost a request per window instead of one for the whole
      // burst, which is the entire reason the window exists.
      this.armDebounceWindow(debounceMs);
    }
  }

  /**
   * (Re)starts the coalescing window. Each change inside the window pushes
   * the deadline out, so the trailing query fires once, after the typing
   * actually stops.
   */
  private armDebounceWindow(debounceMs: number): void {
    clearTimeout(this.debounceTimer);
    this.debounceTimer = setTimeout(() => {
      this.windowOpen = false;
      this.debounceTimer = undefined;
      const pending = this.pendingQuery;
      this.pendingQuery = undefined;
      if (pending !== undefined) {
        this.queueSearch(pending);
      }
    }, debounceMs);
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

  /**
   * Normalizes both result forms to the object form — the ONE funnel every
   * result set passes through (rendering, resolution, and the host's adopted
   * search alike), which is why the id de-duplication belongs there too.
   */
  private normalizeResult(result: TmEntitySearchResult<T>): NormalizedResult<T> {
    return ɵtmNormalizeEntityResult(result, untracked(this.itemId));
  }

  /** Renders a fresh result set and schedules the highlight protocol. */
  private applyResults(query: string, result: NormalizedResult<T>): void {
    const items = result.items; // already de-duplicated by `normalizeResult`
    if (typeof ngDevMode !== 'undefined' && ngDevMode && items.length > 200) {
      console.warn(
        `tm-entity-picker: the search returned ${items.length} results — the search ` +
          `contract expects the implementation to impose a result limit (the dropdown ` +
          `has no virtual scroll).`,
      );
    }
    this.rememberResult(query, { items, hasMore: result.hasMore });
    // The browse list an emit-mode commit re-opens on lands quietly (see
    // `quietBrowse`); any other set is announced.
    const quiet = this.quietBrowse && query === '';
    this.quietBrowse = false;
    if (items.length === 0) {
      this.searchState.set({ kind: 'empty', query });
      if (!quiet) {
        this.announceKey('entityPicker.announce.noResults');
      }
    } else {
      this.searchState.set({ kind: 'results', query, items, hasMore: result.hasMore });
      if (!quiet) {
        this.announceKey(
          result.hasMore ? 'entityPicker.announce.resultsMore' : 'entityPicker.announce.results',
          { count: items.length },
        );
      }
    }
    // The highlight protocol: a typed (non-pristine) query auto-highlights
    // the FIRST result so type-then-Enter is the zero-arrow happy path; a
    // pristine browse list and a fresh empty set highlight NOTHING
    // (open-then-Tab must never change the selection; footer rows are never
    // the auto-highlight target).
    this.scheduleHighlight(
      query !== '' && items.length > 0 ? untracked(this.itemId)(items[0]) : undefined,
    );
  }

  /**
   * Stores an answer the picker actually fetched, so it outlives the popup
   * that asked for it. Closing the dropdown resets the state machine, but
   * the round trip happened: re-opening on unchanged text, and a cell
   * commit, must not pay for it twice.
   *
   * Called from BOTH consumers of a result set. Rendering is not the only
   * one: a departure that resolves while the response is still in flight
   * consumes it directly and never renders it at all — which is precisely
   * the case where the user is about to re-open and look at the rows.
   */
  private rememberResult(query: string, result: NormalizedResult<T>): void {
    this.settledResult = {
      query,
      items: result.items,
      hasMore: result.hasMore,
      source: untracked(this.search),
    };
  }

  /**
   * Requests that the option with `key` be highlighted (`undefined`: no
   * highlight). Issued by a fresh result set and by every arrow key; applied
   * by the constructor's after-render effect once the rows exist, and
   * answered by `keyboardActiveKey` until then.
   */
  private scheduleHighlight(key: unknown): void {
    this.highlightRequest.set({ key, seq: ++this.highlightSeq });
  }

  /**
   * Clears the highlight immediately (stale results must not commit): both
   * aria's active option and a request that has not been applied yet, which
   * would otherwise outlive the rows it was made for.
   */
  private clearHighlight(): void {
    this.highlightRequest.set(null);
    untracked(() => this.listbox()?._pattern.listBehavior.unfocus());
  }

  /**
   * The keys of the options the popup holds right now, in listbox order:
   * the result rows, then the footer rows. Derived from this control's own
   * state, which every keystroke and every answer updates at once — the
   * rendered rows only catch up with it on the next render. It reads the
   * same two lists the template draws its options from, in the same order.
   */
  private optionKeys(): unknown[] {
    return untracked(() => [
      ...this.resultItems().map((item) => this.idOf(item)),
      ...this.footerRows().map((row) => row.key),
    ]);
  }

  /**
   * Opens or closes the popup, writing aria's combobox model as well as this
   * control's. The two are joined by a binding, but a binding pushes only
   * CHANGES, once per render: when aria opens the popup (a keystroke) and
   * this control closes it again (a pick) before a render runs, the bound
   * value reads closed at both checks, nothing is pushed, and aria stays
   * expanded on its own. It would then announce a popup that is gone and
   * consume the next Enter. Writing both keeps them in step whatever the
   * timing.
   */
  private setExpanded(open: boolean): void {
    this.onExpandedChange(open);
    untracked(this.combobox)?.expanded.set(open);
  }

  /**
   * Records an open or a close of the popup the moment it happens, whether
   * this control wrote it or aria did (a keystroke or a collapsed ArrowDown
   * opens, Escape or a blur closes).
   *
   * What a transition owes is run later, by `syncPopup`: an open owes its
   * initial search, a close owes the reset of the search state. A close in
   * particular cannot be acted on here, because a cell commit closes the
   * dropdown first and adopts the in-flight search after. But a REOPEN that
   * arrives before that reset ran must not inherit the rows and the
   * highlight of the open before it, so the reset is run first.
   */
  protected onExpandedChange(open: boolean): void {
    if (open === untracked(this.expanded)) {
      return;
    }
    if (open && this.popupPending) {
      this.onPopupClosed(); // the close before this open, not acted on yet
    }
    this.expanded.set(open);
    this.popupPending = true;
  }

  /**
   * Acts on the popup's latest open or close, once: issues the initial
   * search of an open, or resets the search state after a close. Run by the
   * transition effect, and before it by any key that is decided against the
   * popup's rows — a key that arrives in the same task as the one that
   * opened the popup would otherwise meet no rows at all.
   */
  private syncPopup(): void {
    if (!this.popupPending) {
      return;
    }
    this.popupPending = false;
    if (untracked(this.expanded)) {
      this.onPopupOpened();
    } else {
      this.onPopupClosed();
    }
  }

  /** The dropdown just opened: run the initial (browse or seeded) search. */
  private onPopupOpened(): void {
    if (untracked(this.searchState).kind !== 'idle') {
      return;
    }
    const query = this.queryFor(this.currentText());
    // Re-opening on unchanged text asks the question that was already
    // answered: the state machine reset on close, but the fetch happened.
    // Re-applying paints the rows immediately — no spinner, no second round
    // trip — and produces exactly what the re-fetch would have, count
    // announcement and highlight included.
    const reusable = this.reusableResult(query);
    if (reusable !== null) {
      this.applyResults(query, reusable);
      return;
    }
    this.queueSearch(query);
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
    this.quietBrowse = false;
    // A request left over from this open must not re-apply against the
    // NEXT open's fresh listbox (it would auto-highlight whatever renders
    // first — footer rows included — before the browse results arrive).
    this.highlightRequest.set(null);
  }

  // ---- Resolution (§5.2) — form path only, never in a cell ----
  /**
   * Maps the current text to at most one entity, reusing the freshest
   * search: results already settled for exactly this text (synchronous, no
   * request), else the in-flight request (awaited, NOT aborted), else one
   * fresh request. Exactly one, from a set that is not truncated →
   * auto-pick; zero, many, a capped page, or a failure → keep the text,
   * write `null`, raise the specific localized error.
   */
  private async resolveText(text: string, keepPopup: boolean): Promise<void> {
    // Take over from a resolution already pending: its answer is discarded
    // by the epoch bump, and its request is aborted — UNLESS that request is
    // the one THIS resolution is about to await. A second commit gesture on
    // the same text (Enter while loading, then leaving) reuses the very
    // request the first one adopted, and aborting it there would fail the
    // round trip this resolution just asked for.
    const reused =
      this.inFlight !== null && this.inFlight.query === text ? this.inFlight.controller : null;
    if (this.inFlight !== null && reused === null) {
      // A search for a DIFFERENT query is nobody's input: this resolution
      // asks its own question instead, and the epoch bump below would leave
      // that request answering into the void. The consumer was promised the
      // signal fires when a request is superseded — this is that moment.
      this.inFlight.controller.abort();
      this.inFlight = null;
    }
    ++this.resolutionEpoch;
    if (this.resolutionController !== null && this.resolutionController !== reused) {
      this.resolutionController.abort();
    }
    this.resolutionController = null;
    const token = this.resolutionEpoch;
    // The resolution owns the outcome now: silence the awaited search's own
    // continuation (it must not double-render what this method consumes)…
    const searchToken = ++this.searchEpoch;
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
      // ADOPT the request: it is this resolution's input now, so this
      // resolution's abort handle is the one it already has. Without that,
      // a supersession that starts nothing of its own (an external value
      // write) would leave the consumer working on an answer no one reads.
      this.resolutionController = this.inFlight.controller;
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
      //
      // The search token gates the repair. A superseder that started its
      // OWN search bumps it, and that search may well be for the same text
      // — the reopen-while-resolving path does exactly that — in which case
      // the loading state on screen belongs to a live request, and clearing
      // it here would blank a running search and drop its abort handle.
      const current = untracked(this.searchState);
      if (
        searchToken === this.searchEpoch &&
        untracked(this.expanded) &&
        current.kind === 'loading' &&
        current.query === text
      ) {
        this.searchState.set({ kind: 'idle' });
        this.inFlight = null;
      }
      return;
    }
    // The awaited request is consumed (its own continuation was superseded
    // by the search-epoch bump above and will not run these repairs) — so
    // this is the only place its answer can be recorded.
    this.inFlight = null;
    this.resolutionController = null;
    if (outcome !== 'failed') {
      this.rememberResult(text, outcome);
    }
    if (outcome === 'failed') {
      this.failResolution('searchFailed', text);
      if (this.popupOwnsStatus(keepPopup)) {
        // The Enter path keeps the popup open: the status must not stay
        // frozen on the loading spinner.
        this.searchState.set({ kind: 'error', query: text });
        this.announceKey('entityPicker.announce.searchFailed');
      }
      return;
    }
    // A TRUNCATED set can be trusted about what it contains and never about
    // what it does not: the second entity that makes this text ambiguous may
    // be sitting past the server's cap. Auto-resolution needs the whole
    // answer, so a capped page falls through to `ambiguous` below.
    if (outcome.items.length === 1 && !outcome.hasMore) {
      const item = outcome.items[0];
      this.resolving.set(false);
      // In emit mode an Enter commit keeps the popup open for the next pick
      // (unless the user closed it while the answer was on its way), and
      // the host announces the pick: nothing is left to do here.
      this.applyPick(
        untracked(this.itemId)(item),
        untracked(this.itemLabel)(item),
        item,
        'auto',
        { keepOpen: this.popupOwnsStatus(keepPopup) },
      );
      if (untracked(this.emits)) {
        return;
      }
      if (keepPopup) {
        this.setExpanded(false); // the Enter path: a pick closes the popup
      } else {
        // Blur path: announce the auto-resolution outcome (§8).
        this.announceKey('entityPicker.announce.picked', {
          label: untracked(this.itemLabel)(item),
        });
      }
      return;
    }
    this.failResolution(outcome.items.length === 0 ? 'noMatch' : 'ambiguous', text);
    if (this.popupOwnsStatus(keepPopup)) {
      // The Enter path keeps the popup open as the recovery surface: show
      // the fetched candidates (or the honest empty status) instead of a
      // spinner that nothing will ever resolve.
      this.applyResults(text, outcome);
    }
  }

  /**
   * Hands ownership away from any pending resolution. The epoch bump is what
   * makes a late answer harmless, but the abort is what the CONSUMER was
   * promised: the signal handed to `search` fires when the request is
   * superseded, and a consumer that cancels on it must not be left holding
   * a round trip nobody will ever read.
   */
  private supersedeResolution(): void {
    ++this.resolutionEpoch;
    this.resolutionController?.abort();
    this.resolutionController = null;
    this.resolving.set(false);
  }

  /**
   * Whether a resolution outcome should be painted into the popup's status
   * area. BOTH tests are needed and neither is sufficient:
   *
   * - The caller's intent, because a departure resolves before aria's
   *   close-on-blur effect has flushed — `expanded` still reads true there,
   *   and a blur would re-render and announce a popup that is leaving.
   * - The popup's actual state, because an Enter-while-loading keeps focus
   *   and the popup open, and the user can still close it (Escape) before
   *   the answer lands — writing the status then would leave the NEXT open
   *   showing a stale failure, with no search of its own (the initial
   *   search only runs from the `idle` state).
   */
  private popupOwnsStatus(keepPopup: boolean): boolean {
    return keepPopup && untracked(this.expanded);
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
   * The text this control currently holds — the picker's OWN channel, never
   * the DOM's.
   *
   * The native input lags the picker by a render pass on every
   * picker-authored write: aria mirrors the combobox text model into
   * `element.value` from an after-render effect, and a commit gesture that
   * moves focus in the same task (Tab, above all) is read back before that
   * mirror runs. The raw channel is updated synchronously by every writer —
   * typing, picks, seeds, quiet cell installs — so it is the only reading
   * that is true at every instant.
   */
  private currentText(): string {
    return untracked(this.rawText);
  }

  /**
   * The capture-phase keyboard layer on the host — the only reliable
   * pre-emption point against aria's own input listeners. Five cases, all
   * version-locked to aria's current behavior and each guarded by a test
   * (the Enter and open-arrow cases by the tests that deliver several keys
   * in one task):
   *
   * - Readonly, plain vertical arrows: aria expands the collapsed popup on
   *   ArrowDown and is stood down only by its OWN `disabled` input, which
   *   this control does not use for readonly (it would announce the value
   *   as unavailable). Swallowed here instead.
   * - Grid mode, dropdown closed, plain vertical arrows: same unconditional
   *   expand, but in a grid plain arrows belong to the grid's
   *   commit-and-move model — the original is stopped and an identical
   *   clone is re-dispatched ABOVE the host so it reaches the grid
   *   unconsumed, with the grid's verdict mirrored back onto the original.
   *   TODO: replace this re-dispatch with a plain pass-through once
   *   @angular/aria ships an input to suppress the collapsed
   *   ArrowDown-expands behavior.
   * - Dropdown open, Enter: activated HERE, synchronously, against the
   *   picker's own highlight state (see `keyboardActiveKey`). aria relays
   *   Enter to the listbox from an after-render effect, so a key that
   *   arrives before the render (a scanner, a text expander, a stalled main
   *   thread) would be decided against the previous rows, or dropped when
   *   the listbox has not mounted yet. In grid mode, Enter with no highlight
   *   is the grid's commit instead, handed over as a clean clone.
   * - Dropdown open, plain vertical arrows: moved HERE, synchronously, over
   *   the picker's own option list (see `optionKeys`). aria's relay holds
   *   one key and delivers it after the render, so of several arrows in one
   *   task only the last would move, among the previous rows, and a commit
   *   key in that task would not see the move at all. ArrowUp from no
   *   highlight reaches the LAST option, where aria's own stepping lands
   *   second-to-last.
   * - Dropdown open, plain Home/End: aria's relay would consume them and
   *   move the highlight; the APG editable-combobox model gives them to the
   *   CARET, so the relay is suppressed and the native caret motion runs.
   */
  private readonly onCaptureKeydown = (event: KeyboardEvent): void => {
    if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) {
      return;
    }
    if (this.readonly() && (event.key === 'ArrowDown' || event.key === 'ArrowUp')) {
      // A read-only picker offers no choices. aria's collapsed ArrowDown
      // expands unconditionally and it is only stood down by its OWN
      // `disabled` input — which this control does not use for readonly,
      // because that would announce the value as unavailable.
      event.stopPropagation();
      return;
    }
    const isExpanded = untracked(this.expanded);
    if (
      this.cellHost !== null &&
      !isExpanded &&
      (event.key === 'ArrowDown' || event.key === 'ArrowUp')
    ) {
      event.stopPropagation();
      this.redispatchAbove(event);
      return;
    }
    const vertical = event.key === 'ArrowDown' || event.key === 'ArrowUp';
    if (isExpanded && (vertical || event.key === 'Enter') && !this.disabled() && !this.readonly()) {
      event.stopPropagation(); // aria's deferred relay never sees it
      // A key pressed during an IME composition (`isComposing`, or WebKit's
      // 229 status code) belongs to the composition — the Enter that
      // confirms it, the arrows that walk its candidates: no pick, no
      // commit, no highlight move, and no preventDefault, so the conversion
      // runs untouched.
      if (event.isComposing || (event as { readonly keyCode: number }).keyCode === 229) {
        return;
      }
      // The key is decided against the popup's rows: if the popup opened in
      // this same task, its initial search has to be issued first.
      this.syncPopup();
      if (vertical) {
        // The caret stays where it is while browsing (spec 0006 §4.3).
        event.preventDefault();
        // Step from the highlight the picker's state has decided, over the
        // options that state lists, wrapping at both ends; with nothing
        // highlighted ArrowDown takes the first option and ArrowUp the last.
        // A held key's repeats step too, as under aria's own binding.
        const keys = this.optionKeys();
        const count = keys.length;
        if (count > 0) {
          const at = keys.indexOf(this.keyboardActiveKey()); // -1: nothing highlighted
          const to = event.key === 'ArrowDown' ? at + 1 : Math.max(at, 0) + count - 1;
          this.scheduleHighlight(keys[to % count]);
        }
        return;
      }
      const key = this.keyboardActiveKey();
      if (key === undefined && this.cellHost !== null) {
        // Enter with no highlight in a cell is the GRID's commit. The grid's
        // dropdown gate stands down only once the dropdown is closed, so
        // close it first and hand the grid a clean clone.
        this.setExpanded(false);
        this.redispatchAbove(event);
        return;
      }
      // With the popup open Enter is always consumed: it never submits a form.
      event.preventDefault();
      // A held key's repeats are ignored, as aria's own binding ignores them:
      // one press, one activation.
      if (!event.repeat) {
        this.activateActive(key, true);
      }
      return;
    }
    if (isExpanded && (event.key === 'Home' || event.key === 'End')) {
      event.stopPropagation();
    }
  };

  /**
   * Re-dispatches a stopped keystroke ABOVE the host so it reaches the grid
   * unconsumed, then mirrors the grid's verdict back onto the ORIGINAL: the
   * grid can only `preventDefault` the clone, and the original — still
   * targeting the input — keeps its default action otherwise. For Enter
   * inside a `<form>` that default is implicit submission, which would
   * navigate the page out from under the edit.
   */
  private redispatchAbove(event: KeyboardEvent): void {
    const parent = this.hostElement.parentElement;
    if (parent === null) {
      return;
    }
    if (!parent.dispatchEvent(new KeyboardEvent(event.type, event))) {
      event.preventDefault();
    }
  }

  /** Mirrors keystrokes into the raw channel and queues the search. */
  protected onInput(): void {
    const text = this.inputElement.value; // the DOM IS the source here
    // Typing opens the popup. aria's own input listener does that too, but
    // the open has to be recorded BEFORE the search below builds its state:
    // an open that follows a close in the same task resets what that close
    // left behind, and must not reset this keystroke's search with it. Doing
    // it here keeps that order whichever listener runs first.
    if (!this.disabled()) {
      this.setExpanded(true);
    }
    this.editedSinceFocus = true;
    this.quietBrowse = false; // the user's own query is announced
    this.authoredText = null; // whatever is on screen is the user's now
    this.settledResult = null; // …and the stored answer described the old text
    this.supersedeResolution(); // editing resumes ownership from any pending resolution
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
      this.setExpanded(true);
    }
  }

  /** Focus bookkeeping. */
  protected onFocusin(): void {
    if (!untracked(this.focused)) {
      // Only a genuine arrival resets the edited bookkeeping. Focus also
      // comes BACK here without the control ever having lost it — the panel
      // handing it over after a press that missed a row, a modal page
      // returning it — and those must not erase the fact that the user
      // typed before they happened.
      this.editedSinceFocus = false;
    }
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
    if (!this.editedSinceFocus) {
      // A visit that typed nothing can never change the value. The text on
      // screen may nevertheless no longer be the value's CURRENT display
      // text — the reformat stands down for a focused field, so a warmed
      // entity cache, a rename, or a locale switch during the visit leaves
      // the older rendering in place. Resolving that text would search for
      // a label the picker itself wrote and null the model over it; the
      // reformat effect picks it up instead, now that focus has left.
      return;
    }
    const text = this.currentText();
    if (text === '') {
      // Empty commit: clearing the text clears the value (required is the
      // field's concern). Trivial for an already-null model.
      this.setCanonicalText('', null);
      return;
    }
    const authored = this.authoredText;
    if (
      authored !== null &&
      authored.text === text &&
      Object.is(authored.value, untracked(this.value))
    ) {
      // The text on screen is the text this control WROTE, for the value the
      // model still holds — a pick from the list, a Tab commit, a modal
      // outcome. There is nothing to resolve, and re-resolving it would
      // re-emit `picked` and hand an ambiguous label back its own error.
      // Note this survives a display-context change: `displayWith` may have
      // started formatting that id differently since the pick (a cache the
      // host warms from `picked` is the pattern the contract recommends),
      // which makes the pristine test below false while the user has done
      // nothing at all.
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
   * consumed. Esc is aria's; Enter and the plain vertical arrows while
   * open are the capture layer's (see `onCaptureKeydown`).
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
    this.syncPopup(); // a popup opened in this same task has rows only after its search
    const key = this.keyboardActiveKey();
    if (key !== undefined && typeof key !== 'symbol') {
      const item = untracked(this.resultItems).find((i) => untracked(this.itemId)(i) === key);
      if (item !== undefined) {
        this.applyPick(key as Id, untracked(this.itemLabel)(item), item, 'list', {
          suppressCellActivate: true,
        });
      }
    }
    this.setExpanded(false);
  }

  /** The key of aria's active option, or `undefined` when none is active. */
  private domActiveKey(): unknown {
    return untracked(() => this.ariaOptions().find((o) => o.active())?.value());
  }

  /**
   * The option the keyboard acts on — the one a commit gesture (Enter, Tab)
   * commits and an arrow key steps from: an entity id or a footer-row
   * sentinel, or `undefined` when nothing is highlighted.
   *
   * Usually that is aria's active option. But a highlight is applied after
   * the rows render, and keys can arrive before that render: a barcode
   * scanner, a text expander or a stalled main thread delivers keystrokes,
   * arrows and the commit key back to back. The DOM then still shows the
   * previous rows and the previous highlight. The fresh result set and the
   * arrows have already decided what the highlight will be, so a PENDING
   * request answers instead. What a key commits then never depends on
   * whether a render ran before it.
   */
  private keyboardActiveKey(): unknown {
    // spec 0006 §4.3 ties the auto-highlight to the fresh result set, and
    // §4.2 renders a synchronous set "in the same turn": the highlight is a
    // fact of the picker's state, not of the paint.
    const request = untracked(this.highlightRequest);
    const key =
      request === null || request.seq === this.appliedHighlightSeq
        ? this.domActiveKey()
        : request.key;
    // Only an option the popup still lists counts: a highlight left on a row
    // that the state has since dropped (and the next render will remove)
    // commits nothing.
    return this.optionKeys().includes(key) ? key : undefined;
  }

  /** Commits when an option row is clicked; other panel clicks do nothing. */
  protected onListboxClick(event: MouseEvent): void {
    const row = (event.target as Element).closest('[ngOption]');
    if (row !== null && row.getAttribute('aria-disabled') !== 'true') {
      // The pointer's own target: aria makes the pressed row active, and a
      // press can only land on a row that is on screen.
      this.activateActive(this.domActiveKey());
    }
  }

  /**
   * One activation path for click and Enter (never `valueChange` — the
   * aria-in-overlay mouse-bug posture), given the activated option's `key`:
   * a footer row opens its modal; an entity row commits. With no key,
   * Enter (`enterFallbacks`) falls back to resolution (loading), fail-fast
   * (fresh empty typed set), or a plain close (pristine).
   */
  private activateActive(key: unknown, enterFallbacks = false): void {
    if (key !== undefined) {
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
      if (item === undefined) {
        return;
      }
      this.focus();
      if (untracked(this.emits)) {
        // An explicit list pick toggles: a chosen row comes off, any other
        // row goes on. Either way the popup stays open for the next pick.
        if (untracked(this.ɵchosenIds).includes(key as Id)) {
          this.resolutionFailure.set(null);
          this.resetForNextPick(true);
          // The browse rows stay, but not the highlight on the row just
          // taken off: Tab commits a highlighted row, and would put it
          // straight back.
          this.clearHighlight();
          this.ɵdeselect.emit(key as Id);
          return;
        }
        this.applyPick(key as Id, untracked(this.itemLabel)(item), item, 'list', {
          keepOpen: true,
        });
        return;
      }
      this.applyPick(key as Id, untracked(this.itemLabel)(item), item, 'list');
      this.setExpanded(false);
      return;
    }
    if (!enterFallbacks || this.cellHost !== null) {
      return;
    }
    const text = this.currentText();
    const state = untracked(this.searchState);
    if (text === '' && untracked(this.value) !== null) {
      // Enter with cleared text is a commit gesture: clearing the text
      // clears the value, exactly like the blur empty commit.
      this.touchedSelf.set(true);
      this.touch.emit();
      this.setCanonicalText('', null);
      this.setExpanded(false);
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
    this.setExpanded(false);
  }

  /**
   * Panel presses on rows keep focus in the input — a press inside the
   * popup is its own interaction (a pick, a modal launch), never a blur
   * departure. Presses that miss every row (the listbox's padding band, its
   * scroll gutter) are deliberately left alone so scrollbar drags behave
   * natively; the focusin handler below catches the focus they move.
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

  /**
   * The activedescendant model puts DOM focus on the input and nowhere else.
   * Anything inside the panel receiving focus anyway — a programmatic move,
   * a press this component did not see — hands it straight back, because
   * aria keeps the popup open while its widget holds focus and the input
   * would sit there un-typable with the picker still believing it is
   * focused.
   */
  protected onPanelFocusin(): void {
    if (document.activeElement !== this.inputElement) {
      this.inputElement.focus({ preventScroll: true });
    }
  }

  /** An outside pointer press closes the dropdown (the text is kept). */
  protected onOutsideClick(): void {
    this.setExpanded(false);
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
    const config: {
      component: Type<unknown>;
      size?: TmModalSize;
      title?: string;
      context?: unknown;
    } = typeof page === 'function' ? { component: page } : page;
    const text = this.currentText();
    // Advanced search from an emit-mode host edits the host's whole
    // selection: it opens on the chosen ids and the host's cap.
    const multiple = source === 'advanced' && untracked(this.emits);
    const maxItems = untracked(this.ɵmaxItems);
    const data: TmEntityPickerPageData<Id> = {
      // Pristine text is a browse intent, not a query — a create page must
      // not prefill the OLD entity's label as the new entity's name.
      query: this.isBrowseText(text) ? '' : text,
      ...(source === 'edit' ? { id: untracked(this.value) ?? undefined } : {}),
      ...(multiple
        ? {
            multiple: true as const,
            ids: [...untracked(this.ɵchosenIds)],
            ...(maxItems !== undefined ? { maxItems } : {}),
          }
        : {}),
      // The page's context rides through unread (spec 0010 §E.3); the bare
      // class form, and a config without one, pass none at all.
      ...(config.context !== undefined ? { context: config.context } : {}),
    };
    // Supersede the search AND any pending resolution; the modal outcome
    // governs from here.
    ++this.searchEpoch;
    this.supersedeResolution();
    this.inFlight?.controller.abort();
    this.inFlight = null;
    this.cancelDebounce();
    this.suppressBlur = true; // focus moves into the dialog — not a departure
    this.setExpanded(false);
    // The modal captures whatever holds focus RIGHT NOW as its restore
    // target. A magnifier press suppresses its own focus transfer, so on a
    // picker that was never focused that target would be document.body and
    // dismissing the page would drop the user outside the control entirely.
    this.focus();
    const titleKey =
      source === 'advanced'
        ? 'entityPicker.advancedTitle'
        : source === 'create'
          ? 'entityPicker.createTitle'
          : 'entityPicker.editTitle';
    const size: TmModalSize = config.size ?? (source === 'advanced' ? 'lg' : 'md');
    // The flag is released in a `finally`: a page whose construction throws
    // takes the throw out through `modal.open`, and a latched `suppressBlur`
    // would retire this control's departure handling for good — no touch
    // reporting, no empty commit, no blur resolution, and a reformat effect
    // stuck standing down for a field it still believes is focused.
    let result: Awaited<TmModalRef<PageOutcome<Id, T>>['closed']>;
    try {
      const ref = this.modal.open<PageOutcome<Id, T>>(config.component, {
        size,
        title: config.title ?? untracked(this.translate(titleKey)),
        data,
      });
      this.openModal = ref;
      result = await ref.closed;
    } finally {
      this.openModal = null;
      this.suppressBlur = false;
    }
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
    if (isMultiPick(pick)) {
      // A whole selection is the emit-mode host's to apply; it is not a pick
      // this control could commit as its one value.
      if (untracked(this.emits)) {
        this.settledResult = null;
        this.setCanonicalText('', null);
        this.ɵmultiPick.emit(pick);
        this.focus();
      }
      return;
    }
    // A single pick from a page that does not edit whole selections adds,
    // in emit mode, like any other pick.
    this.applyPick(pick.id, pick.label, pick.item, source);
    this.focus();
  }

  // ---- Announcements ----
  /** Announces a localized message through the polite live region. */
  private announceKey(key: string, params?: Record<string, unknown>): void {
    if (!untracked(this.expanded) && !key.endsWith('.picked')) {
      return; // count/failure announcements belong to the open popup only
    }
    this.ɵannounce(untracked(this.translate(key, params)));
  }

  /**
   * Announces a message through the polite live region, whatever the
   * popup's state; an identical repeat is cleared first so it is read
   * again. The multi-picker announces its additions and removals here.
   * @internal
   */
  ɵannounce(message: string): void {
    // A newer message supersedes a repeat still waiting to be restored:
    // the late restore would otherwise overwrite it.
    this.cancelAnnounceRestore?.();
    if (untracked(this.liveText) === message) {
      // Re-announce an identical message: clear first, restore a beat later
      // (an unchanged live region announces nothing). The wait is a pending
      // task, so the app's stability, and a test's, covers the restore.
      this.liveText.set('');
      const removeTask = this.pendingTasks.add();
      const timer = setTimeout(() => {
        this.liveText.set(message); // schedules its render before the task ends
        cancel(); // clearing a timer that already ran is a no-op
      });
      const cancel = (): void => {
        this.cancelAnnounceRestore = null;
        clearTimeout(timer);
        removeTask();
      };
      this.cancelAnnounceRestore = cancel;
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
   * else the raw unresolved text, which the host resolves — through this
   * control's own adopted search first, then the column's label resolver.
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
   * Accepts the edit: in a cell, a unique result already settled for the
   * current unresolved text auto-picks synchronously first (no request),
   * then the value baseline moves and the dropdown closes. Anything else is
   * the HOST's question — it adopts this control's search for that text and
   * decides from it, falling back to the column's resolver.
   */
  commit(): void {
    if (this.cellHost !== null) {
      const raw = untracked(this.rawText);
      if (raw !== '' && this.text() !== null) {
        // The last SETTLED set for exactly this text, whether or not the
        // popup still shows it: dismissing the list with Escape closes a
        // dropdown, it does not un-fetch what the user already saw.
        //
        // A TRUNCATED page never decides it: the duplicate that would make
        // this text ambiguous may be sitting past the server's cap, and this
        // fast path runs BEFORE the host's own verdict — auto-picking here
        // would settle the commit off a partial answer and the host would
        // never get to refuse it.
        const settled = this.settledResult;
        if (
          settled !== null &&
          settled.query === raw &&
          settled.items.length === 1 &&
          !settled.hasMore
        ) {
          const item = settled.items[0];
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
    this.setExpanded(false);
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
    this.setExpanded(false);
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
    this.authoredText = null;
    this.settledResult = null;
    this.resolutionFailure.set(null);
    this.rawText.set(text);
    this.comboText.set(text);
    const element = this.inputElement;
    element.value = text;
    element.setSelectionRange(text.length, text.length);
    this.setExpanded(true);
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
    this.authoredText = null;
    this.settledResult = null;
    this.rawText.set(text);
    this.comboText.set(text);
    const element = this.inputElement;
    element.value = text;
    element.setSelectionRange(text.length, text.length);
  }

  /**
   * The already-settled answer for exactly `text`, wrapped as an adopted
   * search with a no-op abort — there is no request left to cancel.
   */
  private settledFor(text: string): ɵTmEntityAdoptedSearch<T> | null {
    const answer = this.reusableResult(text);
    return answer === null ? null : { settled: Promise.resolve(answer), abort: () => {} };
  }

  /**
   * The stored answer for `query`, or `null` when there is none to reuse.
   *
   * The entry is dropped by every write to the content (typing, a seed, a
   * quiet cell install), so what survives can only ever be the answer to
   * the text exactly as it now stands: `Adam` → `Ada` → `Adam` re-asks,
   * because the middle keystroke retired the first answer, and it does so
   * whether or not the `Ada` search ever came back.
   */
  private reusableResult(query: string): NormalizedResult<T> | null {
    const settled = this.settledResult;
    if (settled === null || settled.query !== query) {
      return null;
    }
    if (settled.source !== untracked(this.search)) {
      return null;
    }
    return { items: settled.items, hasMore: settled.hasMore };
  }

  /**
   * Hands the host the picker's own answer for `text` — the settled result
   * set, or the request already in flight for it — so a cell commit can be
   * decided by the search the user already paid for instead of by a second,
   * differently-shaped round trip.
   *
   * The adopted request is DETACHED: it is removed from the picker's
   * in-flight slot, so nothing the picker does from here on (a new search,
   * the popup closing, a modal launch, its own destruction) aborts it. That
   * is the point — the editor's lifetime ends at commit and the answer has
   * to outlive it. Ownership passes with it: the returned `abort` is now
   * the only handle, and a host that drops it leaks the request.
   *
   * Returns `null` when the picker has nothing for this exact text, which
   * is the host's signal to fall back to its own resolution path.
   * @internal
   */
  ɵadoptSearch(text: string): ɵTmEntityAdoptedSearch<T> | null {
    // A query still sitting inside the coalescing window was going to be
    // searched a few milliseconds from now. Leaving the cell early is not a
    // reason to discard it and ask a different question instead, so the
    // window is flushed rather than cancelled — the quiet period simply
    // arrived sooner than the timer did.
    //
    // An answer already in hand is asked for first, being the cheaper one.
    // It cannot co-exist with a pending query for the same text — every
    // write to the content retires the stored answer, and a query is only
    // pending because such a write just queued it — so the order is a
    // reading convenience, not a guard. The SECOND check, after the flush,
    // is load-bearing: a synchronous source settles inside `executeSearch`
    // and leaves nothing in the in-flight slot to adopt, so the answer
    // would be sitting right here and get thrown away.
    const inHand = this.settledFor(text);
    if (inHand !== null) {
      return inHand;
    }
    if (this.pendingQuery === text) {
      this.cancelDebounce();
      this.executeSearch(text);
      const flushed = this.settledFor(text);
      if (flushed !== null) {
        return flushed;
      }
    }
    const inFlight = this.inFlight;
    if (inFlight === null || inFlight.query !== text) {
      return null;
    }
    this.inFlight = null; // detached — see above
    return {
      settled: inFlight.settled.then((outcome) =>
        outcome === 'failed'
          ? ('failed' as const)
          : { items: outcome.items, hasMore: outcome.hasMore },
      ),
      abort: () => inFlight.controller.abort(),
    };
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
    this.setExpanded(true);
  }
}

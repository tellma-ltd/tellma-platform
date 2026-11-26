// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgComponentOutlet, NgTemplateOutlet } from '@angular/common';
import {
  afterNextRender,
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  contentChild,
  DestroyRef,
  Directive,
  effect,
  ElementRef,
  inject,
  Injector,
  input,
  type InputSignal,
  type InputSignalWithTransform,
  type Signal,
  signal,
  TemplateRef,
  type Type,
  untracked,
  type WritableSignal,
} from '@angular/core';
import type { FieldTree } from '@angular/forms/signals';
import { ɵtmControlSize } from '@tellma/core-ui';
import { TmButton } from '@tellma/core-ui/button';
import { TmIcon } from '@tellma/core-ui/icon';
import { TmSkeleton } from '@tellma/core-ui/skeleton';
import {
  TmEntities,
  type TmEntityDescriptor,
  type TmId,
  type TmJson,
  type TmJsonObject,
  TmLocalization,
  type TmNavigationDescriptor,
  type TmPathResolution,
  type TmPropertyDescriptor,
} from '@tellma/core';
import { TmValue, ɵtmEditorKind } from '@tellma/core/entity';
import { Lock } from 'lucide';

import type { TmDetailsFieldRef } from './tm-details-context';
import { injectTmDetailsRecord, tmDevMode } from './tm-details-record';
import { TM_DETAILS_SCOPE } from './tm-details-scope';
import type { ɵTmFieldCell, ɵTmFieldEditing } from './tm-field-cell';

// A labelled value of the record (spec 0031 §6.2): the path resolves
// through the entity registry, and its end kind decides the rendering. In
// view mode it is `tm-value` in a description list's term and definition;
// in edit and create mode the editor, loaded on first use so a view-mode
// visit never downloads the edit controls, in a `tm-form-field`. Both modes
// draw the same label row and value box, so switching moves nothing. A
// group anchor renders one cell per slot the tenant's shape admits. Each
// cell registers with the record, and reports its error to the nearest
// card or tab, which its focus() opens first.

/** The context of a `tmFieldEdit` template: the field to bind, and what it edits. */
export interface TmFieldEditContext {
  /** The Signal Forms field the control binds to, as `[formField]`. */
  readonly $implicit: FieldTree<unknown>;
  /** What the field edits: the property, or a navigation whose key the control writes. */
  readonly property: TmPropertyDescriptor | TmNavigationDescriptor | null;
}

/** The context of a `tmFieldView` template: the value to show, and what it is. */
export interface TmFieldViewContext {
  /** The value, wire-shaped; a navigation's related row, or its id when the read returned none. */
  readonly $implicit: unknown;
  /** What the value is: the property, or the navigation. */
  readonly property: TmPropertyDescriptor | TmNavigationDescriptor | null;
}

/**
 * A layout's own edit-mode control for a `tm-field`, which keeps the
 * field's cell, label, hint and errors.
 *
 * @example
 * ```html
 * <tm-field path="Color">
 *   <ng-template tmFieldEdit let-field><acme-color-picker [formField]="field" /></ng-template>
 * </tm-field>
 * ```
 */
@Directive({ selector: 'ng-template[tmFieldEdit]' })
export class TmFieldEdit {
  /** The control's template. */
  readonly template: TemplateRef<TmFieldEditContext> = inject<TemplateRef<TmFieldEditContext>>(TemplateRef);

  /**
   * Types the template's context.
   *
   * @param _directive - The directive.
   * @param _context - The context.
   * @returns Always `true`.
   */
  // eslint-disable-next-line @typescript-eslint/no-unused-vars -- type-position-only parameter
  static ngTemplateContextGuard(_directive: TmFieldEdit, _context: unknown): _context is TmFieldEditContext {
    return true;
  }
}

/**
 * A layout's own view-mode rendering for a `tm-field`, in the field's value box.
 *
 * @example
 * ```html
 * <tm-field path="Color">
 *   <ng-template tmFieldView let-value><acme-color-swatch [value]="value" /></ng-template>
 * </tm-field>
 * ```
 */
@Directive({ selector: 'ng-template[tmFieldView]' })
export class TmFieldView {
  /** The rendering's template. */
  readonly template: TemplateRef<TmFieldViewContext> = inject<TemplateRef<TmFieldViewContext>>(TemplateRef);

  /**
   * Types the template's context.
   *
   * @param _directive - The directive.
   * @param _context - The context.
   * @returns Always `true`.
   */
  // eslint-disable-next-line @typescript-eslint/no-unused-vars -- type-position-only parameter
  static ngTemplateContextGuard(_directive: TmFieldView, _context: unknown): _context is TmFieldViewContext {
    return true;
  }
}

/** The editor's component, once the edit side has loaded. */
let editorType: Promise<Type<unknown>> | null = null;

/**
 * Loads the edit side once: a view-mode visit never downloads it.
 *
 * @returns The editor's component.
 */
function loadEditor(): Promise<Type<unknown>> {
  editorType ??= import('@tellma/core/entity-editing').then(
    ({ ɵTmFieldEditor }) => ɵTmFieldEditor,
    (error: unknown) => {
      // A failed load is forgotten, so the next attempt downloads it again.
      editorType = null;
      throw error;
    },
  );
  return editorType;
}

/**
 * The edit side's loader, which a test may replace to fail or delay it, and
 * the editor once any field has loaded it, which every field then shows.
 */
export const ɵtmFieldEditorLoader: {
  /** Loads the edit side and resolves its editor's component. */
  load: () => Promise<Type<unknown>>;
  /** The editor's component once a load succeeds, so one field's Try again or focus() serves them all. */
  readonly loaded: WritableSignal<Type<unknown> | null>;
} = { load: loadEditor, loaded: signal(null) };

/** The one field that announces a failed load of the edit side, which every editable field shows. */
const failureVoice = signal<object | null>(null);

/** A multilingual twin's path: the group's name, then its slot. */
const TWIN = /^(.+?)([23])$/;

/**
 * A labelled value of the record, by its path: a scalar, a multilingual
 * group (one cell per language the tenant has), a navigation (a link in
 * view mode, the entity picker in edit mode), an image, a file, or a path
 * through navigations (read-only). It reads the record from its details
 * page; a layout passes paths, never values.
 *
 * @example
 * ```html
 * <tm-details-card>
 *   <tm-field path="Name" />
 *   <tm-field path="Currency" filter="IsActive" />
 *   <tm-field path="CreditLimit" hintKey="Acme_Customer_CreditLimit_Hint" />
 * </tm-details-card>
 * ```
 */
@Component({
  selector: 'tm-field',
  imports: [NgComponentOutlet, NgTemplateOutlet, TmButton, TmIcon, TmSkeleton, TmValue],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'tm-field' },
  styles: `
    /* The field is its cells: each one is an item of the card's grid. */
    :host {
      display: contents;
    }

    /* A cell, with the field size ladder tm-form-field uses, so both modes measure the same. */
    .tm-field__cell {
      --tm-details-field-height: var(--field-height);
      --tm-details-field-font-size: var(--field-font-size);
      --tm-details-field-padding-x: var(--field-padding-x);
      --tm-details-field-label-gap: var(--field-label-gap);

      grid-column: span 1;
      min-inline-size: 0;
      font-family: var(--font-ui);
    }

    .tm-field__cell--sm {
      --tm-details-field-height: var(--field-height-sm);
      --tm-details-field-font-size: var(--field-font-size-sm);
      --tm-details-field-padding-x: var(--field-padding-xsm);
      --tm-details-field-label-gap: var(--field-label-gap-sm);
    }

    .tm-field__cell--lg {
      --tm-details-field-height: var(--field-height-lg);
      --tm-details-field-font-size: var(--field-font-size-lg);
      --tm-details-field-padding-x: var(--field-padding-xlg);
      --tm-details-field-label-gap: var(--field-label-gap-lg);
    }

    /* Two columns: every column of a one-column grid, both of a two-column one. */
    .tm-field__cell--wide {
      grid-column: 1 / -1;
    }

    /* View mode: a one-item description list, laid out as tm-form-field lays out its field. */
    .tm-field__view {
      margin: 0;
    }

    .tm-field__item {
      display: flex;
      flex-direction: column;
      gap: var(--tm-details-field-label-gap);
    }

    .tm-field__label {
      display: flex;
      align-items: baseline;
      gap: var(--space-1);
      color: var(--text-strong);
      font-size: var(--field-label-font-size);
      font-weight: var(--weight-medium);
    }

    /* The value box: the control's height and inline padding, its border transparent. */
    .tm-field__box {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      box-sizing: border-box;
      block-size: var(--tm-details-field-height);
      margin: 0;
      padding-inline: var(--tm-details-field-padding-x);
      overflow: hidden;
      border: var(--border-width) solid transparent;
      color: var(--text-default);
      font-size: var(--tm-details-field-font-size);
      white-space: nowrap;
    }

    .tm-field__box:focus-visible {
      outline: var(--focus-ring-width) solid transparent;
      box-shadow: var(--focus-ring);
    }

    /* Several lines: the three rows the textarea shows, growing with the text. */
    .tm-field__box--multiline {
      align-items: flex-start;
      block-size: auto;
      min-block-size: calc(3lh + 2 * var(--field-padding-y) + 2 * var(--border-width));
      padding-block: var(--field-padding-y);
      line-height: var(--leading-ui);
      white-space: pre-wrap;
    }

    .tm-field__value {
      flex: 1 1 auto;
      min-inline-size: 0;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .tm-field__lock {
      --icon-size: var(--icon-size-xs);

      flex: none;
      color: var(--text-secondary);
    }

    .tm-field__hint {
      margin: 0;
      color: var(--text-secondary);
      font-size: var(--field-hint-font-size);
      line-height: var(--leading-snug);
    }

    /* The edit side's failed load: the message in the error colour, then Try again. */
    .tm-field__failed {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2);
      margin: 0;
      color: var(--error);
      font-size: var(--field-hint-font-size);
      line-height: var(--leading-snug);
    }
  `,
  template: `
    @for (cell of cells(); track cell.key) {
      <div
        class="tm-field__cell"
        [class.tm-field__cell--sm]="size() === 'sm'"
        [class.tm-field__cell--lg]="size() === 'lg'"
        [class.tm-field__cell--wide]="cell.wide"
        [attr.data-tm-field]="cell.key"
      >
        @if (loading()) {
          <!-- Loading with nothing held: a skeleton in the value box, and neither template. -->
          <dl class="tm-field__view">
            <div class="tm-field__item">
              <dt class="tm-field__label">{{ cell.label() }}</dt>
              <dd class="tm-field__box" [class.tm-field__box--multiline]="cell.multiline">
                <tm-skeleton class="tm-field__value" width="60%" />
              </dd>
              @if (hint()) {
                <dd class="tm-field__hint">{{ hint() }}</dd>
              }
            </div>
          </dl>
        } @else if (editing(cell)) {
          <!-- Edit and create mode: the editor, once the edit side loads; the value holds its place until then. -->
          @if (editor(); as component) {
            <ng-container *ngComponentOutlet="component; inputs: editorInputs(cell)" />
          } @else {
            <ng-container [ngTemplateOutlet]="valueCell" [ngTemplateOutletContext]="{ $implicit: cell, locked: false, failed: editorFailed() }" />
          }
        } @else {
          <!-- View mode, and a read-only field in edit mode, which shows its lock. -->
          <ng-container [ngTemplateOutlet]="valueCell" [ngTemplateOutletContext]="{ $implicit: cell, locked: mode() !== 'view' }" />
        }
      </div>
    }

    <!-- A cell's value: the term, the value box with the layout's own view or tm-value, and the hint. -->
    <ng-template #valueCell let-cell let-locked="locked" let-failed="failed">
      <dl class="tm-field__view">
        <div class="tm-field__item">
          <dt class="tm-field__label">{{ cell.label() }}</dt>
          <dd class="tm-field__box" tabindex="-1" [class.tm-field__box--multiline]="cell.multiline">
            @if (viewTemplate(); as view) {
              <span class="tm-field__value">
                <ng-container [ngTemplateOutlet]="view.template" [ngTemplateOutletContext]="viewContext(cell)" />
              </span>
            } @else {
              <tm-value class="tm-field__value" [property]="cell.view" [value]="valueOf(cell)" />
            }
            @if (locked) {
              <tm-icon class="tm-field__lock" [svg]="lockIcon" [label]="readOnlyText()" />
            }
          </dd>
          @if (hint()) {
            <dd class="tm-field__hint">{{ hint() }}</dd>
          }
          @if (failed) {
            <!-- The edit side failed to load: say so, and offer the load again. -->
            <dd class="tm-field__failed">
              <!-- The failure is page-wide: one field announces it, the others show it. -->
              <span [attr.role]="voiced() ? 'alert' : null">{{ editorFailedText() }}</span>
              <button tmButton class="tm-field__retry" type="button" variant="ghost" size="sm" (click)="retryEditor()">{{ retryText() }}</button>
            </dd>
          }
        </div>
      </dl>
    </ng-template>
  `,
})
export class TmField {
  readonly #entities = inject(TmEntities);
  readonly #l10n = inject(TmLocalization);
  readonly #injector = inject(Injector);
  readonly #element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  /** The record. */
  readonly #record = injectTmDetailsRecord();

  /** The card or tab around the field. */
  readonly #scope = inject(TM_DETAILS_SCOPE, { optional: true });

  /** The path, PascalCase and relative to the record, such as `Name`, `Currency` or `Customer.Code`. */
  readonly path: InputSignal<string> = input.required<string>();

  /** The label's key; the path's label when omitted. */
  readonly labelKey: InputSignal<string | undefined> = input<string | undefined>(undefined);

  /** The help text's key, shown under the field in both modes. */
  readonly hintKey: InputSignal<string | undefined> = input<string | undefined>(undefined);

  /** Reserved for a help popover; renders nothing. */
  readonly helpKey: InputSignal<string | undefined> = input<string | undefined>(undefined);

  /** The grid columns each cell takes: 2 for text that takes several lines when omitted, else 1. */
  readonly span: InputSignal<1 | 2 | undefined> = input<1 | 2 | undefined>(undefined);

  /** Whether the field stays read-only in edit mode too. */
  readonly readonly: InputSignalWithTransform<boolean, unknown> = input(false, { transform: booleanAttribute });

  /** A navigation's picker predicate, in Queryex. */
  readonly filter: InputSignal<string | null> = input<string | null>(null);

  /** Whether a navigation's picker offers Create…. */
  readonly create: InputSignalWithTransform<boolean, unknown> = input(true, { transform: booleanAttribute });

  /** The layout's own edit-mode control. */
  protected readonly editTemplate = contentChild(TmFieldEdit);

  /** The layout's own view-mode rendering. */
  protected readonly viewTemplate = contentChild(TmFieldView);

  /** The glyph of a read-only field. */
  protected readonly lockIcon = Lock;

  /** The resolved control size, which both modes draw at. */
  protected readonly size = ɵtmControlSize(signal(undefined));

  /** The record's mode. */
  protected readonly mode = this.#record.mode;

  /** Whether the record loads with nothing held: skeletons, and no template. */
  protected readonly loading = computed(() => this.#record.status() === 'loading' && !this.#record.landed());

  /** The editor's component, once any field has loaded it. */
  protected readonly editor: Signal<Type<unknown> | null> = ɵtmFieldEditorLoader.loaded.asReadonly();

  /** Whether the edit side failed to load outside view mode, so the field says so in place of a control. */
  protected readonly editorFailed = signal(false);

  /** Whether this field announces the failed load, so a screen reader hears it once. */
  protected readonly voiced = computed(() => failureVoice() === this);

  /** The failed load's message. */
  protected readonly editorFailedText = computed(() => this.#l10n.ɵtext('tm.details.editorFailed')());

  /** The failed load's retry label. */
  protected readonly retryText = computed(() => this.#l10n.ɵtext('tm.details.retry')());

  /** The accessible text of the lock. */
  protected readonly readOnlyText = computed(() => this.#l10n.ɵtext('tm.details.readOnly')());

  /** The help text, or empty. */
  protected readonly hint = computed(() => {
    const key = this.hintKey();
    return key === undefined ? '' : this.#l10n.ɵtext(key)();
  });

  /** The path's resolution against the record's entity. */
  readonly #resolution = computed(() => this.#entities.resolve(this.#record.descriptor.name, this.path()));

  /** The cells: one, or one per admitted slot of a group; none for a slot the tenant's shape leaves out. */
  protected readonly cells: Signal<readonly ɵTmFieldCell[]> = computed(() => {
    const resolution = this.#resolution();
    const path = this.path();
    if (resolution === null) {
      this.#unresolved(path);
      return [];
    }
    return this.#cellsOf(resolution);
  });

  /** Each cell's editor inputs, kept per cell so the editor is set up once. */
  readonly #editing = new Map<string, { readonly cell: ɵTmFieldCell; readonly editing: ɵTmFieldEditing }>();

  /** Sets the field up: its registrations, its errors' reports and the editor's load. */
  constructor() {
    // --- A field that leaves gives up announcing the failed load ---
    inject(DestroyRef).onDestroy(() => failureVoice.update((voice) => (voice === this ? null : voice)));

    // --- Each cell registers with the record and reports its error to the card or tab ---
    effect((onCleanup) => {
      const cells = this.cells();
      untracked(() => {
        const lifetime = Injector.create({ providers: [], parent: this.#injector });
        const destroyRef = lifetime.get(DestroyRef);
        for (const cell of cells) {
          this.#register(cell, destroyRef);
        }
        onCleanup(() => lifetime.destroy());
      });
    });

    // --- The editor loads once the record may be edited, so Edit finds it ready ---
    // Both signals are read on every run, so a failed prefetch loads again when the mode changes.
    effect(() => {
      const editable = this.#record.can.edit();
      const mode = this.#record.mode();
      if ((editable || mode !== 'view') && this.editor() === null) {
        untracked(() => void this.#loadEditor());
      }
    });
  }

  /**
   * Whether a cell shows its editor: in edit and create mode, unless it is read-only.
   *
   * @param cell - The cell.
   * @returns Whether it does.
   */
  protected editing(cell: ɵTmFieldCell): boolean {
    return this.mode() !== 'view' && !this.#locked(cell);
  }

  /** Loads the edit side again after a failed load: the failed load's Try again. */
  protected retryEditor(): void {
    void this.#loadEditor();
  }

  /**
   * Loads the edit side and takes its editor. A failure outside view mode
   * shows the failed load's message, which the next mode change, focus or
   * Try again loads past; a failed prefetch in view mode shows nothing.
   *
   * @returns Whether the editor loaded.
   */
  #loadEditor(): Promise<boolean> {
    return ɵtmFieldEditorLoader.load().then(
      (component) => {
        this.editorFailed.set(false);
        failureVoice.set(null);
        ɵtmFieldEditorLoader.loaded.set(component);
        return true;
      },
      () => {
        const failed = untracked(this.#record.mode) !== 'view';
        this.editorFailed.set(failed);
        if (failed && untracked(failureVoice) === null) {
          failureVoice.set(this);
        }
        return false;
      },
    );
  }

  /**
   * A cell's value, as the record holds it.
   *
   * @param cell - The cell.
   * @returns The value: a navigation's related row, else its id.
   */
  protected valueOf(cell: ɵTmFieldCell): unknown {
    const resolution = 'end' in cell.view ? cell.view : null;
    if (resolution !== null && resolution.steps.length > 0) {
      return this.#relatedValue(resolution);
    }
    const value = cell.json === null ? null : (this.#record.model() as TmJsonObject)[cell.json];
    if (resolution?.end.kind === 'navigation') {
      return this.#record.related.get(resolution.end.navigation.target, value as TmId | null) ?? value;
    }
    return value;
  }

  /**
   * A `tmFieldView` template's context.
   *
   * @param cell - The cell.
   * @returns The context.
   */
  protected viewContext(cell: ɵTmFieldCell): TmFieldViewContext {
    return { $implicit: this.valueOf(cell), property: cell.edits };
  }

  /**
   * A cell's editor inputs, the same object while the cell is.
   *
   * @param cell - The cell.
   * @returns The inputs.
   */
  protected editorInputs(cell: ɵTmFieldCell): Readonly<Record<string, unknown>> {
    let entry = this.#editing.get(cell.key);
    if (entry?.cell !== cell) {
      const form = this.#record.form as unknown as Readonly<Record<string, FieldTree<unknown> | undefined>>;
      const node = cell.json === null ? undefined : form[cell.json];
      entry = {
        cell,
        editing: {
          node: node ?? (this.#record.form as FieldTree<unknown>),
          hint: this.hint,
          template: computed(() => this.editTemplate()?.template ?? null) as Signal<TemplateRef<unknown> | null>,
          filter: this.filter,
          offerCreate: this.create,
          creating: computed(() => this.#record.mode() === 'create'),
        },
      };
      this.#editing.set(cell.key, entry);
    }
    return entry;
  }

  /**
   * Registers a cell with the record, and its error with the card or tab around it.
   *
   * @param cell - The cell.
   * @param destroyRef - What ends the registrations.
   */
  #register(cell: ɵTmFieldCell, destroyRef: DestroyRef): void {
    const ref: TmDetailsFieldRef = {
      labelKey: this.labelKey() ?? cell.labelKey,
      label: cell.label,
      editable: computed(() => this.editing(cell)),
      focus: () => this.#focus(cell),
    };
    this.#record.registerField(cell.key, ref, destroyRef);
    // An error shows once its field is touched or dirty, and only while the record is edited.
    // The node is looked up inside the computed, so a member that arrives with a later read is followed.
    const shows = computed(() => {
      const state = this.#node(cell)?.();
      return this.mode() !== 'view' && state !== undefined && state.invalid() && (state.touched() || state.dirty());
    });
    this.#scope?.hold(shows, destroyRef);
  }

  /**
   * Focuses a cell's control: opens its tab, expands its card, and once
   * they render, focuses the control, or the value box of a read-only cell.
   *
   * @param cell - The cell.
   */
  #focus(cell: ɵTmFieldCell): void {
    this.#scope?.reveal();
    const attempt = (retry: boolean): void => {
      afterNextRender(
        () => {
          const box = this.#element.querySelector<HTMLElement>(`[data-tm-field="${CSS.escape(cell.key)}"]`);
          const control = box?.querySelector<HTMLElement>(
            'input:not([type="hidden"]), textarea, select, button:not([disabled]):not(.tm-field__retry), [tabindex]:not([tabindex="-1"])',
          );
          if (control) {
            control.focus();
          } else if (retry && this.editing(cell) && this.editor() === null) {
            // The edit side is still loading, or failed: load it, then focus once it renders.
            void this.#loadEditor().then((loaded) => loaded && attempt(false));
          } else {
            box?.querySelector<HTMLElement>('.tm-field__box')?.focus();
          }
        },
        { injector: this.#injector },
      );
    };
    attempt(true);
  }

  /**
   * Whether a cell takes no input in edit mode: a dotted path, the
   * field's `readonly`, a value its ownership keeps from the user, one
   * the form's schema makes read-only, or a member the working copy lacks,
   * as before the record lands.
   *
   * @param cell - The cell.
   * @returns Whether it is read-only.
   */
  #locked(cell: ɵTmFieldCell): boolean {
    if (cell.json === null || cell.edits === null || this.readonly()) {
      return true;
    }
    const ownership = cell.ownership;
    if (ownership !== 'editable' && !(ownership === 'write-once' && this.mode() === 'create')) {
      return true;
    }
    return this.#node(cell)?.().readonly() ?? true;
  }

  /**
   * A cell's Signal Forms field.
   *
   * @param cell - The cell.
   * @returns The field, or `undefined` for a dotted path or a member the working copy lacks.
   */
  #node(cell: ɵTmFieldCell): FieldTree<unknown> | undefined {
    const form = this.#record.form as unknown as Readonly<Record<string, FieldTree<unknown> | undefined>>;
    return cell.json === null ? undefined : form[cell.json];
  }

  /**
   * The cells of a resolved path.
   *
   * @param resolution - The resolution.
   * @returns The cells.
   */
  #cellsOf(resolution: TmPathResolution): readonly ɵTmFieldCell[] {
    const descriptor = this.#record.descriptor;
    const custom = this.labelKey();
    const label = (own: Signal<string>): Signal<string> => (custom === undefined ? own : this.#l10n.ɵtext(custom));
    const { end } = resolution;

    // --- A path through navigations: the related projection's value, read-only ---
    if (resolution.steps.length > 0) {
      const key = custom ?? (end.kind === 'value' ? end.property.labelKey : end.kind === 'text' ? end.slots[0]!.labelKey : end.navigation.labelKey);
      return [{ ...this.#base(resolution.key, key, label(resolution.label)), view: resolution, edits: null, json: null }];
    }

    switch (end.kind) {
      // --- A scalar or a twin: the property's own value ---
      case 'value':
        return [this.#propertyCell(end.property, label(end.property.label), resolution)];

      // --- A group anchor: one cell per admitted slot, each labelled with its language ---
      case 'text': {
        const groupLabel = resolution.label;
        return end.slots.map((slot) => {
          const own = custom === undefined ? slot.label : computed(() => relabel(slot.label(), groupLabel(), this.#l10n.ɵtext(custom)()));
          return this.#propertyCell(slot, own, slot);
        });
      }

      // --- A navigation, an image or a file: the key property it writes ---
      default: {
        const key = descriptor.property(end.navigation.foreignKey);
        const cell = this.#base(end.navigation.foreignKey, custom ?? end.navigation.labelKey, label(end.navigation.label));
        return [
          {
            ...cell,
            view: resolution,
            json: key?.json ?? null,
            edits: end.kind === 'navigation' ? end.navigation : (key ?? null),
            ownership: key?.ownership ?? end.navigation.ownership,
          },
        ];
      }
    }
  }

  /**
   * A cell over one property.
   *
   * @param property - The property.
   * @param label - Its label.
   * @param view - What `tm-value` reads the value by.
   * @returns The cell.
   */
  #propertyCell(property: TmPropertyDescriptor, label: Signal<string>, view: TmPathResolution | TmPropertyDescriptor): ɵTmFieldCell {
    const multiline = ɵtmEditorKind(property) === 'textarea';
    return {
      ...this.#base(property.name, this.labelKey() ?? property.labelKey, label),
      view,
      json: property.json,
      edits: property,
      ownership: property.ownership,
      multiline,
      wide: (this.span() ?? (multiline ? 2 : 1)) === 2,
    };
  }

  /**
   * A cell's common members, read-only until a caller says otherwise.
   *
   * @param key - The registered path.
   * @param labelKey - The label's key.
   * @param label - The label.
   * @returns The members.
   */
  #base(key: string, labelKey: string, label: Signal<string>): Omit<ɵTmFieldCell, 'view' | 'json' | 'edits'> {
    return { key, labelKey, label, ownership: 'derived', multiline: false, wide: this.span() === 2 };
  }

  /**
   * The value at the end of a path through navigations, from the related rows.
   *
   * @param resolution - The path's resolution.
   * @returns The value: a navigation's related row or id, else the property's value; `null` when a step is empty.
   */
  #relatedValue(resolution: TmPathResolution): unknown {
    let row: TmJsonObject | undefined = this.#record.model() as TmJsonObject;
    let owner: TmEntityDescriptor = this.#record.descriptor;
    for (const step of resolution.steps) {
      const id = row[jsonOf(owner, step.foreignKey)] as TmId | null | undefined;
      row = this.#record.related.get(step.target, id);
      if (row === undefined) {
        return null;
      }
      owner = this.#entities.get(step.target);
    }
    const { end } = resolution;
    switch (end.kind) {
      case 'value':
        return row[end.property.json] ?? null;
      case 'text':
        return row[end.slots[0]!.json] ?? null;
      default: {
        const id = (row[jsonOf(owner, end.navigation.foreignKey)] ?? null) as TmJson;
        return end.kind === 'navigation' ? (this.#record.related.get(end.navigation.target, id as TmId | null) ?? id) : id;
      }
    }
  }

  /**
   * A path that resolves to nothing: a slot beyond the tenant's languages
   * renders nothing; any other path is a development error.
   *
   * @param path - The path.
   * @throws Error in development, for a path the entity does not have.
   */
  #unresolved(path: string): void {
    const twin = TWIN.exec(path);
    const group = twin === null ? null : this.#entities.resolve(this.#record.descriptor.name, twin[1]!);
    if (group?.end.kind === 'text' || !tmDevMode()) {
      return;
    }
    throw new Error(`tm-field's path '${path}' names nothing on ${this.#record.descriptor.name}: use a property, a navigation or a path through navigations.`);
  }
}

/**
 * A property's wire name on an entity.
 *
 * @param entity - The entity's descriptor.
 * @param name - The property's name.
 * @returns Its wire name.
 */
function jsonOf(entity: TmEntityDescriptor, name: string): string {
  return entity.property(name)?.json ?? name.charAt(0).toLowerCase() + name.slice(1);
}

/**
 * A slot's label with the group's label replaced by the layout's own:
 * "Name (ع)" under the label "Trade name" becomes "Trade name (ع)".
 *
 * @param slot - The slot's own label.
 * @param group - The group's label.
 * @param custom - The layout's label.
 * @returns The label.
 */
function relabel(slot: string, group: string, custom: string): string {
  return slot.startsWith(group) ? `${custom}${slot.slice(group.length)}` : custom;
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  Injector,
  input,
  model,
  output,
  signal,
  untracked,
  viewChild,
  type InputSignal,
  type InputSignalWithTransform,
  type ModelSignal,
  type OutputEmitterRef,
  type Signal,
} from '@angular/core';
import { FORM_FIELD, FormField, type Field, type FormValueControl, type ValidationError } from '@angular/forms/signals';
import { TmPermissions, type TmId, type TmJson } from '@tellma/core';
import { TM_UI_TRANSLATE, tmResolveFieldErrors } from '@tellma/core-ui';
import { TmEntityPicker, type TmEntityPicked } from '@tellma/core-ui/entity-picker';
import { TM_FORM_FIELD_CONTROL } from '@tellma/core-ui/form-field';

import { TmEntityPickers, type TmEntityPickerBinding } from './tm-entity-pickers';
import { ɵTmFormFieldDelegate } from './tm-form-field-delegate';

// An entity field (spec 0029 §14.1): one `tm-entity-picker` spread with the
// target's binding, a Signal Forms value control of the chosen id. It
// provides `TM_FORM_FIELD_CONTROL` as a delegate over the picker, so
// `tm-form-field` labels, describes and shows errors as for the bare picker.
// Bound to a form field, the picker binds the same field itself: its text
// that names no entity then holds the field invalid, so a save waits.

/** The field's per-instance ids, for the delegate's own element before the picker renders. */
let nextId = 0;

/**
 * Chooses one entity of a type: a picker that searches it, names its rows
 * by their display names, describes them on a second line and offers its
 * pages. Its value is the chosen id. When the user may not query the
 * entity, the field is read-only with the chosen row's name, so a value
 * the user cannot choose again is kept. The field's state and its
 * accessible name and description are forwarded to the picker.
 *
 * @example
 * ```html
 * <tm-form-field label="Parent">
 *   <tm-entity-field entity="gl.Center" [formField]="form.parentId" filter="CenterType = 'Abstract'" />
 * </tm-form-field>
 * ```
 */
@Component({
  selector: 'tm-entity-field',
  imports: [FormField, TmEntityPicker],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [{ provide: TM_FORM_FIELD_CONTROL, useFactory: () => inject(TmEntityField).ɵcontrol }],
  host: { class: 'tm-entity-field' },
  styles: `
    :host {
      display: contents;
    }
  `,
  template: `
    @let picker = binding();
    @let bound = boundField();
    @if (bound !== null && searchable()) {
      <!-- Bound: the picker binds the field too, so its unresolved text holds the field invalid -->
      <tm-entity-picker
        [formField]="bound"
        [search]="picker.search"
        [itemId]="picker.itemId"
        [itemLabel]="picker.itemLabel"
        [itemDescription]="picker.itemDescription"
        [itemLabelParts]="picker.itemLabelParts"
        [displayWith]="picker.displayWith"
        [advancedSearch]="picker.advancedSearch"
        [create]="picker.create"
        [edit]="picker.edit"
        [createLabel]="picker.createLabel"
        [editLabel]="picker.editLabel"
        [placeholder]="placeholder()"
        [aria-label]="ariaLabel()"
        [aria-describedby]="ariaDescribedby()"
        (picked)="picked.emit($event)"
      />
    } @else {
      <!-- Unbound, or an entity the user cannot search: the state forwarded by hand -->
      <tm-entity-picker
        [(value)]="value"
        [search]="picker.search"
        [itemId]="picker.itemId"
        [itemLabel]="picker.itemLabel"
        [itemDescription]="picker.itemDescription"
        [itemLabelParts]="picker.itemLabelParts"
        [displayWith]="picker.displayWith"
        [advancedSearch]="searchable() ? picker.advancedSearch : undefined"
        [create]="searchable() ? picker.create : undefined"
        [edit]="searchable() ? picker.edit : undefined"
        [createLabel]="picker.createLabel"
        [editLabel]="picker.editLabel"
        [placeholder]="placeholder()"
        [disabled]="disabled()"
        [readonly]="readonly() || !searchable()"
        [required]="required()"
        [dirty]="dirty()"
        [invalid]="invalid()"
        [touched]="touched()"
        [pending]="pending()"
        [errors]="errors()"
        [aria-label]="ariaLabel()"
        [aria-describedby]="ariaDescribedby()"
        (touch)="touch.emit()"
        (picked)="picked.emit($event)"
      />
    }
  `,
})
export class TmEntityField implements FormValueControl<TmId | null> {
  readonly #pickers = inject(TmEntityPickers);
  readonly #permissions = inject(TmPermissions);

  /** The form-field binding on this element, when a form binds it. */
  readonly #formField = inject(FORM_FIELD, { self: true, optional: true });

  /** The entity chosen from, such as `gl.Center`. */
  readonly entity: InputSignal<string> = input.required<string>();

  /** A Queryex filter applied beside the entity's declared picker filter; none when `null`. */
  readonly filter: InputSignal<string | null> = input<string | null>(null);

  /** Whether the picker offers Create, where the entity and the user's grants allow it. */
  readonly create: InputSignalWithTransform<boolean, unknown> = input(true, { transform: booleanAttribute });

  /** The chosen id; `null` for none. */
  readonly value: ModelSignal<TmId | null> = model<TmId | null>(null);

  /** Whether the field is disabled; a bound form field sets it. */
  readonly disabled: InputSignalWithTransform<boolean, unknown> = input(false, { transform: booleanAttribute });

  /** Whether the field is read-only; a bound form field sets it. */
  readonly readonly: InputSignalWithTransform<boolean, unknown> = input(false, { transform: booleanAttribute });

  /** Whether a value is required; a bound form field sets it. */
  readonly required: InputSignalWithTransform<boolean, unknown> = input(false, { transform: booleanAttribute });

  /** Whether the value was changed; a bound form field sets it. */
  readonly dirty: InputSignalWithTransform<boolean, unknown> = input(false, { transform: booleanAttribute });

  /** Whether the value is invalid; a bound form field sets it. */
  readonly invalid: InputSignalWithTransform<boolean, unknown> = input(false, { transform: booleanAttribute });

  /** Whether the field was visited; a bound form field sets it. */
  readonly touched: InputSignalWithTransform<boolean, unknown> = input(false, { transform: booleanAttribute });

  /** Whether the value's validation is pending; a bound form field sets it. */
  readonly pending: InputSignalWithTransform<boolean, unknown> = input(false, { transform: booleanAttribute });

  /** The value's errors; a bound form field sets them. */
  readonly errors: InputSignal<readonly ValidationError.WithOptionalFieldTree[]> = input<readonly ValidationError.WithOptionalFieldTree[]>([]);

  /** The picker's placeholder. */
  readonly placeholder: InputSignal<string> = input('');

  /** The picker's accessible name, when no `tm-form-field` labels it. */
  readonly ariaLabel: InputSignal<string | null> = input<string | null>(null, { alias: 'aria-label' });

  /** Ids of elements that describe the picker, beside the form field's. */
  readonly ariaDescribedby: InputSignal<string | null> = input<string | null>(null, { alias: 'aria-describedby' });

  /** Each pick, with its row and how it was made. */
  readonly picked: OutputEmitterRef<TmEntityPicked<TmJson, TmId>> = output<TmEntityPicked<TmJson, TmId>>();

  /** The user left the field, so a form marks it touched. */
  readonly touch: OutputEmitterRef<void> = output<void>();

  /** The picker, once rendered. */
  private readonly picker = viewChild(TmEntityPicker);

  /** The picker's form-field control, once rendered. */
  private readonly inner = viewChild(TM_FORM_FIELD_CONTROL);

  /** The field's binding, made again when the entity or the Create option changes. */
  protected readonly binding: Signal<TmEntityPickerBinding> = computed(() => {
    const entity = this.entity();
    const create = this.create();
    return untracked(() => this.#pickers.binding(entity, { filter: this.filter, create }));
  });

  /** Whether the user may search the entity; else the field is read-only. */
  protected readonly searchable: Signal<boolean> = computed(() => this.#permissions.allows(this.entity(), 'query')());

  /** The form field bound to this element, which the picker binds too; `null` when unbound. */
  protected readonly boundField: Signal<Field<TmId | null> | null> = computed(() => (this.#formField?.field() as Field<TmId | null> | undefined) ?? null);

  /**
   * The control `tm-form-field` labels: the picker, once rendered.
   * Internal: the field provides it as its form-field control.
   */
  readonly ɵcontrol: ɵTmFormFieldDelegate = new ɵTmFormFieldDelegate(
    this.inner,
    {
      controlId: signal(`tm-entity-field-${nextId++}`).asReadonly(),
      state: computed(() => this),
      errors: tmResolveFieldErrors(this.errors, inject(TM_UI_TRANSLATE)),
      local: signal([]).asReadonly(),
      busy: signal(false).asReadonly(),
    },
    inject(Injector),
  );

  /**
   * Moves focus to the picker's input; a form's `focus()` lands here.
   *
   * @param options - The browser's focus options.
   */
  focus(options?: FocusOptions): void {
    this.picker()?.focus(options);
  }
}

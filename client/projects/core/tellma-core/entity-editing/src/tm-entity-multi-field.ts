// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
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
import { requiredError, transformedValue, type FormValueControl, type ValidationError } from '@angular/forms/signals';
import { TmPermissions, type TmId, type TmJson } from '@tellma/core';
import { TM_UI_TRANSLATE, tmResolveFieldErrors } from '@tellma/core-ui';
import { TmEntityMultiPicker, type TmEntityPicked } from '@tellma/core-ui/entity-picker';
import { TM_FORM_FIELD_CONTROL } from '@tellma/core-ui/form-field';

import { TmEntityPickers, type TmEntityPickerBinding } from './tm-entity-pickers';
import { ɵTmFormFieldDelegate } from './tm-form-field-delegate';

// An entity list field (spec 0029 §14.1): one `tm-entity-multi-picker`
// spread with the target's binding, a Signal Forms value control of the
// chosen ids. Signal Forms' `required()` counts an empty list as a value,
// so the field reports a required, empty list itself: as the one error a
// control can add to its field, a parse error, asserted again whenever the
// value, the requirement or the form's reset would clear it.

/** The field's per-instance ids, for the delegate's own element before the picker renders. */
let nextId = 0;

/**
 * Chooses several entities of a type: a multi-picker that searches it,
 * shows the chosen rows as chips and offers its advanced search over the
 * whole selection. Its value is the chosen ids, in pick order. A required
 * field holds an empty list invalid. When the user may not query the
 * entity, the field is read-only with the chosen rows' names.
 *
 * @example
 * ```html
 * <tm-form-field label="Centers">
 *   <tm-entity-multi-field entity="gl.Center" [formField]="form.centerIds" [maxItems]="50" />
 * </tm-form-field>
 * ```
 */
@Component({
  selector: 'tm-entity-multi-field',
  imports: [TmEntityMultiPicker],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [{ provide: TM_FORM_FIELD_CONTROL, useFactory: () => inject(TmEntityMultiField).ɵcontrol }],
  host: { class: 'tm-entity-multi-field' },
  styles: `
    :host {
      display: contents;
    }
  `,
  template: `
    @let picker = binding();
    <!-- The field's state, forwarded; the multi-picker raises no error of its own to bind a form for -->
    <tm-entity-multi-picker
      [(value)]="value"
      [search]="picker.search"
      [itemId]="picker.itemId"
      [itemLabel]="picker.itemLabel"
      [itemDescription]="picker.itemDescription"
      [itemLabelParts]="picker.itemLabelParts"
      [displayWith]="picker.displayWith"
      [advancedSearch]="searchable() ? picker.advancedSearch : undefined"
      [create]="searchable() ? picker.create : undefined"
      [createLabel]="picker.createLabel"
      [maxItems]="maxItems()"
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
      (removed)="removed.emit($event)"
    />
  `,
})
export class TmEntityMultiField implements FormValueControl<readonly TmId[]> {
  readonly #pickers = inject(TmEntityPickers);
  readonly #permissions = inject(TmPermissions);

  /** The entity chosen from, such as `gl.Center`. */
  readonly entity: InputSignal<string> = input.required<string>();

  /** A Queryex filter applied beside the entity's declared picker filter; none when `null`. */
  readonly filter: InputSignal<string | null> = input<string | null>(null);

  /** Whether the picker offers Create, where the entity and the user's grants allow it. */
  readonly create: InputSignalWithTransform<boolean, unknown> = input(true, { transform: booleanAttribute });

  /** The chosen ids, in pick order. */
  readonly value: ModelSignal<readonly TmId[]> = model<readonly TmId[]>([]);

  /** The most entities the field takes; unlimited when omitted. */
  readonly maxItems: InputSignal<number | undefined> = input<number | undefined>(undefined);

  /** Whether the field is disabled; a bound form field sets it. */
  readonly disabled: InputSignalWithTransform<boolean, unknown> = input(false, { transform: booleanAttribute });

  /** Whether the field is read-only; a bound form field sets it. */
  readonly readonly: InputSignalWithTransform<boolean, unknown> = input(false, { transform: booleanAttribute });

  /** Whether at least one entity is required; a bound form field sets it, and an empty list is then invalid. */
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

  /** Each addition, with its row and how it was made. */
  readonly picked: OutputEmitterRef<TmEntityPicked<TmJson, TmId>> = output<TmEntityPicked<TmJson, TmId>>();

  /** Each removal, by id. */
  readonly removed: OutputEmitterRef<TmId> = output<TmId>();

  /** The user left the field, so a form marks it touched. */
  readonly touch: OutputEmitterRef<void> = output<void>();

  /** The multi-picker, once rendered. */
  private readonly picker = viewChild(TmEntityMultiPicker);

  /** The multi-picker's form-field control, once rendered. */
  private readonly inner = viewChild(TM_FORM_FIELD_CONTROL);

  /** Moves on each form reset, which clears the required error until it is asserted again. */
  readonly #resets = signal(0);

  /**
   * The required check, as the field's parse error: an empty list while
   * required. It never writes the value.
   */
  readonly #required = transformedValue<readonly TmId[], readonly TmId[]>(this.value, {
    parse: (ids) => ({ error: untracked(this.required) && ids.length === 0 ? requiredError() : undefined }),
    format: (ids) => ids,
  });

  /** The field's binding, made again when the entity or the Create option changes. */
  protected readonly binding: Signal<TmEntityPickerBinding> = computed(() => {
    const entity = this.entity();
    const create = this.create();
    return untracked(() => this.#pickers.binding(entity, { filter: this.filter, create, multiple: true }));
  });

  /** Whether the user may search the entity; else the field is read-only. */
  protected readonly searchable: Signal<boolean> = computed(() => this.#permissions.allows(this.entity(), 'query')());

  /**
   * The control `tm-form-field` labels: the multi-picker, once rendered.
   * Internal: the field provides it as its form-field control.
   */
  readonly ɵcontrol: ɵTmFormFieldDelegate = new ɵTmFormFieldDelegate(
    this.inner,
    {
      controlId: signal(`tm-entity-multi-field-${nextId++}`).asReadonly(),
      state: computed(() => this),
      errors: tmResolveFieldErrors(this.errors, inject(TM_UI_TRANSLATE)),
      local: signal([]).asReadonly(),
      busy: signal(false).asReadonly(),
    },
    inject(Injector),
  );

  constructor() {
    // --- The required check, asserted again after a change of value, requirement or a reset ---
    // A new value clears a control's parse errors, and so does a reset; the
    // check runs after either, so a required, empty list stays invalid.
    effect(() => {
      const ids = this.value();
      this.required();
      this.#resets();
      untracked(() => this.#required.set(ids));
    });
  }

  /**
   * Moves focus to the multi-picker's input; a form's `focus()` lands here.
   *
   * @param options - The browser's focus options.
   */
  focus(options?: FocusOptions): void {
    this.picker()?.focus(options);
  }

  /** Asserts the required check again after a form's reset cleared it; a bound form field calls it. */
  reset(): void {
    this.#resets.update((count) => count + 1);
  }
}

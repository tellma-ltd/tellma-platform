// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, effect, signal, untracked, type Injector, type Signal } from '@angular/core';
import type { TmFieldError, TmFormFieldControl } from '@tellma/core-ui/contracts';

// A form-field control that stands for the control a wrapper renders inside
// its own view. `tm-form-field` finds its control as content, so a wrapper
// such as `tm-value-editor` provides this delegate in its place: the field's
// label, hint and error ids reach the inner control once it exists, and the
// control's state reaches the field. A wrapper that renders no labelable
// control of its own, such as an image editor, is the control itself: a
// labelled group with the bound field's state.

/** The state a wrapper reports when it renders no inner control, and beside the one it does. */
export interface ɵTmFormFieldDelegateOwn {
  /** The id of the wrapper's own element, labelled when there is no inner control. */
  readonly controlId: Signal<string>;
  /** The bound field's state. */
  readonly state: Signal<{
    readonly required: Signal<boolean>;
    readonly disabled: Signal<boolean>;
    readonly readonly: Signal<boolean>;
    readonly touched: Signal<boolean>;
    readonly dirty: Signal<boolean>;
    readonly invalid: Signal<boolean>;
    readonly pending: Signal<boolean>;
  }>;
  /** The bound field's errors, localized. */
  readonly errors: Signal<readonly TmFieldError[]>;
  /** The wrapper's own errors, such as a refused upload, shown first and marking the field invalid. */
  readonly local: Signal<readonly TmFieldError[]>;
  /** Whether the wrapper is busy, such as with an upload. */
  readonly busy: Signal<boolean>;
}

/**
 * Stands for the control a wrapper renders, in `tm-form-field`. Internal:
 * the value editors of this entry point provide it as their
 * `TM_FORM_FIELD_CONTROL`, and so does the details kit's group around a
 * layout's own control.
 */
export class ɵTmFormFieldDelegate implements TmFormFieldControl {
  readonly #inner: Signal<TmFormFieldControl | undefined>;
  readonly #own: ɵTmFormFieldDelegateOwn;

  /** The field's hint and error ids, held until an inner control takes them. */
  readonly #describedBy = signal<readonly string[]>([]);

  /** The field's label id, held for the inner control, or labelling the wrapper. */
  readonly #labelId = signal<string | null>(null);

  /** Whether the field shows an error, held for the inner control. */
  readonly #fieldError = signal(false);

  /** The id of the element that labels the wrapper itself, when there is no inner control. */
  readonly labelledBy: Signal<string | null> = computed(() => (this.#inner() === undefined ? this.#labelId() : null));

  /** The field's hint and error ids, for the wrapper's own element. */
  readonly ownDescribedBy: Signal<string | null> = computed(() => (this.#inner() === undefined ? this.#describedBy().join(' ') || null : null));

  /**
   * A delegate over a wrapper's inner control.
   *
   * @param inner - The inner control, once the wrapper's view has one.
   * @param own - The wrapper's own state.
   * @param injector - The wrapper's injector, which runs the forwarding.
   */
  constructor(inner: Signal<TmFormFieldControl | undefined>, own: ɵTmFormFieldDelegateOwn, injector: Injector) {
    this.#inner = inner;
    this.#own = own;

    // --- What the field gave reaches each inner control as it appears ---
    effect(
      () => {
        const control = this.#inner();
        const ids = this.#describedBy();
        const label = this.#labelId();
        const error = this.#fieldError();
        untracked(() => {
          control?.setDescribedByIds(ids);
          control?.setLabelId?.(label);
          control?.setFieldError?.(error);
        });
      },
      { injector },
    );
  }

  /** {@inheritDoc TmFormFieldControl.controlId} */
  readonly controlId: Signal<string> = computed(() => this.#inner()?.controlId() ?? this.#own.controlId());

  /** {@inheritDoc TmFormFieldControl.describedByIds} */
  readonly describedByIds: Signal<readonly string[]> = computed(() => this.#inner()?.describedByIds() ?? this.#describedBy());

  /** {@inheritDoc TmFormFieldControl.ownsChrome} */
  get ownsChrome(): boolean {
    // With no inner control, the wrapper draws its own group and needs no box.
    return this.#inner()?.ownsChrome ?? true;
  }

  /** {@inheritDoc TmFormFieldControl.ownsErrorIcon} */
  get ownsErrorIcon(): boolean {
    return this.#inner()?.ownsErrorIcon ?? false;
  }

  /**
   * Present when the field labels its control through `aria-labelledby`:
   * for an inner control that is labelled so, and for the wrapper's own
   * group; absent for an inner native input, which the field's
   * `<label for>` labels.
   */
  get setLabelId(): ((id: string | null) => void) | undefined {
    const control = this.#inner();
    return control === undefined || control.setLabelId !== undefined ? (id) => this.#labelId.set(id) : undefined;
  }

  /** {@inheritDoc TmFormFieldControl.setDescribedByIds} */
  setDescribedByIds(ids: readonly string[]): void {
    this.#describedBy.set(ids);
  }

  /** {@inheritDoc TmFormFieldControl.setFieldError} */
  setFieldError(showsError: boolean): void {
    this.#fieldError.set(showsError);
  }

  /** {@inheritDoc TmFormFieldControl.onContainerClick} */
  onContainerClick(): void {
    this.#inner()?.onContainerClick?.();
  }

  /** {@inheritDoc TmFormFieldControl.required} */
  readonly required: Signal<boolean> = computed(() => this.#inner()?.required() ?? this.#own.state().required());

  /** {@inheritDoc TmFormFieldControl.disabled} */
  readonly disabled: Signal<boolean> = computed(() => this.#inner()?.disabled() ?? this.#own.state().disabled());

  /** {@inheritDoc TmFormFieldControl.readonly} */
  readonly readonly: Signal<boolean> = computed(() => this.#inner()?.readonly() ?? this.#own.state().readonly());

  /** {@inheritDoc TmFormFieldControl.touched} */
  readonly touched: Signal<boolean> = computed(
    () => this.#own.local().length > 0 || (this.#inner()?.touched() ?? this.#own.state().touched()),
  );

  /** {@inheritDoc TmFormFieldControl.dirty} */
  readonly dirty: Signal<boolean> = computed(() => this.#inner()?.dirty() ?? this.#own.state().dirty());

  /** {@inheritDoc TmFormFieldControl.invalid} */
  readonly invalid: Signal<boolean> = computed(
    () => this.#own.local().length > 0 || (this.#inner()?.invalid() ?? this.#own.state().invalid()),
  );

  /** {@inheritDoc TmFormFieldControl.pending} */
  readonly pending: Signal<boolean> = computed(() => this.#own.busy() || (this.#inner()?.pending() ?? this.#own.state().pending()));

  /** {@inheritDoc TmFormFieldControl.localizedErrors} */
  readonly localizedErrors: Signal<readonly TmFieldError[]> = computed(() => [
    ...this.#own.local(),
    ...(this.#inner()?.localizedErrors() ?? this.#own.errors()),
  ]);
}

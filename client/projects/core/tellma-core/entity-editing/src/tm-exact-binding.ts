// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Directive, model, type ModelSignal } from '@angular/core';
import { transformedValue, type FormValueControl, type ValidationError } from '@angular/forms/signals';
import { TmDecimals, type TmDecimal } from '@tellma/core';
import type { ɵTmExactParse } from '@tellma/core/entity';

// The field binding of decimals and longs edited as numbers (spec 0029
// §12.2: "the tmNumber control converts at the binding"): what the edit
// text reads as, by /entity's exact parse, reaches the Signal Forms field
// here, so a text that reads as no number holds the form's save.

/** What the editor hands its field: a value to write, or an error that keeps the value as it is. */
interface TmExactEntry {
  /** The value to write; absent to keep the field's value. */
  readonly value?: TmDecimal | null;
  /** The error that refuses the text; absent once the text reads. */
  readonly error?: ValidationError.WithoutFieldTree;
}

/**
 * The field binding of a decimal or a long edited as a number: bound to
 * the field with `[formField]`, it writes the editor's values and carries
 * its parse errors into the field, so a form holds its save while the
 * text reads as no value. Internal to the value editor.
 */
@Directive({ selector: '[tmExactBinding]' })
export class ɵTmExactBinding implements FormValueControl<unknown> {
  /** The field's value. */
  readonly value: ModelSignal<unknown> = model<unknown>(null);

  /** The binding's parse, whose errors the field reports. */
  readonly #entry = transformedValue<unknown, TmExactEntry>(this.value, {
    parse: (entry) => (entry.error !== undefined ? { error: entry.error } : entry.value === undefined ? {} : { value: entry.value }),
    format: () => ({}),
  });

  /**
   * Reports what the edit text read as: a value differing from the field's
   * is written; an equal one only clears the error; an error keeps the
   * field's value and holds the field invalid.
   *
   * @param parsed - What the text read as.
   */
  write(parsed: ɵTmExactParse): void {
    if ('error' in parsed) {
      this.#entry.set({ error: parsed.error });
      return;
    }
    const current = this.value();
    const same =
      parsed.value === null
        ? current === null || current === undefined || current === ''
        : TmDecimals.isDecimal(current) && TmDecimals.compare(current, parsed.value) === 0;
    this.#entry.set(same ? {} : { value: parsed.value });
  }
}

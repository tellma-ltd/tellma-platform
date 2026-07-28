// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Cross-cutting contracts of the Tellma UI library.
 *
 * This entry point is types + pure helpers ONLY: no Angular, no other
 * @tellma packages, no i18n (enforced by lint) — so the future data grid can
 * depend on it without pulling in the components.
 */

/**
 * Read channel a host uses to drive a control it owns — an Angular signal
 * satisfies it structurally.
 */
export type SignalLike<T> = () => T;

/** Read + write channel (the grid owns a cell's value and passes it in). */
export interface WritableSignalLike<T> extends SignalLike<T> {
  /** Replaces the current value. */
  set(value: T): void;
  /** Derives the next value from the previous one. */
  update(fn: (prev: T) => T): void;
}

/**
 * A field error as surfaced to `tm-form-field`: `kind` mirrors the framework
 * ValidationError's `kind` one-for-one ('required', 'minLength', 'email', …
 * camelCase per Signal Forms — NOT reactive forms' 'minlength'); `message` is
 * the human-readable, ALREADY-LOCALIZED text (resolved by the control through
 * the message resolver — the wrapper only decides whether to show it).
 */
export interface TmFieldError {
  /** The framework error kind, camelCase (e.g. 'required', 'minLength'). */
  readonly kind: string;
  /** The human-readable, already-localized message text. */
  readonly message: string;
}

/**
 * What `tm-form-field` needs to do its job (the MatFormFieldControl seam
 * adapted to Signal Forms). The control re-surfaces the
 * Signal Forms field state it receives via [formField] so the wrapper can
 * apply the display policy and render the localized error text — the FULL
 * state set, not just `invalid`.
 */
export interface TmFormFieldControl {
  /**
   * Id of the actual control element (the <input>), so <label for> targets
   * it and aria wiring resolves.
   */
  readonly controlId: SignalLike<string>;
  /**
   * true = the control renders its own adornment chrome (tm-checkbox,
   * tm-select); false = the field wraps the control in the shared bordered
   * box (tmInput).
   */
  readonly ownsChrome: boolean;
  /**
   * Every id the control exposes via aria-describedby: author-supplied ids
   * first, then the ids the field pushed via `setDescribedByIds`.
   */
  readonly describedByIds: SignalLike<readonly string[]>;
  /**
   * Field pushes its hint/error element ids; the control MERGES them after
   * any author-supplied ids and writes the union into aria-describedby —
   * an author's `aria-describedby` is never clobbered.
   */
  setDescribedByIds(ids: readonly string[]): void;
  /**
   * For controls whose focusable host is NOT labelable (tm-select's <div>
   * trigger): the field passes its <label> id, the control binds
   * aria-labelledby; native-input hosts omit this — <label for> does the
   * job.
   */
  setLabelId?(id: string | null): void;
  // Field state, mirrored from the bound Field (all read-only to the wrapper):
  /** Whether the field is required, mirrored from the bound field. */
  readonly required: SignalLike<boolean>;
  /** Whether the field is disabled, mirrored from the bound field. */
  readonly disabled: SignalLike<boolean>;
  /** Whether the field is readonly, mirrored from the bound field. */
  readonly readonly: SignalLike<boolean>;
  /** Whether the user has blurred the field, mirrored from the bound field. */
  readonly touched: SignalLike<boolean>;
  /** Whether the user has changed the value, mirrored from the bound field. */
  readonly dirty: SignalLike<boolean>;
  /** Whether the field fails validation, mirrored from the bound field. */
  readonly invalid: SignalLike<boolean>;
  /** Async validation in progress. */
  readonly pending: SignalLike<boolean>;
  /**
   * Already-localized messages (resolved through the message resolver).
   *
   * NOTE — named `localizedErrors` rather than `errors`: the control must
   * also declare the framework's `errors` INPUT (raw ValidationError[],
   * bound by [formField]), and one class member cannot carry both types.
   * The seam is otherwise unchanged: the wrapper reads already-localized
   * text and only decides WHETHER to show it.
   */
  readonly localizedErrors: SignalLike<readonly TmFieldError[]>;
  /**
   * Optional: field calls this when the user clicks the container chrome
   * (padding/border, not the input itself) so the control focuses itself.
   */
  onContainerClick?(): void;
}


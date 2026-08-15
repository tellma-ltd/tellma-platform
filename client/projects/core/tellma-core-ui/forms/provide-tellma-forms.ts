// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  InjectionToken,
  makeEnvironmentProviders,
  type EnvironmentProviders,
} from '@angular/core';

/** The field-state inputs the error-display policy decides over. */
export interface TmErrorDisplayState {
  /** The field currently fails at least one validator. */
  readonly invalid: boolean;
  /** The user has blurred the field at least once. */
  readonly touched: boolean;
  /** The user has changed the field's value. */
  readonly dirty: boolean;
  /** Async validation is in progress. */
  readonly pending: boolean;
}

/** Decides whether a field's errors are shown (field-scoped). */
export type TmErrorDisplayPolicy = (state: TmErrorDisplayState) => boolean;

/**
 * Default policy: show errors when invalid AND (touched OR dirty). "Show
 * after a submit attempt" needs no extra plumbing — Signal Forms' `submit()`
 * marks every descendant touched before validating, so this policy surfaces
 * every error then. While async validation is pending, errors are held.
 */
export const tmDefaultErrorDisplay: TmErrorDisplayPolicy = (state) =>
  !state.pending && state.invalid && (state.touched || state.dirty);

/**
 * The active error-display policy. Defaults to `tmDefaultErrorDisplay`;
 * customized via `provideTellmaForms({ errorDisplay })`.
 */
export const TM_ERROR_DISPLAY = new InjectionToken<TmErrorDisplayPolicy>('TM_ERROR_DISPLAY', {
  providedIn: 'root',
  factory: () => tmDefaultErrorDisplay,
});

/** The height/type/padding step a control renders at. */
export type TmControlSize = 'sm' | 'md' | 'lg';

/**
 * Workspace-wide defaults for every control that sits on the field size
 * ladder — form fields, buttons, selects, entity pickers and data grids.
 */
export interface TmFormFieldDefaults {
  /** The size step controls use when they do not set one. */
  readonly size: TmControlSize;
  /** The visual required marker; announced via the localized string. */
  readonly requiredMarker: string;
}

/** The shipped default size step. */
const DEFAULT_SIZE: TmControlSize = 'sm';

/**
 * The workspace-wide control defaults. Customized via
 * `provideTellmaForms({ formFieldDefaults })`.
 *
 * The default size is 'sm'. An ERP is read, not browsed: its screens are
 * dense forms and long tables, and the number of rows on screen at once is
 * a functional property of them, not a matter of taste. 'md' and 'lg' stay
 * available per control, and a distribution that wants a roomier default
 * everywhere changes it once here.
 */
export const TM_FORM_FIELD_DEFAULTS = new InjectionToken<TmFormFieldDefaults>(
  'TM_FORM_FIELD_DEFAULTS',
  {
    providedIn: 'root',
    factory: () => ({ size: DEFAULT_SIZE, requiredMarker: '*' }),
  },
);

/** Options for `provideTellmaForms()` (also accepted via `provideTellmaUi({ forms })`). */
export interface TmFormsOptions {
  /** Replaces the default error-display policy. */
  readonly errorDisplay?: TmErrorDisplayPolicy;
  /** Overrides individual form-field defaults; omitted keys keep the defaults. */
  readonly formFieldDefaults?: Partial<TmFormFieldDefaults>;
}

/**
 * Forms-only providers: the error-display policy, the validation-message
 * resolution defaults, and form-field defaults. Composed by
 * `provideTellmaUi()`; call directly only to customize forms behavior without
 * the umbrella.
 */
export function provideTellmaForms(options: TmFormsOptions = {}): EnvironmentProviders {
  return makeEnvironmentProviders([
    ...(options.errorDisplay
      ? [{ provide: TM_ERROR_DISPLAY, useValue: options.errorDisplay }]
      : []),
    ...(options.formFieldDefaults
      ? [
          {
            provide: TM_FORM_FIELD_DEFAULTS,
            useValue: {
              size: options.formFieldDefaults.size ?? DEFAULT_SIZE,
              requiredMarker: options.formFieldDefaults.requiredMarker ?? '*',
            } satisfies TmFormFieldDefaults,
          },
        ]
      : []),
  ]);
}

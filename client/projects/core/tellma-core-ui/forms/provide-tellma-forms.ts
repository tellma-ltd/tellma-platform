import {
  InjectionToken,
  makeEnvironmentProviders,
  type EnvironmentProviders,
} from '@angular/core';

/** The field-state inputs the error-display policy decides over (§5). */
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

/** Decides whether a field's errors are shown (field-scoped — §5). */
export type TmErrorDisplayPolicy = (state: TmErrorDisplayState) => boolean;

/**
 * Default policy: show errors when invalid AND (touched OR dirty). "Show
 * after a submit attempt" needs no extra plumbing — Signal Forms' `submit()`
 * marks every descendant touched before validating, so this policy surfaces
 * every error then. While async validation is pending, errors are held (§5).
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

/** Workspace-wide form-field defaults (§5). */
export interface TmFormFieldDefaults {
  /** The height/density variant fields use when they do not set one. */
  readonly size: 'sm' | 'md' | 'lg';
  /** The visual required marker; announced via the localized string. */
  readonly requiredMarker: string;
}

/**
 * The workspace-wide form-field defaults. Defaults to size 'md' with a '*'
 * marker; customized via `provideTellmaForms({ formFieldDefaults })`.
 */
export const TM_FORM_FIELD_DEFAULTS = new InjectionToken<TmFormFieldDefaults>(
  'TM_FORM_FIELD_DEFAULTS',
  {
    providedIn: 'root',
    factory: () => ({ size: 'md', requiredMarker: '*' }),
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
 * Forms-only providers (§5): the error-display policy, the validation-message
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
              size: options.formFieldDefaults.size ?? 'md',
              requiredMarker: options.formFieldDefaults.requiredMarker ?? '*',
            } satisfies TmFormFieldDefaults,
          },
        ]
      : []),
  ]);
}

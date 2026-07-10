/**
 * The built-in ENGLISH library strings — the only locale that ships in the
 * core. Every other locale (Arabic included) ships as an optional
 * per-distribution locale pack contributing its strings as a Transloco
 * scope. Plurals use ICU MessageFormat (via @jsverse/transloco-messageformat).
 *
 * Error keys mirror Signal Forms' camelCase error `kind`s one-for-one.
 */
export const TM_UI_STRINGS_EN = {
  errors: {
    required: 'This field is required',
    email: 'Enter a valid email address',
    minLength: 'Enter at least {minLength, plural, one {# character} other {# characters}}',
    maxLength: 'Enter no more than {maxLength, plural, one {# character} other {# characters}}',
    min: 'Enter a value of at least {min}',
    max: 'Enter a value of at most {max}',
    pattern: 'The value does not match the expected format',
    minDate: 'Enter a date on or after {minDate}',
    maxDate: 'Enter a date on or before {maxDate}',
  },
  select: {
    placeholder: 'Select an option',
  },
} as const;

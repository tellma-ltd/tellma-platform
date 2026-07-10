// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, type Signal } from '@angular/core';
import type { ValidationError } from '@angular/forms/signals';

import type { TmFieldError } from '@tellma/core-ui/contracts';

import type { TmUiTranslateFn } from '../i18n/tm-ui-translate';

/**
 * Extracts the typed params a framework error carries (minLength → the
 * required length, min/max → the bound, …) so the localized default can
 * interpolate them. Everything except the error envelope fields is a
 * param.
 */
export function tmErrorParams(error: ValidationError): Record<string, unknown> {
  const params: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(error)) {
    if (key === 'kind' || key === 'message' || key === 'fieldTree' || key === 'formField') {
      continue;
    }
    params[key] = value;
  }
  return params;
}

/**
 * The validation-message resolver. Message precedence: a schema-inline
 * message (the `{message: …}` passed to a validator, surfaced as the
 * framework error's own `message`) wins when present; otherwise the error's
 * camelCase `kind` maps to a localized default via `TM_UI_TRANSLATE`
 * (`errors.<kind>`). The result is reactive: switching the active locale
 * recomputes every message.
 */
export function tmResolveFieldErrors(
  errors: Signal<readonly ValidationError.WithOptionalFieldTree[]>,
  translate: TmUiTranslateFn,
): Signal<readonly TmFieldError[]> {
  return computed(() =>
    errors().map((error) => ({
      kind: error.kind,
      message:
        error.message ?? translate(`errors.${error.kind}`, tmErrorParams(error))(),
    })),
  );
}

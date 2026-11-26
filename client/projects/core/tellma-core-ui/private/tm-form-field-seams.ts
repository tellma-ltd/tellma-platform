// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { InjectionToken } from '@angular/core';

import type { TmFormFieldControl } from '@tellma/core-ui/contracts';

/**
 * A control projects itself into `tm-form-field` by providing this token
 * (`providers: [{ provide: TM_FORM_FIELD_CONTROL, useExisting: … }]`) — the
 * MatFormFieldControl seam adapted to Signal Forms.
 *
 * The token lives here, apart from the field, so a control that only provides
 * it does not load the field with it; `@tellma/core-ui/form-field` exports it
 * as `TM_FORM_FIELD_CONTROL`.
 */
export const ɵTM_FORM_FIELD_CONTROL = new InjectionToken<TmFormFieldControl>(
  'TM_FORM_FIELD_CONTROL',
);

/**
 * Provided by every `tm-form-field` for the controls projected into it; a
 * control injects it optionally to learn whether a field encloses it without
 * importing the field.
 */
export const ɵTM_FORM_FIELD_HOST = new InjectionToken<object>('TM_FORM_FIELD_HOST');

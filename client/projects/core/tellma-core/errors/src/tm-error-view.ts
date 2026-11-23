// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Signal } from '@angular/core';
import type { TmApiError } from '@tellma/core';

// The error view (spec 0024 §11): what the presenter makes of an error, and
// what the inline state and the dialogs render. The presenter describes; the
// inline state runs a view's actions itself, so the error each view came
// from is kept here, where both read it.

/** What an error view offers. */
export type TmErrorAction = 'retry' | 'reload' | 'reload-anyway' | 'sign-in' | 'continue' | 'switch-tenant' | 'close';

/** An error as the user sees it: its tone and icon, the words, the server's items, the reference and the actions. */
export interface TmErrorView {
  /** The tone. */
  readonly tone: 'error' | 'warning' | 'info';
  /** The icon, by registry name. */
  readonly icon: string;
  /** The title. */
  readonly title: Signal<string>;
  /** The message; empty when the row has none. */
  readonly message: Signal<string>;
  /** The server's errors, each in the user's language. */
  readonly items: Signal<readonly string[]>;
  /** The reference support asks for, or `null`. */
  readonly traceId: string | null;
  /**
   * For a `forbidden` refusal, the resource and action it names, raw, as
   * support reads them, such as `acme.SalesInvoice · Save`; otherwise `null`.
   */
  readonly permission: string | null;
  /** What the view offers, the main action first. */
  readonly actions: Signal<readonly TmErrorAction[]>;
}

/** Where an error happened. */
export interface TmErrorContext {
  /** What failed: a read, a save, or another action. */
  readonly kind: 'read' | 'save' | 'action';
  /** The entity the call concerned, such as `gl.Center`: the root of a refused path, and the not-found view's noun. */
  readonly entity?: string;
  /** The tenant the call went to; the routed tenant when omitted. */
  readonly tenantId?: number;
}

/** The error each view was described from, for the actions the inline state runs, such as a step-up's Continue. */
export const TM_VIEW_ERRORS = new WeakMap<TmErrorView, TmApiError>();

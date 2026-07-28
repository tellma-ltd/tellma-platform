// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable, type Signal } from '@angular/core';

import { tmFormatNumber, type TmNumberFormatOptions } from '@tellma/core-ui/l10n';

import { TM_ACTIVE_LOCALE } from '../i18n/tm-active-locale';

/**
 * The reactive formatting facade: the pure `@tellma/core-ui/l10n` functions
 * bound to the app-ambient locale signal, so calling a method inside a
 * `computed()` or a template makes the caller re-render when the user
 * switches language at runtime.
 *
 * No pipes on purpose: a pure pipe would not re-evaluate on a locale
 * change and an impure one re-runs every change-detection pass — the
 * signal-bound facade gives the reactivity without the cost. For
 * locale-explicit work (exports, tests) call the `l10n` functions
 * directly.
 */
@Injectable({ providedIn: 'root' })
export class TmL10n {
  /** The app-ambient formatting locale (see `TM_ACTIVE_LOCALE`). */
  readonly locale: Signal<string> = inject(TM_ACTIVE_LOCALE);

  /**
   * Formats a number in the active locale — reading `locale` first, so a
   * reactive caller re-renders on language switch. Delegates to
   * `tmFormatNumber`.
   */
  formatNumber(value: unknown, options?: TmNumberFormatOptions): string {
    return tmFormatNumber(value, this.locale(), options);
  }
}

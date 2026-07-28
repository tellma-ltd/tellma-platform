// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, inject, InjectionToken, LOCALE_ID, signal, type Signal } from '@angular/core';
import { TRANSLOCO_TRANSPILER, TranslocoService } from '@jsverse/transloco';

/**
 * The default `TM_ACTIVE_LOCALE` implementation: the live Transloco
 * language when a `TranslocoService` is provided (the `provideTellmaUi()`
 * path — language tags like `en`/`ar` are valid BCP-47 locales and feed
 * `Intl` directly), else the static `LOCALE_ID`. One app-lifetime
 * subscription keeps the signal current across language switches.
 */
export function tmDefaultActiveLocale(): Signal<string> {
  // TranslocoService is providedIn:'root', so an optional inject would still
  // instantiate it and then crash on ITS missing config deps in an app that
  // never called provideTransloco/provideTellmaUi. The transpiler token is
  // only present when Transloco was actually provided — probe that instead.
  const translocoProvided = inject(TRANSLOCO_TRANSPILER, { optional: true }) !== null;
  if (!translocoProvided) {
    return signal(inject(LOCALE_ID)).asReadonly();
  }
  const transloco = inject(TranslocoService);
  const locale = signal(transloco.getActiveLang());
  const langSub = transloco.langChanges$.subscribe((lang) => locale.set(lang));
  inject(DestroyRef).onDestroy(() => langSub.unsubscribe());
  return locale.asReadonly();
}

/**
 * The app-ambient locale used for FORMATTING (numbers, dates) as a signal,
 * so displays re-render when the user switches language at runtime.
 * Defaults to the active Transloco language when one is provided, else the
 * static `LOCALE_ID`; a distribution with its own locale machinery
 * overrides the token.
 */
export const TM_ACTIVE_LOCALE = new InjectionToken<Signal<string>>('TM_ACTIVE_LOCALE', {
  providedIn: 'root',
  factory: tmDefaultActiveLocale,
});

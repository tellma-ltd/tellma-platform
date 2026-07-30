// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injector, untracked } from '@angular/core';
import type { TranspileParams } from '@jsverse/transloco';
import { MessageFormatTranspiler } from '@jsverse/transloco-messageformat';

import { TM_ACTIVE_LOCALE } from './tm-active-locale';

/**
 * The ICU transpiler, compiled against the library's FORMATTING locale
 * rather than the bare language tag Transloco hands it.
 *
 * Numbers inside a message — a plural's `#`, an explicit `{n, number}` —
 * are formatted by MessageFormat using the locale it was compiled for, and
 * a bare language tag carries CLDR's DEFAULT numbering system: `ar` means
 * Latin digits. A distribution nominates a regional locale for formatting
 * (`ar-SA`), so a message compiled from the language tag alone would print
 * `أدخل قيمة لا تزيد عن 100` beside a field whose own value reads `١٠٠`.
 * One locale governs every number the library renders, message or value.
 *
 * The locale is read at transpile time, not at construction: the
 * `TM_ACTIVE_LOCALE` factory probes the transpiler token to decide whether
 * Transloco is present, so injecting it any earlier would close a cycle.
 * Reading it late also settles the ordering — `setActiveLang` notifies the
 * transpiler BEFORE it publishes the new language, so at that moment the
 * locale signal still holds the outgoing one.
 */
export class ɵTmMessageFormatTranspiler extends MessageFormatTranspiler {
  private readonly injector = inject(Injector);
  private applied: string | null = null;

  constructor() {
    super({});
  }

  /**
   * Transloco's own hook, deliberately inert: it offers the language tag,
   * and this transpiler follows `TM_ACTIVE_LOCALE` instead.
   */
  override onLangChanged(): void {
    // no-op — see `transpile`
  }

  override transpile(params: TranspileParams): unknown {
    // Untracked: a message's own computed tracks the locale (see
    // `tmDefaultUiTranslate`), so a late switch re-renders it; taking a
    // dependency HERE would instead tie every consumer that reads a string
    // to the transpiler's internals.
    const locale = untracked(() => this.injector.get(TM_ACTIVE_LOCALE)());
    if (locale !== this.applied) {
      this.setLocale(locale);
      this.applied = locale;
    }
    return super.transpile(params);
  }
}

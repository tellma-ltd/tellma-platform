// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable, Injector, untracked } from '@angular/core';
import {
  getValue,
  setValue,
  type TranslocoTranspiler,
  type TranspileParams,
} from '@jsverse/transloco';

import { ɵtmFormatMessage } from './tm-icu';
import { TM_ACTIVE_LOCALE } from './tm-active-locale';
import { TM_UI_ARGUMENT_FORMATTER } from './tm-ui-argument-formatter';

/**
 * The ICU MessageFormat transpiler behind every Transloco message — the
 * library's `tmUi` strings, the app's `tm` strings and server string packs
 * alike.
 *
 * Messages are INTERPRETED from a parsed tree, never compiled to
 * JavaScript, so they format under a Content Security Policy without
 * `'unsafe-eval'` and under Trusted Types.
 *
 * Messages format against the library's FORMATTING locale rather than the
 * bare language tag Transloco hands the transpiler. Numbers inside a
 * message — a plural's `#`, an explicit `{n, number}` — use that locale,
 * and a bare language tag carries CLDR's DEFAULT numbering system: `ar`
 * means Latin digits. A distribution nominates a regional locale for
 * formatting (`ar-SA`), so a message formatted for the language tag alone
 * would print `أدخل قيمة لا تزيد عن 100` beside a field whose own value
 * reads `١٠٠`. One locale governs every number the library renders, message
 * or value.
 *
 * The locale is read at transpile time, not at construction: the
 * `TM_ACTIVE_LOCALE` factory probes the transpiler token to decide whether
 * Transloco is present, so injecting it any earlier would close a cycle.
 *
 * Typed arguments (`number`, `date`, `time`) format through
 * `TM_UI_ARGUMENT_FORMATTER`, which an app replaces to render dates in its
 * own calendar and time zone.
 *
 * Only ICU syntax is interpreted: Transloco's `{{param}}` interpolation is
 * not applied first, so a parameter's VALUE can never be re-read as message
 * syntax, and the parse cache stays keyed by the message alone.
 */
@Injectable()
export class ɵTmMessageFormatTranspiler implements TranslocoTranspiler {
  private readonly injector = inject(Injector);

  /**
   * Formats one resolved translation value. A string is an ICU message; an
   * object (from `translateObject`) formats each nested message named in
   * `params` with that entry's parameters; anything else passes through.
   */
  transpile({ value, params = {} }: TranspileParams): unknown {
    if (!value) {
      return value;
    }
    // Untracked: a message's own computed tracks the locale (see
    // `tmDefaultUiTranslate`), so a late switch re-renders it; taking a
    // dependency HERE would instead tie every consumer that reads a string
    // to the transpiler's internals.
    const locale = untracked(() => this.injector.get(TM_ACTIVE_LOCALE)());
    const format = this.injector.get(TM_UI_ARGUMENT_FORMATTER);
    if (typeof value === 'string') {
      return ɵtmFormatMessage(value, params, locale, format);
    }
    if (typeof value === 'object' && !Array.isArray(value)) {
      let table = value as Record<string, unknown>;
      for (const path of Object.keys(params)) {
        const message: unknown = getValue(table, path);
        if (typeof message === 'string') {
          const nested = (params as Record<string, unknown>)[path];
          table = setValue(
            table,
            path,
            ɵtmFormatMessage(message, toParams(nested), locale, format),
          );
        }
      }
      return table;
    }
    return value;
  }

  /**
   * Transloco's language hook, deliberately inert: it offers the language
   * tag, and this transpiler follows `TM_ACTIVE_LOCALE` instead.
   */
  onLangChanged(): void {
    // no-op — see `transpile`
  }
}

/** A nested `translateObject` parameter entry as a parameter record (non-objects carry none). */
function toParams(value: unknown): Readonly<Record<string, unknown>> {
  return value !== null && typeof value === 'object' ? (value as Record<string, unknown>) : {};
}

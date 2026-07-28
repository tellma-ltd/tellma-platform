// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmEthiopicCalendar } from '@tellma/core-ui/calendar-ethiopic';
import { tmUmalquraCalendar } from '@tellma/core-ui/calendar-umalqura';
import { tmFormatDate, tmGregorianCalendar, tmParseDate } from '@tellma/core-ui/l10n';

/**
 * The cross-calendar format→parse round-trip sweep — the identity the
 * date picker's canonicalization paths rely on. Every (locale × calendar
 * × style) cell must round-trip exactly: CJK glues unit suffixes to the
 * numbers, fa embeds a ZWJ in the Umm al-Qura era, th abbreviates it with
 * dots, es/fr/vi/de interleave particles and multi-word or dotted month
 * names, he glues one-letter prepositions onto hyphenated month names —
 * all previously fatal to a locale/calendar switch or popup selection.
 *
 * (Root-level spec: it spans the l10n engine AND both calendar packs,
 * which the l10n entry point's dependency boundary keeps apart.)
 */

const TODAY = '2026-07-15';

const LOCALES = [
  'en-US',
  'en-GB',
  'de',
  'fr',
  'es',
  'ar-SA',
  'fa',
  'th',
  'tr',
  'he',
  'hi',
  'vi',
  'ja',
  'zh-CN',
  'ko',
];
const STYLES = ['numeric', 'medium', 'long'] as const;
const SAMPLES = ['1985-11-20', '2026-07-27', '2043-02-28'];

describe('format→parse round-trip across locales, calendars, and styles', () => {
  for (const calendar of [tmGregorianCalendar(), tmUmalquraCalendar(), tmEthiopicCalendar()]) {
    it(`round-trips every locale × style under ${calendar.id}`, () => {
      const failures: string[] = [];
      for (const locale of LOCALES) {
        for (const style of STYLES) {
          for (const iso of SAMPLES) {
            const text = tmFormatDate(iso, locale, { calendar, dateStyle: style });
            const back = tmParseDate(text, locale, { calendar, today: TODAY });
            if (back !== iso) {
              failures.push(`${locale} ${style} ${iso}: '${text}' → ${String(back)}`);
            }
          }
        }
      }
      expect(failures, failures.join('\n')).toEqual([]);
    });
  }
});

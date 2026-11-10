// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  Component,
  computed,
  inject,
  signal,
  type EnvironmentProviders,
  type Provider,
  type Signal,
  type WritableSignal,
} from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';

import { TM_BIDI_CONTROLS, tmGregorianCalendar, type TmCalendar } from '@tellma/core-ui/l10n';

import {
  TM_UI_ARGUMENT_FORMATTER,
  tmDefaultUiArgumentFormatter,
  type TmUiArgumentFormatter,
} from '../i18n/tm-ui-argument-formatter';
import { TM_UI_I18N_SCOPE, TM_UI_TRANSLATE } from '../i18n/tm-ui-translate';
import { provideTellmaUi } from '../providers/provide-tellma-ui';
import { provideTmCalendar } from '../providers/tm-calendar';
import { TM_DEVICE_TIME_ZONE, TM_NOW, TM_TIME_ZONE } from '../providers/tm-time-zone';
import { TmL10n } from './tm-l10n';

@Component({ template: `` })
class Host {
  private readonly l10n = inject(TmL10n);
  /** A consumer-style computed — must re-evaluate on locale switch. */
  readonly display: Signal<string> = computed(() =>
    this.l10n.formatNumber(1234.5, { minDecimals: 2 }),
  );
}

/** 20 October 2026, 07:42 UTC — 10:42 in Riyadh, 08:42 in London. */
const AT = '2026-10-20T07:42:00Z';
const AT_MILLIS = Date.UTC(2026, 9, 20, 7, 42);

// Beirut is at +2 in winter and +3 in summer; Riyadh is at +3 all year.
const WINTER = '2026-01-15T09:00:00Z';
const SUMMER = '2026-07-15T09:00:00Z';
const WINTER_MILLIS = Date.UTC(2026, 0, 15, 9);
const SUMMER_MILLIS = Date.UTC(2026, 6, 15, 9);

/** A facade over the given zones and a clock the test moves. */
function zoned(
  timeZone: string,
  deviceTimeZone: string,
  now = AT_MILLIS,
  providers: (Provider | EnvironmentProviders)[] = [],
): {
  l10n: TmL10n;
  timeZone: WritableSignal<string>;
  deviceTimeZone: WritableSignal<string>;
  clock: { now: number };
} {
  const zone = signal(timeZone);
  const device = signal(deviceTimeZone);
  const clock = { now };
  TestBed.configureTestingModule({
    providers: [
      { provide: TM_TIME_ZONE, useValue: zone },
      { provide: TM_DEVICE_TIME_ZONE, useValue: device },
      { provide: TM_NOW, useValue: () => clock.now },
      ...providers,
    ],
  });
  return { l10n: TestBed.inject(TmL10n), timeZone: zone, deviceTimeZone: device, clock };
}

describe('TmL10n', () => {
  it('formats with the active locale and re-renders reactive callers on switch', async () => {
    TestBed.configureTestingModule({
      providers: [provideTellmaUi({ availableLangs: ['en', 'de'] })],
    });
    const fixture = TestBed.createComponent(Host);
    const display = fixture.componentInstance.display;
    expect(display()).toBe('1,234.50');

    TestBed.inject(TranslocoService).setActiveLang('de');
    await fixture.whenStable();
    expect(display()).toBe('1.234,50');
  });

  it('formats with LOCALE_ID when Transloco is absent', () => {
    TestBed.configureTestingModule({});
    const fixture = TestBed.createComponent(Host);
    // The TestBed default LOCALE_ID is en-US.
    expect(fixture.componentInstance.display()).toBe('1,234.50');
  });

  it('passes the grouping option through', () => {
    TestBed.configureTestingModule({});
    const l10n = TestBed.inject(TmL10n);
    expect(l10n.formatNumber(2026, { grouping: false })).toBe('2026');
    expect(l10n.formatNumber(2026)).toBe('2,026');
  });

  describe('time zones', () => {
    it("defaults both zones to the browser's and the clock to the device's", () => {
      TestBed.configureTestingModule({});
      const l10n = TestBed.inject(TmL10n);
      const browserZone = new Intl.DateTimeFormat().resolvedOptions().timeZone;
      expect(l10n.timeZone()).toBe(browserZone);
      expect(l10n.deviceTimeZone()).toBe(browserZone);
      // The display zone IS the device zone until an app provides one.
      expect(TestBed.inject(TM_TIME_ZONE)).toBe(TestBed.inject(TM_DEVICE_TIME_ZONE));
      expect(l10n.zoneMismatch()).toBe(false);
      expect(l10n.zoneLabel()).toBe('');
      const before = Date.now();
      const now = TestBed.inject(TM_NOW)();
      expect(now).toBeGreaterThanOrEqual(before);
      expect(now).toBeLessThanOrEqual(Date.now());
    });

    it('formats an instant in the display zone, bare when the device shares its offset', () => {
      const { l10n } = zoned('Asia/Riyadh', 'Asia/Riyadh');
      expect(l10n.formatTime(AT)).toBe('10:42 AM');
      expect(l10n.formatDateTime(AT)).toBe('10/20/2026, 10:42 AM');
      expect(l10n.formatDateTime(AT, { dateStyle: 'medium', timeStyle: 'medium' })).toBe(
        'Oct 20, 2026, 10:42:00 AM',
      );
      expect(l10n.formatTime(AT, { timeStyle: 'medium' })).toBe('10:42:00 AM');
      expect(l10n.zoneLabel(AT)).toBe('');
    });

    it("labels an instant with the display zone's offset when the device is in another", () => {
      const { l10n } = zoned('Asia/Riyadh', 'Europe/London');
      expect(l10n.formatDateTime(AT)).toBe('10/20/2026, 10:42 AM GMT+3');
      expect(l10n.formatTime(AT)).toBe('10:42 AM GMT+3');
      expect(l10n.zoneLabel(AT)).toBe('GMT+3');
      // 'auto' is the default.
      expect(l10n.formatDateTime(AT, { zoneLabel: 'auto' })).toBe('10/20/2026, 10:42 AM GMT+3');
      expect(l10n.formatDateTime(AT, { zoneLabel: 'none' })).toBe('10/20/2026, 10:42 AM');
      expect(l10n.formatTime(AT, { zoneLabel: 'none' })).toBe('10:42 AM');
    });

    it('never labels by zone id: equal offsets are one clock', () => {
      const { l10n } = zoned('Asia/Riyadh', 'Asia/Kuwait');
      expect(l10n.formatDateTime(AT)).toBe('10/20/2026, 10:42 AM');
      expect(l10n.zoneLabel(AT)).toBe('');
      expect(l10n.zoneMismatch()).toBe(false);
    });

    it('never labels a text without a time, nor a relative time', () => {
      const { l10n } = zoned('Asia/Riyadh', 'Europe/London');
      expect(l10n.formatDateTime(AT, { timeStyle: 'none' })).toBe('10/20/2026');
      expect(l10n.formatDateTime(AT, { dateStyle: 'month' })).toBe('Oct 2026');
      expect(l10n.formatDateTime(AT, { dateStyle: 'quarter' })).toBe('Q4 2026');
      expect(l10n.formatDateTime(AT, { dateStyle: 'year', zoneLabel: 'column' })).toBe('2026');
      expect(l10n.formatRelativeTime(AT, { now: AT_MILLIS + 5 * 60_000 })).toBe('5 min. ago');
    });

    it("compares offsets at the instant shown, not at today's", () => {
      // Beirut on a Riyadh device: an hour apart in winter, equal in summer.
      const { l10n } = zoned('Asia/Beirut', 'Asia/Riyadh', SUMMER_MILLIS);
      expect(l10n.formatTime(WINTER)).toBe('11:00 AM GMT+2');
      expect(l10n.formatTime(SUMMER)).toBe('12:00 PM');
      expect(l10n.zoneLabel(WINTER)).toBe('GMT+2');
      expect(l10n.zoneLabel(SUMMER)).toBe('');
    });

    it("column: under a labelled header a cell is bare unless its offset differs from the header's", () => {
      // The clock is in winter: the header reads GMT+2.
      const { l10n } = zoned('Asia/Beirut', 'Asia/Riyadh', WINTER_MILLIS);
      expect(l10n.zoneMismatch()).toBe(true);
      expect(l10n.zoneLabel()).toBe('GMT+2');
      expect(l10n.formatTime(WINTER, { zoneLabel: 'column' })).toBe('11:00 AM');
      // A summer row is at GMT+3 — the device's own offset, but not the header's.
      expect(l10n.formatTime(SUMMER, { zoneLabel: 'column' })).toBe('12:00 PM GMT+3');
      expect(l10n.formatDateTime(SUMMER, { zoneLabel: 'column' })).toBe(
        '7/15/2026, 12:00 PM GMT+3',
      );
      // 'auto' would label the other way round.
      expect(l10n.formatTime(WINTER)).toBe('11:00 AM GMT+2');
      expect(l10n.formatTime(SUMMER)).toBe('12:00 PM');
    });

    it('column: under a bare header a cell labels as auto does', () => {
      // The clock is in summer: Beirut and Riyadh agree, so the header is bare.
      const { l10n } = zoned('Asia/Beirut', 'Asia/Riyadh', SUMMER_MILLIS);
      expect(l10n.zoneMismatch()).toBe(false);
      expect(l10n.zoneLabel()).toBe('');
      expect(l10n.formatTime(WINTER, { zoneLabel: 'column' })).toBe('11:00 AM GMT+2');
      expect(l10n.formatTime(SUMMER, { zoneLabel: 'column' })).toBe('12:00 PM');
    });

    it('recomputes zoneMismatch and re-renders instants when a zone changes', () => {
      const { l10n, timeZone, deviceTimeZone } = zoned('Asia/Riyadh', 'Asia/Riyadh');
      const text = computed(() => l10n.formatDateTime(AT));
      const label = computed(() => l10n.zoneLabel());
      expect(l10n.zoneMismatch()).toBe(false);
      expect(text()).toBe('10/20/2026, 10:42 AM');
      expect(label()).toBe('');

      timeZone.set('Asia/Kolkata');
      expect(l10n.zoneMismatch()).toBe(true);
      expect(text()).toBe('10/20/2026, 1:12 PM GMT+5:30');
      expect(label()).toBe('GMT+5:30');

      deviceTimeZone.set('Asia/Kolkata');
      expect(l10n.zoneMismatch()).toBe(false);
      expect(text()).toBe('10/20/2026, 1:12 PM');
      expect(label()).toBe('');

      deviceTimeZone.set('Europe/London');
      expect(l10n.zoneMismatch()).toBe(true);
      expect(text()).toBe('10/20/2026, 1:12 PM GMT+5:30');
    });

    it('recomputes zoneMismatch when the page is shown again', () => {
      const { l10n, clock } = zoned('Asia/Beirut', 'Asia/Riyadh', WINTER_MILLIS);
      const header = computed(() => l10n.zoneLabel());
      const cell = computed(() => l10n.formatTime(SUMMER, { zoneLabel: 'column' }));
      expect(l10n.zoneMismatch()).toBe(true);
      expect(header()).toBe('GMT+2');
      expect(cell()).toBe('12:00 PM GMT+3');

      // Daylight saving began while the page sat in the background: no
      // event reports it, so nothing changes yet...
      clock.now = SUMMER_MILLIS;
      expect(l10n.zoneMismatch()).toBe(true);

      // ...until the page is visited again.
      document.dispatchEvent(new Event('visibilitychange'));
      expect(l10n.zoneMismatch()).toBe(false);
      expect(header()).toBe('');
      expect(cell()).toBe('12:00 PM');
    });

    it('stops listening for visits when it is destroyed', () => {
      const added = vi.spyOn(document, 'addEventListener');
      const removed = vi.spyOn(document, 'removeEventListener');
      try {
        zoned('Asia/Beirut', 'Asia/Riyadh');
        const listener = added.mock.calls.find(([type]) => type === 'visibilitychange')?.[1];
        expect(listener).toBeDefined();
        TestBed.resetTestingModule();
        expect(removed.mock.calls.some(([type, fn]) => type === 'visibilitychange' && fn === listener)).toBe(
          true,
        );
      } finally {
        added.mockRestore();
        removed.mockRestore();
      }
    });

    it("gives '' for an empty instant and returns text that is not one as-is", () => {
      const { l10n } = zoned('Asia/Riyadh', 'Europe/London');
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
      try {
        expect(l10n.formatDateTime(null)).toBe('');
        expect(l10n.formatDateTime(undefined)).toBe('');
        expect(l10n.formatTime('')).toBe('');
        expect(l10n.formatRelativeTime(null, { now: AT_MILLIS })).toBe('');
        expect(l10n.formatDateTime('2026-10-20T10:42:00')).toBe('2026-10-20T10:42:00');
        expect(l10n.formatTime('soon', { zoneLabel: 'column' })).toBe('soon');
        expect(l10n.zoneLabel('soon')).toBe('');
        // An empty argument asks for the label now.
        expect(l10n.zoneLabel(null)).toBe('GMT+3');
        expect(l10n.zoneLabel('')).toBe('GMT+3');
      } finally {
        warn.mockRestore();
      }
    });

    it('counts relative days in the display zone and dates in the ambient calendar', () => {
      const { l10n, timeZone } = zoned('UTC', 'UTC');
      // 26 hours apart: one midnight between them in UTC, two in Riyadh.
      const now = Date.UTC(2026, 9, 20, 22, 30);
      const text = computed(() => l10n.formatRelativeTime('2026-10-19T20:30:00Z', { now }));
      expect(text()).toBe('yesterday');
      timeZone.set('Asia/Riyadh');
      expect(text()).toBe('2 days ago');
      expect(l10n.formatRelativeTime(AT, { now: AT_MILLIS + 2 * 3_600_000, style: 'long' })).toBe(
        '2 hours ago',
      );
      expect(l10n.formatRelativeTime('2026-01-05T07:42:00Z', { now })).toBe('Jan 5, 2026');
    });
  });

  describe('calendar', () => {
    /** A stand-in display calendar: the Gregorian fields under another year count. */
    const shifted: TmCalendar = {
      ...tmGregorianCalendar(),
      toParts: (iso) => {
        const parts = tmGregorianCalendar().toParts(iso);
        return { ...parts, year: parts.year + 543 };
      },
    };

    it('formats dates, instants and month names in the ambient calendar', () => {
      const calendar = signal<TmCalendar>(tmGregorianCalendar());
      const { l10n } = zoned('Asia/Riyadh', 'Asia/Riyadh', AT_MILLIS, [provideTmCalendar(calendar)]);
      const quarter = computed(() => l10n.formatDate('2026-11-02', { dateStyle: 'quarter' }));
      const instantQuarter = computed(() => l10n.formatDateTime(AT, { dateStyle: 'quarter' }));
      expect(quarter()).toBe('Q4 2026');
      expect(instantQuarter()).toBe('Q4 2026');
      expect(l10n.formatMonthName(3)).toBe('March');

      calendar.set(shifted);
      expect(quarter()).toBe('Q4 2569');
      expect(instantQuarter()).toBe('Q4 2569');
      // An explicit calendar overrides the ambient one.
      expect(
        l10n.formatDate('2026-11-02', { dateStyle: 'quarter', calendar: tmGregorianCalendar() }),
      ).toBe('Q4 2026');
      expect(l10n.formatMonthName(3, { calendar: tmGregorianCalendar() })).toBe('March');
    });

    it('names months and weekdays in the active locale', async () => {
      TestBed.configureTestingModule({
        providers: [provideTellmaUi({ availableLangs: ['en', 'de'] })],
      });
      const l10n = TestBed.inject(TmL10n);
      const month = computed(() => l10n.formatMonthName(3));
      const weekday = computed(() => l10n.formatWeekdayName(7));
      expect(month()).toBe('March');
      expect(weekday()).toBe('Sunday');
      expect(l10n.formatMonthName(13)).toBe('13');
      expect(l10n.formatWeekdayName(0)).toBe('0');

      TestBed.inject(TranslocoService).setActiveLang('de');
      expect(month()).toBe('März');
      expect(weekday()).toBe('Sonntag');
    });
  });

  describe('the quarter style', () => {
    it('writes through the built-in English pattern when Transloco is absent', () => {
      TestBed.configureTestingModule({});
      const l10n = TestBed.inject(TmL10n);
      expect(l10n.formatDate('2026-11-02', { dateStyle: 'quarter' })).toBe('Q4 2026');
      expect(l10n.formatDate('2026-02-02', { dateStyle: 'quarter' })).toBe('Q1 2026');
    });

    it("writes through the active language's pattern and re-renders on a switch", async () => {
      TestBed.configureTestingModule({
        providers: [provideTellmaUi({ availableLangs: ['en', 'ar'] })],
      });
      const transloco = TestBed.inject(TranslocoService);
      transloco.setTranslation(
        { [TM_UI_I18N_SCOPE]: { l10n: { quarter: 'الربع {quarter} {year}' } } },
        'ar',
        { merge: true },
      );
      const l10n = TestBed.inject(TmL10n);
      const quarter = computed(() => l10n.formatDate('2026-11-02', { dateStyle: 'quarter' }));
      expect(quarter()).toBe('Q4 2026');

      transloco.setActiveLang('ar');
      await new Promise((resolve) => setTimeout(resolve, 20));
      expect(quarter()).toBe('الربع 4 2026');
      expect([...quarter().matchAll(TM_BIDI_CONTROLS)]).toEqual([]);
    });

    it("lets a caller's pattern win over the translated one", () => {
      TestBed.configureTestingModule({});
      const l10n = TestBed.inject(TmL10n);
      expect(
        l10n.formatDate('2026-11-02', { dateStyle: 'quarter', quarterPattern: '{year}/{quarter}' }),
      ).toBe('2026/4');
    });

    it('resolves its pattern lazily, so a message argument formatter may inject the facade', () => {
      // The translate function injects the argument formatter; were the
      // facade to inject the translate function up front, this would be a
      // dependency cycle.
      TestBed.configureTestingModule({
        providers: [
          provideTellmaUi(),
          {
            provide: TM_UI_ARGUMENT_FORMATTER,
            useFactory: (): TmUiArgumentFormatter => {
              const l10n = inject(TmL10n);
              return (type, value, style, locale) =>
                type === 'date'
                  ? l10n.formatDate(String(value))
                  : tmDefaultUiArgumentFormatter(type, value, style, locale);
            },
          },
        ],
      });
      // Either may be asked for first.
      const translate = TestBed.inject(TM_UI_TRANSLATE);
      const l10n = TestBed.inject(TmL10n);
      expect(l10n.formatDate('2026-11-02', { dateStyle: 'quarter' })).toBe('Q4 2026');
      expect(translate('errors.required')()).toBe('This field is required');
    });
  });
});

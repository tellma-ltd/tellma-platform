// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { createEnvironmentInjector, EnvironmentInjector, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TmEntities, type TmJson, type TmQueryRowSet, type TmValueDescriptor } from '@tellma/core';
import { TM_ACTIVE_LOCALE, TM_CALENDAR, TM_DEVICE_TIME_ZONE, TM_NOW, TM_TIME_ZONE, TmL10n } from '@tellma/core-ui';
import { tmUmalquraCalendar } from '@tellma/core-ui/calendar-umalqura';
import { TM_BIDI_CONTROLS } from '@tellma/core-ui/l10n';

import { tmEntityRig, type TmEntityRigOptions } from '../../src/entity/tm-entity.testing.util';
import { TmFormats } from './tm-formats';
import { tmSelectPlan } from './tm-select-plan';

// The typed-value formats over the entity registry's test app: exact
// decimals and longs, the null, flag, enum, date and date-time rules, the
// zone labels of a details value, a column header and its cells across a
// daylight-saving change, and a plan's cells by column kind.

/** A user who reads everything. */
const SYSTEM: TmEntityRigOptions = { access: { isSystem: true } };

/** A decimal of a scale. */
const decimal = (scale: number): TmValueDescriptor => ({ type: 'decimal', nullable: true, scale });

/** An integer, a `long` when asked. */
const integer = (long = false): TmValueDescriptor => ({ type: 'integer', nullable: true, ...(long && { long: true }) });

/**
 * The formats over a display zone, a device zone and a clock of the test's
 * choosing, beside the rig's registry and strings.
 *
 * @param zone - The display zone.
 * @param device - The device's zone.
 * @param now - The clock's reading.
 * @returns The formats.
 */
function zoned(zone: string, device: string, now: string): TmFormats {
  const injector = createEnvironmentInjector(
    [
      TmFormats,
      TmL10n,
      { provide: TM_TIME_ZONE, useValue: signal(zone) },
      { provide: TM_DEVICE_TIME_ZONE, useValue: signal(device) },
      { provide: TM_NOW, useValue: () => Date.parse(now) },
    ],
    TestBed.inject(EnvironmentInjector),
  );
  return injector.get(TmFormats);
}

/**
 * The formats in a locale and the Umm al-Qura calendar.
 *
 * @param locale - The locale.
 * @returns The formats.
 */
function inUmmAlQura(locale: string): TmFormats {
  const injector = createEnvironmentInjector(
    [
      TmFormats,
      TmL10n,
      { provide: TM_ACTIVE_LOCALE, useValue: signal(locale) },
      { provide: TM_CALENDAR, useValue: signal(tmUmalquraCalendar()) },
    ],
    TestBed.inject(EnvironmentInjector),
  );
  return injector.get(TmFormats);
}

describe('TmFormats', () => {
  describe('numbers', () => {
    it('formats a decimal at 28 digits exactly, at its scale', async () => {
      await tmEntityRig(SYSTEM);
      const formats = TestBed.inject(TmFormats);
      expect(formats.text('1234567890123456789012345678', decimal(0))).toBe('1,234,567,890,123,456,789,012,345,678');
      expect(formats.text('123456789012345678901234.5678', decimal(4))).toBe('123,456,789,012,345,678,901,234.5678');
      expect(formats.text('-0.5', decimal(2))).toBe('-0.50');
      expect(formats.text('2.00005', decimal(4))).toBe('2.0001');
    });

    it('formats a long exactly, past what a double holds', async () => {
      await tmEntityRig(SYSTEM);
      const formats = TestBed.inject(TmFormats);
      expect(formats.text('9007199254740993', integer(true))).toBe('9,007,199,254,740,993');
      expect(formats.text('-9223372036854775808', integer(true), 'grid')).toBe('-9,223,372,036,854,775,808');
      expect(formats.text(1234, integer())).toBe('1,234');
    });

    it('writes decimals in the locale’s digits with no bidi control', async () => {
      await tmEntityRig({ ...SYSTEM, languages: 2, language: 'ar' });
      const formats = TestBed.inject(TmFormats);
      const l10n = TestBed.inject(TmL10n);
      const text = formats.text('-1234567890123456789.25', decimal(2));
      expect(text).not.toMatch(TM_BIDI_CONTROLS);
      // The ES2023 signature takes decimal text as an exact decimal; the
      // project's library target predates it.
      const format = new Intl.NumberFormat(l10n.locale(), { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format as (value: number | string) => string;
      const expected = format('-1234567890123456789.25').replace(TM_BIDI_CONTROLS, '');
      // Formatted from its decimal text, every one of the 21 digits survives.
      expect(text).toBe(expected);
    });
  });

  describe('other types', () => {
    it('shows nothing in a grid and a dash in a details view for no value', async () => {
      await tmEntityRig(SYSTEM);
      const formats = TestBed.inject(TmFormats);
      expect(formats.text(null, decimal(2), 'grid')).toBe('');
      expect(formats.text(null, decimal(2))).toBe('—');
      expect(formats.text(undefined, { type: 'string', nullable: true })).toBe('—');
    });

    it('formats a flag, an enum value, text and JSON', async () => {
      await tmEntityRig(SYSTEM);
      const formats = TestBed.inject(TmFormats);
      expect(formats.text(true, { type: 'bool', nullable: false })).toBe('Yes');
      expect(formats.text(false, { type: 'bool', nullable: false })).toBe('No');
      expect(formats.text('Retail', { type: 'enum', nullable: false, enum: 'acme.CustomerType' })).toBe('Retail');
      expect(formats.text('Unlisted', { type: 'enum', nullable: false, enum: 'acme.CustomerType' })).toBe('Unlisted');
      expect(formats.text('E-01', { type: 'string', nullable: false })).toBe('E-01');
      expect(formats.text({ a: 1 }, { type: 'json', nullable: true })).toBe('{"a":1}');
    });

    it('joins a list in the locale', async () => {
      await tmEntityRig(SYSTEM);
      const formats = TestBed.inject(TmFormats);
      expect(formats.text(['Retail', 'Retail'], { type: 'enum', nullable: false, enum: 'acme.CustomerType', array: true })).toBe('Retail, Retail');
    });

    it('formats a date in the active calendar, medium in details and numeric in a grid', async () => {
      await tmEntityRig(SYSTEM);
      const formats = TestBed.inject(TmFormats);
      const of: TmValueDescriptor = { type: 'date', nullable: false };
      expect(formats.text('2026-03-01', of)).toBe('Mar 1, 2026');
      expect(formats.text('2026-03-01', of, 'grid')).toBe('3/1/2026');
    });

    it('formats a date in the Umm al-Qura calendar, in the locale’s digits', async () => {
      await tmEntityRig(SYSTEM);
      const of: TmValueDescriptor = { type: 'date', nullable: false };
      // 2026-03-01 is 12 Ramadan 1447.
      expect(inUmmAlQura('ar-SA').text('2026-03-01', of, 'grid')).toBe('١٢/٩/١٤٤٧');
      expect(inUmmAlQura('ar-SA-u-nu-latn').text('2026-03-01', of, 'grid')).toBe('12/9/1447');
      expect(inUmmAlQura('ar-SA-u-nu-latn').text('2026-03-01', of)).toBe('12 رمضان 1447');
      expect(inUmmAlQura('en').text('2026-03-01', of, 'grid')).toBe('9/12/1447');
    });

    it('shows a date-time in the Umm al-Qura calendar as stored, in UTC, in the locale’s digits', async () => {
      await tmEntityRig(SYSTEM);
      const of: TmValueDescriptor = { type: 'datetime', nullable: false };
      expect(inUmmAlQura('ar-SA').text('2026-03-01T10:30:00', of, 'grid')).toBe('١٢/٩/١٤٤٧، ١٠:٣٠ ص');
      expect(inUmmAlQura('ar-SA-u-nu-latn').text('2026-03-01T10:30:00', of, 'grid')).toBe('12/9/1447، 10:30 ص');
      // Late in the evening it stays the same day: a date-time is never moved into a zone.
      expect(inUmmAlQura('ar-SA-u-nu-latn').text('2026-03-01T22:30:00', of)).toBe('12 رمضان 1447، 10:30 م');
    });

    it('shows a date-time as stored, in UTC and never labelled', async () => {
      await tmEntityRig(SYSTEM);
      const formats = zoned('Asia/Riyadh', 'UTC', '2026-01-15T12:00:00Z');
      const text = formats.text('2026-03-01T10:30:00', { type: 'datetime', nullable: false }, 'grid');
      expect(text).toContain('10:30');
      expect(text).not.toContain('GMT');
    });

    it('writes a foreign key’s id ungrouped, in the locale’s digits', async () => {
      const { entities } = await tmEntityRig(SYSTEM);
      const formats = TestBed.inject(TmFormats);
      const currencyId = entities.get('acme.Customer').property('CurrencyId')!;
      expect(formats.text(1284, currencyId)).toBe('1284');
      expect(formats.text(1284, { type: 'integer', nullable: false })).toBe('1,284');
      expect(inUmmAlQura('ar-SA').text(1284, currencyId)).toBe('١٢٨٤');
    });

    it('names an id by its row once it is known', async () => {
      const { names } = await tmEntityRig(SYSTEM);
      const formats = TestBed.inject(TmFormats);
      names.remember('gl.Center', [{ id: 7, code: 'E-01' }]);
      expect(formats.text(7, { type: 'integer', nullable: false, target: 'gl.Center' })).toBe('E-01');
    });
  });

  describe('zone labels', () => {
    /** An instant. */
    const instant: TmValueDescriptor = { type: 'instant', nullable: true };

    it('labels a details instant with the display zone’s offset when the device’s differs', async () => {
      await tmEntityRig(SYSTEM);
      const formats = zoned('Asia/Beirut', 'UTC', '2026-01-15T12:00:00Z');
      expect(formats.text('2026-01-15T10:00:00+00:00', instant)).toMatch(/12:00.*GMT\+2$/);
      expect(formats.text('2026-07-15T10:00:00+00:00', instant)).toMatch(/1:00.*GMT\+3$/);
    });

    it('leaves an instant bare when the zones agree', async () => {
      await tmEntityRig(SYSTEM);
      const formats = zoned('Asia/Riyadh', 'Asia/Kuwait', '2026-01-15T12:00:00Z');
      expect(formats.text('2026-01-15T10:00:00+00:00', instant)).not.toContain('GMT');
      expect(formats.zoneSuffix(instant)()).toBe('');
    });

    it('labels a column header once, and a cell only across a daylight-saving change', async () => {
      await tmEntityRig(SYSTEM);
      const formats = zoned('Asia/Beirut', 'UTC', '2026-01-15T12:00:00Z');
      const column = TestBed.runInInjectionContext(() => tmSelectPlan(TestBed.inject(TmEntities).get('acme.Customer'), ['CreatedAt'])).columns[0]!;
      expect(formats.zoneSuffix(column)()).toBe(' (GMT+2)');
      expect(formats.zoneSuffix({ type: 'string', nullable: true })()).toBe('');

      const set: TmQueryRowSet = {
        columns: [
          { name: 'Id', type: 'Numeric', nullable: false, path: ['Id'], groupingKey: false },
          { name: 'CreatedAt', type: 'DateTimeOffset', nullable: false, path: ['CreatedAt'], groupingKey: false },
        ],
        rows: [
          [1, '2026-01-15T10:00:00+00:00'],
          [2, '2026-07-15T10:00:00+00:00'],
        ] as TmJson[][],
      };
      const [winter, summer] = TestBed.runInInjectionContext(() =>
        tmSelectPlan(TestBed.inject(TmEntities).get('acme.Customer'), ['CreatedAt']),
      ).rows(set);
      expect(formats.cell(column, winter!)).not.toContain('GMT');
      expect(formats.cell(column, summer!)).toMatch(/GMT\+3$/);
    });
  });

  describe('cells', () => {
    it('shows a group’s slot, a navigation’s display name, a file’s name, and no text for an image', async () => {
      const { entities } = await tmEntityRig({ ...SYSTEM, languages: 2, language: 'ar' });
      const formats = TestBed.inject(TmFormats);
      const customers = TestBed.runInInjectionContext(() => tmSelectPlan(entities.get('acme.Customer'), ['Name', 'Currency', 'Image', 'CreditLimit']));
      expect(customers.select).toBe('Id,Name,Name2,Currency.Id,Currency.Code,ImageId,Image.Variants,CreditLimit');
      const [customer] = customers.rows({
        columns: customers.select.split(',').map((term) => ({ name: term, type: 'String', nullable: true, path: term.split('.'), groupingKey: false })),
        rows: [[1, 'Acme', 'أكمي', 3, 'SAR', 88, 'thumb', '1500.5000']],
      });
      const [name, currency, image, limit] = customers.columns;
      expect(formats.cell(name!, customer!)).toBe('أكمي');
      expect(formats.cell(currency!, customer!)).toBe('SAR');
      expect(formats.cell(image!, customer!)).toBe('');
      expect(formats.cell(limit!, customer!)).toBe(formats.text('1500.5000', decimal(4), 'grid'));

      const files = TestBed.runInInjectionContext(() => tmSelectPlan(entities.get('core.OutgoingEmailAttachment'), ['File']));
      const [file] = files.rows({
        columns: ['Id', 'FileId', 'File.FileName'].map((term) => ({ name: term, type: 'String', nullable: true, path: term.split('.'), groupingKey: false })),
        rows: [[4, 51, 'invoice.pdf']],
      });
      expect(formats.cell(files.columns[0]!, file!)).toBe('invoice.pdf');
    });

    it('writes the key’s and a foreign key’s ids ungrouped', async () => {
      const { entities } = await tmEntityRig(SYSTEM);
      const formats = TestBed.inject(TmFormats);
      const plan = TestBed.runInInjectionContext(() => tmSelectPlan(entities.get('acme.Customer'), ['Id', 'CurrencyId', 'ExternalReference']));
      const [row] = plan.rows({
        columns: plan.select.split(',').map((term) => ({ name: term, type: 'String', nullable: true, path: term.split('.'), groupingKey: false })),
        rows: [[1284, 2048, '4096']],
      });
      expect(plan.columns.map((column) => formats.cell(column, row!))).toEqual(['1284', '2048', '4,096']);
    });

    it('shows an empty cell for a navigation with no related row', async () => {
      const { entities } = await tmEntityRig(SYSTEM);
      const formats = TestBed.inject(TmFormats);
      const centers = TestBed.runInInjectionContext(() => tmSelectPlan(entities.get('gl.Center'), ['Parent']));
      const [root] = centers.rows({
        columns: ['Id', 'Parent.Id', 'Parent.Code'].map((term) => ({ name: term, type: 'String', nullable: true, path: term.split('.'), groupingKey: false })),
        rows: [[1, null, null]],
      });
      expect(formats.cell(centers.columns[0]!, root!)).toBe('');
    });
  });
});

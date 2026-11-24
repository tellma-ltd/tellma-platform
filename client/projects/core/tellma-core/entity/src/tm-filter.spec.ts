// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TmEntities, type TmColumnInput, type TmEntitiesChunk } from '@tellma/core';
import { tmTestEntities } from '@tellma/core-testing';
import { TM_STRINGS_AR } from '@tellma/locale-ar/strings';

import { tmEntityRig, type TmEntityRigOptions } from '../../src/entity/tm-entity.testing.util';
import {
  tmDescribeFilter,
  tmFilterLeaf,
  tmFormatFilter,
  tmParseFilter,
  TmFilterDescriptionText,
  type TmFilterDescription,
  type TmFilterSpec,
} from './tm-filter';
import { tmSelectPlan, type TmColumnPlan } from './tm-select-plan';

// Column filters over the recorded entity model, patched with a date, a
// date-time and a guid column: the `f.` value read and written, each kind's
// leaf (the table of spec 0029 §13.2 row by row), and the sentence a chip
// shows in English, Arabic and a verb-last language.

/** The recorded model with the columns the recording lacks: a date, a date-time and a guid on customers. */
function chunk(): TmEntitiesChunk {
  const patched = tmTestEntities().chunk;
  const customer = patched.entities.find((entity) => entity.name === 'acme.Customer')!;
  const property = (name: string, type: string): unknown => ({
    name,
    json: name[0]!.toLowerCase() + name.slice(1),
    type,
    queryable: true,
    nullable: true,
    labelKey: `Acme_Customer_${name}`,
    excel: true,
    ownership: 'editable',
  });
  (customer.properties as unknown as unknown[]).push(property('DueDate', 'date'), property('PostedOn', 'datetime'), property('Token', 'guid'));
  return patched;
}

/** The English labels of the columns under test. */
const LABELS: Readonly<Record<string, string>> = {
  Acme_Customer_CreditLimit: 'Credit limit',
  Acme_Customer_DueDate: 'Due date',
  Acme_Customer_PostedOn: 'Posted on',
  Acme_Customer_CustomerType: 'Type',
  Acme_CustomerType_Wholesale: 'Wholesale',
  IsActive: 'Active',
  CreatedAt: 'Created',
};

/** A user who reads everything, in the patched model. */
const SYSTEM: TmEntityRigOptions = { access: { isSystem: true }, chunk: chunk(), labels: { en: LABELS } };

/**
 * Customers' columns, made in the rig's app.
 *
 * @param keys - The column inputs.
 * @returns The columns, by key.
 */
function columns(...keys: TmColumnInput<string>[]): Readonly<Record<string, TmColumnPlan>> {
  const plan = TestBed.runInInjectionContext(() => tmSelectPlan(TestBed.inject(TmEntities).get('acme.Customer'), keys));
  return Object.fromEntries(plan.columns.map((column) => [column.key, column]));
}

/**
 * A filter's leaf in Riyadh, from its `f.` value.
 *
 * @param column - The column.
 * @param value - The `f.` value.
 * @param timeZone - The display zone; Riyadh when omitted.
 * @returns The leaf, or null.
 */
function leaf(column: TmColumnPlan | undefined, value: string, timeZone = 'Asia/Riyadh'): string | null {
  return tmFilterLeaf(column!, tmParseFilter(value)!, { timeZone });
}

/**
 * A filter's description, read once.
 *
 * @param column - The column.
 * @param value - The `f.` value.
 * @returns The description.
 */
function describe_(column: TmColumnPlan | undefined, value: string): TmFilterDescription {
  return TestBed.runInInjectionContext(() => tmDescribeFilter(column!, tmParseFilter(value)!))();
}

describe('tmParseFilter and tmFormatFilter', () => {
  it('reads an operator and its one value, commas and all', () => {
    expect(tmParseFilter('contains:a,b')).toEqual({ op: 'contains', values: ['a,b'] });
    expect(tmParseFilter('eq:x:y')).toEqual({ op: 'eq', values: ['x:y'] });
  });

  it('splits a list on its unescaped commas', () => {
    expect(tmParseFilter('between:1,5')).toEqual({ op: 'between', values: ['1', '5'] });
    expect(tmParseFilter('in:a\\,b,c\\\\')).toEqual({ op: 'in', values: ['a,b', 'c\\'] });
  });

  it('refuses an unknown operator, an empty value, a stray escape and a range of other than two', () => {
    for (const value of ['', 'contains', 'like:a', 'contains:', 'in:a,,b', 'in:a\\b', 'between:1', 'between:1,2,3', 'in:a,']) {
      expect(tmParseFilter(value)).toBeNull();
    }
  });

  it('writes what it reads', () => {
    const specs: TmFilterSpec[] = [
      { op: 'in', values: ['a,b', 'c\\', 'd'] },
      { op: 'contains', values: ['a,b\\'] },
      { op: 'between', values: ['2026-01-01', '2026-01-31'] },
    ];
    for (const spec of specs) {
      expect(tmParseFilter(tmFormatFilter(spec))).toEqual(spec);
    }
    expect(tmFormatFilter({ op: 'in', values: ['Service', 'Sale'] })).toBe('in:Service,Sale');
  });
});

describe('tmFilterLeaf', () => {
  describe('text', () => {
    it('reads a one-language column with each operator', async () => {
      await tmEntityRig(SYSTEM);
      const { Code } = columns('Code');
      expect(leaf(Code, 'contains:jed')).toBe("contains(Code, 'jed')");
      expect(leaf(Code, 'starts-with:jed')).toBe("startsWith(Code, 'jed')");
      expect(leaf(Code, 'ends-with:jed')).toBe("endsWith(Code, 'jed')");
      expect(leaf(Code, "eq:O'Neil")).toBe("Code = 'O''Neil'");
      expect(leaf(Code, 'ne:x')).toBe("Code != 'x'");
    });

    it('reads every admitted slot of a group: either slot matches, and neither equals for ne', async () => {
      await tmEntityRig({ ...SYSTEM, languages: 2 });
      const { Name } = columns('Name');
      expect(leaf(Name, 'contains:jed')).toBe("contains(Name, 'jed') or contains(Name2, 'jed')");
      expect(leaf(Name, 'eq:x')).toBe("Name = 'x' or Name2 = 'x'");
      expect(leaf(Name, 'ne:x')).toBe("Name != 'x' and Name2 != 'x'");
    });

    it('takes no operator of another kind', async () => {
      await tmEntityRig(SYSTEM);
      const { Code } = columns('Code');
      expect(leaf(Code, 'between:a,b')).toBeNull();
      expect(leaf(Code, 'in:a')).toBeNull();
    });
  });

  describe('numbers', () => {
    it('compares with exact decimal text, a range inclusive and in order', async () => {
      await tmEntityRig(SYSTEM);
      const { CreditLimit } = columns('CreditLimit');
      expect(leaf(CreditLimit, 'between:100000,500000')).toBe('CreditLimit >= 100000 and CreditLimit <= 500000');
      expect(leaf(CreditLimit, 'between:5,5')).toBe('CreditLimit >= 5 and CreditLimit <= 5');
      expect(leaf(CreditLimit, 'lt:-5')).toBe('CreditLimit < -5');
      expect(leaf(CreditLimit, 'le:0.25')).toBe('CreditLimit <= 0.25');
      expect(leaf(CreditLimit, 'eq:12')).toBe('CreditLimit = 12');
      expect(leaf(CreditLimit, 'ge:12345678901234567890.5')).toBe('CreditLimit >= 12345678901234567890.5');
      expect(leaf(CreditLimit, 'gt:2.5')).toBe('CreditLimit > 2.5');
    });

    it('refuses a reversed range and text that is not a decimal', async () => {
      await tmEntityRig(SYSTEM);
      const { CreditLimit } = columns('CreditLimit');
      expect(leaf(CreditLimit, 'between:5,1')).toBeNull();
      for (const value of ['eq:1e3', 'eq:abc', 'eq:1,5', `eq:${String.fromCharCode(0x661)}`, 'between:1,x']) {
        expect(leaf(CreditLimit, value)).toBeNull();
      }
    });

    it('refuses a fraction for an integer', async () => {
      await tmEntityRig(SYSTEM);
      const { ExternalReference } = columns('ExternalReference');
      expect(leaf(ExternalReference, 'eq:1012')).toBe('ExternalReference = 1012');
      expect(leaf(ExternalReference, 'eq:1.5')).toBeNull();
    });
  });

  describe('days', () => {
    it('compares a date as itself, a range inclusive and the orderings strict', async () => {
      await tmEntityRig(SYSTEM);
      const { DueDate } = columns('DueDate');
      expect(leaf(DueDate, 'between:2026-01-01,2026-01-31')).toBe("DueDate >= '2026-01-01' and DueDate <= '2026-01-31'");
      expect(leaf(DueDate, 'before:2026-03-01')).toBe("DueDate < '2026-03-01'");
      expect(leaf(DueDate, 'after:2026-03-01')).toBe("DueDate > '2026-03-01'");
    });

    it('filters a date-time by whole days with no zone', async () => {
      await tmEntityRig(SYSTEM);
      const { PostedOn } = columns('PostedOn');
      expect(PostedOn!.filter).toBe('date');
      expect(leaf(PostedOn, 'between:2026-01-01,2026-01-31', 'Asia/Beirut')).toBe("PostedOn >= '2026-01-01T00:00:00' and PostedOn < '2026-02-01T00:00:00'");
      expect(leaf(PostedOn, 'before:2026-03-01')).toBe("PostedOn < '2026-03-01T00:00:00'");
      expect(leaf(PostedOn, 'after:2026-12-31')).toBe("PostedOn >= '2027-01-01T00:00:00'");
    });

    it('filters an instant by whole days in the display zone', async () => {
      await tmEntityRig(SYSTEM);
      const { CreatedAt } = columns('CreatedAt');
      expect(leaf(CreatedAt, 'between:2026-01-01,2026-01-31')).toBe(
        "CreatedAt >= '2025-12-31T21:00:00+00:00' and CreatedAt < '2026-01-31T21:00:00+00:00'",
      );
      expect(leaf(CreatedAt, 'before:2026-03-01')).toBe("CreatedAt < '2026-02-28T21:00:00+00:00'");
      expect(leaf(CreatedAt, 'after:2026-03-01')).toBe("CreatedAt >= '2026-03-01T21:00:00+00:00'");
      expect(leaf(CreatedAt, 'before:2026-03-01', 'UTC')).toBe("CreatedAt < '2026-03-01T00:00:00+00:00'");
    });

    it('starts a day with no midnight at its first instant', async () => {
      await tmEntityRig(SYSTEM);
      const { CreatedAt } = columns('CreatedAt');
      // Beirut moves its clocks from midnight to one on the last Sunday of March.
      expect(leaf(CreatedAt, 'between:2026-03-29,2026-03-29', 'Asia/Beirut')).toBe(
        "CreatedAt >= '2026-03-28T22:00:00+00:00' and CreatedAt < '2026-03-29T21:00:00+00:00'",
      );
      expect(leaf(CreatedAt, 'before:2026-03-28', 'Asia/Beirut')).toBe("CreatedAt < '2026-03-27T22:00:00+00:00'");
    });

    it('refuses a day that does not exist and a reversed range', async () => {
      await tmEntityRig(SYSTEM);
      const { DueDate, CreatedAt } = columns('DueDate', 'CreatedAt');
      expect(leaf(DueDate, 'before:2026-02-30')).toBeNull();
      expect(leaf(DueDate, 'before:2026-3-1')).toBeNull();
      expect(leaf(DueDate, 'between:2026-02-01,2026-01-01')).toBeNull();
      expect(leaf(CreatedAt, 'between:2026-02-01,2026-01-01')).toBeNull();
    });
  });

  describe('enums, booleans and navigations', () => {
    it('reads an enum’s values, each once', async () => {
      await tmEntityRig(SYSTEM);
      const { CustomerType } = columns('CustomerType');
      expect(leaf(CustomerType, 'in:Retail,wholesale-trade,Retail')).toBe("CustomerType in ('Retail', 'wholesale-trade')");
    });

    it('refuses a value the enum does not hold', async () => {
      await tmEntityRig(SYSTEM);
      const { CustomerType } = columns('CustomerType');
      expect(leaf(CustomerType, 'in:Retail,Retired')).toBeNull();
      expect(leaf(CustomerType, 'in:retail')).toBeNull();
    });

    it('reads true and false and nothing else', async () => {
      await tmEntityRig(SYSTEM);
      const { IsActive } = columns('IsActive');
      expect(leaf(IsActive, 'is:true')).toBe('IsActive = true');
      expect(leaf(IsActive, 'is:false')).toBe('IsActive = false');
      expect(leaf(IsActive, 'is:yes')).toBeNull();
    });

    it('filters a navigation by the id it holds, through its foreign key', async () => {
      await tmEntityRig({ ...SYSTEM, languages: 2 });
      const { Currency, 'Currency.CreatedBy': creator } = columns('Currency', 'Currency.CreatedBy');
      expect(leaf(Currency, 'in:1012,1015,1012')).toBe('CurrencyId in (1012, 1015)');
      expect(leaf(creator, 'in:7')).toBe('Currency.CreatedById in (7)');
      expect(leaf(Currency, 'in:1.5')).toBeNull();
      expect(leaf(Currency, 'in:-1')).toBeNull();
    });
  });

  describe('columns that take no filter', () => {
    it('gives none for a guid, an image or an expression', async () => {
      await tmEntityRig(SYSTEM);
      const { Token, Image, 'CreditLimit * 2': doubled } = columns('Token', 'Image', { expression: 'CreditLimit * 2', labelKey: 'Acme_Customer' });
      for (const column of [Token, Image, doubled]) {
        expect(column!.filter).toBeNull();
        expect(leaf(column, 'eq:1')).toBeNull();
      }
    });
  });
});

describe('tmDescribeFilter', () => {
  it('describes each kind in English, with each argument its own part', async () => {
    await tmEntityRig(SYSTEM);
    const { Code, CreditLimit, DueDate, CustomerType, IsActive } = columns('Code', 'CreditLimit', 'DueDate', 'CustomerType', 'IsActive');
    expect(describe_(Code, 'contains:jed')).toEqual({
      text: 'Code contains jed',
      parts: [
        { text: 'Code', argument: true },
        { text: ' contains ', argument: false },
        { text: 'jed', argument: true },
      ],
    });
    expect(describe_(CreditLimit, 'between:1500,2000').text).toBe('Credit limit is between 1,500.0000 and 2,000.0000');
    expect(describe_(DueDate, 'before:2026-03-01').text).toBe('Due date is before Mar 1, 2026');
    expect(describe_(IsActive, 'is:false').text).toBe('Active: No');
    expect(describe_(CustomerType, 'in:Retail,wholesale-trade')).toEqual({
      text: 'Type: Retail or Wholesale',
      parts: [
        { text: 'Type', argument: true },
        { text: ': ', argument: false },
        { text: 'Retail', argument: true },
        { text: ' or ', argument: false },
        { text: 'Wholesale', argument: true },
      ],
    });
  });

  it.each([
    ['contains:jed', 'Code contains jed'],
    ['starts-with:jed', 'Code starts with jed'],
    ['ends-with:jed', 'Code ends with jed'],
    ['eq:jed', 'Code is jed'],
    ['ne:jed', 'Code is not jed'],
  ])('describes the text operator of %s in English', async (value, text) => {
    await tmEntityRig(SYSTEM);
    const { Code } = columns('Code');
    expect(describe_(Code, value).text).toBe(text);
  });

  it.each([
    ['lt:5', 'Credit limit is less than 5.0000'],
    ['le:5', 'Credit limit is at most 5.0000'],
    ['eq:5', 'Credit limit is 5.0000'],
    ['ge:5', 'Credit limit is at least 5.0000'],
    ['gt:5', 'Credit limit is greater than 5.0000'],
    ['between:1,2', 'Credit limit is between 1.0000 and 2.0000'],
  ])('describes the number operator of %s in English', async (value, text) => {
    await tmEntityRig(SYSTEM);
    const { CreditLimit } = columns('CreditLimit');
    expect(describe_(CreditLimit, value).text).toBe(text);
  });

  it.each([
    ['before:2026-03-01', 'Due date is before Mar 1, 2026'],
    ['after:2026-03-01', 'Due date is after Mar 1, 2026'],
    ['between:2026-03-01,2026-03-31', 'Due date is between Mar 1, 2026 and Mar 31, 2026'],
  ])('describes the day operator of %s in English', async (value, text) => {
    await tmEntityRig(SYSTEM);
    const { DueDate } = columns('DueDate');
    expect(describe_(DueDate, value).text).toBe(text);
  });

  it('describes an instant filter by its whole days', async () => {
    await tmEntityRig(SYSTEM);
    const { CreatedAt } = columns('CreatedAt');
    expect(describe_(CreatedAt, 'after:2026-03-01').text).toBe('Created is after Mar 1, 2026');
    expect(describe_(CreatedAt, 'between:2026-01-01,2026-01-31').text).toBe('Created is between Jan 1, 2026 and Jan 31, 2026');
  });

  it('describes true and false', async () => {
    await tmEntityRig(SYSTEM);
    const { IsActive } = columns('IsActive');
    expect(describe_(IsActive, 'is:true').text).toBe('Active: Yes');
    expect(describe_(IsActive, 'is:false').text).toBe('Active: No');
  });

  it('describes a related row by its id until it is named', async () => {
    await tmEntityRig(SYSTEM);
    const { Currency } = columns('Currency');
    expect(describe_(Currency, 'in:1012').parts.at(-1)).toEqual({ text: '1012', argument: true });
  });

  it('follows the language: Arabic words and digits around a Latin value', async () => {
    await tmEntityRig({ ...SYSTEM, language: 'ar', tmStrings: { ar: TM_STRINGS_AR }, labels: { en: LABELS, ar: { Code: 'الرمز', Acme_Customer_CreditLimit: 'حد الائتمان' } } });
    const { Code, CreditLimit } = columns('Code', 'CreditLimit');
    expect(describe_(Code, 'contains:jed')).toEqual({
      text: 'الرمز يحتوي على jed',
      parts: [
        { text: 'الرمز', argument: true },
        { text: ' يحتوي على ', argument: false },
        { text: 'jed', argument: true },
      ],
    });
    const range = describe_(CreditLimit, 'between:1,2');
    expect(range.parts.map((part) => part.argument)).toEqual([true, false, true, false, true]);
    expect(range.parts[2]!.text).not.toMatch(/[0-9]/);
  });

  it('joins an Arabic list as alternatives, and words true in Arabic', async () => {
    const ar = { Acme_Customer_CustomerType: 'النوع', Gl_CustomerType_Retail: 'تجزئة', Acme_CustomerType_Wholesale: 'جملة', IsActive: 'نشط' };
    await tmEntityRig({ ...SYSTEM, language: 'ar', tmStrings: { ar: TM_STRINGS_AR }, labels: { en: LABELS, ar } });
    const { CustomerType, IsActive } = columns('CustomerType', 'IsActive');
    expect(describe_(CustomerType, 'in:Retail,wholesale-trade')).toEqual({
      text: 'النوع: تجزئة أو جملة',
      parts: [
        { text: 'النوع', argument: true },
        { text: ': ', argument: false },
        { text: 'تجزئة', argument: true },
        { text: ' أو ', argument: false },
        { text: 'جملة', argument: true },
      ],
    });
    expect(describe_(IsActive, 'is:true').text).toBe('نشط: نعم');
  });

  it('joins an Amharic list as alternatives', async () => {
    await tmEntityRig({ ...SYSTEM, language: 'am' });
    const { CustomerType } = columns('CustomerType');
    const parts = describe_(CustomerType, 'in:Retail,wholesale-trade').parts;
    expect(parts.map((part) => part.argument)).toEqual([true, false, true, false, true]);
    expect(parts[3]).toEqual({ text: ' ወይም ', argument: false });
  });

  it('puts the words where the language’s message puts them', async () => {
    const tm = { filter: { describe: { contains: '{label} {value} ይዟል' } } };
    await tmEntityRig({ ...SYSTEM, language: 'am', tmStrings: { am: tm } });
    const { Code } = columns('Code');
    expect(describe_(Code, 'contains:jed')).toEqual({
      text: 'Code jed ይዟል',
      parts: [
        { text: 'Code', argument: true },
        { text: ' ', argument: false },
        { text: 'jed', argument: true },
        { text: ' ይዟል', argument: false },
      ],
    });
  });

  it('never leaves a placeholder in the text, even for a value that is one', async () => {
    await tmEntityRig(SYSTEM);
    const { Code } = columns('Code');
    const odd = String.fromCharCode(0xe001);
    const description = describe_(Code, `eq:${odd}`);
    expect(description.parts.at(-1)).toEqual({ text: odd, argument: true });
    expect(description.parts.filter((part) => part.argument)).toHaveLength(2);
  });
});

describe('TmFilterDescriptionText', () => {
  it('isolates each argument and keeps the message’s spacing', async () => {
    await tmEntityRig(SYSTEM);
    const { Code } = columns('Code');

    /** A host for the description. */
    @Component({ imports: [TmFilterDescriptionText], template: '<tm-filter-description [description]="description()" />' })
    class Host {
      /** The description shown. */
      readonly description = signal(describe_(Code, 'contains:jed'));
    }

    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement.querySelector('tm-filter-description');
    expect(element.textContent).toBe('Code contains jed');
    expect([...element.querySelectorAll('bdi')].map((bdi) => bdi.textContent)).toEqual(['Code', 'jed']);
  });
});

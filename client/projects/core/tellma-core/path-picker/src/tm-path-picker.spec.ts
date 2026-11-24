// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { TmLocalization, type TmEntitiesChunk } from '@tellma/core';
import { TmPathPickerHarness, tmTestEntities } from '@tellma/core-testing';
import { tmGregorianCalendar } from '@tellma/core-ui/l10n';
import { TM_STRINGS_AR } from '@tellma/locale-ar/platform-strings';
import { TM_QUERYEX_STRINGS_AR } from '@tellma/locale-ar/queryex-strings';

import { tmEntityRig, type TmEntityRigOptions } from '../../src/entity/tm-entity.testing.util';
import { tmSettleUntil } from '../../src/routes/tm-app.testing.util';
import { TmPathPicker } from './tm-path-picker';
import type { TmPathNode } from './tm-path-tree';

// The path picker over the recorded entity model, patched with a date, a
// date-time and a file: the node rules (scalars, one node per multilingual
// group, navigations as branches, image and file navigations, hidden
// foreign keys, non-queryable fields and unreachable paths), the order, the
// twins of the column keys, the date parts in each calendar, the search's
// depth and cap, the drag payload, activation, multiple selection, and the
// picker's own strings in Arabic.

/** The English labels the recorded model's fields read. */
const LABELS: Readonly<Record<string, string>> = {
  IsActive: 'Active',
  CreatedAt: 'Created at',
  CreatedBy: 'Created by',
  ModifiedAt: 'Modified at',
  ModifiedBy: 'Modified by',
  Acme_Customer_CreditLimit: 'Credit limit',
  Acme_Customer_DueDate: 'Due date',
  Acme_Customer_PostedOn: 'Posted on',
  Acme_Customer_Attachment: 'Attachment',
  Gl_Center_ManagerId: 'Manager',
  Core_User: 'User',
  Gl_Center: 'Center',
  Core_Blob: 'File',
};

/**
 * The recorded model, with a date, a date-time and a file on customers.
 *
 * @param patch - A last change to the customer's descriptor.
 * @returns The patched chunk.
 */
function chunk(patch: (customer: { properties: unknown[]; navigations: unknown[] }) => void = () => undefined): TmEntitiesChunk {
  const patched = tmTestEntities().chunk;
  const property = (name: string, type: string, extra: object = {}): unknown => ({
    name,
    json: name[0]!.toLowerCase() + name.slice(1),
    type,
    queryable: true,
    nullable: true,
    labelKey: `Acme_Customer_${name}`,
    excel: true,
    ownership: 'editable',
    ...extra,
  });
  const customer = patched.entities.find((entity) => entity.name === 'acme.Customer')! as unknown as { properties: unknown[]; navigations: unknown[] };
  customer.properties.push(
    property('DueDate', 'date'),
    property('PostedOn', 'datetime'),
    property('AttachmentId', 'integer', {
      foreignKeyOf: 'Attachment',
      blob: { kind: 'customer-file', accept: ['application/pdf'], maxSize: 1048576, image: null, thumbnail: false, readAccess: 'owner-read' },
    }),
  );
  customer.navigations.push({
    name: 'Attachment',
    json: 'attachment',
    foreignKey: 'AttachmentId',
    target: 'core.Blob',
    kind: 'reference',
    nullable: true,
    labelKey: 'Acme_Customer_Attachment',
  });
  patch(customer);
  return patched;
}

/**
 * The rig's options for a user who reads everything, over the patched model.
 *
 * @param options - Options beside those.
 * @returns The options.
 */
function system(options: TmEntityRigOptions = {}): TmEntityRigOptions {
  return { access: { isSystem: true }, chunk: chunk(), labels: { en: LABELS }, ...options };
}

/** A picker whose inputs a test sets. */
@Component({
  imports: [TmPathPicker],
  template: `
    <tm-path-picker
      ariaLabel="Fields"
      [root]="root()"
      [mode]="mode()"
      [dateParts]="dateParts()"
      [draggable]="draggable()"
      [selection]="selection()"
      [accept]="accept()"
      [(selected)]="selected"
      (activate)="activated.push($event.key)"
    />
  `,
})
class PickerHost {
  /** The root entity. */
  readonly root = signal('acme.Customer');
  /** The keys' mode. */
  readonly mode = signal<'columns' | 'fields'>('fields');
  /** Whether date fields branch into their parts. */
  readonly dateParts = signal(false);
  /** Whether the fields drag. */
  readonly draggable = signal(false);
  /** The selection. */
  readonly selection = signal<'none' | 'single' | 'multiple'>('none');
  /** Which fields the host takes. */
  readonly accept = signal<((node: TmPathNode) => boolean) | undefined>(undefined);
  /** The selected keys. */
  readonly selected = signal<ReadonlySet<string>>(new Set());
  /** The activated keys, in order. */
  readonly activated: string[] = [];
}

/**
 * Starts the app and a picker.
 *
 * @param options - The rig's options.
 * @param setup - Sets the picker's inputs before it first renders.
 * @returns The fixture and the harness.
 */
async function picker(
  options: TmEntityRigOptions = system(),
  setup: (host: PickerHost) => void = () => undefined,
): Promise<{ fixture: ComponentFixture<PickerHost>; harness: TmPathPickerHarness }> {
  await tmEntityRig(options);
  const fixture = TestBed.createComponent(PickerHost);
  setup(fixture.componentInstance);
  await fixture.whenStable();
  return { fixture, harness: await TestbedHarnessEnvironment.loader(fixture).getHarness(TmPathPickerHarness) };
}

/**
 * The text a drag of a row carries, or `null` when the row does not drag.
 *
 * @param fixture - The fixture.
 * @param key - The row's key.
 * @returns The text.
 */
function dragText(fixture: ComponentFixture<unknown>, key: string): string | null {
  const row = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(`.tm-tree__item[data-key="${CSS.escape(key)}"] > .tm-tree__row`)!;
  if (row.getAttribute('draggable') !== 'true') {
    return null;
  }
  // A plain object: outside a real drag the browser's own transfer ignores the effects the source sets.
  const data = new Map<string, string>();
  const transfer = { effectAllowed: 'none', dropEffect: 'none', types: [], setData: (type: string, value: string) => data.set(type, value), getData: () => '' };
  const event = new DragEvent('dragstart', { bubbles: true, cancelable: true });
  Object.defineProperty(event, 'dataTransfer', { value: transfer });
  row.dispatchEvent(event);
  return data.get('text/plain') ?? null;
}

describe('TmPathPicker', () => {
  afterEach(() => TestBed.resetTestingModule());

  describe('the fields', () => {
    it('lists scalars, one node per multilingual group, and navigations, and hides foreign keys, non-queryable fields and twins', async () => {
      const { harness } = await picker(system({ languages: 3 }));
      const keys = await harness.getKeys();
      expect(keys).toEqual(expect.arrayContaining(['Code', 'Name', 'CreditLimit', 'DueDate', 'Currency', 'Image', 'Attachment', 'Extra', 'CreatedBy']));
      for (const hidden of ['CurrencyId', 'ImageId', 'AttachmentId', 'CreatedById', 'Notes', 'Name2', 'Name3']) {
        expect(keys).not.toContain(hidden);
      }
      expect(await harness.getKind('Name')).toBe('text');
      expect(await harness.getKind('Code')).toBe('value');
      expect(await harness.getKind('Currency')).toBe('navigation');
      expect(await harness.getKind('Image')).toBe('image');
      expect(await harness.getKind('Attachment')).toBe('file');
      expect(await harness.getPill('Currency')).toBe('Currency');
      expect(await harness.getPill('Code')).toBeNull();
    });

    it('orders by label, with the key and the audit fields last', async () => {
      const { harness } = await picker();
      const keys = await harness.getKeys();
      const labels = await harness.getLabels();
      expect(keys.slice(-5)).toEqual(['Id', 'CreatedAt', 'CreatedBy', 'ModifiedAt', 'ModifiedBy']);
      const head = labels.slice(0, -5);
      expect(head).toEqual([...head].sort(new Intl.Collator('en').compare));
    });

    it('branches a navigation into its target, an image and a file into their blobs, and a date into nothing without date parts', async () => {
      const { harness } = await picker();
      await (await harness.getNode('Currency')).expand();
      expect(await harness.getKeys()).toEqual(expect.arrayContaining(['Currency.Code', 'Currency.DecimalPlaces']));
      await (await harness.getNode('Attachment')).expand();
      expect(await harness.getKeys()).toEqual(expect.arrayContaining(['Attachment.FileName']));
      expect(await (await harness.getNode('DueDate')).isExpandable()).toBe(false);
    });

    it('hides the paths the user cannot reach, and a navigation whose foreign key is not queryable', async () => {
      const unqueryable = chunk((customer) => {
        const currencyId = customer.properties.find((property) => (property as { name: string }).name === 'CurrencyId') as { queryable: boolean };
        currencyId.queryable = false;
      });
      // Customers are read in full; users and blobs only through their projections.
      const { harness } = await picker({
        chunk: unqueryable,
        labels: { en: LABELS },
        access: { securables: [{ resource: 'acme.Customer', action: 'Read', hasFilter: false }] },
      });
      expect(await harness.getKeys()).not.toContain('Currency');

      await (await harness.getNode('CreatedBy')).expand();
      const keys = await harness.getKeys();
      expect(keys).toContain('CreatedBy.Name');
      expect(keys).not.toContain('CreatedBy.Email');
      await (await harness.getNode('Image')).expand();
      expect(await harness.getKeys()).toContain('Image.FileName');
      expect(await harness.getKeys()).not.toContain('Image.State');
    });
  });

  describe('column keys', () => {
    it("branches a multilingual group into its twins, and keeps images and files as leaves", async () => {
      const { harness } = await picker(system({ languages: 3 }), (host) => host.mode.set('columns'));
      await (await harness.getNode('Name')).expand();
      expect(await harness.getKeys()).toEqual(expect.arrayContaining(['Name', 'Name2', 'Name3']));
      expect(await (await harness.getNode('Image')).isExpandable()).toBe(false);
      expect(await (await harness.getNode('Attachment')).isExpandable()).toBe(false);
    });

    it('keeps a group a leaf when the tenant has one language', async () => {
      const { harness } = await picker(system({ languages: 1 }), (host) => host.mode.set('columns'));
      expect(await (await harness.getNode('Name')).isExpandable()).toBe(false);
    });
  });

  describe('date parts', () => {
    it("branches a date into its Gregorian parts, a date-time and an instant into theirs and the day's start", async () => {
      const { harness } = await picker(system(), (host) => host.dateParts.set(true));
      await (await harness.getNode('DueDate')).expand();
      expect((await harness.getKeys()).filter((key) => key.includes('(DueDate'))).toEqual([
        'startOfYear(DueDate)',
        'startOfQuarter(DueDate)',
        'startOfMonth(DueDate)',
        'startOfWeek(DueDate)',
        'quarter(DueDate)',
        'month(DueDate)',
        'weekday(DueDate)',
      ]);
      expect(await harness.getLabels()).toEqual(expect.arrayContaining(['Year', 'Quarter', 'Month', 'Week', 'Quarter of year', 'Month of year', 'Day of week']));

      await (await harness.getNode('PostedOn')).expand();
      expect(await harness.getKeys()).toContain('startOfDay(PostedOn)');
      await (await harness.getNode('CreatedAt')).expand();
      expect(await harness.getKeys()).toEqual(expect.arrayContaining(['startOfMonth(local(CreatedAt))', 'startOfDay(local(CreatedAt))']));
    });

    it.each([['uq'], ['et']])("passes the %s calendar to the parts that take one", async (code) => {
      const { fixture, harness } = await picker(system({ calendars: [code, 'gc'] }), (host) => host.dateParts.set(true));
      // The calendar loads from its own chunk.
      await tmSettleUntil(() => TestBed.inject(TmLocalization).calendarCode() === code, `the ${code} calendar loads`);
      await fixture.whenStable();
      await (await harness.getNode('DueDate')).expand();
      const keys = await harness.getKeys();
      expect(keys).toEqual(expect.arrayContaining([`startOfYear(DueDate, '${code}')`, `month(DueDate, '${code}')`, 'startOfWeek(DueDate)', 'weekday(DueDate)']));
      expect(await harness.getLabels()).not.toContain('Year (Gregorian)');
    });

    it('labels the calendar parts Gregorian under a calendar Queryex does not bind', async () => {
      const { fixture, harness } = await picker(
        system({ calendars: ['xx', 'gc'], calendarDefinitions: [{ code: 'xx', load: () => Promise.resolve(tmGregorianCalendar()) }] }),
        (host) => host.dateParts.set(true),
      );
      await tmSettleUntil(() => TestBed.inject(TmLocalization).calendarCode() === 'xx', 'the custom calendar loads');
      await fixture.whenStable();
      await (await harness.getNode('DueDate')).expand();
      expect(await harness.getKeys()).toContain('startOfYear(DueDate)');
      expect(await harness.getLabels()).toEqual(expect.arrayContaining(['Year (Gregorian)', 'Month of year (Gregorian)', 'Week', 'Day of week']));
    });
  });

  describe('search', () => {
    it('finds fields by label and name three navigation steps deep, inside their muted ancestors', async () => {
      const { harness } = await picker(system(), (host) => host.root.set('gl.Center'));
      await harness.search('code');
      const keys = await harness.getKeys();
      expect(keys).toEqual(expect.arrayContaining(['Code', 'Parent', 'Parent.Code', 'Parent.Parent.Code', 'Parent.Parent.Parent.Code']));
      expect(keys).not.toContain('Parent.Parent.Parent.Parent.Code');
      expect(await (await harness.getNode('Parent')).isContext()).toBe(true);
      expect(await (await harness.getNode('Code')).isContext()).toBe(false);
      expect(await harness.getStatusText()).toBe('4 fields');

      await harness.search('');
      expect(await harness.getStatusText()).toBe('');
      expect(await harness.getKeys()).not.toContain('Parent.Code');
    });

    it('shows at most 200 matches, and says when none match', async () => {
      const { harness } = await picker(system(), (host) => host.root.set('gl.Center'));
      await harness.search('a');
      expect(await harness.getStatusText()).toBe('200 fields');
      const nodes = await harness.getNodes();
      const matches = (await Promise.all(nodes.map((node) => node.isContext()))).filter((context) => !context);
      expect(matches).toHaveLength(200);

      await harness.search('zzz');
      expect(await harness.getStatusText()).toBe('No fields match');
      expect(await harness.getKeys()).toEqual([]);
    });
  });

  describe('drag and activation', () => {
    it('drags each field as its Queryex text: the path, the foreign key or the part', async () => {
      const { fixture, harness } = await picker(system(), (host) => {
        host.draggable.set(true);
        host.dateParts.set(true);
      });
      expect(dragText(fixture, 'Code')).toBe('Code');
      expect(dragText(fixture, 'Currency')).toBe('CurrencyId');
      await (await harness.getNode('Currency')).expand();
      expect(dragText(fixture, 'Currency.Code')).toBe('Currency.Code');
      await (await harness.getNode('DueDate')).expand();
      expect(dragText(fixture, 'startOfMonth(DueDate)')).toBe('startOfMonth(DueDate)');
    });

    it('activates a field on Enter and on a double-click, and never a field the host refuses', async () => {
      const { fixture, harness } = await picker(system(), (host) => host.accept.set((node) => node.type === 'string' || node.kind === 'navigation'));
      // A refused leaf is hidden; a refused branch shows, for its accepted fields, but does not drag.
      expect(await harness.getKeys()).not.toContain('CreditLimit');
      expect(await harness.getKeys()).toContain('Image');
      fixture.componentInstance.draggable.set(true);
      await fixture.whenStable();
      expect(dragText(fixture, 'Image')).toBeNull();
      expect(dragText(fixture, 'Code')).toBe('Code');

      await (await harness.getNode('Code')).doubleClick();
      await (await harness.getNode('Currency')).focus();
      await (await harness.getNode('Currency')).press('Enter');
      await (await harness.getNode('Image')).focus();
      await (await harness.getNode('Image')).press('Enter');
      expect(fixture.componentInstance.activated).toEqual(['Code', 'Currency']);
    });

    it('selects many fields, dropping the ones the host refuses', async () => {
      const { fixture, harness } = await picker(system(), (host) => {
        host.selection.set('multiple');
        host.accept.set((node) => node.type === 'string');
      });
      await (await harness.getNode('Code')).click();
      await (await harness.getNode('Currency')).click();
      await (await harness.getNode('Rank')).click();
      expect([...fixture.componentInstance.selected()].sort()).toEqual(['Code', 'Rank']);
      expect(await (await harness.getNode('Currency')).isSelected()).toBe(false);
    });
  });

  describe('Arabic', () => {
    it("reads the search field, the search's count and the date parts in Arabic", async () => {
      const { harness } = await picker(system({ language: 'ar', tmStrings: { ar: { ...TM_STRINGS_AR, ...TM_QUERYEX_STRINGS_AR } } }), (host) => host.dateParts.set(true));
      expect(await (await harness.getSearchField()).getAriaLabel()).toBe('البحث في الحقول');
      await (await harness.getNode('DueDate')).expand();
      expect(await harness.getLabels()).toEqual(expect.arrayContaining(['السنة', 'ربع السنة', 'يوم الأسبوع']));

      await harness.search('zzz');
      expect(await harness.getStatusText()).toBe('لا توجد حقول مطابقة');
    });
  });
});

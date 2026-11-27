// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, inject, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import type { ComponentHarness, ComponentHarnessConstructor, HarnessLoader } from '@angular/cdk/testing';
import { form, type FieldTree } from '@angular/forms/signals';
import {
  TmApiError,
  TmEntities,
  type TmBlobDescriptor,
  type TmDecimal,
  type TmNavigationDescriptor,
  type TmPropertyDescriptor,
  type TmValueDescriptor,
} from '@tellma/core';
import { TmEntityFieldHarness, TmEntityMultiFieldHarness } from '@tellma/core-testing';
import { TmUploads } from '@tellma/core/uploads';
import { TmFilePicker } from '@tellma/core-ui/files';
import { TmFormField } from '@tellma/core-ui/form-field';
import { TmImage } from '@tellma/core-ui/image';
import {
  TmCheckboxHarness,
  TmChipHarness,
  TmDatePickerHarness,
  TmFormFieldHarness,
  TmInputHarness,
  TmMultiSelectHarness,
  TmNumberHarness,
  TmSelectHarness,
} from '@tellma/core-ui-testing';

import { tmSettle } from '../../src/routes/tm-app.testing.util';
import { tmEntityRig, type TmEntityRig } from '../../src/entity/tm-entity.testing.util';
import { TmValueEditor } from './tm-value-editor';

// The value editor in a form over the entity registry's test app: one
// field per type, each in its tm-form-field, bound to a Signal Forms model;
// the read-only rules; and the image and file pickers' uploads, listed in
// the host's TmUploads while they run.

/** One field the host renders: its name, what it edits and its starting value. */
interface TmTestField {
  /** The model member and test id. */
  readonly name: string;
  /** What the editor edits. */
  readonly property: TmValueDescriptor | TmPropertyDescriptor | TmNavigationDescriptor;
  /** The starting value. */
  readonly value: unknown;
}

/** The fields under test, set before the host is created. */
let fields: readonly TmTestField[] = [];

/** Whether the host's record is new. */
let creating = false;

/** A form of one editor per field, each in its form field, with the host's uploads. */
@Component({
  imports: [TmFormField, TmValueEditor],
  providers: [TmUploads],
  template: `
    @for (entry of fields; track entry.name) {
      <tm-form-field [label]="entry.name" [attr.data-testid]="entry.name">
        <tm-value-editor [property]="entry.property" [field]="fieldOf(entry.name)" [create]="create" [path]="entry.name" />
      </tm-form-field>
    }
  `,
})
class TmEditorHost {
  /** The fields. */
  readonly fields = fields;
  /** Whether the record is new. */
  readonly create = creating;
  /** The host's uploads. */
  readonly uploads = inject(TmUploads);
  /** The model. */
  readonly model = signal<Record<string, unknown>>(Object.fromEntries(fields.map((entry) => [entry.name, entry.value])));
  /** The form. */
  readonly form = form(this.model);

  /**
   * A field of the form.
   *
   * @param name - Its name.
   * @returns The field.
   */
  fieldOf(name: string): FieldTree<unknown> {
    return (this.form as unknown as Record<string, FieldTree<unknown>>)[name]!;
  }
}

/** A started host and its harness loader. */
interface TmEditorTest {
  /** The rig. */
  readonly rig: TmEntityRig;
  /** The fixture. */
  readonly fixture: ComponentFixture<TmEditorHost>;
  /** The host. */
  readonly host: TmEditorHost;
  /** The harness loader. */
  readonly loader: HarnessLoader;
}

/**
 * Starts the host over fields made from the registry.
 *
 * @param make - The fields, given the registry.
 * @param create - Whether the record is new.
 * @returns The test.
 */
async function start(make: (entities: TmEntities) => readonly TmTestField[], create = false): Promise<TmEditorTest> {
  const rig = await tmEntityRig({ access: { isSystem: true }, languages: 2 });
  fields = make(rig.entities);
  creating = create;
  const fixture = TestBed.createComponent(TmEditorHost);
  await fixture.whenStable();
  await tmSettle();
  return { rig, fixture, host: fixture.componentInstance, loader: TestbedHarnessEnvironment.loader(fixture) };
}

/**
 * A harness inside one field.
 *
 * @param loader - The loader.
 * @param name - The field's name.
 * @param type - The harness type.
 * @returns The harness.
 */
async function inField<T extends ComponentHarness>(loader: HarnessLoader, name: string, type: ComponentHarnessConstructor<T>): Promise<T> {
  return (await loader.getChildLoader(`[data-testid="${name}"]`)).getHarness(type);
}

/**
 * A field's form-field harness, by its test id on the field itself.
 *
 * @param loader - The loader.
 * @param name - The field's name.
 * @returns The harness.
 */
async function formField(loader: HarnessLoader, name: string): Promise<TmFormFieldHarness> {
  for (const field of await loader.getAllHarnesses(TmFormFieldHarness)) {
    if ((await (await field.host()).getAttribute('data-testid')) === name) {
      return field;
    }
  }
  throw new Error(`No field is named '${name}'.`);
}

/**
 * Pastes text into a field's input as one edit, as a paste or an
 * autofill does, and leaves the input.
 *
 * @param fixture - The host's fixture.
 * @param name - The field's name.
 * @param text - The text.
 */
async function paste(fixture: ComponentFixture<TmEditorHost>, name: string, text: string): Promise<void> {
  const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(`[data-testid="${name}"] input`)!;
  input.focus();
  input.value = text;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  input.blur();
  await fixture.whenStable();
}

/**
 * Waits for the next frame and the reads it starts: a display name read
 * for an unknown row flushes on a frame, which must not outlive the test.
 *
 * @param fixture - The host's fixture.
 */
async function frame(fixture: ComponentFixture<TmEditorHost>): Promise<void> {
  await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));
  await tmSettle();
  await fixture.whenStable();
}

/** A blob the fake API's upload returns. */
const BLOB: TmBlobDescriptor = { id: 88, kind: 'customer-image', contentType: 'image/webp', size: '512' as TmDecimal };

/** A PNG of a size. */
async function png(width: number, height: number): Promise<Blob> {
  const canvas = new OffscreenCanvas(width, height);
  canvas.getContext('2d')!.fillRect(0, 0, width, height);
  return canvas.convertToBlob({ type: 'image/png' });
}

/**
 * Waits until a condition holds, as an image's crop decodes and encodes
 * off the test's turn.
 *
 * @param condition - The condition.
 */
async function until(condition: () => boolean): Promise<void> {
  for (let waited = 0; !condition(); waited += 10) {
    if (waited > 5000) {
      throw new Error('The condition never held.');
    }
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
}

/** Settles an upload's microtasks and the view. */
async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  for (let turn = 0; turn < 5; turn++) {
    await new Promise((resolve) => setTimeout(resolve));
  }
  await fixture.whenStable();
}

describe('tm-value-editor', () => {
  afterEach(() => {
    fields = [];
    creating = false;
  });

  describe('controls by type', () => {
    it('edits short text in an input with its maximum length, and long text in a textarea', async () => {
      const { loader, host, fixture } = await start((entities) => [
        { name: 'code', property: entities.get('acme.Customer').property('Code')!, value: 'C-1' },
        { name: 'notes', property: { type: 'string', nullable: true }, value: '' },
      ]);
      const code = await inField(loader, 'code', TmInputHarness);
      expect(await code.getValue()).toBe('C-1');
      await code.setValue('C-2');
      expect(host.model()['code']).toBe('C-2');
      const input = fixture.nativeElement.querySelector('[data-testid="code"] input') as HTMLInputElement;
      expect(input.maxLength).toBe(50);
      const notes = await inField(loader, 'notes', TmInputHarness);
      expect((await notes.hostTagName()).toLowerCase()).toBe('textarea');
      expect((fixture.nativeElement.querySelector('[data-testid="notes"] textarea') as HTMLTextAreaElement).rows).toBe(3);
      expect(await (await formField(loader, 'code')).getLabelText()).toBe('code');
    });

    it('edits a slot in its content direction', async () => {
      const { fixture } = await start((entities) => [{ name: 'name2', property: entities.get('acme.Customer').property('Name2')!, value: 'أكمي' }]);
      expect((fixture.nativeElement.querySelector('[data-testid="name2"] input') as HTMLInputElement).dir).toBe('rtl');
    });

    it('edits an integer as a number', async () => {
      const { loader, host } = await start((entities) => [
        { name: 'places', property: entities.get('acme.Currency').property('DecimalPlaces')!, value: 2 },
      ]);
      const places = await inField(loader, 'places', TmNumberHarness);
      await places.setText('3');
      await places.blur();
      expect(host.model()['places']).toBe(3);
    });

    it('edits a decimal and a long as decimal text, at the decimal’s scale', async () => {
      const { loader, host } = await start((entities) => [
        { name: 'creditLimit', property: entities.get('acme.Customer').property('CreditLimit')!, value: '1500.2500' },
        { name: 'externalReference', property: entities.get('acme.Customer').property('ExternalReference')!, value: '42' },
      ]);
      const limit = await inField(loader, 'creditLimit', TmNumberHarness);
      expect(await limit.getText()).toBe('1,500.2500');
      await limit.setText('2000.5');
      await limit.blur();
      expect(host.model()['creditLimit']).toBe('2000.5');
      const reference = await inField(loader, 'externalReference', TmNumberHarness);
      await reference.setText('77');
      await reference.blur();
      expect(host.model()['externalReference']).toBe('77');
    });

    it('keeps a decimal’s value, and holds the form invalid, while its text reads as no number', async () => {
      const { loader, host, fixture } = await start((entities) => [
        { name: 'creditLimit', property: entities.get('acme.Customer').property('CreditLimit')!, value: '1500.2500' },
      ]);
      const limit = await inField(loader, 'creditLimit', TmNumberHarness);
      const field = await formField(loader, 'creditLimit');

      // Text that is no number.
      await paste(fixture, 'creditLimit', '12abc');
      expect(host.model()['creditLimit']).toBe('1500.2500');
      expect(host.form().invalid()).toBe(true);
      expect(await limit.isInvalid()).toBe(true);
      expect(await field.getErrorText()).toContain('1,234.5000');

      // A pasted 19-digit amount, more than a number holds exactly.
      await paste(fixture, 'creditLimit', '1,234,567,890,123,456.78');
      expect(host.model()['creditLimit']).toBe('1500.2500');
      expect(host.form().invalid()).toBe(true);
      expect(await field.getErrorText()).toContain('15');

      // Text that reads again clears the error and writes the value.
      await paste(fixture, 'creditLimit', '2000');
      expect(host.model()['creditLimit']).toBe('2000');
      expect(host.form().invalid()).toBe(false);
    });

    it('leaves a 28-digit stored decimal as it is when the user only passes through it', async () => {
      const { loader, host, fixture } = await start(() => [
        { name: 'balance', property: { type: 'decimal', nullable: true, scale: 0 }, value: '1234567890123456789012345678' },
      ]);
      const balance = await inField(loader, 'balance', TmNumberHarness);
      await balance.focus();
      await balance.blur();
      await fixture.whenStable();
      expect(host.model()['balance']).toBe('1234567890123456789012345678');
      expect(host.form().invalid()).toBe(false);
      expect(host.form().dirty()).toBe(false);
    });

    it('re-enters an equal decimal without rewriting its text', async () => {
      const { host, fixture } = await start((entities) => [
        { name: 'creditLimit', property: entities.get('acme.Customer').property('CreditLimit')!, value: '1500.2500' },
      ]);
      await paste(fixture, 'creditLimit', '1500.25');
      expect(host.model()['creditLimit']).toBe('1500.2500');
    });

    it('edits a flag in a checkbox and a date in a date picker', async () => {
      const { loader, host } = await start(() => [
        { name: 'active', property: { type: 'bool', nullable: false }, value: false },
        { name: 'due', property: { type: 'date', nullable: true }, value: '2026-03-01' },
      ]);
      const active = await inField(loader, 'active', TmCheckboxHarness);
      await active.toggle();
      expect(host.model()['active']).toBe(true);
      const due = await inField(loader, 'due', TmDatePickerHarness);
      expect(await due.getText()).not.toBe('');
    });

    it('edits an enum in a select, in the enum’s order, and a list of them in a multi-select', async () => {
      const { loader, host } = await start((entities) => [
        { name: 'type', property: entities.get('acme.Customer').property('CustomerType')!, value: 'wholesale-trade' },
        { name: 'types', property: { type: 'enum', nullable: false, enum: 'acme.CustomerType', array: true }, value: [] },
      ]);
      const type = await inField(loader, 'type', TmSelectHarness);
      await type.open();
      const options = await type.getOptions();
      expect(options.length).toBe(2);
      expect(await options[0]!.getText()).toBe('Retail');
      await type.selectOption('Retail');
      expect(host.model()['type']).toBe('Retail');

      const types = await inField(loader, 'types', TmMultiSelectHarness);
      await types.open();
      await types.toggle('Retail');
      expect(host.model()['types']).toEqual(['Retail']);
    });
  });

  describe('read-only values', () => {
    it('shows a server-owned value, an instant and JSON as read-only text', async () => {
      const { loader } = await start((entities) => {
        const customer = entities.get('acme.Customer');
        return [
          { name: 'active', property: customer.property('IsActive')!, value: true },
          { name: 'created', property: customer.property('CreatedAt')!, value: '2026-01-15T10:00:00+00:00' },
          { name: 'notes', property: customer.property('Notes')!, value: { a: 1 } },
        ];
      });
      const active = await inField(loader, 'active', TmInputHarness);
      expect([await active.isReadonly(), await active.getValue()]).toEqual([true, 'Yes']);
      const created = await inField(loader, 'created', TmInputHarness);
      expect(await created.isReadonly()).toBe(true);
      expect(await created.getValue()).toContain('2026');
      expect(await (await inField(loader, 'notes', TmInputHarness)).getValue()).toBe('{"a":1}');
    });

    it('edits a write-once value only while the record is created', async () => {
      const kind = (entities: TmEntities): readonly TmTestField[] => [
        { name: 'kind', property: entities.get('core.User').property('Kind')!, value: 'Individual' },
      ];
      const existing = await start(kind);
      expect(await (await inField(existing.loader, 'kind', TmInputHarness)).isReadonly()).toBe(true);
      TestBed.resetTestingModule();
      const created = await start(kind, true);
      expect(await (await created.loader.getChildLoader('[data-testid="kind"]')).getAllHarnesses(TmSelectHarness)).toHaveLength(1);
    });

    it('chooses a related row in the entity field, by its display name', async () => {
      const { loader, rig, fixture, host } = await start((entities) => [
        { name: 'currency', property: entities.get('acme.Customer').navigation('Currency')!, value: 3 },
      ]);
      rig.names.remember('acme.Currency', [{ id: 3, code: 'SAR' }]);
      rig.api.respond('currencies/query', {
        rows: {
          columns: ['Id', 'Code'].map((name) => ({ name, type: name === 'Id' ? 'Numeric' : 'String', nullable: false, path: [name], groupingKey: false })),
          rows: [[3, 'SAR'], [4, 'USD']],
        },
      });
      await fixture.whenStable();
      const currency = await inField(loader, 'currency', TmEntityFieldHarness);
      expect([await currency.isReadonly(), await currency.getText()]).toEqual([false, 'SAR']);

      await currency.pick('USD');
      expect(host.model()['currency']).toBe(4);
      expect(await (await formField(loader, 'currency')).getLabelText()).toBe('currency');
      await frame(fixture);
    });

    it('chooses a list of related rows in the entity list field', async () => {
      const { loader, rig, fixture } = await start(() => [
        { name: 'currencies', property: { type: 'integer', nullable: false, target: 'acme.Currency', array: true }, value: [3] },
      ]);
      rig.names.remember('acme.Currency', [{ id: 3, code: 'SAR' }]);
      await fixture.whenStable();
      const currencies = await inField(loader, 'currencies', TmEntityMultiFieldHarness);
      expect(await currencies.getChipLabels()).toEqual(['SAR']);
      await frame(fixture);
    });

    it('shows a related row read-only where the user may not write it', async () => {
      const { loader, rig, fixture } = await start((entities) => [
        { name: 'createdBy', property: entities.get('acme.Customer').navigation('CreatedBy')!, value: 42 },
      ]);
      rig.names.remember('core.User', [{ id: 42, name: 'Farah' }]);
      await fixture.whenStable();
      const user = await inField(loader, 'createdBy', TmInputHarness);
      expect(await user.isReadonly()).toBe(true);
      await frame(fixture);
    });

    it('keeps a related row’s key property read-only, by its ungrouped id', async () => {
      const { loader } = await start((entities) => [
        { name: 'currencyId', property: entities.get('acme.Customer').property('CurrencyId')!, value: 1284 },
      ]);
      const currency = await inField(loader, 'currencyId', TmInputHarness);
      // An id is written ungrouped.
      expect([await currency.isReadonly(), await currency.getValue()]).toEqual([true, '1284']);
    });
  });

  describe('uploads', () => {
    it('uploads a picked image as the framed square, pending until it settles', async () => {
      const { host, fixture, rig } = await start((entities) => [
        { name: 'imageId', property: entities.get('acme.Customer').property('ImageId')!, value: null },
      ]);
      let finish!: (blob: TmBlobDescriptor) => void;
      rig.api.respond('blobs/customer-image', () => new Promise<TmBlobDescriptor>((resolve) => (finish = resolve)));

      const image = fixture.debugElement.query(By.directive(TmImage)).componentInstance as TmImage;
      image.imageChange.emit({ blob: await png(300, 200), fit: { rect: { x: 0, y: 0, width: 2 / 3, height: 1 }, focal: { x: 1 / 3, y: 0.5 } } });
      await until(() => rig.api.calls().some((call) => call.kind === 'upload'));
      await settle(fixture);
      expect([...host.uploads.running()]).toEqual(['imageId']);
      const group = fixture.nativeElement.querySelector('[data-testid="imageId"] [role="group"]') as HTMLElement;
      expect(group.getAttribute('aria-busy')).toBe('true');

      // The square went up in the kind's format and largest edge.
      const sent = rig.api.calls().find((call) => call.kind === 'upload')!.body as File;
      expect(sent.type).toBe('image/webp');
      const bitmap = await createImageBitmap(sent);
      expect([bitmap.width, bitmap.height]).toEqual([200, 200]);

      finish(BLOB);
      await settle(fixture);
      expect(host.uploads.running().size).toBe(0);
      expect(host.uploads.staged('imageId')).toEqual(BLOB);
      expect(host.model()['imageId']).toBe(88);
      expect(group.getAttribute('aria-busy')).toBeNull();
    });

    it('uploads a picked file and shows its name in a chip', async () => {
      const { host, fixture, rig, loader } = await start((entities) => [
        { name: 'fileId', property: entities.get('core.OutgoingEmailAttachment').property('FileId')!, value: null },
      ]);
      rig.api.respond('blobs/email-attachment', { ...BLOB, id: 51, kind: 'email-attachment', fileName: 'invoice.pdf' });
      const picker = fixture.debugElement.query(By.directive(TmFilePicker)).injector.get(TmFilePicker);
      picker.filesSelected.emit({ accepted: [new File(['%PDF'], 'invoice.pdf', { type: 'application/pdf' })], rejected: [] });
      await settle(fixture);
      expect(host.model()['fileId']).toBe(51);
      const chip = await inField(loader, 'fileId', TmChipHarness);
      expect(await chip.getLabel()).toBe('invoice.pdf');
      expect(await chip.getHref()).toContain('/blobs/email-attachment/51');

      expect(host.uploads.staged('fileId')?.id).toBe(51);

      // A removal clears the value and the blob staged for it.
      await chip.remove();
      expect(host.model()['fileId']).toBeNull();
      expect(host.uploads.staged('fileId')).toBeUndefined();
    });

    it('refuses a file too large for its kind, naming the limit, and uploads nothing', async () => {
      const { fixture, rig, loader } = await start((entities) => [
        { name: 'fileId', property: entities.get('core.OutgoingEmailAttachment').property('FileId')!, value: null },
      ]);
      const picker = fixture.debugElement.query(By.directive(TmFilePicker)).injector.get(TmFilePicker);
      const file = new File(['x'], 'huge.pdf', { type: 'application/pdf' });
      picker.filesSelected.emit({ accepted: [], rejected: [{ file, reason: 'size' }] });
      await settle(fixture);
      const field = await formField(loader, 'fileId');
      expect(await field.getErrorTexts()).toEqual(['This file is too large. The limit is 100 MB.']);
      expect(rig.api.calls().some((call) => call.kind === 'upload')).toBe(false);
    });

    it('offers a retry after an upload fails on the server', async () => {
      const { host, fixture, rig, loader } = await start((entities) => [
        { name: 'fileId', property: entities.get('core.OutgoingEmailAttachment').property('FileId')!, value: null },
      ]);
      rig.api.respond('blobs/email-attachment', () => Promise.reject(new TmApiError({ kind: 'server', status: 500 })));
      const picker = fixture.debugElement.query(By.directive(TmFilePicker)).injector.get(TmFilePicker);
      picker.filesSelected.emit({ accepted: [new File(['%PDF'], 'invoice.pdf', { type: 'application/pdf' })], rejected: [] });
      await settle(fixture);
      const field = await formField(loader, 'fileId');
      expect(await field.getErrorTexts()).toEqual(['Upload failed']);
      expect(host.uploads.running().size).toBe(0);

      rig.api.respond('blobs/email-attachment', { ...BLOB, id: 52, kind: 'email-attachment', fileName: 'invoice.pdf' });
      const retry = [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('[data-testid="fileId"] button')].find(
        (button) => button.textContent?.trim() === 'Try again',
      )!;
      retry.click();
      await settle(fixture);
      expect(host.model()['fileId']).toBe(52);
      expect(await field.getErrorTexts()).toEqual([]);
    });
  });
});

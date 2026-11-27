// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import type { TmColumnPlan, TmJson, TmPathResolution, TmPlanRow, TmValueDescriptor } from '@tellma/core';
import { TmAvatar } from '@tellma/core-ui/avatar';
import { TmImage } from '@tellma/core-ui/image';

import { tmEntityRig, type TmEntityRig } from '../../src/entity/tm-entity.testing.util';
import { tmSettleUntil } from '../../src/routes/tm-app.testing.util';
import { tmSelectPlan } from './tm-select-plan';
import { TmValue } from './tm-value';
import type { TmValueStyle } from './tm-formats';

// The view-mode value over the entity registry's test app: formatted text,
// the empty value per surface, enum badges, related rows as links with
// their avatars, thumbnails asked for only when listed, and file links.

/** One value under test. */
@Component({
  imports: [TmValue],
  template: `<tm-value [property]="property()" [value]="value()" [surface]="surface()" />`,
})
class TmValueHost {
  /** What the value is. */
  readonly property = signal<TmValueDescriptor | TmPathResolution | TmColumnPlan>({ type: 'string', nullable: true });
  /** The value, or a plan's row. */
  readonly value = signal<unknown>(null);
  /** The surface. */
  readonly surface = signal<TmValueStyle>('details');
}

/** A started host. */
interface TmValueTest {
  /** The rig. */
  readonly rig: TmEntityRig;
  /** The fixture. */
  readonly fixture: ComponentFixture<TmValueHost>;
  /**
   * Shows a value and returns the host element.
   *
   * @param property - What the value is.
   * @param value - The value.
   * @param surface - The surface.
   * @returns The `tm-value` element.
   */
  show(property: TmValueDescriptor | TmPathResolution | TmColumnPlan, value: unknown, surface?: TmValueStyle): Promise<HTMLElement>;
}

/** Starts the host in a warm tenant with every grant. */
async function start(): Promise<TmValueTest> {
  const rig = await tmEntityRig({ access: { isSystem: true }, languages: 2 });
  const fixture = TestBed.createComponent(TmValueHost);
  await fixture.whenStable();
  return {
    rig,
    fixture,
    show: async (property, value, surface = 'details') => {
      fixture.componentInstance.property.set(property);
      fixture.componentInstance.value.set(value);
      fixture.componentInstance.surface.set(surface);
      await fixture.whenStable();
      return fixture.nativeElement.querySelector('tm-value') as HTMLElement;
    },
  };
}

/**
 * Plan rows over cells.
 *
 * @param plan - The plan's terms.
 * @param cells - The row's cells.
 * @returns The row.
 */
function rowOf(plan: ReturnType<typeof tmSelectPlan>, cells: readonly TmJson[]): TmPlanRow {
  return plan.rows({
    columns: plan.select.split(',').map((term) => ({ name: term, type: 'String', nullable: true, path: term.split('.'), groupingKey: false })),
    rows: [cells],
  })[0]!;
}

describe('tm-value', () => {
  it('shows a value’s text, a dash for none in a details view and nothing in a grid', async () => {
    const { show } = await start();
    expect((await show({ type: 'decimal', nullable: true, scale: 2 }, '1234567890123456789.5')).textContent?.trim()).toBe(
      '1,234,567,890,123,456,789.50',
    );
    expect((await show({ type: 'decimal', nullable: true, scale: 2 }, null)).textContent?.trim()).toBe('—');
    expect((await show({ type: 'decimal', nullable: true, scale: 2 }, null, 'grid')).textContent?.trim()).toBe('');
  });

  it('shows a flag in a grid as a check for yes, named for assistive technology, and as words in a details view', async () => {
    const { show } = await start();
    const flag = { type: 'bool', nullable: false } as const;
    const yes = await show(flag, true, 'grid');
    expect(yes.querySelector('tm-icon')?.getAttribute('aria-label')).toBe('Yes');
    expect(yes.textContent?.trim()).toBe('');
    const no = await show(flag, false, 'grid');
    expect(no.querySelector('tm-icon')).toBeNull();
    expect(no.querySelector('.tm-value__hidden')?.textContent).toBe('No');
    expect((await show(flag, true)).textContent?.trim()).toBe('Yes');
  });

  it('shows a guid mono and a slot in its content direction', async () => {
    const { show, rig } = await start();
    const guid = await show({ type: 'guid', nullable: false }, '0b4f2a');
    expect(guid.querySelector('.tm-value__mono')?.textContent).toBe('0b4f2a');
    const slot = await show(rig.entities.get('acme.Customer').property('Name2')!, 'أكمي');
    expect(slot.querySelector('span')?.getAttribute('dir')).toBe('rtl');
  });

  it('shows an enum value, or each of a list, as a badge', async () => {
    const { show } = await start();
    const single = await show({ type: 'enum', nullable: false, enum: 'acme.CustomerType' }, 'Retail');
    expect([...single.querySelectorAll('tm-badge')].map((badge) => badge.textContent?.trim())).toEqual(['Retail']);
    const list = await show({ type: 'enum', nullable: false, enum: 'acme.CustomerType', array: true }, ['Retail', 'Retail']);
    expect(list.querySelectorAll('tm-badge').length).toBe(2);
  });

  it('links a related row by its display name when the user may open it', async () => {
    const { show, rig } = await start();
    rig.names.remember('gl.Center', [{ id: 7, code: 'E-01' }]);
    const parent = await show(rig.entities.resolve('gl.Center', 'Parent')!, 7);
    const link = parent.querySelector('a')!;
    expect(link.textContent?.trim()).toBe('E-01');
    expect(link.getAttribute('href')).toBe('/17/centers/7');
    expect(parent.querySelector('bdi')).not.toBeNull();
  });

  it('reads an unknown related row’s name in the background', async () => {
    const { show, rig, fixture } = await start();
    // A query answers with a page of rows.
    rig.api.respond('centers/query', {
      rows: {
        columns: [
          { name: 'Id', type: 'Numeric', nullable: false, path: ['Id'], groupingKey: false },
          { name: 'Code', type: 'String', nullable: true, path: ['Code'], groupingKey: false },
        ],
        rows: [[9, 'W-09']],
      },
    });
    const center = await show({ type: 'integer', nullable: true, target: 'gl.Center' }, 9);
    // The read flushes on a frame and its code is in the entity model's chunk,
    // which each read loads first; the name then renders.
    await tmSettleUntil(() => rig.names.label('gl.Center', 9)() !== null, 'the unknown center is named');
    await fixture.whenStable();
    expect(center.querySelector('a')?.textContent?.trim()).toBe('W-09');
    expect(rig.api.calls().filter((call) => call.route === 'centers/query')).toHaveLength(1);
  });

  it('shows a related row’s avatar keyed by its record, with no link to a page it lacks', async () => {
    const { show, fixture, rig } = await start();
    const manager = await show(rig.entities.resolve('gl.Center', 'Manager')!, { id: 17, name: 'Farah', name2: null, imageId: 5 });
    expect(manager.querySelector('a')).toBeNull();
    expect(manager.querySelector('tm-display-name')?.textContent?.trim()).toBe('Farah');
    const avatar = fixture.debugElement.query(By.directive(TmAvatar)).componentInstance as TmAvatar;
    expect(avatar.colorKey()).toBe('core.User:17');
    expect(avatar.src()).toBe('/17/blobs/user-image/5');
  });

  it('asks for a thumbnail only when the blob lists one', async () => {
    const { show, fixture, rig } = await start();
    const plan = TestBed.runInInjectionContext(() => tmSelectPlan(rig.entities.get('acme.Customer'), ['Image']));
    await show(plan.columns[0]!, rowOf(plan, [1, 88, 'thumb']), 'grid');
    const image = (): TmImage => fixture.debugElement.query(By.directive(TmImage)).componentInstance as TmImage;
    expect(image().src()).toBe('/17/blobs/customer-image/88?variant=thumb');
    expect(image().width()).toBe(32);
    await show(plan.columns[0]!, rowOf(plan, [1, 88, '']), 'grid');
    expect(image().src()).toBe('/17/blobs/customer-image/88');
    expect((await show(plan.columns[0]!, rowOf(plan, [1, null, null]), 'grid')).querySelector('tm-image')).toBeNull();
  });

  it('links a file by its name, downloading it', async () => {
    const { show, rig } = await start();
    const plan = TestBed.runInInjectionContext(() => tmSelectPlan(rig.entities.get('core.OutgoingEmailAttachment'), ['File']));
    const file = await show(plan.columns[0]!, rowOf(plan, [4, 51, 'invoice.pdf']));
    const link = file.querySelector('a')!;
    expect(link.textContent?.trim()).toBe('invoice.pdf');
    expect(link.getAttribute('href')).toBe('/17/blobs/email-attachment/51?download=true');
    expect(link.hasAttribute('download')).toBe(true);
  });

  it("shows a file's size beside its name, from the blob's row", async () => {
    const { show, rig } = await start();
    const file = await show(rig.entities.resolve('core.OutgoingEmailAttachment', 'File')!, { id: 51, fileName: 'invoice.pdf', size: '1536' });
    expect(file.querySelector('a')?.textContent?.trim()).toBe('invoice.pdf');
    expect(file.querySelector('.tm-value__size')?.textContent?.trim()).toBe('1.5 kB');
    const unsized = await show(rig.entities.resolve('core.OutgoingEmailAttachment', 'File')!, { id: 51, fileName: 'invoice.pdf' });
    expect(unsized.querySelector('.tm-value__size')).toBeNull();
  });

  it('shows a plan’s group and navigation cells', async () => {
    const { show, rig } = await start();
    const plan = TestBed.runInInjectionContext(() => tmSelectPlan(rig.entities.get('acme.Customer'), ['Name', 'Currency']));
    const row = rowOf(plan, [1, 'Acme', 'أكمي', 3, 'SAR']);
    expect((await show(plan.columns[0]!, row)).textContent?.trim()).toBe('Acme');
    expect((await show(plan.columns[1]!, row)).textContent?.trim()).toBe('SAR');
  });
});

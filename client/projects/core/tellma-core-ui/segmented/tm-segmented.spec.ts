// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { TestKey } from '@angular/cdk/testing';
import { form, FormField, readonly } from '@angular/forms/signals';
import { LayoutGrid, Table2 } from 'lucide';

import { provideTellmaUi } from '@tellma/core-ui';
import { provideTellmaIcons } from '@tellma/core-ui/icon';
import { TmSegmentedHarness } from '@tellma/core-ui-testing';

import { TmSegment } from './tm-segment';
import { TmSegmented } from './tm-segmented';

/** A view toggle bound to a Signal Forms field, inside a sized query container. */
@Component({
  imports: [FormField, TmSegment, TmSegmented],
  template: `
    <div
      [dir]="dir()"
      [style.inline-size.px]="width()"
      style="container-type: inline-size; --tooltip-delay: 0ms"
    >
      <tm-segmented ariaLabel="View" [labels]="labels()" [formField]="prefs.view">
        <tm-segment value="grid" icon="table-2" label="Grid" />
        <tm-segment value="tiles" icon="layout-grid" label="Tiles" />
        <tm-segment value="list" label="List" disabled />
      </tm-segmented>
    </div>
  `,
})
class Host {
  readonly dir = signal<'ltr' | 'rtl'>('ltr');
  readonly width = signal(1300);
  readonly labels = signal<'always' | 'never' | 'xl'>('xl');
  readonly locked = signal(false);
  readonly model = signal({ view: 'grid' });
  readonly prefs = form(this.model, (path) => readonly(path.view, () => this.locked()));
}

async function setup() {
  TestBed.configureTestingModule({
    providers: [
      provideTellmaUi(),
      provideTellmaIcons({ 'table-2': Table2, 'layout-grid': LayoutGrid }),
    ],
  });
  const fixture = TestBed.createComponent(Host);
  await fixture.whenStable();
  const loader = TestbedHarnessEnvironment.loader(fixture);
  return { fixture, loader, segmented: await loader.getHarness(TmSegmentedHarness) };
}

/** Waits for a resize observation and the change detection it schedules. */
async function settle(fixture: { whenStable(): Promise<unknown> }): Promise<void> {
  await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
  await fixture.whenStable();
}

describe('tm-segmented', () => {
  it('binds to a Signal Forms field both ways', async () => {
    const { fixture, segmented } = await setup();
    expect(await segmented.getCheckedLabel()).toBe('Grid');
    await segmented.check('Tiles');
    expect(fixture.componentInstance.model().view).toBe('tiles');
    fixture.componentInstance.model.set({ view: 'grid' });
    await fixture.whenStable();
    expect(await segmented.getCheckedLabel()).toBe('Grid');
  });

  it('is one tab stop on the checked segment, and the arrows check and move, skipping disabled ones', async () => {
    const { fixture, segmented } = await setup();
    expect(await segmented.isTabStop('Grid')).toBe(true);
    expect(await segmented.isTabStop('Tiles')).toBe(false);
    await segmented.focus('Grid');
    await segmented.press('Grid', TestKey.RIGHT_ARROW);
    expect(fixture.componentInstance.model().view).toBe('tiles');
    expect(await segmented.getFocusedLabel()).toBe('Tiles');
    // "List" is disabled: the next one round is "Grid".
    await segmented.press('Tiles', TestKey.RIGHT_ARROW);
    expect(fixture.componentInstance.model().view).toBe('grid');
    expect(await segmented.isDisabled('List')).toBe(true);
  });

  it('maps Left and Right to the live direction', async () => {
    const { fixture, segmented } = await setup();
    fixture.componentInstance.dir.set('rtl');
    await fixture.whenStable();
    await segmented.focus('Grid');
    await segmented.press('Grid', TestKey.LEFT_ARROW);
    expect(fixture.componentInstance.model().view).toBe('tiles');
  });

  it('keeps the value while readonly', async () => {
    const { fixture, segmented } = await setup();
    fixture.componentInstance.locked.set(true);
    await fixture.whenStable();
    await segmented.check('Tiles');
    expect(fixture.componentInstance.model().view).toBe('grid');
    expect(await segmented.getCheckedLabel()).toBe('Grid');
  });

  it('names the control and keeps an icon segment’s label as its name', async () => {
    const { segmented } = await setup();
    expect(await segmented.getAriaLabel()).toBe('View');
    expect(await segmented.getSegmentAriaLabel('Grid')).toBe('Grid');
    expect(await segmented.getSegmentAriaLabel('List')).toBeNull();
  });

  it('drops icon labels below the breakpoint, with the label as a tooltip', async () => {
    const { fixture } = await setup();
    const labelOf = (index: number) =>
      fixture.nativeElement.querySelectorAll('.tm-segmented__label')[index] as HTMLElement;
    const native = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    /** Hovers the first segment and reports the tooltip text that shows, if any. */
    const hoverTooltip = async (): Promise<string | null> => {
      native.dispatchEvent(new PointerEvent('pointerenter', { pointerType: 'mouse' }));
      await new Promise((resolve) => setTimeout(resolve, 20));
      await fixture.whenStable();
      const text = document.querySelector('.tm-tooltip__panel')?.textContent?.trim() ?? null;
      native.dispatchEvent(new PointerEvent('pointerleave', { pointerType: 'mouse' }));
      native.dispatchEvent(new FocusEvent('blur'));
      await fixture.whenStable();
      return text;
    };
    expect(getComputedStyle(labelOf(0)).display).not.toBe('none');
    expect(await hoverTooltip()).toBeNull();
    fixture.componentInstance.width.set(1000);
    await settle(fixture);
    expect(getComputedStyle(labelOf(0)).display).toBe('none');
    expect(getComputedStyle(labelOf(1)).display).toBe('none');
    // A segment without an icon always shows its label.
    expect(getComputedStyle(labelOf(2)).display).not.toBe('none');
    expect(await hoverTooltip()).toBe('Grid');
    fixture.componentInstance.width.set(1300);
    await settle(fixture);
    expect(getComputedStyle(labelOf(0)).display).not.toBe('none');
    expect(await hoverTooltip()).toBeNull();
  });

  it('honours always and never', async () => {
    const { fixture } = await setup();
    const label = () => fixture.nativeElement.querySelector('.tm-segmented__label') as HTMLElement;
    fixture.componentInstance.width.set(400);
    fixture.componentInstance.labels.set('always');
    await settle(fixture);
    expect(getComputedStyle(label()).display).not.toBe('none');
    fixture.componentInstance.width.set(1300);
    fixture.componentInstance.labels.set('never');
    await settle(fixture);
    expect(getComputedStyle(label()).display).toBe('none');
  });
});

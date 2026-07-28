// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal, type OnDestroy } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmPopoverHarness } from '@tellma/core-ui-testing';

import { TmPopover, TmPopoverContent } from './tm-popover';
import { TmPopoverTrigger } from './tm-popover-trigger';

/** A destruction probe: counts live instances of the lazy content. */
let liveProbes = 0;

@Component({
  selector: 'tm-test-probe',
  template: `probe`,
})
class Probe implements OnDestroy {
  constructor() {
    liveProbes += 1;
  }
  ngOnDestroy(): void {
    liveProbes -= 1;
  }
}

@Component({
  imports: [TmPopover, TmPopoverContent, TmPopoverTrigger, Probe],
  template: `
    <button class="trigger" [tmPopoverTriggerFor]="popover">Filters</button>
    <tm-popover
      #popover
      aria-label="Filters"
      (opened)="openedCount = openedCount + 1"
      (closed)="closedCount = closedCount + 1"
    >
      <ng-template tmPopoverContent>
        <tm-test-probe />
        @if (withField()) {
          <input class="inside-field" />
        }
        <button type="button" class="inside-button">Apply</button>
      </ng-template>
    </tm-popover>
    <input class="outside-field" />
  `,
})
class Host {
  readonly withField = signal(true);
  openedCount = 0;
  closedCount = 0;
}

@Component({
  imports: [TmPopover],
  template: `<tm-popover #bare aria-label="Empty" /><button class="anchor">At</button>`,
})
class NoContentHost {}

async function setup<T>(component: new () => T): Promise<{
  fixture: ComponentFixture<T>;
  host: T;
  root: HTMLElement;
}> {
  liveProbes = 0;
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(component);
  await fixture.whenStable();
  return { fixture, host: fixture.componentInstance, root: fixture.nativeElement as HTMLElement };
}

function panel(): HTMLElement | null {
  return document.querySelector('.tm-popover__panel');
}

async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 0)); // focus + macrotask remeasure
  await fixture.whenStable();
}

describe('tm-popover + tmPopoverTriggerFor', () => {
  it('trigger semantics: aria-haspopup=dialog, live aria-expanded, click toggles', async () => {
    const { fixture, root } = await setup(Host);
    const trigger = root.querySelector('.trigger') as HTMLButtonElement;
    expect(trigger.getAttribute('aria-haspopup')).toBe('dialog');
    expect(trigger.getAttribute('aria-expanded')).toBe('false');
    expect(panel()).toBeNull();

    trigger.click();
    await settle(fixture);
    expect(panel()).not.toBeNull();
    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    expect(panel()?.getAttribute('role')).toBe('dialog');
    expect(panel()?.getAttribute('aria-label')).toBe('Filters');

    trigger.click();
    await settle(fixture);
    expect(panel()).toBeNull();
    expect(trigger.getAttribute('aria-expanded')).toBe('false');
  });

  it('content is lazy: instantiated on open, destroyed on close', async () => {
    const { fixture, host, root } = await setup(Host);
    expect(liveProbes).toBe(0); // nothing floats until needed

    (root.querySelector('.trigger') as HTMLButtonElement).click();
    await settle(fixture);
    expect(liveProbes).toBe(1);
    expect(host.openedCount).toBe(1);

    (root.querySelector('.trigger') as HTMLButtonElement).click();
    await settle(fixture);
    expect(liveProbes).toBe(0);
    expect(host.closedCount).toBe(1);
  });

  it('moves initial focus to the first tabbable, else the panel itself', async () => {
    const { fixture, host, root } = await setup(Host);
    (root.querySelector('.trigger') as HTMLButtonElement).click();
    await settle(fixture);
    expect(document.activeElement?.className).toBe('inside-field');

    (root.querySelector('.trigger') as HTMLButtonElement).click();
    await settle(fixture);
    host.withField.set(false);
    await fixture.whenStable();

    (root.querySelector('.trigger') as HTMLButtonElement).click();
    await settle(fixture);
    expect(document.activeElement?.className).toBe('inside-button');
  });

  it('Escape closes and returns focus to the trigger; consumed Escape does not', async () => {
    const { fixture, root } = await setup(Host);
    const trigger = root.querySelector('.trigger') as HTMLButtonElement;
    trigger.click();
    await settle(fixture);

    const consumed = new KeyboardEvent('keydown', {
      key: 'Escape',
      bubbles: true,
      cancelable: true,
    });
    consumed.preventDefault();
    panel()?.dispatchEvent(consumed);
    await settle(fixture);
    expect(panel()).not.toBeNull(); // an inner overlay had handled it

    panel()?.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
    );
    await settle(fixture);
    expect(panel()).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  it('tabbing out (focus leaving the panel) closes without yanking focus back', async () => {
    const { fixture, root } = await setup(Host);
    (root.querySelector('.trigger') as HTMLButtonElement).click();
    await settle(fixture);

    const outside = root.querySelector('.outside-field') as HTMLInputElement;
    outside.focus(); // fires focusout on the panel
    await settle(fixture);
    expect(panel()).toBeNull();
    expect(document.activeElement).toBe(outside);
  });

  it('programmatic open at a rectangle takes focus; close has no anchor to restore to', async () => {
    const { fixture, root } = await setup(Host);
    const popover = fixture.debugElement.query(By.directive(TmPopover))
      .componentInstance as TmPopover;

    (root.querySelector('.outside-field') as HTMLInputElement).focus();
    popover.open(new DOMRect(40, 40, 10, 10));
    await settle(fixture);
    expect(panel()).not.toBeNull();
    expect(document.activeElement?.className).toBe('inside-field'); // focus moved in

    popover.close();
    await settle(fixture);
    expect(panel()).toBeNull();
    // A rectangle anchor is not focusable — the consumer owns focus then.
    expect(document.activeElement).toBe(document.body);
  });

  it('open() without a content template warns and stays closed', async () => {
    const { fixture, root } = await setup(NoContentHost);
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      const popover = fixture.debugElement.query(By.directive(TmPopover))
        .componentInstance as TmPopover;
      popover.open(root.querySelector('.anchor') as HTMLElement);
      await settle(fixture);
      expect(panel()).toBeNull();
      expect(warn).toHaveBeenCalledWith(expect.stringContaining('tmPopoverContent'));
    } finally {
      warn.mockRestore();
    }
  });

  it('drives the open panel through TmPopoverHarness', async () => {
    const { fixture, root } = await setup(Host);
    (root.querySelector('.trigger') as HTMLButtonElement).click();
    await settle(fixture);
    const harness = await TestbedHarnessEnvironment.documentRootLoader(fixture).getHarness(
      TmPopoverHarness,
    );
    expect(await harness.getAriaLabel()).toBe('Filters');
    expect(await harness.getText()).toContain('probe');
    expect(await harness.isFocused()).toBe(true);
  });
});

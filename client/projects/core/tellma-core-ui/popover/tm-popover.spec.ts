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

/** Two buttons sharing one popover — both carry the same `aria-expanded`. */
@Component({
  imports: [TmPopover, TmPopoverContent, TmPopoverTrigger],
  template: `
    <button class="trigger-a" [tmPopoverTriggerFor]="popover">A</button>
    <button class="trigger-b" [tmPopoverTriggerFor]="popover">B</button>
    <tm-popover #popover aria-label="Shared">
      <ng-template tmPopoverContent>
        <button type="button" class="inside-button">Apply</button>
      </ng-template>
    </tm-popover>
  `,
})
class SharedTriggerHost {}

/** Two independent popovers — the second attaches on top of the first. */
@Component({
  imports: [TmPopover, TmPopoverContent],
  template: `
    <input class="anchor" />
    <button class="other-anchor" type="button">Other</button>
    <tm-popover #first aria-label="First">
      <ng-template tmPopoverContent><button type="button">In first</button></ng-template>
    </tm-popover>
    <tm-popover #second aria-label="Second">
      <ng-template tmPopoverContent><button type="button">In second</button></ng-template>
    </tm-popover>
  `,
})
class TwoPopoverHost {}

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

  it('Escape closes even when focus rests on a programmatic anchor (overlay fallback)', async () => {
    const { fixture, root } = await setup(Host);
    const popover = fixture.debugElement.query(By.directive(TmPopover))
      .componentInstance as TmPopover;
    const anchor = root.querySelector('.outside-field') as HTMLInputElement;
    popover.open(anchor);
    await settle(fixture);
    expect(panel()).not.toBeNull();

    // Focus moves back onto the anchor: the popover stays open by design
    // (the anchor is exempt from focus-out and outside-click closes).
    anchor.focus();
    await settle(fixture);
    expect(panel()).not.toBeNull();

    // Escape pressed THERE must still close: it reaches the popover via
    // the CDK dispatcher (topmost overlay), not the panel's own keydown.
    anchor.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
    );
    await settle(fixture);
    expect(panel()).toBeNull();
  });

  it('a second trigger sharing the popover collapses it instead of re-opening', async () => {
    const { fixture, root } = await setup(SharedTriggerHost);
    const first = root.querySelector('.trigger-a') as HTMLButtonElement;
    const second = root.querySelector('.trigger-b') as HTMLButtonElement;

    first.click();
    await settle(fixture);
    expect(panel()).not.toBeNull();
    expect(second.getAttribute('aria-expanded')).toBe('true');

    // The CDK reports this as an outside click (the panel sits at the
    // OTHER trigger, and it detects clicks at document capture) — it must
    // not close behind the toggle's back, or the toggle reads a false
    // isOpen() and re-anchors here instead of collapsing.
    second.click();
    await settle(fixture);
    expect(panel()).toBeNull();
    expect(second.getAttribute('aria-expanded')).toBe('false');
  });

  it('the anchor Escape fallback survives a later overlay that never handles keys', async () => {
    const { fixture, root } = await setup(TwoPopoverHost);
    const [first, second] = fixture.debugElement
      .queryAll(By.directive(TmPopover))
      .map((debug) => debug.componentInstance as TmPopover);
    const anchor = root.querySelector('.anchor') as HTMLInputElement;

    first.open(anchor);
    await settle(fixture);
    anchor.focus(); // stays open by design: the anchor is exempt
    await settle(fixture);

    // A second overlay attaches ON TOP. Every connected overlay subscribes
    // to the CDK keyboard dispatcher, which delivers only to the topmost
    // subscriber — a dispatcher-routed fallback would starve here.
    second.open(root.querySelector('.other-anchor') as HTMLElement);
    await settle(fixture);
    expect(document.querySelectorAll('.tm-popover__panel')).toHaveLength(2);

    anchor.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
    );
    await settle(fixture);
    expect(document.querySelector('.tm-popover__panel[aria-label="First"]')).toBeNull();
    expect(document.querySelector('.tm-popover__panel[aria-label="Second"]')).not.toBeNull();
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

/**
 * Popovers of each size, anchored to triggers with controllable placement.
 * The unit page loads no token stylesheet, so the wrapper supplies the
 * popover's size tokens at the preset's values.
 */
@Component({
  imports: [TmPopover, TmPopoverContent, TmPopoverTrigger],
  template: `
    <div
      class="tokens"
      style="--popover-width-sm: 320px; --popover-width-md: 440px; --popover-width-lg: 640px; --popover-viewport-margin: 8px; --popover-max-inline-size: 346px"
    >
    <div class="bar" [style.padding-inline-start.px]="inset()">
      <button class="sized-trigger" [tmPopoverTriggerFor]="sized">Open</button>
    </div>
    <tm-popover #sized aria-label="Sized" [size]="size()" [panelClass]="extraClass()">
      <ng-template tmPopoverContent>
        <p class="sized-content">Short</p>
        @for (line of lines(); track $index) {
          <p class="sized-line">A longer line of content that loads later {{ line }}</p>
        }
      </ng-template>
    </tm-popover>
    <div [style.block-size.px]="spacer()"></div>
    </div>
  `,
})
class SizedHost {
  readonly size = signal<'auto' | 'sm' | 'md' | 'lg'>('md');
  readonly extraClass = signal<string | undefined>(undefined);
  readonly inset = signal(16);
  readonly lines = signal<readonly number[]>([]);
  readonly spacer = signal(0);
}

describe('tm-popover sizes', () => {
  /** Opens the sized popover and returns its panel. */
  async function openSized(fixture: ComponentFixture<SizedHost>): Promise<HTMLElement> {
    (fixture.nativeElement.querySelector('.sized-trigger') as HTMLButtonElement).click();
    await settle(fixture);
    return panel() as HTMLElement;
  }

  /** A size token as the test host supplies it, in px. */
  function token(name: string): number {
    const tokens = document.querySelector('.tokens') as HTMLElement;
    return Number.parseFloat(getComputedStyle(tokens).getPropertyValue(name));
  }

  /** The viewport margin token, in px. */
  function margin(): number {
    return token('--popover-viewport-margin');
  }

  it('a fixed size carries its class and the token width, capped by the viewport', async () => {
    const { fixture, host } = await setup(SizedHost);
    for (const size of ['sm', 'md', 'lg'] as const) {
      host.size.set(size);
      await settle(fixture);
      const surface = await openSized(fixture);
      expect(surface.classList.contains(`tm-popover__panel--${size}`)).toBe(true);
      const expected = Math.min(token(`--popover-width-${size}`), window.innerWidth - 2 * margin());
      expect(Math.abs(surface.getBoundingClientRect().width - expected)).toBeLessThanOrEqual(1);
      (fixture.nativeElement.querySelector('.sized-trigger') as HTMLButtonElement).click();
      await settle(fixture);
    }
  });

  it("'auto' keeps the content width up to the cap, with no fixed-size class", async () => {
    const { fixture, host } = await setup(SizedHost);
    host.size.set('auto');
    await settle(fixture);
    const surface = await openSized(fixture);
    for (const size of ['sm', 'md', 'lg']) {
      expect(surface.classList.contains(`tm-popover__panel--${size}`)).toBe(false);
    }
    // "Short" is far narrower than any fixed size.
    expect(surface.getBoundingClientRect().width).toBeLessThan(200);
  });

  it('a fixed size never changes width while its content grows', async () => {
    const { fixture, host } = await setup(SizedHost);
    host.size.set('sm');
    const surface = await openSized(fixture);
    const before = surface.getBoundingClientRect().width;
    host.lines.set([1, 2, 3]);
    await settle(fixture);
    expect(surface.querySelectorAll('.sized-line')).toHaveLength(3);
    expect(surface.getBoundingClientRect().width).toBe(before);
  });

  it('caps the height at the room below the anchor; the content scrolls inside', async () => {
    const { fixture, host } = await setup(SizedHost);
    host.lines.set(Array.from({ length: 80 }, (_, index) => index));
    const surface = await openSized(fixture);
    const anchor = (fixture.nativeElement.querySelector('.sized-trigger') as HTMLElement)
      .getBoundingClientRect();
    const box = surface.getBoundingClientRect();
    // Placed below the trigger (more room there), and inside the viewport margin.
    expect(box.top).toBeGreaterThanOrEqual(anchor.bottom - 1);
    expect(box.bottom).toBeLessThanOrEqual(document.documentElement.clientHeight - margin() + 1);
    expect(surface.scrollHeight).toBeGreaterThan(surface.clientHeight);
  });

  it('keeps a fixed-size panel inside the viewport margins near the inline-end edge', async () => {
    const { fixture, host } = await setup(SizedHost);
    host.size.set('lg');
    // Push the trigger to the far end: a start-aligned panel would hang off.
    host.inset.set(document.documentElement.clientWidth - 80);
    await settle(fixture);
    const box = (await openSized(fixture)).getBoundingClientRect();
    expect(box.left).toBeGreaterThanOrEqual(margin() - 1);
    expect(box.right).toBeLessThanOrEqual(document.documentElement.clientWidth - margin() + 1);
  });

  it('caps the width at the visible viewport, never counting a scrollbar', async () => {
    const { fixture, host } = await setup(SizedHost);
    host.size.set('lg');
    await settle(fixture);
    const surface = await openSized(fixture);
    const cap = document.documentElement.clientWidth - 2 * margin();
    expect(surface.style.getPropertyValue('--tm-popover-inline-cap')).toBe(`${cap}px`);
    expect(surface.getBoundingClientRect().width).toBeLessThanOrEqual(cap + 1);
  });

  it('re-fits the height cap as the page scrolls while the panel is open', async () => {
    const { fixture, host } = await setup(SizedHost);
    host.lines.set(Array.from({ length: 80 }, (_, index) => index));
    host.spacer.set(3000);
    await settle(fixture);
    const surface = await openSized(fixture);
    const trigger = fixture.nativeElement.querySelector('.sized-trigger') as HTMLElement;
    const capFor = (): string =>
      `${Math.floor(
        document.documentElement.clientHeight - trigger.getBoundingClientRect().bottom - margin(),
      )}px`;
    expect(surface.style.getPropertyValue('--tm-popover-block-cap')).toBe(capFor());
    try {
      // The anchor moves up the page, so the room below it grows.
      const capBefore = surface.style.getPropertyValue('--tm-popover-block-cap');
      window.scrollBy(0, 40);
      await new Promise((resolve) => setTimeout(resolve, 50));
      await fixture.whenStable();
      expect(window.scrollY).toBeGreaterThan(0);
      expect(surface.style.getPropertyValue('--tm-popover-block-cap')).not.toBe(capBefore);
      expect(surface.style.getPropertyValue('--tm-popover-block-cap')).toBe(capFor());
    } finally {
      window.scrollTo(0, 0);
    }
  });

  it('applies panelClass to the panel', async () => {
    const { fixture, host } = await setup(SizedHost);
    host.extraClass.set('custom-panel');
    await settle(fixture);
    const surface = await openSized(fixture);
    expect(surface.classList.contains('custom-panel')).toBe(true);
    expect(surface.classList.contains('tm-popover__panel')).toBe(true);
  });

  it('reports the size through TmPopoverHarness', async () => {
    const { fixture, host } = await setup(SizedHost);
    host.size.set('lg');
    await settle(fixture);
    await openSized(fixture);
    const harness = await TestbedHarnessEnvironment.documentRootLoader(fixture).getHarness(
      TmPopoverHarness,
    );
    expect(await harness.getSize()).toBe('lg');
  });
});

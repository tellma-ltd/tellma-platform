// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmTooltipHarness } from '@tellma/core-ui-testing';
import { TmMenu } from '@tellma/core-ui/menu';

import { TmTooltip } from './tm-tooltip';

@Component({
  imports: [TmTooltip],
  template: `
    <button class="first" [tmTooltip]="firstText()" style="--tooltip-delay: 0ms">Refresh</button>
    <button class="second" tmTooltip="Second tooltip" style="--tooltip-delay: 0ms">Post</button>
  `,
})
class Host {
  readonly firstText = signal('Refresh the list');
}

async function setup(): Promise<{ fixture: ComponentFixture<Host>; host: Host; root: HTMLElement }> {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  await fixture.whenStable();
  return { fixture, host: fixture.componentInstance, root: fixture.nativeElement as HTMLElement };
}

function panel(): HTMLElement | null {
  return document.querySelector('.tm-tooltip__panel');
}

function pointerEnter(element: Element, pointerType = 'mouse'): void {
  element.dispatchEvent(new PointerEvent('pointerenter', { pointerType, bubbles: false }));
}

function pointerLeave(element: Element): void {
  element.dispatchEvent(new PointerEvent('pointerleave', { pointerType: 'mouse', bubbles: false }));
}

/** Waits past the (zeroed) show delay or the hide grace period. */
async function tick(fixture: ComponentFixture<unknown>, ms = 0): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, ms));
  await fixture.whenStable();
}

describe('tmTooltip', () => {
  it('registers the text as the accessible description without ever opening', async () => {
    const { root } = await setup();
    const button = root.querySelector('.first') as HTMLElement;
    const describedBy = button.getAttribute('aria-describedby');
    expect(describedBy).not.toBeNull();
    const message = document.getElementById((describedBy as string).split(/\s+/)[0]);
    expect(message?.textContent?.trim()).toBe('Refresh the list');
    expect(panel()).toBeNull(); // never opened
  });

  it('hover shows after the token delay; leave hides after the grace period', async () => {
    const { fixture, root } = await setup();
    const button = root.querySelector('.first') as HTMLElement;
    pointerEnter(button);
    await tick(fixture);
    const surface = panel();
    expect(surface).not.toBeNull();
    expect(surface?.textContent?.trim()).toBe('Refresh the list');
    expect(surface?.getAttribute('role')).toBe('tooltip');
    expect(surface?.getAttribute('aria-hidden')).toBe('true');

    pointerLeave(button);
    await tick(fixture, 150);
    expect(panel()).toBeNull();
  });

  it('a touch pointerenter never shows (long-press owns touch)', async () => {
    const { fixture, root } = await setup();
    pointerEnter(root.querySelector('.first') as HTMLElement, 'touch');
    await tick(fixture, 30);
    expect(panel()).toBeNull();
  });

  it('the surface is hoverable: moving onto it within the grace keeps it open', async () => {
    const { fixture, root } = await setup();
    const button = root.querySelector('.first') as HTMLElement;
    pointerEnter(button);
    await tick(fixture);
    const surface = panel() as HTMLElement;

    pointerLeave(button);
    surface.dispatchEvent(new PointerEvent('pointerenter', { pointerType: 'mouse' }));
    await tick(fixture, 150);
    expect(panel()).not.toBeNull(); // the pointer reached the surface

    surface.dispatchEvent(new PointerEvent('pointerleave', { pointerType: 'mouse' }));
    await tick(fixture, 150);
    expect(panel()).toBeNull();
  });

  it('Escape dismisses and is consumed', async () => {
    const { fixture, root } = await setup();
    pointerEnter(root.querySelector('.first') as HTMLElement);
    await tick(fixture);
    expect(panel()).not.toBeNull();

    const escape = new KeyboardEvent('keydown', {
      key: 'Escape',
      bubbles: true,
      cancelable: true,
    });
    document.body.dispatchEvent(escape);
    await tick(fixture);
    expect(panel()).toBeNull();
    expect(escape.defaultPrevented).toBe(true); // consumed (WCAG 1.4.13)
  });

  it('at most one tooltip is visible: showing the second hides the first', async () => {
    const { fixture, root } = await setup();
    pointerEnter(root.querySelector('.first') as HTMLElement);
    await tick(fixture);
    expect(panel()?.textContent?.trim()).toBe('Refresh the list');

    pointerEnter(root.querySelector('.second') as HTMLElement);
    await tick(fixture);
    const panels = document.querySelectorAll('.tm-tooltip__panel');
    expect(panels).toHaveLength(1);
    expect(panels[0].textContent?.trim()).toBe('Second tooltip');
  });

  it('a text change re-describes the host and updates the visible surface', async () => {
    const { fixture, host, root } = await setup();
    const button = root.querySelector('.first') as HTMLElement;
    pointerEnter(button);
    await tick(fixture);

    host.firstText.set('Reload everything');
    await fixture.whenStable();
    expect(panel()?.textContent?.trim()).toBe('Reload everything');
    const describedBy = button.getAttribute('aria-describedby');
    const message = document.getElementById((describedBy as string).split(/\s+/)[0]);
    expect(message?.textContent?.trim()).toBe('Reload everything');
  });

  it('a text change while shown re-measures: the surface stays centered on the host', async () => {
    const { fixture, host, root } = await setup();
    const button = root.querySelector('.first') as HTMLElement;
    const hostBox = button.getBoundingClientRect();
    const hostCenter = hostBox.left + hostBox.width / 2;
    pointerEnter(button);
    await tick(fixture);
    const before = (panel() as HTMLElement).getBoundingClientRect();
    expect(Math.abs(before.left + before.width / 2 - hostCenter)).toBeLessThanOrEqual(2);

    // The centered placement writes an exact inline offset from the box it
    // measured, so a longer text grows to one side unless it re-measures.
    host.firstText.set('Reload every row that is currently loaded');
    await tick(fixture);
    const after = (panel() as HTMLElement).getBoundingClientRect();
    expect(after.width).toBeGreaterThan(before.width); // the box really grew
    expect(Math.abs(after.left + after.width / 2 - hostCenter)).toBeLessThanOrEqual(2);
  });

  it('a touch long-press shows; the next tap anywhere hides', async () => {
    const { fixture, root } = await setup();
    const button = root.querySelector('.first') as HTMLElement;
    button.dispatchEvent(
      new PointerEvent('pointerdown', {
        pointerType: 'touch',
        isPrimary: true,
        pointerId: 7,
        bubbles: true,
      }),
    );
    // The long-press utility's own 500ms threshold (not the show delay).
    await tick(fixture, 600);
    expect(panel()).not.toBeNull();
    expect(panel()?.textContent?.trim()).toBe('Refresh the list');

    button.dispatchEvent(
      new PointerEvent('pointerup', { pointerType: 'touch', pointerId: 7, bubbles: true }),
    );
    // Non-hover devices fire pointerleave immediately after the lift —
    // the tooltip must survive it (touch dismissal is the next tap).
    button.dispatchEvent(
      new PointerEvent('pointerleave', { pointerType: 'touch', pointerId: 7 }),
    );
    await tick(fixture, 200);
    expect(panel()).not.toBeNull(); // persists after the finger lifts

    document.body.dispatchEvent(
      new PointerEvent('pointerdown', { pointerType: 'touch', isPrimary: true, bubbles: true }),
    );
    await tick(fixture);
    expect(panel()).toBeNull(); // the next tap dismissed it
  });

  it('blur hides immediately', async () => {
    const { fixture, root } = await setup();
    const button = root.querySelector('.first') as HTMLElement;
    pointerEnter(button);
    await tick(fixture);
    expect(panel()).not.toBeNull();

    button.dispatchEvent(new FocusEvent('blur'));
    await tick(fixture);
    expect(panel()).toBeNull();
  });

  it('one Escape dismisses ONE layer: the tooltip first, an open menu second', async () => {
    @Component({
      imports: [TmTooltip, TmMenu],
      template: `
        <button class="host" tmTooltip="Refresh the list" style="--tooltip-delay: 0ms">Go</button>
        <tm-menu #menu [items]="items" aria-label="Actions" />
        <button class="opener" (click)="menu.open($any($event.target))">Menu</button>
      `,
    })
    class Layered {
      readonly items = [{ id: 'a', label: 'Alpha', action: (): void => undefined }];
    }
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(Layered);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;

    // Open the menu, THEN show a tooltip over it.
    (root.querySelector('.opener') as HTMLButtonElement).click();
    await tick(fixture, 20);
    expect(document.querySelector('.tm-menu__panel')).not.toBeNull();
    pointerEnter(root.querySelector('.host') as HTMLElement);
    await tick(fixture, 20);
    expect(panel()).not.toBeNull();

    // Escape #1: only the tooltip goes; the menu SURVIVES.
    document.body.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
    );
    await tick(fixture, 20);
    expect(panel()).toBeNull();
    expect(document.querySelector('.tm-menu__panel')).not.toBeNull();

    // Escape #2 closes the menu.
    document.body.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
    );
    await tick(fixture, 20);
    expect(document.querySelector('.tm-menu__panel')).toBeNull();
  });

  it('reads the description and drives hover through TmTooltipHarness', async () => {
    const { fixture } = await setup();
    const harness = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmTooltipHarness);
    expect(await harness.getDescription()).toBe('Refresh the list');
    expect(await harness.isTooltipVisible()).toBe(false);

    await harness.hover();
    await tick(fixture, 30);
    expect(await harness.isTooltipVisible()).toBe(true);
    expect(await harness.getTooltipText()).toBe('Refresh the list');
  });
});

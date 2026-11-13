// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { provideTellmaUi } from '@tellma/core-ui';
import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';
import { TmSplitterHarness } from '@tellma/core-ui-testing';

import { ɵtmSplitterBounds, ɵtmSplitterClamp } from './internal/tm-splitter-clamp';
import {
  TmSplitter,
  TmSplitterPane,
  type TmSplitterOrientation,
  type TmSplitterSide,
} from './tm-splitter';

describe('the splitter clamp', () => {
  const limits = { min: 0, otherMin: 0, maxFraction: 0.66, separator: 7 };

  it('caps a 1,200 px container at 66 %: a size of 800 renders 792', () => {
    expect(ɵtmSplitterClamp(800, 1200, limits)).toBe(792);
    expect(ɵtmSplitterBounds(1200, limits)).toEqual({ lower: 0, upper: 792 });
  });

  it('keeps the other pane its minimum and the separator its width', () => {
    const tight = { min: 100, otherMin: 340, maxFraction: 1, separator: 7 };
    expect(ɵtmSplitterClamp(900, 1000, tight)).toBe(653);
    expect(ɵtmSplitterClamp(50, 1000, tight)).toBe(100);
  });

  it('lets the primary minimum win when the container is too small for both', () => {
    const tight = { min: 420, otherMin: 340, maxFraction: 0.66, separator: 7 };
    expect(ɵtmSplitterBounds(600, tight)).toEqual({ lower: 420, upper: 420 });
    expect(ɵtmSplitterClamp(500, 600, tight)).toBe(420);
  });
});

@Component({
  imports: [TmSplitter, TmSplitterPane],
  template: `
    <div [attr.dir]="dir()" [style.width.px]="width()" [style.height.px]="height()">
      <tm-splitter
        style="width: 100%; height: 100%"
        [(size)]="size"
        [orientation]="orientation()"
        [primary]="primary()"
        [defaultSize]="800"
        [min]="min()"
        [maxFraction]="maxFraction()"
        [otherMin]="otherMin()"
      >
        <div tmSplitterPane="start">Start</div>
        <div tmSplitterPane="end" id="details">End</div>
      </tm-splitter>
    </div>
  `,
})
class Host {
  readonly dir = signal<'ltr' | 'rtl'>('ltr');
  readonly width = signal(1200);
  readonly height = signal(600);
  readonly size = signal<number | null>(null);
  readonly orientation = signal<TmSplitterOrientation>('horizontal');
  readonly primary = signal<TmSplitterSide>('start');
  readonly min = signal(0);
  readonly maxFraction = signal(0.66);
  readonly otherMin = signal(0);
}

/** The unit page loads no token stylesheet; the bar's width needs one. */
let tokenSheet: HTMLStyleElement;

beforeAll(() => {
  tokenSheet = document.createElement('style');
  tokenSheet.textContent = tmEmitCss(tmTokensDefault);
  document.head.appendChild(tokenSheet);
});

afterAll(() => tokenSheet.remove());

/** Waits for the resize observer's callback and the render after it. */
async function settle(fixture: { whenStable(): Promise<unknown> }): Promise<void> {
  await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
  await fixture.whenStable();
}

async function setup(configure: (host: Host) => void = () => undefined) {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  configure(fixture.componentInstance);
  fixture.autoDetectChanges();
  await settle(fixture);
  const splitter = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmSplitterHarness);
  return { fixture, host: fixture.componentInstance, splitter };
}

describe('tm-splitter', () => {
  it('renders the clamped size and keeps the size the user asked for', async () => {
    const { fixture, host, splitter } = await setup((h) => h.size.set(800));
    expect(await splitter.getPaneSize('start')).toBe(792);
    expect(await splitter.getPaneSize('end')).toBe(1200 - 792 - 7);
    expect(host.size()).toBe(800);

    // A wider container lets the pane grow back to the size asked for.
    host.width.set(1500);
    await settle(fixture);
    expect(await splitter.getPaneSize('start')).toBe(800);
    expect(host.size()).toBe(800);
  });

  it('renders defaultSize while size is null', async () => {
    const { host, splitter } = await setup((h) => {
      h.width.set(1500);
    });
    expect(host.size()).toBeNull();
    expect(await splitter.getPaneSize('start')).toBe(800);
  });

  it('keeps the other pane its minimum', async () => {
    const { splitter } = await setup((h) => {
      h.maxFraction.set(1);
      h.otherMin.set(500);
    });
    expect(await splitter.getPaneSize('start')).toBe(1200 - 500 - 7);
    expect(await splitter.getPaneSize('end')).toBe(500);
  });

  it('is a named, focusable separator reporting the primary share and its range', async () => {
    const { fixture, splitter } = await setup();
    const bar = (fixture.nativeElement as HTMLElement).querySelector('.tm-splitter__separator')!;
    expect(bar.getAttribute('role')).toBe('separator');
    expect(bar.getAttribute('tabindex')).toBe('0');
    expect(bar.getAttribute('aria-orientation')).toBe('vertical');
    expect(await splitter.getLabel()).toBe('Resize panels');
    expect(await splitter.getValue()).toBe(66);
    expect(await splitter.getMinValue()).toBe(0);
    expect(await splitter.getMaxValue()).toBe(66);
    const start = (fixture.nativeElement as HTMLElement).querySelector('[tmSplitterPane="start"]')!;
    expect(start.id).not.toBe('');
    expect(await splitter.getControlledPaneId()).toBe(start.id);
  });

  it('points at a primary end pane by its own id', async () => {
    const { fixture, splitter } = await setup((h) => h.primary.set('end'));
    expect(await splitter.getControlledPaneId()).toBe('details');
    fixture.componentInstance.width.set(1500);
    await settle(fixture);
    expect(await splitter.getPaneSize('end')).toBe(800);
  });

  it('moves the bar the way the arrow points, by step and by four steps with Shift', async () => {
    const { fixture, host, splitter } = await setup((h) => {
      h.width.set(1500);
      h.size.set(600);
    });
    await splitter.press('ArrowRight');
    expect(host.size()).toBe(616);
    await splitter.press('ArrowLeft', true);
    expect(host.size()).toBe(552);
    // Up and Down do nothing on a horizontal bar.
    await splitter.press('ArrowDown');
    expect(host.size()).toBe(552);
    await settle(fixture);
    expect(await splitter.getPaneSize('start')).toBe(552);
  });

  it('mirrors the arrows in a right-to-left page, so the bar still follows them', async () => {
    const { host, splitter } = await setup((h) => {
      h.dir.set('rtl');
      h.width.set(1500);
      h.size.set(600);
    });
    // The start pane is on the right: ArrowRight moves the bar right and shrinks it.
    await splitter.press('ArrowRight');
    expect(host.size()).toBe(584);
    await splitter.press('ArrowLeft');
    expect(host.size()).toBe(600);
  });

  it('grows a primary end pane as the bar moves toward the start', async () => {
    const { host, splitter } = await setup((h) => {
      h.primary.set('end');
      h.width.set(1500);
      h.size.set(600);
    });
    await splitter.press('ArrowLeft');
    expect(host.size()).toBe(616);
  });

  it('answers Up and Down on a vertical bar', async () => {
    const { fixture, host, splitter } = await setup((h) => {
      h.orientation.set('vertical');
      h.height.set(900);
      h.size.set(300);
    });
    const bar = (fixture.nativeElement as HTMLElement).querySelector('.tm-splitter__separator')!;
    expect(bar.getAttribute('aria-orientation')).toBe('horizontal');
    await splitter.press('ArrowDown');
    expect(host.size()).toBe(316);
    await splitter.press('ArrowRight');
    expect(host.size()).toBe(316);
    await settle(fixture);
    expect(await splitter.getPaneSize('start')).toBe(316);
  });

  it('jumps to the ends of the clamp with Home and End, and clamps every move', async () => {
    const { host, splitter } = await setup((h) => {
      h.min.set(200);
      h.size.set(600);
    });
    await splitter.press('End');
    expect(host.size()).toBe(792);
    await splitter.press('ArrowRight');
    expect(host.size()).toBe(792);
    await splitter.press('Home');
    expect(host.size()).toBe(200);
    await splitter.press('ArrowLeft', true);
    expect(host.size()).toBe(200);
  });

  it('moves from the rendered size, not an intent the container cannot hold', async () => {
    const { host, splitter } = await setup((h) => h.size.set(1000));
    await splitter.press('ArrowLeft');
    expect(host.size()).toBe(792 - 16);
  });
});

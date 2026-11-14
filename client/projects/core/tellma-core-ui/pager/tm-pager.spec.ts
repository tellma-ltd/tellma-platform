// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { provideTellmaUi } from '@tellma/core-ui';
import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';
import { TmPagerHarness } from '@tellma/core-ui-testing';

import {
  ɵtmPagerReservedCh,
  ɵtmPagerStatus,
  ɵtmPagerWidestStatus,
} from './internal/tm-pager-status';
import { TmPager } from './tm-pager';

// ---- The status and its width, as pure functions ----

describe('the pager status', () => {
  const base = { page: 1, pageSize: 20, index: 1, totalIsLowerBound: false } as const;

  it('reads items from and to, the last page stopping at the total', () => {
    expect(ɵtmPagerStatus({ ...base, mode: 'range', total: 55 })).toEqual({
      key: 'range',
      params: { from: 1, to: 20, total: 55 },
    });
    expect(ɵtmPagerStatus({ ...base, mode: 'range', page: 3, total: 55 })?.params).toEqual({
      from: 41,
      to: 55,
      total: 55,
    });
  });

  it('reads an empty list as 0 to 0', () => {
    expect(ɵtmPagerStatus({ ...base, mode: 'range', total: 0 })?.params).toEqual({
      from: 0,
      to: 0,
      total: 0,
    });
  });

  it('does not cap the page at a lower-bound total', () => {
    expect(
      ɵtmPagerStatus({ ...base, mode: 'range', page: 51, total: 1000, totalIsLowerBound: true }),
    ).toEqual({ key: 'rangeAtLeast', params: { from: 1001, to: 1020, total: 1000 } });
  });

  it('reads the item and the total in item mode', () => {
    expect(ɵtmPagerStatus({ ...base, mode: 'item', index: 3, total: 293 })).toEqual({
      key: 'item',
      params: { index: 3, total: 293 },
    });
  });

  it('hides while the total is unknown', () => {
    expect(ɵtmPagerStatus({ ...base, mode: 'range', total: null })).toBeNull();
  });
});

describe('the pager width reservation', () => {
  it('reserves "{total}–{total} / {total}" in range mode', () => {
    expect(ɵtmPagerWidestStatus('range', 120, false)).toEqual({
      key: 'range',
      params: { from: 120, to: 120, total: 120 },
    });
    expect(ɵtmPagerReservedCh('120–120 / 120')).toBe(13);
  });

  it('reserves "{total} / {total}" in item mode', () => {
    expect(ɵtmPagerWidestStatus('item', 293, false)).toEqual({
      key: 'item',
      params: { index: 293, total: 293 },
    });
    expect(ɵtmPagerReservedCh('293 / 293')).toBe(9);
  });

  it('reserves past a lower-bound total up to the last row of maxPage', () => {
    // A cap of 1,000+ with pages of 20 up to page 500 reaches row 10,000.
    expect(ɵtmPagerWidestStatus('range', 1000, true, 500 * 20)).toEqual({
      key: 'rangeAtLeast',
      params: { from: 10000, to: 10000, total: 1000 },
    });
    // A reach inside the total keeps the total.
    expect(ɵtmPagerWidestStatus('range', 1000, true, 50 * 20).params).toEqual({
      from: 1000,
      to: 1000,
      total: 1000,
    });
  });

  it('reserves one more digit past a lower-bound total that nothing caps', () => {
    expect(ɵtmPagerWidestStatus('range', 1000, true).params).toEqual({
      from: 10000,
      to: 10000,
      total: 1000,
    });
    expect(ɵtmPagerWidestStatus('item', 293, true)).toEqual({
      key: 'itemAtLeast',
      params: { index: 2930, total: 293 },
    });
  });

  it('counts characters, not UTF-16 units', () => {
    expect(ɵtmPagerReservedCh('𝟏𝟐 / 𝟏𝟐')).toBe(7);
  });
});

// ---- The component, through its harness ----

@Component({
  imports: [TmPager],
  template: `
    <div [attr.dir]="dir()">
      <tm-pager
        [mode]="mode()"
        [(page)]="page"
        [(pageSize)]="pageSize"
        [pageSizes]="pageSizes()"
        [index]="index()"
        [total]="total()"
        [totalIsLowerBound]="lowerBound()"
        [maxPage]="maxPage()"
        [sizeLabel]="sizeLabel()"
        [disabled]="disabled()"
        (previous)="previousCount = previousCount + 1"
        (next)="nextCount = nextCount + 1"
      />
    </div>
  `,
})
class Host {
  readonly dir = signal<'ltr' | 'rtl'>('ltr');
  readonly mode = signal<'range' | 'item'>('range');
  readonly page = signal(1);
  readonly pageSize = signal(20);
  readonly pageSizes = signal<readonly number[]>([]);
  readonly index = signal(1);
  readonly total = signal<number | null>(55);
  readonly lowerBound = signal(false);
  readonly maxPage = signal<number | null>(null);
  readonly sizeLabel = signal<'short' | 'long'>('long');
  readonly disabled = signal(false);
  previousCount = 0;
  nextCount = 0;
}

/** The unit page loads no token stylesheet; the reserved widths need one. */
let tokenSheet: HTMLStyleElement;

beforeAll(() => {
  tokenSheet = document.createElement('style');
  tokenSheet.textContent = tmEmitCss(tmTokensDefault);
  document.head.appendChild(tokenSheet);
});

afterAll(() => tokenSheet.remove());

async function setup(configure: (host: Host) => void = () => undefined) {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  configure(fixture.componentInstance);
  fixture.autoDetectChanges();
  await fixture.whenStable();
  const pager = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmPagerHarness);
  const element = (fixture.nativeElement as HTMLElement).querySelector('tm-pager')!;
  return { fixture, host: fixture.componentInstance, pager, element };
}

describe('tm-pager', () => {
  it('shows the range and its sentence, and names its buttons', async () => {
    const { pager } = await setup();
    expect(await pager.getMode()).toBe('range');
    expect(await pager.getStatusText()).toBe('1–20 / 55');
    expect(await pager.getStatusSentence()).toBe('Items 1 to 20 of 55');
    expect(await pager.getPreviousLabel()).toBe('Previous page');
    expect(await pager.getNextLabel()).toBe('Next page');
  });

  it('sets the fraction left to right and hides it from assistive technology', async () => {
    const { element } = await setup((h) => h.dir.set('rtl'));
    const fraction = element.querySelector('.tm-pager__fraction')!;
    expect(fraction.getAttribute('dir')).toBe('ltr');
    expect(fraction.getAttribute('aria-hidden')).toBe('true');
    expect(getComputedStyle(fraction).fontVariantNumeric).toBe('tabular-nums');
  });

  it('writes page on every click, and disables at the ends of an exact total', async () => {
    const { host, pager } = await setup();
    expect(await pager.isPreviousDisabled()).toBe(true);
    await pager.next();
    await pager.next();
    expect(host.page()).toBe(3);
    expect(await pager.getStatusText()).toBe('41–55 / 55');
    expect(await pager.isNextDisabled()).toBe(true);
    await pager.previous();
    expect(host.page()).toBe(2);
    expect(await pager.isPreviousDisabled()).toBe(false);
  });

  it('keeps Next enabled for a lower-bound total, until maxPage', async () => {
    const { host, pager } = await setup((h) => {
      h.total.set(1000);
      h.lowerBound.set(true);
      h.page.set(50);
    });
    expect(await pager.getStatusText()).toBe('981–1,000 / 1,000+');
    expect(await pager.getStatusSentence()).toBe('Items 981 to 1,000 of 1,000 or more');
    expect(await pager.isNextDisabled()).toBe(false);
    host.maxPage.set(50);
    expect(await pager.isNextDisabled()).toBe(true);
  });

  it('hides the status and keeps Next enabled while the total is unknown', async () => {
    const { pager } = await setup((h) => {
      h.total.set(null);
      h.page.set(9);
    });
    expect(await pager.getStatusText()).toBeNull();
    expect(await pager.isNextDisabled()).toBe(false);
  });

  it('disables every control but keeps its geometry', async () => {
    const { host, pager, element } = await setup((h) => {
      h.page.set(2);
      h.pageSizes.set([20, 50]);
    });
    const width = element.getBoundingClientRect().width;
    host.disabled.set(true);
    expect(await pager.isPreviousDisabled()).toBe(true);
    expect(await pager.isNextDisabled()).toBe(true);
    expect(await (await pager.getSizeSelect())!.isDisabled()).toBe(true);
    expect(element.getBoundingClientRect().width).toBe(width);
  });

  it('emits previous and next in item mode and leaves index to the host', async () => {
    const { host, pager } = await setup((h) => {
      h.mode.set('item');
      h.index.set(3);
      h.total.set(293);
    });
    expect(await pager.getStatusText()).toBe('3 / 293');
    expect(await pager.getStatusSentence()).toBe('Item 3 of 293');
    expect(await pager.getPreviousLabel()).toBe('Previous item');
    expect(await pager.getNextLabel()).toBe('Next item');
    await pager.next();
    await pager.previous();
    expect(host.nextCount).toBe(1);
    expect(host.previousCount).toBe(1);
    expect(host.page()).toBe(1);
    host.index.set(293);
    expect(await pager.isNextDisabled()).toBe(true);
    expect(await pager.getSizeSelect()).toBeNull();
  });

  it('reserves the widest status, so paging never moves the buttons', async () => {
    const { fixture, host, element } = await setup((h) => h.total.set(120));
    const status = element.querySelector<HTMLElement>('.tm-pager__status')!;
    const next = element.querySelector('.tm-pager__next')!;
    expect(status.style.minInlineSize).toBe('13ch');
    const before = next.getBoundingClientRect();
    host.page.set(6);
    await fixture.whenStable();
    expect(status.textContent).toContain('101–120 / 120');
    expect(next.getBoundingClientRect()).toEqual(before);
  });

  it('keeps the buttons still past a lower-bound total, up to maxPage', async () => {
    const { fixture, host, element } = await setup((h) => {
      h.total.set(1000);
      h.lowerBound.set(true);
      h.maxPage.set(500);
    });
    const next = element.querySelector('.tm-pager__next')!;
    const before = next.getBoundingClientRect();
    for (const page of [51, 500]) {
      host.page.set(page);
      await fixture.whenStable();
      expect(next.getBoundingClientRect()).toEqual(before);
    }
    expect(element.querySelector('.tm-pager__status')!.textContent).toContain(
      '9,981–10,000 / 1,000+',
    );
  });

  it('writes the size and the page that keeps the first row in view', async () => {
    const { host, pager } = await setup((h) => {
      h.total.set(400);
      h.pageSizes.set([20, 50, 100]);
      h.pageSize.set(50);
      h.page.set(3);
    });
    const select = (await pager.getSizeSelect())!;
    expect(await select.getTriggerText()).toBe('50 per page');
    await select.selectOption('20 per page');
    expect(host.pageSize()).toBe(20);
    expect(host.page()).toBe(6);
    expect(await pager.getStatusText()).toBe('101–120 / 400');
  });

  it('reads the short size labels as bare numbers', async () => {
    const { pager } = await setup((h) => {
      h.pageSizes.set([20, 1000]);
      h.sizeLabel.set('short');
    });
    const select = (await pager.getSizeSelect())!;
    expect(await select.getTriggerText()).toBe('20');
    await select.open();
    const options = await select.getOptions();
    expect(await Promise.all(options.map((option) => option.getText()))).toEqual(['20', '1,000']);
  });

  it('reserves the widest size option, so the select never resizes', async () => {
    const { host, pager, element } = await setup((h) => h.pageSizes.set([20, 1000]));
    const select = element.querySelector('tm-select')!;
    const width = select.getBoundingClientRect().width;
    await (await pager.getSizeSelect())!.selectOption('1,000 per page');
    expect(host.pageSize()).toBe(1000);
    expect(select.getBoundingClientRect().width).toBe(width);
  });
});

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ApplicationRef, Component, signal } from '@angular/core';
import { DeferBlockBehavior, DeferBlockState, TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { provideTellmaUi, type TmControlSize } from '@tellma/core-ui';
import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';
import { TmPagerHarness, TmSelectHarness } from '@tellma/core-ui-testing';

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
        [size]="size()"
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
  readonly size = signal<TmControlSize | undefined>(undefined);
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

async function setup(
  configure: (host: Host) => void = () => undefined,
  deferBlockBehavior = DeferBlockBehavior.Playthrough,
) {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()], deferBlockBehavior });
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
    const harness = (await pager.getSizeSelect())!;
    const select = element.querySelector('tm-select')!;
    const width = select.getBoundingClientRect().width;
    await harness.selectOption('1,000 per page');
    expect(host.pageSize()).toBe(1000);
    expect(select.getBoundingClientRect().width).toBe(width);
  });
});

// ---- The size select's stand-in, with the select's loading held ----

/** The box properties the stand-in copies from the select's trigger. */
const CHROME = [
  'block-size',
  'padding-inline-start',
  'padding-inline-end',
  'border-top-width',
  'border-top-color',
  'border-top-left-radius',
  'background-color',
  'color',
  'font-family',
  'font-size',
  'line-height',
  'column-gap',
] as const;

/** An element's box, as plain numbers. */
function box(element: Element): Record<'x' | 'y' | 'width' | 'height', number> {
  const { x, y, width, height } = element.getBoundingClientRect();
  return { x, y, width, height };
}

/** The given computed properties of an element. */
function chrome(element: Element): Record<string, string> {
  const style = getComputedStyle(element);
  return Object.fromEntries(CHROME.map((name) => [name, style.getPropertyValue(name)]));
}

/** A pager offering 20, 50 and 100 per page, whose select lands only when the test lets it. */
async function held(configure: (host: Host) => void = () => undefined) {
  const result = await setup((h) => {
    h.total.set(400);
    h.pageSizes.set([20, 50, 100]);
    configure(h);
  }, DeferBlockBehavior.Manual);
  const [block] = await result.fixture.getDeferBlocks();
  const proxy = () => result.element.querySelector<HTMLElement>('.tm-pager__proxy');
  const land = async () => {
    await block!.render(DeferBlockState.Complete);
    await result.fixture.whenStable();
  };
  const select = () => TestbedHarnessEnvironment.loader(result.fixture).getHarness(TmSelectHarness);
  const trigger = () => result.element.querySelector<HTMLElement>('.tm-select__trigger');
  return { ...result, proxy, land, select, trigger };
}

/**
 * Records each time focus leaves an element for no other: a stop on the
 * body, which a screen reader announces as the page.
 */
function bodyStops(): { readonly count: () => number } {
  let stops = 0;
  const listener = (event: FocusEvent): void => {
    if (event.relatedTarget === null) {
      stops += 1;
    }
  };
  document.addEventListener('focusout', listener, true);
  onTestFinished(() => document.removeEventListener('focusout', listener, true));
  return { count: () => stops };
}

/** A cancelable key press on an element. */
function keydown(element: HTMLElement, key: string): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
  element.dispatchEvent(event);
  return event;
}

describe('the size select before it loads', () => {
  for (const size of ['sm', 'md', 'lg'] as const) {
    it(`holds the select's box, chrome and current size, and hands over in place (${size})`, async () => {
      const { element, proxy, land, trigger } = await held((h) => {
        h.pageSize.set(50);
        h.size.set(size);
      });
      const standIn = proxy()!;
      expect(element.querySelector('tm-select')).toBeNull();
      expect(standIn.textContent!.trim()).toBe('50 per page');
      expect(standIn.getAttribute('role')).toBe('combobox');
      expect(standIn.getAttribute('aria-label')).toBe('Page size');
      expect(standIn.getAttribute('aria-expanded')).toBe('false');
      expect(standIn.tabIndex).toBe(0);
      const before = { standIn: box(standIn), pager: box(element), chrome: chrome(standIn) };

      await land();
      expect(proxy()).toBeNull();
      expect(box(element.querySelector('tm-select')!)).toEqual(before.standIn);
      expect(box(element)).toEqual(before.pager);
      expect(chrome(trigger()!)).toEqual(before.chrome);
      expect(trigger()!.textContent!.trim()).toBe('50 per page');
      // Landing on its own takes no focus.
      expect(document.activeElement).toBe(document.body);
    });
  }

  it('keeps the stand-in under a press, then opens the select on its click', async () => {
    const { host, proxy, land, select, trigger } = await held();
    const standIn = proxy()!;
    standIn.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, button: 0 }));
    standIn.focus();
    const stops = bodyStops();
    await land();
    // The press has not ended: the stand-in stays, over an inert select.
    expect(proxy()).toBe(standIn);
    expect(trigger()!.closest('tm-select')!.hasAttribute('inert')).toBe(true);

    standIn.click();
    await TestBed.inject(ApplicationRef).whenStable();
    expect(proxy()).toBeNull();
    const harness = await select();
    expect(await harness.isOpen()).toBe(true);
    expect(document.activeElement).toBe(trigger());
    expect(stops.count()).toBe(0);
    await harness.selectOption('100 per page');
    expect(host.pageSize()).toBe(100);
  });

  it('opens the select on an open key pressed before it lands', async () => {
    const { proxy, land, select, trigger } = await held();
    const standIn = proxy()!;
    standIn.focus();
    const stops = bodyStops();
    expect(keydown(standIn, 'ArrowDown').defaultPrevented).toBe(true);
    await land();
    expect(await (await select()).isOpen()).toBe(true);
    expect(document.activeElement).toBe(trigger());
    expect(stops.count()).toBe(0);
  });

  it('feeds the text typed before it lands to the typeahead', async () => {
    const { proxy, land, select } = await held();
    const standIn = proxy()!;
    standIn.focus();
    expect(keydown(standIn, '1').defaultPrevented).toBe(true);
    await land();
    const harness = await select();
    expect(await harness.isOpen()).toBe(true);
    const active: string[] = [];
    for (const option of await harness.getOptions()) {
      if (await option.isActive()) {
        active.push(await option.getText());
      }
    }
    expect(active).toEqual(['100 per page']);
  });

  it('keeps a focused stand-in that nothing was asked of until it loses focus', async () => {
    const { element, proxy, land, select } = await held();
    const standIn = proxy()!;
    standIn.focus();
    const stops = bodyStops();
    await land();
    // Focus stays where the user put it: the stand-in, over an inert select.
    expect(proxy()).toBe(standIn);
    expect(document.activeElement).toBe(standIn);
    expect(element.querySelector('tm-select')!.hasAttribute('inert')).toBe(true);
    expect(stops.count()).toBe(0);

    // Once focus moves on, the stand-in retires and the select takes its place.
    const next = element.querySelector<HTMLElement>('.tm-pager__next')!;
    next.focus();
    await TestBed.inject(ApplicationRef).whenStable();
    expect(proxy()).toBeNull();
    expect(document.activeElement).toBe(next);
    expect(await (await select()).isOpen()).toBe(false);
    expect(stops.count()).toBe(0);
  });

  it('hands focus straight to the trigger on a key pressed after it lands', async () => {
    const { proxy, land, select, trigger } = await held();
    const standIn = proxy()!;
    standIn.focus();
    await land();
    const stops = bodyStops();
    const focused: Element[] = [];
    const record = (event: FocusEvent): void => void focused.push(event.target as Element);
    document.addEventListener('focusin', record);
    onTestFinished(() => document.removeEventListener('focusin', record));

    expect(keydown(standIn, 'ArrowDown').defaultPrevented).toBe(true);
    await TestBed.inject(ApplicationRef).whenStable();
    expect(proxy()).toBeNull();
    expect(await (await select()).isOpen()).toBe(true);
    expect(document.activeElement).toBe(trigger());
    // One move, from the stand-in to the trigger: no stop on the body, and no second arrival.
    expect(focused).toEqual([trigger()]);
    expect(stops.count()).toBe(0);
  });

  it('takes no press or key while disabled', async () => {
    const { proxy, land, select } = await held((h) => h.disabled.set(true));
    const standIn = proxy()!;
    expect(standIn.getAttribute('aria-disabled')).toBe('true');
    expect(standIn.tabIndex).toBe(-1);
    standIn.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, button: 0 }));
    standIn.click();
    expect(keydown(standIn, 'Enter').defaultPrevented).toBe(false);
    await land();
    const harness = await select();
    expect(await harness.isOpen()).toBe(false);
    expect(await harness.isDisabled()).toBe(true);
  });
});

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { page } from 'vitest/browser';

import breakpointsJson from './breakpoints.json';
import { TM_BREAKPOINTS, TmBreakpoints, type TmBreakpoint } from './tm-breakpoints';

/**
 * Resizes the test frame and waits until the frame's own media queries see
 * the new width — the `change` events that feed the service fire after the
 * resize lands, so asserting before then would race them.
 */
async function resizeTo(width: number): Promise<void> {
  await page.viewport(width, 800);
  await vi.waitUntil(() => window.innerWidth === width);
  // One more frame: MediaQueryList change events are dispatched during the
  // rendering steps that follow the resize.
  await new Promise((resolve) => requestAnimationFrame(() => resolve(undefined)));
}

describe('TM_BREAKPOINTS', () => {
  it('reads every threshold from breakpoints.json', () => {
    expect(TM_BREAKPOINTS).toEqual(breakpointsJson);
    expect(TM_BREAKPOINTS).toEqual({ sm: 576, md: 768, lg: 992, xl: 1200, xxl: 1440 });
    expect(Object.isFrozen(TM_BREAKPOINTS)).toBe(true);
  });
});

describe('TmBreakpoints', () => {
  afterEach(async () => {
    await resizeTo(1280);
  });

  const cases: ReadonlyArray<readonly [number, TmBreakpoint]> = [
    [390, 'xs'],
    [576, 'sm'],
    [767, 'sm'],
    [768, 'md'],
    [1440, 'xxl'],
  ];

  for (const [width, expected] of cases) {
    it(`reports ${expected} at ${width} px`, async () => {
      await resizeTo(width);
      const breakpoints = TestBed.inject(TmBreakpoints);
      expect(breakpoints.current()).toBe(expected);
    });
  }

  it('follows a resize across thresholds without a fresh instance', async () => {
    await resizeTo(390);
    const breakpoints = TestBed.inject(TmBreakpoints);
    const md = breakpoints.atLeast('md');
    expect(breakpoints.current()).toBe('xs');
    expect(md()).toBe(false);

    await resizeTo(768);
    expect(breakpoints.current()).toBe('md');
    expect(md()).toBe(true);

    await resizeTo(767);
    expect(breakpoints.current()).toBe('sm');
    expect(md()).toBe(false);
  });

  it('memoizes atLeast per breakpoint', () => {
    const breakpoints = TestBed.inject(TmBreakpoints);
    expect(breakpoints.atLeast('lg')).toBe(breakpoints.atLeast('lg'));
    expect(breakpoints.atLeast('lg')).not.toBe(breakpoints.atLeast('md'));
  });

  it('answers atLeast at each class boundary', async () => {
    await resizeTo(992);
    const breakpoints = TestBed.inject(TmBreakpoints);
    expect(breakpoints.atLeast('sm')()).toBe(true);
    expect(breakpoints.atLeast('md')()).toBe(true);
    expect(breakpoints.atLeast('lg')()).toBe(true);
    expect(breakpoints.atLeast('xl')()).toBe(false);
    expect(breakpoints.atLeast('xxl')()).toBe(false);
  });

  it('always reports atLeast("xs") as true', async () => {
    await resizeTo(390);
    const breakpoints = TestBed.inject(TmBreakpoints);
    expect(breakpoints.current()).toBe('xs');
    expect(breakpoints.atLeast('xs')()).toBe(true);
  });

  it('detaches its media listeners when the injector is destroyed', () => {
    const removed: string[] = [];
    const original = window.matchMedia.bind(window);
    const spy = vi.spyOn(window, 'matchMedia').mockImplementation((query: string) => {
      const list = original(query);
      const remove = list.removeEventListener.bind(list);
      list.removeEventListener = ((type: string, listener: EventListener) => {
        removed.push(query);
        remove(type, listener);
      }) as typeof list.removeEventListener;
      return list;
    });
    try {
      TestBed.inject(TmBreakpoints);
      TestBed.resetTestingModule();
      expect(removed).toEqual([
        '(width >= 576px)',
        '(width >= 768px)',
        '(width >= 992px)',
        '(width >= 1200px)',
        '(width >= 1440px)',
      ]);
    } finally {
      spy.mockRestore();
    }
  });
});

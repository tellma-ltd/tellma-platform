// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { TmFakeTelemetry } from '@tellma/core-testing';

import { TmPageContexts } from '../context/tm-page-contexts';
import { tmAppRig, tmLoadTestPage, tmNavigate, tmSettleUntil } from '../routes/tm-app.testing.util';

// Page visits (spec 0026 §5.1): one `tm.page.visit` per routed page, at the
// next navigation or at `pagehide`, with how it started, when its chrome
// painted and when its data arrived, or that it was left first.

describe('TmPageVisits', () => {
  let telemetry: TmFakeTelemetry;

  /** The visits reported so far, oldest first. */
  const visits = (): readonly Readonly<Record<string, unknown>>[] =>
    telemetry
      .recent()
      .filter((item) => item.name === 'tm.page.visit')
      .map((item) => item.props);

  /** Waits until the routed page's context is ready and its chrome has painted. */
  async function ready(): Promise<void> {
    const contexts = TestBed.inject(TmPageContexts);
    await tmSettleUntil(() => contexts.current()?.status() === 'ready', 'the page is ready');
    // The chrome's stamp lands one animation frame after the render.
    await new Promise((resolve) => requestAnimationFrame(resolve));
  }

  beforeEach(async () => {
    ({ telemetry } = await tmAppRig());
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it("reports the document's first page at the next navigation, and that one at pagehide", async () => {
    const harness = await tmNavigate('/17/centers');
    await ready();
    expect(visits()).toEqual([]);

    await harness.navigateByUrl('/17/centers/5');
    expect(visits()).toEqual([
      { entry: 'load', outcome: 'ready', ms: expect.any(Number), chromeMs: expect.any(Number), visitS: expect.any(Number) },
    ]);
    await ready();

    window.dispatchEvent(new Event('pagehide'));
    window.dispatchEvent(new Event('pagehide'));
    const [, second] = visits();
    expect(visits()).toHaveLength(2);
    expect(second).toEqual({ entry: 'navigation', outcome: 'ready', ms: expect.any(Number), chromeMs: expect.any(Number), visitS: expect.any(Number) });
    // A visit's data arrives within it, and its length is whole tenths of a second.
    expect(second['ms']).toBeLessThanOrEqual((second['visitS'] as number) * 1000 + 50);
    const tenths = (second['visitS'] as number) * 10;
    expect(Math.abs(tenths - Math.round(tenths))).toBeLessThan(1e-9);
  });

  it('reports a page left before its data arrived as left, with how long it was waited for', async () => {
    const harness = await tmNavigate('/17/centers');
    await ready();
    const contexts = TestBed.inject(TmPageContexts);
    const search = contexts.current();
    tmLoadTestPage.mockImplementationOnce(() => new Promise<Type<unknown>>(() => undefined));
    await harness.navigateByUrl('/17/credit-review');
    await tmSettleUntil(() => contexts.current() !== search, 'the page registered its context');
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(contexts.current()?.status()).toBe('loading');

    await harness.navigateByUrl('/17/centers');
    const left = visits()[1];
    expect(left).toMatchObject({ entry: 'navigation', outcome: 'left' });
    expect(left['ms']).toBeGreaterThanOrEqual(20);
  });
});

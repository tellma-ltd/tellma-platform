// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injector, runInInjectionContext, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { page } from 'vitest/browser';

import { provideTellmaForms, type TmControlSize } from './provide-tellma-forms';
import { ɵtmControlSize } from './tm-control-size';

/** Resizes the test frame and waits for its media queries to see the width. */
async function resizeTo(width: number): Promise<void> {
  await page.viewport(width, 800);
  await vi.waitUntil(() => window.innerWidth === width);
  await new Promise((resolve) => requestAnimationFrame(() => resolve(undefined)));
}

/** Resolves a control size in the test injector, with a writable explicit size. */
function resolve(explicit?: TmControlSize) {
  const size = signal<TmControlSize | undefined>(explicit);
  const resolved = runInInjectionContext(TestBed.inject(Injector), () => ɵtmControlSize(size));
  return { size, resolved };
}

describe('the control size resolver', () => {
  afterEach(async () => {
    await resizeTo(1280);
  });

  it('uses the workspace default at 768 px', async () => {
    await resizeTo(768);
    expect(resolve().resolved()).toBe('sm');
  });

  it('uses the lg touch size at 390 px', async () => {
    await resizeTo(390);
    expect(resolve().resolved()).toBe('lg');
  });

  it('lets an explicit size win at either width', async () => {
    await resizeTo(390);
    expect(resolve('sm').resolved()).toBe('sm');
    expect(resolve('md').resolved()).toBe('md');
    await resizeTo(768);
    expect(resolve('lg').resolved()).toBe('lg');
    expect(resolve('md').resolved()).toBe('md');
  });

  it('follows a configured workspace default from sm up', async () => {
    TestBed.configureTestingModule({
      providers: [provideTellmaForms({ formFieldDefaults: { size: 'md' } })],
    });
    await resizeTo(768);
    expect(resolve().resolved()).toBe('md');
    await resizeTo(390);
    expect(resolve().resolved()).toBe('lg');
  });

  it('changes only when the viewport crosses the sm threshold', async () => {
    await resizeTo(600);
    const { size, resolved } = resolve();
    expect(resolved()).toBe('sm');
    await resizeTo(577);
    expect(resolved()).toBe('sm');
    await resizeTo(575);
    expect(resolved()).toBe('lg');
    // Setting and clearing the explicit size flips between it and the default.
    size.set('md');
    expect(resolved()).toBe('md');
    size.set(undefined);
    expect(resolved()).toBe('lg');
  });
});

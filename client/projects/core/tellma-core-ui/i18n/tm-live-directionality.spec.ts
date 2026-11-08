// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Directionality, type Direction } from '@angular/cdk/bidi';
import { TestBed } from '@angular/core/testing';

import { provideTellmaUi } from '../providers/provide-tellma-ui';
import { ɵTmLiveDirectionality } from './tm-live-directionality';

/** MutationObserver callbacks are microtasks: one await lets them run. */
const flushMutations = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

describe('the live root Directionality', () => {
  const html = document.documentElement;
  const body = document.body;
  let emitted: Direction[];
  let direction: Directionality;

  beforeEach(() => {
    html.removeAttribute('dir');
    body.removeAttribute('dir');
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    direction = TestBed.inject(Directionality);
    emitted = [];
    direction.change.subscribe((value: Direction) => emitted.push(value));
  });

  afterEach(() => {
    html.removeAttribute('dir');
    body.removeAttribute('dir');
  });

  it('is what provideTellmaUi installs at the root', () => {
    expect(direction).toBeInstanceOf(ɵTmLiveDirectionality);
    expect(direction.value).toBe('ltr');
  });

  it('follows <html dir> and emits each change once', async () => {
    html.dir = 'rtl';
    await flushMutations();
    expect(direction.value).toBe('rtl');
    expect(direction.valueSignal()).toBe('rtl');
    expect(emitted).toEqual(['rtl']);

    html.dir = 'ltr';
    await flushMutations();
    expect(direction.value).toBe('ltr');
    expect(emitted).toEqual(['rtl', 'ltr']);
  });

  it('follows <body dir>, which wins over <html dir>', async () => {
    html.dir = 'rtl';
    body.dir = 'ltr';
    await flushMutations();
    expect(direction.value).toBe('ltr');

    body.removeAttribute('dir');
    await flushMutations();
    expect(direction.value).toBe('rtl');

    body.dir = 'rtl';
    html.dir = 'ltr';
    await flushMutations();
    expect(direction.value).toBe('rtl');
  });

  it('stays quiet when the direction does not actually change', async () => {
    html.dir = 'ltr';
    await flushMutations();
    html.lang = 'ar';
    await flushMutations();
    html.removeAttribute('lang');
    expect(emitted).toEqual([]);
  });

  it('stops following the attributes once its injector is destroyed', async () => {
    TestBed.resetTestingModule();
    html.dir = 'rtl';
    await flushMutations();
    expect(emitted).toEqual([]);
  });
});

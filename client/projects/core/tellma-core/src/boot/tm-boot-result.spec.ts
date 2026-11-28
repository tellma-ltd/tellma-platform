// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { TmLaunch } from '../launch/tm-launch';
import { TM_BOOT_RESULT } from './tm-boot-result';
import type { TmBootResult } from './tm-boot-types';

// The real seam between the boot script and the app, with no fake: the
// script's result on this document's window, as the token and the launch
// mark read it in production.

/** This document's window, as the boot script leaves its result on it. */
const host = window as Window & { __tmBoot?: Partial<TmBootResult> };

describe('TM_BOOT_RESULT', () => {
  afterEach(() => {
    delete host.__tmBoot;
  });

  it("reads the boot script's result from the window, and the launch mark seeds from it", () => {
    const result: Partial<TmBootResult> = { state: 'run', auto: 17, mark: null, profile: null };
    host.__tmBoot = result;
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    expect(TestBed.inject(TM_BOOT_RESULT)).toBe(result);
    expect(TestBed.inject(TmLaunch).automatic()).toBe(17);
  });

  it('is null when no boot script ran', () => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    expect(TestBed.inject(TM_BOOT_RESULT)).toBeNull();
    expect(TestBed.inject(TmLaunch).automatic()).toBeNull();
  });
});

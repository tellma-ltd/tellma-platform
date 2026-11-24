// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { TmUrls } from '@tellma/core';
import { TmToasts } from '@tellma/core-ui/toast';

import { tmAttentionRig } from '../../src/attention/tm-attention.testing.util';
import { TmOutcomeToasts } from './tm-outcome-toasts';

// The outcome toasts in a whole test app entered into tenant 17: a job
// started in the background, with Track when the job has a page, and a
// sticky export in progress, with Cancel.

describe('TmOutcomeToasts', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("toasts a started job under its key, with Track opening the job's page", async () => {
    await tmAttentionRig();
    const show = vi.spyOn(TestBed.inject(TmToasts), 'show');
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const target = vi.spyOn(TestBed.inject(TmUrls), 'target').mockReturnValue('/17/jobs/5');
    const outcomes = TestBed.inject(TmOutcomeToasts);

    outcomes.jobStarted(5);
    expect(target).toHaveBeenCalledWith('core.Job', 5);
    const [toast] = show.mock.calls.map(([shown]) => shown);
    expect(toast).toMatchObject({ tone: 'accent', title: 'Started in the background.', duration: 4000, key: 'job:5' });
    expect(toast?.action?.label).toBe('Track');
    toast?.action?.run();
    expect(navigate).toHaveBeenCalledWith('/17/jobs/5');
  });

  it('toasts a started job without Track when the job has no page', async () => {
    await tmAttentionRig();
    const show = vi.spyOn(TestBed.inject(TmToasts), 'show');
    vi.spyOn(TestBed.inject(TmUrls), 'target').mockReturnValue(null);
    TestBed.inject(TmOutcomeToasts).jobStarted(6);
    expect(show.mock.calls[0]?.[0].action).toBeUndefined();
  });

  it('keeps an export in progress until it closes, with Cancel', async () => {
    await tmAttentionRig();
    const show = vi.spyOn(TestBed.inject(TmToasts), 'show');
    const cancel = vi.fn();
    const ref = TestBed.inject(TmOutcomeToasts).exporting(cancel);
    const [toast] = show.mock.calls.map(([shown]) => shown);
    expect(toast).toMatchObject({ tone: 'neutral', title: 'Exporting…', duration: 'sticky' });
    expect(toast?.action?.label).toBe('Cancel');
    toast?.action?.run();
    expect(cancel).toHaveBeenCalledOnce();
    ref.close();
    await expect(ref.closed).resolves.toBe('api');
  });
});

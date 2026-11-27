// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { createEnvironmentInjector, EnvironmentInjector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { TmUrls } from '@tellma/core';
import { TmToasts } from '@tellma/core-ui/toast';

import { tmAttentionRig } from '../../src/attention/tm-attention.testing.util';
import { TmOutcomeToasts } from './tm-outcome-toasts';

// The outcome toasts in a whole test app entered into tenant 17: a job
// started in the background, with Track when the job has a page, and a
// sticky export in progress, with Cancel; each once the toasts' code has
// loaded, through a handle answered at once that holds an update or a
// close until then, and closes when the code cannot load.

describe('TmOutcomeToasts', () => {
  // The toasts' chunk, loaded once up front: a test waits on the toast, which no count of turns bounds while the
  // test runner first fetches the chunk.
  beforeAll(async () => {
    await import('./tm-outcome-toasts-body');
  });

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
    await vi.waitFor(() => expect(show).toHaveBeenCalled());
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
    await vi.waitFor(() => expect(show).toHaveBeenCalled());
    expect(show.mock.calls[0]?.[0].action).toBeUndefined();
  });

  it('keeps an export in progress until it closes, with Cancel', async () => {
    await tmAttentionRig();
    const show = vi.spyOn(TestBed.inject(TmToasts), 'show');
    const cancel = vi.fn();
    const ref = TestBed.inject(TmOutcomeToasts).exporting(cancel);
    await vi.waitFor(() => expect(show).toHaveBeenCalled());
    const [toast] = show.mock.calls.map(([shown]) => shown);
    expect(toast).toMatchObject({ tone: 'neutral', title: 'Exporting…', duration: 'sticky' });
    expect(toast?.action?.label).toBe('Cancel');
    toast?.action?.run();
    expect(cancel).toHaveBeenCalledOnce();
    ref.close();
    await expect(ref.closed).resolves.toBe('api');
  });

  it('applies an update made before the toast showed as it shows', async () => {
    await tmAttentionRig();
    const update = vi.fn();
    const shown = { close: vi.fn(), update, closed: new Promise<'api'>(() => undefined) };
    const show = vi.spyOn(TestBed.inject(TmToasts), 'show').mockReturnValue(shown);
    const ref = TestBed.inject(TmOutcomeToasts).exporting(() => undefined);
    ref.update({ detail: '5 of 9 rows' });
    expect(show).not.toHaveBeenCalled();
    await vi.waitFor(() => expect(update).toHaveBeenCalledWith({ detail: '5 of 9 rows' }));
    ref.update({ detail: '6 of 9 rows' });
    expect(update).toHaveBeenLastCalledWith({ detail: '6 of 9 rows' });
  });

  it('shows nothing for a toast closed before its code loaded', async () => {
    await tmAttentionRig();
    const show = vi.spyOn(TestBed.inject(TmToasts), 'show');
    const ref = TestBed.inject(TmOutcomeToasts).exporting(() => undefined);
    ref.close();
    await expect(ref.closed).resolves.toBe('api');
    // A later toast shows, so the chunk has landed by then; the closed one never did.
    TestBed.inject(TmOutcomeToasts).jobStarted(7);
    await vi.waitFor(() => expect(show).toHaveBeenCalledOnce());
    expect(show.mock.calls[0]?.[0].title).toBe('Started in the background.');
  });

  it('reports a toast whose code cannot load, and closes its handle', async () => {
    const rig = await tmAttentionRig();
    // The service in an injector whose toasts cannot be created, so creating the loaded chunk's body fails.
    const broken = createEnvironmentInjector(
      [
        TmOutcomeToasts,
        {
          provide: TmToasts,
          useFactory: () => {
            throw new Error('The toasts cannot load.');
          },
        },
      ],
      TestBed.inject(EnvironmentInjector),
    );
    const ref = broken.get(TmOutcomeToasts).jobStarted(8);
    await expect(ref.closed).resolves.toBe('api');
    expect(rig.telemetry.errors()).toContainEqual(expect.objectContaining({ message: 'The toasts cannot load.' }));
  });
});

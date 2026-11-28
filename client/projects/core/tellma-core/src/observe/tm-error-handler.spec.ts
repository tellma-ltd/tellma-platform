// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ErrorHandler } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { tmAppRig, tmNavigate, tmSettleUntil } from '../routes/tm-app.testing.util';
import { TmErrorHandler } from './tm-error-handler';
import { TmTelemetry, TmTelemetryFront, type TmTelemetryItem } from './tm-telemetry';

describe('TmErrorHandler', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  it('is the error handler provideTellma installs', async () => {
    await tmAppRig();
    expect(TestBed.inject(ErrorHandler)).toBeInstanceOf(TmErrorHandler);
  });

  it('reports each error to telemetry', async () => {
    const rig = await tmAppRig();
    const failure = new TypeError('boom');
    TestBed.inject(ErrorHandler).handleError(failure);

    expect(rig.telemetry.errors()).toEqual([failure]);
    expect(rig.telemetry.recent().at(-1)).toMatchObject({ name: 'tm.error.captured', props: { errorClass: 'TypeError' } });
  });

  // With the app's own telemetry, whose pipeline tells the sources apart.
  describe('sources', () => {
    let errors: () => readonly TmTelemetryItem[];
    let handler: ErrorHandler;

    /**
     * Hands an error to the handler while an event is dispatched, as the
     * browser's global listeners do.
     *
     * @param event - The event.
     * @param error - The error.
     */
    function duringEvent(event: Event, error: unknown): void {
      const target = new EventTarget();
      target.addEventListener(event.type, () => handler.handleError(error));
      target.dispatchEvent(event);
    }

    beforeEach(async () => {
      vi.spyOn(console, 'debug').mockImplementation(() => undefined);
      vi.spyOn(console, 'error').mockImplementation(() => undefined);
      await tmAppRig({ providers: [{ provide: TmTelemetry, useFactory: () => new TmTelemetryFront() }] });
      const telemetry = TestBed.inject(TmTelemetry);
      errors = () => telemetry.recent().filter((item) => item.name === 'tm.error.captured');
      handler = TestBed.inject(ErrorHandler);
      // The pipeline loads at idle after the first render.
      await tmNavigate('/17/centers/5');
      telemetry.event('acme.spec.started');
      await tmSettleUntil(() => telemetry.recent().some((item) => item.name === 'acme.spec.started'), 'the pipeline took a call');
    });

    it('marks an error reported outside a global event render, and logs it', () => {
      const failure = new TypeError('boom');
      handler.handleError(failure);
      expect(errors().at(-1)?.props).toMatchObject({ source: 'render', errorClass: 'TypeError' });
      expect(console.error).toHaveBeenCalledWith(failure);
    });

    it('marks an uncaught error global', () => {
      const failure = new RangeError('boom');
      duringEvent(new ErrorEvent('error', { error: failure }), failure);
      expect(errors().at(-1)?.props).toMatchObject({ source: 'global', errorClass: 'RangeError' });
    });

    it('marks an unhandled rejection global', () => {
      const failure = new SyntaxError('boom');
      duringEvent(new PromiseRejectionEvent('unhandledrejection', { promise: Promise.resolve(), reason: failure }), failure);
      expect(errors().at(-1)?.props).toMatchObject({ source: 'global', errorClass: 'SyntaxError' });
    });

    it('keeps an error reported during another event render', () => {
      const failure = new EvalError('boom');
      duringEvent(new Event('click'), failure);
      expect(errors().at(-1)?.props).toMatchObject({ source: 'render', errorClass: 'EvalError' });
    });
  });
});

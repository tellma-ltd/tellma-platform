// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { TmTelemetry } from '@tellma/core';

import { provideTellmaTesting } from './provide-tellma-testing';
import { TmFakeTelemetry } from './tm-fake-telemetry';

describe('provideTellmaTesting', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTellmaTesting()] });
  });

  it('puts the fake telemetry behind TmTelemetry', () => {
    expect(TestBed.inject(TmTelemetry)).toBe(TestBed.inject(TmFakeTelemetry));
  });

  it('records events, timings and errors in order', () => {
    const telemetry = TestBed.inject(TmTelemetry);
    const failure = new TypeError('bad');
    telemetry.event('acme.report.run', { rows: 3 });
    telemetry.timing('acme.report.render', 12, { kind: 'summary' });
    telemetry.error(failure, { where: 'render' });

    const fake = TestBed.inject(TmFakeTelemetry);
    expect(fake.recent().map(({ kind, name, props, tenantId }) => ({ kind, name, props, tenantId }))).toEqual([
      { kind: 'event', name: 'acme.report.run', props: { rows: 3 }, tenantId: null },
      { kind: 'timing', name: 'acme.report.render', props: { kind: 'summary', ms: 12 }, tenantId: null },
      { kind: 'error', name: 'tm.error.captured', props: { where: 'render', errorClass: 'TypeError' }, tenantId: null },
    ]);
    expect(fake.errors()).toEqual([failure]);
  });

  it('keeps the trace id the options name', () => {
    const telemetry = TestBed.inject(TmTelemetry);
    const traceId = '4bf92f3577b34da6a3ce929d0e0e4736';
    telemetry.event('acme.report.run', {}, { traceId });
    telemetry.timing('acme.report.render', 5, {}, { traceId, sampled: true });
    telemetry.event('acme.report.closed');

    expect(TestBed.inject(TmFakeTelemetry).recent().map((item) => item.traceId)).toEqual([traceId, traceId, undefined]);
  });

  it('counts flushes and forgets everything on clear', () => {
    const fake = TestBed.inject(TmFakeTelemetry);
    fake.event('acme.report.run');
    fake.flush();
    fake.flush();
    expect(fake.flushes()).toBe(2);

    fake.clear();
    expect(fake.recent()).toEqual([]);
    expect(fake.errors()).toEqual([]);
    expect(fake.flushes()).toBe(0);
  });
});

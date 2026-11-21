// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injectable, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { TmTelemetry, type TmTelemetryItem } from './tm-telemetry';

describe('TmTelemetry', () => {
  it('is quiet by default: it accepts every item, keeps none and never throws', () => {
    const telemetry = TestBed.inject(TmTelemetry);
    expect(() => {
      telemetry.event('acme.report.run', { rows: 3 });
      telemetry.timing('acme.report.render', 12);
      telemetry.error(new Error('boom'), { where: 'test' });
      telemetry.flush();
    }).not.toThrow();
    expect(telemetry.recent()).toEqual([]);
  });

  it('is one instance per root injector', () => {
    expect(TestBed.inject(TmTelemetry)).toBe(TestBed.inject(TmTelemetry));
  });

  it('is replaced by a provided implementation', () => {
    @Injectable()
    class Recording extends TmTelemetry {
      readonly #items = signal<readonly TmTelemetryItem[]>([]);
      override readonly recent = this.#items.asReadonly();
      override event(name: string): void {
        this.#items.update((items) => [...items, { kind: 'event', name, at: 0, tenantId: null, props: {} }]);
      }
      override timing(): void {}
      override error(): void {}
      override flush(): void {}
    }
    TestBed.configureTestingModule({ providers: [{ provide: TmTelemetry, useClass: Recording }] });

    TestBed.inject(TmTelemetry).event('acme.report.run');
    expect(TestBed.inject(TmTelemetry).recent().map((item) => item.name)).toEqual(['acme.report.run']);
  });
});

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { provideTellmaUi } from '@tellma/core-ui';
import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';

import { TmChart } from './tm-chart';
import type { TmChartSpec } from './tm-chart-types';

/*
 * The app's web host serves a policy with `script-src 'self'`, no
 * 'unsafe-eval', and Trusted Types required for scripts (spec 0024 §6).
 * The chart's engine draws on canvas and its tooltip is rich text on the
 * same canvas (spec 0034 §7.5), so it needs no Trusted Types policy; its
 * renderer's one markup assignment, emptying its root, goes through the
 * DOM. This file enforces the policy on its test page and only then loads
 * the engine, draws, hovers and disposes; a markup assignment or an eval
 * would throw, and any refused sink would report a violation.
 *
 * A CSP <meta> can never be lifted once applied, so this is a
 * `*.csp.spec.ts`, run by the `test-csp` target in a page of its own.
 */
const POLICY =
  "script-src 'self'; require-trusted-types-for 'script'; trusted-types angular angular#bundler tellma";

/** Months by customer type, with a threshold. */
const SPEC: TmChartSpec = {
  type: 'column',
  title: 'Sales',
  dimensions: [
    { label: 'Month', members: [{ label: 'Sep' }, { label: 'Oct' }] },
    { label: 'Customer type', members: [{ label: 'Corporate' }, { label: 'Retail' }] },
  ],
  measure: { label: 'Sales' },
  rows: [
    [0, 0, 300],
    [0, 1, 100],
    [1, 0, 200],
    [1, 1, 250],
  ],
  options: { thresholds: [{ value: 280, tone: 'warning' }], labels: true },
};

/** A chart, rendered only once `shown` is set. */
@Component({
  imports: [TmChart],
  template: `
    @if (shown()) {
      <tm-chart style="inline-size: 480px; block-size: 240px" [spec]="spec()" />
    }
  `,
})
class ChartHost {
  /** Whether the chart is in the DOM. */
  readonly shown = signal(false);
  /** The chart's spec. */
  readonly spec = signal<TmChartSpec>(SPEC);
}

/** Waits for a frame and a task, then for the render it causes. */
async function settle(fixture: { whenStable(): Promise<unknown> }): Promise<void> {
  await new Promise((resolve) => requestAnimationFrame(() => setTimeout(resolve)));
  await fixture.whenStable();
}

describe('tm-chart under a strict Content Security Policy', () => {
  it('loads the engine, draws, shows its tooltip and disposes with no Trusted Types policy', async () => {
    // Everything that may legitimately need the JIT (TestBed's dynamic test
    // module) is created BEFORE the policy applies; the engine loads only
    // after it.
    const tokens = document.createElement('style');
    tokens.textContent = tmEmitCss(tmTokensDefault);
    document.head.appendChild(tokens);
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(ChartHost);
    fixture.autoDetectChanges();
    await fixture.whenStable();

    const violations: string[] = [];
    const errors: unknown[] = [];
    document.addEventListener('securitypolicyviolation', (event) =>
      violations.push(`${event.violatedDirective}: ${event.sample}`),
    );
    window.addEventListener('error', (event) => errors.push(event.error));
    window.addEventListener('unhandledrejection', (event) => errors.push(event.reason));
    const meta = document.createElement('meta');
    meta.httpEquiv = 'Content-Security-Policy';
    meta.content = POLICY;
    document.head.appendChild(meta);
    // The policy is live in this frame: a markup sink is refused.
    expect(() => {
      document.createElement('div').innerHTML = '<b>x</b>';
    }).toThrow();

    // Section: draw. The engine chunk loads now, under the policy.
    fixture.componentInstance.shown.set(true);
    const root = fixture.nativeElement as HTMLElement;
    for (let attempt = 0; attempt < 50 && root.querySelector('tm-chart canvas') === null; attempt++) {
      await settle(fixture);
    }
    const canvas = root.querySelector<HTMLCanvasElement>('tm-chart canvas');
    expect(canvas).not.toBeNull();

    // Section: hover a column, which draws the rich-text tooltip on the canvas.
    const box = canvas!.getBoundingClientRect();
    for (const name of ['mousemove', 'mousemove'] as const) {
      canvas!.dispatchEvent(
        new MouseEvent(name, {
          bubbles: true,
          clientX: box.left + box.width * 0.3,
          clientY: box.top + box.height * 0.6,
        }),
      );
      await settle(fixture);
    }
    // The tooltip is canvas pixels: the plot holds no text, no HTML tooltip.
    const plot = root.querySelector('tm-chart [role="img"]')!;
    expect(plot.textContent).toBe('');
    expect([...plot.querySelectorAll('*')].every((node) => ['DIV', 'CANVAS'].includes(node.tagName))).toBe(true);

    // Section: redraw, then dispose, which empties the engine's root.
    fixture.componentInstance.spec.set({ ...SPEC, type: 'line' });
    await settle(fixture);
    fixture.componentInstance.shown.set(false);
    await settle(fixture);
    expect(root.querySelector('tm-chart')).toBeNull();

    // Only the probe above may have been refused, and nothing threw.
    expect(violations.filter((violation) => !violation.includes('<b>x</b>'))).toEqual([]);
    expect(errors).toEqual([]);
    tokens.remove();
  });
});

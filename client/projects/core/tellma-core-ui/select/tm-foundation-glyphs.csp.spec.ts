// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmCheckbox } from '@tellma/core-ui/checkbox';
import { TmFormField } from '@tellma/core-ui/form-field';
import { TmInput } from '@tellma/core-ui/input';

import { TmMultiSelect } from './tm-multi-select';
import { TmOption } from './tm-option';
import { TmOptionGroup } from './tm-option-group';
import { TmSelect } from './tm-select';

/*
 * The app's web host serves a policy with `script-src 'self'`, no
 * 'unsafe-eval', and Trusted Types required for scripts (spec 0024 §6).
 * The foundation's glyphs (spec 0010 §B.7) draw through tm-icon, which
 * builds each <svg> from DOM nodes and never assigns markup, so none of
 * them needs a Trusted Types policy. This file enforces the policy on its
 * test page and only then draws every glyph and opens both selects; any
 * markup assignment would throw, and any refused sink would report a
 * violation.
 *
 * A CSP <meta> can never be lifted once applied, so this is a
 * `*.csp.spec.ts`, run by the `test-csp` target in a page of its own.
 */
const POLICY =
  "script-src 'self'; require-trusted-types-for 'script'; trusted-types angular angular#bundler tellma";

/** Every foundation glyph and both select panels, rendered only once `shown` is set. */
@Component({
  imports: [TmCheckbox, TmFormField, TmInput, TmMultiSelect, TmOption, TmOptionGroup, TmSelect],
  template: `
    @if (shown()) {
      <tm-checkbox [checked]="true">Checked</tm-checkbox>
      <tm-checkbox [indeterminate]="true">Mixed</tm-checkbox>
      <tm-form-field label="Account" error="Pick an account">
        <input tmInput value="4110" />
      </tm-form-field>
      <tm-select aria-label="Account" [(value)]="account">
        <tm-option [value]="1" label="Cash">Cash</tm-option>
      </tm-select>
      <tm-multi-select aria-label="Types" [(value)]="types">
        <tm-option-group label="Operating">
          <tm-option value="Service" label="Service">Service</tm-option>
        </tm-option-group>
        <tm-option value="Sale" label="Sale">Sale</tm-option>
      </tm-multi-select>
    }
  `,
})
class GlyphsHost {
  readonly shown = signal(false);
  readonly account = signal<number | undefined>(1);
  readonly types = signal<readonly string[]>(['Sale']);
}

describe('foundation glyphs under a strict Content Security Policy', () => {
  it('draw, and both select panels open, with no Trusted Types policy', async () => {
    // Everything that may legitimately need the JIT (TestBed's dynamic test
    // module) is created BEFORE the policy applies; every glyph is drawn
    // only after it.
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(GlyphsHost);
    await fixture.whenStable();

    const violations: string[] = [];
    document.addEventListener('securitypolicyviolation', (event) =>
      violations.push(`${event.violatedDirective}: ${event.sample}`),
    );
    const meta = document.createElement('meta');
    meta.httpEquiv = 'Content-Security-Policy';
    meta.content = POLICY;
    document.head.appendChild(meta);
    // The policy is live in this frame: a markup sink is refused.
    expect(() => {
      document.createElement('div').innerHTML = '<b>x</b>';
    }).toThrow();

    fixture.componentInstance.shown.set(true);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    // The checkbox check and dash, the invalid mark, and both selects' carets.
    expect(root.querySelectorAll('tm-icon > svg').length).toBe(5);

    for (const trigger of root.querySelectorAll<HTMLElement>('.tm-select__trigger')) {
      trigger.focus();
      trigger.click();
      await fixture.whenStable();
      await new Promise((resolve) => setTimeout(resolve, 0));
      await fixture.whenStable();
      // The panel's option checks and row checkboxes drew too.
      const panel = document.querySelector('.tm-select__panel')!;
      expect(panel.querySelectorAll('tm-icon > svg').length).toBeGreaterThan(0);
      trigger.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
      await fixture.whenStable();
    }
    // Only the probe above may have been refused.
    expect(violations.filter((violation) => !violation.includes('<b>x</b>'))).toEqual([]);
  });
});

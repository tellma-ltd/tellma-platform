// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, inject, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import type { ValidationError } from '@angular/forms/signals';

import { tmResolveFieldErrors } from '../forms/field-errors';
import { provideTellmaUi } from '../providers/provide-tellma-ui';
import { TM_UI_I18N_SCOPE, TM_UI_TRANSLATE } from './tm-ui-translate';

@Component({ template: `` })
class Host {
  readonly translate = inject(TM_UI_TRANSLATE);
  readonly transloco = inject(TranslocoService);
}

async function setup() {
  TestBed.configureTestingModule({
    // 'am'/'xx' stand in for tenant locales a distribution nominates.
    providers: [provideTellmaUi({ availableLangs: ['en', 'am', 'xx'] })],
  });
  const fixture = TestBed.createComponent(Host);
  await fixture.whenStable();
  // Give the translation load a macrotask to settle.
  await new Promise((resolve) => setTimeout(resolve, 0));
  return fixture.componentInstance;
}

describe('TM_UI_TRANSLATE (Transloco-backed default)', () => {
  it('resolves built-in English strings', async () => {
    const host = await setup();
    const text = host.translate('errors.required');
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(text()).toBe('This field is required');
  });

  it('interpolates params with ICU plurals', async () => {
    const host = await setup();
    const one = host.translate('errors.minLength', { minLength: 1 });
    const many = host.translate('errors.minLength', { minLength: 8 });
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(one()).toBe('Enter at least 1 character');
    expect(many()).toBe('Enter at least 8 characters');
  });

  it('resolves without a blank first frame — the very first read carries text', async () => {
    const host = await setup();
    // No settling: read the signal synchronously on creation.
    expect(host.translate('errors.required')()).toBe('This field is required');
  });

  it('never subscribes or serializes per call: circular params are fine', async () => {
    const host = await setup();
    const params: Record<string, unknown> = { min: 3 };
    params['self'] = params; // JSON.stringify would throw on this
    expect(host.translate('errors.min', params)()).toBe('Enter a value of at least 3');
  });

  it('a fresh signal per call still resolves consistently (computed()-friendly)', async () => {
    const host = await setup();
    // Distinct params objects per call — the per-keystroke validator shape
    // that used to grow a permanent cache entry each time.
    for (let i = 1; i <= 50; i++) {
      expect(host.translate('errors.minLength', { minLength: 8 })()).toBe(
        'Enter at least 8 characters',
      );
    }
  });

  it('falls back to English when the active locale has no pack at all', async () => {
    const host = await setup();
    host.transloco.setActiveLang('am'); // no Amharic pack installed
    const text = host.translate('errors.email');
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(text()).toBe('Enter a valid email address');
  });

  it('falls back per-key when a pack is present but incomplete', async () => {
    const host = await setup();
    // A partial "pseudo pack": has required, lacks email.
    host.transloco.setTranslation(
      { [TM_UI_I18N_SCOPE]: { errors: { required: 'PSEUDO required' } } },
      'xx',
      { merge: true },
    );
    host.transloco.setActiveLang('xx');
    const required = host.translate('errors.required');
    const email = host.translate('errors.email');
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(required()).toBe('PSEUDO required');
    expect(email()).toBe('Enter a valid email address'); // never the raw key
  });

  it('re-renders already-resolved strings when the locale switches at runtime', async () => {
    const host = await setup();
    host.transloco.setTranslation(
      { [TM_UI_I18N_SCOPE]: { errors: { required: 'XX required' } } },
      'xx',
      { merge: true },
    );
    const text = host.translate('errors.required');
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(text()).toBe('This field is required');

    host.transloco.setActiveLang('xx');
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(text()).toBe('XX required'); // the SAME signal, recomputed
  });

  it('guards an unknown custom kind with the raw kind + dev warning', async () => {
    const host = await setup();
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    const text = host.translate('errors.myCustomKind');
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(text()).toBe('myCustomKind');
    expect(warn).toHaveBeenCalled();
    warn.mockRestore();
  });
});

describe('tmResolveFieldErrors (message precedence, §5)', () => {
  it('schema-inline message wins; otherwise the localized kind default', async () => {
    const host = await setup();
    const errors = signal<readonly ValidationError.WithOptionalFieldTree[]>([
      { kind: 'required', message: 'Inline schema message' },
      { kind: 'minLength', minLength: 3 } as unknown as ValidationError.WithOptionalFieldTree,
    ]);
    const resolved = tmResolveFieldErrors(errors, host.translate);
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(resolved().map((e) => e.message)).toEqual([
      'Inline schema message',
      'Enter at least 3 characters',
    ]);
    expect(resolved().map((e) => e.kind)).toEqual(['required', 'minLength']);
  });
});

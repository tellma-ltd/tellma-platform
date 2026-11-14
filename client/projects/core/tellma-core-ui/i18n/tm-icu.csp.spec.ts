// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';

import { provideTellmaUi } from '../providers/provide-tellma-ui';
import { TM_UI_I18N_SCOPE, TM_UI_TRANSLATE } from './tm-ui-translate';

/*
 * The app's web host serves a policy with `script-src 'self'`, no
 * 'unsafe-eval', and Trusted Types required for scripts (spec 0024 §6).
 * This file enforces the same policy on its test page and then formats
 * messages through the real Transloco path; an engine that compiled
 * messages to JavaScript would throw here.
 *
 * A CSP <meta> can never be lifted once applied, and the unit runner shares
 * one page between the files of a run. So this file is a `*.csp.spec.ts`:
 * the main `test` target excludes that suffix and the `test-csp` target
 * runs it in a run (and a page) of its own, where the policy can reach no
 * other suite.
 */
const POLICY =
  "script-src 'self'; require-trusted-types-for 'script'; trusted-types angular angular#bundler tellma";

/** Arabic messages registered for the test: one plural, one select. */
const ARABIC = {
  csp: {
    files:
      '{count, plural, zero {لا ملفات} one {ملف واحد} two {ملفان} few {# ملفات} many {# ملفا} other {# ملف}}',
    reply: '{gender, select, female {ردّت} other {ردّ}}',
  },
};

/** The English twins, so both languages format the same shapes. */
const ENGLISH = {
  csp: {
    files: '{count, plural, =0 {No files} one {# file} other {# files}}',
    reply: '{gender, select, female {She replied} other {They replied}}',
  },
};

describe('message formatting under a strict Content Security Policy', () => {
  it('formats a plural and a select in English and Arabic without eval', () => {
    // Everything that may legitimately need the JIT (TestBed's dynamic test
    // module) is created BEFORE the policy applies; message formatting
    // happens only after it.
    TestBed.configureTestingModule({
      providers: [provideTellmaUi({ availableLangs: ['en', 'ar'] })],
    });
    const transloco = TestBed.inject(TranslocoService);
    const translate = TestBed.inject(TM_UI_TRANSLATE);
    transloco.setTranslation({ [TM_UI_I18N_SCOPE]: ENGLISH }, 'en', { merge: true });
    transloco.setTranslation({ [TM_UI_I18N_SCOPE]: ARABIC }, 'ar', { merge: true });

    const meta = document.createElement('meta');
    meta.httpEquiv = 'Content-Security-Policy';
    meta.content = POLICY;
    document.head.appendChild(meta);

    // The policy is live in this frame: compiling a string to code is refused.
    expect(() => new Function('return 1')).toThrow();

    expect(translate('csp.files', { count: 0 })()).toBe('No files');
    expect(translate('csp.files', { count: 1 })()).toBe('1 file');
    expect(translate('csp.files', { count: 12 })()).toBe('12 files');
    expect(translate('csp.reply', { gender: 'female' })()).toBe('She replied');
    expect(translate('csp.reply', { gender: 'other' })()).toBe('They replied');
    // The library's own plural strings take the same path.
    expect(translate('errors.minLength', { minLength: 3 })()).toBe('Enter at least 3 characters');

    transloco.setActiveLang('ar');
    expect(translate('csp.files', { count: 0 })()).toBe('لا ملفات');
    expect(translate('csp.files', { count: 2 })()).toBe('ملفان');
    expect(translate('csp.files', { count: 5 })()).toBe('5 ملفات');
    expect(translate('csp.files', { count: 11 })()).toBe('11 ملفا');
    expect(translate('csp.reply', { gender: 'female' })()).toBe('ردّت');
    expect(translate('csp.reply', { gender: 'other' })()).toBe('ردّ');
  });
});

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { describe, expect, it } from 'vitest';

import { TM_BOOT_MARKER, tmRenderBootScript, type TmBootScriptConfig } from './boot-script.js';
import { TM_INDEX_HTML_PROBLEM, TM_INDEX_HTML_RULES } from './index-html-rules.js';

const TEMPLATE = `var TM_BOOT=${TM_BOOT_MARKER};(function(){window.__tmBoot=TM_BOOT})();`;

const ENGLISH = {
  Distribution_DisplayName: 'Acme',
  Boot_Title: "{displayName} couldn't start",
  Boot_Unsupported: 'Update your browser.',
  Boot_Cookies: 'Turn on cookies.',
  Boot_SignInFailed: "We couldn't sign you in.",
  Boot_LoadFailed: 'Check your connection.',
  Boot_TryAgain: 'Try again',
};

const CONFIG: TmBootScriptConfig = {
  v: 1,
  languages: [
    { code: 'en', dir: 'ltr' },
    { code: 'ar', dir: 'rtl' },
  ],
  defaultLanguage: 'en',
  strings: { en: ENGLISH, ar: { Distribution_DisplayName: 'أكمي' } },
};

/** The configuration a rendered script carries. */
function carried(script: string): unknown {
  return JSON.parse(script.slice('var TM_BOOT='.length, script.indexOf(';(function')));
}

describe('tmRenderBootScript', () => {
  it('replaces the marker with the configuration as JSON', () => {
    const script = tmRenderBootScript(TEMPLATE, CONFIG);
    expect(script).not.toContain(TM_BOOT_MARKER);
    expect(carried(script)).toEqual(CONFIG);
  });

  it('writes only the members the script reads', () => {
    const languages = [{ code: 'en', dir: 'ltr', label: 'English' }, { code: 'ar', dir: 'rtl', label: 'العربية' }];
    const extra = { ...CONFIG, secret: 'x', languages } as TmBootScriptConfig;
    expect(carried(tmRenderBootScript(TEMPLATE, extra))).toEqual(CONFIG);
  });

  it('escapes the line and paragraph separators', () => {
    const separators = String.fromCharCode(0x2028, 0x2029);
    const script = tmRenderBootScript(TEMPLATE, { ...CONFIG, strings: { ...CONFIG.strings, ar: { Boot_TryAgain: separators } } });
    expect(script).not.toContain(separators[0]);
    expect(script).not.toContain(separators[1]);
    expect(carried(script)).toMatchObject({ strings: { ar: { Boot_TryAgain: separators } } });
  });

  it('needs the marker exactly once', () => {
    expect(() => tmRenderBootScript('var TM_BOOT=null;', CONFIG)).toThrowError(/exactly once/);
    expect(() => tmRenderBootScript(TEMPLATE + TEMPLATE, CONFIG)).toThrowError(/exactly once/);
  });

  it('refuses a configuration the boot script cannot trust', () => {
    const invalid: [string, unknown][] = [
      ['version', { ...CONFIG, v: 2 }],
      ['at least one language', { ...CONFIG, languages: [] }],
      ['not a language tag', { ...CONFIG, languages: [{ code: 'en"', dir: 'ltr' }] }],
      ['direction', { ...CONFIG, languages: [{ code: 'en', dir: 'auto' }] }],
      ['offered twice', { ...CONFIG, languages: [CONFIG.languages[0], CONFIG.languages[0]] }],
      ['default language', { ...CONFIG, defaultLanguage: 'fr' }],
      ['not offered', { ...CONFIG, strings: { ...CONFIG.strings, fr: {} } }],
      ['not a boot string', { ...CONFIG, strings: { ...CONFIG.strings, ar: { Other: 'x' } } }],
      ['not a boot string', { ...CONFIG, strings: { ...CONFIG.strings, ar: { Boot_TryAgain: 1 } } }],
      ['English string', { ...CONFIG, strings: { ar: {} } }],
      ['English string', { ...CONFIG, strings: { en: { ...ENGLISH, Boot_Title: '' } } }],
    ];
    for (const [reason, config] of invalid) {
      expect(() => tmRenderBootScript(TEMPLATE, config as TmBootScriptConfig), reason).toThrowError(reason);
    }
  });
});

describe('the index.html rules', () => {
  it('report one problem code, and check the event handler attributes in the built file only', () => {
    expect(TM_INDEX_HTML_PROBLEM).toBe('index-html-invalid');
    expect(TM_INDEX_HTML_RULES.map((rule) => [rule.id, rule.scope])).toEqual([
      ['base-href', 'both'],
      ['boot-script-first', 'both'],
      ['app-element', 'both'],
      ['manifest-link', 'both'],
      ['color-scheme-meta', 'both'],
      ['no-inline-script', 'both'],
      ['no-event-handler-attribute', 'built'],
    ]);
  });
});

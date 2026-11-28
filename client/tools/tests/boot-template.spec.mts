// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { Script, createContext } from 'node:vm';
import { gzipSync } from 'node:zlib';

import { beforeAll, describe, expect, it } from 'vitest';

import { TM_BOOT_MARKER, tmRenderBootScript, type TmBootScriptConfig } from '../../projects/core/tellma-core-tools/src/build/boot-script.js';
import { bootTemplateBanner, buildBootTemplate } from '../boot/boot-template.mjs';

// The generated boot script: built from @tellma/core's sources the way the
// package build builds it, rendered with the platform's own boot strings,
// held to its size budget, and run as the classic script a browser runs,
// over a minimal stand-in for the browser's globals.

const repoDir = join(dirname(fileURLToPath(import.meta.url)), '..', '..', '..');

/** The `Boot_*` strings of one Core resource file. */
function bootStrings(file: string): Record<string, string> {
  const text = readFileSync(join(repoDir, 'src', 'core', 'Tellma.Core', 'Resources', file), 'utf8');
  const strings: Record<string, string> = {};
  for (const match of text.matchAll(/<data name="(Boot_\w+)"[^>]*>\s*<value>([^<]*)<\/value>/g)) {
    strings[match[1]!] = match[2]!.replace(/&amp;/g, '&');
  }
  return strings;
}

/** The boot configuration of a distribution offering English only, with the platform's strings. */
const ENGLISH: TmBootScriptConfig = {
  v: 1,
  languages: [{ code: 'en', dir: 'ltr' }],
  defaultLanguage: 'en',
  strings: { en: { Distribution_DisplayName: 'Acme', ...bootStrings('Strings.resx') } },
};

/** Acme's configuration: English and Arabic, with the platform's strings. */
const ACME: TmBootScriptConfig = {
  ...ENGLISH,
  languages: [...ENGLISH.languages, { code: 'ar', dir: 'rtl' }],
  strings: { ...ENGLISH.strings, ar: { Distribution_DisplayName: 'أكمي', ...bootStrings('Strings.ar.resx') } },
};

/** The same strings, offering Arabic only; English stays embedded as the fallback. */
const ARABIC: TmBootScriptConfig = {
  ...ACME,
  languages: [{ code: 'ar', dir: 'rtl' }],
  defaultLanguage: 'ar',
};

/**
 * The gzip budget of a rendered boot script, in bytes: 3 KB for the code
 * with the English strings, which every script embeds as the fallback, plus
 * 0.4 KB for each further string table.
 */
function budget(config: TmBootScriptConfig): number {
  const further = Object.keys(config.strings).filter((code) => code !== 'en').length;
  return (3 + 0.4 * further) * 1024;
}

/** The gzipped size of a rendered boot script, in bytes, at the level the budget is measured at. */
function gzipped(script: string): number {
  return gzipSync(script, { level: 9 }).length;
}

/** A minimal element: the members the boot page uses. */
class FakeElement {
  readonly children: FakeElement[] = [];
  readonly listeners: Record<string, () => void> = {};
  readonly dataset: Record<string, string> = {};
  lang = '';
  dir = '';
  className = '';
  type = '';
  tabIndex = 0;
  textContent: string | null = '';
  focused = false;
  constructor(readonly tagName: string) {}
  appendChild(child: FakeElement): FakeElement {
    this.children.push(child);
    return child;
  }
  addEventListener(type: string, listener: () => void): void {
    this.listeners[type] = listener;
  }
  focus(): void {
    this.focused = true;
  }
}

/** The globals the boot script reads, faked; the features it gates on are present unless told otherwise. */
function browser(options: { path?: string; cookie?: string; supported?: boolean; loading?: boolean } = {}) {
  const host = new FakeElement('TM-APP');
  const documentListeners: Record<string, () => void> = {};
  const document = {
    documentElement: new FakeElement('HTML'),
    body: new FakeElement('BODY'),
    title: 'fallback',
    cookie: options.cookie ?? '',
    readyState: options.loading ? 'loading' : 'complete',
    createElement: (tag: string) => new FakeElement(tag.toUpperCase()),
    querySelector: (selector: string) => (selector === 'tm-app' ? host : null),
    addEventListener: (type: string, listener: () => void) => (documentListeners[type] = listener),
  };
  const items = new Map<string, string>();
  const storage = {
    getItem: (key: string) => items.get(key) ?? null,
    setItem: (key: string, value: string) => void items.set(key, value),
    removeItem: (key: string) => void items.delete(key),
  };
  const replaced: string[] = [];
  const window: Record<string, unknown> = {
    document,
    location: { pathname: options.path ?? '/17', search: '', hash: '', replace: (url: string) => replaced.push(url) },
    history: { state: null, replaceState: () => undefined },
    navigator: { cookieEnabled: true, languages: ['en-US'], locks: options.supported === false ? undefined : {} },
    localStorage: storage,
    sessionStorage: storage,
    BroadcastChannel: options.supported === false ? undefined : function BroadcastChannel() {},
    indexedDB: options.supported === false ? undefined : {},
    // The web APIs a fresh script context lacks; the language built-ins it has.
    setTimeout,
    atob,
    TextDecoder,
  };
  window['window'] = window;
  return { window, document, host, items, replaced, documentListeners };
}

/** Runs a script as a classic script over the faked globals. */
function run(script: string, globals: Record<string, unknown>): void {
  new Script(script).runInContext(createContext(globals));
}

describe('the boot script template', () => {
  let template: string;

  beforeAll(async () => {
    template = await buildBootTemplate();
  });

  it('is a classic script that opens with the configuration marker, once', () => {
    expect(template.startsWith(bootTemplateBanner)).toBe(true);
    expect(template.split(TM_BOOT_MARKER).length).toBe(2);
    expect(() => new Script(template)).not.toThrow();
    expect(template).not.toMatch(/\bimport\s*[({]|\bexport\s/);
  });

  it('stays within 3 KB gzip with the platform English strings', () => {
    expect(budget(ENGLISH)).toBe(3 * 1024);
    expect(gzipped(tmRenderBootScript(template, ENGLISH))).toBeLessThanOrEqual(budget(ENGLISH));
  });

  it('stays within the budget with each further string table: Acme in English and Arabic', () => {
    expect(budget(ACME)).toBe(3.4 * 1024);
    expect(gzipped(tmRenderBootScript(template, ACME))).toBeLessThanOrEqual(budget(ACME));
  });

  it('counts the English fallback as the base, not as a further table: Arabic only', () => {
    expect(budget(ARABIC)).toBe(3.4 * 1024);
    expect(gzipped(tmRenderBootScript(template, ARABIC))).toBeLessThanOrEqual(budget(ARABIC));
  });

  it('does nothing unrendered', () => {
    const fake = browser();
    run(template, fake.window);
    expect(fake.window['__tmBoot']).toBeUndefined();
    expect(fake.document.title).toBe('fallback');
  });

  it('decides with the real globals: signed in, the app runs', () => {
    const value = Buffer.from(JSON.stringify({ v: 2, sub: '0b4f', sh: 'x', locale: 'ar' })).toString('base64url');
    const fake = browser({ cookie: `tellma.profile=${value}` });
    run(tmRenderBootScript(template, ACME), fake.window);
    expect(fake.window['__tmBoot']).toMatchObject({ state: 'run', cold: true, profile: { sub: '0b4f' } });
    expect([fake.document.documentElement.lang, fake.document.documentElement.dir]).toEqual(['ar', 'rtl']);
    expect(fake.document.documentElement.dataset['theme']).toBe('light');
    expect(fake.document.title).toBe('أكمي');
    expect(JSON.parse(fake.items.get('tm.boot')!)).toMatchObject({ v: 1, lang: 'ar', sub: null });
  });

  it('decides with the real globals: signed out, the tab leaves to sign in', () => {
    const fake = browser({ path: '/17/centers' });
    run(tmRenderBootScript(template, ACME), fake.window);
    expect(fake.window['__tmBoot']).toMatchObject({ state: 'redirect' });
    expect(fake.replaced).toEqual(['/bff/login?returnUrl=%2F17%2Fcenters&ui_locales=en']);
    expect(JSON.parse(fake.items.get('tm.login')!)).toMatchObject({ returnUrl: '/17/centers', auto: false });
  });

  it('shows the unsupported page at DOMContentLoaded in a browser without the gated features', () => {
    const fake = browser({ supported: false, loading: true });
    run(tmRenderBootScript(template, ENGLISH), fake.window);
    expect(fake.window['__tmBoot']).toMatchObject({ state: 'page' });
    expect(fake.host.children).toEqual([]);
    fake.documentListeners['DOMContentLoaded']!();
    const [main] = fake.host.children;
    expect(main?.className).toBe('tm-boot-page');
    expect(main?.children.map((child) => child.textContent)).toEqual([
      'Acme couldn’t start',
      ENGLISH.strings['en']!['Boot_Unsupported'],
      'Try again',
    ]);
    expect(main?.children[0]?.focused).toBe(true);
  });
});

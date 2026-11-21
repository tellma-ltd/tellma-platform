// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmShowBootPage } from './tm-boot-page';
import type { TmBootConfig, TmBootLanguage, TmBootPageReason } from './tm-boot-types';

// The boot page, built in a document of its own: one main landmark with the
// heading, the reason's sentence and Try again, in the boot language with
// English as the fallback of each string.

const CONFIG: TmBootConfig = {
  v: 1,
  languages: [
    { code: 'en', dir: 'ltr' },
    { code: 'ar', dir: 'rtl' },
    { code: 'am', dir: 'ltr' },
  ],
  defaultLanguage: 'en',
  strings: {
    en: {
      Distribution_DisplayName: 'Acme',
      Boot_Title: "{displayName} couldn't start",
      Boot_Unsupported: 'Update your browser to a current version of Chrome, Edge, Firefox or Safari.',
      Boot_Cookies: 'Turn on cookies for this site, then try again.',
      Boot_SignInFailed: "We couldn't sign you in. Try again in a moment.",
      Boot_LoadFailed: 'Check your connection and try again.',
      Boot_TryAgain: 'Try again',
    },
    ar: {
      Distribution_DisplayName: 'أكمي',
      Boot_Title: 'تعذّر تشغيل {displayName}',
      Boot_Cookies: 'فعّل ملفات تعريف الارتباط لهذا الموقع، ثم حاول مرة أخرى.',
      Boot_TryAgain: 'حاول مرة أخرى',
    },
  },
};

const EN: TmBootLanguage = { code: 'en', dir: 'ltr' };
const AR: TmBootLanguage = { code: 'ar', dir: 'rtl' };

/** A fresh document with the app's host element. */
function newDocument(): Document {
  const document = window.document.implementation.createHTMLDocument('Acme');
  document.body.appendChild(document.createElement('tm-app')).textContent = 'loading';
  return document;
}

describe('tmShowBootPage', () => {
  const sentences: Record<TmBootPageReason, string> = {
    unsupported: 'Update your browser to a current version of Chrome, Edge, Firefox or Safari.',
    cookies: 'Turn on cookies for this site, then try again.',
    'sign-in-failed': "We couldn't sign you in. Try again in a moment.",
    'load-failed': 'Check your connection and try again.',
  };
  for (const [reason, sentence] of Object.entries(sentences) as [TmBootPageReason, string][]) {
    it(`shows the ${reason} sentence`, () => {
      const document = newDocument();
      tmShowBootPage(document, CONFIG, EN, 'Acme', reason, () => undefined);
      expect(document.querySelector('p')?.textContent).toBe(sentence);
    });
  }

  it('replaces the host element content with one main landmark', () => {
    const document = newDocument();
    tmShowBootPage(document, CONFIG, EN, 'Acme', 'cookies', () => undefined);
    const host = document.querySelector('tm-app')!;
    expect(host.children.length).toBe(1);
    const main = host.firstElementChild as HTMLElement;
    expect(main.tagName).toBe('MAIN');
    expect(main.className).toBe('tm-boot-page');
    expect([...main.children].map((child) => child.tagName)).toEqual(['H1', 'P', 'BUTTON']);
    expect(main.querySelector('h1')?.textContent).toBe("Acme couldn't start");
  });

  it('focuses the heading, which takes focus without entering the tab order', () => {
    const document = window.document;
    const host = document.body.appendChild(document.createElement('tm-app'));
    try {
      tmShowBootPage(document, CONFIG, EN, 'Acme', 'cookies', () => undefined);
      const heading = host.querySelector('h1')!;
      expect(heading.tabIndex).toBe(-1);
      expect(document.activeElement).toBe(heading);
    } finally {
      host.remove();
    }
  });

  it('runs the retry on Try again, a plain button', () => {
    const document = newDocument();
    const retry = vi.fn();
    tmShowBootPage(document, CONFIG, EN, 'Acme', 'sign-in-failed', retry);
    const button = document.querySelector('button')!;
    expect(button.type).toBe('button');
    expect(button.className).toBe('tm-button tm-button--primary');
    expect(button.textContent).toBe('Try again');
    button.click();
    expect(retry).toHaveBeenCalledTimes(1);
  });

  it('renders in the boot language, and marks an English fallback as English', () => {
    const document = newDocument();
    tmShowBootPage(document, CONFIG, AR, 'أكمي', 'load-failed', () => undefined);
    const main = document.querySelector('main')!;
    expect([main.lang, main.dir]).toEqual(['ar', 'rtl']);
    const heading = main.querySelector('h1')!;
    expect(heading.textContent).toBe('تعذّر تشغيل أكمي');
    expect([heading.lang, heading.dir]).toEqual(['ar', 'rtl']);
    // Arabic lacks the sentence: English stands in, marked as English.
    const sentence = main.querySelector('p')!;
    expect(sentence.textContent).toBe('Check your connection and try again.');
    expect([sentence.lang, sentence.dir]).toEqual(['en', 'ltr']);
    expect(main.querySelector('button')?.textContent).toBe('حاول مرة أخرى');
  });

  it('falls back to the body without a host element, and builds no markup from strings', () => {
    const document = window.document.implementation.createHTMLDocument('Acme');
    const hostile = { ...CONFIG, strings: { en: { ...CONFIG.strings['en'], Boot_Cookies: '<img src=x onerror=alert(1)>' } } };
    tmShowBootPage(document, hostile, EN, '<b>Acme</b>', 'cookies', () => undefined);
    expect(document.body.querySelector('main')).not.toBeNull();
    expect(document.querySelector('img')).toBeNull();
    expect(document.querySelector('b')).toBeNull();
    expect(document.querySelector('p')?.textContent).toBe('<img src=x onerror=alert(1)>');
    expect(document.querySelector('h1')?.textContent).toBe("<b>Acme</b> couldn't start");
  });
});

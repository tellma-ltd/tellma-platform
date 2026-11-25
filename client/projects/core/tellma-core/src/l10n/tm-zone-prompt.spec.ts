// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { tmFormatZoneName, tmFormatZoneOffset } from '@tellma/core-ui/l10n';
import { TmButtonHarness, TmCheckboxHarness, TmModalHarness } from '@tellma/core-ui-testing';
import { tmArabic } from '@tellma/locale-ar';
import axe from 'axe-core';
import { page } from 'vitest/browser';

import { tmEnglish } from './tm-english';
import { TmLocalization } from './tm-localization';
import { TM_T17_DOCS, tmAppRig, tmNavigate, tmSettle, tmSettleUntil } from '../routes/tm-app.testing.util';
import { ɵtmAskZone, type TmZoneAnswer, type TmZoneAsk } from './tm-zone-prompt';

// The device-zone prompt as a user meets it: in English and in Arabic, in
// both themes, on a phone and on a desktop. It names both zones with their
// offsets, describes itself with its message, focuses Change time zone,
// answers Escape as a dismissal, and passes the accessibility rules.

/** The instant the prompt names the zones at. */
const AT = Date.parse('2026-11-24T10:00:00Z');

/** The accessibility rules the platform holds every surface to. */
const WCAG = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'];

/** Starts the test app in tenant 17, in English or in Arabic, and gives the harness loader of the page. */
async function setUp(language: 'en' | 'ar'): Promise<HarnessLoader> {
  const { me, settings } = TM_T17_DOCS;
  await tmAppRig({
    languages: [tmEnglish(), tmArabic()],
    docs: {
      ...TM_T17_DOCS,
      me: { ...me, user: { ...me.user, ...(language === 'ar' && { preferredLanguage: 'ar' }) } },
      settings: { ...settings, languages: [...settings.languages, { code: 'ar', nativeName: 'العربية', symbol: 'ع', isRightToLeft: true }] },
    },
  });
  const harness = await tmNavigate('/17');
  await tmSettleUntil(() => document.documentElement.lang === language);
  return TestbedHarnessEnvironment.documentRootLoader(harness.fixture);
}

/** Opens the prompt, and keeps its answer once it settles. */
function ask(change: TmZoneAsk['change'] = () => Promise.resolve('ok')): { readonly answer: () => TmZoneAnswer | undefined } {
  let answer: TmZoneAnswer | undefined;
  void ɵtmAskZone(TestBed.inject(Injector), { device: 'Europe/London', display: 'Asia/Riyadh', at: AT, change }).then(
    (settled) => (answer = settled),
  );
  return { answer: () => answer };
}

/** The open dialog's element. */
function dialog(): HTMLElement {
  return document.querySelector<HTMLElement>('[role="dialog"]')!;
}

describe('The device-zone prompt', () => {
  afterEach(async () => {
    document.documentElement.removeAttribute('data-theme');
    await page.viewport(1440, 900);
  });

  for (const language of ['en', 'ar'] as const) {
    for (const theme of ['light', 'dark'] as const) {
      for (const width of [390, 1440]) {
        it(`reads, focuses and dismisses as it should, and passes the accessibility rules (${language}, ${theme}, ${width} px)`, async () => {
          await page.viewport(width, 900);
          document.documentElement.setAttribute('data-theme', theme);
          const loader = await setUp(language);
          const prompt = ask();
          await tmSettleUntil(() => document.querySelector('tm-zone-prompt') !== null);
          await tmSettle();

          // --- The title, and the message that names both zones and describes the dialog ---
          const modal = await loader.getHarness(TmModalHarness);
          const l10n = TestBed.inject(TmLocalization);
          const locale = l10n.locale();
          expect(await modal.getTitle()).toBe(language === 'en' ? "Use your device's time zone?" : 'استخدام المنطقة الزمنية لجهازك؟');
          const message = document.getElementById(dialog().getAttribute('aria-describedby')!)!.textContent!;
          expect(message).toContain(tmFormatZoneName('Europe/London', locale, AT));
          expect(message).toContain(tmFormatZoneOffset(AT, locale, 'Europe/London'));
          expect(message).toContain(tmFormatZoneName('Asia/Riyadh', locale, AT));
          expect(message).toContain(tmFormatZoneOffset(AT, locale, 'Asia/Riyadh'));
          expect(message).toContain('Acme');

          // --- The option, described by its hint, and the two answers, Change time zone focused ---
          const checkbox = await loader.getHarness(TmCheckboxHarness);
          expect(await checkbox.isChecked()).toBe(false);
          const input = dialog().querySelector<HTMLInputElement>('tm-checkbox input')!;
          expect(document.getElementById(input.getAttribute('aria-describedby')!.split(' ')[0]!)?.textContent).toBe(
            language === 'en' ? 'Only on this device. You can turn this off in My settings.' : 'على هذا الجهاز فقط. يمكنك إيقاف ذلك من «إعداداتي».',
          );
          const buttons = await loader.getAllHarnesses(TmButtonHarness);
          const labels = await Promise.all(buttons.map((button) => button.getText()));
          expect(labels).toEqual(expect.arrayContaining(language === 'en' ? ['Keep current', 'Change time zone'] : ['الإبقاء على الحالية', 'تغيير المنطقة الزمنية']));
          await tmSettleUntil(() => document.activeElement?.textContent?.trim() === labels[labels.length - 1]);

          // --- The layout: right to left in Arabic, and within the phone's width ---
          expect(getComputedStyle(dialog()).direction).toBe(language === 'ar' ? 'rtl' : 'ltr');
          expect(dialog().getBoundingClientRect().width).toBeLessThanOrEqual(width);
          expect(document.documentElement.scrollWidth).toBeLessThanOrEqual(width);

          // --- The accessibility rules, on the dialog as it shows ---
          const results = await axe.run(dialog(), { runOnly: { type: 'tag', values: WCAG } });
          expect(results.violations.map((violation) => violation.id)).toEqual([]);

          // --- Escape dismisses ---
          dialog().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
          await tmSettleUntil(() => prompt.answer() !== undefined);
          expect(prompt.answer()).toEqual({ answer: 'dismiss', auto: false });
        });
      }
    }
  }

  it('turns Change time zone busy at once, keeps the dialog open meanwhile, and closes once the change lands', async () => {
    const loader = await setUp('en');
    let land!: (outcome: 'ok') => void;
    const ticked: boolean[] = [];
    const prompt = ask((auto) => {
      ticked.push(auto);
      return new Promise((resolve) => (land = resolve));
    });
    await tmSettleUntil(() => document.querySelector('tm-zone-prompt') !== null);
    await (await loader.getHarness(TmCheckboxHarness)).toggle();
    const change = [...dialog().querySelectorAll<HTMLButtonElement>('button')].find((button) => button.textContent?.includes('Change time zone'))!;
    change.click();
    // Busy in the click's frame, before any task runs.
    TestBed.tick();
    expect(change.getAttribute('aria-busy')).toBe('true');
    expect(ticked).toEqual([true]);
    dialog().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    await tmSettle();
    expect(document.querySelector('tm-zone-prompt')).not.toBeNull();
    land('ok');
    await tmSettleUntil(() => prompt.answer() !== undefined);
    expect(prompt.answer()).toEqual({ answer: 'change', auto: true });
  });

  it('follows a language switch while it is open', async () => {
    const loader = await setUp('en');
    ask();
    await tmSettleUntil(() => document.querySelector('tm-zone-prompt') !== null);
    TestBed.inject(TmLocalization).setLanguage('ar');
    await tmSettleUntil(() => dialog().textContent!.includes('تغيير المنطقة الزمنية'));
    expect(await (await loader.getHarness(TmModalHarness)).getTitle()).toBe('استخدام المنطقة الزمنية لجهازك؟');
  });
});

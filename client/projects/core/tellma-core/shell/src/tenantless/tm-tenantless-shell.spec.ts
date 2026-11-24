// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { TestBed } from '@angular/core/testing';
import type { RouterTestingHarness } from '@angular/router/testing';
import { TmLocalization, type TmLanguageDefinition, tmEnglish } from '@tellma/core';
import { TmFakeSession, TmPageContextHarness } from '@tellma/core-testing';
import { TmMenuTriggerHarness } from '@tellma/core-ui-testing';

import { tmAppRig, tmNavigate, tmSettleUntil, tmTestMembership, type TmAppRig, type TmAppRigOptions } from '../../../src/routes/tm-app.testing.util';

// The tenantless shell's app bar, on the picker and the signed-out page:
// the wordmark, the language menu (absent with one language, a switch
// without a request with two), the account menu (My account to the
// identity's account page, returning here in the active language, and Sign
// out), and the signed-out bar, which shows the wordmark alone. Then the
// signed-out page itself, with and without a user signed in elsewhere.

/** Arabic, with the strings the bar reads. */
const ARABIC: TmLanguageDefinition = {
  code: 'ar',
  dir: 'rtl',
  strings: () => Promise.resolve({ tm: {}, tmUi: {} }),
};

/** The account page of the test distribution's identity. */
const ACCOUNT_URL = 'https://id.example.com/account?client_id=acme';

describe('the tenantless shell', () => {
  let rig: TmAppRig;
  let router: RouterTestingHarness;

  afterEach(() => TestBed.resetTestingModule());

  /**
   * Starts the app with one membership and opens a URL, waiting for its page's chunk.
   *
   * @param url - The URL.
   * @param options - The rig's options.
   * @param before - What the test scripts before the page opens.
   */
  async function open(url: string, options: TmAppRigOptions = {}, before: () => void = () => undefined): Promise<void> {
    rig = await tmAppRig({ tenants: [tmTestMembership(17, { name: 'Acme' })], ...options });
    before();
    router = await tmNavigate(url);
    await tmSettleUntil(() => document.querySelector('tm-tenantless-shell h1') !== null, 'the page renders');
  }

  /** The harness loader over the whole document. */
  function loader(): HarnessLoader {
    return TestbedHarnessEnvironment.documentRootLoader(router.fixture);
  }

  /** The bar's menu triggers, in order. */
  function triggers(): Promise<TmMenuTriggerHarness[]> {
    return loader().getAllHarnesses(TmMenuTriggerHarness);
  }

  /** The wordmark's link and its image's text. */
  function wordmark(): { readonly href: string | null; readonly alt: string | null } {
    const link = document.querySelector('.tm-tenantless-shell__wordmark');
    return { href: link?.getAttribute('href') ?? null, alt: link?.querySelector('img')?.getAttribute('alt') ?? null };
  }

  describe('the app bar', () => {
    it('shows the wordmark, linked to the landing, and the account menu, but no language menu with one language', async () => {
      await open('/tenants');
      expect(wordmark()).toEqual({ href: '/', alt: 'Acme' });
      const [account, ...rest] = await triggers();
      expect(rest).toEqual([]);
      // The name over the email; the avatar's initials are drawn below md only.
      const who = [...document.querySelectorAll('.tm-tenantless-shell__who > span')].map((element) => element.textContent);
      expect(who).toEqual(['Test User', 'test.user@example.com']);
      expect(await account.getText()).toContain('Test User');
    });

    it('switches between two languages from the language menu, with no request', async () => {
      await open('/tenants', { languages: [tmEnglish(), ARABIC] });
      const [language] = await triggers();
      expect((await language.getText()).replace(/\s+/g, ' ')).toBe('Language: English');
      await language.open();
      const menu = await language.getMenu();
      expect(await menu.getItemLabels()).toEqual(['English', 'العربية']);
      expect(await menu.isChecked('English')).toBe(true);
      const calls = rig.api.calls().length;
      await menu.clickItem('العربية');
      const l10n = TestBed.inject(TmLocalization);
      await tmSettleUntil(() => l10n.language() === 'ar', 'Arabic is active');
      expect(rig.api.calls()).toHaveLength(calls);
    });

    it("links My account to the identity's account page, returning here in the active language", async () => {
      await open('/tenants', {}, () =>
        rig.api.respond('/api/distribution-info', {
          identity: { authority: 'https://id.example.com', mode: 'Standalone', accountUrl: ACCOUNT_URL },
        }),
      );
      // The picker's read stores the distribution's document beside the memberships.
      await tmSettleUntil(() => rig.api.calls().some((call) => call.route === '/api/distribution-info'), 'the distribution is read');
      const [account] = await triggers();
      await account.open();
      await tmSettleUntil(() => document.querySelector('.tm-menu__panel a[href]') !== null, 'My account shows');
      const menu = await account.getMenu();
      expect(await menu.getItemLabels()).toEqual(['My account', 'Sign out']);
      const href = await menu.getLinkHref('My account');
      expect(href).toBe(`${ACCOUNT_URL}&return_url=${encodeURIComponent(document.location.href)}&ui_locales=en`);
    });

    it('signs out from the account menu', async () => {
      await open('/tenants');
      const [account] = await triggers();
      await account.open();
      await (await account.getMenu()).clickItem('Sign out');
      expect(TestBed.inject(TmFakeSession).signOuts()).toBe(1);
    });

    it('shows the wordmark alone on the signed-out page', async () => {
      await open('/signed-out', { languages: [tmEnglish(), ARABIC] });
      expect(wordmark()).toEqual({ href: '/', alt: 'Acme' });
      expect(await triggers()).toEqual([]);
    });
  });

  describe('the signed-out page', () => {
    /** The page's text, block by block. */
    const blocks = (): string[] =>
      [...document.querySelectorAll('.tm-signed-out-page :is(h1, p, button, a)')].map((element) => element.textContent?.trim() ?? '');

    it('offers Sign in back to the landing and, while a user is signed in elsewhere, Continue', async () => {
      await open('/signed-out');
      expect(blocks()).toEqual([
        'You signed out',
        "You're signed out of Acme on this device.",
        "You're signed in as Test User.",
        'Sign in',
        'Continue',
      ]);
      expect(document.querySelector('.tm-signed-out-page a')?.getAttribute('href')).toBe('/');
      document.querySelector<HTMLButtonElement>('.tm-signed-out-page button')?.click();
      expect(TestBed.inject(TmFakeSession).signIns()).toEqual([{ returnUrl: '/' }]);
    });

    it('offers only Sign in while no user is signed in, and calls no API', async () => {
      rig = await tmAppRig();
      TestBed.inject(TmFakeSession).setProfile(null);
      router = await tmNavigate('/signed-out');
      await tmSettleUntil(() => document.querySelector('tm-signed-out-page h1') !== null, 'the page renders');
      expect(blocks()).toEqual(['You signed out', "You're signed out of Acme on this device.", 'Sign in']);
      expect(rig.api.calls()).toEqual([]);
    });

    it('registers its page context, which says whether a user is signed in', async () => {
      await open('/signed-out');
      expect(TestBed.inject(TmPageContextHarness).current()).toMatchObject({
        kind: 'custom',
        routeTemplate: '/signed-out',
        title: 'You signed out',
        status: 'ready',
        state: { signedIn: true },
      });
    });
  });
});

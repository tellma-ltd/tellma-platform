// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { RouterTestingHarness } from '@angular/router/testing';
import { type TmLanguageDefinition, type TmLaunchDocs, tmEnglish, tmFeature, TmLocalization } from '@tellma/core';
import { TmFakeSession, type TmSessionMenuHarness, TmTenantShellHarness, tmTestShellSlot } from '@tellma/core-testing';
import { type TmBreakpoint, TmBreakpoints, TmTheme } from '@tellma/core-ui';
import { TmModalHarness } from '@tellma/core-ui-testing';
import { tmAvatarTone } from '@tellma/core-ui/initials';

import {
  TM_T17_DOCS,
  tmAppRig,
  tmNavigate,
  tmRouteTestFeature,
  tmSettle,
  tmSettleUntil,
  type TmAppRig,
  type TmAppRigOptions,
} from '../../../src/routes/tm-app.testing.util';

// The session menu through its harness, opened from the tenant shell's
// session button: its header and avatar tone, the tenant card, the rows and
// the conditions that show them (two languages, two calendars, the slots
// below md), and what each row does.

/** Every width class in ascending order. */
const ORDER: readonly TmBreakpoint[] = ['xs', 'sm', 'md', 'lg', 'xl', 'xxl'];

/** The viewport's breakpoints at a fixed width class. */
function breakpoints(widest: TmBreakpoint): Partial<TmBreakpoints> {
  return {
    current: signal(widest).asReadonly(),
    atLeast: (breakpoint: TmBreakpoint) => signal(ORDER.indexOf(widest) >= ORDER.indexOf(breakpoint)).asReadonly(),
  };
}

/** Arabic, offered beside English, with no strings of its own. */
const ARABIC: TmLanguageDefinition = { code: 'ar', dir: 'rtl', strings: () => Promise.resolve({ tm: {}, tmUi: {} }) };

/** Tenant 17 in English and Arabic, on the Gregorian and Umm al-Qura calendars. */
const BILINGUAL: TmLaunchDocs = {
  ...TM_T17_DOCS,
  settings: {
    ...TM_T17_DOCS.settings,
    languages: [
      { code: 'en', nativeName: 'English', symbol: 'En', isRightToLeft: false },
      { code: 'ar', nativeName: 'العربية', symbol: 'ع', isRightToLeft: true },
    ],
    calendars: ['gc', 'uq'],
  },
};

describe('the session menu', () => {
  let rig: TmAppRig;
  let router: RouterTestingHarness;

  afterEach(() => TestBed.resetTestingModule());

  /** Starts the app at a width class, opens tenant 17 and its session menu. */
  async function open(options: TmAppRigOptions = {}, widest: TmBreakpoint = 'xl'): Promise<TmSessionMenuHarness> {
    rig = await tmAppRig({ ...options, providers: [{ provide: TmBreakpoints, useValue: breakpoints(widest) }, ...(options.providers ?? [])] });
    router = await tmNavigate('/17');
    await tmSettleUntil(() => document.querySelector('tm-tenant-shell tm-app-bar') !== null, 'the tenant shell renders');
    const shell = await TestbedHarnessEnvironment.documentRootLoader(router.fixture).getHarness(TmTenantShellHarness);
    return shell.openSessionMenu();
  }

  it('shows who the user is, with the avatar on the tone of the user record key', async () => {
    const menu = await open();
    expect(await menu.getUserName()).toBe('Farah');
    expect(await menu.getEmail()).toBe(TestBed.inject(TmFakeSession).profile()?.email);
    expect(await menu.getAvatarTone()).toBe(tmAvatarTone('core.User:42'));
  });

  it('lists the tenant card, My settings, the theme, the shortcuts, About and Sign out with one language and one calendar', async () => {
    const menu = await open();
    expect(await menu.getItemLabels()).toEqual(['Acme', 'My settings', 'System', 'Light', 'Dark', 'Keyboard shortcuts', 'About', 'Sign out']);
    expect(await menu.getTenantCard()).toEqual({ label: 'Acme', description: 'Tenant 17', detail: 'Switch' });
    expect(await menu.getTenantCardName()).toBe('Acme, Switch tenant');
    expect(await menu.getLinkHref('My settings')).toBe('/17/me');
    const [theme] = await menu.getGroups();
    expect(await theme.getLabel()).toBe('Theme');
    expect(await menu.isChecked(TestBed.inject(TmTheme).setting() === 'system' ? 'System' : 'Light')).toBe(true);
  });

  it("adds the markers to the tenant card's second line", async () => {
    const menu = await open({ docs: { ...TM_T17_DOCS, tenant: { ...TM_T17_DOCS.tenant, category: 'Sandbox', liveTenantId: 2, state: 'Frozen' } } });
    expect((await menu.getTenantCard()).description).toBe('Tenant 17 · Sandbox · Under maintenance');
  });

  it('offers the language and the calendar with two of each, by their native names and labels', async () => {
    const menu = await open({
      docs: BILINGUAL,
      languages: [tmEnglish(), ARABIC],
      strings: { en: { Calendar_gc: 'Gregorian', Calendar_uq: 'Umm al-Qura' } },
    });
    expect(await menu.getItemLabels()).toEqual(['Acme', 'My settings', 'Language', 'Calendar', 'System', 'Light', 'Dark', 'Keyboard shortcuts', 'About', 'Sign out']);
    const language = await menu.openSubmenu('Language');
    expect(await language.getItemLabels()).toEqual(['English', 'العربية']);
    expect(await language.isChecked('English')).toBe(true);
    const calendar = await menu.openSubmenu('Calendar');
    expect(await calendar.getItemLabels()).toEqual(['Gregorian', 'Umm al-Qura']);
    await calendar.clickItem('Umm al-Qura');
    await tmSettleUntil(() => TestBed.inject(TmLocalization).calendarCode() === 'uq', 'the calendar switches');
  });

  it('lists the slots below md, each with its count, and opens a slot in a drawer', async () => {
    const pending = signal<number | null>(4);
    const feature = tmFeature({ ...tmRouteTestFeature(), appBar: [tmTestShellSlot('acme-approvals', { labelKey: 'Acme_Approvals', count: pending })] });
    const menu = await open({ features: [feature], strings: { en: { Acme_Approvals: 'Approvals' } } }, 'sm');
    expect(await menu.getItemLabels()).toEqual(['Acme', 'My settings', 'System', 'Light', 'Dark', 'Keyboard shortcuts', 'About', 'Approvals', 'Sign out']);
    const [row] = (await menu.getItems()).slice(-2);
    expect(await row.getDetail()).toBe('4');
    await row.click();
    await tmSettleUntil(() => document.querySelector('tm-modal-shell .tm-test-shell-slot-panel') !== null, 'the drawer shows the panel');
    const drawer = await TestbedHarnessEnvironment.documentRootLoader(router.fixture).getHarness(TmModalHarness);
    expect([await drawer.getTitle(), await drawer.getPlacement()]).toEqual(['Approvals', 'end']);
  });

  it('switches tenants from the tenant card', async () => {
    const menu = await open();
    await menu.clickItem('Acme');
    await tmSettleUntil(() => rig.router.url === '/tenants', 'the picker opens');
  });

  it('signs out from Sign out', async () => {
    const menu = await open();
    await menu.clickItem('Sign out');
    await tmSettle();
    expect(TestBed.inject(TmFakeSession).signOuts()).toBe(1);
  });

  it('opens About and the shortcuts as dialogs', async () => {
    const menu = await open();
    await menu.clickItem('About');
    await tmSettleUntil(() => document.querySelector('tm-modal-shell tm-about-dialog') !== null, 'About renders');
    const about = await TestbedHarnessEnvironment.documentRootLoader(router.fixture).getHarness(TmModalHarness);
    expect(await about.getTitle()).toBe('About');
    await about.close();
    await tmSettleUntil(() => document.querySelector('tm-modal-shell') === null, 'About closes');
    const shell = await TestbedHarnessEnvironment.documentRootLoader(router.fixture).getHarness(TmTenantShellHarness);
    await (await shell.openSessionMenu()).clickItem('Keyboard shortcuts');
    await tmSettleUntil(() => document.querySelector('tm-modal-shell tm-shortcuts-dialog') !== null, 'the shortcuts render');
    expect(await (await TestbedHarnessEnvironment.documentRootLoader(router.fixture).getHarness(TmModalHarness)).getTitle()).toBe('Keyboard shortcuts');
  });
});

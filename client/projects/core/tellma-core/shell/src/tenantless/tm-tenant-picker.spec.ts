// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import type { RouterTestingHarness } from '@angular/router/testing';
import { TmApiError, TmClientStore, TmLaunch, TmUrls, type TmMembership } from '@tellma/core';
import { TmFakeSession, TmPageContextHarness, TmTenantPickerHarness, type TmTenantPickerRowHarness } from '@tellma/core-testing';
import { tmIsApplePlatform } from '@tellma/core-ui/kbd';

import {
  tmAppRig,
  tmNavigate,
  tmSettle,
  tmSettleUntil,
  tmTestMembership,
  type TmAppRig,
  type TmAppRigOptions,
} from '../../../src/routes/tm-app.testing.util';

// The tenant picker in a whole test app, through its harness: the stored
// list in the first frame, the order, the names, the badges and Connect;
// the search from seven memberships, with `?q=`, Enter and Mod+K; the
// skeleton, error, empty and no-match states; the tenant-not-found notice
// after a chosen entry and none after an automatic one; and the page context
// and telemetry. The page lives in a lazily loaded chunk, so each wait is on
// the state it produces, never on a count of turns.

/** Tenant 17, warm in the rig, in English and Arabic. */
const ACME = tmTestMembership(17, { name: 'Acme', name2: 'أكمي', languages: ['en', 'ar'] });

/** Tenant 17's sandbox. */
const ACME_SANDBOX = tmTestMembership(18, { name: 'Acme Training', category: 'Sandbox', liveTenantId: 17 });

/** Seven memberships, from which the search shows. */
const SEVEN: readonly TmMembership[] = [
  ACME,
  ACME_SANDBOX,
  tmTestMembership(3, { name: 'Zeta' }),
  tmTestMembership(4, { name: 'Beta' }),
  tmTestMembership(5, { name: 'Gamma', state: 'Suspended' }),
  tmTestMembership(6, { name: 'Gamma Two' }),
  tmTestMembership(7, { name: 'Delta' }),
];

describe('the tenant picker', () => {
  let rig: TmAppRig;
  let router: RouterTestingHarness;

  afterEach(() => TestBed.resetTestingModule());

  /** Starts the app. */
  async function start(options: TmAppRigOptions = {}): Promise<void> {
    rig = await tmAppRig(options);
  }

  /** Presses Mod+K on the page: ⌘K on an Apple platform, Ctrl+K elsewhere. */
  function modK(): KeyboardEvent {
    const apple = tmIsApplePlatform();
    const key = new KeyboardEvent('keydown', { key: 'k', code: 'KeyK', ctrlKey: !apple, metaKey: apple, bubbles: true, cancelable: true });
    document.body.dispatchEvent(key);
    return key;
  }

  /** Opens a URL and waits for the picker's chunk to render it. */
  async function open(url = '/tenants'): Promise<TmTenantPickerHarness> {
    router = await tmNavigate(url);
    await tmSettleUntil(() => document.querySelector('tm-tenant-picker h1') !== null, 'the picker renders');
    return loader().getHarness(TmTenantPickerHarness);
  }

  /** The harness loader over the whole document. */
  function loader(): HarnessLoader {
    return TestbedHarnessEnvironment.documentRootLoader(router.fixture);
  }

  /** The shown names of rows, in order. */
  async function names(rows: readonly TmTenantPickerRowHarness[]): Promise<string[]> {
    return Promise.all(rows.map(async (row) => (await row.getName()).text));
  }

  /** The routes the API was called on. */
  const routes = (): string[] => rig.api.calls().map((call) => call.route);

  it('renders the stored list in the first frame, and reads it again only after that frame renders', async () => {
    await start({ tenants: [ACME] });
    // The read records whether the list was on screen when it went out, and
    // stays unanswered, so the list on screen is the stored one.
    const rendered: boolean[] = [];
    rig.api.respond('/bff/user', () => {
      rendered.push(document.querySelector('tm-tenant-picker .tm-tenant-picker__name') !== null);
      return new Promise(() => undefined);
    });
    router = await tmNavigate('/tenants');
    await tmSettleUntil(() => document.querySelector('tm-tenant-picker .tm-tenant-picker__name') !== null, 'the row renders');
    const picker = await loader().getHarness(TmTenantPickerHarness);
    expect(await picker.getTitle()).toBe('Choose a tenant');
    expect(await names(await picker.getRows())).toEqual(['Acme']);
    expect(await picker.isLoading()).toBe(false);
    await tmSettleUntil(() => rendered.length > 0, 'the memberships are read');
    expect(rendered).toEqual([true]);
    expect(TestBed.inject(Title).getTitle()).toBe('Tenants · Acme');
  });

  it('lists live tenants by name with their sandboxes indented beneath, then the unattached sandboxes', async () => {
    await start({
      tenants: [
        tmTestMembership(9, { name: 'Alpha Orphan', category: 'Sandbox', liveTenantId: 99 }),
        tmTestMembership(3, { name: 'Zeta' }),
        ACME_SANDBOX,
        ACME,
        tmTestMembership(5, { name: 'Old', state: 'Retired' }),
      ],
    });
    const rows = await (await open()).getRows();
    expect(await names(rows)).toEqual(['Acme', 'Acme Training', 'Zeta', 'Alpha Orphan']);
    expect(await Promise.all(rows.map((row) => row.isNested()))).toEqual([false, true, false, false]);
  });

  it('shows the name in the active language in bold and the other names, each with its language, then the id', async () => {
    await start({ tenants: [ACME] });
    const [row] = await (await open()).getRows();
    expect(await row.getName()).toEqual({ text: 'Acme', lang: 'en' });
    expect(await row.getOtherNames()).toEqual([{ text: 'أكمي', lang: 'ar' }]);
    expect(await row.getSecondLine()).toBe('أكمي · Tenant 17');
  });

  it('gives each row its badges, and Connect a link to the tenant home or a reason it is unavailable', async () => {
    await start({
      tenants: [
        ACME,
        ACME_SANDBOX,
        tmTestMembership(2, { name: 'Read', state: 'ReadOnly' }),
        tmTestMembership(3, { name: 'Maintenance', state: 'Frozen' }),
        tmTestMembership(4, { name: 'Stopped', state: 'Suspended' }),
        tmTestMembership(5, { name: 'New', state: 'Provisioning' }),
        tmTestMembership(6, { name: 'Removed', isActive: false }),
      ],
    });
    TestBed.inject(TmClientStore).boot.patch({ lastTenantId: 17 });
    const rows = await (await open()).getRows();
    const seen = await Promise.all(
      rows.map(async (row) => [(await row.getName()).text, await row.getBadges(), await row.getConnectHref(), await row.getUnavailableReason()]),
    );
    expect(seen).toEqual([
      ['Acme', ['Last used'], '/17', null],
      ['Acme Training', ['Sandbox'], '/18', null],
      ['Maintenance', ['Under maintenance'], '/3', null],
      ['New', ['Being set up'], null, 'This tenant is being set up.'],
      ['Read', ['Read-only'], '/2', null],
      ['Removed', ['Deactivated'], null, 'Your access to this tenant was removed.'],
      ['Stopped', ['Suspended'], null, 'Suspended. Contact your administrator.'],
    ]);
    expect(await rows[0].getConnectName()).toBe('Connect to Acme');
  });

  it("follows Connect in the app to the tenant's home, and preloads it on hover", async () => {
    await start({ tenants: [ACME] });
    const preload = vi.spyOn(TestBed.inject(TmUrls), 'preload');
    const [row] = await (await open()).getRows();
    await row.hoverConnect();
    expect(preload).toHaveBeenCalledWith('/17');
    await row.connect();
    await tmSettleUntil(() => rig.router.url === '/17', 'the tenant home is routed');
  });

  it('shows four skeleton rows while nothing is stored, then the list the read answers', async () => {
    await start();
    let answer: (value: unknown) => void = () => undefined;
    rig.api.respond('/bff/user', () => new Promise((resolve) => (answer = resolve)));
    const picker = await open();
    expect(await picker.isLoading()).toBe(true);
    expect(document.querySelectorAll('tm-tenant-picker [aria-busy="true"] > li')).toHaveLength(4);
    await tmSettleUntil(() => routes().includes('/bff/user'), 'the memberships are read');
    answer({ tenants: [ACME] });
    await tmSettleUntil(() => document.querySelector('.tm-tenant-picker__name') !== null, 'the list renders');
    expect(await names(await picker.getRows())).toEqual(['Acme']);
  });

  it('shows the error state when the read fails with nothing stored, and reads again on Try again', async () => {
    await start();
    rig.api.respond('/bff/user', () => {
      throw new TmApiError({ kind: 'offline' });
    });
    const picker = await open();
    await tmSettleUntil(() => document.querySelector('tm-tenant-picker tm-error-state') !== null, 'the error state renders');
    expect(await picker.getError()).toContain('offline');
    expect(TestBed.inject(TmPageContextHarness).current()?.status).toBe('error');
    rig.api.respond('/bff/user', { tenants: [ACME] });
    await picker.clickErrorAction('Try again');
    await tmSettleUntil(() => document.querySelector('.tm-tenant-picker__name') !== null, 'the list renders');
    expect(await picker.getError()).toBeNull();
  });

  it('keeps the stored list when the read fails, offline or on the server', async () => {
    await start({ tenants: [ACME] });
    rig.api.respond('/bff/user', () => {
      throw new TmApiError({ kind: 'offline' });
    });
    rig.api.respond('/api/distribution-info', () => {
      throw new TmApiError({ kind: 'server', status: 500 });
    });
    const picker = await open();
    await tmSettleUntil(() => rig.api.calls().some((call) => call.route === '/bff/user'), 'the memberships are read');
    await tmSettle();
    expect(await names(await picker.getRows())).toEqual(['Acme']);
    expect(await picker.getError()).toBeNull();
  });

  it('shows the empty state of a user without memberships, with Sign out', async () => {
    await start({ tenants: [] });
    const picker = await open();
    expect(await picker.getEmptyState()).toEqual({
      heading: "You're not a member of any tenant yet",
      message: "Ask your administrator to invite you. You're signed in as test.user@example.com.",
    });
    await picker.signOutFromEmptyState();
    expect(TestBed.inject(TmFakeSession).signOuts()).toBe(1);
  });

  describe('the search', () => {
    it('shows from seven memberships only', async () => {
      await start({ tenants: SEVEN.slice(0, 6) });
      expect(await (await open()).hasSearch()).toBe(false);
      TestBed.resetTestingModule();
      await start({ tenants: SEVEN });
      expect(await (await open()).hasSearch()).toBe(true);
    });

    it('filters on every keystroke and keeps the text in ?q= in place of the entry', async () => {
      await start({ tenants: SEVEN });
      const picker = await open();
      await picker.search('gam');
      expect(await names(await picker.getRows())).toEqual(['Gamma', 'Gamma Two']);
      await tmSettleUntil(() => rig.router.url === '/tenants?q=gam', 'the text is in the URL');
      await picker.search('17');
      expect(await names(await picker.getRows())).toEqual(['Acme']);
    });

    it('starts from ?q=', async () => {
      await start({ tenants: SEVEN });
      const picker = await open('/tenants?q=zeta');
      expect(await picker.getSearchText()).toBe('zeta');
      expect(await names(await picker.getRows())).toEqual(['Zeta']);
    });

    it("keeps a matching sandbox's live tenant as a dimmed row", async () => {
      await start({ tenants: SEVEN });
      const picker = await open();
      await picker.search('training');
      const rows = await picker.getRows();
      expect(await names(rows)).toEqual(['Acme', 'Acme Training']);
      expect(await Promise.all(rows.map((row) => row.isDimmed()))).toEqual([true, false]);
    });

    it('says when nothing matches', async () => {
      await start({ tenants: SEVEN });
      const picker = await open();
      await picker.search('nothing');
      expect(await picker.getRows()).toEqual([]);
      expect(await picker.getNoMatch()).toBe('No tenants match “nothing”.');
    });

    it('follows the only enabled match on Enter, and nothing while two match', async () => {
      await start({ tenants: SEVEN });
      const picker = await open();
      // Gamma is suspended, so Gamma Two is the only enabled match.
      await picker.search('gamma');
      await picker.submitSearch();
      await tmSettleUntil(() => rig.router.url === '/6', 'the only enabled match is followed');
      TestBed.resetTestingModule();
      await start({ tenants: SEVEN });
      const again = await open();
      await again.search('acme');
      await again.submitSearch();
      await tmSettle();
      expect(rig.router.url.startsWith('/tenants')).toBe(true);
    });

    it('takes Mod+K to focus the search', async () => {
      await start({ tenants: SEVEN });
      const picker = await open();
      expect(await picker.isSearchFocused()).toBe(false);
      const key = modK();
      expect(key.defaultPrevented).toBe(true);
      expect(await picker.isSearchFocused()).toBe(true);
    });

    it('leaves Mod+K to the browser while the search is hidden', async () => {
      await start({ tenants: [ACME] });
      await open();
      const key = modK();
      expect(key.defaultPrevented).toBe(false);
    });
  });

  describe('the tenant-not-found notice', () => {
    /** Presents tenant 17's not-found refusal while tenant 17 is routed. */
    async function refuse(): Promise<void> {
      const { TmErrorPresenter } = await import('@tellma/core/errors');
      await TestBed.inject(TmErrorPresenter).present(new TmApiError({ kind: 'tenant-not-found', status: 404, code: 'tenant-not-found' }), {
        kind: 'read',
      });
      await tmSettleUntil(() => document.querySelector('tm-tenant-picker h1') !== null, 'the picker renders');
    }

    it('names the tenant after an entry the user chose, and drops both parameters on dismiss', async () => {
      await start({ tenants: [ACME, ACME_SANDBOX] });
      router = await tmNavigate('/17');
      await refuse();
      expect(rig.router.url).toBe('/tenants?notice=tenant-not-found&from=17');
      const picker = await loader().getHarness(TmTenantPickerHarness);
      expect(await picker.getNotice()).toContain('You can no longer open Acme.');
      expect(rig.telemetry.recent().filter((item) => item.name === 'tm.shell.picker-shown').map((item) => item.props)).toEqual([
        { count: '2-6', notice: true },
      ]);
      await picker.dismissNotice();
      await tmSettleUntil(() => rig.router.url === '/tenants', 'the notice leaves the URL');
      expect(await picker.getNotice()).toBeNull();
    });

    it('shows none after an automatic entry', async () => {
      await start({ tenants: [ACME, ACME_SANDBOX] });
      TestBed.inject(TmClientStore).boot.patch({ lastTenantId: null });
      router = await tmNavigate('/');
      await tmSettleUntil(() => rig.router.url === '/17', 'the launch enters the one live tenant');
      expect(TestBed.inject(TmLaunch).automatic()).toBe(17);
      await refuse();
      expect(rig.router.url).toBe('/tenants');
      expect(await (await loader().getHarness(TmTenantPickerHarness)).getNotice()).toBeNull();
    });

    it('names the tenant by its id when the list no longer holds it', async () => {
      await start({ tenants: [ACME] });
      const picker = await open('/tenants?notice=tenant-not-found&from=40');
      expect(await picker.getNotice()).toContain('You can no longer open tenant 40.');
    });
  });

  it('registers its page context: the title, the count and query, and the rows it shows', async () => {
    await start({ tenants: SEVEN });
    const picker = await open('/tenants?q=gamma');
    expect(await picker.getRows()).toHaveLength(2);
    const context = TestBed.inject(TmPageContextHarness).current();
    expect(context).toMatchObject({
      kind: 'custom',
      routeTemplate: '/tenants',
      title: 'Choose a tenant',
      state: { count: 7, query: 'gamma' },
    });
    expect(context?.visible).toEqual({
      columns: ['Name', 'Id'],
      rowCount: 2,
      rows: [
        { id: 5, cells: { Name: 'Gamma', Id: 5 } },
        { id: 6, cells: { Name: 'Gamma Two', Id: 6 } },
      ],
      total: 2,
    });
  });

  it('reports that it was shown once, with the count bucket and no notice', async () => {
    await start({ tenants: SEVEN });
    const picker = await open();
    await picker.search('gamma');
    expect(rig.telemetry.recent().filter((item) => item.name === 'tm.shell.picker-shown').map((item) => item.props)).toEqual([
      { count: '7+', notice: false },
    ]);
  });
});

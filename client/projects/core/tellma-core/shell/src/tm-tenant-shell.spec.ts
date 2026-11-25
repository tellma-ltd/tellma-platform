// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { ChangeDetectionStrategy, Component, input, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { RouterTestingHarness } from '@angular/router/testing';
import { type TmLaunchDocs, TmApiError, TmShellMain, tmFeature } from '@tellma/core';
import { TmFakeSession, TmTenantShellHarness, tmTestShellSlot, tmWarmTenant } from '@tellma/core-testing';
import { tmAvatarTone } from '@tellma/core-ui/initials';

import {
  TM_COLD_SYNC,
  TM_T17,
  TM_T17_DOCS,
  tmAppRig,
  tmNavigate,
  tmRouteTestFeature,
  tmSettle,
  tmSettleUntil,
  tmTestMembership,
  type TmAppRig,
  type TmAppRigOptions,
} from '../../src/routes/tm-app.testing.util';
import { TM_HISTORY_HOST } from './app-bar/tm-installed-mode';
import { TM_SHELL_PANELS_LOAD } from './app-bar/tm-shell-overlays';
import { TmLauncher } from './launcher/tm-launcher';
import { TM_SHELL_ERROR_VIEWS } from './tm-shell-errors';

// The tenant shell in a whole test app, through its harness: each of its
// states (warm, a cold sync pending, failed, client-outdated, a tenant that
// is gone or unavailable, a replaced main), the app bar's names, avatar
// tone, markers with their popovers, slots, reserved boxes, session button
// keys and installed-mode history (through the Navigation API or the
// router), Mod+/, the overlays' chunk failing to load, and a tenant switch,
// which closes the shell's overlays and clears the launcher's text. The
// overlays and the history buttons live in lazily loaded chunks, so each
// wait is on the state it produces.

/** What replaces the route host in the replaced state. */
@Component({
  selector: 'tm-test-tenant-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<p class="tm-test-tenant-state">Unavailable: {{ reason() }}</p>`,
})
class TmTestTenantState {
  /** Why the tenant is unavailable. */
  readonly reason = input('');
}

/** A history move's result whose promises reject, as a move with no entry that way does. */
function refusedMove(): NavigationResult {
  const refused = (): Promise<never> => Promise.reject(new DOMException('No entry', 'InvalidStateError'));
  return { committed: refused(), finished: refused() } as NavigationResult;
}

/** The Navigation API, answering from fields a test sets. */
class TmFakeNavigation extends EventTarget {
  /** Whether Back has an entry. */
  canGoBack = false;
  /** Whether Forward has an entry. */
  canGoForward = false;
  /** The moves asked for. */
  readonly moves: string[] = [];

  /** Asks for a move back. */
  back(): NavigationResult {
    this.moves.push('back');
    return refusedMove();
  }

  /** Asks for a move forward. */
  forward(): NavigationResult {
    this.moves.push('forward');
    return refusedMove();
  }

  /** Changes the entry, as a navigation does. */
  enter(canGoBack: boolean, canGoForward: boolean): void {
    this.canGoBack = canGoBack;
    this.canGoForward = canGoForward;
    this.dispatchEvent(new Event('currententrychange'));
  }
}

/** A display-mode query a test switches. */
class TmFakeDisplayMode extends EventTarget {
  /** Whether the mode matches. */
  matches: boolean;

  /** Creates the query in a mode. */
  constructor(matches: boolean) {
    super();
    this.matches = matches;
  }

  /** Switches the mode, as installing the app or opening it in a tab does. */
  set(matches: boolean): void {
    this.matches = matches;
    this.dispatchEvent(new Event('change'));
  }
}

/** Presses a key combination on the page. */
function press(init: KeyboardEventInit, target: EventTarget = document.body): void {
  target.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, cancelable: true, ...init }));
}

/** Tenant 17's documents with other facts. */
function docsWith(tenant: Partial<TmLaunchDocs['tenant']>): TmLaunchDocs {
  return { ...TM_T17_DOCS, tenant: { ...TM_T17_DOCS.tenant, ...tenant } };
}

describe('the tenant shell', () => {
  let rig: TmAppRig;
  let router: RouterTestingHarness;

  afterEach(() => TestBed.resetTestingModule());

  /** Starts the app. */
  async function start(options: TmAppRigOptions = {}): Promise<void> {
    rig = await tmAppRig(options);
  }

  /** Opens a tenant URL and waits for the shell's app bar. */
  async function open(url = '/17'): Promise<TmTenantShellHarness> {
    router = await tmNavigate(url);
    await tmSettleUntil(() => document.querySelector('tm-tenant-shell tm-app-bar') !== null, 'the tenant shell renders');
    return TestbedHarnessEnvironment.documentRootLoader(router.fixture).getHarness(TmTenantShellHarness);
  }

  /** The routes the API was called on. */
  const routes = (): string[] => rig.api.calls().map((call) => call.route);

  describe('warm', () => {
    it('renders the app bar complete from the documents, with no request before its first frame', async () => {
      await start();
      // Records the calls made when the bar first shows the tenant's name.
      let calls: string[] | null = null;
      const observer = new MutationObserver(() => {
        if (calls === null && document.querySelector('tm-app-bar .tm-app-bar__tenant') !== null) {
          calls = routes();
        }
      });
      observer.observe(document.body, { childList: true, subtree: true });
      const shell = await open();
      observer.disconnect();
      expect(calls).toEqual([]);
      expect(await shell.getMain()).toBe('page');
      expect(await shell.getTenantName()).toBe('Acme');
      expect(await shell.getUserName()).toBe('Farah');
      expect(await shell.getAccountName()).toBe('Account: Farah, Acme');
      expect(await shell.getHomeHref()).toBe('/17');
      expect(await shell.getMarkers()).toEqual([]);
      expect(await shell.getAvatarTone()).toBe(tmAvatarTone('core.User:42'));
    });

    it('reserves the boxes of the bell, the tray, help and the assistant', async () => {
      await start();
      const shell = await open();
      expect(await shell.getReservedSlots()).toEqual(['bell', 'tray', 'help', 'assistant']);
      const [bell, tray, help, assistant] = Array.from(document.querySelectorAll<HTMLElement>('tm-app-bar span[data-tm-slot]'));
      expect([bell.offsetWidth, bell.offsetHeight, tray.offsetWidth, tray.offsetHeight]).toEqual([30, 30, 30, 30]);
      expect([help.offsetWidth, assistant.offsetWidth]).toEqual([0, 0]);
    });
  });

  describe('cold', () => {
    it("shows the profile's name and a skeleton for the tenant's while the sync runs, then the documents' names", async () => {
      await start({ cold: true });
      let answer: (value: unknown) => void = () => undefined;
      rig.api.respond('client/sync', () => new Promise((resolve) => (answer = resolve)));
      const shell = await open();
      expect(await shell.getUserName()).toBe(TestBed.inject(TmFakeSession).profile()?.name);
      expect(await shell.isTenantNameLoading()).toBe(true);
      expect(await shell.getTenantName()).toBeNull();
      expect(await shell.getAccountName()).toBe(`Account: ${TestBed.inject(TmFakeSession).profile()?.name}`);
      expect(await shell.getMarkers()).toEqual([]);
      expect(await shell.getAvatarTone()).toBeNull();
      expect(await shell.getMain()).toBe('page');
      answer(TM_COLD_SYNC);
      await tmSettleUntil(() => document.querySelector('tm-app-bar .tm-app-bar__tenant') !== null, 'the tenant name renders');
      expect(await shell.getTenantName()).toBe('Acme');
      expect(await shell.getUserName()).toBe('Farah');
    });

    it('shows the error state of a failed sync with Retry, which runs the sync again', async () => {
      await start({ cold: true });
      rig.api.respond('client/sync', () => {
        throw new TmApiError({ kind: 'server', status: 500 });
      });
      const shell = await open();
      await tmSettleUntil(() => document.querySelector('tm-tenant-shell main tm-error-state') !== null, 'the error state renders');
      expect(await shell.getMain()).toBe('error');
      expect(await shell.isTenantNameLoading()).toBe(true);
      rig.api.respond('client/sync', TM_COLD_SYNC);
      await shell.clickErrorAction('Try again');
      await tmSettleUntil(() => document.querySelector('tm-tenant-shell main tm-error-state') === null, 'the error state ends');
      expect(await shell.getMain()).toBe('page');
      expect(await shell.getTenantName()).toBe('Acme');
    });

    it("shows the client-outdated view's error state for a sync the server refused as outdated", async () => {
      await start({ cold: true });
      rig.api.respond('client/sync', () => {
        throw new TmApiError({ kind: 'client-outdated', status: 412, code: 'client-outdated' });
      });
      const shell = await open();
      await tmSettleUntil(() => document.querySelector('tm-tenant-shell main tm-error-state') !== null, 'the error state renders');
      expect(await shell.getMain()).toBe('error');
      expect(await shell.getError()).toContain('Reload');
    });

    it('says the sync failed, with Try again, when the error views cannot load', async () => {
      const missing = new TypeError('Failed to fetch dynamically imported module');
      await start({ cold: true, providers: [{ provide: TM_SHELL_ERROR_VIEWS, useValue: () => Promise.reject(missing) }] });
      rig.api.respond('client/sync', () => {
        throw new TmApiError({ kind: 'offline' });
      });
      const shell = await open();
      await tmSettleUntil(() => document.querySelector('.tm-tenant-shell__failed') !== null, 'the failure line renders');
      expect(await shell.getError()).toBe("Couldn't load this tenant. Try again");
      expect(rig.telemetry.errors()).toEqual([missing]);
      rig.api.respond('client/sync', TM_COLD_SYNC);
      await shell.clickErrorAction('Try again');
      await tmSettleUntil(() => document.querySelector('.tm-tenant-shell__failed') === null, 'the failure line ends');
      expect(await shell.getMain()).toBe('page');
    });

    it('says the sync failed, with Try again in place of the route host, for a failure that is not an API error', async () => {
      await start({ cold: true });
      const failure = new TypeError('Failed to fetch');
      rig.api.respond('client/sync', () => Promise.reject(failure));
      const shell = await open();
      await tmSettleUntil(() => document.querySelector('.tm-tenant-shell__failed') !== null, 'the failure line renders');
      expect(await shell.getMain()).toBe('error');
      expect(await shell.getError()).toBe("Couldn't load this tenant. Try again");
      expect(document.querySelector('tm-tenant-shell main router-outlet')).toBeNull();
      expect(document.querySelector('tm-tenant-shell main tm-error-state')).toBeNull();
      expect(rig.telemetry.errors()).toEqual([failure]);
      rig.api.respond('client/sync', TM_COLD_SYNC);
      await shell.clickErrorAction('Try again');
      await tmSettleUntil(() => document.querySelector('.tm-tenant-shell__failed') === null, 'the failure line ends');
      expect(await shell.getMain()).toBe('page');
      expect(await shell.getTenantName()).toBe('Acme');
    });

    it('leaves a tenant that is gone to the error presenter, which opens the picker with its notice', async () => {
      await start({ cold: true });
      rig.api.respond('client/sync', () => {
        throw new TmApiError({ kind: 'tenant-not-found', status: 404, code: 'tenant-not-found' });
      });
      router = await tmNavigate('/17');
      await tmSettleUntil(() => rig.router.url.startsWith('/tenants'), 'the picker opens');
      expect(document.querySelector('.tm-tenant-shell__error')).toBeNull();
    });

    it("leaves an unavailable tenant to the error presenter, whose tenant-state page replaces the route host", async () => {
      await start({ cold: true });
      rig.api.respond('client/sync', () => {
        throw new TmApiError({ kind: 'tenant-unavailable', status: 403, code: 'tenant-suspended' });
      });
      const shell = await open();
      await tmSettleUntil(() => document.querySelector('tm-tenant-shell main tm-error-state') !== null, 'the tenant-state page renders');
      expect(await shell.getMain()).toBe('replaced');
      expect(await shell.getError()).toBeNull();
    });
  });

  it('shows what replaces the route host in main, under the same app bar', async () => {
    await start();
    const shell = await open();
    TestBed.inject(TmShellMain).replace(TmTestTenantState, { reason: 'suspended' });
    await tmSettleUntil(() => document.querySelector('.tm-test-tenant-state') !== null, 'the replacement renders');
    expect(await shell.getMain()).toBe('replaced');
    expect(document.querySelector('tm-tenant-shell main .tm-test-tenant-state')?.textContent).toBe('Unavailable: suspended');
    expect(await shell.getTenantName()).toBe('Acme');
  });

  describe('markers', () => {
    it('shows none for a live, active tenant, and the sandbox and the state for the others, in the account name too', async () => {
      await start({ docs: docsWith({ category: 'Sandbox', liveTenantId: 2, state: 'ReadOnly' }) });
      const shell = await open();
      expect(await shell.getMarkers()).toEqual(['Sandbox', 'Read-only']);
      expect(await shell.getAccountName()).toBe('Account: Farah, Acme, sandbox, read-only');
    });

    it('names maintenance for a frozen tenant', async () => {
      await start({ docs: docsWith({ state: 'Frozen' }) });
      const shell = await open();
      expect(await shell.getMarkers()).toEqual(['Maintenance']);
      expect(await shell.getAccountName()).toBe('Account: Farah, Acme, under maintenance');
      const popover = await shell.openMarker('Maintenance');
      expect(await popover.getText()).toBe('Acme is under maintenance. You can view data but not change it until it ends.');
    });

    it("opens the sandbox's popover with a link to its live tenant while the user's list holds it", async () => {
      await start({
        docs: docsWith({ category: 'Sandbox', liveTenantId: 2 }),
        tenants: [tmTestMembership(17, { name: 'Acme Training', category: 'Sandbox', liveTenantId: 2 }), tmTestMembership(2, { name: 'Acme' })],
      });
      const shell = await open();
      const popover = await shell.openMarker('Sandbox');
      expect(await popover.getAriaLabel()).toBe('Sandbox');
      const link = document.querySelector('.tm-session-panels__link');
      expect(document.querySelector('.tm-session-panels__message')?.textContent).toBe("You're in a sandbox. Changes here don't affect live data.");
      expect([link?.textContent, link?.getAttribute('href')]).toEqual(['Open Acme', '/2']);
    });

    it("leaves the live tenant's link out while the user's list lacks it", async () => {
      await start({ docs: docsWith({ category: 'Sandbox', liveTenantId: 2 }), tenants: [] });
      const shell = await open();
      const popover = await shell.openMarker('Sandbox');
      expect(await popover.getText()).toBe("You're in a sandbox. Changes here don't affect live data.");
    });
  });

  describe('slots', () => {
    it("draws each slot's button with its count in its name, and opens its panel", async () => {
      const pending = signal<number | null>(3);
      const feature = tmFeature({ ...tmRouteTestFeature(), appBar: [tmTestShellSlot('acme-approvals', { labelKey: 'Acme_Approvals', count: pending })] });
      await start({ features: [feature], strings: { en: { Acme_Approvals: 'Approvals' } } });
      const shell = await open();
      expect(await shell.getSlots()).toEqual([{ id: 'acme-approvals', name: 'Approvals, 3' }]);
      pending.set(0);
      await tmSettle();
      expect(await shell.getSlots()).toEqual([{ id: 'acme-approvals', name: 'Approvals' }]);
      await shell.clickSlot('acme-approvals');
      await tmSettleUntil(() => document.querySelector('.tm-test-shell-slot-panel') !== null, 'the panel renders');
    });
  });

  describe('the session button', () => {
    it('opens the menu on its first row with ArrowDown and on its last with ArrowUp', async () => {
      await start();
      await open();
      const button = document.querySelector<HTMLButtonElement>('.tm-app-bar__session')!;
      button.focus();
      press({ key: 'ArrowDown' }, button);
      await tmSettleUntil(() => document.activeElement?.closest('.tm-session-menu') != null, 'the menu takes focus');
      expect(document.activeElement?.textContent).toContain('Acme');
      press({ key: 'Escape' }, document.activeElement!);
      await tmSettleUntil(() => document.activeElement === button, 'focus returns');
      press({ key: 'ArrowUp' }, button);
      await tmSettleUntil(() => document.activeElement?.closest('.tm-session-menu') != null, 'the menu takes focus');
      expect(document.activeElement?.textContent).toContain('Sign out');
    });

    it("opens the menu on the gesture after the overlays' chunk failed to load", async () => {
      let failing = true;
      const missing = new TypeError('Failed to fetch dynamically imported module');
      await start({
        providers: [{ provide: TM_SHELL_PANELS_LOAD, useValue: () => (failing ? Promise.reject(missing) : import('./session/tm-session-panels')) }],
      });
      const shell = await open();
      // The idle load fails first; a press loads again, fails the same way and opens nothing.
      await tmSettleUntil(() => rig.telemetry.errors().length > 0, 'the idle load fails');
      document.querySelector<HTMLButtonElement>('.tm-app-bar__session')!.click();
      await tmSettleUntil(() => rig.telemetry.errors().length > 1, 'the press loads again and fails');
      expect(document.querySelector('.tm-session-menu')).toBeNull();
      expect(rig.telemetry.errors()).toEqual([missing, missing]);
      failing = false;
      const menu = await shell.openSessionMenu();
      expect(await menu.getItemLabels()).toContain('Sign out');
    });
  });

  it('opens the Shortcuts dialog on Mod+/, listing the shortcuts active when it opens', async () => {
    await start();
    await open();
    press({ key: '/', code: 'Slash', ctrlKey: true });
    await tmSettleUntil(() => document.querySelector('tm-modal-shell tm-shortcuts-dialog section') !== null, 'the shortcuts render');
    const general = document.querySelector('tm-shortcuts-dialog section');
    expect(general?.querySelector('h3')?.textContent?.trim()).toBe('General');
    expect(Array.from(general?.querySelectorAll('tbody tr td:first-child') ?? []).map((cell) => cell.textContent?.trim())).toContain('Keyboard shortcuts');
  });

  describe('installed mode', () => {
    /** A display-mode query that matches. */
    const installed = (): MediaQueryList => new TmFakeDisplayMode(true) as unknown as MediaQueryList;

    it('shows no Back and Forward in a browser tab', async () => {
      await start();
      const shell = await open();
      expect(await shell.hasHistoryButtons()).toBe(false);
    });

    it('enables Back after a navigation and Forward after a Back, through the router without the Navigation API', async () => {
      await start({ providers: [{ provide: TM_HISTORY_HOST, useValue: { installed: installed(), standalone: false, navigation: null } }] });
      const shell = await open();
      // The app's bootstrap has the router follow history moves; a test router waits to be asked.
      rig.router.setUpLocationChangeListener();
      expect((await shell.getHistoryState()).forward).toBe(false);
      await router.navigateByUrl('/17/centers');
      await tmSettle();
      expect(await shell.getHistoryState()).toEqual({ back: true, forward: false });
      await shell.back();
      await tmSettleUntil(() => rig.router.url === '/17', 'Back lands');
      expect((await shell.getHistoryState()).forward).toBe(true);
      await shell.forward();
      await tmSettleUntil(() => rig.router.url === '/17/centers', 'Forward lands');
      expect(await shell.getHistoryState()).toEqual({ back: true, forward: false });
      const sources = rig.telemetry.recent().filter((item) => item.name === 'tm.shell.navigated').map((item) => item.props);
      expect(sources).toEqual([{ source: 'back' }, { source: 'forward' }]);
    });

    it("follows the Navigation API's answers and moves through it", async () => {
      const navigation = new TmFakeNavigation();
      navigation.canGoBack = true;
      await start({ providers: [{ provide: TM_HISTORY_HOST, useValue: { installed: installed(), standalone: false, navigation } }] });
      const shell = await open();
      expect(await shell.getHistoryState()).toEqual({ back: true, forward: false });
      navigation.enter(true, true);
      await tmSettle();
      expect(await shell.getHistoryState()).toEqual({ back: true, forward: true });
      await shell.back();
      await shell.forward();
      await tmSettle();
      // Each move's refused promises are settled, so none surfaces as an error.
      expect(navigation.moves).toEqual(['back', 'forward']);
      expect(rig.telemetry.errors()).toEqual([]);
    });

    it('draws Back and Forward once the app is installed while it runs, and drops them when it opens in a tab', async () => {
      const mode = new TmFakeDisplayMode(false);
      await start({ providers: [{ provide: TM_HISTORY_HOST, useValue: { installed: mode, standalone: false, navigation: new TmFakeNavigation() } }] });
      const shell = await open();
      expect(await shell.hasHistoryButtons()).toBe(false);
      mode.set(true);
      await tmSettle();
      expect(await shell.hasHistoryButtons()).toBe(true);
      expect(await shell.getHistoryState()).toEqual({ back: false, forward: false });
      mode.set(false);
      await tmSettle();
      expect(await shell.hasHistoryButtons()).toBe(false);
    });
  });

  it("closes the shell's overlays and clears the launcher's text on a tenant switch", async () => {
    await start();
    const beta = { ...TM_T17_DOCS, settings: { ...TM_T17_DOCS.settings, tenantId: 18, names: ['Beta', null] }, tenant: { ...TM_T17_DOCS.tenant, id: 18 } };
    await tmWarmTenant(rig.store, { ...TM_T17, tenantId: 18 }, beta);
    const shell = await open();
    TestBed.inject(TmLauncher).setQuery('cent');
    await shell.openSessionMenu();
    await router.navigateByUrl('/18');
    await tmSettleUntil(() => document.querySelector('.tm-session-menu') === null, 'the session menu closes');
    expect(TestBed.inject(TmLauncher).query()).toBe('');
    // A tenant switch creates a new shell.
    await tmSettleUntil(() => document.querySelector('tm-app-bar .tm-app-bar__tenant')?.textContent === 'Beta', "the new tenant's shell renders");
    const next = await TestbedHarnessEnvironment.documentRootLoader(router.fixture).getHarness(TmTenantShellHarness);
    expect(await next.getHomeHref()).toBe('/18');
  });
});

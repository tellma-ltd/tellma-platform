// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { LiveAnnouncer } from '@angular/cdk/a11y';
import { TestKey } from '@angular/cdk/testing';
import { type HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { ChangeDetectionStrategy, Component, type Provider, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  TM_LAUNCHER_PROVIDERS,
  type TmLaunchDocs,
  type TmLauncherItem,
  type TmLauncherProvider,
  type TmLauncherSearchContext,
  type TmLanguageDefinition,
  type TmMeResult,
  tmEnglish,
  tmEntity,
  tmFeature,
  tmPage,
  type TmFeature,
  TmUrls,
} from '@tellma/core';
import { TmLauncherHarness, TmTenantShellHarness, tmWarmTenant } from '@tellma/core-testing';
import { TmModal, type TmModalRef } from '@tellma/core-ui/modal';
import { TmModalHarness } from '@tellma/core-ui-testing';
import { page } from 'vitest/browser';

import {
  TM_T17,
  TM_T17_DOCS,
  tmAppRig,
  tmLoadTestPage,
  tmNavigate,
  tmSettle,
  tmSettleUntil,
  type TmAppRig,
} from '../../../src/routes/tm-app.testing.util';
import { TM_LAUNCHER_LOAD, TmLauncher, type TmLauncherChunk } from './tm-launcher';
import { TM_LAUNCHER_COMMANDS, type TmLauncherCommand } from './tm-launcher-commands';
import { TmNavTermsReader } from './tm-nav-terms';

// The launcher in a whole test app at /17/credit-review, through the tenant
// shell's harness and the launcher's: spec 0027 §9.2's transitions, the
// browse state's bands, sections, pinned glyphs and current entry, the
// typed state's rows, marks, no-match line and announcements, §9.5's keys,
// the providers' buckets in their order with the superseded run aborted,
// the recent pages that visibleWhen or a lost permission hides, the three
// surfaces by width, and the deferred chunks: the panel's, the Menu modal's
// and the providers' runs', each with its skeleton or its wait, its failure
// and its retry, and the open that waits answering §9.2's closing events.
// Then §5.3's focus rules from lg and at md in both directions, the pinned
// band's commands and where they leave focus, the active row that answers
// arriving later do not move, §9.8's preloading, and the index that builds
// on the first open and neither on keystrokes nor while closed.

/** A permission, as the access document grants it. */
type TmGrant = readonly [resource: string, action: string];

/** The permissions the test feature's entries ask for. */
const GRANTS: readonly TmGrant[] = [
  ['gl.Center', 'Read'],
  ['gl.Center', 'Save'],
  ['acme.Customer', 'Read'],
  ['acme.Customer', 'Save'],
  ['acme.Currency', 'Read'],
  ['acme.Approval', 'Read'],
];

/** The test feature: two sections, three entities, three pages and a page shown only while support is enabled. */
function feature(): TmFeature {
  return tmFeature({
    key: 'acme',
    sections: [
      { id: 'finance', labelKey: 'Acme_Nav_Finance', icon: 'list', order: 10 },
      { id: 'sales', labelKey: 'Acme_Nav_Sales', icon: 'chart-column', order: 20 },
    ],
    entities: [
      tmEntity('gl.Center', {
        section: 'finance',
        order: 10,
        tabs: [{ key: 'all', labelKey: 'Tab_All' }],
        columns: ['Code', 'Name'],
        search: tmLoadTestPage,
        details: { page: tmLoadTestPage, create: tmLoadTestPage },
      }),
      tmEntity('acme.Currency', { section: 'finance', order: 20 }),
      tmEntity('acme.Customer', { details: tmLoadTestPage, section: 'sales', order: 20 }),
    ],
    pages: [
      tmPage('credit-review', { component: tmLoadTestPage, titleKey: 'Acme_CreditReview', section: 'sales', order: 10 }),
      tmPage('centers/:id/map', { component: tmLoadTestPage, titleKey: 'Acme_CenterMap' }),
      tmPage('approvals', { component: tmLoadTestPage, titleKey: 'Acme_Approvals', securable: ['acme.Approval', 'Read'], section: 'sales', order: 30 }),
      tmPage('support-desk', {
        component: tmLoadTestPage,
        titleKey: 'Acme_SupportDesk',
        section: 'sales',
        order: 40,
        visibleWhen: ({ distribution }) => distribution.support.enabled,
      }),
    ],
  });
}

/** The distribution's strings. */
const STRINGS = {
  en: {
    Acme_Nav_Finance: 'Finance',
    Acme_Nav_Sales: 'Sales',
    acme_Customer_Plural: 'Customers',
    acme_Customer: 'Customer',
    acme_Currency_Plural: 'Currencies',
    acme_Currency: 'Currency',
    Acme_CreditReview: 'Credit review',
    Acme_CenterMap: 'Center map',
    Acme_Approvals: 'Approvals',
    Acme_SupportDesk: 'Support desk',
    Acme_Launcher_Records: 'Records',
    Acme_Launcher_Help: 'Help',
  },
};

/** The stored pins: one an entry covers, one no entry covers. */
const PINS = {
  v: 1,
  items: [
    { id: 'a0000001', url: 'centers', title: 'Centers' },
    { id: 'a0000002', url: 'customers?f.CustomerType=in:Corporate', title: 'Corporate customers' },
  ],
};

/** The stored recent pages, newest first: a page no entry covers, an entry, a page visibleWhen hides, and a gated page. */
const RECENT = [
  { p: 'centers/5/map', k: 'custom', t: 1_792_400_000, n: 'Map of center 5' },
  { p: 'centers', k: 'search', t: 1_792_399_000, n: 'Cost centers' },
  { p: 'support-desk', k: 'custom', t: 1_792_398_000, n: 'Support desk' },
  { p: 'approvals', k: 'custom', t: 1_792_397_000, n: 'Approvals' },
];

/** An access document that grants the permissions. */
function access(grants: readonly TmGrant[]): TmMeResult['access'] {
  return { ...TM_T17_DOCS.me.access, securables: grants.map(([resource, action]) => ({ resource, action, hasFilter: false })) };
}

/** Arabic, which falls back to English for every string it lacks. */
const ARABIC: TmLanguageDefinition = { code: 'ar', dir: 'rtl', strings: () => Promise.resolve({ tm: {}, tmUi: {} }) };

/** Tenant 17's documents, with the pins, the recent pages, the grants and the preferences a test adds, in Arabic when asked. */
function docs(grants: readonly TmGrant[] = GRANTS, preferences: Readonly<Record<string, string>> = {}, arabic = false): TmLaunchDocs {
  const { me, settings } = TM_T17_DOCS;
  return {
    ...TM_T17_DOCS,
    settings: arabic ? { ...settings, languages: [...settings.languages, { code: 'ar', nativeName: 'العربية', symbol: 'ع', isRightToLeft: true }] } : settings,
    me: {
      ...me,
      user: { ...me.user, ...(arabic && { preferredLanguage: 'ar' }) },
      access: access(grants),
      preferences: { 'nav.pinned': JSON.stringify(PINS), 'nav.recent': JSON.stringify(RECENT), ...preferences },
    },
  };
}

/** One call of a test provider: the query, the context, and the answer the test gives. */
interface TmTestCall {
  /** The query as typed. */
  readonly query: string;
  /** The tenant, the language, the limit and the abort signal. */
  readonly context: TmLauncherSearchContext;
  /** Answers the call. */
  readonly answer: (items: readonly TmLauncherItem[]) => void;
}

/** A provider whose answers the test gives one at a time. */
class TmTestProvider implements TmLauncherProvider {
  /** The calls made so far. */
  readonly calls: TmTestCall[] = [];
  /** The provider's id. */
  readonly id: string;
  /** The bucket heading's string key. */
  readonly labelKey: string;
  /** The bucket's place below Pages. */
  readonly order: number;
  /** The shortest query it is asked about. */
  readonly minLength?: number;

  /** Creates the provider. */
  constructor(id: string, labelKey: string, order: number, minLength?: number) {
    this.id = id;
    this.labelKey = labelKey;
    this.order = order;
    if (minLength !== undefined) {
      this.minLength = minLength;
    }
  }

  /** Records the call and answers when the test does. */
  search(query: string, context: TmLauncherSearchContext): Promise<readonly TmLauncherItem[]> {
    return new Promise((resolve) => this.calls.push({ query, context, answer: resolve }));
  }

  /** The last call, which must exist. */
  last(): TmTestCall {
    const call = this.calls.at(-1);
    if (call === undefined) {
      throw new Error(`The ${this.id} provider was not called.`);
    }
    return call;
  }
}

/** A dialog a test opens over the launcher. */
@Component({
  selector: 'tm-test-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<p>A dialog</p>`,
})
class TmTestDialog {}

/** How a test holds a chunk's load: behind a gate it opens, failing while it says so. */
interface TmGate {
  /** Resolves when the load may go on; at once by default. */
  readonly gate?: Promise<void>;
  /** Whether the load fails once the gate opens; never by default. */
  readonly failing?: () => boolean;
}

/** A load behind a gate. */
function gated<T>(load: () => Promise<T>, { gate = Promise.resolve(), failing = () => false }: TmGate = {}): () => Promise<T> {
  return () => gate.then(() => (failing() ? Promise.reject(new Error('The chunk is gone.')) : load()));
}

/** The launcher's own loader, its panel's chunk and the panel's own pieces each behind a gate of the test's. */
function gatedLoad(panel: TmGate = {}, pieces: { readonly sheet?: TmGate; readonly runs?: TmGate } = {}): () => Promise<TmLauncherChunk> {
  return gated(
    () =>
      Promise.all([import('./tm-launcher-panel'), import('./tm-launcher-icons'), import('@tellma/core/recents')]).then(([chunk, icons, recents]) => ({
        create: (injector) =>
          chunk.tmCreateLauncherSurface(injector, icons.tmRegisterLauncherIcons, recents.TmRecents, {
            sheet: gated(() => import('./tm-launcher-sheet'), pieces.sheet),
            runs: gated(() => import('./tm-launcher-runs'), pieces.runs),
          }),
      })),
    panel,
  );
}

/** A gate a test opens. */
function latch(): { readonly wait: Promise<void>; readonly release: () => void } {
  let release: () => void = () => undefined;
  const wait = new Promise<void>((resolve) => (release = resolve));
  return { wait, release };
}

/** Presses a key combination on an element, the focused one by default, and says whether something handled it. */
function press(init: KeyboardEventInit, target: EventTarget = document.activeElement ?? document.body): boolean {
  const event = new KeyboardEvent('keydown', { bubbles: true, cancelable: true, ...init });
  target.dispatchEvent(event);
  return event.defaultPrevented;
}

/** Mod+K, which is Ctrl+K off Apple platforms. */
const MOD_K: KeyboardEventInit = { key: 'k', code: 'KeyK', ctrlKey: true };

/** The app bar's search field. */
function field(): HTMLInputElement {
  return document.querySelector<HTMLInputElement>('.tm-app-bar__field input')!;
}

/** The Menu button. */
function menu(): HTMLButtonElement {
  return document.querySelector<HTMLButtonElement>('.tm-app-bar__menu')!;
}

/** The launcher's skeleton, while it shows. */
function skeleton(): HTMLElement | null {
  return document.querySelector<HTMLElement>('.tm-app-bar__launcher-skeleton');
}

/** A button on the page, outside the shell, which the test's end removes. */
function pageButton(): HTMLButtonElement {
  const button = document.createElement('button');
  button.type = 'button';
  button.className = 'tm-test-page-control';
  button.textContent = 'A page control';
  document.body.append(button);
  return button;
}

/** Waits past the close's own timeout, after which focus has settled. */
async function settled(): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 20));
  await tmSettle();
}

describe('the launcher', () => {
  let rig: TmAppRig;
  let shell: TmTenantShellHarness;
  let loader: HarnessLoader;
  let alpha: TmTestProvider;
  let beta: TmTestProvider;
  const viewport = [window.innerWidth, window.innerHeight] as const;

  afterEach(async () => {
    TestBed.resetTestingModule();
    for (const control of document.querySelectorAll('.tm-test-page-control')) {
      control.remove();
    }
    await page.viewport(...viewport);
  });

  /** How a test sets the app up. */
  interface TmStartOptions {
    /** The viewport's width: from lg up by default. */
    readonly width?: number;
    /** The permissions granted. */
    readonly grants?: readonly TmGrant[];
    /** More stored preferences. */
    readonly preferences?: Readonly<Record<string, string>>;
    /** The launcher's loader, when not its own. */
    readonly load?: () => Promise<TmLauncherChunk>;
    /** Whether the user works in Arabic, which the tenant offers. */
    readonly arabic?: boolean;
    /** More providers for the app. */
    readonly providers?: readonly Provider[];
  }

  /** Starts the app at /17/credit-review and waits for the app bar, and for the launcher's chunk unless its loader is the test's. */
  async function start(options: TmStartOptions = {}): Promise<void> {
    await page.viewport(options.width ?? 1280, 900);
    alpha = new TmTestProvider('records', 'Acme_Launcher_Records', 10);
    beta = new TmTestProvider('help', 'Acme_Launcher_Help', 20, 3);
    rig = await tmAppRig({
      docs: docs(options.grants, options.preferences, options.arabic),
      features: [feature()],
      languages: [tmEnglish(), ARABIC],
      strings: STRINGS,
      providers: [
        { provide: TM_LAUNCHER_PROVIDERS, multi: true, useValue: beta },
        { provide: TM_LAUNCHER_PROVIDERS, multi: true, useValue: alpha },
        ...(options.load === undefined ? [] : [{ provide: TM_LAUNCHER_LOAD, useValue: options.load }]),
        ...(options.providers ?? []),
      ],
    });
    // The distribution document stays absent: its read fails, so the support desk stays hidden.
    rig.api.respond('/api/distribution-info', () => {
      throw new Error('The distribution document is unreachable.');
    });
    const router = await tmNavigate('/17/credit-review');
    await tmSettleUntil(() => document.querySelector('tm-tenant-shell tm-app-bar') !== null, 'the tenant shell renders');
    loader = TestbedHarnessEnvironment.documentRootLoader(router.fixture);
    shell = await loader.getHarness(TmTenantShellHarness);
    if (options.load === undefined) {
      // The bar loads the chunk once the browser is idle.
      await tmSettleUntil(() => TestBed.inject(TmLauncher).ɵsurface() !== null, "the launcher's chunk lands");
    }
  }

  /** The launcher service. */
  const launcher = (): TmLauncher => TestBed.inject(TmLauncher);

  /** The groups' headings and their options' labels, as `heading: label, label`. */
  async function outline(harness: TmLauncherHarness): Promise<string[]> {
    return Promise.all(
      (await harness.getGroups()).map(async (group) => `${await group.getLabel()}: ${(await group.getOptionLabels()).join(', ')}`),
    );
  }

  /** The events of a name the app reported, by their properties. */
  const events = (name: string): unknown[] =>
    rig.telemetry
      .recent()
      .filter((item) => item.name === name)
      .map((item) => item.props);

  describe('transitions', () => {
    it('opens in browse from the Menu button and focuses the input', async () => {
      await start();
      const harness = await shell.openLauncher('menu');
      expect(launcher().state()).toBe('browse');
      expect(await harness.getState()).toBe('browse');
      expect(await harness.getSurface()).toBe('panel');
      expect(await harness.isInputFocused()).toBe(true);
      expect(await shell.isLauncherOpen()).toBe(true);
      expect(field().getAttribute('aria-expanded')).toBe('true');
      expect(field().getAttribute('aria-controls')).toBe(document.querySelector('.tm-launcher__list')!.id);
      expect(events('tm.launcher.opened')).toEqual([{ via: 'menu', state: 'browse' }]);
    });

    it('opens in the click’s frame once its chunk is in', async () => {
      await start();
      document.querySelector<HTMLButtonElement>('.tm-app-bar__menu')!.click();
      // No task, no render: the click's own handler drew the panel.
      expect(document.querySelector('.tm-launcher__list [role="option"]')).not.toBeNull();
      expect(document.querySelector('.tm-launcher')!.matches(':popover-open')).toBe(true);
      expect(document.activeElement).toBe(field());
    });

    it('moves from browse to typed on typing, and back when the text is cleared', async () => {
      await start();
      const harness = await shell.openLauncher('menu');
      await harness.type('cent');
      expect(launcher().state()).toBe('typed');
      expect(await harness.getState()).toBe('typed');
      expect(await harness.getOptionLabels()).toEqual(['Centers', 'Center · Create', 'Map of center 5']);
      await harness.type('');
      expect(launcher().state()).toBe('browse');
      expect(await harness.getState()).toBe('browse');
    });

    it('opens in typed by typing in the search field', async () => {
      await start();
      const harness = await shell.typeInSearchField('corp');
      expect(launcher().query()).toBe('corp');
      expect(await harness.getOptionLabels()).toEqual(['Corporate customers']);
      // The field's focus opened it, before the first key.
      expect(events('tm.launcher.opened')).toEqual([{ via: 'field', state: 'browse' }]);
    });

    it('closes on Escape, keeping the text, and reopens on Mod+K in typed with the text selected', async () => {
      await start();
      let harness = await shell.openLauncher('menu');
      await harness.type('cent');
      await harness.close();
      expect(launcher().state()).toBe('closed');
      expect(launcher().query()).toBe('cent');
      expect(document.querySelector('.tm-launcher__list')).toBeNull();
      expect(document.querySelector('.tm-launcher')!.matches(':popover-open')).toBe(false);
      expect(press(MOD_K, document.body)).toBe(true);
      await tmSettle();
      expect(launcher().state()).toBe('typed');
      expect([field().value, field().selectionStart, field().selectionEnd]).toEqual(['cent', 0, 4]);
      harness = await loader.getHarness(TmLauncherHarness);
      expect(await harness.getActiveOption().then((option) => option?.getLabel())).toBe('Centers');
      expect(events('tm.launcher.opened').at(-1)).toEqual({ via: 'shortcut', state: 'typed' });
    });

    it('focuses the input and selects the text on Mod+K while open', async () => {
      await start();
      const harness = await shell.openLauncher('menu');
      await harness.type('cent');
      field().setSelectionRange(4, 4);
      expect(press(MOD_K)).toBe(true);
      expect(launcher().state()).toBe('typed');
      expect([field().selectionStart, field().selectionEnd]).toEqual([0, 4]);
      expect(events('tm.launcher.opened')).toHaveLength(1);
    });

    it('closes on a Menu click while open', async () => {
      await start();
      await shell.openLauncher('menu');
      document.querySelector<HTMLButtonElement>('.tm-app-bar__menu')!.click();
      await tmSettle();
      expect(launcher().state()).toBe('closed');
      expect(await shell.isLauncherOpen()).toBe(false);
    });

    it('closes on a press outside the panel, keeping the text', async () => {
      await start();
      const harness = await shell.openLauncher('menu');
      await harness.type('cent');
      // A press on the scrim, which covers the page.
      document.querySelector('.tm-launcher')!.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
      await tmSettle();
      expect(launcher().state()).toBe('closed');
      expect(launcher().query()).toBe('cent');
    });

    it('stays open on a press inside the panel', async () => {
      await start();
      await shell.openLauncher('menu');
      document.querySelector('.tm-launcher__heading')!.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
      await tmSettle();
      expect(launcher().state()).toBe('browse');
    });

    it('closes on Tab', async () => {
      await start();
      const harness = await shell.openLauncher('menu');
      await harness.pressKey(TestKey.TAB);
      expect(launcher().state()).toBe('closed');
    });

    it('opens the active option on Enter and closes, keeping the text', async () => {
      await start();
      const harness = await shell.openLauncher('menu');
      await harness.type('cent');
      await harness.pressKey(TestKey.ENTER);
      await tmSettleUntil(() => rig.router.url === '/17/centers', 'the router opens Centers');
      expect(launcher().state()).toBe('closed');
      expect(launcher().query()).toBe('cent');
      expect(events('tm.launcher.chosen')).toEqual([{ bucket: 'pages', position: '1', queryLength: '3-5', newTab: false }]);
      expect(events('tm.shell.navigated').at(-1)).toEqual({ source: 'launcher' });
    });

    it('opens a clicked option and closes', async () => {
      await start();
      const harness = await shell.openLauncher('menu');
      const group = await harness.getGroup('Sales');
      const [, , approvals] = await group!.getOptions();
      await approvals!.click();
      await tmSettleUntil(() => rig.router.url === '/17/approvals', 'the router opens Approvals');
      expect(launcher().state()).toBe('closed');
      expect(events('tm.launcher.chosen')).toEqual([{ bucket: 'pages', position: '4-10', queryLength: '0', newTab: false }]);
    });

    it('opens the active option in a new tab on Mod+Enter and Shift+Enter', async () => {
      await start();
      const open = vi.spyOn(window, 'open').mockReturnValue(null);
      try {
        let harness = await shell.openLauncher('menu');
        await harness.type('cent');
        await harness.pressKey(TestKey.ENTER, { control: true });
        expect(open).toHaveBeenLastCalledWith('/17/centers', '_blank', 'noopener');
        expect(launcher().state()).toBe('closed');
        // The kept text opens the typed state again; its second row is the create row.
        harness = await shell.openLauncher('menu');
        await harness.pressKey(TestKey.DOWN_ARROW);
        await harness.pressKey(TestKey.ENTER, { shift: true });
        expect(open).toHaveBeenLastCalledWith('/17/centers/new', '_blank', 'noopener');
        expect(rig.router.url).toBe('/17/credit-review');
        expect(events('tm.launcher.chosen').map((props) => (props as { newTab: boolean }).newTab)).toEqual([true, true]);
      } finally {
        open.mockRestore();
      }
    });

    it('closes on a route change', async () => {
      await start();
      await shell.openLauncher('menu');
      await rig.router.navigateByUrl('/17/approvals');
      await tmSettle();
      expect(launcher().state()).toBe('closed');
    });

    it('stays open on a change of the query alone', async () => {
      await start();
      await shell.openLauncher('menu');
      await rig.router.navigateByUrl('/17/credit-review?tab=open');
      await tmSettle();
      expect(launcher().state()).toBe('browse');
    });

    it('closes when a dialog opens', async () => {
      await start();
      await shell.openLauncher('menu');
      const ref = TestBed.inject(TmModal).open(TmTestDialog, { size: 'sm', title: 'A dialog' });
      await tmSettle();
      expect(launcher().state()).toBe('closed');
      ref.close();
      await tmSettle();
    });

    it('closes and clears the text on a tenant switch', async () => {
      await start();
      const other = { ...docs(), settings: { ...TM_T17_DOCS.settings, tenantId: 18, names: ['Beta', null] }, tenant: { ...TM_T17_DOCS.tenant, id: 18 } };
      await tmWarmTenant(rig.store, { ...TM_T17, tenantId: 18 }, other);
      const harness = await shell.openLauncher('menu');
      await harness.type('cent');
      await rig.router.navigateByUrl('/18');
      await tmSettleUntil(() => document.querySelector('tm-app-bar .tm-app-bar__tenant')?.textContent === 'Beta', "the new tenant's shell renders");
      expect(launcher().state()).toBe('closed');
      expect(launcher().query()).toBe('');
    });
  });

  describe('focus', () => {
    // Each rule from lg, where the input is the app bar's field, and at md, where it is the panel's own, in both directions.
    for (const { width, arabic, name } of [
      { width: 1280, arabic: false, name: 'from lg, in English' },
      { width: 1280, arabic: true, name: 'from lg, in Arabic' },
      { width: 900, arabic: false, name: 'at md, in English' },
      { width: 900, arabic: true, name: 'at md, in Arabic' },
    ]) {
      describe(name, () => {
        const lg = width >= 1280;

        /** The launcher's input: the app bar's field from lg, the panel's own at md. */
        const input = (): HTMLInputElement => (lg ? field() : document.querySelector<HTMLInputElement>('.tm-launcher__own-input')!);

        /** Opens the launcher from the Menu button, which has focus first, as a press would give it. */
        async function fromMenu(): Promise<TmLauncherHarness> {
          menu().focus();
          return shell.openLauncher('menu');
        }

        it('gives focus back to a page control after Mod+K and Escape', async () => {
          await start({ width, arabic });
          const button = pageButton();
          button.focus();
          expect(press(MOD_K, button)).toBe(true);
          await tmSettle();
          const harness = await loader.getHarness(TmLauncherHarness);
          expect(document.activeElement).toBe(input());
          await harness.close();
          expect(launcher().state()).toBe('closed');
          expect(document.activeElement).toBe(button);
        });

        it('gives focus back to the Menu button after Escape', async () => {
          await start({ width, arabic });
          const harness = await fromMenu();
          expect(document.activeElement).toBe(input());
          await harness.close();
          expect(document.activeElement).toBe(menu());
        });

        it('gives focus back to the Menu button after Escape when the press left focus where it was', async () => {
          await start({ width, arabic });
          (document.activeElement as HTMLElement | null)?.blur();
          // A click that does not focus the button, as a press in Safari or a tap on iOS.
          menu().click();
          await tmSettle();
          const harness = await loader.getHarness(TmLauncherHarness);
          expect(document.activeElement).toBe(input());
          await harness.close();
          expect(document.activeElement).toBe(menu());
        });

        it('gives focus back to the opener after a press on the scrim, and leaves it on a focusable control pressed', async () => {
          await start({ width, arabic });
          await fromMenu();
          // A press on the scrim: focus falls to the body, as the press's own default would leave it.
          const scrim = document.querySelector('.tm-launcher')!;
          scrim.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
          scrim.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
          input().blur();
          await settled();
          expect(launcher().state()).toBe('closed');
          expect(document.activeElement).toBe(menu());
          // A press on a focusable control outside the panel: the control keeps the focus it takes.
          const button = pageButton();
          await fromMenu();
          button.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
          button.focus();
          await settled();
          expect(launcher().state()).toBe('closed');
          expect(document.activeElement).toBe(button);
        });

        it(lg ? 'leaves Tab to move on from the field' : 'gives focus back to the opener on Tab, from which Tab moves on', async () => {
          await start({ width, arabic });
          await fromMenu();
          const from = input();
          expect(press({ key: 'Tab', code: 'Tab' }, from)).toBe(false);
          expect(launcher().state()).toBe('closed');
          expect(document.activeElement).toBe(lg ? from : menu());
        });

        it('selects all the kept text when the launcher opens again from a click', async () => {
          await start({ width, arabic });
          let harness = await fromMenu();
          await harness.type('cent');
          await harness.close();
          if (lg) {
            // A click in the field: its focus opens the launcher, and the click's release keeps the selection.
            const target = field();
            target.focus();
            target.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true }));
            target.click();
          } else {
            document.querySelector<HTMLButtonElement>('.tm-app-bar__group-search')!.click();
          }
          await tmSettle();
          harness = await loader.getHarness(TmLauncherHarness);
          expect(await harness.getState()).toBe('typed');
          expect([input().value, input().selectionStart, input().selectionEnd]).toEqual(['cent', 0, 4]);
        });

        it(lg ? 'keeps focus in the field after opening a row in a new tab' : 'gives focus back to the opener after opening a row in a new tab', async () => {
          await start({ width, arabic });
          const open = vi.spyOn(window, 'open').mockReturnValue(null);
          try {
            const harness = await fromMenu();
            await harness.type('cent');
            await harness.pressKey(TestKey.ENTER, { control: true });
            expect(open).toHaveBeenCalledTimes(1);
            expect(launcher().state()).toBe('closed');
            expect(document.activeElement).toBe(lg ? field() : menu());
          } finally {
            open.mockRestore();
          }
        });

        if (lg) {
          it('closes on a press outside a launcher opened from the field, and stays closed as the field takes focus back', async () => {
            await start({ width, arabic });
            field().focus();
            await tmSettle();
            const harness = await loader.getHarness(TmLauncherHarness);
            await harness.type('cent');
            const scrim = document.querySelector('.tm-launcher')!;
            scrim.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
            scrim.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
            field().blur();
            await settled();
            expect(launcher().state()).toBe('closed');
            expect(await shell.isLauncherOpen()).toBe(false);
            expect(document.activeElement).toBe(field());
            expect(events('tm.launcher.opened')).toHaveLength(1);
          });
        }
      });
    }
  });

  describe('commands', () => {
    it("shows the pinned band's commands with no pins, and runs one on Enter or a click, closing the launcher", async () => {
      const run = vi.fn();
      const commands: readonly TmLauncherCommand[] = [{ id: 'pin-page', label: 'Pin this page', icon: 'pin', run }];
      await start({
        preferences: { 'nav.pinned': JSON.stringify({ v: 1, items: [] }), 'nav.pinnedBarVisible': 'false' },
        providers: [{ provide: TM_LAUNCHER_COMMANDS, useValue: signal(commands).asReadonly() }],
      });
      let harness = await shell.openLauncher('menu');
      expect((await outline(harness))[0]).toBe('Pinned: Pin this page');
      const [chip] = await (await harness.getGroup('Pinned'))!.getOptions();
      expect(await chip!.getHref()).toBeNull();
      await harness.pressKey(TestKey.DOWN_ARROW);
      await harness.pressKey(TestKey.ENTER);
      expect(run).toHaveBeenCalledTimes(1);
      expect(launcher().state()).toBe('closed');
      expect(rig.router.url).toBe('/17/credit-review');
      expect(events('tm.launcher.chosen')).toEqual([{ bucket: 'pinned', position: '1', queryLength: '0', newTab: false }]);
      expect(events('tm.shell.navigated').filter((props) => (props as { source: string }).source === 'pin')).toEqual([]);
      harness = await shell.openLauncher('menu');
      const [again] = await (await harness.getGroup('Pinned'))!.getOptions();
      await again!.click();
      expect(run).toHaveBeenCalledTimes(2);
      expect(launcher().state()).toBe('closed');
      expect(rig.router.url).toBe('/17/credit-review');
    });

    it('gives focus to the opener, else the Menu button, before a command runs, so a dialog it opens gives focus back there', async () => {
      let dialog: TmModalRef<unknown> | null = null;
      const run = (): void => {
        dialog = TestBed.inject(TmModal).open(TmTestDialog, { size: 'sm', title: 'A dialog' });
      };
      const commands: readonly TmLauncherCommand[] = [{ id: 'manage', label: 'Manage pins', icon: 'pin', run }];
      await start({
        preferences: { 'nav.pinned': JSON.stringify({ v: 1, items: [] }), 'nav.pinnedBarVisible': 'false' },
        providers: [{ provide: TM_LAUNCHER_COMMANDS, useValue: signal(commands).asReadonly() }],
      });
      /** Runs the command from the open launcher, then closes its dialog. */
      const runAndClose = async (harness: TmLauncherHarness): Promise<void> => {
        await harness.pressKey(TestKey.DOWN_ARROW);
        await harness.pressKey(TestKey.ENTER);
        expect(launcher().state()).toBe('closed');
        await tmSettleUntil(() => document.querySelector('tm-test-dialog') !== null, 'the dialog opens');
        dialog!.close();
        await tmSettle();
      };
      // Opened from the field, whose focus would open it again: the Menu button.
      field().focus();
      await tmSettle();
      await runAndClose(await loader.getHarness(TmLauncherHarness));
      expect(document.activeElement).toBe(menu());
      expect(launcher().state()).toBe('closed');
      // Opened from a page control: the control.
      const button = pageButton();
      button.focus();
      expect(press(MOD_K, button)).toBe(true);
      await tmSettle();
      await runAndClose(await loader.getHarness(TmLauncherHarness));
      expect(document.activeElement).toBe(button);
      expect(launcher().state()).toBe('closed');
      expect(events('tm.launcher.opened')).toHaveLength(2);
    });

    it("has the tenant shell's pin commands by default: Pin this page, then Manage pins…", async () => {
      await start({ preferences: { 'nav.pinned': JSON.stringify({ v: 1, items: [] }), 'nav.pinnedBarVisible': 'false' } });
      const harness = await shell.openLauncher('menu');
      expect((await outline(harness))[0]).toBe('Pinned: Pin this page, Manage pins…');
      await harness.pressKey(TestKey.DOWN_ARROW);
      await harness.pressKey(TestKey.ENTER);
      expect(launcher().state()).toBe('closed');
      // The page is pinned; the bar is hidden, so a toast says so.
      await tmSettleUntil(() => document.querySelector('.tm-toast') !== null, 'the pinned toast shows');
      expect(document.querySelector('.tm-toast')!.textContent).toContain('Pinned Credit review.');
      const again = await shell.openLauncher('menu');
      expect((await outline(again))[0]).toBe('Pinned: Credit review, Edit pin, Manage pins…');
    });

    it('opens Manage pins from its command, and closing the dialog gives focus to the Menu button with the launcher closed', async () => {
      await start({ preferences: { 'nav.pinned': JSON.stringify({ v: 1, items: [] }), 'nav.pinnedBarVisible': 'false' } });
      const harness = await shell.openLauncher('menu');
      await harness.pressKey(TestKey.DOWN_ARROW);
      await harness.pressKey(TestKey.DOWN_ARROW);
      expect(await harness.getActiveDescendantLabel()).toBe('Manage pins…');
      await harness.pressKey(TestKey.ENTER);
      expect(launcher().state()).toBe('closed');
      await tmSettleUntil(() => document.querySelector('tm-manage-pins-dialog') !== null, 'the Manage pins dialog opens');
      const dialog = await loader.getHarness(TmModalHarness);
      expect(await dialog.getTitle()).toBe('Manage pins');
      await dialog.close();
      await settled();
      expect(document.querySelector('tm-manage-pins-dialog')).toBeNull();
      expect(document.activeElement).toBe(menu());
      expect(launcher().state()).toBe('closed');
      expect(events('tm.launcher.opened')).toHaveLength(1);
    });
  });

  describe('browse state', () => {
    it('shows the recently visited band, then the sections, with the pinned glyphs and the current entry', async () => {
      await start();
      const harness = await shell.openLauncher('menu');
      expect(await outline(harness)).toEqual([
        'Recently visited: Map of center 5, Centers, Approvals',
        'Finance: Centers, Currencies',
        'Sales: Credit review, Customers, Approvals',
      ]);
      const [recent, finance, sales] = await harness.getGroups();
      expect(await recent!.getKind()).toBe('band');
      expect(await finance!.getKind()).toBe('section');
      const [centers, currencies] = await finance!.getOptions();
      expect([await centers!.isPinned(), await centers!.getName()]).toEqual([true, 'Centers, pinned']);
      expect([await currencies!.isPinned(), await currencies!.getName()]).toEqual([false, 'Currencies']);
      // The routed page's entry shows selected without being the active option.
      const [review] = await sales!.getOptions();
      expect([await review!.isCurrent(), await review!.isActive()]).toEqual([true, false]);
      expect(await harness.getActiveOption()).toBeNull();
      expect(document.querySelector('.tm-launcher__footer')?.getAttribute('aria-hidden')).toBe('true');
      expect(document.querySelector('.tm-launcher__footer')?.textContent).toContain('Enter to open');
    });

    it('shows the pinned band while the pinned bar is hidden', async () => {
      await start({ preferences: { 'nav.pinnedBarVisible': 'false' } });
      const harness = await shell.openLauncher('menu');
      expect((await outline(harness))[0]).toBe('Pinned: Centers, Corporate customers, Pin this page, Manage pins…');
    });

    it('hides a recent page that visibleWhen hides, and one whose permission is lost', async () => {
      await start();
      let harness = await shell.openLauncher('menu');
      expect(await (await harness.getGroup('Recently visited'))!.getOptionLabels()).toEqual(['Map of center 5', 'Centers', 'Approvals']);
      await harness.close();
      // The permissions change: the next sync brings an access document without approvals.
      const { me } = docs(GRANTS.filter(([resource]) => resource !== 'acme.Approval'));
      await rig.store.put(TM_T17, 'me', { tag: null, savedAt: Date.now(), data: me });
      await tmSettle();
      harness = await shell.openLauncher('menu');
      expect(await outline(harness)).toEqual(['Recently visited: Map of center 5, Centers', 'Finance: Centers, Currencies', 'Sales: Credit review, Customers']);
    });

    it("draws every icon it names: the entries', the sections', the recent pages' and the pinned glyph", async () => {
      await start({ preferences: { 'nav.pinnedBarVisible': 'false' } });
      const harness = await shell.openLauncher('menu');
      const icons = [...document.querySelectorAll('.tm-launcher__list tm-icon')];
      expect(icons.length).toBeGreaterThan(10);
      expect(icons.filter((icon) => icon.querySelector('svg') === null)).toEqual([]);
      await harness.type('cent');
      expect([...document.querySelectorAll('.tm-launcher__list tm-icon')].filter((icon) => icon.querySelector('svg') === null)).toEqual([]);
    });
  });

  describe('typed state', () => {
    it('lists the matching pages with their descriptions and marks, the first active', async () => {
      await start();
      const harness = await shell.openLauncher('menu');
      await harness.type('cent');
      const pages = await harness.getGroup('Pages');
      const [centers, create, map] = await pages!.getOptions();
      expect([await centers!.getLabel(), await centers!.getDescription(), await centers!.getMarks(), await centers!.isActive()]).toEqual([
        'Centers',
        'Finance',
        ['Cent'],
        true,
      ]);
      expect([await create!.getLabel(), await create!.getHref()]).toEqual(['Center · Create', '/17/centers/new']);
      expect([await map!.getDescription(), await map!.getMarks()]).toEqual(['Recently visited', ['cent']]);
      expect(await harness.getActiveDescendantLabel()).toBe('Centers');
      // The marks are elements, never markup.
      expect(document.querySelector('.tm-launcher__label mark')?.childNodes[0]?.nodeType).toBe(Node.TEXT_NODE);
    });

    it('finds a pin no entry covers, described as pinned', async () => {
      await start();
      const harness = await shell.openLauncher('menu');
      await harness.type('corp');
      const [pin] = await harness.getOptions();
      expect([await pin!.getLabel(), await pin!.getDescription(), await pin!.getHref()]).toEqual([
        'Corporate customers',
        'Pinned',
        '/17/customers?f.CustomerType=in:Corporate',
      ]);
    });

    it('builds the page index as it opens, and neither on keystrokes nor while closed', async () => {
      await start();
      const read = vi.spyOn(TestBed.inject(TmNavTermsReader), 'read');
      let harness = await shell.openLauncher('menu');
      expect(read).toHaveBeenCalled();
      read.mockClear();
      for (const text of ['c', 'ce', 'cen', 'cent', 'cente']) {
        await harness.type(text);
      }
      expect(read).not.toHaveBeenCalled();
      await harness.close();
      // The kept text and a change of the access document while closed: nothing builds until the next open.
      const { me } = docs(GRANTS.filter(([resource]) => resource !== 'acme.Approval'));
      await rig.store.put(TM_T17, 'me', { tag: null, savedAt: Date.now(), data: me });
      await tmSettle();
      expect(read).not.toHaveBeenCalled();
      harness = await shell.openLauncher('menu');
      expect(read).toHaveBeenCalled();
      expect(await harness.getState()).toBe('typed');
      read.mockRestore();
    });

    it('says when nothing matched', async () => {
      await start();
      const harness = await shell.openLauncher('menu');
      await harness.type('zzz');
      const pages = await harness.getGroup('Pages');
      expect(await pages!.getOptionLabels()).toEqual([]);
      expect(await pages!.getEmptyText()).toBe('Nothing matched “zzz”.');
      expect(await harness.getActiveOption()).toBeNull();
    });

    it('announces the results politely half a second after the last keystroke', async () => {
      await start();
      const announce = vi.spyOn(TestBed.inject(LiveAnnouncer), 'announce').mockResolvedValue();
      const harness = await shell.openLauncher('menu');
      await harness.type('cent');
      expect(announce).not.toHaveBeenCalled();
      await tmSettleUntil(() => announce.mock.calls.length > 0, 'the announcement');
      expect(announce).toHaveBeenLastCalledWith('2 pages', 'polite');
      // A bucket that answers later is announced on its own.
      alpha.last().answer([{ id: '1', href: '/17/centers/1', label: 'Center 1' }]);
      await tmSettleUntil(() => announce.mock.calls.length > 1, "the bucket's announcement");
      expect(announce).toHaveBeenLastCalledWith('Records: 1 result', 'polite');
      await harness.type('zzz');
      await tmSettleUntil(() => announce.mock.calls.length > 2, 'the no-result announcement');
      expect(announce).toHaveBeenLastCalledWith('Nothing matched', 'polite');
      expect(events('tm.launcher.no-results')).toEqual([{ queryLength: '3-5' }]);
      announce.mockRestore();
    });
  });

  describe('providers', () => {
    it('places each bucket below Pages in the providers’ order, whatever order they answer in', async () => {
      await start();
      const harness = await shell.openLauncher('menu');
      await harness.type('cent');
      await tmSettleUntil(() => beta.calls.length > 0 && alpha.calls.length > 0, 'the providers run');
      expect(alpha.last().context).toMatchObject({ tenantId: 17, language: 'en', limit: 5 });
      beta.last().answer([{ id: 'h1', href: '/17/help/centers', label: 'About centers', description: 'Help article' }]);
      await tmSettleUntil(() => document.querySelectorAll('.tm-launcher__group--bucket').length === 1, "help's bucket");
      alpha.last().answer(Array.from({ length: 7 }, (_, i) => ({ id: `${i}`, href: `/17/centers/${i}`, label: `Center ${i}` })));
      await tmSettleUntil(() => document.querySelectorAll('.tm-launcher__group--bucket').length === 2, "records' bucket");
      expect(await harness.getGroupLabels()).toEqual(['Pages', 'Records', 'Help']);
      // At most the context's limit of rows, each with its icon or the link glyph.
      expect(await (await harness.getGroup('Records'))!.getOptionLabels()).toEqual(['Center 0', 'Center 1', 'Center 2', 'Center 3', 'Center 4']);
      expect(await (await (await harness.getGroup('Help'))!.getOptions())[0]!.getDescription()).toBe('Help article');
    });

    it("aborts a run that the next keystroke supersedes, and drops its answer", async () => {
      await start();
      const harness = await shell.openLauncher('menu');
      await harness.type('ce');
      await tmSettleUntil(() => alpha.calls.length > 0, 'the first run');
      const first = alpha.last();
      await harness.type('cent');
      await tmSettleUntil(() => alpha.calls.at(-1)?.query === 'cent', 'the last run');
      expect(first.context.signal.aborted).toBe(true);
      first.answer([{ id: 'x', href: '/17/x', label: 'Stale' }]);
      await tmSettle();
      expect(await harness.getGroupLabels()).toEqual(['Pages']);
    });

    it("asks a provider only once the text reaches its minimum length", async () => {
      await start();
      const harness = await shell.openLauncher('menu');
      await harness.type('ce');
      await tmSettleUntil(() => alpha.calls.length > 0, 'the run');
      expect(beta.calls).toEqual([]);
      await harness.type('cen');
      await tmSettleUntil(() => beta.calls.length > 0, "help's run");
    });

    it('aborts the run when the launcher closes', async () => {
      await start();
      const harness = await shell.openLauncher('menu');
      await harness.type('cent');
      await tmSettleUntil(() => alpha.calls.length > 0, 'the run');
      await harness.close();
      expect(alpha.last().context.signal.aborted).toBe(true);
    });

    it('keeps the active row where it first showed when no page matches and a bucket above answers later', async () => {
      await start();
      const harness = await shell.openLauncher('menu');
      await harness.type('zzz');
      await tmSettleUntil(() => alpha.calls.length > 0 && beta.calls.length > 0, 'the providers run');
      beta.last().answer([{ id: 'h1', href: '/17/help/zzz', label: 'About zzz' }]);
      await tmSettleUntil(() => document.querySelectorAll('.tm-launcher__group--bucket').length === 1, "help's bucket");
      expect(await (await harness.getActiveOption())?.getLabel()).toBe('About zzz');
      alpha.last().answer([{ id: '1', href: '/17/centers/1', label: 'Center 1' }]);
      await tmSettleUntil(() => document.querySelectorAll('.tm-launcher__group--bucket').length === 2, "records' bucket");
      expect(await harness.getGroupLabels()).toEqual(['Pages', 'Records', 'Help']);
      expect(await (await harness.getActiveOption())?.getLabel()).toBe('About zzz');
    });

    it("reports a clicked bucket row's position and bucket", async () => {
      await start();
      const harness = await shell.openLauncher('menu');
      await harness.type('cent');
      await tmSettleUntil(() => alpha.calls.length > 0, 'the run');
      alpha.last().answer([{ id: '1', href: '/17/centers/1', label: 'Center 1' }]);
      await tmSettleUntil(() => document.querySelector('.tm-launcher__group--bucket') !== null, "records' bucket");
      const [row] = await (await harness.getGroup('Records'))!.getOptions();
      await row!.click();
      await tmSettleUntil(() => launcher().state() === 'closed', 'the launcher closes');
      expect(events('tm.launcher.chosen')).toEqual([{ bucket: 'records', position: '4-10', queryLength: '3-5', newTab: false }]);
    });
  });

  describe('keys', () => {
    /** The active option's label, or `null`. */
    const active = async (harness: TmLauncherHarness): Promise<string | null> => (await harness.getActiveOption())?.getLabel() ?? null;

    it('moves over the browse state’s options in reading order, across chips and sections, by group and to either end', async () => {
      await start();
      const harness = await shell.openLauncher('menu');
      await harness.pressKey(TestKey.DOWN_ARROW);
      expect(await active(harness)).toBe('Map of center 5');
      expect(await harness.getActiveDescendantLabel()).toBe('Map of center 5');
      await harness.pressKey(TestKey.RIGHT_ARROW);
      expect(await active(harness)).toBe('Centers');
      await harness.pressKey(TestKey.LEFT_ARROW);
      expect(await active(harness)).toBe('Map of center 5');
      await harness.pressKey(TestKey.PAGE_DOWN);
      expect(await (await harness.getActiveOption())!.getName()).toBe('Centers, pinned');
      await harness.pressKey(TestKey.DOWN_ARROW);
      expect(await active(harness)).toBe('Currencies');
      // Across sections: the same index in the next one, clamped at the last section.
      await harness.pressKey(TestKey.RIGHT_ARROW);
      expect(await active(harness)).toBe('Customers');
      await harness.pressKey(TestKey.RIGHT_ARROW);
      expect(await active(harness)).toBe('Customers');
      await harness.pressKey(TestKey.LEFT_ARROW);
      expect(await active(harness)).toBe('Currencies');
      await harness.pressKey(TestKey.PAGE_UP);
      expect(await active(harness)).toBe('Map of center 5');
      await harness.pressKey(TestKey.END);
      expect(await active(harness)).toBe('Approvals');
      await harness.pressKey(TestKey.DOWN_ARROW);
      expect(await active(harness)).toBe('Approvals');
      await harness.pressKey(TestKey.HOME);
      expect(await active(harness)).toBe('Map of center 5');
    });

    it('starts from the last option on Up', async () => {
      await start();
      const harness = await shell.openLauncher('menu');
      await harness.pressKey(TestKey.UP_ARROW);
      expect(await active(harness)).toBe('Approvals');
    });

    it('maps Right and Left to the reading direction in a right-to-left language', async () => {
      await start({ arabic: true });
      const harness = await shell.openLauncher('menu');
      const [first, second] = await (await harness.getGroups())[0]!.getOptionLabels();
      await harness.pressKey(TestKey.DOWN_ARROW);
      expect(await active(harness)).toBe(first);
      // Left is forward in a right-to-left language, and Right back.
      await harness.pressKey(TestKey.LEFT_ARROW);
      expect(await active(harness)).toBe(second);
      await harness.pressKey(TestKey.RIGHT_ARROW);
      expect(await active(harness)).toBe(first);
    });

    it('leaves Right, Left, Home and End to the caret in the typed state', async () => {
      await start();
      const harness = await shell.openLauncher('menu');
      await harness.type('cent');
      for (const key of ['ArrowRight', 'ArrowLeft', 'Home', 'End']) {
        expect(press({ key }, field()), key).toBe(false);
      }
      expect(await active(harness)).toBe('Centers');
    });

    it('moves over the typed rows with Down and Up, and between buckets with PageDown and PageUp', async () => {
      await start();
      const harness = await shell.openLauncher('menu');
      await harness.type('cent');
      await tmSettleUntil(() => alpha.calls.length > 0, 'the run');
      alpha.last().answer([{ id: '1', href: '/17/centers/1', label: 'Center 1' }]);
      await tmSettleUntil(() => document.querySelector('.tm-launcher__group--bucket') !== null, "records' bucket");
      await harness.pressKey(TestKey.DOWN_ARROW);
      expect(await active(harness)).toBe('Center · Create');
      await harness.pressKey(TestKey.PAGE_DOWN);
      expect(await active(harness)).toBe('Center 1');
      await harness.pressKey(TestKey.UP_ARROW);
      expect(await active(harness)).toBe('Map of center 5');
      await harness.pressKey(TestKey.PAGE_UP);
      expect(await active(harness)).toBe('Centers');
    });

    it('activates a row on hover', async () => {
      await start();
      const harness = await shell.openLauncher('menu');
      await harness.type('cent');
      const [, , map] = await harness.getOptions();
      await map!.hover();
      expect(await active(harness)).toBe('Map of center 5');
    });

    it('carries the APG combobox attributes on the input', async () => {
      await start();
      await shell.openLauncher('menu');
      const input = field();
      expect(input.getAttribute('role')).toBe('combobox');
      expect(input.getAttribute('aria-autocomplete')).toBe('list');
      expect(input.getAttribute('aria-keyshortcuts')).toMatch(/^(Control|Meta)\+K$/);
      expect(document.getElementById(input.getAttribute('aria-describedby')!)?.textContent).toContain('Escape to close');
      const listbox = document.querySelector('.tm-launcher__list')!;
      expect(listbox.getAttribute('role')).toBe('listbox');
      for (const group of listbox.querySelectorAll('[role="group"]')) {
        expect(document.getElementById(group.getAttribute('aria-labelledby')!)).not.toBeNull();
      }
    });
  });

  describe('preloading', () => {
    it("starts loading the active row's page on Down, on hover and for the typed state's first row", async () => {
      await start();
      const preload = vi.spyOn(TestBed.inject(TmUrls), 'preload');
      const harness = await shell.openLauncher('menu');
      await harness.pressKey(TestKey.DOWN_ARROW);
      expect(preload).toHaveBeenLastCalledWith(await (await harness.getActiveOption())!.getHref());
      const [, currencies] = await (await harness.getGroup('Finance'))!.getOptions();
      await currencies!.hover();
      expect(preload).toHaveBeenLastCalledWith(await currencies!.getHref());
      await harness.type('cent');
      expect(preload).toHaveBeenLastCalledWith('/17/centers');
      preload.mockRestore();
    });
  });

  describe('surfaces', () => {
    it('gives the panel a field of its own at md', async () => {
      await start({ width: 900 });
      const harness = await shell.openLauncher('group-search');
      expect(await harness.getSurface()).toBe('field');
      expect(await harness.isInputFocused()).toBe(true);
      await harness.type('cent');
      expect(launcher().query()).toBe('cent');
      await harness.close();
      expect(launcher().state()).toBe('closed');
    });

    it('opens the Menu modal below md, focusing Close from the Menu button and the field from search', async () => {
      await start({ width: 420 });
      let harness = await shell.openLauncher('menu');
      expect(await harness.getSurface()).toBe('sheet');
      expect(document.activeElement?.classList.contains('tm-launcher__close')).toBe(true);
      // Below md, the pinned band shows.
      expect((await outline(harness))[0]).toBe('Pinned: Centers, Corporate customers, Pin this page, Manage pins…');
      (document.activeElement as HTMLElement).click();
      await tmSettleUntil(() => document.querySelector('.tm-launcher__list') === null, 'the modal closes');
      expect(launcher().state()).toBe('closed');
      harness = await shell.openLauncher('search');
      expect(await harness.isInputFocused()).toBe(true);
      await harness.type('cent');
      expect(await harness.getOptionLabels()).toEqual(['Centers', 'Center · Create', 'Map of center 5']);
      await harness.close();
      await tmSettleUntil(() => document.querySelector('.tm-launcher__list') === null, 'the modal closes');
      expect([launcher().state(), launcher().query()]).toEqual(['closed', 'cent']);
    });

    it('gives focus back to the Menu button as the Menu modal closes when the tap left focus nowhere', async () => {
      await start({ width: 420 });
      (document.activeElement as HTMLElement | null)?.blur();
      menu().click();
      await tmSettleUntil(() => document.querySelector('.tm-launcher__list') !== null, 'the Menu modal opens');
      const harness = await loader.getHarness(TmLauncherHarness);
      expect(await harness.getSurface()).toBe('sheet');
      await harness.close();
      await tmSettleUntil(() => document.querySelector('.tm-launcher__list') === null, 'the modal closes');
      expect(document.activeElement).toBe(menu());
    });

    it('selects the text on Mod+K in the Menu modal', async () => {
      await start({ width: 420 });
      const harness = await shell.openLauncher('search');
      await harness.type('cent');
      const input = document.activeElement as HTMLInputElement;
      input.setSelectionRange(4, 4);
      expect(press(MOD_K, input)).toBe(true);
      expect([input.selectionStart, input.selectionEnd]).toEqual([0, 4]);
    });

    it('closes when the viewport crosses lg while the panel is open', async () => {
      await start();
      await shell.openLauncher('menu');
      await page.viewport(900, 900);
      await tmSettleUntil(() => launcher().state() === 'closed', 'the launcher closes');
      expect(await shell.isLauncherOpen()).toBe(false);
    });

    it('leaves Ctrl+K to the field in the Menu modal on Apple platforms, where Mod is the Command key', async () => {
      Object.defineProperty(navigator, 'userAgentData', { configurable: true, get: () => ({ platform: 'macOS' }) });
      try {
        await start({ width: 420 });
        const harness = await shell.openLauncher('search');
        await harness.type('cent');
        const input = document.activeElement as HTMLInputElement;
        input.setSelectionRange(4, 4);
        expect(press(MOD_K, input)).toBe(false);
        expect([input.selectionStart, input.selectionEnd]).toEqual([4, 4]);
        expect(press({ key: 'k', code: 'KeyK', metaKey: true }, input)).toBe(true);
        expect([input.selectionStart, input.selectionEnd]).toEqual([0, 4]);
      } finally {
        // The own property shadowed Navigator.prototype's getter; removing it restores the real one.
        Reflect.deleteProperty(navigator, 'userAgentData');
      }
    });
  });

  describe('loading', () => {
    /** Whether the app reported a launcher error. */
    const reported = (): boolean => rig.telemetry.recent().some((item) => item.name === 'tm.error.captured' && item.props['source'] === 'tm-launcher');

    /** Waits for the launcher's chunk, which the bar loads once the browser is idle. */
    const landed = (): Promise<void> => tmSettleUntil(() => launcher().ɵsurface() !== null, "the launcher's chunk lands");

    it('shows a skeleton while an open waits for the chunk, then the panel', async () => {
      const panel = latch();
      await start({ load: gatedLoad({ gate: panel.wait }) });
      menu().click();
      await tmSettle();
      expect(launcher().state()).toBe('browse');
      expect(await shell.isLauncherSkeletonShown()).toBe(true);
      expect(skeleton()!.matches(':popover-open')).toBe(true);
      panel.release();
      const harness = await shell.openLauncher('menu');
      expect(await shell.isLauncherSkeletonShown()).toBe(false);
      expect(await harness.isInputFocused()).toBe(true);
    });

    it('focuses the field at once on Mod+K from lg while the chunk loads, and keeps the text typed meanwhile unselected', async () => {
      const panel = latch();
      await start({ load: gatedLoad({ gate: panel.wait }) });
      launcher().setQuery('ac');
      await tmSettle();
      const button = pageButton();
      button.focus();
      expect(press(MOD_K, button)).toBe(true);
      // The field takes focus before the panel, with the kept text selected.
      expect(document.activeElement).toBe(field());
      expect([field().selectionStart, field().selectionEnd]).toEqual([0, 2]);
      // The user types over the selection.
      field().value = 'cent';
      field().dispatchEvent(new Event('input', { bubbles: true }));
      expect(launcher().query()).toBe('cent');
      panel.release();
      await tmSettleUntil(() => document.querySelector('.tm-launcher')?.matches(':popover-open') === true, 'the panel opens');
      expect(document.activeElement).toBe(field());
      expect([field().value, field().selectionStart, field().selectionEnd]).toEqual(['cent', 4, 4]);
      expect(launcher().state()).toBe('typed');
    });

    it('closes an open that waits on Escape, giving focus back, and opens nothing when the chunk lands', async () => {
      const panel = latch();
      await start({ load: gatedLoad({ gate: panel.wait }) });
      const button = pageButton();
      button.focus();
      press(MOD_K, button);
      expect(launcher().state()).toBe('browse');
      expect(press({ key: 'Escape', code: 'Escape' })).toBe(true);
      expect(launcher().state()).toBe('closed');
      expect(document.activeElement).toBe(button);
      panel.release();
      await landed();
      await tmSettle();
      expect(launcher().state()).toBe('closed');
      expect(document.querySelector('.tm-launcher')!.matches(':popover-open')).toBe(false);
      expect(await shell.isLauncherSkeletonShown()).toBe(false);
      expect(document.activeElement).toBe(button);
    });

    it('closes an open that waits on a press outside, a path change or a dialog taking focus', async () => {
      const panel = latch();
      await start({ load: gatedLoad({ gate: panel.wait }) });
      const button = pageButton();
      // A press on the page's text: focus goes back to the control that had it.
      button.focus();
      press(MOD_K, button);
      document.body.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
      field().blur();
      await settled();
      expect(launcher().state()).toBe('closed');
      expect(document.activeElement).toBe(button);
      // A press inside the app bar's controls is the launcher's own.
      press(MOD_K, button);
      field().dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
      expect(launcher().state()).toBe('browse');
      // A path change.
      await rig.router.navigateByUrl('/17/approvals');
      await tmSettle();
      expect(launcher().state()).toBe('closed');
      // A dialog, which takes focus.
      press(MOD_K, document.body);
      expect(launcher().state()).toBe('browse');
      const ref = TestBed.inject(TmModal).open(TmTestDialog, { size: 'sm', title: 'A dialog' });
      await tmSettle();
      expect(launcher().state()).toBe('closed');
      panel.release();
      await landed();
      await tmSettle();
      expect(launcher().state()).toBe('closed');
      expect(document.querySelector('tm-test-dialog')?.closest('.cdk-overlay-container')?.contains(document.activeElement)).toBe(true);
      ref.close();
      await tmSettle();
    });

    it("holds the keys typed below lg while the chunk loads, for the panel's own field, the first replacing the kept text", async () => {
      const panel = latch();
      await start({ width: 900, load: gatedLoad({ gate: panel.wait }) });
      launcher().setQuery('zz');
      const button = pageButton();
      button.focus();
      press(MOD_K, button);
      const heard = vi.fn();
      button.addEventListener('keydown', heard);
      // Before the skeleton renders, the keys are already the launcher's, never the page's.
      expect(press({ key: 'c', code: 'KeyC' }, button)).toBe(true);
      await tmSettle();
      expect(document.activeElement).toBe(skeleton());
      for (const key of ['e', 'n', 'x', 'Backspace']) {
        expect(press({ key })).toBe(true);
      }
      expect(heard).not.toHaveBeenCalled();
      expect(launcher().query()).toBe('cen');
      panel.release();
      await tmSettleUntil(() => document.querySelector('.tm-launcher__own-input') !== null, "the panel's own field");
      const own = document.querySelector<HTMLInputElement>('.tm-launcher__own-input')!;
      expect(document.activeElement).toBe(own);
      expect([own.value, own.selectionStart, own.selectionEnd]).toEqual(['cen', 3, 3]);
    });

    it('closes an open that waits below lg on Tab, giving focus back to the control that had it, from which Tab moves on', async () => {
      const panel = latch();
      await start({ width: 900, load: gatedLoad({ gate: panel.wait }) });
      const button = pageButton();
      button.focus();
      press(MOD_K, button);
      await tmSettle();
      expect(document.activeElement).toBe(skeleton());
      expect(press({ key: 'Tab', code: 'Tab' })).toBe(false);
      expect(launcher().state()).toBe('closed');
      expect(document.activeElement).toBe(button);
    });

    it('closes an open whose chunk fails to load, reports it, and tries again on the next', async () => {
      let failing = true;
      await start({ load: gatedLoad({ failing: () => failing }) });
      document.querySelector<HTMLButtonElement>('.tm-app-bar__menu')!.click();
      await tmSettleUntil(() => launcher().state() === 'closed', 'the open closes');
      expect(await shell.isLauncherSkeletonShown()).toBe(false);
      expect(reported()).toBe(true);
      failing = false;
      const harness = await shell.openLauncher('menu');
      expect(await harness.getState()).toBe('browse');
    });

    it("shows the skeleton while the Menu modal's chunk loads, then the modal, which its own opening leaves open", async () => {
      const sheet = latch();
      await start({ width: 420, load: gatedLoad({}, { sheet: { gate: sheet.wait } }) });
      await landed();
      menu().click();
      await tmSettle();
      expect(await shell.isLauncherSkeletonShown()).toBe(true);
      expect(document.activeElement).toBe(skeleton());
      sheet.release();
      const harness = await shell.openLauncher('menu');
      expect(await harness.getSurface()).toBe('sheet');
      await settled();
      expect(launcher().state()).toBe('browse');
      expect(await shell.isLauncherSkeletonShown()).toBe(false);
      expect(document.activeElement?.classList.contains('tm-launcher__close')).toBe(true);
    });

    it("closes on Tab and on Escape while the Menu modal's chunk loads, giving focus back to the trigger", async () => {
      const sheet = latch();
      await start({ width: 420, load: gatedLoad({}, { sheet: { gate: sheet.wait } }) });
      await landed();
      (document.activeElement as HTMLElement | null)?.blur();
      menu().click();
      await tmSettle();
      expect(press({ key: 'Tab', code: 'Tab' })).toBe(false);
      expect(launcher().state()).toBe('closed');
      expect(document.activeElement).toBe(menu());
      const search = document.querySelector<HTMLButtonElement>('.tm-app-bar__narrow-search')!;
      menu().blur();
      search.click();
      await tmSettle();
      expect(document.activeElement).toBe(skeleton());
      expect(press({ key: 'Escape', code: 'Escape' })).toBe(true);
      expect(launcher().state()).toBe('closed');
      expect(document.activeElement).toBe(search);
      sheet.release();
      await settled();
      expect(document.querySelector('.tm-launcher__list')).toBeNull();
    });

    it("closes an open whose Menu modal's chunk fails to load, reports it, and tries again on the next", async () => {
      let failing = true;
      const sheet = latch();
      await start({ width: 420, load: gatedLoad({}, { sheet: { gate: sheet.wait, failing: () => failing } }) });
      await landed();
      menu().click();
      await tmSettle();
      expect(await shell.isLauncherSkeletonShown()).toBe(true);
      sheet.release();
      await tmSettleUntil(() => launcher().state() === 'closed', 'the open closes');
      expect(await shell.isLauncherSkeletonShown()).toBe(false);
      expect(reported()).toBe(true);
      failing = false;
      const harness = await shell.openLauncher('menu');
      expect(await harness.getSurface()).toBe('sheet');
    });

    it("lets a dialog that opens while the Menu modal's chunk loads win", async () => {
      const sheet = latch();
      await start({ width: 420, load: gatedLoad({}, { sheet: { gate: sheet.wait } }) });
      await landed();
      menu().click();
      await tmSettle();
      const ref = TestBed.inject(TmModal).open(TmTestDialog, { size: 'sm', title: 'A dialog' });
      await tmSettle();
      expect(launcher().state()).toBe('closed');
      sheet.release();
      await settled();
      expect(document.querySelector('.tm-launcher-sheet')).toBeNull();
      expect(document.querySelector('tm-test-dialog')).not.toBeNull();
      ref.close();
      await tmSettle();
    });

    it("runs the providers once their chunk lands during a typed open", async () => {
      const runs = latch();
      await start({ load: gatedLoad({}, { runs: { gate: runs.wait } }) });
      await landed();
      const harness = await shell.openLauncher('menu');
      await harness.type('cent');
      await tmSettle();
      expect(alpha.calls).toEqual([]);
      runs.release();
      await tmSettleUntil(() => alpha.calls.at(-1)?.query === 'cent', 'the run');
    });

    it("tries the providers' chunk again on the next open after a failed load", async () => {
      let failing = true;
      await start({ load: gatedLoad({}, { runs: { failing: () => failing } }) });
      await landed();
      await tmSettleUntil(reported, 'the failed load is reported');
      failing = false;
      const harness = await shell.openLauncher('menu');
      await harness.type('cent');
      await tmSettleUntil(() => alpha.calls.at(-1)?.query === 'cent', 'the run');
    });
  });
});

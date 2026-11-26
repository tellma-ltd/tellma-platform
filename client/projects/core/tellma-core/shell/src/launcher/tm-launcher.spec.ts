// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { LiveAnnouncer } from '@angular/cdk/a11y';
import { TestKey } from '@angular/cdk/testing';
import { type HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { ChangeDetectionStrategy, Component } from '@angular/core';
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
} from '@tellma/core';
import { TmLauncherHarness, TmTenantShellHarness, tmWarmTenant } from '@tellma/core-testing';
import { TmModal } from '@tellma/core-ui/modal';
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

// The launcher in a whole test app at /17/credit-review, through the tenant
// shell's harness and the launcher's: spec 0027 §9.2's transitions, the
// browse state's bands, sections, pinned glyphs and current entry, the
// typed state's rows, marks, no-match line and announcements, §9.5's keys,
// the providers' buckets in their order with the superseded run aborted,
// the recent pages that visibleWhen or a lost permission hides, the three
// surfaces by width, and the deferred chunk: its skeleton, its failure and
// the first open in the click's frame once it is in.

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

/** The launcher's own loader, behind a gate a test opens, failing while the test says so. */
function gatedLoad(gate: Promise<void>, failing: () => boolean = () => false): () => Promise<TmLauncherChunk> {
  return () =>
    gate.then(() => {
      if (failing()) {
        return Promise.reject(new Error('The chunk is gone.'));
      }
      return Promise.all([import('./tm-launcher-panel'), import('./tm-launcher-icons'), import('@tellma/core/recents')]).then(
        ([panel, icons, recents]) => ({
          TmLauncherPanel: panel.TmLauncherPanel,
          tmRegisterLauncherIcons: icons.tmRegisterLauncherIcons,
          TmRecents: recents.TmRecents,
        }),
      );
    });
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

describe('the launcher', () => {
  let rig: TmAppRig;
  let shell: TmTenantShellHarness;
  let loader: HarnessLoader;
  let alpha: TmTestProvider;
  let beta: TmTestProvider;
  const viewport = [window.innerWidth, window.innerHeight] as const;

  afterEach(async () => {
    TestBed.resetTestingModule();
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
      expect((await outline(harness))[0]).toBe('Pinned: Centers, Corporate customers');
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
      expect((await outline(harness))[0]).toBe('Pinned: Centers, Corporate customers');
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

    it('selects the text on Mod+K in the Menu modal', async () => {
      await start({ width: 420 });
      const harness = await shell.openLauncher('search');
      await harness.type('cent');
      const input = document.activeElement as HTMLInputElement;
      input.setSelectionRange(4, 4);
      expect(press(MOD_K, input)).toBe(true);
      expect([input.selectionStart, input.selectionEnd]).toEqual([0, 4]);
    });
  });

  describe('loading', () => {
    it('shows a skeleton while an open waits for the chunk, then the panel', async () => {
      let release: () => void = () => undefined;
      const gate = new Promise<void>((resolve) => (release = resolve));
      await start({ load: gatedLoad(gate) });
      document.querySelector<HTMLButtonElement>('.tm-app-bar__menu')!.click();
      await tmSettle();
      expect(launcher().state()).toBe('browse');
      expect(await shell.isLauncherSkeletonShown()).toBe(true);
      expect(document.querySelector('.tm-app-bar__launcher-skeleton')!.matches(':popover-open')).toBe(true);
      release();
      const harness = await shell.openLauncher('menu');
      expect(await shell.isLauncherSkeletonShown()).toBe(false);
      expect(await harness.isInputFocused()).toBe(true);
    });

    it('closes an open whose chunk fails to load, reports it, and tries again on the next', async () => {
      let failing = true;
      await start({ load: gatedLoad(Promise.resolve(), () => failing) });
      document.querySelector<HTMLButtonElement>('.tm-app-bar__menu')!.click();
      await tmSettleUntil(() => launcher().state() === 'closed', 'the open closes');
      expect(await shell.isLauncherSkeletonShown()).toBe(false);
      expect(rig.telemetry.recent().some((item) => item.name === 'tm.error.captured' && item.props['source'] === 'tm-launcher')).toBe(true);
      failing = false;
      const harness = await shell.openLauncher('menu');
      expect(await harness.getState()).toBe('browse');
    });
  });
});

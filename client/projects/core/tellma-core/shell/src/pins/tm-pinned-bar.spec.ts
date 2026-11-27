// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { LiveAnnouncer } from '@angular/cdk/a11y';
import { type HarnessLoader, TestKey } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { TestBed } from '@angular/core/testing';
import { type TmLanguageDefinition, type TmLaunchDocs, TmPreferences, tmEnglish, tmPinsKey, type TmPinsDoc, TmUrls } from '@tellma/core';
import { TmPinnedBarHarness, TmTenantShellHarness } from '@tellma/core-testing';
import { TmToastStackHarness } from '@tellma/core-ui-testing';
import { page } from 'vitest/browser';

import { type TmAppRig, tmAppRig, tmNavigate, tmSettle, tmSettleUntil, TM_T17, TM_T17_DOCS } from '../../../src/routes/tm-app.testing.util';
import { TM_PIN_LIMITS } from './tm-pin-rules';
import { TM_PINNED_BAR_FONTS } from './tm-pinned-bar';
import { TmPins } from './tm-pins';
import { TM_PIN_OVERLAYS_LOAD, type TmPinOverlaysChunk } from './tm-pins-ui';

// The pinned bar in a whole test app, through its harness: the chips and
// their current styling, the measured fit (long Arabic labels, right to
// left, below md with the current pill first, across a resize, a pin's
// state change and a font swap, a font that loads late with no chip painted
// before the fit), the current pin in More, nothing moving on hover, focus
// or navigation from md up, the roving tab stop, folders and their menus,
// revoked pins and their menus, the pin control and its popover or toast,
// Alt+Shift+P, drag and drop, and Manage pins in every state of the bar.

/** The fonts the bar waits for, which a test loads when it chooses. */
class TmFakeFonts extends EventTarget {
  /** Whether every face has loaded. */
  status: FontFaceSetLoadStatus;
  /** Resolves once the faces load. */
  readonly ready: Promise<TmFakeFonts>;
  /** Loads the faces. */
  readonly load: () => void;

  /** Creates the fonts, loading or loaded. */
  constructor(status: FontFaceSetLoadStatus) {
    super();
    this.status = status;
    let resolve: (fonts: TmFakeFonts) => void = () => undefined;
    this.ready = status === 'loaded' ? Promise.resolve(this) : new Promise((done) => (resolve = done));
    this.load = () => {
      this.status = 'loaded';
      resolve(this);
    };
  }

  /** A face loaded late, as a swap does. */
  loaded(): void {
    this.dispatchEvent(new Event('loadingdone'));
  }
}

/** A pin of the credit review page with a query, so its stored title is its label. */
function pin(n: number, title = `Review ${n}`): { readonly id: string; readonly url: string; readonly title: string } {
  return { id: `a${String(n).padStart(7, '0')}`, url: `credit-review?q=${n}`, title };
}

/** A pins document of items. */
function doc(...items: readonly unknown[]): TmPinsDoc {
  return { v: 1, items: items as TmPinsDoc['items'] };
}

/** Twelve pins, which do not fit at 1280 px. */
const MANY = doc(...Array.from({ length: 12 }, (_, n) => pin(n + 1, `Monthly review number ${n + 1}`)));

/** Three pins, which fit. */
const FEW = doc(pin(1, 'Receivables'), pin(2, 'Payables'), pin(3, 'Cash'));

/** Long Arabic labels, which the chips cut at 120 px. */
const ARABIC_PINS = doc(
  pin(1, 'تقرير المبيعات الشهري للفروع الشمالية'),
  pin(2, 'الذمم المدينة المتأخرة أكثر من تسعين يومًا'),
  pin(3, 'مراجعة الائتمان للعملاء من الشركات'),
  pin(4, 'التدفقات النقدية المتوقعة للربع القادم'),
  pin(5, 'فواتير الموردين بانتظار الاعتماد النهائي'),
  pin(6, 'أرصدة المخزون حسب المستودع والفئة'),
  pin(7, 'تحليل هوامش الربح حسب خط الإنتاج'),
  pin(8, 'المصروفات التشغيلية مقارنة بالموازنة'),
  pin(9, 'سجل الأصول الثابتة والإهلاك المتراكم'),
);

/** Arabic, which falls back to English for every string it lacks. */
const ARABIC: TmLanguageDefinition = { code: 'ar', dir: 'rtl', strings: () => Promise.resolve({ tm: {}, tmUi: {} }) };

/** The distribution's strings. */
const STRINGS = { en: { Acme_CreditReview: 'Credit review', Acme_Approvals: 'Approvals' } };

/** Tenant 17's documents with stored pins, more preferences, and Arabic when asked. */
function docs(pins: TmPinsDoc, preferences: Readonly<Record<string, string>> = {}, arabic = false, state: TmLaunchDocs['tenant']['state'] = 'Active'): TmLaunchDocs {
  const { me, settings } = TM_T17_DOCS;
  return {
    ...TM_T17_DOCS,
    tenant: { ...TM_T17_DOCS.tenant, state },
    settings: arabic ? { ...settings, languages: [...settings.languages, { code: 'ar', nativeName: 'العربية', symbol: 'ع', isRightToLeft: true }] } : settings,
    me: {
      ...me,
      user: { ...me.user, ...(arabic && { preferredLanguage: 'ar' }) },
      preferences: { 'nav.pinned': JSON.stringify(pins), ...preferences },
    },
  };
}

/** The track's box and every shown item's, in pixels. */
function boxes(): { readonly track: DOMRect; readonly items: readonly DOMRect[] } {
  const track = document.querySelector('.tm-pinned-bar__track')!.getBoundingClientRect();
  const items = [...document.querySelectorAll('.tm-pinned-bar__list > li')].map((item) => item.getBoundingClientRect());
  return { track, items };
}

/** Whether every shown item lies inside the track. */
function fitsTrack(): boolean {
  const { track, items } = boxes();
  return items.length > 0 && items.every((item) => item.left >= track.left - 0.5 && item.right <= track.right + 0.5);
}

/** Each shown item's left edge and width, rounded to a tenth of a pixel. */
function geometry(): string[] {
  return boxes().items.map((item) => `${item.left.toFixed(1)}+${item.width.toFixed(1)}`);
}

/** Whether each shown item's width is its measured width. */
function widthsAreMeasured(): boolean {
  return [...document.querySelectorAll<HTMLElement>('.tm-pinned-bar__list > li')].every((item) => {
    const id = item.querySelector<HTMLElement>('[data-tm-pin]')!.dataset['tmPin']!;
    const measured = document.querySelector(`[data-tm-measure="${id}"]`)!.getBoundingClientRect().width;
    return Math.abs(item.getBoundingClientRect().width - measured) < 0.5;
  });
}

/** Waits a frame and settles, so what a resize observed has rendered. */
async function frame(): Promise<void> {
  await new Promise((resolve) => requestAnimationFrame(resolve));
  await tmSettle();
}

describe('the pinned bar', () => {
  let rig: TmAppRig;
  let loader: HarnessLoader;
  let bar: TmPinnedBarHarness;
  let fonts: TmFakeFonts | null;
  const viewport = [window.innerWidth, window.innerHeight] as const;
  const announced: string[] = [];

  afterEach(async () => {
    TestBed.resetTestingModule();
    document.querySelector('#tm-test-font-swap')?.remove();
    announced.length = 0;
    await page.viewport(...viewport);
  });

  /** How a test sets the app up. */
  interface TmStartOptions {
    /** The stored pins. */
    readonly pins?: TmPinsDoc;
    /** The viewport's width: 1280 px by default. */
    readonly width?: number;
    /** The URL: the credit review page with no query by default. */
    readonly url?: string;
    /** Whether the user works in Arabic. */
    readonly arabic?: boolean;
    /** The fonts' state, when the test drives them. */
    readonly fonts?: FontFaceSetLoadStatus;
    /** More stored preferences. */
    readonly preferences?: Readonly<Record<string, string>>;
    /** The tenant's state. */
    readonly state?: TmLaunchDocs['tenant']['state'];
    /** The overlays' loader, when not their own. */
    readonly overlays?: () => Promise<TmPinOverlaysChunk>;
    /** Whether to wait for the first fit. */
    readonly fitted?: boolean;
    /** Runs once the app's providers are up, before the first navigation. */
    readonly before?: () => void;
  }

  /** Starts the app and waits for the bar's first fit. */
  async function start(options: TmStartOptions = {}): Promise<void> {
    await page.viewport(options.width ?? 1280, 900);
    fonts = options.fonts === undefined ? null : new TmFakeFonts(options.fonts);
    rig = await tmAppRig({
      docs: docs(options.pins ?? MANY, options.preferences, options.arabic, options.state),
      languages: [tmEnglish(), ARABIC],
      strings: STRINGS,
      providers: [
        ...(fonts === null ? [] : [{ provide: TM_PINNED_BAR_FONTS, useValue: fonts }]),
        ...(options.overlays === undefined ? [] : [{ provide: TM_PIN_OVERLAYS_LOAD, useValue: options.overlays }]),
      ],
    });
    const announcer = TestBed.inject(LiveAnnouncer);
    vi.spyOn(announcer, 'announce').mockImplementation((message) => {
      announced.push(String(message));
      return Promise.resolve();
    });
    options.before?.();
    const router = await tmNavigate(options.url ?? '/17/credit-review');
    await tmSettleUntil(() => document.querySelector('tm-pinned-bar .tm-pinned-bar__control') !== null, 'the pinned bar renders');
    loader = TestbedHarnessEnvironment.documentRootLoader(router.fixture);
    bar = await loader.getHarness(TmPinnedBarHarness);
    if (options.fitted !== false) {
      await tmSettleUntil(() => document.querySelector('.tm-pinned-bar__list--fitted, .tm-pinned-bar__empty') !== null, 'the bar fits');
    }
  }

  /** Navigates the app and settles. */
  async function go(url: string): Promise<void> {
    await rig.router.navigateByUrl(url);
    await tmSettle();
  }

  /** The stored pins' ids, a folder's as `id[pin ids]`. */
  function storedIds(): string[] {
    return TestBed.inject(TmPreferences)
      .get(tmPinsKey)()
      .items.map((item) => ('folder' in item ? `${item.id}[${item.items.map((inner) => inner.id).join(' ')}]` : item.id));
  }

  /** The events of a name the app reported, by their properties. */
  const events = (name: string): unknown[] =>
    rig.telemetry
      .recent()
      .filter((item) => item.name === name)
      .map((item) => item.props);

  describe('chips', () => {
    it('shows the pins as links, the current one marked, and its controls', async () => {
      await start({ pins: FEW, url: '/17/credit-review?q=2' });
      expect(await bar.getName()).toBe('Pinned pages');
      expect(await bar.getChipLabels()).toEqual(['Receivables', 'Payables', 'Cash']);
      expect(await bar.hasMore()).toBe(false);
      const payables = await bar.getChip('Payables');
      expect([await payables.getKind(), await payables.getHref(), await payables.isCurrent(), await payables.isAriaCurrent()]).toEqual([
        'pin',
        '/17/credit-review?q=2',
        true,
        true,
      ]);
      expect(await (await bar.getChip('Cash')).isCurrent()).toBe(false);
      expect(await bar.getControl()).toEqual({ name: 'Edit pin', pinned: true, disabled: false, reason: null, keys: 'Alt+Shift+P' });
      expect(await bar.getManageName()).toBe('Manage pins');
      // The current chip fills the row; the others are 24 px.
      expect([(await payables.getSize()).height, (await (await bar.getChip('Cash')).getSize()).height]).toEqual([32, 24]);
    });

    it('shows the empty line, short below sm, with the controls', async () => {
      await start({ pins: doc() });
      expect(await bar.getEmptyText()).toBe('Nothing pinned yet. Use the pin button to pin this page.');
      expect(await bar.getControl()).toEqual({ name: 'Pin this page', pinned: false, disabled: false, reason: null, keys: 'Alt+Shift+P' });
      await page.viewport(400, 900);
      await frame();
      expect(await bar.getEmptyText()).toBe('Nothing pinned yet.');
    });

    it('opens a pin, reporting the navigation', async () => {
      await start({ pins: FEW });
      await (await bar.getChip('Cash')).click();
      await tmSettle();
      expect(rig.router.url).toBe('/17/credit-review?q=3');
      expect(await bar.getCurrentChipLabel()).toBe('Cash');
      expect(events('tm.shell.navigated')).toContainEqual({ source: 'pin' });
    });

    it('preloads the routes of the pins that open, once each, after its first render', async () => {
      const preloaded: string[] = [];
      await start({
        pins: doc(pin(1), { id: 'b0000001', url: 'approvals', title: 'Approvals' }, pin(2)),
        before: () => {
          const urls = TestBed.inject(TmUrls);
          const preload = urls.preload.bind(urls);
          vi.spyOn(urls, 'preload').mockImplementation((url) => {
            preloaded.push(url);
            return preload(url);
          });
        },
      });
      await tmSettle();
      expect(preloaded.filter((url) => url.startsWith('/17/credit-review?') || url === '/17/approvals')).toEqual([
        '/17/credit-review?q=1',
        '/17/credit-review?q=2',
      ]);
    });
  });

  describe('the fit', () => {
    it('shows the items that fit and More, inside the track, at their measured widths', async () => {
      await start();
      const labels = await bar.getChipLabels();
      expect(labels.at(-1)).toBe('More');
      expect(labels.length).toBeGreaterThan(2);
      expect(labels.length).toBeLessThan(13);
      expect(fitsTrack()).toBe(true);
      expect(widthsAreMeasured()).toBe(true);
      const more = await bar.openMenu('More');
      expect(await more.getItemLabels()).toEqual(MANY.items.slice(labels.length - 1).map((item) => (item as { title: string }).title));
    });

    it('fits long Arabic labels, cut at 120 px, right to left', async () => {
      await start({ pins: ARABIC_PINS, arabic: true });
      expect(document.documentElement.dir).toBe('rtl');
      expect(await bar.hasMore()).toBe(true);
      expect(fitsTrack()).toBe(true);
      expect(widthsAreMeasured()).toBe(true);
      // Every label is cut, and the first chip is at the inline start, the right.
      const labels = [...document.querySelectorAll<HTMLElement>('.tm-pinned-bar__list .tm-pinned-bar__label')];
      expect(labels.every((label) => label.getBoundingClientRect().width <= 120.5 && label.scrollWidth > label.clientWidth)).toBe(true);
      const { track, items } = boxes();
      expect(Math.abs(items[0].right - track.right)).toBeLessThan(1);
      expect(items[1].right).toBeLessThanOrEqual(items[0].left);
    });

    it('refits across a resize, showing fewer chips when narrower and more when wider', async () => {
      await start();
      const wide = (await bar.getChipLabels()).length;
      await page.viewport(900, 900);
      await frame();
      const narrow = (await bar.getChipLabels()).length;
      expect(narrow).toBeLessThan(wide);
      expect(fitsTrack()).toBe(true);
      await page.viewport(1280, 900);
      await frame();
      expect((await bar.getChipLabels()).length).toBe(wide);
      expect(fitsTrack()).toBe(true);
    });

    it("refits when a pin's state changes, its chip taking the lock", async () => {
      const pins = doc(pin(1), { id: 'b0000001', url: 'approvals', title: 'Approvals' }, pin(2), pin(3));
      await start({ pins });
      expect((await (await bar.getChip('Approvals')).getKind())).toBe('revoked');
      // The permission arrives: the next sync brings an access document that grants it.
      const { me } = docs(pins);
      const access = { ...me.access, securables: [{ resource: 'acme.Approval', action: 'Read', hasFilter: false }] };
      await rig.store.put(TM_T17, 'me', { tag: null, savedAt: Date.now(), data: { ...me, access } });
      await tmSettle();
      expect(await (await bar.getChip('Approvals')).getKind()).toBe('pin');
      expect(widthsAreMeasured()).toBe(true);
      expect(fitsTrack()).toBe(true);
    });

    it('re-reads the widths when a face loads late', async () => {
      await start({ fonts: 'loaded' });
      const before = geometry();
      // A wider face takes over, as a swap does: the widths follow once it reports in.
      const style = document.createElement('style');
      style.id = 'tm-test-font-swap';
      style.textContent = 'tm-pinned-bar .tm-pinned-bar { font-family: "Courier New", monospace; letter-spacing: 1px; }';
      document.head.append(style);
      fonts!.loaded();
      await frame();
      expect(geometry()).not.toEqual(before);
      expect(widthsAreMeasured()).toBe(true);
      expect(fitsTrack()).toBe(true);
    });

    it('paints no chip before the fonts load, then fits', async () => {
      await start({ fonts: 'loading', fitted: false });
      await tmSettle();
      const list = document.querySelector<HTMLElement>('.tm-pinned-bar__list')!;
      expect(await bar.isFitted()).toBe(false);
      expect(getComputedStyle(list).visibility).toBe('hidden');
      fonts!.load();
      await tmSettleUntil(() => list.classList.contains('tm-pinned-bar__list--fitted'), 'the bar fits');
      expect(getComputedStyle(list).visibility).toBe('visible');
      expect(fitsTrack()).toBe(true);
    });

    it('marks More current while it holds the current pin, and its menu marks the pin', async () => {
      await start({ url: '/17/credit-review?q=12' });
      const more = await bar.getChip('More');
      expect(await more.isCurrent()).toBe(true);
      expect(await bar.getTabStopLabel()).toBe('More');
      await bar.openMenu('More');
      expect(document.querySelector('.tm-pin-overlays__menu [aria-current="page"]')?.textContent).toContain('Monthly review number 12');
    });

    it('moves nothing on hover, focus or navigation from md up', async () => {
      await start({ url: '/17/credit-review?q=1' });
      const before = geometry();
      const chips = await bar.getChips();
      await chips[1].hover();
      await chips[2].focus();
      await tmSettle();
      expect(geometry()).toEqual(before);
      await go('/17/credit-review?q=3');
      expect(await bar.getCurrentChipLabel()).toBe('Monthly review number 3');
      expect(geometry()).toEqual(before);
      // The current pin in More: More takes the styling, and still nothing moves.
      await go('/17/credit-review?q=12');
      expect(geometry()).toEqual(before);
    });

    it('shows pills below md, the current one first', async () => {
      await start({ width: 600, pins: FEW, url: '/17/credit-review?q=3' });
      expect(await bar.getChipLabels()).toEqual(['Cash', 'Receivables', 'Payables']);
      const cash = await bar.getChip('Cash');
      expect([await cash.isCurrent(), (await cash.getSize()).height]).toEqual([true, 24]);
      // A pill: no bar below it, the full chip's height.
      expect(getComputedStyle(document.querySelector('.tm-pinned-bar__chip--current')!).boxShadow).toBe('none');
      await go('/17/credit-review?q=2');
      expect(await bar.getChipLabels()).toEqual(['Payables', 'Receivables', 'Cash']);
      expect(fitsTrack()).toBe(true);
    });
  });

  describe('focus', () => {
    it('is one tab stop, on the current pin, else the first, moved by the arrows, Home and End', async () => {
      await start({ pins: FEW, url: '/17/credit-review?q=2' });
      expect(await bar.getTabStopLabel()).toBe('Payables');
      const payables = await bar.getChip('Payables');
      await payables.focus();
      await payables.pressKey(TestKey.RIGHT_ARROW);
      expect(await bar.getFocusedChipLabel()).toBe('Cash');
      expect(await bar.getTabStopLabel()).toBe('Cash');
      await (await bar.getChip('Cash')).pressKey(TestKey.RIGHT_ARROW);
      expect(await bar.getFocusedChipLabel()).toBe('Cash');
      await (await bar.getChip('Cash')).pressKey(TestKey.HOME);
      expect(await bar.getFocusedChipLabel()).toBe('Receivables');
      await (await bar.getChip('Receivables')).pressKey(TestKey.END);
      expect(await bar.getFocusedChipLabel()).toBe('Cash');
      await (await bar.getChip('Cash')).pressKey(TestKey.LEFT_ARROW);
      expect(await bar.getFocusedChipLabel()).toBe('Payables');
      // Focus leaves the bar: Tab enters on the current pin again.
      (document.querySelector('.tm-app-bar__menu') as HTMLElement).focus();
      await tmSettle();
      expect(await bar.getTabStopLabel()).toBe('Payables');
      await go('/17/credit-review');
      expect(await bar.getTabStopLabel()).toBe('Receivables');
    });

    it('moves by direction right to left, More last', async () => {
      await start({ pins: ARABIC_PINS, arabic: true });
      const chips = await bar.getChips();
      await chips[0].focus();
      await chips[0].pressKey(TestKey.LEFT_ARROW);
      expect(await chips[1].isFocused()).toBe(true);
      await chips[1].pressKey(TestKey.RIGHT_ARROW);
      expect(await chips[0].isFocused()).toBe(true);
      await chips[0].pressKey(TestKey.END);
      expect(await bar.getFocusedChipLabel()).toBe('More');
    });
  });

  describe('folders', () => {
    const FILED = doc(pin(1, 'Receivables'), { id: 'f0000001', folder: 'Daily', items: [pin(2, 'Payables'), pin(3, 'Cash')] }, { id: 'f0000002', folder: 'Empty', items: [] });

    it("shows a folder as a menu chip after a rule, its menu listing its pins, the current pin's folder current", async () => {
      await start({ pins: FILED, url: '/17/credit-review?q=3' });
      expect(await bar.getChipLabels()).toEqual(['Receivables', 'Daily', 'Empty']);
      const daily = await bar.getChip('Daily');
      expect([await daily.getKind(), await daily.isCurrent(), await daily.isAriaCurrent()]).toEqual(['folder', true, false]);
      expect(document.querySelector('[data-tm-pin="f0000001"]')!.closest('li')!.classList.contains('tm-pinned-bar__item--separated')).toBe(true);
      expect(document.querySelector('[data-tm-pin="f0000002"]')!.closest('li')!.classList.contains('tm-pinned-bar__item--separated')).toBe(false);
      const menu = await bar.openMenu('Daily');
      expect(await daily.isExpanded()).toBe(true);
      expect(await menu.getItemLabels()).toEqual(['Payables', 'Cash']);
      await menu.clickItem('Payables');
      await tmSettle();
      expect(rig.router.url).toBe('/17/credit-review?q=2');
      const empty = await bar.openMenu('Empty');
      expect(await empty.getItemLabels()).toEqual(['No pages in this folder']);
    });

    it('opens a folder with Down', async () => {
      await start({ pins: FILED });
      const daily = await bar.getChip('Daily');
      await daily.focus();
      await daily.pressKey(TestKey.DOWN_ARROW);
      await tmSettleUntil(() => document.querySelector('.tm-pin-overlays__menu') !== null, 'the menu opens');
      expect(await daily.isExpanded()).toBe(true);
    });
  });

  describe('revoked pins', () => {
    const REVOKED = doc(pin(1, 'Receivables'), { id: 'b0000001', url: 'approvals', title: 'Approvals' }, { id: 'b0000002', url: 'gone', title: 'Gone' }, pin(2, 'Cash'));

    it('shows a locked chip whose menu says why and unpins, focus going on along the bar', async () => {
      await start({ pins: REVOKED });
      const approvals = await bar.getChip('Approvals');
      expect([await approvals.getKind(), await approvals.getName()]).toEqual(['revoked', 'Approvals, no access']);
      expect(await (await bar.getChip('Gone')).getName()).toBe('Gone, not available');
      await approvals.focus();
      const menu = await bar.openMenu('Approvals');
      expect(await menu.getHeaderText()).toBe('You no longer have access to this page.');
      expect(await menu.getItemLabels()).toEqual(['Unpin']);
      await menu.clickItem('Unpin');
      await tmSettle();
      expect(storedIds()).toEqual(['a0000001', 'b0000002', 'a0000002']);
      expect(await bar.getFocusedChipLabel()).toBe('Gone');
      expect(announced).toContain('Unpinned Approvals');
      expect(events('tm.shell.pins-changed')).toEqual([{ op: 'remove', via: 'menu', count: '1-5' }]);
      const gone = await bar.openMenu('Gone');
      expect(await gone.getHeaderText()).toBe("This page isn't available.");
    });
  });

  describe('the pin control', () => {
    it('pins the page and opens the Pinned popover when its chip shows, which renames and files it', async () => {
      await start({ pins: FEW });
      await bar.clickControl();
      await tmSettleUntil(() => document.querySelector('.tm-pin-overlays__popover') !== null, 'the popover opens');
      const popover = await bar.openPopover();
      expect(await popover.getAriaLabel()).toBe('Pinned');
      expect(await bar.getChipLabels()).toEqual(['Receivables', 'Payables', 'Cash', 'Credit review']);
      expect(announced).toContain('Pinned Credit review');
      expect(await bar.getControl()).toMatchObject({ name: 'Edit pin', pinned: true });
      // The name field takes focus, its text selected.
      const field = document.querySelector<HTMLInputElement>('.tm-pin-overlays__form input')!;
      expect(document.activeElement).toBe(field);
      expect([field.selectionStart, field.selectionEnd]).toEqual([0, 'Credit review'.length]);
      field.value = 'My review';
      field.dispatchEvent(new Event('input'));
      document.querySelector<HTMLFormElement>('.tm-pin-overlays__form')!.requestSubmit();
      await tmSettle();
      expect(await bar.getChipLabels()).toEqual(['Receivables', 'Payables', 'Cash', 'My review']);
      expect(document.querySelector('.tm-pin-overlays__popover')).toBeNull();
      expect(document.activeElement).toBe(document.querySelector('.tm-pinned-bar__control'));
      expect(events('tm.shell.pins-changed')).toEqual([
        { op: 'add', via: 'control', count: '1-5' },
        { op: 'rename', via: 'popover', count: '1-5' },
      ]);
    });

    it('says with a toast once that a pin went into More, moving no chip', async () => {
      await start();
      const before = geometry();
      const labels = await bar.getChipLabels();
      await bar.clickControl();
      await tmSettle();
      expect(geometry()).toEqual(before);
      expect(await bar.getChipLabels()).toEqual(labels);
      const toasts = await loader.getHarness(TmToastStackHarness);
      expect(await Promise.all((await toasts.getToasts()).map(async (toast) => toast.getTitle()))).toEqual(['Pinned Credit review.']);
      expect(document.querySelector('.tm-pin-overlays__popover')).toBeNull();
    });

    it('opens Edit pin on a pinned page, whose Remove unpins it', async () => {
      await start({ pins: FEW, url: '/17/credit-review?q=1' });
      const popover = await bar.openPopover();
      expect(await popover.getAriaLabel()).toBe('Edit pin');
      [...document.querySelectorAll<HTMLButtonElement>('.tm-pin-overlays__actions button')].find((button) => button.textContent?.trim() === 'Remove')!.click();
      await tmSettle();
      expect(storedIds()).toEqual(['a0000002', 'a0000003']);
      expect(announced).toContain('Unpinned Receivables');
      expect(await bar.getControl()).toMatchObject({ name: 'Pin this page', pinned: false });
    });

    it('pins and unpins with Alt+Shift+P, focus going on from the chip it held', async () => {
      await start({ pins: FEW, url: '/17/credit-review?q=2' });
      const payables = await bar.getChip('Payables');
      await payables.focus();
      document.activeElement!.dispatchEvent(new KeyboardEvent('keydown', { key: 'P', code: 'KeyP', altKey: true, shiftKey: true, bubbles: true, cancelable: true }));
      await tmSettle();
      expect(storedIds()).toEqual(['a0000001', 'a0000003']);
      expect(await bar.getFocusedChipLabel()).toBe('Cash');
      expect(announced).toContain('Unpinned Payables');
      document.activeElement!.dispatchEvent(new KeyboardEvent('keydown', { key: 'P', code: 'KeyP', altKey: true, shiftKey: true, bubbles: true, cancelable: true }));
      await tmSettle();
      expect(storedIds()).toHaveLength(3);
      expect(events('tm.shell.pins-changed')).toEqual([
        { op: 'remove', via: 'shortcut', count: '1-5' },
        { op: 'add', via: 'shortcut', count: '1-5' },
      ]);
    });

    it('is disabled with a reason at the cap and while the tenant is frozen, and inert where nothing can be pinned', async () => {
      const full = doc(...Array.from({ length: TM_PIN_LIMITS.pins }, (_, n) => pin(n + 1)));
      await start({ pins: full });
      expect(await bar.getControl()).toMatchObject({ disabled: true, reason: "You've pinned 30 pages. Unpin one to add another." });
      TestBed.resetTestingModule();
      await start({ pins: FEW, state: 'Frozen' });
      expect(await bar.getControl()).toMatchObject({ disabled: true, reason: 'Acme is under maintenance.' });
      TestBed.resetTestingModule();
      await start({ pins: FEW, url: '/17/nothing-here' });
      expect(await bar.getControl()).toMatchObject({ disabled: true, reason: null });
    });

    it('acts on a press made before the overlays land', async () => {
      let release: () => void = () => undefined;
      const gate = new Promise<void>((resolve) => (release = resolve));
      await start({ pins: twoFolders(), overlays: () => gate.then(() => import('./tm-pin-overlays')) });
      const daily = await bar.getChip('Daily');
      await daily.click();
      await daily.click();
      await (await bar.getChip('Weekly')).click();
      release();
      await tmSettleUntil(() => document.querySelector('.tm-pin-overlays__menu') !== null, 'the menu opens');
      expect(await (await bar.getChip('Weekly')).isExpanded()).toBe(true);
      expect(await daily.isExpanded()).toBe(false);
    });
  });

  describe('drag and drop', () => {
    it('reorders a chip along the bar and files a pin in a folder, announcing each', async () => {
      await start({ pins: doc(pin(1, 'Receivables'), pin(2, 'Payables'), { id: 'f0000001', folder: 'Daily', items: [] }) });
      await tmSettleUntil(() => document.querySelector('tm-pin-overlays') !== null, 'the overlays land');
      const chip = (id: string): HTMLElement => document.querySelector<HTMLElement>(`[data-tm-pin="${id}"]`)!;
      /** Drags a chip over a point of another and drops it there. */
      const drag = (from: string, to: string, at: (rect: DOMRect) => number): void => {
        const data = new DataTransfer();
        const target = chip(to);
        const rect = target.getBoundingClientRect();
        const point = { clientX: at(rect), clientY: rect.top + rect.height / 2, bubbles: true, cancelable: true, dataTransfer: data };
        chip(from).dispatchEvent(new DragEvent('dragstart', point));
        target.dispatchEvent(new DragEvent('dragover', point));
        target.dispatchEvent(new DragEvent('drop', point));
        chip(from).dispatchEvent(new DragEvent('dragend', point));
      };
      drag('a0000002', 'a0000001', (rect) => rect.left + 2);
      await tmSettle();
      expect(storedIds()).toEqual(['a0000002', 'a0000001', 'f0000001[]']);
      expect(announced).toContain('Moved Payables, position 1 of 3');
      drag('a0000001', 'f0000001', (rect) => rect.left + rect.width / 2);
      await tmSettle();
      expect(storedIds()).toEqual(['a0000002', 'f0000001[a0000001]']);
      expect(announced).toContain('Moved Receivables to Daily');
      expect(events('tm.shell.pins-changed')).toEqual([
        { op: 'move', via: 'drag', count: '1-5' },
        { op: 'move', via: 'drag', count: '1-5' },
      ]);
    });
  });

  describe('Manage pins', () => {
    for (const [state, pins] of [
      ['empty', doc()],
      ['fitting', FEW],
      ['overflowing', MANY],
    ] as const) {
      it(`opens the dialog from a bar that is ${state}`, async () => {
        await start({ pins });
        const dialog = await bar.openManagePins();
        expect(await dialog.getTitle()).toBe('Manage pins');
        await dialog.close();
        await tmSettle();
        expect(document.querySelector('tm-manage-pins-dialog')).toBeNull();
      });
    }

    it('is gone with the bar while it is hidden, which the tenant shell does not render', async () => {
      await start({ pins: FEW });
      TestBed.inject(TmPins).setVisible(false);
      await tmSettle();
      const shell = await loader.getHarness(TmTenantShellHarness);
      expect(await shell.getPinnedBar()).toBeNull();
    });
  });
});

/** Two folders, for the presses made before the overlays land. */
function twoFolders(): TmPinsDoc {
  return doc(pin(1, 'Receivables'), { id: 'f0000001', folder: 'Daily', items: [pin(2)] }, { id: 'f0000002', folder: 'Weekly', items: [pin(3)] });
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import type { ComponentFixture } from '@angular/core/testing';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import {
  TM_NOW,
  TmAppVersion,
  type TmLanguageDefinition,
  type TmLaunchDocs,
  TmLocalization,
  tmEnglish,
  TmRequestLog,
  TmSupport,
  TmTabBus,
  ɵtmDiagnosticsLine,
  type ɵTmSupportSnapshot,
} from '@tellma/core';
import { TmDiagnosticsPanelHarness } from '@tellma/core-testing';
import type { TmPageContextSnapshot } from '@tellma/core/page-snapshot';
import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';
import { TmModal } from '@tellma/core-ui/modal';
import { TmToasts } from '@tellma/core-ui/toast';
import { page } from 'vitest/browser';

import { TM_T17_DOCS, tmAppRig, type TmAppRigOptions, tmNavigate, tmSettle, tmSettleUntil } from '../../src/routes/tm-app.testing.util';
import { TmDiagnosticsPanel } from './tm-diagnostics-panel';

// The Diagnostics panel as a user meets it in the About dialog: the header,
// the request grid newest first with the slow and failed switch, a row's
// line copied on activation, a pending request's duration ticking, the
// narrow layout's folded cells, and the three buttons; and the panel in a
// md dialog at a desktop's and a phone's width, in English and in Arabic,
// where the grid keeps to the dialog's body and Reference is in reach of
// the grid's own scroller.

/** The panel in a page of its own. */
@Component({
  imports: [TmDiagnosticsPanel],
  template: `<tm-diagnostics [reportable]="reportable()" [snapshot]="snapshot()" />`,
})
class TmPanelHost {
  /** Whether the panel offers Report a problem. */
  readonly reportable = signal(true);
  /** The support dialog's snapshot, if any. */
  readonly snapshot = signal<ɵTmSupportSnapshot<TmPageContextSnapshot> | undefined>(undefined);
}

/** The test's clock, which the app's TM_NOW reads. */
let clock = Date.parse('2026-10-20T11:02:40Z');

/** What a test drives. */
interface TmPanelRig {
  /** The panel's harness. */
  readonly panel: TmDiagnosticsPanelHarness;
  /** The host's fixture. */
  readonly fixture: ComponentFixture<TmPanelHost>;
}

/** Arabic, offered beside English, with no strings of its own. */
const ARABIC: TmLanguageDefinition = { code: 'ar', dir: 'rtl', strings: () => Promise.resolve({ tm: {}, tmUi: {} }) };

/** Tenant 17 in English and Arabic. */
const BILINGUAL: TmLaunchDocs = {
  ...TM_T17_DOCS,
  settings: {
    ...TM_T17_DOCS.settings,
    languages: [
      { code: 'en', nativeName: 'English', symbol: 'En', isRightToLeft: false },
      { code: 'ar', nativeName: 'العربية', symbol: 'ع', isRightToLeft: true },
    ],
  },
};

/** Starts the app in tenant 17 with three requests in the log. */
async function startApp(options: TmAppRigOptions = {}): Promise<void> {
  clock = Date.parse('2026-10-20T11:02:40Z');
  await tmAppRig({ ...options, providers: [{ provide: TM_NOW, useValue: () => clock }] });
  await tmNavigate('/17');
  await tmSettle();
  // --- The log, oldest first: a quick call, a slow failure, a pending save ---
  const log = TestBed.inject(TmRequestLog);
  const begin = (traceId: string, route: string): ReturnType<TmRequestLog['begin']> =>
    log.begin({ traceId, route, tenantId: 17, method: 'POST', background: false });
  const quick = begin('9e8d7c6b5a4938271605f4e3d2c1b0a9', 'client/sync');
  clock += 120;
  quick.end({ outcome: 'ok', status: 200 });
  clock += 1000;
  const failed = begin('0af7651916cd43dd8448eb211c80319c', 'centers/query');
  clock += 4810;
  failed.end({ outcome: 'error', status: 500, code: 'server.internal' });
  begin('4bf92f3577b34da6a3ce929d0e0e4736', 'centers/save');
  clock += 6200;
}

/** Starts the app in tenant 17 with three requests in the log, and renders the panel. */
async function setUp(): Promise<TmPanelRig> {
  await startApp();
  const fixture = TestBed.createComponent(TmPanelHost);
  await tmSettleUntil(() => fixture.nativeElement.querySelector('tm-grid') !== null);
  const panel = await TestbedHarnessEnvironment.harnessForFixture(fixture, TmDiagnosticsPanelHarness);
  return { panel, fixture };
}

describe('TmDiagnosticsPanel', () => {
  afterEach(async () => {
    await page.viewport(1440, 900);
  });

  it('names the builds and where the tab stands', async () => {
    const { panel } = await setUp();
    expect(await panel.getHeader()).toEqual({
      'UI version': TestBed.inject(TmAppVersion).ui || 'Unknown',
      'Server version': 'Unknown',
      'Offline version': 'None',
      Tenant: '17',
      Tab: TestBed.inject(TmTabBus).tabId,
      Connection: 'Online',
      Leader: 'Another tab',
      'Live updates': 'Connecting',
    });
  });

  it('lists the requests newest first, and keeps the slow and failed ones on the switch', async () => {
    const { panel } = await setUp();
    const grid = await panel.getGrid();
    expect(await grid.getHeaderTexts()).toEqual(['Time', 'Duration', 'Outcome', 'Request', 'Reference']);
    expect(await grid.getRenderedRowCount()).toBe(3);
    expect([await grid.getCellText(0, 2), await grid.getCellText(1, 2), await grid.getCellText(2, 2)]).toEqual(['Pending', 'Failed', 'OK']);
    expect(await grid.getCellText(0, 1)).toBe('6.2 s');
    expect(await grid.getCellText(1, 1)).toBe('4.81 s');
    expect(await grid.getCellText(1, 3)).toBe('POST centers/query');
    expect(await grid.getCellText(1, 4)).toContain('0af7651916cd43dd8448eb211c80319c');

    await panel.toggleFlaggedOnly();
    expect(await panel.isFlaggedOnly()).toBe(true);
    // The pending save turns slow on the log's own 3 s timer, not yet run, so only the failure stays.
    expect(await grid.getRenderedRowCount()).toBe(1);
    expect(await grid.getCellText(0, 3)).toBe('POST centers/query');
  });

  it("copies a row's line of the diagnostics text when it is activated, and toasts the outcome", async () => {
    const { panel } = await setUp();
    const write = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
    const show = vi.spyOn(TestBed.inject(TmToasts), 'show');
    await (await panel.getGrid()).activateRow(1);
    const record = TestBed.inject(TmRequestLog).recent()[1]!;
    expect(write).toHaveBeenCalledWith(ɵtmDiagnosticsLine(record, clock));
    await tmSettleUntil(() => show.mock.calls.length > 0);
    expect(show).toHaveBeenCalledWith(expect.objectContaining({ tone: 'success', title: 'Request copied.' }));
  });

  it("ticks a pending request's duration each second", async () => {
    const { panel } = await setUp();
    const grid = await panel.getGrid();
    expect(await grid.getCellText(0, 1)).toBe('6.2 s');
    clock += 2000;
    let text = '';
    await tmSettleUntil(() => {
      void grid.getCellText(0, 1).then((cell) => (text = cell));
      return text === '8.2 s';
    });
  });

  it('folds the start and the outcome into the duration cell below md', async () => {
    await page.viewport(390, 800);
    const { panel } = await setUp();
    const grid = await panel.getGrid();
    expect(await grid.getHeaderTexts()).toEqual(['Duration', 'Request', 'Reference']);
    expect(await grid.getCellText(1, 0)).toMatch(/^4\.81 s\s*\d\d:\d\d:\d\d · Failed$/);
  });

  it('copies the diagnostics, downloads the file once at a time without page data, and opens the support dialog', async () => {
    const { panel, fixture } = await setUp();
    const support = TestBed.inject(TmSupport);
    const copy = vi.spyOn(TestBed.inject(TmRequestLog), 'copy').mockImplementation(() => undefined);
    // The facade's download stays pending until the test lets it start.
    let started!: () => void;
    const download = vi.spyOn(support, 'downloadDump').mockImplementation(() => new Promise<void>((done) => (started = done)));
    const open = vi.spyOn(support, 'open').mockImplementation(() => undefined);
    expect(await panel.getButtons()).toEqual(['Copy diagnostics', 'Download diagnostic file', 'Report a problem']);

    await (await panel.getButton('Copy diagnostics')).click();
    await (await panel.getButton('Report a problem')).click();
    expect(copy).toHaveBeenCalledWith('button');
    expect(open).toHaveBeenCalledTimes(1);

    // --- Download: pending until the file starts, and a second press meanwhile does nothing ---
    const button = await panel.getButton('Download diagnostic file');
    await button.click();
    expect(await button.isPending()).toBe(true);
    await button.click();
    expect(download).toHaveBeenCalledTimes(1);
    started();
    await tmSettle();
    expect(await button.isPending()).toBe(false);

    // --- In the support dialog: no Report a problem, and the file from the dialog's snapshot ---
    const clicks: string[] = [];
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      clicks.push(this.download);
    });
    // The prototype is the page's, which later files share: restore it with this test.
    onTestFinished(() => click.mockRestore());
    fixture.componentInstance.reportable.set(false);
    fixture.componentInstance.snapshot.set({
      at: clock,
      withData: { current: null, overlays: [] },
      withoutData: { current: null, overlays: [] },
      url: '/17',
      routeTemplate: '/:t',
      historyLength: 1,
    });
    await tmSettle();
    expect(await panel.getButtons()).toEqual(['Copy diagnostics', 'Download diagnostic file']);
    await (await panel.getButton('Download diagnostic file')).click();
    await tmSettleUntil(() => clicks.length === 1);
    expect(clicks[0]).toMatch(/-diagnostics-\d{8}-\d{6}\.json$/);
    expect(download).toHaveBeenCalledTimes(1);
  });
});

describe('TmDiagnosticsPanel in a md dialog', () => {
  let tokens: HTMLStyleElement;

  // The design tokens, which size the dialog and the grid's tracks.
  beforeEach(() => {
    tokens = document.createElement('style');
    tokens.textContent = tmEmitCss(tmTokensDefault);
    document.head.appendChild(tokens);
  });

  afterEach(async () => {
    tokens.remove();
    await page.viewport(1440, 900);
  });

  for (const language of ['en', 'ar'] as const) {
    for (const [width, height] of [
      [1440, 900],
      [390, 844],
    ] as const) {
      it(`keeps the grid inside the dialog's body at ${width} px in ${language}, with Reference in reach of the grid's own scroller`, async () => {
        await page.viewport(width, height);
        await startApp({ docs: BILINGUAL, languages: [tmEnglish(), ARABIC] });
        if (language === 'ar') {
          const l10n = TestBed.inject(TmLocalization);
          l10n.setLanguage('ar');
          await tmSettleUntil(() => l10n.language() === 'ar' && document.documentElement.dir === 'rtl', 'the language switches');
        }
        TestBed.inject(TmModal).open(TmDiagnosticsPanel, { size: 'md', title: 'Diagnostics' });
        await tmSettleUntil(() => document.querySelector('.tm-modal__body tm-grid [role="columnheader"]') !== null, 'the grid renders');
        await tmSettle();

        // --- The grid takes the body's width, and the body never scrolls sideways ---
        const body = document.querySelector<HTMLElement>('.tm-modal__body')!;
        const grid = body.querySelector<HTMLElement>('tm-grid')!;
        const padding = getComputedStyle(body);
        const content = body.clientWidth - parseFloat(padding.paddingInlineStart) - parseFloat(padding.paddingInlineEnd);
        expect(grid.getBoundingClientRect().width).toBeLessThanOrEqual(content + 0.5);
        expect(body.scrollWidth).toBe(body.clientWidth);

        // --- Reference, the last column, lies inside the grid's scroller once it scrolls to its end ---
        const scroller = grid.querySelector<HTMLElement>('.tm-grid__scroller')!;
        const headers = [...scroller.querySelectorAll<HTMLElement>('[role="columnheader"]')];
        const reference = headers.at(-1)!;
        expect(reference.textContent?.trim()).toBe('Reference');
        if (width >= 576) {
          // At the dialog's md width the narrow columns fit, so nothing scrolls.
          expect(scroller.scrollWidth).toBeLessThanOrEqual(scroller.clientWidth);
        }
        scroller.scrollTo({ left: language === 'ar' ? -scroller.scrollWidth : scroller.scrollWidth, behavior: 'instant' });
        await tmSettle();
        // The scroller inside its borders (a headless browser's scrollbars take no room), less the row
        // headers' width, which the body's rows keep at its inline start.
        const box = scroller.getBoundingClientRect();
        const border = parseFloat(getComputedStyle(scroller).borderInlineStartWidth);
        const rowHeaders = headers[0]!.getBoundingClientRect().width;
        const [start, end] = [box.left + border, box.right - border];
        const [left, right] = language === 'ar' ? [start, end - rowHeaders] : [start + rowHeaders, end];
        const cell = reference.getBoundingClientRect();
        expect(cell.left).toBeGreaterThanOrEqual(left - 0.5);
        expect(cell.right).toBeLessThanOrEqual(right + 0.5);
        expect(body.scrollLeft).toBe(0);
      });
    }
  }
});

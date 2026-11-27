// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, computed, signal, type Type } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import {
  TmLocalization,
  tmFeature,
  tmWidget,
  type TmDashboardWidgetModel,
  type TmFeature,
  type TmLazyComponent,
  type TmClientMetadata,
  type TmReportArgs,
} from '@tellma/core';

import { tmAppRig, tmAssignmentsTestRoute, tmGatedCoreWidget, tmNavigate, tmSettle } from '../../src/routes/tm-app.testing.util';
import { TmDashboardGrid } from './tm-dashboard-grid';
import { injectTmWidget, type TmWidgetContext } from './tm-widget-context';

// The grid over a test app whose feature declares three widget types: one
// that renders, one whose permission the user lacks, and one whose body's
// chunk the spec controls. Each tile resolves its type, title and access
// against the declarations, renders in reading order at its placement, and
// shows its body or the state that stands in for it, which the grid's tile
// states report. A fourth, fixture type with a status line reads its
// context through injectTmWidget() and reports its runs, which the status
// line and the tile states show; its menu's Refresh and the grid's
// refreshAll tick it.

/** A widget body, which names itself. */
@Component({
  selector: 'tm-cash-body',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<p class="cash">Cash 42</p>',
})
class TmCashBody {}

/** The fixture type's last created context, which the spec drives. */
let reporting: TmWidgetContext | null = null;

/** The fixture type's body: it writes out what its context gives it, and hands the spec its context. */
@Component({
  selector: 'tm-reporting-body',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<p class="reporting">{{ text() }}</p>',
})
class TmReportingBody {
  readonly #widget = injectTmWidget();

  /** The context, written out. */
  protected readonly text = computed(() => {
    const size = this.#widget.size();
    return [
      this.#widget.key,
      this.#widget.type,
      String(size.w) + 'x' + String(size.h),
      JSON.stringify(this.#widget.settings()),
      JSON.stringify(this.#widget.parameters()),
      this.#widget.mode(),
      String(this.#widget.tick().generation),
    ].join(' ');
  });

  /** Hands the spec the context. */
  constructor() {
    reporting = this.#widget;
  }
}

/** A pending load the spec settles. */
interface TmDeferred<T> {
  /** The load. */
  readonly promise: Promise<T>;
  /** Settles it with a value. */
  readonly resolve: (value: T) => void;
  /** Settles it with a failure. */
  readonly reject: (reason: unknown) => void;
}

/**
 * A load the spec settles.
 *
 * @returns The load and its settlers.
 */
function deferred<T>(): TmDeferred<T> {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((done, fail) => {
    resolve = done;
    reject = fail;
  });
  return { promise, resolve, reject };
}

/** The renderable type's body. */
const loadCash = vi.fn((): Promise<Type<unknown>> => Promise.resolve(TmCashBody));

/** The denied type's body, which never loads. */
const loadLocked = vi.fn((): Promise<Type<unknown>> => Promise.resolve(TmCashBody));

/**
 * The test feature: the three widget types. A body once loaded stays loaded
 * for its loader, so each spec hands the controlled type a loader of its own.
 *
 * @param loadFlaky - The controlled type's body.
 * @returns The feature.
 */
function acme(loadFlaky: TmLazyComponent): TmFeature {
  return tmFeature({
    key: 'acme',
    widgets: [
      tmWidget('acme.cash-position', {
        labelKey: 'Acme_Cash',
        icon: 'chart-column',
        component: loadCash,
        defaultSize: { w: 4, h: 6 },
        minSize: { w: 2, h: 3 },
      }),
      tmWidget('acme.approvals', {
        labelKey: 'Acme_Approvals',
        icon: 'list',
        component: loadLocked,
        defaultSize: { w: 4, h: 6 },
        minSize: { w: 2, h: 3 },
        requires: ['acme.Approval', 'Read'],
      }),
      tmWidget('acme.reporting', {
        labelKey: 'Acme_Reporting',
        icon: 'chart-column',
        component: () => Promise.resolve(TmReportingBody),
        defaultSize: { w: 4, h: 6 },
        minSize: { w: 2, h: 3 },
        status: true,
      }),
      tmWidget('acme.flaky', {
        labelKey: 'Acme_Flaky',
        icon: 'list',
        component: loadFlaky,
        defaultSize: { w: 4, h: 6 },
        minSize: { w: 2, h: 3 },
      }),
    ],
  });
}

/** The grid in a host whose inputs the spec sets. */
@Component({
  imports: [TmDashboardGrid],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<tm-dashboard-grid
    [widgets]="widgets()"
    label="Cash overview"
    [denied]="denied()"
    [parameters]="parameters()"
    [presenting]="presenting()"
  />`,
})
class TmGridHost {
  /** The dashboard's supplied arguments. */
  readonly parameters = signal<TmReportArgs>({});
  /** Whether the dashboard is presented. */
  readonly presenting = signal(false);
  /** The grid's widgets. */
  readonly widgets = signal<readonly TmDashboardWidgetModel[]>([]);
  /** Whether a denied tile shows. */
  readonly denied = signal<'show' | 'hide'>('hide');
}

/**
 * Renders the grid over the test app.
 *
 * @param widgets - The grid's widgets.
 * @param denied - Whether a denied tile shows.
 * @param loadFlaky - The controlled type's body.
 * @param slot - The active content slot, when not the first.
 * @param app - More features, the Core chunk when not the route test metadata, and a tenant route to open first, which the lists' links need.
 * @returns The host's fixture.
 */
async function render(
  widgets: readonly TmDashboardWidgetModel[],
  denied: 'show' | 'hide' = 'hide',
  loadFlaky: TmLazyComponent = () => Promise.resolve(TmCashBody),
  slot?: 2 | 3,
  app: { readonly features?: readonly TmFeature[]; readonly metadata?: TmClientMetadata; readonly at?: string } = {},
): Promise<ComponentFixture<TmGridHost>> {
  await tmAppRig({
    metadata: app.metadata,
    features: [acme(loadFlaky), ...(app.features ?? [])],
    strings: {
      en: {
        Acme_Cash: 'Cash position',
        Acme_Approvals: 'Approvals',
        Acme_Flaky: 'Flaky',
        Acme_Margins: 'Margins',
        Acme_Reporting: 'Receivables',
        Acme_Overdue: 'Invoices past their due date, by customer',
      },
    },
  });
  if (app.at !== undefined) {
    await tmNavigate(app.at);
  }
  if (slot !== undefined) {
    vi.spyOn(TestBed.inject(TmLocalization), 'contentSlot').mockReturnValue(slot);
  }
  const fixture = TestBed.createComponent(TmGridHost);
  fixture.componentInstance.widgets.set(widgets);
  fixture.componentInstance.denied.set(denied);
  await tmSettle();
  return fixture;
}

/**
 * The grid's tiles, in DOM order.
 *
 * @param fixture - The host's fixture.
 * @returns The tiles' elements.
 */
function tiles(fixture: ComponentFixture<TmGridHost>): HTMLElement[] {
  return [
    ...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('tm-dashboard-tile'),
  ];
}

/**
 * A tile's heading text.
 *
 * @param tile - The tile's element.
 * @returns The heading.
 */
function heading(tile: HTMLElement): string {
  return tile.querySelector('.tm-card__heading')?.textContent?.trim() ?? '';
}

/**
 * The grid's component.
 *
 * @param fixture - The host's fixture.
 * @returns The grid.
 */
function gridOf(fixture: ComponentFixture<TmGridHost>): TmDashboardGrid {
  return fixture.debugElement.query(By.directive(TmDashboardGrid)).componentInstance as TmDashboardGrid;
}

/**
 * The status the grid reports for each tile.
 *
 * @param fixture - The host's fixture.
 * @returns The statuses, by key.
 */
function statuses(fixture: ComponentFixture<TmGridHost>): [string, string][] {
  return gridOf(fixture)
    .tiles()
    .map((tile) => [tile.key, tile.status]);
}

/**
 * A tile's state heading, when it shows one.
 *
 * @param tile - The tile's element.
 * @returns The state's heading, or `null`.
 */
function state(tile: HTMLElement): string | null {
  return tile.querySelector('.tm-empty-state__heading')?.textContent?.trim() ?? null;
}

/**
 * A tile's status line, its parts joined by the separator the line draws between them.
 *
 * @param tile - The tile's element.
 * @returns The line's text, or `null` when the tile shows none.
 */
function statusLine(tile: HTMLElement): string | null {
  const line = tile.querySelector('.tm-dashboard-tile__status');
  if (line === null) {
    return null;
  }
  return [...line.querySelectorAll('.tm-dashboard-tile__description, time, .tm-dashboard-tile__updated > span')]
    .map((part) => (part.textContent ?? '').trim())
    .join(' · ');
}

describe('the dashboard grid', () => {
  afterEach(() => {
    reporting = null;
    TestBed.resetTestingModule();
  });

  it('is a labelled region of tiles in reading order, each at its placement', async () => {
    const fixture = await render([
      { key: 'second', type: 'acme.cash-position', x: 6, y: 4, w: 6, h: 6 },
      { key: 'first', type: 'acme.cash-position', x: 0, y: 0, w: 6, h: 6 },
    ]);
    const grid = (fixture.nativeElement as HTMLElement).querySelector('tm-dashboard-grid')!;

    expect(grid.getAttribute('role')).toBe('region');
    expect(grid.getAttribute('aria-label')).toBe('Cash overview');
    expect(tiles(fixture).map((tile) => tile.dataset['tile'])).toEqual(['first', 'second']);
    const second = tiles(fixture)[1]!;
    expect(
      ['--tm-col', '--tm-span', '--tm-row', '--tm-rows', '--tm-row-narrow'].map((name) =>
        second.style.getPropertyValue(name),
      ),
    ).toEqual(['7', '6', '1', '6', '7']);
  });

  it("renders a type's body under its title, a labelled section", async () => {
    const fixture = await render([
      { key: 'cash', type: 'acme.cash-position', x: 0, y: 0, w: 6, h: 6 },
    ]);
    const [tile] = tiles(fixture);

    expect(heading(tile!)).toBe('Cash position');
    expect(tile!.querySelector('section')?.getAttribute('aria-label')).toBe('Cash position');
    expect(tile!.querySelector('.cash')?.textContent).toBe('Cash 42');
    expect(gridOf(fixture).tiles()).toEqual([
      { key: 'cash', title: 'Cash position', executedAt: null, status: 'ready' },
    ]);
  });

  it("titles a tile by its own text in the active slot, else slot 1, else its title's key, else its type's label", async () => {
    const fixture = await render([
      {
        key: 'own',
        type: 'acme.cash-position',
        title: ['Treasury', null, null],
        x: 0,
        y: 0,
        w: 3,
        h: 3,
      },
      {
        key: 'blank',
        type: 'acme.cash-position',
        title: ['  ', null, null],
        x: 3,
        y: 0,
        w: 3,
        h: 3,
      },
      {
        key: 'keyed',
        type: 'acme.cash-position',
        titleKey: 'Acme_Margins',
        x: 6,
        y: 0,
        w: 3,
        h: 3,
      },
      { key: 'unknown', type: 'acme.gone', x: 9, y: 0, w: 3, h: 3 },
    ]);

    expect(tiles(fixture).map(heading)).toEqual([
      'Treasury',
      'Cash position',
      'Margins',
      'acme.gone',
    ]);
  });

  it("titles a tile by its own text in the second slot when that slot is active, else by slot 1's", async () => {
    const fixture = await render(
      [
        {
          key: 'second',
          type: 'acme.cash-position',
          title: ['الخزينة', 'Treasury', null],
          x: 0,
          y: 0,
          w: 6,
          h: 3,
        },
        {
          key: 'first',
          type: 'acme.cash-position',
          title: ['Liquidity', '  ', null],
          x: 6,
          y: 0,
          w: 6,
          h: 3,
        },
      ],
      'hide',
      undefined,
      2,
    );

    expect(tiles(fixture).map(heading)).toEqual(['Treasury', 'Liquidity']);
  });

  it('renders a type no declaration knows as not available, loading nothing', async () => {
    const fixture = await render([{ key: 'gone', type: 'acme.gone', x: 0, y: 0, w: 6, h: 6 }]);

    expect(state(tiles(fixture)[0]!)).toBe("This widget isn't available.");
    expect(statuses(fixture)).toEqual([['gone', 'unavailable']]);
  });

  it('leaves out a tile whose permission the user lacks, and its rows close over it', async () => {
    const fixture = await render([
      { key: 'locked', type: 'acme.approvals', x: 0, y: 0, w: 12, h: 4 },
      { key: 'cash', type: 'acme.cash-position', x: 0, y: 4, w: 12, h: 4 },
    ]);

    expect(
      tiles(fixture).map((tile) => [tile.dataset['tile'], tile.style.getPropertyValue('--tm-row')]),
    ).toEqual([['cash', '1']]);
    expect(loadLocked).not.toHaveBeenCalled();
  });

  it('shows a denied tile with the permission it needs when asked to, never loading its body', async () => {
    const fixture = await render(
      [{ key: 'locked', type: 'acme.approvals', x: 0, y: 0, w: 6, h: 6 }],
      'show',
    );
    const [tile] = tiles(fixture);

    expect(heading(tile!)).toBe('Approvals');
    expect(state(tile!)).toBe("You don't have permission for this");
    expect(tile!.textContent).toContain('It needs “acme.Approval: Read”.');
    expect(loadLocked).not.toHaveBeenCalled();
    expect(statuses(fixture)).toEqual([['locked', 'denied']]);
  });

  it('shows five skeleton rows in a busy section while the body loads, then the body', async () => {
    const load = deferred<Type<unknown>>();
    const fixture = await render(
      [{ key: 'flaky', type: 'acme.flaky', x: 0, y: 0, w: 6, h: 6 }],
      'hide',
      () => load.promise,
    );
    const [tile] = tiles(fixture);

    expect(tile!.querySelectorAll('.tm-dashboard-frame__rows .tm-skeleton__bar')).toHaveLength(5);
    expect(heading(tile!)).toBe('Flaky');
    expect(tile!.querySelector('section')?.getAttribute('aria-busy')).toBe('true');
    expect(statuses(fixture)).toEqual([['flaky', 'loading']]);

    load.resolve(TmCashBody);
    await tmSettle();
    expect(tile!.querySelector('tm-skeleton')).toBeNull();
    expect(tile!.querySelector('.cash')).not.toBeNull();
    expect(tile!.querySelector('section')?.hasAttribute('aria-busy')).toBe(false);
    expect(statuses(fixture)).toEqual([['flaky', 'ready']]);
  });

  it('renders nothing and throws nothing when a body loads after its tile is gone', async () => {
    const load = deferred<Type<unknown>>();
    const fixture = await render(
      [{ key: 'flaky', type: 'acme.flaky', x: 0, y: 0, w: 6, h: 6 }],
      'hide',
      () => load.promise,
    );
    const errors = vi.spyOn(console, 'error');
    fixture.componentInstance.widgets.set([]);
    await tmSettle();
    expect(tiles(fixture)).toEqual([]);

    load.resolve(TmCashBody);
    await tmSettle();
    expect(tiles(fixture)).toEqual([]);
    expect((fixture.nativeElement as HTMLElement).querySelector('.cash')).toBeNull();
    expect(errors).not.toHaveBeenCalled();
  });

  it('renders a later tile of a loaded type with its body in its first frame, loading it once', async () => {
    const loadFlaky = vi.fn<TmLazyComponent>(() => Promise.resolve(TmCashBody));
    const fixture = await render(
      [{ key: 'one', type: 'acme.flaky', x: 0, y: 0, w: 6, h: 6 }],
      'hide',
      loadFlaky,
    );
    expect(tiles(fixture)[0]!.querySelector('.cash')).not.toBeNull();

    fixture.componentInstance.widgets.set([
      { key: 'one', type: 'acme.flaky', x: 0, y: 0, w: 6, h: 6 },
      { key: 'two', type: 'acme.flaky', x: 6, y: 0, w: 6, h: 6 },
    ]);
    fixture.detectChanges();
    const second = tiles(fixture)[1]!;
    expect(second.querySelector('.cash')).not.toBeNull();
    expect(second.querySelector('tm-skeleton')).toBeNull();
    expect(loadFlaky).toHaveBeenCalledTimes(1);
  });

  it('offers to try again when the body fails to load, and loads it once more on Try again', async () => {
    const loadFlaky = vi
      .fn<TmLazyComponent>()
      .mockImplementationOnce(() => Promise.reject(new Error('chunk')))
      .mockImplementationOnce(() => Promise.resolve(TmCashBody));
    const fixture = await render(
      [{ key: 'flaky', type: 'acme.flaky', x: 0, y: 0, w: 6, h: 6 }],
      'hide',
      loadFlaky,
    );
    const [tile] = tiles(fixture);

    expect(state(tile!)).toBe("This widget couldn't be loaded.");
    expect(statuses(fixture)).toEqual([['flaky', 'error']]);
    const retry = tile!.querySelector<HTMLButtonElement>('.tm-empty-state button')!;
    expect(retry.textContent?.trim()).toBe('Try again');

    retry.click();
    await tmSettle();
    expect(tile!.querySelector('.cash')).not.toBeNull();
    expect(loadFlaky).toHaveBeenCalledTimes(2);
  });

  it("gives a type's body its context: its key, type, size, settings, the dashboard's arguments, view mode and the first tick", async () => {
    const fixture = await render([
      { key: 'ar', type: 'acme.reporting', x: 0, y: 0, w: 6, h: 5, settings: { basis: 'due' } },
    ]);
    fixture.componentInstance.parameters.set({ center: 4 });
    await tmSettle();

    expect(tiles(fixture)[0]!.querySelector('.reporting')?.textContent).toBe(
      'ar acme.reporting 6x5 {"basis":"due"} {"center":4} view 0',
    );
  });

  it("holds the status line's row with a bar before the first result, then shows when the numbers were computed", async () => {
    const fixture = await render([{ key: 'ar', type: 'acme.reporting', x: 0, y: 0, w: 6, h: 6 }]);
    const [tile] = tiles(fixture);
    expect(tile!.querySelector('.tm-dashboard-tile__status .tm-skeleton__bar')).not.toBeNull();

    reporting!.report({ status: 'loading' });
    await tmSettle();
    expect(tile!.querySelector('section')?.getAttribute('aria-busy')).toBe('true');
    expect(statuses(fixture)).toEqual([['ar', 'loading']]);

    const at = new Date().toISOString();
    reporting!.report({ status: 'ready', executedAt: at });
    await tmSettle();
    expect(tile!.querySelector('.tm-dashboard-tile__status tm-skeleton')).toBeNull();
    expect(tile!.querySelector('.tm-dashboard-tile__status time')?.getAttribute('datetime')).toBe(at);
    expect(statusLine(tile!)).toMatch(/^Updated \d/);
    expect(tile!.querySelector('section')?.hasAttribute('aria-busy')).toBe(false);
    expect(gridOf(fixture).tiles()).toEqual([
      { key: 'ar', title: 'Receivables', executedAt: at, status: 'ready' },
    ]);
  });

  it('writes the date beside the time of a result from another day', async () => {
    const fixture = await render([{ key: 'ar', type: 'acme.reporting', x: 0, y: 0, w: 6, h: 6 }]);
    reporting!.report({ status: 'ready', executedAt: '2026-01-15T09:00:00Z' });
    await tmSettle();

    expect(statusLine(tiles(fixture)[0]!)).toContain('2026');
  });

  it('leaves the status line empty after a failed first run', async () => {
    const fixture = await render([{ key: 'ar', type: 'acme.reporting', x: 0, y: 0, w: 6, h: 6 }]);
    reporting!.report({ status: 'error', executedAt: null, error: null });
    await tmSettle();
    const [tile] = tiles(fixture);

    expect(tile!.querySelector('.tm-dashboard-tile__status')).not.toBeNull();
    expect(statusLine(tile!)).toBe('');
    expect(tile!.querySelector('.tm-dashboard-tile__status tm-skeleton')).toBeNull();
    expect(statuses(fixture)).toEqual([['ar', 'error']]);
  });

  it('marks held numbers stale after a failed run over them, through the next run, until its result', async () => {
    const fixture = await render([{ key: 'ar', type: 'acme.reporting', x: 0, y: 0, w: 6, h: 6 }]);
    const [tile] = tiles(fixture);
    const at = new Date().toISOString();
    reporting!.report({ status: 'ready', executedAt: at });
    reporting!.report({ status: 'error', error: null });
    await tmSettle();

    const stale = tile!.querySelector('.tm-dashboard-tile__stale');
    expect(stale?.getAttribute('aria-label')).toMatch(/^Couldn't refresh\. Showing data from \d/);
    expect(tile!.querySelector('.tm-dashboard-tile__status time')?.getAttribute('datetime')).toBe(at);

    // The next run's start keeps the marker: only its success clears it.
    reporting!.report({ status: 'loading' });
    await tmSettle();
    expect(tile!.querySelector('.tm-dashboard-tile__stale')).not.toBeNull();

    reporting!.report({ status: 'ready', executedAt: at });
    await tmSettle();
    expect(tile!.querySelector('.tm-dashboard-tile__stale')).toBeNull();
  });

  it('notes omitted totals after the time, and describes the section by the full description', async () => {
    const fixture = await render([
      { key: 'ar', type: 'acme.reporting', descriptionKey: 'Acme_Overdue', x: 0, y: 0, w: 6, h: 6 },
    ]);
    reporting!.report({ status: 'ready', executedAt: new Date().toISOString(), totalsOmitted: true });
    await tmSettle();
    const [tile] = tiles(fixture);

    expect(statusLine(tile!)).toMatch(/^Invoices past their due date, by customer · Updated .+ · Totals not shown$/);
    const section = tile!.querySelector('section')!;
    expect(document.getElementById(section.getAttribute('aria-describedby')!)?.textContent).toBe(
      'Invoices past their due date, by customer',
    );
  });

  it('shows no status line for a type that declares none', async () => {
    const fixture = await render([{ key: 'cash', type: 'acme.cash-position', x: 0, y: 0, w: 6, h: 6 }]);

    expect(tiles(fixture)[0]!.querySelector('.tm-dashboard-tile__status')).toBeNull();
  });

  it("offers Refresh in the tile's options menu, which ticks its body clearing what it shows; none when presented or denied", async () => {
    const fixture = await render(
      [
        { key: 'ar', type: 'acme.reporting', x: 0, y: 0, w: 6, h: 6 },
        { key: 'locked', type: 'acme.approvals', x: 6, y: 0, w: 6, h: 6 },
      ],
      'show',
    );
    const [tile, locked] = tiles(fixture);
    const options = tile!.querySelector<HTMLButtonElement>('.tm-dashboard-tile__options')!;
    expect(options.getAttribute('aria-label')).toBe('Widget options for Receivables');
    expect(options.parentElement?.classList.contains('tm-card__actions')).toBe(true);
    expect(locked!.querySelector('.tm-dashboard-tile__options')).toBeNull();

    options.click();
    await tmSettle();
    const refresh = [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(
      (item) => item.textContent?.trim() === 'Refresh',
    );
    expect(refresh).toBeDefined();
    refresh!.click();
    await tmSettle();
    expect(tile!.querySelector('.reporting')?.textContent).toMatch(/ 1$/);
    expect(reporting!.tick()).toEqual({ generation: 1, clear: true, background: false });

    fixture.componentInstance.presenting.set(true);
    await tmSettle();
    expect(tile!.querySelector('.tm-dashboard-tile__options')).toBeNull();
  });

  it("ticks every tile's body on refreshAll, each clearing what it shows first", async () => {
    const fixture = await render([{ key: 'ar', type: 'acme.reporting', x: 0, y: 0, w: 6, h: 6 }]);
    const [tile] = tiles(fixture);
    reporting!.report({ status: 'ready', executedAt: new Date().toISOString() });
    await tmSettle();

    gridOf(fixture).refreshAll();
    await tmSettle();
    expect(reporting!.tick()).toEqual({ generation: 1, clear: true, background: false });
    // A cleared refresh drops the held time, and the line waits again.
    reporting!.report({ status: 'loading' });
    await tmSettle();
    expect(tile!.querySelector('.tm-dashboard-tile__status time')).toBeNull();
    expect(tile!.querySelector('.tm-dashboard-tile__status .tm-skeleton__bar')).not.toBeNull();
  });

  it("keeps the time over a later run that no refresh forced, after a refresh's result", async () => {
    const fixture = await render([{ key: 'ar', type: 'acme.reporting', x: 0, y: 0, w: 6, h: 6 }]);
    const [tile] = tiles(fixture);
    reporting!.report({ status: 'ready', executedAt: '2026-01-15T09:00:00Z' });
    gridOf(fixture).refreshAll();
    await tmSettle();
    reporting!.report({ status: 'loading' });
    const at = new Date().toISOString();
    reporting!.report({ status: 'ready', executedAt: at });
    await tmSettle();

    // A change of the dashboard's arguments reloads the body in the same generation: the time holds.
    fixture.componentInstance.parameters.set({ center: 5 });
    await tmSettle();
    expect(reporting!.tick().clear).toBe(true);
    reporting!.report({ status: 'loading' });
    await tmSettle();
    expect(tile!.querySelector('.tm-dashboard-tile__status time')?.getAttribute('datetime')).toBe(at);
    expect(tile!.querySelector('.tm-dashboard-tile__status .tm-skeleton__bar')).toBeNull();
    expect(tile!.querySelector('section')?.hasAttribute('aria-busy')).toBe(false);
  });

  it("hides a list's View all when presented, and on a denied list tile", async () => {
    const route = tmAssignmentsTestRoute();
    const fixture = await render(
      [
        { key: 'to-me', type: 'core.assignments-to-me', x: 0, y: 0, w: 6, h: 9 },
        { key: 'by-me', type: 'core.assignments-by-me', x: 6, y: 0, w: 6, h: 9 },
      ],
      'show',
      undefined,
      undefined,
      { metadata: route.metadata, features: [route.feature, tmGatedCoreWidget('core.assignments-by-me')], at: '/17' },
    );
    const [toMe, locked] = tiles(fixture);
    expect(toMe!.querySelector('.tm-dashboard-tile__view-all')?.getAttribute('href')).toBe('/17/assignments');
    expect(toMe!.querySelector('.tm-dashboard-tile__view-all')?.parentElement?.classList.contains('tm-card__actions')).toBe(true);
    expect(state(locked!)).toBe("You don't have permission for this");
    expect(locked!.querySelector('.tm-dashboard-tile__view-all')).toBeNull();

    fixture.componentInstance.presenting.set(true);
    await tmSettle();
    expect(toMe!.querySelector('.tm-dashboard-tile__view-all')).toBeNull();
  });
});

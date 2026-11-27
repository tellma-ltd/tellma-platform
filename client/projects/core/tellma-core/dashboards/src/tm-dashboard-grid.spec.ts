// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, signal, type Type } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import {
  TmLocalization,
  tmFeature,
  tmWidget,
  type TmDashboardWidgetModel,
  type TmFeature,
  type TmLazyComponent,
} from '@tellma/core';

import { tmAppRig, tmSettle } from '../../src/routes/tm-app.testing.util';
import { TmDashboardGrid } from './tm-dashboard-grid';

// The grid over a test app whose feature declares three widget types: one
// that renders, one whose permission the user lacks, and one whose body's
// chunk the spec controls. Each tile resolves its type, title and access
// against the declarations, renders in reading order at its placement, and
// shows its body or the state that stands in for it, which the grid's tile
// states report.

/** A widget body, which names itself. */
@Component({
  selector: 'tm-cash-body',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<p class="cash">Cash 42</p>',
})
class TmCashBody {}

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
  template: `<tm-dashboard-grid [widgets]="widgets()" label="Cash overview" [denied]="denied()" />`,
})
class TmGridHost {
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
 * @returns The host's fixture.
 */
async function render(
  widgets: readonly TmDashboardWidgetModel[],
  denied: 'show' | 'hide' = 'hide',
  loadFlaky: TmLazyComponent = () => Promise.resolve(TmCashBody),
  slot?: 2 | 3,
): Promise<ComponentFixture<TmGridHost>> {
  await tmAppRig({
    features: [acme(loadFlaky)],
    strings: {
      en: {
        Acme_Cash: 'Cash position',
        Acme_Approvals: 'Approvals',
        Acme_Flaky: 'Flaky',
        Acme_Margins: 'Margins',
      },
    },
  });
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

describe('the dashboard grid', () => {
  afterEach(() => {
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
    const retry = tile!.querySelector<HTMLButtonElement>('button')!;
    expect(retry.textContent?.trim()).toBe('Try again');

    retry.click();
    await tmSettle();
    expect(tile!.querySelector('.cash')).not.toBeNull();
    expect(loadFlaky).toHaveBeenCalledTimes(2);
  });
});

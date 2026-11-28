// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, computed, effect, signal, untracked } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { TmAssignmentsSummary, TmEntities, TmUrls } from '@tellma/core';
import type { TmAttention } from '@tellma/core/attention';
import { provideTellmaTesting, tmFakeAssignmentsSummary, tmFakeDashboard } from '@tellma/core-testing';

import { tmTileList } from './tm-list-tiles';
import { injectTmWidget } from './tm-widget-context';

// A widget type's body under the testing package's fake dashboard: it reads
// its settings, arguments, mode and size, refreshes on each tick, and its
// reports are recorded in order. Then the built-in list types' headers:
// the counts that follow the assignments document, a count of 0 shown as
// none, and "View all" only while the server routes assignments.

/** A fixture type's body: a cash position that runs once per tick and reports each run. */
@Component({
  selector: 'tm-fixture-cash',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<p class="fixture">{{ text() }}</p>',
})
class TmFixtureCash {
  readonly #widget = injectTmWidget();

  /** What the body shows: its account, its center, its mode and its width. */
  protected readonly text = computed(() => {
    const account = (this.#widget.settings() as { account?: string } | null)?.account ?? 'none';
    return [account, String(this.#widget.parameters()['center'] ?? '-'), this.#widget.mode(), String(this.#widget.size().w)].join(' ');
  });

  /** Runs once per tick, reporting the run's start and its result. */
  constructor() {
    effect(() => {
      const tick = this.#widget.tick();
      untracked(() => {
        this.#widget.report({ status: 'loading' });
        this.#widget.report({ status: 'ready', executedAt: `2026-11-26T06:00:0${tick.generation}.000Z`, totalsOmitted: tick.clear });
      });
    });
  }
}

describe('a widget body under a fake dashboard', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('reads its context, runs on each tick, and has its reports recorded in order', async () => {
    const dashboard = tmFakeDashboard({ type: 'acme.cash-position', settings: { account: 'cash' }, size: { w: 4, h: 6 } });
    TestBed.configureTestingModule({ providers: [provideTellmaTesting(), ...dashboard.providers] });
    const fixture = TestBed.createComponent(TmFixtureCash);
    await fixture.whenStable();
    const text = (): string | null | undefined => (fixture.nativeElement as HTMLElement).querySelector('.fixture')?.textContent;
    expect(text()).toBe('cash - view 4');
    expect(dashboard.reports().map((run) => run.status)).toEqual(['loading', 'ready']);

    dashboard.parameters.set({ center: 7 });
    dashboard.mode.set('edit');
    dashboard.size.set({ w: 6, h: 6 });
    dashboard.refresh({ clear: true });
    await fixture.whenStable();
    expect(text()).toBe('cash 7 edit 6');
    expect(dashboard.tick()).toEqual({ generation: 1, clear: true, background: false });
    expect(dashboard.reports().at(-1)).toEqual({ status: 'ready', executedAt: '2026-11-26T06:00:01.000Z', totalsOmitted: true });
  });
});

describe('the built-in list types’ headers', () => {
  /** The assignments document the headers read. */
  const summary = signal<TmAssignmentsSummary | undefined>(undefined);
  const attention = { assignments: summary } as unknown as TmAttention;
  const urls = { search: (entity: string, query: object = {}) => `/17/${entity}?${new URLSearchParams(query as Record<string, string>).toString()}` } as unknown as TmUrls;

  /**
   * An entity registry that routes assignments, or not.
   *
   * @param routed - Whether the server routes them.
   * @returns The registry.
   */
  function entities(routed: boolean): TmEntities {
    return { summary: () => (routed ? { segment: 'assignments' } : undefined) } as unknown as TmEntities;
  }

  it('counts the open assignments either way from the document, a count of 0 as none, and links each list to its tab', () => {
    const toMe = tmTileList('core.assignments-to-me', attention, urls, entities(true))!;
    const byMe = tmTileList('core.assignments-by-me', attention, urls, entities(true))!;
    expect([toMe.count(), byMe.count()]).toEqual([null, null]);

    summary.set(tmFakeAssignmentsSummary({ open: 4, openByMe: 0 }));
    expect([toMe.count(), byMe.count()]).toEqual([4, null]);
    expect([toMe.list, byMe.list]).toEqual(['to-me', 'by-me']);
    expect([toMe.viewAll, byMe.viewAll]).toEqual(['/17/core.Assignment?', '/17/core.Assignment?tab=by-me']);
  });

  it('gives recents no count and no link, any other type no header, and no link while assignments are not routed', () => {
    const recents = tmTileList('core.recents', attention, urls, entities(true))!;
    expect([recents.list, recents.count(), recents.viewAll]).toEqual(['recents', null, null]);
    expect(tmTileList('acme.cash-position', attention, urls, entities(true))).toBeNull();
    expect(tmTileList('core.assignments-to-me', attention, urls, entities(false))?.viewAll).toBeNull();
  });
});

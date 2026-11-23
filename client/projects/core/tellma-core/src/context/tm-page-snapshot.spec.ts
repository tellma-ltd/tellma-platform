// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injector, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { tmAppRig } from '../routes/tm-app.testing.util';
import type { TmPageContext } from './tm-page-context';
import { tmSerializePageContext, type TmPageContextSnapshotOptions } from './tm-page-snapshot';
import { tmTestAction, tmTestContext } from './tm-page.testing.util';

// The snapshot of a context, with and without page data: every member read
// once, the rows, values and messages kept only with page data, and the
// user's text masked without it. A whole app resolves the entities' labels.

/** A search page's context with every optional member set. */
function search(): TmPageContext {
  return tmTestContext(
    '/:t/:entity',
    {
      kind: 'search',
      entity: 'gl.Center',
      helpKey: 'gl.center',
      state: signal({
        view: 'grid',
        query: { q: 'main', filter: "Name contains 'Addis'", columnFilters: { Code: ['10', '20'] }, top: 25, custom: { note: 'x', n: 2 } },
      }),
      visible: signal({
        columns: [{ path: 'Code', label: 'Code' }, { path: 'Name', label: 'Name' }],
        rows: [{ id: 5, cells: { Code: '10', Name: 'Main' } }],
        total: 42,
        truncated: false,
      }),
      form: {
        fields: signal([
          { path: 'Name', labelKey: 'Name', label: 'Name', value: 'Main', editable: true, errors: ['Required'] },
          { path: 'Code', labelKey: 'Code', label: 'Code', value: '10', editable: false, errors: [] },
        ]),
        dirty: signal(true),
        assisted: signal(new Set<string>()),
        propose: () => false,
      },
      errors: signal([{ source: 'validation' as const, code: 'taken', path: 'Name', message: 'Name is taken.', traceId: 'abc' }]),
    },
    [
      tmTestAction('gl.center.save', { group: 'primary' }),
      tmTestAction('gl.center.export', { enabled: signal(false), params: { type: 'object' } }),
    ],
  );
}

/** Serializes in the test app's injection context. */
function serialize(context: TmPageContext, options: TmPageContextSnapshotOptions): ReturnType<typeof tmSerializePageContext> {
  return TestBed.runInInjectionContext(() => tmSerializePageContext(context, options));
}

describe('tmSerializePageContext', () => {
  it('reads every member with page data, as plain JSON', async () => {
    await tmAppRig();
    const snapshot = serialize(search(), { includeData: true });
    expect(snapshot).toEqual({
      kind: 'search',
      routeTemplate: '/:t/:entity',
      overlay: false,
      entity: 'gl.Center',
      title: '/:t/:entity',
      helpKey: 'gl.center',
      status: 'ready',
      state: {
        view: 'grid',
        query: { q: 'main', filter: "Name contains 'Addis'", columnFilters: { Code: ['10', '20'] }, top: 25, custom: { note: 'x', n: 2 } },
      },
      visible: { columns: ['Code', 'Name'], rowCount: 1, total: 42, rows: [{ id: 5, cells: { Code: '10', Name: 'Main' } }] },
      form: {
        dirty: true,
        fields: [
          { path: 'Name', editable: true, errorCount: 1, value: 'Main' },
          { path: 'Code', editable: false, errorCount: 0, value: '10' },
        ],
      },
      errors: [{ source: 'validation', code: 'taken', path: 'Name', traceId: 'abc', message: 'Name is taken.' }],
      actions: [
        { id: 'gl.center.save', enabled: true, destructive: false, group: 'primary', hasParams: false },
        { id: 'gl.center.export', enabled: false, destructive: false, group: 'menu', hasParams: true },
      ],
    });
    // Plain JSON: it survives a round trip unchanged.
    expect(JSON.parse(JSON.stringify(snapshot))).toEqual(snapshot);
  });

  it("leaves out the rows, values and messages without page data, and masks the query's text and filters", async () => {
    await tmAppRig();
    const snapshot = serialize(search(), { includeData: false });
    expect(snapshot.state).toEqual({
      view: 'grid',
      query: { q: '…', filter: '…', columnFilters: { Code: ['…', '…'] }, top: 25, custom: { note: '…', n: 2 } },
    });
    expect(snapshot.visible).toEqual({ columns: ['Code', 'Name'], rowCount: 1, total: 42 });
    expect(snapshot.form!.fields).toEqual([
      { path: 'Name', editable: true, errorCount: 1 },
      { path: 'Code', editable: false, errorCount: 0 },
    ]);
    expect(snapshot.errors).toEqual([{ source: 'validation', code: 'taken', path: 'Name', traceId: 'abc' }]);
    // A search page's title names no record, so it stays.
    expect(snapshot.title).toBe('/:t/:entity');
  });

  it("masks a report's parameters without page data, and keeps the rest of its state", async () => {
    await tmAppRig();
    const report = tmTestContext('/:t/reports/:id', {
      kind: 'report',
      state: signal({ reportId: 12, parameters: { from: '2026-01-01', customers: ['Acme', 7] }, view: 'chart' }),
    });
    expect(serialize(report, { includeData: false }).state).toEqual({
      reportId: 12,
      parameters: { from: '…', customers: ['…', 7] },
      view: 'chart',
    });
    expect(serialize(report, { includeData: true }).state).toEqual(report.state());
  });

  it("replaces a details page's title, a record's name, with the entity's label without page data", async () => {
    await tmAppRig();
    const details = (entity?: string): TmPageContext => {
      const context = tmTestContext('/:t/:entity/:id', { kind: 'details', ...(entity !== undefined && { entity }) });
      context.title.set('Main warehouse');
      return context;
    };
    expect(serialize(details('gl.Center'), { includeData: true }).title).toBe('Main warehouse');
    expect(serialize(details('gl.Center'), { includeData: false }).title).toBe('Center');
    expect(serialize(details(), { includeData: false }).title).toBe('…');
  });

  it('marks a dialog, leaves out the members a context lacks, and serializes outside an injection context with an injector', async () => {
    await tmAppRig();
    const panel = tmTestContext('panel:bell', { overlay: true, status: signal<'loading' | 'ready' | 'error'>('loading'), state: signal(7) });
    const snapshot = tmSerializePageContext(panel, { includeData: false, injector: TestBed.inject(Injector) });
    expect(snapshot).toEqual({
      kind: 'custom',
      routeTemplate: 'panel:bell',
      overlay: true,
      title: 'panel:bell',
      status: 'loading',
      state: 7,
      errors: [],
      actions: [],
    });
  });

  it('needs no injection context and no injector for any snapshot but a masked details title', async () => {
    await tmAppRig();
    // Outside any injection context, as a promise callback is.
    expect(tmSerializePageContext(search(), { includeData: false }).state).toMatchObject({
      query: { q: '…' },
    });
    const details = tmTestContext('/:t/:entity/:id', { kind: 'details', entity: 'gl.Center' });
    details.title.set('Main warehouse');
    expect(tmSerializePageContext(details, { includeData: true }).title).toBe('Main warehouse');
    expect(() => tmSerializePageContext(details, { includeData: false })).toThrow(/NG0203/);
  });
});

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TmFakeApi, TmFakeDiscover, TmFakeTelemetry } from '@tellma/core-testing';

import { tmEntityRig } from '../../src/entity/tm-entity.testing.util';
import { tmSettle, tmSettleUntil } from '../../src/routes/tm-app.testing.util';
import { injectTmQueryexDraft, type TmQueryexDraft, type TmQueryexDraftOptions } from './tm-queryex-draft';
import type { TmQueryexDiscoverResult } from './tm-queryex-wire';

// The draft one query's editors share, over the fake discover route: one
// check of every clause once the typing pauses, each change aborting the
// check in flight, the server's problems only on the text they answered,
// the lexer's at once, offline and failed checks and their retry, the
// permission profile, aggregation, and the parameters' merged types.

/** An answer with no problem, path or parameter. */
const EMPTY: TmQueryexDiscoverResult = { diagnostics: [], parameters: [], paths: [], usesAggregation: false };

/** What a draft spec drives. */
interface TmDraftRig {
  /** The draft. */
  readonly draft: TmQueryexDraft;
  /** The fake discover route. */
  readonly discover: TmFakeDiscover;
  /** The fake API. */
  readonly api: TmFakeApi;
  /** The fake telemetry. */
  readonly telemetry: TmFakeTelemetry;
}

/**
 * A draft over customers, for a user who reads everything, with the
 * messages of two problem codes.
 *
 * @param options - The draft's options beside its root.
 * @returns The rig.
 */
async function draftRig(options: Partial<TmQueryexDraftOptions> = {}): Promise<TmDraftRig> {
  await tmEntityRig({
    access: { isSystem: true },
    labels: { en: { Queryex_QX2001: 'No field is named {name}', Query_TooDeep: 'The path is too deep' } },
  });
  const discover = TestBed.inject(TmFakeDiscover);
  const draft = TestBed.runInInjectionContext(() => injectTmQueryexDraft({ root: signal('acme.Customer'), ...options }));
  return { draft, discover, api: TestBed.inject(TmFakeApi), telemetry: TestBed.inject(TmFakeTelemetry) };
}

/**
 * Waits until the draft's check has settled.
 *
 * @param draft - The draft.
 * @param status - The status it settles in.
 */
async function settled(draft: TmQueryexDraft, status: 'checked' | 'offline' | 'failed' = 'checked'): Promise<void> {
  await tmSettleUntil(() => draft.status() === status, `the draft is ${status}`);
}

describe('injectTmQueryexDraft', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('checks every clause together once the typing pauses', async () => {
    const { draft, discover, telemetry } = await draftRig();
    draft.text('select').set('Code, Name');
    for (const text of ['C', 'Co', 'Code', 'Code = @Code']) {
      draft.text('filter').set(text);
      TestBed.tick();
      await new Promise((resolve) => setTimeout(resolve, 20));
    }
    expect(draft.status()).toBe('checking');

    await settled(draft);
    expect(discover.requests()).toEqual([{ root: 'acme.Customer', select: 'Code, Name', filters: ['Code = @Code'] }]);
    const timing = telemetry.recent().find((item) => item.name === 'tm.queryex.discover');
    expect(timing?.kind).toBe('timing');
    expect(timing?.props).toEqual(expect.objectContaining({ clauses: 2, problems: 'none' }));
  });

  it('checks nothing without a root or a clause, and never sends a constant', async () => {
    const root = signal('');
    const { draft, discover } = await draftRig({ root });
    draft.text('filter').set('Code = 1');
    await tmSettle();
    expect(draft.status()).toBe('idle');

    root.set('acme.Customer');
    draft.text('filter').set('   ');
    draft.text('constant').set('1 + 1');
    await tmSettle();
    expect(draft.status()).toBe('idle');
    await new Promise((resolve) => setTimeout(resolve, 300));
    expect(discover.requests()).toEqual([]);
  });

  it('aborts the check in flight on a change, and never applies a stale answer', async () => {
    const { draft, discover } = await draftRig();
    discover.respond((request) => ({
      ...EMPTY,
      diagnostics: request.filters?.[0] === 'Nope = 1' ? [{ clause: 'filters[0]', code: 'QX2001', start: 0, length: 4, arguments: { name: 'Nope' } }] : [],
    }));
    discover.hold();
    draft.text('filter').set('Nope = 1');
    await tmSettleUntil(() => discover.requests().length === 1, 'the first check is sent');

    draft.text('filter').set('Code = 1');
    await tmSettleUntil(() => discover.requests().length === 2, 'the second check is sent');
    discover.release();
    await settled(draft);
    expect(draft.diagnostics('filter')()).toEqual([]);
    expect(TestBed.inject(TmFakeApi).calls().filter((call) => call.route === 'queryex/discover')).toHaveLength(2);
  });

  it("shows the server's problems only while the clause still reads what was checked", async () => {
    const { draft, discover } = await draftRig();
    discover.respond({
      ...EMPTY,
      diagnostics: [
        { clause: 'filters[0]', code: 'QX2001', start: 0, length: 4, arguments: { name: 'Nope' } },
        { clause: 'filters[0]', code: 'Query.TooDeep', start: 9, length: 3 },
        { clause: 'select', code: 'Query.Unworded', start: 0, length: 2 },
      ],
    });
    draft.text('select').set('Id');
    draft.text('filter').set('Nope = 1 and A.B.C = 2');
    await settled(draft);

    const problems = draft.diagnostics('filter')();
    expect(problems.map((problem) => [problem.code, problem.start, problem.length, problem.message()])).toEqual([
      ['QX2001', 0, 4, 'No field is named Nope'],
      ['Query.TooDeep', 9, 3, 'The path is too deep'],
    ]);
    // A code no pack words reads as itself.
    expect(draft.diagnostics('select')().map((problem) => problem.message())).toEqual(['Query.Unworded']);

    // The filter changes: its server problems go at once, the select's stay.
    draft.text('filter').set('Nope = 2');
    expect(draft.diagnostics('filter')()).toEqual([]);
    expect(draft.diagnostics('select')()).toHaveLength(1);
  });

  it("shows the lexer's problems at once, before any check, each once", async () => {
    const { draft, discover } = await draftRig();
    discover.respond({ ...EMPTY, diagnostics: [{ clause: 'filters[0]', code: 'QX1001', start: 7, length: 4 }] });
    draft.text('filter').set("Code = 'abc");
    expect(draft.diagnostics('filter')().map((problem) => [problem.code, problem.start])).toEqual([['QX1001', 7]]);

    // The server's copy of the lexer's problem shows once.
    await settled(draft);
    expect(draft.diagnostics('filter')()).toHaveLength(1);
  });

  it('is offline while the server is unreachable, and checks again when it returns', async () => {
    const { draft, discover, api } = await draftRig();
    discover.fail('offline');
    api.setOnline(false);
    draft.text('filter').set('Code = 1');
    await settled(draft, 'offline');

    discover.fail(null);
    api.setOnline(true);
    await settled(draft);
    expect(discover.requests()).toHaveLength(2);
  });

  it('fails on any other error, and checks again on retry', async () => {
    const { draft, discover } = await draftRig();
    discover.fail('server');
    draft.text('filter').set('Code = 1');
    await settled(draft, 'failed');

    discover.fail(null);
    draft.retry();
    expect(draft.status()).toBe('checking');
    await settled(draft);
  });

  it('sends a permission filter alone, under the permission profile', async () => {
    const { draft, discover } = await draftRig({ profile: 'permission' });
    draft.text('select').set('Code');
    draft.text('filter').set('IsActive = true');
    await settled(draft);
    expect(discover.requests()).toEqual([{ root: 'acme.Customer', filters: ['IsActive = true'], profile: 'Permission' }]);
  });

  it('sends having and measures only when the query aggregates, with the confirmed types the server takes', async () => {
    const aggregate = signal(false);
    const parameters = signal([
      { name: 'From', type: 'Date' },
      { name: 'FROM', type: 'Numeric' },
      { name: 'Area', type: 'Geography' },
    ]);
    const { draft, discover } = await draftRig({ aggregate, parameters });
    draft.text('filter').set('CreatedAt >= @From');
    draft.text('having').set('sum(CreditLimit) > 0');
    draft.text('measures').set('sum(CreditLimit)');
    await settled(draft);
    expect(discover.requests().at(-1)).toEqual({
      root: 'acme.Customer',
      filters: ['CreatedAt >= @From'],
      parameters: [{ name: 'From', type: 'Date' }],
    });

    aggregate.set(true);
    await tmSettleUntil(() => discover.requests().length === 2 && draft.status() === 'checked', 'the aggregate check');
    expect(discover.requests().at(-1)).toEqual({
      root: 'acme.Customer',
      filters: ['CreatedAt >= @From'],
      having: 'sum(CreditLimit) > 0',
      measures: 'sum(CreditLimit)',
      aggregate: true,
      parameters: [{ name: 'From', type: 'Date' }],
    });
  });

  it("merges the parameters of every clause, the confirmed type over the server's", async () => {
    const parameters = signal([{ name: 'code', type: 'String' }]);
    const { draft, discover } = await draftRig({ parameters });
    discover.respond({
      ...EMPTY,
      parameters: [
        { name: 'From', sites: [], type: 'DateTimeOffset', conflicts: [] },
        { name: 'Code', sites: [], type: 'Numeric', conflicts: [] },
        { name: 'Who', sites: [], conflicts: ['String', 'Numeric'], entity: 'core.User' },
      ],
    });
    draft.text('select').set('@FROM');
    draft.text('filter').set('CreatedAt >= @From and Code = @Code and CreatedById = @Who and Rank = @Rank');

    // The names show at once, untyped.
    expect(draft.parameters().map((parameter) => parameter.name)).toEqual(['FROM', 'Code', 'Who', 'Rank']);
    await settled(draft);
    expect(draft.parameters()).toEqual([
      { name: 'FROM', type: 'DateTimeOffset', conflicting: false },
      { name: 'Code', type: 'String', conflicting: false },
      { name: 'Who', type: null, conflicting: true, entity: 'core.User' },
      { name: 'Rank', type: null, conflicting: false },
    ]);
  });
});

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { expect, test } from '@playwright/test';

/**
 * The launcher's page search against the clock, in the performance tier: an
 * index of 300 entries builds in 5 ms, and a query over it answers in 2 ms,
 * each the fastest of twenty runs after a warm-up, times a CI multiplier.
 * The first build in a fresh page is reported beside them, not held to the
 * budget: it pays the engine's one-time start-up, the first normalization
 * and collator a page makes and the code's first, unoptimized run, which
 * the warm figures leave out. The search's own module is bundled for the browser as a
 * production build would minify it, and runs over entries as a large
 * distribution's launcher holds them: a label and a synonym in Arabic and in
 * English each, their synonyms split as the terms reader splits them. The
 * pull-request tier pins the work instead, which no runner's speed changes:
 * one fold of every term together, one read of each entry's terms per
 * language, one collator per build, and nothing on a keystroke.
 */

/** The longest an index of 300 entries may take to build, on a quiet machine. */
const INDEX_MS = 5;

/** The longest a query over it may take, on a quiet machine. */
const QUERY_MS = 2;

/**
 * The timing budget's multiplier: `TM_PERF_MULTIPLIER` when set, else 2 on
 * CI, whose shared runners are slower and noisier than a quiet machine.
 */
const PERF_MULTIPLIER = Number(process.env['TM_PERF_MULTIPLIER'] ?? (process.env['CI'] ? 2 : 1));

/** The client workspace, whose sources and tsconfig paths the bundle reads. */
const CLIENT = join(dirname(fileURLToPath(import.meta.url)), '..', '..');

/** The search's functions, as the bundle exposes them on the page's window. */
interface TmLauncherPerfWindow extends Window {
  /** The bundle's exports. */
  readonly tmLauncherPerf: {
    /** Gathers the candidates. */
    tmLauncherCandidates(sources: unknown): unknown[];
    /** Builds the index. */
    tmLauncherIndex(candidates: readonly unknown[]): unknown;
    /** A query's tokens. */
    tmLauncherTokens(query: string): readonly string[];
    /** The candidates a query matches. */
    tmLauncherMatch(index: unknown, tokens: readonly string[]): readonly unknown[];
    /** The rows of the matches. */
    tmLauncherRows(matches: readonly unknown[]): readonly unknown[];
  };
}

/** What the page measured, in milliseconds. */
interface TmLauncherTimes {
  /** The first build in the fresh page, reported only. */
  readonly first: number;
  /** The fastest of twenty builds after five warm-ups. */
  readonly warm: number;
  /** Each query's fastest of twenty runs after five warm-ups. */
  readonly queries: Readonly<Record<string, number>>;
}

/** Bundles the launcher's search module for the browser, minified. */
async function bundle(): Promise<string> {
  // esbuild is the client's own dev dependency; it resolves from the workspace.
  const esbuild = createRequire(join(CLIENT, 'package.json'))('esbuild') as typeof import('esbuild');
  const result = await esbuild.build({
    stdin: {
      contents: "export * from './tm-launcher-index';",
      resolveDir: join(CLIENT, 'projects', 'core', 'tellma-core', 'shell', 'src', 'launcher'),
      loader: 'ts',
    },
    tsconfig: join(CLIENT, 'tsconfig.json'),
    bundle: true,
    minify: true,
    format: 'iife',
    globalName: 'tmLauncherPerf',
    target: 'es2022',
    platform: 'browser',
    write: false,
  });
  return result.outputFiles[0]!.text;
}

test('builds the page index of 300 entries in 5 ms and answers a query in 2 ms, reporting the first build @perf', async ({ page }) => {
  await page.setContent('<!doctype html><html lang="ar"><title>Launcher search</title></html>');
  await page.addScriptTag({ content: await bundle() });

  // --- In the page: the entries, the first build, the warm builds and the queries ---
  const times = await page.evaluate((): TmLauncherTimes => {
    const search = (window as unknown as TmLauncherPerfWindow).tmLauncherPerf;
    const words = ['Sales', 'Purchase', 'Invoices', 'Receipts', 'Centers', 'Accounts', 'Journal', 'Assets', 'Stock', 'Payroll', 'Budgets', 'Taxes'];
    const arabic = ['المبيعات', 'المشتريات', 'الفواتير', 'الإيصالات', 'المراكز', 'الحسابات', 'اليومية', 'الأصول', 'المخزون', 'الرواتب', 'الموازنات', 'الضرائب'];
    const strings = new Map<string, { readonly label: string; readonly synonyms: string }>();
    const entries = Array.from({ length: 300 }, (_, i) => {
      const [a, b, c] = [i % 12, (i * 5 + 3) % 12, (i * 7 + 1) % 12];
      strings.set(`${i} ar`, { label: `${arabic[a]} ${arabic[b]} ${i}`, synonyms: `${arabic[c]} ${i}` });
      strings.set(`${i} en`, { label: `${words[a]} ${words[b]} ${i}`, synonyms: `${words[c]} register` });
      return {
        id: `${i}`,
        href: `/17/customers?f.E=eq:${i}`,
        label: `${arabic[a]} ${arabic[b]} ${i}`,
        icon: 'list',
        section: 'finance',
        createHref: i % 3 === 0 ? `/17/e${i}/new` : null,
        entity: i % 3 === 0 ? `acme.E${i}` : undefined,
      };
    });
    const sources = {
      language: 'ar',
      entries,
      sections: new Map([['finance', 'المالية']]),
      pins: [],
      recents: [],
      pinned: 'المثبتة',
      recent: 'الزيارات الأخيرة',
      // The terms reader's split of a synonyms string.
      terms: (entry: { readonly id: string }, language: string) => {
        const text = strings.get(`${entry.id} ${language}`)!;
        return {
          label: text.label,
          synonyms: text.synonyms
            .split(/[,،]/)
            .map((part) => part.trim())
            .filter((part) => part !== ''),
        };
      },
      createLabel: (entity: string) => `${entity} · إنشاء`,
      entry: () => undefined,
    };
    const build = (): unknown => search.tmLauncherIndex(search.tmLauncherCandidates(sources));
    /** The fastest of twenty runs after five warm-ups. */
    const fastest = (run: () => unknown): number => {
      for (let i = 0; i < 5; i++) {
        run();
      }
      let best = Infinity;
      for (let i = 0; i < 20; i++) {
        const started = performance.now();
        run();
        best = Math.min(best, performance.now() - started);
      }
      return best;
    };
    const started = performance.now();
    const index = build();
    const first = performance.now() - started;
    const warm = fastest(build);
    const queries: Record<string, number> = {};
    for (const query of ['inv', 'sales inv', 'voice', 'الفوات', 'مراكز']) {
      queries[query] = fastest(() => search.tmLauncherRows(search.tmLauncherMatch(index, search.tmLauncherTokens(query))));
    }
    return { first, warm, queries };
  });

  // --- The budgets, times the multiplier; every figure, the first build's included, goes into the report ---
  test.info().annotations.push({ type: 'launcher index (ms)', description: JSON.stringify(times) });
  expect(times.warm, 'a warm build').toBeLessThanOrEqual(INDEX_MS * PERF_MULTIPLIER);
  for (const [query, took] of Object.entries(times.queries)) {
    expect(took, query).toBeLessThanOrEqual(QUERY_MS * PERF_MULTIPLIER);
  }
});

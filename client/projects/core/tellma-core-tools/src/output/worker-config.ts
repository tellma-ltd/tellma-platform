// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmIsFontForFragments } from '../fonts/index.js';
import type { TmEmittedCore, TmEmittedRouting } from '../emit/emitted-metadata.js';
import { tmEchartsModule, tmEntryPointModule, type TmBuildGraph } from './stats.js';

// The service worker's configuration (spec 0024 §5.3), which ngsw-config
// turns into ngsw.json. Two asset groups split the output: `on-demand`, the
// chunks only the ECharts engine, the report studio or the support entry
// point reach, with the manifest and the icons, cached when first fetched;
// and `shell`, everything else the first paint or a later navigation needs,
// cached at install. Each group lists its files by name, so no pattern can
// sweep in a file by accident. The navigation URLs exclude every path the
// server owns, from the routing words in the Core chunk.

/** The service worker's own files, which the worker never caches as assets. */
export const TM_WORKER_FILES: readonly string[] = Object.freeze(['ngsw-worker.js', 'safety-worker.js', 'ngsw.json']);

/** The file extensions of fonts. */
const FONT = /\.(?:woff2?|ttf|otf|eot)$/i;

/** The two groups' files, as paths relative to the output root. */
export interface TmWorkerGroups {
  /** The files cached at install. */
  readonly shell: readonly string[];
  /** The files cached when first fetched. */
  readonly onDemand: readonly string[];
}

/**
 * The navigation URLs: every path, minus files, Angular's reserved `__`
 * segments and each path the server owns.
 *
 * @param routing - The routing words of the Core chunk.
 * @returns The globs, in ngsw-config's notation.
 */
export function tmNavigationUrls(routing: TmEmittedRouting): string[] {
  const urls = ['/**', '!/**/*.*', '!/**/*__*', '!/**/*__*/**'];
  for (const word of routing.serverFirstSegments) {
    urls.push(`!/${word}`, `!/${word}/**`);
  }
  // A server word under a tenant id is the server's both bare and with a path below it.
  for (const word of routing.serverTenantSegments) {
    urls.push(`!/*/${word}`, `!/*/${word}/**`);
  }
  return urls;
}

/**
 * The chunks reachable only through the on-demand entry points: whatever
 * the initial scripts cannot reach without a dynamic import of a chunk that
 * holds ECharts, `@tellma/core/report-studio` or `@tellma/core/support`. A
 * build without those entry points has no such chunk.
 *
 * @param graph - The build's chunk graph.
 * @param initialScripts - The scripts `index.html` loads.
 * @returns The chunks' paths.
 */
export function tmOnDemandChunks(graph: TmBuildGraph, initialScripts: readonly string[]): Set<string> {
  const reportStudio = tmEntryPointModule('tellma-core', 'report-studio');
  const support = tmEntryPointModule('tellma-core', 'support');
  const roots = new Set(graph.outputsHolding((input) => tmEchartsModule(input) || reportStudio(input) || support(input)));
  const everything = graph.reach(initialScripts);
  const elsewhere = graph.reach(initialScripts, (kind, target) => kind === 'static' || !roots.has(target));
  return new Set([...everything].filter((path) => !elsewhere.has(path)));
}

/**
 * Sorts the output into the worker's groups.
 *
 * @param files - Every deployable file, relative to the output root.
 * @param onDemandChunks - The chunks only the on-demand entry points reach.
 * @param fontFragments - The font name fragments of Latin and the offered scripts.
 * @returns The groups. A font for another script is in neither, so the browser fetches it when a page needs it.
 */
export function tmWorkerGroups(
  files: readonly string[],
  onDemandChunks: ReadonlySet<string>,
  fontFragments: readonly string[],
): TmWorkerGroups {
  const shell: string[] = [];
  const onDemand: string[] = [];
  for (const file of files) {
    if (TM_WORKER_FILES.includes(file) || file.endsWith('.map')) {
      continue;
    }
    if (onDemandChunks.has(file) || file === 'manifest.webmanifest' || file.startsWith('icons/')) {
      onDemand.push(file);
    } else if (!FONT.test(file) || tmIsFontForFragments(file, fontFragments)) {
      shell.push(file);
    }
  }
  return { shell, onDemand };
}

/**
 * A file's path as an ngsw-config glob that matches the file alone.
 * ngsw-config turns each glob into a regular expression, escaping `.` and
 * `+` itself and reading `*` and `?` as wildcards, but passing every other
 * character through, so the regular expression's own specials are escaped
 * here. A literal `*` or `?`, which no Windows file name holds, stays a
 * wildcard that still matches the file. The leading `/` keeps a name from
 * reading as a `!` negation.
 */
function escapeGlob(path: string): string {
  return path.replace(/[()[\]{}^$|\\]/g, '\\$&');
}

/**
 * The worker's configuration, `ngsw-config.json`.
 *
 * @param core - The Core chunk, for the versions and the routing words.
 * @param groups - The asset groups' files.
 * @returns The configuration, ready to serialize.
 */
export function tmWorkerConfig(core: TmEmittedCore, groups: TmWorkerGroups): Record<string, unknown> {
  const urls = (files: readonly string[]): string[] => files.map((file) => `/${escapeGlob(file)}`);
  return {
    index: '/index.html',
    appData: { version: core.distribution.version, platformVersion: core.distribution.platformVersion },
    navigationRequestStrategy: 'performance',
    navigationUrls: tmNavigationUrls(core.routing),
    assetGroups: [
      { name: 'shell', installMode: 'prefetch', updateMode: 'prefetch', resources: { files: urls(groups.shell) } },
      { name: 'on-demand', installMode: 'lazy', updateMode: 'lazy', resources: { files: urls(groups.onDemand) } },
    ],
  };
}

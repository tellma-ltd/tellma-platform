// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { existsSync, readFileSync } from 'node:fs';

import { TmBuildFailure } from '../common/problems.js';

// The chunk graph of a build, read from the esbuild metafile that
// `ng build --stats-json` writes: every output file with the source modules
// it holds and the files it imports, statically or dynamically. The preloads,
// the worker's groups and the budgets all walk this graph. Paths are relative
// to the deployable output, with forward slashes, as the metafile writes them.

/** How one output file imports another. */
export type TmImportKind = 'static' | 'dynamic';

/** One output file of the build. */
export interface TmBuildOutput {
  /** The file's path relative to the deployable output, such as `chunk-ABCD1234.js`. */
  readonly path: string;
  /** The modules it holds, as paths relative to the project folder. */
  readonly inputs: readonly string[];
  /** The JavaScript files it imports, with how. */
  readonly imports: readonly { readonly path: string; readonly kind: TmImportKind }[];
  /** The module the file starts at when it is an entry point or a dynamic import's chunk, relative to the project folder with forward slashes. */
  readonly entryPoint?: string;
}

/** The metafile's shape, as far as the build reads it. */
interface Metafile {
  /** Every output file by its path, with what it imports, the modules it holds and the module it starts at. */
  readonly outputs?: Readonly<Record<string, MetafileOutput>>;
}

/** One output file in the metafile. */
interface MetafileOutput {
  /** The files and modules it imports, with how and whether they are outside the build. */
  readonly imports?: readonly { readonly path: string; readonly kind: string; readonly external?: boolean }[];
  /** The modules it holds, by path relative to the project folder. */
  readonly inputs?: Readonly<Record<string, unknown>>;
  /** The module it starts at, when it is an entry point or a dynamic import's chunk. */
  readonly entryPoint?: string;
}

/** A build's chunk graph. */
export class TmBuildGraph {
  /** Every JavaScript output, by path. */
  readonly outputs: ReadonlyMap<string, TmBuildOutput>;

  /**
   * Creates the graph from the outputs.
   *
   * @param outputs - The build's JavaScript outputs.
   */
  constructor(outputs: readonly TmBuildOutput[]) {
    this.outputs = new Map(outputs.map((output) => [output.path, output]));
  }

  /**
   * Reads the graph from the metafile, keeping the JavaScript outputs the
   * build wrote; source maps and the stylesheets' records are left out.
   *
   * @param file - The metafile `ng build --stats-json` wrote.
   * @returns The graph.
   * @throws TmBuildFailure when the file is missing or malformed.
   */
  static read(file: string): TmBuildGraph {
    if (!existsSync(file)) {
      throw new TmBuildFailure(`${file} does not exist; ng build --stats-json writes it.`);
    }
    let metafile: Metafile;
    try {
      metafile = JSON.parse(readFileSync(file, 'utf8')) as Metafile;
    } catch (error) {
      throw new TmBuildFailure(`${file} is not valid JSON.`, { cause: error });
    }
    const outputs: TmBuildOutput[] = [];
    for (const [path, output] of Object.entries(metafile.outputs ?? {})) {
      if (!/\.m?js$/.test(path)) {
        continue;
      }
      outputs.push({
        path,
        inputs: Object.keys(output.inputs ?? {}).map((input) => input.replace(/\\/g, '/')),
        imports: (output.imports ?? [])
          .filter((edge) => edge.external !== true && /\.m?js$/.test(edge.path))
          .filter((edge) => edge.kind === 'import-statement' || edge.kind === 'dynamic-import')
          .map((edge) => ({ path: edge.path, kind: edge.kind === 'dynamic-import' ? 'dynamic' : 'static' })),
        ...(output.entryPoint === undefined ? {} : { entryPoint: output.entryPoint.replace(/\\/g, '/') }),
      });
    }
    return new TmBuildGraph(outputs);
  }

  /**
   * Walks the graph from some files.
   *
   * @param from - The files to start at.
   * @param follow - Which edges to take, by the edge's kind and target; all by default.
   * @returns Every file reached, the starting files included.
   */
  reach(from: Iterable<string>, follow: (kind: TmImportKind, target: string) => boolean = () => true): Set<string> {
    const reached = new Set<string>();
    const pending = [...from].filter((path) => this.outputs.has(path));
    while (pending.length > 0) {
      const path = pending.pop()!;
      if (reached.has(path)) {
        continue;
      }
      reached.add(path);
      for (const edge of this.outputs.get(path)?.imports ?? []) {
        if (!reached.has(edge.path) && follow(edge.kind, edge.path)) {
          pending.push(edge.path);
        }
      }
    }
    return reached;
  }

  /** The targets of every dynamic import, in ordinal order. */
  dynamicTargets(): string[] {
    const targets = new Set<string>();
    for (const output of this.outputs.values()) {
      for (const edge of output.imports) {
        if (edge.kind === 'dynamic' && this.outputs.has(edge.path)) {
          targets.add(edge.path);
        }
      }
    }
    return [...targets].sort();
  }

  /** The files a set of files loads before it runs: the set and every static import, transitively. */
  staticClosure(from: Iterable<string>): Set<string> {
    return this.reach(from, (kind) => kind === 'static');
  }

  /**
   * The files holding any module the test accepts.
   *
   * @param test - Tests a module's path, relative to the project folder with forward slashes.
   * @returns The files' paths, in ordinal order.
   */
  outputsHolding(test: (input: string) => boolean): string[] {
    return [...this.outputs.values()]
      .filter((output) => output.inputs.some(test))
      .map((output) => output.path)
      .sort();
  }
}

/** Escapes a string for use in a regular expression. */
function escape(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/**
 * A test of a module path for an entry point of a Tellma package, matching
 * both its built bundle (`fesm2022/tellma-core-shell.mjs`) and its sources
 * in a workspace (`tellma-core/shell/…`).
 *
 * @param folder - The package's folder name, such as `tellma-core`.
 * @param entryPoint - The secondary entry point, such as `shell`.
 * @returns The test.
 */
export function tmEntryPointModule(folder: string, entryPoint: string): (input: string) => boolean {
  const bundle = new RegExp(`(?:^|/)${escape(folder)}-${escape(entryPoint)}\\.mjs$`);
  const source = new RegExp(`(?:^|/)${escape(folder)}/${escape(entryPoint)}/`);
  return (input) => bundle.test(input) || source.test(input);
}

/** A test of a module path for the ECharts engine: ECharts itself and its renderer, ZRender. */
export function tmEchartsModule(input: string): boolean {
  return /(?:^|\/)node_modules\/(?:echarts|zrender)\//.test(input);
}

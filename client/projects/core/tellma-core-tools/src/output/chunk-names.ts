// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { createHash } from 'node:crypto';
import { existsSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

import { tmContentHash, tmListFiles } from '../common/files.js';
import { TmBuildFailure } from '../common/problems.js';
import { tmReadInitialFiles } from './preloads.js';

// The chunks' names after Angular's chunk optimizer (spec 0024 §6). The
// optimizer bundles esbuild's chunks again with Rollup, from the main
// entry, so the first frame loads a few files instead of many. It then
// rewrites stats.json to describe Rollup's chunks, but it leaves two things
// the build cannot ship:
//
//  - Rollup names its chunks with a base64url hash (`chunk-Ci8p4wIT.js`),
//    outside the hashed-name rule's `-` and eight or more [A-Z0-9], and the
//    main entry keeps the name esbuild hashed before Rollup changed its
//    bytes, so one name could serve two contents from a year-long cache;
//  - esbuild's chunks that no import reaches stay in the output and in
//    stats.json, importing chunks Rollup merged away.
//
// So, right after ng build, the build settles the output. It walks the
// chunk graph from the scripts and module preloads of index.html, deletes
// every chunk the walk does not reach, once no file of the output names it,
// and gives every chunk it reaches a name of its own content: the stem
// Angular gave it, a `-`, and eight characters of Crockford's base32
// SHA-256. It rewrites every reference with it: the import specifiers in
// the chunks, index.html's scripts and preloads, the source maps' names and
// stats.json. The new hash has the old one's length, so no byte of any
// chunk moves: the hidden source maps and stats.json's byte counts stay
// exact.
//
// A chunk outside an import cycle is named by the hash of its final bytes,
// its imports' new names in place. Chunks in a cycle (Rollup's lazy chunks
// import the main entry, which imports them with import()) cannot each hash
// bytes that hold the others' names, so each is named by the hash of the
// whole cycle seen from it: every member's stem and the digests of its code
// between references, each reference to another member written as the
// order the walk from that chunk met it in, each one outside written as its
// final name. Two chunks share a name only when the cycles behind them
// match byte for byte, so a name always serves one content.
//
// The settling also checks that stats.json is the output's true graph: each
// chunk's imports in the stats are exactly the chunks its text names, and
// every chunk the stats list exists. Where the two disagree, the build
// stops rather than measure or cache a graph that is not the output's.

/** What settling the chunks did. */
export interface TmSettledChunks {
  /** Each chunk's old name, mapped to its new one. */
  readonly renamed: ReadonlyMap<string, string>;
  /** The chunks no import reaches, which were deleted, in ordinal order. */
  readonly removed: readonly string[];
}

/** One import in the metafile, as far as the settling reads it. */
interface MetafileImport {
  /** The imported file or module. */
  path: string;
  /** How it is imported. */
  readonly kind?: string;
  /** Whether it is outside the build. */
  readonly external?: boolean;
}

/** One output in the metafile; members the settling does not read are kept as they are. */
interface MetafileOutput {
  /** What the output imports. */
  imports?: MetafileImport[];
  /** Every other member. */
  readonly [member: string]: unknown;
}

/** The metafile, as far as the settling reads it. */
interface Metafile {
  /** Every output file by its path. */
  outputs?: Record<string, MetafileOutput>;
  /** Every other member. */
  readonly [member: string]: unknown;
}

/** One piece of a chunk's text: code, or a reference to another chunk by name. */
type Piece = { readonly text: string } | { readonly target: string };

/** A chunk's file name: a stem, a `-`, an eight-character hash and the extension. */
const CHUNK_NAME = /^(?<stem>.+)-[A-Za-z0-9_-]{8}(?<extension>\.m?js)$/;

/** A JavaScript output's path. */
const JAVASCRIPT = /\.m?js$/;

/** Escapes a string for use in a regular expression. */
function escape(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/**
 * A pattern of a quoted reference to one of the names: in a chunk, an import
 * specifier (`'./chunk-AB12CD34.js'`); in index.html, an attribute value
 * (`"main-AB12CD34.js"`).
 */
function referencePattern(names: Iterable<string>): RegExp {
  const alternatives = [...names].map(escape).join('|');
  return new RegExp(`(["'\`])(\\./)?(${alternatives})\\1`, 'g');
}

/** Splits a text into code and the references the pattern finds. */
function split(text: string, pattern: RegExp): Piece[] {
  const pieces: Piece[] = [];
  let at = 0;
  for (const match of text.matchAll(pattern)) {
    // The quote and the `./` stay with the code; the reference is the name alone.
    const start = match.index + match[1]!.length + (match[2]?.length ?? 0);
    pieces.push({ text: text.slice(at, start) }, { target: match[3]! });
    at = start + match[3]!.length;
  }
  pieces.push({ text: text.slice(at) });
  return pieces;
}

/** Joins pieces back into text, each reference under its name in the map. */
function assemble(pieces: readonly Piece[], names: ReadonlyMap<string, string>): string {
  return pieces.map((piece) => ('text' in piece ? piece.text : names.get(piece.target)!)).join('');
}

/** The SHA-256 of a text's UTF-8 bytes, in hex. */
function digest(text: string): string {
  return createHash('sha256').update(text, 'utf8').digest('hex');
}

/**
 * The strongly connected components of a graph, each listed after every
 * component it reaches (Tarjan's algorithm).
 *
 * @param nodes - The nodes, in the order to start walks from.
 * @param edges - Each node's targets.
 * @returns The components.
 */
function components(nodes: readonly string[], edges: ReadonlyMap<string, readonly string[]>): string[][] {
  const index = new Map<string, number>();
  const low = new Map<string, number>();
  const stack: string[] = [];
  const onStack = new Set<string>();
  const found: string[][] = [];
  const visit = (node: string): void => {
    index.set(node, index.size);
    low.set(node, index.get(node)!);
    stack.push(node);
    onStack.add(node);
    for (const target of edges.get(node) ?? []) {
      if (!index.has(target)) {
        visit(target);
        low.set(node, Math.min(low.get(node)!, low.get(target)!));
      } else if (onStack.has(target)) {
        low.set(node, Math.min(low.get(node)!, index.get(target)!));
      }
    }
    // A node whose walk found no way back above it closes a component.
    if (low.get(node) === index.get(node)) {
      const component: string[] = [];
      let member: string;
      do {
        member = stack.pop()!;
        onStack.delete(member);
        component.push(member);
      } while (member !== node);
      found.push(component);
    }
  };
  for (const node of nodes) {
    if (!index.has(node)) {
      visit(node);
    }
  }
  return found;
}

/**
 * Settles the chunks of an optimized build: deletes the chunks no import
 * reaches, names every reached chunk by its content, and rewrites every
 * reference to it in the chunks, index.html, the source maps and stats.json.
 *
 * @param browserDir - The deployable output, its source maps still beside their files.
 * @param statsFile - The metafile `ng build --stats-json` wrote.
 * @returns The renames and the deleted chunks.
 * @throws TmBuildFailure when stats.json and the output disagree, when a chunk
 * no import reaches is still named by a file, or when a chunk's name has no
 * eight-character hash to replace.
 */
export function tmSettleChunks(browserDir: string, statsFile: string): TmSettledChunks {
  const metafile = JSON.parse(readFileSync(statsFile, 'utf8')) as Metafile;
  const outputs = metafile.outputs ?? {};
  const path = (file: string): string => join(browserDir, ...file.split('/'));
  const indexFile = path('index.html');
  const html = readFileSync(indexFile, 'utf8');

  // --- The walk from index.html, through every import of the stats ---
  const chunks = new Set(Object.keys(outputs).filter((file) => JAVASCRIPT.test(file)));
  const initial = tmReadInitialFiles(html);
  const roots = [...initial.scripts, ...initial.modulePreloads].filter((file) => chunks.has(file));
  if (roots.length === 0) {
    throw new TmBuildFailure('The built index.html loads no chunk that stats.json lists.');
  }
  const imports = (file: string): string[] =>
    (outputs[file]?.imports ?? []).filter((edge) => edge.external !== true && JAVASCRIPT.test(edge.path)).map((edge) => edge.path);
  const reached = new Set<string>();
  const pending = [...roots];
  while (pending.length > 0) {
    const file = pending.pop()!;
    if (reached.has(file)) {
      continue;
    }
    if (!chunks.has(file) || !existsSync(path(file))) {
      throw new TmBuildFailure(`stats.json says the app loads ${file}, which the build did not write.`);
    }
    reached.add(file);
    pending.push(...imports(file));
  }
  const live = [...reached].sort();

  // --- The chunks no import reaches: deleted once no file names them ---
  const removed = [...chunks].filter((file) => !reached.has(file)).sort();
  if (removed.length > 0) {
    const bases = removed.map((file) => file.split('/').pop()!);
    for (const file of tmListFiles(browserDir)) {
      if (file.endsWith('.map') || removed.includes(file)) {
        continue;
      }
      // Read as Latin-1, so a binary file is searched byte for byte too.
      const text = readFileSync(path(file), 'latin1');
      const mention = bases.find((base) => text.includes(base));
      if (mention !== undefined) {
        throw new TmBuildFailure(`${file} names ${mention}, which no import in stats.json reaches from index.html; the stats are not the output's graph.`);
      }
    }
    for (const file of removed) {
      rmSync(path(file), { force: true });
      rmSync(path(`${file}.map`), { force: true });
    }
  }

  // --- Each reached chunk's references, which must be its imports in the stats ---
  for (const file of live) {
    if (file.includes('/')) {
      throw new TmBuildFailure(`${file} is a chunk below the output's root, where the build expects every chunk.`);
    }
    if (!CHUNK_NAME.test(file)) {
      throw new TmBuildFailure(`${file} has no eight-character hash before its extension for the build to replace with its content hash.`);
    }
  }
  const pattern = referencePattern(live);
  const pieces = new Map(live.map((file) => [file, split(readFileSync(path(file), 'utf8'), pattern)]));
  const edges = new Map<string, string[]>();
  for (const file of live) {
    const named = [...new Set(pieces.get(file)!.flatMap((piece) => ('target' in piece ? [piece.target] : [])))];
    const listed = new Set(imports(file));
    const unlisted = named.filter((target) => !listed.has(target));
    const unnamed = [...listed].filter((target) => !named.includes(target));
    if (unlisted.length > 0 || unnamed.length > 0) {
      throw new TmBuildFailure(
        `stats.json disagrees with ${file}: ${[
          ...unlisted.map((target) => `it names ${target}, which the stats do not list among its imports`),
          ...unnamed.map((target) => `the stats list ${target} among its imports, which it does not name`),
        ].join('; ')}.`,
      );
    }
    edges.set(file, named.sort());
  }

  // --- The new names, each component after every component it reaches ---
  const names = new Map<string, string>();
  const stem = (file: string): string => CHUNK_NAME.exec(file)!.groups!['stem']!;
  const rename = (file: string, hash: string): string => `${stem(file)}-${hash}${CHUNK_NAME.exec(file)!.groups!['extension']!}`;
  for (const component of components(live, edges)) {
    const members = new Set(component);
    const cyclic = component.length > 1 || edges.get(component[0]!)!.includes(component[0]!);
    if (!cyclic) {
      // Outside a cycle: the hash of the final bytes, the imports' new names in place.
      const file = component[0]!;
      names.set(file, rename(file, tmContentHash(Buffer.from(assemble(pieces.get(file)!, names), 'utf8'), 8)));
      continue;
    }
    // In a cycle: the hash of the cycle as the walk from each member meets it.
    const hashes = component.map((start) => {
      const order = [start];
      const met = new Map([[start, 0]]);
      const lines: string[] = [];
      for (let at = 0; at < order.length; at++) {
        const file = order[at]!;
        lines.push(`F${stem(file)}`);
        for (const piece of pieces.get(file)!) {
          if ('text' in piece) {
            lines.push(`T${digest(piece.text)}`);
          } else if (members.has(piece.target)) {
            if (!met.has(piece.target)) {
              met.set(piece.target, order.length);
              order.push(piece.target);
            }
            lines.push(`R${met.get(piece.target)!}`);
          } else {
            lines.push(`N${names.get(piece.target)!}`);
          }
        }
      }
      return tmContentHash(Buffer.from(lines.join('\n'), 'utf8'), 8);
    });
    component.forEach((file, at) => names.set(file, rename(file, hashes[at]!)));
  }

  // --- No two chunks, and no chunk and another file, share a name ---
  const changed = new Map([...names].filter(([old, name]) => old !== name));
  const taken = new Set(tmListFiles(browserDir));
  const given = new Set<string>();
  for (const [old, name] of changed) {
    if (taken.has(name) || given.has(name)) {
      throw new TmBuildFailure(`The content hash of ${old} gives ${name}, which another file of the output already has.`);
    }
    given.add(name);
  }

  // --- Every reference under its new name, checked before anything is written ---
  const texts = new Map(live.map((file) => [file, assemble(pieces.get(file)!, names)]));
  const newHtml = assemble(split(html, pattern), names);
  for (const [file, text] of [['index.html', newHtml] as const, ...[...texts].map(([chunk, text]) => [names.get(chunk)!, text] as const)]) {
    const left = [...changed.keys()].find((old) => text.includes(old));
    if (left !== undefined) {
      throw new TmBuildFailure(`${file} names ${left} outside a quoted import or attribute, where the build cannot rename it.`);
    }
  }

  // --- The chunks, their source maps and index.html, under the new names ---
  for (const file of live) {
    const name = names.get(file)!;
    writeFileSync(path(name), texts.get(file)!);
    const map = path(`${file}.map`);
    if (existsSync(map)) {
      // The map's own name for its file; its sources and mappings are unchanged, since no byte moved.
      const value = JSON.parse(readFileSync(map, 'utf8')) as Record<string, unknown>;
      if (typeof value['file'] === 'string') {
        value['file'] = name;
      }
      if (name !== file) {
        rmSync(map);
      }
      writeFileSync(path(`${name}.map`), JSON.stringify(value));
    }
    if (name !== file) {
      rmSync(path(file));
    }
  }
  writeFileSync(indexFile, newHtml);

  // --- stats.json: the new names, without the deleted chunks or the maps of merged ones ---
  // Other outputs stay as they are: esbuild also lists component stylesheets
  // the application builder inlines rather than writes.
  const settled: Record<string, MetafileOutput> = {};
  for (const [file, output] of Object.entries(outputs)) {
    const base = file.endsWith('.map') ? file.slice(0, -4) : file;
    if (removed.includes(base) || (base !== file && JAVASCRIPT.test(base) && !chunks.has(base))) {
      continue;
    }
    const name = names.get(base);
    settled[name === undefined ? file : base === file ? name : `${name}.map`] = {
      ...output,
      ...(output.imports === undefined ? {} : { imports: output.imports.map((edge) => ({ ...edge, path: names.get(edge.path) ?? edge.path })) }),
    };
  }
  writeFileSync(statsFile, `${JSON.stringify({ ...metafile, outputs: settled }, null, 2)}\n`);
  return { renamed: changed, removed };
}

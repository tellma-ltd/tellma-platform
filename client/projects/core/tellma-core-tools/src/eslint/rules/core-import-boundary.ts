// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { existsSync, readFileSync, statSync } from 'node:fs';
import { dirname, join, relative, resolve, sep } from 'node:path';

import { AST_NODE_TYPES, type TSESTree } from '@typescript-eslint/utils';

import { tmAllowedImports, tmCheckEntryPoints, TM_CORE_ENTRY_POINTS, TM_CORE_PACKAGE } from '../core-entry-points.js';
import { tmCreateRule, tmIsTypeOnly, tmStaticString } from '../rule-helpers.js';

// tm/core-import-boundary (spec 0024 §1). Static imports between the entry
// points of @tellma/core follow one graph, so nothing lazy reaches the eager
// bundle and no two entry points depend on each other. Dynamic imports and
// type-only imports cross any boundary. Outside the package, the rule keeps
// code to the public entry points.

// The table is data the rule trusts, so a broken table fails loudly at load.
const TABLE_PROBLEMS = tmCheckEntryPoints(TM_CORE_ENTRY_POINTS);
if (TABLE_PROBLEMS.length > 0) {
  throw new Error(`The @tellma/core entry-point table is unsound:\n${TABLE_PROBLEMS.join('\n')}`);
}

/** The package a file belongs to: its root folder and its name. */
interface TmOwningPackage {
  /** The folder holding the package's package.json. */
  readonly root: string;
  /** The package's name, or `null` when its manifest has none. */
  readonly name: string | null;
}

/** Owning packages by folder, so each folder's manifest is read once per run. */
const packageCache = new Map<string, TmOwningPackage | null>();

/** The `name` of a package manifest, or `null` when it has none or cannot be read. */
function manifestName(manifest: string): string | null {
  try {
    const parsed = JSON.parse(readFileSync(manifest, 'utf8')) as { readonly name?: unknown };
    return typeof parsed.name === 'string' ? parsed.name : null;
  } catch {
    return null;
  }
}

/** The nearest package above `folder`, or `null` outside any package. */
function owningPackage(folder: string): TmOwningPackage | null {
  const cached = packageCache.get(folder);
  if (cached !== undefined) {
    return cached;
  }
  let found: TmOwningPackage | null = null;
  const manifest = join(folder, 'package.json');
  if (existsSync(manifest)) {
    found = { root: folder, name: manifestName(manifest) };
  } else if (dirname(folder) !== folder) {
    found = owningPackage(dirname(folder));
  }
  packageCache.set(folder, found);
  return found;
}

/**
 * The entry point a path belongs to inside the package at `root`: the nearest
 * folder holding an ng-package.json, as `.` for the package root or
 * `./<folder>` for a secondary entry point. `null` when the path lies outside
 * the package.
 */
function entryPointOf(root: string, path: string): string | null {
  const offset = relative(root, path);
  if (offset.startsWith('..') || resolve(root, offset) !== resolve(path)) {
    return null;
  }
  for (let folder = path; ; folder = dirname(folder)) {
    if (folder === root) {
      return '.';
    }
    if (existsSync(join(folder, 'ng-package.json'))) {
      return `./${relative(root, folder).split(sep).join('/')}`;
    }
    if (dirname(folder) === folder) {
      return null;
    }
  }
}

/** The folder a relative specifier names: the path itself when it is a folder, else its parent. */
function targetFolder(path: string): string {
  try {
    return statSync(path).isDirectory() ? path : dirname(path);
  } catch {
    return dirname(path);
  }
}

/** How a module declaration or call names its module. */
type ImportSite =
  | TSESTree.ImportDeclaration
  | TSESTree.ExportNamedDeclaration
  | TSESTree.ExportAllDeclaration
  | TSESTree.ImportExpression;

/** The options of `tm/core-import-boundary`. */
type Options = [{ readonly allowPrivate?: boolean }];

/**
 * `tm/core-import-boundary`: inside `@tellma/core`, a static import of
 * another entry point goes through `@tellma/core/<entry point>`, never a
 * relative path, and follows the entry-point graph; a type-only or dynamic
 * import crosses any boundary. Everywhere, an import names an entry point and
 * nothing below one, and only platform packages (`allowPrivate`) may import
 * `@tellma/core/private`.
 */
export const tmCoreImportBoundary = tmCreateRule<Options, string>({
  name: 'core-import-boundary',
  meta: {
    type: 'problem',
    docs: {
      description:
        'Keeps static imports between the entry points of @tellma/core on their one graph, and other code on the public entry points.',
    },
    schema: [
      {
        type: 'object',
        properties: { allowPrivate: { type: 'boolean' } },
        additionalProperties: false,
      },
    ],
    messages: {
      relativeAcross:
        "Import '{{target}}' through '{{specifier}}', not a relative path: entry points depend on each other only through their public API.",
      selfImport: "This file belongs to '{{specifier}}'; import its own code with a relative path.",
      unknownEntryPoint: "'{{specifier}}' is not an entry point of @tellma/core.",
      deepImport: "Import '{{entryPoint}}', not a file inside it.",
      eagerToLazy:
        "'{{from}}' loads with the app, so it may not import the lazy '{{target}}' statically. Load it with import(), or import its types with 'import type'.",
      offGraph:
        "'{{from}}' may not import '{{target}}' statically: entry points import each other only down one graph, which keeps them free of cycles. Load it with import(), or import its types with 'import type'.",
      privateEntryPoint:
        "'@tellma/core/private' is wiring shared by the platform's own packages, with no stability guarantee; application code never imports it.",
    },
  },
  defaultOptions: [{ allowPrivate: false }],
  create(context, [{ allowPrivate = false }]) {
    const filename = resolve(context.filename);
    const owner = owningPackage(dirname(filename));
    const inside = owner !== null && owner.name === TM_CORE_PACKAGE;
    const ownEntryPoint = inside ? entryPointOf(owner.root, filename) : null;

    /** Checks one place that names a module. */
    const check = (node: ImportSite, source: TSESTree.Node | null, dynamic: boolean): void => {
      const specifier = tmStaticString(source);
      if (specifier === null || source === null) {
        return;
      }
      const typeOnly = node.type !== AST_NODE_TYPES.ImportExpression && tmIsTypeOnly(node);
      const report = (messageId: string, data: Record<string, string>): void =>
        context.report({ node: source, messageId, data });

      // A relative path inside the package must stay within its own entry point.
      if (inside && ownEntryPoint !== null && specifier.startsWith('.')) {
        const target = entryPointOf(owner.root, targetFolder(resolve(dirname(filename), specifier)));
        if (target !== null && target !== ownEntryPoint) {
          report('relativeAcross', {
            target,
            specifier: target === '.' ? TM_CORE_PACKAGE : `${TM_CORE_PACKAGE}/${target.slice(2)}`,
          });
        }
        return;
      }

      // Only the package's own specifiers matter from here on.
      if (specifier !== TM_CORE_PACKAGE && !specifier.startsWith(`${TM_CORE_PACKAGE}/`)) {
        return;
      }
      const [first, ...deeper] = specifier.slice(TM_CORE_PACKAGE.length + 1).split('/');
      const target = specifier === TM_CORE_PACKAGE ? '.' : `./${first}`;
      if (!(target in TM_CORE_ENTRY_POINTS)) {
        report('unknownEntryPoint', { specifier });
        return;
      }
      if (deeper.length > 0) {
        report('deepImport', { entryPoint: `${TM_CORE_PACKAGE}/${first}` });
        return;
      }
      if (!inside) {
        if (target === './private' && !allowPrivate) {
          report('privateEntryPoint', {});
        }
        return;
      }

      // Inside the package: no self-import, and static values follow the graph.
      if (ownEntryPoint === null) {
        return;
      }
      if (target === ownEntryPoint) {
        report('selfImport', { specifier });
        return;
      }
      if (typeOnly || dynamic || tmAllowedImports(TM_CORE_ENTRY_POINTS, ownEntryPoint).has(target)) {
        return;
      }
      const fromLoad = TM_CORE_ENTRY_POINTS[ownEntryPoint]?.load;
      const targetLoad = TM_CORE_ENTRY_POINTS[target]?.load;
      const name = (id: string): string => (id === '.' ? TM_CORE_PACKAGE : `${TM_CORE_PACKAGE}/${id.slice(2)}`);
      report(fromLoad !== 'lazy' && targetLoad === 'lazy' ? 'eagerToLazy' : 'offGraph', {
        from: name(ownEntryPoint),
        target: name(target),
      });
    };

    return {
      ImportDeclaration: (node) => check(node, node.source, false),
      ExportNamedDeclaration: (node) => check(node, node.source, false),
      ExportAllDeclaration: (node) => check(node, node.source, false),
      ImportExpression: (node) => check(node, node.source, true),
    };
  },
});

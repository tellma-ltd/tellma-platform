// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { AST_NODE_TYPES, type TSESTree } from '@typescript-eslint/utils';

import { tmCreateRule, tmStaticName, tmStaticString } from '../rule-helpers.js';

// tm/lazy-layouts (spec 0024 §2.1). Declarations are eager: they load with
// the app, so a layout a declaration references statically would ship in the
// first bundle of every user. Each layout member is therefore an inline
// `() => import('…')`, which the build turns into a chunk of its own.

/**
 * The declaration members that hold a layout. `panel` is an app-bar slot's
 * panel, which the shell loads the same way.
 */
const LAYOUT_MEMBERS: ReadonlySet<string> = new Set([
  'details',
  'page',
  'create',
  'search',
  'component',
  'display',
  'dialog',
  'home',
  'settingsEditor',
  'panel',
]);

/** A declaration function: `tmEntity`, `tmPage`, `tmFeature`, `tmWidget` and the rest. */
const DECLARATION_FUNCTION = /^tm[A-Z]/;

/** Whether `node` is `import('…')` with a literal specifier. */
function isLiteralImport(node: TSESTree.Node): boolean {
  return node.type === AST_NODE_TYPES.ImportExpression && tmStaticString(node.source) !== null;
}

/**
 * Whether `node` is a lazy loader: an arrow function with no parameters whose
 * body is `import('…')`, optionally followed by one `.then(…)` that picks the
 * export.
 */
function isLazyLoader(node: TSESTree.Node): boolean {
  if (node.type !== AST_NODE_TYPES.ArrowFunctionExpression || node.params.length > 0) {
    return false;
  }
  const body = node.body;
  if (isLiteralImport(body)) {
    return true;
  }
  return (
    body.type === AST_NODE_TYPES.CallExpression &&
    body.callee.type === AST_NODE_TYPES.MemberExpression &&
    tmStaticName(body.callee.property, body.callee.computed) === 'then' &&
    isLiteralImport(body.callee.object)
  );
}

/**
 * `tm/lazy-layouts`: in the arguments of a declaration function, every layout
 * member (`details`, `page`, `create`, `search`, `component`, `display`,
 * `dialog`, `home`, `settingsEditor`, `panel`) must be `() => import('…')`.
 */
export const tmLazyLayouts = tmCreateRule({
  name: 'lazy-layouts',
  meta: {
    type: 'problem',
    docs: {
      description: "Requires every layout member of a declaration to be an inline () => import('…').",
    },
    schema: [],
    messages: {
      notLazy:
        "'{{member}}' must be () => import('…') with a literal path, so the layout loads in a chunk of its own instead of with the app.",
    },
  },
  defaultOptions: [],
  create(context) {
    /**
     * Walks the object and array literals of one declaration's arguments. A
     * nested declaration call is left to its own visit, and functions are not
     * entered: their bodies run later and declare nothing.
     */
    const walk = (node: TSESTree.Node): void => {
      if (node.type === AST_NODE_TYPES.ArrayExpression) {
        for (const element of node.elements) {
          if (element !== null) {
            walk(element);
          }
        }
        return;
      }
      if (node.type === AST_NODE_TYPES.TSAsExpression || node.type === AST_NODE_TYPES.TSSatisfiesExpression) {
        walk(node.expression);
        return;
      }
      if (node.type !== AST_NODE_TYPES.ObjectExpression) {
        return;
      }
      for (const property of node.properties) {
        if (property.type !== AST_NODE_TYPES.Property) {
          continue; // a spread's members are checked where they are written
        }
        const name = tmStaticName(property.key, property.computed);
        if (name !== null && LAYOUT_MEMBERS.has(name)) {
          if (!isLazyLoader(property.value)) {
            context.report({ node: property.value, messageId: 'notLazy', data: { member: name } });
          }
          continue;
        }
        walk(property.value);
      }
    };

    return {
      CallExpression(node) {
        if (node.callee.type === AST_NODE_TYPES.Identifier && DECLARATION_FUNCTION.test(node.callee.name)) {
          node.arguments.forEach(walk);
        }
      },
    };
  },
});

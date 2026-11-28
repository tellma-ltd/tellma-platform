// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { AST_NODE_TYPES, type TSESTree } from '@typescript-eslint/utils';

import { tmCreateRule, tmStaticName, tmStaticString } from '../rule-helpers.js';

// tm/literal-declaration-keys (spec 0024 §2.1, §2.3). Every declaration has
// one key, and the merge rule, overrides, `hide`, pins and stored URLs all
// refer to declarations by it. A key written as a literal can be found in the
// source by anyone, a coding agent included; a computed key can drift from
// what the rest of the code refers to.

/** The declaration functions whose first argument is the declaration's key. */
const KEYED_FUNCTIONS: ReadonlySet<string> = new Set(['tmEntity', 'tmPage', 'tmCodedReport', 'tmWidget', 'tmOverride']);

/** Within `tmFeature`, the arrays whose object elements carry a key, and that key's member. */
const KEYED_ARRAYS: ReadonlyMap<string, string> = new Map([
  ['sections', 'id'],
  ['entries', 'id'],
  ['appBar', 'id'],
  ['settingsTabs', 'category'],
]);

/** Unwraps `x as T` and `x satisfies T`. */
function unwrap(node: TSESTree.Node): TSESTree.Node {
  return node.type === AST_NODE_TYPES.TSAsExpression || node.type === AST_NODE_TYPES.TSSatisfiesExpression
    ? unwrap(node.expression)
    : node;
}

/** The static members of an object literal, by name. */
function members(node: TSESTree.ObjectExpression): Map<string, TSESTree.Property> {
  const found = new Map<string, TSESTree.Property>();
  for (const property of node.properties) {
    if (property.type === AST_NODE_TYPES.Property) {
      const name = tmStaticName(property.key, property.computed);
      if (name !== null) {
        found.set(name, property);
      }
    }
  }
  return found;
}

/**
 * `tm/literal-declaration-keys`: the key of every declaration is a string
 * literal. That covers the first argument of `tmEntity`, `tmPage`,
 * `tmCodedReport`, `tmWidget` and `tmOverride`, and within `tmFeature` its
 * `key`, each `hide` entry, and the `id` (or a settings tab's `category`) of
 * every section, entry and app-bar slot written in place.
 */
export const tmLiteralDeclarationKeys = tmCreateRule({
  name: 'literal-declaration-keys',
  meta: {
    type: 'problem',
    docs: {
      description: 'Requires every declaration key to be a string literal.',
    },
    schema: [],
    messages: {
      notLiteral:
        "The {{what}} must be a string literal, so every reference to the declaration can be found in the source.",
    },
  },
  defaultOptions: [],
  create(context) {
    /** Reports `node` unless it is a literal string. */
    const requireLiteral = (node: TSESTree.Node | undefined, what: string): void => {
      if (node !== undefined && tmStaticString(unwrap(node)) === null) {
        context.report({ node, messageId: 'notLiteral', data: { what } });
      }
    };

    /** Checks the keys a `tmFeature` definition writes in place. */
    const checkFeature = (definition: TSESTree.ObjectExpression): void => {
      const found = members(definition);
      requireLiteral(found.get('key')?.value, "feature's key");

      const hideMember = found.get('hide');
      const hide = hideMember === undefined ? undefined : unwrap(hideMember.value);
      if (hide?.type === AST_NODE_TYPES.ArrayExpression) {
        for (const element of hide.elements) {
          if (element !== null && element.type !== AST_NODE_TYPES.SpreadElement) {
            requireLiteral(element, 'hidden key');
          }
        }
      } else if (hide !== undefined) {
        context.report({ node: hide, messageId: 'notLiteral', data: { what: "'hide' list" } });
      }

      for (const [array, keyMember] of KEYED_ARRAYS) {
        const listMember = found.get(array);
        const list = listMember === undefined ? undefined : unwrap(listMember.value);
        if (list?.type !== AST_NODE_TYPES.ArrayExpression) {
          continue;
        }
        for (const element of list.elements) {
          const item = element === null ? null : unwrap(element);
          if (item?.type === AST_NODE_TYPES.ObjectExpression) {
            requireLiteral(members(item).get(keyMember)?.value, `'${keyMember}' of each of '${array}'`);
          }
        }
      }
    };

    return {
      CallExpression(node) {
        if (node.callee.type !== AST_NODE_TYPES.Identifier) {
          return;
        }
        const name = node.callee.name;
        if (KEYED_FUNCTIONS.has(name)) {
          const [key] = node.arguments;
          if (key !== undefined && key.type !== AST_NODE_TYPES.SpreadElement) {
            requireLiteral(key, `key of ${name}`);
          }
        } else if (name === 'tmFeature') {
          const [argument] = node.arguments;
          const definition = argument === undefined ? undefined : unwrap(argument);
          if (definition?.type === AST_NODE_TYPES.ObjectExpression) {
            checkFeature(definition);
          }
        }
      },
    };
  },
});

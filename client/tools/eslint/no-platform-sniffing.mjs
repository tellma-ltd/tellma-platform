// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * tm/no-platform-sniffing — library code reads `navigator.platform` and
 * `navigator.userAgentData` nowhere but `@tellma/core-ui/kbd`, whose
 * `tmIsApplePlatform` is the one platform check (what Mod means, which
 * modifier a pointer gesture takes). A second check drifts from it, and a
 * grid that reads Mod one way beside a menu that labels it another is the
 * bug this rule prevents. The config scopes the rule to the library and
 * module packages outside `kbd/`.
 *
 * It refuses the two properties read off `navigator` (also through
 * `window`, `self` or `globalThis`, a type assertion, optional chaining or a
 * string-keyed bracket) and destructured from it.
 */
const SNIFFED = new Set(['platform', 'userAgentData']);
const GLOBALS = new Set(['window', 'self', 'globalThis']);

/** Strips the TypeScript wrappers that leave the runtime value unchanged. */
function unwrap(node) {
  let current = node;
  while (
    current.type === 'TSAsExpression' ||
    current.type === 'TSNonNullExpression' ||
    current.type === 'TSSatisfiesExpression' ||
    current.type === 'TSTypeAssertion' ||
    current.type === 'ChainExpression'
  ) {
    current = current.expression;
  }
  return current;
}

/** The static name of a member's or a pattern property's key, or null. */
function keyName(key, computed) {
  if (!computed && key.type === 'Identifier') {
    return key.name;
  }
  if (key.type === 'Literal' && typeof key.value === 'string') {
    return key.value;
  }
  if (key.type === 'TemplateLiteral' && key.expressions.length === 0) {
    return key.quasis[0].value.cooked;
  }
  return null;
}

/** Whether an expression is the global `navigator`, read directly or off a global object. */
function isNavigator(node) {
  const target = unwrap(node);
  if (target.type === 'Identifier') {
    return target.name === 'navigator';
  }
  if (target.type === 'MemberExpression') {
    const owner = unwrap(target.object);
    return (
      owner.type === 'Identifier' &&
      GLOBALS.has(owner.name) &&
      keyName(target.property, target.computed) === 'navigator'
    );
  }
  return false;
}

export default /** @type {import('eslint').Rule.RuleModule} */ ({
  meta: {
    type: 'problem',
    docs: {
      description:
        'Read the platform only through tmIsApplePlatform: no navigator.platform or navigator.userAgentData.',
    },
    schema: [],
    messages: {
      sniffing:
        "Do not read navigator.{{name}} here: take the platform from tmIsApplePlatform in '@tellma/core-ui/kbd', " +
        'the one platform check, so every shortcut and pointer gesture agrees on what Mod means.',
    },
  },
  create(context) {
    return {
      // `navigator.platform`, `navigator?.userAgentData`, `window.navigator['platform']`.
      MemberExpression(node) {
        const name = keyName(node.property, node.computed);
        if (name !== null && SNIFFED.has(name) && isNavigator(node.object)) {
          context.report({ node, messageId: 'sniffing', data: { name } });
        }
      },
      // `const { platform } = navigator`.
      VariableDeclarator(node) {
        if (node.id.type !== 'ObjectPattern' || node.init === null || !isNavigator(node.init)) {
          return;
        }
        for (const property of node.id.properties) {
          if (property.type !== 'Property') {
            continue;
          }
          const name = keyName(property.key, property.computed);
          if (name !== null && SNIFFED.has(name)) {
            context.report({ node: property, messageId: 'sniffing', data: { name } });
          }
        }
      },
    };
  },
});

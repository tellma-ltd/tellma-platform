// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { AST_NODE_TYPES, ASTUtils, ESLintUtils, type TSESLint, type TSESTree } from '@typescript-eslint/utils';

// Helpers the `tm/*` rules share: rule creation, global-name lookups and
// static reads of literal keys and specifiers.

/** Creates a `tm/*` rule; the rules carry their documentation in their descriptions. */
export const tmCreateRule = ESLintUtils.RuleCreator.withoutDocs;

/** The names under which a script reaches the global object. */
const GLOBAL_OBJECTS: ReadonlySet<string> = new Set(['window', 'globalThis', 'self']);

/**
 * Whether `identifier` names the global binding of its name: no import,
 * parameter or declaration in any enclosing scope shadows it. A name that the
 * TypeScript libraries declare (`fetch`, `localStorage`) resolves to an
 * implicit global variable with no definitions, which counts as global too.
 */
export function tmIsGlobalName(
  context: Readonly<TSESLint.RuleContext<string, readonly unknown[]>>,
  identifier: TSESTree.Identifier,
): boolean {
  const variable = ASTUtils.findVariable(context.sourceCode.getScope(identifier), identifier);
  return variable === null || (variable.scope.type === 'global' && variable.defs.length === 0);
}

/**
 * The static name of a member access or object key: an identifier key, a
 * string literal or a template literal without substitutions. `null` when the
 * name is computed at run time.
 */
export function tmStaticName(
  key: TSESTree.Expression | TSESTree.PrivateIdentifier,
  computed: boolean,
): string | null {
  if (!computed && key.type === AST_NODE_TYPES.Identifier) {
    return key.name;
  }
  return tmStaticString(key);
}

/** The text of a string literal or a template literal without substitutions, else `null`. */
export function tmStaticString(node: TSESTree.Node | null | undefined): string | null {
  if (node?.type === AST_NODE_TYPES.Literal && typeof node.value === 'string') {
    return node.value;
  }
  if (node?.type === AST_NODE_TYPES.TemplateLiteral && node.expressions.length === 0) {
    return node.quasis[0]?.value.cooked ?? null;
  }
  return null;
}

/**
 * The global name a node reads, when it reads one: a bare global identifier
 * (`fetch`) or a static member of the global object (`window.fetch`,
 * `globalThis['localStorage']`). `null` for anything else.
 */
export function tmGlobalRead(
  context: Readonly<TSESLint.RuleContext<string, readonly unknown[]>>,
  node: TSESTree.Node,
): string | null {
  if (node.type === AST_NODE_TYPES.Identifier) {
    return tmIsGlobalName(context, node) ? node.name : null;
  }
  if (
    node.type === AST_NODE_TYPES.MemberExpression &&
    node.object.type === AST_NODE_TYPES.Identifier &&
    GLOBAL_OBJECTS.has(node.object.name) &&
    tmIsGlobalName(context, node.object)
  ) {
    return tmStaticName(node.property, node.computed);
  }
  return null;
}

/**
 * Every place a module reads one of `names` as a global value: references the
 * module's scopes leave unresolved, and references to the implicit globals the
 * TypeScript libraries declare. Type-only references (`let s: Storage`) are
 * left out; `typeof localStorage` still reads the value and counts.
 */
export function tmGlobalReferences(
  context: Readonly<TSESLint.RuleContext<string, readonly unknown[]>>,
  program: TSESTree.Program,
  names: ReadonlySet<string>,
): TSESTree.Identifier[] {
  const globalScope = context.sourceCode.getScope(program);
  const found: TSESTree.Identifier[] = [];
  const keep = (reference: TSESLint.Scope.Reference): void => {
    if (reference.isValueReference && reference.identifier.type === AST_NODE_TYPES.Identifier) {
      found.push(reference.identifier);
    }
  };
  for (const reference of globalScope.through) {
    if (names.has(reference.identifier.name)) {
      keep(reference);
    }
  }
  for (const name of names) {
    const variable = globalScope.set.get(name);
    if (variable !== undefined && variable.defs.length === 0) {
      variable.references.forEach(keep);
    }
  }
  return found;
}

/**
 * Whether a module declaration brings in types only: `import type`,
 * `export type`, or an import whose every specifier is an inline `type` one.
 * A bare `import 'x'` runs the module, so it is never type-only.
 */
export function tmIsTypeOnly(
  node: TSESTree.ImportDeclaration | TSESTree.ExportNamedDeclaration | TSESTree.ExportAllDeclaration,
): boolean {
  if (node.type === AST_NODE_TYPES.ImportDeclaration) {
    return (
      node.importKind === 'type' ||
      (node.specifiers.length > 0 &&
        node.specifiers.every(
          (specifier) => specifier.type === AST_NODE_TYPES.ImportSpecifier && specifier.importKind === 'type',
        ))
    );
  }
  if (node.exportKind === 'type') {
    return true;
  }
  return (
    node.type === AST_NODE_TYPES.ExportNamedDeclaration &&
    node.specifiers.length > 0 &&
    node.specifiers.every((specifier) => specifier.exportKind === 'type')
  );
}

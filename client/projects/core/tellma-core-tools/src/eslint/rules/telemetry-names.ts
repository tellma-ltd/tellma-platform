// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { AST_NODE_TYPES, ESLintUtils, type TSESTree } from '@typescript-eslint/utils';
import ts from 'typescript';

import { tmCreateRule, tmStaticName, tmStaticString } from '../rule-helpers.js';

// tm/telemetry-names (spec 0026 §1, §3.2, §9.3, Appendix A.2). Telemetry
// names and page action ids are written as literals throughout the code, and
// a malformed one only throws when that code runs in development. This rule
// checks every literal one where it is written:
//
// - the name a `TmTelemetry`'s `event` or `timing` is called with, against
//   the name grammar, and a platform (`tm.`) name's area against the areas
//   the platform owns;
// - the id of a page action written in place (an object literal with `id`,
//   `labelKey` and `invoke`), and the id `TmPageActions.invoke` is called
//   with, against the action id grammar.
//
// With type information the receiver is recognised by its type, a subclass
// of TmTelemetry or TmPageActions included; without it, by its name
// (`telemetry`, `this.#telemetry`, `inject(TmTelemetry)`; `actions`,
// `inject(TmPageActions)`). A computed name or id is left to the runtime guard.

/** A telemetry name: `<scope>.<area>.<event>[.<more>]`. */
const NAME = /^[a-z][a-z0-9]*(\.[a-z0-9][a-z0-9-]*){2,5}$/;

/** The longest telemetry name. */
const NAME_CHARS = 64;

/** A page action id: two to five parts. */
const ACTION_ID = /^[a-z][a-z0-9]*(\.[a-z0-9][a-z0-9-]*){1,4}$/;

/** The areas of the platform's `tm.` names, each owned by one part of the platform. */
const TM_AREAS: ReadonlySet<string> = new Set([
  // Observability and support.
  'error', 'page', 'vitals', 'api', 'action', 'telemetry', 'support',
  // The app framework.
  'launch', 'boot', 'config', 'sw', 'urls', 'session', 'l10n',
  // Client state.
  'store', 'sync', 'realtime', 'prefs', 'drafts',
  // The shell.
  'shell', 'launcher', 'shortcut', 'attention',
  // Entities, queries and the page families.
  'entity', 'queryex', 'search', 'details', 'admin', 'report', 'studio', 'dashboard', 'home',
]);

/** The telemetry methods whose first argument is a name. */
const NAMED_METHODS: ReadonlySet<string> = new Set(['event', 'timing']);

/** The telemetry service's type. */
const TELEMETRY_TYPE = 'TmTelemetry';

/** The page actions service's type. */
const ACTIONS_TYPE = 'TmPageActions';

/** Without type information, a receiver named like the telemetry service. */
const TELEMETRY_NAME = /^(TmTelemetry|telemetry|.*Telemetry)$/;

/** Without type information, a receiver named like the page actions service. */
const ACTIONS_NAME = /^(TmPageActions|actions|.*Actions)$/;

/** Unwraps `x as T`, `x satisfies T` and `x!`. */
function unwrap(node: TSESTree.Node): TSESTree.Node {
  return node.type === AST_NODE_TYPES.TSAsExpression ||
    node.type === AST_NODE_TYPES.TSSatisfiesExpression ||
    node.type === AST_NODE_TYPES.TSNonNullExpression
    ? unwrap(node.expression)
    : node;
}

/**
 * The name a receiver goes by: an identifier, the last member of an access
 * (`this.#telemetry`), or the token an injection reads (`inject(TmTelemetry)`).
 */
function receiverName(node: TSESTree.Node): string | null {
  const receiver = unwrap(node);
  switch (receiver.type) {
    case AST_NODE_TYPES.Identifier:
      return receiver.name;
    case AST_NODE_TYPES.MemberExpression:
      return receiver.property.type === AST_NODE_TYPES.PrivateIdentifier
        ? receiver.property.name
        : tmStaticName(receiver.property, receiver.computed);
    case AST_NODE_TYPES.CallExpression: {
      const [token] = receiver.arguments;
      return token?.type === AST_NODE_TYPES.Identifier ? token.name : null;
    }
    default:
      return null;
  }
}

/** Whether `type`, a member of it or a base class or interface of it is named `name`. */
function isNamed(checker: ts.TypeChecker, type: ts.Type, name: string): boolean {
  if (type.isUnion() || type.isIntersection()) {
    return type.types.some((member) => isNamed(checker, member, name));
  }
  if (type.getSymbol()?.getName() === name) {
    return true;
  }
  // A generic class's instance type is a reference to the class's own type, which holds its bases.
  const reference = (type.flags & ts.TypeFlags.Object) !== 0 && ((type as ts.ObjectType).objectFlags & ts.ObjectFlags.Reference) !== 0;
  const target = reference ? (type as ts.TypeReference).target : type;
  return target.isClassOrInterface() && checker.getBaseTypes(target).some((base) => isNamed(checker, base, name));
}

/** The literal texts an argument may be: a string, or each branch of a conditional. */
function literalTexts(node: TSESTree.Node): readonly { readonly node: TSESTree.Node; readonly text: string }[] {
  const value = unwrap(node);
  if (value.type === AST_NODE_TYPES.ConditionalExpression) {
    return [...literalTexts(value.consequent), ...literalTexts(value.alternate)];
  }
  const text = tmStaticString(value);
  return text === null ? [] : [{ node: value, text }];
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
 * `tm/telemetry-names`: every literal telemetry name passed to
 * `TmTelemetry.event` or `TmTelemetry.timing` reads
 * `<scope>.<area>.<event>`, in at most 64 characters, a platform name in one
 * of the platform's areas; every literal page action id reads two to five
 * lowercase parts.
 */
export const tmTelemetryNames = tmCreateRule({
  name: 'telemetry-names',
  meta: {
    type: 'problem',
    docs: {
      description: 'Requires literal telemetry names and page action ids to follow their grammars.',
    },
    schema: [],
    messages: {
      badName:
        "'{{name}}' is not a telemetry name. A name reads <scope>.<area>.<event>, three to six dot-separated parts of lowercase letters, digits and hyphens, in at most 64 characters.",
      unknownArea:
        "'tm.{{area}}' is not one of the platform's telemetry areas. A feature's names start with its own key, such as acme.",
      badActionId:
        "'{{id}}' is not a page action id. An id reads <scope>.<area>.<verb>, two to five dot-separated parts of lowercase letters, digits and hyphens.",
    },
  },
  defaultOptions: [],
  create(context) {
    // Type information, when the configuration provides it.
    const services = ESLintUtils.getParserServices(context, true);
    const typed =
      services.program === null
        ? null
        : { checker: services.program.getTypeChecker(), typeAt: (node: TSESTree.Node) => services.getTypeAtLocation(node) };

    /** Whether a receiver is the service of `type`, by its type or else by its name. */
    const isService = (receiver: TSESTree.Node, type: string, name: RegExp): boolean => {
      if (typed !== null) {
        const found = typed.typeAt(receiver);
        if ((found.flags & (ts.TypeFlags.Any | ts.TypeFlags.Unknown)) === 0) {
          return isNamed(typed.checker, found, type);
        }
      }
      const named = receiverName(receiver);
      return named !== null && name.test(named);
    };

    /** Checks a literal telemetry name. */
    const checkName = (node: TSESTree.Node, name: string): void => {
      if (name.length > NAME_CHARS || !NAME.test(name)) {
        context.report({ node, messageId: 'badName', data: { name } });
        return;
      }
      const [scope, area] = name.split('.');
      if (scope === 'tm' && area !== undefined && !TM_AREAS.has(area)) {
        context.report({ node, messageId: 'unknownArea', data: { area } });
      }
    };

    /** Checks a literal page action id. */
    const checkActionId = (node: TSESTree.Node, id: string): void => {
      if (!ACTION_ID.test(id)) {
        context.report({ node, messageId: 'badActionId', data: { id } });
      }
    };

    return {
      // `telemetry.event('…')`, `telemetry.timing('…')` and `actions.invoke('…')`.
      CallExpression(node) {
        const callee = unwrap(node.callee);
        if (callee.type !== AST_NODE_TYPES.MemberExpression || callee.property.type === AST_NODE_TYPES.PrivateIdentifier) {
          return;
        }
        const method = tmStaticName(callee.property, callee.computed);
        const [first] = node.arguments;
        if (method === null || first === undefined || first.type === AST_NODE_TYPES.SpreadElement) {
          return;
        }
        if (NAMED_METHODS.has(method) && isService(callee.object, TELEMETRY_TYPE, TELEMETRY_NAME)) {
          for (const { node: literal, text } of literalTexts(first)) {
            checkName(literal, text);
          }
        } else if (method === 'invoke' && isService(callee.object, ACTIONS_TYPE, ACTIONS_NAME)) {
          for (const { node: literal, text } of literalTexts(first)) {
            checkActionId(literal, text);
          }
        }
      },
      // A page action written in place: `{ id: '…', labelKey: '…', invoke: … }`.
      ObjectExpression(node) {
        const found = members(node);
        const id = found.get('id');
        if (id === undefined || !found.has('labelKey') || !found.has('invoke')) {
          return;
        }
        for (const { node: literal, text } of literalTexts(id.value)) {
          checkActionId(literal, text);
        }
      },
    };
  },
});

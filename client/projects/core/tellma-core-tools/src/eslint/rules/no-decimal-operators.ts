// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ESLintUtils, type TSESTree } from '@typescript-eslint/utils';
import ts from 'typescript';

import { tmCreateRule } from '../rule-helpers.js';

// tm/no-decimal-operators (spec 0024 §10.4). A TmDecimal is its exact wire
// text, so `+` would join two decimals as text and `<` would compare them as
// text ('9' > '10'). TypeScript already refuses `-`, `*` and `/` on a string;
// this rule refuses the operators it accepts. Sums go through TmDecimals.add
// and comparisons through TmDecimals.compare.

/** The binary operators TypeScript accepts on strings. */
const STRING_OPERATORS: ReadonlySet<string> = new Set(['+', '<', '<=', '>', '>=']);

/**
 * The name of the brand symbol that makes a string a `TmDecimal`. A type is a
 * decimal when it carries a property declared with this computed name, so the
 * rule follows aliases, unions and type parameters to the brand itself.
 */
const BRAND = 'tmDecimalBrand';

/** Whether a property is the decimal brand: declared as `[tmDecimalBrand]`. */
function isBrand(property: ts.Symbol): boolean {
  return (property.getDeclarations() ?? []).some(
    (declaration) =>
      (ts.isPropertySignature(declaration) || ts.isPropertyDeclaration(declaration)) &&
      ts.isComputedPropertyName(declaration.name) &&
      ts.isIdentifier(declaration.name.expression) &&
      declaration.name.expression.text === BRAND,
  );
}

/** Whether a value of `type` may be a `TmDecimal`: it, a union member or its constraint carries the brand. */
function mayBeDecimal(checker: ts.TypeChecker, type: ts.Type): boolean {
  if (type.flags & (ts.TypeFlags.Any | ts.TypeFlags.Unknown | ts.TypeFlags.Never)) {
    return false;
  }
  if (type.isUnion()) {
    return type.types.some((member) => mayBeDecimal(checker, member));
  }
  if (type.flags & ts.TypeFlags.TypeParameter) {
    const constraint = checker.getBaseConstraintOfType(type);
    return constraint !== undefined && constraint !== type && mayBeDecimal(checker, constraint);
  }
  return checker.getPropertiesOfType(type).some(isBrand);
}

/**
 * `tm/no-decimal-operators`: refuses `+`, `+=`, `<`, `<=`, `>` and `>=` when
 * an operand may be a `TmDecimal`. Needs type information.
 */
export const tmNoDecimalOperators = tmCreateRule({
  name: 'no-decimal-operators',
  meta: {
    type: 'problem',
    docs: {
      description:
        'Refuses +, +=, <, <=, > and >= on a TmDecimal, which would join or compare its text; use TmDecimals instead.',
    },
    schema: [],
    messages: {
      decimalOperator:
        "'{{operator}}' on a TmDecimal works on its text, not its value. Use {{instead}} instead.",
    },
  },
  defaultOptions: [],
  create(context) {
    const services = ESLintUtils.getParserServices(context);
    const checker = services.program.getTypeChecker();

    /** Reports the expression when either operand may be a decimal. */
    const check = (
      node: TSESTree.BinaryExpression | TSESTree.AssignmentExpression,
      operator: string,
    ): void => {
      const operands = [node.left, node.right] as const;
      if (operands.some((operand) => mayBeDecimal(checker, services.getTypeAtLocation(operand)))) {
        const instead = operator === '+' || operator === '+=' ? 'TmDecimals.add' : 'TmDecimals.compare';
        context.report({ node, messageId: 'decimalOperator', data: { operator, instead } });
      }
    };

    return {
      BinaryExpression(node) {
        if (STRING_OPERATORS.has(node.operator)) {
          check(node, node.operator);
        }
      },
      AssignmentExpression(node) {
        if (node.operator === '+=') {
          check(node, node.operator);
        }
      },
    };
  },
});

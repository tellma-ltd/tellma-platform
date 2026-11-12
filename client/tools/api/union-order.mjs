// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Canonical union order for API Extractor reports.
 *
 * TypeScript keeps a union's members sorted by internal type id, and a type
 * gets its id when the checker first meets it. A union the declaration
 * emitter prints from an inferred type (`input<'row' | 'none'>('row')` emits
 * `InputSignal<"row" | "none">`) therefore lists its members in an order
 * that depends on which files the compiler happened to check first: a clean
 * build, a build that reuses a warm `.angular/cache`, and a single-project
 * `ng build` can each print the same union differently. Union order carries
 * no meaning, so the report sorts every union into one canonical order
 * before it is compared or approved, and the same public API always yields
 * the same golden.
 */
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

// The TypeScript the workspace builds with, resolved from client/ so the
// parser matches the compiler that emitted the declarations.
const require = createRequire(
  join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'package.json'),
);
/** @type {typeof import('typescript')} */
const ts = require('typescript');

/** The fence that opens the TypeScript section of an API report. */
const FENCE_OPEN = '```ts';

/** The fence that closes it. */
const FENCE_CLOSE = '```';

/** The name the report's TypeScript is parsed under (never written to disk). */
const FILE_NAME = 'api-report.ts';

/** The only text allowed before a union's first member: whitespace and an optional leading `|`. */
const LEADING_GAP = /^\s*\|?\s*$/;

/** The only text allowed between two union members: whitespace around one `|`. */
const SEPARATOR_GAP = /^\s*\|\s*$/;

/**
 * @typedef {object} UnionMember
 * @property {import('typescript').TypeNode} node The member's type node.
 * @property {string} text The member's text, with every union inside it already in canonical order.
 */

/**
 * The rank of a union member's kind: members sort by rank first, so the
 * canonical order reads the conventional way — literals, then keyword types
 * (`string`, `unknown`), then named types, then structural types (functions,
 * object literals, arrays), with `null` and then `undefined` last.
 *
 * @param {import('typescript').TypeNode} node The member's type node.
 * @returns {number} The rank, lowest first.
 */
function rankOf(node) {
  if (node.kind === ts.SyntaxKind.UndefinedKeyword) {
    return 5;
  }
  if (ts.isLiteralTypeNode(node)) {
    // `null` parses as a literal type; every other literal type ranks first.
    return node.literal.kind === ts.SyntaxKind.NullKeyword ? 4 : 0;
  }
  if (ts.isTemplateLiteralTypeNode(node)) {
    return 0;
  }
  if (node.kind >= ts.SyntaxKind.FirstKeyword && node.kind <= ts.SyntaxKind.LastKeyword) {
    return 1;
  }
  if (
    ts.isTypeReferenceNode(node) ||
    ts.isExpressionWithTypeArguments(node) ||
    ts.isTypeQueryNode(node) ||
    ts.isImportTypeNode(node) ||
    ts.isIndexedAccessTypeNode(node)
  ) {
    return 2;
  }
  return 3;
}

/**
 * The value of a number or bigint literal member (`8`, `-1`, `10n`), or
 * undefined for any other member, so numeric literals sort by value rather
 * than by text. Numbers and bigints compare with each other exactly.
 *
 * @param {import('typescript').TypeNode} node The member's type node.
 * @returns {number | bigint | undefined} The value, when the member is a number or bigint literal.
 */
function numericValueOf(node) {
  if (!ts.isLiteralTypeNode(node)) {
    return undefined;
  }
  // A negative literal parses as a prefix minus over the positive literal.
  const { literal } = node;
  const negative =
    ts.isPrefixUnaryExpression(literal) && literal.operator === ts.SyntaxKind.MinusToken;
  const operand = negative ? literal.operand : literal;
  /** @type {number | bigint} */
  let value;
  if (ts.isNumericLiteral(operand)) {
    // The parser normalizes a number's text: no separators, hex and octal in decimal.
    value = Number(operand.text);
  } else if (ts.isBigIntLiteral(operand)) {
    // A bigint's text keeps its radix prefix and `n` suffix (`0x10n`); BigInt reads the prefix.
    value = BigInt(operand.text.slice(0, -1));
  } else {
    return undefined;
  }
  return negative ? -value : value;
}

/**
 * The sub-rank of a literal (rank 0) member: string literals, then number
 * and bigint literals, then the rest (`true`, `false`, template literals).
 * Numeric literals form one block compared by value, which keeps the order
 * total when numbers and bigints are mixed.
 *
 * @param {import('typescript').TypeNode} node The member's type node.
 * @returns {number} The sub-rank, lowest first.
 */
function literalSubRankOf(node) {
  if (ts.isLiteralTypeNode(node) && ts.isStringLiteral(node.literal)) {
    return 0;
  }
  return numericValueOf(node) !== undefined ? 1 : 2;
}

/**
 * The canonical order of two union members: by kind rank; among literals,
 * by sub-rank and then numeric literals by value; then by text in UTF-16
 * code units. This is a total order that no locale setting can change.
 *
 * @param {UnionMember} a The first member.
 * @param {UnionMember} b The second member.
 * @returns {number} Negative when `a` sorts first, positive when `b` does, 0 when equal.
 */
function compareMembers(a, b) {
  const rank = rankOf(a.node);
  const byRank = rank - rankOf(b.node);
  if (byRank !== 0) {
    return byRank;
  }
  if (rank === 0) {
    const bySubRank = literalSubRankOf(a.node) - literalSubRankOf(b.node);
    if (bySubRank !== 0) {
      return bySubRank;
    }
    // Both numeric, or neither: `<` compares a number with a bigint exactly,
    // and equal values (`1 | 1n`) fall through to the text.
    const valueA = numericValueOf(a.node);
    const valueB = numericValueOf(b.node);
    if (valueA !== undefined && valueB !== undefined) {
      if (valueA < valueB) {
        return -1;
      }
      if (valueA > valueB) {
        return 1;
      }
    }
  }
  return a.text < b.text ? -1 : a.text > b.text ? 1 : 0;
}

/**
 * Sorts the members of every union type in a piece of TypeScript
 * declaration source into canonical order (literals, keyword types, named
 * types, structural types, then `null` and `undefined`; number and bigint
 * literals by value, everything else by text). Inner unions are sorted
 * first, so an outer union compares its members by their canonical text.
 *
 * Only the members move: the separators stay where they were written (the
 * i-th sorted member takes the i-th member's place), so line breaks and a
 * leading `|` survive, and the result depends on the set of members and the
 * layout, never on the order the members came in. A union with anything but
 * whitespace and `|` before or between its members (in a parsed union, only
 * a comment) keeps its written order: the emitter only carries comments on a
 * type it copied from source, whose order is the author's and stable, and
 * moving the members would detach them from their comments. Intersections
 * are never reordered: their order can be meaningful.
 *
 * @param {string} source Declaration source, as found in a report's TypeScript fence.
 * @returns {string} The same source with every union in canonical order.
 * @throws {Error} When the source does not parse as TypeScript, because a
 *   misparsed declaration could otherwise be rewritten silently.
 */
export function sortUnionMembers(source) {
  // transpileModule reports the syntactic diagnostics only, which is exactly
  // the check needed: the report's declarations reference types it does not
  // declare, so a full type check would never pass.
  const { diagnostics = [] } = ts.transpileModule(source, {
    fileName: FILE_NAME,
    reportDiagnostics: true,
    compilerOptions: { target: ts.ScriptTarget.Latest, module: ts.ModuleKind.ESNext },
  });
  if (diagnostics.length > 0) {
    const messages = diagnostics.map((diagnostic) =>
      ts.flattenDiagnosticMessageText(diagnostic.messageText, '\n'),
    );
    throw new Error(`The API report does not parse as TypeScript: ${messages.join('; ')}`);
  }

  const sourceFile = ts.createSourceFile(
    FILE_NAME,
    source,
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.TS,
  );

  /**
   * Whether a union's text before or between its members holds anything but
   * whitespace and the `|` separators, which in a parsed union can only be a
   * comment. The text before the first member is read from the end of the
   * preceding token, so a comment right after `=` or `<` counts too.
   *
   * @param {import('typescript').UnionTypeNode} union The union to inspect.
   * @returns {boolean} True when a comment sits before or between members.
   */
  const hasCommentsBetweenMembers = (union) =>
    !LEADING_GAP.test(source.slice(union.pos, union.types[0].getStart(sourceFile))) ||
    union.types.some(
      (member, index) =>
        index > 0 &&
        !SEPARATOR_GAP.test(source.slice(union.types[index - 1].end, member.getStart(sourceFile))),
    );

  /**
   * The text of a node from its first token to its end, with every union
   * inside it in canonical order.
   *
   * @param {import('typescript').Node} node The node to render.
   * @returns {string} The node's normalized text.
   */
  const render = (node) => {
    if (ts.isUnionTypeNode(node) && !hasCommentsBetweenMembers(node)) {
      const { types } = node;
      const sorted = types
        .map((member) => ({ node: member, text: render(member) }))
        .sort(compareMembers);
      // The written layout, by position: the leading `|` (if any), then the
      // i-th sorted member after the separator written before the i-th
      // member. A union already in canonical order comes out as written.
      let text = source.slice(node.getStart(sourceFile), types[0].getStart(sourceFile));
      sorted.forEach((member, index) => {
        if (index > 0) {
          text += source.slice(types[index - 1].end, types[index].getStart(sourceFile));
        }
        text += member.text;
      });
      return text + source.slice(types[types.length - 1].end, node.end);
    }
    return renderChildren(node);
  };

  /**
   * The text of a node with each child rendered by `render`; the text
   * between children (punctuation, whitespace, comments) is copied as
   * written.
   *
   * @param {import('typescript').Node} node The node whose children to render.
   * @returns {string} The node's text with its children normalized.
   */
  const renderChildren = (node) => {
    let text = '';
    let cursor = node.getStart(sourceFile);
    ts.forEachChild(node, (child) => {
      text += source.slice(cursor, child.getStart(sourceFile)) + render(child);
      cursor = child.end;
    });
    return text + source.slice(cursor, node.end);
  };

  // The source file's span starts at its first token; the trivia before it
  // is kept as written.
  return source.slice(0, sourceFile.getStart(sourceFile)) + renderChildren(sourceFile);
}

/**
 * Puts every union in an API Extractor report's TypeScript fence into
 * canonical order (see {@link sortUnionMembers}). Line endings become LF
 * first: the extractor writes CRLF on Windows, and a fence line ending in
 * `\r` would go unrecognized and leave the report silently unsorted. Beyond
 * that, the markdown outside the fence is returned untouched, and so is a
 * report with no fence.
 *
 * @param {string} report An API Extractor report, with LF or CRLF line endings.
 * @returns {string} The report with LF line endings and every union in its fence in canonical order.
 */
export function normalizeUnionOrder(report) {
  const lines = report.replace(/\r\n?/g, '\n').split('\n');
  const open = lines.indexOf(FENCE_OPEN);
  const close = lines.lastIndexOf(FENCE_CLOSE);
  if (open === -1 || close <= open) {
    return lines.join('\n');
  }
  const body = lines.slice(open + 1, close).join('\n');
  return [...lines.slice(0, open + 1), sortUnionMembers(body), ...lines.slice(close)].join('\n');
}

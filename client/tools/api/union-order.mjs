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
 * The numeric value of a number-literal member (`8`, `-1`), or undefined for
 * any other member, so numeric literals sort by value rather than by text.
 *
 * @param {import('typescript').TypeNode} node The member's type node.
 * @returns {number | undefined} The value, when the member is a number literal.
 */
function numericValueOf(node) {
  if (!ts.isLiteralTypeNode(node)) {
    return undefined;
  }
  const { literal } = node;
  if (ts.isNumericLiteral(literal)) {
    return Number(literal.text);
  }
  if (
    ts.isPrefixUnaryExpression(literal) &&
    literal.operator === ts.SyntaxKind.MinusToken &&
    ts.isNumericLiteral(literal.operand)
  ) {
    return -Number(literal.operand.text);
  }
  return undefined;
}

/**
 * The canonical order of two union members: by kind rank, then number
 * literals by value, then by text in UTF-16 code units — a total order that
 * no locale setting can change.
 *
 * @param {UnionMember} a The first member.
 * @param {UnionMember} b The second member.
 * @returns {number} Negative when `a` sorts first, positive when `b` does, 0 when equal.
 */
function compareMembers(a, b) {
  const byRank = rankOf(a.node) - rankOf(b.node);
  if (byRank !== 0) {
    return byRank;
  }
  const valueA = numericValueOf(a.node);
  const valueB = numericValueOf(b.node);
  if (valueA !== undefined && valueB !== undefined && valueA !== valueB) {
    return valueA - valueB;
  }
  return a.text < b.text ? -1 : a.text > b.text ? 1 : 0;
}

/**
 * Sorts the members of every union type in a piece of TypeScript
 * declaration source into canonical order (literals, keyword types, named
 * types, structural types, then `null` and `undefined`; number literals by
 * value, everything else by text). Inner unions are sorted first, so an
 * outer union compares its members by their canonical text.
 *
 * A union already in canonical order keeps its text as written, line breaks
 * included; one out of order is rewritten as its sorted members joined by
 * `" | "`. A union with a comment between its members keeps its written
 * order: the emitter only carries comments on a type it copied from source,
 * whose order is the author's and stable, and moving the members would
 * detach them from their comments. Intersections are never reordered: their
 * order can be meaningful.
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
   * Whether a union holds a comment before any member, on either side of
   * the `|` that precedes it.
   *
   * @param {import('typescript').UnionTypeNode} union The union to inspect.
   * @returns {boolean} True when a comment sits between members.
   */
  const hasCommentsBetweenMembers = (union) =>
    union.types.some(
      (member, index) =>
        // The trivia right before the member: after its `|`, or before the
        // first member …
        (ts.getLeadingCommentRanges(source, member.pos) ?? []).length > 0 ||
        // … and the trivia after the previous member, before that `|`.
        (index > 0 &&
          (ts.getLeadingCommentRanges(source, union.types[index - 1].end) ?? []).length > 0),
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
      const members = node.types.map((member) => ({ node: member, text: render(member) }));
      const sorted = [...members].sort(compareMembers);
      if (sorted.some((member, index) => member !== members[index])) {
        return sorted.map((member) => member.text).join(' | ');
      }
      // Already canonical: keep the written text, separators and line
      // breaks included (the inner unions are still normalized below).
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
 * canonical order (see {@link sortUnionMembers}). The markdown outside the
 * fence is returned untouched, and so is a report with no fence.
 *
 * @param {string} report An API Extractor report with LF line endings.
 * @returns {string} The report with every union in its fence in canonical order.
 */
export function normalizeUnionOrder(report) {
  const lines = report.split('\n');
  const open = lines.indexOf(FENCE_OPEN);
  const close = lines.lastIndexOf(FENCE_CLOSE);
  if (open === -1 || close <= open) {
    return report;
  }
  const body = lines.slice(open + 1, close).join('\n');
  return [...lines.slice(0, open + 1), sortUnionMembers(body), ...lines.slice(close)].join('\n');
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TSESTree } from '@typescript-eslint/utils';

import { tmCreateRule, tmGlobalRead, tmGlobalReferences, tmIsTypeOnly, tmStaticString } from '../rule-helpers.js';

// tm/no-raw-http (spec 0024 §2.1, §10). Every request goes through TmApi,
// which adds the headers, the user check at both ends of the call, the
// retries, the cancellation and the error mapping. A raw request skips all
// of them.

/** The browser's request APIs. */
const RAW_HTTP: ReadonlySet<string> = new Set(['fetch', 'XMLHttpRequest']);

/** Angular's HTTP client, and every module below it. */
function isAngularHttp(source: string | null): boolean {
  return source === '@angular/common/http' || (source?.startsWith('@angular/common/http/') ?? false);
}

/**
 * `tm/no-raw-http`: refuses `fetch`, `XMLHttpRequest` and Angular's
 * `HttpClient` (any value import of `@angular/common/http`).
 */
export const tmNoRawHttp = tmCreateRule({
  name: 'no-raw-http',
  meta: {
    type: 'problem',
    docs: {
      description: 'Refuses fetch, XMLHttpRequest and HttpClient; every request goes through TmApi.',
    },
    schema: [],
    messages: {
      rawHttp:
        "'{{name}}' sends a request outside TmApi, which adds the headers, the user check, retries and cancellation. Call TmApi.read or TmApi.write instead.",
    },
  },
  defaultOptions: [],
  create(context) {
    const report = (node: TSESTree.Node, name: string): void =>
      context.report({ node, messageId: 'rawHttp', data: { name } });

    /** Reports a module declaration that brings in Angular's HTTP client as a value. */
    const checkModule = (
      node: TSESTree.ImportDeclaration | TSESTree.ExportNamedDeclaration | TSESTree.ExportAllDeclaration,
    ): void => {
      if (node.source !== null && isAngularHttp(node.source.value) && !tmIsTypeOnly(node)) {
        report(node.source, 'HttpClient');
      }
    };

    return {
      ImportDeclaration: checkModule,
      ExportNamedDeclaration: checkModule,
      ExportAllDeclaration: checkModule,
      ImportExpression(node) {
        if (isAngularHttp(tmStaticString(node.source))) {
          report(node.source, 'HttpClient');
        }
      },
      // `window.fetch` and its spellings.
      MemberExpression(node) {
        const name = tmGlobalRead(context, node);
        if (name !== null && RAW_HTTP.has(name)) {
          report(node, name);
        }
      },
      // A bare `fetch`, once every scope is known.
      'Program:exit'(program) {
        for (const identifier of tmGlobalReferences(context, program, RAW_HTTP)) {
          report(identifier, identifier.name);
        }
      },
    };
  },
});

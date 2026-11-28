// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TSESTree } from '@typescript-eslint/utils';

import { tmCreateRule, tmGlobalRead, tmGlobalReferences } from '../rule-helpers.js';

// tm/no-web-storage (spec 0024 §2.1). The platform owns everything a tab keeps
// in the browser: its store is keyed by user and tenant and is swept when a
// session ends, so a distribution that writes the browser's storage directly
// would keep one user's data within the next user's reach.

/** The browser storage APIs a distribution never touches directly. */
const STORAGE: ReadonlySet<string> = new Set(['localStorage', 'sessionStorage', 'indexedDB', 'caches']);

/**
 * `tm/no-web-storage`: refuses reads of `localStorage`, `sessionStorage`,
 * `indexedDB` and `caches`, bare or through `window`, `globalThis` or `self`.
 */
export const tmNoWebStorage = tmCreateRule({
  name: 'no-web-storage',
  meta: {
    type: 'problem',
    docs: {
      description:
        "Refuses direct use of the browser's storage; the platform's client store keeps data per user and clears it at sign-out.",
    },
    schema: [],
    messages: {
      webStorage:
        "'{{name}}' is the browser's own storage. Keep data in the platform's client store, which is per user and is cleared when the session ends.",
    },
  },
  defaultOptions: [],
  create(context) {
    const report = (node: TSESTree.Node, name: string): void =>
      context.report({ node, messageId: 'webStorage', data: { name } });

    return {
      // `window.localStorage` and its spellings.
      MemberExpression(node) {
        const name = tmGlobalRead(context, node);
        if (name !== null && STORAGE.has(name)) {
          report(node, name);
        }
      },
      // A bare `localStorage`, once every scope is known.
      'Program:exit'(program) {
        for (const identifier of tmGlobalReferences(context, program, STORAGE)) {
          report(identifier, identifier.name);
        }
      },
    };
  },
});

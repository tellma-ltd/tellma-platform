// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import tseslint from 'typescript-eslint';
import { describe } from 'vitest';

import { createRuleTester } from '../../../fixtures/rule-tester.js';
import { tmNoWebStorage } from './no-web-storage.js';

// Each case below runs as a test of its own (see createRuleTester).
const tester = createRuleTester({
  languageOptions: { parser: tseslint.parser as never, ecmaVersion: 2022, sourceType: 'module' },
});

describe('tm/no-web-storage', () => {
  describe("refuses the browser's storage, bare or through the global object", () => {
    tester.run('tm/no-web-storage', tmNoWebStorage as never, {
      valid: [
        { code: 'function read(localStorage: Storage) { return localStorage.getItem("x"); }' },
        { code: 'const store = { localStorage: 1 }; void store.localStorage;' },
        { code: 'let session: Storage | null = null; void session;' },
        { code: "const key = 'sessionStorage'; void key;" },
      ],
      invalid: [
        {
          code: "localStorage.setItem('x', '1');",
          errors: [{ messageId: 'webStorage', data: { name: 'localStorage' } }],
        },
        {
          code: "window.sessionStorage.getItem('x');",
          errors: [{ messageId: 'webStorage', data: { name: 'sessionStorage' } }],
        },
        {
          code: "globalThis['localStorage'].clear();",
          errors: [{ messageId: 'webStorage', data: { name: 'localStorage' } }],
        },
        {
          code: "const request = indexedDB.open('acme');",
          errors: [{ messageId: 'webStorage', data: { name: 'indexedDB' } }],
        },
        {
          code: "await self.caches.open('acme');",
          errors: [{ messageId: 'webStorage', data: { name: 'caches' } }],
        },
      ],
    });
  });
});

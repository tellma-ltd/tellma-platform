// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { RuleTester } from 'eslint';
import tseslint from 'typescript-eslint';
import { describe, it } from 'vitest';

import { tmNoRawHttp } from './no-raw-http.js';

const tester = new RuleTester({
  languageOptions: { parser: tseslint.parser as never, ecmaVersion: 2022, sourceType: 'module' },
});

describe('tm/no-raw-http', () => {
  it('refuses fetch and XMLHttpRequest, bare or through the global object', () => {
    tester.run('tm/no-raw-http', tmNoRawHttp as never, {
      valid: [
        // A local binding of the same name is not the browser's.
        { code: 'function load(fetch: () => void) { fetch(); }' },
        { code: "import { fetch } from './my-fetch'; fetch();" },
        { code: 'const api = { fetch: () => 1 }; api.fetch();' },
        // A type mentions no request.
        { code: 'let request: XMLHttpRequest | undefined; void request;' },
        { code: "const name = 'fetch'; void name;" },
      ],
      invalid: [
        { code: "fetch('/api/x');", errors: [{ messageId: 'rawHttp', data: { name: 'fetch' } }] },
        { code: "await window.fetch('/api/x');", errors: [{ messageId: 'rawHttp', data: { name: 'fetch' } }] },
        { code: "globalThis['fetch']('/api/x');", errors: [{ messageId: 'rawHttp', data: { name: 'fetch' } }] },
        { code: 'const send = self.fetch;', errors: [{ messageId: 'rawHttp', data: { name: 'fetch' } }] },
        {
          code: 'const request = new XMLHttpRequest();',
          errors: [{ messageId: 'rawHttp', data: { name: 'XMLHttpRequest' } }],
        },
        { code: 'const f = typeof fetch;', errors: [{ messageId: 'rawHttp', data: { name: 'fetch' } }] },
      ],
    });
  });

  it("refuses Angular's HttpClient: any value import of @angular/common/http", () => {
    tester.run('tm/no-raw-http', tmNoRawHttp as never, {
      valid: [
        { code: "import type { HttpErrorResponse } from '@angular/common/http';" },
        { code: "import { type HttpHeaders } from '@angular/common/http';" },
        { code: "import { DatePipe } from '@angular/common';" },
      ],
      invalid: [
        {
          code: "import { HttpClient } from '@angular/common/http';",
          errors: [{ messageId: 'rawHttp', data: { name: 'HttpClient' } }],
        },
        {
          code: "import { provideHttpClient } from '@angular/common/http';",
          errors: [{ messageId: 'rawHttp' }],
        },
        {
          code: "import { HttpTestingController } from '@angular/common/http/testing';",
          errors: [{ messageId: 'rawHttp' }],
        },
        { code: "export { HttpClient } from '@angular/common/http';", errors: [{ messageId: 'rawHttp' }] },
        { code: "const http = await import('@angular/common/http');", errors: [{ messageId: 'rawHttp' }] },
      ],
    });
  });
});

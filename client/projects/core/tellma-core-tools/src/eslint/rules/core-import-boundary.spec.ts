// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { join } from 'node:path';

import { RuleTester } from 'eslint';
import tseslint from 'typescript-eslint';
import { describe, it } from 'vitest';

import { tmCoreImportBoundary } from './core-import-boundary.js';

// The fixture is a miniature @tellma/core on disk: a package.json named
// '@tellma/core', an ng-package.json at its root (the primary) and one in each
// secondary entry point folder. The linted code is inline; only its file name
// places it in the tree.
const fixture = join(import.meta.dirname, '..', 'fixtures', 'boundary');
const core = (path: string): string => join(fixture, 'tellma-core', path);
const app = (path: string): string => join(fixture, 'app', path);

const tester = new RuleTester({
  languageOptions: { parser: tseslint.parser as never, ecmaVersion: 2022, sourceType: 'module' },
});

describe('tm/core-import-boundary', () => {
  it('lets each eager entry point import only down the graph', () => {
    tester.run('tm/core-import-boundary', tmCoreImportBoundary as never, {
      valid: [
        { code: "import { a } from '@tellma/core';", filename: core('attention/a.ts') },
        { code: "import { a } from '@tellma/core';", filename: core('shell/a.ts') },
        { code: "import { b } from '@tellma/core/attention';", filename: core('shell/a.ts') },
        { code: "import { b } from '@tellma/core/private';", filename: core('shell/a.ts') },
        { code: "import { a } from '@tellma/core';", filename: core('private/a.ts') },
        { code: "import { x } from './x';", filename: core('src/a.ts') },
        { code: "import { x } from '../json/x';", filename: core('src/observe/a.ts') },
        { code: "import { x } from './internal/x';", filename: core('shell/a.ts') },
      ],
      invalid: [
        {
          code: "import { b } from '@tellma/core/attention';",
          filename: core('src/a.ts'),
          errors: [{ messageId: 'offGraph' }],
        },
        {
          code: "import { b } from '@tellma/core/private';",
          filename: core('src/a.ts'),
          errors: [{ messageId: 'offGraph' }],
        },
        {
          code: "import { s } from '@tellma/core/shell';",
          filename: core('attention/a.ts'),
          errors: [{ messageId: 'offGraph' }],
        },
        {
          code: "import { s } from '@tellma/core/shell';",
          filename: core('private/a.ts'),
          errors: [{ messageId: 'offGraph' }],
        },
      ],
    });
  });

  it('refuses an eager import of a lazy entry point, and lets import() and import type cross', () => {
    tester.run('tm/core-import-boundary', tmCoreImportBoundary as never, {
      valid: [
        { code: "const load = () => import('@tellma/core/search');", filename: core('src/a.ts') },
        { code: "import type { S } from '@tellma/core/search';", filename: core('src/a.ts') },
        { code: "import { type S } from '@tellma/core/search';", filename: core('shell/a.ts') },
        { code: "export type { S } from '@tellma/core/details';", filename: core('attention/a.ts') },
      ],
      invalid: [
        {
          code: "import { TmSearchHost } from '@tellma/core/search';",
          filename: core('src/a.ts'),
          errors: [{ messageId: 'eagerToLazy' }],
        },
        {
          code: "import { TmSearchHost } from '@tellma/core/search';",
          filename: core('shell/a.ts'),
          errors: [{ messageId: 'eagerToLazy' }],
        },
        {
          code: "export { TmDetailsHost } from '@tellma/core/details';",
          filename: core('attention/a.ts'),
          errors: [{ messageId: 'eagerToLazy' }],
        },
        {
          code: "export * from '@tellma/core/entity';",
          filename: core('private/a.ts'),
          errors: [{ messageId: 'eagerToLazy' }],
        },
        {
          code: "import '@tellma/core/search';",
          filename: core('src/a.ts'),
          errors: [{ messageId: 'eagerToLazy' }],
        },
      ],
    });
  });

  it('lets a lazy entry point import the eager ones and the lazy ones it lists, and nothing that closes a cycle', () => {
    tester.run('tm/core-import-boundary', tmCoreImportBoundary as never, {
      valid: [
        { code: "import { a } from '@tellma/core';", filename: core('search/a.ts') },
        { code: "import { s } from '@tellma/core/shell';", filename: core('search/a.ts') },
        { code: "import { p } from '@tellma/core/private';", filename: core('search/a.ts') },
        { code: "import { e } from '@tellma/core/entity';", filename: core('search/a.ts') },
        { code: "import { e } from '@tellma/core/entity';", filename: core('details/a.ts') },
      ],
      invalid: [
        {
          code: "import { s } from '@tellma/core/search';",
          filename: core('entity/a.ts'),
          errors: [{ messageId: 'offGraph' }],
        },
        {
          code: "import { d } from '@tellma/core/details';",
          filename: core('search/a.ts'),
          errors: [{ messageId: 'offGraph' }],
        },
      ],
    });
  });

  it('refuses relative imports across entry points and imports of its own entry point by name', () => {
    tester.run('tm/core-import-boundary', tmCoreImportBoundary as never, {
      valid: [],
      invalid: [
        {
          code: "import { s } from '../shell';",
          filename: core('src/a.ts'),
          errors: [{ messageId: 'relativeAcross' }],
        },
        {
          code: "import { s } from '../../shell/x';",
          filename: core('src/observe/a.ts'),
          errors: [{ messageId: 'relativeAcross' }],
        },
        {
          code: "import { x } from '../src/json/x';",
          filename: core('shell/a.ts'),
          errors: [{ messageId: 'relativeAcross' }],
        },
        {
          code: "const load = () => import('../search/x');",
          filename: core('src/a.ts'),
          errors: [{ messageId: 'relativeAcross' }],
        },
        {
          code: "import { a } from '@tellma/core';",
          filename: core('src/a.ts'),
          errors: [{ messageId: 'selfImport' }],
        },
        {
          code: "import { a } from '@tellma/core/shell';",
          filename: core('shell/a.ts'),
          errors: [{ messageId: 'selfImport' }],
        },
      ],
    });
  });

  it('keeps other packages on the public entry points', () => {
    tester.run('tm/core-import-boundary', tmCoreImportBoundary as never, {
      valid: [
        { code: "import { a } from '@tellma/core';", filename: app('src/a.ts') },
        { code: "import { s } from '@tellma/core/search';", filename: app('src/a.ts') },
        { code: "import { c } from '@tellma/core-ui';", filename: app('src/a.ts') },
        {
          code: "import { p } from '@tellma/core/private';",
          filename: app('src/a.ts'),
          options: [{ allowPrivate: true }],
        },
      ],
      invalid: [
        {
          code: "import { p } from '@tellma/core/private';",
          filename: app('src/a.ts'),
          errors: [{ messageId: 'privateEntryPoint' }],
        },
        {
          code: "import type { P } from '@tellma/core/private';",
          filename: app('src/a.ts'),
          errors: [{ messageId: 'privateEntryPoint' }],
        },
        {
          code: "import { x } from '@tellma/core/search/internal/x';",
          filename: app('src/a.ts'),
          errors: [{ messageId: 'deepImport' }],
        },
        {
          code: "import { x } from '@tellma/core/src/json/tm-decimals';",
          filename: app('src/a.ts'),
          errors: [{ messageId: 'unknownEntryPoint' }],
        },
        {
          code: "const load = () => import('@tellma/core/nonexistent');",
          filename: core('search/a.ts'),
          errors: [{ messageId: 'unknownEntryPoint' }],
        },
      ],
    });
  });
});

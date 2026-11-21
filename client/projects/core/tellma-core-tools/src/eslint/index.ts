/**
 * The ESLint preset of a Tellma distribution, and the `tm` plugin behind it.
 *
 * A distribution's `eslint.config.mjs` re-exports the preset:
 *
 * ```js
 * export { default } from '@tellma/core-tools/eslint';
 * ```
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { existsSync, readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';

import eslint from '@eslint/js';
import angular from 'angular-eslint';
import type { ESLint, Linter } from 'eslint';
import { defineConfig } from 'eslint/config';
import tseslint from 'typescript-eslint';

import { tmCoreImportBoundary } from './rules/core-import-boundary.js';
import { tmLazyLayouts } from './rules/lazy-layouts.js';
import { tmLiteralDeclarationKeys } from './rules/literal-declaration-keys.js';
import { tmNoDecimalOperators } from './rules/no-decimal-operators.js';
import { tmNoRawHttp } from './rules/no-raw-http.js';
import { tmNoWebStorage } from './rules/no-web-storage.js';

// The plugin. A config registers it under `tm`, so its rules read `tm/<rule>`.

/** The manifest of this package, for the plugin's name and version. */
const manifest = createRequire(import.meta.url)('../../package.json') as {
  readonly name: string;
  readonly version: string;
};

/**
 * The `tm` ESLint plugin: the platform's own rules, each named
 * `tm/<rule>` once the plugin is registered under `tm`.
 *
 * - `core-import-boundary`: imports of `@tellma/core` follow its entry-point graph.
 * - `no-raw-http`: no `fetch`, `XMLHttpRequest` or `HttpClient`; requests go through `TmApi`.
 * - `no-web-storage`: no direct use of the browser's storage.
 * - `lazy-layouts`: every layout member of a declaration is `() => import('…')`.
 * - `literal-declaration-keys`: every declaration key is a string literal.
 * - `no-decimal-operators`: no `+`, `+=`, `<`, `<=`, `>` or `>=` on a `TmDecimal` (type-aware).
 */
export const tmEslintPlugin: ESLint.Plugin = {
  meta: { name: manifest.name, version: manifest.version },
  rules: {
    'core-import-boundary': tmCoreImportBoundary,
    'no-raw-http': tmNoRawHttp,
    'no-web-storage': tmNoWebStorage,
    'lazy-layouts': tmLazyLayouts,
    'literal-declaration-keys': tmLiteralDeclarationKeys,
    'no-decimal-operators': tmNoDecimalOperators,
  } as unknown as ESLint.Plugin['rules'],
};

/**
 * typescript-eslint's `restrict-plus-operands` at its strictest: `+` only
 * between two numbers, two bigints or two strings, so a decimal's text is
 * never added to a number.
 */
export const TM_RESTRICT_PLUS_OPERANDS: Linter.RuleEntry = [
  'error',
  {
    allowAny: false,
    allowBoolean: false,
    allowNullish: false,
    allowNumberAndString: false,
    allowRegExp: false,
    skipCompoundAssignments: false,
  },
];

// The preset a distribution re-exports.

/** The options of {@link tmEslintConfig}. */
export interface TmEslintOptions {
  /**
   * The prefix of every component and directive selector: the distribution's
   * slug, such as `acme`. By default it is read from the `prefix` of the one
   * application in the nearest `angular.json` at or above the current
   * directory; when none is found, selectors are checked for their style only.
   */
  readonly prefix?: string;
  /**
   * The folder from which the type-aware rules find each file's
   * `tsconfig.json`. Defaults to the folder of that `angular.json`, else the
   * current directory.
   */
  readonly tsconfigRootDir?: string;
}

/** The nearest `angular.json` at or above `folder`, with the folder that holds it. */
function findAngularJson(folder: string): { readonly folder: string; readonly prefix: string | undefined } | null {
  for (let current = folder; ; current = dirname(current)) {
    const file = join(current, 'angular.json');
    if (existsSync(file)) {
      try {
        const workspace = JSON.parse(readFileSync(file, 'utf8')) as {
          readonly projects?: Readonly<Record<string, { readonly projectType?: string; readonly prefix?: string }>>;
        };
        const applications = Object.values(workspace.projects ?? {}).filter(
          (project) => project.projectType === 'application',
        );
        return { folder: current, prefix: applications.length === 1 ? applications[0]?.prefix : undefined };
      } catch {
        return { folder: current, prefix: undefined };
      }
    }
    if (dirname(current) === current) {
      return null;
    }
  }
}

/**
 * The ESLint configuration of a Tellma distribution: ESLint's, typescript-eslint's
 * and angular-eslint's recommended rules, the `tm` rules, strict
 * `restrict-plus-operands`, and the distribution's slug as selector prefix.
 * Type-aware rules read each file's nearest `tsconfig.json`.
 *
 * @param options - Overrides of the prefix and the type-aware root.
 */
export function tmEslintConfig(options: TmEslintOptions = {}): Linter.Config[] {
  const found = findAngularJson(process.cwd());
  const prefix = options.prefix ?? found?.prefix;
  const tsconfigRootDir = options.tsconfigRootDir ?? found?.folder ?? process.cwd();

  return defineConfig(
    // Generated and built files.
    {
      ignores: ['dist/', 'generated/', '.angular/', '**/*.d.ts'],
    },

    // TypeScript, with type information for the decimal rule.
    {
      files: ['**/*.ts'],
      extends: [eslint.configs.recommended, tseslint.configs.recommended, angular.configs.tsRecommended],
      processor: angular.processInlineTemplates,
      languageOptions: {
        parserOptions: { projectService: true, tsconfigRootDir },
      },
      plugins: { tm: tmEslintPlugin },
      rules: {
        'tm/core-import-boundary': 'error',
        'tm/no-raw-http': 'error',
        'tm/no-web-storage': 'error',
        'tm/lazy-layouts': 'error',
        'tm/literal-declaration-keys': 'error',
        'tm/no-decimal-operators': 'error',
        '@typescript-eslint/restrict-plus-operands': TM_RESTRICT_PLUS_OPERANDS,
        '@angular-eslint/component-selector': [
          'error',
          prefix === undefined
            ? { type: 'element', style: 'kebab-case' }
            : { type: 'element', prefix, style: 'kebab-case' },
        ],
        '@angular-eslint/directive-selector': [
          'error',
          prefix === undefined
            ? { type: 'attribute', style: 'camelCase' }
            : { type: 'attribute', prefix, style: 'camelCase' },
        ],
      },
    },

    // Templates, inline ones included.
    {
      files: ['**/*.html'],
      extends: [angular.configs.templateRecommended],
    },
  );
}

/** The preset itself, configured from the current directory's `angular.json`. */
const preset: Linter.Config[] = tmEslintConfig();
export default preset;

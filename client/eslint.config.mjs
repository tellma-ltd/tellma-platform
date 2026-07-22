// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// @ts-check
import { defineConfig } from 'eslint/config';
import eslint from '@eslint/js';
import tseslint from 'typescript-eslint';
import angular from 'angular-eslint';

import tmPrefixExports from './tools/eslint/tm-prefix-exports.mjs';
import noStateBindingsOnFormField from './tools/eslint/no-state-bindings-on-form-field.mjs';

/**
 * The reviewed allowlist for deliberately-unprefixed library exports — the
 * names the spec itself defines without a Tm/tm prefix (§2.1, §4, §7.1).
 * Additions are an explicit, reviewed act (spec deviation record #2).
 */
const UNPREFIXED_EXPORTS = [
  // contracts (§2.1)
  'SignalLike',
  'WritableSignalLike',
  // fonts (§7.1)
  'fontPreloadLinks',
  'PreloadLink',
];

const tmPlugin = {
  rules: {
    'prefix-exports': tmPrefixExports,
    'no-state-bindings-on-form-field': noStateBindingsOnFormField,
  },
};

export default defineConfig(
  {
    ignores: [
      'dist/',
      '.artifacts/',
      '.angular/',
      '**/generated/',
      'projects/core/tellma-core-ui-mcp/dist/',
      '**/*.d.ts',
    ],
  },

  // Library + app TypeScript
  {
    files: ['projects/**/*.ts'],
    extends: [
      eslint.configs.recommended,
      ...tseslint.configs.recommended,
      ...angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        { type: 'attribute', prefix: 'tm', style: 'camelCase' },
      ],
      '@angular-eslint/component-selector': [
        'error',
        { type: 'element', prefix: 'tm', style: 'kebab-case' },
      ],
    },
  },

  // The internal showcase is not a library — app/showcase prefixes apply.
  {
    files: ['projects/internal/**/*.ts'],
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        { type: 'attribute', prefix: 'app', style: 'camelCase' },
      ],
      '@angular-eslint/component-selector': [
        'error',
        { type: 'element', prefix: ['app', 'showcase'], style: 'kebab-case' },
      ],
    },
  },

  // tm/Tm/TM_ prefix on every library export (reviewed allowlist above).
  {
    files: ['projects/core/**/*.ts', 'projects/locale/**/*.ts'],
    ignores: [
      '**/*.spec.ts',
      '**/*.examples.ts',
      // Spec-only helpers (not exported from any entry point).
      '**/*testing.util.ts',
      'projects/core/tellma-core-ui-mcp/**',
    ],
    plugins: { tm: tmPlugin },
    rules: {
      'tm/prefix-exports': ['error', { allow: UNPREFIXED_EXPORTS }],
    },
  },

  // Contracts entry-point boundary (spec §10, DoD 10): types + pure helpers
  // only — no Angular, no other @tellma packages, no i18n/rxjs, no reaching
  // into the primary entry point's internals.
  {
    files: ['projects/core/tellma-core-ui/contracts/**/*.ts'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: ['@angular/*'],
              message: 'The contracts entry point must stay free of Angular imports.',
            },
            {
              group: ['@tellma/*'],
              message: 'The contracts entry point must not depend on other @tellma packages.',
            },
            {
              group: ['@jsverse/*', 'rxjs', 'rxjs/*'],
              message: 'The contracts entry point is dependency-free.',
            },
            {
              group: ['../*'],
              message: "The contracts entry point must not reach into the primary entry point's internals.",
            },
          ],
        },
      ],
    },
  },

  // Grid-engine entry-point boundary (spec 0004 §1, DoD 14): pure TypeScript
  // plus @angular/core SIGNALS only — no DOM, no dependency injection, no
  // components, no other @tellma packages except the contracts types. The
  // engine must stay constructible in a plain vitest test.
  {
    files: ['projects/core/tellma-core-ui/grid-engine/**/*.ts'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          paths: [
            {
              name: '@angular/core',
              importNames: [
                'inject',
                'Injectable',
                'InjectionToken',
                'Injector',
                'Component',
                'Directive',
                'Pipe',
                'NgModule',
                'ElementRef',
                'Renderer2',
                'DestroyRef',
                'ChangeDetectorRef',
                'NgZone',
                'ApplicationRef',
                'effect',
                'afterRenderEffect',
                'afterNextRender',
                'DOCUMENT',
              ],
              message:
                'The grid engine uses @angular/core for signals only — no DI, components, or render hooks.',
            },
            {
              name: '@tellma/core-ui',
              message: 'The grid engine may depend on @tellma/core-ui/contracts only.',
            },
          ],
          patterns: [
            {
              group: ['@angular/*', '!@angular/core'],
              message: 'The grid engine may import @angular/core (signals) only.',
            },
            {
              group: ['@angular/core/*'],
              message: 'The grid engine may not use @angular/core secondary entry points.',
            },
            {
              // Gitignore semantics: exclude the entry points, re-include
              // contracts (its parent directory itself stays includable).
              group: ['@tellma/core-ui/*', '!@tellma/core-ui/contracts'],
              message: 'The grid engine may depend on @tellma/core-ui/contracts only.',
            },
            {
              group: ['@tellma/core-ui-*', '@tellma/core-ui-*/**', '@tellma/locale-*', '@tellma/locale-*/**'],
              message: 'The grid engine may depend on @tellma/core-ui/contracts only.',
            },
            {
              group: ['@jsverse/*', 'rxjs', 'rxjs/*'],
              message: 'The grid engine is dependency-free beyond signals and contracts.',
            },
            {
              group: ['../*'],
              message: 'The grid engine must not reach into sibling entry points.',
            },
          ],
        },
      ],
      'no-restricted-globals': [
        'error',
        ...[
          'document',
          'window',
          'navigator',
          'DOMParser',
          'HTMLElement',
          'Element',
          'Node',
          'Event',
          'KeyboardEvent',
          'MouseEvent',
          'PointerEvent',
          'ClipboardEvent',
          'DataTransfer',
          'MutationObserver',
          'ResizeObserver',
          'IntersectionObserver',
          'getComputedStyle',
          'requestAnimationFrame',
          'cancelAnimationFrame',
          'localStorage',
          'sessionStorage',
          'customElements',
        ].map((name) => ({
          name,
          message: 'The grid engine is DOM-free; DOM work belongs to the grid component layer.',
        })),
      ],
      'no-restricted-syntax': [
        'error',
        {
          selector: "NewExpression[callee.name='InjectionToken']",
          message: 'The grid engine must stay DI-free.',
        },
      ],
    },
  },

  // Templates — inline templates are extracted by the processor above.
  {
    files: ['projects/**/*.html'],
    extends: [...angular.configs.templateRecommended],
    plugins: { tm: tmPlugin },
    rules: {
      'tm/no-state-bindings-on-form-field': 'error',
    },
  },

  // Workspace tooling (node scripts, lint rules, e2e specs).
  {
    files: ['scripts/**/*.mjs', 'tools/**/*.mjs'],
    extends: [eslint.configs.recommended],
    languageOptions: {
      globals: {
        process: 'readonly',
        console: 'readonly',
        fetch: 'readonly',
        setTimeout: 'readonly',
        URLSearchParams: 'readonly',
      },
    },
  },
  {
    files: ['e2e/**/*.ts', 'tools/**/*.mts'],
    extends: [eslint.configs.recommended, ...tseslint.configs.recommended],
  },
);

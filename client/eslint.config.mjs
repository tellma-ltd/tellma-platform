// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// @ts-check
import { defineConfig } from 'eslint/config';
import eslint from '@eslint/js';
import tseslint from 'typescript-eslint';
import angular from 'angular-eslint';
import { TM_RESTRICT_PLUS_OPERANDS, tmEslintPlugin } from '@tellma/core-tools/eslint';

import tmPrefixExports from './tools/eslint/tm-prefix-exports.mjs';
import noStateBindingsOnFormField from './tools/eslint/no-state-bindings-on-form-field.mjs';
import cspOnlyInCspSpecs from './tools/eslint/csp-only-in-csp-specs.mjs';

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
  // The calendar provider — the name the component-basics spec defines
  // (`provideTm…` carries the brand inside; only the prefix pattern differs).
  'provideTmCalendar',
  // The page-scope provider, named by the observability spec in the same shape.
  'provideTmPageScope',
  // The Playwright assertion the app-foundation spec names for every suite.
  'expectNoAxeViolations',
  // The app's one provider call, which the app-foundation spec names; the
  // prefix pattern expects a suffix after `provideTellma`.
  'provideTellma',
];

/**
 * One `tm` plugin for the whole workspace: the rules @tellma/core-tools ships
 * to distributions, plus the library-only rules under tools/eslint. ESLint
 * refuses two different objects under one plugin name, so they merge here.
 */
const tmPlugin = {
  meta: tmEslintPlugin.meta,
  rules: {
    ...tmEslintPlugin.rules,
    'prefix-exports': tmPrefixExports,
    'no-state-bindings-on-form-field': noStateBindingsOnFormField,
    'csp-only-in-csp-specs': cspOnlyInCspSpecs,
  },
};

/**
 * What a calendar pack may import, shared by the two config blocks below
 * (spec files get this list; shipped files get it plus the sibling-climb
 * ban). The bare '@tellma/core-ui' specifier needs its own `paths` entry —
 * a `group` pattern only ever matches subpaths, so without it a pack could
 * pull the whole primary entry point in and still lint clean.
 */
const CALENDAR_BOUNDARY = {
  paths: [
    {
      name: '@tellma/core-ui',
      message: 'Calendar entry points may depend on l10n and contracts only.',
    },
  ],
  patterns: [
    {
      group: ['@angular/*'],
      message: 'Calendar entry points must stay free of Angular imports.',
    },
    {
      group: ['@tellma/core-ui/*', '!@tellma/core-ui/l10n', '!@tellma/core-ui/contracts'],
      message: 'Calendar entry points may depend on l10n and contracts only.',
    },
    {
      group: ['@tellma/core-ui-*', '@tellma/core-ui-*/**', '@tellma/locale-*', '@tellma/locale-*/**'],
      message: 'Calendar entry points may depend on l10n and contracts only.',
    },
    {
      group: ['@jsverse/*', 'rxjs', 'rxjs/*'],
      message: 'Calendar entry points are dependency-free beyond the calendar arithmetic package.',
    },
  ],
};

export default defineConfig(
  {
    ignores: [
      'dist/',
      '.artifacts/',
      '.angular/',
      '**/generated/',
      'projects/core/tellma-core-ui-mcp/dist/',
      'projects/core/tellma-core-tools/dist/',
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

  // tm/Tm/TM_ prefix on every library export (reviewed allowlist above),
  // in core packages, feature packs and locale packs alike.
  {
    files: ['projects/core/**/*.ts', 'projects/module/**/*.ts', 'projects/locale/**/*.ts'],
    ignores: [
      '**/*.spec.ts',
      '**/*.examples.ts',
      // Spec-only helpers (not exported from any entry point).
      '**/*testing.util.ts',
      // Node packages: tooling, not library surface.
      'projects/core/tellma-core-ui-mcp/**',
      'projects/core/tellma-core-tools/**',
    ],
    plugins: { tm: tmPlugin },
    rules: {
      'tm/prefix-exports': ['error', { allow: UNPREFIXED_EXPORTS }],
    },
  },

  // The app framework's packages (spec 0024 §1, §10.4, §12): static imports
  // between @tellma/core entry points follow its graph, declarations keep
  // their keys literal and their layouts lazy, and no + or comparison ever
  // runs on a TmDecimal's text. The decimal rules need type information, so
  // these files lint against their TypeScript projects.
  {
    files: ['projects/core/tellma-core/**/*.ts', 'projects/core/tellma-core-testing/**/*.ts'],
    languageOptions: {
      parserOptions: {
        project: [
          './projects/core/tellma-core/tsconfig.json',
          './projects/core/tellma-core-testing/tsconfig.json',
        ],
        tsconfigRootDir: import.meta.dirname,
      },
    },
    plugins: { tm: tmPlugin },
    rules: {
      'tm/core-import-boundary': ['error', { allowPrivate: true }],
      'tm/lazy-layouts': 'error',
      'tm/literal-declaration-keys': 'error',
      'tm/no-decimal-operators': 'error',
      '@typescript-eslint/restrict-plus-operands': TM_RESTRICT_PLUS_OPERANDS,
    },
  },

  // Specs build declarations from stand-in components and computed keys to
  // exercise the merge and the config check; the lazy-layout and literal-key
  // rules guard app sources, not these fixtures.
  {
    files: ['projects/core/tellma-core/**/*.spec.ts', 'projects/core/tellma-core-testing/**/*.spec.ts'],
    rules: {
      'tm/lazy-layouts': 'off',
      'tm/literal-declaration-keys': 'off',
    },
  },

  // Feature packs hold declarations and layouts, like a distribution's own
  // code, so they take the distribution rules (spec 0024 §2.1) — all but the
  // type-aware decimal rule, which joins when the first pack has a project.
  {
    files: ['projects/module/**/*.ts'],
    plugins: { tm: tmPlugin },
    rules: {
      'tm/core-import-boundary': ['error', { allowPrivate: true }],
      'tm/no-raw-http': 'error',
      'tm/no-web-storage': 'error',
      'tm/lazy-layouts': 'error',
      'tm/literal-declaration-keys': 'error',
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

  // Foundation entry-point boundary (spec 0023 §1): icon and kbd depend on
  // no other @tellma entry point, since every other entry point builds on
  // them, and the icon entry point carries no icon data of its own — the
  // library registers nothing, so Lucide data belongs to its importers.
  {
    files: ['projects/core/tellma-core-ui/icon/**/*.ts', 'projects/core/tellma-core-ui/kbd/**/*.ts'],
    ignores: ['**/*.spec.ts', '**/*.examples.ts'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              // Covers the bare '@tellma/core-ui' as well as its subpaths.
              group: ['@tellma/*'],
              message: 'The icon and kbd entry points depend on no other @tellma entry point.',
            },
            {
              group: ['lucide', 'lucide/*', '@lucide/*'],
              message: 'The icon and kbd entry points ship no icon data; importers pass their own.',
            },
            {
              group: ['@jsverse/*', 'rxjs', 'rxjs/*'],
              message: 'The icon and kbd entry points are dependency-free beyond @angular/core.',
            },
            {
              group: ['../*'],
              message: 'The icon and kbd entry points must not reach into sibling entry points.',
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

  // Calendar entry-point boundary: the same pure posture as l10n (no
  // Angular, no DOM, no DI), plus l10n itself for the shared adapter.
  {
    files: ['projects/core/tellma-core-ui/calendar-*/**/*.ts'],
    rules: {
      'no-restricted-imports': ['error', CALENDAR_BOUNDARY],
      'no-restricted-syntax': [
        'error',
        {
          selector: "NewExpression[callee.name='InjectionToken']",
          message:
            'Calendar entry points must stay DI-free (registration goes through provideTmCalendar).',
        },
      ],
    },
  },

  // …and SHIPPED calendar code additionally never climbs out of its own
  // entry point (a relative sibling import would duplicate that sibling into
  // the pack's bundle). Specs are exempt: both packs are checked against the
  // one date-format golden, which lives next to the code that produced it.
  {
    files: ['projects/core/tellma-core-ui/calendar-*/**/*.ts'],
    ignores: ['**/*.spec.ts'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          ...CALENDAR_BOUNDARY,
          patterns: [
            ...CALENDAR_BOUNDARY.patterns,
            {
              group: ['../*'],
              message: 'Calendar entry points must not reach into sibling entry points.',
            },
          ],
        },
      ],
    },
  },

  // l10n entry-point boundary: pure TypeScript + Intl (+ the calendar
  // arithmetic dependency) — no Angular at all, no DOM, no DI, no other
  // @tellma packages except the contracts types. Formatting/parsing must
  // stay deterministic per (input, locale, options) and constructible in a
  // plain vitest test.
  {
    files: ['projects/core/tellma-core-ui/l10n/**/*.ts'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          paths: [
            {
              name: '@tellma/core-ui',
              message: 'The l10n entry point may depend on @tellma/core-ui/contracts only.',
            },
          ],
          patterns: [
            {
              group: ['@angular/*'],
              message: 'The l10n entry point must stay free of Angular imports.',
            },
            {
              // Gitignore semantics: exclude the entry points, re-include
              // contracts (its parent directory itself stays includable).
              group: ['@tellma/core-ui/*', '!@tellma/core-ui/contracts'],
              message: 'The l10n entry point may depend on @tellma/core-ui/contracts only.',
            },
            {
              group: ['@tellma/core-ui-*', '@tellma/core-ui-*/**', '@tellma/locale-*', '@tellma/locale-*/**'],
              message: 'The l10n entry point may depend on @tellma/core-ui/contracts only.',
            },
            {
              group: ['@jsverse/*', 'rxjs', 'rxjs/*'],
              message:
                'The l10n entry point is dependency-free beyond Intl, contracts, and the calendar arithmetic package.',
            },
            {
              group: ['../*'],
              message: 'The l10n entry point must not reach into sibling entry points.',
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
          message: 'The l10n entry point is DOM-free; DOM work belongs to the component layer.',
        })),
      ],
      'no-restricted-syntax': [
        'error',
        {
          selector: "NewExpression[callee.name='InjectionToken']",
          message: 'The l10n entry point must stay DI-free (the calendar token lives in the primary entry point).',
        },
      ],
    },
  },

  // Unit specs share one browser page per run, and a CSP <meta> can never be
  // lifted: a spec that installs one must be a *.csp.spec.ts, which runs in
  // the test-csp target, a run of its own.
  {
    files: ['projects/**/*.spec.ts'],
    plugins: { tm: tmPlugin },
    rules: {
      'tm/csp-only-in-csp-specs': 'error',
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
    files: ['scripts/**/*.mjs', 'tools/**/*.mjs', 'projects/core/tellma-core-tools/bin/*.js'],
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

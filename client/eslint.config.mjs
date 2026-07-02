// @ts-check
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
  // tokens contract (§4)
  'Ref',
  'ColorRamp',
  'SchemeColors',
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

export default tseslint.config(
  {
    ignores: [
      'dist/',
      '.artifacts/',
      '.angular/',
      '.storybook/',
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

  // The internal sandbox is not a library — app/sandbox prefixes apply.
  {
    files: ['projects/internal/**/*.ts'],
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        { type: 'attribute', prefix: 'app', style: 'camelCase' },
      ],
      '@angular-eslint/component-selector': [
        'error',
        { type: 'element', prefix: ['app', 'sandbox'], style: 'kebab-case' },
      ],
    },
  },

  // tm/Tm/TM_ prefix on every library export (reviewed allowlist above).
  {
    files: ['projects/core/**/*.ts', 'projects/locale/**/*.ts'],
    ignores: ['**/*.spec.ts', '**/*.stories.ts', 'projects/core/tellma-core-ui-mcp/**'],
    plugins: { tm: tmPlugin },
    rules: {
      'tm/prefix-exports': ['error', { allow: UNPREFIXED_EXPORTS }],
    },
  },

  // Contracts entry-point boundary (spec §10, DoD 10): types + pure helpers
  // only — no Angular, no other @tellma packages, no i18n/rxjs, no reaching
  // into src/lib.
  {
    files: ['projects/core/tellma-core-ui/contracts/**/*.ts'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: ['@angular/*'],
              message: 'The contracts entry point must stay free of Angular imports (spec §2.1).',
            },
            {
              group: ['@tellma/*'],
              message: 'The contracts entry point must not depend on other @tellma packages.',
            },
            {
              group: ['@jsverse/*', 'rxjs', 'rxjs/*'],
              message: 'The contracts entry point is dependency-free (spec §7).',
            },
            {
              group: ['../*'],
              message: 'The contracts entry point must not reach into src/lib.',
            },
          ],
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

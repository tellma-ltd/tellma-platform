// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { execFileSync } from 'node:child_process';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

import { ESLint } from 'eslint';
import { describe, expect, it } from 'vitest';

import { tmEslintConfig, tmEslintPlugin } from './index.js';

// The preset lints a miniature distribution client: its angular.json names
// the application's prefix, and its tsconfig.json gives the type-aware rules
// their program. The texts are linted under the name of the fixture's one
// source file.
const fixture = join(import.meta.dirname, 'fixtures', 'distribution');
const feature = join(fixture, 'src', 'app', 'acme.feature.ts');

/** The rule ids the preset reports for `code`. */
async function ruleIds(code: string): Promise<string[]> {
  const eslint = new ESLint({
    cwd: fixture,
    overrideConfigFile: true,
    overrideConfig: tmEslintConfig({ prefix: 'acme', tsconfigRootDir: fixture }),
  });
  const [result] = await eslint.lintText(code, { filePath: feature });
  return (result?.messages ?? []).map((message) => message.ruleId ?? `fatal: ${message.message}`);
}

describe('the ESLint preset', { timeout: 120_000 }, () => {
  it('registers every tm rule in its plugin', () => {
    expect(Object.keys(tmEslintPlugin.rules ?? {}).sort()).toEqual([
      'core-import-boundary',
      'lazy-layouts',
      'literal-declaration-keys',
      'no-decimal-operators',
      'no-raw-http',
      'no-web-storage',
    ]);
  });

  it('passes a distribution that keeps to the rules', async () => {
    const code =
      "declare function tmEntity(name: string, declaration: object): unknown;\n" +
      "export const customers = tmEntity('acme.Customer', { details: () => import('./customer-details') });\n";
    expect(await ruleIds(code)).toEqual([]);
  });

  it('applies the distribution rules', async () => {
    const code =
      "import { HttpClient } from '@angular/common/http';\n" +
      "import { Search } from '@tellma/core/private';\n" +
      "declare function tmEntity(name: string, declaration: object): unknown;\n" +
      "const name = 'acme.Customer';\n" +
      'export const customers = tmEntity(name, { details: Search });\n' +
      "export const load = () => fetch('/x');\n" +
      "export const keep = () => localStorage.getItem('x');\n" +
      'export { HttpClient };\n';
    expect((await ruleIds(code)).sort()).toEqual([
      'tm/core-import-boundary',
      'tm/lazy-layouts',
      'tm/literal-declaration-keys',
      'tm/no-raw-http',
      'tm/no-raw-http',
      'tm/no-web-storage',
    ]);
  });

  it('refuses text arithmetic on a decimal, and a string added to a number', async () => {
    const code =
      "declare const tmDecimalBrand: unique symbol;\n" +
      'type TmDecimal = string & { readonly [tmDecimalBrand]: true };\n' +
      'declare const a: TmDecimal;\n' +
      'export const more = a > a;\n' +
      "export const label = 'Rows: ' + 3;\n";
    expect((await ruleIds(code)).sort()).toEqual(['@typescript-eslint/restrict-plus-operands', 'tm/no-decimal-operators']);
  });

  it('checks selectors for the prefix', async () => {
    const code =
      "import { Component } from '@angular/core';\n" +
      "@Component({ selector: 'app-root', template: '' })\n" +
      'export class Root {}\n';
    expect(await ruleIds(code)).toContain('@angular-eslint/component-selector');
  });

  it("reads the prefix from the angular.json of the directory it runs in", () => {
    const preset = pathToFileURL(join(import.meta.dirname, '..', '..', 'dist', 'eslint', 'index.js')).href;
    const script =
      `const { default: config } = await import(${JSON.stringify(preset)});\n` +
      "const rules = config.find((c) => c.rules?.['@angular-eslint/component-selector']).rules;\n" +
      "process.stdout.write(rules['@angular-eslint/component-selector'][1].prefix);\n";
    const prefix = execFileSync(process.execPath, ['--input-type=module', '-e', script], {
      cwd: fixture,
      encoding: 'utf8',
    });
    expect(prefix).toBe('acme');
  });
});

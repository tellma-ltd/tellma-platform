// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ESLint, RuleTester } from 'eslint';
import tseslint from 'typescript-eslint';
import { describe, expect, it } from 'vitest';

import rule from '../eslint/no-platform-sniffing.mjs';

const tester = new RuleTester({
  languageOptions: {
    parser: tseslint.parser as never,
    ecmaVersion: 2022,
    sourceType: 'module',
  },
});

describe('tm/no-platform-sniffing', () => {
  it('refuses navigator.platform and navigator.userAgentData in every spelling', () => {
    tester.run('tm/no-platform-sniffing', rule as never, {
      valid: [
        // Other navigator members, and the names on other objects.
        'const online = navigator.onLine;',
        'void navigator.clipboard.writeText(text);',
        'const languages = navigator.languages;',
        'const os = process.platform;',
        'const p = options.platform;',
        'const ua = request.userAgentData;',
        'const { language } = navigator;',
        'const { platform } = options;',
        // A computed key the rule cannot read statically.
        'const value = navigator[key];',
        // The supported way.
        "import { tmIsApplePlatform } from '@tellma/core-ui/kbd';\nconst apple = tmIsApplePlatform();",
      ],
      invalid: [
        {
          code: 'const isMac = /Mac/.test(navigator.platform);',
          errors: [{ messageId: 'sniffing', data: { name: 'platform' } }],
        },
        {
          code: 'const data = navigator.userAgentData;',
          errors: [{ messageId: 'sniffing', data: { name: 'userAgentData' } }],
        },
        {
          code: 'const name = navigator?.userAgentData?.platform;',
          errors: [{ messageId: 'sniffing', data: { name: 'userAgentData' } }],
        },
        {
          code: "const p = navigator['platform'];",
          errors: [{ messageId: 'sniffing', data: { name: 'platform' } }],
        },
        {
          code: 'const p = navigator[`platform`];',
          errors: [{ messageId: 'sniffing', data: { name: 'platform' } }],
        },
        {
          code: 'const p = window.navigator.platform;',
          errors: [{ messageId: 'sniffing', data: { name: 'platform' } }],
        },
        {
          code: 'const p = globalThis.navigator.userAgentData;',
          errors: [{ messageId: 'sniffing', data: { name: 'userAgentData' } }],
        },
        {
          code: "const p = self['navigator'].platform;",
          errors: [{ messageId: 'sniffing', data: { name: 'platform' } }],
        },
        {
          code: 'const ua = (navigator as Navigator & { userAgentData?: unknown }).userAgentData;',
          errors: [{ messageId: 'sniffing', data: { name: 'userAgentData' } }],
        },
        {
          code: 'const p = navigator!.platform;',
          errors: [{ messageId: 'sniffing', data: { name: 'platform' } }],
        },
        {
          code: 'const { platform, userAgentData: data } = navigator;',
          errors: [
            { messageId: 'sniffing', data: { name: 'platform' } },
            { messageId: 'sniffing', data: { name: 'userAgentData' } },
          ],
        },
        {
          code: "const { ['platform']: p } = window.navigator;",
          errors: [{ messageId: 'sniffing', data: { name: 'platform' } }],
        },
      ],
    });
  });
});

// The first lint loads the whole workspace config, which takes several seconds on a busy machine.
describe('tm/no-platform-sniffing scope', { timeout: 60_000 }, () => {
  const eslint = new ESLint({ cwd: process.cwd() });
  const CODE = 'export const tmApple = /Mac/.test(navigator.platform);\n';

  /** The rule's messages the workspace config reports for `CODE` at `filePath`. */
  async function sniffingMessages(filePath: string): Promise<number> {
    const [result] = await eslint.lintText(CODE, { filePath });
    return result.messages.filter((message) => message.ruleId === 'tm/no-platform-sniffing').length;
  }

  it('applies to the library and module packages, specs included', async () => {
    expect(await sniffingMessages('projects/core/tellma-core-ui/grid/internal/x.ts')).toBe(1);
    expect(await sniffingMessages('projects/core/tellma-core-ui/menu/x.spec.ts')).toBe(1);
    expect(await sniffingMessages('projects/core/tellma-core/src/app/tm-app-config.ts')).toBe(1);
    expect(await sniffingMessages('projects/module/tellma-module-x/src/x.ts')).toBe(1);
  });

  it('exempts kbd, which holds the one platform check', async () => {
    expect(await sniffingMessages('projects/core/tellma-core-ui/kbd/x.ts')).toBe(0);
    expect(await sniffingMessages('projects/core/tellma-core-ui/kbd/x.spec.ts')).toBe(0);
  });
});

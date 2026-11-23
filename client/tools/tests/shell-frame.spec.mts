// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

// The shell frame's row heights live twice: as root CSS variables in the
// global frame stylesheet, and as TM_SHELL_LAYOUT for code that measures.
// The stylesheet is not a module a unit test can load, so this test reads
// both sources and holds them equal.

const CORE = 'projects/core/tellma-core';
const css = readFileSync(join(process.cwd(), CORE, 'styles/tm-frame.css'), 'utf8');
const layoutSource = readFileSync(join(process.cwd(), CORE, 'src/nav/tm-shell.ts'), 'utf8');

/** The declarations of the first rule whose selector is exactly `selector`. */
function declarations(selector: string): Map<string, string> {
  const escaped = selector.replace(/[[\]]/g, (bracket) => `\\${bracket}`);
  const block = new RegExp(`(?:^|\\n)\\s*${escaped}\\s*\\{([^}]*)\\}`).exec(css);
  expect(block, selector).not.toBeNull();
  const body = (block as RegExpExecArray)[1].replace(/\/\*[\s\S]*?\*\//g, '');
  return new Map(
    body
      .split(';')
      .map((declaration) => declaration.trim())
      .filter((declaration) => declaration !== '')
      .map((declaration) => {
        const colon = declaration.indexOf(':');
        return [declaration.slice(0, colon).trim(), declaration.slice(colon + 1).trim()] as const;
      }),
  );
}

/** TM_SHELL_LAYOUT's heights, read from its source. */
function layout(): { appBar: number; banner: number; pinnedBar: number } {
  const match = /TM_SHELL_LAYOUT = Object\.freeze\(\{ appBar: (\d+), banner: (\d+), pinnedBar: (\d+) \}/.exec(layoutSource);
  expect(match).not.toBeNull();
  const [, appBar, banner, pinnedBar] = (match as RegExpExecArray).map(Number);
  return { appBar, banner, pinnedBar };
}

describe('the shell frame stylesheet', () => {
  it("declares the rows' default heights that TM_SHELL_LAYOUT mirrors, with no banner and no aside", () => {
    const root = declarations(':root');
    const { appBar, banner, pinnedBar } = layout();
    expect([appBar, banner, pinnedBar]).toEqual([40, 32, 32]);
    expect(root.get('--tm-app-bar-height')).toBe(`${appBar}px`);
    expect(root.get('--tm-banner-height')).toBe('0px');
    expect(root.get('--tm-pinned-bar-height')).toBe(`${pinnedBar}px`);
    expect(root.get('--tm-aside-width')).toBe('0px');
  });

  it('derives the chrome height and the toast offset from the rows', () => {
    const root = declarations(':root');
    expect(root.get('--tm-shell-top')).toBe(
      'calc(var(--tm-app-bar-height) + var(--tm-banner-height) + var(--tm-pinned-bar-height))',
    );
    expect(root.get('--toast-offset-block-start')).toBe('calc(var(--tm-shell-top) + 8px)');
  });

  it('takes every row away in immersive mode, over what the shell writes inline', () => {
    const immersive = declarations(':root[data-tm-immersive]');
    expect(Object.fromEntries(immersive)).toEqual({
      '--tm-app-bar-height': '0px !important',
      '--tm-banner-height': '0px !important',
      '--tm-pinned-bar-height': '0px !important',
    });
  });

  it('lays the frame out on the row and column variables', () => {
    const frame = declarations('.tm-shell');
    expect(frame.get('display')).toBe('grid');
    expect(frame.get('block-size')).toBe('100dvh');
    expect(frame.get('grid-template-rows')?.replace(/\s+/g, ' ')).toBe(
      'var(--tm-app-bar-height) var(--tm-banner-height) var(--tm-pinned-bar-height) minmax(0, 1fr)',
    );
    expect(frame.get('grid-template-columns')).toBe('minmax(0, 1fr) var(--tm-aside-width)');
  });

  it("is part of the platform's global styles, after the design tokens", () => {
    const styles = readFileSync(join(process.cwd(), CORE, 'styles.css'), 'utf8');
    const frame = styles.indexOf("@import './styles/tm-frame.css';");
    const tokens = styles.indexOf("@import '@tellma/core-ui-tokens/css/tellma-default.css';");
    expect(frame).toBeGreaterThan(-1);
    expect(tokens).toBeGreaterThan(-1);
    // Both declare --toast-offset-block-start on :root in the same layer, and
    // the later declaration wins: before the tokens, the toasts would fall
    // back to the tokens' 8px and sit over the app bar.
    expect(frame).toBeGreaterThan(tokens);
  });
});

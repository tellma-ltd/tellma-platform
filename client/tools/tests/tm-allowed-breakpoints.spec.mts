// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import stylelint from 'stylelint';
import { describe, expect, it } from 'vitest';

const config = {
  plugins: ['./tools/stylelint/tm-allowed-breakpoints.mjs'],
  rules: {
    'tm/allowed-breakpoints': true,
  },
};

async function warningsFor(code: string): Promise<string[]> {
  const result = await stylelint.lint({ code, config, cwd: process.cwd() });
  return result.results[0].warnings.map((w) => w.text);
}

describe('tm/allowed-breakpoints', () => {
  it('reads its thresholds from the breakpoints.json the library exports', async () => {
    const manifest = JSON.parse(
      readFileSync(join('projects', 'core', 'tellma-core-ui', 'package.json'), 'utf8'),
    );
    expect(manifest.exports['./breakpoints.json'].default).toBe('./layout/breakpoints.json');
    const table = JSON.parse(
      readFileSync(
        join('projects', 'core', 'tellma-core-ui', 'layout', 'breakpoints.json'),
        'utf8',
      ),
    );
    const [warning] = await warningsFor('@media (min-width: 1px) {}');
    expect(warning).toContain(
      Object.values(table)
        .map((value) => `${value}px`)
        .join(', '),
    );
  });

  it('accepts the canonical range forms at breakpoint values', async () => {
    const accepted = [
      '@media (width >= 768px) {}',
      '@media (width < 576px) {}',
      '@media (576px <= width < 992px) {}',
      '@media (width >= 1440px) {}',
      '@media screen and (width >= 1200px) {}',
      '@media (width >= 576px) and (width < 768px) {}',
      '@media not (width < 576px) {}',
      '@container (inline-size >= 576px) {}',
      '@container (width >= 576px) {}',
      '@container sidebar (768px <= inline-size < 1200px) {}',
    ];
    for (const code of accepted) {
      expect(await warningsFor(code), code).toEqual([]);
    }
  });

  it('leaves non-width features unrestricted', async () => {
    const unrestricted = [
      '@media (forced-colors: active) {}',
      '@media (prefers-reduced-motion: reduce) {}',
      '@media (prefers-color-scheme: dark) {}',
      '@media (hover: hover) and (pointer: coarse) {}',
      '@media (orientation: landscape) {}',
      '@media (height >= 600px) {}',
      '@media (min-height: 480px) {}',
      '@media (hover) {}',
      '@container style(--compact: 1) {}',
      '@container (block-size > 400px) {}',
    ];
    for (const code of unrestricted) {
      expect(await warningsFor(code), code).toEqual([]);
    }
  });

  it('refuses the legacy width features, naming the canonical form', async () => {
    expect(await warningsFor('@media (max-width: 600px) {}')).toEqual([
      expect.stringContaining('write "(width < 576px)"'),
    ]);
    expect(await warningsFor('@media (min-width: 768px) {}')).toEqual([
      expect.stringContaining('write "(width >= 768px)"'),
    ]);
    expect(await warningsFor('@container (min-inline-size: 576px) {}')).toEqual([
      expect.stringContaining('write "(inline-size >= 576px)"'),
    ]);
    expect(await warningsFor('@container (max-inline-size: 1000px) {}')).toEqual([
      expect.stringContaining('write "(inline-size < 992px)"'),
    ]);
    expect(await warningsFor('@media (device-width: 768px) {}')).toHaveLength(1);
    expect(await warningsFor('@media (min-device-width: 768px) {}')).toHaveLength(1);
  });

  it('refuses the other operator arrangements', async () => {
    expect(await warningsFor('@media (width <= 767px) {}')).toEqual([
      expect.stringContaining('write "(width < 768px)"'),
    ]);
    expect(await warningsFor('@media (width > 575px) {}')).toEqual([
      expect.stringContaining('write "(width >= 576px)"'),
    ]);
    expect(await warningsFor('@media (768px <= width) {}')).toEqual([
      expect.stringContaining('write "(width >= 768px)"'),
    ]);
    expect(await warningsFor('@media (768px > width) {}')).toEqual([
      expect.stringContaining('write "(width < 768px)"'),
    ]);
    expect(await warningsFor('@media (width = 768px) {}')).toHaveLength(1);
    expect(await warningsFor('@media (576px < width <= 992px) {}')).toEqual([
      expect.stringContaining('write "(576px <= width < 992px)"'),
    ]);
    expect(await warningsFor('@media (992px <= width < 576px) {}')).toHaveLength(1);
  });

  it('refuses other units and other values, naming the nearest threshold', async () => {
    expect(await warningsFor('@media (width >= 48rem) {}')).toEqual([
      expect.stringContaining('write "(width >= 768px)"'),
    ]);
    expect(await warningsFor('@media (width < 600px) {}')).toEqual([
      expect.stringContaining('write "(width < 576px)"'),
    ]);
    expect(await warningsFor('@media (width >= 700px) {}')).toEqual([
      expect.stringContaining('write "(width >= 768px)"'),
    ]);
    expect(await warningsFor('@media (width >= 767.98px) {}')).toHaveLength(1);
    expect(await warningsFor('@media (width >= 50vw) {}')).toHaveLength(1);
    expect(await warningsFor('@media (width >= calc(700px + 68px)) {}')).toHaveLength(1);
    expect(await warningsFor('@media (500px <= width < 992px) {}')).toEqual([
      expect.stringContaining('write "(576px <= width < 992px)"'),
    ]);
  });

  it('reports each bad condition of a compound query', async () => {
    expect(
      await warningsFor('@media (min-width: 576px) and (hover: hover), (max-width: 400px) {}'),
    ).toHaveLength(2);
  });
});

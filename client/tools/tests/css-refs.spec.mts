// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

import { tmCheckCssRefs, tmDeclaredVars, tmVarRefs } from '../tokens/css-refs.mjs';

const EMITTED = `
@layer tm.base {
  :root {
    --field-height: 30px;
    --grid-plain-cell-padding-x: 11px;
  }
}
`;

/** Writes one stylesheet to a scratch dir and returns its path. */
function sheet(css: string): string {
  const dir = mkdtempSync(join(tmpdir(), 'tm-css-refs-'));
  const file = join(dir, 'component.css');
  writeFileSync(file, css);
  return file;
}

describe('tmDeclaredVars', () => {
  it('collects the custom properties a sheet declares', () => {
    expect(tmDeclaredVars(EMITTED)).toEqual(
      new Set(['--field-height', '--grid-plain-cell-padding-x']),
    );
  });
});

describe('tmVarRefs', () => {
  it('records whether each read supplied a fallback', () => {
    const refs = tmVarRefs('a { x: var(--one); y: var(--two, 4px); }', 'f.css');
    expect(refs).toEqual([
      { name: '--one', file: 'f.css', hasFallback: false },
      { name: '--two', file: 'f.css', hasFallback: true },
    ]);
  });
});

describe('tmCheckCssRefs', () => {
  it('passes a sheet that only reads emitted names', () => {
    const file = sheet('.tm-x { block-size: var(--field-height); }');
    expect(tmCheckCssRefs(EMITTED, [file])).toEqual([]);
  });

  it('catches the kebab trap this gate exists for', () => {
    // `cellPaddingXPlain` emits `--grid-cell-padding-xplain`: two adjacent
    // capitals do not split. A rule reading `-x-plain` silently applies
    // nothing at all, which is exactly what nothing else notices.
    const file = sheet('.tm-x { padding-inline: var(--grid-cell-padding-x-plain); }');
    expect(tmCheckCssRefs(EMITTED, [file])).toEqual([
      'component.css reads --grid-cell-padding-x-plain, which no token emits',
    ]);
  });

  it('allows a read that carries a fallback — a miss there is survivable', () => {
    const file = sheet('.tm-x { block-size: var(--not-a-token, 10px); }');
    expect(tmCheckCssRefs(EMITTED, [file])).toEqual([]);
  });

  it('allows a --tm- private local and a sheet reading its own declaration', () => {
    const file = sheet(
      '.tm-x { --tm-local: 4px; --mine: 2px; padding: var(--tm-local) var(--mine); }',
    );
    expect(tmCheckCssRefs(EMITTED, [file])).toEqual([]);
  });

  it('allows a --tm- local declared by a DIFFERENT scanned sheet (the size-ladder pattern)', () => {
    // tm-form-field's sheet declares the ladder locals; the global input
    // sheet and the pickers' sheets read them without re-declaring.
    const declaring = sheet(':host { --tm-field-block-size: 30px; }');
    const reading = sheet('.tm-x { block-size: var(--tm-field-block-size); }');
    expect(tmCheckCssRefs(EMITTED, [declaring, reading])).toEqual([]);
  });

  it('flags a --tm- read that no scanned sheet declares', () => {
    // The private prefix must not be a blanket pass: a typo'd --tm- read is
    // the same silent-drop failure the gate exists for.
    const file = sheet('.tm-x { block-size: var(--tm-field-blok-size); }');
    expect(tmCheckCssRefs(EMITTED, [file])).toEqual([
      'component.css reads --tm-field-blok-size, which no scanned stylesheet declares',
    ]);
  });

  it('allows the custom properties components write as inline styles', () => {
    const file = sheet('.tm-grid__row { grid-template-columns: var(--grid-template); }');
    expect(tmCheckCssRefs(EMITTED, [file])).toEqual([]);
  });
});

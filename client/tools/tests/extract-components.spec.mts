// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { describe, expect, it } from 'vitest';

import { createDocsProject, extractSource, SOURCES } from '../docs/extract-components.mjs';

// A ts-morph project loads the TypeScript compiler and the source's imports
// on first use, which takes many seconds on a busy machine; the default 5 s
// timeout would make that a flake.
describe('extractSource (component tokens)', { timeout: 120_000 }, () => {
  it("documents the tiles' tree-mode tokens on the tree directive, whose internal chrome uses them", () => {
    const source = SOURCES.find(
      (candidate) => candidate.entryPoint === '@tellma/core-ui/tiles-tree',
    );
    expect(source).toBeDefined();

    const tree = extractSource(createDocsProject(), source!).find(
      (component) => component.selector === 'tm-tiles[parentId]',
    );

    expect(tree).toBeDefined();
    expect(tree!.tokens).toEqual(
      expect.arrayContaining([
        '--tiles-spinner-size',
        '--tiles-spinner-slot',
        '--tiles-twisty',
        '--tiles-twisty-size',
        '--tiles-twisty-target',
      ]),
    );
  });
});

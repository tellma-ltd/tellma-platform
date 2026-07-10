// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { join } from 'node:path';
import { Project } from 'ts-morph';
import { describe, expect, it } from 'vitest';

import { extractExamples } from '../docs/extract-components.mjs';

describe('extractExamples (AST scan)', () => {
  it('survives template-less exports, sibling properties, and skips non-exports', () => {
    const project = new Project({ useInMemoryFileSystem: false });
    // extractExamples derives <name>.examples.ts from the source path.
    const fakeSource = join(import.meta.dirname, 'fixtures', 'tricky.ts');
    const examples = extractExamples(project, fakeSource);

    expect(examples.map((e) => e.title)).toEqual(['WithSiblings', 'Plain']);
    expect(examples[0].code).toBe('<tm-x label="A"></tm-x>');
    expect(examples[1].code).toBe('<tm-x label="B"></tm-x>');
  });
});

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

// eslint-disable-next-line @typescript-eslint/ban-ts-comment
// @ts-ignore untyped workspace helper (plain .mjs)
import { discoverLibraries } from '../workspace.mjs';

/**
 * docs-examples.spec.ts compiles every co-located *.examples.ts against the
 * live API, but its SUITES list is static (the browser-mode runner cannot
 * glob). This node-side guard keeps that list complete: a new examples file
 * on disk that the spec doesn't cover fails here.
 */
describe('docs-examples.spec.ts covers every *.examples.ts on disk', () => {
  it('lists every examples file', () => {
    const coreUi = discoverLibraries(join(process.cwd())).find(
      (library: { name: string }) => library.name === '@tellma/core-ui',
    )!;
    const onDisk = coreUi.entryPoints.flatMap((entryPoint: { id: string; dir: string }) =>
      readdirSync(entryPoint.dir)
        .filter((name) => name.endsWith('.examples.ts'))
        .map((name) => `${entryPoint.id.replace('./', '')}/${name}`),
    );
    const spec = readFileSync(join(coreUi.dir, 'docs-examples.spec.ts'), 'utf8');
    for (const file of onDisk) {
      expect(spec, `${file} is not covered by docs-examples.spec.ts`).toContain(`'${file}'`);
    }
    expect(onDisk.length).toBeGreaterThan(0);
  });
});

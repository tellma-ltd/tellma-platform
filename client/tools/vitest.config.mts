// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { configDefaults, defineConfig } from 'vitest/config';

/** The spec that runs a real, cold `ng build` of the fixture distribution client. */
const FIXTURE_BUILD_SPEC = 'projects/core/tellma-core-tools/src/commands/build.fixture.spec.ts';

/**
 * Runs the workspace-tooling tests (lint rules, docs extractor, scripts) and
 * the specs of the Node package @tellma/core-tools, which sit beside its
 * sources.
 */
export default defineConfig({
  test: {
    environment: 'node',
    // The type-aware lint rules import the TypeScript compiler directly; it
    // loads as the plain CommonJS module it is, not through Vite's transform,
    // which would look for a source map the package does not ship.
    server: { deps: { external: ['typescript'] } },
    // Two groups, run one after the other. The fixture's real ng build uses
    // every core the machine has for minutes, so beside the other files it
    // slowed their real processes past their limits, and they slowed it past
    // its own. Alone, it gets the machine to itself once the rest is done.
    projects: [
      {
        extends: true,
        test: {
          name: 'tools',
          include: ['tools/tests/**/*.spec.mts', 'projects/core/tellma-core-tools/src/**/*.spec.ts'],
          exclude: [...configDefaults.exclude, FIXTURE_BUILD_SPEC],
        },
      },
      {
        extends: true,
        test: {
          name: 'fixture-build',
          include: [FIXTURE_BUILD_SPEC],
          sequence: { groupOrder: 1 },
        },
      },
    ],
  },
});

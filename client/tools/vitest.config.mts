// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { defineConfig } from 'vitest/config';

/**
 * Runs the workspace-tooling tests (lint rules, docs extractor, scripts) and
 * the specs of the Node package @tellma/core-tools, which sit beside its
 * sources.
 */
export default defineConfig({
  test: {
    include: ['tools/tests/**/*.spec.mts', 'projects/core/tellma-core-tools/src/**/*.spec.ts'],
    environment: 'node',
    // The type-aware lint rules import the TypeScript compiler directly; it
    // loads as the plain CommonJS module it is, not through Vite's transform,
    // which would look for a source map the package does not ship.
    server: { deps: { external: ['typescript'] } },
  },
});

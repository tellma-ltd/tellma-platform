// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { defineConfig } from 'vitest/config';

/** Runs the workspace-tooling tests (lint rules, docs extractor, scripts). */
export default defineConfig({
  test: {
    include: ['tools/tests/**/*.spec.mts'],
    environment: 'node',
  },
});

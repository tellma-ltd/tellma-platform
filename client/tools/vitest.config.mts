import { defineConfig } from 'vitest/config';

/** Runs the workspace-tooling tests (lint rules, docs extractor, scripts). */
export default defineConfig({
  test: {
    include: ['tools/tests/**/*.spec.mts'],
    environment: 'node',
  },
});

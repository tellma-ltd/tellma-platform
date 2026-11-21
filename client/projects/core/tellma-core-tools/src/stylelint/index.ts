/**
 * The stylelint preset of a Tellma distribution, and the platform's
 * stylelint plugin.
 *
 * A distribution's `stylelint.config.mjs` re-exports the preset:
 *
 * ```js
 * export { default } from '@tellma/core-tools/stylelint';
 * ```
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Config } from 'stylelint';

import { tmAllowedBreakpoints } from './allowed-breakpoints.js';

export { tmAllowedBreakpoints };

/**
 * The stylelint preset: `tm/allowed-breakpoints`, so every width query is
 * range syntax at one of the platform's breakpoints.
 */
const preset: Config = {
  plugins: [tmAllowedBreakpoints],
  rules: {
    'tm/allowed-breakpoints': true,
  },
};

export default preset;

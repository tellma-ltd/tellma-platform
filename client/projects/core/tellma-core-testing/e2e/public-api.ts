/**
 * Public API Surface of @tellma/core-testing/e2e — Playwright helpers for the
 * end-to-end suites of apps built on @tellma/core. This entry point runs in
 * Node, inside Playwright tests, and needs the optional peer
 * `@playwright/test`.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

export { expectNoAxeViolations, tmAxeViolations, type TmAxeViolation } from './tm-axe';

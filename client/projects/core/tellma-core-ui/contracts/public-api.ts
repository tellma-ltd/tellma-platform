/**
 * Public API Surface of @tellma/core-ui/contracts.
 *
 * Zero-/low-runtime cross-cutting contract types, and the grid's key
 * combinations as plain data (`TM_GRID_KEYS`). This entry point stays free
 * of Angular and component imports (enforced by lint) so the data grid can
 * depend on it without pulling in the components, and a keyboard help dialog
 * can list the grid's keys without loading the grid.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export * from './tm-contracts';
export * from './tm-grid-contracts';
export * from './tm-grid-keys';

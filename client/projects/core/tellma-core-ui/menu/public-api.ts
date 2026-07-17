/**
 * Public API Surface of @tellma/core-ui/menu — the tm-menu component (a
 * general-purpose menu on the aria menu pattern + CDK overlay) and the
 * tmContextMenuTrigger directive that opens it at the pointer, element, or
 * touch long-press.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export * from './tm-menu';
export * from './tm-context-menu-trigger';
export { tmObserveLongPress as ɵtmObserveLongPress } from './internal/long-press';

/**
 * Public API Surface of @tellma/core-ui/private — shared composition
 * machinery the library's own entry points build on (the anchored overlay
 * helper and the Escape-dismissal coordinator).
 *
 * This entry point is importable but carries NO stability guarantees: its
 * surface is excluded from the API goldens and may change in any release
 * without notice. Application code should not depend on it.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export {
  tmCreateAnchoredOverlay,
  tmLogicalPositions,
  type TmAnchoredOverlay,
  type TmAnchoredOverlayConfig,
  type TmAnchoredOverlayOrigin,
  type TmOverlayAlign,
  type TmOverlaySide,
} from './tm-anchored-overlay';
export { tmPushEscapeDismissal } from './tm-dismiss-stack';

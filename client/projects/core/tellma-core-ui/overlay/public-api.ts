/**
 * Public API Surface of @tellma/core-ui/overlay — the anchoring and
 * dismissal helpers every Tellma overlay is built on, so a surface built
 * outside the library (a shell panel, a notification tray) anchors the way
 * the library's own menus, popovers and tooltips do and shares their one
 * Escape stack.
 *
 * Two rules come with the contract:
 *
 * - **One Escape stack.** A surface that dismisses on Escape pushes onto
 *   `tmPushEscapeDismissal` when it opens and releases the handle when it
 *   closes. It never adds a document listener of its own: two capture
 *   listeners on one node both run for the same key press, so one Escape
 *   would close two layers.
 * - **Inline top layer.** By default (`popoverHost: 'inline'`) the overlay
 *   pane renders next to the origin element, inside the origin's subtree
 *   order. A container that delegates key or pointer events must therefore
 *   ignore events coming from inside `.cdk-overlay-pane`. An anchor that
 *   sits inside `role="row"` passes `popoverHost: { type: 'parent', element }`
 *   so the pane never becomes an invalid child of the row.
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

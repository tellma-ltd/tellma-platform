/**
 * Public API Surface of @tellma/core-ui/initials — the pure helpers of a
 * person's face: `tmInitials`, the initials a name gives, and
 * `tmAvatarTone`, the categorical tone a stable key picks. `tm-avatar`
 * draws with them; they are an entry point of their own so a surface that
 * must paint a face in an app's first frame (the app bar's session button)
 * can draw the same initials on the same tone without reaching `tm-avatar`,
 * whose photo brings `tm-image`. `@tellma/core-ui/avatar` re-exports them.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export { tmAvatarTone, tmInitials } from './tm-initials';

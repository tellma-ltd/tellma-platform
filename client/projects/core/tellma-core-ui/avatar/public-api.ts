/**
 * Public API Surface of @tellma/core-ui/avatar — `tm-avatar`, a person as a
 * round photo or as initials on a tinted circle, with the pure helpers it
 * builds on: `tmInitials` and `tmAvatarTone`.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export { TmAvatar, type TmAvatarSize } from './tm-avatar';
// The face's pure helpers live in their own entry point, which the app
// shell's first frame reaches without this component's image.
export { tmAvatarTone, tmInitials } from '@tellma/core-ui/initials';

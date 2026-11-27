/**
 * Public API Surface of @tellma/core-ui/keys — the one grammar for writing
 * and matching key combinations: the `TmKeys` notation, the
 * layout-independent `tmKeysMatch`, the platform-aware labels and
 * `tmIsApplePlatform`, the library's one platform check. It is an entry
 * point of its own so a surface on an app's first frame (the shortcut
 * registry, the app bar's hints) matches and labels keys without carrying
 * `tm-kbd`'s key caps, which only lazily loaded menus and dialogs draw.
 * `@tellma/core-ui/kbd` re-exports it.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export {
  tmAriaKeyShortcuts,
  tmFormatKeys,
  tmIsApplePlatform,
  tmKeysMatch,
  tmKeysText,
  type TmKeys,
} from './tm-keys';

/**
 * Public API Surface of @tellma/core-ui/kbd — the one grammar for writing,
 * matching and showing key combinations: the `TmKeys` notation, the
 * layout-independent `tmKeysMatch`, the platform-aware labels and
 * `tm-kbd`'s key caps.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export { TmKbd } from './tm-kbd';
export {
  tmAriaKeyShortcuts,
  tmFormatKeys,
  tmIsApplePlatform,
  tmKeysMatch,
  tmKeysText,
  type TmKeys,
} from './tm-keys';

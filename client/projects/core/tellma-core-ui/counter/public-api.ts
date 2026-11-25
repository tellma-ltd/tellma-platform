/**
 * Public API Surface of @tellma/core-ui/counter — `tm-counter`, a number
 * shown inline or on its parent's corner. It is an entry point of its own
 * so a surface that paints counts in an app's first frame (the app bar's
 * slot buttons) does not carry `tm-badge` with it.
 * `@tellma/core-ui/badge` re-exports it.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export { TmCounter, type TmCounterPlacement, type TmCounterTone } from './tm-counter';

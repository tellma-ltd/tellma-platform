/**
 * Public API Surface of @tellma/core-ui/toast — `TmToasts`, short timed
 * messages in one top-layer stack, and `provideTellmaToasts`, its app-wide
 * configuration.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export type { TmToast, TmToastTone } from './internal/tm-toast-model';
export {
  provideTellmaToasts,
  TmToasts,
  type TmToastRef,
  type TmToastsOptions,
} from './tm-toasts';

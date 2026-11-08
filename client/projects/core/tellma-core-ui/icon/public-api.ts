/**
 * Public API Surface of @tellma/core-ui/icon — `tm-icon`, which draws a
 * Lucide icon by registry name or from icon data, and the runtime registry
 * the names resolve through.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export { TmIcon } from './tm-icon';
export {
  TmIconRegistry,
  provideTellmaIcons,
  type TmIconNode,
  type TmIconSource,
} from './tm-icon-registry';

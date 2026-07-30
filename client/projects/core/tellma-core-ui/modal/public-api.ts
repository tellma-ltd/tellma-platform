/**
 * Public API Surface of @tellma/core-ui/modal — the `TmModal` service:
 * service-opened dialogs with a standard shell, a typed result channel
 * discriminating every dismissal path, and a `canDismiss` guard.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export { TM_MODAL_DATA, TmModal, type TmModalConfig, type TmModalSize } from './tm-modal';
export { TmModalRef, type TmModalResult } from './tm-modal-ref';
export { TmModalFooter } from './tm-modal-footer';

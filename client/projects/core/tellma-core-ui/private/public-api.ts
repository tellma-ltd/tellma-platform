/**
 * Public API Surface of @tellma/core-ui/private — composition machinery the
 * library's own entry points share but applications never use: the
 * validation bubble the form field and the grid both render, and the arrow
 * keys the radio group and the segmented control share.
 *
 * This entry point is importable but carries NO stability guarantees: its
 * surface is excluded from the API goldens and may change in any release
 * without notice. Application code should not depend on it. The anchoring
 * and Escape-dismissal helpers are public in `@tellma/core-ui/overlay`.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export { ɵTmErrorPopover } from './tm-error-popover';
export { ɵtmRadioArrowTarget } from './tm-radio-keys';

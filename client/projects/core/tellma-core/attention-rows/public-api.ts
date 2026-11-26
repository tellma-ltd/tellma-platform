/**
 * Public API Surface of @tellma/core/attention-rows — what the attention
 * surfaces render inside their rows: the server text of notifications, job
 * labels and assignment captions in the reader's language
 * (`TmAttentionText`), the assignment list the tray and the home widgets
 * share (`tm-assignment-list`), and the reader of a notification's or a
 * job's ICU arguments. It loads with whichever code imports it — the bell's
 * and the tray's panels, the arrival toasts and the attention pages — never
 * with the app's first frame.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

export {
  TmAttentionText,
  type TmAssignmentCaption,
  type TmJobLabelSource,
  type TmNotificationText,
  type TmTextSegment,
} from './src/tm-attention-text';
export {
  TmAssignmentList,
  type TmAssignmentActivation,
  type TmAssignmentListVariant,
  type TmAssignmentParty,
} from './src/tm-assignment-list';
export { tmIcuArguments } from './src/tm-icu-arguments';
export { TmRovingRows as ɵTmRovingRows } from './src/tm-roving-rows';

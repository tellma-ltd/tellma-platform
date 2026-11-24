// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The toast policy's durations (spec 0028 §8.1): an outcome the acting tab
// raises shows for 4 s, an arrival from the attention documents for 6 s.
// Failures and error notifications are sticky instead.

/**
 * How long an outcome's toast shows, in milliseconds: "Started in the
 * background.", "Caches refreshed.".
 *
 * @example
 * ```ts
 * inject(TmToasts).show({ tone: 'success', title: 'Caches refreshed.', duration: TM_TOAST_FEEDBACK_MS });
 * ```
 */
export const TM_TOAST_FEEDBACK_MS = 4000;

/** How long an arriving notification's or assignment's toast shows, in milliseconds. */
export const TM_TOAST_ARRIVAL_MS = 6000;

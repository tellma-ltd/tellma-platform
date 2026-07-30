/**
 * Public API Surface of @tellma/core-ui/calendar-umalqura — the Umm
 * al-Qura display calendar as an opt-in entry point: a calendar is not a
 * language (an English UI can display Umm al-Qura; Arabic UIs routinely
 * show Gregorian), and per-entry-point packaging keeps the conversion
 * tables out of apps that don't need them, while month and era names come
 * from Intl at no bundle cost.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export { tmUmalquraCalendar } from './tm-calendar-umalqura';

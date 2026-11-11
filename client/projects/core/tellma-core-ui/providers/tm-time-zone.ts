// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, InjectionToken, signal, type Signal } from '@angular/core';

/**
 * The DEVICE's time zone as a signal: the IANA id the browser resolves
 * (`Europe/London`). The default reads it once; an app that wants to
 * follow a travelling device provides a signal it re-reads (no browser
 * event reports a zone change).
 */
export const TM_DEVICE_TIME_ZONE = new InjectionToken<Signal<string>>('TM_DEVICE_TIME_ZONE', {
  providedIn: 'root',
  factory: () => signal(new Intl.DateTimeFormat().resolvedOptions().timeZone).asReadonly(),
});

/**
 * The DISPLAY time zone as a signal: the IANA id instants are shown in.
 * Defaults to the device's zone ({@link TM_DEVICE_TIME_ZONE}), so the two
 * never differ until an app provides the zone its user or organization
 * chose.
 */
export const TM_TIME_ZONE = new InjectionToken<Signal<string>>('TM_TIME_ZONE', {
  providedIn: 'root',
  factory: () => inject(TM_DEVICE_TIME_ZONE),
});

/**
 * The clock: a function returning the current time in epoch milliseconds.
 * Defaults to `Date.now`; a test replaces it with a fake clock, so
 * relative times and zone labels are deterministic.
 */
export const TM_NOW = new InjectionToken<() => number>('TM_NOW', {
  providedIn: 'root',
  // Looked up on every call, so a fake `Date` installed later is honoured.
  factory: () => () => Date.now(),
});

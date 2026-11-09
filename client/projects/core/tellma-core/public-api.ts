/**
 * Public API Surface of @tellma/core — the Tellma app framework, the runtime
 * a distribution's single-page app imports. This primary entry point loads
 * with the app; each lazy boundary of the framework is a secondary entry
 * point of its own.
 *
 * It holds the plain-JSON value types of the HTTP layer and the exact decimal
 * helpers, and the seams through which telemetry, the request log and page
 * scopes are provided.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// Plain JSON and exact decimals.
export type { TmDecimal, TmId, TmJson, TmJsonObject } from './src/json/tm-json';
export { TmDecimals } from './src/json/tm-decimals';

// Telemetry.
export {
  TmTelemetry,
  type TmTelemetryItem,
  type TmTelemetryOptions,
  type TmTelemetryProps,
} from './src/observe/tm-telemetry';

// The request log.
export {
  TmRequestLog,
  type TmRequestEnd,
  type TmRequestHandle,
  type TmRequestOutcome,
  type TmRequestRecord,
  type TmRequestStart,
} from './src/requests/tm-request-log';

// Page scopes.
export { provideTmPageScope } from './src/context/tm-page-scope';

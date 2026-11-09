/**
 * Public API Surface of @tellma/core-testing — test providers and fakes for
 * apps built on @tellma/core. Its `/e2e` entry point holds the Playwright
 * helpers, and runs in Node.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

export { provideTellmaTesting } from './src/provide-tellma-testing';
export { TmFakeTelemetry } from './src/tm-fake-telemetry';

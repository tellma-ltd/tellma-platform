// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { makeEnvironmentProviders, type EnvironmentProviders } from '@angular/core';
import { TmTelemetry } from '@tellma/core';

import { TmFakeTelemetry } from './tm-fake-telemetry';

/**
 * The test providers of an app built on `@tellma/core`: each platform seam is
 * replaced by a fake a test can inspect. Telemetry goes to
 * {@link TmFakeTelemetry}, which records items and sends nothing.
 *
 * @example
 * ```ts
 * TestBed.configureTestingModule({ providers: [provideTellmaTesting()] });
 * ```
 */
export function provideTellmaTesting(): EnvironmentProviders {
  return makeEnvironmentProviders([TmFakeTelemetry, { provide: TmTelemetry, useExisting: TmFakeTelemetry }]);
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, makeEnvironmentProviders, type EnvironmentProviders } from '@angular/core';
import { TM_CLIENT_METADATA, TmApi, TmTelemetry } from '@tellma/core';
import { TM_BLOB_FETCHER, type TmBlobFetcher } from '@tellma/core-ui/image';

import { TmFakeApi } from './tm-fake-api';
import { TmFakeTelemetry } from './tm-fake-telemetry';
import { tmTestMetadata } from './tm-test-metadata';

/**
 * The test providers of an app built on `@tellma/core`: each platform seam is
 * replaced by a fake a test can inspect. Calls go to {@link TmFakeApi},
 * which answers from a script and records them, image fetches included
 * (`tm-image`'s `TM_BLOB_FETCHER` goes to `TmFakeApi.fetchBlob`), telemetry goes to
 * {@link TmFakeTelemetry}, which records items and sends nothing, and the
 * client metadata is {@link tmTestMetadata}'s, loaded from the start.
 *
 * @example
 * ```ts
 * TestBed.configureTestingModule({ providers: [provideTellmaTesting()] });
 * ```
 */
export function provideTellmaTesting(): EnvironmentProviders {
  return makeEnvironmentProviders([
    TmFakeApi,
    { provide: TmApi, useExisting: TmFakeApi },
    // As provideTellma does, so a component test's images are scripted too.
    {
      provide: TM_BLOB_FETCHER,
      useFactory: (): TmBlobFetcher => {
        const api = inject(TmApi);
        return (url, options) => api.fetchBlob(url, options);
      },
    },
    TmFakeTelemetry,
    { provide: TmTelemetry, useExisting: TmFakeTelemetry },
    { provide: TM_CLIENT_METADATA, useValue: tmTestMetadata() },
  ]);
}

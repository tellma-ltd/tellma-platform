// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type ApplicationConfig, type EnvironmentProviders, inject, makeEnvironmentProviders } from '@angular/core';
import { TM_BLOB_FETCHER } from '@tellma/core-ui/image';

import { TmHttpApi } from '../api/tm-http-api';
import { ɵtmMergeAppDeclarations } from '../declarations/tm-merge';
import { TM_CLIENT_METADATA } from '../metadata/tm-client-metadata';
import { TM_METADATA_LOADERS } from '../metadata/tm-metadata-loaders';
import type { TmAppConfig } from './tm-app-config';
import { TM_APP_DECLARATIONS } from './tm-app-declarations';

// `provideTellma` and `tmAppConfigOf` (spec 0024 §2.2, §3.6). The providers
// remember the config they were made from, so the build's config check can
// take it back out of the app's `ApplicationConfig` without running the app.
// The merged declarations are built on first injection, once the Core chunk
// is loaded.

/** The config each `provideTellma` result was made from. */
const configs = new WeakMap<object, TmAppConfig>();

/**
 * The providers of a Tellma app: the merged declarations of its features,
 * built once from the loaded client metadata, the metadata loaders the HTTP
 * layer loads the Contracts chunk through, and `tm-image`'s blob fetcher over
 * the HTTP layer.
 *
 * @param config - The distribution's app configuration.
 * @returns The providers, for the app's `ApplicationConfig`.
 *
 * @example
 * ```ts
 * export const appConfig: ApplicationConfig = {
 *   providers: [
 *     provideTellma({
 *       metadata: tellmaMetadata,
 *       languages: [tmEnglish(), tmArabic()],
 *       features: [tmGlFeature(), acmeFeature],
 *     }),
 *   ],
 * };
 * ```
 */
export function provideTellma(config: TmAppConfig): EnvironmentProviders {
  const providers = makeEnvironmentProviders([
    {
      provide: TM_APP_DECLARATIONS,
      useFactory: () => ɵtmMergeAppDeclarations(config.features, inject(TM_CLIENT_METADATA)).declarations,
    },
    { provide: TM_METADATA_LOADERS, useValue: config.metadata },
    // Images ride the HTTP layer: the user check, the session and the client's headers apply.
    { provide: TM_BLOB_FETCHER, useFactory: () => inject(TmHttpApi).blobFetcher },
  ]);
  configs.set(providers, config);
  return providers;
}

/**
 * Finds the config `provideTellma` received among an `ApplicationConfig`'s
 * providers, nested arrays included. Nothing is instantiated.
 *
 * @param appConfig - The app's configuration, such as `src/app.config.ts` exports.
 * @returns The config, or `null` when no provider came from `provideTellma`.
 *
 * @example
 * ```ts
 * const config = tmAppConfigOf(appConfig);
 * ```
 */
export function tmAppConfigOf(appConfig: ApplicationConfig): TmAppConfig | null {
  // Walk the providers depth first, since provider lists nest freely.
  const pending: unknown[] = [...appConfig.providers];
  while (pending.length > 0) {
    const provider = pending.shift();
    if (Array.isArray(provider)) {
      pending.unshift(...(provider as unknown[]));
    } else if (typeof provider === 'object' && provider !== null) {
      const config = configs.get(provider);
      if (config !== undefined) {
        return config;
      }
    }
  }
  return null;
}

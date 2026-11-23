// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type ApplicationConfig, type EnvironmentProviders, inject, isDevMode, makeEnvironmentProviders } from '@angular/core';
import {
  provideRouter,
  RouteReuseStrategy,
  TitleStrategy,
  UrlSerializer,
  withComponentInputBinding,
  withNavigationErrorHandler,
  withRouterConfig,
} from '@angular/router';
import {
  provideTellmaUi,
  TM_ACTIVE_LOCALE,
  TM_CALENDAR,
  TM_DEVICE_TIME_ZONE,
  TM_TIME_ZONE,
  TM_UI_ARGUMENT_FORMATTER,
} from '@tellma/core-ui';
import { provideTellmaIcons } from '@tellma/core-ui/icon';
import { TM_BLOB_FETCHER, type TmBlobFetcher } from '@tellma/core-ui/image';

import { TmApi } from '../api/tm-api';
import { TM_API_CACHE, TM_API_LOCALE, type TmApiLocale } from '../api/tm-api-seams';
import { TM_CORE_FEATURE } from '../declarations/tm-core-feature';
import { ɵtmMergeAppDeclarations } from '../declarations/tm-merge';
import { TM_PLATFORM_ICONS } from '../icons/platform-icons';
import { tmArgumentFormatter } from '../l10n/tm-argument-formatter';
import { TmLocalization } from '../l10n/tm-localization';
import { tmTranslationsLoader } from '../l10n/tm-translations';
import { TM_CLIENT_METADATA } from '../metadata/tm-client-metadata';
import { TM_METADATA_LOADERS } from '../metadata/tm-metadata-loaders';
import { TmRouteReuse } from '../routes/tm-route-reuse';
import { tmBuildRoutes } from '../routes/tm-routes';
import { TmTitleStrategy } from '../routes/tm-title-strategy';
import { TmUrlSerializer } from '../routes/tm-url-serializer';
import { TmClientStore } from '../state/store/tm-client-store';
import { TmCacheRefresher } from '../state/sync/tm-cache-refresher';
import { TmNavigationFailure } from './tm-app';
import { type TmAppConfig, ɵTM_APP_CONFIG } from './tm-app-config';
import { tmStartup } from './tm-app-init';
import { TM_APP_DECLARATIONS } from './tm-app-declarations';
import { tmThemeSync } from './tm-theme-sync';

// `provideTellma` and `tmAppConfigOf` (spec 0024 §2.2, §3.6, §9.2). The
// providers remember the config they were made from, so the build's config
// check can take it back out of the app's `ApplicationConfig` without
// running the app. The route table is derived here, from the declarations
// alone: the pages merge without the client metadata. The merged
// declarations are built on first injection, once the Core chunk is loaded.

/** The config each `provideTellma` result was made from. */
const configs = new WeakMap<object, TmAppConfig>();

/**
 * The providers of a Tellma app: the router over the route table its
 * declarations derive, with the title strategy and a reuse strategy that
 * creates every route anew on a change of tenant; the startup initializers;
 * the component library over the app's strings, seeded with the boot
 * record's theme; the active locale, calendar and time zones, which
 * `TmLocalization` resolves; the merged declarations of its features, built
 * once from the loaded client metadata; the metadata loaders; `tm-image`'s
 * blob fetcher over `TmApi.fetchBlob`; the platform's and the features'
 * icons; and each feature's providers, in feature order.
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
  // The pages read no server fact, so their merge needs no metadata.
  const { pages } = ɵtmMergeAppDeclarations(config.features, { entities: [], codedReports: [] }).declarations;
  const providers = makeEnvironmentProviders([
    // A cancelled Back restores the address; parameters reach every page as inputs.
    provideRouter(
      tmBuildRoutes(pages),
      withComponentInputBinding(),
      withRouterConfig({ paramsInheritanceStrategy: 'always', canceledNavigationResolution: 'computed' }),
      withNavigationErrorHandler((failure) => inject(TmNavigationFailure).ɵfailed(failure)),
    ),
    { provide: UrlSerializer, useClass: TmUrlSerializer },
    // A route into another tenant is created anew, never reused.
    { provide: RouteReuseStrategy, useClass: TmRouteReuse },
    tmStartup(config),
    { provide: ɵTM_APP_CONFIG, useValue: config },
    // The component library reads the app's strings, languages and theme.
    provideTellmaUi({
      translations: tmTranslationsLoader(config.metadata, config.languages, isDevMode),
      availableLangs: config.languages.map((language) => language.code),
      theme: { initial: () => inject(TmClientStore).boot.read().theme },
    }),
    tmThemeSync(),
    // The library's locale, calendar and zones, and the requests' language, are the localization service's.
    { provide: TM_ACTIVE_LOCALE, useFactory: () => inject(TmLocalization).locale },
    { provide: TM_CALENDAR, useFactory: () => inject(TmLocalization).calendar },
    { provide: TM_TIME_ZONE, useFactory: () => inject(TmLocalization).timeZone },
    { provide: TM_DEVICE_TIME_ZONE, useFactory: () => inject(TmLocalization).deviceTimeZone },
    {
      provide: TM_API_LOCALE,
      useFactory: (): TmApiLocale => {
        const l10n = inject(TmLocalization);
        return { acceptLanguage: () => l10n.ɵacceptLanguage(), calendarCode: () => l10n.calendarCode() };
      },
    },
    {
      provide: TM_UI_ARGUMENT_FORMATTER,
      useFactory: () => {
        const l10n = inject(TmLocalization);
        return tmArgumentFormatter({ calendar: () => l10n.calendar(), timeZone: () => l10n.timeZone() });
      },
    },
    {
      provide: TM_APP_DECLARATIONS,
      useFactory: () => ɵtmMergeAppDeclarations(config.features, inject(TM_CLIENT_METADATA)).declarations,
    },
    { provide: TM_METADATA_LOADERS, useValue: config.metadata },
    // Images ride the HTTP layer: the user check, the session and the client's
    // headers apply, and a test's fake TmApi answers them too.
    {
      provide: TM_BLOB_FETCHER,
      useFactory: (): TmBlobFetcher => {
        const api = inject(TmApi);
        return (url, options) => api.fetchBlob(url, options);
      },
    },
    // The HTTP layer reports every tenant response's version tags, and each
    // read-only refusal, to the cache refresher.
    { provide: TM_API_CACHE, useExisting: TmCacheRefresher },
    // The platform's icon names, then each feature's, so a feature may redraw one.
    provideTellmaIcons(TM_PLATFORM_ICONS),
    ...config.features.flatMap((feature) => (feature.icons ? [provideTellmaIcons(feature.icons)] : [])),
    // The features' providers, then the title strategy, which no feature replaces.
    ...[TM_CORE_FEATURE, ...config.features].flatMap((feature) => feature.providers ?? []),
    { provide: TitleStrategy, useExisting: TmTitleStrategy },
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

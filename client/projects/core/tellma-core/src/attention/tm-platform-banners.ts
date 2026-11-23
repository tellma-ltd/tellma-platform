// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, effect, type EnvironmentProviders, inject, provideEnvironmentInitializer, untracked } from '@angular/core';

import { TmApi } from '../api/tm-api';
import { TmLocalization } from '../l10n/tm-localization';
import { TmBanners, type TmBannerRef } from './tm-banners';

// The platform's banner sources (spec 0028 §9.5), which `provideTellma`
// starts, so they run in every shell, the tenantless one included. The
// offline banner follows `TmApi.online` (spec 0024 §10.5): it shows once the
// server has been unreachable for 5 s, so a single failed call that the next
// probe answers never flashes it, and closes the moment the server answers
// again. The update banner's source is the update service's, which shows it
// through the same service with its dismissal scoped to the version.

/** How long the server must stay unreachable before the offline banner shows, in milliseconds. */
export const TM_OFFLINE_BANNER_MS = 5000;

/**
 * Starts the platform's banner sources with the app: the offline banner.
 *
 * @returns The providers.
 */
export function tmPlatformBanners(): EnvironmentProviders {
  return provideEnvironmentInitializer(() => {
    const api = inject(TmApi);
    const banners = inject(TmBanners);
    const l10n = inject(TmLocalization);
    let timer: ReturnType<typeof setTimeout> | undefined;
    let ref: TmBannerRef | null = null;
    inject(DestroyRef).onDestroy(() => clearTimeout(timer));

    // --- The offline banner: 5 s unreachable shows it, the next answer closes it ---
    effect(() => {
      const online = api.online();
      untracked(() => {
        clearTimeout(timer);
        if (online) {
          ref?.close();
          ref = null;
        } else if (ref === null) {
          timer = setTimeout(() => {
            ref = banners.show({
              id: 'tm.offline',
              level: 1,
              tone: 'error',
              icon: 'wifi-off',
              message: l10n.ɵtext('tm.attention.banners.offline'),
              dismissible: false,
            });
          }, TM_OFFLINE_BANNER_MS);
        }
      });
    });
  });
}

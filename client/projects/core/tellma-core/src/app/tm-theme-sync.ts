// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, DOCUMENT, effect, type EnvironmentProviders, inject, provideEnvironmentInitializer, untracked } from '@angular/core';
import { TmTheme } from '@tellma/core-ui';

import { TmClientStore } from '../state/store/tm-client-store';

// The theme's glue (spec 0024 §7.6). `TmTheme` starts from the boot record's
// theme, which the boot script already painted, so nothing flashes and
// nothing is written back at startup. A change of the setting is written to
// the boot record, and another tab's write arrives through its changes. The
// browser's `theme-color` follows `--tm-theme-color` under the scheme on
// screen: the ink app bar's colour in both Tellma themes, which a
// distribution's `@layer tm.theme` may change.

/** The custom property `theme-color` follows. */
const THEME_COLOR = '--tm-theme-color';

/**
 * The theme's glue: the boot record keeps the setting, other tabs' changes
 * apply here, and `<meta name="theme-color">` follows the scheme.
 *
 * @returns The providers.
 */
export function tmThemeSync(): EnvironmentProviders {
  return provideEnvironmentInitializer(() => {
    const theme = inject(TmTheme);
    const boot = inject(TmClientStore).boot;
    const document = inject(DOCUMENT);

    // --- The setting is written to the boot record when it changes ---
    effect(() => {
      const setting = theme.setting();
      untracked(() => {
        if (boot.read().theme !== setting) {
          boot.patch({ theme: setting });
        }
      });
    });

    // --- Another tab's change applies here ---
    const changes = boot.changes.subscribe((record) => {
      if (record.theme !== untracked(theme.setting)) {
        theme.set(record.theme);
      }
    });
    inject(DestroyRef).onDestroy(() => changes.unsubscribe());

    // --- `theme-color` follows the custom property under the scheme on screen ---
    effect(() => {
      theme.effective();
      untracked(() => {
        const color = document.defaultView?.getComputedStyle(document.documentElement).getPropertyValue(THEME_COLOR).trim();
        if (!color) {
          return;
        }
        let meta = document.head.querySelector<HTMLMetaElement>('meta[name="theme-color"]');
        if (meta === null) {
          meta = document.createElement('meta');
          meta.name = 'theme-color';
          document.head.append(meta);
        }
        if (meta.content !== color) {
          meta.content = color;
        }
      });
    });
  });
}

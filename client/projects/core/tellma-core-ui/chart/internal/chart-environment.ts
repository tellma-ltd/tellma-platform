// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, DOCUMENT, Injectable, inject, signal, type Signal } from '@angular/core';

// What makes every chart on the page redraw (spec 0034 §7.4, §7.7): a theme
// change, which re-points the tokens, and the user's contrast settings,
// which turn the decals on. One instance per page watches them for all
// charts; the chart reads its tokens itself, so this only says when.

/** The page settings the charts redraw on. */
@Injectable({ providedIn: 'root' })
export class ɵTmChartEnvironment {
  /** Changes whenever the page's theme, or forced colours, may have re-pointed the tokens. */
  readonly theme: Signal<number>;

  /** Whether decals apply to every chart: forced colours, or more contrast asked for. */
  readonly forceDecals: Signal<boolean>;

  /** Starts watching the root element's theme attributes and the media queries. */
  constructor() {
    const document = inject(DOCUMENT);
    const view = document.defaultView;
    const theme = signal(0);
    const bump = (): void => theme.update((tick) => tick + 1);

    // Section: the theme: the root's attributes (the scheme, a brand class
    // or inline token overrides) and the system scheme a `system` theme follows.
    const observer =
      typeof view?.MutationObserver === 'function' ? new view.MutationObserver(bump) : null;
    observer?.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ['data-theme', 'class', 'style'],
    });
    const media = (query: string): MediaQueryList | null =>
      typeof view?.matchMedia === 'function' ? view.matchMedia(query) : null;
    const scheme = media('(prefers-color-scheme: dark)');
    scheme?.addEventListener('change', bump);

    // Section: the contrast settings that turn decals on for every chart.
    const forced = media('(forced-colors: active)');
    const contrast = media('(prefers-contrast: more)');
    const read = (): boolean => forced?.matches === true || contrast?.matches === true;
    const forceDecals = signal(read());
    const onContrast = (): void => forceDecals.set(read());
    forced?.addEventListener('change', onContrast);
    // Forced colours also re-point the plot's inks to the system colours.
    forced?.addEventListener('change', bump);
    contrast?.addEventListener('change', onContrast);

    inject(DestroyRef).onDestroy(() => {
      observer?.disconnect();
      scheme?.removeEventListener('change', bump);
      forced?.removeEventListener('change', onContrast);
      forced?.removeEventListener('change', bump);
      contrast?.removeEventListener('change', onContrast);
    });
    this.theme = theme.asReadonly();
    this.forceDecals = forceDecals.asReadonly();
  }
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStorageLike } from '../state/tm-storage-json';
import { tmBoot } from './tm-boot';
import type { TmBootCaches, TmBootConfig, TmBootResult } from './tm-boot-types';

// The entry of the generated boot script (spec 0024 §4.2). The platform's
// package build bundles this file into `boot/tm-boot.template.js`, a classic
// script that declares `var TM_BOOT` before it; a distribution's build
// replaces that declaration's value with its boot configuration and serves
// the result as `tm-boot.js`, the first script of `index.html`. This file
// only reads the real globals, each guarded, since a browser may refuse
// storage or lack an API altogether, and hands them to `tmBoot`.

/** The boot configuration the distribution's build writes; `null` in the unrendered template. */
declare const TM_BOOT: TmBootConfig | null;

/** The window, with the result the app reads. */
type TmBootWindow = Window & { __tmBoot?: TmBootResult; BroadcastChannel?: unknown };

/** Reads a global that may throw (storage refused, sandboxed frame), as `null` when it throws or is missing. */
function guarded<T>(read: () => T | undefined): T | null {
  try {
    return read() || null;
  } catch {
    return null;
  }
}

if (TM_BOOT) {
  const win = window as TmBootWindow;
  const nav = navigator as Navigator & { locks?: unknown };
  const indexedDB = guarded(() => win.indexedDB);
  win.__tmBoot = tmBoot({
    config: TM_BOOT,
    document,
    location,
    history,
    cookies: guarded(() => document.cookie) || '',
    cookieEnabled: nav.cookieEnabled,
    languages: nav.languages || [nav.language],
    prefersDark: !!guarded(() => win.matchMedia('(prefers-color-scheme: dark)').matches),
    features: { locks: nav.locks, broadcastChannel: win.BroadcastChannel, indexedDB },
    localStorage: guarded<TmStorageLike>(() => win.localStorage),
    sessionStorage: guarded<TmStorageLike>(() => win.sessionStorage),
    indexedDB,
    caches: guarded<TmBootCaches>(() => win.caches),
    now: () => Date.now(),
    wait: (ms) => new Promise((resolve) => setTimeout(resolve, ms)),
  }).result;
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DOCUMENT, inject, InjectionToken } from '@angular/core';

import type { TmBootCaches } from '../../boot/tm-boot-types';
import type { TmStorageLike } from '../tm-storage-json';

// The browser facts the client store uses, behind one token, so that a test
// runs the store over another database factory, a refusing storage or none
// at all, and the in-memory store of the testing package runs it with no
// browser storage. Every member may be missing: a browser that refuses
// storage still runs the app, in memory (spec 0025 §2.7).

/** The browser facts the client store uses. */
export interface TmStoreHost {
  /** IndexedDB, or `null` when the browser refuses it; the store then runs in memory. */
  readonly indexedDB: IDBFactory | null;
  /** The device's local storage, which holds the boot record, or `null` when the browser refuses it. */
  readonly localStorage: TmStorageLike | null;
  /** CacheStorage, whose image cache a session's end deletes, or `null` when the browser lacks it. */
  readonly caches: TmBootCaches | null;
  /** Where the store listens for another tab's boot-record writes and for the page's lifecycle, or `null` for nowhere. */
  readonly events: Pick<EventTarget, 'addEventListener' | 'removeEventListener'> | null;
  /** The language and direction `<html>` carries now, the boot record's defaults. */
  documentLanguage(): { readonly lang: string; readonly dir: 'ltr' | 'rtl' };
  /**
   * Asks the browser to keep the site's storage under pressure, where asking
   * never prompts the user (Chromium and WebKit); does nothing on Gecko,
   * which prompts, and where the browser lacks the call.
   */
  persist(): void;
}

/** Whether the browser's engine is Gecko, whose `storage.persist()` prompts the user. */
function isGecko(userAgent: string): boolean {
  // Gecko alone writes `Gecko/<build>`; WebKit and Blink write `like Gecko`.
  return /\bGecko\/\d/.test(userAgent);
}

/** The browser behind the client store: the real one unless a test provides another. Internal. */
export const TM_STORE_HOST = new InjectionToken<TmStoreHost>('TM_STORE_HOST', {
  providedIn: 'root',
  factory: (): TmStoreHost => {
    const document = inject(DOCUMENT);
    const window = document.defaultView;
    // Each getter throws where the browser blocks site data.
    const read = <T>(get: () => T | undefined): T | null => {
      try {
        return get() ?? null;
      } catch {
        return null;
      }
    };
    const navigator = window?.navigator;
    return {
      indexedDB: read(() => window?.indexedDB),
      localStorage: read(() => window?.localStorage),
      caches: read(() => window?.caches),
      events: window,
      documentLanguage: () => {
        const html = document.documentElement;
        return { lang: html.lang || 'en', dir: html.dir === 'rtl' ? 'rtl' : 'ltr' };
      },
      persist: () => {
        if (navigator?.storage?.persist && !isGecko(navigator.userAgent)) {
          // A refusal changes nothing: the store is only a cache.
          navigator.storage.persist().catch(() => undefined);
        }
      },
    };
  },
});

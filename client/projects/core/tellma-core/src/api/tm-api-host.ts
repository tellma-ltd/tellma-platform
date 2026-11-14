// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DOCUMENT, inject, InjectionToken, isDevMode } from '@angular/core';

// The browser behind the HTTP layer: the one place the platform touches
// `fetch` and `XMLHttpRequest`, the connectivity flag and the page's
// visibility. The real browser is the default; the layer's specs provide a
// fake one, so every request, failure and visibility change is scripted.

/** The browser facts and transports the HTTP layer uses. */
export interface TmApiHost {
  /**
   * Sends one request, as `window.fetch` does.
   *
   * @param url - The request's URL.
   * @param init - Its method, headers, body and signal.
   */
  fetch(url: string, init: RequestInit): Promise<Response>;
  /** Creates the request object of an upload, which reports its progress. */
  xhr(): XMLHttpRequest;
  /** `navigator.onLine` now. */
  onLine(): boolean;
  /** Whether the page is visible now. */
  visible(): boolean;
  /**
   * Listens to an event of the window (`online`, `offline`) or the document
   * (`visibilitychange`).
   *
   * @param target - Where the event fires.
   * @param type - The event's name.
   * @param listener - What runs when it fires.
   * @returns The function that stops listening.
   */
  listen(target: 'window' | 'document', type: string, listener: () => void): () => void;
  /**
   * Fills an array with cryptographically random bytes, as `crypto.getRandomValues` does.
   *
   * @param array - The array to fill.
   */
  random(array: Uint8Array<ArrayBuffer>): Uint8Array<ArrayBuffer>;
  /** Whether the app runs in development mode, where a misuse of the layer throws. */
  devMode(): boolean;
}

/** The browser behind the HTTP layer: the real one unless a test provides another. */
export const TM_API_HOST = new InjectionToken<TmApiHost>('TM_API_HOST', {
  providedIn: 'root',
  factory: (): TmApiHost => {
    const document = inject(DOCUMENT);
    const window = document.defaultView!;
    return {
      fetch: (url, init) => window.fetch(url, init),
      xhr: () => new window.XMLHttpRequest(),
      onLine: () => window.navigator.onLine,
      visible: () => document.visibilityState === 'visible',
      listen: (target, type, listener) => {
        const on = target === 'window' ? window : document;
        on.addEventListener(type, listener);
        return () => on.removeEventListener(type, listener);
      },
      random: (array) => window.crypto.getRandomValues(array),
      devMode: isDevMode,
    };
  },
});

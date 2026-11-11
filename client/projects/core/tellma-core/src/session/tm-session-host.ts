// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DOCUMENT, inject, InjectionToken } from '@angular/core';

import type { TmStorageLike } from '../state/tm-storage-json';

// The browser facts the session reads and the navigations it makes, behind
// one token so that a test can stand in for the cookie jar, the session
// storage and the address bar, none of which a real browser lets a test
// replace.

/** The address bar, as the session reads and leaves it. */
export interface TmSessionLocation {
  /** The path, such as `/17/centers`. */
  readonly pathname: string;
  /** The query, with its `?`, or empty. */
  readonly search: string;
  /** The fragment, with its `#`, or empty. */
  readonly hash: string;
  /** Leaves for another page, keeping this one in the history. */
  assign(url: string): void;
  /** Leaves for another page, replacing this one in the history. */
  replace(url: string): void;
}

/** The tab's session storage, as the session reads, writes and sweeps it; any member may throw. */
export interface TmSessionStorage extends TmStorageLike {
  /** The number of items. */
  readonly length: number;
  /** The key at an index, or `null` past the end. */
  key(index: number): string | null;
}

/** The browser facts the session reads and the navigations it makes. */
export interface TmSessionHost {
  /** The cookie string now, as `document.cookie` returns it; empty when the browser refuses it. */
  cookies(): string;
  /** The tab's session storage, or `null` when the browser refuses it. */
  readonly sessionStorage: TmSessionStorage | null;
  /** The address bar. */
  readonly location: TmSessionLocation;
  /** The document's language now (`<html lang>`), which the app keeps current. */
  language(): string;
}

/** The browser behind the session: the real one unless a test provides another. */
export const TM_SESSION_HOST = new InjectionToken<TmSessionHost>('TM_SESSION_HOST', {
  providedIn: 'root',
  factory: () => {
    const document = inject(DOCUMENT);
    const window = document.defaultView!;
    let sessionStorage: TmSessionStorage | null;
    try {
      sessionStorage = window.sessionStorage;
    } catch {
      sessionStorage = null;
    }
    return {
      cookies: () => {
        try {
          return document.cookie;
        } catch {
          return '';
        }
      },
      sessionStorage,
      location: window.location,
      language: () => document.documentElement.lang,
    };
  },
});

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmLoginMark } from '../session/tm-login-mark';
import type { TmProfile } from '../session/tm-profile';
import type { TmBootRecord } from '../state/tm-boot-record';
import type { TmStorageLike } from '../state/tm-storage-json';

// The shapes of the boot script (spec 0024 §4.2): the configuration the build
// writes into it, the environment it runs over, and the result it leaves on
// `window.__tmBoot` for the app. Types only, shared by the script's sources
// and the app.

/** The boot strings of one language, copied by the build from the string packs. */
export interface TmBootStrings {
  /** The distribution's display name in this language. */
  readonly Distribution_DisplayName?: string;
  /** The boot page's heading, with the `{displayName}` placeholder. */
  readonly Boot_Title?: string;
  /** The sentence when a required browser feature is missing. */
  readonly Boot_Unsupported?: string;
  /** The sentence when cookies are off. */
  readonly Boot_Cookies?: string;
  /** The sentence when a sign-in came back without a session. */
  readonly Boot_SignInFailed?: string;
  /** The sentence when the app's files failed to load. */
  readonly Boot_LoadFailed?: string;
  /** The button's label. */
  readonly Boot_TryAgain?: string;
}

/** One language the distribution offers, as the boot script needs it. */
export interface TmBootLanguage {
  /** The language's code, such as `ar`. */
  readonly code: string;
  /** The language's text direction. */
  readonly dir: 'ltr' | 'rtl';
}

/**
 * The boot configuration (`TM_BOOT`) the build writes into the boot script:
 * the offered languages and each one's boot strings.
 */
export interface TmBootConfig {
  /** The configuration's version; always 1. */
  readonly v: 1;
  /** The offered languages, the distribution's default first. */
  readonly languages: readonly TmBootLanguage[];
  /** The distribution's default language. */
  readonly defaultLanguage: string;
  /** Each offered language's boot strings, and English's, the fallback of every string. */
  readonly strings: Readonly<Record<string, TmBootStrings>>;
}

/** Why the boot page shows. */
export type TmBootPageReason = 'unsupported' | 'cookies' | 'sign-in-failed' | 'load-failed';

/** What the boot script decided: the app runs, the tab is leaving to sign in, or the boot page shows. */
export type TmBootState = 'run' | 'redirect' | 'page';

/**
 * What the boot script leaves on `window.__tmBoot` for the app: its decision,
 * the session and boot record it read, and the launch facts it worked out.
 */
export interface TmBootResult {
  /** What the script decided; the app starts only on `run`. */
  readonly state: TmBootState;
  /** The signed-in user, or `null` without a valid profile cookie. */
  readonly profile: TmProfile | null;
  /** The boot record after the script's writes, or `null` when there is none. */
  readonly record: TmBootRecord | null;
  /** Whether the launch is cold: no record, or the record is another user's. */
  readonly cold: boolean;
  /** The tenant of an automatic entry, or `null` when the user chose the target. */
  readonly auto: number | null;
  /** The sign-in mark the script consumed on its way back from sign-in, or `null`. */
  readonly mark: TmLoginMark | null;
  /**
   * Shows the boot page in the boot language: the app calls it when the files
   * it needs before the first render fail to load.
   *
   * @param reason - Why the boot page shows.
   * @param retry - What Try again does; reloads the page when omitted.
   */
  readonly showPage: (reason: TmBootPageReason, retry?: () => void) => void;
}

/** The location members the boot script reads and the navigations it makes. */
export interface TmBootLocation {
  /** The path, such as `/17/centers`. */
  readonly pathname: string;
  /** The query, with its `?`, or empty. */
  readonly search: string;
  /** The fragment, with its `#`, or empty. */
  readonly hash: string;
  /** Leaves for another page, replacing this one in the history. */
  replace(url: string): void;
  /** Reloads the page. */
  reload(): void;
}

/** The history members the boot script uses to rewrite the address before the app starts. */
export interface TmBootHistory {
  /** The current entry's state, kept across a rewrite. */
  readonly state: unknown;
  /** Rewrites the current entry's address. */
  replaceState(state: unknown, unused: string, url: string): void;
}

/** The browser features the app needs: each must be present, or the boot page shows. */
export interface TmBootFeatures {
  /** `navigator.locks`. */
  readonly locks: unknown;
  /** `BroadcastChannel`. */
  readonly broadcastChannel: unknown;
  /** `indexedDB`. */
  readonly indexedDB: unknown;
}

/** The CacheStorage member the expiry delete uses. */
export interface TmBootCaches {
  /** Deletes one cache. */
  delete(name: string): Promise<boolean>;
}

/**
 * Everything the boot script reads and acts on, injected so that the decision
 * is a pure function of it. The generated script passes the real globals.
 */
export interface TmBootEnv {
  /** The boot configuration the build wrote. */
  readonly config: TmBootConfig;
  /** The document: `<html>`'s attributes, the title and the boot page. */
  readonly document: Document;
  /** The location. */
  readonly location: TmBootLocation;
  /** The history. */
  readonly history: TmBootHistory;
  /** The cookie string, as `document.cookie` returns it. */
  readonly cookies: string;
  /** Whether the browser accepts cookies (`navigator.cookieEnabled`). */
  readonly cookieEnabled: boolean;
  /** The user's preferred languages (`navigator.languages`). */
  readonly languages: readonly string[];
  /** Whether the system prefers a dark color scheme. */
  readonly prefersDark: boolean;
  /** The required browser features. */
  readonly features: TmBootFeatures;
  /** Local storage, or `null` when the browser refuses it. */
  readonly localStorage: TmStorageLike | null;
  /** Session storage, or `null` when the browser refuses it. */
  readonly sessionStorage: TmStorageLike | null;
  /** IndexedDB, or `null` when the browser refuses it. */
  readonly indexedDB: IDBFactory | null;
  /** CacheStorage, or `null` when the browser lacks it. */
  readonly caches: TmBootCaches | null;
  /** The current time in epoch milliseconds. */
  now(): number;
  /** A promise that resolves after the given milliseconds. */
  wait(ms: number): Promise<void>;
}

/** One run of the boot script: its result, and a promise that settles once its deferred work is done. */
export interface TmBootRun {
  /** The result, final as soon as the script returns. */
  readonly result: TmBootResult;
  /** Settles once the deferred work, such as the expiry delete and the redirect after it, is done. */
  readonly settled: Promise<void>;
}

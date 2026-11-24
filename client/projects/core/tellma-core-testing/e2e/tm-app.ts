// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type BrowserContext, expect, type Page } from '@playwright/test';

// The app-level e2e helpers (spec 0024 §13): what the boot script decided,
// what the client store holds, a warm app, several tabs, and the content
// security policy's violations. They read the app's own state (the boot
// result, the boot record, the store and the worker's caches) and wait on it,
// never on the clock.

/** What the boot script decided for a document, as a test reads it. */
export interface TmBootFacts {
  /** `run`, `redirect` or `page`; `null` before the script ran. */
  readonly state: 'run' | 'redirect' | 'page' | null;
  /** Whether the launch was cold. */
  readonly cold: boolean | null;
  /** The tenant of an automatic entry, or `null`. */
  readonly auto: number | null;
  /** The signed-in user's subject, or `null` without a session. */
  readonly sub: string | null;
  /** The signed-in user's email, or `null` without a session. */
  readonly email: string | null;
  /** The sign-in mark the script consumed, or `null`. */
  readonly mark: {
    readonly returnUrl: string;
    readonly auto: boolean;
    readonly sub: string | null;
    readonly hash: string;
  } | null;
}

/** The boot record as `localStorage` holds it. */
export interface TmBootRecordFacts {
  /** The theme: `light`, `dark` or `system`. */
  readonly theme: string;
  /** The boot language. */
  readonly lang: string;
  /** The user whose data the device holds, or `null`. */
  readonly sub: string | null;
  /** The tenant the user last entered, or `null`. */
  readonly lastTenantId: number | null;
}

/** One violation of the content security policy a page reported. */
export interface TmCspViolation {
  /** The page's URL when it reported the violation. */
  readonly page: string;
  /** The directive that was violated, such as `script-src-elem`. */
  readonly directive: string;
  /** What was blocked: a URL, `inline` or `eval`. */
  readonly blocked: string;
  /** The script that caused it, when the browser names one. */
  readonly source: string;
}

/** The violations {@link tmCspWatch} has collected. */
export interface TmCspWatch {
  /** Every violation the context's pages reported so far. */
  violations(): readonly TmCspViolation[];
}

/** The window members the helpers read inside a page. */
interface TmE2eWindow {
  /** The boot script's result. */
  readonly __tmBoot?: {
    readonly state: 'run' | 'redirect' | 'page';
    readonly cold: boolean;
    readonly auto: number | null;
    readonly profile: { readonly sub: string; readonly email: string } | null;
    readonly mark: TmBootFacts['mark'];
  };
  /** The binding the CSP watch reports through. */
  readonly __tmCspViolation?: (violation: Omit<TmCspViolation, 'page'>) => void;
}

/** The name of the client store's database and of its one object store. */
const STORE = { database: 'tm-app', docs: 'docs' } as const;

/** How long the worker may take to install and cache the shell. */
const WARM_UP_TIMEOUT_MS = 60_000;

/**
 * What the boot script decided for the page's current document.
 *
 * @param page - The page.
 * @returns The boot facts; every member is `null` before the script ran.
 */
export function tmBootFacts(page: Page): Promise<TmBootFacts> {
  return page.evaluate(() => {
    const boot = (window as TmE2eWindow).__tmBoot;
    return {
      state: boot?.state ?? null,
      cold: boot?.cold ?? null,
      auto: boot?.auto ?? null,
      sub: boot?.profile?.sub ?? null,
      email: boot?.profile?.email ?? null,
      mark: boot?.mark ?? null,
    };
  });
}

/**
 * The boot record the page's origin holds.
 *
 * @param page - The page.
 * @returns The record, or `null` when there is none or it does not parse.
 */
export function tmBootRecord(page: Page): Promise<TmBootRecordFacts | null> {
  return page.evaluate(() => {
    try {
      return JSON.parse(localStorage.getItem('tm.boot') ?? 'null') as TmBootRecordFacts | null;
    } catch {
      return null;
    }
  });
}

/**
 * The keys of the documents the client store holds under a partition prefix,
 * such as `u:<sub>/` for everything of one user.
 *
 * @param page - A page of the origin.
 * @param prefix - The partition prefix; every document when omitted.
 * @returns Each document's partition and name; a name carries its format version, such as `settings@1`.
 */
export function tmStoredDocs(
  page: Page,
  prefix = '',
): Promise<readonly (readonly [partition: string, name: string])[]> {
  return page.evaluate(
    ({ database, docs, prefix }) =>
      new Promise<(readonly [string, string])[]>((resolve) => {
        // Opened without a version: a store that does not exist yet is not created.
        const request = indexedDB.open(database);
        request.onupgradeneeded = () => request.transaction?.abort();
        request.onerror = () => resolve([]);
        request.onsuccess = () => {
          const db = request.result;
          try {
            const keys = db.transaction(docs, 'readonly').objectStore(docs).getAllKeys();
            keys.onsuccess = () => {
              db.close();
              resolve(
                (keys.result as [string, string][])
                  .filter(([partition]) => partition.startsWith(prefix))
                  .map(([p, n]) => [p, n] as const),
              );
            };
            keys.onerror = () => {
              db.close();
              resolve([]);
            };
          } catch {
            db.close();
            resolve([]);
          }
        };
      }),
    { ...STORE, prefix },
  );
}

/**
 * Makes the app warm in the page's context: it opens `url` signed in, waits
 * until the service worker controls the page and holds every file of its
 * `shell` group, and until the client store holds the launch documents of
 * the tenant the URL enters (or the user's, at a tenantless URL). A later
 * launch then renders from the device alone, offline included.
 *
 * @param page - A page of a signed-in context.
 * @param url - Where the app opens; the tenant home of the user's landing when omitted.
 * @returns A promise that settles once the app is warm.
 */
export async function warmUp(page: Page, url = '/'): Promise<void> {
  await page.goto(url);
  await expect
    .poll(async () => (await tmBootFacts(page)).state, { timeout: WARM_UP_TIMEOUT_MS })
    .toBe('run');

  // --- The worker: it controls the page and has cached its whole shell group ---
  await page.waitForFunction(() => navigator.serviceWorker?.controller != null, null, {
    timeout: WARM_UP_TIMEOUT_MS,
  });
  await expect
    .poll(
      () =>
        page.evaluate(async () => {
          const response = await fetch(`/ngsw.json?ngsw-cache-bust=${Math.random()}`);
          const manifest = (await response.json()) as {
            assetGroups: { name: string; urls: string[] }[];
          };
          const urls = manifest.assetGroups.find((group) => group.name === 'shell')?.urls ?? [];
          for (const name of (await caches.keys()).filter((key) =>
            key.endsWith(':assets:shell:cache'),
          )) {
            const cache = await caches.open(name);
            const held = await Promise.all(
              urls.map((file) => cache.match(new URL(file, location.origin).href)),
            );
            if (held.every((entry) => entry !== undefined)) {
              return true;
            }
          }
          return false;
        }),
      { timeout: WARM_UP_TIMEOUT_MS, message: 'the service worker cached its shell group' },
    )
    .toBe(true);

  // --- The store: the boot record names the user, and the entered tenant's launch documents are kept ---
  const { sub } = await tmBootFacts(page);
  const tenant = /^\/([1-9]\d*)(?:\/|$)/.exec(new URL(page.url()).pathname)?.[1];
  // A tenant's partition, or the user's own at a tenantless URL, whose `tenants` list the picker reads.
  const partition = tenant ? `u:${sub}/t${tenant}` : `u:${sub}/`;
  const wanted = tenant ? ['settings', 'me', 'tenant'] : ['tenants'];
  await expect
    .poll(
      async () => {
        const record = await tmBootRecord(page);
        // A document's name carries its format version after an `@`, such as `settings@1`.
        const names = (await tmStoredDocs(page, partition))
          .filter(([key]) => key === partition)
          .map(([, name]) => name.replace(/@\d+$/, ''));
        return record?.sub === sub && wanted.every((name) => names.includes(name));
      },
      { timeout: WARM_UP_TIMEOUT_MS, message: 'the client store holds the launch documents' },
    )
    .toBe(true);
}

/**
 * Opens tabs of one context at a URL and waits until the app runs in each.
 *
 * @param context - The browser context, signed in.
 * @param count - How many tabs.
 * @param url - Where each tab opens.
 * @returns The tabs, in the order they opened.
 */
export async function openTabs(
  context: BrowserContext,
  count: number,
  url: string,
): Promise<readonly Page[]> {
  const pages: Page[] = [];
  for (let index = 0; index < count; index++) {
    const page = await context.newPage();
    await page.goto(url);
    await expect.poll(async () => (await tmBootFacts(page)).state).toBe('run');
    pages.push(page);
  }
  return pages;
}

/**
 * Collects every violation of the content security policy that the
 * context's pages report, from the first script of every document on.
 * Install it before the first navigation.
 *
 * @param context - The browser context.
 * @returns The watch.
 */
export async function tmCspWatch(context: BrowserContext): Promise<TmCspWatch> {
  const violations: TmCspViolation[] = [];
  await context.exposeBinding(
    '__tmCspViolation',
    (source, violation: Omit<TmCspViolation, 'page'>) => {
      violations.push({ page: source.page?.url() ?? '', ...violation });
    },
  );
  await context.addInitScript(() => {
    document.addEventListener('securitypolicyviolation', (event) => {
      (window as TmE2eWindow).__tmCspViolation?.({
        directive: event.effectiveDirective,
        blocked: event.blockedURI,
        source: event.sourceFile ? `${event.sourceFile}:${event.lineNumber}` : '',
      });
    });
  });
  return { violations: () => [...violations] };
}

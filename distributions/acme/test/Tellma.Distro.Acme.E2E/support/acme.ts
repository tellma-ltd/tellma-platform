// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  type BrowserContext,
  type Locator,
  type Page,
  test as base,
  expect,
} from '@playwright/test';
import { tmCspWatch, tmExpireSession, type TmCspWatch } from '@tellma/core-testing/e2e';

// The suite's shared fixture and its Acme facts: every test's context runs
// under a watch of the production content security policy, which fails the
// test on any violation; the principals the development seed signs in; and
// the few page probes the flows share.

/** The principals the Acme development seed provides, by their roles in the flows. */
export const ACME_PRINCIPALS = {
  /** The administrator: the live tenant Acme (1) and its sandbox (2). */
  admin: 'admin@localhost',
  /** A sales representative in every development tenant. */
  sales: 'sales@localhost',
  /** A center reader in every development tenant. */
  centers: 'centers@localhost',
  /** A tester of the sandbox (2) alone, with no live tenant. */
  sandbox: 'sandbox@localhost',
} as const;

/** The seeded tenants. */
export const ACME_TENANTS = { live: 1, sandbox: 2 } as const;

/** What a test gets beyond Playwright's own fixtures. */
export interface AcmeFixtures {
  /** The policy watch of the test's own context, checked when the test ends. */
  readonly csp: TmCspWatch;
  /** Opens another context like the test's own, under its own policy watch, checked when the test ends. */
  readonly newContext: () => Promise<BrowserContext>;
}

/** The suite's `test`: Playwright's, with every context under the policy watch. */
export const test = base.extend<AcmeFixtures>({
  csp: [
    async ({ context }, use) => {
      const watch = await tmCspWatch(context);
      await use(watch);
      expect(watch.violations(), 'no page violated the content security policy').toEqual([]);
    },
    { auto: true },
  ],
  newContext: async ({ browser, baseURL, locale }, use) => {
    const opened: {
      readonly context: BrowserContext;
      readonly watch: TmCspWatch;
    }[] = [];
    await use(async () => {
      const context = await browser.newContext({
        baseURL,
        locale,
        ignoreHTTPSErrors: true,
      });
      opened.push({ context, watch: await tmCspWatch(context) });
      return context;
    });
    for (const { context, watch } of opened) {
      await context.close();
      expect(watch.violations(), 'no page violated the content security policy').toEqual([]);
    }
  },
});

export { expect };

/**
 * The session-ended dialog.
 *
 * @param page - The page.
 * @returns Its locator.
 */
export function sessionEndedDialog(page: Page): Locator {
  return page.getByRole('alertdialog', { name: 'Your session ended' });
}

/**
 * Ends the session of the page's context while the app runs, and waits for
 * the app to notice: the re-check of a tab coming into view finds the
 * profile cookie gone and shows the session-ended dialog. The re-check
 * triggers arm once the app's session chunk has loaded after the first
 * render, so the tab keeps coming into view until the dialog shows.
 *
 * @param page - A page of the signed-in app.
 * @returns A promise that settles once the dialog shows.
 */
export async function expireInApp(page: Page): Promise<void> {
  await tmExpireSession(page.context());
  await comeIntoViewUntil(page, sessionEndedDialog(page));
}

/**
 * Brings the page into view, as a tab the user returns to, until a locator
 * shows: the app's re-check triggers arm after its first render.
 *
 * @param page - The page.
 * @param shown - What shows once the app has re-checked.
 * @returns A promise that settles once it shows.
 */
export async function comeIntoViewUntil(page: Page, shown: Locator): Promise<void> {
  await expect(async () => {
    await page.evaluate(() => document.dispatchEvent(new Event('visibilitychange')));
    await expect(shown).toBeVisible({ timeout: 1_000 });
  }).toPass({ timeout: 30_000 });
}

/** What the document looked like when its first frame was about to paint. */
export interface FirstPaint {
  /** `<html lang>`. */
  readonly lang: string;
  /** `<html dir>`. */
  readonly dir: string;
  /** `<html data-theme>`. */
  readonly theme: string | null;
}

/**
 * Records, in every document of the context, the `<html>` attributes at the
 * first animation frame, which runs before the first paint.
 *
 * @param context - The context.
 * @returns A promise that settles once the recording is installed.
 */
export async function recordFirstPaint(context: BrowserContext): Promise<void> {
  await context.addInitScript(() => {
    requestAnimationFrame(() => {
      const html = document.documentElement;
      (window as { __acmeFirstPaint?: FirstPaint }).__acmeFirstPaint = {
        lang: html.lang,
        dir: html.dir,
        theme: html.dataset['theme'] ?? null,
      };
    });
  });
}

/**
 * The first paint the page's current document recorded.
 *
 * @param page - The page.
 * @returns The attributes, once the first frame ran.
 */
export async function firstPaint(page: Page): Promise<FirstPaint> {
  await page.waitForFunction(
    () => (window as { __acmeFirstPaint?: FirstPaint }).__acmeFirstPaint !== undefined,
  );
  return page.evaluate(
    () => (window as unknown as { __acmeFirstPaint: FirstPaint }).__acmeFirstPaint,
  );
}

/**
 * Records, in every document of the context, whether an element matching a
 * selector was ever in the document, however briefly.
 *
 * @param context - The context.
 * @param selector - The selector.
 * @returns A promise that settles once the recording is installed.
 */
export async function recordSeen(context: BrowserContext, selector: string): Promise<void> {
  await context.addInitScript((wanted) => {
    const seen = ((window as { __acmeSeen?: Record<string, boolean> }).__acmeSeen ??= {});
    const look = (): void => {
      if (document.querySelector(wanted)) {
        seen[wanted] = true;
      }
    };
    new MutationObserver(look).observe(document, {
      childList: true,
      subtree: true,
    });
  }, selector);
}

/**
 * Whether the page's current document ever held an element {@link recordSeen} watched for.
 *
 * @param page - The page.
 * @param selector - The selector.
 * @returns True when it was seen.
 */
export function wasSeen(page: Page, selector: string): Promise<boolean> {
  return page.evaluate(
    (wanted) => (window as { __acmeSeen?: Record<string, boolean> }).__acmeSeen?.[wanted] === true,
    selector,
  );
}

/**
 * Takes the browser's Web Locks away in every document of the context, as a
 * browser the app cannot run in.
 *
 * @param context - The context.
 * @returns A promise that settles once the removal is installed.
 */
export async function removeWebLocks(context: BrowserContext): Promise<void> {
  await context.addInitScript(() => {
    delete (Navigator.prototype as { locks?: unknown }).locks;
  });
}

/**
 * Answers the BFF's sign-in with an empty page of the app's origin, so a tab
 * that leaves to sign in stays where its stored state can be read.
 *
 * @param context - The context.
 * @returns A promise that settles once the route is installed.
 */
export async function stopAtSignIn(context: BrowserContext): Promise<void> {
  await context.route(
    (url) => url.pathname === '/bff/login',
    (route) =>
      route.fulfill({
        status: 200,
        contentType: 'text/html',
        body: '<!doctype html><title>Sign-in stopped</title>',
      }),
  );
}

/**
 * Records, in every document of the context, each page heading that ever
 * shows: what a launch rendered on its way.
 *
 * @param context - The context.
 * @returns A promise that settles once the recording is installed.
 */
export async function recordHeadings(context: BrowserContext): Promise<void> {
  await context.addInitScript(() => {
    const texts: string[] = [];
    (window as { __acmeHeadings?: string[] }).__acmeHeadings = texts;
    const look = (): void => {
      for (const heading of document.querySelectorAll('h1')) {
        const text = heading.textContent?.trim() ?? '';
        if (text && !texts.includes(text)) {
          texts.push(text);
        }
      }
    };
    new MutationObserver(look).observe(document, {
      childList: true,
      subtree: true,
      characterData: true,
    });
  });
}

/**
 * The headings {@link recordHeadings} saw in the page's current document.
 *
 * @param page - The page.
 * @returns Every heading text, in the order they showed.
 */
export function headings(page: Page): Promise<readonly string[]> {
  return page.evaluate(() => (window as { __acmeHeadings?: string[] }).__acmeHeadings ?? []);
}

/** A request the app's document issued, and whether its shell had rendered by then. */
export interface IssuedRequest {
  /** The request's URL. */
  readonly url: string;
  /** Whether `tm-app` had rendered its first routed view when the app called `fetch`. */
  readonly afterShell: boolean;
}

/**
 * Records, in every document of the context, each `fetch` the page makes
 * and whether the app's first render, `tm-app` and its routed view, was in
 * the page at the call. The DOM is read in the call itself, so a request the
 * app issues in the same task that rendered counts as after the render, and
 * one issued before counts as before, with no clock between them.
 *
 * @param context - The context.
 * @returns A promise that settles once the recording is installed.
 */
export async function recordRequests(context: BrowserContext): Promise<void> {
  await context.addInitScript(() => {
    const issued: { url: string; afterShell: boolean }[] = [];
    (window as { __acmeRequests?: typeof issued }).__acmeRequests = issued;
    const fetch = window.fetch.bind(window);
    window.fetch = (input, init) => {
      const url = input instanceof Request ? input.url : String(input);
      issued.push({
        url: new URL(url, location.href).href,
        // The router outlet's routed view is its next sibling.
        afterShell: document.querySelector('tm-app > router-outlet + *') !== null,
      });
      return fetch(input, init);
    };
  });
}

/**
 * The requests {@link recordRequests} saw the page's current document issue.
 *
 * @param page - The page.
 * @returns Every request, in the order the app issued them.
 */
export function issuedRequests(page: Page): Promise<readonly IssuedRequest[]> {
  return page.evaluate(() => (window as { __acmeRequests?: IssuedRequest[] }).__acmeRequests ?? []);
}

/**
 * Waits until the page's heading reads a text, and returns when it first
 * did, in milliseconds since the document's navigation started.
 *
 * @param page - The page.
 * @param text - The heading's text.
 * @returns The time, checked on every animation frame.
 */
export async function headingShownAt(page: Page, text: string): Promise<number> {
  const handle = await page.waitForFunction(
    (wanted) => {
      const heading = document.querySelector('h1');
      return heading?.textContent?.trim() === wanted && heading.checkVisibility()
        ? performance.now()
        : false;
    },
    text,
    { polling: 'raf' },
  );
  return (await handle.jsonValue()) as number;
}

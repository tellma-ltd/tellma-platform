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

/**
 * The principal whose account reads Arabic: the run's global setup sets its
 * language, since the development seed creates every account in English.
 */
export const ACME_ARABIC_READER = ACME_PRINCIPALS.sales;

/** A language the suite's projects run in. */
export type AcmeLanguage = 'en' | 'ar';

/** The principals a flow signs in as, by their parts in it. */
export interface AcmeCast {
  /** The flow's user, a member of the live tenant (1) and its sandbox (2), whose account reads the project's language. */
  readonly user: string;
  /** Another member of both tenants, who takes the device over. */
  readonly other: string;
  /** The sandbox tester, a member of the sandbox (2) alone. Its account reads English in every project. */
  readonly sandbox: string;
}

/** Each project's cast: the Arabic project's user is the account that reads Arabic. */
const CASTS: Readonly<Record<AcmeLanguage, AcmeCast>> = {
  en: {
    user: ACME_PRINCIPALS.admin,
    other: ACME_PRINCIPALS.sales,
    sandbox: ACME_PRINCIPALS.sandbox,
  },
  ar: {
    user: ACME_ARABIC_READER,
    other: ACME_PRINCIPALS.centers,
    sandbox: ACME_PRINCIPALS.sandbox,
  },
};

/** The texts the flows match, in a project's language. */
export interface AcmeText {
  /** The tenant home's heading. */
  readonly home: string | RegExp;
  /** The customers pages' heading. */
  readonly customers: string | RegExp;
  /** The tenant picker's heading. */
  readonly chooseCompany: string | RegExp;
  /** The session-ended dialog's title. */
  readonly sessionEnded: string | RegExp;
  /** The session-ended dialog's message. */
  readonly sessionEndedMessage: string | RegExp;
  /** The sign-in button of the signed-out page and the session-ended dialog. */
  readonly signIn: string | RegExp;
  /** The signed-out page's heading. */
  readonly signedOut: string | RegExp;
}

/** The texts in each language, as the app's string packs word them; an Arabic imperative takes either gender's form. */
export const ACME_TEXT: Readonly<Record<AcmeLanguage, AcmeText>> = {
  en: {
    home: 'Home',
    customers: 'Customers',
    chooseCompany: 'Choose a company',
    sessionEnded: 'Your session ended',
    sessionEndedMessage: 'Sign in to continue. Your work is kept.',
    signIn: 'Sign in',
    signedOut: "You're signed out",
  },
  ar: {
    home: 'الرئيسية',
    customers: 'العملاء',
    chooseCompany: /^(?:اختر|اختاري) شركة$/,
    sessionEnded: 'انتهت جلستك',
    sessionEndedMessage: /(?:سجّل|سجّلي) الدخول للمتابعة\. عملك محفوظ\./,
    signIn: 'تسجيل الدخول',
    signedOut: /^(?:لقد سجّلت الخروج|لقد سجّلتِ الخروج)$/,
  },
};

/** What a test gets beyond Playwright's own fixtures. */
export interface AcmeFixtures {
  /** The policy watch of the test's own context, checked when the test ends. */
  readonly csp: TmCspWatch;
  /** Opens another context like the test's own, under its own policy watch, checked when the test ends. */
  readonly newContext: () => Promise<BrowserContext>;
  /** The project's language, from its browser locale. */
  readonly lang: AcmeLanguage;
  /** The texts the flows match, in the project's language. */
  readonly text: AcmeText;
  /** The principals the flows sign in as, in the project's language. */
  readonly cast: AcmeCast;
}

/** The suite's `test`: Playwright's, with every context under the policy watch. */
export const test = base.extend<AcmeFixtures>({
  lang: async ({ locale }, use) => {
    await use(locale?.startsWith('ar') === true ? 'ar' : 'en');
  },
  text: async ({ lang }, use) => {
    await use(ACME_TEXT[lang]);
  },
  cast: async ({ lang }, use) => {
    await use(CASTS[lang]);
  },
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
 * The session-ended dialog: the one alert dialog these flows meet. Its title
 * is checked in the project's language by the flow that reads it.
 *
 * @param page - The page.
 * @returns Its locator.
 */
export function sessionEndedDialog(page: Page): Locator {
  return page.getByRole('alertdialog');
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
 * @param text - The heading's text, or a pattern it matches.
 * @returns The time, checked on every animation frame.
 */
export async function headingShownAt(page: Page, text: string | RegExp): Promise<number> {
  const wanted = typeof text === 'string' ? { text } : { source: text.source, flags: text.flags };
  const handle = await page.waitForFunction(
    (wanted) => {
      const heading = document.querySelector('h1');
      const shown = heading?.textContent?.trim() ?? '';
      const matches =
        wanted.text !== undefined
          ? shown === wanted.text
          : new RegExp(wanted.source!, wanted.flags).test(shown);
      return matches && heading!.checkVisibility() ? performance.now() : false;
    },
    wanted as { text?: string; source?: string; flags?: string },
    { polling: 'raf' },
  );
  return (await handle.jsonValue()) as number;
}

/**
 * The timing budgets' multiplier: `TM_PERF_MULTIPLIER` when set, else 2 on
 * CI, whose shared runners are slower and noisier than a quiet machine.
 */
export const ACME_PERF_MULTIPLIER = Number(
  process.env['TM_PERF_MULTIPLIER'] ?? (process.env['CI'] ? 2 : 1),
);

/** How many times a timing gate measures before it judges the median. */
export const ACME_TIMING_SAMPLES = 3;

/**
 * The median of some samples: one slow sample on a busy machine moves it no
 * more than one fast sample does.
 *
 * @param samples - The samples; at least one.
 * @returns The middle sample, or the mean of the middle two.
 */
export function median(samples: readonly number[]): number {
  const sorted = [...samples].sort((a, b) => a - b);
  const middle = Math.floor(sorted.length / 2);
  return sorted.length % 2 === 1 ? sorted[middle]! : (sorted[middle - 1]! + sorted[middle]!) / 2;
}

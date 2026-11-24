// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="node" />

import { mkdir, readFile, readdir, rename, rm, stat, writeFile } from 'node:fs/promises';
import { join } from 'node:path';

import { type Browser, type BrowserContext, type Cookie, type Page } from '@playwright/test';

import { tmE2eHost, type TmE2eHost } from './tm-e2e-host';
import { type TmDroppedMail, tmMailIsFor, tmSignInCode } from './tm-e2e-wire';

// Signing in on the e2e host (spec 0024 §13). The identity engine issues at
// most five sign-in codes per user and ten per address per hour, so a run
// spends one code per principal at most: the identity engine's own sign-in
// cookie is kept per principal in the state directory, and every later
// sign-in is the ordinary round trip through the BFF, which the engine
// completes from that cookie without asking for a code. Each sign-in starts a
// session of its own, so a test that ends its session never ends another's.

/** The BFF's two cookies: the session, and the profile the app reads. */
const APP_SESSION_COOKIES = ['__Host-tellma.session', 'tellma.profile'] as const;

/** The path a sign-in round trip returns to; it is answered in place, so no app starts in the scratch context. */
const LANDING = '/tenants';

/** The identity engine's sign-in page, where a round trip stops when the engine needs a code. */
const LOGIN_PAGE = /\/id\/Identity\/Account\/Login(?:$|[?#])/;

/** How long a code may take to reach the mail drop. */
const CODE_TIMEOUT_MS = 30_000;

/** How long a round trip through the BFF and the identity engine may take. */
const ROUND_TRIP_TIMEOUT_MS = 60_000;

/** How many times a round trip that ends at a server error is tried. */
const ROUND_TRIP_ATTEMPTS = 3;

/** How long a stale lock of another worker's code sign-in is honoured. */
const LOCK_STALE_MS = 120_000;

/** The profile cookie's members the sign-in checks. */
interface TmProfileCookie {
  /** The user's email. */
  readonly email: string;
}

/**
 * Signs the page's browser context in as a principal: it gets a session of
 * its own, the app's two cookies, and the identity engine's sign-in, in place
 * of any it held. The page stays where it is; the next navigation runs as the
 * principal.
 *
 * Only the first sign-in of a principal on a host asks for an email code; it
 * reads the code from the host's mail drop. Later ones complete from the
 * identity engine's sign-in, which is kept per principal between tests and
 * runs.
 *
 * @param page - The page whose context signs in.
 * @param email - The principal's email address.
 * @returns A promise that settles once the context holds the session.
 */
export async function signInAs(page: Page, email: string): Promise<void> {
  const cookies = await tmSessionCookies(browserOf(page.context()), email);
  await page.context().clearCookies();
  await page.context().addCookies(cookies);
}

/**
 * Gives a context the identity engine's sign-in of a principal and no app
 * session, as when the app's session ended while the identity engine's
 * lasted: the app's next sign-in round trip then completes as that principal,
 * without a code. A shared device's second user, or another user signing in
 * from the session-ended dialog, starts this way.
 *
 * @param context - The browser context.
 * @param email - The principal's email address.
 * @returns A promise that settles once the context holds the sign-in.
 */
export async function tmSignInToIdentityAs(context: BrowserContext, email: string): Promise<void> {
  const cookies = await tmSessionCookies(browserOf(context), email);
  await context.clearCookies();
  await context.addCookies(cookies.filter((cookie) => !isAppSessionCookie(cookie)));
}

/**
 * Ends the app's session in a context as an idle expiry does: the session
 * and profile cookies go, and the identity engine's sign-in stays, so the
 * app's next sign-in completes without a code.
 *
 * @param context - The browser context.
 * @returns A promise that settles once the cookies are gone.
 */
export async function tmExpireSession(context: BrowserContext): Promise<void> {
  for (const name of APP_SESSION_COOKIES) {
    await context.clearCookies({ name });
  }
}

/**
 * A fresh session of a principal: the cookies a context holds after the
 * app's sign-in round trip, which runs in a scratch context of its own.
 *
 * @param browser - The browser.
 * @param email - The principal's email address.
 * @returns The cookies, the app's session and the identity engine's sign-in.
 */
async function tmSessionCookies(browser: Browser, email: string): Promise<readonly Cookie[]> {
  const host = tmE2eHost();
  await mkdir(host.stateDir, { recursive: true });
  const scratch = await browser.newContext({ ignoreHTTPSErrors: true, baseURL: host.origin });
  try {
    // The landing is answered here: the app must not start, and write its
    // stored state, in a context that only carries cookies across.
    await scratch.route(
      (url) => url.origin === host.origin && url.pathname === LANDING,
      (route) =>
        route.fulfill({
          status: 200,
          contentType: 'text/html',
          body: '<!doctype html><title>Signed in</title>',
        }),
    );
    const kept = await readIdentity(host, email);
    if (kept) {
      await scratch.addCookies(kept.cookies);
    }
    const page = await scratch.newPage();
    if ((await roundTrip(page, host)) === 'login') {
      await withLock(host, email, async () => {
        // Another worker may have signed the principal in while this one
        // waited for the lock: its kept sign-in spares a second code.
        const now = await readIdentity(host, email);
        if (now && now.writtenAt !== kept?.writtenAt) {
          await scratch.addCookies(now.cookies);
          if ((await roundTrip(page, host)) === 'landed') {
            return;
          }
        }
        await signInWithCode(page, host, email);
      });
    }

    // --- The session: both app cookies, for this principal ---
    const cookies = await scratch.cookies(host.origin);
    const profile = cookies.find((cookie) => cookie.name === 'tellma.profile');
    const signedInAs = profile ? parseProfile(profile.value)?.email : undefined;
    if (
      signedInAs?.toLowerCase() !== email.toLowerCase() ||
      !cookies.some((cookie) => cookie.name === APP_SESSION_COOKIES[0])
    ) {
      throw new Error(
        `The sign-in as ${email} ended without a session of that user (the profile names ${signedInAs ?? 'no one'}).`,
      );
    }
    await writeIdentity(
      host,
      email,
      cookies.filter((cookie) => !isAppSessionCookie(cookie)),
    );
    return cookies;
  } finally {
    await scratch.close();
  }
}

/**
 * Runs the app's sign-in round trip: the BFF's login, then the identity
 * engine, then back to the landing. The identity engine refuses a grant that
 * raced another grant of the same user for the same authorization, and the
 * round trip then ends at a server error with "try again": parallel workers
 * signing one principal in on a host whose authorization does not yet cover
 * it meet that, so a round trip that ends at a server error is run again.
 * The retry guards the harness only: the identity engine's race is a product
 * fault tracked on its own, and a fix there leaves this retry idle.
 *
 * @param page - A page of the scratch context.
 * @param host - The host.
 * @returns `landed` when it came back signed in, `login` when the identity engine asks for a code.
 * @throws Error when every attempt ends at a server error.
 */
async function roundTrip(page: Page, host: TmE2eHost): Promise<'landed' | 'login'> {
  let failure = '';
  for (let attempt = 1; attempt <= ROUND_TRIP_ATTEMPTS; attempt++) {
    // --- One attempt: the page arrives at the landing or the sign-in page, or a document fails ---
    const failed = page
      .waitForResponse(
        (response) =>
          response.request().isNavigationRequest() &&
          response.frame() === page.mainFrame() &&
          response.status() >= 500,
        { timeout: ROUND_TRIP_TIMEOUT_MS },
      )
      .then(
        async (response) => `${response.status()} at ${response.url()}: ${await response.text()}`,
        // No failing document before the wait ended: the arrival decides, its timeout included.
        () => new Promise<never>(() => undefined),
      );
    const arrived = page
      .waitForURL((url) => url.pathname === LANDING || LOGIN_PAGE.test(url.pathname + url.search), {
        timeout: ROUND_TRIP_TIMEOUT_MS,
      })
      .then(() => '');
    // A failure decides the race; the arrival's later timeout is then no one's error.
    arrived.catch(() => undefined);
    await page.goto(
      `${host.origin}/bff/login?returnUrl=${encodeURIComponent(LANDING)}&ui_locales=en`,
    );
    failure = await Promise.race([arrived, failed]);
    if (!failure) {
      return new URL(page.url()).pathname === LANDING ? 'landed' : 'login';
    }
  }
  throw new Error(
    `The sign-in round trip failed ${ROUND_TRIP_ATTEMPTS} times; the last answer was ${failure}`,
  );
}

/**
 * Completes the identity engine's email-code sign-in on its sign-in page,
 * with the code read from the host's mail drop, and waits for the landing.
 *
 * @param page - The page, on the sign-in page.
 * @param host - The host.
 * @param email - The principal's email address.
 * @returns A promise that settles once the page is back at the landing.
 */
async function signInWithCode(page: Page, host: TmE2eHost, email: string): Promise<void> {
  // The baseline comes before the request, so an older message never passes for the new code.
  const before = new Set(await mailFiles(host));
  await page.getByLabel('Email').fill(email);
  await page.getByRole('button', { name: 'Email me a sign-in code' }).click();
  const code = await waitForCode(host, email, before);
  await page.getByLabel('Code').fill(code);
  await page.getByRole('button', { name: 'Verify' }).click();
  await page.waitForURL((url) => url.pathname === LANDING, { timeout: ROUND_TRIP_TIMEOUT_MS });
}

/**
 * Waits for a message to a principal that the mail drop wrote after a
 * baseline, and reads the sign-in code from it.
 *
 * @param host - The host.
 * @param email - The principal's email address.
 * @param before - The drop's files before the code was requested.
 * @returns The code.
 * @throws Error when no code arrives in time.
 */
async function waitForCode(
  host: TmE2eHost,
  email: string,
  before: ReadonlySet<string>,
): Promise<string> {
  const deadline = Date.now() + CODE_TIMEOUT_MS;
  while (Date.now() < deadline) {
    for (const file of await mailFiles(host)) {
      if (before.has(file)) {
        continue;
      }
      const mail = JSON.parse(await readFile(join(host.mailDrop, file), 'utf8')) as TmDroppedMail;
      const code = tmMailIsFor(mail, email) ? tmSignInCode(mail.textBody) : null;
      if (code) {
        return code;
      }
    }
    // The drop is a directory another process writes; a short pause between reads is the poll.
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
  throw new Error(
    `No sign-in code for ${email} reached ${host.mailDrop} within ${CODE_TIMEOUT_MS / 1000} s. ` +
      'The identity engine issues at most 5 codes per user and 10 per address per hour, and issues none silently past that; ' +
      `the kept sign-ins in ${host.stateDir} spare the codes between runs.`,
  );
}

/**
 * The mail drop's message files, oldest first. Files still being written
 * carry another extension and are left out.
 *
 * @param host - The host.
 * @returns The file names.
 */
async function mailFiles(host: TmE2eHost): Promise<readonly string[]> {
  try {
    return (await readdir(host.mailDrop)).filter((file) => file.endsWith('.json')).sort();
  } catch {
    return [];
  }
}

/**
 * Runs a step under a per-principal lock. A code sign-in runs under it, so
 * two workers that find no kept sign-in at once spend one code between them,
 * not two: the second one, once it holds the lock, finds the first one's
 * sign-in kept and uses it. Writing the kept sign-in runs under it too, so
 * two workers never replace the file at once, which Windows refuses. A lock
 * older than {@link LOCK_STALE_MS} is a crashed worker's and is broken.
 *
 * @param host - The host.
 * @param email - The principal's email address.
 * @param step - The step to run while the lock is held.
 * @returns A promise that settles when the step is done.
 */
async function withLock(
  host: TmE2eHost,
  email: string,
  step: () => Promise<void>,
): Promise<void> {
  const lock = join(host.stateDir, `${fileName(email)}.lock`);
  for (;;) {
    try {
      await mkdir(lock);
      break;
    } catch {
      const age = await stat(lock).then(
        (info) => Date.now() - info.mtimeMs,
        () => 0,
      );
      if (age > LOCK_STALE_MS) {
        await rm(lock, { recursive: true, force: true });
      } else {
        await new Promise((resolve) => setTimeout(resolve, 250));
      }
    }
  }
  try {
    await step();
  } finally {
    await rm(lock, { recursive: true, force: true });
  }
}

/** A principal's kept identity sign-in. */
interface TmKeptIdentity {
  /** The identity engine's cookies. */
  readonly cookies: readonly Cookie[];
  /** When the file was written, which tells a newer sign-in from the one already tried. */
  readonly writtenAt: number;
}

/**
 * The identity engine's sign-in kept for a principal, or `null` when none is.
 *
 * @param host - The host.
 * @param email - The principal's email address.
 * @returns The kept sign-in.
 */
async function readIdentity(host: TmE2eHost, email: string): Promise<TmKeptIdentity | null> {
  const file = join(host.stateDir, `${fileName(email)}.json`);
  try {
    const [text, info] = await Promise.all([readFile(file, 'utf8'), stat(file)]);
    return { cookies: JSON.parse(text) as Cookie[], writtenAt: info.mtimeMs };
  } catch {
    return null;
  }
}

/**
 * Keeps a principal's identity sign-in, written under a temporary name and
 * renamed, so a reader in another worker never sees half a file, and under
 * the principal's lock, so two writers never rename onto it at once.
 *
 * @param host - The host.
 * @param email - The principal's email address.
 * @param cookies - The cookies.
 * @returns A promise that settles once the file is in place.
 */
async function writeIdentity(
  host: TmE2eHost,
  email: string,
  cookies: readonly Cookie[],
): Promise<void> {
  const file = join(host.stateDir, `${fileName(email)}.json`);
  await withLock(host, email, async () => {
    const partial = `${file}.${process.pid}.${Date.now()}.partial`;
    await writeFile(partial, JSON.stringify(cookies));
    await rename(partial, file);
  });
}

/** The file name a principal's state is kept under. */
function fileName(email: string): string {
  return email.toLowerCase().replace(/[^a-z0-9.@-]/g, '_');
}

/** Whether a cookie is one of the app's session cookies, which a sign-in replaces and an expiry removes. */
function isAppSessionCookie(cookie: Cookie): boolean {
  return (APP_SESSION_COOKIES as readonly string[]).includes(cookie.name);
}

/** The browser a context belongs to. */
function browserOf(context: BrowserContext): Browser {
  const browser = context.browser();
  if (!browser) {
    throw new Error(
      'Signing in needs a context of a launched browser, which can open a scratch context beside it.',
    );
  }
  return browser;
}

/** The profile cookie's JSON, decoded from its unpadded base64url, or `null` when it does not decode. */
function parseProfile(value: string): TmProfileCookie | null {
  try {
    return JSON.parse(Buffer.from(value, 'base64url').toString('utf8')) as TmProfileCookie;
  } catch {
    return null;
  }
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { BrowserContext, Page } from '@playwright/test';
import { holdNetwork, signInAs, tmIsServerPath, warmUp } from '@tellma/core-testing/e2e';

import { expect, test } from '../support/acme';
import { SHELL_TEXT, pinnedBar, sessionButton } from '../support/shell';

// The warm shell (spec 0027 §14, instant warm launch): a warm launch draws
// the whole chrome, the app bar with its tenant, bell and tray and the
// pinned bar with no skeleton, from the store, the cookie and the bundle,
// with every server request held; the sandbox draws its marker the same
// way; and no shell code waits on the server, so the document issues no
// server request before the shell has rendered.

/** A server request the document issued, and whether the shell had rendered at the call. */
interface ShellRequest {
  /** The request's path. */
  readonly path: string;
  /** Whether the app bar's session button and the pinned bar were in the page at the call. */
  readonly afterShell: boolean;
}

/**
 * Records, in every document of the context, each `fetch` and whether the
 * shell, the app bar's session button and the pinned bar, was in the page
 * at the call. The DOM is read in the call itself, so no clock comes between
 * the render and the request.
 *
 * @param context - The context.
 */
async function recordShellRequests(context: BrowserContext): Promise<void> {
  await context.addInitScript(() => {
    const issued: { path: string; afterShell: boolean }[] = [];
    (window as { __acmeShellRequests?: typeof issued }).__acmeShellRequests = issued;
    const fetch = window.fetch.bind(window);
    window.fetch = (input, init) => {
      const url = new URL(input instanceof Request ? input.url : String(input), location.href);
      issued.push({
        path: url.pathname,
        afterShell:
          document.querySelector('tm-app-bar .tm-app-bar__session') !== null &&
          document.querySelector('nav.tm-pinned-bar') !== null,
      });
      return fetch(input, init);
    };
  });
}

/**
 * The server requests {@link recordShellRequests} saw the current document issue.
 *
 * @param page - The page.
 * @returns Each one, in the order the app issued them.
 */
async function shellRequests(page: Page): Promise<readonly ShellRequest[]> {
  const issued = await page.evaluate(
    () => (window as { __acmeShellRequests?: ShellRequest[] }).__acmeShellRequests ?? [],
  );
  return issued.filter((request) => tmIsServerPath(request.path));
}

test.describe('the warm shell', () => {
  test.beforeEach(async ({ page, cast }) => {
    await recordShellRequests(page.context());
    await signInAs(page, cast.user);
    await warmUp(page, '/2');
    await warmUp(page, '/1');
  });

  test('a warm launch draws the app bar and the pinned bar with every server request held', async ({
    page,
    lang,
  }) => {
    const hold = await holdNetwork(page);
    await page.goto('/1', { waitUntil: 'commit' });
    await expect(sessionButton(page, lang)).toBeVisible();

    // The tenant's name, never its skeleton, and the bell's and the tray's 30 px buttons.
    await expect(page.locator('tm-app-bar .tm-app-bar__tenant')).toHaveCount(1);
    await expect(page.locator('tm-app-bar .tm-app-bar__tenant-skeleton')).toHaveCount(0);
    await expect(page.locator('tm-app-bar [data-tm-slot="bell"] > button')).toHaveCSS(
      'width',
      '30px',
    );
    await expect(page.locator('tm-app-bar [data-tm-slot="tray"] > button')).toHaveCSS(
      'width',
      '30px',
    );

    // The pinned bar, named, from the stored pins: never its skeletons.
    await expect(pinnedBar(page)).toHaveAccessibleName(SHELL_TEXT[lang].pinnedBar);
    await expect(pinnedBar(page).locator('.tm-pinned-bar__control').first()).toBeVisible();
    await expect(pinnedBar(page).locator('.tm-pinned-bar__skeletons')).toHaveCount(0);

    // The live tenant has no marker.
    await expect(page.locator('tm-app-bar .tm-app-bar__marker')).toHaveCount(0);
    await hold.release();
  });

  test('a warm launch of the sandbox draws its marker with every server request held', async ({
    page,
    lang,
  }) => {
    const hold = await holdNetwork(page);
    await page.goto('/2', { waitUntil: 'commit' });
    await expect(sessionButton(page, lang)).toBeVisible();
    await expect(page.locator('tm-app-bar .tm-app-bar__marker[data-tm-marker]')).toHaveCount(1);
    await expect(page.locator('tm-app-bar .tm-app-bar__marker')).toHaveAccessibleName(
      new RegExp(SHELL_TEXT[lang].sandbox),
    );
    await hold.release();
  });

  test('a warm launch issues no server request before the shell renders', async ({
    page,
    lang,
  }) => {
    await page.goto('/1', { waitUntil: 'commit' });
    await expect(sessionButton(page, lang)).toBeVisible();
    await expect(pinnedBar(page)).toBeVisible();

    // The launch revalidates from the server once the shell is up; every call came after it.
    await expect
      .poll(async () => (await shellRequests(page)).length, {
        message: 'the warm launch revalidated from the server',
      })
      .toBeGreaterThan(0);
    for (const request of await shellRequests(page)) {
      expect(request.afterShell, request.path).toBe(true);
    }
  });
});

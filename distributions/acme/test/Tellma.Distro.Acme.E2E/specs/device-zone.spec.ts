// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type CDPSession, type Locator, type Page } from '@playwright/test';
import { expectNoAxeViolations, signInAs, tmBootFacts, warmUp } from '@tellma/core-testing/e2e';

import { type AcmeLanguage, comeIntoViewUntil, expect, test } from '../support/acme';

// The device time zone (spec 0024 §7.8) over the real app and server: the
// browser's zone is moved with the DevTools protocol's time-zone override,
// and the tab is brought into view, as the user returning to it after a
// flight. A first run records the zone silently; a change asks once, after
// the user's hands are off the keyboard and pointer, and Change time zone
// saves the zone on the server, so every tab follows; with the option on, the
// next change applies by itself and says so in a toast. A zone the browser
// reports by its old name (Asia/Calcutta) is offered and saved by its
// current one (Asia/Kolkata).
//
// Each flow ends where it started: the user's zone is moved back to the
// display zone it found, through the same prompt or automatic update. The
// flows move one user's zone, so they run one after the other.

/** The prompt's and the toast's texts, as the app's string packs word them; an Arabic imperative takes either gender's form. */
const ZONE_TEXT: Readonly<
  Record<
    AcmeLanguage,
    {
      readonly title: string;
      readonly message: RegExp;
      readonly auto: string;
      readonly change: string;
      readonly toast: RegExp;
      readonly toastChange: string;
    }
  >
> = {
  en: {
    title: "Use your device's time zone?",
    message: /^Your device is now on .+ \(GMT.*\)\. Acme shows times in .+ \(GMT.*\)\.$/,
    auto: 'Change it automatically next time',
    change: 'Change time zone',
    toast: /^Times now show in .+ \(GMT.*\)\.$/,
    toastChange: 'Change',
  },
  ar: {
    title: 'استخدام المنطقة الزمنية لجهازك؟',
    message: /^أصبح جهازك الآن على .+ \(.+\)\. يعرض .+ الأوقات بتوقيت .+ \(.+\)\.$/,
    auto: 'التغيير تلقائيًا في المرة القادمة',
    change: 'تغيير المنطقة الزمنية',
    toast: /^تُعرض الأوقات الآن بتوقيت .+ \(.+\)\.$/,
    toastChange: 'تغيير',
  },
};

/** Zones the flows move the device to, far from any display zone the seed or an earlier run leaves: one is always a change. */
const DISTANT_ZONES = ['Asia/Tokyo', 'America/New_York'] as const;

/** How long a flow waits for the app's own state, such as a check that waits for the idle moment. */
const STATE_TIMEOUT_MS = 30_000;

/** A zone's offset from UTC now, in minutes, as the test machine's own clock tables give it. */
function offsetNow(zone: string): string {
  return (
    new Intl.DateTimeFormat('en-US', { timeZone: zone, timeZoneName: 'longOffset' })
      .formatToParts(Date.now())
      .find((part) => part.type === 'timeZoneName')?.value ?? zone
  );
}

/**
 * One document of the client store, by partition and name, whatever its
 * format version.
 *
 * @param page - A page of the origin.
 * @param partition - The partition, such as `device` or `u:<sub>/t1`.
 * @param name - The document's name, such as `zone`.
 * @returns The document's data, or `undefined`.
 */
function storedData<T>(page: Page, partition: string, name: string): Promise<T | undefined> {
  return page.evaluate(
    ({ partition, name }) =>
      new Promise<T | undefined>((resolve) => {
        const request = indexedDB.open('tm-app');
        request.onupgradeneeded = () => request.transaction?.abort();
        request.onerror = () => resolve(undefined);
        request.onsuccess = () => {
          const db = request.result;
          try {
            const all = db.transaction('docs', 'readonly').objectStore('docs').openCursor();
            let found: T | undefined;
            all.onsuccess = () => {
              const cursor = all.result;
              if (cursor === null) {
                db.close();
                resolve(found);
                return;
              }
              const [p, n] = cursor.key as [string, string];
              if (p === partition && n.replace(/@\d+$/, '') === name) {
                found = (cursor.value as { readonly data?: T } | undefined)?.data;
              }
              cursor.continue();
            };
            all.onerror = () => {
              db.close();
              resolve(undefined);
            };
          } catch {
            db.close();
            resolve(undefined);
          }
        };
      }),
    { partition, name },
  );
}

/** The device's zone document, as far as the flows read it. */
interface ZoneDoc {
  readonly users: Readonly<
    Record<string, { readonly auto: boolean; readonly seen: Readonly<Record<string, string>> }>
  >;
}

/** What one flow drives: the tab, its DevTools session, the user and the tenant. */
interface ZoneTab {
  readonly page: Page;
  readonly cdp: CDPSession;
  readonly sub: string;
}

/**
 * Moves the browser's zone in a tab, as the operating system's moves it.
 *
 * @param tab - The tab.
 * @param zone - The IANA zone.
 */
async function moveDevice(tab: ZoneTab, zone: string): Promise<void> {
  await tab.cdp.send('Emulation.setTimezoneOverride', { timezoneId: zone });
}

/**
 * The device zone tenant 1 last handled, as the device's zone document holds it.
 *
 * @param tab - The tab.
 * @returns The zone, or `undefined`.
 */
async function seenZone(tab: ZoneTab): Promise<string | undefined> {
  return (await storedData<ZoneDoc>(tab.page, 'device', 'zone'))?.users[tab.sub]?.seen['1'];
}

/**
 * The user's display zone in tenant 1: their preference, else the tenant's.
 *
 * @param tab - The tab.
 * @returns The zone.
 */
async function displayZone(tab: ZoneTab): Promise<string> {
  const partition = `u:${tab.sub}/t1`;
  const me = await storedData<{ readonly user: { readonly preferredTimeZone?: string } }>(
    tab.page,
    partition,
    'me',
  );
  const settings = await storedData<{ readonly timeZone: string }>(tab.page, partition, 'settings');
  return me?.user.preferredTimeZone ?? settings!.timeZone;
}

/**
 * Signs the user in on a fresh device and opens tenant 1 warm: the check's
 * first run records the device's zone and asks nothing. The device then
 * moves to the user's display zone, which the check records as matching.
 *
 * @param page - The page.
 * @param user - The user's email address.
 * @returns The tab, and the display zone the user had.
 */
async function openOnDevice(
  page: Page,
  user: string,
): Promise<{ readonly tab: ZoneTab; readonly display: string }> {
  const cdp = await page.context().newCDPSession(page);
  await signInAs(page, user);
  await warmUp(page, '/1');
  const { sub } = await tmBootFacts(page);
  const tab: ZoneTab = { page, cdp, sub: sub! };

  // --- The first run, silent: the device's zone is recorded, and nothing asks ---
  await expect
    .poll(() => seenZone(tab), {
      timeout: STATE_TIMEOUT_MS,
      message: 'the first run records the zone',
    })
    .not.toBeUndefined();
  await expect(page.getByRole('dialog')).toHaveCount(0);

  // --- The device stands in the display zone, which the check records as matching ---
  const display = await displayZone(tab);
  await moveDevice(tab, display);
  await page.evaluate(() => document.dispatchEvent(new Event('visibilitychange')));
  await expect.poll(() => seenZone(tab), { timeout: STATE_TIMEOUT_MS }).toBe(display);
  return { tab, display };
}

/**
 * The prompt.
 *
 * @param page - The page.
 * @param lang - The project's language.
 * @returns Its locator.
 */
function zonePrompt(page: Page, lang: AcmeLanguage): Locator {
  return page.getByRole('dialog', { name: ZONE_TEXT[lang].title });
}

test.describe('device time zone', () => {
  test.describe.configure({ mode: 'serial' });

  test('device-zone: a silent first run, a prompt after the idle gate, every tab following, the automatic toast', async ({
    page,
    context,
    lang,
    cast,
  }) => {
    const text = ZONE_TEXT[lang];
    const { tab, display } = await openOnDevice(page, cast.user);
    const away = DISTANT_ZONES.find((zone) => offsetNow(zone) !== offsetNow(display))!;

    // --- A second tab of the user, on the same device ---
    const second = await context.newPage();
    const secondTab: ZoneTab = {
      page: second,
      cdp: await context.newCDPSession(second),
      sub: tab.sub,
    };
    await moveDevice(secondTab, display);
    await second.goto('/1');
    await expect.poll(async () => (await tmBootFacts(second)).state).toBe('run');

    // --- The device moves; the prompt waits while the user keeps using the page ---
    await moveDevice(tab, away);
    await moveDevice(secondTab, away);
    // The user is already using the page when it comes into view, so the
    // check finds them busy rather than idle since the second tab opened.
    await page.bringToFront();
    await page.mouse.move(90, 100);
    await page.evaluate(() => document.dispatchEvent(new Event('visibilitychange')));
    for (let step = 0; step < 6; step++) {
      await page.mouse.move(100 + step * 10, 100);
      await page.waitForTimeout(250);
      await expect(zonePrompt(page, lang)).toHaveCount(0);
    }
    await comeIntoViewUntil(page, zonePrompt(page, lang));
    const prompt = zonePrompt(page, lang);
    await expect(prompt.getByText(text.message)).toBeVisible();
    await expectNoAxeViolations(page, '[role="dialog"]');

    // --- Change time zone, with the option ticked ---
    await prompt.getByRole('checkbox', { name: text.auto }).check();
    await prompt.getByRole('button', { name: text.change }).click();
    await expect(prompt).toBeHidden();
    await expect.poll(() => displayZone(tab), { timeout: STATE_TIMEOUT_MS }).toBe(away);
    expect(await seenZone(tab)).toBe(away);

    // --- Every tab follows, and none asks again: the change was handled on this device ---
    await expect.poll(() => displayZone(secondTab), { timeout: STATE_TIMEOUT_MS }).toBe(away);
    await second.bringToFront();
    await second.evaluate(() => document.dispatchEvent(new Event('visibilitychange')));
    // Past the idle moment the prompt would wait for: two seconds without input.
    await second.waitForTimeout(3_000);
    await expect(zonePrompt(second, lang)).toHaveCount(0);
    await second.close();

    // --- Back home: the option applies the change by itself, and the toast says so ---
    await page.bringToFront();
    await moveDevice(tab, display);
    const toast = page.getByText(text.toast);
    await comeIntoViewUntil(page, toast);
    await expect(zonePrompt(page, lang)).toHaveCount(0);
    await expect.poll(() => displayZone(tab), { timeout: STATE_TIMEOUT_MS }).toBe(display);
    await page.getByRole('button', { name: text.toastChange, exact: true }).click();
    await expect(page).toHaveURL(/\/1\/me\/region$/);
  });

  test('device-zone: an override to Asia/Kolkata, reported as Asia/Calcutta, offers and saves Asia/Kolkata', async ({
    page,
    lang,
    cast,
  }) => {
    const text = ZONE_TEXT[lang];
    const { tab, display } = await openOnDevice(page, cast.user);
    // A display zone already at India's offset would make the move invisible.
    test.skip(
      offsetNow(display) === offsetNow('Asia/Kolkata'),
      'The display zone already shows India Standard Time.',
    );

    // --- The browser reports the zone by its old name ---
    await moveDevice(tab, 'Asia/Calcutta');
    await comeIntoViewUntil(page, zonePrompt(page, lang));
    await zonePrompt(page, lang).getByRole('button', { name: text.change }).click();
    await expect(zonePrompt(page, lang)).toBeHidden();
    await expect.poll(() => displayZone(tab), { timeout: STATE_TIMEOUT_MS }).toBe('Asia/Kolkata');
    expect(await seenZone(tab)).toBe('Asia/Kolkata');

    // --- Back where it started, through the same prompt ---
    await moveDevice(tab, display);
    await comeIntoViewUntil(page, zonePrompt(page, lang));
    await zonePrompt(page, lang).getByRole('button', { name: text.change }).click();
    await expect.poll(() => displayZone(tab), { timeout: STATE_TIMEOUT_MS }).toBe(display);
  });
});

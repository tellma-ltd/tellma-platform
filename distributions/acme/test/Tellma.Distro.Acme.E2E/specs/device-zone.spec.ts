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
// reports by its old name (an override to Asia/Kolkata, which Chromium reports
// as Asia/Calcutta) is offered and saved by its current one.
//
// Each flow starts and ends with the user's time zone preference cleared, as
// My settings' reset clears it, so the display zone is the tenant's whatever
// an earlier run, finished or killed, left. The flows move one user's zone,
// so they run one after the other.

/** The prompt's and the toast's texts, as the app's string packs word them; an Arabic imperative takes either gender's form. */
const ZONE_TEXT: Readonly<
  Record<
    AcmeLanguage,
    {
      readonly title: string;
      /** The message, its groups the device zone's name and offset, then the display zone's. */
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
    message: /^Your device is now on (.+) \((.+)\)\. Acme shows times in (.+) \((.+)\)\.$/,
    auto: 'Change it automatically next time',
    change: 'Change time zone',
    toast: /^Times now show in .+ \(GMT.*\)\.$/,
    toastChange: 'Change',
  },
  ar: {
    title: 'استخدام المنطقة الزمنية لجهازك؟',
    message: /^أصبح جهازك الآن على (.+) \((.+)\)\. يعرض .+ الأوقات بتوقيت (.+) \((.+)\)\.$/,
    auto: 'التغيير تلقائيًا في المرة القادمة',
    change: 'تغيير المنطقة الزمنية',
    toast: /^تُعرض الأوقات الآن بتوقيت .+ \(.+\)\.$/,
    toastChange: 'تغيير',
  },
};

/** Zones the flows move the device to, far from any display zone the seed leaves: some two of them always differ from it and from each other. */
const DISTANT_ZONES = ['Asia/Tokyo', 'America/New_York', 'Europe/London'] as const;

/** How long a flow waits for the app's own state, such as a check that waits for the idle moment. */
const STATE_TIMEOUT_MS = 30_000;

/** The route of the user's locale preferences in tenant 1, as the app sends it. */
const LOCALE_ROUTE = '/1/api/web/users/me/locale';

/** A zone's offset from UTC now, as the test machine's own clock tables give it. */
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

/** What one flow drives: the tab, its DevTools session and the user. */
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
 * The user's display zone in tenant 1, as the store holds it: their
 * preference, else the tenant's.
 *
 * @param tab - The tab.
 * @returns The zone, and whether it is the user's own preference.
 */
async function displayZone(
  tab: ZoneTab,
): Promise<{ readonly zone: string; readonly preferred: boolean }> {
  const partition = `u:${tab.sub}/t1`;
  const me = await storedData<{ readonly user: { readonly preferredTimeZone?: string | null } }>(
    tab.page,
    partition,
    'me',
  );
  const settings = await storedData<{ readonly timeZone: string }>(tab.page, partition, 'settings');
  const preferred = me?.user.preferredTimeZone ?? null;
  return { zone: preferred ?? settings!.timeZone, preferred: preferred !== null };
}

/** The `Tellma-Client` header each page last sent, which a call the flows send themselves carries too. */
const clientHeaders = new WeakMap<Page, string>();

/**
 * Remembers the `Tellma-Client` header of the page's own calls.
 *
 * @param page - The page.
 */
function watchClientHeader(page: Page): void {
  page.on('request', (request) => {
    const header = request.headers()['tellma-client'];
    if (header !== undefined) {
      clientHeaders.set(page, header);
    }
  });
}

/**
 * Clears the user's time zone preference in tenant 1, as My settings' reset
 * does, through the page's own origin and session, so the display zone is
 * the tenant's again.
 *
 * @param page - A page of the origin that has called the API.
 */
async function clearZonePreference(page: Page): Promise<void> {
  const client = clientHeaders.get(page);
  expect(client, 'the page has called the API, so its client header is known').toBeDefined();
  const status = await page.evaluate(
    ({ route, client }) =>
      fetch(route, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'Tellma-Client': client },
        body: JSON.stringify({ reset: ['TimeZone'] }),
        credentials: 'same-origin',
      }).then((response) => response.status),
    { route: LOCALE_ROUTE, client: client! },
  );
  expect(status, 'the reset of the time zone preference is accepted').toBe(200);
}

/**
 * Signs the user in on a fresh device and opens tenant 1 warm, with the
 * user's time zone preference cleared: the check's first run records the
 * device's zone and asks nothing. The device then moves to the tenant's
 * zone, which the check records as matching.
 *
 * @param page - The page.
 * @param user - The user's email address.
 * @returns The tab, and the display zone, the tenant's.
 */
async function openOnDevice(
  page: Page,
  user: string,
): Promise<{ readonly tab: ZoneTab; readonly display: string }> {
  const cdp = await page.context().newCDPSession(page);
  watchClientHeader(page);
  await signInAs(page, user);
  await warmUp(page, '/1');
  const { sub } = await tmBootFacts(page);
  const tab: ZoneTab = { page, cdp, sub: sub! };

  // --- A clean start: no preference, whatever an earlier run left; the reload revalidates `me` ---
  if ((await displayZone(tab)).preferred) {
    await clearZonePreference(page);
    await page.reload();
    await expect
      .poll(async () => (await displayZone(tab)).preferred, { timeout: STATE_TIMEOUT_MS })
      .toBe(false);
  }

  // --- The first run, silent: the device's zone is recorded, and nothing asks ---
  await expect
    .poll(() => seenZone(tab), {
      timeout: STATE_TIMEOUT_MS,
      message: 'the first run records the zone',
    })
    .not.toBeUndefined();
  await expect(page.getByRole('dialog')).toHaveCount(0);

  // --- The device stands in the display zone, which the check records as matching ---
  const { zone: display } = await displayZone(tab);
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

/**
 * The open prompt's message, split into the two zones it names.
 *
 * @param page - The page.
 * @param lang - The project's language.
 * @returns Each zone's name and offset, as the page's own state names them.
 */
async function promptZones(
  page: Page,
  lang: AcmeLanguage,
): Promise<{ readonly device: string; readonly display: string }> {
  const message = zonePrompt(page, lang).getByText(ZONE_TEXT[lang].message);
  await expect(message).toBeVisible();
  const [, deviceName, deviceOffset, displayName, displayOffset] = ZONE_TEXT[lang].message.exec(
    (await message.textContent())!.trim(),
  )!;
  return {
    device: `${deviceName} (${deviceOffset})`,
    display: `${displayName} (${displayOffset})`,
  };
}

test.describe('device time zone', () => {
  test.describe.configure({ mode: 'serial' });

  // Whatever a flow left, finished or failed, the user's preference ends cleared.
  test.afterEach(async ({ page }) => {
    if (clientHeaders.has(page)) {
      await clearZonePreference(page);
    }
  });

  test('device-zone: a silent first run, a prompt after the idle gate, every tab following, the automatic toast', async ({
    page,
    context,
    lang,
    cast,
  }) => {
    const text = ZONE_TEXT[lang];
    const { tab, display } = await openOnDevice(page, cast.user);
    const [away, third] = DISTANT_ZONES.filter(
      (zone) => offsetNow(zone) !== offsetNow(display),
    ).filter(
      (zone, index, zones) =>
        zones.findIndex((other) => offsetNow(other) === offsetNow(zone)) === index,
    );
    expect(
      third,
      'two distant zones differ from the display zone and from each other',
    ).toBeDefined();

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
    await moveDevice(tab, away!);
    await moveDevice(secondTab, away!);
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
    const asked = await promptZones(page, lang);
    await expectNoAxeViolations(page, '[role="dialog"]');

    // --- Change time zone, the option left off ---
    await prompt.getByRole('button', { name: text.change }).click();
    await expect(prompt).toBeHidden();
    await expect
      .poll(async () => (await displayZone(tab)).zone, { timeout: STATE_TIMEOUT_MS })
      .toBe(away);
    expect(await seenZone(tab)).toBe(away);

    // --- The second tab asks nothing for the change this device handled ---
    await second.bringToFront();
    await second.evaluate(() => document.dispatchEvent(new Event('visibilitychange')));
    // Past the idle moment the prompt would wait for: two seconds without input.
    await second.waitForTimeout(3_000);
    await expect(zonePrompt(second, lang)).toHaveCount(0);

    // --- The second tab followed: its own state names the new zone as the one it shows ---
    // The device moves on; the second tab's prompt names the display zone from
    // that tab's own localization, which only the adopted change moved.
    await moveDevice(secondTab, third!);
    await comeIntoViewUntil(second, zonePrompt(second, lang));
    const followed = await promptZones(second, lang);
    expect(followed.display, "the second tab shows times in the first tab's new zone").toBe(
      asked.device,
    );

    // --- Change time zone there, with the option ticked ---
    await zonePrompt(second, lang).getByRole('checkbox', { name: text.auto }).check();
    await zonePrompt(second, lang).getByRole('button', { name: text.change }).click();
    await expect(zonePrompt(second, lang)).toBeHidden();
    await expect
      .poll(async () => (await displayZone(secondTab)).zone, { timeout: STATE_TIMEOUT_MS })
      .toBe(third);
    await second.close();

    // --- Back home: the option applies the change by itself, and the toast says so ---
    await page.bringToFront();
    await moveDevice(tab, display);
    const toast = page.getByText(text.toast);
    await comeIntoViewUntil(page, toast);
    await expect(zonePrompt(page, lang)).toHaveCount(0);
    await expect
      .poll(async () => (await displayZone(tab)).zone, { timeout: STATE_TIMEOUT_MS })
      .toBe(display);
    await page.getByRole('button', { name: text.toastChange, exact: true }).click();
    await expect(page).toHaveURL(/\/1\/me\/region$/);
  });

  test('device-zone: an override to Asia/Kolkata, which Chromium reports as Asia/Calcutta, offers and saves Asia/Kolkata', async ({
    page,
    lang,
    cast,
  }) => {
    const text = ZONE_TEXT[lang];
    const { tab, display } = await openOnDevice(page, cast.user);
    expect(
      offsetNow(display),
      "the tenant's zone is not at India's offset, so the move shows",
    ).not.toBe(offsetNow('Asia/Kolkata'));

    // --- The browser reports the zone by its old name ---
    await moveDevice(tab, 'Asia/Kolkata');
    expect(
      await page.evaluate(() => new Intl.DateTimeFormat().resolvedOptions().timeZone),
      'the browser reports the override by its old name; otherwise this flow no longer exercises the aliases',
    ).toBe('Asia/Calcutta');
    await comeIntoViewUntil(page, zonePrompt(page, lang));
    await zonePrompt(page, lang).getByRole('button', { name: text.change }).click();
    await expect(zonePrompt(page, lang)).toBeHidden();
    await expect
      .poll(async () => (await displayZone(tab)).zone, { timeout: STATE_TIMEOUT_MS })
      .toBe('Asia/Kolkata');
    expect(await seenZone(tab)).toBe('Asia/Kolkata');
  });
});

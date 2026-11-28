// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type APIResponse, type Locator, type Page, expect } from '@playwright/test';

import type { AcmeLanguage } from './acme';

// The tenant shell's facts the shell flows share: its texts in each
// project's language, its parts by their classes, the calls a flow sends to
// set the pin keeper's state through the page's own session, and the frame
// probe that tells which painted frame first showed an overlay.

/** The shell's texts the flows match, in a project's language. */
export interface ShellText {
  /** The session button's name: the user, then the tenant. */
  readonly account: RegExp;
  /** The session menu's name. */
  readonly menu: string;
  /** The session menu's My settings row. */
  readonly mySettings: string;
  /** The session menu's language row, which opens the language submenu. */
  readonly language: string;
  /** The session menu's About row, and the About dialog's title. */
  readonly about: string;
  /** The About dialog's Diagnostics tab. */
  readonly diagnostics: string;
  /** The session menu's keyboard shortcuts row, and that dialog's title. */
  readonly shortcuts: string;
  /** The session menu's Report a problem row, and the support dialog's title. */
  readonly reportProblem: string;
  /** The session menu's Sign out row. */
  readonly signOut: string;
  /** The session menu's dark theme row. */
  readonly dark: string;
  /** The About dialog's Reload. */
  readonly reload: string;
  /** The About dialog's Reload anyway. */
  readonly reloadAnyway: string;
  /** The pinned bar's landmark. */
  readonly pinnedBar: string;
  /** The pinned bar's More chip. */
  readonly more: string;
  /** The pin control's name on a page with no pin. */
  readonly pinThisPage: string;
  /** The pin control's name on a pinned page. */
  readonly editPin: string;
  /** The Manage pins button's name, and the dialog's title. */
  readonly managePins: string;
  /** The launcher's Manage pins command. */
  readonly managePinsCommand: string;
  /** The pin popover's title after a pin. */
  readonly pinned: string;
  /** The pin popover's Name field. */
  readonly name: string;
  /** The pin popover's Folder field. */
  readonly folder: string;
  /** The Folder field's New folder option. */
  readonly newFolder: string;
  /** The pin popover's Folder name field. */
  readonly folderName: string;
  /** The pin popover's Done. */
  readonly done: string;
  /** A revoked pin's message. */
  readonly noAccess: string;
  /** A revoked pin's Unpin. */
  readonly unpin: string;
  /** The Manage pins dialog's Save. */
  readonly save: string;
  /** The launcher's pinned band. */
  readonly launcherPinned: string;
  /** The Sales section's heading. */
  readonly sales: string;
  /** The Financials section's heading. */
  readonly financials: string;
  /** The credit-review page's title. */
  readonly creditReview: string;
  /** A word of the credit-review page's synonyms in the project's language. */
  readonly creditSynonym: string;
  /** The sales invoices' plural label, the invoice bucket's heading. */
  readonly salesInvoices: string;
  /** The sandbox marker's name. */
  readonly sandbox: string;
  /** The installed mode's Back. */
  readonly back: string;
  /** The installed mode's Forward. */
  readonly forward: string;
  /** The word in the project's language for a customer, one of Customers' synonyms. */
  readonly clients: string;
  /**
   * The tenant picker's Connect link's name for a tenant.
   *
   * @param name - The tenant's name.
   * @returns The link's name.
   */
  readonly connectTo: (name: string) => string;
}

/** The shell's texts in each language, as the app's string packs word them. */
export const SHELL_TEXT: Readonly<Record<AcmeLanguage, ShellText>> = {
  en: {
    account: /^Account: .+, .+$/,
    menu: 'Account',
    mySettings: 'My settings',
    language: 'Language',
    about: 'About',
    diagnostics: 'Diagnostics',
    shortcuts: 'Keyboard shortcuts',
    reportProblem: 'Report a problem',
    signOut: 'Sign out',
    dark: 'Dark',
    reload: 'Reload',
    reloadAnyway: 'Reload anyway',
    pinnedBar: 'Pinned pages',
    more: 'More',
    pinThisPage: 'Pin this page',
    editPin: 'Edit pin',
    managePins: 'Manage pins',
    managePinsCommand: 'Manage pins…',
    pinned: 'Pinned',
    name: 'Name',
    folder: 'Folder',
    newFolder: 'New folder…',
    folderName: 'Folder name',
    done: 'Done',
    noAccess: 'You no longer have access to this page.',
    unpin: 'Unpin',
    save: 'Save',
    launcherPinned: 'Pinned',
    sales: 'Sales',
    financials: 'Financials',
    creditReview: 'Credit review',
    creditSynonym: 'exposure',
    salesInvoices: 'Sales invoices',
    sandbox: 'Sandbox',
    back: 'Back',
    forward: 'Forward',
    clients: 'clients',
    connectTo: (name) => `Connect to ${name}`,
  },
  ar: {
    account: /^الحساب: .+، .+$/,
    menu: 'الحساب',
    mySettings: 'إعداداتي',
    language: 'اللغة',
    about: 'حول',
    diagnostics: 'التشخيص',
    shortcuts: 'اختصارات لوحة المفاتيح',
    reportProblem: 'الإبلاغ عن مشكلة',
    signOut: 'تسجيل الخروج',
    dark: 'داكن',
    reload: 'إعادة التحميل',
    reloadAnyway: 'إعادة التحميل على أي حال',
    pinnedBar: 'الصفحات المثبّتة',
    more: 'المزيد',
    pinThisPage: 'تثبيت هذه الصفحة',
    editPin: 'تعديل التثبيت',
    managePins: 'إدارة الصفحات المثبّتة',
    managePinsCommand: 'إدارة الصفحات المثبّتة…',
    pinned: 'تم التثبيت',
    name: 'الاسم',
    folder: 'المجلد',
    newFolder: 'مجلد جديد…',
    folderName: 'اسم المجلد',
    done: 'تم',
    noAccess: 'لم يعد لديك وصول إلى هذه الصفحة.',
    unpin: 'إلغاء التثبيت',
    save: 'حفظ',
    launcherPinned: 'المثبّتة',
    sales: 'المبيعات',
    financials: 'المالية',
    creditReview: 'مراجعة الائتمان',
    creditSynonym: 'التعرض',
    salesInvoices: 'فواتير المبيعات',
    sandbox: 'بيئة تجريبية',
    back: 'رجوع',
    forward: 'تقدّم',
    clients: 'زبائن',
    connectTo: (name) => `دخول إلى ${name}`,
  },
};

// --- The shell's parts ---

/**
 * The session button.
 *
 * @param page - The page.
 * @param lang - The project's language.
 * @returns Its locator.
 */
export function sessionButton(page: Page, lang: AcmeLanguage): Locator {
  return page.getByRole('button', { name: SHELL_TEXT[lang].account });
}

/**
 * Opens the session menu from the session button.
 *
 * @param page - The page.
 * @param lang - The project's language.
 * @returns The open menu.
 */
export async function openSessionMenu(page: Page, lang: AcmeLanguage): Promise<Locator> {
  await sessionButton(page, lang).click();
  const menu = page.getByRole('menu', { name: SHELL_TEXT[lang].menu });
  await expect(menu).toBeVisible();
  return menu;
}

/**
 * Opens the About dialog from the session menu.
 *
 * @param page - The page.
 * @param lang - The project's language.
 * @returns The open dialog.
 */
export async function openAbout(page: Page, lang: AcmeLanguage): Promise<Locator> {
  const menu = await openSessionMenu(page, lang);
  await menu.getByRole('menuitem', { name: new RegExp(`^${SHELL_TEXT[lang].about}`) }).click();
  const about = page.getByRole('dialog', { name: SHELL_TEXT[lang].about });
  await expect(about.locator('tm-about-dialog dl')).toBeVisible();
  return about;
}

/**
 * The app bar's search field, the launcher's combobox from `lg` up.
 *
 * @param page - The page.
 * @returns Its locator.
 */
export function launcherField(page: Page): Locator {
  return page.locator('tm-app-bar .tm-app-bar__field input');
}

/**
 * The launcher's active option.
 *
 * @param page - The page.
 * @returns Its locator.
 */
export function activeOption(page: Page): Locator {
  return page.getByRole('listbox').locator('[role="option"][aria-selected="true"]');
}

/**
 * The pinned bar.
 *
 * @param page - The page.
 * @returns Its locator.
 */
export function pinnedBar(page: Page): Locator {
  return page.locator('nav.tm-pinned-bar');
}

/**
 * The pinned bar's shown chips, More included, in the bar's order; the
 * measuring row's copies are not among them.
 *
 * @param page - The page.
 * @returns Their locator.
 */
export function pinChips(page: Page): Locator {
  return pinnedBar(page).locator('.tm-pinned-bar__list .tm-pinned-bar__chip');
}

// --- The pin keeper's state, set through the page's own session ---

/** A pin as `nav.pinned` stores it. */
export interface StoredPin {
  /** The pin's id: 8 lower-case base32 characters. */
  readonly id: string;
  /** The tenant-relative URL, without a leading slash. */
  readonly url: string;
  /** The page's title when it was pinned. */
  readonly title: string;
  /** The user's own name for the pin. */
  readonly label?: string;
}

/** A folder of pins as `nav.pinned` stores it. */
export interface StoredPinFolder {
  /** The folder's id. */
  readonly id: string;
  /** The folder's name. */
  readonly folder: string;
  /** Its pins, in order. */
  readonly items: readonly StoredPin[];
}

/** The client name and version the flows' own calls carry, as the web surface's forgery control asks. */
const CLIENT_HEADER = 'web/e2e';

/**
 * Posts one call of the web surface in tenant 1 as the context's signed-in
 * user, through the context's cookies.
 *
 * @param page - A page of the signed-in context.
 * @param route - The route under `/1/api/web/`.
 * @param body - The call's body.
 * @returns The response.
 */
async function post(page: Page, route: string, body: unknown): Promise<APIResponse> {
  const response = await page.request.post(`/1/api/web/${route}`, {
    headers: { 'Content-Type': 'application/json', 'Tellma-Client': CLIENT_HEADER },
    data: JSON.stringify(body),
  });
  expect(response.ok(), `${route} is accepted: ${response.status()} ${await response.text()}`).toBe(
    true,
  );
  return response;
}

/**
 * Clears the user's two pin preferences in tenant 1, so the pinned bar is
 * empty and shown, as a user who never pinned finds it.
 *
 * @param page - A page of the signed-in context.
 */
export async function resetPins(page: Page): Promise<void> {
  await post(page, 'users/me/preferences/delete', { keys: ['nav.pinned', 'nav.pinnedBarVisible'] });
}

/**
 * Stores the user's pins in tenant 1.
 *
 * @param page - A page of the signed-in context.
 * @param items - The top-level items, in the bar's order.
 */
export async function storePins(
  page: Page,
  items: readonly (StoredPin | StoredPinFolder)[],
): Promise<void> {
  await post(page, 'users/me/preferences/set', {
    items: [{ key: 'nav.pinned', value: JSON.stringify({ v: 1, items }) }],
  });
}

/**
 * Stores whether the user's pinned bar shows in tenant 1.
 *
 * @param page - A page of the signed-in context.
 * @param visible - Whether it shows.
 */
export async function storePinnedBarVisible(page: Page, visible: boolean): Promise<void> {
  await post(page, 'users/me/preferences/set', {
    items: [{ key: 'nav.pinnedBarVisible', value: JSON.stringify(visible) }],
  });
}

/**
 * Stores the user's language in tenant 1.
 *
 * @param page - A page of the signed-in context.
 * @param lang - The language.
 */
export async function storeLanguage(page: Page, lang: AcmeLanguage): Promise<void> {
  await post(page, 'users/me/locale', { language: lang });
}

/**
 * Serves the context's pages a set of pins in tenant 1 in every sync's
 * answer, as though the user had stored them, without writing the user's
 * stored preferences, and holds every write of them unanswered: a flow that
 * signs in as a principal other flows share sees the pins, and leaves the
 * server as it found it.
 *
 * @param page - A page of the context.
 * @param items - The top-level items, in the bar's order.
 */
export async function servePins(
  page: Page,
  items: readonly (StoredPin | StoredPinFolder)[],
): Promise<void> {
  const value = JSON.stringify({ v: 1, items });
  await page.context().route(
    (url) => url.pathname === '/1/api/web/client/sync',
    async (route) => {
      const response = await route.fetch();
      const body = (await response.json()) as {
        me?: { preferences: Readonly<Record<string, string>> };
      };
      const json =
        body.me === undefined
          ? body
          : {
              ...body,
              me: { ...body.me, preferences: { ...body.me.preferences, 'nav.pinned': value } },
            };
      await route.fulfill({ response, json });
    },
  );
  await holdPreferenceWrites(page);
}

/**
 * Holds every write of the user's preferences the context's pages send,
 * unanswered, so a flow that pins or hides the bar changes the device alone.
 *
 * @param page - A page of the context.
 */
export async function holdPreferenceWrites(page: Page): Promise<void> {
  await page.context().route(
    (url) => url.pathname.startsWith('/1/api/web/users/me/preferences/'),
    () => undefined,
  );
}

/**
 * Ends a flow whose preference writes were held: closes the page while the
 * routes still hold the writes it flushes as it goes, then drops the routes
 * still running, such as a sync's answer rewritten in flight.
 *
 * @param page - The flow's page.
 */
export async function closeHeld(page: Page): Promise<void> {
  await page.close();
  await page.context().unrouteAll({ behavior: 'ignoreErrors' });
}

/**
 * Waits until the launcher's panel, whose chunk the shell prefetches at idle,
 * has landed, closed, at the end of the body: the state in which an open
 * shows in its key's frame.
 *
 * @param page - The page.
 */
export async function launcherLanded(page: Page): Promise<void> {
  await expect(page.locator('body > tm-launcher-panel')).toBeAttached();
}

// --- The frame probe ---

/** The frame an element first showed in, recorded by {@link watchFrame}. */
interface ShellFrameWindow {
  /** The frame, counted from 1 for the first frame painted after the input; unset before it showed. */
  __acmeShownFrame?: number;
}

/** The frames after the input the probe watches before it gives up. */
const WATCHED_FRAMES = 10;

/**
 * Records the first frame painted after the next input of a kind in which an
 * element matching a selector is visible, and, when a text is given, reads
 * that text. Each frame's animation frame callback runs before that frame
 * paints; a message it posts runs after the paint, before any later frame,
 * so the DOM it reads is the one that frame painted.
 *
 * @param page - The page.
 * @param input - The input that starts the count: a click, a key press, or a field's text changing.
 * @param selector - The element the input shows.
 * @param text - The text the element shows, ignoring case; any when omitted.
 */
export async function watchFrame(
  page: Page,
  input: 'click' | 'keydown' | 'input',
  selector: string,
  text?: string,
): Promise<void> {
  await page.evaluate(
    ({ input, selector, text, watched }) => {
      const probe = window as ShellFrameWindow;
      delete probe.__acmeShownFrame;
      let frame = 0;
      const shows = (): boolean =>
        [...document.querySelectorAll(selector)].some(
          (element) =>
            element.checkVisibility() &&
            (text === null || element.textContent?.trim().toLowerCase() === text.toLowerCase()),
        );
      const next = (): void => {
        requestAnimationFrame(() => {
          frame++;
          const channel = new MessageChannel();
          channel.port1.onmessage = () => {
            if (shows()) {
              probe.__acmeShownFrame = frame;
            } else if (frame < watched) {
              next();
            } else {
              probe.__acmeShownFrame = Number.POSITIVE_INFINITY;
            }
          };
          channel.port2.postMessage(null);
        });
      };
      document.addEventListener(input, next, { capture: true, once: true });
    },
    { input, selector, text: text ?? null, watched: WATCHED_FRAMES },
  );
}

/**
 * The frame {@link watchFrame} recorded, once it did, noted in the report.
 *
 * @param page - The page.
 * @param what - What showed, for the report.
 * @returns The frame, counted from 1.
 */
export async function shownFrame(page: Page, what: string): Promise<number> {
  const frame = (): Promise<number | undefined> =>
    page.evaluate(() => (window as ShellFrameWindow).__acmeShownFrame);
  await expect.poll(frame).not.toBeUndefined();
  const shown = (await frame())!;
  expect.soft(shown, `${what} showed within the watched frames`).not.toBe(Infinity);
  return shown;
}

// --- Boxes ---

/** A box as the layout flows compare it, rounded to the pixel. */
export interface Box {
  /** Its inline start. */
  readonly x: number;
  /** Its block start. */
  readonly y: number;
  /** Its width. */
  readonly width: number;
  /** Its height. */
  readonly height: number;
}

/**
 * The boxes of every element a selector matches, rounded to the pixel, keyed
 * by each element's place among the matches and its tag: a state class that
 * comes or goes leaves the key as it was.
 *
 * @param page - The page.
 * @param selector - The elements.
 * @returns Their boxes.
 */
export function boxes(page: Page, selector: string): Promise<Readonly<Record<string, Box>>> {
  return page.evaluate((selector) => {
    const found: Record<string, Box> = {};
    document.querySelectorAll(selector).forEach((element, index) => {
      const box = element.getBoundingClientRect();
      found[`${index} ${element.localName}`] = {
        x: Math.round(box.x),
        y: Math.round(box.y),
        width: Math.round(box.width),
        height: Math.round(box.height),
      };
    });
    return found;
  }, selector);
}

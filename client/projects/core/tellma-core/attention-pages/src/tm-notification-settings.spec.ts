// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { TmApiError, type TmLaunchDocs, TmLocalization, TmPageActions, TmPageContexts, TmPreferences } from '@tellma/core';
import { tmNotificationToastsKey } from '@tellma/core/attention';
import { TmNotificationSettingsHarness } from '@tellma/core-testing';
import { TmErrorDialogHarness } from '@tellma/core-ui-testing';
import axe from 'axe-core';
import { page } from 'vitest/browser';

import { TM_T17_DOCS } from '../../src/routes/tm-app.testing.util';
import {
  type TmAttentionRig,
  tmAttentionRig,
  TM_TEST_NOTIFICATION_TYPES,
  tmSettle,
  tmSettleUntil,
} from '../../src/attention/tm-attention.testing.util';
import type { TmNotificationPreference, TmNotificationSettingsUser } from './tm-notification-settings-model';
import { TM_NOTIFICATION_SETTINGS_LOAD, TmNotificationSettings, type TmNotificationSettingsChunk } from './tm-notification-settings';
import { TmNotificationSettingsPage } from './tm-notification-settings-page';

// The Notifications section of My settings in a whole test app entered into
// tenant 17: the body's chunk behind the shell, the cards, the cells and
// their defaults, the whole-set save after the wait with one save in flight,
// a change sent when the section leaves, the save status and Try again, a
// 422's reload over the shown cards, the read-only, frozen and offline rules,
// the pop-up switch of the signed-in user and of another, the test delivery
// results and failures, the stacked layout below md, the page context and
// the accessibility rules in both themes and both directions.

/** The rig's types plus a channel-only one, which never shows in the app. */
const TYPES = [
  ...TM_TEST_NOTIFICATION_TYPES,
  { key: 'gl.center.digest', category: 'centers', mutable: true, severity: 'Info', toast: false, inbox: false },
] as const;

/** The accessibility rules the checks run. */
const WCAG = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'];

/** Longer than the 600 ms between the last change and the save. */
const PAST_WAIT_MS = 750;

/** The section, for the user a test picks. */
@Component({
  imports: [TmNotificationSettings],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<tm-notification-settings [user]="user()" />`,
})
class Host {
  readonly user = signal<TmNotificationSettingsUser>('me');
}

/** What a test drives. */
interface Setup {
  readonly rig: TmAttentionRig;
  readonly fixture: ComponentFixture<Host>;
  readonly section: TmNotificationSettingsHarness;
}

/** Waits real time, then lets the app settle. */
async function wait(ms: number): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, ms));
  await tmSettle();
}

/** A promise the test settles. */
function deferred<T>(): { readonly promise: Promise<T>; readonly resolve: (value: T) => void; readonly reject: (error: unknown) => void } {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((done, fail) => ((resolve = done), (reject = fail)));
  return { promise, resolve, reject };
}

/** A 422 from a save: a type gone after a deployment. */
const gone = (): TmApiError => new TmApiError({ kind: 'validation', status: 422, code: 'validation', message: 'The type is gone.' });

/** The switch input of a type's cell on a channel. */
function switchOf(fixture: ComponentFixture<unknown>, type: string, channel: string): HTMLInputElement {
  return (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(`tr[data-type="${type}"] td[data-channel="${channel}"] input[role="switch"]`)!;
}

/** The section's save events' properties. */
function saves(rig: TmAttentionRig): unknown[] {
  return rig.telemetry
    .recent()
    .filter((item) => item.name === 'tm.attention.settings-saved')
    .map((item) => item.props);
}

/**
 * Enters tenant 17 and renders the section.
 *
 * @param options - The launch documents, the stored rows and the user.
 * @returns What the test drives.
 */
async function setUp(
  options: { readonly docs?: Partial<TmLaunchDocs>; readonly rows?: readonly TmNotificationPreference[]; readonly user?: TmNotificationSettingsUser } = {},
): Promise<Setup> {
  const rig = await tmAttentionRig(options.docs ?? {}, { notificationTypes: TYPES });
  rig.api.respond('notification-preferences/get', options.rows ?? []);
  rig.api.respond('users/notification-preferences/get', options.rows ?? []);
  rig.api.respond('users/preferences/get', { preferences: { 'notifications.toasts': 'false' } });
  const fixture = TestBed.createComponent(Host);
  fixture.componentInstance.user.set(options.user ?? 'me');
  // The body's chunk loads after the shell renders, then reads the set.
  await tmSettleUntil(() => (fixture.nativeElement as HTMLElement).querySelector('.tm-notification-settings__body .tm-notification-settings__card') !== null);
  const section = await TestbedHarnessEnvironment.harnessForFixture(fixture, TmNotificationSettingsHarness);
  return { rig, fixture, section };
}

/** The bodies of the calls to a route. */
function bodies(rig: TmAttentionRig, route: string): unknown[] {
  return rig.api
    .calls()
    .filter((call) => call.route === route)
    .map((call) => call.body);
}

/** The tenant's launch facts in a given state. */
function tenantIn(state: 'ReadOnly' | 'Frozen'): Partial<TmLaunchDocs> {
  return { tenant: { ...TM_T17_DOCS.tenant, state } };
}

describe('tm-notification-settings', () => {
  afterEach(() => {
    document.documentElement.removeAttribute('data-theme');
    TestBed.resetTestingModule();
  });

  it("draws the section's skeleton while the body's chunk loads, then the body in its place", async () => {
    const chunk = deferred<TmNotificationSettingsChunk>();
    const rig = await tmAttentionRig({}, { notificationTypes: TYPES }, [{ provide: TM_NOTIFICATION_SETTINGS_LOAD, useValue: () => chunk.promise }]);
    rig.api.respond('notification-preferences/get', []);
    const fixture = TestBed.createComponent(Host);
    await tmSettle();
    const host = fixture.nativeElement as HTMLElement;
    const shell = host.querySelector('tm-notification-settings')!;
    expect(shell.getAttribute('aria-busy')).toBe('true');
    expect(host.querySelectorAll('.tm-notification-settings__placeholder tm-card')).toHaveLength(2);
    expect(bodies(rig, 'notification-preferences/get')).toEqual([]);
    chunk.resolve(await import('./tm-notification-settings-body'));
    await tmSettleUntil(() => host.querySelector('.tm-notification-settings__body .tm-notification-settings__card') !== null);
    expect(host.querySelector('.tm-notification-settings__placeholder')).toBeNull();
    expect(shell.getAttribute('aria-busy')).toBeNull();
  });

  it("reports a body chunk that fails to load", async () => {
    const rig = await tmAttentionRig({}, { notificationTypes: TYPES }, [
      { provide: TM_NOTIFICATION_SETTINGS_LOAD, useValue: () => Promise.reject(new Error('The chunk is gone.')) },
    ]);
    TestBed.createComponent(Host);
    await tmSettleUntil(() => rig.telemetry.recent().some((item) => item.kind === 'error'));
    expect(bodies(rig, 'notification-preferences/get')).toEqual([]);
  });

  it("lays out one card per category, a type's cells at their defaults or stored values, a locked inbox and a channel-only dash", async () => {
    const { section } = await setUp({ rows: [{ type: 'gl.center.flagged', channel: 'email', enabled: true }] });
    expect(await section.getCategories()).toEqual(['Background jobs', 'Sharing', 'Centers']);
    expect(await section.getCell('core.job.failed', 'inbox')).toEqual({
      kind: 'locked',
      checked: true,
      disabled: true,
      name: 'A job failed, In the app',
      description: 'Always on',
    });
    expect(await section.getCell('core.job.failed', 'email')).toEqual({
      kind: 'switch',
      checked: false,
      disabled: false,
      name: 'A job failed, Email',
      description: null,
    });
    expect((await section.getCell('gl.center.shared', 'inbox')).checked).toBe(true);
    expect((await section.getCell('gl.center.flagged', 'email')).checked).toBe(true);
    const dash = await section.getCell('gl.center.digest', 'inbox');
    expect(dash.kind).toBe('none');
    expect(dash.name).toContain("Doesn't show in the app");
    const popups = await section.getPopupSwitch();
    expect(await popups.isChecked()).toBe(true);
    expect(await popups.getLabelText()).toBe('Show a pop-up when a notification or an assignment arrives');
    expect(await section.getStatus()).toBe('');
  });

  it('saves the whole set once, 600 ms after the last change, omitting the cells at their defaults', async () => {
    const { rig, section } = await setUp({ rows: [{ type: 'gl.center.flagged', channel: 'email', enabled: true }] });
    rig.api.respond('notification-preferences/save', (call: { readonly body: unknown }) => call.body);
    await section.toggleCell('core.job.failed', 'email');
    await section.toggleCell('gl.center.flagged', 'email');
    await wait(300);
    // The change applies in its frame; the save waits.
    expect((await section.getCell('core.job.failed', 'email')).checked).toBe(true);
    expect(bodies(rig, 'notification-preferences/save')).toEqual([]);
    expect(await section.getStatus()).toBe('Saving…');
    await wait(PAST_WAIT_MS);
    expect(bodies(rig, 'notification-preferences/save')).toEqual([[{ type: 'core.job.failed', channel: 'email', enabled: true }]]);
    expect(await section.getStatus()).toBe('All changes saved');
    expect(saves(rig)).toEqual([{ changes: '2-5', outcome: 'ok' }]);
  });

  it('keeps one save in flight, and sends the latest state after it', async () => {
    const { rig, section } = await setUp();
    const first = deferred<unknown>();
    let count = 0;
    rig.api.respond('notification-preferences/save', (call: { readonly body: unknown }) => (++count === 1 ? first.promise : call.body));
    await section.toggleCell('core.job.failed', 'email');
    await wait(PAST_WAIT_MS);
    expect(bodies(rig, 'notification-preferences/save')).toHaveLength(1);
    await section.toggleCell('gl.center.shared', 'inbox');
    await wait(PAST_WAIT_MS);
    // The second save waits for the first.
    expect(bodies(rig, 'notification-preferences/save')).toHaveLength(1);
    expect(await section.getStatus()).toBe('Saving…');
    first.resolve([]);
    await tmSettleUntil(() => bodies(rig, 'notification-preferences/save').length === 2);
    expect(bodies(rig, 'notification-preferences/save')[1]).toEqual([
      { type: 'core.job.failed', channel: 'email', enabled: true },
      { type: 'gl.center.shared', channel: 'inbox', enabled: false },
    ]);
    await tmSettle();
    expect(await section.getStatus()).toBe('All changes saved');
  });

  it("says a save failed with Try again, keeps the user's values, and saves them again on Try again", async () => {
    const { rig, section } = await setUp();
    rig.api.respond('notification-preferences/save', () => {
      throw new TmApiError({ kind: 'server', status: 500 });
    });
    await section.toggleCell('core.job.failed', 'email');
    await wait(PAST_WAIT_MS);
    expect(await section.getStatus()).toBe("Couldn't save your changes");
    expect(await section.canRetry()).toBe(true);
    expect((await section.getCell('core.job.failed', 'email')).checked).toBe(true);
    expect(saves(rig)).toEqual([{ changes: '1', outcome: 'failed' }]);
    rig.api.respond('notification-preferences/save', (call: { readonly body: unknown }) => call.body);
    await section.retry();
    await tmSettleUntil(() => bodies(rig, 'notification-preferences/save').length === 2);
    await tmSettle();
    expect(await section.getStatus()).toBe('All changes saved');
    expect(await section.canRetry()).toBe(false);
  });

  it('reloads the set and shows the save error dialog on a 422', async () => {
    const { rig, fixture, section } = await setUp();
    rig.api.respond('notification-preferences/save', () => {
      throw gone();
    });
    await section.toggleCell('core.job.failed', 'email');
    rig.api.respond('notification-preferences/get', [{ type: 'gl.center.flagged', channel: 'email', enabled: true }]);
    await wait(PAST_WAIT_MS);
    await tmSettleUntil(() => document.querySelector('tm-error-dialog-content') !== null);
    expect(bodies(rig, 'notification-preferences/get')).toHaveLength(2);
    // The reloaded set replaces the user's change.
    expect((await section.getCell('core.job.failed', 'email')).checked).toBe(false);
    expect((await section.getCell('gl.center.flagged', 'email')).checked).toBe(true);
    expect(await section.getStatus()).toBe('');
    const dialog = await TestbedHarnessEnvironment.documentRootLoader(fixture).getHarness(TmErrorDialogHarness);
    expect(await dialog.getTitle()).not.toBe('');
    expect(saves(rig)).toEqual([{ changes: '1', outcome: 'failed' }]);
  });

  it('keeps the cards and the focused switch through a 422 reload, read-only meanwhile, and returns focus to it after the dialog', async () => {
    const { rig, fixture, section } = await setUp();
    rig.api.respond('notification-preferences/save', () => {
      throw gone();
    });
    await section.toggleCell('core.job.failed', 'email');
    const input = switchOf(fixture, 'core.job.failed', 'email');
    input.focus();
    const reload = deferred<readonly TmNotificationPreference[]>();
    rig.api.respond('notification-preferences/get', () => reload.promise);
    await wait(PAST_WAIT_MS);
    expect(bodies(rig, 'notification-preferences/get')).toHaveLength(2);

    // The reload holds: the cards stay, with no skeleton, and the same switch keeps its focus, read-only.
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('tm-skeleton')).toBeNull();
    expect(host.querySelector('tm-notification-settings')!.getAttribute('aria-busy')).toBe('true');
    expect(switchOf(fixture, 'core.job.failed', 'email')).toBe(input);
    expect(input.getAttribute('aria-readonly')).toBe('true');
    expect(document.activeElement).toBe(input);
    input.click();
    await tmSettle();
    expect(input.checked).toBe(true);

    // The read replaces the values in place; the dialog opens over them and gives the focus back on close.
    reload.resolve([]);
    await tmSettleUntil(() => document.querySelector('tm-error-dialog-content') !== null);
    expect(switchOf(fixture, 'core.job.failed', 'email')).toBe(input);
    expect(input.checked).toBe(false);
    expect(input.getAttribute('aria-readonly')).toBeNull();
    const dialog = await TestbedHarnessEnvironment.documentRootLoader(fixture).getHarness(TmErrorDialogHarness);
    await dialog.pressEscape();
    await tmSettleUntil(() => document.querySelector('tm-error-dialog-content') === null);
    expect(document.activeElement).toBe(input);
    expect(bodies(rig, 'notification-preferences/save')).toHaveLength(1);
  });

  it('drops a change made while a save is in flight when a 422 answers it, along with the reload', async () => {
    const { rig, section } = await setUp();
    const answer = deferred<unknown>();
    rig.api.respond('notification-preferences/save', () => answer.promise);
    await section.toggleCell('core.job.failed', 'email');
    await wait(PAST_WAIT_MS);
    expect(bodies(rig, 'notification-preferences/save')).toHaveLength(1);
    await section.toggleCell('gl.center.shared', 'inbox');
    expect((await section.getCell('gl.center.shared', 'inbox')).checked).toBe(false);
    answer.reject(gone());
    await tmSettleUntil(() => document.querySelector('tm-error-dialog-content') !== null);
    await wait(PAST_WAIT_MS);
    // The reloaded set shows, and nothing more is sent for the set it replaced.
    expect(bodies(rig, 'notification-preferences/save')).toHaveLength(1);
    expect((await section.getCell('gl.center.shared', 'inbox')).checked).toBe(true);
    expect((await section.getCell('core.job.failed', 'email')).checked).toBe(false);
    expect(await section.getStatus()).toBe('');
  });

  it('sends a change still waiting out its delay when the section leaves', async () => {
    const { rig, fixture, section } = await setUp();
    rig.api.respond('notification-preferences/save', (call: { readonly body: unknown }) => call.body);
    await section.toggleCell('core.job.failed', 'email');
    expect(bodies(rig, 'notification-preferences/save')).toEqual([]);
    fixture.destroy();
    await tmSettle();
    expect(bodies(rig, 'notification-preferences/save')).toEqual([[{ type: 'core.job.failed', channel: 'email', enabled: true }]]);
  });

  it("sends the signed-in user's change still waiting when the section turns to another user", async () => {
    const { rig, fixture, section } = await setUp();
    rig.api.respond('notification-preferences/save', (call: { readonly body: unknown }) => call.body);
    await section.toggleCell('core.job.failed', 'email');
    fixture.componentInstance.user.set(42);
    await tmSettleUntil(() => bodies(rig, 'users/notification-preferences/get').length === 1);
    expect(bodies(rig, 'notification-preferences/save')).toEqual([[{ type: 'core.job.failed', channel: 'email', enabled: true }]]);
    expect(bodies(rig, 'users/notification-preferences/save')).toEqual([]);
  });

  it('shows a failed read with its error state', async () => {
    const rig = await tmAttentionRig({}, { notificationTypes: TYPES });
    rig.api.respond('notification-preferences/get', () => {
      throw new TmApiError({ kind: 'server', status: 500 });
    });
    const fixture = TestBed.createComponent(Host);
    await tmSettleUntil(() => (fixture.nativeElement as HTMLElement).querySelector('tm-error-state') !== null);
    const section = await TestbedHarnessEnvironment.harnessForFixture(fixture, TmNotificationSettingsHarness);
    expect(await section.hasLoadError()).toBe(true);
  });

  it('saves the switches of a read-only tenant, and disables its test with the reason', async () => {
    const { rig, section } = await setUp({ docs: tenantIn('ReadOnly') });
    rig.api.respond('notification-preferences/save', (call: { readonly body: unknown }) => call.body);
    expect(await section.getLockReason()).toBeNull();
    await section.toggleCell('core.job.failed', 'email');
    await wait(PAST_WAIT_MS);
    expect(bodies(rig, 'notification-preferences/save')).toHaveLength(1);
    const [email] = await section.getTestRows();
    expect(email).toMatchObject({ channel: 'Email', disabled: true, reason: 'Acme is read-only.' });
  });

  it("disables every control of a frozen tenant with the reason, keeping the values visible", async () => {
    const { section } = await setUp({ docs: tenantIn('Frozen'), rows: [{ type: 'gl.center.flagged', channel: 'email', enabled: true }] });
    expect(await section.getLockReason()).toBe('Acme is under maintenance.');
    const flagged = await section.getCell('gl.center.flagged', 'email');
    expect(flagged).toMatchObject({ checked: true, disabled: true, description: 'Acme is under maintenance.' });
    expect(await (await section.getPopupSwitch()).isDisabled()).toBe(true);
    const [email] = await section.getTestRows();
    expect(email).toMatchObject({ disabled: true, reason: 'Acme is under maintenance.' });
  });

  it('disables every control while offline, with the offline reason, and enables them again', async () => {
    const { rig, section } = await setUp();
    rig.api.setOnline(false);
    await tmSettle();
    expect(await section.getLockReason()).toBe("You're offline");
    expect(await section.getCell('core.job.failed', 'email')).toMatchObject({ disabled: true, description: "You're offline" });
    expect(await (await section.getPopupSwitch()).isDisabled()).toBe(true);
    expect((await section.getTestRows())[0]).toMatchObject({ disabled: true, reason: "You're offline" });
    rig.api.setOnline(true);
    await tmSettle();
    expect(await section.getLockReason()).toBeNull();
    expect((await section.getCell('core.job.failed', 'email')).disabled).toBe(false);
    expect(await (await section.getPopupSwitch()).isDisabled()).toBe(false);
  });

  it("applies the signed-in user's pop-up switch through the preferences at once, and deletes the key when it goes back on", async () => {
    const { rig, section } = await setUp();
    rig.api.respond('users/me/preferences/set', { preferences: { 'notifications.toasts': 'false' }, preferencesTag: 'P2' });
    rig.api.respond('users/me/preferences/delete', { preferences: {}, preferencesTag: 'P3' });
    const toasts = TestBed.inject(TmPreferences).get(tmNotificationToastsKey);
    const popups = await section.getPopupSwitch();
    await popups.toggle();
    expect(toasts()).toBe(false);
    await tmSettleUntil(() => bodies(rig, 'users/me/preferences/set').length === 1);
    expect(bodies(rig, 'users/me/preferences/set')).toEqual([{ items: [{ key: 'notifications.toasts', value: 'false' }] }]);
    await popups.toggle();
    expect(toasts()).toBe(true);
    await tmSettleUntil(() => bodies(rig, 'users/me/preferences/delete').length === 1);
    expect(bodies(rig, 'users/me/preferences/delete')).toEqual([{ keys: ['notifications.toasts'] }]);
    // The signed-in user's value never takes another user's route, nor the grid's save.
    expect(bodies(rig, 'users/preferences/set')).toEqual([]);
    expect(bodies(rig, 'users/preferences/delete')).toEqual([]);
    expect(bodies(rig, 'notification-preferences/save')).toEqual([]);
  });

  it('shows the contact email, else the email, else No address, on the test row', async () => {
    const user = TM_T17_DOCS.me.user;
    const { section } = await setUp({ docs: { me: { ...TM_T17_DOCS.me, user: { ...user, email: 'farah@acme.test', contactEmail: 'desk@acme.test' } } } });
    expect((await section.getTestRows())[0]?.address).toBe('desk@acme.test');
    TestBed.resetTestingModule();
    const signIn = await setUp({ docs: { me: { ...TM_T17_DOCS.me, user: { ...user, email: 'farah@acme.test' } } } });
    expect((await signIn.section.getTestRows())[0]?.address).toBe('farah@acme.test');
    TestBed.resetTestingModule();
    const none = await setUp();
    expect((await none.section.getTestRows())[0]?.address).toBe('No address');
  });

  it("shows each test's result inline and announces it, the button busy meanwhile", async () => {
    const { rig, section } = await setUp();
    const sent = deferred<string>();
    rig.api.respond('users/me/test-notification', () => sent.promise);
    await section.sendTest('email');
    expect((await section.getTestRows())[0]).toMatchObject({ pending: true, result: null });
    sent.resolve('Sent');
    await tmSettleUntil(() => document.querySelector('.cdk-live-announcer-element')?.textContent === 'Sent');
    expect((await section.getTestRows())[0]).toMatchObject({ pending: false, result: 'Sent', link: null });
    expect(bodies(rig, 'users/me/test-notification')).toEqual([{ channel: 'email' }]);

    rig.api.respond('users/me/test-notification', 'NoContactAddress');
    await section.sendTest('email');
    await tmSettle();
    expect((await section.getTestRows())[0]).toMatchObject({ result: 'Add an email address in your profile first.', link: '/17/me/profile' });

    rig.api.respond('users/me/test-notification', 'NotConfigured');
    await section.sendTest('email');
    await tmSettle();
    expect((await section.getTestRows())[0]?.result).toBe("Email isn't set up for this server.");
  });

  it('shows the action error dialog when a test fails, and frees the button with no result', async () => {
    const { rig, fixture, section } = await setUp();
    // A role change took the user's access away while the page was open.
    rig.api.respond('users/me/test-notification', () => {
      throw new TmApiError({ kind: 'forbidden', status: 403, code: 'forbidden', message: 'You cannot do this.' });
    });
    await section.sendTest('email');
    await tmSettleUntil(() => document.querySelector('tm-error-dialog-content') !== null);
    expect((await section.getTestRows())[0]).toMatchObject({ pending: false, disabled: false, result: null });
    const dialog = await TestbedHarnessEnvironment.documentRootLoader(fixture).getHarness(TmErrorDialogHarness);
    expect(await dialog.getTitle()).not.toBe('');
  });

  it("reads another user's set and pop-ups through their routes, saves each change there, and has no test delivery", async () => {
    const { rig, section } = await setUp({ user: 42, rows: [{ type: 'gl.center.flagged', channel: 'email', enabled: true }] });
    rig.api.respond('users/preferences/set', { preferences: {} });
    rig.api.respond('users/notification-preferences/save', []);
    expect(bodies(rig, 'users/notification-preferences/get')).toEqual([{ userId: 42 }]);
    expect(bodies(rig, 'users/preferences/get')).toEqual([{ userId: 42 }]);
    expect(bodies(rig, 'notification-preferences/get')).toEqual([]);
    expect((await section.getCell('gl.center.flagged', 'email')).checked).toBe(true);
    expect(await section.hasTestDelivery()).toBe(false);
    const popups = await section.getPopupSwitch();
    expect(await popups.isChecked()).toBe(false);

    // The pop-up switch saves at once; back on, its fallback, it deletes the stored value.
    rig.api.respond('users/preferences/delete', { preferences: {} });
    await popups.toggle();
    await tmSettle();
    expect(bodies(rig, 'users/preferences/delete')).toEqual([{ id: 42, arguments: { keys: ['notifications.toasts'] } }]);
    expect(bodies(rig, 'users/preferences/set')).toEqual([]);
    await popups.toggle();
    await tmSettle();
    expect(bodies(rig, 'users/preferences/set')).toEqual([{ id: 42, arguments: { items: [{ key: 'notifications.toasts', value: 'false' }] } }]);

    // The grid saves the whole set after the wait.
    await section.toggleCell('core.job.failed', 'email');
    await wait(PAST_WAIT_MS);
    expect(bodies(rig, 'users/notification-preferences/save')).toEqual([
      {
        id: 42,
        arguments: {
          preferences: [
            { type: 'core.job.failed', channel: 'email', enabled: true },
            { type: 'gl.center.flagged', channel: 'email', enabled: true },
          ],
        },
      },
    ]);
    expect(bodies(rig, 'notification-preferences/save')).toEqual([]);
    expect(await section.getStatus()).toBe('All changes saved');
  });

  it("says a failed save of another user's pop-ups failed, and sends it again on Try again", async () => {
    const { rig, section } = await setUp({ user: 42 });
    rig.api.respond('users/preferences/delete', () => {
      throw new TmApiError({ kind: 'server', status: 500 });
    });
    const popups = await section.getPopupSwitch();
    await popups.toggle();
    await tmSettle();
    expect(await section.getStatus()).toBe("Couldn't save your changes");
    expect(await popups.isChecked()).toBe(true);
    rig.api.respond('users/preferences/delete', { preferences: {} });
    await section.retry();
    await tmSettleUntil(() => bodies(rig, 'users/preferences/delete').length === 2);
    await tmSettle();
    expect(bodies(rig, 'users/preferences/delete')[1]).toEqual({ id: 42, arguments: { keys: ['notifications.toasts'] } });
    expect(await section.getStatus()).toBe('All changes saved');
  });

  describe('below md', () => {
    afterEach(async () => page.viewport(1280, 900));

    it("stacks each type's switches under its label, each named by its channel", async () => {
      await page.viewport(375, 900);
      const { fixture } = await setUp();
      const host = fixture.nativeElement as HTMLElement;
      const row = host.querySelector('tr[data-type="core.job.failed"]')!;
      const label = row.querySelector('th')!;
      const inbox = row.querySelector('td[data-channel="inbox"]')!;
      const email = row.querySelector('td[data-channel="email"]')!;
      // The columns' headers go; each switch names its channel, under the type's label and under the one before it.
      expect(getComputedStyle(host.querySelector('thead')!).display).toBe('none');
      const name = email.querySelector('.tm-notification-settings__label')!;
      expect(getComputedStyle(name).display).toBe('inline');
      expect(name.textContent.trim()).toBe('Email');
      expect(inbox.getBoundingClientRect().top).toBeGreaterThanOrEqual(label.getBoundingClientRect().bottom - 1);
      expect(email.getBoundingClientRect().top).toBeGreaterThanOrEqual(inbox.getBoundingClientRect().bottom - 1);
    });
  });

  for (const theme of ['light', 'dark'] as const) {
    for (const language of ['en', 'ar'] as const) {
      it(`meets the accessibility rules in the ${theme} theme, in ${language === 'en' ? 'English' : 'Arabic'}`, async () => {
        const { fixture, section } = await setUp({ rows: [{ type: 'gl.center.flagged', channel: 'email', enabled: true }] });
        if (theme === 'dark') {
          document.documentElement.setAttribute('data-theme', 'dark');
        }
        if (language === 'ar') {
          const l10n = TestBed.inject(TmLocalization);
          l10n.setLanguage('ar');
          await tmSettleUntil(() => document.documentElement.dir === 'rtl');
          // The rig's Arabic carries none of the section's strings, so the English shows right to left.
          expect((await section.getCell('core.job.failed', 'inbox')).description).toBe('Always on');
        }
        const results = await axe.run(fixture.nativeElement as HTMLElement, { runOnly: { type: 'tag', values: WCAG } });
        expect(results.violations.map((violation) => violation.id)).toEqual([]);
      });
    }
  }
});

describe('tm-notification-settings-page', () => {
  afterEach(() => TestBed.resetTestingModule());

  /** Enters tenant 17 and renders the page. */
  async function setUpPage(): Promise<{ readonly rig: TmAttentionRig; readonly fixture: ComponentFixture<TmNotificationSettingsPage> }> {
    const rig = await tmAttentionRig({}, { notificationTypes: TYPES });
    rig.api.respond('notification-preferences/get', []);
    rig.api.respond('notification-preferences/save', []);
    rig.api.respond('users/me/test-notification', 'Sent');
    const fixture = TestBed.createComponent(TmNotificationSettingsPage);
    await tmSettleUntil(() => TestBed.inject(TmPageContexts).current()?.status() === 'ready');
    return { rig, fixture };
  }

  it('publishes a settings context whose state says whether a change is unsaved', async () => {
    const { rig } = await setUpPage();
    const context = TestBed.inject(TmPageContexts).current()!;
    expect(context.kind).toBe('settings');
    expect(context.routeTemplate).toBe('/:t/me/notifications');
    expect(context.title()).toBe('Notifications');
    expect(context.form).toBeUndefined();
    expect(context.state()).toEqual({ category: 'notifications', mode: 'edit', dirty: false });
    expect(context.actions().map((action) => action.id)).toEqual(['notifications.settings.set', 'notifications.settings.test']);

    const actions = TestBed.inject(TmPageActions);
    expect(await actions.invoke('notifications.settings.set', { type: 'core.job.failed', channel: 'email', enabled: true }, 'assistant')).toEqual({
      outcome: 'done',
    });
    await tmSettle();
    expect(context.state()).toEqual({ category: 'notifications', mode: 'edit', dirty: true });
    await wait(PAST_WAIT_MS);
    expect(bodies(rig, 'notification-preferences/save')).toEqual([[{ type: 'core.job.failed', channel: 'email', enabled: true }]]);
    expect(context.state()).toEqual({ category: 'notifications', mode: 'edit', dirty: false });
  });

  it("refuses a locked cell as Always on and a cell the grid does not show, and runs a test through its action", async () => {
    const { rig } = await setUpPage();
    const actions = TestBed.inject(TmPageActions);
    expect(await actions.invoke('notifications.settings.set', { type: 'core.job.failed', channel: 'inbox', enabled: false }, 'assistant')).toEqual({
      outcome: 'refused',
      reason: 'disabled',
      message: 'Always on',
    });
    expect(await actions.invoke('notifications.settings.set', { type: 'gl.center.digest', channel: 'inbox', enabled: true }, 'assistant')).toMatchObject({
      outcome: 'refused',
      reason: 'not-found',
    });
    expect(await actions.invoke('notifications.settings.test', { channel: 'email' }, 'assistant')).toEqual({ outcome: 'done', data: 'Sent' });
    expect(bodies(rig, 'users/me/test-notification')).toEqual([{ channel: 'email' }]);
    expect(await actions.invoke('notifications.settings.test', { channel: 'inbox' }, 'assistant')).toMatchObject({ outcome: 'refused', reason: 'not-found' });
  });
});

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { TmConfirm, type TmConfirmYesNoOptions } from '@tellma/core-ui/confirm';
import { TmErrorDialog, type TmErrorDialogOptions, type TmErrorDialogResult } from '@tellma/core-ui/error-dialog';
import { TmFakeSession, TmFakeTelemetry } from '@tellma/core-testing';
import { TmApiError, TmClientStore, type TmDistributionInfo, TmEntities, TmLaunch, TmPageContexts, TmPermissions, TmShellMain, TmSupport } from '@tellma/core';

import { TmFakeApiUpdates, tmFakeUpdatesProviders } from '../../src/api/tm-api.testing.util';
import { tmAppRig, tmNavigate, tmSettle, TM_T17, TM_TEST_SUB, type TmAppRig } from '../../src/routes/tm-app.testing.util';
import { TmErrorPresenter } from './tm-error-presenter';
import { TM_RELOAD_HOST } from './tm-error-updates';
import { TmErrorState } from './tm-error-state';

// The error presenter in a whole test app warm in tenant 17, on
// `/17/centers`. The shared dialogs and the confirm are recorded stand-ins,
// so a spec reads what they were asked to show and answers for the user.

/** The shared error dialog, recorded: one dialog at a time, a later show replacing the open one. */
class TmRecordingErrorDialog {
  /** What each show asked for, oldest first. */
  readonly shown: TmErrorDialogOptions[] = [];
  #answer: ((result: TmErrorDialogResult) => void) | null = null;

  /** Records a show; an open dialog resolves `replaced`. */
  show(options: TmErrorDialogOptions): Promise<TmErrorDialogResult> {
    this.#answer?.('replaced');
    this.shown.push(options);
    return new Promise((resolve) => (this.#answer = resolve));
  }

  /** Answers the open dialog as the user would. */
  answer(result: Exclude<TmErrorDialogResult, 'replaced'>): void {
    const answer = this.#answer;
    this.#answer = null;
    answer?.(result);
  }

  /** The open dialog's options. */
  get open(): TmErrorDialogOptions | undefined {
    return this.#answer === null ? undefined : this.shown.at(-1);
  }
}

/** The confirm, recorded, answering what the spec sets. */
class TmRecordingConfirm {
  /** What each confirm asked. */
  readonly asked: TmConfirmYesNoOptions[] = [];
  /** The user's answer. */
  answer = true;

  /** Records the question and answers. */
  confirm(options: TmConfirmYesNoOptions): Promise<boolean> {
    this.asked.push(options);
    return Promise.resolve(this.answer);
  }
}

/** The pieces a spec drives. */
interface TmPresenterRig extends TmAppRig {
  readonly presenter: TmErrorPresenter;
  readonly dialog: TmRecordingErrorDialog;
  readonly confirm: TmRecordingConfirm;
  readonly updates: TmFakeApiUpdates;
  readonly fakeTelemetry: TmFakeTelemetry;
}

/**
 * Starts the test app on `/17/centers` with the recorded dialogs.
 *
 * @param support - Whether the distribution document enables support requests; without one, none is stored.
 */
async function setUp(support?: boolean): Promise<TmPresenterRig> {
  const dialog = new TmRecordingErrorDialog();
  const confirm = new TmRecordingConfirm();
  const updates = new TmFakeApiUpdates();
  const rig = await tmAppRig({
    providers: [
      { provide: TmErrorDialog, useValue: dialog },
      { provide: TmConfirm, useValue: confirm },
      ...tmFakeUpdatesProviders(updates),
      { provide: TM_RELOAD_HOST, useValue: updates },
    ],
  });
  // --- The distribution document, stored before the launch reads it ---
  if (support !== undefined) {
    const document = { support: { enabled: support, providerAccess: false } } as TmDistributionInfo;
    await TestBed.inject(TmClientStore).put({ kind: 'user', sub: TM_TEST_SUB }, 'distribution', { tag: null, savedAt: Date.now(), data: document });
    rig.api.respond('/api/distribution-info', document);
  }
  await tmNavigate('/17/centers');
  return {
    ...rig,
    presenter: TestBed.inject(TmErrorPresenter),
    dialog,
    confirm,
    updates,
    fakeTelemetry: TestBed.inject(TmFakeTelemetry),
  };
}

/** A `forbidden` refusal with the server's arguments. */
function forbidden(args: Record<string, string>): TmApiError {
  return new TmApiError({
    kind: 'forbidden',
    status: 403,
    code: 'forbidden',
    traceId: '0af7651916cd43dd8448eb211c80319c',
    problem: { type: 'about:blank', title: 'Forbidden', status: 403, code: 'forbidden', instance: '/api/17/centers/save', arguments: args },
  });
}

describe('TmErrorPresenter', () => {
  describe('describe', () => {
    it("names the permission a refusal needs by its label, and keeps the raw pair for support", async () => {
      const { presenter } = await setUp();
      const view = presenter.describe(forbidden({ resource: 'gl.Center', action: 'Update', reason: 'no-grant' }), { kind: 'save' });
      const label = TestBed.inject(TmPermissions).label('gl.Center', 'Update')();
      expect(view.title()).toBe("You don't have permission for this");
      expect(view.message()).toBe(`It needs “${label}”. Ask your administrator to grant it.`);
      expect(view.permission).toBe('gl.Center · Update');
      expect(view.traceId).toBe('0af7651916cd43dd8448eb211c80319c');
      expect(view.tone).toBe('error');
      expect(view.actions()).toEqual([]);

      const filtered = presenter.describe(forbidden({ resource: 'gl.Center', action: 'Read', reason: 'outside-filter' }), { kind: 'read' });
      expect(filtered.title()).toBe("Your access doesn't cover this record");
      expect(filtered.message()).toContain(`“${TestBed.inject(TmPermissions).label('gl.Center', 'Read')()}”`);
      expect(filtered.permission).toBe('gl.Center · Read');
    });

    it('names a refused path from the query root, else shows it raw', async () => {
      const { presenter } = await setUp();
      const refusal = forbidden({ resource: 'core.User', action: 'Read', reason: 'restricted-path', path: 'Manager.Image' });
      const resource = TestBed.inject(TmPermissions).label('core.User')();
      const raw = presenter.describe(refusal, { kind: 'read' });
      expect(raw.title()).toBe("You can't see Manager.Image here");
      expect(raw.message()).toBe(`It needs full “${resource}: Read” access. Ask your administrator to grant it.`);
      expect(raw.permission).toBe('core.User · Read');

      // With the call's entity and the entity model loaded, the path reads as its labels: the
      // center's manager (a user) and the user's image (a blob), two navigations deep.
      await TestBed.inject(TmEntities).load();
      const named = presenter.describe(refusal, { kind: 'read', entity: 'gl.Center' });
      const path = TestBed.inject(TmEntities).resolve('gl.Center', 'Manager.Image');
      expect(path).not.toBeNull();
      expect(named.title()).toBe(`You can't see ${path!.label()} here`);
      expect(named.title()).not.toContain('Manager.Image');
    });

    it('names the tenant of a read-only or frozen refusal as the shell shows it', async () => {
      const { presenter } = await setUp();
      const readOnly = presenter.describe(new TmApiError({ kind: 'read-only', status: 403, code: 'tenant-read-only' }), { kind: 'save' });
      expect(readOnly.title()).toBe('Acme is read-only');
      expect(readOnly.message()).toBe('You can view data but not change it. Contact your administrator.');
      const frozen = presenter.describe(new TmApiError({ kind: 'read-only', status: 403, code: 'tenant-frozen' }), { kind: 'save' });
      expect(frozen.title()).toBe('Acme is under maintenance');
    });

    it("words each kind as the table does, with the server's items and the problem's own title", async () => {
      const { presenter } = await setUp();
      const text = (error: TmApiError): [string, string, readonly string[]] => {
        const view = presenter.describe(error, { kind: 'save' });
        return [view.title(), view.message(), view.actions()];
      };
      expect(text(new TmApiError({ kind: 'offline', code: 'network' }))).toEqual([
        "You're offline",
        "Check your connection. We'll keep trying.",
        ['retry'],
      ]);
      expect(text(new TmApiError({ kind: 'offline', code: 'not-sent' }))).toEqual([
        "You're offline",
        "Your changes are kept. Save again when you're back online.",
        ['close'],
      ]);
      expect(text(new TmApiError({ kind: 'offline', code: 'outcome-unknown' }))[0]).toBe("We couldn't confirm this");
      expect(text(new TmApiError({ kind: 'unavailable', status: 503 }))).toEqual(['Acme is busy', 'Try again in a moment.', ['retry']]);
      expect(text(new TmApiError({ kind: 'client-outdated', status: 412, code: 'client-outdated' }))[2]).toEqual(['reload']);
      expect(text(new TmApiError({ kind: 'client-outdated', status: 404, code: 'server-outdated' }))).toEqual([
        'Acme is being updated',
        'Try again in a minute.',
        ['retry'],
      ]);
      expect(text(new TmApiError({ kind: 'server', status: 500 }))[0]).toBe('Something went wrong');
      expect(text(new TmApiError({ kind: 'not-found', status: 404 }))[0]).toBe("This record doesn't exist or you can't see it");
      expect(text(new TmApiError({ kind: 'bad-request', status: 400 }))).toEqual(["Couldn't complete this", '', ['close']]);
      const items = presenter.describe(
        new TmApiError({
          kind: 'validation',
          status: 422,
          code: 'validation',
          problem: {
            type: 'about:blank',
            title: 'Validation',
            status: 422,
            code: 'validation',
            instance: '/api/17/centers/save',
            errors: [{ path: 'Name', code: 'required', message: 'The name is required.' }],
          },
        }),
        { kind: 'save', entity: 'gl.Center' },
      );
      expect(items.items()).toHaveLength(1);
    });
  });

  describe('present', () => {
    it('shows a server error in the 5xx dialog with its details, reports it, and tries again on Try again', async () => {
      const { presenter, dialog, fakeTelemetry } = await setUp();
      const retry = vi.fn();
      const error = new TmApiError({ kind: 'server', status: 500, code: 'internal', traceId: '4bf92f3577b34da6a3ce929d0e0e4736' });
      const presented = presenter.present(error, { kind: 'save', retry });
      expect(dialog.open).toMatchObject({
        title: 'Something went wrong',
        reference: '4bf92f3577b34da6a3ce929d0e0e4736',
        primary: { label: 'Try again' },
      });
      expect(dialog.open?.secondary).toBeUndefined();
      const details = Object.fromEntries((dialog.open?.details ?? []).map((detail) => [detail.label, detail.value]));
      expect(details).toMatchObject({ Status: '500', Code: 'internal', Version: '1.0.0+0123abc' });
      expect(details['Time']).toMatch(/^\d{4}-\d\d-\d\dT/);
      expect(dialog.open?.details?.every((detail) => detail.code === true)).toBe(true);
      expect(fakeTelemetry.errors()).toEqual([error]);
      // The open dialog is an overlay page context, until it closes.
      expect(TestBed.inject(TmPageContexts).overlays()).toEqual([
        expect.objectContaining({ routeTemplate: 'dialog:error', overlay: true }),
      ]);
      expect(TestBed.inject(TmPageContexts).top()?.errors()).toEqual([
        { source: 'request', code: 'internal', message: 'Something went wrong', traceId: '4bf92f3577b34da6a3ce929d0e0e4736' },
      ]);
      dialog.answer('primary');
      await presented;
      expect(retry).toHaveBeenCalledOnce();
      expect(TestBed.inject(TmPageContexts).overlays()).toEqual([]);
    });

    it('offers Copy diagnostics in the 5xx dialog, and Report this problem when support is on, each leaving the dialog open', async () => {
      const { presenter, dialog } = await setUp(true);
      const facade = TestBed.inject(TmSupport);
      const copy = vi.spyOn(facade, 'copyDiagnostics').mockImplementation(() => undefined);
      const open = vi.spyOn(facade, 'open').mockImplementation(() => undefined);
      const error = new TmApiError({ kind: 'server', status: 500, code: 'internal', traceId: '4bf92f3577b34da6a3ce929d0e0e4736' });
      void presenter.present(error, { kind: 'save' });
      expect(dialog.open?.actions?.map(({ label, icon }) => ({ label, icon }))).toEqual([
        { label: 'Copy diagnostics', icon: 'clipboard' },
        { label: 'Report this problem', icon: 'life-buoy' },
      ]);
      dialog.open?.actions?.[0]?.run();
      dialog.open?.actions?.[1]?.run();
      expect(copy).toHaveBeenCalledWith('dialog');
      expect(open).toHaveBeenCalledWith({ error });
      expect(dialog.open?.actions?.every((action) => action.closes !== true)).toBe(true);
      expect(dialog.open).toBeDefined();
    });

    it('offers only Copy diagnostics when support is off', async () => {
      const { presenter, dialog } = await setUp(false);
      void presenter.present(new TmApiError({ kind: 'server', status: 500, code: 'internal' }), { kind: 'save' });
      expect(dialog.open?.actions?.map(({ label }) => label)).toEqual(['Copy diagnostics']);
    });

    it('gets the update on Reload, says when the download fails, and offers Reload anyway from the second failure', async () => {
      const { presenter, dialog, updates } = await setUp();
      updates.answer = 'failed';
      const error = new TmApiError({ kind: 'client-outdated', status: 412, code: 'client-outdated' });
      let settled = false;
      const presented = presenter.present(error, { kind: 'save' }).then(() => (settled = true));
      const first = dialog.open;
      expect(first?.primary).toMatchObject({ label: 'Reload', pendingLabel: 'Getting the update…' });
      expect(first?.message).toBe('Reload to continue. Your unsaved changes come back after the reload.');
      // A failed download keeps the dialog, with the failure's message and the same Reload.
      expect(await first?.primary?.run?.()).toBe(false);
      await Promise.resolve();
      expect(dialog.shown).toHaveLength(2);
      expect(dialog.open?.message).toBe("Couldn't download the update. Check your connection and try again.");
      expect(dialog.open?.secondary).toBeUndefined();
      // The second failure adds Reload anyway, a plain reload.
      expect(await dialog.open?.primary?.run?.()).toBe(false);
      await Promise.resolve();
      expect(dialog.open?.secondary?.label).toBe('Reload anyway');
      void dialog.open?.secondary?.run?.();
      expect(updates.reloads).toBe(1);
      expect(settled).toBe(false);
      dialog.answer('close');
      await presented;
      expect(updates.checks).toBe(2);
    });

    it('reloads into the new version when the download succeeds', async () => {
      const { presenter, dialog, updates } = await setUp();
      updates.answer = 'ready';
      void presenter.present(new TmApiError({ kind: 'client-outdated', status: 412, code: 'client-outdated' }), { kind: 'action' });
      let done = false;
      void dialog.open?.primary?.run?.().then(() => (done = true));
      await tmSettle();
      // The page is reloading: the button stays busy.
      expect(done).toBe(false);
      expect([updates.checks, updates.reloads]).toEqual([1, 1]);
    });

    it("shows a refusal in the 400 dialog with the server's items, the reference and the raw permission", async () => {
      const { presenter, dialog, fakeTelemetry } = await setUp();
      const presented = presenter.present(forbidden({ resource: 'gl.Center', action: 'Update', reason: 'no-grant' }), { kind: 'save' });
      expect(dialog.open).toMatchObject({
        title: "You don't have permission for this",
        reference: '0af7651916cd43dd8448eb211c80319c',
        details: [{ label: 'Permission', value: 'gl.Center · Update', code: true }],
      });
      expect(dialog.open?.primary).toBeUndefined();
      expect(fakeTelemetry.errors()).toEqual([]);
      dialog.answer('close');
      await presented;
    });

    it('shows nothing for a read the page renders inline, a cancel or an ended session', async () => {
      const { presenter, dialog } = await setUp();
      await presenter.present(new TmApiError({ kind: 'server', status: 500 }), { kind: 'read' });
      await presenter.present(new TmApiError({ kind: 'cancelled' }), { kind: 'save' });
      await presenter.present(new TmApiError({ kind: 'unauthenticated', status: 401 }), { kind: 'save' });
      expect(dialog.shown).toEqual([]);
    });

    it("asks before a step-up, and signs in again with the call's challenge", async () => {
      const { presenter, confirm } = await setUp();
      const challenge = { acr: 'mfa', maxAge: 300 };
      const error = new TmApiError({ kind: 'step-up', status: 401, code: 'step-up-required', challenge });
      confirm.answer = false;
      await presenter.present(error, { kind: 'save' });
      expect(TestBed.inject(TmFakeSession).signIns()).toEqual([]);
      confirm.answer = true;
      void presenter.present(error, { kind: 'save' });
      await vi.waitFor(() => expect(TestBed.inject(TmFakeSession).signIns()).toEqual([{ stepUp: challenge }]));
      expect(confirm.asked[0]).toMatchObject({ title: "Confirm it's you", confirmLabel: 'Continue' });
    });

    it('sweeps a tenant that is gone and goes to the picker, with a notice unless the entry was automatic', async () => {
      const { presenter, store, router, fakeTelemetry } = await setUp();
      store.boot.patch({ lastTenantId: 17 });
      const sweep = vi.spyOn(store, 'sweep');
      vi.spyOn(TestBed.inject(TmLaunch), 'takeAutomatic').mockReturnValue(false);
      const gone = new TmApiError({ kind: 'tenant-not-found', status: 404, code: 'tenant-not-found' });
      await presenter.present(gone, { kind: 'read' });
      // The idle sweep of the user's other sessions may run while the picker's chunk loads.
      expect(sweep.mock.calls.filter(([rule]) => typeof rule === 'object' && 'tenant' in rule)).toEqual([[{ tenant: 17 }]]);
      expect(store.boot.read().lastTenantId).toBeNull();
      expect(store.doc(TM_T17, 'settings')()).toBeUndefined();
      expect(router.url).toBe('/tenants?notice=tenant-not-found&from=17');
      expect(fakeTelemetry.recent().filter((item) => item.name === 'tm.launch.tenant-gone').map((item) => item.props)).toEqual([
        { automatic: false },
      ]);
    });

    it('goes to the picker silently on an automatic entry, and leaves a tab on another tenant where it is', async () => {
      const { presenter, router } = await setUp();
      vi.spyOn(TestBed.inject(TmLaunch), 'takeAutomatic').mockReturnValue(true);
      const gone = new TmApiError({ kind: 'tenant-not-found', status: 404, code: 'tenant-not-found' });
      await presenter.present(gone, { kind: 'read', tenantId: 18 });
      expect(router.url).toBe('/17/centers');
      await presenter.present(gone, { kind: 'read' });
      expect(router.url).toBe('/tenants');
    });

    it('shows the tenant-state page in place of the route for a suspended tenant', async () => {
      const { presenter } = await setUp();
      await presenter.present(new TmApiError({ kind: 'tenant-unavailable', status: 403, code: 'tenant-suspended' }), { kind: 'read' });
      const override = TestBed.inject(TmShellMain).override();
      expect(override?.component).toBe(TmErrorState);
      const view = override?.inputs?.['view'] as ReturnType<TmErrorPresenter['describe']>;
      expect([view.title(), view.message(), view.actions()]).toEqual(['Acme is suspended', 'Contact your administrator.', ['switch-tenant']]);
    });
  });
});

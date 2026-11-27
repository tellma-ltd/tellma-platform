// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { LiveAnnouncer } from '@angular/cdk/a11y';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { computed, DestroyRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTellmaStateTesting, provideTellmaTesting, TmFakeTelemetry } from '@tellma/core-testing';
import { TmConfirm } from '@tellma/core-ui/confirm';
import { ɵtmModalOpened } from '@tellma/core-ui/overlay';
import { TmConfirmHarness } from '@tellma/core-ui-testing';

import { TmApiError } from '../api/tm-api-error';
import { TM_DEV_MODE } from '../app/tm-dev-mode';
import type { TmJsonObject } from '../json/tm-json';
import { TmLocalization } from '../l10n/tm-localization';
import { TmShortcuts } from '../nav/tm-shortcuts';
import { tmAppRig, tmSettleUntil } from '../routes/tm-app.testing.util';
import type { TmActionOrigin, TmActionResult, TmPageAction } from './tm-page-action';
import { TmPageActions } from './tm-page-actions';
import { TmPageContexts } from './tm-page-contexts';
import { tmLifetime, tmTestAction, tmTestContext, tmTestInput, TmTestScope, type TmTestAction } from './tm-page.testing.util';

// The dispatcher's six steps for every origin, the registration defaults and
// development checks, and the top context's shortcuts. The confirmation, the
// announcer and the localization are fakes the specs read, except in the
// last test, which asks through the real dialog in a whole app.

/** Every origin an invocation has. */
const ORIGINS: readonly TmActionOrigin[] = ['user', 'shortcut', 'launcher', 'assistant'];

/** What the specs read and drive. */
interface TmRig {
  /** The public face under test. */
  readonly actions: TmPageActions;
  /** The contexts. */
  readonly contexts: TmPageContexts;
  /** The confirmation's fake: answers `true` unless a spec says otherwise. */
  readonly confirm: ReturnType<typeof vi.fn<(options: unknown) => Promise<boolean>>>;
  /** The announcer's fake. */
  readonly announce: ReturnType<typeof vi.fn>;
  /** The telemetry. */
  readonly telemetry: TmFakeTelemetry;
}

/** Configures the dispatcher over fakes, in development or production. */
function rig(dev = true): TmRig {
  const confirm = vi.fn<(options: unknown) => Promise<boolean>>(() => Promise.resolve(true));
  const announce = vi.fn(() => Promise.resolve());
  TestBed.configureTestingModule({
    providers: [
      provideTellmaTesting(),
      provideTellmaStateTesting(),
      { provide: TM_DEV_MODE, useValue: () => dev },
      { provide: TmConfirm, useValue: { confirm } },
      { provide: LiveAnnouncer, useValue: { announce } },
      // The key, followed by the arguments when there are any.
      {
        provide: TmLocalization,
        useValue: { ɵtext: (key: string, args?: object) => signal(args === undefined ? key : `${key} ${JSON.stringify(args)}`) },
      },
    ],
  });
  return {
    actions: TestBed.inject(TmPageActions),
    contexts: TestBed.inject(TmPageContexts),
    confirm,
    announce,
    telemetry: TestBed.inject(TmFakeTelemetry),
  };
}

/** A component with a page scope of its own. */
function scope(): { scope: TmTestScope; element: HTMLElement; destroy: () => void } {
  const fixture = TestBed.createComponent(TmTestScope);
  return { scope: fixture.componentInstance, element: fixture.nativeElement, destroy: () => fixture.destroy() };
}

/** Registers a routed context publishing `actions`, outside any scope. */
function page(...actions: TmPageAction[]): void {
  TestBed.inject(TmPageContexts).register(tmTestContext('/:t/a', {}, actions), tmLifetime().get(DestroyRef));
}

/** Registers a dialog's context publishing `actions`, outside any scope; returns its end. */
function dialog(...actions: TmPageAction[]): () => void {
  const lifetime = tmLifetime();
  TestBed.inject(TmPageContexts).register(tmTestContext('panel:x', { overlay: true }, actions), lifetime.get(DestroyRef));
  return () => lifetime.destroy();
}

/** A promise and the function that settles it. */
function deferred<T>(): { promise: Promise<T>; resolve: (value: T) => void } {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((settle) => (resolve = settle));
  return { promise, resolve };
}

/** A destructive action with its confirmation keys. */
function destructive(id: string, patch: Partial<TmPageAction> = {}): TmTestAction {
  return tmTestAction(id, {
    destructive: true,
    confirm: { titleKey: 'Acme_Delete_Title', messageKey: 'Acme_Delete_Message' },
    ...patch,
  });
}

/** Presses a key on `target`, bubbling and cancelable as a real one is. */
function press(init: KeyboardEventInit, target: EventTarget = document.body): void {
  target.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, cancelable: true, ...init }));
}

describe('TmPageActions', () => {
  describe('step 1: finding the action', () => {
    it('refuses an id no context offers as not found, and reports no usage', async () => {
      const { actions, telemetry } = rig();
      page(tmTestAction('acme.a.save'));
      expect(await actions.invoke('acme.a.other', {}, 'assistant')).toEqual({ outcome: 'refused', reason: 'not-found' });
      expect(telemetry.recent()).toEqual([]);
    });

    it('searches the top dialog, then the dialogs beneath, then the routed page', async () => {
      const { actions } = rig();
      const routed = tmTestAction('acme.x.run');
      const routedOnly = tmTestAction('acme.page.only');
      page(routed, routedOnly);
      const lower = tmTestAction('acme.x.run');
      const lowerOnly = tmTestAction('acme.lower.only');
      dialog(lower, lowerOnly);
      const upper = tmTestAction('acme.x.run');
      dialog(upper);

      for (const origin of ORIGINS) {
        expect(await actions.invoke('acme.x.run', { n: 1 }, origin)).toEqual({ outcome: 'done' });
      }
      expect(upper.calls).toEqual(ORIGINS.map((origin) => ({ args: { n: 1 }, origin })));
      expect([lower.calls.length, routed.calls.length]).toEqual([0, 0]);

      // An id the dialogs lack still reaches the context beneath them.
      await actions.invoke('acme.lower.only', {}, 'assistant');
      await actions.invoke('acme.page.only', {}, 'launcher');
      expect([lowerOnly.calls, routedOnly.calls]).toEqual([[{ args: {}, origin: 'assistant' }], [{ args: {}, origin: 'launcher' }]]);
    });
  });

  describe('step 2: a disabled action', () => {
    it('refuses it without running it, and with no reason announces nothing', async () => {
      const { actions, announce } = rig();
      const action = tmTestAction('acme.a.save', { enabled: signal(false) });
      page(action);
      for (const origin of ORIGINS) {
        expect(await actions.invoke('acme.a.save', {}, origin)).toEqual({ outcome: 'refused', reason: 'disabled' });
      }
      expect(action.calls).toEqual([]);
      expect(announce).not.toHaveBeenCalled();
    });

    it('carries its reason for every origin, and announces it politely to a user or a shortcut', async () => {
      const { actions, announce } = rig();
      const reason = 'You lack the permission to approve.';
      page(tmTestAction('acme.a.approve', { enabled: signal(false), disabledReason: signal(reason) }));
      for (const origin of ORIGINS) {
        expect(await actions.invoke('acme.a.approve', {}, origin)).toEqual({ outcome: 'refused', reason: 'disabled', message: reason });
      }
      expect(announce.mock.calls).toEqual([
        [reason, 'polite'],
        [reason, 'polite'],
      ]);
    });
  });

  describe('step 3: a busy action', () => {
    it('refuses a second invocation while the first runs, for every origin, and lists the id as running meanwhile', async () => {
      const { actions } = rig();
      const answer = deferred<TmActionResult>();
      const action = tmTestAction('acme.a.save', {}, () => answer.promise);
      page(action);

      const first = actions.invoke('acme.a.save', {}, 'user');
      expect(actions.running()).toEqual(new Set(['acme.a.save']));
      for (const origin of ORIGINS) {
        expect(await actions.invoke('acme.a.save', {}, origin)).toEqual({ outcome: 'refused', reason: 'busy' });
      }
      answer.resolve({ outcome: 'done', data: { saved: 3 } });
      expect(await first).toEqual({ outcome: 'done', data: { saved: 3 } });
      expect(actions.running()).toEqual(new Set());
      expect(action.calls.length).toBe(1);
    });
  });

  describe('step 4: a destructive action', () => {
    it('asks first in the danger form, whoever invokes it, and runs it once confirmed', async () => {
      const { actions, confirm } = rig();
      const action = destructive('acme.a.delete', {
        confirm: { titleKey: 'Acme_Delete_Title', messageKey: 'Acme_Delete_Message', args: signal({ name: 'Main' }) },
      });
      page(action);
      for (const origin of ORIGINS) {
        expect(await actions.invoke('acme.a.delete', {}, origin)).toEqual({ outcome: 'done' });
      }
      expect(confirm.mock.calls.length).toBe(ORIGINS.length);
      expect(confirm).toHaveBeenLastCalledWith({
        title: 'Acme_Delete_Title {"name":"Main"}',
        message: 'Acme_Delete_Message {"name":"Main"}',
        confirmLabel: 'acme.a.delete.label {"name":"Main"}',
        destructive: true,
      });
      expect(action.calls.map((call) => call.origin)).toEqual(ORIGINS);
    });

    it('words its confirmation from the invocation when its arguments are a function of them', async () => {
      const { actions, confirm } = rig();
      const action = destructive('acme.a.delete', {
        params: { type: 'object', properties: { id: { type: 'integer' } } },
        confirm: {
          titleKey: 'Acme_Delete_Title',
          messageKey: 'Acme_Delete_Message',
          args: (args) => ({ kind: (args as { readonly id?: unknown }).id === 7 ? 'import' : 'job' }),
        },
      });
      page(action);
      expect(await actions.invoke('acme.a.delete', { id: 7 }, 'assistant')).toEqual({ outcome: 'done' });
      expect(confirm).toHaveBeenLastCalledWith({
        title: 'Acme_Delete_Title {"kind":"import"}',
        message: 'Acme_Delete_Message {"kind":"import"}',
        confirmLabel: 'acme.a.delete.label {"kind":"import"}',
        destructive: true,
      });
      expect(await actions.invoke('acme.a.delete', { id: 8 }, 'user')).toEqual({ outcome: 'done' });
      expect(confirm).toHaveBeenLastCalledWith(expect.objectContaining({ title: 'Acme_Delete_Title {"kind":"job"}' }));
    });

    it('names its cancel button when the confirmation gives a cancel key', async () => {
      const { actions, confirm } = rig();
      page(destructive('acme.a.delete', { confirm: { titleKey: 'Acme_Delete_Title', messageKey: 'Acme_Delete_Message', cancelKey: 'Acme_Keep' } }));
      expect(await actions.invoke('acme.a.delete', {}, 'user')).toEqual({ outcome: 'done' });
      expect(confirm).toHaveBeenLastCalledWith(expect.objectContaining({ cancelLabel: 'Acme_Keep' }));
    });

    it('refuses arguments the action does not accept as not found, before asking and again once answered', async () => {
      const { actions, confirm } = rig();
      let accepted = new Set<unknown>([7]);
      const answer = deferred<boolean>();
      confirm.mockReturnValue(answer.promise);
      const action = destructive('acme.a.delete', {
        accepts: (args) => typeof args === 'object' && args !== null && !Array.isArray(args) && accepted.has((args as TmJsonObject)['id']),
      });
      page(action);

      // Arguments it does not accept, null among them, never reach the confirmation.
      expect(await actions.invoke('acme.a.delete', { id: 8 }, 'assistant')).toEqual({ outcome: 'refused', reason: 'not-found' });
      expect(await actions.invoke('acme.a.delete', null, 'assistant')).toEqual({ outcome: 'refused', reason: 'not-found' });
      expect(confirm).not.toHaveBeenCalled();

      // A row that leaves while the question is open is refused once answered.
      const pending = actions.invoke('acme.a.delete', { id: 7 }, 'user');
      await Promise.resolve();
      accepted = new Set();
      answer.resolve(true);
      expect(await pending).toEqual({ outcome: 'refused', reason: 'not-found' });
      expect(action.calls).toEqual([]);
    });

    it('answers cancelled when the user declines, without running it', async () => {
      const { actions, confirm } = rig();
      confirm.mockResolvedValue(false);
      const action = destructive('acme.a.delete');
      page(action);
      expect(await actions.invoke('acme.a.delete', {}, 'assistant')).toEqual({ outcome: 'cancelled' });
      expect(action.calls).toEqual([]);
    });

    it('holds the id busy while the question is open, though not running', async () => {
      const { actions, confirm } = rig();
      const answer = deferred<boolean>();
      confirm.mockReturnValue(answer.promise);
      page(destructive('acme.a.delete'));

      const first = actions.invoke('acme.a.delete', {}, 'user');
      expect(await actions.invoke('acme.a.delete', {}, 'shortcut')).toEqual({ outcome: 'refused', reason: 'busy' });
      expect(actions.running()).toEqual(new Set());
      answer.resolve(true);
      expect(await first).toEqual({ outcome: 'done' });
      expect(confirm).toHaveBeenCalledTimes(1);
    });

    it('checks the action again once answered: gone is not found, disabled is refused', async () => {
      const { actions, confirm } = rig();
      const gone = deferred<boolean>();
      confirm.mockReturnValueOnce(gone.promise);
      // The page beneath offers the same id, which never runs in the closed dialog's place.
      const beneath = destructive('acme.x.delete');
      page(beneath);
      const close = dialog(destructive('acme.x.delete'));
      const pending = actions.invoke('acme.x.delete', {}, 'user');
      close();
      gone.resolve(true);
      expect(await pending).toEqual({ outcome: 'refused', reason: 'not-found' });
      expect(beneath.calls).toEqual([]);

      const disabled = deferred<boolean>();
      confirm.mockReturnValueOnce(disabled.promise);
      const enabled = signal(true);
      const action = destructive('acme.a.delete', { enabled, disabledReason: signal('Posted meanwhile.') });
      page(action);
      const second = actions.invoke('acme.a.delete', {}, 'assistant');
      enabled.set(false);
      disabled.resolve(true);
      expect(await second).toEqual({ outcome: 'refused', reason: 'disabled', message: 'Posted meanwhile.' });
      expect(action.calls).toEqual([]);
    });

    it("runs the context's current action once answered, though its list was rebuilt meanwhile", async () => {
      const { actions, confirm } = rig();
      const answer = deferred<boolean>();
      confirm.mockReturnValue(answer.promise);
      // A framework's list, built anew whenever the record changes.
      const record = signal(1);
      const runs: number[] = [];
      const context = tmTestContext('/:t/a');
      const rebuilt = {
        ...context,
        actions: computed(() => {
          const id = record();
          const invoke = (): Promise<TmActionResult> => {
            runs.push(id);
            return Promise.resolve({ outcome: 'done' });
          };
          return [destructive('acme.a.delete', { invoke })];
        }),
      };
      TestBed.inject(TmPageContexts).register(rebuilt, tmLifetime().get(DestroyRef));

      const pending = actions.invoke('acme.a.delete', {}, 'user');
      record.set(2);
      answer.resolve(true);
      expect(await pending).toEqual({ outcome: 'done' });
      expect(runs).toEqual([2]);
    });

    it('answers failed when the question cannot be asked', async () => {
      const { actions, confirm } = rig();
      const problem = new Error('no overlay');
      confirm.mockRejectedValue(problem);
      page(destructive('acme.a.delete'));
      expect(await actions.invoke('acme.a.delete', {}, 'user')).toEqual({ outcome: 'failed', error: problem });
      // Not busy any more.
      confirm.mockResolvedValue(true);
      expect(await actions.invoke('acme.a.delete', {}, 'user')).toEqual({ outcome: 'done' });
    });

    it('throws in development for a destructive action without its confirmation, and asks with the label and description in production', async () => {
      rig();
      page(tmTestAction('acme.a.delete', { destructive: true }));
      await expect(TestBed.inject(TmPageActions).invoke('acme.a.delete', {}, 'user')).rejects.toThrow(
        'The destructive page action "acme.a.delete" needs a confirm with its title and message keys.',
      );

      TestBed.resetTestingModule();
      const { actions, confirm } = rig(false);
      page(tmTestAction('acme.a.delete', { destructive: true, descriptionKey: 'Acme_Delete_Description' }));
      expect(await actions.invoke('acme.a.delete', {}, 'user')).toEqual({ outcome: 'done' });
      expect(confirm).toHaveBeenCalledWith({
        title: 'acme.a.delete.label',
        message: 'Acme_Delete_Description',
        confirmLabel: 'acme.a.delete.label',
        destructive: true,
      });
    });
  });

  describe('step 5: running the action', () => {
    it("answers the action's result", async () => {
      const { actions } = rig();
      page(tmTestAction('acme.a.suggest', {}, () => Promise.resolve({ outcome: 'done', data: [1, 2] })));
      expect(await actions.invoke('acme.a.suggest', { limit: 2 }, 'assistant')).toEqual({ outcome: 'done', data: [1, 2] });
    });

    it('turns a throw into failed, reporting a fault but not an API error, and frees the id', async () => {
      const { actions, telemetry } = rig();
      const fault = new TypeError('broken');
      const api = new TmApiError({ kind: 'forbidden', status: 403 });
      let thrown: unknown = fault;
      page(
        tmTestAction('acme.a.save', {}, () => {
          throw thrown;
        }),
      );
      expect(await actions.invoke('acme.a.save', {}, 'user')).toEqual({ outcome: 'failed', error: fault });
      thrown = api;
      expect(await actions.invoke('acme.a.save', {}, 'shortcut')).toEqual({ outcome: 'failed', error: api });
      expect(telemetry.errors()).toEqual([fault]);
      expect(actions.running()).toEqual(new Set());
    });
  });

  describe('step 6: the usage signal', () => {
    it('reports every invocation of a found action, sampled, with its origin, outcome and duration', async () => {
      const { actions, telemetry } = rig();
      const event = vi.spyOn(telemetry, 'event');
      page(tmTestAction('acme.a.save'), tmTestAction('acme.a.off', { enabled: signal(false) }));
      await actions.invoke('acme.a.save', {}, 'launcher');
      await actions.invoke('acme.a.off', {}, 'assistant');
      expect(event.mock.calls).toEqual([
        ['tm.action.invoked', { action: 'acme.a.save', origin: 'launcher', outcome: 'done', ms: expect.any(Number) }, { sampled: true }],
        ['tm.action.invoked', { action: 'acme.a.off', origin: 'assistant', outcome: 'refused', ms: expect.any(Number) }, { sampled: true }],
      ]);
    });
  });

  describe('register', () => {
    it('applies the defaults, and keeps the input as the receiver of its invoke', async () => {
      const { actions, contexts } = rig();
      page();
      const input = {
        ...tmTestInput('acme.a.save'),
        saved: 0,
        invoke(this: { saved: number }): Promise<TmActionResult> {
          this.saved++;
          return Promise.resolve({ outcome: 'done' });
        },
      };
      actions.register(input, tmLifetime().get(DestroyRef));
      const registered = contexts.current()!.actions()[0]!;
      expect(registered).toMatchObject({ id: 'acme.a.save', labelKey: 'acme.a.save.label', group: 'menu', destructive: false });
      expect([registered.enabled(), registered.disabledReason()]).toEqual([true, null]);
      await actions.invoke('acme.a.save', {}, 'user');
      expect(input.saved).toBe(1);
    });

    it('throws in development for a malformed id or a shortcut on an action that requires arguments, and registers them in production', () => {
      const { actions } = rig();
      const lifetime = tmLifetime().get(DestroyRef);
      expect(() => actions.register(tmTestInput('Save'), lifetime)).toThrow(
        'The page action id "Save" must be a lowercase scope and one to four kebab-case parts, such as "acme.credit-review.suggest-limits".',
      );
      expect(() => actions.register(tmTestInput('acme.a.delete', { destructive: true }), lifetime)).toThrow(
        'The destructive page action "acme.a.delete" needs a confirm with its title and message keys.',
      );
      const params: TmJsonObject = { type: 'object', required: ['limit'] };
      expect(() => actions.register(tmTestInput('acme.a.limit', { shortcut: 'Mod+J', params }), lifetime)).toThrow(
        'The page action "acme.a.limit" requires arguments, so it can take no shortcut.',
      );

      TestBed.resetTestingModule();
      const production = rig(false);
      page();
      production.actions.register(tmTestInput('Save'), tmLifetime().get(DestroyRef));
      expect(production.contexts.current()!.actions().map((action) => action.id)).toEqual(['Save']);
    });
  });

  describe('shortcuts', () => {
    it("keeps the top context's shortcuts live, invoking through the dispatcher, and only while it is on top", () => {
      rig();
      const shortcuts = TestBed.inject(TmShortcuts);
      const routed = scope();
      routed.scope.contexts.register(tmTestContext('/:t/a'), routed.scope.destroyRef);
      const save = tmTestInput('acme.a.save', { shortcut: 'Mod+J' });
      const saved = vi.spyOn(save, 'invoke');
      routed.scope.actions.register(save, routed.scope.destroyRef);
      TestBed.tick();
      expect(shortcuts.active().map((shortcut) => shortcut.id)).toEqual(['acme.a.save']);
      press({ key: 'j', code: 'KeyJ', ctrlKey: true });
      expect(saved).toHaveBeenCalledWith({}, 'shortcut');

      // A dialog's context on top takes the keys; its own shortcut runs inside it.
      const panel = scope();
      panel.scope.contexts.register(tmTestContext('panel:x', { overlay: true }), panel.scope.destroyRef);
      const read = tmTestInput('acme.x.read', { shortcut: 'Mod+Shift+J' });
      const readCalls = vi.spyOn(read, 'invoke');
      panel.scope.actions.register(read, panel.scope.destroyRef);
      const closeModal = ɵtmModalOpened(panel.element);
      // Closed however the test ends, so no later test runs under it.
      onTestFinished(closeModal);
      TestBed.tick();
      expect(shortcuts.active().map((shortcut) => shortcut.id)).toEqual(['acme.x.read']);
      press({ key: 'J', code: 'KeyJ', ctrlKey: true, shiftKey: true }, panel.element);
      expect(readCalls).toHaveBeenCalledWith({}, 'shortcut');
      expect(saved).toHaveBeenCalledTimes(1);

      // Closing the dialog gives the keys back to the page.
      closeModal();
      panel.destroy();
      TestBed.tick();
      expect(shortcuts.active().map((shortcut) => shortcut.id)).toEqual(['acme.a.save']);
      routed.destroy();
      TestBed.tick();
      expect(shortcuts.active()).toEqual([]);
    });

    it('refuses a disabled action pressed by its shortcut, announcing the reason', () => {
      const { announce } = rig();
      const action = tmTestAction('acme.a.approve', {
        shortcut: 'Mod+J',
        enabled: signal(false),
        disabledReason: signal('Already approved.'),
      });
      page(action);
      TestBed.tick();
      press({ key: 'j', code: 'KeyJ', ctrlKey: true });
      expect(announce).toHaveBeenCalledWith('Already approved.', 'polite');
      expect(action.calls).toEqual([]);
    });

    it('follows the top context as its actions change', () => {
      rig();
      const shortcuts = TestBed.inject(TmShortcuts);
      const context = tmTestContext('/:t/a', {}, [tmTestAction('acme.a.save', { shortcut: 'Mod+J' })]);
      TestBed.inject(TmPageContexts).register(context, tmLifetime().get(DestroyRef));
      TestBed.tick();
      context.actions.set([tmTestAction('acme.a.edit', { shortcut: 'Mod+Shift+J' })]);
      TestBed.tick();
      expect(shortcuts.active().map((shortcut) => shortcut.id)).toEqual(['acme.a.edit']);
    });

    it('gives no shortcut to an action whose arguments require members, in production', () => {
      rig(false);
      const shortcuts = TestBed.inject(TmShortcuts);
      const params: TmJsonObject = { type: 'object', required: ['limit'] };
      page(tmTestAction('acme.a.limit', { shortcut: 'Mod+J', params }), tmTestAction('acme.a.save', { shortcut: 'Mod+Shift+J' }));
      TestBed.tick();
      expect(shortcuts.active().map((shortcut) => shortcut.id)).toEqual(['acme.a.save']);
    });
  });

  describe('the real confirmation', () => {
    it("asks in the localized danger dialog, labels its button with the action's label, and runs only once confirmed", async () => {
      await tmAppRig();
      const fixture = TestBed.createComponent(TmTestScope);
      const { contexts, actions, destroyRef } = fixture.componentInstance;
      contexts.register(tmTestContext('/:t/centers/:id'), destroyRef);
      const runs = vi.fn(() => Promise.resolve<TmActionResult>({ outcome: 'done' }));
      actions.register(
        {
          id: 'acme.center.delete',
          labelKey: 'Acme_Center_Delete',
          destructive: true,
          confirm: {
            titleKey: 'Acme_Center_DeleteTitle',
            messageKey: 'Acme_Center_DeleteMessage',
            args: signal({ name: 'Main warehouse', count: 3 }),
          },
          invoke: runs,
        },
        destroyRef,
      );
      const loader = TestbedHarnessEnvironment.documentRootLoader(fixture);

      /** Invokes the action and waits for its question. */
      const ask = async (): Promise<{
        answer: Promise<TmActionResult>;
        dialog: TmConfirmHarness;
      }> => {
        const answer = actions.invoke('acme.center.delete', {}, 'user');
        // The question opens once the confirmation's entry point loads.
        await tmSettleUntil(() => document.querySelector(TmConfirmHarness.hostSelector) !== null);
        return { answer, dialog: await loader.getHarness(TmConfirmHarness) };
      };

      const first = await ask();
      expect(await first.dialog.getTitle()).toBe('Delete Main warehouse?');
      expect(await first.dialog.getMessage()).toBe('Main warehouse and its 3 children will be deleted.');
      expect(await first.dialog.getIcon()).toBe('warning');
      expect(await first.dialog.getChoiceLabels()).toContain('Delete center');
      // The danger form starts on the safe answer.
      expect(await first.dialog.getFocusedChoiceLabel()).not.toBe('Delete center');
      await first.dialog.choose('Delete center');
      expect(await first.answer).toEqual({ outcome: 'done' });
      expect(runs).toHaveBeenCalledTimes(1);

      const second = await ask();
      await second.dialog.pressEscape();
      expect(await second.answer).toEqual({ outcome: 'cancelled' });
      expect(runs).toHaveBeenCalledTimes(1);
    });
  });
});

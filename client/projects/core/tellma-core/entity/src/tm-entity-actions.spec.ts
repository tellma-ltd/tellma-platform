// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  TmApiError,
  TmDisplayNames,
  TmLocalization,
  type TmActionMetadata,
  type TmEntityActionDeclaration,
  type TmEntitiesChunk,
  type TmJsonObject,
} from '@tellma/core';
import { tmTestEntities, type TmFakeApiCall } from '@tellma/core-testing';
import { TmBusyOverlay } from '@tellma/core-ui/busy-overlay';

import { tmEntityRig, type TmEntityRig } from '../../src/entity/tm-entity.testing.util';
import { tmSettle, tmSettleUntil } from '../../src/routes/tm-app.testing.util';
import { TmConfirmations } from './tm-confirmations';
import { ɵtmActionBody } from './tm-action-run';
import { injectTmActionDialog, ɵtmLoad, TmEntityActions, type TmActionDialogContext, type TmEntityActionOutcome } from './tm-entity-actions';

// The entity actions over the entity registry's test app, whose customers
// take Acme's actions: which actions a page offers, in which order, and the
// development warning, the request body each takes, the confirmation loop
// (two warnings in one dialog, five rows per warning, continuing without the
// rows they name, again after a skip, the fifth warning, the key limit, a
// decline), the generated dialog of set-credit-limit with its argument and
// row errors and of hold with its yes/no, the busy overlay and the pending
// button, a declared dialog's context (staying open, a subset of rows, the
// failures it rethrows, a dismissal after a success, a declined warning), the
// failure alert's reference and wording, a job an action starts, a chunk that
// cannot load, and the default confirmation of a destructive action.

/** An entity action's metadata, with the defaults Acme's actions share. */
function action(name: string, patch: Partial<TmActionMetadata>): TmActionMetadata {
  return {
    name,
    kind: 'entity',
    builtIn: false,
    action: null,
    labelKey: `Acme_Customer_Action_${name}`,
    mutation: true,
    idempotent: false,
    destructive: false,
    singleTarget: false,
    supportsFilter: false,
    result: 'own',
    ...patch,
  };
}

/**
 * The recorded Entities chunk, with Acme's customer actions: the built-in
 * ones, set-credit-limit as Acme declares it, an archive with no arguments, a
 * single-target run-now, an invite whose dialog is declared, a merge whose
 * argument takes no control, a hold with a required yes/no, and the API
 * actions.
 *
 * @returns The chunk.
 */
function acmeChunk(): TmEntitiesChunk {
  const { chunk } = tmTestEntities();
  return {
    ...chunk,
    entities: chunk.entities.map((entity) => {
      if (entity.name !== 'acme.Customer') {
        return entity;
      }
      const builtIn = entity.actions.filter((candidate) => candidate.builtIn);
      const api = entity.actions.filter((candidate) => candidate.kind === 'api');
      return {
        ...entity,
        actions: [
          ...builtIn,
          action('set-credit-limit', {
            action: 'SetCreditLimit',
            labelFallback: 'Set credit limit',
            allowWhenReadOnly: true,
            supportsFilter: true,
            result: 'entities',
            arguments: { type: 'object' },
            fields: [
              {
                name: 'creditLimit',
                labelKey: 'Acme_Customer_CreditLimit',
                labelFallback: 'Credit limit',
                required: true,
                value: { type: 'decimal', nullable: false, scale: 4 },
              },
            ],
          }),
          action('archive', { labelFallback: 'Archive', destructive: true, result: 'none' }),
          action('run-now', { labelFallback: 'Run now', singleTarget: true }),
          action('invite', {
            labelFallback: 'Invite',
            arguments: { type: 'object' },
            fields: [{ name: 'message', labelKey: 'Invite_Message', required: false, value: { type: 'string', nullable: true } }],
          }),
          action('merge', {
            labelFallback: 'Merge',
            arguments: { type: 'object' },
            fields: [{ name: 'map', labelKey: 'Merge_Map', required: true, value: { type: 'json', nullable: false } }],
          }),
          action('hold', {
            labelFallback: 'Hold',
            arguments: { type: 'object' },
            fields: [{ name: 'notify', labelKey: 'Hold_Notify', labelFallback: 'Notify the customer', required: true, value: { type: 'bool', nullable: false } }],
          }),
          ...api,
        ],
      };
    }),
  };
}

/** The context the declared invite dialog was opened with, for the test to drive. */
let invite: TmActionDialogContext<{ message: string }> | null = null;

/** The invite action's declared dialog: it hands the test its context. */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<p class="stub-invite">Invite {{ context.ids.length }}</p>`,
})
class TmStubInviteDialog {
  /** The dialog's context. */
  readonly context = injectTmActionDialog<{ message: string }>();

  /** Hands the context to the test. */
  constructor() {
    invite = this.context;
  }
}

/** Acme's customer action declarations, with the invite dialog. */
const DECLARATIONS: readonly TmEntityActionDeclaration<'acme.Customer'>[] = [
  { name: 'set-credit-limit', button: true },
  { name: 'invite', dialog: () => Promise.resolve(TmStubInviteDialog) },
  { name: 'archive', visible: (customer) => (customer as { code?: string }).code !== 'LOCKED' },
];

/** A started test. */
interface TmActionsTest {
  /** The rig. */
  readonly rig: TmEntityRig;
  /** The entity actions. */
  readonly actions: TmEntityActions;
}

/**
 * Starts the app as a system user, with Acme's actions and three known
 * customers.
 *
 * @param declarations - The customer's action declarations.
 * @returns The test.
 */
async function start(declarations: readonly TmEntityActionDeclaration<'acme.Customer'>[] = DECLARATIONS): Promise<TmActionsTest> {
  invite = null;
  const rig = await tmEntityRig({ access: { isSystem: true }, chunk: acmeChunk(), actions: declarations });
  TestBed.inject(TmDisplayNames).remember('acme.Customer', [
    { id: 1, name: 'Bayt Foods' },
    { id: 2, name: 'Cedar Mills' },
    { id: 3, name: 'Delta Traders' },
    { id: 4, name: 'Eagle Supplies' },
    { id: 5, name: 'Falcon Rail' },
    { id: 6, name: 'Gulf Paper' },
    { id: 77, name: 'Acme Trading' },
  ]);
  return { rig, actions: TestBed.inject(TmEntityActions) };
}

/** The open modals, topmost last. */
function modals(): HTMLElement[] {
  return [...document.querySelectorAll<HTMLElement>('tm-modal-shell')];
}

/** The topmost modal. */
function top(): HTMLElement {
  const all = modals();
  expect(all.length).toBeGreaterThan(0);
  return all.at(-1)!;
}

/**
 * Clicks a button of the topmost modal by its text, and settles.
 *
 * @param text - The button's text.
 */
async function click(text: string): Promise<void> {
  const button = [...top().querySelectorAll<HTMLButtonElement>('button')].find((candidate) => candidate.textContent?.trim() === text);
  expect(button, `a button "${text}"`).toBeDefined();
  button!.click();
  await tmSettle();
}

/**
 * A button of the topmost modal, by its text.
 *
 * @param text - The button's text.
 * @returns The button.
 */
function button(text: string): HTMLButtonElement {
  const found = [...top().querySelectorAll<HTMLButtonElement>('button')].find((candidate) => candidate.textContent?.trim() === text);
  expect(found, `a button "${text}"`).toBeDefined();
  return found!;
}

/** Presses Escape in the topmost modal. */
async function escape(): Promise<void> {
  top().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
  await tmSettle();
}

/**
 * A 500 with its reference.
 *
 * @param traceId - The call's trace id.
 * @returns The error.
 */
function serverError(traceId: string): TmApiError {
  return new TmApiError({
    kind: 'server',
    status: 500,
    code: 'internal-error',
    traceId,
    problem: { code: 'internal-error', status: 500, instance: traceId, title: 'Something went wrong on our side' },
  });
}

/**
 * A 422 naming errors.
 *
 * @param errors - The validation items.
 * @returns The error.
 */
function invalid(...errors: readonly TmJsonObject[]): TmApiError {
  return new TmApiError({
    kind: 'validation',
    status: 422,
    code: 'validation',
    traceId: 'trace-2',
    problem: { code: 'validation', status: 422, instance: 'trace-2', title: 'The request has errors', errors: [...errors] },
  });
}

/**
 * Types into the topmost modal's first input, as a user does.
 *
 * @param text - The text.
 */
async function type(text: string): Promise<void> {
  const input = top().querySelector<HTMLInputElement>('input')!;
  input.focus();
  input.value = text;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  input.blur();
  await tmSettle();
}

/**
 * A 409 `confirmation-required` naming warnings.
 *
 * @param warnings - The warnings.
 * @returns The error.
 */
function warning(...warnings: readonly TmJsonObject[]): TmApiError {
  return new TmApiError({
    kind: 'confirmation',
    status: 409,
    code: 'confirmation-required',
    problem: {
      code: 'confirmation-required',
      status: 409,
      instance: 'trace-1',
      title: 'Confirmation required',
      detail: 'The new limit is above the approval threshold.',
      errors: [],
      errorDetails: { confirmations: [...warnings] },
    },
  });
}

/** The calls to an action's route. */
function callsTo(rig: TmEntityRig, route: string): readonly TmFakeApiCall[] {
  return rig.api.calls().filter((call) => call.route === route);
}

/** The keys a call confirmed. */
function confirmed(call: TmFakeApiCall): readonly string[] {
  return (call.options as { confirmations?: readonly string[] }).confirmations ?? [];
}

afterEach(() => {
  vi.restoreAllMocks();
  // A test that failed midway leaves its modals open; the next one starts clean.
  document.querySelectorAll('.cdk-overlay-container').forEach((container) => container.replaceChildren());
});

describe('TmEntityActions', () => {
  describe('the actions a page offers', () => {
    it('offers its own actions that a dialog can take, declared ones first in their order, as visible admits them, and warns of the rest once', async () => {
      const warnings = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
      const { actions } = await start();
      const names = (list: readonly { readonly name: string }[]): string[] => list.map((offered) => offered.name);

      // The declaration lists invite before archive, which the metadata lists first.
      expect(names(actions.offered('acme.Customer'))).toEqual(['set-credit-limit', 'invite', 'archive', 'run-now', 'hold']);
      expect(names(actions.offered('acme.Customer', { record: { id: 5, code: 'LOCKED' } }))).toEqual(['set-credit-limit', 'invite', 'run-now', 'hold']);
      // A selection never offers a single-target action, and visible is not read.
      expect(names(actions.offered('acme.Customer', { bulk: true }))).toEqual(['set-credit-limit', 'invite', 'archive', 'hold']);

      // Once per entity: merge and the API actions, none of them declared.
      actions.offered('acme.Customer');
      const reported = warnings.mock.calls.map(([message]) => String(message)).filter((message) => message.includes('acme.Customer'));
      expect(reported.length).toBe(1);
      expect(reported[0]).toContain("'merge'");
      expect(reported[0]).toContain("'statement'");
      expect(reported[0]).not.toContain("'set-credit-limit'");
      expect(reported[0]).not.toContain("'activate'");
      expect(reported[0]).not.toContain("'hold'");
    });

    it('takes a declaration entry as leaving an action out on purpose, and bulk: false out of a selection', async () => {
      const warnings = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
      const { actions } = await start([
        { name: 'merge', visible: () => false },
        { name: 'set-credit-limit', bulk: false },
      ]);
      // Invite, undeclared here, is offered through its generated dialog.
      expect(actions.offered('acme.Customer').map((offered) => offered.name)).toEqual(['set-credit-limit', 'archive', 'run-now', 'invite', 'hold']);
      expect(actions.offered('acme.Customer', { bulk: true }).map((offered) => offered.name)).toEqual(['archive', 'invite', 'hold']);
      const reported = warnings.mock.calls.map(([message]) => String(message)).find((message) => message.includes('acme.Customer'))!;
      expect(reported).not.toContain("'merge'");
      expect(reported).toContain("'statement'");
    });
  });

  describe('the request body', () => {
    const entities = { name: 'set-credit-limit', result: 'entities', singleTarget: false } as const;

    it('sends ids, returnEntities and the extras to an action that returns entities', () => {
      expect(ɵtmActionBody(entities, [77], { arguments: { creditLimit: '250000' } }, ['permissions'])).toEqual({
        ids: [77],
        returnEntities: true,
        include: ['permissions'],
        arguments: { creditLimit: '250000' },
      });
      expect(ɵtmActionBody(entities, [1, 2], { include: ['balance'] }, ['permissions'])).toEqual({ ids: [1, 2], returnEntities: true, include: ['balance'] });
      // A bulk caller re-reads its list: no include without the entities.
      expect(ɵtmActionBody(entities, [1, 2], { returnEntities: false }, ['permissions'])).toEqual({ ids: [1, 2], returnEntities: false });
    });

    it('sends the one id of a single-target action, and the ids of any other', () => {
      const single = { name: 'run-now', result: 'own', singleTarget: true } as const;
      expect(ɵtmActionBody(single, [7], {}, ['permissions'])).toEqual({ id: 7 });
      expect(ɵtmActionBody(single, [7], { arguments: { at: 'now' } }, [])).toEqual({ id: 7, arguments: { at: 'now' } });
      expect(() => ɵtmActionBody(single, [7, 8], {}, [])).toThrowError(/exactly one record/);
      const other = { name: 'archive', result: 'none', singleTarget: false } as const;
      expect(ɵtmActionBody(other, [7, 8], { returnEntities: true, include: ['x'] }, ['permissions'])).toEqual({ ids: [7, 8] });
    });
  });

  describe('the generated dialog', () => {
    it("opens set-credit-limit's dialog and posts the limit for customer 77", async () => {
      const { rig, actions } = await start();
      rig.api.respond('customers/set-credit-limit', { ids: [77], entities: [{ id: 77, creditLimit: '250000' }], extras: { permissions: {} } });

      const opened = actions.dialog('acme.Customer', 'set-credit-limit', [77]);
      await tmSettleUntil(() => modals().length === 1);
      const modal = top();
      expect(modal.querySelector('.tm-modal__title, [id^="tm-modal-title"]')?.textContent?.trim() ?? modal.textContent).toContain('Set credit limit');
      expect(modal.querySelectorAll('tm-form-field').length).toBe(1);
      expect(modal.querySelector('tm-form-field')!.textContent).toContain('Credit limit');

      // Required: the action does not run while the limit is empty.
      await click('Set credit limit');
      expect(callsTo(rig, 'customers/set-credit-limit')).toEqual([]);

      await type('250000');
      await click('Set credit limit');
      const outcome = await opened;
      const [call] = callsTo(rig, 'customers/set-credit-limit');
      expect(JSON.stringify(call!.body)).toBe('{"ids":[77],"returnEntities":true,"include":["permissions"],"arguments":{"creditLimit":"250000"}}');
      expect(outcome).toEqual({ kind: 'entities', result: { ids: [77], entities: [{ id: 77, creditLimit: '250000' }], extras: { permissions: {} } } });
      await tmSettleUntil(() => modals().length === 0);
    });

    it("lands an argument's error on its field and lists a row's in the alert, keeping the dialog open", async () => {
      const { rig, actions } = await start();
      let attempts = 0;
      rig.api.respond('customers/set-credit-limit', () => {
        attempts++;
        throw new TmApiError({
          kind: 'validation',
          status: 422,
          code: 'validation',
          problem: {
            code: 'validation',
            status: 422,
            instance: 'trace-2',
            title: 'The request has errors',
            errors: [
              { path: 'arguments.creditLimit', code: 'Range', message: 'Must be at most 1,000,000.' },
              { path: 'ids[1]', code: 'Acme.Customer.Blocked', message: 'The customer is blocked.' },
            ],
          },
        });
      });

      const opened = actions.dialog('acme.Customer', 'set-credit-limit', [77, 2]);
      await tmSettleUntil(() => modals().length === 1);
      await type('5000000');
      await click('Set credit limit');
      await tmSettleUntil(() => top().querySelector('tm-alert') !== null);

      const modal = top();
      expect(modal.querySelector('tm-form-field')!.textContent).toContain('Must be at most 1,000,000.');
      const alert = modal.querySelector('tm-alert')!.textContent!;
      expect(alert).toContain('The request has errors');
      expect(alert).toContain('Cedar Mills: The customer is blocked.');
      expect(alert).not.toContain('Must be at most');

      // Editing the limit clears its error; the next run replaces the alert.
      await type('900000');
      expect(top().querySelector('tm-form-field')!.textContent).not.toContain('Must be at most 1,000,000.');
      await click('Set credit limit');
      expect(attempts).toBe(2);

      await click('Cancel');
      expect(await opened).toBeNull();
    });

    it('shows an argument error while the field holds the value it was sent for, and clears it on the next run', async () => {
      const { rig, actions } = await start();
      const answers: (() => unknown)[] = [
        () => {
          throw invalid({ path: 'arguments.creditLimit', code: 'Range', message: 'Must be at most 1,000,000.' });
        },
        () => {
          throw serverError('trace-9');
        },
      ];
      rig.api.respond('customers/set-credit-limit', () => answers.shift()!());
      const field = (): string => top().querySelector('tm-form-field')!.textContent!;

      void actions.dialog('acme.Customer', 'set-credit-limit', [77]);
      await tmSettleUntil(() => modals().length === 1);
      await type('5000000');
      await click('Set credit limit');
      await tmSettleUntil(() => field().includes('Must be at most 1,000,000.'));
      // Every error sits on a field: no alert.
      expect(top().querySelector('tm-alert')).toBeNull();

      // Editing clears it, and undoing the edit brings it back.
      await type('900000');
      expect(field()).not.toContain('Must be at most');
      await type('5000000');
      expect(field()).toContain('Must be at most 1,000,000.');

      // The next run, failing elsewhere, clears it though the field was not edited.
      await click('Set credit limit');
      await tmSettleUntil(() => top().querySelector('tm-alert') !== null);
      expect(field()).not.toContain('Must be at most');
      const alert = top().querySelector('tm-alert')!;
      expect(alert.textContent).toContain('Something went wrong on our side');
      expect(alert.querySelector('tm-reference')!.textContent).toContain('trace-9');
    });

    it('sends a required yes/no argument left at no', async () => {
      const { rig, actions } = await start();
      rig.api.respond('customers/hold', null);
      const opened = actions.dialog('acme.Customer', 'hold', [1]);
      await tmSettleUntil(() => modals().length === 1);
      expect(top().textContent).toContain('Notify the customer');
      await click('Hold');
      expect(await opened).toEqual({ kind: 'own', result: null });
      expect(callsTo(rig, 'customers/hold')[0]!.body).toEqual({ ids: [1], arguments: { notify: false } });
    });

    it("shows the action pending under the busy overlay until the answer, in the click's frame", async () => {
      const { rig, actions } = await start();
      let fail: ((error: unknown) => void) | undefined;
      rig.api.respond('customers/hold', () => new Promise((_, reject) => (fail = reject)));
      const busy = TestBed.inject(TmBusyOverlay);

      void actions.dialog('acme.Customer', 'hold', [1]);
      await tmSettleUntil(() => modals().length === 1);
      expect(button('Hold').getAttribute('aria-busy')).toBeNull();
      button('Hold').click();
      expect(busy.active()).toBe(true);
      await tmSettle();
      expect(button('Hold').getAttribute('aria-busy')).toBe('true');
      expect(button('Hold').classList).toContain('tm-button--pending');

      await tmSettleUntil(() => fail !== undefined);
      fail!(serverError('trace-3'));
      await tmSettleUntil(() => top().querySelector('tm-alert') !== null);
      expect(busy.active()).toBe(false);
      expect(button('Hold').getAttribute('aria-busy')).toBeNull();
    });

    it('names a row error by the rows actually sent, after the user left one out', async () => {
      const { rig, actions } = await start();
      rig.api.respond('customers/set-credit-limit', (call: TmFakeApiCall) => {
        if ((call.body as { ids: readonly number[] }).ids.includes(2)) {
          throw warning({ key: 'Acme.CreditReviewPending', ids: [2] });
        }
        throw invalid({ path: 'ids[1]', code: 'Acme.Customer.Blocked', message: 'The customer is blocked.' });
      });

      void actions.dialog('acme.Customer', 'set-credit-limit', [1, 2, 3]);
      await tmSettleUntil(() => modals().length === 1);
      await type('250000');
      await click('Set credit limit');
      await tmSettleUntil(() => modals().length === 2);
      await click('Continue without this one');
      await tmSettleUntil(() => modals().length === 1 && top().querySelector('tm-alert') !== null);

      // ids[1] of [1, 3] is Delta Traders, not Cedar Mills, the second row asked for.
      const alert = top().querySelector('tm-alert')!.textContent!;
      expect(alert).toContain('Delta Traders: The customer is blocked.');
      expect(alert).not.toContain('Cedar Mills');
      expect(callsTo(rig, 'customers/set-credit-limit').map((call) => (call.body as { ids: unknown }).ids)).toEqual([
        [1, 2, 3],
        [1, 3],
      ]);
    });

    it('runs an action with no arguments at once, with no dialog', async () => {
      const { rig, actions } = await start();
      rig.api.respond('customers/archive', null);
      expect(await actions.dialog('acme.Customer', 'archive', [1, 2])).toEqual({ kind: 'own', result: null });
      expect(modals()).toEqual([]);
      expect(callsTo(rig, 'customers/archive')[0]!.body).toEqual({ ids: [1, 2] });
    });

    it('refuses an action whose arguments no dialog can take', async () => {
      const { actions } = await start();
      await expect(actions.dialog('acme.Customer', 'merge', [1])).rejects.toThrowError(/needs a declared dialog/);
    });
  });

  describe('a declared dialog', () => {
    it('runs a subset and stays open, rethrows a partial failure and lists a server error', async () => {
      const { rig, actions } = await start();
      const answers: (() => unknown)[] = [
        () => ({ invited: 1 }),
        () => {
          throw new TmApiError({ kind: 'partial-failure', status: 207, code: 'partial-failure', problem: { code: 'partial-failure', status: 207, instance: 't' } });
        },
        () => {
          throw new TmApiError({
            kind: 'server',
            status: 500,
            code: 'internal-error',
            problem: { code: 'internal-error', status: 500, instance: 't', title: 'Something went wrong on our side' },
          });
        },
      ];
      rig.api.respond('customers/invite', () => answers.shift()!());

      const opened = actions.dialog('acme.Customer', 'invite', [1, 2, 3], { arguments: { message: 'Welcome' } });
      await tmSettleUntil(() => invite !== null);
      const context = invite!;
      expect(context.entity).toBe('acme.Customer');
      expect(context.action.name).toBe('invite');
      expect(context.ids).toEqual([1, 2, 3]);
      expect(context.initial).toEqual({ message: 'Welcome' });
      expect(top().textContent).toContain('Invite 3');

      // Success, kept open, on a subset.
      const first = await context.run({ message: 'Hi' }, { keepOpen: true, ids: [2], rethrow: ['partial-failure'] });
      expect(first).toEqual({ kind: 'own', result: { invited: 1 } });
      expect(callsTo(rig, 'customers/invite')[0]!.body).toEqual({ ids: [2], arguments: { message: 'Hi' } });
      expect(modals().length).toBe(1);

      // A kind it rethrows: rejected, nothing listed.
      const partial = await context.run({ message: 'Hi' }, { keepOpen: true, rethrow: ['partial-failure'] }).catch((error: unknown) => error);
      expect((partial as TmApiError).kind).toBe('partial-failure');
      await tmSettle();
      expect(top().querySelector('tm-alert')).toBeNull();

      // Any other failure lists in the alert atop the dialog.
      expect(await context.run({ message: 'Hi' }, { keepOpen: true, rethrow: ['partial-failure'] })).toBeNull();
      await tmSettleUntil(() => top().querySelector('tm-alert') !== null);
      expect(top().querySelector('tm-alert')!.textContent).toContain('Something went wrong on our side');

      // Closed with its X, the dialog still reports the run that succeeded, so the page re-reads.
      top().querySelector<HTMLButtonElement>('.tm-modal__close')!.click();
      const outcome: TmEntityActionOutcome = { kind: 'own', result: { invited: 1 } };
      expect(await opened).toEqual(outcome);
    });

    it('marks its run running under the busy overlay, and reports a success after Escape', async () => {
      const { rig, actions } = await start();
      let answer: ((value: unknown) => void) | undefined;
      rig.api.respond('customers/invite', () => new Promise((resolve) => (answer = resolve)));
      const busy = TestBed.inject(TmBusyOverlay);

      const opened = actions.dialog('acme.Customer', 'invite', [1]);
      await tmSettleUntil(() => invite !== null);
      const context = invite!;
      expect(context.running()).toBe(false);
      const running = context.run({ message: 'Hi' }, { keepOpen: true });
      expect(context.running()).toBe(true);
      expect(busy.active()).toBe(true);
      await tmSettleUntil(() => answer !== undefined);
      expect(context.running()).toBe(true);

      answer!({ invited: 1 });
      expect(await running).toEqual({ kind: 'own', result: { invited: 1 } });
      expect(context.running()).toBe(false);
      expect(busy.active()).toBe(false);

      await escape();
      expect(await opened).toEqual({ kind: 'own', result: { invited: 1 } });
    });

    it('keeps the dialog open on a declined warning, and resolves declined', async () => {
      const { rig, actions } = await start();
      rig.api.respond('customers/invite', () => {
        throw warning({ key: 'Acme.AlreadyInvited', ids: [1] });
      });
      const opened = actions.dialog('acme.Customer', 'invite', [1]);
      await tmSettleUntil(() => invite !== null);
      const running = invite!.run({ message: 'Hi' });
      await tmSettleUntil(() => modals().length === 2);
      await click('Cancel');
      expect(await running).toEqual({ kind: 'declined' });
      expect(modals().length).toBe(1);
      expect(top().querySelector('tm-alert')).toBeNull();

      // Nothing ran: closing reports no outcome.
      invite!.close();
      expect(await opened).toBeNull();
    });

    it("words an unknown outcome apart from offline, with the call's reference", async () => {
      const { rig, actions } = await start();
      const answers = [
        new TmApiError({ kind: 'offline', code: 'outcome-unknown', traceId: 'trace-7' }),
        new TmApiError({ kind: 'offline', code: 'network', traceId: 'trace-8' }),
      ];
      rig.api.respond('customers/invite', () => {
        throw answers.shift()!;
      });
      void actions.dialog('acme.Customer', 'invite', [1]);
      await tmSettleUntil(() => invite !== null);

      expect(await invite!.run({ message: 'Hi' })).toBeNull();
      await tmSettleUntil(() => top().querySelector('tm-alert') !== null);
      let alert = top().querySelector('tm-alert')!;
      expect(alert.textContent).toContain("We couldn't confirm this");
      expect(alert.textContent).toContain('Check whether it went through');
      expect(alert.querySelector('tm-reference')!.textContent).toContain('trace-7');

      expect(await invite!.run({ message: 'Hi' })).toBeNull();
      await tmSettle();
      alert = top().querySelector('tm-alert')!;
      expect(alert.textContent).toContain("You're offline");
      expect(alert.textContent).not.toContain("We couldn't confirm this");
      expect(alert.querySelector('tm-reference')!.textContent).toContain('trace-8');
    });

    it('throws outside an action dialog', async () => {
      await start();
      expect(() => TestBed.runInInjectionContext(() => injectTmActionDialog())).toThrowError(/outside an action dialog/);
    });
  });

  describe('the confirmation loop', () => {
    it('confirms two warnings in one dialog and resends with both keys', async () => {
      const { rig, actions } = await start();
      rig.api.respond('customers/set-credit-limit', (call: TmFakeApiCall) => {
        if (confirmed(call).length === 0) {
          throw warning(
            { key: 'Acme.CreditAboveThreshold', ids: [77], arguments: { threshold: 100000 } },
            { key: 'Acme.CreditReviewPending', ids: [77] },
          );
        }
        return { ids: [77], entities: [{ id: 77 }] };
      });

      const running = actions.run('acme.Customer', 'set-credit-limit', [77], { arguments: { creditLimit: '250000' } });
      await tmSettleUntil(() => modals().length === 1);
      const dialog = top();
      expect(dialog.closest('[role="alertdialog"]')).not.toBeNull();
      expect(dialog.textContent).toContain('Confirm to continue');
      expect(dialog.textContent).toContain('The new limit is above the approval threshold.');
      expect(dialog.textContent).toContain('Acme.CreditReviewPending');
      expect(dialog.textContent).toContain('Acme Trading');
      // Cancel holds focus; no "Continue without these" for a single row.
      expect(document.activeElement?.textContent?.trim()).toBe('Cancel');
      expect([...dialog.querySelectorAll('button')].map((button) => button.textContent!.trim())).toContain('Continue');
      expect(dialog.textContent).not.toContain('without');

      await click('Continue');
      expect((await running).kind).toBe('entities');
      const calls = callsTo(rig, 'customers/set-credit-limit');
      expect(calls.length).toBe(2);
      expect(confirmed(calls[1]!)).toEqual(['Acme.CreditAboveThreshold', 'Acme.CreditReviewPending']);
    });

    it('continues a bulk run without the rows the warnings name', async () => {
      const { rig, actions } = await start();
      rig.api.respond('customers/archive', (call: TmFakeApiCall) => {
        if ((call.body as { ids: readonly number[] }).ids.includes(2)) {
          throw warning({ key: 'Acme.OpenOrders', ids: [2] });
        }
        return null;
      });

      const running = actions.run('acme.Customer', 'archive', [1, 2, 3], { returnEntities: false });
      await tmSettleUntil(() => modals().length === 1);
      expect(top().textContent).toContain('Cedar Mills');
      await click('Continue without this one');
      expect(await running).toEqual({ kind: 'own', result: null });
      const calls = callsTo(rig, 'customers/archive');
      expect(calls.map((call) => call.body)).toEqual([{ ids: [1, 2, 3] }, { ids: [1, 3] }]);
      expect(confirmed(calls[1]!)).toEqual([]);
    });

    it('names five rows of a warning, then how many more', async () => {
      const { rig, actions } = await start();
      rig.api.respond('customers/archive', () => {
        throw warning({ key: 'Acme.OpenOrders', ids: [1, 2, 3, 4, 5, 6, 77] });
      });
      const running = actions.run('acme.Customer', 'archive', [1]);
      await tmSettleUntil(() => modals().length === 1);
      const rows = [...top().querySelectorAll('.tm-confirmations ul li')].map((row) => row.textContent!.trim());
      expect(rows).toEqual(['Bayt Foods', 'Cedar Mills', 'Delta Traders', 'Eagle Supplies', 'Falcon Rail', 'and 2 more']);
      await click('Cancel');
      expect(await running).toEqual({ kind: 'declined' });
    });

    it('offers to continue without rows again after a skip, counting only the rows still sent', async () => {
      const { rig, actions } = await start();
      rig.api.respond('customers/archive', (call: TmFakeApiCall) => {
        const ids = (call.body as { ids: readonly number[] }).ids;
        if (ids.includes(2)) {
          throw warning({ key: 'Acme.OpenOrders', ids: [2] });
        }
        if (ids.includes(3)) {
          // It names the row already left out too, which no longer counts.
          throw warning({ key: 'Acme.Overdue', ids: [2, 3] });
        }
        return null;
      });

      const running = actions.run('acme.Customer', 'archive', [1, 2, 3], { returnEntities: false });
      await tmSettleUntil(() => modals().length === 1);
      await click('Continue without this one');
      await tmSettleUntil(() => callsTo(rig, 'customers/archive').length === 2 && modals().length === 1);
      await click('Continue without this one');
      expect(await running).toEqual({ kind: 'own', result: null });
      expect(callsTo(rig, 'customers/archive').map((call) => call.body)).toEqual([{ ids: [1, 2, 3] }, { ids: [1, 3] }, { ids: [1] }]);
    });

    it('fails a run that asks for more than 16 confirmations as a server error, sending nothing more', async () => {
      const { rig, actions } = await start();
      const keys = Array.from({ length: 17 }, (_, index) => ({ key: `Acme.Warning${index + 1}`, ids: [] }));
      rig.api.respond('customers/archive', () => {
        throw warning(...keys);
      });
      const running = actions.run('acme.Customer', 'archive', [1]).catch((error: unknown) => error);
      await tmSettleUntil(() => modals().length === 1);
      await click('Continue');
      const error = (await running) as TmApiError;
      expect(error.kind).toBe('server');
      expect(error.message).toMatch(/more than 16 confirmations/);
      expect(callsTo(rig, 'customers/archive').length).toBe(1);
    });

    it('fails a fifth warning in one run as a server error', async () => {
      const { rig, actions } = await start();
      let round = 0;
      rig.api.respond('customers/archive', () => {
        round++;
        throw warning({ key: `Acme.Warning${round}`, ids: [1] });
      });

      const running = actions.run('acme.Customer', 'archive', [1]).catch((error: unknown) => error);
      for (let answered = 0; answered < 4; answered++) {
        await tmSettleUntil(() => modals().length === 1);
        await click('Continue');
        await tmSettleUntil(() => modals().length === 0 || round > answered + 1);
      }
      const error = (await running) as TmApiError;
      expect(error).toBeInstanceOf(TmApiError);
      expect(error.kind).toBe('server');
      expect(error.status).toBe(409);
      expect(callsTo(rig, 'customers/archive').length).toBe(5);
      expect(confirmed(callsTo(rig, 'customers/archive')[4]!)).toEqual(['Acme.Warning1', 'Acme.Warning2', 'Acme.Warning3', 'Acme.Warning4']);
    });

    it('resolves declined when the user cancels, sending nothing more', async () => {
      const { rig, actions } = await start();
      rig.api.respond('customers/archive', () => {
        throw warning({ key: 'Acme.OpenOrders', ids: [1] });
      });
      const running = actions.run('acme.Customer', 'archive', [1]);
      await tmSettleUntil(() => modals().length === 1);
      await click('Cancel');
      expect(await running).toEqual({ kind: 'declined' });
      expect(callsTo(rig, 'customers/archive').length).toBe(1);
    });

    it('sends the keys already confirmed from the first try, and passes other failures through', async () => {
      const { rig } = await start();
      const confirmations = TestBed.inject(TmConfirmations);
      rig.api.respond('customers/archive', () => null);
      const send = (attempt: { readonly confirmations: readonly string[] }): Promise<unknown> =>
        rig.api.write('customers/archive', { ids: [1] }, { confirmations: attempt.confirmations });
      await confirmations.run(send, { confirmations: ['Acme.Known'] });
      expect(confirmed(callsTo(rig, 'customers/archive')[0]!)).toEqual(['Acme.Known']);

      const failure = new TmApiError({ kind: 'not-found', status: 404 });
      await expect(confirmations.run(() => Promise.reject(failure))).rejects.toBe(failure);
    });
  });

  describe('outcomes', () => {
    it('reports a job a single-target action started, and its own result otherwise', async () => {
      const { rig, actions } = await start();
      rig.api.respond('customers/run-now', { jobId: 12, resourceId: 77 });
      expect(await actions.run('acme.Customer', 'run-now', [77])).toEqual({ kind: 'job', jobId: 12 });
      expect(callsTo(rig, 'customers/run-now')[0]!.body).toEqual({ id: 77 });

      rig.api.respond('customers/run-now', { jobId: 12, resourceId: 77, queued: true });
      expect(await actions.run('acme.Customer', 'run-now', [77])).toEqual({ kind: 'own', result: { jobId: 12, resourceId: 77, queued: true } });
    });
  });

  describe('loading', () => {
    it('fails as offline, unsent, when a chunk cannot load', async () => {
      const error = await ɵtmLoad(() => Promise.reject(new TypeError('Failed to fetch dynamically imported module'))).catch((failure: unknown) => failure);
      expect(error).toBeInstanceOf(TmApiError);
      expect((error as TmApiError).kind).toBe('offline');
      expect((error as TmApiError).code).toBe('network');
    });
  });

  describe('the default confirmation', () => {
    it("asks to run the action's label on the rows, which can't be undone", async () => {
      const { actions } = await start();
      const confirmation = actions.confirmation('acme.Customer', 'archive', 3);
      expect(confirmation.titleKey).toBe('tm.entity.confirmAction');
      expect(confirmation.args!()).toEqual({ action: 'Archive', n: 3 });
      const localization = TestBed.inject(TmLocalization);
      expect(localization.ɵtext(confirmation.titleKey, confirmation.args!())()).toBe('Run “Archive” on 3 items?');
      expect(localization.ɵtext(confirmation.titleKey, { action: 'Archive', n: 1 })()).toBe('Run “Archive” on this item?');
      expect(localization.ɵtext(confirmation.messageKey)()).toBe("This can't be undone.");
      // The platform's own strings under the same scope still resolve.
      expect(localization.ɵtext('tm.entity.permission', { resource: 'Customers', action: 'Read' })()).toBe('Customers: Read');
    });
  });
});

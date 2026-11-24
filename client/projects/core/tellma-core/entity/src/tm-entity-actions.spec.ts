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

import { tmEntityRig, type TmEntityRig } from '../../src/entity/tm-entity.testing.util';
import { tmSettle } from '../../src/routes/tm-app.testing.util';
import { TmConfirmations } from './tm-confirmations';
import { ɵtmActionBody } from './tm-action-run';
import { injectTmActionDialog, TmEntityActions, type TmActionDialogContext, type TmEntityActionOutcome } from './tm-entity-actions';

// The entity actions over the entity registry's test app, whose customers
// take Acme's actions: which actions a page offers and the development
// warning, the request body each takes, the confirmation loop (two warnings
// in one dialog, continuing without the rows they name, the fifth warning,
// a decline), the generated dialog of set-credit-limit with its argument and
// row errors, a declared dialog's context (staying open, a subset of rows,
// the failures it rethrows), a job an action starts, and the default
// confirmation of a destructive action.

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
 * argument takes no control, and the API actions.
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
 * Settles until a condition holds, or fails.
 *
 * @param condition - The condition.
 */
async function until(condition: () => boolean): Promise<void> {
  for (let tries = 0; tries < 40 && !condition(); tries++) {
    await tmSettle();
  }
  expect(condition()).toBe(true);
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
    it('offers its own actions that a dialog can take, as visible admits them, and warns of the rest once', async () => {
      const warnings = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
      const { actions } = await start();
      const names = (list: readonly { readonly name: string }[]): string[] => list.map((offered) => offered.name);

      expect(names(actions.offered('acme.Customer'))).toEqual(['set-credit-limit', 'archive', 'run-now', 'invite']);
      expect(names(actions.offered('acme.Customer', { record: { id: 5, code: 'LOCKED' } }))).toEqual(['set-credit-limit', 'run-now', 'invite']);
      // A selection never offers a single-target action, and visible is not read.
      expect(names(actions.offered('acme.Customer', { bulk: true }))).toEqual(['set-credit-limit', 'archive', 'invite']);

      // Once per entity: merge and the API actions, none of them declared.
      actions.offered('acme.Customer');
      const reported = warnings.mock.calls.map(([message]) => String(message)).filter((message) => message.includes('acme.Customer'));
      expect(reported.length).toBe(1);
      expect(reported[0]).toContain("'merge'");
      expect(reported[0]).toContain("'statement'");
      expect(reported[0]).not.toContain("'set-credit-limit'");
      expect(reported[0]).not.toContain("'activate'");
    });

    it('takes a declaration entry as leaving an action out on purpose, and bulk: false out of a selection', async () => {
      const warnings = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
      const { actions } = await start([
        { name: 'merge', visible: () => false },
        { name: 'set-credit-limit', bulk: false },
      ]);
      // Invite, undeclared here, is offered through its generated dialog.
      expect(actions.offered('acme.Customer').map((offered) => offered.name)).toEqual(['set-credit-limit', 'archive', 'run-now', 'invite']);
      expect(actions.offered('acme.Customer', { bulk: true }).map((offered) => offered.name)).toEqual(['archive', 'invite']);
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
      await until(() => modals().length === 1);
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
      await until(() => modals().length === 0);
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
      await until(() => modals().length === 1);
      await type('5000000');
      await click('Set credit limit');
      await until(() => top().querySelector('tm-alert') !== null);

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
      await until(() => invite !== null);
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
      await until(() => top().querySelector('tm-alert') !== null);
      expect(top().querySelector('tm-alert')!.textContent).toContain('Something went wrong on our side');

      const outcome: TmEntityActionOutcome = { kind: 'own', result: { invited: 1 } };
      context.close(outcome);
      expect(await opened).toEqual(outcome);
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
      await until(() => modals().length === 1);
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
      await until(() => modals().length === 1);
      expect(top().textContent).toContain('Cedar Mills');
      await click('Continue without this one');
      expect(await running).toEqual({ kind: 'own', result: null });
      const calls = callsTo(rig, 'customers/archive');
      expect(calls.map((call) => call.body)).toEqual([{ ids: [1, 2, 3] }, { ids: [1, 3] }]);
      expect(confirmed(calls[1]!)).toEqual([]);
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
        await until(() => modals().length === 1);
        await click('Continue');
        await until(() => modals().length === 0 || round > answered + 1);
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
      await until(() => modals().length === 1);
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

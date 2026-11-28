// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Location } from '@angular/common';
import { Component, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { FieldTree } from '@angular/forms/signals';
import { manualChangeDetection } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { Router } from '@angular/router';
import type { RouterTestingHarness } from '@angular/router/testing';
import { TmApiError, type TmBlobDescriptor, type TmJsonObject, TmUrls } from '@tellma/core';
import { TmPageState, tmPageStateKey } from '@tellma/core/memory';
import { TmDetailsPageHarness, type TmDetailsSummaryHarness, type TmFakeApiCall, TmFieldHarness } from '@tellma/core-testing';

import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';

import { tmEntityRig, type TmEntityRig, type TmEntityRigOptions } from '../../src/entity/tm-entity.testing.util';
import { tmLoadTestPage, tmSettle, tmSettleUntil, TmTestPage } from '../../src/routes/tm-app.testing.util';
import { injectTmDetails, type TmDetailsContext } from './tm-details-context';
import { TmDetailsKit } from './tm-details-kit';
import { TM_DETAILS_RECORD, type TmDetailsRecordState } from './tm-details-record';

// The save flow on the routed details host over the recorded customer
// model, in a kit layout whose contacts sit on a tab not yet visited: the
// edit's write with its stamp, a clean Save, a create replacing its URL
// (and landing on a declared URL) with the saved copy and no read, the
// error summary over a 422 and over the client's own errors (an unvisited
// tab rendering for them), the concurrency dialog's choices and copy, the
// deletion dialog, the error presenter's calls, a conflict and a warning in
// one Save, the busy overlay's start, a failed upload, Cancel clearing the
// summary, and the leave guard.

/** A kit layout: four fields in a card; a tab holding Rank, then the contacts' tab, which is not visited. */
@Component({
  selector: 'tm-test-save-layout',
  imports: [TmDetailsKit],
  template: `
    <tm-details-card>
      <tm-field path="Code" />
      <tm-field path="Name" />
      <tm-field path="CustomerType" />
      <tm-field path="CreditLimit" />
    </tm-details-card>
    <tm-details-tabs>
      <tm-details-tab id="general" labelKey="Name">
        <ng-template tmDetailsTabContent><tm-field path="Rank" /></ng-template>
      </tm-details-tab>
      <tm-details-tab id="contacts" collection="Contacts">
        <ng-template tmDetailsTabContent><tm-child-grid collection="Contacts" [columns]="['Name', 'Email']" /></ng-template>
      </tm-details-tab>
    </tm-details-tabs>
  `,
})
class TmTestSaveLayout {
  /** The latest instance. */
  static latest: TmTestSaveLayout | null = null;

  /** The record. */
  readonly details: TmDetailsContext<TmJsonObject> = injectTmDetails<TmJsonObject>();

  /** The record's state behind it, whose form and uploads the test drives. */
  readonly record: TmDetailsRecordState<TmJsonObject> = inject(TM_DETAILS_RECORD) as TmDetailsRecordState<TmJsonObject>;

  /** Keeps itself for the test. */
  constructor() {
    TmTestSaveLayout.latest = this;
  }
}

/** The stamp the server sent with customer 5. */
const STAMP = '2026-11-20T08:00:00.0000000+00:00';

/**
 * A customer as `customers/get` and `customers/save` return it.
 *
 * @param id - The customer's id.
 * @param name - Its name.
 * @returns The result.
 */
function customer(id: number, name = `Customer ${id}`): TmJsonObject {
  return {
    entities: [
      {
        id,
        code: `C-00${id}`,
        name,
        customerType: 'Retail',
        creditLimit: '0',
        isActive: true,
        modifiedAt: STAMP,
        contacts: [{ id: 100 + id, customerId: id, name: 'Huda' }],
      },
    ],
    related: {},
    extras: { permissions: [] },
  };
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
    problem: { code: 'validation', status: 422, instance: 'trace-2', title: 'The request has errors', errors: [...errors] },
  });
}

/**
 * A 409 for a newer version.
 *
 * @param conflict - Who saved it, and when; nothing when unknown.
 * @returns The error.
 */
function conflict(conflict?: TmJsonObject): TmApiError {
  return new TmApiError({
    kind: 'conflict',
    status: 409,
    code: 'concurrency-conflict',
    problem: {
      code: 'concurrency-conflict',
      status: 409,
      instance: 'trace-3',
      title: 'The record changed',
      ...(conflict && { errorDetails: { conflicts: [conflict] } }),
    },
  });
}

/**
 * A 409 asking to confirm a warning on customer 5.
 *
 * @param key - The warning's key.
 * @returns The error.
 */
function warning(key: string): TmApiError {
  return new TmApiError({
    kind: 'confirmation',
    status: 409,
    code: 'confirmation-required',
    problem: {
      code: 'confirmation-required',
      status: 409,
      instance: 'trace-4',
      title: 'Confirmation required',
      detail: 'The limit is above the approval threshold.',
      errors: [],
      errorDetails: { confirmations: [{ key, ids: [5] }] },
    },
  });
}

/** The grant of a customer permission. */
function grant(action: string): { readonly resource: string; readonly action: string; readonly hasFilter: boolean } {
  return { resource: 'acme.Customer', action, hasFilter: false };
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

/** The topmost modal's button labels, in order. */
function choices(): string[] {
  return [...top().querySelectorAll<HTMLButtonElement>('button')].map((button) => button.textContent!.trim()).filter((label) => label !== '');
}

/**
 * Clicks a button of the topmost modal by its text, and settles.
 *
 * @param text - The button's text.
 */
async function choose(text: string): Promise<void> {
  const button = [...top().querySelectorAll<HTMLButtonElement>('button')].find((candidate) => candidate.textContent?.trim() === text);
  expect(button, `a button "${text}"`).toBeDefined();
  button!.click();
  await tmSettle();
}

/** Presses Escape in the topmost modal, and settles. */
async function escape(): Promise<void> {
  top().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
  await tmSettle();
}

/**
 * The keys a write confirmed.
 *
 * @param call - The write.
 * @returns Its keys.
 */
function confirmed(call: TmFakeApiCall): readonly string[] {
  return (call.options as { confirmations?: readonly string[] }).confirmations ?? [];
}

/**
 * The record a write sent.
 *
 * @param call - The write.
 * @returns Its record.
 */
function sent(call: TmFakeApiCall): TmJsonObject {
  return (call.body as { readonly entities: readonly TmJsonObject[] }).entities[0]!;
}

/**
 * The concurrency choice a write sent.
 *
 * @param call - The write.
 * @returns `Check` or `Override`.
 */
function concurrency(call: TmFakeApiCall): unknown {
  return (call.body as { readonly concurrency?: unknown }).concurrency;
}

/**
 * The unit page loads no token stylesheet and no app stylesheet; the grid's
 * geometry, which its deferred load watches, needs the tokens, and the
 * page's band, which scrolls inside the frame, needs the height the app's
 * full-height layout gives the shell's main region and the frame in it.
 */
let tokenSheet: HTMLStyleElement;

beforeAll(() => {
  tokenSheet = document.createElement('style');
  tokenSheet.textContent = [tmEmitCss(tmTokensDefault), '.tm-shell-frame__main { block-size: 760px !important; } .tm-details { block-size: 100% !important; }'].join('\n');
  document.head.appendChild(tokenSheet);
});

afterAll(() => tokenSheet.remove());

describe('the details save flow', () => {
  let rig: TmEntityRig;
  let router: RouterTestingHarness;

  /**
   * Starts the app with Read and Save on customers.
   *
   * @param declaration - More of the customer's declaration.
   */
  async function start(declaration: TmEntityRigOptions['customer'] = {}): Promise<void> {
    rig = await tmEntityRig({ access: { securables: [grant('Read'), grant('Save')] }, customer: declaration });
    rig.api.respond('customers/get', (call: TmFakeApiCall) => customer((call.body as { readonly id: number }).id));
    router = rig.router;
  }

  /** The writes so far. */
  function writes(): TmFakeApiCall[] {
    return rig.api.calls().filter((call) => call.route === 'customers/save');
  }

  /** The reads so far. */
  function reads(): TmFakeApiCall[] {
    return rig.api.calls().filter((call) => call.route === 'customers/get');
  }

  /** The layout. */
  function layout(): TmTestSaveLayout {
    return TmTestSaveLayout.latest!;
  }

  /** The record's form, by json name. */
  function form(): Readonly<Record<string, FieldTree<unknown>>> {
    return layout().details.form as unknown as Readonly<Record<string, FieldTree<unknown>>>;
  }

  /** The page's harness. */
  function page(): Promise<TmDetailsPageHarness> {
    return TestbedHarnessEnvironment.loader(router.fixture).getHarness(TmDetailsPageHarness);
  }

  /**
   * Opens customer 5 in edit mode, its controls loaded.
   *
   * @returns The page's harness.
   */
  async function editCustomer(): Promise<TmDetailsPageHarness> {
    await router.navigateByUrl('/17/customers/5');
    await tmSettleUntil(() => TmTestSaveLayout.latest?.details.status() === 'ready', 'the customer lands');
    const harness = await page();
    await harness.click('edit');
    await tmSettleUntil(() => document.querySelector('[data-tm-field="Code"] tm-form-field') !== null, 'the controls load');
    return harness;
  }

  /** The error summary, once a failed Save has opened it. */
  async function summary(): Promise<TmDetailsSummaryHarness> {
    await tmSettleUntil(() => document.querySelector('tm-details-summary') !== null, 'the summary opens');
    return (await (await page()).getErrorSummary())!;
  }

  /** What the page's polite status region says. */
  function live(): string {
    return document.querySelector('.tm-details__live')?.textContent?.trim() ?? '';
  }

  /**
   * The telemetry items of a name, by their properties.
   *
   * @param name - The item's name.
   * @returns Their properties, a timing's duration left out.
   */
  function telemetry(name: string): Readonly<Record<string, unknown>>[] {
    return rig.telemetry
      .recent()
      .filter((item) => item.name === name)
      .map(({ props }) => Object.fromEntries(Object.entries(props).filter(([key]) => key !== 'ms')));
  }

  /** Whether the page behind the overlay is inert. */
  function blocked(): boolean {
    return [...document.body.children].some((child) => (child as HTMLElement).inert);
  }

  /** Whether the overlay's scrim shows. */
  function scrim(): boolean {
    return document.querySelector('.tm-busy-overlay')?.classList.contains('tm-busy-overlay--scrim') ?? false;
  }

  /**
   * The label of a member, as the page shows it.
   *
   * @param entity - The entity.
   * @param name - The member.
   * @returns Its label.
   */
  function label(entity: string, name: string): string {
    const descriptor = rig.entities.get(entity);
    return (descriptor.property(name)?.label ?? descriptor.child(name)!.label)();
  }

  beforeEach(() => {
    TmTestSaveLayout.latest = null;
    tmLoadTestPage.mockImplementation(() => Promise.resolve(TmTestSaveLayout));
  });

  afterEach(() => {
    vi.restoreAllMocks();
    tmLoadTestPage.mockImplementation(() => Promise.resolve(TmTestPage));
    // A test that failed midway leaves its modals open; the next one starts clean.
    document.querySelectorAll('.cdk-overlay-container').forEach((container) => container.replaceChildren());
  });

  // --- Success ---

  it("saves an edit with the server's stamp under Check, shows the saved record, says Saved, focuses Edit and marks the lists stale", async () => {
    await start();
    const harness = await editCustomer();
    const pageState = TestBed.inject(TmPageState);
    const list = tmPageStateKey(TestBed.inject(TmUrls).search('acme.Customer'));
    pageState.remember(list, { rows: [] });
    form()['name']!().value.set('Renamed');
    rig.api.respond('customers/save', () => customer(5, 'Renamed'));

    await harness.click('save');
    await tmSettleUntil(() => layout().details.mode() === 'view', 'the save lands');

    expect(writes()).toHaveLength(1);
    expect(sent(writes()[0]!)).toEqual(expect.objectContaining({ id: 5, name: 'Renamed', modifiedAt: STAMP }));
    expect([concurrency(writes()[0]!), confirmed(writes()[0]!)]).toEqual(['Check', []]);
    expect(layout().details.entity()?.['name']).toBe('Renamed');
    expect(live()).toBe('Saved');
    expect(await (await harness.getButton('edit'))!.isFocused()).toBe(true);
    expect(pageState.isStale(list)).toBe(true);
    expect(telemetry('tm.details.save')).toEqual([{ mode: 'edit', outcome: 'ok', errors: '0' }]);
  });

  it('sends nothing on a clean Save in edit mode, and returns to view mode', async () => {
    await start();
    const harness = await editCustomer();

    await harness.click('save');
    await tmSettle();

    expect(writes()).toEqual([]);
    expect(await harness.getMode()).toBe('view');
  });

  it("creates with no key and no stamp, replaces the create page's URL with the new record's, and shows the saved copy with no read", async () => {
    await start();
    await router.navigateByUrl('/17/customers/new');
    await tmSettleUntil(() => document.querySelector('[data-tm-field="Code"] tm-form-field') !== null, 'the controls load');
    const fields = form();
    fields['code']!().value.set('C-009');
    fields['name']!().value.set('Customer 9');
    fields['customerType']!().value.set('Retail');
    fields['creditLimit']!().value.set('0');
    rig.api.respond('customers/save', () => customer(9));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl');

    await (await page()).click('save');
    await tmSettleUntil(() => TestBed.inject(Location).path() === '/17/customers/9', 'the new record opens');
    await tmSettleUntil(() => TmTestSaveLayout.latest?.details.status() === 'ready', 'the saved copy lands');

    const record = sent(writes()[0]!);
    expect(['id' in record, 'modifiedAt' in record]).toEqual([false, false]);
    expect(navigate).toHaveBeenCalledWith('/17/customers/9', { replaceUrl: true, state: { tmSaved: true, tmPageFocus: false } });
    expect(reads()).toEqual([]);
    expect(layout().details.entity()?.['name']).toBe('Customer 9');
    expect(live()).toBe('Saved');
    expect(await (await (await page()).getButton('edit'))!.isFocused()).toBe(true);
    expect(telemetry('tm.details.save')).toEqual([{ mode: 'create', outcome: 'ok', errors: '0' }]);
  });

  it('lands a create on the URL its declaration names, with the saved copy and no read', async () => {
    await start({ details: { page: tmLoadTestPage, createdUrl: (entity, urls) => `${urls.details('acme.Customer', entity['id'] as number)}?from=new` } });
    await router.navigateByUrl('/17/customers/new');
    await tmSettleUntil(() => document.querySelector('[data-tm-field="Code"] tm-form-field') !== null, 'the controls load');
    const fields = form();
    fields['code']!().value.set('C-009');
    fields['name']!().value.set('Customer 9');
    fields['customerType']!().value.set('Retail');
    fields['creditLimit']!().value.set('0');
    rig.api.respond('customers/save', () => customer(9));

    await (await page()).click('save');
    await tmSettleUntil(() => TestBed.inject(Location).path() === '/17/customers/9?from=new', 'the declared URL opens');
    await tmSettleUntil(() => TmTestSaveLayout.latest?.details.status() === 'ready', 'the saved copy lands');

    expect(reads()).toEqual([]);
    expect([layout().details.mode(), layout().details.entity()?.['name']]).toEqual(['view', 'Customer 9']);
  });

  // --- The error summary ---

  it("lists a 422's field, child-row, unregistered and root errors, rendering the unvisited tab, and marks each Fixed in place", async () => {
    await start();
    const harness = await editCustomer();
    form()['name']!().value.set('Renamed');
    rig.api.respond('customers/save', () => {
      throw invalid(
        { path: 'entities[0].code', code: 'Test_CodeTaken', message: 'This code is taken' },
        { path: 'entities[0].contacts[0].email', code: 'Test_Email', message: 'Not an email' },
        { path: 'entities[0].contacts[0]', code: 'Test_Duplicate', message: 'Duplicate contact' },
        { path: 'entities[0].currencyId', code: 'Test_Currency', message: 'The currency is closed' },
        { path: '', code: 'Test_Locked', message: 'The period is locked' },
      );
    });

    await harness.click('save');
    const errors = await summary();

    // --- The summary opens expanded, its heading focused, over the edit ---
    expect(await harness.getMode()).toBe('edit');
    expect(await errors.getHeading()).toBe('Fix 5 problems to save');
    expect([await errors.isExpanded(), await errors.isHeadingFocused()]).toEqual([true, true]);
    const contacts = label('acme.Customer', 'Contacts');
    const texts = await errors.getItemTexts();
    expect(texts.slice(0, 3)).toEqual([
      `${label('acme.Customer', 'Code')}: This code is taken`,
      `${contacts} · row 1 · ${label('acme.CustomerContact', 'Email')}: Not an email`,
      `${contacts} · row 1: Duplicate contact`,
    ]);
    expect(texts[3]).toMatch(/: The currency is closed$/);
    expect(texts[4]).toBe(`${rig.entities.get('acme.Customer').label()}: The period is locked`);
    const items = await errors.getItems();
    expect(await Promise.all(items.map((item) => item.isLink()))).toEqual([true, true, true, false, false]);

    // --- The field shows its error, and the contacts' tab rendered for its rows, its dot on ---
    const code = await TestbedHarnessEnvironment.loader(router.fixture).getHarness(TmFieldHarness.with({ path: 'Code' }));
    expect(await code.getErrors()).toEqual(['This code is taken']);
    expect(document.querySelectorAll('.tm-details-tabs__dot--on')).toHaveLength(1);
    expect(telemetry('tm.details.save')).toEqual([{ mode: 'edit', outcome: 'validation', errors: '4+' }]);

    // --- Fixing the code marks its item Fixed; nothing moves ---
    form()['code']!().value.set('C-105');
    await tmSettle();
    expect(await Promise.all(items.map((item) => item.isFixed()))).toEqual([true, false, false, false, false]);
    expect(await errors.getItemTexts()).toEqual(texts);
    expect(await code.getErrors()).toEqual([]);
  });

  it('goes to a field and to a child cell from their items, collapsing the list', async () => {
    await start();
    const harness = await editCustomer();
    form()['name']!().value.set('Renamed');
    rig.api.respond('customers/save', () => {
      throw invalid(
        { path: 'entities[0].code', code: 'Test_CodeTaken', message: 'This code is taken' },
        { path: 'entities[0].contacts[0].email', code: 'Test_Email', message: 'Not an email' },
      );
    });

    await harness.click('save');
    const errors = await summary();
    await (await errors.getItems())[0]!.activate();
    await tmSettle();
    expect(await errors.isExpanded()).toBe(false);
    expect(document.activeElement?.closest('[data-tm-field="Code"]')).not.toBeNull();

    await errors.toggle();
    await (await errors.getItems())[1]!.activate();
    await tmSettleUntil(() => document.activeElement?.closest('tm-child-grid') != null, "the contact's cell takes focus");
    expect(await errors.isExpanded()).toBe(false);
  });

  it("lists the client's own errors without a request, rendering the unvisited tab whose row holds one", async () => {
    await start();
    const harness = await editCustomer();
    form()['name']!().value.set('');
    const rows = form()['contacts'] as unknown as Readonly<Record<number, Readonly<Record<string, FieldTree<unknown>>>>>;
    rows[0]!['name']!().value.set('');

    await harness.click('save');
    const errors = await summary();

    expect(writes()).toEqual([]);
    expect(await errors.getHeading()).toBe('Fix 2 problems to save');
    const name = await TestbedHarnessEnvironment.loader(router.fixture).getHarness(TmFieldHarness.with({ path: 'Name' }));
    const [message] = await name.getErrors();
    expect(await errors.getItemTexts()).toEqual([
      `${label('acme.Customer', 'Name')}: ${message}`,
      `${label('acme.Customer', 'Contacts')} · row 1 · ${label('acme.CustomerContact', 'Name')}: ${message}`,
    ]);
    expect(document.querySelectorAll('.tm-details-tabs__dot--on')).toHaveLength(1);
    expect(telemetry('tm.details.save')).toEqual([{ mode: 'edit', outcome: 'client-invalid', errors: '1-3' }]);
  });

  it('closes the list on Escape, focusing its toggle', async () => {
    await start();
    const harness = await editCustomer();
    form()['name']!().value.set('');

    await harness.click('save');
    const errors = await summary();
    await errors.pressEscape();

    expect([await errors.isExpanded(), await errors.isToggleFocused()]).toEqual([false, true]);
  });

  it('removes the summary and the server errors on Cancel', async () => {
    await start();
    const harness = await editCustomer();
    form()['name']!().value.set('Renamed');
    rig.api.respond('customers/save', () => {
      throw invalid({ path: 'entities[0].code', code: 'Test_CodeTaken', message: 'This code is taken' });
    });
    await harness.click('save');
    await summary();

    await harness.click('cancel');
    await tmSettleUntil(() => modals().length === 1, 'the discard question opens');
    await choose('Discard changes');

    expect([await harness.getMode(), await harness.getErrorSummary()]).toEqual(['view', null]);
    expect(layout().record.errors()).toEqual([]);
  });

  it("refuses the write while a file has not uploaded, keeping the edit, and marks the file's item Fixed once it uploads", async () => {
    await start();
    const harness = await editCustomer();
    form()['name']!().value.set('Renamed');
    const uploads = layout().record.uploads;
    await uploads.start('imageId', () => Promise.reject(new TmApiError({ kind: 'server', status: 500 }))).catch(() => undefined);

    await harness.click('save');
    const errors = await summary();

    expect(writes()).toEqual([]);
    expect(await harness.getMode()).toBe('edit');
    expect(layout().details.model()['name']).toBe('Renamed');
    expect(live()).toBe("Nothing was saved: a file didn't upload.");
    const [item] = await errors.getItems();
    expect(await item!.getText()).toMatch(/: The file didn't upload\. Try it again or remove it\.$/);

    const blob = { id: 88, kind: 'customer-image', fileName: 'logo.png', contentType: 'image/png', size: '512' } as unknown as TmBlobDescriptor;
    await uploads.start('imageId', () => Promise.resolve(blob));
    await tmSettle();
    expect(await item!.isFixed()).toBe(true);
  });

  // --- The concurrency dialog ---

  it("names who saved a newer version, and Overwrite sends the same record again under Override", async () => {
    await start();
    const harness = await editCustomer();
    form()['name']!().value.set('Renamed');
    const at = new Date(Date.now() - 5 * 60_000).toISOString();
    rig.api.respond('customers/save', () => {
      if (writes().length === 1) {
        throw conflict({ modifiedById: 7, modifiedByName: 'Sami', modifiedAt: at });
      }
      return customer(5, 'Renamed');
    });

    await harness.click('save');
    await tmSettleUntil(() => modals().length === 1, 'the concurrency dialog opens');
    expect(top().textContent).toContain('This record changed while you were editing');
    expect(top().textContent).toContain('Sami saved a newer version 5 minutes ago. Reload to take theirs, or overwrite with yours.');
    expect(choices()).toEqual(['Overwrite', 'Reload theirs']);
    expect(document.activeElement?.textContent?.trim()).toBe('Reload theirs');

    await choose('Overwrite');
    await tmSettleUntil(() => layout().details.mode() === 'view', 'the overwrite lands');
    const [first, second] = writes();
    expect([concurrency(first!), concurrency(second!)]).toEqual(['Check', 'Override']);
    expect(sent(second!)).toEqual(sent(first!));
    expect(telemetry('tm.details.conflict')).toEqual([{ choice: 'overwrite', self: false }]);
    expect(telemetry('tm.details.save').map((props) => props['outcome'])).toEqual(['ok']);
  });

  it('reloads the saved version on Reload, in view mode, sending no second write', async () => {
    await start();
    const harness = await editCustomer();
    form()['name']!().value.set('Renamed');
    rig.api.respond('customers/save', () => {
      throw conflict();
    });

    await harness.click('save');
    await tmSettleUntil(() => modals().length === 1, 'the concurrency dialog opens');
    expect(top().textContent).toContain('Reload to take the saved version, or overwrite it with yours.');
    expect(choices()).toEqual(['Overwrite', 'Reload']);
    await choose('Reload');
    await tmSettleUntil(() => layout().details.mode() === 'view', 'the reload lands');

    expect([writes().length, reads().length]).toEqual([1, 2]);
    expect(layout().details.entity()?.['name']).toBe('Customer 5');
    expect(telemetry('tm.details.conflict')).toEqual([{ choice: 'reload', self: false }]);
  });

  it("says the current user saved it elsewhere, and keeps the edit when the dialog is dismissed", async () => {
    await start();
    const harness = await editCustomer();
    form()['name']!().value.set('Renamed');
    const at = new Date(Date.now() - 5 * 60_000).toISOString();
    rig.api.respond('customers/save', () => {
      throw conflict({ modifiedById: 42, modifiedByName: 'Farah', modifiedAt: at });
    });

    await harness.click('save');
    await tmSettleUntil(() => modals().length === 1, 'the concurrency dialog opens');
    expect(top().textContent).toContain('You saved this record in another tab or window 5 minutes ago. Reload that version, or overwrite it with this one.');
    expect(choices()).toEqual(['Overwrite', 'Reload saved version']);
    await escape();

    expect([writes().length, layout().details.mode(), layout().details.model()['name']]).toEqual([1, 'edit', 'Renamed']);
    expect(telemetry('tm.details.conflict')).toEqual([{ choice: 'dismiss', self: true }]);
  });

  it('resends Override with the keys confirmed so far once a conflict follows a warning', async () => {
    await start();
    const harness = await editCustomer();
    form()['name']!().value.set('Renamed');
    rig.api.respond('customers/save', () => {
      const count = writes().length;
      if (count === 1) {
        throw warning('Acme.CreditAboveThreshold');
      }
      if (count === 2) {
        throw conflict();
      }
      return customer(5, 'Renamed');
    });

    await harness.click('save');
    await tmSettleUntil(() => modals().length === 1 && top().textContent!.includes('Confirm to continue'), 'the warning opens');
    await choose('Continue');
    await tmSettleUntil(() => modals().length === 1 && top().textContent!.includes('This record changed'), 'the concurrency dialog opens');
    await choose('Overwrite');
    await tmSettleUntil(() => layout().details.mode() === 'view', 'the overwrite lands');

    expect(writes().map((call) => [concurrency(call), confirmed(call)])).toEqual([
      ['Check', []],
      ['Check', ['Acme.CreditAboveThreshold']],
      ['Override', ['Acme.CreditAboveThreshold']],
    ]);
  });

  it('asks again about a warning that follows an Overwrite, resending Override and the key', async () => {
    await start();
    const harness = await editCustomer();
    form()['name']!().value.set('Renamed');
    rig.api.respond('customers/save', (call: TmFakeApiCall) => {
      if (concurrency(call) === 'Check') {
        throw conflict();
      }
      if (confirmed(call).length === 0) {
        throw warning('Acme.CreditAboveThreshold');
      }
      return customer(5, 'Renamed');
    });

    await harness.click('save');
    await tmSettleUntil(() => modals().length === 1, 'the concurrency dialog opens');
    await choose('Overwrite');
    await tmSettleUntil(() => modals().length === 1 && top().textContent!.includes('Confirm to continue'), 'the warning opens');
    await choose('Continue');
    await tmSettleUntil(() => layout().details.mode() === 'view', 'the save lands');

    expect(writes().map((call) => [concurrency(call), confirmed(call)])).toEqual([
      ['Check', []],
      ['Override', []],
      ['Override', ['Acme.CreditAboveThreshold']],
    ]);
  });

  // --- The deletion dialog ---

  it("goes to the list on Discard changes once the record was deleted, and keeps the edit on Escape", async () => {
    await start();
    const harness = await editCustomer();
    form()['name']!().value.set('Renamed');
    rig.api.respond('customers/save', () => {
      throw new TmApiError({ kind: 'not-found', status: 404, code: 'not-found' });
    });

    // --- Escape keeps editing ---
    await harness.click('save');
    await tmSettleUntil(() => modals().length === 1, 'the deletion dialog opens');
    expect(top().textContent).toContain('This record was deleted');
    expect(choices()).toEqual(['Discard changes']);
    await escape();
    expect([layout().details.mode(), TestBed.inject(Location).path()]).toEqual(['edit', '/17/customers/5']);

    // --- Discard changes replaces the record's URL with the list's ---
    await harness.click('save');
    await tmSettleUntil(() => modals().length === 1, 'the deletion dialog opens again');
    await choose('Discard changes');
    await tmSettleUntil(() => TestBed.inject(Location).path() === TestBed.inject(TmUrls).search('acme.Customer'), 'the list opens');
    expect(modals()).toEqual([]);
  });

  // --- The error presenter ---

  describe("hands other failures to the error presenter, keeping the edit; Try again re-runs Save where the write may have committed", () => {
    const cases: readonly (readonly [string, TmApiError, boolean])[] = [
      ['bad-request', new TmApiError({ kind: 'bad-request', status: 400, code: 'bad-request' }), false],
      ['limit', new TmApiError({ kind: 'limit', status: 413, code: 'too-many' }), false],
      ['forbidden', new TmApiError({ kind: 'forbidden', status: 403, code: 'forbidden' }), false],
      ['read-only', new TmApiError({ kind: 'read-only', status: 403, code: 'read-only' }), false],
      ['step-up', new TmApiError({ kind: 'step-up', status: 401, code: 'step-up-required' }), false],
      ['client-outdated', new TmApiError({ kind: 'client-outdated', status: 426, code: 'client-outdated' }), false],
      ['offline, not sent', new TmApiError({ kind: 'offline', code: 'not-sent' }), false],
      ['offline, outcome unknown', new TmApiError({ kind: 'offline', code: 'outcome-unknown' }), true],
      ['unavailable', new TmApiError({ kind: 'unavailable', status: 503, code: 'unavailable' }), true],
      ['server', new TmApiError({ kind: 'server', status: 500, code: 'internal-error' }), true],
    ];

    it.each(cases)('%s', async (_name, error, retried) => {
      await start();
      // The presenter's entry point loads on demand, as the page loads it.
      const { TmErrorPresenter } = await import('@tellma/core/errors');
      const present = vi.spyOn(TestBed.inject(TmErrorPresenter), 'present').mockResolvedValue(undefined);
      const harness = await editCustomer();
      form()['name']!().value.set('Renamed');
      rig.api.respond('customers/save', () => {
        if (writes().length === 1) {
          throw error;
        }
        return customer(5, 'Renamed');
      });

      await harness.click('save');
      await tmSettleUntil(() => present.mock.calls.length === 1, 'the presenter shows the failure');

      const [shown, context] = present.mock.calls[0]!;
      expect(shown).toBe(error);
      expect(context).toEqual(expect.objectContaining({ kind: 'save', entity: 'acme.Customer' }));
      expect(typeof context.retry).toBe(retried ? 'function' : 'undefined');
      expect(layout().details.mode()).toBe('edit');
      if (retried) {
        context.retry!();
        await tmSettleUntil(() => layout().details.mode() === 'view', 'Try again saves');
        expect(writes()).toHaveLength(2);
      }
    });
  });

  // --- The busy overlay ---

  it("shows Save busy in the click's frame and the scrim after 200 ms, and the scrim at once after the concurrency dialog", async () => {
    await start();
    const harness = await editCustomer();
    form()['name']!().value.set('Renamed');
    const answers: ((value: unknown) => void)[] = [];
    const failures: ((error: unknown) => void)[] = [];
    rig.api.respond('customers/save', () => new Promise((resolve, reject) => (answers.push(resolve), failures.push(reject))));

    await manualChangeDetection(async () => {
      // --- Save: the button busy in the click's frame, the page blocked, the scrim only after its delay ---
      const button = document.querySelector<HTMLButtonElement>('.tm-details__save')!;
      button.click();
      // A frame callback asked for after the click runs after the render the click scheduled, before the paint.
      const frame = await new Promise<boolean>((resolve) => requestAnimationFrame(() => resolve(button.getAttribute('aria-busy') === 'true')));
      expect(frame).toBe(true);
      await tmSettleUntil(() => answers.length === 1 && blocked(), 'the write is sent with the page blocked');
      expect(scrim()).toBe(false);
      await new Promise((resolve) => setTimeout(resolve, 250));
      TestBed.tick();
      expect(scrim()).toBe(true);

      // --- The concurrency dialog's Overwrite: the scrim at once ---
      failures[0]!(conflict());
      await tmSettleUntil(() => modals().length === 1, 'the concurrency dialog opens');
      const chosen = performance.now();
      [...top().querySelectorAll<HTMLButtonElement>('button')].find((candidate) => candidate.textContent?.trim() === 'Overwrite')!.click();
      await tmSettleUntil(() => answers.length === 2 && scrim(), 'the overwrite blocks with the scrim');
      expect(performance.now() - chosen).toBeLessThan(200);
      answers[1]!(customer(5, 'Renamed'));
      await tmSettleUntil(() => layout().details.mode() === 'view', 'the overwrite lands');
    });
    expect(await harness.getMode()).toBe('view');
  });

  // --- The leave guard ---

  it('asks before leaving with unsaved changes: Stay keeps the page, Discard changes leaves', async () => {
    await start();
    await editCustomer();
    form()['name']!().value.set('Renamed');
    const appRouter = TestBed.inject(Router);
    const location = TestBed.inject(Location);

    // --- Stay, the focused choice: the navigation is refused, and the edit stays ---
    let leaving = appRouter.navigateByUrl('/17');
    await tmSettleUntil(() => modals().length === 1, 'the leave question opens');
    expect(top().textContent).toContain('Leave without saving?');
    expect(top().textContent).toContain("This record has unsaved changes. They're lost if you leave now.");
    expect(choices()).toEqual(['Discard changes', 'Stay']);
    expect(document.activeElement?.textContent?.trim()).toBe('Stay');
    await choose('Stay');
    expect(await leaving).toBe(false);
    expect([location.path(), layout().details.mode(), layout().details.model()['name']]).toEqual(['/17/customers/5', 'edit', 'Renamed']);

    // --- Discard changes: the navigation goes on ---
    leaving = appRouter.navigateByUrl('/17');
    await tmSettleUntil(() => modals().length === 1, 'the leave question opens again');
    await choose('Discard changes');
    expect(await leaving).toBe(true);
    expect(location.path()).toBe('/17');
    expect(telemetry('tm.details.leave-guard')).toEqual([{ choice: 'stay' }, { choice: 'discard' }]);
  });

  it('leaves a clean edit without asking', async () => {
    await start();
    await editCustomer();

    expect(await TestBed.inject(Router).navigateByUrl('/17')).toBe(true);
    expect(modals()).toEqual([]);
  });
});

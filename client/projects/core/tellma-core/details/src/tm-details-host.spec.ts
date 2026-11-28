// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Location } from '@angular/common';
import { Component, computed, type ElementRef, inject, signal, viewChild } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { type FieldTree, required, type SchemaPath, type SchemaPathTree } from '@angular/forms/signals';
import { manualChangeDetection } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { Title } from '@angular/platform-browser';
import { Router } from '@angular/router';
import type { RouterTestingHarness } from '@angular/router/testing';
import { TmApiError, type TmBlobDescriptor, type TmJsonObject, TmPageActions } from '@tellma/core';
import { TmDetailsPageHarness, type TmFakeApiCall } from '@tellma/core-testing';
import { TmCardHarness, TmConfirmHarness } from '@tellma/core-ui-testing';

import { tmEntityRig, type TmEntityRig, type TmEntityRigOptions } from '../../src/entity/tm-entity.testing.util';
import { tmLoadTestPage, tmSettle, tmSettleUntil, TmTestPage } from '../../src/routes/tm-app.testing.util';
import { injectTmDetails } from './tm-details-context';
import { TmDetailsKit } from './tm-details-kit';
import { TM_DETAILS_RECORD } from './tm-details-record';

// The routed details host over the recorded customer model, with a layout
// that registers one field: the load, view → edit → cancel with no request,
// the Discard changes question, the held record under the bar on a refresh,
// a step and a stale return, the empty record while nothing is held, the
// quiet refresh, the permission reasons, the read failure's way out, a
// failed refresh in view and edit mode, a layout that fails to load, an id
// no record can have, create and clone, Save waiting for the form's
// uploads, and a page left while Save runs.

/** A layout over the record: a name input it registers as its first field, and the server copy's name. */
@Component({
  selector: 'tm-test-details-layout',
  template: `
    <label>
      Name
      <input #name class="tm-test-layout__name" [readOnly]="details.mode() === 'view'" [value]="details.model()['name'] ?? ''" (input)="write($event)" />
    </label>
    <p class="tm-test-layout__value">{{ details.entity()?.['name'] ?? '' }}</p>
  `,
})
class TmTestDetailsLayout {
  /** Every instance, in creation order. */
  static readonly instances: TmTestDetailsLayout[] = [];

  /** The record. */
  readonly details = injectTmDetails<TmJsonObject>();

  /** The record's state behind it, whose uploads the test drives. */
  readonly record = inject(TM_DETAILS_RECORD);

  /** The name input. */
  private readonly input = viewChild.required<ElementRef<HTMLInputElement>>('name');

  /** Registers the name field. */
  constructor() {
    TmTestDetailsLayout.instances.push(this);
    this.details.registerField('Name', {
      labelKey: 'Name',
      label: signal('Name'),
      editable: computed(() => this.details.mode() !== 'view'),
      focus: () => this.input().nativeElement.focus(),
    });
  }

  /**
   * Writes the typed name to the working copy.
   *
   * @param event - The input event.
   */
  protected write(event: Event): void {
    const name = (event.target as HTMLInputElement).value;
    this.details.update((model) => ({ ...model, name }));
  }
}

/** A layout in the kit: a half card, an aside and a title extra. */
@Component({
  selector: 'tm-test-kit-layout',
  imports: [TmDetailsKit],
  template: `
    <tm-details-card width="half"><tm-field path="Code" /></tm-details-card>
    <tm-details-card width="half"><tm-field path="Name" /></tm-details-card>
    <tm-details-aside titleKey="Code"><p class="tm-test-layout__aside">Related</p></tm-details-aside>
    <ng-template tmDetailsTitleExtra><span class="tm-test-layout__extra">Draft</span></ng-template>
  `,
})
class TmTestKitLayout {}

/** A layout in the kit whose fields sit in a collapsed card and on a tab, which keeps its record for the test. */
@Component({
  selector: 'tm-test-dots-layout',
  imports: [TmDetailsKit],
  template: `
    <tm-details-card id="terms" titleKey="Code" collapsible collapsed><tm-field path="Code" /></tm-details-card>
    <tm-details-tabs>
      <tm-details-tab id="general" labelKey="Name">
        <ng-template tmDetailsTabContent><tm-field path="Name" /></ng-template>
      </tm-details-tab>
    </tm-details-tabs>
  `,
})
class TmTestDotsLayout {
  /** The latest instance. */
  static latest: TmTestDotsLayout | null = null;

  /** The record. */
  readonly details = injectTmDetails<TmJsonObject>();

  /** Keeps itself for the test. */
  constructor() {
    TmTestDotsLayout.latest = this;
  }
}

/** A read the test answers when it chooses. */
interface TmPendingRead {
  /** The read's call. */
  readonly call: TmFakeApiCall;
  /** Answers it with a customer. */
  resolve(id: number): void;
  /** Fails it. */
  reject(error: unknown): void;
}

/**
 * A customer as `customers/get` returns it.
 *
 * @param id - The customer's id.
 * @returns The result.
 */
function customer(id: number): TmJsonObject {
  return {
    entities: [
      {
        id,
        code: `C-00${id}`,
        name: `Customer ${id}`,
        customerType: 'Retail',
        creditLimit: '0',
        isActive: true,
        modifiedAt: '2026-11-20T08:00:00.0000000+00:00',
        contacts: [{ id: 100 + id, customerId: id, name: 'Huda' }],
      },
    ],
    related: {},
    extras: { permissions: [] },
  };
}

/**
 * A customer as `customers/save` returns it, renamed.
 *
 * @param id - The customer's id.
 * @param name - The saved name.
 * @returns The result.
 */
function saved(id: number, name: string): TmJsonObject {
  const result = customer(id);
  const [entity] = result['entities'] as TmJsonObject[];
  return { ...result, entities: [{ ...entity, name }] };
}

/**
 * A model as the wire would carry it, without the nulls the working copy
 * holds for each member the client writes, which give every member a form field.
 *
 * @param model - The model.
 * @returns Its JSON, nulls dropped.
 */
function valued(model: TmJsonObject): unknown {
  return JSON.parse(JSON.stringify(model, (_key, value: unknown) => (value === null ? undefined : value)));
}

/**
 * A declaration's own rule: the currency, which is nullable, is required.
 *
 * @param p - The customer form's schema path tree.
 */
function currencyRule(p: SchemaPathTree<TmJsonObject>): void {
  required((p as unknown as Readonly<Record<string, SchemaPath<unknown>>>)['currencyId']!);
}

/** An offline failure, as the API client raises it. */
function offline(): never {
  throw new TmApiError({ kind: 'offline' });
}

/** The grant of a customer permission. */
function grant(action: string): { readonly resource: string; readonly action: string; readonly hasFilter: boolean } {
  return { resource: 'acme.Customer', action, hasFilter: false };
}

describe('the details host', () => {
  let rig: TmEntityRig;
  let router: RouterTestingHarness;
  /** The reads waiting for an answer, when the test answers them itself. */
  let pending: TmPendingRead[];
  /** Whether reads wait for the test, rather than answering at once. */
  let hold: boolean;

  /**
   * Starts the app with the customer permissions given.
   *
   * @param actions - The customer actions granted.
   * @param declaration - More of the customer's declaration.
   */
  async function start(actions: readonly string[] = ['Read', 'Save'], declaration: TmEntityRigOptions['customer'] = {}): Promise<void> {
    rig = await tmEntityRig({ access: { securables: actions.map(grant) }, customer: declaration });
    rig.api.respond('customers/get', (call: TmFakeApiCall) => {
      const id = (call.body as { readonly id: number }).id;
      if (!hold) {
        return customer(id);
      }
      return new Promise((resolve, reject) =>
        pending.push({ call, resolve: (answer) => resolve(customer(answer)), reject }),
      );
    });
    router = rig.router;
  }

  /** The reads of customers so far. */
  function reads(): TmFakeApiCall[] {
    return rig.api.calls().filter((call) => call.route === 'customers/get');
  }

  /** Every request the page sent to the customers API so far. */
  function customerCalls(): TmFakeApiCall[] {
    return rig.api.calls().filter((call) => call.route.startsWith('customers/'));
  }

  /**
   * Navigates and waits for the details page's frame.
   *
   * @param url - The URL.
   * @returns The page's harness.
   */
  async function open(url: string): Promise<TmDetailsPageHarness> {
    await router.navigateByUrl(url);
    await tmSettleUntil(() => document.querySelector('.tm-details__title') !== null, 'the details frame renders');
    return TestbedHarnessEnvironment.loader(router.fixture).getHarness(TmDetailsPageHarness);
  }

  /**
   * Opens a customer and waits for it to land.
   *
   * @param id - The customer's id.
   * @returns The page's harness.
   */
  async function openCustomer(id: number): Promise<TmDetailsPageHarness> {
    const page = await open(`/17/customers/${id}`);
    await tmSettleUntil(() => latest()?.details.status() === 'ready', 'the customer lands');
    return page;
  }

  /**
   * Waits for the band's error state.
   *
   * @param what - What the wait is for, named in its failure.
   */
  async function errorState(what: string): Promise<void> {
    await tmSettleUntil(() => document.querySelector('tm-error-state') !== null, what);
  }

  /**
   * The error state's button of a label.
   *
   * @param label - The button's label.
   * @returns The button.
   */
  function errorButton(label: string): HTMLButtonElement {
    return [...document.querySelectorAll<HTMLButtonElement>('tm-error-state button')].find((button) => button.textContent?.trim() === label)!;
  }

  /**
   * Opens a record whose read lands after its kit layout renders, empties
   * the card's field and the tab's, and reads the card's and the tab's
   * error state.
   *
   * @param url - The page's URL: a record's id, or a clone.
   * @returns Whether the card's dot shows, and the tab's name.
   */
  async function lateErrors(url: string): Promise<{ readonly card: boolean; readonly tab: string | null }> {
    tmLoadTestPage.mockImplementation(() => Promise.resolve(TmTestDotsLayout));
    await start();
    hold = true;
    // The held read keeps the page unstable, so the harness must not wait for it.
    const page = await manualChangeDetection(() => open(url));
    await tmSettleUntil(() => TmTestDotsLayout.latest !== null && pending.length === 1, 'the layout renders before the read lands');
    pending[0]!.resolve(5);
    await tmSettleUntil(() => TmTestDotsLayout.latest!.details.model()['code'] !== undefined, 'the read lands');
    if (TmTestDotsLayout.latest!.details.mode() === 'view') {
      await page.click('edit');
    }
    await tmSettleUntil(() => document.querySelector('[data-tm-field="Name"] tm-form-field') !== null, 'the controls load');
    const form = TmTestDotsLayout.latest!.details.form as unknown as Readonly<Record<string, FieldTree<unknown>>>;
    for (const json of ['code', 'name']) {
      form[json]!().value.set('');
      form[json]!().markAsTouched();
    }
    await tmSettle();
    const card = await TestbedHarnessEnvironment.loader(router.fixture).getHarness(TmCardHarness);
    return { card: await card.isInvalid(), tab: document.querySelector('.tm-details-tabs__name')?.textContent?.trim() ?? null };
  }

  /** What the page's polite status region says. */
  function live(): string {
    return document.querySelector('.tm-details__live')?.textContent?.trim() ?? '';
  }

  /** What the status strip's notice says, or `null` while it shows none. */
  function notice(): string | null {
    return document.querySelector('.tm-details__notice')?.textContent?.trim() ?? null;
  }

  /** The latest layout instance. */
  function latest(): TmTestDetailsLayout | undefined {
    return TmTestDetailsLayout.instances.at(-1);
  }

  /**
   * The title the page gives a customer.
   *
   * @param id - The customer's id.
   * @returns The display name.
   */
  function nameOf(id: number): string {
    const entity = (customer(id)['entities'] as TmJsonObject[])[0]!;
    return rig.names.of('acme.Customer', entity)();
  }

  beforeEach(() => {
    TmTestDetailsLayout.instances.length = 0;
    TmTestDotsLayout.latest = null;
    pending = [];
    hold = false;
    tmLoadTestPage.mockImplementation(() => Promise.resolve(TmTestDetailsLayout));
  });

  afterEach(() => {
    tmLoadTestPage.mockImplementation(() => Promise.resolve(TmTestPage));
  });

  it('loads the record with every extra and shows the crumb, its name, its badge and the layout over it', async () => {
    await start();
    const page = await openCustomer(5);

    expect(reads().map((call) => call.body)).toEqual([{ id: 5, include: ['permissions'] }]);
    expect(await page.getCrumb()).toBe('Customers');
    expect(await page.getTitle()).toBe(nameOf(5));
    expect(await page.getBadges()).toEqual(['Active']);
    expect(await page.getMode()).toBe('view');
    expect(await page.isEnabled('edit')).toBe(true);
    expect(document.querySelector('.tm-test-layout__value')?.textContent).toBe('Customer 5');
    expect(TestBed.inject(Title).getTitle()).toBe(`${nameOf(5)} · Acme`);
    expect(await page.isBusy()).toBe(false);
  });

  it('enters edit mode sending nothing, focuses the first field, and returns on Cancel sending nothing', async () => {
    await start();
    const page = await openCustomer(5);
    const before = customerCalls().length;

    await page.click('edit');
    await tmSettle();
    expect(await page.getMode()).toBe('edit');
    expect(document.activeElement).toBe(document.querySelector('.tm-test-layout__name'));
    expect(await page.getStatus()).toBe(`Editing ${nameOf(5)}`);
    expect(await page.getBadges()).toEqual(['Active']);

    await page.click('cancel');
    await tmSettle();
    expect(await page.getMode()).toBe('view');
    expect(await (await page.getButton('edit'))!.isFocused()).toBe(true);
    expect(customerCalls()).toHaveLength(before);
    expect(TmTestDetailsLayout.instances).toHaveLength(1);
  });

  it('asks before discarding changes: Keep editing stays, Discard changes resets to the server copy', async () => {
    await start();
    const page = await openCustomer(5);
    const root = TestbedHarnessEnvironment.documentRootLoader(router.fixture);
    await page.click('edit');
    latest()!.details.update((model) => ({ ...model, name: 'Renamed' }));
    await tmSettle();
    expect(await page.getBadges()).toEqual(['Active', 'Unsaved changes']);

    await page.click('cancel');
    let confirm = await root.getHarness(TmConfirmHarness);
    expect([await confirm.getTitle(), await confirm.getMessage()]).toEqual(['Discard changes?', 'Your changes to this record will be lost.']);
    expect(await confirm.getChoiceLabels()).toEqual(['Discard changes', 'Keep editing']);
    expect(await confirm.getFocusedChoiceLabel()).toBe('Keep editing');
    await confirm.choose('Keep editing');
    await tmSettle();
    expect([await page.getMode(), latest()!.details.dirty()]).toEqual(['edit', true]);

    await page.click('cancel');
    confirm = await root.getHarness(TmConfirmHarness);
    await confirm.choose('Discard changes');
    await tmSettle();
    expect(await page.getMode()).toBe('view');
    expect(latest()!.details.model()['name']).toBe('Customer 5');
    expect(customerCalls().map((call) => call.route)).toEqual(['customers/get']);
  });

  it('holds the record under the bar on Refresh, with Edit disabled, and keeps its layout when the read lands', async () => {
    await start();
    const page = await openCustomer(5);
    hold = true;

    await manualChangeDetection(async () => {
      await page.click('refresh');
      await tmSettleUntil(() => pending.length === 1, 'the refresh reads');
      expect(await page.isProgressShown()).toBe(true);
      expect(await page.isBusy()).toBe(true);
      expect(await page.isEnabled('edit')).toBe(false);
      expect(await page.getTitle()).toBe(nameOf(5));
      pending[0]!.resolve(5);
      await tmSettleUntil(() => latest()?.details.status() === 'ready', 'the refresh lands');
    });

    expect(await page.isBusy()).toBe(false);
    expect(await page.isEnabled('edit')).toBe(true);
    expect(TmTestDetailsLayout.instances).toHaveLength(1);
  });

  it('holds the previous record on a step until the next one lands, then re-keys the layout', async () => {
    await start();
    const page = await openCustomer(5);
    hold = true;

    await manualChangeDetection(async () => {
      await router.navigateByUrl('/17/customers/6');
      await tmSettleUntil(() => pending.length === 1, 'the step reads');
      expect(await page.getTitle()).toBe(nameOf(5));
      expect(await page.isProgressShown()).toBe(true);
      expect(await page.isEnabled('edit')).toBe(false);
      expect(TmTestDetailsLayout.instances).toHaveLength(1);
      expect(latest()!.details.entity()?.['id']).toBe(5);

      pending[0]!.resolve(6);
      await tmSettleUntil(() => latest()?.details.entity()?.['id'] === 6, 'the step lands');
    });

    expect(await page.getTitle()).toBe(nameOf(6));
    expect(TmTestDetailsLayout.instances).toHaveLength(2);
    expect(await page.isEnabled('edit')).toBe(true);
  });

  it('holds the record on a stale return, aborting the step it returns from', async () => {
    await start();
    const page = await openCustomer(5);
    hold = true;

    await manualChangeDetection(async () => {
      await router.navigateByUrl('/17/customers/6');
      await tmSettleUntil(() => pending.length === 1, 'the step reads');
      await router.navigateByUrl('/17/customers/5');
      await tmSettleUntil(() => pending.length === 2, 'the return reads again');
      expect((pending[0]!.call.options as { readonly signal: AbortSignal }).signal.aborted).toBe(true);
      expect(await page.getTitle()).toBe(nameOf(5));
      expect(await page.isProgressShown()).toBe(true);
      expect(await page.isEnabled('edit')).toBe(false);

      pending[1]!.resolve(5);
      await tmSettleUntil(() => latest()?.details.status() === 'ready', 'the return lands');
    });

    expect(TmTestDetailsLayout.instances).toHaveLength(1);
    expect(await page.isEnabled('edit')).toBe(true);
  });

  it('renders the layout over an empty record while loading with nothing held: busy, with no bar, a title skeleton and Edit disabled', async () => {
    await start();
    hold = true;

    await manualChangeDetection(async () => {
      const page = await open('/17/customers/5');
      await tmSettleUntil(() => latest() !== undefined && pending.length === 1, 'the layout renders while the read runs');
      const details = latest()!.details;
      expect([details.status(), details.entity(), details.model()]).toEqual(['loading', undefined, {}]);
      expect(await page.isTitleLoading()).toBe(true);
      expect(await page.isProgressShown()).toBe(false);
      expect(await page.isBusy()).toBe(true);
      expect(await page.getStatus()).toBe('Loading');
      expect(await page.isEnabled('edit')).toBe(false);
      expect(await page.isEnabled('actions')).toBe(false);

      pending[0]!.resolve(5);
      await tmSettleUntil(() => details.status() === 'ready', 'the read lands');
      expect(await page.getTitle()).toBe(nameOf(5));
      expect(await page.getStatus()).toBe('');
    });
    expect(TmTestDetailsLayout.instances).toHaveLength(1);
  });

  it('reads quietly when asked: no bar and no busy band', async () => {
    await start();
    const page = await openCustomer(5);
    hold = true;

    await manualChangeDetection(async () => {
      latest()!.details.refresh({ quiet: true });
      await tmSettleUntil(() => pending.length === 1, 'the quiet refresh reads');
      expect(await page.isProgressShown()).toBe(false);
      expect(await page.isBusy()).toBe(false);
      pending[0]!.resolve(5);
      await tmSettleUntil(() => latest()?.details.status() === 'ready', 'the quiet refresh lands');
    });
    expect(reads()).toHaveLength(2);
  });

  it('disables Edit and New without the Save permission, saying why', async () => {
    await start(['Read']);
    const page = await openCustomer(5);
    const reason = rig.permissions.disabledReason('acme.Customer', 'save')();

    expect(reason).toMatch(/^Needs “.+”\. Ask your administrator to grant it\.$/);
    expect(await page.getDisabledReason('edit')).toBe(reason);
    expect(await page.getDisabledReason('create')).toBe(reason);
    expect(await page.isEnabled('edit')).toBe(false);

    await page.click('edit');
    await tmSettle();
    expect(await page.getMode()).toBe('view');
  });

  it('replaces the body with the error state on a missing record, with a way out to the list', async () => {
    await start();
    rig.api.respond('customers/get', () => {
      throw new TmApiError({ kind: 'not-found', status: 404, code: 'not-found' });
    });
    const page = await open('/17/customers/9');
    await tmSettleUntil(() => document.querySelector('tm-error-state a') !== null, 'the error state renders');

    expect(await (await page.getErrorState())!.getHeading()).not.toBe('');
    const link = (await page.getErrorLink())!;
    expect(await link.getText()).toBe('Customers');
    expect(await page.getTitle()).toBe('Customer');
    expect(await page.getCrumb()).toBe('Customers');
    expect(await page.isEnabled('create')).toBe(true);
    expect(document.querySelector('tm-test-details-layout')).toBeNull();

    await link.click();
    await tmSettle();
    expect(TestBed.inject(Router).url).toBe('/17/customers');
  });

  it('starts a new record from its defaults with no request, and leaves it on Cancel', async () => {
    await start();
    const page = await open('/17/customers/new');
    await tmSettleUntil(() => latest() !== undefined, 'the layout renders');

    expect(customerCalls()).toEqual([]);
    expect(await page.getTitle()).toBe('New customer');
    expect(await page.getMode()).toBe('edit');
    expect(await page.getBadges()).toEqual([]);
    expect(valued(latest()!.details.model())).toEqual({ contacts: [] });

    // The app opened another page before this one, so Cancel goes Back to it;
    // the history moves, which the testing router does not follow.
    await page.click('cancel');
    await tmSettle();
    expect(TestBed.inject(Location).path()).toBe('/17');
  });

  it('starts a clone from the stripped copy of the record it reads', async () => {
    await start();
    const page = await open('/17/customers/new?clone=5');
    await tmSettleUntil(() => latest()?.details.model()['name'] !== undefined, 'the copy lands');

    expect(reads().map((call) => call.body)).toEqual([{ id: 5, include: ['permissions'] }]);
    expect(await page.getTitle()).toBe('New customer');
    const details = latest()!.details;
    expect([details.mode(), details.id(), details.dirty()]).toEqual(['create', null, false]);
    // The form tracks each child row by a symbol of its own, which the wire never carries.
    expect(valued(details.model())).toEqual({ code: 'C-005', name: 'Customer 5', customerType: 'Retail', creditLimit: '0', contacts: [{ id: -1, name: 'Huda' }] });
  });

  it("saves a clone's child rows without a key to the parent, exactly as the body is sent", async () => {
    await start();
    const page = await open('/17/customers/new?clone=5');
    await tmSettleUntil(() => latest()?.details.model()['name'] !== undefined, 'the copy lands');
    const details = latest()!.details;
    // A change of its own, so the copy is written as edited.
    details.update((model) => ({ ...model, customerType: 'Corporate' }));
    rig.api.respond('customers/save', () => saved(6, 'Customer 5'));

    await page.click('save');
    await tmSettleUntil(() => rig.api.calls().some((call) => call.route === 'customers/save'), 'the save writes');

    // The raw body, nulls included: a null for the contact's customerId is a value the server refuses.
    const body = JSON.stringify(rig.api.calls().find((call) => call.route === 'customers/save')!.body);
    expect(body).not.toContain('customerId');
    expect(JSON.parse(body)).toEqual(expect.objectContaining({ entities: [expect.objectContaining({ contacts: [{ id: -1, name: 'Huda', email: null }] })] }));
  });

  it("shows the card's and the tab's error state on a record opened at its id that lands after its layout renders", async () => {
    expect(await lateErrors('/17/customers/5')).toEqual({ card: true, tab: 'Name, has errors' });
  });

  it("shows the card's and the tab's error state on a clone whose copy lands after its layout renders", async () => {
    expect(await lateErrors('/17/customers/new?clone=5')).toEqual({ card: true, tab: 'Name, has errors' });
  });

  it("saves a new record's defaulted member exactly as the body is sent: left out while empty, sent once filled", async () => {
    await start();
    const page = await open('/17/customers/new');
    await tmSettleUntil(() => latest() !== undefined, 'the layout renders');
    const details = latest()!.details;
    details.update((model) => ({ ...model, code: 'C-009', name: 'Customer 9', customerType: 'Retail', creditLimit: '0' }));
    rig.api.respond('customers/save', () => saved(9, 'Customer 9'));

    await page.click('save');
    await tmSettleUntil(() => rig.api.calls().some((call) => call.route === 'customers/save'), 'the save writes');

    // The raw body, nulls included: the rank is left for the server to fill, and every other empty member is sent as null.
    const [entity] = (JSON.parse(JSON.stringify(rig.api.calls().find((call) => call.route === 'customers/save')!.body)) as { entities: TmJsonObject[] }).entities;
    expect(Object.keys(entity!)).not.toContain('rank');
    expect(entity).toEqual(expect.objectContaining({ code: 'C-009', currencyId: null }));
  });

  it("sends a new record's defaulted member the user filled", async () => {
    await start();
    const page = await open('/17/customers/new');
    await tmSettleUntil(() => latest() !== undefined, 'the layout renders');
    latest()!.details.update((model) => ({ ...model, code: 'C-009', name: 'Customer 9', customerType: 'Retail', creditLimit: '0', rank: 'A' }));
    rig.api.respond('customers/save', () => saved(9, 'Customer 9'));

    await page.click('save');
    await tmSettleUntil(() => rig.api.calls().some((call) => call.route === 'customers/save'), 'the save writes');

    const body = rig.api.calls().find((call) => call.route === 'customers/save')!.body as { entities: TmJsonObject[] };
    expect(body.entities[0]!['rank']).toBe('A');
  });

  it('keeps Save disabled while the copied record loads', async () => {
    await start();
    hold = true;

    await manualChangeDetection(async () => {
      const page = await open('/17/customers/new?clone=5');
      await tmSettleUntil(() => pending.length === 1, 'the copy reads');
      expect(await page.getMode()).toBe('edit');
      expect(await page.isEnabled('save')).toBe(false);

      pending[0]!.resolve(5);
      await tmSettleUntil(() => latest()?.details.model()['name'] !== undefined, 'the copy lands');
      expect(await page.isEnabled('save')).toBe(true);
    });
  });

  it('shows the error state with its way out when the copied record is missing, never an empty new record', async () => {
    await start();
    rig.api.respond('customers/get', () => {
      throw new TmApiError({ kind: 'not-found', status: 404, code: 'not-found' });
    });
    const page = await open('/17/customers/new?clone=9');
    await errorState('the error state renders');

    expect(await (await page.getErrorLink())!.getText()).toBe('Customers');
    expect(document.querySelector('tm-test-details-layout')).toBeNull();
    expect(customerCalls().map((call) => call.route)).toEqual(['customers/get']);
  });

  it('shows the not-found state, never a new record, at an id no record of the key can have', async () => {
    await start();
    const page = await open('/17/customers/99999999999999999');
    await errorState('the error state renders');

    expect(await (await page.getErrorLink())!.getText()).toBe('Customers');
    expect(await page.getTitle()).toBe('Customer');
    expect(document.querySelector('tm-test-details-layout')).toBeNull();
    expect(customerCalls()).toEqual([]);
  });

  it('replaces the record with the error state when a refresh fails in view mode, and brings it back on Try again', async () => {
    await start();
    const page = await openCustomer(5);
    rig.api.respond('customers/get', offline);

    await page.click('refresh');
    await errorState('the error state renders');
    expect(document.querySelector('tm-test-details-layout')).toBeNull();

    rig.api.respond('customers/get', () => customer(5));
    errorButton('Try again').click();
    await tmSettleUntil(
      () => document.querySelector('tm-test-details-layout') !== null && latest()?.details.status() === 'ready',
      'the record lands again',
    );
    expect(await page.getErrorState()).toBeNull();
    expect(await page.getTitle()).toBe(nameOf(5));
  });

  it('keeps the edit when a refresh fails in edit mode, says so in the strip, and shows the saved record after Save', async () => {
    await start();
    const page = await openCustomer(5);
    await page.click('edit');
    latest()!.details.update((model) => ({ ...model, name: 'Renamed' }));
    rig.api.respond('customers/get', offline);

    latest()!.details.refresh();
    await tmSettleUntil(() => notice() !== null, 'the strip shows the failure');
    expect(notice()).toBe("This record couldn't be refreshed. Your changes are kept.");
    expect(await page.getErrorState()).toBeNull();
    expect([await page.getMode(), latest()!.details.model()['name'], latest()!.details.status()]).toEqual(['edit', 'Renamed', 'ready']);

    rig.api.respond('customers/save', () => saved(5, 'Renamed'));
    await page.click('save');
    await tmSettleUntil(() => latest()?.details.mode() === 'view', 'the save lands');
    expect(await page.getErrorState()).toBeNull();
    expect(notice()).toBeNull();
    expect(document.querySelector('.tm-test-layout__value')?.textContent).toBe('Renamed');
  });

  it('keeps the server copy after Cancel when a refresh failed in edit mode', async () => {
    await start();
    const page = await openCustomer(5);
    await page.click('edit');
    rig.api.respond('customers/get', offline);
    latest()!.details.refresh();
    await tmSettleUntil(() => notice() !== null, 'the strip shows the failure');

    await page.click('cancel');
    await tmSettle();
    expect(await page.getMode()).toBe('view');
    expect(await page.getErrorState()).toBeNull();
    expect(notice()).toBeNull();
    expect(document.querySelector('.tm-test-layout__value')?.textContent).toBe('Customer 5');
  });

  it('shows the offline state when the layout fails to load, and loads it on Try again', async () => {
    await start();
    tmLoadTestPage.mockImplementation(() => Promise.reject(new Error('The chunk failed to load.')));
    const page = await open('/17/customers/5');
    await errorState('the error state renders');
    expect(document.querySelector('tm-test-details-layout')).toBeNull();
    expect(await page.getErrorLink()).toBeNull();

    tmLoadTestPage.mockImplementation(() => Promise.resolve(TmTestDetailsLayout));
    errorButton('Try again').click();
    await tmSettleUntil(() => latest()?.details.status() === 'ready', 'the layout loads');
    expect(await page.getErrorState()).toBeNull();
    expect(await page.getTitle()).toBe(nameOf(5));
  });

  it("renders a kit layout's cards as the band's grid items, its aside in a labelled column and its title extra after the badges", async () => {
    tmLoadTestPage.mockImplementation(() => Promise.resolve(TmTestKitLayout));
    await start();
    await open('/17/customers/5');
    await tmSettleUntil(() => document.querySelector('.tm-details__aside .tm-test-layout__aside') !== null, 'the aside renders');

    const layout = document.querySelector<HTMLElement>('.tm-details__cards > tm-test-kit-layout')!;
    const aside = document.querySelector<HTMLElement>('aside.tm-details__aside')!;
    expect(getComputedStyle(layout).display).toBe('contents');
    expect(document.querySelectorAll('.tm-details__cards > tm-test-kit-layout > .tm-details-card--half')).toHaveLength(2);
    expect(document.querySelector('.tm-details__columns')!.classList).toContain('tm-details__columns--aside');
    expect(aside.getAttribute('aria-label')).toBe('Code');
    expect(document.querySelector('tm-test-kit-layout .tm-test-layout__aside')).toBeNull();
    expect(document.querySelector('[tmpagetitleextra] .tm-test-layout__extra, .tm-details__badge .tm-test-layout__extra')).not.toBeNull();
  });

  it("applies the declaration's own rules, which load with the layout, after the platform's: the layout renders once both are in hand", async () => {
    let answer!: (rules: { readonly default: typeof currencyRule }) => void;
    await start(undefined, { validate: () => new Promise((resolve) => (answer = resolve as never)) });
    await open('/17/customers/5');
    await tmSettle();
    expect(latest()).toBeUndefined();

    answer({ default: currencyRule });
    await tmSettleUntil(() => latest()?.details.status() === 'ready', 'the layout renders');
    const form = latest()!.details.form as unknown as Readonly<Record<string, FieldTree<unknown>>>;
    // The currency is nullable, so only the declaration's rule requires it; the code keeps the platform's.
    expect(form['currencyId']!().errors().map((error) => error.kind)).toEqual(['required']);
    expect(form['code']!().errors()).toEqual([]);
  });

  it("shows the offline state when the declaration's rules fail to load, and loads both on Try again", async () => {
    let fail = true;
    await start(undefined, {
      validate: () => (fail ? Promise.reject(new Error('The chunk failed to load.')) : Promise.resolve({ default: currencyRule as never })),
    });
    await open('/17/customers/5');
    await errorState('the error state renders');
    expect(document.querySelector('tm-test-details-layout')).toBeNull();

    fail = false;
    errorButton('Try again').click();
    await tmSettleUntil(() => latest()?.details.status() === 'ready', 'the layout loads');
    const form = latest()!.details.form as unknown as Readonly<Record<string, FieldTree<unknown>>>;
    expect(form['currencyId']!().errors().map((error) => error.kind)).toEqual(['required']);
  });

  it("waits for the form's uploads before Save writes, and writes once they settle", async () => {
    await start();
    const page = await openCustomer(5);
    await page.click('edit');
    latest()!.details.update((model) => ({ ...model, name: 'Renamed' }));
    rig.api.respond('customers/save', () => saved(5, 'Renamed'));
    let finish!: (blob: TmBlobDescriptor) => void;
    const blob = { id: 88, kind: 'customer-image', fileName: 'logo.png', contentType: 'image/png', size: '512' } as unknown as TmBlobDescriptor;

    await manualChangeDetection(async () => {
      // An image still uploading holds Save: nothing is written while it runs.
      void latest()!.record.uploads.start('imageId', () => new Promise<TmBlobDescriptor>((resolve) => (finish = resolve)));
      const invoked = TestBed.inject(TmPageActions).invoke('details.save', {}, 'user');
      await tmSettle();
      expect(rig.api.calls().some((call) => call.route === 'customers/save')).toBe(false);
      expect(latest()!.details.mode()).toBe('edit');

      // Once it settles, Save goes on with the write.
      finish(blob);
      expect(await invoked).toEqual({ outcome: 'done' });
    });
    expect(rig.api.calls().filter((call) => call.route === 'customers/save')).toHaveLength(1);
    expect(latest()!.details.mode()).toBe('view');
  });

  it("waits in a clean edit for an upload in flight, then writes the file it staged", async () => {
    await start();
    const page = await openCustomer(5);
    await page.click('edit');
    const layout = latest()!;
    rig.api.respond('customers/save', () => saved(5, 'Acme'));
    let finish!: (blob: TmBlobDescriptor) => void;
    const blob = { id: 88, kind: 'customer-image', fileName: 'logo.png', contentType: 'image/png', size: '512' } as unknown as TmBlobDescriptor;

    await manualChangeDetection(async () => {
      // The pick is the edit's only change, and it has not arrived yet: Save neither leaves nor writes.
      const run = layout.record.uploads.start('imageId', () => new Promise<TmBlobDescriptor>((resolve) => (finish = resolve)));
      // As the image's editor does, the staged id becomes the field's value.
      void run.then((staged) => layout.details.update((model) => ({ ...model, imageId: staged.id })));
      const invoked = TestBed.inject(TmPageActions).invoke('details.save', {}, 'user');
      await tmSettle();
      expect(layout.details.mode()).toBe('edit');
      expect(rig.api.calls().some((call) => call.route === 'customers/save')).toBe(false);

      finish(blob);
      expect(await invoked).toEqual({ outcome: 'done' });
    });
    const writes = rig.api.calls().filter((call) => call.route === 'customers/save');
    expect(writes).toHaveLength(1);
    expect(JSON.stringify(writes[0]!.body)).toContain('"imageId":88');
  });

  it('discards an upload in flight on Cancel: its late result writes nothing, and the next Save waits on nothing', async () => {
    await start();
    const page = await openCustomer(5);
    await page.click('edit');
    const layout = latest()!;
    const record = layout.record;
    let finish!: (blob: TmBlobDescriptor) => void;
    let aborted = false;
    let wrote = false;
    const blob = { id: 88, kind: 'customer-image', fileName: 'logo.png', contentType: 'image/png', size: '512' } as unknown as TmBlobDescriptor;

    await manualChangeDetection(async () => {
      const run = record.uploads.start('imageId', (signal) => {
        signal.addEventListener('abort', () => (aborted = true));
        return new Promise<TmBlobDescriptor>((resolve) => (finish = resolve));
      });
      // As the image's editor does, a result would become the field's value.
      const settled = run.then(
        () => (wrote = true),
        (error: unknown) => error,
      );

      // --- Cancel aborts the upload; its result, arriving after, is ignored ---
      await page.click('cancel');
      await tmSettle();
      expect(layout.details.mode()).toBe('view');
      expect(aborted).toBe(true);
      expect(record.uploads.running().size).toBe(0);
      finish(blob);
      expect(await settled).toMatchObject({ name: 'AbortError' });
    });
    expect(wrote).toBe(false);
    expect(record.uploads.staged('imageId')).toBeUndefined();
    expect(record.model()).toBe(record.entity());

    // --- The next edit's Save writes at once: nothing the cancelled edit started holds it ---
    await page.click('edit');
    layout.details.update((model) => ({ ...model, name: 'Renamed' }));
    rig.api.respond('customers/save', () => saved(5, 'Renamed'));
    expect(await TestBed.inject(TmPageActions).invoke('details.save', {}, 'user')).toEqual({ outcome: 'done' });
    expect(rig.api.calls().filter((call) => call.route === 'customers/save')).toHaveLength(1);
    expect(layout.details.mode()).toBe('view');
  });

  it('writes nothing when the upload a dirty edit waited for fails, and holds Save until the file is tried again', async () => {
    await start();
    const page = await openCustomer(5);
    await page.click('edit');
    const layout = latest()!;
    layout.details.update((model) => ({ ...model, name: 'Renamed' }));
    rig.api.respond('customers/save', () => saved(5, 'Renamed'));
    let fail!: (error: unknown) => void;

    await manualChangeDetection(async () => {
      const run = layout.record.uploads.start('imageId', () => new Promise<TmBlobDescriptor>((_, reject) => (fail = reject)));
      void run.catch(() => undefined);
      const invoked = TestBed.inject(TmPageActions).invoke('details.save', {}, 'user');
      await tmSettle();
      fail(new TmApiError({ kind: 'server', status: 500 }));
      expect(await invoked).toEqual({ outcome: 'cancelled' });
      await tmSettle();
    });
    // The edit stays, so the field keeps its failure in view, and the page says why nothing was saved.
    expect(rig.api.calls().some((call) => call.route === 'customers/save')).toBe(false);
    expect(layout.details.mode()).toBe('edit');
    expect([...layout.record.uploads.failed()]).toEqual(['imageId']);
    expect(live()).toBe("Nothing was saved: a file didn't upload.");

    // --- While the file stays failed, Save refuses at once ---
    expect(await TestBed.inject(TmPageActions).invoke('details.save', {}, 'user')).toEqual({ outcome: 'cancelled' });
    expect(rig.api.calls().some((call) => call.route === 'customers/save')).toBe(false);

    // --- Once the file uploads again, Save writes it ---
    const blob = { id: 88, kind: 'customer-image', fileName: 'logo.png', contentType: 'image/png', size: '512' } as unknown as TmBlobDescriptor;
    await layout.record.uploads.start('imageId', () => Promise.resolve(blob)).then((staged) => layout.details.update((model) => ({ ...model, imageId: staged.id })));
    expect(await TestBed.inject(TmPageActions).invoke('details.save', {}, 'user')).toEqual({ outcome: 'done' });
    const writes = rig.api.calls().filter((call) => call.route === 'customers/save');
    expect(writes).toHaveLength(1);
    expect(JSON.stringify(writes[0]!.body)).toContain('"imageId":88');
  });

  it('keeps a clean edit when the upload it waited for is refused, though its editor dismissed the refusal', async () => {
    await start();
    const page = await openCustomer(5);
    await page.click('edit');
    const layout = latest()!;
    let refuse!: (error: unknown) => void;

    await manualChangeDetection(async () => {
      const run = layout.record.uploads.start('imageId', () => new Promise<TmBlobDescriptor>((_, reject) => (refuse = reject)));
      // As the image's editor does with a refusal: it shows the reason, and the field keeps its value.
      void run.catch(() => layout.record.uploads.dismiss('imageId'));
      const invoked = TestBed.inject(TmPageActions).invoke('details.save', {}, 'user');
      await tmSettle();
      refuse(new TmApiError({ kind: 'validation', status: 415, code: 'blob-rejected', problem: { code: 'blob-rejected', status: 415, instance: 'trace-1', title: 'Refused' } }));
      expect(await invoked).toEqual({ outcome: 'cancelled' });
    });
    expect(layout.details.mode()).toBe('edit');
    expect(layout.record.uploads.failed().size).toBe(0);
    expect(rig.api.calls().some((call) => call.route === 'customers/save')).toBe(false);

    // The refusal was shown; the next Save of the clean edit goes back to view mode.
    expect(await TestBed.inject(TmPageActions).invoke('details.save', {}, 'user')).toEqual({ outcome: 'done' });
    expect(layout.details.mode()).toBe('view');
  });

  it('writes nothing in create mode when an upload fails', async () => {
    await start();
    await open('/17/customers/new');
    await tmSettleUntil(() => latest() !== undefined, 'the layout renders');
    const layout = latest()!;
    layout.details.update((model) => ({ ...model, name: 'New one' }));
    let fail!: (error: unknown) => void;

    await manualChangeDetection(async () => {
      void layout.record.uploads.start('imageId', () => new Promise<TmBlobDescriptor>((_, reject) => (fail = reject))).catch(() => undefined);
      const invoked = TestBed.inject(TmPageActions).invoke('details.save', {}, 'user');
      await tmSettle();
      fail(new TmApiError({ kind: 'offline', status: 0 }));
      expect(await invoked).toEqual({ outcome: 'cancelled' });
    });
    expect(rig.api.calls().some((call) => call.route === 'customers/save')).toBe(false);
    expect(layout.details.mode()).toBe('create');
    expect(TestBed.inject(Router).url).toBe('/17/customers/new');
  });

  it('aborts an upload started while the write runs, so nothing it returns reaches the saved record', async () => {
    await start();
    const page = await openCustomer(5);
    await page.click('edit');
    const layout = latest()!;
    const record = layout.record;
    layout.details.update((model) => ({ ...model, name: 'Renamed' }));
    let answer!: (result: TmJsonObject) => void;
    rig.api.respond('customers/save', () => new Promise<TmJsonObject>((resolve) => (answer = resolve)));
    let finish!: (blob: TmBlobDescriptor) => void;
    let wrote = false;
    const blob = { id: 88, kind: 'customer-image', fileName: 'logo.png', contentType: 'image/png', size: '512' } as unknown as TmBlobDescriptor;

    await manualChangeDetection(async () => {
      const invoked = TestBed.inject(TmPageActions).invoke('details.save', {}, 'user');
      await tmSettleUntil(() => rig.api.calls().some((call) => call.route === 'customers/save'), 'the save writes');
      // A pick while the write runs: as the image's editor does, its result would become the field's value.
      const settled = record.uploads.start('imageId', () => new Promise<TmBlobDescriptor>((resolve) => (finish = resolve))).then(
        () => (wrote = true),
        (error: unknown) => error,
      );
      answer(saved(5, 'Renamed'));
      expect(await invoked).toEqual({ outcome: 'done' });
      expect(record.uploads.running().size).toBe(0);
      finish(blob);
      expect(await settled).toMatchObject({ name: 'AbortError' });
    });
    expect(wrote).toBe(false);
    expect(layout.details.mode()).toBe('view');
    expect(record.model()).toBe(record.entity());
    expect(record.uploads.staged('imageId')).toBeUndefined();
  });

  it('refuses a cancel() while Save waits for an upload in create mode, and Save then writes once', async () => {
    await start();
    await open('/17/customers/new');
    await tmSettleUntil(() => latest() !== undefined, 'the layout renders');
    const layout = latest()!;
    layout.details.update((model) => ({ ...model, code: 'C-006', name: 'New one', customerType: 'Retail', creditLimit: '0' }));
    rig.api.respond('customers/save', () => saved(6, 'New one'));
    let finish!: (blob: TmBlobDescriptor) => void;
    const blob = { id: 88, kind: 'customer-image', fileName: 'logo.png', contentType: 'image/png', size: '512' } as unknown as TmBlobDescriptor;

    await manualChangeDetection(async () => {
      void layout.record.uploads
        .start('imageId', () => new Promise<TmBlobDescriptor>((resolve) => (finish = resolve)))
        .then((staged) => layout.details.update((model) => ({ ...model, imageId: staged.id })));
      const invoked = TestBed.inject(TmPageActions).invoke('details.save', {}, 'user');
      await tmSettle();

      // A hatch's cancel() while Save waits changes nothing: the page stays, and the upload runs on.
      expect(await layout.details.cancel()).toBe(false);
      expect(layout.details.mode()).toBe('create');
      expect(TestBed.inject(Router).url).toBe('/17/customers/new');
      expect([...layout.record.uploads.running()]).toEqual(['imageId']);

      finish(blob);
      expect(await invoked).toEqual({ outcome: 'done' });
    });
    const writes = rig.api.calls().filter((call) => call.route === 'customers/save');
    expect(writes).toHaveLength(1);
    expect(JSON.stringify(writes[0]!.body)).toContain('"imageId":88');
  });

  it('aborts an upload in flight when Cancel leaves a new record, writing nothing', async () => {
    await start();
    const page = await open('/17/customers/new');
    await tmSettleUntil(() => latest() !== undefined, 'the layout renders');
    const record = latest()!.record;
    let aborted = false;

    await manualChangeDetection(async () => {
      void record.uploads
        .start('imageId', (signal) => {
          signal.addEventListener('abort', () => (aborted = true));
          return new Promise<TmBlobDescriptor>(() => undefined);
        })
        .catch(() => undefined);
      await page.click('cancel');
      await tmSettle();
    });
    expect(aborted).toBe(true);
    expect(record.uploads.running().size).toBe(0);
    expect(TestBed.inject(Location).path()).toBe('/17');
    expect(rig.api.calls().some((call) => call.route === 'customers/save')).toBe(false);
  });

  it('aborts an upload in flight once the Discard question is answered, restoring the server copy', async () => {
    await start();
    const page = await openCustomer(5);
    const root = TestbedHarnessEnvironment.documentRootLoader(router.fixture);
    await page.click('edit');
    const layout = latest()!;
    const record = layout.record;
    layout.details.update((model) => ({ ...model, name: 'Renamed' }));
    let aborted = false;

    await manualChangeDetection(async () => {
      void record.uploads
        .start('imageId', (signal) => {
          signal.addEventListener('abort', () => (aborted = true));
          return new Promise<TmBlobDescriptor>(() => undefined);
        })
        .catch(() => undefined);
      await page.click('cancel');
      await tmSettleUntil(() => document.querySelector('tm-confirm-content') !== null, 'the Discard question opens');
      const confirm = await root.getHarness(TmConfirmHarness);
      // The question is open: nothing is discarded yet.
      expect(aborted).toBe(false);
      await confirm.choose('Discard changes');
      await tmSettle();
    });
    expect(aborted).toBe(true);
    expect(record.uploads.running().size).toBe(0);
    expect(layout.details.mode()).toBe('view');
    expect(record.model()).toBe(record.entity());
    expect(layout.details.model()['name']).toBe('Customer 5');
  });

  it('refuses to leave while the write runs, keeping the page, and shows the saved record once it lands', async () => {
    await start();
    const page = await openCustomer(5);
    await page.click('edit');
    latest()!.details.update((model) => ({ ...model, name: 'Renamed' }));
    let answer!: (result: TmJsonObject) => void;
    rig.api.respond('customers/save', () => new Promise<TmJsonObject>((resolve) => (answer = resolve)));

    await manualChangeDetection(async () => {
      const invoked = TestBed.inject(TmPageActions).invoke('details.save', {}, 'user');
      await tmSettleUntil(() => rig.api.calls().some((call) => call.route === 'customers/save'), 'the save writes');
      expect(await TestBed.inject(Router).navigateByUrl('/17')).toBe(false);
      expect(TestBed.inject(Router).url).toBe('/17/customers/5');
      answer(saved(5, 'Renamed'));
      expect(await invoked).toEqual({ outcome: 'done' });
    });
    expect(latest()!.details.mode()).toBe('view');
    expect(TestBed.inject(Router).url).toBe('/17/customers/5');
  });

  it('refuses a Back while the write runs, keeping the history where it was, and goes Back once it lands', async () => {
    await start();
    await TestBed.inject(Router).navigateByUrl('/17');
    const page = await openCustomer(5);
    await page.click('edit');
    latest()!.details.update((model) => ({ ...model, name: 'Renamed' }));
    let answer!: (result: TmJsonObject) => void;
    rig.api.respond('customers/save', () => new Promise<TmJsonObject>((resolve) => (answer = resolve)));
    // The test's location keeps its entries and its position in them.
    const location = TestBed.inject(Location) as unknown as Location & { readonly _history: readonly unknown[]; readonly _historyIndex: number };
    const history = (): readonly number[] => [location._historyIndex, location._history.length];
    // The router hears the history's steps, as a browser's Back.
    TestBed.inject(Router).setUpLocationChangeListener();

    await manualChangeDetection(async () => {
      const invoked = TestBed.inject(TmPageActions).invoke('details.save', {}, 'user');
      await tmSettleUntil(() => rig.api.calls().some((call) => call.route === 'customers/save'), 'the save writes');
      const before = history();

      // --- Back while the write runs: refused, the URL, the position and the entries as they were ---
      location.back();
      await tmSettleUntil(() => location.path() === '/17/customers/5', 'the refused Back puts the URL back');
      await tmSettle();
      expect(history()).toEqual(before);

      answer(saved(5, 'Renamed'));
      expect(await invoked).toEqual({ outcome: 'done' });
    });

    // --- Once it lands, Back reaches the entry before the record's ---
    location.back();
    await tmSettleUntil(() => location.path() === '/17', 'Back leaves the record');
  });
});

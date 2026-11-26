// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Location } from '@angular/common';
import { Component, computed, type ElementRef, signal, viewChild } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { type FieldTree, required, type SchemaPath, type SchemaPathTree } from '@angular/forms/signals';
import { manualChangeDetection } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { Title } from '@angular/platform-browser';
import { Router } from '@angular/router';
import type { RouterTestingHarness } from '@angular/router/testing';
import { TmApiError, type TmJsonObject, TmPageActions } from '@tellma/core';
import { TmDetailsPageHarness, type TmFakeApiCall } from '@tellma/core-testing';
import { TmConfirmHarness } from '@tellma/core-ui-testing';

import { tmEntityRig, type TmEntityRig, type TmEntityRigOptions } from '../../src/entity/tm-entity.testing.util';
import { tmLoadTestPage, tmSettle, tmSettleUntil, TmTestPage } from '../../src/routes/tm-app.testing.util';
import { injectTmDetails } from './tm-details-context';
import { TmDetailsKit } from './tm-details-kit';

// The routed details host over the recorded customer model, with a layout
// that registers one field: the load, view → edit → cancel with no request,
// the Discard changes question, the held record under the bar on a refresh,
// a step and a stale return, the empty record while nothing is held, the
// quiet refresh, the permission reasons, the read failure's way out, a
// failed refresh in view and edit mode, a layout that fails to load, an id
// no record can have, create and clone, and a page left while Save runs.

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
    entities: [{ id, code: `C-00${id}`, name: `Customer ${id}`, isActive: true, contacts: [{ id: 100 + id, customerId: id, name: 'Huda' }] }],
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
    expect(valued(details.model())).toEqual({ code: 'C-005', name: 'Customer 5', contacts: [{ id: -1, name: 'Huda' }] });
  });

  it("saves a clone's child rows without a key to the parent, exactly as the body is sent", async () => {
    await start();
    const page = await open('/17/customers/new?clone=5');
    await tmSettleUntil(() => latest()?.details.model()['name'] !== undefined, 'the copy lands');
    const details = latest()!.details;
    // The copied customer has no type, which the platform's rules require before a save writes.
    details.update((model) => ({ ...model, customerType: 'Retail' }));
    rig.api.respond('customers/save', () => saved(6, 'Customer 5'));

    await page.click('save');
    await tmSettleUntil(() => rig.api.calls().some((call) => call.route === 'customers/save'), 'the save writes');

    // The raw body, nulls included: a null for the contact's customerId is a value the server refuses.
    const body = JSON.stringify(rig.api.calls().find((call) => call.route === 'customers/save')!.body);
    expect(body).not.toContain('customerId');
    expect(JSON.parse(body)).toEqual(expect.objectContaining({ entities: [expect.objectContaining({ contacts: [{ id: -1, name: 'Huda', email: null }] })] }));
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

  it('ends Save cleanly when the page is left while the write runs', async () => {
    await start();
    const page = await openCustomer(5);
    await page.click('edit');
    latest()!.details.update((model) => ({ ...model, name: 'Renamed' }));
    let answer!: (result: TmJsonObject) => void;
    rig.api.respond('customers/save', () => new Promise<TmJsonObject>((resolve) => (answer = resolve)));

    await manualChangeDetection(async () => {
      const invoked = TestBed.inject(TmPageActions).invoke('details.save', {}, 'user');
      await tmSettleUntil(() => rig.api.calls().some((call) => call.route === 'customers/save'), 'the save writes');
      await router.navigateByUrl('/17');
      await tmSettleUntil(() => document.querySelector('tm-details-host') === null, 'the page is left');
      answer(saved(5, 'Renamed'));
      expect(await invoked).toEqual({ outcome: 'done' });
    });
    expect(TestBed.inject(Router).url).toBe('/17');
  });
});

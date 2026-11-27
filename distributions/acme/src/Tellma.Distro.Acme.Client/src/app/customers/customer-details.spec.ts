// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgComponentOutlet } from '@angular/common';
import { provideLocationMocks } from '@angular/common/testing';
import { ApplicationInitStatus, ChangeDetectionStrategy, Component, Injector, input, type Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import type { FieldTree } from '@angular/forms/signals';
import { provideRouter } from '@angular/router';
import { provideTellma, TmEntities, type TmEntityRules, tmEnglish, type TmJsonObject } from '@tellma/core';
import {
  provideTellmaStateTesting,
  provideTellmaTesting,
  type TmFakeDetails,
  TmFieldHarness,
  tmFakeDetails,
  tmTestEntities,
  tmTestMetadata,
} from '@tellma/core-testing';
import { TmCardHarness } from '@tellma/core-ui-testing';

import { acmeFeature } from '../acme.feature';
import CustomerDetails from './customer-details';
import customerRules from './customer-rules';

// Acme's own customer layout and rules, rendered over a fake details
// context and the platform's recorded customer model: the layout in view
// mode with its collapsed audit card, then in edit mode, where a touched
// empty field shows its message once, and a corporate customer needs a
// credit limit.

/** A customer as `customers/get` returns it. */
const CUSTOMER: TmJsonObject = {
  id: 5,
  code: 'C-005',
  name: 'Al Noor Trading',
  customerType: 'Corporate',
  creditLimit: '1500.0000',
  currencyId: 3,
  isActive: true,
  createdAt: '2026-11-01T08:00:00.0000000+00:00',
  createdById: 1,
  modifiedAt: '2026-11-02T08:00:00.0000000+00:00',
  modifiedById: 1,
};

/** Renders a layout in a details record's injector, as the page's frame does. */
@Component({
  selector: 'acme-test-layout-host',
  imports: [NgComponentOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<ng-container *ngComponentOutlet="layout(); injector: injector()" />`,
})
class AcmeTestLayoutHost {
  /** The layout. */
  readonly layout = input.required<Type<unknown>>();

  /** The record's injector. */
  readonly injector = input.required<Injector>();
}

/** Lets the app's timers, microtasks and renders run. */
async function settle(): Promise<void> {
  for (let pass = 0; pass < 5; pass++) {
    await new Promise((resolve) => setTimeout(resolve, 0));
    await TestBed.inject(ApplicationInitStatus).donePromise;
  }
}

/**
 * Waits until a condition holds.
 *
 * @param condition - The condition.
 * @param what - What the wait is for, named in its failure.
 */
async function settleUntil(condition: () => boolean, what: string): Promise<void> {
  for (let pass = 0; pass < 200; pass++) {
    if (condition()) {
      return;
    }
    await settle();
  }
  throw new Error(`Timed out waiting for ${what}.`);
}

describe("Acme's customer details layout", () => {
  let details: TmFakeDetails<TmJsonObject>;
  let host: HTMLElement;
  let loader: HarnessLoader;

  beforeEach(async () => {
    // --- The platform's recorded customer model, with Acme's own feature ---
    const { summaries, chunk } = tmTestEntities();
    TestBed.configureTestingModule({
      providers: [
        provideTellma({
          metadata: {
            core: () => Promise.resolve(tmTestMetadata({ entities: summaries })),
            entities: () => Promise.resolve(chunk),
            contracts: () => Promise.resolve({ fingerprints: {}, long: [], idempotent: [] }),
            strings: { en: () => Promise.resolve({}) },
          },
          languages: [tmEnglish()],
          features: [acmeFeature],
        }),
        provideTellmaTesting(),
        provideTellmaStateTesting(),
        provideRouter([]),
        provideLocationMocks(),
      ],
    });
    await TestBed.inject(ApplicationInitStatus).donePromise;
    await TestBed.inject(TmEntities).load();

    // --- The customer, with Acme's own rules after the platform's ---
    details = TestBed.runInInjectionContext(() =>
      tmFakeDetails<TmJsonObject>({
        entity: 'acme.Customer',
        record: { entity: CUSTOMER, related: { 'acme.Currency': [{ id: 3, code: 'AED', name: 'UAE Dirham' }] }, extras: { permissions: [] } },
        rules: customerRules as unknown as TmEntityRules<TmJsonObject>,
      }),
    );
    const fixture = TestBed.createComponent(AcmeTestLayoutHost);
    fixture.componentRef.setInput('layout', CustomerDetails);
    fixture.componentRef.setInput('injector', Injector.create({ providers: details.providers, parent: TestBed.inject(Injector) }));
    host = fixture.nativeElement as HTMLElement;
    loader = TestbedHarnessEnvironment.loader(fixture);
    await settle();
  });

  /**
   * The form's node of a member.
   *
   * @param json - The member's json name.
   * @returns The node.
   */
  function node(json: string): FieldTree<unknown> {
    return (details.form as unknown as Readonly<Record<string, FieldTree<unknown>>>)[json]!;
  }

  it("declares its rules to load with the layout, as the page applies them", async () => {
    const customer = acmeFeature.entities?.find((entity) => entity.name === 'acme.Customer');
    const load = customer?.['validate'] as (() => Promise<{ readonly default: unknown }>) | undefined;

    expect((await load!()).default).toBe(customerRules);
  });

  it('renders its fields in view mode, with the collapsed audit card', async () => {
    const fields = await loader.getAllHarnesses(TmFieldHarness);
    const cards = await loader.getAllHarnesses(TmCardHarness);

    expect((await Promise.all(fields.map((field) => field.getPath()))).slice(0, 5)).toEqual(['Name', 'Code', 'CustomerType', 'CurrencyId', 'CreditLimit']);
    expect(await Promise.all(fields.map((field) => field.getMode()))).toEqual(fields.map(() => 'view'));
    expect(await (await loader.getHarness(TmFieldHarness.with({ path: 'Code' }))).getValueText()).toBe('C-005');
    expect(cards).toHaveLength(2);
    expect(await cards[1]!.isExpanded()).toBe(false);
  });

  it('shows a touched empty field’s message once in edit mode, and asks a corporate customer for a credit limit', async () => {
    details.edit();
    await settleUntil(() => host.querySelector('[data-tm-field="Code"] tm-form-field') !== null, 'the edit controls load');

    // The platform's rule: a code is required, shown once the field is touched.
    const code = await loader.getHarness(TmFieldHarness.with({ path: 'Code' }));
    node('code')().value.set('');
    await settle();
    expect(await code.getErrors()).toEqual([]);
    await code.blur();
    await settle();
    expect(await code.getErrors()).toEqual(['This field is required']);

    // Acme's rule: a corporate customer's credit limit is required, beside the platform's rule on it.
    node('creditLimit')().value.set(null);
    node('creditLimit')().markAsTouched();
    await settle();
    expect(node('creditLimit')().errors().map((error) => error.kind)).toEqual(['required', 'required']);
    expect(await (await loader.getHarness(TmFieldHarness.with({ path: 'CreditLimit' }))).getErrors()).toEqual(['This field is required']);

    // A retail customer's credit limit keeps the platform's rule alone.
    node('customerType')().value.set('Retail');
    await settle();
    expect(node('creditLimit')().errors().map((error) => error.kind)).toEqual(['required']);
  });
});

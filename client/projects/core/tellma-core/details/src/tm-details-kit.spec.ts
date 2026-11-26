// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgComponentOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, Injector, input, type Type } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import type { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import type { TmJsonObject } from '@tellma/core';
import { type TmFakeDetails, type TmFakeDetailsOptions, TmFieldHarness, tmFakeDetails } from '@tellma/core-testing';
import { TmCardHarness, TmTabGroupHarness } from '@tellma/core-ui-testing';
import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';

import { tmEntityRig } from '../../src/entity/tm-entity.testing.util';
import { tmSettle, tmSettleUntil } from '../../src/routes/tm-app.testing.util';
import { TmDetailsKit } from './tm-details-kit';
import { TM_DETAILS_RECORD, type TmDetailsRecordState } from './tm-details-record';

// The layout kit over the recorded customer model, rendered in a fake
// details context: each kind of field in view and edit mode with the same
// geometry, the two-state skeleton rule, a collapsed card's error dot and a
// field's focus() opening it, tabs in a card's section, the development
// errors, and Acme's customer layout with its messages on touch.

/** A customer as `customers/get` returns it. */
const CUSTOMER: TmJsonObject = {
  id: 5,
  code: 'C-005',
  name: 'Al Noor Trading',
  name2: 'النور للتجارة',
  customerType: 'Retail',
  creditLimit: '1500.0000',
  rank: 'A',
  currencyId: 3,
  isActive: true,
  createdAt: '2026-11-01T08:00:00.0000000+00:00',
  createdById: 1,
  modifiedAt: '2026-11-02T08:00:00.0000000+00:00',
  modifiedById: 1,
  contacts: [
    { id: 11, customerId: 5, name: 'Huda', email: null },
    { id: 12, customerId: 5, name: 'Omar', email: null },
  ],
};

/** The rows the read relates to the customer. */
const RELATED: Readonly<Record<string, readonly TmJsonObject[]>> = {
  'acme.Currency': [{ id: 3, code: 'AED', name: 'UAE Dirham' }],
  'core.User': [{ id: 1, name: 'Admin' }],
};

/** Renders a layout in a details record's injector, as the page's frame does. */
@Component({
  selector: 'tm-test-kit-host',
  imports: [NgComponentOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<ng-container *ngComponentOutlet="layout(); injector: injector()" />`,
})
class TmTestKitHost {
  /** The layout. */
  readonly layout = input.required<Type<unknown>>();

  /** The record's injector. */
  readonly injector = input.required<Injector>();
}

/** Counts the instances of a layout's own field templates. */
@Component({ selector: 'tm-test-counted', template: '<span class="tm-test-counted">{{ kind() }}</span>' })
class TmTestCounted {
  /** The instances, by kind. */
  static readonly made: string[] = [];

  /** The kind of template. */
  readonly kind = input.required<string>();

  /** Counts the instance. */
  constructor() {
    queueMicrotask(() => TmTestCounted.made.push(this.kind()));
  }
}

/** Each end kind of path: a value, a multilingual group, an enum, a navigation, a decimal and a path through a navigation. */
@Component({
  selector: 'tm-test-kinds-layout',
  imports: [TmDetailsKit],
  template: `
    <tm-details-card>
      <tm-field path="Code" />
      <tm-field path="Name" />
      <tm-field path="CustomerType" />
      <tm-field path="Currency" />
      <tm-field path="CreditLimit" hintKey="Code" />
      <tm-field path="Currency.Code" />
      <tm-field path="Rank" readonly labelKey="Name" />
    </tm-details-card>
  `,
})
class TmTestKindsLayout {}

/** A field with the layout's own templates in both modes. */
@Component({
  selector: 'tm-test-templates-layout',
  imports: [TmDetailsKit, TmTestCounted],
  template: `
    <tm-details-card>
      <tm-field path="Code">
        <ng-template tmFieldView><tm-test-counted kind="view" /></ng-template>
        <ng-template tmFieldEdit><tm-test-counted kind="edit" /></ng-template>
      </tm-field>
    </tm-details-card>
  `,
})
class TmTestTemplatesLayout {}

/** A collapsed card. */
@Component({
  selector: 'tm-test-collapsed-layout',
  imports: [TmDetailsKit],
  template: `
    <tm-details-card id="terms" titleKey="Code" collapsible collapsed>
      <tm-field path="Code" />
    </tm-details-card>
  `,
})
class TmTestCollapsedLayout {}

/** Tabs in a card's section, over the contacts. */
@Component({
  selector: 'tm-test-section-layout',
  imports: [TmDetailsKit],
  template: `
    <tm-details-card>
      <tm-field path="Code" />
      <tm-details-tabs tmDetailsCardSection>
        <tm-details-tab id="contacts" collection="Contacts">
          <ng-template tmDetailsTabContent><p class="tm-test-contacts">The contacts</p></ng-template>
        </tm-details-tab>
        <tm-details-tab id="notes" labelKey="Name">
          <ng-template tmDetailsTabContent><tm-field path="Name" /></ng-template>
        </tm-details-tab>
      </tm-details-tabs>
    </tm-details-card>
  `,
})
class TmTestSectionLayout {}

/** Tabs in a card's field grid. */
@Component({
  selector: 'tm-test-grid-tabs-layout',
  imports: [TmDetailsKit],
  template: `
    <tm-details-card>
      <tm-details-tabs><tm-details-tab id="a" labelKey="Code" /></tm-details-tabs>
    </tm-details-card>
  `,
})
class TmTestGridTabsLayout {}

/** Two tabs of one id. */
@Component({
  selector: 'tm-test-twin-tabs-layout',
  imports: [TmDetailsKit],
  template: `
    <tm-details-tabs><tm-details-tab id="a" labelKey="Code" /></tm-details-tabs>
    <tm-details-tabs><tm-details-tab id="b" labelKey="Code" /></tm-details-tabs>
  `,
})
class TmTestTwinTabsLayout {}

/** Two asides. */
@Component({
  selector: 'tm-test-twin-asides-layout',
  imports: [TmDetailsKit],
  template: `
    <tm-details-aside><p>One</p></tm-details-aside>
    <tm-details-aside><p>Two</p></tm-details-aside>
  `,
})
class TmTestTwinAsidesLayout {}

/** An extra the customer does not declare. */
@Component({
  selector: 'tm-test-undeclared-extra-layout',
  imports: [TmDetailsKit],
  template: `<p *tmDetailsExtra="'related-documents'; let rows">{{ rows }}</p>`,
})
class TmTestUndeclaredExtraLayout {}

/** Acme's customer layout, as the distribution writes it. */
@Component({
  selector: 'tm-test-customer-layout',
  imports: [TmDetailsKit],
  template: `
    <tm-details-card>
      <tm-field path="Name" />
      <tm-field path="Code" />
      <tm-field path="CustomerType" />
      <tm-field path="Currency" />
      <tm-field path="CreditLimit" />
    </tm-details-card>
    <tm-details-audit />
  `,
})
class TmTestCustomerLayout {}

/** A rendered layout. */
interface TmTestRendered {
  /** The fake context. */
  readonly details: TmFakeDetails<TmJsonObject>;
  /** The record the kit reads. */
  readonly record: TmDetailsRecordState<TmJsonObject>;
  /** The host's fixture. */
  readonly fixture: ComponentFixture<TmTestKitHost>;
  /** The harness loader. */
  readonly loader: HarnessLoader;
}

/** The unit page loads no token stylesheet; the fields' geometry needs one. */
let tokenSheet: HTMLStyleElement;

beforeAll(() => {
  tokenSheet = document.createElement('style');
  tokenSheet.textContent = tmEmitCss(tmTokensDefault);
  document.head.appendChild(tokenSheet);
});

afterAll(() => tokenSheet.remove());

describe("the details page's layout kit", () => {
  beforeEach(async () => {
    TmTestCounted.made.length = 0;
    await tmEntityRig({ languages: 2 });
  });

  /**
   * A fake context over the customer, in the record's injector.
   *
   * @param options - The context's options; the customer, landed, in view mode when omitted.
   * @returns The context and the injector a layout renders in.
   */
  function context(options: Partial<TmFakeDetailsOptions<TmJsonObject>> = {}): { readonly details: TmFakeDetails<TmJsonObject>; readonly injector: Injector } {
    const details = TestBed.runInInjectionContext(() =>
      tmFakeDetails<TmJsonObject>({ entity: 'acme.Customer', record: { entity: CUSTOMER, related: RELATED, extras: { permissions: [] } }, ...options }),
    );
    return { details, injector: Injector.create({ providers: details.providers, parent: TestBed.inject(Injector) }) };
  }

  /**
   * Renders a layout over the customer.
   *
   * @param layout - The layout.
   * @param options - The context's options.
   * @returns The rendering.
   */
  async function render(layout: Type<unknown>, options: Partial<TmFakeDetailsOptions<TmJsonObject>> = {}): Promise<TmTestRendered> {
    const { details, injector } = context(options);
    const fixture = TestBed.createComponent(TmTestKitHost);
    fixture.componentRef.setInput('layout', layout);
    fixture.componentRef.setInput('injector', injector);
    await tmSettle();
    return { details, record: injector.get(TM_DETAILS_RECORD) as TmDetailsRecordState<TmJsonObject>, fixture, loader: TestbedHarnessEnvironment.loader(fixture) };
  }

  /**
   * Renders a layout that should fail, and returns the error.
   *
   * @param layout - The layout.
   * @returns The error's message.
   */
  function failure(layout: Type<unknown>): string {
    const { injector } = context();
    const fixture = TestBed.createComponent(TmTestKitHost);
    fixture.componentRef.setInput('layout', layout);
    fixture.componentRef.setInput('injector', injector);
    try {
      fixture.detectChanges();
    } catch (error) {
      return (error as Error).message;
    }
    return '';
  }

  /**
   * Enters edit mode and waits for the edit controls to load.
   *
   * @param rendered - The rendering.
   */
  async function edit(rendered: TmTestRendered): Promise<void> {
    rendered.details.edit();
    await tmSettleUntil(() => rendered.fixture.nativeElement.querySelector('tm-form-field') !== null, 'the edit controls load');
    await tmSettle();
  }

  describe('fields', () => {
    it('renders each kind of path in view mode: one cell per language of a group, a navigation as its row, a hint, and no lock', async () => {
      const { loader } = await render(TmTestKindsLayout);
      const cells = await loader.getAllHarnesses(TmFieldHarness);

      expect(await Promise.all(cells.map((cell) => cell.getPath()))).toEqual(['Code', 'Name', 'Name2', 'CustomerType', 'CurrencyId', 'CreditLimit', 'Currency.Code', 'Rank']);
      expect(await Promise.all(cells.map((cell) => cell.getMode()))).toEqual(Array(8).fill('view'));
      expect(await cells[0]!.getValueText()).toBe('C-005');
      expect(await cells[2]!.getValueText()).toBe('النور للتجارة');
      expect(await cells[4]!.getValueText()).toContain('AED');
      expect(await cells[6]!.getValueText()).toBe('AED');
      expect(await cells[5]!.getHint()).toBe('Code');
      expect(await Promise.all(cells.map((cell) => cell.isLocked()))).toEqual(Array(8).fill(false));
    });

    it('renders the editable ones as controls in edit mode, and a path through a navigation and a read-only field with the lock', async () => {
      const rendered = await render(TmTestKindsLayout);
      await edit(rendered);
      const cells = await rendered.loader.getAllHarnesses(TmFieldHarness);

      expect(await Promise.all(cells.map((cell) => cell.getMode()))).toEqual(['edit', 'edit', 'edit', 'edit', 'edit', 'edit', 'view', 'view']);
      expect(await Promise.all(cells.map((cell) => cell.isLocked()))).toEqual([false, false, false, false, false, false, true, true]);
      expect(await cells[7]!.getLabel()).toBe('Name');
      expect(await cells[5]!.getHint()).toBe('Code');
    });

    it('draws the label row and the value box at the same place and size in both modes', async () => {
      const rendered = await render(TmTestKindsLayout);
      const view = await Promise.all((await rendered.loader.getAllHarnesses(TmFieldHarness)).map((cell) => cell.getGeometry()));
      await edit(rendered);
      // The navigation's picker renders once its picker page loads.
      await tmSettleUntil(() => rendered.fixture.nativeElement.querySelector('[data-tm-field="CurrencyId"] .tm-entity-picker') !== null, 'the picker renders');
      const edited = await Promise.all((await rendered.loader.getAllHarnesses(TmFieldHarness)).map((cell) => cell.getGeometry()));

      expect(edited).toEqual(view);
    });

    it("instantiates neither of a field's own templates while the record loads with nothing held, then the view template", async () => {
      const rendered = await render(TmTestTemplatesLayout, { record: undefined, id: 5 });
      const [cell] = await rendered.loader.getAllHarnesses(TmFieldHarness);

      expect(await cell!.getMode()).toBe('loading');
      await tmSettle();
      expect(TmTestCounted.made).toEqual([]);

      rendered.details.land({ entity: CUSTOMER, related: RELATED, extras: { permissions: [] } });
      await tmSettle();
      expect(TmTestCounted.made).toEqual(['view']);

      await edit(rendered);
      expect(TmTestCounted.made).toEqual(['view', 'edit']);
    });

    it('registers each cell with the record, editable outside view mode', async () => {
      const rendered = await render(TmTestKindsLayout);
      const fields = rendered.record.fields();

      expect([...fields.keys()]).toEqual(expect.arrayContaining(['Code', 'Name', 'Name2', 'CurrencyId', 'CreditLimit']));
      expect(fields.get('Code')!.editable()).toBe(false);

      await edit(rendered);
      expect(rendered.record.fields().get('Code')!.editable()).toBe(true);
    });
  });

  describe('cards', () => {
    it('shows a collapsed card’s error dot once a field beneath it shows an error, and a field’s focus() expands it', async () => {
      const rendered = await render(TmTestCollapsedLayout);
      await edit(rendered);
      const card = await rendered.loader.getHarness(TmCardHarness);

      expect(await card.isExpanded()).toBe(false);
      expect(await card.isBodyInert()).toBe(true);
      expect(await card.isInvalid()).toBe(false);

      const code = (rendered.details.form as unknown as Readonly<Record<string, () => { value: { set(value: string): void }; markAsTouched(): void }>>)['code']!;
      code().value.set('');
      code().markAsTouched();
      await tmSettle();
      expect(await card.isInvalid()).toBe(true);
      expect(await card.isExpanded()).toBe(false);

      rendered.record.fields().get('Code')!.focus();
      await tmSettle();
      expect(await card.isExpanded()).toBe(true);
      expect(rendered.fixture.nativeElement.querySelector('[data-tm-field="Code"]').contains(document.activeElement)).toBe(true);
    });

    it('remembers its expanded state in the record by its id', async () => {
      const rendered = await render(TmTestCollapsedLayout);
      const card = await rendered.loader.getHarness(TmCardHarness);
      await card.toggle();

      expect(rendered.record.view.cards()).toEqual({ terms: true });
    });

    it('renders tabs marked as its section below the field grid, with the collection’s label and live count', async () => {
      const rendered = await render(TmTestSectionLayout);
      const section = rendered.fixture.nativeElement.querySelector('.tm-details-card__body > tm-details-tabs.tm-details-card__section');
      const tabs = await rendered.loader.getHarness(TmTabGroupHarness);
      const labels = await Promise.all((await tabs.getTabs()).map((tab) => tab.getLabel()));

      expect(section).not.toBeNull();
      expect(rendered.fixture.nativeElement.querySelector('.tm-details-card__grid tm-details-tabs')).toBeNull();
      expect(labels[0]).toMatch(/2/);
      expect(await tabs.getActivePanelText()).toContain('The contacts');

      await tabs.selectTab(labels[1]!);
      expect(rendered.record.view.tabs()).toEqual({ main: 'notes' });
    });
  });

  describe('development errors', () => {
    it('refuses tabs in a card’s field grid, naming the attribute', () => {
      expect(failure(TmTestGridTabsLayout)).toMatch(/tmDetailsCardSection/);
    });

    it('refuses two tabs of one id', () => {
      expect(failure(TmTestTwinTabsLayout)).toMatch(/have the id 'main'/);
    });

    it('refuses a second aside', () => {
      expect(failure(TmTestTwinAsidesLayout)).toMatch(/one tm-details-aside/);
    });

    it('refuses an extra the entity does not declare', () => {
      expect(failure(TmTestUndeclaredExtraLayout)).toMatch(/'related-documents' is not one acme\.Customer declares/);
    });
  });

  describe("Acme's customer layout", () => {
    it('renders in view mode with the collapsed audit card, and in edit mode shows a message once a field is touched', async () => {
      const rendered = await render(TmTestCustomerLayout);
      const cards = await rendered.loader.getAllHarnesses(TmCardHarness);

      expect(await cards[1]!.getHeading()).toBe('Audit info');
      expect(await cards[1]!.isExpanded()).toBe(false);
      expect(await (await rendered.loader.getHarness(TmFieldHarness.with({ path: 'Code' }))).getValueText()).toBe('C-005');

      await edit(rendered);
      const code = await rendered.loader.getHarness(TmFieldHarness.with({ path: 'Code' }));
      expect(await code.getMode()).toBe('edit');

      const form = rendered.details.form as unknown as Readonly<Record<string, () => { value: { set(value: string): void } }>>;
      form['code']!().value.set('');
      await tmSettle();
      expect(await code.getErrors()).toEqual([]);

      await code.blur();
      await tmSettle();
      expect(await code.getErrors()).toEqual(['This field is required']);
    });
  });
});

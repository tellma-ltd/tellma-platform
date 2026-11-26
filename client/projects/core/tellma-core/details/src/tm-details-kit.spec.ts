// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgComponentOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, Injector, input, signal, type Type } from '@angular/core';
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
import { ɵtmFieldEditorLoader } from './tm-field';

// The layout kit over the recorded customer model, rendered in a fake
// details context: each kind of field in view and edit mode with the same
// geometry, the two-state skeleton rule, a collapsed card's error dot and a
// field's focus() opening it, tabs, extras, the audit card, a failed load
// of the edit side, and the development errors. Acme's own layout has its
// spec in the distribution.

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

/** The customer's rank, a member the server fills when a save leaves it empty. */
@Component({
  selector: 'tm-test-rank-layout',
  imports: [TmDetailsKit],
  template: `<tm-details-card><tm-field path="Rank" /></tm-details-card>`,
})
class TmTestRankLayout {}

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

/** Tabs that are a card's own child, unmarked, as the invoice's lines and attachments are. */
@Component({
  selector: 'tm-test-direct-tabs-layout',
  imports: [TmDetailsKit],
  template: `
    <tm-details-card flush>
      <tm-details-tabs>
        <tm-details-tab id="contacts" collection="Contacts">
          <ng-template tmDetailsTabContent><p class="tm-test-contacts">The contacts</p></ng-template>
        </tm-details-tab>
      </tm-details-tabs>
    </tm-details-card>
  `,
})
class TmTestDirectTabsLayout {}

/** Tabs in a card's field grid, inside an element that is not a section. */
@Component({
  selector: 'tm-test-grid-tabs-layout',
  imports: [TmDetailsKit],
  template: `
    <tm-details-card>
      <div>
        <tm-details-tabs><tm-details-tab id="a" labelKey="Code" /></tm-details-tabs>
      </div>
    </tm-details-card>
  `,
})
class TmTestGridTabsLayout {}

/** A component of the layout's own whose view holds tabs. */
@Component({
  selector: 'tm-test-own-tabs',
  imports: [TmDetailsKit],
  template: `<tm-details-tabs><tm-details-tab id="a" labelKey="Code" /></tm-details-tabs>`,
})
class TmTestOwnTabs {}

/** A card that holds that component among its fields. */
@Component({
  selector: 'tm-test-component-tabs-layout',
  imports: [TmDetailsKit, TmTestOwnTabs],
  template: `
    <tm-details-card>
      <tm-field path="Code" />
      <tm-test-own-tabs />
    </tm-details-card>
  `,
})
class TmTestComponentTabsLayout {}

/** A card that holds that component as its section. */
@Component({
  selector: 'tm-test-component-section-layout',
  imports: [TmDetailsKit, TmTestOwnTabs],
  template: `
    <tm-details-card>
      <tm-field path="Code" />
      <tm-test-own-tabs tmDetailsCardSection />
    </tm-details-card>
  `,
})
class TmTestComponentSectionLayout {}

/** Tabs whose first tab holds a field and whose second shows the contacts. */
@Component({
  selector: 'tm-test-error-tabs-layout',
  imports: [TmDetailsKit],
  template: `
    <tm-details-tabs>
      <tm-details-tab id="general" labelKey="Code">
        <ng-template tmDetailsTabContent><tm-field path="Code" /></ng-template>
      </tm-details-tab>
      <tm-details-tab id="contacts" collection="Contacts">
        <ng-template tmDetailsTabContent><p class="tm-test-contacts">The contacts</p></ng-template>
      </tm-details-tab>
    </tm-details-tabs>
  `,
})
class TmTestErrorTabsLayout {}

/** Tabs whose selection a layout binds both ways. */
@Component({
  selector: 'tm-test-selected-layout',
  imports: [TmDetailsKit],
  template: `
    <tm-details-tabs [(selected)]="selected">
      <tm-details-tab id="contacts" collection="Contacts">
        <ng-template tmDetailsTabContent><p class="tm-test-contacts">The contacts</p></ng-template>
      </tm-details-tab>
      <tm-details-tab id="notes" labelKey="Name">
        <ng-template tmDetailsTabContent><p class="tm-test-notes">The notes</p></ng-template>
      </tm-details-tab>
    </tm-details-tabs>
  `,
})
class TmTestSelectedLayout {
  /** The latest instance. */
  static latest: TmTestSelectedLayout | null = null;

  /** The selected tab, which the tabs write back. */
  readonly selected = signal<string | undefined>('notes');

  /** Keeps the instance for the test. */
  constructor() {
    TmTestSelectedLayout.latest = this;
  }
}

/** One of the record's extras, as its template receives it. */
@Component({
  selector: 'tm-test-extra-layout',
  imports: [TmDetailsKit],
  template: `<p class="tm-test-extra" *tmDetailsExtra="'permissions'; let rows">{{ text(rows) }}</p>`,
})
class TmTestExtraLayout {
  /**
   * The extra's value as text.
   *
   * @param rows - What the template receives.
   * @returns Its JSON.
   */
  protected text(rows: unknown): string {
    return JSON.stringify(rows);
  }
}

/** The audit card alone. */
@Component({
  selector: 'tm-test-audit-layout',
  imports: [TmDetailsKit],
  template: `<tm-details-audit />`,
})
class TmTestAuditLayout {}

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
   * A node of the form, by its json names and row indices.
   *
   * @param rendered - The rendering.
   * @param path - The path.
   * @returns The node.
   */
  function nodeAt(rendered: TmTestRendered, ...path: readonly (string | number)[]): () => { readonly value: { set(value: unknown): void }; markAsTouched(): void } {
    return path.reduce<unknown>((node, segment) => (node as Readonly<Record<string | number, unknown>>)[segment], rendered.details.form) as () => {
      readonly value: { set(value: unknown): void };
      markAsTouched(): void;
    };
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

    describe('when the edit side fails to load', () => {
      const load = ɵtmFieldEditorLoader.load;

      afterEach(() => {
        ɵtmFieldEditorLoader.load = load;
      });

      /** Makes every load of the edit side fail, as a chunk that is gone after a deploy does. */
      function failLoads(): void {
        ɵtmFieldEditorLoader.load = () => Promise.reject(new Error('The chunk failed to load.'));
        ɵtmFieldEditorLoader.loaded.set(null);
      }

      it('loads it again on the mode change after a failed prefetch', async () => {
        failLoads();
        const rendered = await render(TmTestKindsLayout);
        await tmSettle();
        ɵtmFieldEditorLoader.load = load;

        await edit(rendered);
        const code = await rendered.loader.getHarness(TmFieldHarness.with({ path: 'Code' }));
        expect(await code.getMode()).toBe('edit');
      });

      it('says so in each editable field with Try again, and Try again loads the controls', async () => {
        failLoads();
        const rendered = await render(TmTestKindsLayout);
        rendered.details.edit();
        await tmSettleUntil(() => rendered.fixture.nativeElement.querySelector('.tm-field__failed') !== null, 'the failure shows');

        const code = rendered.fixture.nativeElement.querySelector('[data-tm-field="Code"]') as HTMLElement;
        expect(code.querySelector('.tm-field__failed')?.textContent).toContain("This field couldn't be loaded for editing.");
        // The failure is the page's: every editable field shows it, and one announces it.
        expect(rendered.fixture.nativeElement.querySelectorAll('.tm-field__failed').length).toBeGreaterThan(1);
        expect(rendered.fixture.nativeElement.querySelectorAll('.tm-field__failed [role="alert"]').length).toBe(1);
        expect(code.querySelector('tm-form-field')).toBeNull();
        // A read-only field shows its lock, as it does whenever the edit side loads.
        expect(rendered.fixture.nativeElement.querySelector('[data-tm-field="Rank"] .tm-field__failed')).toBeNull();

        ɵtmFieldEditorLoader.load = load;
        code.querySelector<HTMLButtonElement>('.tm-field__retry')!.click();
        await tmSettleUntil(() => rendered.fixture.nativeElement.querySelector('[data-tm-field="Code"] tm-form-field') !== null, 'the controls load');
        expect(rendered.fixture.nativeElement.querySelector('.tm-field__failed')).toBeNull();
      });

      it("loads it on a field's focus() after a failed load, and focuses the control", async () => {
        failLoads();
        const rendered = await render(TmTestKindsLayout);
        rendered.details.edit();
        await tmSettleUntil(() => rendered.fixture.nativeElement.querySelector('.tm-field__failed') !== null, 'the failure shows');

        ɵtmFieldEditorLoader.load = load;
        rendered.record.fields().get('Code')!.focus();
        await tmSettleUntil(() => rendered.fixture.nativeElement.querySelector('[data-tm-field="Code"] tm-form-field') !== null, 'the controls load');
        await tmSettle();
        expect(rendered.fixture.nativeElement.querySelector('[data-tm-field="Code"]').contains(document.activeElement)).toBe(true);
      });
    });

    it('lets the user fill a member the server fills when a save leaves it empty, on a new record', async () => {
      const rendered = await render(TmTestRankLayout, { mode: 'create', record: undefined });
      await tmSettleUntil(() => rendered.fixture.nativeElement.querySelector('[data-tm-field="Rank"] tm-form-field') !== null, 'the controls load');
      const rank = await rendered.loader.getHarness(TmFieldHarness.with({ path: 'Rank' }));

      expect(await rank.getMode()).toBe('edit');
      expect(await rank.isLocked()).toBe(false);
      expect(rendered.record.fields().get('Rank')!.editable()).toBe(true);
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

    it('shows the error dot of a record that lands after the layout renders', async () => {
      const rendered = await render(TmTestCollapsedLayout, { record: undefined, id: 5 });
      rendered.details.land({ entity: CUSTOMER, related: RELATED, extras: { permissions: [] } });
      await tmSettle();
      await edit(rendered);
      const card = await rendered.loader.getHarness(TmCardHarness);

      const code = nodeAt(rendered, 'code');
      code().value.set('');
      code().markAsTouched();
      await tmSettle();
      expect(await card.isInvalid()).toBe(true);
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

    it('renders tabs that are its own child as its section, unmarked', async () => {
      const rendered = await render(TmTestDirectTabsLayout);
      const tabs = await rendered.loader.getHarness(TmTabGroupHarness);

      expect(rendered.fixture.nativeElement.querySelector('.tm-details-card__body > tm-details-tabs')).not.toBeNull();
      expect(rendered.fixture.nativeElement.querySelector('.tm-details-card__grid tm-details-tabs')).toBeNull();
      expect(await tabs.getActivePanelText()).toContain('The contacts');
    });
  });

  describe('tabs', () => {
    /**
     * Each tab's error name, or `null` while it holds no error, and whether its dot shows.
     *
     * @param rendered - The rendering.
     * @returns The states, in tab order.
     */
    function errorStates(rendered: TmTestRendered): { readonly name: string | null; readonly dot: boolean }[] {
      const labels = [...(rendered.fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.tm-details-tabs__label')];
      return labels.map((label) => ({
        name: label.querySelector('.tm-details-tabs__name')?.textContent?.trim() ?? null,
        dot: label.querySelector('.tm-details-tabs__dot--on') !== null,
      }));
    }

    it('shows the error dot and the error name on a tab whose field shows an error, and on one whose collection holds an error, selected or not', async () => {
      const rendered = await render(TmTestErrorTabsLayout);
      await edit(rendered);
      expect(errorStates(rendered)).toEqual([
        { name: null, dot: false },
        { name: null, dot: false },
      ]);

      // A field on the selected tab, touched and empty.
      const code = nodeAt(rendered, 'code');
      code().value.set('');
      code().markAsTouched();
      await tmSettle();
      expect(errorStates(rendered)[0]).toEqual({ name: 'Code, has errors', dot: true });

      // A row of the unselected tab's collection, touched and empty.
      const contact = nodeAt(rendered, 'contacts', 0, 'name');
      contact().value.set('');
      contact().markAsTouched();
      await tmSettle();
      const [, contacts] = errorStates(rendered);
      expect(contacts!.dot).toBe(true);
      expect(contacts!.name).toMatch(/, has errors$/);
    });

    it("shows the error dot and the error name of a field on a record that lands after the layout renders", async () => {
      const rendered = await render(TmTestErrorTabsLayout, { record: undefined, id: 5 });
      rendered.details.land({ entity: CUSTOMER, related: RELATED, extras: { permissions: [] } });
      await tmSettle();
      await edit(rendered);

      const code = nodeAt(rendered, 'code');
      code().value.set('');
      code().markAsTouched();
      await tmSettle();
      expect(errorStates(rendered)[0]).toEqual({ name: 'Code, has errors', dot: true });
    });

    it('binds its selection both ways: the layout selects a tab, and a click writes the selection back', async () => {
      const rendered = await render(TmTestSelectedLayout);
      const tabs = await rendered.loader.getHarness(TmTabGroupHarness);
      expect(await tabs.getActivePanelText()).toContain('The notes');

      TmTestSelectedLayout.latest!.selected.set('contacts');
      await tmSettle();
      expect(await tabs.getActivePanelText()).toContain('The contacts');

      const labels = await Promise.all((await tabs.getTabs()).map((tab) => tab.getLabel()));
      await tabs.selectTab(labels[1]!);
      await tmSettle();
      expect(TmTestSelectedLayout.latest!.selected()).toBe('notes');
      expect(rendered.record.view.tabs()).toEqual({ main: 'notes' });
    });
  });

  describe('extras', () => {
    /**
     * The extra's rendered text.
     *
     * @param rendered - The rendering.
     * @returns The text, or `null` while nothing renders.
     */
    function shown(rendered: TmTestRendered): string | null {
      return (rendered.fixture.nativeElement as HTMLElement).querySelector('.tm-test-extra')?.textContent ?? null;
    }

    it('holds three skeleton lines while the record loads with nothing held, then renders the value', async () => {
      const rendered = await render(TmTestExtraLayout, { record: undefined, id: 5 });
      const skeleton = (rendered.fixture.nativeElement as HTMLElement).querySelector('tm-skeleton');

      expect(skeleton?.querySelectorAll('.tm-skeleton__bar')).toHaveLength(3);
      expect(shown(rendered)).toBeNull();

      rendered.details.land({ entity: CUSTOMER, extras: { permissions: [{ id: 5 }] } });
      await tmSettle();
      expect((rendered.fixture.nativeElement as HTMLElement).querySelector('tm-skeleton')).toBeNull();
      expect(shown(rendered)).toBe('[{"id":5}]');
    });

    it('renders nothing when the read returned no such extra', async () => {
      const rendered = await render(TmTestExtraLayout, { record: { entity: CUSTOMER, extras: {} } });

      expect(shown(rendered)).toBeNull();
      expect((rendered.fixture.nativeElement as HTMLElement).querySelector('tm-skeleton')).toBeNull();
    });

    it("gives a query row set's rows as objects keyed by column name, and renders again when the value changes", async () => {
      const rowSet = { columns: [{ name: 'id' }, { name: 'refused' }], rows: [[5, null], [6]] };
      const rendered = await render(TmTestExtraLayout, { record: { entity: CUSTOMER, extras: { permissions: rowSet } } });

      expect(shown(rendered)).toBe('[{"id":5,"refused":null},{"id":6,"refused":null}]');

      rendered.details.land({ entity: CUSTOMER, extras: { permissions: { columns: [{ name: 'id' }], rows: [[7]] } } });
      await tmSettle();
      expect(shown(rendered)).toBe('[{"id":7}]');
    });
  });

  describe('the audit card', () => {
    it('shows who created and last modified the record and when, in a collapsed card', async () => {
      const rendered = await render(TmTestAuditLayout);
      const card = await rendered.loader.getHarness(TmCardHarness);
      expect(await card.isExpanded()).toBe(false);
      await card.toggle();
      const cells = await rendered.loader.getAllHarnesses(TmFieldHarness);

      expect(await Promise.all(cells.map((cell) => cell.getPath()))).toEqual(['CreatedById', 'CreatedAt', 'ModifiedById', 'ModifiedAt']);
      expect(await cells[0]!.getValueText()).toMatch(/Admin$/);
      expect(await cells[2]!.getValueText()).toMatch(/Admin$/);
      expect(await cells[1]!.getValueText()).toMatch(/2026/);
      expect(await cells[3]!.getValueText()).toMatch(/2026/);
    });

    it('renders nothing for an entity without audit columns', async () => {
      const rendered = await render(TmTestAuditLayout, { entity: 'acme.CustomerContact', record: { entity: { id: 11, customerId: 5, name: 'Huda' } } });

      expect((rendered.fixture.nativeElement as HTMLElement).querySelector('tm-details-card')).toBeNull();
    });
  });

  describe('development errors', () => {
    it('refuses tabs in a card’s field grid, inside an element that is not a section, naming the attribute', () => {
      expect(failure(TmTestGridTabsLayout)).toMatch(/tmDetailsCardSection/);
    });

    it("refuses tabs in a card's field grid inside a component of the layout's own, and takes that component as a section", () => {
      expect(failure(TmTestComponentTabsLayout)).toMatch(/tmDetailsCardSection/);
      expect(failure(TmTestComponentSectionLayout)).toBe('');
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
});

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, inject, signal, type Type } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import type { HarnessLoader } from '@angular/cdk/testing';
import type { TmId } from '@tellma/core';
import { TmEntityFieldHarness } from '@tellma/core-testing';
import type { TmEntityPickerPageData } from '@tellma/core-ui/entity-picker';
import { TM_MODAL_DATA, TmModalRef } from '@tellma/core-ui/modal';
import { TmButtonHarness, TmEmptyStateHarness, TmModalHarness, type TmEntityPickerHarness } from '@tellma/core-ui-testing';

import { tmEntityRig, type TmEntityRig } from '../../src/entity/tm-entity.testing.util';
import { tmSettle } from '../../src/routes/tm-app.testing.util';
import { TmEntityField } from './tm-entity-field';
import { ɵTM_PICKER_PAGES, type TmEntityPickerContext, type ɵTmPickerPages } from './tm-lazy-picker-page';

// The page an entity picker opens: not available while no page family
// provides one, a loaded page rendered in the modal with the picker's data,
// the family each kind loads, each family providing its own half, a retry
// after a failed load, and one report per page shown.

/** A page family's search page, as the test provides it: it shows its context and picks a customer. */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p class="stub-page">{{ context.entity }} {{ context.kind }} {{ context.filter }}</p>
    <button type="button" class="stub-pick" (click)="pick()">Pick</button>
  `,
})
class TmStubPage {
  readonly #ref = inject<TmModalRef<unknown>>(TmModalRef);

  /** The picker's context. */
  readonly context = (inject(TM_MODAL_DATA) as TmEntityPickerPageData).context as TmEntityPickerContext;

  /** Closes with a pick, as a search page does. */
  pick(): void {
    this.#ref.close({ id: 7, label: '1101 - Cash', item: { id: 7, code: '1101', name: 'Cash' } });
  }
}

/** One customer field with a filter. */
@Component({
  imports: [TmEntityField],
  template: `<tm-entity-field entity="acme.Customer" aria-label="Customer" filter="IsActive = true" [(value)]="customerId" />`,
})
class TmPageHost {
  /** The field's value. */
  readonly customerId = signal<TmId | null>(null);
}

/** A started host. */
interface TmPageTest {
  /** The rig. */
  readonly rig: TmEntityRig;
  /** The fixture. */
  readonly fixture: ComponentFixture<TmPageHost>;
  /** The field. */
  readonly field: TmEntityFieldHarness;
  /** A loader at the document's root, where the modal renders. */
  readonly root: HarnessLoader;
}

/**
 * Starts the host with the page families' halves, none when omitted, and
 * opens the field's advanced search.
 *
 * @param families - Each family's half, provided in order.
 * @returns The test.
 */
async function openSearch(families: readonly ɵTmPickerPages[] = []): Promise<TmPageTest> {
  const providers = families.map((pages) => ({ provide: ɵTM_PICKER_PAGES, multi: true, useValue: pages }));
  const rig = await tmEntityRig({ access: { isSystem: true }, providers });
  rig.api.respond('customers/query', { rows: { columns: [], rows: [] } });
  const fixture = TestBed.createComponent(TmPageHost);
  await fixture.whenStable();
  const field = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmEntityFieldHarness);
  await (await field.getPicker()).clickMagnifier();
  await settle(fixture);
  return { rig, fixture, field, root: TestbedHarnessEnvironment.documentRootLoader(fixture) };
}

/**
 * Waits for the views and the loads they started to settle.
 *
 * @param fixture - The fixture.
 */
async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  await tmSettle();
  await fixture.whenStable();
}

/**
 * The button with a text.
 *
 * @param root - The loader.
 * @param text - The button's text.
 * @returns The button.
 */
async function button(root: HarnessLoader, text: string): Promise<TmButtonHarness> {
  for (const candidate of await root.getAllHarnesses(TmButtonHarness)) {
    if ((await candidate.getText()).trim() === text) {
      return candidate;
    }
  }
  throw new Error(`No button reads '${text}'.`);
}

/**
 * Clicks the open dropdown's footer row with a caption.
 *
 * @param picker - The picker.
 * @param caption - The row's caption.
 */
async function footerRow(picker: TmEntityPickerHarness, caption: string): Promise<void> {
  for (const option of await picker.getOptions()) {
    if ((await option.isAction()) && (await option.getText()) === caption) {
      return option.click();
    }
  }
  throw new Error(`No footer row reads '${caption}'.`);
}

/**
 * A page family's loader that records each load and gives the stub page.
 *
 * @param loads - The loads, by family.
 * @param family - The family.
 * @returns The loader.
 */
function stubFamily(loads: string[], family: string): () => Promise<Type<unknown>> {
  return () => {
    loads.push(family);
    return Promise.resolve(TmStubPage as Type<unknown>);
  };
}

/** The picker-page events recorded so far. */
function pageEvents(rig: TmEntityRig): unknown[] {
  return rig.telemetry
    .recent()
    .filter((item) => item.name === 'tm.entity.picker-page')
    .map((item) => item.props);
}

describe('TmLazyPickerPage', () => {
  it('says the page is not available while no page family provides it, and reports nothing', async () => {
    const { rig, root, fixture } = await openSearch();
    const empty = await root.getHarness(TmEmptyStateHarness);
    expect(await empty.getHeading()).toBe("This page isn't available yet.");

    await (await button(root, 'Close')).click();
    await settle(fixture);
    expect(await root.getAllHarnesses(TmModalHarness)).toEqual([]);
    expect(pageEvents(rig)).toEqual([]);
  });

  it('renders the loaded page with the picker’s context, whose pick the field takes, reported once', async () => {
    const loads: string[] = [];
    const { rig, root, fixture } = await openSearch([{ search: stubFamily(loads, 'search') }]);
    expect(loads).toEqual(['search']);
    expect(await (await root.getHarness(TmModalHarness)).getBodyText()).toContain('acme.Customer search IsActive = true');

    (document.querySelector('.stub-pick') as HTMLButtonElement).click();
    await settle(fixture);
    expect(fixture.componentInstance.customerId()).toBe(7);
    expect(pageEvents(rig)).toEqual([{ page: 'search', outcome: 'picked' }]);
  });

  it('loads the details family’s page for Create and Edit, each family providing its own half', async () => {
    const loads: string[] = [];
    const { rig, root, fixture, field } = await openSearch([{ search: stubFamily(loads, 'search') }, { details: stubFamily(loads, 'details') }]);
    const closeModal = async (): Promise<void> => {
      await (await root.getHarness(TmModalHarness)).close();
      await settle(fixture);
    };
    await closeModal();

    // Create, from the footer row the binding labels.
    const picker = await field.getPicker();
    await picker.open();
    await settle(fixture);
    await footerRow(picker, 'New customer…');
    await settle(fixture);
    expect(await (await root.getHarness(TmModalHarness)).getBodyText()).toContain('acme.Customer create');
    await closeModal();

    // Edit, offered once a value is chosen.
    rig.names.remember('acme.Customer', [{ id: 7, code: '1101', name: 'Cash' }]);
    fixture.componentInstance.customerId.set(7);
    await settle(fixture);
    await picker.open();
    await settle(fixture);
    await footerRow(picker, 'Edit…');
    await settle(fixture);
    expect(await (await root.getHarness(TmModalHarness)).getBodyText()).toContain('acme.Customer edit');
    await closeModal();

    expect(loads).toEqual(['search', 'details', 'details']);
  });

  it('loads the later provider’s half where two provide the same one, the other half kept', async () => {
    const loads: string[] = [];
    const { root } = await openSearch([
      { search: stubFamily(loads, 'first search'), details: stubFamily(loads, 'details') },
      { search: stubFamily(loads, 'second search') },
    ]);
    expect(loads).toEqual(['second search']);
    expect(await (await root.getHarness(TmModalHarness)).getBodyText()).toContain('acme.Customer search');
  });

  it('offers a retry when the page fails to load, and reports a dismissed page', async () => {
    let attempts = 0;
    const { rig, root, fixture } = await openSearch([
      { search: () => (++attempts === 1 ? Promise.reject(new Error('offline')) : Promise.resolve(TmStubPage as Type<unknown>)) },
    ]);
    const failed = await root.getHarness(TmEmptyStateHarness);
    expect([await failed.getHeading(), await failed.getTone()]).toEqual(["This page couldn't be loaded.", 'error']);

    await (await button(root, 'Try again')).click();
    await settle(fixture);
    expect(document.querySelector('.stub-page')).not.toBeNull();

    await (await root.getHarness(TmModalHarness)).close();
    await settle(fixture);
    expect(fixture.componentInstance.customerId()).toBeNull();
    expect(pageEvents(rig)).toEqual([{ page: 'search', outcome: 'dismissed' }]);
  });
});

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
import { TmButtonHarness, TmEmptyStateHarness, TmModalHarness } from '@tellma/core-ui-testing';

import { tmEntityRig, type TmEntityRig } from '../../src/entity/tm-entity.testing.util';
import { tmSettle } from '../../src/routes/tm-app.testing.util';
import { TmEntityField } from './tm-entity-field';
import { ɵTM_PICKER_PAGES, type TmEntityPickerContext, type ɵTmPickerPageLoader } from './tm-lazy-picker-page';

// The page an entity picker opens: not available while no page family
// provides one, a loaded page rendered in the modal with the picker's data,
// a retry after a failed load, and one report per page shown.

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
 * Starts the host with a page loader, the default one when omitted, and
 * opens the field's advanced search.
 *
 * @param loader - The loader.
 * @returns The test.
 */
async function openSearch(loader?: ɵTmPickerPageLoader): Promise<TmPageTest> {
  const rig = await tmEntityRig({ access: { isSystem: true }, ...(loader && { providers: [{ provide: ɵTM_PICKER_PAGES, useValue: loader }] }) });
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
    const pages: string[] = [];
    const { rig, root, fixture } = await openSearch((page) => {
      pages.push(page);
      return Promise.resolve(TmStubPage as Type<unknown>);
    });
    expect(pages).toEqual(['search']);
    expect(await (await root.getHarness(TmModalHarness)).getBodyText()).toContain('acme.Customer search IsActive = true');

    (document.querySelector('.stub-pick') as HTMLButtonElement).click();
    await settle(fixture);
    expect(fixture.componentInstance.customerId()).toBe(7);
    expect(pageEvents(rig)).toEqual([{ page: 'search', outcome: 'picked' }]);
  });

  it('offers a retry when the page fails to load, and reports a dismissed page', async () => {
    let attempts = 0;
    const { rig, root, fixture } = await openSearch(() => (++attempts === 1 ? Promise.reject(new Error('offline')) : Promise.resolve(TmStubPage as Type<unknown>)));
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

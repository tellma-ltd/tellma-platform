// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgComponentOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, Injector, input, type Type } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { type HarnessLoader, TestKey } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import type { TmJsonObject } from '@tellma/core';
import { TmGrid } from '@tellma/core-ui/grid';
import { TmChildGridHarness, type TmFakeDetails, type TmFakeDetailsOptions, tmFakeDetails } from '@tellma/core-testing';
import { TmCardHarness } from '@tellma/core-ui-testing';
import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';

import { tmEntityRig } from '../../src/entity/tm-entity.testing.util';
import { tmSettle, tmSettleUntil } from '../../src/routes/tm-app.testing.util';
import { ɵtmChildGridEditLoader } from './tm-child-grid-view';
import { TmDetailsKit } from './tm-details-kit';
import { TM_DETAILS_RECORD, type TmDetailsRecordState } from './tm-details-record';
import {
  TM_SALES_INVOICE,
  TM_SALES_INVOICE_LABELS,
  TM_SALES_INVOICE_RELATED,
  tmSalesInvoiceChunk,
  tmSalesInvoiceSummaries,
} from './tm-sales-invoice.testing.util';

// The child grid over a sales invoice's lines, in a fake details context:
// the deferred grid behind a placeholder of the same geometry until its
// frame enters the viewport, view-mode
// links, the registration once its rows render and its reveal(), new rows
// from the placeholder row with negative ids and the layout's defaults,
// deleting rows with the grid's commands, the exact live totals, and a
// box that holds across view mode, Edit, typing and Cancel, changing only
// when a record lands.

/** Renders a layout in a details record's injector, as the page's frame does. */
@Component({
  selector: 'tm-test-grid-host',
  imports: [NgComponentOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<ng-container *ngComponentOutlet="layout(); injector: injector()" />`,
})
class TmTestGridHost {
  /** The layout. */
  readonly layout = input.required<Type<unknown>>();

  /** The record's injector. */
  readonly injector = input.required<Injector>();
}

/** The invoice's lines, with a total and a default for new lines. */
@Component({
  selector: 'tm-test-lines-layout',
  imports: [TmDetailsKit],
  template: `
    <tm-details-card id="lines" titleKey="Acme_SalesInvoiceLine_Plural" collapsible>
      <tm-child-grid collection="Lines" [columns]="columns" [totals]="['Amount']" [defaults]="defaults" />
    </tm-details-card>
  `,
})
class TmTestLinesLayout {
  /** The columns. */
  readonly columns = ['Center', 'Description', 'Amount'];

  /** A new line's values. */
  readonly defaults = (): TmJsonObject => ({ description: 'New line' });
}

/** The lines, never editable. */
@Component({
  selector: 'tm-test-fixed-lines-layout',
  imports: [TmDetailsKit],
  template: `<tm-details-card><tm-child-grid collection="Lines" [columns]="['Description', 'Amount']" readonly /></tm-details-card>`,
})
class TmTestFixedLinesLayout {}

/** The lines in a card that starts collapsed. */
@Component({
  selector: 'tm-test-collapsed-lines-layout',
  imports: [TmDetailsKit],
  template: `<tm-details-card id="lines" titleKey="Acme_SalesInvoiceLine_Plural" collapsible collapsed><tm-child-grid collection="Lines" [columns]="['Description', 'Amount']" /></tm-details-card>`,
})
class TmTestCollapsedLinesLayout {}

/** The lines below the fold, under a tall spacer. */
@Component({
  selector: 'tm-test-low-lines-layout',
  imports: [TmDetailsKit],
  template: `
    <div class="tm-test-spacer" style="block-size: 4000px"></div>
    <tm-details-card><tm-child-grid collection="Lines" [columns]="['Description', 'Amount']" /></tm-details-card>
  `,
})
class TmTestLowLinesLayout {}

/** A collection the invoice lacks. */
@Component({
  selector: 'tm-test-unknown-layout',
  imports: [TmDetailsKit],
  template: `<tm-details-card><tm-child-grid collection="Payments" [columns]="['Amount']" /></tm-details-card>`,
})
class TmTestUnknownLayout {}

/** A rendered layout. */
interface TmTestRendered {
  /** The fake context. */
  readonly details: TmFakeDetails<TmJsonObject>;
  /** The record's state behind it. */
  readonly record: TmDetailsRecordState<TmJsonObject>;
  /** The host's fixture. */
  readonly fixture: ComponentFixture<TmTestGridHost>;
  /** The harness loader. */
  readonly loader: HarnessLoader;
}

/** The unit page loads no token stylesheet; the grid's geometry needs one. */
let tokenSheet: HTMLStyleElement;

beforeAll(() => {
  tokenSheet = document.createElement('style');
  tokenSheet.textContent = tmEmitCss(tmTokensDefault);
  document.head.appendChild(tokenSheet);
});

afterAll(() => tokenSheet.remove());

describe("the details page's child grid", () => {
  beforeEach(async () => {
    await tmEntityRig({ access: { isSystem: true }, chunk: tmSalesInvoiceChunk(), summaries: tmSalesInvoiceSummaries(), labels: { en: TM_SALES_INVOICE_LABELS } });
  });

  /**
   * A fake context over the invoice, in the record's injector.
   *
   * @param options - The context's options; the invoice, landed, in view mode when omitted.
   * @returns The context and the injector a layout renders in.
   */
  function context(options: Partial<TmFakeDetailsOptions<TmJsonObject>> = {}): { readonly details: TmFakeDetails<TmJsonObject>; readonly injector: Injector } {
    const details = TestBed.runInInjectionContext(() =>
      tmFakeDetails<TmJsonObject>({ entity: 'acme.SalesInvoice', record: { entity: TM_SALES_INVOICE, related: TM_SALES_INVOICE_RELATED }, ...options }),
    );
    return { details, injector: Injector.create({ providers: details.providers, parent: TestBed.inject(Injector) }) };
  }

  /**
   * Renders a layout over the invoice and waits for its grid.
   *
   * @param layout - The layout.
   * @param options - The context's options.
   * @returns The rendering.
   */
  async function render(layout: Type<unknown>, options: Partial<TmFakeDetailsOptions<TmJsonObject>> = {}): Promise<TmTestRendered> {
    const { details, injector } = context(options);
    const fixture = TestBed.createComponent(TmTestGridHost);
    fixture.componentRef.setInput('layout', layout);
    fixture.componentRef.setInput('injector', injector);
    const rendered = { details, record: injector.get(TM_DETAILS_RECORD) as TmDetailsRecordState<TmJsonObject>, fixture, loader: TestbedHarnessEnvironment.loader(fixture) };
    if (options.record !== undefined || !('record' in options)) {
      await tmSettleUntil(() => fixture.nativeElement.querySelector('tm-child-grid tm-grid [role="row"]') !== null, 'the grid renders its rows');
    }
    await tmSettle();
    return rendered;
  }

  /**
   * The height of one grid row, as the tokens set it at the grid's size.
   *
   * @param rendered - The rendering.
   * @returns The height, in pixels.
   */
  function rowHeight(rendered: TmTestRendered): number {
    const host = rendered.fixture.nativeElement.querySelector('tm-child-grid') as HTMLElement;
    return parseFloat(getComputedStyle(host).getPropertyValue('--grid-row-height'));
  }

  /**
   * The frame's height for a number of body rows: the header, the rows, the scroller's border and the status band.
   *
   * @param rendered - The rendering.
   * @param bodyRows - The body rows the frame shows.
   * @returns The height, in pixels.
   */
  function frameHeight(rendered: TmTestRendered, bodyRows: number): number {
    const host = rendered.fixture.nativeElement.querySelector('tm-child-grid') as HTMLElement;
    return rowHeight(rendered) * (bodyRows + 1) + 2 + parseFloat(getComputedStyle(host).getPropertyValue('--grid-status-height'));
  }

  /**
   * The frame's box on the page, and where the totals under it start.
   *
   * @param rendered - The rendering.
   * @returns The frame's edges and size, and the totals' top edge.
   */
  function frameBox(rendered: TmTestRendered): { readonly top: number; readonly left: number; readonly width: number; readonly height: number; readonly totals: number } {
    const root = rendered.fixture.nativeElement as HTMLElement;
    const { top, left, width, height } = root.querySelector('tm-child-grid .tm-child-grid__frame')!.getBoundingClientRect();
    return { top, left, width, height, totals: root.querySelector('tm-child-grid .tm-child-grid__totals')!.getBoundingClientRect().top };
  }

  /**
   * Enters edit mode and waits for the grid to edit.
   *
   * @param rendered - The rendering.
   */
  async function edit(rendered: TmTestRendered): Promise<void> {
    rendered.details.edit();
    await tmSettleUntil(() => rendered.fixture.nativeElement.querySelector('tm-child-grid .tm-grid__row--placeholder') !== null, 'the placeholder row shows');
    await tmSettle();
  }

  /**
   * The invoice's lines in the working copy.
   *
   * @param rendered - The rendering.
   * @returns The lines.
   */
  function lines(rendered: TmTestRendered): readonly TmJsonObject[] {
    return rendered.details.model()['lines'] as readonly TmJsonObject[];
  }

  it('renders the lines read-only in view mode, a navigation as its link, with the exact total and no placeholder row', async () => {
    const rendered = await render(TmTestLinesLayout);
    const child = await rendered.loader.getHarness(TmChildGridHarness.with({ collection: 'Lines' }));
    const grid = await child.getGrid();

    expect(await child.getState()).toBe('grid');
    expect(await child.isEditable()).toBe(false);
    expect(await grid.getHeaderTexts()).toEqual(['Center', 'Description', 'Amount']);
    expect(await grid.hasPlaceholderRow()).toBe(false);
    expect(await grid.getCellText(1, 1)).toBe('Travel');
    expect(await grid.getCellText(0, 2)).toBe('1,000.10');
    const link = rendered.fixture.nativeElement.querySelector('tm-child-grid [role="gridcell"] a') as HTMLAnchorElement;
    expect(link.textContent).toBe('C-4');
    expect(link.getAttribute('href')).toBe('/17/centers/4');
    expect(await child.getTotals()).toEqual({ 'Total Amount': '1,000.30' });
  });

  it('keeps the frame of a loading record at the geometry the grid takes once it lands', async () => {
    const rendered = await render(TmTestLinesLayout, { record: undefined });
    const child = await rendered.loader.getHarness(TmChildGridHarness);
    const before = await child.getHeight();

    expect(await child.getState()).toBe('placeholder');
    expect(before).toBe(frameHeight(rendered, 4));

    rendered.details.land({ entity: TM_SALES_INVOICE, related: TM_SALES_INVOICE_RELATED });
    await tmSettleUntil(() => rendered.fixture.nativeElement.querySelector('tm-child-grid tm-grid [role="row"]') !== null, 'the grid renders its rows');
    expect(await child.getState()).toBe('grid');
    expect(await child.getHeight()).toBe(before);
  });

  it("loads the grid's chunk only once its frame enters the viewport, and registers the collection then", async () => {
    const rendered = await render(TmTestLowLinesLayout, { record: undefined });
    rendered.details.land({ entity: TM_SALES_INVOICE, related: TM_SALES_INVOICE_RELATED });
    await tmSettle();
    const child = await rendered.loader.getHarness(TmChildGridHarness);
    expect(await child.getState()).toBe('placeholder');
    expect(rendered.record.collections().get('Lines')?.ready() ?? false).toBe(false);

    (rendered.fixture.nativeElement.querySelector('tm-child-grid') as HTMLElement).scrollIntoView();
    await tmSettleUntil(() => rendered.fixture.nativeElement.querySelector('tm-child-grid tm-grid [role="row"]') !== null, 'the grid renders its rows');
    expect(await child.getState()).toBe('grid');
    expect(rendered.record.collections().get('Lines')!.ready()).toBe(true);
  });

  it('keeps the grid of a collapsed card a placeholder until the card expands', async () => {
    const rendered = await render(TmTestCollapsedLinesLayout, { record: undefined });
    rendered.details.land({ entity: TM_SALES_INVOICE, related: TM_SALES_INVOICE_RELATED });
    await tmSettle();
    const child = await rendered.loader.getHarness(TmChildGridHarness);
    const card = await rendered.loader.getHarness(TmCardHarness);
    expect(await card.isExpanded()).toBe(false);
    expect(await child.getState()).toBe('placeholder');
    expect(rendered.record.collections().get('Lines')?.ready() ?? false).toBe(false);

    await card.toggle();
    await tmSettleUntil(() => rendered.fixture.nativeElement.querySelector('tm-child-grid tm-grid [role="row"]') !== null, 'the grid renders its rows');
    expect(await child.getState()).toBe('grid');
  });

  it("keeps the grid's state by entity and collection, and its content by record: new for a record not yet saved", async () => {
    const saved = await render(TmTestLinesLayout);
    const grid = saved.fixture.debugElement.query(By.directive(TmGrid)).componentInstance as TmGrid<unknown>;
    expect(grid.gridId()).toBe('details:acme.SalesInvoice:Lines');
    expect(grid.contentKey()).toBe(7);
    saved.fixture.destroy();

    const created = await render(TmTestLinesLayout, { mode: 'create', record: undefined });
    await tmSettleUntil(() => created.fixture.debugElement.query(By.directive(TmGrid)) !== null, 'the grid renders');
    const fresh = created.fixture.debugElement.query(By.directive(TmGrid)).componentInstance as TmGrid<unknown>;
    expect(fresh.gridId()).toBe('details:acme.SalesInvoice:Lines');
    expect(fresh.contentKey()).toBe('new');
  });

  it("follows a view-mode navigation cell's link with Enter", async () => {
    const rendered = await render(TmTestLinesLayout);
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    try {
      const grid = await (await rendered.loader.getHarness(TmChildGridHarness)).getGrid();
      // The keyboard reaches the Center cell from its neighbour, so no pointer touches the link; Enter then follows it.
      await grid.clickCell(0, 1);
      await grid.pressKeys(TestKey.LEFT_ARROW);
      expect(await grid.getActiveCell()).toEqual({ row: 0, col: 0 });
      expect(navigate).not.toHaveBeenCalled();
      await grid.pressKeys(TestKey.ENTER);
      await tmSettle();

      expect(navigate).toHaveBeenCalledTimes(1);
      expect(router.serializeUrl(navigate.mock.calls[0]![0] as Parameters<Router['serializeUrl']>[0])).toBe('/17/centers/4');
    } finally {
      navigate.mockRestore();
    }
  });

  it('registers the collection once its rows render, with what it displays', async () => {
    const rendered = await render(TmTestLinesLayout);
    const lines = rendered.record.collections().get('Lines')!;

    expect(lines.ready()).toBe(true);
    const visible = lines.visible();
    expect(visible.columns).toEqual([
      { path: 'Center', label: 'Center' },
      { path: 'Description', label: 'Description' },
      { path: 'Amount', label: 'Amount' },
    ]);
    expect(visible.rows).toEqual([
      { id: 21, cells: { Center: 'C-4', 'Center.Id': 4, Description: 'Consulting', Amount: '1000.10' } },
      { id: 22, cells: { Center: null, 'Center.Id': null, Description: 'Travel', Amount: '0.20' } },
    ]);
    expect(visible.total).toBe(2);
    expect(visible.truncated).toBe(false);
  });

  it('reveals a row: it expands the card, scrolls to the row, flashes it and focuses the cell of the column', async () => {
    const rendered = await render(TmTestLinesLayout);
    const child = await rendered.loader.getHarness(TmChildGridHarness);
    const grid = await child.getGrid();
    const card = await rendered.loader.getHarness(TmCardHarness);
    const host = rendered.fixture.nativeElement.querySelector('tm-child-grid') as HTMLElement;
    await card.toggle();
    await tmSettle();
    expect(await card.isExpanded()).toBe(false);

    expect(rendered.record.collections().get('Lines')!.reveal(22, { flash: true, column: 'Amount' })).toBe(true);
    await tmSettle();
    expect(await card.isExpanded()).toBe(true);
    expect(await grid.getActiveCell()).toEqual({ row: 1, col: 2 });
    expect(await grid.isRowFlashing(1)).toBe(true);
    expect(host.contains(document.activeElement)).toBe(true);

    // A row the collection does not hold is not found.
    expect(rendered.record.collections().get('Lines')!.reveal(99)).toBe(false);
  });

  it('adds a line from the placeholder row with a negative id and the defaults; the totals follow and the box holds across Edit and typing', async () => {
    const rendered = await render(TmTestLinesLayout);
    const child = await rendered.loader.getHarness(TmChildGridHarness);
    const viewHeight = await child.getHeight();
    const viewBox = frameBox(rendered);
    await edit(rendered);
    const grid = await child.getGrid();
    const editHeight = await child.getHeight();

    expect(await child.isEditable()).toBe(true);
    expect(await grid.hasPlaceholderRow()).toBe(true);
    // Two lines and the placeholder row fit the four rows the frame shows at least; view mode reserves the band and the row Edit adds.
    expect(viewHeight).toBe(frameHeight(rendered, 4));
    expect(frameBox(rendered)).toEqual(viewBox);

    // --- Typing in the placeholder row adds a line, three times over ---
    for (const [row, amount] of [[2, '5'], [3, '7.25'], [4, '0.05']] as const) {
      await grid.openEditor(row, 2, 'type', amount);
      await grid.commitEditor('enter');
      await tmSettle();
    }
    expect(lines(rendered).map((line) => line['id'])).toEqual([21, 22, -1, -2, -3]);
    expect(lines(rendered)[2]).toMatchObject({ id: -1, description: 'New line', amount: '5', centerId: null });
    expect(lines(rendered)[2]).not.toHaveProperty('salesInvoiceId');
    expect(await child.getTotals()).toEqual({ 'Total Amount': '1,012.60' });
    expect(await child.getHeight()).toBe(editHeight);
    expect(frameBox(rendered)).toEqual(viewBox);
    expect(rendered.details.dirty()).toBe(true);

    // --- The height is set again when the saved record lands: five lines and the reserved placeholder row ---
    rendered.details.save();
    await tmSettle();
    expect(await child.getHeight()).toBe(frameHeight(rendered, 6));
  });

  it('keeps the box of view mode after lines added in Edit are cancelled', async () => {
    const rendered = await render(TmTestLinesLayout);
    const child = await rendered.loader.getHarness(TmChildGridHarness);
    const viewBox = frameBox(rendered);
    await edit(rendered);
    const grid = await child.getGrid();
    await grid.openEditor(2, 2, 'type', '5');
    await grid.commitEditor('enter');
    await tmSettle();
    expect(lines(rendered)).toHaveLength(3);

    await rendered.details.cancel();
    await tmSettle();
    expect(await child.isEditable()).toBe(false);
    expect(lines(rendered)).toHaveLength(2);
    expect(frameBox(rendered)).toEqual(viewBox);
  });

  it("deletes lines with the grid's commands, and the totals follow", async () => {
    const rendered = await render(TmTestLinesLayout);
    await edit(rendered);
    const grid = await (await rendered.loader.getHarness(TmChildGridHarness)).getGrid();

    const menu = await grid.openContextMenu(0, 1);
    await menu.clickItem('Delete 1 row');
    await tmSettle();

    expect(lines(rendered).map((line) => line['id'])).toEqual([22]);
    expect(await (await rendered.loader.getHarness(TmChildGridHarness)).getTotals()).toEqual({ 'Total Amount': '0.20' });
  });

  it("reports a line's error to its card once the line is touched, in edit mode only", async () => {
    const rendered = await render(TmTestLinesLayout);
    await edit(rendered);
    const card = await rendered.loader.getHarness(TmCardHarness);
    const form = rendered.details.form as unknown as Readonly<Record<string, Readonly<Record<number, Readonly<Record<string, () => { readonly value: { set(value: unknown): void }; markAsTouched(): void }>>>>>>;

    expect(await card.isInvalid()).toBe(false);
    const amount = form['lines']![0]!['amount']!();
    amount.value.set(null);
    amount.markAsTouched();
    await tmSettle();
    expect(await card.isInvalid()).toBe(true);
    expect(await (await (await rendered.loader.getHarness(TmChildGridHarness)).getGrid()).getErrorCount()).toBe(1);

    await rendered.details.cancel();
    await tmSettle();
    expect(await card.isInvalid()).toBe(false);
  });

  it("holds the card's error only for an error the grid shows: a new line's untouched required cell stays quiet", async () => {
    const rendered = await render(TmTestLinesLayout);
    await edit(rendered);
    const card = await rendered.loader.getHarness(TmCardHarness);
    const grid = await (await rendered.loader.getHarness(TmChildGridHarness)).getGrid();

    // --- A new line typed in its Description: its Amount is required, empty and untouched ---
    await grid.openEditor(2, 1, 'type', 'Freight');
    await grid.commitEditor('enter');
    await tmSettle();
    expect(lines(rendered)[2]).toMatchObject({ id: -1, description: 'Freight', amount: null });
    expect(await grid.getErrorCount()).toBe(0);
    expect(await card.isInvalid()).toBe(false);

    // --- Once the Amount is touched, the grid shows its error, and so does the card ---
    const form = rendered.details.form as unknown as Readonly<Record<string, Readonly<Record<number, Readonly<Record<string, () => { markAsTouched(): void }>>>>>>;
    form['lines']![2]!['amount']!().markAsTouched();
    await tmSettle();
    expect(await grid.getErrorCount()).toBe(1);
    expect(await card.isInvalid()).toBe(true);
  });

  it('says so over the status band when its edit columns fail to load, and loads them on Try again', async () => {
    const load = ɵtmChildGridEditLoader.load;
    try {
      ɵtmChildGridEditLoader.load = () => Promise.reject(new Error('The chunk failed to load.'));
      const rendered = await render(TmTestLinesLayout);
      const root = rendered.fixture.nativeElement as HTMLElement;
      // The view-mode prefetch fails quietly.
      expect(root.querySelector('.tm-child-grid-view__failed')).toBeNull();

      rendered.details.edit();
      await tmSettleUntil(() => root.querySelector('.tm-child-grid-view__failed') !== null, 'the failure shows');
      const failure = root.querySelector('.tm-child-grid-view__failed')!;
      expect(failure.querySelector('[role="alert"]')?.textContent?.trim()).toBe("This list couldn't be loaded for editing.");
      // No row can be added through read-only columns, so the placeholder row waits for them.
      expect(root.querySelector('tm-child-grid .tm-grid__row--placeholder')).toBeNull();

      ɵtmChildGridEditLoader.load = load;
      failure.querySelector('button')!.click();
      await tmSettleUntil(() => root.querySelector('tm-child-grid .tm-grid__row--placeholder') !== null, 'the placeholder row shows');
      expect(root.querySelector('.tm-child-grid-view__failed')).toBeNull();
    } finally {
      ɵtmChildGridEditLoader.load = load;
    }
  });

  it('stays read-only in edit mode when the layout says so', async () => {
    const rendered = await render(TmTestFixedLinesLayout);
    rendered.details.edit();
    await tmSettle();
    const child = await rendered.loader.getHarness(TmChildGridHarness);

    expect(await child.isEditable()).toBe(false);
    expect(await (await child.getGrid()).hasPlaceholderRow()).toBe(false);
  });

  it('names a collection the entity lacks in development', () => {
    const { injector } = context();
    const fixture = TestBed.createComponent(TmTestGridHost);
    fixture.componentRef.setInput('layout', TmTestUnknownLayout);
    fixture.componentRef.setInput('injector', injector);
    expect(() => fixture.detectChanges()).toThrow(/collection 'Payments' names no child collection of acme\.SalesInvoice/);
  });
});

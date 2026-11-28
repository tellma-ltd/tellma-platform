// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgComponentOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, createComponent, EnvironmentInjector, Injector, input, type Type } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { type HarnessLoader, manualChangeDetection } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { TmFilePicker } from '@tellma/core-ui/files';
import { TmFilePreview } from '@tellma/core-ui/file-preview';
import { TmApiError, type TmBlobDescriptor, type TmJsonObject } from '@tellma/core';
import { type TmFakeApiCall, type TmFakeDetails, type TmFakeDetailsOptions, tmFakeDetails } from '@tellma/core-testing';
import { TmCardHarness, TmChipHarness } from '@tellma/core-ui-testing';

import { tmEntityRig, type TmEntityRig } from '../../src/entity/tm-entity.testing.util';
import { tmSettle, tmSettleUntil } from '../../src/routes/tm-app.testing.util';
import { ɵTmChildFilesBody, ɵtmChildFilesEditorLoader } from './tm-child-files-body';
import { TmDetailsKit } from './tm-details-kit';
import { TM_DETAILS_RECORD, type TmDetailsRecordState } from './tm-details-record';
import { type TmDetailsSaveHost, ɵtmDetailsSave } from './tm-details-save';
import type { TmDetailsSummaryState } from './tm-details-summary';
import {
  TM_SALES_INVOICE,
  TM_SALES_INVOICE_LABELS,
  TM_SALES_INVOICE_RELATED,
  tmSalesInvoiceChunk,
  tmSalesInvoiceSummaries,
} from './tm-sales-invoice.testing.util';

// The child files over a sales invoice's attachments, in a fake details
// context, once their deferred body renders: chips that download or preview in view mode; in edit mode, the
// drop zone whose files add their rows at once and upload under each row's
// file path with a progress ring, the policy's refusals, a refused and a
// failed upload on their row with a retry for the latter, removal that
// aborts an upload and moves the later ones up, and inline captions. While
// the record loads the placeholder stays, and the rows' errors reach the
// card; a failed save's summary item for a row focuses that row's caption.

/** Renders a layout in a details record's injector, as the page's frame does. */
@Component({
  selector: 'tm-test-files-host',
  imports: [NgComponentOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<ng-container *ngComponentOutlet="layout(); injector: injector()" />`,
})
class TmTestFilesHost {
  /** The layout. */
  readonly layout = input.required<Type<unknown>>();

  /** The record's injector. */
  readonly injector = input.required<Injector>();
}

/** The invoice's attachments, with their captions. */
@Component({
  selector: 'tm-test-attachments-layout',
  imports: [TmDetailsKit],
  template: `
    <tm-details-card titleKey="Acme_SalesInvoiceAttachment_Plural">
      <tm-child-files collection="Attachments" file="File" caption="Caption" />
    </tm-details-card>
  `,
})
class TmTestAttachmentsLayout {}

/** A rendered layout. */
interface TmTestRendered {
  /** The fake context. */
  readonly details: TmFakeDetails<TmJsonObject>;
  /** The record's state behind it. */
  readonly record: TmDetailsRecordState<TmJsonObject>;
  /** The host's fixture. */
  readonly fixture: ComponentFixture<TmTestFilesHost>;
  /** The harness loader. */
  readonly loader: HarnessLoader;
}

/** An upload the test answers when it chooses. */
interface TmHeldUpload {
  /** The upload's call. */
  readonly call: TmFakeApiCall;
  /** Answers it with a staged blob. */
  resolve(blob: TmBlobDescriptor): void;
  /** Fails it. */
  reject(error: unknown): void;
}

/**
 * A staged blob of the attachments' kind.
 *
 * @param id - Its id.
 * @param fileName - Its file's name.
 * @returns The blob.
 */
function blob(id: number, fileName: string): TmBlobDescriptor {
  return { id, kind: 'invoice-attachment', fileName, contentType: 'application/pdf', size: '4096', variants: '' } as unknown as TmBlobDescriptor;
}

/**
 * A PDF to drop.
 *
 * @param name - Its name.
 * @returns The file.
 */
function pdf(name: string): File {
  return new File(['%PDF-1.7'], name, { type: 'application/pdf' });
}

describe("the details page's child files", () => {
  let rig: TmEntityRig;
  /** The uploads waiting for an answer, in the order they started. */
  let held: TmHeldUpload[];

  beforeEach(async () => {
    rig = await tmEntityRig({ access: { isSystem: true }, chunk: tmSalesInvoiceChunk(), summaries: tmSalesInvoiceSummaries(), labels: { en: TM_SALES_INVOICE_LABELS } });
    held = [];
    rig.api.respond('blobs/invoice-attachment', (call: TmFakeApiCall) => new Promise<TmBlobDescriptor>((resolve, reject) => held.push({ call, resolve, reject })));
  });

  /**
   * Renders a layout over the invoice, landed, in view mode, and waits for
   * the body unless the record is still loading.
   *
   * @param layout - The layout.
   * @param options - The context's options.
   * @returns The rendering.
   */
  async function render(layout: Type<unknown> = TmTestAttachmentsLayout, options: Partial<TmFakeDetailsOptions<TmJsonObject>> = {}): Promise<TmTestRendered> {
    const details = TestBed.runInInjectionContext(() =>
      tmFakeDetails<TmJsonObject>({ entity: 'acme.SalesInvoice', record: { entity: TM_SALES_INVOICE, related: TM_SALES_INVOICE_RELATED }, ...options }),
    );
    const injector = Injector.create({ providers: details.providers, parent: TestBed.inject(Injector) });
    const fixture = TestBed.createComponent(TmTestFilesHost);
    fixture.componentRef.setInput('layout', layout);
    fixture.componentRef.setInput('injector', injector);
    if (options.record !== undefined || !('record' in options)) {
      await tmSettleUntil(() => (fixture.nativeElement as HTMLElement).querySelector('tm-child-files-body') !== null, 'the deferred body renders');
    }
    await tmSettle();
    return { details, record: injector.get(TM_DETAILS_RECORD) as TmDetailsRecordState<TmJsonObject>, fixture, loader: TestbedHarnessEnvironment.loader(fixture) };
  }

  /**
   * Enters edit mode and waits for the edit side to load.
   *
   * @param rendered - The rendering.
   */
  async function edit(rendered: TmTestRendered): Promise<void> {
    rendered.details.edit();
    await tmSettleUntil(() => element(rendered).querySelector('tm-child-files-editor tm-dropzone') !== null, 'the drop zone shows');
    await tmSettle();
  }

  /**
   * The host's element.
   *
   * @param rendered - The rendering.
   * @returns The element.
   */
  function element(rendered: TmTestRendered): HTMLElement {
    return rendered.fixture.nativeElement as HTMLElement;
  }

  /**
   * Drops files on the drop zone, as it reports them.
   *
   * @param rendered - The rendering.
   * @param accepted - The files it takes.
   */
  async function drop(rendered: TmTestRendered, ...accepted: File[]): Promise<void> {
    rendered.fixture.debugElement.query(By.directive(TmFilePicker)).injector.get(TmFilePicker).filesSelected.emit({ accepted, rejected: [] });
    await tmSettle();
  }

  /**
   * The invoice's attachments in the working copy.
   *
   * @param rendered - The rendering.
   * @returns The attachments.
   */
  function attachments(rendered: TmTestRendered): readonly TmJsonObject[] {
    return rendered.details.model()['attachments'] as readonly TmJsonObject[];
  }

  /**
   * The edit side's rows, as their text shows them.
   *
   * @param rendered - The rendering.
   * @returns Each row's text.
   */
  function rows(rendered: TmTestRendered): readonly string[] {
    return [...element(rendered).querySelectorAll('.tm-child-files-editor__item')].map((row) => (row.textContent ?? '').replace(/\s+/g, ' ').trim());
  }

  /**
   * A row's button of a label.
   *
   * @param rendered - The rendering.
   * @param index - The row's index.
   * @param label - The button's text.
   * @returns The button, if any.
   */
  function rowButton(rendered: TmTestRendered, index: number, label: string): HTMLButtonElement | undefined {
    const row = element(rendered).querySelectorAll('.tm-child-files-editor__item')[index];
    return [...(row?.querySelectorAll<HTMLButtonElement>('button') ?? [])].find((button) => button.textContent?.trim() === label);
  }

  it('shows each attachment as a chip that downloads its file, with its size and its caption, in view mode', async () => {
    const rendered = await render();
    const chips = await rendered.loader.getAllHarnesses(TmChipHarness);

    expect(chips).toHaveLength(1);
    expect(await chips[0]!.getLabel()).toBe('invoice.pdf (2 kB)');
    expect(await chips[0]!.getHref()).toBe('/17/blobs/invoice-attachment/501?download=true');
    expect(await chips[0]!.isRemovable()).toBe(false);
    expect(element(rendered).querySelector('.tm-child-files__caption')?.textContent?.trim()).toBe('Signed copy');
    expect(element(rendered).querySelector('tm-child-files-editor')).toBeNull();
  });

  it("previews a file on a plain click, reading the blob through the API, and leaves a modified click to the browser", async () => {
    const open = vi.spyOn(TmFilePreview.prototype, 'open').mockImplementation(() => undefined as unknown as ReturnType<TmFilePreview['open']>);
    try {
      const rendered = await render();
      const link = element(rendered).querySelector<HTMLAnchorElement>('tm-child-files a')!;

      // A modified click keeps the browser's default: a new tab, a download.
      const modified = new MouseEvent('click', { bubbles: true, cancelable: true, ctrlKey: true });
      link.dispatchEvent(modified);
      await tmSettle();
      expect(open).not.toHaveBeenCalled();
      modified.preventDefault();

      const plain = new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 });
      link.dispatchEvent(plain);
      expect(plain.defaultPrevented).toBe(true);
      await tmSettleUntil(() => open.mock.calls.length === 1, 'the preview opens');
      const [file] = open.mock.calls[0]!;
      expect(file).toMatchObject({ name: 'invoice.pdf', type: 'application/pdf', size: 2048 });

      const content = new Blob(['%PDF']);
      rig.api.respond('/17/blobs/invoice-attachment/501', { status: 200, blob: content, etag: null });
      expect(await (file.source as (signal: AbortSignal) => Promise<Blob>)(new AbortController().signal)).toBe(content);
    } finally {
      open.mockRestore();
    }
  });

  it('keeps the placeholder while the record loads, never an empty list, and shows the chips once it lands', async () => {
    const rendered = await render(TmTestAttachmentsLayout, { record: undefined });
    expect(element(rendered).querySelector('.tm-child-files__placeholder')).not.toBeNull();
    expect(element(rendered).querySelector('tm-child-files-body')).toBeNull();
    expect(element(rendered).textContent).not.toContain('No files');

    rendered.details.land({ entity: TM_SALES_INVOICE, related: TM_SALES_INVOICE_RELATED });
    await tmSettleUntil(() => element(rendered).querySelector('tm-child-files-body') !== null, 'the deferred body renders');
    await tmSettle();
    expect(element(rendered).querySelector('.tm-child-files__placeholder')).toBeNull();
    const chips = await rendered.loader.getAllHarnesses(TmChipHarness);
    expect(chips).toHaveLength(1);
    expect(await chips[0]!.getLabel()).toBe('invoice.pdf (2 kB)');
  });

  it("reports a row's file error to its card once the row is touched, in edit mode only", async () => {
    const rendered = await render();
    const card = await rendered.loader.getHarness(TmCardHarness);
    const form = rendered.record.form as unknown as Readonly<Record<string, Readonly<Record<number, Readonly<Record<string, () => { markAsTouched(): void }>>>>>>;
    rendered.record.errors.set([{ segments: ['attachments', 0, 'fileId'], value: 501, error: { kind: 'server', message: 'This file expired. Choose it again.' } }]);
    await tmSettle();
    expect(await card.isInvalid()).toBe(false);

    await edit(rendered);
    form['attachments']![0]!['fileId']!().markAsTouched();
    await tmSettle();
    expect(await card.isInvalid()).toBe(true);

    await rendered.details.cancel();
    await tmSettle();
    expect(await card.isInvalid()).toBe(false);
  });

  it('says so when the record has no attachments', async () => {
    const rendered = await render(TmTestAttachmentsLayout, { record: { entity: { ...TM_SALES_INVOICE, attachments: [] }, related: {} } });
    expect(element(rendered).querySelector('.tm-child-files__none')?.textContent?.trim()).toBe('No files');
  });

  it('adds a row for each dropped file at once and uploads it with a progress ring; Save waits until the blob is staged', async () => {
    const rendered = await render();
    await edit(rendered);

    await drop(rendered, pdf('a.pdf'), pdf('b.pdf'));
    expect(attachments(rendered).map((row) => [row['id'], row['fileId']])).toEqual([
      [31, 501],
      [-1, null],
      [-2, null],
    ]);
    expect(rendered.details.dirty()).toBe(true);
    expect(held.map((upload) => (upload.call.body as File).name)).toEqual(['a.pdf', 'b.pdf']);
    expect([...rendered.record.uploads.running().keys()]).toEqual(['attachments.1.fileId', 'attachments.2.fileId']);

    // --- Each new row shows its file and a ring that follows its upload ---
    const rings = element(rendered).querySelectorAll('tm-child-files-editor tm-progress-ring');
    expect([...rings].map((ring) => ring.getAttribute('aria-label'))).toEqual(['Uploading a.pdf', 'Uploading b.pdf']);
    (held[0]!.call.options as { onProgress(sent: number): void }).onProgress(0.4);
    await tmSettle();
    expect(element(rendered).querySelector('tm-child-files-editor tm-progress-ring')?.getAttribute('aria-valuenow')).toBe('40');

    // --- A staged blob becomes the row's file, and the chip names it ---
    held[0]!.resolve(blob(601, 'a.pdf'));
    await tmSettle();
    expect(attachments(rendered)[1]).toMatchObject({ id: -1, fileId: 601 });
    expect(rendered.record.uploads.running().size).toBe(1);
    // The other upload still runs, so the harness must not wait for the page to settle.
    const staged = await manualChangeDetection(async () => (await rendered.loader.getHarness(TmChipHarness.with({ label: 'a.pdf (4 kB)' }))).getHref());
    expect(staged).toBe('/17/blobs/invoice-attachment/601?download=true');

    held[1]!.resolve(blob(602, 'b.pdf'));
    await tmSettle();
    expect(attachments(rendered)[2]).toMatchObject({ id: -2, fileId: 602 });
    expect(rendered.record.uploads.running().size).toBe(0);
    expect(element(rendered).querySelector('tm-child-files-editor tm-progress-ring')).toBeNull();
  });

  it("refuses a file its kind does not take, naming it and why, and adds no row for it", async () => {
    const rendered = await render();
    await edit(rendered);

    await drop(rendered, new File(['PK'], 'archive.zip', { type: 'application/zip' }), pdf('a.pdf'));
    expect(element(rendered).querySelector('.tm-child-files-editor__refusals')?.textContent?.trim()).toBe("archive.zip: This type of file isn't accepted.");
    expect(attachments(rendered).map((row) => row['id'])).toEqual([31, -1]);
    expect(held).toHaveLength(1);
  });

  it("drops a refused upload's row, so no row without a file is saved, and gives the server's reason among the refusals", async () => {
    const rendered = await render();
    await edit(rendered);
    await drop(rendered, pdf('a.pdf'));

    held[0]!.reject(
      new TmApiError({
        kind: 'validation',
        status: 400,
        code: 'blob-rejected',
        problem: { code: 'blob-rejected', status: 400, instance: 'trace-1', title: 'The file was refused', errors: [{ path: '', code: 'Acme.Unsigned', message: 'The PDF must be signed.' }] },
      }),
    );
    await tmSettle();
    expect(element(rendered).querySelector('.tm-child-files-editor__refusals')?.textContent?.trim()).toBe('a.pdf: The PDF must be signed.');
    expect(attachments(rendered).map((row) => row['id'])).toEqual([31]);
    expect(rows(rendered)).toHaveLength(1);
    expect(rendered.record.uploads.running().size).toBe(0);
    expect(rendered.record.uploads.failed().size).toBe(0);
  });

  it('offers a retry after an upload fails on the server, and stages the file on the retry', async () => {
    const rendered = await render();
    await edit(rendered);
    await drop(rendered, pdf('a.pdf'));

    held[0]!.reject(new TmApiError({ kind: 'server', status: 500 }));
    await tmSettle();
    expect(rows(rendered)[1]).toContain('Upload failed');
    // The row stays, listed failed, which holds Save until it is retried or removed.
    expect(attachments(rendered)[1]).toMatchObject({ id: -1, fileId: null });
    expect([...rendered.record.uploads.failed()]).toEqual(['attachments.1.fileId']);

    rowButton(rendered, 1, 'Try again')!.click();
    await tmSettle();
    expect(held).toHaveLength(2);
    expect(rows(rendered)[1]).not.toContain('Upload failed');
    expect(rendered.record.uploads.failed().size).toBe(0);
    held[1]!.resolve(blob(603, 'a.pdf'));
    await tmSettle();
    expect(attachments(rendered)[1]).toMatchObject({ id: -1, fileId: 603 });
    expect(rowButton(rendered, 1, 'Try again')).toBeUndefined();
  });

  it('clears a failed row from the failures once it is removed', async () => {
    const rendered = await render();
    await edit(rendered);
    await drop(rendered, pdf('a.pdf'));
    held[0]!.reject(new TmApiError({ kind: 'unavailable', status: 429, code: 'rate-limited', problem: { code: 'rate-limited', status: 429, instance: 'trace-2', title: 'Too many requests' } }));
    await tmSettle();
    // A busy server is a failure a retry mends, not a refusal.
    expect(rowButton(rendered, 1, 'Try again')).toBeDefined();
    expect(rendered.record.uploads.failed().size).toBe(1);

    await manualChangeDetection(async () => (await rendered.loader.getAllHarnesses(TmChipHarness))[1]!.remove());
    await tmSettle();
    expect(attachments(rendered).map((row) => row['id'])).toEqual([31]);
    expect(rendered.record.uploads.failed().size).toBe(0);
  });

  it('says so in edit mode when its edit side fails to load, and loads it on Try again', async () => {
    const load = ɵtmChildFilesEditorLoader.load;
    try {
      ɵtmChildFilesEditorLoader.load = () => Promise.reject(new Error('The chunk failed to load.'));
      const rendered = await render();
      // The view-mode prefetch fails quietly.
      expect(element(rendered).querySelector('.tm-child-files__failed-edit')).toBeNull();

      rendered.details.edit();
      await tmSettleUntil(() => element(rendered).querySelector('.tm-child-files__failed-edit') !== null, 'the failure shows');
      const failure = element(rendered).querySelector('.tm-child-files__failed-edit')!;
      expect(failure.querySelector('[role="alert"]')?.textContent?.trim()).toBe("This list couldn't be loaded for editing.");
      expect(element(rendered).querySelector('tm-child-files-editor')).toBeNull();

      ɵtmChildFilesEditorLoader.load = load;
      failure.querySelector('button')!.click();
      await tmSettleUntil(() => element(rendered).querySelector('tm-child-files-editor tm-dropzone') !== null, 'the drop zone shows');
      expect(element(rendered).querySelector('.tm-child-files__failed-edit')).toBeNull();
    } finally {
      ɵtmChildFilesEditorLoader.load = load;
    }
  });

  it("removes a row, aborting its upload, and the later rows' uploads move up with them", async () => {
    const rendered = await render();
    await edit(rendered);
    await drop(rendered, pdf('a.pdf'), pdf('b.pdf'));
    const signal = (held[0]!.call.options as { signal: AbortSignal }).signal;

    // The uploads still run, so the harness must not wait for the page to settle.
    await manualChangeDetection(async () => (await rendered.loader.getAllHarnesses(TmChipHarness))[1]!.remove());
    await tmSettle();
    expect(signal.aborted).toBe(true);
    expect(attachments(rendered).map((row) => row['id'])).toEqual([31, -2]);
    expect([...rendered.record.uploads.running().keys()]).toEqual(['attachments.1.fileId']);

    held[1]!.resolve(blob(604, 'b.pdf'));
    await tmSettle();
    expect(attachments(rendered)[1]).toMatchObject({ id: -2, fileId: 604 });
    expect(rows(rendered)).toHaveLength(2);
  });

  it("edits each row's caption inline", async () => {
    const rendered = await render();
    await edit(rendered);
    const caption = element(rendered).querySelector<HTMLInputElement>('.tm-child-files-editor__caption')!;

    expect(caption.value).toBe('Signed copy');
    expect(caption.getAttribute('aria-label')).toBe('Caption: invoice.pdf');
    expect(caption.getAttribute('maxlength')).toBe('100');
    caption.value = 'Countersigned';
    caption.dispatchEvent(new Event('input'));
    await tmSettle();
    expect(attachments(rendered)[0]).toMatchObject({ caption: 'Countersigned' });
  });

  it("focuses a row's caption from a failed save's summary item, waiting for no grid", async () => {
    const rendered = await render();
    await edit(rendered);
    const { record } = rendered;
    // The first row's file did not upload, which holds the Save before any check.
    await record.uploads.start('attachments.0.fileId', () => Promise.reject(new TmApiError({ kind: 'server', status: 500 }))).catch(() => undefined);
    await tmSettle();

    // --- The save, through a host that keeps the summary it opens ---
    const shown: { summary?: TmDetailsSummaryState } = {};
    const host: TmDetailsSaveHost<TmJsonObject> = {
      injector: TestBed.inject(Injector),
      routed: false,
      cloning: false,
      summary: { set: (view) => (shown.summary = view?.inputs['summary'] as TmDetailsSummaryState | undefined) },
      destroyed: () => false,
      current: () => record,
      announce: () => undefined,
      focusEdit: () => undefined,
      clearDraft: () => undefined,
      remember: () => undefined,
      present: () => undefined,
      navigate: () => Promise.resolve(true),
      leaveClean: () => undefined,
      save: () => Promise.resolve(false),
    };
    expect(await ɵtmDetailsSave(host, record)).toBe(false);

    // --- The row's item: activating it focuses that row's caption ---
    const [item] = shown.summary!.items;
    expect(item!.text).toMatch(/· row 1 · .*: The file didn't upload\. Try it again or remove it\.$/);
    item!.activate!();
    const caption = element(rendered).querySelector('[data-tm-row="0"] .tm-child-files-editor__caption');
    await tmSettleUntil(() => document.activeElement === caption, "the row's caption takes focus");
  });

  it("names a file member that is not a blob in development, at its body's first render", () => {
    const details = TestBed.runInInjectionContext(() => tmFakeDetails<TmJsonObject>({ entity: 'acme.SalesInvoice', record: { entity: TM_SALES_INVOICE, related: TM_SALES_INVOICE_RELATED } }));
    const elementInjector = Injector.create({ providers: details.providers, parent: TestBed.inject(Injector) });

    // The body renders in its deferred chunk; rendered on its own, its first check throws.
    const body = createComponent(ɵTmChildFilesBody, { environmentInjector: TestBed.inject(EnvironmentInjector), elementInjector });
    try {
      body.setInput('collection', 'Attachments');
      body.setInput('file', 'Caption');
      expect(() => body.changeDetectorRef.detectChanges()).toThrow(/file 'Caption' names no file navigation of acme\.SalesInvoiceAttachment/);
    } finally {
      body.destroy();
    }
  });
});

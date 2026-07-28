// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmImageHarness } from '@tellma/core-ui-testing';

import { TM_BLOB_FETCHER, type TmBlobFetchResult } from './tm-blob-fetcher';
import { ɵTM_CACHE_STORAGE } from './internal/blob-cache';
import { TmImage, TmImagePlaceholder } from './tm-image';
import type { TmImageEdit } from './tm-image-types';

/** Encodes a small deterministic PNG. */
async function pngBlob(width: number, height: number): Promise<Blob> {
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext('2d')!;
  context.fillStyle = '#4ca0b6';
  context.fillRect(0, 0, width, height);
  return new Promise((resolve) => canvas.toBlob((blob) => resolve(blob!), 'image/png'));
}

@Component({
  imports: [TmImage],
  template: `
    <tm-image
      [src]="src()"
      [etag]="etag()"
      alt="Agent avatar"
      [width]="50"
      [height]="50"
      [defer]="false"
      [mode]="mode()"
      [editSrc]="editSrc()"
      [maxFileBytes]="maxFileBytes()"
      (imageChange)="edits.push($event)"
    />
  `,
})
class Host {
  readonly src = signal('/img/agent-7');
  readonly etag = signal<string | null>('v1');
  readonly mode = signal<'view' | 'edit'>('view');
  readonly editSrc = signal<string | undefined>(undefined);
  readonly maxFileBytes = signal(20 * 1024 * 1024);
  readonly edits: (TmImageEdit | null)[] = [];
}

@Component({
  imports: [TmImage, TmImagePlaceholder],
  template: `
    <tm-image src="" alt="" [width]="50" [height]="50" [defer]="false">
      <ng-template tmImagePlaceholder><span class="initials">LH</span></ng-template>
    </tm-image>
  `,
})
class PlaceholderHost {}

interface FetcherControl {
  readonly urls: string[];
  fail: boolean;
}

function setupFetcher(): FetcherControl {
  const control: FetcherControl = { urls: [], fail: false };
  TestBed.configureTestingModule({
    providers: [
      provideTellmaUi(),
      { provide: ɵTM_CACHE_STORAGE, useValue: null }, // cache logic has its own spec
      {
        provide: TM_BLOB_FETCHER,
        useValue: async (url: string): Promise<TmBlobFetchResult> => {
          control.urls.push(url);
          if (control.fail) {
            throw new Error('network down');
          }
          return { status: 200, blob: await pngBlob(100, 100), etag: null };
        },
      },
    ],
  });
  return control;
}

/**
 * Polls until the predicate holds (the pipeline decodes asynchronously).
 * The budget covers a cold CI runner's first IntersectionObserver tick,
 * decode, and CacheStorage write; a passing run never waits it out.
 */
async function until(
  fixture: ComponentFixture<unknown>,
  predicate: () => boolean,
  timeoutMs = 15000,
): Promise<void> {
  const start = Date.now();
  for (;;) {
    await fixture.whenStable();
    if (predicate()) {
      return;
    }
    if (Date.now() - start > timeoutMs) {
      throw new Error('condition not met in time');
    }
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
}

function query<T extends Element>(fixture: ComponentFixture<unknown>, selector: string): T | null {
  return (fixture.nativeElement as HTMLElement).querySelector<T>(selector);
}

/** The `role="status"` region's current text ('' = nothing announced). */
function noticeText(fixture: ComponentFixture<unknown>): string {
  return (query(fixture, '.tm-image__notice')?.textContent ?? '').trim();
}

/** Feeds one file to the hidden picker input without awaiting an outcome. */
function dispatchPick(fixture: ComponentFixture<unknown>, file: File): void {
  const transfer = new DataTransfer();
  transfer.items.add(file);
  const input = query<HTMLInputElement>(fixture, '.tm-image__file-input')!;
  input.files = transfer.files;
  input.dispatchEvent(new Event('change'));
}

async function pickFile(fixture: ComponentFixture<unknown>, blob: Blob): Promise<void> {
  dispatchPick(fixture, new File([blob], 'pick.png', { type: blob.type }));
  await until(fixture, () => query(fixture, '.tm-image__crop') !== null);
}

describe('tm-image', () => {
  it('loads the bucketed rendition and renders it with the alt text', async () => {
    const control = setupFetcher();
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    await until(fixture, () => query(fixture, '.tm-image__img') !== null);

    // 50px box at dpr 1 → the 64 bucket via the default srcForSize.
    expect(control.urls).toEqual(['/img/agent-7?size=64']);
    const img = query<HTMLImageElement>(fixture, '.tm-image__img')!;
    expect(img.getAttribute('alt')).toBe('Agent avatar');
    expect(img.src.startsWith('blob:')).toBe(true);
  });

  it('an empty src shows the placeholder (custom template included) with no fetch', async () => {
    const control = setupFetcher();
    const fixture = TestBed.createComponent(PlaceholderHost);
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(control.urls).toEqual([]);
    expect(query(fixture, '.initials')?.textContent).toBe('LH');
  });

  it('a failing fetch renders the labelled error glyph in the same box', async () => {
    const control = setupFetcher();
    control.fail = true;
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    await until(fixture, () => query(fixture, '.tm-image__glyph--error') !== null);
    const glyph = query<HTMLElement>(fixture, '.tm-image__glyph--error')!;
    expect(glyph.getAttribute('aria-label')).toBe('The image could not be loaded');
    const box = query<HTMLElement>(fixture, 'tm-image')!;
    expect(box.getBoundingClientRect().width).toBe(50); // fixed box, no CLS
  });

  it('an etag change refetches; the same etag does not', async () => {
    const control = setupFetcher();
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    // Wait for the full first load: flipping the etag while the first
    // fetch is still in flight would (correctly) coalesce onto it.
    await until(fixture, () => query(fixture, '.tm-image__img') !== null);
    expect(control.urls).toHaveLength(1);

    fixture.componentInstance.etag.set('v2');
    await until(fixture, () => control.urls.length === 2);
    expect(control.urls[1]).toBe('/img/agent-7?size=64');
  });

  it('edit mode: picking a file enters fitting and emits the file with the cover fit', async () => {
    setupFetcher();
    const fixture = TestBed.createComponent(Host);
    const host = fixture.componentInstance;
    host.mode.set('edit');
    await fixture.whenStable();
    await until(fixture, () => query(fixture, '[data-tm-image-action="replace"]') !== null);

    await pickFile(fixture, await pngBlob(100, 100));
    expect(host.edits).toHaveLength(1);
    const edit = host.edits[0]!;
    expect(edit.blob).not.toBeNull();
    // A square image in a square box: the cover fit is the whole frame.
    expect(edit.fit.rect).toEqual({ x: 0, y: 0, width: 1, height: 1 });
    expect(edit.fit.focal).toEqual({ x: 0.5, y: 0.5 });
  });

  it('zooming commits an adjusted, clamped fit; Done shows the local preview', async () => {
    setupFetcher();
    const fixture = TestBed.createComponent(Host);
    const host = fixture.componentInstance;
    host.mode.set('edit');
    await fixture.whenStable();
    await until(fixture, () => query(fixture, '[data-tm-image-action="replace"]') !== null);
    await pickFile(fixture, await pngBlob(100, 100));

    const surface = query<HTMLElement>(fixture, '.tm-image__crop')!;
    surface.dispatchEvent(new KeyboardEvent('keydown', { key: '+', bubbles: true }));
    await new Promise((resolve) => setTimeout(resolve, 400)); // commit debounce
    await fixture.whenStable();
    expect(host.edits).toHaveLength(2);
    const zoomed = host.edits[1]!;
    expect(zoomed.fit.rect.width).toBeCloseTo(1 / 1.1, 3);
    expect(zoomed.fit.rect.x).toBeGreaterThanOrEqual(0);
    expect(zoomed.fit.rect.x + zoomed.fit.rect.width).toBeLessThanOrEqual(1);

    // Done → the picked image displays cropped per the committed fit.
    (query<HTMLButtonElement>(fixture, '.tm-image__edit-bar button')!).click();
    await until(fixture, () => query(fixture, '.tm-image__crop-view') !== null);
    expect(query(fixture, '.tm-image__crop-view img')?.getAttribute('alt')).toBe('Agent avatar');
  });

  it('re-fitting an existing image fetches the ORIGINAL and emits blob: null', async () => {
    const control = setupFetcher();
    const fixture = TestBed.createComponent(Host);
    const host = fixture.componentInstance;
    host.mode.set('edit');
    host.editSrc.set('/img/agent-7/original');
    await fixture.whenStable();
    await until(fixture, () => query(fixture, '[data-tm-image-action="adjust"]') !== null);

    (query<HTMLButtonElement>(fixture, '[data-tm-image-action="adjust"]')!).click();
    await until(fixture, () => query(fixture, '.tm-image__crop') !== null);
    expect(control.urls).toContain('/img/agent-7/original'); // no size param

    const slider = query<HTMLInputElement>(fixture, '.tm-image__zoom')!;
    slider.value = '2';
    slider.dispatchEvent(new Event('input'));
    slider.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    expect(host.edits.length).toBeGreaterThan(0);
    const last = host.edits[host.edits.length - 1]!;
    expect(last.blob).toBeNull(); // the bytes never round-trip
    expect(last.fit.rect.width).toBeCloseTo(0.5, 3);
  });

  it('delete emits null and returns the box to the placeholder', async () => {
    setupFetcher();
    const fixture = TestBed.createComponent(Host);
    const host = fixture.componentInstance;
    host.mode.set('edit');
    await fixture.whenStable();
    await until(fixture, () => query(fixture, '[data-tm-image-action="remove"]') !== null);

    const loader = TestbedHarnessEnvironment.loader(fixture);
    const harness = await loader.getHarness(TmImageHarness);
    expect(await harness.isShowingImage()).toBe(true);
    await harness.clickRemove();
    expect(host.edits).toEqual([null]);
    expect(await harness.isShowingImage()).toBe(false);
    expect(query(fixture, '.tm-image__glyph')).not.toBeNull(); // placeholder glyph
  });

  it('rejections surface through the status region', async () => {
    setupFetcher();
    const fixture = TestBed.createComponent(Host);
    const host = fixture.componentInstance;
    host.mode.set('edit');
    await fixture.whenStable();
    await until(fixture, () => query(fixture, '.tm-image__file-input') !== null);

    dispatchPick(fixture, new File(['not an image'], 'nope.png', { type: 'image/png' }));
    await until(fixture, () => noticeText(fixture) !== '');
    expect(noticeText(fixture)).toBe('The file is not a supported image');
    expect(host.edits).toEqual([]); // nothing emitted
  });

  it('an oversized pick names the ceiling in full, not rounded to whole MB', async () => {
    setupFetcher();
    const fixture = TestBed.createComponent(Host);
    const host = fixture.componentInstance;
    host.mode.set('edit');
    host.maxFileBytes.set(512 * 1024);
    await fixture.whenStable();
    await until(fixture, () => query(fixture, '.tm-image__file-input') !== null);

    const bytes = new Uint8Array(600 * 1024);
    dispatchPick(fixture, new File([bytes], 'big.png', { type: 'image/png' }));
    await until(fixture, () => noticeText(fixture) !== '');
    expect(noticeText(fixture)).toBe('The file is larger than 0.5 MB');
  });

  it('a load failure announces through the status region, and recovery retracts it', async () => {
    const control = setupFetcher();
    control.fail = true;
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    // The glyph is silent to a screen reader until it is focused; the
    // failure has to reach the live region like every other outcome.
    await until(fixture, () => noticeText(fixture) !== '');
    expect(noticeText(fixture)).toBe('The image could not be loaded');

    control.fail = false;
    fixture.componentInstance.etag.set('v2');
    await until(fixture, () => query(fixture, '.tm-image__img') !== null);
    expect(noticeText(fixture)).toBe('');
  });

  it('re-opening a stored image restores its committed crop; a new version does not', async () => {
    const control = setupFetcher();
    const fixture = TestBed.createComponent(Host);
    const host = fixture.componentInstance;
    host.mode.set('edit');
    host.editSrc.set('/img/agent-7/original');
    await fixture.whenStable();
    await until(fixture, () => query(fixture, '[data-tm-image-action="adjust"]') !== null);

    const openPane = async (): Promise<HTMLInputElement> => {
      query<HTMLButtonElement>(fixture, '[data-tm-image-action="adjust"]')!.click();
      await until(fixture, () => query(fixture, '.tm-image__zoom') !== null);
      return query<HTMLInputElement>(fixture, '.tm-image__zoom')!;
    };
    const closePane = async (): Promise<void> => {
      query<HTMLButtonElement>(fixture, '.tm-image__edit-bar button')!.click();
      await until(fixture, () => query(fixture, '.tm-image__zoom') === null);
    };

    const slider = await openPane();
    slider.value = '2';
    slider.dispatchEvent(new Event('input'));
    slider.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    await closePane();

    // Seconds later the user re-opens the pane — their crop is still theirs.
    expect((await openPane()).value).toBe('2');
    await closePane();

    // A different image behind the same box starts at the cover fit again.
    const fetches = control.urls.length;
    host.etag.set('v2');
    await until(fixture, () => control.urls.length > fetches);
    expect((await openPane()).value).toBe('1');
  });
});

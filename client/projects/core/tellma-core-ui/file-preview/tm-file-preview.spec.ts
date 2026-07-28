// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmFilePreviewHarness } from '@tellma/core-ui-testing';

import { tmDetectPreviewKind } from './internal/kind-detection';
import { TmFilePreview } from './tm-file-preview';

async function pngBlob(): Promise<Blob> {
  const canvas = document.createElement('canvas');
  canvas.width = 8;
  canvas.height = 8;
  canvas.getContext('2d')!.fillRect(0, 0, 8, 8);
  return new Promise((resolve) => canvas.toBlob((blob) => resolve(blob!), 'image/png'));
}

@Component({ template: `` })
class Host {}

async function setup(): Promise<{ fixture: ComponentFixture<Host>; preview: TmFilePreview }> {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  await fixture.whenStable();
  return { fixture, preview: TestBed.inject(TmFilePreview) };
}

function content(): HTMLElement | null {
  return document.querySelector('tm-file-preview-content');
}

async function until(
  fixture: ComponentFixture<unknown>,
  predicate: () => boolean,
  timeoutMs = 3000,
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

/** Temporarily forces `navigator.pdfViewerEnabled`. */
function withPdfViewer(enabled: boolean): () => void {
  const descriptor = Object.getOwnPropertyDescriptor(Navigator.prototype, 'pdfViewerEnabled');
  Object.defineProperty(navigator, 'pdfViewerEnabled', { value: enabled, configurable: true });
  return () => {
    delete (navigator as unknown as Record<string, unknown>)['pdfViewerEnabled'];
    void descriptor; // the prototype getter is visible again after delete
  };
}

describe('tmDetectPreviewKind', () => {
  it('detects from MIME first, extension second, and never sniffs', () => {
    expect(tmDetectPreviewKind('a.png', 'image/png')).toBe('image');
    expect(tmDetectPreviewKind('a.png', undefined)).toBe('image');
    expect(tmDetectPreviewKind('vector.svg', 'image/svg+xml')).toBe('svg');
    expect(tmDetectPreviewKind('vector.svg', undefined)).toBe('svg');
    expect(tmDetectPreviewKind('doc.pdf', 'application/pdf')).toBe('pdf');
    expect(tmDetectPreviewKind('clip.mp4', 'video/mp4')).toBe('video');
    expect(tmDetectPreviewKind('note.wav', undefined)).toBe('audio');
    expect(tmDetectPreviewKind('data.csv', 'text/csv')).toBe('text');
    expect(tmDetectPreviewKind('data.json', undefined)).toBe('text');
    // MIME wins over a lying extension.
    expect(tmDetectPreviewKind('actually.png', 'text/plain')).toBe('text');
    // HTML is never a renderable kind — policy.
    expect(tmDetectPreviewKind('page.html', 'text/html')).toBe('unsupported');
    expect(tmDetectPreviewKind('page.html', undefined)).toBe('unsupported');
    expect(tmDetectPreviewKind('blob.bin', undefined)).toBe('unsupported');
    // A generic octet-stream falls back to the extension.
    expect(tmDetectPreviewKind('report.pdf', 'application/octet-stream')).toBe('pdf');
  });
});

describe('TmFilePreview', () => {
  it('opens an lg modal titled with the file name and renders a raster image', async () => {
    const { fixture, preview } = await setup();
    const ref = preview.open({
      name: 'photo.png',
      type: 'image/png',
      size: 2048,
      source: await pngBlob(),
    });
    await until(fixture, () => content()?.querySelector('img.tm-preview__media') !== null);

    expect(document.querySelector('.tm-modal-panel--lg')).not.toBeNull();
    expect(document.querySelector('.tm-modal__title')?.textContent).toBe('photo.png');
    const img = content()!.querySelector<HTMLImageElement>('img.tm-preview__media')!;
    expect(img.getAttribute('alt')).toBe('photo.png');
    expect(img.src.startsWith('blob:')).toBe(true);

    const harness = await TestbedHarnessEnvironment.documentRootLoader(fixture).getHarness(
      TmFilePreviewHarness,
    );
    expect(await harness.hasPrintButton()).toBe(true); // images only
    expect(await harness.getDownloadName()).toBe('photo.png');
    ref.close();
  });

  it('renders plain text escaped, capped at 1 MB with a notice', async () => {
    const { fixture, preview } = await setup();
    const payload = `<script>alert(1)</script>\n${'x'.repeat(2 * 1024 * 1024)}`;
    preview.open({
      name: 'big.txt',
      type: 'text/plain',
      source: new Blob([payload], { type: 'text/plain' }),
    });
    await until(fixture, () => content()?.querySelector('.tm-preview__text') !== null);

    const pre = content()!.querySelector('.tm-preview__text')!;
    expect(pre.innerHTML).toContain('&lt;script&gt;'); // escaped, never markup
    expect(pre.textContent?.length).toBe(1024 * 1024); // capped
    expect(content()!.querySelector('.tm-preview__notice')).not.toBeNull();
  });

  it('NEVER renders HTML — the download-only card, even with an html blob (policy pin)', async () => {
    const { fixture, preview } = await setup();
    preview.open({
      name: 'page.html',
      type: 'text/html',
      source: new Blob(['<h1>hi</h1><script>document.title="pwned"</script>'], {
        type: 'text/html',
      }),
    });
    await until(fixture, () => content()?.querySelector('.tm-preview__card') !== null);
    expect(content()!.querySelector('iframe')).toBeNull();
    expect(content()!.querySelector('.tm-preview__text')).toBeNull();
    expect(document.title).not.toBe('pwned');
    const harness = await TestbedHarnessEnvironment.documentRootLoader(fixture).getHarness(
      TmFilePreviewHarness,
    );
    expect(await harness.getCardTitle()).toBe('Preview not available');
    expect(await harness.getDownloadName()).toBe('page.html'); // download still offered
    expect(await harness.hasPrintButton()).toBe(false);
  });

  it('a .pdf-named file whose BLOB is text/html never reaches the frame as HTML', async () => {
    // Detection reads the declared metadata, but the object URL serves the
    // blob's own Content-Type — an attacker uploading HTML as
    // 'invoice.pdf' would otherwise execute same-origin in the
    // deliberately non-sandboxed frame.
    const restore = withPdfViewer(true);
    try {
      const { fixture, preview } = await setup();
      preview.open({
        name: 'invoice.pdf',
        type: 'application/pdf',
        source: new Blob(['<script>document.title="pwned"</script>'], { type: 'text/html' }),
      });
      await until(fixture, () => content()?.querySelector('iframe.tm-preview__frame') !== null);
      const frameUrl = content()!
        .querySelector('iframe.tm-preview__frame')!
        .getAttribute('src')!;
      const served = await fetch(frameUrl).then((response) => response.blob());
      expect(served.type).toBe('application/pdf'); // re-wrapped, never text/html
      expect(document.title).not.toBe('pwned');
    } finally {
      restore();
    }
  });

  it('a {url} source with a dangerous scheme falls back to the download-only card', async () => {
    const restore = withPdfViewer(true);
    try {
      const { fixture, preview } = await setup();
      preview.open({
        name: 'evil.pdf',
        type: 'application/pdf',
        source: { url: 'javascript:document.title="pwned"' },
      });
      await until(fixture, () => content()?.querySelector('.tm-preview__card') !== null);
      expect(content()!.querySelector('iframe')).toBeNull();
      expect(document.title).not.toBe('pwned');
    } finally {
      restore();
    }
  });

  it('PDF renders through a NON-sandboxed iframe only when the browser has a viewer', async () => {
    const restore = withPdfViewer(true);
    try {
      const { fixture, preview } = await setup();
      const ref = preview.open({
        name: 'doc.pdf',
        type: 'application/pdf',
        source: new Blob(['%PDF-1.4'], { type: 'application/pdf' }),
      });
      await until(fixture, () => content()?.querySelector('iframe.tm-preview__frame') !== null);
      const frame = content()!.querySelector('iframe.tm-preview__frame')!;
      expect(frame.hasAttribute('sandbox')).toBe(false); // sandboxed frames never render PDFs
      expect(frame.getAttribute('src')?.startsWith('blob:')).toBe(true);
      ref.close();
      await fixture.whenStable();
    } finally {
      restore();
    }

    TestBed.resetTestingModule(); // a second, independent open in this test
    const restoreOff = withPdfViewer(false);
    try {
      const { fixture, preview } = await setup();
      preview.open({
        name: 'doc.pdf',
        type: 'application/pdf',
        source: new Blob(['%PDF-1.4'], { type: 'application/pdf' }),
      });
      await until(fixture, () => content()?.querySelector('.tm-preview__card') !== null);
      expect(content()!.querySelector('iframe')).toBeNull(); // honest fallback
    } finally {
      restoreOff();
    }
  });

  it('media: a playable audio blob renders controls; an unplayable type is the card upfront', async () => {
    const { fixture, preview } = await setup();
    preview.open({
      name: 'beep.wav',
      type: 'audio/wav',
      source: new Blob([new Uint8Array(64)], { type: 'audio/wav' }),
    });
    await until(fixture, () => content()?.querySelector('audio') !== null);
    const audio = content()!.querySelector('audio')!;
    expect(audio.hasAttribute('controls')).toBe(true);
    expect(audio.getAttribute('preload')).toBe('metadata');

    preview.open({
      name: 'clip.fake',
      type: 'video/x-not-a-real-codec',
      source: new Blob([new Uint8Array(64)], { type: 'video/x-not-a-real-codec' }),
    });
    await until(
      fixture,
      () => document.querySelectorAll('tm-file-preview-content').length === 2,
    );
    const second = document.querySelectorAll('tm-file-preview-content')[1];
    await until(fixture, () => second.querySelector('.tm-preview__card') !== null);
    expect(second.querySelector('video')).toBeNull();
  });

  it('a {url} media source streams directly — no object URL', async () => {
    const { fixture, preview } = await setup();
    preview.open({
      name: 'song.mp3',
      type: 'audio/mpeg',
      source: { url: '/media/song.mp3' },
    });
    await until(fixture, () => content()?.querySelector('audio') !== null);
    const audio = content()!.querySelector('audio')!;
    expect(audio.getAttribute('src')).toBe('/media/song.mp3');
  });

  it('a lazy loader shows the busy state, then renders; a rejection shows the error card', async () => {
    const { fixture, preview } = await setup();
    let resolveBlob!: (blob: Blob) => void;
    preview.open({
      name: 'slow.txt',
      type: 'text/plain',
      source: () => new Promise<Blob>((resolve) => (resolveBlob = resolve)),
    });
    await until(fixture, () => content() !== null);
    const harness = await TestbedHarnessEnvironment.documentRootLoader(fixture).getHarness(
      TmFilePreviewHarness,
    );
    expect(await harness.isLoading()).toBe(true);

    resolveBlob(new Blob(['later'], { type: 'text/plain' }));
    await until(fixture, () => content()?.querySelector('.tm-preview__text') !== null);
    expect(await harness.getTextContent()).toBe('later');

    preview.open({
      name: 'broken.txt',
      type: 'text/plain',
      source: () => Promise.reject(new Error('auth expired')),
    });
    await until(fixture, () =>
      Boolean(
        document.querySelectorAll('tm-file-preview-content')[1]?.querySelector(
          '.tm-preview__card-title',
        ),
      ),
    );
    const second = document.querySelectorAll('tm-file-preview-content')[1];
    expect(second.querySelector('.tm-preview__card-title')?.textContent?.trim()).toBe(
      'The file could not be loaded',
    );
  });

  it('revokes its object URLs when the modal closes', async () => {
    const { fixture, preview } = await setup();
    const revoke = vi.spyOn(URL, 'revokeObjectURL');
    try {
      const ref = preview.open({
        name: 'photo.png',
        type: 'image/png',
        source: await pngBlob(),
      });
      await until(fixture, () => content()?.querySelector('img.tm-preview__media') !== null);
      const url = content()!
        .querySelector<HTMLImageElement>('img.tm-preview__media')!
        .getAttribute('src')!;
      ref.close();
      await until(fixture, () => content() === null);
      expect(revoke.mock.calls.map((call) => call[0])).toContain(url);
    } finally {
      revoke.mockRestore();
    }
  });
});

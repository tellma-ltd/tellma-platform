// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, inject } from '@angular/core';

import { TmButton } from '@tellma/core-ui/button';
import { TmFilePreview } from '@tellma/core-ui/file-preview';
import { TmFilePicker, type TmFileSelection } from '@tellma/core-ui/files';

/** A deterministic 64×64 PNG built on a canvas. */
async function pngBlob(): Promise<Blob> {
  const canvas = document.createElement('canvas');
  canvas.width = 64;
  canvas.height = 64;
  const context = canvas.getContext('2d')!;
  context.fillStyle = '#4ca0b6';
  context.fillRect(0, 0, 64, 64);
  context.fillStyle = '#0a141a';
  context.fillRect(0, 0, 32, 32);
  return new Promise((resolve) => canvas.toBlob((blob) => resolve(blob!), 'image/png'));
}

/** A minimal single-page PDF. */
function pdfBlob(): Blob {
  const pdf = `%PDF-1.4
1 0 obj
<< /Type /Catalog /Pages 2 0 R >>
endobj
2 0 obj
<< /Type /Pages /Kids [3 0 R] /Count 1 >>
endobj
3 0 obj
<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>
endobj
trailer
<< /Size 4 /Root 1 0 R >>
%%EOF
`;
  return new Blob([pdf], { type: 'application/pdf' });
}

/** A 0.2s 440Hz WAV generated in memory. */
function wavBlob(): Blob {
  const rate = 8000;
  const count = Math.floor(rate * 0.2);
  const buffer = new ArrayBuffer(44 + count * 2);
  const view = new DataView(buffer);
  const writeAscii = (offset: number, text: string): void => {
    for (let i = 0; i < text.length; i += 1) {
      view.setUint8(offset + i, text.charCodeAt(i));
    }
  };
  writeAscii(0, 'RIFF');
  view.setUint32(4, 36 + count * 2, true);
  writeAscii(8, 'WAVEfmt ');
  view.setUint32(16, 16, true);
  view.setUint16(20, 1, true);
  view.setUint16(22, 1, true);
  view.setUint32(24, rate, true);
  view.setUint32(28, rate * 2, true);
  view.setUint16(32, 2, true);
  view.setUint16(34, 16, true);
  writeAscii(36, 'data');
  view.setUint32(40, count * 2, true);
  for (let i = 0; i < count; i += 1) {
    view.setInt16(44 + i * 2, Math.round(12000 * Math.sin((2 * Math.PI * 440 * i) / rate)), true);
  }
  return new Blob([buffer], { type: 'audio/wav' });
}

/**
 * TmFilePreview demo host — one button per kind (image, SVG, PDF, audio,
 * text, truncated text, HTML policy pin, unknown), URL-sourced audio,
 * lazy/failing loaders, and a pick-and-preview flow through tmFilePicker.
 * Drives the Playwright battery.
 */
@Component({
  imports: [TmButton, TmFilePicker],
  template: `
    <h2>File preview</h2>

    <section>
      <h3>By kind</h3>
      <div class="row">
        <button tmButton variant="secondary" data-testid="preview-image" (click)="openImage()">
          Image
        </button>
        <button tmButton variant="secondary" data-testid="preview-svg" (click)="openSvg()">
          SVG
        </button>
        <button tmButton variant="secondary" data-testid="preview-pdf" (click)="openPdf()">
          PDF
        </button>
        <button tmButton variant="secondary" data-testid="preview-audio" (click)="openAudio()">
          Audio (blob)
        </button>
        <button tmButton variant="secondary" data-testid="preview-audio-url" (click)="openAudioUrl()">
          Audio (url)
        </button>
        <button tmButton variant="secondary" data-testid="preview-text" (click)="openText()">
          Text
        </button>
        <button tmButton variant="secondary" data-testid="preview-big-text" (click)="openBigText()">
          Text (2 MB)
        </button>
        <button tmButton variant="secondary" data-testid="preview-html" (click)="openHtml()">
          HTML (never rendered)
        </button>
        <button tmButton variant="secondary" data-testid="preview-unknown" (click)="openUnknown()">
          Unknown
        </button>
      </div>
    </section>

    <section>
      <h3>Lazy sources</h3>
      <div class="row">
        <button tmButton variant="secondary" data-testid="preview-lazy" (click)="openLazy()">
          Slow loader
        </button>
        <button tmButton variant="secondary" data-testid="preview-failing" (click)="openFailing()">
          Failing loader
        </button>
      </div>
    </section>

    <section>
      <h3>Pick and preview</h3>
      <button
        tmButton
        variant="secondary"
        tmFilePicker
        data-testid="pick-and-preview"
        (filesSelected)="previewPicked($event)"
      >
        Pick a file…
      </button>
    </section>
  `,
  styles: `
    section {
      margin-block-end: 24px;
    }
    .row {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
    }
  `,
})
export class FilePreviewStory {
  private readonly preview = inject(TmFilePreview);

  protected async openImage(): Promise<void> {
    const blob = await pngBlob();
    this.preview.open({ name: 'pattern.png', type: 'image/png', size: blob.size, source: blob });
  }

  protected openSvg(): void {
    const svg = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16"><rect width="16" height="16" fill="#4ca0b6"/></svg>';
    const blob = new Blob([svg], { type: 'image/svg+xml' });
    this.preview.open({ name: 'vector.svg', type: 'image/svg+xml', size: blob.size, source: blob });
  }

  protected openPdf(): void {
    const blob = pdfBlob();
    this.preview.open({ name: 'doc.pdf', type: 'application/pdf', size: blob.size, source: blob });
  }

  protected openAudio(): void {
    const blob = wavBlob();
    this.preview.open({ name: 'beep.wav', type: 'audio/wav', size: blob.size, source: blob });
  }

  protected openAudioUrl(): void {
    this.preview.open({
      name: 'test-audio.wav',
      type: 'audio/wav',
      source: { url: '/test-audio.wav' },
    });
  }

  protected openText(): void {
    const blob = new Blob(['Ledger note: posting runs nightly at 02:00.\n<not markup>'], {
      type: 'text/plain',
    });
    this.preview.open({ name: 'note.txt', type: 'text/plain', size: blob.size, source: blob });
  }

  protected openBigText(): void {
    const blob = new Blob(['start-marker\n' + 'x'.repeat(2 * 1024 * 1024)], {
      type: 'text/plain',
    });
    this.preview.open({ name: 'big.txt', type: 'text/plain', size: blob.size, source: blob });
  }

  protected openHtml(): void {
    const blob = new Blob(['<h1>hi</h1><script>document.title="pwned"</script>'], {
      type: 'text/html',
    });
    this.preview.open({ name: 'page.html', type: 'text/html', size: blob.size, source: blob });
  }

  protected openUnknown(): void {
    const blob = new Blob([new Uint8Array(32)], { type: '' });
    this.preview.open({ name: 'blob.bin', size: blob.size, source: blob });
  }

  protected openLazy(): void {
    this.preview.open({
      name: 'slow.txt',
      type: 'text/plain',
      source: () =>
        new Promise((resolve) =>
          setTimeout(() => resolve(new Blob(['finally here'], { type: 'text/plain' })), 700),
        ),
    });
  }

  protected openFailing(): void {
    this.preview.open({
      name: 'broken.txt',
      type: 'text/plain',
      source: () => Promise.reject(new Error('auth expired')),
    });
  }

  protected previewPicked(selection: TmFileSelection): void {
    const file = selection.accepted[0];
    if (file !== undefined) {
      this.preview.open({ name: file.name, type: file.type, size: file.size, source: file });
    }
  }
}

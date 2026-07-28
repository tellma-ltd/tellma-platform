// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, inject, type OnDestroy, signal } from '@angular/core';
import { DomSanitizer, type SafeResourceUrl } from '@angular/platform-browser';

import { TM_UI_TRANSLATE, TmL10n } from '@tellma/core-ui';
import { TmButton } from '@tellma/core-ui/button';
import { TM_MODAL_DATA, TmModalFooter } from '@tellma/core-ui/modal';
import { TmSpinner } from '@tellma/core-ui/spinner';

import type { TmPreviewFile } from '../tm-file-preview';
import { tmDetectPreviewKind, type TmPreviewKind } from './kind-detection';

/** The plain-text preview cap; the tail is truncated with a notice. */
const TEXT_CAP_BYTES = 1024 * 1024;

/**
 * The modal body of `TmFilePreview`: resolves the source (blob, lazy
 * loader with a spinner, or a direct URL for streamable media), renders
 * per kind, and pins the size/print/download footer. Every object URL it
 * mints is revoked on destroy (= modal close).
 *
 * @internal Opened exclusively by `TmFilePreview`; never use directly.
 */
@Component({
  selector: 'tm-file-preview-content',
  imports: [TmButton, TmModalFooter, TmSpinner],
  template: `
    <div class="tm-preview" [attr.aria-busy]="state() === 'loading'">
      @switch (state()) {
        @case ('loading') {
          <tm-spinner class="tm-preview__spinner" />
        }
        @case ('error') {
          <div class="tm-preview__card">
            <p class="tm-preview__card-title">{{ loadErrorLabel() }}</p>
          </div>
        }
        @default {
          @switch (viewKind()) {
            @case ('image') {
              <img class="tm-preview__media" [src]="displayUrl()" [alt]="file.name" />
            }
            @case ('svg') {
              <!-- img-only: scripts never execute inside an <img>. -->
              <img class="tm-preview__media" [src]="displayUrl()" [alt]="file.name" />
            }
            @case ('pdf') {
              <!-- The browser's own viewer is trusted UI (print/download
                   chrome included); sandboxed iframes never render PDFs,
                   so this frame is deliberately non-sandboxed. -->
              <iframe class="tm-preview__frame" [src]="pdfUrl()" [title]="file.name"></iframe>
            }
            @case ('video') {
              <!-- No autoplay: playback starts on the user's play action. -->
              <video
                class="tm-preview__media"
                controls
                preload="metadata"
                [src]="displayUrl()"
                (error)="onMediaError()"
              ></video>
            }
            @case ('audio') {
              <audio
                class="tm-preview__audio"
                controls
                preload="metadata"
                [src]="displayUrl()"
                (error)="onMediaError()"
              ></audio>
            }
            @case ('text') {
              @if (truncated()) {
                <p class="tm-preview__notice">{{ truncatedLabel() }}</p>
              }
              <pre class="tm-preview__text">{{ text() }}</pre>
            }
            @default {
              <div class="tm-preview__card">
                <p class="tm-preview__card-title">{{ unsupportedLabel() }}</p>
                <p class="tm-preview__card-hint">{{ unsupportedHintLabel() }}</p>
              </div>
            }
          }
        }
      }
      <!-- One PERMANENT live region. The messages above live inside the
           @switch, which destroys and recreates its branches — text that
           arrives together with its region is unreliably announced, and
           the modal is already open and focused when a lazy load fails. -->
      <div class="tm-preview__live" role="status">{{ liveMessage() }}</div>
    </div>
    <div tmModalFooter class="tm-preview__footer">
      <span class="tm-preview__size">{{ sizeText() }}</span>
      @if (canPrint()) {
        <button
          tmButton
          variant="ghost"
          data-tm-preview-action="print"
          (click)="print()"
        >
          {{ printLabel() }}
        </button>
      }
      @if (downloadUrl(); as url) {
        <a
          class="tm-preview__download"
          data-tm-preview-action="download"
          [href]="url"
          [download]="file.name"
        >
          {{ downloadLabel() }}
        </a>
      }
    </div>
  `,
  styleUrl: './tm-file-preview-content.css',
  host: { class: 'tm-file-preview' },
})
export class ɵTmFilePreviewContent implements OnDestroy {
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly l10n = inject(TmL10n);

  protected readonly file = inject<TmPreviewFile>(TM_MODAL_DATA);

  protected readonly loadErrorLabel = this.translate('preview.loadError');
  protected readonly unsupportedLabel = this.translate('preview.unsupported');
  protected readonly unsupportedHintLabel = this.translate('preview.unsupportedHint');
  protected readonly truncatedLabel = this.translate('preview.truncated');
  protected readonly printLabel = this.translate('preview.print');
  protected readonly downloadLabel = this.translate('preview.download');

  protected readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  /** The detected kind, downgraded to unsupported on decode/play failures. */
  protected readonly effectiveKind = signal<TmPreviewKind>(
    tmDetectPreviewKind(this.file.name, this.file.type),
  );
  protected readonly displayUrl = signal<string | null>(null);
  protected readonly pdfUrl = signal<SafeResourceUrl | null>(null);
  protected readonly downloadUrl = signal<string | null>(null);
  protected readonly text = signal('');
  protected readonly truncated = signal(false);

  private readonly objectUrls = new Set<string>();
  private destroyed = false;

  /** The kind the template renders — PDF needs the browser's own viewer. */
  protected readonly viewKind = computed<TmPreviewKind>(() => {
    const kind = this.effectiveKind();
    if (kind === 'pdf' && navigator.pdfViewerEnabled !== true) {
      return 'unsupported';
    }
    return kind;
  });

  protected readonly canPrint = computed(
    () => this.state() === 'ready' && this.viewKind() === 'image',
  );

  /** What the permanent live region announces (`''` = nothing to say). */
  protected readonly liveMessage = computed(() => {
    if (this.state() === 'error') {
      return this.loadErrorLabel();
    }
    return this.truncated() ? this.truncatedLabel() : '';
  });

  /** Localized size line (the active locale drives the digits). */
  protected readonly sizeText = computed(() => {
    const size = this.file.size;
    if (size === undefined) {
      return '';
    }
    const locale = this.l10n.locale();
    const [value, unit] =
      size < 1024
        ? [size, 'byte']
        : size < 1024 * 1024
          ? [size / 1024, 'kilobyte']
          : [size / (1024 * 1024), 'megabyte'];
    return new Intl.NumberFormat(locale, {
      style: 'unit',
      unit,
      maximumFractionDigits: 1,
    }).format(value);
  });

  constructor() {
    void this.resolve();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    for (const url of this.objectUrls) {
      URL.revokeObjectURL(url);
    }
    this.objectUrls.clear();
  }

  /** A media element that cannot play its source becomes the card. */
  protected onMediaError(): void {
    this.effectiveKind.set('unsupported');
  }

  /** Prints the image through a transient same-origin frame. */
  protected print(): void {
    const url = this.displayUrl();
    if (url === null) {
      return;
    }
    const frame = document.createElement('iframe');
    frame.style.position = 'fixed';
    frame.style.inlineSize = '0';
    frame.style.blockSize = '0';
    frame.style.border = 'none';
    document.body.appendChild(frame);
    const doc = frame.contentDocument;
    if (doc === null) {
      frame.remove();
      return;
    }
    const img = doc.createElement('img');
    img.style.maxWidth = '100%';
    // The frame is torn down on EVERY outcome — a load failure must not
    // leave a detached iframe behind.
    const cleanup = (): void => frame.remove();
    img.addEventListener('load', () => {
      frame.contentWindow?.focus();
      frame.contentWindow?.print();
      setTimeout(cleanup, 1000);
    });
    img.addEventListener('error', cleanup);
    img.src = url;
    doc.body.appendChild(img);
  }

  // ---- source resolution ----

  private async resolve(): Promise<void> {
    const source = this.file.source;
    if (source instanceof Blob) {
      await this.presentBlob(source);
      return;
    }
    if (typeof source === 'function') {
      try {
        const blob = await source();
        if (!this.destroyed) {
          await this.presentBlob(blob);
        }
      } catch {
        if (!this.destroyed) {
          this.state.set('error');
        }
      }
      return;
    }
    this.presentUrl(source.url);
  }

  /**
   * Direct URLs: media streams (range requests). Everything else needs
   * bytes this component fetched itself — see the `default` branch.
   */
  private presentUrl(url: string): void {
    this.downloadUrl.set(url);
    const kind = this.effectiveKind();
    switch (kind) {
      case 'image':
      case 'svg':
      case 'video':
      case 'audio':
        this.displayUrl.set(url);
        break;
      default:
        // Text and unknown URL sources have no bytes to show, and a PDF
        // is deliberately here too: the viewer frame is NOT sandboxed, so
        // it may only ever be handed bytes this component fetched and
        // re-typed itself. A URL is served by an endpoint whose real
        // content type we cannot see — one that echoed a stored
        // `text/html` would run script in this origin. Download-only.
        this.effectiveKind.set('unsupported');
        break;
    }
    if (kind === 'video' || kind === 'audio') {
      this.gateMediaType(kind);
    }
    this.state.set('ready');
  }

  /**
   * Mints the object URL behind the download link. The type is ALWAYS
   * replaced: `download` saves the file whatever the blob claims to be,
   * but "Open link in new tab" NAVIGATES to the href, and a blob URL
   * inherits this app's origin. No allowlist can gate that safely — the
   * set of types a browser hands to a scripting parser is open-ended
   * (`text/html`, every `+xml` media type via XSLT or XHTML-namespaced
   * script), and an EMPTY type is MIME-SNIFFED from the bytes, so HTML
   * uploaded under no type at all would still execute here.
   */
  private mintDownloadUrl(blob: Blob): string {
    return this.mintObjectUrl(new Blob([blob], { type: 'application/octet-stream' }));
  }

  private async presentBlob(blob: Blob): Promise<void> {
    const kind = this.effectiveKind();
    // The download href is its own inert URL, minted up front so it is
    // registered for revocation before any await — and so a truncated text
    // preview still offers the FULL bytes. The render sinks below mint a
    // real-typed URL only where one is actually rendered.
    this.downloadUrl.set(this.mintDownloadUrl(blob));
    if (kind === 'text') {
      this.truncated.set(blob.size > TEXT_CAP_BYTES);
      this.text.set(await blob.slice(0, TEXT_CAP_BYTES).text());
      if (this.destroyed) {
        return; // the modal closed mid-read — nothing left to render into
      }
      this.state.set('ready');
      return;
    }
    switch (kind) {
      case 'image':
      case 'svg': {
        // Decode off-DOM; a failure becomes the unsupported card.
        const url = this.mintObjectUrl(blob);
        try {
          const probe = new Image();
          probe.src = url;
          await probe.decode();
          this.displayUrl.set(url);
        } catch {
          this.effectiveKind.set('unsupported');
        }
        break;
      }
      case 'pdf': {
        // The DECLARED kind chose the pdf renderer, but the object URL
        // serves the blob's OWN Content-Type — an attacker uploading HTML
        // as 'invoice.pdf' would otherwise execute same-origin in the
        // non-sandboxed frame. Re-wrap so the URL is application/pdf no
        // matter what the bytes claim.
        const pdfBlob =
          blob.type === 'application/pdf' ? blob : new Blob([blob], { type: 'application/pdf' });
        this.pdfUrl.set(this.sanitizer.bypassSecurityTrustResourceUrl(this.mintObjectUrl(pdfBlob)));
        break;
      }
      case 'video':
      case 'audio':
        this.displayUrl.set(this.mintObjectUrl(blob));
        this.gateMediaType(kind);
        break;
      default:
        break; // unsupported: the card + the download link, nothing to mint
    }
    if (!this.destroyed) {
      this.state.set('ready');
    }
  }

  /** `canPlayType('')` means the engine cannot play it — the card, upfront. */
  private gateMediaType(kind: 'video' | 'audio'): void {
    const type = this.file.type;
    if (type === undefined || type === '') {
      return; // unknown container: let the element's error event decide
    }
    const probe = document.createElement(kind);
    if (probe.canPlayType(type) === '') {
      this.effectiveKind.set('unsupported');
    }
  }

  /**
   * Mints an object URL this component owns — every one is revoked on
   * close. (A viewer can hold two: the download link's octet-stream
   * re-wrap plus the real-typed URL its render sink needs.)
   */
  private mintObjectUrl(blob: Blob): string {
    const url = URL.createObjectURL(blob);
    this.objectUrls.add(url);
    return url;
  }
}

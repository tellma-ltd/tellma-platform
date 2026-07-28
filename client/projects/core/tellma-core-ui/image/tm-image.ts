// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgTemplateOutlet } from '@angular/common';
import {
  afterNextRender,
  booleanAttribute,
  Component,
  computed,
  contentChild,
  DestroyRef,
  Directive,
  effect,
  ElementRef,
  inject,
  Injector,
  input,
  output,
  signal,
  TemplateRef,
  untracked,
} from '@angular/core';

import { TM_UI_TRANSLATE } from '@tellma/core-ui';
import { TmTooltip } from '@tellma/core-ui/tooltip';

import { ɵTmImageBlobCache } from './internal/blob-cache';
import { tmCoverFit } from './internal/fit';
import { tmDefaultSrcForSize, tmSizeBucket } from './internal/size-buckets';
import { tmProcessPickedFile } from './internal/image-processing';
import { ɵTmImageEditPane } from './internal/tm-image-edit-pane';
import type { TmImageEdit, TmImageFit } from './tm-image-types';

/** The formats the replace dialog accepts. */
const ACCEPT = 'image/png,image/jpeg,image/webp,image/gif,image/avif';

/**
 * Marks the `ng-template` rendered in the no-image state (and while the
 * image loads). Without it, a generic image glyph shows.
 */
@Directive({ selector: 'ng-template[tmImagePlaceholder]' })
export class TmImagePlaceholder {
  /** The placeholder template this marker sits on. */
  readonly template = inject<TemplateRef<void>>(TemplateRef);
}

/** A picked-or-refitted image held locally, displayed cropped per its fit. */
interface LocalPreview {
  readonly url: string;
  readonly blob: Blob;
  readonly width: number;
  readonly height: number;
  readonly fit: TmImageFit;
}

/**
 * The four mutually exclusive box renderings. Named so the emitted
 * declaration carries the alias — a literal union's print order follows
 * the compiler's type interning and churns the API golden.
 */
type DisplayState = 'preview' | 'image' | 'error' | 'placeholder';

/** An active fitting session. */
interface FittingSession {
  readonly url: string;
  readonly width: number;
  readonly height: number;
  readonly pickedBlob: Blob | null;
  readonly initialFit: TmImageFit | null;
}

/**
 * A record image in a fixed box — one component, two modes.
 *
 * View mode renders the sized rendition through the blob cache: deferred
 * until near-viewport, fetched via {@link TM_BLOB_FETCHER} (the app's
 * interceptors apply), decoded off-DOM, and swapped in with zero layout
 * shift; the placeholder template shows until then. Edit mode adds
 * replace / re-fit / delete affordances inside the same fixed box and
 * emits {@link TmImageEdit} on every committed adjustment — the consumer
 * sends it to the server, which crops from the original it holds.
 *
 * Cropping is presentation, not redaction: a re-fit never discards stored
 * pixels — users who cropped something out must delete or replace.
 *
 * @tmGroup media
 * @tmA11yNotes `alt` is required (`''` only for decorative images); the
 *   error state is announced through its accessible label; the crop
 *   surface pans with arrow keys and zooms with `+`/`-` and the
 *   always-visible slider; rejections announce via a `role="status"`
 *   region.
 */
@Component({
  selector: 'tm-image',
  imports: [NgTemplateOutlet, TmTooltip, ɵTmImageEditPane],
  template: `
    @if (fitting(); as session) {
      @defer (on immediate) {
        <tm-image-edit-pane
          [imageUrl]="session.url"
          [naturalWidth]="session.width"
          [naturalHeight]="session.height"
          [boxWidth]="width()"
          [boxHeight]="height()"
          [initialFit]="session.initialFit"
          (fitCommitted)="onFitCommitted($event)"
          (done)="onFitDone()"
        />
      }
    } @else {
      @switch (displayState()) {
        @case ('preview') {
          <div class="tm-image__crop-view">
            <img
              [src]="preview()!.url"
              [alt]="alt()"
              draggable="false"
              [style.width.px]="previewWidth()"
              [style.transform]="previewTransform()"
            />
          </div>
        }
        @case ('image') {
          <img class="tm-image__img" [src]="displayUrl()" [alt]="alt()" draggable="false" />
        }
        @case ('error') {
          <div
            class="tm-image__glyph tm-image__glyph--error"
            role="img"
            tabindex="0"
            [attr.aria-label]="errorLabel()"
            [tmTooltip]="errorLabel()"
          >
            <svg viewBox="0 0 24 24" fill="none" aria-hidden="true">
              <rect x="3.75" y="3.75" width="16.5" height="16.5" rx="2" stroke="currentColor" stroke-width="1.5" />
              <path d="m8 16 3-4 2.2 2.6L16 11l1.5 2" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
              <path d="m4.5 4.5 15 15" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" />
            </svg>
          </div>
        }
        @default {
          @if (placeholder(); as marker) {
            <ng-container [ngTemplateOutlet]="marker.template" />
          } @else {
            <div class="tm-image__glyph" aria-hidden="true">
              <svg viewBox="0 0 24 24" fill="none" aria-hidden="true">
                <rect x="3.75" y="3.75" width="16.5" height="16.5" rx="2" stroke="currentColor" stroke-width="1.5" />
                <circle cx="9" cy="9.5" r="1.4" fill="currentColor" />
                <path d="m7 16.5 3.2-3.6 2.5 2.8 2-2.2 2.3 3" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
              </svg>
            </div>
          }
        }
      }
      @if (mode() === 'edit') {
        <div class="tm-image__chrome">
          <button
            type="button"
            class="tm-image__chrome-button"
            data-tm-image-action="replace"
            (click)="pickerInput.click()"
          >
            {{ replaceLabel() }}
          </button>
          @if (canRefit()) {
            <button
              type="button"
              class="tm-image__chrome-button"
              data-tm-image-action="adjust"
              (click)="onRefit()"
            >
              {{ adjustLabel() }}
            </button>
          }
          @if (hasImage()) {
            <button
              type="button"
              class="tm-image__chrome-button"
              data-tm-image-action="remove"
              (click)="onDelete()"
            >
              {{ removeLabel() }}
            </button>
          }
        </div>
        <input
          #pickerInput
          type="file"
          class="tm-image__file-input"
          [accept]="accept"
          (change)="onFilePicked($event)"
        />
      }
    }
    <div
      class="tm-image__notice"
      role="status"
      [class.tm-image__notice--visible]="noticeText() !== null"
    >
      @if (noticeText(); as text) {
        {{ text }}
      }
    </div>
  `,
  styleUrl: './tm-image.css',
  host: {
    class: 'tm-image',
    '[class.tm-image--circle]': 'shape() === "circle"',
    '[style.inline-size.px]': 'width()',
    '[style.block-size.px]': 'height()',
    '[style.aspect-ratio]': 'width() / height()',
  },
})
export class TmImage {
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly cache = inject(ɵTmImageBlobCache);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  /** Base URL of the image resource (no size param). Empty = no image. */
  readonly src = input.required<string>();
  /** The record's current image version stamp (`null` = unknown). */
  readonly etag = input<string | null>(null);
  /** Required alternative text; `''` only for decorative images. */
  readonly alt = input.required<string>();
  /** Box shape; `circle` clips via border-radius. */
  readonly shape = input<'rect' | 'circle'>('rect');
  /** CSS px of the box — fixed for the component's lifetime. */
  readonly width = input.required<number>();
  /** CSS px of the box — fixed for the component's lifetime. */
  readonly height = input.required<number>();
  /** Builds the sized URL. Default appends `?size=<bucket>`. */
  readonly srcForSize = input<(src: string, size: number) => string>(tmDefaultSrcForSize);
  /** Defer the fetch until near-viewport (IntersectionObserver). */
  readonly defer = input(true, { transform: booleanAttribute });
  /**
   * URL of the stored ORIGINAL — enables re-fitting an existing image;
   * absent, an existing image can only be replaced or deleted.
   */
  readonly editSrc = input<string | undefined>(undefined);
  /** `view` renders; `edit` adds replace / re-fit / delete. */
  readonly mode = input<'view' | 'edit'>('view');
  /** Reject picked files over this many bytes (before any processing). */
  readonly maxFileBytes = input(20 * 1024 * 1024);
  /** Downscale picked output whose longest edge exceeds this many px. */
  readonly maxEdgePx = input(4096);

  /** One committed edit; `null` = the user deleted the image. */
  readonly imageChange = output<TmImageEdit | null>();

  /** The resolved no-image template marker, when the consumer provided one. */
  readonly placeholder = contentChild(TmImagePlaceholder);

  /** The hidden file input's `accept` attribute. */
  protected readonly accept = ACCEPT;
  /** Localized label of the load-error glyph. */
  protected readonly errorLabel = this.translate('image.error');
  /** Localized label of the replace button. */
  protected readonly replaceLabel = this.translate('image.replace');
  /** Localized label of the re-fit button. */
  protected readonly adjustLabel = this.translate('image.adjust');
  /** Localized label of the delete button. */
  protected readonly removeLabel = this.translate('image.remove');

  /** View pipeline state. */
  private readonly status = signal<'empty' | 'pending' | 'ready' | 'error'>('empty');
  /** Object URL of the fetched rendition currently on screen. */
  protected readonly displayUrl = signal<string | null>(null);
  private readonly visible = signal(false);
  private loadToken = 0;
  private destroyed = false;

  /** Local edit overrides. */
  protected readonly fitting = signal<FittingSession | null>(null);
  /** The uncommitted local pick/re-fit shown instead of the stored image. */
  protected readonly preview = signal<LocalPreview | null>(null);
  private readonly deleted = signal(false);
  private lastCommittedFit: TmImageFit | null = null;

  private readonly noticeKey = signal<{ key: string; params?: Record<string, unknown> } | null>(
    null,
  );
  /** Localized rejection/processing notice under the box (`null` = none). */
  protected readonly noticeText = computed(() => {
    const notice = this.noticeKey();
    return notice === null ? null : this.translate(notice.key, notice.params)();
  });

  /** Which of the four mutually exclusive box renderings is active. */
  protected readonly displayState = computed<DisplayState>(() => {
    if (this.preview() !== null) {
      return 'preview';
    }
    if (this.deleted()) {
      return 'placeholder';
    }
    const status = this.status();
    if (status === 'ready') {
      return 'image';
    }
    return status === 'error' ? 'error' : 'placeholder';
  });

  /** Whether an image (stored or local preview) is showing — gates delete. */
  protected readonly hasImage = computed(
    () => this.preview() !== null || (!this.deleted() && this.status() === 'ready'),
  );
  /** Whether re-fit is possible: a local original, or a stored `editSrc`. */
  protected readonly canRefit = computed(
    () =>
      this.preview() !== null ||
      (this.editSrc() !== undefined && !this.deleted() && this.status() === 'ready'),
  );

  /** The local preview rendered cropped per its fit — pure CSS transform. */
  protected readonly previewWidth = computed(() => {
    const preview = this.preview();
    return preview === null ? 0 : preview.width * this.previewScale(preview);
  });
  /** Pan of the preview `img` so the fit rect's origin lands on the box. */
  protected readonly previewTransform = computed(() => {
    const preview = this.preview();
    if (preview === null) {
      return '';
    }
    const scale = this.previewScale(preview);
    return `translate(${-preview.fit.rect.x * preview.width * scale}px, ${
      -preview.fit.rect.y * preview.height * scale
    }px)`;
  });

  constructor() {
    const destroyRef = inject(DestroyRef);
    let observer: IntersectionObserver | null = null;
    destroyRef.onDestroy(() => {
      // Invalidate in-flight loads FIRST: a fetch/decode resolving after
      // this point must revoke its own object URL (the token check in
      // swapIn), not adopt it — otherwise the URL and the blob it pins
      // leak for the life of the tab.
      this.loadToken += 1;
      this.destroyed = true;
      observer?.disconnect();
      this.setDisplayUrl(null);
      this.discardPreview();
      this.discardFitting();
    });

    afterNextRender(() => {
      if (!untracked(this.defer) || typeof IntersectionObserver === 'undefined') {
        this.visible.set(true);
        return;
      }
      observer = new IntersectionObserver(
        (entries) => {
          if (entries.some((entry) => entry.isIntersecting)) {
            this.visible.set(true);
            observer?.disconnect();
            observer = null;
          }
        },
        { rootMargin: '200px' },
      );
      observer.observe(this.host.nativeElement);
    });

    // A record-identity change discards local edit overrides.
    effect(() => {
      this.src();
      this.etag();
      untracked(() => {
        this.discardPreview();
        this.deleted.set(false);
      });
    });

    // The view pipeline: (src, etag, visibility) → cache → decode → swap.
    effect(() => {
      const src = this.src();
      const etag = this.etag();
      if (!this.visible()) {
        return;
      }
      const token = ++this.loadToken;
      untracked(() => {
        if (src === '') {
          this.status.set('empty');
          this.setDisplayUrl(null);
          return;
        }
        if (this.status() !== 'ready') {
          this.status.set('pending');
        }
        void this.load(src, etag, token);
      });
    });
  }

  // ---- view pipeline ----

  private async load(src: string, etag: string | null, token: number): Promise<void> {
    try {
      const bucket = tmSizeBucket(
        untracked(this.width),
        untracked(this.height),
        globalThis.devicePixelRatio ?? 1,
      );
      const url = untracked(this.srcForSize)(src, bucket);
      const blob = await this.cache.getImage(url, etag, (refreshed) => {
        void this.swapIn(refreshed, token);
      });
      await this.swapIn(blob, token);
    } catch {
      if (token === this.loadToken) {
        this.status.set('error');
        this.setDisplayUrl(null);
      }
    }
  }

  /** Decodes off-DOM, then swaps — no flash of a partially decoded image. */
  private async swapIn(blob: Blob, token: number): Promise<void> {
    const url = URL.createObjectURL(blob);
    try {
      const probe = new Image();
      probe.src = url;
      await probe.decode();
    } catch {
      URL.revokeObjectURL(url);
      if (token === this.loadToken) {
        this.status.set('error');
        this.setDisplayUrl(null);
      }
      return;
    }
    if (token !== this.loadToken) {
      URL.revokeObjectURL(url);
      return;
    }
    this.setDisplayUrl(url);
    this.status.set('ready');
  }

  /** Swaps the display URL, revoking the replaced object URL. */
  private setDisplayUrl(url: string | null): void {
    const previous = untracked(this.displayUrl);
    if (previous !== null && previous !== url) {
      URL.revokeObjectURL(previous);
    }
    this.displayUrl.set(url);
  }

  // ---- edit mode ----

  /** Runs a picked file through the guardrails and stages it as the preview. */
  protected async onFilePicked(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (file === undefined) {
      return;
    }
    const result = await tmProcessPickedFile(file, {
      maxBytes: untracked(this.maxFileBytes),
      maxEdgePx: untracked(this.maxEdgePx),
    });
    if (this.destroyed) {
      return; // the dialog resolved after the component was gone
    }
    if (result.kind === 'tooLarge') {
      this.noticeKey.set({
        key: 'image.tooLarge',
        params: { maxMb: Math.round(untracked(this.maxFileBytes) / (1024 * 1024)) },
      });
      return;
    }
    if (result.kind === 'undecodable') {
      this.noticeKey.set({ key: 'image.unsupported' });
      return;
    }
    this.noticeKey.set(null);
    this.discardFitting();
    const url = URL.createObjectURL(result.blob);
    this.fitting.set({
      url,
      width: result.width,
      height: result.height,
      pickedBlob: result.blob,
      initialFit: null,
    });
    // A pick with zero adjustments is still one committed edit: the file
    // plus the centered cover fit.
    this.lastCommittedFit = tmCoverFit(
      result.width,
      result.height,
      untracked(this.width),
      untracked(this.height),
    );
    this.imageChange.emit({ blob: result.blob, fit: this.lastCommittedFit });
  }

  /** Re-fit: a held pick re-opens losslessly; else the stored original. */
  protected async onRefit(): Promise<void> {
    const preview = untracked(this.preview);
    if (preview !== null) {
      this.preview.set(null);
      this.fitting.set({
        url: preview.url,
        width: preview.width,
        height: preview.height,
        pickedBlob: preview.blob,
        initialFit: preview.fit,
      });
      return;
    }
    const editSrc = untracked(this.editSrc);
    if (editSrc === undefined) {
      return;
    }
    let url: string | null = null;
    try {
      const blob = await this.cache.getImage(editSrc, untracked(this.etag));
      if (this.destroyed) {
        return;
      }
      url = URL.createObjectURL(blob);
      const probe = new Image();
      probe.src = url;
      await probe.decode();
      if (this.destroyed) {
        return;
      }
      this.fitting.set({
        url,
        width: probe.naturalWidth,
        height: probe.naturalHeight,
        pickedBlob: null,
        initialFit: null,
      });
      url = null; // ownership transferred to the fitting session
    } catch {
      this.noticeKey.set({ key: 'image.error' });
    } finally {
      if (url !== null) {
        URL.revokeObjectURL(url); // failure/destroy exits own the revoke
      }
    }
  }

  /** Emits one edit for a fit the pane committed (debounced upstream). */
  protected onFitCommitted(fit: TmImageFit): void {
    this.lastCommittedFit = fit;
    const session = untracked(this.fitting);
    this.imageChange.emit({ blob: session?.pickedBlob ?? null, fit });
  }

  /** Closes the fit pane, keeping a picked file as the local preview. */
  protected onFitDone(): void {
    const session = untracked(this.fitting);
    if (session === null) {
      return;
    }
    this.fitting.set(null);
    if (session.pickedBlob !== null) {
      const fit =
        this.lastCommittedFit ??
        tmCoverFit(session.width, session.height, untracked(this.width), untracked(this.height));
      this.discardPreview();
      this.preview.set({
        url: session.url,
        blob: session.pickedBlob,
        width: session.width,
        height: session.height,
        fit,
      });
      this.deleted.set(false);
    } else {
      // A re-fit: the display keeps the server rendition until the backend
      // processes the new fit and the etag input moves.
      URL.revokeObjectURL(session.url);
    }
    void this.restoreFocusToChrome();
  }

  /** Discards any local state and emits `null` (the user deleted the image). */
  protected onDelete(): void {
    this.discardPreview();
    this.deleted.set(true);
    this.noticeKey.set(null);
    this.imageChange.emit(null);
  }

  // ---- internals ----

  private previewScale(preview: LocalPreview): number {
    return untracked(this.width) / (preview.fit.rect.width * preview.width);
  }

  private discardPreview(): void {
    const preview = untracked(this.preview);
    if (preview !== null) {
      URL.revokeObjectURL(preview.url);
      this.preview.set(null);
    }
  }

  private discardFitting(): void {
    const session = untracked(this.fitting);
    if (session !== null) {
      URL.revokeObjectURL(session.url);
      this.fitting.set(null);
    }
  }

  /** Leaving the fitting state moves focus back onto the chrome. */
  private async restoreFocusToChrome(): Promise<void> {
    await new Promise((resolve) => afterNextRender(() => resolve(undefined), {
      injector: this.injector,
    }));
    this.host.nativeElement
      .querySelector<HTMLElement>('[data-tm-image-action="replace"]')
      ?.focus();
  }
}

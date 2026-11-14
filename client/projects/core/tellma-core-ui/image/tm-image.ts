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
import { Camera, Crop, Image as ImageGlyph, ImageOff, Trash2 } from 'lucide';

import { TM_UI_TRANSLATE } from '@tellma/core-ui';
import { TmIcon } from '@tellma/core-ui/icon';
import { TmTooltip } from '@tellma/core-ui/tooltip';

import { ɵTmImageBlobCache } from './internal/blob-cache';
import { tmMaxMegabytes } from './internal/byte-size';
import { tmCoverFit } from './internal/fit';
import { tmDefaultSrcForSize, tmSizeBucket } from './internal/size-buckets';
import { tmProcessPickedFile } from './internal/image-processing';
import { ɵTmImageEditPane } from './internal/tm-image-edit-pane';
import type { TmImageEdit, TmImageFit } from './tm-image-types';

/** The formats the replace dialog accepts. */
const ACCEPT = 'image/png,image/jpeg,image/webp,image/gif,image/avif';

/** The box's glyphs, drawn through tm-icon; none is directional. */
const GLYPHS = {
  /** A failed load. */
  error: ImageOff,
  /** No image and no placeholder template. */
  empty: ImageGlyph,
  /** The edit chrome's pick action. */
  replace: Camera,
  /** The edit chrome's re-fit action. */
  adjust: Crop,
  /** The edit chrome's delete action. */
  remove: Trash2,
} as const;

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
 * The five mutually exclusive box renderings. `fallback` is a failed load
 * rendered through the placeholder template (`placeholderOnError`). Named so
 * the emitted declaration carries the alias — a literal union's print order
 * follows the compiler's type interning and churns the API golden.
 */
type DisplayState = 'preview' | 'image' | 'error' | 'fallback' | 'placeholder';

/** The view load of one record identity: in flight, or on screen. */
interface ActiveLoad {
  /** The `src` it loads. */
  readonly src: string;
  /** The `etag` it loads. */
  readonly etag: string | null;
  /** The instance's wait: its hold on the shared request and revalidation. */
  readonly controller: AbortController;
  /** Whether the bytes are still to arrive (leaving the viewport aborts until then). */
  awaitingBytes: boolean;
}

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
 * shift; the placeholder template shows until then. A wait the box
 * abandons (destroyed, a new `src` or `etag`, or scrolled away before the
 * bytes arrive) is aborted: the request stops once no other instance waits
 * on it, and nothing is cached or shown as an error. Edit mode adds
 * replace / re-fit / delete affordances inside the same fixed box and
 * emits {@link TmImageEdit} on every committed adjustment — the consumer
 * sends it to the server, which crops from the original it holds.
 *
 * Cropping is presentation, not redaction: a re-fit never discards stored
 * pixels — users who cropped something out must delete or replace.
 *
 * @tmGroup media
 * @tmA11yNotes `alt` is required (`''` only for decorative images); the
 *   edit affordances are icon-only buttons, each named by the label it
 *   also shows as a tooltip; the error state carries its own accessible
 *   label AND announces; the crop surface pans with arrow keys and zooms
 *   with `+`/`-` and the always-visible slider; rejections and load
 *   failures announce via a `role="status"` region.
 */
@Component({
  selector: 'tm-image',
  imports: [NgTemplateOutlet, TmIcon, TmTooltip, ɵTmImageEditPane],
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
        @case ('fallback') {
          <!-- A failed load shown as the placeholder (an avatar's initials):
               the box still presents the image, named by its alt text. -->
          <div
            class="tm-image__fallback"
            [attr.role]="alt() === '' ? null : 'img'"
            [attr.aria-label]="alt() === '' ? null : alt()"
            [attr.aria-hidden]="alt() === '' ? 'true' : null"
          >
            <ng-container [ngTemplateOutlet]="placeholder()!.template" />
          </div>
        }
        @case ('error') {
          <div
            class="tm-image__glyph tm-image__glyph--error"
            role="img"
            tabindex="0"
            [attr.aria-label]="errorLabel()"
            [tmTooltip]="errorLabel()"
          >
            <tm-icon [svg]="glyphs.error" />
          </div>
        }
        @default {
          @if (placeholder(); as marker) {
            <ng-container [ngTemplateOutlet]="marker.template" />
          } @else {
            <div class="tm-image__glyph" aria-hidden="true">
              <tm-icon [svg]="glyphs.empty" />
            </div>
          }
        }
      }
      @if (mode() === 'edit') {
        <!-- Icon-only actions tucked into one corner: the chrome sits ON
             the image, so anything wider hides the thing being edited.
             Each carries its label as the accessible name and as a
             tooltip, so the wording is still reachable. -->
        <div class="tm-image__chrome">
          <button
            type="button"
            class="tm-image__chrome-button"
            data-tm-image-action="replace"
            [attr.aria-label]="replaceLabel()"
            [tmTooltip]="replaceLabel()"
            (click)="pickerInput.click()"
          >
            <tm-icon [svg]="glyphs.replace" />
          </button>
          @if (canRefit()) {
            <button
              type="button"
              class="tm-image__chrome-button"
              data-tm-image-action="adjust"
              [attr.aria-label]="adjustLabel()"
              [tmTooltip]="adjustLabel()"
              (click)="onRefit()"
            >
              <tm-icon [svg]="glyphs.adjust" />
            </button>
          }
          @if (hasImage()) {
            <button
              type="button"
              class="tm-image__chrome-button"
              data-tm-image-action="remove"
              [attr.aria-label]="removeLabel()"
              [tmTooltip]="removeLabel()"
              (click)="onDelete()"
            >
              <tm-icon [svg]="glyphs.remove" />
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
  /**
   * Defer the fetch until near-viewport (IntersectionObserver). A fetch
   * whose box leaves the viewport before the bytes arrive is aborted, and
   * starts again when the box returns.
   */
  readonly defer = input(true, { transform: booleanAttribute });
  /**
   * URL of the stored ORIGINAL — enables re-fitting an existing image;
   * absent, an existing image can only be replaced or deleted.
   */
  readonly editSrc = input<string | undefined>(undefined);
  /** `view` renders; `edit` adds replace / re-fit / delete. */
  readonly mode = input<'view' | 'edit'>('view');
  /**
   * Render a failed load through the `tmImagePlaceholder` template instead
   * of the broken-image glyph — an avatar falls back to its initials. The
   * box then presents the image as before, named by `alt`, and the failure
   * shows no notice. Without a placeholder template the glyph shows as
   * usual. Default `false`.
   */
  readonly placeholderOnError = input(false, { transform: booleanAttribute });
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
  /** The box's glyph data. */
  protected readonly glyphs = GLYPHS;
  /** Localized label of the load-error glyph. */
  protected readonly errorLabel = this.translate('image.error');
  private readonly replaceExistingLabel = this.translate('image.replace');
  private readonly addLabel = this.translate('image.add');
  /**
   * Localized label of the pick button: there is nothing to "replace"
   * until the box holds something, so an empty one offers to ADD.
   */
  protected readonly replaceLabel = computed(() =>
    this.hasImage() ? this.replaceExistingLabel() : this.addLabel(),
  );
  /** Localized label of the re-fit button. */
  protected readonly adjustLabel = this.translate('image.adjust');
  /** Localized label of the delete button. */
  protected readonly removeLabel = this.translate('image.remove');

  /** View pipeline state. */
  private readonly status = signal<'empty' | 'pending' | 'ready' | 'error'>('empty');
  /** Object URL of the fetched rendition currently on screen. */
  protected readonly displayUrl = signal<string | null>(null);
  /** Whether the box is near the viewport (always, without `defer`). */
  private readonly visible = signal(false);
  /** Bumped by every new or abandoned load, so a stale one swaps nothing in. */
  private loadToken = 0;
  /** The current record identity's view load; `null` before the first and after an abort. */
  private activeLoad: ActiveLoad | null = null;
  /** Aborted on destroy: the re-fit's fetch of the original waits on it. */
  private readonly lifetime = new AbortController();
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

  /** Whether a failed load renders the placeholder template (and stays silent). */
  private readonly fallsBackOnError = computed(
    () => this.placeholderOnError() && this.placeholder() !== undefined,
  );

  /** Which of the mutually exclusive box renderings is active. */
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
    if (status !== 'error') {
      return 'placeholder';
    }
    return this.fallsBackOnError() ? 'fallback' : 'error';
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
      // leak for the life of the tab. The abort releases this instance's
      // hold on the shared request and on any background revalidation.
      this.abortLoad();
      this.lifetime.abort();
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
      // Observed for the component's life, not just until the first sight:
      // a box that leaves the viewport before its bytes arrive aborts its
      // wait, and resumes when it comes back.
      observer = new IntersectionObserver(
        (entries) => {
          // Entries for one target arrive in order; the last is current.
          this.visible.set(entries[entries.length - 1].isIntersecting);
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
        // The fit is normalized against THIS record's image — a different
        // record must never re-open the previous one's crop.
        this.lastCommittedFit = null;
      });
    });

    // The view pipeline: (src, etag, visibility) → cache → decode → swap.
    effect(() => {
      const src = this.src();
      const etag = this.etag();
      const visible = this.visible();
      untracked(() => {
        const active = this.activeLoad;
        if (active !== null && (active.src !== src || active.etag !== etag)) {
          // A new record identity abandons the old wait; what is on screen
          // stays until the new bytes swap in.
          this.abortLoad();
        }
        if (src === '') {
          this.status.set('empty');
          this.setDisplayUrl(null);
          this.clearLoadError();
          return;
        }
        if (!visible) {
          // Left the defer viewport before the bytes arrived: abort the
          // wait; the load starts again when the box comes back.
          if (this.activeLoad?.awaitingBytes === true) {
            this.abortLoad();
          }
          return;
        }
        if (this.activeLoad !== null) {
          return; // this identity is already loading, or loaded
        }
        if (this.status() !== 'ready') {
          this.status.set('pending');
        }
        this.startLoad(src, etag);
      });
    });
  }

  // ---- view pipeline ----

  /** Starts the view load of one record identity. */
  private startLoad(src: string, etag: string | null): void {
    const token = ++this.loadToken;
    const load: ActiveLoad = { src, etag, controller: new AbortController(), awaitingBytes: true };
    this.activeLoad = load;
    void this.load(load, token);
  }

  /**
   * Abandons the current view load: its request is aborted once no other
   * instance waits on it, its background revalidation once no instance
   * shows the URL, and a decode still running swaps nothing in.
   */
  private abortLoad(): void {
    const active = this.activeLoad;
    if (active !== null) {
      this.activeLoad = null;
      this.loadToken += 1;
      active.controller.abort();
    }
  }

  private async load(load: ActiveLoad, token: number): Promise<void> {
    try {
      const bucket = tmSizeBucket(
        untracked(this.width),
        untracked(this.height),
        globalThis.devicePixelRatio ?? 1,
      );
      const url = untracked(this.srcForSize)(load.src, bucket);
      const blob = await this.cache.getImage(url, load.etag, {
        signal: load.controller.signal,
        onRefresh: (refreshed) => {
          void this.swapIn(refreshed, token);
        },
      });
      load.awaitingBytes = false;
      await this.swapIn(blob, token);
    } catch {
      // An abandoned load is not a failure: no glyph, no announcement.
      if (!load.controller.signal.aborted) {
        // The wait is over: a failed box stays failed when it scrolls away
        // and back, rather than refetching on every pass.
        load.awaitingBytes = false;
        this.failLoad(token);
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
      this.failLoad(token);
      return;
    }
    if (token !== this.loadToken) {
      URL.revokeObjectURL(url);
      return;
    }
    this.setDisplayUrl(url);
    this.status.set('ready');
    this.clearLoadError();
  }

  /**
   * Fails the pipeline (stale tokens no-op). The error glyph is silent to
   * a screen reader until it is focused, so the failure also goes through
   * the status region — the channel pick rejections already announce on.
   * A failure that falls back to the placeholder stays silent: the box
   * still presents the image by its alt text, as an avatar's initials do.
   */
  private failLoad(token: number): void {
    if (token !== this.loadToken) {
      return;
    }
    this.status.set('error');
    this.setDisplayUrl(null);
    if (!untracked(this.fallsBackOnError)) {
      this.noticeKey.set({ key: 'image.error' });
    }
  }

  /**
   * Retracts a load failure once one succeeds — matched on the key so a
   * pending pick rejection (a different message) is never wiped by a
   * background revalidation landing behind it.
   */
  private clearLoadError(): void {
    if (untracked(this.noticeKey)?.key === 'image.error') {
      this.noticeKey.set(null);
    }
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
        params: { maxMb: tmMaxMegabytes(untracked(this.maxFileBytes)) },
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
      // Destroying the component aborts this fetch too.
      const blob = await this.cache.getImage(editSrc, untracked(this.etag), {
        signal: this.lifetime.signal,
      });
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
        // Re-opening restores the fit committed against this same
        // original; only a record change (which clears it) starts at cover.
        initialFit: this.lastCommittedFit,
      });
      url = null; // ownership transferred to the fitting session
    } catch {
      // A fetch aborted by the destroy is no failure to announce.
      if (!this.destroyed) {
        this.noticeKey.set({ key: 'image.error' });
      }
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

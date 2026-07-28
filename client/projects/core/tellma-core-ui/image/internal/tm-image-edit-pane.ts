// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  Component,
  computed,
  DestroyRef,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';

import { TM_UI_TRANSLATE } from '@tellma/core-ui';

import type { TmImageFit } from '../tm-image-types';

/** Zoom bounds relative to the cover fit (1 = the whole cover rect). */
const MAX_ZOOM = 8;
/** Debounce for continuous adjustments (wheel, keys) before a commit. */
const COMMIT_DEBOUNCE_MS = 300;
/** Arrow-key pan step, as a fraction of the current crop rect. */
const KEY_PAN_FRACTION = 0.05;

/** Clamps a rect origin into `[0, max]` (`max` may be 0 for exact fits). */
function clampTo(value: number, max: number): number {
  return Math.min(Math.max(value, 0), Math.max(0, max));
}

/**
 * The fit surface of `tm-image` edit mode: the image being fitted renders
 * under a fixed viewport of the box's aspect; the user pans (pointer/touch
 * drag, arrow keys) and zooms (wheel, pinch, and the always-visible zoom
 * slider — the accessible path). The fit state is the normalized
 * `TmImageFit` rect, clamped so the rect never leaves the image; every
 * committed adjustment emits it.
 *
 * @internal Instantiated by `TmImage` edit mode; never use directly.
 */
@Component({
  selector: 'tm-image-edit-pane',
  template: `
    <div
      class="tm-image__crop"
      role="group"
      tabindex="0"
      [attr.aria-label]="cropLabel()"
      (pointerdown)="onPointerDown($event)"
      (pointermove)="onPointerMove($event)"
      (pointerup)="onPointerEnd($event)"
      (pointercancel)="onPointerEnd($event)"
      (wheel)="onWheel($event)"
      (keydown)="onKeydown($event)"
    >
      <img
        class="tm-image__crop-img"
        [src]="imageUrl()"
        alt=""
        draggable="false"
        [style.width.px]="displayWidth()"
        [style.transform]="imageTransform()"
      />
    </div>
    <div class="tm-image__edit-bar">
      <input
        class="tm-image__zoom"
        type="range"
        min="1"
        [max]="maxZoom"
        step="0.01"
        [value]="zoom()"
        [attr.aria-label]="zoomLabel()"
        (input)="onSlider($event)"
        (change)="commit()"
      />
      <button type="button" class="tm-image__chrome-button" (click)="done.emit()">
        {{ doneLabel() }}
      </button>
    </div>
  `,
  styleUrl: './tm-image-edit-pane.css',
  host: { class: 'tm-image__edit-pane' },
})
export class ɵTmImageEditPane {
  private readonly translate = inject(TM_UI_TRANSLATE);

  /** Object URL of the image being fitted (picked file or fetched original). */
  readonly imageUrl = input.required<string>();
  /** Pixel dimensions of the image being fitted. */
  readonly naturalWidth = input.required<number>();
  readonly naturalHeight = input.required<number>();
  /** The fixed viewport (the component box). */
  readonly boxWidth = input.required<number>();
  readonly boxHeight = input.required<number>();
  /** Restores a previous fit (lossless re-edit); null starts at cover. */
  readonly initialFit = input<TmImageFit | null>(null);

  /** Emits the normalized fit on every committed adjustment. */
  readonly fitCommitted = output<TmImageFit>();
  /** The Done affordance — the host leaves the fitting state. */
  readonly done = output<void>();

  protected readonly maxZoom = MAX_ZOOM;
  protected readonly cropLabel = this.translate('image.cropSurface');
  protected readonly zoomLabel = this.translate('image.zoom');
  protected readonly doneLabel = this.translate('image.done');

  /**
   * Interaction state, layered over the reactive defaults: `null` means
   * "not touched yet", which resolves to the restored `initialFit` (else
   * the centered cover). No initialization timing — the defaults are
   * computed from the inputs whenever they become available.
   */
  private readonly zoomOverride = signal<number | null>(null);
  private readonly rectXOverride = signal<number | null>(null);
  private readonly rectYOverride = signal<number | null>(null);

  /** The largest box-aspect rect that fits the image, in source px. */
  private readonly coverRect = computed(() => {
    const aspect = this.boxWidth() / this.boxHeight();
    const width = Math.min(this.naturalWidth(), this.naturalHeight() * aspect);
    return { width, height: width / aspect };
  });

  /** Zoom relative to cover (1 = largest box-aspect rect inside the image). */
  protected readonly zoom = computed(() => {
    const override = this.zoomOverride();
    if (override !== null) {
      return override;
    }
    const fit = this.initialFit();
    if (fit === null) {
      return 1;
    }
    const width = fit.rect.width * this.naturalWidth();
    return Math.min(MAX_ZOOM, Math.max(1, this.coverRect().width / width));
  });

  private readonly rectWidth = computed(() => this.coverRect().width / this.zoom());
  private readonly rectHeight = computed(() => this.coverRect().height / this.zoom());

  /** Crop-rect origin in SOURCE pixels (clamped inside the image). */
  private readonly rectX = computed(() => {
    const raw =
      this.rectXOverride() ??
      (this.initialFit() !== null
        ? this.initialFit()!.rect.x * this.naturalWidth()
        : (this.naturalWidth() - this.rectWidth()) / 2);
    return clampTo(raw, this.naturalWidth() - this.rectWidth());
  });
  private readonly rectY = computed(() => {
    const raw =
      this.rectYOverride() ??
      (this.initialFit() !== null
        ? this.initialFit()!.rect.y * this.naturalHeight()
        : (this.naturalHeight() - this.rectHeight()) / 2);
    return clampTo(raw, this.naturalHeight() - this.rectHeight());
  });

  /** CSS px per source px at the current zoom. */
  private readonly displayScale = computed(() => this.boxWidth() / this.rectWidth());

  protected readonly displayWidth = computed(() => this.naturalWidth() * this.displayScale());
  protected readonly imageTransform = computed(() => {
    const scale = this.displayScale();
    return `translate(${-this.rectX() * scale}px, ${-this.rectY() * scale}px)`;
  });

  private readonly pointers = new Map<number, { x: number; y: number }>();
  private pinchDistance: number | null = null;
  private dragging = false;
  private commitTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.commitTimer));
  }

  /** The current fit, normalized against the source image. */
  currentFit(): TmImageFit {
    const naturalWidth = untracked(this.naturalWidth);
    const naturalHeight = untracked(this.naturalHeight);
    const x = untracked(this.rectX);
    const y = untracked(this.rectY);
    const width = untracked(this.rectWidth);
    const height = untracked(this.rectHeight);
    return {
      rect: {
        x: x / naturalWidth,
        y: y / naturalHeight,
        width: width / naturalWidth,
        height: height / naturalHeight,
      },
      focal: { x: (x + width / 2) / naturalWidth, y: (y + height / 2) / naturalHeight },
    };
  }

  // ---- interactions ----

  protected onPointerDown(event: PointerEvent): void {
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
    this.pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
    if (this.pointers.size === 2) {
      this.pinchDistance = this.currentPinchDistance();
      this.dragging = false;
    } else if (this.pointers.size === 1) {
      this.dragging = true;
    }
  }

  protected onPointerMove(event: PointerEvent): void {
    const previous = this.pointers.get(event.pointerId);
    if (previous === undefined) {
      return;
    }
    this.pointers.set(event.pointerId, { x: event.clientX, y: event.clientY });
    if (this.pointers.size === 2) {
      const distance = this.currentPinchDistance();
      if (this.pinchDistance !== null && this.pinchDistance > 0 && distance > 0) {
        this.zoomBy(distance / this.pinchDistance);
      }
      this.pinchDistance = distance;
      return;
    }
    if (this.dragging) {
      const scale = untracked(this.displayScale);
      this.panBy(
        -(event.clientX - previous.x) / scale,
        -(event.clientY - previous.y) / scale,
      );
    }
  }

  protected onPointerEnd(event: PointerEvent): void {
    const wasInteracting = this.pointers.delete(event.pointerId);
    if (this.pointers.size < 2) {
      this.pinchDistance = null;
    }
    if (this.pointers.size === 0 && wasInteracting) {
      this.dragging = false;
      this.commit();
    }
  }

  protected onWheel(event: WheelEvent): void {
    event.preventDefault();
    this.zoomBy(event.deltaY < 0 ? 1.1 : 1 / 1.1);
    this.scheduleCommit();
  }

  protected onKeydown(event: KeyboardEvent): void {
    const stepX = untracked(this.rectWidth) * KEY_PAN_FRACTION;
    const stepY = untracked(this.rectHeight) * KEY_PAN_FRACTION;
    switch (event.key) {
      case 'ArrowLeft':
        this.panBy(-stepX, 0);
        break;
      case 'ArrowRight':
        this.panBy(stepX, 0);
        break;
      case 'ArrowUp':
        this.panBy(0, -stepY);
        break;
      case 'ArrowDown':
        this.panBy(0, stepY);
        break;
      case '+':
      case '=':
        this.zoomBy(1.1);
        break;
      case '-':
        this.zoomBy(1 / 1.1);
        break;
      default:
        return;
    }
    event.preventDefault();
    this.scheduleCommit();
  }

  protected onSlider(event: Event): void {
    const value = Number.parseFloat((event.target as HTMLInputElement).value);
    if (Number.isFinite(value)) {
      this.setZoom(value);
    }
  }

  /** Emits the current fit as a committed adjustment. */
  protected commit(): void {
    clearTimeout(this.commitTimer);
    this.commitTimer = undefined;
    this.fitCommitted.emit(this.currentFit());
  }

  // ---- internals ----

  private scheduleCommit(): void {
    clearTimeout(this.commitTimer);
    this.commitTimer = setTimeout(() => this.commit(), COMMIT_DEBOUNCE_MS);
  }

  /** Pans by source-px deltas; the rect computeds clamp the result. */
  private panBy(deltaX: number, deltaY: number): void {
    this.rectXOverride.set(untracked(this.rectX) + deltaX);
    this.rectYOverride.set(untracked(this.rectY) + deltaY);
  }

  private zoomBy(factor: number): void {
    this.setZoom(untracked(this.zoom) * factor);
  }

  /** Zooms about the rect center; the rect computeds clamp the result. */
  private setZoom(zoom: number): void {
    const clamped = Math.min(this.maxZoom, Math.max(1, zoom));
    const centerX = untracked(this.rectX) + untracked(this.rectWidth) / 2;
    const centerY = untracked(this.rectY) + untracked(this.rectHeight) / 2;
    this.zoomOverride.set(clamped);
    this.rectXOverride.set(centerX - untracked(this.rectWidth) / 2);
    this.rectYOverride.set(centerY - untracked(this.rectHeight) / 2);
  }

  private currentPinchDistance(): number {
    const [first, second] = [...this.pointers.values()];
    return Math.hypot(second.x - first.x, second.y - first.y);
  }
}

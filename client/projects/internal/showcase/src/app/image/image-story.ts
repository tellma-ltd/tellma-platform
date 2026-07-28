// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, inject, signal } from '@angular/core';

import { TmClientCache } from '@tellma/core-ui';
import { TmButton } from '@tellma/core-ui/button';
import { TmImage, TmImagePlaceholder, type TmImageEdit } from '@tellma/core-ui/image';

/**
 * tm-image demo host — three same-src instances (the cache coalesces to
 * one request), etag flip + clearAll refetch hooks for the network
 * counting battery, the error glyph, custom placeholder, and edit mode
 * with an emission readout. Drives the Playwright battery.
 */
@Component({
  imports: [TmButton, TmImage, TmImagePlaceholder],
  template: `
    <h2>Image</h2>

    <section>
      <h3>View (three instances, one request)</h3>
      <div class="row">
        <tm-image
          data-testid="view-1"
          [src]="src"
          [etag]="etag()"
          alt="Test pattern"
          [width]="96"
          [height]="96"
          [defer]="false"
        />
        <tm-image
          data-testid="view-2"
          [src]="src"
          [etag]="etag()"
          alt="Test pattern"
          [width]="96"
          [height]="96"
          [defer]="false"
        />
        <tm-image
          data-testid="view-circle"
          [src]="src"
          [etag]="etag()"
          alt="Test pattern"
          shape="circle"
          [width]="96"
          [height]="96"
          [defer]="false"
        />
      </div>
      <div class="row">
        <button tmButton variant="secondary" data-testid="flip-etag" (click)="flipEtag()">
          Flip etag
        </button>
        <button tmButton variant="secondary" data-testid="clear-caches" (click)="clearCaches()">
          Clear caches
        </button>
        <output data-testid="etag-value">{{ etag() }}</output>
      </div>
    </section>

    <section>
      <h3>Error + placeholder</h3>
      <div class="row">
        <tm-image
          data-testid="error-image"
          src="/missing-image.png"
          alt="Broken"
          [width]="96"
          [height]="96"
          [defer]="false"
        />
        <tm-image data-testid="empty-image" src="" alt="" [width]="96" [height]="96" [defer]="false">
          <ng-template tmImagePlaceholder>
            <span class="initials" data-testid="custom-placeholder">LH</span>
          </ng-template>
        </tm-image>
      </div>
    </section>

    <section>
      <h3>Deferred (loads near-viewport)</h3>
      <div class="spacer" aria-hidden="true"></div>
      <tm-image
        data-testid="deferred-image"
        src="/test-image.png?v=deferred"
        etag="v1"
        alt="Deferred test pattern"
        [width]="96"
        [height]="96"
      />
    </section>

    <section>
      <h3>Edit</h3>
      <tm-image
        data-testid="edit-image"
        mode="edit"
        [src]="src"
        [editSrc]="src"
        [etag]="etag()"
        alt="Test pattern"
        [width]="160"
        [height]="160"
        [defer]="false"
        (imageChange)="onImageChange($event)"
      />
      <output data-testid="image-change">{{ lastChange() }}</output>
    </section>
  `,
  styles: `
    section {
      margin-block-end: 24px;
    }
    .row {
      display: flex;
      align-items: center;
      gap: 12px;
      margin-block-end: 12px;
    }
    .initials {
      display: flex;
      align-items: center;
      justify-content: center;
      inline-size: 100%;
      block-size: 100%;
      font-weight: 600;
    }
    /* Pushes the deferred instance well past the viewport + IO margin. */
    .spacer {
      block-size: 1600px;
    }
  `,
})
export class ImageStory {
  private readonly clientCache = inject(TmClientCache);

  readonly src = '/test-image.png';
  readonly etag = signal('v1');
  readonly lastChange = signal('none');

  protected flipEtag(): void {
    this.etag.update((current) => (current === 'v1' ? 'v2' : 'v1'));
  }

  protected clearCaches(): void {
    void this.clientCache.clearAll();
  }

  protected onImageChange(edit: TmImageEdit | null): void {
    if (edit === null) {
      this.lastChange.set('deleted');
      return;
    }
    const rect = edit.fit.rect;
    this.lastChange.set(
      JSON.stringify({
        blob: edit.blob === null ? null : edit.blob.size > 0,
        rect: {
          x: Math.round(rect.x * 1000) / 1000,
          y: Math.round(rect.y * 1000) / 1000,
          width: Math.round(rect.width * 1000) / 1000,
          height: Math.round(rect.height * 1000) / 1000,
        },
      }),
    );
  }
}

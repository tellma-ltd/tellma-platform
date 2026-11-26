// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgComponentOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal, type Type } from '@angular/core';
import { TmLocalization, TmTelemetry } from '@tellma/core';
import { TM_MODAL_DATA } from '@tellma/core-ui/modal';
import { TmSkeleton } from '@tellma/core-ui/skeleton';

// The content of a modal the shell opens before the code it shows has loaded
// (spec 0027 §7.2, §11): the About and Shortcuts dialogs and a slot's drawer
// open in the click's frame, with this placeholder in the final geometry,
// and the content replaces it as soon as its chunk lands. A chunk that fails
// to load says so in place of the placeholder.

/** What a lazily loaded modal shows. */
export interface TmShellLazyContentData {
  /** Loads the component the modal shows. */
  readonly load: () => Promise<Type<unknown>>;
  /** The component's inputs. */
  readonly inputs?: Readonly<Record<string, unknown>>;
  /** The placeholder's height, the content's own, so nothing moves when it arrives. */
  readonly height: string;
}

/** A modal's content that loads on open: a placeholder in the content's geometry, then the content. */
@Component({
  selector: 'tm-shell-lazy-content',
  imports: [NgComponentOutlet, TmSkeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (component(); as component) {
      <ng-container [ngComponentOutlet]="component" [ngComponentOutletInputs]="data.inputs ?? {}" />
    } @else if (failed()) {
      <!-- The chunk did not load. -->
      <p class="tm-shell-lazy-content__failed" role="alert">{{ failedLabel() }}</p>
    } @else {
      <!-- The placeholder, as tall as the content, announced as loading. -->
      <div class="tm-shell-lazy-content__placeholder" role="status" [style.block-size]="data.height" [attr.aria-label]="loadingLabel()">
        <tm-skeleton [lines]="4" />
      </div>
    }
  `,
  styles: `
    .tm-shell-lazy-content__placeholder {
      box-sizing: border-box;
    }

    .tm-shell-lazy-content__failed {
      margin: 0;
      color: var(--text-muted);
    }
  `,
})
export class TmShellLazyContent {
  readonly #l10n = inject(TmLocalization);
  readonly #component = signal<Type<unknown> | null>(null);
  readonly #failed = signal(false);

  /** What the modal shows. */
  protected readonly data = inject<TmShellLazyContentData>(TM_MODAL_DATA);

  /** The loaded component, or `null` while it loads. */
  protected readonly component = this.#component.asReadonly();

  /** Whether the component failed to load. */
  protected readonly failed = this.#failed.asReadonly();

  /** The placeholder's status. */
  protected readonly loadingLabel = this.#l10n.ɵtext('tm.shellOverlays.dialogs.loading');

  /** The failure's line. */
  protected readonly failedLabel = this.#l10n.ɵtext('tm.shellOverlays.dialogs.failed');

  /** Creates the content, which starts loading its component. */
  constructor() {
    const telemetry = inject(TmTelemetry);
    this.data.load().then(
      (component) => this.#component.set(component),
      (error: unknown) => {
        // Offline, or after a deploy removed the chunk.
        telemetry.error(error, { source: 'tm-shell-lazy-content' });
        this.#failed.set(true);
      },
    );
  }
}

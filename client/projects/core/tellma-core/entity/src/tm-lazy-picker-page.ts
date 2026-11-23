// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgComponentOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  InjectionToken,
  signal,
  type Type,
} from '@angular/core';
import { TmLocalization, TmTelemetry } from '@tellma/core';
import { TM_UI_TRANSLATE } from '@tellma/core-ui';
import { TmButton } from '@tellma/core-ui/button';
import { TmEmptyState } from '@tellma/core-ui/empty-state';
import type { TmEntityPickerPageData } from '@tellma/core-ui/entity-picker';
import { TM_MODAL_DATA, TmModalFooter, TmModalRef } from '@tellma/core-ui/modal';
import { TmSpinner } from '@tellma/core-ui/spinner';

import { ɵTM_PICKER_STRINGS_EN } from './tm-entity-strings';

// The page an entity picker opens in its modal (spec 0029 §14.4): one shim
// for every entity, which loads the search page's picker mode (spec 0030
// §17) or the details page's (spec 0031 §15) inside the modal and renders
// it there, the picker's data and the modal's ref reaching it through the
// modal's injector. Which page a kind loads is a seam: until the search and
// details pages land, it resolves to none and the shim says the page is not
// available.

/**
 * What an entity picker's page is opened for, in the page's
 * `TmEntityPickerPageData.context`: the entity it serves, the field's own
 * filter beside the entity's declared one, and the page's kind.
 */
export interface TmEntityPickerContext {
  /** The entity's name, such as `gl.Center`. */
  readonly entity: string;
  /** The field's Queryex filter, applied beside the entity's declared picker filter; `null` for none. */
  readonly filter: string | null;
  /** Advanced search, a new record, or the chosen record's details. */
  readonly kind: 'search' | 'create' | 'edit';
}

/**
 * Loads the component a picker page renders: the search page's picker mode
 * for `search`, the details page's for `details`; `null` while the page
 * family has none. Internal: the search and details pages provide it.
 */
export type ɵTmPickerPageLoader = (page: 'search' | 'details') => Promise<Type<unknown> | null>;

/**
 * The loader of the picker pages. Internal: its default resolves every page
 * to none, which the shim shows as not available.
 */
export const ɵTM_PICKER_PAGES = new InjectionToken<ɵTmPickerPageLoader>('TM_PICKER_PAGES', {
  providedIn: 'root',
  factory: () => () => Promise.resolve(null),
});

/**
 * Registers the pickers' English strings, once per app. Internal.
 *
 * @param localization - The app's localization.
 */
export function ɵtmRegisterPickerStrings(localization: TmLocalization): void {
  localization.ɵregisterStrings('picker', ɵTM_PICKER_STRINGS_EN);
}

/**
 * The page an entity picker opens in its modal for advanced search, a new
 * record or the chosen record's details. It loads the page its context
 * names and renders it in place; the page reads the picker's data and closes
 * the modal itself. While no page family provides the kind, it says the page
 * is not available; a page that fails to load offers a retry. Each page it
 * shows is reported once, with how the modal closed.
 *
 * @example
 * ```ts
 * const page: TmEntityPickerPage = {
 *   component: TmLazyPickerPage,
 *   size: 'lg',
 *   context: { entity: 'gl.Center', filter: null, kind: 'search' } satisfies TmEntityPickerContext,
 * };
 * ```
 */
@Component({
  selector: 'tm-lazy-picker-page',
  imports: [NgComponentOutlet, TmButton, TmEmptyState, TmModalFooter, TmSpinner],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'tm-lazy-picker-page',
    '[attr.aria-busy]': "state() === 'loading' ? 'true' : null",
  },
  styles: `
    :host {
      display: block;
    }
    .tm-lazy-picker-page__loading {
      display: flex;
      align-items: center;
      justify-content: center;
      min-block-size: 12rem;
    }
  `,
  template: `
    @switch (state()) {
      <!-- The loaded page, under the modal's injector -->
      @case ('ready') {
        <ng-container *ngComponentOutlet="page()" />
      }
      <!-- Loading: a spinner in the space the page will take -->
      @case ('loading') {
        <div class="tm-lazy-picker-page__loading"><tm-spinner /></div>
      }
      <!-- No page family provides the kind yet -->
      @case ('unavailable') {
        <tm-empty-state size="panel" live="polite" [heading]="unavailableText()" />
        <div tmModalFooter>
          <button tmButton type="button" variant="primary" (click)="close()">{{ closeText() }}</button>
        </div>
      }
      <!-- The page's code failed to load: a retry -->
      @case ('failed') {
        <tm-empty-state size="panel" tone="error" live="polite" [heading]="failedText()" />
        <div tmModalFooter>
          <button tmButton type="button" variant="ghost" (click)="close()">{{ closeText() }}</button>
          <button tmButton type="button" variant="primary" (click)="load()">{{ retryText() }}</button>
        </div>
      }
    }
  `,
})
export class TmLazyPickerPage {
  readonly #ref: TmModalRef<unknown> = inject(TmModalRef);
  readonly #loader = inject(ɵTM_PICKER_PAGES);
  readonly #telemetry = inject(TmTelemetry);
  readonly #context = (inject(TM_MODAL_DATA) as TmEntityPickerPageData).context as TmEntityPickerContext;

  /** Whether the shim is torn down, so a late load renders nothing. */
  #destroyed = false;

  /** Whether a page was shown, so its modal's outcome is reported. */
  #shown = false;

  /** Where the shim is: loading the page, showing it, or explaining why it shows none. */
  protected readonly state = signal<'loading' | 'ready' | 'unavailable' | 'failed'>('loading');

  /** The loaded page. */
  protected readonly page = signal<Type<unknown> | null>(null);

  /** The not-available message. */
  protected readonly unavailableText: ReturnType<TmLocalization['ɵtext']>;

  /** The load-failure message. */
  protected readonly failedText: ReturnType<TmLocalization['ɵtext']>;

  /** The retry button's label. */
  protected readonly retryText: ReturnType<TmLocalization['ɵtext']>;

  /** The close button's label, the modal's own. */
  protected readonly closeText = inject(TM_UI_TRANSLATE)('modal.close');

  constructor() {
    const localization = inject(TmLocalization);
    ɵtmRegisterPickerStrings(localization);
    this.unavailableText = localization.ɵtext('tm.picker.unavailable');
    this.failedText = localization.ɵtext('tm.picker.loadFailed');
    this.retryText = localization.ɵtext('tm.errors.retry');
    inject(DestroyRef).onDestroy(() => (this.#destroyed = true));

    // --- Each shown page is reported once, with how its modal closed ---
    void this.#ref.closed.then((result) => {
      if (!this.#shown) {
        return; // Nothing was shown: an unavailable page is no use of one.
      }
      // A page closes with its pick; `null` (a record gone) and a bare close pick nothing.
      const picked = result.via === 'api' && result.value !== null && result.value !== undefined;
      this.#telemetry.event('tm.entity.picker-page', { page: this.#context.kind, outcome: picked ? 'picked' : 'dismissed' });
    });
    void this.load();
  }

  /** Loads the page the context names, showing it, or why there is none. */
  protected async load(): Promise<void> {
    this.state.set('loading');
    let page: Type<unknown> | null;
    try {
      page = await this.#loader(this.#context.kind === 'search' ? 'search' : 'details');
    } catch {
      if (!this.#destroyed) {
        this.state.set('failed');
      }
      return;
    }
    if (this.#destroyed) {
      return;
    }
    this.#shown ||= page !== null;
    this.page.set(page);
    this.state.set(page === null ? 'unavailable' : 'ready');
  }

  /** Closes the modal with no pick. */
  protected close(): void {
    this.#ref.close();
  }
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  DestroyRef,
  Directive,
  effect,
  type EmbeddedViewRef,
  inject,
  input,
  type InputSignal,
  type OnInit,
  TemplateRef,
  untracked,
  ViewContainerRef,
} from '@angular/core';
import { TmSkeleton } from '@tellma/core-ui/skeleton';
import type { TmJson, TmJsonObject } from '@tellma/core';

import { injectTmDetailsRecord, tmDevMode } from './tm-details-record';

// The record's extras in a layout (spec 0031 §6, §5.1): a structural
// directive renders its template with an extra's value once the record has
// it (three skeleton lines while the record loads with nothing held,
// nothing when the read returned none), and a title extra joins the
// header's badges. Every extra the descriptor lists arrives with each read.

/** The context of a `*tmDetailsExtra` template. */
export interface TmDetailsExtraContext {
  /** A query row set extra's rows, as objects keyed by column name; any other extra's value. */
  readonly $implicit: TmJson | readonly TmJsonObject[];
  /** The extra's value as the read returned it. */
  readonly value: TmJson;
}

/**
 * Renders its template with one of the record's extras, by name: the
 * extras the entity's descriptor lists arrive with every read and save.
 * While the record loads with nothing held, three skeleton lines hold its
 * place; when the read returned no such extra, it renders nothing.
 *
 * @example
 * ```html
 * <acme-related-documents *tmDetailsExtra="'related-documents'; let rows" [rows]="rows" />
 * ```
 */
@Directive({ selector: '[tmDetailsExtra]' })
export class TmDetailsExtra implements OnInit {
  readonly #record = injectTmDetailsRecord();
  readonly #template = inject<TemplateRef<TmDetailsExtraContext>>(TemplateRef);
  readonly #container = inject(ViewContainerRef);

  /** The extra's name, as the descriptor lists it. */
  readonly tmDetailsExtra: InputSignal<string> = input.required<string>();

  /** The rendered template, and the value it shows. */
  #shown: { readonly view: EmbeddedViewRef<TmDetailsExtraContext>; readonly value: TmJson } | null = null;

  /** Whether the skeleton shows. */
  #skeleton = false;

  /**
   * Types the template's context.
   *
   * @param _directive - The directive.
   * @param _context - The context.
   * @returns Always `true`.
   */
  // eslint-disable-next-line @typescript-eslint/no-unused-vars -- type-position-only parameter
  static ngTemplateContextGuard(_directive: TmDetailsExtra, _context: unknown): _context is TmDetailsExtraContext {
    return true;
  }

  /** Follows the record: the skeleton while it loads, then the extra's value. */
  constructor() {
    effect(() => {
      const loading = this.#record.status() === 'loading' && !this.#record.landed();
      const value = this.#record.extras()[this.tmDetailsExtra()];
      untracked(() => this.#render(loading, value));
    });
    inject(DestroyRef).onDestroy(() => this.#container.clear());
  }

  /** Refuses an extra the entity does not declare. */
  ngOnInit(): void {
    const name = this.tmDetailsExtra();
    if (!this.#record.descriptor.extras.includes(name) && tmDevMode()) {
      throw new Error(`The extra '${name}' is not one ${this.#record.descriptor.name} declares: name one of its extras.`);
    }
  }

  /**
   * Renders the skeleton, the template with a value, or nothing.
   *
   * @param loading - Whether the record loads with nothing held.
   * @param value - The extra's value, or `undefined`.
   */
  #render(loading: boolean, value: TmJson | undefined): void {
    if (loading) {
      if (!this.#skeleton) {
        this.#clear();
        const skeleton = this.#container.createComponent(TmSkeleton);
        skeleton.setInput('lines', 3);
        this.#skeleton = true;
      }
      return;
    }
    if (this.#shown?.value === value && !this.#skeleton) {
      return;
    }
    this.#clear();
    if (value !== undefined) {
      const view = this.#container.createEmbeddedView(this.#template, { $implicit: rowsOf(value), value });
      this.#shown = { view, value };
    }
  }

  /** Removes what is rendered. */
  #clear(): void {
    this.#container.clear();
    this.#shown = null;
    this.#skeleton = false;
  }
}

/**
 * Renders its template after the header's badges, in the title row: a
 * layout's own badge or button, such as a workflow state chip.
 *
 * @example
 * ```html
 * <ng-template tmDetailsTitleExtra><tm-badge tone="info">{{ state() }}</tm-badge></ng-template>
 * ```
 */
@Directive({ selector: 'ng-template[tmDetailsTitleExtra]' })
export class TmDetailsTitleExtra {
  /** Registers the template with the record, whose header renders it. */
  constructor() {
    injectTmDetailsRecord().registerTitleExtra(inject<TemplateRef<unknown>>(TemplateRef), inject(DestroyRef));
  }
}

/**
 * An extra's value as a template gets it: a query row set's rows as
 * objects keyed by column name; any other value as it is.
 *
 * @param value - The extra's value.
 * @returns The rows, or the value.
 */
function rowsOf(value: TmJson): TmJson | readonly TmJsonObject[] {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    return value;
  }
  const { columns, rows } = value as { readonly columns?: unknown; readonly rows?: unknown };
  if (!Array.isArray(columns) || !Array.isArray(rows)) {
    return value;
  }
  const names = (columns as readonly { readonly name?: unknown }[]).map((column) => String(column.name));
  return (rows as readonly (readonly TmJson[])[]).map((cells) => Object.fromEntries(names.map((name, index) => [name, cells[index] ?? null])));
}

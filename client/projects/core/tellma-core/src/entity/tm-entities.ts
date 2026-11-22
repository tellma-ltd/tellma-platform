// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable, Injector, signal, type Signal } from '@angular/core';

import { TM_APP_DECLARATIONS } from '../app/tm-app-declarations';
import { TmLocalization } from '../l10n/tm-localization';
import { TmClientMetadataStore } from '../metadata/tm-client-metadata';
import { TM_METADATA_LOADERS } from '../metadata/tm-metadata-loaders';
import type { TmEntityDeclaration } from './tm-entity-declaration';
import type { TmEntitySummary } from './tm-entity-metadata';

// The entity registry's summary members (spec 0029 §4.1): what route
// matching, URL building and the route host read from the Core chunk's
// entity summaries and the merged declarations, and the Entities chunk's one
// load. The descriptor members over the Entities chunk join them with the
// entity model.

/**
 * The app's entities: their summaries from the Core chunk, their merged
 * declarations, their labels, and the Entities chunk's load.
 *
 * @example
 * ```ts
 * const centers = inject(TmEntities).bySegment('centers'); // the gl.Center summary
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmEntities {
  readonly #injector = inject(Injector);
  readonly #metadata = inject(TmClientMetadataStore);
  readonly #l10n = inject(TmLocalization);

  /** The summaries by lower-cased name, built at first use. */
  #byName: ReadonlyMap<string, TmEntitySummary> | null = null;

  /** The summaries by segment, built at first use. */
  #bySegment: ReadonlyMap<string, TmEntitySummary> | null = null;

  /** The Entities chunk's load, once it started. */
  #load: Promise<void> | null = null;

  /** Whether the Entities chunk has loaded. */
  readonly #loaded = signal(false);

  /** Whether the Entities chunk has loaded. */
  readonly loaded: Signal<boolean> = this.#loaded.asReadonly();

  /**
   * An entity's summary from the Core chunk.
   *
   * @param name - The entity's name, in any case: `gl.Center` or `GL.center`.
   * @returns The summary, or `undefined` for a name the server does not register.
   */
  summary(name: string): TmEntitySummary | undefined {
    this.#byName ??= new Map(this.#metadata.get().entities.map((summary) => [summary.name.toLowerCase(), summary]));
    return this.#byName.get(name.toLowerCase());
  }

  /**
   * The entity whose API resource segment is `segment`, case included: the
   * router's match, so `Centers` is no entity.
   *
   * @param segment - The segment, such as `centers`.
   * @returns The summary, or `undefined`.
   */
  bySegment(segment: string): TmEntitySummary | undefined {
    this.#bySegment ??= new Map(
      this.#metadata
        .get()
        .entities.filter((summary) => summary.segment !== null)
        .map((summary) => [summary.segment!, summary]),
    );
    return this.#bySegment.get(segment);
  }

  /**
   * An entity's merged declaration, from `TM_APP_DECLARATIONS`.
   *
   * @param name - The entity's name, in any case.
   * @returns The declaration, or `undefined` when no feature declares the entity.
   */
  declaration(name: string): TmEntityDeclaration | undefined {
    const summary = this.summary(name);
    return summary && this.#injector.get(TM_APP_DECLARATIONS, null)?.entities.get(summary.name);
  }

  /**
   * An entity's label, singular or plural, in the active language.
   *
   * @param name - The entity's name, in any case.
   * @param plural - Whether the plural label is wanted.
   * @returns The label; the name itself for an unknown entity.
   */
  label(name: string, plural = false): Signal<string> {
    const summary = this.summary(name);
    return summary
      ? this.#l10n.ɵtext(plural ? summary.pluralLabelKey : summary.labelKey, undefined, summary.labelFallback)
      : signal(name).asReadonly();
  }

  /**
   * Loads the Entities chunk, once; later calls share the first load, and a
   * failed load is tried again on the next call.
   *
   * @returns A promise that resolves once the chunk is loaded.
   */
  load(): Promise<void> {
    const loaders = this.#injector.get(TM_METADATA_LOADERS, null);
    this.#load ??= (loaders ? loaders.entities() : Promise.resolve()).then(
      () => this.#loaded.set(true),
      (error: unknown) => {
        this.#load = null;
        throw error;
      },
    );
    return this.#load;
  }
}

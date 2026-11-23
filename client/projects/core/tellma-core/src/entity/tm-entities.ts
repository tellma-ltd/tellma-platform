// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, inject, Injectable, Injector, signal, type Signal } from '@angular/core';

import { TM_APP_DECLARATIONS } from '../app/tm-app-declarations';
import { TM_DEV_MODE } from '../app/tm-dev-mode';
import { TmLocalization } from '../l10n/tm-localization';
import { TmClientMetadataStore } from '../metadata/tm-client-metadata';
import { TM_METADATA_LOADERS } from '../metadata/tm-metadata-loaders';
import { TmTelemetry } from '../observe/tm-telemetry';
import { TmLaunchCache } from '../state/launch/tm-launch-cache';
import type { TmEntityDeclaration } from './tm-entity-declaration';
import type { TmEntityDescriptor, TmEnumDescriptor, TmPathResolution } from './tm-entity-descriptors';
import type { TmEntitySummary } from './tm-entity-metadata';
import type { TmEntityModel } from './tm-entity-model';

// The entity registry (spec 0029 §4): what route matching, URL building, the
// route host and the shells read from the Core chunk's entity summaries and
// the merged declarations at once, and, after one load of the Entities
// chunk, the descriptors gated by the tenant's shape. The model over the
// Entities chunk is a lazy chunk the load brings with it, so this file holds
// only the summary members and the doors into the model.

/** The shapes a tenant's languages give: one to three content languages. */
type TmShape = 1 | 2 | 3;

/**
 * The app's entities: their summaries from the Core chunk, their merged
 * declarations and labels, and, once the Entities chunk is loaded, their
 * descriptors, named enums and paths.
 *
 * @example
 * ```ts
 * const entities = inject(TmEntities);
 * const centers = entities.bySegment('centers'); // the gl.Center summary
 * await entities.load();
 * const code = entities.get('gl.Center').property('Code');
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmEntities {
  readonly #injector = inject(Injector);
  readonly #metadata = inject(TmClientMetadataStore);
  readonly #l10n = inject(TmLocalization);
  readonly #devMode = inject(TM_DEV_MODE);
  readonly #telemetry = inject(TmTelemetry);

  /** The summaries by lower-cased name, built at first use. */
  #byName: ReadonlyMap<string, TmEntitySummary> | null = null;

  /** The summaries by segment, built at first use. */
  #bySegment: ReadonlyMap<string, TmEntitySummary> | null = null;

  /** The Entities chunk's load, once it started. */
  #load: Promise<void> | null = null;

  /** The model over the loaded Entities chunk. */
  readonly #model = signal<TmEntityModel | null>(null);

  /** The descriptors of the summary alone, by name and shape, which a production app reads before the load settles. */
  readonly #bare = new Map<string, TmEntityDescriptor>();

  /** Whether the Entities chunk has loaded. */
  readonly loaded: Signal<boolean> = computed(() => this.#model() !== null);

  /** The model over the loaded Entities chunk, or `null` before the load settles. Internal. */
  readonly ɵmodel: Signal<TmEntityModel | null> = this.#model.asReadonly();

  /** The routed tenant's shape: its number of content languages; one outside a tenant. Internal. */
  readonly ɵshape: Signal<TmShape> = computed(
    () => Math.min(3, Math.max(1, this.#injector.get(TmLaunchCache).docs.settings()?.languages.length ?? 1)) as TmShape,
  );

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
   * Whether an entity has a details page: its API offers `details`, and its
   * merged declaration has a `details` member, which the server cannot see.
   * Internal: the routes, the URLs and the permissions read it.
   *
   * @param summary - The entity's summary.
   * @returns Whether it has one.
   */
  ɵhasDetailsPage(summary: TmEntitySummary): boolean {
    return summary.operations.includes('details') && this.declaration(summary.name)?.['details'] !== undefined;
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
   * Loads the Entities chunk and the model over it, once; later calls share
   * the first load, and a failed load is tried again on the next call. A
   * warm app reads both from the worker's cache.
   *
   * @returns A promise that resolves once the chunk is loaded.
   */
  load(): Promise<void> {
    const loaders = this.#injector.get(TM_METADATA_LOADERS, null);
    const started = performance.now();
    this.#load ??= Promise.all([
      loaders ? loaders.entities() : Promise.resolve({ entities: [], enums: [] }),
      import('./tm-entity-model'),
    ]).then(
      ([chunk, { TmEntityModel: Model }]) => {
        const launch = this.#injector.get(TmLaunchCache);
        this.#model.set(
          new Model(chunk, {
            text: (key, fallback) => this.#l10n.ɵtext(key, undefined, fallback),
            entityLabel: (name, plural) => this.label(name, plural),
            declaration: (name) => this.declaration(name),
            settings: launch.docs.settings,
          }),
        );
        this.#telemetry.timing('tm.entity.metadata-loaded', performance.now() - started);
      },
      (error: unknown) => {
        this.#load = null;
        throw error;
      },
    );
    return this.#load;
  }

  /**
   * An entity's descriptor: its metadata merged with its declaration and
   * gated by the routed tenant's shape. Read inside a `computed`, it follows
   * a change of the tenant's languages. It needs {@link TmEntities.load}:
   * before the load settles a development app throws, and a production app
   * gets a descriptor of the summary alone.
   *
   * @param name - The entity's name, in any case.
   * @returns The descriptor.
   * @throws Error for an entity the metadata does not list.
   */
  get(name: string): TmEntityDescriptor {
    const shape = this.ɵshape();
    const model = this.#model();
    const descriptor = model ? model.descriptor(name, shape) : this.ɵbeforeLoad('TmEntities.get') ?? this.#summaryDescriptor(name, shape);
    if (descriptor === undefined) {
      throw new Error(`The metadata lists no entity '${name}'.`);
    }
    return descriptor;
  }

  /**
   * A named enum: a C# enum such as `gl.CenterType`, or a value set such as
   * `core.TimeZone`. It needs {@link TmEntities.load}.
   *
   * @param name - The enum's name.
   * @returns The enum, or `undefined`.
   */
  enum(name: string): TmEnumDescriptor | undefined {
    const model = this.#model();
    return model ? model.enum(name) : this.ɵbeforeLoad('TmEntities.enum');
  }

  /**
   * Reads a column key or Queryex path against an entity, under the routed
   * tenant's shape. It needs {@link TmEntities.load}.
   *
   * @param root - The entity the path starts from, such as `gl.Center`.
   * @param key - The path, such as `Parent.Manager`.
   * @returns The resolution, or `null` for a path that does not resolve.
   */
  resolve(root: string, key: string): TmPathResolution | null {
    const model = this.#model();
    return model ? model.resolve(root, key, this.ɵshape()) : (this.ɵbeforeLoad('TmEntities.resolve') ?? null);
  }

  /**
   * Refuses a member that needs the Entities chunk before it loads, in a
   * development app; a production app carries on. Internal: the members
   * here and the display names' select call it.
   *
   * @param member - The member's name, such as `TmEntities.get`.
   * @returns `undefined`.
   * @throws Error in a development app.
   */
  ɵbeforeLoad(member: string): undefined {
    if (typeof ngDevMode !== 'undefined' && ngDevMode && this.#devMode()) {
      throw new Error(`${member} needs the Entities chunk: await TmEntities.load() first, as the route host does.`);
    }
    return undefined;
  }

  /**
   * A descriptor of an entity's summary alone, for a production app that
   * reads one before the Entities chunk loads: no members, and the
   * summary's operations and capabilities.
   *
   * @param name - The entity's name.
   * @param shape - The tenant's shape.
   * @returns The descriptor, or `undefined` for an unknown entity.
   */
  #summaryDescriptor(name: string, shape: TmShape): TmEntityDescriptor | undefined {
    const summary = this.summary(name);
    if (summary === undefined) {
      return undefined;
    }
    const key = `${summary.name}|${shape}`;
    let descriptor = this.#bare.get(key);
    if (descriptor === undefined) {
      const none = (): undefined => undefined;
      descriptor = Object.freeze({
        ...summary,
        key: { property: 'Id', type: 'int' as const },
        properties: [],
        navigations: [],
        children: [],
        actions: [],
        relatedSelect: [],
        searchable: [],
        naturalKeys: [],
        extras: [],
        defaultOrderBy: 'Id desc',
        limits: { maxTake: 0, countCap: 0, maxSkipWindow: 0, maxSearchLength: 0, maxIds: 0, maxExtras: 0, maxFilterLength: 0 },
        label: this.label(summary.name),
        pluralLabel: this.label(summary.name, true),
        capabilities: new Set(summary.capabilities),
        operations: new Set(summary.operations),
        declaration: this.declaration(summary.name) ?? Object.freeze({ name: summary.name }),
        slots: shape,
        property: none,
        navigation: none,
        child: none,
      });
      this.#bare.set(key, descriptor);
    }
    return descriptor;
  }
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, signal, type Signal } from '@angular/core';

import type { TmApi } from '../api/tm-api';
import { TmApiError } from '../api/tm-api-error';
import type { TmJson, TmJsonObject } from '../json/tm-json';
import type { TmEntitiesChunk } from '../metadata/tm-client-metadata';
import type { TmTenantSettingsForClient } from '../state/launch/tm-launch-docs';
import type { TmEntityActionDeclaration, TmEntityDeclaration } from './tm-entity-declaration';
import type {
  TmChildDescriptor,
  TmEntityActionDescriptor,
  TmEntityDescriptor,
  TmEnumDescriptor,
  TmNavigationDescriptor,
  TmPathEnd,
  TmPathResolution,
  TmPropertyDescriptor,
} from './tm-entity-descriptors';
import type { TmEntityMetadata, TmEnumMetadata, TmPropertyMetadata } from './tm-entity-metadata';
import { tmRowsToObjects, type TmQueryRowSet, type ɵTmJsonNames } from './tm-rows-to-objects';

// The entity model over the Entities chunk (spec 0029 §4): descriptors gated
// by the tenant's shape, named enums, path resolution, the traversal rule of
// client permissions, and the display names' background reads (§5.3). This
// module is a lazy chunk of its own: the registry loads it with the Entities
// chunk, since every member here needs that chunk, so none of it weighs on
// the app's first frame.

/** What the model reads from the app: labels, the tenant's languages and the merged declarations. */
export interface TmEntityModelContext {
  /**
   * A string's text in the active language.
   *
   * @param key - The string key.
   * @param fallback - The text when no strings hold the key.
   * @returns The text.
   */
  readonly text: (key: string, fallback?: string) => Signal<string>;
  /**
   * An entity's label from its summary.
   *
   * @param name - The entity's name.
   * @param plural - Whether the plural label is wanted.
   * @returns The label.
   */
  readonly entityLabel: (name: string, plural: boolean) => Signal<string>;
  /**
   * An entity's merged declaration.
   *
   * @param name - The entity's name.
   * @returns The declaration, or `undefined`.
   */
  readonly declaration: (name: string) => TmEntityDeclaration | undefined;
  /** The routed tenant's settings, for its languages' symbols and directions. */
  readonly settings: Signal<TmTenantSettingsForClient | undefined>;
}

/** A descriptor as the model builds it, with the wire names of its paths. */
type TmModelDescriptor = TmEntityDescriptor & ɵTmJsonNames;

/**
 * Indexes a list by lower-cased name.
 *
 * @param items - The items.
 * @returns The items by lower-cased name.
 */
function byName<T extends { readonly name: string }>(items: readonly T[]): ReadonlyMap<string, T> {
  return new Map(items.map((item) => [item.name.toLowerCase(), item]));
}

/**
 * The entity model over one Entities chunk. Internal: `TmEntities`,
 * `TmPermissions` and `TmDisplayNames` read it through their own members.
 */
export class TmEntityModel {
  /** The app's labels, languages and declarations. */
  readonly #context: TmEntityModelContext;

  /** Each entity's metadata, by lower-cased name. */
  readonly #metadata: ReadonlyMap<string, TmEntityMetadata>;

  /** Each named enum, by name. */
  readonly #enums: ReadonlyMap<string, TmEnumMetadata>;

  /** The descriptors built so far, by shape, then by lower-cased name. */
  readonly #descriptors = new Map<number, Map<string, TmModelDescriptor>>();

  /** The enum descriptors built so far, by name. */
  readonly #enumDescriptors = new Map<string, TmEnumDescriptor>();

  /**
   * Indexes the Entities chunk.
   *
   * @param chunk - The Entities chunk.
   * @param context - The app's labels, languages and declarations.
   */
  constructor(chunk: TmEntitiesChunk, context: TmEntityModelContext) {
    this.#context = context;
    this.#metadata = byName(chunk.entities);
    this.#enums = new Map(chunk.enums.map((entry) => [entry.name, entry]));
  }

  /**
   * An entity's metadata, as the Entities chunk carries it.
   *
   * @param name - The entity's name, in any case.
   * @returns The metadata, or `undefined`.
   */
  metadata(name: string): TmEntityMetadata | undefined {
    return this.#metadata.get(name.toLowerCase());
  }

  // ---------------------------------------------------------------------------
  // Descriptors
  // ---------------------------------------------------------------------------

  /**
   * An entity's descriptor under a shape: built once per entity and shape.
   *
   * @param name - The entity's name, in any case.
   * @param shape - The tenant's number of content languages.
   * @returns The descriptor, or `undefined` for an entity the chunk lacks.
   */
  descriptor(name: string, shape: 1 | 2 | 3): TmModelDescriptor | undefined {
    let descriptors = this.#descriptors.get(shape);
    if (descriptors === undefined) {
      this.#descriptors.set(shape, (descriptors = new Map()));
    }
    const lower = name.toLowerCase();
    let descriptor = descriptors.get(lower);
    if (descriptor === undefined) {
      const metadata = this.#metadata.get(lower);
      if (metadata === undefined) {
        return undefined;
      }
      descriptor = this.#build(metadata, shape);
      descriptors.set(lower, descriptor);
    }
    return descriptor;
  }

  /**
   * Builds one descriptor: the twins beyond the shape leave the properties
   * and every select list, and each member gets its label.
   *
   * @param metadata - The entity's metadata.
   * @param shape - The tenant's number of content languages.
   * @returns The descriptor.
   */
  #build(metadata: TmEntityMetadata, shape: 1 | 2 | 3): TmModelDescriptor {
    const { text, entityLabel, declaration } = this.#context;
    const label = (member: { readonly labelKey: string; readonly labelFallback?: string }): Signal<string> =>
      text(member.labelKey, member.labelFallback);

    // --- Shape gating: a twin beyond the tenant's languages is no property ---
    const excluded = new Set(
      metadata.properties.filter((property) => (property.multilingual?.slot ?? 1) > shape).map((property) => property.name.toLowerCase()),
    );
    const admitted = (path: string): boolean => !excluded.has(path.toLowerCase());
    const properties = metadata.properties.filter((property) => admitted(property.name)).map((property) => this.#property(property, shape));

    // --- The members, each labelled ---
    // A navigation is chosen by writing its key, so it carries the key's ownership.
    const ownership = (key: string): TmPropertyMetadata['ownership'] =>
      metadata.properties.find((property) => property.name.toLowerCase() === key.toLowerCase())?.ownership ?? 'editable';
    const navigations: TmNavigationDescriptor[] = metadata.navigations.map((navigation) => ({
      ...navigation,
      label: label(navigation),
      ownership: ownership(navigation.foreignKey),
    }));
    const children: TmChildDescriptor[] = metadata.children.map((child) => ({ ...child, label: label(child) }));
    const merged: TmEntityDeclaration = declaration(metadata.name) ?? Object.freeze({ name: metadata.name });
    const declaredActions = Array.isArray(merged['actions']) ? (merged['actions'] as readonly TmEntityActionDeclaration<string>[]) : [];
    // An action's arguments are gated and labelled as the properties are.
    const actions: TmEntityActionDescriptor[] = metadata.actions.map(({ fields: argumentFields, ...action }) => {
      const declared = declaredActions.find((candidate) => candidate.name === action.name);
      const fields = argumentFields
        ?.filter((field) => (field.multilingual?.slot ?? 1) <= shape)
        .map((field) => this.#slotted(field, text(field.labelKey, field.labelFallback), shape));
      return { ...action, label: label(action), ...(fields && { fields }), ...(declared && { declaration: declared }) };
    });

    // --- Lookups by name, ignoring case ---
    const propertyIndex = byName(properties);
    const navigationIndex = byName(navigations);
    const childIndex = byName(children);
    return Object.freeze({
      ...metadata,
      label: entityLabel(metadata.name, false),
      pluralLabel: entityLabel(metadata.name, true),
      properties,
      relatedSelect: metadata.relatedSelect.filter(admitted),
      searchable: metadata.searchable.filter(admitted),
      naturalKeys: metadata.naturalKeys.filter(admitted),
      navigations,
      children,
      capabilities: new Set(metadata.capabilities),
      operations: new Set(metadata.operations),
      actions,
      declaration: merged,
      slots: shape,
      property: (name: string) => propertyIndex.get(name.toLowerCase()),
      navigation: (name: string) => navigationIndex.get(name.toLowerCase()),
      child: (name: string) => childIndex.get(name.toLowerCase()),
      ɵjsonPath: (path: readonly string[]) => this.#jsonPath(metadata, path),
    });
  }

  /**
   * One property's descriptor, labelled by its key and, for a multilingual
   * slot, by its language.
   *
   * @param property - The property's metadata.
   * @param shape - The tenant's number of content languages.
   * @returns The descriptor.
   */
  #property(property: TmPropertyMetadata, shape: 1 | 2 | 3): TmPropertyDescriptor {
    return this.#slotted(property, this.#context.text(property.labelKey, property.labelFallback), shape);
  }

  /**
   * A labelled member, a property or an action's argument. A multilingual
   * slot's label is the group's label with its language's symbol when the
   * tenant has two or three languages, and its content direction is its
   * language's; both follow the tenant's settings.
   *
   * @param member - The member's metadata.
   * @param base - The label its key reads.
   * @param shape - The tenant's number of content languages.
   * @returns The member with its label, and its direction when it fills a slot.
   */
  #slotted<T extends { readonly multilingual?: { readonly slot: 1 | 2 | 3 } }>(
    member: T,
    base: Signal<string>,
    shape: 1 | 2 | 3,
  ): T & { readonly label: Signal<string>; readonly contentDir?: 'ltr' | 'rtl' } {
    const slot = member.multilingual?.slot;
    if (slot === undefined) {
      return { ...member, label: base };
    }
    const { settings } = this.#context;
    const language = (): { readonly symbol: string; readonly isRightToLeft: boolean } | undefined => settings()?.languages[slot - 1];
    const descriptor = {
      ...member,
      label: shape === 1 ? base : computed(() => `${base()} (${language()?.symbol ?? slot})`),
    };
    // The direction is read when asked, so a change of the tenant's languages shows at once.
    return Object.defineProperty(descriptor, 'contentDir', {
      enumerable: true,
      get: (): 'ltr' | 'rtl' => (language()?.isRightToLeft ? 'rtl' : 'ltr'),
    });
  }

  /**
   * The wire names of a path's segments, read from the metadata.
   *
   * @param root - The entity the path starts from.
   * @param path - The path in C# names.
   * @returns The JSON names, or `null` when a segment does not resolve.
   */
  #jsonPath(root: TmEntityMetadata, path: readonly string[]): readonly string[] | null {
    const names: string[] = [];
    let entity: TmEntityMetadata | undefined = root;
    for (const [index, segment] of path.entries()) {
      const lower = segment.toLowerCase();
      const navigation = entity?.navigations.find((candidate) => candidate.name.toLowerCase() === lower);
      if (index === path.length - 1) {
        const member = navigation ?? entity?.properties.find((candidate) => candidate.name.toLowerCase() === lower);
        if (member === undefined) {
          return null;
        }
        names.push(member.json);
      } else {
        if (navigation === undefined) {
          return null;
        }
        names.push(navigation.json);
        entity = this.metadata(navigation.target);
      }
    }
    return names;
  }

  // ---------------------------------------------------------------------------
  // Named enums
  // ---------------------------------------------------------------------------

  /**
   * A named enum's descriptor, built once.
   *
   * @param name - The enum's name, such as `gl.CenterType` or `core.TimeZone`.
   * @returns The descriptor, or `undefined`.
   */
  enum(name: string): TmEnumDescriptor | undefined {
    let descriptor = this.#enumDescriptors.get(name);
    const metadata = this.#enums.get(name);
    if (descriptor === undefined && metadata !== undefined) {
      const labels = new Map<string, Signal<string>>();
      const values = new Map(metadata.values.map((value) => [value.value, value]));
      descriptor = Object.freeze({
        ...metadata,
        label: (value: string): Signal<string> => {
          const canonical = metadata.aliases?.[value] ?? value;
          let label = labels.get(canonical);
          if (label === undefined) {
            const entry = values.get(canonical);
            label =
              entry?.labelKey !== undefined
                ? this.#context.text(entry.labelKey, entry.labelFallback)
                : signal(entry?.label ?? canonical).asReadonly();
            labels.set(canonical, label);
          }
          return label;
        },
      });
      this.#enumDescriptors.set(name, descriptor);
    }
    return descriptor;
  }

  // ---------------------------------------------------------------------------
  // Paths
  // ---------------------------------------------------------------------------

  /**
   * Reads a column key or Queryex path against an entity: every segment but
   * the last names a navigation; the last names a scalar, a twin, a group
   * anchor or a navigation, all ignoring case.
   *
   * @param root - The entity the path starts from.
   * @param key - The path, such as `Parent.Manager`.
   * @param shape - The tenant's number of content languages.
   * @returns The resolution, or `null` when the path does not resolve under the shape.
   */
  resolve(root: string, key: string, shape: 1 | 2 | 3): TmPathResolution | null {
    const segments = key.split('.');
    let entity = this.descriptor(root, shape);
    const steps: TmNavigationDescriptor[] = [];

    // --- Every segment but the last crosses a navigation ---
    for (const segment of segments.slice(0, -1)) {
      const navigation = entity?.navigation(segment);
      if (navigation === undefined) {
        return null;
      }
      steps.push(navigation);
      entity = this.descriptor(navigation.target, shape);
    }
    if (entity === undefined) {
      return null;
    }

    // --- The last segment: a property, a group anchor, or a navigation ---
    const last = segments[segments.length - 1];
    const property = entity.property(last);
    let end: TmPathEnd;
    let endLabel: Signal<string>;
    let fkPath: string | undefined;
    if (property !== undefined) {
      const group = property.multilingual?.group;
      if (group !== undefined && group.toLowerCase() === property.name.toLowerCase()) {
        // The anchor stands for the whole group, labelled without a language symbol.
        const slots = entity.properties.filter((candidate) => candidate.multilingual?.group === group);
        end = { kind: 'text', slots };
        endLabel = this.#context.text(property.labelKey, property.labelFallback);
      } else {
        end = { kind: 'value', property };
        endLabel = property.label;
      }
    } else {
      const navigation = entity.navigation(last);
      if (navigation === undefined) {
        return null;
      }
      const policy = entity.property(navigation.foreignKey)?.blob;
      end =
        navigation.target === 'core.Blob' && policy !== undefined
          ? { kind: policy.image !== null ? 'image' : 'file', navigation, policy }
          : { kind: 'navigation', navigation };
      endLabel = navigation.label;
      fkPath = [...steps.map((step) => step.name), navigation.foreignKey].join('.');
    }
    const labels = [...steps.map((step) => step.label), endLabel];
    return {
      root: this.descriptor(root, shape)?.name ?? root,
      key,
      steps,
      entity: entity.name,
      end,
      label: computed(() => labels.map((part) => part()).join(' / ')),
      ...(fkPath !== undefined && { fkPath }),
    };
  }

  /**
   * Whether a path passes the traversal rule, walked segment by segment as
   * the server walks it: at each entity after the root that the user reads
   * with a filter or not at all, the one segment read there must be in the
   * entity's related projection. A navigation segment passes by its name or
   * its foreign key's, and a group anchor reads every admitted slot. The
   * caller checks the root's own `Read`.
   *
   * @param root - The entity the path starts from.
   * @param key - The path.
   * @param shape - The tenant's number of content languages.
   * @param restricted - Whether the user's `Read` on an entity is filtered or absent.
   * @returns Whether the path is reachable; `false` when it does not resolve.
   */
  reachable(root: string, key: string, shape: 1 | 2 | 3, restricted: (entity: string) => boolean): boolean {
    const resolution = this.resolve(root, key, shape);
    if (resolution === null) {
      return false;
    }

    // --- What the last segment reads where it is read: each entry passes by any of its names ---
    const { steps, end } = resolution;
    const last: readonly (readonly string[])[] =
      end.kind === 'value'
        ? [[end.property.name]]
        : end.kind === 'text'
          ? end.slots.map((slot) => [slot.name])
          : [[end.navigation.name, end.navigation.foreignKey]];

    // --- Each restricted entity a step reaches projects the segment read there ---
    // Spec 0015 §5.2 step 4 also counts a child root's owner as the root when
    // the child's Read carries no grant of its own. The access summary does
    // not say which of a child's grants are its own (spec 0014 §5.5 composes
    // the owner's into it), so the client never assumes so: it may under-offer
    // an owner's columns, but never offers one the server refuses.
    return steps.every((step, index) => {
      if (step.target !== 'core.Blob' && !restricted(step.target)) {
        return true;
      }
      const projection = new Set(this.descriptor(step.target, shape)?.relatedSelect.map((name) => name.toLowerCase()));
      const next = steps[index + 1];
      const reads = next === undefined ? last : [[next.name, next.foreignKey]];
      return reads.every((names) => names.some((name) => projection.has(name.toLowerCase())));
    });
  }
}

// -----------------------------------------------------------------------------
// The display names' background reads
// -----------------------------------------------------------------------------

/** The most ids one background read asks for. */
const IDS_PER_READ = 100;

/** One entity's ids whose display names the registry reads in the background. Internal. */
export interface ɵTmDisplayNameRead {
  /** The HTTP layer. */
  readonly api: TmApi;
  /** The entity's descriptor. */
  readonly descriptor: TmEntityDescriptor;
  /** The display name's columns, the key's first. */
  readonly columns: readonly string[];
  /** The key's path and wire name. */
  readonly key: { readonly name: string; readonly json: string };
  /** The ids, as decimal text. */
  readonly keys: readonly string[];
  /** The tenant to read from. */
  readonly tenantId: number;
  /** Aborts the reads when the tab leaves the tenant. */
  readonly signal: AbortSignal;
}

/**
 * Reads the display names of an entity's ids: one background query per 100
 * ids, each with the display name's select, inactive rows included. A
 * refusal names its ids missing; any other failure leaves them unknown, so
 * a later resolve asks again. Internal: `TmDisplayNames.resolve` runs it.
 *
 * @param read - The entity, its ids and the read's options.
 * @param hold - Takes each query's rows, the ids it did not return and its duration in milliseconds.
 * @returns A promise that settles once every query has.
 */
export async function ɵtmReadDisplayNames(
  read: ɵTmDisplayNameRead,
  hold: (rows: readonly TmJsonObject[], missing: readonly string[], ms: number) => void,
): Promise<void> {
  const { api, descriptor, key, keys, tenantId, signal } = read;
  const size = Math.min(IDS_PER_READ, descriptor.limits.maxTake > 0 ? descriptor.limits.maxTake : IDS_PER_READ);
  const queries: Promise<void>[] = [];
  for (let start = 0; start < keys.length; start += size) {
    const batch = keys.slice(start, start + size);
    const started = performance.now();
    queries.push(
      api
        .read<{ readonly rows: TmQueryRowSet }>(
          `${descriptor.segment}/query`,
          {
            select: read.columns.join(','),
            filters: [`${key.name} in (${batch.join(', ')})`],
            take: batch.length,
            // An inactive row still names itself wherever a record points at it.
            ...(descriptor.capabilities.has('activatable') && { includeInactive: true }),
          },
          { signal, background: true, tenantId },
        )
        .then(
          (page) => tmRowsToObjects(descriptor, page.rows),
          (error: unknown) => {
            if (error instanceof TmApiError && error.status === 403) {
              return [];
            }
            throw error;
          },
        )
        .then(
          (rows) => {
            const returned = new Set(rows.map((row) => idText(row[key.json])));
            hold(rows, batch.filter((id) => !returned.has(id)), performance.now() - started);
          },
          () => undefined,
        ),
    );
  }
  await Promise.all(queries);
}

/**
 * An id's decimal text, as the registry keys ids.
 *
 * @param id - The wire id.
 * @returns The text, or `null` for no id.
 */
function idText(id: TmJson | undefined): string | null {
  return typeof id === 'number' ? String(id) : typeof id === 'string' ? id.replace(/^(-?)0+(?=\d)/, '$1') : null;
}

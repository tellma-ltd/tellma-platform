// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Signal } from '@angular/core';

import type { TmEntityActionDeclaration, TmEntityDeclaration } from './tm-entity-declaration';
import type {
  TmActionMetadata,
  TmBlobKindPolicy,
  TmChildMetadata,
  TmEntityMetadata,
  TmEntityOperation,
  TmEnumMetadata,
  TmNavigationMetadata,
  TmPropertyMetadata,
} from './tm-entity-metadata';

// The descriptors (spec 0029 §4.1-§4.3): an entity's metadata merged with its
// declaration and gated by the tenant's shape, with labels as signals that
// follow the language. `TmEntities.get` builds them once the Entities chunk
// is loaded, at most one per entity and shape; `TmEntities.resolve` reads a
// path through them. Every type here is type-only.

/** A scalar property of an entity, labelled, with the direction of its content language. */
export interface TmPropertyDescriptor extends TmPropertyMetadata {
  /** The property's label; a twin's carries its language's symbol. */
  readonly label: Signal<string>;
  /** A multilingual slot's content direction, such as `rtl` for an Arabic `Name2`. */
  readonly contentDir?: 'ltr' | 'rtl';
}

/** A navigation of an entity, labelled, with who writes its key. */
export interface TmNavigationDescriptor extends TmNavigationMetadata {
  /** The navigation's label. */
  readonly label: Signal<string>;
  /** Who writes the navigation's key property, so who may choose its related row; `editable` when the key is unknown. */
  readonly ownership: TmPropertyMetadata['ownership'];
}

/** A child collection of an entity, labelled. */
export interface TmChildDescriptor extends TmChildMetadata {
  /** The collection's label. */
  readonly label: Signal<string>;
}

/** An action of an entity, labelled, with how its declaration offers it. */
export interface TmEntityActionDescriptor extends TmActionMetadata {
  /** The action's label. */
  readonly label: Signal<string>;
  /** The declaration's entry for the action, when it has one. */
  readonly declaration?: TmEntityActionDeclaration<string>;
}

/** A named enum, with its values' labels. */
export interface TmEnumDescriptor extends TmEnumMetadata {
  /**
   * A value's label in the active language.
   *
   * @param value - The value, or one of its aliases.
   * @returns The label; the value itself when the enum lacks it.
   */
  label(value: string): Signal<string>;
}

/**
 * An entity's metadata merged with its declaration and gated by the
 * tenant's shape: twins beyond the tenant's content languages are left out.
 *
 * @example
 * ```ts
 * const center = inject(TmEntities).get('gl.Center');
 * const code = center.property('Code');
 * ```
 */
export interface TmEntityDescriptor
  extends Omit<
    TmEntityMetadata,
    'properties' | 'navigations' | 'children' | 'actions' | 'relatedSelect' | 'searchable' | 'capabilities' | 'operations'
  > {
  /** The entity's singular label. */
  readonly label: Signal<string>;
  /** The entity's plural label. */
  readonly pluralLabel: Signal<string>;
  /** The entity's properties, twins beyond the shape removed. */
  readonly properties: readonly TmPropertyDescriptor[];
  /** The related projection's paths, twins beyond the shape removed. */
  readonly relatedSelect: readonly string[];
  /** The properties search matches, twins beyond the shape removed. */
  readonly searchable: readonly string[];
  /** The entity's navigations. */
  readonly navigations: readonly TmNavigationDescriptor[];
  /** The entity's child collections. */
  readonly children: readonly TmChildDescriptor[];
  /** The capabilities the entity's stack declares. */
  readonly capabilities: ReadonlySet<string>;
  /** The operations the entity's API offers. */
  readonly operations: ReadonlySet<TmEntityOperation>;
  /** The entity's actions. */
  readonly actions: readonly TmEntityActionDescriptor[];
  /** The merged declaration; `{ name }` when no feature declares the entity. */
  readonly declaration: TmEntityDeclaration;
  /** The tenant's number of content languages. */
  readonly slots: 1 | 2 | 3;
  /**
   * A property by name.
   *
   * @param name - The property's name, in any case.
   * @returns The property, or `undefined`.
   */
  property(name: string): TmPropertyDescriptor | undefined;
  /**
   * A navigation by name.
   *
   * @param name - The navigation's name, in any case.
   * @returns The navigation, or `undefined`.
   */
  navigation(name: string): TmNavigationDescriptor | undefined;
  /**
   * A child collection by name.
   *
   * @param name - The collection's name, in any case.
   * @returns The collection, or `undefined`.
   */
  child(name: string): TmChildDescriptor | undefined;
}

/** What the last segment of a resolved path names. */
export type TmPathEnd =
  | { readonly kind: 'value'; readonly property: TmPropertyDescriptor }
  | { readonly kind: 'text'; readonly slots: readonly TmPropertyDescriptor[] }
  | { readonly kind: 'navigation'; readonly navigation: TmNavigationDescriptor }
  | { readonly kind: 'image' | 'file'; readonly navigation: TmNavigationDescriptor; readonly policy: TmBlobKindPolicy };

/**
 * A column key or Queryex path read against an entity: the navigations it
 * crosses and what its last segment names.
 *
 * @example
 * ```ts
 * const resolution = inject(TmEntities).resolve('gl.Center', 'Parent.Manager');
 * // resolution?.end.kind === 'navigation'; resolution?.fkPath === 'Parent.ManagerId'
 * ```
 */
export interface TmPathResolution {
  /** The entity the path starts from, such as `gl.Center`. */
  readonly root: string;
  /** The path, as given. */
  readonly key: string;
  /** The navigations crossed, in order. */
  readonly steps: readonly TmNavigationDescriptor[];
  /** The entity owning the last segment. */
  readonly entity: string;
  /** What the last segment names: a scalar or a twin, a group anchor, a navigation, or a blob navigation. */
  readonly end: TmPathEnd;
  /** The segments' labels joined with " / ". */
  readonly label: Signal<string>;
  /** For a navigation end, the path of the property holding the id, such as `Parent.ManagerId`. */
  readonly fkPath?: string;
}

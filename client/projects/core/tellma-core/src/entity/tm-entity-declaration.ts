// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmMarkDeclaration } from '../declarations/tm-declaration-registry';
import type { TmNavContext } from '../nav/tm-nav-declarations';

// The entity declaration (spec 0029 §3): what a feature says about an entity
// that the metadata cannot know, such as its launcher entry, its grids'
// default columns and its details layout. The merge rule treats it as one
// unit keyed by the entity's name. The launcher members are typed here; the
// search, details and picker members are typed by their page frameworks, and
// until then pass through as they are written.

/**
 * What a feature declares about one entity. Every member is optional; a
 * declaration with only `name` still names the entity.
 *
 * @example
 * ```ts
 * const input: TmEntityDeclarationInput = {
 *   section: 'sales', order: 10, icon: 'users',
 *   details: () => import('./customer-details'),
 * };
 * ```
 */
export interface TmEntityDeclarationInput {
  /** The launcher section of the entity's entry; absent, the entry is found by search alone. */
  readonly section?: string;
  /** The entry's place within its section. */
  readonly order?: number;
  /** A registered icon name for the entry and the entity's pages. */
  readonly icon?: string;
  /** The string key of the entry's comma-separated search synonyms. */
  readonly synonymsKey?: string;
  /** Hides the launcher entry when it returns `false`. */
  readonly visibleWhen?: (context: TmNavContext) => boolean;
  /** The members the page frameworks read: columns, details, picker and the rest. */
  readonly [member: string]: unknown;
}

/**
 * One entity's declaration: its name, and the members a feature set. It is
 * frozen, and the merge rule patches it member by member.
 */
export interface TmEntityDeclaration {
  /** The entity's name, such as `acme.Customer`. */
  readonly name: string;
  /** The members the feature set. */
  readonly [member: string]: unknown;
}

/**
 * Declares an entity: its launcher entry, its default grids, its details
 * layout and its hatches. Listed in a feature's `entities`, it adds the
 * entity; listed in a later feature's `overrides`, it patches the earlier
 * declaration member by member.
 *
 * @param name - The entity's name, as the metadata spells it: `acme.Customer`.
 * @param declaration - The members to set.
 * @returns The frozen declaration.
 *
 * @example
 * ```ts
 * export const customers = tmEntity('acme.Customer', {
 *   section: 'sales', order: 10, icon: 'users',
 *   details: () => import('./customer-details'),
 * });
 * ```
 */
export function tmEntity(name: string, declaration?: TmEntityDeclarationInput): TmEntityDeclaration {
  return tmMarkDeclaration(Object.freeze({ ...declaration, name }), 'entity');
}

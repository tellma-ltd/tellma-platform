// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmEntityDeclaration } from '../entity/tm-entity-declaration';
import type { TmCodedReportDeclaration } from '../reports/tm-coded-report';
import type { TmWidgetTypeDeclaration } from '../dashboards/tm-widget';
import { tmMarkDeclaration } from './tm-declaration-registry';
import type { TmDeclarationKey } from './tm-declaration-types';
import type { TmPageDeclaration } from './tm-page';

// `tmOverride` (spec 0024 §2.3): how a later feature patches an earlier
// section, entry, settings tab, app-bar slot or page that it has no
// declaration value for. Entities, pages, coded reports and widget types are
// patched with their own declaration values instead.

/** A patch of an earlier client declaration, by its kind-qualified key. */
export interface TmOverride {
  /** The declaration to patch. */
  readonly key: TmDeclarationKey;
  /** The members to set; each replaces the earlier declaration's member whole. */
  readonly members: Readonly<Record<string, unknown>>;
}

/**
 * What a feature's `overrides` list holds: an entity, page, coded-report or
 * widget declaration, which patches the earlier declaration of its own key,
 * or a `tmOverride`.
 */
export type TmDeclarationPatch =
  | TmEntityDeclaration
  | TmPageDeclaration
  | TmCodedReportDeclaration
  | TmWidgetTypeDeclaration
  | TmOverride;

/**
 * Patches an earlier feature's declaration member by member: each member
 * given replaces the earlier one whole, arrays included, and every other
 * member is inherited. The key itself cannot change.
 *
 * @param key - The declaration to patch, such as `section:administration`.
 * @param members - The members to set.
 * @returns The frozen patch, for a feature's `overrides`.
 *
 * @example
 * ```ts
 * overrides: [tmOverride('section:administration', { order: 10 })],
 * ```
 */
export function tmOverride(key: TmDeclarationKey, members: Readonly<Record<string, unknown>>): TmOverride {
  return tmMarkDeclaration(Object.freeze({ key, members: Object.freeze({ ...members }) }), 'override');
}

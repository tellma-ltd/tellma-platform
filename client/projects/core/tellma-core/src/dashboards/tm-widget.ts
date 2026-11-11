// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmMarkDeclaration } from '../declarations/tm-declaration-registry';
import type { TmLazyComponent, TmSecurableRef } from '../declarations/tm-declaration-types';

// The client half of a dashboard widget type (spec 0035 §4.3). The server
// registers each type's key and validates its settings; the client declares
// how the type renders. The build fails when the two sides disagree.

/** A tile's size on the dashboard grid. */
export interface TmWidgetSize {
  /** The width, in columns from 1 to 12. */
  readonly w: number;
  /** The height, in rows from 1 to 24. */
  readonly h: number;
}

/** How a widget type renders and how the gallery offers it. */
export interface TmWidgetDefinition {
  /** The string key of the gallery entry and the default tile title. */
  readonly labelKey: string;
  /** The string key of the gallery entry's second line. */
  readonly descriptionKey?: string;
  /** A registered icon name. */
  readonly icon: string;
  /** The tile's body. */
  readonly component: TmLazyComponent;
  /** The settings editor, in edit mode; absent when the type has no settings. */
  readonly settingsEditor?: TmLazyComponent;
  /** The size of a newly added tile. */
  readonly defaultSize: TmWidgetSize;
  /** The smallest size the editor allows. */
  readonly minSize: TmWidgetSize;
  /** The permission the tile requires; without it the tile is denied and absent from the gallery. */
  readonly requires?: TmSecurableRef;
  /** Whether the tile shows the status line; default `false`. */
  readonly status?: boolean;
}

/**
 * One widget type's declaration: its key, and the members a feature set. It
 * is frozen, and the merge rule patches it member by member.
 */
export interface TmWidgetTypeDeclaration {
  /** The widget type's key, such as `acme.cash-position`. */
  readonly key: string;
  /** The members the feature set. */
  readonly [member: string]: unknown;
}

/**
 * Declares how the widget type the server registers under `key` renders.
 * Listed in a feature's `widgets`, it adds the type; listed in a later
 * feature's `overrides`, it patches the earlier declaration member by member.
 *
 * @param key - The widget type's key, as the server registers it.
 * @param definition - How the type renders.
 * @returns The frozen declaration.
 *
 * @example
 * ```ts
 * export const cashPosition = tmWidget('acme.cash-position', {
 *   labelKey: 'Acme_Widget_CashPosition', icon: 'wallet', status: true,
 *   component: () => import('./cash-position'),
 *   defaultSize: { w: 4, h: 4 }, minSize: { w: 3, h: 3 },
 * });
 * ```
 */
export function tmWidget(key: string, definition: TmWidgetDefinition): TmWidgetTypeDeclaration {
  return tmMarkDeclaration(Object.freeze({ ...definition, key }), 'widget');
}

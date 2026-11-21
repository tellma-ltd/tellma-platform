// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Type } from '@angular/core';

// The small types every declaration shares (spec 0024 §2.2): the lazy
// component a hatch loads, the permission a declaration names, and the
// kind-qualified key through which `hide` and `tmOverride` name an earlier
// declaration.

/**
 * A component that loads on demand: a function returning a dynamic import of
 * the component's module. The module may export the component as its default
 * export or the function may pick a named export.
 *
 * Declarations only hold these functions; the platform calls one when the
 * page, panel or tile it belongs to first renders.
 *
 * @example
 * ```ts
 * const component: TmLazyComponent = () => import('./credit/credit-review-page');
 * const named: TmLazyComponent = () => import('./centers').then((m) => m.CentersMap);
 * ```
 */
export type TmLazyComponent = () => Promise<Type<unknown> | { default: Type<unknown> }>;

/**
 * A permission a declaration names: a securable resource and one of its
 * actions, as the server's permission model spells them.
 *
 * @example
 * ```ts
 * const securable: TmSecurableRef = ['acme.Customer', 'Read'];
 * ```
 */
export type TmSecurableRef = readonly [resource: string, action: string];

/**
 * The key through which a later feature names an earlier client declaration,
 * qualified by its kind: a page by its path, a section or entry by its id, a
 * settings tab by its category and an app-bar slot by its id.
 *
 * Entities, widget types and coded reports are server facts, so they have no
 * such key: the server decides which exist.
 *
 * @example
 * ```ts
 * const keys: readonly TmDeclarationKey[] = ['entry:core.Role', 'section:administration', 'page:credit-review'];
 * ```
 */
export type TmDeclarationKey = `${'page' | 'section' | 'entry' | 'settingsTab' | 'appBar'}:${string}`;

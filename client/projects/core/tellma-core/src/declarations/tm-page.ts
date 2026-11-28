// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmRouteSkeleton } from '../app/tm-route-data';
import type { TmPageKind } from '../context/tm-page-kind';
import type { TmNavContext } from '../nav/tm-nav-declarations';
import { tmMarkDeclaration } from './tm-declaration-registry';
import type { TmLazyComponent, TmSecurableRef } from './tm-declaration-types';

// `tmPage` (spec 0024 §2.4): a page with no entity behind it. The platform
// gives it a route under the tenant, a launcher entry when its path has no
// parameter, a title, a page context and its permission gate.

/** What `tmPage` declares about a page, beside its path. */
export interface TmPageDefinition {
  /** The page's component, loaded when the route first renders; parameters reach it as inputs. */
  readonly component: TmLazyComponent;
  /** The string key of the page's title and launcher label. */
  readonly titleKey: string;
  /** The launcher section of the page's entry; a page with a path parameter takes none. */
  readonly section?: string;
  /** The entry's place within its section. */
  readonly order?: number;
  /** A registered icon name for the entry. */
  readonly icon?: string;
  /** The string key of the entry's comma-separated search synonyms. */
  readonly synonymsKey?: string;
  /** Hides the launcher entry when it returns `false`. */
  readonly visibleWhen?: (context: TmNavContext) => boolean;
  /** The permission the page requires; without it the route renders the `forbidden` error view in place. */
  readonly securable?: TmSecurableRef;
  /** The page's family; default `custom`. */
  readonly kind?: TmPageKind;
  /** The skeleton the route draws before the page loads; default `blank`. */
  readonly skeleton?: TmRouteSkeleton;
}

/** A page declaration: its path under the tenant, and what `tmPage` declared about it. */
export interface TmPageDeclaration extends TmPageDefinition {
  /**
   * The page's path under `/{tenantId}/`: `/`-separated kebab-case literals
   * and `:name` parameters, starting with a literal.
   */
  readonly path: string;
}

/**
 * Declares a page with no entity behind it. Its route is `/{tenantId}/{path}`;
 * each `:name` segment of the path reaches the component as the input `name`.
 * Listed in a feature's `pages`, it adds the page; listed in a later
 * feature's `overrides`, it patches the earlier declaration member by member.
 *
 * The path's grammar is checked with the rest of the configuration: kebab-case
 * literal segments and camelCase `:name` parameters, a literal first segment
 * that is no reserved word, and no segment `new`.
 *
 * @param path - The page's path, such as `credit-review` or `reports/:id/studio`.
 * @param page - What to declare about the page.
 * @returns The frozen declaration.
 *
 * @example
 * ```ts
 * export const creditReview = tmPage('credit-review', {
 *   component: () => import('./credit/credit-review-page'),
 *   titleKey: 'Acme_CreditReview', section: 'financials',
 * });
 * ```
 */
export function tmPage(path: string, page: TmPageDefinition): TmPageDeclaration {
  return tmMarkDeclaration(Object.freeze({ ...page, path }), 'page');
}

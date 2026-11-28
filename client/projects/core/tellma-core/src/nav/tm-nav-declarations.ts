// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmSecurableRef } from '../declarations/tm-declaration-types';
import type { TmDistributionInfo } from '../state/tm-distribution-info';

// The launcher's declarations (spec 0027 §3.1): the sections a feature adds,
// and the entries beyond those its entities, pages and coded reports derive.
// The navigation registry turns the merged declarations into the sections and
// entries a user sees.

/**
 * What a `visibleWhen` predicate reads: the facts that decide whether a
 * launcher entry shows, beyond its permissions.
 */
export interface TmNavContext {
  /** The distribution information document. */
  readonly distribution: TmDistributionInfo;
}

/**
 * A launcher section: a titled group of entries.
 *
 * @example
 * ```ts
 * const sales: TmNavSection = { id: 'sales', labelKey: 'Acme_Nav_Sales', icon: 'users', order: 20 };
 * ```
 */
export interface TmNavSection {
  /** The section's id, unique across every feature; entries name it in their `section`. */
  readonly id: string;
  /** The string key of the section's title. */
  readonly labelKey: string;
  /** A registered icon name. */
  readonly icon: string;
  /** The section's place in the launcher: distributions and packs use 1 to 700; the platform's sections close it. */
  readonly order: number;
}

/**
 * A launcher entry beyond the ones entities, pages and coded reports derive:
 * a link to any URL in the tenant.
 *
 * @example
 * ```ts
 * const overdue: TmNavEntry = {
 *   id: 'acme.overdue-customers', section: 'sales', labelKey: 'Acme_Nav_OverdueCustomers',
 *   url: (t) => `/${t}/customers?tab=overdue`, requires: ['acme.Customer', 'Read'],
 * };
 * ```
 */
export interface TmNavEntry {
  /** The entry's id, unique across every feature's entries and the entries the platform derives. */
  readonly id: string;
  /** The section the entry belongs to; absent, the entry is found by search alone. */
  readonly section?: string;
  /** The string key of the entry's label. */
  readonly labelKey: string;
  /** The entry's canonical path in tenant `t`. */
  readonly url: (t: number) => string;
  /** A registered icon name. */
  readonly icon?: string;
  /** The entry's place within its section; ties order by the localized label. */
  readonly order?: number;
  /** The string key of the entry's comma-separated search synonyms. */
  readonly synonymsKey?: string;
  /** The permissions that show the entry: it shows when any one passes. */
  readonly requires?: TmSecurableRef | readonly TmSecurableRef[];
  /** Hides the entry when it returns `false`; navigation only, the route stays. */
  readonly visibleWhen?: (context: TmNavContext) => boolean;
  /** Reserved for the help drawer. */
  readonly helpKey?: string;
}

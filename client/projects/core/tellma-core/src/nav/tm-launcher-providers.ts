// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { InjectionToken } from '@angular/core';

// The launcher's provider seam (spec 0027 §9.6). Global search lists the
// matching pages at once and the providers' buckets below them: records,
// settings or help, each a later feature's. A feature registers a provider in
// its `providers` without importing the shell; the launcher calls each one
// on its search output, aborts a call the next keystroke supersedes, and
// appends each answer's bucket in `order` as it arrives.

/** One result row of a provider's bucket. */
export interface TmLauncherItem {
  /** The row's id, unique within its provider. */
  readonly id: string;
  /** A registered icon name. */
  readonly icon?: string;
  /** The page the row opens, as `TmUrls` builds it: `/17/sales-invoices/431`. */
  readonly href: string;
  /** The row's label, localized: `INV-2026-0431`. */
  readonly label: string;
  /** A muted description, localized: `Rawabi Trading · AED 12,400`. */
  readonly description?: string;
}

/** What a provider's search knows besides the query. */
export interface TmLauncherSearchContext {
  /** The routed tenant. */
  readonly tenantId: number;
  /** The active language, such as `ar`. */
  readonly language: string;
  /** The most rows the bucket shows: 5. */
  readonly limit: number;
  /** Aborted by the next keystroke, the launcher's closing or a navigation. */
  readonly signal: AbortSignal;
}

/**
 * A source of launcher results other than pages: one bucket under its own
 * heading, below Pages.
 *
 * @example
 * ```ts
 * class InvoiceSearch implements TmLauncherProvider {
 *   readonly id = 'acme-invoices';
 *   readonly labelKey = 'Acme_Launcher_Invoices';
 *   readonly order = 10;
 *   async search(query: string, context: TmLauncherSearchContext): Promise<readonly TmLauncherItem[]> {
 *     const rows = await findInvoices(query, context.limit, context.signal);
 *     return rows.map((row) => ({ id: String(row.id), href: `/${context.tenantId}/sales-invoices/${row.id}`, label: row.code }));
 *   }
 * }
 * ```
 */
export interface TmLauncherProvider {
  /** The provider's id, kebab-case: `records`, `settings`, `help`. */
  readonly id: string;
  /** The string key of the bucket's heading. */
  readonly labelKey: string;
  /** The bucket's place below Pages, which is 0; greater than 0. */
  readonly order: number;
  /** The shortest query, in normalized characters, the provider is asked about; default 2. */
  readonly minLength?: number;
  /**
   * Finds the rows that match a query.
   *
   * @param query - The query as typed.
   * @param context - The tenant, the language, the row limit and the abort signal.
   * @returns The rows; an empty list or a rejection renders no bucket.
   */
  search(query: string, context: TmLauncherSearchContext): Promise<readonly TmLauncherItem[]>;
}

/**
 * The launcher's providers: a multi token, which features provide in their
 * `providers`. Without one, the launcher lists pages alone.
 *
 * @example
 * ```ts
 * tmFeature({ key: 'acme', providers: [{ provide: TM_LAUNCHER_PROVIDERS, multi: true, useClass: InvoiceSearch }] });
 * ```
 */
export const TM_LAUNCHER_PROVIDERS = new InjectionToken<readonly TmLauncherProvider[]>('TM_LAUNCHER_PROVIDERS', {
  providedIn: 'root',
  factory: () => [],
});

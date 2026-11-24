// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmMembership, TmTenantState } from '@tellma/core';
import { tmSearchNormalize } from '@tellma/core/search-normalize';

// The tenant picker's rows (spec 0027 §6.2), as pure functions of the
// memberships, the active language and the last tenant: each row's names in
// their languages, its badges and the reason its Connect is unavailable; the
// order, live tenants first with their sandboxes beneath; and the search,
// which keeps a matching sandbox's live tenant as a dimmed row.

/** The membership fields that hold a tenant's names, by the position of their content language. */
const NAME_SLOTS = ['name', 'name2', 'name3'] as const satisfies readonly (keyof TmMembership)[];

/** The badge each lifecycle state shows; an active tenant shows none. */
const STATE_BADGES: Readonly<Partial<Record<TmTenantState, TmTenantBadge>>> = {
  ReadOnly: 'readOnly',
  Frozen: 'frozen',
  Suspended: 'suspended',
  Provisioning: 'provisioning',
};

/** A badge of a picker row: its string key under `tm.tenantless.picker.badges`. */
export type TmTenantBadge = 'lastUsed' | 'sandbox' | 'readOnly' | 'frozen' | 'suspended' | 'provisioning' | 'inactive';

/** Why a row's Connect is unavailable: its string key under `tm.tenantless.picker.unavailable`. */
export type TmTenantUnavailable = 'inactive' | 'suspended' | 'provisioning';

/** One of a tenant's names, with the language it is written in. */
export interface TmTenantName {
  /** The name. */
  readonly text: string;
  /** Its content language, such as `ar`; `null` when the tenant lists none for it. */
  readonly lang: string | null;
}

/** One row of the picker. */
export interface TmTenantRow {
  /** The membership. */
  readonly membership: TmMembership;
  /** The name in the active language, else the first name. */
  readonly name: TmTenantName;
  /** The other non-empty names, in the tenant's language order. */
  readonly others: readonly TmTenantName[];
  /** Whether the row is a sandbox listed beneath its live tenant, so it is indented. */
  readonly nested: boolean;
  /** The badges, in display order. */
  readonly badges: readonly TmTenantBadge[];
  /** Why Connect is unavailable, or `null` when the tenant can be opened. */
  readonly unavailable: TmTenantUnavailable | null;
  /** The names, each folded for search. */
  readonly keys: readonly string[];
}

/** A row as the search shows it. */
export interface TmTenantShownRow {
  /** The row. */
  readonly row: TmTenantRow;
  /** Whether the row is shown only as a matching sandbox's live tenant. */
  readonly dimmed: boolean;
}

/** What a search shows. */
export interface TmTenantSearch {
  /** The rows to show, in order. */
  readonly shown: readonly TmTenantShownRow[];
  /** The rows that match, in order; every row while nothing is typed. */
  readonly matches: readonly TmTenantRow[];
}

/** A language tag's language subtag, lowercase: `ar` for `ar-SA`. */
function subtag(code: string): string {
  return code.split('-', 1)[0].toLowerCase();
}

/**
 * A tenant's name in the active language, and its other names. The active
 * language's position among the tenant's content languages, matched by
 * language subtag, picks `name`, `name2` or `name3`; without a match, or
 * when that name is empty, the first name shows.
 *
 * @param membership - The membership.
 * @param language - The active language, such as `ar`.
 * @returns The name to show, and the other non-empty names in language order.
 */
export function tmTenantNames(
  membership: TmMembership,
  language: string,
): { readonly name: TmTenantName; readonly others: readonly TmTenantName[] } {
  const names = NAME_SLOTS.map((slot, index) => ({ text: (membership[slot] ?? '').trim(), lang: membership.languages[index] ?? null }));
  const wanted = subtag(language);
  const at = membership.languages.findIndex((code) => subtag(code) === wanted);
  const index = at >= 0 && at < names.length && names[at].text !== '' ? at : 0;
  return { name: names[index], others: names.filter((name, other) => other !== index && name.text !== '') };
}

/**
 * Why a tenant cannot be opened: the membership was deactivated, the
 * tenant is suspended, or it is being set up, in that order.
 *
 * @param membership - The membership.
 * @returns The reason, or `null` when Connect is available.
 */
export function tmTenantUnavailable(membership: TmMembership): TmTenantUnavailable | null {
  if (!membership.isActive) {
    return 'inactive';
  }
  return membership.state === 'Suspended' ? 'suspended' : membership.state === 'Provisioning' ? 'provisioning' : null;
}

/**
 * The picker's rows: live tenants by name, each followed by its own
 * sandboxes by name, then the sandboxes whose live tenant is not listed.
 * Recency never moves a row. A retired tenant, which the server does not
 * list, is left out.
 *
 * @param memberships - The user's memberships.
 * @param language - The active language, which picks the names and their collation.
 * @param lastTenantId - The tenant the user last opened, which shows Last used.
 * @returns The rows, in display order.
 */
export function tmTenantRows(memberships: readonly TmMembership[], language: string, lastTenantId: number | null): TmTenantRow[] {
  const listed = memberships.filter((membership) => membership.state !== 'Retired');
  const liveIds = new Set(listed.filter((membership) => membership.category === 'Live').map((membership) => membership.id));
  const rows = listed.map((membership): TmTenantRow => {
    const { name, others } = tmTenantNames(membership, language);
    const state = STATE_BADGES[membership.state];
    return {
      membership,
      name,
      others,
      nested: membership.category === 'Sandbox' && membership.liveTenantId !== null && liveIds.has(membership.liveTenantId),
      badges: [
        ...(membership.id === lastTenantId ? (['lastUsed'] as const) : []),
        ...(membership.category === 'Sandbox' ? (['sandbox'] as const) : []),
        ...(state === undefined ? [] : [state]),
        ...(membership.isActive ? [] : (['inactive'] as const)),
      ],
      unavailable: tmTenantUnavailable(membership),
      keys: [name, ...others].map((each) => tmSearchNormalize(each.text)),
    };
  });

  // Section: names in the active language's collation; the id breaks a tie.
  const collator = new Intl.Collator(language);
  rows.sort((a, b) => collator.compare(a.name.text, b.name.text) || a.membership.id - b.membership.id);

  // Section: each live tenant, then its own sandboxes; the unattached sandboxes last.
  const ordered: TmTenantRow[] = [];
  for (const live of rows.filter((row) => row.membership.category === 'Live')) {
    ordered.push(live, ...rows.filter((row) => row.nested && row.membership.liveTenantId === live.membership.id));
  }
  ordered.push(...rows.filter((row) => row.membership.category === 'Sandbox' && !row.nested));
  return ordered;
}

/**
 * Searches the rows: a row matches when one of its names, folded, holds the
 * folded text, or when the text is its id. A matching sandbox keeps its
 * live tenant as a dimmed row above it.
 *
 * @param rows - The rows, in display order.
 * @param text - The search text; empty shows every row.
 * @returns The rows to show and the rows that match.
 */
export function tmTenantSearch(rows: readonly TmTenantRow[], text: string): TmTenantSearch {
  const query = tmSearchNormalize(text);
  if (query === '') {
    return { shown: rows.map((row) => ({ row, dimmed: false })), matches: rows };
  }
  const matches = rows.filter((row) => String(row.membership.id) === query || row.keys.some((key) => key.includes(query)));
  const matching = new Set(matches);
  const parents = new Set(matches.filter((row) => row.nested).map((row) => row.membership.liveTenantId));
  return {
    shown: rows
      .filter((row) => matching.has(row) || parents.has(row.membership.id))
      .map((row) => ({ row, dimmed: !matching.has(row) })),
    matches,
  };
}

/**
 * The count of memberships as the picker's telemetry reports it.
 *
 * @param count - The memberships listed.
 * @returns `0`, `1`, `2-6` or `7+`.
 */
export function tmTenantCountBucket(count: number): '0' | '1' | '2-6' | '7+' {
  return count === 0 ? '0' : count === 1 ? '1' : count < 7 ? '2-6' : '7+';
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmMembership } from '@tellma/core';

// A tenant's names (spec 0027 §6.2): the one its row or link shows in the
// active language, and the others. Its own module, because both the tenant
// picker and the tenant shell's marker popovers name tenants, and those load
// in separate chunks: what they share stays this small.

/** The membership fields that hold a tenant's names, by the position of their content language. */
const NAME_SLOTS = ['name', 'name2', 'name3'] as const satisfies readonly (keyof TmMembership)[];

/** One of a tenant's names, with the language it is written in. */
export interface TmTenantName {
  /** The name. */
  readonly text: string;
  /** Its content language, such as `ar`; `null` when the tenant lists none for it. */
  readonly lang: string | null;
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

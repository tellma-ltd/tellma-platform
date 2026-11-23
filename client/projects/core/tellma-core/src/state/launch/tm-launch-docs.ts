// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmAssignmentsSummary, TmInboxSummary } from '../../attention/tm-attention-documents';
import type { TmId, TmJson } from '../../json/tm-json';
import type { TmTenantCategory } from '../../launch/tm-membership';

// The launch documents (spec 0025 §3.1) in their wire shape: what
// `client/sync` answers and the store keeps, camelCase, with every null
// member omitted (spec 0016 §3.1). The settings are spec 0013 §5.6's client
// view, the envelope spec 0016 §3.4's, and the tenant's facts spec 0025
// §5.1's.

/** How a language's digits are written: the Latin digits, or the script's own. */
export type TmDigitStyle = 'Latin' | 'Native';

/** One language a tenant offers. */
export interface TmLanguageInfo {
  /** The language's code, such as `ar`. */
  readonly code: string;
  /** The language's name in itself, such as `العربية`. */
  readonly nativeName: string;
  /** The language's short symbol, such as `ع`. */
  readonly symbol: string;
  /** Whether the language is written right to left. */
  readonly isRightToLeft: boolean;
}

/** The tenant's settings as the client sees them. */
export interface TmTenantSettingsForClient {
  /** The tenant's id. */
  readonly tenantId: number;
  /** The tenant's name in each of its languages, in language order; `null` where it has none. */
  readonly names: readonly (string | null)[];
  /** The languages the tenant offers; the first is its primary language. */
  readonly languages: readonly TmLanguageInfo[];
  /** The calendars the tenant offers, such as `gc` and `uq`; the first is its default. */
  readonly calendars: readonly string[];
  /** The tenant's time zone, an IANA name such as `Asia/Riyadh`. */
  readonly timeZone: string;
  /** The tenant's region, such as `SA`, when it sets one. */
  readonly region?: string;
  /** The tenant's digit style, when it sets one. */
  readonly digits?: TmDigitStyle;
  /** The client-visible settings by key. */
  readonly entries: Readonly<Record<string, TmJson>>;
}

/** The caller's own profile. */
export interface TmUserProfileView {
  /** The user's id. */
  readonly id: number;
  /** Who the user is: a person, the system, or a service account. */
  readonly kind: 'Individual' | 'System' | 'Service';
  /** Where the user is in joining the tenant. */
  readonly state: 'New' | 'Invited' | 'Joined';
  /** The user's name in the tenant's primary language. */
  readonly name: string;
  /** The user's name in the tenant's second language, if any. */
  readonly name2?: string;
  /** The user's name in the tenant's third language, if any. */
  readonly name3?: string;
  /** The user's sign-in email, if any. */
  readonly email?: string;
  /** The user's contact email, if any. */
  readonly contactEmail?: string;
  /** The user's contact mobile number, if any. */
  readonly contactMobile?: string;
  /** The blob id of the user's image, if any. */
  readonly imageId?: number;
  /** The blob id of the user's signature, if any. */
  readonly signatureId?: number;
  /** The user's preferred language, if set. */
  readonly preferredLanguage?: string;
  /** The user's preferred calendar, if set. */
  readonly preferredCalendar?: string;
  /** The user's preferred time zone, if set. */
  readonly preferredTimeZone?: string;
  /** The user's preferred region, if set. */
  readonly preferredRegion?: string;
  /** The user's preferred digit style, if set. */
  readonly preferredDigits?: TmDigitStyle;
  /** The user's gender, if set. */
  readonly gender?: 'Female' | 'Male';
  /** When the user's row last changed, as an ISO 8601 timestamp with its offset. */
  readonly modifiedAt: string;
}

/** One action the caller may take on one securable. */
export interface TmSecurableSummary {
  /** The securable's resource, such as `gl.Center`. */
  readonly resource: string;
  /** The action, such as `Read`. */
  readonly action: string;
  /** Whether a filter limits the rows the action reaches. */
  readonly hasFilter: boolean;
}

/** A permission of the caller's that the server could not apply. */
export interface TmAccessProblem {
  /** The permission's id. */
  readonly permissionId: number;
  /** The role that holds the permission. */
  readonly roleId: number;
  /** The permission's resource. */
  readonly resource: string;
  /** The permission's action. */
  readonly action: string;
  /** Why the permission does not apply, such as `UnknownResource` or `FilterInvalid`. */
  readonly code: string;
  /** The filter's diagnostics, when its filter is at fault. */
  readonly diagnostics: readonly TmJson[];
}

/** What the caller may do in the tenant. */
export interface TmAccessSummary {
  /** The composite `permissions` wire tag the summary was decided under. */
  readonly tag: string;
  /** The summary's format version. */
  readonly formatVersion: number;
  /** Whether the caller is the system user, which may do everything. */
  readonly isSystem: boolean;
  /** Every action the caller may take, one entry per securable and action. */
  readonly securables: readonly TmSecurableSummary[];
  /** The caller's permissions that could not be applied. */
  readonly problems: readonly TmAccessProblem[];
  /**
   * The next instant one of the caller's role memberships opens or closes,
   * as an ISO 8601 timestamp with its offset; absent when none does.
   */
  readonly validUntil?: string;
}

/** The caller's own envelope: profile, preferences and access. */
export interface TmMeResult {
  /** The caller's profile. */
  readonly user: TmUserProfileView;
  /** The `preferences` wire tag the profile and bag were read under. */
  readonly preferencesTag: string;
  /** The caller's preference bag: each key's stored string. */
  readonly preferences: Readonly<Record<string, string>>;
  /** What the caller may do. */
  readonly access: TmAccessSummary;
  /** The four client tags as the server last reported them; never a source of held tags. */
  readonly tags: Readonly<Record<string, string>>;
}

/** The result of setting or deleting the caller's preferences. */
export interface TmPreferencesResult {
  /** The caller's whole preference bag after the write. */
  readonly preferences: Readonly<Record<string, string>>;
  /** The `preferences` wire tag after the write. */
  readonly preferencesTag: string;
}

/** The result of setting the caller's language, calendar, time zone, region or digits. */
export interface TmMeLocaleResult {
  /** The preferred language after the write, if set. */
  readonly preferredLanguage?: string;
  /** The preferred calendar after the write, if set. */
  readonly preferredCalendar?: string;
  /** The preferred time zone after the write, if set. */
  readonly preferredTimeZone?: string;
  /** The preferred region after the write, if set. */
  readonly preferredRegion?: string;
  /** The preferred digit style after the write, if set. */
  readonly preferredDigits?: TmDigitStyle;
  /** When the user's row changed, as an ISO 8601 timestamp with its offset. */
  readonly modifiedAt: string;
  /** The `preferences` wire tag after the write. */
  readonly preferencesTag: string;
}

/** What a tab knows of the tenant it shows. */
export interface TmTenantFacts {
  /** The tenant's id. */
  readonly id: number;
  /** Whether the tenant holds live books or is a sandbox. */
  readonly category: TmTenantCategory;
  /**
   * The tenant's state; only the three that serve calls. `ReadOnly` refuses
   * the writes not allowed when read-only, and `Frozen` every write.
   */
  readonly state: 'Active' | 'ReadOnly' | 'Frozen';
  /** A sandbox's live tenant; absent for a live tenant. */
  readonly liveTenantId?: number;
  /** Whether the server hosts the real-time hub, so the leader tab connects instead of polling. */
  readonly realtime: boolean;
}

/** The documents the chrome renders from. */
export interface TmLaunchDocs {
  /** The tenant's settings. */
  readonly settings: TmTenantSettingsForClient;
  /** The caller's own envelope. */
  readonly me: TmMeResult;
  /** The tenant's facts. */
  readonly tenant: TmTenantFacts;
  /** The user's inbox summary, which the leader tab keeps fresh; absent until it is first read. */
  readonly inbox?: TmInboxSummary;
  /** The user's assignments summary, which the leader tab keeps fresh; absent until it is first read. */
  readonly assignments?: TmAssignmentsSummary;
}

/** A cacheable entity's whole list, as the tab holds it. */
export interface TmCachedEntitySet<T = TmJson> {
  /** Every row, in the server's order. */
  readonly items: readonly T[];
  /** Each row by its id. */
  readonly byId: ReadonlyMap<TmId, T>;
  /** Whether a newer list is due; the rows still serve until it lands. */
  readonly stale: boolean;
}

/**
 * The stored `sync` document: when the last sync was sent, the composite
 * `entities` tag it held, and each cacheable list's current wire tag by
 * entity name. Internal.
 */
export interface TmSyncDoc {
  /** When the last sync was sent, in epoch milliseconds. */
  readonly at: number;
  /** The composite `entities` wire tag, or `null` when the next sync must report every list's tag. */
  readonly entities: string | null;
  /** Each cacheable list's current wire tag, by entity name. */
  readonly entityTags: Readonly<Record<string, string>>;
}

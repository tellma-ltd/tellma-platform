// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmIsText, tmParseJsonObject, tmReadItem, tmWriteItem, type TmStorageLike } from './tm-storage-json';

// The boot record (spec 0025 §2.1): `localStorage['tm.boot']`, about 1 KB,
// shared by the boot script and the client store. Its device fields (theme,
// language, direction) let the boot script paint the first frame right; its
// user fields (whose data the store holds, and the last tenant) decide warm
// or cold and where an expired session returns. Plain code with no Angular
// import: the boot script bundles it too.

/** The local-storage key of the boot record. */
export const TM_BOOT_RECORD_KEY = 'tm.boot';

/** The theme a device keeps: a fixed scheme, or the system's. */
export type TmBootTheme = 'system' | 'light' | 'dark';

/** The boot record: the few facts the boot script needs before the app runs. */
export interface TmBootRecord {
  /** The record's version; always 1. */
  readonly v: 1;
  /** The device's theme. */
  readonly theme: TmBootTheme;
  /** The device's language, such as `ar`. */
  readonly lang: string;
  /** The text direction of `lang`. */
  readonly dir: 'ltr' | 'rtl';
  /** The user whose data the store holds, or `null`; cleared at sign-out and after an expiry. */
  readonly sub: string | null;
  /** The tenant the user last entered, or `null`; cleared with `sub`. */
  readonly lastTenantId: number | null;
}

/**
 * Whether a value is a tenant id: a positive integer that a number holds
 * exactly.
 *
 * @param value - The value.
 * @returns Whether it is a tenant id.
 */
export function tmIsTenantId(value: unknown): value is number {
  return typeof value === 'number' && value > 0 && Number.isSafeInteger(value);
}

/**
 * Parses a stored boot record, or `null` when the text is absent, is not
 * JSON, is not version 1, or has a member of the wrong shape.
 *
 * @param text - The stored text, or `null`.
 * @returns The record, or `null`.
 */
export function tmParseBootRecord(text: string | null): TmBootRecord | null {
  const record = tmParseJsonObject(text);
  return isBootRecord(record) ? record : null;
}

/** Whether a stored value is a well-formed boot record, member by member. */
function isBootRecord(value: object | null): value is TmBootRecord {
  const { v, theme, lang, dir, sub, lastTenantId } = (value || {}) as Readonly<Record<string, unknown>>;
  return (
    v === 1 &&
    (theme === 'system' || theme === 'light' || theme === 'dark') &&
    tmIsText(lang) &&
    (dir === 'ltr' || dir === 'rtl') &&
    (sub === null || tmIsText(sub)) &&
    (lastTenantId === null || tmIsTenantId(lastTenantId))
  );
}

/**
 * Reads the boot record, or `null` when it is absent, invalid, or the storage
 * refuses.
 *
 * @param storage - The device's local storage, or `null` when it is unavailable.
 * @returns The record, or `null`.
 */
export function tmReadBootRecord(storage: TmStorageLike | null): TmBootRecord | null {
  return tmParseBootRecord(tmReadItem(storage, TM_BOOT_RECORD_KEY));
}

/**
 * Writes the boot record; a refusing storage is ignored, since the record is
 * only a cache of choices the app can make again.
 *
 * @param storage - The device's local storage, or `null` when it is unavailable.
 * @param record - The record.
 */
export function tmWriteBootRecord(storage: TmStorageLike | null, record: TmBootRecord): void {
  tmWriteItem(storage, TM_BOOT_RECORD_KEY, record);
}

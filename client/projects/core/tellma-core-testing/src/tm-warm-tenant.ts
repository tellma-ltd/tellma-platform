// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmClientStore, TmLaunchDocs, TmPartition, ɵTmSyncDoc } from '@tellma/core';

// A warm tenant in the store. The store reads every tenant document as
// absent once the tenant's last sync is past the idle window, so stored
// launch documents alone make a cold entry; a recent `sync` stamp beside them
// makes the entry warm, as after a launch that synced a moment ago.

/** What {@link tmWarmTenant} stamps beside the documents. */
export interface TmWarmTenantOptions {
  /** When the tenant last synced, in epoch milliseconds; now by default. */
  readonly at?: number;
  /** Each cacheable list's current wire tag, by entity name; none by default. */
  readonly entityTags?: Readonly<Record<string, string>>;
}

/**
 * Stores a tenant's launch documents and a recent sync, so the tenant's
 * entry is warm: its chrome and pages render from the store with no call to
 * the server. The sync's composite `entities` tag is the envelope's.
 *
 * @param store - The client store, such as `provideTellmaStateTesting()` provides.
 * @param partition - The tenant's partition.
 * @param docs - The tenant's `settings`, `me` and `tenant` documents, and the
 * `inbox` summary a test gives, which needs `TM_ATTENTION_DOCUMENTS`
 * registered as `TM_LEADER_DOCUMENTS`, as `provideTellma` registers them.
 * @param options - The sync's time and list tags.
 * @returns A promise that settles once every document is stored.
 *
 * @example
 * ```ts
 * TestBed.configureTestingModule({ providers: [provideTellmaTesting(), provideTellmaStateTesting()] });
 * await tmWarmTenant(TestBed.inject(TmClientStore), { kind: 'tenant', sub: tmTestProfile().sub, tenantId: 17 }, { settings, me, tenant });
 * ```
 */
export async function tmWarmTenant(
  store: TmClientStore,
  partition: Extract<TmPartition, { readonly kind: 'tenant' }>,
  docs: TmLaunchDocs,
  options: TmWarmTenantOptions = {},
): Promise<void> {
  const at = options.at ?? Date.now();
  const sync: ɵTmSyncDoc = { at, entities: docs.me.tags['entities'] ?? null, entityTags: options.entityTags ?? {} };
  // The documents first, then the stamp that makes them current.
  for (const name of ['settings', 'me', 'tenant', 'inbox'] as const) {
    // The inbox is stored only when the test gives it.
    if (docs[name] !== undefined) {
      await store.put(partition, name, { tag: null, savedAt: at, data: docs[name] });
    }
  }
  await store.put(partition, 'sync', { tag: null, savedAt: at, data: sync });
}

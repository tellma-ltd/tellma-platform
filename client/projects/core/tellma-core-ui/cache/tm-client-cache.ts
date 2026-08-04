// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injectable } from '@angular/core';

/**
 * The global cleanup hook for the library's persistent client-side state.
 *
 * Convention replaces registration: every browser store a library feature
 * creates is named with a `tm-` prefix (today: the image blob cache in
 * CacheStorage; the same convention will bind any future IndexedDB
 * database or web-storage key), so there is nothing to register, no
 * init-order dependency, and state left by features that never loaded this
 * session — or by prior sessions — is swept all the same.
 *
 * A distribution calls {@link clearAll} on logout. Tenant switching clears
 * nothing: cache keys carry tenant identity by construction (the tenant id
 * is part of every API URL), so entries are tenant-scoped already. Library
 * features treat their caches as re-populatable accelerators — clearing
 * mid-flight is always safe.
 */
@Injectable({ providedIn: 'root' })
export class TmClientCache {
  /**
   * Deletes every `tm-`-prefixed browser store — best-effort, in parallel,
   * never throws. A no-op where the Cache API is unavailable (insecure
   * context, restrictive browser profile).
   */
  async clearAll(): Promise<void> {
    try {
      if (typeof caches === 'undefined') {
        return;
      }
      const keys = await caches.keys();
      await Promise.allSettled(
        keys.filter((key) => key.startsWith('tm-')).map((key) => caches.delete(key)),
      );
    } catch {
      // Best-effort by contract: a failure to enumerate or delete must
      // never break the caller's logout flow.
    }
  }
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, inject, Injectable, signal, type Signal } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';

import { TM_BOOT_RESULT } from '../boot/tm-boot-result';

// The launch mark (spec 0024 §9.3): the tenant this document entered without
// the user choosing it. The boot script seeds it (`__tmBoot.auto`), and the
// launch redirect sets it when it replaces `/` with a tenant. It ends at the
// first 2xx from that tenant, at a route into another tenant, or when a
// `404 tenant-not-found` takes it, so a tenant that no longer exists leads to
// the picker silently only on an entry the user did not choose.

/** The tenant segment that opens an app path, such as `17` in `/17/centers`. */
const TENANT_PATH = /^\/([1-9]\d*)(?:[/?#]|$)/;

/**
 * The tenant an app URL routes into, or `null` for a tenantless one.
 *
 * @param url - The router's URL, such as `/17/centers?x=1`.
 */
function routedTenant(url: string): number | null {
  const match = TENANT_PATH.exec(url);
  return match ? Number(match[1]) : null;
}

/**
 * The launch mark: the tenant this document entered without the user
 * choosing it, until that entry proves itself or the user goes elsewhere.
 *
 * @example
 * ```ts
 * // On a 404 tenant-not-found from tenant 17:
 * const silent = inject(TmLaunch).takeAutomatic(17);
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmLaunch {
  readonly #automatic = signal<number | null>(inject(TM_BOOT_RESULT)?.auto ?? null);

  /** The tenant this document entered without the user choosing it, or `null`. */
  readonly automatic: Signal<number | null> = this.#automatic.asReadonly();

  /** Creates the mark and ends it at the first route into another tenant. */
  constructor() {
    const router = inject(Router);
    const subscription = router.events.subscribe((event) => {
      if (event instanceof NavigationEnd) {
        this.#leaveOthers(event.urlAfterRedirects);
      }
    });
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
    // A navigation that ended before the first injection counts too.
    if (router.navigated) {
      this.#leaveOthers(router.url);
    }
  }

  /**
   * Marks the tenant the launch redirect picked for `/`.
   *
   * @param tenantId - The tenant.
   */
  markAutomatic(tenantId: number): void {
    this.#automatic.set(tenantId);
  }

  /**
   * Ends the mark once the tenant answered with a 2xx: the entry proved
   * itself.
   *
   * @param tenantId - The tenant that answered.
   */
  settle(tenantId: number): void {
    if (this.#automatic() === tenantId) {
      this.#automatic.set(null);
    }
  }

  /**
   * Takes the mark on a `404 tenant-not-found`: `true` once when the tenant
   * was entered automatically, which ends the mark; `false` otherwise.
   *
   * @param tenantId - The tenant that was not found.
   * @returns Whether the entry was automatic.
   */
  takeAutomatic(tenantId: number): boolean {
    if (this.#automatic() !== tenantId) {
      return false;
    }
    this.#automatic.set(null);
    return true;
  }

  /** Ends the mark when a route went into a tenant other than the marked one. */
  #leaveOthers(url: string): void {
    const tenant = routedTenant(url);
    if (tenant !== null && tenant !== this.#automatic()) {
      this.#automatic.set(null);
    }
  }
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The routed tenant (spec 0024 §9.4): the tenant segment that opens an app
// path. The launch mark ends at a route into another tenant, and the HTTP
// layer sends a tenant call to the routed tenant when the caller names none.

/** The tenant segment that opens an app path, such as `17` in `/17/centers`. */
const TENANT_PATH = /^\/([1-9]\d*)(?:[/?#]|$)/;

/**
 * The tenant an app URL routes into, or `null` for a tenantless one.
 *
 * @param url - The router's URL, such as `/17/centers?x=1`.
 * @returns The tenant's id, or `null`.
 */
export function tmRoutedTenant(url: string): number | null {
  const match = TENANT_PATH.exec(url);
  return match ? Number(match[1]) : null;
}

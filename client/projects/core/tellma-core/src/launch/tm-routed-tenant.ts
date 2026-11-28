// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The routed tenant (spec 0024 §9.4): the tenant segment that opens an app
// path. The launch mark ends at a route into another tenant, and the HTTP
// layer sends a tenant call to the routed tenant when the caller names none.
// The segment rules live here, beside it and free of imports, so the route
// table, the URL service and every reader of the routed tenant share one
// rule without an import cycle.

/** A positive integer without leading zeros: a tenant or a record id. */
const POSITIVE = /^[1-9]\d*$/;

/** The first segment of an app path, such as `17` in `/17/centers?x=1`. */
const FIRST_SEGMENT = /^\/([^/?#]*)/;

/**
 * The tenant a path segment names: a positive safe integer without leading
 * zeros, so `017` and `0` name none.
 *
 * @param segment - The path segment.
 * @returns The tenant's id, or `null`.
 */
export function tmTenantSegment(segment: string | undefined): number | null {
  const id = Number(segment);
  return segment !== undefined && POSITIVE.test(segment) && Number.isSafeInteger(id) ? id : null;
}

/**
 * Whether a path segment is a record id: a positive integer without leading
 * zeros. A `long` key's id may exceed the safe integers, so it stays text.
 *
 * @param segment - The path segment.
 * @returns Whether it is an id.
 */
export function tmIsIdSegment(segment: string | undefined): boolean {
  return segment !== undefined && POSITIVE.test(segment);
}

/**
 * The tenant an app URL routes into, or `null` for a tenantless one: its
 * first segment, by the route table's own tenant rule.
 *
 * @param url - The router's URL, such as `/17/centers?x=1`.
 * @returns The tenant's id, or `null`.
 */
export function tmRoutedTenant(url: string): number | null {
  return tmTenantSegment(FIRST_SEGMENT.exec(url)?.[1]);
}

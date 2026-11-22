// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmRouteMatch } from '../urls/tm-urls';

// One table of URLs over the route rig's declarations, with what the router
// activates for each and what `TmUrls.describe` answers. The route table's
// spec runs every row through the router and the URL service's spec runs
// every row through `describe`, so a route added to one and not the other
// fails a row. A URL no route matches is the not-found page to the router
// and `null` to `describe`.

/** What the router activates for a URL. */
export interface TmRoutedCase {
  /** The router's URL after the navigation, when it differs from the row's. */
  readonly url?: string;
  /** The deepest routed component's name. */
  readonly component: string;
  /** The route data's page family. */
  readonly kind?: string;
  /** The route data's entity route. */
  readonly entity?: string;
  /** The routed parameters. */
  readonly params: Readonly<Record<string, string>>;
}

/** One URL, as the router routes it and as `describe` classifies it. */
export interface TmRouteCase {
  /** The URL. */
  readonly url: string;
  /** What the router activates. */
  readonly routed: TmRoutedCase;
  /** What `describe` answers, member by member; `null` when no route matches. */
  readonly described: Partial<TmRouteMatch> | null;
}

/** A route host in tenant 17. */
function host(kind: string, params: Readonly<Record<string, string>> = {}, entity?: string): TmRoutedCase {
  return { component: 'TmRouteHost', kind, ...(entity && { entity }), params: { t: '17', ...params } };
}

/** A match in tenant 17. */
function tenant(kind: TmRouteMatch['kind'], template: string, match: Partial<TmRouteMatch> = {}): Partial<TmRouteMatch> {
  return { kind, tenantId: 17, template: `/:t${template}`, pinnable: true, ...match };
}

/** The not-found page under the tenant shell. */
const TENANT_NOT_FOUND: TmRoutedCase = { component: 'TmNotFoundPage', params: { t: '17' } };

/** The not-found page outside any shell. */
const NOT_FOUND: TmRoutedCase = { component: 'TmNotFoundPage', params: {} };

/** Every URL the route and URL specs agree on. */
export const TM_ROUTE_CASES: readonly TmRouteCase[] = [
  // --- The tenantless pages ---
  { url: '/tenants', routed: { component: 'TmTenantPicker', params: {} }, described: { kind: 'picker', template: '/tenants', pinnable: false } },
  { url: '/signed-out', routed: { component: 'TmSignedOutPage', params: {} }, described: { kind: 'signed-out', template: '/signed-out', pinnable: false } },

  // --- The platform's own tenant pages ---
  { url: '/17', routed: host('home'), described: tenant('home', '') },
  { url: '/17/settings', routed: host('settings'), described: tenant('core', '/settings') },
  { url: '/17/settings/general', routed: host('settings', { category: 'general' }), described: tenant('core', '/settings/:category') },
  { url: '/17/me', routed: host('settings'), described: tenant('core', '/me') },
  { url: '/17/me/', routed: { ...host('settings'), url: '/17/me' }, described: tenant('core', '/me') },
  { url: '/17/me/region', routed: host('settings'), described: tenant('core', '/me/region') },
  {
    url: '/17/list/sales-invoice-lines',
    routed: host('search', { segment: 'sales-invoice-lines' }, 'list'),
    described: tenant('list', '/list/sales-invoice-lines', { entity: 'acme.SalesInvoiceLine' }),
  },
  { url: '/17/list/centers', routed: host('search', { segment: 'centers' }, 'list'), described: tenant('list', '/list/centers', { entity: 'gl.Center' }) },
  {
    url: '/17/reports/acme-credit-exposure',
    routed: host('report', { key: 'acme-credit-exposure' }),
    described: tenant('report', '/reports/:key', { key: 'acme-credit-exposure', securable: ['acme.Customer', 'Read'] }),
  },

  // --- The features' pages, before the entity routes they share a first segment with ---
  { url: '/17/credit-review', routed: host('custom'), described: tenant('page', '/credit-review') },
  { url: '/17/centers/5/map', routed: host('custom', { id: '5' }), described: tenant('page', '/centers/:id/map') },
  { url: '/17/approvals', routed: host('custom'), described: tenant('page', '/approvals', { securable: ['acme.Approval', 'Read'] }) },

  // --- The entity routes ---
  { url: '/17/centers', routed: host('search', { segment: 'centers' }, 'search'), described: tenant('search', '/centers', { entity: 'gl.Center' }) },
  { url: '/17/centers/new', routed: host('details', { segment: 'centers' }, 'create'), described: tenant('create', '/centers/new', { entity: 'gl.Center' }) },
  {
    url: '/17/centers/5',
    routed: host('details', { segment: 'centers', id: '5' }, 'details'),
    described: tenant('details', '/centers/:id', { entity: 'gl.Center', id: 5 }),
  },
  {
    url: '/17/customers/9',
    routed: host('details', { segment: 'customers', id: '9' }, 'details'),
    described: tenant('details', '/customers/:id', { entity: 'acme.Customer', id: 9 }),
  },
  { url: '/17/currencies', routed: host('search', { segment: 'currencies' }, 'search'), described: tenant('search', '/currencies', { entity: 'acme.Currency' }) },

  // --- No route: a stack without a details page, a child entity, unknown segments ---
  { url: '/17/currencies/5', routed: TENANT_NOT_FOUND, described: null },
  { url: '/17/currencies/new', routed: TENANT_NOT_FOUND, described: null },
  { url: '/17/sales-invoice-lines', routed: TENANT_NOT_FOUND, described: null },
  { url: '/17/sales-invoice-lines/5', routed: TENANT_NOT_FOUND, described: null },
  { url: '/17/list/nope', routed: TENANT_NOT_FOUND, described: null },
  { url: '/17/nope', routed: TENANT_NOT_FOUND, described: null },
  // Ids are positive integers; literal segments match exactly; sections and reports by name.
  { url: '/17/centers/abc', routed: TENANT_NOT_FOUND, described: null },
  { url: '/17/centers/05', routed: TENANT_NOT_FOUND, described: null },
  { url: '/17/centers/0', routed: TENANT_NOT_FOUND, described: null },
  { url: '/17/centers/5/more', routed: TENANT_NOT_FOUND, described: null },
  { url: '/17/Centers', routed: TENANT_NOT_FOUND, described: null },
  { url: '/17/me/unknown', routed: TENANT_NOT_FOUND, described: null },
  { url: '/17/settings/general/more', routed: TENANT_NOT_FOUND, described: null },
  { url: '/17/reports/unknown', routed: TENANT_NOT_FOUND, described: null },
  // A tenant is a positive safe integer without leading zeros.
  { url: '/017', routed: NOT_FOUND, described: null },
  { url: '/0', routed: NOT_FOUND, described: null },
  { url: '/17x', routed: NOT_FOUND, described: null },
  { url: '/99999999999999999999', routed: NOT_FOUND, described: null },
  { url: '/nope', routed: NOT_FOUND, described: null },
  { url: '/tenants/x', routed: NOT_FOUND, described: null },
];

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, DOCUMENT, inject, Injectable, Injector, signal, type Signal, untracked } from '@angular/core';
import { NavigationCancel, NavigationEnd, NavigationError, ResolveEnd, Router } from '@angular/router';

import { TM_APP_DECLARATIONS } from '../app/tm-app-declarations';
import { TM_DEV_MODE } from '../app/tm-dev-mode';
import type { TmLazyComponent, TmSecurableRef } from '../declarations/tm-declaration-types';
import type { TmPageDeclaration } from '../declarations/tm-page';
import { TmEntities } from '../entity/tm-entities';
import type { TmEntitySummary } from '../entity/tm-entity-metadata';
import type { TmDecimal, TmId } from '../json/tm-json';
import { tmIsIdSegment, tmRoutedTenant, tmTenantSegment } from '../launch/tm-routed-tenant';
import { TmTelemetry } from '../observe/tm-telemetry';
import { TM_ROUTE_HOSTS, TM_TENANT_SHELL } from '../routes/tm-route-hosts';
import { TM_ME_SECTIONS, TmRouteRules } from '../routes/tm-route-rules';
import {
  tmEncodeQueryValue,
  tmParseListPayload,
  tmParseSearchQuery,
  tmPrintListPayload,
  tmPrintSearchQuery,
  type TmListPayload,
  type TmQueryProblemSink,
  type TmSearchDefaults,
  type TmSearchQuery,
} from './tm-search-query';

// The app's URLs (spec 0024 §9.4): one grammar for the router, links, pins,
// recents, notification targets and server-built links. Every builder of
// `TmUrls` uses the routed tenant; `forTenant` builds links into any tenant.
// `describe` classifies a stored URL exactly as the route table routes it,
// and `preload` loads a route's chunks at idle, running no guard.

/** The builders of one tenant's URLs. */
export interface TmTenantUrls {
  /**
   * The tenant's home.
   *
   * @returns The URL, such as `/17`.
   */
  home(): string;
  /**
   * An entity's search page.
   *
   * @param entity - The entity's name, such as `gl.Center`.
   * @param query - The query state, printed in its canonical form.
   * @returns The URL, such as `/17/centers?q=jed`.
   */
  search(entity: string, query?: TmSearchQuery): string;
  /**
   * A record's details page.
   *
   * @param entity - The entity's name.
   * @param id - The record's id.
   * @param child - A row of one of its collections to show.
   * @returns The URL, such as `/17/centers/5?child=Lines:42`.
   */
  details(entity: string, id: TmId, child?: { readonly collection: string; readonly id: TmId }): string;
  /**
   * An entity's create page.
   *
   * @param entity - The entity's name.
   * @param options - The record to clone.
   * @returns The URL, such as `/17/centers/new?clone=5`.
   */
  create(entity: string, options?: { readonly clone?: TmId }): string;
  /**
   * A page under the tenant, by its path.
   *
   * @param path - The page's path, such as `me` or `settings/general`.
   * @returns The URL, such as `/17/me`.
   */
  page(path: string): string;
  /**
   * A drill-through list: a stack's or a child's rows under a fixed filter,
   * which rides the `#d=` fragment. The query's own filter and columns
   * never travel.
   *
   * @param segment - The entity's segment.
   * @param payload - The fixed filter and the opening columns.
   * @param query - The rest of the list's query state.
   * @returns The URL, such as `/17/list/sales-invoice-lines#d=…`.
   */
  list(segment: string, payload: TmListPayload, query?: TmSearchQuery): string;
  /**
   * A server resource's page: a record's details page when the entity is a
   * stack whose API offers `details` and whose declaration has a details page.
   *
   * @param resource - The entity's name.
   * @param id - The record's id.
   * @returns The URL, or `null` when the entity has no details page.
   */
  target(resource: string, id: TmId): string | null;
  /**
   * A blob's URL.
   *
   * @param kind - The blob kind, such as `customer-image`.
   * @param id - The blob's id.
   * @param options - The variant to read, and whether to download it.
   * @returns The URL, such as `/17/blobs/customer-image/88?variant=thumb`.
   */
  blob(kind: string, id: number, options?: { readonly variant?: string; readonly download?: boolean }): string;
}

/** What a stored URL routes to, by the route table. */
export interface TmRouteMatch {
  /** The route's family. */
  readonly kind: 'launch' | 'picker' | 'signed-out' | 'home' | 'search' | 'details' | 'create' | 'page' | 'core' | 'report' | 'list';
  /** The tenant, on a tenant route. */
  readonly tenantId?: number;
  /** The entity, on an entity route or a list. */
  readonly entity?: string;
  /** The record's id, on a details route. */
  readonly id?: TmId;
  /** The coded report's key, on a report route. */
  readonly key?: string;
  /** The matched page, on a page route. */
  readonly page?: TmPageDeclaration;
  /** The page's securable, or the coded report's. */
  readonly securable?: TmSecurableRef;
  /** The route's template, such as `/:t/centers/:id`. */
  readonly template: string;
  /** Whether the route can be pinned: every tenant route. */
  readonly pinnable: boolean;
  /** The URL's `#d=` fragment, which tells two lists of one segment apart; absent without one. */
  readonly hash?: string;
}

/** The members of an entity declaration that the URL builders read. */
interface TmSearchDeclaration {
  /** The page's tabs; the first is the default. */
  readonly tabs?: readonly { readonly key: string }[];
  /** The page's columns: paths, column objects with a path, or expressions. */
  readonly columns?: readonly (string | { readonly path?: string })[];
  /** The search hatch. */
  readonly search?: TmLazyComponent;
  /** The details page, or its layouts. */
  readonly details?: TmLazyComponent | { readonly page: TmLazyComponent; readonly create?: TmLazyComponent };
}

/** What the builders resolve: entities to segments and defaults, and a build outside a tenant. */
@Injectable({ providedIn: 'root' })
class TmUrlResolver {
  readonly #entities = inject(TmEntities);
  readonly #rules = inject(TmRouteRules);
  readonly #telemetry = inject(TmTelemetry);
  readonly #devMode = inject(TM_DEV_MODE);

  /**
   * An entity's summary for a URL builder: an unknown entity, or one without
   * a segment, throws in development and yields the tenant home in
   * production.
   *
   * @param entity - The entity's name.
   * @returns The summary, or `null`.
   */
  summary(entity: string): (TmEntitySummary & { readonly segment: string }) | null {
    const summary = this.#entities.summary(entity);
    if (summary?.segment != null) {
      return summary as TmEntitySummary & { readonly segment: string };
    }
    if (this.#devMode()) {
      throw new Error(`The entity '${entity}' has no route: the server registers no entity of that name with an API segment.`);
    }
    return null;
  }

  /**
   * The entity a list's segment shows.
   *
   * @param segment - The segment.
   * @returns The summary, or `undefined`.
   */
  listSummary(segment: string): TmEntitySummary | undefined {
    return this.#rules.list(segment);
  }

  /**
   * Whether an entity is a stack with a details page.
   *
   * @param entity - The entity's name.
   * @returns Whether it has one.
   */
  hasDetailsPage(entity: string): boolean {
    const summary = this.#entities.summary(entity);
    return summary?.kind === 'stack' && summary.segment !== null && this.#rules.hasDetailsPage(summary);
  }

  /**
   * The search page defaults an entity's declaration sets: its first tab,
   * its columns when they are all paths, and the page size.
   *
   * @param summary - The entity's summary.
   * @returns The defaults.
   */
  defaults(summary: TmEntitySummary): TmSearchDefaults {
    const declaration = this.#entities.declaration(summary.name) as TmSearchDeclaration | undefined;
    const paths = declaration?.columns?.map((column) => (typeof column === 'string' ? column : column.path));
    return {
      ...(declaration?.tabs?.[0] && { tab: declaration.tabs[0].key }),
      // A declared expression column never travels, so no URL's columns equal such a default.
      ...(paths && paths.every((path) => path !== undefined) && { cols: paths as string[] }),
    };
  }

  /**
   * A builder's answer outside a tenant: a development error, and in
   * production the picker with a telemetry event.
   *
   * @returns The picker's URL.
   */
  noTenant(): string {
    if (this.#devMode()) {
      throw new Error('A TmUrls builder ran outside a tenant route. Build links into a tenant with forTenant(tenantId).');
    }
    this.#telemetry.event('tm.urls.no-tenant');
    return '/tenants';
  }
}

/** The builders of one tenant's URLs, over the tenant's path prefix. */
class TmTenantUrlBuilders implements TmTenantUrls {
  /** The tenant's path, such as `/17`, or `null` outside a tenant. */
  readonly #prefix: () => string | null;

  /** Resolves entities and a build outside a tenant. */
  readonly #urls: TmUrlResolver;

  /**
   * Creates the builders.
   *
   * @param prefix - The tenant's path, such as `/17`, or `null` outside a tenant.
   * @param urls - Resolves entities and a build outside a tenant.
   */
  constructor(prefix: () => string | null, urls: TmUrlResolver) {
    this.#prefix = prefix;
    this.#urls = urls;
  }

  /** {@inheritDoc TmTenantUrls.home} */
  home(): string {
    return this.#path('');
  }

  /** {@inheritDoc TmTenantUrls.search} */
  search(entity: string, query: TmSearchQuery = {}): string {
    const summary = this.#urls.summary(entity);
    return summary ? this.#path(`/${summary.segment}`, tmPrintSearchQuery(query, this.#urls.defaults(summary))) : this.home();
  }

  /** {@inheritDoc TmTenantUrls.details} */
  details(entity: string, id: TmId, child?: { readonly collection: string; readonly id: TmId }): string {
    const summary = this.#urls.summary(entity);
    return summary
      ? this.#path(`/${summary.segment}/${id}`, { search: child ? `child=${tmEncodeQueryValue(`${child.collection}:${child.id}`)}` : '' })
      : this.home();
  }

  /** {@inheritDoc TmTenantUrls.create} */
  create(entity: string, options: { readonly clone?: TmId } = {}): string {
    const summary = this.#urls.summary(entity);
    return summary
      ? this.#path(`/${summary.segment}/new`, { search: options.clone === undefined ? '' : `clone=${tmEncodeQueryValue(String(options.clone))}` })
      : this.home();
  }

  /** {@inheritDoc TmTenantUrls.page} */
  page(path: string): string {
    return this.#path(`/${path}`);
  }

  /** {@inheritDoc TmTenantUrls.list} */
  list(segment: string, payload: TmListPayload, query: TmSearchQuery = {}): string {
    const summary = this.#urls.listSummary(segment);
    const { search } = tmPrintSearchQuery({ ...query, filter: undefined, cols: undefined }, summary ? this.#urls.defaults(summary) : {});
    return this.#path(`/list/${segment}`, { search, hash: tmPrintListPayload(payload) });
  }

  /** {@inheritDoc TmTenantUrls.target} */
  target(resource: string, id: TmId): string | null {
    return this.#urls.hasDetailsPage(resource) ? this.details(resource, id) : null;
  }

  /** {@inheritDoc TmTenantUrls.blob} */
  blob(kind: string, id: number, options: { readonly variant?: string; readonly download?: boolean } = {}): string {
    const query = [
      ...(options.variant === undefined ? [] : [`variant=${tmEncodeQueryValue(options.variant)}`]),
      ...(options.download ? ['download=true'] : []),
    ];
    return this.#path(`/blobs/${tmEncodeQueryValue(kind)}/${id}`, { search: query.join('&') });
  }

  /** The URL of a path under the tenant, with its query and fragment. */
  #path(path: string, query: { readonly search?: string; readonly hash?: string } = {}): string {
    const prefix = this.#prefix();
    if (prefix === null) {
      return this.#urls.noTenant();
    }
    return `${prefix}${path}${query.search ? `?${query.search}` : ''}${query.hash ?? ''}`;
  }
}

/**
 * The app's URLs: builders into the routed tenant, `forTenant` into any
 * tenant, the query codec's parsers, and the classifier and preloader of
 * stored URLs.
 *
 * @example
 * ```ts
 * const urls = inject(TmUrls);
 * urls.search('gl.Center', { q: 'jed' }); // '/17/centers?q=jed'
 * urls.forTenant(18).home();             // '/18'
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmUrls implements TmTenantUrls {
  readonly #router = inject(Router);
  readonly #entities = inject(TmEntities);
  readonly #rules = inject(TmRouteRules);
  readonly #window = inject(DOCUMENT).defaultView;
  readonly #injector = inject(Injector);
  readonly #resolver = inject(TmUrlResolver);
  readonly #devMode = inject(TM_DEV_MODE);

  /** Loads the Entities chunk, as one preload. */
  readonly #loadEntities = (): Promise<void> => this.#entities.load();

  /** The routed tenant. */
  readonly #tenant = signal<number | null>(tmRoutedTenant(this.#router.url));

  /** The loaders `preload` has called, so each runs once. */
  readonly #preloaded = new Set<() => Promise<unknown>>();

  /** Whether the app is destroyed, after which a preload queued at idle runs nothing. */
  #destroyed = false;

  /** The builders of the routed tenant. */
  readonly #routed = new TmTenantUrlBuilders(() => {
    const tenantId = this.#tenant();
    return tenantId === null ? null : `/${tenantId}`;
  }, this.#resolver);

  /** The tenant the routed page shows, or `null` outside a tenant. */
  readonly tenantId: Signal<number | null> = this.#tenant.asReadonly();

  /** Creates the service, which follows the routed tenant from each navigation's resolution. */
  constructor() {
    inject(DestroyRef).onDestroy(() => (this.#destroyed = true));

    // A navigation's pages are created after its resolution, so their links use its tenant.
    this.#router.events.subscribe((event) => {
      if (event instanceof ResolveEnd) {
        this.#tenant.set(tmRoutedTenant(event.urlAfterRedirects));
      } else if (event instanceof NavigationEnd || event instanceof NavigationCancel || event instanceof NavigationError) {
        this.#tenant.set(tmRoutedTenant(this.#router.url));
      }
    });
  }

  /**
   * The builders of another tenant's URLs.
   *
   * @param tenantId - The tenant.
   * @returns The builders.
   */
  forTenant(tenantId: number): TmTenantUrls {
    return new TmTenantUrlBuilders(() => `/${tenantId}`, this.#resolver);
  }

  /** {@inheritDoc TmTenantUrls.home} */
  home(): string {
    return this.#routed.home();
  }

  /** {@inheritDoc TmTenantUrls.search} */
  search(entity: string, query?: TmSearchQuery): string {
    return this.#routed.search(entity, query);
  }

  /** {@inheritDoc TmTenantUrls.details} */
  details(entity: string, id: TmId, child?: { readonly collection: string; readonly id: TmId }): string {
    return this.#routed.details(entity, id, child);
  }

  /** {@inheritDoc TmTenantUrls.create} */
  create(entity: string, options?: { readonly clone?: TmId }): string {
    return this.#routed.create(entity, options);
  }

  /** {@inheritDoc TmTenantUrls.page} */
  page(path: string): string {
    return this.#routed.page(path);
  }

  /** {@inheritDoc TmTenantUrls.list} */
  list(segment: string, payload: TmListPayload, query?: TmSearchQuery): string {
    return this.#routed.list(segment, payload, query);
  }

  /** {@inheritDoc TmTenantUrls.target} */
  target(resource: string, id: TmId): string | null {
    return this.#routed.target(resource, id);
  }

  /** {@inheritDoc TmTenantUrls.blob} */
  blob(kind: string, id: number, options?: { readonly variant?: string; readonly download?: boolean }): string {
    return this.#routed.blob(kind, id, options);
  }

  /**
   * A search page's query state, from its URL. An invalid parameter or a
   * malformed `#d=` payload is dropped, and reported in development.
   *
   * @param params - The URL's query parameters.
   * @param hash - The raw `location.hash`, never the router's decoded fragment.
   * @returns The query state.
   */
  parseSearch(params: URLSearchParams, hash: string): TmSearchQuery {
    return tmParseSearchQuery(params, hash, this.#report);
  }

  /**
   * A drill-through list's payload and query state, from its URL.
   *
   * @param params - The URL's query parameters.
   * @param hash - The raw `location.hash`.
   * @returns The payload, `null` when the fragment carries none or a malformed one, and the query state.
   */
  parseList(params: URLSearchParams, hash: string): { readonly payload: TmListPayload | null; readonly query: TmSearchQuery } {
    return { payload: tmParseListPayload(hash), query: tmParseSearchQuery(params, '', this.#report) };
  }

  /**
   * The tenant picker's URL.
   *
   * @param options - The notice to show, and the tenant it is about.
   * @returns The URL, such as `/tenants?notice=tenant-not-found&from=17`.
   */
  picker(options: { readonly notice?: 'tenant-not-found'; readonly from?: number } = {}): string {
    const query = [...(options.notice ? [`notice=${options.notice}`] : []), ...(options.from === undefined ? [] : [`from=${options.from}`])];
    return `/tenants${query.length > 0 ? `?${query.join('&')}` : ''}`;
  }

  /**
   * The signed-out page's URL.
   *
   * @returns `/signed-out`.
   */
  signedOut(): string {
    return '/signed-out';
  }

  /**
   * Classifies a stored URL, such as a pin's or a recent's, by the route
   * table: the route it reaches, with its `#d=` fragment.
   *
   * @param url - The URL, from its path on: `/17/centers?q=jed`.
   * @returns The match, or `null` when the URL reaches a not-found page.
   */
  describe(url: string): TmRouteMatch | null {
    const hashAt = url.indexOf('#');
    const hash = hashAt < 0 ? '' : url.slice(hashAt);
    const segments = this.#router.parseUrl(url).root.children['primary']?.segments ?? [];
    // A tenant segment with matrix parameters, such as `/17;x=1`, reaches the not-found page.
    if (segments[0] && tmTenantSegment(segments[0].path) !== null && Object.keys(segments[0].parameters).length > 0) {
      return null;
    }
    const match = this.#match(segments.map((segment) => segment.path));
    return match && (hash.startsWith('#d=') ? { ...match, hash } : match);
  }

  /**
   * Loads, at idle, everything a route's first frame needs: on a tenant
   * route the tenant shell, then its host's chunk, its declaration's lazy
   * layout and hatch chunks, and on an entity route the Entities chunk. It
   * runs no guard or resolver, and loads each chunk once.
   *
   * @param url - The route's URL.
   */
  preload(url: string): void {
    const idle = (run: () => void): unknown =>
      this.#window?.requestIdleCallback ? this.#window.requestIdleCallback(run) : setTimeout(run);
    idle(() => {
      // A preload queued before the app was destroyed runs nothing.
      if (this.#destroyed) {
        return;
      }
      const match = this.describe(url);
      for (const load of match ? this.#loadersOf(match) : []) {
        if (!this.#preloaded.has(load)) {
          this.#preloaded.add(load);
          // A failed load is tried again by the next preload or by the route itself.
          load().catch(() => this.#preloaded.delete(load));
        }
      }
    });
  }

  /** Reports a dropped query entry in development. */
  readonly #report: TmQueryProblemSink = (problem) => {
    if (this.#devMode()) {
      console.warn(problem);
    }
  };

  /** The route a URL's decoded path segments reach, by the route table's order. */
  #match(segments: readonly string[]): TmRouteMatch | null {
    const [first, second] = segments;
    // --- The tenantless routes ---
    if (first === undefined) {
      return { kind: 'launch', template: '/', pinnable: false };
    }
    if (segments.length === 1 && (first === 'tenants' || first === 'signed-out')) {
      return { kind: first === 'tenants' ? 'picker' : 'signed-out', template: `/${first}`, pinnable: false };
    }
    const tenantId = tmTenantSegment(first);
    if (tenantId === null) {
      return null;
    }
    const rest = segments.slice(1);
    const tenant = (match: Omit<TmRouteMatch, 'tenantId' | 'pinnable' | 'template'>, template: string): TmRouteMatch => ({
      ...match,
      tenantId,
      template: `/:t${template}`,
      pinnable: true,
    });

    // --- The platform's own pages ---
    if (second === undefined) {
      return tenant({ kind: 'home' }, '');
    }
    if (second === 'settings' && rest.length <= 2) {
      return tenant({ kind: 'core' }, rest.length === 2 ? '/settings/:category' : '/settings');
    }
    if (second === 'me' && (rest.length === 1 || (rest.length === 2 && TM_ME_SECTIONS.includes(rest[1]!)))) {
      return tenant({ kind: 'core' }, `/${rest.join('/')}`);
    }
    if (second === 'list' && rest.length === 2) {
      const summary = this.#rules.list(rest[1]!);
      if (summary) {
        return tenant({ kind: 'list', entity: summary.name }, `/list/${rest[1]}`);
      }
    }
    if (second === 'reports' && rest.length === 2) {
      const report = this.#rules.report(rest[1]!);
      if (report) {
        return tenant({ kind: 'report', key: report.key, securable: report.securable }, '/reports/:key');
      }
    }

    // --- The features' pages, in feature order ---
    for (const page of untracked(() => this.#pages())) {
      const parts = page.path.split('/');
      if (parts.length === rest.length && parts.every((part, index) => part.startsWith(':') || part === rest[index])) {
        return tenant({ kind: 'page', page, ...(page.securable && { securable: page.securable }) }, `/${page.path}`);
      }
    }

    // --- The entity routes ---
    const stack = this.#rules.stack(second);
    if (stack && rest.length === 1) {
      return tenant({ kind: 'search', entity: stack.name }, `/${second}`);
    }
    if (stack && rest.length === 2 && this.#rules.hasDetailsPage(stack)) {
      if (rest[1] === 'new') {
        return tenant({ kind: 'create', entity: stack.name }, `/${second}/new`);
      }
      if (tmIsIdSegment(rest[1])) {
        // An id beyond the safe integers stays exact as its digits, as a `bigint` key's id travels.
        const id = Number(rest[1]);
        return tenant({ kind: 'details', entity: stack.name, id: Number.isSafeInteger(id) ? id : (rest[1] as TmDecimal) }, `/${second}/:id`);
      }
    }
    return null;
  }

  /** The merged pages, in feature order. */
  #pages(): readonly TmPageDeclaration[] {
    return this.#injector.get(TM_APP_DECLARATIONS, null)?.pages ?? [];
  }

  /** The chunks a route's first frame needs: on a tenant route, the tenant shell's first. */
  #loadersOf(match: TmRouteMatch): (() => Promise<unknown>)[] {
    const declaration = match.entity === undefined ? undefined : (this.#entities.declaration(match.entity) as TmSearchDeclaration | undefined);
    const details = typeof declaration?.details === 'function' ? { page: declaration.details } : declaration?.details;
    const entities = this.#loadEntities;
    const loaders: (TmLazyComponent | (() => Promise<unknown>) | undefined)[] = (() => {
      switch (match.kind) {
        case 'search':
        case 'list':
          return [TM_ROUTE_HOSTS.search, declaration?.search, entities];
        case 'details':
          return [TM_ROUTE_HOSTS.details, details?.page, entities];
        case 'create':
          return [TM_ROUTE_HOSTS.details, details?.create ?? details?.page, entities];
        case 'page':
          return [match.page?.component];
        case 'home':
          return [TM_ROUTE_HOSTS.home];
        case 'report':
          return [TM_ROUTE_HOSTS.report];
        case 'core':
          return [match.template.startsWith('/:t/me') ? TM_ROUTE_HOSTS.me : TM_ROUTE_HOSTS.settings];
        default:
          return [];
      }
    })();
    // Every tenant route renders in the tenant shell, which the route table
    // returns at once only after its load has settled: a first entry whose
    // shell is still loading waits a task, and misses the click's frame
    // (spec 0024 §4.4). It is one loader, so it loads once for every route.
    const shell = match.tenantId === undefined ? undefined : TM_TENANT_SHELL;
    return [shell, ...loaders].filter((load) => load !== undefined);
  }
}

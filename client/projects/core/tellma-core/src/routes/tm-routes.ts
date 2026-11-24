// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import {
  ActivatedRoute,
  type ActivatedRouteSnapshot,
  type CanDeactivateFn,
  type CanMatchFn,
  type Route,
  type Routes,
  type RunGuardsAndResolvers,
  type UrlMatcher,
  type UrlSegment,
} from '@angular/router';

import type { TmRouteData } from '../app/tm-route-data';
import type { TmPageDeclaration } from '../declarations/tm-page';
import { TmLaunchRedirect, tmLaunchMatch } from '../launch/tm-launch-redirect';
import { tmIsIdSegment, tmTenantSegment } from '../launch/tm-routed-tenant';
import { tmSessionGuard } from '../session/tm-session-guard';
import { TM_ROUTE_ERROR_VIEWS, TmRouteError } from './tm-route-error';
import { TmRouteHost } from './tm-route-host';
import { TM_ROUTE_HOSTS, tmShellComponent } from './tm-route-hosts';
import { TM_ME_SECTIONS, TmRouteRules } from './tm-route-rules';

// The route table (spec 0024 §9.2), built once at provider time from the
// declarations alone: the launch redirect, the tenantless pages, and under
// a positive-integer tenant segment the home, settings, My settings, the
// drill-through list, coded reports, the features' pages in feature order,
// then the entity routes, whose segments are looked up at match time once
// the Core chunk is loaded. Literal segments match exactly: `/17/Centers`
// is not found.

/** The route data the platform's own routes add for the route host and the not-found page. */
export interface TmRouteDataInternal {
  /** The entity route the host serves, which picks its family's host and its entity. */
  readonly ɵentity?: 'search' | 'list' | 'details' | 'create';
  /** The parameters whose change the page absorbs in place, with no leave check. */
  readonly ɵinPlace?: readonly string[];
  /** Whether the route renders outside any shell, so it brings its own `main`. */
  readonly ɵmain?: boolean;
  /**
   * The page's route template, such as `/:t/credit-review` or `/:t/:segment`,
   * whose host registers the page's default context with `:segment` filled in.
   */
  readonly ɵtemplate?: string;
}

/** The not-found page's title key. */
const NOT_FOUND = 'tm.errors.notFound.title';

/** The not-found page: no route matches the path. */
@Component({
  selector: 'tm-not-found-page',
  imports: [TmRouteError],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- Outside any shell, the page brings the document's main landmark. -->
    @if (main) {
      <main><tm-route-error [view]="view" /></main>
    } @else {
      <tm-route-error [view]="view" />
    }
  `,
})
export class TmNotFoundPage {
  /** Whether the page renders outside any shell. */
  protected readonly main = (inject(ActivatedRoute).snapshot.data as TmRouteDataInternal).ɵmain === true;

  /** The view's strings. */
  protected readonly view = TM_ROUTE_ERROR_VIEWS['not-found'];
}

/**
 * The first path segment as the tenant: a positive integer without leading
 * zeros and without matrix parameters, as the parameter `t`.
 */
const tenantMatcher: UrlMatcher = (segments) =>
  tmTenantSegment(segments[0]?.path) === null || Object.keys(segments[0]!.parameters).length > 0
    ? null
    : { consumed: segments.slice(0, 1), posParams: { t: segments[0]! } };

/** `settings` and `settings/{category}`: one route, so a tab click reuses the host. */
const settingsMatcher: UrlMatcher = (segments) => {
  if (segments[0]?.path !== 'settings' || segments.length > 2) {
    return null;
  }
  const posParams: Record<string, UrlSegment> = segments[1] ? { category: segments[1] } : {};
  return { consumed: segments, posParams };
};

/** The segment at `index` of a route's match. */
function segmentAt(segments: readonly UrlSegment[], index: number): string {
  return segments[index]?.path ?? '';
}

/** A drill-through list's segment names a stack or a child. */
const listMatch: CanMatchFn = (_route, segments) => inject(TmRouteRules).list(segmentAt(segments, 1)) !== undefined;

/** A coded report's key is one the declarations merged. */
const reportMatch: CanMatchFn = (_route, segments) => inject(TmRouteRules).report(segmentAt(segments, 1)) !== undefined;

/** An entity segment names a stack. */
const stackMatch: CanMatchFn = (_route, segments) => inject(TmRouteRules).stack(segmentAt(segments, 0)) !== undefined;

/** An entity segment names a stack with a details page; a record's id is a positive integer. */
const detailsMatch: CanMatchFn = (_route, segments) =>
  inject(TmRouteRules).details(segmentAt(segments, 0)) !== undefined &&
  (segmentAt(segments, 1) === 'new' || tmIsIdSegment(segmentAt(segments, 1)));

/**
 * The route in a router state that a route config created, searching from
 * its root down the primary outlet.
 */
function find(state: ActivatedRouteSnapshot, config: Route | null): ActivatedRouteSnapshot | null {
  for (let route: ActivatedRouteSnapshot | null = state; route; route = route.firstChild) {
    if (route.routeConfig === config) {
      return route;
    }
  }
  return null;
}

/**
 * The section a page route renders: its child route's path, the hub's bare
 * path standing for its first section, or `null` on a route without
 * sections.
 */
function sectionOf(route: ActivatedRouteSnapshot): string | null {
  const child = route.firstChild?.routeConfig;
  // `/{t}/me` renders the first section, so it and `/{t}/me/profile` are one section.
  return child ? child.path || TM_ME_SECTIONS[0] : null;
}

/**
 * A page route's leave check: the checks its page registered, unless the
 * navigation stays on the same route, in the same child route, and changes
 * only parameters the page absorbs in place, such as the settings tab. A
 * move between My settings sections is a leave. A route whose host was
 * never rendered has no checks.
 */
// A section change leaves the section, as spec 0032 §8.1 says.
const leave: CanDeactivateFn<TmRouteHost | null> = (host, current, _state, next) => {
  const reused = find(next.root, current.routeConfig);
  const inPlace = new Set((current.data as TmRouteDataInternal).ɵinPlace ?? []);
  const same =
    reused !== null &&
    sectionOf(reused) === sectionOf(current) &&
    [...new Set([...Object.keys(current.params), ...Object.keys(reused.params)])].every(
      (name) => inPlace.has(name) || current.params[name] === reused.params[name],
    );
  return same || (host?.ɵleave() ?? true);
};

/**
 * The hub's guards run again whenever the navigation changes its section or
 * its tenant, so its leave check runs.
 */
const sectionChange: RunGuardsAndResolvers = (from, to) => from.params['t'] !== to.params['t'] || sectionOf(from) !== sectionOf(to);

/**
 * A page route: the route host, its data, and the leave check.
 *
 * @param route - The route's path or matcher and any other members.
 * @param data - The route's data.
 * @returns The route.
 */
function page(route: Route, data: TmRouteData & TmRouteDataInternal): Route {
  return { ...route, component: TmRouteHost, data, canDeactivate: [leave] };
}

/**
 * The app's route table, derived from its merged pages.
 *
 * @param pages - The merged pages, in feature order.
 * @returns The routes.
 */
export function tmBuildRoutes(pages: readonly TmPageDeclaration[]): Routes {
  const tenant: Routes = [
    page(
      { path: '', pathMatch: 'full' },
      { kind: 'home', skeleton: 'home', titleKey: 'tm.home.title', load: TM_ROUTE_HOSTS.home, ɵtemplate: '/:t' },
    ),
    page(
      { matcher: settingsMatcher },
      {
        kind: 'settings',
        skeleton: 'details',
        titleKey: 'tm.admin.settings.title',
        load: TM_ROUTE_HOSTS.settings,
        ɵinPlace: ['category'],
        ɵtemplate: '/:t/settings',
      },
    ),
    // The hub persists across its sections, which are child routes with no
    // component of their own; a section change still runs its leave check.
    page(
      { path: 'me', runGuardsAndResolvers: sectionChange, children: ['', ...TM_ME_SECTIONS].map((path) => ({ path, children: [] })) },
      { kind: 'settings', skeleton: 'me', titleKey: 'tm.admin.me.title', load: TM_ROUTE_HOSTS.me, ɵtemplate: '/:t/me' },
    ),
    page(
      { path: 'list/:segment', canMatch: [listMatch] },
      { kind: 'search', skeleton: 'search', ɵentity: 'list', ɵtemplate: '/:t/list/:segment' },
    ),
    page(
      { path: 'reports/:key', canMatch: [reportMatch] },
      { kind: 'report', skeleton: 'report', load: TM_ROUTE_HOSTS.report, ɵtemplate: '/:t/reports/:key' },
    ),
    // The features' pages, in feature order, before the entity routes they may share a first segment with.
    ...pages.map((declared) =>
      page(
        { path: declared.path },
        {
          kind: declared.kind ?? 'custom',
          skeleton: declared.skeleton ?? 'blank',
          load: declared.component,
          titleKey: declared.titleKey,
          ɵtemplate: `/:t/${declared.path}`,
          ...(declared.securable && { securable: declared.securable }),
        },
      ),
    ),
    page(
      { path: ':segment', canMatch: [stackMatch] },
      { kind: 'search', skeleton: 'search', ɵentity: 'search', ɵtemplate: '/:t/:segment' },
    ),
    page(
      { path: ':segment/new', canMatch: [detailsMatch] },
      { kind: 'details', skeleton: 'details', ɵentity: 'create', ɵtemplate: '/:t/:segment/new' },
    ),
    page(
      { path: ':segment/:id', canMatch: [detailsMatch] },
      { kind: 'details', skeleton: 'details', ɵentity: 'details', ɵtemplate: '/:t/:segment/:id' },
    ),
    { path: '**', component: TmNotFoundPage, data: { titleKey: NOT_FOUND } satisfies Partial<TmRouteData> },
  ];
  return [
    { path: '', pathMatch: 'full', canMatch: [tmLaunchMatch], canActivate: [tmSessionGuard], component: TmLaunchRedirect },
    {
      path: 'tenants',
      canActivate: [tmSessionGuard],
      loadComponent: tmShellComponent('TmTenantPicker'),
      // The shell frame registers the title's string (spec 0027 §6.2).
      data: { titleKey: 'tm.shell.tenants' } satisfies Partial<TmRouteData>,
    },
    { path: 'signed-out', loadComponent: tmShellComponent('TmSignedOutPage') },
    { matcher: tenantMatcher, canActivate: [tmSessionGuard], loadComponent: tmShellComponent('TmTenantShell'), children: tenant },
    {
      path: '**',
      canActivate: [tmSessionGuard],
      component: TmNotFoundPage,
      data: { titleKey: NOT_FOUND, ɵmain: true } satisfies Partial<TmRouteData> & TmRouteDataInternal,
    },
  ];
}

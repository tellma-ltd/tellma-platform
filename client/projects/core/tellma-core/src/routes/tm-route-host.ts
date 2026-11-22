// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgComponentOutlet } from '@angular/common';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  Injectable,
  Injector,
  reflectComponentType,
  signal,
  type Signal,
  type Type,
  untracked,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, type ActivatedRouteSnapshot, NavigationEnd, Router } from '@angular/router';

import { TM_API_UPDATES } from '../api/tm-api-seams';
import type { TmRouteData, TmRouteSkeleton } from '../app/tm-route-data';
import { TM_BOOT_RESULT } from '../boot/tm-boot-result';
import { provideTmPageScope } from '../context/tm-page-scope';
import type { TmLazyComponent } from '../declarations/tm-declaration-types';
import { TmEntities } from '../entity/tm-entities';
import { TmLocalization } from '../l10n/tm-localization';
import { TmTelemetry } from '../observe/tm-telemetry';
import { TmLaunchRedirect } from '../launch/tm-launch-redirect';
import { tmTenantSegment } from '../launch/tm-routed-tenant';
import { TmLaunchCache } from '../state/launch/tm-launch-cache';
import { TmLeaveChecks } from './tm-leave-checks';
import { TmPageFocus } from './tm-page-focus';
import { TmPageSkeleton } from './tm-page-skeleton';
import { TM_ROUTE_ERROR_VIEWS, TmRouteError, type TmRouteErrorKind } from './tm-route-error';
import { TM_ROUTE_HOSTS } from './tm-route-hosts';
import { TM_ROUTE_PERMISSIONS } from './tm-route-permissions';
import { TmRouteRules } from './tm-route-rules';
import type { TmRouteDataInternal } from './tm-routes';

// The route host (spec 0024 §4.4): the component of every page route. It
// draws the page's skeleton in the click's frame, then loads the page's
// component (and, on an entity route, the Entities chunk) while a cold
// tenant entry runs its refresh, and creates the page only once the launch
// documents are current and the tenant's language and calendar have loaded.
// A `tmPage` whose permission the user lacks renders the forbidden view in
// place. A chunk that fails to load asks the service worker for an update: a
// newer version shows `client-outdated`, anything else `offline`. Each page
// gets a page scope and a leave-check registry. A load holds the focus move
// until its view renders; a load superseded, or a host destroyed while it
// waits, gives its hold back.

/** Where the host stands: the skeleton, the page, or an error view in its place. */
type TmHostState =
  | { readonly view: 'loading' }
  | { readonly view: 'page'; readonly component: Type<unknown> }
  | { readonly view: 'error'; readonly error: TmRouteErrorKind };

// A stored report and a dashboard route as details pages, yet draw the
// compact header's skeletons (spec 0024 §4.4).
/** The skeletons of the entities whose details page is not a record's form. */
const ENTITY_SKELETONS: Readonly<Record<string, TmRouteSkeleton>> = { 'core.Report': 'report', 'core.Dashboard': 'dashboard' };

/**
 * The component a lazy component's module holds.
 *
 * @param load - The lazy component.
 * @returns The component.
 */
async function resolveComponent(load: TmLazyComponent): Promise<Type<unknown>> {
  const loaded = await load();
  return typeof loaded === 'function' ? loaded : loaded.default;
}

/**
 * The component a router state renders deepest: the nearest component at
 * or above its deepest route, since a My settings section has none.
 */
function routedComponent(root: ActivatedRouteSnapshot): unknown {
  let route = root;
  while (route.firstChild) {
    route = route.firstChild;
  }
  for (let at: ActivatedRouteSnapshot | null = route; at; at = at.parent) {
    if (at.component) {
      return at.component;
    }
  }
  return null;
}

/**
 * Times the document's launch into a tenant page: from navigation start to
 * the first render of the page the launch lands on. A launch is warm when
 * the boot script found the same user and the page's tenant entry found its
 * launch documents current. A launch that lands elsewhere, such as on the
 * picker, is not timed, so no later page counts the time spent there.
 */
@Injectable({ providedIn: 'root' })
export class TmLaunchTimer {
  readonly #telemetry = inject(TmTelemetry);
  readonly #router = inject(Router);
  readonly #cold = inject(TM_BOOT_RESULT)?.cold ?? true;

  /** Whether the launch was timed, or ended untimed. */
  #timed = false;

  /**
   * Starts following the launch's navigations: one that ends outside a page
   * route ends the launch untimed. The launch page at `/` only passes the
   * launch on to where it leads. Internal: the boot sequence starts it.
   */
  ɵstart(): void {
    const watch = this.#router.events.subscribe((event) => {
      if (this.#timed) {
        watch.unsubscribe();
        return;
      }
      if (event instanceof NavigationEnd) {
        const component = routedComponent(this.#router.routerState.snapshot.root);
        if (component !== TmRouteHost && component !== TmLaunchRedirect) {
          this.#timed = true;
          watch.unsubscribe();
        }
      }
    });
  }

  /**
   * Emits the launch's timing at the first page rendered, unless the launch
   * already ended.
   *
   * @param routeKind - The page's family.
   * @param cold - Whether the page's tenant entry was cold: it ran a cold sync, or the sync failed.
   */
  rendered(routeKind: string, cold: boolean): void {
    if (!this.#timed) {
      this.#timed = true;
      this.#telemetry.timing(this.#cold || cold ? 'tm.launch.cold' : 'tm.launch.warm', performance.now(), { routeKind });
    }
  }
}

/** The component of every page route: skeleton, then the page, in one page scope. */
@Component({
  selector: 'tm-route-host',
  imports: [NgComponentOutlet, TmPageSkeleton, TmRouteError],
  providers: [provideTmPageScope(), TmLeaveChecks],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @switch (state().view) {
      @case ('page') {
        <!-- The page, behind its permission gate; without the permission, the forbidden view naming it. -->
        @if (allowed()) {
          <ng-container *ngComponentOutlet="component(); inputs: inputs()" />
        } @else {
          <tm-route-error [view]="views.forbidden" [params]="permission()" />
        }
      }
      @case ('error') {
        <!-- The page's chunk failed to load. -->
        <tm-route-error [view]="views[error()]" (retry)="load()" />
      }
      @default {
        <!-- The page's skeleton, from the click's frame until the page renders. -->
        <tm-page-skeleton [kind]="skeleton()" />
      }
    }
  `,
  styles: `
    :host {
      display: block;
    }
  `,
  host: { '[attr.aria-busy]': "state().view === 'loading' ? 'true' : null" },
})
export class TmRouteHost {
  readonly #injector = inject(Injector);
  readonly #rules = inject(TmRouteRules);
  readonly #entities = inject(TmEntities);
  readonly #launch = inject(TmLaunchCache);
  readonly #l10n = inject(TmLocalization);
  readonly #updates = inject(TM_API_UPDATES);
  readonly #permissions = inject(TM_ROUTE_PERMISSIONS);
  readonly #focus = inject(TmPageFocus);
  readonly #timer = inject(TmLaunchTimer);
  readonly #leave = inject(TmLeaveChecks);

  /** The route's data. */
  protected readonly data: Signal<TmRouteData & TmRouteDataInternal>;

  /** The route's parameters, the tenant's included. */
  readonly #params: Signal<Readonly<Record<string, string>>>;

  /** The route's query parameters. */
  readonly #query: Signal<Readonly<Record<string, string>>>;

  /** Where the host stands. */
  readonly #state = signal<TmHostState>({ view: 'loading' });

  /** Where the host stands. */
  protected readonly state: Signal<TmHostState> = this.#state.asReadonly();

  /** The page's component, once loaded. */
  protected readonly component = computed(() => {
    const state = this.#state();
    return state.view === 'page' ? state.component : null;
  });

  /** The forbidden view's placeholder: the permission the page needs. */
  protected readonly permission = computed(() => {
    const securable = this.data().securable;
    return { permission: securable ? `${securable[0]} · ${securable[1]}` : '' };
  });

  /** The route's error views. */
  protected readonly views = TM_ROUTE_ERROR_VIEWS;

  /** The error view in the page's place. */
  protected readonly error = computed<TmRouteErrorKind>(() => {
    const state = this.#state();
    return state.view === 'error' ? state.error : 'offline';
  });

  /** The routed entity's name, on an entity route. */
  readonly #entity = computed(() => {
    const data = this.data();
    const segment = this.#params()['segment'];
    if (data.ɵentity === undefined || segment === undefined) {
      return undefined;
    }
    return (data.ɵentity === 'list' ? this.#rules.list(segment) : this.#rules.stack(segment))?.name;
  });

  /** The skeleton the host draws: the route's, unless the routed entity's details page has its own. */
  protected readonly skeleton = computed<TmRouteSkeleton>(() => {
    const data = this.data();
    const entity = this.#entity();
    return (data.ɵentity === 'details' && entity !== undefined && ENTITY_SKELETONS[entity]) || data.skeleton;
  });

  /** The page's component loader: the route's own, else its family's host. */
  readonly #loader = computed(() => {
    const data = this.data();
    return data.load ?? (data.ɵentity === 'details' || data.ɵentity === 'create' ? TM_ROUTE_HOSTS.details : TM_ROUTE_HOSTS.search);
  });

  /** Whether the user may open the page: always, unless the route names a securable. */
  protected readonly allowed = computed(() => {
    const securable = this.data().securable;
    return securable === undefined || this.#permissions.can(securable[0], securable[1])();
  });

  /**
   * The page's inputs: the route's parameters and query parameters, and the
   * entity on an entity route, each only when the page declares it.
   */
  protected readonly inputs = computed(() => {
    const component = this.component();
    const declared = new Set((component && reflectComponentType(component)?.inputs.map((input) => input.templateName)) ?? []);
    const entity = this.#entity();
    const values: Record<string, unknown> = { ...this.#query(), ...this.#params(), ...(entity && { entity }) };
    return Object.fromEntries(Object.entries(values).filter(([name]) => declared.has(name)));
  });

  /** The current load, so a superseded one stops; a destroyed host has none. */
  #run = 0;

  /** The focus holds of the loads not rendered yet, released when a load supersedes them or the host goes. */
  readonly #holds = new Set<() => void>();

  /** Whether the host is destroyed. */
  #destroyed = false;

  /** Creates the host, which loads its page now and again whenever the routed page changes in place. */
  constructor() {
    const route = inject(ActivatedRoute);
    inject(DestroyRef).onDestroy(() => {
      this.#destroyed = true;
      this.#run++;
      // A load that never renders, such as one waiting on a failed cold entry, holds focus no longer.
      this.#release();
    });
    this.data = toSignal(route.data, { requireSync: true }) as Signal<TmRouteData & TmRouteDataInternal>;
    this.#params = toSignal(route.params, { requireSync: true });
    this.#query = toSignal(route.queryParams, { requireSync: true });
    // A reused route, such as another entity's search page, loads its page anew.
    effect(() => {
      this.#loader();
      this.#entity();
      untracked(() => void this.load());
    });
  }

  /**
   * Whether the page may be left: the leave checks its page registered.
   * Internal: the page route's `canDeactivate` asks it.
   *
   * @returns Whether the navigation may go on.
   */
  ɵleave(): boolean | Promise<boolean> {
    return this.#leave.run();
  }

  /**
   * Loads the page: its component and, on an entity route, the Entities
   * chunk, beside a cold tenant entry's refresh, then shows the page.
   *
   * @returns A promise that settles once the page or an error view shows.
   */
  protected async load(): Promise<void> {
    const run = ++this.#run;
    // A superseded load renders nothing: this load's hold, taken first, takes over from its hold.
    const hold = this.#focus.ɵhold();
    this.#release();
    const release = (): void => {
      this.#holds.delete(release);
      hold();
    };
    this.#holds.add(release);
    const current = (): boolean => run === this.#run;
    this.#state.set({ view: 'loading' });

    // --- The cold entry's refresh, the page's chunk and the Entities chunk, in parallel ---
    // The entry settles with whether it was cold; a failed one was cold too.
    const tenantId = tmTenantSegment(this.#params()['t']);
    const entered =
      tenantId === null
        ? Promise.resolve({ ok: true, cold: false })
        : this.#launch.ɵenter(tenantId).then(
            (cold) => ({ ok: true, cold }),
            () => ({ ok: false, cold: true }),
          );
    let component: Type<unknown>;
    try {
      [component] = await Promise.all([resolveComponent(this.#loader()), this.#entity() === undefined ? null : this.#entities.load()]);
    } catch {
      // A chunk that fails to load: a newer version is the likely cause.
      const update = await this.#updates.check();
      if (current()) {
        this.#state.set({ view: 'error', error: update === 'ready' || update === 'failed' ? 'client-outdated' : 'offline' });
      }
      this.#rendered(release);
      return;
    }

    // --- A failed refresh keeps the skeleton until the documents arrive: the shell shows the failure ---
    const entry = await entered;
    if (!entry.ok && current()) {
      await this.#warm();
    }
    // --- The tenant's language and calendar load before the page: its first frame and read use them ---
    // A load that fails keeps the previous ones, as at startup.
    if (current()) {
      await this.#l10n.ɵready();
    }
    if (!current()) {
      this.#rendered(release);
      return;
    }
    this.#state.set({ view: 'page', component });
    // The launch is timed once the page has rendered.
    const routeKind = this.data().kind;
    this.#rendered(release, () => this.#timer.rendered(routeKind, entry.cold));
  }

  /** Releases every focus hold still taken. */
  #release(): void {
    for (const release of [...this.#holds]) {
      release();
    }
  }

  /**
   * Releases the focus hold once the host's new view has rendered, or at
   * once when the host is gone, after what else waits for the render.
   */
  #rendered(release: () => void, rendered?: () => void): void {
    if (this.#destroyed) {
      release();
    } else {
      afterNextRender(
        {
          read: () => {
            rendered?.();
            release();
          },
        },
        { injector: this.#injector },
      );
    }
  }

  /** Resolves once the routed tenant's launch documents are current. */
  #warm(): Promise<void> {
    return new Promise((resolve) => {
      const watch = effect(
        () => {
          if (this.#launch.warm()) {
            watch.destroy();
            resolve();
          }
        },
        { injector: this.#injector },
      );
    });
  }
}

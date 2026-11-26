// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Type } from '@angular/core';

import type { TmLazyComponent } from '../declarations/tm-declaration-types';

// The components the route table loads from other entry points: the shells,
// and the hosts of the page families (spec 0024 §9.2, §4.4). Until each
// family's entry point lands, its host is the shell's placeholder page; the
// search family's is @tellma/core/search and the details family's is
// @tellma/core/details (spec 0031 §1).

/**
 * The components the primary entry point loads from `@tellma/core/shell`,
 * by name, each through its loader. Internal: the shell's `ɵTM_SHELL`
 * satisfies it, so a component either side renames or drops fails the
 * build. A loader lets the shell keep a component in a chunk of its own,
 * such as the tenantless pages, which no tenant route's first frame needs.
 */
export interface TmShellComponents {
  /** The tenant shell, the tenant route's component. */
  readonly TmTenantShell: () => Promise<Type<unknown>>;
  /** The tenant picker at `/tenants`. */
  readonly TmTenantPicker: () => Promise<Type<unknown>>;
  /** The page at `/signed-out`. */
  readonly TmSignedOutPage: () => Promise<Type<unknown>>;
  /** The page of every family without an entry point of its own. */
  readonly TmPagePlaceholder: () => Promise<Type<unknown>>;
}

/** The shell entry point's module, as far as the primary reads it. */
interface TmShellModule {
  /** The shell's components' loaders, by name. */
  readonly ɵTM_SHELL: TmShellComponents;
}

/**
 * The shell's components that have loaded, by name: each is kept once its
 * own loader settles, since a loader may load a chunk of its own.
 */
const loaded: { -readonly [Name in keyof TmShellComponents]?: Type<unknown> } = {};

/**
 * Loads a component of `@tellma/core/shell`.
 *
 * The specifier is cast so that TypeScript does not resolve it: the shell
 * imports this entry point, and the package build would report the dynamic
 * import back as a cycle between the two. The emitted code keeps the literal
 * `import('@tellma/core/shell')`, which the app's build splits into the
 * shell's chunk. The names are {@link TmShellComponents}', which the shell's
 * own build checks.
 *
 * @param name - The component's name.
 * @returns The lazy component.
 */
export function tmShellComponent(name: keyof TmShellComponents): TmLazyComponent {
  return () =>
    (import('@tellma/core/shell' as string) as Promise<TmShellModule>)
      .then((shell) => shell.ɵTM_SHELL[name]())
      .then((component) => (loaded[name] = component));
}

/**
 * A route's `loadComponent` for a component of `@tellma/core/shell`: the
 * component itself once its load has settled, else its load. Internal: the
 * route table's shell routes use it.
 *
 * @param name - The component's name.
 * @returns The loader.
 */
export function tmShellRoute(name: keyof TmShellComponents): () => Type<unknown> | ReturnType<TmLazyComponent> {
  // A later entry into a route whose component is already in memory
  // activates within the click's task, so its first frame shows the route
  // (spec 0024 §4.4); a promise would wait a task, and miss that frame.
  const load = tmShellComponent(name);
  return () => loaded[name] ?? load();
}

/** The page every family without an entry point of its own renders for now. */
const placeholder = tmShellComponent('TmPagePlaceholder');

/** What `@tellma/core/search` holds for the route table. */
interface TmSearchModule {
  /** The search host. */
  readonly TmSearchHost: Type<unknown>;
}

/** The search host, from its own entry point, which the app's build splits into a chunk of its own. */
const search: TmLazyComponent = () => (import('@tellma/core/search' as string) as Promise<TmSearchModule>).then((module) => module.TmSearchHost);

/** The details entry point's module, as far as the primary reads it. */
interface TmDetailsModule {
  /** The routed details host. */
  readonly TmDetailsHost: Type<unknown>;
}

/**
 * Loads the details host from `@tellma/core/details`, whose specifier is
 * cast for the reason {@link tmShellComponent} gives: the details entry
 * point imports this one.
 *
 * @returns The host's component.
 */
const details: TmLazyComponent = () =>
  (import('@tellma/core/details' as string) as Promise<TmDetailsModule>).then((module) => module.TmDetailsHost);

/** The host of each route family whose component the route data does not name. */
export const TM_ROUTE_HOSTS = {
  /** The tenant home's host, which picks the `home` hatch or the default home. */
  home: placeholder,
  /** The search host: an entity's search page and a drill-through list, which hosts the entity's `search` hatch. */
  search,
  /** The details host, which hosts an entity's `details` page, for create and view. */
  details,
  /** The coded report host. */
  report: placeholder,
  /** The settings page. */
  settings: placeholder,
  /** The My settings hub. */
  me: placeholder,
} as const satisfies Readonly<Record<string, TmLazyComponent>>;

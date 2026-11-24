// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Type } from '@angular/core';

import type { TmLazyComponent } from '../declarations/tm-declaration-types';

// The components the route table loads from other entry points: the shells,
// and the hosts of the page families (spec 0024 §9.2, §4.4). Until each
// family's entry point lands, its host is the shell's placeholder page.

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
  return () => (import('@tellma/core/shell' as string) as Promise<TmShellModule>).then((shell) => shell.ɵTM_SHELL[name]());
}

/** The page every family without an entry point of its own renders for now. */
const placeholder = tmShellComponent('TmPagePlaceholder');

/** The host of each route family whose component the route data does not name. */
export const TM_ROUTE_HOSTS = {
  /** The tenant home's host, which picks the `home` hatch or the default home. */
  home: placeholder,
  /** The search host, which hosts an entity's `search` hatch. */
  search: placeholder,
  /** The details host, which hosts an entity's `details` page, for create and view. */
  details: placeholder,
  /** The coded report host. */
  report: placeholder,
  /** The settings page. */
  settings: placeholder,
  /** The My settings hub. */
  me: placeholder,
} as const satisfies Readonly<Record<string, TmLazyComponent>>;

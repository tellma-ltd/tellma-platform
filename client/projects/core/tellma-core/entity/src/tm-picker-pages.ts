// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { InjectionToken, type Type } from '@angular/core';

// The contract between the picker page that an entity picker opens (spec
// 0029 §14.4) and the page families that fill it: what the page is opened
// for, and the seam each family provides its half of. It lives on the view
// side, so a page family provides its half, and reads the context, without
// reaching the editing entry point that holds the picker page itself.

/**
 * What an entity picker's page is opened for, in the page's
 * `TmEntityPickerPageData.context`: the entity it serves, the field's own
 * filter beside the entity's declared one, and the page's kind.
 */
export interface TmEntityPickerContext {
  /** The entity's name, such as `gl.Center`. */
  readonly entity: string;
  /** The field's Queryex filter, applied beside the entity's declared picker filter; `null` for none. */
  readonly filter: string | null;
  /** Advanced search, a new record, or the chosen record's details. */
  readonly kind: 'search' | 'create' | 'edit';
}

/**
 * A page family's half of the picker pages: the loader of the component
 * its picker mode renders. Internal: the search and details pages each
 * provide theirs.
 */
export interface ɵTmPickerPages {
  /** Loads the search page's picker mode, which advanced search renders. */
  readonly search?: () => Promise<Type<unknown>>;
  /** Loads the details page's picker mode, which Create and Edit render. */
  readonly details?: () => Promise<Type<unknown>>;
}

/**
 * The picker pages, a multi-provider each page family adds its half to, so
 * neither replaces the other; a later provider of the same half wins. A
 * family loads its half with an `import()` of its own entry point, which
 * this one never reaches statically. Both halves must be provided at the
 * root environment injector, in the app's providers: `TmModal` opens its
 * dialogs without an injector of their own, so a provider on a route or a
 * component is never seen. Internal: with none, every page shows as not
 * available.
 *
 * @example
 * ```ts
 * // The app's providers, beside the search family's routes.
 * { provide: ɵTM_PICKER_PAGES, multi: true, useValue: { search: () => import('@tellma/core/search').then((m) => m.TmSearchPickerPage) } }
 * ```
 */
export const ɵTM_PICKER_PAGES = new InjectionToken<readonly ɵTmPickerPages[]>('TM_PICKER_PAGES');

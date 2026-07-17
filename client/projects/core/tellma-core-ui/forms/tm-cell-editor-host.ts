// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { InjectionToken } from '@angular/core';

import type { TmCellEditorHost } from '@tellma/core-ui/contracts';

/**
 * The registration sink a grid provides to the cell-editor views it creates.
 *
 * A grid instantiates each editor view with an injector that provides this
 * token; every grid-embeddable form control injects it OPTIONALLY and, when
 * present, registers itself as the cell's `TmCellEditor` on construction.
 * Outside a grid cell the token is absent and registration is a no-op —
 * standalone usage is unaffected.
 *
 * The token lives in the primary entry point — component-free and already a
 * dependency of every control — so controls need no import from the grid
 * package and no control↔grid entry-point cycle arises.
 */
export const TM_CELL_EDITOR_HOST = new InjectionToken<TmCellEditorHost>('TM_CELL_EDITOR_HOST');

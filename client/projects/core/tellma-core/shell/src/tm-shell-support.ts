// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { InjectionToken, type Signal, type Type } from '@angular/core';

// The seam the support entry point fills (spec 0027 §10.1 row 8, §11.1):
// the session menu's Report a problem row and the About dialog's
// Diagnostics tab. Until an app provides it, the menu has no such row and
// About shows its About tab alone, with no tab strip. The support entry
// point names both in its own strings, so the shell carries none of them.

/** What the support entry point gives the tenant shell. Internal: provided by the support entry point. */
export interface TmShellSupport {
  /** The session menu row's label, "Report a problem", in the active language. */
  readonly reportLabel: Signal<string>;
  /** Opens the support dialog on its Report a problem form, from the session menu's row. */
  readonly reportProblem: () => void;
  /** The About dialog's second tab's label, "Diagnostics", in the active language. */
  readonly diagnosticsLabel: Signal<string>;
  /** Loads the About dialog's Diagnostics tab's content, on the tab's first selection. */
  readonly diagnostics: () => Promise<Type<unknown>>;
}

/** The support entry point's seam in the tenant shell; absent until an app provides it. Internal. */
export const TM_SHELL_SUPPORT = new InjectionToken<TmShellSupport>('TM_SHELL_SUPPORT');

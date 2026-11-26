// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, InjectionToken, type Type } from '@angular/core';
import { TmSupport, ɵTM_SUPPORT_LOAD, ɵTM_SUPPORT_TOOLS_LOAD } from '@tellma/core';

// What the tenant shell's overlays ask of the support tools (spec 0027 §10.1
// row 8, §11.1; spec 0026 §8.4, §12.1): the session menu's Report a problem
// opens the support dialog through the primary's facade, and the About
// dialog's Diagnostics tab loads the panel from @tellma/core/support on its
// first selection, or, when that entry point cannot load, the offline line
// from the support host's chunk, which the startup preloads at idle and so is
// in memory offline. The shell names both rows in its own strings, so they
// render before either chunk loads. Only the overlays' chunk reads this; it
// hands the seam to the About dialog as an input, so the dialog's chunk
// shares no module with it.

/** The support tools as the tenant shell's overlays use them. Internal. */
export interface TmShellSupport {
  /** Opens the support dialog, from the session menu's Report a problem. */
  readonly open: () => void;
  /** Loads the Diagnostics panel, the About dialog's Diagnostics tab. */
  readonly diagnostics: () => Promise<Type<unknown>>;
  /** Loads the line the Diagnostics tab shows when the panel cannot load. */
  readonly offline: () => Promise<Type<unknown>>;
}

/** The support tools in the tenant shell, over the primary's support facade. Internal: a test replaces it. */
export const TM_SHELL_SUPPORT = new InjectionToken<TmShellSupport>('TM_SHELL_SUPPORT', {
  providedIn: 'root',
  factory: () => {
    const support = inject(TmSupport);
    const loadSupport = inject(ɵTM_SUPPORT_LOAD);
    const loadTools = inject(ɵTM_SUPPORT_TOOLS_LOAD);
    return {
      open: () => support.open(),
      diagnostics: () => loadSupport().then((module) => module.TmDiagnosticsPanel),
      offline: () => loadTools().then((tools) => tools.host.ɵTmSupportOffline),
    };
  },
});

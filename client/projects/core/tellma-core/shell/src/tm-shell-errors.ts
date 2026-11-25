// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { InjectionToken } from '@angular/core';

// The error views both shells render inline: the tenant picker's failed read
// and the tenant shell's failed cold sync (spec 0027 §6.2, §7.5). They live in
// the lazy @tellma/core/errors, which this entry point reaches only through
// import(), so neither shell's first frame carries them.

/**
 * Loads the error views the shells' error states render: `@tellma/core/errors`,
 * through `import()`. A test replaces it to see a shell without them.
 */
export const TM_SHELL_ERROR_VIEWS = new InjectionToken<() => Promise<typeof import('@tellma/core/errors')>>('TM_SHELL_ERROR_VIEWS', {
  providedIn: 'root',
  factory: () => () => import('@tellma/core/errors'),
});

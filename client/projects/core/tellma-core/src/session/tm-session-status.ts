// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injectable, signal } from '@angular/core';

// The writable state behind `TmSession.state`. The session shows it read-only;
// the session paths, which run the expiry and the sign-out, write it. Both
// inject this holder, so neither needs the other.

/** Where the tab's session stands. */
export type TmSessionState = 'active' | 'expired' | 'signing-out';

/** The writable state of the tab's session. */
@Injectable({ providedIn: 'root' })
export class TmSessionStatus {
  /** The state: active until an expiry or a sign-out. */
  readonly state = signal<TmSessionState>('active');
}

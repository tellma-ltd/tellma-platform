// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, inject, type Signal, untracked } from '@angular/core';
import { TmTelemetry, ɵTmLeaveChecks } from '@tellma/core';

import { TmDetailsDialogs } from './tm-details-dialogs';

// The in-app leave guard (spec 0031 §11.3, §8.7): a check the routed page's
// `canDeactivate` runs on every router navigation away (links, the launcher,
// pins, Back and Forward). A clean page leaves silently, a busy one refuses,
// and a dirty one asks. A refused navigation keeps the history index, as
// the router's computed resolution of a cancelled navigation does. Full-page
// navigations never reach it: the draft covers them, and the page never
// prompts on unload while dirty (§11.4).

/** What a leave guard reads: whether the page has unsaved changes, whether a write runs, and what Discard does. */
export interface TmLeaveGuardCheck {
  /** Whether the page has unsaved changes, which leaving asks about. */
  readonly dirty: Signal<boolean>;
  /** Whether a write is in flight, which refuses every navigation away; never when omitted. */
  readonly busy?: Signal<boolean>;
  /** What Discard changes does before the navigation proceeds, such as clearing the page's draft. */
  readonly discard?: () => void;
}

/**
 * Guards the routed page the caller renders in against leaving with unsaved
 * changes: while `busy`, every navigation away is refused; while `dirty`,
 * it asks "Leave without saving?", and Discard changes runs `discard` and
 * leaves while Stay or Escape stays. The check lives while `destroyRef`
 * does. Outside a routed page it does nothing. Call it in an injection
 * context.
 *
 * @param check - Whether the page is dirty and busy, and what Discard does.
 * @param destroyRef - The caller's life; the injection context's when omitted.
 *
 * @example
 * ```ts
 * tmLeaveGuard({ dirty: computed(() => this.model() !== this.saved()), busy: this.saving });
 * ```
 */
export function tmLeaveGuard(check: TmLeaveGuardCheck, destroyRef: DestroyRef = inject(DestroyRef)): void {
  const checks = inject(ɵTmLeaveChecks, { optional: true });
  if (checks === null) {
    return;
  }
  const dialogs = inject(TmDetailsDialogs);
  const telemetry = inject(TmTelemetry);
  checks.add(() => {
    // --- A write in flight refuses; a clean page leaves silently ---
    if (check.busy !== undefined && untracked(check.busy)) {
      return false;
    }
    if (!untracked(check.dirty)) {
      return true;
    }
    // --- A dirty one asks ---
    return dialogs.leave().then((choice) => {
      telemetry.event('tm.details.leave-guard', { choice });
      if (choice === 'discard') {
        check.discard?.();
      }
      return choice === 'discard';
    });
  }, destroyRef);
}

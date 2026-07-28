// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TemplateRef, Type } from '@angular/core';
import type { Observable } from 'rxjs';
import { hasModifierKey } from '@angular/cdk/keycodes';

/**
 * The typed outcome a modal's {@link TmModalRef.closed} promise resolves
 * with — every dismissal path is discriminated by `via`:
 *
 * - `'api'` — programmatic {@link TmModalRef.close}; carries the close value
 *   (or `undefined` when closed without one).
 * - `'close-button'` — the shell's X button.
 * - `'backdrop'` — a click on the backdrop scrim.
 * - `'escape'` — the Escape key.
 *
 * User dismissals (`'close-button'`/`'backdrop'`/`'escape'`) never carry a
 * value: a modal that produces a result returns it through `close(value)`.
 */
export type TmModalResult<R> =
  | { via: 'api'; value: R | undefined }
  | { via: 'close-button' | 'backdrop' | 'escape' };

/** The user-initiated dismissal paths (the ones `canDismiss` guards). */
type TmModalDismissVia = 'close-button' | 'backdrop' | 'escape';

/** The dismissal policy captured from the open config. @internal */
interface DismissalPolicy {
  readonly escapeDismiss: boolean;
  readonly backdropDismiss: boolean;
  readonly canDismiss: (() => boolean | Promise<boolean>) | undefined;
}

/**
 * The slice of the CDK `DialogRef` the ref actually drives — structural,
 * so the CDK type's invariant component parameter never leaks in here.
 * @internal
 */
interface AttachableDialogRef<R> {
  readonly closed: Observable<R | undefined>;
  readonly keydownEvents: Observable<KeyboardEvent>;
  readonly backdropClick: Observable<MouseEvent>;
  close(result?: R): void;
}

/**
 * Handle to an open modal: close it programmatically and observe its
 * outcome. Content components receive the ref via injection
 * (`inject(TmModalRef)`); the opener receives it from `TmModal.open`.
 *
 * All four close paths — programmatic, X button, backdrop, Escape —
 * funnel through this ref, so `closed` always resolves with an accurate
 * {@link TmModalResult} and the `canDismiss` guard is consulted before
 * every user-initiated dismissal.
 */
export class TmModalRef<R = void> {
  private dialogRef: AttachableDialogRef<TmModalResult<R>> | null = null;
  private pendingResult: TmModalResult<R> | null = null;
  private settled = false;
  private guardPending = false;
  private resolveClosed!: (result: TmModalResult<R>) => void;

  /**
   * Resolves once, when the modal has closed, with the result of whichever
   * dismissal path closed it. Never rejects.
   */
  readonly closed: Promise<TmModalResult<R>> = new Promise((resolve) => {
    this.resolveClosed = resolve;
  });

  /** @internal Constructed by `TmModal.open` only. */
  constructor(private readonly policy: DismissalPolicy) {}

  /**
   * Closes the modal programmatically. `closed` resolves with
   * `{ via: 'api', value }`. Not guarded by `canDismiss` — consumer code
   * owns its own calls.
   */
  close(value?: R): void {
    this.settle({ via: 'api', value });
  }

  /**
   * Runs a user-initiated dismissal through the `canDismiss` guard.
   * While an async guard is pending, further dismissal attempts are
   * ignored (one confirmation flow at a time); a guard that throws or
   * rejects keeps the modal open.
   * @internal
   */
  ɵdismiss(via: TmModalDismissVia): void {
    if (this.settled || this.guardPending) {
      return;
    }
    const guard = this.policy.canDismiss;
    if (!guard) {
      this.settle({ via });
      return;
    }
    let verdict: boolean | Promise<boolean>;
    try {
      verdict = guard();
    } catch {
      return;
    }
    if (typeof verdict === 'boolean') {
      if (verdict) {
        this.settle({ via });
      }
      return;
    }
    this.guardPending = true;
    verdict.then(
      (allowed) => {
        this.guardPending = false;
        if (allowed) {
          this.settle({ via });
        }
      },
      () => {
        this.guardPending = false;
      },
    );
  }

  /**
   * Wires the CDK dialog ref: result plumbing, Escape (skipping consumed
   * events — an inner overlay that handled its own Escape called
   * `preventDefault`), and backdrop clicks. The CDK dialog itself never
   * self-closes (`disableClose: true`), so every path flows through here.
   * @internal
   */
  ɵattach(dialogRef: AttachableDialogRef<TmModalResult<R>>): void {
    this.dialogRef = dialogRef;
    dialogRef.closed.subscribe((result) => {
      // An undefined result is a close that bypassed this ref (service
      // teardown, closeOnNavigation) — report it as an empty 'api' close.
      this.settled = true;
      this.resolveClosed(result ?? { via: 'api', value: undefined });
    });
    dialogRef.keydownEvents.subscribe((event) => {
      if (event.key !== 'Escape' || event.defaultPrevented || hasModifierKey(event)) {
        return;
      }
      if (this.policy.escapeDismiss) {
        event.preventDefault();
        this.ɵdismiss('escape');
      }
    });
    dialogRef.backdropClick.subscribe(() => {
      if (this.policy.backdropDismiss) {
        this.ɵdismiss('backdrop');
      }
    });
    if (this.pendingResult !== null) {
      dialogRef.close(this.pendingResult);
    }
  }

  private settle(result: TmModalResult<R>): void {
    if (this.settled) {
      return;
    }
    this.settled = true;
    if (this.dialogRef) {
      this.dialogRef.close(result);
    } else {
      // close() during the shell's own construction — apply on attach.
      this.pendingResult = result;
    }
  }
}

/**
 * The shell's `DIALOG_DATA` payload, built by `TmModal.open`.
 * @internal
 */
export interface ɵTmModalShellPayload {
  readonly component: Type<unknown> | null;
  readonly template: TemplateRef<unknown> | null;
  readonly title: string | undefined;
  readonly titleId: string;
  readonly showClose: boolean;
  readonly data: unknown;
  readonly ref: TmModalRef<unknown>;
}

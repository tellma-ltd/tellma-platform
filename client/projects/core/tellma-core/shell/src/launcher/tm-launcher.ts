// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injectable, type Signal, signal } from '@angular/core';

// The launcher's contract (spec 0027 §9.2), as the app bar calls it: the
// Menu button, the search field and the search button open it, the field
// shows and edits its kept text, and the tenant shell closes it and clears
// the text on a tenant switch. This is the contract with no surface behind
// it yet: show() opens nothing, so the state stays closed; the launcher's
// overlay replaces these bodies. The text belongs to the tenant it was typed
// in: a tenant switch, through /tenants or a deep link, clears it.

/** What opened the launcher, as `tm.launcher.opened` reports it. */
export type TmLauncherVia = 'menu' | 'field' | 'shortcut' | 'icon' | 'link';

/** Why the launcher closed. */
export type TmLauncherCloseReason = 'escape' | 'outside' | 'tab' | 'chosen' | 'route';

/** The launcher: its state, its kept text, and the calls that open and close it. */
@Injectable({ providedIn: 'root' })
export class TmLauncher {
  readonly #query = signal('');
  // The tenant the text was typed in, or `null` before any.
  #tenantId: number | null = null;

  /** Whether the launcher is closed, browsing, or listing matches for typed text. */
  readonly state: Signal<'closed' | 'browse' | 'typed'> = signal<'closed' | 'browse' | 'typed'>('closed').asReadonly();

  /** The launcher's text, kept across close and navigation in this tab. */
  readonly query: Signal<string> = this.#query.asReadonly();

  /**
   * Opens the launcher.
   *
   * @param via - What opened it.
   */
  show(via: TmLauncherVia): void {
    // No surface yet: nothing opens.
    void via;
  }

  /**
   * Closes the launcher, keeping its text.
   *
   * @param reason - Why it closed.
   */
  close(reason: TmLauncherCloseReason): void {
    // No surface yet: nothing is open.
    void reason;
  }

  /**
   * Keeps the text for the tenant it was typed in: entering another tenant
   * closes the launcher and clears it, as the tenant shell asks on each
   * entry. A tenant shell lives for one tenant, so the launcher, which
   * outlives it, remembers the tenant.
   *
   * @param tenantId - The tenant the shell entered.
   */
  enterTenant(tenantId: number): void {
    if (this.#tenantId !== null && this.#tenantId !== tenantId) {
      this.close('route');
      this.#query.set('');
    }
    this.#tenantId = tenantId;
  }

  /**
   * Replaces the launcher's text.
   *
   * @param text - The new text; `''` clears it.
   */
  setQuery(text: string): void {
    this.#query.set(text);
  }
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Signal } from '@angular/core';
import type { TmKeys } from '@tellma/core-ui/keys';

import type { TmActionConfirmation } from '../entity/tm-entity-declaration';
import type { TmJson, TmJsonObject, TmJsonSchema } from '../json/tm-json';

// The page action's contracts (spec 0026 §9.4). A page action is a command a
// page exposes; the toolbar, the Actions menu, a shortcut and a future
// assistant are views of it, and every one of them invokes it through the
// one dispatcher, `TmPageActions.invoke`.

/** Who invoked an action: a view the user clicked, a shortcut, the launcher or the assistant. */
export type TmActionOrigin = 'user' | 'shortcut' | 'launcher' | 'assistant';

/**
 * How an invocation ended: `done`, with the action's data if any;
 * `cancelled` when the user declined its confirmation or its warnings;
 * `refused` without running, because no context offers the action
 * (`not-found`), it is disabled (`disabled`, with its reason as `message`)
 * or it is already running (`busy`); or `failed` when it threw.
 */
export type TmActionResult =
  | { readonly outcome: 'done'; readonly data?: TmJson }
  | { readonly outcome: 'cancelled' }
  | { readonly outcome: 'refused'; readonly reason: 'not-found' | 'disabled' | 'busy'; readonly message?: string }
  | { readonly outcome: 'failed'; readonly error: unknown };

/**
 * A command a page or a dialog exposes. Views read it and call
 * `TmPageActions.invoke(action.id, {}, 'user')`, never `invoke` directly, so
 * the permission, double-submit and confirmation rules hold in one place.
 */
export interface TmPageAction {
  /**
   * The action's id: `details.save` for a framework's, `gl.center.activate`
   * for an entity's, `acme.credit-review.suggest-limits` for a
   * distribution's. It matches `^[a-z][a-z0-9]*(\.[a-z0-9][a-z0-9-]*){1,4}$`.
   */
  readonly id: string;
  /** The string key of the action's label. */
  readonly labelKey: string;
  /** The string key of what the action does, for the assistant. */
  readonly descriptionKey?: string;
  /** A registered icon name. */
  readonly icon?: string;
  /** Where views show it: the toolbar (`primary`), or the Actions menu and the overflow (`menu`). */
  readonly group: 'primary' | 'menu';
  /** Its place within its group; default 100. */
  readonly order?: number;
  /** Whether it may run now; permissions included. */
  readonly enabled: Signal<boolean>;
  /** Whether running it destroys something, so it asks for `confirm` first. */
  readonly destructive: boolean;
  /**
   * Why it is disabled, localized, while a permission or a record rule
   * disables it; `null` while it is enabled or disabled for a transient
   * cause, such as loading.
   */
  readonly disabledReason: Signal<string | null>;
  /**
   * The confirmation it asks for; required when `destructive`. Its
   * arguments may also be a function of the invocation's, so the texts can
   * follow what is being confirmed.
   */
  readonly confirm?: Omit<TmActionConfirmation, 'args'> & {
    /** The arguments of the title, the message and the confirm label. */
    readonly args?: Signal<TmJsonObject> | ((args: TmJson) => TmJsonObject);
  };
  /** Its shortcut, live while its context is on top. An action whose `params` require members takes none. */
  readonly shortcut?: TmKeys;
  /** The JSON Schema (2020-12) object schema of its arguments. */
  readonly params?: TmJsonSchema;
  /**
   * Runs the action. Called only by the dispatcher.
   *
   * @param args - The arguments, as `params` describes them; `{}` from a view or a shortcut.
   * @param origin - Who invoked it.
   * @returns How it ended: `done`, or `cancelled` when the user declined its warnings.
   */
  invoke(args: TmJson, origin: TmActionOrigin): Promise<TmActionResult>;
}

/**
 * A page action as `TmPageActions.register` takes it: `group` defaults to
 * `menu`, `enabled` to always, `destructive` to `false` and
 * `disabledReason` to `null`.
 */
export type TmPageActionInput = Omit<TmPageAction, 'group' | 'enabled' | 'destructive' | 'disabledReason'> &
  Partial<Pick<TmPageAction, 'group' | 'enabled' | 'destructive' | 'disabledReason'>>;

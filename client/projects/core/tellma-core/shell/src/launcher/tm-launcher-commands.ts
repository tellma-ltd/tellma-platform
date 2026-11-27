// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, InjectionToken, type Signal } from '@angular/core';

import { TmPins } from '../pins/tm-pins';

// The launcher Pinned band's commands (spec 0027 §9.3): "Pin this page" (or
// "Edit pin") and "Manage pins…", which the pins service provides with the
// commands it runs while a tenant shell renders. They follow the routed
// page, so they are a signal. With none, the band lists the pins alone and
// shows only when there are pins.

/** A command option of the launcher's Pinned band. Internal. */
export interface TmLauncherCommand {
  /** The command's id, stable for the command. */
  readonly id: string;
  /** The option's label, in the active language. */
  readonly label: string;
  /** The option's icon's registered name. */
  readonly icon: string;
  /** Runs the command once the launcher has closed. */
  run(): void;
}

/**
 * The Pinned band's commands, in the band's order after the pins: the pins
 * service's, which are the tenant shell's pin commands while it renders.
 * Internal: a test provides its own.
 */
export const TM_LAUNCHER_COMMANDS = new InjectionToken<Signal<readonly TmLauncherCommand[]>>('TM_LAUNCHER_COMMANDS', {
  providedIn: 'root',
  factory: () => inject(TmPins).ɵlauncherCommands,
});

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, inject, InjectionToken, type Signal } from '@angular/core';
import { TmLocalization } from '@tellma/core';

import { tmRegisterPinCommandsStrings } from '../pins/tm-pin-commands-strings';
import { TmPins } from '../pins/tm-pins';

// The launcher Pinned band's commands (spec 0027 §9.3): "Pin this page" (or
// "Edit pin"), then "Manage pins…", once the pins have loaded. A pin
// command the pin control holds (the cap, a long link, the tenant's
// maintenance) stays listed and says why in a toast when chosen. They run
// the tenant shell's pin controls, which the pins service holds while that
// shell renders, and follow the routed page, so they are a signal. They are
// built here, in the launcher panel's chunk, so the shell's first frame
// carries none of them. With no shell, the band lists the pins alone and
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
 * The Pinned band's commands, in the band's order after the pins: the
 * tenant shell's pin commands while it renders, else none. Internal: a test
 * provides its own.
 */
export const TM_LAUNCHER_COMMANDS = new InjectionToken<Signal<readonly TmLauncherCommand[]>>('TM_LAUNCHER_COMMANDS', {
  providedIn: 'root',
  factory: () => {
    const pins = inject(TmPins);
    const l10n = inject(TmLocalization);
    tmRegisterPinCommandsStrings(l10n);
    return computed(() => {
      const controls = pins.ɵcontrols();
      // Before the pins load, nothing may edit them.
      if (controls === null || pins.ɵrefusal() === 'cold') {
        return [];
      }
      const state = controls.control();
      const commands: TmLauncherCommand[] = [];
      // A page with nothing to pin offers no pin command; Manage pins stays.
      if (!state.inert) {
        commands.push({
          id: state.pinned ? 'tm.pins.edit' : 'tm.pins.pin',
          label: l10n.ɵtext(state.pinned ? 'tm.pins.edit' : 'tm.pins.pin')(),
          icon: 'pin',
          run: () => controls.press('menu', null),
        });
      }
      commands.push({ id: 'tm.pins.manage', label: l10n.ɵtext('tm.pinCommands.manage')(), icon: 'settings-2', run: () => controls.manage() });
      return commands;
    });
  },
});

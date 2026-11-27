// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_PINS_STRINGS_AR } from '@tellma/locale-ar/pins-strings';
import { TM_SHELL_STRINGS_AR } from '@tellma/locale-ar/shell-strings';

import { TM_SHELL_HISTORY_STRINGS_EN } from './app-bar/tm-shell-history';
import { TM_ABOUT_DIALOG_STRINGS_EN } from './dialogs/tm-about-dialog';
import { TM_SHORTCUTS_DIALOG_STRINGS_EN } from './dialogs/tm-shortcuts-dialog';
import { TM_LAUNCHER_STRINGS_EN } from './launcher/tm-launcher-strings';
import { TM_MANAGE_PINS_STRINGS_EN } from './pins/tm-manage-pins-dialog';
import { TM_PIN_COMMANDS_STRINGS_EN } from './pins/tm-pin-commands-strings';
import { TM_PIN_OVERLAYS_STRINGS_EN } from './pins/tm-pin-overlays';
import { TM_PINS_STRINGS_EN } from './pins/tm-pins-strings';
import { TM_SHELL_OVERLAYS_STRINGS_EN } from './session/tm-shell-overlays-strings';
import { TM_TENANT_SHELL_STRINGS_EN } from './tm-tenant-shell-strings';
import { TM_TENANTLESS_STRINGS_EN } from './tenantless/tm-tenantless-strings';

// The shell's English tables against their Arabic: the same keys, and each
// Arabic message takes the English one's arguments. A key Arabic lacks falls
// back to English mid-sentence; an argument it drops deletes a value from
// the sentence. Arabic may also branch on the ambient gender, which English
// never names.

/** The shell's English tables, by their scope under `tm`, each with the Arabic table that holds that scope. */
const ENGLISH: Readonly<Record<string, readonly [english: unknown, arabic: Readonly<Record<string, unknown>>]>> = {
  tenantless: [TM_TENANTLESS_STRINGS_EN, TM_SHELL_STRINGS_AR],
  tenantShell: [TM_TENANT_SHELL_STRINGS_EN, TM_SHELL_STRINGS_AR],
  shellOverlays: [TM_SHELL_OVERLAYS_STRINGS_EN, TM_SHELL_STRINGS_AR],
  shellHistory: [TM_SHELL_HISTORY_STRINGS_EN, TM_SHELL_STRINGS_AR],
  aboutDialog: [TM_ABOUT_DIALOG_STRINGS_EN, TM_SHELL_STRINGS_AR],
  shortcutsDialog: [TM_SHORTCUTS_DIALOG_STRINGS_EN, TM_SHELL_STRINGS_AR],
  launcher: [TM_LAUNCHER_STRINGS_EN, TM_SHELL_STRINGS_AR],
  pins: [TM_PINS_STRINGS_EN, TM_PINS_STRINGS_AR],
  pinOverlays: [TM_PIN_OVERLAYS_STRINGS_EN, TM_PINS_STRINGS_AR],
  pinCommands: [TM_PIN_COMMANDS_STRINGS_EN, TM_PINS_STRINGS_AR],
  managePins: [TM_MANAGE_PINS_STRINGS_EN, TM_PINS_STRINGS_AR],
};

/** The arguments the session supplies rather than the caller. */
const AMBIENT = new Set(['gender']);

/** Every leaf's path and message in a table. */
function leaves(node: unknown, prefix = ''): Map<string, string> {
  const into = new Map<string, string>();
  if (typeof node === 'string') {
    return into.set(prefix, node);
  }
  for (const [key, value] of Object.entries(node as Record<string, unknown>)) {
    for (const [path, message] of leaves(value, prefix === '' ? key : `${prefix}.${key}`)) {
      into.set(path, message);
    }
  }
  return into;
}

/** The argument names a message references, the ambient ones left out. */
function argumentsOf(message: string): string[] {
  return [...new Set([...message.matchAll(/\{([A-Za-z]\w*)\s*[,}]/g)].map((match) => match[1]))].filter((name) => !AMBIENT.has(name)).sort();
}

describe("the shell's Arabic", () => {
  for (const [scope, [english, arabic]] of Object.entries(ENGLISH)) {
    it(`covers exactly the English keys of ${scope}, each with the English arguments`, () => {
      const englishLeaves = leaves(english);
      const arabicLeaves = leaves(arabic[scope]);
      expect([...arabicLeaves.keys()].sort()).toEqual([...englishLeaves.keys()].sort());
      for (const [path, message] of englishLeaves) {
        expect({ path, args: argumentsOf(arabicLeaves.get(path) ?? '') }).toEqual({ path, args: argumentsOf(message) });
      }
    });
  }
});

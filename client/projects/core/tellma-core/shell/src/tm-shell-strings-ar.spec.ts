// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_SHELL_STRINGS_AR } from '@tellma/locale-ar/shell-strings';

import { TM_SHELL_HISTORY_STRINGS_EN } from './app-bar/tm-shell-history';
import { TM_ABOUT_DIALOG_STRINGS_EN } from './dialogs/tm-about-dialog';
import { TM_SHORTCUTS_DIALOG_STRINGS_EN } from './dialogs/tm-shortcuts-dialog';
import { TM_SHELL_OVERLAYS_STRINGS_EN } from './session/tm-shell-overlays-strings';
import { TM_TENANT_SHELL_STRINGS_EN } from './tm-tenant-shell-strings';
import { TM_TENANTLESS_STRINGS_EN } from './tenantless/tm-tenantless-strings';

// The shell's English tables against their Arabic: the same keys, and each
// Arabic message takes the English one's arguments. A key Arabic lacks falls
// back to English mid-sentence; an argument it drops deletes a value from
// the sentence. Arabic may also branch on the ambient gender, which English
// never names.

/** The shell's English tables, by their scope under `tm`. */
const ENGLISH: Readonly<Record<string, unknown>> = {
  tenantless: TM_TENANTLESS_STRINGS_EN,
  tenantShell: TM_TENANT_SHELL_STRINGS_EN,
  shellOverlays: TM_SHELL_OVERLAYS_STRINGS_EN,
  shellHistory: TM_SHELL_HISTORY_STRINGS_EN,
  aboutDialog: TM_ABOUT_DIALOG_STRINGS_EN,
  shortcutsDialog: TM_SHORTCUTS_DIALOG_STRINGS_EN,
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
  for (const [scope, english] of Object.entries(ENGLISH)) {
    it(`covers exactly the English keys of ${scope}, each with the English arguments`, () => {
      const englishLeaves = leaves(english);
      const arabicLeaves = leaves(TM_SHELL_STRINGS_AR[scope]);
      expect([...arabicLeaves.keys()].sort()).toEqual([...englishLeaves.keys()].sort());
      for (const [path, message] of englishLeaves) {
        expect({ path, args: argumentsOf(arabicLeaves.get(path) ?? '') }).toEqual({ path, args: argumentsOf(message) });
      }
    });
  }
});

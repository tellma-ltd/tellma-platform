// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_SUPPORT_STRINGS_AR } from '@tellma/locale-ar/support-strings';

import type { TmStringTree } from '../app/tm-app-config';
import { TM_SUPPORT_HOST_STRINGS_EN } from './tm-support-offline';

// The strings the support dialog shows before the support tools load, against
// the Arabic pack's part for them: a key the pack lacks falls back to English,
// and an argument it drops or invents renders the keys in the wrong place.

/**
 * Each message of a string tree by its dotted key path.
 *
 * @param tree - The tree.
 * @param prefix - The path so far.
 * @returns The messages, by path.
 */
function messages(tree: TmStringTree, prefix = ''): Map<string, string> {
  const found = new Map<string, string>();
  for (const [key, value] of Object.entries(tree)) {
    const path = `${prefix}${key}`;
    if (typeof value === 'string') {
      found.set(path, value);
    } else {
      for (const [inner, message] of messages(value, `${path}.`)) {
        found.set(inner, message);
      }
    }
  }
  return found;
}

/**
 * The arguments a message names, sorted.
 *
 * @param message - The message.
 * @returns The names.
 */
function argumentsOf(message: string | undefined): string[] {
  return [...(message ?? '').matchAll(/\{(\w+)/g)].map(([, name]) => name!).sort();
}

describe("the support dialog's first strings", () => {
  it('have their Arabic under the same keys, each message with the English arguments', () => {
    const english = messages({ supportHost: TM_SUPPORT_HOST_STRINGS_EN });
    const arabic = new Map([...messages(TM_SUPPORT_STRINGS_AR)].filter(([path]) => path.startsWith('supportHost.')));

    expect([...arabic.keys()].sort()).toEqual([...english.keys()].sort());
    for (const [path, message] of english) {
      // The addressee's gender is the context's, not the caller's, so Arabic alone names it.
      const names = argumentsOf(arabic.get(path)).filter((name) => name !== 'gender');
      expect({ path, names }).toEqual({ path, names: argumentsOf(message) });
    }
  });
});

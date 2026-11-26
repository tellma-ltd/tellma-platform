// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStringTree } from '@tellma/core';
import { TM_DETAILS_STRINGS_AR } from '@tellma/locale-ar/details-strings';

import { TM_DETAILS_STRINGS_EN } from './tm-details-strings';

// The details page's strings against the Arabic pack's part for them: a key
// the pack lacks falls back to English mid-page, and an argument it drops or
// invents deletes a value from the sentence or renders empty text.

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

describe("the details page's strings", () => {
  it('have their Arabic under the same keys, each message with the English arguments', () => {
    const english = messages({ details: TM_DETAILS_STRINGS_EN });
    const arabic = messages(TM_DETAILS_STRINGS_AR);

    expect([...arabic.keys()].sort()).toEqual([...english.keys()].sort());
    for (const [path, message] of english) {
      expect({ path, names: argumentsOf(arabic.get(path)) }).toEqual({ path, names: argumentsOf(message) });
    }
  });
});

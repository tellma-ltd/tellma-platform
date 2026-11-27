// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmJson } from '@tellma/core';

// Stored message arguments as the server reads them (spec 0021 §2.4): the
// top-level members of one JSON object. Strings and numbers pass as they
// are; booleans become ICU's "true" and "false", which a select's keys
// name; a localized text (an array of exactly three elements, the first a
// string and the others strings or null) passes as the array, which
// TmServerText resolves to the active content slot; any other array or
// object becomes its JSON text. Text that is not one JSON object gives no
// arguments, so the message renders without them.

/**
 * Parses stored arguments into a message's ICU arguments.
 *
 * @param json - The arguments as JSON text, or `null`.
 * @returns The arguments, or `undefined` when there are none.
 */
export function tmIcuArguments(json: string | null | undefined): Record<string, TmJson> | undefined {
  if (typeof json !== 'string') {
    return undefined;
  }
  let parsed: unknown;
  try {
    parsed = JSON.parse(json);
  } catch {
    return undefined;
  }
  return tmIcuObject(parsed);
}

/**
 * Normalizes an already parsed value into ICU arguments: each member of an
 * object by the rules above.
 *
 * @param value - The parsed value.
 * @returns The arguments, or `undefined` when the value is not an object.
 */
export function tmIcuObject(value: unknown): Record<string, TmJson> | undefined {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return undefined;
  }
  const args: Record<string, TmJson> = {};
  for (const [name, member] of Object.entries(value as Readonly<Record<string, TmJson>>)) {
    args[name] =
      typeof member === 'boolean'
        ? String(member)
        : Array.isArray(member)
          ? isLocalizedText(member)
            ? member
            : JSON.stringify(member)
          : typeof member === 'object' && member !== null
            ? JSON.stringify(member)
            : member;
  }
  return args;
}

/** Whether an array has a localized text's shape: three slots, the first text, the others text or null. */
function isLocalizedText(value: readonly TmJson[]): boolean {
  return (
    value.length === 3 &&
    typeof value[0] === 'string' &&
    value.slice(1).every((slot) => slot === null || typeof slot === 'string')
  );
}

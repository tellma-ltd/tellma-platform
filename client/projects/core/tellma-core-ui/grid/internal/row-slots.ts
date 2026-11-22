// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Gives each rendered row the slot that tracks its row element.
 *
 * A row keeps its slot while it stays rendered, so its element follows it
 * through a scroll or a data change. A row that enters takes the slot of one
 * that left, the last of them first: the rows a scroll brings in at one end
 * take the elements of those that left the other end in mirror order, which
 * lets the list reconcile a scroll in either direction by moving only the
 * elements it re-binds. A row with no slot left to take gets a new one.
 *
 * `retired` names a row whose slot no entering row may take in this render:
 * the row that was active, so a move of the active cell to a row that is
 * just coming in lands on a fresh element and the browser reports the focus
 * change, rather than re-binding the focused element in place.
 *
 * @param keys The rendered rows' keys, in render order.
 * @param previous Each key's slot from the last render, in that render's order.
 * @param retired The key whose slot stays out of reuse, if it left.
 * @returns Each rendered key's slot.
 */
export function ɵtmGridAssignRowSlots(
  keys: readonly string[],
  previous: ReadonlyMap<string, number>,
  retired: string | null,
): Map<string, number> {
  const rendered = new Set(keys);
  // The slots of the rows that left, in the order they were rendered.
  const free: number[] = [];
  let next = 0;
  for (const [key, slot] of previous) {
    next = Math.max(next, slot + 1);
    if (!rendered.has(key) && key !== retired) {
      free.push(slot);
    }
  }
  const slots = new Map<string, number>();
  for (const key of keys) {
    slots.set(key, previous.get(key) ?? free.pop() ?? next++);
  }
  return slots;
}

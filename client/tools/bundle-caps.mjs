// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * The size gate's caps: error levels on whole entry points that the
 * platform's specs set, as opposed to the ratchet ceilings each library
 * declares in "tellma".budgetsInKb. A cap is measured on the same linked,
 * bundled-alone weight as the ceiling, every member of the entry point
 * counted whether or not an app uses it, but at gzip level 9, the level the
 * caps are stated at.
 *
 * The platform holds each cap's level here. A library raises one in its
 * package.json, and only with a note saying why:
 *
 *   "tellma": { "capsInKb": { ".": { "error": 52, "note": "…" } } }
 *
 * A lower level needs no note.
 */

/** The gzip level the caps are measured at. */
export const CAP_GZIP_LEVEL = 9;

/**
 * The platform's caps in KB (1,024 bytes): by package name, then by entry
 * point id.
 */
export const ENTRY_POINT_CAPS = Object.freeze({
  // Spec 0024 §12: the @tellma/core primary's own weight, the sum of its
  // shares (0024's services, the icon data and 0025–0029, 0032, 0034 and
  // 0035's), is capped at 50 KB.
  '@tellma/core': Object.freeze({ '.': 50 }),
});

/**
 * @typedef {object} TmEntryPointCap
 * @property {string} id          the entry point's id, '.' for the primary
 * @property {number} kb          the level in force, in KB
 * @property {number} platformKb  the platform's level, in KB
 */

/**
 * Reads a library's caps: the platform's level for each of its capped entry
 * points, as the library's "tellma".capsInKb sets it, and every problem with
 * that member — a raise without a note among them.
 *
 * @param {{ name: string, tellma?: any, entryPoints: readonly { id: string }[] }} library
 *   the library, as tools/workspace.mjs discovers it
 * @returns {{ caps: TmEntryPointCap[], problems: string[] }} the caps, and one
 *   line per problem
 */
export function readEntryPointCaps(library) {
  const platform = ENTRY_POINT_CAPS[library.name] ?? {};
  const raw = library.tellma?.capsInKb;
  const problems = [];
  // --- The library's settings: an object of { error, note } by entry point ---
  /** @type {Record<string, unknown>} */
  let settings = {};
  if (raw !== undefined) {
    if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) {
      problems.push(`${library.name}: "tellma".capsInKb must be an object of caps by entry point.`);
    } else {
      settings = raw;
    }
  }
  for (const id of Object.keys(settings)) {
    if (platform[id] === undefined) {
      problems.push(`${library.name}: "tellma".capsInKb names '${id}', which has no cap to set.`);
    }
  }
  // --- Each platform cap, with the library's level and its note ---
  const caps = [];
  for (const [id, platformKb] of Object.entries(platform)) {
    if (!library.entryPoints.some((entryPoint) => entryPoint.id === id)) {
      problems.push(`${library.name}: the platform caps '${id}', which is not an entry point.`);
      continue;
    }
    const setting = settings[id];
    if (setting === undefined) {
      caps.push({ id, kb: platformKb, platformKb });
      continue;
    }
    const error = typeof setting === 'object' && setting !== null ? setting.error : undefined;
    if (typeof error !== 'number' || !Number.isFinite(error) || error <= 0) {
      problems.push(`${library.name}: "tellma".capsInKb['${id}'].error must be a positive number of KB.`);
      continue;
    }
    const note = setting.note;
    if (error > platformKb && (typeof note !== 'string' || note.trim() === '')) {
      problems.push(
        `${library.name}: the cap on '${id}' is raised from ${platformKb} to ${error} KB without a "note" saying why.`,
      );
    }
    caps.push({ id, kb: error, platformKb });
  }
  return { caps, problems };
}

/**
 * Judges one measured entry point against its cap.
 *
 * @param {string} importPath the entry point's import path, such as '@tellma/core'
 * @param {TmEntryPointCap} cap its cap
 * @param {number} bytes its weight at gzip level 9, in bytes
 * @returns {string | null} the failure line when the weight is over the cap,
 *   else null
 */
export function capFailure(importPath, cap, bytes) {
  const kb = bytes / 1024;
  return kb > cap.kb
    ? `${importPath}: ${kb.toFixed(2)} KB at gzip level ${CAP_GZIP_LEVEL} is over its cap of ${cap.kb} KB.`
    : null;
}

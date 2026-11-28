// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// Which declaration function made a value. A feature's `overrides` list mixes
// entity, page, coded-report and widget declarations with `tmOverride` values
// (spec 0024 §2.2), and their shapes may overlap, so the merge cannot tell
// them apart by their members. Each declaration function records the value it
// returns here instead. The record lives beside the value, never on it, so a
// declaration stays exactly the plain object its spec shows.
//
// The record is one map per JavaScript realm, kept on `globalThis` under a
// registered symbol, not a module-local one. A build step may evaluate this
// package twice (once from its sources through a path mapping and once from
// the built package the config check imports); every copy must still see
// the kinds the others recorded.

/** The kind of value a declaration function returns. */
export type TmDeclarationKind = 'entity' | 'page' | 'report' | 'widget' | 'override';

/** The registered symbol under which the realm's one record is kept. */
const REGISTRY = Symbol.for('tellma.core.declarationKinds');

/**
 * The kind of every value a declaration function returned, held weakly: the
 * realm's one record, created by whichever copy of this module asks first.
 *
 * @returns The record.
 */
function kinds(): WeakMap<object, TmDeclarationKind> {
  const realm = globalThis as unknown as Record<symbol, WeakMap<object, TmDeclarationKind> | undefined>;
  return (realm[REGISTRY] ??= new WeakMap());
}

/**
 * Records that a declaration function made `value`, and returns it.
 *
 * @param value - The declaration, already frozen.
 * @param kind - The kind of declaration it is.
 * @returns `value` itself.
 */
export function tmMarkDeclaration<T extends object>(value: T, kind: TmDeclarationKind): T {
  kinds().set(value, kind);
  return value;
}

/**
 * The kind of a value a declaration function made, or `undefined` for any
 * other value, such as a spread copy of a declaration.
 *
 * @param value - The value to classify.
 * @returns Its kind, when a declaration function made it.
 */
export function tmDeclarationKindOf(value: unknown): TmDeclarationKind | undefined {
  return typeof value === 'object' && value !== null ? kinds().get(value) : undefined;
}

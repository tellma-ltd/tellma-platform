// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmJsonObject } from '../json/tm-json';

// The typed entity registries (spec 0029 §15). Both start empty: a
// distribution's emitted `generated/tellma-entities.d.ts` augments them with
// one member per entity, so rows and paths type-check by name alone. A pack
// compiled without that file still compiles, with untyped rows.

/**
 * The wire interface of every entity of the distribution, by entity name.
 * The distribution's emitted declarations add the members.
 *
 * @example
 * ```ts
 * type Customer = TmEntityTypes['acme.Customer']; // AcmeCustomer, once emitted
 * ```
 */
// eslint-disable-next-line @typescript-eslint/no-empty-object-type -- an augmentation target: the emitted declarations add its members
export interface TmEntityTypes {}

/**
 * The path shape of every entity of the distribution, by entity name: its
 * scalar properties, multilingual groups, navigations and child collections,
 * each by PascalCase name. The distribution's emitted declarations add the
 * members.
 *
 * @example
 * ```ts
 * type Scalars = TmEntityShapes['gl.Center']['scalars']; // 'Id' | 'Code' | …, once emitted
 * ```
 */
// eslint-disable-next-line @typescript-eslint/no-empty-object-type -- an augmentation target: the emitted declarations add its members
export interface TmEntityShapes {}

/**
 * The wire row of an entity: its emitted interface when the distribution's
 * declarations register it, else an untyped JSON object.
 *
 * @example
 * ```ts
 * function total(row: TmEntityType<'acme.Customer'>) { return row.creditLimit; }
 * ```
 */
export type TmEntityType<N extends string> = N extends keyof TmEntityTypes ? TmEntityTypes[N] : TmJsonObject;

// -----------------------------------------------------------------------------
// Typed paths
// -----------------------------------------------------------------------------

/** The path shape of an entity the emitted declarations register, else `never`. */
type TmShapeOf<N extends string> = N extends keyof TmEntityShapes ? TmEntityShapes[N] : never;

// The paths are never enumerated past one segment: a union of every path
// grows with the cube of an entity's navigations, and even two segments grow
// with their square, which a connected model cannot afford (TS2590 and
// minutes of checking). An annotation checks the first segment exactly; a
// declaration passed to `tmEntity` checks each literal segment by segment, at
// a cost linear in its length.

/**
 * What one segment may name on an entity: a scalar, a multilingual group, a
 * navigation, or a navigation, a dot and any text. Any text for an entity
 * the emitted declarations do not register.
 */
type TmPathStep<N extends string> = [TmShapeOf<N>] extends [never]
  ? string
  : TmShapeOf<N> extends {
        readonly scalars: infer S extends string;
        readonly groups: infer G extends string;
        readonly navigations: infer V;
      }
    ? S | G | (keyof V & string) | `${keyof V & string}.${string}`
    : string;

/**
 * A path from an entity, in PascalCase C# names: `Code`, `Parent.Name`,
 * `Parent.Manager`. Once the distribution's emitted declarations register
 * the entity, an annotation checks a path's first segment exactly, a
 * scalar, a group or a navigation, and accepts any text after a
 * navigation's dot; editors complete that first segment. `tmEntity` checks
 * every segment of the literal paths its declaration names. Without the
 * declarations, any text is a path.
 *
 * @example
 * ```ts
 * const path: TmPath<'gl.Center'> = 'Parent.Manager.Name'; // checks Parent only
 * ```
 */
export type TmPath<N extends string> = TmPathStep<N>;

/**
 * The rest of a literal path checked from an entity, one segment at a time:
 * the whole path when every segment names what its entity has, else the
 * prefix that checked followed by what may come next, which the compiler
 * reports against the literal.
 */
type TmCheckFrom<N extends string, Rest extends string, Full extends string, Prefix extends string> = string extends Rest
  ? Full
  : [TmShapeOf<N>] extends [never]
    ? Full
    : TmShapeOf<N> extends {
          readonly scalars: infer S extends string;
          readonly groups: infer G extends string;
          readonly navigations: infer V;
        }
      ? Rest extends `${infer H}.${infer T}`
        ? H extends keyof V
          ? V[H] extends string
            ? TmCheckFrom<V[H], T, Full, `${Prefix}${H}.`>
            : Full
          : `${Prefix}${TmPathStep<N>}`
        : Rest extends S | G | (keyof V & string)
          ? Full
          : `${Prefix}${TmPathStep<N>}`
      : Full;

/**
 * A path checked segment by segment from an entity: the literal itself when
 * it resolves, else what may follow its last valid segment. Text that is no
 * literal passes as it is.
 *
 * @example
 * ```ts
 * type Ok = TmCheckedPath<'gl.Center', 'Parent.Manager.Name'>; // 'Parent.Manager.Name'
 * ```
 */
export type TmCheckedPath<N extends string, P extends string> = P extends string ? (string extends P ? P : TmCheckFrom<N, P, P, ''>) : never;

/**
 * An order checked from an entity: its path segment by segment, and the
 * `:desc` suffix apart from it.
 *
 * @example
 * ```ts
 * type Ok = TmCheckedOrder<'gl.Center', 'Parent.Code:desc'>; // 'Parent.Code:desc'
 * ```
 */
export type TmCheckedOrder<N extends string, O extends string> = O extends `${infer P}:desc` ? `${TmCheckedPath<N, P>}:desc` : TmCheckedPath<N, O>;

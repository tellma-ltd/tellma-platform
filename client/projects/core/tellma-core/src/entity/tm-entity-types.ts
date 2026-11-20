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

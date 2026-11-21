// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The plain-JSON value types of the HTTP layer (spec 0024 §10.1, §10.4).
// Bodies parse with `JSON.parse` and serialize with `JSON.stringify`, with no
// reviver and no replacer: the server sends every value a JavaScript number
// cannot hold exactly as a JSON string, so these types describe exactly what
// the plain parser produces.

/**
 * Any value that plain `JSON.parse` can produce: `null`, a boolean, a number,
 * a string, an array of values or an object of values.
 *
 * @example
 * ```ts
 * const body: TmJson = JSON.parse('{"id": 5, "total": "1234.5000"}');
 * ```
 */
export type TmJson = null | boolean | number | string | readonly TmJson[] | TmJsonObject;

/**
 * A JSON object: string keys, each holding a {@link TmJson} value.
 */
export type TmJsonObject = { readonly [key: string]: TmJson };

/**
 * The brand that sets a {@link TmDecimal} apart from a plain string. It is
 * declared and never exported, so only the parsing helpers of
 * {@link TmDecimals} (or an explicit cast) produce a decimal.
 */
declare const tmDecimalBrand: unique symbol;

/**
 * A `decimal` or `long` value as its exact wire text: an optional minus sign,
 * digits and an optional point followed by digits, never an exponent, as in
 * `'123456789012.3456'` or `'9007199254740993'`. The server writes a decimal
 * at its column's full scale (`'1234.5000'`), and the text keeps that scale.
 *
 * A JavaScript number holds only about 15 significant digits exactly, so
 * these values stay strings from the wire to the screen. `+` on a string
 * would join text and `<` would compare it as text, so arithmetic and
 * comparison go through {@link TmDecimals}, and the platform's lint rules
 * refuse the operators on a `TmDecimal`.
 */
export type TmDecimal = string & { readonly [tmDecimalBrand]: true };

/**
 * Any entity's id: an `int` key is a `number`, and a `bigint` key travels as
 * a {@link TmDecimal}, because its digits may exceed what a number holds.
 */
export type TmId = number | TmDecimal;

/**
 * A JSON Schema (draft 2020-12) object schema, as plain JSON: the metadata
 * describes an action's arguments with one.
 */
export type TmJsonSchema = TmJsonObject;

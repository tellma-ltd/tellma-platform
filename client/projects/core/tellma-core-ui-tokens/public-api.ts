/**
 * Public API Surface of @tellma/core-ui-tokens.
 *
 * The typed TmTokens contract, the brand default preset, the tokens→CSS
 * emitter, and the build-time missing-ref validation gate. All
 * dependency-free; the zod mirror + JSON Schema generation live in the
 * workspace tooling (client/tools/tokens), keeping the shipped runtime
 * tiny. Color-contrast accessibility is exercised by the axe browser
 * battery, not by token validation.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export * from './contract/tokens';
export * from './presets/tellma-default';
export * from './emit/emit-css';
export * from './schema/validate';

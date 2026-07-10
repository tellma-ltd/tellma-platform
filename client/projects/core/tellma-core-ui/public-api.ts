/**
 * Public API Surface of @tellma/core-ui (primary entry point).
 *
 * Carries only the cross-cutting, component-free surface: providers, i18n,
 * fonts, and forms infrastructure. Components are exported from their own
 * secondary entry points (@tellma/core-ui/input, /checkbox, /form-field,
 * /select); the contract types from @tellma/core-ui/contracts.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export * from './i18n/strings-en';
export * from './i18n/tm-ui-translate';
export * from './forms/field-errors';
export * from './forms/provide-tellma-forms';
export * from './providers/provide-tellma-ui';
export * from './spinner/tm-spinner';

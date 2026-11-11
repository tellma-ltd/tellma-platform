/**
 * Public API Surface of @tellma/core-ui (primary entry point).
 *
 * Carries only the cross-cutting, component-free surface: providers, i18n,
 * fonts, forms infrastructure, the formatting facade with its calendar and time-zone seams, breakpoints and the theme. Components are exported from their own
 * secondary entry points (@tellma/core-ui/input, /checkbox, /form-field,
 * /select, /spinner); the contract types from @tellma/core-ui/contracts.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export * from './cache/tm-client-cache';
export * from './i18n/strings-en';
export * from './i18n/tm-active-locale';
export * from './i18n/tm-ui-argument-formatter';
export type { TmUiTranslationTable, TmUiTranslationsLoader } from './i18n/tm-translations-loader';
export * from './i18n/tm-ui-translate';
export * from './forms/field-errors';
export * from './forms/provide-tellma-forms';
export * from './forms/tm-cell-editor-host';
export * from './forms/tm-control-size';
export * from './forms/tm-date-validators';
export * from './l10n-facade/tm-l10n';
export * from './layout/tm-breakpoints';
export * from './providers/provide-tellma-ui';
export * from './providers/tm-calendar';
export * from './providers/tm-time-zone';
export { TmTheme, type TmThemeOptions, type TmThemeSetting } from './theme/tm-theme';

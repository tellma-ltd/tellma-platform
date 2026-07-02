/*
 * Public API Surface of @tellma/core-ui (primary entry point).
 *
 * Carries only the cross-cutting, component-free surface: providers, i18n,
 * fonts, and forms infrastructure. Components are exported from their own
 * secondary entry points (@tellma/core-ui/input, /checkbox, /form-field,
 * /select); the contract types from @tellma/core-ui/contracts.
 */

export * from './lib/i18n/strings-en';
export * from './lib/i18n/tm-ui-translate';
export * from './lib/forms/field-errors';
export * from './lib/forms/provide-tellma-forms';
export * from './lib/providers/provide-tellma-ui';

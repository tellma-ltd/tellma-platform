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

export * from './i18n/strings-en';
export * from './i18n/tm-ui-translate';
export * from './fonts/font-subsets';
export * from './fonts/font-manifest.generated';
export * from './forms/field-errors';
export * from './forms/provide-tellma-forms';
export * from './providers/provide-tellma-ui';

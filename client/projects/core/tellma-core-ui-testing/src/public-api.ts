/*
 * Public API Surface of @tellma/core-ui-testing.
 *
 * Component harnesses (the typed automation surface, spec §10) plus the
 * shared form() fixture. Harnesses drive the TestBed layer; Playwright
 * specs use raw locators against the stories.
 */

export * from './lib/input-harness';
export * from './lib/checkbox-harness';
export * from './lib/select-harness';
export * from './lib/form-field-harness';
export * from './lib/form-fixture';

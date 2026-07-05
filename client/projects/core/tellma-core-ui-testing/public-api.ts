/*
 * Public API Surface of @tellma/core-ui-testing.
 *
 * Component harnesses (the typed automation surface, spec §10) plus the
 * shared form() fixture. Harnesses drive the TestBed layer; Playwright
 * specs use raw locators against the stories.
 */

export * from './input-harness';
export * from './checkbox-harness';
export * from './select-harness';
export * from './form-field-harness';
export * from './form-fixture';

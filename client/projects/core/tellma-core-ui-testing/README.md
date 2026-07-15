# @tellma/core-ui-testing

CDK `ComponentHarness` drivers for the `tm-*` controls, shipped for
consumers: `TmInputHarness`, `TmCheckboxHarness`, `TmSelectHarness` +
`TmOptionHarness`, and `TmFormFieldHarness`.

Harnesses are the typed, implementation-independent way to drive the
components from TestBed-based tests — they survive internal DOM changes that
would break raw selectors. Browser-level suites (Playwright) use locators
against rendered pages instead.

```ts
const select = await loader.getHarness(TmSelectHarness);
await select.selectOption('Active');
expect(await select.getTriggerText()).toBe('Active');
```

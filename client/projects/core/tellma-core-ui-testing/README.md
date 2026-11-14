# @tellma/core-ui-testing

CDK `ComponentHarness` drivers for the `tm-*` controls, shipped for
consumers: `TmInputHarness`, `TmCheckboxHarness`, `TmSelectHarness` and
`TmMultiSelectHarness` (with `TmOptionHarness` and `TmOptionGroupHarness`),
and `TmFormFieldHarness`, among others. The status and identity indicators
have `TmCounterHarness`, `TmAvatarHarness`, `TmProgressBarHarness`,
`TmRelativeTimeHarness`, `TmCopyButtonHarness`, `TmReferenceHarness` and
`TmEmptyStateHarness`; `tm-badge`, `tm-progress-ring` and `tm-skeleton` carry
no behaviour a harness would add to a locator. Chips have `TmChipHarness` and
`TmChipSetHarness`; the foreign-key selectors have `TmEntityPickerHarness`
and `TmEntityMultiPickerHarness` (chips, the count badge, search and pick);
and the feedback surfaces `TmBannerBarHarness`, plus
`TmToastHarness` (one toast), `TmToastStackHarness` (the overflow pill and the
live regions) and `TmBusyOverlayHarness`, which render on `<body>` and are
located with a document-root loader. The dialogs `TmConfirmHarness` and
`TmErrorDialogHarness` render in the overlay container and are located the
same way. The layout pieces have `TmCardHarness` (the disclosure, the error
dot and its name) and `TmSplitterHarness` (the bar's value, range and keys);
`tm-page-header` and `dl[tmDescriptionList]` impose no behaviour of their
own.

Harnesses are the typed, implementation-independent way to drive the
components from TestBed-based tests — they survive internal DOM changes that
would break raw selectors. Browser-level suites (Playwright) use locators
against rendered pages instead.

```ts
const select = await loader.getHarness(TmSelectHarness);
await select.selectOption('Active');
expect(await select.getTriggerText()).toBe('Active');
```

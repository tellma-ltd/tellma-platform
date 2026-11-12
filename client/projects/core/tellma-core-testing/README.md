# @tellma/core-testing

Test providers and fakes for apps built on `@tellma/core`, and the Playwright helpers their
end-to-end suites share.

| Entry point | Holds | Runs in |
|---|---|---|
| `@tellma/core-testing` | `provideTellmaTesting()`, which replaces each platform seam with a fake a test can inspect and provides the test client metadata; `TmFakeApi`, which answers each route from a script and records every call; `TmFakeTelemetry`, which records every telemetry item and sends nothing; `tmTestMetadata()` and `tmTestEntitiesChunk()`, the Core and Entities chunks of a small Acme distribution, each patchable member by member | the unit-test browser |
| `@tellma/core-testing/e2e` | `expectNoAxeViolations` and `tmAxeViolations`: one axe-core scan of the WCAG 2.1 A and AA rules over a page or part of it | Node, inside Playwright tests; needs the optional peer `@playwright/test` |

```ts
TestBed.configureTestingModule({ providers: [provideTellmaTesting()] });
TestBed.inject(TmFakeApi).respond('acme/credit-exposure', { rows: [] });
// … run the code under test …
expect(TestBed.inject(TmFakeTelemetry).recent().map((item) => item.name)).toContain('acme.report.run');
```

```ts
import { expectNoAxeViolations } from '@tellma/core-testing/e2e';

test('the page is accessible', async ({ page }) => {
  await page.goto('/17/customers');
  await expectNoAxeViolations(page);
});
```

The package builds against the built `@tellma/core` (`tsconfig.lib.json`), as a consumer would;
`tsconfig.json` is the editor, lint and typecheck view over the sources.

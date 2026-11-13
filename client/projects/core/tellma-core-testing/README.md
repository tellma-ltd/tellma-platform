# @tellma/core-testing

Test providers and fakes for apps built on `@tellma/core`, and the Playwright helpers their
end-to-end suites share.

| Entry point | Holds | Runs in |
|---|---|---|
| `@tellma/core-testing` | `provideTellmaTesting()`, which replaces each platform seam with a fake a test can inspect and provides the test client metadata; `TmFakeApi`, which answers each route from a script and records every call, image fetches scripted by URL included; `TmFakeTelemetry`, which records every telemetry item and sends nothing; `tmTestMetadata()` and `tmTestEntitiesChunk()`, the Core and Entities chunks of a small Acme distribution, each patchable member by member; `provideTellmaStateTesting()`, which adds the client state's fakes: `TmFakeTabBus`, which records posts and delivers the messages a test plays as other tabs', and `TmFakeSession`, signed in as `tmTestProfile()`'s user and active until the test changes it | the unit-test browser |
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

Both providers run `TmClientStore` in memory: no IndexedDB, and a boot record kept in a map, so
each test starts from an empty store and leaves nothing behind. Opening the store therefore
reports `tm.store.unavailable` once, as a browser without IndexedDB would. Beside
`provideTellmaTesting()`, the state providers let the store's write guard pass:

```ts
TestBed.configureTestingModule({ providers: [provideTellmaTesting(), provideTellmaStateTesting()] });
const store = TestBed.inject(TmClientStore);
const user: TmPartition = { kind: 'user', sub: tmTestProfile().sub };
await store.put(user, 'tenants', { tag: null, savedAt: Date.now(), data: [] });
TestBed.inject(TmFakeSession).setState('expired');
await expect(store.put(user, 'tenants', { tag: null, savedAt: Date.now(), data: [] })).rejects.toThrow(TmStoreRefusedError);
```

The package builds against the built `@tellma/core` (`tsconfig.lib.json`), as a consumer would;
`tsconfig.json` is the editor, lint and typecheck view over the sources.

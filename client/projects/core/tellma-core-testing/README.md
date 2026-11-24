# @tellma/core-testing

Test providers and fakes for apps built on `@tellma/core`, and the Playwright helpers their
end-to-end suites share.

| Entry point | Holds | Runs in |
|---|---|---|
| `@tellma/core-testing` | `provideTellmaTesting()`, which replaces each platform seam with a fake a test can inspect and loads the test client metadata into its holder, as the Core chunk's load would; `TmFakeApi`, which answers each route from a script and records every call, image fetches scripted by URL included; `TmFakeTelemetry`, which records every telemetry item and sends nothing; `TmPageContextHarness`, which reads the page-context registry as snapshots (the routed page's, the dialogs' and the top one), lists the top context's actions and invokes them through the dispatcher; `TmBannerHostHarness`, which reads the banner row (open, open and empty, the shown banner through `@tellma/core-ui-testing`'s `TmBannerBarHarness`) and its live region's announcement and politeness; `tmTestMetadata()` and `tmTestEntitiesChunk()`, the Core and Entities chunks of a small Acme distribution, each patchable member by member; `provideTellmaStateTesting()`, which adds the client state's fakes: `TmFakeTabBus`, which records posts and delivers the messages a test plays as other tabs', `TmFakeSession`, signed in as `tmTestProfile()`'s user and active until the test changes it, `TmFakeLocks`, the browser's exclusive locks kept in memory, which shows the locks held, and a session storage of the test's own behind `@tellma/core/memory`'s `TmDrafts`, so no draft outlives the test; `tmWarmTenant()`, which stores a tenant's launch documents with a recent sync, so its entry is warm, and the inbox and assignments summaries when the test gives them (the inbox when it also registers `TM_ATTENTION_DOCUMENTS`, as `provideTellma` does); `tmFakeInboxSummary()` and `tmFakeAssignmentsSummary()`, an empty inbox and an empty tray with the members a test names; `TmEntityFieldHarness` and `TmEntityMultiFieldHarness`, the harnesses of `@tellma/core/entity`'s entity fields, which drive the picker inside and hand it out (they compose `@tellma/core-ui-testing`'s picker harnesses); `TmColumnFilterHarness`, the harness of `@tellma/core/column-filter`'s `tm-column-filter`, which hands out each kind's controls and drives the operator, Apply, Clear and Enter | the unit-test browser |
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
each test starts from an empty store and leaves nothing behind. The store runs in memory by
design there, so it reports no `tm.store.unavailable`. Beside
`provideTellmaTesting()`, the state providers let the store's write guard pass:

```ts
TestBed.configureTestingModule({ providers: [provideTellmaTesting(), provideTellmaStateTesting()] });
const store = TestBed.inject(TmClientStore);
const user: TmPartition = { kind: 'user', sub: tmTestProfile().sub };
await store.put(user, 'tenants', { tag: null, savedAt: Date.now(), data: [] });
TestBed.inject(TmFakeSession).setState('expired');
await expect(store.put(user, 'tenants', { tag: null, savedAt: Date.now(), data: [] })).rejects.toThrow(TmStoreRefusedError);
```

A tenant's documents read as absent unless the tenant synced recently, so stored launch documents
alone make a cold entry. `tmWarmTenant()` stores them with a recent `sync` stamp, as after a launch
that synced a moment ago:

```ts
const tenant: TmPartition = { kind: 'tenant', sub: tmTestProfile().sub, tenantId: 17 };
await tmWarmTenant(TestBed.inject(TmClientStore), tenant, { settings, me, tenant: facts });
```

`fixtures/entity-model/` holds the server's own output for its Acme-like test model, recorded by
`test/core/Tellma.Core.Tests` (rerun it with `TELLMA_UPDATE_GOLDENS=1` after a change to the model):
`entities-chunk.json`, the Entities chunk, and `core-entities.json`, the Core chunk's summaries. The
workspace's type tests (`tools/tests/entity-model.spec.mts`) check that each satisfies its client
type, and compile the declarations the same model writes. `tmTestEntities()` returns both, a fresh
copy per call, for tests of the entity registry, display names and permissions over the shapes the
server sends:

```ts
const { summaries, chunk } = tmTestEntities();
const metadata = tmTestMetadata({ entities: summaries });
```

```ts
const field = await loader.getHarness(TmEntityFieldHarness);
await field.pick('1101 - Cash');
expect(await field.getText()).toBe('1101 - Cash');
```

The package builds against the built `@tellma/core` (`tsconfig.lib.json`), as a consumer would;
`tsconfig.json` is the editor, lint and typecheck view over the sources.

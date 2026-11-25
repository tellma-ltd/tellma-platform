# @tellma/core-testing

Test providers and fakes for apps built on `@tellma/core`, and the Playwright helpers their
end-to-end suites share.

| Entry point | Holds | Runs in |
|---|---|---|
| `@tellma/core-testing` | `provideTellmaTesting()`, which replaces each platform seam with a fake a test can inspect and loads the test client metadata into its holder, as the Core chunk's load would; `TmFakeApi`, which answers each route from a script and records every call, image fetches scripted by URL included; `TmFakeTelemetry`, which records every telemetry item and sends nothing; `TmPageContextHarness`, which reads the page-context registry as snapshots (the routed page's, the dialogs' and the top one), lists the top context's actions and invokes them through the dispatcher; `TmBannerHostHarness`, which reads the banner row (open, open and empty, the shown banner through `@tellma/core-ui-testing`'s `TmBannerBarHarness`) and its live region's announcement and politeness; `tmTestMetadata()` and `tmTestEntitiesChunk()`, the Core and Entities chunks of a small Acme distribution, each patchable member by member; `provideTellmaStateTesting()`, which adds the client state's fakes: `TmFakeTabBus`, which records posts and delivers the messages a test plays as other tabs', `TmFakeSession`, signed in as `tmTestProfile()`'s user and active until the test changes it, `TmFakeLocks`, the browser's exclusive locks kept in memory, which shows the locks held, `TmFakeHub`, which holds the leader's realtime connections when a test runs the leader election, each a `TmFakeHubConnection` that counts its starts, stops and nudges and reports what a real one would (connected, dropped, an event, a refusal, the server's end), and a session storage of the test's own behind `@tellma/core/drafts`' `TmDrafts`, so no draft outlives the test; `tmWarmTenant()`, which stores a tenant's launch documents with a recent sync, so its entry is warm, and the inbox and assignments summaries when the test gives them (the inbox when it also registers `TM_ATTENTION_DOCUMENTS`, as `provideTellma` does); `tmFakeInboxSummary()` and `tmFakeAssignmentsSummary()`, an empty inbox and an empty tray with the members a test names; `TmEntityFieldHarness` and `TmEntityMultiFieldHarness`, the harnesses of `@tellma/core/entity`'s entity fields, which drive the picker inside and hand it out (they compose `@tellma/core-ui-testing`'s picker harnesses); `TmColumnFilterHarness`, the harness of `@tellma/core/column-filter`'s `tm-column-filter`, which hands out each kind's controls and drives the operator, Apply, Clear and Enter; `TmFakeDiscover`, which scripts `TmFakeApi`'s `queryex/discover` route for `@tellma/core/expression-editor`'s draft: it records every request, answers from a script (by default, the parameters each clause names, with no problem), holds answers until released, and fails calls with any `TmApiError` kind; `TmExpressionEditorHarness`, the harness of `tm-expression-editor`, which types, reads the status line, the problems list and the parameter chips, and presses Retry, F8 and Mod+Enter; `TmPathPickerHarness`, the harness of `@tellma/core/path-picker`'s `tm-path-picker`, which searches, reads the rows' keys, labels, kinds and pills and the search's count, and hands out the tree (through `@tellma/core-ui-testing`'s `TmTreeHarness`); `TmDetailsPageHarness`, the harness of `@tellma/core/details`'s page (the routed host and the source page), which reads the crumb, the title or its skeleton, the badges, each toolbar button with its disabled reason, the progress bar, the band's busy and error states and the polite status, and drives the toolbar; `tmFakeDetails()`, a `TmDetailsContext` for a layout's unit test, with the real record state (form, dirty rule, refusals, registrations) over commands that act at once and are recorded, and a read state the test sets; `TmTenantPickerHarness` and `TmTenantPickerRowHarness`, the harnesses of `@tellma/core/shell`'s tenant picker at `/tenants`, which read its rows (names with their languages, badges, Connect's link or unavailable reason, nesting and dimming), its search, notice, skeleton, error and empty states, and drive the search, Enter, Connect and the notice's Dismiss; `TmTenantShellHarness`, the harness of `@tellma/core/shell`'s tenant shell, which reads what its main landmark shows (the page, a failed cold sync's error state with its actions, or what replaces the route host) and its app bar (the wordmark's link, the session button's name, names and avatar tone, the markers, the slots, the reserved boxes and the installed-mode Back and Forward), opens a marker's popover and hands out the session menu, waiting for their chunk; `TmSessionMenuHarness`, the session menu's harness, a `TmMenuHarness` that also reads the header's name, email and avatar tone and the tenant card; `tmTestShellSlot()`, an app-bar slot for a test feature, with a badge's count and a test panel; `TmDiagnosticsPanelHarness`, the harness of `@tellma/core/support`'s Diagnostics panel, which reads the header's labels and values, hands out the request grid (through `@tellma/core-ui-testing`'s `TmGridHarness`), turns the slow and failed switch and hands out the action buttons by label; `TmSupportOfflineHarness`, which reads the line the support dialog shows when the support tools cannot load, and the keys it names | the unit-test browser |
| `@tellma/core-testing/e2e` | `expectNoAxeViolations` and `tmAxeViolations`: one axe-core scan of the WCAG 2.1 A and AA rules over a page or part of it; `signInAs(page, email)`, a fresh session through the e2e host's email-code sign-in, reading the code from its mail drop and keeping each principal's identity sign-in so a run spends one code per principal at most, with `tmSignInToIdentityAs` (the identity sign-in alone, so the app's own round trip completes it) and `tmExpireSession` (the app's session cookies gone, as an expiry); `warmUp(page, url)`, which waits until the service worker holds its whole shell group and the store the entered tenant's launch documents; `openTabs`; `holdNetwork`, `goOffline` and `fakeProblem(page, route, problem)`, which act on the whole context so requests the worker sends are reached too; `tmCspWatch`, which collects every content-security-policy violation of a context's pages; and the readers `tmBootFacts`, `tmBootRecord` and `tmStoredDocs`. The suite's configuration publishes the host's facts in `TM_E2E_ENV`'s variables, which `tmE2eHost()` reads | Node, inside Playwright tests; needs the optional peer `@playwright/test` |

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

# Platform-core watch-list

Unverified leads from the planning analysis. Implementers check every lead in their spec's section
against the spec and the code. A confirmed lead is resolved under Rule 2 (PROCESS.md §4.1) and
noted as a deviation; a lead that turns out wrong is ignored. Reviewers use this list as a
checklist of known traps.

# Cross-spec ordering notes (per analysis cluster)

### Cluster c01: 0010-A, 0010-B, 0010-C, 0010-D, 0010-E, 0010-F, 0010-G

I split spec 0010 into seven independent parts (A–G), each with its own Testing and DoD, and gave each part its own entry. The cross-cutting facts:

1. BACKEND ORDER. Parts A, F and G have no prerequisites and can start immediately, in parallel; they touch different projects.
   - Spec 0011 (the distribution host) needs 0010-a1: in-proc identity mode needs the Distribution seed, the store check and TellmaIdentityStore.MigrateAsync, plus DatabaseCollations in Tellma.Core.Abstractions/Hosting. It also needs 0010-g1. a2, a3 and a5 complete 0011 §5.5 and §5.9.
   - Spec 0018 needs a2 (v1 routes, existingOnly).
   - Spec 0012 needs g1, f1, f2, f3, plus `CalendarMap` from f5 (0012 §11.1 calls it unconditionally). Pull CalendarMap forward, or land f5 before 0012's schema checkpoint.
   - f6 (measures and the evaluator) and most of f4 are needed only by 0033 and 0034. Despite §F.0's 'Order' text, they can slip.
   - DatabaseCollations is created by 0010-a1. Part G's and Part F's integration fixtures must also create databases with COLLATE Latin1_General_100_CI_AS. Whichever lands first adds the constant, or hard-codes the string; do not let two checkpoints add it.
   - Every SQL fixture in the repo today runs a bare CREATE DATABASE: identity integration and E2E, EF integration, Queryex integration. The new identity store check refuses such databases, so a1 must convert the identity fixtures in the same change, or the whole identity suite goes red.

2. CLIENT ORDER. These steps are a strict chain, required for build and import correctness:
   - (i) 0023 §3.1 icon and §3.2 kbd. These are 0023 checkpoints, a new `lucide` npm dependency, and not part of 0010.
   - (ii) 0010-b1.
   - (iii) 0010-d1, the public @tellma/core-ui/overlay. Every entry point, grid and menu included, re-imports from it.
   - (iv) 0010-b2, d2 and d3, in any order. d2 needs only b1; b2 and d3 need icon.
   - (v) Part C: c1 menu → c2 keys and glyphs → c3 → {c4, c5, c6}. Part C needs §D.1 for the submenu Escape stack and the §C.15 pane, which the spec's order rule does not say. Doing Part D before Part C also avoids grid conflicts with §D.14's number-cell change.
   - (vi) The rest of 0023 builds on §B.1 and §D.1. Its §4.1 badge and §4.3 chip are needed before 0010-e2.
   - (vii) 0010-e1 after c3 (grid column inputs); e2 after e1 and the 0023 chip.
   The grid entry point is touched by b2 (the pixel test only), d2 (the number-cell span), c1–c6 and e1. Never run two of those in parallel. The ./grid ceiling of 43 KB (§C.11) does not budget for §D.14 or §E.4, so expect one budgetNotes raise.

3. SHARED SEAMS AND FILES.
   - Every client checkpoint that adds tokens (b1, c3, c5, d1, e1) must run `pnpm run identity:copy-assets`, which rewrites the identity server's token CSS under src/apps/Tellma.Identity. That is a client checkpoint touching a backend file, so it conflicts with concurrent Part A work.
   - The client library's API goldens (client/api/*.api.md), the components.json docs, harnesses in @tellma/core-ui-testing, the Arabic twins in @tellma/locale-ar (parity gate) and budgetsInKb with budgetNotes must ship in the same checkpoint as each entry-point change, per the Parts B–E shared rules.
   - The §B.3 live Directionality means showcase e2e specs that flip direction via the [dir] wrapper must flip <html dir> instead. This is hidden churn in existing specs.

4. THINGS AN AUTONOMOUS AGENT CANNOT INVENT. Ask Ahmad, or defer:
   - §A.4's interim-client redirect URIs: the vendor callbacks for claude-web and codex-web, and the loopback paths for claude-code, codex and cursor.
   - Where to record the 10M-row 'uq' benchmark result (§F DoD, flag 4).
   - Where the shared collation-pin test helper lives (Part A says no new project; 0011 reuses it).
   - The tzdb release for zone.tab (§F.22).
   - The path and format of the ICU Ethiopic vector file shared by §F.19 and spec 0013 §12.
   - The bidi-control character set (§D.14 defers it to spec 0024 §7.9).

5. ALREADY DONE. ARCHITECTURE.md already carries Part A's identity paragraph (line ~896), the collation paragraph (~685), Part G's UseCollation (~659), and the Queryex 'uq', Exists and evaluator text (~752). The 'Docs' DoD items need verification only. §A.12 has no identity checkpoint; spec 0021 implements and tests it. §F.5 needs no code.

6. SLOW TESTS.
   - The only mandated long run is the one-off 10M-row 'uq' benchmark (15–45 min). Keep it out of CI; it must run before 0010-f5's §F.11 code.
   - The every-day calendar differentials (§F.11, §F.17, §F.19; ~65k–73k days per function) are fast only if written set-based. Make that a checkpoint requirement.
   - The §F.15 500-measure differential takes roughly 1–4 min per PR (the spec says none is nightly).
   - The client e2e battery grows by roughly 15–30 min in total across Parts B–E (4 widths × LTR/RTL, @cross-engine on Firefox and WebKit, axe light/dark). Several frame-timing assertions are flake risks: the drawer and active-row 'in the click's frame', the chip 'same frame' and the §C.13 50 ms long-task scroll probe. Run them on Chromium only, with rAF-based checks.

7. LOC. Totals are roughly A 19.5k, B 8.5k, C 23k, D 14.5k, E 6.5k, F 31.5k, G 2k, about 106k including tests and goldens. Part F is the densest: each amendment carries corpus, golden, interpreter and differential work. Part A includes a regenerated Initial migration (~1.7k generated lines replacing ~1.7k).

### Cluster c02: 0011, 0012

Build order across the two specs (they are mutually dependent):
1. 0010 §G.1 (UseCollation) and, for in-proc identity, 0010 §A.1 and §A.11.
2. 0011-a, which also bumps the CPM pins. Land the bump first and alone if possible: EF 10.0.11, SqlClient 6.1.6 and Testcontainers 4.14.0 rebuild the frozen identity, table-type and Queryex suites.
3. 0012-a, then 0012-b.
4. 0011-b, then 0011-c.
5. 0012-c.
6. 0010 Part F (§F.1–§F.4, §F.6–§F.9, §F.11, §F.14, §F.16, §F.18).
7. 0012-d, then 0012-e, then 0012-f. 0012-g can start any time after 0012-c but should land before 0015.
8. 0011-d, which needs 0012-b's StorageFingerprint and 0012-c's Maintenance batches.
9. 0011-e, then 0011-f, then 0011-g.
10. 0011-h, after 0014/0016/0018/0020. The hub front door (0011 §4.3) lands with 0021; the client-telemetry relay and admin support routes land with 0026.

Shared seams that must land together or be pulled forward as contract-only types:
(a) 0015 §14.1 TellmaException and IndividualRequiredException, needed by 0011-b/c.
(b) 0013 §2 tag contracts, MultilingualShape, TenantSettings and ICalendarSystem, needed by 0011-c's RequestContext and 0012-c's IDataBatch and persist frame.
(c) 0014 §4.5 SecurableEndpointMetadata and MemberEndpointMetadata, needed by 0011-e's audit.
(d) 0015 §2.1 StackOperations, needed because FeatureContribution.Entity<T>() feeds the TellmaDbContext model.
(e) 0017 [BlobReference], BlobPreset, BlobKindPolicy and the core.Blobs shape, needed by 0012-a and the fixture.
(f) 0024 §3.2 TellmaRoutes and IClientAssetContributor, needed by 0011-a/d.
(g) 0016 ApiOptions.ClientNames, needed by 0011-e's CSRF rule.
Give each pulled type one owner checkpoint, so the later spec's checkpoint extends rather than re-declares it.

0011 §2.1's full FeatureContributionItem catalog must NOT ship in 0011-a. An item without a realizer is a composition problem, so each owner spec adds its item record, sugar and realizer.

Ordering traps:
- The grant trap. Spec 0001 emits an unguarded GRANT EXECUTE ON TYPE to tellma_app. Every fixture or design-time database (catalog fixture, Tellma.Testing.Entities fixture, dotnet ef database update) must create the tellma_app role before migrating. Decide the mechanism once, in 0011-b/0012-a.
- UseTableTypes requires a sweepScope that neither spec names. Pick constants for the catalog, tenant and fixture contexts in 0012-a/0011-b.
- Stand-ins. 0012's fixture needs core.Users, core.UserStamps, core.VersionTags, core.Jobs and core.Blobs. Use stand-ins in Tellma.Testing.Entities until 0013/0014/0017 contribute the real tables, then swap them. Plan a small swap task inside those specs' checkpoints.
- Client esproj. 0011's DoD lists Tellma.Distro.Acme.Client, but its build needs spec 0024's tellma-client and a repo-root pnpm workspace (currently client/pnpm-workspace.yaml), and CI's build job has no Node. Move the esproj, the SPA proxy wiring and the pnpm workspace relocation into 0024's first checkpoint.
- Shared vector files. launch-landing-vectors.json (0011-f) and display-name-golden.json (0012-d) live in @tellma/core, which 0024 creates. Whichever checkpoint lands first creates the file at its final path (client/projects/core/tellma-core/src/...), and 0024/0029 adopt it.
- Test helpers. Move OidcFlowClient from Tellma.Identity.IntegrationTests into a shared test-support project before 0011-g. Consolidate the three duplicate SqlServerFixture classes into Tellma.Testing.Support when Tellma.Testing.Entities is created. (Closed by PC-027: the four copies are one fixture in Tellma.Testing.Support/Sql. Tellma.Identity.E2E's IdentityServerFixtureBase still starts its own container.)
- Analyzer reach. Once Abstractions carries the analyzer asset (0012-g), every project referencing Abstractions runs TELLMA0001–0006, including Tellma.Identity and the connectors. CI design-e2e's package flow must pack Tellma.Core.Queryex and the analyzer with its ScriptDom DLL, and 0012-a's Abstractions→Queryex edge already breaks that flow. Fix CI in 0012-a.
- CI capacity. Both specs require Category=Integration on Windows LocalDB per PR, but ci.yml has only an ubuntu integration job with a 25-minute timeout. Add a Windows job, and budget time for the combined 0011/0012 suites (roughly 6–12 min added).
- Problem codes. 0011's web tests that assert problem codes need an interim mapping, because 0016 owns problem details. Keep it minimal and let 0016 replace it.

MVP summary: every checkpoint above is core except 0011-h and 0012-g (important). Inside 0011-e, the admin surface v1 and the PRM document are deferrable content.

Calibration: 0011 ≈ 41k LOC and 0012 ≈ 42k LOC including tests and golden SQL. Each is comparable to identity (38k) and Queryex (34k); the size reflects the heavy integration tiers, not spec prose.

### Cluster c03: 0013, 0014

1) Contract-first ordering trap: 0011 §4.1's RequestContext names 0013's ICalendarSystem and TenantSettings, and 0012 §5.1's IDataBatch names 0013's VersionTagDependency, CacheResult<T> and CachedEntitySet<T>. Checkpoint 0013-a (the Abstractions contracts plus the calendars, which need nothing but BCL and Queryex) must therefore land before 0011's request-context checkpoint and 0012's IDataBatch checkpoint. The alternative is for those checkpoints to create the types under 0013's exact names. 0010 Part F §F.1/F.6/F.7/F.18/F.22 must land before 0012/0013/0014 (0010 §F.0 says so explicitly).

2) Three-way mutual dependency between 0012's executor, 0013's tags/settings and 0014's prologue. The 0012 executor emits 0013's prelude, guard and bump, wraps the body in 0014's IF @tm_Guard, and takes 0014's application lock. 0012 §13.2's fixture composes 0013's core.VersionTags and 0014's six tables (every audit FK targets core.Users). A workable order:
- 0010-F
- 0013-a
- 0011 composition and request context
- 0012 entity bases, annotations and emitter
- an annotations step declaring [Stack]/StackOperations (0015), [ApiResource] (0016) and [BlobReference]/BlobPreset/BlobReadAccess plus the core.Blobs model (0017)
- 0014-a (tables into the fixture)
- 0012 executor, with seams for the contributors, the marked-batch flag, persist-frame steps 3-5/7 and result-set accounting, tested with fake contributors
- 0013-d
- 0014-b
- 0014-c
- 0013-e
- 0013-f
- 0014-d
- 0014-e
- the 0015 pipeline
- 0014-f
- 0013-h

0013-b, 0013-c and 0013-g can run in parallel once their stated prerequisites are in. 0013-f and 0014-d are mutually dependent (the prologue absorbs the cold settings load, the runner re-runs the Order-200 initializer, and that initializer reads the connected profile). Build 0013-f with the loader-batch path and a fake Order-100 initializer, and finish the wiring in 0014-d. If one agent owns both, merging them is an option.

3) Test ownership moves. Some 0013 §12 integration cases need 0014's runner: 'guard raises 50412 and the runner re-runs once', the Rerun/Refresh policy cases, and a membership save through a user save. Those move to 0014-d. 0013's MeResult DTO-shape snapshot moves to 0016. 0014's 'role save racing me/locale' and the 'inbox seen' self-write cases move to 0018 and 0021. The witness HTTP filter test moves to 0016. The change-tracking write-set audit is described three times (0012 §13.2, 0013 §12, 0014 §8.1); implement it once in 0012.

4) Composition lines that reference later specs:
- 0014 §1.2 registers the User and Role stacks with 0018's UserService<User>/RoleService<Role>; land those lines with 0018.
- 0013's SettingsService needs 0015's ApiService/IApiActionInvoker and 0014's RequireAsync.
- 0013's SettingKey References rule needs 0015's IStackRegistry.
- [Cacheable]'s startup check needs 0014's registry, and the cacheable Read securable with FilterRoot=null is registered by 0015's StackSecurableContributor.

5) Shared seams to keep consistent:
- CacheOptions (Tellma:Cache) sizes every cache, including 0014's permissions, preferences and connect caches.
- The @tm_ parameter namespace is shared by 0013's guard/bump and 0014's prologue/re-checks.
- One @tm_tag Guid per batch serves the bump, the companion UserStamps insert and refresh-caches.
- Error numbers 50401/50412/50422/50503 are mapped in 0012 §6.3.
- The executor needs a flag that distinguishes an EvaluateFor ForUser load (no user snapshot) from a job-scope ForUser batch (replace the snapshot). Neither spec defines it.
- Tellma.Core.Tests, Tellma.Core.IntegrationTests and test/shared/Tellma.Testing.Entities do not exist yet (0011/0012 create them); 0013 adds test/shared/Tellma.Testing.Resources.

6) Content and data gaps with no source: an Arabic Strings.ar.resx for Core (the reference distribution declares ar-SA and the startup check demands the satellite); CLDR region display names per shipped language (a reproducible generator, e.g. Node Intl.DisplayNames from the client toolchain); the frozen 243 language-region pairs, generated once from .NET 10 cultures. The pair and OptionalCalendars assertions depend on ICU and must pass on both Windows ICU and Ubuntu libicu.

7) The test/core/Tellma.Core.Email.Tests/Observability/AlertQueryTests.cs name test is email-scoped. New infra/monitoring queries for meter Tellma.Core (0014 names three; 0013 names none) need a Core-level equivalent.

8) Size calibration: 0013 is estimated at about 43.5k LOC across 8 checkpoints (summing the per-checkpoint figures: 5.5+5+5+6+4.5+7+5+5.5k) and 0014 at about 32k across 6. Both run high because of SQL integration suites, the 2,196-row CalendarMonths seed migration, resx data (Region_ names in two languages) and calendar goldens. If the planner wants fewer, larger checkpoints, the natural merges are 0013-b+0013-c (pure localization, about 9.5k) and 0014-e into 0014-d (about 12k, which is too big), so keep 0014 as six.

### Cluster c04: 0015, 0016

1) Contract-seed ordering trap (most important). Specs earlier in the chain consume types owned by 0015/0016: 0014's IAccessEvaluator.RequireAsync throws 0015's ForbiddenException(ForbiddenReason) and its validators use ValidationErrors/ValidationCodes; 0014's User/Role entities carry 0016's [ApiResource]; 0012 §2.2's startup gate reads 0016's [RelatedSelect]; 0012 §12.3's analyzers TELLMA0004-0006 target 0015's [ApiAction]/[EntityAction]/EntityService/IEntityValidator/IPersistEffect/IEnlists; 0011 §2.1's contribution items (Entity/EntityCompanion/ApiService/Validator/Confirmation) are realised by 0015; 0011 §5.1 step 14 slots 0016's TellmaContractMiddleware. Recommend an early 'contracts seed' checkpoint: 0016-a (TellmaJson, scalar converters, attributes, McpExposure, records) plus the Errors/Validation/annotation parts of 0015-a, landing before 0012's metadata gate and analyzers, before 0014, and right after 0011's composition contracts. The rest of 0015-a (contexts naming IDataBatch/RequestContext/version-tag types) follows once the 0011-0014 contracts exist.
2) 0015's contracts reference types owned by LATER specs: 0019 (IExcelRowSource, ExcelQuery, ExcelPage, ExportSource, ChildExportOperations), 0020 (IJobQueue, IJobProgress, RunsWhenReadOnly), 0021 (NotificationRequest, WithdrawRequest, INotifier), 0022 (EmailOutbox exemption), 0028 (BatchAssignments). Either land those contract types contract-first in 0015-a or have the owning specs add context members later; the 0015 §17 API golden makes later additions visible churn, which is permitted (additive).
3) Shared seams 0012 must provide for 0015: deterministic batch-local naming plus a recording view for persist re-entry/replay (0015 §5.1), an append-time CallerAuthority hook (0015 §13.3), StatementOrdinal on executor exceptions (0012 §6.4), a commit-probe/killed-connection test seam, and DataAccessScope.RoundTrips. Have the 0012 planner include these, or 0015-g/0015-i will have to amend 0012 code.
4) Fixture additions: 0015's suites need columns that 0012 §13.2's fixture lacks (Shipment↔Widget FKs both directions, Shipment with Enlist, fixture.Gadgets). Add them in 0015-i/0015-d to test/shared/Tellma.Testing.Entities with a migration; lower ceilings on fixture stacks via [Stack(...)] for the volume pins (MaxTake, CountCap, MaxDeleteByQueryRows) so integration tests stay fast.
5) Must-land-together: 0015-e's computed extras and arguments need 0016's TellmaJson and the QueryRowSet converter (0016-a/0016-b) — if the converter is placed in 0016-b, schedule 0016-b before 0015-e. 0016-c needs 0015-h (actions) and 0015-i (IApiActionInvoker). 0016-f needs 0018's Core/GL stacks and the acme reference distribution with 0011's migrator, so it comes after 0018. 0015-k's conformance base is closed by 0018 for User/Role/Center.
6) Cross-spec test pins to relocate: 0015 §18.2 job-host enlistment/lost lease → 0020; 0015 role-save blocking pin → 0018 (or raw write); 0015/0016 child-export pins → 0019; 0016 reports lanes → 0033; 0016 blob/hub chains → 0017/0021; 0016 tellma_get of core.Role → after 0018.
7) Size calibration: 0015 is the largest spec (242 KB) and the heart of the backend; estimated ~49k LOC across 11 checkpoints (contract-heavy XML docs plus a scripted batch double and a large SQL integration suite). 0016 ~36k across 8 (MCP ≈ 10k of it, deferrable). MVP slice: 0015-a..i plus 0016-a..d and 0016-f; 0015-j (Excel row source) only blocks 0019; 0015-k and 0016-e are important; 0016-g/h (MCP) can slip.
8) No outside working files: neither spec references .tmp/, docs/research or brand assets. The external items are SDK/package verification (ModelContextProtocol.AspNetCore 2.2.0 features; Microsoft.AspNetCore.* pin bump to 10.0.11, which touches the frozen identity server), Docker for Linux Testcontainers, and a live identity server for the nightly MCP cases only.

### Cluster c05: 0017, 0018, 0019

1. Build 0017-a (blob primitives: [BlobReference]/BlobPreset/BlobReadAccess, BlobKindPolicy plus presets, the IBlobStore family, BlobName, IImageProcessor, FileSystemBlobStore, conformance base) almost first. It has no dependencies, and 0012's EntityMetadata.BlobReferences (BlobReferenceMetadata.Policy is a BlobKindPolicy) and 0014's User entity (ImageId/SignatureId attributes) cannot compile without it. Split the Blob entity and BlobRejectedException into 0017-c, because they need 0012's Entity<int>/TableName, 0015's TellmaException and 0016's RelatedSelectAttribute.
2. Imaging (0017-b) is load-bearing. Core's user-image kind uses the Avatar preset, which has an Image policy, so 0017 §9's `blobs` startup check fails in any host (web, migrator or integration test host) without an IImageProcessor once 0017-c is in. Land 0017-b no later than 0017-c, or give test hosts a fake processor.
3. 0015 §7.4 validates every changed FK under the target's Read decision, but core.Blob has no securable. Either 0015's FK validator exempts [BlobReference] columns or 0017-c amends it; otherwise every attach fails with Fk.NotFound. Decide which checkpoint owns the fix.
4. 0014 §1.2 and 0018 §1.2 both claim the Entity<User, UserService<User>>/Entity<Role, RoleService<Role>> registrations, and the service types are 0018's. 0014's checkpoint should ship UserService<TUser>/RoleService<TRole> shells (plus the registration), and 0018-c/e should fill them in without a second Entity line.
5. 0015 §15 implements 0019's IExcelRowSource/ExcelQuery/ExcelPage/ExportSource contracts. The 0015 row-source checkpoint creates them in Tellma.Core.Abstractions.Excel with 0019's exact shapes, and 0019-a adds the rest.
6. Identity server prerequisites: Tellma.Identity's management API is currently unversioned (api/identity/invitations, api/identity/service-accounts) and has no ExistingOnly. 0010 Part A §A.1, §A.8 (and ideally §A.7) must be implemented before 0018-a/0018-c's in-proc integration tests. 0010 §G.1 ([Collation] on standalone classes) is needed for UserInvitationOutcomeList. 0010 §F.1 (KeySetRestriction) and §F.7 (FilterTree.Via) are not in src/core/Tellma.Core.Queryex yet and are needed by 0017-c, 0018-e and 0019.
7. Acme migration strategy: 0011-0016 checkpoints will need interim tenant migrations in distributions/acme/src/Tellma.Distro.Acme.Migrator. 0018-h's InitialTenant lists tables of 0017, 0019-0022, 0026, 0028, 0033 and 0035, and its parity test (GetPendingModelChanges empty) runs every commit. Adopt one rule for every checkpoint that adds or changes a tenant table: regenerate the single InitialTenant migration with dotnet-ef 10.0.9 until first release, and keep the parity test green. LOC estimates exclude the generated migration/snapshot (likely 8-12k lines).
8. Features owned by later specs but woven into these three:
   - The CoreFeature BlobKind transform for email-attachment ships with 0022. The support-screenshot narrowing ships with 0026. A transform naming an undeclared kind fails startup.
   - The welcome email (IEmailOutbox in invite) and EmailNotificationChannel.DeliverAsync belong in 0022's checkpoint; 0018-f ships only the class plus the test send.
   - Acme's [Assignable] invoices, assign/post, acme.invoice.assigned and demo assignments ship with 0028. AcmeReports, the acme.sales-by-center seed, its Sales grant and core.report-seeds ship with 0033.
   - The Report case of 0019's ExportJobArguments/ExportJobHandler ships with 0033.
9. Mutual dependency between 0019 and 0020. 0019-c's Export/Import entities need 0020 (IJobEntity/JobId FK, IJobQueue, Job.Status), and the import-file kind exists only when Import.FileId is in the model, so even inspect-import depends on 0020. Meanwhile 0020 §14.4 core.file-retention deletes through the Export/Import (and 0026 SupportRequest) stacks. Sequence 0020 core first, then 0019-c, and land the file-retention handler after 0019-c (or make it tolerate absent stacks).
10. Landing 0019-b adds export routes, and later checkpoints add the import routes, on every stack with Export/Import, including users, roles, centers and acme. Update 0015/0016 API goldens and any route-list assertions (for example 0018 §5.5) in the same checkpoint.
11. Acme development data has two unresolved problems:
   - 0018 §6.3 invites e2e principals during `migrate`, but the in-proc identity engine runs only in the web host and the <slug>-svc client is seeded at web startup (0011 §5.7). On a fresh clone, and in 0024 §13's Playwright webServer, which runs migrate first, the invite has no server. Settle the approach before 0018-g, for example bootstrap-style direct Subject assignment for Development principals.
   - The tenant-2 sandbox invite of 'a user of no other tenant' fails under ExistingOnly.
   - Client specs 0024 §13 and 0029-0032 depend on these principals and on the acme entities, so 0018-g is core for the client.
12. CI and test infrastructure:
   - Bump Testcontainers.MsSql from 4.13.0 to 4.14.0 (0011/0012) before adding Testcontainers.Azurite 4.14.0.
   - Azure.Storage.Blobs 12.29.2 changes Tellma.Identity's transitive closure under central transitive pinning, so re-run the identity suites.
   - Docker is required on the Linux runner for Azurite.
   - Put BlobStoreConformanceTests in src/core/Tellma.Core.Testing, which already exists and hosts 0015's StackConformanceTests.
   - Blob alert queries need a new alert-query test; the existing one is email-only.
13. Verify every new package pin resolves before its checkpoint, since the versions were chosen in spec text dated 2026-09: SkiaSharp 4.152.1 (+ NativeAssets.Linux.NoDependencies), PDFtoImage 5.4.0, Azure.Storage.Blobs 12.29.2, Testcontainers(.Azurite) 4.14.0 and DocumentFormat.OpenXml 3.5.1.
14. Human-only items to schedule for the first human-testing milestone:
   - 0019's corpus files saved by Excel, Google Sheets and LibreOffice (LibreOffice can be scripted), and the manual Windows-Excel check.
   - 0018's 'acme smoke deployment' CI job, if no deployment environment exists.
   - The first real Azure deployment's check that Storage Blob Data Contributor covers container creation (0017).
15. Calibrated totals including tests, excluding generated migrations: 0017 ≈ 21-22k, 0018 ≈ 30-32k, 0019 ≈ 33-34k.
16. Suggested placement:
   - 0017-a: before 0012's metadata checkpoint.
   - 0017-b: any time before 0017-c.
   - 0017-c: after 0012, 0014 and 0015 core.
   - 0017-d: after 0016 and 0018-c.
   - 0018-a/b/c/d/e: right after 0016.
   - 0018-g/h: after 0017-c.
   - 0018-f: after 0021.
   - 0017-e: after 0020.
   - 0019: after 0020/0021 and before 0033.

### Cluster c06: 0020, 0021, 0022

1) Contract layering is circular and must be sequenced explicitly. The abstractions of 0020 (IJobQueue, IJobProgress, JobItem, JobStatus, Job), 0021 (INotifier, NotificationRequest/WithdrawRequest, descriptors, IClientEventPublisher, ClientEvent) and 0015 (CodedError, ValidationException, the [Stack] attributes) reference each other. 0011's FeatureContribution already names NotificationTypeDescriptor and NotificationChannelDescriptor. 0013's cache.changed publisher needs IClientEventPublisher, and 0015's PersistContext/ActionContext.Notify/Withdraw and SaveOptions.OnPersist need INotifier/IJobQueue/IJobProgress. Recommended order: 0011 composition (with the descriptor records or a forward stub) → 0012 → 0015 contract types → 0021-a → 0020-a → 0013 cache publisher / 0015 pipeline runtime. Keep InboxSummary/InboxRunningJob (which use JobStatus) out of 0021-a.
2) Schema before notifier. 0021-b's delivery enqueue INSERTs into core.Jobs with sq_Jobs, and the summary reads IX_Jobs_RequestedActive, so 0020-b must land before 0021-b. 0020-c's completion calls INotifier; spec 0020 §16 allows in-memory fakes, but the real Notifier (0021-b) should precede 0020-d's end-to-end tests where possible. Define one internal tenant-poller wake seam in 0020-b; IJobQueue, the INotifier post-commit nudge, run-now and tick step 2 all use it.
3) Worker before consumers. 0021-c (delivery and retention handlers), 0022-b (dispatch), 0017's blob sweep/reconcile and 0019's export/import handlers all need 0020-c/d. 0020-c should ship a test-only 'poll once for tenant' driver, so that the consumers' integration tests do not depend on hosted-service timing.
4) The database clock is the only clock (0020 §1.3). Lease expiry, backoff, the heartbeat throttle (60 s), GapThreshold (≥1 h) and the tick lease (30 s) are computed by SYSUTCDATETIME(), so a fake TimeProvider cannot advance them. Provide shared fixture helpers that rewrite DueAt/LeaseExpiresAt/LastTickAt/Attempts. Without them, the 0020/0021/0022 integration suites either sleep for minutes or cannot reach their paths (e.g. the outbox's 12-attempt exhaustion).
5) Cross-spec seams that need an owner at planning time:
   - EmailNotificationChannel: 0018 declares it, 0022 §4 specifies and tests DeliverAsync. Put DeliverAsync in 0022-a.
   - The hub-claims step in 0011's tenant middleware (§4.3 step 3) uses 0021's TellmaHubClaims. Either 0011 leaves a hook, or 0021-d amends it.
   - MapTellma's hub mapping (0016 §9.2) is filled by 0021-d.
   - core.file-retention (0020 §14.4) needs the 0019 and 0026 stacks; it is split out as 0020-g, which is under 2k LOC and should be folded into a later checkpoint.
   - The jobHandlers/notification client-metadata sections need 0024 §3.2's IClientAssetContributor.
6) The InitialTenant migration (0018 §7) enumerates tables from 0012–0022, 0026, 0028, 0033 and 0035. Either generate it once in the last backend checkpoint, or allow regenerating it per checkpoint with a final review. Do not freeze it in 0018's own checkpoint.
7) Verify early in 0020-b that the core.Schedules → ScheduleStates (CASCADE), Schedules → Jobs (SET NULL) and Jobs → ScheduleStates.LastJobId (SET NULL) graph is accepted by SQL Server; error 1785 (multiple cascade paths) is likely. Also fix the StringList [Value]→[Id] slip in tick step 1's ReadOnly variant.
8) Three specs (0020, 0021, 0022), plus 0011 and 0007 already, mandate alert queries under infra/monitoring/ checked by 'the existing name test'. That test lives only in Tellma.Core.Email.Tests and scans infra/monitoring/email. Generalise it once, in the first checkpoint that adds non-email KQL (0020-d), and reuse it.
9) Integration fixtures for all three specs run with RCSI on and off on LocalDB (Windows) and Testcontainers SQL Server (Linux). That doubles their runtime; budget roughly 10–15 min of PR integration time for this cluster. Only 0021's Azure SignalR live suite needs nightly gating and a CI secret.
10) Package pins introduced: Cronos 0.13.0 (also listed by 0011 §1.1; the first checkpoint to land adds it) and Microsoft.Azure.SignalR 1.33.1 (Tellma.Defaults.Azure.AspNetCore only). The StackExchangeRedis pin belongs to a future on-premises host; Quartz stays identity-only.
11) MVP critical path through this cluster: 0021-a, 0020-a, 0020-b, 0021-b, 0020-c and 0022-a. 0018's invite flow, which needs the outbox enlist plus core.user.added through INotifier, makes these load-bearing even though 'jobs/notifications/email' look like peripheral features. 0020-d, 0020-e and 0022-b are next in importance, because without them no background work ever runs and no email is sent. The hub (0021-d), the inbox (0021-c), the outbox verbs/webhooks (0022-c) and file-retention can slip.

### Cluster c07: 0023, 0024, 0025

1) Interleave 0023 with 0010 Parts B–E (0010's 'Parts B–E shared rules'). The order is: 0023-a (icon, kbd) → 0010 Part B → 0010 Part D (§D.1 overlay, §D.2, §D.4, §D.9, §D.10) → 0023-b/c/d/e/f. 0010 Part C needs 0023-a and Part B. 0023-c (chip) must land before 0010 Part E. 0023-g (tiles) needs 0010 Part C (§C.1, §C.2, §C.9). 0023-h comes last. 0010 §C.15's popup cell editor hosts 0023's tm-date-time-field, so §C.15 cannot fully finish before 0023-e.

2) 0024 and 0025 are mutually dependent inside the @tellma/core primary, and 0026/0027/0028/0029 add more back-references. A workable order:
- 0024-a → 0024-b → 0024-c → 0024-d → 0024-e
- 0025-a → 0025-b
- (0011 landed) 0024-f
- 0023-b/d done → 0024-g → 0024-h → 0025-e
- 0024-i (also needs 0023-c/d) → 0025-c → 0025-d
- 0024-j (after 0029/0028 seams) → 0024-k → 0025-f (after 0027/0028/0030/0031/0032)

0024-a should create explicit seam tokens with no-op defaults so later specs fill them rather than refactor:
- TmTelemetry, TmRequestLog begin/end, TmErrorHandler and provideTmPageScope/TmPageContexts (0026)
- TmEntities.bySegment over the Core chunk (0029)
- /shell placeholder route components (0027)
- TmInputIdle and the toast overflow (0028)
- the TmHomeHost/TmSearchHost/TmDetailsHost stubs (0030/0031/0035)

3) Spec 0011 vs spec 0024 ownership:
- The C# IClientAssetContributor/ClientAssetContext/ClientMetadataChunk/TellmaRoutes, the emit file layout and naming, MapTellma's SPA hosting rules and the hosting test matrix are specified in 0024 §3.2/§6/§13 but implemented by 0011 §5.1/§5.3/§6.1, which builds first. Assign them to 0011.
- 0011's C# tests link `client/projects/core/tellma-core/src/launch/launch-landing-vectors.json`, a package 0024 creates. Either 0011 creates the file at that path first, or 0024-c lands before 0011's test.
- 0011's DoD lists the Acme Client esproj, but its build runs `tellma-client build` (0024-e). Either 0011 ships a placeholder Client project that 0024-f completes, or 0011's Client esproj build waits for 0024-e.

4) Repo-root pnpm workspace. ARCHITECTURE.md, 0011 §1.2/§10 and 0024 §1 assume one workspace at the repo root with a catalog, but today it is client/pnpm-workspace.yaml and no spec owns the move. Put it in 0024-a, or earlier if 0011's Acme Client project lands first. It touches every client/scripts/*.mjs and CI.

5) A blocking defect needs a decision before any CSP-served e2e (0024-k). 0024 §6's CSP (script-src 'self', no unsafe-eval, require-trusted-types-for 'script') breaks @jsverse/transloco-messageformat, because @messageformat/core 3.4.0 compiles with `new Function`. This affects every ICU string in core-ui and core. Resolve it (build-time precompile of ICU messages in tellma-client build, or an eval-free runtime) as part of 0024-e/0024-h.

6) The Acme e2e host (0024-k) is shared by every client spec (0024–0035).
- Build it once and early, and cache the built output.
- It needs SQL Server (LocalDB on Windows, Testcontainers/Docker on Linux), in-proc identity (0018 §6.2), the acme.sample-data principals (0018 §6.3), and an out-of-process way to read email sign-in codes. That last one is unspecified: 0003 §10.6's capturing sender is in-process only.
- Many 0024 §13 and 0025 §14 flows depend on 0027 shells and pins, 0028 banners and the bell, 0030/0031 pages and saves, and 0032 Language & region and admin. Schedule those flows as follow-ups attached to the owning specs' checkpoints, not as blockers of 0024/0025.

7) Budgets.
- The @tellma/core primary ceiling (0024 §12) is the sum of shares: 14 KB for 0024's services, 4 KB of icon data, 9 KB for 0025, plus shares from 0026–0029, 0032, 0034 and 0035, to 50 KB total. Set interim ceilings per checkpoint, raising each with a budgetNotes entry.
- The 0023 entry points that the primary, /shell and /attention import statically count toward the initial-JS budget (230/260 KB).
- 0024 §3.6's startup config check would put tmCheckAppConfig in the eager primary; decide before 0024-b fixes its home.

8) New npm packages across the cluster: lucide (0023-a), @angular/service-worker 22.0.x (0024-e), tsx promoted to a runtime dependency of core-tools, lighthouse (nightly), @microsoft/signalr 10.x (0025-d). No new NuGet packages beyond what 0011–0021 bring.

9) Brand and PWA assets must be produced; none exist in the repo.
- @tellma/core/brand needs {wordmark,mark}-on-{dark,light}.svg.
- Acme needs favicon.svg, icon-192.png, icon-512.png and icon-maskable-512.png.
- The only wordmark sources are untracked .tmp/ui-design/assets/tellma-wordmark-{color,white}.svg and the identity server's _Wordmark.cshtml. There is no 'mark' glyph source at all.

10) Calibration. These estimates include tests, goldens, stories and e2e: 0023 ≈ 38k LOC across 8 checkpoints (30 entry points versus 0005's 26.7k for about 15); 0024 ≈ 47k across 11; 0025 ≈ 23k across 6. Every checkpoint is within 2.5k–6.5k.

### Cluster c08: 0026, 0027, 0028, 0029

I read all four specs in full (0026, 0027, 0028, 0029). Only the primitives and test infrastructure of 0001–0009 exist in the repo: src/core/Tellma.Core is an empty project. Tellma.Core.AspNetCore, the @tellma/core client package, @tellma/core-testing and @tellma/core-tools do not exist, and only client/projects/core/tellma-core-ui* exist. Every deliverable in this cluster is new code.

ORDERING TRAPS
1. Spec 0024 depends on primary-entry-point members owned by these four specs:
   - TmErrorHandler (0026 §4), provided by provideTellma.
   - TmRequestLog.begin/end (0026 §8.1), called by TmApi §10.3.
   - TmAppVersion.compare (0026 §7), used by 0024 §5.6.
   - TmTelemetry.flush (0026 §3.4), called at sign-out.
   - provideTmPageScope and TmPageContexts (0026 §9.2), used by the 0024 §4.4 route host.
   - TmShellMain (0027 §7.5), used by the 0024 §11 presenter.
   - TmShortcuts (0027 §4).
   - TmInputIdle and TmBanners with the offline/update sources (0028 §9), used by 0024 §7.8, §5.5 and §10.5.
   - TM_ATTENTION_DOCUMENTS (0028 §5.1).
   - TmEntities.load/bySegment, the details-page rule and TmPermissions (0029 §4, §7), used by 0024 §4.4, §9.2 and §11.
   - tmCheckEntityDeclaration (0029 §3), used by the 0024 §3.6 config check.

   Interleave the work:
   a. The 0024 package scaffold and metadata container (§1, §2.2, §3.1–§3.2).
   b. 0029-a, then 0029-b, 0027-a, 0026-a, 0026-b, 0026-c and 0028-c. These primary seams depend on little beyond the scaffold, and TmRequestLog and TmAppVersion are standalone.
   c. 0024's route host, HTTP layer, error presenter, PWA update and time-zone checkpoints.
   d. The shells: 0027-c, then 0027-d.

   Otherwise 0024 must carry stubs that later checkpoints replace.
2. 0027-d places the bell and tray (0028-e) and the banner host (0028-d) in the app bar. Land 0027-d with fixed reserved boxes, then have 0028-d and 0028-e plug in. Alternatively, pull tm-banner-host into 0028-c's slice, because the offline and update banners are core.
3. Several pages are declarations on the 0030 and 0031 frameworks: the 0026 Support requests pages (0026-h), the 0028 Notifications/Assignments/Jobs pages and job details (0028-g), and the 0029 binding's Advanced search/Create/Edit modals (which import /search and /details). Schedule 0026-h and 0028-g after 0030 and 0031. Build 0029-e with a TmLazyPickerPage seam, then wire it once 0030 and 0031 land.
4. Spec 0015 puts `BatchAssignments Assignments` on PersistContext and ActionContext (0015 §7.1, §13.1), and Notify/Withdraw from 0021, while 0028 §3.1 owns the BatchAssignments and IAssignments types. Either:
   - the 0028-a Abstractions contracts land with 0015's context checkpoint, with the member stubbed to throw; or
   - 0015 omits the member and 0028-a adds it to 0015's contexts, an amendment to freshly written code.
   Decide this up front. Spec 0018's Acme invoice assign/post and the demo assignments (0018 §1.4, §6.3) land only with or after 0028-a and 0028-b.
5. IDisplayNames is claimed by both 0012 (Goals, §3.1, §13 unit test) and 0029 §1. Implement the C# DisplayNames with 0012, because 0015 §5.2.1, 0019 and 0033 need it early. Create display-name-golden.json at its final client path (client/projects/core/tellma-core/…) at that time, so that 0012's C# test and 0029-b's TS test read one file.
6. 0029-a, the C# emit, needs 0024 §3.2's IClientAssetContributor and emit command. 0029-b needs 0029-a's fixture JSON. 0029-f needs the 0010 Part F engine amendments (§F.13, §F.14, §F.21) and 0015's guarded runner, PipelineLimits and traversal.
7. 0026-e (support server) needs the following, so it sits late in the server track:
   - 0017 (blob kind and attach), 0021 (notification type, Notify/Withdraw) and 0014 §10 (IAdministratorDirectory);
   - the 0011 admin surface;
   - 0015 Enlist/IOpenWriteHost;
   - 0020's core.file-retention, for its retention test.
8. 0027-b (TmNavigation) needs TmPermissions and TmEntities summaries (0029-b). 0026-a needs TmShortcuts (0027-a) for action shortcuts. 0026-c needs 0027-a for Mod+Alt+Shift+D.
9. The About dialog (0027-d) lazily hosts tm-diagnostics (0026-f). The dialog can ship first with a placeholder tab.

SHARED SEAMS THAT MUST AGREE
- TmShortcut ownership for the modal rule (0027 §4.2 step 5 versus 0026 §9.4) needs an internal owner tag. Specify it once in 0027-a and use it in 0026-a.
- The dispatcher-versus-bell Stop confirmation conflict (0026 §9.5 versus 0028 §6.2/§10.5/§13) needs a single ruling before 0028-e.
- Types exported from the primary that are defined by later specs (0030's TmTileDeclaration, TmCustomFilterDeclaration and TmSearchPageActionDeclaration; 0034's tmCodedReport launcher members) must be lifted early. 0029-b's implementer needs to read 0030's declarations of them.
- Shared goldens span TS and C#: display-name-golden.json (0012 and 0029) and queryex-print-golden.json plus the lexer corpus (0029-d against Tellma.Core.Queryex). Keep their generators runnable on Windows and Linux.

SLOW AND TIMING-SENSITIVE TESTS (cluster-wide)
Four specs mandate Playwright on the Acme host:
- 0026: 4 widths × en/ar.
- 0027: 6 widths × en/ar, plus Chromium --app.
- 0028: 4 widths × en/ar, multi-tab.
- 0029: Chromium, Firefox and WebKit for drag/undo.

Together that is well over 30 minutes if run in full on every PR. Recommendation: run one width per language plus Chromium only on PR, and gate the full viewport matrix, Firefox, WebKit and installed mode nightly under a trait. Gate micro-benchmarks behind a perf trait or give them CI multipliers: 0026 diagnosticsText < 2 ms; 0027 index ≤ 5 ms and query ≤ 2 ms for 300 entries; the frame-probe and CLS specs. The 0028 fixture needs READ_COMMITTED_SNAPSHOT on the shared SQL fixture, plus deterministic concurrency for the 2601 test. Every e2e checkpoint (0026-h, 0027-g, 0028-h, 0029-h) depends on a running Acme stack (0011 host, 0018 reference stacks, 0024 Acme client and e2e harness), which exists for none of these specs today. Consider one shared Acme-e2e harness checkpoint before them.

ESTIMATES
- 0026: about 30k LOC.
- 0027: about 32k.
- 0028: about 32k (server about 8.5k).
- 0029: about 36k (server about 8.5k).
- Total: about 130k LOC across 31 checkpoints, each 2.5k–6.5k.

MVP SUMMARY
- Core: 0026-a, 0026-b, 0026-c, 0027-a through 0027-e, 0028-c, 0029-a through 0029-e.
- Important but load-bearing: 0029-f and 0029-g, needed by 0030's advanced filter and column chooser and by 0032's role permission-filter cell.
- Deferrable: support requests (0026-e, 0026-g, 0026-h), the assignment server stack (0028-a, 0028-b, load-bearing only for Acme's assignable invoice and the home lists), notification settings (0028-f), and the 0028 e2e (0028-h).
- New NuGet: OpenTelemetry.Exporter.InMemory, for the relay test. No new npm packages.
- Brand SVGs: real artwork must be supplied for @tellma/core/brand/ (0024 §7.6, rendered by 0027). No tellma-brand checkout exists beside the repo.

### Cluster c09: 0030, 0031, 0032

Build order across the cluster: 0031-a first, then 0030-a/b/c, then 0031-b/c1/c2/d/e, then 0030-d/e/f1/g, then 0031-f/g, then 0030-f2/h/i, then 0031-h/i, then 0032-a through 0032-h.
- 0031-a (`TmEntityActions`, `TmConfirmations`, the generated action dialog, the error-path splitter) lives in `@tellma/core/entity`, not `/details`. It must land before 0030-f1 (bulk actions) and before 0032-c (the invite and credentials dialogs use `run({ keepOpen, ids, rethrow })`).
- The types-only primary exports must exist before any consumer compiles: `TmSearchSnapshot`, `TmTileDeclaration`, `TmCustomFilterDeclaration`, `TmSearchPageActionDeclaration` and `TmColumnPreference` from 0030-a; `TmDetailsSnapshot` and `TmDetailsDraft` from 0031-a. Spec 0029's `tmEntity` declaration type already references `TmSearchPageActionDeclaration`, so 0029's checkpoint may need to land these types first.
- 0030-g writes the search snapshot and router state that 0031-g reads. 0031-g can be unit-tested with fake snapshots, but its paging e2e needs 0030-g on Acme.
- `TmImportResultView` ships in 0030-f2 and is load-bearing for 0032-b2. If export and import slip, the Imports page slips with them.
- 0032 creates `@tellma/module-gl`, but the Acme `app.config.ts` from 0024 already lists `tmGlFeature()`. Either 0024 ships a stub package or the Acme app adds the GL feature in 0032-a. The 0030 tree e2e on Centers (0030-i) depends on 0032-a.
- The label chains of 0032 §12.4 belong in 0014's `SecurablesClientAssetContributor` (C#), whose record already names them. Put them in the 0014 checkpoint to avoid a golden churn later.

Seams that need a decision before coding:
1. `TmPageState.remember` clears `isStale` (0025 §10). Both 0030 §15.1 (remember on destroy) and 0031 §12.2 (remember the search snapshot on every step) can silently drop a `markStale` from a save. A stale-preserving update should be added to 0025's `TmPageState`, or the hosts should re-mark.
2. Same-route param navigations, such as the Settings tab click, trigger the router's `canDeactivate` and therefore the leave guard (0024 §9.2 / 0031 §11.3). Exempt them in `tmLeaveGuard`, or give the settings route a custom `runGuardsAndResolvers`.
3. The layering of `TmEntityActions.run` (overlay, confirmations, destructive confirm) must be fixed in 0031-a, because 0030 bulk actions (no overlay), 0031 actions (overlay) and 0032 dialogs (keepOpen) all consume it.
4. A search tree's snapshot `ids` include ancestor context rows, which breaks 0031 §12's position arithmetic. Decide whether `ids` hold matches only.

Infrastructure prerequisites:
- None of these specs can start until 0024 (the `@tellma/core` package, router, `TmApi`, Acme client app, e2e host), 0025, 0026, 0027, 0029, 0023 and 0010 Parts C/D/E exist. Today the client has only `tellma-core-ui`, without 0023's components or 0010's grid amendments.
- Every e2e suite needs the Acme e2e host, which needs most of the server stack: 0011, 0012–0019, 0020 and 0022 for 0032, and 0003 identity.
- Plan e2e as Chromium en plus one ar width on PR, with the width × language × engine matrix, `@perf` frame-probe and warm-launch specs, and the full axe batteries nightly. One mandated e2e, the 0032 one-hour membership expiry, needs a server clock hook nobody specifies.
- Each new entry point (`/search`, `/details`, `/admin`, `@tellma/module-gl` and `/centers`) needs API-extractor goldens, budget lines in `size:check`, README files per AGENTS.md, and en/ar string packs for `tm.search`, `tm.details`, `tm.entity` and `tm.admin`.

Size: about 37k LOC for 0030, 41k for 0031 and 41k for 0032, all including tests. These are composition-heavy UI specs with large harness suites, so expect more test code than 0006 per KB of spec but less than 0004.

### Cluster c10: 0033, 0034, 0035

1) Nothing in 0033–0035 exists yet: src/core/Tellma.Core is an empty project, Tellma.Core.Abstractions has only Email/Hosting/TableTypes/Tenancy/Webhooks, there is no @tellma/core client library (client/projects/core holds only tellma-core-ui*), no Tellma.Core.Mcp, no Acme distro, and no echarts dependency. The whole cluster sits at the end of the dependency graph: it needs 0010 Part F (Queryex engine amendments F.7–F.20) plus Parts B/C/D (core-ui amendments), 0011–0021 server, and 0023–0031 client.
2) Parallelisable early work: 0034-a (tm-pivot) and 0034-b (tm-chart) depend only on core-ui + 0010 Parts B/C/D + 0023, so they can be built long before any reporting server code. 0035-a (home page + widget registry) depends only on 0024/0025/0026/0028 and is the one MVP-core checkpoint in the cluster.
3) Ordering traps across specs that the planner must sequence or stub: (a) 0018 §7's Acme InitialTenant migration already lists core.Reports, ReportShares, Dashboards, DashboardWidgets and DashboardShares (with FK_DashboardWidgets_ReportId → Reports). Either generate it without them and regenerate or extend it in 0033-a/0035-b (pre-release regeneration is fine; the 0018 parity test will force consistency), or land those entities first. (b) 0019 §12.2–§12.3's export-job report case is typed over 0033's ReportExportRequest, so it must be stubbed until 0033-h. (c) 0016's Contracts chunk long/idempotent lists, its tellma-reports policy and its route tables name reports/* and dashboards/audience, so tests over them wait for 0033/0035. (d) 0024's metadata sections codedReports and widgetTypes, its build codes (coded-report-*, widget-missing/not-registered) and its tenant root route (which loads 0035's TmHomeHost) need either 0033-g/0035-a or tolerant placeholders. (e) 0030's list mode must leave a seam for 0034-e's crumb and chips from history.state.tmDrill.
4) Two-way seam between 0033 and 0035: 0033 owns IDashboardReadThrough, which 0035 implements. 0033-a should register a null implementation (ReadThroughAsync returns null, DashboardsUsing returns empty) so Report.InUse and read-through runs are inert, and 0035-b replaces it. The DI registration must be replaceable (last wins or TryAdd), and 0035-b's tests must assert the real one is active.
5) The generic sharing model in 0033-a/0033-b is shared infrastructure: 0035-b reuses it whole (ShareableService, Shareable<,>(), IShareCommands, shares extra, shared-with-me, IAudienceResolver), and 0034-f's @tellma/core/sharing dialogs serve both stacks. Build them generically, parameterise the sharing integration suite by resource from the start, and keep share writes MERGE-free: 0033 §5.4 says MERGE, but 0012's TELLMA0003 analyzer and its Decision 18 forbid it on temporal tables.
6) Two cross-language artifacts need a fixed home before the checkpoints that use them: the definition document's JSON Schema (produced in 0033-c, read by 0034-c's conformance test) and drill-golden.json (produced and consumed by 0033-e and 0034-e). Suggest test/assets/reports/, with a test that fails when the regenerated schema differs from the checked-in copy.
7) The real-server e2e checkpoints are 0034-i and 0035-e. They need the Acme server and client, SQL, Azurite, the job runner and the SignalR hub, and the specs ask for every engine, width and language on PR. Recommend a PR/nightly split: on PR, Chromium with en at 1440 px and ar at 390 px; nightly, cross-engine, the full width matrix and visual baselines in a pinned Linux container. Keep 0033's perf suite nightly behind a trait and record its timings rather than assert them, because the 'CI SQL tier' is undefined.
8) Rough totals: 0033 ≈ 52.5k LOC over 9 server checkpoints; 0034 ≈ 61k over 9 client checkpoints; 0035 ≈ 34k over 5. 0033-d/e/f and 0034-g/h are the riskiest to keep under 9k. If one overruns, move telemetry or plan-cache work out of 0033-e, or split the studio's item editor into its own checkpoint.
9) Icons and fonts: every Lucide name used across 0034 and 0035 must exist in the tm-icon registry, which does not exist in core-ui yet (0023 §3.1). Chart-bidi and visual tests also need a pinned Arabic font, but core-ui/fonts ships only Latin Noto Sans and Noto Sans Mono.


# Per-spec leads

## 0010-A

**Possible contradictions and gaps (unverified leads):**

- §A.11 vs the existing fixtures: test/apps/Tellma.Identity.IntegrationTests/Infrastructure/SqlServerFixture.cs:73 and Tellma.Identity.E2E/Infrastructure/IdentityServerFixture.cs:498 run a bare `CREATE DATABASE [name]` (the server default collation). The new store check refuses those databases. Part A Testing says the fixtures 'let EF create their databases', so 0010-a1 must rewrite both fixtures, or every existing identity test breaks.
- Part A Testing (unit) runs 'Spec 0011 §10's collation pin' over Tellma.Identity.Migrations, but 0011 is unimplemented, so no pin helper exists. Part A DoD also says 'No new project'. The helper must go into an existing test project (the identity unit tests, or test/shared/Tellma.Testing.Support) so 0011's catalog and Acme pins can reuse it later. Its location is unspecified.
- §A.3 integration ('A full code flow against a local document server') conflicts with §A.3's own fetch guards: https only, and a host may not resolve to loopback or private addresses. TellmaIdentityClientMetadataOptions has no test escape hatch, and the unit tests' 'fake resolver' implies an injectable resolver abstraction that the contract does not name. The implementer must add an internal seam, such as replacing the named client's primary handler or resolver through DI.
- §A.4: the redirect URIs of the five interim clients are unspecified (vendor callbacks, and the paths for port-less loopback). Part A DoD still counts §A.4 as 'seed configuration in Tellma.Identity.Web'. That configuration cannot be written correctly without external input; the code (ThirdParty) can.
- §A.5 says 'The control-plane client stores no resource grant'. Today's seeded control-plane descriptor (ClientDescriptorFactory, around line 285) holds rsrc:urn:tellma:control-plane and rsrc:<issuer origin>. The spec does not say whether these permissions stay. They must, for 'a request without resource still gets those platform audiences' and for the operator API's aud check, so the sentence should read 'no distribution-origin grant'.
- §A.3: DocumentClientHandler runs on the authorize, PAR, token and revocation endpoints only. The end-session and introspection endpoints are not mentioned. A document client presenting client_id at end-session would resolve through the stale row, without a document check.

**Slow or heavy tests (planning estimates):**

- Part A integration suite (SQL Server through Testcontainers.MsSql/Docker or TELLMA_TEST_SQL): about +2–4 min for the new store, flow and route tests. Keep on every PR, as the spec requires.
- Identity E2E (Playwright .NET, Linux job): about +1–2 min for the Arabic PAR sign-in and the account back link. Keep.
- §A.3 fetch-guard timeout (default Timeout 5 s) and the per-host-per-minute rate limit: drive them with FakeTimeProvider and short configured timeouts so unit tests do not sleep. Keep.

**External dependencies:**

- SQL Server 2022 for integration and E2E: Testcontainers.MsSql (Docker) or the TELLMA_TEST_SQL connection. The Azure SQL catalog_collation_type_desc branch cannot be exercised locally.
- dotnet-ef 10.0.9 (dotnet tool restore) to re-scaffold the Initial migration
- Playwright for .NET browsers plus Deque.AxeCore.Playwright for Tellma.Identity.E2E (existing)
- An HTTPS-capable local document server for the §A.3 integration flows (Kestrel dev cert), or an in-memory primary handler swapped into the named client
- No new NuGet packages (spec: 'No new package is referenced'). OpenIddict, Quartz and IHttpClientFactory already exist.
- For §A.4 hosted entries: the vendors' published OAuth callback URIs for hosted Claude and hosted Codex. These are not in the repo.

## 0010-B

**Possible contradictions and gaps (unverified leads):**

- §B.3 says provideTellmaUi provides `Directionality` with ɵTmLiveDirectionality at the root, but the showcase today wraps stories in a [dir] element (CDK Dir). Part B DoD requires the showcase overlays to stop depending on that wrapper, so every story that flips direction through the wrapper must flip <html dir> instead. That migration of existing e2e specs (grid-rtl, locale-ar and others) is unlisted work.
- Part B DoD sets the primary ceiling to 7 'Part D's primary additions included' (TM_TIME_ZONE, TM_DEVICE_TIME_ZONE, TM_NOW, TmL10n facade growth, clearAll +0.3). The spec gives no measured current primary size, so 7 may be too tight once Part D lands. If so, Part D needs its own budgetNotes raise, which the spec does not foresee.

**Slow or heavy tests (planning estimates):**

- The Playwright showcase battery grows: new stories × 4 widths (390/768/992/1440) × LTR/RTL in Chromium, plus @cross-engine on Firefox and WebKit, plus the axe battery (light/dark × LTR/RTL with popups open). Estimated +3–6 min for Part B. Keep; tag only the necessary specs @cross-engine.
- §B.7's pixel-for-pixel comparison of tm-checkbox against a grid cell: screenshot comparison is font- and AA-sensitive across OSes. Keep it on Chromium only, with a tolerance.

**External dependencies:**

- lucide npm package (a new dependency of @tellma/core-ui, introduced by 0023 §1/§3.1; not yet in client/package.json)
- pnpm 11 / Node, vitest browser mode with Playwright 1.61.1 browsers (chromium, firefox, webkit), axe-core run in-page by client/e2e/support/axe.ts (removed 2026-11-08 by PC-F05: the e2e axe floor runs axe-core directly through client/e2e/support/axe.ts)
- Browsers supporting media-query range syntax (Safari 16.4+, Firefox 102+); Part B Review flag 4

## 0010-C

**Possible contradictions and gaps (unverified leads):**

- The Parts B–E order rule ('Spec 0023 §3.1 and §3.2 first, then Part B, then Part D… Part C… follows Part B') never says that Part C needs Part D. Yet §C.10's submenus push onto 'the shared Escape stack (§D.1)', §C.15's pane is a §D.1 anchored pane with popoverHost parent, and §D.1 says every library entry point (grid and menu included) imports @tellma/core-ui/overlay. §D.14 also edits the grid's number cells. Part C must therefore come after at least 0010-d1, and preferably after all of Part D, to avoid grid merge conflicts.
- §C.11 sets ./grid to 43 KB (42 for §C.1–§C.14, +1 for §C.15). This does not cover §D.14 (number cells wrapped in a dir=ltr span) or §E.4 (itemDescription, itemLabelParts and formatParts on TmGridColumn, plus two-line rows in the cell dropdown). Part E's gates list the ./grid golden but raise only ./entity-picker, so size:check may fail with no sanctioned raise.
- §C.4's key table says Enter or Space on the select-all header 'toggles select-all', while §C.5 lists the toggle as 'a header click, Space on its header cell, or Ctrl+Shift+Space' and omits Enter. Pick one.
- §C.1 (F2 on a popup column's cell opens the read-only pane, §C.15) ties §C.1 to §C.15. In the proposed split, c3 ships F2 as actuating the first interactive child, and c6 adds the popup branch.
- §C.9's breaking loadChildren signature: Part C flag 5 notes the alternative. The spec asserts 'no other consumer exists', which holds today (only tm-tree-grid.examples.ts and specs). If spec 0030 or other client work lands first using the old shape, it breaks.

**Slow or heavy tests (planning estimates):**

- §C.13 scroll probe ('no long task over 50 ms') on an expanded tree. This is a timing-sensitive perf assertion. Run it on Chromium only, non-parallel, with retries, or gate it with a tag on shared CI runners. Expect about 10–20 s.
- §C.7 gutter e2e. It needs a dedicated browser launch with ignoreDefaultArgs ['--hide-scrollbars'] and is platform-dependent (classic scrollbars on Windows and Linux only). Keep it on Chromium only.
- Part C DoD 'the active-row class lands in the press's frame'. This frame-timing assertion can flake on loaded CI. Keep it with a rAF-based check.
- §C.15 popup-editor e2e on @cross-engine (Firefox and WebKit), plus the RTL and axe variants of the grid-search-page and menu-rich stories. Estimated +6–10 min for the whole Part C battery. Keep.

**External dependencies:**

- lucide (via 0023 §3.1) and the kbd entry point (0023 §3.2)
- Playwright chromium, firefox and webkit; a Chromium launch with scrollbars shown for §C.7
- @angular/aria 22.0.3 Menu/MenuItem (submenu input, 100 ms expansionDelay) and the CDK overlay (existing)

## 0010-D

**Possible contradictions and gaps (unverified leads):**

- §D.14 defines TM_BIDI_CONTROLS only as 'spec 0024 §7.9's set'; Part D never lists the characters. The implementer of 0010-d2 must read spec 0024 §7.9, which is not yet implemented, to fix the regex. That makes a hidden cross-spec read prerequisite.
- §D.14 changes the grid's number cells (a dir=ltr span), which is grid code, while Part C's ceilings (§C.11) do not budget for it. If Part C lands first, d2 must re-check ./grid's ceiling. If d2 lands first, Part C's 43 KB includes it implicitly.
- §D.5: 'Every opener of a tm-* database therefore closes its connection on versionchange'. Today the only tm-* store is the CacheStorage tm-images-v1, and the IndexedDB openers (tm-app) arrive with spec 0025. The rule is an obligation on future code that no Part D test can enforce beyond the blocked-delete vector.

**Slow or heavy tests (planning estimates):**

- Part D DoD 'the drawer renders in the click's frame (panel present one frame after the click)'. This is frame-timing sensitive, so keep it with a rAF-based assertion on Chromium.
- The e2e 'counting aborted requests on navigation' (§D.6) depends on network interception timing. Keep, using route interception with deliberately delayed responses.
- Format golden growth: four new arrays plus period, name and grouping rows across locales and zones. This is fast (well under 10 s) but sensitive to the browser ICU and tzdata version, so goldens must be regenerated with the pinned Playwright Chromium. Keep.

**External dependencies:**

- The CLDR and ICU data of the pinned Chromium (Playwright 1.61.1) and of Node for golden:approve: outputs are ICU-version dependent
- tzdata in the browser for Asia/Beirut DST, Asia/Kolkata +5:30 and Africa/Addis_Ababa
- lucide (via 0023 §3.1) for §D.10 and §D.11

## 0010-E

**Possible contradictions and gaps (unverified leads):**

- §E.4 adds three inputs and two-line dropdown rendering to TmGridColumn (the ./grid entry point), but Part E raises only the ./entity-picker ceiling (12→14), and §C.11's ./grid ceiling of 43 does not budget for it (see Part C).
- §E.2's keyboard table: Backspace in an empty input 'first press calls focusLast(); a second press removes that chip'. After focusLast() focus is on the chip, so the second Backspace is the chip's own Backspace/Delete removal from 0023 §2.1, not an input behaviour. Implement it as the chip's removal to avoid a double handler.
- §E.2 composes 'a fixed-width count badge at the inline end' but does not say which component draws it (0023 §4.1 tm-counter or tm-badge, or a private span). The 0023 §1 table lists badge only as a dependency of chip, not of entity-picker.

**Slow or heavy tests (planning estimates):**

- Re-running 'the single picker's suite in emit mode' (§E.2 tests) roughly doubles the entity-picker unit time (+30–60 s). Keep.
- 'A pick shows its chip in the same frame (e2e)' is a frame-timing assertion. Keep it on Chromium.

**External dependencies:**

- lucide (via 0023 §3.1); the 0023 chip and badge entry points
- Playwright browsers and axe (existing)
- PC-023 → the 0010-E multi-picker checkpoint: `tm-chip-set overflow="scroll"` is ready for §E.2. `exit` emits `'start'`/`'end'` for an arrow past either end, and `'end'` when the focused chip is removed with no neighbour left; `focusLast()` focuses the last visible chip (call it on Backspace in the empty input). Delete or Backspace on a focused removable chip emits that chip's `remove`; once the host drops the chip, the set focuses the next chip, else the previous. `TmChip.activate` is an `OutputRef` built with `outputFromObservable`, so the chip renders a label button only while something listens: bind `(activate)` or use `outputToObservable`, never `.emit()`. The focused chip scrolls into view with `focus({ preventScroll })` plus a manual `scrollBy`, so the page around the field never scrolls.

## 0010-F

**Possible contradictions and gaps (unverified leads):**

- §F.0 'Order' says §F.8, §F.14 and §F.15 ship first because 'specs 0012, 0013 and 0014 cannot ship without them'. In fact 0012 needs §F.8 (QueryArguments.Omit) and §F.9 (IDataBatch.EvaluateConstant) but cites §F.15 only in a prose note on QueryColumn.Precision (0012 around line 675). No 0012, 0013 or 0014 API calls Measures or the evaluator; only spec 0033 consumes §F.14 and §F.15. 0010-f6 can therefore slip to just before 0033 without blocking 0012.
- §F.0 lists §F.11 as needed by 0012 and 0013. 0012 §11.1 calls QueryexSchemaBuilder.CalendarMap('uq', '[core].[CalendarMonths]') unconditionally, so either 0010-f5 lands before 0012's schema checkpoint, or that call is deferred. There is no middle ground unless CalendarMap and its Build() validation are pulled forward into f1 or f3.
- Part F DoD: 'the uq join-shape benchmark ran before §F.11 was built, and its result is recorded against flag 4'. The spec is frozen after merge ('revised only if implementation forces a design change'), and no location for the record is given (Queryex README? the PR? eng/ notes?). The planner must choose one.
- §F.15 PartialTable.Add(cell, partial, Array buffer, row) takes 'one of the reader's typed column arrays (spec 0012 §3.3)', but 0012's QueryRowSet is not built yet. Part F must define the accepted element types itself, consistent with 0012 §11.3's buffer table (int[], long[], short[], byte[], decimal[], bool[], string?[] for String and HierarchyId, DateOnly[], DateTime[], DateTimeOffset[]). Its differential harness must also produce reader Precision and Scale from DbColumn metadata without 0012. A mismatch would surface only when 0033 wires it.
- §F.19 tests require 'a checked-in vector file … that both the corpus and the interpreter read', and spec 0013 §12 reads 'the source of spec 0010 §F.19's vector'. The file's path, format and generator script are unspecified, and two specs share it.
- §F.22 does not pin the tzdb release for the checked-in zone.tab. The test 'every Zones key is current against the checked-in ids' may break when either side is refreshed independently.
- Integration fixture: §F Testing says the database 'is created as spec 0011 §6.7 states', but test/core/Tellma.Core.Queryex.IntegrationTests/SqlServerFixture.cs:68 runs a bare CREATE DATABASE. The fixture must change in f1, and existing differential results could shift for collation-sensitive vectors (text ties, ordering) because tempdb and the database collation now differ.

**Slow or heavy tests (planning estimates):**

- The 'uq' join-shape benchmark at 10M rows (Part F Testing and DoD), run once before §F.11. Loading 10M lines plus headers and running the three shapes takes about 15–45 min and roughly 1–3 GB on a local SQL Server. Do not put it in CI. Ship it as a script under eng/ (or a [Trait('Category','Benchmark')] test skipped by default) and record the result once.
- The §F.11 'uq' differential, for each of ~12 functions over every day 1900-04-30..2077-11-16 (~64.6k days) plus out-of-range days. If it is written set-based (one query per function over a day table compared to the interpreter) it takes seconds to about a minute. Per-day compiles would take hours. Keep, but require the set-based form.
- The §F.17 and §F.19 differential over every day 1900–2100 (~73k days) under 'gc' and 'et' for every function, and 'uq' over its range. It has the same profile: keep it set-based, about 1–2 min.
- The §F.15 differential: 500 generated measures plus hand-written ones, each a compiled statement plus regroupings for the three identities. That is about 1.5–3k SQL executions, about 1–4 min. Keep it on every PR as the spec says ('none is nightly'), with a fixed seed. If CI time hurts, a [Trait] could cut the generated count on PRs, but the spec demands it per PR.
- The §F.10 property test (bind-then-compile against compile-with-arguments, differential on the fixture). Keep with a bounded case count.
- LedgerFixture growth (child table, month map ~2k rows, numeric bound rows) adds seconds to integration setup. Keep.

**External dependencies:**

- SQL Server 2022 for the integration and differential suites (TELLMA_TEST_SQL or Testcontainers/Docker), with the database created as 'CREATE DATABASE … COLLATE Latin1_General_100_CI_AS' per 0011 §6.7. The LedgerDdl text columns keep their declared Latin1_General_100_CI_AS_SC.
- A SQL Server instance with room for 10M fact rows for the one-off benchmark
- IANA tzdb zone.tab (a release consistent with the embedded CLDR windowsZones table) for the checked-in id list (§F.22)
- Node.js with full ICU (Intl.DateTimeFormat 'ethiopic' calendar) to generate the checked-in Ethiopic vector file (§F.19), also reused by spec 0013 §12
- The .NET BCL UmAlQuraCalendar (range 1900-04-30..2077-11-16) and System.Data.SqlTypes.SqlDecimal. No new NuGet packages are allowed ('no new package reference').

- PC-012 → a later Queryex checkpoint (or a fix item): the null-safe comparison forms in Lowerer.Comparisons still repeat a non-atomic operand at group grain; a constant factor per comparison, not the exponential growth PC-012 fixed for calendar calls. Reuse PlanNamedScalar.
- PC-101 → the first checkpoint whose corpus or differential casts a datetimeoffset to a datetime: the reference interpreter (test/shared/Tellma.Queryex.Testing/Semantics/Interpreter.Functions.cs, AsMoment/Converted) converts an offset through UtcDateTime, while SQL `CAST(dto AS datetime2)` keeps the wall time and drops the offset. Nothing reaches that path today. Fix the interpreter to use DateTimeOffset.DateTime before such a vector lands, or the differential will disagree with the server.
## 0010-G

**Possible contradictions and gaps (unverified leads):**

- Part G Testing says the integration fixture 'creates each database with spec 0011 §6.7's SQL Server statement', but the fixture today runs a bare CREATE DATABASE and DatabaseCollations is introduced by Part A (0010-a1). If g1 lands before a1, it must either hard-code 'Latin1_General_100_CI_AS' in the fixture or add DatabaseCollations itself. Coordinate so only one checkpoint adds the type.
- §G.1 example: the registration uses the catalog's TenantMembershipRecord with the 'catalog' schema, both from spec 0011, which does not exist yet. The tests must use a local test class instead.

**Slow or heavy tests (planning estimates):**

- None heavy. The integration suite adds one SQL Server type round trip (seconds). Keep.

**External dependencies:**

- SQL Server (Testcontainers/Docker or TELLMA_TEST_SQL) for the integration suite
- dotnet-ef 10.0.9 to re-scaffold the migrations host

## 0011
- (PC-025) Once a tenant is bound, the request-context holder refuses a different Kind or Subject and a replaced or dropped UserId (null to a value stays allowed): a front door binds tenant and principal in one Set. The two-writers reflection pin searches only Tellma.*.dll referenced by Tellma.Core.Tests; the Tellma.Core.AspNetCore checkpoint must add the reference or run the same search in its own suite. Scope factory: a tenant missing from the snapshot throws TenantUnavailableException (tenant-not-found); TenantNotFoundException from CreateScopeAsync means only an inactive run-as member.

**Possible contradictions and gaps (unverified leads):**

- Table-type grants break fresh databases: spec 0001's generator emits an unguarded 'GRANT EXECUTE ON TYPE::… TO [tellma_app]' (src/core/Tellma.Core.EntityFrameworkCore/TableTypes/TableTypesSqlServerMigrationsSqlGenerator.cs AppendGrants). But §6.1 says a database 'dotnet ef database update' creates 'has no tellma_app', and §10's catalog fixture creates the catalog by Migrate(). Both fail with Msg 15151 at TenantMembershipList's migration unless the role exists first. Fixtures and design-time DBs must create the role, or the grant must be guarded.
- §3.5 says 'AddTellma sets tellma_app as the default principal set' for spec 0001's grants. Spec 0001 has no model-level default-principal API, only per-type HasGrants. The conventions must stamp HasGrants('tellma_app') on every type.
- §6.5 says OnModelCreating 'calls UseTableTypes(...)'. In the library, UseTableTypes is a DbContextOptionsBuilder extension, and it requires a sweepScope string. Neither 0011 nor 0012 names the sweep scope for CatalogDbContext or TellmaDbContext.
- §1.2/§11 put the Client esproj in the DoD and Tellma.slnx. Its build runs 'pnpm run build:esproj' -> 'tellma-client build' (spec 0024 §2.6), which does not exist, and ci.yml's build-test job has no Node/pnpm. §6.6 step 1 and §1.2 assume a repo-root pnpm workspace, but the workspace is currently client/pnpm-workspace.yaml. Recommend deferring the esproj and SPA proxy to 0024.
- §2.1's FeatureContributionItem catalog and FeatureContribution sugar name types owned by 0013–0035: SecurableRegistryBuilder, ValueSetDescriptor, ICalendarSystem, BlobKindPolicy, StackOperations, IEntityValidator, ReportSeed, ReportKind, SecurableRef, NotificationTypeDescriptor and others. §2.2 says 'Tellma.Core ships every realizer of §2.1', and §2.1 says an item with no realizer is a composition problem. The contract therefore cannot be shipped whole by 0011; each owner must add its item, sugar and realizer.
- The tenant model's leaf input is FeatureContribution.Entity<T>(), i.e. StackContributionItem(Type, Type?, StackOperations?) with 0015 §2.1's StackOperations. Building TellmaDbContext (§3.5, §6.5) therefore needs a 0015 type before 0015 exists.
- Several pieces need contract types pulled forward from later specs. The exceptions derive from 0015 §14.1's TellmaException (§3.7, §4.6). The guard raises 0015's IndividualRequiredException (§4.3). RequestContext carries 0013's TenantSettings and ICalendarSystem (§4.1). The audit requires 0014's SecurableEndpointMetadata/MemberEndpointMetadata (§5.3). CSRF rule 3 reads 0016's ApiOptions.ClientNames (§5.4). ITellmaLinks.Target needs 0015's IStackRegistry (§5.10).
- §4.3's hub front door needs 0014's IGuardedBatchRunner, a 0012 prologue-only batch, MapHub and 0021 §6.1's claims. §10 also asks for the verdict matrix 'on the Hub group's negotiate'. Neither is buildable or testable within 0011; both belong with 0021.
- §5.1's UseTellma installs middleware that 0016 owns: problem-details mapping, rate limiter, timeouts, output caching, compression and TellmaContractMiddleware. Until 0016 lands, 0011's web tests asserting 404 tenant-not-found, 403 tenant-* and 503 catalog-unavailable with Retry-After need an interim mapping.
- §10/§11's Acme integration rows depend on other specs, so 0011's DoD is unreachable in isolation: core.settings step (0013), bootstrap administrator and UserId binding (0014), mutations and AllowWhenReadOnly (0015/0016), core.blob-container (0017), invite (0018) and RunsWhenReadOnly jobs (0020).
- §9 says HostTelemetryNames lives in Tellma.Core.Abstractions.Tenancy. Spec 0012 §12.1 says adapter packages declare their own holders ('Tellma.Core.AspNetCore'). The placement needs one answer.
- §9 says 'The existing instrument-name test cross-checks' the alert queries, but only an email-scoped AlertQueryTests exists, and 0011 names no alert query of its own.
- §10 requires Category=Integration on Windows LocalDB on every PR; ci.yml runs integration on ubuntu only.
- §1.1 pins conflict with Directory.Packages.props: EF 10.0.9, SqlClient 6.1.1 ('kept on the version EF resolves to') and Testcontainers 4.13.0. ASP.NET packages would stay on 10.0.9 while KeyPerFile is 10.0.11, a mixed servicing line.
- §3.4 fixes the first registry load at a 10-second bound with no option, which slows the readiness tests.
- (db.query.text removal verified by PC-032: EnrichWithSqlCommand runs after the tags are set; SqlClient 1.16 replaces literals with '?'. The span-name and tellma.db.role parts stay open.) §5.1/§3.5: removing db.query.text, naming the SQL span per 0012 §6.1 and tagging tellma.db.role on raw catalog SqlCommands all assume the SqlClient instrumentation's enrich hook exposes the command. This needs verifying on OpenTelemetry.Instrumentation.SqlClient 1.16 with SqlClient 6.1.6; otherwise the executor must own its own Activity.
- DatabaseCollations is claimed both by §1.1/§6.7 and by 0010 §A.11's placement table; whichever lands first must create it.
- §2.3 core.taxonomy reads taxonomy.json 'from the repository root when present' without saying how a running host finds the root (walk up from the content root?).

**Slow or heavy tests (planning estimates):**

- §10 Tellma.Core.IntegrationTests: Testcontainers mssql 2022 start (~20–40 s once per run on Linux) plus a catalog DB created by Migrate() per test class (~1–3 s each); whole suite ~1–2 min. Keep on PR (Category=Integration).
- §10 concurrent-transition and two-refresher CAS tests: timing-sensitive concurrency; seconds each. Keep, but synchronise with barriers instead of sleeps to avoid flakiness.
- §10 Acme IntegrationTests: repeated migrate+provision from an empty server, in-proc identity host start, full OIDC sign-in, two WebApplicationFactory hosts for suspension propagation, back-channel logout end to end; plausibly 3–6 min. Keep on PR, but override RefreshInterval to ~1 s in tests and share one server per collection; watch ci.yml's 25-minute integration job budget.
- §3.4/§10 'readiness before and after catalog availability': the first registry load has a fixed 10-second bound, so this test waits at least 10 s unless the bound is made an internal option; keep, make the bound injectable.
- §10 Acme.Web.Tests 'emit-client-assets exits 0 with no database reachable': as an out-of-process dotnet run it costs 20–40 s; call TellmaMigrator.RunAsync in-process instead. Keep.
- §10 Tellma.Distro.Acme.E2E: Playwright scaffold, empty; would need a browser install in CI if it runs. Defer running it until the UI specs ship.

**External dependencies:**

- SQL Server: LocalDB on Windows (prefer 2022/v16), Docker + mcr.microsoft.com/mssql/server:2022-latest on Linux; principal needs CREATE ANY DATABASE, sp_getapplock, KILL not needed; Azure SQL branch (EngineEdition 5, CREATE USER FROM EXTERNAL PROVIDER, CATALOG_COLLATION) is only unit-tested by SQL text
- GitHub Actions: a Windows integration job (LocalDB) is required by §10 but absent from .github/workflows/ci.yml
- NuGet (new or bumped): Microsoft.EntityFrameworkCore.* 10.0.11, Microsoft.Data.SqlClient 6.1.6, Testcontainers.MsSql 4.14.0, Microsoft.AspNetCore.Authentication.OpenIdConnect, Microsoft.AspNetCore.Authentication.JwtBearer, Microsoft.AspNetCore.SpaProxy (10.0.9 line), Microsoft.Extensions.Configuration.KeyPerFile 10.0.11, Azure.Extensions.AspNetCore.Configuration.Secrets 1.5.2; existing Serilog.AspNetCore, OpenTelemetry.* 1.16, Azure.Monitor.OpenTelemetry.AspNetCore, Azure.Extensions.AspNetCore.DataProtection.Blobs/Keys, Microsoft.AspNetCore.Mvc.Testing, Microsoft.Extensions.TimeProvider.Testing
- Microsoft.VisualStudio.JavaScript.Sdk (esproj MSBuild SDK) plus Node and pnpm for the Client esproj; spec 0024's tellma-client tool — none exist yet
- ASP.NET Core HTTPS dev certificate (dotnet dev-certs) for https://localhost and the __Host- cookie
- ICU on the host (core.globalization check; InvariantGlobalization must stay off; alpine images need icu-libs)
- Azure (deployment only, not tests): Blob storage + Key Vault key for the Data Protection ring, Key Vault references/AddAzureKeyVault, managed identity for SQL, App Service settings (Always On, WEBSITES_CONTAINER_STOP_TIME_LIMIT=30), Application Insights connection string
- Identity: in-proc engine signing keys for Development; Tellma:Identity:ClientSecret/ServiceClientSecret outside Development
- (from PC-025) Front doors (tenant filter, MCP filter, the worker after CreateScopeAsync) must set CultureInfo.CurrentCulture/CurrentUICulture from RequestContext.CultureInfo after EnsureAccessAsync/CreateScopeAsync returns: an initializer's AsyncLocal change does not flow back (spec 0013 §9.3 step 5 cannot work as written). The guard meters only the outcomes it decides; the tenant middleware meters pre-binding outcomes and scopes{kind=request} and needs a seam into the internal TenancyMetrics. ITellmaLinks.Target reads stacks through the internal IStackRouteSource (default NoStackRoutes); the stack-registry checkpoint replaces it. AddTellma registers ISandboxContext → TenantSandboxContext (scoped; throws outside a tenant): in-process identity needs its own answer for tenantless mail and must not override host-wide with Never.

- PC-032 → PC-034: ITenantProvisioningTrigger and NotConfiguredProvisioningTrigger (registered with TryAdd, answers 501) exist already; replace the registration, do not re-declare the types.
- PC-032 → PC-085: Azure Monitor exporter and webhook endpoint stamping were deferred here.
- PC-032 → PC-081: the SPA catch-all fallback turns a 405 into a 404; revisit with the SPA host.

- PC-032 → the spec 0016 MCP endpoint checkpoint: the web-host audit refuses a tenant endpoint without a guard marker except Hub and Mcp surfaces; `/{tenantId}/mcp` must stamp the internal TenantFrontDoorMetadata(Mcp) beside AddEndpointFilter<TenantAccessFilter> (Tellma.Core.Mcp has InternalsVisibleTo), or the audit fails startup.
- PC-032 → PC-063: IStackRouteSource gained `Routes`; the stack registry must implement it (the audit checks stack segments against reserved route words).

- PC-032 → the realtime/MCP checkpoints: the Hub route group requires SignalR's HubMetadata, so hubs are mapped with MapHub only; anything else on Hub fails the startup audit.

- PC-034 → PC-038: implement the internal Tellma.Core.Provisioning.ITenantVersionTagSeeder (SeedAsync after the fingerprint row and grants, before any step; Suspended tenants stop right after it) and register it; the migrator skips the seed while none is registered.
- PC-034 → PC-077: implement the internal IProvisioningFrameOpener (opens the step's write frame in its tenant scope, once per step) and register it; ProvisioningStepRunner runs steps frameless while none is registered.
- PC-034 → PC-077: enlist the dbo.__TellmaProvisioning record write (ProvisioningRecords.RecordAsync) in the step's Provisioning frame, so a step's work and its completion record commit together; until then the record is written after the step commits and a crash in between re-runs the (idempotent) step.
- PC-034 → PC-078: implement the internal ITenantDriftReport (the drift scan after the steps on every migrate) and register it; the order-10 administrator bootstrap step reads TenantProvisioningContext.AdminEmail/AdminSubject.
- PC-034 → PC-088/PC-093: implement the internal ITenantAdministratorInviter (called by provision after the steps and the membership hint, before SetState Active, whenever the run names an --admin-email; the inviter decides what a Development subject needs) over the invite action; provision skips the invitation while none is registered.
- PC-034 → PC-049: the settings provisioning step reads a seed tenant's Languages and Calendars from Tellma:Seed:Tenants (the migrator's SeedTenant binding: Id, Name, Category, LiveTenantId, Languages, Calendars); the migrator passes them nowhere else.
- PC-034 → PC-085: the Acme migrator is `TellmaMigrator.RunAsync(args, "acme", AcmeComposition.Compose, <identity store delegate>)` plus a factory deriving TellmaDesignTimeDbContextFactory<TellmaDbContext> overriding Slug; test/core/assets/Tellma.Core.Migrator.TestDistribution is the miniature to copy. The in-process emit test can call TellmaMigrator.RunAsync(["emit-client-assets", dir], …).
- PC-034 → PC-094: emit-client-assets writes `tellma-metadata.ts` (imports tmMetadataLoaders from '@tellma/core', exports tellmaMetadata with 'tm-metadata/<chunk>-<hash12>.json' loaders and `strings: {}`) and `assets/{core,entities,contracts}-<Crockford hash12>.json`; it sweeps stale emitted files, so string packs must follow `strings-<lang>-<hash12>.json` to be swept. A contributor may not write the module path, anything under assets/, or a Core section named formatVersion.
- PC-034 → every checkpoint that adds or changes a core-feature table: regenerate the TestDistribution's Initial migration (its README has the command), or Tellma.Core.IntegrationTests' migrator suite fails on pending model changes.

## 0012
- (PC-028) PC-040 (distribution-text analyzer): refuse `RAISERROR` at severity 11 or above in batch text, and require `THROW`. Under `XACT_ABORT ON`, `RAISERROR` does not abort the batch: the text runs on to its `COMMIT` while the client receives the error. The executor now treats an out-of-band number in a batch that commits its own transaction as a defect (probe, else `DataAccessAmbiguousException(Unknown)`). A `RAISERROR` that raises a distribution-band number through `sys.messages` would still surface as `BatchAssertionFailedException` after its batch committed, so only the analyzer closes the gap.
- (PC-028) Spec 0020 worker checkpoint: a job handler whose sent `Maintenance` write is cancelled at host stop gets `DataAccessAmbiguousException(Unknown)`, not `OperationCanceledException`, because the write may have committed (D-052). Map any exception thrown after the worker's stopping token fired to `Released`. Test the stopping token, not the exception or its inner exception: the inner exception can be SqlClient's cancelled-command `SqlException`. An `Explicit` batch cancelled before its commit still surfaces `OperationCanceledException`.

**Possible contradictions and gaps (unverified leads):**

- Grants on fresh databases: §13.2's fixture is created by Migrate(), and its [TableType] entities' migrations emit spec 0001's unguarded 'GRANT EXECUTE ON TYPE … TO [tellma_app]'. On a fresh LocalDB or container database that fails with Msg 15151 unless the fixture creates tellma_app first. The spec is silent; the same applies to 0011's design-time databases.
- §1.3 lists 'UseTableTypes()' with no argument, but the library's UseTableTypes requires a sweepScope string. The scopes for TellmaDbContext and TellmaFixtureDbContext are unspecified.
- §6.1 cancels reads with HttpContext.RequestAborted. The executor lives in Tellma.Core, which has no ASP.NET reference, and 0011 §4.2 bans IHttpContextAccessor in Core services. No contract says how the scoped executor obtains the request token (e.g., a scoped cancellation holder that TenantMiddleware sets).
- IDataBatch (§5.2) and the persist frame (§5.5 step 4: '@tm_expectedTags : VersionTagList', 'always permissions and settings') compile against 0013 types. §4.4 nonetheless says VersionTagList is 'defined by the spec that names it' (0013), so 0012's executor cannot be built before those contracts exist. Likewise ITenantDatabase.Context/Schema read 0013's TenantSettings.SqlServerTimeZoneName/Shape (§5.1).
- §13.2's fixture 'composes spec 0013's model contribution and spec 0014's access contribution' plus 0017's core.Blobs in full, and step 5 and AuditConvention target core.Users. The integration tier therefore cannot run before 0013/0014/0017 unless stand-ins are created. The spec allows a stand-in only for core.Jobs.
- Two attributes named TableTypeAttribute and two named ExcludeFromTableTypeAttribute exist: the library's in Tellma.Core.EntityFrameworkCore.TableTypes and 0012's in Tellma.Core.Abstractions.Entities (0010 §G.1 confirms both). Any file importing both namespaces, TableTypeConvention included, hits CS0104 and needs aliases.
- §Context/§4.4 say the bulk lists are 'plain [TableType] classes', but src/core/Tellma.Core.Abstractions/TableTypes/BulkLists.cs carries no attribute. Adding it touches spec-0001 code and its documented HasTableType registration path.
- §1.1 ships the analyzers as an analyzer asset of Tellma.Core.Abstractions. That silently extends TELLMA0001–0006 to Tellma.Identity, the connectors, Email, Webhooks and Testing.Support. Analyzer dependencies (ScriptDom) are not resolved from NuGet and must be packed beside the analyzer. The CI design-e2e package flow packs Abstractions alone and would break once Abstractions references Queryex.
- §2.5/§13.1 test IDisplayNames against 'display-name-golden.json' and apply 'spec 0029 §5.2's rule'. Both the file and the rule are owned by a client spec, 0029, scheduled far later.
- §13.3 asserts 'the migrator keeps exactly two rows across three migrations', which requires spec 0011's migrator (§6.1); 0012's integration tier depends on 0011-d.
- §13.1/§14 require the integration suite on LocalDB and Testcontainers on every PR, but ci.yml runs Category=Integration on ubuntu only. No fault-injection harness exists for 'connection-kill'.
- §11.1 declares CalendarMap('uq', '[core].[CalendarMonths]') unconditionally. Until spec 0013 creates that table, any 'uq' calendar function compiles but fails at run time; the fixture schema does not include it.
- §1.3 pins UseCompatibilityLevel(160) (SQL Server 2022 semantics). Developer LocalDB instances are often 2019 (max compat 150). This is harmless for EF's own SQL generation but means the dev tier may not match production semantics; specify LocalDB 2022.
- §12.1 says the adapter packages declare their own TelemetryNames holders, but spec 0011 §9 places HostTelemetryNames (meter Tellma.Core.AspNetCore) in Tellma.Core.Abstractions.Tenancy.

**Slow or heavy tests (planning estimates):**

- §13.3 'Executor' + §14 'full retry and probe matrix under connection-kill fault injection': KILL of the session mid-batch, deadlock-victim induction across two connections, the commit probe waiting on an uncommitted insert, and THROW 50503 retried three times with 50/200/800 ms jittered delays (~1 s minimum). Plausibly 30–90 s and flake-prone. Keep on PR in a serial xUnit collection with explicit synchronisation (no sleeps). Add a trait such as Fault=true so it can be quarantined without dropping the tier. Needs ALTER ANY CONNECTION (sa / LocalDB owner).
- §5.6/§13.3 budgets 'Import, per chunk of at most 10,000 roots' and §8.2 'OPTION (RECOMPILE) above 1,000 rows': 10,000-row saves with children on LocalDB/containers, ~5–20 s. Keep.
- §13.3 'Skip/Take reuse one plan (two pages, one sys.dm_exec_query_stats entry)': needs VIEW SERVER STATE and is sensitive to plan-cache churn from parallel tests. Keep, run serially and identify the plan by query hash.
- §13.2 change tracking on every fixture table with a CHANGETABLE audit after every batch in test mode: adds overhead (~10–30%) across the whole integration tier. Keep (spec-mandated).
- Whole Tellma.Core.IntegrationTests/Data tier: container start plus fixture migrations, likely 2–5 min. Keep on PR, sharing one database per collection. Combined with 0011's suites it pressures ci.yml's 25-minute integration job.
- §13.1 analyzer tests via Microsoft.CodeAnalysis.Testing: each case compiles a snippet; ~30–60 s total. Keep on PR.
- No benchmark or perf test is mandated. Review flags 5 and 14 mention measurements (scoped recount vs whole table above ~100k rows; bigint id-path size at depth 32) as decision triggers, not tests. Defer.

**External dependencies:**

- SQL Server 2019+ (2022 recommended to match UseCompatibilityLevel(160)): hierarchyid, system-versioned temporal tables, STRING_AGG, change tracking (fixture), sp_sequence_get_range, sys.dm_exec_query_stats (VIEW SERVER STATE), KILL for fault injection (ALTER ANY CONNECTION); LocalDB on Windows, Testcontainers mssql 2022 on Linux; TELLMA_TEST_SQL override
- NuGet: Microsoft.EntityFrameworkCore.SqlServer.HierarchyId 10.0.11, Microsoft.Data.SqlClient 6.1.6, Microsoft.SqlServer.TransactSql.ScriptDom 170.x (verify it loads in a netstandard2.0 analyzer host in VS and dotnet build, and pack its DLL beside the analyzer), Microsoft.CodeAnalysis.CSharp.Analyzer.Testing (or the Microsoft.CodeAnalysis.Testing family) — new CPM pins; Roslyn stays at the 5.0.0 pin EF Design requires
- CI: a Windows LocalDB integration job (spec requires LocalDB and Testcontainers per PR); the design-e2e package flow must pack Tellma.Core.Queryex and the analyzer

- PC-027 → PC-040 (TELLMA0003): refuse a bare `nameof(...)` hole in IDataBatch.Sql; a column or object name is written `SqlIdentifier(nameof(...))` (ruled at PC-027 review 1; the analyzer did not exist yet).
- PC-027 → the 0012 error-taxonomy checkpoint: cancelling a batch on a client-side failure helps only while the server still runs it; a small writing batch can commit before the client sees the failing result set, so a defect raised after send must be classified as ambiguous (may have committed) for a writing batch.

- PC-012 → spec 0012's batch composers (Count, Assert, key capture; still throwing after PC-028, so whichever checkpoint implements them): a CompiledQuery with `Recompile` set must get RECOMPILE merged into the composer's single OPTION clause (e.g. `OPTION (RECOMPILE, MAXRECURSION 32)`); Assert counts into a variable first, since IF takes no OPTION (D-050). The spec 0012 guards that embed Body under IF (spec text around lines 1700, 1717 and 1722 of docs/specs/0012) must assign the result to a variable under the hint and then test it, or a flagged filter runs on a cached plan.

- PC-027 → the owner of TransactionMode.Explicit's callers: spec 0012 says Explicit exists so a decision is taken in C# before commit, but the executor commits straight after reading and exposes no hook; the contract for that hook is unspecified (PC-027 review 2, R2-4).
- PC-027 → PC-028 (error mapping): StatementOrdinalAt maps SqlException.LineNumber alone; for an error raised in a trigger or called procedure (SqlError.Procedure set) the line counts within that module, so such errors must not be mapped to a batch statement.

- PC-028 → PC-038: IVersionTagRegistry, VersionTagEffects and UserVersionTagRule are declared in Tellma.Core.Abstractions.Caching; PC-038 implements and registers them, never re-declares them. PC-028 already emits the version-tag guard text (PersistChecks): PC-038 reuses it rather than re-implementing it. DataBatch.TagParameter() binds @tm_tag for the bump epilogue.
- PC-028 → PC-038/PC-050 (connect prologue): the executor emits the version-tag guard and the 50401 caller re-check from the GuardPremises passed to DataBatch.MarkConnectPrologue(GuardPremises); the prologue must declare @tm_UserId and, for a user, bind @tm_ExpectedUserPermissionsTag (ReservedSqlNames.ExpectedUserPermissionsTag).
- PC-028 → PC-037 (save emitter): call DataBatch.DeclareInsertedRoots(table, ids) (int and long overloads; the first non-empty call wins) so the commit probe can verify; statements that throw 50409/50404 read their preceding set with cursor.NextIfAsync plus ReadConflictsAsync/ReadMissingIdsAsync; StatementDraft.EntityType names the entity of tree statements. Persist re-checks run in every transaction mode; a TransactionHead statement is refused when the text opens no transaction.

## 0013
- (PC-025) `RequestContext.ApplyCultures()` sets CurrentCulture/CurrentUICulture from the bound context; front doors and the spec 0020 worker call it in their own method after EnsureAccessAsync / CreateScopeAsync returns. PC-049 must use it, not set cultures inside an initializer.

**Possible contradictions and gaps (unverified leads):**

- Ordering trap, not a contradiction: 0011 §4.1's RequestContext names ICalendarSystem and TenantSettings, and 0012 §5.1's IDataBatch sketch names VersionTagDependency, CacheResult<CachedEntitySet<T>> — both owned by 0013 §2.2/§3.1/§4.3/§5.6/§8.1. The 0013 Abstractions contracts (checkpoint 0013-a) must land before the 0011 request-context and 0012 IDataBatch checkpoints, or those checkpoints must create the types under 0013's names
- §1.3 requires 'the Tellma.Core satellite assembly of every declared non-English code', and the reference distribution declares ['en-GB','ar-SA'] (0011 §1.3), yet §10 never states which satellites Core ships or where translations come from; an implementer must author a full Strings.ar.resx (plus Arabic Region_ names) with no source
- §10.1 Region_<code> 'generated from CLDR … refreshed with the catalogue' names no generator, data version or location; the §12 resource audit treats Region_ keys as convention-derived, so the generator's output set must exactly equal the core.Region value set (§7.7: union of every catalogued language's regions)
- §11/§13: 'Alert queries live under infra/monitoring/ and are cross-checked by the existing name test' — 0013 names no alert query, and the only existing name test is email-scoped (Tellma.Core.Email.Tests/Observability/AlertQueryTests.cs); the DoD item 'alert queries name only instruments that exist' has nothing concrete to check unless queries are invented
- §5.7 seeded tenants 'that list Languages or Calendars get them', but 0011 §6.2's TenantProvisioningContext(Tenant, Services, IsNew, AdminSubject, AdminEmail) carries neither; the step must read Tellma:Seed:Tenants itself by tenant id — unspecified
- §5.7 says the provisioning step 'completes the placeholder through the pipeline', while 0011 §6.2 says steps enlist their writes in the step runner's Provisioning frame (IOpenWriteHost, 0015 §13.3) and §5.1 says Settings 'is not contributed as a stack'; whether a non-stack entity can be enlisted, or the step must issue its own IDataBatch.Save<Settings> in a System-scope Persist batch, is not stated
- §9.3 step 1 has the Order-200 initializer read PreferredLanguage/Calendar/TimeZone/Region 'through the preferences cache', but RequestContext (0011 §4.1) carries no profile and 0014's IUserConnector exposes no current ConnectedUser; a scoped accessor (or reading the preferences VersionedCache by (TenantId, UserId) with the user snapshot's PreferencesTag) must be invented
- §2.5: BatchOutcome.UserVersionTags must be null for 'an EvaluateFor load of spec 0014 §5.1', yet that load does carry a connect prologue (ForUser variant), as does a job scope's ForUser batch whose snapshot should be replaced; the executor needs an explicit flag distinguishing the two ForUser uses — not specified in 0013, 0014 §5.1 or 0012 §5.3
- §2.5 guard: the UserStamps arm takes UPDLOCK 'when the batch writes that row', but the stated test is table-based ('core.UserStamps or a table with a Preferences rule among its written tables'); since User carries a Preferences rule, every administrative save of another user's row takes UPDLOCK on the caller's own stamp row. Harmless (0014's application lock already serialises those writes) but the rule as written is broader than its stated intent
- §12 integration bullets 'the runner re-runs once', 'membership save bumps only that user's PermissionsTag' (through a user save) and the Rerun/Refresh policy cases require 0014's IGuardedBatchRunner/prologue; §12 'Wire tags' DTO snapshot includes 0016's MeResult. These tests cannot be green at 0013's own checkpoints and must be owned by 0014-d and 0016 respectively
- §5.3 References rule needs 0015's IStackRegistry at startup and §6.2 Related needs StackDescriptor.RelatedSelect; 0013-f can only validate References once 0015 §2 exists (stub or defer the rule)
- Minor: §1.2 lists ApiService<SettingsService>() and Securables(core.Settings.General × Read|Save); 0014 §4.2 says ApiService registers each [ApiAction] securable with FilterRoot=null and §5.5 registers the same pair through Securables — idempotent per 0014 §4.1 'same tuple with same filter root', so consistent, but the two registrations must agree on FilterRoot=null

**Slow or heavy tests (planning estimates):**

- §12 Integration 'Guard and policies' — REPEATABLEREAD wait, UPDLOCK serialisation of two writers, two self-writes on one stamp row, two concurrent refresh-caches, tag-then-rows ordering under a concurrent bump: multi-connection SQL concurrency, ~1-5 s each, flaky if sleep-based; keep in the PR tier but implement with explicit barriers (second connection holding an open transaction) and a dedicated xUnit collection; consider Trait 'Concurrency' for selective reruns
- §12 'a settings bump from a second instance' — two service providers on one DB; ~2-5 s; keep
- §12 Migrator bullet — seed on every migrate, wholesale bump only after an applying run, new registry name gets its row: requires running the migrator against a freshly created database (LocalDB ~10-30 s; Linux Testcontainers adds ~15-30 s container start, shared); keep but share one database per collection; gate behind Trait if CI time is tight
- §12 'Isolation: two tenant databases sharing user ids' — needs a second migrated tenant DB, ~10-30 s; keep with a shared two-database fixture (same fixture as 0014's isolation test)
- §12 'Write audit' — change tracking on every fixture table with a CHANGETABLE comparison after every batch: adds per-batch overhead to the whole integration suite (0012 owns the mechanism; implement once)
- §12 'Round trips' via SqlConnection.RetrieveStatistics — fast; keep
- §12 'VersionedCache one loader under 64 concurrent callers' — milliseconds; keep
- §12 Catalogue invariants over 243 culture pairs on Ubuntu and Windows — <1 s, but ICU-version sensitive; keep, run on both OS legs
- §12 core.CalendarMonths seed equality (2,196 rows) and calendar goldens — <1 s; keep
- §12 Resource audit scanning string literals under src/ — 1-5 s file scan, grows with the repo; keep, but make the src-root discovery robust (walk up to Tellma.slnx)

**External dependencies:**

- SQL Server (LocalDB on Windows, Testcontainers.MsSql on Linux; TELLMA_TEST_SQL override): system-versioned temporal tables, table-valued parameters, REPEATABLEREAD/UPDLOCK/FORCESEEK hints, change tracking (CHANGETABLE), read-committed snapshot, sys.time_zone_info / AT TIME ZONE
- ICU on every host (spec 0011 core.globalization check): libicu on Linux CI, Windows ICU (icu.dll) on Windows; the frozen 243 language-region pairs and the Gregorian-OptionalCalendars assertion are ICU-version sensitive
- tzdata on Linux images (TimeZoneInfo.TryFindSystemTimeZoneById for the core.time-zones warning; IANA->Windows conversion TryConvertIanaIdToWindowsId)
- New central NuGet versions in Directory.Packages.props: Microsoft.Extensions.Caching.Memory, Microsoft.Extensions.Localization (+ .Abstractions), Microsoft.AspNetCore.DataProtection.Abstractions (and DataProtection for tests); MessageFormat 8.0.0 already pinned; Microsoft.Extensions.Diagnostics.Testing and TimeProvider.Testing already pinned
- CLDR data for Region_<code> display names in each shipped language and for 'likely region' defaults — no source named; practical options: Node's Intl.DisplayNames from the client workspace toolchain, or unicode-org cldr-json
- Arabic translations of every Core Strings key for Strings.ar.resx (the reference distribution declares ar-SA; §1.3 fails startup without the ar satellite) — content authorship, no source specified
- Data Protection key ring for Secret settings in hosts that read them (ephemeral provider suffices in tests)
- (from PC-025, for PC-049 and every front door) An IRequestContextInitializer cannot set CultureInfo.CurrentCulture/CurrentUICulture for the request (§9.3 step 5): it runs inside an async call and the AsyncLocal change does not flow back. The front door sets both from RequestContext.CultureInfo after EnsureAccessAsync/CreateScopeAsync returns.

## 0014

**Possible contradictions and gaps (unverified leads):**

- §1.2 has CoreFeature register contribution.Entity<User, UserService<User>>() and Entity<Role, RoleService<Role>>() while §1.2 itself says 'Spec 0018 supplies the services'; 0014's composition cannot compile without 0018's types. These two lines must land with 0018 (or 0014 registers a temporary default service), and until then the User/Role stacks, their securables from 0015's StackSecurableContributor, and 'contribution.Validator<User, UserAccessRules<User>>()' have no stack to attach to
- §3.1 User carries [Stack(Operations = All | Enlist)] (0015), [ApiResource] (0016) and [BlobReference(...)] with BlobPreset/BlobReadAccess (0017) plus FKs to core.Blobs; these attribute types and the core.Blobs model must exist in Abstractions/model before 0014-a. 0012 §2.2 lists them as declared by 0015/0016/0017, so a strictly spec-ordered build cannot compile 0014-a; an early annotations checkpoint (or declaring the types in 0012) is needed
- §1.2 composition list omits services the spec requires: IPersistEffect<User> (companion insert, §8.2), IPermissionDriftScanner (§9), ITenantBootstrapper (§11), IAdministratorDirectory (§10), the core.UserStamp × Read securable (§4.2) and the class of provisioning step 10 (0011 §6.2 says the step 'calls spec 0014's ITenantBootstrapper' without naming the owner of the step class)
- §4.3 Core's default sensitive set names core.Schedule × Save|Delete|Activate (spec 0020's resource). §4.1/§4.5 do not say whether MarkSensitive/default sensitivity on a pair absent from the registry is ignored or a composition problem; a composition without 0020 (or a build before 0020 lands) has undefined behaviour
- §4.6 layer 4: 'A filter installed by the group asserts … otherwise it replaces the result with 500' — 0014 owns the semantics and the counter, 0016 §5.1 owns the filter order; neither states which spec implements the endpoint filter. Plan it with 0016 and keep 0014 to recording witnesses
- §5.1 EvaluateFor uses ConnectPremises.ForUser with null expectations and says its batch's BatchOutcome.UserVersionTags is null, but 0013 §2.5 says the executor builds UserVersionTagSnapshot 'on a batch that carried a connect prologue', which a ForUser batch does (and a job scope's ForUser batch legitimately should replace the snapshot). The executor needs a premises-level flag to tell the two apart; none is specified
- §7.4/§14: the integration case 'A role save racing a membership edit and a me/locale write' and 'a preference flush and an inbox seen' depend on 0018's me/locale and 0021's inbox/seen; they cannot be green in this spec's checkpoints
- §3.7 lists the role-save rules 1-9 without Permissions.WildcardResourceOnPublicRole (only §3.5 and Appendix A raise it), and §8.4's RoleAccessRules summary also omits it; implement from Appendix A
- §8.3 L1 requires an Invited or Joined individual administrator, but §11's deployed bootstrap creates the first administrator as New (no subject) and only later invites through 0018. Every non-System security write between bootstrap and a successful invite throws Access.LastAdministrator. This is by design for production, but integration fixtures that seed users must create Invited/Joined administrators or every security write fails
- §14 says 'The schema of §3 applied by migrations' without naming the migration set: production migrations live with the reference distribution (0011 §1.2, 0017 'the migrator ships the table … with the reference distribution's migrations'), and test migrations with 0012 §13.2's fixture context. 0014-a should target the fixture context and regenerate the distribution's migrations once 0011's reference distribution exists

**Slow or heavy tests (planning estimates):**

- §14 'The throttled stamp writes once per minute under 1,000 requests' — 1,000 prologue round trips against LocalDB/container, ~3-10 s; keep (could reduce to a few hundred without losing meaning)
- §14 'the join flip exactly once under two concurrent first requests' — two-connection race, <1 s, flaky without a barrier; keep with explicit synchronisation
- §14 'L1-L3 under two concurrent administrator removals (exactly one succeeds; the lock serialises)' — concurrent persist batches with a 5 s application-lock timeout; ~1-6 s; keep; avoid asserting LockTimeout by actually waiting 5 s unless SecurityLockTimeout is configured low in the test
- §14 'A role save racing a membership edit and a me/locale write completes with no deadlock' — three-way concurrency, seconds, needs 0018's me/locale; defer to 0018's checkpoint, then keep under a Concurrency trait
- §14 'Two self-writes of one user (a preference flush and an inbox seen) serialise on the stamp row' — needs 0021's inbox seen for the literal case; implement with two bag writes now; seconds; keep
- §14 persist re-check races ('a role save committed between the two round trips', 'a deactivation committed between them') — need an injection hook between round trips; deterministic once hooked; keep
- §14 'Frozen: reads succeed against a READ_ONLY database' — ALTER DATABASE … SET READ_ONLY WITH ROLLBACK IMMEDIATE requires a dedicated database (~10-30 s to create/migrate); gate behind a trait or run in its own collection, nightly if CI is tight
- §14 'The drift scanner against a database seeded with a filter over a column the next migration renames' — needs a database migrated to an intermediate state then forward (~20-60 s); recommend nightly gate, with the PR tier covering drift through a fake schema in unit tests
- §14 'Two tenant databases sharing user ids and subjects' — second migrated database, ~10-30 s; keep with a shared fixture reused by 0013's isolation test
- §14 'The write-set audit catches an undeclared write' — change tracking overhead on the whole suite (owned by 0012); keep
- §14 'The witness filter replaces a 2xx with 500' — needs a TestServer host with 0011/0016 route groups; moderate; defer to 0016

**External dependencies:**

- SQL Server (LocalDB on Windows, Testcontainers.MsSql on Linux): sp_getapplock with transaction owner, system-versioned temporal tables with a persisted computed column included in an index, filtered unique indexes, BIN2 collation on Subject, change tracking, ALTER DATABASE SET READ_ONLY (Frozen test), read-committed snapshot
- Microsoft.Extensions.Diagnostics.Testing (MetricCollector<T>) and Microsoft.Extensions.TimeProvider.Testing — already pinned centrally
- No identity-server or third-party service: the identity client is spec 0018's and is stubbed (no Live=true suite)
- Spec 0003's fixed Development subject 00000000-0000-0000-0000-000000000001 for the bootstrap's in-proc path
- (from PC-025) The guard and scope factory fail closed when no initializer bound UserId: every guard call fails until the user-connection initializer exists. A reflection test pins which Tellma.Core types take IRequestContextHolder; the runner that re-runs the negotiation initializer must be added to its allow-list knowingly.

- PC-029 → PC-033 (Queryex schema provider): core.UserStamp is exposed read-only to Queryex only through the model annotation "Tellma:Queryex:ReadOnlyEntity" (UserStamp and UserPreference are internal non-entity classes); the provider must honour it (expose core.UserStamp read-only with its User navigation), keep core.UserPreferences out of the schema, and pin the 0014 §3.8 navigation names and the string-typed enum properties.
- PC-029: a distribution leaf that adds a required column with no default breaks Core's seeds; the startup gate reports EF's message.

## 0015

**Possible contradictions and gaps (unverified leads):**

- Cross-spec type ownership blocks compilation of 0015's contracts: ActionContext.Assignments and PersistContext.Assignments are 0028 §3.1's BatchAssignments; Notify/Withdraw take 0021's NotificationRequest/WithdrawRequest; §15 implements 0019's IExcelRowSource/ExcelQuery/ExcelPage/ExportSource; §4.1 OnPersist and §13.1 name 0020's IJobQueue/IJobProgress; §5.1 names 0020 §3.1 RunsWhenReadOnly. The planner must land these contract types contract-first (in the earliest checkpoint) or add the members later (the §17 API golden then churns).
- §2.2 'Child query surfaces → Actions' and 0019 §1.2 say the registry fills Actions with 0019's ChildExportOperations<TChild>, but 0011 §2.1 defines no contribution item for it and 0019 §1.1 says 'the pipeline never depends on the codec'. No seam is specified for how StackRegistry learns the child export companion; the §18.1 pin 'an Exportable surface's two export Actions' cannot pass before 0019.
- §18.2 enlistment pins need 'a reference in each direction over the fixture's foreign keys' (Shipment→Widget and Widget→Shipment) and Shipment with Enlist among its operations, but 0012 §13.2's fixture tables have no such FK columns and §18.2's 'This spec's additions' lists only fixture.Gadgets.
- §18.2 'A job handler's enlisted update appended to the completion batch and rolled back with a lost lease' needs 0020's worker, completion batch and lease fence; not pinnable in 0015's suite — defer to 0020.
- §18.2 'A role save blocking behind a member's in-flight persist' needs a Role save path; 0015's fixture registers only Widget/Node/Shipment/Gadget and RoleService is 0018's.
- §6.1 step 5 / §16.2 'no foreign key value' one-RT create: every TopLevelEntity carries CreatedById/ModifiedById FKs, and §7.4 pre-loads 'every reference whose value changed'. The spec never states that server-owned audit FKs and references to rows inserted in the same payload (temporary ids, e.g. ParentId of a new tree node) are excluded from the Loader.Visible pre-load; without that exclusion no create is ever 1 RT and same-payload references fail Fk.NotFound.
- §11.1 'When neither the service nor a component overrides ValidateActionAsync for the action' — overrides are per method, not per action name; a stack whose service overrides ValidateActionAsync for a custom action cannot be statically known to leave deactivate alone, so the fast path (1 RT) is lost for activate/deactivate on any stack with a custom-action validator. Needs a rule (e.g. any override moves both built-ins to the general path, and the budget test reflects it).
- §10.1 'no IEntityValidator<T> overriding ValidateDeleteAsync' relies on detecting overrides of default interface methods (IEntityValidator.ValidateDeleteAsync 'default: nothing'); implementable via InterfaceMapping but must be stated as the mechanism; same for services overriding protected virtual hooks.
- §5.2 compiles every clause (step 3, InvalidQueryException) before the traversal rule (step 4, 403), while 0016 §5.6 says the walk runs 'after DiscoverQuery and before CompileQuery'. Which wins for a request with both a diagnostic and a restricted path (400 vs 403) differs between the two specs.
- §18.3 conformance base lives in Tellma.Core.Testing, which per §1.1 and ARCHITECTURE.md references only Tellma.Core.Abstractions, yet it must measure DataAccessScope.RoundTrips (0012), read the securable registry, compose a host over a distribution database and run the real pipeline; and 'refusing an undeclared [enlistment pair]' is a compile error by design (IEnlists<T> receiver constraint), so the runtime refusal can only be exercised via reflection/dynamic invocation.
- §17 requires 'an API golden' of three namespaces but no C# API-golden tooling or file location is specified, and the repo has none.
- §5.1 'Replays the recording … every batch-local name a recorded statement holds is unchanged' requires 0012's IDataBatch to allocate batch-local names deterministically across a fresh batch and to support a recording view; 0012 §5.2/§5.4 must expose that, otherwise the re-entry design is unimplementable as written.
- §13.3 run-time refusal 'a statement appended through IDataBatch's public members whose declared writes include a table OwnerOf maps to a stack outside the appender's CallerAuthority' needs an append-time authority hook inside 0012's IDataBatch implementation (0012 §5.2) keyed on a CallerAuthority ambient the pipeline sets — 0012 must provide this seam.
- §3.1/§4.2 EntitiesResult.Ids is IReadOnlyList<long>, while 0016 §3.2 requires ids 'written in the stack's KeyType' (number for int keys) and 0016's long converter writes every long as a string; a key-type-aware converter for EntitiesResult<TEntity> is needed (see 0016 gaps).

**Slow or heavy tests (planning estimates):**

- §18.1 scheduler termination property test: seconds if iteration count bounded; keep on PR with a fixed seed and capped iterations
- §18.2 whole integration suite, 'one database per class' on LocalDB/Testcontainers: ~2-6 s per database create+migrate × ~20-30 classes ≈ 1-3 min, plus Docker container start on Linux (~20-40 s); keep on PR (spec mandates), share one container per run
- §18.2 'unique race from two connections' and 'SQL cycle race rolled back': timing-sensitive concurrency; needs deterministic orchestration (barriers/applocks, not sleeps); keep but write deterministically, retry-flake risk
- §18.2 'Ambiguous commits: the commit probe after a killed connection': requires fault injection in 0012's executor (kill SPID or test hook); seconds; keep, depends on 0012 exposing a test seam
- §18.2 'a role save blocking behind a member's in-flight persist': lock-wait observation, timing-sensitive; keep with explicit lock orchestration or move to 0018 where a Role stack exists
- §18.2 'a denial re-checked after FastDenyWindow' (default 5 s, 0014 §12.1): use FakeTimeProvider (Microsoft.Extensions.TimeProvider.Testing already pinned) — never real sleeps; keep
- §18.2 'a dependent beyond MaxTake', 'a tree beyond MaxTake refused', first-page CountCap, delete-by-query cap (MaxDeleteByQueryRows): with default ceilings each needs >10,000 seeded rows (bulk insert ~2-10 s each); recommend lowering ceilings on fixture stacks via [Stack(MaxTake=…, CountCap=…, MaxDeleteByQueryRows=…)] and keep on PR
- §18.2 'MaxRowsPerSave and a [MaxChildren] cap at depth two': 100,001 / 10,001 in-memory rows refused before any round trip; ~1 s and memory-heavy but no DB; keep (or lower via [Stack]/[MaxChildren] on a fixture)
- §18.2 'Budget: the round-trip budget of §16.2 for every row' and §18.3 conformance base per stack: many DB round trips; ~30-60 s; keep
- §18.2 'Client abort: a save whose client aborts after the persist was sent still commits' and 'revocation mid-stream': timing-sensitive cancellation; keep with deterministic hooks

**External dependencies:**

- SQL Server: LocalDB on Windows, Testcontainers.MsSql (Docker) on Linux; READ_COMMITTED_SNAPSHOT on; change tracking on fixture tables (0012 §13.2 executor audit); hierarchyid; sp_getapplock; TELLMA_TEST_SQL env var convention (0012 §13.1)
- No new runtime NuGet beyond 0011/0012 pins; tests use already-pinned Microsoft.Extensions.Diagnostics.Testing (MetricCollector) and Microsoft.Extensions.TimeProvider.Testing (FakeTimeProvider)
- A C# public-API golden mechanism for §17 does not exist in the repo: either a new pin (e.g. PublicApiGenerator) or a hand-rolled reflection dump
- Optional property-testing library (FsCheck/CsCheck) for the §18.1 scheduler property test, or a hand-rolled randomized harness (Queryex's suites set the precedent)

## 0016

**Possible contradictions and gaps (unverified leads):**

- Ordering trap: Tellma.Core.Abstractions.Api types owned here are consumed by specs that come earlier in the dependency chain — 0012 §2.2's startup gate reads [RelatedSelect]; 0014 §3.1/§3.5 entities carry [ApiResource]; 0014 §14 and 0015 §5.3/§11.2 use TellmaJson.Options; 0015's StackDescriptor carries McpExposure; 0011 §5.1 step 14 places TellmaContractMiddleware and §5.2 cites 'spec 0016's JSON conventions'. 0016-a must land before 0012's metadata gate and 0014.
- §3.2 says EntitiesResult<T>.Ids and the permissions extra's id are 'written in the stack's KeyType' (example "ids": [5]), but §3.4 types Ids as IReadOnlyList<long> and §3.2's own table writes every long as a JSON string. A key-type-aware converter (keyed on TEntity's key) is required but not specified.
- §5.6 says the traversal walk runs 'after DiscoverQuery and before CompileQuery', while 0015 §5.2 compiles all clauses (step 3, 400 query-invalid) before the traversal rule (step 4, 403 restricted-path); the precedence for a request with both faults is ambiguous.
- §11.4 runs EnsureAccessAsync with IsMutation=false for every tool call and relies on 0015 §5.1's persist-time verdict to refuse writes on ReadOnly/Frozen tenants. That covers pipeline persists and open-frame PersistAsync, but not an [ApiAction] that writes through its own IGuardedBatchRunner batch (allowed by 0015 §11.3); on the web the edge's TenantEndpointMetadata(IsMutation) catches it, on MCP nothing does unless tellma_action checks ActionDescriptor.Mutation/AllowWhenReadOnly against the verdict.
- §3.1 requires 'A mapped [JsonIgnore] property that is not [ServerOwned] fails the realised startup gate', but §4.6's audit list (four rules + JsonTypeInfo) omits it and no owner (0012's metadata gate vs 0016's audit) is named.
- §13.3 pins depend on later specs: 'a child surface of a stack with Export answers export' needs 0019's ChildExportOperations; 'With the batch lane full, a reports/run …' and '[CodedReport] action takes the same lanes' need 0033; Blobs and Hub chains need 0017/0021; tellma_get of a core.Role with more than DefaultChildTop permissions needs 0018's Role stack. These pins must move to the owning specs' checkpoints or be stubbed.
- §3.10 ContractsClientAssetContributor implements 0024 §3.2's IClientAssetContributor, a contract owned by a client-foundation spec, and is run by 0011 §6.1's emit-client-assets command; the backend half of 0024 §3.2 must exist first.
- §1.1 says Microsoft.AspNetCore.OpenApi 10.0.11 'moves the repository's Microsoft.AspNetCore.* pins with it', which bumps the frozen identity server's (0003) dependencies from 10.0.9; not called out as an amendment to implemented code.
- §11.1/§11.2 hedge on the SDK ('Should the SDK's default metadata route be unable to express {tenantId}, the document is mapped by hand'); §11.5 form-mode elicitation and §11.1 stateless '2026-07-28 revision' presume SDK 2.2.0 capabilities that must be verified before building 0016-g/h.
- §13.4 records tellma.mcp.result.chars on every PR 'so the character cap … is checked against measured output', but no threshold, artifact or failing condition is defined, so it cannot be a gate.

**Slow or heavy tests (planning estimates):**

- §13.1/§13.2 AspNetCore.IntegrationTests: provisioning three-plus tenants through 0011's migrator (each tenant DB create + full tenant-model migrations + seeds ≈ 10-40 s) and an in-process host; keep on PR but provision once per collection/run and reuse
- §13.3 rate-limit and lane tests (600 req/min per user, 64 concurrent per tenant, 6/24 batch lane): with defaults they would need hundreds of requests; set lowered ApiOptions in the fixture configuration; seconds; keep
- §13.3/§8.3 request timeout 504 (30 s / 5 min defaults) and MCP ToolTimeout (60 s): lower via options in tests; keep
- §9.5 drain (ShutdownTimeout 30 s) named in §13.1's integration list ('health probes and drain'): simulate with a short shutdown timeout or gate behind a trait; otherwise ~30 s per run — recommend gate/shorten
- §13.3 client abort cancelling a read while a sent persist commits: timing-sensitive; keep with deterministic hooks
- §13.3 'tellma.data.roundtrips per operation at or under the budget' across every projected operation: tens of seconds; keep
- §13.2 cross-instance confirmation replay with a second host over the same key ring: two in-process hosts, seconds; keep
- §13.4 Live=true MCP cases against a running identity server: nightly only (already gated by trait, skipped without configuration)
- §13.4 recording tellma.mcp.result.chars per tool over the fixture tenant on every PR: cheap but the consumer/artifact is unspecified; keep as a logged measurement

**External dependencies:**

- New NuGet pins: ModelContextProtocol.AspNetCore 2.2.0 (verify it exists with the stateless 2026-07-28 protocol revision, an HttpContext-aware request filter, a resource-metadata event, form-mode elicitation, Origin validation), Microsoft.AspNetCore.OpenApi 10.0.11; move all Microsoft.AspNetCore.* pins to 10.0.11 (Mvc.Testing, Identity.EntityFrameworkCore, Authentication.Google/MicrosoftAccount currently 10.0.9)
- An MCP SDK client package for the integration suite (likely ModelContextProtocol core/client package at the matching version)
- Microsoft.AspNetCore.Authentication.JwtBearer and OpenIdConnect (pinned by 0011), Data Protection (framework), System.Threading.RateLimiting and response compression/output caching/request timeouts (shared framework)
- SQL Server for integration: LocalDB on Windows, Testcontainers.MsSql + Docker on Linux; the acme reference distribution and its migrator (0011 §1.2)
- Nightly only: a running platform identity server (spec 0003 with 0010 Part A amendments) configured for the Live=true MCP cases
- Already pinned test helpers: Microsoft.AspNetCore.TestHost 10.0.11, Microsoft.Extensions.TimeProvider.Testing, Microsoft.Extensions.Diagnostics.Testing
- (from PC-025) The problem mapper must map TenantUnavailableException's new code tenant-not-found (a background scope refused for a Provisioning/Retired tenant) to 404, and map StepUpRequiredException.

## 0017
- PC-029 → PC-083 (the 0017 blob entity and model): Blob, BlobState and BlobConfiguration (core.Blobs, §2.1 shape) already exist because the fixture's blob stand-in was removed; extend those files, never recreate them.

**Possible contradictions and gaps (unverified leads):**

- 0015 §7.4 validates every changed FK through Loader.Visible<TTarget> and refuses it as Fk.NotFound when the caller holds no Read on the target. 0017 §2.2, however, declares core.Blob with no securable. Read literally, every attach (User.ImageId, Customer.ImageId, Export.FileId written by the export handler) fails with Fk.NotFound. Neither spec exempts [BlobReference] columns from the generic FK validator; the exemption must be added, since the blob validator owns those columns.
- 0012 §3.1 builds BlobReferenceMetadata.Policy 'from the attribute's preset and spec 0017's kind registry', while 0017 §2.4 builds BlobKindRegistry 'from every [BlobReference] property in the realised EF model'. The two are mutually dependent. The implementer must fix the order: registry from the EF model plus transforms first, then the metadata Policy.
- §11 pins 'the sweep claim with a TimeProvider fake', but §7.1's claim SQL compares against SYSUTCDATETIME() and SweepReclaimAfter is applied with DATEADD on the server clock. A TimeProvider fake cannot move the database clock, so the test has to seed ExpiresAt relative to server time.
- §4.4: each [BlobReference] column contributes statements that DECLARE @tb{b}_expected and bind @tb{b}_p0 (that column's kind). Two columns under one ordinal would redeclare the variable. The spec says 'per column … under the effect statement's own ordinal b' but never states that it means one IDataBatch.Sql call per column.
- §1.3/§9: CoreFeature's BlobKind transform for email-attachment (0022 §9.3) and the support-screenshot narrowing (0026 §12.4) name kinds that exist only once OutgoingEmailAttachment/SupportRequest are in the model. §9 fails startup when a transform names an undeclared kind, so each transform must land with its owning spec, not with 0017.
- §11 calls BlobStoreConformanceTests 'abstract' and runs it from both test/core/Tellma.Core.Tests and the Azure adapter's integration project without saying where it lives. Test projects should not reference each other, so put it in src/core/Tellma.Core.Testing (the precedent is 0015 §18.3).
- §12 says 'the three alert queries checked by the existing monitoring test', but the existing AlertQueryTests is email-scoped (it reflects over Tellma.Core.Email constants and links only infra/monitoring/email). A new blob alert-query test is required.
- 0011 §1.3 binds AddAzureBlobStore from Tellma:Blobs:Azure 'with the host's TokenCredential', but no spec says which credential UseAzureDefaults constructs (for example, a ManagedIdentityCredential with an optional client id), while 0017 §6.3 forbids the adapter from constructing one.
- ARCHITECTURE.md (Local development, shared infrastructure) lists the Azurite emulator as per-machine dev infrastructure, while 0018 §6.2 runs Development on the file-system store with no Azure section. This minor misalignment should be noted when ARCHITECTURE.md is updated.

**Slow or heavy tests (planning estimates):**

- §11 Azurite conformance via Testcontainers.Azurite: first image pull 30-60 s, container start 5-10 s, suite ~10-20 s. Keep on the Linux PR runner and skip on a Windows runner without Docker, as the spec states.
- §11 RCSI concurrency test (one transaction holds a confirmed row lock while the sweep claim blocks on a second connection): ~2-5 s and timing-sensitive. Keep it, but detect the block by polling sys.dm_exec_requests (LCK_* wait) rather than with fixed sleeps.
- §11 distributions/acme blob end-to-end (web host + LocalDB/Testcontainers + file-system store): ~30-60 s of shared fixture start. Keep under Category=Integration and share the host across the class.
- §11 Imaging suite including the PDFium render: under 10 s, but it loads native binaries on both OSes. Keep.
- §11 'oversize body 413 before the body is read': send only a large Content-Length header and never stream 100 MiB. Keep.
- §3.6 thumbnail timeout (5 s) is not mandated by §11. If it gets a test, inject a tiny timeout instead of waiting 5 s.

**External dependencies:**

- NuGet: SkiaSharp 4.152.1, SkiaSharp.NativeAssets.Linux.NoDependencies 4.152.1, PDFtoImage 5.4.0 (ships PDFium natives for win-x64/linux-x64), Azure.Storage.Blobs 12.29.2, Testcontainers.Azurite 4.14.0 with Testcontainers 4.14.0. Check that each version resolves on nuget.org before the checkpoint: the spec pinned them in 2026-09 and they have not been verified here.
- Azure.Core TokenCredential (Azure.Core 1.61.0 already pinned; check Azure.Storage.Blobs 12.29.2's minimum) and Azure.Identity 1.21.0 (pinned) for the ManagedIdentityCredential that Tellma.Defaults.Azure must build.
- Docker on the Linux CI runner with the Azurite image (mcr.microsoft.com/azure-storage/azurite).
- SQL Server features: sequences, filtered unique indexes, OUTPUT … INTO table variables, updatable TOP CTE, RCSI (LocalDB on Windows, Testcontainers.MsSql on Linux).
- Binary test fixtures that must be authored or generated: JPEG with an EXIF orientation tag, PNG with alpha, WebP, animated GIF, a crafted 60 MP header, a JPEG/HTML polyglot, a plain PDF, an encrypted PDF (needs a tool such as qpdf, or hand-crafted bytes), an OOXML file.
- A writable Tellma:ScratchPath and LocalApplicationData. On Windows the RootPath is capped at 160 characters to stay under MAX_PATH.
- No Azure account and no Live tier: the first real Azure deployment must confirm that Storage Blob Data Contributor covers container creation.

## 0018

**Possible contradictions and gaps (unverified leads):**

- Stack-registration conflict: 0014 §1.2 says CoreFeature's `Entity<User, UserService<User>>()` and `Entity<Role, RoleService<Role>>()` are 'the one registration of the two stacks. Spec 0018 supplies the services and adds no Entity line', whereas 0018 §1.2 lists the same two Entity lines as 0018's CoreFeature registrations. Implemented literally, they register twice. Because the types are 0018's, 0014 cannot compile its registration until UserService<TUser>/RoleService<TRole> exist, so 0014's checkpoint must ship shells (or defer the line) and 0018 must fill them in without re-registering.
- §6.3 has acme.sample-data invite the e2e principals through §3.3 during `migrate`. Under 0011 §5.7, though, the in-proc identity engine is hosted only by the web process, and the <slug>-svc client is seeded at web-host startup. On a fresh clone (0011 §6.6; 0024 §13's Playwright webServer runs `migrate` before starting the host) no identity server is reachable and the client registration does not yet exist, so the invite raises DependencyUnavailableException and provisioning fails. A decision is needed, for example assigning Subject directly as the §6.1 bootstrap does, or hosting the engine in the migrator for Development.
- §6.3 invites 'in tenant 2 only, a user of no other tenant', but tenant 2 is 'Acme Sandbox (Sandbox of 1)' (0011 §5.7), and §3.3 sets ExistingOnly on sandbox tenants. A fresh in-proc identity store holds only admin@localhost, so this invite yields Users.SandboxRequiresExistingIdentity, not a usable principal. §A.7 is also not implemented in Tellma.Identity yet.
- §7 InitialTenant must contain tables of 0019-0022, 0026, 0028, 0033 and 0035, while the parity test (GetPendingModelChanges empty) requires the migration to match the composed model at every commit. The migration therefore cannot be final when 0018 lands and must be regenerated by every later checkpoint that adds a table. 0011-0016 checkpoints will also have needed interim acme migrations, which this one replaces.
- EmailNotificationChannel ownership is split across specs. 0018 §1.2/§3.1 owns the class and its test send, 0022 §4 specifies DeliverAsync, and 0021 registers the channel descriptor. 0018's DoD defers only 'the welcome email's outbox path' to 0022 and never mentions DeliverAsync, so the planner must assign DeliverAsync to 0022's checkpoint explicitly.
- §5.4's rule (a type change to Service/Operation/Sale requires before-image SubtreeCount = 1 and no payload row naming the row as parent) refuses a valid save that, in the same payload, re-parents the row's existing children elsewhere. This is minor, but an implementer following the text will reproduce it.
- §6.3 'images on some' customers does not say where the image bytes come from (embedded resources or generated with Skia). Staging under the System scope also needs the image processor and blob store composed in the migrator (they are, via UseAzureDefaults) and core.blob-container (Order 20) to run before acme.sample-data (Order 200).

**Slow or heavy tests (planning estimates):**

- §9 'Against the in-proc identity server' (invite end to end, delivery status after a real send through the email log sink, service account + client_credentials token + bearer endpoint): engine start ~10-20 s plus tests ~20-40 s. Keep under Category=Integration with one shared host per class.
- §9 distribution suite 'migrate then provision from an empty server' with Development sample data (40 centers, ~60 customers with staged images, 20 invoices, principals, two tenants): ~1-3 min per fresh database. Keep, provision once per test class, and do not re-provision per test.
- §7 model-parity test (GetPendingModelChanges over the composed context): ~10-20 s model build. Keep.
- §9 members extra 'at the 500-row cap': seeding ~501 users + memberships through TVPs takes a few seconds. Keep.
- §9 'Recomposed write-back' (the stub identity server commits a role save while answering invite): timing-sensitive. Make it deterministic by committing inside the stub's callback, never with sleeps.
- §10 CI 'the acme smoke deployment provisions a tenant and invites its administrator': this needs a deployment environment that may not exist for an autonomous agent. Gate it as a pipeline job, or defer until deployment infrastructure exists.

**External dependencies:**

- Tellma.Identity in-proc engine with 0010 Part A §A.1 (Distribution seed, <slug>-svc client with tellma_identity scope), §A.8 (v1 routes) and ideally §A.7 (existingOnly) implemented. Today the controllers are unversioned and lack ExistingOnly.
- dotnet-ef 10.0.9 local tool (present in .config/dotnet-tools.json) and a design-time SQL Server (LocalDB TellmaTenantDesign or TELLMA_TENANT_MIGRATIONS_SQL) for generating InitialTenant.
- SQL Server with hierarchyid, system-versioned temporal tables, sequences and RCSI (LocalDB on Windows, Testcontainers.MsSql on Linux).
- The email log sink from spec 0007 (exists) for the delivery-status integration test.
- Arabic translations authored for Strings.ar.resx (GL keys are listed in §5.1, plus every Acme entity, member, action and setting).
- Image bytes for 'images on some' sample customers: generated or embedded assets, plus a registered IImageProcessor and IBlobStore in the migrator composition (UseAzureDefaults provides both).
- No new NuGet packages (uses IHttpClientFactory/Microsoft.Extensions.Http, already pinned).

## 0019

**Possible contradictions and gaps (unverified leads):**

- Ordering trap with 0015 §15. 0015 implements IExcelRowSource, ExcelQuery, ExcelPage and ExportSource 'verbatim' from 0019 §2.1/§4.1, which are declared in Tellma.Core.Abstractions.Excel, and 0015 §4.1/§6.8 also needs SaveSource.Import and OnPersist. Whichever spec lands first must create these contract types. If 0015 goes first (the likely order), its row-source checkpoint must create them, and 0019-a must not redefine them.
- ReportExportJobArguments (§12.1) carries 0033's ReportExportRequest, and §15's pure test requires 'ExportJobArguments round-tripping through JSON on its kind for all three cases'. That test cannot pass before 0033 exists, so the Report case (arguments record, handler dispatch, round-trip assertion) has to ship in 0033's checkpoint.
- The import-file and export-file kinds exist only when Import.FileId/Export.FileId carry [BlobReference] in the realised model (0017 §2.4), so intake (inspect-import) depends on the Export/Import entities. Those entities need 0020 (IJobEntity, the JobId FK to core.Jobs, IJobQueue in ImportService.ContributeAsync, Job.Status in ValidateDeleteAsync). Even the synchronous import therefore depends transitively on 0020. Conversely, 0020 §14.4's core.file-retention deletes through the Export, Import and SupportRequest stacks, which is a mutual dependency the planner must sequence (retention after 0019-c and 0026).
- §3.1 has a TimeOnly row ('fraction + hh:mm:ss'), but QueryexType has no time-of-day member (QxBool…QxGeography), the manifest's Type column can only hold QueryexType names, and 0012 does not support TimeOnly properties (0010 Part D states 'No time-of-day type'). The row is unreachable, or the type mapping is undefined.
- §4.1/§4.2/§12.3 set ExcelQuery.CountCap to 100,000 (synchronous) or 1,048,575 (background), but 0015 §2.5 sets the stack's CountCap ceiling at 10,000 and throws LimitExceededException when it is exceeded. 0015 §15 explicitly exempts only MaxSkipWindow from binding the row source, so the implementer must also exempt CountCap (and Take = cap + 1 versus MaxTake) on this path.
- The §2.1 sketch declares `ExcelOperations<TEntity> where TEntity : class` without IEnlists<Import>, which §12.1/§12.2 and 0015 §13.3's table require for import/start's enlisted insert. 0015's core.enlistments startup check validates IEnlists pairs, so the companion must implement it.
- §15's cross-tenant round trip needs 'two fixture tenants', but 0012 §13.2's FixtureTenant exposes TenantId = 1 only. A second fixture tenant database, or a manifest whose SourceTenantId differs, must be provided.
- 0016 §… lowers the web surface's MaxEntitiesPerSave to 1000, while §2.3 bases the synchronous import on the stack's MaxSaveCount/MaxIds (10,000). The spec does not state that an in-process SaveAsync from the import action is exempt from the web lowering, though it presumably is.
- §15/§16 require corpus files saved by Excel, LibreOffice and Google Sheets plus a manual Windows-Excel check. These cannot be produced by an autonomous agent, so the DoD is not fully reachable without a human step.

**Slow or heavy tests (planning estimates):**

- §15 'a by-query export of MaxSynchronousExportRows + 1 rows rejected at the first page': at the default of 100,001 rows, seeding alone takes ~10-30 s. Keep, but lower MaxSynchronousExportRows in the test host (for example to 50), since the option is configurable.
- §15 'TooLargeForSynchronous' at the default MaxSynchronousImportRows (10,000) and 16 MB: lower the options in the test host. Keep.
- §15 chunked background import suite (about 15 scenarios through the 0020 worker harness: simulated crash, lease loss, host stop, three exhausted attempts, PendingConfirmation/confirm, cancel, retention stop, re-chunk 100 → 37): ~1-3 min total and timing-sensitive. Keep it under Category=Integration, perhaps with an extra 'Slow' trait, and drive leases and stops through deterministic fault-injection hooks and FakeTimeProvider, never sleeps.
- §15 TooManySharedStrings / spill: crafting a <si/> flood at the default 16,000,000 entries or a 32 MB budget costs ~5-20 s and large memory. Keep it pure, with lowered MaxSharedStringEntries/MaxInMemorySharedStringBytes or a lazily generated stream.
- §15 crafted package-limit zips (2 GB declared uncompressed): craft only the central directory and never materialise 2 GB. Fast. Keep.
- §15 export → import round trips across two fixture tenants at depth 2 in three modes: ~10-30 s. Keep.
- §15 'One manual milestone check on Windows desktop Excel' and the corpus 'files saved by Excel, LibreOffice and Google Sheets': human-only. Defer to the first human-testing milestone, and commit synthetic stand-ins first, flagged for replacement.
- Review flags 9 and 14 mention measuring lock escalation at 10,000-row chunks and export duration near MaxExportRows. Neither is mandated as a test, so do not add them.

**External dependencies:**

- NuGet: DocumentFormat.OpenXml 3.5.1 (MIT). Check that the version resolves on nuget.org.
- A writable Tellma:ScratchPath with enough disk for spooled exports (up to the 100 MiB export-file kind limit) and copied imports (up to a 2 GB uncompressed bound).
- Corpus files genuinely saved by Microsoft Excel, LibreOffice (scriptable headless with `soffice --convert-to`) and Google Sheets (needs a Google account): an autonomous agent can produce only the LibreOffice ones, if LibreOffice is installed.
- Windows desktop Excel for the manual [$-170401] Um Al Qura rendering and manifest-survival check.
- ICU culture data on Linux (globalization-invariant mode must be off) for ar-SA and am-ET date patterns and LCIDs; a custom culture reporting LCID 0x1000.
- 0013's ICalendarSystem implementations for gc, uq (0010 §F.11 host month table) and et (0010 §F.19).
- SQL Server: TVP key lists (StringList nvarchar(450), IdList, BigIdList, GuidList, DateList) and RCSI. A second fixture tenant database or a manifest SourceTenantId override is needed for the cross-tenant tests.

## 0020

**Possible contradictions and gaps (unverified leads):**

- §7.4 ReadOnly variant of tick step 1 filters with `[k].[HandlerKey] IN (SELECT [Value] FROM @tb{b}_t0)` but StringList's only column is `Id` (src/core/Tellma.Core.Abstractions/TableTypes/BulkLists.cs; §6.9's statements use `[k].[Id]`). The golden text as written would not compile — use [Id]
- Likely SQL Server error 1785 (multiple cascade paths): core.ScheduleStates is reachable from core.Schedules via FK_ScheduleStates_ScheduleId ON DELETE CASCADE (§7.2) and via FK_Jobs_ScheduleId ON DELETE SET NULL (§2.1) → FK_ScheduleStates_LastJobId ON DELETE SET NULL (§7.2). Verify in 0020-b's first migration; a fallback (e.g. LastJobId NO ACTION with job-retention nulling it, or no FK) would be a spec deviation needing sign-off
- §14.4 core.file-retention and its §16 pins require Export/Import (0019 §12) and SupportRequest (0026 §12.8) stacks, yet §17 counts it in 0020's done; 0020 lands before 0019/0026 in any sane order — split it out (0020-g)
- §1.1/§12.3/§17 require changing 'the reference distribution's infra template', but no spec (0011, 0018) creates an infra template under distributions/acme; only prose in 0011 §… hosting. Either create one (scope creep) or record the settings in docs/ARCHITECTURE only
- §13 says alert queries are 'cross-checked by the existing name test', but that test lives in Tellma.Core.Email.Tests and only scans infra/monitoring/email against email telemetry constants; it must be generalised (or a new one written in Tellma.Core.Tests) to cover JobsTelemetryNames
- §1.4 says every statement is fixed text owned by the spec and §16 golden-files 'the statement text of §4–§7 and §11.1', but several are not given: the pause UPDATE (§7.7), the ScheduleStates upsert / PausedReason clear in ScheduleService.ContributeAsync (§7.8), the backlog sample (§6.3 item 4, a fragment only), the job actions' RT1 held-requester load (§11.1). Implementers author them; reviewers should not flag their wording as deviation
- §3.1 sketches JobHandlerAttribute with primary-constructor optional parameters, but every usage (§4.5 of 0021, §5 of 0022, §14.4) writes `LeaseSeconds = 600` — attribute named-argument syntax that needs settable properties; implement Key as ctor parameter and the rest as properties
- Contract cycle: JobItem.NotifyOnSuccess takes 0021's NotificationRequest; JobError derives from 0015 §7.3 CodedError; 0015 §4/§13.3 consume IJobQueue/IJobProgress; 0021's InboxRunningJob uses JobStatus. The abstractions must be co-landed in a fixed order (0021-a → 0020-a, after 0015's contract types) or the build breaks
- §1.3 principle 1 (database clock only) vs §16 'the worker under a fake TimeProvider': lease expiry, backoff, heartbeat throttle and gap are computed by SYSUTCDATETIME(); only interval/drain logic can be faked. Integration tests must manipulate stored timestamps
- §7.5 step 6 cites §7.1 for 'RequestedById = null for a built-in'; §7.1 says nothing of it (the rule is implicit in §7.9/§8). §3.3's table names IJobProgress.Flush while the interface declares FlushAsync — cosmetic
- §12.1 says 'A worker host without the hub is not supported in this release' while §1.1/§10 design for a future dedicated worker host with NullClientEventPublisher — not contradictory, but 0021 §6.6's realtime-hosting check is what enforces it

**Slow or heavy tests (planning estimates):**

- §16 integration suite overall, run twice (RCSI on and off) on LocalDB (Windows) / Testcontainers SQL Server (Linux): ~100+ fixture tests, plausibly 4–8 min per OS leg incl. container start. Keep on PR (spec mandates), but share one database per collection and reset with targeted deletes
- Plan test (§16; filtered-index seek under TOP(@room), reserved-band clustered range seek, IX_ScheduleStates_Due seek): seconds, but plan shapes can differ between LocalDB and SQL Server 2022 containers — keep, tag a trait so it can be quarantined if flaky
- Claim contention from two connections and the poll re-run with an injected deadlock / killed connection (§6.8, §16): timing-sensitive concurrency + fault injection into the executor; seconds each; keep, needs a deterministic injection seam rather than real deadlocks
- Enqueue for 3,000 owners (§16): bulk insert + one UPDATE, ~2–5 s; keep
- Retention paging: DELETE TOP (1000) is a literal, so the paging test needs >1,000 job rows per band (and file-retention >500): ~3–10 s; keep
- Worker suite under fake TimeProvider (adaptive interval, nudge coalescing, drain, 5-s jump throttle): fast but async-scheduling flake risk; keep
- DB-clock hazard: lease ≥15 s, tick lease 30 s, heartbeat throttle 60 s, GapThreshold ≥1 h and retry backoff are computed with SYSUTCDATETIME() and cannot be moved by a fake TimeProvider — tests must rewrite LeaseExpiresAt/LastTickAt/DueAt directly; any test that sleeps would run minutes. Ban Task.Delay waits in review
- End-to-end worker test (enqueue→claim→progress→completion, §16): ~5–15 s with a real hosted worker; keep

**External dependencies:**

- NuGet Cronos 0.13.0 (new to Directory.Packages.props)
- SQL Server features: sequences + sys.sp_sequence_get_range, filtered indexes, LOCK_ESCALATION=DISABLE, system-versioned temporal table (Schedules), READPAST/UPDLOCK/ROWLOCK, OPENJSON/STRING_AGG/CONCAT_WS (SQL 2017+), THROW 50422, showplan access for the plan test; RCSI toggled on/off per fixture
- LocalDB on Windows, Docker + Testcontainers SQL Server on Linux
- ICU on both OSes for IANA zone ids via TimeZoneInfo.FindSystemTimeZoneById (Windows ICU mode required; alias Asia/Calcutta must be refused by the value set)
- Azure App Service settings Always On and WEBSITES_CONTAINER_STOP_TIME_LIMIT=30 (deployment only; WEBSITE_INSTANCE_ID read for LeaseOwner)
- No browsers, fonts, PDF engines, credentials or brand assets

## 0021

**Possible contradictions and gaps (unverified leads):**

- EmailNotificationChannel ownership is split three ways: 0018 (§… line 375) declares the class in Tellma.Core.Notifications with 'DeliverAsync as spec 0022 §4 describes'; 0021 §4.1 registers the channel; 0022 §4 specifies DeliverAsync and §10 unit-tests it, while 0022 §1.1's runtime list omits the class. If 0018 is built before 0022, DeliverAsync cannot call IEmailOutbox. Assign DeliverAsync to 0022-a and only SendTestAsync (or a stub) to 0018
- Hub claims seam: 0011 §4.3 step 3 has the tenant middleware add 'spec 0021 §6.1's hub claims', but TellmaHubClaims is a 0021 type; 0011's middleware checkpoint must either leave a hook or 0021-d amends the 0011 middleware
- §6.6 realtime-hosting check detects Azure mode and the ApplicationName by locating the SDK's ServiceOptions 'by type name' (reflection), while Tellma.Core.AspNetCore and its unit test project must not reference Microsoft.Azure.SignalR; §12 pins only the in-process failure. The Azure-mode failures ('connection string without delegate', 'wrong ApplicationName') are untested unless a test project referencing the SDK (Tellma.Defaults.Azure.AspNetCore tests or the live project) covers them
- Contract cycle with 0020: InboxRunningJob/InboxSummary use JobStatus (0020-a) while 0020-a's JobItem uses NotificationRequest (0021-a); keep InboxSummary out of 0021-a. 0011's FeatureContribution.NotificationType/NotificationChannel take 0021's descriptor records, so those records must exist when 0011's composition compiles
- §3.1 says a call fails with InvalidOperationException when the type is not registered 'on this instance' and §11 requires inbox and email registered — fine — but 0022's core.email.failed and every other Core type are declared by their raisers (§2.3), so test compositions that host CoreFeature alone (§12 'hub tests host CoreFeature alone') have only core.user.added; worker notices (core.job.*) are registered by 0020's CoreFeature contributions, so ordering of 0020-f vs 0021 tests matters
- §2.5 / §11: NotificationsClientAssetContributor depends on 0024 §3.2's emit command, a client-spec backend piece likely scheduled later; only unit-testable until then
- §5.2 marks seen/read/... `Idempotent = true` though each call increments InboxVersion — consistent with 0016's retry-safe meaning, but reviewers may flag it

**Slow or heavy tests (planning estimates):**

- Notifications fixture suite (§12) under RCSI on and off on LocalDB/Testcontainers: ~60–80 tests, plausibly 2–4 min per leg; keep on PR
- 'A summary started while a verb's transaction is open returning the earlier version' (RCSI on): two-connection concurrency, seconds; keep, needs deterministic ordering (open transaction held explicitly)
- Summary cap test needs >SummaryCountCap (≥100) rows; retention paging needs >RetentionPageSize rows (configurable down): seconds; keep
- Delivery job end to end through spec 0020's worker: ~5–15 s; keep
- Realtime unit suite with TestServer WebSockets incl. 'a connection closed when its session ticket expires': can wait seconds for expiry unless the ticket clock is faked; keep but use a short/fake ticket lifetime
- Realtime live suite against a real Azure SignalR instance (§12, Live=true): 1–3 min plus provisioning; gate behind Live trait + nightly + CI secret; defer until the Azure instance exists

**External dependencies:**

- NuGet Microsoft.Azure.SignalR 1.33.1 (new; Tellma.Defaults.Azure.AspNetCore only); SignalR server from the Microsoft.AspNetCore.App shared framework; Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.11 only for a future on-prem host (not in repo)
- MessageFormat 8.0.0 (already pinned) for ICU rendering
- Azure SignalR Service instance + Azure:SignalR:ConnectionString in the CI secret store (live suite only)
- Redis (only for on-prem multi-instance; not needed for tests)
- SQL Server: sequences + sp_sequence_get_range, filtered indexes, RCSI on/off; LocalDB/Testcontainers
- WebSocket support in TestServer; sticky sessions only for Redis deployments

## 0022

**Possible contradictions and gaps (unverified leads):**

- §1.4 says 'SQL blocks are the exact statements to emit', but only §6's delivery update is given; the pre-mark (§5 step 3), settle (§5 step 5, incl. the interrupted-row settle of step 1), attachment load (§5 step 2) and retention id read (§7.3) must be authored by the implementer
- EmailNotificationChannel ownership: §4 specifies and §10 tests DeliverAsync, §1.1's runtime list omits the class, and 0018 declares it (Tellma.Core.Notifications, 'DeliverAsync as spec 0022 §4 describes'). Build DeliverAsync in 0022-a regardless of 0018's order
- §9.3 places the tables in 0018 §7's InitialTenant migration, which also needs tables from 0019, 0026, 0028, 0033 and 0035 — the migration can only be generated after all those schemas exist (or must be regenerated per checkpoint)
- §6 step 2 relies on 'throws once, so spec 0007's receiver answers 503'; the implemented receivers catch exceptions (e.g. SendGridEventsWebhookReceiver) — confirm the catch maps to WebhookOutcome.TransientFailure (503) and not 500
- §3.2 example and §3.1 sketch omit CancellationToken per notation, but the implemented IEmailDeliveryEventHandler.HandleAsync takes one — implement against the real signature
- §1.2 'orphaned' includes an unsettled row whose job is Failed/Cancelled; §7.3 retention deletes orphaned rows older than Retention, so an interrupted Pending row whose job failed is silently deleted after 180 days without a retry — intended per Decisions 11, but worth a reviewer note

**Slow or heavy tests (planning estimates):**

- Dispatch 'attempts exhausted' (MaxAttempts = 12, backoff up to 7,200 s on the database clock): a naive run through the real worker would take hours; tests must set the job's Attempts/DueAt directly or invoke the handler through 0020-c's poll-once driver. Keep, with that harness
- Integration suite on LocalDB/Testcontainers (+ blob store fake for attachments): ~25–35 tests, ~1–3 min; keep on PR
- Delivery-event batches with duplicates/out-of-order: fast; keep

**External dependencies:**

- No new NuGet packages (uses implemented spec 0007 connectors and Tellma.Core.Testing)
- A blob store implementation for integration tests (spec 0017's test/local store — Azurite or filesystem, whichever 0017 chose)
- SQL Server (LocalDB/Testcontainers); ITenantDatabaseFactory needs catalog-resolved connection strings
- Email transports only for real deployments (SendGrid/ACS/SMTP credentials); none needed in tests

## 0023

**Possible contradictions and gaps (unverified leads):**

- §2.2 'Motion' says the only motion is the switch knob, progress sweep, ring spin and modal fade. Yet §5.4 step 2 shows a spinner in the busy overlay and §8.2/§8.5 show tm-spinner in the tree's and tiles' reserved slots, and tm-spinner animates. Neither the motion list nor the reduced-motion clause ('All four are static') covers tm-spinner. Decide whether tm-spinner is exempt or must be static under reduce.
- §8.5 gives the virtualization pitch only for the lattice (70 px = 60 px tile + 10 px gap). The list layout below 576 px and tree mode ('one column with --divider lines') have no pitch: is it 60 or 61 px with the divider? tmComputeAxisWindow needs an exact figure.
- §5.1 toasts: `TmToasts.focus()` moves focus into the top-layer toast host, and Tab moves between toast buttons. TmModal (and so confirm/error-dialog) is a CDK Dialog with a focus trap, so focus() while a modal is open will be pulled back into the dialog. The spec does not say whether focus() refuses, or Tab is scoped, while a modal is open.
- §10 has `confirm.ok` ("OK") but no API path uses it: ask() takes caller labels and confirm() requires confirmLabel. Conversely, the collapsed chip set's visible "+N" text (§4.3) has no key; only its accessible name `chip.more` does. Its digits must follow the locale (§2.2 'Numbers').
- Ordering: 0010's shared rules require 0023 §3.1/§3.2 before 0010 Part B, and 0023 §4.3 (chip) before 0010 Part E. But 0023's own context says it 'builds on spec 0010 Parts B–D'. 0023 cannot ship as one unit; the planner must interleave (see cluster notes).
- §12 says every suite 'runs on every pull request', including the 10,000-row long-task assertion. That is a wall-clock perf assertion on shared runners, which conflicts with a reliable PR gate (see slow tests).

**Slow or heavy tests (planning estimates):**

- §12 Playwright: 'tiles at 10,000 rows with no long task over 50 ms while scrolling'. About 30–60 s. Timing-sensitive on shared CI runners, so expect flakes. Recommendation: keep on Chromium only behind an @perf tag, run nightly, and keep a PR smoke that only asserts virtualization (rendered rows below N).
- §12/§13 axe battery: every story state in light/dark × LTR/RTL, with overlays open, across 30 entry points (roughly 150 states × 4). Likely 5–10 min. Recommendation: keep on PR but shard; or run changed-entry-point stories on PR and the full battery nightly.
- §13 'keydown-to-paint probe per story'. 2–4 min across all stories. Keep, sharded.
- §12 cross-engine native drag (tree → textarea, tree → reorder list) in Chromium, Firefox and WebKit, plus the search-field Mod+Z undo in three engines. About 1–3 min. Keep as @cross-engine on PR.
- Toast timer pause/resume, busy overlay 200 ms scrim and progress hold 250 ms. Each is a few seconds of real time and timing-sensitive. Recommendation: keep, but drive with Playwright page.clock or vitest fake timers, never wall-clock sleeps.
- Forced-colors and reduced-motion emulateMedia suites plus the Pixel 7 touch project. About 2 min. Keep.
- Overall, 0023 likely adds 10–20 min of Playwright to each PR run unless sharded.

**External dependencies:**

- npm `lucide` (vanilla, ISC), new to the repo. A `dependencies` entry of @tellma/core-ui with allowedNonPeerDependencies. Review flag 2: check at implementation time whether `@lucide/icons` v1 is stable; the registry accepts its `{ node }` objects either way.
- @angular/aria/tree, inside the existing @angular/aria 22.0.3. Verify that tree supports selectionMode 'explicit' and lazily rendered ngTreeItemGroup in 22.0.3.
- Existing toolchain only otherwise: Playwright Chromium, Firefox and WebKit plus the Pixel 7 device project, axe-core via client/e2e/support/axe.ts (removed 2026-11-08 by PC-F05: the e2e axe floor runs axe-core directly through client/e2e/support/axe.ts), vitest browser mode.
- Browser features: Popover API (popover=manual) for the toast and busy-overlay hosts; the `inert` attribute; Intl.Segmenter (avatar initials); navigator.clipboard (copy, with fallback); document.execCommand('delete') for undoable clear; HTML5 DnD (no touch DnD on Samsung Internet or Firefox Android, Review flag 1).

## 0024
- (PC-069 → PC-075) TmSession must write `boot.patch({sub})`, and only after the store boot step's sweep and readAll; until it lands every launch is cold, warm launch is unreachable and the 3c branch never runs.
- (PC-069 → PC-110) Replace the interim TmSessionPaths bodies (signOut omitted; expired only sets state and reloads; changedUser only emits and replaces to /, no broadcast) and add spec 0024 §8.8's re-check triggers. Until then a tab of user A keeps showing A's data (and session-storage drafts) after user B signs in elsewhere, until it navigates; PC-071 onward renders data while this gap is open.
- (PC-067) The app-config check reports `string-key-collision` only when the build passes the string packs to it (optional argument); the build/config-check checkpoint must pass them. (The declaration registry lives on globalThis under `Symbol.for`, so loading @tellma/core twice is safe.)

**Possible contradictions and gaps (unverified leads):**

- §6 CSP vs ICU messages. The CSP is `script-src 'self'` (no 'unsafe-eval') with `require-trusted-types-for 'script'`. But the library's ICU strings go through @jsverse/transloco-messageformat → @messageformat/core 3.4.0, whose compile uses `new Function` (verified at node_modules/.pnpm/@messageformat+core@3.4.0/.../lib/messageformat.js:130). Every ICU message (0023 §10 keys, 0024 §8.4's plural sign-out message, the string packs) would throw under the deployed CSP. `serve` sets no CSP (§2.6), so only the Acme e2e host with --webroot would catch this. A build-time ICU precompile, or an eval-free runtime, is required and is not specified.
- §3.6 vs §3.3/§4.3. The startup config check calls tmCheckAppConfig(config, metadata, entities) and, in production, 'emits each problem as tm.config.problem'. That requires the Entities chunk at startup on every route, while §3.3 loads that chunk only on entity routes and §4.3 says initializers await only local reads. It also puts the whole checker, including 0029's tmCheckEntityDeclaration, in the eager primary within §12's 14 KB share. When the production check runs, and whether it is eager, is unresolved.
- §3.2/§6/§13 vs spec 0011 §5.1/§5.3/§6.1: ownership overlap. The C# contracts IClientAssetContributor, ClientAssetContext and TellmaRoutes, the emit's file naming, the SPA hosting rules and the 'C# emit and hosting' test suite are specified here, but implemented by 0011 (§6.1 emit-client-assets, §5.1 MapTellma, §5.3 audit), which lands earlier. The planner must assign them to 0011 and treat 0024's §13 C# rows as already covered or extended.
- §9.3: `launch-landing-vectors.json` lives in `@tellma/core` (src/launch/), which 0024 creates, but spec 0011's C# tests (0011 §10) link it. 0011 must create that file at client/projects/core/tellma-core/src/launch/ ahead of the package, or 0024-c must land first.
- §1/§2.1 (and ARCHITECTURE.md): 'a member of the repo-root pnpm workspace'. Today the workspace and lockfile live in client/ (client/pnpm-workspace.yaml), and no spec owns moving them to the repo root with a pnpm catalog or adapting client/scripts. Spec 0011 §10 step 1 already assumes `pnpm install` at the repo root.
- §5.3: the `on-demand` group names 'the ECharts engine's dynamic import, @tellma/core/report-studio or @tellma/core/support', which ship with 0034 and 0026. The spec does not say how the generator behaves while those entry points don't exist yet (it must not fail the build).
- §13 `signInAs`: 'reads the code from the capturing sender that replaces the Development email sink in this host (spec 0003 §10.6)'. But the Acme host runs out of process (`dotnet run`), and no spec (0011, 0018, 0022) defines a Development switch, endpoint or file drop through which Playwright can read the email code. 0003 §10.6's capturing sender is an in-process substitution.
- §4.4/§10.3/§2.2/§8.4: the route host uses provideTmPageScope (0026 §9.2), TmApi uses TmRequestLog.begin/end (0026 §8.1), provideTellma registers TmErrorHandler (0026 §4.1), and sign-out calls TmTelemetry.flush(). All belong to 0026, which builds on 0024. The seam (tokens with no-op defaults) is not specified.
- §4.4 cold entry and §7.1 inputs depend on 0025's TmLaunchCache and TmCacheRefresher, while 0025 depends on 0024's TmApi and TmSession. 0024 can only be completed interleaved with 0025.
- §4.2 step 2 (/signed-out): 'apply lang, dir and theme from the record, else step 4's cold choice'. But step 4's cold language order begins with the profile's `locale`, and /signed-out runs with no cookie needed. Presumably the profile step is skipped; this is unstated.
- §2.6 `serve`: 'In Acme, it first runs the platform's generated-asset steps (tokens:build-css)'. A published tool running a monorepo-only script is unspecified; detection of the in-repo case is needed.
- §12: the primary's 50 KB ceiling is the sum of shares from 0025–0029, 0032, 0034 and 0035. Before those specs land, the ceiling the checks enforce is undefined. The planner must set an interim ceiling per checkpoint.

**Slow or heavy tests (planning estimates):**

- §12/§13 nightly: installability (CDP Page.getInstallabilityErrors on Acme's production build, about 2 min), Lighthouse cold on Slow 4G (LCP ≤ 4 s, 3–5 min) and warm chrome ≤ 300 ms with 4× CPU throttling (about 2 min). Keep nightly, as the spec says. New dev dependency: lighthouse.
- §13 Acme e2e host startup: webServer runs migrate, then tellma-client build (ng build plus checks), then dotnet run, with a Testcontainers SQL Server on Linux. Fixed cost of about 3–6 min per CI job. Keep, but build once per job and reuse across the 0024–0035 suites; cache the built output by hash.
- §13 sign-out across three tabs, including 'a save still running after 60 s'. At least 60 s of wall time if run for real. Gate behind a nightly tag, or drive the 60 s watchdog with Playwright page.clock.
- §13 '412 with an older server build' plus §10.5's single 30 s server-outdated retry. At least 30 s of wall time in e2e. Unit-test the 30 s retry with a fake clock (as the spec says) and keep the e2e free of the wait, or gate it nightly.
- §13 device-zone.spec: a 2 s idle gate and multiple tabs, about 30–60 s. Keep. zone-labels.spec @cross-engine, about 1 min. Keep.
- §13 responsive and RTL: the boot page, dialogs and overlays at 390/768/992/1440 px in en and ar, 3–5 min. Keep, sharded.
- §13 offline banner: the /health/live probe every 5 s backing off to 30 s, roughly 10–40 s. Keep with page.clock if possible.
- §13 build-check integration ('each source and output check code on an injected problem, exit 3'). If each case runs a real ng build, 1–2 min each, 10+ min total. Recommendation: one real build fixture shared by the output-check cases; source checks run on synthetic inputs without ng build.
- §12 staging smoke (abort propagation via the server's cancelled counter, headers/CSP per response class, worker update across a slot swap). Needs an Azure App Service deployment. Defer until a deployment pipeline exists.
- Whole 0024 PR e2e on the Acme host is likely 15–30 min. Shard, and run @cross-engine subsets only on Firefox and WebKit.

**External dependencies:**

- npm `@angular/service-worker` 22.0.x, new: provideServiceWorker, SwUpdate, ngsw-worker.js, safety-worker.js and the `ngsw-config` bin.
- npm `tsx` as a runtime dependency of @tellma/core-tools (the config check loads app.config.ts). Today it is a devDependency at the client root.
- npm `lighthouse` (nightly dev dependency); @playwright/test as an optional peer of @tellma/core-testing/e2e.
- An ICU runtime that works under the §6 CSP (see contradictions). The current @messageformat/core 3.4.0 uses `new Function`.
- SQL Server: LocalDB on Windows and Testcontainers.MsSql (already in Directory.Packages.props) with Docker on Linux, for the Acme e2e host.
- The ASP.NET Core HTTPS development certificate (`dotnet dev-certs https --export-path`) for `serve` and the e2e host on https://localhost.
- Chromium DevTools Protocol (Page.getInstallabilityErrors, Page.getAppManifest, Emulation.setTimezoneOverride) via Playwright's Chromium; Firefox and WebKit for the @cross-engine subsets.
- Browser features gated in tm-boot.js: navigator.locks, BroadcastChannel and IndexedDB; plus Trusted Types and service workers.
- Brand assets: @tellma/core/brand/{wordmark,mark}-on-{dark,light}.svg (four SVGs with viewBox). These do not exist in the repo.
- Acme PWA icons: public/icons/favicon.svg, icon-192.png, icon-512.png and icon-maskable-512.png (PNG header sizes are checked). These must be created.
- Fonts: the existing Noto Sans/Mono and the locale-ar Arabic subset; preloads are injected by the build.
- Azure SignalR origins in connect-src (only when Azure:SignalR:ConnectionString is set) and Azure App Service for the staging smoke. Deployment-time only.
- (D-046, from PC-019) The server pins CLDR 48 culture data with two corrections: om-ET, om-KE, ti-ER and ti-ET numeric dates stay day-first (CLDR 48 switched them to M/d/yy), and ar-SA/ar-SO use '%' instead of U+066A. The client's §7.7 fallback formats for om/ti/so must apply the same day-first rule, or one user sees two date orders.

- PC-069 → PC-071: `expired(source, sh?)` ignores a 401 whose request handle `sh` no longer matches the cookie, so PC-071 only has to pass the request's handle. Within 60 s of a sign-in (`mark.at`) `expired()` shows the sign-in-failed boot page and keeps the cookie; its retry deletes the cookie and reloads.
- PC-069 → PC-075: once PC-075 writes `sub` into the boot record, an expiry is reported twice (`cookie`/`401`, then `boot`); dedupe it there.
- PC-069: the boot bundle is at its 3,072 B ceiling for English-only (zero headroom after PC-022's typographic apostrophes). The next byte added to the bundled boot modules needs an equal saving or a D-048 ruling.
- PC-071 → PC-104, PC-096, PC-110, PC-069's successors and the telemetry pipeline: TmHttpApi resolves TmSession, TmSessionPaths, TmLaunch, TmTelemetry and its three seams at each use, never in a field initialiser, so their owners may inject `TmApi` eagerly and provide a seam with `useExisting` without a circular dependency. Keep it that way: a new eager `inject()` in TmHttpApi of anything that calls TmApi brings the cycle back.
- PC-071 → PC-104: TmLocalization must provide the internal `TM_API_LOCALE` seam (`client/projects/core/tellma-core/src/api/tm-api-seams.ts`): `acceptLanguage()` returns §7.7 step 4's string and `calendarCode()` the calendar. Until then the HTTP layer sends the document's `lang` and no `Tellma-Calendar`.
- PC-071 → PC-096: TmCacheRefresher must provide the internal `TM_API_CACHE` seam (`observe(t, tags, 'header')`, `refresh(t, docs, 'header')`); the default does nothing. TmHttpApi already calls `observe` one task after the body and `refresh(t, ['tenant'], 'header')` on `read-only`.
- PC-071 → PC-110: TmUpdates must provide the internal `TM_API_UPDATES` seam (`check(): Promise<'ready' | 'none' | 'failed' | 'unreachable' | 'disabled'>`); the default answers `disabled`. TmErrorPresenter reads the step-up challenge from `TmApiError.challenge`, a member PC-071 added to §10.1's shape.
- PC-071 → PC-113: TmAppVersion.compare must reuse `tmCompareBuilds` (`src/api/tm-api-wire.ts`), so the §5.6 verdict and TmAppVersion order builds alike; TmHttpApi reads the UI version from `TM_CLIENT_METADATA` until then. Blob fetches are deliberately kept out of TmRequestLog.
- PC-071 → PC-059 (done at PC-071's merge): `TmApi.fetchBlob` takes PC-059's `TmBlobFetchOptions`, signal included; the interim `TmFetchBlobOptions` is gone. `provideTellma` now points `TM_BLOB_FETCHER` at `TmApi.fetchBlob` (which rejects an abort with `signal.reason`, as §D.6 asks), and the HttpClient default now loads `@angular/common/http` by a dynamic import at its first fetch, so PC-059's unsubscribe-on-abort goes inside that lazy body.
- PC-071 → PC-104, PC-110, PC-113 and the other 0024 own-services checkpoints: PC-071 raised the `@tellma/core` primary budget from 6.5 to 12.5 KB (gzip 12.42 KB measured after its fix round 1), which leaves about 1.6 KB of 0024's 14 KB own-services share for TmLocalization, TmUpdates, TmUrls and TmErrorPresenter. Measure each addition; a raise past 14 KB is a decision for the orchestrator, not a silent bump.
## 0025

**Possible contradictions and gaps (unverified leads):**

- §8.1 vs spec 0024 §1: 0024's entry-point table says `/realtime` holds `TmHub (0025)`, while §8.1 exports only `tmCreateHub(...)` returning a `TmHubConnection` and calls the result 'a TmHub'. TmHub is never declared as a type or class. The golden name needs deciding.
- §6.2 `hub.event` is 'at most 4 KB', but the spec never says what the leader does with a larger event: drop it, truncate it, or relay a refresh hint. Followers deliver relayed events to events() subscribers, so an oversized payload's fate matters to page consumers.
- §1 says browser storage has 'four writers': TmClientStore, TmDrafts, TmSession and tm-boot.js. But 0024 §4.2 step 3c and §2.6's `session-ended` sweep also delete CacheStorage `tm-images-v1`, which the core-ui cache/image fetcher (0010 §D.6) writes, and 0010 §D.5 clearAll deletes broadly. So the claim is incomplete, and the tm-no-web-storage lint rule's platform allow-list needs those writers.
- §7.2 step 2 (on election: `refresh(t, ['tenant'], 'leader')`) is the warm launch's revalidation. If the leader checkpoint slips (it is marked important here), warm documents are revalidated only by response headers (§3.2/§4.3). The `tenant` document (state, realtime) carries no tag in headers, so a ReadOnly/Frozen change goes unseen without a leader or a cache.changed event. Leader election is therefore load-bearing for tenant-state freshness.
- §3.3 entity lists use `TmApi.read('{segment}/all', …)`. Per 0024 §10.1, `read` refuses a route not listed `idempotent` in the Contracts chunk. The spec assumes 0016 §2.3's `/all` (and the leader routes `inbox/summary` and `assignments/summary`) are in the retried class; that must be verified against 0016/0021/0028's ApiAction flags.
- §14 multi-tab scenarios rely on surfaces from later specs: pins and the pinned bar (0027), the bell and jobs (0028/0020), the tree search page (0030), drafts on details (0031), ReadOnly and role administration (0032). This spec cannot fully meet its own DoD until those land.

**Slow or heavy tests (planning estimates):**

- §14 'Hidden-leader soak': a hidden leader keeps one connection for 15 min under Chromium's intensive throttling. 15+ min. Keep nightly as specified; never on PR.
- §14 multi-tab 'Realtime disabled → the leader polls every 60 s, only while a tab is visible'. Needs at least 2–3 min of real time to observe two polls and the hidden pause. Gate nightly, or use page.clock where the server need not participate.
- §14 multi-tab 'job.changed events dropped → the bell's ring and progress still move within 75 s'. 75–90 s, and needs 0020 jobs and 0028's bell. Gate nightly, and defer until 0028 lands.
- §14 'ReadOnly → every tab shows the marker within 15 s' (15–30 s) and 'a membership that ends … within 3 s' (requires waiting for a real server-side boundary instant, several seconds). Keep on PR.
- §14 'Close the leader → a follower connects within 2 s' and 'Pause the leader in the debugger → a visible follower steals' (≥5 s hung timeout plus 0–2 s jitter). About 10–20 s each. Keep.
- §14 C# 'client/sync' integration on real SQL (Category=Integration, Testcontainers on Linux). About 1 min including container start. Keep on PR as the repo already does.
- §14 'Staging smoke: the 150 s client timeout through Azure SignalR'. At least 150 s, and needs Azure. Defer to the deployment pipeline.
- Refresher unit with the device-clock-ahead backoff (30 s, 60 s … 10 min) and leader 60 s probes. These use fake clocks, so they are fast. Keep.

**External dependencies:**

- npm `@microsoft/signalr`, new, pinned to the server's ASP.NET Core major (SDK 10.0.300 → 10.x) and imported only by @tellma/core/realtime.
- Browser APIs: Web Locks (navigator.locks, including { steal: true }, ifAvailable and query()), BroadcastChannel, IndexedDB (relaxed durability), navigator.storage.persist() (skipped on Gecko), the Page Lifecycle freeze/resume events (absent on WebKit), and pagehide/pageshow persisted.
- Server-side SignalR hub (0021) and, in production, Azure SignalR Service (negotiate redirect; origins in the CSP). Staging only.
- SQL Server for the ClientSyncService integration test: Testcontainers.MsSql on Linux, LocalDB or a SQL instance on Windows.
- Playwright multi-context, plus the debugger-pause ability (CDP Debugger.pause) for the steal scenario; WebKit for the @cross-engine hung-leader spec.

## 0026

**Possible contradictions and gaps (unverified leads):**

- §12.3 sketch: the stack attribute is commented out ('// [Stack(Operations = Query | Details | Delete | Enlist, DefaultOrderBy = "Id desc")]') while [ApiResource] and [DefaultSelect] are live. Spec 0015 §2.2 says Enlist is outside All and must be named, and the core.enlistments startup check fails an IEnlists<SupportRequest> whose stack omits Enlist; the default would also project Save. The implementer must apply the attribute; the spec should show it uncommented.
- §9.5 says views call TmPageActions.invoke and never action.invoke, and every destructive action confirms for every origin. Spec 0028 §13 registers the bell overlay action 'attention.stop-job' as destructive and confirmed, but 0028 §6.2's Stop button confirms only for commitsInSteps handlers ('any other job stops without asking'). Either the bell's Stop is a second path that bypasses the dispatcher, which breaks §9.5's one-place rule, or it is a view of the action and §6.2's no-confirm export stop cannot hold. The same split applies to 'Mark all as read' and attention.read-all.
- §9.4 'Shortcuts. While its context is top, the registry registers each shortcut with TmShortcuts (scope page)', together with spec 0027 §4.2 step 5 ('only overModals shortcuts run, plus the page shortcuts of the dialog's own context'). The TmShortcut contract (0027 §4.1) has no owner or context member. The registry therefore cannot tell an overlay context's page shortcuts from the routed page's shortcuts under a context-less modal such as TmConfirm, or from a direct TmShortcuts.register page shortcut. An internal marker is needed and is unspecified.
- §1 places TmSupportRequestDetails and TmSupportDumpSummary in /support, and §12.5 says the details is 'markup only, TmDetailsKit' (spec 0031). /support must then import /details, but §1 and spec 0024 §1's import rules do not say /support may, and §1's 25 KB budget does not state whether /details is excluded (0028 §1 states such an exclusion explicitly for /attention-pages).
- §3.2 caps client string values at 64 characters, while §6.2 lets the relay accept 128 (message 256 vs the client's 200-character cap; stack 4,096 characters vs '4 KB'). This is not a conflict but needs one source of truth in tests: the relay tests should use §6.2's numbers and the client tests §3.2's.

**Slow or heavy tests (planning estimates):**

- §13 Playwright on Acme: full flows at 390/768/992/1440 px × en/ar (≈8 viewport-language combos) plus warm-launch, layout-shift and frame-probe specs; plausibly 6–12 min. Keep one width per language on PR; gate the full viewport matrix nightly.
- §14 'diagnosticsText() under 2 ms for 100 records' unit: timing-sensitive micro-benchmark, flaky on shared CI. Keep with a generous CI multiplier or gate behind a perf trait.
- §13 C# relay '429 from the rate policy': needs > 60 batches/min per user (spec 0016 §8.2); seconds if the window is injectable via TimeProvider/options. Keep.
- §13 C# support integration on real SQL incl. retention via 0020's core.file-retention: 1–3 min with fixture reuse. Keep under the integration trait.
- §3.3/§4.2 throttles (30 s, 10 min windows) and §11.1's 3 s no-frame timeout: must run on fake clocks; otherwise minutes. Keep with fake time only.
- §13 Manual: real capture on Chrome, Edge, Firefox and Safari desktop per release: not automatable; keep as a release checklist.

**External dependencies:**

- NuGet OpenTelemetry.Exporter.InMemory (not in Directory.Packages.props) for the relay's in-memory exporter test; the other OpenTelemetry, Azure.Monitor and Serilog packages already exist
- ASP.NET Core rate limiting (built-in) for tellma-telemetry
- SQL Server for the support suite: LocalDB on Windows, Testcontainers.MsSql on Linux (both already used)
- Playwright Chromium with clipboard-read permission granted; canvas.captureStream for tmFakeDisplayMedia; requestVideoFrameCallback/getDisplayMedia hints may need DOM type augmentation (preferCurrentTab, selfBrowserSurface, surfaceSwitching, monitorTypeSurfaces) in TypeScript ~6.0
- Git metadata at build time so the informational version carries +<40-hex sha> (SDK SourceLink default); CI archiving of source maps per UI version (owned by 0024 §2.6)
- Application Insights connection string only for manual verification; not needed by tests
- Real desktop Chrome, Edge, Firefox, Safari for the per-release manual capture check
- The Acme reference distribution host and client (0011, 0018, 0024) for e2e

## 0027

**Possible contradictions and gaps (unverified leads):**

- Appendix A and §6.2: tm.launcher.open (Mod+K) is registered by the tenant shell's launcher and also by the tenant picker ('the picker registers tm.launcher.open itself'). §4.3 refuses 'a combination that is already live', with a dev throw. TmLauncher is declared a root service of /shell (§9.2), so if it registers at construction, returning from a tenant to /tenants conflicts. The registration must be scoped to the tenant shell component's DestroyRef, and the spec does not say so.
- §4.2 step 5 versus §4.1: the modal rule admits 'the page shortcuts of the dialog's own context (spec 0026 registers those only while on top)', but TmShortcut carries no owner or context. A page shortcut of the routed page stays live while a context-less modal (TmConfirm, a slot drawer) is open, and the registry cannot tell it apart from the dialog's own. An internal ownership tag is needed.
- §9.3 item 2: recents chips use 'spec 0035 §9.2's icon for its kind'. The launcher thus depends on a table owned by the home-page spec, which is likely deferred. The implementer must lift that map into the primary or /shell early.
- §3.2: core.settings is 'visible with Read on any core.Settings.<category>' expressed through requires (TmSecurableRef[]). Settings categories are contributed per feature (spec 0032 §7.1), so the list must be computed from the securables section at runtime rather than written as a static requires array. This is under-specified.
- §4.3's platform family against an editable grid's type-to-edit (lead raised in grid key-map
  work). The grid seeds its editor only with a typed character, so Alt+Shift+<letter or digit>
  and Mod+Alt+Shift+<key> reach the registry on a US layout. Two cases are unverified:
  (a) Chromium on Windows may report AltGraph for a left Ctrl+Alt chord on a layout that has AltGr
  (German, French). If so, Mod+Alt+Shift+<key> there counts as typing and seeds the cell editor.
  (b) On Apple, Option+Shift+<letter> types a character (∏, Í) and seeds the editor by design.
  Check both in the Appendix A manual pass, and decide whether the platform family needs another
  chord on those layouts or platforms.
- §4.3's page family against the grid's row activation (lead raised in grid list-page work). Spec
  0010 §C.1 opens the active row in a new tab on Mod+Enter, so `TM_GRID_KEYS` now publishes
  `Mod+Enter` (only while `rowActivation="row"`, but the table is static). §4.3 refuses every
  `TM_GRID_KEYS` entry with a modifier, which makes the page family's `Mod+Enter` unregistrable
  everywhere. Either exempt `Mod+Enter` (the grid prevents it only when it acts, so §4.2 step 2
  already gives it to the focused grid), or drop it from the page family.
- §1 says /shell imports /attention, and §5.2 has both shells render <tm-banner-host /> from /attention. Spec 0028 §1 also lists TmArrivals and TmAttention as tenant-shell-rooted services in /attention, so the tenantless shell must not instantiate them; nothing says how /attention scopes those roots versus the banner host. This is minor, but it is an ordering detail for the 0027-c and 0028-d split.

**Slow or heavy tests (planning estimates):**

- §13 Matching: the '300-entry benchmark (index ≤ 5 ms, query ≤ 2 ms)' unit is a timing assertion that is flaky on loaded CI runners. Gate it behind a perf trait (nightly) or assert with a CI multiplier; keep the functional vectors on PR.
- §13 Responsive e2e: app bar, pinned bar, launcher and session menu at 390/576/768/992/1200/1440 px, 'cell by cell', in en and ar: roughly 12 viewport-language combos × 4 surfaces, plausibly 8–15 min. Run 2 widths per language on PR and gate the full matrix nightly.
- §13 axe battery: light/dark × LTR/RTL × 6 overlay states (launcher, menu, marker popover, pin popover, About, Shortcuts) ≈ 24 scans, 2–4 min. Keep on PR or nightly.
- §14 frame-probe and CLS specs (shell-feedback.spec, launcher-perf.spec, shell-layout.spec, launcher-hang.spec): timing-sensitive. Keep, with retries or quarantine on CI noise.
- §13 installed mode via Chromium --app: needs a headed or app-mode launch, sometimes unsupported in headless CI. Gate behind a trait if the CI image cannot run it.
- §13 Manual per release: iOS home-screen back/forward, Mod+K versus the browser in four browsers, every Appendix A key in Chrome and Edge on Windows with Arabic and English input languages, and ChromeOS. Release checklist only.

**External dependencies:**

- Brand SVGs brand/wordmark-on-dark.svg, brand/mark-on-dark.svg, brand/wordmark-on-light.svg (and -on-light mark) as the platform defaults in @tellma/core/brand/ (spec 0024 §7.6 owns them); real artwork must come from the brand owner, since no tellma-brand checkout exists beside this repo
- Noto Sans / Noto Sans Arabic fonts (already in the client devDependencies) for the font-load fit tests
- Playwright Chromium, plus --app mode for installed-mode tests; WebKit is optional for the Safari counter fallback
- Browser Navigation API (canGoBack/canGoForward); the fallback path is needed for Safari < 26.2
- The Acme reference distribution with tenants 1 (en+ar) and 2 (sandbox of 1)
- No new npm or NuGet packages
- PC-023 → the 0027 shell checkpoints: toasts clear the top bar through `--toast-offset-block-start` (default 8 px), which the shell sets. TmToasts keeps its top-layer host above modals by watching the CDK OverlayContainer's children; a shell surface shown as a top-layer popover outside that container would cover the toasts. TmBusyOverlay holds every key at window capture while a run is in progress, so shell shortcuts (Mod+K) are swallowed while busy, by design. It re-inerts only new `<body>` children: a dialog opened inside an existing OverlayContainer during a run is drawn above the scrim but stays inert.

## 0028

**Possible contradictions and gaps (unverified leads):**

- §6.2 versus §13 page-context row and spec 0026 §9.5: the bell's Stop confirms only for commitsInSteps handlers ('any other job stops without asking'), but the panel's overlay context registers attention.stop-job as 'destructive, confirmed'. Spec 0026 §9.5 requires views to invoke through the dispatcher, which always confirms a destructive action. So either the Stop button bypasses the dispatcher, a second path, or exports cannot stop without a confirmation. §10.5 also says cancel is destructive and 'always confirms' on the Jobs pages, which conflicts with the bell's no-confirm export stop under the same action. The planner should have the implementer pick one rule.
- §3.4 startup check 'assignments' verifies that every [Assignable] entity has Query and Details 'which TmUrls.target opens'. Spec 0029 §7 and spec 0024 §9.4 make TmUrls.target null unless the client declaration has a details member, which the server cannot check. Tray rows and the Assignments page's rowLink then produce no link for an assignable entity without a client details layout, and nothing flags it at build. Spec 0029 §3's tmCheckEntityDeclaration could add a check, but none is specified.
- §1 says /attention imports only the primary, and §10.2 has TmNotificationOpenPage (in /attention-pages) call TmAttention.read. /attention-pages therefore imports /attention, which §1 does not list, although 0024 §1's import rules probably admit it. The /attention-pages budget note excludes /search and /details but not /attention.
- §2.1 'Ids come from the sequence core.sq_Assignments START WITH 1000' gives no AS int, so SQL Server defaults it to bigint while Id is int (the statement casts). This is harmless below 2^31, but the migration should declare AS int for consistency with the int PK.
- §5.2 says the counts 'cap at 100 or above (spec 0021 §9.1 for the inbox, this spec's §3.1 for assignments)'. SummaryCountCap is in §3.1's AssignmentsOptions (100–1,000), so a deployment that raises the cap still shows '99+' from 100. That is consistent, but it means raising the cap changes nothing visible; this note is only to avoid an implementer 'fixing' it.

**Slow or heavy tests (planning estimates):**

- §12 Playwright on Acme at 390/768/992/1440 × en/ar: three-tab assignment flow, export/import progress and stop, offline banner, update banner, settings and test email. Multi-tab and job-driven flows are slow, plausibly 10–20 min for the full matrix. Run one width per language on PR and gate the full matrix nightly.
- §12 e2e '5 s offline while typing in a grid editor shows the banner only after typing stops': wall-clock dependent (5 s offline threshold plus 2 s idle gate), about 10 s each and flaky-prone. Keep, but drive TmApi.online through a test hook rather than real network loss.
- §12 fixture 'two concurrent first assigns raise 2601': needs two connections and a barrier; seconds; timing-sensitive. Keep, making it deterministic with explicit transactions and lock hints.
- §12 fixture 'a summary started while a seen is uncommitted returning the earlier SeenAt (RCSI on)': needs RCSI on the fixture database and an open transaction on a second connection. Keep, and make sure the shared fixture enables READ_COMMITTED_SNAPSHOT.
- §12 'a plan test that every summary statement seeks an open-row index': showplan XML assertions can differ between LocalDB and the SQL Server 2022 container. Keep, but assert on index names, not plan shape.
- §12 job details '30 s poll while the hub is connected' and the 5 s non-requester poll: must use fake timers in the harness; real time would cost minutes. Keep with fake time.
- §13 frame-probe and layout specs (attention-feedback.spec, attention-layout.spec, attention-hang.spec): timing-sensitive. Keep with retries.

**External dependencies:**

- SQL Server with READ_COMMITTED_SNAPSHOT enabled on the fixture database (LocalDB on Windows, Testcontainers.MsSql on Linux)
- SignalR hub and IClientEventPublisher from 0021 for live assignments.changed/inbox.changed in e2e; a fake publisher for the web integration tests
- Fake email sender (0007/0022) for the test-delivery e2e
- Playwright multi-page contexts (three tabs) with focus and visibility control
- No new npm or NuGet packages
- PC-023 → the 0028 TmBanners/TmToasts checkpoint: `tm-banner-bar`'s previous and next buttons are always enabled (the host wraps around), and `position.index` counts from 1. The hidden tone prefix reuses the `alert.info|warning|error` keys. The default overflow pill says "N more" (key `toast.more`, added by PC-023); 0028's "open the bell" pill goes through `provideTellmaToasts({ overflow })`. `TmToastRef.update()` re-announces only when the title, detail or tone changes.

## 0029

**Possible contradictions and gaps (unverified leads):**

- §1 'DisplayNames, which implements spec 0012's IDisplayNames' assigns the server renderer to this spec. Spec 0012's Goals ('Ship the display-name rule: [DisplayNameSource], EntityMetadata.DisplayName and IDisplayNames, the one server renderer') and spec 0012 §13's unit row ('IDisplayNames against spec 0029 §5.2's shared golden file') put it in 0012, which lands first and is consumed by 0015 §5.2.1, 0019 §4.1 and 0033. Ownership must be settled: implement DisplayNames with 0012 and keep only the client renderer and extra golden rows here. The golden lives 'in @tellma/core', a client package that does not exist until spec 0024, yet 0012's C# test reads it, so the file's path must be created early and be readable from test/core.
- §3: 'The tile, custom-filter and page-action types are spec 0030's, exported here', and spec 0024 §1 also lists TmTileDeclaration and TmCustomFilterDeclaration in the primary. 0029-b must therefore define types whose semantics are specified in spec 0030 before 0030 is implemented, and TmPlanRow (rowLink's argument) is defined in §6.2's /entity section but used by a primary declaration type. The implementer has to lift these definitions from 0030 and keep TmPlanRow type-only in the primary.
- §14.4 and §1: the picker binding 'import()s @tellma/core/search and /details' (0030 §17, 0031 §15). Those entry points do not exist when 0029 is built, so the dynamic imports fail to compile. The binding needs an injection seam (TmLazyPickerPage resolved through a token) or this part must land after 0030 and 0031. The spec does not provide the seam.
- §2.2 TmEntitySummary.segment is 'a child's query segment, null for read-only roots', while §2.1 includes read-only roots such as core.Blob and core.UserStamp in every emitted section. Spec 0027 §3.2 creates launcher entries for 'tmEntity with a query operation', and 0029 §3's build check reports entity-not-routable for section on an entity with no segment. That is consistent, but resolve() over a path through core.Blob must not assume a segment. This is minor and needs a test only.
- §10.1 says the editor implements spec 0004's TmCellEditor members (value, text, commit, cancel, focus, seed) and registers through TM_CELL_EDITOR_HOST 'as every tm-* control does'. That relies on 0010 §C.15's popup cell editors having landed in core-ui; if 0010 Part C is scheduled after 0029-g, the cell-editor half of the harness cannot run.

**Slow or heavy tests (planning estimates):**

- §17 e2e 'drag a field into a filter at the caret, then undo (Chromium, Firefox, WebKit)': requires all three Playwright engines, about 2–4 min. Keep Chromium on PR and gate Firefox and WebKit nightly unless CI already installs them.
- §17 typed codegen: compiling the emitted .d.ts and expect-error cases with tsc, about 10–40 s. Keep.
- §17 queryex/discover integration on SQL Server: 1–2 min with fixture reuse. Keep under the integration trait.
- §17 engine-generated lexer corpus and queryex-print-golden C# Discover check: fast to run, but regenerating the corpus is a tool step. Keep the check on PR and run regeneration manually.
- §18 geometry and frame specs (expression-editor-geometry.spec, column-filter-geometry.spec, discover-abort.spec, entity-language-switch.spec): timing-sensitive e2e. Keep with retries.
- §17 instants across Asia/Beirut DST and Riyadh: they need IANA zone data in the test runtime (Node/ICU full-icu, which is the default). Fast. Keep.

**External dependencies:**

- System.Text.Json.Schema.JsonSchemaExporter (built into .NET 9+/net10.0) for action-argument schemas; no new NuGet
- SQL Server for discover integration (LocalDB/Testcontainers)
- Playwright Chromium, Firefox and WebKit browsers for the drag/undo e2e
- Full ICU/IANA time-zone data and Intl.NumberFormat v3 string formatting (Node 24 and evergreen browsers) for exact decimal formatting and DST tests
- The TypeScript compiler (already present) for the emitted .d.ts compile test
- No new npm packages
- (D-046, from PC-019) The enum-name clash startup check (<entity schema>.<EnumTypeName>) covers only entity-property enums. When the 0029 emit lands, extend it to setting-key and action-field enums.

## 0030

**Possible contradictions and gaps (unverified leads):**

- §12.3 step 5 says every bulk action runs through `TmConfirmations.run` with the selection's ids. But `TmSearchPageActionDeclaration.run(search, ids): Promise<void>` (§3.1) takes no `{ confirmations, skip }` attempt, so a `selection: true` page action cannot take part in the resend loop or 'Continue without these'. The spec does not say whether page actions are exempt or whether `run` gains an attempt parameter.
- §12.3 step 2 says the page stays usable during a bulk action (busy item only). But spec 0031 §10.4 says the action dialog's `run` executes under the busy overlay, which makes the page inert, and §12.2 routes entity actions with arguments through that dialog. Which bulk paths take the overlay is left implicit. It also depends on the 0031 gap about what `TmEntityActions.run` itself wraps.
- §15.1/§15.2 vs spec 0025 §10: `TmPageState.remember` 'clears isStale'. The host remembers on destroy. If a stale entry is restored (§15.2 step 2) and the user leaves before the refresh read lands, the remembered snapshot holds the stale rows with isStale cleared, so the next return renders stale data with no read. The host should keep the stale flag, for example by not remembering until the refresh lands or by re-marking it, and the spec does not say which.
- §15.1 `ids` is 'the shown rows, in display order'. In a search tree that includes context (ancestor) rows. Spec 0031 §12 steps through `ids` and computes position as `(page − 1) × size + index + 1` against `result.total`, which counts matches only. So the details pager steps onto non-matching ancestors and shows wrong positions. Neither spec says whether a search tree's snapshot `ids` should exclude context rows.
- §16.2's example browse-tree select for Centers ('Id,Code,Name,Name2,ParentId,SubtreeCount,ActiveSubtreeCount,IsActive') omits `CenterType`, which `tmGlCenters.columns` (spec 0032 §6) shows. Golden tests built from the example would be wrong; follow §5.1's rule instead.
- §1 vs spec 0024 §1's entry-point table: 0024 lists only `TmTileDeclaration`, `TmCustomFilterDeclaration` and type-only `TmSearchSnapshot` in the primary, while §1 also puts `TmSearchPageActionDeclaration` and `TmColumnPreference` there. This is harmless, but the API golden must include all five.
- §1 says `TmImportResultView` ships in `/search` 'with the route', and spec 0032 statically imports `/search` from `/admin` only for it. That pulls the whole 16 KB search route chunk into the admin chunk graph, and no budget line covers it (spec 0024 §12 has no admin-route total).

**Slow or heavy tests (planning estimates):**

- §18/§19 e2e matrix: 'every flow in `ar` at 390, 768, 992 and 1440' plus @cross-engine on Firefox and WebKit, over about 15 flows (warm launch, type/paste/undo in three engines, keyboard-only open and return, tabs' three forms at four widths, row-end Columns while scrolling in LTR/RTL, tree expand + second tab, export download and background path, good/bad import, restricted role). Estimate 25–45 min for the full matrix. Recommend: on PR, Chromium en at 1440 + ar at 390; the full width × language × engine matrix nightly behind a `@matrix`/`@cross-engine` tag.
- §19 timing-sensitive specs: `warm-launch.spec` (chrome within 1 s with requests held), `search-feedback.spec` (a frame probe after each gesture and the first key), `view-toggle.spec` (zero requests). Each takes a few seconds but they flake on loaded CI agents. Keep `view-toggle` on PR (request count is deterministic). Gate the 1 s and same-frame assertions behind a `@perf` tag run nightly, or with retries.
- `search-geometry.spec` (box identity across typing, loading, selecting, filtering and an empty result at four widths): ~1–2 min, deterministic. Keep on PR at one width and run all widths nightly.
- §18 axe battery: every §5.3 state, every popup, selection mode, search tree and tiles × light/dark × LTR/RTL ≈ 40+ page states, ~3–6 min. Keep a smoke subset on PR and the full battery nightly.
- §19 'Non-blocking': spec 0024 §12's staging abort smoke needs a staging environment. Defer until staging exists.
- Harness suites with the fake clock (five keys 30 ms apart, the 250 ms hold, 500 ms announcement debounce) run in milliseconds under fake time. Keep them on PR.

**External dependencies:**

- Playwright 1.61.1 browsers (Chromium, Firefox, WebKit) installed on CI (`playwright install --with-deps`). axe-core (run in-page by client/e2e/support/axe.ts) and vitest are already in client/package.json (removed 2026-11-08 by PC-F05: the e2e axe floor runs axe-core directly through client/e2e/support/axe.ts).
- The full Acme e2e host (spec 0024 §13): SQL Server with the Acme database and dev data (spec 0018 §6.3), the multitenant web host (0011), the identity server (0003) for sign-in, blob storage for import files and export downloads (0017: filesystem or Azurite), and the background job runner (0020) for `export/start` and `import/start` (the background export e2e path).
- Lucide icon names that must be registered in the platform icon registry (0024 §2): table-2, layout-grid, list-tree, list, funnel, columns-3, inbox, search-x, circle-alert, refresh-cw, check, plus a chevron.
- English and Arabic copy for every `tm.search.*` string (ICU plurals, `tm.search.filterButton`, `tm.search.orderedBy`); e2e runs in `ar`.
- Browser APIs: `document.execCommand('insertText')` (deprecated but still the only undoable insert), with a `setRangeText` fallback; `history.state` persistence across reloads.
- No new npm packages: Angular 22 `resource()`, Signal Forms and router state already exist.

## 0031

**Possible contradictions and gaps (unverified leads):**

- §12.2 vs spec 0025 §10: every within-page step 'remembers' the search snapshot with the new highlight, and 0025 says `remember` 'replaces … clears isStale'. Scenario: open a record from the list, edit and Save (§8.3 step 3 `markStale` marks the list entry stale), then press Next. The step's `remember` clears the flag, and Back to the list renders the old rows with no re-read, so the edit is invisible. 0031 needs a stale-preserving update, such as recall + re-mark or a `TmPageState` patch method, and neither spec provides one.
- §12.1 step 4 position `(page − 1) × size + index + 1` with `[total]="result.total"` assumes `ids` are the page's matches. For a search tree (spec 0030 §16.4), `ids` include ancestor context rows and the edge read (§12.2 step 2) returns `ancestors` separately. The spec does not say how 0031 rebuilds `ids` in display order from a tree page read, or whether context rows are steppable.
- §10.4 leaves the layering of `TmEntityActions.run` vs `TmActionDialogContext.run` unspecified. Only the dialog's `run` is said to run 'under the busy overlay, through `TmConfirmations.run`'. §10.3 (activate) and §10.2 (delete) apply the overlay and confirmations at the call site, while spec 0030 §12.3 needs bulk runs without the overlay. `TmEntityActionOutcome` includes `declined`, which implies `TmEntityActions.run` wraps `TmConfirmations`. The implementer must decide whether `run` wraps confirmations, the overlay and the dispatcher's destructive confirm.
- §11.3 + spec 0024 §9.2 vs spec 0032 §7.1. The leave guard runs as the route's `canDeactivate`, and Angular also runs `canDeactivate` on a reused route whose path params change (default `runGuardsAndResolvers: 'paramsChange'`). The Settings tab click (`/settings` → `/settings/invoicing`, same route via the optional-param matcher) in dirty edit mode would therefore pop 'Leave without saving?'. 0032 §13 requires 'a first tab click … in edit mode keeping the host and the working copy'. The guard must exempt same-route param changes, or the settings route needs a custom `runGuardsAndResolvers`, and neither spec says which.
- §10.4 'Which actions appear': an argument-taking action with no `fields` and no `dialog` never appears and 'a development warning names them'. Spec 0032 §3 declares `preferences/set` and `preferences/delete` with `visible: () => false`, apparently to silence the warning, but the spec never says whether a declaration with `visible` suppresses the development warning.
- §3.1 says entity sources write through `TmApi.write(route, body, { confirmations })`, but `TmDetailsSource.write`'s change object carries `confirmations` and not `skip`. A details save never needs `skip`, but `TmConfirmations.run`'s `send(attempt)` passes both, so the source adapter must ignore `skip`. This is minor but undocumented.
- §1 budget: '/details weighs at most 23 KB' plus `/entity` 28 KB (spec 0029) plus the deferred editable grid must fit spec 0024 §12's 80 KB details route. No figure exists for the editable grid chunk, so whether 80 KB holds is unverified until 0031-c2.
- §17's e2e 'a reload into a build with a changed save contract' has no fixture mechanism in spec 0024 §13's e2e helpers (`warmUp`, `holdNetwork`, …) for serving a different Contracts chunk. A new helper is needed.

**Slow or heavy tests (planning estimates):**

- §17 e2e on Acme: about 13 flows × '390, 768, 992 and 1440 px in en and ar' × Chromium + @cross-engine Firefox/WebKit. Full matrix ≈ 25–40 min. Recommend: Chromium en/ar at one width on PR, the full matrix nightly.
- Timing-sensitive e2e: 'reload during a save (the savingAt notice, then the self conflict)' needs a held write and a precisely timed reload; 'Back during a save refused, history intact'; 'a two-context conflict and Overwrite' (two browser contexts). Each takes ~10–30 s and is flake-prone. Keep them on PR in Chromium only, with network holding through `holdNetwork`.
- 'step-up return' needs the real identity server's re-authentication round trip (spec 0003). It takes ~20–40 s and breaks if the identity server's step-up UI changes. Gate it to nightly.
- §18 rAF probes: `details-feedback.spec` (Edit, Cancel, New, next, Refresh change the screen in the next frame) and the 1 s warm-launch. Gate the strict frame assertions behind `@perf` nightly; harness equivalents (Edit sends nothing) stay on PR.
- `paging-aborts.spec` (three fast nexts, two aborted reads, one applied) is deterministic in the harness with a fake clock. Keep the harness version on PR and the e2e version nightly.
- 'a reload into a build with a changed save contract' requires serving two builds or a doctored Contracts chunk to the e2e host, which makes the setup heavy. Gate it nightly or replace it with a harness test that overrides `TmApi.fingerprint`.
- The axe battery across view/edit/loading/refreshing/error, strip states, every dialog, aside, error dots, compact header and disabled reasons × light/dark × LTR/RTL: ~3–5 min. Run a subset on PR and the full battery nightly.

**External dependencies:**

- Playwright browsers (Chromium, Firefox, WebKit). Two browser contexts for the conflict test.
- Acme e2e host as for 0030: SQL Server with Acme data, the web host, the identity server (sign-in and step-up for sensitive saves), and blob storage (0017) for image and attachment uploads, staged blob serving and attach on save.
- Signal Forms (`form`, `submit`, `applyEach`, `validate`, `required({ when })`) from @angular/forms 22.0.5, already in the repo. The host relies on reference-copy-on-write semantics for 'dirty by reference' (§4.2), so the implementer must verify them against the installed version.
- sessionStorage (drafts) and `history.state` (`tmFrom`/`tmPage`). `structuredClone` for the clone strip.
- Lucide icons: circle-check, sparkles, refresh-cw, lock and chevrons, registered in the icon registry.
- English and Arabic copy for `tm.details.*` and `tm.entity.*` (confirmAction, permissionNeeded, outsideFilter, ICU plurals in the summary).

## 0032

**Possible contradictions and gaps (unverified leads):**

- §7.1 says a tab click navigates `/{t}/settings` → `/{t}/settings/{category}` on the same route and 'Angular reuses the host, in edit mode too'. Spec 0024 §9.2 gives every page route a `canDeactivate` that runs `tmLeaveGuard` (spec 0031 §11.3), and Angular runs `canDeactivate` on a reused route whose params change (default `runGuardsAndResolvers: 'paramsChange'`). A dirty Settings page would therefore prompt 'Leave without saving?' on every tab click, which contradicts §13's harness case. One of the specs must exempt same-route param changes.
- §1 and §6 create `@tellma/module-gl` (folder `module/tellma-module-gl/`), but spec 0024 §2.2's Acme `app.config.ts` already calls `tmGlFeature()` and 0024 §14 requires `tellma-client build` to pass on Acme. If 0024 lands first, its checkpoint needs a stub `@tellma/module-gl` or must omit the GL feature until 0032-a.
- §4.7's `--tm-paper` token is defined in no spec. It also breaks the repo's token naming: existing tokens are unprefixed (`--surface-subtle`, `--grid-zebra-bg`, `--modal-footer-bg` in tellma-core-ui-tokens/css/tellma-default.css). A token name and owner must be chosen.
- §12.4 says this spec 'owns' the label chains, while spec 0014 §10's `ClientSecurable` (C#) already carries the resolved label key and fallback members. If 0014's contributor is built first without the chains, its golden changes in 0032. The planner should put the chain logic in the 0014 contributor checkpoint.
- §3 declares `{ name: 'preferences/set', visible: () => false }` and `{ name: 'preferences/delete', visible: () => false }`. Per spec 0031 §10.4 these SingleTarget argument actions without `fields` or `dialog` never appear anyway. The declarations' purpose (silencing the development warning?) is unstated, and spec 0030 §12.2 ignores `visible` on the search page.
- §9 sketch: `TmScheduleDetails` declares `private readonly d` and its template reads `d.entity()`, which fails under Angular strict templates since template-accessed members must be at least protected. `owner()` and `t()` are used but never defined, and §5.1's `TmRoleDetails` also uses an undefined `t()`. Sketch-level only, but an implementer copying it hits compile errors.
- §13's e2e 'a one-hour Auditor membership saved, then its buttons disabling for that user at its end' has no way to advance server time. Neither spec 0024 §13's e2e helpers nor the Acme host specify a server clock override.
- §8.2 Profile: 'A move that changed a profile member meets spec 0031 §9.1's self-case dialog'. If the move came from an administrator editing this user on `/users/{id}` (§4.1), the 409's `modifiedById` is the administrator, so the dialog shows the other-user case, not the self case. The statement holds only for the user's own edits in another tab.
- §1 '/admin imports `/search` (for the import result view)': statically importing the search route's entry point pulls its whole chunk graph into /admin's lazy load. The 31 KB budget is 'self-weight', and no route-total line exists in spec 0024 §12 for admin pages, so the cost is unbounded and unmeasured.
- §4.1 says the window editor's column 'is keyed `endsAt`' (camelCase) while every other column key in these specs is a PascalCase path (`EndsAt`, spec 0029 §6.1). The implementer must decide whether the grid column key is the json name or the path.

**Slow or heavy tests (planning estimates):**

- §13 e2e 'a one-hour Auditor membership saved, then its buttons disabling for that user at its end': as written it takes an hour of wall clock, or a server-side clock override that no spec provides. Defer, or gate nightly behind a test-only server `TimeProvider` hook in the Acme e2e host. Alternative: cover it in the harness, where the window text's next-boundary re-render already runs under a fake clock.
- §13 e2e Imports: 'a background import whose second chunk raises a warning, whose notice opens the Import page, where Confirm finishes it' runs the real job runner (0020), the notification hub and a multi-chunk workbook. Estimate 30–90 s per run, timing-dependent. Gate it nightly.
- §13 e2e Users: 'create, invite, Check delivery' needs the identity server and the email outbox/provider events (sandbox). ~20–40 s. Keep a Chromium-only smoke on PR and the full version nightly.
- §13 e2e Settings: two administrators in two browser contexts on the same key (conflict + Overwrite), and 'add a language, then its name field appears' (a settings save, then client/sync and descriptor rebuild). ~20–30 s each. Keep on PR in Chromium.
- §14 RTL: 'every §13 e2e in `ar` at 390, 768, 992 and 1440 px' × @cross-engine: about 20 flows × 8+ variants, an estimated 40–60 min. Recommend Chromium en + ar at one width on PR, the full matrix nightly.
- §14 timing: `admin-feedback.spec` (rAF probe after Edit, a tab click and F2 on a Filter cell) and the 1 s `warm-launch.spec` on /17/settings/invoicing, /17/me, /17/me/region and /17/roles/4. Gate behind `@perf` nightly.
- §13 axe 'every page, tab, dialog, notice and empty state; light and dark, LTR and RTL', estimated 60+ states, ~5–8 min. Run a subset on PR and the full battery nightly.
- Harness suites with fake time (5 s import poll, 600 ms locale debounce, the 250 ms filter check) are fast. Keep them on PR.

**External dependencies:**

- New client library project `client/projects/module/tellma-module-gl` (primary + `/centers`), registered in angular.json/pnpm workspace, with an API-extractor config and README.
- A new design token for the signature 'paper' box (§4.7 `--tm-paper`) in `tellma-core-ui-tokens`, light in both themes.
- Acme e2e host with: SQL Server (Acme DB, settings keys from 0018 §1.4), the identity server (0003) for sign-in, step-up and invitations; the email outbox (0022) with a sandbox/fake provider for invite delivery statuses; the background job runner (0020) and the SignalR hub (0021/0025) for import following and pending confirmations; blob storage (0017, filesystem or Azurite) for export files, import workbooks, user images, signatures and email attachments.
- A controllable server clock (TimeProvider override) on the e2e host if the membership-expiry e2e is kept.
- Browser features: sandboxed iframes with Blob object-URL `src`, the CSP meta in blob documents, Trusted Types compatibility (no `srcdoc`/DOMParser), Intl.Collator/ListFormat, `longOffset` time-zone names (ICU coverage differs by engine; the spec handles unknown ids).
- Lucide icons: users, shield, shield-check, calendar-clock, mail, file-down, file-up, network, scale, send, key-round, sliders-horizontal, play, user-check, rotate-cw, id-card, globe, sun-moon, bell, user-round, external-link, all registered in the icon registry.
- English and Arabic copy for `tm.admin.*` and the server resx keys this spec depends on (`SettingCategory_`, `SettingGroup_`, `Setting_<key>_Description`, `Securable_`, `Access_Problem_`, `Access_Reason_`, `Gl_Nav_Financials`, `Gl_Center_CodeHint`).

## 0033

**Possible contradictions and gaps (unverified leads):**

- §5.4 'Writes' prescribes 'one MERGE over a table-valued parameter for the grants set'. Spec 0012's analyzer TELLMA0003 refuses MERGE anywhere in IDataBatch.Sql and 0012 Decision 18 says 'never MERGE' precisely because of MERGE defects on temporal targets — and core.ReportShares/DashboardShares are temporal. Implement as DELETE + separate INSERT (new grants, ids from core.sq_ReportShares) + UPDATE (access changes) with TVPs, and flag the spec text
- §5.1 'Shareable<TEntity,TShare>()' is said to add a ShareableContributionItem that Tellma.Core realises, but spec 0011 §2.1's FeatureContributionItem list (ReportSeed/CodedReport/WidgetType items are listed) has no ShareableContributionItem — either 0011 needs the record or it lives in Tellma.Core.Abstractions.Sharing; decide placement
- §2.1 C# sketches give OwnerId/ReportId/UserId/RoleId/SharedById with 'FK →' comments but no [References] attributes; 0012 derives Queryex navigations (Owner, Report, Role, User) from [References] columns, and §5.1's startup check ('exactly one foreign-key navigation to TEntity'), §5.3's Via("Role")/Exists(Share, Row, …) and the clients' 'Owner' column (0034 §2.3, 0035 §12.1) all require them. Implementer must add [References(...)] on each FK
- §5.6/§6.1 vs 0035 §6.3: Report.InUse's `others` argument is 'the count of its other rows' and audience Through takes 'up to 20 dashboards', but DashboardsUsing returns at most 20 rows per report, so `others` silently caps at 15 (20 − 5 titles) for heavily used reports; 0034 §10.3 renders it as 'and N other dashboards'. Either uncap the count (separate COUNT) or say 'at least'
- §5.4 transfer: 'Their user grant, if any, is removed' — not stated whether the removal withdraws the new owner's core.report.shared notification as §7's grant removal does (WithdrawRequest). Decide
- §1 says the document's JSON Schema is exported by JsonSchemaExporter and 0034 §14 tests 'the wire mirrors against spec 0033's JSON Schema', but no checked-in artifact path or emission step (build target, test that regenerates) is specified — the cross-language seam needs a fixed location
- 0034 §14 says drill vectors live in 'drill-golden.json … shared with its C# tests', but 0033 §15 does not mention the shared golden nor where it lives (test/assets?), and the per-coordinate conditions of §9.4's table are built only on the client — define which side produces and which consumes the vectors
- §8.11/§8.3: a stored report with an owner that fails to compile answers 400 query-invalid, but a standard (seeded) report that fails to compile after a schema change also answers 400 (not 500) — inconsistent with §8.11's 'developer-only documents get 500' rule; minor, decide
- §15 Performance: '2 s on the CI SQL tier' — the tier is undefined; a wall-clock assertion on shared CI runners will flake
- Ordering trap with 0018 §7: Acme's InitialTenant migration is specified to already contain core.Reports/ReportShares (and the three dashboard tables). If 0018 lands first, its migration cannot include entities that do not exist yet — the migration must be regenerated (pre-release) or extended by an incremental migration when 0033-a/0035-b land; the parity test will catch drift either way
- Ordering trap with 0019 §12.2–§12.3: the core.export job's report case types ReportExportJobArguments over 0033's ReportExportRequest; 0019's implementer must stub/defer that case to 0033-h
- Ordering trap with 0016 §2.4/§8.2/§8.3: the Contracts chunk long/idempotent lists and the tellma-reports policy name reports/* and dashboards/audience routes that do not exist until 0033/0035 — 0016 tests over those routes must be deferred or written against fixture actions

**Slow or heavy tests (planning estimates):**

- §15 Performance (nightly, spec-mandated): pivot over 1M lines with a 6-level tree and two column dimensions under 2 s on 'the CI SQL tier', plus managed heap < 512 MB at the caps (250,000 leaf rows, 6-deep tree × 3 column dimensions, cells × partials at MaxPivotCellPartials). Seeding 1M lines via SqlBulkCopy ~1–3 min, runs ~10–30 s, total ~2–5 min. Recommend: gate behind a Performance trait, nightly only; do not assert the 2 s wall-clock on LocalDB/Testcontainers (record and alert instead) since the 'CI SQL tier' is undefined
- §15 Sharing: 'a role permission with more than 500 holders counted exactly' — inserting 500+ users/memberships, ~5–15 s. Keep on PR
- §15 Runs: 'every cell of a two-tree pivot equals a brute-force descendantOf query per measure' — one query per cell×measure; keep the fixture small (tens of cells), ~5–20 s. Keep
- §15 Runs: 'an abort reaches SQL' — timing-sensitive (needs a long-running statement and polling sys.dm_exec_requests). Keep, but make deterministic (WAITFOR/large cross join + bounded poll)
- §15 Export: 'a multi-page export totals every page' and MaxTake+1 cases need >10,000 rows at defaults — run with lowered ReportsOptions.MaxTake/MaxSynchronousExportRows in tests (seconds). Keep
- §15 Cube builder caps incl. MaxPivotCellPartials (5,000,000 partial slots at defaults) — run with lowered caps in unit tests; the at-default memory check belongs to the nightly perf suite
- Background export (§15 Export) runs a real job through 0020 + blob storage — ~5–15 s per case. Keep

**External dependencies:**

- SQL Server (LocalDB on Windows, Testcontainers MsSql on Linux): temporal tables, filtered unique indexes, sequences + sp_sequence_get_range, TVPs/UDTTs, hierarchyid (tree dimensions/descendantOf), errors 8134/8115, RCSI (as provisioned by 0011)
- An undefined 'CI SQL tier' for the nightly perf assertion (§15) — needs a decision (dedicated Azure SQL / fixed-size container)
- No new NuGet packages (§1 states none): System.Text.Json JsonSchemaExporter and SHA-256 are BCL; DocumentFormat.OpenXml arrives with 0019; ModelContextProtocol.AspNetCore with 0016 (Tellma.Core.Mcp)
- Azurite/blob storage for the background export file (via 0019/0017) in integration tests
- Acme reference distribution projects (Tellma.Distro.Acme, its Migrator and seed data) from 0018

## 0034

**Possible contradictions and gaps (unverified leads):**

- §14 'drill-golden.json (… shared with its C# tests)' — no path, owner or format is defined, and 0033 §15 never mentions it; the per-coordinate conditions (§8.1 step 3) are built only on the client, so it is unclear what the C# side asserts. Define the file location (e.g. test/assets/reports/drill-golden.json) and each side's role before 0034-e
- §14 'the wire mirrors against spec 0033's JSON Schema' — 0033 §1 only says JsonSchemaExporter exports the records' schema; no checked-in artifact or regeneration step exists for the client test to read (see 0033 gaps)
- §1 places TmReportsPage and TmReportPage in @tellma/core/reports and the drill list's crumb/chips 'in @tellma/core/search, to this spec's rules' — the 0030 implementer must leave a seam for list-mode crumb/chips sourced from history.state.tmDrill, otherwise 0034-e must amend 0030's code
- §4.1 TmReportParameter.type is required, but 0033 §4.1's stored parameter type is optional until save; the studio's preview host has 'its own bar' (§12.2) over an unsaved inline document whose parameters may still be untyped/unconfirmed — the bar's control choice for an untyped parameter is unspecified
- §2.3 tmReports columns include 'Owner', which needs the derived Owner navigation from a [References] OwnerId (0033 sketch omits the attribute; see 0033 gaps)
- §1 budgets assume a @tellma/core primary of 0.6 KB for reports' declarations; tmReports with tabs/actions/clone and tmReportStudioPage plus TM_APP_DECLARATIONS merge logic for coded reports may not fit 0.6 KB gz — verify early in 0034-c
- §14 says 'Every suite runs on PR' including cross-engine, all widths and visual baselines — conflicts in practice with CI time; needs an explicit PR/nightly split (recommendation above)

**Slow or heavy tests (planning estimates):**

- §14 'E2e on Acme (Chromium; Firefox and WebKit @cross-engine)' with 'every e2e in ar at four widths' (390/768/992/1440) — the journey set × 2 languages × 4 widths × up to 3 engines; estimate 15–30 min Chromium-only, 45–90 min all engines. Spec says every suite runs on PR. Recommend: PR = Chromium, en at 1440 + ar at 390; nightly = full width/language matrix and cross-engine
- pivot-sticky.spec: 5 host widths × 3 attribute counts × 2 directions = 30 cases (~1–2 s each, ~1 min). Keep on PR (Chromium)
- chart-bidi.spec on all three engines with pixel comparison of mixed-direction labels — ~2–4 min, engine-sensitive. Keep Chromium on PR, gate Firefox/WebKit to nightly (@cross-engine)
- Visual snapshots: tm-chart per type and tm-pivot styles in light/dark/RTL with pinned fonts and DPR 1 — baselines are platform-sensitive; recommend running only in a pinned Linux container (nightly or a dedicated job), never on Windows dev machines
- Timing-sensitive frame-probe specs: report-feedback.spec, run-supersede.spec, reloading-bar ≥250 ms harness test — keep, but use fake clock where possible and tag for quarantine if flaky
- warm-launch.spec and budget checks need a production build of the Acme client (~2–5 min build) — share the build with 0024's suite; keep on PR
- axe on every §3.3 state, the table view, each dialog and each studio section in two themes — moderate (~2–5 min); keep on PR

**External dependencies:**

- npm: echarts (new; pin an exact version ≥2 weeks old, e.g. 5.6.0 or 6.0.x; zrender comes transitively) imported only via echarts/core modules; must fit 0024 §12's 170 KB gz engine line
- Fonts: visual and chart-bidi tests need pinned fonts including an Arabic face — tellma-core-ui/fonts currently ships only Noto Sans Latin/Latin-ext and Noto Sans Mono; an Arabic font (e.g. Noto Sans Arabic, OFL) must be added or provided by 0023/0024
- Lucide icon names used must exist in the tm-icon registry (0023 §3.1): list, sigma, grid-3x3, chart-column, sliders-horizontal, circle-alert, circle-help, circle-pause, file-plus, wrench, inbox, refresh-cw, etc. — no icon registry exists in core-ui yet
- Playwright browsers Chromium, Firefox, WebKit (@playwright/test 1.61.1 already in client/package.json)
- Live server stack for e2e: Acme distro (0018) with SQL Server, blob storage (Azurite) for exports and image cells, background jobs (0020), notification hub (0021) for the share journey

## 0035

**Possible contradictions and gaps (unverified leads):**

- §3.1 Dashboard's Stack(Operations = Query | Details | Save | Delete) omits Enlist, yet §7.3 says a later dashboard seed step 'needs no change here'; 0033 §13's seed steps upsert through an enlisted save, so a future dashboard seed would need Enlist added to the stack
- §3.1 DashboardWidget.Title ([Multilingual] + Title2/Title3) has no stated length ('nvarchar at the stated lengths'), while Description is ≤ 1,024; presumably 255 like other titles — state it
- §3.1 sketches carry 'FK → …' comments without [References] attributes on OwnerId/DashboardId/UserId/RoleId/ReportId; 0012 derives navigations (Owner, Dashboard, Report, Role) only from [References]/[ParentKey], and §12.1's 'Owner' column, 0033 §5.1's startup check and 0033 §5.3's Via/Exists need them
- §6.3 DashboardsUsing returns at most 20 rows per report, but 0033 §5.6's Report.InUse 'others' is described as the count of the other rows — an undercount beyond 20 dashboards (see 0033 gaps)
- §3.3 and §11.1 claim a 24-tile dashboard's first runs never meet a 429 (6 running + queue 24 in the per-user batch lane), but the lane is per user across tabs: a second open dashboard, a home hatch with report widgets, or a present-mode display under the same user shares it. Review flag 5 acknowledges it; the absolute 'none meets a 429' wording is too strong — tiles must render 429 via the host's error state
- Ordering trap: 0018 §7's InitialTenant migration lists Dashboards/DashboardWidgets/DashboardShares (with FK_DashboardWidgets_ReportId → core.Reports), so either both 0033-a and 0035-b entities exist before 0018's migration is generated, or the migration is regenerated/extended later
- Ordering trap: 0024's tenant root route (0024 §9.2, 'The tenant root loads 0035's TmHomeHost') and its build tool's widget-missing/widget-not-registered checks require 0035-a's server registry and client declarations; 0024 must ship a placeholder home or 0035-a must follow 0024 closely

**Slow or heavy tests (planning estimates):**

- §17 'Playwright on Acme' at 6 widths (390, 768, 992, 1440, 1920, 2560) × 2 languages on PR — ~12 passes over the journeys; estimate 20–40 min. Recommend PR = 1440 en + 390 ar (+1920 for the width-cap assertion); full 12-config matrix nightly
- Silent refresh 'under Playwright's clock' and scheduler e2e (refresh-scheduler.spec) — fast with clock control; keep
- dashboard-hang.spec ('hung runs never delay navigation') and dashboard-feedback.spec (frame probes, no running transitions under both prefers-reduced-motion values) — timing-sensitive; keep on Chromium PR, tag for quarantine
- home-geometry.spec (assignment arrives over the SignalR hub, zero layout shift) — needs the live hub; ~10–30 s; keep
- axe battery over home, view, present, edit, gallery, dialogs × light/dark × LTR/RTL — ~3–5 min; keep on PR at one width
- Server integration suites (Dashboards, Web) — normal SQL integration speed; keep on PR

**External dependencies:**

- SQL Server (LocalDB/Testcontainers) as for 0033: temporal tables, filtered unique indexes, sequences, check constraints
- Browser APIs: Fullscreen API, Screen Wake Lock API (unsupported/partial in some engines — tests must stub or accept silent refusal), ResizeObserver, CSS container queries, Intl.DateTimeFormat with time zones and the Umm al-Qura calendar
- SignalR hub (0021) for live assignment updates on home in e2e; no scale-out/Redis needed for single-instance CI
- Playwright browsers; Acme distro + client app (0018/0024) for e2e
- No new NuGet or npm packages (§1 'adds no dependency between projects or packages'); icons layout-dashboard, presentation, puzzle, lock, sliders-horizontal, history, send, wifi-off, inbox, wallet, refresh-cw, x must exist in the tm-icon registry

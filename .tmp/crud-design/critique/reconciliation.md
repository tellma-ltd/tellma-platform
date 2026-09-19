# Reconciliation — specs 0010–0020 (2026-09-04)

Inputs: `spec-style.md`, `notation.md`, the eleven `specs-fix-*.md` logs, the Cross-spec sections of `specs-review-A.md` and `specs-review-B.md`. Every deferred item was verified against the owner spec with Grep and a range Read; the ones below were applied in place, the rest were already satisfied.

## Changes applied

### 0010 (6)
- §2.2 `FeatureContributionItem` block — added `StackCompanionContributionItem(EntityType, CompanionType)` (item f).
- §2.2 `FeatureContribution` block — added the `EntityCompanion<TEntity, TCompanion>()` sugar; Members row for spec 0014's items names `EntityCompanion` and `StackCompanionContributionItem` (item f).
- §3.6 `RequestContext.Client` remark — closed set now `web | cli | mcp | worker` (spec 0015's client set) (item j).
- §6.1 `migrate` row — runs spec 0013's `IPermissionDriftScanner.ScanAsync` per tenant after membership reconciliation and prints the items (A-0013-5 deferred to 0010).
- §6.2 platform steps — `core.bootstrap-administrator` calls `BootstrapAdministrator(TenantBootstrapRequest(Email = AdminEmail, Name = AdminEmail's local part, PreferredLanguage = the distribution default, Subject = AdminSubject))`, matching spec 0013's record and spec 0017 §6.1 (B cross deferred to 0010).
- §1.4 Web `Program.cs` illustration — six lines reduced to five (style sweep).

### 0011 (5)
- §5.2 `UpdateSpec` — added `AllOrNothing: bool = false` (item c).
- §9.1 — `AllOrNothing = true` semantics: existence over every id (missing ids as a result set, `THROW 50404, N'Entity.NotFound', 1`), then visibility (`COUNT(@tb5_keys) = COUNT(@tb5_t0)`, `THROW 50403, N'RowSecurity', 1`) before the `UPDATE`, per the delete-by-ids rule (item c).
- §7.4 error table — `50403` and `50404` rows name the all-or-nothing `Update` (§9.1) as a source (item c).
- §5.5 step 6 — persist assembly names spec 0013's `IAccessGuards.ContributeLock` (first) and `IAccessGuards.ContributeInvariants` (after the `ContributeAsync` statements, before the post-check) (item b).

### 0012 (3)
- §3.4 Projection — `GetAllCachedAsync(DetailsRequest)` and the `<entity>/all` route with body `AllRequest(Select, Include)` (item e).
- §5.7 Provisioning — the step runs in the migrator's step-runner scope (`ITenantScopeFactory.CreateScopeAsync(snapshot, allowNonActive: true)`, `Kind = System`) (item g).
- §12 integration suite — `fixture.Lookups` is added to spec 0011's shared project `test/shared/Tellma.Testing.Entities` (item o).

### 0014 (6)
- §6.7 step 4 — the guards are `IAccessGuards.ContributeLock(batch)` (after the executor's re-checks, before the emitter's first statement) and `ContributeInvariants(batch)` (after every `ContributeAsync` contribution, before the post-check); the trigger is the batch's declared writes known from metadata (item b).
- §11.1 — the all-or-nothing `Update` checks existence (`50404`, missing ids as a result set) then visibility (`50403`) before the `UPDATE`, matching spec 0011 §9.1 (item c).
- §18.2 — the fixture schema lives in the shared project `test/shared/Tellma.Testing.Entities`; `fixture.Tasks`/`Task` renamed `fixture.Shipments`/`Shipment` per spec 0011 §13.2 (item o, A-0011-5).
- §19 Projects — the fixture additions go to `TellmaFixtureDbContext` in `test/shared/Tellma.Testing.Entities` (item o).

### 0015 (3)
- §4.4 — an action method may live on a stack companion (spec 0014's `EntityCompanion`); the endpoint invokes `ActionDescriptor.HandlerType` (B cross (c) deferred to 0015).
- §5.1 — the `Hub` group and the blob `GET` drop the `Tellma-Client` requirement; the blob upload `POST` keeps it (item p).
- §5.3 rule 1 — `Tellma-Client` is required on the `Web` group and the blob upload `POST`; the GET and the negotiate stay the only exemptions (item p; §9.1 already agreed).

### 0016 (5)
- §7.4 — `core.blob-reconcile` cron `0 2 * * 0` (Sunday 02:00), citing spec 0019's built-in schedule table as the record (item a).
- §6.4 — `BlobContainerStep` runs in the migrator's step-runner scope (`CreateScopeAsync(snapshot, allowNonActive: true)`) (item g).
- §4.4 and Review flag 20 — "spec 0014 §13.1" replaced by the `ISaveEffect<T>.ContributeAsync` participation rule of spec 0014 (style sweep: sibling section references).
- §10 integration suite — `fixture.BlobOwners` is added to spec 0011's `test/shared/Tellma.Testing.Entities` (item o).

### 0017 (1)
- §5.6 `GlSampleCentersStep` — runs after the platform steps in the migrator's step-runner scope (`CreateScopeAsync(snapshot, allowNonActive: true)`, `Kind = System`) (item g).

### 0018 (2)
- §1.1 tests row — integration tests use spec 0011's fixture entities in `test/shared/Tellma.Testing.Entities` (item o).
- §13 pure suite — fixture names aligned to spec 0011 §13.2: `fixture.Widgets` (multilingual) with children `fixture.WidgetParts`, `fixture.Nodes`, the `long`-keyed `fixture.Shipments` (was `fixture.WidgetLines`, unnamed entities) (item o).

### 0013, 0019, 0020 (0)
No change required.

## Deferred items found already satisfied

- (a) 0019 §7.9 already `0 2 * * 0` for `core.blob-reconcile` and `0 1 * * 0` for `core.tree-verify`; §14.5 agrees.
- (b) 0013 §8.3 defines `ContributeLock`/`ContributeInvariants` with placement; 0017 §3.8 cites `IAccessGuards` by name only.
- (d) `@tb{b}_main` appears in no spec; 0011 §1.4, 0014 §5.3, 0017 §4 use `@tb{b}_keys` / the opaque `IdsSource`.
- (e) 0015 §3.3/§4.2 `AllRequest` → `GetAllCachedAsync(DetailsRequest)`; 0014 §4.1 signature agrees; `IdsRequest.Arguments` kept in 0015 and 0014 takes `ids`/`arguments` separately.
- (f) 0014 §11.3 mechanism and 0018 §1.2 `contribution.EntityCompanion<TEntity, ExcelOperations<TEntity>>()` agree.
- (g) 0010 §4.4/§6.2 define `allowNonActive`; 0019 §8 worker scope uses `CreateScopeAsync(snapshot)` (default `false`), §6.3 the system scope.
- (h) `15 core.settings` present in 0010 §6.2, 0012 §5.7, 0017 §6.3.
- (i) 0012 §7.1 `EntityLabel(entityType, plural: bool = false)`; 0018 §3.3 uses `plural: true`.
- (j) 0015 §6.1/§10.1 list `worker`; 0019 §8 sets `Client = "worker"`.
- (k) `PartialFailureException(Code, Results, Failed)` identical in 0014 §14.1, 0015 §7.1, 0017 §3.3.
- (l) 0020 §2.4 catalogue: `core.user.added { tenantName, actorName }` raised by 0017 §3.3 (no registration there), 0018's three types with `{ fileName, rowCount }`, `{ fileName, rowCount, errorCount }`, `{ fileName, errorCode }`, 0019's four with matching arguments; the type-key grammar in 0020 §2.3 equals 0019 §3.7.
- (m) 0019 §10 and 0020 §1.1/§6.4: `INotifier` always present, `IClientEventPublisher` → `NullClientEventPublisher` via `TryAdd`; 0010 states no conflicting composition.
- (n) 0011 §1.3 and 0010 §3.5 both set `Application Name = DeploymentIdentity.DeploymentId`.
- (o) 0011 §13.2 and (after the fix) 0014 §18.2 agree on `test/shared/Tellma.Testing.Entities`; 0013 names no fixture entity.
- 0012 §2.5 publishes `cache.changed`; 0020 §6.3 cites it. 0012 §6.3 calls `ITenantCatalog.RenameAsync` post-commit. 0015 §2.4 lists `notification-preferences/get`. 0017 §3.3 passes `actorName`. 0019 §14.1 restates `[JobHandler("core.export", MaxAttempts = 3)]` from 0018 §12.3.

## Drift sweep (names)

Every listed name grep-checked across the eleven specs; one spelling per concept found for `IDataBatch`, `BatchPurpose` (`Read | Validate | Persist | Maintenance`), the `TellmaSqlErrors` numbers (50401, 50403, 50404, 50409, 50412, 50413, 50422, 50428, 50503, band 50600–50699), `ConcurrencyMode = Check | Override`, `SaveOptions`, `EntitiesResult` (identical member sets in 0014 §4.3 and 0015 §3.4), `QueryResult`, `AffectedResult`, `JobAccepted(JobId, ResourceId)`, `RequestContext`, `ITenantScopeFactory`, `IUserConnector` (`Connect`, `ConnectAsUser`, `ConnectAsSystem`, `Apply`), `IGuardedBatchRunner`, `IAccessEvaluator`, `IAccessGuards`, `ISecurableRegistry`, `core.VersionTags`, `core.UserStamps`, `PreferencesTag`, `PermissionsTag`, the four `Tellma-*` headers, `IBlobService`, `IBlobStore`, `[BlobReference]`, `IJobQueue` (`Enqueue(batch, …)`, `EnqueueAsync`), `INotifier`, `IClientEventPublisher`, `IJobProgress` (`Report`, `Flush`, `Append`), `IJobEntity`, the `core.*` tables, `ITenantProvisioningStep`, `dbo.__TellmaProvisioning`, `WellKnownIds`, `StackDescriptor`, the attributes, `EntityService`, `MultilingualShape`, `KeySetRestriction(Path, TableSource)`, `TreeEntity`/`ActivatableTreeEntity`, and the package names. The only shape drift found was `IAccessGuards.Contribute` (0014, fixed above).

## Style sweep

- Fenced `csharp` blocks: sixteen, all labelled *Illustration*; one (0010 §1.4 Web `Program.cs`) was six lines and is now five.
- `/// <`: none.
- Process words (ledger, seams.md, theme, brain dump, briefing, synthesizer, reviewer): none about the exercise (the one "ledger" is the GL sense in a 0014 illustration).
- Sibling references by section number: three in 0016 (§4.4 body, §7.4, flag 20), all replaced by the contract name; none elsewhere.
- ARCHITECTURE.md: named only in Definition-of-done Docs bullets, never by section.
- Mandatory sections (Context, Goals / Non-goals, Testing, Definition of done, Decisions record, Review flags): present in all eleven.

## Not reconciled

Nothing outstanding in the specs. The `seams.md`/ledger deltas the fix logs list (seams §1, §3.3, §8.3, §9, §11, §12, §13.1, §14, §15, §18, §19, §20, §21, §22) are working-file updates outside the eleven specs and were not touched.

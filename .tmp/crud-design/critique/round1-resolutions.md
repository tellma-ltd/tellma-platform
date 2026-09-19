# Round 1 — resolutions

Applied 2026-09-04 to `ledger.md` and `seams.md`. One line per finding, keyed by critique file and number. "Applied" = the text now states the fix (where the fix was itself a judgment call, a review flag S26–S37 was added in ledger §5). "Rejected"/"Deferred" carry the reason.

## round1-completeness.md

- **C1** Applied. Composition contract of `UserAccess.Decide` (union of memberships, public roles, bespoke criteria; write implies read with the same filter; `Or` of matching filters; criterion alone = `Filtered`) in seams §11.2 and ledger seam 11.
- **C2** Applied. Two-stage pre-check specified: RT1 before images under the caller's `Read` filter (absent → 404) plus a `Count` under the `Save` grant (short → 403), compared in C#; `50404` is now used by the delete-by-ids statement (seams §1.5, §1.6, §3.3; ledger seam 11).
- **C3** Applied. Fallback policy denies, per-group `RequireAuthorization(TellmaPolicies.<Surface>)`, `AllowAnonymous` only with `DeployableEndpointMetadata`, startup audit (seams §13.2; ledger §4.4.5).
- **C4** Applied. Ledger seam 1 now carries `Save(rows, concurrency)`, `DeleteSpec.ByQuery(…, Cap)`, `ByIds(…, ExpectedStamps?)`; data-level `SaveOptions` removed; errata 0011 #12–13 rewritten; §2.26 states the service-level record.
- **C5** Applied. `Run<TResult>(purpose, compose, read)`; `ConnectPremises.ExpectedUserPermissionsTag`, `ConnectResult.UserPermissionsTag`, `UserAccess.UserTag` in ledger seam 16 and errata 0013 #11/#20.
- **C6** Applied. `<Area>TelemetryNames` holders throughout the ledger (seam 14, errata 0011 #18, 0018 D1, 0019 §3.1); `IdentityInvitationStatus` → `InviteStatus` (seam 21, errata 0017 #3).
- **C7** Applied. `ImportRequest.Concurrency`, `ExportId`/`ImportId`, `RequestContextSnapshot.Client`, `SchedulePausedReason`, `IQueryexSchemaProvider.Fingerprint`, `DeleteByIdsAsync(ids, expectedStamps?)`, `50401` placement all ratified in the ledger's seam entries; ledger §3 intro adopts every §24 item; errata 0018 D9 rewritten (see S14).
- **C8** Applied. `Role.IsPublic` mechanism stated in ledger §2.29 and seams §11.3.
- **C9** Applied as a deferral (S35): packs declare no language support this release; the compliance-module question is closed as "no restriction" (ledger §7). Rejected the alternative of adding `FeatureDeclaration.SupportedLanguages` now — a warning nobody acts on is contract surface without a consumer.

## round1-security-correctness.md

- **S1** Applied. Three id sets per root (`@tb{b}_new`, `@tb{b}_saved`, `@tb{b}_touched`); root `INSERT` outputs into `@tb{b}_new`; post-check over `@tb{b}_saved`; `PersistContext.NewIds/SavedIds/TouchedIds`; conformance tests named (seams §1.3, §1.6, §3.3; ledger seam 1).
- **S2** Applied (preferred fix; flag S27). `core.Jobs`/`core.Notifications` rows take ids inside their statements via `sp_sequence_get_range`; `Notify`/`Enqueue` stay synchronous; `Enqueue` returns `BatchResult<list<int>>`; `JobRequest.Entity` lets the statement set the owner's `JobId`; `JobRequestList` and `NotificationRowList(Ordinal, UserId)` (seams §1.2, §1.8, §8.1, §8.3, §15; ledger §2.8, seams 8/15, errata).
- **S3** Applied with one deviation (flag S34): the default projection is stated (`Id`, `Name` group, `Code`, avatar columns) and Core's `User`/`Role` rely on it rather than carrying a redundant `[RelatedSelect]`; the gate warning and the conformance test are stated (seams §2.2; ledger seam 2).
- **S4** Applied (flag S37). `SqlOptions.UserIds` + `ForCaller(writes)`; user rules keyed by table with fixed rules for the two preference tables; `IVersionTagRegistry.Resolve(writtenTables)`; startup gate requirement; the invite write-back and self-service statements declare it (seams §1.1, §5.1, §5.3, §11.4, §15, §21; ledger §2.18, seam 5).
- **S5** Applied (flag S26 restating S4). The in-transaction guard reads the tenant rows and the caller's `UserStamps` row `WITH (REPEATABLEREAD, ROWLOCK)` and compares both levels (seams §1.6 step 4, §5.3; ledger §2.24).
- **S6** Applied as the bounded variant (flag S28): `AccessOptions.FastDenyWindow = 5s`, `UserAccess.ValidatedAt`; the alternative of dropping `TryDenyFast` rejected because the cheap deny on the common "no permission" path is worth keeping when correctly bounded.
- **S7** Applied. `COMPLETE` row-count fence (`Job.LeaseLost`) and ordering as the first statement of its transaction (seams §8.3; ledger §2.9).
- **S8** Applied. Tick step 2 joins `ScheduleStates` on the tick lease and asserts it first (`Schedule.TickLeaseLost`).
- **S9** Applied (flag S30). `[JobHandler].Schedulable` (only `core.export`), `Schedules.HandlerNotSchedulable`; ownership changes only through the explicit `take-over` action (`Schedules.OwnedByAnotherUser` on a non-owner's change to run-defining fields). Chose the explicit action over a `TakeOver` request flag because the save wire shape has no per-request option slot and an action is visible on MCP.
- **S10** Applied. `SaveOptions.SelfService = true`: `[SelfEditable]` SET list, child collections forced to `null`, `Users.SelfServiceOnly`; test stated (seams §3.2, §21; ledger §2.26, §2.29).
- **S11** Applied. `ReadOnly` defined; `ConnectPremises.AllowStateFlip`, `@tm_AllowFlip`; conformance row (seams §9, §16; ledger §2.24, seam 9).
- **S12** Applied. `core.session-sweep` dropped; `SessionSweepService` hosted timer in `Tellma.Core.AspNetCore`; jobs/schedules tenant-scoped only (seams §8.1, §13.2, §23; ledger §2.9, seam 8, §4.1.8, errata 0010 #8, 0019 D2).
- **S13** Applied. `IEntityPipeline<,>`/`IEntityBehavior<,>` public in Abstractions, not-for-distributions; constructor injection (seams §3.2, §23; ledger §2.26, errata 0014 #5a).
- **S14** Applied. Hydrated rows always `Check` with the hydrated `ModifiedAt`; `ImportRequest.Concurrency = Check` by default; `Override` only on request; `Excel.Import.ConcurrencyConflict` (seams §19; ledger seams 13/19, §4.9, errata 0018 D9).
- **S15** Applied (flag S33, settles 0011 flag 5). T3b/T5a/T5b capture old nodes and recount only affected rows and their old/new ancestor chains; whole-table form is the weekly `core.tree-verify` job (seams §1.4; ledger §2.10).
- **S16** Applied. Every cache key begins with the tenant id; `UserAccess.TenantId`; `Invalidate(tenantId, userId)`; connect cache `(TenantId, Subject)`; fixture test (seams §5.1, §11.2, §16; ledger seam 5).
- **S17** Applied. `TellmaSqlErrors.Transient = 50503` for `Access.LockTimeout`, retried then `DependencyUnavailableException`; `IAccessGuards` runs for every persist whose writes meet a security table (seams §1.2, §1.6, §10, §11.2; ledger §2.20).
- **S18** Applied. `Export`/`Import` are `[Stack(Operations = Query | Details | Delete)]`, every column server-owned, written only with `SaveSource = System` (seams §19; ledger seams 3/19).
- **S19** Applied in part. `ConnectAsSystem` throws unless `RequestContext.Kind = System` (seams §16; ledger §2.24). The analyzer for calls outside `Tellma.Core` is deferred (ledger §7): the runtime check closes the hole and a new analyzer id is surface the specs need not carry now.
- **S20** Applied. Capture rule stated: one `OUTPUT … INTO` per statement; per-statement `@tb{b}_cap{n}` carrying every captured column when two sets need it; weak-owner blob kinds remain deferred for the securable reason only (seams §1.3; ledger seam 1).
- **S21** Applied. `ResolveAsync` by state; `private, no-store` for a staged preview, `private, max-age, immutable` for committed (seams §12.1, §12.3; ledger seam 12).
- **S22** Applied. A `50409` whose conflicts are all `'M'` → `NotFoundException`; 409 when any `'C'` (seams §1.3, §10; ledger §2.20, seam 10).
- **S23** Applied. `ByIds` SQL: `50404` then `50403` then optional `50409` with `IdStampList`; `50428` for `ByQuery`'s `ExpectedCount` (seams §1.5, §1.8).
- **S24** Applied. `Settings` declares `settings` only; `IDataBatch.BumpVersionTag(name)`; `SettingsService.Save` bumps `permissions` on a language change (seams §1.1, §11.3, §22; ledger §2.18, seam 4).
- **S25** Applied (flag S29). Built-ins: `IsActive`, policies, arguments, key immutable (`Schedules.BuiltInImmutable`, `BuiltInAlwaysActive`).
- **S26** Applied. `CLAIM` filters `Attempts < MaxAttempts`; exhausted rows failed set-based first.
- **S27** Applied. `CountMismatch = 50428`.
- **S28** Applied. Retry classes listed in seams §1 and ledger seam 1.
- **S29** Applied. Healing only on `PK_<Table>`.
- **S30** Applied. `VersionTag.Missing` row-count check and `tellma.versiontags.missing`.
- **S31** Applied. Migrator seed is the only seed; `HasData` rows and step `30 core.version-tags` removed (seams §5.3, §11.4, §18; ledger seam 18, errata 0010 #10).
- **S32** Applied. Blob `GET` and hub negotiate exempt from `Tellma-Client`; hub rejects cross-origin negotiate (seams §13.1; ledger §2.19).
- **S33** Applied. `AccessService.Check` on another user also needs `core.Role × Read`.
- **S34** Applied (flag S32). `Jobs.ErrorDetails` leaves the Queryex entity and wire; read through `[ApiAction("error-details", Action = "Diagnose")]`, `core.Job × Diagnose` never bespoke. Chose a distinct action over column stripping in the self-scope projection because the pipeline has no column-level permission mechanism and adding one for a single column is disproportionate.
- **S35** Applied. `IJobHandler` (arguments-only) and `IEntityJobHandler<TEntity : IJobEntity>`.
- **S36** Applied (flag S31). `IdentityInvitation.ExistingOnly` as a spec 0003 amendment (get-by-email without mail; spec 0003 has no sandbox concept, verified by grep); sandbox invites set it; unknown emails are `Users.SandboxRequiresExistingIdentity`. Rejected refusing invites on sandboxes outright: a sandbox with no way to add members is unusable.
- **S37** Applied in part. `ReturnEntities` defaults to `false` for `Source = Import`; the read-back is bounded by `MaxEntitiesPerSave`; MCP `tellma_save` inherits the web cap. Rejected moving the read-back after `COMMIT`: 0014 flag 10 stands — the read-back's consistency with the row just written (temporal or not) is worth more than the lock time of a ≤ 1,000-row result.
- **S38** Applied. `EXEC`/`sp_executesql` refused by the analyzer except `sp_sequence_get_range`/`sp_getapplock`.
- **S39** Applied. "overridable hooks"; attribute applications moved to Members tables in seams §15, §21, §22; seam 6 illustration tagged.
- **S40** Applied for `ExportForImport` (explicit `Action` on the Excel `[ApiAction]`s) and the blob names in §2.30. Rejected `Users.State varchar(12)`: the `varchar(n) = max(8, longest member)` convention stands and widening a `varchar` for a future member is an expand-only migration under the N−1 rule (§2.31).
- **S41** Applied. Auto-registration of `(Resource, Action, FilterRoot = Entity)` for every `[EntityAction]`/`[ApiAction]` on entity services; `[ApiAction].Resource` and `contribution.ApiService<T>()` for `[ApiRoute]` services (seams §3.1, §6, §11.1; ledger seam 3).
- **S42** Applied. `Validator<,>()`, `SaveEffect<,>()`, `DetailsContributor<,>()` sugars; contravariance and ordering stated.
- **S43** Applied. `[ApiResource]` optional; `contribution.Entity<T>()` projects regardless.
- **S44** Applied as a deferral (flag S36). `BlobReadAccess.Custom` and `CustomReadFilter` dropped this release.
- **S45** Applied. `FromCache` on a non-`Read` batch declares `Rerun`.
- **S46** Applied. A criterion alone yields `Filtered` (seams §11.2).
- **S47** Applied. Ledger §2.31 (N−1 rule) and seams §1.7; errata 0011 #26.
- **S48** Applied. MCP human-only this release (ledger §7; seams §13.2).
- **S49** Applied. Sentence in seams §5.1 and ledger seam 5.
- **S50** Applied. Wire-id rule stated for requests versus results (seams §13.1; ledger seam 13).

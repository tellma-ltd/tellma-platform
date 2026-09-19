# Brief: cross-spec review of the 11 September 2026 revisions

Specs `docs/specs/0010` … `0021` and `ARCHITECTURE.md` were revised on 11 September 2026. You are
one of several reviewers, each with one lens. Read the files your lens names in full (page with
offset/limit; the Read tool caps around 25k tokens per call) and report only defects: a
contradiction between two passages, a name used in prose that no block declares, a consumer that
still assumes the old rule, a rule stated in one spec and silently violated in another, a C#
sketch that would not parse, a missing ripple. Do not report style, do not propose new features,
do not re-litigate decisions; the decisions below are final.

## The decisions taken today (final)

1. `IsActive` is server-owned: every row is created active; only the activate/deactivate actions
   (an `IDataBatch.Update`) change it; it is absent from the editable Excel shape.
2. No LINQ surface: `ITenantDatabase.Linq<T>()`, `UseDbContext`, the injectable `TellmaDbContext`
   and `DbSet` conveniences are gone; the context is platform-internal; `TELLMA0002` refuses any EF
   query or `SaveChanges` over it outside `Tellma.Core` and `*.Migrator` projects.
3. `[Children]` is dropped: a child type declares exactly one `[ParentKey]`.
4. `[Sibling(navigation)]` marks a one-row-per-owner entity keyed by the owner's key; the schema
   provider derives a one-to-one navigation from the owner (left join). Packs extend another
   feature's entity by `RequiresShape<TDefault, TShape>()` (a required interface, checked by
   `core.entity-shapes`) or by a sibling table — never by touching the class.
5. `SaveReceipt` is `(Stamp, Inserted, Updated, Deleted)`; no `StampedIds`, no `ChildDeltas`.
6. `UpdateSpec` has `ByIds` and `ByQuery` (cap + expected count, like delete-by-query; THROW
   50413 `Update.CapExceeded`, 50428 `Update.CountMismatch`).
7. `TransactionMode.Explicit` batches are never retried by the executor.
8. A rename takes three releases under rolling deployment (add+copy while writing both / re-copy
   while writing new only / drop).
9. `Deployable` → `Tenantless` everywhere (route group, metadata records, `AsTenantlessEndpoint`,
   the static context); "deployable" survives only in the ordinary sense (a deployable app).
10. Standalone sandboxes are legal: `LiveTenantId` is optional on a `Sandbox` row and forbidden on
    a `Live` row (`CK_Tenants_LiveTenantId`).
11. `RegistrationPolicy` is gone entirely (no enum, no option, no check, no `DistributionInfo`
    member, no 409 for a second live tenant). The `tellma.profile` cookie carries the membership
    list (≤ 20 entries) so the SPA skips the picker without a request when exactly one entry is
    `Live`, `Active` and `isActive`.
12. One cache: the registry snapshot precomputes `TenantInfo.ConnectionString`; the connection
    factory keeps no string cache; a relocation drops only the tenant's pooled context factory.
13. Warm SQL profiles are allowed (`Min Pool Size` per profile) with a `MaxWarmTenants` warning.
14. The catalog mirrors `Name`, `Name2`, `Name3`, `Languages` (`TenantDisplayNames` record,
    `RenameAsync(tenantId, names)`), and `TenantDescriptor` carries them.
15. Membership hint reconciliation is `MembershipReconcileService`, a daily leader-elected hosted
    timer in `Tellma.Core.AspNetCore` (catalog applock), not a migrator step.
16. The admin surface is mapped only when `Tellma:Admin:Enabled` (default true under Standalone
    identity, false under InProc).
17. `DeploymentVersions(PlatformVersion, DistributionVersion)` carries the commit hash; logs and
    traces are enriched with both.
18. `Tellma.Defaults.Azure` (`src/defaults/`) with `UseAzureDefaults()` composes email (ACS,
    SendGrid), webhooks, the Azure blob store, ImageSharp and Azure SignalR; no Core package
    references it.
19. The reference distribution has a Client esproj (`Tellma.Distro.Acme.Client`), SpaProxy in the
    Web csproj, `proxy.conf.js`, `aspnetcore-https.js`; Development `PublicOrigin` is
    `https://localhost:4200`.
20. Spec 0021 owns the identity-server amendments (Distribution seed kind, per-tenant resources
    `<origin>/{int}/mcp`, client ID metadata documents, interim native clients, control-plane
    origin grants, `tellma_kind`, `existingOnly`); 0010, 0015 and 0017 point at it.
21. Every timestamp is `datetimeoffset(n)` / `DateTimeOffset`, written at offset zero from
    `SYSUTCDATETIME()`; audit columns `datetimeoffset(7)`, others `(3)`; the temporal period columns
    `ValidFrom`/`ValidTo` alone stay `datetime2(7)`; Queryex types them `DateTimeOffset`; the
    Queryex kind names (`DateTime`, `DateTimeOffset`) and buffers stay; a deliberate wall-clock
    `datetime2` column is a distribution's own choice. Spec 0011 §4.1 states the rule once.
22. `[NaturalKey]` may sit on any scalar with a unique index; inference stays string-only; key
    lists bind through the list type of the key's CLR type; key cells follow the property's type.
23. `[Computed]` is a fourth `PropertyOwnership`: `ResetComputed` runs on every row before
    `PreprocessAsync`, the hook sets the value, the emitter writes it on insert and in every `SET`
    list; excluded from the editable Excel shape; `readOnly` + `x-tellma-computed` in OpenAPI.
24. The schema guard: `IEntityMetadataProvider.StorageFingerprint`; `dbo.__TellmaSchema` (two
    newest rows, written by the migrator after `Migrate()` only when the history ends at its own
    last migration); the executor's first statement on every round trip is
    `IF NOT EXISTS (… WHERE [Fingerprint] = @tm_schema) THROW 50501, N'SchemaBehind', 1;`;
    `50501` → `TenantUnavailableException` code `tenant-schema-behind`, 503, `Retry-After: 30`;
    `@tm_schema` is a reserved name.
25. `IdBufferCapacity` is gone: returned ranges re-enter the buffer in full; `IdBufferLowWater`
    sizes refills only.
26. Contract blocks are C# sketches (fenced `csharp`; no `using`, no `///`, no bodies, no
    cancellation tokens; async members carry the `Async` suffix in the sketch even where prose
    names them without it). Section 1 of each spec carries the placement sentence.

## Severity

- **blocking**: an implementer following both passages would build two incompatible things, or a
  named member does not exist anywhere.
- **major**: a stale rule survives (the old behaviour is still described somewhere), or a
  consumer was not updated for one of the decisions above.
- **minor**: a wording drift with one obvious reading.

Report blocking and major only, at most fifteen per lens, the most consequential first. Every
finding must quote the offending text (≤ 200 characters), name the file and section, state the
conflicting passage or decision, and propose the one-line fix. Findings without a quote are
discarded.

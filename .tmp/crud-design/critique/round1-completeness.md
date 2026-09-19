# Round 1 — completeness critique (coverage map)

Written 2026-09-04 against `ledger.md` (835 lines), `seams.md` (2172 lines), the ten `themes/<key>/decisions.md` files (§5 Answers and §10 Conflicts), the brain dump `.tmp/crud-stack.md`, the breakdown `.tmp/crud-stack-specs.md`, and the briefing.

Notation: **L §x** = `ledger.md` section; **S §x** = `seams.md` section; **T \<key\> D*n*** = a theme decisions file's decision, as cited by that file's §5 table. "Covered" means a spec author can find a concrete, final answer at the cited location; "theme-only" means the ledger is silent and the theme decisions file is the only home (acceptable because the ledger is an overlay, but noted); **GAP** is listed again in §7.

Summary: 88 brain-dump questions and `??` markers mapped (0 GAP, 5 theme-only); 66 "Owns" items mapped (2 partial); 8 breakdown seams + 17 briefing seams mapped (all have a ledger §3 entry and a `seams.md` section; 2 rule-level gaps inside seam 11); 8 ARCHITECTURE misalignments mapped (0 GAP); 105 theme conflict items mapped (1 partial); 12 ledger-named contract members differ from or are absent in `seams.md`. Nine findings, seven blocking.

---

## 1. (a) Brain-dump open questions and `??` markers

### 1.1 Backend layers

| # | Question (brief) | Answered at |
|---|---|---|
| a1 | "Are those the proper layer names in a .NET business app?" | L §1.24 (layer folders dropped), L §6 row *Distribution repo layout* (`Entities/`, `Services/`, `Endpoints/`; 0010 D3); T host-tenancy §5 D3 |

### 1.2 User entity — `??` and inline questions

| # | Marker | Answered at |
|---|---|---|
| a2 | `State, -- ?? We need to track the user's invitation state` | L §1.17, §2.29 (`State New\|Invited\|Active`, `InviteStatus`, `LastInviteError`); S §11.3–11.4 |
| a3 | `ImageId, -- How do we best store, retrieve, and modify the user profile picture?` | L §2.6, §3 seam 12; S §12 (`user-image`, `GET /{tenantId}/blobs/{kind}/{id}`); T blobs §5 D2/D8/D9 |
| a4 | `ImageFitJson, -- here or in a centralized blob metadata table?` | L §1.18 (dropped; presentation applied before storage); T blobs §5 D2/D11 |
| a5 | `PushSettings, -- ?? what shape` | L §1.18, §2.21 (`core.NotificationPreferences (UserId, Type, Inbox, Email)`); push subscriptions deferred L §7 |
| a6 | "Those columns will cause temporal churn … sibling non-temporal table" | L §1.14, §2.18 (`core.UserStamps`); S §5.2 |
| a7 | `UserSettingsVersion` / `PermissionsVersion` fingerprint columns | L §2.18 (`PreferencesTag`, `PermissionsTag` on `core.UserStamps`) |
| a8 | `InboxTracking` | L §2.18 (`InboxSeenAt`); L §4.4.7; S §15.2 `inbox/summary` |

### 1.3 User entity — open questions

| # | Question | Answered at |
|---|---|---|
| a9 | "What is the better approach for the record+blobs pattern?" | L §1.9, §2.28, §3 seam 12; S §12 |
| a10 | "Is JSON the right shape for User preferences? Pinned screens in a distinct table?" | L §2.21 (opaque `core.UserPreferences (UserId, Key, Value)`); admin-curated pinned screens deferred L §7; T users-roles §5 D6 |
| a11 | "What do we need to store for the notification settings? One JSON field?" | L §2.21 (`NotificationPreferences`), §2.29 (`ContactEmail`, `ContactMobile`); S §15.1 |
| a12 | "Separate SettingsVersion and PermissionsVersion, or one?" | L §2.18 (separate, differently scoped) |
| a13 | "Version, ETag, or fingerprint?" | L §1.24, §2.18 ("version tag") |
| a14 | "What is the shape of inbox tracking?" | L §2.18 (`InboxSeenAt`), §4.4.7 (capped counts) |
| a15 | "Are the user states listed above exhaustive?" | L §1.17, §2.29 |
| a16 | "Write-once columns (Subject, Email): two UDTTs or a service rule?" | L §2.5 (`[WriteOnce]` excluded from UPDATE + `WriteOnce` error), §2.29 (`Subject` server-owned; `Users.EmailLockedAfterInvite`); S §2.2 |

### 1.4 Role, Permission, Centers — open questions

| # | Question | Answered at |
|---|---|---|
| a17 | "Global permissions: system role with hardcoded Id, `IsPublic` on Roles, or a separate table?" | **Theme-only for the mechanism**: T users-roles §5 D8/D21 (`IsPublic` on `Role` with no-members and no-wildcard invariants); S §11.3 `Role.IsPublic`, §11.4 `IX_Roles_IsPublic`, §16 prologue `R.[IsPublic] = 1`. The ledger only says "No seeded `Public` role" (L §2.29) and never states the chosen mechanism — see finding 8 |
| a18 | "Do we need SavedById on the weak entities?" | L §1.12 (children carry no audit columns; child change stamps the parent) |
| a19 | "hierarchyId kept in step with ParentId under bulk save: in memory or SQL?" | L §2.10; S §1.4 |
| a20 | "How to best model the CenterType column?" | L §2.11; S §21 |
| a21 | "Best convention to encode the Resource a permission secures?" | L §2.16; S §11.1 |
| a22 | Extensibility note: "distros extend or fully replace these entities while reusing the service logic … validate the interface matches the DB" | L §2.26 (`tellma.UseEntity<TDefault, TLeaf>()`), L §6 row *capability interfaces*; T data-access §5 D9 (metadata validated at startup) |

### 1.5 Save emitter, batch, ids

| # | Question | Answered at |
|---|---|---|
| a23 | "Better name for the SQL Save emitter and the multi-statement builder/executor?" | L §2.7 (`SaveEmitter`, `IDataBatch`/`DataBatch`) |
| a24 | "Does the emitter need to know upsert vs synchronize?" | S §1.3 (`null` untouched, `[]` delete all, list synchronise); L §3 seam 2; T data-access §5 D3/D13 |
| a25 | "What should the builder/executor API look like?" | L §2.7, §3 seam 1; S §1.1 |
| a26 | `MayRetry` flag on every statement | L §1.3 (`Idempotent`; retry is the executor's job); S §1 |
| a27 | "Minimize Id gaps without a dedicated round trip; ride OnConnect or the validation query?" | L §2.8 (exact-deficit reservation on RT1, warm buffer 64) |
| a28 | "Who maintains the reserved IDs — a thread-safe singleton?" | L §2.8; S §1.2 (`IIdAllocator` singleton) |
| a29 | "Un-consume Ids when a save fails?" | L §2.8 (validation failure returns; persist attempt never) |
| a30 | "Who assigns ids — service or data layer?" | L §2.8 (`IdReservation.Assign`, after RT1, before validators); S §1.2 |

### 1.6 Service layer, endpoints, queries

| # | Question | Answered at |
|---|---|---|
| a31 | "MCP schema is discoverable at runtime … can be changed without breaking agents (is this true?)" | Theme-only: T host-tenancy §5 D18, T web-api §5 (partly; tool and argument names are a compatibility surface) |
| a32 | "Should we keep the search parameter?" | L §3 seam 3 (`[Searchable]` → search disjunction); S §3.2 `SearchFilter`; T service-pipeline §5 D6 |
| a33 | "Hints on search-page vs entity-picker origin?" | Theme-only: T service-pipeline §5 D6, T web-api §5 (no hint) |
| a34 | "Another array of arrays for the ancestors?" | L §3 seam 13 (`QueryResult.Ancestors`); S §13.1; S §1.1 `RowQueryOptions.Ancestors` |
| a35 | "count stops counting beyond 9999" | S §1.1 `Count(cap = 10000)`, §3.1 `CountCap`; L §5 0014 flag 8 |
| a36 | "If the user lacks any read permission … throw ForbiddenException (?)" | L §3 seam 10/11 (type-level denial 403; row-level 404); S §10, §11.2 `Require` |
| a37 | "Details: build the queries — queryex or raw SQL?" | L §6 rows *reports/SqlBuilder* and *validation* (keyed loads model-emitted; Queryex for user text; raw SQL through `DetailsPlan.AddExtra`); S §3.3 |
| a38 | Save step 4 "Start the transaction (is this right?)" | L §1.2 (transaction = the persist round trip's text); S §1.6 |
| a39 | "Platform API that lets custom validators participate in the batch context load?" | L §2.26, §6 row *validation* (`IContextLoader`); S §3.2–3.3 |
| a40 | "How can we dedupe validation context queries?" | S §3.3 (`IContextLoader` dedup by structural key); T service-pipeline §5 D10 |
| a41 | "Where should the transaction boundary begin and end?" | L §1.2; S §1.6 |
| a42 | "Collapse DB calls #1 and #2 optimistically with cached permissions?" | L §2.24, §3 seam 16; S §16 |
| a43 | Notes: empty vs missing child collection | L §3 seam 2; S §1.3 |
| a44 | "Even though Save admits a single entity …" | L §1.23 (array; UI sends one); S §13.1 `SaveRequest<T>` |
| a45 | "How do we best extract the common API — base class? inheritance vs composition?" | L §2.26; S §3.2; T service-pipeline §5 D2 |
| a46 | "5 ids requested, 4 found: 4 or 404?" | S §3.2 (`GetByIdsAsync` partial; `GetByIdAsync` 404); T web-api §5 D5 |
| a47 | Queryex: TVP-backed list restriction | L §3 seam 4 (`KeySetRestriction`); S §4 |
| a48 | Queryex: `level()` | L §3 seam 4; S §4 |
| a49 | "Capability boilerplate reused across distros, surface flexible?" | L §3 seam 3; S §3 |
| a50 | UserService custom endpoints (invite, self-service, test notification, preferences) | L §3 seam 21; S §21 `UserService<TUser>` |

### 1.7 Web layer, access control, concurrency, rate limiting

| # | Question | Answered at |
|---|---|---|
| a51 | "Extract the common CRUD endpoint boilerplate into Core?" | L §3 seam 13, §6 row *Web API*; S §13.2 `MapTellma` |
| a52 | "Source-generated JSON serialization?" | L §3 seam 2 (0015 D7 options); S §2 header; T web-api §5 D7 |
| a53 | "X-Today header? Or the user's timezone?" | L §1.10, §2.19, §2.27 (no today header; `Tellma-Time-Zone` display-only; `today()` tenant zone) |
| a54 | "Registering securables and enforcing access hard to forget?" | L §2.22; S §11.1 (endpoint metadata, startup audit, witness) |
| a55 | "Is 'Securable' the right word?" | L §2.22 |
| a56 | "Permissions invalidated by a schema change: shim or block?" | L §1.26; S §11.2 rules (drift grants nothing, reported; `Alias`) |
| a57 | "Endpoint: does user X have permission Y on Z, with what filter, and why?" | L §3 seam 11 (`EvaluateFor`); S §21 `AccessService.Check` |
| a58 | Access rules: public union, implicit read, OR of filters, inactive roles, bespoke criteria | Public/inactive: S §16 prologue; bespoke: S §3.2 `BespokeGrant`, §11.2 `AccessCriterion`; **implicit read and OR-composition: GAP in seams (finding 1)**; T users-roles D11 |
| a59 | Weak entities as queryable roots with path rewriting | Deferred with a named seam: L §7 (`FilterTree.Via`); S §4 |
| a60 | Self-lockout: cannot delete/deactivate self or strip own admin | S §11.2 codes and `IAccessGuards`; L §1.26 |
| a61 | "If you can read an entity you can read all related entities and extras" | L §1.23 (narrowed to `[RelatedSelect]`); S §11.2 traversal rule |
| a62 | "Good alternative to RowVersion, implementable in C#?" | L §2.4; S §1.3 guard |
| a63 | Rate limiting: in-memory, bounded payload | S §13.2 `TellmaApiOptions`; L §1.28 |

### 1.8 Caching and settings

| # | Question | Answered at |
|---|---|---|
| a64 | "How do we guarantee the cache version is invalidated when a cacheable entity is updated?" | L §2.18 (executor epilogue from declared writes); S §5.3, fixture write audit |
| a65 | "Is 'version' the accurate name? etag? fingerprint?" | L §2.18 |
| a66 | "Is 'metaversion' the accurate name?" | L §1.14, §2.18 (`FormatVersion`) |
| a67 | "Cacheable entities … up to 50–100 records?" | S §5.1 `[Cacheable(MaxRows = 1000)]`, ceiling 10,000 |
| a68 | LRU, thread-safe, stampede-safe, observable caches | L §6 row *caching*; S §5.1 `VersionedCache`, `CacheTelemetryNames` |
| a69 | "Is calling the table Settings correct?" | L §2.21 (`core.Settings`) |
| a70 | "What do we call the KV table?" | L §2.21 (`core.SettingEntries`) |
| a71 | "Language axis vs culture axis?" | L §1.32; S §9 (`Language`, `Culture`, `ContentLanguageIndex`), §22 |
| a72 | "Criteria for top-level table vs KV? Everything in KV?" | Theme-only for the criterion: T settings §5 D8 (typed = read by the platform on the request path); shape fixed in L §2.21 |
| a73 | "Single settings API vs per category? Resource = category?" | L §2.21, §2.16 (`settings/save`; `core.Settings.<Category>` × `Save`); S §22 |
| a74 | "Edit signature: patch vs load-and-save?" | L §2.21 (`TenantSettingsPatch` field mask); S §22 |
| a75 | "Settings collections tables (do we need those?)" | L §2.21 (`SettingEntries` only) |

### 1.9 Exceptions, localization, multi-tenancy

| # | Question | Answered at |
|---|---|---|
| a76 | "Status code: enumerate exceptions or an interface?" | L §2.20; S §10 |
| a77 | "Return validation messages or codes?" | S §10 (both: `errors` items carry `code`, `message`, `arguments`); L §2.20 |
| a78 | "Compliance modules may not support all languages — restrict?" | Theme-only: T settings §5 D13 (no restriction; a pack's `SupportedLanguages` mismatch is a startup warning). No contract in S §6 or §22 — finding 9 |
| a79 | "How do we organize the resource files in the backend?" | L §3 seam 22; S §22 (`Resources/Strings.resx`, key conventions) |
| a80 | "How do we support ICU message format?" | L §3 seam 22; S §22 `IcuStringLocalizerFactory` |
| a81 | "Custom calendar header (does a standard one exist?)" | L §2.19 (`Tellma-Calendar`); briefing §8 |
| a82 | "Culture headers ignored if not one of the tenant's languages" | L §1.32, §4.6.13 (dropped; `Accept-Language` against the shipped catalogue) |
| a83 | "Is the TenantRegistry the right shape?" | L §1.19; S §9 (`ITenantRegistry`, `ITenantCatalog`, connection factory/provider) |
| a84 | "If each db gets a password, where do we store them?" | L §1.19, §6 row *secrets* |
| a85 | "One MCP server per tenant, or one per distro?" | L §6 row *MCP topology*; S §13.2 (`/{tenantId}/mcp`, per-tenant audience) |
| a86 | "Extend the TenantRegistry to blob storage / Key Vault?" | T host-tenancy §5 D8, T blobs §5 D5 (no); L §6 row *Hosting on Azure* (one container per tenant); `ITenantSecretStore` deferred L §7 |
| a87 | "Extend the TenantRegistry to provisioning? Unify while extensible?" | L §2.23, §3 seam 18; S §18; L §7 (sandbox cloning, `ITenantSecretStore` deferred) |
| a88 | Membership lookup ("which tenants am I a member of"; fan-out) | L §1.20; S §9 `ITenantMembershipDirectory` |

### 1.10 Background tasks, blobs, MCP, inbox

| # | Question | Answered at |
|---|---|---|
| a89 | "Is the design robust?" | L §1.6, §1.29, §2.9; S §8; T background §5 D1/D4–D7/D10 |
| a90 | "Shared coordination state in the catalog DB, or leasing without a central table?" | L §4.1.8 (no catalog coordination tables); S §8.2 `core.JobWorkerState` per tenant |
| a91 | "How do OTel traces fit — inherit or own?" | S §8.2 `TraceParent`, §8.1 `ActivitySourceName`; L §4.1.8; T background §5 D12 (own root span with links) |
| a92 | "Scheduler where CRON expressions can be stored anywhere?" | L §1.24 (dropped); S §8.1 `Schedule`, §8.3 tick statements |
| a93 | "User-triggered tasks under the user's credentials?" | S §8.1 (`RunAsUserId`, `ConnectAsUser`); L §3 seam 9 |
| a94 | "Triggered tasks: which credentials? System user? Prevent scheduling reads the user cannot see?" | S §8.1 (`RunAsUserId` = last saver; system user on built-ins; `SchedulePausedReason.OwnerInactive`); L §2.30 `WellKnownIds` |
| a95 | "Replay all vs last-only; safeguard against the year-old restore?" | S §8.1 `MissedPolicy`, `OverlapPolicy`, `CatchUpWindowMinutes`, `GapThreshold`; §8.3 gap-hold |
| a96 | "Lease expiry with a safety buffer, or renewal?" | S §8.3 RENEW; T background §5 D5 |
| a97 | "Nudge the engine so a new email is processed immediately" | S §8.1 `IJobQueue.Enqueue` (nudge via `OnCommitted`); cross-instance nudge deferred L §7 |
| a98 | "Logs and metrics for the background system" | S §8.1 `JobsTelemetryNames` |
| a99 | "Azure blob storage uses the adapter pattern — should FileSystem too?" | L §2.17 (`Tellma.Connector.AzureBlobs.Adapter`; file-system store in `Tellma.Core`); S §12.1 |
| a100 | "Should every tenant get their own [container]?" | L §6 row *Hosting on Azure*; S §12 (provisioning step 20) |
| a101 | "Should tenantId be a parameter in the interface or part of the config?" | S §12.1 `IBlobStore` (tenant id per call) |
| a102 | MCP: few intent-based tools; auth for humans and autonomous agents | S §13.2 (seven tools; OAuth 2.1 resource server); L §6 spec 0003 amendment |
| a103 | "Self-initiated task completion in the inbox? Non-unsubscribable types? Alternative access to the artifact?" | L §2.15 (`Exports` row = durable path); S §15 (`Mutable = false` types) |
| a104 | "What happens when you click an unread item?" | S §15 (`TargetResource`/`TargetId`); T background §5 D15/D16 |
| a105 | "Best entity model and API interface?" (inbox) | S §15, §15.1–15.2 |
| a106 | "Notifying must not add a round trip" | L §3 seam 15; S §15.2 |
| a107 | "SignalR notifies that counters changed" | S §15 (`inbox.changed`), §20 |

Note on numbering: 107 rows because inline `??`/note items are listed beside the formal "Open Questions"; the headline count of 88 in the summary counts the formal questions and `??` markers only.

---

## 2. (b) "Owns:" items per spec

| Spec | Owns item | Location |
|---|---|---|
| 0010 | Reference distribution with Web and Migrator projects | L §2.23; S §0.2 (`TellmaMigrator`), §18 |
| 0010 | `AddTellma` composition root at minimal fidelity | L §3 seam 6; S §6 |
| 0010 | Tenant routing from the URL | S §13.1 routes (`/{tenantId:int:min(1)}/…`); L §4.6.1 |
| 0010 | Tenant registry and catalog, single-live and multi-live | L §1.19, §6 row *Multi-tenancy*; S §9 (`TenantRegistrationPolicy`, `ITenantCatalog`) |
| 0010 | Per-tenant connection resolution | S §9 (`ITenantConnectionFactory`, `ITenantConnectionProvider`, `TenantLocation`) |
| 0010 | Secrets policy | L §6 row *secrets*; S §9 `CredentialProfile` |
| 0010 | Membership-lookup seam | L §1.20; S §9 `ITenantMembershipDirectory` |
| 0010 | Sandbox context implementation | S §9 note (`ISandboxContext` over `RequestContext.IsSandbox`) |
| 0010 | OIDC relying party with the BFF cookie | L §1.25, §6 row *Identity*; S §13.2 (`TellmaAuthentication`, `ICatalogSessionStore`, `/bff/*`) |
| 0011 | Entity contract (keyed, temporal, child, tree, audited, activatable, multilingual; enum-as-string; tree columns) | L §2.3, §2.11; S §2.1–2.2, §1.7 |
| 0011 | ID allocator with range reservation riding the batch | L §2.8; S §1.2 |
| 0011 | Bulk save emitter (upsert + child sync; audit and concurrency stamping; cache-tag bumps) | S §1.3; L §2.4, §2.18 (bumps as epilogue) |
| 0011 | Batch builder/executor (Queryex + save + raw SQL; retry flags; transaction scope; result readers) | L §2.7; S §1.1 |
| 0011 | Queryex host integration (schema adapter, materializer, TVP restriction, `level()`, amendments) | L §3 seam 4; S §4, §1.1 `Query<T>` |
| 0011 | Tree recompute statement | L §2.10; S §1.4 |
| 0011 | DB-call budget instruments | S §1.2 `DataAccessScope`, `DataTelemetryNames`; S §14 |
| 0011 | (briefing) ordinal-binding analyzer; LocalDB fixture tests | S §0.1 (`TELLMA0001–0003`); S §5.3 fixture tier; S §23 |
| 0012 | Settings and KV tables, what belongs in each | L §2.21; S §22 tables |
| 0012 | Server and UI settings DTOs and read path | S §22 (`TenantSettings`, `TenantSettingsForClient`, `ITenantSettingsCache`, `settings/client`) |
| 0012 | Cache infrastructure (version tags + metaversion, bounded LRU, stampede safety, meters) | S §5.1 (`VersionedCache`, `FormatVersion` via `ToWire`, `CacheTelemetryNames`) |
| 0012 | Framework-level guarantee that a save bumps the right tag | L §2.18; S §5.3 bump + fixture audit |
| 0012 | Cacheable-entity mechanism | S §5.1 (`[Cacheable]`, `ICacheableEntities`, `FromCache`) |
| 0012 | Localization (catalogue, distro subset, tenant languages/calendars, negotiation, fallback, resources, ICU, calendar-aware formatting, Name2/Name3 gating) | S §22; L §2.27; S §6 `Languages`/`AddLanguage` |
| 0013 | User, UserSettings, RoleMembership, Role, Permission classes; churn columns in a sibling table | L §2.29, §2.18; S §11.3–11.4, §5.2 |
| 0013 | Tenant bootstrap seeding the first admin | L §2.23; S §18, §11.2 `ITenantBootstrapper` |
| 0013 | Per-request connect step | L §2.24; S §16 |
| 0013 | Securables registry | L §2.22; S §11.1 |
| 0013 | Permission evaluation: public permissions, inactive roles, bespoke criteria | S §16 prologue, §11.2 |
| 0013 | Permission evaluation: implicit read, disjunctive filters | **Partial** — T users-roles D11 only; absent from S §11 and the ledger (finding 1) |
| 0013 | Permission evaluation: weak-entity path rewriting | Deferred with seam `FilterTree.Via` (L §7; S §4) |
| 0013 | RLS filter composition | S §4 note, §11.2 rules; L §3 seam 4 |
| 0013 | Permissions cache | S §5.1 kind `permissions`, §11.2 `UserAccess`, `AccessOptions` |
| 0013 | Self-lockout guards | S §11.2 `IAccessGuards`, codes |
| 0013 | Drift policy | S §11.2 rules; `SecurableRegistryBuilder.Alias` |
| 0013 | "Can I, and why" query | S §11.2 `EvaluateFor`; §21 `AccessService.Check` |
| 0014 | Service base and composition decision | L §2.26; S §3.2 |
| 0014 | Standard operations (query + capped count + ancestors; details + related + row echo; save; delete by ids/query; by parent ids; with descendants) | S §3.2, §13.1 |
| 0014 | Save pipeline (authorization, RLS pre-check, preprocessing, validation with batched dedup, transaction boundary, persist, RLS post-check, side effects) | S §1.6, §3.3; pre-commit effects removed L §1.9. **Partial**: the RLS pre-check has no statement or contract in `seams.md` (finding 2) |
| 0014 | Capability recipes (IsActive, tree + cycle validation, audit, record-plus-blobs hook) | S §3 table |
| 0014 | Concurrency override flag | S §1.1 `ConcurrencyMode`, §3.2 `SaveOptions.Concurrency` |
| 0014 | Search-parameter decision | S §3 table, §3.2 `SearchFilter` |
| 0014 | Distro extension of pack entities | L §2.26; S §6 `UseEntity<,>` |
| 0015 | Endpoint projection with Minimal APIs | S §13.2 `MapTellma` |
| 0015 | Three surfaces; MCP tool-shape and auth sketch | S §13.2; `v1` seam L §7 (MCP is now built, not a seam — L §5 0015 flag 11) |
| 0015 | Wire shapes and source-generated JSON | S §13.1, §2 header |
| 0015 | Exception-to-status contract | S §10 |
| 0015 | Validation error format | S §10 problem body |
| 0015 | Payload and rate limits | S §13.2 `TellmaApiOptions` |
| 0015 | Culture, calendar, today and timezone headers | L §2.19; S §13.1 |
| 0016 | Blob service contract; file-system and Azure implementations; packaging | S §12.1; L §2.17 |
| 0016 | Tenant scoping | S §12.1 `IBlobStore(tenantId, …)` |
| 0016 | Staging vs inline | L §1.9; S §12 |
| 0016 | Orphan collection | S §12.3 (`core.blob-sweep`, `core.blob-reconcile`) |
| 0016 | Etag-validated retrieval endpoint | S §12 endpoints |
| 0016 | Where image metadata lives | S §12.2 (`Width`, `Height`, `Variants`); `ImageFitJson` dropped L §1.18 |
| 0017 | UserService (invite via bulk invite/delivery APIs, state model, self-service, image, notification settings, test notification) | S §21; notification preferences at S §15 (0020's statement) |
| 0017 | RoleService validating against the registry | S §21 `RoleService<TRole>`, §11.2 `RoleAccessRules` |
| 0017 | Settings edit API | Moved to 0012: L §2.21, errata 0017 #9; S §22 `SettingsService`, §23 matrix |
| 0017 | GL module package + Abstractions + registry entry | L §2.17, §2.23; S §21 (`GlFeature`) |
| 0017 | Center as the first tree entity | S §21 `gl.Centers`; L §2.10–2.11 |
| 0017 | Seed data | S §18 (`HasData` band; `gl.sample-centers` step) |
| 0017 | Reference distribution's migrations (+ local-dev admin bootstrap) | L §6 row *migrator*; S §18 |
| 0018 | Export by query / by ids in display shape | S §19 `ExportRequest` |
| 0018 | Export-for-import with children | S §19 `ExportForImportRequest` |
| 0018 | Natural-key declaration and inference | Owned by 0011 (L §2.12; S §7); 0018 consumes |
| 0018 | Multilingual-aware column mapping with override | S §19 (`ImportColumnMapping`, `ImportPlanColumn.Language`) |
| 0018 | Three import modes with hydration of partial sheets | S §19 `ImportMode`; L §4.5.10 (hydration through `GetByIdsAsync`), S §3.3 round-trip ledger |
| 0018 | Bulk natural-to-surrogate translation | S §19 `ExcelImportSession.Lookups`; S §4 `KeySetRestriction` |
| 0018 | Tree import with in-sheet parents | L §1.22; S §19 error codes `OrphanChildRow`, `ParentCycle` |
| 0018 | Localization metadata on columns | S §19 (`ExcelSheetPlan.NumberFormats`, `ExcelContext`) |
| 0018 | Library choice | L §0 table, §2.17 (`DocumentFormat.OpenXml`) |
| 0018 | Hand-off of large files to background work | L §2.14; S §19 (`Background`, `core.export`/`core.import`) |
| 0019 | Leasing machinery (columns, handler registry, lease/renewal, nudging, multi-instance safety, telemetry) | L §2.9; S §8 |
| 0019 | CRON scheduler with replay policy and credentials model | S §8.1, §8.3 |
| 0019 | Trace linkage | S §8.2 `TraceParent`; L §4.1.8; T background D12 |
| 0019 | First consumers (import/export, blob sweep) | S §8.1 handler keys |
| 0019 | Inbox entity and counters; per-type actions; notification preferences; SignalR hub | Split to 0020: S §15, §20; L §0 |

---

## 3. (c) Seams

Breakdown "Seams to fix" and briefing §4 seams, with the ledger §3 entry and the `seams.md` contract.

| # | Seam | Ledger §3 | seams.md |
|---|---|---|---|
| B1 / 1 | One batch abstraction serves five specs | Seam 1 | §1 (`IDataBatch`, emitter, tree, delete, standard columns, standalone types) |
| B2 / 2 | Entity class vs wire shape | Seam 2 | §2 |
| B3 / 3 | One capability, declared once | Seam 3 | §3 |
| B4 / 4 | Queryex schema per tenant configuration | Seam 4 | §4 |
| B5 / 5 | Version tags | Seam 5 (→ §2.18) | §5 |
| B6 / 6 | Feature composition | Seam 6 | §6 |
| B7 / 7 | Natural keys | Seam 7 (→ §2.12) | §7 |
| B8 / 8 | Background-task columns and lease statements | Seam 8 (→ §2.9) | §8 |
| 9 | Request context | Seam 9 (→ §2.25) | §9 |
| 10 | Platform exceptions and HTTP mapping | Seam 10 (→ §2.20) | §10 |
| 11 | Permission evaluation API | Seam 11 | §11 — **rule-level gaps**: implicit read / filter composition (finding 1); two-stage pre-check (finding 2) |
| 12 | Blob staging tokens | Seam 12 | §12 |
| 13 | Wire shapes for query/details/save | Seam 13 | §13 |
| 14 | Telemetry names and the DB-call budget | Seam 14 | §14 (constant-holder names differ — finding 6) |
| 15 | Notification enqueue riding the save batch | Seam 15 | §15 |
| 16 | Connect-call collapse | Seam 16 (→ §2.24) | §16 (`Run` signature differs — finding 5) |
| 17 | Vocabulary | Seam 17 (→ §2) | §17 |
| — | Added: provisioning and bootstrap | Seam 18 | §18 |
| — | Added: Excel operations and row source | Seam 19 | §19 |
| — | Added: session/tenant-state listeners, hub | Seam 20 | §20 |
| — | Added: identity-server client and Core/GL stacks | Seam 21 | §21 |
| — | Added: settings, labels, calendars, negotiation | Seam 22 | §22 |

---

## 4. (d) Misalignments with ARCHITECTURE.md

| # | Breakdown item | Ledger §6 row |
|---|---|---|
| d1 | Feature composition absent from the brain dump; how much the first release builds | *Feature Composition* (minimal fidelity: `ITellmaFeature`, `Requires`, data-only items, two gates, endpoint audit) |
| d2 | HTTP verbs (GET/DELETE projection vs POST-everywhere) | *Web API / endpoints* (POST-only web surface; REST only for `v1`; the blob GET) |
| d3 | Save cardinality (arrays vs single) | *Guiding Principles — bulk I/O* stands (save takes an array, L §1.23; blob upload the named exception) |
| d4 | Catalog database (Catalog DB per distribution vs table in the live DB) | *Multi-tenancy* (one catalog database per distribution always; `catalog` schema) |
| d5 | Reference distribution location (`distributions/<slug>/` vs `samples/…`; `taxonomy.json` missing) | *Platform repo layout*, *Rollout & Phasing*, *Reserved slugs* (`distributions/acme/`; `taxonomy.json` created by 0010) |
| d6 | Two MCP servers need distinct names | *MCP topology* (`tellma-dev` vs `tellma-tenant`) |
| d7 | Capability interfaces vs services coded against interfaces | *Data Layer — capability interfaces* (capability as the single declaration; generic services over leaf types; never a paired interface) |
| d8 | Promised homes (ID allocation, EF-to-Queryex adapter) | *Spec pointers* (both land in 0011; calendar codes land with 0012) |

---

## 5. (e) Theme conflicts (§10) vs ledger §4

Every theme's §10 list was compared item by item with the ledger's §4 subsection for that theme. Counts match one to one (background-inbox 10/10, blobs 12/12, core-gl-stacks 9/9, data-access 10/10, excel 8/8, host-tenancy 9/9, service-pipeline 13/13, settings-cache-l10n 8/8, users-roles-permissions 9/9, web-api-mcp 17/17 = 105). Status per item:

| Theme | Items | Resolution status |
|---|---|---|
| host-tenancy (0010) §10.1–9 | 9 | L §4.1.1–9, all resolved (enum-column convention, `TenantNotFoundException`, `AcceptsBinaryMetadata`, `core.session-sweep`, spec 0003 amendment) |
| data-access (0011) §10.1–10 | 10 | L §4.2.1–10, all resolved (item 2 reversed; item 7 superseded by `IJobEntity`) |
| settings-cache-l10n (0012) §10.1–8 | 8 | L §4.3.1–8, all resolved (51001 → 50412; `UserSettingsTag` → `PreferencesTag`; time-zone header exists) |
| users-roles-permissions (0013) §10.1–9 | 9 | L §4.4.1–9; **item 5 partial**: "fallback policy and group `RequireAuthorization`" is quoted but not resolved (finding 3); the other four sub-items of item 5 and items 1–4, 6–9 resolved |
| service-pipeline (0014) §10.1–13 | 13 | L §4.5.1–13, all resolved (server stamp; `TenantNotFoundException`; `IBlobService`; `INotifier`/`IJobQueue`) |
| web-api-mcp (0015) §10.1–17 | 17 | L §4.6.1–17, all resolved (`DataAccessScope`; Skip/Take slots added to seam 4; raw-body upload) |
| blobs (0016) §10.1–12 | 12 | L §4.7.1–12, all resolved (item 2 deferred with `FilterTree.Via`; container pre-creation step) |
| core-gl-stacks (0017) §10.1–9 | 9 | L §4.8.1–9, all resolved (0013's columns; `Center<TCenter>` kept; `settings/client` member-only) |
| excel (0018) §10 bullets 1–8 | 8 | L §4.9 bullets 1–8, all resolved (`KeySetRestriction`; `SaveOptions`; FK validation under the read filter adopted for JSON too; `Tellma:ScratchPath`) |
| background-inbox (0019/0020) §10.1–10 | 10 | L §4.10.1–10, all resolved (item 9 recorded for the outbox spec; plural names) |

---

## 6. (f) Ledger-named seam types and members vs `seams.md`

Every type and member the ledger names in §2.7–2.30 and §3 seams 1–22 was looked up in `seams.md`. The following groups were verified present with the same shape: seam 1 (`ITenantDatabase`, `ITenantDatabaseFactory.Open(tenantId, context, shape)`, `QueryexContextValues`, `BatchPurpose`, `TransactionMode`, all `IDataBatch` members except `Save`, `BatchResult`, `BatchOutcome`, `IDataBatchContributor`, `DataBatchStage`, `SqlOptions`, `TableName`, `SqlIdentifier`, `QueryArguments`, `RowQueryOptions`, `EntityQuery`, `EntityQueryResult`, `ConcurrencyMode`, `SaveReceipt`, `IdDelta`, `UpdateSpec`, `UpdateReceipt`, `DeleteReceipt`, `RawResult`, `IIdAllocator`, `IdReservation`, `DataAccessScope`, `TellmaSqlErrors`, `LargeBatchThreshold`, blob capture tables); seam 2 (`EntityMetadata` and every member, `PropertyMetadata`, `PropertyOwnership`, `ChildCollectionMetadata`, `ReferenceMetadata`, `NaturalKeyMetadata`, `MultilingualGroup`, `TreeMetadata`, `BlobReferenceMetadata`, `IEntityMetadataProvider`, the six bases, `IActivatable`, `IJobEntity`, all 23 annotations of L §2.3); seam 3 (`StackDescriptor`, `ActionDescriptor`, `ActionKind`, `ApiServiceDescriptor`, `IStackRegistry`, `StackContributionItem`, `[Stack]`, `[EntityAction]`, `[ApiAction]`, `[ApiRoute]`, `EntityService<,>` and its nine hooks, `IEntityValidator`, `ISaveEffect`, `IDetailsContributor`, `EntitiesResult`, `QueryResult`, `AffectedResult`, `UseEntity<,>`); seam 4 (`IQueryexSchemaProvider.GetSchema(shape)`, `KeySetRestriction`, `QuerySpec.Restrictions`, `level()`, `CompiledQuery.Prologue/Body`, Skip/Take slots); seam 5 (all fourteen names, both tables, all statements); seam 6 (all sugars and `TellmaBuilder` members); seams 7–8 (all names, statements, handler keys); seam 9 (all names); seam 10 (all seventeen exception types and internal data exceptions); seam 11 (all names including `TryDenyFast`, `EvaluateAll`, `Alias`, `MarkSensitive`, the three endpoint metadata records); seam 12 (all names, kinds, endpoints); seam 13 (all wire records, headers, routes, reserved MCP names, `TellmaEndpoints(Web, Api, Hub, Blobs, Deployable)`); seam 15 (all names); seam 16 (`IUserConnector` members, `ConnectedUser`, `ConnectPremises { Cold, For }`, `PermissionRow`, `ConnectPrologue`, `NoActivityStampMetadata`); seams 18–20, 22 (all names); §2.30 miscellany.

Differences and absences:

| # | Ledger | seams.md | Kind |
|---|---|---|---|
| f1 | Seam 1: `IDataBatch.Save<TEntity>(rows, options: SaveOptions?)` and `record SaveOptions(Concurrency: ConcurrencyMode = Check)`; errata 0011 #12 "`SaveOptions(Concurrency)` replaces `OverrideConcurrency`" | §1.1 `Save<TEntity>(rows, concurrency: ConcurrencyMode = Check)`; `SaveOptions` exists only in `.Crud` (§3.2) — resolution #1 | Defined differently |
| f2 | Seam 1: `DeleteSpec.ByQuery(Filter, Arguments?)` | §1.1 `ByQuery(Filter, Arguments?, Cap: int)` — resolution #24 | Defined differently |
| f3 | Seam 1 / §2.7: `IPersistBatch` "is the same interface exposed to hooks" | No `IPersistBatch`; hooks see `PersistContext.Batch: IDataBatch` | Name absent (harmless; the ledger already says it is the same interface) |
| f4 | Seam 14 / errata 0011 #18: `DataMeter` ("constants unchanged"); errata 0019 §3.1: `JobsTelemetry.MeterName`, `NotificationsTelemetry.MeterName`; errata 0018 D1: `ExcelMeters.MeterName` | `DataTelemetryNames`, `JobsTelemetryNames`, `NotificationsTelemetryNames`, `ExcelTelemetryNames` (+ new `CrudTelemetryNames`, `UsersTelemetryNames`, `CacheTelemetryNames`, `LocalizationTelemetryNames`, `AccessTelemetryNames`, `TenancyTelemetryNames`, `HostTelemetryNames`, `ApiTelemetryNames`, `McpTelemetryNames`, `BlobTelemetryNames`) — resolution #5 | Renamed |
| f5 | Seam 16: `IGuardedBatchRunner.Run<TResult>(compose, read)` | §16 `Run<TResult>(purpose: BatchPurpose, compose: (ConnectedUser, IDataBatch) -> void, read: (ConnectedUser, BatchOutcome) -> TResult)` — resolution #21 | Defined differently |
| f6 | Seam 16: `ConnectResult` "gains `SettingsStale`" only; `ConnectPremises` unchanged; `UserAccess { Decide, System }` | `ConnectPremises` gains `ExpectedUserPermissionsTag`; `ConnectResult` gains `UserPermissionsTag`; `UserAccess` gains `UserTag` — resolution #10 | Members added (needed by the two-level guard of L §2.24) |
| f7 | Seam 21: `IdentityInvitationStatus` | Absent; `IdentityInvitationResult.Status: InviteStatus?` — resolution #18 | Name absent |
| f8 | Seam 13 / errata 0018 D9: "`Check` when the sheet carries `Stamp` and `Override` otherwise" (derived only) | §19 `ImportRequest.Concurrency: ConcurrencyMode?` (null = derived; caller may force) — resolution #13 | Defined differently |
| f9 | Seam 13: `JobAccepted(JobId, ResourceId?)` built from `ExportOutcome`/`ImportOutcome` that carry only `JobId` (errata 0018 §3.1) | §19 `ExportOutcome.ExportId`, `ImportOutcome.ImportId` added — resolution #13 | Members added |
| f10 | Seam 9: `RequestContextSnapshot` (members of 0010 §3.1) | §9 adds `Client` — resolution #22 | Member added |
| f11 | Seam 8: `Schedule.PausedReason` as a string with two literal values (errata 0019 D9 does not name a type) | §8.1 `enum SchedulePausedReason = OwnerInactive \| Exhausted`, `varchar(13)` — resolution #12 | Type added |
| f12 | Seam 4: `IQueryexSchemaProvider.GetSchema(shape)` | §4 adds `Fingerprint: string` (engine cache identity) | Member added |
| f13 | §2.26: single get "returns one entity" in `EntitiesResult<T>`; delete-by-ids signature unstated | §3.2 `DeleteByIdsAsync(ids, expectedStamps? = null)`; `IdsRequest` carries no stamps — resolution #6 | Member added |
| f14 | Seam 13: `me` "per 0013 §3.8 with `preferencesTag`, `tags`" (0013's `me` carried an `inbox` member) | §13.1 `MeResult` without `inbox` — resolution #9 | Defined differently (documented) |

Items f4–f14 are all recorded in `seams.md` §24 as corrections the ledger must absorb; f1–f2 and f5 change signatures a spec author would otherwise copy from the ledger.

---

## 7. Findings

Blocking = a gap in a seam contract, a conflict resolution, or an "Owns" item.

1. **[blocking — seam 11, 0013 Owns] Implicit read and filter composition are not in `seams.md`.** The 0013 "Owns" list names "implicit read" and "disjunctive filters"; the brain dump states both rules (§Access control). T users-roles D11 defines implicit read ("any grant on `(R, A ≠ Read, F)` also grants `(R, Read, F)`"), but neither `ledger.md` nor `seams.md` §11.2 states it, and §11.2 states OR-composition only for bespoke criteria (`BespokeGrant` "disjoined with the stored grants"). A 0014 or 0018 author reading the seam cannot know that `Save` on a resource permits `Read`, or how several filtered grants combine. *Resolution:* add to S §11.2 "Rules the pipeline applies" (and L §3 seam 11) the composition contract of `UserAccess.Decide`: grants are the union of active-role memberships, public roles and bespoke criteria; an unfiltered grant yields `Unrestricted`; otherwise `Filter` = OR of every matching grant's filter; a grant on `(R, A ≠ Read)` implies `(R, Read)` with the same filter; wildcard `*` on either axis matches; inactive roles and drifted permissions contribute nothing.

2. **[blocking — seam 11/16, 0014 Owns] The two-stage RLS pre-check has no contract or statement.** Ledger §3 seam 11 and §4.4.3 say the pipeline "runs the two-stage pre-check (404 then 403) and the in-transaction post-check"; the 0014 "Owns" list includes "RLS pre-check". `seams.md` specifies only the post-check (§1.6 step 11, `THROW 50403`). §3.3 says `Before(index)` is "loaded under the save grant" — which collapses the two stages into one and cannot distinguish 404 from 403 — and `TellmaSqlErrors.NotFound = 50404` ("pre-check count mismatch" in L §2.20) is declared in S §1.2 but used by no statement. *Resolution:* add to S §3.3 (validation round, RT1) the pre-check contract: existing-row before images are loaded under the caller's `Read` filter (missing → `NotFoundException`, 404, identical to non-existence); a second count of the same ids under the `Save` grant's filter must equal the first, else `ForbiddenException` (403); state whether the 404 stage is a C# comparison or `Assert(..., 50404, "Entity.NotFound")`, and add the row to the §1.6 assembly note for `Validate` batches. Mirror in L §3 seam 11.

3. **[blocking — conflict 0013 §10.5] "Fallback policy and group `RequireAuthorization`" is quoted but not resolved.** Ledger §4.4.5 resolves four of item 5's five sub-items and skips this one; `seams.md` names `TellmaPolicies { Web, Api, Mcp, ControlPlane }` (§13.2) but never says which group carries which policy, what the fallback policy is, or how `AllowAnonymous` is confined to deployable endpoints (briefing §8 says the fallback applies only to endpoints with zero metadata). *Resolution:* add to L §4.4.5 and S §13.2 one sentence: each tenant route group calls `RequireAuthorization(TellmaPolicies.<Surface>)`; the application fallback policy denies; `AllowAnonymous` is permitted only with `DeployableEndpointMetadata`; the `MapTellma` audit fails startup on any endpoint that has neither a policy nor deployable metadata.

4. **[blocking — seam 1] Ledger seam 1 and `seams.md` §1.1 disagree on `IDataBatch.Save` and `DeleteSpec`.** f1–f2: the ledger's `Save(rows, options: SaveOptions?)` with a data-level `record SaveOptions(Concurrency)` versus seams' `Save(rows, concurrency: ConcurrencyMode)` with `SaveOptions` only in `.Crud`; `DeleteSpec.ByQuery` gains `Cap`. Errata 0011 #12–13 still describe the data-level `SaveOptions`. *Resolution:* apply `seams.md` §24 resolutions 1 and 24 to L §3 seam 1's contract block and errata 0011 #12–13; state in L §2.26 that `SaveOptions` is the service-level record only.

5. **[blocking — seam 16] `IGuardedBatchRunner.Run` and the connect records differ.** f5–f6: the ledger's `Run<TResult>(compose, read)` versus seams' `Run(purpose, compose, read)`; `ConnectPremises.ExpectedUserPermissionsTag`, `ConnectResult.UserPermissionsTag`, `UserAccess.UserTag` exist only in seams, yet the two-level guard of L §2.24 cannot be computed without them. *Resolution:* apply §24 resolutions 10 and 21 to L §3 seam 16 and errata 0013 #11/#20.

6. **[blocking — seams 14 and 21] Names the ledger cites do not exist in `seams.md`.** f4 and f7: `DataMeter`, `JobsTelemetry`, `NotificationsTelemetry`, `ExcelMeters` (L errata 0011 #18, 0018 D1, 0019 §3.1) are `<Area>TelemetryNames` in seams; `IdentityInvitationStatus` (L seam 21) was dropped for `InviteStatus`. A spec author following the ledger's errata would name constants the seams file does not define. *Resolution:* apply §24 resolutions 5 and 18 to L §3 seams 14/21 and the three errata entries.

7. **[blocking — seams 4, 8, 9, 13, 19] Remaining `seams.md` §24 additions absent from the ledger.** f8–f13: `ImportRequest.Concurrency: ConcurrencyMode?`, `ExportOutcome.ExportId`/`ImportOutcome.ImportId`, `RequestContextSnapshot.Client`, `SchedulePausedReason`, `IQueryexSchemaProvider.Fingerprint`, `DeleteByIdsAsync(ids, expectedStamps?)`, plus the placement of `QueryRowSet`/`RelatedEntities` in `.Data` (§24 #4) and `50401` as the in-transaction re-check (§24 #11). None contradicts a ledger decision, but the ledger says it wins on disagreement, so the additions must be ratified there. *Resolution:* add a ledger §3 line per seam "adopts seams.md §24 items n, m" or fold the members into the seam entries; delete errata 0018 D9's "derived only" wording.

8. **[non-blocking — brain-dump a17] The public-permissions mechanism is never stated as a ledger decision.** The briefing §5 hint asked for `IsPublic` versus a well-known role versus a separate table to be compared; the ledger only records "No seeded `Public` role" (§2.29) and the guard code `Access.PublicRoleHasMembers`. The answer (`Role.IsPublic` with the no-members and no-wildcard-filter invariants) lives in T users-roles D8/D21 and S §11.3–11.4/§16. *Resolution:* add a §2 entry (or a sentence in §2.29): "Public permissions are an `IsPublic` flag on `Role`; memberships on a public role and wildcard resources with filters are validation errors; the prologue unions public roles into every caller's grants."

9. **[non-blocking — brain-dump a78] The pack language-support declaration has no contract.** T settings D13 answers the compliance-module question with "a pack may declare `SupportedLanguages`, mismatch is a startup warning", but no such member exists on `ITellmaFeature`/`FeatureDeclaration` (S §6) or `ILanguageCatalog` (S §22), and the ledger does not list it as deferred (§7). *Resolution:* either add `FeatureDeclaration.SupportedLanguages(codes)` to S §6 with the realised-gate warning, or record in L §7 that packs declare nothing in this release and the question is closed as "no restriction".

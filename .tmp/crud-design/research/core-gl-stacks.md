# Research: Core and GL reference stacks (theme `core-gl-stacks`, future spec 0017)

All findings were verified on **2026-09-01** unless a finding says otherwise. Each finding is
labelled **Verified** (read directly from the cited primary source or from this repo) or
**Inference** (a conclusion drawn from verified facts; not itself stated by a source). Repo facts
cite file paths at the `main` commit `12f45dd`; EF Core facts cite the local checkout
`C:\Users\ahmad\workspace\efcore` at `9d1b795935` (branch `release/10.0`, 2026-06-09).

---

## 1. JSON Merge Patch (RFC 7396) vs JSON Patch (RFC 6902) in .NET 10 / ASP.NET Core 10

### 1.1 `Microsoft.AspNetCore.JsonPatch.SystemTextJson` — status and version

**Verified.**
- Latest stable: **10.0.11, released 2026-08-11**; previous 10.0.10 (2026-07-14), 10.0.9
  (2026-06-09), 10.0.8 (2026-05-12). Targets `net10.0` only. It ships on the ASP.NET Core 10
  monthly servicing train.
- The ASP.NET Core 10 docs (page `ms.date` 2026-05-27): "JSON Patch support in ASP.NET Core web
  API is based on System.Text.Json serialization, starting with .NET 10", and it "requires the
  `Microsoft.AspNetCore.JsonPatch.SystemTextJson` NuGet package".
- Limitations stated by the docs: "isn't a drop-in replacement for the legacy `Newtonsoft.Json`-based
  implementation. It doesn't support dynamic types, such as `ExpandoObject`." `ApplyTo` "generally
  follows the conventions and options of System.Text.Json", honouring `JsonNumberHandling` and
  `PropertyNameCaseInsensitive`; "The runtime type of the target object, not the declared type,
  determines which properties `ApplyTo` patches"; the target "is modified in place. The caller is
  responsible for discarding changes if any operation fails."
- Minimal API usage: `MapPatch` with a `JsonPatchDocument<T>` handler parameter bound from the
  body; the sample adds `.Accepts<JsonPatchDocument<Customer>>("application/json-patch+json")`.
  Controllers use `[FromBody] JsonPatchDocument<T>`; content type `application/json-patch+json`.
- Security: the docs state the JSON Patch standard "has inherent security risks" and "the ASP.NET
  Core implementation doesn't attempt to mitigate inherent security risks"; recommended
  mitigations are to validate document size/structure (e.g. cap `copy` operations) before
  `ApplyTo`, and to patch "POCOs with explicitly defined properties that are safe to modify".
- ASP.NET Core 10 release notes: OpenAPI generation "now correctly applies the
  `application/json-patch+json` media type" to JSON Patch request bodies (the only JSON Patch
  change listed for 10). The release notes list no other JSON Patch feature and no merge-patch
  feature.
- Source (`dotnet/aspnetcore` `main`, `src/Features/JsonPatch.SystemTextJson/src/JsonPatchDocumentOfT.cs`):
  `public class JsonPatchDocument<TModel> : IJsonPatchDocument, IEndpointParameterMetadataProvider where TModel : class`;
  no `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` attributes; it holds a
  `JsonSerializerOptions SerializerOptions` and resolves members through
  `SerializerOptions.GetTypeInfo()` (`JsonTypeInfo.Properties`).

**Inference.** Because member resolution goes through `JsonSerializerOptions.GetTypeInfo`, a
source-generated `JsonSerializerContext` attached to the options should in principle be honoured,
but this is not documented and the "runtime type of the target" rule means each concrete leaf
type must be registered; treat source-generation compatibility as **unverified**.

Sources: https://www.nuget.org/packages/Microsoft.AspNetCore.JsonPatch.SystemTextJson ·
https://learn.microsoft.com/en-us/aspnet/core/web-api/jsonpatch?view=aspnetcore-10.0 ·
https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0 ·
https://raw.githubusercontent.com/dotnet/aspnetcore/main/src/Features/JsonPatch.SystemTextJson/src/JsonPatchDocumentOfT.cs

**Implication.** JSON Patch is available first-party on .NET 10, but it brings an operation-based
attack surface, in-place mutation with caller-side rollback, and a reflection-shaped member lookup
that sits awkwardly with the source-generated JSON the web surface wants.

### 1.2 JSON Merge Patch — the standard and first-party support

**Verified.**
- RFC 7396 (October 2014, obsoletes RFC 7386): media type `application/merge-patch+json`; the
  merge algorithm recurses into objects; "Null values in the merge patch are given special meaning
  to indicate the removal of existing values in the target"; "It is not possible to patch part of a
  target that is not an object, such as to replace just some of the values in an array" (arrays are
  replaced whole); consequently a merge patch cannot set a property *to* null.
- ASP.NET Core ships **no** merge-patch support: neither the JSON Patch page nor the ASP.NET Core
  10 release notes mention RFC 7396 (verified by absence in both primary docs).
- Community package `Morcatko.AspNetCore.JsonMergePatch.SystemText`: latest 6.0.4, released
  2025-03-14, targets `net6.0`, ~622K total downloads — i.e. not maintained against .NET 10.

Sources: https://www.rfc-editor.org/rfc/rfc7396.html ·
https://www.nuget.org/packages/Morcatko.AspNetCore.JsonMergePatch.SystemText

**Implication.** A merge-patch style edit for settings has to be hand-rolled (a typed DTO of
optional members, or a `JsonElement`/`JsonObject` merged server-side); there is no framework piece
to adopt, and the RFC's "null means remove" must be mapped explicitly onto nullable settings such
as `SecondaryLanguage`.

### 1.3 The typical choice for "edit a few fields out of many"

**Verified.**
- Microsoft Azure REST API Guidelines: "DO create and update resources using PATCH [RFC 5789] with
  JSON Merge Patch [(RFC 7396)]"; "DO accept JSON fields with a null value only for a PATCH
  operation with a JSON Merge Patch payload. A field with a value of null instructs the service to
  delete the field"; and "YOU SHOULD NOT have a property of an updatable resource whose value is an
  array of polymorphic objects. Updating an array property with JSON merge-patch is not
  version-resilient".
- Google AIP-134 (standard Update): "Google APIs generally use the `PATCH` HTTP verb only, and do
  not support `PUT` requests"; partial updates use an `update_mask` (`google.protobuf.FieldMask`);
  "the service must treat an omitted field mask as an implied field mask equivalent to all fields
  that are populated".
- ASP.NET Core 10 adds built-in Minimal API validation (`AddValidation()`, automatic discovery of
  handler parameter types, DataAnnotations on records, `DisableValidation()` per endpoint,
  `IProblemDetailsService` integration) — usable on a typed patch DTO.

Sources: https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md ·
https://google.aip.dev/134 ·
https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0

**Inference.** Both dominant public API style guides converge on "send only the fields you want
changed" (merge-patch or an explicit field mask) rather than operation lists; JSON Patch is the
exception. For a POST-everywhere web surface with typed, source-generated bodies, the compatible
shape is a typed "partial settings" DTO where absent means untouched, plus either (a) an explicit
`Fields` list (field-mask style, distinguishes "set to null" from "untouched") or (b) RFC 7396
semantics where JSON `null` resets the setting. Option (a) is the safer fit for nullable settings.

**Implication.** The settings edit API should be a partial-update DTO per settings category (the
category doubling as the permission resource), not `JsonPatchDocument<T>`; "load-and-save-back"
remains viable only if the concurrency stamp protects the whole row.

---

## 2. SQL Server `hierarchyid` maintenance for bulk reparenting

### 2.1 What the engine guarantees (and does not)

**Verified** (SQL docs; `GetReparentedValue`/`GetDescendant` pages `ms.date` 2017-07-22, updated
2026-08-24; Hierarchical Data page `ms.date` 2025-05-19; method reference `ms.date` 2025-11-03; all
"Applies to" SQL Server, Azure SQL Database, Azure SQL Managed Instance, Fabric SQL database):
- `node.GetReparentedValue(oldRoot, newRoot)` "Returns a node whose path from the root is the path
  to *newRoot*, followed by the path from *oldRoot*." "The hierarchyid data type represents but
  doesn't enforce the hierarchical structure. Users must ensure that the hierarchyid is
  appropriately structured for the new location. A unique index on the hierarchyid data type can
  help prevent duplicate entries."
- `parent.GetDescendant(child1, child2)`: `(NULL, NULL)` returns a child; `(child1, NULL)` a child
  greater than `child1`; `(NULL, child2)` a child less than `child2`; both non-null → between;
  raises an exception if a child argument is not a child of `parent` or `child1 >= child2`.
  "GetDescendant is deterministic."
- Limitations: "It's up to the application to manage concurrency in generating and assigning
  hierarchyid values. There's no guarantee that hierarchyid values in a column are unique unless
  the application uses a unique key constraint"; relationships "aren't enforced like a foreign key
  relationship"; comparison and indexes are depth-first; encoding is limited to 892 bytes;
  `ToString()` yields `nvarchar(4000)`; "Moving nonleaf nodes is slower with hierarchyid" (a move
  "affects *n* rows, where *n* is number of nodes in the subtree being moved"); "Subtree queries
  are significantly faster with hierarchyid" while "Direct descendant queries are slightly slower".
- Documented subtree move (`MoveOrg`): `SET TRANSACTION ISOLATION LEVEL SERIALIZABLE; ... SELECT
  @nnew = @nnew.GetDescendant(max(OrgNode), NULL) FROM ... WHERE OrgNode.GetAncestor(1) = @nnew;
  UPDATE ... SET OrgNode = OrgNode.GetReparentedValue(@nold, @nnew) WHERE
  OrgNode.IsDescendantOf(@nold) = 1;` — one moved root per statement, with `MAX(child)` under the
  new parent to avoid a sibling collision. Lesson 2 shows the multi-root variant as a cursor loop
  with `IF @@error <> 0 GOTO START` retry on duplicate-key violation.
- Documented insert patterns: compute `@last_child = MAX(node) WHERE node.GetAncestor(1) = @parent`
  (needs a breadth-first index) then `@parent.GetDescendant(@last_child, NULL)`, either under a
  unique index with retry on key violation or inside a serializable transaction; or track
  `LastChild` on the parent row.
- Tree enforcement option: `ParentId AS EmployeeId.GetAncestor(1) PERSISTED FOREIGN KEY REFERENCES
  Org_T3(EmployeeId)` — "preferred when code that isn't trusted to maintain the hierarchical tree
  has direct DML access", at a per-DML cost.

Sources: https://learn.microsoft.com/en-us/sql/t-sql/data-types/getreparentedvalue-database-engine?view=sql-server-ver17 ·
https://learn.microsoft.com/en-us/sql/t-sql/data-types/getdescendant-database-engine?view=sql-server-ver17 ·
https://learn.microsoft.com/en-us/sql/relational-databases/hierarchical-data-sql-server?view=sql-server-ver17 ·
https://learn.microsoft.com/en-us/sql/t-sql/data-types/hierarchyid-data-type-method-reference?view=sql-server-ver17 ·
https://learn.microsoft.com/en-us/sql/relational-databases/tables/lesson-2-creating-and-managing-data-in-a-hierarchical-table?view=sql-server-ver17

**Implication.** `GetReparentedValue` + `GetDescendant(MAX(sibling), NULL)` is a per-moved-root
operation that must run under a unique index on `Node` (and serializable isolation or key-violation
retry); it does not scale to "N arbitrary rows moved in one save" without a loop.

### 2.2 Generating many new sibling nodes in one statement without collisions

**Verified.** The documented bulk pattern (Lesson 1, `ms.date` 2026-01-05) does not call
`GetDescendant` at all. It numbers siblings and builds paths in a recursive CTE:

```sql
-- 1. number the children of each parent (recursive queries forbid aggregates, hence ROW_NUMBER)
INSERT INTO #Children (EmployeeID, ManagerID, Num)
SELECT EmployeeID, ManagerID,
       ROW_NUMBER() OVER (PARTITION BY ManagerID ORDER BY ManagerID)
FROM HumanResources.EmployeeDemo;

-- 2. build '/a/b/c/' strings top-down and cast once
WITH Paths (path, EmployeeID) AS (
    SELECT hierarchyid::GetRoot() AS OrgNode, EmployeeID
    FROM #Children AS C WHERE ManagerID IS NULL
    UNION ALL
    SELECT CAST (p.path.ToString() + CAST (C.Num AS VARCHAR (30)) + '/' AS hierarchyid),
           C.EmployeeID
    FROM #Children AS C INNER JOIN Paths AS p ON C.ManagerID = P.EmployeeID)
INSERT INTO HumanResources.NewOrg (OrgNode, ...) SELECT P.path, ... FROM ... JOIN Paths AS P ...;
```

Source: https://learn.microsoft.com/en-us/sql/relational-databases/tables/lesson-1-converting-a-table-to-a-hierarchical-structure?view=sql-server-ver17

**Inference.** For the save pipeline, the collision-free bulk statement is a *recompute from
`ParentId`*: `ROW_NUMBER() OVER (PARTITION BY ParentId ORDER BY <stable key, e.g. Id>)` inside a
recursive CTE rooted at the affected subtree roots (or the whole table for a reference-data tree
of hundreds to low thousands of rows), then `UPDATE ... SET Node = Paths.path WHERE Node <> Paths.path
OR Node IS NULL`. It is deterministic (stable ordering), handles inserts and moves in one pass,
never calls `GetDescendant`, and is guarded by the unique index on `Node`. Cost is O(affected
subtree) writes, which the docs already attribute to any non-leaf move. A `SubtreeCount` /
`ActiveSubtreeCount` refresh can ride the same batch either as a second recursive CTE over
`ParentId` or as a self-join `d.Node.IsDescendantOf(p.Node) = 1 GROUP BY p.Id` (a depth-first range
seek per parent); which is cheaper at Center scale is **unverified** and should be measured on
LocalDB in T2's fixture tests. Cycle validation cannot be done by the CTE (it would recurse until
`MAXRECURSION`); validate the ancestor chain in C# before persisting, as the briefing suggests.

**Implication.** Prefer one appended recompute statement (CTE + `ROW_NUMBER`) over per-row
`GetReparentedValue`; keep `GetReparentedValue` for the rare "move one subtree, preserve sibling
nodes" action if the design wants stable node values.

### 2.3 Indexing: depth-first on `Node`, breadth-first on level + `Node` (not level + `ParentId`)

**Verified.** The docs' canonical definitions:

```sql
CREATE TABLE Organization (
    BusinessEntityID HIERARCHYID,
    OrgLevel AS BusinessEntityID.GetLevel(),
    EmployeeName NVARCHAR(50) NOT NULL);
CREATE CLUSTERED INDEX Org_Breadth_First ON Organization (OrgLevel, BusinessEntityID);
CREATE UNIQUE INDEX Org_Depth_First ON Organization (BusinessEntityID);
```

"In a depth-first index, all nodes in the subtree of a node are colocated" (subtree queries); "In
a breadth-first index, all direct children of a node are colocated" (immediate-children queries).
Lesson 1 uses a non-clustered variant: `ALTER TABLE ... ADD H_Level AS OrgNode.GetLevel();
CREATE UNIQUE INDEX EmpBFInd ON HumanResources.NewOrg(H_Level, OrgNode);` with the PK clustered on
`OrgNode`. The legacy Tellma schema (see §5) did the same: `[Node] HIERARCHYID NOT NULL CONSTRAINT
[UQ_Centers__Node] UNIQUE CLUSTERED`, `[Level] AS [Node].GetLevel()`, `[Id] INT PRIMARY KEY
NONCLUSTERED`.

Sources: https://learn.microsoft.com/en-us/sql/relational-databases/hierarchical-data-sql-server?view=sql-server-ver17 ·
https://learn.microsoft.com/en-us/sql/relational-databases/tables/lesson-1-converting-a-table-to-a-hierarchical-structure?view=sql-server-ver17

**Inference.** Because `Center` keeps a real `ParentId` FK column, "direct children" queries
(`GetByParentIds`) are served by a plain index on `ParentId` and do not need the breadth-first
`(Level, Node)` index; that index only pays for level-scoped predicates (the `level()` Queryex
function). `Level` can be a computed column `Node.GetLevel()` (persisted if indexed), which answers
the briefing's "derivable column" critique without storing it; `IsLeaf` is likewise derivable from
`SubtreeCount = 1`.

**Implication.** Minimum index set for `gl.Center`: unique index on `Node` (depth-first; backs
`descendantOf`/`ancestorOf`), index on `ParentId`, unique on `Code`; add `(Level, Node)` only if
`level()` predicates are expected.

### 2.4 EF Core 10 and driver facts that constrain the design

**Verified.**
- EF Core docs: HierarchyId support since EF Core 8 via `Microsoft.EntityFrameworkCore.SqlServer.HierarchyId`
  (`options.UseSqlServer(cs, x => x.UseHierarchyId())`); the CLR type `HierarchyId` lives in
  `Microsoft.EntityFrameworkCore.SqlServer.Abstractions` (no other dependencies); the HierarchyId
  package "brings in `Microsoft.EntityFrameworkCore.SqlServer.Abstractions` and
  `Microsoft.SqlServer.Types` as transitive dependencies". Translated members: `GetAncestor(n)`,
  `GetDescendant(child)`/`(child1, child2)`, `GetLevel()`, `GetReparentedValue(oldRoot, newRoot)`,
  `HierarchyId.GetRoot()`, `IsDescendantOf(parent)`, `HierarchyId.Parse(s)`, `ToString()`, and the
  comparison operators. "The parameters values for HierarchyId properties are sent to the database
  in their compact, binary format."
- Local EF 10 source: `SqlServerHierarchyIdTypeMapping.ConfigureParameter` sets
  `SqlDbType.Udt` and `UdtTypeName = "hierarchyid"`; `SqlServerHierarchyIdValueConverter` is a
  `ValueConverter<HierarchyId?, Microsoft.SqlServer.Types.SqlHierarchyId>`; EF pins
  `Microsoft.SqlServer.Types` **160.1000.6** (`Directory.Packages.props`).
- NuGet `Microsoft.SqlServer.Types`: latest **170.1000.7, released 2025-11-19**, targets
  `netstandard2.0` and `net472` (so it loads on .NET 10 on Linux/Windows).
- `Microsoft.Data.SqlClient` `SqlMetaData` (TVP column metadata): `SqlDbType.Udt` requires a
  `Type userDefinedType`; the constructor throws `ArgumentException` when "`userDefinedType` points
  to a type that does not have `SqlUserDefinedTypeAttribute` declared"; an overload also takes
  `string serverTypeName`.
- `CREATE TYPE ... AS TABLE` documents no restriction on column data types beyond `CREATE TABLE`
  rules; `hierarchyid` is a system type usable on Azure SQL Database (where user CLR is not
  supported). Whether spec 0001's row-image derivation already handles a `hierarchyid` store type
  is **unverified** (no `hierarchyid`/`Udt` handling found under `src/core` outside Queryex).
- Spec 0001 provides a per-property exclusion attribute `[ExcludeFromTableType]`
  (`docs/specs/0001-efcore-table-types.md`, line 584) and fluent exclusions.

Sources: https://learn.microsoft.com/en-us/ef/core/providers/sql-server/hierarchyid ·
local `src/EFCore.SqlServer.HierarchyId/Storage/Internal/SqlServerHierarchyIdTypeMapping.cs`,
`.../Storage/ValueConversion/Internal/SqlServerHierarchyIdValueConverter.cs`, `Directory.Packages.props` ·
https://www.nuget.org/packages/Microsoft.SqlServer.Types ·
https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.server.sqlmetadata.-ctor ·
https://learn.microsoft.com/en-us/sql/t-sql/statements/create-type-transact-sql?view=sql-server-ver17

**Implication.** Binding `Node` inside a TVP requires `Microsoft.SqlServer.Types.SqlHierarchyId`
(the only type SqlClient accepts for a `Udt` column); the cleaner design is to exclude `Node` from
the Center UDTT (`[ExcludeFromTableType]`) and let the appended recompute statement write it, so
the bulk-save binder stays free of `Microsoft.SqlServer.Types`, while the EF model (already
depending on it transitively) reads `Node` back as `HierarchyId`.

---

## 3. Identity server integration facts (from `src/apps/Tellma.Identity`)

### 3.1 Authentication and the scope

**Verified.**
- Both endpoints sit on `InvitationsController`, decorated
  `[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme, Policy = ApiPolicies.IdentityScope)]`.
  The policy (`Infrastructure/ApiPolicies.cs`) requires an authenticated principal whose
  space-delimited `scope` claim contains `TellmaIdentityConstants.IdentityScope = "tellma_identity"`.
  Validation is same-process (`AddValidation(... UseLocalServer())`).
- The distribution's client (`Services/Provisioning/ClientDescriptorFactory.DistributionService`):
  `ClientId = "<slug>-svc"`, confidential, `ConsentType = Implicit`, permissions: token +
  revocation endpoints, grant `client_credentials` (+ token exchange when allowed), scopes
  `tellma_api` and `tellma_identity`, resources = the distribution origin and the identity issuer
  origin; properties `Origin = <origin>` and `FirstParty = true`.
- Token recipe used by the integration tests (`test/apps/Tellma.Identity.IntegrationTests/Api/InvitationApiTests.cs`):
  `POST /connect/token` (form): `grant_type=client_credentials`, `client_id=<slug>-svc`,
  `client_secret=...`, `scope=tellma_identity`, `resource=<identity issuer origin>`; read
  `access_token` from the JSON. In in-proc mode the OpenIddict endpoints are prefixed with the
  configured `PathBase` (`OpenIddictConfigurator.Prefixed(prefix, "connect/token")`). Access
  tokens live 10 minutes (spec 0003 §6.3).
- Anonymous call → **401** (test `Invite_requires_the_identity_scope`; delivery-status has the same
  test). **Inference:** a token lacking the scope fails `RequireAssertion` → 403.
- Controller routes are absolute (`api/identity/...`) and are *not* passed through
  `RoutePrefix`; whether an in-proc host maps them under `PathBase` is **unverified**
  (`Hosting/IdentityConfigurator.cs:120` applies `PathBase` only to cookie paths).

### 3.2 Bulk invite

**Verified** (`Controllers/InvitationsController.cs`, `Controllers/Api/InvitationDtos.cs`,
`Services/Invitations/InvitationService.cs`, `Services/Invitations/InvitationReturnUrlValidator.cs`).
- Route: `POST api/identity/invitations`. JSON is camelCase (tests read `results`, `email`, `sub`,
  `status`, `error`).
- Request `InviteUsersRequest`: `users` — 1..1000 items (`[MinLength(1)] [MaxLength(1000)]`), each
  `InviteUserItem { email (required, [EmailAddress]); displayName?; locale? (BCP 47, defaults to
  "en" when blank); gender? ("female" | "male", unrecognised → unstated/neutral); returnUrl? }`.
  `[ApiController]` model validation returns 400 problem details for violations (framework
  behaviour; **inference**).
- Response `InviteUsersResponse { results: InviteUserResult[] }` in request order, one per user,
  except that caller cancellation stops the batch after the users already processed (a prefix).
  `InviteUserResult { email; sub: string?; status: "Invited" | "Reinvited" | "Active" | null;
  error: string? }` — status+sub or error, never both.
- Status semantics (`InvitationService.CreateOrGetAsync`):
  - `Invited`: new identity user created (`Id = Guid.NewGuid().ToString("D")`, `LifecycleState = Active`,
    `EmailConfirmed = false`), a single-use link (7-day lifetime, `InvitationLifetime`) queued.
  - `Reinvited`: existing user that is either credential-less `Active`, or `Orphaned` (restored to
    `Active` first, audited); link queued.
  - `Active`: existing `Active` user holding a passkey, password, or external login — **no email is
    sent**; "If this membership is new to the caller, telling the user about it is the caller's
    responsibility."
- Per-user error strings emitted by the code: "The return url is not a destination this client is
  registered to receive users at."; "The user cannot be invited; an operator must re-enable the
  account first." (`Disabled` or `Purged`, deliberately indistinguishable); "The user could not be
  restored from the orphaned state."; "The user could not be created: <identity errors>"; "The user
  could not be invited." (unexpected exception; a user row created before the failing step stays,
  credential-less and `Active`, so a re-invite resolves as `Reinvited`).
- `returnUrl` rule: null, a local path, or an absolute `http(s)` URL whose scheme/host/port equal
  the calling client's registered `Origin` (first-party clients only; no userinfo) — otherwise the
  per-user error above. The URL is held server-side and re-checked when the link is opened.
- Delivery mechanics: emails are handed to the background dispatcher in one batch after the loop;
  a Quartz sweep (`Hosting/QuartzConfigurator.cs`: `InvitationDispatchInterval = 2 min`, start delay
  30 s) claims and sends anything still `Pending`. Each user is one `UserManager` persistence
  operation, so a 1000-user batch is O(n) round trips inside the identity server.
- Identity `sub` format: `Guid.ToString("D")` — 36 characters; the dev admin's fixed subject is
  `00000000-0000-0000-0000-000000000001`.

### 3.3 Bulk delivery status

**Verified** (`InvitationsController.DeliveryStatus`, `InvitationDeliveryStatusService`,
`Data/Entities/SingleUseCode.cs`).
- Route: `POST api/identity/invitations/delivery-status`. Request
  `InvitationDeliveryStatusRequest { subs: string[] }` 1..1000. Response
  `InvitationDeliveryStatusResponse { results: InvitationDeliveryStatusResult[] }` — one per
  requested sub, in request order, duplicates preserved positionally.
- `InvitationDeliveryStatusResult { sub; state; expectsDeliveryEvents: bool; sentUtc: DateTimeOffset?;
  updatedUtc: DateTimeOffset?; reason: string? }`.
- `state` ∈ `NotFound | Pending | Sent | Delivered | Bounced | Complained | Rejected | Abandoned | Accepted`
  (enum `InvitationDeliveryState`, values 0..8). Derivation from the latest invitation code per user
  raised **by the same `client_id`**: `ConsumedUtc` set → `Accepted`; else dispatch
  `Pending`→`Pending`, `Rejected`→`Rejected`, `Abandoned`→`Abandoned`, `Sent`/`Sandboxed`→ provider
  report: `Delivered`→`Delivered`, `SpamReported`→`Complained`, `Bounced`/`Dropped`/`Failed`→`Bounced`,
  `Deferred`/`Other`/none→`Sent`.
- `NotFound` is returned identically for "no such user" and "invited by another client" (anti-probing).
  `reason` is populated only for `Bounced`, `Rejected`, `Abandoned`. `expectsDeliveryEvents = false`
  means `Sent` is terminal (on-prem SMTP relays report nothing).
- A token that carries the scope but no `client_id`/`sub` claim → `Forbid()` (403).

### 3.4 Neighbouring facts the design relies on

**Verified.**
- Service accounts (`Controllers/ServiceAccountsController.cs`, same policy): `POST api/identity/service-accounts`
  `{ displayName, resources[] }` → `{ clientId, clientSecret }` (secret returned once);
  `GET`/`DELETE api/identity/service-accounts/{clientId}` ownership-scoped (spec 0003 §11.4).
- Lifecycle states (`Data/UserLifecycleState.cs`): `Active`, `Orphaned`, `Disabled`, `Purged`; only
  `Active` obtains tokens. The server has no tenant notion, does not list users by tenant, and does
  not reset passwords for a distribution admin (spec 0003 §10.1, §11.4).
- Local development (spec 0003 §10.4; `Options/TellmaIdentitySeedOptions.cs`, `Services/Seeding/IdentitySeeder.cs`):
  the dev admin `admin@localhost` with fixed `Subject = 00000000-0000-0000-0000-000000000001` is
  seeded only when `Seed.DevAdmin.Enabled` **and** the environment is Development; "the distribution
  seeds a matching tenant and admin role on the same `sub`". Deployed instances seed a break-glass
  admin into an empty store from a setup-token hash instead.
- Scopes registered (`Hosting/OpenIddictConfigurator.cs`): in-proc mode registers `email`,
  `profile`, `offline_access`, `tellma_api`, `tellma_identity` (no control-plane scope).
- SignalR session rules (spec 0003 §7.4): cookie-authenticated at negotiate; with Azure SignalR a
  hub-scoped service token whose claims are pruned to the subject via `ClaimsProvider`; every
  session-ending event closes the user's connections (`users/{user}/:closeConnections` on Azure
  SignalR; tracked abort self-hosted); `CloseOnAuthenticationExpiration` enabled; thin events only.

Sources (repo): `src/apps/Tellma.Identity/Controllers/InvitationsController.cs`,
`Controllers/Api/InvitationDtos.cs`, `Controllers/Api/ServiceAccountDtos.cs`,
`Controllers/ServiceAccountsController.cs`, `Infrastructure/ApiPolicies.cs`,
`TellmaIdentityConstants.cs`, `Services/Invitations/*.cs`, `Data/Entities/SingleUseCode.cs`,
`Data/UserLifecycleState.cs`, `Services/Provisioning/ClientDescriptorFactory.cs`,
`Hosting/OpenIddictConfigurator.cs`, `Hosting/QuartzConfigurator.cs`,
`Options/TellmaIdentitySeedOptions.cs`, `test/apps/Tellma.Identity.IntegrationTests/Api/*.cs`;
`docs/specs/0003-identity-server.md` §6.2, §7.4, §10.1–10.6, §11.4.

**Implication.** `UserService.Invite` is a chunked (≤1000) M2M call made **outside** any tenant DB
transaction, writing back per-user: `Subject` (36-char string, unique per tenant), tenant state
`Invited` for `Invited`/`Reinvited`, `Active`-with-no-email for `Active` (the tenant must send its
own "you were added" notice), and the per-user error verbatim for refusals; the admin's fine-grained
view is a second bulk call to delivery-status whose `NotFound`/`expectsDeliveryEvents`/`reason`
fields must be rendered as designed above. The local-dev bootstrap must seed the tenant admin on
sub `00000000-0000-0000-0000-000000000001` only in Development.

---

## 4. SignalR in ASP.NET Core 10

### 4.1 `IUserIdProvider`, per-user sends, groups

**Verified** (docs `signalr/groups` `ms.date` 2024-04-04, `signalr/authn-and-authz` `ms.date`
2026-08-25, both updated 2026-07/08 for the aspnetcore-10.0 moniker).
- "By default, SignalR uses the `ClaimTypes.NameIdentifier` from the `ClaimsPrincipal` associated
  with the connection as the user identifier." Customise by implementing `IUserIdProvider.GetUserId(HubConnectionContext)`
  and registering `builder.Services.AddSingleton<IUserIdProvider, ...>()`. "The user identifier is
  case-sensitive."
- `Clients.User(userId)` delivers to every connection of that user ("a user could be connected on
  their desktop as well as their phone"); there is no need for a hand-made per-user group.
- Groups: `Groups.AddToGroupAsync(connectionId, name)`; "Group membership isn't preserved when a
  connection reconnects"; "Groups are kept in memory, so they won't persist through a server
  restart"; "It's not possible to count the members of a group"; "groups are not a security
  feature"; group names are case-sensitive.
- Connection principal is captured once: "SignalR doesn't automatically revalidate the user during
  the life of the connection, regardless of the authentication scheme"; the remedy is to "Close
  affected connections so that clients reconnect and reauthenticate", and
  `CloseOnAuthenticationExpiration` (an `HttpConnectionDispatcherOptions` setting) closes a
  connection when its authentication ticket expires.
- "Starting with ASP.NET Core 10, known API endpoints no longer redirect to login pages when using
  cookie authentication. Instead, they return 401/403 status codes."
- Authentication refresh (`EnableAuthenticationRefresh`, `OnAuthenticationRefresh`) is documented as
  "available in .NET 11 and later" — not in 10. The ASP.NET Core 10 release notes' SignalR section
  lists no features.

### 4.2 Sending from a background service (`IHubContext`)

**Verified** (`signalr/hubcontext`, `signalr/scale` `ms.date` 2026-07-06).
- `IHubContext<THub>` (or strongly typed `IHubContext<THub, TClient>`) "can be injected into a
  controller, middleware, or other DI service"; "When client methods are called from outside of the
  Hub class, there's no caller associated with the invocation" (no `ConnectionId`/`Caller`/`Others`);
  an `IHubContext<THub>` can be cast to non-generic `IHubContext` for library code that does not
  know the hub type.
- Scale-out: without a backplane "When SignalR on one of the servers wants to send a message to all
  clients, the message only goes to the clients connected to that server." Options: Azure SignalR
  Service ("functions as a proxy for real-time traffic and doubles as a backplane"; "the
  recommendation is to use the Azure SignalR Service for all ASP.NET Core SignalR apps hosted on
  Azure") or the Redis backplane ("recommended scale-out approach for apps hosted on your own
  infrastructure"). Sticky sessions are required for self-hosted farms (including with Redis) unless
  clients are WebSockets-only with `SkipNegotiation`; not required with Azure SignalR.

### 4.3 Azure SignalR Service SDK and data plane

**Verified.**
- `Microsoft.Azure.SignalR`: latest **1.33.1, released 2026-06-24**; targets `net8.0` and
  `netstandard2.0`; 1.33.0 (2026-03-04) and earlier are marked deprecated on NuGet. GitHub release
  notes: v1.32.0 (2025-09-10) "Dropped support for .NET 6" (targets .NET 8 and .NET 9); v1.33.1
  mentions bumping the .NET SDK 10.x in `release.yml`, new service protocols
  (`RefreshAuthMessage`, `GetConnectionClaimsMessage`, `UpdateConnectionClaimsMessage`) and
  MessagePack CVE bumps. **Inference:** it runs on .NET 10 through its `net8.0` asset; no explicit
  `net10.0` target exists.
- `Microsoft.Azure.SignalR.Management`: 1.33.1, 2026-06-24, same targets (for sending from a
  process that hosts no hub).
- SDK options (`signalr-howto-use`, `ms.date` 2024-04-18): `services.AddSignalR().AddAzureSignalR()`
  with connection string from `Azure:SignalR:ConnectionString`; `ClaimsProvider` — "By default,
  all claims from `HttpContext.User` of the negotiated request are reserved" in the client token;
  `AccessTokenLifetime` default 1 hour; `ServerStickyMode` (`Disabled` default; `Required` when
  per-connection state is captured at negotiate); `GracefulShutdown.Mode` (`WaitForClientsClose` /
  `MigrateClients`); "Each client connection is only created in one of the application servers".
- FAQ: a custom `IUserIdProvider` receives a *logical* `HubConnectionContext` under the Azure SDK —
  "only `HubConnectionContext.GetHttpContext()` and `HubConnectionContext.User` are available".
- Data-plane REST (latest version `20241201`, doc `ms.date` 2026-06-03/04): `POST /api/hubs/{hub}/users/{user}/:send`,
  `POST /api/hubs/{hub}/users/{user}/:closeConnections` (204; optional `reason`, `excluded`),
  `HEAD /api/hubs/{hub}/users/{user}`, `PUT /api/hubs/{hub}/users/{user}/groups/{group}` (optional
  `ttl`), `DELETE .../users/{user}/groups`; auth via HS256 JWT signed with the AccessKey (`aud` =
  request URL) or a Microsoft Entra token (`https://signalr.azure.com/.default`); body ≤ 1 MB; the
  service identifies a client's user from the `nameid` claim of the client token.

Sources: https://learn.microsoft.com/en-us/aspnet/core/signalr/groups?view=aspnetcore-10.0 ·
https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0 ·
https://learn.microsoft.com/en-us/aspnet/core/signalr/hubcontext?view=aspnetcore-10.0 ·
https://learn.microsoft.com/en-us/aspnet/core/signalr/scale?view=aspnetcore-10.0 ·
https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0 ·
https://www.nuget.org/packages/Microsoft.Azure.SignalR ·
https://www.nuget.org/packages/Microsoft.Azure.SignalR.Management ·
https://api.github.com/repos/Azure/azure-signalr/releases ·
https://learn.microsoft.com/en-us/azure/azure-signalr/signalr-howto-use ·
https://learn.microsoft.com/en-us/azure/azure-signalr/signalr-resource-faq ·
https://learn.microsoft.com/en-us/azure/azure-signalr/swagger/signalr-data-plane-rest-v20241201

**Inference.** Because one distribution host serves many tenants and a `sub` may hold sessions in
several of them, the SignalR user identifier must be tenant-qualified (e.g. `"{tenantId}:{sub}"`
from a custom `IUserIdProvider` that reads only `HubConnectionContext.User`/`GetHttpContext()`), or
else per-user sends must go to a `tenant:{id}` group; `Clients.User(...)` then gives tenant
isolation for free and works from any instance through the service. Self-hosted on-prem must be
single-instance or add the Redis backplane.

**Implication.** T10's hub is one Core class; UserService's "test notification" and the
background-task runner send through `IHubContext<TellmaHub>` (singleton-safe), keyed by the
tenant-qualified user id; deactivation calls the same "close user connections" seam spec 0003
already requires.

---

## 5. Cost-centre / responsibility-centre taxonomies — sanity check of `CenterType`

### 5.1 Accounting taxonomy

**Verified.**
- Responsibility centres: "an organizational unit headed by a manager, who is responsible for its
  activities and results" (Wikipedia). The standard four types — **cost (expense) centre**
  (incurs costs, no direct revenue; e.g. janitorial, IT, accounting), **revenue centre** (sales
  only), **profit centre** (revenues and expenses; e.g. a product line), **investment centre**
  (profit plus return on invested capital; e.g. a subsidiary; measured by ROI/RI/EVA) — from
  AccountingTools (article dated 2026-03-07) and Lumen Learning (which folds "service centers like
  maintenance or accounting departments" into expense centres and omits revenue centres).
  A department "can function as more than one type of responsibility center".
- Cost centres subdivide into **production cost centres** ("where the products are manufactured or
  processed") and **service cost centres** ("units providing services to other business areas",
  e.g. personnel, logistics, canteen) (Wikipedia "Cost centre"). The CIMA wording "a production or
  service location, function, activity, or item of equipment whose costs may be attributed to cost
  units" appears only in secondary sources (not verified against CIMA's own text).

Sources: https://en.wikipedia.org/wiki/Responsibility_center ·
https://www.accountingtools.com/articles/what-is-a-responsibility-center.html ·
https://courses.lumenlearning.com/suny-managacct/chapter/responsibility-accounting-in-business-environments/ ·
https://en.wikipedia.org/wiki/Cost_centre_(business)

### 5.2 The legacy Tellma model

**Verified** (`tellma-ltd/tellma`, branch `master`,
`Tellma.Database.Application/dbo/Tables/dbo.Centers.sql`):

```sql
[CenterType] NVARCHAR (255) NOT NULL,
CONSTRAINT [CK_Centers__CenterType] CHECK ([CenterType] IN (
    N'Abstract', N'BusinessUnit', N'Administration', N'Marketing', N'Service', N'Operation',
    N'Sale', N'FinanceCost', N'OtherPL',
    N'ConstructionInProgressExpendituresControl',
    N'InvestmentPropertyUnderConstructionOrDevelopmentExpendituresControl',
    N'WorkInProgressExpendituresControl',
    N'CurrentInventoriesInTransitExpendituresControl')),
...
[Node]   HIERARCHYID NOT NULL CONSTRAINT [UQ_Centers__Node] UNIQUE CLUSTERED,
[Level]  AS [Node].GetLevel(),
[IsLeaf] BIT NOT NULL DEFAULT 1
         CONSTRAINT [CK_Centers__CenterType_IsLeaf] CHECK ([IsLeaf] = 1 OR [CenterType] IN (N'Abstract', N'BusinessUnit'))
```

Also: `Id INT PRIMARY KEY NONCLUSTERED IDENTITY`, `ParentId` self-FK, `Name/Name2/Name3
NVARCHAR(255)`, `Code NVARCHAR(50) NOT NULL UNIQUE`, `IsActive BIT`, four audit columns
(`DATETIMEOFFSET(7)` + user FKs); `IsLeaf` was maintained by triggers.

Source: https://raw.githubusercontent.com/tellma-ltd/tellma/master/Tellma.Database.Application/dbo/Tables/dbo.Centers.sql

**Inference.** The brain dump's `Service | Operation | Sale` are three of the legacy leaf types
and map onto the textbook taxonomy as service cost centre / production (operating) cost centre /
revenue centre. What the three-value set drops relative to both the literature and the legacy
schema: (a) a **non-posting grouping node** (`Abstract`) and a **profit/investment-centre**
grouping (`BusinessUnit`) — the legacy invariant that *only those two types may have children*
is the rule that keeps postings on leaves; (b) SG&A-style cost centres (`Administration`,
`Marketing`, `FinanceCost`, `OtherPL`), which look like an IAS 1 function-of-expense split (not
verified against IAS 1 text here); (c) the expenditure-control centres that GL modules use for
WIP/CIP capitalisation. Whether (b) and (c) belong in the *reference* GL module or in later packs
is a product decision, but the parent-type rule in (a) is structural and belongs in the tree
capability's validation.

**Implication.** Model `CenterType` as a C# enum stored as a string column with a CHECK
constraint (Queryex compares it as `'Service'`), keep the name `CenterType` (legacy precedent),
seed the set as at least `Abstract | BusinessUnit | Service | Operation | Sale` with a validation
rule that only `Abstract`/`BusinessUnit` centres may have children, and derive `IsLeaf`/`Level`
instead of storing them.

---

## Unverified items (for the designers' Verification section)

- Whether `JsonPatchDocument<T>` on .NET 10 works with a source-generated `JsonSerializerContext`
  (source suggests it resolves through `JsonSerializerOptions.GetTypeInfo`; not documented).
- Whether spec 0001's UDTT derivation accepts a `hierarchyid` store type unchanged (no handling
  found under `src/core`; the SQL `CREATE TYPE` docs impose no restriction).
- Relative cost of `IsDescendantOf` self-join vs recursive `ParentId` CTE for `SubtreeCount`
  refreshes at Center scale (measure on LocalDB).
- Whether an in-proc identity host exposes `api/identity/...` under `PathBase` (controller routes
  are absolute in code).
- `Microsoft.Azure.SignalR` 1.33.1 on .NET 10 is supported via its `net8.0` asset by inference;
  no release note names .NET 10 as a target.
- The CIMA definition wording and the IAS 1 function-of-expense mapping of the legacy SG&A centre
  types were taken from secondary sources.

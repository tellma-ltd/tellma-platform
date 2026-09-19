# Web API surface and the MCP seam — proposal under the performance, operations, correctness, and security lenses

Theme key `web-api-mcp`, future spec 0015. Written 2026-09-01 for a reader who has not seen the
other design files. Every name is intended as final; a spec author should be able to write spec
0015 from this file alone.

Two lenses decide ties here. The first is tier-2 performance and operations: round trips per
operation, bulk shapes, no boxing on the hot path, plan-cache friendliness, bounded memory on every
instance, multi-instance safety, and observability that finds an N+1 on the first day. The second
is correctness and security: fail-closed access control, no silent data loss (lost updates,
precision loss, over-writes from untrusted payloads), a wire that survives an N−1 client through a
deploy, and package seams that keep a distribution compiling across platform minors. Where either
lens conflicts with distribution-author simplicity, the conflict is named and the safer or faster
choice is flagged for review.

An earlier proposal for this theme was written under the distribution-author-simplicity lens. This
file agrees with much of it — three surfaces, POST-only web, array save, one entity class on the
wire, `[ApiAction]` projection, the closed exception set, one MCP endpoint per tenant, RFC 9457
problems — and does not restate what it agrees with beyond what a spec author needs. It
**contradicts** that proposal on eleven points, each marked "Contradiction" in §2: related entities
travel as a display projection, never as full rows (D5); `today()` and the engine's time-zone slot
bind to the tenant zone, never to a user-controlled header (D10); server-owned columns are excluded
by the SQL emitter, not "overwritten by the pipeline" (D6); the concurrency stamp is opaque and a
missing stamp is an error, not a silent last-write-wins (D5); the endpoint filter is an early deny,
and authorization is re-evaluated inside the service (D8); query rows are a columnar buffer, not
`object[][]` (D4); traversing a navigation in a query is subject to a column-level rule (D12);
platform exceptions are mapped by an endpoint filter, not the exception middleware (D11); MCP
exposes no concurrency override and confirms deletes with a signed preview token (D16); the MCP
audience is computed from the configured public origin, never from the `Host` header (D17); and
`related` values are typed sets, not `object` (D5).

---

## 1. Critique

The brain dump's web-layer sections are short and mostly right in direction. The problems below
are the ones a performance or security reviewer would raise on the first read.

**The layer is described as thin, and then asked to do the two things that are not thin.** "The
thin web layer wraps the service methods" is the right ambition, but the same document asks the
web layer to negotiate culture and calendar, to bind `today()`, to enforce rate and payload
limits, to map exceptions, and to serve an MCP server. All of that is real code that runs on every
request, and none of it is designed. The design principle to adopt is that the web layer owns
exactly three things — transport (binding, headers, limits, CSRF, compression), the problem
mapping, and the projection of the catalog onto routes and tools — and that **authorization and
business validation live in the service, where MCP and background jobs reach them too**. A check
that only exists in an endpoint filter is a check the MCP tool bypasses.

**"Save admits a single entity" contradicts the guiding principle and creates a second code path.**
The endpoint takes an array. The UI sends one. Nothing else needs saying.

**"X-Today" is a client-controlled input to a security predicate.** Row-level-security filters are
Queryex text authored by admins, and `today()` is a natural thing to write in one
(`ValidUntil >= today()`, `PostingDate <= today()`). If the request decides what `today()` means,
a user shifts their own row set by up to a day by choosing a header. The same applies to the
engine's `TimeZone` slot, which `local()` conversions consume. The brain dump's alternative — the
user's time zone — has the same property. Spec 0008 already says `today()` is "the current date
in the tenant's zone"; the web layer must keep it there and bind the user's zone only to
*display* (message formatting, exports). This is a contradiction with the simplicity proposal and
is D10.

**"If you can read an entity you can read all the related entities" is a PII leak when applied
to full rows.** Every audited entity points at `User` through `CreatedById`/`ModifiedById`. A user
who can read `Center` would, under a full-row `related` dictionary, receive every referenced user's
`Email`, `ContactMobile`, `Subject`, notification settings, and invitation state — columns they
have no permission on, for rows their `User` row-level filter would hide. The same leak exists in
`query`: `select: "CreatedBy.Email"` traverses a navigation the caller may not read. The rule the
brain dump wants — no partially visible document — is satisfied by a **related projection** (id,
code, display names, active flag) that every entity declares once and that is what a navigation
exposes to a caller without `read` on the target. D5 and D12.

**Optimistic concurrency is under-specified on the wire.** "The save payload accepts an override
flag" says what happens when a conflict is detected; it does not say what happens when the client
sends *no* stamp. Import has no stamps; a details page always does. If a missing stamp means "no
check", every client bug that drops the stamp becomes a silent lost update. D5 makes the mode
explicit and makes a missing stamp under `Check` a validation error.

**"Rate limiting … number of entities … length of every free-text parameter" mixes three
mechanisms.** Rate is an in-process limiter; body size is Kestrel metadata enforced before the
body is read; cardinality and string length are validation. Only the first is "rate limiting".
The brain dump's constraint — purely in-memory, per instance — is right and is exactly what
`System.Threading.RateLimiting` is.

**The MCP section defers what the design goals make a day-one requirement, and skips the two
hard parts.** The hard parts are authorization against an identity server that today has neither
protected-resource metadata nor client-id metadata documents (both must be added), and write
safety for an agent that does not hold concurrency stamps and will set any `confirm` flag it is
offered. D16–D19 design both; the tools themselves are the easy part.

**Precision is never mentioned.** An accounting platform whose amounts are `decimal(19,4)` and
whose client is a JavaScript engine has a silent-corruption path: `JSON.parse` yields a double,
and a value with more than 15–17 significant digits comes back changed on the next save. The wire
must be lossless and the client contract must say how to parse it. D4.

**No operational surface.** Nothing says how the host reports liveness and readiness, how a
request that times out releases its database connection, how in-flight requests drain on a deploy,
or what a request logs. D13 and D15.

**Detail choices to change.** `{tenantId}` is `int`-constrained (a slug alias is additive later).
MCP tool names use underscores (OpenAI-compatible). Header names drop the `X-` prefix. The "13
endpoints" list gains `get-by-ids` (the brain dump's own "if the user requests 5 ids" question
has no endpoint to attach to). `DeleteByQuery` gains an `expectedCount` checked *inside the
delete transaction*, not in a preceding count query.

**Where the orchestrator's hints land.** Adopted: POST-everywhere on the web surface with REST kept
for `v1`; save as an array; one entity class with `[NotMapped]` children plus a declared editable
split (with the risks answered mechanically in D6); RFC 9457 with property paths; one MCP endpoint
per tenant; the startup audit over `EndpointDataSource`; `ModifiedAt` as the concurrency stamp
(D5 gives the wire rule and the guard TVP the pipeline needs). Refined: the connect-call collapse
is adopted but the failure modes are named as web-layer obligations (D8); "output caching" from
the architecture's principle is confined to anonymous GETs. Rejected: nothing outright.

---

## 2. Decisions

### D1 — Packaging: `Tellma.Core.AspNetCore`, `Tellma.Core.Mcp`, wire contracts in `Tellma.Core.Abstractions.Api`; module packages never reference ASP.NET Core

**Decision.** Two Core-layer projects:

| Project | References | Contents |
|---|---|---|
| `Tellma.Core.AspNetCore` (`src/core/Tellma.Core.AspNetCore/`) | `Tellma.Core`; `FrameworkReference Microsoft.AspNetCore.App` | `MapTellma()`, endpoint projection, request-context binding, CSRF filter, problem filter, JSON options, limits, compression, health endpoints, OpenAPI (Development), the `RequireSecurable`/`AllowMember` conventions. Also the home of the BFF endpoints and tenant middleware owned by the distribution-host theme. |
| `Tellma.Core.Mcp` (`src/core/Tellma.Core.Mcp/`) | `Tellma.Core.AspNetCore`; `ModelContextProtocol.AspNetCore` 2.2.0 | The Tellma Tenant MCP server: seven generic tools, PRM document, bearer/challenge wiring, result shaping and caps, the delete-confirmation token. |

Wire contracts — request and response records, `QueryRowSet`, `RelatedEntities`, the closed
exception set, the declaration attributes, header names, telemetry name constants — live in
`Tellma.Core.Abstractions` under namespace `Tellma.Core.Abstractions.Api`. They reference only the
BCL (`System.Text.Json` included). A module package (`Tellma.Module.Gl`) therefore can declare
`[ApiAction]` methods, throw platform exceptions, and return platform envelopes **without
referencing any web package**, which is the repo rule. A module that needs an endpoint the
projection cannot express ships a separate `Tellma.Module.<M>.AspNetCore` package; the module
itself never takes a framework reference.

Tests: `test/core/Tellma.Core.AspNetCore.Tests` (in-process `TestServer` over a fixture composition
using the data-access theme's LocalDB fixture entities; no `Category=Integration` trait — the host
is in-process and LocalDB is the repo's default test database) and `test/core/Tellma.Core.Mcp.Tests`
(the SDK's in-memory client over the same fixture host). D22 lists the mandatory test classes.

**Rationale.** `Tellma.Core` stays framework-free (the migrator and every test project reference it
without a web host). The wire contracts belong in Abstractions because three consumers (pipeline,
Excel, MCP) and every module produce or consume them. MCP is separate so an air-gapped distribution
can omit the SDK.

**Alternatives rejected.** One package for web and MCP (forces the SDK on every distribution).
Wire contracts in `Tellma.Core.AspNetCore` (modules could not return them).

**Confidence.** High. **Review flag.** None.

### D2 — Routes: tenant-first, `int`-constrained; reserved top-level segments; health endpoints outside the tenant prefix

**Decision.**

```
/{tenantId:int}/api/web/{resource}/{operation}                 the private SPA surface (built)
/{tenantId:int}/api/web/blobs/{id}                             the one GET on the web surface (blob theme)
/{tenantId:int}/api/v1/…                                       the versioned public surface (reserved seam)
/{tenantId:int}/mcp                                            the Tellma Tenant MCP server (built)
/.well-known/oauth-protected-resource/{tenantId:int}/mcp       the MCP protected-resource metadata (GET, anonymous)
/api/distribution-info                                         distribution contract surface (host theme)
/api/webhooks/{key}                                            spec 0007, unchanged
/healthz                                                       liveness: anonymous, no I/O, 200 once the composition gate passed
/readyz                                                        readiness: anonymous, checks the tenant registry source (catalog/config) and the data-protection key ring
/openapi/web.json                                              Development only
```

`{resource}` is the kebab-case plural derived from the EF table name (`gl.Centers` → `centers`,
`gl.InvoiceLines` → `invoice-lines`), overridable with `[ApiResource("…")]`; it is also the
securable resource id, the OpenAPI tag, and the default Excel sheet name. `{operation}` is one of
the standard segments in the table below or an `[ApiAction]` name. The SPA fallback excludes
`/api`, `/mcp`, `/.well-known`, `/openapi`, `/healthz`, `/readyz`. Standard operations exist only
when the service declares the capability (D7):

| Segment | Securable action | Body | Result |
|---|---|---|---|
| `query` | `read` | `QueryRequest` | `QueryResult` |
| `get` | `read` | `GetRequest` | `EntitiesResult<T>`; 404 when absent or invisible |
| `get-by-ids` | `read` | `IdsRequest` | `EntitiesResult<T>`; partial, never 404 |
| `get-by-parent-ids` (tree) | `read` | `ParentIdsRequest` | `QueryResult` |
| `save` | `save` | `SaveRequest<T>` | `EntitiesResult<T>` |
| `delete` | `delete` | `IdsRequest` | `AffectedResult` |
| `delete-by-query` | `delete` | `DeleteByQueryRequest` | `AffectedResult`; never on MCP |
| `delete-with-descendants` (tree) | `delete` | `IdsRequest` | `AffectedResult` |
| `activate` / `deactivate` | `activate` | `IdsRequest` | `EntitiesResult<T>` |
| `export` / `export-for-import` | `read` | `ExportRequest` | `.xlsx` stream or 202 `TaskAccepted` |
| `import` | `save` | `multipart/form-data` | `ImportResult` or 202 `TaskAccepted` |
| `{action}` | the attribute's action | the method's request type | the method's return type |

**Rationale.** Tenant-first lets one route group carry tenant resolution for every surface and
makes the RFC 9728 path-insertion rule produce a per-tenant metadata document. Liveness must not
touch a database (a database outage must not make the orchestrator kill healthy instances);
readiness must (a new instance must not receive traffic before it can resolve tenants).

**Alternatives rejected.** Slug in the URL (reserved-word and normalization problems; can be added
as an alias). Health endpoints under the tenant prefix (they are per instance, not per tenant).

**Confidence.** High. **Review flag.** `int` versus slug is the distribution-host theme's call.

### D3 — Verbs: POST-only web surface; writes are never auto-retried by the client; `v1` keeps REST

**Decision.** Every operation under `/{tenantId}/api/web` is `POST application/json`, except the
blob GET and the two `multipart/form-data` endpoints (`import`, blob `upload`). The public `v1`
surface projects REST verbs when built. MCP is one POST by protocol.

Client retry contract (written into the spec because a POST-only surface removes the verb-based
idempotency signal): the SPA and the MCP host retry **reads** (`query`, `get`, `get-by-ids`,
`get-by-parent-ids`, `export`) on network failure, 429 and 503 with `Retry-After`; they never
retry `save`, `import`, `delete*`, `activate`/`deactivate`, or actions — a lost response to a
create is surfaced to the user, who re-queries. The `Idempotency-Key` header is reserved for the
`v1` surface (where scripted callers do retry) and is not implemented now.

**Rationale.** Queryex text belongs in bodies; one verb gives one binding path and one CSRF story;
the SPA never uses HTTP caching of query results; output caching cannot serve authenticated
responses anyway. A retried create with app-assigned ids is a duplicate row, and no
client-generated key exists on this surface to dedupe it, so the rule must be "do not retry".

**Confidence.** High. Departure from the architecture document recorded in §7. **Review flag.**
Whether the web surface should accept a client-generated `clientKey` per new entity for safe
retry (the pipeline would need a per-tenant dedupe table; deferred to `v1`).

### D4 — Query wire: Queryex text in; a columnar `QueryRowSet` out; lossless numbers; bounded paging window; count capped inside SQL

**Decision.** Request (camelCase JSON):

```json
{ "select": "Id,Code,Name,Parent.Name,IsActive", "filter": "IsActive and Name contains @q",
  "orderBy": "Code", "skip": 0, "take": 50, "search": "east", "arguments": { "q": "east" },
  "includeCount": true, "includeAncestors": false, "aggregate": false, "having": null }
```

Response:

```json
{ "columns": [ { "name": "Id", "type": "Numeric", "storeType": "int", "nullable": false, "path": ["Id"], "groupingKey": false }, … ],
  "rows": [ [5, "E-01", "East region", "Regions", true] ],
  "count": 10000, "countCapped": true,
  "ancestors": [ [1, "R", "Regions", null, true] ] }
```

Rules:

1. **Server-side representation is columnar.** `QueryResult.Rows` is a `QueryRowSet`: one typed
   buffer per column (`int[]`, `long[]`, `decimal[]`, `bool[]`, `string?[]`, `DateOnly[]`,
   `DateTime[]`, `DateTimeOffset[]`, `byte[][]`, `Guid[]`, `SqlHierarchyId`-as-string `string?[]`)
   plus one null bitmap per nullable column, filled by the data-access theme's reader through
   typed `SqlDataReader` getters (`GetInt32`, `GetDecimal`, …) — no `GetValue`, no boxing, one
   allocation per column per page. A `JsonConverter<QueryRowSet>` in Abstractions writes the
   row-major JSON with `Utf8JsonWriter`, switching on the column kind once per column, not per
   cell. Contradiction: the simplicity proposal's `IReadOnlyList<IReadOnlyList<object?>>` boxes
   every numeric cell (a 50×30 page is 1,500 allocations; a 10,000-row export page is 300,000),
   and forces every scalar runtime type into the source-generated context.
2. **Encoding by column kind.** `int`/`long`/`short`/`byte` → JSON number; `decimal` → JSON
   number written by STJ with its full scale (`1234.5000`), never exponent notation; `bool` →
   `true`/`false`; `string` → string; `DateOnly` → `"yyyy-MM-dd"`; `DateTime` (stored
   `datetime2`) → `"yyyy-MM-ddTHH:mm:ss.fffffff"` with trailing zeros trimmed and no offset;
   `DateTimeOffset` → RFC 3339 with offset; `hierarchyid` → its string path; `varbinary` → base64;
   `Guid` → canonical string; SQL NULL → `null`. `columns[].storeType` carries the structured
   store type name (`decimal(19,4)`, `nvarchar(255)`) from the Queryex schema so a generic client
   can pick a parser per column.
3. **Lossless numbers are a client obligation.** The wire is lossless: STJ writes `decimal` and
   `long` exactly. A JavaScript `JSON.parse` is not: it returns a double, so a `decimal(19,4)`
   value above roughly 10¹¹ or a `long` above 2⁵³ changes on the way in and comes back changed on
   the next save. The spec records the client contract: the SPA parses `/api/web` bodies with a
   lossless parser (numbers with more than 15 significant digits are materialized as decimal
   strings, then as the client's decimal type) and the MCP layer never re-parses numbers. The
   server rejects an inbound number whose textual form exceeds the property's precision or scale
   with a 422 `precision` error at the property path — it never rounds silently.
4. **Paging window.** `take` defaults to 50 and is clamped to `MaxTake` (10,000); `skip + take`
   above `MaxSkipWindow` (100,000) is a 400 `bad-request` (`OFFSET` paging beyond that scans; the
   search page never gets there and export uses no paging). The engine must emit `skip`/`take` as
   parameter slots so every page shares one plan (a data-access ask; §6 seam 1).
5. **Count is capped inside SQL, in the same round trip.** `includeCount` adds a second statement
   to the same batch: `SELECT COUNT(*) FROM (SELECT TOP (@cap + 1) 1 AS x FROM … WHERE <filter and
   RLS>) AS c` and the response reports `count = min(n, cap)`, `countCapped = n > cap`. Never a
   second round trip, never an uncapped `COUNT(*)`.
6. **Ancestors ride the same batch** for tree entities: a third statement selecting the ancestor
   rows of the page's matches by `Node.IsDescendantOf` that are not themselves in the page. They
   are returned in a separate array so the UI never confuses them with matches.
7. **Arguments** are JSON scalars; the server infers each declared parameter's type with
   `DiscoverQuery` and converts before `CompileQuery`. The engine's text-keyed caches (§16 of the
   Queryex spec) make the second bind a cache hit; the host emits
   `tellma.api.query.discover.duration` so the cost is visible.
8. Queryex diagnostics are a 400 `query-invalid` problem whose `errors` keys are the clause names
   and whose `diagnostics[]` carries `code`, `location`, `start`, `length`, `arguments` verbatim.

**Rationale.** Arrays of arrays are 3–5× smaller than objects and map onto `CompiledQuery.Columns`;
columnar buffers are the only representation that keeps a 10,000-row export page under a few
megabytes of managed memory and off the large-object-heap churn path. Capping the count inside SQL
is what makes "count stops at 9,999 on millions of rows" true instead of aspirational.

**Alternatives rejected.** Row objects (bigger, slower). Decimals as JSON strings (lossless with
`JSON.parse`, but inconsistent with ints, breaks numeric `outputSchema`s for agents, and pushes
string parsing into every consumer; the lossless-parser obligation is smaller). A wire `FilterTree`
(client complexity for nothing; the tree is composed server-side).

**Confidence.** High on the columnar buffer and count cap; medium on numbers-as-JSON-numbers.
**Review flag.** Decimals as JSON numbers with a lossless client parser (this proposal) versus
decimals as strings on the wire; the client-library choice (`lossless-json` or equivalent) belongs
to the SPA and must be settled before the first grid ships.

### D5 — Entity envelopes: one `EntitiesResult<T>`; `related` is a display projection in typed sets; explicit concurrency mode with an opaque stamp; delete-by-query verified inside the transaction

**Decision.** `get`, `get-by-ids`, `save`, `activate`, `deactivate`, and id-shaped actions return:

```json
{ "ids": [5],
  "entities": [ { "id": 5, "code": "E-01", "name": "East region", "parentId": 1, "centerType": "Operation",
                  "isActive": true, "createdAt": "2026-08-01T06:12:44.1234567", "createdById": 1,
                  "modifiedAt": "2026-08-30T12:02:10.0000000", "modifiedById": 3,
                  "node": "/1/2/", "subtreeCount": 4, "activeSubtreeCount": 3 } ],
  "related": { "Center": [ { "id": 1, "code": "R", "name": "Regions", "isActive": true } ],
               "User":   [ { "id": 1, "name": "Admin", "isActive": true }, { "id": 3, "name": "Sara", "isActive": true } ] },
  "extras": { "history": [ … ] },
  "rows": [ [5, "E-01", "East region", "Regions"] ] }
```

1. **`entities`** are whole entities in wire shape (D6): every property, child collections
   nested, foreign keys as ids, enums as strings, `name2`/`name3` omitted when the tenant does not
   configure that language.
2. **`related` is a projection, not a row.** Contradiction with the simplicity proposal, which
   returned related entities "in the same wire shape without their own children". Each entity
   declares once, with `[RelatedSelect("Id,Code,Name,Name2,Name3,IsActive")]` (default when
   absent: the key, the natural key, the multilingual display names, `IsActive` if present), the
   columns it exposes when reached through a navigation. `related` holds only those columns. A
   caller who can read the main entity therefore learns of a referenced user what the UI must
   show — a name — and nothing the `User` resource's own permissions protect. The same projection
   is what D12 allows a `query` to traverse into without `read` on the target. `related` is keyed
   by entity name and holds an **array** (not an id-keyed object): the client indexes by `id`
   itself, the server writes one typed list per entity type through one `JsonTypeInfo` lookup per
   type per response (`RelatedEntities` in §3), never one per row, and the shape is identical for
   `int` and `long` keys.
3. **`extras`** is a per-service bag selected by `include`; unknown names are a 400. `rows` is
   the search-row echo, present when `select` was given, one row per entity in `entities` order,
   using the `QueryRowSet` of D4.
4. **`ids`** is always present: input order for save, requested order for `get-by-ids` omitting
   not-found ids.

Save:

```json
{ "entities": [ { "id": 0, "code": "E-02", "name": "East 2", "parentId": 5, "centerType": "Operation" } ],
  "returnEntities": true, "select": "Id,Code,Name,Parent.Name", "include": [],
  "concurrency": "check" }
```

5. **Concurrency mode is explicit.** `concurrency` ∈ `check` (default) | `override`. Under
   `check`, every entity with `id > 0` must carry `modifiedAt` equal to the stored stamp; a
   missing or null stamp on an update is a 422 `concurrency-stamp-required` at
   `entities[i].modifiedAt`; a mismatch is a 409 `concurrency-conflict` listing every conflicting
   id with the stored stamp and modifier. Under `override`, no stamp is checked (import's update
   and merge modes use `override` explicitly because a sheet carries no stamps). Contradiction
   with the boolean `overrideConcurrency` whose absent-stamp behaviour was unspecified.
6. **The stamp is opaque.** The client never parses `modifiedAt`; it echoes the string it
   received. The server compares the parsed `datetime2(7)` value for equality inside the persist
   batch through the `(Id, ExpectedStamp)` guard TVP the pipeline theme defines; the endpoint
   layer's only job is to carry the string losslessly (the `DateTime` encoding of D4 preserves all
   seven fractional digits). `modifiedAt` is `[ServerOwned]` for the emitter (D6) *and* the
   stamp source for the guard — the two roles do not conflict because the guard reads the
   inbound value and the emitter never writes it.
7. **Ids.** `id` absent or `0` creates; a negative id is a 400; a duplicate id within the payload
   is a 422 `duplicate-id`; a child whose `id` does not belong to the enclosing parent is a 422
   `child-not-owned` — the pipeline must verify ownership, and the emitter's synchronize
   statements must key on `(ParentId, Id)`, never on `Id` alone, or a crafted payload moves or
   overwrites another parent's child (§6 seam 2).
8. **Limits.** `MaxEntitiesPerSave` 1,000 (413); `MaxIdsPerRequest` 10,000 (413).

`DeleteByQueryRequest` is `{ "filter": "…", "arguments": { }, "expectedCount": 1204 }`. The count
is not checked in a preceding query; it is checked inside the delete transaction so there is no
window between the count the user saw and the rows deleted:

```sql
SET XACT_ABORT ON;
BEGIN TRAN;
DELETE c FROM [gl].[Centers] AS c WHERE <compiled filter AND RLS>;
IF @@ROWCOUNT <> @expected
BEGIN
    ROLLBACK;
    SELECT CAST(0 AS bit) AS Ok, @actualCount AS Actual;   -- the batch reads this and throws CountMismatchException (409)
    RETURN;
END
COMMIT;
SELECT CAST(1 AS bit) AS Ok, @expected AS Actual;
```

(`@actualCount` is the `@@ROWCOUNT` captured into a variable immediately after the `DELETE`.)
The FK-restrict errors (547) that a delete raises map to a 422 `in-use` at `ids[i]` with the
referencing entity name, never to a 500.

**Rationale.** The projection closes the PII leak and shrinks payloads (a `User` row is ~1 KB; its
projection is ~60 bytes). Typed sets keep serialization on the metadata-mode path. An explicit
mode makes the dangerous default (no stamp) impossible to reach by accident. Verifying the count
inside the transaction is the only version of "never delete more than the user was shown" that is
true under concurrency.

**Alternatives rejected.** Full related rows (leak; size). Id-keyed `related` objects (string keys,
per-row type lookups). A boolean override (silent when the stamp is missing). A preview-then-token
delete flow (server state or a signed token for a case `expectedCount` already covers on the web
surface; MCP does use a token, D16).

**Confidence.** High. **Review flag.** `[RelatedSelect]` defaulting to display columns (this
proposal) versus full rows filtered by the target's row-level security (a second permission
evaluation per related type per request, and still leaks columns).

### D6 — The entity class is the wire shape; server-owned columns are excluded by the emitter; strict JSON; the N−1 rules

**Decision.** No DTO layer. The entity class serializes with System.Text.Json under one
`JsonSerializerOptions` registered by `AddTellma`:

- `PropertyNamingPolicy = CamelCase`; `PropertyNameCaseInsensitive = true`;
  `DefaultIgnoreCondition = WhenWritingNull`; `NumberHandling = Strict`;
  `AllowDuplicateProperties = false`; `UnmappedMemberHandling = Skip` (an N−1 client keeps
  working through a deploy — see rule 5); `MaxDepth = 16` (parent → child → grandchild is depth
  6; the default 64 is a stack-depth DoS margin nobody needs); enums as strings through
  `JsonStringEnumConverter<TEnum>` (AOT-safe); `DateOnly`, `DateTime`, `DateTimeOffset`, `decimal`,
  `byte[]`, `Guid`, `hierarchyid` as in D4.
- Child collections are `[NotMapped] List<TChild>` properties on the parent (no parent→child EF
  navigation; no Queryex collections; the wire has both).
- **`[ServerOwned]` is enforced by the SQL emitter, not by a pipeline convention.** Contradiction
  with "the pipeline overwrites server-owned values". The data-access theme's emitter derives the
  `UPDATE … SET` list from the UDTT row image **minus** `[ServerOwned]` columns and the
  `INSERT` column list minus `[ServerOwned]` columns that are not `AfterCreate`; audit, tree,
  activation, and invitation-state columns are written only by the statements that own them
  (audit stamping, the tree recompute, `activate`). A client-sent server-owned value therefore
  cannot reach a table even if every pipeline step forgets to overwrite it, and the analyzer the
  table-types spec deferred (no hard-coded ordinals) has a sibling rule: an `UPDATE` emitted for
  a `[TableType]` entity that names a `[ServerOwned]` column is a build error in the emitter's
  own tests. Platform bases mark `CreatedAt`, `CreatedById`, `ModifiedAt`, `ModifiedById`, `Node`,
  `Level`, `SubtreeCount`, `ActiveSubtreeCount`, `IsActive`, and `User`'s invitation state;
  `[ServerOwned(AfterCreate = true)]` marks write-once columns (`Subject`, `Email`).
- `[JsonIgnore]` is the "never on the wire" marker; a `[JsonIgnore]` property that is not
  `[ServerOwned]` fails the startup gate (a hidden-but-writable column is a contradiction).
- **Source generation.** `TellmaApiJsonContext` (metadata mode) covers every envelope, request,
  problem, `QueryRowSet`, `RelatedEntities`, and scalar; entity types resolve through a
  `DefaultJsonTypeInfoResolver` appended to `TypeInfoResolverChain`. The startup gate resolves the
  `JsonTypeInfo` of every registered entity, child, and request type once, so an unserializable
  member fails startup, and `JsonSerializerIsReflectionEnabledByDefault` stays true (a
  distribution is not trimmed). A scaffolded per-distribution context is a later optimization
  with no wire change.
- **N−1 rules** (the guiding principle's zero-downtime deploy): within a platform minor, wire
  properties are added, never removed or renamed; a removal is a two-release expand/contract
  (mark obsolete, ship, remove); new required request members ship with defaults; enum values
  are added, never removed; unknown inbound members are skipped and counted
  (`tellma.api.unknown_members`, tag `type`), so an old client's extra field is visible in
  telemetry rather than silently dropped forever. Every `/api/web` response carries
  `Tellma-Build: <DeploymentIdentity version>`; the SPA compares it with its own and prompts a
  reload when the major/minor differ.

**Rationale.** A DTO hierarchy triples the mechanical code and drifts; the entity already carries
the column metadata. The risks of one class on the wire are real and are closed where they cannot
be forgotten: in the emitted SQL, in the startup gate, and in the parser options.

**Alternatives rejected.** A `ForSave` hierarchy (rejected by the architecture). `[Editable]`
opt-in (inverts the common case). Pipeline-level overwrite alone (a convention, not a guarantee).

**Confidence.** High. **Review flag.** `MaxDepth = 16` (raise if a distribution nests deeper).

### D7 — Projection: `MapTellma()` maps every endpoint from the stack catalog through closed generic handlers; `[ApiAction]` is the only custom-endpoint mechanism for modules; the tenant group is the escape hatch for the distribution

**Decision.** The distribution's web layer is `builder.AddTellma<TDbContext>(t => { t.AddCore();
t.AddGl(); t.AddMcp(); })` and `app.MapTellma()`. `MapTellma()` reads `IEntityStackCatalog`
(built by the service-pipeline theme in the realize phase) and, for each stack, maps the standard
operations its service capabilities imply plus its `[ApiAction]` methods onto the tenant group.
Binding: a static generic class `StandardEndpoints<TEntity>` exposes one static method per
operation (`Task<Ok<QueryResult>> QueryAsync([FromBody] QueryRequest request,
IEntityService<TEntity> service, CancellationToken cancellationToken)`); the projector closes it
over each entity type and passes the `MethodInfo`-backed delegate to `MapPost`, so
`RequestDelegateFactory` sees real parameter types and attributes, OpenAPI sees real schemas,
and no reflection runs per request. `[ApiAction]` methods are mapped through a per-action
generated delegate that resolves the service from the request scope, reads the single body
parameter with the platform options, and awaits the method. The projection adds, per endpoint:
`SecurableEndpointMetadata` or `MemberOnlyEndpointMetadata` (D8), `IApiEndpointMetadata`,
`ProducesProblem` metadata for 400/401/403/404/409/413/422, the request-size metadata, and the
tags `tellma.resource`/`tellma.operation` on the request `Activity` through an endpoint filter.

`[ApiAction]` gains `Destructive` (MCP `destructiveHint`; when true the MCP action tool requires
confirmation, D16) and keeps `Action`, `MemberOnly`, `Idempotent`, `Mcp`, `Description`. Rules:
public instance method on the service, `Task<T>`/`Task`, at most one body parameter plus an
optional `CancellationToken`; the route is `POST /{tenantId}/api/web/{resource}/{name}`; the
securable is `(resource, Action ?? name)` unless `MemberOnly`. A non-entity service carries
`[ApiRoute("me")]` and projects only its actions.

The escape hatch is the tenant group itself, for the distribution's Web project only:

```csharp
RouteGroupBuilder tenant = app.MapTellma().TenantGroup;
tenant.MapPost("reports/aging/stream", AgingReport.StreamAsync).RequireSecurable("aging-report", "read");
```

A module package has no escape hatch beyond `[ApiAction]` (D1): it cannot reference the group type.

**Rationale.** Zero web lines per entity; every custom action is one attribute that feeds HTTP,
OpenAPI, the securables registry, and MCP; the attribute is the security boundary (no
convention-based discovery of public methods). Closed generic static methods keep binding on the
framework's compiled path.

**Alternatives rejected.** Controllers per entity; endpoint classes per stack; attribute-free
discovery.

**Confidence.** High on shape; medium on the `Delegate.CreateDelegate` mechanics (§8). **Review
flag.** None.

### D8 — Authorization: the service is authoritative; the endpoint filter is an early deny; the startup audit verifies metadata against the securables registry

**Decision.** Contradiction with "the securable filter evaluates the permission and attaches the
row-level filter to the request context for the service". The layering is:

1. **`SecurableEndpointFilter`** (tenant group) reads `SecurableEndpointMetadata(resource, action)`
   and calls `IPermissionEvaluator.TryDenyFastAsync(resource, action)` — a call answered from the
   permissions cache with **no database round trip**. A definite "no permission on this resource
   and action at all" ends the request with 403 before the body is deserialized. Any other
   answer (allowed, or cache cold) falls through. The filter never passes a decision to the
   service.
2. **The service pipeline** evaluates the same `(resource, action)` itself, inside its first
   batch, where the connect step validates the permissions tag (the collapse the users and
   pipeline themes own). It composes the row-level `FilterTree`. This is the authoritative check
   and the one MCP tools and background jobs also go through, because they call the service.
3. **`MemberOnlyEndpointMetadata`** endpoints skip step 1; the service's connect step still
   requires an active member.
4. The host sets `FallbackPolicy = RequireAuthenticatedUser()`; every projected endpoint also
   carries `RequireAuthorization()` explicitly so the audit does not depend on the fallback.
5. **Startup audit** (part of the composition's aggregated validation, run host-free in tests):
   every endpoint whose route template begins with `/{tenantId:int}` must carry exactly one of
   the two metadata types, must carry `IApiEndpointMetadata`, must not carry `AllowAnonymous`,
   and its `(resource, action)` must exist in the securables registry (a typo in
   `[ApiAction(Action = "aprove")]` is a startup failure, not a silently unreachable action).
   Every endpoint outside the prefix must carry `AllowAnonymous` explicitly or be one of the
   BFF endpoints (so an accidentally tenant-less business endpoint fails too).

Failure modes of the collapse, stated as web-layer obligations: a deactivated user whose
permissions cache is warm passes step 1 and is denied by the connect step in step 2 (403
`forbidden`, the cache entry evicted); a stale permissions tag makes the pipeline recompute and
re-run the batch once (the second run is the one that counts; `tellma.api.permissions.stale`
counts it); the row-level pre-check on update runs in the same batch as validation context, so
the ordering "pre-check before validation" costs no round trip.

**Rationale.** A check that lives only in an endpoint filter is bypassed by every non-HTTP
caller; a filter that passes its verdict through a scoped holder creates a trust edge the service
cannot verify. The early deny keeps the common denial cheap (no body parse, no batch), and the
audit makes forgetting impossible.

**Alternatives rejected.** Policy-based `IAuthorizationRequirementData` attributes (duplicates the
evaluator before the connect step). Filter-only enforcement.

**Confidence.** High. **Review flag.** None.

### D9 — CSRF on the cookie surface: required client header, content-type allow-list, origin agreement; no antiforgery token; bearer never accepted

**Decision.** `CsrfEndpointFilter` runs first on the tenant group, before any body read, and
rejects with 403 `csrf` unless all hold: `Tellma-Client` is present and its name is in the closed
set (`web`, `cli`); `Content-Type` is `application/json` (or `multipart/form-data` only on
endpoints carrying `AcceptsMultipartMetadata`: `import`, blob `upload`); when `Origin` is present
it equals the configured public origin (`Tellma:PublicOrigin`, D17 — never the `Host` header); when
`Sec-Fetch-Site` is present it is `same-origin` or `none`. The session cookie stays
`SameSite=Lax`, `HttpOnly`, `Secure`. No antiforgery token, no `AddAntiforgery` on this surface.
Bearer tokens are refused on `/api/web` (a request with `Authorization` gets 401 `unsupported-credential`)
so a single credential type keeps the reasoning valid. Cookie challenges return 401/403, never
redirects (`IApiEndpointMetadata` on every endpoint). The filter increments
`tellma.api.requests.rejected{reason=csrf}`.

**Rationale.** BCP 212's custom-header control plus `SameSite=Lax` plus the content-type
allow-list closes the form-post and cross-site-fetch vectors without a token round trip;
comparing `Origin` against a configured value rather than the request's own host closes
host-header confusion. Checking before the body read makes a forged request cost one header
parse.

**Alternatives rejected.** Antiforgery token in a readable cookie (a bootstrap call and a second
cookie for no additional protection when no CORS policy exists). `SameSite=Strict` (breaks the
OIDC return trip). Accepting bearer on the web surface (`v1` exists for scripts).

**Confidence.** High. **Review flag.** Whether a POST with neither `Origin` nor `Sec-Fetch-Site`
should be rejected outright (every current browser sends `Origin` on POST; non-browser callers
have no business on the cookie surface) — this proposal allows it, on the custom-header control
alone, until the claim is verified against the browser matrix (§8).

### D10 — Headers and zones: `Accept-Language`, `Tellma-Time-Zone`, `Tellma-Calendar`, `Tellma-Client`; the engine's `today()` and `TimeZone` slots bind to the **tenant** zone; the header zone is display-only

**Decision.** Request headers:

| Header | Value | Effect | Precedence when absent |
|---|---|---|---|
| `Accept-Language` | standard | message language and formatting culture, negotiated against the distribution's shipped catalogue (`-u-` extensions stripped first) | user preference → tenant primary language → `en` |
| `Tellma-Time-Zone` | IANA id | `RequestContext.DisplayTimeZone`: formats instants in messages and exports | user preference → tenant zone |
| `Tellma-Calendar` | catalogue code (`gc`, `uq`, `et`) | date formatting in messages and Excel | user preference → tenant primary calendar |
| `Tellma-Client` | `<name>/<version>` | CSRF control (D9); `tellma.client` tag (name only) | required on the cookie surface; set by the MCP host |

Response headers on every `/api/web` response: `Content-Language`, `Tellma-Build` (D6),
`Tellma-Cache-Tags: settings=…;permissions=…;user-settings=…;securables=…` (the tags the connect
step read on this request, set by the operation filter after the handler returns and before the
result executes), and `Retry-After` on 429/503.

`RequestContext` carries **two zones**. `TenantTimeZone` (from tenant settings) binds the Queryex
`Today` and `TimeZone` parameter slots for every compiled query, stored filter, and row-level
criterion; `DisplayTimeZone` (header → user → tenant) is used only to format. Contradiction with
"`today()` binds to the request's effective zone with the tenant as fallback": a user-selectable
header must not shift a security predicate, and a stored report definition must mean the same
thing for every reader. Spec 0008's wording ("the current date in the tenant's zone") stands
unchanged. A client that wants "my today" passes a date argument (`@d`) and the SPA computes it in
its own zone. There is no today header.

**Rationale.** No standard header carries a zone or calendar; `Tellma-*` names follow RFC 6648.
Binding the engine to the tenant zone is the only choice under which row-level security is
deterministic per tenant and a report is reproducible.

**Alternatives rejected.** `X-Today` (client clock, two sources of truth). Request-zone `today()`
(security predicate under user control). Cookies for preferences.

**Confidence.** High. **Review flag.** None; the users and settings themes must expose the tenant
zone on the request context at tenant-resolution time, before any query compiles.

### D11 — Exceptions: the closed set in Abstractions; mapped by an endpoint filter on the tenant group; RFC 9457 with codes and localized messages; 500s carry only a trace id

**Decision.** The service pipeline throws only the closed set of §3.3. `TellmaProblemEndpointFilter`
(outermost on the tenant group, inside the CSRF filter) catches `TellmaProblemException`, records
`tellma.api.problems{status,code}`, and returns `TypedResults.Problem` / `ValidationProblem` with
`application/problem+json`. Contradiction with mapping through `IExceptionHandler`: the exception
middleware clears the response, re-dispatches, and loses the endpoint's own `Activity` scope; a
filter returns a result like any other. `AddProblemDetails()` with `CustomizeProblemDetails`
(adding `traceId` and `code`) remains for framework-generated problems (malformed JSON, 415, 429,
404 route miss) and for the unhandled 500 backstop, which writes `code: "internal"`, the `traceId`,
and nothing else outside Development.

| Exception | Status | `code` | Extra members |
|---|---|---|---|
| `BadRequestException` | 400 | `bad-request` | — |
| `QueryInvalidException` | 400 | `query-invalid` | `errors` by clause; `diagnostics[]` |
| `ValidationFailedException` | 422 | `validation` | `errors` (path → localized messages); `errorDetails[]` (path, code, arguments) |
| `StepUpRequiredException` | 401 | `step-up-required` | `WWW-Authenticate: Bearer error="insufficient_user_authentication", acr_values="…", max_age=…` |
| `HumanRequiredException` | 403 | `human-required` | a service account attempted an operation that requires step-up (no `auth_time` exists to step up) |
| `ForbiddenException` | 403 | `forbidden` | `resource`, `action` |
| `TenantSuspendedException` | 403 | `tenant-suspended` / `tenant-read-only` | — |
| `NotFoundException` | 404 | `not-found` | `entity`, `id` — identical for a missing row and a row hidden by row-level security |
| `ConcurrencyConflictException` | 409 | `concurrency-conflict` | `conflicts[]` (id, modifiedAt as the opaque string, modifiedById, modifiedByName) |
| `CountMismatchException` | 409 | `count-mismatch` | `expected`, `actual` |
| `PayloadTooLargeException` | 413 | `payload-too-large` | `limit`, `actual`, `maximum` |
| `DependencyUnavailableException` | 503 | `dependency-unavailable` | `dependency` (`database`, `identity`, `blobs`); `Retry-After` |

Validation error paths use camelCase JSON names with index grammar
(`entities[3].roleMemberships[1].roleId`); the pipeline emits them in wire casing; the built-in
`AddValidation()` is not enabled on tenant endpoints. Unique-index violations (2601/2627) are 422
at the property the index covers; FK restrict (547) on delete is 422 `in-use`; deadlock (1205) and
transient errors after the executor's retries are 503 `database`; a SQL timeout is 503 with
`Retry-After: 5`. Problem `type` is `https://tellma.com/problems/<code>`.

**Rationale.** A closed set makes the SPA's handling total and keeps distributions from inventing
statuses; the filter keeps mapping on the request path where telemetry already is.

**Alternatives rejected.** `IHasStatusCode` on arbitrary exceptions (open set). Codes only
(localization is the server's job). Exception middleware as the primary mapper (re-dispatch).

**Confidence.** High. **Review flag.** Resolvable `https://tellma.com/problems/<code>` versus
`urn:tellma:problem:<code>`.

### D12 — Navigation traversal in queries: reachable columns without `read` on the target are the target's related projection

**Decision.** After `DiscoverQuery` and before `CompileQuery`, the query pipeline walks every path
in `select`, `filter`, `orderBy`, and `having`. For each path that crosses a navigation into an
entity `E` different from the root: if the caller holds `read` on `E`'s resource (any filter), the
path is allowed; otherwise the terminal property must be in `E`'s `[RelatedSelect]` set (D5), or
the request fails with 403 `forbidden` naming `E`'s resource. Paths through weak entities resolve
to their owning top-level entity. The rule is enforced in the service (so MCP inherits it) and
documented in `tellma_describe` per navigation ("reachable: Id, Code, Name, IsActive"). The joined
rows are **not** filtered by `E`'s row-level security in either case — a documented limitation,
recorded so nobody assumes otherwise.

**Rationale.** Without this rule `select: "CreatedBy.Email"` leaks any user's email to anyone who
can read anything audited, and multi-hop paths reach every table in the model. The projection set
is exactly what the UI must show for a foreign key, so the common query loses nothing.

**Alternatives rejected.** Per-caller schema variants (multiplies schema caches keyed on identity).
Row-level filters on joins (the engine has no per-join predicates; a later amendment).

**Confidence.** High on the rule; medium on placement (service pipeline). **Review flag.** Whether
`read` on `E` should also be required for *filtering* on non-projected columns (this proposal:
yes — a filter on `CreatedBy.Email contains 'x'` is an oracle).

### D13 — Limits: one options object; a per-tenant concurrency limiter; timeouts that cancel the SQL command

**Decision.** `TellmaApiOptions` (`Tellma:Api`), every value a default:

| Limit | Default | Mechanism |
|---|---|---|
| JSON body | 8 MB | `RequestSizeLimitAttribute` metadata on the tenant group |
| Multipart body | 100 MB | per-endpoint metadata (`import`, `upload`); `FormOptions.MultipartBodyLengthLimit` aligned |
| Entities per save / ids per request | 1,000 / 10,000 | pipeline → 413 |
| `take` / count cap / skip window | 10,000 / 10,000 / 100,000 | clamp / SQL cap / 400 |
| Queryex ceilings | `QueryexLimits.Default` | engine → 400 |
| Requests per minute per `{tenantId}:{sub}` | 600, sliding window, 6 segments, queue 0 | `AddRateLimiter` policy `tellma-user`, partition key from the authenticated principal (`UseRateLimiter` after `UseAuthentication`); 429 + `Retry-After` |
| Concurrent requests per tenant per instance | 64, queue 0 | policy `tellma-tenant` (a burst on one tenant must not exhaust the instance for others; the SQL pool is per connection string, so this also bounds pool waits) |
| Anonymous per IP | 60 / min fixed | policy `tellma-anonymous` (PRM, distribution-info, health, OpenAPI) |
| Concurrent exports / imports per user | 2 / 1 | concurrency limiters |
| MCP tool calls per minute per user | 120 | policy `tellma-mcp` |
| Request timeout | 30 s web; 300 s export/import; 60 s MCP | `AddRequestTimeouts` policies |

The batch executor receives `HttpContext.RequestAborted` on every command; on timeout SqlClient
sends an attention and the connection returns to the pool with its transaction rolled back, so a
timed-out request never pins a connection. All limiters are in-process partitions; idle
partitions are reclaimed by the limiter's own timer. Every rejection increments
`tellma.api.requests.rejected{reason}` with `reason` ∈ `csrf`, `rate_limit`, `tenant_concurrency`,
`payload_too_large`, `unsupported_media`, `unknown_client`, `unsupported_credential`.

**Confidence.** High on mechanism; the numbers are tuning defaults. **Review flag.** The
per-tenant concurrency value.

### D14 — Compression on for JSON over HTTPS; output caching only for anonymous tenant-independent GETs

**Decision.** `AddResponseCompression` (Brotli, then Gzip, `Fastest`, `EnableForHttps = true`,
MIME types `application/json` and `application/problem+json`; `text/event-stream` and `.xlsx`
excluded). The recorded justification: no secret is reflected into any compressed body — the
session cookie is `HttpOnly` and never echoed, no CSRF token exists, `Tellma-Client` is constant,
and validation messages echo user input next to nothing secret. `CacheOutput` 5 minutes on
`/api/distribution-info`, the PRM documents, and `/openapi/web.json`; never under `/{tenantId}`.

**Confidence.** High.

### D15 — Observability: instruments, one log scope per request, the DB-call budget per operation, and the drain contract

**Decision.**

- Meter `Tellma.Core.AspNetCore`: `tellma.api.requests.rejected` (counter, `reason`);
  `tellma.api.problems` (counter, `status`, `code`); `tellma.api.operation.duration` (histogram,
  s; `tellma.resource`, `tellma.operation`, `tellma.client`); `tellma.api.operation.db_calls`
  (histogram, `{call}`; same tags) — read from the data-access theme's scoped `IDbCallCounter` when
  the operation filter completes, which is what finds an N+1 per operation on day one;
  `tellma.api.permissions.stale` (counter); `tellma.api.unknown_members` (counter, `type`);
  `tellma.api.query.discover.duration` (histogram). Meter `Tellma.Core.Mcp`:
  `tellma.mcp.tool.calls` (`tool`, `outcome` ∈ `ok`, `error`, `denied`, `truncated`, `confirm`);
  `tellma.mcp.tool.duration`; `tellma.mcp.result.chars`. No tenant or user tags on any instrument.
- The operation filter opens one logging scope `{TenantId, UserId, Client, Resource, Operation}`
  (logs may carry tenant ids; metrics may not) and tags the request `Activity` with the three
  closed-set tags. `Server-Timing: db;dur=<ms>;desc="<n> calls"` is emitted when
  `Tellma:Api:ServerTiming` is true (default: Development only).
- Drain: `HostOptions.ShutdownTimeout` 25 s; the host stops accepting, in-flight requests finish,
  the MCP subscription streams close. The infrastructure (host theme) must raise App Service's
  container stop limit above the default 5 s, or every deploy aborts saves mid-flight.

**Confidence.** High.

### D16 — MCP tools ship in 0015: seven generic tools; no concurrency override; deletes confirmed by elicitation or a signed preview token; result caps

**Decision.** `tellma.AddMcp()` registers `ModelContextProtocol.AspNetCore` 2.2.0 in
`HttpServerSessionMode.Stateless`; `MapTellma()` maps `/{tenantId:int}/mcp` (POST; GET/DELETE
405) with `RequireAuthorization("TellmaMcp")`. The tool list is static, deterministic, identical
for every caller (`ttlMs` 300,000, `cacheScope` `public`):

| Tool | Arguments | Annotations | Notes |
|---|---|---|---|
| `tellma_whoami` | — | readOnly, idempotent | user, tenant (id, name, `kind`, languages, calendars, zone), permission matrix |
| `tellma_describe` | `entity?`, `detail` | readOnly, idempotent | catalogue, or one entity's properties (editable, type, maxLength, enum values, navigation target and its reachable columns), children, searchable columns, natural key, default select, example filters; per tenant (Name2/Name3 gating), cached by the settings tag |
| `tellma_query` | `entity`, `select?`, `filter?`, `orderBy?`, `skip?`, `top` (50, max 500), `arguments?`, `includeCount?`, `format` (`table` default \| `objects`) | readOnly | one text block; `truncated: true` plus guidance at the cap |
| `tellma_get` | `entity`, `ids` (1..100), `include?` | readOnly | `EntitiesResult` with `related` projections; entities carry `modifiedAt` |
| `tellma_save` | `entity`, `entities` (1..100) | not destructive, not idempotent | **no override argument**; `concurrency` is always `check`; a conflict returns `isError` with the stored values and the instruction to re-get and re-apply |
| `tellma_delete` | `entity`, `ids`, `withDescendants?`, `confirmation?` | destructive | first call returns a preview (count, display names) and a `confirmation` token; second call with the token deletes. When the client declares `elicitation.form`, a form-mode elicitation replaces the two calls |
| `tellma_action` | `entity`, `action`, `ids?`, `input?` | per attribute (`Idempotent`, `Destructive`) | `Destructive` actions use the same confirmation as delete |

The confirmation token is `IDataProtector`-protected (the data-protection key ring is shared across
instances by the host theme), bound to `(sub, tenantId, entity, sha256(sorted ids), withDescendants,
expires = now + 5 min)`, single-use per process (a bounded in-memory set; a replay on another
instance within five minutes is accepted — the operation is idempotent). MRTR `requestState`
uses the same protector. Every tool re-evaluates permissions through the service on every call; a
handle is a name, not a capability.

Result shaping: `MaxToolResultChars` 60,000 (Claude Code's default cap is 25,000 tokens);
`tellma_query` and `tellma_get` declare **no `outputSchema`** (declaring one obliges the server
to send `structuredContent` *and* the text block, doubling tokens); `tellma_whoami` and
`tellma_describe` declare one. Tool descriptions state that returned data is tenant content, not
instructions. Never exposed: `delete-by-query`, settings edit, `Mcp = Hidden` actions,
`[ApiResource(Mcp = ReadOnly)]` write operations. Input validation and business outcomes are
`isError` tool results with `errors`/`errorDetails` rendered; protocol errors are reserved for
malformed requests. Distribution tools (`m.WithTools<T>()`) run under the same bearer and must
call services; a distribution tool that runs SQL directly bypasses permissions — a residual risk
the deferred bypass analyzer should cover.

**Rationale.** Under ten tools stays far below the degradation threshold; the static list is
cacheable; the write-safety rules are the difference between an agent that can be trusted with
`save` and one that cannot. An agent offered `overrideConcurrency` sets it; an agent offered
`confirm: true` sets it — neither is a safeguard.

**Alternatives rejected.** Per-permission `tools/list` (a permission evaluation per list; hiding is
not security). `confirm: true` (no safeguard). Exposing override (lost updates by default).

**Confidence.** Medium-high. **Review flag.** Shipping all seven tools in 0015 versus host, auth,
and the four read tools in 0015 with the three write tools after the first agent evaluation.

### D17 — MCP topology: one endpoint per tenant; the audience and every self-referencing URL come from `Tellma:PublicOrigin`

**Decision.** `/{tenantId}/mcp` is the MCP server of exactly one tenant; its RFC 8707 resource
identifier is `{PublicOrigin}/{tenantId}/mcp`; the PRM document lives at
`{PublicOrigin}/.well-known/oauth-protected-resource/{tenantId}/mcp` and is populated per request
through `McpAuthenticationEvents.OnResourceMetadataRequest` with `resource`,
`authorization_servers = [issuer]`, `scopes_supported = ["tellma_api"]`,
`bearer_methods_supported = ["header"]`, `resource_name = "<tenant> — <distribution>"`.
Contradiction with computing `https://{host}/{tenantId}/mcp` from the request: `Host` is
attacker-controlled on any deployment that does not pin host filtering, and a PRM document that
echoes it is a cache-poisoning and phishing primitive (a client that trusts the document is sent
to whatever `resource` it names). `Tellma:PublicOrigin` is required configuration validated at
startup; the same value feeds the CSRF origin check (D9) and the BFF redirect URIs. The token's
`aud` must equal the resource exactly; a token whose `aud` is the distribution's API audience
(`{PublicOrigin}` without a path) is refused at `/mcp`, and an MCP-audience token is refused at
`/api/v1` — audiences are not interchangeable.

**Confidence.** High. **Review flag.** None.

### D18 — MCP resource-server authentication: `JwtBearer` against the issuer; scope policy; membership and activity checks per call; service accounts cannot step up

**Decision.** `AddMcp()` registers `JwtBearer` (authority = issuer; JWKS cached; `MapInboundClaims
= false` so `sub` stays `sub`; `ValidateIssuer`; `AudienceValidator` accepts exactly the D17
resource; `RequireHttpsMetadata` true outside Development; clock skew 60 s against 10-minute
tokens) as the authenticate scheme and the SDK's scheme as the challenge scheme. Policy
`TellmaMcp` requires an authenticated principal whose `scope` contains `tellma_api`. Denied
requests get the SDK's 401 with `resource_metadata`; insufficient scope gets 403 with
`error="insufficient_scope"`. After bearer validation the request context is populated from the
principal and route values (`Client = "mcp"`, display zone from the user's preference), and every
tool call runs the service's connect step: subject → active member, tenant not suspended; a
deactivated user is denied on the next call even though the token is still valid (signed-only
JWTs cannot be revoked; 10-minute lifetime bounds the rest). A `client_credentials` principal
(no `auth_time`) hitting a step-up-required operation gets `HumanRequiredException` (403), never a
step-up challenge it cannot answer. The SDK's `Origin` validation is configured with the hosted
clients' origins as the allow-list (native clients send none). No CORS policy.

**Confidence.** High. **Review flag.** None.

### D19 — What the identity server must add, in order

1. **Per-tenant resources under a granted origin, path-pattern restricted.** A requested
   `resource` is accepted when it equals a granted distribution origin **or** matches
   `<granted origin>/{int}/mcp` exactly; it becomes `aud` verbatim. Refinement of "any resource
   under a granted origin": the pattern keeps the audience space enumerable and prevents a client
   from minting tokens for paths the distribution never serves.
2. **Client ID Metadata Documents**: advertise `client_id_metadata_document_supported: true`;
   fetch URL-shaped client ids with SSRF guards (HTTPS only, no private ranges, 5 KB cap, 5 s
   timeout, cached per HTTP headers), validate `client_id` equality and exact `redirect_uris`,
   auth method `none` or `private_key_jwt` only; show the client hostname on consent.
3. Advertise `none` in `token_endpoint_auth_methods_supported`.
4. Until 2 lands: pre-registered public native clients (`claude-code`, `codex`, `cursor`) with
   port-less loopback redirects and the `tellma_api` scope plus every distribution origin.
5. Keep: PKCE S256 advertised and required, `iss` in authorization responses, refresh-token
   rotation, `resource` on refresh requests validated against the original grant (a refresh must
   not widen `aud`).

**Confidence.** Medium-high. **Review flag.** CIMD on the platform server versus waiting for
OpenIddict 8.

### D20 — Naming

The runtime server is the **Tellma Tenant MCP server** (`Tellma.Core.Mcp`, `/{tenantId}/mcp`,
`serverInfo.name = "tellma-tenant"`); the developer tooling stays the Tellma Developer MCP
(`dotnet tellma mcp`, `tellma-dev`). Tool names use underscores. Headers are `Tellma-*`. Problem
codes are kebab-case. **Confidence.** High.

### D21 — OpenAPI in Development as a contract fixture; the `v1` seam

**Decision.** `AddOpenApi("web")` emits `/openapi/web.json` (3.1) in Development, with a
transformer adding `x-tellma-securable`, marking `[ServerOwned]` properties `readOnly`, and
listing `[RelatedSelect]` columns per navigation. The reference distribution's test project
snapshots this document; a diff fails the build unless the snapshot is updated in the same change
and the change respects the N−1 rules of D6 (a removed property or renamed member in the diff is
a test failure regardless). `/{tenantId}/api/v1` is reserved: `Asp.Versioning.Http` 10.2.x
URL-segment versioning, bearer `tellma_api` with `aud = {PublicOrigin}`, REST verbs, explicit
`[ApiResource(Public = true)]` opt-in, `Idempotency-Key`. Nothing else is built now.

**Confidence.** High.

### D22 — Tests that must exist

`Tellma.Core.AspNetCore.Tests`: the securable audit (an unsecured endpoint fails startup; an
unregistered action fails startup); the CSRF matrix (missing header, wrong content type, foreign
origin, bearer on web); the problem-details contract (every exception in the closed set → status,
code, members; a 500 carries no message); N−1 tolerance (unknown inbound members skipped and
counted; every response type deserializes into last-minor wire types); lossless numbers
(`decimal(19,4)` and `long` round-trip through the writer bit-exactly; over-precision inbound →
422 `precision`); concurrency modes (missing stamp → 422; mismatch → 409; override skips);
`related` projection (no non-projected column ever appears); navigation traversal (D12 denies
and allows correctly); delete-by-query count mismatch rolls back; count cap and paging window;
request timeout cancels the SQL command and returns the connection; rate-limit partitions.
`Tellma.Core.Mcp.Tests`: PRM document per tenant from the configured origin; audience separation
(distribution-audience token refused); deactivated member denied; delete requires token or
elicitation; save conflict returns `isError` with stored values; result cap truncation; tool list
determinism and `ttlMs`.

**Confidence.** High.

---

## 3. Contracts

Compilable-looking C#; XML docs abbreviated. Namespaces are final.

### 3.1 Wire records — `Tellma.Core.Abstractions.Api`

```csharp
namespace Tellma.Core.Abstractions.Api;

/// <summary>A search-page or report query over one entity, in Queryex text.</summary>
public sealed record QueryRequest
{
    /// <summary>The select list; null selects the stack's declared default.</summary>
    public string? Select { get; init; }
    /// <summary>The row predicate; row-level security is composed server-side.</summary>
    public string? Filter { get; init; }
    /// <summary>The group predicate; only with <see cref="Aggregate"/>.</summary>
    public string? Having { get; init; }
    /// <summary>The ordering list with direction suffixes.</summary>
    public string? OrderBy { get; init; }
    /// <summary>Rows to skip; skip + take is bounded by the configured window.</summary>
    public int Skip { get; init; }
    /// <summary>Rows to return; clamped to the configured maximum.</summary>
    public int? Take { get; init; }
    /// <summary>Free-text term mapped onto the entity's declared searchable columns.</summary>
    public string? Search { get; init; }
    /// <summary>Declared-parameter values; types are inferred from the expressions.</summary>
    public IReadOnlyDictionary<string, JsonElement>? Arguments { get; init; }
    /// <summary>Whether to return the paging-independent count, capped in SQL.</summary>
    public bool IncludeCount { get; init; }
    /// <summary>Tree entities only: return non-matching ancestors of matched rows.</summary>
    public bool IncludeAncestors { get; init; }
    /// <summary>True for a grouped query.</summary>
    public bool Aggregate { get; init; }
}

/// <summary>The kind of a result column, deciding its JSON encoding and typed buffer.</summary>
public enum QueryColumnKind { Int32, Int64, Int16, Byte, Decimal, Double, Boolean, String, Date, DateTime, DateTimeOffset, Time, Guid, Binary, HierarchyId }

/// <summary>One result column, mirroring the compiled query's column.</summary>
public sealed record QueryColumn(string Name, string Type, string? StoreType, QueryColumnKind Kind, bool Nullable, IReadOnlyList<string>? Path, bool GroupingKey);

/// <summary>Columnar row storage: one typed buffer per column and a null bitmap per nullable column. Written as arrays of arrays.</summary>
[JsonConverter(typeof(QueryRowSetJsonConverter))]
public sealed class QueryRowSet
{
    /// <summary>The columns, in wire order.</summary>
    public IReadOnlyList<QueryColumn> Columns { get; }
    /// <summary>The number of rows.</summary>
    public int RowCount { get; }
    /// <summary>The typed buffer of a column (e.g. <c>int[]</c>, <c>decimal[]</c>, <c>string?[]</c>).</summary>
    public Array GetBuffer(int column);
    /// <summary>Whether the cell is SQL NULL.</summary>
    public bool IsNull(int column, int row);
    /// <summary>A builder filled by the data-access reader through typed getters.</summary>
    public sealed class Builder { public Builder(IReadOnlyList<QueryColumn> columns, int capacity); public void AppendRow(ReadOnlySpan<CellValue> cells); public QueryRowSet Build(); }
}

/// <summary>Query rows plus the capped count and the ancestors of a tree page.</summary>
public sealed record QueryResult
{
    public required QueryRowSet Rows { get; init; }
    public int? Count { get; init; }
    public bool CountCapped { get; init; }
    public QueryRowSet? Ancestors { get; init; }
}

/// <summary>Details request for one entity.</summary>
public sealed record GetRequest(int Id, string? Select = null, IReadOnlyList<string>? Include = null);

/// <summary>A request over a set of ids.</summary>
public sealed record IdsRequest
{
    public required IReadOnlyList<int> Ids { get; init; }
    public bool ReturnEntities { get; init; } = true;
    public string? Select { get; init; }
    public IReadOnlyList<string>? Include { get; init; }
}

/// <summary>Tree view request: the children of the given parents (null or empty = roots).</summary>
public sealed record ParentIdsRequest(IReadOnlyList<int>? ParentIds, string? Select = null, string? Filter = null, IReadOnlyDictionary<string, JsonElement>? Arguments = null);

/// <summary>How a save treats the concurrency stamp of updated entities.</summary>
public enum ConcurrencyMode
{
    /// <summary>Every update must carry the stored stamp; missing → 422, mismatch → 409.</summary>
    Check,
    /// <summary>No stamp is checked; the caller's version wins.</summary>
    Override,
}

/// <summary>Whole-entity save of one or more entities with their child collections.</summary>
public sealed record SaveRequest<TEntity>
{
    public required IReadOnlyList<TEntity> Entities { get; init; }
    public bool ReturnEntities { get; init; } = true;
    public string? Select { get; init; }
    public IReadOnlyList<string>? Include { get; init; }
    public ConcurrencyMode Concurrency { get; init; } = ConcurrencyMode.Check;
}

/// <summary>Delete every row matching a filter; rolled back unless exactly <see cref="ExpectedCount"/> rows were deleted.</summary>
public sealed record DeleteByQueryRequest(string Filter, IReadOnlyDictionary<string, JsonElement>? Arguments, int ExpectedCount);

/// <summary>Related entities by entity name, each a typed list in the entity's related projection. Serialized as name → array.</summary>
[JsonConverter(typeof(RelatedEntitiesJsonConverter))]
public sealed class RelatedEntities
{
    /// <summary>Adds one typed set; the converter resolves one JsonTypeInfo per set.</summary>
    public void Add<T>(string entityName, IReadOnlyList<T> entities, IReadOnlySet<string> projection) where T : class;
    public IReadOnlyDictionary<string, RelatedEntitySet> Sets { get; }
}

/// <summary>One typed set: the runtime entity type, the rows, and the projected property names.</summary>
public sealed record RelatedEntitySet(Type EntityType, IReadOnlyList<object> Entities, IReadOnlySet<string> Projection);

/// <summary>Entities in wire shape with related projections, extras, and search-row echoes.</summary>
public sealed record EntitiesResult<TEntity>
{
    public required IReadOnlyList<int> Ids { get; init; }
    public required IReadOnlyList<TEntity> Entities { get; init; }
    public RelatedEntities? Related { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Extras { get; init; }
    public QueryRowSet? Rows { get; init; }
}

/// <summary>The row count an operation affected.</summary>
public sealed record AffectedResult(int Count);

/// <summary>A long operation accepted for background execution.</summary>
public sealed record TaskAccepted(long TaskId);
```

### 3.2 Declaration attributes — `Tellma.Core.Abstractions.Api`

```csharp
namespace Tellma.Core.Abstractions.Api;

/// <summary>Overrides or annotates an entity's API identity; absent, the resource derives from the table name.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class ApiResourceAttribute(string? resource = null) : Attribute
{
    public string? Resource { get; } = resource;
    public string? Description { get; init; }
    public McpExposure Mcp { get; init; } = McpExposure.Full;
    public bool Public { get; init; }
}

/// <summary>The default select list when a query names none.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class DefaultSelectAttribute(string select) : Attribute { public string Select { get; } = select; }

/// <summary>The columns exposed when the entity is reached through a navigation: in <c>related</c> and in query paths without read permission on it.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class RelatedSelectAttribute(string select) : Attribute { public string Select { get; } = select; }

/// <summary>Marks a public service method as an API action and an MCP action.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ApiActionAttribute(string name) : Attribute
{
    public string Name { get; } = name;
    /// <summary>The securable action; default is <see cref="Name"/>; must exist in the securables registry at startup.</summary>
    public string? Action { get; init; }
    public bool MemberOnly { get; init; }
    public bool Idempotent { get; init; }
    /// <summary>Destructive actions require confirmation on MCP.</summary>
    public bool Destructive { get; init; }
    public McpExposure Mcp { get; init; } = McpExposure.Full;
    public string? Description { get; init; }
}

/// <summary>Route prefix for a non-entity API service.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ApiRouteAttribute(string route) : Attribute { public string Route { get; } = route; }

/// <summary>A column the client may send but the emitter never writes from the payload.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class ServerOwnedAttribute : Attribute
{
    /// <summary>True for write-once columns: written on insert, never on update.</summary>
    public bool AfterCreate { get; init; }
}

/// <summary>MCP exposure of a stack or action.</summary>
public enum McpExposure { Full, ReadOnly, Hidden }
```

### 3.3 The closed exception set — `Tellma.Core.Abstractions.Api`

```csharp
namespace Tellma.Core.Abstractions.Api;

/// <summary>Base of every exception the platform maps to a problem response; the subclass set is closed.</summary>
public abstract class TellmaProblemException(string code, string? detail = null, Exception? inner = null) : Exception(detail, inner)
{
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, string> Arguments { get; init; } = new Dictionary<string, string>();
}

public sealed class BadRequestException(string? detail = null) : TellmaProblemException("bad-request", detail);
public sealed class QueryInvalidException(IReadOnlyList<QueryDiagnostic> diagnostics) : TellmaProblemException("query-invalid") { public IReadOnlyList<QueryDiagnostic> Diagnostics { get; } = diagnostics; }
public sealed record QueryDiagnostic(string Clause, string Code, int Start, int Length, string? Location, IReadOnlyDictionary<string, string> Arguments);
public sealed class ValidationFailedException(IReadOnlyList<ValidationError> errors) : TellmaProblemException("validation") { public IReadOnlyList<ValidationError> Errors { get; } = errors; }
/// <summary>One field error at a wire-cased path such as "entities[0].roleMemberships[2].roleId".</summary>
public sealed record ValidationError(string Path, string Code, IReadOnlyDictionary<string, string> Arguments);
public sealed class StepUpRequiredException(string acrValues, int? maxAge) : TellmaProblemException("step-up-required") { public string AcrValues { get; } = acrValues; public int? MaxAge { get; } = maxAge; }
public sealed class HumanRequiredException() : TellmaProblemException("human-required");
public sealed class ForbiddenException(string resource, string action) : TellmaProblemException("forbidden") { public string Resource { get; } = resource; public string Action { get; } = action; }
public sealed class TenantSuspendedException(bool readOnly) : TellmaProblemException(readOnly ? "tenant-read-only" : "tenant-suspended");
public sealed class NotFoundException(string entity, int? id = null) : TellmaProblemException("not-found") { public string Entity { get; } = entity; public int? Id { get; } = id; }
public sealed class ConcurrencyConflictException(IReadOnlyList<ConcurrencyConflict> conflicts) : TellmaProblemException("concurrency-conflict") { public IReadOnlyList<ConcurrencyConflict> Conflicts { get; } = conflicts; }
/// <summary>ModifiedAt is the opaque stamp string the client must echo.</summary>
public sealed record ConcurrencyConflict(int Id, string ModifiedAt, int ModifiedById, string? ModifiedByName);
public sealed class CountMismatchException(int expected, int actual) : TellmaProblemException("count-mismatch") { public int Expected { get; } = expected; public int Actual { get; } = actual; }
public sealed class PayloadTooLargeException(string limit, long actual, long maximum) : TellmaProblemException("payload-too-large");
public sealed class DependencyUnavailableException(string dependency, TimeSpan? retryAfter = null, Exception? inner = null) : TellmaProblemException("dependency-unavailable", inner: inner) { public string Dependency { get; } = dependency; public TimeSpan? RetryAfter { get; } = retryAfter; }
```

### 3.4 Header and telemetry names — `Tellma.Core.Abstractions.Api`

```csharp
namespace Tellma.Core.Abstractions.Api;

/// <summary>Request and response header names of the platform surfaces.</summary>
public static class TellmaHeaders
{
    public const string TimeZone = "Tellma-Time-Zone";
    public const string Calendar = "Tellma-Calendar";
    public const string Client = "Tellma-Client";
    public const string CacheTags = "Tellma-Cache-Tags";
    public const string Build = "Tellma-Build";
}

/// <summary>Meter and instrument names of the web surface.</summary>
public static class ApiTelemetryNames
{
    public const string MeterName = "Tellma.Core.AspNetCore";
    public const string RequestsRejected = "tellma.api.requests.rejected";
    public const string Problems = "tellma.api.problems";
    public const string OperationDuration = "tellma.api.operation.duration";
    public const string OperationDbCalls = "tellma.api.operation.db_calls";
    public const string PermissionsStale = "tellma.api.permissions.stale";
    public const string UnknownMembers = "tellma.api.unknown_members";
    public const string QueryDiscoverDuration = "tellma.api.query.discover.duration";
    public const string ResourceTag = "tellma.resource";
    public const string OperationTag = "tellma.operation";
    public const string ClientTag = "tellma.client";
    public const string ReasonTag = "reason";
}

/// <summary>Meter and instrument names of the Tellma Tenant MCP server.</summary>
public static class McpTelemetryNames
{
    public const string MeterName = "Tellma.Core.Mcp";
    public const string ToolCalls = "tellma.mcp.tool.calls";
    public const string ToolDuration = "tellma.mcp.tool.duration";
    public const string ResultChars = "tellma.mcp.result.chars";
    public const string ToolTag = "tool";
    public const string OutcomeTag = "outcome";
}
```

### 3.5 Host surface — `Tellma.Core.AspNetCore`

```csharp
namespace Tellma.Core.AspNetCore;

/// <summary>Maps every platform surface; the distribution calls it once.</summary>
public static class TellmaEndpointRouteBuilderExtensions
{
    /// <summary>Maps the tenant group and every projected endpoint, the MCP server when registered, the well-known
    /// documents, health, the distribution contract surface, the webhook fronting, and the SPA fallback; then runs
    /// the securable audit and fails startup on any finding.</summary>
    public static TellmaEndpoints MapTellma(this WebApplication app);
}

/// <summary>Handles to the mapped groups, for the distribution's hand-mapped endpoints.</summary>
public sealed class TellmaEndpoints
{
    /// <summary>The /{tenantId}/api/web group carrying CSRF, problem mapping, negotiation, limits, securable filter, telemetry.</summary>
    public RouteGroupBuilder TenantGroup { get; }
    /// <summary>Endpoints outside any tenant (anonymous; output-cached where declared).</summary>
    public RouteGroupBuilder DistributionGroup { get; }
}

/// <summary>Securable conventions for hand-mapped endpoints.</summary>
public static class TellmaEndpointConventionBuilderExtensions
{
    public static TBuilder RequireSecurable<TBuilder>(this TBuilder builder, string resource, string action) where TBuilder : IEndpointConventionBuilder;
    public static TBuilder AllowMember<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder;
    /// <summary>Declares that the endpoint reads multipart/form-data (import, blob upload); the CSRF filter allows it there only.</summary>
    public static TBuilder AcceptsMultipart<TBuilder>(this TBuilder builder, long maxBytes) where TBuilder : IEndpointConventionBuilder;
}

/// <summary>Endpoint metadata read by the securable filter, the audit, OpenAPI, and MCP.</summary>
public sealed record SecurableEndpointMetadata(string Resource, string Action);
public sealed record MemberOnlyEndpointMetadata;
public sealed record AcceptsMultipartMetadata(long MaxBytes);

/// <summary>Bound from "Tellma:Api". Every member has a default.</summary>
public sealed class TellmaApiOptions
{
    /// <summary>The distribution's public origin, e.g. https://etpharma.app.tellma.com. Required; validated at startup.</summary>
    public required Uri PublicOrigin { get; set; }
    public long MaxJsonBodyBytes { get; set; } = 8 * 1024 * 1024;
    public long MaxUploadBytes { get; set; } = 100L * 1024 * 1024;
    public int MaxEntitiesPerSave { get; set; } = 1_000;
    public int MaxIdsPerRequest { get; set; } = 10_000;
    public int MaxTake { get; set; } = 10_000;
    public int MaxCount { get; set; } = 10_000;
    public int MaxSkipWindow { get; set; } = 100_000;
    public int RequestsPerMinutePerUser { get; set; } = 600;
    public int ConcurrentRequestsPerTenant { get; set; } = 64;
    public int AnonymousRequestsPerMinutePerIp { get; set; } = 60;
    public int ConcurrentExportsPerUser { get; set; } = 2;
    public int ConcurrentImportsPerUser { get; set; } = 1;
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan LongRequestTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public bool EnableCompression { get; set; } = true;
    public bool ServerTiming { get; set; }
    public ISet<string> ClientNames { get; } = new HashSet<string>(StringComparer.Ordinal) { "web", "cli" };
    public string ProblemTypeBase { get; set; } = "https://tellma.com/problems/";
}
```

```csharp
namespace Tellma.Core.Mcp;

/// <summary>Registers the Tellma Tenant MCP server; called inside AddTellma.</summary>
public static class TellmaMcpBuilderExtensions
{
    public static TellmaBuilder AddMcp(this TellmaBuilder tellma, Action<TellmaMcpOptions>? configure = null);
}

/// <summary>Bound from "Tellma:Mcp".</summary>
public sealed class TellmaMcpOptions
{
    public int MaxToolResultChars { get; set; } = 60_000;
    public int DefaultTop { get; set; } = 50;
    public int MaxTop { get; set; } = 500;
    public int MaxIdsPerCall { get; set; } = 100;
    public int ToolCallsPerMinutePerUser { get; set; } = 120;
    public TimeSpan ToolTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan ConfirmationLifetime { get; set; } = TimeSpan.FromMinutes(5);
    public int ListTtlMs { get; set; } = 300_000;
    /// <summary>Origins of browser-hosted MCP clients allowed by the SDK's Origin check.</summary>
    public ISet<string> AllowedOrigins { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "https://claude.ai", "https://chatgpt.com" };
    /// <summary>Extra SDK tool types a distribution adds.</summary>
    public IList<Type> ToolTypes { get; } = [];
}
```

### 3.6 Shapes needed from other themes' seams

From the **service-pipeline theme** (seam 3): `IEntityStackCatalog` with `Stacks`, `Services`,
`FindByResource`, `FindByEntityName`; `EntityStackDescriptor(EntityName, Resource, EntityType,
ServiceType, Operations, Actions, Properties, Children, SearchableProperties, NaturalKey,
DefaultSelect, RelatedSelect, Description, Mcp, Public)`; `ApiActionDescriptor(Name, Action,
MemberOnly, Idempotent, Destructive, Mcp, Description, Method, RequestType, ResultType)`; and the
generic service surface:

```csharp
public interface IEntityService<TEntity>
{
    Task<QueryResult> QueryAsync(QueryRequest request, CancellationToken cancellationToken);
    Task<EntitiesResult<TEntity>> GetAsync(GetRequest request, CancellationToken cancellationToken);
    Task<EntitiesResult<TEntity>> GetByIdsAsync(IdsRequest request, CancellationToken cancellationToken);
    Task<EntitiesResult<TEntity>> SaveAsync(SaveRequest<TEntity> request, CancellationToken cancellationToken);
    Task<AffectedResult> DeleteAsync(IdsRequest request, CancellationToken cancellationToken);
    Task<AffectedResult> DeleteByQueryAsync(DeleteByQueryRequest request, CancellationToken cancellationToken);
}
public interface ITreeService<TEntity> : IEntityService<TEntity>
{
    Task<QueryResult> GetByParentIdsAsync(ParentIdsRequest request, CancellationToken cancellationToken);
    Task<AffectedResult> DeleteWithDescendantsAsync(IdsRequest request, CancellationToken cancellationToken);
}
public interface IActivatableService<TEntity> : IEntityService<TEntity>
{
    Task<EntitiesResult<TEntity>> ActivateAsync(IdsRequest request, CancellationToken cancellationToken);
    Task<EntitiesResult<TEntity>> DeactivateAsync(IdsRequest request, CancellationToken cancellationToken);
}
```

From the **distribution-host theme** (seam 9):

```csharp
namespace Tellma.Core.Abstractions.Requests;

public enum TenantKind { Live, Sandbox }

/// <summary>Immutable facts of the current request or job scope; populated in stages by the host.</summary>
public sealed record RequestContext
{
    public required int TenantId { get; init; }
    public required TenantKind TenantKind { get; init; }
    /// <summary>The tenant's zone; binds the Queryex today() and TimeZone slots.</summary>
    public required TimeZoneInfo TenantTimeZone { get; init; }
    /// <summary>Header → user preference → tenant; formats instants for display only.</summary>
    public required TimeZoneInfo DisplayTimeZone { get; init; }
    /// <summary>Today in <see cref="TenantTimeZone"/>.</summary>
    public required DateOnly Today { get; init; }
    public string? Subject { get; init; }
    public int? UserId { get; init; }
    /// <summary>True for a client_credentials principal (no auth_time; cannot step up).</summary>
    public bool IsServiceAccount { get; init; }
    public required CultureInfo Culture { get; init; }
    public required string CalendarCode { get; init; }
    public required string Client { get; init; }
    public ClaimsPrincipal? Principal { get; init; }
    /// <summary>Version tags read by the connect step, echoed in Tellma-Cache-Tags.</summary>
    public IReadOnlyDictionary<string, string>? Tags { get; init; }
}

/// <summary>Scoped holder; no AsyncLocal. Copied into job scopes by the background-task machinery.</summary>
public interface IRequestContextAccessor
{
    RequestContext Current { get; }
    void Enrich(Func<RequestContext, RequestContext> update);
}
```

From the **permissions theme** (seam 11): `IPermissionEvaluator` with
`ValueTask<bool> TryDenyFastAsync(string resource, string action)` (cache-only; true = definitely
no permission), `Task<PermissionDecision> EvaluateAsync(resource, action, ct)` returning
`{ Allowed, FilterTree? Filter, IReadOnlyList<PermissionReason> Why }`, and
`Task<PermissionMatrix> EvaluateAllAsync(ct)`; plus `ISecurablesRegistry.Contains(resource,
action)` for the startup audit. From the **data-access theme** (seams 1, 14): the batch executor
accepting a `CancellationToken` per command; `IDbCallCounter { int Calls; TimeSpan Elapsed; }`
scoped per request; the `QueryRowSet.Builder` fill contract (typed getters, one `AppendRow` per
row); `skip`/`take` as parameter slots. From the **settings theme** (seam 5): the tag names
`settings`, `permissions`, `user-settings`, `securables` and the tenant zone at resolution time.
From the **blob theme** (seam 12): `GET blobs/{id}` mapped with `AllowMember()` plus a service
check; `POST blobs/upload` with `AcceptsMultipart`. From the **Excel theme**: `ExportRequest`,
`ImportResult`. From the **background-task theme**: `TaskAccepted`, `tellma_task_status`.

---

## 4. Schema

This theme owns no tables. It reads `User.Subject`, `User.IsActive`, `User.Kind` (service
account), the sibling activity/tag table (users theme), the tenant tag table (settings theme),
and the tenant catalog row's state and kind (host theme). Its persistent artifacts are the
`Tellma:Api` and `Tellma:Mcp` configuration sections, whose schema is §3.5, with `PublicOrigin`
required.

---

## 5. Answers

| Brain-dump question (abridged) | Answer |
|---|---|
| "How do we extract the common CRUD endpoint boilerplate into the platform Core?" | D7: `MapTellma()` projects every endpoint from the stack catalog through closed generic handlers; custom actions are `[ApiAction]`; modules have no other endpoint mechanism. |
| "Can we use source-generated JSON serialization?" | D6: yes for every envelope, request, problem, `QueryRowSet`, `RelatedEntities`; entities resolve by reflection appended to the chain and are validated at startup; rows are written by a converter over typed buffers (D4). |
| "Should we accept an X-Today header … or the user's timezone?" | D10: neither binds `today()`. `Tellma-Time-Zone` is display-only; the engine's `today()` and `TimeZone` slots bind to the tenant zone because stored filters and row-level security must be deterministic. |
| Three surfaces | D2/D3/D21: web built (POST-only), MCP built (D16–D19), `v1` reserved with verbs, versioning, audience, and `Idempotency-Key` fixed. |
| "The MCP schema is discoverable … can be changed without breaking agents (is this true?)" | Partly: adding tools and optional arguments is safe; renaming or removing a tool or argument breaks saved prompts and skills. Tool and argument names are a compatibility surface. |
| "How would the web layer determine the status code?" | D11: a closed exception set in Abstractions and one mapping in an endpoint filter; distributions throw platform types only; anything else is a 500 with a trace id. |
| "Messages or codes?" | D11: both — localized `errors`, machine `errorDetails`. |
| Rate limiting "purely in-memory" | D13: in-process partitioned limiters per user, per tenant, per IP; size by endpoint metadata; cardinality and length by validation. |
| "One MCP server per tenant, or per distro?" | D17: per tenant; the audience is the tenant MCP URL from the configured origin. |
| "Few tools, intent-based" | D16: seven generic tools, entity discovery inside `tellma_describe`, caps, confirmation tokens, no override. |
| MCP auth for humans and autonomous agents | D18/D19: OAuth 2.1 resource server with per-tenant PRM; humans via code + PKCE (CIMD or pre-registered native clients); agents via service-account `client_credentials`, which cannot step up. |
| "Should we keep the search parameter?" | Carried on the wire; the pipeline theme owns semantics (declared searchable columns; prefix match on codes, contains on names is the performant default). |
| "5 ids requested, 4 found: 4 or 404?" | D5: `get-by-ids` returns 4; `get` returns 404, identically for missing and invisible. |
| "Difficult to forget to secure an endpoint" | D8: metadata on every projected endpoint, `RequireSecurable`/`AllowMember` on hand-mapped ones, registry check and `AllowAnonymous` check in the startup audit, and the authoritative check inside the service. |
| Write-once columns: two UDTTs or a service rule? | D6: `[ServerOwned(AfterCreate = true)]`, excluded from the emitted `UPDATE` — a data-layer guarantee without a second UDTT. |
| "Optimistic concurrency … an override flag" | D5: `concurrency: check \| override`; the stamp is opaque; a missing stamp under `check` is a 422; the guard TVP runs inside the persist batch. |
| "Accessing a record I have no read permission on should look non-existent" | D11: 404 `not-found` with identical members; type-level absence of permission is 403 (reveals nothing row-specific). |
| "If you can read an entity you can read the related entities" | D5/D12: narrowed to the related projection; full rows of the target need `read` on it. |
| "Are those the proper layer names?" | Not this theme's; `Tellma.Core.AspNetCore` is the web layer. |

---

## 6. Seams

1. **Batch abstraction** (data access). Every command takes the request's `CancellationToken`;
   `skip`/`take` are parameter slots; the count-cap and ancestors statements ride the query batch;
   the delete-by-query count check is one statement with the delete; the reader fills
   `QueryRowSet.Builder` through typed getters. Contract: `IDbCallCounter` scoped per request.
2. **Entity class vs wire shape** (data access). Single class, `[NotMapped]` children,
   `[ServerOwned]` excluded by the emitter from `UPDATE`/`INSERT` lists, synchronize statements
   keyed on `(ParentId, Id)`, `[RelatedSelect]` on every entity, `MaxDepth = 16`.
3. **One capability, declared once** (service pipeline). The catalog descriptor is the only thing
   HTTP and MCP read; `Destructive` and `RelatedSelect` join it.
4. **Queryex schema per tenant** (data access). The projection never touches the schema; D12's
   traversal rule runs on discovery output, not on schema variants; `Name2`/`Name3` gating shows in
   `tellma_describe` per tenant.
5. **Version tags** (settings). Echoed in `Tellma-Cache-Tags` after the handler returns; the tag
   names are fixed; the tenant zone must be available at tenant resolution.
6. **Feature composition** (host). `AddMcp()` is a feature that `Requires` the stack feature;
   the projection is a contribution of the stack feature; `IEndpointContributor` exists only in
   `Tellma.Core.AspNetCore` for distribution code, never for modules.
7. **Natural keys** (data access). Reported by `tellma_describe`.
8. **Background-task columns** (background tasks). `TaskAccepted` is the 202 body.
9. **Request context** (host). Two zones, `Today` in the tenant zone, `IsServiceAccount`, `Tags`;
   no `AsyncLocal`; MCP populates the same holder from the bearer principal.
10. **Platform exceptions and mapping** (types: pipeline; mapping: here). §3.3 in Abstractions;
    `HumanRequiredException` added; `NotFoundException` for row-level misses; SQL error classes
    mapped as in D11.
11. **Permission evaluation** (permissions). `TryDenyFastAsync` (cache-only) for the endpoint
    filter; `EvaluateAsync` inside the service; `ISecurablesRegistry.Contains` for the audit.
12. **Blob staging tokens** (blobs). Tokens travel inside entity JSON; `upload` uses
    `AcceptsMultipart`.
13. **Wire shapes** (here). §3.1; Excel reuses `QueryRequest`/`IdsRequest`; the pipeline returns
    envelopes directly.
14. **Telemetry** (data access owns the DB budget). `tellma.api.operation.db_calls` is the
    per-operation aggregation of the data-access counter; the `Activity` carries the three tags.
15. **Notification enqueue** (background tasks). Not touched.
16. **Connect-call collapse** (users/pipeline). The endpoint filter performs no database call; the
    failure modes are stated in D8.
17. **Vocabulary**. Resource segments are kebab-case plural from the plural table name; securable
    actions `read`, `save`, `delete`, `activate` plus action names; problem codes kebab-case;
    headers `Tellma-*`; "Tellma Tenant MCP server"; "concurrency stamp" for `ModifiedAt` in its
    wire role.

---

## 7. Departures

1. **Verbs.** REST projection applies to `v1` only; the web surface is POST-only (D3).
2. **Route shape.** `api/{tenantId}/documents` becomes `/{tenantId}/api/web/{resource}/{operation}` (D2).
3. **New packages.** `Tellma.Core.AspNetCore` and `Tellma.Core.Mcp`; the webhooks package is no
   longer the only framework-referencing Core project (D1).
4. **Two MCP servers, named** (D20).
5. **Output caching** is confined to anonymous tenant-independent GETs (D14).
6. **Entity-class-as-wire** extends "no separate DTO model" to the wire path; the
   parent→child ban is read as an EF-model rule (D6).
7. **"If you can read an entity you can read all related entities"** (brain dump, not the
   architecture) is narrowed to the related projection (D5, D12).
8. **`today()`** stays as spec 0008 defines it; the departure the simplicity proposal recorded is
   withdrawn (D10).

---

## 8. Verification

Relied on from `research/web-api-mcp.md` (verified 2026-09-01): route groups and group-level
conventions; endpoint filters and factories; `TypedResults`; OpenAPI 3.1 per-request generation;
`[AsParameters]`/`BindAsync`; STJ options, `TypeInfoResolverChain`, .NET 10 `AllowDuplicateProperties`
and `Strict`; built-in validation is shape-only and PascalCase-keyed; `IProblemDetailsService`,
`IExceptionHandler`, RFC 9457; in-process partitioned rate limiting; `IRequestSizeLimitMetadata`;
request timeouts cancel `RequestAborted`; output caching never serves authenticated responses;
compression defaults and the HTTPS opt-in; antiforgery does not cover JSON endpoints; MCP
2026-07-28 statelessness, headers, MRTR, `ttlMs`, annotations, `isError`, the `outputSchema` →
`structuredContent` + text obligation; RFC 9728 path insertion, RFC 8707 `resource`, CIMD over
DCR, PKCE S256, 401/403 challenges; C# SDK 2.2.0 `MapMcp(pattern)`, `Stateless`, request filters,
`AddMcp`, `OnResourceMetadataRequest`, tool attribute defaults; OpenIddict 7.6.1 feature matrix;
the client matrix; Anthropic's tool guidance and Claude Code's 25,000-token cap;
`Asp.Versioning.Http` 10.2.3. From the briefing's digest: MERGE is out; retry is the executor's
job and one connection/one transaction/one batch; RCSI facts; `rowversion` unsuitable as a tag;
Guid tags; the `-u-ca-` trap; no standard zone header; App Service drain default 5 s; the invite
API facts; the dev admin `sub`.

Asserted here without a fresh source, to be verified by the spec author:

- Chromium, Firefox, and Safari send `Origin` on every POST including same-origin `fetch` (the
  basis of the D9 review flag).
- `RequestDelegateFactory` accepts a delegate created over a closed generic static method and
  reads its parameter attributes (`[FromBody]`) from `Delegate.Method`; fallback is a per-stack
  lambda closing over the resolved service.
- `PartitionedRateLimiter` disposes idle partitions on its internal timer (memory bound for
  per-user keys).
- STJ writes `decimal` with its full scale and never in exponent notation, and trims trailing
  zeros of `DateTime` fractional seconds without changing the value.
- The SDK's `Origin` validation accepts requests with no `Origin` header and rejects unlisted
  origins (the `AllowedOrigins` option assumes a configurable allow-list).
- Whether the SDK's default `ResourceMetadataUri` can express `{tenantId}`; otherwise the PRM
  document is mapped by hand on the distribution group and the per-request event supplies it.
- A lossless JSON parser for the SPA (`lossless-json` or equivalent) with a compatible license.
- SqlClient returns a cancelled command's connection to the pool with the transaction rolled back
  (attention-signal semantics) rather than marking it doomed.

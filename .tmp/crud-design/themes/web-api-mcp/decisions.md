# Web API surface and the MCP seam — settled design (theme `web-api-mcp`, future spec 0015)

Written 2026-09-01. This file is the complete design of the theme for a reader who has seen
nothing else: every name, shape, statement, and limit below is final unless §9 flags it. Contract
blocks use the platform's contract notation (`.tmp/crud-design/notation.md`): names are
normative; shape is described, not transcribed.

The theme owns the HTTP and MCP edge of the CRUD stack: how a distribution gets endpoints and
tools without writing them, the three surfaces and which ship now, verbs and cardinality, the wire
shapes every other theme produces or consumes, the exception-to-status contract, limits, headers,
CSRF, compression, and the Tellma Tenant MCP server with its authorization sketch. It owns no
tables.

---

## 1. Critique

The brain dump's web-layer sections ("Dedicated API per client", "Web Layer", "Exception
handling", "Rate limiting", "MCP Server") are right in direction — three surfaces with different
compatibility promises, all-POST for the SPA, an MCP surface that is not a 1:1 wrapper — and
wrong or silent in the places a reviewer trips on first.

**"Save admits a single entity" contradicts the guiding principle.** Save endpoints accept arrays;
the UI sends an array of one; import reuses the same pipeline. A single-entity endpoint is a
second code path for nothing.

**"X-Today" makes a client-controlled value an input to a security predicate.** Row-level
security filters are Queryex text authored by admins, and `today()` is a natural thing to write
in one (`ValidUntil >= today()`). If the request decides what `today()` means, a user shifts their
own visible row set by a day by choosing a header; the user's time zone has the same property. The
Queryex spec already binds `today()` to the tenant's zone; the web layer keeps it there and uses a
header zone only to *format*.

**"If you can read an entity you can read all the related entities" leaks personal data when
applied to full rows.** Every audited entity points at `User` through `CreatedById` and
`ModifiedById`; a caller who can read `Center` would receive every referenced user's email, mobile,
subject, and invitation state — columns they hold no permission on. The same leak exists in a
query: `select: "CreatedBy.Email"` traverses a navigation the caller may not read. What the rule
wants — no partially visible document — is satisfied by a *related projection* (key, code, display
names, active flag) that each entity declares once.

**Optimistic concurrency is under-specified on the wire.** An override flag says what happens on a
detected conflict; nothing says what happens when the client sends no stamp at all. If "no stamp"
means "no check", every client bug that drops the stamp becomes a silent lost update.

**Precision is never mentioned.** An accounting platform whose amounts are `decimal(19,4)` and
whose client is a JavaScript engine has a silent-corruption path through `JSON.parse`. The wire
must be lossless and the client contract must say how to parse it.

**"Rate limiting … number of entities … length of every free-text parameter" mixes three
mechanisms.** Rate is an in-process limiter; body size is server metadata enforced before the body
is read; cardinality and string length are validation. The constraint that matters — purely
in-memory, per instance — is exactly what `System.Threading.RateLimiting` is.

**The exception question is a false choice, twice.** "Enumerate exceptions or an interface": the
answer is a *closed set* of platform exception types mapped by one table the web layer owns. An
interface lets any distribution invent status codes; enumerating framework exceptions at the edge
leaks infrastructure. "Messages or codes": RFC 9457 has a place for both.

**CSRF is unstated.** All-POST plus a session cookie is the CSRF-exposed shape, and ASP.NET Core 10
antiforgery does not protect JSON endpoints automatically. The posture must be explicit.

**The MCP section defers what the design goals make a day-one requirement and skips the two hard
parts**: authorization against an identity server that today has neither protected-resource
metadata nor client-id metadata documents, and write safety for an agent that holds no concurrency
stamps and will set any `confirm` flag it is offered. With `User`, `Role`, and `Center` an admin
can already do real work through an agent; more importantly, the tools are projections of the same
catalog that produces the endpoints, so building them now is what proves the catalog carries the
metadata agents need.

**Nothing says how a distribution adds a custom endpoint**, and nothing says how the host reports
liveness, drains on deploy, or releases a database connection when a request times out.

**Detail choices to change.** `{tenantId}` is an `int`-constrained route segment. MCP tool names
use underscores (OpenAI's tool-name pattern rejects dots, and ChatGPT/Codex are named target
clients). Header names drop the deprecated `X-` prefix. The endpoint list gains `get-by-ids` (the
"five ids requested, four found" question has no endpoint to attach to otherwise).
`DeleteByQuery` gains an `expectedCount` checked *inside* the delete transaction.

**Orchestrator hints.** Adopted: POST-everywhere on the web surface with REST kept for `v1`; save
as an array; one entity class with not-mapped child collections plus a declared server-owned
split; RFC 9457 with property paths; one MCP endpoint per tenant; the startup audit over the
endpoint table; `ModifiedAt` as the concurrency stamp in its wire role. Refined: the connect-call
collapse is adopted with its failure modes stated as web-layer obligations; "output caching" is
confined to anonymous tenant-independent GETs. Rejected: the breakdown's "MCP left as a seam" (§9).

---

## 2. Decisions

### D1 — Packaging: `Tellma.Core.AspNetCore` and `Tellma.Core.Mcp`; wire contracts in `Tellma.Core.Abstractions.Api`; modules never reference ASP.NET Core

**Decision.** Two new Core-layer projects:

| Project | References | Contents |
|---|---|---|
| `Tellma.Core.AspNetCore` (`src/core/Tellma.Core.AspNetCore/`) | `Tellma.Core`; framework reference `Microsoft.AspNetCore.App` | `MapTellma()`, endpoint projection, request-context binding, the CSRF, problem, negotiation, securable, and telemetry endpoint filters, JSON options, limits, compression, health endpoints, OpenAPI (Development), the `RequireSecurable`/`AllowMember`/`AcceptsMultipart` conventions. Also the home of the BFF endpoints and tenant middleware owned by the distribution-host theme. |
| `Tellma.Core.Mcp` (`src/core/Tellma.Core.Mcp/`) | `Tellma.Core.AspNetCore`; `ModelContextProtocol.AspNetCore` 2.2.0 | The Tellma Tenant MCP server: seven generic tools, the protected-resource-metadata document, bearer and challenge wiring, result shaping and caps, the delete-confirmation token. |

Wire contracts — request and response records, `QueryRowSet`, `RelatedEntities`, the closed
exception set, the declaration attributes, header names, telemetry name constants — live in
`Tellma.Core.Abstractions` under namespace `Tellma.Core.Abstractions.Api` and reference only the
BCL (`System.Text.Json` included). A module package (`Tellma.Module.Gl`) therefore declares
`[ApiAction]` methods, throws platform exceptions, and returns platform envelopes without
referencing any web package. A module that needs an endpoint the projection cannot express ships a
separate `Tellma.Module.<M>.AspNetCore` package; the module itself never takes a framework
reference.

Tests: `test/core/Tellma.Core.AspNetCore.Tests` (in-process `TestServer` over a fixture composition
using the data-access theme's LocalDB fixture entities; no `Category=Integration` trait) and
`test/core/Tellma.Core.Mcp.Tests` (the SDK's in-memory client over the same fixture host). D23
lists the mandatory test classes. New pins in `Directory.Packages.props`:
`ModelContextProtocol.AspNetCore` 2.2.0, `Microsoft.AspNetCore.OpenApi` on the framework's patch
line (10.0.11 is current; the repo's `Microsoft.AspNetCore.*` pin at 10.0.9 moves with it).

**Rationale.** `Tellma.Core` stays framework-free (the migrator and every test project reference
it without a web host); today the only Core project with a framework reference is
`Tellma.Core.Webhooks`. The wire contracts belong in Abstractions because three consumers
(pipeline, Excel, MCP) and every module produce or consume them. MCP is separate so an air-gapped
distribution can omit the SDK.

**Alternatives rejected.** One package for web and MCP (forces the SDK on every distribution).
Wire contracts in `Tellma.Core.AspNetCore` (modules could not return them). Naming the web package
`Tellma.Core.Web` (collides in spirit with `Tellma.Identity.Web`, a deployable).

**Confidence.** High.

### D2 — Surfaces and routes: tenant-first, `int`-constrained; the web surface and the MCP server ship in 0015, `v1` is a reserved seam

**Decision.**

```
/{tenantId:int}/api/web/{resource}/{operation}                 the private SPA surface (built)
/{tenantId:int}/api/web/blobs/{id}                             the one GET on the web surface (blob theme)
/{tenantId:int}/api/v1/…                                       the versioned public surface (reserved seam, D22)
/{tenantId:int}/mcp                                            the Tellma Tenant MCP server (built, D17–D21)
/.well-known/oauth-protected-resource/{tenantId:int}/mcp       the MCP protected-resource metadata (GET, anonymous)
/api/distribution-info                                         distribution contract surface (host theme)
/api/webhooks/{key}                                            spec 0007, unchanged
/healthz                                                       liveness: anonymous, no I/O, 200 once the composition gate passed
/readyz                                                        readiness: anonymous; checks the tenant registry source and the data-protection key ring
/openapi/web.json                                              Development only
```

`{resource}` is the entity's **resource name**: the lowercase kebab-case plural derived from the
EF table name (`core.Users` → `users`, `gl.Centers` → `centers`, `gl.InvoiceLines` →
`invoice-lines`), overridable with `[ApiResource("…")]` on the entity class. The same string is
the securable resource id (permissions theme), the OpenAPI tag, and the default Excel sheet name.
The Queryex logical name (`Center`) is the **entity name**, used as the `related` dictionary key
and as the MCP `entity` argument. `{operation}` is one of the standard segments below or an
`[ApiAction]` name; a standard operation exists only when the service declares the capability
(D8):

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
| `export` / `export-for-import` | `read` | `ExportRequest` (Excel theme) | `.xlsx` stream with `Content-Disposition`, or 202 `TaskAccepted` |
| `import` | `save` | `multipart/form-data` | `ImportResult` (Excel theme), or 202 `TaskAccepted` |
| `{action}` | the attribute's action | the method's request type | the method's return type |

The SPA fallback (`index.html`) excludes `/api`, `/mcp`, `/.well-known`, `/openapi`, `/healthz`,
`/readyz`; a tenant-prefixed deep link (`/17/centers/5`) never collides with the API because the
API always has `api` or `mcp` as its second segment. Health endpoints sit outside the tenant prefix
because they are per instance, not per tenant; liveness must not touch a database (an outage must
not make the orchestrator kill healthy instances), readiness must (a new instance must not receive
traffic before it can resolve tenants).

**Rationale.** Tenant-first lets one route group carry tenant resolution for every surface and
makes the RFC 9728 path-insertion rule produce a per-tenant metadata document. `int` is stable,
short, and free of the reserved-word and normalization problems a slug carries; a slug alias can be
mapped onto the same group later without breaking clients. Deriving the resource segment from the
table name means the distribution declares nothing for the common case.

**Alternatives rejected.** `/api/web/{tenantId}/…` (splits the surfaces from the tenant prefix
and makes the PRM path odd). Singular resource segments (tables are plural per the architecture;
one derivation rule beats two). Free-form `{tenantId}` (ambiguous matches with SPA routes).

**Confidence.** High on shape; the `int`-versus-slug call belongs to the distribution-host theme
(§10). **Review flag** in §9.

### D3 — Verbs: the web surface is POST-only; `v1` keeps the REST projection; the client never auto-retries a write

**Decision.** Every operation under `/{tenantId}/api/web` is `POST application/json`, reads
included. Exceptions: the blob GET (so `<img src>` and `ETag`/`If-None-Match` work) and the two
`multipart/form-data` endpoints (`import`, blob `upload`). The public `v1` surface projects REST
verbs (`GET` query and get, `POST` save, `DELETE` delete) when built. MCP is one POST by protocol.

Client retry contract, written into the spec because a POST-only surface removes the verb-based
idempotency signal: the SPA and the MCP host retry **reads** (`query`, `get`, `get-by-ids`,
`get-by-parent-ids`, `export`) on network failure, 429, and 503 with `Retry-After`; they never
retry `save`, `import`, `delete*`, `activate`/`deactivate`, or actions — a lost response to a
create is surfaced to the user, who re-queries. `Idempotency-Key` is reserved for `v1` and not
implemented now.

**Rationale.** Queryex text (`select`, `filter`, `orderBy`, `arguments`) is long, quoted, and
operator-laden and does not belong in a query string. One verb gives one client helper, one CSRF
story (D10), one binding pattern for every generated endpoint. The SPA never uses HTTP caching of
query results (it caches by version tag), and output caching cannot serve authenticated responses
anyway (D15), so REST verbs buy nothing on this surface. A retried create with app-assigned ids is
a duplicate row and no client-generated key exists here to dedupe it.

**Alternatives rejected.** REST projection on the web surface (the architecture's current text;
recorded as a departure in §7). The `QUERY` method draft (not reliably supported by browser
`fetch`).

**Confidence.** High.

### D4 — Query wire: Queryex text in; a columnar `QueryRowSet` out as arrays of arrays; lossless numbers; bounded paging; count capped inside SQL

**Decision.** Request and response (camelCase JSON):

```json
{ "select": "Id,Code,Name,Parent.Name,IsActive", "filter": "IsActive and Name contains @q",
  "orderBy": "Code", "skip": 0, "take": 50, "search": "east", "arguments": { "q": "east" },
  "includeCount": true, "includeAncestors": false, "aggregate": false, "having": null }
```

```json
{ "columns": [ { "name": "Id", "type": "Numeric", "storeType": "int", "kind": "Int32", "nullable": false, "path": ["Id"], "groupingKey": false }, … ],
  "rows": [ [5, "E-01", "East region", "Regions", true] ],
  "count": 10000, "countCapped": true,
  "ancestors": [ [1, "R", "Regions", null, true] ] }
```

1. `select` is optional; the default is the entity's declared `[DefaultSelect]` or, absent that,
   the key, the natural key, and the multilingual display names. `filter`, `having`, and `orderBy`
   are single Queryex expressions; the server composes them with row-level-security criteria
   through `FilterTree`; the tree never travels on the wire. `search` is carried unchanged; the
   service-pipeline theme owns its semantics (declared searchable columns).
2. **The server-side representation is columnar.** `QueryResult.Rows` is a `QueryRowSet`: one
   typed buffer per column (`int[]`, `long[]`, `decimal[]`, `bool[]`, `string?[]`, `DateOnly[]`,
   `DateTime[]`, `DateTimeOffset[]`, `byte[][]`, `Guid[]`, hierarchy paths as `string?[]`) plus a
   null bitmap per nullable column, filled by the data-access theme's reader through typed
   `SqlDataReader` getters — no `GetValue`, no boxing. A converter in Abstractions writes the
   row-major JSON with `Utf8JsonWriter`, switching on the column kind once per column. A boxed
   `object[][]` (1,500 allocations per 50×30 page; 300,000 per 10,000-row export page) would also
   force every scalar runtime type into the source-generated context.
3. **Encoding by column kind.** Integers → JSON number; `decimal` → JSON number with its full
   scale (`1234.5000`), never exponent notation; `bool` → `true`/`false`; `string` → string;
   `DateOnly` → `"yyyy-MM-dd"`; `DateTime` (`datetime2`) → `"yyyy-MM-ddTHH:mm:ss.fffffff"` with no
   offset; `DateTimeOffset` → RFC 3339 with offset; `hierarchyid` → its string path (`"/1/3/"`);
   `varbinary` → base64; `Guid` → canonical string; SQL NULL → `null`. `columns[].storeType`
   carries the structured store type (`decimal(19,4)`, `nvarchar(255)`) from the Queryex schema and
   `columns[].kind` the CLR kind, so a generic client picks a parser per column.
4. **Lossless numbers are a client obligation.** The server writes `decimal` and `long` exactly.
   `JSON.parse` does not read them exactly, so the SPA parses `/api/web` bodies with a lossless
   parser (numbers beyond 15 significant digits materialize as decimal strings, then as the
   client's decimal type), and the MCP layer never re-parses numbers. Inbound, the server rejects a
   number whose textual form exceeds the property's precision or scale with a 422 `precision`
   error at the property path; it never rounds silently.
5. **Paging window.** `take` defaults to 50 and is clamped to `MaxTake` (10,000); `skip + take`
   above `MaxSkipWindow` (100,000) is a 400 `bad-request`. The engine must emit `skip`/`take` as
   parameter slots so every page shares one plan (data-access ask, §6 seam 1).
6. **Count is capped inside SQL, in the same round trip.** `includeCount` adds a second statement
   to the query batch: `SELECT COUNT(*) FROM (SELECT TOP (@cap + 1) 1 AS x FROM … WHERE <filter
   AND RLS>) AS c`; the response reports `count = min(n, cap)`, `countCapped = n > cap`. Never a
   second round trip, never an uncapped `COUNT(*)`.
7. **Ancestors ride the same batch** for tree entities: a third statement selects the ancestors
   (by `Node.IsDescendantOf`) of the page's matches that are not themselves in the page, returned in
   a separate array so the UI never confuses them with matches.
8. **Arguments** are JSON scalars; the server infers each declared parameter's type with
   `DiscoverQuery` and converts before `CompileQuery`; the engine's text-keyed caches make the
   second bind a cache hit, and `tellma.api.query.discover.duration` makes the cost visible.
9. Queryex diagnostics are a 400 `query-invalid` problem whose `errors` keys are the clause names
   (`select`, `filter`, `orderBy`, `having`) and whose `diagnostics[]` carries `code`, `location`,
   `start`, `length`, `arguments` verbatim for editors.

**Rationale.** Arrays of arrays are 3–5× smaller than objects and map directly onto
`CompiledQuery.Columns`; column metadata costs a few hundred bytes and lets a generic client (the
grid, an agent, a test) interpret values without knowing the select. Columnar buffers keep a
10,000-row export page in a few megabytes of managed memory. Capping the count inside SQL is what
makes "count stops at 9,999 on millions of rows" true.

**Alternatives rejected.** Row objects (bigger, slower to write). Decimals as JSON strings
(lossless under `JSON.parse`, but inconsistent with integers, breaks numeric schemas for agents,
and pushes string parsing into every consumer). A wire `FilterTree` (client complexity for
nothing). `GET` with query strings (D3). Omitting `columns` when `select` was explicit (saves
bytes, costs uniformity; always present).

**Confidence.** High on the columnar buffer and the count cap; medium on numbers-as-JSON-numbers
(§9).

### D5 — Entity envelopes: one `EntitiesResult<T>` for get, get-by-ids, save, activate, and id-shaped actions; `related` is a declared display projection in typed sets

**Decision.**

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

1. **`entities`** are whole entities in wire shape (D7): every property, child collections nested
   (`"roleMemberships": [ … ]` on a user), foreign keys as ids, enums as strings, multilingual
   columns as `name`, `name2`, `name3` with a column the tenant does not configure omitted.
2. **`related` is a projection, not a row.** Each entity declares once, with
   `[RelatedSelect("Id,Code,Name,Name2,Name3,IsActive")]` (default when absent: the key, the natural
   key, the multilingual display names, and `IsActive` when present), the columns it exposes when
   reached through a navigation. `related` holds only those columns for the entities that any
   foreign key on the main entities or their children points at, so a caller who can read the main
   entity learns of a referenced user what the UI must show — a name — and nothing the `User`
   resource's own permissions protect. The set of navigations loaded is the stack's declared
   `[DetailsExpand]` (default: every navigation on the entity and its children). `related` is keyed
   by entity name and holds an **array**, not an id-keyed object: the client indexes by `id`, the
   server writes one typed list per entity type through one `JsonTypeInfo` lookup per type per
   response, and the shape is identical for `int` and `long` keys.
3. **`extras`** is a per-service open bag (`map<string, JSON>`) selected by `include`; unknown
   names are a 400. **`rows`** is the search-page row echo, present when `select` was given, one
   `QueryRowSet` row per entity in `entities` order, so the search page updates its cached row when
   the details page loads.
4. **`ids`** is always present: input order for save; requested order for `get-by-ids`, omitting
   not-found ids. `get` returns 404 when the id is absent *or* invisible under row-level security;
   `get-by-ids` returns what is found and never 404.

**Rationale.** One envelope means the SPA writes one parser and one cache-update path, and an
agent sees one shape in `tellma_get` and `tellma_save`. The projection closes the leak named in
§1 and shrinks payloads (a `User` row is about 1 KB, its projection about 60 bytes). Typed sets
keep serialization on the metadata-mode source-generated path.

**Alternatives rejected.** A distinct `DetailsResult` with a single `entity` (a second parser for a
one-element array). Full related rows in the same wire shape (the leak; the size). Full related
rows filtered by the target's row-level security (a second permission evaluation per related type
per request, and it still leaks columns). Id-keyed `related` objects (string keys; per-row type
lookups).

**Confidence.** High. **Review flag** in §9 (`[RelatedSelect]` default).

### D6 — Save: array in, explicit concurrency mode, opaque stamp, id rules; delete-by-query verified inside the transaction

**Decision.**

```json
{ "entities": [ { "id": 0, "code": "E-02", "name": "East 2", "parentId": 5, "centerType": "Operation" } ],
  "returnEntities": true, "select": "Id,Code,Name,Parent.Name", "include": [],
  "concurrency": "check" }
```

1. **Cardinality.** `save` takes an array (the UI sends one); the response is the same
   `EntitiesResult<T>`; with `returnEntities: false` it carries `ids` and an empty `entities`
   array. `MaxEntitiesPerSave` (1,000) caps the array with a 413; larger sets go through import.
2. **Children.** A child collection that is present is synchronized (children missing from it
   are deleted); an absent collection is left untouched; an empty array deletes all children. The
   emitter's synchronize statements key on `(ParentId, Id)`, never on `Id` alone, and the pipeline
   rejects a child whose `id` belongs to another parent with 422 `child-not-owned`, so a crafted
   payload cannot move or overwrite another parent's child.
3. **Concurrency mode is explicit.** `concurrency` ∈ `check` (default) | `override`. Under
   `check`, every entity with `id > 0` must carry `modifiedAt` equal to the stored stamp: a
   missing or null stamp on an update is a 422 `concurrency-stamp-required` at
   `entities[i].modifiedAt`; a mismatch is a 409 `concurrency-conflict` listing every conflicting
   id with the stored stamp, `modifiedById`, and `modifiedByName`, so the UI can offer "user Y
   changed this record; overwrite?" and re-send with `override`. Under `override` no stamp is
   checked; import's update and merge modes use `override` explicitly because a sheet carries no
   stamps.
4. **The stamp is opaque.** The client never parses `modifiedAt`; it echoes the string it
   received (the `DateTime` encoding of D4 preserves all seven fractional digits). The server
   compares the parsed `datetime2(7)` value for equality inside the persist batch through the
   `(Id, ExpectedStamp)` guard TVP the service-pipeline theme defines. `modifiedAt` is
   `[ServerOwned]` for the emitter *and* the stamp source for the guard; the roles do not conflict
   because the guard reads the inbound value and the emitter never writes it. Should the
   data-access theme choose a different stamp column, the wire rule is unchanged and only the
   member name moves (§10).
5. **Ids.** `id` absent or `0` creates; a negative id is a 400; a duplicate id within a payload is
   a 422 `duplicate-id`.
6. **`IdsRequest`** (`delete`, `activate`, `deactivate`, `delete-with-descendants`, id-shaped
   actions): `{ "ids": [5, 6], "returnEntities": true, "select": "…", "include": [] }`, capped
   at `MaxIdsPerRequest` (10,000) with a 413.
7. **`DeleteByQueryRequest`** is `{ "filter": "…", "arguments": { }, "expectedCount": 1204 }`.
   The count is checked inside the delete transaction, so there is no window between the count the
   user saw and the rows deleted:

```sql
SET XACT_ABORT ON;
BEGIN TRAN;
DECLARE @actual int;
DELETE c FROM [gl].[Centers] AS c WHERE <compiled filter AND RLS>;
SET @actual = @@ROWCOUNT;
IF @actual <> @expected
BEGIN
    ROLLBACK;
    SELECT CAST(0 AS bit) AS Ok, @actual AS Actual;   -- the batch reads this and throws CountMismatchException (409)
    RETURN;
END
COMMIT;
SELECT CAST(1 AS bit) AS Ok, @actual AS Actual;
```

   Foreign-key restrict errors (547) on any delete map to a 422 `in-use` at `ids[i]` naming the
   referencing entity, never to a 500.

**Rationale.** Array save is the guiding principle and what import needs. An explicit mode makes
the dangerous default (no stamp, no check) unreachable by accident. Verifying the count inside the
transaction is the only version of "never delete more than the user was shown" that holds under
concurrency; `expectedCount` turns the most dangerous endpoint into a two-phase confirm without
server state.

**Alternatives rejected.** Save returning only ids (the details page needs the round-tripped
entity and the search page its row). Patch semantics ("the operation is save, not patch"). A
boolean `overrideConcurrency` (silent when the stamp is missing). A preview-then-token delete flow
on the web surface (server state or a signed token for a case `expectedCount` already covers; MCP
does use a token, D17).

**Confidence.** High. **Review flag** in §9 (`expectedCount` versus a preview token).

### D7 — The entity class is the wire shape; `[ServerOwned]` columns are excluded by the SQL emitter; strict JSON; the N−1 rules

**Decision.** There is no DTO layer. The entity class serializes with System.Text.Json under one
`JsonSerializerOptions` registered once by `AddTellma`:

- `PropertyNamingPolicy = CamelCase`; `PropertyNameCaseInsensitive = true`;
  `DefaultIgnoreCondition = WhenWritingNull`; `NumberHandling = Strict`;
  `AllowDuplicateProperties = false`; `UnmappedMemberHandling = Skip` (see the N−1 rules);
  `MaxDepth = 16` (parent → child → grandchild is depth 6; the default 64 is a stack-depth
  margin nobody needs); enums as strings through `JsonStringEnumConverter<TEnum>` (AOT-safe);
  `DateOnly`, `DateTime`, `DateTimeOffset`, `decimal`, `byte[]`, `Guid`, and `hierarchyid` as in
  D4.
- Child collections are `[NotMapped]` list properties on the parent entity, named after the
  child table (`RoleMemberships`), so the EF model keeps no parent→child navigation, Queryex has no
  collections, and the wire has both.
- **`[ServerOwned]` is enforced by the SQL emitter, not by a pipeline convention.** The
  data-access theme's emitter derives the `UPDATE … SET` list from the UDTT row image **minus**
  `[ServerOwned]` columns and the `INSERT` column list minus `[ServerOwned]` columns that are not
  `AfterCreate`; audit, tree, activation, and invitation-state columns are written only by the
  statements that own them (audit stamping, the tree recompute, `activate`). A client-sent
  server-owned value therefore cannot reach a table even if every pipeline step forgets to
  overwrite it. Platform bases mark `CreatedAt`, `CreatedById`, `ModifiedAt`, `ModifiedById`,
  `Node`, `Level`, `SubtreeCount`, `ActiveSubtreeCount`, `IsActive` (changed only through
  `activate`/`deactivate`), and `User`'s invitation state; `[ServerOwned(AfterCreate = true)]`
  marks write-once columns (`Subject`, `Email` on `User`): written on insert, never on update. A
  distribution marks its own computed columns. The wire tolerates every property on the way in — a
  details payload must round-trip into save unchanged — and the OpenAPI document and
  `tellma_describe` report the split (`readOnly`, `editable: false`) so a client never guesses.
- `[JsonIgnore]` is the "never on the wire" marker; a mapped `[JsonIgnore]` column that is not
  `[ServerOwned]` fails the startup gate (the pipeline would map a default value into the row).
- **Source generation.** `TellmaApiJsonContext` (metadata mode) covers every envelope, request,
  problem, `QueryRowSet`, `RelatedEntities`, and scalar; entity types resolve through a
  `DefaultJsonTypeInfoResolver` appended to `TypeInfoResolverChain`, so the distribution declares
  nothing. The startup gate resolves the `JsonTypeInfo` of every registered entity, child, and
  request type once, so an unserializable member fails startup rather than the first request.
  `JsonSerializerIsReflectionEnabledByDefault` stays true (a distribution is not trimmed); a
  scaffolded per-distribution context is a later optimization with no wire change.
- **N−1 rules** (zero-downtime deploys): within a platform minor, wire properties are added,
  never removed or renamed; a removal is a two-release expand/contract (mark obsolete, ship,
  remove); new required request members ship with defaults; enum values are added, never removed;
  unknown inbound members are skipped and counted (`tellma.api.unknown_members`, tag `type`) so an
  old client's extra field is visible in telemetry rather than dropped silently forever. Every
  `/api/web` response carries `Tellma-Build: <DeploymentIdentity version>`; the SPA compares it
  with its own and prompts a reload when the major or minor differ.

**Rationale.** A parallel DTO hierarchy is the single largest source of mechanical code in a CRUD
stack (three classes per entity, a mapper, and drift); the entity already carries the column
metadata the wire needs, and the one thing it lacks — the editable split — is one attribute. The
risks of one class on the wire are real and are closed where they cannot be forgotten: in the
emitted SQL, in the startup gate, and in the parser options.

**Alternatives rejected.** A `ForSave` hierarchy (already rejected by the architecture).
`[Editable]` opt-in (inverts the common case). Pipeline-level overwrite alone (a convention, not a
guarantee). A hand-listed per-distribution `JsonSerializerContext` (exactly the list an agent
forgets to update; deferred as an optimization).

**Confidence.** High. **Review flag** in §9 (reflection-resolved entities; `MaxDepth`).

### D8 — Endpoint projection: `MapTellma()` maps every endpoint from the stack catalog; `[ApiAction]` is the custom-action mechanism; the tenant group is the distribution's escape hatch

**Decision.** The distribution's entire web layer is:

*Illustration*

```csharp
builder.AddTellma<EtpharmaDbContext>(tellma => { tellma.AddCore(); tellma.AddGl(); tellma.AddMcp(); });
WebApplication app = builder.Build();
app.MapTellma();
app.Run();
```

`MapTellma()` reads `IEntityStackCatalog` (built by the service-pipeline theme in the realize
phase of `AddTellma`) and, for each stack, maps the standard operations its service capabilities
imply plus its `[ApiAction]` methods onto the tenant group, which already carries tenant
resolution, authentication, the CSRF, problem, negotiation, securable, and telemetry filters,
rate limiting, and body limits. A read-only stack yields `query`/`get`/`get-by-ids`/`export`;
an activatable service adds `activate`/`deactivate`; a tree service adds
`get-by-parent-ids`/`delete-with-descendants`.

Binding mechanics: a static generic class `StandardEndpoints<TEntity>` exposes one static method
per operation (for example `QueryAsync(QueryRequest request, IEntityService<TEntity> service,
CancellationToken cancellationToken)`, the body parameter marked from-body); the projector closes
it over each entity type and hands the delegate to `MapPost`, so the framework's request-delegate
factory sees real parameter types and attributes, OpenAPI sees real schemas, and no reflection
runs per request. `[ApiAction]` methods are mapped through a per-action generated delegate that
resolves the service from the request scope, reads the single body parameter with the platform
options, and awaits the method. The projection adds, per endpoint: `SecurableEndpointMetadata`
or `MemberOnlyEndpointMetadata` (D9), the API-endpoint marker that makes cookie challenges answer
401/403 instead of redirecting, problem metadata for 400/401/403/404/409/413/422, the request-size
metadata, and the `tellma.resource`/`tellma.operation` tags on the request activity.

A custom action is a method on the service:

*Illustration*

```csharp
[ApiAction("invite", Description = "Invite the selected users by email; safe to repeat.", Idempotent = true)]
public Task<EntitiesResult<User>> InviteAsync(IdsRequest request, CancellationToken cancellationToken) { … }
```

Rules: a public instance method on the service, returning a task of a result (or a bare task),
taking at most one body parameter plus an optional cancellation token; the route is
`POST /{tenantId}/api/web/{resource}/{name}`; the securable is `(resource, Action ?? name)` unless
`MemberOnly`; the result serializes like any envelope; the MCP `tellma_action` tool lists it
unless `Mcp = Hidden`; `Destructive = true` makes the MCP action tool require confirmation (D17).
A non-entity service (`MeService`, `SettingsService`, `NotificationsService`) carries
`[ApiRoute("me")]` on the class and projects only its actions. The attribute is the security
boundary: a public helper method never becomes an endpoint by convention.

The escape hatch, for the distribution's Web project only, is the tenant group itself:

*Illustration*

```csharp
RouteGroupBuilder tenant = app.MapTellma().TenantGroup;
tenant.MapPost("reports/aging/stream", AgingReport.StreamAsync).RequireSecurable("aging-report", "read");
```

A module package has no escape hatch beyond `[ApiAction]` (D1); it cannot reference the group
type.

The line-count probe — adding `Center` (tree, IsActive, audit) to a distribution, web and MCP
included: one sealed entity class of about twelve column lines (tree, audit, and IsActive columns
come from the bases); one empty sealed service class declaring the capability markers; zero web
lines; zero MCP lines; one attribute per custom action on a method that had to exist anyway.
Each capability is declared once (entity base plus service marker) and projects to columns,
permission actions, service methods, routes, default filters, OpenAPI, and `tellma_describe`.

**Rationale.** Zero web lines per entity; every custom action is one attribute that feeds HTTP,
OpenAPI, the securables registry, and MCP. The escape hatch *is* the tenant group, so an unusual
endpoint cannot accidentally skip tenant resolution, CSRF, or the securable audit.

**Alternatives rejected.** Controllers per entity (the boilerplate the brain dump wants gone).
Endpoint classes per stack (a file per entity for nothing). Convention-based discovery of public
methods (accidental endpoints).

**Confidence.** High on shape; medium on the closed-generic delegate mechanics (§8 names the
fallback).

### D9 — Authorization layering: the service is authoritative; the endpoint filter is an early deny; a startup audit makes an unsecured endpoint a startup failure

**Decision.**

1. **`SecurableEndpointFilter`** (tenant group) reads `SecurableEndpointMetadata(resource,
   action)` and calls `IPermissionEvaluator.TryDenyFastAsync(resource, action)` — answered from
   the permissions cache with **no database round trip**. A definite "no permission on this
   resource and action at all" ends the request with 403 `forbidden` before the body is
   deserialized. Any other answer (allowed, or cache cold) falls through; the filter never passes a
   verdict to the service.
2. **The service pipeline** evaluates the same `(resource, action)` itself inside its first batch,
   where the connect step validates the permissions tag, and composes the row-level `FilterTree`.
   This is the authoritative check and the one MCP tools and background jobs also go through,
   because they call the service. A check that exists only in an endpoint filter is a check every
   non-HTTP caller bypasses.
3. **`MemberOnlyEndpointMetadata`** endpoints (`me`, settings read, notifications, blob GET) skip
   step 1; the service's connect step still requires an active member.
4. The host sets the fallback authorization policy to "authenticated user"; every projected
   endpoint also carries `RequireAuthorization()` explicitly so the audit does not depend on the
   fallback.
5. **Startup audit**, part of the composition's aggregated validation and run host-free in tests:
   every endpoint whose route template begins with `/{tenantId:int}` must carry exactly one of the
   two metadata types, must carry the API-endpoint marker, must not carry `AllowAnonymous`, and
   its `(resource, action)` must exist in the securables registry (a typo in
   `[ApiAction(Action = "aprove")]` is a startup failure, not a silently unreachable action).
   Every endpoint outside the prefix must carry `AllowAnonymous` explicitly or be one of the BFF
   endpoints, so an accidentally tenant-less business endpoint fails too.
   `RequireSecurable(resource, action)` / `AllowMember()` are the only ways to satisfy the audit on
   hand-mapped endpoints.

Failure modes of the connect-call collapse, as web-layer obligations: a deactivated user whose
permissions cache is warm passes step 1 and is denied by the connect step in step 2 (403, the
cache entry evicted); a stale permissions tag makes the pipeline recompute and re-run the batch
once (`tellma.api.permissions.stale` counts it); the row-level pre-check on update runs in the same
batch as validation-context loading, so "pre-check before validation" costs no round trip.

**Rationale.** The fallback policy covers "authenticated", never "authorized for which
securable"; the audit closes the gap, the early deny keeps the common denial cheap, and the
metadata is what OpenAPI and `tellma_describe` read.

**Alternatives rejected.** Policy-based authorization attributes with a dynamic policy provider
(duplicates the evaluator before the connect step). Filter-only enforcement (bypassed by MCP and
jobs). A filter that hands its verdict and filter to the service through the request context (a
trust edge the service cannot verify).

**Confidence.** High.

### D10 — Credentials and CSRF on the web surface: cookie only, required client header, content-type allow-list, origin agreement against the configured public origin; no antiforgery token

**Decision.** `/{tenantId}/api/web` accepts the distribution's BFF session cookie only
(distribution-host theme). `CsrfEndpointFilter` runs first on the tenant group, before any body
read, and rejects with 403 `csrf` unless all hold:

1. `Tellma-Client` is present and its name is in the closed set (`web`, `cli`) — a custom header
   forces a CORS preflight no foreign origin can pass, since the host registers no CORS policy;
2. `Content-Type` is `application/json`, or `multipart/form-data` only on endpoints carrying
   `AcceptsMultipartMetadata` (`import`, blob `upload`) — form-encoded bodies are refused, closing
   the form-post vector;
3. when `Origin` is present it equals the configured public origin (`Tellma:Api:PublicOrigin`,
   never the `Host` header, which is attacker-influenced wherever host filtering is not pinned);
   when `Sec-Fetch-Site` is present it is `same-origin` or `none`.

The session cookie stays `SameSite=Lax`, `HttpOnly`, `Secure` (`Strict` breaks the OIDC return
trip). No antiforgery token is issued and antiforgery services are not used on this surface.
Bearer tokens are refused on `/api/web` (401 `unsupported-credential`) so a single credential type
keeps the reasoning valid; scripts use `v1` when it exists and agents use MCP. Cookie challenges
return 401/403, never redirects. The filter increments `tellma.api.requests.rejected{reason=csrf}`.

**Rationale.** BCP 212's custom-header control plus `SameSite=Lax` plus the content-type
allow-list closes the form-post and cross-site-fetch vectors without a token round trip; the .NET
10 antiforgery docs state JSON endpoints are not auto-rejected and that a header token is an
option, not a requirement. Comparing `Origin` against a configured value closes host-header
confusion. Checking before the body read makes a forged request cost one header parse.

**Alternatives rejected.** Antiforgery token in a readable cookie echoed in a header (a bootstrap
call and a second cookie for no additional protection when no CORS policy exists).
`SameSite=Strict`. Accepting bearer on the web surface.

**Confidence.** High. **Review flags** in §9 (requests with neither `Origin` nor `Sec-Fetch-Site`;
bearer-on-web for the CLI before `v1`).

### D11 — Headers and zones: `Accept-Language`, `Tellma-Time-Zone`, `Tellma-Calendar`, `Tellma-Client`; `today()` and the engine's `TimeZone` slot bind to the tenant zone; the header zone is display-only

**Decision.** Request headers (all optional except `Tellma-Client` on the cookie surface):

| Header | Value | Effect | Precedence when absent |
|---|---|---|---|
| `Accept-Language` | standard | message language and formatting culture of messages, negotiated against the distribution's shipped language catalogue — not against the tenant's content languages, which are a different axis; `-u-` extensions are stripped before negotiation | user preference → tenant primary language → `en` |
| `Tellma-Time-Zone` | IANA id (`Asia/Riyadh`) | `RequestContext.DisplayTimeZone`: formats instants in messages and exports | user preference → tenant zone |
| `Tellma-Calendar` | catalogue code from the settings theme (`gc`, `uq`, `et`) | date formatting in messages and Excel | user preference → tenant primary calendar |
| `Tellma-Client` | `<name>/<version>` | CSRF control (D10); the `tellma.client` telemetry tag (name only, never the version) | required on the cookie surface; the MCP host sets `mcp` itself |

Response headers on every `/api/web` response: `Content-Language` (the resolved language),
`Tellma-Build` (D7), `Tellma-Cache-Tags: settings=…;permissions=…;user-settings=…;securables=…`
(the version tags the connect step read on this request, set by the telemetry filter after the
handler returns and before the result executes — how the SPA revalidates its own caches without
an extra call), and `Retry-After` on 429/503.

`RequestContext` carries **two zones**. `TenantTimeZone` (from tenant settings) binds the Queryex
`Today` and `TimeZone` parameter slots for every compiled query, stored filter, and row-level
criterion; `DisplayTimeZone` (header → user → tenant) is used only to format. A user-selectable
header must not shift a security predicate, and a stored report must mean the same thing for
every reader; the Queryex spec's wording ("the current date in the tenant's zone") stands. A
client that wants "my today" passes a date argument (`@d`) computed in its own zone. There is no
today header. The negotiated values are written once into the scoped request context (§6 seam 9).

**Rationale.** No standard header carries a zone or calendar; GitHub's `Time-Zone` header is the
precedent and the precedence order copies theirs. `Tellma-*` names follow RFC 6648. Culture names
with `-u-ca-` extensions produce mixed-calendar output on .NET 10/ICU, so the calendar travels
separately.

**Alternatives rejected.** `X-Today` (client clocks drift; two sources of truth; a security input).
Request-zone `today()` with the tenant as fallback (the same security defect, softer). Cookies for
preferences (the SPA already knows them; headers are explicit).

**Confidence.** High.

### D12 — Exceptions: a closed set in Abstractions, mapped by an endpoint filter on the tenant group; RFC 9457 output with codes and localized messages; a 500 carries only a trace id

**Decision.** The service pipeline throws only the closed set of §3.3.
`TellmaProblemEndpointFilter` (outermost on the tenant group, inside the CSRF filter) catches
`TellmaProblemException`, records `tellma.api.problems{status,code}`, and returns the
`application/problem+json` result. `AddProblemDetails()` with a customizer that adds `traceId`
and `code` remains for framework-generated problems (malformed JSON, 415, 429, a 404 route miss)
and for the unhandled-500 backstop, which writes `code: "internal"`, the `traceId`, and nothing
else outside Development.

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

Example body:

```json
{ "type": "https://tellma.com/problems/validation", "title": "Validation failed", "status": 422,
  "detail": "2 errors in 1 entity.", "instance": "/17/api/web/centers/save",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01", "code": "validation",
  "errors": { "entities[0].name": ["The Name (E) field is required."],
              "entities[0].parentId": ["A center cannot be its own ancestor."] },
  "errorDetails": [ { "path": "entities[0].name", "code": "required", "arguments": { "property": "Name" } },
                    { "path": "entities[0].parentId", "code": "tree-cycle", "arguments": { } } ] }
```

Rules: `errors` keys use the JSON (camelCase) property names with index grammar
(`entities[3].roleMemberships[1].roleId`); the pipeline emits them in wire casing directly; the
framework's built-in validation (`AddValidation()`, shape-only, synchronous, PascalCase-keyed) is
not enabled on tenant endpoints. Messages are localized to the negotiated language; codes are the
closed vocabulary the localization theme owns. Unique-index violations (2601/2627) surface as 422
at the property the index covers; foreign-key restrict (547) on delete as 422 `in-use`; deadlock
(1205) and transient errors after the executor's retries as 503 `database`; a SQL timeout as 503
with `Retry-After: 5`. Problem `type` is `https://tellma.com/problems/<code>`.

**Rationale.** A closed set makes the SPA's handling total (a switch on `code`), keeps
distributions from inventing statuses, and lets the MCP layer turn the same objects into
`isError` tool results. "Messages and codes" costs nothing extra and serves the SPA (`errors`) and
agents and tests (`errorDetails`). The filter keeps mapping on the endpoint's own filter chain,
where the telemetry scope already is; the exception middleware stays a backstop.

**Alternatives rejected.** A status-code interface on arbitrary exceptions (open set). Codes only
(the client-bloat worry is right; localization is the server's job). The framework's
PascalCase-keyed validation problem (the client would need a mapper).

**Confidence.** High. **Review flag** in §9 (problem `type` URI form).

### D13 — Navigation traversal in queries: columns reachable without `read` on the target are the target's related projection

**Decision.** After `DiscoverQuery` and before `CompileQuery`, the query pipeline walks every path
in `select`, `filter`, `orderBy`, and `having`. For each path that crosses a navigation into an
entity `E` other than the root: if the caller holds `read` on `E`'s resource (with any filter),
the path is allowed; otherwise the terminal property must be in `E`'s `[RelatedSelect]` set (D5),
or the request fails with 403 `forbidden` naming `E`'s resource. Paths through weak entities
resolve to their owning top-level entity. The rule is enforced in the service (so MCP inherits
it) and documented in `tellma_describe` per navigation ("reachable: Id, Code, Name, IsActive").
The joined rows are **not** filtered by `E`'s row-level security in either case — a documented
limitation, recorded so nobody assumes otherwise.

**Rationale.** Without this rule `select: "CreatedBy.Email"` leaks any user's email to anyone who
can read anything audited, and multi-hop paths reach every table in the model. The projection is
exactly what the UI must show for a foreign key, so the common query loses nothing.

**Alternatives rejected.** Per-caller schema variants (multiplies schema caches keyed on
identity). Row-level filters on joins (the engine has no per-join predicates; a later amendment).

**Confidence.** High on the rule; medium on placement (the service pipeline owns it, §10).
**Review flag** in §9 (filtering on non-projected columns).

### D14 — Limits: one options object; rate, concurrency, size, cardinality, and time as separate mechanisms

**Decision.** `TellmaApiOptions` (`Tellma:Api`), every value a default the distribution never
sets:

| Limit | Default | Mechanism |
|---|---|---|
| JSON body | 8 MB | request-size metadata on the tenant group |
| Multipart body (import, blob upload) | 100 MB (Excel and blob themes may raise per endpoint) | per-endpoint metadata; multipart body-length option aligned |
| Entities per save / ids per request | 1,000 / 10,000 | pipeline → 413 |
| `take` / count cap / skip window | 10,000 / 10,000 / 100,000 | clamp / SQL cap / 400 |
| Queryex ceilings | `QueryexLimits.Default` | engine diagnostics → 400 |
| String lengths | `[MaxLength]` on the entity | pipeline validation → 422 |
| Requests per minute per `{tenantId}:{sub}` | 600, sliding window, 6 segments, queue 0 | policy `tellma-user`, partition key from the authenticated principal (`UseRateLimiter` after `UseAuthentication`); 429 + `Retry-After` |
| Concurrent requests per tenant per instance | 64, queue 0 | policy `tellma-tenant` (a burst on one tenant must not exhaust the instance for others; the SQL pool is per connection string, so this also bounds pool waits) |
| Anonymous endpoints per IP | 60 / minute, fixed window | policy `tellma-anonymous` (PRM, distribution-info, health, OpenAPI) |
| Concurrent exports / imports per user | 2 / 1 | concurrency limiters `tellma-export`, `tellma-import` |
| MCP tool calls per minute per user | 120 | policy `tellma-mcp` |
| Request timeout | 30 s web; 300 s export/import; 60 s MCP | request-timeout policies; 504 when nothing was written |

The batch executor receives `HttpContext.RequestAborted` on every command; on timeout SqlClient
sends an attention and the connection returns to the pool with its transaction rolled back, so a
timed-out request never pins a connection. All limiters are in-process partitions — per
instance, no shared state — and every rejection increments
`tellma.api.requests.rejected{reason}` with `reason` ∈ `csrf`, `rate_limit`, `tenant_concurrency`,
`payload_too_large`, `unsupported_media`, `unknown_client`, `unsupported_credential`.

**Confidence.** High on mechanism; the numbers are tuning defaults. **Review flag** in §9
(per-tenant concurrency value).

### D15 — Compression on for JSON over HTTPS; output caching only for anonymous tenant-independent GETs

**Decision.** Response compression with Brotli then Gzip at `Fastest`, `EnableForHttps = true`,
MIME types `application/json` and `application/problem+json` (`text/event-stream` and `.xlsx`
excluded), and a 1 KB minimum. The recorded justification: no secret is ever reflected into a
compressed body — the session cookie is `HttpOnly` and never echoed, no CSRF token exists,
`Tellma-Client` is constant, and validation messages echo user input next to nothing secret.
Output caching: five minutes on `/api/distribution-info`, the PRM documents, and
`/openapi/web.json`; never under `/{tenantId}`. Entity-level freshness stays in the settings
theme's tag-validated cache.

**Confidence.** High.

### D16 — Observability and operations: instruments, one log scope per request, the DB-call budget per operation, health, and the drain contract

**Decision.**

- Meter `Tellma.Core.AspNetCore`: `tellma.api.requests.rejected` (counter, `reason`);
  `tellma.api.problems` (counter, `status`, `code`); `tellma.api.operation.duration` (histogram,
  s; `tellma.resource`, `tellma.operation`, `tellma.client`); `tellma.api.operation.db_calls`
  (histogram, `{call}`; same tags) — read from the data-access theme's scoped `IDbCallCounter` when
  the telemetry filter completes, which is what finds an N+1 per operation on day one;
  `tellma.api.permissions.stale` (counter); `tellma.api.unknown_members` (counter, `type`);
  `tellma.api.query.discover.duration` (histogram, s). Meter `Tellma.Core.Mcp`:
  `tellma.mcp.tool.calls` (counter; `tool`, `outcome` ∈ `ok`, `error`, `denied`, `truncated`,
  `confirm`); `tellma.mcp.tool.duration` (histogram, s; `tool`); `tellma.mcp.result.chars`
  (histogram, `{char}`; `tool`). Tag values are closed sets: resource names from the catalog,
  operations from the standard segments plus declared action names, client names from the closed
  set, tool names from the seven tools. No tenant or user tags on any instrument.
- The telemetry filter opens one logging scope `{TenantId, UserId, Client, Resource, Operation}`
  (logs may carry tenant ids; metrics may not) and tags the request activity with the three
  closed-set tags. `Server-Timing: db;dur=<ms>;desc="<n> calls"` is emitted when
  `Tellma:Api:ServerTiming` is true (default: Development only).
- Drain: host shutdown timeout 25 s; the host stops accepting, in-flight requests finish, MCP
  subscription streams close. The infrastructure (host theme) must raise App Service's container
  stop limit above its 5 s default, or every deploy aborts saves mid-flight.
- Health as in D2: `/healthz` liveness without I/O; `/readyz` readiness checking the tenant
  registry source and the data-protection key ring.

**Confidence.** High.

### D17 — The Tellma Tenant MCP server ships in 0015: seven generic tools projected from the catalog; no concurrency override; deletes confirmed by elicitation or a signed preview token; result caps

**Decision.** `tellma.AddMcp()` registers `ModelContextProtocol.AspNetCore` 2.2.0 in stateless
session mode (the 2026-07-28 revision: no `initialize`, no sessions, no affinity); `MapTellma()`
maps `/{tenantId:int}/mcp` (POST; GET and DELETE answer 405) with `RequireAuthorization("TellmaMcp")`.
The tool list is static, deterministic, and identical for every caller (`ttlMs` 300,000,
`cacheScope` `public`); permissions are reported by `tellma_whoami` and re-evaluated on every
call, because hiding is not security:

| Tool | Arguments | Annotations | Result |
|---|---|---|---|
| `tellma_whoami` | none | readOnly, idempotent, closed-world | user (id, name, email), tenant (id, name, `kind: live\|sandbox`, languages, calendars, zone), a permission matrix `entity → allowed operations and actions` |
| `tellma_describe` | `entity?`, `detail: "list"\|"full"` | readOnly, idempotent | without `entity`: the catalogue (name, resource, title, description, operations, actions); with `entity`: properties (name, type, nullable, editable, maxLength, enum values, navigation target and its reachable columns, multilingual), child collections, searchable columns, natural key, default select, three example filters; per tenant (`Name2`/`Name3` gating), cached by the settings tag |
| `tellma_query` | `entity`, `select?`, `filter?`, `orderBy?`, `skip?`, `top` (default 50, max 500), `arguments?`, `includeCount?`, `format: "table"\|"objects"` | readOnly | one text block (`QueryResult` as a table, or objects); `truncated: true` plus guidance when the character cap is hit |
| `tellma_get` | `entity`, `ids` (1..100), `include?` | readOnly | `EntitiesResult` with `related` projections; entities carry `modifiedAt` |
| `tellma_save` | `entity`, `entities` (1..100) | not destructive, not idempotent | **no override argument**; `concurrency` is always `check`; a conflict returns `isError` with the stored values and the instruction to re-get and re-apply; result in concise form (ids and display names) |
| `tellma_delete` | `entity`, `ids`, `withDescendants?`, `confirmation?` | destructive | the first call returns a preview (count, display names) and a `confirmation` token; the second call with the token deletes. When the client declares `elicitation.form`, a form-mode elicitation ("Delete N record(s) of `<entity>`?") replaces the two calls |
| `tellma_action` | `entity`, `action` (`activate`, `deactivate`, or an `[ApiAction]` name), `ids?`, `input?` | per attribute (`Idempotent`, `Destructive`) | the action's result, concise; `Destructive` actions use the same confirmation as delete |

Reserved names, shipped with the Excel and background-task themes: `tellma_export`,
`tellma_import` (long-running: the tasks extension once it stabilizes, else a
`tellma_task_status` pair). Never exposed: `delete-by-query`, settings edit, actions marked
`Mcp = Hidden`, and the write operations of a stack marked `[ApiResource(Mcp = ReadOnly)]`.

The confirmation token is protected by the data-protection key ring the host theme shares across
instances, bound to `(sub, tenantId, entity, sha256(sorted ids), withDescendants, expires = now +
5 min)`, single-use per process (a bounded in-memory set; a replay on another instance within
five minutes is accepted — the operation is idempotent). The MRTR `requestState` uses the same
protector. Every tool call runs the service's connect step and permission evaluation; a handle is
a name, not a capability.

Result shaping: `MaxToolResultChars` 60,000 (Claude Code's default cap is 25,000 tokens);
`tellma_query` and `tellma_get` declare **no `outputSchema`** (declaring one obliges the server
to send `structuredContent` *and* the text block, doubling tokens); `tellma_whoami` and
`tellma_describe` declare one. Every `inputSchema` is JSON Schema 2020-12 with
`additionalProperties: false`. Input validation, validation failures, and 403/404/409 outcomes are
`isError` tool results with `errors`/`errorDetails` rendered so the model self-corrects; protocol
errors are reserved for malformed requests. Tool descriptions state that returned data is tenant
content, not instructions. The Queryex syntax reference ships as the MCP resource
`tellma://queryex/syntax`, which `tellma_describe` points at. `serverInfo.name` is
`tellma-tenant`; `server/discover` `instructions` state the tenant name and kind and the rule
"read before you write; never guess ids — query them". Distribution-authored tools
(`tellma.AddMcp(m => m.WithTools<AgingReportTools>())`, the SDK's own attributes) run under the
same bearer and must call services; a distribution tool that runs SQL directly bypasses
permissions — a residual risk the deferred bypass analyzer should cover.

**Rationale.** Under ten tools stays far below the 30–50 tool degradation threshold; entity
discovery inside `tellma_describe` is the progressive-disclosure pattern Anthropic's guidance
recommends; a static list is cacheable. The write-safety rules are the difference between an
agent that can be trusted with `save` and one that cannot: an agent offered `overrideConcurrency`
sets it, and an agent offered `confirm: true` sets it — neither is a safeguard. Underscore names
satisfy MCP, Claude, and OpenAI's `^[a-zA-Z0-9_-]{1,64}$`. Shipping now, rather than "designing
for it", is what proves the catalog carries what agents need; the marginal cost is the SDK package,
the bearer and PRM wiring, and tests, while the identity-server work (D20) needs lead time.

**Alternatives rejected.** One tool per entity per operation (context bloat). Per-permission
`tools/list` (a permission evaluation per list call; a non-cacheable list; hiding is not
security). `confirm: true` (no safeguard). Exposing override (lost updates by default). Deferring
MCP entirely (the breakdown's position; §9).

**Confidence.** Medium-high. **Review flag** in §9 (which tools ship in 0015).

### D18 — MCP topology: one endpoint per tenant; the audience and every self-referencing URL come from `Tellma:Api:PublicOrigin`

**Decision.** `/{tenantId}/mcp` is the MCP server of exactly one tenant. Its RFC 8707 resource
identifier is `{PublicOrigin}/{tenantId}/mcp` (no trailing slash); the protected-resource-metadata
document lives at `{PublicOrigin}/.well-known/oauth-protected-resource/{tenantId}/mcp` (RFC 9728
path insertion) and is populated per request through the SDK's resource-metadata request event
with `resource`, `authorization_servers = [identity issuer]`, `scopes_supported = ["tellma_api"]`,
`bearer_methods_supported = ["header"]`, `resource_name = "<tenant name> — <distribution>"`.
`Tellma:Api:PublicOrigin` is required configuration validated at startup; the same value feeds
the CSRF origin check (D10) and the BFF redirect URIs. A PRM document that echoed the `Host`
header would be a cache-poisoning and phishing primitive. The token's `aud` must equal the
resource exactly: a token whose `aud` is the distribution's API audience (`{PublicOrigin}` with
no path) is refused at `/mcp`, and an MCP-audience token is refused at `/api/v1` — audiences are
not interchangeable, so a token for tenant A is structurally invalid at tenant B. A user who works
in two tenants configures two servers; `tellma_whoami` names the tenant and its kind so a model
never confuses live with sandbox. `ISandboxContext` reads the same scoped request context as the
web surface.

**Rationale.** One code path, one audience per tenant, the smallest tool list, and a natural fit
for the PRM path-insertion rule; the MCP specification wants the path form of the resource
"when path component is necessary to identify individual MCP server", which is exactly this case.

**Alternatives rejected.** One server per distribution with a `tenant` argument on every tool
(every call carries a tenant id the model can get wrong; one audience for all tenants; a
three-dimensional permission matrix). Computing the audience from the request host.

**Confidence.** High.

### D19 — MCP resource-server authentication: JWT bearer against the issuer; scope policy; membership and activity checks per call; service accounts cannot step up

**Decision.** `AddMcp()` registers JWT bearer authentication (authority = the platform issuer;
JWKS cached; inbound claim mapping off so `sub` stays `sub`; issuer validated; the audience
validator accepts exactly the D18 resource; HTTPS metadata required outside Development; 60 s
clock skew against 10-minute tokens) as the authenticate scheme and the SDK's MCP scheme as the
challenge scheme. Policy `TellmaMcp` requires an authenticated principal whose `scope` contains
`tellma_api`. Denied requests get the SDK's 401 with `WWW-Authenticate: Bearer
resource_metadata="…"`; insufficient scope gets 403 with `error="insufficient_scope",
scope="tellma_api"`. After bearer validation the request context is populated from the principal
and route values (`Client = "mcp"`, display zone from the user's preference), and every tool call
runs the service's connect step: subject → active member, tenant not suspended; a deactivated
user is denied on the next call even though the token is still valid (signed-only JWTs cannot be
revoked; the 10-minute lifetime bounds the rest). A `client_credentials` principal (no
`auth_time`) hitting a step-up-required operation gets `HumanRequiredException` (403), never a
step-up challenge it cannot answer. The SDK's `Origin` validation uses the hosted clients'
origins as its allow-list (`https://claude.ai`, `https://chatgpt.com`; native clients send none).
No CORS policy. Rate limit 120 tool calls per minute per `{tenantId}:{sub}`; 60 s timeout per
tool call.

**Rationale.** This is the MCP specification's required shape (an OAuth 2.1 resource server, RFC
9728 metadata, audience-bound tokens); the SDK implements the metadata and challenge half; bearer
validation is ordinary ASP.NET Core. An OpenIddict validation handler would pull the OpenIddict
client stack into every distribution for no gain.

**Confidence.** High.

### D20 — Human users and autonomous agents: two flows, and what the identity server must add, in order

**Decision.** *Humans* (Claude Code, Claude.ai/Cowork, Codex, ChatGPT, Cursor) use authorization
code + PKCE S256 against the identity server with `resource=<tenant MCP URL>`. *Autonomous
agents* (an Agent SDK process, a scheduled script) use a service account: the tenant admin creates
one through the distribution (spec 0003's create-service-account API returns `client_id` and a
secret once), the agent obtains a token with `client_credentials`, `scope=tellma_api`,
`resource=<tenant MCP URL>`, and supplies it through its MCP client's static-header mechanism.
The service account is a tenant member like any user — a `User` row whose `Subject` is the
service account's `sub`, flagged as a service account (users theme, §10). Hosted Claude cannot
do machine-to-machine, so autonomous work never routes through claude.ai.

The identity server changes 0015 depends on (an amendment to spec 0003's implementation, listed
here because the lead time is on this theme's critical path):

1. **Per-tenant resources under a granted origin, path-pattern restricted.** Today `resource`
   values are validated in the identity server's own code against per-client `rsrc:` permissions
   by exact string and copied into `aud` verbatim (OpenIddict's own resource validation is
   disabled). Rule to add: a requested resource is accepted when it equals a granted distribution
   origin **or** matches `<granted origin>/{int}/mcp` exactly, and becomes `aud` verbatim — so
   `https://etpharma.app.tellma.com/17/mcp` is grantable to any client that may name
   `https://etpharma.app.tellma.com`. The pattern keeps the audience space enumerable and stops a
   client from minting tokens for paths the distribution never serves. No per-tenant
   registration, no OpenIddict 8 dynamic resources.
2. **Client ID Metadata Documents.** Advertise `client_id_metadata_document_supported: true`; for
   a URL-shaped `client_id` fetch the document with SSRF guards (HTTPS only, no private ranges,
   5 KB cap, 5 s timeout, cached per HTTP headers), validate `client_id` equality and exact
   `redirect_uris` (loopback port relaxation for native clients), accept auth method `none` or
   `private_key_jwt` only, and show the client hostname on consent. This removes the
   pre-registration step for Claude Code, hosted Claude, Codex, and ChatGPT at once. OpenIddict
   7.6.1 has no CIMD and no tracking issue.
3. **Advertise `none` in `token_endpoint_auth_methods_supported`** (hosted Claude and Codex choose
   CIMD only then).
4. **Until 2 lands: pre-registered public native clients** (`claude-code`, `codex`, `cursor`),
   native application type with port-less loopback redirect URIs, granted `tellma_api` and every
   distribution origin (the existing path that grants a new distribution's audience to platform
   clients covers it); hosted Claude via an org-entered client id in its custom-connector dialog.
5. **Keep:** PKCE S256 advertised and required, `iss` in authorization responses, refresh-token
   rotation, and `resource` on refresh requests validated against the original grant (a refresh
   must not widen `aud`).

**Rationale.** The verified client matrix: CIMD is the common path for four of five clients and
the standards-track direction; DCR is deprecated by MCP, larger to build, and needed only by
Cursor, which accepts a static client. The origin-pattern rule is a small policy change in a
factory that already exists.

**Alternatives rejected.** A distribution-wide MCP audience (`{PublicOrigin}/mcp`) with the tenant
enforced from membership (a token for one tenant replayable at another of the same distribution).
Implementing DCR. Waiting for OpenIddict 8 (§9).

**Confidence.** Medium-high.

### D21 — Naming

The runtime server for end users is the **Tellma Tenant MCP server** (package `Tellma.Core.Mcp`,
route `/{tenantId}/mcp`, `serverInfo.name = "tellma-tenant"`, `title = "<Tenant name> (Tellma)"`).
The developer tooling stays the **Tellma Developer MCP** (`dotnet tellma mcp`, `tellma-dev`;
`@tellma/core-ui-mcp`, `tellma-core-ui`). Tool names use underscores. Headers are `Tellma-*`.
Problem codes are kebab-case. "Concurrency stamp" names `ModifiedAt` in its wire role. Resource
segments are kebab-case plural; securable actions are `read`, `save`, `delete`, `activate`, plus
custom action names. **Confidence.** High.

### D22 — OpenAPI in Development as a contract fixture; the `v1` seam

**Decision.** `AddOpenApi("web")` emits `/openapi/web.json` (OpenAPI 3.1) only in Development,
with a document transformer that adds `x-tellma-securable` (resource, action), marks
`[ServerOwned]` properties `readOnly`, and lists `[RelatedSelect]` columns per navigation. Its
purpose is the coding agent building the SPA or tests, so schemas are real types (D8). The
reference distribution's test project snapshots the document; a diff fails the build unless the
snapshot is updated in the same change, and a removed or renamed member in the diff is a test
failure regardless (the N−1 rules of D7).

The public surface is a seam only: `/{tenantId}/api/v1` is reserved; the projector takes a
surface kind (`Web`, `PublicV1`) that chooses verb projection and credential type; when built it
uses `Asp.Versioning.Http` 10.2.x URL-segment versioning, bearer `tellma_api` with
`aud = {PublicOrigin}` (spec 0003's existing distribution audience), REST verbs, entities opted in
explicitly with `[ApiResource(Public = true)]`, `Idempotency-Key`, and one OpenAPI document per
version. Nothing else is built now.

**Confidence.** High.

### D23 — Tests that must exist

`Tellma.Core.AspNetCore.Tests`: the securable audit (an unsecured endpoint fails startup; an
unregistered action fails startup; an anonymous tenant endpoint fails startup); the CSRF matrix
(missing header, wrong content type, foreign origin, bearer on web); the problem-details contract
(every exception in the closed set → status, code, members; a 500 carries no message); N−1
tolerance (unknown inbound members skipped and counted; every response type deserializes into
last-minor wire types); lossless numbers (`decimal(19,4)` and `long` round-trip through the writer
bit-exactly; over-precision inbound → 422 `precision`); concurrency modes (missing stamp → 422;
mismatch → 409; override skips); `related` projection (no non-projected column ever appears);
navigation traversal (D13 denies and allows correctly); delete-by-query count mismatch rolls
back; count cap and paging window; request timeout cancels the SQL command and returns the
connection; rate-limit partitions; `Tellma-Cache-Tags` and `Tellma-Build` present.
`Tellma.Core.Mcp.Tests`: PRM document per tenant from the configured origin; audience separation
(a distribution-audience token is refused); a deactivated member is denied; delete requires a
token or elicitation; a save conflict returns `isError` with stored values; result-cap
truncation; tool-list determinism and `ttlMs`; `HumanRequiredException` for a service account on
a step-up operation.

**Confidence.** High.

---

## 3. Contracts

Contract blocks use the platform's contract notation: names are normative; shape is described,
not transcribed. Ids on the wire are JSON numbers; the request records carry them as `long` so one
shape serves `int` and `long` keys (the service converts and rejects an id outside its key's range
with a 400).

### 3.1 Wire records — `Tellma.Core.Abstractions.Api` (owned here; consumed by the pipeline, Excel, and MCP)

```contract
data QueryRequest
  Select: string?              // null selects the entity's declared default
  Filter: string?              // row predicate; row-level security is composed server-side
  Having: string?              // group predicate; only with Aggregate
  OrderBy: string?
  Skip: int = 0                // skip + take bounded by MaxSkipWindow
  Take: int?                   // default 50; clamped to MaxTake
  Search: string?              // mapped by the service onto declared searchable columns
  Arguments: map<string, JSON>?
  IncludeCount: bool = false   // capped count in the same batch
  IncludeAncestors: bool = false
  Aggregate: bool = false

enum QueryColumnKind = Int32 | Int64 | Int16 | Byte | Decimal | Double | Boolean | String | Date | DateTime | DateTimeOffset | Time | Guid | Binary | HierarchyId

record QueryColumn(Name: string, Type: string, StoreType: string?, Kind: QueryColumnKind, Nullable: bool, Path: list<string>?, GroupingKey: bool)

// Columnar rows: one typed buffer per column plus a null bitmap per nullable column; serialized as arrays of arrays by a converter in this namespace.
contract QueryRowSet
  Columns: list<QueryColumn>
  RowCount: int
  GetBuffer(column: int) -> Array          sync   // int[], long[], decimal[], bool[], string?[], DateOnly[], DateTime[], DateTimeOffset[], byte[][], Guid[]
  IsNull(column: int, row: int) -> bool    sync
  Builder(columns: list<QueryColumn>, capacity: int)   // filled by the data-access reader through typed getters, one AppendRow per row

record QueryResult(Rows: QueryRowSet, Count: int?, CountCapped: bool, Ancestors: QueryRowSet?)

record GetRequest(Id: long, Select: string?, Include: list<string>?)

data IdsRequest
  Ids: list<long>              required; capped at MaxIdsPerRequest
  ReturnEntities: bool = true
  Select: string?
  Include: list<string>?

record ParentIdsRequest(ParentIds: list<long>?, Select: string?, Filter: string?, Arguments: map<string, JSON>?)   // null or empty = roots

enum ConcurrencyMode = Check | Override

data SaveRequest<TEntity>
  Entities: list<TEntity>      required; capped at MaxEntitiesPerSave
  ReturnEntities: bool = true
  Select: string?
  Include: list<string>?
  Concurrency: ConcurrencyMode = Check

record DeleteByQueryRequest(Filter: string, Arguments: map<string, JSON>?, ExpectedCount: int)

// Related entities by entity name, each a typed list restricted to the entity's related projection; serialized as name -> array.
contract RelatedEntities
  Add<T>(entityName: string, entities: list<T>, projection: set<string>)   sync
  Sets: map<string, RelatedEntitySet>

record RelatedEntitySet(EntityType: Type, Entities: list<object>, Projection: set<string>)

data EntitiesResult<TEntity>
  Ids: list<long>              required; input order for save, requested order for get-by-ids
  Entities: list<TEntity>      required
  Related: RelatedEntities?
  Extras: map<string, JSON>?
  Rows: QueryRowSet?           // search-row echo, one row per entity, when Select was given

record AffectedResult(Count: int)
record TaskAccepted(TaskId: long)      // the 202 body for export/import handed to background work
```

### 3.2 Declaration attributes — `Tellma.Core.Abstractions.Api`

```contract
annotation [ApiResource(Resource?)]      on entity class, inherited
  Resource: string?            // kebab-case segment and securable resource id; default derived from the table name
  Description: string?         // agent- and OpenAPI-facing
  Mcp: McpExposure = Full
  Public: bool = false         // projected on the versioned public surface (seam)

annotation [DefaultSelect(Select)]       on entity class, inherited
annotation [RelatedSelect(Select)]       on entity class, inherited   // columns exposed through a navigation (related, and query paths without read on the target)
annotation [DetailsExpand(Navigations)]  on entity class, inherited   // navigations loaded into related; default: every navigation on the entity and its children

annotation [ApiAction(Name)]             on public service method
  Name: string                 // kebab-case segment; unique per service; not a standard operation name
  Action: string? = Name       // securable action; must exist in the securables registry at startup
  MemberOnly: bool = false     // any active member; no securable
  Idempotent: bool = false     // MCP idempotentHint
  Destructive: bool = false    // MCP destructiveHint; requires confirmation on MCP
  Mcp: McpExposure = Full
  Description: string?         // falls back to the method's documentation summary through the manifest

annotation [ApiRoute(Route)]             on non-entity service class   // e.g. "me", "settings"

annotation [ServerOwned(AfterCreate = false)]   on entity property, inherited
  // Excluded by the emitter from UPDATE (and from INSERT unless AfterCreate). Positioned in the entity contract namespace of the data-access theme (§10); listed here because this theme reads it.

enum McpExposure = Full | ReadOnly | Hidden
```

### 3.3 The closed exception set — `Tellma.Core.Abstractions.Api`

```contract
base TellmaProblemException            // abstract; the subclass set is closed
  Code: string                         // kebab-case; also the problem type suffix
  Detail: string?                      // localized by the thrower
  Arguments: map<string, string>       // untrusted display data for message composition

record QueryDiagnostic(Clause: string, Code: string, Start: int, Length: int, Location: string?, Arguments: map<string, string>)   // Clause ∈ select | filter | orderBy | having
record ValidationError(Path: string, Code: string, Arguments: map<string, string>)                                                // Path is wire-cased: entities[0].roleMemberships[2].roleId
record ConcurrencyConflict(Id: long, ModifiedAt: string, ModifiedById: long, ModifiedByName: string?)                             // ModifiedAt is the opaque stamp string

contract BadRequestException(detail?)                               : TellmaProblemException  // 400 bad-request
contract QueryInvalidException(diagnostics: list<QueryDiagnostic>)  : TellmaProblemException  // 400 query-invalid
contract ValidationFailedException(errors: list<ValidationError>)   : TellmaProblemException  // 422 validation
contract StepUpRequiredException(acrValues: string, maxAge: int?)   : TellmaProblemException  // 401 step-up-required
contract HumanRequiredException()                                   : TellmaProblemException  // 403 human-required
contract ForbiddenException(resource: string, action: string)       : TellmaProblemException  // 403 forbidden
contract TenantSuspendedException(readOnly: bool)                   : TellmaProblemException  // 403 tenant-suspended | tenant-read-only
contract NotFoundException(entity: string, id: long?)               : TellmaProblemException  // 404 not-found
contract ConcurrencyConflictException(conflicts: list<ConcurrencyConflict>) : TellmaProblemException  // 409 concurrency-conflict
contract CountMismatchException(expected: int, actual: int)         : TellmaProblemException  // 409 count-mismatch
contract PayloadTooLargeException(limit: string, actual: long, maximum: long) : TellmaProblemException  // 413 payload-too-large
contract DependencyUnavailableException(dependency: string, retryAfter: duration?, inner?) : TellmaProblemException  // 503 dependency-unavailable
```

### 3.4 Header and telemetry names — `Tellma.Core.Abstractions.Api`

```contract
contract TellmaHeaders                  // constants
  TimeZone  = "Tellma-Time-Zone"
  Calendar  = "Tellma-Calendar"
  Client    = "Tellma-Client"
  CacheTags = "Tellma-Cache-Tags"
  Build     = "Tellma-Build"

contract ApiTelemetryNames              // constants; meter "Tellma.Core.AspNetCore"
  RequestsRejected      = "tellma.api.requests.rejected"
  Problems              = "tellma.api.problems"
  OperationDuration     = "tellma.api.operation.duration"
  OperationDbCalls      = "tellma.api.operation.db_calls"
  PermissionsStale      = "tellma.api.permissions.stale"
  UnknownMembers        = "tellma.api.unknown_members"
  QueryDiscoverDuration = "tellma.api.query.discover.duration"
  ResourceTag = "tellma.resource"; OperationTag = "tellma.operation"; ClientTag = "tellma.client"; ReasonTag = "reason"

contract McpTelemetryNames              // constants; meter "Tellma.Core.Mcp"
  ToolCalls = "tellma.mcp.tool.calls"; ToolDuration = "tellma.mcp.tool.duration"; ResultChars = "tellma.mcp.result.chars"
  ToolTag = "tool"; OutcomeTag = "outcome"
```

### 3.5 Host surface — `Tellma.Core.AspNetCore` and `Tellma.Core.Mcp`

```contract
service MapTellma(app) -> TellmaEndpoints   sync   // extension on the web application; maps every surface, then runs the securable audit and fails startup on any finding

record TellmaEndpoints(TenantGroup: RouteGroupBuilder, DistributionGroup: RouteGroupBuilder)
  // TenantGroup: /{tenantId:int}/api/web with CSRF, problem, negotiation, securable, and telemetry filters, limits, and rate policies attached
  // DistributionGroup: endpoints outside any tenant (anonymous; output-cached where declared)

contract TellmaEndpointConventionBuilderExtensions       // extensions on endpoint convention builders
  RequireSecurable(resource: string, action: string)     sync
  AllowMember()                                          sync
  AcceptsMultipart(maxBytes: long)                       sync   // import, blob upload; the CSRF filter allows multipart there only

record SecurableEndpointMetadata(Resource: string, Action: string)
record MemberOnlyEndpointMetadata()
record AcceptsMultipartMetadata(MaxBytes: long)

data TellmaApiOptions                    // bound from "Tellma:Api"
  PublicOrigin: Uri                      required; validated at startup
  MaxJsonBodyBytes: long = 8 MB
  MaxUploadBytes: long = 100 MB
  MaxEntitiesPerSave: int = 1000
  MaxIdsPerRequest: int = 10000
  MaxTake: int = 10000
  MaxCount: int = 10000
  MaxSkipWindow: int = 100000
  RequestsPerMinutePerUser: int = 600
  ConcurrentRequestsPerTenant: int = 64
  AnonymousRequestsPerMinutePerIp: int = 60
  ConcurrentExportsPerUser: int = 2
  ConcurrentImportsPerUser: int = 1
  RequestTimeout: duration = 30 s
  LongRequestTimeout: duration = 5 min
  EnableCompression: bool = true
  ServerTiming: bool = false
  ClientNames: set<string> = { "web", "cli" }
  ProblemTypeBase: string = "https://tellma.com/problems/"

service AddMcp(tellma, configure?) -> TellmaBuilder   sync   // extension on the composition builder; registers the Tellma Tenant MCP server as a feature that Requires the stack feature

data TellmaMcpOptions                    // bound from "Tellma:Mcp"
  MaxToolResultChars: int = 60000
  DefaultTop: int = 50
  MaxTop: int = 500
  MaxIdsPerCall: int = 100
  ToolCallsPerMinutePerUser: int = 120
  ToolTimeout: duration = 60 s
  ConfirmationLifetime: duration = 5 min
  ListTtlMs: int = 300000
  AllowedOrigins: set<string> = { "https://claude.ai", "https://chatgpt.com" }   // browser-hosted MCP clients; native clients send no Origin
  ToolTypes: list<Type>                  // distribution-authored SDK tool types
```

### 3.6 Shapes this theme needs from other themes' seams

From the **service-pipeline theme** (seam 3) — the catalog the projection and MCP read, populated
in the realize phase before `MapTellma()` runs:

```contract
contract IEntityStackCatalog
  Stacks: list<EntityStackDescriptor>
  Services: list<ApiServiceDescriptor>
  FindByResource(resource: string) -> EntityStackDescriptor?     sync
  FindByEntityName(entityName: string) -> EntityStackDescriptor? sync

enum StandardOperation = Query | Get | GetByIds | GetByParentIds | Save | Delete | DeleteByQuery | DeleteWithDescendants | Activate | Deactivate | Export | ExportForImport | Import

record EntityStackDescriptor(EntityName: string, Resource: string, EntityType: Type, ServiceType: Type,
  Operations: set<StandardOperation>,            // derived from the service's capability markers
  Actions: list<ApiActionDescriptor>,
  Properties: list<EntityPropertyDescriptor>,    // name, type, nullable, editable, maxLength, enum values, navigation target, multilingual
  Children: list<EntityStackDescriptor>,         // child collections
  SearchableProperties: list<string>, NaturalKey: string?, DefaultSelect: string?, RelatedSelect: set<string>,
  Description: string?, Mcp: McpExposure, Public: bool)

record ApiActionDescriptor(Name: string, Action: string?, MemberOnly: bool, Idempotent: bool, Destructive: bool, Mcp: McpExposure, Description: string?, Method: MethodInfo, RequestType: Type?, ResultType: Type)
record ApiServiceDescriptor(Route: string, ServiceType: Type, Actions: list<ApiActionDescriptor>)

contract IEntityService<TEntity>
  Query(request: QueryRequest) -> QueryResult
  Get(request: GetRequest) -> EntitiesResult<TEntity>              // throws NotFoundException for absent or invisible
  GetByIds(request: IdsRequest) -> EntitiesResult<TEntity>         // partial
  Save(request: SaveRequest<TEntity>) -> EntitiesResult<TEntity>
  Delete(request: IdsRequest) -> AffectedResult
  DeleteByQuery(request: DeleteByQueryRequest) -> AffectedResult

contract ITreeService<TEntity> : IEntityService<TEntity>
  GetByParentIds(request: ParentIdsRequest) -> QueryResult
  DeleteWithDescendants(request: IdsRequest) -> AffectedResult

contract IActivatableService<TEntity> : IEntityService<TEntity>
  Activate(request: IdsRequest) -> EntitiesResult<TEntity>
  Deactivate(request: IdsRequest) -> EntitiesResult<TEntity>
```

From the **distribution-host theme** (seam 9) — the scoped holder both surfaces populate and every
service reads; no `AsyncLocal`; copied into job scopes by the background-task machinery:

```contract
enum TenantKind = Live | Sandbox

record RequestContext
  TenantId: int                required
  TenantKind: TenantKind       required
  TenantTimeZone: TimeZoneInfo required   // binds the Queryex today() and TimeZone slots
  DisplayTimeZone: TimeZoneInfo required  // header -> user preference -> tenant; formatting only
  Today: DateOnly              required   // in TenantTimeZone
  Subject: string?                        // 36-char GUID string; null on anonymous distribution endpoints
  UserId: long?                           // set by the connect step
  IsServiceAccount: bool = false          // client_credentials principal; cannot step up
  Culture: CultureInfo         required   // message language and formatting; extensions stripped
  CalendarCode: string         required   // gc | uq | et
  Client: string               required   // web | cli | mcp
  Principal: ClaimsPrincipal?
  Tags: map<string, string>?              // version tags read by the connect step; echoed in Tellma-Cache-Tags

contract IRequestContextAccessor
  Current: RequestContext
  Enrich(update: RequestContext -> RequestContext)   sync   // tenant middleware, then negotiation, then the connect step
```

From the **permissions theme** (seam 11):

```contract
contract IPermissionEvaluator
  TryDenyFast(resource: string, action: string) -> bool        // cache-only, no round trip; true = definitely no permission
  Evaluate(resource: string, action: string) -> PermissionDecision
  EvaluateAll() -> PermissionMatrix                            // rendered by tellma_whoami

record PermissionDecision(Allowed: bool, Filter: FilterTree?, Why: list<PermissionReason>)

contract ISecurablesRegistry
  Contains(resource: string, action: string) -> bool   sync   // the startup audit
```

From the **data-access theme** (seams 1, 14): the batch executor accepting a cancellation token
per command; `IDbCallCounter { Calls: int; Elapsed: duration }` scoped per request; the
`QueryRowSet.Builder` fill contract (typed getters, one `AppendRow` per row); `skip`/`take` as
parameter slots; the emitter's `[ServerOwned]` exclusion and `(ParentId, Id)` synchronize keys.
From the **settings theme** (seam 5): the tag names `settings`, `permissions`, `user-settings`,
`securables` and the tenant zone and calendar at tenant-resolution time. From the **blob theme**
(seam 12): `GET blobs/{id}` mapped with `AllowMember()` plus a service check; `POST blobs/upload`
with `AcceptsMultipart`. From the **Excel theme**: `ExportRequest` (a `QueryRequest` or
`IdsRequest` plus format options) and `ImportResult`. From the **background-task theme**:
`TaskAccepted` usage and the `tellma_task_status` tool.

---

## 4. Schema

This theme owns no tables. It reads `User.Subject`, `User.IsActive`, the service-account flag on
`User`, and the sibling activity/tag table (users theme); the tenant tag table (settings theme);
and the tenant catalog row's state (`Active`, `ReadOnly`, `Suspended`) and kind (host theme). Its
persistent artifacts are the `Tellma:Api` and `Tellma:Mcp` configuration sections, whose schema
is §3.5, with `PublicOrigin` required.

---

## 5. Answers

| Brain-dump question (abridged) | Answer |
|---|---|
| "How do we extract the common CRUD endpoint boilerplate into the platform Core?" | D8: `MapTellma()` projects every endpoint from the stack catalog through closed generic handlers; custom actions are `[ApiAction]`; zero web lines per entity; modules have no other endpoint mechanism. |
| "Can we use source-generated JSON serialization?" | D7: yes for every envelope, request, problem, `QueryRowSet`, `RelatedEntities`; entities resolve by reflection appended to the chain and are validated at startup; rows are written by a converter over typed buffers (D4). |
| "Should we accept an X-Today header … or the user's timezone?" | D11: neither binds `today()`. `Tellma-Time-Zone` is display-only; the engine's `today()` and `TimeZone` slots bind to the tenant zone because stored filters and row-level security must be deterministic. |
| Three surfaces `{tenantId}/api/web`, `/api/v1`, `/mcp` | D2/D3/D22: web built (POST-only), MCP built (D17–D20), `v1` reserved with verbs, versioning, audience, and `Idempotency-Key` fixed. |
| "The MCP schema is discoverable at runtime … can be changed without breaking agents (is this true?)" | Partly. Clients re-list tools (`ttlMs`), so adding tools, optional arguments, and description changes are safe. Renaming or removing a tool or a required argument breaks saved prompts, skills, and workflows that name it; tool and argument names are a compatibility surface and stay stable across minors. |
| "How would the web layer determine the status code — enumerate exceptions or an interface?" | D12: a closed set of platform exception types in Abstractions and one mapping in an endpoint filter; distributions throw platform types only; anything else is a 500 with a trace id. |
| "Messages or codes?" | D12: both — localized `errors`, machine `errorDetails`. |
| Rate limiting "generous but bounded … purely in-memory" | D14: in-process partitioned limiters per user, per tenant, per IP; size by endpoint metadata; cardinality and length by validation. |
| "One MCP server per tenant, or per distro?" | D18: per tenant at `/{tenantId}/mcp`; the audience is the tenant MCP URL from the configured origin; two tenants = two configured servers. |
| "Few tools, intent-based, well documented" | D17: seven generic tools plus two reserved; entity discovery inside `tellma_describe`; result caps; confirmation tokens; no override; underscore names. |
| MCP auth for a human via Claude Code/Codex and for autonomous agents | D19/D20: OAuth 2.1 resource server with per-tenant PRM; humans via authorization code + PKCE (CIMD or pre-registered native clients); agents via service-account `client_credentials`, which cannot step up. |
| "Should we keep the search parameter?" | Carried on the wire unchanged (D4); the pipeline theme owns its semantics (declared searchable columns; reported by `tellma_describe`); no picker-versus-page hint. |
| "5 ids requested, 4 found: 4 or 404?" | D5: `get-by-ids` returns four; `get` returns 404, identically for missing and invisible. |
| "Difficult to forget to secure an endpoint" | D9: securable metadata on every projected endpoint, `RequireSecurable`/`AllowMember` on hand-mapped ones, registry and `AllowAnonymous` checks in the startup audit, and the authoritative check inside the service. |
| Write-once columns: two UDTTs or a service rule? | D7: `[ServerOwned(AfterCreate = true)]`, excluded from the emitted `UPDATE` — a data-layer guarantee without a second UDTT. |
| "Optimistic concurrency … an override flag" | D6: `concurrency: check \| override`; the stamp is opaque; a missing stamp under `check` is a 422; the guard TVP runs inside the persist batch. |
| "Accessing a record I have no read permission on should look non-existent" | D12: 404 `not-found` with identical members; type-level absence of permission is 403, which reveals nothing row-specific. |
| "If you can read an entity you can read all related entities and extras" | D5/D13: narrowed to the related projection; full rows of the target need `read` on it. |
| "All UI endpoints exposed as POST" | D3: yes on the web surface; `v1` keeps REST. |
| "Are those the proper layer names?" | Not this theme's; `Tellma.Core.AspNetCore` is the web layer and needs no rename. |

---

## 6. Seams

1. **Batch abstraction** (data access). Every command takes the request's cancellation token;
   `skip`/`take` are parameter slots; the count-cap and ancestors statements ride the query batch;
   the delete-by-query count check is one statement with the delete; the reader fills
   `QueryRowSet.Builder` through typed getters. Contract needed: `IDbCallCounter` scoped per
   request.
2. **Entity class vs wire shape** (data access owns; consumed here). Single class, `[NotMapped]`
   child collections named after the child table, `[ServerOwned]` excluded by the emitter from
   `UPDATE`/`INSERT` lists, synchronize statements keyed on `(ParentId, Id)`, `[RelatedSelect]` on
   every entity, `MaxDepth = 16`.
3. **One capability, declared once** (service pipeline). The catalog descriptor (§3.6) is the
   *only* thing HTTP and MCP read; `Destructive` and `RelatedSelect` join it; the capability is
   declared on the entity (base class plus marker) and the service (marker), and the descriptor's
   `Operations` set is derived from the markers.
4. **Queryex schema per tenant** (data access). The projection never touches the schema; D13's
   traversal rule runs on discovery output, not on schema variants; `Name2`/`Name3` gating shows
   on the wire as omitted properties and in `tellma_describe` per tenant.
5. **Version tags** (settings). Echoed in `Tellma-Cache-Tags` after the handler returns; the tag
   names `settings`, `permissions`, `user-settings`, `securables` are fixed; the tenant zone and
   calendar must be available at tenant resolution, before any query compiles.
6. **Feature composition** (host). `AddMcp()` is a feature that `Requires` the stack feature; the
   projection is a *contribution* of the stack feature, not a separate feature; `MapTellma()` is
   the single map call and reads the realized registry. Contract needed: `TellmaBuilder` with
   `AddStack<TEntity, TService>()` and `Requires` edges; an `IEndpointContributor` (`Map(TellmaEndpoints)`)
   in `Tellma.Core.AspNetCore` for distribution code only, never for modules.
7. **Natural keys** (data access). Reported by `tellma_describe`; nothing else here.
8. **Background-task columns** (background tasks). `TaskAccepted` is the 202 body; task status is
   read through the notifications surface; `tellma_task_status` is reserved.
9. **Request context** (host). Two zones, `Today` in the tenant zone, `IsServiceAccount`, `Tags`;
   no `AsyncLocal`; tenant middleware sets tenant facts, the negotiation filter sets
   culture/calendar/zones/client, the connect step sets `UserId` and `Tags`; the MCP host populates
   the same holder from the bearer principal and route values so `ISandboxContext` and every
   service see one shape.
10. **Platform exceptions and HTTP mapping** (types: service pipeline; mapping: here). §3.3 lives
    in Abstractions so both themes and every module reference it; the mapping is D12; the pipeline
    throws `ValidationFailedException` with wire-cased paths, `NotFoundException` for row-level
    misses, and `HumanRequiredException` for service accounts on step-up operations.
11. **Permission evaluation** (permissions). `TryDenyFast` (cache-only) for the endpoint filter;
    `Evaluate` inside the service; `EvaluateAll` for the `tellma_whoami` matrix;
    `ISecurablesRegistry.Contains` for the audit.
12. **Blob staging tokens** (blobs). Tokens travel inside entity JSON as ordinary string
    properties; `upload` uses `AcceptsMultipart`; the blob GET is the web surface's only GET.
13. **Wire shapes** (owned here). §3.1 is the contract; Excel reuses `QueryRequest` and
    `IdsRequest` inside `ExportRequest`; the pipeline returns envelopes directly so the endpoint is
    a pass-through.
14. **Telemetry** (data access owns the DB budget). `tellma.api.operation.db_calls` is the
    per-operation aggregation of the data-access counter; the request activity carries
    `tellma.resource`/`tellma.operation`/`tellma.client` so the budget histogram slices by
    operation.
15. **Notification enqueue** (background tasks). Not touched.
16. **Connect-call collapse** (users/pipeline). Endpoint filters perform no database call; the
    securable filter uses cached permissions and the service's first batch validates them; a stale
    tag makes the pipeline recompute and re-run once; the web layer sees one call and the failure
    modes are D9's.
17. **Vocabulary**. Resource segments are kebab-case plural derived from the plural table name
    (`gl.Invoices` → `invoices`); securable resource id = resource segment; entity name = Queryex
    logical name; operation segments are D2's; securable actions `read`, `save`, `delete`,
    `activate` plus action names; problem codes kebab-case; headers `Tellma-*`; "Tellma Tenant MCP
    server"; "concurrency stamp".

---

## 7. Departures from ARCHITECTURE.md

1. **Verbs.** "Endpoints are generated … (read → GET, save → POST, delete → DELETE …)" becomes
   true only for the versioned public surface; the private web surface is POST-only (D3). Reason:
   Queryex text belongs in bodies, one verb gives one CSRF and one binding story, and the SPA never
   uses HTTP caching of queries.
2. **Route shape.** The brain dump's `api/{tenantId}/documents` becomes
   `/{tenantId}/api/web/{resource}/{operation}` (D2); any architecture text with the older shape
   changes with it. Reason: tenant-first serves every surface, including the RFC 9728 well-known
   path.
3. **New packages.** `Tellma.Core.AspNetCore` and `Tellma.Core.Mcp` join the Core family;
   `Tellma.Core.Webhooks` is no longer the only Core project with a framework reference (D1). The
   rule that optional Core-layer packages depend only on Abstractions gets the carve-out Queryex
   has: `Tellma.Core.AspNetCore` references `Tellma.Core` because it is the host half of Core.
4. **Two MCP servers, named.** The "MCP topology" paragraph describes developer tooling only; it
   gains the Tellma Tenant MCP server and the naming that separates the two (D21).
5. **Output caching.** The guiding principle lists "output caching" among server speed-ups; on the
   authenticated surfaces it is unsafe and is confined to anonymous tenant-independent GETs (D15).
6. **Entity-class-as-wire.** "No separate DTO model on the persistence path" extends to the wire
   path with `[NotMapped]` children and `[ServerOwned]` (D7); the parent→child ban is read as an
   EF-model rule.
7. **Related entities.** The brain dump's (not the architecture's) "if you can read an entity you
   can read all related entities" is narrowed to the related projection (D5, D13).

---

## 8. Verification

Relied on from `research/web-api-mcp.md` (verified there 2026-09-01): route groups with route
parameters and group-level conventions; endpoint filters and factories; typed results; OpenAPI
3.1 with per-request generation; `[AsParameters]`/`BindAsync`; STJ options,
`TypeInfoResolverChain`, .NET 10 `AllowDuplicateProperties` and `Strict`; built-in validation is
shape-only, synchronous, and PascalCase-keyed; problem-details services and RFC 9457; in-process
partitioned rate limiting; request-size metadata; request timeouts cancel `RequestAborted` and
default to 504; output caching never serves authenticated responses; compression defaults and the
HTTPS opt-in; antiforgery does not cover JSON endpoints and cookie auth answers 401/403 on API
endpoints; MCP 2026-07-28 statelessness, `Mcp-Method`/`Mcp-Name` headers, MRTR elicitation,
`ttlMs`/`cacheScope`, tool annotations, `isError`, and the `outputSchema` → `structuredContent`
plus text obligation; RFC 9728 path insertion, RFC 8707 `resource`, CIMD over deprecated DCR,
PKCE S256, 401/403 challenges; C# SDK 2.2.0 (`MapMcp(pattern)`, stateless mode, request filters,
`AddMcp` PRM/401, the per-request resource-metadata event, tool attribute defaults with
`Destructive` and `OpenWorld` defaulting to true); OpenIddict 7.6.1 has RFC 8707 resources,
PKCE and `iss` metadata, but no PRM, DCR, or CIMD; the client matrix (Claude Code CIMD or
pre-registered; hosted Claude CIMD only with `none` advertised and no machine-to-machine;
ChatGPT/Codex CIMD; Cursor DCR-or-static); Anthropic's tool guidance, the 30–50 tool threshold,
Claude Code's 25,000-token result cap; OpenAI's tool-name pattern; `Asp.Versioning.Http` 10.2.3.
From the briefing's digest: MERGE is out; retry is the executor's job; one connection, one
transaction, one batch; RCSI facts; `rowversion` unsuitable as a tag; the `-u-ca-` trap; no
standard zone header; App Service's 5 s drain default; the invite-API facts; the dev admin `sub`.

Verified against the repo on 2026-09-01: the identity server calls `DisableResourceValidation()`
(`src/apps/Tellma.Identity/Hosting/OpenIddictConfigurator.cs`), validates `resource` in
`TellmaPrincipalFactory.SetAudiencesAsync`, and grants new distribution audiences through
`ClientProvisioningService.GrantResourceToPlatformClientsAsync` (the basis of D20 items 1 and 4);
`Tellma.Core.Webhooks` is the only `src/core` project with a `FrameworkReference` and maps
`/api/webhooks/{key}` with `AllowAnonymous()`, `DisableAntiforgery()`, and
`WebhookEndpointMetadata` (the pattern the distribution group reuses); `ISandboxContext` exposes
`IsSandbox` only; `Directory.Packages.props` pins no `ModelContextProtocol.*`,
`Microsoft.AspNetCore.OpenApi`, or `Asp.Versioning.*`; spec 0008 §1.4/§14/§15/§17 and Appendix A
match the diagnostic shape, limits, and versioning rules used in D4 and D12; spec 0003 §6–§7,
§9.3, and §10.2 match the token, cookie, step-up, and service-account facts used in D10, D12,
D19, and D20.

Asserted without a fresh source, to be verified by the spec author before freezing:

- The framework's request-delegate factory accepts a delegate created over a closed generic static
  method and reads its parameter attributes from the delegate's method; the fallback is a
  per-stack lambda closing over the resolved service through a small generic helper.
- Chromium, Firefox, and Safari send `Origin` on every POST including same-origin `fetch` (the
  basis of the §9 flag on requests carrying neither `Origin` nor `Sec-Fetch-Site`).
- The partitioned rate limiter disposes idle partitions on its internal timer (memory bound for
  per-user keys).
- STJ writes `decimal` with its full scale and never in exponent notation.
- The SDK's `Origin` validation accepts requests with no `Origin` header and rejects unlisted
  origins through a configurable allow-list.
- Whether the SDK's default resource-metadata URI can express `{tenantId}`; otherwise the PRM
  document is mapped by hand on the distribution group and the per-request event supplies it.
- Whether the SDK's stateless mode serves every named client at launch (Claude Code v2, Codex,
  ChatGPT speak 2026-07-28; Cursor's revision is undocumented); the hybrid session mode is the
  switch, at the cost of session affinity on App Service.
- SqlClient returns a cancelled command's connection to the pool with the transaction rolled
  back rather than marking it doomed.
- A lossless JSON parser for the SPA with a compatible license.
- The 60,000-character result cap versus Claude Code's 25,000-token cap assumes about four
  characters per token for JSON; measure against real `tellma_query` output.

---

## 9. Review flags

1. **`int` tenant id in the URL** (D2) versus a slug alias from day one; the distribution-host
   theme owns tenant routing.
2. **Decimals as JSON numbers with a lossless client parser** (D4) versus decimals as JSON
   strings on the wire; the client library choice must be settled before the first grid ships.
3. **`[RelatedSelect]` defaulting to display columns** (D5) versus full related rows filtered by
   the target's row-level security (a second permission evaluation per related type per request,
   and still a column leak).
4. **`expectedCount` on delete-by-query** (D6) versus a preview-then-confirm-token flow on the
   web surface (MCP uses the token).
5. **Reflection-resolved entity serialization** (D7) versus a scaffolded per-distribution
   source-generated context (AOT/trim-safe, faster cold start); and `MaxDepth = 16`.
6. **Requests with neither `Origin` nor `Sec-Fetch-Site`** (D10): allowed on the custom-header
   control alone, versus rejected outright once the browser matrix is verified.
7. **Bearer-on-web for the Tellma CLI** before `v1` ships (D10): refused here; the alternative is
   a `cli` client name with bearer accepted on the web surface.
8. **Problem `type` URIs** (D12): resolvable `https://tellma.com/problems/<code>` versus
   `urn:tellma:problem:<code>`.
9. **Filtering on non-projected navigation columns** (D13): denied here (a filter on
   `CreatedBy.Email contains 'x'` is an oracle); the alternative allows filter and order paths and
   restricts only `select`.
10. **Per-tenant concurrency limit of 64 per instance** (D14) and the other numeric defaults.
11. **Which MCP tools ship in 0015** (D17): all seven here; alternatives are host, auth, and the
    four read tools now with the three write tools after the first agent evaluation, or the
    breakdown's "MCP left as a seam" entirely.
12. **CIMD on the platform identity server** (D20) versus waiting for OpenIddict 8 and living
    with pre-registered clients meanwhile.
13. **Mapping platform exceptions in an endpoint filter** (D12) versus an exception handler
    registered with the exception middleware; both produce the same body.

---

## 10. Conflicts

1. **Tenant routing (T1).** This theme assumes `/{tenantId:int}` first, a tenant middleware that
   reads the route value after routing and skips endpoints marked like the webhook fronting, and
   `Tellma:Api:PublicOrigin` as the one source of the public origin for CSRF, PRM, and BFF
   redirect URIs. T1 owns the id shape, the middleware, and the catalog row states
   (`Active`, `ReadOnly`, `Suspended`) that `TenantSuspendedException` reports.
2. **Request context (T1).** Two zones (`TenantTimeZone` binds the engine; `DisplayTimeZone`
   formats), `Today` in the tenant zone, `IsServiceAccount`, `Tags`, `Client`; populated in three
   stages; no `AsyncLocal`. T3 must expose the tenant zone and calendar at tenant-resolution time.
3. **`today()` binding (T2, T3).** Spec 0008's "the current date in the tenant's zone" stands;
   the Queryex host binds `Today` and `TimeZone` from `TenantTimeZone`, never from a header or a
   user preference.
4. **`[ServerOwned]` placement and enforcement (T2).** The attribute belongs in T2's entity
   contract namespace; the emitter must exclude `[ServerOwned]` columns from `UPDATE` (and from
   `INSERT` unless `AfterCreate`), key child synchronize statements on `(ParentId, Id)`, and
   accept `[NotMapped]` child collections named after the child table.
5. **Concurrency stamp (T2, T5).** The wire rule (explicit mode, opaque stamp, missing stamp →
   422, mismatch → 409, guard TVP inside the persist batch) assumes `ModifiedAt` as the stamp; if
   T2 chooses `rowversion`, only the wire member name changes.
6. **Id type on the wire (T2).** Request records carry ids as `long`; the service converts for
   `int` keys and rejects out-of-range ids with a 400.
7. **Query rows (T2).** The reader fills `QueryRowSet.Builder` through typed getters; `skip`/`take`
   are parameter slots; the count-cap and ancestors statements ride the query batch; the executor
   honours the request's cancellation token per command and exposes `IDbCallCounter`.
8. **Catalog and capability markers (T5).** `IEntityStackCatalog` and the descriptors of §3.6 are
   the only thing HTTP and MCP read; `Operations` derives from the service's capability markers;
   `[ApiAction]` on service methods is the custom-action mechanism; `[DefaultSelect]`,
   `[RelatedSelect]`, `[DetailsExpand]` are read from the entity. The pipeline returns
   `EntitiesResult<T>`/`QueryResult` directly and throws only the closed exception set with
   wire-cased validation paths.
9. **Navigation traversal rule (T5, T4).** Enforced in the query pipeline after discovery: a path
   into an entity the caller cannot `read` may terminate only in that entity's related projection;
   weak-entity paths resolve to the owner. T4's evaluator must answer `read` on arbitrary
   resources cheaply.
10. **Permission evaluation API (T4).** `TryDenyFast` (cache-only), `Evaluate`, `EvaluateAll`,
    and `ISecurablesRegistry.Contains`; securable actions `read`, `save`, `delete`, `activate`
    plus declared action names; every `[ApiAction]` action must be registered or startup fails.
11. **Service accounts as tenant members (T4, T8).** A `User` row per service account (`Subject`
    = the service account's `sub`, flagged as a service account) so MCP `client_credentials`
    callers pass the connect step; such a user cannot step up.
12. **Version tags (T3).** The tag names `settings`, `permissions`, `user-settings`, `securables`
    and their values must be on the request context after the connect step for `Tellma-Cache-Tags`.
13. **Message language versus content languages (T3).** `Accept-Language` negotiates against the
    distribution's shipped catalogue, not the tenant's content languages; the calendar travels in
    `Tellma-Calendar`, never in a culture extension.
14. **Identity server (spec 0003 implementation).** D20's five items — path-pattern resources,
    CIMD, `none` advertised, pre-registered native clients, refresh not widening `aud` — are
    prerequisites on 0015's critical path.
15. **Excel (T9) and background tasks (T10).** `ExportRequest`/`ImportResult` shapes; the 202
    `TaskAccepted` body; `tellma_export`/`tellma_import`/`tellma_task_status` are reserved tool
    names; export and import endpoints use the long request timeout and the concurrency limiters.
16. **Blobs (T7).** The blob GET is the web surface's only GET and carries `AllowMember()` plus a
    service check; `upload` is multipart with the `Tellma-Client` header; staging tokens travel
    inside entity JSON.
17. **Hosting (T1).** App Service's container stop limit must exceed the 25 s drain; the
    data-protection key ring must be shared across instances (MCP confirmation tokens and MRTR
    state depend on it); `/readyz` checks the tenant registry source and that key ring.

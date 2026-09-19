# Web API surface and the MCP seam — proposal under the distro-author-simplicity lens

Theme key `web-api-mcp`, future spec 0015. Written 2026-09-01 for a reader who has not seen the
other design files. The proposal is written so that a spec author can draft spec 0015 from it
without asking questions; every name below is intended as final.

The lens that decides ties: a distribution author is a coding agent. The measure of every choice
here is "how many lines does the distribution write, and how mechanical are they" — for the HTTP
surface and, equally, for an end user's AI agent talking to the tenant through MCP. Where this lens
conflicts with tier-2 performance or long-term maintainability, the conflict is named and the
simplicity-favouring choice is flagged for review.

---

## 1. Critique

The brain dump's web-layer thinking is short (three sections: "Dedicated API per client", "Web
Layer", "Exception handling", plus "Rate limiting" and "MCP Server") and is mostly right in
direction. The problems are gaps, one internal contradiction, and one decision that should be
reversed.

**The general design is right: three surfaces, all-POST web, MCP not 1:1 with endpoints.**
Separate surfaces for the SPA, the versioned public API, and agents are the correct shape because
the three clients have incompatible compatibility promises (none / strict / discoverable) and
incompatible ergonomics (Queryex text in bodies / REST resources / few intent-oriented tools).
Nothing below changes that.

**Contradiction: "save takes one entity" versus the guiding principle "save endpoints accept
arrays".** The brain dump's `Save` section admits a single entity while its own note says "the
part of the pipeline that is reused with Import is entirely bulk-shaped". The web endpoint must
take an array; the UI sends an array of one; the same endpoint serves multi-row grids later. A
single-entity endpoint would be a second code path for nothing.

**Reversal: MCP should not be "out of scope for now".** The brain dump's own third design goal is
"a first class MCP server that ships from day 1", and then the MCP section defers it "until we have
added enough entities to make it useful". With User, Role, and Center an admin can already do real
work through an agent (invite users, assign roles, maintain the cost-centre tree). More
importantly, under this design the MCP tools are *projections of the same service registry that
produces the HTTP endpoints* — the marginal cost of shipping them in 0015 is the SDK package, the
bearer/PRM wiring, and tests; the marginal cost of deferring them is discovering in 0020 that the
registry lacks the metadata agents need (descriptions, editable/server-owned markers, searchable
columns, natural keys) and retrofitting every entity. The auth half is the genuinely risky part and
needs identity-server work with lead time (§2 D14–D16), which is another reason to start now.

**Gap: the wire shapes are described in prose, never fixed.** "Array of arrays", "a dictionary of
related entities", "extras", "row echo" are mentioned but never given a JSON shape, and the three
surfaces would drift apart without one. §3 fixes them.

**Gap: no answer to "which properties may the client set".** The brain dump's own question about
write-once columns (`Subject`, `Email`) is answered at the data layer ("two UDTTs?") when it is a
wire-and-pipeline question: the wire tolerates every property (a details payload must round-trip
back into save unchanged), and the pipeline overwrites server-owned ones. That needs a marker the
entity declares once (`[ServerOwned]`), which no section proposes.

**Gap: the exception question conflates two things.** "Does the web layer enumerate exceptions or
do they implement an interface" — the answer is neither: a *closed set of platform exception types*
mapped by *one table* the web layer owns. An interface (`IHasStatusCode`) lets any distribution
invent status codes, which makes the SPA's error handling open-ended; enumerating framework
exceptions (`SqlException`, `JsonException`) at the edge leaks infrastructure. And "messages or
codes" is a false choice: RFC 9457 has a place for both.

**Gap: headers.** "X-Today" is the wrong primitive (a client clock is a support nightmare and the
`X-` prefix is deprecated); a time-zone header plus the server clock gives the same date and is
verifiable. Nothing says how `Accept-Language` interacts with the tenant's content languages, or
what the calendar header is called.

**Gap: CSRF is unstated.** "All the UI endpoints are exposed as POST" with a cookie session is
exactly the CSRF-exposed shape, and .NET 10's antiforgery middleware does not protect JSON
endpoints automatically. The posture must be explicit.

**Gap: nothing says how the distribution adds a custom endpoint**, only that `UserService` has
some. The mechanism decides whether the web layer is "zero lines per entity" or "a controller per
service".

**Detail choices to change.** `{tenantId}` should be an integer route constraint, not a free
segment (a slug alias can be added later without breaking anything). The MCP tool names must use
underscores, not dots: OpenAI's tool-name pattern is `^[a-zA-Z0-9_-]{1,64}$`, and ChatGPT/Codex
are named target clients, so dotted names (`tellma.entities.query`) would fail there. Rate limits
"purely in-memory per instance" is right and is what ASP.NET Core's limiter is; but the brain dump
lists "length of every free-text parameter" under rate limiting when it is payload validation —
the two are separate mechanisms (endpoint metadata for size, DataAnnotations for shape).

**Where the orchestrator's hints are adopted, refined, or rejected.** Adopted: all-POST web
surface with REST kept for `v1` (D3); save as an array (D5); single entity class with `[NotMapped]`
children plus a declared server-owned split (D6); server-side `Search` (carried unchanged, D4);
RFC 9457 with property paths (D9); one MCP endpoint per tenant (D13); a startup audit over
`EndpointDataSource` (D11). Refined: validation-error keys are camelCase JSON names, not the
framework's PascalCase CLR names (D9); the research file's suggestion of a per-permission
`tools/list` is rejected in favour of a static, deterministic tool list with permissions reported
by `tellma_describe` (D17) — simpler, cacheable, and no worse for the model. Rejected: the
breakdown's "MCP left as a seam" (D12, review flag).

---

## 2. Decisions

### D1 — Packaging: `Tellma.Core.AspNetCore` and `Tellma.Core.Mcp`; wire contracts in `Tellma.Core.Abstractions.Api`

**Decision.** Two new Core-layer projects:

| Project | Path | References | Contents |
|---|---|---|---|
| `Tellma.Core.AspNetCore` | `src/core/Tellma.Core.AspNetCore/` | `Tellma.Core`; `FrameworkReference Microsoft.AspNetCore.App` | The ASP.NET Core host integration of Core: `MapTellma()`, endpoint projection, request-context binding, CSRF filter, problem-details mapping, JSON configuration, limits, OpenAPI. Also the natural home of the host's BFF endpoints and tenant middleware (owned by the distribution-host theme). |
| `Tellma.Core.Mcp` | `src/core/Tellma.Core.Mcp/` | `Tellma.Core.AspNetCore`; `ModelContextProtocol.AspNetCore` 2.2.0 | The tenant MCP server: tools, PRM, bearer challenge, result shaping. |

Wire contracts — request/response records, the closed exception set, header names, telemetry
names, `ApiActionAttribute`, `ServerOwnedAttribute` — live in `Tellma.Core.Abstractions` under
`Tellma.Core.Abstractions.Api` (BCL only), so a module package can return a platform envelope from
a custom action and the SPA's TypeScript types mirror one source.

Test projects: `test/core/Tellma.Core.AspNetCore.Tests` (TestServer over a fixture composition
with the LocalDB fixture entities of the data-access theme; no `Category=Integration`, the host
is in-process) and `test/core/Tellma.Core.Mcp.Tests` (the SDK's in-memory client against the same
fixture host).

**Rationale.** `Tellma.Core` must stay free of a framework reference (the migrator references the
Web project, but packs reference only Abstractions and tests reference Core without a web host).
The `.AspNetCore` suffix follows `Microsoft.EntityFrameworkCore.SqlServer` /
`ModelContextProtocol.AspNetCore` / `OpenIddict.Server.AspNetCore`. MCP is separate so a
distribution that must not ship it (an air-gapped on-prem customer with no agent tooling) does not
carry the SDK, while the template scaffolds `AddMcp()` in by default.

**Alternatives rejected.** Folding the web layer into `Tellma.Core` (drags ASP.NET Core into every
consumer of Core). Naming it `Tellma.Core.Web` (ambiguous with `Tellma.Identity.Web`, which is a
deployable). One package for web and MCP (forces the SDK on every distribution).

**Confidence.** High. **Review flag.** None.

### D2 — Routes: tenant first, `int` constrained, kebab-case plural resource segments, verb-led operation segments

**Decision.** Every tenant-scoped route starts with the tenant id:

```
/{tenantId:int}/api/web/{resource}/{operation}          the private SPA surface (built in 0015)
/{tenantId:int}/api/v1/…                                the versioned public surface (reserved)
/{tenantId:int}/mcp                                     the tenant MCP server (built in 0015)
/.well-known/oauth-protected-resource/{tenantId:int}/mcp   the MCP protected-resource metadata (GET)
/api/distribution-info                                  distribution contract surface (host theme)
/api/webhooks/{key}                                     spec 0007, unchanged
/openapi/web.json                                       Development environment only
```

`{resource}` is the entity's **resource name**: the lowercase kebab-case form of the table name
(`core.Users` → `users`, `gl.Centers` → `centers`, `gl.InvoiceLines` → `invoice-lines`), derived
once by the stack registry from the EF table name and overridable with `[ApiResource("...")]` on
the entity class. The same string is the securable resource id (permissions theme), the OpenAPI
tag, and the Excel sheet default name. The Queryex logical name (`Center`) is the *entity name*,
used in JSON (`related` dictionary keys) and MCP arguments.

`{operation}` is one of the thirteen standard segments or a custom action name:

| Segment | Standard operation | Securable action | Body | Result |
|---|---|---|---|---|
| `query` | Query | `read` | `QueryRequest` | `QueryResult` |
| `get` | Get (details, one id) | `read` | `GetRequest` | `EntitiesResult<T>` (404 when absent or invisible) |
| `get-by-ids` | GetByIds (partial: found rows only) | `read` | `IdsRequest` | `EntitiesResult<T>` |
| `get-by-parent-ids` | GetByParentIds (tree) | `read` | `ParentIdsRequest` | `QueryResult` |
| `save` | Save (array; upsert + synchronize children) | `save` | `SaveRequest<T>` | `EntitiesResult<T>` |
| `delete` | DeleteByIds | `delete` | `IdsRequest` | `AffectedResult` |
| `delete-by-query` | DeleteByQuery (advanced menu; never on MCP) | `delete` | `DeleteByQueryRequest` | `AffectedResult` |
| `delete-with-descendants` | DeleteWithDescendants (tree) | `delete` | `IdsRequest` | `AffectedResult` |
| `activate` / `deactivate` | Activate / Deactivate (IsActive capability) | `activate` | `IdsRequest` | `EntitiesResult<T>` |
| `export` / `export-for-import` | Excel export (query or ids) | `read` | `ExportRequest` | `.xlsx` file, or 202 `TaskAccepted` |
| `import` | Excel import | `save` | `multipart/form-data` | `ImportResult`, or 202 `TaskAccepted` |
| `{action}` | `[ApiAction]` on the service (D7) | the attribute's action (default = name) | the method's request type | the method's return type |

The SPA is served with an `index.html` fallback that excludes `/api`, `/mcp`, `/.well-known`, and
`/openapi`, so a tenant-prefixed deep link (`/17/centers/5`) and a tenant-prefixed API never
collide: the API always has the `api`/`mcp` segment second.

**Rationale.** Tenant-first makes every surface — including the well-known PRM path, which RFC 9728
forms by inserting `/.well-known/oauth-protected-resource` between host and path — read the same
way, and lets one route group carry tenant resolution for all of them. `int` is stable, short, and
free of the reserved-word problems a slug carries; a slug alias (`/etpharma-live/api/web/…`) can be
mapped onto the same group later without breaking clients. Deriving the resource segment from the
table name means the distribution declares nothing for the common case.

**Alternatives rejected.** `/api/web/{tenantId}/…` (splits surfaces from the tenant prefix; the PRM
path for MCP becomes odd). Singular resource segments (`/center/query`) — the table is plural per
the architecture document (`gl.Invoices`), and one derivation rule beats two. Free-form `{tenantId}`
(no constraint) — invites ambiguous matches with the SPA's own routes.

**Confidence.** High on shape; medium on `int` versus slug (the distribution-host theme owns tenant
routing). **Review flag.** `int` id versus slug in the URL.

### D3 — Verbs: the web surface is POST-only (blob GETs excepted); `v1` keeps the REST projection

**Decision.** Every operation on `/{tenantId}/api/web` is `POST` with a JSON body, including reads.
Exceptions are the blob-retrieval endpoint of the blob theme (`GET /{tenantId}/api/web/blobs/{id}`,
so `<img src>` and browser caching with `ETag`/`If-None-Match` work) and the Excel `import`/blob
`upload` endpoints, which are `POST multipart/form-data`. The public `v1` surface, when built,
projects REST verbs (`GET` query and get, `POST` save, `DELETE` delete). MCP is one `POST` by
protocol.

**Rationale.** Queryex text (`select`, `filter`, `orderBy`, `arguments`) does not belong in a query
string: it is long, contains operators and quotes, and hits URL length limits on real filters.
One verb means one client helper (`api.post(resource, operation, body)`), one CSRF story (D8), one
binding pattern for every generated endpoint, and no debate per operation. The SPA never needs HTTP
caching of query results (it caches by version tag), and output caching cannot serve authenticated
responses anyway (research §1.13), so the REST verbs buy nothing on this surface.

**Alternatives rejected.** REST projection on the web surface (per the architecture document):
query strings for Queryex are the deal-breaker, and mixed verbs mean mixed CSRF handling.
`QUERY` method (RFC draft): not supported by browsers' `fetch` reliably.

**Confidence.** High. **Departure** from the architecture document, recorded in §7.

### D4 — Query wire shape: Queryex text in, arrays of arrays out, with column metadata and a capped count

**Decision.** `QueryRequest` and `QueryResult` (all JSON camelCase; C# in §3):

```json
// POST /17/api/web/centers/query
{
  "select": "Id,Code,Name,Parent.Name,IsActive",
  "filter": "IsActive and Name contains @q",
  "orderBy": "Code",
  "skip": 0,
  "take": 50,
  "search": "east",
  "arguments": { "q": "east" },
  "includeCount": true,
  "includeAncestors": false,
  "aggregate": false,
  "having": null
}
```

```json
{
  "columns": [
    { "name": "Id", "type": "Numeric", "nullable": false, "path": ["Id"], "groupingKey": false },
    { "name": "Parent.Name", "type": "String", "nullable": true, "path": ["Parent", "Name"], "groupingKey": false }
  ],
  "rows": [ [5, "E-01", "East region", "Regions", true] ],
  "count": 10000,
  "countCapped": true,
  "ancestors": [ [1, "R", "Regions", null, true] ]
}
```

Rules:

- `select` is optional; the default is the stack's declared default select (one attribute on the
  entity, `[DefaultSelect("Code,Name")]`, or the first few display columns when absent). `filter`,
  `having`, and `orderBy` are single Queryex expressions (strings); the server composes them with
  row-level-security criteria through `FilterTree` — the tree never travels on the wire.
- `arguments` values are JSON scalars; the server infers each declared parameter's type with
  `DiscoverQuery` (spec 0008 §2.4) and converts. `search` is the server-side search term (the
  service pipeline theme decides its semantics; the wire just carries it).
- `take` defaults to 50 and is capped at `MaxTake` (default 10,000); `count` stops at `MaxCount`
  (default 10,000): the response reports `count: 10000, countCapped: true`.
- `rows` are arrays of scalars in `columns` order. Encoding by `QueryexType`: `Numeric` → JSON
  number (decimals with full scale, never exponent notation); `Bool` → `true`/`false`; `String` →
  string; `Date` → `"yyyy-MM-dd"`; `DateTime` → `"yyyy-MM-ddTHH:mm:ss.fffffff"` without offset;
  `DateTimeOffset` → RFC 3339 with offset; `HierarchyId` → its string path (`"/1/3/"`); binary →
  base64; absent → `null`. The writer is a hand-written `Utf8JsonWriter` emitter switched on the
  column type (no boxing into `object[][]`, which would defeat source generation).
- `ancestors` is present only when `includeAncestors` was true on a tree entity: the ancestor rows
  (same columns) that were not themselves matches, so the UI can render results in place.
- Queryex diagnostics are a 400 problem (D9) whose `errors` keys are the clause names (`select`,
  `filter`, `orderBy`, `having`) and whose `diagnostics` extension carries the engine's
  `code`/`location`/`span`/`arguments` verbatim for editors.

**Rationale.** Arrays of arrays are 3–5× smaller than objects for grid pages and map directly onto
`CompiledQuery.Columns`. Column metadata costs a few hundred bytes and lets a generic client (the
grid, an agent, a test) interpret values without knowing the select in advance. A single filter
string is what a user types and what an agent writes; a wire `FilterTree` would force every client
to build trees for no benefit.

**Alternatives rejected.** Row objects (bigger, slower to write). A wire `FilterTree` (needless
client complexity; the research file's polymorphism concern disappears when the tree is
server-only). `GET` with query strings (D3).

**Confidence.** High. **Review flag.** Whether `columns` should be omitted when the client passed
an explicit `select` (saves bytes; costs uniformity — this proposal keeps it always).

### D5 — Entity envelopes: one `EntitiesResult<T>` for get, get-by-ids, save, and activate; save takes an array

**Decision.**

```json
// POST /17/api/web/centers/get
{ "id": 5, "select": "Id,Code,Name,Parent.Name", "include": ["history"] }
```

```json
{
  "ids": [5],
  "entities": [ { "id": 5, "code": "E-01", "name": "East region", "parentId": 1, "centerType": "Operation",
                  "isActive": true, "createdAt": "2026-08-01T09:12:44.1234567+03:00", "createdById": 1,
                  "modifiedAt": "2026-08-30T15:02:10.0000000+03:00", "modifiedById": 3,
                  "node": "/1/2/", "subtreeCount": 4, "activeSubtreeCount": 3 } ],
  "related": { "Center": { "1": { "id": 1, "code": "R", "name": "Regions", "…": "…" } },
               "User":   { "1": { "id": 1, "name": "Admin" }, "3": { "id": 3, "name": "Sara" } } },
  "extras": { "history": [ "…" ] },
  "rows": [ [5, "E-01", "East region", "Regions"] ]
}
```

- `entities` carries whole entities in wire shape (D6): every property, child collections nested
  (`"roleMemberships": [ … ]` on a user), foreign keys as ids, enums as strings, multilingual
  columns as `name`, `name2`, `name3` (gated by tenant languages — a column the tenant does not use
  is omitted).
- `related` is keyed by **entity name** then by id (string keys — JSON objects have string keys) and
  holds the entities that any foreign key on the main entities or their children points at, in the
  *same* wire shape but without their own children; the set of navigations loaded is the stack's
  declared `[DetailsExpand]` (defaults to every navigation on the entity and its children).
  Related entities are not filtered by the caller's permissions: "if you can read an entity you can
  read what it points at" (the brain dump's rule).
- `extras` is a per-service open bag (`Dictionary<string, JsonElement>` on the wire; typed on the
  server by the service) selected with `include`; unknown names are a 400.
- `rows` is the search-page row echo: present when `select` was given, one row per entity in
  `entities` order.
- `ids` is always present (input order for save; requested order for get-by-ids, omitting
  not-found ids). `get` returns 404 when the id is absent *or* invisible under row-level security;
  `get-by-ids` returns what is found (a partial result), never 404.

Save:

```json
// POST /17/api/web/centers/save
{
  "entities": [ { "id": 0, "code": "E-02", "name": "East 2", "parentId": 5, "centerType": "Operation", "isActive": true } ],
  "returnEntities": true,
  "select": "Id,Code,Name,Parent.Name",
  "include": [],
  "overrideConcurrency": false
}
```

Response: the same `EntitiesResult<T>`; with `returnEntities: false` it carries `ids` and an empty
`entities` array. `id: 0` (or absent) creates; a positive id updates; a child collection that is
present is synchronized (missing children are deleted), an absent child collection is left
untouched, an empty array deletes all children. `modifiedAt` on an updated entity is the
optimistic-concurrency stamp the client last saw; a mismatch is a 409 (D10) unless
`overrideConcurrency` is true. `MaxEntitiesPerSave` (default 1,000) caps the array; larger sets
go through import.

`IdsRequest` (`delete`, `activate`, `deactivate`, `delete-with-descendants`, custom id actions):
`{ "ids": [5, 6], "returnEntities": true, "select": "…", "include": [] }` — capped at
`MaxIdsPerRequest` (default 10,000). `DeleteByQueryRequest`:
`{ "filter": "…", "arguments": { }, "expectedCount": 1204 }` — the server refuses (409,
`tellma:count-mismatch`) when the current count differs from `expectedCount`, so the advanced
menu can never delete more than the user was shown.

**Rationale.** One envelope means the SPA writes one parser and one cache-update path
(`entities` + `rows`), and an agent sees one shape in `tellma_get` and `tellma_save`. Array save
is the guiding principle and what import already needs. `expectedCount` is one field that turns
the most dangerous endpoint into a two-phase confirm without a session.

**Alternatives rejected.** A distinct `DetailsResult` with a single `entity` (a second parser for a
one-element array). Save returning only ids (the details page needs the round-tripped entity and
the search page its row). Patch semantics (the brain dump: "the operation is save not patch").

**Confidence.** High. **Review flag.** `expectedCount` on delete-by-query (alternative: a two-call
"preview then confirm token" flow).

### D6 — Entity class is the wire shape; `[ServerOwned]` marks what the pipeline overwrites; JSON is camelCase with STJ

**Decision.** There is no DTO layer. The entity class serializes directly with System.Text.Json
under these rules, applied by the platform's `JsonSerializerOptions` (registered once by
`AddTellma`):

- `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`, `PropertyNameCaseInsensitive = true`,
  `DefaultIgnoreCondition = WhenWritingNull` on responses, `NumberHandling = Strict`,
  `AllowDuplicateProperties = false`, unknown members skipped (an N−1 SPA must keep working
  through a deploy), enums as strings via `JsonStringEnumConverter`, `DateOnly` as `yyyy-MM-dd`,
  `DateTimeOffset` as RFC 3339, `decimal` as number, `byte[]` as base64, `HierarchyId` (when the
  EF package's type is used) through a platform converter to its string path.
- Child collections are `[NotMapped] List<TChild>` properties on the parent entity (the EF model
  keeps no parent→child navigation; Queryex has no collections; the wire has both).
- `[ServerOwned]` (in `Tellma.Core.Abstractions.Api`) marks properties the client may send but the
  pipeline ignores on save: the platform base classes already mark `CreatedAt`, `CreatedById`,
  `ModifiedAt`, `ModifiedById`, `Node`, `SubtreeCount`, `ActiveSubtreeCount`, `IsActive` (changed
  only through `activate`/`deactivate`), and the invitation state on `User`; a distribution marks
  its own computed columns. Write-once columns (`Subject`, `Email` on `User`) carry
  `[ServerOwned(AfterCreate = true)]`: settable on create, ignored on update. The pipeline theme
  implements the overwrite; this theme owns the attribute because the OpenAPI document and
  `tellma_describe` report it (`readOnly: true` / `"editable": false`) so a client never guesses.
- `[JsonIgnore]` is the "never on the wire" marker (none of the three first entities need it).
- Source generation: `TellmaApiJsonContext` (source-generated, metadata mode) covers every envelope,
  request, problem type, and scalar; entity types resolve through a `DefaultJsonTypeInfoResolver`
  appended to `TypeInfoResolverChain`, so the distribution declares nothing. Startup creates the
  `JsonTypeInfo` of every registered entity once, so an unserializable member fails the startup
  gate rather than the first request.

**Rationale.** A parallel DTO hierarchy is the single largest source of mechanical code in a CRUD
stack (three classes per entity, a mapper, and drift). The entity class already carries the column
metadata the wire needs; what it lacks is the editable split, which one attribute supplies. The
risks the hint names are real and are answered in the pipeline: client-sent server-owned values
are overwritten from the database (never trusted), write-once columns are frozen after create, and
`modifiedAt` is deliberately *not* ignored on the way in because it is the concurrency stamp.

**Alternatives rejected.** A `ForSave` hierarchy (rejected by the architecture document already).
A per-distribution `JsonSerializerContext` listing every entity (one attribute per entity — cheap,
but it is exactly the kind of list an agent forgets to update; it can be added later as an
optimization with no wire change). `[Editable]` opt-in instead of `[ServerOwned]` opt-out
(inverts the common case: most columns are editable).

**Confidence.** High. **Review flag.** Reflection-based entity serialization (this proposal)
versus a scaffolded per-distribution source-generated context (AOT/trim-safe, faster cold start).

### D7 — Endpoint projection: `MapTellma()` emits every endpoint from the stack catalog; custom actions are `[ApiAction]` methods; the escape hatch is the tenant group itself

**Decision.** The distribution's entire web layer is:

```csharp
// Program.cs of Tellma.Distro.Etpharma.Web (the reference distribution's shape)
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddTellma<EtpharmaDbContext>(tellma =>
{
    tellma.AddCore();     // users, roles, settings, me, notifications
    tellma.AddGl();       // centers
    tellma.AddMcp();      // the tenant MCP server (D12)
});

WebApplication app = builder.Build();
app.MapTellma();          // every surface: /{tenantId}/api/web/**, /{tenantId}/mcp, /.well-known/**, /api/distribution-info, /api/webhooks/{key}, the BFF endpoints, the SPA fallback
app.Run();
```

`MapTellma()` reads `IEntityStackCatalog` (the registry the service-pipeline theme builds during
the realize phase of `AddTellma`) and, for each stack, maps the standard operations its service
supports plus its `[ApiAction]` methods, onto the tenant group that already carries tenant
resolution, authentication, the CSRF filter, request-context negotiation, rate limiting, body
limits, and the securable filter. Which standard operations exist is decided by the capabilities
the service declares (the service theme's "one capability, declared once"): a read-only stack
yields `query`/`get`/`get-by-ids`/`export`; `IActivatable` adds `activate`/`deactivate`; a tree
adds `get-by-parent-ids`/`delete-with-descendants`.

A custom action is a method on the service:

```csharp
public sealed class UserService(EntityServiceContext<User> context) : EntityService<User>(context), IActivatable
{
    /// <summary>Sends invitations for the given users through the identity server.</summary>
    [ApiAction("invite", Description = "Invite the selected users by email; safe to repeat.", Idempotent = true)]
    public Task<EntitiesResult<User>> InviteAsync(IdsRequest request, CancellationToken cancellationToken) { … }

    /// <summary>Updates the calling user's own preferences.</summary>
    [ApiAction("my-preferences", MemberOnly = true, Mcp = McpExposure.Hidden)]
    public Task<UserPreferences> SaveMyPreferencesAsync(UserPreferences request, CancellationToken cancellationToken) { … }
}
```

Projection rules for `[ApiAction]`: the method is public, returns `Task<T>` (or `Task`), takes at
most one body parameter (the first parameter that is not `CancellationToken`) and optionally a
`CancellationToken`; the route is `POST /{tenantId}/api/web/{resource}/{name}`; the securable is
`(resource, Action ?? name)` unless `MemberOnly`; the result serializes like any envelope; the
MCP `tellma_action` tool lists it unless `Mcp = Hidden`. A non-entity service (`MeService`,
`SettingsService`, `NotificationsService`) is registered with `[ApiRoute("me")]` on the class and
projects only its `[ApiAction]` methods. The projection binds handlers once per stack by closing
a generic handler class over `TEntity` (`typeof(StandardHandlers<>).MakeGenericType(entityType)`)
and mapping the resulting delegates with `MapPost`, so `RequestDelegateFactory` performs ordinary
typed binding and the OpenAPI document sees real types.

The escape hatch, for the genuinely unusual endpoint (server-sent events, a bespoke content type):

```csharp
RouteGroupBuilder tenant = app.MapTellma().TenantGroup;      // carries every convention above
tenant.MapPost("reports/aging/stream", AgingReport.StreamAsync)
      .RequireSecurable("aging-report", "read");            // or .AllowMember(); mandatory (D11)
```

**Rationale.** Zero web lines per entity, one attribute per custom action, and the same attribute
feeds HTTP, OpenAPI, and MCP. An agent adding a capability edits one file (the service). The escape
hatch keeps the conventions on (it *is* the tenant group) so an unusual endpoint cannot accidentally
skip tenant resolution, CSRF, or the securable audit.

**Alternatives rejected.** Controllers per entity (the boilerplate the brain dump wants gone).
Endpoint classes per stack (`CenterEndpoints : ICarterModule`-style — a file per entity for
nothing). Convention-based method discovery without an attribute (public helper methods would
become endpoints by accident; the attribute is the security boundary).

**Confidence.** High. **Review flag.** None.

### D8 — Authentication and CSRF on the web surface: cookie only, required client header, origin check, no antiforgery token

**Decision.** `/{tenantId}/api/web` accepts the distribution's BFF session cookie only (the
distribution-host theme owns the cookie and the OIDC handler). Every request under the group passes
`CsrfEndpointFilter`, which rejects with 403 `tellma:csrf` unless all of:

1. the request carries `Tellma-Client` (D9 headers) — a custom header forces a CORS preflight that
   no foreign origin can pass, since the host registers no CORS policy;
2. `Content-Type` is `application/json` (or `multipart/form-data` on the endpoints that declare it:
   `import`, blob `upload`) — form-encoded bodies are refused, closing the form-post vector;
3. `Origin`, when present, equals the request's own origin, and `Sec-Fetch-Site`, when present, is
   `same-origin` or `none`.

The session cookie stays `SameSite=Lax` (spec 0003 §7.1; `Strict` breaks the post-login redirect
chain). No antiforgery token is issued and `AddAntiforgery` is not used on this surface. Cookie
challenges return 401/403 (every projected endpoint carries `IApiEndpointMetadata`, which the
`MapPost` family adds automatically; the escape hatch group adds it explicitly). Bearer tokens are
*not* accepted on the web surface: scripts use `v1` when it exists and agents use MCP — a single
credential type per surface keeps the CSRF reasoning valid (a bearer request would have to bypass
the cookie-oriented checks).

**Rationale.** RFC 10017 (BCP 212) makes the custom header the MUST-equivalent control and
`SameSite=Strict` a SHOULD; the .NET 10 antiforgery docs state JSON endpoints are not auto-rejected
and that a header-based token is an option, not a requirement. Three cheap checks give
defence-in-depth without a token round trip, and the header doubles as the telemetry client tag.

**Alternatives rejected.** Antiforgery token in a readable cookie echoed in a header (works, but
adds a `GET /antiforgery/token` bootstrap call and a second cookie for no additional protection
given no CORS). `SameSite=Strict` (breaks the OIDC return trip). Accepting bearer on the web
surface (muddles the posture; `v1` exists for that).

**Confidence.** High. **Review flag.** Bearer-on-web for the Tellma CLI before `v1` ships.

### D9 — Headers and negotiation: `Accept-Language`, `Tellma-Time-Zone`, `Tellma-Calendar`, `Tellma-Client`; `today()` binds to the request's effective zone

**Decision.** Request headers (all optional except `Tellma-Client` on the cookie surface):

| Header | Value | Used for | Precedence when absent |
|---|---|---|---|
| `Accept-Language` | standard | UI/message language and formatting culture of messages; negotiated against the distribution's shipped language catalogue (not against the tenant's content languages — the two are different axes) | user preference → tenant primary language → `en` |
| `Tellma-Time-Zone` | IANA id (`Asia/Riyadh`) | binds `today()`; formats instants in messages and exports | user preference → tenant time zone |
| `Tellma-Calendar` | calendar code from the settings theme's catalogue (`gc`, `uq`, `et`) | date formatting in messages and Excel | user preference → tenant primary calendar |
| `Tellma-Client` | `<name>/<version>` from a closed name set (`web`, `mcp`, `cli`) | CSRF control (D8); the `tellma.client` telemetry tag (name only, never the version) | required on the cookie surface; set by the MCP host itself |

Response headers on every `/api/web` response: `Content-Language` (the resolved language),
`Tellma-Cache-Tags: settings=<tag>;permissions=<tag>;user-settings=<tag>;securables=<tag>` — the
version tags the connect step already read on this request (the settings/cache theme owns which
tags exist; this header is how the SPA revalidates its own caches without an extra call), and
`Retry-After` on 429/503.

There is no "today" header: the server clock in the request's effective time zone *is* today, and
the SPA renders dates in the same zone it sent. `-u-ca-` culture extensions are stripped before
negotiation (they produce mixed-calendar output on .NET 10/ICU). The negotiated values are written
once into the scoped request context (§6, seam 9) and are what the Queryex host binds into the
`Today`/`TimeZone`/`UserId` parameter slots.

**Rationale.** No standard header carries time zone or calendar (research §5.2); GitHub's
`Time-Zone` header is the precedent and the precedence order copies theirs. Non-`X-` names follow
RFC 6648. A constant client header is the cheapest CSRF control and gives a bounded telemetry
dimension for free.

**Alternatives rejected.** `X-Today` (client clocks drift; two sources of truth for "today").
Culture names with `-u-ca-`/`-u-tz-` extensions (a trap). Cookies for preferences (the SPA already
knows them; headers are explicit and cache-friendly).

**Confidence.** High on headers; medium on `today()` semantics. **Review flag.** `today()` binding
to the *request's* zone (header → user → tenant) versus spec 0008's wording "the current date in the
tenant's zone" — this proposal treats the tenant zone as the fallback, not the rule, and the
data-access theme should amend the engine documentation accordingly.

### D10 — Exceptions: a closed set of platform exception types in Abstractions, one mapping table in the web layer, RFC 9457 output with both messages and codes

**Decision.** The service pipeline throws only types from the closed set below (declared in
`Tellma.Core.Abstractions.Api`, C# in §3); `TellmaProblemHandler : IExceptionHandler` maps them.
Anything else is a 500 with a `traceId` and no detail outside Development.

| Exception | Status | `type` suffix / `code` | Extra members |
|---|---|---|---|
| `BadRequestException` | 400 | `bad-request` | — |
| `QueryInvalidException : BadRequestException` | 400 | `query-invalid` | `errors` keyed by clause; `diagnostics[]` (Queryex code, location, span, arguments) |
| `ValidationFailedException` | 422 | `validation` | `errors` (path → localized messages); `errorDetails[]` (path, code, arguments) |
| `StepUpRequiredException` | 401 | `step-up-required` | `WWW-Authenticate: Bearer error="insufficient_user_authentication", acr_values="…", max_age=…` |
| `ForbiddenException` | 403 | `forbidden` | `resource`, `action` |
| `TenantSuspendedException` | 403 | `tenant-suspended` or `tenant-read-only` | — |
| `NotFoundException` | 404 | `not-found` | `entity`, `id` (row-level-security misses use this too) |
| `ConcurrencyConflictException` | 409 | `concurrency-conflict` | `conflicts[]` (id, modifiedAt, modifiedById, modifiedByName) |
| `CountMismatchException` | 409 | `count-mismatch` | `expected`, `actual` |
| `PayloadTooLargeException` | 413 | `payload-too-large` | `limit`, `actual` |
| `DependencyUnavailableException` | 503 | `dependency-unavailable` | `dependency` (`identity`, `blobs`, `database`), `Retry-After` |

Every response is `application/problem+json`:

```json
{
  "type": "https://tellma.com/problems/validation",
  "title": "Validation failed",
  "status": 422,
  "detail": "2 errors in 1 entity.",
  "instance": "/17/api/web/centers/save",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "code": "validation",
  "errors": {
    "entities[0].name": ["The Name (E) field is required."],
    "entities[0].parentId": ["A center cannot be its own ancestor."]
  },
  "errorDetails": [
    { "path": "entities[0].name", "code": "required", "arguments": { "property": "Name" } },
    { "path": "entities[0].parentId", "code": "tree-cycle", "arguments": { } }
  ]
}
```

Rules: `errors` keys use the JSON (camelCase) property names and the ASP.NET/FluentValidation
index grammar (`entities[3].roleMemberships[1].roleId`); the pipeline theme owns the paths and
emits them in wire casing directly (the framework validator's PascalCase keys are not used —
`AddValidation()` is not enabled on save endpoints; the pipeline validates in bulk). Messages are
localized to the negotiated language; codes are the closed vocabulary the localization theme owns.
Framework-generated problems (malformed JSON, unsupported media type, 429, 404 route miss) pass
through `AddProblemDetails()` with `CustomizeProblemDetails` adding `traceId` and `code`.
Unique-index violations (2601/2627) surface as `ValidationFailedException` from the pipeline, never
as 500.

**Rationale.** A closed set makes the SPA's error handling total (a `switch` on `code`), keeps
distributions from inventing statuses, and lets the MCP layer turn the same objects into
`isError` tool results. "Messages and codes" costs nothing extra and serves both the SPA (shows
`errors`) and agents/tests (read `errorDetails`).

**Alternatives rejected.** An `IHttpProblem` interface on arbitrary exceptions (open set).
Codes-only (the brain dump's worry about client bloat is right; localizing on the server is the
platform's job). `HttpValidationProblemDetails`' PascalCase keys (the client would need a mapper).

**Confidence.** High. **Review flag.** Problem `type` URIs — resolvable `https://tellma.com/problems/<code>`
(this proposal) versus `urn:tellma:problem:<code>`.

### D11 — Securable metadata plus a startup audit make an unsecured endpoint a startup failure

**Decision.** Every projected endpoint carries `SecurableEndpointMetadata(resource, action)`;
`MemberOnlyEndpointMetadata` marks endpoints any active tenant member may call (`me`, settings
read, notifications). `SecurableEndpointFilter` (on the tenant group) evaluates the securable through
the permissions theme's `IPermissionEvaluator` and throws `ForbiddenException` when the caller
holds no permission on `(resource, action)` at all; the row-level filter it returns is attached to
the request context for the service. The host sets `FallbackPolicy = RequireAuthenticatedUser()`.
`MapTellma()` ends with an audit over `EndpointDataSource`: any endpoint whose route starts with
`/{tenantId}` that lacks exactly one of the two metadata types, or that carries `AllowAnonymous`,
is reported through the composition's aggregated startup validation and the host does not start.
The same audit runs host-free in tests. `RequireSecurable(...)`/`AllowMember()` are the only ways
to satisfy it on hand-mapped endpoints.

**Rationale.** ASP.NET Core's fallback policy covers "authenticated", never "authorized for which
securable"; the audit closes the gap, and metadata is what OpenAPI and `tellma_describe` read.

**Alternatives rejected.** `IAuthorizationRequirementData` attributes with a dynamic policy
provider (works, but pushes the securable check before the pipeline's connect step and duplicates
the evaluator; the endpoint filter can be ordered after tenant resolution and share the same scoped
evaluator the service uses).

**Confidence.** High. **Review flag.** None.

### D12 — Ship the tenant MCP server in 0015: host, auth, and seven generic tools projected from the same catalog

**Decision.** `tellma.AddMcp()` registers `ModelContextProtocol.AspNetCore` 2.2.0 in
`HttpServerSessionMode.Stateless` and `MapTellma()` maps `/{tenantId:int}/mcp` (POST; GET/DELETE
answer 405) with `RequireAuthorization("TellmaMcp")`. The tool set is generic over
`IEntityStackCatalog` — no distribution code — and is deterministic and identical for every caller
(`ttlMs` 300 000, `cacheScope: "public"`):

| Tool | Arguments | Annotations | Result |
|---|---|---|---|
| `tellma_whoami` | none | readOnly, idempotent, closed-world | user (id, name, email), tenant (id, name, `kind: live|sandbox`, languages, calendars, time zone), a permission matrix `entity → allowed operations/actions` |
| `tellma_describe` | `entity?`, `detail: "list"|"full"` | readOnly, idempotent | without `entity`: the catalogue (name, resource, title, description, operations, actions); with `entity`: properties (name, type, nullable, editable, maxLength, enum values, navigation target, multilingual), child collections, searchable columns, natural key, default select, three example filters |
| `tellma_query` | `entity`, `select?`, `filter?`, `orderBy?`, `skip?`, `top` (default 50, max 500), `arguments?`, `includeCount?`, `format: "table"|"objects"` | readOnly | `structuredContent` = `QueryResult` (table) or objects; `truncated: true` plus guidance when the character cap hit |
| `tellma_get` | `entity`, `ids` (1..100), `include?` | readOnly | `EntitiesResult` (entities with children, `related`, `extras`) |
| `tellma_save` | `entity`, `entities` (1..100), `overrideConcurrency?` | not destructive, not idempotent | `EntitiesResult` in concise form (ids and display names) |
| `tellma_delete` | `entity`, `ids`, `withDescendants?`, `confirm?` | **destructive** | requires a form-mode elicitation "Delete N record(s) of `<entity>`?" when the client declares `elicitation.form`, else `confirm: true`; `AffectedResult` |
| `tellma_action` | `entity`, `action` (`activate`, `deactivate`, or an `[ApiAction]` name), `ids?`, `input?` | per action attribute (`Idempotent`) | the action's result, concise |

Reserved names, shipped with the Excel and background-task themes: `tellma_export`,
`tellma_import` (long-running → the tasks extension or a `tellma_task_status` pair). Never
exposed: `delete-by-query`, settings edit, and any action marked `Mcp = McpExposure.Hidden`; a
stack marked `[ApiResource(Mcp = McpExposure.ReadOnly)]` hides its `save`/`delete`/actions. Every
tool's `inputSchema` is JSON Schema 2020-12 with `additionalProperties: false`; input validation
errors, validation failures, 403/404/409 outcomes are returned as `isError: true` text with the
problem's `errors`/`errorDetails` rendered so the model self-corrects (never as JSON-RPC protocol
errors). Result caps: `MaxToolResultChars` default 60 000 (below Claude Code's 25 000-token cap);
`tellma_query` also caps rows at `top`. The Queryex syntax reference ships as an MCP resource
`tellma://queryex/syntax` that `tellma_describe` points at. `serverInfo.name` is `tellma-tenant`;
`server/discover` `instructions` state the tenant name, kind, and the rule "read before you write;
never guess ids — query them". Distribution-authored tools are an escape hatch:
`tellma.AddMcp(m => m.WithTools<AgingReportTools>())` using the SDK's `[McpServerTool]` directly.

**Rationale.** Ten tools or fewer stay far under the 30–50 tool degradation threshold; entity
discovery inside `tellma_describe` is the progressive-disclosure pattern Anthropic's guidance
recommends; a static list is cacheable and honest (permissions are in `tellma_whoami`, and every
call re-checks). Underscore names satisfy MCP, Claude, and OpenAI's `^[a-zA-Z0-9_-]{1,64}$`.

**Alternatives rejected.** One tool per entity per operation (context bloat; the brain dump rejects
it). Per-permission `tools/list` (legal but adds a permission evaluation per list call and a
non-cacheable list for little gain). Deferring MCP to a later spec (the critique in §1).

**Confidence.** Medium-high on shape; medium on shipping in 0015. **Review flag.** Ship in 0015
(this proposal) versus host-and-auth only in 0015 with tools in a later spec versus entirely later
(the breakdown's position).

### D13 — MCP topology: one endpoint per tenant; the RFC 8707 resource is the tenant MCP URL

**Decision.** `/{tenantId}/mcp` is the MCP server of exactly one tenant. Its canonical resource
identifier is `https://<slug>.app.tellma.com/{tenantId}/mcp` (no trailing slash); the
protected-resource-metadata document is served at
`https://<slug>.app.tellma.com/.well-known/oauth-protected-resource/{tenantId}/mcp` by the SDK's
`AddMcp` handler, populated per request through `McpAuthenticationEvents.OnResourceMetadataRequest`
(verified: `Func<ResourceMetadataRequestContext, Task>`, "set the ResourceMetadata property to
provide the appropriate metadata for the current request") with `resource`, `authorization_servers`
= `[identity issuer]`, `scopes_supported = ["tellma_api"]`, `bearer_methods_supported = ["header"]`,
`resource_name = "<tenant name> — <distribution>"`. A user who works in two tenants configures two
servers; `tellma_whoami` names the tenant and its kind so a model never confuses live with sandbox.
`ISandboxContext` reads the same scoped request context as the web surface.

**Rationale.** One code path, one audience per tenant (a token for tenant A is structurally invalid
at tenant B), the smallest tool list, and a natural fit for the PRM path-insertion rule.

**Alternatives rejected.** One server per distribution with a `tenant` argument on every tool (every
call carries a tenant id the model can get wrong; one audience for all tenants; the permission
matrix becomes three-dimensional).

**Confidence.** High. **Review flag.** None (the hint is adopted as is).

### D14 — MCP resource-server auth: JWT bearer against the platform issuer with a per-request audience

**Decision.** `AddMcp()` registers `JwtBearer` (authority = the identity issuer; JWKS cached; the
tokens are signed-only JWTs per spec 0003 §6) as the authenticate scheme and
`McpAuthenticationDefaults.AuthenticationScheme` as the challenge scheme. `TokenValidationParameters.AudienceValidator`
accepts exactly `https://{host}/{tenantId}/mcp` computed from the request's route values; `scope`
must contain `tellma_api`. Denied requests get the SDK's 401 with
`WWW-Authenticate: Bearer resource_metadata="…"`; insufficient scope gets 403 with
`error="insufficient_scope", scope="tellma_api"`. After bearer validation the same tenant filter as
the web surface runs (subject → active tenant member, tenant not suspended); a non-member gets a
403 problem rendered as a tool error. `Origin` is validated by the SDK (403 on mismatch); no CORS
policy exists. Rate limit: 120 tool calls per minute per `{tenantId}:{sub}`; request timeout 60 s
per tool call.

**Rationale.** This is the MCP specification's required shape (OAuth 2.1 resource server, RFC 9728
metadata, audience-bound tokens) and the SDK implements the metadata/challenge half; the bearer
half is ordinary ASP.NET Core.

**Alternatives rejected.** OpenIddict validation handler (works, but pulls the OpenIddict client
stack into every distribution for no gain over `JwtBearer`). Cookie auth for MCP (agents have no
browser session).

**Confidence.** High.

### D15 — Human users and autonomous agents: two flows, and what the identity server must add

**Decision.** *Humans* (Claude Code, Claude Cowork/claude.ai, Codex, ChatGPT, Cursor) use
authorization code + PKCE S256 against the identity server, sending `resource=<tenant MCP URL>`.
*Autonomous agents* (an Agent SDK process, a scheduled script) use a service account: the tenant
admin creates one through the distribution (spec 0003 §10.2), the server returns `client_id` and a
secret, the agent obtains a token with `client_credentials`, `scope=tellma_api`,
`resource=<tenant MCP URL>`, and supplies it through its MCP client's static-header mechanism;
the service account is a tenant member like any user (a `User` row whose `Subject` is the
service account's `sub`, owned by the users theme). Hosted Claude cannot do machine-to-machine, so
autonomous work never routes through claude.ai.

The identity server changes this requires (a small amendment to spec 0003's implementation, owned
by the identity work but listed here because 0015 depends on it), in priority order:

1. **Accept per-tenant resources under a granted origin.** Today `resource` values are validated
   against per-client `rsrc:` permissions by exact string (`DisableResourceValidation()` plus the
   custom check in `TellmaPrincipalFactory.SetAudiencesAsync`). Rule to add: a requested resource
   whose origin equals a granted distribution origin is accepted and becomes `aud` verbatim — so
   `https://etpharma.app.tellma.com/17/mcp` is grantable to any client that may name
   `https://etpharma.app.tellma.com`. No per-tenant registration, no OpenIddict 8 dynamic resources.
2. **Client ID Metadata Documents.** Advertise `client_id_metadata_document_supported: true`, and
   for a URL-shaped `client_id` fetch (SSRF-guarded, size-capped, cached per HTTP headers) and
   validate the document, matching `redirect_uris` exactly (loopback port relaxation for native
   clients). This removes the pre-registration step for Claude Code, hosted Claude, Codex, and
   ChatGPT at once. OpenIddict 7.6.1 has no CIMD; no issue exists on 2026-09-01.
3. **`token_endpoint_auth_methods_supported` must include `none`** (hosted Claude and Codex choose
   CIMD only then), which is true for public clients already but must be advertised.
4. **Pre-registered public native clients as the fallback** until CIMD lands: one per vendor
   (`claude-code`, `codex`, `cursor`), `ApplicationTypes.Native`, port-less loopback redirect URIs,
   granted `tellma_api` and every distribution origin (the existing "grant a new distribution's
   audience to the platform clients" path, `GrantResourceToPlatformClientsAsync`, covers it); hosted
   Claude via an org-entered client id in the custom-connector dialog.
5. Already true and to keep: PKCE S256 advertised, `iss` in authorization responses, refresh-token
   rotation, discovery answering within seconds.

**Rationale.** Verified client matrix (research §5.4): CIMD is the common path for four of five
clients and is the standards-track direction; DCR is deprecated by MCP. The origin-prefix rule is a
one-line policy change in a factory that already exists, versus a registration per tenant.

**Alternatives rejected.** Distribution-wide MCP audience (`https://<slug>.app.tellma.com/mcp`)
with tenant enforced from membership (a token for one tenant would be replayable at another tenant
of the same distribution; the MCP spec wants the path form "when path component is necessary to
identify individual MCP server", which is exactly this case). Implementing DCR (larger, deprecated,
only Cursor needs it, and Cursor accepts a static client).

**Confidence.** Medium-high. **Review flag.** CIMD in the platform identity server (this proposal)
versus waiting for OpenIddict 8.

### D16 — Naming the two MCP servers

**Decision.** The runtime server for end users is the **Tellma Tenant MCP server** (package
`Tellma.Core.Mcp`, route `/{tenantId}/mcp`, `serverInfo.name = "tellma-tenant"`, `title =
"<Tenant name> (Tellma)"`). The developer tooling stays the **Tellma Developer MCP** (`dotnet
tellma mcp` umbrella and `@tellma/core-ui-mcp`, names `tellma-dev` / `tellma-core-ui`). The
architecture document's "MCP topology" paragraph gains a sentence naming both.

**Confidence.** High.

### D17 — Limits: payload, cardinality, rate, and time, as one options object with defaults

**Decision.** `TellmaApiOptions` (bound from `Tellma:Api`), every value a default the distribution
never sets:

| Limit | Default | Mechanism |
|---|---|---|
| JSON body | 8 MB | `RequestSizeLimitAttribute` metadata on the tenant group |
| Multipart body (import, blob upload) | 100 MB (the Excel and blob themes may raise per endpoint) | per-endpoint metadata |
| Entities per save | 1 000 | pipeline check → 413 |
| Ids per request | 10 000 | 413 |
| `take` / count cap | 10 000 / 10 000 | clamp / capped count |
| Expression length, tokens, joins, slots | Queryex `QueryexLimits.Default` (8 192 chars per expression, …) | engine diagnostics → 400 |
| String lengths | `[MaxLength]` on the entity | pipeline validation → 422 |
| Requests per minute per `{tenantId}:{sub}` | 600, sliding window (6 segments), no queue | `AddRateLimiter` partitioned policy `tellma-user`; 429 with `Retry-After` |
| Anonymous endpoints (PRM, distribution-info, OpenAPI) per IP | 60 / minute fixed window | policy `tellma-anonymous` |
| Concurrent exports / imports per user | 2 / 1 | concurrency limiters `tellma-export`, `tellma-import` |
| MCP tool calls per minute per user | 120 | policy `tellma-mcp` |
| Request timeout | 30 s web; 300 s export/import; 60 s MCP | `AddRequestTimeouts` policies; the batch executor honours `RequestAborted` |

All limiters are `System.Threading.RateLimiting` in-process partitions — per instance, no shared
state, exactly the brain dump's requirement. Every rejection increments
`tellma.api.requests.rejected` with a bounded `reason` tag.

**Confidence.** High on mechanism; the numbers are defaults to tune. **Review flag.** None.

### D18 — Compression on, output caching only for anonymous tenant-independent GETs

**Decision.** `AddResponseCompression` with Brotli then Gzip, `EnableForHttps = true`, MIME types
`application/json`, `application/problem+json`, `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`
excluded (already compressed), level `Fastest`, and a 1 KB minimum. The justification the spec must
record: no secret is ever reflected into a compressed body — the session cookie is `HttpOnly` and
never echoed, there is no CSRF token, and `Tellma-Client` is a constant. Output caching:
`CacheOutput` 5 minutes on `/api/distribution-info`, the PRM documents, and `/openapi/web.json`;
never on `/api/web` or `/mcp`. Entity-level freshness stays in the settings/cache theme's tag cache.

**Confidence.** High.

### D19 — OpenAPI for the web surface in Development; the `v1` seam

**Decision.** `AddOpenApi("web")` emits `/openapi/web.json` (OpenAPI 3.1) only when
`IsDevelopment()`, with a document transformer that adds `x-tellma-securable` (resource, action)
and marks `[ServerOwned]` properties `readOnly`. Its purpose is the coding agent building the SPA
or tests, so schemas are real types (the projection maps typed delegates, D7).

The public surface is a seam only: the prefix `/{tenantId}/api/v1` is reserved, the projector
takes a `SurfaceKind` (`Web`, `PublicV1`) that chooses verb projection and credential type, and
when built it uses `Asp.Versioning.Http` 10.2.x URL-segment versioning, bearer `tellma_api` with
`aud = https://<slug>.app.tellma.com` (spec 0003's existing audience), REST verbs, entities opted
in explicitly with `[ApiResource(Public = true)]`, and `Asp.Versioning.OpenApi` for one document per
version. Nothing else is built now.

**Confidence.** High.

### D20 — Telemetry names owned by this theme

**Decision.** Constants in `Tellma.Core.Abstractions.Api.ApiTelemetryNames` and `McpTelemetryNames`:

- Meter `Tellma.Core.AspNetCore`: `tellma.api.requests.rejected` (counter, `{request}`; tag
  `reason` ∈ `csrf`, `rate_limit`, `payload_too_large`, `unsupported_media`, `unknown_client`,
  `unsecured`); `tellma.api.problems` (counter, `{problem}`; tags `status`, `code`);
  `tellma.api.operation.duration` (histogram, `s`; tags `tellma.resource`, `tellma.operation`,
  `tellma.client` — all closed sets: resource names come from the catalog, operations from the
  thirteen segments plus declared action names). The request `Activity` gets the same three tags.
- Meter `Tellma.Core.Mcp`: `tellma.mcp.tool.calls` (counter; tags `tool`, `outcome` ∈ `ok`,
  `error`, `denied`, `truncated`); `tellma.mcp.tool.duration` (histogram, `s`; tag `tool`);
  `tellma.mcp.result.chars` (histogram, `{char}`; tag `tool`).

No tenant or user tags anywhere (repo rule); the structured log carries them.

**Confidence.** High.

### D21 — The distro line-count probe (the lens's own test)

Adding `Center` (IsActive + tree) to a distribution, web and MCP included:

```csharp
// Entities/Center.cs — 1 class, ~12 lines of columns (the tree, audit, and IsActive columns come from the bases)
[TableType]
[ApiResource(Description = "Cost and profit centres, arranged as a tree.")]
[DefaultSelect("Code,Name,CenterType,IsActive")]
public sealed class Center : TreeEntity, IActivatable, IAudited
{
    [Required, MaxLength(50)]  public string Code { get; set; } = null!;
    [Required, MaxLength(255)] public string Name { get; set; } = null!;
    [MaxLength(255)]           public string? Name2 { get; set; }
    [MaxLength(255)]           public string? Name3 { get; set; }
    [Required]                 public CenterType CenterType { get; set; }
}

// Services/CenterService.cs — 1 class, 0 members
public sealed class CenterService(EntityServiceContext<Center> context) : EntityService<Center>(context), IActivatableService, ITreeService;

// Program.cs — unchanged (AddGl() registers the stack; a distro-only entity adds one line: tellma.AddStack<Center, CenterService>())
```

Web endpoints written: **0**. MCP tools written: **0**. Custom action: **1 attribute** on a method
that had to exist anyway. Capabilities declared: IsActive and tree, each **once** (the base class
plus marker interface), projected to columns, permission actions, service methods, routes, default
filters, OpenAPI, and `tellma_describe`.

---

## 3. Contracts

Compilable-looking C#; XML docs abbreviated to summaries. Namespaces are final.

### 3.1 Wire records — `Tellma.Core.Abstractions.Api` (owned here; consumed by the pipeline, Excel, and MCP)

```csharp
namespace Tellma.Core.Abstractions.Api;

/// <summary>A search-page or report query over one entity, in Queryex text.</summary>
public sealed record QueryRequest
{
    /// <summary>The select list; null selects the stack's declared default.</summary>
    public string? Select { get; init; }
    /// <summary>The row-level predicate; null is unrestricted (row-level security still applies).</summary>
    public string? Filter { get; init; }
    /// <summary>The group-level predicate; only with <see cref="Aggregate"/>.</summary>
    public string? Having { get; init; }
    /// <summary>The ordering list with direction suffixes.</summary>
    public string? OrderBy { get; init; }
    /// <summary>Rows to skip. Requires an ordering.</summary>
    public int Skip { get; init; }
    /// <summary>Rows to return; clamped to the configured maximum.</summary>
    public int? Take { get; init; }
    /// <summary>The free-text search term the service maps onto its declared searchable columns.</summary>
    public string? Search { get; init; }
    /// <summary>Declared-parameter values; types are inferred from the expressions.</summary>
    public IReadOnlyDictionary<string, JsonElement>? Arguments { get; init; }
    /// <summary>Whether to return the paging-independent count, capped.</summary>
    public bool IncludeCount { get; init; }
    /// <summary>Tree entities only: return the ancestors of matched rows that did not match.</summary>
    public bool IncludeAncestors { get; init; }
    /// <summary>True for a grouped query.</summary>
    public bool Aggregate { get; init; }
}

/// <summary>One result column, mirroring the compiled query's column.</summary>
public sealed record QueryColumn(string Name, string Type, bool Nullable, IReadOnlyList<string>? Path, bool GroupingKey);

/// <summary>Query rows as arrays of scalars in column order.</summary>
public sealed record QueryResult
{
    public required IReadOnlyList<QueryColumn> Columns { get; init; }
    public required IReadOnlyList<IReadOnlyList<object?>> Rows { get; init; }
    public int? Count { get; init; }
    public bool CountCapped { get; init; }
    public IReadOnlyList<IReadOnlyList<object?>>? Ancestors { get; init; }
}

/// <summary>Details request for one entity.</summary>
public sealed record GetRequest(int Id, string? Select = null, IReadOnlyList<string>? Include = null);

/// <summary>A request over a set of ids (get-by-ids, delete, activate, deactivate, id-shaped actions).</summary>
public sealed record IdsRequest
{
    public required IReadOnlyList<int> Ids { get; init; }
    public bool ReturnEntities { get; init; } = true;
    public string? Select { get; init; }
    public IReadOnlyList<string>? Include { get; init; }
}

/// <summary>Tree view request: the children of the given parents (null or empty = roots).</summary>
public sealed record ParentIdsRequest(IReadOnlyList<int>? ParentIds, string? Select = null, string? Filter = null,
    IReadOnlyDictionary<string, JsonElement>? Arguments = null);

/// <summary>Whole-entity save of one or more entities with their child collections.</summary>
public sealed record SaveRequest<TEntity>
{
    public required IReadOnlyList<TEntity> Entities { get; init; }
    public bool ReturnEntities { get; init; } = true;
    public string? Select { get; init; }
    public IReadOnlyList<string>? Include { get; init; }
    /// <summary>Accept the caller's version over a newer stored version (409 otherwise).</summary>
    public bool OverrideConcurrency { get; init; }
}

/// <summary>Delete every row matching a filter; refused unless the count still equals <see cref="ExpectedCount"/>.</summary>
public sealed record DeleteByQueryRequest(string Filter, IReadOnlyDictionary<string, JsonElement>? Arguments, int ExpectedCount);

/// <summary>Entities in wire shape with the related entities they point at, per-service extras, and search-row echoes.</summary>
public sealed record EntitiesResult<TEntity>
{
    public required IReadOnlyList<int> Ids { get; init; }
    public required IReadOnlyList<TEntity> Entities { get; init; }
    /// <summary>Entity name → id (as string) → entity without children.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, object>>? Related { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Extras { get; init; }
    public IReadOnlyList<IReadOnlyList<object?>>? Rows { get; init; }
}

/// <summary>The row count an operation affected.</summary>
public sealed record AffectedResult(int Count);

/// <summary>A long operation accepted for background execution (Excel and background-task themes).</summary>
public sealed record TaskAccepted(long TaskId);
```

### 3.2 Declaration attributes — `Tellma.Core.Abstractions.Api`

```csharp
namespace Tellma.Core.Abstractions.Api;

/// <summary>Overrides or annotates an entity's API identity. Absent, the resource name derives from the table name.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class ApiResourceAttribute(string? resource = null) : Attribute
{
    /// <summary>The kebab-case resource segment and securable resource id, e.g. "centers".</summary>
    public string? Resource { get; } = resource;
    /// <summary>Agent- and OpenAPI-facing description.</summary>
    public string? Description { get; init; }
    /// <summary>How the tenant MCP server exposes the stack. Default <see cref="McpExposure.Full"/>.</summary>
    public McpExposure Mcp { get; init; } = McpExposure.Full;
    /// <summary>Whether the versioned public surface projects this stack (seam; default false).</summary>
    public bool Public { get; init; }
}

/// <summary>The default select list used when a query names none; also the concise MCP shape.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class DefaultSelectAttribute(string select) : Attribute { public string Select { get; } = select; }

/// <summary>Marks a public service method as an API action: POST /{tenantId}/api/web/{resource}/{name}, and an MCP action.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ApiActionAttribute(string name) : Attribute
{
    /// <summary>The kebab-case action segment; unique per service; not a standard operation name.</summary>
    public string Name { get; } = name;
    /// <summary>The securable action; default is <see cref="Name"/>.</summary>
    public string? Action { get; init; }
    /// <summary>Any active tenant member may call it; no securable is checked.</summary>
    public bool MemberOnly { get; init; }
    /// <summary>Repeating the call has no further effect (MCP idempotentHint).</summary>
    public bool Idempotent { get; init; }
    /// <summary>Whether the MCP action tool lists it.</summary>
    public McpExposure Mcp { get; init; } = McpExposure.Full;
    /// <summary>Agent-facing description; falls back to the method's XML summary through the manifest.</summary>
    public string? Description { get; init; }
}

/// <summary>Route prefix for a non-entity API service (e.g. "me", "settings").</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ApiRouteAttribute(string route) : Attribute { public string Route { get; } = route; }

/// <summary>A property the client may send but the save pipeline never takes from the payload.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class ServerOwnedAttribute : Attribute
{
    /// <summary>True for write-once columns: taken from the payload on create, ignored on update.</summary>
    public bool AfterCreate { get; init; }
}

/// <summary>MCP exposure of a stack or action.</summary>
public enum McpExposure { Full, ReadOnly, Hidden }
```

### 3.3 The closed exception set — `Tellma.Core.Abstractions.Api`

```csharp
namespace Tellma.Core.Abstractions.Api;

/// <summary>Base of every exception the platform maps to a problem response. The set of subclasses is closed.</summary>
public abstract class TellmaProblemException(string code, string? detail = null, Exception? inner = null) : Exception(detail, inner)
{
    /// <summary>The closed-vocabulary code, e.g. "validation"; also the problem type suffix.</summary>
    public string Code { get; } = code;
    /// <summary>Named values for message composition; untrusted display data.</summary>
    public IReadOnlyDictionary<string, string> Arguments { get; init; } = new Dictionary<string, string>();
}

public sealed class BadRequestException(string? detail = null) : TellmaProblemException("bad-request", detail);
public sealed class QueryInvalidException(IReadOnlyList<QueryDiagnostic> diagnostics) : TellmaProblemException("query-invalid")
{ public IReadOnlyList<QueryDiagnostic> Diagnostics { get; } = diagnostics; }
/// <summary>One engine diagnostic, wire-shaped: Clause is "select"|"filter"|"orderBy"|"having".</summary>
public sealed record QueryDiagnostic(string Clause, string Code, int Start, int Length, string? Location, IReadOnlyDictionary<string, string> Arguments);
public sealed class ValidationFailedException(IReadOnlyList<ValidationError> errors) : TellmaProblemException("validation")
{ public IReadOnlyList<ValidationError> Errors { get; } = errors; }
/// <summary>One field error: a wire-cased path such as "entities[0].roleMemberships[2].roleId".</summary>
public sealed record ValidationError(string Path, string Code, IReadOnlyDictionary<string, string> Arguments);
public sealed class StepUpRequiredException(string acrValues, int? maxAge) : TellmaProblemException("step-up-required")
{ public string AcrValues { get; } = acrValues; public int? MaxAge { get; } = maxAge; }
public sealed class ForbiddenException(string resource, string action) : TellmaProblemException("forbidden")
{ public string Resource { get; } = resource; public string Action { get; } = action; }
public sealed class TenantSuspendedException(bool readOnly) : TellmaProblemException(readOnly ? "tenant-read-only" : "tenant-suspended");
public sealed class NotFoundException(string entity, int? id = null) : TellmaProblemException("not-found")
{ public string Entity { get; } = entity; public int? Id { get; } = id; }
public sealed class ConcurrencyConflictException(IReadOnlyList<ConcurrencyConflict> conflicts) : TellmaProblemException("concurrency-conflict")
{ public IReadOnlyList<ConcurrencyConflict> Conflicts { get; } = conflicts; }
public sealed record ConcurrencyConflict(int Id, DateTimeOffset ModifiedAt, int ModifiedById, string? ModifiedByName);
public sealed class CountMismatchException(int expected, int actual) : TellmaProblemException("count-mismatch")
{ public int Expected { get; } = expected; public int Actual { get; } = actual; }
public sealed class PayloadTooLargeException(string limit, long actual, long maximum) : TellmaProblemException("payload-too-large");
public sealed class DependencyUnavailableException(string dependency, TimeSpan? retryAfter = null, Exception? inner = null)
    : TellmaProblemException("dependency-unavailable", inner: inner)
{ public string Dependency { get; } = dependency; public TimeSpan? RetryAfter { get; } = retryAfter; }
```

### 3.4 Header and telemetry names — `Tellma.Core.Abstractions.Api`

```csharp
namespace Tellma.Core.Abstractions.Api;

/// <summary>Request and response header names of the platform surfaces.</summary>
public static class TellmaHeaders
{
    public const string TimeZone  = "Tellma-Time-Zone";
    public const string Calendar  = "Tellma-Calendar";
    public const string Client    = "Tellma-Client";
    public const string CacheTags = "Tellma-Cache-Tags";
}

/// <summary>Meter and instrument names of the web surface.</summary>
public static class ApiTelemetryNames
{
    public const string MeterName = "Tellma.Core.AspNetCore";
    public const string RequestsRejected = "tellma.api.requests.rejected";
    public const string Problems = "tellma.api.problems";
    public const string OperationDuration = "tellma.api.operation.duration";
    public const string ResourceTag = "tellma.resource";
    public const string OperationTag = "tellma.operation";
    public const string ClientTag = "tellma.client";
    public const string ReasonTag = "reason";
}

/// <summary>Meter and instrument names of the tenant MCP server.</summary>
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

### 3.5 Host surface — `Tellma.Core.AspNetCore` (owned here)

```csharp
namespace Tellma.Core.AspNetCore;

/// <summary>Maps every platform surface onto the application; the distribution calls it once.</summary>
public static class TellmaEndpointRouteBuilderExtensions
{
    /// <summary>Maps the tenant group and every projected endpoint, the MCP server when registered,
    /// the well-known documents, the distribution contract surface, the webhook fronting, and the SPA
    /// fallback; then audits every tenant endpoint for securable metadata.</summary>
    public static TellmaEndpoints MapTellma(this WebApplication app);
}

/// <summary>Handles to the mapped groups, for hand-mapped endpoints.</summary>
public sealed class TellmaEndpoints
{
    /// <summary>The /{tenantId}/api/web group with every convention attached.</summary>
    public RouteGroupBuilder TenantGroup { get; }
    /// <summary>Endpoints outside any tenant (anonymous, output-cached where declared).</summary>
    public RouteGroupBuilder DistributionGroup { get; }
}

/// <summary>Securable and member-only conventions for hand-mapped endpoints (D11).</summary>
public static class TellmaEndpointConventionBuilderExtensions
{
    public static TBuilder RequireSecurable<TBuilder>(this TBuilder builder, string resource, string action) where TBuilder : IEndpointConventionBuilder;
    public static TBuilder AllowMember<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder;
}

/// <summary>Endpoint metadata read by the securable filter, the startup audit, OpenAPI, and MCP.</summary>
public sealed record SecurableEndpointMetadata(string Resource, string Action);
public sealed record MemberOnlyEndpointMetadata;

/// <summary>Bound from "Tellma:Api". Every member has a default (D17).</summary>
public sealed class TellmaApiOptions
{
    public long MaxJsonBodyBytes { get; set; } = 8 * 1024 * 1024;
    public long MaxUploadBytes { get; set; } = 100L * 1024 * 1024;
    public int MaxEntitiesPerSave { get; set; } = 1_000;
    public int MaxIdsPerRequest { get; set; } = 10_000;
    public int MaxTake { get; set; } = 10_000;
    public int MaxCount { get; set; } = 10_000;
    public int RequestsPerMinutePerUser { get; set; } = 600;
    public int AnonymousRequestsPerMinutePerIp { get; set; } = 60;
    public int ConcurrentExportsPerUser { get; set; } = 2;
    public int ConcurrentImportsPerUser { get; set; } = 1;
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan LongRequestTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public bool EnableCompression { get; set; } = true;
    public ISet<string> ClientNames { get; } = new HashSet<string>(StringComparer.Ordinal) { "web", "cli", "mcp" };
    public string ProblemTypeBase { get; set; } = "https://tellma.com/problems/";
}
```

```csharp
namespace Tellma.Core.Mcp;

/// <summary>Registers the tenant MCP server; called inside AddTellma.</summary>
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
    public int ListTtlMs { get; set; } = 300_000;
    /// <summary>Extra tool types (SDK [McpServerToolType]) a distribution adds.</summary>
    public IList<Type> ToolTypes { get; } = [];
}
```

### 3.6 Shapes needed from other themes' seams

From the **service pipeline theme** (seam 3, "one capability declared once") — the catalog the
projection and MCP read:

```csharp
namespace Tellma.Core.Abstractions.Stacks;

/// <summary>Every registered entity stack and non-entity API service, built during AddTellma's realize phase.</summary>
public interface IEntityStackCatalog
{
    IReadOnlyList<EntityStackDescriptor> Stacks { get; }
    IReadOnlyList<ApiServiceDescriptor> Services { get; }
    EntityStackDescriptor? FindByResource(string resource);
    EntityStackDescriptor? FindByEntityName(string entityName);
}

public enum StandardOperation { Query, Get, GetByIds, GetByParentIds, Save, Delete, DeleteByQuery, DeleteWithDescendants, Activate, Deactivate, Export, ExportForImport, Import }

public sealed record EntityStackDescriptor(
    string EntityName,                                    // "Center"
    string Resource,                                      // "centers"
    Type EntityType, Type ServiceType,
    IReadOnlySet<StandardOperation> Operations,           // derived from the service's capability interfaces
    IReadOnlyList<ApiActionDescriptor> Actions,
    IReadOnlyList<EntityPropertyDescriptor> Properties,   // name, type, nullable, editable, maxLength, enum values, navigation target, multilingual
    IReadOnlyList<EntityStackDescriptor> Children,        // child collections
    IReadOnlyList<string> SearchableProperties,
    string? NaturalKey, string? DefaultSelect, string? Description,
    McpExposure Mcp, bool Public);

public sealed record ApiActionDescriptor(string Name, string? Action, bool MemberOnly, bool Idempotent, McpExposure Mcp, string? Description,
    MethodInfo Method, Type? RequestType, Type ResultType);
public sealed record ApiServiceDescriptor(string Route, Type ServiceType, IReadOnlyList<ApiActionDescriptor> Actions);
```

And the generic service surface the projection calls (names to be settled by that theme; the
operation set is what matters):

```csharp
public interface IEntityService<TEntity>
{
    Task<QueryResult> QueryAsync(QueryRequest request, CancellationToken cancellationToken);
    Task<EntitiesResult<TEntity>> GetAsync(GetRequest request, CancellationToken cancellationToken);           // throws NotFoundException
    Task<EntitiesResult<TEntity>> GetByIdsAsync(IdsRequest request, CancellationToken cancellationToken);      // partial
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

From the **distribution-host theme** (seam 9, request context) — the scoped holder both surfaces
populate and every service reads:

```csharp
namespace Tellma.Core.Abstractions.Requests;

public enum TenantKind { Live, Sandbox }

/// <summary>Immutable facts of the current request or job scope; populated in stages by the host.</summary>
public sealed record RequestContext
{
    public required int TenantId { get; init; }
    public required TenantKind TenantKind { get; init; }
    /// <summary>The identity subject (36-char GUID string); null for anonymous distribution endpoints.</summary>
    public string? Subject { get; init; }
    /// <summary>The tenant user id once the connect step resolved it.</summary>
    public int? UserId { get; init; }
    public required CultureInfo Culture { get; init; }        // UI/message language + formatting; extensions stripped
    public required string CalendarCode { get; init; }         // "gc" | "uq" | "et"
    public required TimeZoneInfo TimeZone { get; init; }
    /// <summary>Today in <see cref="TimeZone"/>; bound to the Queryex today() slot.</summary>
    public required DateOnly Today { get; init; }
    public required string Client { get; init; }               // "web" | "cli" | "mcp"
    public ClaimsPrincipal? Principal { get; init; }
}

/// <summary>Scoped holder. Set once by tenant middleware, enriched once by header negotiation and once by the connect step.</summary>
public interface IRequestContextAccessor
{
    RequestContext Current { get; }
    void Enrich(Func<RequestContext, RequestContext> update);
}
```

From the **permissions theme** (seam 11): `IPermissionEvaluator.EvaluateAsync(string resource,
string action, CancellationToken) → PermissionDecision { bool Allowed; FilterTree? Filter;
IReadOnlyList<PermissionReason> Why; }` plus a batch form `EvaluateAllAsync()` returning the
matrix `tellma_whoami` renders. From the **settings/cache theme** (seam 5): the tag names and
values the connect step read, as `IReadOnlyDictionary<string, string> Tags` on the request
context's enrichment, for the `Tellma-Cache-Tags` header. From the **blob theme** (seam 12): the
`GET /{tenantId}/api/web/blobs/{id}` endpoint mapped on `TenantGroup` with `AllowMember()` plus a
service-level check, and `POST /{tenantId}/api/web/blobs/upload` (multipart) returning a staging
token. From the **Excel theme**: `ExportRequest` (a `QueryRequest` or `IdsRequest` plus format
options) and `ImportResult`. From the **background-task theme**: `TaskAccepted` usage and the
`tellma_task_status` tool.

---

## 4. Schema

This theme owns no tables. Every column it reads belongs to another theme: `User.Subject`,
`User.IsActive` and the sibling activity/tag table (users theme); the tenant tag table
(settings/cache theme); the tenant catalog row's state (`Active`, `ReadOnly`, `Suspended`) and kind
(distribution-host theme). The only persistent artifact this theme introduces is configuration
(`Tellma:Api`, `Tellma:Mcp` sections), whose schema is the two options classes in §3.5.

---

## 5. Answers

| Brain-dump question (abridged) | Answer |
|---|---|
| "How do we extract the common CRUD endpoint boilerplate into the platform Core, so that the distro codebase stays lean?" | D7: `MapTellma()` projects every endpoint from the stack catalog; custom actions are `[ApiAction]` methods; zero web lines per entity (D21). |
| "Can we use source-generated JSON serialization for maximum performance?" | D6: yes for every envelope, request, and problem type (`TellmaApiJsonContext`); entity types resolve by reflection appended to the chain so the distribution declares nothing; query rows use a hand-written writer. A scaffolded per-distribution context is a later optimization (review flag). |
| "Should we accept an X-Today header … or supply the user's timezone instead?" | D9: `Tellma-Time-Zone` (IANA), no today header; `today()` = server clock in the request's effective zone with precedence header → user preference → tenant. |
| Three surfaces `{tenantId}/api/web`, `/api/v1`, `/mcp` | D2/D3/D19: web built (POST-only); `v1` reserved with its versioning and verb projection fixed; MCP built (D12). |
| "The MCP schema is discoverable at runtime … can be changed without breaking existing agents (is this true?)" | Partly. Clients re-list tools (`tools/list` with `ttlMs`), so adding tools, arguments with defaults, and description changes are safe. Renaming or removing a tool or a required argument breaks saved prompts, skills, and workflows that name it; tool names and argument names are a compatibility surface and stay stable across minors. |
| "How would the web layer determine the status code … enumerate exceptions or an interface?" | D10: a closed set of platform exception types and one mapping table; distributions throw platform types only. |
| "Does the server return validation error messages or codes?" | D10: both — localized messages in `errors`, codes and arguments in `errorDetails`. |
| Rate limiting: "generous but bounded limit on input size and rate … purely in-memory" | D17: in-process partitioned limiters per `{tenantId}:{sub}`, per-IP for anonymous, concurrency for export/import; sizes by endpoint metadata; cardinality by the pipeline. |
| "Should there be one MCP server per tenant, or one server for the entire distro?" | D13: one per tenant at `/{tenantId}/mcp`; two tenants = two configured servers. |
| MCP: "few enough tools … intent-based and well documented" | D12: seven generic tools plus two reserved; entity discovery in `tellma_describe`; result caps; underscore names. |
| MCP auth: "a human user via Claude Code or Codex … and autonomous agents on a server" | D14/D15: OAuth 2.1 resource server with per-tenant PRM; humans via authorization code + PKCE (CIMD or pre-registered native clients); agents via service-account `client_credentials`. |
| "Should we keep the search parameter?" | Carried on the wire unchanged (D4); the pipeline theme owns its semantics (searchable columns declared once on the entity, reported by `tellma_describe`). |
| "If the user requests 5 ids and 4 are found, 4 or 404?" | D5: `get-by-ids` returns the four; `get` (single) returns 404. |
| "How do we design the API surface to make … difficult to forget to secure an endpoint?" | D11: securable metadata on every projected endpoint, `RequireSecurable`/`AllowMember` on hand-mapped ones, fallback policy, and a startup audit that refuses the host otherwise. |
| Write-once columns (`Subject`, `Email`): two UDTTs or service-layer rule? | D6: service-layer rule declared once with `[ServerOwned(AfterCreate = true)]`; the wire tolerates the values; the pipeline ignores them on update. |
| "Are those the proper layer names …?" | Not this theme's to decide; "Web" for the HTTP/MCP layer matches `Tellma.Core.AspNetCore`'s scope and needs no rename. |

---

## 6. Seams

1. **Batch abstraction** (owner: data access). Position: the executor must take the request's
   `CancellationToken` (`HttpContext.RequestAborted`, which the request-timeout middleware cancels)
   on every command so a timed-out request releases its connection; nothing else is needed here.
2. **Entity class vs wire shape** (owner: data access). Position: single entity class,
   `[NotMapped]` child collections, `[ServerOwned]`/`[ServerOwned(AfterCreate)]` markers,
   camelCase STJ (D6). Contract needed: the child-collection convention (`List<TChild>` property
   named after the child table, `[NotMapped]`) so the projection and `tellma_describe` can find
   children without a registry entry.
3. **One capability, declared once** (owner: service pipeline). Position: the capability is
   declared on the entity (base class + marker interface) and the service (marker interface); the
   catalog descriptor (§3.6) is the *only* thing HTTP and MCP read. Contract needed:
   `IEntityStackCatalog` exactly as in §3.6, populated in the realize phase before `MapTellma()`
   runs.
4. **Queryex schema per tenant** (owner: data access). Position: the projection never touches the
   schema; `QueryInvalidException` carries diagnostics verbatim (D10); `Name2`/`Name3` gating shows
   up on the wire as omitted properties, which `tellma_describe` must reflect per tenant (so
   `describe` output is per-tenant even though the tool list is not).
5. **Version tags** (owner: settings/cache). Position: the tags the connect step read are echoed in
   `Tellma-Cache-Tags` on every web response (D9); contract needed: the tag set as
   `IReadOnlyDictionary<string, string>` on the enriched request context, with stable key names
   (`settings`, `permissions`, `user-settings`, `securables`).
6. **Feature composition** (owner: distribution host). Position: `AddMcp()` is a feature that
   `Requires` the stack feature; `MapTellma()` is the single map call and reads the realized
   registry; the projection is a *contribution* of the stack feature, not a separate feature.
   Contract needed: `TellmaBuilder` with `AddStack<TEntity, TService>()` and feature `Requires`
   edges; a hook the stack feature uses to register endpoint contributions (`IEndpointContributor`
   with `void Map(TellmaEndpoints endpoints)`), which is also the escape-hatch home for a module's
   unusual endpoint.
7. **Natural keys** (owner: data access). Position: `tellma_describe` reports the natural key;
   nothing else here.
8. **Background-task columns** (owner: background tasks). Position: `TaskAccepted` is the 202 body;
   task status is read through the notifications/inbox surface (that theme's endpoints), and the
   MCP `tellma_task_status` tool is reserved.
9. **Request context** (owner: distribution host). Position and contract: §3.6 `RequestContext` /
   `IRequestContextAccessor`; tenant middleware sets tenant facts, the negotiation filter sets
   culture/calendar/time zone/client, the connect step sets `UserId` and tags. No `AsyncLocal`
   source of truth; the MCP host populates the same holder from the bearer principal and route
   values so `ISandboxContext` and every service see one shape.
10. **Platform exceptions and HTTP mapping** (types: service pipeline; mapping: here). Position:
    the closed set of §3.3 lives in Abstractions so both themes and every module reference it; the
    mapping table is D10. The pipeline theme must throw `ValidationFailedException` with wire-cased
    paths and `NotFoundException` for row-level-security misses.
11. **Permission evaluation** (owner: permissions). Contract needed: `IPermissionEvaluator` with
    `EvaluateAsync(resource, action)` → `{Allowed, Filter, Why}` and `EvaluateAllAsync()` for the
    matrix; the securable filter calls the first without a database round trip (cached permissions,
    validated by the connect step inside the service's first batch — the collapse belongs to the
    users/pipeline themes and this theme adds no round trip).
12. **Blob staging tokens** (owner: blobs). Position: the web surface's only `GET` is the blob
    endpoint; `upload` is multipart with the `Tellma-Client` header; tokens travel inside entity
    JSON as ordinary string properties.
13. **Wire shapes** (owner: here). §3.1 is the contract; the Excel theme reuses `QueryRequest`
    and `IdsRequest` inside `ExportRequest`; the pipeline theme returns `EntitiesResult<T>`
    directly so the endpoint is a pass-through.
14. **Telemetry** (owner: data access for the DB budget). This theme's instruments are D20; the
    request `Activity` carries `tellma.resource`/`tellma.operation`/`tellma.client` so the DB-call
    budget histogram can be sliced by operation.
15. **Notification enqueue** (owner: background tasks). Not touched.
16. **Connect-call collapse** (owners: users/pipeline). Position: endpoint filters perform no
    database call; the securable filter uses cached permissions and the service's first batch
    validates them; when the batch reports a stale permissions tag the pipeline recomputes and
    re-runs — the web layer sees one call.
17. **Vocabulary**. Resource segments are kebab-case plural derived from the (plural) table name,
    matching `gl.Invoices`; securable resource id = resource segment; entity name = Queryex logical
    name; operation segments are the thirteen verbs of D2; securable actions are `read`, `save`,
    `delete`, `activate`, plus custom action names; problem codes are kebab-case; headers are
    `Tellma-*`; the runtime MCP server is the "Tellma Tenant MCP server".

---

## 7. Departures from ARCHITECTURE.md

1. **Verbs.** "Endpoints are generated … (read → GET, save → POST, delete → DELETE …)" becomes
   true only for the versioned public surface; the private web surface is POST-only (D3). Reason:
   Queryex text belongs in bodies, one verb gives one CSRF and one binding story, and the SPA never
   uses HTTP caching of queries.
2. **Route shape.** The multi-tenancy section's example `api/{tenantId}/documents` becomes
   `/{tenantId}/api/web/{resource}/{operation}` (D2). Reason: tenant-first serves every surface,
   including the RFC 9728 well-known path for MCP.
3. **New packages.** `Tellma.Core.AspNetCore` and `Tellma.Core.Mcp` join the Core family; the
   statement that `Tellma.Core.Webhooks` is "the one project here taking a FrameworkReference to
   Microsoft.AspNetCore.App" is no longer true (D1). The package table and dependency graph gain
   both, and the rule "optional Core-layer packages depend only on Abstractions" gets the same
   carve-out Queryex has: `Tellma.Core.AspNetCore` references `Tellma.Core` because it is the host
   half of Core, not an optional pipeline.
4. **Two MCP servers, named.** The "MCP topology" paragraph describes only developer tooling; it
   gains the Tellma Tenant MCP server (D16) and the naming that separates the two.
5. **Output caching.** The guiding principle lists "output caching" among server speed-ups; on the
   authenticated surfaces it is unsafe and is confined to anonymous tenant-independent GETs (D18).
6. **Entity-class-as-wire.** The architecture says "no separate DTO model on the persistence path";
   this design extends that to the wire path with `[NotMapped]` children and `[ServerOwned]` (D6) —
   an extension, recorded so the "no parent→child navigations" rule is read as an EF-model rule.
7. **`today()` semantics** (spec 0008 §10.6 text, not the architecture document): the tenant zone is
   the fallback, not the definition (D9, review flag).

---

## 8. Verification

Facts relied on from `.tmp/crud-design/research/web-api-mcp.md` (verified there on 2026-09-01):
route groups with route parameters and group-level conventions (§1.2); endpoint filters and filter
factories (§1.3); `TypedResults`/`Results<>` (§1.4); OpenAPI 3.1 default and per-request document
generation (§1.5); `[AsParameters]`/`BindAsync` (§1.6); STJ options and `TypeInfoResolverChain`
(§1.7, §2); built-in validation is shape-only, sync, PascalCase-keyed (§1.8); `IProblemDetailsService`,
`IExceptionHandler`, RFC 9457 (§1.9); in-process partitioned rate limiting (§1.10); request size
metadata (§1.11); request timeouts cancel `RequestAborted` and default to 504 (§1.12); output caching
never serves authenticated responses by default (§1.13); compression defaults and the HTTPS opt-in
(§1.14); antiforgery does not cover JSON endpoints and cookie auth returns 401/403 on API endpoints
(§1.15); MCP 2026-07-28 is stateless with `Mcp-Method`/`Mcp-Name` headers, MRTR elicitation,
cacheable lists with `ttlMs`/`cacheScope`, tool annotations, and `isError` tool results (§3); the
authorization rules — RFC 9728 PRM with path insertion, RFC 8707 `resource` as the canonical MCP
URL, CIMD preferred over deprecated DCR, PKCE S256, 401/403 challenges (§3.4); C# SDK 2.2.0
`MapMcp(pattern)`, `HttpServerSessionMode.Stateless`, request filters, `AddMcp` PRM/401 (§4);
OpenIddict 7.6.1 has RFC 8707 resources and PKCE/`iss` but no PRM, DCR, or CIMD (§5.2); the client
matrix — Claude Code CIMD/pre-registered, hosted Claude CIMD-only-with-`none`, ChatGPT/Codex CIMD,
Cursor DCR-or-static, hosted Claude no machine-to-machine (§5.4); Anthropic's tool-design guidance,
the 30–50 tool degradation threshold, and Claude Code's 25 000-token result cap (§6);
`Asp.Versioning.Http` 10.2.3 with URL-segment versioning (§7).

Facts relied on from the sibling research files: RFC 10017 (BCP 212) BFF cookie and custom-header
CSRF requirements, and the .NET 10 antiforgery statement that JSON endpoints are not auto-rejected
(`host-tenancy.md` §1.5–1.7); the request-context recommendation against `AsyncLocal` (§6);
`AuthorizationMiddleware` fallback-policy semantics and `AllowAnonymous` absoluteness, and the
startup-audit technique (`users-roles-permissions.md` §2); no standard time-zone/calendar header,
GitHub's `Time-Zone` precedent, and the `-u-ca-` trap (`settings-cache-l10n.md` §3.4, §5.2); the
`Lines[3].Quantity` path grammar and the PascalCase-key issue #61764 (`service-pipeline.md` §1.1).

Facts verified by this proposal on 2026-09-01:

- `ModelContextProtocol.AspNetCore` latest stable is **2.2.0 (2026-08-13)**; 2.1.0 (2026-08-05),
  2.0.0 (2026-07-28), 1.4.1 (2026-07-09); targets net8.0/net9.0/net10.0. Source:
  https://www.nuget.org/packages/ModelContextProtocol.AspNetCore
- `McpAuthenticationEvents.OnResourceMetadataRequest : Func<ResourceMetadataRequestContext, Task>`
  exists and is documented as the per-request hook ("set the ResourceMetadata property to provide
  the appropriate metadata for the current request") — which is what makes per-tenant PRM documents
  at `/.well-known/oauth-protected-resource/{tenantId}/mcp` feasible with the stock handler
  (D13). Source: https://csharp.sdk.modelcontextprotocol.io/v2/api/ModelContextProtocol.AspNetCore.Authentication.McpAuthenticationEvents.html
- OpenAI tool names must match `^[a-zA-Z0-9_-]{1,64}$` (dots rejected), hence underscore tool
  names (D12). Sources: https://developers.openai.com/api/docs/guides/function-calling ;
  https://community.openai.com/t/openai-badrequesterror-error-code-400-does-not-match-a-za-z0-9-1-64/709823
- No OpenIddict CIMD support or tracking issue surfaced in a search on 2026-09-01; the IETF draft is
  at `draft-ietf-oauth-client-id-metadata-document-01`, and other providers (Clerk, Authlete) ship
  it as an opt-in flag. Sources: https://datatracker.ietf.org/doc/html/draft-ietf-oauth-client-id-metadata-document-01 ;
  https://clerk.com/changelog/2026-08-06-client-id-metadata-documents ; https://www.authlete.com/developers/cimd/
- Repo facts (read from the working tree): the identity server calls `server.DisableResourceValidation()`
  and validates `resource` against per-client `rsrc:` permissions in its own code
  (`Hosting/OpenIddictConfigurator.cs`, `Services/AuthenticationPolicy/TellmaPrincipalFactory.cs`
  `SetAudiencesAsync`), copies requested resources into `aud` verbatim, and grants new distribution
  audiences to platform clients (`ClientProvisioningService.GrantResourceToPlatformClientsAsync`) —
  the basis of D15 item 1. `Tellma.Core.Webhooks` maps `/api/webhooks/{key}` with `AllowAnonymous()`,
  `DisableAntiforgery()`, and `WebhookEndpointMetadata` for tenant-middleware skipping — the pattern
  the distribution group reuses. `Directory.Packages.props` pins no `ModelContextProtocol.*`,
  `Microsoft.AspNetCore.OpenApi`, or `Asp.Versioning.*`; OpenIddict is at 7.5.0 (7.6.1 is current).

Still unverified:

- Whether `RequestDelegateFactory` accepts delegates produced by `Delegate.CreateDelegate` over a
  closed generic static method with full parameter-binding metadata (the D7 projection mechanism);
  the fallback is a per-stack lambda that closes over the resolved `IEntityService<TEntity>` via a
  small generic helper class, which is known to work.
- Whether the SDK's `Stateless` mode serves every named client at launch (Claude Code v2, Codex,
  ChatGPT speak 2026-07-28; Cursor's revision is undocumented). If a client still speaks 2025-11-25,
  `HttpServerSessionMode.StatefulForInitializeClients` is the switch, at the cost of session
  affinity on App Service.
- Whether the MCP SDK's `AddMcp` handler serves the PRM document for a route with a route parameter
  when `ResourceMetadataUri` is left at its default (the docs say it "defaults to
  `/.well-known/oauth-protected-resource/<resource-path>`"); if the default cannot express
  `{tenantId}`, the document is mapped by hand on the distribution group (one `MapGet`) and the
  handler is given the explicit URI per request through the same event.
- The 25 000-token Claude Code cap versus a 60 000-character default: the ratio assumed is ~4 chars
  per token for JSON, which should be measured against real `tellma_query` output.

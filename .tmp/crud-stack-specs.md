# CRUD stack: spec breakdown

Breakdown of `crud-stack.md` into ten specs, numbered 0010 to 0019 in implementation order. All
ten are designed together first; each is then frozen and implemented on its own once the seams
listed at the end are fixed.

## The specs, in implementation order

### 0010 Distribution host and multi-tenancy
The skeleton every later spec plugs into.
- Owns: the reference distribution inside the platform repo with Web and Migrator projects, the
  `AddTellma` composition root at minimal fidelity (a feature declares and contributes, Requires
  edges only, one aggregated startup validation), tenant routing from the URL, the tenant registry
  and catalog for both single-live and multi-live shapes, per-tenant connection resolution, the
  secrets policy, the membership-lookup seam, the sandbox context implementation, and
  distribution-side authentication as an OIDC relying party with the BFF cookie.
- Brain-dump sections: "What we want to build" item 1, "Multi-tenancy", the auth half of "Web Layer".
- Leaves for later: tenant provisioning beyond a seam, the manifest source generator, the Builder
  tool, the bypass analyzer.

### 0011 Data access layer
Everything between an entity class and SQL Server, tested against fixture entities on LocalDB.
- Owns: the entity contract in Abstractions (keyed, temporal top-level, weak child, tree, audited,
  activatable, multilingual bases and conventions such as enum-as-string and tree columns), the ID
  allocator with range reservation riding on the batch, the bulk save emitter (upsert plus child
  synchronisation derived from the EF model and UDTT metadata, audit and concurrency stamping,
  cache-tag bumps), the batch builder and executor (Queryex, save and raw SQL statements in one
  round trip, per-statement retry flags, transaction scope, result readers), Queryex host
  integration (EF-model schema adapter, materializer, list restriction through a TVP, `level()`,
  any other amendments spec 0008 deferred), the tree recompute statement, and the DB-call budget
  instruments.
- Brain-dump sections: "SQL Save emitter and Multi-Statement Builder", "Id ranges", "Queryex
  engine", the entity half of "Extensibility", the data half of "Optimistic concurrency".
- Leaves for later: validation, side effects, HTTP.

### 0012 Tenant settings, localization, and the version cache
The tenant's configuration and the cache that keeps every instance fresh.
- Owns: the Settings and key-value tables and what belongs in each, the server and UI settings
  DTOs and their read path, the cache infrastructure (version tags with a hardcoded metaversion,
  bounded LRU, stampede safety, meters), the framework-level guarantee that a save bumps the right
  tag, the cacheable-entity mechanism, and localization (Core language catalogue, distro subset,
  tenant languages and calendars, request culture and calendar negotiation, fallback chain,
  resource organisation, ICU messages, calendar-aware formatting, gating of Name2 and Name3 in
  the Queryex schema).
- Brain-dump sections: "Caching", "Settings tables", "Localization".
- Leaves for later: the settings edit API (0017).

### 0013 Users, roles, and permissions
Who the caller is and what they may touch.
- Owns: the User, UserSettings, RoleMembership, Role and Permission entity classes with the
  churn-prone columns split into a sibling table, the tenant bootstrap that seeds the first admin,
  the per-request connect step (subject to user, activity stamp, version-tag reads), the
  securables registry, permission evaluation (public permissions, implicit read, disjunctive
  filters, inactive roles, bespoke criteria, weak-entity path rewriting), row-level-security
  filter composition, the permissions cache, self-lockout guards, the drift policy when securables
  change, and the "can I, and why" query.
- Brain-dump sections: the schema and state model of "User entity", "RoleMembership", "Role",
  "Permission", "Access control".
- Leaves for later: UserService and RoleService (0017), invitation (0017), the image (0016).

### 0014 CRUD stack: service pipeline and capabilities
The reusable service that every entity gets for free.
- Owns: the service base and the composition decision, the standard operations (query with capped
  count and ancestors, details with the related-entity dictionary and row echo, save, delete by
  ids and by query, get by parent ids, delete with descendants), the save pipeline (authorization,
  RLS pre-check, preprocessing, the validation framework with batched and deduplicated context
  loading, the transaction boundary, bulk persist, RLS post-check, transactional and pre-commit
  and post-commit side effects), capability recipes (IsActive, tree with cycle validation, audit,
  record-plus-blobs hook), the concurrency override flag, the search-parameter decision, and
  distro extension of pack entities.
- Brain-dump sections: "Service Layer", "Endpoint summary" and every per-endpoint section except
  the Excel ones, "Capability endpoints", the service half of "Optimistic concurrency".
- Leaves for later: Excel (0018), HTTP (0015).

### 0015 CRUD stack: web API surface
How the service is exposed.
- Owns: endpoint projection from service capabilities with Minimal APIs, the three surfaces (web
  built, versioned and MCP left as seams, including the MCP tool-shape and auth sketch), wire
  shapes and source-generated JSON, the exception-to-status contract, the validation error format,
  payload and rate limits, and the culture, calendar, today and timezone headers.
- Brain-dump sections: "Web Layer", "Dedicated API per client", "Exception handling", "Rate
  limiting", "MCP Server".

### 0016 Blob storage and the record-plus-blobs pattern
Binary data next to records.
- Owns: the blob service contract with file-system and Azure implementations and their packaging,
  tenant scoping, the staging-versus-inline decision for blobs attached to unsaved records,
  orphan collection, the etag-validated retrieval endpoint, and where image metadata lives.
- Brain-dump sections: "Blobs", "The record + blobs pattern".

### 0017 Core and GL reference stacks
The first end-to-end product.
- Owns: UserService (invitation through the identity server's bulk invite and delivery-status
  APIs, the user state model, self-service profile and preferences, image, notification settings,
  test-notification endpoints), RoleService with validation against the securables registry, the
  settings edit API, the GL module package with its Abstractions companion and registry entry,
  Center as the first tree entity, seed data, and the reference distribution's migrations.
- Brain-dump sections: "3 services", "Custom endpoints", "Centers entity", the user states.

### 0018 Excel codec: export and import
Entities as spreadsheets, both directions.
- Owns: export by query and by ids in display shape, export-for-import in editable shape with
  children, natural-key declaration and inference, multilingual-aware column mapping with user
  override, the three import modes with hydration of partial sheets, bulk natural-to-surrogate
  translation, tree import with in-sheet parents, localization metadata on columns, the library
  choice, and hand-off of large files to background work.
- Brain-dump sections: the four Export sections and "Import".

### 0019 Background tasks, scheduler, and the inbox
Work that outlives the request, and how the user hears about it.
- Owns: the leasing machinery (standard columns, handler registry, batch lease and renewal,
  nudging, multi-instance safety, telemetry), the CRON scheduler with its replay policy and
  credentials model, trace linkage, the first consumers (large import and export, the blob orphan
  sweep), the inbox entity and counters, per-type actions, notification preferences, and the
  SignalR hub under the session rules of spec 0003.
- Brain-dump sections: "Background Tasks and Scheduling", "Inbox".
- Leaves for later: the email outbox, which stays the separate spec 0007 promised and becomes a
  consumer of this machinery. If this spec grows too large, the inbox and hub split off as 0020.

## Seams to fix in the joint design before any spec freezes

- One batch abstraction serves five specs. Validation context loading, ID reservation, cache-tag
  bumps, and inbox notifications all ride the same round trip as the save, so the API in 0011
  must be shaped by 0012, 0013, 0014 and 0019 up front.
- Entity class versus wire shape. The architecture bans parent-to-child navigations on entities,
  yet save and details payloads carry child collections. 0011 and 0015 must agree whether a
  separate DTO layer exists.
- One capability, declared once. Columns, service methods, permission action, routes, and default
  filters for something like IsActive span 0011, 0013, 0014 and 0015.
- The Queryex schema is per tenant configuration. Name2 gating, RLS composition, and weak-entity
  path rewriting all reshape it, so its cache key and lifetime cross 0011, 0012 and 0013.
- Version tags. Which tags exist, where they live, who bumps them, and who reads them at connect
  time crosses 0011, 0012 and 0013.
- Feature composition seam. 0010 defines the feature contract, 0014 the stack feature, 0017 the
  module package. All three must use one shape.
- Natural keys belong to the entity contract in 0011 even though their first consumer is 0018.
- Background task columns and lease statements in 0019 are emitted by 0011's machinery, so the
  lease shape should be known when 0011 is designed.

## Misalignments with ARCHITECTURE.md to settle

- Feature composition is absent from the brain dump but is the architecture's answer to
  extracting CRUD boilerplate. Decide how much of it the first release builds.
- HTTP verbs. The architecture projects read to GET and delete to DELETE. The brain dump makes
  every web endpoint a POST.
- Save cardinality. The guiding principles say save endpoints accept arrays. The brain dump's
  Save takes one entity.
- Catalog database. The architecture assumes a Catalog DB per distribution and cites sharding
  code that does not exist yet. The brain dump keeps the catalog table inside the live DB for
  single-live distributions.
- Reference distribution location. The phasing text says `distributions/<slug>/`. The layout tree
  says `samples/tellma-sample-distribution/`. Both are empty today, and `taxonomy.json` with the
  GL module entry does not exist.
- Two MCP servers. The architecture's `dotnet tellma mcp` is developer tooling. The brain dump's
  is the runtime server for end users. They need distinct names.
- Capability interfaces. The architecture rejects a paired interface per entity. The brain dump
  wants services coded against interfaces so distros can replace entities. Generic services over
  leaf types with capability constraints reconcile both.
- Promised homes. ID allocation and the EF-to-Queryex adapter were promised to separate specs.
  Both land in 0011, and the architecture's pointers change then.

# PC-F67: OpenAPI schema writer depth decoupled from the wire MaxDepth

## Implementer notes

### Root cause

`Microsoft.AspNetCore.OpenApi` 10.0.11 (verified by decompiling the package) builds every schema in
its internal, keyed `OpenApiSchemaService`. That service takes `IOptions<JsonOptions>`, the HTTP
JSON options, and copies them twice: once for `JsonSchemaExporter.GetJsonSchemaAsNode`, and once
for the context that parses the exported node back into an `OpenApiSchema`. Both copies keep
`MaxDepth = 16` (spec 0016 §3.1). The parse step fails once the exported node nests past 16, and
the exporter itself fails a few levels further (R1-6 of PC-096 observed both). Either failure fails
the whole `/openapi/web.json` with a 500, not just one route.

A schema nests deeper than the values it describes. Measured with the exporter under the platform's
rules: `MeResult` 15, `ClientSyncResult` with a typed `me` 17, a 16-object value (the deepest the
wire carries) 33. That is two levels per object (`properties`, then the member). A polymorphic level
costs four (`anyOf`, its entry, `properties`, the member), so the worst value the wire can carry
needs 65.

### What was built, and where

- **`WebOpenApi.SchemaMaxDepth = 128`** (`src/core/Tellma.Core.AspNetCore/OpenApi/WebOpenApi.cs`):
  the document's own limit, documented on the constant with the arithmetic above (65 needed at
  worst, about twice that as margin). It is only a recursion guard for the generator. Nothing a
  caller sends is read under it.
- **`SchemaJson`** (`src/core/Tellma.Core.AspNetCore/OpenApi/SchemaJson.cs`, internal):
  - `Register(services, documentName)` runs right after `AddOpenApi("web")`. It finds each service
    keyed by the document whose public constructor takes `IOptions<JsonOptions>`. In 10.0.11 that
    is exactly the schema service. It swaps that descriptor in place for a keyed factory, which
    builds the same implementation type with `ActivatorUtilities` and passes it the key plus the
    document's options. The internal type is never named: it is taken from the descriptor
    `AddOpenApi` registered.
  - If no such service exists, composition fails with `InvalidOperationException`. Otherwise a
    generator change would quietly fall back to the wire's limit.
  - `Create(services)` builds the document's options through
    `IOptionsFactory<JsonOptions>.Create(Options.DefaultName)`, a fresh instance run through every
    configure and post-configure step that the HTTP options get. These are `TellmaJson.Configure`,
    the unknown-member counter and the generator's own resolver insert. `MaxDepth` is then set to
    `SchemaMaxDepth`. The HTTP options singleton is never touched, so request and response bodies
    keep 16.
- **`ClientSyncResult.Me` is `MeResult?` again.** `ClientSyncService` returns
  `MeEnvelope.Of(...)` directly. `PreSerializedJson<T>.Of` had no other caller and is removed. The
  type stays for `Settings`, which spec 0025 §5.1 serves from the cache's bytes. Its remark that
  deep results are pre-serialized for the OpenAPI document is dropped, and so is the `Me` paragraph
  in `ClientSyncResult`'s XML doc.
- **Wire unchanged.** `MeResult` is written by the HTTP options under the same rules that
  `PreSerializedJson.Of` used (`TellmaJson`). `ContractDescriber` already described the member as
  `MeResult`, so the contract fingerprint `1UjYUCJx3WE2` and the route and stack goldens are
  unchanged. The client already types it as `me?: TmMeResult`, so no client type or API golden
  changes. There is no committed OpenAPI golden in the repository yet: spec 0016 §12.1's snapshot
  belongs to the reference distribution's test project, which does not snapshot the document today.
  So nothing was regenerated.
- **README** (`src/core/Tellma.Core.AspNetCore/README.md`): the `/openapi/web.json` row names the
  document's own limit.

### Tests

- `WebOpenApiTests` (Tellma.Core.AspNetCore.Tests):
  - `The_sync_answers_envelope_is_described_as_the_envelope`: client/sync's `me` references the
    `MeResult` component.
  - `A_value_as_deep_as_the_wire_carries_is_described_level_by_level`: a 16-object value (`PjDepth`
    over 15 distinct `PjLevel<T>` levels) is the body of `depth/read` and the answer of
    `depth/answer`, with one component per level down to `JsonElement`.
  - `The_schema_generator_reads_the_hosts_JSON_options_under_the_documents_own_nesting_limit`: the
    same naming, number handling, unmapped-member handling, converters and resolver chain, with
    `MaxDepth = 128`. The HTTP options stay at 16 after the document is served.
  - `The_documents_nesting_limit_without_a_document_fails`.
- `WebApiProjectionTests`, on a Development host after serving the document:
  - `A_body_is_read_sixteen_levels_deep_and_refused_one_level_deeper_while_the_OpenAPI_document_is_served`:
    a 16-level body reaches the action; at 17 levels the answer is 400 `bad-request` /
    `malformed-json` and the action never runs.
  - `An_answer_nesting_one_level_past_sixteen_is_never_written_while_the_OpenAPI_document_is_served`:
    a 500 `internal`.
- Mutation check: with the `SchemaJson.Register` call removed, 7 of these tests fail. Every
  document-reading test fails, the existing ones included, because client/sync's typed `me` now
  breaks the whole document.
- `PreSerializedJsonTests`: the `Of` round-trip test is removed with `Of`. The sync-answer
  schema test becomes "a member's schema is any value".
- `ClientSyncTests` reads `result.Me` directly, and the wire test also pins the `me` member as it is written under the platform's rules.
- Run: Tellma.Core.AspNetCore.Tests in full (990/990, unit filter); Abstractions PreSerializedJson/TellmaJsonOptions/PublicApi goldens (80); Core.Tests route, stack, contract-describer and client-fixture goldens (31, unchanged, so nothing to regenerate); ClientSyncTests and UserSelfServiceTests (30/31, see below).

### Deviations

1. The OpenAPI document generates its schemas under its own nesting limit,
   `WebOpenApi.SchemaMaxDepth = 128`, not spec 0016 §3.1's `MaxDepth = 16`. This also adds a second,
   document-only instance of the HTTP JSON options, against §3.1's "No other copy exists". The wire
   (`TellmaJson.Options` and the HTTP JSON options) keeps 16. This is D-185's open question for
   Ahmad: confirm that the OpenAPI document may use its own depth limit (or amend §3.1/§12.1).
2. PC-096 deviation 1 is withdrawn for `Me`, which is the real type on the wire contract, in the
   document and in the fingerprint. It stands for `Settings`, which spec 0025 §5.1 asks to be the
   cache's bytes.

### Open risks

- **The schema service is replaced by a factory over a framework-internal type.** The type is not
  named, but the replacement relies on three things: the service is keyed by the document name,
  takes `IOptions<JsonOptions>` in a public constructor, and takes its `[ServiceKey]` as a string
  that `ActivatorUtilities` fills from the explicit argument. A package bump that changes any of
  these either fails composition (no match) or fails `WebOpenApiTests` (the document 500s on the
  typed `me`). Neither can pass silently.
- **`settings` is still "any value" in the document.** It references
  `PreSerializedJsonOfTenantSettingsForClient`, an empty schema, while `TenantSettingsForClient`
  has its own component, which another route's schema brings in. A schema transformer could
  describe `PreSerializedJson<T>` as `T` (for example `allOf` over T's component), which would close
  §12.1 for this member too. It is out of this fix's scope.

- **Pre-existing local failure, not this change.** `UserSelfServiceTests.Another_user_s_bag_is_read_set_and_deleted_under_the_preferences_filter_within_its_budgets`
  fails with `ValidationException: Access.LastAdministrator` on `preferences/set`. It fails
  identically on the base commit with this change stashed (2026-11-22 03:18 UTC, Windows, run
  alone, fresh fixture database), so it is deterministic and not caused here. PC-096 reported the
  same suite green earlier on 2026-11-21. That points at a date- or time-dependent cause (the
  guard's `StartsAt <= SYSUTCDATETIME()` or a seed's timestamps). This is a lead for the
  orchestrator. The other 30 of the 31 ClientSyncTests and UserSelfServiceTests pass.

### What reviewers should examine hardest

- `SchemaJson.Register`'s match rule and the `ActivatorUtilities` call. Check the key argument
  against the `[ServiceKey] string documentName` parameter, and confirm that a lifetime other than
  singleton would not break it.
- That `IOptionsFactory<JsonOptions>.Create(DefaultName)` gives the same contracts as the HTTP
  singleton for schema purposes: the resolver chain, `ScalarSchemas` and `EntitySchemas` (which
  still read the HTTP options for type info and reference ids).
- That 128 is high enough and documented truthfully.

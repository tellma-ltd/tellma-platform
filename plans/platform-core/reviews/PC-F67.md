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
costs four (`anyOf`, its entry, `properties`, the member). The depth follows the type, not the value:
the exporter writes each type in full the first time it meets it, so a type whose optional members
nest past 16 objects still costs two levels per object type (review 1, R1-1).

### What was built, and where

- **`WebOpenApi.SchemaMaxDepth = 128`** (`src/core/Tellma.Core.AspNetCore/OpenApi/WebOpenApi.cs`):
  the document's own limit, documented on the constant in terms of type nesting: it fits a type
  nesting 63 objects plain, or about 31 polymorphic levels, past the 65 that the deepest value the
  wire carries would need. It is only a recursion guard for the generator. Nothing a caller sends
  is read under it.
- **`SchemaDepthCheck`** (`src/core/Tellma.Core.AspNetCore/OpenApi/SchemaDepthCheck.cs`, internal,
  check `host.openapi-depth`, review 1): registered with the document, so in Development only. At
  startup it exports every distinct web route body and answer type with the exporter, under the
  document's options and the generator's exporter settings (null-oblivious as non-nullable, an
  any-value schema written as an object), measures the node's nesting, and refuses a type past 128
  with a composition problem naming it. It costs one export per distinct route type, once.
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
  - Review 1: `A_value_as_deep_as_the_wire_carries_is_described_level_by_level` now ends in a
    polymorphic level (`PjShape`, discriminator `shape`, one derived `PjCircle`), walked through its
    `anyOf`. `A_types_schema_nests_two_levels_per_object_and_four_per_polymorphic_level_however_shallow_its_values`
    pins the per-level costs and the two boundary types. `A_body_whose_type_nests_as_deep_as_the_document_describes_is_described`
    (`PjTallest`, 63 object types ending in `int[]`, a schema exactly 128 deep) is served.
    `A_body_whose_type_nests_past_what_the_document_describes_fails_startup_in_development`
    (`PjTooTall`, 64 object types, 129) fails startup with the check's problem.
    `..._fails_the_whole_document_without_the_check` removes the check and gets a 500 for the
    document, so the check's boundary is the generator's own, to the level.
    `The_check_measures_nothing_without_the_route_model`.
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
- `ClientSyncTests` reads `result.Me` directly. The wire test (renamed
  `The_answer_writes_the_settings_bytes_as_they_are_the_envelope_as_the_callers_own_read_and_omits_the_members_it_does_not_carry`)
  compares the `me` member with the caller's own `MeAsync` read serialized under the platform's
  rules (`JsonNode.DeepEquals`), an oracle independent of the envelope (review 1, R1-2).
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
- That `SchemaDepthCheck`'s exporter settings match the generator's closely enough. The boundary
  tests pin it at 128 against 129 for plain objects and arrays; a polymorphic type is measured the
  same way but its boundary is not pinned against the generator.

## Review 1

### Findings

Counts: 0 blocking, 0 major, 2 minor.

Checked and found sound:

- **The document-only options cannot reach the wire.** I decompiled `Microsoft.AspNetCore.OpenApi`
  10.0.11. `AddOpenApiCore` registers `OpenApiSchemaService`, `OpenApiDocumentService` (twice) and
  `IDocumentProvider`, plus the `OpenApiSchemaJsonOptions` configure step. `OpenApiSchemaService`
  (primary constructor `[ServiceKey] string documentName, IOptions<JsonOptions>,
  IOptionsMonitor<OpenApiOptions>`) is the only type in the package that takes
  `IOptions<JsonOptions>`, apart from the generated `MapOpenApi` delegate. So `Register` replaces
  exactly one descriptor. The service copies the options twice: once for `GetJsonSchemaAsNode`,
  whose `PushSchemaNode` throws at `EffectiveMaxDepth`, and once for `OpenApiJsonSchemaContext`'s
  read-back. Both copies now see 128.
- **The new instance is never registered.** `IOptionsFactory<JsonOptions>.Create` returns a fresh
  instance that the `IOptions<JsonOptions>` cache never sees. It is wrapped in `Options.Create` and
  handed only to that constructor. Bodies (`ThrowOnBadRequest` binding), answers
  (`ActionAnswers.ContractJsonResult` resolves `IOptions<JsonOptions>` per request),
  `TellmaJson.Options` and the hub all keep 16. The hub is unaffected because it uses
  `JsonHubProtocolOptions.PayloadSerializerOptions`, configured separately by
  `TellmaJson.Configure` in `TellmaRealtimeServiceCollectionExtensions.cs:91`, not the HTTP options.
- **The guard.** It is reached only in Development (`TellmaWebApplicationBuilderExtensions.cs:155`),
  which is the only place the document exists. A bump that renames the options parameter's type,
  turns the descriptor into a factory, or un-keys it gets no match and fails composition. A bump
  that keeps the match but moves the export elsewhere 500s the document on the typed `me`, which
  needs 17 levels against 16, and every document test fails. `ActivatorUtilities` fills the
  `[ServiceKey] string` from the explicit `key!` argument. Any lifetime works, since `Create` runs
  per instance.
- **Wire and fingerprint.** Every serializer modifier lives in `TellmaJson.Configure`, plus the
  unknown-member counter (reads only) and the OpenAPI context, which resolves only its own schema
  types. Multilingual shaping is done in services, not in the serializer. So `MeResult` is written
  by the HTTP options exactly as `PreSerializedJson.Of` wrote it under `TellmaJson.Options`.
  `MeResult`'s members are fixed shapes (strings, enums, `SecurableSummary`, `AccessProblem`), so
  nesting it one level deeper inside the answer has no depth risk. `ContractDescriber` already
  described `PreSerializedJson<T>` as `T`, and the fingerprint `1UjYUCJx3WE2` stays in
  `fixture-routes.txt`/`gadget-routes.txt`.
- **Spec conformance.** 0025 §5.1 types `Me` as `MeResult?`, so this change restores the spec
  shape. The 0016 §3.1 deviation is flagged honestly for Ahmad. The framework already copied the
  options inside the schema service, so the actual departure is the depth alone.
- **AGENTS.md and the test list.** XML docs, inline section comments and both READMEs hold.
  Inline comments cite specs, and nothing else does. The added and changed files have no BOM and
  no CRLF. The diff does not touch `users/me`, `MeEnvelope`, preferences or the last-administrator
  guard, so it cannot cause the `UserSelfServiceTests` `Access.LastAdministrator` failure (PC-F68).
- **Tests I ran myself.** Built `Tellma.Core.AspNetCore.Tests` (0 errors). WebOpenApiTests,
  WebApiProjectionTests and UnknownMembersTests passed 80/80. In Tellma.Core.Tests,
  RouteContractsTests, StackDescriptorGoldenTests and ContractDescriberTests passed 28/28, with the
  goldens unchanged. In Tellma.Core.Abstractions.Tests, PreSerializedJsonTests and
  TellmaJsonOptionsTests passed 67/67. I did not re-run ClientSyncTests: R1-2 rests on reading the
  test, not on running it. The pc/PC-F67 push run 44982351675 was skipped, so it gives no CI
  evidence yet.

1. **R1-1 (minor) `SchemaMaxDepth`'s bound is stated for values, but schema depth follows the type.
   The worst case it names is untested.**
   - Where: `src/core/Tellma.Core.AspNetCore/OpenApi/WebOpenApi.cs:70-79` (and the README row).
   - The remark says the deepest value the wire carries needs at most 65 levels. The exporter
     inlines every (type, property) pair the first time it meets it. So the schema's depth is set
     by how deeply the CLR type nests, not by how deeply a value of it can nest.
   - A contract type may legally nest past 16 objects: a record chain with optional members whose
     values stay shallow. The wire carries it, and the realised gate resolves its `JsonTypeInfo`
     and passes it.
   - Its schema still needs two levels per object (four per polymorphic level). About 64 plain
     levels, or about 32 polymorphic ones, fail the whole `/openapi/web.json` with a 500 again.
   - The tests only reach about 33 levels: `PjDepth` is 16 plain objects. Nothing exercises the
     polymorphic arithmetic the 65 comes from.
   - Impact is low: Development only, and such types are unusual. But the constant's own
     justification is the thing reviewers were asked to check.
   - Fix:
     - State the bound in terms of type nesting (two levels per nested object member, four per
       polymorphic level), and say that 128 covers types up to about 32 polymorphic or 64 plain
       levels.
     - Add a polymorphic level, or a short polymorphic chain, to the depth fixture, so the per-level
       cost the remark relies on is pinned.

2. **R1-2 (minor) The new `me` wire assertion cannot fail.**
   - Where: `test/core/Tellma.Core.IntegrationTests/Client/ClientSyncTests.cs:227`.
   - `coldWire` is `cold` serialized under `TellmaJson.Options`. The assertion compares its `me`
     member with `cold.Me` serialized under the same options. Both sides resolve `MeResult`'s
     contract through the same resolver chain, so they are equal by construction. The only
     exception is a member-level converter on `ClientSyncResult.Me`.
   - So the claim that "the wire test also pins the `me` member as it is written under the
     platform's rules" is not backed. Suppose the envelope's JSON drifted from what `users/me`
     answers, for example through a member attribute, a context option, or a different declared
     type in the generated context. This test would stay green.
   - The wire is unchanged by construction (see above), so this is test adequacy, not a defect.
   - Fix: compare the `me` member with an independent oracle. Either use the caller's own `me` read
     (`MeAsync(admin)`, serialized under `TellmaJson.Options`, with the time-dependent members
     normalized or taken from the same clock), or pin a short literal of the member's shape.
     Alternatively, drop the line and stop claiming it in the notes.

### Responses

- R1-1: fixed in the "PC-F67: review 1 fixes" commit (child of 0dabb9cc), per the orchestrator's
  ruling. `SchemaMaxDepth`'s remark and the README row now state the bound in terms of type nesting
  (two levels per nested object member, one per array, four per polymorphic level; 128 fits about
  63 plain objects or 31 polymorphic levels). The depth fixture's innermost level is now
  polymorphic (`PjShape`/`PjCircle`), so the 16-object wire tests and the document walk cross it.
  The cheap Development startup check was practical and is added: `SchemaDepthCheck`
  (`host.openapi-depth`) measures every web route's body and answer type with the exporter and
  fails startup past 128. Tests pin the per-level costs, a type exactly at 128 served, one at 129
  refused at startup, and the same type with the check removed 500ing the document, so the check
  and the generator agree to the level.
- R1-2: fixed in the "PC-F67: review 1 fixes" commit (child of 0dabb9cc). The `me` assertion now
  compares against the caller's own `users/me` read (`MeAsync(admin)`, serialized under
  `TellmaJson.Options`) with `JsonNode.DeepEquals`. No normalization was needed: the existing
  envelope test already shows the two reads agree member for member, `Access.ValidUntil` included.
  ClientSyncTests 13/13 locally.

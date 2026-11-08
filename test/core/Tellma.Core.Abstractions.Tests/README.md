# Tellma.Core.Abstractions.Tests

Contract-type tests: the value objects that carry meaning across a wire, and the shared checks every
implementation of a contract relies on.

| Folder | Covers |
|---|---|
| `Email/` | `EmailCorrelation` — canonical-form round trips (colons inside the reference, unicode, a null tenant, the deployment wire envelope), the rejection table, and constructor validation. `EmailMessageValidation` — exactly what counts as structurally invalid, which is what every transport rejects on its own rather than failing a batch over. |
| `Client/` | `ClientAssetContext` — a section captures exactly one complete JSON value, one name per chunk, camelCase names; generated-file paths are plain relative paths, unique without regard to case; a section escapes text as the platform JSON rules do. |
| `Composition/` | `FeatureDeclaration` and `FeatureContribution` record exactly the items and bindings the platform realises (the calendar sugar included); `CompositionProblem`, `TellmaCompositionException` (one message line per problem, identical newlines on every OS) and `DeploymentVersions` validate their components; `TellmaRoutes` lists exactly the reserved words, read-only. |
| `Hosting/` | `DeploymentIdentity` — id composition per environment, and its refusal to compose an id that would collide or corrupt a wire envelope. |
| `Api/` | `TellmaJson` — the pinned options (rules, converter order, resolver chain, read-only), a host's options configured identically and idempotently, and the member rules (camelCase, nulls omitted, unknown members skipped, duplicates refused, depth 16, Arabic and Amharic unescaped while HTML-sensitive characters stay escaped); enums reading back only as defined members (lists only on flags enums, no numbers, no undeclared bits, as values and keys); every scalar of the wire round-tripping in its one form, with the forms each refuses (quoted ints, `NaN`, exponent strings, unrepresentable decimals, offsets on a date-time), dictionary keys of every custom scalar included; the request and result records' bindings and shapes, required members refused as binding errors; `WireIds`; the projection attributes; the pinned header and telemetry names. |
| `Crud/` | The stack annotations: `StackOperations` values, `[Stack]` defaults and inheritance, the action attributes' defaults and that an override stays an action, details extras unioned over base classes, `[ApiRoute]` and `[EntityRef]`. |
| `Validation/` | `ValidationPath` — bracket rendering, value equality as a dictionary key, immutability, segment rules, a dotted key as one segment; `ValidationErrors` — order, argument copies, expression-named members, refusal of a member the row does not own, concurrent adds; the pinned validation codes. |
| `Errors/` | The closed exception set — only this assembly extends it and every member is sealed; each member's code, display arguments and refusals; bounded log messages; the stale-context outcome naming its stale inputs, or none for the write guard; the pinned limit names; coded errors and warnings comparing by content. |
| `Caching/` | `VersionTag` — the wire form, value equality, `None`, fresh tags; the tag names and `ForEntity`; snapshots answering `None` for an absent name, ordinal names, copied tags; the cache limits' defaults and validation; the client shapes' format versions; the guard row; the pinned cache and localization telemetry names. |
| `Settings/` | `SettingKey<T>` — the default serialized under the platform's JSON rules or the key's own contract, the label key, category and group rules, reading stored values back (numbers for longs, defined enum names only, null only for a null default); `TenantSettings` — stored values and defaults, invalid and refused values reported once and read as the default, values read once and remembered, secrets decrypted on first read and every way one can be unreadable, `Today` on the zone's wall clock with a fake clock, copies and equality. |
| `Access/` | The endpoint metadata records. |
| `Notifications/` | The notification type and channel descriptors' defaults and their client-metadata JSON. |
| `Repository/` | A guard that every `.csproj` under `src/` and `test/` appears in `Tellma.slnx`, since an unlisted project is silently never built, tested, or format-checked. |

## Running

```bash
dotnet test test/core/Tellma.Core.Abstractions.Tests
```

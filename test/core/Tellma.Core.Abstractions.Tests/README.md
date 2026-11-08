# Tellma.Core.Abstractions.Tests

Contract-type tests: the value objects that carry meaning across a wire, and the shared checks every
implementation of a contract relies on.

| Folder | Covers |
|---|---|
| `Email/` | `EmailCorrelation` — canonical-form round trips (colons inside the reference, unicode, a null tenant, the deployment wire envelope), the rejection table, and constructor validation. `EmailMessageValidation` — exactly what counts as structurally invalid, which is what every transport rejects on its own rather than failing a batch over. |
| `Client/` | `ClientAssetContext` — a section captures exactly one complete JSON value, one name per chunk, camelCase names; generated-file paths are plain relative paths, unique without regard to case. |
| `Composition/` | `FeatureDeclaration` and `FeatureContribution` record exactly the items and bindings the platform realises; `CompositionProblem`, `TellmaCompositionException` (one message line per problem, identical newlines on every OS) and `DeploymentVersions` validate their components; `TellmaRoutes` lists exactly the reserved words, read-only. |
| `Hosting/` | `DeploymentIdentity` — id composition per environment, and its refusal to compose an id that would collide or corrupt a wire envelope. |
| `Repository/` | A guard that every `.csproj` under `src/` and `test/` appears in `Tellma.slnx`, since an unlisted project is silently never built, tested, or format-checked. |

## Running

```bash
dotnet test test/core/Tellma.Core.Abstractions.Tests
```

# Tellma.Core.Abstractions.Tests

Contract-type tests: the value objects that carry meaning across a wire, and the shared checks every
implementation of a contract relies on.

| Folder | Covers |
|---|---|
| `Email/` | `EmailCorrelation` — canonical-form round trips (colons inside the reference, unicode, a null tenant, the deployment wire envelope), the rejection table, and constructor validation. `EmailMessageValidation` — exactly what counts as structurally invalid, which is what every transport rejects on its own rather than failing a batch over. |
| `Hosting/` | `DeploymentIdentity` — id composition per environment, and its refusal to compose an id that would collide or corrupt a wire envelope. |
| `Repository/` | A guard that every `.csproj` under `src/` and `test/` appears in `Tellma.slnx`, since an unlisted project is silently never built, tested, or format-checked. |

## Running

```bash
dotnet test test/core/Tellma.Core.Abstractions.Tests
```

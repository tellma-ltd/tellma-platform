# Tellma.Core.Tests

Unit tests for `Tellma.Core`. No database, no network: compositions run against in-memory
configuration, and the startup gate runs under a real generic host.

| Folder | Covers |
|---|---|
| `Composition/` | The graph gate (every problem in one exception, one message line each, with its fix; unknown requirements, duplicate names, cycles and their paths, a feature type added twice, options bound twice, slug and feature-name grammars, unknown item kinds, nested features); realisation (topological order, a dependent feature replacing a required feature's service, collection services, item validation, the deployment identity and versions, the gate inserted first); host-free validation; the startup gate under a real host (every problem at once, before any other hosted service starts; a throwing check; one shared scope; the infrastructure filter); the feature graph; the C# renderings problem texts use. |
| `Composition/Checks/` | Each built-in startup check, through a realised composition. |
| `Repository/` | `taxonomy.json` at the repository root: every distribution folder registered, every slug well formed, no reserved slug taken but the reference distribution's. |
| `Infrastructure/` | Test features, items, checks and the helpers that compose them. |

## Running

```bash
dotnet test test/core/Tellma.Core.Tests
```

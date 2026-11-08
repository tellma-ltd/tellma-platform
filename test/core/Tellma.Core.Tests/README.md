# Tellma.Core.Tests

Unit tests for `Tellma.Core`. No database, no network: compositions run against in-memory
configuration, the startup gate runs under a real generic host, and the file-system blob store runs
in a fresh temporary directory per case.

| Folder | Covers |
|---|---|
| `Composition/` | The graph gate (every problem in one exception, one message line each, with its fix; unknown requirements, duplicate names, cycles and their paths, a feature type added twice, options bound twice, slug and feature-name grammars, unknown item kinds, nested features); realisation (topological order, a dependent feature replacing a required feature's service directly or transitively, two unrelated features registering one service refused, collection services including the framework's multi-registration types, the options seam refusing a second path, item validation, the deployment identity and versions, the gate inserted first); host-free validation; the startup gate under a real host (every problem at once, before any other hosted service starts; a throwing check; a multi-line problem kept to one line; an uncreatable check beside the built-in ones; one shared scope; the infrastructure filter); the feature graph; the C# renderings problem texts use. |
| `Composition/Checks/` | Each built-in startup check, through a realised composition. |
| `Blobs/` | The blob name grammar (kinds, reserved device names, variants, keys, names, listing prefixes, traversal strings); the kind presets and the blob-reference declaration's policy; content-type determination over fixture byte prefixes (signatures, OOXML and plain zip, text with and without NUL, executables, HTML and SVG in their disguises); file-name sanitisation; the shared blob-store conformance suite against the file-system store in a temporary directory; the file-system store's layout, part files, cancellation, shared reads and deletes, stray files, root rules and log events; `AddFileSystemBlobStore` (binding, the caller's action, the Development default, validation, idempotence); the store instruments' tag values and the pinned telemetry names. |
| `Repository/` | `taxonomy.json` at the repository root: every distribution folder registered, every slug well formed, no reserved slug taken but the reference distribution's. |
| `Infrastructure/` | Test features, items, checks and the helpers that compose them. |

## Running

```bash
dotnet test test/core/Tellma.Core.Tests
```

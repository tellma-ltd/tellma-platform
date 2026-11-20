# Tellma.Module.Gl.Tests

Unit tests for the GL module's two packages, `Tellma.Module.Gl.Abstractions` and
`Tellma.Module.Gl`. Nothing here opens a database: the centers' rules run over a scripted loader,
and the sample-centers step over a recording pipeline. The centers' behaviour against SQL Server,
their round-trip budgets and the stack conformance suite closed over them live in
`Tellma.Core.IntegrationTests`, whose fixture database composes the module.

| Folder | Covers |
|---|---|
| `Rules/` | `CenterService`'s rules over validation contexts and a scripted loader: a parent in the payload judged by its payload type and never loaded, a stored parent loaded once by id and judged only when the row is new or moves to it, a parent the pipeline already refused or that does not exist left to the platform, a self-parent left to the cycle check; a type change to one that does not group refused while the center keeps a stored child or the payload puts one under it, and accepted when it has none or every stored child moves away in the payload, a switch between grouping types always accepted; a delete refused while the center keeps a child the delete does not name (a skipped level included), accepted when the delete names the whole subtree or removes descendants, which loads nothing, and the targeted-ancestor walk bounded by the targets on a cyclic input. |
| `Samples/` | The default sample tree (version one, eight centers, one root, unique codes, children only under grouping centers, English and Arabic names) and the `gl.sample-centers` step over a recording pipeline: its name, order and version; a replacement list written instead of the default; stored centers kept under their ids and new ones under temporary ids; the provisioning log event; nothing read or written for an empty list or outside Development; a list refused for an unknown or self parent, a duplicate code, a blank code or name, or more than the step's ceiling; the code filter's chunking; and the names placed by the tenant's languages. |
| `Packaging/` | Neither package's dependency closure reaches the runtime, Entity Framework, SqlClient or ASP.NET Core; the contract package's closure is the platform's contract alone; and the feature is named for the module, requires nothing and contributes exactly its stack, its provisioning step and its default sample centers. |
| `Localization/` | The resource audit over the module's strings, against a composition of the GL feature and the module's source literals, with the keys the browser package will use listed as pending; and the strings shipping in English and Arabic with the same keys under the module's base name. |

## Running

```bash
dotnet test test/module/gl/Tellma.Module.Gl.Tests
```

## License

Apache-2.0

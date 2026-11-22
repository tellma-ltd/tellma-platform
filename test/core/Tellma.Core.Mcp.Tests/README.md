# Tellma.Core.Mcp.Tests

Unit tests of the tenant MCP server's parts that need no host: what an agent's arguments are
refused for, how results are rendered under the character cap, and the server's static shape. The
endpoint itself, its bearer challenges and each read tool against a real tenant are driven end to
end by `McpTests` in `distributions/acme/test/Tellma.Distro.Acme.IntegrationTests`, where Acme's web
host composes the server.

| Folder | Covers |
|---|---|
| `Tools/` | The argument reader: an undeclared argument refused by name, JSON null and blank strings read as absent, integers outside their range, ids as integers or digit strings, the closed sets matched ignoring case, booleans, and parameter values that nest or repeat a name in another case. |
| `Rendering/` | A Markdown table with escaped cells, empty absent values and the count; JSON objects with the full-page hint; a page cut at a row boundary under the cap with the skip that pages on; records with their collections windowed under `$collections` and an absent id reported; a first record whose lines pass the cap keeping its header while the rest are named by id; one collection's window. |
| `Hosting/` | The five read tools' annotations and closed 2020-12 schemas, `whoami` alone with an output schema; the list's fixed order; the startup check refusing the `tellma_` prefix on a distribution's tool and a name taken twice; the options' validation; the origin rule; the catalogue's search; the instructions naming a sandbox, and naming no tenant to a caller not admitted; the syntax resource. |

## Running

```bash
dotnet test test/core/Tellma.Core.Mcp.Tests
```

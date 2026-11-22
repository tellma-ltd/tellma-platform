# Tellma.Core.Mcp.Tests

Unit tests of the tenant MCP server: what an agent's arguments are refused for, how results are
rendered under the character cap, the server's static shape, and, over the in-memory web test host
(`Tellma.Testing.WebHost`) with the server composed, the endpoint's bearer rules, the front door's
refusals and what an agent is told of the platform's own entities. Each read tool against a real
tenant is driven end to end by `McpTests` in
`distributions/acme/test/Tellma.Distro.Acme.IntegrationTests`, where Acme's web host composes the
server.

| Folder | Covers |
|---|---|
| `Tools/` | The argument reader: an undeclared argument refused by name, JSON null and blank strings read as absent, integers outside their range, ids as integers or digit strings, the closed sets matched ignoring case, booleans, and parameter values that nest or repeat a name in another case. |
| `Rendering/` | Rows (`RowRenderingTests`): a Markdown table with escaped cells, empty absent values and the count; JSON objects with the full-page hint; a page cut at a row boundary under the cap with the skip that pages on. Records (`EntityRenderingTests`): collections windowed under `$collections` at every depth and an absent id reported; a first record whose lines pass the cap keeping its header while the rest are named by id; a record of ten thousand lines writing only the lines its window keeps; the bounded writer stopping once its room is spent; the trailing lines under the cap however many ids they name; one collection's window. |
| `Hosting/` | The five read tools' annotations and closed 2020-12 schemas, `whoami` alone with an output schema; the list's fixed order; the startup check refusing the `tellma_` prefix on a distribution's tool and a name taken twice; the options' validation; the origin rule; the catalogue's search; the instructions naming a sandbox, and naming no tenant to a caller not admitted; the syntax resource. Over the host (`McpHost`, `EndpointTests`): the bearer events inert on the public API; a token naming a second audience refused; a member served with the tenant's name and a distribution's tools listed; a non-member, a deactivated member and a service account refused with no tenant named. The call filter's own rules (`ToolCallFilterTests`): denied versus error, the request's cancellation alone unanswered, a problem's text; and a distribution tool's failures mapped as the web surface maps them. |
| `Describe/` | Over the host's real registry: entity names matched ignoring case, a hidden stack and its child entities answered as unknown and children carrying their owner's exposure; the columns the tenant's multilingual shape leaves out; sensitive operations, sensitive actions and hidden actions never described; whoami's matrix without sensitive grants or hidden entities, listing the filtered ones. |

## Running

```bash
dotnet test test/core/Tellma.Core.Mcp.Tests
```

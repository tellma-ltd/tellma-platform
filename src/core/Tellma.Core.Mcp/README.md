# Tellma.Core.Mcp

The Tellma Tenant MCP server: one stateless Streamable HTTP MCP endpoint per tenant at
`/{tenantId}/mcp`, built on the official MCP C# SDK, through which an agent (Claude, ChatGPT, Cursor,
Codex) reads a tenant's data under the signed-in user's own permissions. A distribution's web host
composes it from its `composeWeb` delegate; the migrator never does, because the server adds no
entity, securable or table.

```csharp
builder.AddTellma(AcmeComposition.Slug, AcmeComposition.Compose, tellma =>
{
    tellma.AddMcp();                                                   // the platform's tools
    // tellma.AddMcp(m => m.ToolTypes.Add(typeof(CreditExposureTools)));  // plus a distribution's own
});
```

`MapTellma` maps the endpoint among the tenant surfaces, and the distribution information document
announces it as the `mcp` surface.

## What a request goes through

1. **The bearer.** The platform's `Tellma.Bearer` scheme validates the token. Its audience must be
   exactly the tenant's resource, `{PublicOrigin}/{tenantId}/mcp`, so a token for another tenant or
   for the public API is refused. A missing or invalid token answers 401 with
   `WWW-Authenticate: Bearer resource_metadata="…"`, pointing the client at the tenant's
   protected-resource metadata, from which it discovers the identity server; a token without the
   `tellma_api` scope answers 403 `insufficient_scope`.
2. **The origin.** A request without `Origin` (a native client) passes; a browser origin must be in
   `Tellma:Mcp:AllowedOrigins` (`https://claude.ai` and `https://chatgpt.com` by default), or the
   request is refused with 403. The endpoint answers no CORS preflight.
3. **The tenant.** The tenant middleware binds the tenant and the principal as for any tenant
   surface: an unknown tenant is 404.
4. **The tool call.** The request filter runs the tenant's front door (the tenant's state, the
   caller's membership, the request context) once per request, so a caller who is no active member
   is refused as the web surface refuses them; it also refuses a service account, and runs
   the tool in the request's cultures. Messages reach the model in `Tellma:Mcp:MessageLanguage`
   (English by default) with Gregorian dates; display names stay in the user's content language.
   Whatever a tool lets escape is mapped exactly as the web surface maps it and answered as an
   `isError` result carrying the same `code`, `detail`, `errors` and `errorDetails`.

The rate limit (`ToolCallsPerMinutePerUser` per tenant and subject) and the per-call timeout
(`ToolTimeout`) are the host's own `mcp` policies, fed from these options.

## The tools

The tool list is static and the same for every caller; permissions are evaluated on every call and
reported by `tellma_whoami`, never by hiding tools. Every input schema is a closed JSON Schema
2020-12 object, and every tool declares `openWorldHint: false`.

| Tool | Does |
|---|---|
| `tellma_whoami` | The caller, the tenant (with its category: live or sandbox), the permission matrix per entity, and the version tags. The only tool with an output schema and structured content. |
| `tellma_describe` | The catalogue of entities (narrowed by `search`), or one entity's columns, navigations and the columns reachable through them, child collections, operations, actions with their argument schemas, and example filters. Entity descriptions are cached per tenant, entity and language under the tenant's settings tag. |
| `tellma_query` | Rows of a stack or a child entity through the stack's service or the child query service, as a Markdown table or JSON objects, truncated row by row under `MaxToolResultChars`. |
| `tellma_aggregate` | Grouped totals computed in the database. |
| `tellma_get` | Whole records by id, each child collection cut to a window at every depth before any JSON is written, or one window of one record's collection. Each piece is written bounded by the room left, so the work follows what the result shows, never what was loaded, and the trailing lines always fit under `MaxToolResultChars`. |

`tellma_save`, `tellma_delete` and `tellma_action`, and the reserved names of `McpToolNames`, are not
served yet. A distribution's tool may not take the `tellma_` prefix; a startup check refuses one that
does.

The resource `tellma://queryex/syntax` is the Queryex reference in Markdown
([Resources/queryex-syntax.md](Resources/queryex-syntax.md)).

## Layout

| Folder | Holds |
|---|---|
| `Hosting/` | The route and its origin check, the server's identity and instructions, the front door's per-request decision, the request filters and the tool-name check. |
| `Authentication/` | The bearer events that check the tenant audience (exactly one `aud` value, the tenant's resource) and write the MCP challenges. |
| `Tools/` | The tools, their argument reader and schema builder, and the entities they reach. |
| `Describe/` | The catalogue and entity descriptions, and their cache. |
| `Rendering/` | The row and record renderings under the character cap, the record shapes that name each collection to window, and the bounded JSON writer that stops once its room is spent. |
| `Observability/` | The `Tellma.Core.Mcp` meter (`tellma.mcp.tool.calls`, `tellma.mcp.tool.duration`, `tellma.mcp.result.chars`) and the log messages. |
| `Resources/` | The Queryex syntax resource. |

## Options (`Tellma:Mcp`)

| Option | Default |
|---|---|
| `MaxToolResultChars` | 60,000 |
| `DefaultTop` / `MaxTop` | 50 / 500 |
| `DefaultChildTop` / `MaxChildTop` | 50 / 500 |
| `MaxIdsPerCall` | 100 |
| `MessageLanguage` | `en`; empty for the user's own |
| `ToolCallsPerMinutePerUser` | 120 |
| `ToolTimeout` | 60 seconds |
| `ConfirmationLifetime` | 5 minutes |
| `ListTtlMs` | 300,000 |
| `AllowedOrigins` | `https://claude.ai`, `https://chatgpt.com` |
| `ToolTypes` | none; set in code only |

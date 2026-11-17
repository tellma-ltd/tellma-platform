# @tellma/core-ui-mcp

Generated, version-exact component documentation plus a stdio MCP server
answering against it.

- `generated/components.json` — schema-validated record of every component,
  directive and documented service (`TmToasts`, `TmConfirm`, `TmModal` and
  the other root services, which carry no selector): inputs/outputs with
  types and defaults, public members, slots, CSS tokens read, the a11y
  model, canonical usage examples (template examples and a service's
  TypeScript ones), and the matching test harness (`null` for an entry
  without one). Extracted from source by the docs pipeline; never
  hand-written.
- `generated/llms.txt` — the same content as a single flat digest a coding
  agent (or a person) can load in one read.
- `tellma-core-ui-mcp` (bin) — a stdio MCP server exposing `list`,
  `describe`, and `example` tools over the same data.

## Wiring a consuming repo

1. Install this package pinned to the same version as `@tellma/core-ui`.
2. Register the server (e.g. `.mcp.json` for Claude Code):

   ```json
   { "mcpServers": { "tellma-core-ui": { "command": "npx", "args": ["tellma-core-ui-mcp"] } } }
   ```

3. Point agents that don't speak MCP at
   `node_modules/@tellma/core-ui-mcp/generated/llms.txt`.

Because the artifacts ship inside the package, answers always describe the
exact installed version — no repository browsing, no version skew.

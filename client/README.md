# Tellma client workspace

The Angular workspace for the `@tellma/*` npm package family (spec
`docs/specs/0002-component-library-foundation.md`). Angular 22, pnpm, signal-first, zoneless.

| Package | Folder |
|---|---|
| `@tellma/core-ui` (+ `/contracts`, `/input`, `/checkbox`, `/form-field`, `/select`) | `projects/core/tellma-core-ui` |
| `@tellma/core-ui-tokens` | `projects/core/tellma-core-ui-tokens` |
| `@tellma/core-ui-testing` | `projects/core/tellma-core-ui-testing` |
| `@tellma/core-ui-mcp` | `projects/core/tellma-core-ui-mcp` |
| `@tellma/locale-ar` | `projects/locale/tellma-locale-ar` |
| showcase (internal dev host, never published) | `projects/internal/showcase` |

## Workflow

```bash
pnpm install
pnpm exec playwright install chromium   # once per machine (unit tests run in real Chromium too)

pnpm run build          # tokens gates + CSS, then every package in dependency order
pnpm run test           # all unit suites (vitest via @angular/build:unit-test)
pnpm run e2e            # Playwright behavioral/a11y/RTL suite against the showcase
pnpm run lint           # ESLint (tm- rules, contracts boundary) + stylelint (token sizing)
pnpm run typecheck      # the full core-ui program incl. files no build compiles (examples)
pnpm run lint:test      # the custom lint rules' own unit tests + MCP smoke tests
pnpm run tokens:check   # token schema + WCAG-contrast + completeness gates
pnpm run api:check      # public-API goldens (client/api/*.api.md)
pnpm run api:approve    # accept an INTENDED public-API change (commit the diff)
pnpm run docs:build     # components.json (schema-validated) + llms.txt for the MCP package
pnpm run size:check     # per-entry-point gzipped self-weight vs the §8 ceilings
pnpm start              # showcase dev server (the component showcase + e2e target)
```

**Changing a public API?** `api:check` fails on any drift; review the surface
change, run `api:approve`, and commit the golden diff alongside the code.

**No hardcoded ports.** Every server binds to a port from the worktree's
`.dev-ports.local` (once `dotnet tellma setup-worktree` exists) or an
OS-assigned free port — see `scripts/ports.mjs`. Two worktrees always run in
parallel without collisions. Never pass a literal port in configs or scripts.

Each project folder carries its own README describing what it ships.

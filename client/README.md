# Tellma client workspace

The Angular workspace for the `@tellma/*` npm package family (spec
`docs/specs/0002-component-library-foundation.md`). Angular 22, pnpm, signal-first, zoneless.

It is a member of the one pnpm workspace rooted at the repository root: `pnpm-workspace.yaml`
there lists this folder, the Node packages inside it and every distribution's Client project;
`pnpm-lock.yaml` beside it is the only lockfile; and every external version comes from the
workspace's `catalog` (each dependency here is `"catalog:"`). The root `package.json` forwards
every script below, so each runs from the repository root or from here.

| Package | Folder |
|---|---|
| `@tellma/core` (+ `/private`) | `projects/core/tellma-core` |
| `@tellma/core-testing` (+ `/e2e`) | `projects/core/tellma-core-testing` |
| `@tellma/core-tools` (Node: `tellma-client`, the ESLint, stylelint and tsconfig presets) | `projects/core/tellma-core-tools` |
| `@tellma/core-ui` (+ `/contracts`, `/l10n`, `/calendar-umalqura`, `/calendar-ethiopic`, `/input`, `/number`, `/date-picker`, `/entity-picker`, `/checkbox`, `/form-field`, `/select`, `/button`, `/tabs`, `/modal`, `/popover`, `/tooltip`, `/alert`, `/image`, `/files`, `/file-preview`, `/menu`, `/grid`, `/tree-grid`, `/grid-engine`, `/spinner`, `/private`) | `projects/core/tellma-core-ui` |
| `@tellma/core-ui-tokens` | `projects/core/tellma-core-ui-tokens` |
| `@tellma/core-ui-testing` | `projects/core/tellma-core-ui-testing` |
| `@tellma/core-ui-mcp` | `projects/core/tellma-core-ui-mcp` |
| `@tellma/locale-ar` | `projects/locale/tellma-locale-ar` |
| showcase (internal dev host, never published) | `projects/internal/showcase` |

## Workflow

```bash
pnpm install --frozen-lockfile          # at the repository root (or here: pnpm installs the whole workspace)
pnpm exec playwright install chromium   # once per machine (unit tests run in real Chromium too)

pnpm run build          # tokens gates + CSS, then every package in dependency order
pnpm run test           # all unit suites (vitest via @angular/build:unit-test), test-* targets in runs of their own
pnpm run e2e            # Playwright behavioral/a11y/RTL suite against the showcase
pnpm run lint           # ESLint (tm- rules, entry-point boundaries, decimal rules) + stylelint (token sizing, breakpoints)
pnpm run typecheck      # the core, core-testing, core-tools and core-ui programs incl. files no build compiles (examples, specs)
pnpm run lint:test      # the custom lint rules' own unit tests, the core-tools specs + MCP smoke tests
pnpm run tokens:check   # token schema + missing-ref gate (contrast is axe's job in e2e)
pnpm run api:check      # public-API goldens (client/api/*.api.md)
pnpm run api:approve    # accept an INTENDED public-API change (commit the diff)
pnpm run docs:build     # components.json (schema-validated) + llms.txt for the MCP package
pnpm run size:check     # gzipped self-weight vs each package's "tellma".budgetsInKb ceilings
pnpm start              # showcase dev server (the component showcase + e2e target)
```

`lint` and `lint:test` first compile `@tellma/core-tools` (`tsc -b`, incremental): the workspace's
ESLint and stylelint configs load its built presets, as a distribution's would.

**Changing a public API?** `api:check` fails on any drift; review the surface
change, run `api:approve`, and commit the golden diff alongside the code.

**No hardcoded ports.** Every server binds to a port from the worktree's
`.dev-ports.local` (once `dotnet tellma setup-worktree` exists) or an
OS-assigned free port — see `scripts/ports.mjs`. Two worktrees always run in
parallel without collisions. Never pass a literal port in configs or scripts.

Each project folder carries its own README describing what it ships.

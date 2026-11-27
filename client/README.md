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
pnpm run typecheck:emitted # core and core-testing again, against the entity declarations the platform alone emits
pnpm run lint:test      # the custom lint rules' own unit tests, the core-tools specs, MCP smoke tests + workspace checks
pnpm run tokens:check   # token schema + missing-ref gate (contrast is axe's job in e2e)
pnpm run api:check      # public-API goldens (client/api/*.api.md)
pnpm run api:approve    # accept an INTENDED public-API change (commit the diff)
pnpm run docs:build     # components.json (schema-validated) + llms.txt for the MCP package
pnpm run size:check     # linked, gzipped production self-weight vs "tellma".budgetsInKb ceilings and the platform caps
pnpm start              # showcase dev server (the component showcase + e2e target)
```

`typecheck` compiles `@tellma/core` with its entity registries (`TmEntityTypes`, `TmEntityShapes`)
empty, so every entity row is an open JSON object; a distribution's build compiles the same sources
beside its emitted `tellma-entities.d.ts`, which closes each row to its wire interface.
`typecheck:emitted` (`tools/typecheck/emitted-entities.mts`) compiles core and core-testing once
more beside the declarations the platform alone emits, so code that indexes a row with any string,
or passes an open row where a closed one is due, fails here rather than in a distribution. A typed
probe beside them fails the check if the declarations stop augmenting core's registries. Those
declarations are the server's output, kept as text in
`tools/typecheck/fixtures/tellma-entities.d.ts.txt` and served to the compiler from memory (on disk
as a `.d.ts`, they would augment `@tellma/core` in every program the workspace's tools build). The
server's unit suite fails when the text no longer matches the platform's model. To re-record it,
run `dotnet test test/core/Tellma.Core.Tests --filter "FullyQualifiedName~ClientEntityFixturesTests"`
from the repository root with `TELLMA_UPDATE_GOLDENS=1` in the environment, review the diff, and
run the suite again without the variable.

`lint` and `lint:test` first compile `@tellma/core-tools` (`tsc -b`, incremental): the workspace's
ESLint and stylelint configs load its built presets, as a distribution's would.

Every unit test target in `angular.json` names `vitest-base.config.mjs` as its `runnerConfig`, and
`test` refuses one that does not, or that sets the builder's `reporters` option (which would drop
the file's reporters) in its options or a configuration. The file re-exports `@tellma/core-tools`'
Vitest preset, whose watchdog ends a run that makes no progress for 5 minutes, or a Vitest that has
not finished closing 2 minutes after its run, with exit code 1 and a report of what was pending: the unfinished files, tests and
hooks, each browser session's page, frames and open requests, and the dependency optimizer. The
preset also restores every `vi.spyOn` spy and `vi.stubGlobal` or `vi.stubEnv` stub before each
test: a project's files share one page per worker, and a spy one file left on a prototype would
otherwise reach whichever files the run happens to put after it in that page.
`lint:test` then runs in two groups (`tools/vitest.config.mts`): every other spec file in
parallel, then the core-tools fixture build spec alone, so its real `ng build` never competes
with the other specs for the machine.

Among them, `tools/tests/vitest-instances.spec.mts` checks the install itself: in every workspace
package whose `angular.json` has an `@angular/build:unit-test` target (this workspace and each
distribution's Client project), Angular's builder and the browser provider must reach one copy of
Vitest and one of Vite. pnpm links a copy of a package for each set of peers it resolves, and Vite
takes sass as an optional peer, so a second sass in the graph once left Acme's unit run on two
copies of each. The `sass` override in `pnpm-workspace.yaml` keeps the one `@angular/build` pins,
and the check also fails when the two differ, so raise the override with `@angular/build`.

**Changing a public API?** `api:check` fails on any drift; review the surface
change, run `api:approve`, and commit the golden diff alongside the code. The
reports list every union's members in one canonical order
(`tools/api/union-order.mjs`): TypeScript prints an inferred union in the order
the compiler first met its member types, which differs between a clean build,
a warm `.angular/cache` and a single-project `ng build`, so without the sort the
same API could approve to different goldens.

**No hardcoded ports.** Every server binds to a port from the worktree's
`.dev-ports.local` (once `dotnet tellma setup-worktree` exists) or an
OS-assigned free port — see `scripts/ports.mjs`. Two worktrees always run in
parallel without collisions. Never pass a literal port in configs or scripts.

Each project folder carries its own README describing what it ships.

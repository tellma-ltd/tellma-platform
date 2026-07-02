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
| sandbox (internal dev host, never published) | `projects/internal/sandbox` |

## Workflow

```bash
pnpm install
pnpm run build       # all packages in dependency order
pnpm run test        # unit tests (vitest via @angular/build:unit-test)
pnpm run e2e         # Playwright behavioral/a11y suite against the sandbox
pnpm start           # sandbox dev server
pnpm run storybook   # Storybook dev server
```

**No hardcoded ports.** Every server binds to a port from the worktree's
`.dev-ports.local` (once `dotnet tellma setup-worktree` exists) or an
OS-assigned free port — see `scripts/ports.mjs`. Two worktrees always run in
parallel without collisions. Never pass a literal port in configs or scripts.

## Storybook under Angular 22 — decision record (stage-3 spike, 2026-07-01)

**Verdict: PASS with workarounds.** `@storybook/angular@10.4.6` declares
`@angular/core >=18 <22`, so peer ranges are overridden in
`pnpm-workspace.yaml` (`peerDependencyRules.allowedVersions`). Two real
incompatibilities surfaced and are worked around there and in `angular.json`:

1. **Duplicate peer-keyed webpack instances** (Storybook's vs
   `@angular-devkit/build-angular`'s) crash with `The 'compilation' argument
   must be an instance of Compilation`. Fixed by pnpm `overrides` pinning one
   `webpack` + one `postcss` so the instances converge.
2. **Double sourcemap emission** (Storybook and the v22 builder both inject
   `SourceMapDevToolPlugin`) fails the build with asset-filename conflicts.
   Fixed by a dedicated `sandbox:build:storybook` configuration with
   `sourceMap: false`; Storybook targets it via `browserTarget`.

Verified: `ng run sandbox:build-storybook` succeeds; the probe-select story
renders and is fully interactive (open, option-click commit, close) in the
static build. Revisit and drop the overrides when Storybook ships official
Angular 22 support.

## Stage-3 spike findings (spec §3.4 composition)

- All 11 probe specs pass: clipping escape (`usePopover:'inline'`), flip-up
  (with the `updatePosition()`-on-`(attach)` **macrotask** fix — required),
  the trigger→listbox→option ARIA id chain across the portal, real-mouse
  option/outside/trigger clicks, keyboard commit + focus retention, Esc.
- **angular/components#32504 did NOT bite** this composition — no explicit
  pointer-path mitigation needed; the mouse specs stay in the suite as the
  regression guard.
- **Component hosts must be `display: block`.** An inline host wrapping the
  block trigger hit-tests ABOVE the trigger in Chromium, so real user clicks
  land on the host and never reach the trigger.

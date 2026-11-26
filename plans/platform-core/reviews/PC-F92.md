# PC-F92: Core-ui kbd out of the primary's first frame

## Implementer notes

### The cause, confirmed

On a clean build at the branch point (e83e61c5), Acme's `main-*.js` holds `tm-kbd`'s code (its
`tm-kbd__key` template and styles), and Acme initialJs is **223.14 KB in 4 files**. The primary
reaches `@tellma/core-ui/kbd` for the shortcut registry's helpers (`tm-shortcut.ts`'s
`tmFormatKeys`, `tm-shortcuts.ts`'s `tmIsApplePlatform` and `tmKeysMatch`), and the eager
`/shell` reaches it for the app bar's `tmFormatKeys`. One FESM is one module to the app's
bundler, so once a lazy chunk (the menu rows, the Shortcuts dialog) makes `TmKbd` live, its code
is placed in a chunk the first frame loads (D-281's mechanism).

The guard was shown failing first: with `@tellma/core-ui/kbd` added to the `"."`, `./shell` and
`./attention` `staticGraphExcludes` of `@tellma/core` and nothing else changed, `size:check`
failed on all three (`tellma-core.mjs -> tellma-core-tellma-core-….mjs -> tellma-core-ui-kbd.mjs`,
and the shell and attention chains through it).

### What was built

- **`@tellma/core-ui/keys`**, a new secondary entry point (`keys/ng-package.json`,
  `keys/public-api.ts`). `tm-keys.ts` and `tm-keys.spec.ts` move there unchanged (`git mv`). It
  exports exactly the grammar `./kbd` exported: `TmKeys`, `tmFormatKeys`, `tmKeysText`,
  `tmKeysMatch`, `tmAriaKeyShortcuts`, `tmIsApplePlatform`. API golden
  `client/api/core-ui.keys.api.md` is new.
- **`@tellma/core-ui/kbd`** keeps its whole public surface (its golden is unchanged): `TmKbd`
  from `./tm-kbd`, and the grammar re-exported from `@tellma/core-ui/keys` (a package import, so
  the built `./kbd` FESM imports `./keys` rather than carrying a second copy). `tm-kbd.ts`
  imports the grammar from `@tellma/core-ui/keys`.
- **Importers re-pointed** to `@tellma/core-ui/keys` wherever they use the grammar only: the
  shortcut registry (`tm-shortcut.ts`, `tm-shortcuts.ts`), `tm-page-action.ts` (type only), the
  app bar, the expression editor, the copy button, the chart, the pivot, the tiles, the grid
  (`grid-core`, `grid-keymap`, `grid-context-menu`, `find-bar`, `tm-grid-driver`), grid-editing's
  `editing.ts`, the menu's `tm-menu-entries.ts` (type only), and the specs that import helpers
  only. The four `TmKbd` users keep `./kbd`: the Shortcuts dialog, the menu rows,
  `docs-examples.spec.ts` and the showcase's kbd story (which also exercises the re-export).
  Re-pointing the lazily loaded importers too (not only the first-frame ones) keeps a lazy chunk
  that only matches keys from sharing a chunk with `tm-kbd`, and gives one rule: code that matches
  or labels keys imports `./keys`; `./kbd` is for the caps.
- **Guards.** `@tellma/core`'s `staticGraphExcludes` gains `@tellma/core-ui/kbd` on `"."`,
  `./shell` and `./attention` (the initialJs set: the primary plus the modulepreloaded `/shell`
  and `/attention`). All three pass after the change.
- **Lint.** The foundation boundary now covers `icon` and `keys` (no `@tellma` import), and `kbd`
  in its own block (no `@tellma` import but `@tellma/core-ui/keys`, through a `regex` pattern;
  a gitignore negation cannot re-include a child of an excluded parent). The shared patterns
  (no lucide, no rxjs or Transloco, no `../`) are one `FOUNDATION_PATTERNS` constant.
  `tm/no-platform-sniffing` now exempts `keys/**` instead of `kbd/**`, and its message names
  `@tellma/core-ui/keys`. Probed: a scratch file in `kbd/` importing `./icon`, the bare
  `@tellma/core-ui` and reading `navigator.platform` gets three errors, its `./keys` import none;
  the same file in `keys/` is refused all three imports (the probe files were deleted). The
  rule's own spec (`tools/tests/no-platform-sniffing.spec.mts`) now pins the move: `keys/` is the
  exempt folder, `kbd/x.ts` is in the covered list, and the supported-way example imports from
  `./keys`.
- **Test.** `tm-kbd.spec.ts` gains a case that `./kbd`'s grammar functions are `./keys`'s own
  (identity), so a copy of the grammar in `./kbd` fails it; the `./kbd` ceiling catches a copy in
  the built FESM too.
- **Budgets** (`@tellma/core-ui` package.json): `./keys` born at 2 (1.56 measured, rounded up to
  the next 0.5 KB, the counter/initials/blob-fetcher precedent); `./kbd` lowered from 2.35 to the
  spec's 1 (2.21 before, 0.82 after), as ./badge was when tm-counter left it. The ./kbd note's
  "still exceeds the spec's 1" sentence is put in the past tense, since it no longer does.
- **Docs.** core-ui README (the entry-point table and the foundation convention), core README (the
  first-frame paragraph on the static-graph check), ARCHITECTURE.md (the key-combination sentence
  of the UI component library section), and TSDoc/inline comments that named `kbd` as the
  grammar's home (`tm-shortcut.ts`, `contracts/tm-grid-keys.ts`, `grid-keymap.ts`).

### Measurements (clean builds: `rm -rf client/dist` / the Acme `dist` first)

| Figure | Before (e83e61c5) | After |
| --- | --- | --- |
| Acme initialJs | 223.14 in 4 files | **222.81 in 4 files** (−0.33) |
| Acme corePrimaryFirstFrame | 52.03 | 52.04 |
| Acme searchRoute / detailsRoute | 104.12 / 38.01 | 104.13 / 38.01 |
| `@tellma/core-ui/kbd` (size gate) | 2.21 of 2.35 | 0.82 of 1 |
| `@tellma/core-ui/keys` | – | 1.56 of 2 |
| `@tellma/core` `"."` | 53.71 of 53.72 | 53.72 of 53.72 (53.7197, 0.28 B under) |
| `@tellma/core-ui/grid` | 40.36 of 40.5 | 40.37 of 40.5 |

After the change `tm-kbd__key` is in one Acme chunk only, which `index.html` prefetches (the
tenantless pages) and no initial chunk loads. The saving is −0.33, not the analysis's −0.58: that
figure was measured with Angular's chunk optimizer off, before PC-F102 turned it on.

No ceiling was raised. The `"."` figure moves by gzip noise from the changed import specifier
(about 10 B; no code of the primary changed, only the module its key helpers come from), and sits 0.28 B under its ceiling.

### Tests and gates run locally

- Clean `pnpm run build`, then `size:check`: green, the three new guards included (and shown red
  on the base before the source change).
- Clean Acme `tellma-client build --no-dotnet-build`, before and after: exit 0, no budget warning.
- `ng test core-ui` over kbd, keys, copy, menu-rich, docs-examples, grid-keymap, the four grid
  specs that import the grammar, pivot, tiles and chart: 13 files, 792/792.
- `ng test core` over `src/nav`, `src/context`, the app bar, the Shortcuts dialog, the session
  menu, the tenant picker and the expression editor: 15 files, 306/306.
- `lint`, `typecheck`, `docs:build` (no generated change), `api:check` (only the new keys golden,
  approved) and `lint:test` (534 tests; its one failure, the platform-sniffing scope spec, was
  this change's and is fixed: 24/24 on re-run).
- Showcase e2e on Chromium: `kbd.spec.ts` and `menu-rich.spec.ts`, 42/42 (the key caps in the
  story and in the menu rows).
- `dotnet build Tellma.slnx` and `dotnet format --verify-no-changes` (no .NET change).

### Deviations

1. Spec 0023 §1's table gives `kbd` all the grammar's exports and no dependency. `kbd` keeps every
   export, but now depends on a new `keys` entry point that holds the grammar and that it
   re-exports. A public API addition (`@tellma/core-ui/keys`), flagged for Ahmad as the
   blob-fetcher and counter entry points were. The `copy` row likewise now depends on `keys`,
   not `kbd` (`button`, `icon`, `keys`, `tooltip`): code that only matches or labels keys
   imports `keys`. (Added in fix1, R1-2.)
2. Spec 0023 §3.2 says `tm/no-platform-sniffing` refuses the platform reads "outside `kbd`"; the
   one platform check now lives in `keys`, so the exemption is `keys/**`.
3. Beyond the ledger's `"."`, the guard also covers `./shell` and `./attention`, the rest of the
   initialJs set.

### Open risks

- `@tellma/core` `"."` is 0.28 B under 53.72. The next change to the primary (PC-F91 among them)
  re-measures it anyway; a later merge could tip it by noise alone.
- PC-F91 (held) edits imports in some of the same files (app bar, grid, chart). The changes here
  are one import line each, so a conflict, if any, is a one-line union.

### What reviewers should examine hardest

- That every first-frame importer of the grammar is re-pointed: the static-graph guard on `"."`,
  `./shell` and `./attention` is the proof (`size:check` output), and the Acme main chunk no
  longer holds `tm-kbd__key`.
- That `./kbd`'s FESM imports `@tellma/core-ui/keys` rather than inlining `tm-keys.ts`
  (`dist/core-ui/fesm2022/tellma-core-ui-kbd.mjs`), and the identity test.
- The kbd lint block's `regex` pattern: it must refuse every `@tellma` import but
  `@tellma/core-ui/keys`.
- That key caps still render wherever `TmKbd` is used: the menu rows (`tm-menu-rich.spec.ts`),
  the Shortcuts dialog (`tm-shortcuts-dialog.spec.ts`), the session menu's shortcut row
  (`tm-session-menu.spec.ts`) and the showcase story (`e2e/specs/kbd.spec.ts`).

## Review 1

### Findings

**R1-1 (blocking): the new `keys` library folder is unclassified, so the additions axe battery's
registry test fails.** `client/e2e/specs/additions-axe.spec.ts:54` (`OLDER_FOLDERS`) and
`client/e2e/support/additions-stories.ts:93` (`ADDITIONS_ENTRY_POINTS`) are the two lists every
`projects/core/tellma-core-ui/*` folder must be in exactly once
(`additions-axe.spec.ts:128`, "every library folder is either an additions entry point or an
older folder"). The commit adds `tellma-core-ui/keys/` and puts it in neither list.
- Failure, reproduced on a86e609f: `node scripts/e2e.mjs --project=chromium
  e2e/specs/additions-axe.spec.ts --grep registry` gives 1 failed and 3 passed: "the library
  folder keys is classified exactly once". The test has no `@nightly` tag, so it is PR tier. The
  pc/PC-F92 integration CI would go red on it. The pc/PC-F92 push run 45796165809 was skipped, so
  CI has not caught it yet.
- Test-list gap (PLAN §7.1 item 4): the commit ran only `kbd.spec.ts` and `menu-rich.spec.ts`.
  A new library folder touches this registry spec too.
- Fix: add `'keys'` to `OLDER_FOLDERS`, in sort order after `'input'`. In the list's TSDoc (lines 42-53), name it with the other UI-less splits, for example: "the keys grammar, split
  out of kbd's entry point with no UI of its own (the kbd story draws and matches what it
  defines)". The `initials` and `blob-fetcher` splits were handled the same way. Then re-run the
  registry tests.

**R1-2 (minor): the `copy` row of spec 0023 §1 no longer matches its dependencies, and the
Deviations do not say so.** `docs/specs/0023-component-library-additions.md:100` gives `copy` the
dependencies `button`, `icon`, `kbd`, `tooltip`. After this change,
`client/projects/core/tellma-core-ui/copy/tm-copy-button.ts:27` imports `@tellma/core-ui/keys`,
and `copy` no longer reaches `kbd` at all.
- Deviations 1 and 2 (commit message, and lines 104-109 of this file) cover the `kbd` row and
  §3.2. The `copy` row's change is a consequence of the same re-pointing, but it is not recorded.
  So the spec fix that goes to Ahmad would miss it.
- Spec 0010's "matched through `kbd`" wording (§C.10's "gains imports of … `@tellma/core-ui/kbd`",
  §C.12) still reads true through the re-export, so it needs no entry. The dependency table is a
  factual list, so it does.
- Fix: extend Deviation 1, in the commit message and in this file. Something like: "the `copy` row
  likewise now depends on `keys`, not `kbd`; code that only matches or labels keys imports
  `keys`."

### Responses

- R1-1: fixed in fix1. `keys` joins `OLDER_FOLDERS` in `client/e2e/specs/additions-axe.spec.ts`,
  after `input`, and the list's TSDoc names it with the other UI-less splits (the kbd story draws
  and matches what it defines). The whole `additions-axe.spec.ts` was run on Chromium. The other
  registries, allowlists and folder enumerations in `client/` were checked for a missing `keys`
  entry, and none needs one:
  - `e2e/support/additions-stories.ts` (`ADDITIONS_ENTRY_POINTS`, the stories): `keys` has no
    UI and no story, so it belongs in `OLDER_FOLDERS`, as `initials` and `blob-fetcher` do.
  - `tools/tests/library-docs.spec.mts` (`ADDITIONS`, the documented components and their
    harnesses): it lists only entry points with components; `initials` and `blob-fetcher` are
    absent too, and its examples walk reads the folders itself.
  - `tools/docs/extract-components.mts`, `scripts/api-extractor.mjs`, `scripts/bundle-size.mjs`
    and `tools/workspace.mjs` discover entry points from the `ng-package.json` files. The docs
    build changed nothing, the keys golden was generated, and size:check measures `./keys`.
  - `@tellma/core-ui` package.json: `budgetsInKb` and `budgetNotes` have `./keys`. Its
    `lazyChunkBudgetsInKb`, `treeShakes`, `staticGraphExcludes`, `apiGoldens` and
    `docs.globalStyles` maps name no foundation entry point, and `keys` ships no stylesheet.
  - The tsconfig `paths` (client, Acme) map `@tellma/core-ui/*` by wildcard. `angular.json`, the
    vitest configs and the CI workflows (`ci.yml`, `nightly.yml`, `nightly-tier.yml`) name no
    core-ui entry point.
  - ESLint: the foundation and platform-sniffing blocks were updated in commit 1. No other block
    enumerates the core-ui folders.
- R1-2: fixed in fix1. Deviation 1 above, and the fix1 commit message, now cover the `copy` row.
- The "." figure (53.7197 of 53.72): R1 found the 0.28 B margin is hash noise from the shared
  chunk's re-hash, not growth. No action, per the ruling.
- Fix1 gates: `typecheck`, `lint`, `lint:test` (534/534) and `api:check` (no drift) are green. The
  Chromium e2e of `additions-axe.spec.ts` (PR tier: the four registry tests and every story's
  PR-tier scan, 36 tests), `kbd.spec.ts` and `menu-rich.spec.ts` passed 78/78. The `@nightly`
  combinations of the axe battery were not run.

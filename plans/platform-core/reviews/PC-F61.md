# PC-F61: Nightly client-e2e: Playwright OS-dependency install fails and WebKit cannot launch

## Implementer notes

### Root cause

PC-F20 (67fa80db, 89b9360d; merged 2026-11-11) replaced `playwright install --with-deps` with
`eng/playwright-browsers/install-browsers.sh`. Its step 2 runs the caller's CLI command as root:

```
sudo -n env "PATH=$PATH" timeout ... <cli> install-deps <browsers>
```

The client callers passed `pnpm exec playwright` (nightly) and `pnpm --dir <pkg> exec playwright`
(ci.yml), so step 2 ran **pnpm as root**. sudo resets the environment: HOME becomes /root, and
`CI`, `PNPM_HOME` and the rest of the runner's variables are dropped (only PATH is passed back).
pnpm 11 defaults `verify-deps-before-run` to `install` (pnpm 11.9.0 source,
`runDepsStatusCheck`), so before `exec` it checks the workspace's install. Run as root, with another
home, store and configuration, it finds the install out of date and runs `pnpm install`. That
install's modules-directory compatibility check then wants to purge `node_modules`
(`purgeModulesDirsOfImporters`), which needs a confirmation prompt unless `CI` is set or
`confirmModulesPurge` is false. With no TTY and `CI` dropped by sudo, it throws
`ERR_PNPM_ABORTED_REMOVE_MODULES_DIR_NO_TTY`. apt never ran. The script took that for an apt
failure, retried once, warned "is the apt mirror reachable?" and went on to the launch probe. On
ubuntu-26.04, Chromium and Firefox launch without the extra packages, but WebKit lacks libgtk-4,
GStreamer and others, so the step failed.

It is not the apt mirror, the pnpm version as such, the working directory or a missing package.
The "Error: command.parse:" line is only the prefix Playwright's `screenshot` command puts on the
launch error.

Evidence:
- The last nightly before PC-F20 (42996613362, 2026-11-11 09:36Z, ubuntu-26.04) ran
  `pnpm exec playwright install chromium firefox webkit --with-deps`. Playwright ran as the runner
  user and called sudo only for apt. It fetched 139 MB in 17 s and the job passed. The first
  nightly after PC-F20 (43196029407, 2026-11-12, not 11-13) fails with the same
  `ERR_PNPM_ABORTED_REMOVE_MODULES_DIR_NO_TTY` as 44506202284 and 44701549452.
- **The PR tier has the same bug, hidden.** In PR #18's run 44699551746, the `client` and
  `acme-client` jobs (`pnpm --dir ... exec playwright`) fail both install-deps attempts with the
  same pnpm error. They pass only because Chromium launches without the missing packages, so the
  only sign is the "OS dependencies: unavailable" warning. The nine packages the script means to
  install are missing in those jobs: the CJK (IPA Gothic, WenQuanYi Zen Hei), Thai (TLWG Loma),
  GNU FreeFont, Unifont and X11 font packages. `identity-e2e`, whose CLI is
  `pwsh .../playwright.ps1` (no package manager), installs them: "OS dependencies: installed".
- Reproduced locally in an `ubuntu:26.04` container (the runner's image) with a `runner` user that
  has passwordless sudo, `CI=true`, Node 24 and pnpm 11.9.0, on a minimal workspace (root plus
  `client` with `@playwright/test` 1.61.1). Log at the scratch path PC-F61-repro.log:
  - `sudo -n env PATH=$PATH pnpm exec playwright install-deps --dry-run webkit` gives
    `ERR_PNPM_ABORTED_REMOVE_MODULES_DIR_NO_TTY`, exit 1.
  - The pre-fix script (`webkit -- pnpm exec playwright`) reproduces the nightly exactly: both
    attempts hit the pnpm error, then the warning, then "webkit failed to launch, and the OS
    dependencies they need could not be installed".
  - The fixed script refuses `pnpm exec playwright` with exit 2.
  - The fixed script with `node_modules/.bin/playwright` reaches apt as root ("Need to get 250 MB
    of archives", then the downloads and dpkg). In the bare container, 250 MB through a distant
    mirror outran the 150 s per-attempt limit, so that pass could not launch. A hosted runner
    needs 139 MB in 17 s. No file under the workspace became root-owned.
  - Second container (PC-F61-repro2.log, Node 24.22.3, pnpm 11.9.0, sudo-rs 0.2.13, the sudo
    of ubuntu-26.04). It used a test copy of the fixed script whose only change is a 1500 s
    per-attempt limit. Result: "Fetched 250 MB", then "chromium firefox webkit installed and
    launching (OS dependencies: installed)", exit 0. The committed script then ran unmodified:
    "(OS dependencies: present)", all three launched, exit 0. Root-owned files under the
    workspace: 0.

### What was built

- `eng/playwright-browsers/install-browsers.sh`
  - New argument check: the CLI command must not start with a package manager (`pnpm`, `pnpx`,
    `npm`, `npx`, `yarn`, `corepack`, `bun`, `bunx`; the basename, case-insensitive, extension
    dropped, so `pnpm.CMD` and `/usr/bin/npx` are caught too). Such a command fails with
    `::error::` and exit 2, and the comment explains why. Without the check, the next caller who
    writes `pnpm exec` brings this failure back. It would look like an apt-mirror outage in the
    PR tier and stay hidden as long as Chromium happens to launch.
  - Usage examples use `client/node_modules/.bin/playwright`. The `env PATH=...` comment now
    names node and pwsh only and points at the check.
  - The give-up warning no longer asserts a mirror problem. It now reads "each attempt's output is
    in the log; an unreachable apt mirror is the usual cause", because this bug hid behind that
    wording for a week.
- `.github/workflows/nightly-tier.yml`: client-e2e and client-e2e-quarantine pass
  `node_modules/.bin/playwright` (their working directory is `client/`), with a comment saying why
  the CLI is not `pnpm exec`.
- `.github/workflows/ci.yml`: `client` passes `client/node_modules/.bin/playwright`, and
  `acme-client` (Linux) passes
  `distributions/acme/src/Tellma.Distro.Acme.Client/node_modules/.bin/playwright`, each with a
  comment. The pwsh callers (identity-e2e, nightly dotnet) and the Windows steps, which do not use
  the script, are unchanged.
- `eng/playwright-browsers/README.md`: the examples, the hand-run sudo hint and a new paragraph on
  why the CLI must not be a package manager.

The bin shim is the same pinned CLI that `pnpm exec playwright` resolves: a POSIX sh script
written by `pnpm install` that runs `node .../@playwright/test/cli.js` with a NODE_PATH baked in.
It needs only `node` on PATH, which `env PATH=$PATH` keeps past sudo, and it does not depend on the
caller's working directory. pnpm also writes it on Windows (beside `.CMD`/`.ps1`). Under Git Bash
the fixed script ran chromium through `client/node_modules/.bin/playwright` here: "chromium
installed and launching (OS dependencies: not-applicable)". The guard was also checked there for
`pnpm`, `pnpm.CMD`, `/usr/bin/npx` and `Yarn` (exit 2 each).

### Deviations

None. No spec covers this; ARCHITECTURE.md does not describe the browser install, so it is
unchanged.

### Tests run locally

- `dotnet build Tellma.slnx`: Build succeeded, 0 warnings. `dotnet format Tellma.slnx
  --verify-no-changes`: clean. No .NET or client source changed.
- `bash -n` on the script. Both workflows parse with PyYAML, and every `install-browsers.sh`
  caller was listed from the parsed `run:` values.
- The Docker reproduction on ubuntu:26.04 and the fixed end-to-end run, both under Root cause
  above. The logs are PC-F61-repro.log and PC-F61-repro2.log in the orchestrator's scratch folder,
  and the harness is in PC-F61-repro/.

### Unverified until the orchestrator dispatches the nightly

- That client-e2e and client-e2e-quarantine install the OS dependencies on the hosted runner
  ("OS dependencies: installed"), that WebKit launches, and that the e2e suites then run. Whatever
  fails after the install step has never been reached since 2026-11-11 and is outside this fix.
  Two jobs have not run since then: the WebKit/Firefox e2e and the quarantine summary.
- The PR-tier `client` and `acme-client` jobs should now show "OS dependencies: installed" instead
  of the warning. CI on the integration commit shows it.

### Open risks / what reviewers should examine hardest

- The guard is a blocklist of launcher names. A wrapper script that itself calls `pnpm exec` would
  slip through. The README and comments state the rule; I judged a whitelist (node/pwsh/path to a
  shim only) too brittle.
- The shim path depends on pnpm's `node_modules/.bin` layout, i.e. `@playwright/test` being a
  direct dependency of the package named. True for client and the acme client; if a package
  drops it, the step fails at once with "No such file", which is loud.
- Pre-existing, not changed: when a per-attempt `timeout` kills apt during dpkg, the retry may
  find dpkg interrupted. A hosted runner's install takes about 35 s against the 150 s limit, so
  this was left alone.

## Review 1

Scope: commit 1 (b4a628df). Fix item, so no spec sections; reviewed against the failing runs,
PLAN.md §6-§7 and AGENTS.md.

What was checked, and holds:

- Root cause. Nightly 44701549452, client-e2e "Install browsers": both attempts print
  `Scope: all 5 workspace projects`, then `ERR_PNPM_ABORTED_REMOVE_MODULES_DIR_NO_TTY`, with the
  stack running through `runDepsStatusCheck` → `runPnpmCli ... pnpm.mjs install`. apt never runs;
  Chromium and Firefox launch, WebKit fails on libgtk-4 and GStreamer. That is pnpm 11's
  verify-deps-before-run install, run as root by `sudo -n env PATH=...` with CI and HOME reset,
  exactly as the notes say. The same error is in the first red nightly 43196029407 (4 hits), and
  every platform-core nightly since (11-13 through 11-20) is red, while the 19f54b37 runs
  (main trampoline, no client jobs) are green.
- PR tier. Run 44699551746: `client` and `acme-client (ubuntu-latest)` hit the same error on both
  attempts and finish "(OS dependencies: unavailable)"; `identity-e2e` (pwsh) reaches apt and
  finishes "(OS dependencies: installed)". The claim holds, except for the emoji part (R1-1).
- No test relied on the missing fonts. The client has no screenshot goldens (`toHaveScreenshot`,
  `toMatchSnapshot` and the like appear nowhere in client/ or distributions/), and the tests that
  use CJK text assert text or values, not rendering (avatar.spec.ts asserts fixed 72 px boxes, not
  glyph widths). The PR tier also ran with these fonts until 11-11 (`--with-deps`).
- Every caller. Parsing both workflows lists six `install-browsers.sh` callers: identity-e2e and
  nightly dotnet (pwsh, unchanged), client and acme-client Linux (repo-root paths, working
  directory `.`), client-e2e and client-e2e-quarantine (`node_modules/.bin/playwright`, working
  directory `client`). Each path resolves from its job's working directory; under sudo the cwd is
  kept and `timeout` execs the relative path. No other tracked script or doc calls the script with
  a package manager (CONTRIBUTING.md's `pnpm --dir client exec playwright install` lines are
  developer installs that never reach sudo).
- The shim. Both packages list `@playwright/test` directly, so `pnpm install` links
  `node_modules/.bin/playwright` (present locally beside `.CMD`/`.ps1`). It is a sh script that
  needs only `node`, which the passed-through PATH keeps. Ran here under Git Bash:
  `client/node_modules/.bin/playwright --version`, the acme one and `node_modules/.bin/playwright`
  from client/ all print 1.61.1.
- The guard, exercised under Git Bash with fake CLIs (log-free, scratch folder
  PC-F61-r1-fake): `./pnpm`, `./npx`, `./pnpm.CMD`, `./Yarn.ps1`, `/usr/bin/corepack`, `./bunx`
  and `PNPM` all exit 2 with the `::error::`; `./node`, `./playwright`, `./pwsh` and
  `./npm-run-all` pass the guard and reach step 1. No false positive. A backslash Windows path
  (`C:\x\pnpm.cmd`) slips through, but it can only occur on Windows, where step 2 never runs, so
  it does not matter.
- Root-owned files and sudo-rs. PC-F61-repro2.log (ubuntu:26.04, sudo-rs 0.2.13, pnpm 11.9.0):
  the fixed script installs and launches all three engines, the committed script then reports
  "present", and `find ~/ws -user root` counts 0. install-deps as root writes nothing under the
  runner's home either (HOME is /root under sudo).
- Windows: the Windows steps do not call the script and are unchanged. The guard runs on every
  OS, but no Windows caller passes a package manager.
- AGENTS.md / PLAN.md §7.1: the script stays LF and executable (100755), `bash -n` passes, no
  PC ids, `plans/` paths or spec references in comments or messages, README updated. No .NET or
  client source changed, so no unit or e2e test is touched; the shell guard has no automated test,
  as the script never had one, and a regression fails loudly (exit 2) on the next CI run. The
  PR-tier effect shows on the integration CI run; the nightly effect needs a dispatch, as the
  notes say.

### Findings

**R1-1 (minor): the implementer notes misstate which fonts the PR tier lacks.**
- Where: plans/platform-core/reviews/PC-F61.md:41 ("the CJK, Thai and emoji fonts the script means
  to install are missing").
- Scenario: run 44699551746's dry run for `client`, `acme-client` and `identity-e2e` lists nine
  missing packages: fonts-freefont-ttf, fonts-ipafont-gothic, fonts-tlwg-loma-otf, fonts-unifont,
  fonts-wqy-zenhei, xfonts-cyrillic, xfonts-encodings, xfonts-scalable and xfonts-utils. No emoji
  font is among them (the runner image already carries one, or Playwright does not ask for it).
  Someone triaging an emoji-rendering difference from these notes would chase the wrong cause.
- Fix: say "the CJK (IPA Gothic, WenQuanYi Zen Hei), Thai (TLWG Loma), GNU FreeFont, Unifont and
  X11 font packages".

### Responses

R1-1: fixed in the review 1 fixes commit. Checked against run 44699551746's `client` warning,
which lists the same nine packages with no emoji font. The PR-tier bullet in the implementer
notes now names them: the CJK (IPA Gothic, WenQuanYi Zen Hei), Thai (TLWG Loma), GNU FreeFont,
Unifont and X11 font packages.

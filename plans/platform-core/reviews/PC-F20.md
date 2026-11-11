# PC-F20: Chromium install survives an apt-mirror outage

## Review 1

Reviewed commit 67fa80d. No blocking findings; one major and one minor.

What holds up:

- **The outage path is fixed, within budget.** In the failing run (43047668112, client job),
  `apt-get update` took 3m22s and exited 0 with `W:` lines. `apt-get install` then spent 20 s on
  each mirror lookup for nine font packages. With the script, the worst case is the download (a
  cache hit for identity-e2e), an offline dry run, two attempts of at most 165 s each with 20 s
  between them, and probes of at most 135 s per browser. That is about 6 minutes for identity-e2e
  and the dotnet nightly job (10-minute steps), and about 13 minutes for the three-browser nightly
  jobs (15-minute steps).
- **The dry run behaves the same through both CLIs.** The Node CLI (playwright-core 1.61.1, from
  the pnpm catalog) and the .NET driver (1.61.1-beta under Microsoft.Playwright 1.61.0) carry the
  same `reportMissingDependenciesLinux`. Both run `apt-get install -s` as the runner user, with no
  network, print `Missing system dependencies (N):` and then the packages indented by two spaces,
  and set exit code 1. The `sed` in the script matches that format. `playwright.ps1` ends in
  `exit [Microsoft.Playwright.Program]::Main($args)`, so the exit code reaches bash. The failing
  log shows that the runner image carries apt lists, so the dry run has data to work from.
  Ubuntu 26.04 x64 has its own entry in the dependency table of both versions.
- **The probe exercises the same binary the tests use.** `playwright screenshot` calls
  `launchContext(..., { headless: true })` with no channel, so for chromium it launches
  chromium-headless-shell. The client e2e config sets no channel, the Angular unit-test builder
  uses `chromiumHeadless`, and `PlaywrightFixture` launches with `Headless = true`. A launch runs
  `validateHostRequirementsForExecutablesIfNeeded` (an ldd scan, plus WebKit's dlopen'd
  `libGLESv2`/`libx264`). A missing shared library therefore fails the probe with Playwright's
  own list. A `DEPENDENCIES_VALIDATED` marker restored from identity-e2e's cache skips that scan,
  but the real launch still fails on a library that is not there. Missing fonts cannot be caught
  this way. The warning names them, and no `toHaveScreenshot`/snapshot assertion exists to be
  thrown by font fallback.
- **The nightly rewiring keeps the old browsers and directories.** The browser lists are
  unchanged. The client-e2e and quarantine install steps still run from `client/`, so
  `pnpm exec playwright` resolves the same package; `../eng/...` is correct from there. The
  dotnet job uses the same .NET CLI path as before.
- **Bash and AGENTS.md.** I ran the script in Git Bash against a stub CLI (with `uname` forced to
  Linux): argument split, usage errors, missing-deps-then-launches (passes with the warning),
  missing-deps-then-launch-failure (exit 1 with the matching reason), and the success on attempt
  1 all behave as described. The script is LF, executable, has a README and has inline comments.
  No `docs/` reference.

### Findings

**R1-1 (major): `timeout` cannot stop the apt that Playwright starts under sudo. After an attempt
times out, the retry contends with an orphaned root apt, and that apt runs on into the test steps.**

- Where: `eng/playwright-browsers/install-browsers.sh:81-88`. The comment claims "`timeout`
  signals the CLI's whole process group; sudo relays the signal to apt, so a hung attempt does
  not outlive its budget".
- Why the claim fails: Playwright's `install-deps` runs
  `sudo -- sh -c "apt-get update&& apt-get install -y --no-install-recommends …"`
  (`transformCommandsForRoot`). `timeout` runs as the `runner` user. Its group-wide SIGTERM, and
  the SIGKILL from `--kill-after`, cannot be delivered to the root-owned `sh`/`apt-get`
  (`EPERM`, and the group kill skips them silently). sudo does not relay the signal either. Both
  sudo and sudo-rs drop a signal that comes from their own process group (it is assumed to have
  reached the command already), and here the sender, `timeout`, is in that group. Even if sudo
  did relay it, the relay would reach `sh` and not the running `apt-get update`. So when
  attempt 1 hits its 150 s, pnpm/pwsh and node die, `timeout` returns 124, and root apt keeps
  going.
- Scenario: the outage in run 43047668112. `apt-get update` alone took 200 s, so at about 150 s
  attempt 1 is "killed" while its `apt-get update` still holds `/var/lib/apt/lists/lock`.
  Attempt 2 starts about 20 s later. Its `apt-get update` either fails at once with
  "Could not get lock" or waits out its own budget, so the retry can never do useful work. The
  orphan then moves on to `apt-get install`, which spent more than 5 minutes on 20 s lookups in
  that log. It holds the dpkg lock while `dotnet test` / `pnpm run test` / the e2e run go on.
  If the mirror comes back mid-run, fonts are installed and fontconfig changes partway through
  the tests. Layout-sensitive assertions, such as overflow and truncation checks, can then fail
  intermittently, and nothing in the log ties that to the install step.
  - In the slow-but-working mirror case that the retry exists for ("enough to ride out a blip"),
    the same race makes attempt 2 fail on the lock. The step then warns. In the nightly jobs
    WebKit fails its probe for want of libraries that the orphan was about to install a minute
    later.
- The step's own time bound still holds, because `timeout` returns and the runner ends the step
  about 5 s after the shell exits. That is why this is major rather than blocking: the outage
  case now passes, but the bounded-retry design and its comment are not what actually runs.
- Fix: put the time limit on the root side, so the whole apt tree belongs to a process that is
  allowed to kill it. For example:

  ```bash
  timeout_as_root=(sudo -n env "PATH=$PATH" timeout --kill-after=15 "$deps_attempt_timeout_seconds")
  "${timeout_as_root[@]}" "${cli[@]}" install-deps "${browsers[@]}"
  ```

  With uid 0, Playwright runs `sh -c` directly, without sudo, inside the root `timeout`'s
  process group. `sudo -n` also fails fast where sudo needs a password (see R1-2). Another
  option: run `apt-get update` / `apt-get install -y --no-install-recommends <dry-run packages>`
  yourself, each under `sudo -n timeout …`. After the loop, assert that no `apt-get`/`dpkg` from
  the script is still alive, for example `pgrep -x apt-get` after a failed attempt, and log it.
  Correct the comment at lines 81-82 either way.

**R1-2 (minor): when run locally on a Linux box whose sudo asks for a password, each attempt hangs
on a password prompt it cannot read until `timeout` gives up.**

- Where: `eng/playwright-browsers/install-browsers.sh:85`, and `README.md:16-23`, which offers
  the script for local use ("From the repository root, on Linux or on Windows under Git Bash")
  and says sudo is passwordless only on a hosted runner.
- Scenario: a developer on Ubuntu with password sudo and some packages missing (fonts, say).
  Without `--foreground`, GNU `timeout` puts the CLI in a new process group, which is not the
  terminal's foreground group. sudo's password prompt reads `/dev/tty`, takes SIGTTIN and stops.
  The developer sees no prompt, or a prompt that never accepts input. After 165 s the attempt is
  abandoned (the root-side stop/kill caveat of R1-1 applies), the same happens on attempt 2, and
  the run ends with the "is the apt mirror reachable?" warning. That warning points at the wrong
  cause. A stopped sudo can be left behind.
- Fix: the `sudo -n` of R1-1's fix makes this fail at once with a clear message. In addition,
  when `-t 0` holds and `sudo -n true` fails, run `sudo -v` once before the loop so the password
  is asked in the foreground. Alternatively, have the README say that the apt step is for CI and
  that, locally, `sudo <cli> install-deps` should be run by hand.

### Responses

- **R1-1: fixed.** Each attempt now runs as
  `sudo -n env "PATH=$PATH" timeout --kill-after=15 150 <cli> install-deps <browsers>`, with no
  sudo prefix when the script is already uid 0. The CLI sees uid 0 and runs apt itself, in the
  root `timeout`'s process group, so the TERM and then the KILL reach apt. The comment now says
  why the limit has to run as root and no longer claims that sudo relays the signal. The README's
  step 2 says the same.
  Verified in the offline `dotnet/sdk:10.0-resolute` container against a stub CLI. The stub's
  `install-deps` holds a `flock` on a stand-in apt lock from a grandchild, the way
  `sh -c apt-get …` would. When attempt 1 hangs, it is killed at the limit and the lock is
  released: attempt 2 takes it with `flock -n` and succeeds. No lock holder is left at the end of
  any scenario (`pgrep`), whether the script runs as root or as uid 1000 through a stub
  passwordless `sudo`. The stub logs the exact
  `sudo -n env PATH=… timeout --kill-after=15 … install-deps` command line. The earlier scenarios
  still behave as before: deps present, mirror up, mirror down with libraries present (warning,
  exit 0) or missing (exit 1), launch failure after install (exit 1), and download failure
  (exit 1).
- **R1-2: fixed.** Before the loop, the script checks `sudo -n true`. Neither a missing sudo nor
  a password sudo prompts or hangs. Either one emits a `::warning::` that says root or
  passwordless sudo is needed and that on a developer machine `install-deps` should be run under
  sudo by hand, and names the packages still missing. The launch probe then decides. I did not
  add the interactive `sudo -v`. The README now says the apt step is meant for CI, and gives a
  `sudo env "PATH=$PATH" … install-deps chromium` example for local use. Verified as uid 1000 with
  no sudo, and with a stub sudo that refuses `-n`: the warning appears, the probe passes (exit 0),
  and with the probe failing the script exits 1.

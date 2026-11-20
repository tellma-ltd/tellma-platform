# PC-F60: CI on pc/** branches; integration-windows moves to the nightly tier

## Implementer notes

### What was built

Scope is D-144's workflow half, all under `.github/` and `CONTRIBUTING.md`. No product code, test
code, docs/ or ARCHITECTURE.md change.

- `.github/workflows/ci.yml`
  - `on.push.branches` is `[main, 'pc/**']`. A new comment above `on:` says what the push trigger
    is for and that the draft guards let push runs through.
  - The `integration-windows` job and its comment are removed. Nothing else in the file named it
    except the `all-checks-passed` comment ("joins this list once it has run green"), which is
    removed too. The `needs:` list was already without it and is unchanged.
  - The `integration` job's comment now says the PR tier runs integration suites on Linux only and
    that nightly-tier.yml runs the Core and Acme suites on Windows against LocalDB.
  - The concurrency group gains `${{ github.ref }}` (deviation 1 below), and its comment is rewritten.
  - A typo in the `all-checks-passed` comment is fixed ("all required job" to "every required job").
- `.github/workflows/nightly-tier.yml`
  - `integration-windows` added after `dotnet-windows`. It has the same name, runner, timeout (45),
    env (`TELLMA_TEST_SQL` LocalDB string), steps, step names, step order, commands and filter
    (`Category=Integration&Category!=Nightly&Live!=true`). The job has no artifacts, so there were
    no artifact names to keep. Only two things changed: `if:` is
    `github.ref == 'refs/heads/platform-core'` like the other jobs in the file, and the comment
    before the two `dotnet test` steps says "as in ci.yml's build-test job" instead of "see
    build-test", because this file has no build-test job. There is no name collision and no
    `needs:` is required, since the nightly file has no aggregate job.
  - The header line and the "Selection" comment describe the moved job. The PR tier runs no
    integration tests on Windows. The nightly tier runs the Core and Acme integration suites on
    Windows LocalDB with the PR-tier filter. The other integration suites, and every Nightly
    integration test, run on Linux only. The `dotnet-windows` comment now says "Nightly
    integration and E2E tests stay on the Linux job above", because Windows integration now runs
    in this file.
- `CONTRIBUTING.md`
  - "Test tiers" intro: the PR tier also runs on every push to `main` or a `pc/**` branch. The
    nightly tier also runs two integration suites on Windows.
  - "PR tier": integration suites run on Linux only. The sentence about a non-gating Windows
    LocalDB job is gone.
  - "Nightly tier": a new paragraph covers the Windows LocalDB run, with the `TELLMA_TEST_SQL`
    value and the two exact commands. It ends with the line that the other integration suites, and
    Nightly integration tests, are Linux-only.

### Checks (orchestrator items 1a, 1b)

(a) Draft guards on push events. Every job's `if:` is `${{ !github.event.pull_request.draft }}`, or
`${{ !cancelled() && !github.event.pull_request.draft }}` on all-checks-passed. A push event's
payload has no `pull_request`. Dereferencing a missing property in a GitHub expression yields
null, null is falsy, and `!null` is true, so every job runs on a push. Evidence: push run
42447411093 on main (2026-11-08, same guards) ran every job, all-checks-passed included, with no
job skipped.

(b) Concurrency. Before this change the group was
`ci-<event>-<pull_request.head.sha || sha>`, which on a push is `ci-push-<sha>`.
- Distinct pushes to pc branches carry distinct shas, so each got its own group and its own run.
  `cancel-in-progress: false` means no run is ever cancelled for being superseded.
- Residual case: two refs pushed at the same sha landed in one group. That happens with two pc
  branches created at the platform-core tip with no commits, which a pc/** push trigger now runs,
  or with main and a pc branch at one commit. The second run then waited behind the first. GitHub
  keeps only one pending run per group, so a third same-sha run cancels the pending one, which is
  another branch's run.
- Hence deviation 1: the group is now `ci-<event>-<ref>-<sha>`. For a push the ref is
  `refs/heads/pc/PC-xxx` or `refs/heads/main`. For a pull_request it is `refs/pull/<n>/merge`.
  Each branch's or PR's push now has its own group, and no run can wait behind or replace a run on
  another ref. The PR semantics are kept: a reopen or ready_for_review of the same head sha shares
  ref and sha, so it still waits for the first run. PR #18 runs as before on every platform-core
  push; platform-core is not in the push filter, so it gets no second push run.

### Local checks

- Both workflow files parse with PyYAML 6.0.3 (`python -I`). Parsed: ci.yml `on.push.branches` is
  `['main', 'pc/**']`, its job list no longer holds integration-windows, and the needs list is
  unchanged. nightly-tier.yml has jobs dotnet, dotnet-windows, integration-windows, client-e2e and
  client-e2e-quarantine.
- Moved job, original (`git show HEAD:.github/workflows/ci.yml`) against new:
  - The parsed job objects differ only in `if`.
  - The text diff is that `if:` line plus the one re-pointed comment (two lines became three).
- actionlint is not on PATH, so it was not run.
- `git diff --check` is clean, and the files are LF and UTF-8 without BOM.
- `dotnet build Tellma.slnx` printed "Build succeeded" with 0 warnings and 0 errors.
  `dotnet format Tellma.slnx --verify-no-changes` exited 0 and reported no file changes (only
  the usual workspace-load notice).
- No .NET or client tests were run, as the brief directs: nothing they cover changed.

### Grep (item 6)

`git grep` outside docs/ and plans/ for `integration-windows` and for Windows, LocalDB or PR-tier
integration wording found ci.yml, nightly-tier.yml and CONTRIBUTING.md (all updated). It also
found three lines that stay accurate and are unchanged:
- The Acme integration README says the server is "LocalDB on a Windows runner", which is now the
  nightly one.
- ARCHITECTURE.md "CI/CD & Quality Gates" says the PR tier "gates every push to a pull request"
  and never claims Windows integration. It does not mention the pc/** push trigger. That is
  orchestration rather than architecture, so it is left as is (flag for the orchestrator if it
  should be added).
- eng/playwright-browsers/README.md mentions Windows CI jobs, which is unrelated.
The untracked `.tmp/` copies (gitignored) were ignored.

### Deviations

1. The concurrency group adds `${{ github.ref }}` so that the same commit on two refs never shares
   a group. Without it, a third same-sha push could cancel another branch's pending run, which
   item 1(b) rules out.
2. The nightly job's verbosity comment points at "ci.yml's build-test job", because nightly-tier.yml
   has no build-test job. This is the only text change inside the moved job besides `if:`.

### Open risks

- From now on, any push to a pc/** branch runs the full PR tier, about 17 min and 11 jobs. That
  includes a branch push with no new commits and a hold push (D-087). Runner minutes grow with
  the number of pc pushes. This is intended by D-144.
- Branch deletions: deleting a pc branch should not start a run. GitHub does not run push
  workflows for branch deletions, but this is not verified here.
- The moved job's first nightly run is the first time it runs under workflow_dispatch from the
  trampoline. Its steps are unchanged, so only the guard is new, and that guard is the same
  expression the other four nightly jobs use. D-144's evidence line expects a hand-started
  nightly after this lands.

### What reviewers should examine hardest

- Item 1(b), and whether adding `github.ref` to the concurrency group changes any pull_request
  behaviour PROCESS relies on: §5.3 watches PR #18 runs by head sha, which is unaffected.
- That the moved job is byte-for-byte the original apart from `if:` and the one comment.
- The CONTRIBUTING.md wording of the two tiers' OS coverage.

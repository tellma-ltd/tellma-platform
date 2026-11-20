# PC-F60: CI on pc/** branches; integration-windows moves to the nightly tier

## Implementer notes

### What was built

Scope is D-144's workflow half, all under `.github/` and `CONTRIBUTING.md`. No product code, test
code, docs/ or ARCHITECTURE.md change.

- `.github/workflows/ci.yml`
  - `on.push.branches` is `[main, 'pc/**']`. Under the scope amendment (commit 2), every job's
    `if:` adds the integration-push gate to its draft guard:
    `(github.event_name != 'push' || github.ref == 'refs/heads/main' || contains(github.event.head_commit.message, ': integrate'))`.
    On all-checks-passed the gate sits beside `!cancelled()` and the draft guard. A pc push whose
    head commit does not carry ": integrate" therefore skips every job.
  - The nine gated `if:` values are double-quoted YAML scalars. The phrase's colon followed by a
    space is a mapping indicator in a plain scalar, and the plain form failed to parse ("mapping
    values are not allowed here"); GitHub would have rejected the file.
  - The comment above `on:` covers the trigger, the gate, the three integration titles, that a
    skipped push still creates an all-skipped run, and that `contains` ignores case and reads the
    whole message (body included), plus the quoting. The all-checks-passed comment says it skips
    with the others on such pushes.
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
  - "Test tiers" intro: the PR tier also runs on every push to `main`, and on a `pc/**` push
    whose head commit is an integration commit (message contains `: integrate`); any other `pc/**`
    push skips every job. The nightly tier also runs two integration suites on Windows.
  - "PR tier": integration suites run on Linux only. The sentence about a non-gating Windows
    LocalDB job is gone.
  - "Nightly tier": a new paragraph covers the Windows LocalDB run, with the `TELLMA_TEST_SQL`
    value and the two exact commands. It ends with the line that the other integration suites, and
    Nightly integration tests, are Linux-only.

### Checks (orchestrator items 1a, 1b)

(a) Draft guards on push events. A push event's payload has no `pull_request`. Dereferencing a
missing property in a GitHub expression yields null, null is falsy, and `!null` is true, so the
draft guard passes every push. Evidence: push run 42447411093 on main (2026-11-08, same draft
guards) ran every job, all-checks-passed included, with no job skipped.

(a2) The integration-push gate (scope amendment). Truth table of the full guard, evaluated with
GitHub's expression rules: a missing property is null, `contains` ignores case and is false on
null.

| Event | Result |
|---|---|
| pull_request, PR #18 ready | runs (event is not push) |
| pull_request, draft | skipped (draft guard, as before) |
| push to main, any message | runs (ref is main) |
| pc push, `PC-042: integrate` / `: integrate (merge platform-core)` / `: integrate (CI fixes)` | runs |
| pc push, implementation, `review n fixes` or old-style `merge platform-core` commit | every job skipped |
| pc push with no head_commit | every job skipped |

Pull requests and pushes to main are unaffected: the gate's first two disjuncts are true for
them, so their guards reduce to the old ones. On a skipped pc push, all-checks-passed is skipped
too (its `if:` is false), so the run has no failing step and no waiting job. GitHub still creates
the run and completes it with every job skipped; it is neither red nor hanging. Observed on this
branch's second push: see Local checks.

(b2) Concurrency under the gate. Each integration push is a new commit (a merge, an integrate
commit or a CI-fix commit), so its sha is new and its group `ci-push-refs/heads/pc/<id>-<sha>` is
its own. An earlier all-skipped run on the same branch has another sha, so another group, and it
finishes in seconds anyway. A re-push of an unchanged sha creates no push event. So each
integration push still gets its own run, never queued behind or cancelled by another.

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
- After the amendment, ci.yml still parses and every job's parsed `if` is the gated expression
  (nine of nine). The first plain-scalar attempt failed PyYAML, which is how the quoting
  requirement was found.
- The gate's truth table above was evaluated with a scripted model of the expression.
- Observed on GitHub: the push of cf37c45a, a commit without the gate phrase, created run
  44694269933. It completed within seconds with conclusion `skipped`, and all nine jobs were
  skipped, all-checks-passed included. It was neither red nor hanging. A watcher must read
  `skipped` as "no gate ran", not as green. Run 44693894569 is the full run on cdb4bca8, started
  under commit 1's ungated trigger.
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

- Every pc/** push still creates a run, but a run that is not an integration push has every job
  skipped. Only integration pushes run the full PR tier (about 17 min, 11 jobs).
- The gate is a substring match over the whole commit message, ignoring case. Any commit whose
  title or body contains ": integrate" starts a full run, for example a fix-round body line such
  as "R1-2: integrate the probe". That is a false positive: it costs CI minutes and breaks nothing.
  The false negative matters more. An integration commit titled any other way starts no real run,
  because the gate skips it. Only the three integration commits must carry the marker:
  - the §4.3 integration merge, `{PC}: integrate (merge platform-core)`, or the empty
    `{PC}: integrate`;
  - the green-path re-merge when non-plans commits came in between;
  - the red-path `{PC}: integrate (CI fixes)`.
  The plans-only re-merge after a rejected platform-core push keeps `{PC}: merge platform-core`.
  It is skipped by design (truth table above), as D-144 and §4.6 require.
- A hold push (D-087) pushes the last fix-round commit, so it is skipped, as intended.
- Branch deletions: deleting a pc branch should not start a real run. Its push has no
  head_commit, so the gate skips every job even if GitHub creates a run. GitHub does not usually
  run workflows for deletions, but that is not verified here.
- The moved job's first nightly run is the first time it runs under workflow_dispatch from the
  trampoline. Its steps are unchanged, so only the guard is new, and that guard is the same
  expression the other four nightly jobs use. D-144's evidence line expects a hand-started
  nightly after this lands.

### What reviewers should examine hardest

- The gate truth table (a2), the double-quoting of the nine `if:` values, and that pull_request
  and main-push runs are unaffected.
- Item 1(b), and whether adding `github.ref` to the concurrency group changes any pull_request
  behaviour PROCESS relies on: §5.3 watches PR #18 runs by head sha, which is unaffected.
- That the moved job is byte-for-byte the original apart from `if:` and the one comment.
- The CONTRIBUTING.md wording of the two tiers' OS coverage.

## Review 1

### Findings

R1-1 (minor) plans/platform-core/reviews/PC-F60.md:157-161 ("Open risks", second bullet). The note
says PROCESS.md §4.3's merge messages "must therefore use the `: integrate` titles. That includes
the plans-only re-merge before the fast-forward, which then starts a run nobody waits for." That
contradicts D-144 ("A plans-only re-merge after a rejected platform-core push stays
`{PC}: merge platform-core` and starts no pc run"), PROCESS.md §4.3 (green message) and §4.6 ("No
other commit may contain ': integrate'"). Scenario: an orchestrator or integrator acting on this
note titles the plans-only re-merge `{PC}: integrate (merge platform-core)`, which starts a full
~17 min pc run that nobody watches, and breaks the §4.6 rule that only the three integration
commits carry the marker. The workflow itself is right (the old title is skipped, as the truth
table shows, and that is the intended behaviour for the plans-only re-merge). Fix: reword the
bullet so the false-negative risk covers only the three integration commits (§4.3 integration
message, green-path non-plans re-merge, red-path CI fixes), and state that the plans-only
re-merge keeps `{PC}: merge platform-core` and is skipped by design.

No blocking or major findings. Verified, item by item:

- D-144 trigger and gate. ci.yml `on.push.branches` is `[main, 'pc/**']` and all nine jobs carry
  `(github.event_name != 'push' || github.ref == 'refs/heads/main' || contains(github.event.head_commit.message, ': integrate'))`
  (PyYAML parse of every job's `if`, re-run by the reviewer). `&&` binds tighter than `||`, and the
  gate is parenthesised, so for pull_request (any type, draft included) and push to main the gate
  is true and each guard reduces exactly to the old one; a draft PR still skips every job via the
  unchanged draft guard. `contains` is case-insensitive and false on a null `head_commit`. The
  double-quoted scalars are valid YAML, and GitHub evaluated them as expressions rather than as
  truthy literals: runs 44694269933 (cf37c45a) and 44694363809 (73403de8) both completed with
  conclusion `skipped` and all nine jobs `skipped`, all-checks-passed included, with no
  startup or expression failure. Run 44693894569 (cdb4bca8, ungated) is a full push run on
  pc/PC-F60 with no integration-windows job, which confirms the pc/** trigger reads the pushed
  commit's ci.yml (main's ci.yml still has `branches: [main]`). The positive gate path (an
  `: integrate` pc push running every job) is not yet observed; D-144's rollout makes PC-F60's own
  integration push that evidence.
- all-checks-passed. With the gate beside `!cancelled()`, a non-integration pc push skips it
  rather than running it against skipped needs (which would fail its step); on an integration push
  it behaves as before. No other job has `needs:`, so the implicit `success()` interplay is
  unchanged. Its `needs:` list is unchanged and never held integration-windows.
- Concurrency `ci-<event>-<ref>-<sha>`. Push: `refs/heads/pc/<id>` or `refs/heads/main` plus
  `github.sha`; pull_request: `refs/pull/<n>/merge` plus the head sha, so PR reopen and
  ready_for_review at one head sha still share a group, as before. Two runs share a group only for
  the same event, ref and sha, so no branch can queue behind or replace another's pending run.
  §5.3's PR #18 watch filters `--event pull_request --branch platform-core`, so pc push runs on the
  same sha (after the fast-forward) do not pollute it.
- integration-windows move. A text diff of the job block in `cdb4bca8^:ci.yml` against
  nightly-tier.yml shows only the `if:` line (now `github.ref == 'refs/heads/platform-core'`, the
  same guard as the other four nightly jobs) and the one re-pointed verbosity comment. Runner,
  timeout, `TELLMA_TEST_SQL`, steps and both `dotnet test` filters
  (`Category=Integration&Category!=Nightly&Live!=true`) are identical. The old ci.yml had no
  workflow-level `env`/`permissions`/`defaults` the job relied on; nightly-tier's
  `contents: read` covers checkout. The main-branch trampoline dispatches
  `nightly-tier.yml --ref platform-core`, so the job runs nightly once PC-F60 lands.
- Selection comment and CONTRIBUTING.md describe both tiers accurately: the PR tier runs
  integration on Linux only and unit on both OSes; the nightly tier adds Core and Acme on Windows
  LocalDB with the PR-tier filter; Nightly integration tests stay Linux-only. No other file outside
  docs/ and plans/ describes the old Windows integration job (reviewer grep over Markdown, C#,
  scripts and JSON).
- AGENTS.md / PLAN.md §7.1: no PC, decision, plans/ or docs/ references in the new comments or in
  CONTRIBUTING.md; files are UTF-8 without BOM, LF, `git diff --check` clean. No product or test
  code changed, so no suite is touched (§7.1 item 4); the reviewer ran none.

### Responses

R1-1: fixed in the review 1 fixes commit. The Open risks bullet now limits the false-negative
risk to the three integration commits. It states that the plans-only re-merge keeps
`{PC}: merge platform-core` and is skipped by design.

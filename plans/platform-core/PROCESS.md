# Platform-core run: operating process

How every checkpoint is implemented, reviewed, committed and integrated, and how the
orchestrator paces the run against the usage window. [PLAN.md](PLAN.md) explains *what* and *in
which order*; this file explains *how*. Every agent follows the parts that name its role.

Paths: **MAIN** = `C:/Users/ahmad/workspace/tellma/tellma-platform` (the main checkout), **WT** =
`C:/Users/ahmad/workspace/tellma/tellma-platform-worktrees/platform-core`, and slots are
`WT/pc-1` … `WT/pc-4`. In commands, `MAIN` and `WT` are shorthand: always write the full path.

## 1. Roles

| Role | Who | Model and effort | Writes |
|---|---|---|---|
| **Orchestrator** | the main session | session model (Opus), session effort | LEDGER.md, DECISIONS.md, DEPS.md and CHECKPOINTS.md edits (gates, splits), slot setup, launches, notifications. All only in MAIN |
| **Implementer** | one background agent per checkpoint, resumed for its fix round(s) and integration | inherit; `high`, or `xhigh` for the §3 list | code, tests, docs, `reviews/PC-nnn.md` (notes and responses), commits 1–3, the integration merge. All only in its slot |
| **Reviewer 1** | fresh background agent | inherit; `high` | `reviews/PC-nnn.md` "## Review 1 / ### Findings" only |
| **Reviewer 2** | fresh background agent, only for the §3 two-review list and escalations (§4.4) | `fable` for security-bound checkpoints, inherit at `xhigh` for complex ones | "## Review 2 / ### Findings" only |
| **Batch finders** | Workflow agents | `fable` while the Fable meter is under 90%, otherwise inherit at `xhigh`; read-only | structured findings |
| **Batch fixer** | Workflow agent | inherit; `high` | fixes, `PC-Bnn` commit, integration |
| **Fix agent** | background agent for a red CI run, nightly failure or hotfix | inherit; `high` | `PC-Fnn` commits |

- **Never pin an Opus model id.** Leaving `model` unset makes every agent inherit the session
  model, so switching the session to a newer Opus upgrades every agent launched afterwards (§12).
  Pin only `fable`.
- **Set `effort` explicitly on every spawn**, on the Agent tool and on Workflow `agent()`. This file
  is the instruction that authorises it.
- **Address agents stably.** If the Agent tool accepts a `name`, spawn with `impl-PC-nnn`,
  `r1-PC-nnn`, `r2-PC-nnn`, `fix-PC-Fnn`, and resume by that name. Always also record the agent id
  from the spawn result: `usage_window.py --agent` matches transcript files by id. LEDGER.md §In
  flight holds both, so they survive compaction.
- **Workflows are only for batch reviews and the MVP-gate review (§13).** Ultracode is on to lift
  concurrency caps and approval prompts, but every checkpoint step is a background Agent-tool
  spawn or a resume. Add no fan-out stages that this process does not list.

## 2. Repository, branches, slots

- **`platform-core`** holds integrated work. A non-draft PR `platform-core → main` stays open so
  GitHub CI (Linux and Windows) runs on every integration push. **No agent merges that PR, pushes
  to `main`, force-pushes, deletes branches or rewrites published history.** Server-side rulesets
  enforce this too (SETUP.md §1).
- **MAIN** stays on `platform-core`. Only the orchestrator works there. It keeps LEDGER.md and
  DECISIONS.md uncommitted between snapshots and runs `git pull --ff-only` after every
  integration. If that pull ever fails, stop and resolve it before reading any state from MAIN.
- **Snapshot procedure.** In MAIN run `git pull --ff-only`, then
  `git add plans/platform-core/LEDGER.md plans/platform-core/DECISIONS.md plans/platform-core/DEPS.md plans/platform-core/CHECKPOINTS.md`,
  then `git commit -m "PC-LEDGER: snapshot <date>"`, then `git push origin HEAD:platform-core`. If
  the push is rejected, run `git pull --rebase origin platform-core` and push again. Run it after
  each batch review, at every park, right after the kickoff dependency audit, and before launching
  any checkpoint whose CHECKPOINTS.md entry or gates changed since the last snapshot. Agents read
  CHECKPOINTS.md from their slot, which branches from `origin/platform-core`.
- **Slots** `WT/pc-1` … `WT/pc-4` are persistent worktrees, each with its own `bin/obj`,
  `node_modules` and ports. A slot is free when LEDGER.md §In flight does not list it and
  `git status` there is clean.
- **Per-checkpoint branch** `pc/PC-nnn` is cut in a slot from the latest `origin/platform-core`,
  pushed after every commit, and kept after integration as the audit trail.
- **Reviews** live in `plans/platform-core/reviews/PC-nnn.md`. Reviewers write findings there,
  and the implementer writes responses and commits the file with its fix commits.

**Command form (everyone).** Run shell commands only through the Bash tool (never PowerShell), and
read and edit files with the Read, Edit and Write tools. Write paths with forward slashes, and run
every command as `cd <folder> && <command>`. An agent's directory resets after every call; the
orchestrator's does **not**, because its last `cd` persists. So the orchestrator also prefixes
every MAIN command with `cd C:/Users/ahmad/workspace/tellma/tellma-platform && `, or a bare
`check_deps.py`, `git pull` or `usage_window.py` would run against a slot's stale copy. Never use
`git -C`. The permission allowlist matches exactly this form (SETUP.md §3.2). If a command is
blocked, do not retry variants: report it (§4.7 `blocks=`).

**Slot creation** (the orchestrator, once per slot, N = 1…4):

```bash
cd C:/Users/ahmad/workspace/tellma/tellma-platform && git fetch origin && git worktree add C:/Users/ahmad/workspace/tellma/tellma-platform-worktrees/platform-core/pc-N -b pc/slot-N origin/platform-core
```

Then, in the slot:

1. Write `.dev-ports.local` with slot-unique keys: `CLIENT_SHOWCASE=43N0`, `ACME_WEB=71N0`,
   `ACME_CLIENT=42N0` and `DB_SUFFIX=pcN`. The scripts honour the keys that exist today, and a
   checkpoint that adds a local run path (0024-k first) reads the rest.
2. Warm the slot: `cd <slot>/client && pnpm install --frozen-lockfile && pnpm run tokens:build-css`
   (use the repo root instead of `client/` once 0024-a moves the pnpm workspace root), then
   `cd <slot> && dotnet restore Tellma.slnx && dotnet build Tellma.slnx`.

**Starting a checkpoint** (the orchestrator):

```bash
cd <slot> && git fetch origin && git switch -C pc/PC-nnn origin/platform-core
```

## 3. Effort and model lists (analysis ids; map to PC ids through CHECKPOINTS.md)

- **Implementer `xhigh`:** SEED (PC-015), 0011-e, 0011-f, 0012-c1, 0012-c2, 0012-e, 0012-f,
  0013-d, 0013-f, 0014-c, 0014-d, 0014-e, 0014-f, 0015-d, 0015-f, 0015-g, 0015-i, 0020-c,
  0024-d, 0025-b, 0031-d, 0033-d, 0033-e, 0033-f. Everything else `high`.
- **Two-review list.** Only these checkpoints get Review 2 (Ahmad's ruling, PLAN.md §3.3); every
  other checkpoint gets Review 1 only, unless it is escalated (§4.4).
  - **Security-bound** (Reviewer 2 on `fable`): 0010-a1/a2/a3/a4/a5, 0011-e, 0011-f, 0014-c,
    0014-d, 0014-e, 0014-f, 0015-g, 0016-c, 0016-d, 0017-c2, 0017-d, 0018-c, 0020-c, 0021-d, 0024-c,
    0024-d.
  - **Complex** (Reviewer 2 inherit at `xhigh`): SEED, 0010-f1, 0010-f2, 0010-f3, 0010-f6, 0012-c1,
    0012-c2, 0012-d, 0012-e, 0012-f, 0013-d, 0013-e, 0013-f, 0015-d, 0015-f, 0015-i, 0019-e,
    0025-b, 0025-c, 0031-d, 0033-d, 0033-e, 0033-f.
  - If the weekly Fable meter is at or above 90%, use inherit at `xhigh` for every Reviewer 2.

## 4. The checkpoint cycle

```
orchestrator: assign slot, create pc/PC-nnn, LEDGER row → impl
  Implementer ── commit 1 "PC-nnn: <title>" ── push branch                         → r1
  Reviewer 1 (fresh) ── findings R1-n into reviews/PC-nnn.md                        → fix1
  Implementer (resumed) ── fix or push back ── commit 2 "PC-nnn: review 1 fixes" ── push
    two-review list or escalated (§3, §4.4) only:                                   → r2
    Reviewer 2 (fresh) ── reviews commits 1+2, findings R2-n                        → fix2
    Implementer (resumed) ── fix or push back ── commit 3 "PC-nnn: review 2 fixes" ── push
  Implementer (resumed) ── integrate into platform-core ── push                     → integrated
  CI green for that sha                                                             → done
```

A checkpoint off the two-review list has two commits; one on it has three. Fix commits are made
even when a review changes nothing: they then carry only the review file with its responses.
PC-000 and `PC-Fnn` fixes take Review 1 only.

### 4.1 Implementer brief (orchestrator fills `{}`)

```
You implement checkpoint {PC} "{title}" (analysis id {aid}) of the Tellma platform-core run.

Where you work. Only in the git worktree {slot} (branch pc/{PC}). Your shell's working directory
resets to the orchestrator's main checkout after every command. That checkout is on platform-core
and holds files you must never touch or commit. So: run shell commands only through the Bash tool
(never PowerShell), and read and edit files with the Read, Edit and Write tools; every path you
read or write is absolute under {slot}; run every command as `cd {slot} && <command>` (or
`cd {slot}/client && …`).
Before every commit and push, run `cd {slot} && git rev-parse --abbrev-ref HEAD` and confirm that
it prints pc/{PC}. Never touch other slots. Other checkpoints are being built in parallel.

Read first, in full:
- {slot}/plans/platform-core/PROCESS.md: §4, §8, §11.
- The "### {PC}" entry in {slot}/plans/platform-core/CHECKPOINTS.md, including its Planner note
  and Later seams.
- Every spec section that entry lists, in {slot}/docs/specs/. Read the surrounding sections when a
  rule depends on them.
- Each involved spec's section of {slot}/plans/platform-core/WATCHLIST.md. These are leads, not
  rulings: check each against the spec and the code.
- C:/Users/ahmad/workspace/tellma/tellma-platform/plans/platform-core/DECISIONS.md, the live copy
  (read-only). Its rulings bind you.
- The sections of {slot}/ARCHITECTURE.md that describe the projects you touch (find them by its
  headings).
- The code your checkpoint extends, and the seams earlier checkpoints left for it
  (`git log origin/platform-core`, grep for the types your entry names).
After switching branches, run `cd {slot}/client && pnpm run tokens:build-css` (or at the repo
root after 0024-a) before any client build.

Rules of conduct:
1. This is the platform core of a production system serving paying customers. Exercise due
   diligence. Never cut corners to save time or tokens, and never rush.
2. The specs are the authority but not omniscient. Make obvious corrections and improvements, and
   state every deviation in the commit message under "Deviations". Report rulings that are not
   obvious to the orchestrator in your return value.
3. Nothing is deployed. Backward compatibility is not a factor. If existing code (specs 0001-0009
   or earlier checkpoints) should change to fit the new code better, change it. Never hack around
   it.
4. AGENTS.md and docs/AGENTS.md apply in full: XML docs and TSDoc on everything, inline section
   comments, a README per project, Windows and Linux. No spec, §, docs/ or PC-id references in XML
   docs, TSDoc, error messages, logs or test names. Inline code comments may cite specs ("spec 0012
   §5.2"), but never PC ids, plans/ paths, review-finding ids or decision ids: plans/ is deleted
   before the merge. Never edit anything under docs/. A spec error is fixed in code and stated
   under Deviations.
5. If ARCHITECTURE.md disagrees with what you build, update it in this checkpoint and report it.
   Never edit LEDGER.md, DECISIONS.md, DEPS.md, PLAN.md, PROCESS.md or CHECKPOINTS.md.
6. PR-tier tests only (PLAN.md §6; commands in PROCESS.md §11). Tag nightly-tier tests and do not
   wait on them. Run commands that may exceed 10 minutes with run_in_background, wait for them to
   exit, and read their output before continuing. Never commit or return while a command you
   started is still running.
7. If the permission system blocks a command, do not retry variants. Note the exact command and
   report blocks=<n>.
8. The serialized migrations (PROCESS.md §5.1) are regenerated with dotnet-ef, never hand-edited
   or hand-merged.

Done means PLAN.md §7.1 items 1-6. Then:
- Write "## Implementer notes" in {slot}/plans/platform-core/reviews/{PC}.md (format PROCESS.md
  §4.5): what was built, where, deviations, open risks, and what reviewers should examine hardest.
- Commit everything as commit 1 (format §4.6) and push: `cd {slot} && git push -u origin pc/{PC}`.
- Return the §4.7 summary.

You will be resumed twice with review findings, and then asked to integrate.
```

### 4.2 Reviewer briefs

**Reviewer 1:**

```
You are Reviewer 1 for checkpoint {PC} "{title}" of the Tellma platform-core run. Review ONLY
commit 1 ({sha1}), which is checked out in the git worktree {slot}.

Your shell's working directory resets to a different checkout after every command. Run shell
commands only through the Bash tool as `cd {slot} && <command>`, and read files with the Read tool
by absolute paths under {slot}. Never read code from any other folder. Modify no file except
{slot}/plans/platform-core/reviews/{PC}.md. Never commit.

Read: the "### {PC}" entry in {slot}/plans/platform-core/CHECKPOINTS.md; every spec section it
lists, in {slot}/docs/specs/; {slot}/plans/platform-core/PLAN.md §6-§7; the involved specs'
sections of {slot}/plans/platform-core/WATCHLIST.md; "## Implementer notes" in the review file;
the live C:/Users/ahmad/workspace/tellma/tellma-platform/plans/platform-core/DECISIONS.md
(read-only). Inspect the change with `cd {slot} && git show --stat {sha1}` and
`cd {slot} && git diff {sha1}^ {sha1}`, and read changed files in full where needed.

Review adversarially for: (a) spec conformance, every requirement in scope item by item, with each
deviation justified; (b) correctness bugs, edge cases, error paths; (c) test adequacy: does each
spec rule have a test that would fail if the rule broke? and were the touched suites (PLAN.md §7.1
item 4) actually run? (d) the AGENTS.md rules and PLAN.md §7.1. Build and run the touched tests
yourself when a claim depends on it. Report only real issues, each with file:line, the failure
scenario and a suggested fix, ranked blocking / major / minor. No style nits the build does not
enforce.

Write them under "## Review 1" / "### Findings", numbered R1-1, R1-2, … Leave "### Responses"
empty. Return the §4.7 summary with the counts.
```

**Reviewer 2:** the same frame, with these changes:

```
Review commits 1 and 2 together: `cd {slot} && git diff {sha1}^ {sha2}`, the checkpoint as it now
stands. You have not seen the implementation discussion. Be adversarial: a skeptic who expects
bugs. In addition to Reviewer 1's lenses: concurrency and transactions, security (authz bypass,
injection, tenant isolation, secrets in logs), contract consistency with neighbouring specs' types
and wire shapes, failure under partial outages, Windows/Linux differences. Read "## Review 1" and
its responses. Where the implementer pushed back, judge whether the pushback holds and say so.
Write under "## Review 2" / "### Findings", numbered R2-1, R2-2, …
```

### 4.3 Resume messages to the implementer

Fix rounds:

```
Review {n} for {PC} is in {slot}/plans/platform-core/reviews/{PC}.md under "## Review {n}". For each
finding, fix it or push back with a concrete reason. Pushing back on a wrong finding is as valuable
as fixing a right one. Record each response under that review's "### Responses" (R{n}-k: fixed in
<sha> | pushed back: <reason>). Re-run PLAN.md §7.1 items 2-4, commit as "{PC}: review {n} fixes"
(format §4.6), push with `cd {slot} && git push origin pc/{PC}`, and return the §4.7 summary.
```

Integration (after the last fix round):

```
Integrate {PC}: run `cd {slot} && git fetch origin`. If origin/platform-core is not an ancestor of
HEAD, run `cd {slot} && git merge origin/platform-core -m "{PC}: merge platform-core"`, resolving
conflicts by regenerating lockfiles, API goldens, EF snapshots and serialized migrations rather
than hand-merging them, and re-run PLAN.md §7.1 items 2-4. Then push with
`cd {slot} && git push origin HEAD:platform-core` and `cd {slot} && git push origin pc/{PC}`. If the
platform-core push is rejected, repeat from the fetch. Return the §4.7 summary with the sha you
pushed to platform-core.
```

### 4.4 Escalation and verification

- **Escalate to Review 2.** A checkpoint off the two-review list gets Review 2 (inherit at `xhigh`)
  when Review 1 reports a blocking finding, or four or more major findings, or when fix 1 rewrites
  about a third of the checkpoint or more. Record the escalation in LEDGER.md.
- **Verify disputed points.** If the last fix round rewrites a large part of the checkpoint, or
  the orchestrator doubts a pushback on a blocking finding, it may run **one** targeted verification
  agent on the disputed points before integration, and log it in LEDGER.md.
- There is never a fourth implementer round. Anything still unresolved goes to DECISIONS.md, and the
  checkpoint integrates.

### 4.5 Review file

```
# PC-nnn: <title>
## Implementer notes
## Review 1
### Findings        (R1-1 … severity, file:line, scenario, suggested fix)
### Responses       (R1-1: fixed in <sha> | pushed back: <reason>)
## Review 2
### Findings
### Responses
```

### 4.6 Commit messages

```
PC-042: <checkpoint title>

Spec coverage: 0012 §5.1-§5.5, §13.2 (fixture)
Deviations:
- <what differs from the spec, and why>   (or "none")
Tests: <suites run: unit filter; integration suites by name; client gates; e2e>; nightly-tier: <tagged tests>
Review: plans/platform-core/reviews/PC-042.md

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

Fix commits are titled `PC-042: review 1 fixes` or `PC-042: review 2 fixes`; their bodies list the
fixed and pushed-back finding ids and any new deviations. The other prefixes are
`PC-042: merge platform-core`, `PC-B03: batch review fixes (PC-015…PC-031)`, `PC-F07: <what> (PC-042)`
and `PC-LEDGER: snapshot <date>`. Each message ends with the Co-Authored-By line of the model that
wrote it.

### 4.7 Agent return value (short; details live in files)

```
{PC} {step}: status=done|blocked|failed; sha=<sha or ->; findings=<blocking/major/minor or ->;
deviations=<n>; blocks=<n>; decisions_for_orchestrator=<one line each, or none>; note=<≤ 60 words>
```

## 5. Scheduling

### 5.1 Readiness and slots

- **Ready** means the LEDGER status is `todo` and every gate in DEPS.md is `done`.
  `cd C:/Users/ahmad/workspace/tellma/tellma-platform && python -I plans/platform-core/tools/check_deps.py --ready`
  lists the ready checkpoints. After any edit to DEPS.md (a split, a merge, a new gate) the same script must report
  0 problems before scheduling continues. A new gate may only point to an earlier checkpoint.
- **PC-140 (MVP gate)** also needs batch reviews PC-B01…B07 to be integrated. It is never filler
  work.
- **Concurrency:** **3** cycles by default, and **4** only while the ready queue holds both backend
  and client work.
- **Pick** the highest-priority ready checkpoint (PLAN.md Appendix A order), subject to the tier
  rule (PLAN.md §4) and the usage gates (§6). Prefer a slot that last built the same area, since its
  build output is warm.
- **Exclusive resources.** Two in-flight checkpoints must not own the same entry point, project
  file or migration at once. The **serialized migrations** are the shared fixture migrations
  (`test/shared/Tellma.Testing.Entities/Migrations`) and the Acme tenant migration
  (`distributions/acme/…/Migrations/Tenant`). At most one in-flight checkpoint may change the EF
  model behind each of them. Changes are regenerated with dotnet-ef 10.0.x, never hand-merged, and
  the parity test stays green.
- **Merging small checkpoints.** The orchestrator may merge two adjacent ready checkpoints of the
  same spec when both are under 3k LOC. The merged checkpoint keeps the lower id; the other becomes
  `merged-into:PC-nnn`, and DEPS.md is updated and checked.

### 5.2 Batch reviews (PLAN.md §8.2)

There is one batch review per phase (PLAN.md §8.2). Start it as soon as every checkpoint of its
phase is `done` (or one layer's half of it, PLAN.md §8.2) and the usage gates allow about 8 points. Prepare a free slot (§13) and run the batch workflow alongside the lanes. Its fix
commit integrates like any checkpoint.

### 5.3 CI verdicts and nightly triage

- Every integration push gets its own CI run: PC-000 sets a per-sha concurrency group with no
  cancellation. After every agent completion and on every wake, run
  `cd MAIN && gh run list --branch platform-core --event pull_request --limit 20 --json headSha,status,conclusion,url`.
  For each `integrated` row whose sha has a completed run, a green run moves it to `done` and a red
  one stops the line (§8.3). A pending run never blocks other integrations. A checkpoint's slot is
  freed when the checkpoint reaches `integrated`.
- On each wake (at least daily), read the latest `nightly-tier` run on `platform-core` and turn each
  new failure into a `PC-Fnn` item, prioritised by the tier of the code it covers.
- A flaky test is never retried into green silently. Quarantine it with a tag or trait, record it
  under LEDGER.md "Flaky", and queue a fix.

## 6. Usage pacing

### 6.1 Reading usage

`get_usage` (`mcp__ccd_session_mgmt__get_usage`) returns the weekly all-models percent `U`, the
weekly Fable percent and `resetsAt`, plus any shorter-window limit the plan has. If a 5-hour (or
other short) window is ever listed, gate on it too, as `S` below. Read usage before every launch
and after every agent completion, and log a row in LEDGER.md §Usage at least every 5 points. The
endpoint is rate-limited, so never poll it in a loop.

### 6.2 Cost model and calibration

- The cost of a full checkpoint cycle in points is `p = LOC/1000 × k`, with priors **k = 0.36
  (backend or infra) and 0.80 (client or both)** (PLAN.md §3.1). That `p` is a two-review cycle.
  Step shares of `p`: implement 0.55, review 1 0.10, fix 1 0.12, review 2 0.13, fix 2 + integrate
  0.10. A one-review cycle costs about 0.82 × p: implement 0.55, review 1 0.10, fix 1 0.12,
  integrate 0.05.
- **Calibrate from transcripts, never from completion summaries.** The token count in a completion
  notice is the agent's final context size. Get cost units with
  `cd MAIN && python -I plans/platform-core/tools/usage_window.py --session <orchestrator session id> --from <window start UTC>`.
  The session id goes in LEDGER.md §Run; find it at kickoff as the newest `*.jsonl` in the project's
  transcript folder (`--latest`).
- Compute `ρ = (U_now − U_window_start) / units(window start..now)` cumulatively over the current
  weekly window. Use it once the window spans at least 10 points; until then keep the prior 0.115
  points per M units. The first measured `ρ` and `k` **replace** the priors. After that, update
  `k` per layer as an exponential moving average of real cost per kLOC (weight 0.3). A
  checkpoint's real cost is the sum of its agents' units (`--agent <id>`, or `--by-agent`) × ρ.
- Fable steps cost about 1.8× their raw units, which the script's price weights already include.
  Also log ΔFable% against Fable units.
- The running forecast in LEDGER.md §Calibration is the remaining cycles at the current `k`, plus 6
  points per pending batch review (8 for a phase over 20 checkpoints), plus 0.15 points per pending
  checkpoint for the orchestrator.
  Publish it after 15 and after 40 integrated checkpoints (§9).

### 6.3 Gates

Let `R = 100 − U`, and `F` = the sum over in-flight checkpoints of `p × (remaining step shares)`.

- **Launch a new checkpoint** only if `R − F − p_new ≥ 5` (and `S ≤ 85` if a short window exists).
- **Start the next step** of an in-flight checkpoint only if `R ≥ 2 + that step's cost`. Finishing
  started work comes first.
- **Batch review** only if `R − F − 8 ≥ 5`.
- **Filler.** When no full checkpoint fits but `R − F ≥ 3`, use the remainder on small,
  self-contained work: nightly triage, a `PC-Fnn` fix, a checkpoint under 2k LOC, or scoping the
  next batch review. Points left at a reset are lost.

## 7. Limits: drain, park, wake

### 7.1 Drain

When the gates allow no launch, let in-flight steps finish and launch nothing new. When no agent is
running, park.

### 7.2 Park

1. Record in LEDGER.md §Run: `Parked = yes`, the time, `Parked at U`, `resetsAt`, and the next three
   ready checkpoints. Then run the snapshot procedure (§2).
2. CronCreate a **one-shot** at `resetsAt` + 10 minutes, in local time, with the prompt:
   `Platform-core run: the usage window reset. Resume per plans/platform-core/PROCESS.md §7.3.`
3. At **every** park, CronCreate a recurring check at `7,37 * * * *` with the prompt:
   `Platform-core run is parked (LEDGER.md §Run). Call get_usage. If weekly all-models usage is at
   least 15 points below "Parked at U", resume per plans/platform-core/PROCESS.md §7.3; otherwise
   end the turn without doing anything else.` This catches Ahmad's manual reset and any
   promotional reset Anthropic grants all users, within 30 minutes. A 30-minute cadence keeps the
   session's one-hour prompt cache warm, so each check costs a cache read (about 0.3 points a day).
4. **Only in the manual-reset week** (the window starting Tue Nov 10) and only if `U ≥ 90`, notify
   Ahmad (§9): `Platform-core parked at U%. Apply the one-time usage reset (by Sat Nov 14 20:00 UTC); the run resumes within 30 min.`
5. A short-window park (`S > 85` with nothing running) uses a one-shot at that window's reset + 5
   minutes, with the same prompt as step 2.
6. End the turn.

**Unexpected resets while running.** The gates read `get_usage` before every launch, so a
promotional reset that lands mid-run simply shows up as more headroom. Log it in LEDGER.md §Usage
as "unexpected reset", recalibrate the window start for `ρ` (§6.2), and update the forecast. If it
lands in the manual-reset week before Ahmad's reset, his one-time reset is still to come and the
§7.2 step-4 notice still applies at the next near-limit park. Ahmad may also wake a parked run at
any time with "Check usage and resume per PROCESS.md §7.3". That is harmless when nothing has
reset: the run re-parks.

If a request ever hits a limit anyway, the interactive session waits and continues by itself at
the reset (`autoContinueAtUsageLimit`, up to three times). A background agent caught by a limit may
fail. Its slot keeps the files, and §8.4 recovers it once usage allows.

### 7.3 Resume

Delete the parking cron jobs (CronList, CronDelete), set `Parked = no`, read and log `get_usage`. If
a manual or promotional reset happened, compare the new `resetsAt` with the previous one; if it moved, log it,
recompute the forecast, and notify Ahmad. Then recover per §10 if anything was
interrupted, and schedule per §5.

## 8. Failure handling

### 8.1 Blocked implementer

If the implementer returns `blocked`, read its notes, then choose one of four actions:

- resume it with guidance;
- re-spawn the step at `xhigh` with a summary of what failed;
- split the checkpoint (`PC-nnn.1`/`.2`, with DEPS.md updated and checked);
- park it and continue elsewhere.

A spec contradiction is ruled on by the orchestrator under Rule 2 and recorded in DECISIONS.md.
Nothing waits for Ahmad. His only planned input is the usage reset.

### 8.2 Integration conflicts

The implementer resolves them during integration: it regenerates lockfiles (`pnpm install`), API
goldens (`pnpm run api:approve` after reading the diff), EF snapshots and serialized migrations,
and never hand-merges them. When two migrations conflict, the later checkpoint re-scaffolds its own
on top of the integrated one.

### 8.3 Red CI on `platform-core`

Stop the line. Assign a free slot `{fslot}`, record `PC-Fnn` in LEDGER.md §In flight, and run
`cd {fslot} && git fetch origin && git switch -C pc/PC-Fnn origin/platform-core`. If the failure lies
in the checkpoint whose push turned CI red and its implementer is resumable, resume it with the
message below. Otherwise spawn a fix agent with the §4.1 brief ({PC} = PC-Fnn, {slot} = {fslot})
plus the same message.

```
CI run <url> is red after your integration of {PC}. Work only in {fslot} on branch pc/PC-Fnn
(command form as before). Fix it, commit "PC-Fnn: <what> ({PC})", push with
`cd {fslot} && git push -u origin pc/PC-Fnn`, and return the §4.7 summary.
```

The fix gets one review (the Reviewer 1 brief) and then integrates per §4.3.
Other lanes finish their current step but do not integrate until a green run exists on a sha that
contains the fix.

### 8.4 Agent death (limit, crash, app restart)

The slot holds the files. Resume the agent with:

```
You were interrupted. Inspect `cd {slot} && git status` and `git log -3`, then finish the step.
```

If it cannot be resumed, spawn a fresh agent with the same brief plus that instruction. Its
predecessor's notes are in the review file. If the agent failed on a usage limit, do not re-spawn
until `get_usage` shows that limit has reset.

## 9. Communication with Ahmad

Ahmad is mostly away. Use PushNotification, which reaches his phone when Remote Control is
connected, plus email if KICKOFF.md authorises it. Notify him only for these events:

- any **permission pause or classifier block** an agent reports, or the session itself waiting for
  approval. Send this at once: `Platform-core: permission block on <PC>: <command>. Approve or widen the allowlist.`
- the calibrated forecast after 15 and after 40 integrated checkpoints
- a park in the manual-reset week (§7.2), a moved `resetsAt` after the manual reset, and any
  unexpected (promotional) reset with the revised forecast
- the MVP gate closing, and the end of each tier
- a blocker that stops all lanes

Each message is one line under 200 characters, leading with what he would act on. Everything else
goes to LEDGER.md and DECISIONS.md.

## 10. Recovery after compaction or restart

1. Re-read PLAN.md §1–§4, this file in full, and LEDGER.md.
2. Run `cd C:/Users/ahmad/workspace/tellma/tellma-platform && git pull --ff-only` and fix any
   failure first. Then, with the same `cd … &&` prefix, run
   `git log origin/platform-core --oneline -40` and
   `python -I plans/platform-core/tools/check_deps.py --ready`.
3. For each row in LEDGER.md §In flight, check the slot (`git status`, `git log -3`) and whether the
   agent is alive (ListAgents). Wait for live agents, and resume dead ones per §8.4. Make the LEDGER
   step match the commits on the branch.
4. If parked, re-arm the parking cron jobs (CronList).
5. Continue scheduling.

## 11. Repo facts every agent must know

- **Full gate (exact).**
  1. `cd <slot> && dotnet build Tellma.slnx`, and read "Build succeeded": a failed build leaves
     `dotnet test` running stale DLLs.
  2. `cd <slot> && dotnet format Tellma.slnx --verify-no-changes`, and grep its output rather than
     trusting the exit code.
  3. `cd <slot> && dotnet test Tellma.slnx --no-build --filter "Category!=Integration&Category!=E2E&Category!=Nightly&Live!=true"`.
  4. Integration suites one at a time:
     `cd <slot> && dotnet test <test project> --no-build --filter "Category=Integration&Category!=Nightly&Live!=true"`.

  Never run an unfiltered `dotnet test Tellma.slnx`.
- **Client PR tier.** `cd <slot>/client && pnpm run <gate>` for lint, typecheck, lint:test,
  tokens:check, build, test, api:check, docs:build and size:check, and e2e with
  `cd <slot>/client && node scripts/e2e.mjs --project chromium` (tagged tests excluded per
  CONTRIBUTING.md after PC-000). Once 0024-a moves the pnpm workspace to the repo root, run them
  there.
- Write files as UTF-8 without BOM; `dotnet format` fails on BOMs and charset. Write scripts with
  the Write tool: Bash heredocs can halve backslashes (use `chr(92)` in Python).
- The first full Debug build in a slot takes about 6 minutes, a no-op rebuild 68 s. Build the
  touched projects in the inner loop and the solution once at the end.
- Integration tests use Testcontainers SQL Server, one container per assembly (≈2 GB). If
  `mssql/server:2022-latest` exits 255, set `TELLMA_TEST_SQL_IMAGE=mcr.microsoft.com/mssql/server:2025-latest`.
  Every database is created `COLLATE Latin1_General_100_CI_AS` (spec 0011 §6.7).
- Never run a distribution (migrator, web host, e2e host) against the shared local SQL Server with
  default database names: `Tellma.dev.acme.*` is shared by every slot. Use Testcontainers or the
  slot's database suffix, and the slot's ports from `.dev-ports.local`.
- Client: pnpm 11, Angular 22, ESM, zoneless. Unit tests run in real Chromium (vitest browser
  mode). The tokens CSS is generated and gitignored: rebuild it after switching branches, or new
  component vars silently vanish. API goldens are LF; review every `api:approve` diff before
  committing it.
- Real clipboard cut/paste-move e2e fails on local Windows and passes on Linux CI; do not chase it
  locally. Identity E2E sign-in codes are rate-limited to 10 per IP per hour, so reuse the signed-in
  storage state.
- The CI `design-e2e` recipe deletes and regenerates the committed MigrationsHost migration. Never
  run it in a slot.
- Workflow scripts must be LF (CRLF scripts are refused) and live under the working directory.
  Before launching a workflow, run a Bash `cd MAIN` so its agents inherit the right directory.

## 12. Model upgrades

Ahmad may switch the orchestrator session to a newer Opus in the model picker at any time. The
switch applies from the session's next turn. Running agents finish on the old model, and every
agent spawned afterwards inherits the new one. Note the switch in LEDGER.md and recalibrate `k`.

## 13. Batch review workflow (template)

Before launching:

1. Pick a free slot and record `PC-Bnn | <slot> | pc/PC-Bnn | batch` in LEDGER.md §In flight.
2. Run `cd <slot> && git fetch origin && git switch -C pc/PC-Bnn origin/platform-core`.
3. Run `cd MAIN` (so agents inherit it).
4. Launch with `args = { batch, members: [...PC ids], slot, sha: <slot HEAD>, commits: [...each member's three commit shas from LEDGER.md], finderModel: 'fable' or undefined when the Fable meter ≥ 90 }`.

Free the slot when the workflow returns. Keep the script LF.

```js
export const meta = {
  name: 'pc-batch-review',
  description: 'Platform-core batch review: read-only finders, then an Opus fixer that integrates',
  phases: [{ title: 'Review' }, { title: 'Fix' }],
}
const A = args
const FIND = {
  type: 'object',
  properties: { findings: { type: 'array', items: { type: 'object', properties: {
    id: { type: 'string' }, severity: { type: 'string', enum: ['blocking', 'major', 'minor'] },
    where: { type: 'string' }, scenario: { type: 'string' }, fix: { type: 'string' } },
    required: ['id', 'severity', 'where', 'scenario', 'fix'] } } },
  required: ['findings'],
}
const LENSES = [
  'cross-checkpoint contracts: types, wire shapes and seams that one checkpoint defines and another consumes',
  'end-to-end scenarios: walk the main user and operator journeys through the integrated code',
  'failure, concurrency and security: tenant isolation, authz, transactions, retries, partial outages',
]
phase('Review')
const found = await parallel(LENSES.map((lens, i) => () => agent(
  `You are a batch reviewer (${lens}) for ${A.batch} of the Tellma platform-core run. Read-only: modify nothing. ` +
  `Use the Bash tool; your shell's directory resets after every call, so read code only as absolute paths under ${A.slot} ` +
  `(checked out at ${A.sha}) or with \`cd ${A.slot} && git show ${A.sha}:<path>\`. Members: ${A.members.join(', ')}; ` +
  `their entries are in ${A.slot}/plans/platform-core/CHECKPOINTS.md and their review files in ${A.slot}/plans/platform-core/reviews/. ` +
  `Their own changes are the commits ${A.commits.join(' ')}. Review against the specs they cite. Report only issues the ` +
  `per-checkpoint reviews could not see, because they span checkpoints or need the integrated whole. Each finding needs ` +
  `file:line, a concrete failure scenario and a fix.`,
  { label: `review:${i}`, phase: 'Review', schema: FIND, effort: 'xhigh', ...(A.finderModel ? { model: A.finderModel } : {}) })))
if (found.some(r => !r)) throw new Error('a batch finder failed; rerun the batch')
const all = found.flatMap((r, i) => r.findings.map(f => ({ ...f, id: `L${i}-${f.id}` })))
log(`${all.length} findings across ${LENSES.length} lenses`)
phase('Fix')
const out = await agent(
  `You are the batch fixer for ${A.batch} in worktree ${A.slot} (branch pc/${A.batch}). Follow the command form and rules of ` +
  `${A.slot}/plans/platform-core/PROCESS.md §2 and §4.1. Verify each finding below against the code before acting: fix the ` +
  `real ones and push back on the wrong ones, recording every verdict in ${A.slot}/plans/platform-core/reviews/${A.batch}.md. ` +
  `Run the PR-tier gates (PLAN.md §7.1), commit "${A.batch}: batch review fixes (${A.members[0]}…${A.members[A.members.length - 1]})", ` +
  `push, and integrate per PROCESS.md §4.3. Return the §4.7 summary.\n\nFindings:\n${JSON.stringify(all, null, 1)}`,
  { label: 'fix', phase: 'Fix', effort: 'high' })
return { batch: A.batch, findings: all.length, result: out }
```

For the MVP gate, use the same script with the lenses rewritten as the PLAN.md §7.2 journeys, plus
a fourth finder that follows HUMAN-TESTING.md on a clean clone.

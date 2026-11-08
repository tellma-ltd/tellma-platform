# Platform-core run: setup and pre-flight

For Ahmad. Do these steps in order, then paste [KICKOFF.md](KICKOFF.md). After that the run needs
nothing from you except the one-time usage reset (§4) and, if it ever happens, approving a
permission prompt it notifies you about.

## 1. Repository (in this order)

Your local `main` is behind: `origin/main` has #15 and #16, including the
System.Security.Cryptography pins that keep the NuGet audit green. Always branch from
`origin/main`. `specs-backup` is pushed to this **public** repo with 514 `.tmp` files, so its
history is already public; deleting the branch does not unpublish it.

1. **Clone `tellma-brand`** to `C:\Users\ahmad\workspace\tellma\tellma-brand`.
2. **Rescue the brand files.** Copy `.tmp/ui-design/assets/tellma-wordmark-white.svg` into
   `tellma-brand/brand-assets/logo/exports/svg/`, and export a **vector mark** SVG there from
   `brand-assets/logo/source-ai/` (the repo has the mark only as PNG app icons). Spec 0024 §7.6
   needs wordmark and mark files for both dark and light backgrounds. Commit and push
   `tellma-brand`. Without these files the implementer recolours the wordmark and ships a
   placeholder mark, which goes in DECISIONS.md.
3. **Archive `.tmp`**, still on `specs-backup`, into a separate **private** repo. Include the
   untracked, ignored `.tmp/ui-design/`. Then move `.tmp` out of the working tree. Nothing in specs
   0010–0035 needs `.tmp`: the planning analysis checked every spec for outside references.
4. **Merge the docs into `main`** through a docs-only branch:
   ```bash
   git fetch origin
   ```
   ```bash
   git switch -c docs/specs-0010-0035 origin/main
   ```
   ```bash
   git checkout origin/specs-backup -- ARCHITECTURE.md docs/research
   ```
   Add `.github/workflows/nightly-tier.yml` (the trampoline below), then stage only these paths
   (`plans/` is untracked here and must not go to `main`):
   ```bash
   git add ARCHITECTURE.md docs/research .github/workflows/nightly-tier.yml
   ```
   Commit, push, open a PR and merge it. **Do not merge `specs-backup` itself**: it carries `.tmp` and a `.gitignore`
   change. **Specs 0010–0035 do not go to `main` here.** They live on `platform-core` and merge
   into `main` together with their complete implementation.

   The trampoline exists because GitHub fires `schedule`, and accepts `gh workflow run`, only for
   workflows on the default branch. This file dispatches the platform-core version of the same
   workflow each night, and PC-000 then fills that version with the real nightly jobs:
   ```yaml
   name: Nightly tier
   on:
     schedule: [{ cron: '23 2 * * *' }]
     workflow_dispatch: {}
   permissions:
     actions: write
     contents: read
   jobs:
     dispatch:
       if: github.ref == 'refs/heads/main'
       runs-on: ubuntu-latest
       steps:
         - run: gh workflow run nightly-tier.yml --repo ${{ github.repository }} --ref platform-core
           env:
             GH_TOKEN: ${{ github.token }}
   ```
5. **Create `platform-core` from `origin/main`**, never the stale local `main`:
   ```bash
   git fetch origin
   ```
   ```bash
   git switch -c platform-core origin/main
   ```
   Add specs 0010–0035 (`git checkout origin/specs-backup -- docs/specs` before the branch is
   deleted, or from the archive repo) and `plans/platform-core/`, commit, then:
   ```bash
   git push -u origin platform-core
   ```
6. **Open a non-draft PR `platform-core → main`** titled e.g. "Platform core (specs 0010–0035):
   in progress, do not merge". CI skips drafts. Put its number into KICKOFF.md.
7. **Guard the branches server-side.** `main` already has two rulesets: no deletion or
   force-push (not bypassable), and PR + 1 approval + `all-checks-passed` (your team may bypass).
   Agents run with your `gh` credentials, so they could use that bypass (`gh pr merge --admin`).
   The local deny rules on `gh pr merge` and `gh api` (§3.2) close that gap. Add a ruleset on
   `platform-core` and `pc/**` with Restrict deletions and Block force pushes, and no bypass.
8. **Delete `specs-backup`** locally and remotely once this command prints nothing:
   ```bash
   git diff origin/platform-core origin/specs-backup -- docs ARCHITECTURE.md
   ```

## 2. Machine

- **Windows Update:** Settings → Windows Update → *Pause updates* for 5 weeks. This is safer than
  disabling services, and updates resume on their own.
- **Power:** when plugged in, never sleep and never hibernate; set lid close to *Do nothing* (or
  keep the lid open). The screen may turn off. Cron wake-ups fire only while the machine is awake,
  and a sleep of more than about 30 minutes across a reset needs a manual Enter.
- **Docker Desktop:** start at sign-in, automatic updates off.
- **Claude Desktop:** keep it open, do not install an app update mid-run, and keep other Claude
  usage minimal, since it shares the weekly pool.
- Create `C:\Users\ahmad\workspace\tellma\tellma-platform-worktrees\platform-core\` for the slots.
- Optional speed-up: Defender real-time scanning slows .NET and pnpm builds. Excluding the repo,
  the slot folder, `%USERPROFILE%\.nuget` and the pnpm store is your call, as it is a security
  setting.

## 3. Claude Code configuration

### 3.1 Session

| Setting | Value | Why |
|---|---|---|
| Where | **Local** (Claude Desktop, Code tab), folder `tellma-platform`, **worktree toggle off**, branch `platform-core` | The orchestrator works in the main checkout and creates its own slots |
| Model | **Opus 5.5** | Agents inherit it; Fable is pinned only for some reviewers |
| Effort | **High** | The orchestrator coordinates; agents get their own effort (PROCESS.md §3). If the first `xhigh` spawn shows the app clamping effort to the session's level, raise the session to **xhigh** |
| Ultracode | **On** | Lifts concurrency caps and workflow approval prompts. KICKOFF restricts workflows to batch reviews |
| Permission mode | **Auto** | Unattended without bypass, with the allowlist below keeping classifier decisions rare |
| Remote Control | **On**, then test it | PushNotification reaches your phone, and a permission prompt (the one thing that can stall the run) can be answered from it. KICKOFF sends a test |

### 3.2 `.claude/settings.local.json` (not committed; merge into the existing file)

Every agent runs commands in exactly one form: the Bash tool, `cd <folder> && <command>`. These
rules match that form.

```json
{
  "permissions": {
    "additionalDirectories": [
      "C:\\Users\\ahmad\\workspace\\tellma\\tellma-platform-worktrees\\platform-core",
      "C:\\Users\\ahmad\\workspace\\tellma\\tellma-brand"
    ],
    "allow": [
      "Bash(cd:*)",
      "Bash(dotnet build:*)", "Bash(dotnet test:*)", "Bash(dotnet format:*)", "Bash(dotnet restore:*)",
      "Bash(dotnet tool restore:*)", "Bash(dotnet ef migrations add:*)", "Bash(dotnet ef migrations remove:*)",
      "Bash(dotnet tool run dotnet-ef:*)",
      "Bash(pnpm install:*)", "Bash(pnpm run:*)", "Bash(pnpm exec:*)", "Bash(node scripts/:*)",
      "Bash(python -I plans/platform-core/tools/:*)",
      "Bash(git status:*)", "Bash(git diff:*)", "Bash(git log:*)", "Bash(git show:*)", "Bash(git rev-parse:*)",
      "Bash(git add:*)", "Bash(git commit:*)", "Bash(git fetch:*)", "Bash(git switch:*)",
      "Bash(git checkout --theirs:*)", "Bash(git checkout --ours:*)", "Bash(git merge --abort)",
      "Bash(git rm:*)", "Bash(git mv:*)", "Bash(git restore:*)", "Bash(mkdir:*)",
      "Bash(dotnet new:*)", "Bash(dotnet sln:*)", "Bash(dotnet add:*)", "Bash(dotnet run:*)",
      "Bash(git merge origin/platform-core:*)", "Bash(git pull --ff-only:*)", "Bash(git pull --rebase origin platform-core)",
      "Bash(git worktree add:*)", "Bash(git worktree list:*)",
      "Bash(git push -u origin pc/:*)", "Bash(git push origin pc/:*)", "Bash(git push origin HEAD:platform-core)",
      "Bash(gh pr checks:*)", "Bash(gh pr view:*)", "Bash(gh run list:*)", "Bash(gh run view:*)",
      "Bash(gh workflow run nightly-tier.yml:*)"
    ],
    "deny": [
      "Bash(git push --force:*)", "Bash(git push -f:*)", "Bash(git push origin main:*)", "Bash(git push origin HEAD:main:*)",
      "Bash(git push origin --delete:*)", "Bash(git branch -D:*)", "Bash(git reset --hard:*)",
      "Bash(gh pr merge:*)", "Bash(gh api:*)"
    ]
  },
  "autoCompactWindow": 400000,
  "autoContinueAtUsageLimit": true
}
```

Auto mode pauses for a human after 3 consecutive or 20 total classifier blocks. The run reports
every block to you at once (PROCESS.md §9). If a pattern repeats, widen the allowlist from your
phone or at the machine.

## 4. The usage reset

- The run spends this window's remainder tonight and tomorrow, then parks until Tue Nov 10, 20:00
  UTC (noon PT), and resumes by itself.
- In the window from Nov 10 it will probably run out around Thu Nov 12 or Fri Nov 13. It then
  parks and notifies you. **Apply the one-time reset whenever you see it.** The run checks every 30
  minutes and resumes by itself; there is nothing to answer.
- **Deadline: apply it by Sat Nov 14, 20:00 UTC.** Spending a full window takes the run 35–58 hours,
  and whatever is unspent at the Nov 17 reset is lost. If you are asleep when it parks, you lose
  only idle hours, not tokens.
- If the manual reset turns out to move the weekly reset time, the run detects it, recomputes the
  forecast and tells you.

## 5. Answers to the open questions

1. **Environment.** Local only. The laptop already has Docker with cached SQL Server images,
   Playwright browsers for every engine, .NET 10 and Node 24, and 64 GB for four slots. Cloud VMs
   have 4 vCPU and 16 GB, no SQL Server preinstalled and no setup script in the repo, and cloud
   sessions do not report back to a local orchestrator. A hybrid buys nothing here, because tokens
   rather than wall-clock time bind this run (PLAN.md §3.4). Linux coverage comes free from GitHub
   CI on every integration push.
2. **Ultracode at High.** Yes. It is a standing session mode at the session's effort. The
   orchestrator sets effort per agent through the Agent tool's and Workflow `agent()`'s `effort`,
   explicitly authorised by PROCESS.md. If effort turns out to be capped at the session level, raise
   the session to xhigh.
3. **Subagents vs side sessions.** Background subagents (the Agent tool) for the checkpoint cycle,
   because the implementer can be resumed with its context intact for both fix rounds and
   integration, as your process asks. Workflows only for batch reviews. Side sessions report back
   only a 400-character note (cloud ones nothing), and can't be orchestrated step by step.
4. **Models.** Opus everywhere by inheritance. Fable is used for Review 2 on the security list and
   for batch finders while its own weekly meter is under 90%; it costs about 1.8× Opus per token.
   No Sonnet or Haiku: the run is bound by quality and tokens, and rework from a weaker model costs
   more than it saves.
5. **Upgrading Opus mid-run.** Yes: pick the new model in the session's model picker. It applies
   from the next turn. Running agents finish on the old model, and new ones inherit the new one,
   since no Opus id is pinned.
6. **Not wasting tokens near the limit.** The orchestrator meters cost from transcripts (completion
   summaries report context size, not consumption) and gates every launch and step on usage. It
   then drains, parks and fills the last points with small work (PROCESS.md §6–7). Background agents
   caught by a weekly limit may fail, because the documented workflow pause covers only resets
   within 24 hours. But every step commits and pushes, and the slot keeps uncommitted files, so a
   failure costs at most one agent's reasoning, never days.
7. **Is ~3.5 weeks enough?** Not for everything (≈ 7.5 weeks central), and not quite for Tier 1
   (≈ 4.7 weeks central, range 3.4–6.5, against ≈ 3.6 available by Nov 20). The central
   expectation is the MVP gate at the end of November (around Nov 30 to Dec 2). Tier 2 follows early
   in the Dec 8 window, and Tier 3 (Excel, reports, dashboards, MCP, support, assignments) ends in the
   Dec 15 window. That is your Option B. Tiers keep whatever exists coherent and testable, and you get
   calibrated forecasts after 15 and 40 checkpoints. If Nov 20 matters more than review depth,
   PLAN.md §3.3 lists the two levers: together they save about 0.85 weeks
   and bring the MVP to around Wed Nov 25. A second subscription is not
   recommended.
8. **Slow tests.** There are many: full viewport × language matrices, Firefox/WebKit, frame and perf
   probes, the 15-minute hidden-leader soak, Lighthouse, a one-hour membership expiry, multi-minute
   job e2e, the 1M-row pivot. They move to the nightly GitHub workflow or are deferred (PLAN.md §6),
   and PC-000 makes CI exclude them from PR runs. No agent waits on them.
9. **Are the specs self-contained?** Yes, apart from the brand SVGs (§1 step 2) and the claude-web
   and codex-web callback URLs of 0010 §A.4. Those are optional seed entries, left out by default.
   The 0017 `.tmp` mention is a runtime folder the blob store creates. No spec specifies a
   watermark.
10. **If you're away at the critical moment.** The run never waits on a question. It parks,
    notifies you, and checks every 30 minutes, so it resumes within half an hour of the reset,
    whenever you apply it. AskUserQuestion would block the session and gain nothing. The one thing
    that can stall it is a permission pause, and that is notified immediately.

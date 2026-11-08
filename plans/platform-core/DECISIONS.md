# Platform-core decisions

Non-obvious rulings and process decisions, kept for Ahmad's review. Only the orchestrator edits this
file, in the main checkout. It gathers rulings from implementers' return values, review files and
its own choices. Obvious corrections stay in commit messages only.

Format, one entry per ruling:

```
## D-nnn (PC-nnn, YYYY-MM-DD): <short title>
- Spec: <spec §>
- Situation: <what the spec says or misses>
- Ruling: <what was built>
- Why: <reason>
- Revisit if: <condition>
```

## Carried in from planning (2026-11-07)

### D-000a: Tier 3 order
- Ruling: Excel import/export (9a) → reports (9b) → dashboards (9c) → assignments, support, housekeeping, document clients (9d).
- Why: real-data import makes human testing more productive. Ahmad may reorder before kickoff.

### D-000b: claude-web / codex-web seed entries (0010 §A.4)
- Ruling: left out unless Ahmad supplies the vendors' callback URIs. The ThirdParty flag and the other seed entries ship.
- Why: the spec does not give the URIs, and they must not be invented.

### D-000c: nightly test tier
- Ruling: the matrices, cross-engine runs, perf probes and soaks that specs place "on every PR" run nightly on GitHub Actions. The PR tier keeps a Chromium subset (PLAN.md §6).
- Why: otherwise each would stall every checkpoint cycle for 10–60 minutes on the laptop.

### D-001: batch reviews split into finders and a fixer
- Ruling: three read-only finders (Fable while its meter is under 90%) plus one Opus fixer that verifies each finding, fixes it or pushes back, and integrates (PLAN.md §8.2).
- Why: Ahmad's brief asked for one reviewer that fixes. Spec-round experience showed Fable finds cross-checkpoint gaps well, but per-file verification and editing go better on Opus.

### D-002: the implementer performs integration
- Ruling: after fix 2, the same implementer merges `platform-core` in, re-runs the gates and pushes (PROCESS.md §4.3).
- Why: it holds the context needed to resolve conflicts correctly.

### D-003: PC-000 and PC-Fnn fixes take Review 1 only
- Why: they are small infrastructure or targeted fixes. A second round would cost more than it finds.

### D-004: contract-first types for Tier-3 owners
- Ruling: 0015-a creates 0019's Excel row-source types and 0028's `BatchAssignments` family under their owners' names. `Assignments` throws `NotSupportedException` until 0028-a registers it.
- Why: 0015's contracts name them, and Tier 1 must not depend on Tier-3 code.

### D-005: 0010 §F.14 measures deferred
- Ruling: 0012-d's Rows and 0029-f's discover endpoint ship without measures. 0010-f6 (Tier 3) adds them to both.

### D-007 (orchestrator, 2026-11-08): agent addressing without names
- Situation: the Agent tool accepts `effort` and `model: "fable"` but has no `name` parameter (PROCESS.md §1 "Address agents stably").
- Ruling: no effort or Fable fallback is needed. Agents are addressed by the id from the spawn result (SendMessage `to: <id>`); LEDGER.md §In flight records the role label (`impl-PC-nnn`, `r1-PC-nnn`, …) next to the id.
- Revisit if: the Agent tool gains `name`.

### D-008 (orchestrator, 2026-11-08): audit before the first Tier-1 launches
- Ruling: the Tier-1 checkpoints with no gates (PC-001/002/003/004/008) wait until the kickoff dependency audit is applied and snapshotted, as KICKOFF.md orders, even though slots pc-2/pc-3 idle meanwhile.
- Why: a gate the audit adds may point to PC-001 (e.g. package pins), and a checkpoint launched on the old graph could not be ungated afterwards. The idle time costs wall-clock, not tokens.

### D-009 (orchestrator, 2026-11-08): kickoff dependency audit applied
- Situation: one auditor (inherit, `high`) read every Tier-1 entry's analyst notes and the cited spec sections, computed transitive closures over DEPS.md, and proposed 18 missing gates (14 high, 4 medium confidence), 6 ordering problems and 5 suspect gates (full report: WT/audit/dep-audit.md, outside the repo).
- Ruling, gates: all 18 applied to DEPS.md and the CHECKPOINTS.md "Gated on" lines: PC-049←037 (emitter), PC-072←042 (TreeCycleValidator), PC-092←089 (Development blob store/imaging), PC-116←110 (sign-out, error presenter), PC-119←058 (multi-picker), PC-121←031, 056 (tm-tree, popup cell editor), PC-124←120 (select plan, tmFilterLeaf), PC-125←119 (/entity scaffold, tm-value-editor), PC-126←111, 051, 031 (page actions, row-end templates, pager), PC-127←111, PC-138←079 (SettingsService), PC-039←032 (endpoint audit), PC-100←099 (real notifier in a composed host), PC-102←077 (member-only invoker), PC-136←122 (launcher). check_deps: 0 problems.
- Ruling, ordering problems: seams and planner notes added, tagged "(Kickoff audit, 2026-11-08.)": BlobRejectedException and ImportException seeded contract-first in PC-062 and mapped by type in PC-081 (consistent with D-004); a no-op TmLauncher seam in PC-118 (PC-122 replaces it); a TmPermissions seam token with an allow-all default in PC-103 (server stays the authority; PC-105 replaces it); an IStackRegistry seam line in PC-025; PC-124 extends PC-105's tile/custom-filter/page-action declaration types. PC-098's fake notifier and the launch-landing vector file location were already covered.
- Ruling, suspect gates: all 5 kept (PC-046←044, PC-126←041, PC-087←055, PC-038←022, PC-059←013). Removing a gate risks a broken build; each costs little because the other gates of the same checkpoint (or its consumers) already wait on as deep a chain. PC-059←013 is treated as a deliberate serialization of shared core-ui files.
- Revisit if: an implementer reports a gate as unnecessary or another missing; a new gate may only point to an earlier checkpoint.

### D-010 (PC-000, 2026-11-08): the PR tier keeps @cross-engine tests on Chromium
- Situation: the PC-000 entry said to grep-invert `@nightly`, `@cross-engine` and `@perf`. In this repo `@cross-engine` marks tests that run in full on Chromium plus a Firefox/WebKit subset, so excluding the tag would drop about 40 Chromium PR tests.
- Ruling: the PR tier runs `node scripts/e2e.mjs --project chromium --project touch --grep-invert "@nightly|@perf"` (CONTRIBUTING.md); Firefox/WebKit runs are nightly. PROCESS.md §11 and PLAN.md §6 updated to match.

### D-011 (PC-000, 2026-11-08): flaky tests must fail CI, not retry green
- Situation: CI Playwright ran `retries: 2`, which turns a flaky test green silently, against PROCESS.md §5.3.
- Ruling: CI keeps retries only if a test that passes on retry still fails the job (or retries go to 0); the flake then gets quarantined and tracked under LEDGER.md "Flaky".

### D-012 (PC-000, 2026-11-08): nightly after the merge into main is a merge-time task
- Ruling: platform-core's `nightly-tier.yml` stays guarded to `refs/heads/platform-core`. At the platform-core → main merge, restore its schedule and a main guard (the trampoline is replaced), and drop the platform-core sentences in CONTRIBUTING.md and the `.gitattributes` plans/** line with plans/. Tracked in LEDGER.md §Merge-time tasks.

### D-013 (orchestrator, 2026-11-08): when the 4th cycle is allowed
- Situation: PROCESS.md §5.1 allows 4 cycles "while the ready queue holds both backend and client work". After PC-000 integrated, the in-flight cycles were all backend (PC-001/002/003) and the ready queue all client (PC-004/008).
- Ruling: count in-flight and ready work together. A 4th cycle runs when the 4 cycles would mix backend and client work, since the limit exists to avoid CPU and merge contention between same-layer builds.

### D-014 (orchestrator, 2026-11-08): a dedicated fix slot for stop-the-line fixes
- Situation: PROCESS.md §8.3 assigns a free slot to a PC-Fnn fix, but all four slots held in-flight checkpoints when PC-000's integration turned CI red (five flaky e2e tests, previously hidden by retries, now fail the run per D-011).
- Ruling: a fifth worktree `WT/pc-f` serves only PC-Fnn fixes. Fixes are small, so it adds little CPU or merge contention, and the line restarts without waiting for a lane to free. The five tests are quarantined first (PC-F01); their root-cause fix is a separate queued item (PC-F02).
- Revisit if: the fix slot ever holds more than one fix at a time.

### D-015 (PC-003, 2026-11-08): Queryex host-composition rulings reported by the implementer
- Zones: Antarctica/Troll is left out (no CLDR Windows mapping), giving 418 current ids; the tzdb release behind the checked-in zone.tab ids is pinned to 2026b (WATCHLIST gap).
- A filter that folds to false drops the restrictions and their joins; QX5006 raised from a restriction reports location "Restrictions[i]".
- The Queryex integration fixture writes COLLATE Latin1_General_100_CI_AS literally; whichever of PC-003 / PC-002 integrates second switches it to `DatabaseCollations`.
- Seam: string key sets can hit SQL Msg 468 unless the host's list types carry the column collation (0010-G, PC-005, and spec 0012 must honour it).
- Also fixed a latent `BindKey.Equals` bug (version was never compared).
- Revisit if: Review 1/2 of PC-003 disputes any of these.

### D-016 (PC-001, 2026-11-08): composition-root rulings that bind later checkpoints
- `IClientAssetContributor`, `ClientAssetContext`, `ClientMetadataChunk` and `TellmaRoutes` are created by PC-001 (Tellma.Core.Abstractions.Client / .Composition). PC-015 (contract seed) extends them and never re-declares them (planner note added to PC-015).
- A `[CollectionService]` attribute replaces the fixed collection-service exemption list. Every checkpoint that owns a multi-registration interface marks it, including `IRequestContextInitializer`, `ITenantStateListener`, `ISessionTerminationListener`, `IAccessCriteriaProvider` and `INotificationChannel`.
- Declared options are validated by the `core.options` check inside the startup gate, not by `ValidateOnStart` (which would abort before the gate aggregates). Platform options from 0011 §8 bind later through `OptionsRegistrar`.
- Contributions are collected before the graph gate, so nested features join it; `TellmaComposition.Validate` also returns realisation problems.
- Every Microsoft 10.0.x pin and dotnet-ef move to 10.0.11 (one servicing line). Identity and other migrations are regenerated with dotnet-ef 10.0.11 after this lands (PC-002 first).
- Revisit if: Review 1 of PC-001 disputes any of these.

### D-006 (Ahmad, 2026-11-08): review levers
- Ruling: Review 2 runs only for the PROCESS.md §3 two-review list (security-bound or complex, 44 checkpoints) and for escalations (§4.4); every other checkpoint gets Review 1 and one fix round. Cross-checkpoint batch reviews run once per phase instead of per group.
- Why: Ahmad traded some review depth for about 0.7 weeks of Tier-1 budget (PLAN.md §3.3). The escalation rule keeps a second review wherever Review 1 shows trouble.

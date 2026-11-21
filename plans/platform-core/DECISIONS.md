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
- After Review 2: `level()` accepts any HierarchyId-typed operand (null, coalesce, if produce one, and SQL Server accepts GetLevel() on each); the spec's "only a path" premise is corrected in code and recorded as a deviation. The `Restrictions[i]` location applies only to joins no user clause needs (restrictions are lowered after all user clauses).
- Revisit if: Review 1/2 of PC-003 disputes any of these.

### D-016 (PC-001, 2026-11-08): composition-root rulings that bind later checkpoints
- `IClientAssetContributor`, `ClientAssetContext`, `ClientMetadataChunk` and `TellmaRoutes` are created by PC-001 (Tellma.Core.Abstractions.Client / .Composition). PC-015 (contract seed) extends them and never re-declares them (planner note added to PC-015).
- A `[CollectionService]` attribute replaces the fixed collection-service exemption list. Every checkpoint that owns a multi-registration interface marks it, including `IRequestContextInitializer`, `ITenantStateListener`, `ISessionTerminationListener`, `IAccessCriteriaProvider` and `INotificationChannel`.
- Declared options are validated by the `core.options` check inside the startup gate, not by `ValidateOnStart` (which would abort before the gate aggregates). Platform options from 0011 §8 bind later through `OptionsRegistrar`.
- Contributions are collected before the graph gate, so nested features join it; `TellmaComposition.Validate` also returns realisation problems.
- Every Microsoft 10.0.x pin and dotnet-ef move to 10.0.11 (one servicing line). Identity and other migrations are regenerated with dotnet-ef 10.0.11 after this lands (PC-002 first).
- After Review 1 (orchestrator rulings): override is allowed only along transitive dependencies (requires, nesting, the implicit core); two unrelated features registering one non-collection service type is a graph-gate problem. Registrations made directly on `tellma.Services` (host store choices) stay outside that rule. Framework multi-registration types (IHostedService, IHostedLifecycleService, IConfigureOptions<>, IConfigureNamedOptions<>, IPostConfigureOptions<>, IValidateOptions<>, IOptionsChangeTokenSource<>, IStartupFilter, IStartupCheck) are collection services by definition. A contributed check that cannot be created is reported, and the built-in checks still run. `Tellma:PublicOrigin` refuses a trailing slash.
- Revisit if: Review 1 of PC-001 disputes any of these.

### D-017 (PC-002, 2026-11-08): identity-store rulings and integration order with PC-001/PC-003
- The shared collation pin (and a latest-migration pin) lives in Tellma.Core.Testing/Database, which adds a Microsoft.EntityFrameworkCore.Relational line to Directory.Packages.props. Whichever of PC-001 / PC-002 integrates second raises it to 10.0.11 with the other EF pins (else NU1605) and regenerates the identity migration with dotnet-ef 10.0.11.
- PC-002 also moved the EF and Queryex integration fixtures onto the platform collation, so PC-005's fixture deliverable is already done (PC-005 verifies, does not redo). PC-003's literal COLLATE in the Queryex fixture switches to the shared pin at whichever integration comes second (D-015).
- A Development Distribution entry without secrets fails seeding: the seeder never generates a secret. Spec 0011's `AddTellma` path (the in-proc identity mode, PC-047, and the Acme distribution, PC-085) must supply per-boot development secrets.
- Pre-existing, not fixed: StandaloneFactory removes user secrets only at Build(), so registration-time reads in local test hosts see the developer's secrets (CI unaffected). Queued for the 0011 host checkpoints to settle.
- Revisit if: Review 1/2 of PC-002 disputes any of these.

### D-018 (orchestrator, 2026-11-08): systemic e2e flakiness is root-caused, not quarantined test by test
- Situation: after PC-F01 quarantined five flaky tests, CI on a plans-only snapshot (5250c6c, run 42456030333) went red on four different flaky tests; the run on d1ab25e in between was clean. Each run of the ~435-test PR tier flakes on a few tests, which retries: 2 used to hide.
- Ruling: the line stays stopped and PC-F02 root-causes the flakiness across all nine tests (CI worker count vs runner vCPUs, focus-dependent assertions, timing waits, animations), fixes it deterministically and un-quarantines what it fixes. Quarantining each new flake would empty the suite piece by piece and leave every integration's verdict a coin toss. PC-000 and PC-F01 are `done` on d1ab25e's green run; 5250c6c's red is the flake, not a regression.
- Revisit if: PC-F02 cannot reach a reliably green PR tier; then cut CI workers to 1 as a stopgap and quarantine the rest, recorded here.

### D-019 (PC-004, 2026-11-08): kbd grammar refuses Shift with a symbol key; icon/kbd size ceilings
- Spec: 0023 §3.2 (shortcut grammar), §1 (entry-point budgets).
- Situation: §3.2 is silent on Shift beside a symbol key; a binding such as `Mod+Shift+/` never matches on a US layout, because Shift changes the produced character. The measured ./icon and ./kbd bundles exceed §1's estimates.
- Ruling: the grammar rejects Shift combined with a symbol key at parse/registration time and names the shifted symbol to bind instead. The ceilings are ./icon 3 KB and ./kbd 2.5 KB, as real logic weight; the budget notes state the measured reason.
- Revisit if: a layout-independent key-code binding is ever needed (spec 0027's shortcut registry).

### D-020 (PC-015 + orchestrator, 2026-11-08): contract-seed rulings and the owners of unseeded types
- Specs: 0015 §2.3, §14.1; 0016 §1.3, §4.1, me payload; 0017 §1.1; 0021 descriptors; 0014 §4.5.
- Spec conflicts (implementer rulings, kept):
  - [Stack] has no DefaultSelect. 0015 §2.3 lists one, but 0016 §4.1 and every consumer use [DefaultSelect].
  - TellmaException does not check its Code's grammar. 0015 §14.1 says kebab-case, but 0017's BlobRejectedException carries dotted Blob.* codes.
  - TellmaException's constructor is private protected, so the exception set is closed at compile time and every owner lives in Abstractions.
- Long ids in ConcurrencyConflict.Id and ConfirmationRequest.Ids serialize as strings until PC-064 adds the problem-body id display rule.
- Owners assigned by the orchestrator, with planner notes added:
  - The MeResult family (MeResult, UserProfileView, AccessSummary) → PC-090, the self-service me endpoint.
  - ApiOptions.ClientNames → PC-032, which creates Tellma.Core.AspNetCore and its CSRF rule.
  - PC-035 and PC-039 extend the seeded notification descriptors and endpoint metadata.
  - PC-018's Queryex edge and CI pack step are already done.
- Unknown-member counting (tellma.api.unknown_members) stays with 0016-e, on the web host's HTTP JSON options.
- Scratch files: agents shared one scratchpad folder, and PC-015 may have overwritten another agent's draft (commit1.txt, build/format logs). From now on every brief tells agents to keep scratch files under a subfolder named for their PC id and role.
- Revisit if: a later spec section re-declares a seeded type, or the closed exception set blocks a distribution-owned exception.

### D-021 (orchestrator, 2026-11-08): TellmaJson writes non-ASCII text unescaped
- Spec: 0016 §3 (TellmaJson options). The spec does not say which encoder to use.
- Situation: the default System.Text.Json encoder writes Arabic and Amharic as \uXXXX. That costs about six times the bytes on the wire, in stored JSON, and against MCP's MaxToolResultChars, for a product whose main markets write in those scripts.
- Ruling: TellmaJson uses `JavaScriptEncoder.Create(UnicodeRanges.All)`. HTML-sensitive characters (<, >, &, ', ") stay escaped, so JSON embedded in HTML stays safe. UnsafeRelaxedJsonEscaping is never used. A test pins both halves.
- Revisit if: a consumer needs ASCII-only JSON (none known).

### D-022 (orchestrator, 2026-11-08): a blob reference's MaxSizeBytes only narrows its preset
- Spec: 0017 §2.3.
- Situation: PC-017's implementer made a BlobReferenceAttribute.MaxSizeBytes above 0 replace the preset's MaxSize, so a declaration could widen an upload limit. The concern was that a silent clamp would hide the declared value.
- Ruling: the spec stands. The effective limit is min(attribute, preset), and an attribute value larger than its preset is refused at model validation with a clear message, never silently widened or clamped. MaxUploadSize still caps everything.
- Also accepted from PC-017:
  - the kind grammar refuses Windows device names;
  - the Windows RootPath cap is 153 characters (root + 106 = 259);
  - listing prefixes are whole segments (BlobName.IsValidPrefix), with planner notes on PC-083 and PC-089.
- Revisit if: a real stack needs a reference larger than every preset (add a preset instead).

### D-023 (PC-008 + orchestrator, 2026-11-08): eval-free in-house ICU interpreter; date/time through a formatter seam; primary at 8 KB
- Specs: 0010 §B.3, the PC-008 planner note on the CSP (0024 §6: no unsafe-eval, Trusted Types), 0013 §10.2, 0023 §10 and 0002.
- Ruling (implementer): @messageformat/core compiles messages with new Function, so it and @jsverse/transloco-messageformat were dropped. An in-house interpreter now handles every tmUi, tm and string-pack message. A CSP test pins it.
- Ruling (orchestrator, from R1-2): `{x, number|date|time}` arguments go through an injectable formatter seam. The default date/time output is ISO 8601. Spec 0024's TmServerText (PC-104) supplies the calendar-aware formatter.
- Budget: the primary ceiling is 8 KB, not the planned 7 (measured 7.04 KB; the interpreter costs about 1.4 KB). PC-011 and the other Part D checkpoints must re-measure primary before adding to it. ./grid sits at 38.00/38 KB.
- **Misalignment for Ahmad:** specs 0002 (around line 857) and 0023 §10 (around line 1300) still name @jsverse/transloco-messageformat. The code and ARCHITECTURE.md use the in-house interpreter, so the specs are the stale side.
- Revisit if: a CSP-safe upstream ICU engine appears that is smaller than the in-house one.

### D-024 (orchestrator, 2026-11-08): file-system blob store publishes create-only with link(2) outside Windows
- Spec: 0017 §1.3 and §2.3 (blobs are immutable; a second write of the same name raises BlobAlreadyExistsException).
- Situation: on Unix, .NET 10's File.Move(overwrite: false) runs lstat and then rename(2), and rename replaces an existing file. PC-017's Review 1 measured both racing writers succeed in 1313 of 2000 trials on Linux, against 0 on Windows. A racing writer silently replaced an immutable blob.
- Ruling:
  - Outside Windows, publish by hard-linking the finished part file to its destination with link(2), called through LibraryImport. EEXIST means the blob already exists. The part file is then unlinked.
  - renameat2(RENAME_NOREPLACE) is rejected: it is Linux-only, and some filesystems lack it.
  - A filesystem without hard links fails closed. The store's probe refuses the root path at startup, and there is never a fallback to rename.
  - Windows keeps File.Move without overwrite, which is atomic there.
  - The abstract conformance suite gains a concurrent same-name race, which binds every future adapter, Azure included.
- Revisit if: .NET ships a portable create-only move.

### D-025 (orchestrator, 2026-11-08): TellmaJson refuses undefined enum values
- Spec: 0016 §3.1 (enum wire form via JsonStringEnumConverter<TEnum>).
- Situation: PC-015's Review 2 showed that the spec'd converter reads comma lists on non-[Flags] enums. "ReadOnly, Hidden" becomes an undefined McpExposure 3, and "B, C" silently becomes D. Spec 0012 stores enums as varchar with no CHECK constraint, and writing an undefined value back throws, so a single save would turn every later read of that row into a 500.
- Ruling: the TellmaJson enum converter refuses any value that is not a defined member, in both Read and ReadAsPropertyName. For [Flags] enums it refuses any undeclared bit, and a non-[Flags] enum refuses a comma list outright. A refused value is a normal JSON binding error (400).
- **Spec clarification for Ahmad:** 0016 §3.1 should state this rule. As written, the spec's converter choice allows the hole.
- Revisit if: a wire contract ever needs forward-compatible unknown enum values (none does; the client and server ship together).

### D-026 (PC-006 + orchestrator, 2026-11-08): management API rulings
- Spec: 0010 §A.6-§A.8.
- The tellma.identity.api.calls counter and its log line cover every call to a management route, including 401 and 403, as the spec says. They carry an outcome tag (ok, unauthorized, forbidden, error) alongside version and endpoint.
  - Rejected calls are recorded by a decorating IAuthorizationMiddlewareResultHandler, because AuthorizationMiddleware answers before MVC filters run.
  - A 403 names the client. A 401 has no client to name.
- Implementer rulings accepted, where the spec is silent:
  - existingOnly ignores ReturnUrl, since no link carries it.
  - existingOnly on an orphaned user who holds a credential answers Active and restores the user. The restore is audited with the calling client's id.
  - The endpoint tag values are a closed set of operation names (for example invitations.invite), and the version tag is "v1". Dashboards key on these values.
- Revisit if: a v2 management API is added (the tag values must stay stable across versions).

### D-027 (PC-007 + orchestrator, 2026-11-08): Exists paths stay out of DiscoveryResult.Paths
- Spec: 0010 §F.12, §F.13 and §F.21.
- Conflict: §F.21 says Via and Exists nodes' "paths still appear in Paths", but §F.12 says Exists reports no PathUse.
- Ruling: §F.12 and §F.13 govern. Exists paths do not appear in Paths. §F.21's sentence covers Via only, with Via's prefix. PC-101, which owns §F.21, has a planner note.
- Also from PC-007:
  - Lowering-time ceiling diagnostics (QX5006/QX5007) inside a composed filter report the leaf's own location, for example Filter.And[1]. This fixes a latent bug against spec 0008 §2.3.
  - The fixture has no optional centre, so the "lines with no center" differential uses invoices with no customer.
- Revisit if: a discovery consumer needs to know which entities an Exists touches.

### D-028 (orchestrator, 2026-11-08): cap concurrent heavy agents on the laptop
- Situation: with four checkpoint lanes, the fix slot and their reviewers all building and running suites, the laptop hit its limits around 19:00-21:00. Symptoms:
  - about 47 dotnet and 34 headless-chrome processes;
  - PR-tier e2e at 465/478 from timeouts;
  - the showcase's esbuild service stopping;
  - CREATE DATABASE timeouts;
  - one 30-minute Identity suite.
- These are false failures that cost reruns and blur real flakes. PROCESS.md §5.1 caps checkpoint cycles at 4, but it does not count the fix slot or reviewers.
- Ruling: at most 5 agents that build or run tests at the same time, counting implementers, reviewers and fix agents. A step that would exceed it waits for the next completion. Steps that only read are exempt. Client e2e runs are the heaviest, so the scheduler prefers not to overlap more than two client lanes.
- Revisit if: local timeouts persist at 5, or vanish and the window has points to spare.

### D-029 (PC-016 + orchestrator, 2026-11-08): one calendar authority, host-independent server text
- Spec: 0013 §5-§6 and Appendix A; 0024 (client localization).
- Umm al-Qura:
  - Situation: .NET's tables, which spec 0013 mandates, and browser ICU agree only for AH 1420-1450 (1999-2029). Outside that span they differ by one day on 35,920 of 64,850 days. .NET matches the historical anchor 1 Muharram 1400 = 1979-11-20; ICU gives 1979-11-21.
  - Ruling: .NET is the single authority, used by the server and by Queryex's core.CalendarMonths. The client converts Umm al-Qura dates with a month-start table generated from .NET over its supported range and shipped as data, never with Intl's islamic-umalqura. PC-104 owns the client side (planner note).
- Server text:
  - Situation: Gregorian month names and date patterns come from the host's ICU, so Windows and Ubuntu differ. For example, the en-GB full date has a comma on only one of them, and am-ET month spellings differ.
  - Ruling: server text is host-independent. PC-019's negotiated culture clone pins the CLDR names and patterns for every supported language (planner note). Until then, PC-016's goldens run over pinned culture clones.
- Accepted from PC-016:
  - The calendar contribution item, Calendar<T>(), its realizer and the CoreFeature registrations landed in PC-016, because no checkpoint listed them.
  - StaleContextException (0013 §2.9) joins the closed exception set.
- **Spec error for Ahmad:** Appendix A's prose rule ("12 September in the year before a Gregorian leap year") is wrong for 2099. 1 Meskerem 2092 falls on 2099-09-12 (ICU agrees with the code). The code follows the arithmetic, not the prose.
- Addendum (PC-016 Review 1): array setting values are ImmutableArray<T> rather than T[], a deviation from 0013 §5.3. A tenant snapshot is then shared safely without copying. The client's Umm al-Qura fallback outside the table is islamic-tbla, which equals .NET's HijriCalendar with adjustment 0.
- Addendum (PC-016 fix 1): the server's Umm al-Qura calendar is UmAlQuraExtendedCalendar. It is the .NET table, plus the tabular calendar (adjustment 0) outside AH 1318-1500, and it is valid from AD 622-07-18. Muharram 1501 begins 2077-11-17 and has 29 days, so the table wins the one overlap. A date before a calendar's epoch renders as "yyyy-MM-dd gc", which TryParse refuses.
- Addendum (PC-F09): CI's ubuntu-latest is now ubuntu-26.04 with libicu 78.2. From ICU 78, ar-SA's default calendar is Gregorian, with Umm al-Qura only optional. Other CLDR data moved too: en-GB's full date has its comma again, and ti-ET's short date is M/d/yyyy. Calendar tests therefore build their culture clones explicitly and never rely on host defaults. Local Linux checks use mcr.microsoft.com/dotnet/sdk:10.0-resolute (ICU 78) as well as sdk:10.0 (ICU 74). PC-019's pinned culture data must be chosen independently of either ICU.
- Revisit if: the client must convert dates outside .NET's Umm al-Qura range (AH 1318-1500).

### D-030 (PC-053 + orchestrator, 2026-11-08): imaging rulings
- Spec: 0017 §1.1, §3.5, §3.6, §9 and §10.
- Accepted from the implementer:
  - IImageProcessor takes `kind` on both methods and a `timeout` on ThumbnailAsync. The processor records tellma.blobs.image.* and BlobImageRender itself, so PC-083's BlobService passes the kind and BlobOptions.ThumbnailTimeout and does not record those instruments again.
  - CoverSquare never upscales: side = min(MaxDimension, shorter side). The spec says "scales to MaxDimension", but upscaling only adds bytes and blur.
  - A `core.imaging` startup check confirms that Skia and PDFium load.
  - The imaging instruments live on the Tellma.Core meter, as an exception to naming meters by package.
- Orchestrator rulings:
  - ProcessAsync (normalisation) gets a time budget like ThumbnailAsync. Native decode cannot be stopped mid-call, so image work also runs behind a bounded concurrency limit: a hostile file within the pixel cap can then waste one slot but never starve the host.
  - Native .pdb files from SkiaSharp's Win32 package (about 100 MB per Windows RID) are excluded from build and publish output.
  - PDFium exposure is bounded: render page 1 only, and cap page size and decoded raster size before rendering.
- Licences: SkiaSharp and PDFtoImage are MIT, and the PDFium builds are Apache-2.0. No AGPL or GPL dependency is used.
- Addendum (PC-053 Review 1, which escalated it to Review 2):
  - New options: BlobOptions.ImageProcessTimeout (default 15 s) and ImageConcurrency (default max(1, ProcessorCount/2)).
  - Processing over its budget raises ImageRejectedException (reason timeout, Blob.ImageRejected).
  - A busy gate that cannot be entered in time is a distinct 503 with Retry-After.
  - The gate is released only when native work truly ends.
  - PDFium's page 1 is pre-walked: image dimensions (JPX and JBIG2 included), object count, page size (14,400 pt) and input length are capped, and it fails fast while an abandoned render holds the gate.
  - The processor never reads the caller's stream after it returns.
  - **Residual risk:** memory used by PDFium's non-image content and PDFium bugs stay unbounded until out-of-process rendering.
- Addendum 2 (PC-053 Review 2, R2-1): form-XObject fan-out expands inside FPDF_LoadPage, before the object cap is checked. So a small PDF can still grow memory exponentially in-process, and the input cap does not bound that parse. A failed or refused thumbnail never fails the upload. **Out-of-process PDF rendering (PC-F08) is required before the MVP gate (PC-140).** Until then this is recorded residual risk, limited by the fact that only an authenticated user with upload rights can trigger it.
- Evidence (PC-053 fix 2): a 1.4 KB FormFanOut(depth 6, breadth 10) file expands to about 210k objects inside the uninterruptible FPDF_LoadPage. That costs 3-11 s of single-threaded CPU in mcr.microsoft.com/dotnet/sdk:10.0 against about 1 s on Windows, holding the PDF gate throughout. The pushback on a "finishes inside 5 s" assertion is accepted. The test asserts refusal (decode-failed, never timeout) and reports the cost. PC-F08 is promoted to the next fix-slot item after the current stop-the-line fix.
- Addendum 3 (PC-F08):
  - PDF thumbnails render in a per-request worker (`dotnet exec`, the host's runtimeconfig and deps.json), killed at 5 s.
  - Memory is capped by the OS: a Windows Job Object, and Linux prlimit RLIMIT_DATA, because RLIMIT_AS does not cap the managed heap. The cap is Tellma:Blobs:PdfWorkerMemoryLimit (256 MiB).
  - Linux workers are denied network by seccomp.
  - Hosts must run framework-dependent; core.imaging refuses single-file and self-contained hosts.
  - New package Tellma.Core.Imaging.PdfWorker, which the release pipeline publishes with Tellma.Core.Imaging.
  - **Residual risk:**
    - No filesystem confinement (no Landlock or AppContainer), so a PDFium code-execution exploit could read what the host user can read and leak it through pixels.
    - No network block on Windows, which has no unprivileged mechanism for it.
    - Ruling: Linux is the supported production host for PDF thumbnails. Filesystem confinement is PC-F11, tracked for before general availability, not the MVP.
  - D-030 addendum 1's fail-fast on an abandoned render no longer applies, because the worker is killed.
- Addendum 4 (PC-F08 Review 1): the worker must contain exploit code, not just PDFium's ordinary behaviour.
  - Linux: RLIMIT_AS becomes a real cap (a smaller GC reservation). Seccomp denies SysV IPC, tkill and the pidfd calls, and allows kill, tgkill and prlimit64 only on the worker's own pid. Capabilities are dropped with no_new_privs set.
  - Windows: workers run under a restricted, low-integrity token, so they cannot OpenProcess the host. If that proves infeasible, Windows PDF thumbnails are disabled outside Development.
  - PDF workers get their own bound, Tellma:Blobs:PdfWorkerConcurrency (default max(1, ProcessorCount/4)), so image uploads never wait behind them.
- Addendum 5 (PC-F08 fix 1, blocked and ruled):
  - Windows workers run under a restricted low-integrity token, so they can no longer read, write or inject into the host. They can still terminate it, and same-user low-integrity workers may open each other. **PDF thumbnails are therefore disabled on Windows hosts outside Development.**
  - The GC heap hard limit is set on Linux only, because the Windows Job Object already caps memory.
  - Linux RLIMIT_AS = VmSize + limit + measured slack.
  - core.imaging warns when Yama ptrace_scope is 0, and disables PDF thumbnails on musl. Production Linux hosts are glibc.
- Revisit if: out-of-process rendering becomes necessary (a hostile-PDF memory exhaustion seen in the wild).

### D-031 (orchestrator, 2026-11-09): bundle ceilings at their limit
- Situation: after PC-011, three ceilings are almost full:
  - ./popover at 4087/4096 B;
  - ./grid at 38909/38912 B;
  - ./image at 10187/10240 B.
  Any further feature on those entry points will cross its ceiling. Size ceilings exist to catch accidental bloat, not to block spec'd features.
- Ruling: a checkpoint whose spec'd additions cross a ceiling may raise it, by at most what it adds rounded up to the next 0.5 KB, under three conditions:
  - it first looks for and takes honest savings in code it touches;
  - it adds a budgetNotes entry naming the feature, the bytes it adds and the savings it took;
  - its reviewer checks the measurement.
  A raise that only absorbs accidental growth (a new dependency, a duplicate) is never allowed.
- First use: PC-011's review 1 fixes (popover clamp against classic scrollbars, and a stale height cap on scroll or resize) may raise ./popover under this rule. Its ruling on disabled reasons: a reason on a button that has tmTooltip shows the tooltip text first and then the reason, never replacing the only visible label of an icon-only button.
- Revisit if: a ceiling rises three times; then re-split the entry point instead.

### D-032 (PC-065, 2026-11-09): client commands after the workspace moves to the repo root
- Takes effect once PC-065 integrates. Every client command then runs from the slot root, as `cd <slot> && …`; the old `cd <slot>/client && pnpm run <gate>` still works.
  - Install: `pnpm install --frozen-lockfile`.
  - Gates: `pnpm run lint|typecheck|lint:test|tokens:check|build|test|api:check|docs:build|size:check|build:showcase`.
  - Tokens CSS after a branch switch: `pnpm run tokens:build-css`. API goldens: `pnpm run api:approve`.
  - Browsers: `pnpm --dir client exec playwright install chromium`.
  - PR-tier e2e: `node client/scripts/e2e.mjs --project chromium --project touch --grep-invert "@nightly|@perf|@quarantine"`.
- The lockfile is /pnpm-lock.yaml. Dependency versions live in the root pnpm-workspace.yaml `catalog` (`catalogMode: strict`) and are referenced as `catalog:`. A lane that merges across the move resolves a modify/delete conflict on client/pnpm-lock.yaml by taking the delete and running `pnpm install` at the root.
- CI gains a `client-windows` job that runs every client gate except e2e (spec 0024 §12), and all-checks-passed requires it.
- The @tellma/core entry-point graph is data in core-tools (src/eslint/core-entry-points.ts) and is checked for cycles. Later owners add their lazy-to-lazy edges to that table.
- Briefs after PC-065 use these commands. PROCESS.md §11 already anticipates the move.

### D-033 (PC-018 + orchestrator, 2026-11-09): entity-model rulings
- Spec: 0012 §1.3, §1.4, §4.1, §4.2 and §6.4; 0011 §3.5, §6.1 and §6.4.
- datetimeoffset(3): every non-audit DateTimeOffset column, on entities and on standalone table types, maps to datetimeoffset(3) through a convention. The exceptions are the audit pair, IdStampList at (7) and the datetime2(7) periods.
- EF's ForeignKeyIndexConvention is removed globally, so indexes exist only where the spec declares them. The audit FKs (CreatedById/ModifiedById) carry no index. This matches §4.2/§1.3, and 0014 §3.9 limits user deletes to New users. A Core configuration's FK gets an index only if the configuration declares one.
- PK and FK constraint names are deterministic for explicit configuration too, so §6.4's 547-by-name mapping always matches.
- Every table type grants EXECUTE to tellma_app with no guard, as 0011 §3.5 and §6.4 say.
- **Spec contradiction for Ahmad:** 0011 §6.1 says the design-time `dotnet ef database update` database "has no tellma_app". The grant then fails there with Msg 15151, reproduced on LocalDB. PC-034's design-time factory creates the role first (planner note). §6.1 should say so.
- Revisit if: a query plan shows the missing audit-FK indexes hurting a real workload.

### D-034 (PC-009 + orchestrator, 2026-11-09): resource grants and seeds
- Spec: 0010 §A.2, §A.4 and §A.5; D-000b.
- Accepted from the implementer:
  - One decision point, ResourceGrantValidator over IResourceGrantEvaluator, with OpenIddict's built-in resource checks turned off.
  - A refused resource answers invalid_target, as §A.2 says.
  - Token exchange without a resource no longer issues an audience covering every distribution (a cross-distribution bug, fixed).
  - Narrowing applies to the access token only; the rotated refresh token keeps the original grant (RFC 8707).
  - Device flow resources are validated and carried.
  - The control plane keeps its two exact platform rsrc permissions and is recognized by scp:tellma_control_plane.
- Pre-existing gap fixed: an authorization request that OpenIddict refused still reached Authorize and Accept and started a sign-in. Both now re-raise the refusal.
- Orchestrator rulings:
  - EndSessionController follows the same rule: a refused end-session request never signs the user out.
  - §A.4's interim agent seeds ship, as D-000b says. They use a key-addressed form, never index-merged Seed:Clients array entries, so no deployment's entries are corrupted. Their redirects are RFC 8252 loopback (127.0.0.1, any port) with a documented default path that a deployment can override.
- Addendum (PC-009 Review 1), stricter than the spec and stated as deviations:
  - A machine token that names a distribution origin together with any other resource (more distributions, the control-plane audience or the issuer) is refused with invalid_target.
  - On the interactive path, an explicit distribution resource is the whole aud, and tellma_identity together with a distribution resource is refused. A client that needs both asks for two tokens.
  - This keeps §A.5's "one token, one place" property and closes the replay of a CLI token against the management API.
  - The seeds ship as TellmaIdentitySeedOptions.InterimClients, keyed by client id with an Enabled flag. Each is Native, RequireConsent and ThirdParty, with redirect http://127.0.0.1/callback (any port).
- Addendum 2 (PC-009 Review 2):
  - Interactive grants naming resources from more than one distribution origin are refused with invalid_target; same-origin combinations stay allowed.
  - The identity management API's audience is `urn:tellma:identity` in every mode, standalone and in-proc, so it never equals a distribution origin. This deviates from §A.5's "issuer origin".
  - The resource check runs after OpenIddict's PKCE, presenter and redirect checks.
  - A disabled interim seed whose client exists logs a startup Warning.
  - An end-session request without an id_token_hint shows a confirmation page, so a bare GET cannot log a user out (CSRF).
  - **Spec clarifications for Ahmad:** 0010 §A.5 (the management audience) and 0003 (end-session confirmation).
- Revisit if: Ahmad supplies the agents' real callback paths (they then replace the default).

### D-035 (orchestrator, 2026-11-09): a second fix slot
- Situation: PC-065's integration turned CI red while the fix slot pc-f held PC-F08, a long out-of-process rendering fix, and all four checkpoint slots were busy. D-014's revisit condition, the fix slot holding more than one fix, was met.
- Ruling: `WT/pc-f2` (ports 4360/7160/4260, DB_SUFFIX pcf2) serves stop-the-line fixes whenever pc-f is busy. PC-F10 is its first use. The D-028 heavy-agent cap of 5 still applies; a stop-the-line fix may exceed it by one, because the line is stopped.
- Revisit if: both fix slots are ever busy at once.

### D-036 (PC-024 + PC-060, 2026-11-09): spec errors found during implementation
- **For Ahmad, spec corrections needed:**
  - 0011 §7.5: the registration statement cannot run, because SQL Server does not allow NEXT VALUE FOR inside COALESCE. The code draws the id in its own statement and computes the default `<prefix>.<id>` name in SQL.
  - 0011 §3.8 and 0014: subjects must be printable ASCII without spaces, because SQL Server ignores trailing spaces when comparing, even under BIN2.
  - 0010 §B.4: an unmatched tm-select value shows the placeholder (spec 0002 §3.4), not String(value) with a dev warning. String(value) applies to tm-multi-select only.
- Also from PC-024:
  - Outside Development, error 4060 at startup leaves the host started but not ready, rather than failing it.
  - core.sql-profiles also governs the catalog connection.
  - ITenantRegistry.IsAvailable is added; a stale or never-loaded snapshot makes Find and Get throw catalog-unavailable.
- Revisit if: the specs are corrected (then drop this entry's for-Ahmad list).

### D-037 (PC-021 + orchestrator, 2026-11-09): entity-metadata rulings
- ResetServerOwned never resets the key, so temporary ids (Id<0) survive preprocessing (0015 §6.3-§6.4). **Spec contradiction for Ahmad:** 0012 §3.1 says "every ServerOwned property", and Id is ServerOwned by derivation.
- EF entity inheritance mapping (TPH/TPT) is unsupported, and the startup gate refuses it. No spec in 0010-0035 relies on it, because extension is plain CLR inheritance plus UseEntity. The ARCHITECTURE.md "Document (TPT root)" bullet is replaced.
- Every entity declares [Table] in a schema other than dbo (0012 §4.1/§2.2/§2.8). **Binding on later checkpoints:** the entities of 0017 (Blob), 0019 (Export/Import), 0020 (Job/Schedule), 0022 (OutgoingEmail) and 0026 (SupportRequest), whose specs state the table only in a comment, must add [Table(..., Schema = "core")] or their spec's schema.
- Revisit if: a spec adds entity inheritance mapping.

### D-038 (PC-060, 2026-11-09): shared component bases
- Ruling: a base class that carries public inputs shared by public components (TmSelectBase) is public and marked @sealed, not ɵ-prefixed. The API-golden filter drops ɵ members, which would hide the inherited input surface. ɵ stays for internal-only bases.
- Known gap: ɵTmGridBase's inherited inputs are missing from the ./grid golden. The next grid checkpoint should switch it to the public @sealed form.
- Also accepted: a readonly select never opens (a pre-existing bug, fixed) and exposes aria-readonly.
- **Misalignment for Ahmad:** 0010 §B.4 names contentChildren(TmOptionGroup), which the design does not need. See also D-036's §B.4 note.

### D-159 (orchestrator, 2026-11-20): PC-088 Review 2 rulings

- PC-088 Review 2 (fable, ed00f3de..8caf7211): 0 blocking, 1 major, 5 minor, all from code reading. R2-1 (major, latent): invite and issue-credentials check only errors after ValidatedAsync, so an unconfirmed warning lets the identity call run, then ConfirmationRequiredException persists nothing and the confirmed resend emails everyone again; fixed by also stopping on unconfirmed warnings and the tenant-state verdict. R2-2: R1-4's keep-on-unknown-commit gains nothing (the secret is never delivered on a 503) and leaves an orphan when the persist did not commit; replaced by a read-back if within budget, else reverted to delete on any fault. R2-3 (a cancel after the identity answer loses the whole write-back) and R2-4 (three races between the round trips, none opening access) are fixed where contained, else recorded as open risks. R2-5 recorded in WATCHLIST 0018. R2-6: the open-transaction probe gets a positive control. Secrets, authority (self-scoped Read is the owner filter rebased through the navigation), failure-path convergence, the probe and the budgets were confirmed. Review 1's fixes hold. PC-088 holds behind PC-145 after fix 2 (D-087).
- Carry to PC-190: tellma_action refuses the sensitive invite and issue-credentials actions (both mcp Full today).
- PC-088 fix 2 (c1574599): ActionContext.ValidatedAsync now returns Task<bool> (false on errors, unconfirmed warnings or a tenant-state refusal); the R1-4 keep-on-unknown-commit change is withdrawn and 0018 §3.8 holds as written; a new error code Users.CredentialsConcurrent (en and ar) answers the losing concurrent issue-credentials; the probe has a positive control; R2-5 is in WATCHLIST 0018. R2-3 is left as documented behaviour (a cancel after the identity answer loses the write-back, and a retry re-emails) pending Ahmad's answer below; it opens no access. Its one local RequestTimeoutTests failure is the pre-PC-F64 test (7/7 on rerun). PC-088 holds behind PC-145.
- For Ahmad (fix 2 additions): (4) ValidatedAsync returning Task<bool> is a public contract change to 0015 §11.2, alongside item (1). (5) 0018's error-code table should gain Users.CredentialsConcurrent. (6) R2-3 needs a choice: the persist ignores cancellation once an action has called out, or 0018 §3.3 states that a cancelled invite re-emails on retry.
- For Ahmad: (1) 0015 §11.2 / deviation 1: the ValidatedAsync contract covers unconfirmed warnings and the tenant-state verdict, not only errors. (2) 0018 §3.3: a request cancelled after the identity server answered loses the whole write-back, and a retry re-emails; a pipeline or spec answer is needed. (3) 0018: service-client deletes are distribution-wide, so a cloned sandbox tenant could delete the live tenant's client (not reachable today).

### D-158 (orchestrator, 2026-11-20): PC-145 Review 1 rulings

- PC-145 Review 1 (Opus high, 1226a7c1): 0 blocking, 0 major, 3 minor. R1-1 a requested cancel can drop the withdrawals of a committed retention-stop page (WithdrawAsync gets the item token); fix with CancellationToken.None. R1-2 the statement-level repeats (0020 §16) are pinned only by text goldens. R1-3 missing tests (nudges, second cancel of a Q-kind row, a Held row in the retention stop, cancellation between retention pages, RCSI off). All 8 deviations accepted. Rulings: JobLabels stays as the seam §16's offline pin tests (no later checkpoint consumes it server-side); Confirm declared on JobService is correct and is seen by the registry, endpoint audit, admin securables and evaluator; the reset deactivating built-ins (Id <> 500) is test-only and covers PC-146's built-in 2; the tree-verify audit suspension is test-only (disjoint tables, separate autocommitted round trips), not a product race. Migrations are dotnet-ef output touching only this checkpoint's seeds. The reviewer ran every CoreFeature unit project in full (D-151), all green. PC-145 holds behind PC-F62 after fix 1 and wires the D-156 retention carry at its integration.
- For Ahmad: (1) 0014 §2 AccessActions should add Confirm or say the job's Confirm is service-declared. (2) 0020 §2.3 should name JobLabels' server-side reader (the reserved MCP tellma_job, or channel rendering), or the §16 label pin should move to the client. (3) 0020 §14.4's stop SQL could bind p0, not p1.

### D-157 (orchestrator, 2026-11-20): PC-088 Review 1 rulings

- PC-088 Review 1 (Opus high, ed00f3de): 1 blocking, 2 major, 4 minor. R1-1 (blocking, reproduced): UserAccessCriteria gives every member Read on their own user row, so core.User, core.RoleMembership and core.UserStamp are Filtered with no grants and AccessEvaluationTests' "every decision Denied" fails; same class as PC-146 R2-1 (D-153). Fixed by listing the self-scoped securables in PC-146's shape; whichever of PC-088, PC-145 and PC-146 lands later merges the lists. R1-2/R1-3 (major): §8.1 budgets for admin save, activate/deactivate and delete are claimed but unasserted; issue-credentials lacks the 0018 §9 recompose test, CreateServiceAccountAsync lacks the no-open-transaction check, and the invite partial-failure path has only a unit test over a hand-built context. All to be added. R1-4: the new client is deleted on any persist fault, including an unknown commit outcome, which can lock a service account out; delete only on a proven non-commit. R1-5/R1-6/R1-7 minor. Secrets checked clean (secret only in CredentialsResult, redacted in its text form; no log carries email, subject, client id or secret). Goldens and migrations touch only this checkpoint's objects; regenerate at integration. R2 on fable follows fix 1 (security-bound).
- For Ahmad: (1) withdrawn by D-159 (R2-2): keeping the new client on an unknown commit gains nothing, since the secret is never delivered. (2) 0018 §3.3/§9 duplicate subject: Unique at the root with property Subject (0015 §14.2); a 2627 rolls back the whole write-back after invitations were queued, so a re-invite emails those users again; a per-row guard would avoid it but is a spec change. (3) Deviation 1: ActionContext.ValidatedAsync is an addition to 0015 §11.2's contract. (4) Deviation 8: 0018 §3.2's rules read as unconditional. (5) Deviation 12: 0018 §9 "InvitedAt set once" contradicts the §3.3 SQL.

### D-156 (orchestrator, 2026-11-20): PC-F62 Review 1 rulings; carries to PC-145 and PC-088

- PC-F62 Review 1 (Opus high, 7a58ce3f): 0 blocking, 0 major, 3 minor. R1-1 the user capture before a cascade reads without locks, so a row inserted between capture and cascade loses its user tag bump (no current model affected). R1-2 three plan refusals (save reaching a user-rule table, SET DEFAULT on a user column, cascade cycle) fire on first use as a 500, not at composition (0012 §3.2). R1-3 multi-hop capture is tested by text prefix only. The derivation, authority (Aggregate only, CallerAuthority unchanged), atomicity and PC-144 pin were confirmed; the user capture fixes a real VersionTagTests failure. PC-F62 holds behind PC-F63 after fix 1 (D-087).
- PC-F62 fix 1 (c3309b0f): R1-1 capture reads WITH (UPDLOCK, HOLDLOCK); R1-2 the SET DEFAULT and save-reach shapes fail core.version-tags at startup, plan-time backstops kept; R1-3 a unit golden through a child node's scope (no executed case: no fixture entity keys a child table, and adding one would need a new fixture migration). Its one local RequestTimeoutTests failure ran the pre-PC-F64 test (pc-2 does not contain 5c27e84d), so it is the PC-F64 root cause, not a new flake. Holds behind PC-F63.
- Widening to saves is accepted (defence only under §4.1); the refusal moves to composition. No seam for raw SQL deletes now: carried to PC-145 (job and file retention DELETE of core.Jobs declares core.ScheduleStates and IJobEntity tables), wired by whichever of PC-145 and PC-F62 lands second. PC-088's local user-delete declaration is redundant and harmless once PC-F62 lands; removal carried to whichever lands second.
- For Ahmad: spec gap, not a departure. 0012 §5.2's WrittenTables row should say Save and Delete also declare every table a referencing key's ON DELETE action writes (siblings, the §4.1 cascade tables, the IJobEntity and Jobs.ScheduleId SET NULL keys), read from the model's foreign keys. 0012 §5.2 (Write authority) and 0015 §13.3 should say the caller-channel check covers the tables the statement's own text writes, and tables written only by a key's action are declared but exempt.

### D-166 (orchestrator, 2026-11-21): platform-core PR runs red since PC-088 on one race; fixed in PC-148's CI fixes, not a separate PC-Fnn

- Four of the pull_request runs on platform-core since 7146ab13 (44869279845, 44869358625, 44870793408, 44871610920) failed on one test only: UserIdentityActionTests.Credentials_on_an_account_deactivated_meanwhile_are_refused_and_the_new_client_deleted. The runs on 1ebd8b99, 5a71ea5d, 787d0814 and daeb4ae4 were green. PC-088's own pc run was green. I missed these at the time: D-154 took PR runs out of the done gate, but a red on platform-core still falls under §8.3.
- Root cause (PC-148 CI fixes 9d3d364f, verified against the four runs' logs): on a failed action, ActionRun faulted Persisted and rethrew without awaiting the flows parked on it. The caller therefore saw the failure before issue-credentials deleted its client (0018 §3.8), possibly after its scope was disposed. ActionRun.RunGeneralAsync now awaits the released flows before raising. A deterministic unit pin fails without the fix.
- §8.3 judgement: the fix was already committed on pc/PC-148, whose integration run is the gate, and PC-147 is held behind it, so no other integration is in flight. A PC-Fnn would duplicate that commit. The line is treated as stopped until PC-148 lands. If PC-148's run goes red on anything else, the fix moves to a PC-Fnn under §8.3. From now on I check platform-core's PR runs after each push.
- For Ahmad: 0015 §11.2 should say that a failed action awaits the flows released from its Persisted barrier before the failure reaches the caller.

### D-165 (Ahmad, 2026-11-21): save the manual usage reset for next week

- Ahmad: "I'll go with your recommendation and wait till next week for the reset, when hopefully more work is unblocked that can run in parallel. If new developments change the calculus at any point, let me know".
- Basis (2026-11-21 05:15): weekly all-models 85% at ≈1.05 pts/h, running out ≈ 2026-11-21 19:30 UTC; a reset applied then would be ≈68% used before Tuesday noon (full use needs ≈1.55 pts/h, the past peak with five heavy agents and a full ready queue). No T1/T2 is ready; 44 T1 and 24 T2 wait on dependencies. The run idles from exhaustion to the 2026-11-24 20:00 rollover; agents in flight resume after it.
- Standing: the orchestrator tells Ahmad if the calculus changes (for example a burst of T1/T2 work becoming ready before exhaustion, or a pace that would use a reset in full before Tuesday noon). A reset is applied only once the current allowance is near 100%, never with points unspent.

### D-164 (orchestrator, 2026-11-20): PC-148 Review 1 rulings; carries to PC-149 and PC-143

- PC-148 Review 1 (Opus high, b01932b6): 0 blocking, 1 major, 4 minor. R1-1 (major): EmailNotificationChannel sends every recipient into one EnqueueAsync, so one address the outbox refuses (e.g. ContactEmail "Ada <a@x>", which MailAddress accepts) or a name with a control character throws ValidationException and fails the whole delivery batch with no retry; fixed by per-recipient validation. The D-151 carry landed (EnlistmentFrame.Admit admits EmailOutbox, with the commit/rollback test). All 9 deviations accepted. PC-148 holds behind PC-088 after fix 1.
- Carries: the 0018 §3.3 welcome email has no owner (PC-088 excludes it, PC-143 defers to 0022, PC-148 does not wire it); assigned to PC-149, whose done-when already has welcome emails leaving through the sender, and which starts after PC-088 lands. PC-143 registers EmailNotificationChannel only once PC-149 is on platform-core, or opted-in email piles up as Pending rows whose jobs fail closed over 12 attempts. PC-149 also emits tellma.outbox.enqueued.
- For Ahmad: under D-132 option (b), outgoing-emails gets delete-by-query back (0022 §8 lists it).

### D-163 (orchestrator, 2026-11-20): PC-147 Review 1 rulings; live SignalR suite deferred until provisioned

- PC-147 Review 1 (Opus high, dac43255): 0 blocking, 0 major, 9 minor. Security confirmed sound: only the front door mints hub claims (each exactly once, a principal already carrying one is refused, a fresh ticket per request); every group is tenant-qualified and families cannot collide; the publisher sends only to the scope's tenant; clients can subscribe to nothing; session.ended reaches no other tenant or session. AddHubOptions is required (ASP.NET Core 10 uses HubOptions<THub> only when registered) and is pinned by every realtime test. The deviation-2 ordering claim is confirmed in Azure SignalR SDK 1.33.1 source. Findings: R1-1 the live suite and offline Azure test sit in the core unit project (ARCHITECTURE.md test layout, 0021 §12), moved to Defaults.Azure.Tests and a new Defaults.Azure.AspNetCore.IntegrationTests; R1-2..R1-9 (weak 404 assertion, untested 4 KB limit, TellAsync try scope, needless catalog reads, digit-led deployment id throws a bare exception, uncounted 401/403, memo refusals untested and unexplained, N-instance fan-out of state changes) fixed or recorded in fix 1.
- Nightly signalr-live: the require step keeps failing like the other live connector jobs (a skip would report green), and gets a job-level `if: vars.SIGNALR_LIVE_ENABLED == 'true'` gate so an unprovisioned suite is off, not red by design. The "Nightly live connector suites" workflow is disabled_manually and scheduled runs come from main, so it runs nowhere until platform-core merges. Deferred (PLAN §6): the live suite has never run, so its Azure-specific assertions are unverified.
- For Ahmad: (1) provisioning the live SignalR suite needs a GitHub environment live-realtime-tests with secret SIGNALR_TEST_CONNECTIONSTRING (a test Azure SignalR Service instance) and the variable SIGNALR_LIVE_ENABLED=true. (2) 0021 §6.2: the session group is t{tid}.x{handle}, not x{handle}. (3) 0021 §6.5: session.ended goes to local connections directly plus GroupExcept to the rest, since only a direct local send is ordered before the abort's close; §6.5 also omits OnRemovedAsync, and (if R1-9 is not fixed) every instance publishes each state change to the whole tenant group. (4) 0021 §6.6/§11: the host. prefix on the realtime-hosting problem code. (5) 0021 §6.6 / 0007: deployment ids may start with a digit or hyphen, which the Azure application name cannot.

- PC-147 fix 1 (e7a8bf75): all nine findings fixed. R1-1 moved the live suite to test/defaults/Tellma.Defaults.Azure.AspNetCore.IntegrationTests and the shared host to test/shared/Tellma.Testing.WebHost. R1-9 sends tenant-state closes and cache.changed only to each instance's own connections, which leaves one open risk: an instance whose registry refresh fails does not tell its own connections until a later refresh. R2 runs on fable. For Ahmad, revised: item (5) is withdrawn, because AddTellma already refuses a slug that does not start with a letter. Item (3) is narrowed: tenant-state rows reach each instance's own connections, not t{tid}, so nothing is delivered N times. New: 0021 §12 should name the moved live-suite project. New: 0021 §10.1 connects{refused} now also counts hub 401 and 403 responses from authentication, authorization and CSRF.

- PC-147 Review 2 (fable, e7a8bf75): 0 blocking, 0 major, 5 minor. Security confirmed after fix 1: no tenant's signal reaches another tenant's connection. The R1-9 open risk is acceptable, because a stale instance serves the API from the same stale snapshot. The R1-6 pushback holds. Findings: R2-1 local sends are serial before the abort; R2-2 the revoked memo refuses a reactivated member for 5 minutes in every mode; R2-3 the revocations-untold alert is masked; R2-4 outage 5xx is counted as refused connects; R2-5 no integration test of the front door against a real connect. For Ahmad: on Azure, the sign-out or revocation memo refuses a pre-minted token on only about 1 connect in N instances, so a connection that slips through lives until its ticket expires. This is the window 0021 §6.6 and 0010 §A.12 accept; closing it would need a membership check at connect, which the spec rules out.

- PC-147 fix 2 (8076570a): R2-1..R2-5 all fixed. R2-5 was not deferred: Acme RealtimeHubTests runs over a real tenant database. For Ahmad: (6) 0021 §6.5: the revoked-subject memo lifetime is now set by mode, 5 min on Azure SignalR Service (a reactivation inside that window is refused until it lapses) and 10 s self-hosted. (7) 0021 §10.1: closes gains a told tag, and the untold-revocation alert reads closes{reason=access-revoked,told=false}, because the spec's events comparison cannot be exact. (8) 0021 states no send bound; session.ended sends now run side by side, each bounded at 2 s. (9) Pre-existing access-stack behaviour outside PC-147, found by the new Acme test: on a warm connect cache, a read 62 s after the cold connect's stamp leaves LastActiveAt unchanged, with or without the hub. Only the cold connect stamps, which contradicts 0014 §7.3's throttled per-request stamp, if that stamp is meant to apply to warm requests. It has no owner; it needs a ruling on whether 0014 §7.3 or the cache is right before a fix is assigned.

### D-162 (orchestrator, 2026-11-20): nightly 44847843416 green; PC-F63 and PC-F64 verified

- Nightly 44847843416 on platform-core cabe29d3 (dispatched after PC-F63 landed): dotnet, dotnet-windows, integration-windows, client-e2e and client-e2e-quarantine all green. This verifies PC-F63 (WebKit chart-bidi on the Linux runner; real Safari stays unverified) and PC-F64 (TenantScopeTests suspension refresh on integration-windows) against nightly 44745573605, and the dotnet job's OS-dependency step passed this time.

### D-161 (Ahmad, 2026-11-20): PC-F63's blocked push approved

- Ahmad: "Approve the push" (answering "Approve PC-F63's fast-forward push of 247691dc to platform-core?"). The orchestrator confirmed origin/platform-core (f0dacf30) is an ancestor of 247691dc and pushed f0dacf30..247691dc, fast-forward, not forced. The approval covers that push only. PC-F63 is done (D-154); PC-F62 released.

### D-160 (orchestrator, 2026-11-20): PC-F63 platform-core push blocked; Ahmad notified

- PC-F63's CI run 44845403533 is green for 247691dc. Its fast-forward `git push origin HEAD:platform-core` was denied by the auto-mode classifier as a destructive or unwanted action, the same block as PC-144 (D-152). The agent stopped and tried no other route; the orchestrator did not push it either (an approval covers only the push it names). Ahmad notified by push and email. PC-F62, PC-145 and PC-088 stay held, and the nightly verifying PC-F63 and PC-F64 waits, until the push lands.

### D-155 (orchestrator, 2026-11-20): PC-F63 and PC-F64 root causes; one permission block notified

- PC-F63 (nightly WebKit chart-bidi): WebKit's GraphicsContext::drawBidiText carries a right-to-left run's leftmost glyph's GPOS y-offset (cursive Arabic finals) onto the baseline of that run and every later run; confirmed in WebKit source and reproduced on Windows WebKit. Fix b4b8f762 closes every Arabic-script word with a zero-width ALM (U+061C) in the chart's canvas text only; Chromium and Firefox pixel-identical. Review 1: 0/0/3. The ALM never leaves the ECharts option (no accessible name or tooltip gets it). Hebrew/R-class needs no closing (no cursive attachment). The ./chart budget is at 6.868 of 6.87 KB; the next addition needs a D-031-style budget ruling. The nightly dotnet job's "OS dependencies: unavailable" was a slow apt mirror, no change. PC-F63 holds behind PC-146.
- For Ahmad: ALM departs from spec 0034 §7.4, §14 and decision 26 and spec 0024 §7.9 ("LRM and RLM only"). The reviewer judges it the right fix and proposes wording in reviews/PC-F63.md.
- PC-F63 hit one permission block: a host-side TCP proxy into its Linux Docker container was denied; it did not retry and served a static showcase build inside the container instead. Ahmad notified (push and email); no action needed for the item.
- PC-F64 integration CI red 44785071160 (Windows build-test, SmtpProtocolTests full-feature MIME after 10 s, unrelated to PC-F64): the SMTP tests' sender connected to "localhost" while the in-process server bound 0.0.0.0 only; Windows tries ::1 first and each refused attempt cost ~2 s, so a loaded runner hit MailKit's 10 s connect budget. Fixed test-side with one explicit 127.0.0.1 (bdf3e354).
- PC-F63 fix 1 (aa763156) closed R1-1..R1-3. R1-2 (a label cut with an ellipsis loses its closing ALM, so WebKit shifts it again) is documented, not fixed: an ALM before the ellipsis fixes WebKit but splits a Latin label from its ellipsis and loses kerning, changing its pixels in all three engines (pixels-agree rule). Accepted as a known limitation. merge-tree with platform-core clean; holds behind PC-146.
- PC-F64 CI-fix run 44798852215 red on ubuntu build-test only: AspNetCore RequestTimeoutTests (a read whose answer began after its timeout answered 504 rather than losing its connection). Second occurrence after PC-146 fix 1 (D-153 flake candidate), so it is root-caused in PC-F64 as CI fixes 2 (D-011, D-113), not re-run.
- PC-F64 CI fixes 2 (5c27e84d, pc run 44823811836): RequestTimeoutTests root cause is test timing, not product. The 300 ms timer starts before the access guard; on a cold, loaded host it fired there, and with nothing written 504 is the correct answer (0016 §8.3). A warm-up alone failed 3 of 10. Four tests now fire the real timeout inside the endpoint; 10 idle runs green, a mutation check still fails the test, 845/845 under load. Ruling: the tests reach the framework's internal timeout source by reflection (no public seam) and fail loudly if it is renamed; accepted as test-only, since adding a product seam solely for tests would widen the product surface. Revisit if an ASP.NET Core upgrade breaks it.

### D-154 (Ahmad, 2026-11-20): a green pc push run makes an item done; PR #18's run no longer gates it

- Ahmad: "Don't gate done on the `pull_request` run if the push run is green. Everything else in the process stays the same."
- Context: since D-144 each integrated commit gets two runs on PR #18's checks list, CI (push) on pc/{PC} (the gate) and CI (pull_request) once the same sha is PR #18's head. While main does not move they test the same tree.
- From now on an item is done when its green pc/{PC} commit is on platform-core. The orchestrator no longer watches the PR run to mark done. A red PR run on platform-core, seen in the §5.3 run list, still stops the line (§8.3). PROCESS.md §4 diagram, §5.3 and §11 updated (D-088).
- Applied first to PC-144 (711641ab; pc 44771340622 green; its PR run 44774613660 had also finished green).

### D-153 (orchestrator, 2026-11-20): PC-146 Review 2 rulings; PC-F64 Review 1 rulings

- PC-146 Review 2 (inherit xhigh, 5efa9448..261870b7): 1 blocking, 0 major, 2 minor. R2-1: the new NotificationAccessCriteria makes core.Notification × Read Filtered for a caller with no grants, failing AccessEvaluationTests' "every decision Denied" assertion deterministically; the product is right (0021 §8), so the test is rewritten to list the self-scoped securables, which PC-145's core.Job self-scope then extends. R1-1 is confirmed closed in all four unit projects that compose CoreFeature. The AspNetCore RequestTimeoutTests failure in fix 1 is an unrelated load flake (839/839 on rerun); recorded as a flake candidate. PC-146 holds after fix 2 behind PC-F64 (D-087).
- For Ahmad: 0021 §4.3 says preference-save errors sit at `[i].type`, but the 0016 §7.3 edge (PC-081, pinned in ValidationPathsTests) renders `body[i].type`. The code follows 0016; 0021 §4.3 should say `body[i].type` for a JSON-array body.
- PC-F64 Review 1 (6952a9d3): 0 blocking, 0 major, 1 minor (an ARCHITECTURE.md sentence overstated the five-minute bound). Root cause confirmed from the nightly log: the test failed in setup (CatalogHost.MigrateAsync on EF's 30 s default) during a two-minute LocalDB stall; the product's suspension refresh is correct; the longer bound is test-helper only. No stall lever now; moving LocalDB data off C: and the design-time-factory test's 30 s default are on WATCHLIST 0011 for a recurrence. The implementer's identity-store "inconsistency" is not real (the migrator hands the store a connection string with Command Timeout = MigrationCommandTimeout, already pinned). PC-F64 integrates after fix 1.

### D-152 (Ahmad, 2026-11-20): PC-144 platform-core push approved after a permission block

- PC-144's agent had its fast-forward `git push origin HEAD:platform-core` of the green 711641ab (pc run 44771340622) denied by the auto-mode permission classifier; it did not retry. Ahmad was notified (push and email) and approved the push in chat. The orchestrator verified HEAD = 711641ab and that origin/platform-core was its ancestor, then pushed 3bbbac07..711641ab from pc-3. The approval covers that push only.

### D-151 (orchestrator, 2026-11-20): nightly 44745573605 after PC-F61; PC-146 escalated; touched tests include shared compositions

- Nightly 44745573605 on 007f0ff0 (dispatched to verify PC-F61): the OS dependencies installed for chromium, firefox and webkit in client-e2e and client-e2e-quarantine, so PC-F61 is verified. New reds, each a fix item (D-144, not a line stop): client-e2e, 3 WebKit chart-bidi canvas tests (397 passed) → PC-F63 in pc-f; integration-windows, TenantScopeTests.A_suspension_another_instance_wrote_refuses_the_next_scope_after_a_refresh after 1 min 5 s (775 passed; the job passed in 44701549452) → PC-F64 in pc-f2. The nightly dotnet job ended "OS dependencies: unavailable" after two attempts; PC-F63 checks whether that is a mirror stall or a missed caller.
- PC-146 (0021-c) Review 1 at 5efa9448: 1 blocking, 1 major, 3 minor. R1-1: InboxService at the "inbox" route collides with two existing test models, failing about 79 unit tests in Tellma.Core.Tests and Tellma.Core.AspNetCore.Tests that the implementer's filtered run missed (pc run 44743945020 was skipped, as designed for a non-integration commit). Escalated to Review 2 (inherit, xhigh, §4.4).
- Reading of D-144's "touched tests": a change to a shared composition (CoreFeature, route segments, stack or service registrations) touches every unit project that composes it, so that project runs in full once (still with the unit filter, never an unfiltered dotnet test Tellma.slnx). Added to the brief additions.
- Carry to PC-148: EnlistmentFrame.Admit must admit EmailOutbox (0015 §13.3 admits it in every frame; not yet implemented), and 0021 §12's "enlisted rows committed with the completion" e2e test lands there. The job frame stays as built.
- For Ahmad: 0021 §4.4 says any row-writing channel enlists with host, but 0015 §13.3's participant list does not admit channel senders. Either 0015 admits registered senders in the core.notification-delivery frame, or 0021 restricts row-writing channels to writers admitted in every frame. Interim: no change; a refused enlistment throws before the group is recorded, so nothing half-commits and the item fails visibly at MaxAttempts. Caveat: an API-sending channel registered before a refused one re-sends on every attempt.

### D-150 (orchestrator, 2026-11-20): PC-144 Review 1 rulings; PC-F62 queued for undeclared cascade writes

- PC-144 (0020-e) Review 1 at 7d470e81: 0 blocking, 2 major, 4 minor; no escalation. R1-1: DeletePlan.Writes declares only registered model siblings, so the tables written by FK ON DELETE CASCADE / SET NULL from other tables go undeclared, in every module (a Schedule delete writes core.Jobs through FK_Jobs_ScheduleId SET NULL). R1-2: most validator codes untested, including ReplayAllRequiresAllow (§16 pin).
- Ruling: PC-144 does not change shared delete-plan code. Its fix 1 declares the core.Jobs write beside the existing core.ScheduleStates DELETE as an interim and pins it with a test. The general fix is a new fix item, PC-F62 (0015/0012): DeletePlan declares the tables of every Cascade/SetNull referencing FK, exempt from authority as siblings are, and the per-service workarounds are removed. PC-F62 starts after PC-144 lands, so it removes the workaround in one place.
- The implementer's "double fire on an ambiguous step-2 commit" open risk is wrong per the reviewer (job insert and state advance are atomic; the fence plus UPDLOCK/READPAST block a re-fire), so it is not the D-146 gap. The pause event rename (0020 §9 SchedulePaused → SchedulerSchedulePaused) must be reverted or listed as a deviation.
- PC-144 holds after fix 1 behind PC-087 (D-087).

### D-149 (orchestrator, 2026-11-20): PC-B04 results; Tier 3 still waits

- PC-B04 (Phase 4, PC-062…PC-084): 8 findings (0 blocking, 6 major, 2 minor), all fixed in 27f8bb6a; parts pushed back (Amharic keys, the ProblemMapper backstop per D-108, blob pre-validation, a client build check). The fixer self-watched pc run 44730508322 (green) and fast-forwarded platform-core to f8806aa8 (D-147). Its local run had one load flake that passed on a local rerun; the workflow did not name the test, so it is unidentified. CI was not re-run.
- For Ahmad (from PC-B04): (a) 0016 §8.3: a timed-out request that sent a persist, or whose body writer began, is aborted and never answered 504 (even a refused persist ends as outcome unknown); (b) 0011 §5.4: the client version cap is now 128 visible ASCII characters, owned by DeploymentVersions and checked at composition; (c) 0013 §6.3 step 1 needs the rule that a recompose re-decides required grants; (d) 0017 §4.4 says an action persist has empty Captures, contradicting §4.1 and 0015 §11.2; the code follows §4.1.
- With pc-2 free, check_deps lists only Tier 3 (PC-174, PC-181). Weekly usage is 67% with 4 d 7 h left, so the window will not expire unused and PLAN §4 keeps Tier 3 waiting (as D-140). pc-2 idles until a Tier 1 or 2 item is ready.

### D-148 (orchestrator, 2026-11-20): PC-087 Review 1 rulings, and PC-144/PC-146 launched as Tier 2

- PC-087 (0018-b) Review 1 at e2ed8550: 0 blocking, 0 major, 6 minor. The reviewer accepted all six deviations the implementer flagged: DeleteContext<TEntity>.WithDescendants (needed because 0015 §10.3 reruns the §10.1 hook round with the same context), DeploymentIdentity.IsDevelopment (built from the same IHostEnvironment.EnvironmentName), Center served without DeleteByQuery (required by D-132's registry refusal), the refined HasChildren and type-change rules, the hidden-parent non-leak, and the regenerated fixture migration (dotnet-ef has-pending-model-changes clean, gl objects only). Held after fix 1 behind PC-F61 (D-087).
- For Ahmad: (a) spec 0015's DeleteContext contract should list WithDescendants; (b) 0018 §5.5's centers route list depends on the D-132 choice: under option (b) centers get delete-by-query back; (c) both 0018 §5.4 rows (HasChildren, type change) need rewording, since the literal rules refuse valid saves (WATCHLIST 0018 lead). Fix 1 (0d6e317a) adds two more: the first 0018 §5.4 row should judge a stored parent only when the row is new or moves, and its select should drop SubtreeCount. It also found that a hidden parent stays hidden only when every gl.Center grant carries the row filter: a Save grant implies reading what it reaches (0014 §5.2 step 2), so with Read filtered and Save unfiltered the parent is readable and ParentMustBeGrouping is the right answer.
- After PC-100 landed no Tier 1 checkpoint was ready (check_deps: PC-144, PC-146 at T2; PC-174, PC-181 at T3), so PC-144 (0020-e) went to pc-3 and PC-146 (0021-c) to pc-4 from f4e6120b, both R1 only. T3 stays queued (D-140).

### D-147 (orchestrator, 2026-11-20): batch-review fixers integrate under D-144 by watching their own pc CI run

- A workflow agent cannot be resumed by the orchestrator, so the §13 batch fixer pushes its integration commit to pc/PC-Bnn, watches that run itself, and follows §4.3's green or red message until the commit is on platform-core. PROCESS.md §13's fixer prompt says so. The orchestrator still watches PR #18's run on the result.
- PC-B04 (Phase 4, PC-062…PC-084) launched in pc-2 at 36827fe1 once PC-082 went green (pc 44703261531, PR 44705432684). Weekly usage 59% with 4 d 10 h left; Fable 23%, so finders run on Fable.

### D-146 (orchestrator, 2026-11-20): PC-100 Review 2 rulings

- Deviation D-15 rejected: MaxParallelBatches must hold, including at boot when every poller plans at once; polls get stagger/jitter, and a test pins that N pollers starting together never exceed the bound.
- R2-6, interim: renewing a lease the instance already holds uses the tenant's last-known registry entry when the snapshot is older than MaxStaleness; every other path keeps the stale-snapshot refusal. Otherwise a catalog outage longer than 5 min lapses every lease and runs jobs twice (0020 §6.7). **For Ahmad:** 0011 (registry staleness) and 0020 (renewal) should state the same rule. PC-100 pushed back on the interim (accepted): the stale-snapshot refusal sits at three registry reads on the renewal path (TenantScopeFactory.CreateScopeAsync, TenantConnectionProvider.Tenant, TenantConnectionFactory.ConnectionString), so a renewal-only last-known path needs a scope flag both connection seams honour, a change to 0011's registry and connection contracts (shape in reviews/PC-100.md, R2-6). The gap stays open: a catalog outage longer than MaxStaleness still lapses leases. It becomes a fix item once Ahmad rules. Also from fix2: R2-1 releases a claim that finds no instance slot uncounted (JobClaimReleased); under steady saturation a woken tenant may release claims beyond the one freed slot. New deviation D-16 adds poll stagger and jitter.

### D-145 (orchestrator, 2026-11-20): PC-082 Review 1 rulings

- R1-1, interim: spec 0016's order is kept (the rate limiter runs before tenant membership is known), plus a per-user (sub) cross-tenant cap that bounds one account's total rate and partition memory. **For Ahmad:** any signed-in user of the distribution can still drain another tenant's 64 concurrent tellma-tenant permits, so its members get 429. That also works as a load oracle: a busy real tenant refuses a stranger's concurrent requests, a missing one never does. The alternative is to take the tellma-tenant and tellma-user permits inside the access guard after membership is confirmed (a 0016 §8.2 / 0011 §5.1 order change). Same family as D-137's state-before-membership question. PC-082 fix1 adds ApiOptions.RequestsPerMinutePerUserAcrossTenants (1,200, validated >= RequestsPerMinutePerUser) and an implied tellma-user-across-tenants policy, additions to 0016 §8.2 for Ahmad to confirm. The rate refusal logs ApiRequestRejected(reason=rate_limited) with the policy and lane in a log scope (SYSLIB1025 forbids a second method under one event name).
- R1-5: the tellma-web timeout on the Blobs group aborts any blob download still streaming at 30 s. Carried to PC-089 (DisableRequestTimeout or a time-to-first-byte policy on the blob GET). **For Ahmad:** 0016 §8.3 should say which timeout applies to blob downloads.

### D-144 (Ahmad, 2026-11-20): integration gates move to GitHub CI; integration-windows moves to the nightly tier

- Situation: a local integration gate took about 45-100 min (PC-142 ~50, PC-098 ~55, PC-102 ~100) on a host shared by 4-6 slots, against about 17 min for CI, and the D-087 release queue serialised that cost.
- Ruling: this replaces D-105's local integration gate and closes D-105's open item "CI on pc/** branches". D-105's other open items (docs:build to nightly, profiling slow .NET tests, pipelined or batched integrations) stay open.
  - ci.yml runs on push to main and `pc/**`, but on a pc/** push every job (all-checks-passed included) skips unless the head commit message contains ": integrate". Pull-request runs (PR #18) and pushes to main are unaffected. Implementation and fix-round commits never carry the marker, so their pushes start only a run that concludes `skipped` (every job skipped), which never counts as green. Integration merges use `--no-ff`, so a fast-forward cannot drop the marker (PC-F60 R1). CI on the pc/{PC} integration commit is the gate. The concurrency group becomes ci-<event>-<ref>-<sha> (PC-F60, accepted): the same sha pushed to two refs no longer shares a group, so one branch can never cancel another's run. PR #18's run on every platform-core push is the re-confirmation, and a red there still stops the line (PROCESS.md §8.3).
  - Implementation and fix rounds: build the touched projects in the inner loop and the whole solution once at the end (0 warnings, "Build succeeded"), run `dotnet format --verify-no-changes`, and run only the unit and integration tests the change touches. No full unit filter, no full integration suites, no full client e2e, and never an unfiltered `dotnet test Tellma.slnx` (PLAN.md §7.1 items 2-4).
  - Integration: fetch, merge origin/platform-core with `-m "{PC}: integrate (merge platform-core)"` (regenerate lockfiles, goldens, snapshots and migrations, never hand-merge), or, if platform-core is already an ancestor, `git commit --allow-empty -m "{PC}: integrate"`, so every integration ends on a marked commit. Build and format locally, push only pc/{PC}, and return status=ci-pending with `git rev-parse HEAD`. The orchestrator watches that run and resumes the agent.
    - Green: the agent confirms HEAD still equals the green commit and pushes `HEAD:platform-core` (fast-forward only). If rejected, it fetches; plans-only commits in between are re-merged and pushed without a new CI wait; anything else is re-merged, pushed to pc/{PC} and waits for CI again.
    - Red: the agent fixes it within the same integration step from `gh run view <id> --log-failed`, reproducing locally as needed, and commits the fix as `{PC}: integrate (CI fixes)` so its push gets a run (orchestrator's reading of the marker rule). A plans-only re-merge after a rejected platform-core push stays `{PC}: merge platform-core` and starts no pc run. A red pc run does not stop the line, and this fixing is not a fourth implementer round.
    - A CI run is never re-run to turn it green; flakes are root-caused (D-011, D-113).
  - The D-087 one-at-a-time release queue stays; each release now waits on about 17 min of CI instead of an hour of local gate.
  - Reviewers still build and run the tests they need to check a claim, and may cite the pc branch's CI run.
  - integration-windows moves unchanged from ci.yml to nightly-tier.yml (LocalDB, Core and Acme, filter "Category=Integration&Category!=Nightly&Live!=true"), guarded to platform-core like the other nightly jobs. A red nightly is a PC-Fnn fix item, root-caused (D-011, D-113), and does not stop the line. The TenantScope LocalDB case (PC-F59, D-135) is now covered nightly.
  - The Windows CI job otherwise stays: unit tests still run on both OSes in build-test. Consequence: the Identity, EF Core and Queryex integration suites no longer run on Windows before a merge (CI's Windows integration job only ever ran Core and Acme, and those two now run on Windows nightly only).
- Rollout: the workflow changes go through fix item PC-F60 (pc-f): one R1, then integrate with CI on both sides (its pc/PC-F60 push, then PR #18's run). Items in flight that have not reached integration (PC-082, PC-100) integrate under the new flow; from the snapshot that lands PC-F60 onward every integration uses it.
- Evidence (filled in as it lands): the pc/** trigger fired on PC-F60's first, ungated push (run 44693894569); gated non-integrate pushes concluded skipped (runs 44694269933, 44694363809); the first ": integrate" run 44697079750 on pc/PC-F60 (a96be78b, green, full tier without integration-windows) confirms the pc/** trigger; PR #18 re-confirmed it in run 44699551746. PC-F60 landed at a96be78b; nightly tier started by hand after PC-F60 with `gh workflow run nightly-tier.yml --ref platform-core`: run 44701549452: integration-windows ran there and passed, as did dotnet and dotnet-windows; client-e2e and client-e2e-quarantine failed on the known Playwright OS-dependency install (also red in the previous nightly), now fix item PC-F61. This snapshot is the switch-over: every integration from here uses the new flow.

### D-143 — PC-082 hand-offs (2026-11-20)

- Interim internal McpLimitOptions binds Tellma:Mcp (ToolCallsPerMinutePerUser 120, ToolTimeout 60 s) for the tellma-mcp policy until Tellma.Core.Mcp owns McpOptions; that checkpoint absorbs it.
- ARCHITECTURE.md (framework line) now lists the rate limiter, request timeouts, response compression, output caching and Microsoft.AspNetCore.OpenApi (Development document).
- Pre-existing: MapTellma reads IOptions<ApiOptions>.Value eagerly while mapping, so an invalid Tellma:Api surfaces as an OptionsValidationException from MapTellma, not the startup gate. Left as is; a candidate for a later cleanup.
- New flake candidate: Core JobRunLockingTests claim seek failed once under load in PC-082's gate and passed alone. Same treatment as D-139: if it recurs it becomes a PC-Fnn item.

### D-142 — PC-100 Review 1 rulings (2026-11-20)

- Escalated to Review 2 (inherit, xhigh) under §4.4: R1 had 2 blocking findings (back-to-back polling when a refused tenant is woken by any release; one item's cancel abandoning the whole lease and double-running the rest).
- Deviation D-5 (wake tenants refused a slot) stays only in R1-1's narrowed form: a release must not make a refused tenant poll again at once while the refusing key is still at its MaxConcurrency.
- Deviation D-12 accepted (DrainAsync bounded by the host token): the token fires only at ShutdownTimeout, so no work is lost or doubled; a long lease is just not shortened.

### D-141 — PC-100 hand-offs (2026-11-20)

- Composed hosts run the job worker unless Tellma:Jobs:Enabled=false; the shared unit-test compositions (Core.Tests TestComposition, AspNetCore TellmaTestHost) set it off because their fake tenants have no databases. Later hosts and test compositions must decide this explicitly.
- Rule for later checkpoints: hosted job services must not read IOptions.Value in their constructors (the host builds them before the startup gate runs); JobWorker, JobPoll and JobConcurrency read options lazily.
- JobDispatcher.DrainAsync is also bounded by StoppingAsync's host token, so the final renewal is skipped once that token is cancelled. Pending R1.

### D-140 — PC-174 (T3) not started early (2026-11-20)

- After PC-142 went green, the only ready checkpoint was PC-174 (Tier 3). PLAN §4 lets a lower tier start only when no higher tier is ready, a slot would idle, *and* the window would otherwise expire unused. The weekly window was 54% used with 4 d 19 h left (about 31% elapsed), so it will not expire unused; PC-174 waits for Tier 1/2 work to run out or the pacing to change.

### D-139 — New flake candidate from PC-098's gate (2026-11-19)

- BlobLifecycleStatementTests.The_sweeps_claim_blocks_on_a_row_a_save_is_confirming_and_skips_it_once_the_save_commits (from PC-084) failed once in PC-098's local integration gate: "No request waited for the lock within 30 seconds" on a loaded host; the class passed 2/2 in two reruns alone, and PC-098 does not touch it. Under D-011/D-113 it is not quarantined: if it recurs (locally or on CI) it becomes a PC-Fnn fix item and is root-caused. Briefs list it as a known candidate meanwhile.

### D-138 — PC-102 fix1 hand-offs (2026-11-19)

- R1-1 fixed: IScopeConnection gains IsPrologueDue (past the fast-deny window, permissions aged, or an activity stamp due). Discover then runs the guarded runner's statement-less Read prologue, so a deactivated caller gets tenant-not-found; otherwise it costs 0 round trips. Supersedes D-136's second bullet.
- **For Ahmad:** 0029 §9 "Messages" omits Permissions.FilterTooLong and Permissions.FilterInvalid, which the permission profile answers.

### D-137 — PC-081 Review 2 notes outside its diff (2026-11-19)

- The telemetry stage wraps the Web group only; 0016 §5.1 puts the blob GET on the same chain. Carried to PC-089.
- **For Ahmad:** TenantAccessGuard.EnsureStateServes runs before the membership check (0011's order), so any signed-in user learns a tenant exists when it is suspended, or read-only/frozen on a mutation. That is a tenant-existence oracle for authenticated non-members. Keep 0011's order, or check membership first so non-members always get tenant-not-found?
- A non-JSON body to a route that declares its media types is diverted to the SPA fallback before CSRF sees it: it answers 415 unsupported-media, not 415 csrf-rejected, and tellma.auth.csrf_rejections does not count it. No handler runs, so nothing is exposed; recorded only.

### D-136 — PC-102 hand-offs (2026-11-19)

- QueryWireDiagnostics (Tellma.Core/Crud/Query) is the one QueryexDiagnostic → QueryDiagnostic edge mapping. Whichever of PC-081 / PC-102 integrates second converges PC-081's InvalidQueryException mapping on it (named in that item's integration message).
- Discovery runs without the guarded runner: it reads IScopeConnection.CurrentAsync (0 round trips warm, 1 cold) with no fast-deny re-check. Pending R1.
- QueryexType serializes without the Qx prefix platform-wide through one TellmaJson converter shared by row sets and contract fingerprints. PermissionFilterRules is extracted from RoleAccessRules so role save and discovery share one judge.

### D-135 — PC-F59 root cause and an open classification gap (2026-11-19)

- Root cause of the TenantScopeTests red (run 44571660410): over LocalDB named pipes, about 0.5-0.9% of refused logins to a missing database arrive as SQL 233 alone instead of [4060, 18456]. Production classification is correct: a failed open is unsent; 233 is Ambiguous, which reports before the send, and 4060 is reported-transient, so both get the same re-run budget. The fix is test-only.
- Open gap, not seen locally or on CI: an error list ordered [18456, 4060] would classify as Final, because the classifier keys on the primary error. Carried to the next checkpoint that touches the SQL error classifier (0012 §6.3); no action until then.

### D-134 — PC-142 fix2 hand-offs (2026-11-19)

- The D-132 gate is in: StackRegistry refuses at startup a stack that keeps DeleteByQuery while its service or a validator judges deletes (HookOverrides.ValidatesDeletes). No platform stack breaks; User and Role already omit DeleteByQuery.
- Read D-131's §16.2 note together with D-132: Ahmad's choice there (a gate vs a judged delete by query) also decides whether delete rules apply to delete by query. **For Ahmad:** whichever option is chosen, 0015 §16.2's delete-by-query row and 0015's capability-table TopLevelEntity default need wording that matches it.

### D-133 — PC-081 hand-offs (2026-11-19)

- The SPA `{*path}` fallback swallowed 405/415 as a 404 route-not-found. PC-081 changed SpaHosting (outside 0016's own files) so RouteMisses in the fallback answers 405 with Allow, or 415. Accepted, pending R1/R2.
- 0016 §10.2 (an infra/monitoring alert on `tellma.api.problems{status=500}`) is not in PC-081's sections and no checkpoint owns it yet. **For Ahmad:** place it (suggested: the observability/infra checkpoint that owns alert rules).
- Tellma-Priority parsing stays with PC-082; the 429 Retry-After must come from PC-082's limiter OnRejected. Carried to PC-082.
- Acme NeedsUserConnectionAndBootstrap tests stay skipped until the real user connection and admin bootstrap exist. ARCHITECTURE.md needed no change.

### D-132 Delete-by-query bypasses delete validators: fail closed (2026-11-19)

- Situation: PC-142 R2-1. DeleteRun never runs ValidateDeleteAsync on a delete by query (spec 0015 §10.2: one Persist round trip), and every TopLevelEntity stack serves DeleteByQuery by default. Only security tables are gated (D-119). So a stack whose deletes are judged can have its delete rules bypassed by query; e.g. spec 0020's ScheduleService refuses deleting built-in schedules, yet DeleteByQueryAsync("Id = <built-in>") would delete one.
- Interim ruling (fail closed, as D-119): the stack registry refuses at startup a stack that validates deletes and still serves StackOperations.DeleteByQuery. Delete by query stays one round trip for stacks that do not judge deletes. Implemented in PC-142's last round, with a unit test and a conformance-base assertion.
- For Ahmad: choose between (a) this gate, or (b) a judged delete by query running a validation round (2 round trips, changing spec 0015 §10.2 and §16.2). This supersedes D-131's "wording alignment" note on §16.2's delete-by-query row.

### D-131 PC-142 R1: escalation, spec item and carries (2026-11-19)

- R1 found 4 majors (missing §16.2 budget rows, collections at depth, closers without actions/validators, untouched suites not run), so PC-142 gets a second review (PLAN §8.1), Opus inherit at high: it is not on the two-review list, so no fable.
- For Ahmad: spec 0015 §16.1's tellma.crud.validation.rounds row should list the open frame's hosts (api_action, job, provisioning) as crud.operation values, as the enlistments row already does. Interim: the collector admits operations ∪ hosts.
- For Ahmad: spec 0015 §16.2's delete-by-query row says a judged delete takes 2 round trips, but §10.2 and DeleteRun say a delete by query is always 1 (it never runs delete validators); the base asserts 1. Align §16.2 to §10.2's wording.
- Carries: the conformance base's Excel rows lane goes to PC-174, and export/import reachability with their budget rows to PC-176/PC-177.

### D-130 PC-086 R1 rulings (2026-11-19)

- R1-3: the client entity model emits each blob kind's MaxSize, not a cap from the emitting machine's Tellma:Blobs:MaxUploadSize (a deployment setting must not leak into a public, immutable chunk); recorded as a deviation.
- Fix1 outcome: an enum's emitted name now comes from its home schema (the ordinally smallest schema among sites declared in the enum's own assembly, else among all sites), replacing the first-site rule (R1-1). R1-5 pushed back: the metadata builder refuses a parent key with no navigation, pinned by a test.
- Accepted deviations: one emitted name per C# enum type (<schema>.<Type>), with SettingKeyDescriptor.EnumType qualified (spec 0032 §7.3 already uses the qualified form); [EntityRef] action-field label keys drop the Id suffix (spec 0029 §2.4); the .d.ts golden is checked in as .txt because API Extractor crashes on the @tellma/core augmentation.

### D-129 PC-098 R1-4 deferred (2026-11-19)

- For Ahmad: PC-098 deviation D-3 reads spec 0020 literally: Retry→Failed at MaxAttempts also applies to the Retry(0) of a guard failure and of a stop after progress. The guard fails whenever the tenant-wide permissions tag moves, so an administrator's role save during a job's last attempt (e.g. a third-attempt core.import) ends it Failed/AttemptsExhausted; likewise a host stop after progress. Kept as specified for now; the alternative is not counting guard failures and stops toward MaxAttempts.

### D-128 PC-098 hand-offs (2026-11-19)

- For Ahmad: spec 0020 §3.4 maps every OperationCanceledException to Released/Retry(0), which would release a handler that times out on its own forever without counting an attempt. PC-098 treats only a worker-issued token's cancellation as a stop; any other OCE retries with Jobs.Internal (deviation). Once the host is stopping, any exception ends the invocation as its stop (D-056).
- D-121/D-122 carries closed in PC-098: the worker hands the frame a DataBatch; OpenWriteFrame.AppendAsync restores the completion batch's Limits; the frame's count/reservation release waits for EndCompletion(failure); CompletionBroken makes the worker send a fresh completion.
- For Ahmad: spec 0020 §3.3 says IJobProgress.Report makes JobItem.State current at once; the Abstractions docs said the opposite and were corrected to the spec. Spec 0020 §9 gains JobScopeUnavailable and JobNoticeRecipientsFailed log events.
- job.changed after a claim stays with PC-100; after a progress-writing renewal it is PC-098's. JobEnqueueCodes still lack resx entries (earlier carry); new Jobs.* codes are English and Arabic only, as Amharic is partial repo-wide.

### D-127 PC-080 R2 rulings (2026-11-19)

- R2-3: the raw-bytes answer for actions returning (byte[] Json, string WireTag) applies only to actions declared in a platform (Tellma.Core*) assembly; an audit row fails startup when any other action returns that shape. The shape stays, so PC-079's SettingsService.ClientAsync is unchanged.
- R2-2: WireBinding.SettledAsync cannot re-apply cultures from an async finally. The problem writer applying cultures itself is carried to PC-081.
- R2-1 (major): the JSON contract gate must neither pass members that throw only on write (Type, delegates, IntPtr, multi-dimensional arrays) nor fail on [JsonIgnore] members.
- PC-080 holds after fix2 to serialize with PC-079's integration; it is released at once if nothing is integrating.
- Outcome: "platform" means an assembly named Tellma.Core or Tellma.Core.* (so Tellma.Core.AspNetCore.Tests qualifies). A name test, not a trust boundary; distributions own their assembly names. WireBinding.SettledAsync was removed; endpoints call services directly.

### D-126 PC-080 fix1 outcomes (2026-11-19)

- R1-1: AccessWitnessFilter.Wrap now runs on every Web, Api and Blobs endpoint, RequestDelegate ones included. An unwitnessed 2xx that has started is aborted in OnStarting; one that has not is answered 500. PC-089's carry is updated.
- The D-125 raw-bytes hand-off is closed in PC-080: ActionAnswers.Of writes an action declared as (byte[] Json, string WireTag) as its Json bytes, application/json, and the tag travels in the version-tags header. SettingsService.ClientAsync needs no edge work.
- Agents share the scratchpad across slots; briefs now ask for checkpoint-prefixed log names.

### D-125 PC-079 hand-offs (2026-11-19)

- PC-080 (in R1) must write SettingsService.ClientAsync's (byte[] Json, string WireTag) result as raw bytes at the edge, not serialize the tuple. Sent to PC-080 with its review-1 fix round.
- A language change bumps every entity:* tag along with permissions (closes the D-076 carry).
- A missing expected entry in a settings save is a ConcurrencyException conflict (IsMissing, Id 0), never NotFound.
- Settings MCP exposure: R1-4 found spec 0016 names only details, save and refresh-caches as never exposed, and client returns a byte[]/tag tuple that the invoker would answer as {}. Ruling: client is Hidden too, entity-tags stays Full. For Ahmad: add client to spec 0016's never-exposed list.

### D-124 PC-080 hand-offs and spec notes (2026-11-19)

- For Ahmad: spec 0016 §3.1's JsonIgnore gate fails only Editable/WriteOnce properties; Derived and DatabaseOwned pass beside ServerOwned. Align the wording ("that a save writes").
- For Ahmad: spec 0016 §4.6. SignalR MapHub stamps DisableRequestTimeout on the hub connection endpoint, so Hub-group hub endpoints are exempt from that rule; the spec should say so.
- The witness filter covers the Web, Api and Blobs groups. A guarded endpoint that neither evaluates access nor records the member witness turns its successes into 500. PC-083/PC-084 blob endpoints are already on platform-core: R1 checks them, and PC-080's integration gate must cover them. PC-089's blob endpoints must comply (carry).
- Carry to PC-082: WithRequestTimeout("tellma-long") metadata is inert until PC-082 registers the tellma-web/tellma-long policies; register them before installing the timeout middleware.
- Carry to PC-081: BadRequestException→400 and LimitExceededException→413 are mapped early in TellmaProblemExceptionHandler; fold them into the full mapping.

### D-123 (orchestrator, 2026-11-18): PC-084 round-trip budget and carries; for Ahmad

- Spec 0017 §10.1 ("a save adds none") and the checkpoint's done-criterion conflict with §4.2's mandated blob load. A warm-buffer, insert-only save that attaches a blob needs one extra round trip, and the code cannot avoid it. Interim: accepted as a PC-084 deviation. On a cold buffer the load folds into round 1 (R1-1), so no other save shape pays. **For Ahmad:** amend §10.1 or the budget.
- Spec 0015 §6.6: blob columns are exempt from the uniqueness rule that their UX_ index had triggered. R1 judged the exemption narrow and correct.
- R2 (fable, 0/1/3): R2-1 is fixed fail closed. A blob download whose owner's Read is sensitive runs AssuranceCheck, per D-108 and D-111. **For Ahmad:** spec 0017 §5.1 names only EvaluateAsync.
- R2-2 corrects the carry below. The hook belongs in PC-077's SaveRun.Enlisted.cs `Translate`, where the group's roots are in scope. A second carry: call `BlobAttachments.Prefetch` from PC-077's `EnlistAsync`. PC-084 integrates after PC-077, so both are PC-084's.
- Fix 2 (575849a4), both **for Ahmad**:
  - Deviation 12: a download through an owner whose Read is sensitive asks for step-up even when nothing is found. Spec 0017 §5.1 needs the step-up.
  - Deviation 11: a stale-denial download takes 2 round trips, against §5.1/§10.1's one.
- The cold-buffer prefetch now sits inside ReserveIds, which PC-077's EnlistAsync already calls.
- Carry: persist blob refusals raised by enlisted groups are counted by whichever of PC-077 and PC-084 integrates second (R1-6). The hook: in SaveRun.Persist.cs, where `Enlistments?.Translate(failure)` returns a translated exception, call `BlobAttachments.CountPersistRefusal(translated, groupRoots, _operation.Services.GetService<BlobAttachMetrics>())` before the throw, and add one enlisted-save test.

### D-122 (orchestrator, 2026-11-18): PC-077 R2 rulings; for Ahmad

- R2-1 (major, fixed in fix 2): the open write frame re-decides the [ApiAction] securable for the fresh ConnectedUser on every compose, as the other pipeline hosts do. A revoked grant must not commit enlisted writes on a permissions re-entry.
- R2-2 (interim, fail closed): on a re-entry caused by a permissions change, a frame with enlisted groups does not commit on validators decided under stale grants. It either re-runs the groups' validators in memory or raises StaleContextException. **For Ahmad:** spec 0015 §5.1 should say which; PC-078's UserAccessRules and RoleAccessRules are grant-dependent target rules.
- **For spec 0020:** PC-078's AccessGuards casts the batch to DataBatch, and the job seam runs PersistGuards on the worker's completion batch, so the worker must hand the frame a DataBatch.
- Release order: PC-078 integrates first. PC-077 holds after fix 2 (D-087), then integrates under D-121.

### D-121 (orchestrator, 2026-11-18): PC-077 and PC-078 meet at the access guards; the later to integrate reconciles

- PC-077 contributes the access guards once per host, over the union of its own tables and every enlisted group's tables (nested groups included). The lock comes first, and the invariants run after the last group and OnPersist.
- PC-078 places the guards on a stack's own tables. Its fix 1 may move the decision to batch.WrittenTables ∪ StackTables (D-119 R1-2).
- Ruling: whichever of the two integrates second makes the union reach every guard path, including PC-077's OpenWriteFrame.Persist.cs. It also runs both checkpoints' guard and enlistment tests together in its integration gate.
- PC-078 fix 1 (1a5de53e): the guards are now decided over StackTables ∪ batch.WrittenTables at the invariants step, and the lock stays a TransactionHead statement. PC-077's group union should adopt the same rule. **For Ahmad:** spec 0014 §8.3 decides over WrittenTables, while spec 0015 §6.7 step 4 calls ContributeLock "ahead of item 1" from metadata. One of the two needs aligning.
- PC-078 fix 1 outcomes for D-119: users' and roles' delete-by-query is refused as a BadRequest naming the resource (spec 0018 §4 and spec 0015 §16.2 say otherwise). A deactivated existing administrator fails the bootstrap naming the user id; R1 preferred reactivating through the activate action. **Both are for Ahmad.**
- PC-077 fix 1 (b5cf1ae7): PC-078's guard rule hooks into Crud/Save/PersistGuards.Contribute. That is called at all four persist sites, OpenWriteFrame.ComposeOntoAsync included, and whoever integrates second keeps those calls.
- **For Ahmad (spec 0015 §5.1):** how re-run enlistments are answered. PC-077 matches them by task and step from the first run and treats divergence as stale (deviation 11). Also whether EnlistmentOutcome.Stamp should become nullable; today it falls back to the frame's clock (deviation 12).
- Outcome (PC-077 integration 23730416/392703f6):
  - One guard rule everywhere: StackTables ∪ batch.WrittenTables, decided at the invariants step. Save, delete and action pass StackTables. OpenWriteFrame passes none and relies on each group's save or delete declaring its target's tables in WrittenTables. GroupTables and ComposedTables are removed.
  - With real stack-table ownership wired, PC-078's two AdministratorGuardTests for another stack's raw security write now hit the append refusal first. Each test asserts that refusal, then asserts the guards' Access.LastAdministrator refusal in an instance using NoStackTableOwners. Accepted: both layers stay pinned.
- Also from PC-077 R1, for spec 0020: the job-frame seam overwrites the worker batch's Limits.

### D-120 (orchestrator, 2026-11-18): BlobContainerStep moves from PC-089 to PC-083; the provisioning-record row goes to the later of PC-078 and PC-083

- Ruling: PC-083 built spec 0017 §6.4 BlobContainerStep (`core.blob-container`, order 20). D-096 had given it to PC-089. R1 accepted the build, and it is kept as PC-083 deviation 8. PC-089 no longer owns §6.4.
- Carry: D-096's provisioning-record row (Acme MigratorTests.The_provisioning_record_holds_the_platform_steps_with_their_versions) needs `core.bootstrap-administrator` (PC-078), `core.settings` (PC-049, done) and `core.blob-container` (now PC-083). Whichever of PC-078 and PC-083 integrates last must see it run green. The carry is on PC-078's status row and in PC-083's integration message.

### D-119 (orchestrator, 2026-11-18): PC-078 implementer's open questions; R1 to judge, for Ahmad

- Q1: delete-by-query bypasses ValidateDeleteAsync, so the user and role delete rules (self-deletion, the system user, OnlyNewUsersDeletable, HasMembers, the administrator role) rest only on L1-L3 and the foreign keys. Should users and roles refuse delete-by-query, or validate it?
- Q2: a deployed bootstrap leaves the administrator New (no subject, no invite), so L1 is unmet and non-System security writes are refused until the invite. This is by design; to be confirmed.
- Q3: the bootstrap does not reactivate an existing inactive user. The spec is silent.
- Q4: users' and roles' saves are sensitive, so every fixture that writes them supplies aal2 step-up.
- Q5: PC-078 places the guards on the stack's own tables only. The union over enlisted groups is PC-077's, being built in parallel; PC-077 was told.
- R1 (0/1/5) and interim rulings for fix 1:
  - Q1: users' and roles' delete-by-query is refused with a closed-set error before any round trip (R1-1; spec 0015 §16.2's budget row). Today it skips CannotDeleteSelf and OnlyNewUsersDeletable for a user that no audit foreign key references.
  - Q3: the bootstrap never silently restores privileges. An inactive existing administrator fails the step with an actionable error (R1-6). **For Ahmad:** reactivate or fail.
  - Q5 / R1-2: the guard decision covers batch.WrittenTables ∪ StackTables. It lands in PC-078 if WrittenTables exists on platform-core; otherwise it carries to PC-077.
  - R1-4: duplicate-permission collapse (spec 0014 §3.7) is owned by no checkpoint. PC-078 does it if contained; otherwise it carries to a later checkpoint.
  - **For Ahmad:** spec 0014 §3.4's "DELETE half" deletes the wrong keys for delete actions. PC-078's fix is right, and the spec wording needs amending.
- R2 (fable, 0/2/3) and interim rulings for fix 2. All three D-119 rulings held under R2.
  - R2-1: the bootstrap step is no longer recorded as done when it bootstrapped nobody. A later run that names an administrator then performs it, including `provision --admin-email` on an Active tenant. **For Ahmad:** spec 0014 §8.3's recovery path depends on spec 0011's provisioning record. The two specs need one owner for that path.
  - R2-4: fail closed. Changing the email of a New user that has memberships needs the same rights as those memberships, or is refused. **For Ahmad:** spec 0014 needs a line on this escalation.
  - R2-5: DeleteByQuery is removed from the users' and roles' stack operations.
- Fix 2 outcomes (4cfcd532), each **for Ahmad**:
  - The bootstrap step runs only when the run names an administrator. A run that names none neither runs nor records it, and logs a warning. `provision --tenant N --admin-email` on an Active or ReadOnly tenant runs that step alone. Spec 0011 §6.1-§6.2 ("already provisioned") and spec 0014 §8.3's recovery path need aligning.
  - Changing a New user's email tests every membership that has not ended as granted (Users.MembershipEscalation). Spec 0014 §8.4 names only added, moved or widened memberships.
  - There is a new public `StackOperations.DeleteByQuery = 128`, part of All and requiring Delete. It is not in the enum of spec 0015 §2 or spec 0016 §4. User and Role omit it, and the startup gate refuses it over a security table. Spec 0018 §4 still projects delete-by-query on both stacks.
  - Spec 0015 §6.7 step 4's "ahead of item 1" should read "a transaction-head statement".
- A second block on PC-078: `powershell -NoProfile -Command "Get-Process testhost,dotnet,sqlservr ..."` was refused by the classifier. It was not needed, and Ahmad was notified.
- Also: PC-078's implementer was blocked running `powershell.exe Get-CimInstance Win32_Process` (a check for lingering testhost processes). It did not retry, and the work completed. Ahmad was notified under §9.

### D-118 (orchestrator, 2026-11-18): PC-076 ActionContext.Persisted needs a custom awaitable; for Ahmad

- A §4.4 verification of PC-076 confirmed a residual race. An action flow reads `Persisted`, awaits something else (say a slow HTTP call), then composes a write or an `Errors.Add`. RT2 may already have committed without it.
- Before the verification fixes, this was a silent write loss for void methods, `ValidateActionAsync`, components and child flows: success was answered. A value method raised a 500 after the commit.
- `Persisted` is a plain `Task`. Awaiting one goes through the non-virtual TaskAwaiter, so nothing distinguishes "suspended on Persisted" from any other await. No fix within the current contract can prevent the loss.
- Interim: the verification fixes make the loss loud. Every late refusal is recorded on the run and checked before RT2 is sent. After a commit it is raised for void and value actions alike, under a distinct Error event, and the residual is documented in Persisted's remarks.
- **For Ahmad:** change spec 0015 §7.1 so that `Persisted` is a custom awaitable (or a method such as `await context.PersistAsync()`). RT2 can then wait until the flow actually awaits it. This fully closes the race.
- Outcome (b665e864): late refusals from any flow are recorded on the run and checked before every send. After a commit they are logged as ActionComposedAfterPersisted and raised for void and value actions alike. Released hook and component flows are awaited after the commit. One case stays silent: a child flow that nobody awaits and that composes after the response has gone out (PC-076 deviation 9).
- Also for Ahmad: spec 0012 has no long-id stamp list (stamped deletes of long-keyed stacks are refused with a 400). The `@tb` table names in spec 0018's invite should read as illustrative, since the deferred batch names its tables under `@tm_rec`.
- Related, still open: D-117 Q5, how a committed action whose result is lost is answered (a closed-set code, or an idempotency rule).

### D-117 (orchestrator, 2026-11-18): PC-076 implementer's open questions; R1 to judge, for Ahmad

Kept as built until Reviewer 1 judges them; spec 0015 gives no clear answer to any of these.

1. Any `ValidateActionAsync` override, in a service or a component, sends both built-in actions (activate and deactivate) to the general path. There is no per-action rule.
2. A deferred action batch refuses appends that answer a batch-local name or a result at once (Save/Delete/Tvp/DeclareIdTable/DeclareKeyTable/FromCache). Methods write stack rows through `context.Save`/`Update`.
3. Persisted gating uses an AsyncLocal gate. Reading `Persisted` releases the reader, so a later load is refused.
4. Stamped deletes of long-keyed stacks stay refused by the data layer.
5. After a proven commit:
   - A void method's failure is logged, and the ids answer it (the D-116 principle).
   - A value-returning method has nothing to answer with, so its failure is raised. That may invite a duplicating retry.
6. The Read-Denied → NotFound branch is reachable on cacheable stacks. Read takes no filter there, so a filtered write grant implies no read.

R1 judgements (0/3/4) and interim rulings for fix 1:

- Q1 and Q6: accepted. Q6's "only cacheable stacks" is wrong, because D-116 R2-5's criterion-only grants reach the branch too.
- Q2: rejected for Tvp/DeclareIdTable/DeclareKeyTable (R1-2). Spec 0018's invite needs `SqlOptions.UserIds` to name a declared table, and spec 0014 §3.4's preference bag depends on the same. Fix 1 should solve it inside PC-076; if it cannot, this escalates as a cross-spec conflict.
- Q3: the AsyncLocal gate is fine, but releasing the flow when it reads `Persisted` can hang the request or lose writes (R1-1). To be fixed.
- Q4: accepted as interim, but the refusal must come before any round trip, as a closed-set error. The BigIdStampList gap is in spec 0012.
- Q5: raising is kept for now, though it invites a retry. **For Ahmad:** a closed-set "committed, result lost" code, or a rule that such actions must be idempotent. A void method's failure after a normal commit (R1-4) is answered with ids, consistent with D-116.

### D-116 (orchestrator, 2026-11-18): PC-074 R2 open questions; interim rulings, for Ahmad

- R2-2: a re-entered persist keeps round trip 1's row-level verdicts (before images under Read, the Save-grant pre-check, reference visibility), even if the stack's Read or Save filter narrowed in between. So an edit that moves another user's row into a just-narrowed Save filter can commit. Spec 0015 §5.1 is silent on this for a save's pre-check. Interim: kept as built and noted as a deviation. Ahmad to decide whether a changed filter should raise StaleContextException.
- R2-5: a caller whose Save comes only from a bespoke criterion with `Action = Save` has Read denied, so the read-back refuses (fails closed). Interim: failing closed stands, and deviation 12's wording is corrected. Ahmad to decide whether the read-back needs a Read decision at all.
- R2-3 (fix 2): a committed save must not be answered in a way that invites a duplicating retry. Outcome (1cdc5f20): when the read after a proven commit fails, the save answers with the ids alone and Entities empty, even under ReturnEntities. That departs from spec 0015 §6.9 (PC-074 deviation 20); a 503 was rejected because a caller would retry it and insert again. R2-2 stays deferred as deviation 19. Fix 2 also made `ForeignKeyViolationException` (Abstractions) carry a public `Conflict` (MissingTarget / StillReferenced / Unknown), read from the 547 message.

### D-115 (orchestrator, 2026-11-18): PC-074 R1-7, a spec gap for Ahmad

- Spec 0015 §14.2 has no row for a 547 raised by child synchronisation's deletes, when another table still references the deleted child row. Today it passes through as a 500 when the referencing table is outside the stack, and as `Fk.NotFound` when it is inside. Interim: if §14.2 already maps a delete's 547 to an in-use code, PC-074 reuses it; otherwise PC-074 keeps the current behaviour as a listed deviation. Ahmad to decide the row (likely an in-use code naming the child row). Outcome of fix 1 (f206fbc2): mapped to the existing delete-row code `Fk.InUse` (entity/property, at the dropping collection or the root) and pinned by a unit test. A payload row of the same entity that writes the referencing column stays `Fk.NotFound` (PC-074 deviation 18). Ahmad to confirm and amend §14.2.

### D-114 (orchestrator, 2026-11-18): PC-074 hand-offs

- PC-074 keeps `NoStackTableOwners` as the `IStackTableOwners` default and nothing produces write frames yet, so a ReadOnly tenant refuses every save and the foreign-stack refusal in OnPersist is pinned by unit tests only. PC-077 (write frames and enlisted writes) owns the producer; its brief must carry this so it wires the real owners and adds the integration test.
- `Assignments.Concurrent` is a local constant because `ValidationCodes` in Abstractions lacks it. Kept for PC-074; moving it into Abstractions is a follow-up for the next checkpoint that touches `ValidationCodes`, or a fix item if R1 calls it out.

### D-113 (orchestrator, 2026-11-17): PC-072 R2 open questions; interim rulings, for Ahmad
- Sensitive-Read stacks: a self-reference asks for (R, Read) step-up through the reference checks (D-111), but the before images, ModifiedByName and the write-once "differs" read of the same stack R do not; only the Save pair is asked, through RequireAsync. Reviewer 2 found this is not a bypass under spec 0014 §4.3. Interim ruling: keep it as built (the Save pair's step-up covers reading the rows being saved). Ahmad to choose one rule.
- Spec 0015 §6.6 step 5 says uniqueness holders are skipped "on create and update alike", which is stricter than deviation 18 (a value is freed only where the persist's statement order frees it). Interim ruling: deviation 18 stands, because the literal rule lets a save pass validation and then fail on the unique index. Ahmad to amend the spec or overrule.
- PC-072 fix 2 (R2-2) changed deviation 1: a validator's Visible load of an entity type that no permission guards (no grant can reach it) is now unrestricted instead of denied, matching the reference rule (unguarded targets are checked for existence only). Noted for Ahmad with the rest of D-113.

### D-112 (PC-B03, 2026-11-17): default step-up class aal2 with tier ranking; accepted by Ahmad
- The PC-B03 fixer (XC-2/XC-3) added one shared tier ranking: aal1 < aal2/phr < aal3/phrh. AssuranceCheck accepts a session at or above the bar, falls back to exact match outside the scheme, StrongerAcr ranks lists by their strongest member, and a startup validator refuses an unranked step-up class when the identity server runs in-proc. The default step-up class is now urn:tellma:acr:aal2.
- Ahmad (2026-11-17) accepted the departure: aal2 as the default step-up class with tier-ranked comparison stands. Spec 0011 §5.5 and its appsettings sample are to be brought in line with the code.
- This departs from spec 0011 §5.5 and its appsettings sample (D-066 item 7). Spec 0011 §7.5 (reconcile `@readAt` predicate) and spec 0012 §7.3-7.4 plus spec 0011's listener contract (id buffers keyed by database location) also need text edits. For Ahmad.

### D-111 (orchestrator, 2026-11-17): PC-072 R1-3 reference-visibility reads follow D-108
- Ruling: the save's reference-visibility pre-load is a read decision like a traversal, so per D-108 it must not skip the step-up (AssuranceCheck) or the FastDenyWindow re-check that a direct Read of the same sensitive pair gets. A reference to a child-entity row is checked under the owning root's Read filter, as spec 0015 §7.4 says ("under the target's Read filter"), not for existence only. If the implementer shows the specs intend otherwise, it pushes back with the citation and the question goes to Ahmad.
- PC-072's other spec-wording items for Ahmad: spec 0015 §6.6 step 5's signature for the D-095 ByValues seam.
- PC-B02 spec-wording items for Ahmad: spec 0012 §6.3's two-step arrow for 50501, and the schema-behind row missing from spec 0015 §14.2.

### D-110 (orchestrator, 2026-11-17): PC-B02 and PC-B03 overdue; launch at the 20:00 UTC reset
- Phases 2 (PC-015…PC-031) and 3 (PC-032…PC-061) are both fully `done`, but neither batch review was started when its last member closed. This was an orchestrator miss: §5.2 says to start as soon as the phase is done. Ahmad's question surfaced it.
- At 18:55 UTC, U = 91 (R = 9) and F ≈ 1 (the PC-070 integration), so the §6.3 batch gate (R − F − 8 ≥ 5) fails. The weekly window resets at 20:00 UTC.
- Ahmad (18:58 UTC) overrode the gate: start both reviews now in parallel; fixing and integration may wait for the reset. The finders were launched at b346ad7c in one workflow (pc-1 for PC-B02, pc-2 for PC-B03), and their findings are written to each slot's reviews/PC-Bnn.md. Each fixer is spawned after the reset with the §13 fixer brief.
- Ahmad then paused the workflow (TaskStop). It resumes from run wf_08f0b9d9-329 after the reset, and finders that already completed return from the cache.
- Original ruling, superseded: launch PC-B02 and PC-B03 as soon as the reset is detected, each in its own free slot. Finders run on `fable` (Fable meter 5%). Until then, only the §13 preparation runs (slots and member commit lists) as filler.
- From now on, check §5.2 phase completeness after every `done`, beside check_deps.

### D-109 (PC-070, 2026-11-17): D-062 child-target loading confirmed; cached shared instances to R1; Gadgets = Lookup
- Spec: 0015 §5.3, §5.5; 0012 EntityQuery.
- D-062's provisional ruling is final: children's and collections' related entities load through a captured FK set, one grouped Rows per expand level, inside the details round trip. Spec 0012 stays unchanged. Spec 0015 §5.3 should name this mechanism (for Ahmad).
- GetAllCached with no extras returns the cache's shared instances. Reviewer 1 judges whether that is safe for the entity types and callers, and proposes copies, a read-only contract or freezing if it is not. The fix round follows that verdict.
- Spec 0015's cacheable "Gadgets" example corresponds to the test fixture's Lookup entity. The spec text should be aligned (for Ahmad).
- PC-F56 (from the PC-068 CI integration-windows flake) adds `--logger "console;verbosity=normal"` and xUnit diagnostic messages to the CI .NET test steps, because the red log showed only the exception type. That item's integration therefore runs both gate sides (§11).

### D-108 (orchestrator, 2026-11-17): PC-068 query path: spec alignments for Ahmad, and the culture carry

- The query steps run compile, then the traversal rule, then the Geography select refusal, as spec 0015 §5.2 orders them. Spec 0016 §5.6 puts the traversal rule before the compile, and should be aligned with 0015. The outcomes differ only for a query that is both invalid and restricted.
- A ceiling (QX5) diagnostic raised in the platform's own text is relocated to the Filter/Select/OrderBy clause at span (0,0). Spec 0016 §3.5's closed clause list should name that location.
- Carry, binding on the edge, MCP and job call sites: after every pipeline call, re-apply RequestContext.ApplyCultures. An async callee cannot change its caller's AsyncLocal cultures, so a re-apply inside the pipeline does not reach the caller. This extends PC-050's carry on GuardedBatchRunner.
- PC-068 R1-5: DataAccessRetryExhausted maps to 503 with Retry-After (spec 0015 §14.2) in the pipeline itself, not at the edge.
- PC-068 R1-6: a traversal decision must not bypass step-up or the FastDenyWindow re-check that a direct Read of the same sensitive pair gets. It is fixed in PC-068, unless the implementer shows the specs intend otherwise, in which case the question goes to Ahmad.

### D-107 (orchestrator, 2026-11-17): the tm-chart engine chunk ceiling is 200 KB gz; spec 0024 §12's 170 KB line goes to Ahmad

- Situation: PC-194's lazy engine chunk measures 199.74 KB gz. ECharts 6.1.0 with exactly the modules spec 0034 §7.3 lists is about 194 KB on its own: PC-194's implementer measured 194.38 and its R1 measured 193.83. The facade adapter adds 5.32 KB. Within the module API, what remains in the chunk cannot be removed: the HTML tooltip that TooltipView imports statically, the zh locale that echarts/core registers, and the time scale. The only optional cut is LegendPlainComponent (-1.8 KB), which would lose the scroll legend.
- Ruling (D-031 arithmetic): the chunk ceiling is 200 KB gz, recorded in the size-gate budget notes. The entry point ./chart stays at 6.6 KB, under spec 0034's 9 KB.
- For Ahmad: spec 0024 §12's 170 KB line is unreachable with ECharts, so either the spec takes 200 KB, or the ECharts choice is revisited. Spec 0034 §7.5 should also say that zrender's innerHTML writes are routed to replaceChildren() under Trusted Types.
- PC-194 R1 reported 4 majors, so the item escalates to Review 2 (inherit, xhigh) per PROCESS.md §4.4.

### D-106 (orchestrator, 2026-11-16): user-run job scopes have no assurance, so sensitive pairs refuse them; for Ahmad with the jobs work

- Situation: in PC-052, RequireAsync refuses a sensitive pair with StepUpRequired when the scope's Assurance is below spec 0011's bar. A job scope run for a user carries no Assurance, so every sensitive pair is refused in jobs. Spec 0014 §4.3/§5.1 say this applies to every caller, request or not, and spec 0015 §9.1 names "a job" explicitly, so the code follows the spec.
- For Ahmad: should such a job run as the system, carry the requester's assurance captured when it was enqueued, or stay refused? This carries to the job-runner checkpoints of spec 0020.
- Also from PC-052 (accepted as deviation 14, consistent with D-073): AccessHolders.Grants lists the all-roles decision's grants, not every matching row as spec 0014 §5.1's sketch says. A filtered wildcard-action row on an action with no filter root grants nothing there and is omitted. Spec 0014 §5.1 should be aligned.

### D-105 (Ahmad, 2026-11-16): the integration gate runs on the item's scope; slow suites are left to CI unless touched

- Situation: integrations run one at a time and took 60-95 min each (PC-151 65, PC-F50 92, PC-193 96) on a host loaded by 4-6 slots, against 17 min for CI. The release briefs asked for every suite, beyond PLAN.md §7.1 item 4's touched-only rule. PC-193 (client only) spent about an hour on .NET suites. The last three red CIs (PC-F42 via PC-F51, the VersionTag flake, PC-F53) were Linux-only, and each had passed the full local gate on Windows.
- Ruling (Ahmad picked recommendations 1 and 2): integration runs the PROCESS.md §11 integration gate. Its scope is the item's own change set plus the files its merge resolution touched.
  - Client scope runs the client gates and only the e2e specs that cover the touched code.
  - Server scope runs build, format, the unit filter and the touched integration suites.
  - Shared triggers run every integration suite.
  - CI still runs the whole PR tier on every push, and a red CI still stops the line. The implementation and fix rounds are unchanged.
- Not adopted: moving integration-windows and docs:build to nightly, profiling slow .NET tests, pipelined or batched integrations, and CI on pc/** branches. These stay open for Ahmad.

### D-104 (orchestrator, 2026-11-16): PC-193's merge with PC-F50 and PC-151, accepted as gated

- Budget: the core-ui '.' ceiling goes from 9.18 to 9.21 KB. It measured 9.17 before the merge with platform-core and 9.20 after, so the only growth is the D-098 seam's own +0.03 KB on top of what platform-core already carried. ./pivot stays at 14.1 (it measures 14.00), and ./tiles-tree measures 2.34 against its unchanged 2.37.
- The D-102 carry renames tiles.loadFailed to tilesTree.loadFailed. A registered string table cannot shadow a core top-level key, and 'tiles' stays in the core table. The string now lives in TM_TILES_TREE_STRINGS_EN, which ./tiles-tree exports and TmTilesTree registers. The Arabic string moved with it and is covered by the EN-AR parity test.
- Under PC-151's folder gate, pivot is classified with the older folders, not the additions, because its own pivot.spec.ts runs the axe scans. Ahmad may rule that it joins the additions battery instead.

### D-103 (orchestrator, 2026-11-16): PC-F53 takes the segmented-label CSS fix; the forced-colors HighlightText sweep is PC-F54

- Situation: PC-F53 R1-1 found the checked segment's label invisible under forced colors. The segment keeps forced-color-adjust auto, so Chromium paints a Canvas backplate behind the label, and HighlightText equals Canvas. The test that claimed the label is legible passed anyway.
- Ruling: the CSS fix and a stronger label check go into PC-F53, because the defect is in the code PC-F53's test covers and the fix is small. The reviewer saw the same pattern (color: HighlightText without forced-color-adjust none) in the checkbox, date-popup, entity-picker, select/multi-select and tree stylesheets. That sweep is a separate fix item, PC-F54 (pc-f). It does not gate the line, and it touches none of PC-F53's files.

### D-102 (orchestrator, 2026-11-16): ./tiles re-split under D-031 lands at 9.24 KB, with a tiles-tree entry point; the 7 KB cap goes to Ahmad

- Ruling: PC-F50 moves the tree inputs (parentId, hasChildren, expandedIds, loadChildren) off TmTiles onto a TmTilesTree directive (selector `tm-tiles[parentId]`) in a new `@tellma/core-ui/tiles-tree` entry point. ./tiles drops from 10,576 B to 9,359 B (ceiling 10.35 → 9.24 KB); ./tiles-tree is 2,323 B (ceiling 2.37 KB). A static-graph exclude and an eslint ban keep the tree, grid-engine and spinner out of ./tiles. The run proceeds on 9.24 KB.
- Why not 7 KB: the class core, template, about 1.3 KB of styles, the skeleton and the synchronous pointer path are needed at first paint. Everything lazy-able totals 877 B before loader cost, and a lazy-keyboard prototype netted 81 B (22 B with idle preload). R1 confirmed the numbers and found no further honest split; D-092's independent measurement (8.89 KB with tree, skeleton and flash removed) agrees.
- Carries: PC-131 and PC-132 (spec 0030 tiles views) import TmTilesTree only where trees of tiles are shown, behind `@defer` or a tree-only component, or a tree page pays 11,682 B and the saving is lost. `tiles.loadFailed` is now read only by ./tiles-tree; whichever of PC-193 (the `ɵtmRegisterUiStrings` seam, D-098) and PC-F50 integrates second moves it into ./tiles-tree's own registration with its Arabic string and the parity test.
- For Ahmad: (1) spec 0023 §1 still says 7 KB for ./tiles; rule the cap (proposed 9.24 KB). (2) Spec 0023 §1 needs a tiles-tree row (2.37 KB), and the tiles row drops spinner and grid-engine; §8.5's four tree inputs now live on TmTilesTree.

### D-101 (orchestrator, 2026-11-16): PC-094 and PC-151 spec misalignments for Ahmad

- Spec 0011 §2.2 says the deployment version comes from the entry assembly. PC-094's DeploymentVersionsReader reads the composition method's assembly first, because under test hosts and tools the entry assembly is not the distribution. Spec 0024 §3.1 and 0011 §2.2 should agree on that order.
- Spec 0011 §1.2 has the SPA proxy port resolved at runtime. PC-094 computes it in MSBuild from .dev-ports.local, because the SPA proxy reads it before the host runs.
- PC-094's interim client stand-ins (main.ts with bootstrapApplication, local tmEnglish/tmArabic, the angular.json style list, the minimal server AcmeFeature) carry to PC-103, PC-104 and PC-092 (LEDGER Status notes).
- PC-151 exempts the date-range and date-time pickers (Alt+ArrowDown) from spec 0023 §12's keydown-to-paint gate: only aria-expanded flips in the key's frame, because spec 0005 §16 loads the calendar popup through @defer (on immediate). Painting in the key's frame would need the popup chunk un-deferred, which changes spec 0005's performance rule and the date pickers' budget. The exemption is tracked; Ahmad rules if he wants the paint. (Corrected 2026-11-16 after PC-151 R1-4: the @defer is from spec 0005 §16, not spec 0010, and date-time is affected too.)

### D-100 (orchestrator, 2026-11-15): PC-F46 goes to review with the nightly tiles probe unproven on a loaded host

- Situation: PC-F46 cut the 10,000-tile scroll cost from 35-67 to 1-7 tasks over 50 ms per 80 wheel steps, interleaved on the same host (median task 26-31 ms, was 32-56). The nightly @perf probe, which asserts zero, still sees 2-26 long tasks on this host. The host is loaded by five concurrent slots, so the number is noise-bound. The implementer reported status=blocked for that reason. Every PR-tier gate passes.
- Ruling: the item goes to Review 1 as usual. The probe is nightly-tier (PLAN.md §6), so it does not gate a PR-tier checkpoint. Its first verdict on a quiet host is the platform-core nightly after integration, recorded under Flaky as a watch. If it fails there, a follow-up item chooses between a spec 0023 §12 bound Ahmad would rule on (a count or percentile) and re-binding tm-image in place (a tiles budget raise under D-031). The ARCHITECTURE.md tiles sentence and the spec 0005 §7.2/§7.4 deviations (shared object URLs; the on-screen blob stands in under an unknown stamp) are reviewed with the item.

### D-099 (orchestrator, 2026-11-15): PC-217 holds with three spec 0010 §A.3 gaps for Ahmad

PC-217 (client ID metadata documents) holds at a94fb9e4 after two reviews. Spec 0010 §A.3 should be brought in line with the code in three places (docs/ is not edited in the run):
- (a) The option list lacks MaxConcurrentFetches (default 16, with separate slots for the refresh and probe budgets) and MaxInFlightPerHost (2 per host within each budget). Both exist so that published documents cannot starve real clients.
- (b) The per-host limit is split into a refresh budget and a probe budget, so one instance can fetch up to twice the spec's per-host rate. The refresh budget is only for clients whose row holds an authorization; otherwise an attacker's own ids would buy protected slots after one probe.
- (c) §A.3 allows http://[::1] loopback redirects for native clients, but Chromium ignores an IPv6 literal in CSP form-action, so the consent and resumed sign-in pages cannot let such a redirect through. The code emits no source for an IPv6-literal host (pinned by an E2E test). Native clients must use 127.0.0.1 or localhost for browser consent.
Other choices, recorded in the review file: rows carry a fetched-at stamp and an older copy never overwrites a newer row; the metadata cache is LRU and evicts unauthorized entries first; an expired copy is not served on a purely local refusal.

### D-098 (orchestrator, 2026-11-15): entry points register their own UI strings; no "." raise for strings only a lazy entry point reads

- Situation: PC-193 (tm-pivot) raised "." from 9.14 to 9.33 KB (measured 9.23) to hold English strings that only the lazily loaded ./pivot reads. It would be the seventh raise on ".", past D-031's limit of three before a re-split. The cause is structural: English comes from the single TM_UI_STRINGS_EN table in ".", and an entry point has no way to register its own strings. ./pivot has about 1.1 KB of room under its spec ceiling.
- Ruling: PC-193 adds a small string-registration seam to "." so an entry point can contribute its own default strings, typed and overridable through the same localisation path as the core table. The pivot strings move into ./pivot. "." may rise only by the seam's own bytes, under D-031's conditions (budgetNotes entry, reviewer checks the measurement). The 9.33 raise is withdrawn. Later entry points with strings that only they read follow the same seam. This is the re-split D-031 asks for, applied to strings, which are what has driven the "." raises.
- Revisit if: "." rises again for anything other than strings every entry point reads.

### D-097 (2026-11-15): PC-064 is released over the cap, alongside PC-066's integration

Ahmad asked for PC-064's re-merge to start now, over the D-028 cap of five heavy agents. He also widened the allowlist for `git push -q origin HEAD:platform-core`. That breaks two rules for this one item: the cap, and one release at a time. Both releases merge and gate independently, and whichever pushes second gets a non-fast-forward rejection. That item fetches, merges again, re-runs the gates the new merge touched (the full gate if any C# project or the client changed), and pushes again. Nothing is forced. The CI watch, ledger snapshot and next release follow each landed push as usual.

### D-096 (2026-11-15): spec 0017 §6.4 BlobContainerStep goes to PC-089; PC-141's skipped rows carry to their seam checkpoints

- Ruling: spec 0017 §6.4 (the `core.blob-container` provisioning step, BlobContainerStep) is in no checkpoint's Sections: PC-017 covers §6.1-§6.2 and PC-089 §6.3. PC-089 takes it, since it builds the per-tenant containers and EnsureTenant the step drives. The carry is on PC-089's status row.
- Carries: PC-141 left six Acme §10 rows on conditional skips (PlatformSeams, keyed on each seam's presence), so each runs once its seam lands. The checkpoint that lands each seam must see it run green: PC-078 (first request binds UserId, Frozen read, ReadOnly read stamps activity), PC-090 (AllowWhenReadOnly action), PC-100 (RunsWhenReadOnly job), and PC-089 for the provisioning-record row (it needs core.bootstrap-administrator from PC-078, core.settings from PC-049 and core.blob-container from PC-089, and PC-089 lands last of the three).
- Why: a skipped row with no owner never runs. The conditional skip makes the row run by itself; the carry makes its owner responsible for seeing it pass.

### D-095 (2026-11-15): PC-066 holds with two spec gaps for Ahmad; re-homing moves to PC-077

- Ruling: PC-066 (0015-c) is not on the two-review list. Review 1 found 0/1/7, all fixed, and fix 1 rewrote under a tenth of commit 1 (about 450 of 5,900 lines), so no Review 2. It holds at a1abbefc; merge-tree is clean.
- Scope: the §7.3 error re-homing view is deferred to PC-077, whose deliverables list an "error re-homing view" (PC-066 deviation 15). The carry is on PC-077's status row.
- For Ahmad (1): spec 0015 §6.6 step 5 says to use ByKey on a child, scoped to the parent key in C#. ByKey's `IReadOnlyDictionary<TK,T>` holds one row per value, so it refuses a property that is unique only within its parent, and step 5 fails for every child `[Unique]`. PC-066 deviation 6 leaves a Later seam for the write pipeline: an internal `ByValues<T,TK>(property, values)` returning `IReadOnlyDictionary<TK, IReadOnlyList<T>>` through the same key-set restriction and comparer, scoped by parent in C#. Step 5's signature needs amending.
- For Ahmad (2): spec 0015 §7.5 names no log event for a consent given outside every write frame. PC-066 logs it as `ConfirmationAcceptedOutsideFrame`, because the log generator refuses two methods with one event name (SYSLIB1025).
- Why: both are spec contradictions with a working code answer, so nothing waits. The write pipeline needs the ByValues seam before it validates child uniqueness.

### D-094 (2026-11-15): ./grid is at its cap; PC-F26 fits by savings, and a re-split is queued

- Ruling: PC-F26's forced-colors rules put ./grid at 42.05 KB against its 42.03 ceiling once merged over PC-F24 (platform-core alone measures 42.02). No raise: D-057(2) and D-067 allow none without real savings or a re-split. PC-F26 gives the overlay ink a forced-colors default on the scroller and drops the per-rule Highlight fallbacks, keeping every review-1 fix; if that does not fit it stops unpushed. Fix item PC-F44 then re-splits ./grid (or takes measured savings) to restore real headroom, and PC-F42 (more grid forced-colors CSS) waits for it.
- Outcome: PC-F26 measured every savings variant with the gate's own measurer (gzip makes savings tiny and non-additive): the best variant that keeps behaviour is 43049 bytes against a 43038-byte ceiling, and the scroller default breaks the review-1 off-glyph fix. So PC-F26 holds until PC-F44 creates headroom and is then released again; no raise, no weaker fix. PC-F44 moves ahead of other fix items.
- Why: ./grid has crept up by hundredths of a KB per item since D-067, so each grid change now needs a ruling. Only a re-split makes room for the grid work still to come.
- For Ahmad: none.

### D-093 (2026-11-15): tenant time zones resolve through the pinned CLDR table (PC-049)

- Ruling: the tenant settings store resolves a tenant zone's Windows id through the pinned CldrTimeZones table (the one Queryex and the core.TimeZone value set use), not the host ICU's TimeZoneInfo.TryConvertIanaIdToWindowsId, and the startup time-zone check tests exactly what the store requires.
- Why: Review 2 found a host with current tzdata but older libicu passes the startup check, then silently falls back to UTC for a tenant on a newer zone (e.g. America/Coyhaique, tzdb 2025b): wrong Today and wrong SQL AT TIME ZONE. One pinned table keeps every layer on the same answer (D-015, D-029).
- For Ahmad: spec 0013 §5.6 names TryConvertIanaIdToWindowsId; it should name the pinned table instead.

### D-092 (2026-11-15): tiles bundle ceiling and the 10,000-tile scroll requirement (PC-061)

- Ruling: the `./tiles` entry point's ceiling is 10.14 KB (measure 10.04 KB) with a budgetNotes entry, not spec 0023 §1's 7 KB. Review 1 measured it feature by feature: with tree mode, skeleton and flash all removed it is still 8.89 KB, so 7 KB cannot be reached without dropping spec'd behaviour. The "." ceiling follows D-072 (9.18 KB). The §12 "10,000 tiles, no long task over 50 ms" probe measured 48 long tasks in 80 wheel steps (median 71 ms, max 164 ms): a real defect, not a flake, so it is fixed in fix 1 and never quarantined. If fix 1 cannot reach 50 ms, the probe stays @perf (nightly) and failing, and a fix item is opened.
- Why: a quarantine tag is for flaky tests (LEDGER "Flaky"); hiding a deterministic miss of a spec requirement would let it ship. The size cap is a spec estimate the implemented behaviour cannot meet.
- Outcome (fix 1, 98bec3b0): steps 1-4 brought over-50 ms tasks from 54-75 to 6-28 per 80 steps on a quiet host, but the target is not met; the probe is un-quarantined, @perf (nightly) and asserts 50 ms. Fix item PC-F46 takes the rest and may recycle tile views if focus, selection and a11y semantics hold. The perf fixes cost 0.18 KB, so ./tiles is 10.22 KB against a 10.32 KB ceiling.
- For Ahmad: spec 0023 §1's 7 KB cap for ./tiles should be about 10.3 KB, unless tree mode moves behind a lazy seam (which changes the spec'd `parentId` input and is needed by 0030-g anyway).

### D-091 (2026-11-15): row-set routes fingerprint only their envelope (PC-064)

- Ruling: every `query` route shares one contract fingerprint per (stack, action) ContractRevision pair, because a row set describes its own columns. A stack's queryable shape is not folded into the fingerprint.
- Why: spec 0016 §3.10 builds the fingerprint from the request and response JSON contracts plus the revisions, and QueryRequest/RowPage name no stack type. The fingerprint is no client cache key: caches revalidate by version tags (0016 §6.2, 0025), and its only consumers are the Tellma-Contract 412 check and the draft save-route stamp (0031 §11.1). A stale tab querying a renamed column gets 400 query-invalid, never a silent write; it only misses the 412 reload prompt.
- For Ahmad: spec 0016 §3.10 could state that row-set routes fingerprint only their envelope.

### D-090 (2026-11-15): tree saves under RCSI take update locks; cycle write-skew; sibling blobs on cascade (PC-042)

- Ruling: Review 2 of PC-042 reproduced on RCSI that a save beneath a node that a concurrent move is re-pathing keeps a stale id path for good, and Verify cannot detect it. Correctness wins over spec 0015 §12.1's "No additional locks": the tree statements take UPDLOCK in T1 and on T2's parent read (or an equally correct lock-free fix the implementer finds), with a test that fails without it. The write-skew test CHECKPOINTS names is added. If two cycle-closing saves still meet only through a 1205 deadlock and retry, the test asserts that the cycle is caught, and code comments do not repeat the spec's claim. The implementer decides sibling blob capture on cascade by the nearest rule (0012 §8.5/§9.2, 0017) and states it under Deviations.
- Why: a stale materialized path is silent, permanent corruption of a tree. A spec line about locking cannot outrank it, and nothing waits for Ahmad.
- Outcome (fix 2, 9279c407): T2's anchor-parent read takes UPDLOCK, ROWLOCK, FORCESEEK; T1 takes no lock (pushed back with a reason recorded in the review file). Two cycle-closing saves either serialise or meet through a 1205 deadlock whose re-run hits the fence. A sibling holding a blob is deleted explicitly, with its blob captured (deviations 17-19).
- For Ahmad: (1) spec 0015 §12.1 must drop "No additional locks" for the tree statements, and its claim that the guard's U-locks serialise two cycle-closing saves is inaccurate (they meet through a deadlock and retry); (2) specs 0012 §8.5/§9.2 do not say whether a cascade delete captures sibling blobs; (3) Review 1 accepted PC-042's deviations #5 (MAXRECURSION is MaxDepth − 1, correcting an off-by-one in 0012 §10.2), and #8, #9 and #12 are spec gaps or signature differences listed in that checkpoint's commit message.

### D-089 (2026-11-14): ledger snapshots wait while an integration is in progress

- Ruling: while an integrator is merging and gating for platform-core, the orchestrator does not push LEDGER/DECISIONS snapshots. It keeps the edits in the main checkout and pushes them right after that integration's push lands. Ahmad's rulings are still recorded at once, but their push also waits.
- Why: PC-F29 had to merge platform-core three times, once only because of a ledger-only snapshot. Each extra merge costs the integrator a fetch, a merge and a judgement call on gates.
- For Ahmad: none.

### D-088 (2026-11-14): the orchestrator folds Ahmad's rulings into PROCESS.md and PLAN.md

- Ruling (Ahmad, 2026-11-14): the orchestrator may edit PROCESS.md and PLAN.md to fold in a ruling of Ahmad's (a D-nnn marked as his ruling). The "never edit" line in the §4.1 brief binds spawned agents, not the orchestrator. Edits are made in the main checkout only, cite the D-nnn in the changed text, and are committed with the next snapshot.
- Applied first to D-087: the hold step (§4 diagram and text), the implementer brief's closing line (§4.1), the hold message and the integration note (§4.3), the release-queue rule (§5.1) and slot freeing at hold (§5.3).
- For Ahmad: none (his ruling).

### D-087 (2026-11-14): holding does not merge or re-run gates; merge and gate once at release

- Ruling (Ahmad, 2026-11-14): when an item holds after its last fix round, it does not merge origin/platform-core or re-run PLAN.md §7.1 items 2-4. It runs `git merge-tree origin/platform-core HEAD` (after `git fetch origin`), reports any conflicts it shows, and pushes only pc/PC-xxx. The merge and the gates run once, when the orchestrator releases the item (the §4.3 integration message). This replaces the merge-and-hold step for every item that holds from now on.
- Why: throughput. Held items were merging and gating twice, once at hold and again at release, because platform-core always moves while they wait.
- For Ahmad: none (his ruling). Folded into PROCESS.md §4, §4.1, §4.3, §5.1 and §5.3 under D-088.

### D-086 (2026-11-14): popup cell editors — grid-editing entry point, pane parent, composite editors (PC-056)

- Ruling: popup cell editors live in the lazily loaded `./grid-editing` entry point (grid 41.93 KB, grid-editing 7.42 KB against a ceiling raised from 7.34 to 7.52 KB, with a budgetNotes entry). The pane's DOM parent is the in-cell `.tm-grid__editor` box. Composite cell editors (sub-forms in a popup, e.g. the window and filter editors of spec 0032) must shield inner tm-* controls: provide their own TM_CELL_EDITOR_HOST and register with the grid's through `{ skipSelf: true }`. TmGridEditorDef documents this, and an ngDevMode error fires when more than one control registers.
- Why: TM_CELL_EDITOR_HOST is last-registration-wins, so an unshielded sub-form would steal the cell's editor session; the split keeps editing code off read-only grids.
- For Ahmad: spec 0010 §C.15 and ARCHITECTURE.md disagree on three points that need aligning: (1) opening a readonly column's popup cell shows a read-only view mode; (2) a readonly grid that has a popup column still preloads grid-editing; (3) the pane's DOM parent is the in-cell editor box.

### D-085 (2026-11-14): other tellma-core-tools spawn-test timeouts under local load (PC-F24)

- Ruling: D-078 extends to the two other tellma-core-tools specs that spawn real child processes, `build.spec` "stops with exit 3 …" and `serve.spec` "fails before the emit …". A local lint:test failure caused only by these timing out does not block, provided each passes when run alone. CI stays the authority: a failure there is stop-the-line as usual. Both are tracked as flaky rows; PC-F29 (splitting the shim test) is the model for a fix if they ever fail in CI.
- Why: PC-F24 saw them time out only with five slots loading the machine, and both passed 25/25 alone. They test tooling that PC-F24 does not touch.
- For Ahmad: none.

### D-084 (2026-11-14): shadow-property gate allow-list and scope (PC-F41)

- **Allow-list.** The startup gate (in core.entity-metadata) allows the period columns and Node per spec 0012 §14, plus five model-only machinery columns that other specs require: Job StateJson, LeaseToken, ConfirmedKeys (spec 0020 §2.3) and Blob StorageKey, Sha256 (spec 0017 §2.1-2.2). Entries are keyed by CLR type + property name; Job and Blob are sealed and only Core can contribute model configurations.
- **Scope.** Only the tenant model is gated: it is the only model taking pack or distribution input. The catalog context is Core-only, and OpenIddict's vendor entities carry shadow FKs by design.
- **Neighbouring fix.** core.Job.ConfirmedKeys is now withheld from the Queryex schema, as spec 0020 §2.3 requires.
- **For Ahmad:** spec 0012 §14 says "two platform shadow properties exist"; it should list the five machinery columns from 0020 §2.3 and 0017 §2.1-2.2 too.

### D-083 (2026-11-14): outbound HTTP logging default and SendGrid refusal level (PC-F39)

- **One default, in core.** `AddTellma` removes IHttpClientFactory's default loggers for every named and typed client, so no host that composes a distribution (web, migrator commands, emit-client-assets) can log request headers such as bearer tokens or API keys at Trace. The standalone identity host keeps its own call because it does not compose a distribution; PC-055's per-client call on the identity client stays as a harmless duplicate. Trade-off, documented in the Tellma.Core, Tellma.Identity.Web and SendGrid adapter READMEs: the per-request "Start/End processing HTTP request" lines are gone; outbound calls stay visible as HttpClient spans and metrics where an exporter is configured, and each sender logs its own failures.
- **SendGrid refusal at Warning.** `RequestFailed` moves from Debug to Warning (status + provider error text, never the key), matching spec 0007 §9.2's table ("Message failed" at Warning) and its PII rule.
- **For Ahmad:** spec 0007 (around line 1801) still says the adapters record a refusal's status and error code at Debug; it should say Warning.

### D-082 (2026-11-14): reorder-list single-pointer path, drop fallback, tree prefetch and budgets (PC-031)

- **Reorder handle.** The handle's keyboard grab does not satisfy WCAG 2.5.7, which needs a single-pointer alternative to dragging; screen-reader activation also sends a click. A click on the handle therefore opens a menu: Move up, Move down, Move to top, Move to bottom, disabled at the ends. Each choice moves the item, emits `moved`, announces, and returns focus to the handle. The keyboard grab model stays.
- **WebKit drop fallback.** A drop without the drag-id type is accepted only when its text/plain is non-empty and exactly matches the drag in flight, and its types include neither the drag type nor `Files`. The in-flight record ends on window dragend/drop. A drop from another Tellma tab carries its own id and is refused. A nonce in the text was rejected, because the text is what editors insert.
- **Tree prefetch.** `children()` runs when a node first renders, so leafness is known before expansion. A failed prefetch is silent and retried on expansion.
- **Budgets.** pager 2.8, tree 5.09 and reorder-list 4.8 (raised from 4.47 at fix1 for the move menu; measured 4.70) KB (linked + 0.1), and root 9.14 KB, each with a budgetNotes entry.
- **For Ahmad:**
  - Spec 0023 §8.3/§8.4 name the keyboard handle as the 2.5.7 path, which it is not.
  - Spec 0023 §8.2 should allow the render-time prefetch. An optional `hasChildren` hint would let fetching loaders opt out.
  - Spec 0023 §2.1 should add Enter toggling in `multiple` mode (per 0029 §11.3).
  - Spec 0023 §1's 2/4/3 KB caps are superseded.
  - The global `styles/tm-drop-target.css` sheet contradicts 0023 §1's "one global stylesheet", and 0024 §7.6's `styles.css` must import it.

### D-081 (2026-11-14): analyzer reach, generated code and the shadow-property gate (PC-040)

- **Transitive reach.** Tellma.Core.Abstractions ships `buildTransitive/Tellma.Core.Abstractions.targets`. It adds the TELLMA analyzers from the package's `analyzers/dotnet/cs` unless they are already present, because NuGet's default `exclude="Build,Analyzers"` otherwise drops them for consumers of Tellma.Core and of distributions. design-e2e proves it on a Tellma.Core-only consumer.
- **Generated code.** The rules analyze generated code and report in it (`Analyze | ReportDiagnostics`). An `// <auto-generated/>` header must not bypass TELLMA0004 or TELLMA0006.
- **TELLMA0006.** It keeps the strict reading: every mapped entity table is stack-owned, `Job` included, since `Job` is a `[Stack]`. There is no exemption list. An escape hatch, if one is ever needed, is a spec 0015 contract attribute.
- **Test suppressions.** In the Core test projects, TELLMA0003 and TELLMA0006 are suppressed through `.editorconfig` globs over `Data/**`, `Caching/**` and `Jobs/**`, not project-wide.
- **Shadow properties.** No Roslyn rule bans navigations without explicit FKs: the contract has no CLR navigations, and a shadow FK is a model-build fact. ARCHITECTURE.md says instead that the startup gate refuses any shadow property other than the period columns and `Node`. That gate is PC-F41.
- **For Ahmad:** spec 0012 §14 says only "no implicit shadow properties is the rule", without naming the enforcing check. ARCHITECTURE.md's promise of an analyzer is withdrawn in favour of a startup gate.

### D-080 (2026-11-14): identity service-client secret placement and outcome tags (PC-055)

- **Ruling.** The `<slug>-svc` client secret (Tellma:Identity:ServiceClientSecret) is required in every Production host that calls the identity server: the migrator and the standalone web hosts, since invite and issue-credentials run in the web host. It is exempt only in in-process Development, where the web host fills it from the per-boot IdentityClientSecrets. Its validation moves from the `host.identity` startup check to the `core.options` validator. The `tellma.identity.*` instruments gain the outcome tag `cancelled`, for a caller's own cancellation, so that it is not counted as `error`.
- **Why.** Spec 0018 §2.1 and 0011 §6.1 need the secret where the calls are made. Counting a caller's cancellation as a server error would page on user behaviour.
- **For Ahmad:** spec 0011 §5.7 still names `host.identity` as the owner of the check. Spec 0018 §2.2's closed outcome-tag set lacks `cancelled`. Deployment templates and onboarding must supply the secret to both hosts.

### D-079 (2026-11-14): PC-062 contract placement and signature conventions

- **Golden placement.** The pipeline public-API goldens live in Tellma.Core.Abstractions.Tests/PublicApi, beside the existing Abstractions goldens. They do not live in Tellma.Core.Tests/Crud, which spec 0015 §1.1/§18.1 names: the contracts ship in Tellma.Core.Abstractions, so their surface is pinned where the assembly is tested.
- **TKey constraint.** Where a contract sketch closes over Entity<TKey>, TKey carries `struct, IBinaryInteger<TKey>`, the constraint Entity<TKey> itself declares.
- **Cancellation.** Every async contract member takes a trailing `CancellationToken cancellationToken = default`, even where the sketch omits it.
- **Closed hierarchies.** ActionDescriptor and ExportSource are closed with an internal abstract member, following the PathSegment precedent (review R1-1).
- **For Ahmad:** spec 0015 §1.1/§18.1 still name Tellma.Core.Tests/Crud for the golden, and its sketches omit the TKey constraint and the cancellation tokens; bring the spec in line.

### D-078 (2026-11-14): the tellma-core-tools shim-test timeout does not block an integration

- Ruling: while CI stays green, an integrator's local lint:test failure caused only by `tellma-core-tools/src/common/common.spec.ts` "starts a package manager's shim … and fails a missing program" timing out at about 5 s does not block a push, provided the file passes when run alone. PC-F29 is raised to medium and takes the next free fix slot. If the test fails in CI, it becomes a stop-the-line item as usual.
- Why: the test spawns a real pnpm shim and exceeds its 5 s limit only when parallel slots load the machine. PC-030's change touches no tools code, and holding integrations on a known local timing flake would stall the line for nothing.
- Update (2026-11-14): PC-F29 integrated (CI 43684370336). It split the shim test into three renamed tests with explicit timeouts and made build.fixture.spec.ts robust under load, so this waiver is no longer needed for those two tests; the flaky rows are closed. D-085 still covers the build.spec and serve.spec timeouts.
- For Ahmad: none.

### D-077 (PC-054, 2026-11-14): remembered tree expansion loads with the new content's rows

- Ruling: keep PC-054's R1-8 deferral. A contentKey change runs the remembered lazy rows' loads only when a new rows array arrives (array identity), so a switch never loads children for the outgoing content's rows. The contentKey TSDoc states the contract: a new contentKey comes with its own rows array. Until that array arrives, remembered lazy rows show expanded with no children and no spinner.
- Why: loading at the bind fetched children of the outgoing content whenever row ids overlapped between the two contents, which is a wrong read and not only wasted work. Keeping the same rows array across a deliberate content switch is caller misuse, and documenting it costs nothing.
- For Ahmad: (1) spec 0010 §C.9 should state that contentKey and rows change together; (2) spec 0031's deep-link timing (§C.8 cites it) should name afterRenderEffect, because an effect in the grid's own host runs before the grid's bindings update and scrollToRow returns false (carried to PC-135).

### D-076 (PC-044, 2026-11-13): cacheable-entity entries are keyed by the tenant's multilingual shape

- Ruling: CacheableEntityKey (and so the single-flight key) carries the tenant's MultilingualShape. A language change gives a fresh key, and the old shape's entries age out. The `entities` and `entity:*` tags do not move on a language change. A Persist-batch FromCache miss takes a lock-only guard row on entity:<Name> (NULL expected tag), so a concurrent write is serialised as it is on a hit.
- Why: the cached column list follows the shape, so a tag-only check would serve the old columns until someone wrote the table, and a mixed-shape single flight would hand one caller the other shape's rows. Keying is cheaper than bumping every entity tag tenant-wide.
- For Ahmad: spec 0013 §6.3 is silent on language changes. The server side is closed by the key, but the client's entity wire tags do not move, so PC-049 (carry) must make the client notice. Spec 0013 §4.4 should also say that a Persist miss still guards the tag.

### D-075 (PC-054, 2026-11-13): tree expansion moves into ./tree-grid now

- Ruling: PC-054's fix round moves the tree-only expansion and batching machinery (about 0.80 KB linked) out of ./grid, behind the treeConfig seam, into ./tree-grid. The flash path stays in ./grid. Both ceilings are re-derived on the linked measure (+0.1). The budgetNotes entry states the headroom under the 42 KB cap in bytes.
- Why: commit 1 left ./grid 168 B under spec 0010 §C.11's 42 KB hard cap, so every flat grid paid for tree code, and the next ./grid change would have forced D-057(2)'s re-split anyway. The seam already exists, so the move is cheap now and costs more later.
- For Ahmad: none (within §C.11 and D-057).

### D-074 (PC-097, 2026-11-13): job owners are checked in SQL, and enqueue needs a transaction

- Ruling: enqueue accepts any in-memory JobId. The owner UPDATE succeeds only when the owner row exists and its JobId is null or names an ended job, read under READCOMMITTEDLOCK; `@@ROWCOUNT` is compared with the owner count, and a mismatch throws 50422 `Jobs.OwnerUnavailable`. Enqueue outside a transaction throws `Jobs.EnqueueOutsideTransaction` (TransactionMode.None is refused). The codes are public in JobEnqueueCodes. This replaces the implementer's D-8 (an in-memory null-JobId check).
- Why: spec 0022 §7.1's outbox retry re-enqueues an owner with a terminal JobId. An in-memory check also misses stale or partly loaded entities and races between concurrent enqueuers, which the UPDATE's locks serialise. Without a transaction, the job INSERT and the owner UPDATE are not atomic, and the retry rules could duplicate jobs.
- For Ahmad: spec 0020's enqueue contract should state that the null-or-terminal JobId rule is enforced in SQL, and that enqueue requires a transaction; spec 0022 §7.1 relies on it.

### D-073 (PC-043, 2026-11-13): a wildcard-resource grant implies Read only where its action is registered

- Ruling: `(*, A')` with A' ≠ Read matches `(R, Read)` only when R registers A'. `(R, A')` is unchanged. UserAccess keeps a per-action resource set built from the registry. This is narrower than spec 0014 §5.2 step 2 and fails closed. Spec §8's delegation coverage of `(R, Read)` by `(*, A')` should follow the same rule (PC-078 carry). PC-043 D5 stays: `(*, A)` with an action no resource registers is UnknownAction drift, and PC-078's §3.7 rule 1 save check must refuse it.
- Why: read literally, step 2 lets an odd grant such as `(*, Invite)` give unrestricted Read on every resource, including core.Role and settings. Write-implies-read exists so a user can see what they act on, and a resource without A' offers nothing to act on.
- For Ahmad: (1) spec 0014 §5.2 step 2 and the delegation rule's `(*, A')` clause need the "where R registers A'" qualifier; (2) §3.7 rule 1 should refuse `(*, A)` for an action no resource registers.

### D-072 (PC-030, 2026-11-13): form-control entry points above their spec 0023 §1 caps

- Ruling: the six new entry points (radio, segmented, switch, search-field, date-range, date-time) may ship above spec 0023 §1's caps of record (2, 2, 1, 2, 2 and 3 KB). These are recorded raises, as in D-058. At integration each ceiling is its linked size (D-067) + 0.1 KB. ./private and "." are re-derived as the parent's linked ceiling plus the checkpoint's measured growth. Each budgetNotes entry states the spec cap, the measured linked size, the savings tried, and why. TmDatePicker/TmTimeField `parseErrors` stays internal (ɵ or @internal), since only the composites read it. The no-write rule compares wall times, not minute-floored instants.
- Why: §1's figures predate the Signal Forms state surface every control carries (about 3.2 KB, as in tm-checkbox). Trimming below that would mean dropping the forms integration the same spec requires.
- For Ahmad: (1) spec 0023 §1's caps for these six need restating, or the Signal Forms surface needs to be counted separately; (2) §10 has no message for the time field's parse error, so `errors.parseTime` (en + ar) is new; (3) §1's "one global stylesheet" now also needs styles/tm-search.css (merge-time task: @tellma/core/styles.css must import it); (4) §6.4's "exact instants" for the no-write rule would rewrite a bound 11:00:30 as 11:00:00, so the rule should compare wall times.

### D-071 (PC-075, 2026-11-13): the interim session expiry signs in instead of reloading (amends D-048)

- Ruling: until PC-110's expiry dialog lands, TmSession's interim `expired()` sweeps the user's stored partitions and navigates to sign-in; it clears `sub` only when that sweep did not apply (cut short, rejected or resolved false), and otherwise keeps it per spec 0024 §8.6. This replaces D-048's reload. D-048's intent holds: an expired tab never keeps showing the user's data. The reload's only advantage was simplicity, and the sign-in avoids a pointless round trip to the boot page.
- Also from PC-075 review 1: the store's boot step writes `sub` itself, but only when the store opened on disk. A store that could not reach an existing IndexedDB (3 s open timeout or a transaction failure) never writes `sub` (it does write it when IndexedDB is absent or under a memory host, which is harmless), and its sweeps report "not applied", so the next launch's backstop sweep still runs. `ɵstart` never rejects (spec 0025 Decision 13).
- For Ahmad: spec 0025 §2.6 says a receiving tab empties the mirror entries under a sweep's prefix, but it re-reads them. Spec 0025 §2.1/§2.6 and 0024 §8.1 need text saying who writes `sub`; spec 0025 §2.1's contract needs `sweep(): Promise<boolean>`. The user sees no explanation on expiry until PC-110.
- Addendum (PC-075 R2-1): a storage fault after the store opens (an abort other than quota, a request refused at once, or a failed reopen) moves the store to memory and resolves put/patch/readAll, beyond spec 0025 §2.7's open-and-quota cases. For Ahmad: §2.7 should cover faults after open.

### D-070 (PC-057, 2026-11-13): unbound entity labels are bidi-isolated in both the picker and the grid

- Context: spec 0010 §E.1 says both "One-line rows do the same" (wrap the label in <bdi>) and "unbound, rows render as today"; §E.4 repeats the contradiction for the grid display cell. PC-057 commit 1 resolved the picker one way (<bdi>, dir=auto) and the grid the other (plain span), so in RTL an unbound column drew code-first in the cell and name-first in its own dropdown (Review 1, R1-2).
- Ruling: isolate in both. An unbound label renders inside one <bdi> in picker rows and in the grid's unbound display cell, matching the input's dir="auto"; an RTL test pins the agreement. Also accepted from review 1: maxItems' Done-disabled string belongs to PC-058; label parts render only when they join with " - " to the display text (picker and grid alike); the hidden ", " accessible-name separator stays a literal.
- For Ahmad: pick one reading of §E.1/§E.4 (this ruling chose isolation).

### D-069 (PC-038, 2026-11-13): version-tag guard locking, corrected twice by review
- Spec: 0013 §2.5, §2.4.
- Situation. The spec's guard (EXISTS … Tag <> expected) holds no lock on rows that still match (SQL Server releases locks on rows a seek's residual predicate rejects). Review 1 found the first replacement deadlocked a settings save against a role save; Review 2 found a tenant-level bump deadlocking against a stamp-row write.
- Rulings (orchestrator, fix rounds 1 and 2):
  - The guard steps through the expected names in Latin1_General_100_BIN2 order by seek, taking each lock in its final mode: XLOCK on names the batch bumps, REPEATABLEREAD (S) on the rest; every tag row before the stamp row. Deadlock race tests reproduce 1205 on the earlier code and none on this one.
  - VersionTagList.Tag is nullable: NULL means lock-only, so a VersionTag.None expectation is still compared.
  - The connect prologue marks its statements (DataBatch.BeginConnectPrologue); only those are left out of the bump plan. Other prologue writes that bump a tag, tagged writes after the bump, and None-mode batches with a bump plan are refused. The migrator bumps on a failed run, bounded by Tellma:Migrator:VersionTagBumpTimeout (30 s); the wholesale bump takes TABLOCKX.
  - The two-tenant isolation test moves to PC-050 (CHECKPOINTS, WATCHLIST).
- For Ahmad: correct 0013 §2.5 to the ordered, final-mode guard; the NULL lock-only marker; the residuals not prevented (two batches each writing the other's caller's stamp row; any stamp lock taken before the guard), which end through 1205, re-run, then 50412 or commit.
- Planner note for PC-050: use BeginConnectPrologue, and either refuse TransactionMode.Explicit on a batch carrying the connect prologue or keep the activity stamp out of the transaction (WATCHLIST).

### D-068 (PC-037, 2026-11-13): allocator and save emitter — deviations and an executor ordering note
- Spec: 0012 §7.1, §7.2, §8.
- Implementer rulings (pending reviews):
  - IIdAllocator.TakeAsync takes an ITenantDatabase instead of the spec's TakeAsync(Type, int): the singleton allocator has no ambient tenant.
  - Low-water refills run only for sequences whose buffer is already in use, not every table in WrittenTables (Jobs and Blobs no longer burn 64 ids per batch).
  - The touched set holds root ids only; grandchild writes reach their root through capture tables.
- Planner note for whichever of PC-037 and PC-038 integrates second: the executor now appends refill statements after the caller's statements and before the epilogue stage, and runs batch completion steps (DataBatch.OnExecuted) after the audit and before the outcome and the commit callbacks. Save registers no UserIdSource, so user-level tag bumps on saves are wired by the version-tag code, not the emitter. The second integrator re-runs the version-tag lock and bump suites on the merge. After review 1 the order is: audit, snapshot replacement (tenant and user), completion steps, outcome, commit callbacks; RoundTrips is computed after the steps. A refill that finds its sequence behind logs IdAllocator.BehindOnRefill, counts a heal and drops buffered ids at or below the table without its own round trip; the next reservation heals.
- For Ahmad: (1) spec 0012 §7.1's TakeAsync signature; (2) §7.2's refill-on-every-written-table text; (3) §8's touched-set example covers level 1 only.

### D-067 (PC-051, 2026-11-13): the size gate measures what ships; ./grid stays under 42 KB
- Situation. PC-051 (spec 0010 §C.4, §C.14) raised ./grid from 39.25 to 43.5 KB (measured 43.27). Review 1 traced the reported duplicate: ng-packagr's partial compilation writes each component's template and styles twice (ɵɵngDeclareComponent and ɵɵngDeclareClassMetadata); an app's linker guards the second copy behind dev mode and production drops it. Measured ./grid (KB gzip, parent → PC-051): gate 39.01 → 43.27; gate without class metadata 37.29 → 41.23; linked production output 35.58 → 39.98.
- Ruling.
  1. The bundle-size gate measures linked production output for every entry point. All ceilings are re-baselined in PC-051's fix round: new ceiling = new measure at the parent + the entry's old headroom, never above a spec ceiling of record.
  2. Spec 0010 §C.11's 42 KB is the hard cap for ./grid on the linked measure; PC-051's ceiling is its linked measure + at most 0.1 KB, leaving about 2 KB for PC-054 (§C.8, §C.9). D-057(2) still holds: no raise beyond 42 KB without a re-split or real savings.
  3. Workarounds shaped only by the artifact (the separate row-end cell component, the moved template) are undone unless they earn their place; the split grid-view-header-nav.css merges back unless loaded separately, with no raise of the 13.5 kB anyComponentStyle error.
  4. PC-051 escalates to Review 2 (§4.4) on R1-1.
- For Ahmad: (1) the size gate now measures linked production output; every published ceiling changes number (not headroom); (2) §C.4's key table has no answer for interactive header content that is not a header action; (3) §C.4's sketch makes TmGridHeaderAction a directive, but it must be a component to carry its styles. (4) Corrected after review 2 (the earlier thirteen-entry list was wrong): after the merge with platform-core, ./grid measures 40.42 KB linked (ceiling 40.51), leaving 1.49 KB under 42 for PC-054. grid-view.css was split, with the header-row rules moved to grid-header.css, to stay under the 13.5 kB error. The primary "." ceiling was raised 8.6 → 8.75 for the grid header strings. Nineteen entry points measure above a spec cap that recorded raises had already superseded: spec 0023: avatar, badge, banner, busy-overlay, chip, copy, empty-state, icon, kbd, progress, skeleton, toast; spec 0005 §16: core-ui primary ".", l10n; spec 0004: menu; PC-026's at-birth raises: card, error-dialog, page-header, splitter. relative-time (0.96 of 1.00) and image (9.53, ceiling 9.72 of 10) are under their caps. (5) On a sortable header, Enter sorts, so other interactive header content is reachable by pointer only; §C.4 needs a row for it.

### D-066 (PC-047, 2026-11-13): sessions and BFF — implementer rulings (pending reviews)
- Spec: 0011 §5.5, §3.8, §5.7; 0018/0022 sandbox context.
- Rulings (provisional until Review 1 and Review 2 judge them):
  - The default authorization policy is the framework default (names no scheme); only the fallback is the session policy, so the in-proc engine's [Authorize] endpoints keep the engine's scheme. The endpoint audit refuses any non-platform endpoint under a policy that names no scheme: distributions must not use bare RequireAuthorization().
  - Signing in over an existing session rotates it (fresh key, old row deleted, listeners notified), closing a fixation path through RenewAsync.
  - Refresh is single-flight per instance; across instances the TokensVersion compare-and-set picks one writer and the loser adopts its ticket, relying on the authority's 30 s reuse leeway (0011 §5.5).
  - The in-proc engine is bound by reflection (no compile-time reference from Tellma.Core.AspNetCore to Tellma.Identity); host.identity requires an IEmailSender in in-proc mode.
- Review 1 (R1-2) showed the risk is a certain failure (EmailRouter reads TenantSandboxContext.IsSandbox, which throws with no tenant). Orchestrator ruling: fix it in PC-047 rather than gate on a later checkpoint; the implementer gives tenantless engine mail an explicit sandbox/audience answer consistent with 0007/0018/0022 and tests it end to end. Former open risk, owner: the email pipeline (0018/0022 checkpoints). In-proc, the engine sends mail outside any tenant (invitations, sign-in codes), but a distribution's ISandboxContext is TenantSandboxContext, which throws with no tenant bound. The tenantless sandbox answer must be decided there (WATCHLIST lead).
- Fix 1 outcome: in-proc engine mail is platform mail and is never sandboxed (0007 §3.1's answer for a host without tenants). The engine resolves its sender by key; in-proc the host keys a sender that wraps the composed ISandboxContext so it answers false only while that sender sends in a tenantless scope. Every other tenantless send still throws. Sandbox tenants cannot reach it (their invitations are ExistingOnly, 0018 §3.3). Tested end to end through the engine queue and the real router. The login policy now bounds the caller (policy acr replaces the caller's, shorter max_age wins); new timings: 20 s refresh backoff after a deferral, 10 s redemption deadline.
- For Ahmad: (1) confirm identity mail is never sandboxed; (2) spec 0011 §3.9's "engine registers its own ISandboxContext" cannot hold in a shared container and needs a correction; (3) confirm that distributions may not use scheme-less RequireAuthorization(); (4) the refresh backoff and redemption deadline are not in the spec. Review 2 overturned two fix-round rulings: the login policy now sends the stronger of its tier and the caller's step-up tier (not the policy's alone), and the refresh timings are re-chosen to keep every retry inside the authority's 30 s reuse leeway. (5) Spec 0011 §3.8: MembershipReconcileService runs once per instance per interval (the applock only excludes overlap); kept, as the reconcile is idempotent; the spec should say so. (6) Final refresh timings (review 2 fixes): 5 s redemption deadline (cold metadata bounded), 2 retries 1 s apart for possibly-spent tokens and failed writes (12 s window, under half the 30 s leeway), 20 s backoff only for failures that never reached the authority. (7) Spec 0011's default step-up class urn:tellma:acr:mfa is not in spec 0003 §9's tier scheme (aal1-3, phr/phrh): an unrankable caller acr wins only when it equals Tellma:Session:StepUp:Acr; one spec must align with the other.

### D-065 (PC-073, 2026-11-13): tellma-client build/serve rulings and a new PC-094 gate
- Spec: 0024 §2.1, §2.6, §4.2, §6, §12.
- Rulings:
  - ng build runs with NG_BUILD_OPTIMIZE_CHUNKS=false: Angular 22's chunk optimizer renames chunks with mixed-case base64url hashes that fail §6's hashed-name rule and leaves stats.json out of step with the output (verified by Review 1 against @angular/build 22.0.5).
  - Budgets gain a bootScript row (3 KB + 0.4 KB per extra string table, per D-048). -ext font subsets and fonts of scripts a distribution does not offer belong to no worker group (network, browser-cached).
  - PC-094 is now also gated on PC-049 (DEPS.md, CHECKPOINTS.md): a real Migrator's emitted metadata has no languages until PC-049 emits that section, so tellma-client build stops on "offers no languages".
  - PC-073 escalated to Review 2 (§4.4) on R1-1 (blocking: lint:test depends on a gitignored build output).
- For Ahmad:
  - (1) Spec 0024 §2.6/§2.1 do not say how tellma-client serve delivers tm-boot.js and the brand files; without them the boot script 404s under serve. PC-073 adds it; the spec needs a clause.
  - (2) Until PC-094 lands the @tellma/core/brand defaults, every distribution must override all four brand SVGs.
  - (3) Spec 0024 §4.1's index.html sketch has no <base href="/">: deep-link reloads under /<tenant>/ resolve scripts relative to the path and 404. PC-073 makes it part of the build contract (review 2); the spec needs it.

### D-064 (PC-026, 2026-11-12): error-dialog budget and confirm label
- Spec: 0023 §5.5, §5.6, §10, §1 budgets.
- Rulings:
  - The error-dialog entry point's budget is raised from 3.8 to 4.2 KB (measured 4.07). Almost all of the +0.41 KB is the description-list stylesheet, now scoped to the dialog content's view so the dialog does not depend on an app-level global style. The zero-cost alternative (a globalStyles entry plus a TSDoc note) leaves a hidden dependency on the app; rejected.
  - TmConfirm.confirm()'s confirmLabel is optional and defaults to confirm.ok ("OK"). §5.5 makes it required, which left §10's confirm.ok string with no consumer.
  - Stop the line on CI 43325841371 (ddb8259, flaky grid-a11y forced-colors e2e): PC-026's merge 9d14e67 waits on pc/PC-026 until PC-F25 turns platform-core green; PC-F25 took the next heavy slot under the D-028 cap.
- For Ahmad:
  - (1) Confirm the 4.2 KB error-dialog budget, or say so and the stylesheet moves to globalStyles.
  - (2) Spec 0023 §5.5: make confirmLabel optional (default confirm.ok), or drop confirm.ok from §10.

### D-063 (orchestrator, 2026-11-12): PC-073 ahead of PC-057 while PC-F21 integrates
- Situation: when pc-2 freed, the ready T1 queue was PC-051 (held on PC-F21, D-057), PC-057, PC-073, PC-075. PC-057 adds grid cell options and spends ./grid bytes. PC-F21, which re-splits ./grid and lowers its ceiling to 39.25 KB, was mid-integration.
- Ruling: launch PC-073 (CLI, no grid code) first. PC-057 and PC-051 start on the first free slot after PC-F21 lands, so they build on the re-split grid rather than merge into it.

### D-062 (PC-033, 2026-11-12): Queryex host — collation, captured keys, child FK targets
- Spec: 0012 §5.2, §11.1, §13.2; 0015 §5.3; 0016 DetailsExpand.
- Situation and rulings:
  - Msg 468: a StringList restriction on a column with an explicit collation (core.User.Subject, BIN2) now applies COLLATE to the list's side, which keeps the column's index seekable. The engine (Tellma.Core.Queryex) gained PropertyDescriptor.Collation. EF's runtime model drops collation, so the provider reads [Collation] off the CLR property; a collation set only through the fluent API would be missed.
  - KeysOf (the captured key table) stays internal. Spec 0015's runtime lives in Tellma.Core and can reach it.
  - Child FK targets: spec 0012's EntityQuery has no per-child select, so the details round trip cannot load what children point at (e.g. Lines.Account). Provisional ruling: 0012 is unchanged; 0015 loads children's FK targets through a captured FK set (WATCHLIST lead under 0015).
  - Grouped Rows with CountCap counts groups (capped at cap+1), because 0016 allows Aggregate with IncludeCount.
- For Ahmad:
  - (1) Spec 0012 §5.2 has no public contract that can name a captured key table. Add one, or confirm it stays internal to Tellma.Core.
  - (2) Child FK targets in the details round trip: should 0012 gain a ChildSelect, or should 0015 load them via a captured FK set (the provisional ruling)?
  - (3) Fluent-API-only collations are invisible to Queryex. Settled by review 2: CollationConvention now throws on a fluent UseCollation on a tenant column, so [Collation] is the only source. Confirm.
  - (4) Review 2 added engine diagnostic QX3203 (comparing or combining text of different collations is refused), an addition to spec 0008's diagnostic table.
  - (5) core.UserStamp is a read-only Queryex root (no CLR type: Rows, Count and Assert only). Its PermissionsTag and PreferencesTag are withheld from the schema as cache validators; the spec is silent.
  - (6) TieBroken was removed: unpaged ordered reads (captures, ancestors, child pages) now compile with Skip = 0, so each ends in OFFSET @qx{n}_skip ROWS bound to 0, and the engine appends the key only when the ordering lacks it.

### D-061 (PC-035, 2026-11-12): seeded write-frame contracts and planner notes
- Spec: 0015 §7.5, §13.3; 0021 §2.3, §11; 0018; 0022.
- Situation: INotificationChannel.DeliverAsync needs IOpenWriteHost, which 0015 (PC-062) owns. PC-035 seeded contract-only versions of IWriteHost, WriteHostKind, IOpenWriteHost, EnlistmentOutcome and IConfirmations, shaped member for member as 0015 sketches them. Notification channel senders are created in the startup gate's tenantless scope to read their keys.
- Ruling, recorded as planner notes:
  - PC-062: extend the seeded write-frame types in Tellma.Core.Abstractions; do not re-declare them.
  - PC-062: IOpenWriteHost.PersistAsync takes a CancellationToken (added in PC-035 fix 1).
  - 0018-f and 0022-a: EmailNotificationChannel (and any INotificationChannel) must have a constructor that needs no tenant, because the gate resolves senders tenantless.
  - 0021-b: add the TableType<…>() contributions for the three notification table types together with the statements that bind them.
  - The future securable registry (PC-039/PC-063): NotificationTypesCheck.CheckTargets accepts composed entities; extend it to securable resources when that registry lands.

### D-060 (PC-F21, 2026-11-12): lazy grid editing — the draft window, budgets and Save
- Spec: 0010 §C.11, §C.15; 0004 §8.4, §11; 0006 §7.4.
- Situation: ./grid exceeded its 42 KB ceiling (D-057). PC-F21 moves the editors and the editing key resolver into a lazy ./grid-editing chunk, preloaded when the browser is idle once an editable grid renders. Two designs failed review (hold-and-replay keys in R1; a keyed draft in R2: modifiers, dead keys and IME broke it).
- Ruling: during the load window the cell shows a real bare <input> draft. The browser owns its text, and the user's own gestures commit it (Enter, Tab, blur, press on another cell); Escape cancels it. When the editor arrives it takes over the text, caret and selection. A failed or stalled load never commits; it reports once per grid. Entity drafts stay pending until the editor's own search resolves them. Ceilings: ./grid 39.25 KB (measured 38.99; 3 KB under the spec's 42 left for the list-page work), ./grid-editing 6.5 KB (6.44).
- Escalation: one §4.4 verification confirmed the R2 blocking and major findings fixed in a real browser with trusted input. Minor V-1 (Tab from the last cell's draft) is fixed at integration.
- For Ahmad:
  - (1) V-2: an entity typed in the window and saved before the editor arrives leaves the cell pending (value null). A screen that gates Save on errorCount alone saves null. The TSDoc now says Save must also wait for pendingCount. Should the grid also retry the load while a request is pending?
  - (2) Spec 0010 §C.11 needs a ./grid-editing row and the lowered ./grid ceiling.
  - (3) Spec 0010 §C.15 puts the popup editors' +1 KB on ./grid, and its "always edit mode" rule needs a window clause.
  - (4) Specs 0004 §8.4 (type-to-edit, caret, IME), 0004 §11 (undo stays with the draft input) and 0006 §7.4 (entity text resolves after the editor arrives) each need a load-window clause.
  - (5) Integration raised the showcase's `anyComponentStyle` error budget in client/angular.json from 13 to 13.5 kB (warning stays 12 kB). The draft rule pushed grid-view.css to 13.25 kB; it was trimmed to 13.07 kB. ./grid now measures 39.01 KB, 2.99 KB under the spec's 42.

### D-059 (orchestrator, 2026-11-12): check_deps readiness masked ready checkpoints
- Situation: tools/check_deps.py counted a gate as met only when its LEDGER status cell was exactly `done`. Since the 2026-11-11 status rows carry a note after the keyword (`done (CI … green; …)`), so every checkpoint gated on PC-023, PC-028, PC-029 and the like looked blocked, and `--ready` listed only T3 work. Eight T1 checkpoints (PC-026, PC-033, PC-034, PC-035, PC-051, PC-057, PC-073, PC-075) were in fact ready; slots sat idle for part of 2026-11-11/12 because of it.
- Ruling: the tool now treats a status that is `done` or starts with `done ` as met. Status cells keep their notes.
- Effect: PC-026 (pc-1), PC-033 (pc-3) and PC-034 (pc-f) launched at once (5 heavy agents with PC-F21 R2 and PC-F23). PC-051 still waits on PC-F21 (D-057); PC-035, PC-057, PC-073, PC-075 launch as slots free, in priority order.

### D-058 (orchestrator, 2026-11-12): PC-023 entry-point ceilings, and the shared core-ui primary
- **Provisional ruling**, for Review 1 to confirm. Each new spec 0023 entry point takes its measured size plus about 0.1 KB, as in D-049:

  | Entry point | Spec 0023 §1 | Measured | Ceiling |
  |---|---|---|---|
  | chip | 2 KB | 5.60 KB | 5.7 KB |
  | toast | 4 KB | 5.28 KB | 5.4 KB |
  | banner | 2 KB | 2.48 KB | 2.6 KB |
  | busy-overlay | 1 KB | 2.37 KB | 2.5 KB |

  The overshoot is mostly stylesheets (0.4 to 1.3 KB each) and duplicated Angular class metadata (0.17 to 0.49 KB each). If Review 1 finds avoidable code, the ceilings fall back.
- **Core-ui primary ".".** PC-023 raises it from 8.48 to 8.58 KB of new strings, and PC-048 from 8.48 to 8.61 KB (D-057). Whichever of the two integrates second re-measures after the merge and sets the ceiling to measured + 0.1 KB. That should be about 8.8 KB; neither branch's own number (8.7 or 9) carries over.
- **Banner buttons.** The banner uses its own 24 px buttons, because tmButton's smallest step is 30 px. Review 1 checks touch targets.
- **Process note.** Agents that write logs or drafts into the shared session scratchpad overwrote one another's files. From now on, briefs say: write logs under `<slot>/.tmp` with unique names.
- **Misalignment for Ahmad:** spec 0023 §1's budgets for chip, banner and busy-overlay are below the measured cost of their styles alone. §10's `{count}` placeholders become ICU `{count, number}`, and toast.more is a new key.

### D-057 (orchestrator, 2026-11-12): PC-048 budgets, and the ./grid re-split
- **Situation.** PC-048 (spec 0010 §C.1–§C.3, §C.5–§C.7) took ./grid from 38.73 KB to 41.92 KB gzip, after 0.10 KB of savings. Spec 0010 §C.11 gives ./grid 42 KB for all of §C.1–§C.14. That leaves 0.08 KB for §C.4 and §C.14 (PC-051) and for §C.8 and §C.9 (PC-054). This is the third ./grid raise, which trips D-031's re-split clause.
- **Ruling.**
  1. **The 42 KB ceiling stands for PC-048.** It is the spec's ceiling of record, so it is not a raise beyond the spec. Review 1 must check the measurement and the per-feature attribution, and look for accidental bloat.
  2. **No further ./grid raise without a re-split or real savings.** Fix item PC-F21 measures where ./grid's bytes go (source-map explorer or similar) and does one of two things: moves features to on-demand or secondary entry points, or takes honest savings, until §C.4, §C.8, §C.9 and §C.14 fit inside 42 KB. Spec §C.11 itself names loading the editable-only machinery on demand as the obvious cut. PC-051 and PC-054 do not start until PC-F21 is done.
  3. **The @tellma/core-ui primary entry goes from 8.5 KB to 9 KB** under D-031: 8.48 KB → 8.61 KB, all new spec'd strings.
  4. **Mod+Enter in TM_GRID_KEYS** (§C.1/§C.12) conflicts with spec 0027 §4.3, whose registry refuses modifier entries. The 0027 checkpoint rules on it; WATCHLIST ## 0027 has the lead.
- **After PC-048's fix round (integrated 9007c81):**
  - ./grid is exactly 42.00 KB (43,008 B), with no headroom.
  - The core-ui primary "." measures 8.61 KB, with its ceiling at 8.71. PC-023 re-measures it after its own merge.
  - R1-5 and R1-6 were pushed back to Ahmad. The row-activation hint stays on the scroller: moving it to the tab-stop cell would make NVDA and JAWS repeat it on every arrow key. The emphasis tint stays equal to the zebra stripe, per §C.6. It is a candidate to land with PC-F21's on-demand editing.
  - The api:check golden's union order for `rowActivation` differs between a single-project build and the full build. CI uses the full-build order.
- **Misalignments for Ahmad:**
  - Spec 0010 §C.1: the row-activation hint is announced only from the scroller.
  - Spec 0010 §C.6: the emphasis background is the same colour as the zebra stripe.
  - Spec 0010 §C.11 budgets ./grid at 42 KB for §C.1–§C.14, but §C.1–§C.3 and §C.5–§C.7 alone fill it.
  - Mod+Enter appears in both spec 0010 §C.1/§C.12 and spec 0027 §4.3's refusal rule.

### D-056 (orchestrator, 2026-11-12): PC-028 fix-round-2 deviations accepted
- **Out-of-band errors.** In an Auto batch that declares a write, a server error numbered outside both bands (for example RAISERROR at severity 11 to 18, which XACT_ABORT does not abort) is a Defect. With inserted roots the commit probe decides: Committed surfaces as ambiguous, NotCommitted as the translated error. Without inserted roots it surfaces as Ambiguous(Unknown). PC-040 must also refuse RAISERROR at severity 11 or above in batch text (WATCHLIST ## 0012).
- **Probe re-runs.** The commit probe re-runs its own reported and lost-connection failures within the batch's delays and RetryAttempts, and re-runs a probe timeout at most once. Pool timeouts (InvalidOperationException) stay non-transient.
- **No verdict.** Once an idempotent write has been re-run after a lost connection with no verdict, every exit surfaces Ambiguous(Unknown).
- **Explicit cancel.** An Explicit batch cancelled before its commit surfaces OperationCanceledException. A cancelled sent non-Explicit write stays Ambiguous(Unknown). The spec 0020 worker maps any exception after its stopping token fires to Released, keying on the token (WATCHLIST).
- **Unsent failures.** A non-SQL throw from ExecuteReaderAsync while the connection is still open (for example a TVP SetValue failure) is Unsent: no probe, no re-run.

### D-055 (orchestrator, 2026-11-12): PC-B01 batch review rulings
- **Consent granularity.** Remembered consent is per exact resource, which means per tenant. Spec 0010 §A.3/§A.4 is silent on this. Per-tenant is the narrower grant, so it stands until Ahmad rules otherwise. Per-distribution consent would let one approval cover tenants the user never saw.
- **CLI audience.** The CLI no longer gets the tellma_identity audience. The management API is machine-to-machine only (tellma_kind=service), per spec 0003 §11.4.
- **Moved origin.** After a distribution's origin moves, refresh tokens issued before the move keep the old audience until they lapse or are revoked. This is accepted and documented in the identity README. Revocation on move is a candidate follow-up, not MVP.
- **ARCHITECTURE.md.** The PC-B01 fixer updated ARCHITECTURE.md's identity amendments: the machine-only management API, consent bound to places, and moved-origin retirement.
- **Misalignments for Ahmad:**
  - Spec 0003 §4 row 2a lists "server management" as a CLI audience, which conflicts with §11.4. The row needs correcting.
  - Spec 0010 §A.3/§A.4 should say whether consent is per tenant or per distribution.

### D-054 (orchestrator, 2026-11-12): PC-071 interim decisions, for Review 1 to test
- Provisionally accepted:
  - **Public `challenge` on TmApiError.** TmApiError gains a public `challenge` member (the parsed step-up challenge), which spec 0024 §10.1 does not list. The implementer needs it so the PC-069 → PC-071 hand-off can pass the challenge on a 401.
  - **Internal seams.** TM_API_LOCALE, TM_API_CACHE and TM_API_UPDATES stand in for PC-104, PC-096 and PC-110. TmAppVersion (PC-113) is not created. A WATCHLIST line names each owner.
  - **Blob fetcher.** provideTellma provides TM_BLOB_FETCHER over TmHttpApi in place of the HttpClient default, plus an internal TM_METADATA_LOADERS token.
- **Size budget, open.** The @tellma/core primary entry's gzip budget rises from 6.5 KB to 12.5 KB (measured 6.34 → 12.27 KB). D-031 counts this as a raise. Review 1 must judge whether the growth is inherent or comes from avoidable imports. If it is avoidable, the ceiling falls back.
- After fix 1 (9af3158), Review 1 confirmed the raise is inherent: the primary entry stands at 12.42 of 12.5 KB, and about 1.6 KB of spec 0024's 14 KB own-services share remains (WATCHLIST ## 0024).
- **Also provisionally accepted after fix 1:**
  - **fetchBlob:** a public `TmApi.fetchBlob` with `TmFetchBlobOptions`, so that TM_BLOB_FETCHER goes through TmApi and TmFakeApi can answer it.
  - **Sign-out:** the sign-out answer skips the response user check.
  - **Stale-session 401:** a 401 from a replaced session rejects `cancelled`.
  - **Older-build verdicts:** a 30 s window for older-build verdicts.
  - **Lazy import:** the core-ui TM_BLOB_FETCHER default imports `@angular/common/http` lazily.
- **After fix 2 (8d54380, integrated 279ef75), also accepted:**
  - **Same-origin paths only:** TmApi admits only paths on its own origin: one leading `/`, no protocol-relative or backslash forms, no control characters, and no `.`, `..` or `%2e` segments. Anything else is refused as `invalid-url` before sending; it throws in dev and leaves connectivity untouched.
  - **Blob fetch options:** fetchBlob takes PC-059's `TmBlobFetchOptions`, and `TmFetchBlobOptions` is gone.
  - **Testing package:** `@tellma/core-testing` now peers `@tellma/core-ui`, so `provideTellmaTesting` can route TM_BLOB_FETCHER to TmFakeApi.
  - **Size:** no raise was needed. The primary entry stands at 12.47 of 12.5 KB; the 0.03 KB of headroom left is a WATCHLIST risk for PC-104/110.
- **Misalignment for Ahmad:** spec 0024 §10.1's TmApi/TmApiError surface omits the challenge and fetchBlob.

### D-053 (orchestrator, 2026-11-12): PC-059 ceilings for ./image and ./alert
- Ruling: ./image takes 11 KB gzip (measured 10.69 KB; the six spec'd Lucide glyphs alone cost 0.64 KB on a 9.95 KB start) and ./alert takes 2.25 KB (measured 2.14 KB; the implementer proposed 2.5). Each is measured + ≈0.1 KB as in D-049; D-031 counts both as raises.
- Also accepted, for Review 1 to test: a date-picker live direction-flip fix beyond spec 0010 §D.11's text, since the CDK stamps the pane's dir only at attach; a throwing keep() goes to ErrorHandler and the name is deleted, since the spec says only "never throws"; boot-expiry IndexedDB handlers simplified to keep the boot script within D-048's 3,072 B after the §D.5 versionchange handler.
- **Misalignment for Ahmad:** CHECKPOINTS PC-059's Done-when said ./image ≤ 10 KB.

### D-052 (orchestrator, 2026-11-12): PC-028 retry and commit-probe rulings
- Ruling: known transient error numbers (e.g. Azure throttling 40501, 10928, 10929) classify as transient before the severity-20 lost-connection rule.
- Ruling: after a NotCommitted probe verdict, any final failure of the re-run triggers a second probe before an error reaches the caller. Spec 0012 §6.5 names only 2627 as proof of commit; but a first attempt that commits after the probe makes the re-run fail on its version-tag guard (50412, which would reconnect and write twice) or on a conflict (50409, a false conflict).
- Ruling: the commit probe's key lookup is seek-only (FORCESEEK), so it cannot scan and block under READCOMMITTEDLOCK on large import chunks.
- Ruling: a Read or Validate batch that hits the command timeout (-2) is re-run at most once, not three times as spec 0012 §6.4 says. Four 30 s timeouts would make the caller wait about 2 minutes and quadruple the load on a slow server. Other transient errors keep the spec's count.
- Also: retry delays and reopens honour the request token (§6.1), but an unknown writing outcome is reported as ambiguous, never as a failure. Declaring IVersionTagRegistry ahead of PC-038 is accepted; PC-038 reuses PersistChecks' guard text.
- **Misalignment for Ahmad:** spec 0012 §6.4 (timeout re-run count; classification order) and §6.5 (proof of commit; probe hint); recorded under LEDGER.md merge-time tasks.

### D-051 (orchestrator, 2026-11-11): a batch review may borrow an idle fix slot
- Situation: Phase 1 closed (PC-012 green on CI 43103329956) with all four checkpoint slots in use and the fix slot pc-f idle.
- Ruling: PC-B01 runs in pc-f rather than waiting for a checkpoint slot. A stop-the-line fix arriving meanwhile takes pc-f2 when free, else the next slot to free. The batch finders only read, so D-028's cap counts only the batch fixer.
- Revisit if: a red CI finds both fix slots busy.

### D-050 (orchestrator, 2026-11-11): PC-012 'uq' lookups: end check outside the seek, RECOMPILE on hoisted variables
- Ruling: a lookup by date checks the month's EndDate on the one row the `TOP (1) … ORDER BY StartDate DESC` seek finds, outside the seek (deviation 1). Answers are identical for non-overlapping months. Spec 0010's form scanned every month backward for a date past the table's end: 35 s vs 139 ms for 8,400 such rows, and equal in range.
- Ruling (flag 4): a statement that declares a hoisted 'uq' calendar variable carries `OPTION (RECOMPILE)`. The benchmark (10M lines over 1M headers) measured the hoisted variable at 18–26× a bound parameter for month selectivity; RECOMPILE brings it to 1.7–2.0×. The host-evaluated-parameter alternative (the host resolves the call against a cached month table and binds a parameter, ≈1.0×) is a design change left for Ahmad.
- Amended after Review 1: OPTION cannot sit in Body (Body is embedded as a derived table or under IF: Msg 156) and a statement takes one OPTION clause. So RECOMPILE is a structural `Recompile` flag on CompiledQuery; the standalone Sql appends ` OPTION (RECOMPILE)` after Body. It is set only when a surviving 'uq' calendar variable is read from a predicate (WHERE, ON, HAVING), never for hierarchy-only variables or constant statements. This changes the spec 0010 §F.3 Sql = Prologue + Body invariant and adds a public member. Spec 0012's composers that embed Body merge the hint into their own single OPTION clause (WATCHLIST ## 0012).
- Confirmed after the fix round: the standalone Sql writes the hint as its own clause line (Body ends in a line feed): Prologue + Body + "OPTION (RECOMPILE)
" + ";
". The flag is read from WHERE (semi-join predicates included) or HAVING; ON had no hoisted-variable case.
- Also accepted: TypedEquivalence and the call hash compare selector arguments by resolved value, ignoring case ('UQ' = 'uq'), a fix to pre-existing behaviour; no golden changed.
- **Misalignment for Ahmad:** spec 0010 §F examples 1 and 2 (lookup text) and the hoisting design's plan quality; recorded under LEDGER.md merge-time tasks.
- Revisit if: compile cost of RECOMPILE shows up in report latency, or Ahmad picks the host-parameter design.

### D-049 (orchestrator, 2026-11-11): PC-020 indicator budgets at birth
- Ruling: the seven spec 0023 indicator entry points take ceilings of measured size + ≈0.1 KB (rounded up to 0.05) after the savings Reviewer 1 found, in KB gzip: avatar 2.63 (2.75), badge 2.08 (2.2), copy 3.20 (3.3), empty-state 1.30 (1.4), progress 2.56 (2.7), relative-time 1.21 (1.35), skeleton 1.53 (1.65). Each budgetNote states the measured size and savings taken.
- Why: savings taken were data-tone/data-shape/data-size attribute bindings instead of per-value classes (badge, counter, progress bar, skeleton, empty-state); a single copy-button template measured heavier (3.25) than two templates and was not taken. The spec's §1 figures (8 KB set total) predate the components.
- Also: the warning progress fill uses color-mix(in srgb, var(--warning) 75%, var(--text-strong)), 3.77:1 against the light track (WCAG 1.4.11), since no darker warning role exists.
- **Misalignment for Ahmad:** spec 0023 §1 budgets; recorded under LEDGER.md merge-time tasks (with the `textStyle` input name).
- Revisit if: a later checkpoint raises any of these ceilings again (D-031 counts this as the first raise).

### D-048 (orchestrator, 2026-11-11): PC-069 boot budget per language and interim expiry
- Ruling (reworded after Review 2): spec 0024 §4.2's 3 KB gzip covers the boot code plus the always-embedded English fallback; each further string table adds 0.4 KB gzip (en+ar or ar-only ≤ 3.4 KB, en+ar+am ≤ 3.8 KB). The boot-template test asserts English-only and Acme's real en+ar configuration, computing the allowance from the language count.
- Ruling: until the session checkpoints land, TmSession's interim `expired()` reloads (or routes to the boot page) after setting state, so an expired tab never keeps showing the user's data. Writing `sub` into the boot record and the §8.8 re-check triggers stay with their owners (WATCHLIST ## 0024).
- Why: measured template 2,863 B, en 3,065 B, en+ar 3,389 B, en+ar+am 3,712 B (Reviewer 1); the old check watched only a configuration that never ships. An expired tab would otherwise be a dead end that still shows data.
- **Misalignment for Ahmad:** spec 0024 §4.2 should state what 3 KB covers and the per-language allowance; recorded under LEDGER.md merge-time tasks.
- Revisit if: a distribution offers four or more languages, or the boot strings grow.

### D-047 (orchestrator, 2026-11-11): PC-014 sign-in language and pending authorization requests
- Ruling: a language switch on the sign-in page rewrites ui_locales nested in identity-page return addresses (at most three deep), but never a pending authorization request; only the consent page's own switch rewrites the request's ui_locales.
- Why: spec 0010 §A.10 has the authorization endpoint re-apply the language from the request, and protocol messages are never edited in passing. Reviewer 2 (Opus xhigh, 0/0/0) traced the redirect surface and upheld it.
- Residual: a returning user with an English cookie in a PAR flow with explicit consent sees sign-in in Arabic and consent in English; rare, since first-party PAR clients have implicit consent.
- **Misalignment for Ahmad:** spec 0010 §A.10 says the return re-applies the request's language, but deviation 5 lets the language cookie outrank a pushed request's ui_locales on consent. Bring the wording in line; recorded under LEDGER.md merge-time tasks.
- Revisit if: Ahmad wants the sign-in-page switch to win over a pending authorization request too.

### D-046 (orchestrator, 2026-11-11): PC-019 language rulings
- Ruling: (1) the satellite rule of spec 0013 §1.3 stays a startup failure: an offered non-English language without a Tellma.Core satellite stops the host (Reviewer 1 agreed: §7.2's fallback covers missing keys, not a missing Core language); the remedy text names adding the satellite. (2) The negotiated culture is named <language>-<region> and carries pinned Latin-digit Gregorian formats field by field, not a -u-nu-latn clone (§9.2 contradicts itself; a clone would read the host's ICU, against D-029). (3) Culture data is pinned from CLDR 48 via ICU 78.2 with two corrections (om/ti day-first numeric dates; '%' for ar-SA/ar-SO), generated by eng/culture-data; the CI Ubuntu leg turns the reproducibility skip into a failure. (4) ILanguageCatalog.All is an accepted additive contract change. (5) The om/ti day-first rule is routed to the client's 0024 §7.7 fallback and the enum-clash check's wider scope to the 0029 emit (WATCHLIST.md).
- Why: Reviewer 1 verified the readings and the 51 changed calendar goldens; the corrections keep server and client date orders aligned.
- Misalignment: spec 0013 §1.3 vs AddLanguage, and §9.2's culture naming; recorded under LEDGER.md merge-time tasks for Ahmad.
- Revisit if: Ahmad wants AddLanguage languages offerable before their Core satellite exists.

### D-045 (orchestrator, 2026-11-11): LEDGER status cells stay bare
- Ruling: the Status column of LEDGER.md's status table holds the bare state word (`todo`, `impl`, …, `done`); CI runs, red-run histories and fix-item links go in Notes. check_deps.py compares the cell exactly, so `done (green at …)` read as not done.
- Why: seven done checkpoints (PC-007, PC-009, PC-016, PC-021, PC-024, PC-060, PC-065) carried annotations, which hid PC-010, PC-014, PC-019, PC-025 and PC-067 from the ready list, possibly since 2026-11-09. Normalized 2026-11-11 05:50.
- Revisit if: the tool is changed to parse the leading word instead.

### D-044 (orchestrator, 2026-11-11): PC-041 review-1 follow-ups
- Ruling: a menu keeps one Escape entry that collapses an expanded submenu (and refocuses its parent) or else closes the menu, in place of spec 0010 §C.10's per-level entries (deviation 17; one Escape still closes one level, and the stack order against tooltips and modals is unchanged). After a grid right-click outside the selection, the grid no longer takes focus back from the menu's first row; an outside press on nothing focusable then leaves focus on the page, as for every other restoreFocus:false invoker. Whether screen readers read a rich menu's header on open is left to a manual NVDA and VoiceOver pass.
- Why: the single entry fixes the ArrowRight-then-Escape race without changing what users see; the focus change is what Reviewer 1 asked for; an agent cannot verify screen-reader speech.
- Misalignment: spec 0010 §C.10's Escape sentence needs rewording; recorded under LEDGER.md merge-time tasks with the manual pass.
- Revisit if: the manual pass shows the header is not announced.

### D-043 (orchestrator, 2026-11-11): PC-F08 review-2 follow-ups
- Ruling: (1) a Linux host removes its runtime's debugger pipes before every worker start, in every environment, Development included (a managed debugger cannot attach to a Linux host after the first PDF thumbnail; debug on Windows or with PDF thumbnails off). (2) The window in which a starting worker is still dumpable on an unprivileged host is accepted until PC-F11. (3) PC-F11 also denies a worker /proc beyond its own entries and its temporary directory. (4) Other processes under the host's account remain reachable from a worker, and a worker of a root host can raise the host's oom_score_adj and reach root's files; the hosting guidance is: an unprivileged account with nothing else under it, run as a service and not from a terminal, CAP_SYS_PTRACE only where crash dumps are wanted. (5) The real diagnostics tools were not run; a staging run is a merge-time task.
- Why: each residual needs either extra privileges or PC-F11's confinement to close, and none is reachable without first compromising PDFium; the cheap closures (non-dumpable host and worker, signal and terminal syscalls refused, setsid) are in.
- Misalignment: spec 0017 records none of this; LEDGER.md merge-time tasks list it for Ahmad.
- Revisit if: developers need a debugger on a Linux host with PDF thumbnails on, or a deployment runs other services under the host's account.

### D-042 (orchestrator, 2026-11-11): PC-041 menu budget and review-1 routing
- Ruling: the ./menu size ceiling is 8.5 KB, not spec 0010 §C.11's 7 (measured 8.21; Reviewer 1 confirmed nothing short of dropping a spec'd entry kind reaches 7: external glyphs 7.96, no dev diagnostics 8.10); the budget note must list the savings taken (D-031). The group-divider rule (above a group whenever a non-separator entry precedes it) replaces §C.10's "every group but the first". The pre-existing right-click close-on-release bug (R1-2, Firefox/WebKit) is fixed in PC-041's fix round rather than a new fix item, because it lives in the same files. A @cross-engine subset of the menu e2e is added.
- Why: the raise fits D-031's arithmetic and the reviewer found nothing droppable; routing R1-2 to the owner of the files avoids a parallel edit.
- Misalignment: spec 0010 §C.11 and §C.10 need bringing in line; recorded under LEDGER.md merge-time tasks.
- Revisit if: a later menu feature pushes ./menu past 8.5.

### D-041 (orchestrator, 2026-11-11): PC-F08 host-memory vector is not a residual
- Ruling: PC-F08 Review 2 (R2-1) found that on Linux a compromised PDF worker can reopen the host's W^X doublemapper memfd through /proc/<host>/fd for writing and read /proc/<host>/environ; kernel.yama.ptrace_scope does not gate either path, so D-030 addendum 5's warning does not close the vector. It is fixed in PC-F08 fix2, not accepted: by default prctl(PR_SET_DUMPABLE, 0) in the host on Linux whenever PDF thumbnails are enabled, verified in the sdk:10.0 container (including a uid-0 host), with probes that assert both paths are refused. The ptrace_scope guidance is corrected. Losing core dumps of the host is accepted.
- Why: host code execution and secret reads from a PDF are the exact threat PC-F08 exists for; the fix is cheap and needs no extra privileges, unlike a distinct worker uid or a PID namespace.
- Revisit if: verification shows non-dumpable does not close both paths, or it breaks a diagnostics path operators rely on beyond core dumps.

### D-040 (orchestrator, 2026-11-11): PC-F08 review-1 residuals
- Ruling: (1) the RLIMIT_AS residual (a compromised worker can trade the runtime's reservations for about 3.55 GiB shared memory at the default limit) is accepted for now, with a memory cgroup as the deployment guidance; Reviewer 2 may escalate it. (2) The filesystem residual is wider than D-030 addendum 3 (in-place overwrite, truncate, rename, delete of files the host account may write); PC-F11 now covers writes as well as reads. (3) macOS is treated like Windows (PDF thumbnails only in Development), the conservative reading of addendum 5. (4) musl hosts are detected and get no PDF thumbnails; not exercised on a real musl host, accepted. (5) DOTNET_GCRegionRange stays unset (it crashed the collector at twice the limit).
- Why: each choice keeps the worker's isolation at least as strict as D-030 asks, and none blocks Tier 1; the stricter alternatives (a re-exec under a soft RLIMIT_AS, a cgroup the host manages) are unmeasured.
- Misalignment: spec 0017 lacks Tellma:Blobs:PdfWorkerConcurrency, the host rule and two log events; recorded under LEDGER.md merge-time tasks for Ahmad to bring the spec in line.
- Revisit if: Reviewer 2 rates the RLIMIT_AS residual blocking, or a deployment target runs on musl or macOS in production.

### D-039 (Ahmad, 2026-11-10): Fable phase and the Fable-only reset
- Ruling: Anthropic released Fable 5.2 and granted one manual Fable-only reset in the window starting Tue Nov 10. Until no new Fable agent fits, every newly spawned agent runs on `fable` at its role's effort, gated on the Fable meter as well as the all-models gates. Ahmad then presses the Fable-only reset, and the run reverts to the normal model lists. Rules: PROCESS.md §7.4.
- Why: the Fable meter is a separate half-budget that the normal process leaves mostly unused (Fable was 31% when the last window closed at 100% all-models), and the reset makes it spendable twice in one week.
- Assumptions to verify: the Fable meter's 100% equals 50 all-models points (checked from ΔFable% ≈ 2 × ΔU in the first hours), and the reset also removes the Fable spend from the all-models meter. If U does not drop at the reset, record it here.
- Applied at the resume of 2026-11-10 20:10 UTC, after the scheduled weekly rollover (all 0%, Fable 0%, resets 2026-11-17 20:00). The one-time full reset (§7.2) is unchanged and still pending, deadline Sat Nov 14 20:00 UTC; a one-shot cron at Sat Nov 14 12:00 UTC reminds Ahmad.
- Revisit if: the `fable` alias does not resolve to Fable 5.2, or the measured Fable cost factor differs a lot from 1.8.

### D-006 (Ahmad, 2026-11-08): review levers
- Ruling: Review 2 runs only for the PROCESS.md §3 two-review list (security-bound or complex, 44 checkpoints) and for escalations (§4.4); every other checkpoint gets Review 1 and one fix round. Cross-checkpoint batch reviews run once per phase instead of per group.
- Why: Ahmad traded some review depth for about 0.7 weeks of Tier-1 budget (PLAN.md §3.3). The escalation rule keeps a second review wherever Review 1 shows trouble.

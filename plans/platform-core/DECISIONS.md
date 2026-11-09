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

### D-006 (Ahmad, 2026-11-08): review levers
- Ruling: Review 2 runs only for the PROCESS.md §3 two-review list (security-bound or complex, 44 checkpoints) and for escalations (§4.4); every other checkpoint gets Review 1 and one fix round. Cross-checkpoint batch reviews run once per phase instead of per group.
- Why: Ahmad traded some review depth for about 0.7 weeks of Tier-1 budget (PLAN.md §3.3). The escalation rule keeps a second review wherever Review 1 shows trouble.

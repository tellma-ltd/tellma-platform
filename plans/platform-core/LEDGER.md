# Platform-core ledger

Live state of the run. Only the orchestrator edits this file, and only in the main checkout (PROCESS.md §2).
It is committed by the snapshot procedure. Status values: `todo`, `impl`, `r1`, `fix1`, `r2`, `fix2`,
`integrated` (pushed, CI pending), `done` (CI green for its sha), `blocked`, `merged-into:PC-nnn`.

## Run

| Item | Value |
|---|---|
| Started | 2026-11-08 05:03 UTC |
| Orchestrator session id | 55d8a508-95e7-4eec-9239-0e94c12f0353 |
| PR | #18 (platform-core → main, open, non-draft; CI green on 148c074) |
| Slots | pc-1, pc-2, pc-3, pc-4 (WT/pc-N; pc-4 added 05:30 for the 4th cycle while backend and client work are both ready, ports 43N0/71N0/42N0, DB_SUFFIX pcN) |
| Parked | no |
| Parked at U | |
| Agent tool supports name / effort / fable | name: **no** (address agents by id via SendMessage) / effort: yes / fable: yes (model enum) |
| Environment check | git clean on platform-core, no prompt on `cd MAIN && …`; gh logged in (repo, workflow scopes); Docker 29.6.2; .NET SDK 10.0.401 (+9.0.314); Node 24.18.0; pnpm 11.9.0; check_deps 218 checkpoints, 0 problems |
| Notification test | email "Platform-core: run starting" delivered (Gmail id 1a1b98a787d056c9); PushNotification not sent (terminal active, tool skips while Ahmad is at the machine; it reaches the phone when he is away) |

## Calibration

Cost units are price-weighted (tools/usage_window.py). ρ = weekly points per M cost units.

| Updated | ρ | k_backend | k_client | Forecast T1 / T2 / T3 (weeks, incl. batch reviews and orchestrator) |
|---|---|---|---|---|
| 2026-11-08 (prior, levers applied) | 0.115 | 0.36 | 0.80 | 4.0 / 0.8 / 1.5 |

## Usage

| When (UTC) | Weekly all % | Weekly Fable % | Resets | Note |
|---|---|---|---|---|
| 2026-11-08 02:20 | 40 | 22 | 2026-11-10 20:00 | after planning |
| 2026-11-08 05:03 | 42 | 23 | 2026-11-10 20:00 | kickoff; window start for ρ (orchestrator units 0.21M) |
| 2026-11-08 05:58 | 42 | 23 | 2026-11-10 20:00 | PC-000 impl done; 4 cycles in flight |
| 2026-11-08 06:20 | 43 | 23 | 2026-11-10 20:00 | PC-000 fix1 done, integrating; no escalation (R1 0/1/2) |
| 2026-11-08 07:15 | 45 | 23 | 2026-11-10 20:00 | PC-003 impl done; PC-F01 fixing |
| 2026-11-08 08:05 | 47 | 23 | 2026-11-10 20:00 | PC-003 → r2; line stopped for PC-F02 |
| 2026-11-08 09:10 | 50 | 26 | 2026-11-10 20:00 | PC-001/003 held; PC-002 r2 (Fable); PC-004 fix1; PC-F02 impl |
| 2026-11-08 12:40 | 51 | 26 | 2026-11-10 20:00 | PC-F02 done (CI 42471952389 green on 4c89ae1); PC-001 + PC-004 integrating; PC-002/003 held |
| 2026-11-08 12:55 | 52 | 26 | 2026-11-10 20:00 | PC-001 integrated 354f93d; PC-002 + PC-004 integrating; PC-008 launched (p≈3.6); PC-F03 impl |
| 2026-11-08 13:30 | 54 | 26 | 2026-11-10 20:00 | PC-004 done; PC-002 integrated e971e64; PC-003 integrating; PC-015 + PC-017 launched |

## In flight

| PC | Slot | Branch | Step | Implementer agent (name / id) | Reviewer agent (name / id) | Started (UTC) |
|---|---|---|---|---|---|---|
| PC-015 | pc-1 | pc/PC-015 | impl | impl-PC-015 / a77753b07b64fdd2e (xhigh) | | 2026-11-08 13:10 |
| PC-008 | pc-2 | pc/PC-008 | impl | impl-PC-008 / a6567bfbe1a75233d (high) | | 2026-11-08 12:55 |
| PC-017 | pc-3 | pc/PC-017 | impl | impl-PC-017 / a6415c68954c2a576 (high) | | 2026-11-08 13:30 |

## Fix items

| Fix | For | Status | Commits | Cost units (M) | Notes |
|---|---|---|---|---|---|
| PC-F01 | PC-000 (CI red on 8a1ebf8) | done | 6ef6423, d1ab25e (pc) | 2.49 (impl 2.18, r1 0.31) | quarantined 5 flaky e2e; nightly job client-e2e-quarantine (non-blocking, per-test table); CI 42455962373 green; nightly 42456014559 green |
| PC-F03 | entity-picker same-frame Enter race: Enter in the same frame as the last keystroke skips the highlight (reproduced, data-changing; frozen 0006 component) | fix1 (R1 1/0/2), pc-f, fix-PC-F03 / a62d83dfcb72a8e4f (high); r1 aa9a0cc9ac7a293cb | af35d27 | | arrow-key burst split out to PC-F04; from PC-F02 R1-2; brief: reviews/PC-F02.md R1-2 |
| PC-F04 | entity-picker arrow keys in a burst: type + ArrowDown + Enter in one task picks row 1 where paced input picks row 2 (aria relays only the last arrow, after render) | todo (low; filler) | | | from PC-F03 R1 decisions; brief: reviews/PC-F03.md implementer notes "open risk" |
| PC-F02 | systemic e2e flakiness: CI 42456030333 red on 5250c6c (plans-only) with 4 NEW flaky tests | done (R1 1/1/2; CI 42471952389 green), pc-f free | 2c91df4, 4c36d45, 4c89ae1 | 11.32M | grid bundle at its 38 KB ceiling; picker same-frame Enter race open | root-cause all 9; un-quarantine the fixed PC-F01 five |

## Batch reviews (one per phase)

| Batch | Phase | Status | Commit | Findings |
|---|---|---|---|---|

## Merge-time tasks (before platform-core merges into main)

| Task | Source |
|---|---|
| nightly-tier.yml: restore the schedule and a main guard, replacing the trampoline | PC-000, D-012 |
| CONTRIBUTING.md: drop the platform-core-only sentences; .gitattributes: drop the plans/** line; delete plans/ | PC-000, D-012, PLAN.md header |
| ARCHITECTURE.md CI/CD: client job is Ubuntu-only (spec 0024 target) — confirm wording | PC-000 impl note |

## Flaky

| Test | Since | Tracking |
|---|---|---|
| e2e entity-picker.spec.ts:584 modal round-trips › create page prefills… | 2026-11-08 (hidden by retries:2 before) | fixed by PC-F02 (4c36d45); un-quarantined |
| e2e entity-picker.spec.ts:607 modal round-trips › edit page targets committed id… | 2026-11-08 | fixed by PC-F02; un-quarantined |
| e2e grid-entity.spec.ts:132 unresolved commit text resolves… after editor teardown | 2026-11-08 | fixed by PC-F02; un-quarantined |
| e2e grid-keyboard.spec.ts:241 readonly grid is a single tab stop | 2026-11-08 | fixed by PC-F02; un-quarantined |
| e2e popover.spec.ts:93 block-end/start alignment mirrors under RTL | 2026-11-08 | fixed by PC-F02; un-quarantined |
| dotnet Identity integration Flows.AssurancePolicyTests.Max_age_zero_forces_one_reauthentication_then_completes: 200 not 302 at 2nd email-code verify, once in a full run (4/4 alone; local, pre-existing) | 2026-11-08 (PC-002 r2) | watch; PC-Fnn candidate if it recurs (identity suite, Tier 1) |
| dotnet Smtp connector integration: port-in-use (local only, passed 3/3 on rerun) | 2026-11-08 (PC-002 fix1) | watch; quarantine if it recurs on CI |
| e2e entity-picker.spec.ts:83 sync source keystroke-instant / async spinner | 2026-11-08 (CI 42456030333) | fixed by PC-F02 |
| e2e grid-entity.spec.ts:58 type-to-edit … Enter commits with NO move | 2026-11-08 | fixed by PC-F02 |
| e2e grid-keyboard.spec.ts:258 single tab stop with link column | 2026-11-08 | fixed by PC-F02 |
| e2e modal.spec.ts:135 focus trapped and restored to opener | 2026-11-08 | fixed by PC-F02 |

## Deferred tests

| Test | Spec | Reason |
|---|---|---|

## Status

| PC | Analysis id | Tier | Status | Commits | Cost units (M) | Notes |
|---|---|---|---|---|---|---|
| PC-000 | SETUP | 1 | done | 61a1eba, ad7a24b, 8a1ebf8 (pc) | 2.80 (impl 2.25, r1 0.55) | nightly 42452020325 green; CI 42451990728 red (5 flaky) → PC-F01; done with PC-F01 at d1ab25e (CI 42455962373 green) |
| PC-001 | 0011-a | 1 | done | 13d1717, 6ffd1dc, 354f93d (pc) | 13.40M | CI 42482202715 green |
| PC-002 | 0010-a1 | 1 | done | 24e9a35, cd7ca36, 3a77d35, e971e64 (pc) | 27.79M | blocks=1 in fix1: agent tried amend + force-with-lease (denied; Ahmad notified 08:50). Review responses cite 88fa52a = cd7ca36; identity migration regenerated as 20261108125718_Initial; CI 42486546065 green |
| PC-003 | 0010-f1 | 1 | integrated | bfc40a1, b780419, de5c585, 68ddcc0 (pc) | 24.89M | Queryex fixture on DatabaseCollations.Platform (D-015 done) |
| PC-004 | 0023-a | 1 | done | cd25a67, d532bfa, 681f7f8 (pc) | 14.67M | CI 42484063555 green |
| PC-005 | 0010-g1 | 1 | todo | | | |
| PC-006 | 0010-a2 | 1 | todo | | | |
| PC-007 | 0010-f2 | 1 | todo | | | |
| PC-008 | 0010-b1 | 1 | impl | | | |
| PC-009 | 0010-a3 | 1 | todo | | | |
| PC-010 | 0010-f3 | 1 | todo | | | |
| PC-011 | 0010-d1 | 1 | todo | | | |
| PC-012 | 0010-f5 | 1 | todo | | | |
| PC-013 | 0010-d2 | 1 | todo | | | |
| PC-014 | 0010-a5 | 1 | todo | | | |
| PC-015 | SEED | 1 | impl | | | |
| PC-016 | 0013-a | 1 | todo | | | |
| PC-017 | 0017-a | 1 | impl | | | |
| PC-018 | 0012-a | 1 | todo | | | |
| PC-019 | 0013-b | 1 | todo | | | |
| PC-020 | 0023-b | 1 | todo | | | |
| PC-021 | 0012-b | 1 | todo | | | |
| PC-022 | 0013-c | 1 | todo | | | |
| PC-023 | 0023-c | 1 | todo | | | |
| PC-024 | 0011-b | 1 | todo | | | |
| PC-025 | 0011-c | 1 | todo | | | |
| PC-026 | 0023-d | 1 | todo | | | |
| PC-027 | 0012-c1 | 1 | todo | | | |
| PC-028 | 0012-c2 | 1 | todo | | | |
| PC-029 | 0014-a | 1 | todo | | | |
| PC-030 | 0023-e | 1 | todo | | | |
| PC-031 | 0023-f | 1 | todo | | | |
| PC-032 | 0011-e | 1 | todo | | | |
| PC-033 | 0012-d | 1 | todo | | | |
| PC-034 | 0011-d | 1 | todo | | | |
| PC-035 | 0021-a | 1 | todo | | | |
| PC-036 | 0020-a | 1 | todo | | | |
| PC-037 | 0012-e | 1 | todo | | | |
| PC-038 | 0013-d | 1 | todo | | | |
| PC-039 | 0014-b | 1 | todo | | | |
| PC-040 | 0012-g | 1 | todo | | | |
| PC-041 | 0010-c1 | 1 | todo | | | |
| PC-042 | 0012-f | 1 | todo | | | |
| PC-043 | 0014-c | 1 | todo | | | |
| PC-044 | 0013-e | 1 | todo | | | |
| PC-045 | 0010-c2 | 1 | todo | | | |
| PC-046 | 0013-g | 1 | todo | | | |
| PC-047 | 0011-f | 1 | todo | | | |
| PC-048 | 0010-c3 | 1 | todo | | | |
| PC-049 | 0013-f | 1 | todo | | | |
| PC-050 | 0014-d | 1 | todo | | | |
| PC-051 | 0010-c4 | 1 | todo | | | |
| PC-052 | 0014-e | 1 | todo | | | |
| PC-053 | 0017-b | 1 | todo | | | |
| PC-054 | 0010-c5 | 1 | todo | | | |
| PC-055 | 0018-a | 1 | todo | | | |
| PC-056 | 0010-c6 | 1 | todo | | | |
| PC-057 | 0010-e1 | 1 | todo | | | |
| PC-058 | 0010-e2 | 1 | todo | | | |
| PC-059 | 0010-d3 | 1 | todo | | | |
| PC-060 | 0010-b2 | 1 | todo | | | |
| PC-061 | 0023-g | 1 | todo | | | |
| PC-062 | 0015-a | 1 | todo | | | |
| PC-063 | 0015-b | 1 | todo | | | |
| PC-064 | 0016-b | 1 | todo | | | |
| PC-065 | 0024-a | 1 | todo | | | |
| PC-066 | 0015-c | 1 | todo | | | |
| PC-067 | 0024-b | 1 | todo | | | |
| PC-068 | 0015-d | 1 | todo | | | |
| PC-069 | 0024-c | 1 | todo | | | |
| PC-070 | 0015-e | 1 | todo | | | |
| PC-071 | 0024-d | 1 | todo | | | |
| PC-072 | 0015-f | 1 | todo | | | |
| PC-073 | 0024-e | 1 | todo | | | |
| PC-074 | 0015-g | 1 | todo | | | |
| PC-075 | 0025-a | 1 | todo | | | |
| PC-076 | 0015-h | 1 | todo | | | |
| PC-077 | 0015-i | 1 | todo | | | |
| PC-078 | 0014-f | 1 | todo | | | |
| PC-079 | 0013-h | 1 | todo | | | |
| PC-080 | 0016-c | 1 | todo | | | |
| PC-081 | 0016-d | 1 | todo | | | |
| PC-082 | 0016-e | 1 | todo | | | |
| PC-083 | 0017-c1 | 1 | todo | | | |
| PC-084 | 0017-c2 | 1 | todo | | | |
| PC-085 | 0011-g | 1 | todo | | | |
| PC-086 | 0029-a | 1 | todo | | | |
| PC-087 | 0018-b | 1 | todo | | | |
| PC-088 | 0018-c | 1 | todo | | | |
| PC-089 | 0017-d | 1 | todo | | | |
| PC-090 | 0018-d | 1 | todo | | | |
| PC-091 | 0018-e | 1 | todo | | | |
| PC-092 | 0018-g | 1 | todo | | | |
| PC-093 | 0018-h | 1 | todo | | | |
| PC-094 | 0024-f | 1 | todo | | | |
| PC-095 | 0016-f | 1 | todo | | | |
| PC-096 | 0025-b | 1 | todo | | | |
| PC-097 | 0020-b | 1 | todo | | | |
| PC-098 | 0020-c | 1 | todo | | | |
| PC-099 | 0021-b | 1 | todo | | | |
| PC-100 | 0020-d | 1 | todo | | | |
| PC-101 | 0010-f4 | 1 | todo | | | |
| PC-102 | 0029-f | 1 | todo | | | |
| PC-103 | 0024-g | 1 | todo | | | |
| PC-104 | 0024-h | 1 | todo | | | |
| PC-105 | 0029-b | 1 | todo | | | |
| PC-106 | 0027-a | 1 | todo | | | |
| PC-107 | 0025-e | 1 | todo | | | |
| PC-108 | 0025-c | 1 | todo | | | |
| PC-109 | 0028-c | 1 | todo | | | |
| PC-110 | 0024-i | 1 | todo | | | |
| PC-111 | 0026-a | 1 | todo | | | |
| PC-112 | 0026-b | 1 | todo | | | |
| PC-113 | 0026-c | 1 | todo | | | |
| PC-114 | 0024-k | 1 | todo | | | |
| PC-115 | 0027-b | 1 | todo | | | |
| PC-116 | 0027-c | 1 | todo | | | |
| PC-117 | 0029-c | 1 | todo | | | |
| PC-118 | 0027-d | 1 | todo | | | |
| PC-119 | 0029-e | 1 | todo | | | |
| PC-120 | 0029-d | 1 | todo | | | |
| PC-121 | 0029-g | 1 | todo | | | |
| PC-122 | 0027-e | 1 | todo | | | |
| PC-123 | 0035-a1 | 1 | todo | | | |
| PC-124 | 0030-a | 1 | todo | | | |
| PC-125 | 0031-a | 1 | todo | | | |
| PC-126 | 0030-b | 1 | todo | | | |
| PC-127 | 0031-b | 1 | todo | | | |
| PC-128 | 0030-c | 1 | todo | | | |
| PC-129 | 0031-c1 | 1 | todo | | | |
| PC-130 | 0031-c2 | 1 | todo | | | |
| PC-131 | 0030-d | 1 | todo | | | |
| PC-132 | 0030-g | 1 | todo | | | |
| PC-133 | 0031-d | 1 | todo | | | |
| PC-134 | 0031-e | 1 | todo | | | |
| PC-135 | 0031-f | 1 | todo | | | |
| PC-136 | 0032-a | 1 | todo | | | |
| PC-137 | 0032-c | 1 | todo | | | |
| PC-138 | 0032-f | 1 | todo | | | |
| PC-139 | 0032-d | 1 | todo | | | |
| PC-140 | MVP | 1 | todo | | | |
| PC-141 | 0011-h | 2 | todo | | | |
| PC-142 | 0015-k | 2 | todo | | | |
| PC-143 | 0018-f | 2 | todo | | | |
| PC-144 | 0020-e | 2 | todo | | | |
| PC-145 | 0020-f | 2 | todo | | | |
| PC-146 | 0021-c | 2 | todo | | | |
| PC-147 | 0021-d | 2 | todo | | | |
| PC-148 | 0022-a | 2 | todo | | | |
| PC-149 | 0022-b | 2 | todo | | | |
| PC-150 | 0026-d | 2 | todo | | | |
| PC-151 | 0023-h | 2 | todo | | | |
| PC-152 | 0024-j | 2 | todo | | | |
| PC-153 | 0025-d | 2 | todo | | | |
| PC-154 | 0026-f | 2 | todo | | | |
| PC-155 | 0028-d | 2 | todo | | | |
| PC-156 | 0028-e | 2 | todo | | | |
| PC-157 | 0028-f | 2 | todo | | | |
| PC-158 | 0030-e | 2 | todo | | | |
| PC-159 | 0030-f1 | 2 | todo | | | |
| PC-160 | 0030-h | 2 | todo | | | |
| PC-161 | 0031-g | 2 | todo | | | |
| PC-162 | 0031-h | 2 | todo | | | |
| PC-163 | 0027-f | 2 | todo | | | |
| PC-164 | 0028-g | 2 | todo | | | |
| PC-165 | 0032-e | 2 | todo | | | |
| PC-166 | 0032-g | 2 | todo | | | |
| PC-167 | 0035-a | 2 | todo | | | |
| PC-168 | 0029-h | 2 | todo | | | |
| PC-169 | 0027-g | 2 | todo | | | |
| PC-170 | 0030-i | 2 | todo | | | |
| PC-171 | 0031-i | 2 | todo | | | |
| PC-172 | 0032-h | 2 | todo | | | |
| PC-173 | 0019-a | 3 | todo | | | |
| PC-174 | 0015-j | 3 | todo | | | |
| PC-175 | 0019-b | 3 | todo | | | |
| PC-176 | 0019-c | 3 | todo | | | |
| PC-177 | 0019-d | 3 | todo | | | |
| PC-178 | 0019-e | 3 | todo | | | |
| PC-179 | 0030-f2 | 3 | todo | | | |
| PC-180 | 0032-b2 | 3 | todo | | | |
| PC-181 | 0010-f6 | 3 | todo | | | |
| PC-182 | 0033-a | 3 | todo | | | |
| PC-183 | 0033-b | 3 | todo | | | |
| PC-184 | 0033-c | 3 | todo | | | |
| PC-185 | 0033-d | 3 | todo | | | |
| PC-186 | 0033-e | 3 | todo | | | |
| PC-187 | 0033-f | 3 | todo | | | |
| PC-188 | 0033-g | 3 | todo | | | |
| PC-189 | 0016-g | 3 | todo | | | |
| PC-190 | 0016-h | 3 | todo | | | |
| PC-191 | 0033-h | 3 | todo | | | |
| PC-192 | 0033-i | 3 | todo | | | |
| PC-193 | 0034-a | 3 | todo | | | |
| PC-194 | 0034-b | 3 | todo | | | |
| PC-195 | 0034-c | 3 | todo | | | |
| PC-196 | 0034-d | 3 | todo | | | |
| PC-197 | 0034-e | 3 | todo | | | |
| PC-198 | 0034-f | 3 | todo | | | |
| PC-199 | 0034-g | 3 | todo | | | |
| PC-200 | 0034-h | 3 | todo | | | |
| PC-201 | 0034-i | 3 | todo | | | |
| PC-202 | 0035-b | 3 | todo | | | |
| PC-203 | 0035-c | 3 | todo | | | |
| PC-204 | 0035-d | 3 | todo | | | |
| PC-205 | 0035-e | 3 | todo | | | |
| PC-206 | 0028-a | 3 | todo | | | |
| PC-207 | 0028-b | 3 | todo | | | |
| PC-208 | 0028-h | 3 | todo | | | |
| PC-209 | 0026-e | 3 | todo | | | |
| PC-210 | 0026-g | 3 | todo | | | |
| PC-211 | 0026-h | 3 | todo | | | |
| PC-212 | 0017-e | 3 | todo | | | |
| PC-213 | 0020-g | 3 | todo | | | |
| PC-214 | 0022-c | 3 | todo | | | |
| PC-215 | 0032-b1 | 3 | todo | | | |
| PC-216 | 0025-f | 3 | todo | | | |
| PC-217 | 0010-a4 | 3 | todo | | | |

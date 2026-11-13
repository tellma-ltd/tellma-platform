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
| Fable phase | ended 2026-11-11 06:50 UTC (no new Fable checkpoint fits after PC-025; Ahmad emailed/pushed 06:50; remind once at U 85 if the reset is still pending) |
| Fable reset | applied (detected 2026-11-11 10:44 UTC: Fable 91 → 3, all-models ≈ 51 → 9; ρ window restarted at 10:44, U 9; Ahmad emailed (Gmail 1a1ca365086f89f3) and pushed) |
| Fable alias resolves to | claude-fable-5-2 (Fable 5.2; transcripts of impl-PC-013, impl-PC-041, fix-PC-F08, 2026-11-10 20:16) |
| Full reset | pending (deadline Sat Nov 14 20:00 UTC; reminder cron Sat Nov 14 12:00 UTC) |
| Agent tool supports name / effort / fable | name: **no** (address agents by id via SendMessage) / effort: yes / fable: yes (model enum) |
| Environment check | git clean on platform-core, no prompt on `cd MAIN && …`; gh logged in (repo, workflow scopes); Docker 29.6.2; .NET SDK 10.0.401 (+9.0.314); Node 24.18.0; pnpm 11.9.0; check_deps 218 checkpoints, 0 problems |
| Notification test | email "Platform-core: run starting" delivered (Gmail id 1a1b98a787d056c9); PushNotification not sent (terminal active, tool skips while Ahmad is at the machine; it reaches the phone when he is away) |

## Calibration

Cost units are price-weighted (tools/usage_window.py). ρ = weekly points per M cost units.

| Updated | ρ | k_backend | k_client | Forecast T1 / T2 / T3 (weeks, incl. batch reviews and orchestrator) |
|---|---|---|---|---|
| 2026-11-08 (prior, levers applied) | 0.115 | 0.36 | 0.80 | 4.0 / 0.8 / 1.5 |
| 2026-11-08 15:10 (first measured ρ: 15 pts / 157.06M units since 05:03) | 0.0955 | 0.36 (prior; refit after more done) | 0.80 (prior) | not yet republished (due at 15 integrated) |
| 2026-11-09 07:40 (first measured k, from 12 integrated checkpoints: real units × ρ ÷ estimated kLOC; one-review cycles scaled to full) | 0.0955 | 0.45 (range 0.20-0.89; PC-053 0.89, PC-005 0.20) | 0.60 (PC-008 0.46, PC-011 0.35, PC-065 1.48 outlier: workspace move) | forecast due at 15 integrated |
| 2026-11-10 20:25 (forecast at 18 done; k EMA (0.3) over PC-018 0.53, PC-009 0.86, PC-024 0.45; raw fit over 14 backend: mean 0.58, median 0.53; client: no new done since 07:40, mean without PC-065 0.49) | 0.0955 (old window; re-measure once the new window spans 10 points) | 0.55 | 0.60 | **3.6 / 0.75 / 1.5** (T1: 123 pending, cycles 294 + 7 batch reviews 48 + orchestrator 18 = 361 pts; T2 75 pts; T3 150 pts; stop-the-line fixes not modelled, about 1 pt each; the Fable phase adds up to ~50 pts this week) |
| 2026-11-11 10:44 k_fable (Fable phase 2026-11-10 20:16 → 2026-11-11 06:50, agents finished by 10:40; kept out of the Opus k EMA) | Fable pts per M Fable units ≈ 0.27 (≈ 46 U pts over ≈ 170M units of Fable agents) | Fable cost factor ≈ 2.8 × Opus ρ (planned 1.8) | — | Fable checkpoints measured at Opus ρ understate their weekly cost by ≈ 2.8×; any future Fable phase should budget p × 2.8 |
| 2026-11-11 10:44 (forecast after the Fable reset, 25 done; k_backend 0.50 (EMA over Opus cycles; raw Opus mean 0.58, median 0.53), k_client 0.55 (median 0.46; PC-065 outlier)) | 0.0955 (new window from 10:44; re-measure once it spans 10 points) | 0.50 | 0.55 | **3.2 / 0.7 / 1.4** (T1: 116 pending, cycles 255 + 7 batch reviews 48 + orchestrator 17 = 320 pts; T2 70 pts; T3 140 pts; full reset still pending, deadline Sat Nov 14 20:00 UTC) |
| 2026-11-12 11:50 (forecast at 40 done; ρ measured over the window since the Fable reset: 39 pts / 318.18M units since 2026-11-11 10:44; k EMA (0.3) rescaled to the new ρ, over the Opus cycles done since 10:44: backend PC-012 0.63, PC-022 0.27, PC-027 0.39, PC-028 0.43, PC-029 0.54, PC-032 0.44, PC-101 0.33; client PC-020 0.61, PC-045 1.01, PC-069 0.63, PC-059 0.85, PC-071 0.58, PC-048 0.47; raw fit at the new ρ: backend n 26 median 0.67, client n 13 median 0.61) | 0.123 | 0.45 | 0.64 | **2.9 / 0.75 / 1.45** (T1: 101 pending, cycles 229 + 6 batch reviews 42 + orchestrator 15 = 287 pts, plus about 10 pts of stop-the-line fixes at the observed rate; T2 74 pts; T3 144 pts. Throughput about 1.7 U pts/h with 5 heavy agents. Points: 52 left this window, plus Ahmad's manual full reset (deadline Sat Nov 14 20:00 UTC) 100, plus the Nov 17 window 100 → T1 ends around Nov 25-26 if the manual reset is used) |

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
| 2026-11-08 14:10 | 55 | 26 | 2026-11-10 20:00 | PC-002 done; PC-003 integrated 68ddcc0; PC-005 launched; PC-F03 fix1 |
| 2026-11-08 15:10 | 57 | 26 | 2026-11-10 20:00 | PC-003 done; PC-005 integrated e33ceb3; PC-006 launched; ρ measured 0.0955 |
| 2026-11-08 17:50 | 60 | 26 | 2026-11-10 20:00 | PC-017 integrated 65ce1d1; PC-007 launched; PC-006 r1; PC-008/015 integrating soon |
| 2026-11-08 19:00 | 63 | 27 | 2026-11-10 20:00 | PC-008 integrated 0b64145; PC-F03 done; snapshot f112880; PC-016 launched; PC-006 r2 (Fable) |
| 2026-11-08 22:10 | 68 | 28 | 2026-11-10 20:00 | CI red 0b64145..0a35bb9 (CSP leak); PC-F06 integrated c71a009 (CI pending); PC-006/007 held; PC-053 launched |
| 2026-11-08 23:20 | 70 | 28 | 2026-11-10 20:00 | line green (c71a009, 732a1af); PC-F05 done; PC-007 integrated a6c928d; snapshot 35f367b; PC-011 launched |
| 2026-11-09 02:50 | 75 | 28 | 2026-11-10 20:00 | CI red a6c928d/35f367b (flaky grid-menu) → PC-F07 done 4220487; PC-006 integrated da3b766; PC-018 launched |
| 2026-11-09 03:30 | 76 | 28 | 2026-11-10 20:00 | PC-006 done; PC-016 integrated 0492c35; PC-065 (workspace root move) launched ahead of PC-060 to limit client-lane churn |
| 2026-11-09 05:00 | 79 | 28 | 2026-11-10 20:00 | CI red 0492c35/05cd337 (host-ICU test) → PC-F09 done f8d5130; PC-011/016 done; PC-053 integrating; PC-018 r1; PC-F08 waits for PC-053 |
| 2026-11-09 05:40 | 81 | 28 | 2026-11-10 20:00 | PC-053 integrated 63f0394; PC-F08 launched (pc-f, xhigh); PC-009 launched (pc-1) |
| 2026-11-09 06:30 | 83 | 28 | 2026-11-10 20:00 | PC-053 done; ready queue is client-only (PC-013/041/060): held until PC-065 (workspace move) integrates, to avoid cross-move merges; points will run out before the reset anyway |
| 2026-11-09 07:20 | 84 | 28 | 2026-11-10 20:00 | PC-018 done (a996caa green); PC-021 + PC-024 launched; PC-009 r1; PC-065 fix1; PC-F08 impl |
| 2026-11-09 07:45 | 86 | 28 | 2026-11-10 20:00 | PC-065 integrated eee616f (62M: workspace move); PC-009 fix1; PC-060 launched (gate with measured k: 14 - 6.1 - 2.4 = 5.5) |
| 2026-11-09 09:40 | 93 | 31 | 2026-11-10 20:00 | DRAIN: R=7 < F≈7.5; no new launches. PC-065/F10 done (b0bb12e green); PC-009 fix2; PC-F08 r1; PC-021/024/060 impl |
| 2026-11-09 10:40 | 96 | 31 | 2026-11-10 20:00 | drain; R=4: only next steps of started work (PC-024 fix1+integrate started) |
| 2026-11-10 20:10 | 0 | 0 | 2026-11-17 20:00 | scheduled weekly rollover; resume (§7.3); new window start for ρ; Fable phase starts; CI 42608310703 green on f2b1591 (PC-F12, PC-009, PC-024 done) |
| 2026-11-10 20:16 | 0 | 0 | 2026-11-17 20:00 | Fable phase: PC-013 + PC-041 impl and PC-F08 fix1 launched on fable (RF 50; F_fable ≈ 1.8 × (3.6×0.82 + 2.7×0.82 + 1.5) ≈ 12); PC-021/PC-060 integrating (Opus, resumed) |
| 2026-11-10 21:00 | 4 | 7 | 2026-11-17 20:00 | PC-021 integrated d554208; pc-4 free (no ready checkpoint until CIs land); PC-F04 launched as filler (fable). ΔFable/ΔU = 7/4 = 1.75 with Opus integrations mixed in: keep the /2 for now |
| 2026-11-10 21:40 | | | 2026-11-17 20:00 | PC-060 integrated 3d7aa7b; CI red on d554208 (windows unit, 2 tests) → stop the line, PC-F13 in pc-4 |
| 2026-11-10 22:10 | 8 | 15 | 2026-11-17 20:00 | ΔFable/ΔU = 15/8 ≈ 1.9 (keep /2); RF 42.5. CI red on 3d7aa7b (flaky select e2e) → PC-F14 in pc-2; PC-F13 fix pushed, R1 queued behind the D-028 cap |
| 2026-11-10 23:50 | 13 | 23 | 2026-11-17 20:00 | ΔFable/ΔU since 22:10 = 8/5 = 1.6 (Opus fixers mixed in; keep /2); RF 38.5. PC-F14 fix d24366d done; PC-F13 R1 spawned (fable); PC-F14 R1 queued behind the D-028 cap |
| 2026-11-11 01:40 | | | 2026-11-17 20:00 | PC-F13 done (3d019e6; backend jobs green); CI still red on the PC-F14 flake (select.spec.ts:379) until PC-F14 integrates; PC-013 fix1; PC-F08 r2 |
| 2026-11-11 02:00 | 19 | 34 | 2026-11-17 20:00 | ΔFable/ΔU since 23:50 = 11/6 ≈ 1.8 (keep /2); RF 33. PC-F04 impl done, R1 spawned (fable) |
| 2026-11-11 03:20 | 24 | 45 | 2026-11-17 20:00 | ΔFable/ΔU since 02:00 = 11/5 = 2.2 (all Fable work now; supports /2); RF 27.5. PC-013 fix1 done (integrate after green); no ready checkpoint (check_deps --ready empty); 4 heavy agents, all fix rounds |
| 2026-11-11 04:10 | | | 2026-11-17 20:00 | CI 42947124853 green on 8043295: line restored. PC-021, PC-060, PC-F13, PC-F14 done. PC-013 integrating |
| 2026-11-11 05:50 | 30 | 55 | 2026-11-17 20:00 | PC-F08 done (88f13fa green); PC-013 integrated 09ab023. check_deps --ready had been blind since 2026-11-09: 7 status cells read 'done (…)' instead of 'done' (D-045); normalized → READY PC-010, PC-014, PC-019, PC-025, PC-067. Fable gate: RF 22.5, F_fable ≈ 2 (two integrations); launched PC-010 (p 1.93, two-review), PC-014 (p 1.65, two-review), PC-019 (p 2.26) on fable: 22.5 − 2 − 1.8×5.84 ≈ 10 ≥ 3. PC-025 and PC-067 wait for the D-028 cap (5 heavy) |
| 2026-11-11 06:10 | | | 2026-11-17 20:00 | CI red on 09ab023 (ubuntu unit, FileSystemBlobStoreTests .part not found) → stop the line, PC-F15 queued; PC-F04/PC-041 told to hold their platform-core push |
| 2026-11-11 06:30 | | | 2026-11-17 20:00 | 09ab023 green on rerun (attempt 2): line restored; PC-013 done; PC-F15 downgraded to a flake fix (todo); PC-F04 and PC-041 told to integrate |
| 2026-11-11 06:50 | 37 | 69 | 2026-11-17 20:00 | ΔFable/ΔU since 05:50 = 14/7 = 2.0 (the /2 holds). RF 15.5; F_fable ≈ 1.8 × (0.8 × (1.93+1.65+2.26) + 0.3) ≈ 9; PC-025 (p 1.35, 2.4 Fable) fits: 15.5 − 9 − 2.4 = 4.1 ≥ 3; after it PC-067 (3.5) and PC-F15 (≈1.8) do not → Fable phase ends; Ahmad notified (Gmail + push). PC-F04 integrated fe04dbf; core-ui unit count 1455 → 1400 after merging PC-013 (l10n specs reworked; check) |
| 2026-11-11 07:51 | 49 | 90 | 2026-11-17 20:00 | ΔFable/ΔU since 06:50 = 21/12 = 1.75, but Fable rose 21 against ~9 forecast for the running Fable agents (k_fable well above 1.8·p). Fable ≥90% → every Reviewer 2 inherit xhigh (§3). PC-014 fix1 eebab50 → R2 (Opus xhigh); PC-067 R1 0/0/4 → fix1 + integrate. Three resumed Fable agents still running (PC-010 fix1, PC-019, PC-025): if the Fable meter hits 100% and they die, continue each on Opus from its pushed branch |
| 2026-11-11 09:05 | 50 | 91 | 2026-11-17 20:00 | Fable rise slowed (90 → 91 in 75 min). PC-014 R2 0/0/0 → integrate (D-047); PC-067 integrated f391cbd (fast-forward, CI 42988536575 pending); ready: PC-020, PC-045, PC-059 → PC-020 launched in pc-3 (Opus) |
| 2026-11-11 10:44 | 9 | 3 | 2026-11-17 20:00 | **Fable reset** detected: before U ≈ 51 / Fable 91 (last read 50/91), after 9/3; U dropped ≈ 42, about the Fable spend (≈ 92 Fable pts at ΔFable/ΔU ≈ 2 → ≈ 46 U pts). ρ window restarted (U 9). Normal process from here, including Reviewer 2 on fable for security-bound checkpoints. PC-014, PC-019, PC-067 done since 07:51 (25 done) |
| 2026-11-11 12:03 | 12 | 6 | 2026-11-17 20:00 | PC-010 done (CI 43015763392 green; 27 done). PC-025 done. PC-012 (last Phase 1 checkpoint; deps now met) is next at the first free heavy slot, in pc-f, ahead of PC-032/PC-059; PC-B01 starts when PC-012 is done. In flight: PC-027, PC-069 impl; PC-022, PC-020 R1; PC-045 fix1+integrate |
| 2026-11-11 14:50 | | | 2026-11-17 20:00 | CI red on 9299989 (PC-045): Windows lint-rule scope test → stop the line, PC-F19 in pc-f (over the D-028 cap); PC-020 holding. PC-022 done after a flaky rerun (PC-F18 opened) |
| 2026-11-11 16:00 | | | 2026-11-17 20:00 | Line still stopped: d704d03 code-green, Chromium apt install red ×3 on a runner mirror outage → PC-F20 (5 heavy: PC-012, PC-032, PC-027, PC-069, PC-F20); 3rd re-run in parallel |
| 2026-11-11 16:24 | | | 2026-11-17 20:00 | Line green: CI 43056016205 on 67fa80d (PC-F20). PC-045, PC-F19 done; PC-069 cleared to push; PC-020 integrate queued behind the D-028 cap |
| 2026-11-11 17:40 | 22 | 8 | 2026-11-17 20:00 | ΔU 10 since 12:03 over ~6 h (≈1.7/h) with 5 heavy agents; Opus-heavy (Fable +2). 29 done; in flight PC-012 fix1, PC-027 fix2+int, PC-069 fix2+int, PC-020 int, PC-032 r1 |
| 2026-11-11 21:31 | | | 2026-11-17 20:00 | PC-012 (last Phase 1 checkpoint) integrated 7db93ca, CI 43103329956 pending → PC-B01 batch review on green, in the next free checkpoint slot (pc-3 after PC-020 lands). PC-028 launched in pc-2; ready T1: PC-029, PC-048, PC-071 |
| 2026-11-12 01:24 | 35 | 16 | 2026-11-17 20:00 | ΔU 13 since 17:40 (≈1.6/h, 5 heavy agents + PC-B01 Fable finders). 34 done; in flight PC-059/PC-029 fix+int, PC-028 fix1, PC-071 impl, PC-F18, PC-B01 |
| 2026-11-12 06:00 | 43 | 19 | 2026-11-17 20:00 | ΔU 8 since 01:24 (≈1.7/h). 37 done + PC-F15 done; PC-B01 done. In flight: PC-071 fix2+int, PC-101 fix1+int, PC-023 impl, PC-048 impl, PC-F18 |
| 2026-11-12 11:50 | 48 | 19 | 2026-11-17 20:00 | Line green: CI 43210943912 on a77ee57 (PC-F22). PC-048 done → 40 done; 40-checkpoint forecast published (T1 2.9 weeks). PC-023 released to integrate; PC-F21 fix1 (R2 due), PC-F18 fix1; pc-2, pc-3, pc-f free (only T3 ready) |
| 2026-11-12 13:50 | | | 2026-11-17 20:00 | PC-F18 done (CI 43216268294), PC-023 done (CI 43221550998) → 41 done. check_deps fix (D-059): 8 T1 ready → PC-026 pc-1, PC-033 pc-3, PC-034 pc-f launched; PC-F21 r2, PC-F23 impl |
| 2026-11-12 19:22 | | | 2026-11-17 20:00 | PC-F23 done (CI 43248406932), PC-035 done (CI 43284321336, 42 done), PC-F21 done (CI 43287447113; PC-051/PC-057 unblocked). In flight: PC-026, PC-033, PC-034 fix+int; PC-073, PC-036 impl |
| 2026-11-12 20:00 | 59 | 19 | 2026-11-17 20:00 | ΔU 11 since 11:50 (≈1.4/h, 5 heavy agents, Opus only). R 41: in flight PC-026/PC-033/PC-034 fix+int, PC-073/PC-036 impl; next ready PC-051, PC-057, PC-075 |
| 2026-11-13 03:00 | | | 2026-11-17 20:00 | CI red on ddb8259 (grid-a11y forced-colors flake + real forced-colors CSS bugs) → PC-F25 fixed, green at f9b7e33; PC-033, PC-036 done (45 + fix items); PC-026 released to integrate; PC-047 r2 fable; PC-038/PC-051 fix1; PC-073 fix2; PC-037 impl. D-064..D-067 |

## In flight

| PC | Slot | Branch | Step | Implementer agent (name / id) | Reviewer agent (name / id) | Started (UTC) |
|---|---|---|---|---|---|---|
| PC-075 | pc-3 | pc/PC-075 | fix2 + integrate (R2 0/1/5: post-boot storage faults reject put/patch/readAll and a memory-fallback patch is lost; sweep vs in-flight write ordering; sign-in-failed defers the sweep; linked-measure restatement (~16.44); stale PC-113 lead; fixed 50 ms waits) | a079c6e696d8bf4b1 (Opus inherit) | r1 abcee4d6b2b554a69 done, r2 a5a1ec716eb481414 done | 2026-11-13 |
| PC-097 | pc-f2 | pc/PC-097 | impl | a342737a128172612 (Opus inherit) | | 2026-11-13 |
| PC-030 | pc-1 | pc/PC-030 | impl | afa920119d3e8d01d (Opus inherit) | | 2026-11-13 |
| PC-043 | pc-4 | pc/PC-043 | impl | a5331f5d26f9b6e59 (Opus inherit, xhigh) | | 2026-11-13 |
| PC-054 | pc-2 | pc/PC-054 | impl | aede283b100436c0c (Opus inherit) | | 2026-11-13 |

## Fix items

| Fix | For | Status | Commits | Cost units (M) | Notes |
|---|---|---|---|---|---|
| PC-F01 | PC-000 (CI red on 8a1ebf8) | done | 6ef6423, d1ab25e (pc) | 2.49 (impl 2.18, r1 0.31) | quarantined 5 flaky e2e; nightly job client-e2e-quarantine (non-blocking, per-test table); CI 42455962373 green; nightly 42456014559 green |
| PC-F03 | entity-picker same-frame Enter race: Enter in the same frame as the last keystroke skips the highlight (reproduced, data-changing; frozen 0006 component) | done 000126e (R1 1/0/2 all fixed; CI 42505603036 green), fix-PC-F03 / a62d83dfcb72a8e4f (high); r1 aa9a0cc9ac7a293cb | af35d27, bcba0eb, 2645e7a, 000126e (pc) | 11.78M | arrow-key burst split out to PC-F04; from PC-F02 R1-2; brief: reviews/PC-F02.md R1-2 |
| PC-F04 | entity-picker arrow keys in a burst: type + ArrowDown + Enter in one task picks row 1 where paced input picks row 2 (aria relays only the last arrow, after render) | done: integrated fe04dbf (CI 42963703089 green; two clean merges; e2e 593/593); fix1 done cafad9e + 0601db5 (footerRows single source; e2e 549/549); R1 0/0/1 (optionKeys hand-kept copy of the template's rows), bursts 520/520 under load, aria 22.0.3 pinned; impl done efbfeca on d554208 (picker owns plain arrows while open; 3 same-family races fixed; 4 deviations from 0006 §4.3; 27 unit + 14 e2e tests, fail-before shown), pc-f2, fix-PC-F04 / a15b7b3b70625f925 (fable, high) | | | from PC-F03 R1 decisions; brief: reviews/PC-F03.md implementer notes "open risk" |
| PC-F05 | e2e timing-sensitive tests: (a) grid-touch.spec.ts:206 double-tap opens the editor (wall-clock threshold; passed on retry locally); (b) entity-picker "axe floor › popup states" (five axe scans, 12-25 s against a 30 s cap, hit 30.1 s with 1 worker); (c) other heavy axe scans near the cap (grid-entity axe & RTL dropdown open (dark), grid-a11y-editable populated) | done 732a1af (CI green; R1 0/1/2 fixed), pc-f free, fix-PC-F05 / aa332812685080600 (high); r1 ae34baef2b7ac3576 | d67c757, 0073b2b, 1962b30, 732a1af (pc) | 9.76M | from PC-F03 fix1 + PC-008 r1 |
| PC-F06 | CI red on 0b64145/f112880: tm-control-size.spec.ts 5/5 fail with CSP 'unsafe-eval' from Angular JIT; PC-008's tm-icu-csp.spec.ts CSP leaks into the shared vitest browser page (order-dependent) | done c71a009 (CI green), a6567bfbe1a75233d; r1 a9b4263ecc1f6dbc3 | ce1f544, ed4aa7e, c71a009 (pc) | | stop the line; PC-006 integration held |
| PC-F07 | CI red on a6c928d and 35f367b: grid-menu.spec.ts:131 "a 3-row selection pluralizes the row items" flaky (menuLabels read [] before the menu rendered) | done 4220487 (CI green; R1 0/0/1 fixed), pc-f free, fix-PC-F07 = aa332812685080600; r1 a56b459f20f91c5fd | 97502a4, 4220487 (pc) | 7.34M | stop the line; PC-006/PC-016 integrations held |
| PC-F08 | out-of-process PDF rendering for thumbnails: PDFium form-XObject fan-out expands in-process inside FPDF_LoadPage (memory unbounded, D-030 addendum 2) | done: integrated 88f13fa (CI 42950568102 green; clean merge over 739d09c, Windows gate green). fix2 done e123782 + 89d8e65 (host non-dumpable + debugger pipes removed before every worker start; fcntl/ioctl signal and TIOCSTI paths closed; worker setsid + non-dumpable; verified uid 1654 / capless root / root in sdk:10.0; blocks=1 (docker exec rm, worked around; Ahmad notified 04:40); rulings D-043). fix2 (resumed af299b7e06195163f; D-041). R2 0/1/1 (aeb0e1592089bed5d): host-memory vector via /proc/<host>/fd doublemapper memfd reopen, Yama does not gate it; environ readable; other D-040 residuals hold. fix1 done 7ea007f + bfa9e4a (fix-PC-F08b / af299b7e06195163f, fable xhigh, 20.04M): R1-1..6 fixed, addenda 4+5 applied, 7 deviations; points ruled in D-040; not verified: real musl host, restricted token under IIS, arm64 — earlier history: was BLOCKED/parked: fix1 half-done UNCOMMITTED in pc-f (24 modified + 2 new files; addendum-5 items not applied); agent responses withheld 3x (classifier block; Ahmad notified); resume after reset with a FRESH agent (§8.1 re-spawn) using scratchpad/PC-F08 notes; R1 0/2/4; xhigh; two reviews: security-bound), pc-f, fix-PC-F08 / a2885b9362b097797; r1 a8779ceb6a081e7d0; blocks=1 (rm empty scratch dir; Ahmad notified 08:4x); MUST land before PC-140; evidence: 1.4 KB PDF = 3-11 s CPU on Linux holding the PDF gate) | da3714f, 7ea007f, bfa9e4a | | from PC-053 R2-1 |
| PC-F09 | CI red on 0492c35 (ubuntu only): CalendarFormattingTests umm-al-qura culture test assumes host ICU default calendar (UmAlQura) — Ubuntu gives Gregorian | done f8d5130 (CI green), pc-f free, fix-PC-F09 = impl-PC-016 a7a4c107c0b2fd2e0 (resumed); r1 a63ec536f92b2800c | c6ac197, ba9e69d, f8d5130 (pc) | | stop the line; PC-053 integration held |
| PC-F10 | CI red on eee616f (client + client-windows): tools/tests/core-lint-scope.spec.mts 2/4 fail on a clean runner (lint sees no rules); passed locally (build-state dependency) | done b0bb12e (CI green), pc-f2 free (new 2nd fix slot, D-035), fix-PC-F10 = impl-PC-065 aa5429f429a3c864d (resumed); r1 a74b58946c4730232 | 4eb5789, b0bb12e (pc) | | stop the line |
| PC-F11 | PDF worker filesystem confinement (Landlock on Linux; AppContainer or low-integrity on Windows); deny /proc beyond the worker's own entries and its temp directory (D-043); covers writes too (in-place overwrite via shared mapping, truncate, rename, delete; D-040) | todo (before GA, not MVP) | | | from PC-F08 residual risk (D-030 addendum 3) |
| PC-F12 | CI red on c9d40fc: identity-e2e AccessibilityTests.The_consent_screen_is_accessible times out waiting for "Authorize application" (PC-009 regression; E2E not run locally) | done f2b1591 (CI 42608310703 green), pc-f2 free, fix-PC-F12 = impl-PC-009 a584af71acfe37be7 (resumed); r1 a041cc926ee8db924 | b3a8f07, f2b1591 (pc) | | stop the line; PC-021/PC-060 integrations held |
| PC-F13 | CI red on d554208 (build-test windows-latest, unit): ModelCompositionTests.One_definition_builds_one_model_and_another_composition_builds_its_own (Assert.Same on two RuntimeModels, PC-021) and TenantRegistryTests.A_reloaded_profile_that_breaks_the_connection_rules_carries_no_tenant (Assert.Empty, PC-024; also failed locally on Windows at 7f0462a per the PC-060 integrator) | done: integrated 3d019e6 (fast-forward; CI 42932109472: build-test windows+ubuntu, integration green; client red only on select.spec.ts:379, the PC-F14 flake, not yet integrated); R1-1..3 fixed, two new fake-clock tests fail on the old registry, fill-the-cache test fails on the old factory; cost fix ≈ 2.1M + r1 2.80M; R1 0/0/3 (null listener task faults the dispatch; two behaviour changes unpinned; commit-message test name), stability 57/57 under load; r1 ab502c409f6bf4ae2 (fable high); fix d8e214c pushed (3 causes: EF shared model cache eviction → factory pins its model via UseModel; fake reader counted reloads before the snapshot swap; listener bound started late); open: TenancyCompositionTests probes may advance the clock before the loop re-arms; commit message cites a wrong test name (real: The_host_loads_the_snapshot_at_start_and_the_loop_probes_every_interval); Windows only, Linux not run; pc-4, fix-PC-F13 = impl-PC-021 a71dae021826abce3 (resumed, Opus) | d8e214c (pc/PC-F13) | | stop the line; no integrations until green |
| PC-F14 | CI red on 3d7aa7b (client, PR-tier e2e chromium): select.spec.ts:379 tm-multi-select Space/Enter toggle flaky (multi-value stayed "" after Home + Space; passed on retry) | done: integrated 8043295 (CI 42947124853 green); fix1 fc671dd (R1-1 setExpanded writes aria's model too; R1-2 keys before render queued and replayed; gates rerun; select.spec 320/320); R1 0/1/2 (tm-select expanded model stale after a relayed commit → double dispatch; keys before the panel renders still dropped; gates omitted), keyboard tests 80/80 under load; r1 a758ab4fefeabc8b0 (fable high); fix d24366d pushed (product bug: aria combobox keyboardEventRelay collapses two keys in one render window; tm-select-base relays open-panel keys synchronously; 2 new unit tests; e2e 20/20 under load), pc-2, fix-PC-F14 = impl-PC-060 a517313f6813eaf8a (resumed, Opus) | d24366d (pc/PC-F14) | | stop the line |
| PC-F15 | CI red on 09ab023 (build-test ubuntu-latest, unit): FileSystemBlobStoreTests.Objects_land_under_the_tenant_kind_and_fan_out_directories — BlobStoreException wrapping FileNotFoundException on <root>/t7/.tmp/<guid>.part (the tenant's .tmp directory vanished between create and open?); not in PC-013's code; 88f13fa and 739d09c were green | done (b5ba404, CI 43158463420 green); product bug, Linux only: a CreateNew open racing another writer's CreateDirectory raises ENOENT as FileNotFoundException, which skipped the ensure-and-retry; fixed, with a deterministic race test (opener injected at construction); R1 0/0/1 fixed | e4fc644, d4fb2ad, b5ba404 (pc) | 2.2M (fix 1.35, r1 ~0.85) | possible real race in FileSystemBlobStore temp handling; fix when a slot frees |
| PC-F16 | identity path base: EmailCode.cshtml.cs falls back to literal /Identity/Account/Login and EndSessionController redirects to literal /Identity/Account/LoggedOut, both missing the in-proc /id path base (pre-existing, found by PC-014); plus the passkey E2E helper's unconditional click after a 5 s wait | done (86d077b, CI 43177399924 green); 3 redirect sites → Url.Page with the area; PathBaseRedirectTests; passkey helper probes conditional mediation (DOMContentLoaded on the button path); R1 0/0/2 fixed; follow-up: StepUpTests/SyncedPasskeyTierTests still wait-then-click | 794745d, 526c4b9, 86d077b (pc) | 4.99M (fix 3.96, r1 1.03) | from PC-014 impl |
| PC-F17 | grid scroll budget: the nightly @perf scroll probe (no long task > 50 ms) fails on the flat grid (46-48 long tasks up to 94 ms) and on the 21,000-row tree (2-18 long tasks of 52-132 ms; tm-icon ≈ 10% of scroll CPU); lands @quarantine with PC-045 | todo (Opus; before GA) | | | from PC-045 R1 |
| PC-F18 | client test infra under parallel slot load: (a) tm-grid-clipboard.spec.ts "menu Paste…" waits with a turn-capped `eventually` (20 turns ≈ 100 ms) that races a real Blob.text() on loaded CI (red on ef67f47) → make `eventually` deadline-based (≈ 2 s) or resolve the mock's text() at once; (b) the real-clipboard e2e lock lives in the OS temp dir shared by every slot and its wait counts against the 60 s test timeout → per-slot lock path or exclude the wait | done (CI 43216268294 green at 2bd087b) ← integrate (fix1 703d2e8: headed-only lock, rename-based reclaim, mkdir error codes, unit wait 10 s) ← fix1 (R1 0/0/4: lock only when headed; atomic stale reclaim; mkdir error codes; unit wait about 10 s) ← r1 (impl e32415c on pc/PC-F18: deadline-based unit wait; e2e clipboard lock in client/.artifacts, wait credited to the test timeout, dead-pid reclaim; PR-tier e2e 779/779 with --workers 2) ← impl (2026-11-11 18:1x), pc-f2, fix-PC-F18 / a91a89a49cb025f3e (Opus) | e32415c, 703d2e8, 2bd087b (pc) | 3.0M (impl+fix) + r1 0.8M | from PC-045 integrate |
| PC-F19 | CI red on 9299989 (client-windows lint:test): tools/tests/no-platform-sniffing.spec.mts scope test "applies to the library and module packages, specs included" expected +0 to be 1 on Windows (rule added by PC-045); Linux jobs failed on an Ubuntu mirror DNS outage | done (d704d03; green at 67fa80d, CI 43056016205): code jobs green; Install Chromium (identity-e2e, client) red three times on a runner Ubuntu-mirror DNS outage (15:59); 3rd re-run red too; PC-F20 opened): the scope test, not the rule, was wrong; typescript-eslint under CI=true lints typed paths from disk, ignoring lintText → test opts out with disallowAutomaticSingleRunInference; helper fails on fatal parse messages | d704d03 |  | stop the line; PC-020 holds its push |
| PC-F20 | CI red three times on d704d03 with every code job green: Install Chromium (identity-e2e, client; playwright --with-deps) fails when runners cannot resolve the Ubuntu apt mirrors | done (2b3377b, CI 43077273546 green; the sudo path ran on a hosted runner, mirror up): R1 0/1/1 fixed in e9f7c7a (per-attempt limit runs as root via sudo -n timeout; sudo -n preflight, no password hang). First version 67fa80d green on CI 43056016205 | 67fa80d, e9f7c7a, 2b3377b (pc) | 1.74M (impl+fix 1.11, r1 0.63) | stop the line (infra); may push to platform-core |
| PC-F21 | ./grid budget (D-057): spec 0010 §C.11's 42 KB for §C.1–§C.14 is filled by §C.1–3/5–7 (41.92 KB after PC-048); measure where the bytes go and re-split (on-demand editable machinery, secondary entry points) or take honest savings until §C.4/§C.14 (PC-051) and §C.8/§C.9 (PC-054) fit PC-048 R1 attribution: editors.ts 2.09, status-bar.ts 0.72, editor-session.ts 0.69, field-writer.ts 0.50 KB ≈ 4.0 KB freeable on demand, plus editing code inside grid-core.ts | done (CI 43287447113 green at 22780e3; V-1/V-2 9208dfb; showcase anyComponentStyle error budget 13 → 13.5 kB (D-060 item 5); ./grid 39.01/39.25; e2e CI mode 848 passed) ← integrate (verify ae842da6eeee1e322: R2 blocking/major fixed, new 0/0/2; V-1 tabindex fixed at integration, V-2 to Ahmad, D-060) ← verify (one targeted verification, §4.4) of fix2 35039d0: bare <input> draft owned by the browser; editor takes over text and caret on arrival; idle preload; entity drafts stay pending until search resolves; ./grid 38.99/39.25, ./grid-editing 6.44/6.5; e2e 789 passed ← fix2, the last round, then one targeted verification (§4.4). R2 1/2/8: a modifier or dead-key keydown commits the draft (Shift+C replaces it); a failed or timed-out load commits mid-typing; an entity draft on a search-only column clears the cell; Mod+Z undoes an unrelated edit; focus stolen; enum prefix; IME append; timer leak; test gaps; lucide golden import; no touch keyboard. Direction: the draft is a bare <input>, plus an idle preload of ./grid-editing ← r2 (inherit xhigh; fix1 c07bef9: draft committed by the engine during the load, no hold or replay; 10 s load timeout; shared typed-text predicate; static-graph size gate; ./grid 38.27/38.75, ./grid-editing 6.27/6.37; golden back to row/none; e2e 784 passed) ← fix1 → R2 (R1 1/3/6, escalated: api:check union order from a warm cache (blocking); a double-click during the load discards held text; a held commit leaves the engine session open (blur/Save/focus/write order); replay's typed-char test misses AltGr/Alt; default actions lost; no load timeout; focus stolen on arrival; lint sees direct imports only; TSDoc spec refs; deviation 3 also in prod) ← r1 (fix 15ba8ec: editing moved to new entry @tellma/core-ui/grid-editing, loaded via import() when editable, keys held and replayed; ./grid 42.00 → 37.06 KB (ceiling 37.5), ./grid-editing 7.06 (7.16); ESLint guard; e2e 779) — gates PC-051/PC-054 | 15ba8ec, c07bef9, 35039d0, 9208dfb, e1ea13f, fc3eb6c, 22780e3 (pc) |  | D-031 re-split clause (third raise) |
| PC-F22 | CI red on 9007c81 (client, PR-tier e2e chromium): grid-search-page.spec.ts:124 'Enter opens the active row; Mod+Enter opens it in a new tab' (ltr) flaky — first attempt 5.4 s, activated text not '1 via keyboard'; retry passed; new PC-048 test | done (CI 43210943912 green at a77ee57) ← integrated a77ee57 (fast-forward of 6e6f973 + a77ee57; CI 43210943912 pending; R1-1 fixed with a mutant-checked test; R1-2 → PC-F24) ← fix1 + integrate (R1 0/0/2: test for a repeating Enter with no active row; consumer-write mirror race pushed to PC-F24) ← r1 (fix 6e6f973: product race — driver Enter read the activeRowId model, written back only on the next render, so Enter queued behind ArrowDown fell through; now activateRow() reads the engine synchronously; deterministic unit + e2e; spec ×20 840/840; full CI-mode e2e 781/781) | 6e6f973, a77ee57 (pc) |  | stop the line; PC-023 holds its push |
| PC-F23 | api:check golden union order depends on the build path: a single-project `ng build` and the full `pnpm run build` print literal unions in different orders (`rowActivation` row/none, chip `kind`); PC-048 and PC-023 each flipped goldens with api:approve, so CI api:check can fail on whichever order the other path produced. Make the API report order-stable (sort unions in the report, or make api:approve use the CI build path). PC-F22 finding: the flip comes from a warm .angular/cache (clean build prints "row" first, matching CI); approving from a warm cache writes the wrong order | done (CI 43248406932 green; integrated fe611bf; fix 2aeb8e1, merge 01a5726; post-merge approve reordered chip/toast/banner/core-ui-testing goldens) ← fix1 + integrate (R1 0/1/4: same-line comments lost when reordering; multi-line separators; number vs bigint rank; CRLF; shape tests) ← r1 (impl 87db466: TypeScript-parser normalizer sorts every union in the API reports on check and approve; 29 goldens re-normalized once; warm and clean builds both pass) ← impl (2026-11-12 11:55), pc-2, fix-PC-F23 (Opus) | 87db466, 2aeb8e1, 01a5726, fe611bf (pc) | 3.75M (impl+fix+int) + r1 1.05M | from PC-048/PC-023 integrations |
| PC-F24 | tmGridDriver: a consumer write to activeRowId reaches the grid only on the next render, so an ArrowDown or Enter queued behind it acts on the stale active cell (pre-existing; found by PC-F22 R1-2). Fix: apply the pending consumer write before moveActiveRow/activateRow | todo (low) | | | follow-up |
| PC-F25 | CI red on ddb8259 (client, PR-tier e2e chromium): grid-a11y.spec.ts:95 forced colors 'selected and active cells stay distinguishable from normal cells' flaky (selected background equalled the plain even row's at line 113; passed on retry) | done (CI 43352828665 green; f9b7e33) | 6b47145, f9b7e33 (pc) | 5.64M (impl+fix 4.30, r1 1.34) | stop the line |
| PC-F26 | forced colors: the row-activation accent bar is Highlight on the first painted cell and may match a hit row header or a checked checkcell wherever SelectedItem equals Highlight (Windows); found by PC-F25 | todo (low) | | | |
| PC-F27 | CI red on 89c4708 (client, PR-tier e2e chromium): splitter.spec.ts:146 'a shrinking container clamps the list and keeps the size asked for' flaky (toBeLessThan NaN on the first attempt; passed on retry) | done (CI 43388722956 green at c3103a7; fast-forward; fix1 waits on the resize's own render (aria-valuemin) and on fonts; 270/270 repeat) | dfe426b, c3103a7 (pc) |  | stop the line |
| PC-F28 | CI red on 9f9adb7 (build-test windows + ubuntu, unit) and once on 5c3674f: TimerTests.The_reconciliation_runs_five_minutes_after_the_first_snapshot_and_then_every_interval ("condition did not hold in time"; likely FakeTimeProvider advance before the timer is armed; PC-047) | done (integrated a533113 (CI 43421083528 green; fast-forward; root cause: the reconcile PeriodicTimer was created after the first run, so early advances were lost; services now wait on absolute due times via HostedSchedule with an internal armed hook; TimerTests 60/60 + 50/50 under load); r1 a5122952bd3d79729 done 0/0/1: R1-1 same lost-Advance race in SessionLifecycleTests identity-server-deadline test (hang risk); fix1 integrated baffb30 (CI 43428288932 green; stall test steps the clock until done, all awaits bounded; 32/32 under load) (impl-PC-047 a5cf206eb092b4ff3 in pc-f, also commits the untracked review file)) | a533113, baffb30 (pc) |  | stop the line; D-028 cap exceeded by one (6 heavy) until PC-037 returns held |
| PC-F29 | load-sensitive tellma-core-tools tests from PC-073: lint:test build.fixture.spec.ts (1200 s beforeAll) and common.spec 'starts a package manager's shim' (vitest default 5 s, spawns pnpm --version) time out when dotnet gates run alongside; pass alone | todo (low) | | | make them robust under load (explicit timeouts sized to a cold run, or move the cold ng build to the nightly tier) |
| PC-F02 | systemic e2e flakiness: CI 42456030333 red on 5250c6c (plans-only) with 4 NEW flaky tests | done (R1 1/1/2; CI 42471952389 green), pc-f free | 2c91df4, 4c36d45, 4c89ae1 | 11.32M | grid bundle at its 38 KB ceiling; picker same-frame Enter race open | root-cause all 9; un-quarantine the fixed PC-F01 five |

## Batch reviews (one per phase)

| Batch | Phase | Status | Commit | Findings |
|---|---|---|---|---|
| PC-B01 | 1 (PC-000…PC-014 at 7db93ca) | done (7e13990, CI 43142784871 green) | a31a1a5, 7e13990 (pc) | 0/2/2 all fixed (identity API audience/kind policy; CLI audience; file-preview download anchor; …); D-055; one finder returned none; 553k subagent tokens |

## Merge-time tasks (before platform-core merges into main)

| Task | Source |
|---|---|
| ci.yml: add `integration-windows` (LocalDB) to `all-checks-passed` once it has run green on platform-core (kept non-gating at birth) | PC-085 R1-2 |
| nightly-tier.yml: restore the schedule and a main guard, replacing the trampoline | PC-000, D-012 |
| CONTRIBUTING.md: drop the platform-core-only sentences; .gitattributes: drop the plans/** line; delete plans/ | PC-000, D-012, PLAN.md header |
| ARCHITECTURE.md CI/CD: client job is Ubuntu-only (spec 0024 target) — confirm wording | PC-000 impl note |
| Spec 0017 to be brought in line with PC-F08: host non-dumpable and debugger pipes removed on Linux once a worker starts; worker setsid/non-dumpable and refused signal/terminal syscalls; BlobPdfWorkerPtraceUnrestricted's new meaning; hosting guidance (unprivileged account with nothing else under it, run as a service, CAP_SYS_PTRACE only where crash dumps are wanted; D-043); Tellma:Blobs:PdfWorkerConcurrency (§1.3/§3.5), the PDF-thumbnail host rule (Linux glibc only; Windows/macOS Development only; musl never), log events BlobPdfThumbnailsDisabled and BlobPdfWorkerPtraceUnrestricted (§10.2) | PC-F08 fix1, D-040 |
| Staging: run dotnet-dump, dotnet-counters, dotnet-trace and a managed debugger against a Linux host after a PDF worker has started, to confirm PC-F08's measured diagnostics costs | PC-F08 fix2, D-043 |
| Spec 0010 §C.11: ./menu ceiling 8.5 KB (was 7; D-042); §C.10: group dividers drawn above a group whenever a non-separator entry precedes it; one Escape entry per menu that collapses an expanded submenu or closes the menu, replacing "each level pushes its own entry" (D-044) | PC-041, D-042 |
| Spec 0012 §2 convention table (child IX_<Table>_<Parent>Id INCLUDE (Id) on every child) vs spec 0014 §3.6–§3.7: the convention now skips its index when a configuration declares an unfiltered index leading with the parent key; spec 0014 §3.1 Users.State should read ISNULL(CAST(CASE … AS varchar(8)), 'New') | PC-029 |
| Spec 0012 §6.4/§6.5: transient numbers before the severity-20 rule; a timed-out Read/Validate re-runs once (not 3×); after NotCommitted any final re-run failure re-probes (not only 2627); probe lookup is FORCESEEK (D-052) | PC-028 |
| Spec 0012 §5.4 (batch read dedup): a read repeated after a declared write must not reuse the earlier handle; built as a write epoch that clears the dedup cache (PC-027 review 2). Also §TransactionMode.Explicit: no hook for the pre-commit C# decision the spec cites | PC-027 |
| Spec 0010 §F examples 1–2 ('uq' lookup by date): EndDate checked outside the TOP (1) seek; hoisted 'uq' variables get OPTION (RECOMPILE) as a CompiledQuery.Recompile flag, host-bound parameter is the faster alternative; spec 0012's composers must merge the hint into their one OPTION clause, and its three IF guards that embed Body must assign under the hint first (D-050) | PC-012 |
| Spec 0012 §5.2 vs analyzer TELLMA0003: the analyzer accepts nameof holes, but §5.2 binds every non-SqlIdentifier scalar as a parameter, so a nameof hole renders as @tb{b}_p{n}, never an identifier; ruled at R1: column names via SqlIdentifier(nameof(...)), the analyzer refuses a bare nameof hole; §12.3 should say so | PC-027 impl, R1 |
| Spec 0024 §4.2 (D-048): say the boot script's 3 KB gzip covers code + English, +0.4 KB per further language; budget covers (≈3.05 KB with English strings, ≈3.37 KB with Arabic, ≈3.70 KB with Amharic) | PC-069 impl |
| Spec 0023: TmRelativeTime's wording input is `textStyle` (Angular never delivers a value to an input named `style`); counter.overflow receives {max} pre-formatted; §1 budgets for the seven indicator entry points are below the built sizes (budgetNotes) | PC-020 impl |
| Spec 0008 Appendix A: name each Queryex diagnostic's message arguments (engine raises QX3101 with two argument shapes; QX3005 carries @index); count-type problem details need expected/actual/maximum carried by the exceptions | PC-022 impl |
| Spec 0010 §A.10: the return to the authorization endpoint re-applies the request's language, but on consent the language cookie outranks a pushed (PAR) request's ui_locales (PC-014 deviation 5; D-047) | PC-014 R2 |
| Spec 0024 §1: add the ./config-check lazy entry point to the entry-point table (PC-067 deviation; its .d.ts imports types from @tellma/core so TmFeature stays one branded type) | PC-067 R1 |
| Spec 0011 §3.9 vs §5.7: in-process identity shares the container with AddTellma, whose scoped TenantSandboxContext throws outside a tenant, so the engine's tenantless invitation mail cannot get a sandbox answer as §3.9 requires; spec 0013 §9.3 step 5 (initializer sets the thread culture) cannot work and moves to the front doors | PC-025 R1 |
| Spec 0010 §F.8/§F.9: the compiled-query L2 key is text + the omitted declarations it refers to (with types) + polarity, not "the omitted set"; Via/Exists folded as one atom lifts a supplied sibling restriction inside the node (ambiguity to confirm); §F.8 should state that a repeated ordering term is dropped only when folding rewrote it or the term it repeats, and otherwise stays QX4008 as in spec 0008 (deviation 13); 0008's limit wording and §F.8: an absent value where an omitted parameter stood costs one typed node, not the three of cast(null) (deviation 14) | PC-010 R1, fix1 |
| Spec 0013 §1.3 vs AddLanguage: a language added with AddLanguage can never be offered without a Tellma.Core satellite (startup fails); §7.2's English fallback does not cover it. §9.2 names the negotiated culture <language>-<region> yet asks for a -u-nu-latn clone; the build pins formats field by field (D-046) | PC-019, D-046 |
| Manual screen-reader pass (NVDA + VoiceOver): a rich menu's header block is read on open through aria-describedby of role="menu" | PC-041 R1-9, D-044 |

## Flaky

| Test | Since | Tracking |
|---|---|---|
| dotnet Tellma.Core.IntegrationTests RegistryCatalogTests missing-catalog test (failed once under load on LocalDB, PC-085 impl) | 2026-11-13 | watch; root-cause per D-018 if it recurs |
| client lint:test tellma-core-tools build.fixture.spec.ts (one cold ng build; 1200 s beforeAll timeout hit while dotnet build and client gates ran in parallel; 11/11 in 299 s alone; PC-057 fix1) | 2026-11-13 | watch on CI |
| CI client-e2e-quarantine/client-e2e: playwright OS deps install failed (apt mirror), webkit failed to launch (run 43403796974 at 5c3674f) | 2026-11-13 | infra; watch |
| dotnet Tellma.Imaging HostileDocumentTests memory-growth floor (119 MiB under the 128 MiB floor under parallel load; passes alone; PC-047 merge) | 2026-11-13 | watch |
| dotnet Tellma.Core.IntegrationTests DataBatchTests cancellation wall-clock assertion (failed once under load; 28/28 alone; PC-047 merge) | 2026-11-13 | watch |
| dotnet Tellma.Core.IntegrationTests SqlQueryTextTests.A_commands_span_carries_no_query_text (captures other classes' fixture ALTER DATABASE spans running at the same time; passes alone; seen by PC-033 integrate and PC-038 R2) | 2026-11-13 | watch; a fix item if it reaches CI |
| client e2e grid-clipboard test stuck on the shared clipboard lock (PC-B01 fixer, local; 14 load failures on first full run, reruns green) | 2026-11-12 | watch; PC-F18 area |
| (lesson) client tooling tests that lint sample text through a typed-linted path must set disallowAutomaticSingleRunInference, or they pass locally and fail under CI=true | 2026-11-11 | consider a shared ESLint helper for tooling tests (PC-F19) |
| client core-ui tm-grid-clipboard.spec.ts "menu Paste reads the async clipboard and drives the same ladder" (CI ef67f47: expected 'Alpha' to be 'MenuPasted'; no client change since the previous green sha) | 2026-11-11 | timing-dependent (turn-capped wait vs real Blob read); PC-F18 |
| client grid @perf scroll probe (nightly; long tasks > 50 ms on flat grid and tree) | 2026-11-11 | @quarantine; PC-F17 |
| dotnet Tellma.Core.Email.Tests host failed to launch in a solution-wide unit run under load (xUnit "Test process did not return valid JSON", 0 tests run; passed 64/5-skipped alone; PC-010 integrate) | 2026-11-11 | watch |
| dotnet Tellma.Identity.E2E InProcPasskeyTests.Register_a_passkey_then_sign_in_with_it_under_the_path_base (PasskeyCeremonies.RegisterThenSignInAsync waits 5 s for the conditional ceremony, then clicks the passkey button unconditionally; Windows under load, PC-014 impl) | 2026-11-11 | watch; fix with PC-F16 |
| dotnet Tellma.Core.Tests FileSystemBlobStoreTests.Objects_land_under_the_tenant_kind_and_fan_out_directories (.part not found under t7/.tmp; ubuntu CI 42957255864 attempt 1) | 2026-11-11 | PC-F15 |
| dotnet Tellma.Connector.Smtp.Adapter.Tests (SmtpProtocolTests, SmtpConformanceTests, SmtpReconnectTests): SocketException WSALookupServiceEnd (cancelled DNS lookup under load), Windows local, failed once in the solution-wide filtered run (PC-013 integrate) | 2026-11-11 | watch |
| WebKit-only axe color-contrast failure on the plain trigger of the older menu story, dark theme (pre-existing; not in any tier) | 2026-11-11 | watch; found by PC-041 fix1 |
| dotnet Tellma.Core.Tests TenantRegistryTests.A_hung_listener_never_holds_the_refresh_and_times_out_into_the_next_notification (failed once locally on Windows at 7f0462a, PC-060 integrate) | 2026-11-10 | watch; PC-F13 examines the class |
| e2e entity-picker.spec.ts:584 modal round-trips › create page prefills… | 2026-11-08 (hidden by retries:2 before) | fixed by PC-F02 (4c36d45); un-quarantined |
| e2e entity-picker.spec.ts:607 modal round-trips › edit page targets committed id… | 2026-11-08 | fixed by PC-F02; un-quarantined |
| e2e grid-entity.spec.ts:132 unresolved commit text resolves… after editor teardown | 2026-11-08 | fixed by PC-F02; un-quarantined |
| e2e grid-keyboard.spec.ts:241 readonly grid is a single tab stop | 2026-11-08 | fixed by PC-F02; un-quarantined |
| e2e popover.spec.ts:93 block-end/start alignment mirrors under RTL | 2026-11-08 | fixed by PC-F02; un-quarantined |
| dotnet Identity integration Flows.AssurancePolicyTests.Max_age_zero_forces_one_reauthentication_then_completes | 2026-11-08 (PC-002 r2; recurred PC-006 r2: alone 3/3 fail) | root cause: OidcFlowClient.cs:101 WaitForCodeAsync returns the spent first code; fixed in PC-006 fix2 (CountFor + WaitForCodeAfterAsync) |
| dotnet Smtp connector integration: port-in-use (local only, passed 3/3 on rerun) | 2026-11-08 (PC-002 fix1) | watch; quarantine if it recurs on CI |
| dotnet EF integration CREATE DATABASE "Execution Timeout Expired" (ApplyMigrationsTests parity, MemoryOptimizedTests) under parallel-slot load; green on rerun | 2026-11-08 (PC-005 fix1; PC-015 saw an EF load timeout too) | watch; host load, not code. If it hits CI, raise the fixture's CREATE DATABASE timeout |
| e2e grid-touch.spec.ts:206 double-tap opens the editor (touch) | 2026-11-08 (PC-F03 fix1, local) | PC-F05 |
| e2e heavy axe scans time out at 30 s under parallel-slot CPU load (entity-picker popup states, grid-entity axe & RTL dropdown open (dark), grid-a11y-editable populated); pass alone | 2026-11-08 (PC-008 impl, PC-F03 fix1; also PC-F02) | watch; local load only so far. If CI shows it, raise those tests' timeout with a reason or split the scan |
| e2e tooltip "the wrapper pattern gives disabled controls a working tooltip" | 2026-11-08 (PC-F05 stress: 1/2260 at 10 workers, not reproduced in 2860 more) | watch; track if CI shows it |
| e2e grid-menu.spec.ts:131 a 3-row selection pluralizes the row items | 2026-11-08 (CI 42532168842 and the 35f367b run) | PC-F07 |
| e2e tree-grid lazy loading in-flight spinner tests (2/20 under stress: load landed before the spinner assertion) | 2026-11-08 (PC-F07 stress) | fixed in PC-F07 (held-response loader gate in the showcase story) |
| e2e grid-clipboard real-clipboard test timed out 2/20 locally (Windows cross-worker clipboard lock) | 2026-11-08 (PC-F07, local only) | known local-Windows limitation (PROCESS §11); watch on CI |
| dotnet Tellma.Core.Testing.Tests runner start glitch ("Test process did not return valid JSON") in the solution-wide unit filter under load; alone 39/39 | 2026-11-09 (PC-006 integrate) | watch; host load |
| dotnet Identity integration RefreshTokenTests.A_malformed_pushed_allow_list_is_rejected_rather_than_ignored (67 s, failed once under host load; rerun 140/140) | 2026-11-09 (PC-016 integrate) | watch; load-induced (identity suite) |
| e2e image.spec.ts: 2 misses in a loaded 10-minute run (code untouched), then 33/33 at repeat-each 3 | 2026-11-09 (PC-011 fix1) | watch; possible load flake |
| client lint:test mcp-server.spec 60 s beforeAll timeout on a clean checkout (reproduced twice locally, also on the parent commit) | 2026-11-09 (PC-065 r1) | watch; pre-existing, cold-start cost |
| dotnet Imaging suite runtime ~3 min on Windows (≈150 worker starts) after PC-F08 | 2026-11-09 | watch against CI build-test's 20-minute timeout |
| dotnet Identity integration InvitationDeliveryStatusApiTests.A_delivered_invitation_reports_what_the_provider_said (failed once in a merged full run; 6/6 alone). Cause: test mutates the row before EmailDispatchRecorder writes SentUtc | 2026-11-09 (PC-009 integrate) | watch; fix = wait for SentUtc before mutating; PC-Fnn if CI hits it |
| local machine overload: PR-tier e2e 465/478 (timeouts) and showcase esbuild service stopping during PC-F06 with 5 lanes + reviewers | 2026-11-08 19:00-21:00 | D-028: cap concurrent heavy agents |
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
| PC-003 | 0010-f1 | 1 | done | bfc40a1, b780419, de5c585, 68ddcc0 (pc) | 24.89M | Queryex fixture on DatabaseCollations.Platform (D-015 done); CI 42488163369 green |
| PC-004 | 0023-a | 1 | done | cd25a67, d532bfa, 681f7f8 (pc) | 14.67M | CI 42484063555 green |
| PC-005 | 0010-g1 | 1 | done | 6b6f48f, e33ceb3 (pc, ff) | 4.23M | 2nd MigrationsHost migration AddSubjectsList; Msg 468 seam stays open for 0012 (StringList has no collation) |
| PC-006 | 0010-a2 | 1 | done | e99eb6f, 1991dc8, cade933, da3b766 (pc) | 23.84M | 3 deviations; ruling: counter counts every call incl. 401/403 (spec stands) |
| PC-007 | 0010-f2 | 1 | done | 66fa627, 334d4c7, 7f25a25, a6c928d (pc) | 24.07M | red run was the flaky grid-menu test, fixed by PC-F07; green at 4220487; 3 deviations; Exists not in Paths (D-027) |
| PC-008 | 0010-b1 | 1 | done | 2d7734a, 2161a0b, aa8686e, 0b64145 (pc) | 21.50M | CI 42509964842 red (CSP leak) → fixed by PC-F06; green at c71a009 | 8 deviations; primary ceiling 8 not 7; in-house eval-free ICU; client ICU has no date/time |
| PC-009 | 0010-a3 | 1 | done | 42c5e6a, 372c259, 00e104c, 8fc45b6, 3ec00db, c9d40fc (pc) | 31.38M | CI 42601509854 red: identity-e2e consent screen → PC-F12; done with PC-F12 at f2b1591, CI 42608310703 green; 13 deviations; D-034 |
| PC-010 | 0010-f3 | 1 | done | 36c28a9, 09fffb8, 713ef92, 63c64cc (pc) | 35.89M (impl+fix+int 30.29 fable, r1 2.01, r2 3.59 Opus xhigh) | CI 43015763392 green; Email.Tests host failed to launch once in the merged-tree unit run (xUnit "did not return valid JSON", load), passed alone |
| PC-011 | 0010-d1 | 1 | done | d35f4fc, 672b257, 05cd337 (pc) | 20.20M | ./popover ceiling 4→4.5 KB under D-031 | | 9 deviations; budgets: popover 9 B, grid 3 B, image 53 B left (D-031) |
| PC-012 | 0010-f5 | 1 | done | 3737de4, f756a00, ba2fefa, 7db93ca (pc) | 27.29M (impl+fix+int 21.32 Opus, r1 2.75, r2 3.22) | D-050: end check outside the seek; OPTION (RECOMPILE) on hoisted uq variables (fix1); CI 43103329956 green; 1 permission block (force-with-lease push refused; retried plain) notified |
| PC-013 | 0010-d2 | 1 | done | 4925846, 2a90ae2, 09ab023 (pc) | 21.47M (impl 12.64, r1 4.92, fix+int ≈ 3.9+; fable) | CI 42957255864 red on attempt 1 (FileSystemBlobStoreTests, flaky, PC-F15), green on rerun; integrated 09ab023 (CI 42957255864 red: ubuntu unit FileSystemBlobStoreTests, PC-F15; failed job rerun requested; fix1 2a90ae2; R1 0/1/3; impl 4925846; 16 deviations, see reviews/PC-013.md; budgets ./l10n 8→10, ./grid 38→38.5 under D-031) |
| PC-014 | 0010-a5 | 1 | done | 5d356b3, eebab50, 5bf6602, fc941ca (pc) | 20.99M (impl+fix+int 17.44 fable, r1 1.16, r2 2.39 Opus xhigh) | CI 42999471991 green; passkey E2E helper race and the two un-prefixed identity paths stay with PC-F16 |
| PC-015 | SEED | 1 | done | ca3759b, 0ebb7fc, 8d53a8c, 0a35bb9 (pc) | 35.86M | red only from the PC-008 CSP leak; green at c71a009 | 11 deviations; D-020 rulings + planner notes PC-018/032/035/039/090 |
| PC-016 | 0013-a | 1 | done | 2cf9477, 8210754, cb3438f, 0492c35 (pc) | 24.53M | red run was the host-ICU test, fixed by PC-F09; green at f8d5130; 13 deviations; D-029 (Umm al-Qura authority, host-independent server text) |
| PC-017 | 0017-a | 1 | done | e1133a6, 0c184db, 2193781, 65ce1d1 (pc) | 12.83M | 12 deviations; MaxSizeBytes narrow-only (D-022); Linux create-only via link(2) (D-024); no R2 (3 majors, no blocking) |
| PC-018 | 0012-a | 1 | done | 7c001cb, 05131bc, a996caa (pc) | 27.44M | 14 deviations; planner notes PC-033/034/063 |
| PC-019 | 0013-b | 1 | done | 6a36b74, 7f9bee7, b22322e, 97330a0 (pc) | 22.56M (impl+fix+int 20.97 fable, r1 1.59 Opus) | CI 42992138704 green (Linux pinned-ICU leg passed); Identity integration suite not re-run after the last two merges (no .NET input changed); Linux leg now requires pinned ICU 78.2 (TELLMA_REQUIRE_PINNED_ICU=1) |
| PC-020 | 0023-b | 1 | done | 184c8c2, 061247d, 5d9ccc2 (pc) | 22.60M + r1 (impl+fix+int Opus; D-049 budgets) | CI 43117918168 green; three merges of platform-core; e2e 705/705 on 2ca1d86 |
| PC-021 | 0012-b | 1 | done | 81f3ea1, 5b20cd2, d554208 (pc) | 20.89M (impl 18.41, r1 2.48) | green at 8043295, CI 42947124853; 11 deviations; D-037 |
| PC-022 | 0013-c | 1 | done | 897f7fc, 3f2b60c, ef67f47 (pc) | 9.10M (impl+fix+int 7.67, r1 1.43; Opus) | CI 43026874374 red (client unit: tm-grid-clipboard.spec.ts menu Paste, expected 'Alpha' to be 'MenuPasted'; no client change since green 63c64cc) → re-run green (attempt 2); 364bcc2 green; RequestContext.Localization (zone resolved once per process, UTC fallback); ApplyCultures applies LocalizationScope; added Queryex_QX4009 strings |
| PC-023 | 0023-c | 1 | done (CI 43221550998 green; integrated 4e5d705; golden re-approved from a clean build 6a32a52; '.' 8.71/8.81, chip 5.65/5.75, toast 5.58/5.7, banner 2.48/2.6, busy-overlay 2.39/2.5; e2e 837/837) | 83d81bb, c224a4e, b44e9a5, 76d2f23, 6a32a52, 4e5d705 (pc) | 20.51M (impl+fix+int 18.45, r1 2.06) |  |
| PC-024 | 0011-b | 1 | done | 5d367f3, e5e0155, 630bf26 (pc) | 25.08M | CI red from the PC-F12 consent test; done with PC-F12 at f2b1591, CI 42608310703 green; 16 deviations; D-036 |
| PC-025 | 0011-c | 1 | done | ef6a61c, 3c46607, 3db32cd (pc) | 22.05M (impl+fix+int 20.60 fable, r1 1.45 Opus) | CI 43007537552 green |
| PC-026 | 0023-d | 1 | done (integrated 89c4708; CI 43372513599 red from its own splitter flake, fixed by PC-F27; green at c3103a7, CI 43388722956) | 65a5310, a486a58, 064e666, 89c4708 (pc) | 17.23M (impl+fix 14.97, r1 2.26) |  |
| PC-027 | 0012-c1 | 1 | done | 7a4fc5e, 3e11619, d2ccf81, 4a8712d (pc) | 18.99M + r1 (impl+fix+int 16.31 Opus, r2 2.68; r1 id not recorded) | CI 43091104900 green; dedup per write epoch (merge-time task); SqlServerFixture consolidated; 1 permission block (process listing) notified |
| PC-028 | 0012-c2 | 1 | done (17dedec, CI 43148071042 green) | c56c48a, 675cbb1, 9b5727e, 17dedec (pc) | 21.15M (impl+fix1+fix2 16.54, r1 2.14, r2 2.47) | D-056 |
| PC-029 | 0014-a | 1 | done (1816210, CI 43152469396 green) | c3bce14, 56bd70b, 1816210 (pc) | 14.39M (impl+fix1+int 12.24, r1 2.15) | merge with PC-028 needed a semantic fix to DataBatchDatabase (seeded caller id above the reserved band; reset keeps ids ≤ 999) |
| PC-030 | 0023-e | 1 | impl | | | |
| PC-031 | 0023-f | 1 | todo | | | |
| PC-032 | 0011-e | 1 | done | 521655a, 427c7c1, 5067600, aad2e61 (pc) | 26.64M (impl+fix+int 19.65 Opus, r1 3.42, r2 3.57 Fable) | CI 43109655216 green; 22 deviations; compressed/fingerprinted index.html all get CSP; Hub group requires HubMetadata (MapHub only) |
| PC-033 | 0012-d | 1 | done (CI 43314555760 green; 6488ee1) | f156e3d, 5f0a6e0, 107225d, 6488ee1 (pc) | 27.99M (impl 22.01, r1 2.87, r2 3.11) |  |
| PC-034 | 0011-d | 1 | done (CI 43298198316 green; integrated 4fb84e2; commit 3 77389f9; steps keyed by name through registrations with factories; generic steps closed over the leaf) | 2cbb909, 074a856, 77389f9, 91886ef, c1817dc, 4fb84e2 (pc) | 24.18M (impl+fix+int 19.11, r1 1.77, r2 3.30) |  |
| PC-035 | 0021-a | 1 | done (CI 43284321336 green; integrated d0093fc; fast-forward; new deviation 12: IDataBatch.IsComposing) | c49c44f, d0093fc (pc) | 11.79M (impl+fix+int 9.64, r1 2.15) |  |
| PC-036 | 0020-a | 1 | done (CI 43325841371 red on ddb8259 from a pre-existing grid flake, fixed by PC-F25; green at f9b7e33, CI 43352828665) | 2e5de44, 60bff60, ddb8259 (pc) | 9.52M (impl+fix 8.14, r1 1.38) |  |
| PC-037 | 0012-e | 1 | done (integrated 620e2c9, CI 43438810750 green) | 79c94a2, 30c3508, e09c4db, 0b683ab, 491773d, 63479b9, 620e2c9 (pc) |  |  |
| PC-038 | 0013-d | 1 | done (integrated c18780c, CI 43391353328 green) | 57575e0, b3ac84e, ac5c5b6, 9e82c32, c18780c (pc) |  |  |
| PC-039 | 0014-b | 1 | done (integrated db62c09, CI 43447738414 green) | be9c828, 8447934, bcb367c, c251cc7, 609ee5e, db62c09 (pc) |  |  |
| PC-040 | 0012-g | 1 | todo | | | |
| PC-041 | 0010-c1 | 1 | done | 143f3df, 3cd84d0 (pc) | impl 16.34M (fable) | 3cd84d0, CI 42968324773 green; gates on d5987b1 (3cd84d0 adds only plans/), e2e 629/629; integrate (fix1 76bf308; R1 0/2/8; impl 143f3df; 16 deviations; ./menu ceiling 8.5 vs §C.11's 7, D-031 ruling after R1) |
| PC-042 | 0012-f | 1 | todo | | | |
| PC-043 | 0014-c | 1 | impl | | | |
| PC-044 | 0013-e | 1 | todo | | | |
| PC-045 | 0010-c2 | 1 | done | 761c33a, 8c6809f, c6807ad, 9299989 (pc) | 20.29M (impl+fix+int 18.53, r1 1.76; Opus) | CI 43039915850 red (client-windows: new no-platform-sniffing scope test fails on Windows → PC-F19; Linux jobs: Ubuntu mirror DNS outage installing Chromium); type-to-edit only on a typed character (no Ctrl/Alt/Meta; AltGraph off Apple; Option without Cmd/Ctrl on Apple); @perf probe @quarantine (PC-F17); CI 43039915850 red (PC-F19), green at 67fa80d (43056016205) |
| PC-046 | 0013-g | 1 | todo | | | |
| PC-047 | 0011-f | 1 | done (integrated e4d093c, CI 43396977726 green); its TimerTests reconciliation test later went flaky at 5c3674f and 9f9adb7 (PC-F28) | 90931cd, 5b1ef79, 48d00f6, d732283, e4d093c (pc) |  |  |
| PC-048 | 0010-c3 | 1 | done (CI 43210943912 green at a77ee57 after PC-F22; integrated 9007c81) | eb03017, f6d0487, 9007c81 (pc) | 15.74M (impl+fix1+int 13.43, r1 2.31) | R1 0/0/8: 6 fixed, R1-5/R1-6 pushed back to Ahmad (D-057); ./grid 42.00/42 KB, '.' 8.61 (ceiling 8.71); e2e 779/779 |
| PC-049 | 0013-f | 1 | todo | | | |
| PC-050 | 0014-d | 1 | todo | | | |
| PC-051 | 0010-c4 | 1 | done (integrated e65654e, CI 43456272658 green; ./grid 40.42 of 40.51, 1.58 KB under 42 for PC-054; e2e 924) | 6927550, 2dfda91, 6ebe667, 7f49700, e927e10, e65654e (pc) |  |  |
| PC-052 | 0014-e | 1 | todo | | | |
| PC-053 | 0017-b | 1 | done | 7f6643a, 8c01d52, 4dd6091, 8a27e10, 81a154f, 63f0394 (pc) | 27.83M | 11 deviations; D-030; ESCALATED to R2 (2 blocking) |
| PC-054 | 0010-c5 | 1 | impl | | | |
| PC-055 | 0018-a | 1 | todo | | | |
| PC-056 | 0010-c6 | 1 | todo | | | |
| PC-057 | 0010-e1 | 1 | done (integrated cc60f8e, CI 43477397796 green; linked ceilings entity-picker 10.98 (10.88), grid 40.77 (40.68; 1.32 KB left under 42 for PC-054), grid-editing 5.67 (5.58); e2e 951) | 86eb8cf, f74dc2d, 13a04b1, cc60f8e (pc) |  |  |
| PC-058 | 0010-e2 | 1 | todo | | | |
| PC-059 | 0010-d3 | 1 | done (bcb703e, CI 43156693433 green) | c60627e, 89dd3d5, bcb703e (pc) | 17.03M (impl+fix1+int 15.81, r1 1.22) | R1 0/1/1 fixed; e2e 716/716; '.' entry 8.48/8.5 KB |
| PC-060 | 0010-b2 | 1 | done | 8c1dddd, ed9759f, 3d7aa7b (pc) | 15.86M (impl 13.46, r1 2.40) | green at 8043295, CI 42947124853; 7 deviations; ./select 8.28/9 |
| PC-061 | 0023-g | 1 | todo | | | |
| PC-062 | 0015-a | 1 | todo | | | |
| PC-063 | 0015-b | 1 | todo | | | |
| PC-064 | 0016-b | 1 | todo | | | |
| PC-065 | 0024-a | 1 | done | e5ad0db, 1bfcefa, eee616f (pc) | 62.04M | red run fixed by PC-F10; green at b0bb12e; 14 deviations; workspace root moved (D-032) |
| PC-066 | 0015-c | 1 | todo | | | |
| PC-067 | 0024-b | 1 | done | 8a98924, f391cbd (pc, fast-forward) | 12.66M (impl+fix 10.39, r1 2.27; Opus) | CI 42988536575 green |
| PC-068 | 0015-d | 1 | todo | | | |
| PC-069 | 0024-c | 1 | done | f3d5dc9, b72fb2a, 6357ff2, e6d89fa (pc) | 20.68M (impl+fix+int 17.55 Opus, r2 3.13 Fable; r1 in impl figure) | CI 43076418506 green; boot '.' 3,072 B English-only = zero headroom (WATCHLIST ## 0024); merged-tree e2e 611/644 under load, all 33 timeouts passed on rerun |
| PC-070 | 0015-e | 1 | todo | | | |
| PC-071 | 0024-d | 1 | done (279ef75, CI 43168140640 green) | 11c16b1, 9af3158, 8d54380, 279ef75 (pc) | 23.78M (impl+fix1+fix2 16.03, r1 1.64, r2 fable 6.11) | R1 1/0/7, R2 fable 0/0/6, all fixed; 20 deviations (D-054); e2e 726/726; core primary 12.47/12.5 KB |
| PC-072 | 0015-f | 1 | todo | | | |
| PC-073 | 0024-e | 1 | done (integrated 9f9adb7; CI 43408822577 red from PC-047's flaky TimerTests, fixed by PC-F28; green at a533113, CI 43421083528) | 2f3b1b4, 9812a0e, fe8eae5, a7c1051, e2e0665, 9f9adb7 (pc) |  |  |
| PC-074 | 0015-g | 1 | todo | | | |
| PC-075 | 0025-a | 1 | fix2 + integrate | 61c750f, 8f00d42 |  |  |
| PC-076 | 0015-h | 1 | todo | | | |
| PC-077 | 0015-i | 1 | todo | | | |
| PC-078 | 0014-f | 1 | todo | | | |
| PC-079 | 0013-h | 1 | todo | | | |
| PC-080 | 0016-c | 1 | todo | | | |
| PC-081 | 0016-d | 1 | todo | | | |
| PC-082 | 0016-e | 1 | todo | | | |
| PC-083 | 0017-c1 | 1 | todo | | | |
| PC-084 | 0017-c2 | 1 | todo | | | |
| PC-085 | 0011-g | 1 | integrated db81630 (CI pending; fix1 6ba59cb; R1-6 pushed back: AspNetCore→Webhooks edge would break ARCHITECTURE rule 6, IVT seam kept and documented; R1-4 compares runtime models only; new fix: RCSI ALTER WITH ROLLBACK IMMEDIATE (pooled catalog connection timed out Acme readiness); integration-windows non-gating) | a147a95, 6ba59cb, db81630 (pc) |  |  |
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
| PC-097 | 0020-b | 1 | impl | | | |
| PC-098 | 0020-c | 1 | todo | | | |
| PC-099 | 0021-b | 1 | todo | | | |
| PC-100 | 0020-d | 1 | todo | | | |
| PC-101 | 0010-f4 | 1 | done (450f7dd, CI 43164947502 green) | b11e206, c66a4a7, 450f7dd (pc) | 8.94M (impl+fix1+int 7.20, r1 1.74) | R1 0/1/5 all fixed; new deviations: null for an IsNotNull parameter is a caller error; printed text over a parse ceiling reports that ceiling's diagnostic |
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

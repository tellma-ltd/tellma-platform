# Tree rollups in pivots — benchmark (Part B)

Compares the query cost of level navigations (L) and the virtual closure (C), plus a third shape
the results forced into view: grouping by leaf in SQL and rolling up in C# (**Leaf + C#**). Part A
([complexity.md](complexity.md)) covers contracts and code.

## 1. Setup

- **Engine.** SQL Server 2025 Enterprise Developer (17.0.4045.5) in Docker on a laptop (i7-1370P,
  container capped at 8 CPUs and 12 GB). `MAXDOP 4` at database scope, roughly one database's
  share of an elastic pool. Compatibility level 170, so batch mode on rowstore is available.
- **Schema.** Spec 0012's tree shape: `Node hierarchyid` of the id path, `UX_<T>_Node`,
  `IX_<T>_ParentId`. Facts in `dbo.Lines` (about 100 bytes a row): one year of dates, 2% with no
  center. Every fact sits on a leaf, which is the worst case for fan-out on uniform trees.
- **Measurement.** Warm cache. One warm-up run, then the median of 5 runs (1M) or 3 runs (10M),
  timed at the client with all rows drained. CPU, memory grant and tempdb come from Query Store.
  The slow fact-grain variants ran once. Scripts are in [bench/](bench/): `00-setup.sql`, `gen.py`,
  `run.ps1`, `rollup.cs`. Raw data is in `results-1m.csv` and `results-10m.csv`.
- **Correctness.** At 100K facts, every variant was decoded into subtree totals per (row member,
  column member) and compared with a brute-force `IsDescendantOf` ground truth. That covers the
  grand totals, the (None) member, and ragged trees. **All matched exactly**, including the
  two-tree four-variant twins. L6 differs only by the members below its cap, as it should. The
  C# rollup's cell counts match Cpre's row counts exactly.

### Shapes

| Tree | Depth | Members | Note |
|---|---|---|---|
| D2 | 2 | 1,640 | shallow and wide |
| D4 | 4 | 1,554 | |
| D6 | 6 | 1,092 | |
| D8 | 8 | 510 | deeper than 0033's default of 6 |
| R8 | ≤ 8 | ~1,240 | ragged: leaves at mixed depths |
| W5 | 5 | 37,448 | many members (customers, a large chart) |
| T3 × accounts | 3 × 5 | 39 × 1,364 | two trees |
| B6 × accounts | 6 × 5 | 126 × 1,364 | two deeper trees |

Columns: none, `quarter` (4 members), or the accounts tree. Each shape ran at 1M and at 10M facts.

### Variants

| Name | Emission |
|---|---|
| **L** | 0010 §F.9 as specced: level joins off the fact's center, `GROUPING SETS` at fact grain |
| Ldim | L, with the level joins computed once per member in a derived table that the facts hash-join |
| **Lpre** | L over a pre-aggregation: facts grouped by (leaf ids, column keys) first, grouping sets on top |
| L6pre | Lpre capped at 6 levels |
| **C** | closure at fact grain (`CROSS APPLY` k < `GetLevel()` per fact) |
| Cdim / Cmat | closure per member (derived table) / through a materialized closure table |
| **Cpre** | C over the same pre-aggregation |
| `…NL` | the same, without the label `MAX(...)` columns (labels looked up afterwards) |
| **Leaf** | plain `GROUP BY CenterId, <column key>`: the SQL half of Leaf + C# |

## 2. Results (median seconds)

| Scenario | L | Ldim | Lpre | LpreNL | L6pre | C | Cdim | Cmat | Cpre | CpreNL | Leaf | Leaf + C# |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| D2-1M-none | 27.3 | 2.96 | 0.18 | 0.13 | | 32.0 | 3.62 | 3.48 | 0.18 | 0.15 | 0.09 | |
| D2-1M-quarter | 31.3 | 3.33 | 0.64 | 0.62 | | 36.7 | 4.20 | 4.15 | 0.87 | 0.80 | 0.18 | |
| D4-1M-none | 133.6 | 4.88 | 0.15 | 0.13 | | 46.0 | 6.45 | 6.35 | 0.17 | 0.14 | 0.07 | |
| D4-1M-quarter | 47.1 | 5.04 | 0.67 | 0.64 | | 48.1 | 9.57 | 9.00 | 0.75 | 0.73 | 0.16 | |
| D6-1M-none | 94.9 | 8.93 | 0.18 | 0.13 | | 48.8 | 10.3 | 10.5 | 0.18 | 0.12 | 0.08 | |
| D6-1M-quarter | 94.4 | 11.6 | 1.83 | 0.47 | | 69.4 | 16.6 | 15.4 | 1.01 | 0.62 | 0.16 | |
| D8-1M-none | 69.3 | 5.71 | 0.17 | 0.10 | 0.11 | 75.4 | 5.66 | 5.22 | 0.13 | 0.12 | 0.06 | |
| D8-1M-quarter | 145.2 | 5.09 | 1.02 | 0.98 | 1.33 | 75.7 | 8.14 | 8.29 | 0.67 | 0.43 | 0.12 | |
| R8-1M-none | 74.6 | 5.64 | 0.34 | 0.18 | 0.15 | 70.8 | 10.5 | 10.2 | 0.25 | 0.15 | 0.08 | |
| R8-1M-quarter | 164.3 | 5.37 | 1.47 | 0.69 | 1.22 | 78.4 | 19.7 | 19.9 | 0.88 | 0.58 | 0.17 | |
| W5-1M-none | 71.8 | 7.64 | 2.90 | 1.47 | | 74.2 | 13.5 | 5.80 | 3.20 | 2.02 | 0.49 | |
| W5-1M-quarter | 126.5 | 18.0 | 14.4 | 6.11 | | 75.6 | 20.8 | 18.6 | 10.9 | 9.32 | 1.74 | |
| T3-1M-accounts | 168.7 | 12.4 | 6.75 | 2.04 | | 457.8 | 65.1 | 58.2 | 10.8 | 7.37 | 0.46 | |
| B6-1M-accounts | 63.5 | 18.1 | 6.75 | 4.66 | | 559.0 | 123.0 | 234.7 | 15.0 | 22.4 | 0.37 | |
| D2-10M-none | | | 0.55 | 0.68 | | | | | 0.93 | 0.74 | 0.62 | |
| D2-10M-quarter | | | 3.45 | 3.95 | | | | | 3.61 | 3.50 | 1.03 | |
| D4-10M-none | | | 0.62 | 0.78 | | | | | 0.78 | 0.74 | 0.65 | |
| D4-10M-quarter | | | 3.68 | 3.30 | | | | | 3.60 | 3.74 | 0.98 | **0.90** |
| D8-10M-none | | | 0.61 | 0.71 | 0.73 | | | | 0.81 | 0.77 | 0.55 | |
| D8-10M-quarter | | | 3.02 | 3.05 | 3.07 | | | | 2.91 | 2.86 | 0.92 | |
| R8-10M-none | | | 0.63 | 0.66 | 0.70 | | | | 0.75 | 0.81 | 0.61 | |
| R8-10M-quarter | | | 3.50 | 4.89 | 3.28 | | | | 3.42 | 3.33 | 0.88 | |
| W5-10M-none | | | 3.63 | 2.23 | | | | | 3.60 | 4.70 | 0.92 | |
| W5-10M-quarter | | | 18.2 | 8.55 | | | | | 15.9 | 20.9 | 2.58 | **1.77** |
| T3-10M-accounts | | | 11.1 | 9.22 | | | | | 12.5 | 16.7 | 1.02 | **0.82** |
| B6-10M-accounts | | | 15.5 | 8.02 | | | | | 43.7 | 39.9 | 2.31 | **2.06** |

**Leaf + C#** is end to end: the leaf SQL, the member trees (id, parent), and the in-memory rollup
into every (row ancestor or ALL) × (column ancestor or ALL) cell, with four accumulators for the
self twins. The rollup alone took 8 ms (D4-quarter), 78 ms (T3), 298 ms (W5-quarter) and 786 ms
(B6).

**Leaf + C# is lower than Leaf** because the two columns come from different harnesses. The
`run.ps1` columns drain every row through an interpreted PowerShell loop, which costs about
10–15 µs a row: 1.0–1.3 s on W5-quarter and B6, where Query Store's server durations for Leaf are
1.25 s and 1.31 s. `rollup.cs` reads with compiled code, and its SQL time matches Leaf's server
duration. The overhead also inflates the grouping-sets variants, which return more rows. Comparing
server durations alone (Lpre / Cpre / Leaf, 10M): W5-quarter 20.9 / 15.4 / 1.25 s, B6 15.6 / 44.9 /
1.31 s, T3 10.8 / 12.3 / 0.73 s. That gives the same ranking and margins. Differences under about
30% are within this setup's noise.

**Memory grants.** Lpre and Cpre reached the container's per-query cap of about 1.5 GB on the
large shapes (W5, B6, and D8/R8 with columns) and spilled. Leaf used 78–105 MB everywhere. The
fact-grain variants spilled up to 4.9 GB (Ldim) and 23 GB (Cmat, T3) to tempdb.

## 3. Findings

1. **0010 §F.9's emission, as specced, is unusable at fact grain: 27–169 s at 1M facts.**
   `hierarchyid` methods are CLR calls, and the specced join calls `GetLevel()`/`GetAncestor()`
   on every fact row, for every level. The naive closure is the same, worse with two trees (458–559 s).
   Whichever approach is chosen, the engine must not evaluate `hierarchyid` per fact.
2. **`GROUPING SETS` over fact rows costs 3–20 s at 1M, whichever approach is used.** This is
   Ldim, Cdim and Cmat. The plan for grouping sets is a Sort feeding a **row-mode** Stream
   Aggregate, and the sort spills, while a plain `GROUP BY` stays a batch-mode hash aggregate.
   Pre-aggregating to leaf grain first (Lpre, Cpre) is **10–30× faster**. That is an emission rule
   for 0010 §F.8 itself, independent of the tree decision. A materialized closure table (Cmat) buys
   nothing over the virtual one (Cdim).
3. **Once pre-aggregated, single-tree L and C perform the same** at every depth, at both sizes and
   with ragged trees: Lpre ≈ Cpre within noise, and each wins some scenarios. Neither the depth nor
   the fan-out tips the scale, because the fan-out now applies to distinct leaf combinations
   (thousands), not to facts. At 10M a single-tree pivot takes 0.6–0.9 s with no columns, about
   3–3.7 s with quarter columns, and 16–18 s for the 37K-member tree.
4. **With two trees, C falls behind.** Its fan-out multiplies (depth × depth: 15 for T3, 30 for
   B6). Cpre is 1.1× slower than Lpre on T3 and **2.8× slower on B6 at 10M** (43.7 s vs 15.5 s).
   Without labels the gap is up to 4.8× (B6 at 1M).
5. **The depth cap buys nothing.** L6pre ≈ Lpre on D8 and R8 at both sizes. With pre-aggregation,
   a deeper tree costs almost nothing in either approach.
6. **Carrying labels through the grouping sets costs up to 2× on large outputs.** Dropping them
   took Lpre from 18.2 s to 8.6 s on W5-quarter and from 15.5 s to 8.0 s on B6, and cut the memory
   grants 2–4×. It changed little on small outputs.
7. **Leaf + C# is the fastest shape everywhere, by 4–21× on the hard ones.** It stays at 0.8–2.1 s
   at 10M on the four worst shapes, against 11–44 s for the best SQL grouping-sets variant. On easy
   shapes it is within noise of Lpre/Cpre (0.6 s against 0.6–0.9 s). The reasons:
   - The only fact scan is a plain batch-mode `GROUP BY`.
   - The rollup runs over leaf rows, which are never more than the output cells.
   - Its memory grant is constant at about 80–100 MB, which matters most on an elastic pool, where
     a database gets less memory than this container.

### Tipping points

| Situation | Fastest SQL grouping-sets shape | Margin to Leaf + C# |
|---|---|---|
| One tree, small output (< ~10K cells) | L ≈ C (pre-aggregated) | ≈ equal at 10M with no columns; Leaf 3–4× faster with columns |
| One tree, large output (~190K cells) | L ≈ C, both slow (16–18 s at 10M) | Leaf + C# ~9× faster |
| Two trees | **L**, by 1.1× (shallow) to 2.8× (deeper) | Leaf + C# 13–21× faster than L |
| Facts not pre-aggregated | neither (3–560 s) | n/a: pre-aggregation is mandatory |

## 4. Implications for the decision

- **Performance no longer separates L and C for single-tree pivots.** It favours L for two trees,
  but only when the SQL computes the grouping sets.
- **Both SQL approaches need a new engine emission**: pre-aggregation below the grouping sets
  (eager aggregation), with measures decomposed (`avg` into sum and count). This adds to Part A's
  internal-complexity column for **both** L and C. The engine's twin rule also changes: under
  pre-aggregation a masked `AVG` is no longer exact, unlike Part A §2.1.
- **Leaf + C# is the fastest and adds no engine contract** (Part A §4). It needs only today's
  plain `GROUP BY`, gives full depth, and keeps fan-out out of SQL and out of the language. The
  same rollup computes every non-tree subtotal too. Since every Queryex aggregate decomposes
  (`count`, `sum`, `min`, `max`, and `avg` as sum and count), **pivots would not need 0010 §F.8's
  grouping sets at all**. That would cut F.8 and F.9 together. It flips if a non-decomposable
  aggregate (count distinct, median, percentile) ever joins the language. 0033 decision 6 forbids
  aggregation in the *client*, not in the server's cube builder (Part A §4).
- **Cost of Leaf + C#:**
  - The cube builder owns the rollup, `Having` on the finest set, and the twins.
  - `MaxPivotRows` now caps leaf rows rather than output rows. Leaf rows are never more than
    output cells, so the cap stays at least as tight.
  - Labels need the member trees (id, parent, labels), which the cache or the same batch can supply.

## 5. Caveats

- One laptop container, not Azure SQL. Azure's I/O, memory per database and vCore speed differ.
  The relative findings (CLR per row, row-mode stream aggregates for grouping sets, constant
  memory for Leaf) are engine behaviour and should carry over; the absolute times will not.
- Warm cache, no filter selectivity, uniform hashing of facts to leaves (real ledgers are skewed,
  which shrinks the number of distinct leaf combinations and favours the pre-aggregated shapes),
  only `sum` and `count` measures, no `Having`.
- 3–5 runs per point. Differences under about 30% are noise. Some plans chose DOP 1 (e.g. Lpre on
  D6/D8/R8-quarter at 1M), which accounts for several of the single-tree swings.
- The C# rollup used `decimal` and four accumulators, one thread. It did not build the cube's
  member objects or labels; that cost is proportional to the output in every approach.

## 6. Reproducing

```bash
pwsh .tmp/tree-rollup-analysis/bench/run.ps1 -Plan validate -Setup   # recreate DB, validate at 100K
pwsh .tmp/tree-rollup-analysis/bench/run.ps1 -Plan 1m                # timings at 1M
pwsh .tmp/tree-rollup-analysis/bench/run.ps1 -Plan 10m               # pre-aggregated + Leaf at 10M
```

`rollup.cs` must run outside the repo, because Central Package Management rejects its inline
package version: `dotnet run -c Release rollup.cs -- accounts 5`. The container is
`tellma-tree-bench` on port 14339.

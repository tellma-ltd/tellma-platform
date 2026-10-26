# `'uq'` emission forms — benchmark (spec 0010 Part F, review flag 4)

SQL Server 2025 Developer in Docker (8 CPUs, 12 GB, `MAXDOP 4`). `dbo.Lines` has 10M rows, uniform
over 2016-01-01 .. 2025-06-15, with "today" = 2025-06-15. `dbo.CalendarMonths` is generated from
.NET `UmAlQuraCalendar` (1318–1500 AH) with §F.13's two unique indexes. Each result is the median
of 7 runs after a warm-up; duration and CPU come from Query Store. The checksums are identical
across S, P, V, VR and A in every cell. Raw data is in `results.csv`; scripts are `00-setup.sql`
and `uq.ps1`.

## Query and variants

`SELECT CustomerId, SUM(Amount), COUNT_BIG(*) FROM Lines WHERE PostingDate >= <X> GROUP BY CustomerId`,
with `<X>` = `startOfMonth(today(), 'uq')` (MTD: 0.37% of rows) or `startOfYear(today(), 'uq')`
(YTD: about 10%, two lookups).

| Variant | `<X>` emitted as |
|---|---|
| **S** | §F.13's scalar form: `(SELECT TOP (1) StartDate … @today …)`, nested for `startOfYear` |
| **P** | the lateral-only alternative's workaround: a parameter default, evaluated first, bound as `@from` |
| V | a prologue variable: `DECLARE @v date = (<lookup>)`, then `>= @v` |
| VR | V with `OPTION (RECOMPILE)` |
| A | an uncorrelated `OUTER APPLY` lookup |
| G | reference only: Gregorian `startOfMonth`/`startOfYear` (algorithmic, so slightly different rows) |

Index phases: **cov** = `IX(PostingDate) INCLUDE (CustomerId, Amount)`, **nc** =
`IX(PostingDate)` (not covering), **none** = no date index.

## Results: server duration / CPU (ms)

| Phase | Range | S | P | V | VR | A | G |
|---|---|---|---|---|---|---|---|
| cov | MTD | 59 / 56 | 53 / 53 | 66 / 112 | 51 / 51 | 58 / 58 | 42 / 42 |
| cov | YTD | 425 / 475 | 204 / 504 | 197 / 506 | 192 / 497 | 440 / 492 | 169 / 161 |
| nc | MTD | **405 / 494** | 685 / 2,479 | 661 / 2,363 | 626 / 2,308 | 429 / 522 | 626 / 2,291 |
| nc | YTD | **4,303 / 10,758** | 567 / 1,808 | 630 / 2,021 | 533 / 1,836 | 4,153 / 10,807 | 836 / 2,713 |
| none | MTD | **852 / 3,207** | 438 / 1,553 | 516 / 1,812 | 724 / 2,517 | 1,478 / 4,619 | 382 / 1,396 |
| none | YTD | **941 / 3,364** | 618 / 2,010 | 597 / 1,989 | 564 / 1,971 | 1,029 / 3,621 | 593 / 1,912 |

**Aggregated operand.** `month(max(PostingDate), 'uq')` per customer (50K groups, a full scan)
added 0–21% over the same query without it: cov 661 → 801 ms, none 836 → 847 ms, nc within noise.

**Constant-statement round trip** (P's extra cost): median 3.4 ms, p90 5.7 ms, over Docker on
Windows. Expect lower in an Azure region.

## Why S loses when the plan scans

The scalar subquery runs **once**: its estimated executions are 1. But it reaches the fact rows
through a **row-mode Nested Loops** join, with the lookup on the outer side and `Lines` on the
inner side. So:

- **With a seekable index**, the join turns the lookup into a dynamic seek. That is fine (cov), and
  can even beat P (nc-MTD), where the optimizer chose a scan for P.
- **When the plan scans**, the date predicate is evaluated in the row-mode join instead of the
  batch-mode scan, and the estimate is blind (3M or 10M rows). With no index the whole plan drops to
  row mode (none: 1.5–2× slower). The worst case is nc-YTD, where the optimizer scans and filters 10M
  rows in the row-mode join: 4.3 s against 0.57 s (7.6×).
- **A behaves like S**, for the same reason. V and VR keep the predicate in the batch-mode scan
  and perform like P. V's blind estimate only over-parallelizes (cov-MTD, 2× CPU).

## Recommendation

**Emit each operand kind in the form that suits it:**

| Operand | Emission | Why |
|---|---|---|
| Row-grain (`month(Invoice.PostingDate, 'uq')`) | lateral seek, as specced | unchanged; the only form legal in grouping keys |
| Path-free (`startOfMonth(today(), 'uq')`) | **prologue variable**: `DECLARE @qx{b}_v{n} date = (<lookup>)`, compared as a variable | keeps the predicate in the batch-mode scan; performs like a bound parameter (V and VR above), with no extra round trip |
| Aggregated (`month(max(PostingDate), 'uq')`) | scalar subquery, as specced | runs once per group; 0–21% at 50K groups |

**Why not the specced scalar form for path-free operands.** It is up to 7.6× slower when the date
filter cannot seek on an index (nc-YTD: 4.3 s against 0.57 s). Fact tables filtered on a
non-covering or absent date index are common.

**Why not lateral only.** It refuses path-free operands, so relative Hijri dates would exist only
as report parameter defaults. Search-page custom filters and tabs, saved filters and permission
criteria have no parameters, so they could never say "this Hijri month". That is a larger loss
than any cost measured here.

**Cost.** Round 2 had this design and cut it under Q-22 ("not worth two engine amendments"). It
reuses the engine's existing prologue, so it replaces the scalar form's path-free branch rather
than adding a third path. The remaining risk is the variable's blind estimate, which
over-parallelized one cell (cov-MTD: 2× CPU at the same duration). `OPTION (RECOMPILE)` fixes that,
but adds a compile per run, so I would not use it by default.

**Spec changes:**
- §F.13: the emission forms, with path-free operands taking prologue variables.
- Part F decision 13 and review flag 4: rewritten for the three forms, with this benchmark as the
  evidence.
- §F.11: the constant statement keeps the scalar form, since it has no row source to scan.

**Before deciding**, extend the benchmark to the realistic join shape, where lines take
`PostingDate` through the invoice header.

## Caveats

- One query shape, with the date on the fact table. Tellma's lines usually take `PostingDate`
  through the invoice header join, which this does not test.
- Warm cache, uniform dates, one container. Differences under about 20% are noise.

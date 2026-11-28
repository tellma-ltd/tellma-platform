# Queryex 'uq' join-shape benchmark

A one-off measurement, not a test suite. It asks how a path-free Umm al-Qura call should reach a
filter over a large fact table. An example is `startOfMonth(today(), 'uq')` in a filter on
`Invoice.PostingDate`.

Queryex hoists such a call into a variable that is declared ahead of the statement:

```sql
DECLARE @qx0_v0 date = (SELECT [M].[StartDate] FROM ...);
SELECT ... WHERE ([P1].[PostingDate] >= @qx0_v0) ...
```

SQL Server cannot see a variable's value when it compiles the statement. It must guess how many
rows the comparison matches, which it does not have to do for a bound parameter. The benchmark
measures what that guess costs.

## What it measures

The read is a grouped sum over ten million invoice lines, filtered by their header's posting date.
There are a million headers, posted in key order over eleven years. The lower bound is supplied in
four shapes:

| Shape | The lower bound |
|---|---|
| `variable` | The engine's design: a hoisted `DECLARE` initialised by the month lookup. |
| `parameter` | The lookup's answer, bound as a parameter. This is the baseline. |
| `scalar` | The lookup written in place, as an uncorrelated scalar subquery. |
| `recompile` | The hoisted variable with `OPTION (RECOMPILE)`, so the optimizer sees the value. |

Each shape runs against two header date indexes:

- covering: `(PostingDate) INCLUDE (CenterId)`;
- non-covering: `(PostingDate)` alone, so each matching header needs a key lookup.

Each also runs at two selectivities: since the start of the current Hijri month (about 0.4% of the
headers), and since the start of the current Hijri year (about 4%).

Each combination is compiled and warmed once, then timed over seven runs. The reported time is the
median.

The month table holds months from the backend's Hijri style 131 (the tabular Kuwaiti algorithm).
These are not Umm al-Qura months, but they have the same shape, and the benchmark measures the
join, not the calendar.

## Running it

Run it with `sqlcmd` against a disposable SQL Server 2022 or later, on Windows or Linux:

```sh
sqlcmd -S <server> -U <user> -P <password> -C -b -i uq-join-shape.sql -W -s "|"
```

The script creates the `QueryexUqBenchmark` database on its first run and reuses it after that.
The first load takes a few minutes and about 1.5 GB of disk. Drop the database when you are done.

## Recorded result

Recorded on 2026-11-11 on SQL Server 2022 CU25 (16.0.4255.1), in a Linux container on a Windows 11
workstation with a 4 GB memory cap. Times are medians in milliseconds. The ratio compares each
shape with the bound parameter.

| Header index | Since | parameter | variable | scalar | recompile |
|---|---|---|---|---|---|
| covering | month | 21 | 544 (25.7×) | 104 (4.9×) | 42 (2.0×) |
| covering | year | 1,436 | 580 (0.40×) | 1,296 (0.90×) | 621 (0.43×) |
| non-covering | month | 34 | 606 (17.9×) | 137 (4.0×) | 57 (1.7×) |
| non-covering | year | 1,786 | 612 (0.34×) | 4,603 (2.6×) | 1,224 (0.69×) |

These figures come from the second of two full runs. The first run agreed within its noise: the
variable was 10× and 26× the parameter at month selectivity, and 0.45× and 0.56× at year
selectivity.

**Reading.** At month selectivity the variable is 18–26 times slower than a bound parameter, far
beyond the 1.5× threshold. Without the value, SQL Server assumes a fixed share of the rows match.
It then picks a parallel scan of the headers and a hash join over all ten million lines. At year
selectivity the same guess happens to suit the data better than the serial seek plan the parameter
gets, so the variable is faster there. Recompiling recovers most of the selective case. What is
left is mostly compile time, roughly 20 ms a run. The scalar form is worse than the variable for
wide ranges and is not a remedy.

**Verdict.** The hoisted variable exceeds the 1.5× threshold (decision flag 4 of the Queryex
amendments), so the design is escalated rather than kept. The engine was built as specified, with
hoisted variables. Whether to recompile, switch to another shape or keep the variable is the
escalation's decision.

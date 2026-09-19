# Brief: timestamps become `datetimeoffset` / `DateTimeOffset` in one spec

You are converting one spec file under `docs/specs/` from the "UTC stored as `datetime2`"
timestamp convention to the "instant stored as `datetimeoffset`" convention. Spec 0011 §4.1 states
the rule once for the whole family (it is being rewritten in parallel; do not restate the rule in
your spec). Read the whole spec in pages (the Read tool caps around 25k tokens; page with
offset/limit), then convert, then verify.

## The rule you are applying

Every platform timestamp is an instant: stored as `datetimeoffset(n)` with the precision it had as
`datetime2(n)`, written at offset zero from `SYSUTCDATETIME()` (SQL Server converts `datetime2`
to `datetimeoffset` implicitly with offset `+00:00`), carried in C# as `DateTimeOffset`, typed by
Queryex as `DateTimeOffset` (so `now()` compares with it directly and calendar functions need
`local()`). Wire strings are ISO 8601 with the offset.

## What to change

1. **Column tables and DDL fragments.** `datetime2(7)` → `datetimeoffset(7)`, `datetime2(3)` →
   `datetimeoffset(3)`, in every column table, every inline table definition (`dbo.__Tellma…`,
   standalone `[TableType]` shapes such as `IdStampList`), every sketch remark
   (`// datetime2(3)` → `// datetimeoffset(3)`), and every prose mention of a timestamp column's
   type.
2. **C# sketches.** A member, parameter, tuple element or return type `DateTime` / `DateTime?`
   that carries a timestamp becomes `DateTimeOffset` / `DateTimeOffset?`. Arrays `DateTime[]`
   that are Queryex column buffers stay (see exceptions).
3. **SQL statements.** `SYSUTCDATETIME()` stays as the source of every stamp — do not replace it.
   Change only declared *types* that hold stamps: `DECLARE @x datetime2(7)` →
   `DECLARE @x datetimeoffset(7)`; table-variable and TVP column types `datetime2(n)` →
   `datetimeoffset(n)`; `CAST(… AS datetime2(n))` → `CAST(… AS datetimeoffset(n))`. Comparisons
   and `DATEADD` expressions against `SYSUTCDATETIME()` stay as they are (the implicit conversion
   makes them correct).
4. **Prose.** Sentences asserting that timestamps are "UTC `datetime2`", "UTC wall-clock",
   "stored in UTC without an offset" or similar become "instants (`datetimeoffset`, offset zero)".
   A sentence that only says a value is taken "in UTC" from the clock may stay. Remarks such as
   "`datetime2(3)` UTC" become "`datetimeoffset(3)`".
5. **Binder and reader notes.** Where the spec says a stamp binds "with seven fractional digits",
   keep it and add "as `SqlDbType.DateTimeOffset`". Where a reader "reads `DateTime`" for a
   timestamp column, it reads `DateTimeOffset`.
6. **Cronos and schedules (0019).** Next-occurrence computations take and return
   `DateTimeOffset` (`CronExpression.GetNextOccurrence(DateTimeOffset, TimeZoneInfo)`).

## What NOT to change (exceptions)

- **Temporal period columns** `ValidFrom` / `ValidTo` (`GENERATED ALWAYS AS ROW START/END`,
  `PERIOD FOR SYSTEM_TIME`) stay `datetime2(7)`: SQL Server requires `datetime2` for period
  columns. Every column table row and prose mention of them keeps `datetime2(7)`. History tables
  follow.
- **`date` columns, `DateOnly`, `Today`, `today()`, `TimeSpan`, `time` columns, `TimeOnly`** stay.
- **Queryex type-system names** — `QueryColumnKind.DateTime` and `.DateTimeOffset`, `QxDateTime2`,
  `QxDateTimeOffset`, the `GetBuffer` return list (`DateTime[]`, `DateTimeOffset[]`), and any
  sentence describing the engine's date types — stay: a distribution may still map a deliberate
  wall-clock `datetime2` column, and the engine keeps both kinds.
- **Anything already `DateTimeOffset` / `datetimeoffset`** stays.
- **Identity-server text** (spec 0003's tables are not in your file; if quoted, leave it).
- Do not touch any file other than the one spec you were given. Do not add or remove members,
  sections or SQL statements. Do not reflow paragraphs you did not change.

## Mechanics

- Use a Python script or the Edit tool; anchor on exact text; never a blind global replace of
  `DateTime` (it would hit `DateTimeOffset`, the Queryex kinds and `DateOnly`-adjacent prose).
- Keep line endings (LF) and encoding (UTF-8, no BOM).

## Verification before you finish

Run these over the file and fix anything they find, then report the residue:

- `grep -n "datetime2"` — every remaining hit must be a temporal period column
  (`ValidFrom`/`ValidTo`, `GENERATED ALWAYS`, `PERIOD FOR SYSTEM_TIME`), a Queryex store-type
  name, or a deliberate wall-clock column. List each remaining hit with its reason.
- `grep -n "DateTime[^O]"` inside `csharp` fences — every remaining hit must be a Queryex kind or
  buffer. List each with its reason.
- `grep -n -i "utc"` — every remaining hit must be `SYSUTCDATETIME()`, `UtcNow`, a time-zone
  default (`"UTC"` as a zone id), or a sentence about *taking* the clock in UTC, not about storing
  `datetime2`. List anything doubtful.
- Balanced `csharp` fences; no `///`; no `using`.

## Report

Return: the file name; counts of column-table cells, sketch members, SQL type declarations and
prose sentences changed; the residue lists from the three greps with the reason each survivor
stays; and any place where you could not tell whether a `DateTime` was a timestamp or a
wall-clock value — do not guess there; list it with the line number and leave it unchanged.

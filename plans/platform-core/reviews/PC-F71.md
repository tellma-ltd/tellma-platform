# PC-F71: Server Umm al-Qura extended calendar throws past AH 9665

## Implementer notes

### Root cause

`UmAlQuraExtendedCalendar.DaysIn` took every month length outside the table from the framework's
`HijriCalendar.GetDaysInMonth`. The framework's tabular calendar stops at `DateTime.MaxValue`, which
is AH 9666-04-03. Its `CheckYearMonthRange` accepts the year 9666 but refuses any month after the
4th of it, so it throws `ArgumentOutOfRangeException` for `(9666, 5..12)`. The same framework still
answers `GetDaysInYear(9666)` (354) and `IsLeapYear(9666)`, so its own range is ragged. Our
`DaysIn` documents "0 for a month the calendar lacks" and is the length every other member is built
on, so the throw leaked out incidentally through:

- `DaysIn(9666, m >= 5)` itself;
- `Calendar.GetDaysInMonth(9666, m >= 5)`, `GetDaysInYear(9666)` and `IsLeapYear(9666)`, which sum
  or test `DaysIn`;
- `Compose`, which relied on a `try`/`catch` around `HijriCalendar.ToDateTime` to refuse the days
  of Rabi' II 9666 after the 3rd.

The rest of the 9666 behaviour was already right: every representable day converts.

### The documented range, and the choice

The calendar's documented range is AD 622-07-18 to `MaxSupportedDateTime` = `DateTime.MaxValue`,
i.e. AH 1-01-01 to 9666-04-03 (spec 0013 §8.3: the tabular `HijriCalendar` beyond the table; D-029's
PC-016 fix 1 addendum: valid from AD 622-07-18; the class's XML docs). The *dates* cannot be
extended: `DateOnly`/`DateTime` end there. The *lengths* can, and I extended them:

- `DaysIn` now computes the tabular length by the calendar's rule for every month of AH 1 to 9666
  outside the table (`TabularDaysIn`: 30 for an odd month and for Dhu al-Hijjah of a leap year, 29
  otherwise; the leap rule is still the framework's `HijriCalendar.IsLeapYear`, which answers for
  all of 9666). So `DaysIn(9666, 5..12)` = 30, 29, 30, 29, 30, 29, 30, 29 and `DaysIn(9666, 4)`
  stays 29, as before.
- `Compose` refuses a day after AH 9666-04-03 with an explicit comparison against `LastDate` (the
  tabular date of `DateOnly.MaxValue`), and the `try`/`catch` is gone. `ToDateTime`, `IsLeapDay`,
  `AddMonths` and `AddYears` already refuse a null `Compose` with the calendar's own
  `ArgumentOutOfRangeException` ("do not name a date" / "outside the range"), so they now refuse
  past the last day explicitly too.
- `MaxYear` is the year of `LastDate` (still 9666); 9667 and later have no months (`DaysIn` 0,
  `GetDaysInMonth`/`GetDaysInYear`/`IsLeapYear` throw the documented refusal).

Why lengths, not a refusal of the months after 9666-04-03:

- It mirrors the sibling `EthiopianCalendar`, whose `DaysIn` is arithmetic for every month of its
  last year and whose composition alone stops at `DateOnly.MaxValue`.
- It is what the framework itself does for the partial month (Rabi' II 9666 has 29 days although 3
  convert) and for the year (`GetDaysInYear(9666)` = 354). Refusing the later months would have
  left year 9666 with a length that no sum of its months gives, or forced year-level members to
  refuse a year whose first 93 days the calendar renders.
- It matches PC-104's client, which (per its notes) already returns lengths for those months.

### What was built, and where

- `src/core/Tellma.Core/Calendars/UmAlQuraExtendedCalendar.cs`: `LastDate`, `MaxYear` from it,
  `TabularDaysIn`, `IsAfterLastDate` (tuple comparison), `DaysIn` and `Compose` as above; the class
  remarks state the range and what happens past it.
- `test/core/Tellma.Core.Tests/Calendars/UmAlQuraExtendedCalendarTests.cs` (new):
  - every month outside the table, AH 1 to 9666-04, against `HijriCalendar.GetDaysInMonth` (less one
    for Muharram 1501), and every year's length and leap flag against the framework's;
  - every day from AD 622-07-18 to `DateOnly.MaxValue` decomposes, composes back, and follows the
    previous day (next day, or day 1 after the previous month's `DaysIn`), ending at (9666, 4, 3);
  - AH 9666: all twelve lengths, the year's 354 days, the starts of months 1-4 (9999-10-01, 10-31,
    11-29, 12-29), the last day, `GetDayOfYear(DateTime.MaxValue)` = 92;
  - the framework's own refusal of `GetDaysInMonth(9666, 5)` pinned as the reason for the rule;
  - days after the last (9666-04-04, 04-29, 05-01, 12-29): `Compose` null, `TryParse` null,
    `ToDateTime` and `IsLeapDay` throw with `ParamName` "day" and the calendar's own message;
  - AH 9667: no lengths, documented refusals;
  - arithmetic reaching the last day (`AddYears`, `AddMonths` forward and back, time of day kept)
    and refusing past it, including a day clamped into Rabi' II 9666 (`AddMonths(9666-03-30, 1)`).
  - the last day renders "3/4/9666" in ar-SA and reads back.
- `src/core/Tellma.Core/README.md` (calendars section) and `test/core/Tellma.Core.Tests/README.md`.

### For PC-104's integration (it owns the vector file and its consumer)

There is no server-side vector generator on platform-core: `umalqura-vectors.json` and
`UmAlQuraClientVectorsTests` exist only on pc/PC-104, so nothing here was regenerated. The test
that binds the server's lengths to the client's past 9999-12-31 lands at PC-104's integration
(review 1, R1-1):

- Add a `lengths` array to `umalqura-vectors.json`, rows `[year, month, length]`, generated from
  `UmAlQuraExtendedCalendar.DaysIn`. It must cover at least months 5-12 of AH 9666:
  `[[9666, 5, 30], [9666, 6, 29], [9666, 7, 30], [9666, 8, 29], [9666, 9, 30], [9666, 10, 29],
  [9666, 11, 30], [9666, 12, 29]]` (9666 is a common year).
- Assert it on both sides: `UmAlQuraClientVectorsTests` with `DaysIn`, and the client spec with its
  `daysInMonth`.
- The `months` rows (`[year, month, firstDay, length]`) cannot carry those months: their consumer
  asserts `Compose(year, month, 1)`, and their first days are past 9999-12-31. The `months` array
  can still be extended to the four months of 9666 that begin by then:

```
[9666, 1, "9999-10-01", 30],
[9666, 2, "9999-10-31", 29],
[9666, 3, "9999-11-29", 30],
[9666, 4, "9999-12-29", 29],
```

- The client's `daysIn` has no upper year (R1 point 2: `daysIn(9667, 1)` is 30 there and 0 on the
  server). That is past both sides' date range and is PC-104's to decide.

### Deviations

None from the specs. One behaviour change: `GetDaysInMonth(9666, 5..12)`, `GetDaysInYear(9666)` and
`IsLeapYear(9666)` now answer (lengths, 354, false) where they threw the framework's exception.

### Open risks

- `TabularDaysIn` restates the framework's month-length rule instead of delegating. The first test
  compares it with the framework on every month the framework answers (113,788 months), so a
  divergence fails the build.
- The whole-range day walk covers 3.4 million days. It runs in about 4 s locally; it is a unit test,
  not tagged nightly.

### Tests run locally

- `Tellma.Core.Tests` with the unit filter, narrowed to `Tellma.Core.Tests.Calendars`: 394 green,
  the 11 new ones included. Against the unchanged calendar (the old file built with the new tests),
  5 of the 11 fail: the 9666 lengths, the year-level members, the arithmetic and two of the
  past-the-end theory rows. So the tests pin the fix.
- `dotnet build Tellma.slnx`: Build succeeded, 0 warnings. `dotnet format Tellma.slnx
  --verify-no-changes`: clean.
- Not run: `CalendarSweepTests` (Queryex integration). It compares the SQL lowering with the
  interpreter over `core.CalendarMonths`, which is seeded from the framework's `UmAlQuraCalendar`
  for AH 1318-1500 and never reads `UmAlQuraExtendedCalendar`, so this change cannot reach it. The
  client-vector tests exist only on pc/PC-104.

### What reviewers should examine hardest

- Whether lengths for the months past 9999-12-31 (rather than a refusal) is the right reading of
  "0 for a month the calendar lacks" and of the brief's "documented range".
- That every public member of `UmAlQuraExtendedCalendar` past the last day now refuses with the
  calendar's own exception, not the framework's (the base `Calendar.AddDays`/`AddWeeks` family is
  the framework's and refuses past `MaxSupportedDateTime` on its own).

## Review 1

Reviewer 1 (Opus), commit 071cc228. Local runs on Windows:
- `dotnet build test/core/Tellma.Core.Tests/Tellma.Core.Tests.csproj`: Build succeeded, 0 warnings.
- `Tellma.Core.Tests` with the CI unit filter, narrowed to `Tellma.Core.Tests.Calendars` and
  `CalendarsCheckTests`: 418/418 green in 19 s (the implementer's 394 plus the 24 check tests).
- The two new or changed `.cs` files are LF with no BOM (`git ls-files --eol`).

The pc/PC-F71 CI run (45031747403) was skipped (no ": integrate" commit), so there is no Linux
evidence yet. The change is pure arithmetic over `HijriCalendar` and `DateOnly` and does not
depend on ICU; its one rendering test uses `PinnedCultures.ArSa`.

Judgement of the orchestrator's four points:

1. **D-029 and the specs.** Consistent.
   - D-029's PC-016 fix 1 addendum defines the server's `uq` as the .NET table plus the tabular
     calendar (adjustment 0) outside AH 1318-1500, valid from AD 622-07-18.
   - 0013 §8.2-§8.3 require only that dates beyond the table go through `HijriCalendar` with
     adjustment 0, and that `MaxSupported` give the exact range. `MaxSupported` is unchanged (the
     table's end).
   - `TabularDaysIn` restates the tabular rule but keeps the framework's leap rule
     (`Tabular.IsLeapYear`). The first test compares it with `HijriCalendar.GetDaysInMonth` on
     every month the framework answers. So the calendar is still .NET's wherever .NET has one.
   - 0008 §10.4 (`uq` from a host table, Nullable outside it) and 0024 §7.4 say nothing about the
     range past AD 9999, so neither is contradicted.
   - No spec or ARCHITECTURE.md change is needed.
2. **The client's lengths on pc/PC-104 (8dbe71bf).** They are equal.
   - In `tm-calendar-umalqura.ts`, outside the table, `daysIn` is `tabularDays(year, month)` less
     the overlap. `tabularDays` gives odd months 30 and even months 29, and month 12 30 in a leap
     year.
   - `isTabularLeap(9666)` is false: `(14 + 11 * 9666) mod 30 = 20`, which is not below 11.
   - So the client's months 5-12 are 30, 29, 30, 29, 30, 29, 30, 29, the server's `LengthsOf9666`.
   - The client's `fromParts` gives 9999-12-31 for (9666, 4, 3) and null for (9666, 4, 4): the same
     boundary as the server.
   - The two differ only past the server's range. The client's `daysIn` has no upper year, so
     `daysIn(9667, 1)` is 30 there but 0 on the server. That code is PC-104's, not this commit's.
3. **Explicit, documented refusals.** Every member that composes a day refuses explicitly:
   - `Compose` and `TryParse` give null;
   - `ToDateTime` and `IsLeapDay` throw `day` with the calendar's message;
   - `AddMonths` and `AddYears` throw `months` and `years`, "outside the range";
   - year 9667 is refused through `CheckYear`.

   The class remarks, the returns of `DaysIn` and `Compose`, and the README state the range. No
   path reaches a framework exception any more:
   - `Tabular.ToDateTime` is called only after `IsAfterLastDate` passes;
   - `Tabular.IsLeapYear` is called only for years 1 to 9666.

   One wrinkle, in which argument a refusal names, is R1-2.
4. **The 11 tests.** They pin the boundary. Tracing the old code confirms the 5 failures:
   - the whole-range length test (`GetDaysInYear(9666)` threw);
   - the AH 9666 test;
   - the theory rows (9666, 5, 1) and (9666, 12, 29), where `DaysIn` threw inside `Compose`;
   - the arithmetic test (`AddYears` blamed the framework's `month`).

   These changes to the new code would also fail a test:
   - dropping `IsAfterLastDate` fails rows (9666, 4, 4) and (9666, 5, 1) with the framework's
     exception;
   - an off-by-one in `LastDate` fails `Compose(9666, 4, 3) == DateOnly.MaxValue`;
   - a wrong leap or length rule fails the framework comparison over 113,788 months, or the
     hard-coded `LengthsOf9666`.

   The gap left is the one shared with the client (R1-1).

### Findings

**R1-1 (minor). No test checks that the server and the client agree on months 5-12 of AH 9666,
and the vector rows the notes propose cannot cover them.**
- Where: this file, "For PC-104's integration" (the four rows proposed for
  `umalqura-vectors.json`).
- Scenario:
  - D-029 makes one calendar authority, and the shared vector file is the only test that binds the
    server's lengths to the client's.
  - Its rows are `[year, month, firstDay, length]`, and its consumer asserts
    `Compose(year, month, 1)`. So months 5-12 of 9666 can never be rows, as the notes say.
  - Each side's months 5-12 are then pinned only by its own constants. The server has
    `LengthsOf9666`. The client on pc/PC-104 has no 9666 length assertion:
    `tm-calendar-umalqura.spec.ts` checks only `fromParts` at 9666-04-03 and 04-04.
  - A later change to either side's rule for those months, such as a client cap at the ISO
    window, would pass both suites while the server and the client disagree. Preventing that
    disagreement is the point of this item.
- Fix:
  - When PC-104 integrates, add a `lengths` array to the vector file, such as
    `[[9666, 5, 30], …, [9666, 12, 29]]`, generated from `UmAlQuraExtendedCalendar.DaysIn`.
  - Assert it on both sides, with `DaysIn` and with `daysInMonth`.
  - On this commit, amend the notes' "rows to add" to say so, so PC-104 carries it.

**R1-2 (minor). `GetDaysInMonth` blames `month` for a year outside the calendar, and the new test
makes that part of the contract.**
- Where: `src/core/Tellma.Core/Calendars/UmAlQuraExtendedCalendar.cs:238-243` and
  `test/core/Tellma.Core.Tests/Calendars/UmAlQuraExtendedCalendarTests.cs:147`.
- Scenario:
  - `GetDaysInMonth(9667, 1)`, and likewise `(0, 1)`, throws ParamName `month` with "The year and
    month do not name a month the calendar supports."
  - For the same year, `GetDaysInYear`, `IsLeapYear`, `GetMonthsInYear` and `IsLeapMonth` all
    blame `year`. So does the sibling `EthiopianCalendar.GetDaysInMonth`, which calls `CheckYear`
    first.
  - A caller is told the month is wrong when the month is valid and the year is not.
  - The behaviour predates this commit, but no test froze it. Line 147 now asserts `"month"`.
    Point 3 is about exactly these refusals, so the wrong name should not be frozen now.
- Fix: in `GetDaysInMonth`, call `CheckYear(year, era)` first, then refuse a month outside 1-12 as
  `month` (as `IsLeapMonth` does), then return `DaysIn`. Change line 147 to expect `"year"`.

### Responses

- R1-1: fixed in the review 1 fixes commit (notes only, per D-222). "For PC-104's integration" now
  asks PC-104's integration to add a `lengths` array (`[year, month, length]`, generated from
  `DaysIn`, at least AH 9666 months 5-12) to `umalqura-vectors.json` and to assert it on both
  sides. The four `months` rows for 9666-01..04 are kept as an optional extension. The client's
  unbounded `daysIn` past AH 9666 is noted as PC-104's to decide. No code change here.
- R1-2: fixed in the review 1 fixes commit. `GetDaysInMonth` calls `CheckYear` first, then refuses
  a month outside 1-12 as `month`, then returns `DaysIn`, as `EthiopianCalendar` does.
  `Beyond_ah_9666_no_month_has_a_length` now expects `year`. The new
  `A_month_length_refusal_names_the_argument_at_fault` pins `year` for (0, 1) and (9667, 13),
  `month` for (9666, 0) and (1447, 13), and `era` for era 2.

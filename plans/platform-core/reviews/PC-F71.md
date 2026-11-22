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
`UmAlQuraClientVectorsTests` exist only on pc/PC-104, so nothing here was regenerated. Once this
lands, the vectors can list every month of AH 9666 whose first day is representable. Its consumer
asserts `Compose(year, month, 1)` and `DaysIn(year, month)` per row, so months 5-12 of 9666 cannot
be rows (their first days are past 9999-12-31). The rows to add are:

```
[9666, 1, "9999-10-01", 30],
[9666, 2, "9999-10-31", 29],
[9666, 3, "9999-11-29", 30],
[9666, 4, "9999-12-29", 29],
```

The client's lengths for 9666 months 5-12 should equal the server's `DaysIn`: 30, 29, 30, 29, 30,
29, 30, 29 (9666 is a common year).

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

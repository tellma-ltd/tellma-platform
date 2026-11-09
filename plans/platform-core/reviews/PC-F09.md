# PC-F09: Host-independent Umm al-Qura culture test (PC-016)

## Implementer notes

### Cause

CI run 42553979775 (platform-core 0492c35) failed on `build-test (ubuntu-latest)` only:
`CalendarFormattingTests.A_culture_whose_own_calendar_is_umm_al_qura_still_renders_gregorian_dates_with_gregorian_names`
asserted that `CultureInfo.GetCultureInfo("ar-SA")` has `UmAlQuraCalendar` as its calendar. The runner
image is now **ubuntu-26.04** (libicu 78.2). From ICU 78, CLDR gives Saudi Arabia a Gregorian default
calendar; Umm al-Qura stays one of ar-SA's optional calendars. Windows' ICU and Ubuntu 24.04's libicu 74
(the earlier container check) still default to Umm al-Qura, which is why every local run passed.
Reproduced in `mcr.microsoft.com/dotnet/sdk:10.0-resolute` (Ubuntu 26.04, libicu 78.2). It was the only
failure in `Tellma.Core.Tests`. The four `TaxonomyTests` that also failed there are an artefact of the
container copy, not of ICU.

### Product

No change. `CalendarCultureData` decides from the culture's own `DateTimeFormat.Calendar`. A culture
already on a Gregorian calendar is used as it is, which matters for PC-019's pinned negotiated clone.
Any other culture is switched, on a private clone, to the Gregorian calendar among its
`OptionalCalendars`. The outcome does not depend on the host's default for the culture's name: a
Gregorian-default ar-SA (ICU 78) and an Umm al-Qura ar-SA render identically. The comments in
`CalendarCultureData` and `GregorianCalendarSystem` and the `Tellma.Core` README no longer state that
ar-SA's default is Umm al-Qura.

### Tests

- The ar-SA test now clones ar-SA and sets its calendar to the `UmAlQuraCalendar` from its
  `OptionalCalendars`. It asserts the golden "15 مارس 2026", that every style renders as it does for a
  Gregorian-switched clone, and that the Gregorian month name is used.
- The Bulgarian read-back test pins bg-BG's short pattern (`d.MM.yyyy 'г'.`, the value ICU 74 to 78
  give) instead of relying on the host's.
- Sweep: the remaining host-culture assertions (ar-SA's Gregorian name مارس, en-US and zh-CN field
  order, ja-JP's short pattern, and the properties over every predefined culture) pass on ICU 74, 78
  and Windows. The goldens already use `PinnedCultures`, which sets its Gregorian calendar explicitly.

### Runs

Windows: build 0 warnings, unit filter green (Tellma.Core.Tests 850). Ubuntu 26.04 (libicu 78.2) and 24.04 (libicu 74): Tellma.Core.Tests 849 + 1 skip, green.

### For the reviewer

- Is any other assertion in `test/core/Tellma.Core.Tests/Calendars/` still tied to one ICU release's
  data?

## Review 1

### Findings

Checked: (a) the product is unchanged and correct. `CalendarCultureData.cs:44-53` is the only production path that reads a culture's calendar. It tests the instance's own `DateTimeFormat.Calendar` and switches a private clone to a Gregorian calendar from that instance's `OptionalCalendars`. It never looks up the host's default for the culture's name. No other code under `src/` reads `OptionalCalendars` or a culture's `Calendar`. (b) The new ar-SA test is not vacuous. Without the switch, an Umm al-Qura format yields Hijri month names, and the golden "15 مارس 2026" and the `MonthName` check would fail. On every host the clone is genuinely on `UmAlQuraCalendar`. (c) A probe over ar-SA, ar-AE, fa-IR, th-TH, he-IL, am-ET, ti-ET, en-GB, bg-BG, ja-JP, zh-CN and en-US on Windows, ICU 74 and ICU 78 showed several changes on ICU 78. ar-SA defaults to Gregorian, with Umm al-Qura still optional. ti-ET's short pattern is M/d/yyyy. am-ET's long pattern is "dddd d MMMM yyyy". bg-BG's pattern is the same on all three. Every concrete host-data assertion that is left (ar-SA مارس, en-US and zh-CN field order, ja-JP's short pattern) holds on all three. The goldens use `PinnedCultures`. CI run 42553979775 shows this test as the only unit failure on ubuntu-26.04. (d) Tellma.Core.Tests results: Windows 850/850. sdk:10.0-resolute (libicu 78) and sdk:10.0 (libicu 74) each gave 845 passed, 1 skipped and the same 4 `TaxonomyTests` failures. Those four fail because a `git archive` copy has no `.git` for `TaxonomyCheck.FindRepositoryRoot`, so ICU is not the cause. With an empty `.git` added, resolute gives 849 passed and 1 skipped.

R1-1 (minor) `src/core/Tellma.Core/README.md:145-146`: the new sentence says the host-culture tests "assert only properties". That is not true. `CalendarFormattingTests.cs:150` (the new "15 مارس 2026" golden over host ar-SA data), `:194` (host ar-SA مارس), `:228-229` (host en-US and zh-CN order) and `:302` (host ja-JP short pattern) all assert concrete host CLDR values. They pass on Windows and on ICU 74 and 78 today. Failure scenario: a reader trusts the README and treats a future ICU-driven failure of one of these tests as a product bug, or adds more host goldens on the belief that none exist. Suggested fix: reword it, for example "the golden tests pin their cultures' data, and the host-culture tests assert properties plus a few values stable across ICU releases (Arabic's Gregorian month names, field order)". Alternatively, pin those few values as the Bulgarian test now does.

### Responses

- R1-1: fixed in ba9e69d, the robust way. Every concrete-value assertion now pins
  the culture data it reads: the ar-SA month name uses `PinnedCultures.ArSa`; the en-US and zh-CN
  field orders and the ja-JP short rendering use clones with a pinned short pattern and separator
  (`WithShortDate`). The Umm al-Qura culture test can't pin Gregorian names on a clone whose calendar
  is Umm al-Qura, because the switch to its optional Gregorian calendar reloads them from the host.
  It therefore drops the host golden: it asserts that the rendering carries the same culture's own
  Gregorian name of March and not the name of the date's month in Umm al-Qura, plus the day and year,
  and still checks every style against the Gregorian-switched clone. The README sentence now says
  that goldens and value assertions pin their data, host-culture tests assert properties, and the
  Umm al-Qura test compares with the culture's own Gregorian data.

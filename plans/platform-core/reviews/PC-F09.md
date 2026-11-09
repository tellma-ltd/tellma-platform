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

# Queryex Ethiopian calendar vectors

A generator, not a test suite. It writes the Ethiopian dates that ICU gives a set of Gregorian
dates, so the Queryex suites can hold the engine's Ethiopian arithmetic to the same source the
client reads Ethiopian dates from.

The output is checked in at `test/shared/Tellma.Queryex.Testing/Semantics/EthiopicVectors.csv` and
embedded in the testing library, where two things read it:

- the reference interpreter's own check, which converts every vector both ways;
- the constant corpus, which compiles the Ethiopian year, month, day and quarter of two years of
  vectors and compares the server's answers with the file.

## What it writes

One row per Gregorian date, in date order, under the header `gregorian,year,month,day`:

- the pinned conformance dates the server and the client are held to, some of them well outside
  the range below;
- every boundary day of every Ethiopian year that overlaps 1900 to 2100: the first day of each
  year, quarter and Pagume, the last day of the third quarter, the last day of Nehase, and the
  last days of Pagume.

Each date is read at midnight UTC through `Intl.DateTimeFormat` with the `ethiopic` calendar, so no
time zone can move it to another day.

## Running it

Node 24 or later (the repository's engine), on Windows or Linux, from the repository root:

```sh
node eng/queryex-ethiopic-vectors/generate.mjs test/shared/Tellma.Queryex.Testing/Semantics/EthiopicVectors.csv
```

The file only changes if ICU's Ethiopian calendar does. A run that changes it should be reviewed as
a change to the calendar itself.

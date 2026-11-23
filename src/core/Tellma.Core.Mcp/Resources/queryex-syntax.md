# Queryex syntax

Queryex is the expression language of the `select`, `filter`, `having` and `orderBy` arguments of
`tellma_query` and `tellma_aggregate`. Every expression is compiled against the entity's columns,
type-checked, and run in the database; nothing is evaluated on the server or by you.

## Lists and items

- `select` and `orderBy` are comma-separated lists of expressions; `filter` and `having` are one
  predicate each.
- An `orderBy` item may end in `asc` (the default) or `desc`: `PostingDate desc, Id`.
- Empty items are errors: no leading, trailing or doubled commas.

## Paths

- A column is named by its property name: `Code`, `Name`, `IsActive`.
- A navigation is followed with `.`: `Parent.Code`, `Account.Center.Name`. A path through an
  absent navigation is absent.
- Names are matched ignoring case. A name that collides with a keyword is bracketed: `[Order]`.
- Call `tellma_describe` with the entity for its columns, navigations and their types.

## Literals and parameters

- Strings are delimited by `'`; a quote inside is doubled: `'It''s fine'`. Backslash is an ordinary
  character.
- Numbers are exact decimals with digits on both sides of any point: `1`, `0.5`, `3.14`. There is
  no exponent and no negative literal: write `-1`.
- `true`, `false` and `null`.
- A date is a string literal in ISO 8601 where a date is expected: `'2026-01-31'`,
  `'2026-01-31T14:30'`, `'2026-01-31T14:30:00+03:00'`. A GUID is a hyphenated string literal.
- `@name` is a parameter; give its value in the call's `arguments`: `Code = @code` with
  `{"code": "A100"}`. Prefer parameters to splicing values into the text.

## Operators

From tightest to loosest:

| Operators | Meaning |
| --- | --- |
| `-x` | negation |
| `*` `/` `%` | multiplication, division, remainder |
| `+` `-` `\|\|` | addition, subtraction, string concatenation |
| `=` `!=` `<` `<=` `>` `>=` | comparison; never chained: `a = b = c` is an error |
| `x in (a, b, c)` | membership |
| `x is null`, `x is not null` | absence |
| `not` | negation of a predicate: `not a = b` is `not (a = b)` |
| `and` | conjunction |
| `or` | disjunction |

Absent values (null) compare totally:

- `=` is true when both sides are absent and false when one is; `!=` is its exact complement.
- `<`, `<=`, `>` and `>=` are false when either side is absent, so a range such as
  `PostingDate >= @from and PostingDate <= @to` leaves out rows with no date. Write
  `PostingDate < @d or PostingDate is null` to keep them.
- String comparison follows the column's collation, usually ignoring case; trailing spaces do not
  count.

There are no implicit conversions between numbers and strings, or between the three date types;
use `cast`.

## Functions

Every call takes parentheses, including `today()`. Function names are matched ignoring case.

### Aggregations

Allowed only in `tellma_aggregate`'s `select`, `having` and `orderBy`, and never nested.

| Function | Result |
| --- | --- |
| `count()` | the rows of the group |
| `count(x)`, `count(x, c)` | the rows where `x` is present (and `c` is true) |
| `sum(x)`, `sum(x, c)` | the total of `x` (over the rows where `c` is true) |
| `avg(x)`, `avg(x, c)` | the average of `x` |
| `min(x)`, `max(x)` (each with optional `c`) | the smallest and largest `x` |

In `tellma_aggregate`, every `select` item with no aggregation is a grouping key, so
`Account.Code, year(PostingDate), sum(Amount)` totals `Amount` per account and year. An item may not
mix a bare column with an aggregation (`Price * sum(Qty)` is an error). `having` reads only
aggregations: `sum(Amount) > 1000`.

### Dates and times

Calendar operations take a `date` or a `datetime`, never a `datetimeoffset`: convert it first with
`local(x)` (the tenant's time zone) or `local(x, 'Africa/Nairobi')` (an IANA zone).

| Function | Result |
| --- | --- |
| `year(d)`, `quarter(d)`, `month(d)`, `day(d)`, `week(d)` | the part, in the Gregorian calendar or the calendar named by an optional last argument, such as `year(d, 'et')` |
| `weekday(d)` | 1 for Monday to 7 for Sunday |
| `hour(t)`, `minute(t)`, `second(t)` | the time of day's part |
| `startOfYear(d)`, `startOfMonth(d)`, `startOfWeek(d)`, `startOfDay(d)` | the period's first date; a week starts on Monday |
| `addYears(d, n)`, `addMonths(d, n)` | calendar arithmetic, clamped to the month's last day |
| `addDays(d, n)`, `addHours(d, n)`, `addMinutes(d, n)`, `addSeconds(d, n)` | fixed-length arithmetic |
| `diffYears(d1, d2)`, `diffMonths(d1, d2)` | the completed years or months from `d1` to `d2` |
| `diffDays(d1, d2)`, `diffHours(d1, d2)`, `diffMinutes(d1, d2)`, `diffSeconds(d1, d2)` | the elapsed time from `d1` to `d2` |
| `local(x)`, `local(x, zone)` | a `datetimeoffset` as a local `datetime` |
| `today()`, `now()` | the tenant's date, and the current instant |

### Context, conditions and conversions

| Function | Result |
| --- | --- |
| `me()` | your user id |
| `if(c, a, b)` | `a` when `c` is true, else `b` |
| `coalesce(a, b, ...)` | the first present value |
| `cast(x, 'type')` | `x` as `'numeric'`, `'string'`, `'bool'`, `'guid'`, `'date'`, `'datetime'` or `'datetimeoffset'` |

### Numbers and strings

| Function | Result |
| --- | --- |
| `abs(x)`, `floor(x)`, `ceiling(x)` | the usual numeric functions |
| `round(x, digits)` | `x` rounded to a literal number of digits |
| `length(s)`, `trim(s)`, `upper(s)`, `lower(s)` | the usual string functions |
| `left(s, n)`, `right(s, n)`, `substring(s, start)`, `substring(s, start, len)` | parts of a string; `substring` counts from 1 |
| `replace(s, old, new)` | `s` with each `old` replaced |
| `contains(s, part)`, `startsWith(s, prefix)`, `endsWith(s, suffix)` | whether `s` holds the text, matched literally: `%` and `_` are ordinary characters; false when `s` is absent |

### Hierarchies

On an entity with a tree, `descendantOf(key, k1, k2, ...)` is true for a row at or below any of the
rows whose `key` is one of the values, and `ancestorOf(key, k1, ...)` for a row at or above them.
`key` is a unique column path and the values are literals or parameters:
`descendantOf(Parent.Code, 'Assets')`, `ancestorOf(Id, @id)`.

## Worked filters

- Active centers whose code starts with A: `IsActive = true and startsWith(Code, 'A')`.
- This year's entries above a threshold, or with no amount yet:
  `PostingDate >= startOfYear(today()) and (Amount > @min or Amount is null)` with
  `{"min": 1000}`.
- Rows of either of two parents, created by you:
  `Parent.Code in ('A100', 'A200') and CreatedById = me()`.

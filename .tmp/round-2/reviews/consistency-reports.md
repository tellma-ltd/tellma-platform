# Consistency check — cluster "reports" (specs 0033, 0034, 0035; spec 0010 Part F)

Scope: `docs/specs/0033-report-definitions-and-engine.md`, `0034-report-renderers-and-studio.md`,
`0035-dashboards-and-home-page.md` and `0010-amendments-to-implemented-specs.md` Part F (§F.0–§F.15,
its Testing, Definition of done, Decisions record and Review flags), against `known-issues.md`,
`addenda.md`, `rulings.md` and `design/00-architecture.md` (in that precedence) and the style guide.
Every `spec NNNN §x` pointer in the four texts was resolved mechanically (578 pointers; every
target heading exists except the two bare `§C.x` pointers of K-reports-15) and the claim each makes
was read against the target. Inbound pointers into 0033–0035 from 0011–0032 were spot-checked
(K-reports-32). Line numbers below are today's.

Known issues and addenda that concern these specs, and their state:

| Item | State | Finding |
|---|---|---|
| K48 (`'uq'` path-free operands; 0033 flag 6 goes) | **not resolved** in 0010 §F.0, §F.11, §F.13 or 0033 | K-reports-1 |
| K50 (`IDataBatch` member for constant statements) | **not resolved** in 0012 §5.2; 0033 names no member | K-reports-2 |
| K-0035a (0033 §4.3 cites 0035 §5.3's full rule) | **not resolved** | K-reports-3 |
| K-0035b (plain delete confirmation; 0033 §5.6 sentence goes) | resolved in 0034 §10.3; **not** in 0033 §5.6, Decision 12, nor 0035 flag 11's citation | K-reports-4 |
| K-0034a (`edit: 'page'`; one save path) | **not resolved** in 0034 (0031 and 0029 side: K-crud-4, K-shell-entity-5) | K-reports-5 |
| K49 / addendum 4 (`core.report-seeds` at `Order` 900) | resolved in 0033 §13; **not** in 0011 §6.2 or 0018 §6.3 | K-reports-6 |
| Addendum 3 (0015 §5.3 admits computed extras) | **not resolved**; 0033 §5.2 and 0035 §6.1 cite the missing rule | K-reports-7 |
| K39 (`canDeactivate` in 0024 §9.2; the studio uses it) | **not resolved** (same as K-crud-7, K-foundation-5) | K-reports-8 |
| K-F (0010 front matter covers Part F) | **not resolved** (same as K-foundation-14, K-shell-entity-14) | K-reports-11 |
| K20 (parameter preferences fall back to `{ v: 1, args: {} }`) | resolved: 0025 Appendix A; 0034 §4.5 and 0035 §10.3 cite it | — |
| K26 (`TmPageAction.confirm` shape) | not applicable: 0034 declares no `confirm` value (§13 says "confirmed") | — |
| Addendum 1 (Part F numbering §F.0–§F.15) | resolved: headings and every citation in 0010–0035 match; no `§F.16` anywhere | — |
| Addendum 8 (`IChildQueryService.QueryAsync(resource, request)`) | resolved: 0033 §10, 0015 §5.2 | — |
| Addendum 9 (0019 §4.4 table writer; `Core_Job_ExportReport`) | resolved in 0033 §12; the key is missing from 0013 §10.1 | K-reports-30 |

Findings other clusters already filed that change these specs (not repeated below unless the fix
needs text in 0033–0035): K-crud-4 (0031 §3.7 `edit: 'page'`), K-crud-7 / K-foundation-5 (K39),
K-crud-10 (drill `from`, see K-reports-9), K-crud-19 (0034 §8.3's recents sentence, see
K-reports-37), K-foundation-6 (0024 reserved paths vs `reports/:id/studio`), K-foundation-14 /
K-shell-entity-14 (K-F), K-foundation-26 (0034 §4.2 and 0035 §15.3 "44 px (spec 0010 §B.6)"),
K-shell-entity-5 (0029 §3 `details.edit`), K-shell-entity-37 (0028 §7.4 restates 0035 §9.2), and
the K-shell-entity finding that adds "spec 0034 §9" to 0028 §8.1's "Exporting…" row.

---

## Major

### K-reports-1 — major — known issue K48 — 0010 §F.0, §F.11, §F.13, Part F Decision 13, Review flag 4, Testing; 0033 §4.3, §8.7, Review flag 6

**Problem.** K48 is binding: a path-free `'uq'` operand compiles to the same `TOP (1)` month-map
seek as a scalar subquery (legal in `SELECT`, `WHERE`, `HAVING`); QX3104 applies only to such an
operand inside an aggregate's argument or a grouping key; `startOfMonth(today(), 'uq')` works as a
parameter default; 0033's flag 6 goes. Both specs still refuse every path-free operand: §F.13
"Anything else is QX3104: a path-free operand (`startOfMonth(today(), 'uq')`)…", §F.11 "a table
calendar QX3104 (§F.13)", 0033 §4.3 "a table calendar is QX3104 (§8.7)", 0033 §8.7 "a path-free
operand (`startOfMonth(today(), 'uq')`) is QX3104", 0033 flag 6. (K48's "only" also admits an
aggregated operand such as `month(max(PostingDate), 'uq')` in `SELECT` and `HAVING`, where SQL
Server allows an outer aggregate inside a subquery; the fix below states it so.)

**Fix — 0010 §F.0, codes table.** Replace
"| QX3104 | A table calendar needs a row-grain operand that reads a path | `function` | §F.13 |"
with
"| QX3104 | A table-calendar operand that is not a row-grain path sits in an aggregation's argument or a grouping key | `function` | §F.13 |".

**Fix — 0010 §F.11.** In **Diagnostics**, replace "a list QX2009; a table calendar QX3104 (§F.13)."
with "a list QX2009. A `'uq'` call emits §F.13's scalar form." In the **Tests** line, replace
"`HasUser = false` makes `me()` absent;" with "`HasUser = false` makes `me()` absent; a `'uq'` item
(`startOfMonth(today(), 'uq')`);".

**Fix — 0010 §F.13.** Replace the **Operands** bullet ("Each date operand must read a path and
contain no aggregation, … `'uq'))` instead).") with:

> - **Operands.** A date operand that reads a path and contains no aggregation is evaluated at row
>   grain through the lateral lookup below, in every clause. Any other operand, path-free
>   (`startOfMonth(today(), 'uq')`) or aggregated (`month(max(PostingDate), 'uq')`), emits the same
>   `TOP (1)` seek as a scalar subquery, which SQL Server allows in `SELECT`, `WHERE` and `HAVING`.
>   Inside an aggregation's argument or a grouping key such an operand is QX3104: SQL Server
>   refuses a subquery there (errors 130 and 144).

In **Emission**, replace "One lateral lookup per distinct operand per statement," with "A row-grain
operand gets one lateral lookup per distinct operand per statement,". After the `OUTER APPLY` block
add the bullet:

> - **Scalar form.** Any other operand emits the same `SELECT TOP (1) [M].[<column>] … ORDER BY
>   [M].[StartDate] DESC` as a parenthesized subquery in place, one per use; a second lookup nests
>   one more subquery.

In **Tests**, replace "QX3104 for both refused shapes and inside a constant statement;" with "a
scalar-form golden for a path-free and an aggregated operand in `SELECT`, `WHERE` and `HAVING` and
in a constant statement; QX3104 for such an operand inside an aggregation's argument and in a
grouping key;".

**Fix — 0010 Part F Decision 13.** Replace the item with: "13. **`'uq'` reads a host month table by
one `TOP (1)` seek: lateral for a row-grain path operand, a scalar subquery for any other; QX3104
only where SQL Server refuses a subquery** — the lateral form keeps `GROUP BY` and `GROUPING_ID`
legal and the seek sargable, and the scalar form runs a default such as `startOfMonth(today(),
'uq')` with no prologue variable (§F.13)."

**Fix — 0010 Part F Review flag 4.** Replace the item with: "4. **Two emission forms for `'uq'`**
(§F.13): the lateral seek for row-grain path operands, a scalar subquery for the rest, refused
inside aggregation arguments and grouping keys. Alternative: the lateral form only, refusing every
other operand. Flips if the scalar form's plans measure poorly over large fact tables."

**Fix — 0033 §4.3, Grammar.** Replace "`today()`, `startOfYear(today())`, `addMonths(today(), -1)`,
`me()`, `'East'`, `5`. It checks against the type (a string literal coerces to a date type, spec
0008 §7.3); a table calendar is QX3104 (§8.7)." with "`today()`, `startOfYear(today())`,
`addMonths(today(), -1)`, `startOfMonth(today(), 'uq')`, `me()`, `'East'`, `5`. It checks against
the type (a string literal coerces to a date type, spec 0008 §7.3)."

**Fix — 0033 §8.7.** Replace "`'uq'` works in every calendar function over row-grain operands (spec
0010 §F.13), through spec 0013 §8.2's month map; dates outside 1900-04-30 … 2077-11-16 give null,
and a path-free operand (`startOfMonth(today(), 'uq')`) is QX3104." with "`'uq'` works in every
calendar function through spec 0013 §8.2's month map, and a date outside the map gives null. Its
operand rules, QX3104 included, are spec 0010 §F.13's."

**Fix — 0033 Review flags.** Delete flag 6 ("**`'uq'` only over columns** …"). Renumber the rest
(with K-reports-33's deletion of flag 3: old 4, 5, 7–12 become 3–10). No spec cites 0033's flag
numbers.

**Files:** `0010-amendments-to-implemented-specs.md`, `0033-report-definitions-and-engine.md`.

### K-reports-2 — major — known issue K50 — 0012 §5.2, §11.3; 0033 §4.3, §8.3

**Problem.** K50: 0012 §5.2's `IDataBatch` gains the member that runs a constant statement compiled
by `EvaluateConstant` (0010 §F.11) and returns its one row, named to match §F.11, and 0033 §4.3 uses
it. `IDataBatch` has `Rows`, `Count`, `Sql`, … and no such member; 0033 §4.3 and §8.3 step 3 only
say the run "executes one constant statement" / "compile them with `EvaluateConstant` … and run the
statement", so nothing says how a service runs it on a batch.

**Fix — 0012 §5.2 sketch.** After
"    BatchResult<int> Count(QuerySpec spec, QueryArguments? arguments, int cap = 10000);" insert
"    BatchResult<QueryRowSet> EvaluateConstant(IReadOnlyList<ConstantItem> items, int languageVersion); // spec 0010 §F.11's constant statement; one row".

**Fix — 0012 §11.3.** After the first paragraph (ending "only stored text carries an older stamp,
per leaf.") add: "`EvaluateConstant(items, languageVersion)` compiles spec 0010 §F.11's statement
with `BatchOrdinal = b`, binds its `Today`, `Now`, `UserId` and `TimeZone` slots from the same
`QueryexContextValues` as `Rows`, and returns its one row."

**Fix — 0033 §4.3, Evaluation.** Replace "the run executes one constant statement (spec 0010 §F.11)
holding every applied default," with "the run executes one constant statement holding every applied
default through `IDataBatch.EvaluateConstant` (spec 0012 §5.2, spec 0010 §F.11),".

**Fix — 0033 §8.3 step 3.** Replace "compile them with `EvaluateConstant` (spec 0010 §F.11) under the
definition's `LanguageVersion` and run the statement in one Read round trip." with "run them through
`IDataBatch.EvaluateConstant` (spec 0012 §5.2) under the definition's `LanguageVersion`, in one Read
round trip."

0010 §F.11 cites no batch member, so it needs no change.

**Files:** `0012-data-access-layer.md`, `0033-report-definitions-and-engine.md`.

### K-reports-3 — major — known issue K-0035a — 0033 §4.3

**Problem.** K-0035a: 0033 §4.3's sentence on dashboards cites 0035 §5.3's full rule (a default, a
fixed-value binding, or a required dashboard parameter). It still reads "Dashboards hide parameters,
so there a required one needs a default (spec 0035 §5.3)", which is also wrong: dashboards have a
parameter bar (0035 §10).

**Fix.** Replace "Dashboards hide parameters, so there a required one needs a default (spec 0035
§5.3)." with "On a dashboard a required parameter needs a default, a non-null fixed-value binding or
a required dashboard parameter (spec 0035 §5.3)."

**Files:** `0033-report-definitions-and-engine.md`.

### K-reports-4 — major — known issue K-0035b — 0033 §5.6, Decision 12; 0035 Review flag 11

**Problem.** K-0035b: delete uses the host's plain confirmation and 0033 §5.6's "reads the audience"
sentence goes. 0034 §10.3 and flag 12 comply; 0033 §5.6 still says "after a warning that reads the
audience (spec 0034 §10.3)", which 0034 §10.3 no longer says; Decision 12 says "after a warning";
0035 flag 11 cites "as spec 0033 §5.6 describes for reports", which K-0035b removes.

**Fix — 0033 §5.6.** Replace "shares included, after a warning that reads the audience (spec 0034
§10.3)." with "shares included, after the host's plain confirmation (spec 0034 §10.3)."

**Fix — 0033 Decision 12.** Replace "**Delete removes for everyone after a warning; a dashboard
blocks it**" with "**Delete removes for everyone after the host's confirmation; a dashboard blocks
it**".

**Fix — 0035 Review flag 11.** Replace "Alternative: name the reach, as spec 0033 §5.6 describes for
reports." with "Alternative: read the audience first and name the reach, as spec 0034's review
flag 12 weighs for reports."

**Files:** `0033-report-definitions-and-engine.md`, `0035-dashboards-and-home-page.md`.

### K-reports-5 — major — known issue K-0034a — 0034 §2.3, §10.2, Review flag 11

**Problem.** K-0034a: a `page` hatch declares `edit: 'page'`, which hides the host's `details.edit`
and in-place edit mode (0031 §3.7, 0029 §3: K-crud-4, K-shell-entity-5). 0034's declaration does
not declare it, §10.2 says the host's `details.edit` "must not show on this hatch (review flag 11)",
and flag 11 records the opposite of the ruling ("spec 0031 §3.7 keeps the host's `details.edit` on
every `page` hatch").

K-0034a also opens with "so there is one save path". With `edit: 'page'` and today's §12.1, create
saves through the host and edit through the studio page (§12.9): two paths. The fix below applies
the `edit: 'page'` rule and records the single-path design as flag 11's alternative. **Needs a
ruling** if K-0034a means one path now: then §12.1's Edit column, §12.9 and Decision 17 must host
the studio as `TmReportPage`'s edit mode (as 0035 §12 does), and `edit: 'page'` would not apply.

**Fix — 0034 §2.3.** Replace
"             create: () => import('@tellma/core/report-studio').then((m) => m.TmReportStudio) },"
with
"             create: () => import('@tellma/core/report-studio').then((m) => m.TmReportStudio),
             edit: 'page' },                                   // the studio edits (§12.1); no in-place edit (spec 0031 §3.7)".

**Fix — 0034 §10.2.** Replace "- **No in-place edit:** definitions are edited in the studio, so the
host's `details.edit` must not show on this hatch (review flag 11)." with "- **No in-place edit.**
The declaration's `edit: 'page'` (spec 0031 §3.7) removes the host's `details.edit`; Edit opens the
studio."

**Fix — 0034 Review flag 11.** Replace the item with: "11. **The studio edits on its own page**
(§12.1), so create saves through the details host and edit through the studio (§12.9). Alternative:
the studio as the report page's edit mode, as dashboards do, with no `/studio` route and one save
path. Flips on preferring one studio lifecycle."

**Files:** `0034-report-renderers-and-studio.md` (0031 and 0029 per K-crud-4, K-shell-entity-5).

### K-reports-6 — major — contradiction; known issue K49 / addendum 4 — 0011 §6.2; 0018 §6.3 vs 0033 §13

**Problem.** 0033 §13: "`core.report-seeds` runs at `Order` 900, after every pack and distribution
step, so the roles a seed names exist." 0011 §6.2 lists "`30 core.report-seeds`", and its
`ITenantProvisioningStep.Order` comment bands "platform 0–99, packs 100–199, distributions 200+".
0018 §6.3 runs `core.report-seeds` before `gl.sample-centers` and `acme.sample-data`, the step that
creates the role `Sales`, so on a fresh development tenant the seed's grant to `Sales` is skipped
with `ReportEvents.SeedRoleMissing` and never written later (grants are insert-only).

**Fix — 0011 §6.2, step list.** Replace "`20 core.blob-container` (spec 0017 pre-creates the tenant's
container); `30 core.report-seeds` (spec 0033 §13 upserts the seeded report definitions). Spec 0018
adds `100 gl.sample-centers`." with "`20 core.blob-container` (spec 0017 pre-creates the tenant's
container); `900 core.report-seeds` (spec 0033 §13 upserts the seeded report definitions). Spec 0018
adds `100 gl.sample-centers`. Orders 900 and above are platform steps that must run after every
pack and distribution step."

**Fix — 0011 §6.2 sketch.** Replace "// platform 0–99, packs 100–199, distributions 200+" with
"// platform 0–99 and 900+, packs 100–199, distributions 200–899".

**Fix — 0018 §6.3.** Replace "`core.blob-container`, `core.report-seeds`, `gl.sample-centers` and
`acme.sample-data`," with "`core.blob-container`, `gl.sample-centers`, `acme.sample-data` and
`core.report-seeds`,".

**Files:** `0011-distribution-host-and-multitenancy.md`, `0018-core-and-gl-reference-stacks.md`.

### K-reports-7 — major — addendum 3; claim not in target — 0015 §5.3 vs 0033 §5.2, 0035 §6.1

**Problem.** 0033 §5.2 ("It is a computed extra (spec 0015 §5.3)") and 0035 §6.1 ("shaped in C# as a
computed extra (spec 0015 §5.3)") rely on addendum 3's rule. 0015 §5.3's `DetailsPlan` offers only
`AddExtra` (raw SQL or a `QuerySpec`, each one result set sent as is); nothing computes an extra in
C# or reads a statement without sending it. (K-crud-28 cites the same gap for 0032.)

**Fix — 0015 §5.3 sketch.** After "    public void AddExtra(string name, QuerySpec spec, QueryArguments?
arguments);" add

```csharp
    public BatchResult<RowPage> AddRead(QuerySpec spec, QueryArguments? arguments);                  // a read a computed extra uses; never an extra itself
    public void AddComputedExtra(string name, Func<IReadOnlyList<TEntity>, QueryRowSet> compute);   // C# over the loaded rows after the batch; the result is Extras[name]
```

**Fix — 0015 §5.3 member table.** Add the row "| `AddComputedExtra` | An extra SQL cannot express,
computed in C# by the service from the loaded rows and from its `AddRead` results once the batch has
run; it adds no round trip. Spec 0033 §5.2's `permissions` and spec 0018 §4.3's `problems` are
computed extras. |". (The owner of 0015 may rename the two members; 0033 and 0035 cite only the
section.)

**Files:** `0015-crud-service-pipeline.md`.

### K-reports-8 — major — known issue K39 — 0024 §9.2 vs 0034 §12.1

**Problem.** K39: 0024 §9.2's route assembly sets a `canDeactivate` that runs the page scope's leave
checks, which "the details host and the report studio use". 0034 §12.1 gives the studio's edit page
`tmLeaveGuard` (0031 §11.3); 0024 §9.2's "Page routes" row has no `canDeactivate`, so the studio's
guard never runs on a router navigation.

**Fix — 0024 §9.2, "Page routes" row** (same text as K-crud-7). Replace "every one has `TmRouteHost`
as its component and a `TmRouteData` (§4.4);" with "every one has `TmRouteHost` as its component, a
`TmRouteData` (§4.4) and a `canDeactivate` that runs the leave checks registered in its page scope
(0031 §11.3), which the details host and the report studio register;".

**Files:** `0024-client-app-foundation.md`.

### K-reports-9 — major — contradiction with ruling 2 — 0033 §9.4; 0034 §8.1, §8.2, §8.3, §8.4, §13

**Problem.** Ruling 2 (and 0024 §9.5): no spec adds a URL parameter outside `x.{key}` and `p.{Key}`.
The drill-through list's URL carries `from={id | key}` (0033 §9.4 and its example; 0034 §8.1 step 4,
§8.3), which 0030 §4.3's canonical rewrite also strips (K-crud-10). The crumb and the page context
can take the source report from navigation state instead, as §8.2 already does for the title.

**Fix — 0033 §9.4.** Replace "`/{t}/list/{segment}?filter=…&cols=…&from={id}` (spec 0030 §17, spec
0034 §8)" with "`/{t}/list/{segment}?filter=…&cols=…` (spec 0030 §17, spec 0034 §8)". In the
example, replace "and Invoice.CustomerId = 1012&cols=Invoice.SerialNumber,…&from=42`, URL-encoded."
with "and Invoice.CustomerId = 1012&cols=Invoice.SerialNumber,…`, URL-encoded."

**Fix — 0034 §8.1 step 4.** Replace "4. **The URL** is
`/{t}/list/{drill.segment}?filter=…&cols={drill.columns}&from={id | key}`, with no `from` for a
draft." with "4. **The URL** is `/{t}/list/{drill.segment}?filter=…&cols={drill.columns}`; the source
report travels in navigation state (§8.2)."

**Fix — 0034 §8.2.** Replace "`router.navigateByUrl(url, { state: { tmDrill: { fromTitle, chips } }
})`," with "`router.navigateByUrl(url, { state: { tmDrill: { from, fromUrl, fromTitle, chips } } })`,
`from` being the report's id or key (null for a draft) and `fromUrl` its canonical URL with `p.*`,".

**Fix — 0034 §8.3 opener.** Replace "the URL `filter` is a fixed leaf, `cols` the columns, `from` the
source report." with "the URL `filter` is a fixed leaf and `cols` the columns; the source report
comes from `history.state.tmDrill` (§8.2)."

**Fix — 0034 §8.4.** Replace "The crumb's title comes from `reports/get-by-ids` or the coded
declaration, else "Report"." with "The crumb reads "Reports" and links to `/{t}/reports`."

**Fix — 0034 §13 table.** Replace "| `search` (the drill list) | spec 0030's, plus `from` |" with
"| `search` (the drill list) | spec 0030's, plus `from` from `history.state.tmDrill` (null from a bare
link) |".

**Files:** `0033-report-definitions-and-engine.md`, `0034-report-renderers-and-studio.md` (0030 per
K-crud-10).

### K-reports-10 — major — name mismatch with the owner — 0033 §3.1 vs spec 0005 §2.2

**Problem.** `ReportFormat.dateStyle` is `'short' | 'medium' | 'long'`. The platform's date styles are
spec 0005 §2.2's `'numeric' | 'medium' | 'long'` (one name shared with `tm-date-picker`), which 0010
§D.4 and §D.7 extend and 0029 §12.2 uses. `'short'` has no formatter, and the strict document
(`formatVersion: 1`, unknown values refused) freezes the wrong name once definitions are saved.

**Fix — 0033 §3.1.** Replace "  dateStyle?: 'short' | 'medium' | 'long';   // date types" with
"  dateStyle?: 'numeric' | 'medium' | 'long'; // date types; spec 0005 §2.2's names".

**Files:** `0033-report-definitions-and-engine.md`.

---

## Minor

### K-reports-11 — minor — known issue K-F — 0010 Context, Goals / Non-goals

**Problem.** K-F: Part F's Goals and Non-goals bullets and the Context "Other specs" list must cover
Part F (0012 §11.1–§11.2, 0013 §8.2, 0015 §5.2, 0029 §9, 0033, 0035 §6.3). Goals and Non-goals list
Parts A–E only; "Other specs that carry parts of this design" has no Part F line. (K-foundation-14
and K-shell-entity-14 give the same Goals and Context text; this adds the Non-goals bullet.)

**Fix — 0010 Goals.** After the **Part E** bullet add: "- **Part F.** The Queryex changes the data
access layer, the permission evaluator and the report engine need: the seven amendments moved from
spec 0012 (§F.1–§F.7), grouping sets, level navigations, omitted parameters, constant statements,
parameter binding, `'uq'` through a month table, `Exists` and `ComparedWith` (§F.8–§F.15)."

**Fix — 0010 Non-goals.** After the **Parts B–E** bullet add: "- **Part F:** a language-level list
parameter and `descendantOf` over a table-valued parameter (spec 0012 §11.2); window functions for
running totals (spec 0033); a new `QueryexLanguage.Version`."

**Fix — 0010 Context, "Other specs".** Add: "- spec 0012 §11.1–§11.2, spec 0013 §8.2, spec 0015
§5.2, spec 0029 §9, spec 0033, spec 0035 §6.3 — the hosts and consumers of Part F: `TreeLevels`
and `CalendarMap`, the month table, the level-path refusal, `queryex/discover`, the report engine
and the dashboard read-through."

**Files:** `0010-amendments-to-implemented-specs.md`.

### K-reports-12 — minor — wrong pointer — 0034 §12.3

**Problem.** "**Top** is a `tmNumber` hinted with spec 0033 §3.4 rule 9's range." Rule 9 is the
chart's dimension counts; `top`'s ranges are rule 8.

**Fix.** Replace "spec 0033 §3.4 rule 9's range" with "spec 0033 §3.4 rule 8's range".

**Files:** `0034-report-renderers-and-studio.md`.

### K-reports-13 — minor — wrong pointer — 0034 §6.3

**Problem.** "A data cell takes the stronger of its row's and column's style (spec 0033 §12.3's
ladder)". 0033 §12.3 is the pivot's sheet layout and defines no style order; the workbook's row
styles are §12.4's, and they differ (`Subtotal3` exists there, `leaf` here).

**Fix.** Replace "takes the stronger of its row's and column's style (spec 0033 §12.3's ladder);"
with "takes the stronger of its row's and column's style, in the table's order (`total` strongest,
then `subtotal-1`, `subtotal-2`, `leaf`);".

**Files:** `0034-report-renderers-and-studio.md`.

### K-reports-14 — minor — claim not in target — 0033 §10 → 0016 §4.6

**Problem.** 0033 §10: "Surfaces are on by default, so the startup audit lists every one it
projected (spec 0016 §4.6)." 0016 §4.6's audit only checks child segments for uniqueness; nothing
lists the projected surfaces (Q-35's hardening line).

**Fix — 0016 §4.6.** After the bullet ending "resolves a `JsonTypeInfo` (§3.1)." add: "The audit
also logs, once at Information, every child query surface it projected with its owner resource,
since surfaces are on by default (spec 0033 §10)."

**Files:** `0016-crud-web-api.md`.

### K-reports-15 — minor — dangling pointer (xref list) — 0034 §5.1

**Problem.** "`rowHeaders: false` (§C.6), zebra rows of 30 px, a frozen header and the stable gutter
(§C.7)". 0034 has no Part C; the pointers continue "spec 0010 §C.1" in the same sentence but read
as dangling.

**Fix.** Replace "`rowHeaders: false` (§C.6)" with "`rowHeaders: false` (spec 0010 §C.6)" and "the
stable gutter (§C.7)" with "the stable gutter (spec 0010 §C.7)".

**Files:** `0034-report-renderers-and-studio.md`.

### K-reports-16 — minor — numbering gap — 0035 §15, Non-goals

**Problem.** §15 runs §15.1–§15.4, then §15.6 (§15.5 was removed by ruling Q-20). Non-goals cites
"§15.6". No other spec cites 0035 §15.5 or §15.6.

**Fix.** Rename "### 15.6 The seam for per-role and per-user home pages" to "### 15.5 The seam for
per-role and per-user home pages"; in Non-goals replace "(deferred; §15.6 names the seam)" with
"(deferred; §15.5 names the seam)".

**Files:** `0035-dashboards-and-home-page.md`.

### K-reports-17 — minor — wrong pointer — 0034 §15 (Non-blocking row)

**Problem.** "spec 0033's `run-cancel.spec` for the server half": 0033 §15 names no such test; its
`runs` suite pins "an abort reaches SQL".

**Fix.** Replace "spec 0033's `run-cancel.spec` for the server half" with "spec 0033 §15's `runs`
suite ("an abort reaches SQL") for the server half".

**Files:** `0034-report-renderers-and-studio.md`.

### K-reports-18 — minor — claim not in target — 0034 §3.5

**Problem.** "`display` and `format` through `TmFormats` (spec 0029 §12)". `TmFormats.text(value,
of, style)` takes a value descriptor and a `grid`/`details` style; it cannot apply `decimals`,
`grouping`, `percent`, `dateStyle` or `calendar`. The formatters that take them are `TmL10n`'s
(0005 §2.2, 0010 §D.4, §D.7).

**Fix.** Replace "`display` and `format` through `TmFormats` (spec 0029 §12), `enum` through" with
"`display` and `format` through `TmL10n` (spec 0005 §2.2's `formatNumber` and `formatDate`, spec
0010 §D.4 for instants), `enum` through".

**Files:** `0034-report-renderers-and-studio.md`.

### K-reports-19 — minor — example mismatch with the owner — 0034 §3.2, §4.4 vs 0033 §3.5, §8.1

**Problem.** Report 42 is 0033's "Sales by customer" (§3.5, §8.1): `From` (required, defaulted) and
`To` (optional). 0034 §3.2 says its run "echoes `AsOf = 2026-10-20` (`Default`)", and §4.4's example
sends `Customer`, a key report 42 does not declare (0033 §4.4 ignores it; 0034 §4.4 drops it).

**Fix — 0034 §3.2.** Replace "the result echoes `AsOf = 2026-10-20` (`Default`) for the bar." with
"the result echoes `From = 2026-01-01` (`Default`) and `To` (`Omitted`) for the bar."

**Fix — 0034 §4.4.** Replace "**Example —** `/17/reports/42?p.Customer=1012&p.To=` runs `arguments:
{ "Customer": 1012, "To": null }`: `To` is omitted and `From` takes its default." with "**Example
—** `/17/reports/42?p.To=2026-06-30` runs `arguments: { "To": "2026-06-30" }`, and `From` takes its
default."

**Files:** `0034-report-renderers-and-studio.md`.

### K-reports-20 — minor — contradiction — 0034 §7.9 vs 0033 §3.1, §3.4 rule 9

**Problem.** "**Stored types the facade does not draw** (`heatmap`, `treemap`, `gauge`) render as the
table". 0033 refuses those names at save (`ReportDefinition.ChartTypeUnsupported`), so no stored
definition holds one; only a coded report's `ReportChartLayout.Type` can.

**Fix.** Replace "- **Stored types the facade does not draw** (`heatmap`, `treemap`, `gauge`) render
as the table," with "- **A chart type the facade does not draw** (only a coded report can return
one) renders as the table,".

**Files:** `0034-report-renderers-and-studio.md`.

### K-reports-21 — minor — shape mismatch — 0034 §12.9 vs 0025 §10

**Problem.** "calls `TmPageState.markStale`." 0025 §10's `markStale(scope: { tenantId: number;
entity?: string })` takes a scope.

**Fix.** Replace "and calls `TmPageState.markStale`." with "and calls `TmPageState.markStale({
tenantId, entity: 'core.ReportDefinition' })` (spec 0025 §10)."

**Files:** `0034-report-renderers-and-studio.md`.

### K-reports-22 — minor — shape mismatch — 0034 §3.7, 0035 §11.4 vs 0025 §10

**Problem.** 0025 §10: "**Key.** The page's canonical path". 0034 remembers under
`tmPageStateKey('/{t}/reports/{id}/run')` and 0035 under `tmPageStateKey('/{t}/dashboards/{id}/tiles')`,
paths of no route, so `markStale` with an entity (which maps entries through `TmUrls.describe`)
never reaches them.

**Fix — 0025 §10, Key bullet.** Append: "A `page` hatch that remembers apart from its details host
appends one segment of its own (`/17/reports/42/run`, `/17/dashboards/3/tiles`); `markStale` treats
it as its page's path."

**Files:** `0025-client-state-and-cross-tab-sync.md`.

### K-reports-23 — minor — shape mismatch — 0033 §6.2 vs 0035 §7.1

**Problem.** `IAudienceResolver.ResolveAsync(resource, id, root)` serves `core.Dashboard` too (0035
§7.1), but §6.2 step 2 reads permissions "on `core.ReportDefinition` or `*`" and step 3 always runs
`DashboardsUsing`.

**Fix.** In step 2 replace "on `core.ReportDefinition` or `*` with any action" with "on the resource
(`core.ReportDefinition`, `core.Dashboard`) or `*` with any action". In step 3 replace "3.
**Dashboards.** `DashboardsUsing([id])`" with "3. **Dashboards** (`core.ReportDefinition` only).
`DashboardsUsing([id])`".

**Files:** `0033-report-definitions-and-engine.md`.

### K-reports-24 — minor — shape gap — 0033 §8.4, §9.2; 0034 §5.2

**Problem.** §8.4 says a grandchild root's list selects "its top-level owner's too (§9.2)", and 0034
§8.3 opens a grandchild row "on its top-level owner on the intermediate child". §9.2's
`ReportRowIdentity(IdOrdinal, Entity, OwnerIdOrdinal, OwnerEntity, Collection)` holds one owner and
the row's own id, and `?child=` addresses a child of the owner, so a List row over a grandchild root
cannot open.

**Fix — 0033 §9.2 sketch.** Replace "public sealed record ReportRowIdentity(int IdOrdinal, string
Entity, int? OwnerIdOrdinal, string? OwnerEntity, string? Collection);" with "public sealed record
ReportRowIdentity(int IdOrdinal, string Entity, int? OwnerIdOrdinal, string? OwnerEntity, string?
Collection, int? ChildIdOrdinal); // ChildIdOrdinal: a grandchild root's parent id".

**Fix — 0033 §9.2, Identity bullet.** Append: "Over a grandchild root, `OwnerEntity` is the top-level
owner and the row opens `/{t}/{ownerSegment}/{ownerId}?child={Collection}:{childId}`, on the
intermediate child."

**Fix — 0033 §8.4.** Replace "a grandchild its top-level owner's too (§9.2)." with "a grandchild its
parent's key and its top-level owner's (§9.2)."

**Fix — 0034 §5.2.** After "`TmUrls.details(ownerEntity, ownerId, { collection, id })`" insert "(a
grandchild: the intermediate child's id)".

**Files:** `0033-report-definitions-and-engine.md`, `0034-report-renderers-and-studio.md`.

### K-reports-25 — minor — count mismatch — 0033 §12.4

**Problem.** "Four styles plus the phantom:" then names five (`Total`, `Subtotal1`, `Subtotal2`,
`Subtotal3`, `Normal`) plus `Phantom`, which are 0019 §4.4's `ExcelRowStyle`.

**Fix.** Replace "Four styles plus the phantom:" with "The row styles are spec 0019 §4.4's
`ExcelRowStyle`:".

**Files:** `0033-report-definitions-and-engine.md`.

### K-reports-26 — minor — shape mismatch — 0035 §15.2 vs 0010 §D.4, §D.8

**Problem.** The date line calls `l10n.formatDateTime(now, { dateStyle: 'weekday', timeStyle: 'none'
})`, `now` being `TM_NOW`'s epoch milliseconds. `TmL10n.formatDateTime` takes an ISO instant string
(§D.4), and §D.8's example passes `new Date(now).toISOString()`.

**Fix.** Replace "`l10n.formatDateTime(now, { dateStyle: 'weekday', timeStyle: 'none' })`" with
"`l10n.formatDateTime(new Date(now).toISOString(), { dateStyle: 'weekday', timeStyle: 'none' })`".

**Files:** `0035-dashboards-and-home-page.md`.

### K-reports-27 — minor — two names for one thing; export lists — 0035 §1.1, §8.1, §12.1; 0034 §1

**Problem.** 0035 parses a stored dashboard through `tmDashboardModel(entity)` (§8.1) and through
`tmParseDashboard` (§12.1). Neither, nor `TmTileState` (§8.1) or `TmPlacement` (§8.2), is in §1.1's
exports. 0034 §12.8 declares `TmReportOverlay` in `@tellma/core/reports`, which `/report-studio`
imports, but §1's `/reports` row omits it.

**Fix — 0035 §12.1 table.** Replace "`parametersJson` and `settingsJson` parse through
`tmParseDashboard`" with "`parametersJson` and `settingsJson` parse through `tmDashboardModel`
(§8.1)".

**Fix — 0035 §1.1, main-module row.** Replace "`tmPackDashboard`, `tmBindWidgetArguments`,
`tmDashboardPage`" with "`tmPackDashboard`, `tmDashboardModel`, `TmTileState`, `TmPlacement`,
`tmBindWidgetArguments`, `tmDashboardPage`".

**Fix — 0034 §1, `@tellma/core/reports` row.** Replace "`TmReportDrillEvent`, `TmReportParameterField`"
with "`TmReportDrillEvent`, `TmReportOverlay`, `TmReportParameterField`".

**Files:** `0035-dashboards-and-home-page.md`, `0034-report-renderers-and-studio.md`.

### K-reports-28 — minor — shape mismatch — 0035 §12.1 vs 0031 §3.7

**Problem.** 0035 declares `details: { page: … }` with the comment "view, edit and create". 0031 §3.7
renders `page` "in view and edit mode" and `create` "when given" in create mode; with no `create`,
nothing says create mode uses `page`.

**Fix.** Replace "  details: { page: () => import('@tellma/core/dashboards').then((m) =>
m.tmDashboardPage()) }, // view, edit and create" with

```ts
  details: { page: () => import('@tellma/core/dashboards').then((m) => m.tmDashboardPage()),    // view and edit
             create: () => import('@tellma/core/dashboards').then((m) => m.tmDashboardPage()) }, // the same page in create mode
```

**Files:** `0035-dashboards-and-home-page.md`.

### K-reports-29 — minor — shape mismatch — 0031 §3.7 vs 0034 §13

**Problem.** 0031 §3.7: "A hatch may register `dashboard` or `report`". 0034 §13's create hatch (the
studio) registers `studio`.

**Fix — 0031 §3.7.** Replace "A hatch may register `dashboard` or `report`;" with "A hatch may
register `dashboard`, `report` or `studio`;".

**Files:** `0031-details-page.md`.

### K-reports-30 — minor — missing keys in the owner's list — 0013 §10.1

**Problem.** 0013 §10.1's "Reports and dashboards" bullet lists `Core_Dashboard_Synonyms` but not
`Core_ReportDefinition_Synonyms` (0034 §2.3) or `Core_Job_ExportReport` (addendum 9; 0033 §12.1,
0019 §12.2).

**Fix.** Replace "`Notification_core_dashboard_shared`; `Core_Dashboard_Synonyms`." with
"`Notification_core_dashboard_shared`; `Core_ReportDefinition_Synonyms`, `Core_Dashboard_Synonyms`;
`Core_Job_ExportReport` (spec 0019 §12.2)."

**Files:** `0013-settings-localization-and-cache.md`.

### K-reports-31 — minor — unowned piece — 0035 §1.1, §4.1 vs 0024 §3.2

**Problem.** 0024 §3.2: each metadata section's owner implements one `IClientAssetContributor`. 0035
§4.1 says "The emit writes the keys into the Core metadata chunk as `widgetTypes`" but names no
contributor, and §1.1's runtime list has none.

**Fix — 0035 §1.1, Server bullet.** Replace "`DashboardReadThrough`, `WidgetTypeRegistry`)" with
"`DashboardReadThrough`, `WidgetTypeRegistry`, `WidgetTypeClientAssetContributor`)".

**Fix — 0035 §4.1 table.** Replace "| The emit | writes the keys into the Core metadata chunk as
`widgetTypes` (spec 0024 §3.1)," with "| The emit | `WidgetTypeClientAssetContributor`, the section's
`IClientAssetContributor` (spec 0024 §3.2), writes the keys into the Core metadata chunk as
`widgetTypes` (spec 0024 §3.1),".

**Files:** `0035-dashboards-and-home-page.md`.

### K-reports-32 — minor — inbound wrong pointers and paraphrases — 0018 §1.3, 0024 Appendix A, 0023 §4

**Problem.** Three specs point into this cluster's contracts imprecisely:
- 0018 §1.3's seams table: "| `ReportSeed`, `ShareAccess`, `IReportResults` | spec 0033 §5.1, §11,
  §13 |". `ShareAccess` is declared in 0033 §2.1.
- 0024 Appendix A: "| `coded-report-source` | a coded report without exactly one source |". 0034
  §2.2 also admits `component` with `route`.
- 0023 (line 382): "An absolute time of day is `TmL10n.formatTime` (spec 0035 §8.5)." The rule's
  owner is 0034 §3.6; 0035 §8.5 cites it.

**Fix — 0018 §1.3.** Replace "spec 0033 §5.1, §11, §13" with "spec 0033 §2.1, §11, §13".

**Fix — 0024 Appendix A.** Replace "a coded report without exactly one source" with "a coded report
whose sources are not exactly one of `route`, `run` and `component`, or `component` with `route`".

**Fix — 0023.** Replace "(spec 0035 §8.5)" in that sentence with "(spec 0034 §3.6)".

**Files:** `0018-core-and-gl-reference-stacks.md`, `0024-client-app-foundation.md`,
`0023-component-library-additions.md`.

### K-reports-33 — minor — repetition — 0033 §4.4, §8.5, Review flag 3 vs 0010 §F.8, §F.10

**Problem.** 0033 §4.4 restates §F.10's folding rule and copies five of its six table rows (the
sixth, `if(Amount > @Limit, …)`, is 0033's own). 0033 §8.5 step 5 restates §F.8's `Having` rule word
for word, and 0033 flag 3 duplicates Part F Decision 9 and flag 6: one decision, two flags.

**Fix — 0033 §4.4.** Replace the bullet "- **Omitted means no restriction.** The run compiles with
`OmittedParameters` (spec 0010 §F.10). In a filter, … Anywhere else the parameter is a typed absent
value." and the table that follows it (the eight lines from "| Clause, `@Customer` and `@Other`
omitted | Effective |" to the `if(Amount > @Limit, …)` row) with:

> - **Omitted means no restriction.** The run compiles with `OmittedParameters`, so spec 0010 §F.10
>   folds every atom that mentions an omitted parameter in a filter, a `having` or a measure's
>   condition (`sum(Amount, CustomerId = @Customer)` becomes `sum(Amount)`). Elsewhere the parameter
>   is a typed absent value: `if(Amount > @Limit, 'Big', 'Small')` gives `'Small'`.

**Fix — 0033 §8.5 step 5.** Replace "5. **Having** filters the finest set's rows only. Subtotals and
totals include every row the filter admitted, so a collapsed member never shows an empty total and
a visible subtotal can exceed the sum of its visible children." with "5. **Having** filters the
finest set's rows only (spec 0010 §F.8), so a collapsed member never shows an empty total."

**Fix — 0033 Review flags.** Delete flag 3 ("**`Having` filters the finest set only**"); in 0010 Part F
flag 6 replace "Flips when a second consumer wants SQL's semantics." with "Flips when a second
consumer wants SQL's semantics, or report users expect totals to drop with filtered leaves."
Renumber per K-reports-1.

**Files:** `0033-report-definitions-and-engine.md`, `0010-amendments-to-implemented-specs.md`.

### K-reports-34 — minor — repetition — 0033 §4.4 vs 0034 §4.4

**Problem.** 0033 §4.4's "URL form" bullet restates 0034 §4.4's codec (`?p.{Key}=`, empty is `null`,
absent takes the default), and 0034 §4.4 in turn defines its text as "spec 0033 §4.4's
culture-free text", a term 0033 never defines (0033 says "the platform scalar encoding … (spec 0016
§3.2)").

**Fix — 0033 §4.4.** Replace "- **URL form.** Spec 0034 §4.4 carries arguments as `?p.{Key}=` the
culture-free text of the type (`p.From=2026-01-01`, `p.Customer=1012`). An empty value is `null`; an
absent key takes the default." with "- **URL form.** Spec 0034 §4.4 carries arguments in the URL."

**Fix — 0034 §4.4.** Replace "- **Text** is spec 0033 §4.4's culture-free text, encoded per spec 0024
§9.5." with "- **Text** is the value's platform scalar encoding (spec 0016 §3.2) without JSON quotes,
encoded per spec 0024 §9.5; §4.1's table shows each type."

**Files:** `0033-report-definitions-and-engine.md`, `0034-report-renderers-and-studio.md`.

### K-reports-35 — minor — repetition — 0033 §7, 0035 §7.2 vs 0021 §2.3

**Problem.** Both restate 0021 §2.3's catalogue row after citing it: 0033 "category `reports`,
`Info`, `Toast = false`, target `core.ReportDefinition`, arguments `{ title, actorName }`"; 0035 "the
same category, severity, `Toast = false`, arguments, …".

**Fix — 0033 §7.** Replace "The catalogue row is spec 0021 §2.3's: category `reports`, `Info`,
`Toast = false`, target `core.ReportDefinition`, arguments `{ title, actorName }`." with "The
catalogue row is spec 0021 §2.3's; this section states what the raiser decides."

**Fix — 0035 §7.2.** Replace "A newly added user grant tells the grantee, exactly as spec 0033 §7
does for reports: the same category, severity, `Toast = false`, arguments, per-slot requests and
withdrawal (spec 0021 §2.3)." with "A newly added user grant tells the grantee, with spec 0033 §7's
per-slot requests and withdrawal; the catalogue row is spec 0021 §2.3's."

**Files:** `0033-report-definitions-and-engine.md`, `0035-dashboards-and-home-page.md`.

### K-reports-36 — minor — repetition — 0035 §10.3 vs 0034 §4.2, 0025 Appendix A

**Problem.** "The bar" row cites 0034 §4.2's layout, then restates it ("a wrapping row ended by Reset
that becomes a Parameters button, a summary and an end drawer below `sm`; fields commit on pick,
Enter or blur"); "Precedence" restates 0025 Appendix A's value and class.

**Fix.** In "The bar" row replace "in spec 0034 §4.2's layout, a wrapping row ended by Reset that
becomes a Parameters button, a summary and an end drawer below `sm`; fields commit on pick, Enter or
blur" with "in spec 0034 §4.2's layout". In "Precedence" replace "`dashboard.<id>.params` (spec 0025
Appendix A: `{ v: 1, args }` of supplied values, write-behind)" with "`dashboard.<id>.params` (spec
0025 Appendix A)".

**Files:** `0035-dashboards-and-home-page.md`.

### K-reports-37 — minor — repetition; wrong pointer — 0034 §8.3 (K-crud-19)

**Problem.** "The list is never pinned and never a recent (spec 0024 §9.4)." repeats 0030 §17, and
0024 §9.4 covers pinning only; recents are 0025 §13.

**Fix.** Replace "The list is never pinned and never a recent (spec 0024 §9.4)." with "Pins and
recents: spec 0030 §17." (as K-crud-19).

**Files:** `0034-report-renderers-and-studio.md`.

### K-reports-38 — minor — process words — 0035 Decision 6, §8.1, §15.2, §15.3, Decision 13, §18; 0034 Decision 12

**Problem.** "**Dashboards Q1:** …" (0035 Decision 6) is the brief's question id. "The design" points
at UI canvases outside the repository (`ui-design` is git-ignored), so a frozen spec cannot cite
them: 0035 §8.1 "as the design's home body", §15.2 "The design's location suffix (…) is not shipped:
nothing supplies it, and a hatch can", §15.3 "the design's two columns", Decision 13 "Alternative:
the design's 5 and 2.", 0034 Decision 12 "as the design, Excel and AG Charts do". 0035 §18's "the
edits listed under Context" reads as a change log.

**Fix — 0035.** Decision 6: replace "**Dashboards Q1: same-key binding by default;" with "**Same-key
binding by default;". §8.1 table: replace "padded 14×16 px (11×10 below `sm`), as the design's home
body" with "padded 14×16 px (11×10 below `sm`)". §15.2: replace "The design's location suffix ("·
Riyadh plant") is not shipped: nothing supplies it, and a hatch can" with "No location suffix
follows the date; a `home` hatch can add one". §15.3: replace "**Wide** shows the design's two
columns (7 : 5)" with "**Wide** shows two columns (7 : 5)". Decision 13: replace "Alternative: the
design's 5 and 2." with "Alternative: 5 and 2 rows." §18: replace "the edits listed under Context
are in place" with "the parts the specs under Context carry are in place".

**Fix — 0034 Decision 12.** Replace "as the design, Excel and AG Charts do" with "as Excel and AG
Charts do".

**Files:** `0035-dashboards-and-home-page.md`, `0034-report-renderers-and-studio.md`.

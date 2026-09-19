# Theme T9 — Excel codec: export and import (future spec 0018)

Design proposal carrying all three lenses (distro-author simplicity, tier-2 performance and
operations, correctness and security). Everything here is written for a reader who has not seen
the other design files. Names are final-looking. C# is illustrative of shape; the spec translates
it into the neutral contract notation.

---

## 0. Lens checks

### Lens A — distro-author simplicity and AI-native authoring

**How many lines does a distribution write to make an entity exportable and importable?** Zero.
Every entity that is queryable gets display-shape export; every entity that is savable and keyed
gets export-for-import, inspect, and import. The codec derives the sheet layout, the column set,
the header labels, the cell encodings, the validation lists, and the natural keys from the entity
metadata that the data layer already computes from the class and the EF model. The only optional
lines are `[NaturalKey]` on a property whose uniqueness the distribution wants to be the row
identity or the reference key (one attribute per key, typically one per entity — `Email` on `User`,
`Code` on `Center`), and `[ExcludeFromExcel]` on an editable property that must never appear in a
sheet (a JSON settings bag, for example). Nothing is registered, no mapping class is written, no
DTO exists.

**Is every capability declared once?** Yes. "Excel" is not a capability an author declares; it is a
projection of two capabilities the service pipeline already knows: *queryable* (⇒ `Export`) and
*savable with a key* (⇒ `ExportForImport`, `InspectImport`, `Import`). A tree entity gets
in-sheet parent references and topological ordering from its tree capability without any Excel-
specific declaration. The natural key is declared once on the entity and consumed by import, by
export-for-import, and — later — by MCP tools that accept natural keys instead of ids.

**What does the MCP consumer get?** The same four service operations. `export_records` (display
shape, by query or by ids) and `import_records` (from a staged file, with mode and row key) map one
to one onto `ExportAsync` and `ImportAsync`; a template for an entity is `ExportForImportAsync`
with an empty id list. No codec work is specific to MCP.

### Lens B — tier-2 performance and operations

**DB round trips per operation (common case, after the connect call collapses into the first
business round trip as the pipeline design proposes; add one otherwise):**

| Operation | Round trips | What each carries |
|---|---|---|
| Export (display, by query or by ids) | 1 | connect + the grid query with `Take = cap + 1` (ids ride a TVP restriction) |
| ExportForImport by ids | 1 | connect + parent flat query + one flat query per child collection, all restricted by the id TVP |
| ExportForImport by query | 2 | connect + parent flat query (`Take = cap + 1`); then child queries restricted by the returned parent ids |
| InspectImport | 0 | reads the manifest, header rows and sheet dimensions from the staged file only |
| Import, Insert without references | 2 | connect + validation context; persist |
| Import, any mode with references or Update/Upsert | 3 | connect + resolution/hydration (row keys, every reference key, existing children); validation context; persist |
| Import, background, N chunks | 3 per chunk | the same three, per chunk of `ImportChunkRows` |

**N+1 hunting.** Reference resolution is one query per distinct *(target entity, key property)*
pair, never per row and never per column: two columns that both reference `Center` by `Code`
share one query, and one TVP carries every distinct value from every row. Existing children are
loaded with one query per child collection restricted through the parent's key path
(`User.Email in @keys`), not per parent. Row-key resolution and hydration are the same statement:
the flat query that finds the existing rows also returns their editable columns. In-sheet parent
references (trees) cost no query at all.

**Locks and connections held across I/O.** None. Export drains the data reader into a temp file
on local disk and releases the connection before the first byte reaches the HTTP response or the
blob store. Import parses the whole file (from a staged blob copied to local disk) before the
first DB call; the transaction opens at the persist statement, inside the pipeline, after every
file read has completed. Resolution and validation reads run outside the transaction under
read-committed snapshot; the unique indexes and foreign keys in the persist batch remain the
guarantee, as they are for a JSON save.

**Plan-cache friendliness.** Resolution queries are compiled Queryex queries whose only variable
part is a TVP parameter and the RLS predicate the user already has in every query, so each
(entity, key) pair has one plan per tenant. Chunked background imports reuse the same statements
chunk after chunk.

**Observability from day one.** Meter `Tellma.Core.Excel` with row histograms, durations,
resolution-query counts per import, spilled shared-string tables, truncated exports, error
counts, and a `background` tag — names in §3 and D16.

### Lens C — correctness, security, long-term maintainability

**Where could a stale cache leak data?** (1) The header-label catalogue and the language symbol
table are cached per (entity, tenant settings tag, resource version); a stale copy would mislabel
`Name2` after an admin swaps tenant languages. The cache key includes the tenant settings tag the
settings theme owns, and the connect step reads that tag before the export runs, so the export
cannot use labels older than the request. (2) Natural-key resolution never reads the cacheable-
entity cache; it always queries the database with the user's read filter. (3) Permissions and RLS
come from the same evaluation the query and save operations use; export runs the same compiled
query the grid runs, import runs the same pipeline a JSON save runs, so there is no separate
authorization path to drift.

**Which write path bypasses the tag bump?** None: import persists through the bulk save emitter
that bumps cache tags for every table a batch writes. The codec never emits an `INSERT`, `UPDATE`
or `DELETE`.

**No silent data loss.** Values Excel cannot carry exactly (`long`, decimals beyond 15 significant
digits, `DateTimeOffset`, `Guid`) are written as text cells in export-for-import and parsed back
exactly. A blank cell in a mapped column is an explicit null, never "skip"; a write-once column
whose value differs from the stored value is an error, never silently ignored; an unmapped column
is an error unless the caller says to ignore it; a truncated export is flagged in the manifest and
in the response. Background imports that commit chunk by chunk report exactly which row ranges
were committed.

**Fail closed.** Import requires the same `save` permission and RLS pre- and post-checks as Save.
Reference resolution applies the *target* entity's read filter, so a user cannot discover or
reference rows they cannot read. The manifest is untrusted input: every path it names is checked
against the target entity's metadata, and its `Id` and stamp columns are honoured only when its
source tenant equals the target tenant — and even then only to *select* rows the RLS pre-check
then verifies.

**Schema evolution.** The manifest carries a manifest-format version and a per-entity schema
fingerprint. A fingerprint mismatch is a warning: mapping is by header and by path, so a file
exported from platform N imports into N+1 as long as its columns still exist; columns that no
longer exist surface as unmapped. The public surface a distribution compiles against is two
attributes, four request/response records, and one options record; everything else is internal.

---

## 1. Critique of the brain dump

The four export sections and the import section describe the right product: the grid as a sheet,
an editable sheet that round-trips, natural keys instead of ids, bulk translation, tree import,
language-aware mapping, insert/update/merge with hydration. The following points are wrong,
underspecified, or internally inconsistent.

1. **"The values are exported in the Excel sheet raw (numbers, booleans)" is not achievable for
   every type.** Excel keeps 15 significant digits: a `long` id, a `decimal(19,4)` amount above
   99,999,999,999.9999, and any `Guid` are corrupted or impossible as numbers. Dates are not raw
   either — they are serial numbers relative to a date system the workbook chooses. The design
   needs a per-type encoding table with text-cell fallbacks, not a blanket "raw".
2. **"Localization metadata is ignored during import" is right but too weak.** Import must never
   consult a cell's number format at all — not even to decide date versus number. The mapped
   property's type decides; the format is display only. Stating it that strongly removes an entire
   class of locale bugs (a serial in a column a user reformatted as text, a date typed in a number-
   formatted column).
3. **The natural-key inference chain ends in keys that cannot identify anything.** "…otherwise the
   first text column, otherwise the first column" yields non-unique columns; a reference resolved
   through them is ambiguous by construction and a row identified through them cannot be updated.
   The chain must stop at unique candidates and fall back to the surrogate id — and the brain dump
   already concedes that surrogate ids "work reliably within tenants, never across". The fallback
   should be explicit and flagged in the file, not a best effort.
4. **Tree import "using the surrogate key to reference a row's parents" contradicts the rule that
   every FK is a natural key, and cannot work for a parent that is new in the same sheet** (no id
   exists yet). Parents are references like any other: resolved by natural key, in-sheet first.
5. **Same-tenant round trips are the dominant use case and the brain dump does not support them
   well.** Export, fix 500 rows, import: if the natural key is the thing being fixed (renaming
   codes, correcting emails) a natural-key row identity cannot express it, and nothing detects
   that a row changed between export and import. The editable sheet needs the surrogate id and
   the concurrency stamp, guarded so they are used only when the file came from the same tenant.
6. **Update semantics for children are undefined.** "Partial sheets should not wipe out the
   remaining properties" says nothing about child collections: does a child sheet with no rows for
   a parent delete that parent's children? The save contract (empty collection deletes, missing
   collection is untouched) has to be mapped onto sheets explicitly.
7. **Blank-cell semantics are undefined.** A mapped column with a blank cell in Update mode is
   either "set to null" or "leave alone"; both are defensible and the choice changes what a partial
   sheet means.
8. **No manifest.** Nothing in the brain dump records which language each `Name` column was
   exported in, which natural key each reference column used, which calendar dates were written
   in, or which tenant and schema the file came from. The header row alone cannot carry this
   (users rename headers; "Name (E)" parses, "Center / Code" parses, a user's "Cost centre" does
   not). A hidden manifest sheet is required.
9. **Ethiopian calendars are unaddressed.** Excel has no Ethiopian calendar; a tenant on that
   calendar cannot get calendar-aware date rendering from a number format. The export needs a
   text-date fallback and the importer needs to parse it.
10. **The synchronous/background boundary is missing.** "Up to a limit" is stated for export but
    the limit is not named, nothing says what happens above it, and import has no size or row
    ceiling at all. A 1M-row import in one transaction is not realistic (log growth, lock
    escalation at 5,000 locks, a lease that must be renewed for an hour). Chunked background
    import with per-chunk atomicity is what every industrial tool does, and it has to be a
    deliberate decision because it changes the all-or-nothing promise.
11. **Error reporting is unspecified.** The pipeline's validation errors are keyed by entity path
    (`[15].RoleMemberships[2].RoleId`); an Excel user needs sheet, row and column. The mapping
    from one to the other is codec work and must be in the spec.
12. **The file has no intake path.** Every web endpoint is JSON over POST; an import file is
    binary. The blob theme is designing staged uploads with tokens and an orphan sweep; import
    should ride that rather than invent multipart.
13. **Four export operations are two axes.** `ExportByQuery`, `ExportByIds`,
    `ExportByQueryForImport`, `ExportByIdsForImport` are *source* (query | ids) × *shape*
    (display | editable). Two operations with a request that carries either a filter or an id list
    halve the surface for the pipeline, the endpoint projection, and the MCP tool list.
14. **"Merge" is a poor name.** It collides with SQL `MERGE` (banned by this exercise) and with
    JSON merge-patch. `Upsert` is the industry term (Salesforce, Dataverse alternate keys).
15. **Security gaps.** Nothing says whether resolving a reference by natural key must respect the
    user's read filter on the referenced entity (it must, or the importer is an existence oracle);
    nothing bounds the uncompressed size of an uploaded package (zip bombs); nothing says the
    manifest is untrusted.
16. **Small things.** Sheet names are capped at 31 characters and cannot contain `/ \ ? * : [ ]`;
    `ExportByIds` does not say in which order rows come back; `Search` is not mentioned for export
    though the grid uses it; export-for-import of zero rows is the obvious "download a template"
    and is not mentioned; nothing distinguishes an export "read" from a bulk-extraction permission.

---

## 2. Decisions

### D1 — Library: Open XML SDK 3.5.1 directly, forward-only, in `Tellma.Core.Excel`

**Decision.** The codec is written on `DocumentFormat.OpenXml` 3.5.1 (MIT) using
`OpenXmlWriter` for sheets on export and `OpenXmlReader` for sheets on import. The package is
`src/core/Tellma.Core.Excel/` (Core family; references `Tellma.Core.Abstractions` and
`Tellma.Core`), tests in `test/core/Tellma.Core.Excel.Tests/` (pure, no DB) and
`test/core/Tellma.Core.Excel.IntegrationTests/` (LocalDB, `Category=Integration`). Public,
distribution-facing types live in `Tellma.Core.Abstractions` under namespace
`Tellma.Core.Abstractions.Excel`; the writer, reader, planner and resolver are internal.

Export writes inline strings (`t="inlineStr"`), a `styles.xml` with one `cellXfs` entry per
distinct number format used, `date1904="0"`, serials from the 1899-12-30 epoch, a frozen bold
header row with autofilter, and column widths from label length and type. Import reads
`workbookPr/@date1904`, loads `styles.xml` as a small DOM (only to know nothing — see D4: formats
are never consulted for parsing; the part is read only to validate the package), streams the
shared-string part into a `SharedStringStore` that keeps up to `ExcelOptions.MaxInMemorySharedStrings`
entries in memory and spills beyond that to an offset-indexed temp file, and reads each sheet
part forward-only.

**Rationale.** It is the only MIT, Microsoft-maintained option that (a) keeps memory flat with row
count in both directions, (b) can emit everything the editable shape needs — `dataValidation`
lists, a hidden sheet, `sheetProtection`, freeze panes, autofilter, hidden columns, RTL sheet
views — and (c) carries no transitive license risk into distributions (each is a separate legal
entity that would otherwise need its own EPPlus license or NPOI fee assessment). The cost — roughly
a thousand lines of platform code — is paid once.

**Rejected.** ClosedXML (DOM; OOM reported near 350k rows; SixLabors.Fonts split license as a
transitive dependency that a distribution must never reference directly); EPPlus (Polyform
Noncommercial; per-developer commercial license and a license key in configuration for every
distribution author); NPOI (binary maintenance-fee EULA; SkiaSharp native binaries in every web
host); MiniExcel (Apache-2.0 and streaming, but no data validation, no protection, no defined
names, no custom XML parts — the manifest and validation lists would need a second Open XML pass
anyway; 2.0 is a preview with a breaking split). Sylvan.Data.Excel is kept as a named reading
accelerator if the hand-written SAX reader proves slow; not adopted now (second parsing path).

**Confidence.** High. **Review flag.** MiniExcel 1.46 plus a small Open XML post-pass would ship
faster; it is a defensible alternative if the thousand lines are judged too expensive for the
first release.

### D2 — Four service operations; Excel is a projection of existing capabilities

**Decision.** The CRUD service pipeline exposes, for any entity that is queryable:

```csharp
Task<ExportOutcome> ExportAsync(ExportRequest request, CancellationToken ct);
```

and for any entity that is savable and keyed, additionally:

```csharp
Task<ExportOutcome> ExportForImportAsync(ExportForImportRequest request, CancellationToken ct);
Task<ImportPlan>    InspectImportAsync(InspectImportRequest request, CancellationToken ct);
Task<ImportOutcome> ImportAsync(ImportRequest request, CancellationToken ct);
```

`ExportRequest` and `ExportForImportRequest` carry *either* `Ids` *or* the query clauses
(`Filter`, `OrderBy`, `Search`, `Arguments`); supplying both or neither is a caller error. With
`Ids`, rows come back in the order of the id list (a `Take` is not applicable). An empty `Ids`
array is valid for `ExportForImportAsync` and yields the entity's import template: headers,
validation lists, manifest, no rows. Endpoints, permission actions, and the MCP tools are
projected from these four methods by the web theme; the codec contributes no endpoint of its own.

**Rationale.** Source × shape as one request halves the surface; the template falls out for free;
the MCP tool list stays at two.

**Rejected.** Four `ExportBy…` methods (brain dump): pure duplication. A separate `Template`
operation: an empty export already is one.

**Confidence.** High.

### D3 — Display-shape export: the grid as a sheet, spooled, capped

**Decision.** `ExportAsync` runs exactly the query `GetByQuery` would run for the same request
(same `Select`, `Filter`, `OrderBy`, `Search`, `Arguments`, same RLS composition, `Skip = 0`,
`Take = cap + 1`, no ancestors, no count) and writes one sheet:

- Sheet name: the entity's plural label in the request language, scrubbed of `/ \ ? * : [ ]`,
  trimmed to 31 characters, never `History`.
- Header row: `request.Headers[i]` when supplied (a parallel array the SPA sends from its grid
  definition), else derived: for a column with a `Path`, the localized property label chain
  (`Center / Code`, `Name (E)`); for a computed expression, the select item's source `Text`.
- One column per `QueryexColumn`, encoded by D4 with the number format of D5 from the request
  culture, calendar and time zone.
- RTL sheet view when the request language is right-to-left. Frozen header, autofilter, widths.

The rows are drained from the data reader into a temp file under the scratch directory opened
with `FileOptions.DeleteOnClose`; the connection is released when the reader completes; the file
is then copied to the HTTP response (synchronous) or uploaded to the blob store (background).

Caps: `ExcelOptions.MaxSynchronousExportRows` (default 100,000). When the query returns
`cap + 1` rows the synchronous export **fails** with `ExcelErrorCodes.ExportRowLimitExceeded`
(carrying the cap) instead of silently truncating; the SPA offers "export in the background", which
is the same request with `Background = true`. A background export is capped at
`ExcelOptions.MaxExportRows` (default 1,048,575 — one sheet under a header); beyond it the task
fails with the same code. No multi-sheet chunking.

**Rationale.** Silent truncation is data loss the user cannot see; a hard, named cap with a
background path is honest. Spooling to disk keeps the DB connection short and the working set
flat regardless of client bandwidth. One sheet keeps "same as the grid" literally true.

**Rejected.** Streaming the package directly to the response (holds the connection open for the
duration of a slow download and needs a seekable stream anyway); auto-promotion to background when
the cap is hit (the response shape would change mid-request); multi-sheet chunking (breaks
sorting and filtering across the export; a >1M-row extract belongs to the reporting tier).

**Confidence.** High on shape; medium on the default caps. **Review flag.** 100,000 synchronous
rows may be generous for Azure SQL elastic pools; 50,000 is the alternative.

### D4 — Cell encoding per property type (both shapes)

**Decision.** The codec encodes by the *property or column type*, never by value, so a column is
homogeneous and sortable; the importer decodes by the *mapped property type*, never by cell format.

| Type | Display export | Export-for-import | Import accepts |
|---|---|---|---|
| `bool` | boolean cell | boolean cell + validation list `TRUE,FALSE` | boolean; text `true/false/yes/no/1/0` and the localized labels |
| `byte`, `short`, `int` | number | number, format `0` | number; text parsed invariant |
| `long` | number (precision loss above 15 digits documented) | **text cell**, column format `@` | number or text |
| `decimal(p,s)` | number, format from scale | number; **text when the value needs > 15 significant digits**; column format from scale | number; text parsed invariant (`NumberStyles.Number`, no thousands separator accepted in text) |
| `double`, `float` | number | number | number or invariant text |
| `string` | inline string, column format `@` | inline string, column format `@`, `dataValidation` `textLength ≤ MaxLength` when declared | any cell; numbers are converted with invariant formatting; trimmed |
| enum-as-string | localized label | stored value + validation list of stored values (when the joined list ≤ 255 chars) | stored value, or any tenant-language/English label |
| `DateOnly` | serial + date format (D5) or text (Ethiopian) | serial + date format, or text in the manifest's calendar and pattern (Ethiopian) | serial (date1904-aware; `< 61` refused as suspect), ISO 8601 text, text in the column's manifest calendar+pattern, else the request calendar's short pattern |
| `DateTime` | serial with fraction + datetime format | same as `DateOnly` with time tokens | as above, plus time |
| `DateTimeOffset` | converted to the request time zone, serial + format | **text**, ISO 8601 with offset, `@` | ISO 8601 text; a serial is interpreted in the request time zone |
| `TimeOnly` | fraction + `hh:mm:ss` | same | fraction or `HH:mm[:ss]` text |
| `Guid` | text | text, `@` | text |
| `byte[]`, JSON-typed, `hierarchyid`, tree bookkeeping, audit columns | included in display export only when selected, as text | **excluded** | — |
| null | empty cell | empty cell | blank ⇒ null (D9) |

Cells that begin with `=`, `+`, `-`, `@` are safe: inline string cells are never formulas in
SpreadsheetML (formulas are `<f>` elements), so the CSV-injection class does not exist for `.xlsx`.
This must be re-evaluated if CSV is ever added.

**Rationale.** Homogeneous typed columns are what Excel users sort and filter; text fallbacks are
the only lossless carrier for wide types; deciding by property type removes the cell-format
guesswork that every generic importer gets wrong.

**Confidence.** High. **Review flag.** `long` as text in the editable shape is ugly for users who
expect numeric ids; the alternative (number below 2^53, text above) makes a column heterogeneous.

### D5 — Localization metadata is a per-column number format; Ethiopian falls back to text

**Decision.** Number formats are built from the request context (culture, calendar) at export
time and stored as one `numFmt` per distinct string:

- Numbers: `#,##0` for integers, `#,##0.` + `0`×scale for decimals. Separators in format codes are
  locale-independent placeholders; Excel renders them per the viewer's locale.
- Dates: the culture's `ShortDatePattern` translated token by token (`d`→`d`, `dd`→`dd`,
  `M`→`m`, `MM`→`mm`, `MMM`→`mmm`, `MMMM`→`mmmm`, `yy`→`yy`, `yyyy`→`yyyy`, literal separators
  quoted); times `HH:mm:ss` → `hh:mm:ss`, `tt` → `AM/PM`. Prefixed with the locale tag
  `[$-CCLLLL]` where `LLLL` is the culture's LCID in hex (omitted for custom cultures whose LCID
  is 0x1000) and `CC` is the calendar code: none for Gregorian, `06` for Hijri (arithmetic),
  `17` for Um Al Qura. Example: `[$-170401]dd/mm/yyyy` for an Arabic, Um Al Qura request.
- Ethiopian (and any calendar without a Windows calendar id): date cells are written as **text**
  rendered by the platform's calendar-aware formatter in the culture's short pattern; the manifest
  records `DateEncoding = Text`, `Calendar = Ethiopian`, and the pattern per column, and the
  importer parses that column with them. This applies to both shapes; in the editable shape the
  round trip is exact because export and import share the pattern.

Import never reads a format. The only thing the manifest's calendar and pattern influence is the
parsing of *text* date cells.

**Rationale.** Excel's number-format grammar already carries LCID, calendar and pattern; Hijri and
Um Al Qura render natively; nothing else is possible for Ethiopian. Keeping the metadata in the
format string (and mirrored in the manifest) means a Gregorian user importing an Ethiopian-
exported file still gets correct dates.

**Rejected.** A second "display" date column next to a serial column for Ethiopian tenants
(doubles columns, confuses mapping); exporting Gregorian serials to Ethiopian users (correct data,
unusable sheet).

**Confidence.** Medium-high: the `[$-CCLLLL]` encoding is triangulated across Microsoft's grammar,
Windows calendar ids and LibreOffice's documentation, not stated on one Microsoft page. A round-
trip test against desktop Excel is part of the milestone plan.

### D6 — Editable-shape workbook: one parent sheet, one sheet per child collection, one hidden manifest sheet

**Decision.** `ExportForImportAsync` produces:

1. **Parent sheet** named as in D3. Columns in order: `Id` (number for `int` keys, text for
   `long`), a hidden `Stamp` column (the concurrency token's wire string, `@`), then every
   *editable* scalar property in declaration order — with each foreign key replaced by one
   reference column per key property (`Center / Code`, `Parent / Code`, `Role / Name (E)`), and
   each multilingual group expanded into one column per configured tenant language
   (`Name (E)`, `Name (ع)`; plain `Name` for a monolingual tenant). `request.Columns` restricts
   the set to a subset of paths (always keeping `Id` and `Stamp`).
2. **One child sheet per child collection** when `request.IncludeChildren` (default true), named
   `<Parent plural> · <Collection label>` truncated to 31 characters (the manifest maps sheet
   names, so truncation is safe). Columns: the parent reference columns first (`User / Email`),
   then the child's `Id`, then the child's editable properties and reference columns as above.
   Child rows are grouped under their parent in parent order.
3. **A hidden sheet `_tellma`** holding the manifest (layout in §4), protected without a password
   against accidental edits.

Headers are localized labels in the request language. Validation lists (D4) are attached to
boolean and enum columns; `@` to text-typed columns; the header row is frozen and filterable;
data cells are unlocked (no sheet protection on data sheets, so users can insert rows and sort).
Reference dropdowns backed by lookup sheets are a non-goal (D18).

Reference keys per foreign key: `request.ReferenceKeys` (`{"CenterId": "Code"}`) overrides the
target entity's default natural key (D7); a target with no unique natural key is exported by
surrogate id (`Center / Id`) and the manifest marks the column `KeyKind = Surrogate`.

Per-sheet cap: `ExcelOptions.MaxExportRows` per sheet (children included); exceeding it fails
the export with `ExportRowLimitExceeded` naming the sheet.

**Rationale.** One sheet per collection keyed by the parent's natural key is the shape Dynamics
365 F&O staging and its Excel add-in use; it keeps every sheet a rectangular table that sorts,
filters and pastes cleanly, unlike Odoo's blank-parent-cell rows. `Id` and `Stamp` give same-
tenant round trips exact identity and change detection (the Dataverse hidden-column pattern),
guarded by the manifest's source tenant (D9). A hidden sheet is the only manifest carrier every
spreadsheet application (Excel, LibreOffice, Google Sheets) round-trips; custom XML parts and
defined names are not known to survive Google Sheets.

**Rejected.** Children as extra rows under the parent with blank parent cells (Odoo);
children in a JSON cell; a custom XML part as the sole manifest carrier (portability unverified);
a visible manifest sheet (users delete or edit it).

**Confidence.** High on layout; medium on carrying `Id`/`Stamp`. **Review flag.** Omitting `Id` and
`Stamp` gives a purer "natural keys only" file at the cost of same-tenant bulk edits of the key
columns themselves and of change detection between export and import.

### D7 — Natural keys: one attribute, unique-index-backed, inferred when absent, surrogate fallback

**Decision.** The entity contract (owned by the data-access theme, consumed here) gains:

```csharp
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class NaturalKeyAttribute : Attribute
{
    /// <summary>Preference among the entity's natural keys; the lowest is the default.</summary>
    public int Order { get; init; }
}
```

Rules:

- A `[NaturalKey]` property on a top-level entity must be backed by a single-column unique index
  in the EF model (filtered `WHERE [col] IS NOT NULL` when nullable). On a child entity it must be
  backed by a unique index on `(ParentKey, Property)`. Violations fail the aggregated startup
  validation, not a request.
- A multilingual group (`Name`/`Name2`/`Name3`) is one natural key named after its primary member;
  the slot used for a given file column is chosen per language (D8).
- **Inference** when no attribute exists, in this order and only among properties backed by a
  single-column unique index: `Name` (group), `Code`, then required unique string properties in
  declaration order, then nullable unique string properties. The default key is the first
  candidate. Declared keys precede inferred ones; the ordered list is
  `EntityMetadata.NaturalKeys`.
- Composite natural keys are a non-goal for this release (D18); a composite unique index is not a
  candidate.
- A reference may be resolved through **any** scalar property of the target when the caller says
  so (`ReferenceKeys` on export, `ColumnMappings` on import) — including non-unique ones — and
  ambiguity is then a validation error per D10. Row identity (D9) accepts only a natural key or
  `Id`.
- **No natural key** on a target: references export by surrogate `Id`, flagged `Surrogate` in the
  manifest; import of such a column into a different tenant is a mapping error
  (`ExcelErrorCodes.SurrogateReferenceFromOtherTenant`) unless the caller remaps it. A startup
  *warning* (not an error) lists entities referenced by an importable entity that have no unique
  natural key.

**Rationale.** Uniqueness must be a database guarantee, not a C# check (write skew). One attribute
is the mechanical minimum. Inference makes the common entity work with zero lines. Refusing to
"mandate" a natural key keeps line-item-like entities legal while making the fallback visible.

**Rejected.** Mandating a unique-and-required natural key for every entity (breaks weak-entity-like
top-level entities); the brain dump's chain into non-unique columns (cannot identify rows);
class-level composite declarations (deferred).

**Confidence.** High.

### D8 — Column and sheet mapping: manifest, then header text, then labels in any tenant language, then technical paths; language-aware; user override

**Decision.** Mapping runs at inspect and import time and produces an `ImportPlan`:

1. **Sheets.** A sheet is matched to the parent entity or a child collection by the manifest's
   sheet table, else by its name against the entity/collection labels in every tenant language and
   English, else against the technical collection name (`RoleMemberships`). The first non-hidden
   sheet is the parent sheet when no manifest exists. Unmatched sheets are ignored with a warning.
   `request.SheetMappings` overrides.
2. **Columns.** Each header cell is matched, in order: (a) the manifest's column table by header
   text (exact) — this yields the path *and* the language of a multilingual column and the key of a
   reference column; (b) the header parsed as `<Label>` or `<Label> (<symbol>)` or
   `<Navigation label> / <Key label>[ (<symbol>)]`, with labels resolved in every tenant language
   and English and symbols resolved through the language symbol table; (c) the technical path
   (`Name2`, `Parent.Code`, `CenterId`). Multilingual columns map to the tenant slot of the column's
   *language*: a file column in Arabic maps to whichever of `Name`/`Name2`/`Name3` is Arabic in the
   target tenant; a language the target does not configure leaves the column unmapped. A header
   `Name` without a symbol maps to the primary slot. Two columns mapping to the same path is a
   `DuplicateColumn` error.
3. **Override.** `request.ColumnMappings` (by sheet + header text or column letter → path, or →
   null to ignore) wins over everything.
4. **Unmapped columns** are errors (`UnmappedColumn`, with up to three suggested paths by label
   similarity) unless `request.IgnoreUnmappedColumns`.
5. **Write-once and server-owned paths** may be mapped only where D9 allows.

The label catalogue and symbol table come from the localization theme and are cached per
(entity, tenant settings tag, resource version).

**Rationale.** The manifest makes unchanged files map perfectly with zero user input; the parsed
label chain covers renamed or hand-made files across tenants whose languages differ; the
technical path is the escape hatch for scripts and agents; the override is the human escape
hatch. Failing on unmapped columns prevents the classic silent "column ignored" data loss.

**Rejected.** Mapping by column position (users insert and reorder columns); mapping by header
only (no language, no key information); silent ignoring of unmapped columns.

**Confidence.** High.

### D9 — Import modes `Insert | Update | Upsert`, row identity, hydration, blank cells, children

**Decision.**

- `ImportMode.Insert`: every parent row is new. `Id` and `Stamp` columns are ignored (ids are
  allocated by the pipeline). Duplicates against the database surface through the pipeline's
  uniqueness pre-check and, ultimately, unique-index errors mapped to the row and column.
- `ImportMode.Update`: `RowKey` (a natural key path or `Id`) identifies each row; a row whose key
  resolves to no existing row is `RowNotFound` at (row, key column); every row must resolve.
- `ImportMode.Upsert`: found rows follow the Update path, others the Insert path.
- `RowKey` default: the manifest's row key if present in the sheet, else the entity's default
  natural key if present, else `Id` when the manifest's `SourceTenantId` equals the target tenant.
  `Id` is refused as a row key when the source tenant differs or is unknown
  (`IdKeyRequiresSameTenant`).
- **Hydration.** For Update/Upsert the resolution round returns the complete editable column set
  of every matched row (one flat Queryex query restricted by the key TVP); the codec overlays the
  mapped columns on the hydrated values, so a partial sheet touches only the columns it contains.
- **Blank cells** in a mapped column set the property to null; a non-nullable property then fails
  the pipeline's required-value validation at that cell. To leave a column untouched, remove it
  from the sheet (or map it to null).
- **Write-once properties** (as marked by the entity contract): mappable in Insert; in Update/
  Upsert a value that differs from the hydrated value is `WriteOnceChanged` at that cell; equal
  values are accepted (the natural key is often the write-once column).
- **Server-owned properties** (audit, tree bookkeeping, computed): never mappable
  (`ServerOwnedColumn`), including when a display-shape export is fed back in.
- **Concurrency.** When the sheet carries `Stamp` and the source tenant matches, each updated
  row's stamp is passed to the pipeline as its expected stamp; a mismatch is the pipeline's
  concurrency conflict (`ConcurrencyConflict` at the row) unless `request.OverrideConcurrency`.
  Without a stamp, no check is made (documented).
- **Children.** A child sheet present in the workbook means "these are the complete children of
  every parent row present in the parent sheet": each parent's collection is synchronized to its
  sheet rows (rows for a parent absent from the parent sheet are `OrphanChildRow` errors). A child
  sheet absent from the workbook leaves children untouched (the collection is passed as *absent*,
  the pipeline's "missing collection is not touched"). Child rows are matched to existing children
  by the child's `Id` column (same tenant), else by the child's declared natural key within the
  parent, else replaced (delete and insert). Existing children are loaded in the resolution round
  through the parent key path (one query per collection).
- **In-file uniqueness.** Before any DB call, duplicate row-key values within the sheet are
  `DuplicateRowKey` errors, and duplicates in any unique column are reported against both rows.

**Rationale.** These are the save contract's semantics mapped onto sheets, with the two ambiguities
(blank cells, absent child sheets) resolved in the direction that never silently drops data and is
easy to state in one sentence each.

**Rejected.** Blank means "keep" (a sheet then cannot clear a value; the sentinel alternative
`#NULL` is a footgun); child sheet with no rows for a parent meaning "keep" (the collection can
then never be emptied through Excel); Odoo-style "link to the first match" on ambiguity.

**Confidence.** High on modes; medium on children. **Review flag.** "Child sheet present ⇒
complete replacement" is the sharpest edge in this design; the alternative is a per-row
`Action` column (`Keep|Delete`) on child sheets.

### D10 — Bulk natural-to-surrogate resolution: Queryex queries with a TVP restriction and the target's read filter

**Decision.** The codec emits, per distinct *(target entity, key path)* pair across all sheets and
columns (the row key, every reference column, every child sheet's parent reference, tree parents
not found in-sheet), one `ExcelLookup` request; the pipeline compiles each as a Queryex query:

- `Root` = target entity, `Select` = `"<KeyPath>, Id"` (plus the editable columns when the lookup
  is also the hydration of the row key), `Filter` = the user's **read** filter on the target
  (from permission evaluation), and a **list restriction** `KeyPath in @qx{b}_l0` bound to a
  `StringList` / `IdList` / `BigIdList` / `GuidList` TVP by the key's type. All lookups ride one
  batch with the connect call.
- The compiled SQL is, illustratively:

```sql
SELECT [T].[Code], [T].[Id]
FROM   [gl].[Centers] AS [T]
WHERE  [T].[Code] IN (SELECT [Id] FROM @qx3_l0)          -- list restriction, StringList TVP
  AND  (<user's read predicate on Center, compiled from its FilterTree>)
```

- Matching back in C#: sheet values are trimmed and NFC-normalized; DB values are grouped with
  `StringComparer.OrdinalIgnoreCase` after the same normalization. Exactly one row ⇒ resolved;
  zero ⇒ `ReferenceNotFound` at (row, column) — which is also what an RLS-hidden row yields;
  more than one ⇒ `ReferenceAmbiguous` with the match count. A DB match that C# cannot attribute
  to any sheet value (a collation equivalence beyond case, such as accent- or width-insensitive
  collations) is reported as `ReferenceNotFound` with argument `collation = true` so the message
  can hint at it.
- Key values longer than 450 characters (the `StringList` column width) are `KeyTooLong` before
  any query.
- **In-sheet first.** Before emitting a lookup for a reference to the entity being imported (tree
  parents; any self-reference), the value is matched against the sheet's own rows by the same key
  (the sheet's *mapped* column for that key). A hit binds the reference to that row's entity — its
  allocated id for new rows, its hydrated id for existing ones — and no query is needed.
- Multilingual keys resolve through the tenant slot of the *file column's* language (D8); the
  restriction path is then `Name2` rather than `Name` when that is the slot.

**Rationale.** A Queryex query composes the read filter (fail closed), gates `Name2`/`Name3`, and
produces one plan per (entity, key) per tenant; the TVP restriction is the amendment the data-
access theme already plans for `GetByIds`. Distinct-value TVPs make the cost O(distinct values),
not O(rows × columns).

**Rejected.** Raw SQL with the TVP as the driving table and `COUNT(*)` grouping (cannot apply the
RLS `FilterTree`, which compiles only through `CompileQuery`); resolving without the target's read
filter (existence oracle); per-row lookups (N+1).

**Confidence.** High on the mechanism; medium on the C#-side equality rule under exotic
collations. **Review flag.** Applying the referenced entity's *read* filter to references is
stricter than a JSON save that posts a raw id; the pipeline theme should adopt the same rule for
FK validation so Excel and JSON agree.

### D11 — Tree import: parents by natural key, in-sheet first, topological order, cycle check in the pipeline

**Decision.** For a tree entity the `Parent / <Key>` column is an ordinary reference column
(D6, D10). Resolution order: in-sheet match, then DB lookup, then `ReferenceNotFound`. After
ids are allocated the codec sets `ParentId` on every row, orders rows so that in-sheet parents
precede their children (Kahn's algorithm over the in-sheet graph; a cycle among sheet rows is
`ParentCycle` reported at every row in the cycle before any DB call), and hands the ordered list to
the pipeline, whose tree capability validates cycles against the loaded ancestor chains and appends
the recompute statement. A single bulk `INSERT` from one TVP satisfies the self-referencing FK
regardless of row order (constraints are checked at statement end), so the ordering exists for
chunk boundaries (D14) and for readable errors, not for correctness of the insert.

**Rationale.** Consistency with every other reference; no id needed for new parents; cycles among
new rows can only be detected in memory and this is the only component that sees the whole sheet.

**Confidence.** High.

### D12 — Errors carry sheet, row and column; capped; synchronous import is all-or-nothing

**Decision.** Every codec error is an `ImportError(Sheet, Row, Column, Header, Path, Code,
Arguments)`; pipeline validation errors keyed by entity path are translated through the codec's
coordinate map (entity index ⇒ sheet row; property path ⇒ column when the column exists in the
sheet, else the row alone; child path ⇒ the child sheet's row). `ImportException` (a platform
validation exception) carries the list, capped at `ExcelOptions.MaxReportedErrors` (default 1,000)
with `TotalErrors`; the web theme maps it to 422 problem details whose `errors[]` items carry the
coordinates. Codes are stable constants in `ExcelErrorCodes`; messages are localized by the host.
A synchronous import is one transaction: any error anywhere rolls back everything. An "errors
workbook" (the upload echoed with an error column) is a non-goal for this release (D18).

**Rationale.** Coordinates are the only thing an Excel user can act on; capping keeps a 1M-row
failure from returning a 1M-item payload; all-or-nothing is the save contract and is what the
brain dump asks for at synchronous scale.

**Confidence.** High.

### D13 — File intake through blob staging; package limits; shared-string store

**Decision.** Import files never travel in a JSON body. The SPA (or an agent) uploads the file to
the blob theme's staging endpoint and receives a staging token; `InspectImportRequest.FileToken`
and `ImportRequest.FileToken` name it. The codec copies the staged blob to a local temp file
(`DeleteOnClose`) and reads it from there — twice for background imports (D14). Staged files that
are never imported are swept by the blob theme's orphan collection; a completed import confirms
the blob for retention of `ExcelOptions.ImportFileRetentionDays` (default 7) so the task record
can link to it.

Limits enforced before parsing: `MaxImportFileBytes` (compressed, default 256 MB;
`MaxSynchronousImportFileBytes` 16 MB), `MaxImportUncompressedBytes` (sum of part lengths from
the zip central directory, default 2 GB; parts are read through a counting stream that aborts
past the limit), at most one workbook, no external links, no macros required (a `.xlsm` is read
as a workbook; macros are ignored). Row counts for the synchronous decision come from each sheet's
`dimension` element, or from a forward count when it is absent.

Shared strings: streamed into `SharedStringStore` (in memory up to `MaxInMemorySharedStrings`,
default 1,000,000 entries; beyond that an offset index over a temp file).

**Rationale.** One intake path shared with images and attachments; a file on local disk can be
read twice; limits stop zip bombs and runaway memory; the SST is the only unbounded part of a
user-saved file.

**Rejected.** Multipart import endpoint (a second intake path; still needs staging for
background); reading the blob stream directly (not seekable twice; slow re-reads from Azure).

**Confidence.** High. **Review flag.** Staging-only adds one round trip for tiny synchronous
imports; a multipart convenience endpoint could be added later without changing the codec.

### D14 — Background hand-off: thresholds, chunked persistence, progress, results, inbox

**Decision.** A request with `Background = true` enqueues a task (through the background-task
theme's machinery, riding the same connect round trip) and returns `ExportOutcome.TaskId` /
`ImportOutcome.TaskId` with HTTP 202 semantics; a synchronous request whose file or row count
exceeds `MaxSynchronousImportRows` (default 10,000 rows summed over sheets) or
`MaxSynchronousImportFileBytes` fails with `ImportTooLargeForSynchronous` naming the limits, and
the SPA re-submits with `Background = true`. The task payload is `ExcelTaskPayload` (§3) — the
serialized request, the entity name, the staged file token, and the captured request context
(tenant, user, culture, calendar, time zone, sandbox flag). The handler runs under the requesting
user with permissions evaluated at run time (the scheduler theme's rule for user-triggered work).

Background export: same code as D3 with `MaxExportRows`; the result workbook is written to the
blob store under the task; the inbox item `ExcelExportReady` links to the etag-validated blob
download; result files are swept after `ExportFileRetentionDays` (default 7).

Background import runs in two passes over the local copy:

1. **Pass 1 (no DB):** mapping, type decoding of every cell, in-file uniqueness of row keys and
   unique columns, in-sheet parent graph and cycle check, row counts. Any error aborts before any
   commit.
2. **Pass 2:** rows are processed in chunks of `ImportChunkRows` (default 10,000 parent rows plus
   their child rows; tree rows are chunked in topological order so a parent's chunk never follows
   its child's). Each chunk runs resolution, validation and persist as its own transaction (three
   round trips). A validation or persist error in chunk *k* stops the task; chunks `< k` stay
   committed. The outcome reports `CommittedRowRanges` and the errors of chunk *k*.
   `ImportRequest.Atomic = true` instead runs the whole file as one transaction and is refused
   above `MaxAtomicImportRows` (default 100,000).

Progress: phases `Parsing | Resolving | Validating | Saving`, rows processed, rows total, chunk
index; the lease is renewed by the task runtime while the handler runs (the runtime's sliding
lease; the handler needs nothing). Completion writes the `ImportOutcome` (or the capped error list)
as a JSON blob under the task and raises `ExcelImportCompleted` / `ExcelImportFailed` inbox items.
Export-ready and import-completed notifications are not mutable by the user's notification
preferences (the file is reachable from the task page as well, so muting would not strand it —
but the default stays on).

**Rationale.** Every industrial bulk tool (Dataverse import jobs, F&O staging, Data Loader
batches) commits in bounded batches; a single 1M-row transaction escalates locks, bloats the log,
and holds a lease for an hour. Chunk-level atomicity with an explicit committed-range report is
honest; the `Atomic` switch keeps the strict option for mid-sized files.

**Rejected.** Auto-promotion to background (response shape changes); one transaction always;
validate-all-then-persist-all (three file passes and double the DB reads for a guarantee that
concurrent writers can still break).

**Confidence.** Medium. **Review flag.** Chunked commits change the brain dump's all-or-nothing
promise for large files; the alternative is `Atomic` by default with a hard cap and no chunking.

### D15 — Access control and security posture

**Decision.** `ExportAsync` and `ExportForImportAsync` require the entity's `read` action and apply
the user's read filter exactly as the query and details operations do; `InspectImportAsync` and
`ImportAsync` require `save` and run the pipeline's RLS pre-check (on hydrated existing rows) and
post-check. Reference resolution applies the target's read filter (D10). The manifest is
validated, never trusted: paths must exist and be importable, sheet and column indexes are
bounded, `SourceTenantId` only gates whether `Id`/`Stamp` are *considered*. Ids from a sheet are
never inserted; they select rows that the RLS pre-check verifies. No separate `export` permission
action exists in this release.

**Confidence.** High. **Review flag.** An `export` action on the securables registry would let
admins forbid bulk extraction to users who may read on screen; it is one line in the permission
theme's registry if wanted.

### D16 — Options, telemetry, naming

**Decision.** `ExcelOptions` (bound from configuration by the distribution host, defaults above)
and meter `Tellma.Core.Excel` with instruments named in `ExcelMeters` (§3): `tellma.excel.export.rows`
(histogram), `tellma.excel.export.duration` (histogram, ms), `tellma.excel.export.truncated`
(counter), `tellma.excel.import.rows` (histogram), `tellma.excel.import.duration`,
`tellma.excel.import.lookups` (histogram: resolution queries per import), `tellma.excel.import.errors`
(counter), `tellma.excel.import.chunks` (histogram), `tellma.excel.sst.spilled` (counter). Tags:
`entity` (logical name), `shape` (`display|editable`), `mode`, `background` (bool). No tenant tag.

Vocabulary: "editable shape" and "display shape"; `Upsert` not `Merge`; "row key" for the row-
identifying key; "reference key" for the natural key a foreign-key column is expressed in;
"manifest" for the hidden sheet; "stamp" for the concurrency token column.

**Confidence.** High.

### D17 — Testing

**Decision.** Pure tests (`Tellma.Core.Excel.Tests`): the planner over fixture entity metadata
(column sets, labels, formats, sheet names), encoder/decoder round trips per type including the
15-digit boundary, date systems (1900/1904, serial 60), manifest read/write, the mapping algorithm
across tenant-language permutations, in-sheet resolution and cycle detection, and a fixture corpus
of workbooks saved by Excel, LibreOffice and Google Sheets (SST present, `dimension` absent,
renamed headers, inserted columns, a 1904 workbook, a `veryHidden` sheet). Integration tests on
LocalDB (`Category=Integration`): export → import round trip of the fixture entities in all three
modes with children and a tree; resolution with RLS; chunked background import with a forced
failure in chunk 2 reporting committed ranges. The milestone plan includes one manual round trip
through desktop Excel on Windows for the Um Al Qura format tag.

### D18 — Non-goals for this release (explicit)

Multi-entity workbooks (the manifest's `Entities` table has one row; a later spec sequences
several entities per workbook the way F&O data packages do, reusing the per-entity pipeline in
dependency order — Excel stays the medium because the files must be human-editable; zipped JSON
or SQLite is rejected for that reason); CSV; reference dropdowns backed by lookup sheets; an
errors workbook; partial success within a synchronous import; composite natural keys; per-row
child `Action` columns; importing display-shape exports (they map, but server-owned and computed
columns fail as `ServerOwnedColumn`, by design).

---

## 3. Contracts

### 3.1 Owned by this theme — `Tellma.Core.Abstractions` (namespace `Tellma.Core.Abstractions.Excel`)

```csharp
/// <summary>Excludes an otherwise editable property from every Excel shape.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class ExcludeFromExcelAttribute : Attribute;

/// <summary>How imported parent rows relate to existing rows.</summary>
public enum ImportMode
{
    /// <summary>Every row is new; ids and stamps in the file are ignored.</summary>
    Insert,
    /// <summary>Every row must match an existing row through the row key.</summary>
    Update,
    /// <summary>Rows that match are updated; the rest are inserted.</summary>
    Upsert,
}

/// <summary>The query half shared by both export requests: either <see cref="Ids"/> or the
///     clauses, never both. Mirrors the query request of the service pipeline.</summary>
public abstract record ExportSourceRequest
{
    /// <summary>Explicit ids; rows are returned in this order. Empty is valid.</summary>
    public IReadOnlyList<int>? Ids { get; init; }
    /// <summary>Row-level filter (the pipeline's filter shape).</summary>
    public string? Filter { get; init; }
    /// <summary>Ordering list.</summary>
    public string? OrderBy { get; init; }
    /// <summary>Free-text search, translated by the service as for a query.</summary>
    public string? Search { get; init; }
    /// <summary>Values for parameters used in the clauses.</summary>
    public IReadOnlyDictionary<string, object?> Arguments { get; init; } = new Dictionary<string, object?>();
    /// <summary>True to enqueue a background task instead of answering synchronously.</summary>
    public bool Background { get; init; }
}

/// <summary>Display-shape export: the grid as a sheet.</summary>
public sealed record ExportRequest : ExportSourceRequest
{
    /// <summary>The select list exactly as the grid uses it.</summary>
    public required string Select { get; init; }
    /// <summary>Optional header labels parallel to the select items; derived when null.</summary>
    public IReadOnlyList<string>? Headers { get; init; }
}

/// <summary>Editable-shape export: entities with children, references by natural key.</summary>
public sealed record ExportForImportRequest : ExportSourceRequest
{
    /// <summary>Subset of property paths to include; all editable properties when null.</summary>
    public IReadOnlyList<string>? Columns { get; init; }
    /// <summary>Whether child collections get their own sheets. Default true.</summary>
    public bool IncludeChildren { get; init; } = true;
    /// <summary>Per foreign-key property, the target property to express it in
    ///     (e.g. <c>{"CenterId": "Code"}</c>); the target's default natural key otherwise.</summary>
    public IReadOnlyDictionary<string, string> ReferenceKeys { get; init; } = new Dictionary<string, string>();
}

/// <summary>What an export produced: a file (synchronous) or a task (background).</summary>
public sealed record ExportOutcome
{
    /// <summary>The workbook, when synchronous. The caller owns and disposes it.</summary>
    public Stream? Workbook { get; init; }
    /// <summary>Suggested file name (ASCII-safe; the web layer adds the RFC 5987 form).</summary>
    public string? FileName { get; init; }
    /// <summary>Rows written to the parent sheet, when synchronous.</summary>
    public int? Rows { get; init; }
    /// <summary>The background task id, when <see cref="ExportSourceRequest.Background"/>.</summary>
    public long? TaskId { get; init; }
}

/// <summary>A caller-supplied column mapping override.</summary>
/// <param name="Sheet">Sheet name as it appears in the workbook.</param>
/// <param name="Column">Header text, or a column letter such as "C".</param>
/// <param name="Path">Target property path; null to ignore the column.</param>
public sealed record ImportColumnMapping(string Sheet, string Column, string? Path);

/// <summary>A caller-supplied sheet mapping override.</summary>
/// <param name="Sheet">Sheet name as it appears in the workbook.</param>
/// <param name="Collection">Child collection path, "" for the parent sheet, null to ignore.</summary>
public sealed record ImportSheetMapping(string Sheet, string? Collection);

/// <summary>Reads a staged workbook's manifest and headers and proposes a mapping. No DB.</summary>
public sealed record InspectImportRequest
{
    /// <summary>The staging token of the uploaded file.</summary>
    public required string FileToken { get; init; }
    public IReadOnlyList<ImportSheetMapping> SheetMappings { get; init; } = [];
    public IReadOnlyList<ImportColumnMapping> ColumnMappings { get; init; } = [];
}

/// <summary>Runs an import.</summary>
public sealed record ImportRequest
{
    /// <summary>The staging token of the uploaded file.</summary>
    public required string FileToken { get; init; }
    public ImportMode Mode { get; init; } = ImportMode.Insert;
    /// <summary>Row-identifying key: a natural-key path or "Id". Required for Update and Upsert
    ///     unless the manifest supplies one.</summary>
    public string? RowKey { get; init; }
    public IReadOnlyList<ImportSheetMapping> SheetMappings { get; init; } = [];
    public IReadOnlyList<ImportColumnMapping> ColumnMappings { get; init; } = [];
    /// <summary>Whether unmapped columns are ignored instead of rejected. Default false.</summary>
    public bool IgnoreUnmappedColumns { get; init; }
    /// <summary>Whether stamp mismatches are overridden. Default false.</summary>
    public bool OverrideConcurrency { get; init; }
    public bool Background { get; init; }
    /// <summary>Background only: one transaction for the whole file (refused above the atomic
    ///     row cap) instead of per-chunk transactions.</summary>
    public bool Atomic { get; init; }
}

/// <summary>The proposed mapping and facts about a staged workbook.</summary>
public sealed record ImportPlan(
    string? Entity,
    int ManifestVersion,
    bool ManifestPresent,
    bool SameTenant,
    bool SchemaFingerprintMatches,
    IReadOnlyList<ImportPlanSheet> Sheets,
    IReadOnlyList<string> RowKeyCandidates,
    string? DefaultRowKey,
    long TotalRows,
    bool RequiresBackground,
    IReadOnlyList<ImportError> Warnings);

/// <summary>One sheet of the plan.</summary>
public sealed record ImportPlanSheet(
    string Sheet,
    string? Collection,
    ImportSheetStatus Status,
    long Rows,
    IReadOnlyList<ImportPlanColumn> Columns);

/// <summary>One column of a sheet in the plan.</summary>
/// <param name="Suggestions">Up to three candidate paths when unmapped.</param>
public sealed record ImportPlanColumn(
    string Column,
    string Header,
    string? Path,
    ImportColumnStatus Status,
    string? Language,
    string? ReferenceKey,
    IReadOnlyList<string> Suggestions);

public enum ImportSheetStatus { Parent, Child, Manifest, Ignored }
public enum ImportColumnStatus { Mapped, Unmapped, Ignored, Duplicate, ServerOwned }

/// <summary>One import problem located in the workbook. <paramref name="Row"/> is 1-based as
///     Excel numbers it; <paramref name="Column"/> is the letter; either may be null when the
///     problem is not cell-specific.</summary>
public sealed record ImportError(
    string? Sheet,
    int? Row,
    string? Column,
    string? Header,
    string? Path,
    string Code,
    IReadOnlyList<KeyValuePair<string, string>> Arguments);

/// <summary>What an import did.</summary>
public sealed record ImportOutcome
{
    public int Inserted { get; init; }
    public int Updated { get; init; }
    public int ChildrenInserted { get; init; }
    public int ChildrenUpdated { get; init; }
    public int ChildrenDeleted { get; init; }
    /// <summary>Background, chunked: the committed parent-row ranges when the task stopped early.</summary>
    public IReadOnlyList<(int From, int To)> CommittedRowRanges { get; init; } = [];
    public IReadOnlyList<ImportError> Warnings { get; init; } = [];
    public long? TaskId { get; init; }
}

/// <summary>A validation failure of an import, mapped by the web layer to 422 with per-cell errors.</summary>
public sealed class ImportException : ValidationException   // the pipeline's platform exception
{
    public IReadOnlyList<ImportError> Errors { get; }
    /// <summary>Total errors found, which may exceed the reported list.</summary>
    public int TotalErrors { get; }
}

/// <summary>Stable error codes; hosts localize.</summary>
public static class ExcelErrorCodes
{
    public const string ExportRowLimitExceeded = "excel.export.rowLimitExceeded";
    public const string ImportTooLargeForSynchronous = "excel.import.tooLargeForSynchronous";
    public const string UnmappedColumn = "excel.import.unmappedColumn";
    public const string DuplicateColumn = "excel.import.duplicateColumn";
    public const string ServerOwnedColumn = "excel.import.serverOwnedColumn";
    public const string WriteOnceChanged = "excel.import.writeOnceChanged";
    public const string RowNotFound = "excel.import.rowNotFound";
    public const string DuplicateRowKey = "excel.import.duplicateRowKey";
    public const string IdKeyRequiresSameTenant = "excel.import.idKeyRequiresSameTenant";
    public const string SurrogateReferenceFromOtherTenant = "excel.import.surrogateReferenceFromOtherTenant";
    public const string ReferenceNotFound = "excel.import.referenceNotFound";
    public const string ReferenceAmbiguous = "excel.import.referenceAmbiguous";
    public const string KeyTooLong = "excel.import.keyTooLong";
    public const string OrphanChildRow = "excel.import.orphanChildRow";
    public const string ParentCycle = "excel.import.parentCycle";
    public const string InvalidCell = "excel.import.invalidCell";          // type decode failure
    public const string ConcurrencyConflict = "excel.import.concurrencyConflict";
    public const string PackageTooLarge = "excel.import.packageTooLarge";
    public const string MalformedWorkbook = "excel.import.malformedWorkbook";
}

/// <summary>Distribution-configurable limits and defaults.</summary>
public sealed record ExcelOptions
{
    public int MaxSynchronousExportRows { get; init; } = 100_000;
    public int MaxExportRows { get; init; } = 1_048_575;
    public int MaxSynchronousImportRows { get; init; } = 10_000;
    public int MaxAtomicImportRows { get; init; } = 100_000;
    public int ImportChunkRows { get; init; } = 10_000;
    public long MaxSynchronousImportFileBytes { get; init; } = 16L << 20;
    public long MaxImportFileBytes { get; init; } = 256L << 20;
    public long MaxImportUncompressedBytes { get; init; } = 2L << 30;
    public int MaxInMemorySharedStrings { get; init; } = 1_000_000;
    public int MaxReportedErrors { get; init; } = 1_000;
    public int ExportFileRetentionDays { get; init; } = 7;
    public int ImportFileRetentionDays { get; init; } = 7;
}

/// <summary>Instrument names of the <c>Tellma.Core.Excel</c> meter.</summary>
public static class ExcelMeters
{
    public const string MeterName = "Tellma.Core.Excel";
    public const string ExportRows = "tellma.excel.export.rows";
    public const string ExportDuration = "tellma.excel.export.duration";
    public const string ExportTruncated = "tellma.excel.export.truncated";
    public const string ImportRows = "tellma.excel.import.rows";
    public const string ImportDuration = "tellma.excel.import.duration";
    public const string ImportLookups = "tellma.excel.import.lookups";
    public const string ImportErrors = "tellma.excel.import.errors";
    public const string ImportChunks = "tellma.excel.import.chunks";
    public const string SharedStringsSpilled = "tellma.excel.sst.spilled";
}
```

### 3.2 Owned by this theme — the codec's internal seam (`Tellma.Core.Excel`, consumed by the pipeline in Core)

The codec is pure: it never opens a connection. The pipeline runs the queries the codec asks for.

```csharp
/// <summary>A query the codec needs the pipeline to run: flat rows over one root entity,
///     optionally restricted to a list of key values through a TVP. The pipeline adds the
///     user's read filter, compiles it with Queryex, and streams rows back.</summary>
public sealed record ExcelQuery(
    string RootEntity,
    IReadOnlyList<string> SelectPaths,
    string? OrderBy,
    ExcelListRestriction? Restriction,
    int? Take);

/// <summary>"<paramref name="Path"/> in (values)" bound as a TVP of the path's type.</summary>
public sealed record ExcelListRestriction(string Path, IReadOnlyList<object> Values);

/// <summary>Streams rows for an <see cref="ExcelQuery"/>; implemented by the pipeline.</summary>
public delegate IAsyncEnumerable<object?[]> ExcelRowSource(ExcelQuery query, CancellationToken ct);

/// <summary>Plans and writes workbooks for both shapes.</summary>
public interface IExcelExporter
{
    /// <summary>Builds the display-shape plan from compiled query columns and the request context.</summary>
    ExcelWorkbookPlan PlanDisplay(EntityMetadata entity, IReadOnlyList<ExcelColumnSpec> columns, ExcelContext context);

    /// <summary>Builds the editable-shape plan: sheets, columns, reference keys, manifest.</summary>
    ExcelWorkbookPlan PlanEditable(EntityMetadata entity, ExportForImportRequest request, ExcelContext context);

    /// <summary>Writes the workbook, pulling rows per sheet from <paramref name="rows"/> in plan
    ///     order. Throws <see cref="ExportRowLimitExceededException"/> past the cap.</summary>
    Task<int> WriteAsync(ExcelWorkbookPlan plan, ExcelRowSource rows, Stream destination, CancellationToken ct);
}

/// <summary>A display-shape column as the pipeline describes it from a compiled query.</summary>
public sealed record ExcelColumnSpec(string Header, ExcelValueKind Kind, int? Scale, IReadOnlyList<string>? Path);

/// <summary>Parses, maps and materializes workbooks into entities in the save shape.</summary>
public interface IExcelImporter
{
    /// <summary>Reads manifest, headers and dimensions; proposes the mapping. No DB.</summary>
    Task<ImportPlan> InspectAsync(Stream workbook, EntityMetadata entity, InspectImportRequest request, ExcelContext context, CancellationToken ct);

    /// <summary>Pass 1: decodes every cell, checks in-file constraints, and returns the lookups
    ///     the pipeline must run (one per distinct target/key pair).</summary>
    Task<ExcelImportSession<TEntity>> ParseAsync<TEntity>(Stream workbook, EntityMetadata entity, ImportRequest request, ExcelContext context, CancellationToken ct)
        where TEntity : class;
}

/// <summary>A parsed workbook awaiting resolution. Chunkable for background imports.</summary>
public sealed class ExcelImportSession<TEntity> where TEntity : class
{
    /// <summary>The lookups to run, deduplicated by (entity, key path).</summary>
    public IReadOnlyList<ExcelQuery> Lookups { get; }
    /// <summary>Parent rows in topological order; the chunk boundaries the runner may use.</summary>
    public int RowCount { get; }
    /// <summary>Binds lookup results (rows of <c>[key, id, editable columns…]</c>) and produces the
    ///     entities of one chunk with children attached, ids of existing rows set, and expected
    ///     stamps; the coordinate map translates pipeline errors back to cells.</summary>
    ExcelChunk<TEntity> Resolve(int fromRow, int toRow, IReadOnlyDictionary<ExcelQuery, IReadOnlyList<object?[]>> lookupRows, Func<int, IReadOnlyList<int>> allocateIds);
}

/// <summary>Entities of one chunk plus what the pipeline needs to save and report on them.</summary>
public sealed record ExcelChunk<TEntity>(
    IReadOnlyList<TEntity> Entities,
    IReadOnlyList<(int Id, string ExpectedStamp)> ExpectedStamps,
    IReadOnlyDictionary<string, ExcelCell> CoordinateMap,   // entity path → cell
    IReadOnlyList<ImportError> Errors);

/// <summary>Request-context facts the codec needs: languages, culture, calendar, time zone,
///     tenant id, label catalogue, language symbols, calendar-aware formatter.</summary>
public sealed record ExcelContext(
    int TenantId,
    IReadOnlyList<string> TenantLanguages,
    CultureInfo Culture,
    string Calendar,
    TimeZoneInfo TimeZone,
    ILabelCatalog Labels,
    ICalendarFormatter Formatter);
```

### 3.3 Needed from other themes

**Data access (entity contract, Queryex host integration, batch).**

```csharp
/// <summary>Per-entity metadata derived once from the class and the EF model; the codec's
///     only view of an entity. Names are the data-access theme's to finalize.</summary>
public sealed record EntityMetadata(
    string Name,                                   // logical (Queryex) name, e.g. "Center"
    Type ClrType,
    PropertyMetadata Key,
    IReadOnlyList<PropertyMetadata> Properties,
    IReadOnlyList<ReferenceMetadata> References,   // (NavigationName, ForeignKeyProperty, TargetEntity)
    IReadOnlyList<ChildCollectionMetadata> Children,   // (CollectionProperty, ChildEntity, ParentKeyProperty)
    IReadOnlyList<NaturalKeyMetadata> NaturalKeys, // ordered; (PropertyPath, IsDeclared, IsRequired)
    IReadOnlyList<MultilingualGroup> MultilingualGroups,   // ("Name", [Name, Name2, Name3])
    string? TreeParentProperty,
    string? ConcurrencyProperty,
    string SchemaFingerprint);                     // hash of importable paths and types

public sealed record PropertyMetadata(
    string Name, Type ClrType, bool IsNullable, bool IsEditable, bool IsWriteOnce, bool IsServerOwned,
    bool IsUnique, int? MaxLength, int? Precision, int? Scale, IReadOnlyList<string>? EnumValues,
    bool ExcludedFromExcel);
```

- `QuerySpec` amendment: `IReadOnlyList<ListRestriction> Restrictions` where
  `ListRestriction(string Path, string ParameterName)` compiles to `[alias].[col] IN (SELECT [Id]
  FROM @name)` and the host binds `IdList`/`BigIdList`/`GuidList`/`StringList` by the path's type.
  The path may traverse navigations (`User.Email`).
- The flat-row materializer (`object?[]` + `QueryexColumn.Path` → `TEntity`) for hydration.
- The id allocator callback `allocateIds(count)` usable before persist (ids assigned in memory).
- The batch builder accepting several compiled queries in one round trip with `NextResult()`
  readers.

**Service pipeline.** `SaveBulkAsync<TEntity>(IReadOnlyList<TEntity> entities, SaveOptions)` with
`SaveOptions { ReturnEntities = false, ExpectedStamps, OverrideConcurrency, Origin = SaveOrigin.Import,
AbsentChildCollections }`; the validation error type with entity-path keys; the editable/write-
once/server-owned markers; the uniqueness pre-check; the tree cycle validation over in-payload
graphs; the collapse of connect into the first business round trip.

**Localization.** `ILabelCatalog { string Entity(string entity, bool plural); string Property(string
entity, string path); string EnumValue(string entity, string path, string value); string
LanguageSymbol(string language); }` resolving in a given language with the fallback chain; the
tenant settings tag for the label cache key; `ICalendarFormatter { string FormatDate(DateOnly d,
string calendar, CultureInfo c, string pattern); bool TryParseDate(string s, string calendar,
CultureInfo c, string pattern, out DateOnly d); }` covering Gregorian, Um Al Qura, Hijri, Ethiopian.

**Permissions.** `IPermissionEvaluator.EvaluateAsync(resource, action)` → allowed + `FilterTree`,
used by the pipeline for `read` on the target of every lookup and for `save` on the imported entity.

**Blobs.** `IBlobStaging { Task<StagedBlob> OpenAsync(string token); Task<string> StageAsync(Stream,
string contentType); Task ConfirmAsync(string token, TimeSpan retention); }` where `StagedBlob`
exposes a readable stream, `Length`, `ContentType`.

**Background tasks.** `IBackgroundTaskHandler<ExcelTaskPayload>` with
`ExecuteAsync(BackgroundTaskContext context, ExcelTaskPayload payload, IProgress<TaskProgress>
progress, CancellationToken ct)`; `BackgroundTaskContext` restoring the request context captured at
enqueue; `IBackgroundTasks.EnqueueAsync(payload, batch)` riding the connect round trip; result
blob attachment; inbox item kinds `ExcelExportReady`, `ExcelImportCompleted`, `ExcelImportFailed`
with a per-kind click action (download / open task).

**Web.** POST endpoints projected from the four methods; `ExportOutcome.Workbook` streamed as
`application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` with `Content-Disposition`
(`filename` ASCII plus `filename*` UTF-8); `ImportException` → 422 problem details with
`errors[]` items `{sheet,row,column,header,path,code,message}`; background outcomes → 202 with
the task id in the JSON body.

---

## 4. Schema

This theme owns **no database tables**. Its persistent shape is the workbook.

### 4.1 Hidden manifest sheet `_tellma` (editable shape; also written by display export with `Shape = Display`)

Column A = key, columns B… = values. Rows 1–13 are the header block; the columns table follows
after one blank row.

```
Row  A                   B…
1    Tellma.Manifest     1                       -- manifest format version (int)
2    Shape               Editable | Display
3    Entity              Center                  -- logical entity name
4    Distribution        <distribution slug>
5    Platform            <Tellma.Core version>
6    SourceTenantId      <int>
7    ExportedAt          <ISO 8601 UTC>
8    Culture             ar-SA
9    Calendar            UmAlQura
10   TimeZone            Asia/Riyadh
11   Languages           en, ar                  -- tenant slots 1..3 in order
12   SchemaFingerprint   <hash>
13   Truncated           false                   -- true when the cap cut the export
14   RowKey              Code                    -- default row key for Update/Upsert (editable shape)
15   (blank)
16   Sheet | Collection | Column | Header | Path | Kind | Role | Language | ReferenceEntity | ReferenceKey | KeyKind | DateEncoding | DatePattern
17…  Centers |        | A | Id            | Id        | Int32   | Id        |    |        |      |         |        |
     Centers |        | B | Stamp         | Stamp     | String  | Stamp     |    |        |      |         |        |
     Centers |        | C | Code          | Code      | String  | Property  |    |        |      |         |        |
     Centers |        | D | Name (E)      | Name      | String  | Property  | en |        |      |         |        |
     Centers |        | E | Name (ع)      | Name2     | String  | Property  | ar |        |      |         |        |
     Centers |        | F | Parent / Code | ParentId  | String  | Reference |    | Center | Code | Natural |        |
     Centers |        | G | Center Type   | CenterType| String  | Property  |    |        |      |         |        |
     Centers |        | H | Opened On     | OpenedOn  | Date    | Property  |    |        |      |         | Serial | dd/mm/yyyy
     Users · Role Memberships | RoleMemberships | A | User / Email | (parent) | String | ParentReference | | User | Email | Natural | |
```

`Role` ∈ `Id | Stamp | Property | Reference | ParentReference`. `KeyKind` ∈ `Natural | Surrogate`.
`DateEncoding` ∈ `Serial | Text`. The sheet is protected without a password; it is hidden, not
`veryHidden`, so a curious user can inspect it.

### 4.2 Data sheets

```
<Entity plural>                         -- parent sheet, row 1 = headers (bold, frozen, autofilter)
  A  Id            number (int) / text (long)
  B  Stamp         text, hidden column
  C… editable properties, reference columns, multilingual columns (order in D6)

<Entity plural> · <Collection label>    -- one per child collection
  A… parent reference column(s)   "User / Email"
  then Id, then child properties as above
```

Validation lists: booleans `TRUE,FALSE`; enums as their stored values when the comma-joined list
is ≤ 255 characters; text columns `textLength` ≤ `MaxLength`. Number formats per D5. Column formats
`@` on text-typed columns.

### 4.3 Background-task payload (columns requested on the task theme's table, not owned here)

```
Kind            nvarchar(64)    'ExcelExport' | 'ExcelImport'
Entity          nvarchar(128)   logical entity name
Request         nvarchar(max)   serialized ExportRequest / ExportForImportRequest / ImportRequest
FileToken       nvarchar(128)   staged input file (import)
ResultBlobId    (blob theme's id type) null   the workbook (export) or the outcome JSON (import)
RowsTotal       int null
RowsProcessed   int null
Phase           nvarchar(32)    Parsing | Resolving | Validating | Saving | Uploading
ChunkIndex      int null
```

---

## 5. Answers

| Brain-dump question (abridged) | Answer |
|---|---|
| "What is the best way to declare the natural key of each entity? A Core attribute?" | `[NaturalKey(Order)]` on a property, unique-index-backed, validated at startup (D7). |
| "Should we mandate that every entity has a natural key that is required and unique? … fall back to surrogate keys?" | No mandate. Inference over unique columns; targets without one are referenced by surrogate `Id`, flagged in the manifest, refused across tenants; a startup warning lists them (D7). |
| "Does Excel understand the same numeric/date formatting primitives as Tellma? Does it understand calendars?" | Numbers and dates yes via number-format codes with `[$-CCLLLL]` tags; Hijri and Um Al Qura yes; Ethiopian no — text-date fallback with the calendar and pattern in the manifest (D5). |
| "Export and import of multiple entities … what machinery … Excel still the right medium?" | Non-goal now; the manifest reserves an `Entities` table; Excel remains the medium; zipped JSON/SQLite rejected as not human-editable (D18). |
| "same columns, same rows but without the paging (up to a limit)" | The grid query with `Take = cap + 1`; 100,000 synchronous, 1,048,575 background; over the cap fails with a named error, never truncates (D3). |
| "ExportByIds accepts a list of Ids and a Queryex select" | `ExportRequest.Ids` + `Select`; rows in id order; ids via the TVP restriction (D2, D3). |
| "every FK represented by a natural key … system should make a good guess" | Reference columns `<Nav> / <Key>` using the target's default natural key; per-FK override on export and import (D6, D7). |
| "values are exported raw … localization is metadata on the columns, ignored on import" | Encoding by property type with text fallbacks (D4); formats never consulted on import (D5). |
| "FKs … translated in bulk into surrogate keys … not found or ambiguity is a validation error" | One Queryex lookup per (target, key) with a TVP of distinct values and the target's read filter; `ReferenceNotFound` / `ReferenceAmbiguous` per cell (D10). |
| "Import supports tree entities using the surrogate key to reference a row's parents … even if the parent is new in the same import" | Parents by natural key, resolved in-sheet first; topological order; cycle detection (D11). |
| "Columns are mapped by default using the sheet headers, multi-lingual columns mapped intelligently … can be overridden … every service supplies the default mapping" | Manifest → header parse → labels in every tenant language → technical path; language slots resolved per column language; `ColumnMappings` override; no per-service mapping code (D8). |
| "Insert / Update / Merge … Update and Merge require designating one column as the natural key … partial sheets hydrate" | `Insert | Update | Upsert`; `RowKey` natural key or same-tenant `Id`; hydration in the resolution round; blank = null; child sheet present = full replacement (D9). |
| "Import is subject to the same access control checks as Save. And unlike save it does not read and return the entities" | Same `save` permission and RLS checks; returns counts, warnings, committed ranges, or a task id (D15, D12, D14). |
| Queryex engine: "restrict results where a column is IN a list … passed as a TVP" | Consumed as `ListRestriction` on `QuerySpec`; the codec's lookups and `Ids` exports depend on it (D10, §3.3). |
| Background: "Importing or Exporting enormous Excel files" | Thresholds, staged file, two-pass chunked import, progress phases, result blobs, inbox items (D13, D14). |

---

## 6. Seams

1. **Batch abstraction (data access).** The codec never touches it directly. The pipeline runs
   the codec's `ExcelQuery` lookups as compiled Queryex queries in one round trip, with the connect
   call and the id reservation riding along, and streams `object?[]` rows back. Needed: several
   compiled queries per batch, TVP parameters bound per statement, `NextResult()` readers.
2. **Entity class vs wire shape.** Import produces entity instances in the save shape — child
   collections attached as the pipeline expects (the `[NotMapped]` collections if that design
   wins), an *absent* collection distinguishable from an empty one, and server-owned properties
   left at default for the pipeline to overwrite. The codec needs the editable / write-once /
   server-owned markers on `PropertyMetadata`.
3. **One capability, declared once.** Excel declares nothing; it projects from *queryable* and
   *savable + keyed*, and reads the tree capability's parent property. The web theme projects the
   four methods to routes and the securables registry gets no new action (review flag in D15).
4. **Queryex schema per tenant configuration.** Lookups and exports use the tenant's schema (with
   `Name2`/`Name3` gated); a column in a language the tenant lacks is unmappable by construction.
   The label cache is keyed on the tenant settings tag.
5. **Version tags.** No new tag. Import bumps whatever the save emitter bumps.
6. **Feature composition.** `Tellma.Core.Excel` registers `IExcelExporter`, `IExcelImporter`,
   `ExcelOptions`, the meter, and the background handler as a Core feature that the stack feature
   `Requires`.
7. **Natural keys.** Contract in D7: `[NaturalKey]`, unique-index backing validated at startup,
   the inference order, `EntityMetadata.NaturalKeys`, child keys scoped by parent.
8. **Background-task columns and leases.** The payload of §4.3; the handler relies on the runtime
   to renew the lease and to restore the captured request context; the sweep of result files
   after the retention period is a scheduled task the codec registers.
9. **Request context.** `ExcelContext` is built from it: tenant id, tenant languages, culture,
   calendar (carried separately from the culture, never as a `-u-ca-` extension), time zone,
   sandbox flag (exports from a sandbox tenant carry `SourceTenantId` of the sandbox, so an `Id`
   round trip into the live tenant is refused).
10. **Platform exceptions.** `ImportException : ValidationException` (422) and
    `ExportRowLimitExceededException : ValidationException` (422 with the cap); package limits map
    to the payload-too-large family (413).
11. **Permission evaluation.** `read` on the entity for exports; `save` for import; `read` on each
    lookup target inside resolution.
12. **Blob staging tokens.** Import input; export results; retention confirmations.
13. **Wire shapes.** Export takes the query request's clauses verbatim; import errors extend the
    validation error item with `sheet`, `row`, `column`, `header`.
14. **Telemetry.** D16; no tenant tags.
15. **Notification enqueue.** The task-completion inbox items are raised by the task runtime, not
    by the codec.
16. **Connect-call collapse.** Assumed: the first business round trip is the export query or the
    resolution batch. Failure mode specific to Excel: a background task must re-run connect at
    start (the user may have been deactivated since enqueue) — the task runtime's rule.
17. **Vocabulary.** `Upsert`; "editable shape" / "display shape"; "row key"; "reference key";
    "manifest"; "stamp" (whatever the concurrency token is finally called, the column header
    follows it).

---

## 7. Departures from ARCHITECTURE.md

None within the ranges read (Guiding Principles). Display-shape export is the "within-stack
exports" tier of the reports section as the research file summarizes it; nothing here changes the
tiers. One pointer will need adding when the spec lands: the natural-key attribute lives in the
entity contract package rather than in an Excel package.

---

## 8. Verification

Relied on from `research/excel.md` (verified 2026-09-01 there): library versions, licenses and
dependency graphs (EPPlus Polyform/commercial and license-key requirement; NPOI maintenance-fee
EULA and SkiaSharp; ClosedXML's SixLabors.Fonts split license and issue #2734; MiniExcel's
feature gaps and 2.0 preview status; Open XML SDK 3.5.1 MIT with `OpenXmlWriter`/`OpenXmlReader`);
streaming benchmarks and the peak-memory numbers; Excel limits (1,048,576 rows, 31-character sheet
names and forbidden characters, 15 significant digits, 255-character typed validation lists,
`textLength` validation, format strings < 255 chars); number-format grammar and the `[$-NNCCLLLL]`
tag (triangulated, not on one Microsoft page); Windows calendar ids (Hijri 6, Um Al Qura 23 = 0x17;
no Ethiopian); date systems (`date1904`, the 1900 leap-year bug, 1899-12-30 epoch); shared-string
behaviour (Excel always writes an SST; inline strings valid); hidden sheets, defined names, custom
XML parts as carriers; `sheetProtection` semantics; Odoo, Dataverse, F&O and Data Loader
conventions (`Field/External ID`, hidden id + checksum + modified-on, header/lines staging, error
rows files, batch sizes).

Verified in this pass from the repository: `StringList` is a `[TableType]` class with a single
`[Key] [MaxLength(450)] string Id` column (hence the 450-character key cap and the `SELECT [Id]
FROM @tvp` shape); `IdList`/`BigIdList` carry `int Id`/`long Id`.

Still unverified (each has a mitigation in the design): that Google Sheets and LibreOffice
preserve a *hidden* sheet and its protection on round trip (mitigation: mapping degrades to
header parsing when the manifest is absent); that desktop Excel renders `[$-170401]` as Um Al
Qura in every locale build (mitigation: manual test in the milestone plan; the serial is correct
regardless); whether Open XML SDK 3.5.1 can write a package to a non-seekable stream (moot: the
codec spools to a temp file); the exact `CultureInfo.LCID` for every culture the platform ships
(custom cultures report 0x1000 and get no LCID tag); Excel's rendering of boolean cells in
non-English UI languages (display only); that `dimension` is written by Google Sheets (mitigation:
forward count fallback).

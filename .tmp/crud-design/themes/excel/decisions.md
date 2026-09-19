# Theme T9 — Excel codec: export and import (future spec 0018) — settled design

This is the complete settled design for the Excel codec: every name, shape, statement and
column a spec author needs. It is written for a reader who has seen none of the other design
files. Contract blocks use the platform's contract notation: names are normative; shape is
described, not transcribed.

---

## 1. Critique of the brain dump

The four export sections and the import section describe the right product — the grid as a
sheet, an editable sheet that round-trips, natural keys instead of ids, bulk translation, tree
import, language-aware mapping, insert/update/merge with hydration. The following points are
wrong, underspecified, or internally inconsistent, and the decisions below correct each one.

1. **"The values are exported raw (numbers, booleans)" is not achievable for every type.** Excel
   keeps 15 significant digits: a `long` id, a `decimal(19,4)` amount above
   99,999,999,999.9999 and any `Guid` are corrupted or impossible as numbers. Dates are not raw
   either — they are serials relative to a date system the workbook chooses. A per-type encoding
   table with text-cell fallbacks is required (D4).
2. **"Localization metadata is ignored during import" is right but too weak.** Import must never
   consult a cell's number format at all — not even to decide date versus number. The mapped
   property's type decides; the format is display only (D5). Stated that strongly it removes an
   entire class of locale bugs.
3. **The natural-key inference chain ends in keys that identify nothing.** "…otherwise the first
   text column, otherwise the first column" yields non-unique columns; a reference resolved
   through them is ambiguous by construction and a row identified through them cannot be
   updated. The chain stops at unique candidates and falls back to the surrogate id explicitly and
   visibly (D7).
4. **Tree import "using the surrogate key to reference a row's parents" contradicts the rule
   that every foreign key is a natural key, and cannot work for a parent that is new in the same
   sheet** (no id exists yet). Parents are references like any other, resolved in-sheet first (D11).
5. **Same-tenant round trips are the dominant use case and the brain dump does not serve them.**
   Export, fix 500 rows, import: when the natural key itself is being corrected (codes renamed,
   emails fixed) a natural-key row identity cannot express the edit, and nothing detects that a
   row changed between export and import. The editable sheet carries the surrogate id and the
   concurrency stamp, usable only when the file came from the same tenant of the same
   distribution (D6, D9).
6. **Update semantics for children are undefined.** The save contract (empty collection deletes,
   missing collection is untouched) has to be mapped onto sheets explicitly (D9).
7. **Blank-cell semantics are undefined.** A blank in a mapped column is "set to null" or "leave
   alone"; the choice changes what a partial sheet means (D9: null).
8. **No manifest.** Nothing records which language each `Name` column was exported in, which
   natural key each reference column used, which calendar dates were written in, or which tenant
   and schema the file came from. The header row alone cannot carry this. A hidden manifest sheet
   is required (D6, §4.1).
9. **Ethiopian calendars are unaddressed.** Excel has no Ethiopian calendar; the export needs a
   text-date fallback and the importer must parse it (D5).
10. **The synchronous/background boundary is missing.** "Up to a limit" names no limit; import has
    no size or row ceiling; a 1M-row import in one transaction is unrealistic (log growth, lock
    escalation, an hour-long lease). Chunked background import with per-chunk atomicity is a
    deliberate decision because it changes the all-or-nothing promise (D3, D14).
11. **Error reporting is unspecified.** Pipeline validation errors are keyed by entity path
    (`[15].RoleMemberships[2].RoleId`); an Excel user needs sheet, row and column (D12).
12. **The file has no intake path.** Every web endpoint is JSON over POST; an import file is
    binary. Import rides the blob theme's staged uploads rather than inventing multipart (D13).
13. **Four export operations are two axes.** Source (query | ids) × shape (display | editable):
    two operations whose request carries either a filter or an id list (D2).
14. **"Merge" is a poor name.** It collides with SQL `MERGE` (banned) and with JSON merge-patch;
    `Upsert` is the industry term (D9).
15. **Security gaps.** Resolving a reference by natural key must respect the user's read filter on
    the referenced entity or the importer is an existence oracle; the uncompressed size of an
    uploaded package is unbounded (zip bombs); the manifest is untrusted input; a background
    import retried after a crash could re-apply a committed chunk (D10, D13, D14, D15).
16. **Small things.** Sheet names are capped at 31 characters and cannot contain `/ \ ? * : [ ]`;
    `ExportByIds` does not say in which order rows come back; `Search` is not mentioned for export
    though the grid uses it; export-for-import of zero rows is the obvious "download a template";
    nothing distinguishes an on-screen read from bulk extraction.

---

## 2. Decisions

### Round trips per operation (common case; the connect call rides the first business round trip as the pipeline design proposes — add one otherwise)

| Operation | DB round trips | What each carries |
|---|---|---|
| Export, display shape, by query or by ids | 1 | connect + the grid query with `Take = cap + 1` (ids through the TVP list restriction) |
| Export, editable shape, by ids | 1 | connect + parent flat query + one flat query per child collection, all restricted by the id TVP |
| Export, editable shape, by query | 2 | connect + parent flat query (`Take = cap + 1`); then one child query per collection restricted by the returned parent ids (bounded by the cap) |
| InspectImport | 0 data round trips | connect/permission check only; the plan is computed from the staged file |
| Import, Insert, no reference columns | 2 | connect + id reservation + the pipeline's validation context; persist |
| Import, any mode with references, or Update/Upsert | 3 | connect + resolution batch (lookups, row-key hydration, save-filter check, existing children, id reservation); validation context; persist |
| Import, background, N chunks | 3 per chunk | the same three per chunk, the task's progress stamp riding the persist transaction |

No lock or connection is held across file I/O: export drains the reader into a local temp file
and releases the connection before the first byte reaches the response or the blob store; import
parses the whole file before its first DB call, and the persist transaction opens inside the
pipeline after every file read has completed.

### D1 — Library and packaging: Open XML SDK 3.5.1 directly, forward-only, in `Tellma.Core.Excel`

**Decision.** The codec is written on `DocumentFormat.OpenXml` 3.5.1 (MIT), pinned in central
package management, using `OpenXmlWriter` for sheet parts on export and `OpenXmlReader` for
sheet parts on import. The package is `src/core/Tellma.Core.Excel/` (Core family). It references
`Tellma.Core.Abstractions` and `Tellma.Core`; **`Tellma.Core` never references
`Tellma.Core.Excel`** — the codec is a feature layered on the service pipeline, not a dependency
of it. Tests: `test/core/Tellma.Core.Excel.Tests/` (pure, no database) and
`test/core/Tellma.Core.Excel.IntegrationTests/` (LocalDB fixture entities, `Category=Integration`).
Distribution-facing types live in `Tellma.Core.Abstractions` under namespace
`Tellma.Core.Abstractions.Excel` (§3.1); the writer, reader, planner, mapper and resolver are
internal to `Tellma.Core.Excel`.

Export writes inline strings (`t="inlineStr"`), a `styles.xml` with one `cellXfs` entry per
distinct number format used, `workbookPr date1904="0"`, serials from the 1899-12-30 epoch, a
frozen bold header row with autofilter, and column widths from label length and type. Import
reads `workbookPr/@date1904`, validates the package structure, streams the shared-string part into
a `SharedStringStore` (in memory up to `ExcelOptions.MaxInMemorySharedStringBytes`, then an
offset-indexed temp file), and reads each sheet part forward-only. The XML readers run with DTD
processing prohibited and no external resolver.

**Rationale.** It is the only MIT, Microsoft-maintained option that keeps memory flat with row
count in both directions, can emit everything the editable shape needs (`dataValidation` lists, a
hidden sheet, `sheetProtection`, freeze panes, autofilter, hidden columns, right-to-left sheet
views) and carries no transitive license risk into distributions, each of which is a separate
legal entity. The cost — roughly a thousand lines of platform code — is paid once. The dependency
direction keeps Excel replaceable and keeps the Open XML dependency out of `Tellma.Core`.

**Rejected.** ClosedXML (DOM; out-of-memory reports near 350k rows; SixLabors.Fonts split license
as a transitive dependency a distribution must never reference directly). EPPlus (Polyform
Noncommercial; a per-developer commercial license and a license key in configuration for every
distribution author). NPOI (binary maintenance-fee EULA; SkiaSharp native binaries in every web
host). MiniExcel (Apache-2.0 and streaming, but no data validation, protection, defined names or
custom XML parts; 2.0 is a preview with a breaking split). Sylvan.Data.Excel stays a named
reading accelerator if the SAX reader proves slow; not adopted now (a second parsing path).

**Confidence.** High. Review flag 9.1.

### D2 — Four operations, contributed by the Excel feature as projections of existing capabilities

**Decision.** For every entity the stack feature marks *queryable*, the Excel feature contributes
`Export`; for every entity marked *savable with a key* it additionally contributes
`ExportForImport`, `InspectImport` and `Import`. The operations are implemented by
`ExcelOperations<TEntity>` in `Tellma.Core.Excel` on top of the pipeline's public query, save,
batch and permission APIs; the pipeline has no Excel knowledge. The web theme projects endpoints
and the MCP tools from the operation registry the stack feature exposes and features contribute to.
A distribution writes zero lines: no registration, no mapping class, no DTO. The only optional
lines are `[NaturalKey]` on a property (D7) and `[ExcludeFromExcel]` on an editable property that
must never appear in a sheet (a JSON settings bag, for example).

`ExportRequest` and `ExportForImportRequest` carry *either* `Ids` *or* the query clauses
(`Filter`, `OrderBy`, `Search`, `Arguments`, matching the pipeline's query request); supplying both
or neither is a caller error. With `Ids`: the id count must not exceed the applicable row cap;
rows come back ordered by `OrderBy` when supplied, else in id-list order (buffered; the list is
bounded by the request payload limit). An empty `Ids` array is valid for `ExportForImport` and
yields the entity's import template: headers, validation lists, manifest, no rows. The MCP sketch
maps `Export`/`ExportForImport` to one `export_records` tool with a shape argument and
`InspectImport`/`Import` to one `import_records` tool with an inspect-only argument.

**Rationale.** Source × shape as one request halves the surface for the pipeline, the endpoint
projection and the MCP tool list; the template falls out for free; the feature-contribution shape
is the composition seam already planned.

**Rejected.** Four `ExportBy…` methods (pure duplication); a separate `Template` operation; the
codec as a dependency of the pipeline (inverts the package direction and drags Open XML into
every host).

**Confidence.** High.

### D3 — Display-shape export: the grid as a sheet, spooled, capped, never truncated

**Decision.** `Export` runs exactly the query the pipeline's query operation would run for the
same request (same `Select`, `Filter`, `OrderBy`, `Search`, `Arguments`, the same RLS composition,
`Skip = 0`, `Take = cap + 1`, no ancestors, no count) and writes one data sheet plus the hidden
manifest sheet (`Shape = Display`):

- Sheet name: the entity's plural label in the request language, scrubbed of `/ \ ? * : [ ]`,
  trimmed to 31 characters, never `History`.
- Header row: `request.Headers[i]` when supplied (a parallel array the SPA sends from its grid
  definition; each capped at 255 characters), else derived: the localized property label chain
  for a column with a `Path` (`Center / Code`, `Name (E)`), the select item's source `Text` for a
  computed expression. Duplicate headers get a ` (2)`, ` (3)` suffix.
- One column per `QueryexColumn`, encoded from its `QueryexType` (D4) with the number format of
  D5. A bare-path decimal column takes its scale from the property's store type; a computed
  numeric column uses Excel's `General` format.
- Right-to-left sheet view when the request language is right-to-left; frozen header; autofilter;
  widths.

Rows are drained from the data reader into a temp file under the host's scratch directory opened
with delete-on-close; the connection is released when the reader completes; the file is then
copied to the HTTP response (synchronous) or uploaded to the blob store (background). The file
name is `<Entity plural, ASCII-safe> <yyyy-MM-dd HHmm>.xlsx`, with the UTF-8 `filename*` form added
by the web layer.

Caps: `ExcelOptions.MaxSynchronousExportRows` (default 100,000). When the query returns
`cap + 1` rows the synchronous export **fails** with `ExcelErrorCodes.ExportRowLimitExceeded`
(carrying the cap) instead of truncating; the SPA offers "export in the background", which is the
same request with `Background = true`. A background export is capped at `ExcelOptions.MaxExportRows`
(default 1,048,575 — one sheet under a header) and fails with the same code beyond it. No
multi-sheet chunking.

**Rationale.** Silent truncation is data loss the user cannot see; a hard, named cap with a
background path is honest. Spooling keeps the DB connection short and the working set flat
regardless of client bandwidth. One sheet keeps "same as the grid" literally true.

**Rejected.** Streaming the package straight to the response (holds the connection for a slow
download; needs a seekable stream anyway); automatic promotion to background at the cap (the
response shape would change mid-request); multi-sheet chunking (breaks sorting and filtering; a
million-row extract belongs to the reporting tier).

**Confidence.** High on shape; medium on the default caps. Review flag 9.2.

### D4 — Cell encoding by type, decoding by mapped type

**Decision.** The codec encodes by the *property or column type*, never by value, so a column is
homogeneous and sortable; the importer decodes by the *mapped property type*, never by cell
format.

| Type | Display export | Editable export | Import accepts |
|---|---|---|---|
| `bool` | boolean cell | boolean cell + validation list `TRUE,FALSE` | boolean; text `true/false/yes/no/1/0`; the localized labels |
| `byte`, `short`, `int` | number | number, format `0` | number (integral); text parsed invariant |
| `long` | number (precision loss above 15 digits documented) | **text cell**, column format `@` | number or text |
| `decimal(p,s)` | number, format from scale (`General` when computed) | number; **text when the value needs more than 15 significant digits**; column format from scale | number; text parsed invariant (`NumberStyles.Number` without thousands separators) |
| `double`, `float` | number | number | number or invariant text |
| `string` | inline string, column format `@` | inline string, column format `@`, `dataValidation textLength ≤ MaxLength` when declared | any cell; numbers converted with invariant formatting; trimmed; length checked by the pipeline |
| enum-as-string | localized label | stored value + validation list of stored values (when the comma-joined list is ≤ 255 characters) | stored value, or any tenant-language or English label |
| `DateOnly` | serial + date format (D5), or text for Ethiopian | serial + date format, or text in the manifest's calendar and pattern | integral serial (date1904-aware; `< 61` refused as suspect); ISO 8601 text; text in the column's manifest calendar and pattern; else the request calendar's short pattern |
| `DateTime` | serial with fraction + datetime format | the same with time tokens | as above plus time |
| `DateTimeOffset` | converted to the request time zone, serial + format | **text**, ISO 8601 with offset, `@` | ISO 8601 text; a serial is interpreted in the request time zone |
| `TimeOnly` | fraction + `hh:mm:ss` | the same | fraction or `HH:mm[:ss]` text |
| `Guid` | text | text, `@` | text |
| `hierarchyid` | its string path, text | **excluded** | — |
| `byte[]`, JSON-typed, tree bookkeeping, computed | display export only when selected: JSON as text, `byte[]` blank | **excluded** | — |
| audit columns (`CreatedAt`…) | by their own type, when selected | **excluded** (server-owned) | — |
| null | empty cell | empty cell | blank ⇒ null (D9) |

Decoding rules for cell kinds: `t="s"` resolves through the shared-string store; `inlineStr` and
rich-text runs are concatenated; `t="str"` and formula cells use the cached value (`<v>`), a
formula without a cached value is blank; `t="b"`, `t="n"`, `t="d"` as their kinds; `t="e"` (an
Excel error such as `#N/A`) is `InvalidCell`. A non-integral serial in a `DateOnly` column is
`InvalidCell`. Every decode failure is `InvalidCell` at the cell with the expected type as an
argument.

Cells beginning with `=`, `+`, `-`, `@` are safe: inline string cells are never formulas in
SpreadsheetML (formulas are `<f>` elements), so the CSV-injection class does not exist for `.xlsx`.
This must be re-evaluated if CSV is ever added.

**Rationale.** Homogeneous typed columns are what users sort and filter; text fallbacks are the
only lossless carrier for wide types; deciding by property type removes the cell-format guesswork
every generic importer gets wrong.

**Confidence.** High. Review flag 9.3.

### D5 — Localization metadata is a per-column number format; Ethiopian falls back to text; import never reads a format

**Decision.** Number formats are built from the request context (culture, calendar) at export time
and stored as one `numFmt` per distinct string:

- Numbers: `#,##0` for integers, `#,##0.` + `0` × scale for decimals. Separators in format codes
  are locale-independent placeholders; Excel renders them per the viewer's locale.
- Dates: the culture's `ShortDatePattern` translated token by token (`d`→`d`, `dd`→`dd`, `M`→`m`,
  `MM`→`mm`, `MMM`→`mmm`, `MMMM`→`mmmm`, `yy`→`yy`, `yyyy`→`yyyy`, literal separators quoted);
  times `HH:mm:ss`→`hh:mm:ss`, `tt`→`AM/PM`. Prefixed with the locale tag `[$-CCLLLL]` where `LLLL`
  is the culture's LCID in hex (omitted for custom cultures reporting 0x1000) and `CC` is the
  Windows calendar code: none for Gregorian, `06` for Hijri (arithmetic), `17` for Um Al Qura.
  Example: `[$-170401]dd/mm/yyyy` for an Arabic, Um Al Qura request.
- Ethiopian (and any calendar without a Windows calendar id): date cells are written as **text**
  rendered by the platform's calendar-aware formatter in the culture's short pattern; the manifest
  records `DateEncoding = Text`, the calendar and the pattern per column, and the importer parses
  that column with them. The round trip is exact because export and import share the pattern.

Import never reads a cell format. The manifest's calendar and pattern influence only the parsing
of *text* date cells.

**Rationale.** Excel's number-format grammar carries LCID, calendar and pattern; Hijri and Um Al
Qura render natively; nothing else is possible for Ethiopian. Mirroring the metadata in the
manifest means a Gregorian user importing an Ethiopian-exported file still gets correct dates.

**Rejected.** A second display column next to a serial column for Ethiopian tenants (doubles
columns, confuses mapping); Gregorian serials for Ethiopian users (correct data, unusable sheet).

**Confidence.** Medium-high: the `[$-CCLLLL]` encoding is triangulated across Microsoft's grammar,
Windows calendar ids and LibreOffice's documentation, not stated on one Microsoft page; a
round-trip test through desktop Excel is part of the milestone plan.

### D6 — Editable-shape workbook: one parent sheet, one sheet per child collection, one hidden manifest sheet

**Decision.** `ExportForImport` produces:

1. **Parent sheet**, named as in D3. Columns in order: `Id` (number for `int` keys, text for
   `long`), a hidden `Stamp` column (the concurrency token's wire form, `@`), then every *editable*
   scalar property in declaration order — each foreign key replaced by one reference column named
   `<Navigation label> / <Key label>` (`Center / Code`, `Parent / Code`, `Role / Name (E)`), each
   multilingual group expanded into one column per configured tenant language (`Name (E)`,
   `Name (ع)`; plain `Name` for a monolingual tenant). `request.Columns` restricts the set to a
   subset of paths; `Id`, `Stamp` and the default natural key column are always kept.
2. **One child sheet per child collection** when `request.IncludeChildren` (default true), named
   `<Parent plural> · <Collection label>` truncated to 31 characters (the manifest maps sheet
   names, so truncation is safe; a collision after truncation gets a ` (2)` suffix). Columns: the
   parent reference column first (`User / Email`), then the child's `Id`, then the child's editable
   properties and reference columns as above. Child rows are grouped under their parent in parent
   order.
3. **A hidden sheet `_tellma`** holding the manifest (§4.1), protected without a password against
   accidental edits; hidden, not `veryHidden`, so a curious user can inspect it.

Headers are localized labels in the request language and are unique per sheet (a collision gets
the technical path in parentheses). Validation lists (D4) go on boolean and enum columns; `@` on
text-typed columns; the header row is frozen and filterable; data sheets are not protected, so
users can insert rows and sort.

Reference keys per foreign key: `request.ReferenceKeys` (`{"CenterId": "Code"}`) overrides the
target entity's default natural key (D7); a target with no unique natural key is exported by
surrogate id (`Center / Id`) and the manifest marks the column `KeyKind = Surrogate`.

Per-sheet cap: `ExcelOptions.MaxExportRows` per sheet (children included); exceeding it fails
the export with `ExportRowLimitExceeded` naming the sheet.

**Rationale.** One sheet per collection keyed by the parent's natural key is the shape Dynamics
365 Finance & Operations staging and its Excel add-in use; every sheet stays a rectangular table
that sorts, filters and pastes cleanly, unlike Odoo's blank-parent-cell rows. `Id` and `Stamp`
give same-tenant round trips exact identity and change detection (the Dataverse hidden-column
pattern), gated by the manifest's source identity (D9). A hidden sheet is the one manifest carrier
every spreadsheet application round-trips; custom XML parts and defined names are not known to
survive Google Sheets.

**Rejected.** Children as extra rows under the parent with blank parent cells (Odoo); children in
a JSON cell; a custom XML part as the sole manifest carrier (portability unverified); a visible
manifest sheet (users delete or edit it).

**Confidence.** High on layout; medium on carrying `Id`/`Stamp`. Review flag 9.4.

### D7 — Natural keys: one attribute, unique-index-backed, inferred when absent, surrogate fallback

**Decision.** The entity contract (owned by the data-access theme, consumed here) carries
`[NaturalKey(Order?)]` on a property, with these rules:

- A `[NaturalKey]` property on a top-level entity must be backed by a single-column unique index
  in the EF model (filtered `WHERE [col] IS NOT NULL` when nullable). On a child entity it must be
  backed by a unique index on `(ParentKey, Property)`. Violations fail the aggregated startup
  validation, not a request.
- A multilingual group (`Name`/`Name2`/`Name3`) is one natural key named after its primary member;
  the slot used for a given file column is chosen per language (D8).
- **Inference** when no attribute exists, in this order and only among properties backed by a
  single-column unique index: `Name` (group), `Code`, then required unique string properties in
  declaration order, then nullable unique string properties. Declared keys precede inferred ones
  in ascending `Order`; the ordered list is `EntityMetadata.NaturalKeys`, and the first entry is
  the default key.
- Composite natural keys are a non-goal for this release (D18); a composite unique index is not a
  candidate.
- A reference may be resolved through **any** scalar property of the target when the caller says
  so (`ReferenceKeys` on export, `ColumnMappings` on import) — including non-unique ones — and
  ambiguity is then a validation error (D10). Row identity (D9) accepts only a natural key or `Id`.
- **No natural key** on a target: references export by surrogate `Id`, flagged `Surrogate` in the
  manifest; importing such a column into a different tenant is a mapping error
  (`SurrogateReferenceFromOtherTenant`) unless the caller remaps it. A startup *warning* lists
  entities referenced by an importable entity that have no unique natural key.

**Rationale.** Uniqueness must be a database guarantee, not a C# check (write skew). One attribute
is the mechanical minimum; inference makes the common entity work with zero lines; refusing to
mandate a natural key keeps line-item-like top-level entities legal while making the fallback
visible.

**Rejected.** Mandating a unique-and-required natural key on every entity; the brain dump's chain
into non-unique columns; class-level composite declarations (deferred).

**Confidence.** High.

### D8 — Column and sheet mapping: manifest, then header text, then labels in any tenant language, then technical paths; language-aware; user override

**Decision.** Mapping runs at inspect and import time and produces an `ImportPlan`:

1. **Sheets.** A sheet is matched to the parent entity or a child collection by the manifest's
   sheet table, else by its name against the entity and collection labels in every tenant language
   and English, else against the technical collection name (`RoleMemberships`). The first
   non-hidden sheet is the parent sheet when no manifest exists. Unmatched sheets are ignored with
   a warning. `request.SheetMappings` overrides.
2. **Columns.** Each header cell is matched, in order: (a) the manifest's column table by header
   text (exact) — this yields the path *and* the language of a multilingual column and the key of
   a reference column; (b) the header parsed as `<Label>`, `<Label> (<symbol>)`, or
   `<Navigation label> / <Key label>[ (<symbol>)]`, with labels resolved in every tenant language
   and English and symbols resolved through the language symbol table; (c) the technical path
   (`Name2`, `Parent.Code`, `CenterId`). Multilingual columns map to the tenant slot of the column's
   *language*: a file column in Arabic maps to whichever of `Name`/`Name2`/`Name3` is Arabic in the
   target tenant. A column in a language the target does not configure is **ignored with the
   warning `UnconfiguredLanguageColumn`** (the value has no home). A header `Name` without a
   symbol maps to the primary slot. Two columns mapping to the same path is a `DuplicateColumn`
   error.
3. **Override.** `request.ColumnMappings` (sheet + header text or column letter → path, or → null
   to ignore) wins over everything.
4. **Unmapped columns** are errors (`UnmappedColumn`, with up to three suggested paths by label
   similarity) unless `request.IgnoreUnmappedColumns`.
5. **Write-once and server-owned paths** are mappable only where D9 allows.

The label catalogue and symbol table come from the localization theme and are cached per
(entity, tenant settings tag, resource version); the connect step reads the settings tag before
the operation runs, so labels are never older than the request.

**Rationale.** The manifest makes unchanged files map perfectly with zero user input; the parsed
label chain covers renamed or hand-made files across tenants whose languages differ; the technical
path is the escape hatch for scripts and agents; the override is the human escape hatch. Failing on
unmapped columns prevents the classic silent "column ignored" data loss.

**Rejected.** Mapping by column position (users insert and reorder columns); mapping by header only
(no language, no key information); silently ignoring unmapped columns.

**Confidence.** High. Review flag 9.5.

### D9 — Import modes `Insert | Update | Upsert`; row identity; hydration; blank cells; children

**Decision.**

- `ImportMode.Insert`: every parent row is new. `Id` and `Stamp` columns are ignored (the pipeline
  allocates ids). Duplicates against the database surface through the pipeline's uniqueness
  pre-check and, ultimately, unique-index errors (2601/2627) mapped to the row and column.
- `ImportMode.Update`: `RowKey` (a natural key path or `Id`) identifies each row; a row whose key
  resolves to no existing row is `RowNotFound` at (row, key column); every row must resolve.
- `ImportMode.Upsert`: found rows follow the Update path, the rest the Insert path.
- `RowKey` default: the manifest's `RowKey` if that column is present in the sheet, else the
  entity's default natural key if present, else `Id` when the file is **same-source** — the
  manifest's `Distribution` equals the running distribution's slug *and* `SourceTenantId` equals
  the target tenant. `Id` is refused as a row key otherwise (`IdKeyRequiresSameTenant`).
- **Hydration.** For Update/Upsert the resolution round returns the complete editable column set
  of every matched row (one flat Queryex query restricted by the key TVP, under the user's read
  filter); the codec overlays the mapped columns on the hydrated values, so a partial sheet touches
  only the columns it contains.
- **Blank cells** in a mapped column set the property to null; a non-nullable property then fails
  the pipeline's required-value validation at that cell. To leave a column untouched, remove it
  from the sheet or map it to null.
- **Write-once properties** (as marked by the entity contract): mappable in Insert; in
  Update/Upsert a value differing from the hydrated value is `WriteOnceChanged` at that cell;
  equal values are accepted (the natural key is often the write-once column).
- **Server-owned properties** (audit, tree bookkeeping, computed, and anything a capability
  declares as not save-editable): never mappable (`ServerOwnedColumn`), including when a
  display-shape export is fed back in.
- **Concurrency.** When the sheet carries `Stamp` and the file is same-source, each updated row's
  stamp is passed to the pipeline as its expected stamp; a mismatch is the pipeline's concurrency
  conflict (`ConcurrencyConflict` at the row) unless `request.OverrideConcurrency`. Without a
  stamp no check is made (documented).
- **Children.** A child sheet present in the workbook means "these are the complete children of
  every parent row present in the parent sheet": each parent's collection is synchronized to its
  sheet rows; a child row whose parent is absent from the parent sheet is `OrphanChildRow`. A child
  sheet absent from the workbook leaves children untouched (the collection is passed as *absent*,
  the pipeline's "missing collection is not touched"). Child rows match existing children by the
  child's `Id` column (same-source), else by the child's declared natural key within the parent,
  else the collection is replaced (delete and insert). Existing children are loaded in the
  resolution round through the parent key path, one query per collection.
- **In-file uniqueness.** Before any DB call, duplicate row-key values within the sheet are
  `DuplicateRowKey` errors, and duplicates in any unique column are reported against both rows.

**Rationale.** These are the save contract's semantics mapped onto sheets, with the two
ambiguities (blank cells, absent child sheets) resolved in the direction that never silently drops
data and is statable in one sentence each. The same-source gate uses the distribution slug because
tenant ids are unique only within a distribution.

**Rejected.** Blank means "keep" (a sheet then cannot clear a value; a `#NULL` sentinel is a
footgun); a child sheet with no rows for a parent meaning "keep" (the collection could never be
emptied through Excel); Odoo-style "link to the first match" on ambiguity.

**Confidence.** High on modes; medium on children. Review flag 9.6.

### D10 — Bulk natural-to-surrogate resolution: Queryex queries with a TVP list restriction under the target's read filter

**Decision.** The codec emits one `ExcelQuery` per distinct *(target entity, key path)* pair across
all sheets and columns (the row key, every reference column, every child sheet's parent reference,
tree parents not found in-sheet); the pipeline compiles each as a Queryex query:

- `Root` = target entity; `Select` = `"<KeyPath>, Id"` (plus the editable columns when the lookup is
  also the hydration of the row key); `Filter` = the user's **read** filter on the target (from
  permission evaluation); a **list restriction** `<KeyPath> in <TVP>` bound to `StringList` /
  `IdList` / `BigIdList` / `GuidList` by the key's type. Every lookup rides one batch with the
  connect call and the id reservation. The compiled shape, illustratively (the engine names the
  TVP parameter in its own namespace):

```sql
SELECT [T].[Code], [T].[Id]
FROM   [gl].[Centers] AS [T]
WHERE  [T].[Code] IN (SELECT [Id] FROM @qx3_l0)          -- list restriction, StringList TVP
  AND  (<the user's read predicate on Center, compiled from its FilterTree>)
```

- **The save-filter check rides the same batch.** For Update/Upsert the resolution batch also
  carries, per row-key lookup, a second statement with the same TVP and the user's *save* filter
  returning ids only. A row found by hydration but absent from that set is the pipeline's RLS
  pre-check failure, reported at the row; no fourth round trip exists. (If the pipeline performs
  its pre-check in the validation round instead, the codec hands it the hydrated ids there.)
- Matching back in C#: sheet values are trimmed and NFC-normalized; DB values are grouped with
  `StringComparer.OrdinalIgnoreCase` after the same normalization. Exactly one row ⇒ resolved;
  zero ⇒ `ReferenceNotFound` at (row, column) — which is also what an RLS-hidden row yields;
  more than one ⇒ `ReferenceAmbiguous` with the match count. A DB match C# cannot attribute to any
  sheet value (a collation equivalence beyond case, such as accent- or width-insensitivity) is
  `ReferenceNotFound` with argument `collation = true` so the message can hint at it.
- Key values longer than 450 characters (the `StringList` column width) are `KeyTooLong` before
  any query.
- **In-sheet first.** Before emitting a lookup for a reference to the entity being imported (tree
  parents; any self-reference), the value is matched against the sheet's own rows by the same key
  (the sheet's *mapped* column for that key). A hit binds the reference to that row's entity — its
  allocated id for new rows, its hydrated id for existing ones — and no query is needed.
- Multilingual keys resolve through the tenant slot of the *file column's* language (D8); the
  restriction path is then `Name2` rather than `Name` when that is the slot.

**Rationale.** A Queryex query composes the read filter (fail closed), gates `Name2`/`Name3`, and
produces one plan per (entity, key) per tenant; the TVP restriction is the engine amendment the
data-access theme already plans for `GetByIds`. Distinct-value TVPs make the cost O(distinct
values), not O(rows × columns). Applying the read filter keeps the importer from being an
existence oracle.

**Rejected.** Raw SQL with the TVP as the driving table (cannot apply the RLS `FilterTree`, which
compiles only through `CompileQuery`); resolving without the target's read filter; per-row lookups.

**Confidence.** High on the mechanism; medium on the C#-side equality rule under exotic
collations. Review flag 9.7.

### D11 — Tree import: parents by natural key, in-sheet first, topological order, cycle check

**Decision.** For a tree entity the `Parent / <Key>` column is an ordinary reference column (D6,
D10). Resolution order: in-sheet match, then DB lookup, then `ReferenceNotFound`. After ids are
allocated the codec sets the parent key on every row, orders rows so that in-sheet parents precede
their children (Kahn's algorithm over the in-sheet graph; a cycle among sheet rows is
`ParentCycle` reported at every row in the cycle before any DB call), and hands the ordered list to
the pipeline, whose tree capability validates cycles against the loaded ancestor chains and
appends the node recompute statement. A single bulk `INSERT` from one TVP satisfies the
self-referencing foreign key regardless of row order (constraints are checked at statement end),
so the ordering exists for chunk boundaries (D14) and readable errors, not for insert correctness.

**Rationale.** Consistency with every other reference; no id needed for new parents; cycles among
new rows can only be detected in memory, and the codec is the one component that sees the whole
sheet.

**Confidence.** High.

### D12 — Errors carry sheet, row and column; capped; synchronous import is all-or-nothing

**Decision.** Every codec error is an `ImportError(Sheet, Row, Column, Header, Path, Code,
Arguments)`; pipeline validation errors keyed by entity path are translated through the codec's
coordinate map (entity index ⇒ sheet row; property path ⇒ column when the column exists in the
sheet, else the row alone; child path ⇒ the child sheet's row). `ImportException` (a subtype of the
pipeline's validation exception) carries the list capped at `ExcelOptions.MaxReportedErrors`
(default 1,000) plus `TotalErrors`; the web theme maps it to 422 problem details whose `errors[]`
items carry the coordinates. Codes are stable constants in `ExcelErrorCodes`; messages are
localized by the host. A synchronous import is one transaction: any error anywhere rolls back
everything. An "errors workbook" (the upload echoed with an error column) is a non-goal for this
release (D18).

**Rationale.** Coordinates are the only thing an Excel user can act on; capping keeps a
million-row failure from returning a million-item payload; all-or-nothing is the save contract and
is what the brain dump asks for at synchronous scale.

**Confidence.** High.

### D13 — File intake through blob staging; package limits; shared-string store

**Decision.** Import files never travel in a JSON body. The SPA (or an agent) uploads the file to
the blob theme's staging endpoint and receives a staging token; `InspectImportRequest.FileToken`
and `ImportRequest.FileToken` name it. The codec copies the staged blob to a local temp file
(delete-on-close) and reads it from there — twice for background imports (D14). Staged files that
are never imported are swept by the blob theme's orphan collection; a completed import confirms
the blob with a retention of `ExcelOptions.ImportFileRetentionDays` (default 7) so the task record
can link to it.

Limits enforced before parsing: `MaxImportFileBytes` (compressed, default 256 MB;
`MaxSynchronousImportFileBytes` 16 MB); `MaxImportUncompressedBytes` (sum of part lengths from
the zip central directory, default 2 GB; parts are read through a counting stream that aborts past
the limit); one workbook part; no external links; a `.xlsm` is read as a workbook and its macros
ignored. Row counts for the synchronous decision come from each sheet's `dimension` element, or
from a forward count when it is absent.

Shared strings stream into `SharedStringStore`: in memory up to
`MaxInMemorySharedStringBytes` (default 32 MB of UTF-16 text); beyond that an offset index over a
temp file.

**Rationale.** One intake path shared with images and attachments; a file on local disk can be
read twice; the limits stop zip bombs and runaway memory; the shared-string table is the only
unbounded part of a user-saved file, and a byte budget (not an entry count) is what bounds a web
host serving several imports at once.

**Rejected.** A multipart import endpoint (a second intake path; still needs staging for
background); reading the blob stream directly (not seekable twice; slow re-reads from Azure).

**Confidence.** High. Review flag 9.8.

### D14 — Background hand-off: thresholds, chunked and resumable persistence, progress, results, inbox

**Decision.** A request with `Background = true` enqueues a task through the background-task
machinery (riding the same connect round trip) and returns `ExportOutcome.TaskId` /
`ImportOutcome.TaskId` with HTTP 202 semantics. A synchronous import whose row count exceeds
`MaxSynchronousImportRows` (default 10,000 rows summed over sheets) or whose file exceeds
`MaxSynchronousImportFileBytes` fails with `ImportTooLargeForSynchronous` naming the limits; the SPA
re-submits with `Background = true`. The task payload is `ExcelTaskPayload` (§4.3): the serialized
request, the entity name, the staged file token, and the captured request context (tenant, user,
culture, calendar, time zone, sandbox flag). The handler runs as the requesting user with
permissions evaluated at run time, and the task runtime re-runs the connect step at start (a user
deactivated since enqueue gets nothing).

**Background export**: the same code as D3 with `MaxExportRows`; the workbook is written to the
blob store under the task with a retention of `ExportFileRetentionDays` (default 7); the inbox item
`ExcelExportReady` links to the etag-validated download.

**Background import** runs two passes over the local copy:

1. **Pass 1 (no DB):** mapping, type decoding of every cell, in-file uniqueness of row keys and
   unique columns, the in-sheet parent graph and cycle check, row counts. Any error aborts before
   any commit.
2. **Pass 2:** rows are processed in chunks of `ImportChunkRows` (default 10,000 parent rows plus
   their child rows; tree rows are chunked in topological order so a parent's chunk never follows
   its child's). Each chunk runs resolution, validation and persist as its own transaction (three
   round trips). **The task's progress stamp (`ChunkIndex`, `RowsProcessed`) is written in the
   chunk's persist transaction**, so a task re-leased after a crash or lease loss resumes at
   `ChunkIndex + 1` after re-running pass 1 — a committed chunk is never re-applied. A validation or
   persist error in chunk *k* stops the task; chunks `< k` stay committed; the outcome reports
   `CommittedRowRanges` and the errors of chunk *k*. Cancellation (user or lease loss) takes effect
   at a chunk boundary; an in-flight transaction rolls back. `ImportRequest.Atomic = true` instead
   runs the whole file as one transaction and is refused above `MaxAtomicImportRows` (default
   100,000).

Progress: phases `Parsing | Resolving | Validating | Saving | Uploading`, rows processed, rows
total, chunk index; the runtime renews the lease while the handler runs (its sliding lease; the
handler needs nothing). Completion writes the `ImportOutcome` (or the capped error list) as a JSON
blob under the task and raises `ExcelImportCompleted` / `ExcelImportFailed` inbox items. Export-
ready and import-completed notifications cannot be muted by the user's notification preferences
(the file is also reachable from the task page, so muting would not strand it; the default stays
on).

**Rationale.** Every industrial bulk tool commits in bounded batches; a single million-row
transaction escalates locks, bloats the log and holds a lease for an hour. Chunk-level atomicity
with an explicit committed-range report is honest; the progress stamp inside the chunk transaction
is what makes retry safe. Lock escalation within a 10,000-row chunk (above the 5,000-lock
threshold) is accepted: the transaction is short and the escalation is the same one a 10,000-row
JSON save incurs.

**Rejected.** Automatic promotion to background (response shape changes); one transaction always;
validate-all-then-persist-all (three file passes and double the DB reads for a guarantee that
concurrent writers can still break); progress written outside the chunk transaction (re-applies a
committed chunk on retry).

**Confidence.** Medium. Review flag 9.9.

### D15 — Access control and security posture

**Decision.** `Export` and `ExportForImport` require the entity's `read` action and apply the
user's read filter exactly as the query and details operations do; `InspectImport` and `Import`
require `save` and run the pipeline's RLS pre-check (on hydrated existing rows) and post-check.
Reference resolution applies the target's read filter (D10). The manifest is validated, never
trusted: paths must exist and be importable, sheet and column indexes are bounded, the same-source
gate only decides whether `Id`/`Stamp` are *considered*. Ids from a sheet are never inserted; they
select rows the RLS pre-check verifies. No separate `export` permission action exists in this
release.

**Confidence.** High. Review flag 9.10.

### D16 — Options, telemetry, naming

**Decision.** `ExcelOptions` is bound by the distribution host from the configuration section
`Tellma:Excel` (defaults in §3.1). Meter `Tellma.Core.Excel` with instruments named in
`ExcelMeters`: `tellma.excel.export.rows` (histogram), `tellma.excel.export.duration` (histogram,
ms), `tellma.excel.export.rejected` (counter: cap exceeded), `tellma.excel.import.rows`
(histogram), `tellma.excel.import.duration`, `tellma.excel.import.lookups` (histogram: resolution
queries per import), `tellma.excel.import.errors` (counter), `tellma.excel.import.chunks`
(histogram), `tellma.excel.import.resumed` (counter), `tellma.excel.sst.spilled` (counter). Tags:
`entity` (logical name), `shape` (`display|editable`), `mode`, `background` (bool). No tenant tag.

Vocabulary: "display shape" and "editable shape"; `Upsert`, not `Merge`; "row key" for the
row-identifying key; "reference key" for the natural key a foreign-key column is expressed in;
"manifest" for the hidden sheet; "stamp" for the concurrency-token column, whose header follows
whatever the data-access theme names the token.

**Confidence.** High.

### D17 — Testing

**Decision.** Pure tests (`Tellma.Core.Excel.Tests`): the planner over fixture entity metadata
(column sets, labels, formats, sheet names, truncation and collision suffixes); encoder/decoder
round trips per type including the 15-digit boundary; date systems (1900/1904, serial 60); every
cell kind of D4; manifest read/write; the mapping algorithm across tenant-language permutations;
in-sheet resolution and cycle detection; a fixture corpus of workbooks saved by Excel, LibreOffice
and Google Sheets (shared-string table present, `dimension` absent, renamed headers, inserted
columns, a 1904 workbook, a `veryHidden` sheet, an error cell, a formula without a cached value);
package-limit rejection with a crafted zip. Integration tests on LocalDB (`Category=Integration`):
export → import round trip of the fixture entities in all three modes with children and a tree;
resolution with RLS; chunked background import with a forced failure in chunk 2 reporting
committed ranges and resuming correctly after a simulated lease loss. The milestone plan includes
one manual round trip through desktop Excel on Windows for the Um Al Qura format tag.

### D18 — Non-goals for this release (explicit)

Multi-entity workbooks (the manifest has one `Entity` row; a later manifest version sequences
several entities per workbook the way Finance & Operations data packages do, reusing the
per-entity pipeline in dependency order — Excel stays the medium because the files must be
human-editable; zipped JSON or SQLite is rejected for that reason); CSV; reference dropdowns backed
by lookup sheets; an errors workbook; partial success within a synchronous import; composite
natural keys; per-row child `Action` columns; importing display-shape exports (they map, but
server-owned and computed columns fail as `ServerOwnedColumn`, by design).

---

## 3. Contracts

Contract blocks use the platform's contract notation: names are normative; shape is described,
not transcribed.

### 3.1 Owned by this theme — `Tellma.Core.Abstractions`, namespace `Tellma.Core.Abstractions.Excel`

```contract
annotation [ExcludeFromExcel]      on property   // never appears in any Excel shape

enum ImportMode = Insert | Update | Upsert

// The source half shared by both export requests: either Ids or the clauses, never both.
data ExportSourceRequest
  Ids: list<key>?                  // key = the entity's key type; empty is valid; order preserved
  Filter: string?                  // the pipeline's row-level filter shape
  OrderBy: string?
  Search: string?                  // translated by the service as for a query
  Arguments: map<string, object?>  // values for parameters used in the clauses
  Background: bool = false         // enqueue a task instead of answering synchronously

data ExportRequest : ExportSourceRequest            // display shape
  Select: string                   required        // exactly as the grid uses it
  Headers: list<string>?                           // parallel to the select items; derived when null

data ExportForImportRequest : ExportSourceRequest  // editable shape
  Columns: list<string>?                           // subset of property paths; all editable when null
  IncludeChildren: bool = true
  ReferenceKeys: map<string, string>               // foreign-key property -> target property, e.g. CenterId -> Code

record ExportOutcome(Workbook: Stream?, FileName: string?, Rows: int?, TaskId: TaskId?)

record ImportColumnMapping(Sheet: string, Column: string, Path: string?)   // Column = header text or letter; Path null = ignore
record ImportSheetMapping(Sheet: string, Collection: string?)              // "" = parent sheet; null = ignore

data InspectImportRequest
  FileToken: string                required        // blob staging token
  SheetMappings: list<ImportSheetMapping> = []
  ColumnMappings: list<ImportColumnMapping> = []

data ImportRequest
  FileToken: string                required
  Mode: ImportMode = Insert
  RowKey: string?                  // natural-key path or "Id"; required for Update/Upsert unless the manifest supplies one
  SheetMappings: list<ImportSheetMapping> = []
  ColumnMappings: list<ImportColumnMapping> = []
  IgnoreUnmappedColumns: bool = false
  OverrideConcurrency: bool = false
  Background: bool = false
  Atomic: bool = false             // background only: one transaction for the whole file

record ImportPlan(Entity: string?, ManifestVersion: int, ManifestPresent: bool, SameSource: bool,
                  SchemaFingerprintMatches: bool, Sheets: list<ImportPlanSheet>,
                  RowKeyCandidates: list<string>, DefaultRowKey: string?, TotalRows: long,
                  RequiresBackground: bool, Warnings: list<ImportError>)
record ImportPlanSheet(Sheet: string, Collection: string?, Status: ImportSheetStatus, Rows: long,
                       Columns: list<ImportPlanColumn>)
record ImportPlanColumn(Column: string, Header: string, Path: string?, Status: ImportColumnStatus,
                        Language: string?, ReferenceKey: string?, Suggestions: list<string>)
enum ImportSheetStatus = Parent | Child | Manifest | Ignored
enum ImportColumnStatus = Mapped | Unmapped | Ignored | Duplicate | ServerOwned

// Row is 1-based as Excel numbers it; Column is the letter; either is null when not cell-specific.
record ImportError(Sheet: string?, Row: int?, Column: string?, Header: string?, Path: string?,
                   Code: string, Arguments: list<(string, string)>)

record ImportOutcome(Inserted: int, Updated: int, ChildrenInserted: int, ChildrenUpdated: int,
                     ChildrenDeleted: int, CommittedRowRanges: list<(From: int, To: int)>,
                     Warnings: list<ImportError>, TaskId: TaskId?)

// A validation failure of an import; the web layer maps it to 422 with per-cell errors.
contract ImportException : ValidationException      // the pipeline's platform validation exception
  Errors: list<ImportError>        // capped at ExcelOptions.MaxReportedErrors
  TotalErrors: int

contract ExportRowLimitExceededException : ValidationException
  Cap: int
  Sheet: string?

data ExcelOptions                  // configuration section Tellma:Excel
  MaxSynchronousExportRows: int = 100_000
  MaxExportRows: int = 1_048_575
  MaxSynchronousImportRows: int = 10_000
  MaxAtomicImportRows: int = 100_000
  ImportChunkRows: int = 10_000
  MaxSynchronousImportFileBytes: long = 16 MB
  MaxImportFileBytes: long = 256 MB
  MaxImportUncompressedBytes: long = 2 GB
  MaxInMemorySharedStringBytes: long = 32 MB
  MaxReportedErrors: int = 1_000
  ExportFileRetentionDays: int = 7
  ImportFileRetentionDays: int = 7
```

`ExcelErrorCodes` (constants; hosts localize): `excel.export.rowLimitExceeded`,
`excel.import.tooLargeForSynchronous`, `excel.import.unmappedColumn`, `excel.import.duplicateColumn`,
`excel.import.serverOwnedColumn`, `excel.import.unconfiguredLanguageColumn` (warning),
`excel.import.writeOnceChanged`, `excel.import.rowNotFound`, `excel.import.duplicateRowKey`,
`excel.import.idKeyRequiresSameTenant`, `excel.import.surrogateReferenceFromOtherTenant`,
`excel.import.referenceNotFound`, `excel.import.referenceAmbiguous`, `excel.import.keyTooLong`,
`excel.import.orphanChildRow`, `excel.import.parentCycle`, `excel.import.invalidCell`,
`excel.import.concurrencyConflict`, `excel.import.packageTooLarge`, `excel.import.malformedWorkbook`.

`ExcelMeters` (constants): `MeterName = "Tellma.Core.Excel"` and the instrument names of D16.

### 3.2 Owned by this theme — the codec's seam inside `Tellma.Core.Excel`

The codec is pure: it never opens a connection. `ExcelOperations<TEntity>` runs the queries the
codec asks for through the pipeline and hands rows back.

```contract
// Flat rows over one root entity, optionally restricted to key values through a TVP; the
// pipeline adds the user's read filter, compiles with Queryex, and streams rows back.
record ExcelQuery(RootEntity: string, SelectPaths: list<string>, OrderBy: string?,
                  Restriction: ExcelListRestriction?, Take: int?)
record ExcelListRestriction(Path: string, Values: list<object>)   // "<Path> in (values)", TVP by the path's type

contract IExcelRowSource
  Rows(query: ExcelQuery) -> stream<list<object?>>

// Request-context facts the codec needs.
record ExcelContext(TenantId: int, DistributionSlug: string, TenantLanguages: list<string>,
                    Culture: CultureInfo, Calendar: string, TimeZone: TimeZoneInfo,
                    Labels: ILabelCatalog, Formatter: ICalendarFormatter)

record ExcelColumnSpec(Header: string, Type: QueryexType, Scale: int?, Path: list<string>?)

service IExcelExporter
  PlanDisplay(entity: EntityMetadata, columns: list<ExcelColumnSpec>, context: ExcelContext) -> ExcelWorkbookPlan   sync
  PlanEditable(entity: EntityMetadata, request: ExportForImportRequest, context: ExcelContext) -> ExcelWorkbookPlan   sync
  Write(plan: ExcelWorkbookPlan, rows: IExcelRowSource, destination: Stream) -> int   // rows written; throws past the cap

service IExcelImporter
  Inspect(workbook: Stream, entity: EntityMetadata, request: InspectImportRequest, context: ExcelContext) -> ImportPlan
  Parse<TEntity>(workbook: Stream, entity: EntityMetadata, request: ImportRequest, context: ExcelContext) -> ExcelImportSession<TEntity>

// A parsed workbook awaiting resolution; chunkable for background imports.
contract ExcelImportSession<TEntity>
  Lookups: list<ExcelQuery>        // deduplicated by (entity, key path)
  RowCount: int                    // parent rows in topological order
  Resolve(fromRow: int, toRow: int, lookupRows: map<ExcelQuery, list<list<object?>>>,
          allocateIds: (count: int) -> list<key>) -> ExcelChunk<TEntity>   sync

record ExcelChunk<TEntity>(Entities: list<TEntity>, ExpectedStamps: list<(Id: key, Stamp: string)>,
                           CoordinateMap: map<string, ExcelCell>, Errors: list<ImportError>)

// The four operations, contributed to the stack feature's operation registry per entity.
service ExcelOperations<TEntity>
  Export(request: ExportRequest) -> ExportOutcome
  ExportForImport(request: ExportForImportRequest) -> ExportOutcome
  InspectImport(request: InspectImportRequest) -> ImportPlan
  Import(request: ImportRequest) -> ImportOutcome
```

| Member | Meaning |
|---|---|
| `IExcelRowSource.Rows` | Streams `object?[]` rows for one query; the pipeline compiles it with the caller's read filter and the TVP restriction and reads with `NextResult()` when several queries share a batch. |
| `ExcelImportSession.Resolve` | Binds lookup results (rows of `[key, id, editable columns…]`), overlays hydrated values, assigns allocated ids to new rows, attaches children, sets tree parent keys, and returns the entities of one chunk with expected stamps and the coordinate map that translates pipeline errors back to cells. |
| `ExcelOperations.Import` | Orchestrates: permission check, copy staged file, `Parse`, resolution batch (lookups + save-filter check + existing children + id reservation), `Resolve`, the pipeline's bulk save with `ReturnEntities = false`, `Origin = Import`, expected stamps, and absent collections; translates errors; or enqueues the task when `Background`. |

### 3.3 Needed from other themes

**Data access (entity contract, Queryex host integration, batch).**

```contract
// Per-entity metadata derived once from the class and the EF model; the codec's only view of an entity.
record EntityMetadata(Name: string, ClrType: Type, Key: PropertyMetadata, Properties: list<PropertyMetadata>,
                      References: list<ReferenceMetadata>, Children: list<ChildCollectionMetadata>,
                      NaturalKeys: list<NaturalKeyMetadata>, MultilingualGroups: list<MultilingualGroup>,
                      TreeParentProperty: string?, ConcurrencyProperty: string?, SchemaFingerprint: string)
record PropertyMetadata(Name: string, ClrType: Type, IsNullable: bool, IsEditable: bool, IsWriteOnce: bool,
                        IsServerOwned: bool, IsUnique: bool, MaxLength: int?, Precision: int?, Scale: int?,
                        EnumValues: list<string>?, ExcludedFromExcel: bool)
record ReferenceMetadata(NavigationName: string, ForeignKeyProperty: string, TargetEntity: string)
record ChildCollectionMetadata(CollectionProperty: string, ChildEntity: string, ParentKeyProperty: string)
record NaturalKeyMetadata(PropertyPath: string, IsDeclared: bool, IsRequired: bool)
record MultilingualGroup(Name: string, Slots: list<string>)     // ("Name", [Name, Name2, Name3])
annotation [NaturalKey(Order?)]    on property
```

- A `QuerySpec` amendment `Restrictions: list<ListRestriction(Path, ParameterName)>` compiling to
  `[alias].[col] IN (SELECT [Id] FROM @name)`, the host binding `IdList`/`BigIdList`/`GuidList`/
  `StringList` by the path's type; the path may traverse navigations (`User.Email`).
- Every child entity present in the Queryex schema as a root with its parent navigation (needed
  for child export queries and hydration of existing children).
- The flat-row materializer (`object?[]` + `QueryexColumn.Path` → `TEntity`) for hydration.
- The id allocator usable before persist (ids assigned in memory), riding the resolution batch.
- The batch builder accepting several compiled queries and a save-filter statement in one round
  trip with `NextResult()` readers, and the task progress stamp as a statement in the persist batch.

**Service pipeline.** A bulk save entry `SaveBulk<TEntity>(entities, SaveOptions { ReturnEntities,
ExpectedStamps, OverrideConcurrency, Origin = Import, AbsentChildCollections })`; the validation
error type with entity-path keys; the editable / write-once / server-owned markers; the uniqueness
pre-check; tree cycle validation over in-payload graphs; the operation registry features contribute
to; the collapse of connect into the first business round trip; the capability declaration that
says whether `IsActive` is save-editable.

**Localization.** `ILabelCatalog { Entity(entity, plural), Property(entity, path),
EnumValue(entity, path, value), LanguageSymbol(language) }` resolving in a given language with the
fallback chain; the tenant settings tag for the label cache key; `ICalendarFormatter
{ FormatDate(date, calendar, culture, pattern), TryParseDate(text, calendar, culture, pattern) }`
covering Gregorian, Um Al Qura, Hijri, Ethiopian.

**Permissions.** `IPermissionEvaluator.Evaluate(resource, action)` → allowed + `FilterTree`, used
for `read` on the entity and on every lookup target, and for `save` on the imported entity.

**Blobs.** `IBlobStaging { Open(token) -> StagedBlob (stream, Length, ContentType);
Stage(stream, contentType) -> token; Confirm(token, retention) }`; the blob theme's sweep deletes
confirmed blobs whose retention expired (no Excel-specific schedule).

**Background tasks.** A handler contract `Execute(context, payload, progress)` with the request
context restored from enqueue; `Enqueue(payload, batch)` riding the connect round trip; result
blob attachment; the progress stamp writable inside a caller's transaction; inbox item kinds
`ExcelExportReady`, `ExcelImportCompleted`, `ExcelImportFailed` with per-kind click actions
(download / open task); `TaskId` type.

**Web.** POST endpoints projected from the four operations; `ExportOutcome.Workbook` streamed as
`application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` with `Content-Disposition`
(`filename` ASCII plus `filename*` UTF-8); `ImportException` → 422 problem details with `errors[]`
items `{sheet,row,column,header,path,code,message}`; `ExportRowLimitExceededException` → 422 with
the cap; package limits → 413; background outcomes → 202 with the task id in the body; the
per-request payload limit that bounds `Ids` and `Headers`.

---

## 4. Schema

This theme owns **no database tables**. Its persistent shape is the workbook.

### 4.1 Hidden manifest sheet `_tellma` (both shapes)

Column A = key, columns B… = values. Rows 1–14 are the header block; the columns table follows
after one blank row.

```
Row  A                   B…
1    Tellma.Manifest     1                       -- manifest format version (int)
2    Shape               Editable | Display
3    Entity              Center                  -- logical entity name
4    Distribution        <distribution slug>     -- part of the same-source gate
5    Platform            <Tellma.Core version>
6    SourceTenantId      <int>                   -- part of the same-source gate
7    ExportedAt          <ISO 8601 UTC>
8    Culture             ar-SA
9    Calendar            UmAlQura
10   TimeZone            Asia/Riyadh
11   Languages           en, ar                  -- tenant slots 1..3 in order
12   SchemaFingerprint   <hash>
13   Truncated           false                   -- reserved; always false (exports fail, never truncate)
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
`DateEncoding` ∈ `Serial | Text`. Same-source ⇔ `Distribution` equals the running distribution's
slug and `SourceTenantId` equals the target tenant.

### 4.2 Data sheets

```
<Entity plural>                         -- parent sheet, row 1 = headers (bold, frozen, autofilter)
  A  Id            number (int) / text (long)
  B  Stamp         text, hidden column
  C… editable properties, reference columns, multilingual columns (order in D6)

<Entity plural> · <Collection label>    -- one per child collection
  A  parent reference column        "User / Email"
  B  Id
  C… child properties as above
```

Validation lists: booleans `TRUE,FALSE`; enums as their stored values when the comma-joined list
is ≤ 255 characters; text columns `textLength ≤ MaxLength`. Number formats per D5; column format
`@` on text-typed columns.

### 4.3 Background-task payload (columns requested on the background-task theme's table, not owned here)

```
Kind            nvarchar(64)    'ExcelExport' | 'ExcelImport'
Entity          nvarchar(128)   logical entity name
Request         nvarchar(max)   serialized ExportRequest / ExportForImportRequest / ImportRequest
FileToken       nvarchar(128)   staged input file (import)
ResultBlobId    (blob theme's id type) null   the workbook (export) or the outcome JSON (import)
RowsTotal       int null
RowsProcessed   int null        written inside each chunk's transaction
Phase           nvarchar(32)    Parsing | Resolving | Validating | Saving | Uploading
ChunkIndex      int null        last committed chunk; written inside each chunk's transaction
```

---

## 5. Answers

| Brain-dump question (abridged) | Answer |
|---|---|
| "What is the best way to declare the natural key of each entity? A Core attribute?" | `[NaturalKey(Order?)]` on a property, unique-index-backed, validated at startup (D7). |
| "Should we mandate that every entity has a natural key that is required and unique? … fall back to surrogate keys?" | No mandate. Inference over unique columns; targets without one are referenced by surrogate `Id`, flagged in the manifest, refused across tenants; a startup warning lists them (D7). |
| "Does Excel understand the same numeric/date formatting primitives as Tellma? Does it understand calendars?" | Numbers and dates yes via number-format codes with `[$-CCLLLL]` tags; Hijri and Um Al Qura yes; Ethiopian no — text-date fallback with the calendar and pattern in the manifest (D5). |
| "Export and import of multiple entities … what machinery … Excel still the right medium?" | Non-goal now; a later manifest version sequences entities; Excel remains the medium; zipped JSON/SQLite rejected as not human-editable (D18). |
| "same columns, same rows but without the paging (up to a limit)" | The grid query with `Take = cap + 1`; 100,000 synchronous, 1,048,575 background; over the cap fails with a named error, never truncates (D3). |
| "ExportByIds accepts a list of Ids and a Queryex select" | `ExportRequest.Ids` + `Select`; rows in `OrderBy` or id order; ids via the TVP restriction (D2, D3). |
| "every FK represented by a natural key … system should make a good guess" | Reference columns `<Nav> / <Key>` using the target's default natural key; per-FK override on export and import (D6, D7). |
| "values are exported raw … localization is metadata on the columns, ignored on import" | Encoding by property type with text fallbacks (D4); formats never consulted on import (D5). |
| "FKs … translated in bulk into surrogate keys … not found or ambiguity is a validation error" | One Queryex lookup per (target, key) with a TVP of distinct values under the target's read filter; `ReferenceNotFound` / `ReferenceAmbiguous` per cell (D10). |
| "Import supports tree entities using the surrogate key … even if the parent is new in the same import" | Parents by natural key, resolved in-sheet first; topological order; cycle detection (D11). |
| "Columns are mapped by default using the sheet headers, multi-lingual columns mapped intelligently … overridden … every service supplies the default mapping" | Manifest → header parse → labels in every tenant language → technical path; language slots per column language; `ColumnMappings` override; no per-service mapping code (D8). |
| "Insert / Update / Merge … designate one column as the natural key … partial sheets hydrate" | `Insert / Update / Upsert`; `RowKey` natural key or same-source `Id`; hydration in the resolution round; blank = null; child sheet present = full replacement (D9). |
| "Import is subject to the same access control checks as Save. And unlike save it does not read and return the entities" | Same `save` permission and RLS checks; returns counts, warnings, committed ranges, or a task id (D12, D14, D15). |
| Queryex engine: "restrict results where a column is IN a list … passed as a TVP" | Consumed as `ListRestriction` on `QuerySpec`; lookups and `Ids` exports depend on it (D10, §3.3). |
| Background: "Importing or Exporting enormous Excel files" | Thresholds, staged file, two-pass chunked resumable import, progress phases, result blobs, inbox items (D13, D14). |

---

## 6. Seams

1. **Batch abstraction (data access).** The codec never touches it directly. `ExcelOperations`
   runs the codec's lookups as compiled Queryex queries in one round trip with the connect call,
   the save-filter check and the id reservation riding along, and streams `object?[]` rows back;
   the task progress stamp rides the chunk's persist batch. Needed: several compiled queries per
   batch, TVP parameters bound per statement, `NextResult()` readers, a caller-supplied statement
   inside the persist transaction.
2. **Entity class vs wire shape.** Import produces entity instances in the save shape — child
   collections attached as the pipeline expects, an *absent* collection distinguishable from an
   empty one, server-owned properties left at default for the pipeline to overwrite. The codec
   needs the editable / write-once / server-owned markers on `PropertyMetadata`.
3. **One capability, declared once.** Excel declares nothing; it projects from *queryable* and
   *savable + keyed*, reads the tree capability's parent property, and honours each capability's
   save-editability declaration (an `IsActive` that is changed only through activate/deactivate is
   excluded from the editable sheet). The web theme projects the four operations to routes; the
   securables registry gets no new action (review flag 9.10).
4. **Queryex schema per tenant configuration.** Lookups and exports use the tenant's schema with
   `Name2`/`Name3` gated; a column in a language the tenant lacks is unmappable by construction;
   child entities must be schema roots. The label cache is keyed on the tenant settings tag.
5. **Version tags.** No new tag. Import bumps whatever the save emitter bumps.
6. **Feature composition.** `Tellma.Core.Excel` is a Core feature that `Requires` the stack feature
   and contributes `ExcelOperations<TEntity>` for each eligible entity, plus `IExcelExporter`,
   `IExcelImporter`, `ExcelOptions`, the meter and the background handler.
7. **Natural keys.** Contract in D7: `[NaturalKey]`, unique-index backing validated at startup, the
   inference order, `EntityMetadata.NaturalKeys`, child keys scoped by parent.
8. **Background-task columns and leases.** The payload of §4.3; the handler relies on the runtime
   to renew the lease, restore the captured context and re-run connect; the progress stamp must be
   writable inside the handler's own transaction so chunked work is resumable.
9. **Request context.** `ExcelContext` is built from it: tenant id, distribution slug, tenant
   languages, culture, calendar (carried separately from the culture, never as a `-u-ca-`
   extension), time zone. Exports from a sandbox tenant carry the sandbox's tenant id, so an `Id`
   round trip into the live tenant is refused.
10. **Platform exceptions.** `ImportException : ValidationException` (422);
    `ExportRowLimitExceededException : ValidationException` (422 with the cap); package limits map
    to the payload-too-large family (413).
11. **Permission evaluation.** `read` on the entity for exports; `save` for import; `read` on each
    lookup target inside resolution; the save filter for the pre-check statement.
12. **Blob staging tokens.** Import input; export results; retention confirmations; the blob
    theme's sweep enforces retention.
13. **Wire shapes.** Export takes the query request's clauses verbatim; import errors extend the
    validation error item with `sheet`, `row`, `column`, `header`.
14. **Telemetry.** D16; no tenant tags.
15. **Notification enqueue.** Task-completion inbox items are raised by the task runtime, not the
    codec.
16. **Connect-call collapse.** Assumed: the first business round trip is the export query or the
    resolution batch. Excel-specific failure mode: a background task re-runs connect at start.
17. **Vocabulary.** `Upsert`; "display shape" / "editable shape"; "row key"; "reference key";
    "manifest"; "stamp" (the column header follows the final name of the concurrency token).

---

## 7. Departures from ARCHITECTURE.md

None within the ranges read (Guiding Principles). Display-shape export is the "within-stack
exports" tier of the reports section; nothing here changes the tiers. One pointer needs adding when
the spec lands: the natural-key attribute lives in the entity contract package, not in an Excel
package.

---

## 8. Verification

Relied on from `research/excel.md` (verified 2026-09-01 there): library versions, licenses and
dependency graphs (EPPlus Polyform/commercial with a license key; NPOI maintenance-fee EULA and
SkiaSharp; ClosedXML's SixLabors.Fonts split license and issue #2734; MiniExcel's feature gaps and
2.0 preview status; Open XML SDK 3.5.1 MIT with `OpenXmlWriter`/`OpenXmlReader`); streaming
benchmarks and peak-memory numbers; Excel limits (1,048,576 rows, 31-character sheet names and
forbidden characters, 15 significant digits, 255-character typed validation lists, `textLength`
validation, format strings under 255 characters); the number-format grammar and the `[$-NNCCLLLL]`
tag (triangulated, not on one Microsoft page); Windows calendar ids (Hijri 6, Um Al Qura 23 =
0x17; no Ethiopian); date systems (`date1904`, the 1900 leap-year bug, the 1899-12-30 epoch);
shared-string behaviour (Excel always writes a shared-string table; inline strings are valid);
hidden sheets, defined names and custom XML parts as carriers; `sheetProtection` semantics; Odoo,
Dataverse, Finance & Operations and Data Loader conventions.

Verified in this pass from the repository: `StringList` is a `[TableType]` class with a single
`[Key] [MaxLength(450)] string Id` column (hence the 450-character key cap and the
`SELECT [Id] FROM @tvp` shape); `IdList`/`BigIdList`/`GuidList` carry a single `Id` column;
`QueryexType` has one numeric member (`QxNumeric`) plus `QxBool`, `QxString`, `QxGuid`, `QxDate`,
`QxDateTime`, `QxDateTimeOffset`, `QxHierarchyId` — so a display-shape decimal scale must come from
the property behind a bare path, not from the type (D3); no Excel library is pinned in central
package management yet (Open XML SDK 3.5.1 is a new pin); spec 0008 §13.4 confirms result columns
carry the Queryex type, not a SQL type, so the display encoder reads tolerantly within the type.

Still unverified (each has a mitigation): that Google Sheets and LibreOffice preserve a hidden
sheet and its protection on round trip (mapping degrades to header parsing when the manifest is
absent); that desktop Excel renders `[$-170401]` as Um Al Qura in every locale build (manual test
in the milestone plan; the serial is correct regardless); whether Open XML SDK 3.5.1 writes a
package to a non-seekable stream (moot: the codec spools to a temp file); the exact
`CultureInfo.LCID` for every culture the platform ships (custom cultures report 0x1000 and get no
LCID tag); Excel's rendering of boolean cells in non-English UI languages (display only); that
`dimension` is written by Google Sheets (forward count fallback); that Open XML SDK 3.5.1
prohibits DTD processing by default (the codec sets it explicitly).

---

## 9. Review flags

- **9.1 Library.** Open XML SDK directly (a thousand lines of platform code, full feature coverage,
  MIT). Alternative: MiniExcel 1.46 for the streaming core plus a small Open XML post-pass for the
  manifest sheet, validation lists and protection — ships faster, adds a second library and a
  two-pass write.
- **9.2 Synchronous export cap.** 100,000 rows. Alternative: 50,000 for Azure SQL elastic pools.
- **9.3 `long` keys as text in the editable shape.** Lossless but ugly for users expecting numeric
  ids. Alternative: number below 2^53, text above — a heterogeneous column.
- **9.4 `Id` and `Stamp` in the editable sheet.** Gives same-source round trips exact identity and
  change detection. Alternative: a purer natural-keys-only file, losing bulk edits of the key
  columns themselves and change detection between export and import.
- **9.5 Columns in an unconfigured language.** Ignored with a warning (the value has no home).
  Alternative: a hard `UnmappedColumn` error, forcing the user to remove the column.
- **9.6 Child sheet present ⇒ complete replacement of the listed parents' children.** The sharpest
  edge in the design. Alternative: a per-row `Action` column (`Keep | Delete`) on child sheets.
- **9.7 References resolved under the target's read filter.** Stricter than a JSON save that posts
  a raw id. The pipeline theme should adopt the same rule for foreign-key validation so Excel and
  JSON agree; otherwise Excel stays stricter by design.
- **9.8 Staging-only intake.** One round trip more for tiny synchronous imports. Alternative: a
  multipart convenience endpoint later, without changing the codec.
- **9.9 Chunked background commits.** Changes the all-or-nothing promise for large files, with
  resumability and committed-range reporting. Alternative: `Atomic` by default with a hard cap and
  no chunking. Related knobs: `ImportChunkRows` 10,000 (lock escalation accepted) versus 5,000;
  `MaxInMemorySharedStringBytes` 32 MB versus a larger budget.
- **9.10 No `export` permission action.** Exports use `read`. Alternative: an `export` action in
  the securables registry so admins can forbid bulk extraction to users who may read on screen —
  one line in the permission theme's registry.
- **9.11 Errors workbook.** A non-goal now; the upload echoed with an error column is the most
  usable channel for thousands of errors and could be added later on the same coordinate map.
- **9.12 By-query editable export in one round trip.** Two round trips now (parent, then children
  by parent ids). Alternative: one round trip by re-anchoring the parent filter through the child's
  parent navigation (the weak-entity path rewriting the permission theme needs anyway).

---

## 10. Conflicts

- **Data access (T2).** Must ship the `ListRestriction` amendment on `QuerySpec` with TVP binding by
  path type; put every child entity in the Queryex schema as a root with its parent navigation;
  expose `EntityMetadata`/`PropertyMetadata` with editable, write-once, server-owned, unique,
  scale and enum facts; own `[NaturalKey(Order?)]` with the startup validation of unique-index
  backing (single column on top-level entities, `(ParentKey, Property)` on children); allow the id
  allocator and a caller-supplied statement (the task progress stamp) to ride a batch; name the
  concurrency token, whose wire form becomes the `Stamp` column.
- **Service pipeline (T5).** Must expose a bulk save with `ReturnEntities`, `ExpectedStamps`,
  `OverrideConcurrency`, `Origin`, and absent-collection semantics; an operation registry features
  contribute to; the RLS pre-check either as a batch statement the codec can co-schedule or inside
  the validation round (never a separate round trip); a decision on whether `IsActive` is
  save-editable (recommended: no — changed only through activate/deactivate, hence excluded from
  the editable sheet); and, per flag 9.7, whether foreign-key validation applies the target's read
  filter.
- **Web API (T6).** The 422 error item gains `sheet`, `row`, `column`, `header`; a POST that
  returns a file; 202 for background outcomes; 413 for package limits; the payload limit that
  bounds `Ids` and `Headers`; the MCP sketch's two Excel tools.
- **Localization (T3).** `ILabelCatalog` with entity/property/enum labels and language symbols in
  a given language with the fallback chain; `ICalendarFormatter` with Ethiopian parsing; the
  tenant settings tag as the label cache key; the `Name (E)` / `Name (ع)` symbol convention.
- **Permissions (T4).** Evaluation returning the `FilterTree` for `read` on arbitrary lookup
  targets and for `save` on the imported entity; optionally the `export` action (flag 9.10).
- **Blobs (T7).** Staging tokens readable by the codec; confirm-with-retention and a sweep that
  deletes expired confirmed blobs; the result-file download endpoint with etag validation.
- **Background tasks and inbox (T10).** A handler contract with restored request context and
  connect re-run; a progress stamp writable inside the handler's own transaction (resumable chunked
  work); inbox kinds `ExcelExportReady`, `ExcelImportCompleted`, `ExcelImportFailed` that cannot be
  muted; the `TaskId` type; the poison policy applied to a task that keeps failing pass 1.
- **Host (T1).** The distribution slug in the request context (part of the same-source gate); the
  `Tellma:Excel` configuration section; the scratch directory for spooled files; the aggregated
  startup validation that reports natural-key violations and the no-natural-key warning.

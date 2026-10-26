# F05 — Blobs (0017), Excel codec (0019), Background jobs and scheduler (0020)

Digest for the round-2 design pass. Facts are cited by spec and section ("0017 §5.3"). All three
specs are round-1 (designed, not implemented) and may be changed in place.

## 1. Sources read

| Source | Lines |
|---|---|
| `docs/specs/0017-blob-storage.md` | 1307 |
| `docs/specs/0019-excel-codec.md` | 1848 |
| `docs/specs/0020-background-jobs-and-scheduler.md` | 1963 |
| (context) `.tmp/round-2/README.md`, `.tmp/round-2-brief.md` | 92, 462 |

## 2. Key facts

### 2A. Blobs (spec 0017)

#### Shape of the pattern (0017 Context, §1.2, §4.1)

- Upload first, staged; then the record is saved through the ordinary JSON save with the staged
  blob's `int` id in an `int?` column; the save transaction confirms the blob. Nothing
  non-transactional happens inside a save; physical deletion is the sweep's alone.
- One table `core.Blobs` holds every intrinsic fact; records hold only the FK.
- Cost (§1.2): upload = 1 DB round trip + 1–2 create-only store writes (primary, thumbnail) in
  parallel; save that attaches/replaces/removes = +0 round trips over a plain save; `GET` cache
  miss = 1 round trip (two statements) + 1 storage read; `GET` with matching `If-None-Match` = 1
  round trip, 0 storage.

#### Endpoints (0017 §8.1, §5.3)

Both mapped by `MapTellmaBlobs` on `TellmaEndpoints.Blobs`, cookie policy, `AllowMember` (any
connected active member; finer checks in the service). `/{tenantId}/blobs` is a reserved second
segment (see f04).

| Route | Verb | Request | Response |
|---|---|---|---|
| `/{tenantId}/blobs/{kind}?fileName=` | POST | raw body (not multipart); `Content-Type` = declared type; `Content-Length` required; `fileName` ≤ 255 chars; `Tellma-Client` header required (CSRF control); `WithMutation(true)`; body cap raised to `BlobOptions.MaxUploadSize` for this endpoint only | `201 Created`, body `BlobDescriptor`, `Location: /{tenantId}/blobs/{kind}/{id}` |
| `/{tenantId:int:min(1)}/blobs/{kind}/{id:int}?variant=&download=` | GET | `If-None-Match`, `Range`, `If-Range` honoured; exempt from `Tellma-Client` (so `<img src>` works); rate policy `tellma-blobs` (spec 0016 §8.2) | `200` stream, `206`, `304`, `412`, `416` |

- `BlobDescriptor(int Id, string Kind, string ContentType, long Size, int? Width, int? Height,
  string? FileName, DateTimeOffset? ExpiresAt)` — camel-cased on the wire; `ExpiresAt` is the
  staging deadline (§3.1).
- Upload is deliberately single-file — "the one named exception" to the bulk-shaped API; parallel
  single-file requests for many files; errors and retries per file (§8.1). Not an `[ApiAction]`,
  not in any stack descriptor; MCP reaches it later via reserved `tellma_upload`.
- Tenant states: upload on a `ReadOnly` tenant → 403 `tenant-read-only`; downloads succeed on
  `ReadOnly` (§5.3, §8.1).
- **There is no endpoint to delete/cancel a staged blob** and no metadata-only `GET`; abandoned
  uploads simply expire into the sweep (§3.2, §4.6).

#### Download semantics (0017 §5.1–§5.3)

- URL names only kind and id → immutable by construction; a re-upload changes the record's id
  and so the URL (Decision 12).
- `ETag`: `"{id}"` for the primary, `"{id}-{variant}"` for a variant; strong; computed before any
  storage call; `If-None-Match` match → 304 with `ETag` and `Cache-Control`, storage untouched.
- `Cache-Control: private, max-age=31536000, immutable` for a `Committed` blob (never `public`);
  `Cache-Control: private, no-store` for a `Staged` blob.
- A `Staged` blob is served **to its uploader only, with no owner row** — the details page can
  preview a picked image before saving (§5.1 step 4).
- `Content-Disposition: inline; filename*=UTF-8''…` when the served type is one of the four image
  types, `application/pdf`, `text/plain`, `text/csv`, `application/json` and `download` ≠ `true`;
  `attachment` otherwise (OOXML, zip, or `download=true`). `download` binds as boolean, absent =
  false.
- `Content-Security-Policy: frame-ancestors 'self'` and `X-Content-Type-Options: nosniff` on
  every response → the SPA may embed a preview in `<iframe>`/`<object>`; no other origin can.
- Range: `Accept-Ranges: bytes`; 206 / 416; `If-Range` against the strong ETag. PDF viewers fetch
  page 1 of linearised PDFs by ranges; large downloads resume.
- Authorisation: "if you can read the owner you can read its blobs", evaluated on every cache miss,
  fail-closed. `OwnerRead` → owner resource's `Read` grant with its row filter (via `OwnerPath`
  for child kinds); `AnyMember` → any connected active member. Missing, hidden, released,
  wrong-kind, unlisted variant → indistinguishable 404 `not-found`. Bad `variant` (grammar
  `^[a-z0-9]{1,16}$`) → 400 `bad-request`.
- An unlisted variant is 404, never the primary (Review flag 24) — the client must read
  `Variants` before asking for `thumb`.
- 50 avatars = 50 one-round-trip `GET`s cold, 0 afterwards; over the tenant's `tellma-blobs`
  concurrency they queue, not fail. No output caching, no compression (§5.3).

#### Variants and thumbnails (0017 §2.1, §2.4, §3.2, §3.6)

- `Variants nvarchar(100)`: comma-separated existing variant names; `''` for none; **`thumb` is
  the only variant today**.
- Thumbnail rendered at upload (never later). `thumb` content type = the kind's
  `Thumbnail.Format` (WebP by default).
- Kinds with `Image` policy: thumbnail from the normalised image (`CoverSquare` → square,
  `Contain` → keeps aspect ratio). Kinds with `Thumbnail` but no `Image` (attachments): four image
  types decoded under the pixel cap, PDF page 1 rendered via PDFtoImage/PDFium (process-wide lock),
  anything else (OOXML, zip, encrypted PDF, over cap, render > `ThumbnailTimeout` 5 s) → no
  thumbnail; never a rejection.
- Without an `IImageProcessor`, a `Thumbnail`-only kind stores no thumbnail; a kind with `Image`
  then fails startup.

#### Kinds, presets, policies (0017 §1.3, §2.3, §2.4)

| Preset | `MaxSize` | Allowed types | `Image` (stored primary) | `Thumbnail` |
|---|---|---|---|---|
| `Attachment` | 100 MiB | `application/pdf`; docx/xlsx/pptx OOXML; `image/jpeg`, `image/png`, `image/webp`, `image/gif`; `text/plain`, `text/csv`, `application/json`; `application/zip` | none | 256 px, WebP |
| `Avatar` | 10 MiB input | the four image types | `MaxDimension 512`, `CoverSquare`, WebP | 96 px, WebP |
| `Photo` | 10 MiB input | the four image types | `MaxDimension 1600`, `Contain`, WebP | 256 px, WebP |
| `Signature` | 2 MiB input | the four image types | `MaxDimension 400`, `Contain`, **Png**, alpha preserved | none |

Core kinds (§1.3, §5.2):

| Kind | Owner column | Preset | Read access |
|---|---|---|---|
| `user-image` | `User.ImageId` (spec 0014) | `Avatar` | `AnyMember` |
| `user-signature` | `User.SignatureId` (spec 0014; narrowed to 1 MiB there via `MaxSizeBytes = 1_048_576`) | `Signature` | `AnyMember` |
| `export-file` | `Export.FileId` (spec 0019) | `Attachment` | `OwnerRead` |
| `import-file` | `Import.FileId` (spec 0019) | `Attachment` | `OwnerRead` |
| `email-attachment` | `OutgoingEmailAttachment.FileId` (spec 0022 §2.1) | `Attachment`, thumbnail turned off by `CoreFeature` | `OwnerRead` via `core.OutgoingEmail` (admins only by default) |

- `[BlobReference(string Kind, BlobPreset Preset = Attachment, long MaxSizeBytes = 0,
  BlobReadAccess ReadAccess = OwnerRead)]` on an `int?` property of a stack root or child entity
  at any depth; exactly one property per kind; kind grammar `^[a-z][a-z0-9-]{1,39}$` (§2.3).
- The attribute derives: column `int NULL` + FK NO ACTION + filtered unique index (one owner row
  per blob), Queryex navigation, `EntityMetadata.BlobReferences` (spec 0012's
  `BlobReferenceMetadata(Property, Kind, Policy)`), kind registration, validator + effect,
  download authorisation, Excel exclusion. Ownership `Editable`. **No permission action added**:
  upload needs membership, attach needs owner `Save`, download needs owner `Read` (§2.3).
- Policy overrides: `BlobsBuilder.Kind(kind, p => p with {…})` (host) and
  `FeatureContribution.BlobKind(kind, configure)` (pack) (§1.3).
- Image processing (§3.6): server always re-encodes (security control); EXIF orientation applied,
  metadata stripped, animated GIF → frame 0. "The browser crops for humans, `CoverSquare` for
  everything else"; no fit/crop metadata is stored and no original is kept for re-cropping
  (Review flag 9) → any interactive crop happens client-side **before** upload.

#### Queryex / read shape (0017 §2.2)

- `Blob` entity (`core.Blob`) is **not a stack**: no securable, no endpoints, no service; reachable
  only through each owner's navigation, named property minus `Id` (`ImageId` → `Image`,
  `FileId` → `File`).
- Queryex properties: `Id, Kind, State, ContentType, FileName, Size, Width, Height, Variants,
  CreatedAt, CreatedById, CommittedAt, ExpiresAt`; `StorageKey` and `Sha256` never in the schema.
- `[RelatedSelect("Id,Kind,ContentType,FileName,Size,Width,Height,Variants")]` (never
  `CreatedById`) — a details read returns file name and size beside the owner; a tile knows from
  `Variants` whether to request `thumb`.

#### Limits and options (0017 §1.3, §3.5)

`BlobOptions` bound from `Tellma:Blobs`: `StagingTtl` 24 h; `SweepBatchSize` 500;
`SweepReclaimAfter` 60 min; `MaxStagedPerUser` 200; `MaxStagedBytesPerUser` 512 MiB;
`MaxUploadSize` 100 MiB (must be ≥ largest kind `MaxSize`); `ThumbnailTimeout` 5 s. Per-kind
`StagingTtl` override possible. File name sanitised (trim, ≤ 255, separators/control chars → `_`,
`.`/`..` → `file`), display-only. Image input pixel cap `MaxInputPixels` 50,000,000.

#### Stage flow and content types (0017 §3.2, §3.3, §3.4)

- Order: resolve kind → check length (`min(policy.MaxSize, MaxUploadSize)`; enforced again while
  reading) → read bytes (images buffered; attachments > 64 KiB to a temp file) → determine
  content type → process image / render thumbnail → random 32-hex `StorageKey` → one DB round trip
  (soft quota + insert `Staged` row, id from `core.sq_Blobs` inside the statement) → create-only
  writes → return descriptor. Row before bytes; a store failure is a 500 and the id is never seen.
- Content type is **server-determined**: sniffed JPEG/PNG/GIF/WebP/PDF win over the declaration;
  `PK` archive takes the declared type if OOXML or zip; text types take the declaration only when
  allowed and no NUL in the first 8 KiB; executables, HTML and SVG are **always rejected**.
- Quota is soft (write-skew possible); a `System` scope skips the quota (§3.4).

#### Attach in a save (0017 §4.1–§4.4)

- Attach rule: a newly attached value (non-null on insert, changed non-null on update; same for
  child rows) must be a blob that is `Staged`, unexpired, of the property's kind, staged **by the
  saving user**, and used at most once in the whole payload. Unchanged → untouched; `null` on
  update releases; deleted row / removed child row releases. "The token *is* the staged id."
- Holds on every save path: details page save, `me/save` (spec 0018), MCP, enlisted job saves,
  import (blob columns are excluded from sheets, so nothing attaches).
- Validation codes (members of spec 0015 `ValidationCodes`), at the property path:
  - `Blob.DuplicateReference` — same id newly attached more than once.
  - `Blob.NotAttachable` with `reason` ∈ `missing`, `not-staged`, `kind`, `uploader`, `expired`
    (first failing in that order); and `reason = persist` at `ValidationPath.Root` when the
    in-transaction re-check throws (a concurrent save won, or it expired between validation and
    persist) or at the property path when the unique index refuses a second owner. "The client
    re-uploads."
- Effect statements run inside the persist transaction for every persist on the table (save,
  delete by ids/query/with descendants, activate/deactivate, actions): release dereferenced
  (`Committed` → `Released`), confirm newly attached (`Staged` → `Committed`, `ExpiresAt NULL`).
- Temporal owners (`core.Users`): history keeps the old id; old blob released and swept; history
  reference dangles. Edit history shows *that* an image changed, not the old bytes (§4.5).
- Server-generated blobs (§4.6): job stages under its run-as user, enlists the owner-row insert
  in the job frame, confirmed in the completion transaction. Download `GET blobs/export-file/{id}`
  authorised by the `Exports` row. A synchronous import leaves its `import-file` upload **staged**;
  the sweep reclaims it after the TTL.

#### State machine, sweep, reconcile, schedules (0017 §2.1, §7)

- `BlobState { Staged, Committed, Released, Deleting }`; `ExpiresAt` means staging deadline /
  release time / reclaim time by state, `NULL` while `Committed`.
- `core.blob-sweep` (`BatchSize 1, LeaseSeconds 600, MaxAttempts 3, MaxConcurrency 1`) every
  15 min; progress via `IJobProgress.Report` message `Blob.SweepProgress` args `{ claimed,
  deleted }`. Erasure latency ≤ one sweep interval (15 min).
- `core.blob-reconcile` (`LeaseSeconds 900`) Saturday 02:00 tenant zone; releases committed blobs
  with no owner row older than 1 day.
- Built-in schedules: `BuiltInSchedule(4, "core.blob-sweep", "*/15 * * * *")`,
  `BuiltInSchedule(5, "core.blob-reconcile", "0 2 * * 6")`; `Skip` / `Coalesce`, system user,
  editable only `Name*`, `CronExpression`, `TimeZoneId`; the sweep cannot be deactivated (§7.4).
- Operator tool: `TellmaMigrator blobs verify --tenant <id> [--delete-orphans]` (§7.3).

#### Errors (0017 §8.2)

`BlobRejectedException(Code, Arguments)` → problem `code` `blob-rejected`, one `errors` item at
path `body`:

| Code | Status | Arguments |
|---|---|---|
| `Blob.UnknownKind` | 404 | — |
| `Blob.LengthRequired` | 411 | — |
| `Blob.TooLarge` | 413 | `maxBytes` |
| `Blob.UnsupportedType` | 415 | `contentType` |
| `Blob.StagingQuotaExceeded` | 422 | `maxCount`, `maxBytes` |
| `Blob.ImageRejected` | 422 | `reason` ∈ `format`, `too-many-pixels`, `decode-failed` |

Store failure → 500 `internal` with trace id. A non-member upload → 404 `tenant-not-found`;
missing `Tellma-Client` → 403 `csrf-rejected`; oversize → 413 before the body is read (§11).

#### Observability (0017 §10)

Meters `Tellma.Core` and `Tellma.Blobs` (store). Counters/histograms `tellma.blobs.uploads`
(`outcome` ∈ `staged`, `rejected_size`, `rejected_type`, `rejected_image`, `quota`, `failed`),
`.upload.bytes`, `.upload.duration`, `.image.duration`, `.image.failures`, `.attach.rejected`,
`.downloads` (`served`, `not_modified`, `not_found`), `.store.*`, `.sweep.*`,
`.reconcile.released`. Server-side only; nothing client-side.

### 2B. Excel codec (spec 0019)

#### Two shapes (0019 Context, §1.3)

- **Display shape** (`ExportKind.Display`): "the grid as a sheet: the same columns, the same rows,
  the same filter and ordering the user sees on the search page, without paging". One data sheet +
  manifest. Not for round trips.
- **Editable shape** (`ExportKind.ForImport`): the entity as rectangular tables that round-trip —
  every editable property, every child collection at every depth on its own sheet, linked to its
  owner by `Id`; FKs expressed as natural keys; hidden `_tellma` manifest sheet.
- Vocabulary: parent sheet, child sheet, owner sheet, collection path (`RoleMemberships`,
  `WidgetParts.WidgetPartNotes`), link, row key, reference key, manifest, stamp (header `Stamp`,
  value = `ModifiedAt` wire string `yyyy-MM-ddTHH:mm:ss.fffffffzzz`), same-source, lookup, chunk.

#### The seven `[ApiAction]`s (0019 §1.2, §2.1, §2.2)

Projected by spec 0016 to `POST /{tenantId}/api/web/{resource-segment}/{method}`; contributed by
the stack companion `ExcelOperations<TEntity>` on every stack whose `Operations` include `Export`
(the display export) or `Import` (the other three) — zero distribution lines. No new securable.

| Method (route tail) | Stack op | Securable | Mutation / Idempotent | Request → Response |
|---|---|---|---|---|
| `export` | `Export` | `Read` | false / true | `ExportRequest` → file stream (`ExportOutcome(Stream Workbook, string FileName, int Rows)`) |
| `export/start` | `Export` | `Read` | true / false | `ExportRequest` → 202 `JobAccepted(JobId, null)` |
| `export-for-import` | `Import` | `Read` | false / true | `ExportForImportRequest` → file stream |
| `export-for-import/start` | `Import` | `Read` | true / false | `ExportForImportRequest` → 202 `JobAccepted(JobId, null)` |
| `inspect-import` | `Import` | `Save` | false / true | `InspectImportRequest` → `ImportPlan` |
| `import` | `Import` | `Save` | (mutation) | `ImportRequest` → `ImportOutcome` |
| `import/start` | `Import` | `Save` | true / false | `StartImportRequest` → 202 `JobAccepted(JobId, ImportId)` |

- **There is no separate "import template" operation.** The template is `export-for-import` with
  `Ids = []` (empty list): headers, validation lists, manifest, no rows. An empty `Ids` on the
  display `export` is `BadRequestException` (§2.1).
- Note `export-for-import` exists under the `Import` op (not `Export`); a stack with `Import` but
  not `Export` offers "export for import" and the template but no grid export (§1.2, §2.2).
- Synchronous `export`, `export-for-import`, `inspect-import`, `import` run under the **long
  request timeout** and per-user export/import concurrency limits of `TellmaApiOptions` (spec
  0016); the three `/start` methods run under the ordinary web timeout (§2.2).

#### Request shapes (0019 §2.1)

```
ExportSourceRequest   { Ids?: long[]; Filter?; OrderBy?; Search?; Arguments?: {string: JsonElement};
                        IncludeInactive = false }          // Ids XOR clauses
ExportRequest         : ExportSourceRequest { Select (required, "exactly as the grid uses it");
                        Headers?: string[] (parallel to select items, each ≤ 255; derived when null) }
ExportForImportRequest: ExportSourceRequest { Columns?: string[]; ReferenceKeys: {FK -> target prop};
                        Children?: ExportCollection[] }    // Children null = all at every depth; [] = none
ExportCollection(Collection, Columns?, ReferenceKeys?, Children?)
InspectImportRequest  { FileId (staged import-file blob); SheetMappings[]; ColumnMappings[] }
ImportRequest         { FileId; Mode = Insert; RowKey?; SheetMappings[]; ColumnMappings[];
                        IgnoreUnmappedColumns = false; IgnoreSheetStamps = false }
StartImportRequest    : ImportRequest { Atomic = false }
ImportColumnMapping(Sheet, Column /*letter A..XFD*/, Path? /*null = ignore*/)
ImportSheetMapping(Sheet, Collection? /*"" = parent, path, null = ignore*/)
```

- `Ids` with `Filter`, `Search`, `Arguments` or `IncludeInactive = true` → `BadRequestException`;
  `Ids` ≤ `StackLimits.MaxIds`; ids keep `OrderBy` order, else `Id` order. A by-ids source applies
  no activatable conjunct. No clauses = the access filter alone, `Id` order (§2.1).
- `IncludeInactive` mirrors the grid's query: `false` joins the activatable conjunct (§2.1).
- `ExportForImportRequest.Columns`: subset of parent's **editable** paths; `Id`, `Stamp` and the
  default natural key column always kept; non-editable path → `BadRequestException`.
  `ReferenceKeys` overrides the target's default natural key with any scalar of the target.
- **Column mapping addresses columns by letter, never header text** (§2.1).
- `ImportMode { Insert, Update, Upsert }`; `ExportKind { Display, ForImport }`.

#### How display-export columns are chosen (0019 §4.1, §3.1, §3.3) — the brief's open question

- `Export` runs **exactly the query `query` would run** for the same request (same `Select`,
  `Filter`, `OrderBy`, `Search`, `Arguments`, `IncludeInactive`, same access composition),
  `Skip = 0`, **no ancestors**, `Take = cap + 1`.
- "**One column per `QueryexColumn` in select order**" — every select item becomes one Excel column.
  So sending the grid's select verbatim exports `Id` and every unpacked navigation term
  (`Center.Id`, `Center.Name`, `Center.Name2`, `Center.Name3`) as separate columns.
- Headers: `request.Headers[i]` when supplied, else derived — the localized property-label chain
  for a column with a `Path` (`Center / Code`, `Name (E)`), the select item's source text for a
  computed expression. Duplicate labels within a sheet get the technical path in parentheses
  (`Date (PostingDate)`).
- Enum cells render the localized label (`ILabelProvider.EnumValueLabel`, request culture);
  `bool` → boolean cell; `DateTimeOffset` → converted to request display zone; dates as serials
  with a `[$-CCLLLL]` locale/calendar number format (`[$-170401]dd/mm/yyyy` for ar-SA + Um Al
  Qura), Ethiopian (`et`) as text; `long` as number (precision loss accepted in display);
  `[BlobReference]` columns as the blob id number; JSON as text; `byte[]` blank; audit columns,
  `JobId`, derived, computed exported when selected (§3.1, §3.2).
- **Nothing in the display shape picks a multilingual slot for the user's language**: a
  `Center.Name` select item is slot 1. There is no "display name"/localized-name column concept
  in the export request; the client would have to send the slot path it wants (§4.1; see §3/§5).
- RTL sheet view when the request culture is RTL; header row bold, frozen, autofiltered (§3.3).
- Tree entities: the display export passes "no ancestors" — a tree grid exports as flat rows,
  no hierarchy indicator (§4.1).
- File name: `<Entity plural, ASCII-safe> <yyyy-MM-dd HHmm>.xlsx` in the request display zone;
  spec 0016 adds `filename*` UTF-8 with the unscrubbed label (§4.3).

#### Editable-shape columns (0019 §5.1, §5.2)

- Parent sheet: `Id` (number for `int` keys, text for `long`), `Stamp` (hidden column), then
  every editable scalar (`Editable` + `WriteOnce`; never `Derived`, `DatabaseOwned`), excluding
  `[ExcludeFromExcel]`, `[BlobReference]`, `[JsonColumn]`, `byte[]`, `hierarchyid`; each FK
  replaced by one reference column `<Navigation label> / <Key label>` (`Center / Code`); each
  `[Multilingual]` group expanded per tenant language (`Name (E)`, `Name (ع)`).
- `IsActive` is server-owned: absent from the editable shape; imported rows are created active
  (§9.1).
- Child sheets: one per selected collection path, named `Widgets · Widget Parts · Widget Part
  Notes` (31-char limit, leading segments dropped); columns: parent reference
  `<Owner navigation label> / Id`, own `Id`, editable properties and references.
- Reference key default = target's natural key (spec 0012 `EntityMetadata.NaturalKey`); no key →
  surrogate `Center / Id`, manifest `KeyKind = Surrogate`. Core declares `core.User.Email`,
  `core.Role.Code`, `gl.Center.Code` (§6.1).
- Manifest `_tellma` (hidden, protected without password): `Tellma.Manifest` version 1, `Shape`,
  `Entity`, `Deployment`, `Platform`, `SourceTenantId`, `ExportedAt`, `Culture`, `Calendar`,
  `TimeZone`, `Languages`, `SchemaFingerprint`, `Truncated` (always false), then a column table
  (`Sheet | Collection | Column | Header | Path | Type | Role | Language | ReferenceEntity |
  KeyKind | DateEncoding | DatePattern`) (§5.3).
- Same-source gate: manifest `Deployment` = running deployment and `SourceTenantId` = target
  tenant; only then may `Id` be row identity and `Stamp` an expected stamp (§5.4).

#### Sync vs background (0019 §2.3, §4.2, §14.1)

| Operation | Sync when | Otherwise |
|---|---|---|
| `export` | rows ≤ `MaxSynchronousExportRows` (100,000) | **fails** (never truncates, never promotes) with `LimitExceededException("Excel.Export.RowLimitExceeded", rows, cap)` → 413; message names `export/start` |
| `export-for-import` | parent rows ≤ 100,000; each sheet ≤ `MaxExportRows` | same; message names `export-for-import/start` |
| `/start` exports | never | job fails a sheet above `MaxExportRows` (1,048,575) |
| `import` | `TotalRows ≤ MaxSynchronousImportRows` (10,000) over every mapped sheet, fits one save (parents ≤ min(`MaxSaveCount`, `MaxIds`), `TotalRows ≤ MaxRowsPerSave`), file ≤ `MaxSynchronousImportFileBytes` (16 MB), uncompressed ≤ 512 MiB | `ImportException` with one error `Excel.Import.TooLargeForSynchronous` (`rows`, `maxRows`, `bytes`, `maxBytes`), message names `import/start` |
| `import/start` | never | `Atomic = true` above `MaxAtomicImportRows` (100,000) etc. → job fails with `Excel.Import.TooLargeForAtomic` in pass 1 |

- "A synchronous export or import never promotes itself to background: the response shape would
  change mid-request" (§2.3). **The client must choose** `export` vs `export/start` (e.g. from the
  grid's count) and must handle 413 by offering `/start`.
- By-query export uses `CountCap = cap`: the 413 fires at the first page before any row is
  spooled (one round trip, no temp file) (§4.1, §15).
- `ImportPlan.RequiresBackground` and `ImportPlan.AtomicAllowed` tell the client which import path
  and whether to offer `Atomic` (§2.1).

#### Import flow and plan (0019 §7.1, §8.1, §2.1)

1. Upload: `POST /{tenantId}/blobs/import-file?fileName=` → `BlobDescriptor`; `FileId` = its id.
   A `FileId` that is not the caller's own staged, unexpired upload →
   `NotFoundException("core.Blob", [FileId])` (§7.1).
2. `inspect-import` (1 round trip) → `ImportPlan(Entity?, ManifestVersion, ManifestPresent,
   SameSource, SchemaFingerprintMatches, Sheets[], RowKeyCandidates[], DefaultRowKey?, TotalRows,
   RequiresBackground, AtomicAllowed, Warnings[])`.
   - `ImportPlanSheet(Sheet, Collection?, Status, Rows, Columns[])`, `ImportSheetStatus { Parent,
     Child, Manifest, Ignored, Duplicate }`.
   - `ImportPlanColumn(Column /*letter*/, Header, Path?, Status, Language?, Suggestions[])`,
     `ImportColumnStatus { Mapped, Unmapped, Ignored, Duplicate, ServerOwned }`; up to three
     Damerau-Levenshtein suggestions for unmapped headers.
   - `RowKeyCandidates`: `Id` first when same-source and mapped, then mapped natural keys.
   - The plan is **not stored**; the client echoes overrides on `import` (mapping re-runs).
     Every plan column echoed as an override is a fixed point (§8.3, §15).
3. `import` (sync) or `import/start` (background) with the same mappings.

- Mapping order per header: manifest column table → generated header index in the request
  culture, each tenant language and English (labels, twin symbols, reference headers, technical
  paths, `<Label> (<Path>)`) → `ColumnMappings` override (wins). Unmapped columns are **errors**
  unless `IgnoreUnmappedColumns` (§8.1).
- Language-aware: a column's language maps to whichever slot holds that language in the target
  tenant; a column in an unconfigured language is ignored with warning
  `Excel.Import.UnconfiguredLanguageColumn` (§8.2).
- Modes (§9.1): `Insert` (every row new; `Id`/`Stamp` only link, warned `IdentityColumnIgnored`
  `reason = InsertMode`), `Update` (every row must resolve via row key; miss = `RowNotFound`),
  `Upsert`. Row key default `Id` on a same-source file, else first mapped natural key (§9.2).
- Partial sheets: hydration via `GetByIdsAsync` and overlay; blank cell = null (no sentinel);
  to leave a column untouched, remove it or map it to null (§9.3, §9.4).
- Children (§9.5): present child sheet = complete children of every owner row present (owner row
  with no rows → children deleted); absent sheet = untouched. Replacement requires every editable
  column and every descendant sheet, else `Excel.Import.IncompleteChildSheet`.
- Self-references/trees: Kahn ordering, `Excel.Import.ParentCycle`; temporary negative ids per
  chunk (§9.6).
- References resolved under the target's read filter; hidden = `ReferenceNotFound` (never an
  existence oracle) (§10, §13).
- Concurrency: every hydrated row saved under `Check`; hydration-window conflicts retried up to
  `ImportConcurrencyRetries` (2); sheet-stamp conflicts reported at once as
  `Excel.Import.ConcurrencyConflict` (`modifiedAt`, `modifiedBy`) (§11.2).

#### Import results and errors (0019 §2.1, §11.3, Appendix A)

- `ImportOutcome(Inserted, Updated, ChildrenInserted, ChildrenUpdated, ChildrenDeleted,
  CommittedRowRanges: (From, To)[], Warnings: ImportError[])`; warnings only
  `UnconfiguredLanguageColumn`, `IdentityColumnIgnored`.
- `ImportError(Sheet?, Row?, Column?, Header?, Property?, Code, Arguments) : CodedError`.
- `ImportException(Errors, TotalErrors, CommittedRowRanges)` → **422** (spec 0016 §7.1) with
  `sheet`, `row`, `column`, `header`, `property` on each item and `totalErrors`,
  `committedRowRanges` beside them. `Errors` capped at `MaxReportedErrors` (100) in sheet, row,
  column order. Also thrown by `inspect-import` for a mapping naming a missing sheet/column/path.
- Pipeline errors pass through the coordinate map with their own codes (`Required`, `MaxLength`,
  `Unique`, `Fk.NotFound`, `Fk.InUse`, `Tree.Cycle`, `Tree.TooDeep`, `WriteOnce`, `Precision`,
  `Import.LanguageNotConfigured`, `Entity.NotFound`, distribution codes); a whole-payload error →
  sheet null, row null.
- A synchronous import is all-or-nothing; `ForbiddenException` passes through as **403** (names
  no rows) (§11.3, §13).
- `BadRequestException` (400) before the blob is read for request faults: duplicate mappings,
  bad letter, bad collection, bad `RowKey` (§8.1).
- Excel error codes (Appendix A): `Excel.Export.RowLimitExceeded`, `Excel.Import.{TooLargeForSynchronous,
  TooLargeForAtomic, PackageTooLarge, TooManySharedStrings, MalformedWorkbook, UnknownSheet,
  DuplicateSheet, OwnerSheetAbsent, UnmappedColumn, UnknownColumn, UnknownPath, DuplicateColumn,
  ServerOwnedColumn, UnconfiguredLanguageColumn, IdentityColumnIgnored, WriteOnceChanged,
  RowNotFound, DuplicateRowKey, IdKeyRequiresSameTenant, SurrogateReferenceFromOtherTenant,
  ReferenceNotFound, ReferenceAmbiguous, KeyTooLong, OrphanChildRow, IncompleteChildSheet,
  ParentCycle, TooManyChildren, TooManyDescendants, InvalidCell, ConcurrencyConflict,
  ResumeMismatch, Failed}`.
- **Deferred**: an errors workbook echoing the upload with an error column; partial success in a
  sync import; CSV; lookup dropdowns; multi-entity workbooks; composite natural keys; per-row
  child `Action` columns; MCP `tellma_export`/`tellma_import` (Context, Non-goals).

#### `Export` and `Import` entities — "My exports" / imports page (0019 §12.1)

- `core.Exports` (`Export : TopLevelEntity, IJobEntity`, resource `core.Export`): `Id`,
  `Resource varchar(128)`, `Kind varchar(9)` (`Display`/`ForImport`), `RequestJson` (≤ 64 KB),
  `FileName nvarchar(255)`, `FileId int?` (`export-file` blob), `RowCount int?`, `ExpiresAt`,
  `JobId int?` (FK `ON DELETE SET NULL`, unique), audit set; `IX_Exports_CreatedBy (CreatedById,
  CreatedAt DESC)`.
- `core.Imports` (`Import`, resource `core.Import`): `Id`, `Resource`, `Mode varchar(8)`,
  `RequestJson`, `FileId` (`import-file` — the uploaded workbook), `ResultJson` (the
  `ImportOutcome` when `ErrorCount = 0`, else `errors`, `totalErrors`, `committedRowRanges`;
  ~300 B/error), `RowCount`, `ErrorCount`, `ExpiresAt`, `JobId`, audit set.
- `[Stack(Operations = Query | Details | Delete | Enlist)]` → projected `query`, `get`,
  `get-by-ids`, `delete`, `delete-by-query`; securables `core.Export`/`core.Import` ×
  `Read | Delete`; **no `Save` securable, no `save` endpoint**.
- `[DefaultSelect]`: Export → `Id, Resource, Kind, FileName, FileId, RowCount, ExpiresAt, JobId`
  + 4 audit; Import → `Id, Resource, Mode, FileId, RowCount, ErrorCount, ExpiresAt, JobId` +
  audit. `RequestJson`/`ResultJson` only when the details page selects them.
- Queryex navigations: `File`, `Job`, `CreatedBy`, `ModifiedBy`.
- Self-scope `ExportAccessCriteria`/`ImportAccessCriteria`: `CreatedById = me()` for `Read` and
  `Delete`, reason `self` → every member sees and deletes their own ("My exports"); an
  administrator with a stored grant sees all. Deleting a row releases its blob.
- A row exists only when it has a file to own: `Import` row at `import/start`; `Export` row at the
  background run's completion. **A synchronous export creates no row** and leaves nothing behind.
- Retention 7 days (`ExportFileRetentionDays`, `ImportFileRetentionDays`), by spec 0020's daily
  `core.file-retention`.

#### Background paths (0019 §12.2–§12.6)

- `export/start`, `export-for-import/start`: enqueue `core.export` (no entity), `RunAsUserId` =
  `RequestedById` = caller → `JobAccepted(JobId, null)`. Arguments carry the typed request and
  `ExcelJobCulture(Culture, Calendar, TimeZone, Language)` so the job renders like the request.
- `import/start`: inserts the `Import` row (attaching the staged upload) and enqueues `core.import`
  in one persist batch → `JobAccepted(JobId, ImportId)`.
- **Client follow-up as specified** (§12.2): follow the job through `jobs/query` (self-scope) and
  the hub's `job.changed`; cancel a job it requested (spec 0020 §11.2); find a finished export's row
  through `exports/query` on `JobId` or the `core.export.ready` notification's `TargetId`; a
  finished import's result in `ResultJson` (`imports/get`); either file via the blob `GET`.
- `core.export` handler: `BatchSize 1, LeaseSeconds 600, MaxAttempts 3, Schedulable = true` — a
  user schedule may fire an export (§12.3). Progress after each page: `percent = min(99, 100 ×
  rows / total)` with message `Excel.Export.Progress` `{ rows }`; never reports 100. Denied `Read`
  at run time → `Fail(JobError("forbidden", { resource, action }))`.
- `core.import` handler: `BatchSize 1, LeaseSeconds 600, MaxAttempts = 1` (never re-runs blindly;
  a crash → `Jobs.AttemptsExhausted` + `core.job.failed`; admin `retry` resumes from checkpoint).
  Chunks of `ImportChunkRows` (10,000); checkpoint `ImportCheckpointState(NextRow, OrderHash,
  Inserted, Updated, ChildrenInserted, ChildrenUpdated, ChildrenDeleted, CommittedRowRanges,
  CommittedNewIds)` in `Jobs.StateJson` rides each chunk's transaction; `ProgressPercent =
  min(99, 100 × NextRow / RowCount)`. Progress phase keys `Excel.Import.Parsing`,
  `Excel.Import.Resolving`, `Excel.Import.Validating`, `Excel.Import.Saving`. Cancellation takes
  effect at a chunk boundary; earlier chunks stay committed (**partial success is possible in a
  background non-`Atomic` import**).
- Failed background import: `Import.ResultJson` gets the errors; item fails with
  `JobError("Excel.Import.Failed", {})`.

#### Notifications raised (0019 §12.3, §12.6)

| Type | When | Arguments | Target |
|---|---|---|---|
| `core.export.ready` | background export succeeded (`NotifyOnSuccess`) | `fileName`, `rowCount` | `TargetResource = "core.Export"`, `TargetId` = the `Export` row id |
| `core.import.completed` | background import succeeded | `fileName`, `rowCount`, `errorCount` | `core.Import`, `ImportId` |
| `core.import.failed` | background import deliberately failed | `fileName`, `errorCode` (first error) | `core.Import`, `ImportId` |

- Recipient: the item's `Job.RequestedById`. All three `Mutable = false` (user cannot mute).
  **There is no `core.export.failed`**: a failed export is covered by spec 0020's generic
  `core.job.failed`. Synchronous operations raise no notification.

#### Options and telemetry (0019 §14)

`ExcelOptions` (`Tellma:Excel`): `MaxSynchronousExportRows` 100000, `MaxExportRows` 1048575,
`MaxSynchronousImportRows` 10000, `MaxAtomicImportRows` 100000, `ImportChunkRows` 10000,
`ImportConcurrencyRetries` 2, `MaxSynchronousImportFileBytes` 16 MB,
`MaxSynchronousImportUncompressedBytes` 512 MiB, `MaxImportFileBytes` 100 MiB,
`MaxImportUncompressedBytes` 2 GB, `MaxInMemorySharedStringBytes` 32 MB, `MaxSharedStringEntries`
16,000,000, `MaxReportedErrors` 100, `ExportFileRetentionDays` 7, `ImportFileRetentionDays` 7.
Server meters `tellma.excel.*` only.

### 2C. Background jobs and the scheduler (spec 0020)

#### Tables (0020 §2.1, §7.1, §7.2, §6.10)

- `core.Jobs` — one queue table per tenant; non-temporal; never saved through `IDataBatch.Save`;
  business rows reference it through `IJobEntity.JobId` (FK `ON DELETE SET NULL`, unique) and the
  machinery never writes a business table except that one column at enqueue (§1.3, §2.4).
  Columns: `Id`, `HandlerKey nvarchar(64)`, `Status varchar(9)`, `DueAt`, `Attempts`,
  `LeaseToken` (model-only), `LeaseExpiresAt`, `LeaseOwner nvarchar(128)` (`<instance>/<pid>`,
  diagnostic), `StartedAt` (first claim), `CompletedAt`, `CancelRequestedAt`, `ArgumentsJson`
  (≤ 64 KB), `StateJson` (checkpoint; model-only), `ProgressPercent tinyint` (≤ 100),
  `ProgressKey nvarchar(64)`, `ProgressArgumentsJson nvarchar(4000)`, `ErrorCode nvarchar(64)`,
  `ErrorArgumentsJson nvarchar(4000)`, `ErrorTraceId varchar(32)` (W3C trace id), `TraceParent
  nvarchar(55)`, `RunAsUserId int` (FK users), `RequestedById int?` ("who is notified" and the
  owner of "My jobs"), `ScheduleId int?` (FK `ON DELETE SET NULL`), `ScheduledFor` (the cron
  occurrence; null for run-now and user-triggered jobs), `CreatedAt`. **No `ModifiedAt`, no audit
  columns, no label/title column.**
- Indexes include `IX_Jobs_RequestedBy (RequestedById, CreatedAt DESC)` and
  `IX_Jobs_RequestedActive (RequestedById, CreatedAt DESC, Id DESC) INCLUDE (ScheduledFor)`
  filtered `Status IN ('Pending','Running','Held')` — serves spec 0021 §5.3's running-jobs read.
- `core.Schedules` (temporal) + `core.ScheduleStates` (non-temporal sibling: `ScheduleId`,
  `NextDueAt`, `LastFiredAt`, `LastScheduledFor`, `LastSkippedAt`, `LastJobId`, `LeaseToken`,
  `LeaseExpiresAt`) + `core.JobWorkerState` (single row: `Id = 1`, `LastTickAt`,
  `LastTickOwner`).

#### `JobStatus` and transitions (0020 §2.2)

`enum JobStatus { Pending, Running, Succeeded, Failed, Cancelled, Held }`

| From | To | By |
|---|---|---|
| `Pending` | `Running` | claim |
| `Running` | `Pending` | completion with `Retry` or `Released` |
| `Running` | `Succeeded` / `Failed` / `Cancelled` | completion |
| `Pending` | `Cancelled` | `cancel` |
| `Running` | `Running` + `CancelRequestedAt` | `cancel` (token cancelled at the next renewal, or next claim cancels) |
| `Pending`, `Running` (lease expired) | `Held` | gap hold |
| `Pending`, `Held`, `Running` (lease expired) | `Cancelled` | retired-key cancel (`Jobs.HandlerRetired`) |
| `Held` | `Pending` | `resume` |
| `Held` | `Cancelled` | `cancel` |
| `Failed` | `Pending` | `retry` (`Attempts = 0`) |

- Platform error codes (`ErrorCode`): `Jobs.UserInactive`, `Jobs.AttemptsExhausted`,
  `Jobs.EntityMissing`, `Jobs.HeldAfterGap`, `Jobs.Cancelled`, `Jobs.Internal` (no arguments; the
  exception only in logs; `ErrorTraceId` is what a requester quotes to support),
  `Jobs.HandlerRetired`; else a handler's code or a validation code (§2.1, §3.4).
- Outcome rules (§3.4): `ValidationException` → `Failed` at once with the first error; other
  exceptions → `Retry` with `Jobs.Internal` and full-jitter backoff
  `U(0, min(RetryMaxSeconds, RetryBaseSeconds × 2^(Attempts−1)))`; `Retry` at `MaxAttempts` →
  `Failed`.
- `Succeeded` sets `ProgressPercent = 100` (§5.4); handlers never report 100 themselves.

#### The `Job` entity and progress fields (0020 §2.3, §3.3)

- `Job : Entity<int>` (no audit), every member `[ServerOwned]`; stack resource `core.Job`,
  `[Stack(Operations = Query | Details)]`; `[DefaultSelect]` = every member minus `ArgumentsJson`,
  `LeaseOwner`, `TraceParent` (the details page selects `ArgumentsJson`). `StateJson` and
  `LeaseToken` are shadow properties, absent from Queryex. Navigations `RunAsUser`,
  `RequestedBy`, `Schedule`.
- Progress = `ProgressPercent` + `ProgressKey` (a resource key) + `ProgressArgumentsJson` (its ICU
  arguments). Errors likewise. "The SPA renders the key with its arguments from the string pack
  (spec 0013 §10.4), exactly as it renders a validation code" (§3.3). Keys live in the handler
  assembly's `Strings.resx`; `Jobs.*` keys are Core's.
- Known progress keys: `Blob.SweepProgress {claimed, deleted}` (0017 §7.1),
  `Excel.Export.Progress {rows}` (0019 §12.3), `Excel.Import.Parsing|Resolving|Validating|Saving`
  (0019 §12.4).
- `IJobProgress.Report(percent, message, state)` is **buffered and rides the next renewal**;
  renewal interval = shortest `LeaseSeconds / 5` among in-flight batches (§5.2); `FlushAsync`
  writes now; `Append` writes inside the caller's transaction (import chunks).
- Note: `CountCap` named in the round-2 focus is not a job field; it is Excel's capped count on
  the first page of a query (0019 §4.1, `ExcelQuery.CountCap`).

#### Handler options (0020 §3.1)

`[JobHandler(Key, BatchSize = 1, LeaseSeconds = 300, MaxAttempts = 5, MaxConcurrency = 1,
RetryBaseSeconds = 30, RetryMaxSeconds = 3600, Schedulable = false, InboxOnlyFailureNotices =
false)]`. Key grammar `^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+$` ≤ 64; `core.` reserved; module
slug or deployment slug prefixes (`acme.recompute-balances`) (§3.7). A breaking arguments change
is a new key; retired keys via `FeatureContribution.RetiredJobKey`.

Known handlers: `core.export` (600 s lease, 3 attempts, `Schedulable`), `core.import` (600 s, 1
attempt), `core.blob-sweep`, `core.blob-reconcile`, `core.job-retention`, `core.file-retention`,
`core.tree-verify`, `core.notification-delivery` (spec 0021; `BatchSize 4`, 12 attempts,
inbox-only failures), the email outbox handler (spec 0022; `BatchSize 25`, 120 s, 12 attempts).

#### Who sees and cancels which jobs (0020 §11.2, §8)

- Securables: `core.Job × Read | Retry | Cancel | Resume`; `core.Schedule × Read | Save | Delete |
  Activate` (**the three schedule write actions are sensitive by default and require step-up per
  spec 0014**; `core.Job × Cancel` is not sensitive).
- Self-scope criterion `RequestedById = me()` for `core.Job × Read` and `core.Job × Cancel` →
  every member sees "My jobs" and cancels jobs they requested, without a role. **`retry` and
  `resume` are never self-scoped** (administrators only) — a member cannot retry their own failed
  import or resume their own held job.
- Run-as rules: built-ins and platform batch handlers run as `WellKnownIds.SystemUserId`; user
  schedules run as their creator (re-stamped only by `take-over`); user-triggered jobs run as the
  requester; run-now runs as the caller (system user on a built-in) with the caller as
  `RequestedById`; admin `retry`/`resume` keep the original `RunAsUserId`. Permissions are
  evaluated at run time (§8).
- System jobs have `RequestedById = null` → failures go to administrators; they are invisible in
  every member's "My jobs" (§4.1).

#### Endpoints (0020 §11.3; spec 0016 projection, `POST /{tenantId}/api/web/...`)

| Route | Notes |
|---|---|
| `jobs/query`, `jobs/get` (details) | `Query | Details` only; self-scoped for members |
| `jobs/retry` | `[EntityAction("retry")]`, action `Retry`; precondition `Status = Failed` else `Jobs.NotRetryable` |
| `jobs/cancel` | action `Cancel`, `Destructive = true`; precondition `Status IN (Pending, Held, Running)` else `Jobs.NotCancellable`; `Running` only flagged |
| `jobs/resume` | action `Resume`; precondition `Status = Held` else `Jobs.NotHeld` |
| `schedules/*` standard ops (query, get, save, delete, activate, deactivate) | full stack; `[Searchable]` `Name`, `Code` |
| `schedules/take-over` | `[EntityAction("take-over", Action = "Save")]` over ids; sets `RunAsUserId` = caller |
| `schedules/run-now` | `[EntityAction("run-now", Action = "Save", SingleTarget)]` → 202 `JobAccepted(JobId, null)` |

- The job actions are `Idempotent = true` batches but **not** spec 0016's retry-safe
  `Idempotent` (a client retry after commit fails the precondition); they publish `job.changed`
  for each target with a `RequestedById` (§11.1).
- `JobAccepted(int JobId, int? ResourceId)` is spec 0016's 202 body; a 202 carries its polling
  target in the body, no `Location` header; "no polling endpoint exists beyond the standard
  operations" (spec 0016 §3.9, §9.3).
- MCP tool `tellma_job` reserved, not shipped.

#### Pages the spec names (0020 §11.3)

- **Schedules**: standard search and details; owner, next due, last fired, `take-over` and
  `run-now` buttons, a paused banner naming the remedy (`take-over` for `OwnerInactive`, an edited
  expression for `Exhausted`); built-in badge and locked fields derived from `Id <=
  WellKnownIds.ReservedIdBandEnd` (999) (§7.1).
- **Background jobs** (administrative): status, handler and age filters; each error's trace id
  with a copy control; a heartbeat banner reading `core.JobWorkerState`; a "held after gap" banner
  with resume and cancel over the held ids.
- **My jobs**: the same query under the self-scope criterion, with cancel on own jobs.

#### Hub events and notifications raised by the machinery (0020 §10)

- Hub event `job.changed { jobId }` — thin; published **only when `RequestedById` is set and only
  to that user**: on the completion batch, on the job actions' batch, and via `PublishAsync`
  after claims and after **progress-carrying renewals** (§5.2, §10). Spec 0021: the SPA calls
  `inbox/summary` on every `job.changed` (0021 §5.4); its `InboxRunningJob(Id, HandlerKey,
  Status, ProgressPercent, ProgressKey, ProgressArgumentsJson, CancelRequestedAt, StartedAt,
  CreatedAt)` (0021 §5.2) lists the caller's own `Pending|Running|Held` jobs with
  `ScheduledFor IS NULL` (no cron occurrences), capped at `SummaryLatestCount`, newest first,
  under an "In progress" heading in the bell dropdown (0021 §5.3, §5.4).

| Key | Mutable | Severity | Toast | Target | Raised | Withdrawn | Recipients | Arguments |
|---|---|---|---|---|---|---|---|---|
| `core.job.failed` | false | `Error` | true | `core.Job` | completion ends `Failed` (inbox only when `InboxOnlyFailureNotices`) | never | `RequestedById`, else administrators | `{ handlerKey, errorCode }` |
| `core.job.held` | false | `Warning` | true | none | gap hold, once per requester; `DedupKey 'core.job.held'` | `resume`/`cancel` of the requester's last held row | each distinct `RequestedById` | `{ count }` |
| `core.schedule.paused` | **true** | `Warning` | true | `core.Schedule` | §7.7; `DedupKey 'core.schedule.paused:<scheduleId>'` | `activate`, delete | `Exhausted`/`HandlerRetired`: owner if a person, else admins; `OwnerInactive`: admins | `{ scheduleId, name, reason }` |
| `core.scheduler.gap` | false | `Warning` | true | none | gap detected; `DedupKey 'core.scheduler.gap'` | `resume`/`cancel` ending the last held row | administrators | `{ previousTickAt, now, heldCount }` |

All `Category = "jobs"`; rely on spec 0021's default `OnDuplicate = Suppress`.

#### Scheduler (0020 §7)

- `Schedule : TopLevelEntity, IActivatable` (resource `core.Schedule`, temporal): `Code` (unique;
  = `HandlerKey` on built-ins), `Name`/`Name2`/`Name3` (`[Multilingual]`), `HandlerKey`
  (`[WriteOnce]`), `ArgumentsJson` (`[JsonColumn]` — out of Queryex; details read carries it),
  `CronExpression` (five-field Unix, Cronos 0.13.0 `CronFormat.Standard`, incl. `L`, `W`, `#`,
  `?`, `@daily` macros; no seconds), `TimeZoneId` (IANA or Windows; null = tenant zone at fire
  time), `MissedPolicy` (`Coalesce` default | `ReplayAll` | `Skip`), `OverlapPolicy` (`Skip`
  default | `Allow`), `CatchUpWindowMinutes` (1440), `RunAsUserId` (`[ServerOwned]`),
  `PausedReason` (`OwnerInactive | Exhausted | HandlerRetired`, `[ServerOwned]`), `IsActive`
  (server-owned; actions only).
- **`Schedule` has no next-due/last-fired members**; those live only in `core.ScheduleStates`,
  which the spec never declares as an entity or Queryex source (§7.2).
- Validation codes (§7.8): `Schedules.CronInvalid`, `Schedules.CronNeverOccurs`,
  `Schedules.TimeZoneUnknown`, `Schedules.HandlerUnknown`, `Schedules.HandlerNotSchedulable`
  (**only `core.export` is schedulable this release**), `Schedules.ReplayAllRequiresAllow`,
  `Schedules.ArgumentsTooLarge`, `Schedules.OwnedByAnotherUser` (changing `HandlerKey`,
  `ArgumentsJson`, `CronExpression` or `TimeZoneId` on another user's schedule; take over first),
  `Schedules.OwnerInactive` (on `activate`), `Schedules.BuiltInImmutable` (built-ins: only
  `Name*`, `CronExpression`, `TimeZoneId` editable; no delete, no take-over),
  `Schedules.BuiltInAlwaysActive`, `Schedules.RunInProgress` (run-now on a `Skip` schedule with a
  pending/running job).
- Run-now: allowed on paused/inactive schedules; `ScheduledFor` null; `ScheduleStates`
  untouched (§7.8).
- Pausing (§7.7): `IsActive = 0` + `PausedReason`; reactivation via `activate` clears it, is
  refused while the cause stands, and never catches up.
- Built-ins (§7.9): ids 1–99 Core, 100–499 packs, 500–999 distribution;
  `Code = Name = HandlerKey` (so built-in names are technical keys unless the tenant renames).

| Id | Key | Cron |
|---|---|---|
| 1 | `core.job-retention` | `0 3 * * *` |
| 2 | `core.notification-retention` | `30 3 * * *` |
| 3 | `core.file-retention` | `0 4 * * *` |
| 4 | `core.blob-sweep` | `*/15 * * * *` |
| 5 | `core.blob-reconcile` | `0 2 * * 6` |
| 6 | `core.tree-verify` | `0 1 * * 6` |

#### Gap hold (0020 §6.10)

- Each poll compares `JobWorkerState.LastTickAt` with `GapThreshold` (6 h). On a gap, rows more
  than the threshold overdue become `Held` (`Jobs.HeldAfterGap`) for an admin's `resume` or a
  `cancel` (members may cancel their own held rows). Schedules coalesce automatically.

#### Options and timing (0020 §12.1, §6.4, §14.4)

`JobsOptions` (`Tellma:Jobs`): `Enabled` true, `MaxParallelBatches` 8, `MinPollInterval` 1 s,
`MaxPollInterval` 30 s, `DrainTimeout` 4 s, `GapThreshold` 6 h, `SucceededRetention` 30 d,
`FailedRetention` 180 d. Pickup on another instance is bounded by `MaxPollInterval` (local nudge
only). `core.job-retention` never deletes `Held`/`Pending`. Workers poll only `Active` tenants:
on a `ReadOnly` tenant no export runs (Review flag 13).

## 3. Gaps for round 2

Blobs (0017):

- **No kind-policy metadata for the client** (allowed types, max size, image/thumbnail presence) —
  the SPA cannot set a file input's `accept` or pre-check size without hard-coding;
  `EntityMetadata.BlobReferences` is server-side only (0017 §2.3); spec 0029's client entity
  metadata is the natural carrier.
- **No discard endpoint for a staged blob** — an abandoned pick, a re-crop or a cancelled form
  keeps counting against the 200-row / 512 MiB per-user quota for 24 h (0017 §3.4, §3.5).
- **No upload progress or resumable upload contract** — raw-body single POST, no chunking; a
  progress bar needs XHR `upload.onprogress` (fetch has none); 100 MiB uploads on spotty links
  restart from zero (0017 §8.1, Review flag 6).
- **No client-side crop/resize contract** — the server only `CoverSquare`s or `Contain`s; "the
  browser crops for humans" (0017 §3.6) but no component, aspect, or size guidance exists; spec
  0014 says the SPA uploads a transparent PNG raster of a stylus capture for signatures (needs a
  signature-pad primitive).
- **No details-page attach recipe** — staged-id field binding, preview of the staged blob via the
  `GET` (uploader-only, `no-store`), re-upload on `Blob.NotAttachable`, and the 24 h TTL versus
  session drafts that survive refresh (a draft restored after the TTL fails with
  `reason = expired`) are all unspecified on the client side (0017 §4.1, §5.1).
- **No URL builder / avatar helper** — tenant id + kind + id + `variant=thumb` only when
  `Variants` lists it; an avatar for a user needs `User.ImageId` and kind `user-image`
  (0017 §2.2, Review flag 24).
- **No sign-out cache hygiene** — committed blobs are `private, max-age=31536000, immutable`; they
  stay in the browser HTTP cache (and any service-worker cache) after sign-out on shared devices;
  no `Clear-Site-Data` or SW purge is specified (0017 §5.3).
- **Blob responses vs the service worker** — staged responses are `no-store`; a SW runtime cache
  for `/blobs` must honour that and must not cache 404s of hidden blobs across users.

Excel (0019):

- **Display-shape columns ≠ grid columns** (the brief's open question): the export takes the
  grid's `Select` verbatim and writes one column per select item (0019 §2.1, §4.1), so `Id` and
  every unpacked navigation term become columns; there is no "one displayed column = one Excel
  column" request shape, no way to say "nav property in the user's language", and multilingual
  slots are not localized. Needs a decision (client sends a display select + `Headers`, or the
  request gains a column model).
- **No aggregate/report export** — `export` runs the stack's `query`, never an aggregate; reports
  (brief: flat, pivot fully expanded, chart data) need spec 0033's own export path.
- **No auto sync/background choice** — the client must pick `export` vs `export/start` (count
  known from the grid?) and handle 413 `Excel.Export.RowLimitExceeded` by offering `/start`
  (0019 §2.3).
- **Synchronous download mechanics** — the file is a chunked `POST` response with no
  `Content-Length` (spec 0016 §3.9); the SPA must fetch to a `Blob` and trigger a download, with
  no byte progress, under the long request timeout; cancellation on navigation per design rule C.
- **No in-progress row for background exports** — the `Export` row appears only at completion;
  "My exports" shows only finished files; progress lives in "My jobs"/the bell (0019 §12.1).
- **No errors workbook and a 100-error cap** — import UI can show only `MaxReportedErrors` (100)
  plus `totalErrors` (0019 Non-goals, Review flag 11).
- **Import mapping UI data is complete but unstored** — the client must hold the plan and echo
  `SheetMappings`/`ColumnMappings` on `import`; no server-side session (0019 §8.1).
- **Tree export** — display export passes "no ancestors" and has no depth/indent column; a tree
  grid exports flat (0019 §4.1).
- **Selected-rows export** — `Ids` path exists (≤ `MaxIds`), but cross-page selection beyond
  `MaxIds` has no path.
- **Scheduled export UI** — `core.export` is schedulable but nothing builds a schedule's
  `ArgumentsJson` (`ExportJobArguments` with `ExcelJobCulture`) from a search page; no "schedule
  this export" action exists (0019 §12.3, 0020 §7.8).

Jobs (0020):

- **No human label for a job** — `Job` has only `HandlerKey`; `ArgumentsJson` is off the default
  select and off `InboxRunningJob`; the bell and "My jobs" cannot say "Export of Centers" without
  a handler display-label convention (e.g. a `Jobs.Handler.<key>` resource key) and a
  resource/subject field (0020 §2.3; 0021 §5.2).
- **`core.ScheduleStates` and `core.JobWorkerState` are not queryable** — the Schedules page's
  "next due, last fired" and the Background jobs page's heartbeat banner have no specified read
  (no entity, navigation or endpoint) (0020 §7.2, §6.10, §11.3).
- **Live progress is coarse or absent** — `Report` rides renewals every `LeaseSeconds/5` (120 s
  for `core.export`); import checkpoints via `Append` publish no `job.changed`; so a progress bar
  driven by events updates rarely (0020 §5.2, §10).
- **Admin pages get no live events** — `job.changed` goes only to `RequestedById`; system jobs
  (null requester) and other users' jobs never push (0020 §10).
- **No member retry** — a member's failed import (`MaxAttempts = 1`) resumes only by an
  administrator's `retry` (0019 §12.4, 0020 §11.2).
- **No cron authoring aids** — no server-side description of an expression or next-N-occurrences
  preview; validation only on save (`Schedules.CronInvalid`/`CronNeverOccurs`) (0020 §7.8).
- **Step-up for schedule writes** — `core.Schedule × Save | Delete | Activate` are sensitive by
  default (spec 0014 step-up); the SPA needs a step-up flow (0020 §11.2).
- **Stale notification targets** — `core.export.ready` targets the `Export` row, deleted after 7
  days; the job row after 30 days (succeeded); deep links from older notices 404 (0019 §14.1,
  0020 §12.1).

## 4. Likely change points

- **0019 §2.1 (`ExportRequest`), §4.1 (display plan), §3.3 (headers)** — to answer the brief's
  grid-columns-vs-select question (display select per column, localized nav display, `Headers`
  from the grid's labels) and possibly the tree/indent and selected-rows cases.
- **0019 §1.2/§2.2 (operation gating)** — if the search page's export menu maps "export grid" to
  `Export` and "export for import"/"import template" to `Import`, confirm the gating, and decide
  whether "import template" deserves a named route rather than `Ids = []`.
- **0019 §12.1–§12.3** — if "My exports" should show in-progress exports (row at `/start`), or the
  notification should survive the row's retention.
- **0019 §12.3 progress / 0020 §5.2, §10 (`job.changed` cadence)** — if the UI needs live progress
  (flush per page, or publish on `Append`).
- **0020 §2.3 (`Job`), §11.3 (pages)** — add a display label/subject (or a handler-label resource
  convention), expose `ScheduleStates` and `JobWorkerState` reads for the specified pages.
- **0020 §11.2** — decide member `retry` of their own failed jobs (import resume).
- **0020 §7.8** — a "schedule this export" seam from the search page, if round 2 wants it; cron
  preview/description if the Schedules page needs it.
- **0017 §8.1** — a `DELETE` (discard) for a caller's staged blob, if the details page or crop UI
  needs quota hygiene; possibly a kind-policy read for the client (or put it in spec 0029's
  metadata and edit 0017 §2.3 to say so).
- **0017 §5.3** — cache headers vs sign-out (`Clear-Site-Data`) and the service worker's `/blobs`
  strategy (owner likely spec 0024/0025).
- **0021 §5.2 `InboxRunningJob`** (outside these sources) — would need the job label/subject if
  added to 0020.

## 5. Contradictions and surprises

- **"Import template" is not an operation.** The brief's export menu lists "import template";
  0019 delivers it as `export-for-import` with `Ids = []` (0019 §2.1). An empty `Ids` on the
  display `export` is a 400.
- **`export-for-import` lives under the `Import` op, not `Export`** (0019 §1.2): a stack with
  `Export` only has no "export for import"; one with `Import` only has the editable export but no
  grid export.
- **0019 explicitly says `Select` is "exactly as the grid uses it"** (§2.1) — the opposite of
  what the brief's open question suspects is right; the one-column-per-select-item rule (§4.1)
  makes the grid's mandatory `Id` and nav unpacking (`Center.Id, Center.Name, Center.Name2, …`)
  surface as extra columns. `Headers` exists but is parallel to select items, not to displayed
  columns.
- **Sync never promotes to background** (0019 §2.3): the 100,000-row export cap and the
  10,000-row / 16 MB import caps fail with 413/422 whose messages name the `/start` route; the
  client must branch.
- **Partial success exists, but only in background imports** — a non-`Atomic` `import/start`
  commits chunks and reports `CommittedRowRanges`; synchronous imports are all-or-nothing
  (0019 §11.3, §12.4). The import UI must present committed ranges after a failure.
- **No `core.export.failed` notification** — a failed background export surfaces only as the
  generic `core.job.failed { handlerKey, errorCode }` targeting `core.Job` (0019 §12.3).
- **Synchronous exports leave no trace** — no `Export` row, no job, no notification.
- **`job.changed` cadence**: for export/import the lease is 600 s, renewals every 120 s; a
  `cancel` on a running export/import takes effect only at the next renewal (up to ~2 min), at a
  chunk boundary for imports; earlier chunks stay committed (0020 §5.2, §11.1; 0019 §12.4).
- **Import progress via `Append` raises no `job.changed`** (0020 §5.2, §5.3, §10 list claims,
  renewals, completions and actions only).
- **`core.import` is `MaxAttempts = 1`** — a crash ends in `Jobs.AttemptsExhausted`; only an
  administrator's `retry` resumes from the checkpoint (0019 §12.4).
- **Signature size**: 0017's `Signature` preset is 2 MiB, but spec 0014 declares `User.SignatureId`
  with `MaxSizeBytes = 1_048_576` — the effective limit is 1 MiB.
- **Staged blobs are downloadable by their uploader** (no owner needed) with `no-store` — the
  intended preview-before-save path (0017 §5.1); committed blobs are immutable-cached for a year
  and never revalidated, so a re-upload must change the id (it does by construction).
- **Signatures and avatars are `AnyMember`** — any active member can fetch any user's signature
  PNG by id (0017 §5.2, Review flag 3).
- **The upload is the platform's one non-bulk, non-JSON write; the blob `GET` is the one `GET`** on
  the tenant surface; upload needs `Tellma-Client`, the `GET` is exempt (0017 §5.3, §8.1).
- **Naming drift**: 0017 says `AcceptsBinary(BlobOptions.MaxUploadSize)` and options at
  `Tellma:Blobs`; spec 0016 writes `AcceptsBinaryMetadata(Blobs:MaxUploadSize)`.
- **Upload on a `ReadOnly` tenant is 403 `tenant-read-only`, download works**; jobs never run on
  a non-`Active` tenant (0017 §8.1; 0020 §6.2).
- **The thumbnail exists only if listed** — requesting `thumb` for a blob without it is 404, not
  the primary (0017 Review flag 24); avatars' thumb is 96 px, attachments' 256 px, signatures have
  none; email attachments have none.
- **`Code = Name = HandlerKey` on built-in schedules** — the Schedules page shows technical keys
  (`core.blob-sweep`) as names unless localized labels are added (0020 §7.9).
- **Run-now never impersonates** — it runs as the caller, not the schedule owner (0020 Decision
  13); built-ins run-now as the system user with the caller as requester.
- **Display export of `DateTimeOffset` converts to the display zone; editable shape writes ISO
  text** — and Ethiopian dates are text in both shapes (0019 §3.1, §3.2).

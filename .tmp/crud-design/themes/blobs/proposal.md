# T7 — Blob storage and the record-plus-blobs pattern (future spec 0016)

Designed with all three lenses (distro-author simplicity, tier-2 performance and operations, correctness and maintainability). Everything below is written for a reader who has not seen the brain dump's other themes. Table names are singular (`core.Blob`) to match the brain dump's `core.User`; if the vocabulary seam settles on plural, the table becomes `core.Blobs` with no other change.

## 0. Lens checks

### Lens A — distro-author simplicity and AI-native authoring

| Probe | Answer |
|---|---|
| Lines a distro writes to give an entity an image | **One property line**: `[BlobReference("product-image", BlobPreset.Photo)] public int? ImageId { get; set; }`. The attribute drives the FK to `core.Blob`, the Queryex navigation, the save-time validator, the confirm/release statements in the persist batch, the kind registration, the upload policy, the download authorization, and the export-for-import exclusion. No endpoint, no service method, no migration code beyond the migration the model change produces. |
| Lines for N attachments per record | A weak child entity (`InvoiceAttachment { Id, InvoiceId, [BlobReference("invoice-attachment")] BlobId, Caption }`) — the same ~10 lines any weak entity costs; nothing blob-specific beyond the attribute. |
| Lines for a custom policy (own size limit, own image sizes) | One registration line: `blobs.AddKind("invoice-attachment", BlobKindPolicy.Attachment with { MaxSize = 200 * MiB })`. |
| Is the capability declared once? | Yes — the attribute is the single declaration; the endpoint pair (`POST …/blobs/{kind}`, `GET …/blobs/{kind}/{id}`) is mapped once by the stack for every kind, never per entity. |
| Common requirement → straightforward? | Upload-then-save is two client calls but one JSON save endpoint; the UI library and the MCP tool (`upload_file` seam, §6) hide the sequence. |
| Escape hatch | `IBlobStore` is public and replaceable (an on-prem S3-compatible store is one class); `IImageProcessor` is replaceable (licence swap); a kind may use `BlobReadAccess.Custom` with a distro-supplied `FilterTree`. |

### Lens B — tier-2 performance and operations

| Probe | Answer |
|---|---|
| DB calls: upload | **1** (`INSERT core.Blob`; the id comes from the buffered allocator, whose reservation rides an earlier round trip in steady state). Storage writes (primary + thumbnail, parallel) happen *after* the insert returns, outside any transaction. |
| DB calls: save that attaches/replaces/removes a blob | **+0** over a plain save. Blob-state loading rides the validation-context batch; confirm and release ride the persist batch through the emitter's `OUTPUT … INTO` capture. No post-commit work at all. |
| DB calls: delete by ids / by query / with descendants | **+0**: the emitted `DELETE … OUTPUT deleted.<col>` feeds the same release statement. |
| DB calls: GET a blob | **1** (owner row with RLS + blob metadata, one Queryex query) then one storage read. `If-None-Match` hit: 1 DB call, 0 storage. Fresh browser cache: 0 (URL is immutable-by-id, `max-age=1y, immutable`). |
| DB calls: sweep | 2 per batch of ≤ 500 due rows (select-due, delete-rows) with bounded-parallel storage deletes between them; loops until a short batch. |
| N+1s | A grid of 50 avatars costs 50 GETs on a cold browser — each 1 PK-shaped DB call; after that, zero for a year per (browser, blob). Recorded optimization for very large grids: an HMAC-signed URL that skips the DB call (§2 D8, not built now). The sweep never issues per-row DB calls; storage deletes run with `MaxDegreeOfParallelism = 8`. |
| Locks held across I/O | **None.** Storage I/O never happens inside a `SqlTransaction`: upload writes after its autocommit insert; the sweep runs storage deletes between two autocommit statements; GET reads storage after the query completes. Confirm/release statements inside the save transaction touch `core.Blob` rows by primary key via a table variable — row locks for the remaining life of the save transaction only. |
| Plan-cache friendliness | Confirm/release/sweep statements are fixed text with parameters and table variables; the only per-kind variation is the kind literal, which is a parameter. |
| Multi-instance safety | Create-only storage writes (`If-None-Match: *` / `File.Move(overwrite: false)`); the sweep is idempotent (missing objects are not errors, row deletes are by id) and single-runner by the scheduler's claim; no lease columns needed on `core.Blob`. |
| Observability from day one | Meter `Tellma.Core.Blobs`: uploads/downloads by kind and outcome, bytes, durations, store operations by operation/outcome (emitted by both adapters under the shared constant names), sweep deletions and failures, image-processing duration. No tenant tags. |

### Lens C — correctness, security, maintainability

| Probe | Answer |
|---|---|
| Where does a stale cache leak data? | Only the browser's private cache of an `immutable` blob after the user loses read access — the same residual as any downloaded file; `Cache-Control: private` keeps shared caches out. No server-side authorization cache exists; every cache miss re-authorizes against the owner row with RLS. |
| Which write path bypasses the tag bump? | Uploads write only `core.Blob`, which is not a cacheable entity and carries no tag. Attaching a blob changes the owner row through the pipeline, so the owner's tag bump is the emitter's ordinary duty. A distro's raw-SQL update of a blob column bypasses release (storage leak, not data leak); the FK from the owner column to `core.Blob.Id` still forbids dangling references, and the weekly reconciliation anti-join finds committed blobs no owner references. |
| Fail-closed access control | Upload: only an active tenant member, only registered kinds, per-user staging quota. Attach: the pipeline accepts a changed reference only if the blob is `Staged`, of the declared kind, and staged by the saving user — checked in validation (friendly 422) and again inside the transaction (rowcount guard, rollback on mismatch). Download: 404 for missing and for RLS-hidden alike. Non-image kinds are always `Content-Disposition: attachment` + `X-Content-Type-Options: nosniff`; SVG and executables are never accepted; stored images are re-encoded, so no client byte is ever served back verbatim. |
| Concurrency soundness | Two saves attaching the same staged id serialize on the row: the second confirm updates 0 rows → rollback → `Blob_NotAttachable`. A concurrent re-upload on the same record is caught by the owner's concurrency token; the release statement uses the *actual* old value captured by `OUTPUT deleted`, not the value loaded during validation, so an override save still releases the right blob. |
| No silent data loss | A blob row cannot be deleted while referenced (FK). Release is a state flip inside the save transaction; physical deletion is the sweep's, minutes later, from `Released` rows only. A DB restore to an older backup orphans storage objects (cost, not loss) — the reconciliation tool lists storage against rows. |
| Schema evolution / N−1 | `StorageKey` stores the full object name, so the path grammar can change without moving objects. `Variants` lists what exists per row, so adding a thumbnail size to a kind does not break old rows (GET falls back to the primary). Columns are additive; state strings are a closed set with a CHECK. |
| What breaks a distro on the next platform minor? | Only the attribute surface (`BlobReferenceAttribute`, `BlobPreset`) and the URL scheme `blobs/{kind}/{id}` — both frozen by the spec; `IBlobStore` gains members only as default-implemented interface members. |
| Testability | An abstract `BlobStoreConformanceTests` runs against the file-system store (temp dir) and the Azure adapter (Azurite via Testcontainers, `Category=Integration`); `BlobService` and the sweep run on the LocalDB fixture with a `TimeProvider` fake; image processing runs on checked-in fixture images including a decompression bomb and a polyglot. |

## 1. Critique

**The general design is right but stops one layer short.** The brain dump correctly separates records (JSON, SQL, Queryex) from blobs (binary, object store, etag GETs) and asks the two right questions (staging vs. inline; where image metadata lives). It misses that without a **blob table** the system cannot answer "which staged blobs are orphans", "which committed blobs lost their owner", "who uploaded this", or "what content type do I serve" without touching storage — and storage listing is the one operation Azure lifecycle rules, Azurite, and the file system all handle differently. Every decision below hangs off `core.Blob`.

**Specific problems.**

1. *Save pipeline steps 10 and 12.* "Pre-commit non-transactional side effects (creating blobs)" creates an orphan whenever the commit fails afterwards, and "post-commit deletion" leaks an object whenever the delete fails after the commit — neither failure leaves a record to retry from. With staged uploads and a blob table, both steps disappear: the blob write precedes the save request entirely, deletion is a state flip inside the transaction, and the physical delete belongs to the sweep. The save pipeline keeps **zero non-transactional side effects** for blobs.
2. *`ImageFitJson` on `User`.* It conflates a presentation choice with storage. Fit/crop is applied **before** storage (client-side crop for humans, server-side cover-crop for agents), and the stored bytes are already the final image. The column is dropped; if re-cropping without re-upload is ever wanted, it becomes a kind policy that keeps the original as a variant.
3. *`IBlobService { WriteBlobAsync(tenantId, pathBlobPairs); ReadBlobAsync(tenantId, paths); DeleteBlobsAsync(tenantId, paths) }`.* "Paths" invite client-derived names and traversal; names must be server-generated under a strict grammar. A batch *read* returning byte arrays is the wrong shape for 100 MB attachments (streams, one at a time). A batch *write* on Azure is not atomic and must be documented as best-effort parallel. The prose says "3 implementations" and lists two. `WriteBlob`/`DeleteBlobs` naming is inconsistent.
4. *`ImageId` has no type and no FK.* It is `int?` with a DB FK to `core.Blob.Id` — the FK is the only thing that makes "delete blob row" fail-closed.
5. *No limits, no processing, no authorization.* Size, type, pixel budget, re-encoding, who may download — none is stated. All are security-relevant (decompression bombs, polyglot files, HTML-in-SVG, enumerating avatars of users you cannot read).
6. *Multi-tenancy section: "extend the TenantRegistry to blob storage connection strings?"* No. One storage account per distribution (already the hosting decision) and one **container per tenant** addressed by tenant id means no per-tenant blob secrets exist to store, which is the only reading compatible with "keys never in the DB in clear text".
7. *"Garbage collect after N days."* Days is the wrong unit; a staged blob only has to outlive the form it was uploaded for. 24 hours is generous; the industry precedent is one hour to one week.
8. *Bulk principle vs. uploads.* "Every API is bulk shaped" does not fit bytes: an upload is bandwidth-bound, parallel single-file requests use the pipe better than one multipart body, per-file errors are cleaner, and retries are per file. The upload endpoint is deliberately single-file (§7).

## 2. Decisions

### D1 — Staged uploads; the record save carries the staged blob id (high)
**Decision.** A blob is uploaded first through `POST /{tenantId}/api/web/blobs/{kind}` (raw body), which stores it as a `Staged` row in `core.Blob` plus its object(s) in the store, and returns a `BlobDescriptor` whose `Id` is the staging token. The record is saved through the ordinary JSON save endpoint with the blob-reference property (`ImageId`, `BlobId`) set to that id. The save pipeline confirms the blob (`Staged → Committed`) inside the save transaction and releases any blob the change dereferenced (`Committed → Released`). Nothing non-transactional happens in the save.
**Rationale.** Keeps one JSON save endpoint for UI, import and MCP; validation of bytes (size, type, dimensions) happens at upload with a specific error; the admin-creates-a-user-with-a-photo case is natural; the whole "pre-commit side effects" class of bugs disappears. Every surveyed SaaS API (Stripe, Slack, Notion, Shopify, Drive above 5 MB) uses this shape.
**Rejected.** Multipart save: forces every save endpoint's body limit and content-type handling to change, re-uploads the image on every failed save attempt, requires MCP and import clients to build multipart bodies, and needs the blob write inside or around the transaction. Separate opaque token strings: the staged id *is* the token because the attach rule (D9) makes guessing useless; a second identifier would only add wire vocabulary.
**Review flag.** None; the alternative is strictly worse for this platform.

### D2 — A central `core.Blob` table holds every intrinsic fact about the bytes; records hold only the reference (high)
**Decision.** `core.Blob` (schema in §4) stores kind, storage name, state, content type, size, SHA-256, image dimensions, variants, original file name, uploader and timestamps. The owning record stores only the `int?` FK. No `ImageFitJson`, no per-entity copies of content type or size.
**Rationale.** Orphan sweep, per-user quota, download metadata without a storage round trip, FK-protected deletion, and reconciliation all need one table. Intrinsic facts (bytes never change) belong with the bytes; the record's only fact is "which blob".
**Rejected.** Metadata on the record: duplicated per entity, no way to enumerate orphans, no home for staged blobs that have no record yet. Metadata in storage only (blob metadata/tags): metadata writes churn the ETag, tags are eventually consistent and need extra RBAC, and neither exists on the file system.
**Review flag.** Dropping `ImageFitJson` (crop before upload) — Ahmad may want server-kept originals for re-cropping; that is a later kind policy, not a column on the record.

### D3 — Identity: `Id int` from `sq_Blob` is the only client-visible identifier; `StorageKey` is the internal object name (high)
**Decision.** `Id` follows the platform's app-assigned sequence ids and is what URLs, ETags, and blob-reference columns use. `StorageKey` is the store's full object name for the primary variant: `{kind}/{k0k1}/{key32}` where `key32` is 16 random bytes as lowercase hex and `k0k1` its first two characters; variants are `{StorageKey}.{variant}`. Clients never see `StorageKey`.
**Rationale.** Sequence ids keep the FK 4 bytes and match every other table; random object names avoid Azure hot partitions, make the file-system fan-out (`ab/…`) natural, and are traversal-proof by grammar. Storing the *full* name lets the grammar change later without moving objects.
**Rejected.** Random GUID as PK: 16-byte FKs and a fragmenting clustered key on a table that can reach millions of rows in document-heavy tenants. Deriving the object name from `Id`: sequential names, and a grammar change would strand old objects.

### D4 — `IBlobStore` in Abstractions takes the tenant id per call; file-system store in Core, Azure store as a connector adapter (high)
**Decision.** `Tellma.Core.Abstractions.Blobs.IBlobStore` (contract in §3) is a singleton with `int tenantId` on every member. `FileSystemBlobStore` ships in the new `Tellma.Core.Blobs` package (no vendor, so no connector). `AzureBlobStore` ships in `Tellma.Connector.AzureBlobStorage.Adapter` (`src/connector/azure-blob-storage/`) over `Azure.Storage.Blobs` 12.29.2, accepting a host-supplied `TokenCredential` (or a dev connection string for Azurite). Both are exercised by one abstract conformance suite.
**Rationale.** A singleton store shares one `BlobServiceClient`; the sweep and future tenant tooling address many tenants from one background scope, which a scoped tenant-bound store cannot do. The tenant-scoped orchestration (`IBlobService`, D6) reads the tenant from the request context and passes it down. Azure has a maintained first-party client, so the adapter-only rule applies; the file system has no vendor.
**Rejected.** Tenant id in configuration (one store instance per tenant): explodes clients and cannot serve background scopes. A "connector" for the file system: nothing to adapt.

### D5 — One container (Azure) or one directory (file system) per tenant, one storage account per distribution (high)
**Decision.** Azure container name `{ContainerPrefix}t{tenantId}` (prefix from options, default `tellma-`, dev worktrees `tellma-dev-{id8}-`; validated as a 3–63-char DNS label). Containers are created lazily with `CreateIfNotExists` and remembered in-process. File-system layout `{RootPath}/t{tenantId}/{StorageKey with / → separator}` with temp files under `{RootPath}/t{tenantId}/.tmp/`.
**Rationale.** Containers are unlimited and free; per-tenant containers give one-shot teardown, unambiguous lifecycle prefixes, container-scoped RBAC if ever needed, and no reliance on name discipline for isolation. No per-tenant secrets exist (managed identity on the distribution's account), which satisfies "keys never in the DB".
**Rejected.** Per-tenant storage accounts: per-tenant secrets or per-tenant identities, provisioning cost, no benefit. Shared container with tenant prefixes: isolation by string discipline only.

### D6 — Upload flow and the tenant-scoped `IBlobService` (high)
**Decision.** `IBlobService.StageAsync(kind, body, length, declaredContentType, fileName)` in `Tellma.Core.Blobs`:
1. Resolve the kind policy (unknown kind → 404 `Blob_UnknownKind`); enforce `MaxSize` from `Content-Length` and again while reading; images are buffered in memory (policy `MaxInputSize`, default 10 MiB), attachments stream to a temp file above 64 KiB.
2. Sniff magic bytes; reject types outside the kind's allowed set (`Blob_UnsupportedType` → 415); for images run `IImageProcessor` (D11); compute SHA-256 of the bytes to be stored.
3. Allocate `Id` from the allocator; **one DB round trip**: check the per-user staging quota (`MaxStagedPerUser` 200, `MaxStagedBytesPerUser` 512 MiB, both options) and `INSERT core.Blob` with `State = 'Staged'`, `ExpiresAt = DATEADD(second, @ttl, SYSUTCDATETIME())`, in one batch; quota failure → 422 `Blob_StagingQuotaExceeded`.
4. Write the object(s) to `IBlobStore` create-only. On failure the row stays `Staged` and expires into the sweep; the client gets a 500 and never receives the id.
5. Return `201 BlobDescriptor`.
Row before bytes means every object in storage has a row; a failed write leaves a row without bytes that only the sweep ever touches (the id was never handed out).
**Rationale.** One DB call, no lock across storage I/O, no invisible orphans in the common failure modes.
**Rejected.** Bytes before row: a failure between leaves an object no DB-driven sweep can see. Row, bytes, second row update with the storage ETag: two DB calls for a value the design does not need (the ETag comes from `Id`).

### D7 — Staging TTL 24 hours; per-user quota; sweep every 15 minutes (medium)
**Decision.** Options `Blobs:StagingTtl = 24h` (kind may override), `Blobs:SweepInterval = 15m`, `Blobs:SweepBatchSize = 500`, `Blobs:MaxStagedPerUser = 200`, `Blobs:MaxStagedBytesPerUser = 512 MiB`.
**Rationale.** A staged blob must outlive the form it was uploaded for, including "start the record, finish tomorrow morning"; 24 h costs nothing with quotas in place. Precedents range from 1 h (Notion) to 1 week (Drive).
**Review flag.** TTL 24 h vs. 1 h (Notion): a coding agent or a human never needs a day, but a draft-heavy UI might; the option makes it a deployment choice.

### D8 — Download: `GET /{tenantId}/api/web/blobs/{kind}/{id}?variant=thumb&download=1`, authorized through the owner row, immutable caching (high)
**Decision.** One endpoint for all kinds, mapped once by the stack. Handler: resolve kind → owner `(entity, column)` → one Queryex query on the owner entity, `Select` the blob navigation's `StorageKey, ContentType, Size, FileName, Variants`, `Filter: <column> = @id` AND the caller's RLS `FilterTree` from the permission evaluator for `(ownerResource, read)` (or the kind's `ReadAccess` rule) → no row → 404 (same as missing) → `ETag: "{id}"` (strong; bytes never change for an id) → `If-None-Match` match → 304 with `ETag` and `Cache-Control`, storage untouched → else open the store stream → `Results.Stream(stream, contentType, fileDownloadName?, entityTag)` with `Cache-Control: private, max-age=31536000, immutable`, `X-Content-Type-Options: nosniff`; `Content-Disposition: inline` only for image kinds, `attachment; filename*=UTF-8''…` otherwise or when `download=1`. Unknown variant → 400; a variant absent from the row's `Variants` → serve the primary.
**Rationale.** Kind in the URL avoids a blob-table lookup before authorization (one DB call, not two); the id-only URL is immutable by construction, so the browser caches for a year with no revalidation; a re-upload changes the record's id and therefore the URL. Authorization is "if you can read the owner you can read its blobs", evaluated on every cache miss, fail-closed.
**Rejected.** Per-entity projected URLs (`/users/{id}/image?v=`): one endpoint per slot, a mutable URL that revalidates on every render, and a `v` dance. A blob-id-only URL without kind: two DB calls or a union over every owner. Signed URLs (HMAC over tenant/id/expiry, no DB call): recorded as a later optimization for very large grids; not built because Queryex has no computed properties to carry the signature and the immutable cache already amortizes the cost.
**Review flag.** Registering Core's `user-image` kind with `BlobReadAccess.AnyMember` (every active member may fetch any user's avatar, since avatars appear on documents everywhere) versus `OwnerRead` (requires read permission on Users) — a policy call that belongs with T4/T8; the mechanism supports both.

### D9 — The attach rule and the save-pipeline contribution (high)
**Decision.** A blob-reference property is client-editable with one rule: a **changed** non-null value must identify a blob that is `Staged`, of the property's declared kind, and was staged by the saving user (`CreatedById = @userId`). An unchanged value is untouched; `null` releases. The capability contributes three things to the pipeline (contract in §3): (1) a validation-context request that loads `Id, State, Kind, CreatedById` for every changed non-null reference in the payload (rides the context batch; dedup key `("blob", ids)`), (2) a validator that reports `Blob_NotAttachable` on the property path, (3) persist-batch statements: the emitter captures `OUTPUT deleted.Id, deleted.<col>, inserted.<col> INTO @b_<Table>_<col>` on its INSERT/UPDATE/DELETE statements for every blob-reference column, and the capability appends, per column:

```sql
-- release blobs the change dereferenced
UPDATE b SET State = N'Released', ExpiresAt = SYSUTCDATETIME()
FROM core.Blob AS b
INNER JOIN @b_User_ImageId AS c ON c.OldBlobId = b.Id
WHERE (c.NewBlobId IS NULL OR c.NewBlobId <> c.OldBlobId) AND b.State = N'Committed';

-- confirm newly attached blobs (kind and uploader re-checked inside the transaction)
UPDATE b SET State = N'Committed', CommittedAt = SYSUTCDATETIME(), ExpiresAt = NULL
FROM core.Blob AS b
INNER JOIN @b_User_ImageId AS c ON c.NewBlobId = b.Id
WHERE (c.OldBlobId IS NULL OR c.OldBlobId <> c.NewBlobId)
  AND b.State = N'Staged' AND b.Kind = @kind AND b.CreatedById = @userId;
SELECT
  (SELECT COUNT(*) FROM @b_User_ImageId WHERE NewBlobId IS NOT NULL AND (OldBlobId IS NULL OR OldBlobId <> NewBlobId)) AS Expected,
  @@ROWCOUNT AS Confirmed;
```

The pipeline reads `(Expected, Confirmed)`; a mismatch rolls the transaction back and surfaces `Blob_NotAttachable` (a concurrent save won the blob or it expired between validation and persist). Deletes and child synchronisation feed the same table variable (`inserted.<col>` is `NULL` for deleted rows), so DeleteByIds, DeleteByQuery, DeleteWithDescendants and child removal all release correctly with no extra statement kinds.
**Rationale.** Old values come from `OUTPUT deleted`, never from memory, so an override save releases what was actually on the row. The FK plus the state machine make every path fail closed: an id that is not staged-by-me is rejected, a committed blob can never be re-pointed to another record, and a referenced blob row can never be deleted.
**Rejected.** Releasing from values loaded during validation: wrong under the concurrency override. `THROW` inside the batch on mismatch: works, but the executor already returns result sets and owns the rollback decision; keep the DB free of control flow.

### D10 — Physical deletion is the sweep's job only; no post-commit side effects (high)
**Decision.** The scheduled built-in task `blob-sweep` (a T10 consumer, per tenant, every `SweepInterval`, overlap policy *skip*, runs as the system user) repeats until a short batch: `SELECT TOP (@n) Id, StorageKey, Kind, Variants FROM core.Blob WHERE State <> N'Committed' AND ExpiresAt < SYSUTCDATETIME() ORDER BY ExpiresAt` → delete every object (primary + variants) through `IBlobStore.DeleteAsync` (missing objects are success) → `DELETE core.Blob WHERE Id IN (SELECT Id FROM @ids) AND State <> N'Committed'` → then `IBlobStore.PurgeIncompleteAsync(tenantId, now − 24h)` for adapter housekeeping (`.part` files). An FK violation on the row delete (a `Released` row somehow re-referenced) is logged, counted (`tellma.blobs.sweep.failures`), and skipped. A weekly `blob-reconcile` task runs, per kind, `SELECT b.Id FROM core.Blob b WHERE b.Kind = @kind AND b.State = N'Committed' AND NOT EXISTS (SELECT 1 FROM <owner> o WHERE o.<col> = b.Id)` and flips survivors to `Released` (catches raw-SQL deletes of owner rows). An operator tool (not scheduled) lists storage through `IBlobStore.ListAsync` against rows to find objects orphaned by a database restore.
**Rationale.** A released blob is already unreachable (its owner no longer points at it; the GET authorizes through the owner), so deleting it minutes later loses nothing and removes the only non-transactional step from the save. The sweep is idempotent and needs no lease columns because the scheduler runs one instance per schedule and the operations tolerate repetition. The Azure lifecycle rule is not relied on (day-granular, up to 24 h to start, absent on Azurite and on the file system); a deployment may still add `delete after 7 days under no prefix` as a backstop for `.part`-style leftovers, which is outside this design.
**Rejected.** Post-commit delete in the request: an extra DB call per replacing save, a failure mode with nothing to retry from, and a privacy story no better than "≤ 15 minutes later".
**Review flag.** Privacy: erasure of a replaced or deleted image completes at the next sweep (≤ `SweepInterval`), not at commit. State it in the spec; lower the interval if a customer needs it.

### D11 — Server-side image processing with ImageSharp behind `IImageProcessor` (medium)
**Decision.** `Tellma.Core.Blobs` ships `ImageSharpImageProcessor` (`SixLabors.ImageSharp` 4.1.1). Pipeline for image kinds: body limit → sniff (JPEG, PNG, WebP, GIF accepted; never SVG) → `Image.Identify` → reject above `MaxInputPixels` (50 MP) → decode with `DecoderOptions { TargetSize, MaxFrames = 1, SkipMetadata = false }` under a process-wide `MemoryAllocator` capped at 256 MB per allocation / 512 MB total → auto-orient from EXIF → strip metadata → resize per policy (`Contain` within `MaxDimension`, or `CoverSquare`) → encode primary and thumbnail in the policy's format (default WebP). Presets: `Avatar` (cover-square 512, thumbnail 96), `Photo` (contain 1600, thumbnail 256). The stored bytes are never the client's bytes.
**Rationale.** Fully managed, no native assets per RID, first-class decode limits — the lowest-friction choice for an agent-authored platform. Re-encoding is a security control (polyglots, metadata, bombs), not a feature.
**Rejected.** SkiaSharp: MIT and fast, but native assets per RID, the `NoDependencies` Linux package on App Service, and no built-in allocation cap. Magick.NET: strongest limits, heaviest footprint. System.Drawing: Windows-only.
**Review flag (licence).** Six Labors Split License 1.0: Apache-2.0 applies to the platform (open source) and to distributions as *transitive* consumers; the pricing page's wording is narrower. Recommendation: either buy the Boutique licence ($799/yr, all Six Labors libraries) to remove doubt, or switch the default to SkiaSharp — a one-package change behind `IImageProcessor`. Ahmad decides.

### D12 — Size and type limits are per kind, with global ceilings (high)
**Decision.** `BlobKindPolicy { MaxSize, AllowedContentTypes, Image?, StagingTtl?, ReadAccess }`. Presets: `Attachment` (100 MiB; PDF, Office OOXML, images, `text/plain`, `text/csv`, `application/zip`), `Avatar`, `Photo` (10 MiB input). Global ceiling `Blobs:MaxUploadSize` (default 100 MiB) enforced per request through `IHttpMaxRequestBodySizeFeature`, so the JSON endpoints keep Kestrel's default. Content type on the row is server-determined: the sniffed type for sniffable formats; for text types the declared type only if it is in the allowed set and the first 8 KiB contain no NUL byte. Executables (`MZ`, ELF, Mach-O), HTML and SVG are rejected regardless of declaration. `Content-Length` is required (411 otherwise).
**Rationale.** Limits belong to the kind because a 10 MiB avatar and a 100 MiB drawing are both legitimate; the ceiling protects the host.

### D13 — Blob-reference columns are excluded from Excel import/export-for-import and from natural keys (high)
**Decision.** The Excel codec treats `[BlobReference]` columns as not importable and not exportable-for-import; display export may show the file name via the navigation (`Image.FileName`).
**Rationale.** Ids are tenant-local and bytes do not travel in sheets; a cross-tenant image copy is a later "copy blob" feature.

### D14 — Packaging, options and composition (high)
**Decision.** `Tellma.Core.Abstractions/Blobs/` (interfaces, records, attribute, telemetry constants). `src/core/Tellma.Core.Blobs/` (entity + EF configuration, `BlobService`, `FileSystemBlobStore`, `ImageSharpImageProcessor`, endpoint mapping, sweep and reconcile handlers, options, README). `src/connector/azure-blob-storage/Tellma.Connector.AzureBlobStorage.Adapter/` (`AzureBlobStore`, `AddAzureBlobStore`). Tests: `test/core/Tellma.Core.Blobs.Tests` (unit + LocalDB), `test/connector/azure-blob-storage/…Adapter.IntegrationTests` (Azurite via `Testcontainers.Azurite` 4.14.0, `Category=Integration`; moves `Testcontainers.MsSql` to 4.14.0 in the same change). Composition: `services.AddTellmaBlobs(o => …)` registers `IBlobService`, the kind registry, the processor and the tasks; the host chooses exactly one store: `AddFileSystemBlobStore(o => o.RootPath = …)` or `AddAzureBlobStore(o => { o.ServiceUri = …; o.ContainerPrefix = …; }, credential)`. Startup validation fails when no store is registered, when a kind referenced by an attribute is unregistered or registered twice, when a kind has two owner columns, or when `RootPath` is relative, cross-volume with its `.tmp`, or longer than 160 characters on Windows. Package pins added: `Azure.Storage.Blobs` 12.29.2 (also lifts the identity server's transitive 12.26.0), `SixLabors.ImageSharp` 4.1.1, `Testcontainers.Azurite` 4.14.0. Blob stores never consult `ISandboxContext` (writing to our own storage is not an external side effect; sandbox tenants get their own containers like any tenant).

### D15 — Telemetry (high)
Meter `Tellma.Core.Blobs` (adapter meter `Tellma.Connector.AzureBlobStorage` emits the `store.*` instruments under the same constant names): `tellma.blobs.uploads` (counter; `kind`, `outcome` ∈ `staged|rejected_size|rejected_type|rejected_image|quota|failed`), `tellma.blobs.upload.bytes` (counter, By), `tellma.blobs.upload.duration` (histogram, s), `tellma.blobs.image.duration` (histogram, s), `tellma.blobs.downloads` (counter; `kind`, `outcome` ∈ `served|not_modified|not_found`), `tellma.blobs.store.operations` (counter; `operation` ∈ `write|read|delete|list|purge`, `outcome` ∈ `ok|conflict|not_found|failed`), `tellma.blobs.store.duration` (histogram, s; `operation`), `tellma.blobs.sweep.deleted` (counter; `state` ∈ `staged|released`), `tellma.blobs.sweep.failures` (counter). `kind` is a closed set per distribution; no tenant tag.

### D16 — Naming (medium)
`IBlobStore` (bytes by name, replaces the brain dump's `IBlobService`), `IBlobService` (tenant-scoped staging and reading), `core.Blob`, `Blob.Kind`, states `Staged | Committed | Released`, `StorageKey`, `ExpiresAt`, `BlobDescriptor`, `BlobReferenceAttribute`, `BlobPreset`, `BlobKindPolicy`, tasks `blob-sweep` and `blob-reconcile`.
**Review flag.** `IBlobStore` vs keeping `IBlobService` for the low-level contract as the brain dump wrote it.

## 3. Contracts

### 3.1 Owned by this theme — `Tellma.Core.Abstractions.Blobs`

```csharp
namespace Tellma.Core.Abstractions.Blobs;

/// <summary>
///     Stores and retrieves opaque objects for a tenant. Singleton; the tenant is a parameter so background
///     scopes can address many tenants. Object names follow <see cref="BlobName"/>; writes are create-only.
/// </summary>
public interface IBlobStore
{
    /// <summary>Writes every object, create-only; parallel and best-effort (a failure leaves earlier writes in place). Throws <see cref="BlobAlreadyExistsException"/> if a name exists.</summary>
    Task WriteAsync(int tenantId, IReadOnlyList<BlobWrite> writes, CancellationToken cancellationToken);

    /// <summary>Opens one object for streaming, or returns <c>null</c> when it does not exist. The caller disposes the result.</summary>
    Task<BlobStream?> OpenReadAsync(int tenantId, string name, CancellationToken cancellationToken);

    /// <summary>Deletes every named object; a missing object is success. Parallel, bounded.</summary>
    Task DeleteAsync(int tenantId, IReadOnlyList<string> names, CancellationToken cancellationToken);

    /// <summary>Lists objects under a name prefix (reconciliation and tenant tooling only; never on a request path).</summary>
    IAsyncEnumerable<BlobStoreEntry> ListAsync(int tenantId, string prefix, CancellationToken cancellationToken);

    /// <summary>Adapter housekeeping: removes incomplete artefacts (temp files) older than the cut-off. No-op where none exist.</summary>
    Task PurgeIncompleteAsync(int tenantId, DateTimeOffset olderThan, CancellationToken cancellationToken);
}

/// <summary>One object to write: its name, MIME type, content and exact length.</summary>
public sealed record BlobWrite(string Name, string ContentType, Stream Content, long Length);

/// <summary>An opened object: the readable content and its length.</summary>
public sealed class BlobStream(Stream content, long length) : IAsyncDisposable
{
    /// <summary>The object's bytes, positioned at the start.</summary>
    public Stream Content { get; } = content;

    /// <summary>The object's length in bytes.</summary>
    public long Length { get; } = length;

    /// <inheritdoc/>
    public ValueTask DisposeAsync() { return Content.DisposeAsync(); }
}

/// <summary>A listed object.</summary>
public sealed record BlobStoreEntry(string Name, long Length, DateTimeOffset LastModified);

/// <summary>Grammar of object names: <c>{kind}/{k0k1}/{key32}[.{variant}]</c>; validates before any adapter touches storage.</summary>
public static class BlobName
{
    /// <summary>Kind grammar: <c>^[a-z][a-z0-9-]{1,39}$</c>.</summary>
    public static bool IsValidKind(string kind);

    /// <summary>Variant grammar: <c>^[a-z0-9]{1,16}$</c>.</summary>
    public static bool IsValidVariant(string variant);

    /// <summary>Full-name grammar; the file-system store additionally re-checks the resolved path stays under the tenant root.</summary>
    public static bool IsValid(string name);

    /// <summary>Builds the primary name from a kind and a fresh 32-hex key.</summary>
    public static string Primary(string kind, string key32);

    /// <summary>Builds a variant name from a primary name.</summary>
    public static string Variant(string primaryName, string variant);
}

/// <summary>Create-only write hit an existing object.</summary>
public sealed class BlobAlreadyExistsException(string name) : Exception($"Blob '{name}' already exists.");

/// <summary>Any other store failure, wrapping the adapter's exception.</summary>
public sealed class BlobStoreException(string message, Exception inner) : Exception(message, inner);

/// <summary>Marks an <c>int?</c> property as a reference to <c>core.Blob</c> of one kind. Drives FK, navigation, validation, confirm/release, upload policy and download authorization.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class BlobReferenceAttribute(string kind, BlobPreset preset = BlobPreset.Attachment) : Attribute
{
    /// <summary>The kind name; exactly one property in the model may declare it.</summary>
    public string Kind { get; } = kind;

    /// <summary>The policy preset applied unless the host registers the kind explicitly.</summary>
    public BlobPreset Preset { get; } = preset;
}

/// <summary>Built-in policies.</summary>
public enum BlobPreset { Attachment, Avatar, Photo }

/// <summary>Who may download blobs of a kind.</summary>
public enum BlobReadAccess
{
    /// <summary>Read permission on the owner resource, with its row-level filter (default).</summary>
    OwnerRead,
    /// <summary>Any active member of the tenant.</summary>
    AnyMember,
    /// <summary>A filter supplied at registration replaces the permission filter.</summary>
    Custom,
}

/// <summary>Limits and processing for one kind.</summary>
public sealed record BlobKindPolicy
{
    /// <summary>Maximum accepted upload in bytes (input size for images).</summary>
    public required long MaxSize { get; init; }
    /// <summary>Accepted MIME types after sniffing.</summary>
    public required IReadOnlySet<string> AllowedContentTypes { get; init; }
    /// <summary>Image processing; <c>null</c> for non-image kinds.</summary>
    public ImagePolicy? Image { get; init; }
    /// <summary>Overrides <c>Blobs:StagingTtl</c>.</summary>
    public TimeSpan? StagingTtl { get; init; }
    /// <summary>Download authorization rule.</summary>
    public BlobReadAccess ReadAccess { get; init; } = BlobReadAccess.OwnerRead;
    /// <summary>Preset instances.</summary>
    public static BlobKindPolicy Attachment { get; }
    public static BlobKindPolicy Avatar { get; }
    public static BlobKindPolicy Photo { get; }
}

/// <summary>How an image kind is normalised before storage.</summary>
public sealed record ImagePolicy(int MaxDimension, ImageFit Fit, int? ThumbnailSize, ImageFormat Format = ImageFormat.WebP, int MaxInputPixels = 50_000_000);

/// <summary>Resize behaviour.</summary>
public enum ImageFit { Contain, CoverSquare }

/// <summary>Output encoding.</summary>
public enum ImageFormat { WebP, Jpeg, Png }

/// <summary>Decodes, validates, normalises and re-encodes untrusted images. Replaceable (licence choice).</summary>
public interface IImageProcessor
{
    /// <summary>Processes an image; throws <see cref="ImageRejectedException"/> for undecodable or over-budget input.</summary>
    Task<ProcessedImage> ProcessAsync(ReadOnlyMemory<byte> input, ImagePolicy policy, CancellationToken cancellationToken);
}

/// <summary>Primary and optional thumbnail bytes with the primary's dimensions and MIME type.</summary>
public sealed record ProcessedImage(ReadOnlyMemory<byte> Primary, int Width, int Height, ReadOnlyMemory<byte>? Thumbnail, string ContentType);

/// <summary>Thrown by <see cref="IImageProcessor"/>; carries a diagnostic code the host localises.</summary>
public sealed class ImageRejectedException(string code, params object[] arguments) : Exception(code);

/// <summary>Telemetry names (meter, instruments, tag keys, closed tag values) shared by Core and the Azure adapter.</summary>
public static class BlobsTelemetry
{
    public const string MeterName = "Tellma.Core.Blobs";
    public const string AzureMeterName = "Tellma.Connector.AzureBlobStorage";
    public const string Uploads = "tellma.blobs.uploads";
    public const string UploadBytes = "tellma.blobs.upload.bytes";
    public const string UploadDuration = "tellma.blobs.upload.duration";
    public const string ImageDuration = "tellma.blobs.image.duration";
    public const string Downloads = "tellma.blobs.downloads";
    public const string StoreOperations = "tellma.blobs.store.operations";
    public const string StoreDuration = "tellma.blobs.store.duration";
    public const string SweepDeleted = "tellma.blobs.sweep.deleted";
    public const string SweepFailures = "tellma.blobs.sweep.failures";
    public const string KindTag = "kind";
    public const string OutcomeTag = "outcome";
    public const string OperationTag = "operation";
    public const string StateTag = "state";
}
```

### 3.2 Owned by this theme — `Tellma.Core.Blobs`

```csharp
namespace Tellma.Core.Blobs;

/// <summary>Tenant-scoped blob orchestration over <see cref="IBlobStore"/> and <c>core.Blob</c>. Scoped; reads tenant and user from the request context.</summary>
public interface IBlobService
{
    /// <summary>Validates, processes and stages an upload; one DB round trip then create-only store writes. Returns the descriptor whose <c>Id</c> the record save attaches.</summary>
    Task<BlobDescriptor> StageAsync(string kind, Stream body, long length, string? declaredContentType, string? fileName, CancellationToken cancellationToken);

    /// <summary>Resolves a blob for download through its owner row and the caller's permissions; <c>null</c> means 404.</summary>
    Task<BlobDownload?> ResolveAsync(string kind, int id, string? variant, CancellationToken cancellationToken);
}

/// <summary>Wire shape returned by the upload endpoint and echoed in details payloads through the navigation.</summary>
public sealed record BlobDescriptor(int Id, string Kind, string ContentType, long Size, int? Width, int? Height, string? FileName, DateTimeOffset? ExpiresAt);

/// <summary>Everything the download endpoint needs before touching storage.</summary>
public sealed record BlobDownload(int Id, string Kind, string StorageName, string ContentType, long Size, string? FileName, bool IsImage);

/// <summary>Upload rejected; maps to 413 (<c>Blob_TooLarge</c>), 415 (<c>Blob_UnsupportedType</c>) or 422 (every other code).</summary>
public sealed class BlobRejectedException(string code, params object[] arguments) : Exception(code);

/// <summary>Registry of kinds, filled from <see cref="BlobReferenceAttribute"/> scanning and explicit registration; validated at startup.</summary>
public interface IBlobKindRegistry
{
    /// <summary>The kind, its policy and its owner (entity resource and column), or <c>null</c>.</summary>
    BlobKindDescriptor? Find(string kind);
    /// <summary>All kinds, for the reconcile task and diagnostics.</summary>
    IReadOnlyList<BlobKindDescriptor> All { get; }
}

/// <summary>A registered kind.</summary>
public sealed record BlobKindDescriptor(string Kind, BlobKindPolicy Policy, string OwnerResource, string OwnerEntity, string OwnerColumn, FilterTree? CustomReadFilter);

/// <summary>Options bound from <c>Blobs</c>.</summary>
public sealed class BlobOptions
{
    public TimeSpan StagingTtl { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(15);
    public int SweepBatchSize { get; set; } = 500;
    public int MaxStagedPerUser { get; set; } = 200;
    public long MaxStagedBytesPerUser { get; set; } = 512L << 20;
    public long MaxUploadSize { get; set; } = 100L << 20;
}

/// <summary>Composition.</summary>
public static class BlobServiceCollectionExtensions
{
    /// <summary>Registers the service, registry, processor, tasks and endpoints' dependencies.</summary>
    public static IServiceCollection AddTellmaBlobs(this IServiceCollection services, Action<BlobsBuilder>? configure = null);
    /// <summary>Registers the file-system store.</summary>
    public static IServiceCollection AddFileSystemBlobStore(this IServiceCollection services, Action<FileSystemBlobStoreOptions> configure);
}

/// <summary>Fluent registration surface.</summary>
public sealed class BlobsBuilder
{
    /// <summary>Registers or overrides a kind's policy (owner still comes from the attribute).</summary>
    public BlobsBuilder AddKind(string kind, BlobKindPolicy policy);
    /// <summary>Replaces the image processor.</summary>
    public BlobsBuilder UseImageProcessor<TProcessor>() where TProcessor : class, IImageProcessor;
}

/// <summary>Endpoint mapping; called once by the stack's <c>MapTellma</c>.</summary>
public static class BlobEndpointRouteBuilderExtensions
{
    /// <summary>Maps <c>POST blobs/{kind}</c> and <c>GET blobs/{kind}/{id}</c> under the web surface group.</summary>
    public static IEndpointRouteBuilder MapTellmaBlobs(this IEndpointRouteBuilder endpoints);
}
```

Azure adapter (`Tellma.Connector.AzureBlobStorage.Adapter`): `AzureBlobStoreOptions { Uri? ServiceUri; string? ConnectionString /* Azurite only */; string ContainerPrefix = "tellma-"; int MaxParallelism = 8; }` and `AddAzureBlobStore(this IServiceCollection, Action<AzureBlobStoreOptions>, TokenCredential? credential = null)` — production passes `ManagedIdentityCredential`; `DefaultAzureCredential` is never constructed by the adapter. File-system: `FileSystemBlobStoreOptions { string RootPath; }`.

### 3.3 Needed from other themes

- **T2 batch/emitter (seam 1).** `IBatchBuilder.Add(SqlStatement { Text, Parameters, MayRetry, WritesTables })` with a table-valued parameter for `IdList`; a result reader for a `(int Expected, int Confirmed)` row; and on the save emitter: `SaveStatement.CaptureChanges(string column)` that declares `DECLARE @b_<Table>_<Column> TABLE (RowId INT NOT NULL, OldBlobId INT NULL, NewBlobId INT NULL)` before the statement and adds `OUTPUT deleted.Id, deleted.<Column>, inserted.<Column> INTO @b_…` (INSERT: `NULL, inserted.<Column>`; DELETE: `deleted.<Column>, NULL`) to every INSERT/UPDATE/DELETE it emits for that table, including child synchronisation and query-driven deletes. Blob-reference columns are declared through `[BlobReference]`, so the emitter can capture them by convention with no per-call code.
- **T5 pipeline hook (seam 3).** `ISaveContributor<TEntity> { void RequestContext(SaveContextRequests<TEntity>); void Validate(SaveValidation<TEntity>); void ContributePersist(PersistBatch<TEntity>); }` plus a delete-side `ContributeDelete(DeleteBatch<TEntity>)`. The capability registers one contributor per blob-reference column; `Validate` receives the loaded context by dedup key and reports errors as `(propertyPath, code, args)`. The property is marked client-editable with a pipeline-enforced rule rather than server-owned.
- **T4 permission evaluation (seam 11).** `IPermissionEvaluator.Evaluate(resource, action) → PermissionResult { Allowed, FilterTree? Filter }` for the download handler.
- **T1 request context (seam 9).** `ITenantContext.TenantId` (`int`) and `IUserContext.UserId` (`int`) in request and job scopes.
- **T10 (seam 8).** A built-in schedule registration API for `blob-sweep` and `blob-reconcile` with per-tenant execution, overlap policy *skip*, system-user credentials.
- **T6 (seam 13).** The web surface group prefix, the CSRF header requirement (the upload POST sends it), the exception-to-status map entries for `BlobRejectedException` (413/415/422) and `Blob_UnknownKind` (404); `GET` is allowed for `blobs/{kind}/{id}` even on the all-POST surface.

## 4. Schema

```
core.Blob  (non-temporal; one row per uploaded object family; no UDTT — never bulk-saved through the emitter)
  Id            int            NOT NULL  PK clustered; app-assigned from sq_Blob
  Kind          nvarchar(40)   NOT NULL  registered kind name (grammar ^[a-z][a-z0-9-]{1,39}$)
  StorageKey    nvarchar(120)  NOT NULL  UNIQUE; full object name of the primary variant, e.g. user-image/3f/3fa9…(32 hex)
  State         nvarchar(10)   NOT NULL  CHECK (State IN (N'Staged', N'Committed', N'Released'))
  ContentType   nvarchar(100)  NOT NULL  server-determined MIME type of the primary variant
  FileName      nvarchar(255)  NULL      sanitized original name (display and Content-Disposition only; never a path)
  Size          bigint         NOT NULL  bytes of the primary variant
  Sha256        binary(32)     NOT NULL  hash of the stored primary bytes
  Width         int            NULL      images only
  Height        int            NULL      images only
  Variants      nvarchar(100)  NOT NULL  comma-separated variant names that exist ('' for none; e.g. 'thumb')
  CreatedAt     datetime2(3)   NOT NULL  SYSUTCDATETIME() at staging
  CreatedById   int            NOT NULL  FK core.User(Id); the uploader — the only user who may attach it
  CommittedAt   datetime2(3)   NULL      set on confirm
  ExpiresAt     datetime2(3)   NULL      sweep deadline: staging deadline while Staged, release time while Released, NULL while Committed

  IX_Blob_Expiry     nonclustered (ExpiresAt) INCLUDE (StorageKey, Kind, Variants, State) WHERE ExpiresAt IS NOT NULL   -- sweep
  IX_Blob_StagedBy   nonclustered (CreatedById) INCLUDE (Size) WHERE State = N'Staged'                                   -- quota
  UX_Blob_StorageKey unique nonclustered (StorageKey)
```

Owner side (per blob-reference column, e.g. `core.User.ImageId`): `int NULL`, `FK → core.Blob(Id)` with `ON DELETE NO ACTION`, nonclustered index on the column (also serves the download lookup `WHERE ImageId = @id` and the reconcile anti-join). Queryex schema: entity `Blob` (source `[core].[Blob]`, key `Id`, scalar properties as above except `StorageKey`, which is exposed only to the platform's own download query through a schema flag, and `Sha256`) plus a many-to-one navigation from every owner column (`Image` for `ImageId`, `Blob` for `BlobId`); `Blob` is not a securable root, so it is reachable only through navigations.

State machine: `Staged --confirm (save tx)--> Committed --release (save/delete tx, reconcile)--> Released --sweep--> (row and objects gone)`; `Staged --ExpiresAt passes--> swept`.

## 5. Answers

| Brain-dump question (abridged) | Answer |
|---|---|
| "What is the better approach for the record+blobs pattern?" (Option 1 staging vs Option 2 in-memory/multipart) | Option 1, staged uploads with the staged id carried in the JSON save (D1, D6, D9). |
| "How do we best store, retrieve, and modify the user profile picture?" | Upload to kind `user-image` (Avatar preset), save `User.ImageId`, render `GET blobs/user-image/{ImageId}` with immutable caching; replace by uploading again and saving the new id; remove by saving `null` (D2, D8, D9). |
| "`ImageFitJson` — here or in a centralized blob metadata table?" | Neither: dropped; crop before upload, server normalises (D2). Intrinsic facts live in `core.Blob`. |
| "Garbage collect it if it remains in staging for longer than N days (what is an appropriate period)?" | 24 hours by default, per-kind override, swept every 15 minutes (D7, D10). |
| Save flow step 10 "pre-commit non-transactional side effects e.g. creating blobs" and step 12 "deleting blobs" post-commit | Both removed for blobs: creation precedes the save request; deletion is a transactional state flip plus the sweep (D1, D9, D10). |
| "Azure blob storage utilizes the connector/adapter pattern. Should the FileSystem one utilize the same?" | Both implement `IBlobStore`; only Azure is a connector package (it adapts a vendor client); the file-system store lives in `Tellma.Core.Blobs` (D4, D14). |
| "Should every tenant get their own [container]?" | Yes: one container or directory per tenant, one account per distribution (D5). |
| "Should tenantId be a parameter in the interface, or part of the config?" | A parameter on every `IBlobStore` member; the tenant-scoped `IBlobService` supplies it from the request context (D4). |
| "Should we extend the TenantRegistry to blob storage connection strings, azure key vault?" | No: one account per distribution addressed by managed identity, containers by tenant id; nothing per tenant to store (D5). |
| "Self service endpoints for updating my profile pic" | Same upload endpoint; the self-service profile save (0017) attaches the id under the same attach rule (D9). |
| "Every API is bulk shaped" applied to uploads | Single-file raw-body uploads, parallel per file; stores are batch-shaped for writes/deletes (D12, §7). |

## 6. Seams

1. **Batch abstraction (T2).** I need: statements with `MayRetry` (confirm/release: `false`; sweep select: `true`; sweep delete: `true`, idempotent by id), `IdList` TVP, a typed reader for the `(Expected, Confirmed)` row, and the emitter's `CaptureChanges` convention for `[BlobReference]` columns (§3.3). The sweep runs autocommit statements, never inside the save transaction.
2. **Entity vs wire shape (T2/T6).** Blob-reference properties are plain `int?` on the entity and on the wire; details payloads may include the navigation's `BlobDescriptor` in the related-entity dictionary so the UI can show file name and size without a second call.
3. **One capability, declared once (T5).** `[BlobReference]` is the declaration; the stack scans it to register the kind, configure EF (FK + navigation), attach the contributor, and exclude the column from Excel. No permission action is added: uploading needs membership, attaching needs the owner's save permission, downloading needs the owner's read permission.
4. **Queryex schema (T2/T3).** `Blob` entity and owner navigations enter the schema; `StorageKey` is platform-only. No per-tenant variation, so no cache-key impact.
5. **Version tags (T3).** None owned; `core.Blob` is not cacheable; owner tag bumps are the emitter's.
8. **Background tasks (T10).** Two built-in per-tenant schedules (`blob-sweep` every 15 min, `blob-reconcile` weekly), overlap *skip*, system user, no lease columns on `core.Blob`; `PurgeIncompleteAsync` is called at the end of each sweep.
9. **Request context (T1).** `int` tenant id and `int` user id, available in request and job scopes; if the tenant id becomes a string, `IBlobStore` changes to match and container naming re-normalises to the DNS alphabet.
11. **Permission evaluation (T4).** `(ownerResource, read) → Allowed + FilterTree` composed into the download query; `AnyMember` bypasses it for kinds that opt in; `Custom` substitutes a registration-time filter.
12. **Blob staging tokens (owned).** The token is the `int` id of a `Staged` row; attach rule: staged, right kind, staged by me; TTL 24 h; consumers (T5 pipeline, T8 UserService self-service) pass it as the blob-reference property value, nothing else.
13. **Wire shapes (T6).** Upload: raw body, `Content-Type`, `Content-Length` (required), `?fileName=`; response `201 BlobDescriptor`. Download: `GET blobs/{kind}/{id}?variant=&download=`; `ETag "{id}"`, `Cache-Control: private, max-age=31536000, immutable`, 304 on `If-None-Match`, 404 for missing/hidden, 400 for a bad variant. Errors use the platform validation-error format with property path `Body`.
14. **Telemetry (T2 owns the budget).** Uploads count 1 DB call, saves +0, GET 1 — the DB-call budget instrument sees ordinary values; blob-specific instruments in D15.
15. **Notification enqueue (T10).** Not used.
17. **Vocabulary.** `core.Blob` singular per the brain dump (plural if the seam goes plural); `CreatedAt/CreatedById` only (immutable rows need no `Modified*`); enum-as-string states.
- **MCP (T6).** The MCP surface needs an `upload_file` path: for small images a tool accepting base64 (≤ 2 MiB) that calls `StageAsync`; for large files a tool returning the upload URL and kind for the agent's HTTP client. Recorded as a seam, not built.

## 7. Departures

- **"All I/O is bulkified; save endpoints accept arrays" (Guiding Principles).** The upload endpoint is single-file by design: bytes are bandwidth-bound, parallel single-file requests are faster and retry per file, and multipart batching would need per-part error envelopes. Store-level writes and deletes remain batch-shaped. A multipart batch upload can be added later without changing the store.
- **"Output caching, response compression" (Guiding Principles).** Neither applies to blob responses: output caching never serves authenticated requests, and images are pre-compressed; HTTP caching is delegated to the browser via `immutable` instead.
- **Hosting table "Blob Storage account per distribution".** Kept; refined to one container per tenant. No change.
- **Library architecture.** Adds `Tellma.Core.Blobs` (Core runtime package, like `Tellma.Core.Webhooks`) and the connector `Tellma.Connector.AzureBlobStorage.Adapter` under `src/connector/azure-blob-storage/`; pins `Azure.Storage.Blobs`, `SixLabors.ImageSharp`, `Testcontainers.Azurite` and bumps `Testcontainers.MsSql` to 4.14.0.
- **Save pipeline (brain dump, not ARCHITECTURE.md).** Steps 10 and 12 lose their blob use cases; the pipeline has no pre-commit non-transactional step at all.

## 8. Verification

Relied on from `research/blobs.md` (verified 2026-09-01): `Azure.Storage.Blobs` 12.29.2 with a `net10.0` target and the identity server's transitive 12.26.0; create-only uploads via `IfNoneMatch = ETag.All`; tag writes do not churn ETags but tag queries are eventually consistent and need Data Owner RBAC; lifecycle rules are day-granular, up to 24 h to start, unsupported on Azurite; Azurite 3.37.0 lacks soft delete, versioning and lifecycle; `Testcontainers.Azurite` 4.14.0 requires `Testcontainers` 4.14.0; container names are 3–63-char DNS labels with unlimited containers per account and container-scoped RBAC; Microsoft's guidance against `DefaultAzureCredential` in production; ImageSharp 4.1.1, its Split License clauses (a)/(b), pricing tiers, `Image.Identify`, `DecoderOptions.TargetSize/MaxFrames`, `MemoryAllocatorOptions` caps; SkiaSharp 4.151.1 needing `NativeAssets.Linux.NoDependencies` with no allocation cap; `Results.Stream` implementing 304/412/206 from a supplied ETag with preconditions evaluated inside result execution; RFC 9110 strong/weak validator rules; `Cache-Control: private, max-age, immutable` for immutable-by-id URLs; output caching never serving authenticated requests; Kestrel's 30,000,000-byte default body limit and `IFormFile` spilling above 64 KB; `File.Move` being `rename(2)`/`MoveFileEx` only within one volume; `Path.GetFullPath(path, basePath)` for traversal defence; Windows reserved names and `MAX_PATH`; git's two-hex-character fan-out; the staged-upload precedents (Stripe, Slack abort-on-incomplete, Notion 1-hour expiry, Shopify, Drive ≤ 5 MB multipart). From the briefing: no triggers and no logic in the DB (so `OUTPUT … INTO` is safe), RCSI defaults, `MERGE` is out, `ISandboxContext` semantics, one storage account per distribution.

Unverified (prior knowledge or inference): `Azure.Storage.Blobs.Batch`'s `BlobBatchClient.DeleteBlobsAsync` (up to 256 per batch) as an alternative to bounded-parallel single deletes — the adapter ships with parallel deletes and may adopt the batch client after verification; that Storage Blob Data Contributor includes container creation (`containers/write`) so lazy `CreateIfNotExists` works under the recommended role — otherwise provisioning must pre-create containers; ImageSharp's WebP encoder default quality and the exact EXIF auto-orient API in 4.x; that `EntityTagHeaderValue` requires a pre-quoted tag; that `IHttpMaxRequestBodySizeFeature` can be raised per request for the upload endpoint after routing (documented for endpoints generally, not re-read today).

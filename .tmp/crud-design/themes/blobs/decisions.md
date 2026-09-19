# Blob storage and the record-plus-blobs pattern — settled design (theme `blobs`, future spec 0016)

This file is the complete settled design for spec 0016. It is written for a reader who has seen none of the working files: every name, type, statement and column is here. Contract blocks use the platform's contract notation (names are normative; shape is described, not transcribed); SQL is the exact shape to emit. Table names are singular (`core.Blob`) to match the brain dump's `core.User`; if the vocabulary seam settles on plural, the table becomes `core.Blobs` with no other change.

## 1. Critique

**The general direction is right but stops one layer short.** The brain dump correctly separates records (JSON, SQL, Queryex) from blobs (binary, object store, etag-validated GETs) and asks the two right questions (staging versus inline; where image metadata lives). It misses that without a **blob table** the platform cannot answer "which staged blobs are orphans", "which committed blobs lost their owner", "who uploaded this", or "what content type do I serve" without touching storage — and storage listing is the one operation Azure lifecycle rules, Azurite and the file system all handle differently (lifecycle is day-granular and absent on Azurite and on disk). Every decision below hangs off `core.Blob`.

Specific problems in the brain dump:

1. **Save-pipeline steps 10 and 12.** "Pre-commit non-transactional side effects (creating blobs)" orphans an object whenever the commit fails afterwards; "post-commit deletion" leaks an object whenever the delete fails after the commit; neither failure leaves a record to retry from. With staged uploads and a blob table both steps disappear: the blob write precedes the save request entirely, "deletion" is a state flip inside the save transaction, and the physical delete belongs to a scheduled sweep. The save pipeline keeps **zero non-transactional side effects** for blobs.
2. **`ImageFitJson` on `User`** conflates a presentation choice with storage. Fit/crop is applied before storage (client-side crop for humans, server-side cover-crop for agents), and the stored bytes are already the final image. The column is dropped.
3. **`IBlobService { WriteBlobAsync(tenantId, pathBlobPairs); ReadBlobAsync(tenantId, paths); DeleteBlobsAsync(tenantId, paths) }`.** "Paths" invite client-derived names and traversal; object names must be server-generated under a strict grammar. A batch *read* returning byte arrays is the wrong shape for 100 MiB attachments (one stream at a time). A batch *write* on Azure is not atomic and must be documented as best-effort parallel. The prose says "3 implementations" and lists two.
4. **`ImageId` has no type and no FK.** It is `int?` with a DB foreign key to `core.Blob.Id`; the FK is what makes "delete a blob row" fail closed.
5. **No limits, no processing, no authorization** are stated — all security-relevant (decompression bombs, polyglot files, HTML-in-SVG, enumerating avatars of users one cannot read).
6. **"Extend the TenantRegistry to blob storage connection strings?"** No. One storage account per distribution (the hosting decision) plus one container per tenant addressed by tenant id means no per-tenant blob secret exists — the only reading compatible with "keys never in the DB in clear text".
7. **"Garbage collect after N days."** Days is the wrong unit; a staged blob only has to outlive the form it was uploaded for. Industry precedent is one hour (Notion) to one week (Drive); 24 hours with per-user quotas is generous and cheap.
8. **"Every API is bulk shaped" does not fit bytes.** An upload is bandwidth-bound; parallel single-file requests use the pipe better than one multipart body, errors and retries are per file. The upload endpoint is deliberately single-file; the store contract stays batch-shaped.
9. **Server-generated blobs are absent.** "Your export is ready" (inbox) and every background task that produces a file need the same storage, the same authorized GET and the same sweep; the design must let a job scope stage a blob and let the owning record's save commit it, with no second code path.
10. **Two races must be closed by construction:** a save confirming a staged blob at the moment the sweep deletes its bytes, and a released blob being physically deleted while a still-open transaction re-reads it. Both are closed below by a time check on confirm and a claim step in the sweep.

## 2. Decisions

Cost table (common case; DB round trips are counted, storage calls named):

| Operation | DB | Storage | Notes |
|---|---|---|---|
| Upload (stage) | 1 | 1–2 writes (primary, thumbnail; parallel) | Id from the buffered allocator; no lock across storage I/O |
| Save that attaches, replaces or removes a blob | +0 over a plain save | 0 | Context load rides the validation batch; confirm/release ride the persist batch |
| Delete by ids / by query / with descendants | +0 | 0 | The emitter's `OUTPUT deleted` feeds the release statement |
| GET a blob (cache miss) | 1 (two statements, one round trip) | 1 read | `If-None-Match` hit: 1 DB, 0 storage; fresh browser cache: 0 |
| Sweep | 2 per batch of ≤ 500 | ≤ 500 × variants deletes, bounded parallel | Repeats until a short batch |

### D1 — Staged uploads; the record save carries the staged blob id (high)

A blob is uploaded first through `POST /{tenantId}/api/web/blobs/{kind}` (raw body), which stores it as a `Staged` row in `core.Blob` plus its object(s) in the store and returns a `BlobDescriptor` whose `Id` is the staging token. The record is saved through the ordinary JSON save endpoint with its blob-reference property (`ImageId`, `BlobId`, …) set to that id. The save pipeline confirms the blob (`Staged → Committed`) inside the save transaction and releases whatever blob the change dereferenced (`Committed → Released`). Nothing non-transactional happens inside the save.

Rationale: one JSON save endpoint for UI, import, MCP and background producers; byte validation (size, type, dimensions) happens at upload with a specific error; the admin-creates-a-user-with-a-photo case is natural; the whole "pre-commit side effect" class of bugs disappears; every surveyed SaaS API with a general attachment model (Stripe, Slack, Notion, Shopify, Drive above 5 MB) uses this shape.

Rejected: multipart save (forces every save endpoint's body limit and content-type handling to change, re-uploads the image on every failed save attempt, requires MCP, import and job producers to build multipart bodies, and puts the blob write inside or around the transaction); a separate opaque token string (the staged id *is* the token because the attach rule in D9 makes guessing useless).

### D2 — A central `core.Blob` table holds every intrinsic fact about the bytes; records hold only the reference (high)

`core.Blob` (schema in §4) stores kind, storage name, state, content type, size, SHA-256, image dimensions, variants, original file name, uploader and timestamps. The owning record stores only an `int?` foreign key. No `ImageFitJson`, no per-entity copies of content type or size.

Rationale: the orphan sweep, the per-user quota, download metadata without a storage round trip, FK-protected deletion and reconciliation all need one table; intrinsic facts belong with the bytes, the record's only fact is "which blob".

Rejected: metadata on the record (duplicated per entity, no way to enumerate orphans, no home for a staged blob that has no record yet); metadata in storage only (metadata writes churn the storage ETag, tags are eventually consistent and need extra RBAC, and neither exists on the file system).

### D3 — `Id int` from `sq_Blob` is the only client-visible identifier; `StorageKey` is the internal object name (high)

`Id` follows the platform's app-assigned sequence ids and is what URLs, ETags and blob-reference columns use. `StorageKey` is the store's full object name of the primary variant: `{kind}/{k0k1}/{key32}` where `key32` is 16 random bytes as lowercase hex and `k0k1` its first two characters; variants are named `{StorageKey}.{variant}`. Clients never see `StorageKey`. The grammar is validated by `BlobName` before any adapter touches storage.

Rationale: sequence ids keep the FK four bytes and match every other table; random object names avoid Azure hot partitions, make the file-system fan-out (`ab/…`, git's convention) natural and are traversal-proof by grammar; storing the *full* name lets the grammar change later without moving objects.

Rejected: a GUID primary key (16-byte FKs, fragmenting clustered key on a table that reaches millions of rows in document-heavy tenants); deriving the object name from `Id` (sequential names; a grammar change strands old objects).

### D4 — `IBlobStore` in Abstractions takes the tenant id per call; file-system store in Core, Azure store as a connector adapter (high)

`Tellma.Core.Abstractions.Blobs.IBlobStore` (§3.1) is a singleton with `tenantId: int` on every member. `FileSystemBlobStore` ships in the new `Tellma.Core.Blobs` package (no vendor, so no connector). `AzureBlobStore` ships in `Tellma.Connector.AzureBlobs.Adapter` (`src/connector/azure-blobs/`, following `Tellma.Connector.AcsEmail.Adapter`) over `Azure.Storage.Blobs` 12.29.2, accepting a host-supplied `TokenCredential` (or a connection string for Azurite only); it never constructs `DefaultAzureCredential`. Both are exercised by one abstract conformance suite.

Rationale: a singleton store shares one `BlobServiceClient`; the sweep and tenant tooling address many tenants from one background scope, which a scoped tenant-bound store cannot do; the tenant-scoped orchestration (`IBlobService`, D6) reads the tenant from the request context and passes it down. Azure has a maintained first-party client, so the adapter-only rule applies; the file system has nothing to adapt.

Rejected: tenant id in configuration with one store instance per tenant (explodes clients; cannot serve background scopes); a "connector" package for the file system.

### D5 — One container (Azure) or one directory (file system) per tenant; one storage account per distribution (high)

Azure container name `{ContainerPrefix}t{tenantId}` (prefix from options, default `tellma-`; dev worktrees `tellma-dev-{id8}-`; the result is validated as a 3–63-character DNS label). Containers are created lazily with `CreateIfNotExists` and remembered in-process; a provisioning step may pre-create them (T1 seam). File-system layout `{RootPath}/t{tenantId}/{StorageKey with '/' → directory separator}`, temp files under `{RootPath}/t{tenantId}/.tmp/`, written as `{key32}.part`, flushed to disk, then `File.Move(tmp, final, overwrite: false)` — atomic because both paths share the volume (startup validation rejects a `RootPath` whose `.tmp` would cross volumes).

Rationale: containers are unlimited and free; per-tenant containers give one-shot teardown, unambiguous lifecycle prefixes and container-scoped RBAC without relying on name discipline for isolation; no per-tenant secret exists (managed identity on the distribution's account). Sandbox tenants are tenants like any other and get their own container; writing to our own storage is not an external side effect, so blob stores never consult `ISandboxContext`.

Rejected: per-tenant storage accounts (per-tenant secrets or identities, provisioning cost, no benefit); a shared container with tenant prefixes (isolation by string discipline only).

### D6 — Upload flow and the tenant-scoped `IBlobService` (high)

`IBlobService` lives in `Tellma.Core.Abstractions.Blobs` (module packages may produce blobs; they never reference `Tellma.Core`). It is scoped and reads the tenant id and user id from the request context, which T1 makes available in request scopes and in job scopes alike, so a background producer (an export task) stages through the same call. `StageAsync(request)`:

1. Resolve the kind (`Blob_UnknownKind` → 404). Require `Content-Length` (411 otherwise); enforce the kind's `MaxSize` from the declared length and again while reading. Images are buffered in memory (policy `MaxSize`, default 10 MiB); attachments stream to a temp file in the store's temp area above 64 KiB, computing SHA-256 on the way.
2. Sniff magic bytes; reject types outside the kind's allowed set (`Blob_UnsupportedType` → 415); for image kinds run `IImageProcessor` (D11); the hash and size are those of the bytes that will be stored.
3. Allocate `Id` from the platform allocator. **One DB round trip**: the per-user staging quota check and the `INSERT core.Blob` (`State = N'Staged'`, `ExpiresAt = DATEADD(second, @ttl, SYSUTCDATETIME())`) in one statement; quota exceeded → 422 `Blob_StagingQuotaExceeded`. The check is a soft limit and is not made race-proof.
4. Write the object(s) to `IBlobStore` create-only. On failure the row stays `Staged` and expires into the sweep; the client gets a 500 and never receives the id.
5. Return `201` with the `BlobDescriptor`.

Row before bytes means every object in storage has a row; a failed write leaves a row without bytes that only the sweep ever touches.

Rejected: bytes before row (a failure between leaves an object no DB-driven sweep can see); a second DB call to record the storage ETag (the HTTP ETag comes from `Id`, D8).

### D7 — Staging TTL 24 hours; per-user quota; sweep every 15 minutes (medium)

Options (§3.2): `Blobs:StagingTtl = 24h` (a kind may override), `Blobs:SweepInterval = 15m`, `Blobs:SweepBatchSize = 500`, `Blobs:SweepReclaimAfter = 60m`, `Blobs:MaxStagedPerUser = 200`, `Blobs:MaxStagedBytesPerUser = 512 MiB`, `Blobs:MaxUploadSize = 100 MiB`.

Rationale: a staged blob must outlive the form it was uploaded for, including "start the record, finish tomorrow morning"; with quotas in place 24 hours costs nothing; precedents range from 1 hour to 1 week.

### D8 — Download: `GET /{tenantId}/api/web/blobs/{kind}/{id}?variant=thumb&download=1`, authorized through the owner row, immutable caching (high)

One endpoint for all kinds, mapped once by the stack. `IBlobService.ResolveAsync(kind, id, variant)`:

1. Resolve the kind → owner `(entity, column, resource)` from the kind registry; unknown kind → 404; unknown variant name (grammar) → 400.
2. **One DB round trip, two statements.** Statement 1 (Queryex, retryable): root = the owner entity, `Select` = the owner's key, `Filter` = `<column> = @id` AND the caller's row-level filter from the permission evaluator for `(ownerResource, read)` — or no filter for `BlobReadAccess.AnyMember`, or the registration-time filter for `BlobReadAccess.Custom`. If the evaluator denies the action outright, the query is not issued. Statement 2 (raw SQL, retryable): `SELECT Id, Kind, StorageKey, ContentType, Size, FileName, Width, Height, Variants FROM core.Blob WHERE Id = @id AND Kind = @kind AND State = N'Committed'`. No owner row or no blob row → `null` → 404, indistinguishable from missing.
3. The endpoint computes the strong ETag `"{id}"` for the primary and `"{id}-{variant}"` for a variant (the bytes behind an id never change); on an `If-None-Match` match it returns 304 with `ETag` and `Cache-Control`, storage untouched. Otherwise it opens the store stream and returns `Results.Stream(stream, contentType, fileDownloadName, entityTag)` with `Cache-Control: private, max-age=31536000, immutable`, `X-Content-Type-Options: nosniff`; `Content-Disposition: inline` only for image kinds, `attachment; filename*=UTF-8''…` for every other kind or when `download=1`. Range processing is off in this release. A variant absent from the row's `Variants` serves the primary (only possible when a kind's policy gained a variant after the row was staged).

Rationale: the kind in the URL avoids a blob-table lookup before authorization and gives the owner directly; the id-only URL is immutable by construction, so the browser caches for a year with no revalidation, and a re-upload changes the record's id and therefore the URL; authorization is "if you can read the owner you can read its blobs", evaluated on every cache miss, fail-closed. `StorageKey` and `Sha256` are not in the Queryex schema, which is why the blob row is read by raw SQL rather than through a navigation. A grid of 50 avatars costs 50 one-round-trip GETs on a cold browser and zero afterwards; an HMAC-signed URL that skips the DB call is a recorded later optimization, not built.

Rejected: per-entity projected URLs (`/users/{id}/image?v=`; one endpoint per slot, mutable URLs revalidating on every render); a blob-id-only URL without the kind (a union over every owner); output caching (never serves authenticated requests).

### D9 — The attach rule and the save-pipeline contribution (high)

A blob-reference property is client-editable under one rule: a **changed** non-null value must identify a blob that is `Staged`, not expired, of the property's declared kind, staged by the saving user (`CreatedById = @userId`), and appears at most once across the payload. An unchanged value is untouched; `null` releases. The capability contributes three things to the pipeline, once per blob-reference column, through the T5 contributor hook (§3.4):

1. A validation-context request loading `Id, State, Kind, CreatedById, ExpiresAt` for every changed non-null reference in the payload (rides the context batch; dedup key `("blob", ids)`), and a validator reporting `Blob_NotAttachable` on the property path (friendly 422) and `Blob_DuplicateReference` for an id used twice.
2. Persist-batch capture: for every blob-reference column the emitter declares `DECLARE @b{o}_<Table>_<Column> TABLE (RowId int NOT NULL, OldBlobId int NULL, NewBlobId int NULL)` (`{o}` is the batch ordinal) before its statements and adds an `OUTPUT … INTO` clause to every INSERT, UPDATE and DELETE it emits for that table — including child synchronisation and query-driven deletes: INSERT `OUTPUT inserted.Id, NULL, inserted.<Column>`; UPDATE `OUTPUT inserted.Id, deleted.<Column>, inserted.<Column>`; DELETE `OUTPUT deleted.Id, deleted.<Column>, NULL`. (`OUTPUT … INTO` is safe because the platform has no triggers.)
3. Persist-batch statements appended per column, inside the save transaction:

```sql
-- release blobs the change dereferenced (old value from OUTPUT, never from memory)
UPDATE b SET State = N'Released', ExpiresAt = SYSUTCDATETIME()
FROM core.Blob AS b
INNER JOIN @b0_User_ImageId AS c ON c.OldBlobId = b.Id
WHERE (c.NewBlobId IS NULL OR c.NewBlobId <> c.OldBlobId) AND b.State = N'Committed';

-- confirm newly attached blobs (kind, uploader and expiry re-checked inside the transaction)
DECLARE @expected0 int = (SELECT COUNT(*) FROM @b0_User_ImageId
                          WHERE NewBlobId IS NOT NULL AND (OldBlobId IS NULL OR OldBlobId <> NewBlobId));
UPDATE b SET State = N'Committed', CommittedAt = SYSUTCDATETIME(), ExpiresAt = NULL
FROM core.Blob AS b
INNER JOIN @b0_User_ImageId AS c ON c.NewBlobId = b.Id
WHERE (c.OldBlobId IS NULL OR c.OldBlobId <> c.NewBlobId)
  AND b.State = N'Staged' AND b.Kind = @kind AND b.CreatedById = @userId
  AND b.ExpiresAt > SYSUTCDATETIME();
DECLARE @confirmed0 int = @@ROWCOUNT;
SELECT @expected0 AS Expected, @confirmed0 AS Confirmed;
```

The pipeline reads `(Expected, Confirmed)` before committing — the same read-then-commit step the row-level-security post-check uses — and on a mismatch rolls the transaction back and surfaces `Blob_NotAttachable` (a concurrent save won the blob, it expired between validation and persist, or the same id was attached twice). `ExpiresAt > SYSUTCDATETIME()` on confirm is what makes the sweep race-free: the sweep only claims rows whose `ExpiresAt` is already in the past on the same server clock, so a row the sweep has claimed can never be confirmed afterwards, and a row confirmed earlier is no longer `Staged` when the claim runs (an `UPDATE` evaluates its predicate against the latest committed version and waits on a locked row).

Rationale: old values come from `OUTPUT deleted`, so an override save releases what was actually on the row; the FK plus the state machine make every path fail closed (an id not staged-by-me is rejected, a committed blob can never be re-pointed to another record, a referenced blob row can never be deleted). Deletes, child removal and query deletes feed the same table variable, so DeleteByIds, DeleteByQuery, DeleteWithDescendants and child synchronisation all release correctly with no extra statement kinds.

Rejected: releasing from values loaded during validation (wrong under the concurrency override); `THROW` inside the batch on mismatch (works, and is the fallback if the batch commits inside its own text — see §9).

### D10 — Physical deletion is the sweep's job only; no post-commit side effects (high)

The built-in scheduled task `blob-sweep` (per tenant, every `SweepInterval`, overlap policy *skip*, runs as the system user) repeats until a short batch:

```sql
-- 1. claim: due rows become Deleting for SweepReclaimAfter (a crashed sweep is retried after that)
DECLARE @now datetime2(3) = SYSUTCDATETIME();
WITH due AS (
    SELECT TOP (@n) Id, Kind, StorageKey, Variants, State, ExpiresAt
    FROM core.Blob WITH (ROWLOCK)
    WHERE State IN (N'Staged', N'Released', N'Deleting') AND ExpiresAt < @now
    ORDER BY ExpiresAt)
UPDATE due SET State = N'Deleting', ExpiresAt = DATEADD(minute, @reclaimMinutes, @now)
OUTPUT inserted.Id, inserted.Kind, inserted.StorageKey, inserted.Variants;
```

then deletes every object (primary plus variants) through `IBlobStore.DeleteAsync` (missing objects are success, bounded parallelism 8), then

```sql
-- 2. remove the rows whose objects are gone (@ids is an IdList table-valued parameter)
DELETE b FROM core.Blob AS b INNER JOIN @ids AS i ON i.Id = b.Id WHERE b.State = N'Deleting';
```

and finally `IBlobStore.PurgeIncompleteAsync(tenantId, now − 24h)` for adapter housekeeping (`.part` files). A storage failure leaves the row `Deleting`; it is reclaimed after `SweepReclaimAfter`. An FK violation on the row delete is impossible by construction (only `Staged`, `Released` and `Deleting` rows are deleted and only `Staged` rows can be attached) and is logged, counted and skipped if it ever occurs.

The weekly built-in task `blob-reconcile` runs, per registered kind, `UPDATE b SET State = N'Released', ExpiresAt = SYSUTCDATETIME() FROM core.Blob AS b WHERE b.Kind = @kind AND b.State = N'Committed' AND b.CommittedAt < DATEADD(day, -1, SYSUTCDATETIME()) AND NOT EXISTS (SELECT 1 FROM <owner table> AS o WHERE o.<column> = b.Id)` — the backstop for owner rows removed outside the pipeline (raw SQL, DB cascades). An operator tool (not scheduled) lists storage through `IBlobStore.ListAsync` against rows to find objects orphaned by a database restore.

Rationale: a released blob is already unreachable (its owner no longer points at it, and the GET authorizes through the owner), so deleting it minutes later loses nothing and removes the only non-transactional step from the save; the claim makes the sweep idempotent and safe against concurrent confirms; the scheduler's overlap policy keeps one runner per tenant, so `core.Blob` needs no lease columns. Azure lifecycle rules are not relied on (day-granular, up to 24 hours to start, absent on Azurite and on the file system); a deployment may add one as a backstop outside this design.

Rejected: post-commit delete in the request (an extra DB call per replacing save, a failure mode with nothing to retry from, and a privacy story no better than "within `SweepInterval`"); a grace window instead of a claim (correct only while every transaction is shorter than the window).

### D11 — Server-side image processing with ImageSharp behind `IImageProcessor` (medium)

`Tellma.Core.Blobs` ships `ImageSharpImageProcessor` over `SixLabors.ImageSharp` 4.1.1. Pipeline for image kinds: body limit → sniff (JPEG, PNG, WebP, GIF accepted; never SVG) → `Image.Identify` → reject above `MaxInputPixels` (50 MP) → decode with `DecoderOptions { TargetSize, MaxFrames = 1 }` under a process-wide memory allocator capped at 256 MB per allocation and 512 MB in total → auto-orient from EXIF → strip metadata → resize per policy (`Contain` within `MaxDimension`, or `CoverSquare`) → encode primary and thumbnail in the policy's format (default WebP). Presets: `Avatar` (cover-square 512, thumbnail 96), `Photo` (contain 1600, thumbnail 256). The stored bytes are never the client's bytes.

Rationale: fully managed, no native assets per RID, first-class decode limits — the lowest-friction choice for an agent-authored platform; re-encoding is a security control (polyglots, metadata, bombs), not a feature.

Rejected: SkiaSharp (MIT and fast, but native assets per RID, the `NoDependencies` Linux package on App Service, no built-in allocation cap — the licence-risk-free fallback, one package swap behind `IImageProcessor`); Magick.NET (strongest limits, heaviest footprint); System.Drawing (Windows-only).

### D12 — Size and type limits are per kind, with global ceilings (high)

`BlobKindPolicy { MaxSize, AllowedContentTypes, Image, StagingTtl, ReadAccess }` with presets `Attachment` (100 MiB; PDF, Office OOXML, images, `text/plain`, `text/csv`, `application/zip`), `Avatar` and `Photo` (10 MiB input). The attribute may narrow `MaxSize` and set `ReadAccess` without any host code; the host's `AddKind` replaces a policy wholesale. The global ceiling `Blobs:MaxUploadSize` is applied per request by raising `IHttpMaxRequestBodySizeFeature.MaxRequestBodySize` on the upload endpoint only, so the JSON endpoints keep Kestrel's default. Content type on the row is server-determined: the sniffed type for sniffable formats; for text types the declared type only if it is in the allowed set and the first 8 KiB contain no NUL byte. Executables (`MZ`, ELF, Mach-O), HTML and SVG are rejected regardless of declaration.

### D13 — Blob-reference columns are excluded from Excel import and export-for-import and from natural keys (high)

The Excel codec treats `[BlobReference]` columns as not importable and not exportable-for-import; display export may show `Image.FileName` through the navigation. Ids are tenant-local and bytes do not travel in sheets; a cross-tenant copy is a later "copy blob" feature.

### D14 — Packaging, options and composition (high)

- `Tellma.Core.Abstractions/Blobs/`: `IBlobStore`, `IBlobService`, records, enums, `BlobReferenceAttribute`, `BlobName`, `IImageProcessor`, exceptions, `BlobTelemetryNames`.
- `src/core/Tellma.Core.Blobs/`: the `Blob` entity and its EF configuration (contributed to the tenant DB model by the blobs feature; `CreatedById` is configured against the model's `User` leaf type through the feature's `Requires` edge on the users feature), `BlobService`, `BlobKindRegistry` (built at startup from `[BlobReference]` properties in the EF model plus explicit registrations), `FileSystemBlobStore`, `ImageSharpImageProcessor`, endpoint mapping, the `blob-sweep` and `blob-reconcile` handlers, options, README.
- `src/connector/azure-blobs/Tellma.Connector.AzureBlobs.Adapter/`: `AzureBlobStore`, `AzureBlobStoreOptions`, `AddAzureBlobStore`, README.
- Tests: `test/core/Tellma.Core.Blobs.Tests` (unit; `BlobService`, the attach statements and the sweep on the LocalDB fixture with a `TimeProvider` fake; image fixtures including a decompression bomb and a polyglot; the abstract `BlobStoreConformanceTests` run against the file-system store in a temp directory); `test/connector/azure-blobs/Tellma.Connector.AzureBlobs.Adapter.IntegrationTests` (the same conformance suite on Azurite via `Testcontainers.Azurite` 4.14.0, `Category=Integration`).
- Composition: `services.AddTellmaBlobs(configure)` registers the service, registry, processor and tasks; the host registers exactly one store: `AddFileSystemBlobStore(o => o.RootPath = …)` or `AddAzureBlobStore(o => { o.ServiceUri = …; o.ContainerPrefix = …; }, credential)`. Startup validation fails when no store or two stores are registered, when a kind referenced by an attribute is registered twice or by two properties, when `RootPath` is relative, cross-volume with its `.tmp`, or longer than 160 characters on Windows, and when the container prefix plus `t{tenantId}` cannot be a DNS label.
- Package pins added to `Directory.Packages.props`: `Azure.Storage.Blobs` 12.29.2 (also lifts the identity server's transitive 12.26.0 so the solution has one copy), `SixLabors.ImageSharp` 4.1.1, `Testcontainers.Azurite` 4.14.0 with `Testcontainers.MsSql` moved from 4.13.0 to 4.14.0 in the same change.

### D15 — Telemetry (high)

One meter name `Tellma.Blobs` in `BlobTelemetryNames` (the email precedent: `EmailTelemetryNames.MeterName = "Tellma.Email"` is shared by `Tellma.Core` and the SendGrid adapter). Instruments: `tellma.blobs.uploads` (counter; tags `kind`, `outcome` ∈ `staged | rejected_size | rejected_type | rejected_image | quota | failed`), `tellma.blobs.upload.bytes` (counter, By), `tellma.blobs.upload.duration` (histogram, s), `tellma.blobs.image.duration` (histogram, s), `tellma.blobs.downloads` (counter; `kind`, `outcome` ∈ `served | not_modified | not_found`), `tellma.blobs.store.operations` (counter; `operation` ∈ `write | read | delete | list | purge`, `outcome` ∈ `ok | conflict | not_found | failed`; emitted by both stores), `tellma.blobs.store.duration` (histogram, s; `operation`), `tellma.blobs.sweep.deleted` (counter; `state` ∈ `staged | released`), `tellma.blobs.sweep.failures` (counter). `kind` is a closed set per distribution; no tenant tag. Alert queries under `infra/monitoring/` are cross-checked by the existing test.

### D16 — Naming (medium)

`IBlobStore` (bytes by name; replaces the brain dump's `IBlobService` shape), `IBlobService` (tenant-scoped staging and resolution), `core.Blob`, `Kind`, states `Staged | Committed | Released | Deleting`, `StorageKey`, `ExpiresAt`, `BlobDescriptor`, `BlobReferenceAttribute`, `BlobPreset`, `BlobKindPolicy`, tasks `blob-sweep` and `blob-reconcile`, Core kind `user-image`.

### D17 — Temporal owners keep the reference, not the image (medium)

When a temporal owner (such as `User`) changes `ImageId`, its history row keeps the old id, the old blob is released and swept, and the history row's reference dangles (history tables carry no FK). Edit history therefore shows *that* an image changed, not the old bytes. A kind that must retain replaced blobs is a later policy (`ReleasePolicy.Retain`), not built now.

### D18 — Server-generated blobs use the same path (high)

A background task that produces a file (an export, a generated report) calls `IBlobService.StageAsync` from its job scope under the task's user (the platform's system user for built-in schedules), then saves the owning record — the inbox item or task row that carries a `[BlobReference("export-file")]` property — through the pipeline, which confirms the blob under D9. The recipient downloads through `GET blobs/export-file/{id}` authorized by the owner row. No second write path exists.

## 3. Contracts

### 3.1 `Tellma.Core.Abstractions.Blobs`

```contract
// Bytes by name for one tenant per call. Singleton; writes are create-only.
contract IBlobStore
  WriteAsync(tenantId: int, writes: list<BlobWrite>) -> void
  OpenReadAsync(tenantId: int, name: string) -> BlobContent?
  DeleteAsync(tenantId: int, names: list<string>) -> void
  ListAsync(tenantId: int, prefix: string) -> list<BlobStoreEntry>      // async sequence; tooling only
  PurgeIncompleteAsync(tenantId: int, olderThan: DateTimeOffset) -> void

record BlobWrite(Name: string, ContentType: string, Content: Stream, Length: long)
record BlobContent(Content: Stream, Length: long)                      // disposable; caller disposes
record BlobStoreEntry(Name: string, Length: long, LastModified: DateTimeOffset)

// Object-name grammar {kind}/{k0k1}/{key32}[.{variant}]; validated before any adapter touches storage.
service BlobName                                                       // static helper
  IsValidKind(kind: string) -> bool         sync   // ^[a-z][a-z0-9-]{1,39}$
  IsValidVariant(variant: string) -> bool   sync   // ^[a-z0-9]{1,16}$
  IsValid(name: string) -> bool             sync
  Primary(kind: string, key32: string) -> string   sync
  Variant(primaryName: string, variant: string) -> string   sync

// Tenant-scoped orchestration over IBlobStore and core.Blob. Scoped; tenant and user from the request context.
service IBlobService
  StageAsync(request: BlobStageRequest) -> BlobDescriptor
  ResolveAsync(kind: string, id: int, variant: string?) -> BlobDownload?

record BlobStageRequest(Kind: string, Content: Stream, Length: long, DeclaredContentType: string?, FileName: string?)
record BlobDescriptor(Id: int, Kind: string, ContentType: string, Size: long, Width: int?, Height: int?, FileName: string?, ExpiresAt: DateTimeOffset?)
record BlobDownload(Id: int, Kind: string, StorageName: string, ContentType: string, Size: long, FileName: string?, IsImage: bool, ETag: string)

// Marks an int? property as a reference to core.Blob of one kind; exactly one property in the model per kind.
annotation [BlobReference(kind: string, preset: BlobPreset = Attachment, MaxSizeBytes: long?, ReadAccess: BlobReadAccess = OwnerRead)]   on property

enum BlobPreset = Attachment | Avatar | Photo
enum BlobReadAccess = OwnerRead | AnyMember | Custom
enum ImageFit = Contain | CoverSquare
enum ImageFormat = WebP | Jpeg | Png

record BlobKindPolicy
  MaxSize: long                          required
  AllowedContentTypes: list<string>      required
  Image: ImagePolicy?
  StagingTtl: TimeSpan?
  ReadAccess: BlobReadAccess = OwnerRead
  Attachment: BlobKindPolicy             // static preset
  Avatar: BlobKindPolicy                 // static preset
  Photo: BlobKindPolicy                  // static preset

record ImagePolicy(MaxDimension: int, Fit: ImageFit, ThumbnailSize: int?, Format: ImageFormat = WebP, MaxInputPixels: int = 50000000)

// Decodes, validates, normalises and re-encodes untrusted images. Replaceable.
contract IImageProcessor
  ProcessAsync(input: bytes, policy: ImagePolicy) -> ProcessedImage

record ProcessedImage(Primary: bytes, Width: int, Height: int, Thumbnail: bytes?, ContentType: string)

record BlobTelemetryNames                 // constants: MeterName = "Tellma.Blobs", instrument names, tag keys, closed tag values (D15)
```

Exceptions (all carry a diagnostic code plus arguments; the host localizes): `BlobRejectedException(Code, Arguments)` from `StageAsync` — `Blob_TooLarge` (413), `Blob_UnsupportedType` (415), `Blob_UnknownKind` (404), `Blob_LengthRequired` (411), every other code including `Blob_StagingQuotaExceeded` and `Blob_ImageRejected` (422, property path `Body`); `ImageRejectedException(Code, Arguments)` from `IImageProcessor`, translated to `Blob_ImageRejected`; `BlobAlreadyExistsException(Name)` and `BlobStoreException(Message, Inner)` from stores. `BlobRejectedException` implements the platform exception contract of seam 10 so the web layer maps it without a special case.

| Member | Meaning |
|---|---|
| `IBlobStore.WriteAsync` | Writes every object create-only (`If-None-Match: *` on Azure; `File.Move(overwrite: false)` on disk), in parallel, best-effort: a failure leaves earlier writes in place and throws `BlobAlreadyExistsException` or `BlobStoreException`. |
| `IBlobStore.OpenReadAsync` | Opens one object for streaming or returns null when it does not exist; the caller disposes. |
| `IBlobStore.DeleteAsync` | Deletes every named object with bounded parallelism; a missing object is success. |
| `IBlobStore.ListAsync` | Enumerates objects under a prefix for reconciliation and tenant tooling; never on a request path. |
| `IBlobStore.PurgeIncompleteAsync` | Adapter housekeeping: removes incomplete artefacts (`.part` files) older than the cut-off; a no-op on Azure. |
| `IBlobService.StageAsync` | D6: validates, processes, one DB round trip, create-only store writes; returns the descriptor whose `Id` a record save attaches. Usable from request and job scopes. |
| `IBlobService.ResolveAsync` | D8: one DB round trip through the owner row and the caller's permissions; null means 404. |
| `[BlobReference]` | Declares the kind, the FK to `core.Blob`, the Queryex navigation (`Image` for `ImageId`, `Blob` for `BlobId`, otherwise the property name minus `Id`), the validator, the confirm/release statements, the upload policy (preset plus overrides), the download authorization and the Excel exclusion. |
| `BlobReadAccess` | `OwnerRead`: read permission on the owner resource with its row-level filter (default). `AnyMember`: any active tenant member. `Custom`: a filter supplied at registration replaces the permission filter. |

### 3.2 `Tellma.Core.Blobs`

```contract
data BlobOptions                              // bound from "Blobs"
  StagingTtl: TimeSpan = 24h
  SweepInterval: TimeSpan = 15m
  SweepBatchSize: int = 500
  SweepReclaimAfter: TimeSpan = 60m
  MaxStagedPerUser: int = 200
  MaxStagedBytesPerUser: long = 512 MiB
  MaxUploadSize: long = 100 MiB

data FileSystemBlobStoreOptions
  RootPath: string                            required, absolute

service IBlobKindRegistry
  Find(kind: string) -> BlobKindDescriptor?   sync
  All: list<BlobKindDescriptor>

record BlobKindDescriptor(Kind: string, Policy: BlobKindPolicy, OwnerResource: string, OwnerEntity: string, OwnerTable: string, OwnerColumn: string, CustomReadFilter: FilterTree?)

// Composition (extension methods on the service collection / endpoint builder).
service BlobsComposition
  AddTellmaBlobs(configure: (BlobsBuilder) -> void?)
  AddFileSystemBlobStore(configure: (FileSystemBlobStoreOptions) -> void)
  MapTellmaBlobs()                            // POST blobs/{kind}, GET blobs/{kind}/{id}; called once by the stack's web mapping

service BlobsBuilder
  AddKind(kind: string, policy: BlobKindPolicy) -> BlobsBuilder     // replaces the attribute's preset; owner still from the attribute
  UseImageProcessor<TProcessor>() -> BlobsBuilder                    // where TProcessor: IImageProcessor
```

### 3.3 `Tellma.Connector.AzureBlobs.Adapter`

```contract
data AzureBlobStoreOptions
  ServiceUri: Uri?                            // https://<account>.blob.core.windows.net
  ConnectionString: string?                   // Azurite and local development only
  ContainerPrefix: string = "tellma-"
  MaxParallelism: int = 8

service AzureBlobsComposition
  AddAzureBlobStore(configure: (AzureBlobStoreOptions) -> void, credential: TokenCredential?)   // production passes ManagedIdentityCredential
```

*Illustration* (a distribution adds a profile image to an entity):

```csharp
[BlobReference("product-image", BlobPreset.Photo)]
public int? ImageId { get; set; }
```

### 3.4 Needed from other themes

- **T2 batch and emitter (seam 1).** Statements with `MayRetry` and `WritesTables`, an `IdList` table-valued parameter, a typed reader for the `(Expected, Confirmed)` row, and the emitter capture convention of D9 for every `[BlobReference]` column (INSERT/UPDATE/DELETE, child synchronisation, query-driven deletes). Child rows must be deleted by explicit emitted statements, not DB cascades, or their blobs are only reclaimed by the weekly reconcile.
- **T5 pipeline hook (seam 3).** A per-column contributor: `ISaveContributor<TEntity>` with `RequestContext(SaveContextRequests<TEntity>)`, `Validate(SaveValidation<TEntity>)`, `ContributePersist(PersistBatch<TEntity>)`, plus `ContributeDelete(DeleteBatch<TEntity>)`; errors reported as `(propertyPath, code, arguments)`; the property is marked client-editable with a pipeline-enforced rule; the pipeline reads result sets before commit.
- **T4 permission evaluation (seam 11).** `Evaluate(resource, action) → (Allowed, Filter: FilterTree?)`; for a weak owner the filter of the top-level securable rewritten through the parent navigation.
- **T1 request context (seam 9).** `TenantId: int` and `UserId: int` in request and job scopes.
- **T10 (seam 8).** Built-in schedule registration for `blob-sweep` and `blob-reconcile`: per tenant, overlap policy *skip*, system-user credentials.
- **T6 (seam 13).** The web surface group prefix; the CSRF header on the upload POST; `GET` allowed for `blobs/{kind}/{id}` on the otherwise all-POST surface; exception-to-status entries above; the validation error format for upload rejections.

## 4. Schema

`core.Blob` — non-temporal; one row per uploaded object family; no UDTT (never bulk-saved through the emitter); carries `CreatedAt/CreatedById` only (rows are immutable except for `State`, `CommittedAt`, `ExpiresAt`).

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | PK clustered, sequence `sq_Blob` | app-assigned |
| `Kind` | `nvarchar(40)` | no | | registered kind name, grammar `^[a-z][a-z0-9-]{1,39}$` |
| `StorageKey` | `nvarchar(120)` | no | unique (`UX_Blob_StorageKey`) | full object name of the primary variant, e.g. `user-image/3f/3fa9…` |
| `State` | `nvarchar(10)` | no | CHECK IN (`Staged`, `Committed`, `Released`, `Deleting`) | |
| `ContentType` | `nvarchar(100)` | no | | server-determined MIME type of the primary variant |
| `FileName` | `nvarchar(255)` | yes | | sanitized original name; display and `Content-Disposition` only, never a path |
| `Size` | `bigint` | no | | bytes of the primary variant |
| `Sha256` | `binary(32)` | no | | hash of the stored primary bytes |
| `Width` | `int` | yes | | images only |
| `Height` | `int` | yes | | images only |
| `Variants` | `nvarchar(100)` | no | | comma-separated variant names that exist (`''` for none; `thumb`) |
| `CreatedAt` | `datetime2(3)` | no | | `SYSUTCDATETIME()` at staging |
| `CreatedById` | `int` | no | FK → `core.User(Id)` | the uploader — the only user who may attach it |
| `CommittedAt` | `datetime2(3)` | yes | | set on confirm |
| `ExpiresAt` | `datetime2(3)` | yes | | staging deadline while `Staged`; release time while `Released`; reclaim time while `Deleting`; NULL while `Committed` |

Indexes: `IX_Blob_Expiry` nonclustered `(ExpiresAt) INCLUDE (Kind, StorageKey, Variants, State) WHERE ExpiresAt IS NOT NULL` (sweep claim); `IX_Blob_StagedBy` nonclustered `(CreatedById) INCLUDE (Size) WHERE State = N'Staged'` (quota); `UX_Blob_StorageKey` unique nonclustered `(StorageKey)`.

Owner side, per blob-reference column (e.g. `core.User.ImageId`): `int NULL`, FK → `core.Blob(Id)` `ON DELETE NO ACTION`, nonclustered index on the column (serves the download lookup `WHERE ImageId = @id` and the reconcile anti-join).

Queryex schema: entity `Blob` (source `[core].[Blob]`, key `Id`) with scalar properties `Kind, State, ContentType, FileName, Size, Width, Height, Variants, CreatedAt, CreatedById, CommittedAt, ExpiresAt` — `StorageKey` and `Sha256` are never in the schema — plus a many-to-one navigation from every owner column. `Blob` is not a securable root and is reachable only through navigations. No per-tenant variation, so no cache-key impact.

State machine: `Staged --confirm (save tx)--> Committed --release (save/delete tx, reconcile)--> Released --claim (sweep)--> Deleting --objects deleted--> row gone`; `Staged --ExpiresAt passes--> Deleting --> gone`; `Deleting --reclaim after SweepReclaimAfter--> Deleting`.

## 5. Answers

| Brain-dump question (abridged) | Answer |
|---|---|
| "What is the better approach for the record+blobs pattern?" (staging vs in-memory/multipart) | Staged uploads with the staged id carried in the JSON save (D1, D6, D9). |
| "How do we best store, retrieve, and modify the user profile picture?" | Upload to kind `user-image` (Avatar preset), save `User.ImageId`, render `GET blobs/user-image/{ImageId}` with immutable caching; replace by uploading again and saving the new id; remove by saving `null` (D2, D8, D9). |
| "`ImageFitJson` — here or in a centralized blob metadata table?" | Neither: dropped; crop before upload, the server normalises (D2, D11). Intrinsic facts live in `core.Blob`. |
| "Garbage collect if it remains in staging longer than N days (what period)?" | 24 hours by default, per-kind override, swept every 15 minutes with a claim step (D7, D10). |
| Save step 10 "pre-commit non-transactional side effects e.g. creating blobs" and step 12 "deleting blobs" post-commit | Both removed for blobs: creation precedes the save request; deletion is a transactional state flip plus the sweep (D1, D9, D10). |
| "Azure blob storage utilizes the connector/adapter pattern. Should the FileSystem one utilize the same?" | Both implement `IBlobStore`; only Azure is a connector package; the file-system store lives in `Tellma.Core.Blobs` (D4, D14). |
| "Should every tenant get their own [container]?" | Yes: one container or directory per tenant, one account per distribution (D5). |
| "Should tenantId be a parameter in the interface, or part of the config?" | A parameter on every `IBlobStore` member; the scoped `IBlobService` supplies it from the request context (D4, D6). |
| "Should we extend the TenantRegistry to blob storage connection strings, azure key vault?" | No: one account per distribution addressed by managed identity, containers by tenant id; nothing per tenant to store (D5). |
| "Self service endpoints for updating my profile pic" | Same upload endpoint; the self-service profile save attaches the id under the attach rule (D9). |
| "Every API is bulk shaped" applied to uploads | Single-file raw-body uploads, parallel per file; stores are batch-shaped for writes and deletes (§7). |
| "Your export is ready" — where does the artifact live? | A blob of kind `export-file` staged from the job scope and owned by the inbox item or task row (D18). |
| Packaging (Core vs connector), library licensing, size and type limits, the etag endpoint, the pipeline hook | D14, D11, D12, D8, D9. |

## 6. Seams

1. **Batch abstraction (T2).** Confirm/release statements are not retryable (inside the save transaction); sweep claim and delete are retryable and idempotent; needs `IdList`, the `(Expected, Confirmed)` reader, and the emitter's capture convention (D9, §3.4). The sweep runs autocommit statements, never inside a save transaction.
2. **Entity vs wire shape (T2/T6).** Blob-reference properties are plain `int?` on the entity and on the wire; details payloads may include the navigation's `BlobDescriptor` in the related-entity dictionary so the UI shows file name and size without a second call.
3. **One capability, declared once (T5).** `[BlobReference]` is the declaration; the stack scans it to register the kind, configure EF (FK plus navigation), attach the contributor and exclude the column from Excel. No permission action is added: uploading needs membership, attaching needs the owner's save permission, downloading needs the owner's read permission.
4. **Queryex schema (T2/T3).** `Blob` entity and owner navigations enter the schema without `StorageKey`/`Sha256`; no per-tenant variation. Kinds on weak owners need weak entities as query roots with rewritten filters.
5. **Version tags (T3).** None owned; `core.Blob` is not cacheable; owner tag bumps are the emitter's.
6. **Feature composition (T1).** The blobs feature declares the `Blob` entity, `Requires` the users feature (for the `CreatedById` FK), contributes the two schedules and the endpoint mapping.
8. **Background tasks (T10).** Two built-in per-tenant schedules (`blob-sweep` every 15 minutes, `blob-reconcile` weekly), overlap *skip*, system user, no lease columns on `core.Blob`; export artifacts are blobs (D18).
9. **Request context (T1).** `int` tenant id and `int` user id in request and job scopes; if the tenant id becomes a string, `IBlobStore` changes to match and container naming re-normalises to the DNS alphabet.
10. **Platform exceptions (T5/T6).** `BlobRejectedException` implements the shared contract; status map in §3.1.
11. **Permission evaluation (T4).** `(ownerResource, read) → Allowed + FilterTree` composed into the download query; `AnyMember` bypasses it for kinds that opt in; `Custom` substitutes a registration-time filter.
12. **Blob staging tokens (owned).** The token is the `int` id of a `Staged` row; attach rule: staged, unexpired, right kind, staged by me, used once; TTL 24 hours; consumers (T5 pipeline, T8 UserService self-service, T10 producers) pass it as the blob-reference property value, nothing else.
13. **Wire shapes (T6).** Upload: `POST blobs/{kind}?fileName=`, raw body, `Content-Type`, `Content-Length` required, CSRF header; response `201 BlobDescriptor`. Download: `GET blobs/{kind}/{id}?variant=&download=`; `ETag "{id}"` or `"{id}-{variant}"`, `Cache-Control: private, max-age=31536000, immutable`, 304 on `If-None-Match`, 404 for missing or hidden, 400 for a bad variant. Errors use the platform validation-error format with property path `Body`.
14. **Telemetry (T2 owns the budget).** Uploads count 1 DB call, saves +0, GET 1; blob instruments in D15.
15. **Notification enqueue (T10).** Not used by this theme; T10's "export ready" notification references the blob id.
17. **Vocabulary.** `core.Blob` singular (plural if the seam goes plural); `CreatedAt/CreatedById` only; enum-as-string states.
- **MCP (T6).** The MCP surface needs an `upload_file` tool: base64 for small files (≤ 2 MiB) calling `StageAsync`, and for large files the upload URL and kind for the agent's HTTP client. Recorded as a seam, not built.

## 7. Departures

- **"All I/O is bulkified; save endpoints accept arrays" (Guiding Principles).** The upload endpoint is single-file: bytes are bandwidth-bound, parallel single-file requests are faster and retry per file, and multipart batching would need per-part error envelopes. Store-level writes and deletes stay batch-shaped; a multipart batch upload can be added later without changing the store.
- **"Output caching, response compression" (Guiding Principles).** Neither applies to blob responses: output caching never serves authenticated requests, and images are pre-compressed; HTTP caching is delegated to the browser via `immutable`.
- **Hosting table "Blob Storage account per distribution".** Kept; refined to one container per tenant.
- **Library architecture.** Adds `Tellma.Core.Blobs` (a Core runtime package like `Tellma.Core.Webhooks`) and `Tellma.Connector.AzureBlobs.Adapter` under `src/connector/azure-blobs/`; pins `Azure.Storage.Blobs`, `SixLabors.ImageSharp`, `Testcontainers.Azurite`; bumps `Testcontainers.MsSql`.
- **Save pipeline (brain dump, not ARCHITECTURE.md).** Steps 10 and 12 lose their blob use cases; the pipeline has no pre-commit non-transactional step at all.

## 8. Verification

Relied on from `research/blobs.md` (verified 2026-09-01): `Azure.Storage.Blobs` 12.29.2 with a `net10.0` target, resolving transitively at 12.26.0 through the identity server today; create-only uploads via `IfNoneMatch = ETag.All`; tag writes do not churn ETags but tag queries are eventually consistent and need Data Owner RBAC; lifecycle rules are day-granular, up to 24 hours to start, unsupported on Azurite; Azurite 3.37.0 lacks soft delete, versioning and lifecycle; `Testcontainers.Azurite` 4.14.0 requires `Testcontainers` 4.14.0; container names are 3–63-character DNS labels, unlimited per account, RBAC scoped to a container; Microsoft's guidance against `DefaultAzureCredential` in production; ImageSharp 4.1.1, its Split License clauses (a)/(b), pricing tiers, `Image.Identify`, `DecoderOptions.TargetSize`/`MaxFrames`, allocator caps; SkiaSharp 4.151.1 needing `NativeAssets.Linux.NoDependencies` and lacking an allocation cap; `Results.Stream` implementing 304/412/206 from a supplied ETag with preconditions evaluated inside result execution; RFC 9110 strong/weak validator rules; `Cache-Control: private, max-age, immutable` for immutable-by-id URLs; output caching never serving authenticated requests; Kestrel's 30,000,000-byte default body limit and `IFormFile` spilling above 64 KB; `File.Move` being `rename(2)`/`MoveFileEx` only within one volume; `Path.GetFullPath(path, basePath)` for traversal defence; Windows reserved names and `MAX_PATH`; git's two-hex-character fan-out; the staged-upload precedents (Stripe, Slack abort-on-incomplete, Notion one-hour expiry, Shopify, Drive ≤ 5 MB multipart).

Verified in the repo (2026-09-01): connector packages are named `Tellma.Connector.<Vendor>.Adapter` under `src/connector/<vendor>/` (`acs-email/Tellma.Connector.AcsEmail.Adapter`, `smtp/Tellma.Connector.Smtp.Adapter`); the email meter name `Tellma.Email` is a constant in `Tellma.Core.Abstractions/Email/EmailTelemetryNames.cs` shared by the SendGrid adapter; `Directory.Packages.props` pins `Testcontainers.MsSql` 4.13.0 and neither `Azure.Storage.Blobs`, ImageSharp nor SkiaSharp; `ISandboxContext`/`SandboxContext.Never` exist as described. From the briefing: no triggers and no logic in the database (so `OUTPUT … INTO` is safe), RCSI defaults, `MERGE` is out, one storage account per distribution, the request context is a scoped holder copied into job scopes.

Unverified (prior knowledge or inference): `Azure.Storage.Blobs.Batch`'s `BlobBatchClient.DeleteBlobsAsync` (up to 256 per batch) as an alternative to bounded-parallel single deletes — the adapter ships with parallel deletes; that Storage Blob Data Contributor includes `containers/write` so lazy `CreateIfNotExists` works under the recommended role (otherwise provisioning pre-creates containers); ImageSharp's WebP encoder default quality and the exact EXIF auto-orient API in 4.x; that `EntityTagHeaderValue` requires a pre-quoted tag; that `IHttpMaxRequestBodySizeFeature.MaxRequestBodySize` can be raised in the endpoint before the body is read (documented for endpoints generally, not re-read); that an `UPDATE` under read-committed snapshot evaluates a target-table predicate against the latest committed version after waiting on a row lock (standard SQL Server behaviour, not re-read today) — the sweep's claim relies on it, and the LocalDB suite includes a test that opens a confirming transaction and runs the claim concurrently.

## 9. Review flags

1. **Staging TTL 24 hours vs 1 hour (Notion).** Neither a human nor an agent needs a day; a draft-heavy UI might. The option makes it a deployment choice.
2. **ImageSharp under the Six Labors Split License vs SkiaSharp.** The platform is Apache-2.0 and distributions consume ImageSharp transitively, which reads as Apache-2.0 for both; the pricing page's wording is narrower. Either buy the Boutique licence ($799/yr) to remove doubt or switch the default to SkiaSharp (one package swap behind `IImageProcessor`).
3. **`user-image` read access: `AnyMember` vs `OwnerRead`.** Avatars appear on documents everywhere, so any active member fetching any avatar is the useful default; `OwnerRead` requires read permission on Users. The mechanism supports both; the policy belongs with T4/T8.
4. **Attach guard read in C# before commit vs `THROW` inside the batch.** The design reads `(Expected, Confirmed)` and rolls back, matching the row-level-security post-check. If T2's batch commits inside its own text, the statement ends with `IF @confirmed0 <> @expected0 THROW 51016, N'Blob_NotAttachable', 1;` and the executor maps that error number, the way it maps 2601/2627 to field errors.
5. **Sweep claim state `Deleting` vs a grace window.** The claim adds a fourth state and a reclaim option but is correct regardless of transaction length; a grace window (`ExpiresAt < now − 15 min`) is simpler and correct only while every transaction is shorter than the window.
6. **`Content-Length` required (411) vs accepting chunked uploads.** Required makes the size and quota checks cheap and matches browsers and SDKs; some streaming clients send chunked bodies.
7. **Range processing off in this release.** Turning it on needs seekable store streams (Azure `OpenReadAsync` and `FileStream` both are, not re-verified); resumable downloads of 100 MiB attachments would benefit.
8. **`IBlobStore` vs keeping `IBlobService` as the low-level name** as the brain dump wrote it.
9. **Dropping `ImageFitJson`** (crop before upload). Server-kept originals for re-cropping would be a later kind policy with the original as a variant.
10. **Temporal history does not retain replaced images** (D17). A `Retain` release policy for kinds on temporal owners is the alternative.
11. **`core.Blob` singular vs plural** — follows the vocabulary seam.
12. **`Sha256` kept** (integrity, future dedup, reconciliation) at the cost of hashing every upload; dropping it saves nothing measurable.
13. **Lazy container creation vs provisioning pre-creating containers** — depends on the RBAC fact above.
14. **Erasure latency:** a replaced or deleted image is physically gone within `SweepInterval` (15 minutes), not at commit; lower the interval if a customer needs it.
15. **Raw-body upload with `?fileName=` vs `multipart/form-data`.** Raw bodies are simplest for `fetch(file)` and for agents; multipart is what HTML forms send natively.
16. **`Variants` as a comma-separated list vs a `HasThumbnail bit`.** The list survives future multi-size kinds; the bit is simpler and Queryex-friendlier today.
17. **Per-user staging quota as a soft, write-skew-prone check** vs a unique-index-backed hard limit; the soft check suffices for a limit whose purpose is abuse control.

## 10. Conflicts

1. **T2 (emitter and batch).** Must emit the `OUTPUT … INTO @b{o}_<Table>_<Column>` capture for every `[BlobReference]` column on INSERT, UPDATE, DELETE, child synchronisation and query-driven deletes; must delete children by explicit statements rather than DB cascades (or accept that the weekly reconcile is the only backstop); must provide `IdList`, a `(Expected, Confirmed)` reader, and a transaction model in which the pipeline reads a result set before commit (D9, flag 4).
2. **T2/T4 (Queryex roots).** Kinds on weak owners (`InvoiceAttachment.BlobId`) need weak entities registered as Queryex roots with the top-level securable's filter rewritten through the parent navigation; until then only top-level owners are supported.
3. **T5 (pipeline hook).** The contributor shape in §3.4, the "client-editable with a pipeline-enforced rule" marker for blob-reference properties, and the read-before-commit step shared with the RLS post-check.
4. **T4 (users and permissions).** `User.ImageId` carries `[BlobReference("user-image", BlobPreset.Avatar)]`; the `user-image` read-access policy (flag 3); `core.Blob.CreatedById` is an FK to `core.User` configured by the blobs feature against the model's `User` leaf type; the permission evaluator contract.
5. **T6 (web surface).** `GET` on the all-POST web surface for `blobs/{kind}/{id}`; per-endpoint body-size override for the upload POST; the CSRF header requirement applies to the upload POST; the exception-to-status entries of §3.1; the MCP `upload_file` seam.
6. **T10 (background tasks and inbox).** Two built-in per-tenant schedules with overlap *skip* under the system user; export artifacts are `export-file` blobs owned by the inbox item or task row and staged from the job scope under the task's user (D18); the "export ready" notification links to `GET blobs/export-file/{id}`.
7. **T1 (host and tenancy).** `int` tenant id in `IBlobStore`; the store registration line in the composition root; DNS-label normalisation of the container prefix; container creation and teardown in the provisioning seam; the request context (tenant, user) copied into job scopes.
8. **T8 (Core stacks and migrations).** `core.Blob` and `sq_Blob` are part of the reference distribution's migrations; UserService's self-service image endpoint is an ordinary save of `ImageId`.
9. **T9 (Excel).** `[BlobReference]` columns are excluded from import and export-for-import.
10. **T3 (Queryex schema).** `Blob` enters the schema without `StorageKey`/`Sha256`; no per-tenant gating.
11. **Vocabulary.** Singular `core.Blob`; `CreatedAt/CreatedById` only on immutable rows.
12. **Package pins.** `Testcontainers.MsSql` 4.13.0 → 4.14.0 touches every suite that uses it.

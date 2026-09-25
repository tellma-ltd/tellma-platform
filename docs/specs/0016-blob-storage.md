# Spec: Blob Storage and the Record-plus-Blobs Pattern

- **Author:** Ahmad Akra
- **Date:** 4 September 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

Records and their binary attachments travel in different formats (JSON versus bytes), live in
different places (a SQL table versus an object store), and are read in different ways (a Queryex
query versus a cached, ETag-validated `GET`). A user's profile picture is the first case; product
images, document attachments and generated export files follow the same shape. This spec ships the
platform's one answer to that shape: a blob is uploaded first and staged, the record is saved
through the ordinary JSON save with the staged blob's id in an `int?` column, and the save
transaction confirms the blob. Nothing non-transactional happens inside a save, and physical
deletion belongs to a scheduled sweep.

The design hangs off one table, `core.Blobs`, which holds every intrinsic fact about the bytes
(kind, storage name, state, content type, size, hash, dimensions, uploader, timestamps). Without it
the platform could not answer which staged blobs are orphans, which committed blobs lost their
owner, who uploaded a file, or what content type to serve — and storage listing is the one
operation Azure lifecycle rules, Azurite and the file system all handle differently. Records hold
only the reference.

The spec builds on the frozen platform: table types (spec 0001) for the `IdList` parameter the
sweep binds, the identity server's BFF cookie session (spec 0003 §4) under which the blob endpoints
authenticate, the sandbox context of spec 0007 §3 (which blob stores never consult — writing to the
platform's own storage is not an external side effect), and the Queryex engine (spec 0008) through
which the download's owner check is compiled. It consumes the batch abstraction, emitter and entity
metadata of spec 0011 (`IDataBatch`, `SaveEmitter`, `EntityMetadata`), the pipeline components of
spec 0014 (`IEntityValidator`, `IPersistEffect`, `PersistContext`), the access evaluation of spec
0013 (`IAccessEvaluator`), the request context and endpoint groups of spec 0010 (`RequestContext`,
`TellmaEndpoints`), the job machinery of spec 0019 (`IJobHandler`, `BuiltInSchedule`), and the
problem-details mapping of spec 0015.

Two things are deliberately left out. Blob kinds declared on child entities wait for weak entities
to become securable query roots (spec 0013's `FilterTree.Via`); the emitter's capture shape already
covers children, so the lift is a composition-gate change. A per-kind custom read filter
(`BlobReadAccess.Custom`) is a later addition to the same registration surface. HMAC-signed
download URLs, range requests, a `Retain` release policy for temporal owners, cross-tenant blob
copies and the MCP upload tool are deferred, not built.

## Goals / Non-goals

**Goals**

- Ship the blob contracts in `Tellma.Core.Abstractions`: `IBlobStore` (bytes by name for one
  tenant per call), `IBlobService` (tenant-scoped staging and resolution), `[BlobReference]` with
  its presets and read-access policy, `BlobKindPolicy`, `IImageProcessor`, `BlobName`, the
  `Blob` entity, `BlobRejectedException` and `BlobTelemetryNames`.
- Ship the runtime inside `Tellma.Core`: the `core.Blobs` table and model configuration,
  `BlobService`, the kind registry, the file-system store, the attach validator and the
  confirm/release effect, the `core.blob-sweep` and `core.blob-reconcile` handlers and their
  built-in schedules, the `core.blob-container` provisioning step, options and composition.
- Ship the upload and download endpoints in `Tellma.Core.AspNetCore` on the `Blobs` route group:
  a raw-body single-file `POST` and the platform's one `GET`, ETag-validated and immutable-cached.
- Ship `Tellma.Core.Imaging` (ImageSharp behind `IImageProcessor`, licence-isolated) and
  `Tellma.Connector.AzureBlobs.Adapter` (`AzureBlobStore` over `Azure.Storage.Blobs`), both
  exercised by one abstract store conformance suite.
- Pin the two races by construction: a save confirming a blob the sweep is deleting, and a
  released blob being deleted under an open transaction.

**Non-goals (explicitly out of scope)**

- **Blob kinds on child entities** — wait for weak entities as securable roots (spec 0013's
  reserved `FilterTree.Via`); the composition gate refuses them this release.
- **`BlobReadAccess.Custom`** and a per-kind read filter — a later overload of `BlobsBuilder.Kind`.
- **The `Exports`/`Imports` owner rows, `core.file-retention`, and the export handler that stages
  files** — spec 0018 (entities, handlers) and spec 0019 (retention schedule); they use only
  `IBlobService.StageAsync`, `IBlobStore.OpenReadAsync` and the attach rule defined here.
- **The MCP upload tool** (`tellma_upload`) — reserved by spec 0015.
- **Signed URLs, range requests, a `Retain` release policy, cross-tenant copy, multipart batch
  upload, server-kept originals for re-cropping** — deferred.
- **Excel handling of blob columns** — spec 0018 applies the exclusion this spec declares.

## 1. Placement and architecture

Contract blocks are C# sketches: names and shapes are normative; `using` directives, XML
documentation, cancellation-token parameters, method bodies and accessibility details are omitted,
so a block is never pasted into code. SQL statements are the exact shape to emit.

### 1.1 Projects, namespaces and dependency edges

| Piece | Location | References |
|---|---|---|
| Blob contracts | `Tellma.Core.Abstractions`, namespace `Tellma.Core.Abstractions.Blobs` (`IBlobStore`, `IBlobService`, `BlobName`, `BlobKindPolicy`, `ImagePolicy`, `IImageProcessor`, `Blob`, `BlobState`, `IBlobKindRegistry`, `BlobKindDescriptor`, `BlobRejectedException`, `BlobStoreException`, `BlobAlreadyExistsException`, `BlobTelemetryNames`); namespace `Tellma.Core.Abstractions.Entities` (`[BlobReference]`, `BlobPreset`, `BlobReadAccess`) | `Tellma.Core.Queryex` only (the package's one edge) |
| Runtime | `Tellma.Core`, namespace `Tellma.Core.Blobs`: `BlobService`, `BlobKindRegistry`, `BlobConfiguration` (the EF configuration of `core.Blobs`), `FileSystemBlobStore`, `BlobReferenceValidator<T>`, `BlobReferenceEffect<T>`, `BlobSweepHandler`, `BlobReconcileHandler`, `BlobContainerStep`, `BlobOptions`, `FileSystemBlobStoreOptions`, `BlobsBuilder`, `BlobsComposition` | as `Tellma.Core`; never `Tellma.Core.Imaging` or `SixLabors.ImageSharp` |
| Imaging | `src/core/Tellma.Core.Imaging/`, namespace `Tellma.Core.Imaging`: `ImageSharpImageProcessor`, `ImagingComposition.AddTellmaImageSharp` | `Tellma.Core.Abstractions` + `SixLabors.ImageSharp` 4.1.1 only |
| Endpoints | `Tellma.Core.AspNetCore`: `MapTellmaBlobs` on `TellmaEndpoints.Blobs` | `Tellma.Core` + ASP.NET Core |
| Azure adapter | `src/connector/azure-blobs/Tellma.Connector.AzureBlobs.Adapter/`, namespace `Tellma.Connector.AzureBlobs`: `AzureBlobStore`, `AzureBlobStoreOptions`, `AzureBlobsComposition.AddAzureBlobStore` | `Tellma.Core.Abstractions` + `Azure.Storage.Blobs` 12.29.2 |
| Tests | `test/core/Tellma.Core.Tests/Blobs/`, `test/core/Tellma.Core.IntegrationTests/Blobs/`, `test/core/Tellma.Core.Imaging.Tests/`, `test/connector/azure-blobs/Tellma.Connector.AzureBlobs.Adapter.IntegrationTests/` | §11 |

- **Licence isolation.** `Tellma.Core.Imaging` is the only package that references ImageSharp.
  The reference distribution obtains it through `Tellma.Defaults.Azure`, whose `UseAzureDefaults()`
  calls `AddTellmaImageSharp()` (spec 0010 §1.3); a distribution that prefers another library
  registers any `IImageProcessor` through `tellma.Blobs(b => b.ImageProcessor<T>())` and drops the
  package. `Tellma.Core` compiles and runs without it; the realised startup gate fails only when an
  image-kind blob exists and no processor is registered.
- **Adapter-only rule.** Azure has a maintained first-party client, so it is a connector adapter
  under `src/connector/azure-blobs/`, following the existing connector layout. The file system has
  nothing to adapt and ships inside `Tellma.Core`.
- **Package pins** added to `Directory.Packages.props`: `Azure.Storage.Blobs` 12.29.2 (also lifts
  the identity server's transitive 12.26.0 so the solution carries one copy), `SixLabors.ImageSharp`
  4.1.1 and `Testcontainers.Azurite` 4.14.0, which requires `Testcontainers` 4.14.0, the line of the
  `Testcontainers.MsSql` pin of spec 0010 §1.1 and spec 0011 §1.2.

### 1.2 The flow in one picture

```
upload (raw body)  ──►  BlobService.StageAsync ──► core.Blobs row (Staged, ExpiresAt) ──► IBlobStore.WriteAsync (create-only)
                                                          │
save (JSON, ImageId = staged id) ──► validator (attach rule) ──► persist tx: emitter capture ──► confirm / release
                                                          │
GET blobs/{kind}/{id} ──► ResolveAsync (owner row + caller's Read filter) ──► IBlobStore.OpenReadAsync ──► immutable
                                                          │
core.blob-sweep (every 15 min) ──► claim expired Staged / Released rows as Deleting ──► delete objects ──► delete rows
```

Cost, common case (database round trips counted, storage calls named):

| Operation | DB | Storage |
|---|---|---|
| Upload (stage) | 1 | 1–2 create-only writes (primary, thumbnail), parallel |
| Save that attaches, replaces or removes a blob | +0 over a plain save | 0 |
| Delete by ids, by query, with descendants | +0 | 0 |
| `GET` on a cache miss | 1 (two statements) | 1 read |
| `GET` with a matching `If-None-Match` | 1 | 0 |
| Sweep | 2 per batch of ≤ 500 rows | ≤ 500 × variants deletes, bounded parallelism |

### 1.3 Composition

```csharp
// Tellma.Core.Blobs (runtime)
public sealed class BlobOptions                             // bound from Tellma:Blobs
{
    public TimeSpan StagingTtl { get; set; } = TimeSpan.FromHours(24);
    public int SweepBatchSize { get; set; } = 500;
    public TimeSpan SweepReclaimAfter { get; set; } = TimeSpan.FromMinutes(60);
    public int MaxStagedPerUser { get; set; } = 200;
    public long MaxStagedBytesPerUser { get; set; } = 512L * 1024 * 1024;   // 512 MiB
    public long MaxUploadSize { get; set; } = 100L * 1024 * 1024;           // 100 MiB
}

public sealed class FileSystemBlobStoreOptions
{
    public string RootPath { get; set; }                    // required; absolute
}

public static class BlobsComposition
{
    public static void AddFileSystemBlobStore(
        this IServiceCollection services, Action<FileSystemBlobStoreOptions> configure);
}

public sealed class BlobsBuilder                            // tellma.Blobs(b => …)
{
    public BlobsBuilder Kind(string kind, BlobKindPolicy policy);
    public BlobsBuilder ImageProcessor<TProcessor>() where TProcessor : IImageProcessor;
}

// Tellma.Core.Imaging
public static class ImagingComposition
{
    public static void AddTellmaImageSharp(this IServiceCollection services);
}

// Tellma.Connector.AzureBlobs
public sealed class AzureBlobStoreOptions
{
    public Uri? ServiceUri { get; set; }                    // https://<account>.blob.core.windows.net
    public string? ConnectionString { get; set; }           // Azurite and local development only
    public string ContainerPrefix { get; set; } = "tellma-";
    public int MaxParallelism { get; set; } = 8;
}

public static class AzureBlobsComposition
{
    public static void AddAzureBlobStore(
        this IServiceCollection services,
        Action<AzureBlobStoreOptions> configure,
        TokenCredential? credential);
}
```

| Member | Meaning |
|---|---|
| `BlobsBuilder.Kind` | Replaces the policy a `[BlobReference]` preset produced for `kind` wholesale; the owner still comes from the attribute. A kind no attribute declares is a composition problem. |
| `BlobsBuilder.ImageProcessor<T>` | Registers `T` as the `IImageProcessor`; the alternative to `AddTellmaImageSharp`. Registering both is a composition problem. |
| `AddFileSystemBlobStore` / `AddAzureBlobStore` | Register the one `IBlobStore` singleton; both or neither is a composition problem. `AddAzureBlobStore` never constructs a credential: production passes a `ManagedIdentityCredential`, local development a connection string for Azurite. |
| `FeatureContribution.BlobKind(kind, policy)` | The feature-level form of `BlobsBuilder.Kind` (spec 0010's `BlobKindContributionItem`); a pack registers a policy for a kind its entity declares. |

The blob feature is part of `CoreFeature` (spec 0010's `ITellmaFeature`): it contributes the
`Blob` model configuration, the two validators/effects per `[BlobReference]` column, the two job
handlers, the two built-in schedules (§7.4), the provisioning step (§6.4) and the startup check
(§9). The kinds shipped by Core are declared by attributes on Core entities: `user-image`
(`User.ImageId`, spec 0013; `Avatar`, `AnyMember`), `user-signature` (`User.SignatureId`, spec
0013; `Photo`, `OwnerRead`), `export-file` (`Export.FileId`, spec 0018), `import-file` and
`import-result` (`Import.FileId`/`ResultFileId`, spec 0018), the last three
`Attachment` with `OwnerRead`.

*Illustration* (a distribution that composes the store and processor itself, instead of the Azure
bundle):

```csharp
builder.Services.AddTellmaImageSharp();
builder.Services.AddFileSystemBlobStore(o => o.RootPath = builder.Configuration["Tellma:Blobs:FileSystem:RootPath"]!);
builder.AddTellma("<slug>", t => t.Blobs(b => b.Kind("product-image", BlobKindPolicy.Photo with { MaxSize = 4 * 1024 * 1024 })));
```

## 2. The model

### 2.1 Table `core.Blobs`

Non-temporal; no UDTT (never saved through the emitter); sequence `core.sq_Blobs`; rows are
immutable except `State`, `CommittedAt` and `ExpiresAt`.

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | `PK_Blobs` clustered | app-assigned from `core.sq_Blobs` inside the stage statement (§3.4) |
| `Kind` | `nvarchar(40)` | no | | registered kind; grammar `^[a-z][a-z0-9-]{1,39}$` |
| `StorageKey` | `nvarchar(120)` | no | `UX_Blobs_StorageKey` | full object name of the primary variant, e.g. `user-image/3f/3fa9…`; never client-visible |
| `State` | `varchar(9)` | no | | `Staged`, `Committed`, `Released`, `Deleting` |
| `ContentType` | `nvarchar(100)` | no | | server-determined MIME type of the primary variant |
| `FileName` | `nvarchar(255)` | yes | | sanitised original name; display and `Content-Disposition` only, never a path |
| `Size` | `bigint` | no | | bytes of the stored primary |
| `Sha256` | `binary(32)` | no | | hash of the stored primary bytes |
| `Width` | `int` | yes | | images only |
| `Height` | `int` | yes | | images only |
| `Variants` | `nvarchar(100)` | no | | comma-separated variant names that exist; `''` for none; `thumb` today |
| `CreatedAt` | `datetimeoffset(3)` | no | | `SYSUTCDATETIME()` at staging |
| `CreatedById` | `int` | no | `FK_Blobs_CreatedById → core.Users(Id)` NO ACTION | the uploader — the only user who may attach it |
| `CommittedAt` | `datetimeoffset(3)` | yes | | set on confirm |
| `ExpiresAt` | `datetimeoffset(3)` | yes | | staging deadline while `Staged`; release time while `Released`; reclaim time while `Deleting`; `NULL` while `Committed` |

Indexes: `IX_Blobs_Expiry (ExpiresAt) INCLUDE (Kind, StorageKey, Variants, State) WHERE ExpiresAt IS
NOT NULL` (the sweep claim); `IX_Blobs_StagedBy (CreatedById) INCLUDE (Size) WHERE State = 'Staged'`
(the quota); `UX_Blobs_StorageKey (StorageKey)`. No version tag: `core.Blobs` is never cached,
carries no tag attribute and never bumps one. The migrator ships the table and sequence with the
reference distribution's migrations; `core.Blobs` follows the N−1 schema-evolution rule of spec 0011
like every platform table.

State machine:

```
Staged ──confirm (save tx)──► Committed ──release (save/delete tx, reconcile)──► Released ──claim (sweep)──► Deleting ──objects deleted──► row gone
Staged ──ExpiresAt passes──► Deleting ──► row gone          Deleting ──SweepReclaimAfter passes──► Deleting (re-claimed)
```

### 2.2 The `Blob` entity

```csharp
// Tellma.Core.Abstractions.Blobs
public sealed class Blob : Entity<int>   // system-written; table core.Blobs; never saved through IDataBatch.Save
{
    public string Kind { get; set; }
    public BlobState State { get; set; }
    public string ContentType { get; set; }
    public string? FileName { get; set; }
    public long Size { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string Variants { get; set; }
    public DateTimeOffset CreatedAt { get; set; }           // datetimeoffset(3); server-owned
    public int CreatedById { get; set; }                    // server-owned; FK -> core.Users
    public DateTimeOffset? CommittedAt { get; set; }        // datetimeoffset(3)
    public DateTimeOffset? ExpiresAt { get; set; }          // datetimeoffset(3)
}

public enum BlobState { Staged, Committed, Released, Deleting }
```

- **Not a stack.** `Blob` is contributed as a model configuration only
  (`contribution.Model<BlobConfiguration>()`), never as `contribution.Entity<Blob>()`: it has no
  securable, no endpoints, no service. It is reachable only through the navigation every
  `[BlobReference]` column defines.
- **Queryex.** Entity `core.Blob` (source `[core].[Blobs]`, key `Id`) with the scalar properties
  listed above; `StorageKey` and `Sha256` are never in the schema. Each owner column adds a
  many-to-one navigation named after the property minus its `Id` suffix (`ImageId` → `Image`,
  `FileId` → `File`, `ResultFileId` → `ResultFile`), exactly as spec 0011 derives every navigation
  from an FK. No per-tenant variation, so no schema-key impact.
- **Related projection.** `Blob` carries
  `[RelatedSelect("Id,Kind,ContentType,FileName,Size,Width,Height")]` so a details read (spec 0014)
  returns file name and size beside the owner without a second call, and never `CreatedById`.

### 2.3 `[BlobReference]`, presets and read access

```csharp
// Tellma.Core.Abstractions.Entities
public sealed class BlobReferenceAttribute(                 // on an int? property of a top-level entity
    string Kind, BlobPreset Preset = BlobPreset.Attachment, long? MaxSizeBytes = null,
    BlobReadAccess ReadAccess = BlobReadAccess.OwnerRead) : Attribute;

public enum BlobPreset { Attachment, Avatar, Photo }

public enum BlobReadAccess { OwnerRead, AnyMember }
```

| Member | Meaning |
|---|---|
| `Kind` | The blob kind the property references; exactly one property in the model per kind; grammar `^[a-z][a-z0-9-]{1,39}$`. |
| `Preset` | The `BlobKindPolicy` preset the kind starts from; `MaxSizeBytes` narrows `MaxSize` and `ReadAccess` sets the download policy without host code; `BlobsBuilder.Kind` replaces the whole policy. |
| `ReadAccess` | `OwnerRead`: the caller must pass the owner resource's `Read` grant with its row-level filter (default). `AnyMember`: any active member of the tenant. |

The attribute is the single declaration of the capability. From it the platform derives: the
column shape (`int NULL`, `FK_<Table>_<Column> → core.Blobs(Id)` NO ACTION, `IX_<Table>_<Column>`,
per spec 0011's standard column sets), the Queryex navigation, `EntityMetadata.BlobReferences`
(spec 0011's `BlobReferenceMetadata(Property, Kind, Policy)`), the kind registration, the attach
validator and the confirm/release effect (§4), the download authorisation (§5), and the Excel
exclusion (spec 0018). The property's ownership is `Editable`: the client writes it, the validator
enforces the attach rule. No permission action is added — uploading needs membership, attaching
needs the owner's `Save` grant, downloading needs the owner's `Read` grant.

### 2.4 Kind policies

```csharp
// Tellma.Core.Abstractions.Blobs
public sealed record BlobKindPolicy
{
    public long MaxSize { get; init; }                                // required
    public IReadOnlyList<string> AllowedContentTypes { get; init; }   // required
    public ImagePolicy? Image { get; init; }
    public TimeSpan? StagingTtl { get; init; }                        // null = BlobOptions.StagingTtl
    public BlobReadAccess ReadAccess { get; init; } = BlobReadAccess.OwnerRead;
    public static BlobKindPolicy Attachment { get; }                  // preset
    public static BlobKindPolicy Avatar { get; }                      // preset
    public static BlobKindPolicy Photo { get; }                       // preset
}

public sealed record ImagePolicy(
    int MaxDimension, ImageFit Fit, int? ThumbnailSize, ImageFormat Format = ImageFormat.WebP,
    int MaxInputPixels = 50000000);

public enum ImageFit { Contain, CoverSquare }

public enum ImageFormat { WebP, Jpeg, Png }

public interface IBlobKindRegistry                                    // singleton; built at startup
{
    BlobKindDescriptor? Find(string kind);
    IReadOnlyList<BlobKindDescriptor> All { get; }
}

public sealed record BlobKindDescriptor(
    string Kind, BlobKindPolicy Policy, string OwnerResource, TableName OwnerTable,
    string OwnerColumn);
```

Presets:

| Preset | `MaxSize` | `AllowedContentTypes` | `Image` |
|---|---|---|---|
| `Attachment` | 100 MiB | `application/pdf`; the three OOXML types (`…wordprocessingml.document`, `…spreadsheetml.sheet`, `…presentationml.presentation`); `image/jpeg`, `image/png`, `image/webp`, `image/gif`; `text/plain`, `text/csv`, `application/json`; `application/zip` | none |
| `Avatar` | 10 MiB (input) | `image/jpeg`, `image/png`, `image/webp`, `image/gif` | `MaxDimension 512`, `CoverSquare`, `ThumbnailSize 96`, `WebP` |
| `Photo` | 10 MiB (input) | the same four image types | `MaxDimension 1600`, `Contain`, `ThumbnailSize 256`, `WebP` |

`BlobKindRegistry` is built once at startup from every `[BlobReference]` property in the realised
EF model (`OwnerResource` is the stack descriptor's `Resource`, the owner's entity name; the owner
table and column come from the model) overlaid with `BlobsBuilder.Kind` /
`FeatureContribution.BlobKind` replacements. Its checks report into the realised gate (§9).

## 3. Staging: the upload

### 3.1 The service

```csharp
// Tellma.Core.Abstractions.Blobs
public interface IBlobService   // scoped; tenant and user from RequestContext; usable in request and job scopes
{
    Task<BlobDescriptor> StageAsync(BlobStageRequest request);
    Task<BlobDownload?> ResolveAsync(string kind, int id, string? variant);
}

public sealed record BlobStageRequest(
    string Kind, Stream Content, long Length, string? DeclaredContentType, string? FileName);

public sealed record BlobDescriptor(
    int Id, string Kind, string ContentType, long Size, int? Width, int? Height, string? FileName,
    DateTimeOffset? ExpiresAt);

public sealed record BlobDownload(
    int Id, string Kind, BlobState State, string StorageName, string ContentType, long Size,
    string? FileName, bool IsImage, string ETag);
```

| Member | Meaning |
|---|---|
| `StageAsync` | §3.2–§3.5: validates, processes, one database round trip, create-only store writes; returns the descriptor whose `Id` a record save attaches. Requires `RequestContext.UserId`; a job scope stages under its run-as user. |
| `ResolveAsync` | §5.1: one database round trip through the owner row and the caller's grant; `null` means 404. |
| `BlobDescriptor.ExpiresAt` | The staging deadline; `null` once committed. Serialised camel-cased like every wire record of spec 0015. |
| `BlobDownload.State` | `Staged` or `Committed` — the endpoint chooses the cache policy from it (§5.3). |

### 3.2 The stage flow

`StageAsync` runs these steps in order; every rejection is a `BlobRejectedException` (§8.2) and
leaves nothing behind.

1. **Resolve the kind** through `IBlobKindRegistry`; unknown → `Blob.UnknownKind`. Validate the
   file name: trimmed, at most 255 characters, path separators and control characters replaced by
   `_`, a bare `.`/`..` replaced by `file`; the result is display-only.
2. **Check the length.** `Length` is required (`Blob.LengthRequired` when the caller passes a
   negative or unknown length; the endpoint maps a missing `Content-Length` to it) and must not
   exceed `min(policy.MaxSize, BlobOptions.MaxUploadSize)` (`Blob.TooLarge`). The limit is enforced
   again while reading: a body that runs past the declared length is `Blob.TooLarge`.
3. **Read the bytes.** Image kinds are buffered in memory (bounded by the policy's `MaxSize`).
   Attachment kinds above 64 KiB stream to a temporary file under spec 0010's `Tellma:ScratchPath`
   (default the operating system's temp directory), opened delete-on-close, while SHA-256 is
   computed on the way; smaller bodies stay in memory.
4. **Determine the content type** (§3.3); outside the allowed set → `Blob.UnsupportedType`.
5. **Process images** (§3.6) for kinds whose policy carries an `Image`; the stored primary is the
   processor's output, never the client's bytes; failures → `Blob.ImageRejected`. The hash, size,
   dimensions and content type recorded are those of the bytes that will be stored.
6. **Generate the storage key**: 16 random bytes (`RandomNumberGenerator`) as lowercase hex
   (`key32`); `StorageKey = BlobName.Primary(kind, key32)`; the thumbnail is
   `BlobName.Variant(StorageKey, "thumb")`.
7. **One database round trip** (§3.4): the soft per-user quota check and the insert of the
   `Staged` row with its id taken from `core.sq_Blobs` inside the statement. Quota exceeded →
   `Blob.StagingQuotaExceeded`.
8. **Write the objects** through `IBlobStore.WriteAsync` (primary and thumbnail in one call,
   create-only). On failure the row stays `Staged` and expires into the sweep; the caller receives
   the store's exception as a 500 and never sees the id.
9. **Return** the `BlobDescriptor`.

Row before bytes: every object in storage has a row, and a failed write leaves a row without
bytes that only the sweep touches (a missing object is success for the sweep's delete).

### 3.3 Content-type determination

- **Sniffed formats** take the sniffed type regardless of the declaration: JPEG (`FF D8 FF`), PNG,
  GIF (`GIF87a`/`GIF89a`), WebP (`RIFF…WEBP`), PDF (`%PDF-`). A `PK` archive takes the declared type
  when it is one of the three OOXML types or `application/zip`, else `application/zip` when that
  is allowed, else rejected.
- **Text types** (`text/plain`, `text/csv`, `application/json`) take the declared type only when
  it is in the allowed set and the first 8 KiB contain no NUL byte.
- **Always rejected**, whatever the declaration or the allowed set: executables (`MZ`, ELF
  `7F 45 4C 46`, Mach-O), HTML (a leading `<!DOCTYPE html` or `<html` after whitespace), SVG
  (`<svg` or `<?xml` followed by `<svg` in the first 8 KiB). Anything not sniffable and not a
  permitted text type is `Blob.UnsupportedType`.
- The declared type is parsed as a media type without parameters; casing is ignored.

### 3.4 The stage statement

One `Persist` batch through spec 0013's `IGuardedBatchRunner` (the connect prologue rides it; the
transaction is the batch's own; the statement declares `Writes = { core.Blobs }`, `Idempotent =
false`). Parameters, ordinal `b`: `@tb{b}_p0` user id, `@tb{b}_p1` kind, `@tb{b}_p2` storage key,
`@tb{b}_p3` content type, `@tb{b}_p4` file name, `@tb{b}_p5` size, `@tb{b}_p6` SHA-256, `@tb{b}_p7`
width, `@tb{b}_p8` height, `@tb{b}_p9` variants, `@tb{b}_p10` TTL seconds, `@tb{b}_p11`
`MaxStagedPerUser`, `@tb{b}_p12` `MaxStagedBytesPerUser`.

```sql
DECLARE @tb{b}_count int, @tb{b}_bytes bigint;
SELECT @tb{b}_count = COUNT(*), @tb{b}_bytes = ISNULL(SUM([Size]), 0)
FROM [core].[Blobs] WHERE [CreatedById] = @tb{b}_p0 AND [State] = 'Staged';
IF @tb{b}_count >= @tb{b}_p11 OR @tb{b}_bytes + @tb{b}_p5 > @tb{b}_p12
    THROW 50422, N'Blob.StagingQuotaExceeded', 1;
DECLARE @tb{b}_now datetimeoffset(3) = SYSUTCDATETIME();
DECLARE @tb{b}_exp datetimeoffset(3) = DATEADD(second, @tb{b}_p10, @tb{b}_now);
DECLARE @tb{b}_id int = NEXT VALUE FOR [core].[sq_Blobs];
INSERT INTO [core].[Blobs] ([Id], [Kind], [StorageKey], [State], [ContentType], [FileName], [Size], [Sha256],
    [Width], [Height], [Variants], [CreatedAt], [CreatedById], [CommittedAt], [ExpiresAt])
VALUES (@tb{b}_id, @tb{b}_p1, @tb{b}_p2, 'Staged', @tb{b}_p3, @tb{b}_p4, @tb{b}_p5, @tb{b}_p6,
    @tb{b}_p7, @tb{b}_p8, @tb{b}_p9, @tb{b}_now, @tb{b}_p0, NULL, @tb{b}_exp);
SELECT @tb{b}_id AS [Id], @tb{b}_exp AS [ExpiresAt];   -- result set: the descriptor's id and deadline
```

- **Id inside the statement.** `Blob` is a system-written row like `Job` and `Notification`; its
  id comes from the sequence in the same statement, never from the allocator's buffer, so staging
  has no dependency on `IIdAllocator` and cannot leave a reserved id unused.
- **The quota is soft.** The count and sum are read without locks; two concurrent uploads may
  both pass one slot below the limit. The limit exists for abuse control, not accounting, and a
  unique-index-backed hard limit is not built. The `50422` assertion is translated by
  `BlobService` into `BlobRejectedException(Blob.StagingQuotaExceeded)` (spec 0011 surfaces every
  `50422` as `BatchAssertionFailedException` with the message as the code).
- **TTL.** `@tb{b}_p10` is the kind's `StagingTtl` or `BlobOptions.StagingTtl` (24 h).

### 3.5 Limits

| Limit | Where | Value |
|---|---|---|
| Body size | `AcceptsBinary(BlobOptions.MaxUploadSize)` on the upload endpoint raises Kestrel's per-request cap for that endpoint only; the JSON endpoints keep spec 0015's `MaxJsonBodyBytes` | 100 MiB |
| Per-kind size | `BlobKindPolicy.MaxSize`, narrowed by `[BlobReference].MaxSizeBytes` | preset |
| Staged rows per user | `BlobOptions.MaxStagedPerUser` | 200 |
| Staged bytes per user | `BlobOptions.MaxStagedBytesPerUser` | 512 MiB |
| Image input pixels | `ImagePolicy.MaxInputPixels` | 50,000,000 |
| Image decode memory | process-wide allocator: 256 MB per allocation, 512 MB total | fixed |
| File name | 255 characters after sanitisation | fixed |

### 3.6 Image processing

```csharp
// Tellma.Core.Abstractions.Blobs
public interface IImageProcessor   // decodes, validates, normalises and re-encodes untrusted images; replaceable
{
    Task<ProcessedImage> ProcessAsync(byte[] input, ImagePolicy policy);
}

public sealed record ProcessedImage(
    byte[] Primary, int Width, int Height, byte[]? Thumbnail, string ContentType);

// Tellma.Core.Imaging
public sealed class ImageSharpImageProcessor : IImageProcessor;

public sealed class ImageRejectedException(
    string Code, IReadOnlyDictionary<string, object?> Arguments) : Exception;
```

`ImageSharpImageProcessor` runs, in order: identify the format (JPEG, PNG, WebP and GIF accepted;
anything else → `ImageRejectedException`, translated to `Blob.ImageRejected`); read the header
(`Image.Identify`) and reject when `width × height > MaxInputPixels`; decode the first frame only
with a target size no larger than the policy's `MaxDimension` under a process-wide memory
allocator capped at 256 MB per allocation and 512 MB in total; auto-orient from EXIF; strip every
metadata block; resize — `Contain` fits within `MaxDimension × MaxDimension` without upscaling,
`CoverSquare` centre-crops to a square and scales to `MaxDimension`; encode the primary in the
policy's `Format` (`image/webp`, `image/jpeg` or `image/png` becomes the stored `ContentType`);
encode the thumbnail the same way at `ThumbnailSize` when set (`CoverSquare` thumbnails are
square; `Contain` thumbnails keep the aspect ratio). Re-encoding is a security control — polyglot
files, embedded metadata and decompression bombs never reach storage — not a feature. Fit and
crop are presentation choices applied before storage (the browser crops for humans, `CoverSquare`
for everything else); no fit metadata is stored.

`ImageRejectedException(Code, Arguments)` is internal to the processor packages; `BlobService`
translates it. The realised gate fails startup when any kind whose policy has an `Image` exists
and no `IImageProcessor` is registered.

## 4. Attaching: the record-plus-blobs save

### 4.1 The attach rule

A blob-reference property is client-editable under one rule. A **newly attached** value — a
non-null value on an inserted row, or a changed non-null value on an updated row (spec 0014's
`SaveContext.IsNew` and `Changed`) — must identify a blob that is `Staged`, unexpired, of the
property's declared kind, staged by the saving user (`CreatedById = RequestContext.UserId`), and
used at most once across the whole payload. An unchanged value is untouched; `null` on an updated
row releases the previous blob; a deleted row releases its blob. The token *is* the staged id:
guessing an id is useless because only its uploader can attach it, and only once.

The rule holds on every save path without exception: the details page save, the `me/save` of spec
0017 (`ImageId` and `SignatureId` are self-editable columns of that spec's rule, and the caller is
the uploader), an agent save through MCP, an enlisted save from a job frame (spec 0014 §13.3; the
run-as user staged the blob), and an import — where spec 0018 excludes blob columns from the
sheet, so hydration leaves them unchanged and nothing attaches.

### 4.2 The validator

`BlobReferenceValidator<TEntity>` is an `IEntityValidator<TEntity>` (spec 0014) registered by
`CoreFeature` once per `[BlobReference]` column of every stack whose entity declares one; a
validator registered for a default entity runs for the distribution's leaf.

- **Context load.** For every newly attached id in the payload, `context.Loader.ByIds<Blob,
  int>(ids, "Id,State,Kind,CreatedById,ExpiresAt")` is declared before the validator's `LoadAsync`,
  so it rides the validation round's batch; the loader deduplicates by structural key, so several
  columns of several kinds share one statement.
- **`Blob.DuplicateReference`** at the path of every occurrence after the first when one id is
  newly attached more than once in the payload (any row, any column).
- **`Blob.NotAttachable`** at the property path when the id is absent, or its row is not
  `Staged`, or `Kind` differs from the property's kind, or `CreatedById` is not the caller, or
  `ExpiresAt` is not later than `RequestContext.Now`.
- **Delete validation** adds nothing: a released blob needs no check.

Both codes are members of spec 0014's `ValidationCodes`. The validator never touches storage.

### 4.3 The emitter's capture

Spec 0011's `SaveEmitter` declares, for every `[BlobReference]` column of a table it writes,
`@tb{b}_blob_<Table>_<Column> TABLE ([RowId] int NOT NULL, [OldBlobId] int NULL, [NewBlobId] int
NULL)` before the table's statements and fills it from every `INSERT`, `UPDATE` and `DELETE` it
emits for that table — root statements, child synchronisation, delete by ids, by query and with
descendants — with `OUTPUT inserted.[Id], NULL, inserted.[<Column>]` on insert, `OUTPUT
inserted.[Id], deleted.[<Column>], inserted.[<Column>]` on update and `OUTPUT deleted.[Id],
deleted.[<Column>], NULL` on delete (through the per-statement `@tb{b}_cap{n}` table when the
statement also feeds an id set). Old values therefore come from the row as it was at write time,
never from memory: an override save releases what was actually on the row. `OUTPUT … INTO` is safe
because the platform has no triggers. The capture table's ordinal `{b}` is the ordinal of the
table's save statement — the same ordinal `PersistContext.AffectedIds` carries — so
`BlobReferenceEffect<T>` forms the identifier deterministically:

```csharp
// Tellma.Core.Blobs
public static class BlobCaptureName
{
    public static SqlIdentifier For(
        SqlIdentifier savedIds,
        TableName table,
        string column);         // @tb{b}_saved -> @tb{b}_blob_<Table>_<Column>
}
```

### 4.4 The effect

`BlobReferenceEffect<TEntity>` is an `IPersistEffect<TEntity>` registered beside the validator. Its
`ContributeAsync(PersistContext)` appends, per `[BlobReference]` column of the entity's table, the
two statements below through `PersistContext.Batch.Sql` with `Writes = { core.Blobs }` and
`Idempotent = false`; `AfterCommitAsync` does nothing. Spec 0014's
`IPersistEffect<T>.ContributeAsync` rule runs it for every `Persist` batch on the stack's table — a
save, a delete by ids, by query or with descendants, `activate`/`deactivate` and every action — so a
delete releases exactly what its `OUTPUT deleted` captured; on a delete persist
`PersistContext.Entities` is empty and `AffectedIds` names the deleted key table, and the statements
depend only on the capture table. Parameters, under the effect statement's own ordinal `b` (spec
0011 §5.4): `@tb{b}_p0` the column's kind, `@tb{b}_p1` the caller's user id
(`RequestContext.UserId`); `{s}` below is the save statement's ordinal, which `BlobCaptureName.For`
derives from `AffectedIds` (§4.3). Text for `core.Users.ImageId`:

```sql
-- release blobs the change dereferenced (old value from OUTPUT, never from memory)
UPDATE b SET [State] = 'Released', [ExpiresAt] = SYSUTCDATETIME()
FROM [core].[Blobs] AS b INNER JOIN @tb{s}_blob_Users_ImageId AS c ON c.[OldBlobId] = b.[Id]
WHERE (c.[NewBlobId] IS NULL OR c.[NewBlobId] <> c.[OldBlobId]) AND b.[State] = 'Committed';
-- confirm newly attached blobs; kind, uploader and expiry re-checked inside the transaction
DECLARE @tb{b}_expected int = (SELECT COUNT(*) FROM @tb{s}_blob_Users_ImageId
                               WHERE [NewBlobId] IS NOT NULL AND ([OldBlobId] IS NULL OR [OldBlobId] <> [NewBlobId]));
UPDATE b SET [State] = 'Committed', [CommittedAt] = SYSUTCDATETIME(), [ExpiresAt] = NULL
FROM [core].[Blobs] AS b INNER JOIN @tb{s}_blob_Users_ImageId AS c ON c.[NewBlobId] = b.[Id]
WHERE (c.[OldBlobId] IS NULL OR c.[OldBlobId] <> c.[NewBlobId])
  AND b.[State] = 'Staged' AND b.[Kind] = @tb{b}_p0 AND b.[CreatedById] = @tb{b}_p1 AND b.[ExpiresAt] > SYSUTCDATETIME();
IF @@ROWCOUNT <> @tb{b}_expected THROW 50422, N'Blob.NotAttachable', 1;
```

- **Placement.** The statements are `BlobReferenceEffect<T>`'s `ContributeAsync` contribution
  (spec 0014 §6.7, §13.1): after the emitter's statements for every table and the service's
  `ContributeAsync` statements, first among the effects (`CoreFeature` precedes every pack in
  registration order), and before the access guards, the row-level post-check and the tag bumps,
  inside the transaction.
- **The mismatch** (`THROW 50422`) rolls the transaction back under `XACT_ABORT` and surfaces as
  `ValidationException` with code `Blob.NotAttachable` (the message is the code, per spec 0011's
  invariant band): a concurrent save won the blob, it expired between validation and persist, or
  the same id reached two rows. The client re-uploads.
- **Race with the sweep.** `ExpiresAt > SYSUTCDATETIME()` on confirm and `ExpiresAt < now` on the
  sweep's claim (§7.1) are evaluated on the same server clock: a row the sweep has claimed can
  never be confirmed afterwards, and a row confirmed earlier is no longer `Staged` when the claim
  runs, because an `UPDATE` waits on a locked row and re-evaluates its predicate against the
  committed version. §11 pins this with a concurrent-transaction test.
- **Fail-closed by construction.** The FK stops any deletion of a referenced row; only `Staged`
  rows can be confirmed, so a committed blob can never be re-pointed to another record; only
  `Committed` rows are released, so a double release is a no-op.

### 4.5 Temporal owners

When a temporal owner (`core.Users`) changes `ImageId`, its history row keeps the old id, the old
blob is released and swept, and the history reference dangles (history tables carry no FK). Edit
history shows *that* an image changed, not the old bytes. A kind that must retain replaced blobs
is a later `Retain` release policy.

### 4.6 Server-generated blobs

A job that produces a file (spec 0018's export handler) calls `IBlobService.StageAsync` from its
job scope under the job's run-as user, then enlists the save of the owning record — the `Exports`
row whose `FileId` carries `[BlobReference("export-file", Attachment, ReadAccess = OwnerRead)]` —
with the job frame (spec 0014 §13.3), so the row's statements and the effect of §4.4 ride the
partition's completion batch and the blob is confirmed inside the completion transaction, the
run-as user being both the uploader and the saver. The recipient downloads through
`GET blobs/export-file/{id}` authorised by the `Exports` row. Spec 0019's `core.file-retention`
deletes expired `Exports` and `Imports` rows through the pipeline, so their blobs are released by
the capture and reclaimed by the sweep; a synchronous import leaves its `import-file` upload
staged, and the sweep reclaims it after the TTL. No second write path exists.

## 5. Downloading

### 5.1 `ResolveAsync`

`IBlobService.ResolveAsync(kind, id, variant)`:

1. Resolve the kind → `BlobKindDescriptor`; unknown → `null` (404). Validate `variant` against
   `BlobName.IsValidVariant`; invalid → `BadRequestException` (400, code `bad-request`).
2. Evaluate `IAccessEvaluator.EvaluateAsync(OwnerResource, "Read", [])` (spec 0013) unless the
   policy is `AnyMember`. A `Denied` outcome skips statement 1 below; `Filtered` conjoins
   `decision.Filter`; `Unrestricted` and `AnyMember` use the column predicate alone.
3. **One `Read` batch, two statements**, through `IGuardedBatchRunner` (the prologue rides it).
   Statement 1 (`IDataBatch.Rows`): root = `OwnerResource`, `Select = "Id"`,
   `Filter = FilterTree.And(Leaf("<OwnerColumn> = @id"), accessFilter?)`, `Take = 1`, `@id` a
   declared parameter. Statement 2 (`IDataBatch.Sql`, `Idempotent = true`; `@tb{b}_p0` id,
   `@tb{b}_p1` kind, `@tb{b}_p2` caller id):

```sql
SELECT [Id], [Kind], [State], [StorageKey], [ContentType], [Size], [FileName], [Width], [Height], [Variants]
FROM [core].[Blobs]
WHERE [Id] = @tb{b}_p0 AND [Kind] = @tb{b}_p1
  AND ([State] = 'Committed'
       OR ([State] = 'Staged' AND [CreatedById] = @tb{b}_p2 AND [ExpiresAt] > SYSUTCDATETIME()));
```

4. Combine: no blob row → `null`. A `Committed` row is served only when statement 1 returned an
   owner row (or was skipped for `Denied`, in which case → `null`). A `Staged` row is served to
   its uploader with no owner — the details page previews a picked image before the save. A
   `Released` or `Deleting` row never matches. `StorageName` is `StorageKey` for the primary,
   `BlobName.Variant(StorageKey, variant)` when `variant` is listed in `Variants`, and the primary
   when it is not (only possible when a kind's policy gained a variant after the row was staged).
   `IsImage` is `ContentType` starting with `image/`; `ETag` is `"{id}"` for the primary and
   `"{id}-{variant}"` for a variant.

Missing, hidden and wrong-kind are indistinguishable (404). `StorageKey` and `Sha256` are outside
the Queryex schema, which is why the blob row is read by raw SQL rather than through the
navigation. Authorisation is "if you can read the owner you can read its blobs", evaluated on
every cache miss, fail-closed.

### 5.2 Read access

| `ReadAccess` | Statement 1 | Who can download a committed blob |
|---|---|---|
| `OwnerRead` | the column predicate conjoined with the caller's `Read` filter on the owner resource | a caller who can read the owner row |
| `AnyMember` | the column predicate only | any connected active member |

`user-image` is `AnyMember` (avatars render on documents everywhere); `user-signature`,
`export-file`, `import-file` and `import-result` are `OwnerRead` (a signature is rendered onto
documents server-side and fetched only by a caller who may read the user row; the
`Exports`/`Imports` rows are self-scoped by spec 0018).

### 5.3 The endpoint

`GET /{tenantId:int:min(1)}/blobs/{kind}/{id:int}?variant=&download=` on `TellmaEndpoints.Blobs`
(cookie policy, `AllowMember`, `WithMutation(false)`; the securable check is the service's).

- **Conditional requests.** The strong ETag is computed before any store call; on an
  `If-None-Match` match the endpoint returns 304 with `ETag` and `Cache-Control`, storage
  untouched. Otherwise it opens `IBlobStore.OpenReadAsync(tenantId, StorageName)` (a missing
  object → 404 and `tellma.blobs.downloads{outcome=not_found}`) and returns
  `Results.Stream(stream, contentType, fileDownloadName, entityTag)`, which implements 304 and 412
  from the supplied tag. Range processing is off.
- **Headers.** `Cache-Control: private, max-age=31536000, immutable` for a `Committed` blob — the
  bytes behind an id never change, so a year without revalidation is safe and a re-upload changes
  the record's id and therefore the URL; never `public`. `Cache-Control: private, no-store` for a
  `Staged` blob. `X-Content-Type-Options: nosniff` always.
  `Content-Disposition: inline; filename*=UTF-8''…` for image kinds unless `download=true`;
  `attachment; filename*=UTF-8''…` otherwise (`download` binds as a boolean; absent is `false`).
  `Content-Length` from the row's `Size` for the primary; from the store's `BlobContent.Length` for
  a variant.
- **Exempt from `Tellma-Client`.** A browser cannot decorate an `<img>` request with a custom
  header; the `GET` is side-effect free and relies on spec 0010's `Origin`/`Sec-Fetch-Site` check
  alone (spec 0015 lists it as one of the two exemptions).
- **Tenant state.** The prologue writes nothing on a `ReadOnly` tenant, so downloads succeed
  against a read-only database; `Suspended`, `Provisioning` and `Retired` follow spec 0010's
  verdicts.

A grid of fifty avatars costs fifty one-round-trip `GET`s on a cold browser cache and zero
afterwards. Output caching is not used (it never serves authenticated requests) and blob responses
are not compressed (images are pre-compressed).

## 6. Stores

### 6.1 `IBlobStore` and the name grammar

```csharp
// Tellma.Core.Abstractions.Blobs
public interface IBlobStore     // singleton; bytes by name for one tenant per call; writes create-only
{
    Task EnsureTenantAsync(int tenantId);
    Task WriteAsync(int tenantId, IReadOnlyList<BlobWrite> writes);
    Task<BlobContent?> OpenReadAsync(int tenantId, string name);
    Task DeleteAsync(int tenantId, IReadOnlyList<string> names);
    IAsyncEnumerable<BlobStoreEntry> ListAsync(
        int tenantId, string prefix);                       // async sequence; tooling and reconcile only
    Task PurgeIncompleteAsync(int tenantId, DateTimeOffset olderThan);
}

public sealed record BlobWrite(string Name, string ContentType, Stream Content, long Length);

public sealed record BlobContent(Stream Content, long Length);    // disposable; the caller disposes

public sealed record BlobStoreEntry(string Name, long Length, DateTimeOffset LastModified);

public sealed class BlobStoreException(string Message, Exception? Inner) : Exception;

public sealed class BlobAlreadyExistsException(string Name) : Exception;

public static class BlobName                                // grammar {kind}/{k0k1}/{key32}[.{variant}]
{
    public static bool IsValidKind(string kind);            // ^[a-z][a-z0-9-]{1,39}$
    public static bool IsValidVariant(string variant);      // ^[a-z0-9]{1,16}$
    public static bool IsValid(string name);
    public static string Primary(
        string kind, string key32);     // key32: ^[0-9a-f]{32}$; k0k1 = its first two characters
    public static string Variant(string primaryName, string variant);
}
```

| Member | Meaning |
|---|---|
| `EnsureTenantAsync` | Creates the tenant's container or directory when absent; idempotent; called by the provisioning step (§6.4) and lazily by the first write for a tenant the process has not seen. |
| `WriteAsync` | Writes every object create-only, in parallel, best-effort: a failure leaves earlier writes in place and throws `BlobAlreadyExistsException(Name)` or `BlobStoreException(Message, Inner)`. Every name is validated by `BlobName.IsValid` before storage is touched. |
| `OpenReadAsync` | Opens one object for streaming or returns `null` when it does not exist. |
| `DeleteAsync` | Deletes every named object with bounded parallelism; a missing object is success. |
| `ListAsync` | Enumerates objects under a prefix (a kind, or a kind and fan-out segment); never on a request path. |
| `PurgeIncompleteAsync` | Adapter housekeeping: removes incomplete artefacts older than the cut-off; a no-op on Azure. |

Stores never consult `ISandboxContext`: a sandbox tenant has its own container like any other, and
writing to the platform's own storage is not an external side effect. `BlobAlreadyExistsException`
and `BlobStoreException` are thrown by the adapters and are never mapped by the web layer (a 500
with a trace id), so they derive from `Exception` rather than `TellmaException`.

### 6.2 `FileSystemBlobStore`

Layout `{RootPath}/t{tenantId}/{StorageKey with '/' as the directory separator}`; temporary files
under `{RootPath}/t{tenantId}/.tmp/{key32}.part`. A write streams to the `.part` file, flushes to
disk (`FileStream.Flush(flushToDisk: true)`), then `File.Move(tmp, final, overwrite: false)` —
atomic because both paths share one volume. Every path is resolved with `Path.GetFullPath(name,
tenantRoot)` and must remain under the tenant root (the grammar already forbids traversal; the check
is belt-and-braces). `OpenReadAsync` returns a `FileStream` opened with `FileShare.Read`;
`DeleteAsync` ignores `FileNotFoundException`/`DirectoryNotFoundException`; `ListAsync` walks the
prefix directory; `PurgeIncompleteAsync` deletes `.part` files older than the cut-off;
`EnsureTenantAsync` creates `t{tenantId}` and `.tmp`. Startup validation (§9) rejects a `RootPath`
that is relative, whose `.tmp` would cross a volume, or longer than 160 characters on Windows (the
deepest path stays under `MAX_PATH`). Default for local development and air-gapped on-premises
deployments.

### 6.3 `AzureBlobStore`

One `BlobServiceClient` per process built from `ServiceUri` plus the host-supplied
`TokenCredential`, or from `ConnectionString` (Azurite and local development only; the adapter
never constructs `DefaultAzureCredential`). Container name `{ContainerPrefix}t{tenantId}`
(`tellma-t42`); `EnsureTenantAsync` runs `CreateIfNotExists` and remembers the container in a
process-wide set; a write for an unremembered tenant ensures first. Create-only uploads send
`IfNoneMatch = ETag.All`; a 409 becomes `BlobAlreadyExistsException`. `DeleteAsync` issues
single deletes with `MaxParallelism` (8) and treats 404 as success; `OpenReadAsync` returns the
SDK's download stream; `ListAsync` pages `GetBlobsAsync(prefix)`; `PurgeIncompleteAsync` is a
no-op. No lifecycle rule is relied on (day-granular, up to a day to start, absent on Azurite); a
deployment may add one as a backstop outside this design. One storage account per distribution
addressed by managed identity, one container per tenant: no per-tenant secret exists and the
tenant catalog stores nothing for blobs.

### 6.4 Provisioning

`BlobContainerStep : ITenantProvisioningStep` (spec 0010) — `Name = "core.blob-container"`, `Order =
20`, `Version = 1` — calls `IBlobStore.EnsureTenantAsync(tenantId)` so a tenant's container exists
before its first upload, in the migrator's step-runner scope (spec 0010's
`ITenantScopeFactory.CreateScopeAsync(snapshot, allowNonActive: true)`) under the migrator's
identity. Lazy creation on first write remains the fallback for a store registered after
provisioning ran. Teardown of a retired tenant's container is an operator action, not automated.

## 7. Sweep and reconcile

### 7.1 `core.blob-sweep`

`BlobSweepHandler` — `[JobHandler("core.blob-sweep", BatchSize = 1, LeaseSeconds = 600, MaxAttempts
= 3, MaxConcurrency = 1)]`, an `IJobHandler` (spec 0019) — runs in the tenant's job scope as the
system user and repeats until a short batch or cancellation:

```sql
-- claim (Maintenance batch; TransactionMode None; Idempotent = false; @tb{b}_p0 SweepBatchSize, @tb{b}_p1 SweepReclaimAfter minutes)
DECLARE @tb{b}_now datetimeoffset(3) = SYSUTCDATETIME();
WITH due AS (
    SELECT TOP (@tb{b}_p0) [Id], [Kind], [StorageKey], [Variants], [State], [ExpiresAt]
    FROM [core].[Blobs] WITH (ROWLOCK)
    WHERE [State] IN ('Staged', 'Released', 'Deleting') AND [ExpiresAt] < @tb{b}_now
    ORDER BY [ExpiresAt])
UPDATE due SET [State] = 'Deleting', [ExpiresAt] = DATEADD(minute, @tb{b}_p1, @tb{b}_now)
OUTPUT inserted.[Id], inserted.[Kind], inserted.[StorageKey], inserted.[Variants], deleted.[State];
```

then `IBlobStore.DeleteAsync(tenantId, names)` for every claimed row's primary and variants
(`BlobName.Variant` per listed variant; a missing object is success), then, in a second batch
with `@tb{b}_t0 : IdList` holding the ids whose objects are gone:

```sql
DELETE b FROM [core].[Blobs] AS b INNER JOIN @tb{b}_t0 AS i ON i.[Id] = b.[Id] WHERE b.[State] = 'Deleting';
```

and, once per run, `IBlobStore.PurgeIncompleteAsync(tenantId, now − 24 h)`.

- **Claim semantics.** A claimed row is `Deleting` for `SweepReclaimAfter` (60 min); a sweep that
  crashes after claiming is retried by the next run when the reclaim time passes. A storage
  failure leaves the row `Deleting` and is metered; the run continues with the other rows. The
  `deleted.[State]` column feeds `tellma.blobs.sweep.deleted{state}`.
- **No lease columns on `core.Blobs`.** The schedule's overlap policy `Skip` and
  `MaxConcurrency = 1` keep one runner per tenant; the claim is nonetheless idempotent, so a
  second runner would only find fewer due rows.
- **An FK violation** on the row delete is impossible by construction (only `Staged`, `Released`
  and `Deleting` rows are deleted; only `Staged` rows can be attached) and, if it ever occurs, is
  logged (`BlobSweepRowRetained`), counted in `tellma.blobs.sweep.failures` and skipped.
- **Progress** through `IJobProgress.Report` after each batch (message: rows claimed, objects
  deleted); the job's cancellation token is checked between batches.
- **Erasure latency.** A replaced or deleted blob is physically gone within one sweep interval
  (15 minutes) of the commit that released it, never at the commit itself. A released blob is
  already unreachable — its owner no longer points at it and the `GET` authorises through the
  owner — so the delay loses nothing and removes the only non-transactional step a save would
  otherwise carry.

### 7.2 `core.blob-reconcile`

`BlobReconcileHandler` — `[JobHandler("core.blob-reconcile", BatchSize = 1, LeaseSeconds = 900,
MaxAttempts = 3, MaxConcurrency = 1)]` — runs once per registered kind (`IBlobKindRegistry.All`), in
one `Maintenance` batch per kind with `Writes = { core.Blobs }`, `Idempotent = true` (`@tb{b}_p0`
the kind; owner table and column from the descriptor, bracket-quoted):

```sql
UPDATE b SET [State] = 'Released', [ExpiresAt] = SYSUTCDATETIME()
FROM [core].[Blobs] AS b
WHERE b.[Kind] = @tb{b}_p0 AND b.[State] = 'Committed' AND b.[CommittedAt] < DATEADD(day, -1, SYSUTCDATETIME())
  AND NOT EXISTS (SELECT 1 FROM [core].[Users] AS o WHERE o.[ImageId] = b.[Id]);
```

It is the backstop for owner rows removed outside the pipeline (raw SQL, an operator's script).
The one-day grace excludes blobs committed by a transaction still in flight. Released rows are
metered (`tellma.blobs.reconcile.released{kind}`) and logged (`BlobReconcileReleased`) because
every non-zero count points at a path that bypassed the emitter.

### 7.3 Operator tooling

A restore of the database from a backup can leave objects in storage that no row references
(uploads after the backup point) and rows whose objects are gone (a storage restore). Neither is
scheduled: the `TellmaMigrator` gains a `blobs verify --tenant <id> [--delete-orphans]` command
that walks `IBlobStore.ListAsync` per kind against `core.Blobs` (`StorageKey` and the listed
variants), reports both sets, and deletes unreferenced objects only under the flag. It runs under
the migrator's identity and never touches rows.

### 7.4 Schedules

`CoreFeature` contributes `BuiltInSchedule("core.blob-sweep", "*/15 * * * *")` and
`BuiltInSchedule("core.blob-reconcile", "0 2 * * 0")` (Sunday 02:00 in the tenant zone) — the
expressions of record in spec 0019's built-in schedule table — realised as spec 0019's built-in
`core.Schedules` rows: `OverlapPolicy = Skip`, `MissedPolicy = Coalesce`,
run as the system user, immutable except for `Name*`, `CronExpression` and `TimeZoneId`. A tenant
that needs faster erasure edits the sweep's cron; it cannot deactivate it.

## 8. The web surface

### 8.1 Endpoints

Both endpoints are mapped once by `MapTellmaBlobs` on `TellmaEndpoints.Blobs` (spec 0010), under
the cookie policy; `MapTellma` calls it. Each carries `MemberEndpointMetadata` through
`AllowMember` — any connected active member; the finer checks are the service's.

| Endpoint | Metadata | Request | Response |
|---|---|---|---|
| `POST /{tenantId}/blobs/{kind}?fileName=` | `AllowMember`, `WithMutation(true)`, `AcceptsBinary(BlobOptions.MaxUploadSize)`; `Tellma-Client` required (the CSRF control of spec 0010) | raw body; `Content-Type` = the declared type; `Content-Length` required (411 `Blob.LengthRequired` otherwise); `fileName` ≤ 255 characters | `201 Created`, body `BlobDescriptor`, `Location: /{tenantId}/blobs/{kind}/{id}` |
| `GET /{tenantId}/blobs/{kind}/{id}?variant=&download=` | `AllowMember`, `WithMutation(false)`; exempt from `Tellma-Client` | `If-None-Match` honoured | `200` stream or `304`; headers per §5.3 |

The upload is deliberately single-file — the one named exception to the platform's bulk-shaped
API: bytes are bandwidth-bound, parallel single-file requests use the pipe better than one
multipart body, and errors and retries are per file. The store contract stays batch-shaped. The
endpoint is not an `[ApiAction]`: it is hand-mapped, appears in no stack descriptor, and MCP
reaches it later through the reserved `tellma_upload` tool. The tenant-state verdicts of spec 0010
apply: an upload on a `ReadOnly` tenant is 403 `tenant-read-only`; a download succeeds.

### 8.2 Errors

```csharp
// Tellma.Core.Abstractions.Blobs
public sealed class BlobRejectedException(
    string Code, IReadOnlyDictionary<string, object?> Arguments) : TellmaException;
```

| Code | HTTP status | Problem `code` | When |
|---|---|---|---|
| `Blob.UnknownKind` | 404 | `blob-rejected` | the upload route's kind is not registered (the `GET` resolves an unknown kind to `null`, 404 `not-found`, §5.1) |
| `Blob.LengthRequired` | 411 | `blob-rejected` | no `Content-Length` |
| `Blob.TooLarge` | 413 | `blob-rejected` | declared or actual length above the kind's or global limit (`Arguments`: `maxBytes`) |
| `Blob.UnsupportedType` | 415 | `blob-rejected` | content type outside the allowed set or always-rejected (`Arguments`: `contentType`) |
| `Blob.StagingQuotaExceeded` | 422 | `blob-rejected` | the soft per-user quota (`Arguments`: `maxCount`, `maxBytes`) |
| `Blob.ImageRejected` | 422 | `blob-rejected` | the processor refused the image (`Arguments`: `reason` ∈ `format`, `too-many-pixels`, `decode-failed`) |

Spec 0015 maps `BlobRejectedException` by code to the status above; the problem body carries
`errors` with one item at path `body` whose `code` and `arguments` are the exception's, rendered
under the request culture like every validation message. Attach-time failures are ordinary
validation errors (422 `validation`): `Blob.NotAttachable` and `Blob.DuplicateReference` at the
property path from the validator (§4.2), and `Blob.NotAttachable` at `ValidationPath.Root` when
the persist-time re-check throws (§4.4, spec 0014 §14.2). A bad `variant` is 400 `bad-request`; a
missing, hidden, released or wrong-kind blob is 404 `not-found`; a store failure is 500 `internal`.

## 9. Startup checks

`BlobKindRegistry` and the store registration report into spec 0010's realised gate through one
`IStartupCheck` named `blobs`; every problem is a `CompositionProblem` with a fix hint and any
problem fails startup:

- exactly one `IBlobStore` is registered (none, or both stores, is a problem);
- every `[BlobReference]` sits on an `int?` property of a top-level entity (a child entity or a
  non-nullable property is a problem this release);
- every kind is declared by exactly one property in the realised model, its name matches the
  grammar, and every `BlobsBuilder.Kind` / `FeatureContribution.BlobKind` names a declared kind;
- an `IImageProcessor` is registered when any kind's policy carries an `Image`; at most one
  processor registration;
- the file-system `RootPath` is absolute, at most 160 characters on Windows, and its `.tmp`
  shares the volume; the Azure `ContainerPrefix` plus `t2147483647` is a valid DNS label
  (`^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$`, no consecutive hyphens), and exactly one of `ServiceUri`
  and `ConnectionString` is set;
- `BlobOptions` values are positive and `MaxUploadSize` is at least the largest kind `MaxSize`
  after narrowing (a kind larger than the global ceiling is a problem, not a silent clamp).

## 10. Observability

### 10.1 Instruments

```csharp
// Tellma.Core.Abstractions.Blobs
public static class BlobTelemetryNames
{
    public const string MeterName = "Tellma.Core";          // uploads, downloads, image, sweep, reconcile
    public const string StoreMeterName = "Tellma.Blobs";    // store instruments; shared with the Azure adapter
    public const string Uploads = "tellma.blobs.uploads";
    public const string UploadBytes = "tellma.blobs.upload.bytes";
    public const string UploadDuration = "tellma.blobs.upload.duration";
    public const string ImageDuration = "tellma.blobs.image.duration";
    public const string Downloads = "tellma.blobs.downloads";
    public const string StoreOperations = "tellma.blobs.store.operations";
    public const string StoreDuration = "tellma.blobs.store.duration";
    public const string SweepDeleted = "tellma.blobs.sweep.deleted";
    public const string SweepFailures = "tellma.blobs.sweep.failures";
    public const string ReconcileReleased = "tellma.blobs.reconcile.released";
    public const string KindTag = "kind";
    public const string OutcomeTag = "outcome";
    public const string OperationTag = "operation";
    public const string StateTag = "state";
}
```

| Instrument | Kind | Tags |
|---|---|---|
| `tellma.blobs.uploads` | counter | `kind`; `outcome` ∈ `staged`, `rejected_size`, `rejected_type`, `rejected_image`, `quota`, `failed` |
| `tellma.blobs.upload.bytes` | counter (By) | `kind` |
| `tellma.blobs.upload.duration` | histogram (s) | `kind` |
| `tellma.blobs.image.duration` | histogram (s) | `kind` |
| `tellma.blobs.downloads` | counter | `kind`; `outcome` ∈ `served`, `not_modified`, `not_found` |
| `tellma.blobs.store.operations` | counter | `operation` ∈ `ensure`, `write`, `read`, `delete`, `list`, `purge`; `outcome` ∈ `ok`, `conflict`, `not_found`, `failed` |
| `tellma.blobs.store.duration` | histogram (s) | `operation` |
| `tellma.blobs.sweep.deleted` | counter | `state` ∈ `staged`, `released`, `deleting` |
| `tellma.blobs.sweep.failures` | counter | — |
| `tellma.blobs.reconcile.released` | counter | `kind` |

`kind` is a closed set per distribution; no tenant or user tag anywhere. The upload counts one
database round trip in `DataAccessScope`, a save adds none, a `GET` counts one; spec 0011's
`tellma.data.roundtrips` observes them. Alert queries under `infra/monitoring/` cover
`sweep.failures > 0` and `reconcile.released > 0`.

### 10.2 Log events

Structured events (name, level, properties; never a tenant-identifying metric, always a tenant id
on the log): `BlobStaged` (Information; kind, id, size, contentType, durationMs),
`BlobRejected` (Information; kind, code), `BlobStoreWriteFailed` (Error; kind, id, exception),
`BlobDownloadNotFound` (Debug; kind, id), `BlobSweepBatch` (Information; claimed, deleted,
failed), `BlobSweepRowRetained` (Warning; id), `BlobReconcileReleased` (Warning; kind, count),
`BlobTenantEnsured` (Information; tenantId, container). Messages name no spec and no document.

## 11. Testing

| Project | Tier | Pins |
|---|---|---|
| `test/core/Tellma.Core.Tests/Blobs/` | unit, PR | `BlobName` grammar (valid/invalid kinds, variants, names; traversal strings rejected); content-type determination (§3.3) over fixture byte prefixes including a `PK` OOXML, an `MZ` executable, HTML with leading whitespace, an SVG behind an XML prolog, text with a NUL byte; file-name sanitisation; `BlobKindRegistry` construction and every §9 problem over synthetic models; the abstract `BlobStoreConformanceTests` run against `FileSystemBlobStore` in a temporary directory (create-only conflict, missing read is `null`, delete of missing is success, list by prefix, purge of `.part` files, `EnsureTenantAsync` idempotence, traversal rejected); `BlobService.StageAsync` over a fake store and an in-memory batch (each rejection code, quota mapping, row-before-bytes on a failing store); `ResolveAsync` combination rules (§5.1 step 4) over canned result sets; ETag and header computation of the endpoint |
| `test/core/Tellma.Core.IntegrationTests/Blobs/` | `Category=Integration`, PR (LocalDB or the `Testcontainers.MsSql` 4.14.0 container) | the stage statement (id from the sequence, quota `50422`); the capture and effect statements over the fixture entity `fixture.BlobOwners (Id, Name, FileId [BlobReference("fixture-file")], PhotoId [BlobReference("fixture-photo", Photo)])`, added to spec 0011's shared fixture project `test/shared/Tellma.Testing.Entities`, whose database carries the full `core.Blobs` and `core.sq_Blobs` (spec 0011 §13.2): attach on insert, replace on update, `null` release, delete by ids/by query/with descendants release, an override save releasing the row's actual old value, the same id on two rows → `Blob.NotAttachable`, an expired row → `Blob.NotAttachable`, a foreign uploader → `Blob.NotAttachable`; the sweep claim with a `TimeProvider` fake (claims only past-`ExpiresAt` rows, re-claims after `SweepReclaimAfter`, deletes rows only in `Deleting`); **the concurrency test** — a transaction confirms a staged row and holds its lock while the claim runs on a second connection under RCSI; the claim must block and then skip the row; the reconcile statement releases an orphan and leaves a young orphan and a referenced blob alone; the `ResolveAsync` round trip through a filtered `Read` grant (visible owner served, hidden owner 404, staged-to-uploader served, staged-to-other 404) |
| `test/core/Tellma.Core.Imaging.Tests/` | unit, PR | `ImageSharpImageProcessor` over fixtures: each accepted format, `Contain` and `CoverSquare` dimensions, thumbnail sizes, EXIF orientation applied and metadata stripped, a 60-megapixel header rejected before decode, a decompression bomb rejected by the allocator cap, a JPEG/HTML polyglot re-encoded to bytes with no `<` in the first 8 KiB, an animated GIF reduced to one frame, output content type per `ImageFormat` |
| `test/connector/azure-blobs/Tellma.Connector.AzureBlobs.Adapter.IntegrationTests/` | `Category=Integration`; PR on the Linux runner (Testcontainers), skipped on a Windows runner without Docker, nightly on both (spec 0010 §10) | the same `BlobStoreConformanceTests` against Azurite (`Testcontainers.Azurite` 4.14.0); container naming and `EnsureTenantAsync`; create-only conflict as `BlobAlreadyExistsException` |
| `distributions/acme` end-to-end (spec 0010's host tests) | `Category=Integration`, PR | upload → save `User.ImageId` → `GET` 200 with `immutable` → `GET` with `If-None-Match` 304 → save `null` → `GET` 404; a non-member upload 404 `tenant-not-found`; an upload without `Tellma-Client` 403 `csrf-rejected`; an oversize body 413 before the body is read; a `ReadOnly` tenant: upload 403, download 200 |

No `Live=true` tier exists for this spec: the Azure adapter is exercised on Azurite only; the
first Azure deployment verifies that Storage Blob Data Contributor covers container creation
(provisioning pre-creates the container as the fallback).

## 12. Definition of done

- **Projects**: `Tellma.Core.Abstractions` (namespaces `.Blobs` and the `.Entities` additions),
  `Tellma.Core` (`Tellma.Core.Blobs`), `Tellma.Core.AspNetCore` (`MapTellmaBlobs`),
  `src/core/Tellma.Core.Imaging`, `src/connector/azure-blobs/Tellma.Connector.AzureBlobs.Adapter`,
  and the four test projects of §11 — each with a README stating purpose and usage, XML docs on
  every member, building and testing on Windows and Linux under warnings-as-errors, wired into
  `Tellma.slnx`; the package pins of §1.1 in `Directory.Packages.props`.
- **Behavior**: the model of §2 (table, indexes, entity, navigation, related projection,
  presets), staging per §3 (flow, content types, statement, limits, image processing), the attach
  rule, validator, capture contract and effect of §4 (including deletes and temporal owners),
  resolution and the endpoint of §5, both stores and the provisioning step of §6, the sweep,
  reconcile, tooling and schedules of §7, the web surface and error mapping of §8, the startup
  checks of §9 — implemented and pinned by the suites of §11, green in CI.
- **Observability**: every §10.1 instrument emitted with its closed tag values and asserted by the
  unit suite; the §10.2 events emitted; the two alert queries checked by the existing monitoring
  test.
- **CI**: the unit and LocalDB suites on every PR; the Azurite suite on the Linux PR runner and
  nightly on both platforms; the reference distribution's blob end-to-end on every PR.
- **Docs**: ARCHITECTURE.md updated where this spec touches it — the package naming and
  dependency rules (`Tellma.Core.Imaging` as an Abstractions-plus-ImageSharp package never
  referenced by `Tellma.Core`; `Tellma.Connector.AzureBlobs.Adapter` in the connector list); the
  guiding principle on bulk I/O gaining its one named exception (blob uploads are single-file raw
  bodies); the Azure hosting section (one container per tenant on the distribution's storage
  account); the observability section (the `Tellma.Blobs` meter shared with the adapter); the web
  API section's note that the blob `GET` is the one `GET` on the tenant surface. Public XML docs
  and error messages reference no `docs/` paths, per repo rule.
- **Not in scope of done**: blob kinds on child entities, `BlobReadAccess.Custom`, signed URLs,
  range requests, a `Retain` policy, cross-tenant copy, the MCP upload tool, and the
  `Exports`/`Imports` owners and their retention (specs 0018 and 0019).

## Decisions record

The load-bearing decisions, where not already evident above:

1. **Staged uploads; the save carries the staged id** — one JSON save path for the UI, import,
   MCP and jobs; byte validation at upload with a specific error; no non-transactional step in a
   save (§3, §4.1).
2. **One `core.Blobs` table holds every intrinsic fact; records hold only the FK** — orphans,
   quotas, download metadata, fail-closed deletion and reconciliation all need one table (§2.1).
3. **`Id int` from `core.sq_Blobs` is the only client-visible identifier; `StorageKey` is a random
   full object name** — four-byte FKs like every table; random names avoid hot partitions and are
   traversal-proof by grammar; storing the full name lets the grammar change later (§2.1, §3.2).
4. **The id is taken inside the stage statement** — `Blob` is a system-written row like `Job` and
   `Notification`; no allocator dependency, no unused reservation (§3.4).
5. **`IBlobStore` is a singleton with the tenant id per call; `IBlobService` is scoped** — the
   sweep and tooling address many tenants from one scope; one `BlobServiceClient` per process
   (§6.1).
6. **One container or directory per tenant; one storage account per distribution; no per-tenant
   secret** — container-scoped RBAC and one-shot teardown without name discipline; managed
   identity makes the catalog's blob column unnecessary (§6.3).
7. **Row before bytes** — every object has a row; a failed write leaves only a row for the sweep
   (§3.2).
8. **Old values from `OUTPUT deleted`, never from memory** — an override save releases what was
   on the row; deletes of every shape feed the same capture (§4.3).
9. **The attach guard is a `THROW` inside the transaction** — the batch commits inside its own
   text, so a C#-side read-then-commit does not exist; the invariant band maps it to a validation
   error (§4.4).
10. **Physical deletion is the sweep's alone, with a `Deleting` claim state** — correct whatever
    the length of a concurrent transaction; a grace window is correct only while every transaction
    is shorter than it (§7.1).
11. **`ExpiresAt` carries three meanings by state** — one indexed column serves the staging
    deadline, the release time and the reclaim time, so the sweep has one predicate (§2.1).
12. **The download URL names the kind and the id only** — immutable by construction, so a year of
    browser caching needs no revalidation; the kind gives the owner without a union over every
    owner table (§5).
13. **Authorisation through the owner row on every cache miss** — "if you can read the owner you
    can read its blobs"; fail-closed; `AnyMember` opts a kind out (§5.2).
14. **Images are re-encoded by the server; ImageSharp behind `IImageProcessor` in a separate
    package** — re-encoding is the security control; the split licence stays in one swappable
    package (§1.1, §3.6).
15. **Content type is server-determined; executables, HTML and SVG never stored** — declared
    types are hints for text and `PK` archives only (§3.3).
16. **Limits are per kind with global ceilings; the body limit is raised on the upload endpoint
    only** — JSON endpoints keep their small limit (§3.5).
17. **The upload is single-file and raw-body** — the platform's one named exception to bulk-shaped
    APIs; the store stays batch-shaped (§8.1).
18. **Blob columns are excluded from Excel and never natural keys** — ids are tenant-local and
    bytes do not travel in sheets (§2.3).
19. **Temporal owners keep the reference, not the bytes** — history shows that an image changed;
    retention is a later policy (§4.5).
20. **Server-generated files use the same path** — a job stages under its run-as user and the
    owner row's enlisted save confirms inside the job's completion transaction (§4.6).
21. **The sweep cadence lives on the built-in schedule, not in options** — one knob, editable by
    the tenant administrator within the rules for built-ins; no second configuration surface
    (§7.4).
22. **Two built-in schedules and a migrator command, no lifecycle rules** — day-granular rules
    absent on Azurite and on disk cannot be the mechanism; a restore-recovery walk is an operator
    action (§7).

## Review flags

1. **Staging TTL 24 hours** (§3.4) versus one hour (the tightest industry precedent). Neither a
   human nor an agent needs a day, a draft-heavy form might; the per-kind and global options make
   it a deployment choice. A quota-pressure signal in `tellma.blobs.uploads{outcome=quota}` would
   flip it downward.
2. **ImageSharp under the Six Labors split licence in `Tellma.Core.Imaging`** (§1.1, §3.6) versus
   SkiaSharp as the default. The platform is Apache-2.0 and distributions consume ImageSharp
   transitively, which reads as Apache-2.0 under the licence's open-source clause; the vendor's
   pricing wording is narrower. The licence reading must be confirmed, or the Boutique licence
   bought, before the first release; either way a swap is a package swap in the distribution,
   never a change inside `Tellma.Core`.
3. **`user-image` read access `AnyMember`** (§5.2) versus `OwnerRead`. Avatars appear on
   documents everywhere, so any active member fetching any avatar is the useful default;
   `OwnerRead` would require `core.User × Read` to render a document's author. A tenant that
   treats staff photos as restricted data would flip it.
4. **Attach guard as a `THROW` inside the batch** (§4.4) versus a C#-side `(Expected, Confirmed)`
   read before commit. Settled by the batch model: the transaction commits inside the round trip's
   text, so no read-then-commit step exists.
5. **`Deleting` claim state with `SweepReclaimAfter`** (§7.1) versus a grace window
   (`ExpiresAt < now − 15 min`). The claim adds a fourth state and an option but is correct
   regardless of transaction length; evidence that no transaction ever outlives the window would
   make the window acceptable.
6. **`Content-Length` required (411)** (§3.2) versus accepting chunked uploads. Required makes the
   size and quota checks cheap and matches browsers and SDKs; a streaming client that must send
   chunked bodies would flip it to a read-then-check with an in-flight limit.
7. **Range processing off** (§5.3). Turning it on needs seekable store streams (both stores
   return seekable streams, unverified under the Azure SDK's download path); resumable downloads
   of 100 MiB attachments would flip it.
8. **`IBlobStore` as the low-level name** (§6.1) versus keeping `IBlobService` for bytes as the
   original sketch had it. The service/store split reads naturally and matches the email
   precedent; a strong preference for the sketch's names would flip it.
9. **No stored fit metadata** (§3.6) versus an `ImageFitJson` column. Crop before upload and
   server-side cover-crop leave nothing to store; server-kept originals for re-cropping would be
   a later kind policy with the original as a variant.
10. **Temporal history does not retain replaced images** (§4.5) versus a `Retain` release policy
    for kinds on temporal owners. Audit requirements on staff photos would flip it.
11. **Plural `core.Blobs`** (§2.1) — settled by the platform's table-naming convention.
12. **`Sha256` kept** (§2.1) at the cost of hashing every upload, for integrity checks, the
    restore-recovery walk and future dedup; dropping it saves nothing measurable.
13. **Provisioning pre-creates the container; lazy creation is the fallback** (§6.4) versus lazy
    only. Settled; the first Azure deployment verifies that Storage Blob Data Contributor covers
    `containers/write`, which decides whether the fallback ever fires.
14. **Erasure latency equals the sweep interval (15 minutes)** (§7.1) versus deletion at commit. A
    replaced or deleted blob is unreachable at commit and gone within the interval; a customer
    with an at-commit erasure requirement lowers the sweep's cron.
15. **Raw-body upload with `?fileName=`** (§8.1) versus `multipart/form-data`. Raw bodies are
    simplest for `fetch(file)` and for agents and need no part-level error envelope; HTML forms
    sending multipart natively, or a need to carry metadata beside the bytes, would flip it.
16. **`Variants` as a comma-separated list** (§2.1) versus a `HasThumbnail bit`. The list survives
    multi-size kinds; the bit is simpler and Queryex-friendlier today. A second variant name
    arriving would confirm the list; none arriving in a year would argue for the bit.
17. **A soft, write-skew-prone staging quota** (§3.4) versus a unique-index-backed hard limit. The
    quota exists for abuse control; evidence of a user exceeding it by more than a handful of
    rows under concurrency would flip it.
18. **`BlobReadAccess.Custom` dropped this release** (§2.3) versus a
    `BlobsBuilder.Kind(kind, policy, readFilter)` overload now. The two shipped values cover every
    Core kind; a pack kind whose readers are neither the owner's readers nor every member would
    flip it.
19. **The blob id taken inside the stage statement** (§3.4; decision 4) versus
    `IIdAllocator.TakeAsync`. In-statement matches the other system-written rows and removes a
    dependency; a wish to keep every `int` id on one allocator path would flip it.
20. **Persist effects run on delete and action persists with an empty `PersistContext.Entities`**
    (§4.4; the `IPersistEffect<T>.ContributeAsync` participation rule of spec 0014, on which the
    release of every deleted owner's blob and spec 0019's `core.file-retention` depend) versus a
    dedicated delete-effect contract. One component per capability keeps the registration surface
    small; a second capability needing delete-time logic with entity images would argue for a
    `DeleteContext`-shaped hook.
21. **The sweep cadence on the built-in schedule alone** (§7.4; decision 21) versus a
    `SweepInterval` option that seeds the schedule. One knob; a deployment wanting to set the
    cadence from configuration rather than tenant by tenant would flip it.
22. **`BlobDownload.State` on the resolution record** (§3.1) versus separate resolution calls for
    staged previews and committed downloads. One call, one round trip, and the endpoint's only
    branch is the cache policy; a security preference for keeping staged bytes off the `GET` path
    entirely would flip it.

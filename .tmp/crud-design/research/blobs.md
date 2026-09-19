# Research: Blob storage and the record-plus-blobs pattern (theme `blobs`, future spec 0016)

All findings were verified on **2026-09-01** unless a line says otherwise. "Verified" means the fact was read from the cited source on that date; "Inferred" means it follows from verified facts but was not itself read from a source; "Unverified" means it is prior knowledge that could not be confirmed today. Primary sources (vendor docs, source code, package registries) are listed before secondary ones.

## 0. Repo facts this theme must respect (read from the repo, 2026-09-01)

| Fact | Where | Implication |
|---|---|---|
| `Azure.Storage.Blobs` is **not** pinned in `Directory.Packages.props`, but resolves **transitively at 12.26.0** (with `Azure.Storage.Common` 12.25.0) through `Azure.Extensions.AspNetCore.DataProtection.Blobs` 1.5.3, referenced only by `src/apps/Tellma.Identity`. `CentralPackageTransitivePinningEnabled` is `true`. Pinned neighbours: `Azure.Core` 1.61.0, `Azure.Identity` 1.21.0. | `Directory.Packages.props`; `project.assets.json` under `src/apps/Tellma.Identity/obj` | Adding `<PackageVersion Include="Azure.Storage.Blobs" Version="12.29.2" />` lifts the identity server's transitive copy too (one SDK version in the solution); `Azure.Core` 1.61.0 already satisfies the 12.29.2 floor (1.55.0). |
| No image-processing library is referenced anywhere in `src/` or `test/`. | grep over `*.cs`, `*.csproj`, `*.props` | The image library is a fresh choice with no legacy constraint. |
| The platform repo is **Apache-2.0**; distributions are **closed-source, proprietary** and reference only published `Tellma.*` packages. | `ARCHITECTURE.md` "Licensing & Intellectual Property"; root `LICENSE` | Decides which clause of the Six Labors Split License applies to the platform vs. to distributions (see §2.1). |
| Hosting: one **Blob Storage account per distribution**, one Key Vault per distribution, secrets never in the DB; local dev uses a **shared Azurite** with per-worktree **container prefixes** (`Tellma.dev.<worktree-id>.*` naming for DBs, "blob containers ... prefixed similarly"). | `ARCHITECTURE.md` "Hosting on Azure", "Parallel Local Development" | Container names must fit a worktree prefix + tenant discriminator inside 63 lowercase DNS characters (see §1.7). |
| Connector layout exists: `src/connector/{acs-email,marmin-ae,sendgrid,smtp}`; the naming rule for cross-cutting adapters is `Tellma.Connector.<vendor>.Adapter` with zero scoping segments (e.g. `Tellma.Connector.Smtp.Adapter`); a raw client is written only when the upstream client is absent or unfit. | `ARCHITECTURE.md` "Library architecture", `src/connector/` | `Azure.Storage.Blobs` is a maintained first-party client, so the Azure implementation is an adapter-only package (`Tellma.Connector.AzureBlobs.Adapter` or similar), not a raw client; the file-system implementation has no vendor and belongs in Core. |

## 1. Azure.Storage.Blobs (current version, .NET 10 support, ETags, headers, tags, lifecycle, soft delete, naming, tenancy, auth, Azurite)

### 1.1 Current version and target frameworks

- **Verified.** Latest stable **`Azure.Storage.Blobs` 12.29.2, released 2026-08-24**; prereleases 12.30.0-beta.1 (2026-07-22) and 12.30.0-beta.2 (2026-08-24). MIT licence. Dependencies for every TFM: `Azure.Core >= 1.55.0`, `Azure.Storage.Common >= 12.28.0`.
- **Verified.** The 12.29.2 nuspec has four dependency groups: **`netstandard2.0`, `netstandard2.1`, `net8.0`, `net10.0`** — a native `net10.0` target exists.
- **Verified.** Recent changelog: 12.29.2 (2026-08-24) fixed client-side-encryption detection and a version-downgrade check on downloads; 12.29.1 (2026-06-23) fixed `GenerateSasUri`/`GenerateUserDelegationSasUri` ignoring request headers/query parameters and structured-message uploads with checksums on empty content; 12.29.0 (2026-06-04) added validation of length-prefixed fields in Blob Query responses "to prevent excessive memory allocation from malformed or untrusted payloads". The changelog talks in service API versions (latest referenced: 2026-10-06), not runtime targets.
- Sources: https://www.nuget.org/packages/Azure.Storage.Blobs ; https://api.nuget.org/v3-flatcontainer/azure.storage.blobs/12.29.2/azure.storage.blobs.nuspec ; https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/storage/Azure.Storage.Blobs/CHANGELOG.md
- **Implication:** pin 12.29.2; there is no .NET 10 blocker and the package already ships a net10.0 build.

### 1.2 ETags and conditional requests on upload and download

- **Verified (REST).** Blob service ETags are returned **quoted** for service versions 2011-08-18 and later. Supported conditional headers: `If-Modified-Since`, `If-Unmodified-Since`, `If-Match`, `If-None-Match` (the latter accepts `*`, meaning "only if the resource does not exist"). Put Blob, Put Block List, Delete Blob, Set Blob Metadata/Properties, Get Blob, Get Blob Properties all support the four headers plus `x-ms-if-tags`; **Put Block**, **Create Container**, **Find Blobs by Tags** and **Undelete Blob** do not.
- **Verified (REST).** Unmet conditions on **read** operations return **304** for `If-Modified-Since`/`If-None-Match` and **412** for `If-Match`/`If-Unmodified-Since`; unmet conditions on **write** operations (PUT/DELETE) always return **412**. Since 2013-08-15 reads may combine headers, evaluated as `If-Match && If-Unmodified-Since && (If-None-Match || If-Modified-Since)`; writes accept only one conditional header (plus the two allowed pairs) or the request is a 400.
- **Verified (SDK).** `BlobRequestConditions : BlobLeaseRequestConditions : RequestConditions : MatchConditions` exposes `IfMatch`/`IfNoneMatch` (type `Azure.ETag?`), `IfModifiedSince`/`IfUnmodifiedSince`, `LeaseId`, `TagConditions` (a SQL-like predicate string), and `AccessTierIfModifiedSince`/`AccessTierIfUnmodifiedSince`. `Azure.ETag` is a `readonly struct` with a `string` constructor, a static **`ETag.All`** (`*`) and `ToString(string format)`. (Inferred, not read today: format `"H"` yields the quoted header form; `"G"` the raw value.)
- **Verified (SDK).** `BlobUploadOptions` carries `Conditions`, `HttpHeaders`, `Metadata`, `Tags`, `AccessTier`, `ImmutabilityPolicy`, `LegalHold`, `ProgressHandler`, `TransferOptions`, `TransferValidation`. **`BlobClient.UploadAsync(Stream, bool overwrite = false)` "creates a new block blob or throws if the blob already exists"**; the `UploadAsync(Stream, BlobUploadOptions)` overload **"overwrites the contents of the blob, creating a new block blob if none exists"** unless `Conditions` are set (e.g. `IfNoneMatch = ETag.All` for create-only). Every overload returns `Response<BlobContentInfo>` (ETag + LastModified of the new content).
- **Verified (SDK).** Download details (`BlobDownloadDetails`, reachable from `DownloadStreamingAsync`/`DownloadContentAsync`) expose `ETag` (quoted), `LastModified` ("any operation that modifies the blob, including an update of the blob's metadata or properties, changes the last-modified time"), `ContentType` ("for Download Blob this is 'application/octet-stream'" when none was set), `ContentLength`, `ContentHash` (MD5, if set), `CacheControl`, `ContentDisposition`, `ContentEncoding`, `Metadata`, `TagCount`, `VersionId`, `CreatedOn`. (Inferred: passing `IfNoneMatch` on a download that is unmodified surfaces in the SDK as a `RequestFailedException` with status 304, so an app that wants to answer 304 cheaply should not round-trip to storage for it — see §3.)
- Sources: https://learn.microsoft.com/en-us/rest/api/storageservices/specifying-conditional-headers-for-blob-service-operations ; https://learn.microsoft.com/en-us/dotnet/api/azure.storage.blobs.models.blobrequestconditions ; https://learn.microsoft.com/en-us/dotnet/api/azure.storage.blobs.models.blobuploadoptions ; https://learn.microsoft.com/en-us/dotnet/api/azure.storage.blobs.blobclient.uploadasync ; https://learn.microsoft.com/en-us/dotnet/api/azure.storage.blobs.models.blobdownloaddetails ; https://learn.microsoft.com/en-us/dotnet/api/azure.etag
- **Implication:** create-only writes (`IfNoneMatch = ETag.All`) make blob ids safe against accidental overwrite, and the storage ETag is available at write time to be stored on the record; the record can then be the source of the HTTP ETag so GETs answer 304 without a storage call.

### 1.3 `BlobHttpHeaders` (content type, cache control) and metadata

- **Verified.** `BlobHttpHeaders` has exactly six properties: `CacheControl`, `ContentDisposition`, `ContentEncoding`, `ContentHash` (MD5 checked by the service on upload, 400 on mismatch), `ContentLanguage`, `ContentType`. They are set at upload (`BlobUploadOptions.HttpHeaders`) or later via Set Blob Properties (which bumps ETag/LastModified).
- **Verified.** Metadata keys must be valid C#-identifier-like ASCII (start with letter/underscore), case-insensitive on read/set, 8 KB total; `Set Blob Metadata` replaces all metadata and changes last-modified. Setting **tags** does **not** change ETag or last-modified (see §1.4).
- Sources: https://learn.microsoft.com/en-us/dotnet/api/azure.storage.blobs.models.blobhttpheaders ; https://learn.microsoft.com/en-us/rest/api/storageservices/naming-and-referencing-containers--blobs--and-metadata ; https://learn.microsoft.com/en-us/azure/storage/blobs/storage-manage-find-blobs (metadata vs tags table)
- **Implication:** store the real MIME type as `ContentType` at upload (otherwise downloads come back as `application/octet-stream`); anything the app needs to flip after the fact (e.g. "staged" → "committed") must be a **tag**, because metadata/properties writes churn the ETag and a lifecycle rule cannot filter on metadata.

### 1.4 Blob index tags and tag-filtered lifecycle rules

- **Verified.** Limits: **10 tags per blob**, key 1–128 chars, value 0–256 chars, case-sensitive, strings only, allowed chars alphanumerics plus ` +-.:=_/`. Tags can be set on upload (`x-ms-tags` on Put Blob / Put Block List / Copy Blob) or via Set Blob Tags, which **does not change the blob's last-modified time or ETag**. Only GPv2 and premium block-blob accounts support tags; not HNS (Data Lake) accounts except a non-indexed preview. `Copy Blob` does not copy tags. Tags on soft-deleted blobs cannot be modified.
- **Verified.** The tag index is **eventually consistent** ("it might take some time before the blob index updates"; under a heavy tagging burst up to ~10 minutes, normally under a second). `Find Blobs by Tags` supports `=`, `>`, `>=`, `<`, `<=`, `AND`, `@container = '...'`; no `<>`/`OR` (those exist only in `x-ms-if-tags`). Queries are efficient for single-tag equality and single-tag ranges; `AND` across tags is less efficient. Each clause in a Find call bills as one list transaction; tags bill per average monthly count.
- **Verified.** Tag operations need **separate permissions** from blob data: RBAC actions `.../blobs/tags/read`, `.../blobs/tags/write`, `.../blobs/filter/action` (all in **Storage Blob Data Owner**; Storage Blob Data Contributor alone is **not** enough), or SAS permissions `t` (tags) and `f` (find).
- **Verified.** Lifecycle rules may filter with `blobIndexMatch` (`{"name","op":"==","value"}`; **equality only**, up to 10 tag conditions per rule, flat-namespace accounts only), combined by logical AND with `prefixMatch` (up to 10 case-sensitive prefixes; a prefix must start with the container name, e.g. `staging/`; `container1` without a slash matches every container beginning with that string) and the required `blobTypes`.
- Sources: https://learn.microsoft.com/en-us/azure/storage/blobs/storage-manage-find-blobs ; https://learn.microsoft.com/en-us/azure/storage/blobs/lifecycle-management-policy-structure ; https://learn.microsoft.com/en-us/rest/api/storageservices/specifying-conditional-headers-for-blob-service-operations (tags predicate) ; https://learn.microsoft.com/en-us/rest/api/storageservices/find-blobs-by-tags
- **Implication:** a `state=staged` tag is a workable marker (set at upload, cleared at commit without ETag churn), but Find-by-tags is eventually consistent and needs extra RBAC; a deterministic **container or prefix** for staged blobs is the more robust filter for the sweep.

### 1.5 Lifecycle management (delete staging blobs older than N days)

- **Verified.** A policy is a JSON document of up to **100 rules**; each rule has `filters` (`blobTypes` required, `prefixMatch`, `blobIndexMatch`) and `actions` on `baseBlob`/`snapshot`/`version` with conditions **`daysAfterModificationGreaterThan`**, **`daysAfterCreationGreaterThan`**, `daysAfterLastAccessTimeGreaterThan`, `daysAfterLastTierChangeGreaterThan`. Conditions are **integers in days** (Microsoft's own sample uses `0`); there is no hour granularity. Current versions are evaluated on last-modified or last-access time; `daysAfterCreationGreaterThan` also applies to current versions.
- **Verified.** Execution: "it can take up to 24 hours for changes to go into effect and for the first execution to start"; policies "process objects continuously in the background"; "there's no way to track the time at which the policy will execute"; a run may need more than one pass; a delete in an account with soft delete enabled only **soft-deletes** the blob. Policies are free (delete operations are free). Policy applies to the whole account and must be written in full (no partial updates).
- Sources: https://learn.microsoft.com/en-us/azure/storage/blobs/lifecycle-management-overview ; https://learn.microsoft.com/en-us/azure/storage/blobs/lifecycle-management-policy-structure ; https://learn.microsoft.com/en-us/azure/storage/blobs/lifecycle-management-policy-faq
- **Implication:** lifecycle management is a coarse, Azure-only, day-granular **safety net** (e.g. `delete staging/* after 1 day`) but cannot be the primary orphan sweep: it has no equivalent on the file-system store, is not tenant-aware unless prefixes are tenant-scoped, and gives no feedback to the app. The application-side sweep (T10 consumer) must exist regardless; a lifecycle rule can back it up in SaaS.

### 1.6 Soft delete

- **Verified.** Blob soft delete keeps deleted blobs/snapshots/versions for a **retention period of 1–365 days** (Microsoft recommends at least 7). Deleting marks the blob soft-deleted (no snapshot created); **overwriting** an active blob with Put Blob / Put Block List / Copy Blob automatically creates a **soft-deleted snapshot** of the prior state (unless versioning is on, in which case a previous version is created instead). Restore is `Undelete Blob` (restores blob plus all its soft-deleted snapshots; cannot restore a single version). Soft-deleted data bills at the active rate; "enabling soft delete for frequently overwritten data might result in increased storage capacity charges and increased latency when listing blobs". Container soft delete is a separate feature. Metadata/property writes are not protected. Not documented as on by default for new accounts (the doc says it must be enabled).
- Sources: https://learn.microsoft.com/en-us/azure/storage/blobs/soft-delete-blob-overview
- **Implication:** with soft delete on, "delete after commit" is recoverable for the retention window, which lowers the stakes of the post-commit delete step; **immutable-by-id blobs (never overwritten) avoid the snapshot churn and cost** that soft delete adds to overwrite-in-place designs.

### 1.7 Container and blob naming rules and limits

- **Verified.** **Container names:** 3–63 characters, lowercase letters, digits and single hyphens, must start and end with a letter or digit, no consecutive hyphens, no spaces (valid DNS label). Reserved: `$root`, `$logs`, `$web` (system containers; lifecycle ignores them). **Blob names:** any characters, 1–1024 characters (Azurite/legacy emulator: 256), case-sensitive, at most **254 path segments** in flat-namespace accounts; avoid names ending in `.`, `/`, `\`. Virtual directories are just a delimiter convention (`/`) used when listing. Metadata rules as in §1.3.
- **Verified.** Account-level limits: **"no limit"** on the number of containers or blobs per account; default account capacity 5 PiB; default request rate 20,000 rps (40,000 in listed regions); a **single block blob** targets up to 3,000 rps; max block blob ≈190.7 TiB; **Put Blob single write up to 5,000 MiB**, block up to 4,000 MiB (service version ≥ 2019-12-12). Hot-partition guidance: the partition key is account + container + blob name, so sequential/append-only naming concentrates traffic.
- Sources: https://learn.microsoft.com/en-us/rest/api/storageservices/naming-and-referencing-containers--blobs--and-metadata ; https://learn.microsoft.com/en-us/azure/storage/common/scalability-targets-standard-account ; https://learn.microsoft.com/en-us/azure/storage/blobs/scalability-targets
- **Implication:** tenant ids and worktree prefixes must be normalised to the DNS-label alphabet (e.g. `tellma-dev-<id8>-t<tenantId>`), which fits in 63 characters; blob names should use random or hashed ids (e.g. a lowercase GUID/ULID under `<entity>/<id>`) rather than monotonically increasing sequence numbers.

### 1.8 Per-tenant containers vs virtual directories

- **Verified.** Azure RBAC data roles can be assigned at **container scope** (`.../blobServices/default/containers/<name>`) as well as account/resource group/subscription; a read-only lock on the account blocks container-scoped assignments; assignments take up to 10 minutes to propagate. Lifecycle `prefixMatch` must start with a container name, so per-tenant containers give per-tenant lifecycle prefixes for free; per-tenant virtual directories need `container/<tenant>/...` prefixes (10 per rule).
- **Verified.** There is no documented cap on containers per account; container soft delete exists as a container-level safety net; Delete Container "cannot recover blobs" without container soft delete.
- **Inferred.** A per-tenant container gives: one-shot tenant teardown (`Delete Container`), container-scoped SAS/RBAC if ever needed, unambiguous lifecycle prefixes, and no risk of a prefix collision leaking across tenants. A per-tenant virtual directory in a shared container gives: fewer management objects and no create-container step at provisioning, at the cost of relying purely on name discipline for isolation. Neither affects throughput (partitioning is by full name).
- Sources: https://learn.microsoft.com/en-us/azure/storage/blobs/assign-azure-role-data-access ; https://learn.microsoft.com/en-us/azure/storage/blobs/lifecycle-management-policy-faq ; https://learn.microsoft.com/en-us/azure/storage/common/scalability-targets-standard-account
- **Implication:** per-tenant containers are free of quota concerns and are the stronger isolation boundary; the interface should take a tenant id and let the Azure adapter map it to a container while the file-system adapter maps it to a directory.

### 1.9 Authentication: `DefaultAzureCredential`, production guidance

- **Verified.** `DefaultAzureCredential` chain order (Azure.Identity docs, page dated 2025-08-13): Environment → Workload Identity → Managed Identity → Visual Studio → Visual Studio Code (needs `Azure.Identity.Broker`) → Azure CLI → Azure PowerShell → Azure Developer CLI → Interactive browser (excluded by default) → Broker. `AZURE_TOKEN_CREDENTIALS=prod|dev|<CredentialName>` narrows the chain (individual names need Azure.Identity ≥ 1.15.0; resilient managed-identity retries need ≥ 1.16.0).
- **Verified.** Microsoft's explicit production guidance: **"replace `DefaultAzureCredential` with a specific `TokenCredential` implementation, such as `ManagedIdentityCredential`"** (unpredictable fallback, debugging difficulty, performance overhead). Reuse one credential instance (token cache; high-volume apps that do not reuse credentials can hit HTTP 429 from Entra). Recommended shape: `ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(...))` in Production/Staging, `ChainedTokenCredential(VisualStudioCredential, AzureCliCredential, AzurePowerShellCredential)` in Development, wired through `AddAzureClients(...).UseCredential(...)`.
- Sources: https://learn.microsoft.com/en-us/dotnet/azure/sdk/authentication/credential-chains ; https://learn.microsoft.com/en-us/dotnet/azure/sdk/authentication/best-practices
- **Implication:** the Azure adapter should accept a `TokenCredential` (or a service URI + credential) from the host rather than constructing `DefaultAzureCredential` itself; `Azure.Identity` 1.21.0 (already pinned) is ahead of both feature floors.

### 1.10 Azurite for local development and tests

- **Verified.** Latest **Azurite 3.37.0, released 2026-08-26**, targets storage service version **2026-06-06**. Install via `npm install -g azurite`, Docker `mcr.microsoft.com/azure-storage/azurite`, or the VS Code extension; default ports 10000 (blob) / 10001 (queue) / 10002 (table); well-known account `devstoreaccount1` with the documented key; connection string `UseDevelopmentStorage=true`; IP-style URLs `http://127.0.0.1:10000/devstoreaccount1/<container>/<blob>`; options `--skipApiVersionCheck`, `--loose`, `--inMemoryPersistence`, `--oauth basic` with `--cert/--key` for HTTPS.
- **Verified.** Supported: SharedKey, OAuth, account/service SAS, block blobs, leases, snapshots, copy within the same instance, CORS, CRC64 transactional checksums, Put Block From URL. **Not supported:** blob **soft delete/undelete**, **versioning**, **lifecycle management**, immutability policies, blob query, static websites, encryption scopes, object replication, last-access-time tracking, blob expiry. Blob tags are listed as **"Blob Tags (preview)"**; the issue tracker shows filter-by-tag implemented (issue #1873 closed 2025-05-16) with a bug on multi-condition single-tag queries fixed 2026-06-01 (#2514), and 3.37.0 notes "improved server behaviour alignment for blob tags".
- **Verified.** `Testcontainers.Azurite` **4.14.0 (2026-08-14)** targets netstandard2.0/2.1, net8.0/9.0/**net10.0** and depends on `Testcontainers >= 4.14.0` (the repo pins `Testcontainers.MsSql` 4.13.0, so both would move to 4.14.0 together).
- Sources: https://github.com/Azure/Azurite/blob/main/README.md ; https://api.github.com/repos/Azure/Azurite/releases/latest ; https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azurite ; https://api.github.com/search/issues?q=repo:Azure/Azurite+%22Find+Blobs+by+Tags%22 ; https://www.nuget.org/packages/Testcontainers.Azurite
- **Implication:** the Azure adapter's conformance suite can run on Azurite (Testcontainers, `Category=Integration`) for upload/download/conditions/delete, but **soft delete, lifecycle and tag queries cannot be exercised locally** — anything the design relies on from those features must be tested in a `Live=true` suite against a real account, and the orphan sweep must not depend on them.

### 1.11 Direct-to-storage browser uploads (optional Azure-only path)

- **Verified.** A **user delegation SAS** is signed with an Entra-issued user delegation key (needs `.../blobServices/generateUserDelegationKey/action`, scoped at account or above); the key, and therefore any SAS made from it, is valid for **at most seven days**; it works for Blob and Data Lake only. `GenerateUserDelegationSasUri` bugs were fixed in SDK 12.29.1. Azurite supports service/account SAS (user-delegation support not confirmed today).
- Sources: https://learn.microsoft.com/en-us/rest/api/storageservices/create-user-delegation-sas ; CHANGELOG above
- **Implication:** a browser-direct upload is possible in SaaS but has no on-prem equivalent (file-system store) and would bypass server-side type/size/image validation; uploads through the application are the portable baseline, with SAS as a later Azure-only optimisation for very large attachments.

## 2. Server-side image processing libraries for .NET (2026)

### 2.1 SixLabors.ImageSharp

- **Verified.** Latest stable **4.1.1, published 2026-08-20** (GitHub API `published_at: 2026-08-20T06:22:25Z`; NuGet 8/20/2026). Prior: 4.1.0 (2026-08-11), 4.0.0 (2026-05-12; .NET 8 migration and API breaks). Targets **net8.0** (runs on net10.0 by roll-forward); sole dependency `System.IO.Hashing >= 8.0.0`; fully managed, no native code. NuGet total downloads ≈ **303 M**. 4.1.1 is a security-flavoured fix ("Fix CCITT decompressor bounds checks and error handling"); recent releases hardened GIF/PNG/TIFF/WebP decoders.
- **Verified — licence.** *Six Labors Split License, Version 1.0, June 2022.* Apache-2.0 terms apply when the consumer (a) uses the Work "for use in software licensed under an Open Source or Source Available license", (b) consumes it as a **Transitive Package Dependency**, (c) is a for-profit with **less than 1M USD annual gross revenue** using it as a Direct Package Dependency, or (d) is a non-profit/registered charity. Otherwise the **Six Labors Commercial License** is required. Pricing page (no date shown): commercial licence required for "a direct package dependency in closed-source, for-profit software produced by a business earning more than 1M USD in annual gross revenue"; flat per-organisation tiers **Boutique (≤10 devs) $80/mo or $799/yr; Agency (11–20) $130/mo or $1,299/yr; Enterprise (unlimited) $520/mo or $4,999/yr**, covering all five Six Labors libraries.
- **Verified — hardening for untrusted input** (Six Labors security guidance and API docs): `Image.Identify` reads dimensions/metadata without allocating pixel buffers; `DecoderOptions { TargetSize, MaxFrames, SkipMetadata, SegmentIntegrityHandling, ColorProfileHandling, Sampler, Configuration }` (all `init`-only); `MemoryAllocator.Create(new MemoryAllocatorOptions { AllocationLimitMegabytes, SingleBufferAllocationLimitMegabytes (default 1 GB, cap 2047 MB), AccumulativeAllocationLimitMegabytes (default unlimited), MaximumPoolSizeMegabytes })` bounds decode memory (their example: 256 MB per allocation and 512 MB total ≈ 64 MP RGBA); restrict codecs via a dedicated `Configuration`; keep host-level body limits and rate limiting.
- Sources: https://api.github.com/repos/SixLabors/ImageSharp/releases/latest ; https://www.nuget.org/packages/SixLabors.ImageSharp ; https://azuresearch-usnc.nuget.org/query?q=packageid:SixLabors.ImageSharp ; https://github.com/SixLabors/ImageSharp/blob/main/LICENSE ; https://sixlabors.com/pricing/ ; https://docs.sixlabors.com/articles/imagesharp/security.html ; https://docs.sixlabors.com/api/ImageSharp/SixLabors.ImageSharp.Formats.DecoderOptions.html ; https://docs.sixlabors.com/api/ImageSharp/SixLabors.ImageSharp.Memory.MemoryAllocatorOptions.html
- **Implication (verified facts + inference):** because `tellma-platform` is Apache-2.0, the platform's own use falls under clause (a) and distributions consume ImageSharp **transitively** through `Tellma.Core` (clause (b)); on a plain reading no commercial licence is owed by either. This is a legal reading of the licence text, not vendor confirmation — **flag for Ahmad** (a $799–$4,999/yr Six Labors licence removes all doubt and is cheap relative to the risk). Note the pricing page's wording ("direct package dependency in closed-source ... software") is narrower than the licence text and could be read either way for distributions that also reference ImageSharp directly.

### 2.2 SkiaSharp

- **Verified.** Latest stable **4.151.1, published 2026-08-05** (Skia milestone 151; GitHub API `2026-08-05T22:07:44Z`; NuGet 8/5/2026); prior 4.151.0 (2026-07-31), 4.150.x line still receives backports; 4.152.0 previews add Nano Server/FreeType font support and .NET 11 WASM. **MIT** licence. Targets netstandard2.0, net462, net6.0 and platform TFMs including net9.0/net10.0. NuGet total downloads ≈ **323 M** (much of it transitive via MAUI/Xamarin). The core package depends on `SkiaSharp.NativeAssets.Win32` and `.macOS` only; **Linux needs a separate native package**: `SkiaSharp.NativeAssets.Linux` (depends on system Fontconfig) or **`SkiaSharp.NativeAssets.Linux.NoDependencies` 4.151.1** whose `libSkiaSharp.so` "does not have any dependencies on third-party libraries" (only libc/libm/libdl/libpthread), i.e. no `libfontconfig1` on the App Service Linux image. Alpine/musl and Azure Linux are covered by the project's container tests.
- **Verified (API).** `SKCodec.Create(Stream|SKData|path)` exposes `Info` (dimensions, colour type), `FrameCount`, `GetScaledDimensions(scale)` and scanline/incremental decode before any pixel buffer is allocated. **No built-in allocation ceiling** was found in the API surface (inference: the caller must check `Info.Width * Info.Height` before `SKBitmap.Decode`).
- Sources: https://api.github.com/repos/mono/SkiaSharp/releases/latest ; https://www.nuget.org/packages/SkiaSharp ; https://www.nuget.org/packages/SkiaSharp.NativeAssets.Linux.NoDependencies ; https://azuresearch-usnc.nuget.org/query?q=packageid:SkiaSharp ; https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skcodec
- **Implication:** licence-clean and fast, but it adds a native library per RID to every distribution and needs the `NoDependencies` Linux asset on App Service; decompression-bomb protection is a manual pre-check.

### 2.3 Magick.NET

- **Verified.** Latest stable **14.16.0, published 2026-07-27**, bundling **ImageMagick 7.1.2-29**; **Apache-2.0**; `Magick.NET-Q8-AnyCPU` targets net8.0 and netstandard2.0 and depends on `Magick.NET.Core`; Q8 is the recommended quantum depth; x64/arm64/x86/AnyCPU variants plus OpenMP builds. NuGet total downloads of the Q8-AnyCPU package ≈ **28.6 M**. `ResourceLimits` exposes **`Width`, `Height`, `Area`, `Memory`, `Disk`, `ListLength`, `MaxMemoryRequest`, `MaxProfileSize`, `Thread`, `Throttle`, `Time`** and `LimitMemory(Percentage)` — built-in process-wide guards against oversized images; ImageMagick uses 50% of available memory for the pixel cache by default.
- **Unverified today:** the exact RID set embedded in the AnyCPU package (the `Magick.Native` NuGet search returned no package; natives ship inside the Magick.NET packages) and the Ghostscript requirement (only for PDF/PS/EPS input, per prior knowledge).
- Sources: https://api.github.com/repos/dlemstra/Magick.NET/releases/latest ; https://www.nuget.org/packages/Magick.NET-Q8-AnyCPU ; https://raw.githubusercontent.com/dlemstra/Magick.NET/main/src/Magick.NET/ResourceLimits.cs ; https://raw.githubusercontent.com/dlemstra/Magick.NET/main/docs/Readme.md
- **Implication:** the strongest built-in resource limits and the widest format support, but the heaviest native footprint (tens of MB of ImageMagick per RID) for a profile-picture/thumbnail use case; overkill unless document-conversion features arrive later.

### 2.4 System.Drawing.Common

- **Verified.** Windows-only since .NET 6; on Linux it throws `TypeInitializationException` → `PlatformNotSupportedException`; the `System.Drawing.EnableUnixSupport` switch existed only in .NET 6 and was removed in .NET 7. Microsoft's recommended replacements: SkiaSharp, ImageSharp ("tiered license"), Aspose.Drawing, Microsoft.Maui.Graphics.
- Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/system-drawing-common-windows-only
- **Implication:** not an option for a Linux App Service host.

### 2.5 Decompression-bomb protections (summary)

| Library | Pre-decode dimension read | Decode-time downscale | Memory/dimension caps | Frame cap |
|---|---|---|---|---|
| ImageSharp 4.1.1 | `Image.Identify` (verified) | `DecoderOptions.TargetSize` (verified) | `MemoryAllocatorOptions.AllocationLimitMegabytes` / `AccumulativeAllocationLimitMegabytes` (verified) | `DecoderOptions.MaxFrames` (verified) |
| SkiaSharp 4.151.1 | `SKCodec.Info` (verified) | `GetScaledDimensions` + scaled `SKImageInfo` (verified API; behaviour per codec) | none built in (inferred) | `FrameCount` readable; caller decides (inferred) |
| Magick.NET 14.16.0 | `MagickImageInfo`/ping (prior knowledge, unverified today) | via `MagickReadSettings` size hints (unverified today) | `ResourceLimits.Width/Height/Area/Memory` (verified) | `ResourceLimits.ListLength` (verified) |

- **Implication:** whichever library is chosen, the pipeline should (1) enforce a request-body limit, (2) sniff the format by magic bytes, (3) read dimensions without decoding and reject above a pixel budget, (4) decode with a hard allocator limit, (5) re-encode to a fixed format/size so stored bytes are never the user's original.

### 2.6 The usual choice for profile pictures and thumbnails

- **Verified facts:** ImageSharp is the only fully managed option with first-class decode-time limits and no native assets; SkiaSharp is MIT but needs per-RID natives and the `NoDependencies` Linux package; Magick.NET is Apache-2.0 with the heaviest natives. Microsoft's own migration page names SkiaSharp and ImageSharp first.
- **Inferred:** for "resize a profile picture to a few fixed sizes and emit JPEG/WebP", ImageSharp is the lowest-friction choice for a coding-agent-authored platform (one managed package, init-only `DecoderOptions`, allocator caps); SkiaSharp is the licence-risk-free fallback if the Split License reading in §2.1 is judged unacceptable. Both should be hidden behind a small platform interface (e.g. `IImageProcessor`) so the choice is swappable.

## 3. ASP.NET Core: serving a stream with ETag/Last-Modified, 304 handling, Cache-Control choices

### 3.1 `Results.File` / `Results.Stream` signatures (ASP.NET Core 10)

- **Verified.** `Results.File(Stream fileStream, string? contentType = null, string? fileDownloadName = null, DateTimeOffset? lastModified = null, EntityTagHeaderValue? entityTag = null, bool enableRangeProcessing = false)` is an alias for `Results.Stream(...)` with the same parameters; the stream **is disposed after the response is sent**. `Results.File(byte[] ...)` (alias of `Results.Bytes`) has `enableRangeProcessing` **before** `lastModified` in its parameter list. `Results.File(string path, ...)` resolves non-rooted paths against `IWebHostEnvironment.WebRootFileProvider`. `lastModified` configures `Last-Modified` and conditional/range handling; `entityTag` configures `ETag`.
- Source: https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.results.file
- **Implication:** the framework already implements the conditional-GET protocol; the endpoint only needs to supply a strong ETag and (optionally) last-modified, and it can hand the storage stream straight through.

### 3.2 How the framework evaluates conditions (`FileResultHelper`, `dotnet/aspnetcore` main)

- **Verified (source).** `GetPreconditionState` evaluates `If-Match` (412 if no strong match), `If-None-Match` (304 on match), `If-Modified-Since` (304 if not modified since), `If-Unmodified-Since` (412 if modified since) and takes the max state. `SetHeadersAndLog` always sets `Last-Modified`/`ETag` when supplied, adds `Accept-Ranges: bytes` when range processing is enabled, returns **304 with no body** or **412 with no body** as appropriate, validates `If-Range` (strong comparison), sets `Content-Range`/`Content-Length` for 206, and never serves a body for HEAD.
- Source: https://raw.githubusercontent.com/dotnet/aspnetcore/main/src/Shared/ResultsHelpers/FileResultHelper.cs
- **Implication:** no custom 304 code is needed; the endpoint must still perform **authorization before** returning the result (a 304 is only reached after the handler runs, so RLS/permission checks run on every request regardless of caching).

### 3.3 `EntityTagHeaderValue`

- **Verified.** Constructors `EntityTagHeaderValue(StringSegment tag)` and `(StringSegment tag, bool isWeak)`; `Any` (`*`), `IsWeak`, `Tag` ("gets the quoted tag"), `Compare(other, useStrongComparison)`, `Parse`/`TryParse`/`ParseList`. (Prior knowledge, not re-read today: the tag argument must already be quoted, e.g. `"\"abc\""`, or the constructor throws.)
- Source: https://learn.microsoft.com/en-us/dotnet/api/microsoft.net.http.headers.entitytagheadervalue
- **Implication:** build the ETag as `new EntityTagHeaderValue($"\"{token}\"")` from a value the record already holds (blob id, content hash, or the storage ETag captured at upload).

### 3.4 RFC 9110 rules the design should honour

- **Verified.** `If-Match` uses **strong** comparison; `If-None-Match` uses **weak** comparison; a matching `If-None-Match` on GET/HEAD **MUST** yield 304 (412 for other methods); a 304 SHOULD carry `ETag` and may carry `Cache-Control`, `Content-Location`, `Date`, `Expires`, `Vary`. Strong validators change whenever the representation's bytes change.
- Source: https://www.rfc-editor.org/rfc/rfc9110.html (§8.8.1, §13.1.1, §13.1.2, §15.4.5)
- **Implication:** an ETag derived from the blob id is a valid strong validator only if the bytes behind an id never change — i.e. **immutable-by-id** storage (new upload = new id). If blobs are overwritten in place, the ETag must come from a content hash or the storage ETag instead.

### 3.5 Cache-Control choices for private, authenticated, immutable-by-id blobs

- **Verified (MDN, RFC 8246).** `private` restricts storage to the browser cache (omitting it on personalised content lets shared caches serve it to other users); `no-cache` allows storage but forces revalidation; `no-store` forbids storage; `max-age=N` gives freshness; `must-revalidate` forbids serving stale; **`immutable` (RFC 8246)** tells the browser not to revalidate while fresh, intended for versioned/cache-busted URLs; with no `Cache-Control` at all, caches apply **heuristic** freshness from `Last-Modified`, so caching requirements should always be explicit.
- **Verified (ASP.NET Core).** The **output-cache** middleware's default policy caches only GET/HEAD 200 responses and **never authenticated requests** or responses that set cookies; it must run after `UseAuthentication`/`UseAuthorization`. The **response-compression** middleware's default MIME list is text/JSON/XML/JS/CSS only (images are natively compressed and excluded), compression is off for HTTPS by default (CRIME/BREACH), and it skips responses with `Content-Range`.
- Sources: https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Cache-Control ; https://httpwg.org/specs/rfc8246.html ; https://learn.microsoft.com/en-us/aspnet/core/performance/caching/output ; https://learn.microsoft.com/en-us/aspnet/core/performance/response-compression
- **Implication (inferred from the above):** for a cookie-authenticated, per-user-authorised blob GET keyed by an immutable id, the right header is **`Cache-Control: private, max-age=<long>, immutable`** plus a strong `ETag` (browser caches for the session's lifetime and never revalidates while fresh; a revoked user's browser cache is the accepted residual, as with any downloaded file); for a mutable URL (e.g. `/users/{id}/image` that changes when the user re-uploads) use **`Cache-Control: private, no-cache`** plus `ETag` so every render revalidates with a 304 costing no storage I/O. Neither output caching nor compression middleware applies to these responses.

### 3.6 Upload-side limits in ASP.NET Core (relevant to multipart-save and size limits)

- **Verified.** Kestrel `MaxRequestBodySize` default **30,000,000 bytes (≈28.6 MB)**, overridable per endpoint with `[RequestSizeLimit]`/`IHttpMaxRequestBodySizeFeature`; `FormOptions.MultipartBodyLengthLimit` default **128 MB** per section; `IFormFile` buffers ≤64 KB in memory and larger files to `ASPNETCORE_TEMP`; large uploads should stream with `MultipartReader`. Security checklist: never trust the client file name (use `Path.GetFileName` for display, a server-generated name for storage) or content type; verify **magic bytes**; store outside the web root without execute permission; enforce size limits; consider antivirus scanning in the background with a quarantine area; run with least privilege.
- Source: https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads
- **Implication:** a dedicated blob-upload endpoint gets its own body limit and streaming reader; folding binaries into the JSON save endpoint would force that endpoint's body limit and content-type handling to change for every entity.

## 4. File-system blob stores: atomic writes, path traversal, portability

### 4.1 Atomic write: temp file + rename

- **Verified (docs).** `File.Move(string source, string dest, bool overwrite)` exists since .NET Core 3.0; with `overwrite=false` an existing destination throws `IOException`; moving **across volumes is copy-then-delete** and is not atomic; `File.Copy` + `Delete` "isn't atomic ... may leave a partially-written destination file".
- **Verified (runtime source, `main`).** Windows: `FileSystem.MoveFile` calls `MoveFileEx` with **`MOVEFILE_COPY_ALLOWED`** always and **`MOVEFILE_REPLACE_EXISTING`** only when `overwrite` is true. Unix: `MoveFile` calls **`rename(2)`** (`Interop.Sys.Rename`) when the destination does not exist or `overwrite` is true; on `EXDEV` ("rename fails across devices / mount points") it falls back to `CopyFile` + `DeleteFile`; with `overwrite=false` it pre-checks with `LStat` and otherwise uses a link-or-copy path.
- **Verified.** `FileStream.Flush(bool flushToDisk)` with `true` "ensure[s] that all buffered data in intermediate file buffers is written to disk" (the OS buffer is flushed). .NET exposes **no directory fsync**, so on Linux the rename itself may not be durable across a power loss until the directory entry is flushed (inferred from POSIX semantics; not a .NET doc statement).
- Sources: https://learn.microsoft.com/en-us/dotnet/api/system.io.file.move ; https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/IO/FileSystem.Windows.cs ; https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/Common/src/Interop/Windows/Kernel32/Interop.MoveFileEx.cs ; https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/System.Private.CoreLib/src/System/IO/FileSystem.Unix.cs ; https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.flush
- **Implication:** write to `<root>/<tenant>/.tmp/<id>.part` **on the same volume** as the final path, `Flush(true)`, close, then `File.Move(tmp, final, overwrite: false)` for create-only (or `true` for replace) — this is atomic on both OSes as long as the temp directory shares the volume; a crash leaves only `.part` files under a well-known directory that the orphan sweep can delete by age.

### 4.2 Path traversal safety

- **Verified.** `Path.GetFullPath(string path, string basePath)` returns a deterministic absolute path independent of the process's current directory (the single-argument overload depends on the current directory, which "can change at any time"); it normalises `.`/`..`, does not require the path to exist, and on Windows expands short names and accepts `\\?\` device paths. `Path.IsPathFullyQualified` tells relative from rooted.
- **Verified (Win32 naming).** Windows reserves `< > : " / \ | ? *`, control characters 0–31, the device names `CON, PRN, AUX, NUL, COM1–COM9, COM¹²³, LPT1–LPT9, LPT¹²³` (also with any extension, e.g. `NUL.txt`), forbids trailing space/period in the shell, is case-insensitive by default, and enforces **MAX_PATH = 260** unless long paths are enabled via registry/Group Policy on Windows 10 1607+. Linux forbids only `/` and NUL and is case-sensitive; a backslash is a legal file-name character there.
- **Verified (ASP.NET Core guidance, §3.6):** never build paths from client-supplied names.
- Sources: https://learn.microsoft.com/en-us/dotnet/api/system.io.path.getfullpath ; https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file ; https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads
- **Implication:** the file-system store should accept only **server-generated ids matching a strict alphabet** (e.g. `^[0-9a-f]{32}$` or a lowercase ULID) and reject anything else before touching the file system; as defence in depth, resolve with `Path.GetFullPath(Path.Combine(root, rel), root)` and require the result to start with `root + Path.DirectorySeparatorChar` (ordinal on Linux, ordinal-ignore-case on Windows). Original file names travel in the database, never in the path.

### 4.3 Portability considerations (Windows/Linux)

- **Verified/Inferred as marked.** Case: Linux is case-sensitive (verified) → generate lowercase ids (inferred). Separators: use `Path.Combine`/`Path.DirectorySeparatorChar`, never hard-coded `/` or `\` (inferred from the naming docs). Length: keep the root short and the relative path shallow to stay under 260 characters on Windows without the long-path opt-in (verified limit). Reserved names cannot arise if ids are hex (inferred). Volumes: keep the temp directory under the same root as the data (verified `EXDEV` fallback). Fan-out: git splays loose objects across **256 subdirectories keyed by the first two hex characters** "to keep the number of directory entries ... to a manageable number" (verified) → the same `ab/abcdef...` layout is a proven convention for large flat id spaces (inferred applicability).
- Sources: as above plus https://git-scm.com/docs/gitrepository-layout
- **Implication:** a layout such as `<root>/<tenantId>/<kind>/<id[0..2]>/<id>` with lowercase hex ids is portable, traversal-proof by construction, and keeps directories small; the same `<kind>/<id>` string can be the Azure blob name so both adapters share one path grammar.

## 5. Client upload patterns for records with attachments in modern SaaS APIs

### 5.1 Stripe — staged upload, reference by id

- **Verified.** `POST https://files.stripe.com/v1/files` (separate subdomain) as `multipart/form-data` with `purpose` and `file`; returns a **File object (`file_...`)**; the id is then passed in other API calls (e.g. `evidence[receipt]={{FILE_ID}}` on a dispute). Browser-direct upload with the **publishable key** is supported; file links need a secret/restricted key. Per-purpose size/format limits; `identity_document` images must be < 8000×8000 px; VBA-macro Office files rejected; PDFs are validated. "You can only use an uploaded file in a single API request." Retention of never-attached files is **not documented** (unverified).
- Source: https://docs.stripe.com/file-upload
- **Trade-off recorded:** two round trips, but the record API stays JSON, the upload endpoint can be exposed to less-privileged (even browser) credentials, and validation (size/type/dimensions) happens at upload time with a specific error, not inside a record save.

### 5.2 Slack — three-step upload with explicit completion

- **Verified.** `files.getUploadURLExternal` (requires `filename` and `length`; Tier 4 rate limit) returns `upload_url` + `file_id`; the client POSTs the bytes to `upload_url`; `files.completeUploadExternal` finalises and optionally shares. **"If `files.completeUploadExternal` is not called, the upload will be aborted"** (no timeout documented). The legacy single-call `files.upload` was closed to new apps on **2024-05-16** and is sunset on **2025-11-12** (originally 2025-03-11, extended in March 2025), with Slack citing reliability for large files as the reason for the sequenced flow.
- Sources: https://docs.slack.dev/reference/methods/files.getUploadURLExternal ; https://docs.slack.dev/changelog/2024/05/16/apps/ ; https://docs.slack.dev/changelog/2025/03/17/files-upload-extension/
- **Trade-off recorded:** the "complete" call is the commit; uncompleted uploads are discarded by the platform — a direct precedent for "staged blob confirmed by the record save, otherwise swept".

### 5.3 Google Drive — simple / multipart / resumable

- **Verified.** Three upload types: **simple** (`uploadType=media`, ≤5 MB, no metadata), **multipart** (`uploadType=multipart`, ≤5 MB, metadata + media in one request), **resumable** (`uploadType=resumable`; initiate with POST → session URI in `Location`; PUT chunks that are multiples of **256 KB** with `Content-Range`; query status with `Content-Range: bytes */*` → `308 Resume Incomplete`; session URI expires after **one week**).
- Source: https://developers.google.com/workspace/drive/api/guides/manage-uploads
- **Trade-off recorded:** Google keeps the combined metadata+binary request only for small files (≤5 MB) and pushes everything larger to a staged, resumable session — the multipart-with-record pattern is explicitly a small-file convenience.

### 5.4 GitHub — parent must exist, binary posted to a separate host

- **Verified.** Release assets are uploaded with a raw binary body and `Content-Type` to the `upload_url` (host `uploads.github.com`) returned by *Create a release*; the release must exist first; `?name=` names the asset; duplicate names error until the old asset is deleted; **each file must be under 2 GiB**, up to 1,000 assets per release, no total-size or bandwidth cap.
- Sources: https://docs.github.com/en/rest/releases/assets?apiVersion=2022-11-28 ; https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases
- **Trade-off recorded:** the "record-first, then attach" order — the opposite of staging — is simple but does not support attaching a file to a record that does not exist yet (the brain dump's admin-creates-user-with-photo case).

### 5.5 Notion — staged upload with a one-hour attach window

- **Verified.** A File Upload object has statuses `pending`, `uploaded`, `expired`, `failed` and an `expiry_time`; **uploads must be attached within one hour** or they expire and cannot be attached later; once attached, `expiry_time` becomes `null` (permanent). Single-part uploads ≤ 20 MB via `multipart/form-data`; larger files use multi-part mode.
- Sources: https://developers.notion.com/reference/file-upload ; https://developers.notion.com/guides/data-apis/importing-external-files (search-confirmed one-hour rule)
- **Trade-off recorded:** the most explicit published TTL for staged uploads found today is **1 hour**; Notion pairs it with an explicit status so clients can detect expiry before the attach call.

### 5.6 Shopify — staged targets with a stable resource URL

- **Verified.** `stagedUploadsCreate` returns `stagedTargets[]{url, resourceUrl, parameters}`; the client POSTs the file to `url` with `parameters`, then passes `resourceUrl` to `fileCreate`/`productUpdate`; `fileSize` is required for VIDEO/MODEL_3D. Target validity and cleanup of unclaimed uploads are **not documented** (unverified).
- Source: https://shopify.dev/docs/api/admin-graphql/latest/mutations/stagedUploadsCreate
- **Trade-off recorded:** a staged-upload token in the form of an opaque URL that the record mutation later consumes — the GraphQL rendering of the same pattern.

### 5.7 Direct-to-storage variants

- **Verified.** Azure user delegation SAS (§1.11) enables browser→storage uploads with ≤7-day tokens; Stripe allows browser→Stripe with a publishable key. (Prior knowledge, unverified today: AWS S3 presigned PUT/POST is the same idea.)
- **Implication:** every direct-to-storage design still needs a server-side "complete/attach" step that validates and records the object; it removes bytes from the app tier but not the staging/commit/sweep logic.

### 5.8 Trade-off summary (verified precedents → inferred consequences for Tellma)

| Concern | Staged upload + token in the record save (Stripe, Slack, Notion, Shopify, Drive >5 MB) | Multipart with the record in one request (Drive ≤5 MB) |
|---|---|---|
| Record API shape | Stays JSON; one save endpoint for UI, import and MCP | Save endpoint must accept `multipart/form-data`; MCP/agent clients and import must build multipart bodies |
| Unsaved-record case (admin creating a user with a photo) | Natural: upload first, save later | Natural as well, but the whole image re-uploads on every failed save attempt |
| Validation feedback | Specific, immediate (size, type, dimensions) at upload | Mixed into save validation; the binary must be buffered/streamed alongside JSON |
| Transactionality | Blob write precedes the DB transaction; commit confirms; deletes follow commit; **no non-transactional side effect inside the save pipeline** | Blob write must happen inside or around the transaction (the brain dump's "pre-commit non-transactional side effect") |
| Orphans | Need a sweep (Slack: abort if not completed; Notion: 1-hour expiry) | None from the upload path, but failed saves after the blob write still orphan unless rolled back manually |
| Body limits | Upload endpoint has its own limit; save endpoint stays small | Save endpoint's body limit must cover the largest attachment |
| Retries/resumability | Upload can be retried or resumed independently of the record | Whole request retried |
| Round trips | 2 (upload, save) | 1 |

- **Implication:** every surveyed vendor with a general attachment model uses the staged pattern; the single-request form survives only as a small-file convenience. The design cost is the sweep, whose TTL precedent ranges from **1 hour (Notion)** to **1 week (Drive resumable sessions)**; lifecycle rules can only add a day-granular Azure backstop (§1.5).

## 6. Findings most likely to change a design decision (digest)

1. **Azure.Storage.Blobs 12.29.2 (2026-08-24) ships a `net10.0` target** and already resolves transitively at 12.26.0 through the identity server; pin it centrally so the solution has one copy (§0, §1.1).
2. **Set Blob Tags does not bump ETag/last-modified; metadata and properties writes do** — use tags, a container, or a prefix for the "staged" marker (§1.3–1.4).
3. **Lifecycle management is day-granular, up to 24 h to start, unsupported on Azurite and absent on the file system** — the app-side sweep is mandatory; Azure lifecycle is only a backstop (§1.5, §1.10).
4. **Azurite cannot exercise soft delete, versioning, lifecycle, or (reliably) tag queries** — those need a `Live=true` suite (§1.10).
5. **Container-scoped RBAC and unlimited containers per account** make per-tenant containers cheap; tenant ids must be DNS-label safe within 63 chars including the worktree prefix (§1.7–1.8).
6. **Microsoft says not to use `DefaultAzureCredential` in production** — the adapter should take a `TokenCredential` from the host (§1.9).
7. **ImageSharp's Split License (v1.0, June 2022): Apache-2.0 for open-source consumers and for transitive consumers** — the platform is Apache-2.0 and distributions consume it transitively, but the pricing page's wording is narrower; a licence question for Ahmad (§2.1).
8. **SkiaSharp needs `SkiaSharp.NativeAssets.Linux.NoDependencies` on Linux App Service and has no built-in allocation cap**; ImageSharp has `DecoderOptions.TargetSize`/`MaxFrames` and allocator limits; Magick.NET has `ResourceLimits.Width/Height/Area/Memory` (§2.2–2.5).
9. **ASP.NET Core `Results.Stream` implements 304/412/206 from a supplied ETag and Last-Modified**; the endpoint must authorise before returning it; `Cache-Control: private, max-age, immutable` fits immutable-by-id blobs, `private, no-cache` fits mutable URLs (§3).
10. **`File.Move` is `rename(2)`/`MoveFileEx` only within one volume** (cross-volume = copy+delete); keep the temp directory under the data root and reject any id that is not a strict lowercase hex/ULID (§4).
11. **All surveyed SaaS APIs use staged upload with a later attach**; Slack aborts uncompleted uploads, Notion expires unattached uploads after **1 hour**, Drive sessions after **1 week** (§5).
12. **Kestrel's default body limit is 30,000,000 bytes and `IFormFile` spills to disk above 64 KB** — a dedicated streaming upload endpoint keeps the JSON save endpoint's limits untouched (§3.6).

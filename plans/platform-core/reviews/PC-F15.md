# PC-F15: FileSystemBlobStore flake on ubuntu CI (`.part` not found under `t7/.tmp`)

## Implementer notes

### Root cause: a product race in the lazy tenant-ensure path (Linux only)

CI 42957255864 attempt 1 failed `FileSystemBlobStoreTests.Objects_land_under_the_tenant_kind_and_fan_out_directories`
with `BlobStoreException` wrapping `FileNotFoundException` on `<root>/t7/.tmp/<guid>.part`, thrown by
the **first** `OpenPart` call in `WriteOneAsync` (FileSystemBlobStore.cs line 627 at 09ab023).

The directory never vanished; it **appeared**. The test writes a primary and its thumbnail in one
`WriteAsync` to tenant 7, which was never provisioned, so both writes run in parallel (`Task.Run`) and
both take the lazy-ensure path (0017 §6.2: "a write that finds `t{tenantId}` missing runs it and
retries once"):

1. Writer A opens `t7/.tmp/A.part` with `FileMode.CreateNew`; `open(2)` fails with `ENOENT`.
2. Writer B, already in its catch block, runs `Directory.CreateDirectory(t7/.tmp)`.
3. The .NET runtime on Unix classifies A's `ENOENT` *after* the fact: when the parent directory exists
   at classification time it throws `FileNotFoundException`, otherwise `DirectoryNotFoundException`.
   It now exists, so A gets `FileNotFoundException`.
4. The store caught only `DirectoryNotFoundException`, so A's failure skipped ensure-and-retry and
   surfaced as a write failure.

Windows is not affected: `CreateFile` reports a missing directory as `ERROR_PATH_NOT_FOUND`
(`DirectoryNotFoundException`) directly, with no after-the-fact check.

It is not test isolation. Every harness uses its own GUID root under the temp directory. No product
code deletes `.tmp`, a tenant directory or a fan-out directory. `PurgeIncompleteAsync` deletes only
old `.part` files. The only `Directory.Delete` calls are in the harness, and they are per-root.

**Production impact (product bug = yes).** Any `WriteAsync` with more than one object (a primary plus
its variants: the normal image upload), or any concurrent writes, to a tenant whose directory is
missing on a Linux host can fail one write with a `BlobStoreException`. A tenant directory is missing
when provisioning never ran for it, or when it was removed out of band. That is exactly the case the
lazy ensure exists for. Provisioned tenants are unaffected because `.tmp` already exists. Severity is
low but the bug is real: the upload fails as a 500 and succeeds on retry.

### Fix

- `WriteOneAsync` now treats both `DirectoryNotFoundException` and `FileNotFoundException` from the
  create-only part-file open as "directory missing" (new `IsMissingDirectory`), on the first open and
  on the retry alike. This is sound because a `CreateNew` open can never fail with "not found" for
  the file itself. With `O_CREAT|O_EXCL`, `ENOENT` can only blame a directory on the path (an existing
  name, including a dangling symlink, is `EEXIST`).
- A test seam was added: `internal Func<string, FileStream> PartFileOpener { get; set; }`, which
  defaults to the existing `OpenPart`. The race happens inside the runtime, between `open(2)` failing
  and the runtime's own parent-directory check, so no ordering hook in product code can reproduce it.
  The seam lets a test replay the exact observed sequence instead.
- Behaviour matches 0017 §6.2: ensure once, retry once, and fail with "vanished again" when the retry
  still finds no directory. No deviation from spec.

### Evidence

- **Runtime behaviour confirmed empirically on Linux** in `mcr.microsoft.com/dotnet/sdk:10.0`
  (.NET 10.0.400; the container was removed with `--rm`). A `CreateNew` open into a missing directory
  throws `DirectoryNotFoundException` on its own. Across 20,000 races against a concurrent
  `Directory.CreateDirectory`, the open threw `FileNotFoundException` 15 times,
  `DirectoryNotFoundException` 19,607 times and succeeded 378 times. That rate (~1 in 1,300) fits a
  rare CI flake.
- New deterministic test
  `A_write_whose_open_loses_a_race_to_another_writer_creating_the_directory_still_lands`. Through the
  seam, the first open really fails for the missing `.tmp`; the test then creates the directory (the
  racing writer) and throws `FileNotFoundException`, as the Unix runtime does. The test asserts that
  the write lands, both opens happened, no `.part` is left, and `BlobTenantMissingOnWrite` was logged
  once. It runs identically on Windows and Linux.
  - **Without the fix** (`IsMissingDirectory` narrowed back to `DirectoryNotFoundException`) it fails:
    1 failed, 30 passed in `FileSystemBlobStoreTests`.
  - With the fix it passes.
- `dotnet build Tellma.slnx`: Build succeeded, 0 warnings, 0 errors.
- `dotnet format Tellma.slnx --verify-no-changes` on the two changed files: clean.
- Stability loop (Windows, `--no-build`, filter `FullyQualifiedName~Tellma.Core.Tests.Blobs`,
  345 tests per run, conformance suite included): 25/25 runs passed. The loop can only show that
  nothing regressed: the race is Linux-only and too rare (~1 in 1,300 contended opens) for a loop
  to prove its absence. The deterministic test is the proof of the fix.
- No integration test touches the blob store. Every `FileSystemBlobStore` and `IBlobStore` test lives
  in `Tellma.Core.Tests`, so there was no Testcontainers run.

### Risks

- A `FileNotFoundException` from the open for any other reason would now trigger one ensure and one
  retry before failing. With `CreateNew` there is no such other reason, and the retry is bounded
  (once), so the worst case is one extra `CreateDirectory` call.
- `PartFileOpener` is a mutable internal property on a singleton. Only tests set it, and only before
  first use; `InternalsVisibleTo` limits it to the test and host assemblies already listed.
- The existing flaky test is unchanged. It still exercises the real race (two parallel writes into an
  unprovisioned tenant), and with the fix the race is harmless rather than merely rare.

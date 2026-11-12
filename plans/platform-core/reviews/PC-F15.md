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
- A test seam was added (fixed at construction since R1-1): an internal constructor overload takes a
  `Func<string, FileStream>` part-file opener, held in a readonly field. The public constructor, the
  only one DI sees, passes the store's own `OpenPart`, which is now internal static so a test opener
  can delegate to it. The race happens inside the runtime, between `open(2)` failing
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
- The part-file opener is chosen once, at construction, through an internal constructor that DI never
  uses (it only considers public constructors). A live store cannot be re-pointed (R1-1).
- The existing flaky test is unchanged. It still exercises the real race (two parallel writes into an
  unprovisioned tenant), and with the fix the race is harmless rather than merely rare.

## Review 1

Verdict: approve after the minor below (no blocking or major issues).

What was checked and holds:

- **Root cause.** Confirmed against dotnet/runtime `main`. On Unix, `SafeFileHandle.Open` passes a failed
  `open(2)` to `Interop.CheckIo(error, path)` with `isDirError: false`. Then
  `Interop.GetExceptionForIoErrno` turns `ENOENT` into `DirectoryNotFoundException` only when
  `!Directory.Exists(parent)` at that moment, and into `FileNotFoundException` otherwise. Nothing checks
  `O_CREAT`. So a concurrent `CreateDirectory(t7/.tmp)` between the syscall and the check produces exactly
  the CI exception, and the implementer's Linux container numbers agree.
- **No real deletion.** `src/` has no `Directory.Delete` call. The only tenant-directory delete is
  `FileSystemBlobStoreHarness.RemoveTenantStorageAsync`, which the failing test never calls. Each harness
  has its own GUID root and deletes it only on dispose. `PurgeIncompleteAsync` deletes only old `.part`
  files, and tenant deprovisioning is not implemented. The failing test cannot have lost a directory.
- **Widened catch.** With `O_CREAT|O_EXCL`, `ENOENT` can only blame a path component. A dangling symlink
  as the last component gives `EEXIST`, and `EACCES` still gives `UnauthorizedAccessException`, which is
  not caught, so behaviour is unchanged. When a removed root or a dangling symlinked tenant/root makes
  `.tmp` missing, the exception is still `DirectoryNotFoundException` and follows the same path as before
  the fix. On Windows, `CREATE_NEW` reports a missing directory as `ERROR_PATH_NOT_FOUND`, so the widening
  is a no-op there.
- **Retry path.** The `finally` still deletes the `.part` file when the write was not published.
  `File.Delete` treats a missing file as success. The losing writer now logs
  `BlobTenantMissingOnWrite` and `BlobTenantEnsured`, which is accurate. Behaviour matches 0017 §6.2:
  ensure once, then retry once.
- **New test.** If `IsMissingDirectory` goes back to `DirectoryNotFoundException` only, the
  `FileNotFoundException` reaches the outer `IOException` catch, `WriteAsync` throws, and the test fails.
  The test is deterministic on both platforms, because the first real open always fails with
  `DirectoryNotFoundException`.
- **AGENTS.md.** The new members have XML docs. No spec, PC-id or `plans/` reference appears in docs,
  messages or test names.
- **Tests.** `dotnet test test/core/Tellma.Core.Tests --filter FullyQualifiedName~Tellma.Core.Tests.Blobs`
  built cleanly. 345 passed, 0 failed.

### Findings

**R1-1 (minor): the test seam can be changed after construction by production assemblies.**
`src/core/Tellma.Core/Blobs/FileSystemBlobStore.cs:105`
- **Problem.** `internal Func<string, FileStream> PartFileOpener { get; set; }` is a mutable delegate on a
  singleton. Because of the `InternalsVisibleTo` list in `Tellma.Core.csproj` (lines 56-77), it is
  writable from production assemblies too: `Tellma.Core.AspNetCore`, `Tellma.Core.Migrator`,
  `Tellma.CultureData.Generator` and `Tellma.Testing.Entities`, not only test projects. A later change in
  one of those (a decorator or a diagnostic hook) could swap the opener on the live store while writes
  are in flight.
- **Effect.** The swap is an unsynchronised write that other writes read concurrently. The delegate
  could also lose the create-only guarantee, for example a `FileMode.Create` opener that reuses a
  `.part` file. The XML doc's "Tests replace it" is the only guard. `src/` has no other mutable delegate
  seam to follow as precedent.
- **Fix.** Fix the seam at construction time, for example:
  - add an internal constructor overload (or an optional internal parameter) that takes the opener,
    keep the property get-only, and let `FileSystemBlobStoreHarness` take an optional opener and pass it
    through; or
  - make it `init`-only and set it from the harness.

  Either way the singleton cannot be re-pointed after DI has built it.

### Responses

R1-1: fixed in d4fb2ad. The opener is taken once, through an internal constructor overload (the
public constructor, which DI uses, passes the store's own `OpenPart`), and kept in a readonly
field; the settable property is gone. `OpenPart` became internal static so a test's opener can
delegate to the real create-only open. `FileSystemBlobStoreHarness` takes an optional opener and
passes it through; the race test now builds its harness with it. Blob tests 345/345, build
0 warnings, format clean.

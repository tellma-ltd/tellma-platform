// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Blobs
{
    /// <summary>
    ///     The <see cref="IBlobStore" /> on a local or mounted file system: the default for local
    ///     development and for air-gapped on-premises deployments.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Layout: <c>{RootPath}/t{tenantId}/{name}</c>, the name's <c>/</c> becoming the
    ///         directory separator. A write streams to <c>{RootPath}/t{tenantId}/.tmp/{guid}.part</c>
    ///         (a fresh GUID per object, so a primary and its variants written in parallel never share
    ///         one), flushes it to disk, then publishes it under its final name create-only: a
    ///         non-overwriting move on Windows, and on Unix a hard link (<c>link(2)</c>) followed by
    ///         an unlink of the part file. Both are atomic and both refuse an existing name, so of
    ///         two writers racing to one name exactly one wins. Both paths are inside one tenant
    ///         directory and so on one volume. A reader sees an object whole or not at all, and a
    ///         crash leaves at most a <c>.part</c> file, which <see cref="PurgeIncompleteAsync" />
    ///         removes.
    ///     </para>
    ///     <para>
    ///         On Unix the root's file system must support hard links. The store probes that before
    ///         its first write or tenant provisioning (<see cref="ProbeCreateOnlyPublish" />) and
    ///         fails closed: without hard links every write fails, and the racy
    ///         <c>rename(2)</c> is never used instead.
    ///     </para>
    ///     <para>
    ///         Every path is resolved with <see cref="Path.GetFullPath(string, string)" /> and must
    ///         stay inside the tenant's directory. The name grammar already rules traversal out; the
    ///         check is a second, independent line of defence.
    ///     </para>
    /// </remarks>
    internal sealed class FileSystemBlobStore : IBlobStore
    {
        /// <summary>The directory, inside each tenant's directory, that holds in-flight writes.</summary>
        public const string TemporaryDirectoryName = ".tmp";

        /// <summary>The extension of an in-flight write.</summary>
        public const string PartExtension = ".part";

        // Deletes are metadata operations; a handful in flight saturates a disk without starving the
        // thread pool.
        private const int DeleteParallelism = 8;

        // The copy buffer of a write, rented from the shared pool.
        private const int CopyBufferSize = 81920;

        private readonly BlobStoreMetrics _metrics;
        private readonly ILogger<FileSystemBlobStore> _logger;

        // Opens a part file create-only; OpenPart outside tests, and fixed at construction so a live
        // store can never be re-pointed while writes are in flight.
        private readonly Func<string, FileStream> _partFileOpener;

        // Set once the root has been shown to support the create-only publish; only a success is
        // remembered, so a transient failure is probed again by the next call.
        private volatile bool _publishVerified;

        /// <summary>Creates the store over the configured root.</summary>
        /// <param name="options">The store's options; the root must be set, absolute and, on Windows, short enough.</param>
        /// <param name="metrics">The store instruments.</param>
        /// <param name="logger">The store's logger.</param>
        /// <exception cref="InvalidOperationException">The root is unset, relative or too long.</exception>
        public FileSystemBlobStore(
            IOptions<FileSystemBlobStoreOptions> options, BlobStoreMetrics metrics, ILogger<FileSystemBlobStore> logger)
            : this(options, metrics, logger, OpenPart)
        {
        }

        /// <summary>
        ///     Creates the store over the configured root with a given way of opening part files.
        ///     Tests pass one to reproduce, deterministically, how a platform reports an open that
        ///     raced another writer's creation of the directory; it is fixed for the store's lifetime.
        /// </summary>
        /// <param name="options">The store's options; the root must be set, absolute and, on Windows, short enough.</param>
        /// <param name="metrics">The store instruments.</param>
        /// <param name="logger">The store's logger.</param>
        /// <param name="partFileOpener">Opens a fresh part file create-only, failing rather than reusing one.</param>
        /// <exception cref="InvalidOperationException">The root is unset, relative or too long.</exception>
        internal FileSystemBlobStore(
            IOptions<FileSystemBlobStoreOptions> options,
            BlobStoreMetrics metrics,
            ILogger<FileSystemBlobStore> logger,
            Func<string, FileStream> partFileOpener)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(partFileOpener);

            // The same rules the options validator applies, for a store constructed without one.
            string? rootPath = options.Value.RootPath;
            IReadOnlyList<string> failures = FileSystemBlobStoreOptionsValidator.Describe(rootPath, OperatingSystem.IsWindows());
            if (failures.Count > 0)
            {
                throw new InvalidOperationException(string.Join(" ", failures));
            }

            // Normalised once ("C:\a\..\b\" becomes "C:\b"), so every containment check compares
            // against the same spelling the resolved paths start with.
            RootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath!));
            _metrics = metrics;
            _logger = logger;
            _partFileOpener = partFileOpener;

            BlobStoreLog.Root(logger, RootPath);
        }

        /// <summary>The normalised root directory.</summary>
        public string RootPath { get; }

        /// <inheritdoc />
        public Task EnsureTenantAsync(int tenantId, CancellationToken cancellationToken)
        {
            string tenantRoot = TenantRoot(tenantId);
            cancellationToken.ThrowIfCancellationRequested();

            long started = Stopwatch.GetTimestamp();
            try
            {
                // Provisioning is the earliest point a tenant's storage is touched, so a root that
                // cannot publish create-only is reported here, before any upload.
                EnsurePublishSupported();
                EnsureTenantDirectory(tenantId, tenantRoot);
            }
            catch (BlobStoreException)
            {
                _metrics.Record(BlobTelemetryTagValues.StoreEnsure, BlobTelemetryTagValues.StoreFailed, Elapsed(started));
                throw;
            }

            _metrics.Record(BlobTelemetryTagValues.StoreEnsure, BlobTelemetryTagValues.StoreOk, Elapsed(started));
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task WriteAsync(int tenantId, IReadOnlyList<BlobWrite> writes, CancellationToken cancellationToken)
        {
            string tenantRoot = TenantRoot(tenantId);
            ArgumentNullException.ThrowIfNull(writes);

            // Every name and length is checked before storage is touched, so an invalid entry
            // anywhere in the list writes nothing at all.
            string[] finalPaths = new string[writes.Count];
            HashSet<string> names = new(StringComparer.Ordinal);
            long totalBytes = 0;
            for (int i = 0; i < writes.Count; i++)
            {
                BlobWrite write = writes[i] ?? throw new ArgumentNullException(nameof(writes), "A blob write list must not contain null.");
                ArgumentNullException.ThrowIfNull(write.Content, nameof(writes));
                ArgumentNullException.ThrowIfNull(write.ContentType, nameof(writes));
                if (write.Length < 0)
                {
                    throw new ArgumentException($"The blob write '{write.Name}' declares a negative length.", nameof(writes));
                }

                if (!write.Content.CanRead)
                {
                    throw new ArgumentException($"The content of the blob write '{write.Name}' is not readable.", nameof(writes));
                }

                finalPaths[i] = ResolveObjectPath(tenantRoot, write.Name, nameof(writes));
                if (!names.Add(write.Name))
                {
                    throw new ArgumentException($"The blob '{write.Name}' appears twice in one write.", nameof(writes));
                }

                totalBytes += write.Length;
            }

            if (writes.Count == 0)
            {
                return;
            }

            long started = Stopwatch.GetTimestamp();
            Exception? failure;
            try
            {
                // Before any byte is streamed: a root without create-only publish fails every write.
                EnsurePublishSupported();
                failure = await WriteAllAsync(tenantId, tenantRoot, writes, finalPaths, cancellationToken);
            }
            catch (BlobStoreException exception)
            {
                failure = exception;
            }

            // A cancelled call is neither a success nor a storage failure, so it is not metered.
            if (failure is OperationCanceledException)
            {
                ExceptionDispatchInfo.Throw(failure);
            }

            string outcome = failure switch
            {
                null => BlobTelemetryTagValues.StoreOk,
                BlobAlreadyExistsException => BlobTelemetryTagValues.StoreConflict,
                _ => BlobTelemetryTagValues.StoreFailed,
            };
            _metrics.Record(BlobTelemetryTagValues.StoreWrite, outcome, Elapsed(started), totalBytes);

            // Rethrown with its original stack, which names the failing write's own frames.
            if (failure is not null)
            {
                ExceptionDispatchInfo.Throw(failure);
            }
        }

        /// <inheritdoc />
        public Task<BlobContent?> OpenReadAsync(int tenantId, string name, CancellationToken cancellationToken)
        {
            string tenantRoot = TenantRoot(tenantId);
            ArgumentNullException.ThrowIfNull(name);
            string path = ResolveObjectPath(tenantRoot, name, nameof(name));
            cancellationToken.ThrowIfCancellationRequested();

            long started = Stopwatch.GetTimestamp();
            FileStream stream;
            try
            {
                // FileShare.Delete beside Read, so the sweep can delete an object while a download
                // of it is still streaming; the open handle keeps the bytes readable to the end.
                stream = new FileStream(path, new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read | FileShare.Delete,
                    Options = FileOptions.Asynchronous,
                });
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                // A missing object and a missing tenant directory read alike, and creating either
                // is a write's business, never a read's.
                _metrics.Record(BlobTelemetryTagValues.StoreRead, BlobTelemetryTagValues.StoreNotFound, Elapsed(started));
                return Task.FromResult<BlobContent?>(null);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _metrics.Record(BlobTelemetryTagValues.StoreRead, BlobTelemetryTagValues.StoreFailed, Elapsed(started));
                throw new BlobStoreException($"Opening the blob '{name}' of tenant {tenantId} failed.", exception);
            }

            _metrics.Record(BlobTelemetryTagValues.StoreRead, BlobTelemetryTagValues.StoreOk, Elapsed(started));
            return Task.FromResult<BlobContent?>(new BlobContent(stream, stream.Length));
        }

        /// <inheritdoc />
        public async Task DeleteAsync(int tenantId, IReadOnlyList<string> names, CancellationToken cancellationToken)
        {
            string tenantRoot = TenantRoot(tenantId);
            ArgumentNullException.ThrowIfNull(names);

            // Validate every name first: an invalid entry deletes nothing.
            string[] paths = new string[names.Count];
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i] ?? throw new ArgumentNullException(nameof(names), "A blob name list must not contain null.");
                paths[i] = ResolveObjectPath(tenantRoot, name, nameof(names));
            }

            if (paths.Length == 0)
            {
                return;
            }

            long started = Stopwatch.GetTimestamp();
            var failures = new Exception?[paths.Length];
            await Parallel.ForAsync(
                0,
                paths.Length,
                new ParallelOptions { MaxDegreeOfParallelism = DeleteParallelism, CancellationToken = cancellationToken },
                (i, _) =>
                {
                    failures[i] = DeleteFile(paths[i]);
                    return ValueTask.CompletedTask;
                });

            // Every delete was attempted; the first failure in list order is reported.
            int failed = Array.FindIndex(failures, static failure => failure is not null);
            if (failed < 0)
            {
                _metrics.Record(BlobTelemetryTagValues.StoreDelete, BlobTelemetryTagValues.StoreOk, Elapsed(started));
                return;
            }

            _metrics.Record(BlobTelemetryTagValues.StoreDelete, BlobTelemetryTagValues.StoreFailed, Elapsed(started));
            int count = failures.Count(static failure => failure is not null);
            throw new BlobStoreException(
                $"Deleting {count} of {paths.Length} blobs of tenant {tenantId} failed; the first was '{names[failed]}'.",
                failures[failed]);
        }

        /// <inheritdoc />
        public IAsyncEnumerable<BlobStoreEntry> ListAsync(int tenantId, string prefix, CancellationToken cancellationToken)
        {
            // Validated eagerly, so a bad prefix throws at the call rather than at the first MoveNext.
            string tenantRoot = TenantRoot(tenantId);
            ArgumentNullException.ThrowIfNull(prefix);
            if (!BlobName.IsValidPrefix(prefix))
            {
                throw new ArgumentException($"'{prefix}' is not a valid blob listing prefix: a kind, or a kind and a fan-out segment.", nameof(prefix));
            }

            string directory = ResolveContainedPath(tenantRoot, prefix, nameof(prefix));
            return ListCoreAsync(tenantId, prefix, directory, cancellationToken);
        }

        /// <inheritdoc />
        public Task PurgeIncompleteAsync(int tenantId, DateTimeOffset olderThan, CancellationToken cancellationToken)
        {
            string tenantRoot = TenantRoot(tenantId);
            string temporary = Path.Join(tenantRoot, TemporaryDirectoryName);
            DateTime cutoff = olderThan.UtcDateTime;

            long started = Stopwatch.GetTimestamp();
            int failed = 0;
            Exception? firstFailure = null;
            try
            {
                foreach (FileInfo part in new DirectoryInfo(temporary).EnumerateFiles())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Only the store's own artefacts, and only those old enough that no write can
                    // still be streaming into them.
                    if (!part.Name.EndsWith(PartExtension, StringComparison.Ordinal) || part.LastWriteTimeUtc >= cutoff)
                    {
                        continue;
                    }

                    Exception? failure = DeleteFile(part.FullName);
                    if (failure is not null)
                    {
                        failed++;
                        firstFailure ??= failure;
                    }
                }
            }
            catch (DirectoryNotFoundException)
            {
                // No tenant directory, or no .tmp in it: nothing was ever left incomplete.
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _metrics.Record(BlobTelemetryTagValues.StorePurge, BlobTelemetryTagValues.StoreFailed, Elapsed(started));
                throw new BlobStoreException($"Purging incomplete blob writes of tenant {tenantId} failed.", exception);
            }

            if (firstFailure is not null)
            {
                _metrics.Record(BlobTelemetryTagValues.StorePurge, BlobTelemetryTagValues.StoreFailed, Elapsed(started));
                throw new BlobStoreException($"Purging {failed} incomplete blob writes of tenant {tenantId} failed.", firstFailure);
            }

            _metrics.Record(BlobTelemetryTagValues.StorePurge, BlobTelemetryTagValues.StoreOk, Elapsed(started));
            return Task.CompletedTask;
        }

        /// <summary>
        ///     Checks that the file system under <paramref name="rootPath" /> can publish create-only:
        ///     on Unix, by hard-linking a probe file under the root; on Windows, where the
        ///     non-overwriting move needs nothing extra, it touches nothing. The startup check reuses
        ///     it to fail a host before it serves traffic.
        /// </summary>
        /// <param name="rootPath">The store's root; created when absent.</param>
        /// <returns>Null when publishing is supported; otherwise what is wrong, naming the root.</returns>
        public static string? ProbeCreateOnlyPublish(string rootPath)
        {
            ArgumentNullException.ThrowIfNull(rootPath);
            if (OperatingSystem.IsWindows())
            {
                return null;
            }

            string probe = Path.Join(rootPath, ".probe-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
            string link = probe + ".link";
            try
            {
                Directory.CreateDirectory(rootPath);
                File.WriteAllBytes(probe, []);
                return UnixHardLinks.TryLink(probe, link, out int errorNumber) switch
                {
                    HardLinkResult.Linked => null,
                    HardLinkResult.NotSupported =>
                        $"The file system under the blob root '{rootPath}' does not support hard links, which the file-system blob store needs to publish objects create-only ({UnixHardLinks.Describe(errorNumber)}).",
                    HardLinkResult.AlreadyExists or HardLinkResult.Failed or _ => $"Hard-linking a probe file under the blob root '{rootPath}' failed ({UnixHardLinks.Describe(errorNumber)}).",
                };
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return $"The blob root '{rootPath}' could not be probed for hard links: {exception.Message}";
            }
            finally
            {
                _ = DeleteFile(link);
                _ = DeleteFile(probe);
            }
        }

        /// <summary>
        ///     Opens a fresh part file, failing rather than reusing one: the store's own opener, and
        ///     the real open a test's opener delegates to.
        /// </summary>
        /// <param name="partPath">The part file.</param>
        /// <returns>The open part file.</returns>
        internal static FileStream OpenPart(string partPath)
        {
            return new FileStream(partPath, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
            });
        }

        /// <summary>Seconds elapsed since a <see cref="Stopwatch.GetTimestamp" /> reading.</summary>
        /// <param name="started">The reading.</param>
        /// <returns>The elapsed seconds.</returns>
        private static double Elapsed(long started)
        {
            return Stopwatch.GetElapsedTime(started).TotalSeconds;
        }

        /// <summary>Deletes one file, treating a missing file or directory as success.</summary>
        /// <param name="path">The file.</param>
        /// <returns>The failure, or null when the file is gone.</returns>
        private static Exception? DeleteFile(string path)
        {
            try
            {
                File.Delete(path);
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return exception;
            }
        }

        /// <summary>Copies exactly the declared length of a write's content into its part file.</summary>
        /// <param name="write">The write.</param>
        /// <param name="part">The part file.</param>
        /// <param name="cancellationToken">Cancels the copy.</param>
        /// <returns>A task that completes when the copy is done.</returns>
        /// <exception cref="BlobStoreException">The content yields fewer or more bytes than declared.</exception>
        private static async Task CopyExactlyAsync(BlobWrite write, FileStream part, CancellationToken cancellationToken)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
            try
            {
                long copied = 0;
                while (true)
                {
                    // Never asks for more than one byte past the declared length: enough to detect
                    // an overrun without reading an unbounded stream to its end.
                    int request = (int)Math.Min(buffer.Length, write.Length - copied + 1);
                    int read = await write.Content.ReadAsync(buffer.AsMemory(0, request), cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    copied += read;
                    if (copied > write.Length)
                    {
                        throw new BlobStoreException(
                            $"The content of the blob '{write.Name}' runs past its declared length of {write.Length} bytes.", null);
                    }

                    await part.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                if (copied != write.Length)
                {
                    throw new BlobStoreException(
                        $"The content of the blob '{write.Name}' ended after {copied} of its declared {write.Length} bytes.", null);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>Whether a failed create-only open of a part file means its directory is missing.</summary>
        /// <remarks>
        ///     A create-only open never fails because the file itself is absent, so "not found" can
        ///     only blame a directory on the path. On Unix the runtime reports that as
        ///     <see cref="FileNotFoundException" /> rather than <see cref="DirectoryNotFoundException" />
        ///     when, by the time it inspects the failure, the directory exists: exactly what happens
        ///     when a concurrent write of the same tenant creates it in between.
        /// </remarks>
        /// <param name="exception">The failure of the open.</param>
        /// <returns>True when the directory is missing (or was missing when the open ran).</returns>
        private static bool IsMissingDirectory(IOException exception)
        {
            return exception is DirectoryNotFoundException or FileNotFoundException;
        }

        /// <summary>The directory of a tenant, after checking the tenant id.</summary>
        /// <param name="tenantId">The tenant; positive.</param>
        /// <returns><c>{RootPath}/t{tenantId}</c>.</returns>
        private string TenantRoot(int tenantId)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
            return Path.Join(RootPath, "t" + tenantId.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>The path of an object, after checking its name.</summary>
        /// <param name="tenantRoot">The tenant's directory.</param>
        /// <param name="name">The object's name.</param>
        /// <param name="parameterName">The argument the name came from, for the exception.</param>
        /// <returns>The object's absolute path, inside the tenant's directory.</returns>
        /// <exception cref="ArgumentException">The name is invalid, or resolves outside the tenant's directory.</exception>
        private static string ResolveObjectPath(string tenantRoot, string name, string parameterName)
        {
            return BlobName.IsValid(name)
                ? ResolveContainedPath(tenantRoot, name, parameterName)
                : throw new ArgumentException($"'{name}' is not a valid blob name.", parameterName);
        }

        /// <summary>Resolves a grammar-checked relative name under the tenant's directory, refusing any escape.</summary>
        /// <param name="tenantRoot">The tenant's directory.</param>
        /// <param name="relative">The name or prefix, with <c>/</c> separators.</param>
        /// <param name="parameterName">The argument the name came from, for the exception.</param>
        /// <returns>The absolute path.</returns>
        /// <exception cref="ArgumentException">The path resolves outside the tenant's directory.</exception>
        private static string ResolveContainedPath(string tenantRoot, string relative, string parameterName)
        {
            string full = Path.GetFullPath(relative.Replace(BlobName.SegmentSeparator, Path.DirectorySeparatorChar), tenantRoot);

            // Ordinal on every platform: the resolved path is built from the tenant root's own
            // spelling, so anything that does not start with it exactly has escaped.
            string boundary = tenantRoot + Path.DirectorySeparatorChar;
            return full.StartsWith(boundary, StringComparison.Ordinal) && full.Length > boundary.Length
                ? full
                : throw new ArgumentException($"The blob name '{relative}' resolves outside the tenant's storage.", parameterName);
        }

        /// <summary>Probes the root for create-only publish once, remembering only a success.</summary>
        /// <exception cref="BlobStoreException">The root cannot publish create-only.</exception>
        private void EnsurePublishSupported()
        {
            if (_publishVerified)
            {
                return;
            }

            string? failure = ProbeCreateOnlyPublish(RootPath);
            if (failure is not null)
            {
                BlobStoreLog.PublishUnsupported(_logger, RootPath, failure);
                throw new BlobStoreException(failure, null);
            }

            _publishVerified = true;
        }

        /// <summary>Creates the tenant's directory and its temporary directory.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="tenantRoot">The tenant's directory.</param>
        /// <exception cref="BlobStoreException">The directories could not be created.</exception>
        private void EnsureTenantDirectory(int tenantId, string tenantRoot)
        {
            try
            {
                // Creates t{id} and t{id}/.tmp in one call; a no-op when both exist.
                Directory.CreateDirectory(Path.Join(tenantRoot, TemporaryDirectoryName));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new BlobStoreException($"Creating the blob storage of tenant {tenantId} failed.", exception);
            }

            BlobStoreLog.TenantEnsured(_logger, tenantId, tenantRoot);
        }

        /// <summary>Runs every write, each in parallel, and reports the first failure in list order.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="tenantRoot">The tenant's directory.</param>
        /// <param name="writes">The validated writes.</param>
        /// <param name="finalPaths">Each write's resolved final path.</param>
        /// <param name="cancellationToken">Cancels the writes.</param>
        /// <returns>The first failure in list order, or null when every write succeeded.</returns>
        private async Task<Exception?> WriteAllAsync(
            int tenantId, string tenantRoot, IReadOnlyList<BlobWrite> writes, string[] finalPaths, CancellationToken cancellationToken)
        {
            // One write runs inline; several run on the pool so their I/O overlaps.
            var tasks = new Task[writes.Count];
            for (int i = 0; i < writes.Count; i++)
            {
                BlobWrite write = writes[i];
                string finalPath = finalPaths[i];
                tasks[i] = writes.Count == 1
                    ? WriteOneAsync(tenantId, tenantRoot, write, finalPath, cancellationToken)
                    : Task.Run(() => WriteOneAsync(tenantId, tenantRoot, write, finalPath, cancellationToken), CancellationToken.None);
            }

            try
            {
                await Task.WhenAll(tasks);
                return null;
            }
            catch (Exception)
            {
                // Every write has finished; the earliest failure in list order wins, whatever
                // order they failed in.
                foreach (Task task in tasks)
                {
                    if (task.IsFaulted)
                    {
                        return task.Exception.InnerException;
                    }

                    if (task.IsCanceled)
                    {
                        return new OperationCanceledException(cancellationToken);
                    }
                }

                throw;
            }
        }

        /// <summary>Writes one object: part file, flush to disk, create-only publish.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="tenantRoot">The tenant's directory.</param>
        /// <param name="write">The write.</param>
        /// <param name="finalPath">The object's final path.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>A task that completes when the object is in place.</returns>
        private async Task WriteOneAsync(
            int tenantId, string tenantRoot, BlobWrite write, string finalPath, CancellationToken cancellationToken)
        {
            // A cheap early answer so an obvious conflict costs no copy. It is only an optimisation:
            // a racing writer can pass it too, and the create-only publish below is what decides.
            if (File.Exists(finalPath))
            {
                throw new BlobAlreadyExistsException(write.Name);
            }

            string partPath = Path.Join(
                tenantRoot, TemporaryDirectoryName, Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + PartExtension);
            bool published = false;
            try
            {
                FileStream part;
                try
                {
                    part = _partFileOpener(partPath);
                }
                catch (IOException exception) when (IsMissingDirectory(exception))
                {
                    // The tenant's directory (or its .tmp) is missing: it was never provisioned, or
                    // was removed out of band. Create it once and retry once; nothing has been read
                    // from the content yet, so the retry starts clean.
                    BlobStoreLog.TenantMissingOnWrite(_logger, tenantId);
                    EnsureTenantDirectory(tenantId, tenantRoot);
                    try
                    {
                        part = _partFileOpener(partPath);
                    }
                    catch (IOException retryException) when (IsMissingDirectory(retryException))
                    {
                        throw new BlobStoreException(
                            $"Writing the blob '{write.Name}' of tenant {tenantId} failed: its storage vanished again after being created.", retryException);
                    }
                }

                // Stream, flush the stream's buffer, then force the bytes to the device, so the
                // publish below can never expose a name whose bytes a crash would lose.
                await using (part)
                {
                    await CopyExactlyAsync(write, part, cancellationToken);
                    await part.FlushAsync(cancellationToken);
                    part.Flush(flushToDisk: true);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
                published = Publish(tenantId, write.Name, partPath, finalPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new BlobStoreException($"Writing the blob '{write.Name}' of tenant {tenantId} failed.", exception);
            }
            finally
            {
                // A failed or cancelled write leaves no part file behind when it can help it; one
                // that survives (a crash) is the purge's.
                if (!published)
                {
                    _ = DeleteFile(partPath);
                }
            }
        }

        /// <summary>
        ///     Publishes a complete part file under its final name, atomically and without ever
        ///     replacing an existing name.
        /// </summary>
        /// <remarks>
        ///     On Windows, <c>File.Move(overwrite: false)</c> is <c>MoveFileEx</c> without
        ///     <c>MOVEFILE_REPLACE_EXISTING</c>, which the file system refuses atomically when the
        ///     name exists. On Unix the same call is a check followed by <c>rename(2)</c>, which
        ///     replaces a name created in between, so the publish is <c>link(2)</c> instead, then an
        ///     unlink of the part file.
        /// </remarks>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="name">The object's name.</param>
        /// <param name="partPath">The complete, flushed part file.</param>
        /// <param name="finalPath">The object's final path.</param>
        /// <returns>True: the part file no longer needs deleting (it was moved, or its unlink was attempted).</returns>
        /// <exception cref="BlobAlreadyExistsException">The final name exists.</exception>
        /// <exception cref="BlobStoreException">The file system cannot publish create-only.</exception>
        private bool Publish(int tenantId, string name, string partPath, string finalPath)
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    File.Move(partPath, finalPath, overwrite: false);
                    return true;
                }
                catch (IOException) when (File.Exists(finalPath))
                {
                    // Another write of the same name published first; the move refused to
                    // overwrite it.
                    throw new BlobAlreadyExistsException(name);
                }
            }

            switch (UnixHardLinks.TryLink(partPath, finalPath, out int errorNumber))
            {
                case HardLinkResult.Linked:
                    break;
                case HardLinkResult.AlreadyExists:
                    throw new BlobAlreadyExistsException(name);
                case HardLinkResult.NotSupported:
                    // Fail closed: without hard links there is no create-only publish, and the
                    // racy rename is never an acceptable substitute.
                    throw new BlobStoreException(
                        $"Publishing the blob '{name}' of tenant {tenantId} failed: the file system under '{RootPath}' does not support hard links, which the file-system blob store requires ({UnixHardLinks.Describe(errorNumber)}).", null);
                case HardLinkResult.Failed:
                default:
                    throw new BlobStoreException(
                        $"Publishing the blob '{name}' of tenant {tenantId} failed: {UnixHardLinks.Describe(errorNumber)}.", null);
            }

            // The object is published. A part file that cannot be unlinked is only a second name
            // for the same bytes: logged and left for the purge, never the writer's failure.
            Exception? unlinkFailure = DeleteFile(partPath);
            if (unlinkFailure is not null)
            {
                BlobStoreLog.PartFileLeft(_logger, unlinkFailure, tenantId, partPath);
            }

            return true;
        }

        /// <summary>Enumerates a validated prefix's objects, one fan-out directory at a time.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="prefix">The validated prefix.</param>
        /// <param name="directory">The prefix's resolved directory.</param>
        /// <param name="cancellationToken">Cancels the enumeration.</param>
        /// <returns>The objects, in ordinal order of their names.</returns>
        private async IAsyncEnumerable<BlobStoreEntry> ListCoreAsync(
            int tenantId, string prefix, string directory, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            long started = Stopwatch.GetTimestamp();
            string outcome = BlobTelemetryTagValues.StoreOk;
            try
            {
                // A kind prefix walks its fan-out directories in order; a fan-out prefix is one.
                IReadOnlyList<(string Prefix, string Directory)> segments = prefix.Contains(BlobName.SegmentSeparator, StringComparison.Ordinal)
                    ? [(prefix, directory)]
                    : ReadFanOuts(tenantId, prefix, directory, ref outcome);

                foreach ((string segmentPrefix, string segmentDirectory) in segments)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // One fan-out directory is materialised at a time (1/256 of a kind), which
                    // bounds memory and lets the entries be sorted into the order Azure lists in.
                    foreach (BlobStoreEntry entry in ReadEntries(tenantId, segmentPrefix, segmentDirectory, ref outcome))
                    {
                        yield return entry;
                    }

                    await Task.Yield();
                }
            }
            finally
            {
                // An early stop by the consumer is a success; a cancellation is not metered.
                if (!cancellationToken.IsCancellationRequested)
                {
                    _metrics.Record(BlobTelemetryTagValues.StoreList, outcome, Elapsed(started));
                }
            }
        }

        /// <summary>The fan-out directories of a kind, in order.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="kind">The kind.</param>
        /// <param name="directory">The kind's directory.</param>
        /// <param name="outcome">Set to the failed outcome when the directory cannot be read.</param>
        /// <returns>Each fan-out segment's prefix and directory; empty when the kind has none.</returns>
        private static List<(string Prefix, string Directory)> ReadFanOuts(int tenantId, string kind, string directory, ref string outcome)
        {
            try
            {
                List<(string Prefix, string Directory)> segments = [];
                foreach (DirectoryInfo fanOut in new DirectoryInfo(directory).EnumerateDirectories())
                {
                    // Anything that is not a fan-out segment cannot hold a valid name; skip it.
                    string segmentPrefix = kind + BlobName.SegmentSeparator + fanOut.Name;
                    if (fanOut.Name.Length == BlobName.FanOutLength && BlobName.IsValidPrefix(segmentPrefix))
                    {
                        segments.Add((segmentPrefix, fanOut.FullName));
                    }
                }

                segments.Sort(static (a, b) => string.CompareOrdinal(a.Prefix, b.Prefix));
                return segments;
            }
            catch (DirectoryNotFoundException)
            {
                return [];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                outcome = BlobTelemetryTagValues.StoreFailed;
                throw new BlobStoreException($"Listing the blobs under '{kind}' of tenant {tenantId} failed.", exception);
            }
        }

        /// <summary>The objects of one fan-out directory, in order.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="segmentPrefix">The fan-out segment's prefix, <c>{kind}/{k0k1}</c>.</param>
        /// <param name="directory">The segment's directory.</param>
        /// <param name="outcome">Set to the failed outcome when the directory cannot be read.</param>
        /// <returns>The segment's objects; empty when the directory does not exist.</returns>
        private static List<BlobStoreEntry> ReadEntries(int tenantId, string segmentPrefix, string directory, ref string outcome)
        {
            try
            {
                List<BlobStoreEntry> entries = [];
                foreach (FileInfo file in new DirectoryInfo(directory).EnumerateFiles())
                {
                    // Only names the contract can address: a stray file (or one differing only in
                    // case on a case-insensitive volume) is not a blob.
                    string name = segmentPrefix + BlobName.SegmentSeparator + file.Name;
                    if (BlobName.IsValid(name))
                    {
                        entries.Add(new BlobStoreEntry(name, file.Length, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)));
                    }
                }

                entries.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
                return entries;
            }
            catch (DirectoryNotFoundException)
            {
                return [];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                outcome = BlobTelemetryTagValues.StoreFailed;
                throw new BlobStoreException($"Listing the blobs under '{segmentPrefix}' of tenant {tenantId} failed.", exception);
            }
        }
    }
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using System.Text;
using Tellma.Core.Imaging.PdfWorker;

namespace Tellma.Core.Imaging.Workers
{
    /// <summary>A page a worker rendered, checked: a PNG of the given dimensions.</summary>
    /// <param name="Png">The PNG.</param>
    /// <param name="Width">Its width.</param>
    /// <param name="Height">Its height.</param>
    internal readonly record struct RenderedPage(byte[] Png, int Width, int Height);

    /// <summary>
    ///     Renders PDF thumbnails in disposable worker processes, one per document: starts a
    ///     worker, waits for it to be ready, caps its memory and CPU time, sends the document, and
    ///     checks the answer. The worker is killed when the call's token is cancelled, and always
    ///     when the call ends, so a hostile document can stop or starve its own worker, never the
    ///     host.
    /// </summary>
    /// <param name="launcher">Starts the workers.</param>
    /// <param name="memoryLimit">The memory each worker may add once ready, in bytes.</param>
    internal sealed class PdfWorkerClient(PdfWorkerLauncher launcher, long memoryLimit) : IDisposable
    {
        /// <summary>
        ///     How long the call waits for a worker it killed to be gone: a killed process ends at
        ///     once unless the kernel holds it, and the call's slot stays taken meanwhile.
        /// </summary>
        public static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(30);

        /// <summary>
        ///     How long a worker whose pipes broke is given to finish exiting, so its exit code can be
        ///     reported; past that it is killed like any other.
        /// </summary>
        public static readonly TimeSpan DeathWait = TimeSpan.FromSeconds(5);

        // The workers running now, so disposal can kill them.
        private readonly ConcurrentDictionary<WorkerProcess, byte> _live = new();
        private volatile bool _disposed;

        /// <summary>The launcher.</summary>
        public PdfWorkerLauncher Launcher => launcher;

        /// <summary>How many workers run now; for the suite.</summary>
        internal int LiveWorkers => _live.Count;

        /// <summary>The process ids of the workers running now; for the suite.</summary>
        internal IReadOnlyList<int> LiveWorkerIds => [.. _live.Keys.Select(static worker => worker.Id)];

        /// <summary>Raised with each worker once it is ready and limited, before it gets the document; for the suite.</summary>
        internal event Action<WorkerProcess>? Ready;

        /// <summary>Renders page 1 of <paramref name="pdf" /> in a new worker.</summary>
        /// <remarks>Blocks the calling thread for the worker's life; the processor calls it from its own.</remarks>
        /// <param name="pdf">The document, at most <see cref="WorkerProtocol.MaxInputBytes" />.</param>
        /// <param name="size">The thumbnail square's side, at most <see cref="WorkerProtocol.MaxThumbnailSize" />.</param>
        /// <param name="cpuTime">The CPU time the worker may use once ready; null for no limit.</param>
        /// <param name="cancellationToken">Kills the worker when cancelled.</param>
        /// <returns>The checked page.</returns>
        /// <exception cref="ImageFailure">The worker refused the document: it cannot be rendered within the caps.</exception>
        /// <exception cref="PdfWorkerException">The worker failed: it could not start, died, or broke the protocol.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was cancelled.</exception>
        public RenderedPage Render(byte[] pdf, int size, TimeSpan? cpuTime, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(pdf);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(pdf.Length, WorkerProtocol.MaxInputBytes, nameof(pdf));
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(size, WorkerProtocol.MaxThumbnailSize);
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);

            WorkerProcess worker = launcher.Start();
            _live[worker] = 0;
            try
            {
                // Section: a cancelled call kills its worker, which breaks the pipes a read or a
                // write below may be blocked on. Disposal racing the start kills it too.
                using CancellationTokenRegistration kill = cancellationToken.Register(static state => ((WorkerProcess)state!).Kill(), worker);
                if (_disposed)
                {
                    worker.Kill();
                }

                // Section: ready, then the limits, then the document. The worker has read nothing of
                // the upload before its limits are in place; on Linux it must have installed its
                // system-call filter.
                WorkerCapabilities capabilities = Talk(worker, () => WorkerProtocol.ReadReady(worker.Output), cancellationToken);
                if (OperatingSystem.IsLinux() && !capabilities.HasFlag(WorkerCapabilities.SystemCallFilter))
                {
                    throw new PdfWorkerException(PdfWorkerFailure.Protocol, "The worker started without its system-call filter.");
                }

                try
                {
                    worker.ApplyLimits(new WorkerLimits(memoryLimit, cpuTime));
                }
                catch (PdfWorkerException)
                {
                    // A worker killed by the cancellation cannot be measured; that is no failure.
                    cancellationToken.ThrowIfCancellationRequested();
                    throw;
                }

                Ready?.Invoke(worker);
                _ = Talk(
                    worker,
                    () =>
                    {
                        WorkerProtocol.WriteRequest(worker.Input, size, pdf);
                        return true;
                    },
                    cancellationToken);
                WorkerResult result = Talk(worker, () => WorkerProtocol.ReadResult(worker.Output, size), cancellationToken);

                // Section: the answer. A refusal is the document's; a failure is the worker's.
                switch (result.Status)
                {
                    case WorkerStatus.Rendered:
                        (int width, int height) = WorkerPng.Check(result.Payload, size);
                        return new RenderedPage(result.Payload, width, height);

                    case WorkerStatus.Refused:
                        throw ImageFailure.DecodeFailed(Printable(result.Payload));

                    case WorkerStatus.Failed:
                    default:
                        throw new PdfWorkerException(PdfWorkerFailure.Failed, $"The worker failed: {Printable(result.Payload)}");
                }
            }
            finally
            {
                // Section: the worker never outlives the call. The call returns, and the caller
                // gives back its slot, only once the worker is gone.
                worker.Kill();
                _ = worker.WaitForExit(ExitWait);
                _ = _live.TryRemove(worker, out _);
                worker.Dispose();
            }
        }

        /// <summary>Kills every worker still running.</summary>
        public void Dispose()
        {
            _disposed = true;
            foreach (WorkerProcess worker in _live.Keys)
            {
                worker.Kill();
            }
        }

        /// <summary>
        ///     Runs one exchange with the worker, telling apart the call's cancellation, the worker's
        ///     death and a protocol violation.
        /// </summary>
        /// <typeparam name="T">The exchange's result.</typeparam>
        /// <param name="worker">The worker.</param>
        /// <param name="exchange">The read or write.</param>
        /// <param name="cancellationToken">The call's token, which kills the worker.</param>
        /// <returns>The exchange's result.</returns>
        private static T Talk<T>(WorkerProcess worker, Func<T> exchange, CancellationToken cancellationToken)
        {
            try
            {
                T result = exchange();
                cancellationToken.ThrowIfCancellationRequested();
                return result;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or ObjectDisposedException)
            {
                // A killed worker's pipes break: that is the cancellation, not a failure.
                cancellationToken.ThrowIfCancellationRequested();
                if (exception is InvalidDataException)
                {
                    throw new PdfWorkerException(PdfWorkerFailure.Protocol, exception.Message, exception);
                }

                // The worker went away: by itself, or stopped by a limit. Its exit code and its last
                // words say which.
                _ = worker.WaitForExit(DeathWait);
                int? exitCode = worker.ExitCode;
                string memory = worker.PeakMemory is long peak ? $"; peak committed memory {peak / (1024 * 1024)} MiB" : string.Empty;
                string error = worker.ErrorOutput is { Length: > 0 } text ? $"; it wrote: {text}" : string.Empty;
                throw new PdfWorkerException(
                    PdfWorkerFailure.Exited,
                    $"The worker ended before it answered (exit code {exitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}{memory}){error}",
                    exception)
                {
                    ExitCode = exitCode,
                };
            }
        }

        /// <summary>A worker's detail as one line of printable ASCII, since a compromised worker controls it.</summary>
        /// <param name="detail">The UTF-8 detail.</param>
        /// <returns>The text.</returns>
        internal static string Printable(byte[] detail)
        {
            var text = new StringBuilder(detail.Length);
            foreach (char c in Encoding.UTF8.GetString(detail))
            {
                _ = text.Append(c is >= ' ' and < '\x7F' ? c : '?');
            }

            return text.ToString();
        }
    }
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;

namespace Tellma.Core.Imaging.Workers
{
    /// <summary>The limits the host puts on a worker once it is ready, before it sends the upload.</summary>
    /// <param name="Memory">The memory the worker may add to what its runtime holds at that moment, in bytes.</param>
    /// <param name="CpuTime">The CPU time the worker may still use; null for no limit.</param>
    internal readonly record struct WorkerLimits(long Memory, TimeSpan? CpuTime);

    /// <summary>
    ///     A started worker process and the host's three pipes to it, behind the operating system's
    ///     own controls: a Job Object on Windows, resource limits on Linux.
    /// </summary>
    /// <remarks>
    ///     Every member may be called from any thread. <see cref="Kill" /> may race a read or write
    ///     on the pipes, which it unblocks: a killed worker's pipes break.
    /// </remarks>
    internal abstract class WorkerProcess : IDisposable
    {
        /// <summary>The exit code a worker the host killed reports on Windows, as SIGKILL reports on Linux.</summary>
        public const int KilledExitCode = 137;

        /// <summary>The most of a worker's standard error the host keeps for its log, in bytes.</summary>
        public const int MaxErrorBytes = 2048;

        /// <summary>The worker's process id.</summary>
        public abstract int Id { get; }

        /// <summary>The worker's standard input, written by the host.</summary>
        public abstract Stream Input { get; }

        /// <summary>The worker's standard output, read by the host.</summary>
        public abstract Stream Output { get; }

        /// <summary>The worker's exit code, once it has exited; null while it runs.</summary>
        public abstract int? ExitCode { get; }

        /// <summary>The most memory the worker committed, where the platform reports it; for the log.</summary>
        public virtual long? PeakMemory => null;

        /// <summary>The start of what the worker wrote to standard error, made safe for a log line.</summary>
        public string ErrorOutput => Error.Text;

        /// <summary>Drains standard error, keeping its start.</summary>
        protected abstract ErrorTail Error { get; }

        /// <summary>Applies the host's limits; called once, after the ready frame and before the request.</summary>
        /// <param name="limits">The limits.</param>
        /// <exception cref="PdfWorkerException">The limits could not be applied.</exception>
        public abstract void ApplyLimits(WorkerLimits limits);

        /// <summary>Kills the worker at once, if it still runs; idempotent.</summary>
        public abstract void Kill();

        /// <summary>Waits for the worker to exit.</summary>
        /// <param name="timeout">How long to wait.</param>
        /// <returns>True once it has exited.</returns>
        public abstract bool WaitForExit(TimeSpan timeout);

        /// <summary>Kills the worker if it still runs, and releases the host's handles to it.</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>Releases the host's handles to the worker.</summary>
        /// <param name="disposing">True when called from <see cref="Dispose()" />.</param>
        protected abstract void Dispose(bool disposing);
    }

    /// <summary>
    ///     Reads a worker's standard error to its end on a background thread, so the worker never
    ///     blocks on a full pipe, and keeps the first <see cref="WorkerProcess.MaxErrorBytes" />.
    /// </summary>
    internal sealed class ErrorTail
    {
        // Guards the kept bytes.
        private readonly Lock _sync = new();
        private readonly byte[] _kept = new byte[WorkerProcess.MaxErrorBytes];
        private readonly Thread _reader;
        private int _length;

        /// <summary>Starts draining <paramref name="error" />, which the drain disposes at its end.</summary>
        /// <param name="error">The read end of the worker's standard error.</param>
        public ErrorTail(Stream error)
        {
            ArgumentNullException.ThrowIfNull(error);
            _reader = new Thread(() => Drain(error)) { IsBackground = true, Name = "Tellma PDF worker: stderr" };
            _reader.Start();
        }

        /// <summary>
        ///     What was kept, as one line of printable ASCII: anything else, line breaks included,
        ///     becomes a space or a question mark, since a compromised worker controls these bytes.
        /// </summary>
        public string Text
        {
            get
            {
                // The drain ends when the worker's end closes; a short wait lets an exited worker's
                // last words arrive.
                _ = _reader.Join(TimeSpan.FromMilliseconds(500));
                lock (_sync)
                {
                    var text = new StringBuilder(_length);
                    foreach (byte b in _kept.AsSpan(0, _length))
                    {
                        _ = text.Append(b switch
                        {
                            (byte)'\r' or (byte)'\n' or (byte)'\t' => ' ',
                            >= 0x20 and < 0x7F => (char)b,
                            _ => '?',
                        });
                    }

                    return text.ToString().Trim();
                }
            }
        }

        /// <summary>Reads the stream to its end, keeping the start.</summary>
        /// <param name="error">The stream.</param>
        private void Drain(Stream error)
        {
            byte[] buffer = new byte[1024];
            try
            {
                using (error)
                {
                    int read;
                    while ((read = error.Read(buffer)) > 0)
                    {
                        lock (_sync)
                        {
                            int keep = Math.Min(read, _kept.Length - _length);
                            buffer.AsSpan(0, keep).CopyTo(_kept.AsSpan(_length));
                            _length += keep;
                        }
                    }
                }
            }
#pragma warning disable CA1031 // A broken pipe ends the drain; what was kept is all there is.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }
    }
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Imaging.Workers
{
    /// <summary>
    ///     A host's end of a worker pipe whose blocking reads and writes end when the worker is
    ///     killed, even while another process holds the pipe's far end open: each one runs as
    ///     overlapped I/O that the kill cancels.
    /// </summary>
    /// <remarks>
    ///     A synchronous read on a pipe ends only when every writer is gone. On Windows, a child that
    ///     some other code in the host starts while a worker is being created may inherit the
    ///     worker's end of a pipe; killing the worker would then leave the read blocked for as long
    ///     as that unrelated child lives. Here the kill ends it.
    /// </remarks>
    /// <param name="inner">The pipe end, opened for overlapped I/O; disposed with this stream.</param>
    /// <param name="killed">Cancelled when the worker is killed.</param>
    internal sealed class KillableStream(Stream inner, CancellationToken killed) : Stream
    {
        /// <inheritdoc />
        public override bool CanRead => inner.CanRead;

        /// <inheritdoc />
        public override bool CanWrite => inner.CanWrite;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc />
        /// <exception cref="IOException">The worker was killed, or its pipe broke.</exception>
        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            return Wait(inner.ReadAsync(buffer.AsMemory(offset, count), killed));
        }

        /// <inheritdoc />
        /// <exception cref="IOException">The worker was killed, or its pipe broke.</exception>
        public override void Write(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            _ = Wait(WriteAndReport(buffer.AsMemory(offset, count)));
        }

        /// <inheritdoc />
        public override void Flush()
        {
            inner.Flush();
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>Waits for an overlapped operation, reporting the kill as a broken pipe, which is what it is to the reader.</summary>
        /// <param name="operation">The operation.</param>
        /// <returns>Its result.</returns>
        /// <exception cref="IOException">The worker was killed.</exception>
        private static int Wait(ValueTask<int> operation)
        {
            try
            {
                return operation.IsCompletedSuccessfully ? operation.Result : operation.AsTask().GetAwaiter().GetResult();
            }
            catch (OperationCanceledException exception)
            {
                throw new IOException("The worker was killed.", exception);
            }
        }

        /// <summary>Writes, as an operation <see cref="Wait" /> takes.</summary>
        /// <param name="buffer">The bytes.</param>
        /// <returns>Their count, once written.</returns>
        private async ValueTask<int> WriteAndReport(ReadOnlyMemory<byte> buffer)
        {
            await inner.WriteAsync(buffer, killed).ConfigureAwait(false);
            return buffer.Length;
        }
    }
}

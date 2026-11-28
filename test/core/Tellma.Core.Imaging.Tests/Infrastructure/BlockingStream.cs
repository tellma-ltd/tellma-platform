// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Imaging.Tests.Infrastructure
{
    /// <summary>
    ///     A seekable stream whose first read that would reach past a threshold blocks until
    ///     released, and which counts every read or reposition made after the suite marks the call
    ///     returned.
    /// </summary>
    internal sealed class BlockingStream(byte[] bytes, int blockAfter) : MemoryStream(bytes, writable: false)
    {
        private int _blockedOnce;
        private int _returned;
        private int _touchesAfterReturn;

        /// <summary>Set when a read is blocked.</summary>
        public ManualResetEventSlim Blocked { get; } = new();

        /// <summary>Set by the suite to let the blocked read finish.</summary>
        public ManualResetEventSlim Release { get; } = new();

        /// <summary>Reads and repositions after <see cref="MarkReturned" />.</summary>
        public int TouchesAfterReturn => Volatile.Read(ref _touchesAfterReturn);

        /// <inheritdoc />
        public override long Position
        {
            get => base.Position;
            set
            {
                Touch();
                base.Position = value;
            }
        }

        /// <summary>Marks the processor's call as returned.</summary>
        public void MarkReturned()
        {
            Volatile.Write(ref _returned, 1);
        }

        // Only the array overload is overridden: a derived memory stream's span overload already
        // routes through it, and overriding both would make each call the other.

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            Touch();
            if (base.Position + count > blockAfter && Interlocked.Exchange(ref _blockedOnce, 1) == 0)
            {
                Blocked.Set();
                Release.Wait(TimeSpan.FromSeconds(30));
            }

            return base.Read(buffer, offset, count);
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin loc)
        {
            Touch();
            return base.Seek(offset, loc);
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Release.Set();
                Blocked.Dispose();
                Release.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>Counts an access made after the call returned.</summary>
        private void Touch()
        {
            if (Volatile.Read(ref _returned) == 1)
            {
                Interlocked.Increment(ref _touchesAfterReturn);
            }
        }
    }
}

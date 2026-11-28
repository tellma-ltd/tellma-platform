// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Imaging.Tests.Internals
{
    /// <summary>
    ///     The input view native decoders read through: a window from the start position, end of
    ///     input once closed or cancelled, and no exception on any path a native reader takes.
    /// </summary>
    public sealed class GatedReadStreamTests
    {
        [Fact]
        public void It_views_the_inner_stream_from_its_position_at_construction()
        {
            using var inner = new MemoryStream([9, 9, 1, 2, 3]) { Position = 2 };
            using var gated = new GatedReadStream(inner, CancellationToken.None);
            byte[] buffer = new byte[8];

            Assert.Equal(3, gated.Length);
            Assert.Equal(3, gated.Read(buffer, 0, buffer.Length));
            Assert.Equal([1, 2, 3], buffer[..3]);
            Assert.Equal(0, gated.Read(buffer, 0, buffer.Length));

            gated.Position = 1;
            Assert.Equal(2, gated.ReadByte());
        }

        [Fact]
        public void Seeking_clamps_to_the_view_instead_of_throwing()
        {
            using var inner = new MemoryStream([1, 2, 3]);
            using var gated = new GatedReadStream(inner, CancellationToken.None);

            Assert.Equal(0, gated.Seek(-10, SeekOrigin.Begin));
            Assert.Equal(3, gated.Seek(10, SeekOrigin.End));
            gated.Position = 99;
            Assert.Equal(3, gated.Position);
        }

        [Fact]
        public void A_closed_gate_or_a_cancelled_token_reads_as_end_of_input()
        {
            using var cancellation = new CancellationTokenSource();
            using var inner = new MemoryStream([1, 2, 3]);
            using var closed = new GatedReadStream(inner, CancellationToken.None);
            using var cancelled = new GatedReadStream(inner, cancellation.Token);
            byte[] buffer = new byte[3];

            closed.CloseGate();
            cancellation.Cancel();

            Assert.Equal(0, closed.Read(buffer));
            Assert.Equal(0, cancelled.Read(buffer));
            Assert.True(closed.IsClosed);
            Assert.True(cancelled.IsClosed);
        }

        [Fact]
        public void A_failing_inner_stream_closes_the_gate_instead_of_throwing()
        {
            var inner = new MemoryStream([1, 2, 3]);
            using var gated = new GatedReadStream(inner, CancellationToken.None);
            inner.Dispose();

            Assert.Equal(0, gated.Read(new byte[3]));
            Assert.True(gated.IsClosed);
        }

        [Fact]
        public void Disposing_the_view_leaves_the_inner_stream_open()
        {
            using var inner = new MemoryStream([1, 2, 3]);
            var gated = new GatedReadStream(inner, CancellationToken.None);

            gated.Dispose();

            Assert.True(inner.CanRead);
            Assert.Equal(0, gated.Read(new byte[3]));
        }

        [Fact]
        public void It_is_read_only_and_refuses_an_unseekable_input()
        {
            using var inner = new MemoryStream([1, 2, 3]);
            using var gated = new GatedReadStream(inner, CancellationToken.None);

            Assert.False(gated.CanWrite);
            Assert.Throws<NotSupportedException>(() => gated.Write([1], 0, 1));
            Assert.Throws<NotSupportedException>(() => gated.SetLength(1));
            Assert.Throws<ArgumentException>(() => new GatedReadStream(new UnseekableStream(), CancellationToken.None));
        }

        /// <summary>A stream that cannot seek.</summary>
        private sealed class UnseekableStream : MemoryStream
        {
            /// <inheritdoc />
            public override bool CanSeek => false;
        }
    }
}

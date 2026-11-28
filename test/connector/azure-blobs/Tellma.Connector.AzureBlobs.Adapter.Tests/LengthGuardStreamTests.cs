// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Connector.AzureBlobs.Tests
{
    /// <summary>
    ///     The length guard an upload's content is read through: exactly the declared length, a
    ///     failure on an early end or an overrun that it also records, and seeks the client's retry
    ///     rewinds with.
    /// </summary>
    public sealed class LengthGuardStreamTests
    {
        private const string Name = "kind/ab/0123";

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_content_of_its_declared_length_reads_whole_and_reports_that_length()
        {
            byte[] bytes = [.. Enumerable.Range(0, 1000).Select(static i => (byte)i)];
            LengthGuardStream guard = new(new MemoryStream(bytes), bytes.Length, Name);

            using MemoryStream read = new();
            await guard.CopyToAsync(read, 7, Token);

            Assert.Equal(bytes, read.ToArray());
            Assert.Equal(bytes.Length, guard.Length);
            Assert.Null(guard.Mismatch);
        }

        [Fact]
        public async Task A_content_running_past_its_declared_length_fails_and_records_why()
        {
            LengthGuardStream guard = new(new MemoryStream(new byte[11]), 10, Name);

            InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(() => guard.CopyToAsync(Stream.Null, Token));

            Assert.Equal("The content of the blob 'kind/ab/0123' runs past its declared length of 10 bytes.", failure.Message);
            Assert.Equal(failure.Message, guard.Mismatch);
        }

        [Fact]
        public void A_content_ending_before_its_declared_length_fails_and_records_why()
        {
            LengthGuardStream guard = new(new MemoryStream(new byte[9]), 10, Name);

            InvalidDataException failure = Assert.Throws<InvalidDataException>(() => guard.CopyTo(Stream.Null));

            Assert.Equal("The content of the blob 'kind/ab/0123' ended after 9 of its declared 10 bytes.", failure.Message);
            Assert.Equal(failure.Message, guard.Mismatch);
        }

        [Fact]
        public async Task A_zero_length_read_before_the_declared_end_is_no_end_and_the_content_still_reads_whole()
        {
            byte[] bytes = [1, 2, 3, 4];
            LengthGuardStream guard = new(new MemoryStream(bytes), bytes.Length, Name);

            // --- Zero-length reads, synchronous and asynchronous, before any byte and midway ---
            Assert.Equal(0, guard.Read([]));
            Assert.Equal(0, await guard.ReadAsync(Memory<byte>.Empty, Token));
            byte[] first = new byte[2];
            Assert.Equal(2, guard.Read(first));
            Assert.Equal(0, guard.Read(new byte[4], 0, 0));
            Assert.Equal(0, await guard.ReadAsync(new byte[4].AsMemory(0, 0), Token));

            // --- Nothing was refused, and the rest still reads to the declared end ---
            Assert.Null(guard.Mismatch);
            byte[] rest = new byte[4];
            Assert.Equal(2, await guard.ReadAsync(rest, Token));
            Assert.Equal(0, await guard.ReadAsync(rest, Token));
            Assert.Equal(bytes[2..], rest[..2]);
            Assert.Null(guard.Mismatch);
        }

        [Fact]
        public void An_empty_content_of_declared_length_zero_reads_nothing_and_one_byte_more_fails()
        {
            Assert.Equal(0, new LengthGuardStream(new MemoryStream([]), 0, Name).Read(new byte[4]));
            _ = Assert.Throws<InvalidDataException>(() => new LengthGuardStream(new MemoryStream([1]), 0, Name).Read(new byte[4]));
        }

        [Fact]
        public void A_rewind_reads_the_content_again_from_where_the_guard_began()
        {
            // The content starts mid-stream, as a body the caller had already advanced.
            MemoryStream content = new([9, 9, 1, 2, 3]) { Position = 2 };
            LengthGuardStream guard = new(content, 3, Name);
            byte[] first = new byte[3];
            guard.ReadExactly(first);

            guard.Position = 0;
            byte[] second = new byte[3];
            guard.ReadExactly(second);

            Assert.Equal([1, 2, 3], first);
            Assert.Equal(first, second);
            Assert.Equal(3, guard.Position);
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => guard.Seek(4, SeekOrigin.Begin));
        }

        [Fact]
        public void A_content_that_cannot_seek_cannot_seek_through_the_guard()
        {
            LengthGuardStream guard = new(new NonSeekable(), 3, Name);

            Assert.False(guard.CanSeek);
            _ = Assert.Throws<NotSupportedException>(() => guard.Seek(0, SeekOrigin.Begin));
        }

        /// <summary>An empty readable stream that cannot seek.</summary>
        private sealed class NonSeekable : MemoryStream
        {
            /// <inheritdoc />
            public override bool CanSeek => false;
        }
    }
}

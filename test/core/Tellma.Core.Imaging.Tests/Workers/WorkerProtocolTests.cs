// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Buffers.Binary;
using System.Text;
using Tellma.Core.Imaging.PdfWorker;

namespace Tellma.Core.Imaging.Tests.Workers
{
    /// <summary>
    ///     The frames between the processor and its worker: each round-trips, and each reader
    ///     refuses a wrong magic number, an unknown status or capability, or a declared length
    ///     beyond its cap before allocating for it.
    /// </summary>
    public sealed class WorkerProtocolTests
    {
        [Fact]
        public void A_ready_frame_round_trips_its_capabilities()
        {
            using var stream = new MemoryStream();
            WorkerProtocol.WriteReady(stream, WorkerCapabilities.SystemCallFilter);
            stream.Position = 0;

            Assert.Equal(WorkerCapabilities.SystemCallFilter, WorkerProtocol.ReadReady(stream));
        }

        [Fact]
        public void A_ready_frame_with_unknown_capabilities_or_magic_is_refused()
        {
            Assert.Throws<InvalidDataException>(() => WorkerProtocol.ReadReady(Frame(WorkerProtocol.ReadyMagic, 6)));
            Assert.Throws<InvalidDataException>(() => WorkerProtocol.ReadReady(Frame(WorkerProtocol.ResultMagic, 0)));
            Assert.Throws<EndOfStreamException>(() => WorkerProtocol.ReadReady(new MemoryStream([0x54, 0x50])));
        }

        [Fact]
        public void A_request_round_trips()
        {
            using var stream = new MemoryStream();
            WorkerProtocol.WriteRequest(stream, 256, "%PDF-1.4"u8);
            stream.Position = 0;

            WorkerRequest request = WorkerProtocol.ReadRequest(stream);
            Assert.Equal(256, request.Size);
            Assert.Equal("%PDF-1.4"u8.ToArray(), request.Pdf);
        }

        [Theory]
        [InlineData(0, 10)]
        [InlineData(WorkerProtocol.MaxThumbnailSize + 1, 10)]
        [InlineData(256, 0)]
        [InlineData(256, WorkerProtocol.MaxInputBytes + 1)]
        [InlineData(256, -1)]
        public void A_request_out_of_range_is_refused_before_its_body_is_read(int size, int length)
        {
            Assert.Throws<InvalidDataException>(() => WorkerProtocol.ReadRequest(Frame(WorkerProtocol.RequestMagic, size, length)));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void A_result_round_trips(int code)
        {
            var status = (WorkerStatus)code;
            using var stream = new MemoryStream();
            WorkerProtocol.WriteResult(stream, status, "payload"u8);
            stream.Position = 0;

            WorkerResult result = WorkerProtocol.ReadResult(stream, 256);
            Assert.Equal(status, result.Status);
            Assert.Equal("payload"u8.ToArray(), result.Payload);
        }

        [Fact]
        public void A_result_beyond_its_cap_or_of_an_unknown_status_is_refused_before_its_body_is_read()
        {
            Assert.Throws<InvalidDataException>(() => WorkerProtocol.ReadResult(Frame(WorkerProtocol.ResultMagic, (int)WorkerStatus.Rendered, WorkerProtocol.MaxImageBytes(64) + 1), 64));
            Assert.Throws<InvalidDataException>(() => WorkerProtocol.ReadResult(Frame(WorkerProtocol.ResultMagic, (int)WorkerStatus.Refused, WorkerProtocol.MaxDetailBytes + 1), 64));
            Assert.Throws<InvalidDataException>(() => WorkerProtocol.ReadResult(Frame(WorkerProtocol.ResultMagic, (int)WorkerStatus.Rendered, -1), 64));
            Assert.Throws<InvalidDataException>(() => WorkerProtocol.ReadResult(Frame(WorkerProtocol.ResultMagic, 3, 0), 64));
            Assert.Throws<InvalidDataException>(() => WorkerProtocol.ReadResult(Frame(WorkerProtocol.RequestMagic, 0, 0), 64));
        }

        [Fact]
        public void The_image_cap_bounds_an_uncompressed_png_of_the_thumbnail_and_no_more()
        {
            // An RGBA PNG of 256 x 256 stored uncompressed is its 256 rows of 1 + 1024 bytes and a
            // little framing; the cap is that, a sixty-fourth more and 4 KiB.
            Assert.Equal((256 * 1025) + (256 * 1025 / 64) + 4096, WorkerProtocol.MaxImageBytes(256));
            Assert.True(WorkerProtocol.MaxImageBytes(WorkerProtocol.MaxThumbnailSize) < 20 * 1024 * 1024);
            Assert.Throws<ArgumentOutOfRangeException>(() => WorkerProtocol.MaxImageBytes(WorkerProtocol.MaxThumbnailSize + 1));
        }

        [Fact]
        public void A_long_detail_is_cut_to_the_cap_on_a_character_boundary()
        {
            string text = new('é', WorkerProtocol.MaxDetailBytes);

            byte[] detail = WorkerSession.Detail(text);

            Assert.True(detail.Length <= WorkerProtocol.MaxDetailBytes);
            Assert.Equal(new string('é', detail.Length / 2), Encoding.UTF8.GetString(detail));
        }

        /// <summary>A stream holding a frame header of a magic number and integers, with no body.</summary>
        /// <param name="magic">The magic number.</param>
        /// <param name="values">The integers.</param>
        /// <returns>The stream.</returns>
        private static MemoryStream Frame(uint magic, params int[] values)
        {
            byte[] frame = new byte[4 + (4 * values.Length)];
            BinaryPrimitives.WriteUInt32LittleEndian(frame, magic);
            for (int i = 0; i < values.Length; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4 + (4 * i)), values[i]);
            }

            return new MemoryStream(frame);
        }
    }
}

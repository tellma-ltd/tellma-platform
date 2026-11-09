// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using SkiaSharp;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Imaging.Tests.Fixtures;
using Tellma.Core.Imaging.Tests.Infrastructure;

namespace Tellma.Core.Imaging.Tests.Observability
{
    /// <summary>
    ///     The image instruments and log events: their names, closed tag sets and values, and the
    ///     render event's place before native work.
    /// </summary>
    public sealed class TelemetryTests : IDisposable
    {
        // The closed set of failure reasons the spec defines.
        private static readonly string[] Reasons = ["format", "too-many-pixels", "decode-failed", "timeout"];

        private readonly ProcessorHarness _harness = new();

        public void Dispose()
        {
            _harness.Dispose();
        }

        [Fact]
        public async Task A_normalisation_records_its_duration_under_its_kind_and_operation()
        {
            await _harness.ProcessAsync(ImageFixtures.QuadrantImage(SKEncodedImageFormat.Png, 32, 32), BlobKindPolicy.Photo.Image!);

            CollectedMeasurement<double> measurement = Assert.Single(_harness.Duration.GetMeasurementSnapshot());
            Assert.True(measurement.Value >= 0);
            AssertTags(measurement.Tags, (BlobTelemetryNames.KindTag, ProcessorHarness.Kind), (BlobTelemetryNames.OperationTag, BlobTelemetryTagValues.ImageProcess));
            Assert.Empty(_harness.Failures.GetMeasurementSnapshot());
        }

        [Fact]
        public async Task A_thumbnail_records_its_duration_under_the_thumbnail_operation()
        {
            await _harness.ThumbnailAsync(DocumentFixtures.HalfBlackPage(100, 100), BlobContentTypes.Pdf);

            CollectedMeasurement<double> measurement = Assert.Single(_harness.Duration.GetMeasurementSnapshot());
            AssertTags(measurement.Tags, (BlobTelemetryNames.KindTag, ProcessorHarness.Kind), (BlobTelemetryNames.OperationTag, BlobTelemetryTagValues.ImageThumbnail));
        }

        public static TheoryData<string, byte[]> Refusals => new()
        {
            { BlobTelemetryTagValues.ImageFormatReason, "not an image"u8.ToArray() },
            { BlobTelemetryTagValues.ImageTooManyPixelsReason, ImageFixtures.DeclaringSize(SKEncodedImageFormat.Png, 10_000, 6_000) },
            { BlobTelemetryTagValues.ImageDecodeFailedReason, ImageFixtures.TruncatedJpeg(0.5) },
        };

        [Theory]
        [MemberData(nameof(Refusals))]
        public async Task A_refusal_is_counted_once_with_its_reason(string reason, byte[] input)
        {
            await Assert.ThrowsAsync<ImageRejectedException>(() => _harness.ProcessAsync(input, BlobKindPolicy.Photo.Image!));

            CollectedMeasurement<long> failure = Assert.Single(_harness.Failures.GetMeasurementSnapshot());
            Assert.Equal(1, failure.Value);
            AssertTags(
                failure.Tags,
                (BlobTelemetryNames.KindTag, ProcessorHarness.Kind),
                (BlobTelemetryNames.OperationTag, BlobTelemetryTagValues.ImageProcess),
                (BlobTelemetryNames.ReasonTag, reason));
        }

        [Fact]
        public async Task The_render_event_precedes_native_work_and_names_kind_operation_type_and_size()
        {
            byte[] input = ImageFixtures.QuadrantImage(SKEncodedImageFormat.Jpeg, 32, 32);

            await _harness.ProcessAsync(input, BlobKindPolicy.Photo.Image!);
            await _harness.ThumbnailAsync(DocumentFixtures.HalfBlackPage(100, 100), BlobContentTypes.Pdf);

            FakeLogRecord[] renders = [.. _harness.Logger.Collector.GetSnapshot().Where(static r => r.Id.Name == "BlobImageRender")];
            Assert.Equal(2, renders.Length);
            Assert.All(renders, static r => Assert.Equal(LogLevel.Debug, r.Level));
            AssertState(renders[0], ("Kind", ProcessorHarness.Kind), ("Operation", "process"), ("ContentType", BlobContentTypes.Jpeg), ("Size", input.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            AssertState(renders[1], ("Kind", ProcessorHarness.Kind), ("Operation", "thumbnail"), ("ContentType", BlobContentTypes.Pdf));
        }

        [Fact]
        public async Task A_failure_is_logged_at_debug_with_its_reason()
        {
            await Assert.ThrowsAsync<ImageRejectedException>(
                () => _harness.ProcessAsync(ImageFixtures.TruncatedJpeg(0.5), BlobKindPolicy.Photo.Image!));

            FakeLogRecord failed = Assert.Single(_harness.Logger.Collector.GetSnapshot(), static r => r.Id.Name == "BlobImageFailed");
            Assert.Equal(LogLevel.Debug, failed.Level);
            AssertState(failed, ("Reason", ImageRejectedException.DecodeFailedReason), ("Operation", "process"));
        }

        [Fact]
        public async Task No_instrument_carries_a_tag_outside_the_closed_sets()
        {
            await _harness.ProcessAsync(ImageFixtures.QuadrantImage(SKEncodedImageFormat.Png, 32, 32), BlobKindPolicy.Photo.Image!, BlobKindPolicy.Photo.Thumbnail);
            await Assert.ThrowsAsync<ImageRejectedException>(() => _harness.ProcessAsync("x"u8.ToArray(), BlobKindPolicy.Photo.Image!));
            await _harness.ThumbnailAsync(DocumentFixtures.PasswordProtected(), BlobContentTypes.Pdf);

            string[] allowed = [BlobTelemetryNames.KindTag, BlobTelemetryNames.OperationTag, BlobTelemetryNames.ReasonTag];
            IEnumerable<IReadOnlyDictionary<string, object?>> tags = _harness.Duration.GetMeasurementSnapshot().Select(static m => m.Tags)
                .Concat(_harness.Failures.GetMeasurementSnapshot().Select(static m => m.Tags));
            Assert.All(tags, tagSet => Assert.All(tagSet.Keys, key => Assert.Contains(key, allowed)));
            Assert.All(_harness.Failures.GetMeasurementSnapshot(), static m => Assert.Contains((string)m.Tags[BlobTelemetryNames.ReasonTag]!, Reasons));
        }

        /// <summary>Asserts that a tag set is exactly the given pairs.</summary>
        /// <param name="tags">The tags.</param>
        /// <param name="expected">The expected key and value pairs.</param>
        private static void AssertTags(IReadOnlyDictionary<string, object?> tags, params (string Key, string Value)[] expected)
        {
            Assert.Equal(expected.Length, tags.Count);
            foreach ((string key, string value) in expected)
            {
                Assert.Equal(value, tags[key]);
            }
        }

        /// <summary>Asserts properties of a structured log record.</summary>
        /// <param name="record">The record.</param>
        /// <param name="expected">The expected property names and rendered values.</param>
        private static void AssertState(FakeLogRecord record, params (string Key, string Value)[] expected)
        {
            IReadOnlyList<KeyValuePair<string, string?>> state = record.StructuredState!;
            foreach ((string key, string value) in expected)
            {
                Assert.Contains(new KeyValuePair<string, string?>(key, value), state);
            }
        }
    }
}

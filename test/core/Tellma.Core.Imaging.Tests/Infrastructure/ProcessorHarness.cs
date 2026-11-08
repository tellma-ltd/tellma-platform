// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Testing;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Imaging.Tests.Infrastructure
{
    /// <summary>
    ///     A processor with its own meter factory, a recording logger, and collectors for both image
    ///     instruments.
    /// </summary>
    internal sealed class ProcessorHarness : IDisposable
    {
        /// <summary>The kind every call of the suites is made under.</summary>
        public const string Kind = "fixture-photo";

        private readonly ServiceProvider _services;

        /// <summary>Creates the harness.</summary>
        /// <param name="timeProvider">The clock; the system's when null.</param>
        /// <param name="pdf">The PDF rasterizer; PDFium when null.</param>
        public ProcessorHarness(TimeProvider? timeProvider = null, IPdfRasterizer? pdf = null)
        {
            _services = new ServiceCollection().AddMetrics().BuildServiceProvider();
            IMeterFactory meterFactory = _services.GetRequiredService<IMeterFactory>();

            Logger = new FakeLogger<SkiaImageProcessor>();
            Duration = new MetricCollector<double>(meterFactory, BlobTelemetryNames.MeterName, BlobTelemetryNames.ImageDuration);
            Failures = new MetricCollector<long>(meterFactory, BlobTelemetryNames.MeterName, BlobTelemetryNames.ImageFailures);
            Processor = new SkiaImageProcessor(Logger, meterFactory, timeProvider ?? TimeProvider.System, pdf ?? PdfiumRasterizer.Instance);
        }

        /// <summary>The processor.</summary>
        public SkiaImageProcessor Processor { get; }

        /// <summary>The processor's log.</summary>
        public FakeLogger<SkiaImageProcessor> Logger { get; }

        /// <summary>The duration histogram's measurements.</summary>
        public MetricCollector<double> Duration { get; }

        /// <summary>The failure counter's measurements.</summary>
        public MetricCollector<long> Failures { get; }

        /// <summary>The failure reasons counted so far, in order.</summary>
        public IReadOnlyList<string?> FailureReasons =>
            [.. Failures.GetMeasurementSnapshot().Select(static m => m.Tags[BlobTelemetryNames.ReasonTag] as string)];

        /// <summary>The names of the log events written so far, in order.</summary>
        public IReadOnlyList<string?> EventNames =>
            [.. Logger.Collector.GetSnapshot().Select(static record => record.Id.Name)];

        /// <summary>Normalises <paramref name="input" /> under <paramref name="policy" />.</summary>
        /// <param name="input">The upload.</param>
        /// <param name="policy">The policy.</param>
        /// <param name="thumbnail">The thumbnail policy, or null.</param>
        /// <returns>The processed image.</returns>
        public Task<ProcessedImage> ProcessAsync(byte[] input, ImagePolicy policy, ThumbnailPolicy? thumbnail = null)
        {
            return Processor.ProcessAsync(Kind, input, policy, thumbnail, TestContext.Current.CancellationToken);
        }

        /// <summary>Renders a thumbnail of <paramref name="input" />.</summary>
        /// <param name="input">The stored bytes.</param>
        /// <param name="contentType">The stored media type.</param>
        /// <param name="size">The thumbnail size.</param>
        /// <param name="format">The thumbnail format.</param>
        /// <returns>The thumbnail, or null.</returns>
        public async Task<byte[]?> ThumbnailAsync(byte[] input, string contentType, int size = 256, ImageFormat format = ImageFormat.WebP)
        {
            using var stream = new MemoryStream(input, writable: false);
            return await Processor.ThumbnailAsync(
                Kind, stream, contentType, new ThumbnailPolicy(size, format), TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Duration.Dispose();
            Failures.Dispose();
            _services.Dispose();
        }
    }
}

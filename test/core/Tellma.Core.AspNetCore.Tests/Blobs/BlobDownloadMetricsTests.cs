// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.AspNetCore.Blobs;

namespace Tellma.Core.AspNetCore.Tests.Blobs
{
    /// <summary>The download counter: on the runtime's meter, by kind and outcome, and never by tenant or user.</summary>
    public sealed class BlobDownloadMetricsTests
    {
        [Fact]
        public void Each_download_outcome_is_counted_under_its_kind_on_the_runtime_meter()
        {
            ServiceCollection services = [];
            _ = services.AddMetrics();
            using ServiceProvider provider = services.BuildServiceProvider();
            IMeterFactory meters = provider.GetRequiredService<IMeterFactory>();
            using MetricCollector<long> downloads = new(meters, BlobTelemetryNames.MeterName, BlobTelemetryNames.Downloads);
            BlobDownloadMetrics metrics = new(meters);

            metrics.Count("user-image", BlobTelemetryTagValues.DownloadServed);
            metrics.Count("user-image", BlobTelemetryTagValues.DownloadNotModified);
            metrics.Count("export-file", BlobTelemetryTagValues.DownloadNotFound);

            // --- One measurement each, tagged by kind and outcome alone ---
            Assert.Equal(
                [
                    ("user-image", "served"),
                    ("user-image", "not_modified"),
                    ("export-file", "not_found"),
                ],
                downloads.GetMeasurementSnapshot().Select(static measurement =>
                {
                    Assert.Equal(1, measurement.Value);
                    Assert.Equal(2, measurement.Tags.Count);
                    return ((string)measurement.Tags[BlobTelemetryNames.KindTag]!, (string)measurement.Tags[BlobTelemetryNames.OutcomeTag]!);
                }));
            Assert.Equal(BlobTelemetryNames.DownloadUnit, downloads.Instrument!.Unit);
        }
    }
}

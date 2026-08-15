// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Email.Tests.Infrastructure;

namespace Tellma.Core.Email.Tests.Observability
{
    /// <summary>
    ///     What the router actually emits, and — just as important — what it does not: a tag set that
    ///     grew a per-tenant dimension would multiply every other dimension and the cost with it.
    /// </summary>
    public class EmailMetricsTests
    {
        [Fact]
        public async Task Records_one_measurement_per_message_with_the_full_tag_set()
        {
            FakeEmailSender live = new();
            await using var host = EmailTestHost.Build(
                new Dictionary<string, string?> { ["Email:Provider"] = "fake" },
                configure: services => services.AddFakeTransport("fake", live));

            using MetricCollector<long> sent = Collector(host, EmailTelemetryNames.SentMessagesInstrument);

            await SendAsync(host, [
                EmailTestMessages.Message(EmailAudience.Internal, "a", new EmailCorrelation("outbox", "1", 3)),
                EmailTestMessages.Message(EmailAudience.External, "b"),
            ]);

            IReadOnlyList<CollectedMeasurement<long>> measurements = sent.GetMeasurementSnapshot();
            Assert.Equal(2, measurements.Count);

            CollectedMeasurement<long> first = measurements[0];
            Assert.Equal("fake", first.Tags[EmailTelemetryNames.TransportTag]);
            Assert.Equal("sent", first.Tags[EmailTelemetryNames.OutcomeTag]);
            Assert.Equal("internal", first.Tags[EmailTelemetryNames.AudienceTag]);
            Assert.Equal("live", first.Tags[EmailTelemetryNames.DeliveryTag]);
            Assert.Equal("outbox", first.Tags[EmailTelemetryNames.OwnerTag]);
            Assert.Equal("live", first.Tags[EmailTelemetryNames.TenantCategoryTag]);

            // Uncorrelated mail still carries the owner dimension, so the dimension is never absent.
            Assert.Equal(EmailTelemetryNames.NoOwner, measurements[1].Tags[EmailTelemetryNames.OwnerTag]);
        }

        [Fact]
        public async Task Never_tags_a_measurement_with_anything_tenant_identifying()
        {
            FakeEmailSender live = new();
            await using var host = EmailTestHost.Build(
                new Dictionary<string, string?> { ["Email:Provider"] = "fake" },
                configure: services => services.AddFakeTransport("fake", live));

            using MetricCollector<long> sent = Collector(host, EmailTelemetryNames.SentMessagesInstrument);

            await SendAsync(host, [EmailTestMessages.Message(EmailAudience.Internal)]);

            // A per-tenant tag multiplies every other dimension, and the cost with it, for a
            // question the structured logs answer better.
            string[] expected =
            [
                EmailTelemetryNames.AudienceTag,
                EmailTelemetryNames.DeliveryTag,
                EmailTelemetryNames.OutcomeTag,
                EmailTelemetryNames.OwnerTag,
                EmailTelemetryNames.TransportTag,
                EmailTelemetryNames.TenantCategoryTag,
            ];

            Assert.Equal(
                expected,
                Assert.Single(sent.GetMeasurementSnapshot()).Tags.Keys.Order(StringComparer.Ordinal));
        }

        [Fact]
        public async Task Distinguishes_the_wire_mechanism_from_the_reported_outcome()
        {
            FakeEmailSender live = new();
            await using var host = EmailTestHost.Build(
                new Dictionary<string, string?> { ["Email:Provider"] = "fake" },
                configure: services => services.AddFakeTransport("fake", live));
            host.Sandbox.IsSandbox = true;

            using MetricCollector<long> sent = Collector(host, EmailTelemetryNames.SentMessagesInstrument);

            await SendAsync(host, [EmailTestMessages.Message(EmailAudience.External)]);

            // Sandboxed is the outcome; withheld is how it came about. The alert queries need both.
            CollectedMeasurement<long> measurement = Assert.Single(sent.GetMeasurementSnapshot());
            Assert.Equal("sandboxed", measurement.Tags[EmailTelemetryNames.OutcomeTag]);
            Assert.Equal(EmailTelemetryNames.WithheldDelivery, measurement.Tags[EmailTelemetryNames.DeliveryTag]);
            Assert.Equal(
                EmailTelemetryNames.SandboxTenantCategory,
                measurement.Tags[EmailTelemetryNames.TenantCategoryTag]);
        }

        [Fact]
        public async Task Tags_the_send_duration_with_the_exception_type_when_a_batch_throws()
        {
            FakeEmailSender live = new() { ThrowOnSend = new InvalidOperationException("no credentials") };
            await using var host = EmailTestHost.Build(
                new Dictionary<string, string?> { ["Email:Provider"] = "fake" },
                configure: services => services.AddFakeTransport("fake", live));

            using MetricCollector<double> duration = new(
                host.Services.GetRequiredService<IMeterFactory>(),
                EmailTelemetryNames.MeterName,
                EmailTelemetryNames.SendDurationInstrument);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => SendAsync(host, [EmailTestMessages.Message(EmailAudience.Internal)]));

            CollectedMeasurement<double> measurement = Assert.Single(duration.GetMeasurementSnapshot());
            Assert.Equal(nameof(InvalidOperationException), measurement.Tags[EmailTelemetryNames.ErrorTypeTag]);
        }

        [Fact]
        public async Task Records_the_batch_size_per_transport_call()
        {
            FakeEmailSender live = new();
            FakeEmailSender sandbox = new();
            await using var host = EmailTestHost.Build(
                new Dictionary<string, string?> { ["Email:Provider"] = "fake" },
                configure: services => services.AddFakeTransport("fake", live, sandbox));
            host.Sandbox.IsSandbox = true;

            using MetricCollector<int> batchSize = new(
                host.Services.GetRequiredService<IMeterFactory>(),
                EmailTelemetryNames.MeterName,
                EmailTelemetryNames.SendBatchSizeInstrument);

            await SendAsync(host, [
                EmailTestMessages.Message(EmailAudience.Internal, "a"),
                EmailTestMessages.Message(EmailAudience.External, "b"),
                EmailTestMessages.Message(EmailAudience.External, "c"),
            ]);

            // One measurement per underlying call, not per batch the caller made.
            Assert.Equal([1, 2], batchSize.GetMeasurementSnapshot().Select(static m => m.Value).Order());
        }

        private static MetricCollector<long> Collector(EmailTestHost host, string instrument)
        {
            return new MetricCollector<long>(
                host.Services.GetRequiredService<IMeterFactory>(), EmailTelemetryNames.MeterName, instrument);
        }

        private static async Task<IReadOnlyList<EmailSendResult>> SendAsync(
            EmailTestHost host, IReadOnlyList<EmailMessage> messages)
        {
            using IServiceScope scope = host.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IEmailSender>()
                .SendAsync(messages, TestContext.Current.CancellationToken);
        }
    }
}

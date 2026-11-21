// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Calendars;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     The scope's access witness: a securable endpoint is witnessed by an evaluation of its
    ///     pair in any spelling the registry resolves, a member endpoint by the caller's connection
    ///     as a member, and a response without its witness is counted and logged at error.
    /// </summary>
    public sealed class AccessWitnessTests : IDisposable
    {
        /// <summary>The documents' resource.</summary>
        private const string Doc = "acme.Doc";

        /// <summary>The fixture's securables: documents, under a former name too.</summary>
        private static readonly SecurableRegistry Registry = SecurableRegistryTests.Compose(("acme", static builder => builder
            .Add(Doc, "Read", Doc)
            .Add(Doc, "Save", Doc)
            .Alias("acme.Document", Doc)));

        /// <summary>The meter factory and the logs.</summary>
        private readonly ServiceProvider _services = new ServiceCollection()
            .AddMetrics()
            .AddLogging()
            .AddFakeLogging()
            .BuildServiceProvider();

        /// <summary>The scope's context: user 7 of tenant 1.</summary>
        private readonly Holder _holder = new();

        /// <summary>The witness under test.</summary>
        private readonly AccessWitness _witness;

        /// <summary>Creates the witness over the fixture.</summary>
        public AccessWitnessTests()
        {
            _witness = new AccessWitness(
                _holder,
                Registry,
                new AccessMetrics(_services.GetRequiredService<IMeterFactory>()),
                _services.GetRequiredService<ILoggerFactory>());
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _services.Dispose();
        }

        [Fact]
        public void A_securable_endpoint_is_witnessed_by_an_evaluation_of_its_pair_in_any_spelling()
        {
            _witness.Record(Doc, "Read");

            Assert.True(_witness.Confirm(new SecurableEndpointMetadata(Doc, "Read"), "GET docs"));
            Assert.True(_witness.Confirm(new SecurableEndpointMetadata("acme.document", "READ"), "GET documents"));
            Assert.True(_witness.HasEvaluated("ACME.DOC", "read"));
            Assert.False(_witness.HasEvaluated(Doc, "Save"));
            Assert.False(_witness.IsMemberWitnessed);
        }

        [Fact]
        public void A_member_endpoint_is_witnessed_once_the_caller_connected_as_a_member()
        {
            _witness.Record(Doc, "Read");
            Assert.False(_witness.Confirm(null, "POST users/me"));

            _witness.RecordMember();

            Assert.True(_witness.IsMemberWitnessed);
            Assert.True(_witness.Confirm(null, "POST users/me"));
        }

        [Fact]
        public void A_response_without_its_witness_is_counted_and_logged_at_error_naming_what_was_expected()
        {
            using MetricCollector<long> missing = new(_services.GetRequiredService<IMeterFactory>(), AccessTelemetryNames.MeterName, AccessTelemetryNames.WitnessMissing);
            _witness.Record(Doc, "Read");

            Assert.False(_witness.Confirm(new SecurableEndpointMetadata("acme.Document", "Save"), "PUT documents"));
            Assert.False(_witness.Confirm(null, "POST users/me"));

            Assert.Equal([1L, 1L], missing.GetMeasurementSnapshot().Select(static measurement => measurement.Value));
            Assert.All(missing.GetMeasurementSnapshot(), static measurement => Assert.Empty(measurement.Tags));
            FakeLogRecord[] logged = [.. _services.GetFakeLogCollector().GetSnapshot().Where(static record => record.Id.Id == AccessEvents.MissingWitness)];
            Assert.Equal(2, logged.Length);
            Assert.All(logged, static record => Assert.Equal((LogLevel.Error, AccessEvents.Category, "1", "7"), (record.Level, record.Category, record.GetStructuredStateValue("TenantId"), record.GetStructuredStateValue("UserId"))));
            Assert.Equal(
                [("PUT documents", "acme.Document:Save"), ("POST users/me", "member")],
                logged.Select(static record => (record.GetStructuredStateValue("Endpoint"), record.GetStructuredStateValue("Expected"))));
        }

        [Fact]
        public void A_refusal_outside_a_tenant_is_logged_for_tenant_zero()
        {
            _holder.Current = RequestContext.Tenantless(new GregorianCalendarSystem(), DateTimeOffset.UnixEpoch);

            Assert.False(_witness.Confirm(null, "GET health"));

            FakeLogRecord record = Assert.Single(_services.GetFakeLogCollector().GetSnapshot(), static record => record.Id.Id == AccessEvents.MissingWitness);
            Assert.Equal("0", record.GetStructuredStateValue("TenantId"));
        }

        [Fact]
        public void Recording_is_safe_for_concurrent_use_and_keeps_each_pair_once()
        {
            _ = Parallel.For(0, 1000, index => _witness.Record(index % 2 == 0 ? Doc : "ACME.DOC", $"Action{index % 10}"));
            _witness.RecordMember();

            Assert.Equal(10, _witness.Evaluated.Count);
            Assert.True(_witness.IsMemberWitnessed);
        }

        [Fact]
        public void The_arguments_are_checked()
        {
            _ = Assert.Throws<ArgumentNullException>(() => _witness.Record(null!, "Read"));
            _ = Assert.Throws<ArgumentNullException>(() => _witness.Record(Doc, null!));
            _ = Assert.Throws<ArgumentNullException>(() => _witness.HasEvaluated(null!, "Read"));
            _ = Assert.Throws<ArgumentNullException>(() => _witness.Confirm(null, null!));
        }

        /// <summary>The scope's context: user 7 of tenant 1 by default.</summary>
        private sealed class Holder : IRequestContextAccessor
        {
            /// <inheritdoc />
            public RequestContext Current { get; set; } = RequestContext.Tenantless(new GregorianCalendarSystem(), DateTimeOffset.UnixEpoch) with
            {
                Tenant = new TenantDescriptor(1, "Acme", null, null, ["en"], TenantCategory.Live, TenantState.Active, null),
                Kind = PrincipalKind.User,
                Subject = "sub-7",
                UserId = 7,
            };
        }
    }
}

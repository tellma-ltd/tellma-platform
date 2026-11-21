// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Connector.AzureBlobs.Tests
{
    /// <summary>
    ///     A host identity that cannot produce a token, with storage itself healthy: every operation
    ///     reports a storage failure and meters it as failed, and a write or a delete of several
    ///     objects still attempts each of them. The credential fails before anything is sent, so
    ///     nothing here reaches Azure.
    /// </summary>
    public sealed class AzureBlobStoreCredentialFailureTests
    {
        private const int Tenant = 1;

        private static readonly Uri Endpoint = new("https://acme.blob.core.windows.net");

        private static readonly string First = BlobName.Primary("user-image", "0123456789abcdef0123456789abcdef");

        private static readonly string Second = BlobName.Primary("user-image", "fedcba9876543210fedcba9876543210");

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>The credential failures Azure.Identity raises: an identity endpoint that refused, and one that is unavailable.</summary>
        public static TheoryData<bool> Unavailable => [false, true];

        [Theory]
        [MemberData(nameof(Unavailable))]
        public async Task A_read_reports_a_storage_failure_and_meters_it(bool unavailable)
        {
            using Harness harness = new(unavailable);

            BlobStoreException failure = await Assert.ThrowsAsync<BlobStoreException>(() => harness.Store.OpenReadAsync(Tenant, First, Token));

            Assert.IsType(harness.ExceptionType, failure.InnerException, exactMatch: false);
            Assert.Equal([(BlobTelemetryTagValues.StoreRead, BlobTelemetryTagValues.StoreFailed)], harness.Outcomes());
        }

        [Theory]
        [MemberData(nameof(Unavailable))]
        public async Task A_write_of_two_objects_attempts_both_reports_a_storage_failure_and_meters_it(bool unavailable)
        {
            using Harness harness = new(unavailable);
            BlobWrite[] writes =
            [
                new(First, "image/webp", new MemoryStream(new byte[3]), 3),
                new(Second, "image/webp", new MemoryStream(new byte[3]), 3),
            ];

            BlobStoreException failure = await Assert.ThrowsAsync<BlobStoreException>(() => harness.Store.WriteAsync(Tenant, writes, Token));

            // One at a time: the first write's failure must not cancel the second.
            Assert.IsType(harness.ExceptionType, failure.InnerException, exactMatch: false);
            Assert.Equal(2, harness.TokenRequests);
            Assert.Equal([(BlobTelemetryTagValues.StoreWrite, BlobTelemetryTagValues.StoreFailed)], harness.Outcomes());
        }

        [Theory]
        [MemberData(nameof(Unavailable))]
        public async Task A_delete_of_two_names_attempts_both_reports_a_storage_failure_and_meters_it(bool unavailable)
        {
            using Harness harness = new(unavailable);

            BlobStoreException failure = await Assert.ThrowsAsync<BlobStoreException>(() => harness.Store.DeleteAsync(Tenant, [First, Second], Token));

            Assert.StartsWith("Deleting 2 of 2 blobs of tenant 1 failed", failure.Message, StringComparison.Ordinal);
            Assert.IsType(harness.ExceptionType, failure.InnerException, exactMatch: false);
            Assert.Equal(2, harness.TokenRequests);
            Assert.Equal([(BlobTelemetryTagValues.StoreDelete, BlobTelemetryTagValues.StoreFailed)], harness.Outcomes());
        }

        [Theory]
        [MemberData(nameof(Unavailable))]
        public async Task Ensuring_and_listing_report_a_storage_failure_and_meter_it(bool unavailable)
        {
            using Harness harness = new(unavailable);

            _ = await Assert.ThrowsAsync<BlobStoreException>(() => harness.Store.EnsureTenantAsync(Tenant, Token));
            _ = await Assert.ThrowsAsync<BlobStoreException>(async () =>
            {
                await foreach (BlobStoreEntry entry in harness.Store.ListAsync(Tenant, "user-image", Token))
                {
                    Assert.Fail($"The listing yielded '{entry.Name}' without a token.");
                }
            });

            Assert.Equal(
                [
                    (BlobTelemetryTagValues.StoreEnsure, BlobTelemetryTagValues.StoreFailed),
                    (BlobTelemetryTagValues.StoreList, BlobTelemetryTagValues.StoreFailed),
                ],
                harness.Outcomes());
        }

        /// <summary>A store over an HTTPS endpoint whose credential always fails, one operation at a time, and its operation counter.</summary>
        private sealed class Harness : IDisposable
        {
            private readonly ServiceProvider _provider;

            private readonly MetricCollector<long> _operations;

            private int _tokenRequests;

            /// <summary>Composes the store.</summary>
            /// <param name="unavailable">Whether the credential is unavailable rather than refused.</param>
            public Harness(bool unavailable)
            {
                ExceptionType = unavailable ? typeof(CredentialUnavailableException) : typeof(AuthenticationFailedException);
                TokenCredential credential = DelegatedTokenCredential.Create((_, _) =>
                {
                    _ = Interlocked.Increment(ref _tokenRequests);
                    throw unavailable
                        ? new CredentialUnavailableException("The managed identity endpoint is unavailable.")
                        : new AuthenticationFailedException("The managed identity endpoint refused the request.");
                });

                ServiceCollection services = [];
                _ = services.AddLogging();
                services.AddAzureBlobStore(
                    static options =>
                    {
                        options.ServiceUri = Endpoint;
                        options.MaxParallelism = 1;
                    },
                    credential);
                _provider = services.BuildServiceProvider();
                _operations = new MetricCollector<long>(
                    _provider.GetRequiredService<IMeterFactory>(), BlobTelemetryNames.StoreMeterName, BlobTelemetryNames.StoreOperations);
                Store = _provider.GetRequiredService<IBlobStore>();
            }

            /// <summary>The store.</summary>
            public IBlobStore Store { get; }

            /// <summary>The type of the credential's failure.</summary>
            public Type ExceptionType { get; }

            /// <summary>How many times the client asked the credential for a token.</summary>
            public int TokenRequests => Volatile.Read(ref _tokenRequests);

            /// <summary>The operations counted so far, with their outcomes, in order.</summary>
            /// <returns>The operations and outcomes.</returns>
            public (string Operation, string Outcome)[] Outcomes()
            {
                return [.. _operations.GetMeasurementSnapshot().Select(static measurement =>
                    ((string)measurement.Tags[BlobTelemetryNames.OperationTag]!, (string)measurement.Tags[BlobTelemetryNames.OutcomeTag]!))];
            }

            /// <inheritdoc />
            public void Dispose()
            {
                _operations.Dispose();
                _provider.Dispose();
            }
        }
    }
}

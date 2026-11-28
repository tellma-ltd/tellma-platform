// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Blobs;
using Tellma.Core.IntegrationTests.Access;
using Tellma.Core.Testing.Blobs;

namespace Tellma.Core.IntegrationTests.Blobs
{
    /// <summary>
    ///     Staging against a tenant database through the real connect prologue: the row the stage
    ///     statement stores with its id from the blob sequence, the soft per-user quota refusing the
    ///     upload past either limit, and the platform's own scope staging past it.
    /// </summary>
    /// <param name="fixture">The tenant databases, each test seeding its own caller.</param>
    [Trait("Category", "Integration")]
    public sealed class BlobStagingTests(ConnectDatabase fixture) : IClassFixture<ConnectDatabase>
    {
        /// <summary>The kind every case stages: the user's signature, an image kind of the platform's model.</summary>
        private const string Kind = "user-signature";

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_staged_row_takes_its_id_from_the_sequence_and_records_the_uploader()
        {
            await using ServiceProvider instance = Instance(static _ => { });
            (int userId, string subject) = await fixture.SeedCallerAsync("Bea");
            DateTimeOffset before = DateTimeOffset.UtcNow.AddSeconds(-5);

            BlobDescriptor staged = await StageAsync(instance, subject, Png(300));

            // --- The id is the sequence's latest value ---
            int current = await fixture.ScalarAsync<int>(
                ConnectDatabase.TenantA, "SELECT CAST([current_value] AS int) FROM [sys].[sequences] WHERE [object_id] = OBJECT_ID(N'[core].[sq_Blobs]');");
            Assert.Equal(current, staged.Id);

            // --- The row, as the descriptor and the store say ---
            string row = Assert.IsType<string>(await fixture.ScalarAsync<string>(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"""
                SELECT CONCAT([Kind], N'|', [State], N'|', [ContentType], N'|', [Size], N'|', DATALENGTH([Sha256]), N'|', [Variants], N'|', [CreatedById], N'|', IIF([CommittedAt] IS NULL, N'open', N'committed'))
                FROM [core].[Blobs] WHERE [Id] = {staged.Id};
                """)));
            Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"{Kind}|Staged|{BlobContentTypes.Png}|300|32||{userId}|open"), row);

            string storageKey = Assert.IsType<string>(await fixture.ScalarAsync<string>(
                ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"SELECT [StorageKey] FROM [core].[Blobs] WHERE [Id] = {staged.Id};")));
            Assert.NotNull(instance.GetRequiredService<InMemoryBlobStore>().Find(ConnectDatabase.TenantA, storageKey));

            // --- The deadline is the database's clock plus the default staging lifetime ---
            DateTimeOffset expires = Assert.IsType<DateTimeOffset>(staged.ExpiresAt);
            Assert.InRange(expires, before.AddHours(24), DateTimeOffset.UtcNow.AddHours(24).AddSeconds(5));
        }

        [Fact]
        public async Task The_quota_refuses_the_upload_past_the_count()
        {
            await using ServiceProvider instance = Instance(static options => options.MaxStagedPerUser = 2);
            (_, string subject) = await fixture.SeedCallerAsync("Cal");

            _ = await StageAsync(instance, subject, Png(100));
            _ = await StageAsync(instance, subject, Png(100));
            BlobRejectedException refused = await Assert.ThrowsAsync<BlobRejectedException>(() => StageAsync(instance, subject, Png(100)));

            Assert.Equal(BlobRejectedException.StagingQuotaExceeded, refused.Code);
            Assert.Equal((object?)2, refused.Arguments[BlobRejectedException.MaxCountArgument]);
        }

        [Fact]
        public async Task The_quota_refuses_the_upload_past_the_bytes_and_an_expired_row_counts_for_nothing()
        {
            await using ServiceProvider instance = Instance(static options => options.MaxStagedBytesPerUser = 150);
            (int userId, string subject) = await fixture.SeedCallerAsync("Dee");

            BlobDescriptor first = await StageAsync(instance, subject, Png(100));
            BlobRejectedException refused = await Assert.ThrowsAsync<BlobRejectedException>(() => StageAsync(instance, subject, Png(100)));
            Assert.Equal(BlobRejectedException.StagingQuotaExceeded, refused.Code);

            // --- The first row expires: the sweep has not claimed it, and it no longer counts ---
            await fixture.ExecuteAsync(
                ConnectDatabase.TenantA,
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Blobs] SET [ExpiresAt] = DATEADD(minute, -1, SYSUTCDATETIME()) WHERE [Id] = {first.Id} AND [CreatedById] = {userId};"));
            _ = await StageAsync(instance, subject, Png(100));
        }

        [Fact]
        public async Task The_platforms_own_scope_stages_past_the_quota_as_the_system_user()
        {
            await using ServiceProvider instance = Instance(static options => options.MaxStagedPerUser = 1);
            List<int> ids = [];

            for (int upload = 0; upload < 3; upload++)
            {
                await using AsyncServiceScope scope = await fixture.ScopeAsync(subject: null, kind: PrincipalKind.System, background: true, services: instance);
                ids.Add((await scope.ServiceProvider.GetRequiredService<IBlobService>().StageAsync(Request(Png(100)), Token)).Id);
            }

            int bySystem = await fixture.ScalarAsync<int>(ConnectDatabase.TenantA, string.Create(
                CultureInfo.InvariantCulture,
                $"SELECT COUNT(*) FROM [core].[Blobs] WHERE [Id] IN ({string.Join(", ", ids)}) AND [CreatedById] = {WellKnownIds.SystemUserId} AND [State] = 'Staged';"));
            Assert.Equal(3, bySystem);
        }

        /// <summary>An instance of the platform over the fixture's databases, with in-memory blobs and adjusted options.</summary>
        /// <param name="configure">Adjusts the blob options.</param>
        /// <returns>The instance; the caller disposes it.</returns>
        private ServiceProvider Instance(Action<BlobOptions> configure)
        {
            return fixture.CreateInstance(configure: services =>
            {
                _ = services.AddInMemoryBlobs();
                _ = services.Configure(configure);
            });
        }

        /// <summary>Stages bytes as a member, in a scope of their own.</summary>
        /// <param name="instance">The instance.</param>
        /// <param name="subject">The member's subject.</param>
        /// <param name="bytes">The bytes.</param>
        /// <returns>The descriptor.</returns>
        private async Task<BlobDescriptor> StageAsync(IServiceProvider instance, string subject, byte[] bytes)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject, services: instance);
            return await scope.ServiceProvider.GetRequiredService<IBlobService>().StageAsync(Request(bytes), Token);
        }

        /// <summary>An upload of the kind.</summary>
        /// <param name="bytes">The bytes.</param>
        /// <returns>The request.</returns>
        private static BlobStageRequest Request(byte[] bytes)
        {
            return new BlobStageRequest(Kind, new MemoryStream(bytes, writable: false), bytes.LongLength, BlobContentTypes.Png, "signature.png");
        }

        /// <summary>A PNG of a length: its signature, then filler; the stub processor stores it as it is.</summary>
        /// <param name="length">The length.</param>
        /// <returns>The bytes.</returns>
        private static byte[] Png(int length)
        {
            byte[] bytes = new byte[length];
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
            return bytes;
        }
    }
}

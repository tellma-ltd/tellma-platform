// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Migrator;
using Tellma.Core.Testing.Blobs;

namespace Tellma.Core.IntegrationTests.Migrator
{
    /// <summary>
    ///     <c>blobs verify</c> against a migrated tenant of the test distribution whose storage, an
    ///     in-memory store every run shares, disagrees with its blob rows: the objects no row
    ///     references and the rows whose objects are gone are both reported, the unreferenced objects
    ///     are deleted only under the flag, no row is ever written, and an unknown tenant is a failure.
    /// </summary>
    /// <param name="sql">The suite's server.</param>
    [Trait("Category", "Integration")]
    public sealed class BlobsVerifyCommandTests(SqlServerFixture sql)
    {
        /// <summary>The kind every row and object is of: Core's, so the test distribution registers it.</summary>
        private const string Kind = "user-image";

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Verify_reports_both_orphan_sets_and_deletes_unreferenced_objects_only_with_the_flag()
        {
            // --- One store across the runs, and a migrated seed tenant ---
            InMemoryBlobStore store = new();
            MigratorHarness migrator = new(sql, "mig_blobs")
            {
                Services = services =>
                {
                    services.RemoveAll<IBlobStore>();
                    services.RemoveAll<InMemoryBlobStore>();
                    _ = services.AddSingleton(store);
                    _ = services.AddSingleton<IBlobStore>(store);
                },
            };
            migrator.Seed(1);
            _ = await migrator.RunConvergedAsync("migrate");

            // --- A row with both objects; a row whose thumbnail is gone; a young staged row and a deleting row with none ---
            string whole = Name();
            string thinned = Name();
            int wholeId = await SeedAsync(migrator, "Committed", whole, "thumb", "DATEADD(day, -1, SYSUTCDATETIME())");
            int thinnedId = await SeedAsync(migrator, "Committed", thinned, "thumb", "DATEADD(hour, -1, SYSUTCDATETIME())");
            _ = await SeedAsync(migrator, "Staged", Name(), "thumb", "SYSUTCDATETIME()");
            _ = await SeedAsync(migrator, "Deleting", Name(), string.Empty, "DATEADD(day, -1, SYSUTCDATETIME())");

            // --- Objects: the rows' present ones, an object no row names, and a variant its row does not list ---
            string unreferenced = Name();
            string unlisted = BlobName.Variant(whole, "big");
            await WriteAsync(store, whole, BlobName.Variant(whole, "thumb"), thinned, unreferenced, unlisted);
            string[] orphans = [.. new[] { unreferenced, unlisted }.Order(StringComparer.Ordinal)];

            // --- Without the flag: both sets reported, nothing deleted ---
            MigratorResult report = await migrator.RunConvergedAsync("blobs", "verify", "--tenant", "1");

            Assert.Equal(
                [.. orphans.Select(static name => $"tenant 1: orphan {name}").Order(StringComparer.Ordinal)],
                report.Lines.Where(static line => line.StartsWith("tenant 1: orphan ", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
            Assert.Equal(
                [string.Create(CultureInfo.InvariantCulture, $"tenant 1: missing {BlobName.Variant(thinned, "thumb")} (blob {thinnedId}, Committed)")],
                report.Lines.Where(static line => line.StartsWith("tenant 1: missing ", StringComparison.Ordinal)));
            Assert.Equal("tenant 1: verified (5 objects, 4 rows, 2 orphans, 1 missing)", report.Lines[^1]);
            Assert.Equal(5, store.Names(1).Count);

            // --- With the flag: the same report, the unreferenced objects deleted, every row as it was ---
            string rows = await RowsAsync(migrator);
            MigratorResult deleted = await migrator.RunConvergedAsync("blobs", "verify", "--tenant", "1", "--delete-orphans");

            Assert.Equal("tenant 1: verified (5 objects, 4 rows, 2 orphans, 2 deleted, 1 missing)", deleted.Lines[^1]);
            Assert.Equal([.. new[] { whole, BlobName.Variant(whole, "thumb"), thinned }.Order(StringComparer.Ordinal)], store.Names(1));
            Assert.Equal(rows, await RowsAsync(migrator));
            Assert.Contains(wholeId.ToString(CultureInfo.InvariantCulture), rows, StringComparison.Ordinal);

            // --- A rerun finds nothing left to delete ---
            MigratorResult rerun = await migrator.RunConvergedAsync("blobs", "verify", "--tenant", "1", "--delete-orphans");
            Assert.Equal("tenant 1: verified (3 objects, 4 rows, 0 orphans, 0 deleted, 1 missing)", rerun.Lines[^1]);
        }

        [Fact]
        public async Task Verify_of_a_tenant_the_catalog_does_not_hold_is_a_failure()
        {
            MigratorHarness migrator = new(sql, "mig_blobs_none");
            migrator.Seed(1);
            _ = await migrator.RunConvergedAsync("migrate");

            MigratorResult result = await migrator.RunAsync("blobs", "verify", "--tenant", "7");

            Assert.True(result.ExitCode == MigratorExitCodes.PartialFailure, result.ToString());
            Assert.StartsWith("tenant 7: failed (tenant-not-found: ", result.Lines[^1], StringComparison.Ordinal);
        }

        /// <summary>A fresh primary name of the kind.</summary>
        /// <returns>The name.</returns>
        private static string Name()
        {
            return BlobName.Primary(Kind, Guid.NewGuid().ToString("N"));
        }

        /// <summary>Seeds a blob row of the kind into the tenant's database, uploaded by the system user.</summary>
        /// <param name="migrator">The harness.</param>
        /// <param name="state">The row's state.</param>
        /// <param name="storageKey">Its primary name.</param>
        /// <param name="variants">Its variants.</param>
        /// <param name="createdAt">When it was staged, a SQL expression.</param>
        /// <returns>The row's id.</returns>
        private static Task<int> SeedAsync(MigratorHarness migrator, string state, string storageKey, string variants, string createdAt)
        {
            string committedAt = state == "Committed" ? createdAt : "NULL";
            string expiresAt = state == "Committed" ? "NULL" : "DATEADD(hour, 1, SYSUTCDATETIME())";
            return SqlServerFixture.ScalarAsync<int>(migrator.TenantConnectionString(1), string.Create(CultureInfo.InvariantCulture, $"""
                DECLARE @id int = NEXT VALUE FOR [core].[sq_Blobs];
                INSERT INTO [core].[Blobs] ([Id], [Kind], [State], [ContentType], [FileName], [Size], [Variants], [CreatedAt], [CreatedById], [CommittedAt], [ExpiresAt], [Sha256], [StorageKey])
                VALUES (@id, N'{Kind}', '{state}', N'image/png', NULL, 3, N'{variants}', {createdAt}, {WellKnownIds.SystemUserId}, {committedAt}, {expiresAt}, 0x{new string('0', 64)}, N'{storageKey}');
                SELECT @id;
                """));
        }

        /// <summary>Writes objects of a few bytes into the tenant's storage.</summary>
        /// <param name="store">The store.</param>
        /// <param name="names">The objects' names.</param>
        /// <returns>A task that completes when written.</returns>
        private static Task WriteAsync(InMemoryBlobStore store, params string[] names)
        {
            byte[] bytes = [1, 2, 3];
            return store.WriteAsync(1, [.. names.Select(name => new BlobWrite(name, "image/png", new MemoryStream(bytes), bytes.Length))], Token);
        }

        /// <summary>Every blob row of the tenant, as one text: id, state, variants and expiry, in id order.</summary>
        /// <param name="migrator">The harness.</param>
        /// <returns>The text.</returns>
        private static async Task<string> RowsAsync(MigratorHarness migrator)
        {
            return (await SqlServerFixture.ScalarAsync<string>(
                migrator.TenantConnectionString(1),
                "SELECT STRING_AGG(CONCAT([Id], N'|', [State], N'|', [Variants], N'|', CONVERT(nvarchar(40), [ExpiresAt], 127)), N';') WITHIN GROUP (ORDER BY [Id]) FROM [core].[Blobs];"))!;
        }
    }
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Migrations;
using Tellma.Identity.Options;
using Tellma.Identity.Services.Seeding;

namespace Tellma.Identity.IntegrationTests
{
    /// <summary>
    ///     The store check the engine runs at every start, against a real SQL Server: a missing
    ///     store is created in the platform collation when the host may migrate; a store in another
    ///     collation is refused whether or not it may; a store the host may not migrate must already
    ///     hold the engine's last migration; and the migrator's entry brings an empty store current.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class StoreCheckTests(SqlServerFixture fixture)
    {
        /// <summary>A collation other than the platform's, which no test server uses as the platform's.</summary>
        private const string ForeignCollation = "SQL_Latin1_General_CP1_CI_AS";

        /// <summary>The remedy the failing tests configure, so they can see it quoted.</summary>
        private const string MigrateCommand = "dotnet run --project the-migrator -- migrate";

        [Fact]
        public async Task A_host_that_migrates_creates_a_missing_store_in_the_platform_collation_with_snapshot_isolation()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idstorenew");
            string connectionString = factory.ConfigurationOverrides["TellmaIdentity:ConnectionString"]!;

            using (factory.CreateClient())
            {
                // Started: the check passed on the store the engine itself created.
            }

            Assert.Equal(DatabaseCollations.Platform, await ScalarAsync(
                connectionString, "SELECT CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation'))"));
            Assert.Equal(true, await ScalarAsync(
                connectionString, "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE database_id = DB_ID()"));
            Assert.Contains(TellmaIdentityConstants.LatestMigration, await AppliedMigrationsAsync(connectionString));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task A_store_in_another_collation_is_refused_naming_both_collations(bool applyMigrations)
        {
            string connectionString = await fixture.CreateDatabaseAsync("idstoreforeign", ForeignCollation);
            using StandaloneFactory factory = Factory(connectionString, applyMigrations);

            IdentityStoreCheckException failure = StartupFailure(factory);

            Assert.Contains(DatabaseName(connectionString), failure.Message, StringComparison.Ordinal);
            Assert.Contains(ForeignCollation, failure.Message, StringComparison.Ordinal);
            Assert.Contains(DatabaseCollations.Platform, failure.Message, StringComparison.Ordinal);

            // Refused before anything was written, migrations included.
            Assert.Equal(0, await ScalarAsync(connectionString, "SELECT COUNT(*) FROM sys.tables"));
        }

        [Fact]
        public async Task A_missing_store_is_refused_and_left_uncreated_when_the_host_may_not_migrate()
        {
            string connectionString = fixture.ReserveDatabase("idstoremissing");
            using StandaloneFactory factory = Factory(connectionString, applyMigrations: false);

            IdentityStoreCheckException failure = StartupFailure(factory);

            AssertNamesTheRemedy(failure, connectionString);
            Assert.Null(await ScalarAsync(
                fixture.MasterConnectionString, $"SELECT DB_ID(N'{DatabaseName(connectionString)}')"));
        }

        [Fact]
        public async Task An_empty_store_is_refused_and_left_empty_when_the_host_may_not_migrate()
        {
            string connectionString = await fixture.CreateDatabaseAsync("idstoreempty");
            using StandaloneFactory factory = Factory(connectionString, applyMigrations: false);

            IdentityStoreCheckException failure = StartupFailure(factory);

            AssertNamesTheRemedy(failure, connectionString);
            Assert.Contains(TellmaIdentityConstants.LatestMigration, failure.Message, StringComparison.Ordinal);
            Assert.Equal(0, await ScalarAsync(connectionString, "SELECT COUNT(*) FROM sys.tables"));
        }

        [Fact]
        public async Task A_store_behind_the_engine_is_refused_when_the_host_may_not_migrate()
        {
            string connectionString = await fixture.CreateDatabaseAsync("idstorebehind");
            await TellmaIdentityStore.MigrateAsync(connectionString, TestContext.Current.CancellationToken);
            await ExecuteAsync(
                connectionString,
                $"DELETE FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'{TellmaIdentityConstants.LatestMigration}'");
            using StandaloneFactory factory = Factory(connectionString, applyMigrations: false);

            IdentityStoreCheckException failure = StartupFailure(factory);

            AssertNamesTheRemedy(failure, connectionString);
            Assert.Contains(TellmaIdentityConstants.LatestMigration, failure.Message, StringComparison.Ordinal);
            Assert.Contains($"'{TellmaIdentityConstants.Schema}'", failure.Message, StringComparison.Ordinal);

            // Nothing was seeded into the store the check refused.
            Assert.Equal(0, await ScalarAsync(connectionString, "SELECT COUNT(*) FROM [idsvr].[OpenIddictScopes]"));
        }

        [Fact]
        public async Task A_store_already_migrated_past_the_engine_passes()
        {
            // The previous engine release runs on a store the next one has migrated (N-1).
            string connectionString = await fixture.CreateDatabaseAsync("idstoreahead");
            await TellmaIdentityStore.MigrateAsync(connectionString, TestContext.Current.CancellationToken);
            await ExecuteAsync(
                connectionString,
                "INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'29991231235959_Future', N'10.0.11')");
            using StandaloneFactory factory = Factory(connectionString, applyMigrations: false);

            using (factory.CreateClient())
            {
                // Started: the later row did not fail the check, and seeding ran.
            }

            Assert.NotEqual(0, await ScalarAsync(connectionString, "SELECT COUNT(*) FROM [idsvr].[OpenIddictScopes]"));
        }

        [Fact]
        public async Task A_host_that_migrates_without_the_engine_migrations_is_refused_before_creating_anything()
        {
            string connectionString = fixture.ReserveDatabase("idstorenomigrations");
            using StandaloneFactory factory = Factory(connectionString, applyMigrations: true);

            // The host's own context, pointed at the store with no migrations assembly named: EF
            // would fall back to the engine assembly, which holds none.
            factory.ServiceOverrides.Add(services => services.PostConfigure<TellmaIdentityOptions>(options =>
                options.ConfigureDbContext = (_, builder) => builder.UseSqlServer(connectionString)));

            IdentityStoreCheckException failure = StartupFailure(factory);

            Assert.Contains(TellmaIdentityConstants.MigrationsAssemblyName, failure.Message, StringComparison.Ordinal);
            Assert.Null(await ScalarAsync(
                fixture.MasterConnectionString, $"SELECT DB_ID(N'{DatabaseName(connectionString)}')"));
        }

        [Fact]
        public async Task The_migrator_entry_brings_an_empty_store_current_and_the_check_then_passes()
        {
            string connectionString = await fixture.CreateDatabaseAsync("idstoremigrator");

            await TellmaIdentityStore.MigrateAsync(connectionString, TestContext.Current.CancellationToken);

            Assert.Contains(TellmaIdentityConstants.LatestMigration, await AppliedMigrationsAsync(connectionString));
            using StandaloneFactory factory = Factory(connectionString, applyMigrations: false);
            using (factory.CreateClient())
            {
                // Started: the check passed on a store the host never migrated.
            }

            // The entry applies nothing more on a second run.
            await TellmaIdentityStore.MigrateAsync(connectionString, TestContext.Current.CancellationToken);
            Assert.Single(await AppliedMigrationsAsync(connectionString));
        }

        [Fact]
        public async Task A_login_without_a_user_in_an_existing_store_is_named_not_reported_as_unmigrated()
        {
            // Creating a login needs a server this suite owns: the container, not a developer's.
            Assert.SkipWhen(
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TELLMA_TEST_SQL")),
                "Creates a server login, which only the suite's own container is used for.");

            string connectionString = await fixture.CreateDatabaseAsync("idstorenouser");
            await TellmaIdentityStore.MigrateAsync(connectionString, TestContext.Current.CancellationToken);

            // A login with no user in the store: the operator's missed fourth step.
            string login = $"idnouser_{Guid.NewGuid():N}";
            string password = $"Aa1!{Guid.NewGuid():N}";
            await ExecuteAsync(fixture.MasterConnectionString, $"CREATE LOGIN [{login}] WITH PASSWORD = N'{password}', CHECK_POLICY = OFF");
            try
            {
                SqlConnectionStringBuilder asLogin = new(connectionString)
                {
                    IntegratedSecurity = false,
                    UserID = login,
                    Password = password,
                };
                using StandaloneFactory factory = Factory(asLogin.ConnectionString, applyMigrations: false);

                IdentityStoreCheckException failure = StartupFailure(factory);

                Assert.Contains("cannot open it", failure.Message, StringComparison.Ordinal);
                Assert.Contains("needs a user in it", failure.Message, StringComparison.Ordinal);
                Assert.Contains(login, failure.Message, StringComparison.Ordinal);
                Assert.DoesNotContain(password, failure.Message, StringComparison.Ordinal);
            }
            finally
            {
                await ExecuteAsync(fixture.MasterConnectionString, $"DROP LOGIN [{login}]");
            }
        }

        [Fact]
        public void An_unreachable_store_fails_the_check_naming_the_database()
        {
            // Nothing listens on port 1, so the connection is refused at once.
            using StandaloneFactory factory = Factory(
                "Server=tcp:127.0.0.1,1;Database=idstoreunreachable;User ID=unused;Password=unused;Connect Timeout=2;Encrypt=False",
                applyMigrations: false);

            IdentityStoreCheckException failure = StartupFailure(factory);

            Assert.Contains("could not read database 'idstoreunreachable'", failure.Message, StringComparison.Ordinal);
            Assert.NotNull(Unwrap<System.Data.Common.DbException>(failure));
        }

        [Fact]
        public async Task An_invalid_distribution_entry_stops_the_host_before_its_store_is_touched()
        {
            // Startup validation of seed entries runs only through the host's own options pipeline,
            // so this is the test that fails if that wiring ever drops it.
            string connectionString = fixture.ReserveDatabase("idstoreinvalidseed");
            using StandaloneFactory factory = Factory(connectionString, applyMigrations: true);
            factory.ConfigurationOverrides["TellmaIdentity:Seed:Clients:0:ClientId"] = "Not_A_Slug";
            factory.ConfigurationOverrides["TellmaIdentity:Seed:Clients:0:Kind"] = "Distribution";
            factory.ConfigurationOverrides["TellmaIdentity:Seed:Clients:0:Origin"] = "https://acme.example.com/app";

            Exception exception = Assert.ThrowsAny<Exception>(factory.CreateClient);

            OptionsValidationException failure = Assert.IsType<OptionsValidationException>(
                Unwrap<OptionsValidationException>(exception));
            Assert.Contains(failure.Failures, static f => f.Contains("TellmaIdentity:Seed:Clients:0 ('Not_A_Slug')", StringComparison.Ordinal)
                && f.Contains("slug", StringComparison.Ordinal));
            Assert.Contains(failure.Failures, static f => f.Contains("('Not_A_Slug'): Origin must", StringComparison.Ordinal));
            Assert.Null(await ScalarAsync(
                fixture.MasterConnectionString, $"SELECT DB_ID(N'{DatabaseName(connectionString)}')"));
        }

        /// <summary>The first exception of the given type in a failure's inner chain, or null.</summary>
        private static T? Unwrap<T>(Exception exception)
            where T : Exception
        {
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                if (current is T match)
                {
                    return match;
                }

                if (current is AggregateException aggregate
                    && aggregate.Flatten().InnerExceptions.OfType<T>().FirstOrDefault() is { } inner)
                {
                    return inner;
                }
            }

            return null;
        }

        /// <summary>A standalone host on the given store, quoting <see cref="MigrateCommand" /> on failure.</summary>
        private static StandaloneFactory Factory(string connectionString, bool applyMigrations)
        {
            StandaloneFactory factory = new();
            factory.ConfigurationOverrides["TellmaIdentity:ConnectionString"] = connectionString;
            factory.ConfigurationOverrides["TellmaIdentity:Seed:ApplyMigrations"] = applyMigrations ? "true" : "false";
            factory.ConfigurationOverrides["TellmaIdentity:Seed:MigrateCommand"] = MigrateCommand;
            return factory;
        }

        /// <summary>Starts the host, expecting the store check to stop it, and returns the check's failure.</summary>
        private static IdentityStoreCheckException StartupFailure(StandaloneFactory factory)
        {
            Exception exception = Assert.ThrowsAny<Exception>(factory.CreateClient);

            // The host may wrap what its hosted service threw; the check's own failure is the cause.
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                if (current is IdentityStoreCheckException failure)
                {
                    return failure;
                }

                if (current is AggregateException aggregate)
                {
                    IdentityStoreCheckException? inner = aggregate.Flatten().InnerExceptions
                        .OfType<IdentityStoreCheckException>()
                        .FirstOrDefault();
                    if (inner is not null)
                    {
                        return inner;
                    }
                }
            }

            throw new Xunit.Sdk.XunitException($"The host failed to start, but not on the store check: {exception}");
        }

        /// <summary>Asserts a history failure names the database, the schema and the configured command.</summary>
        private static void AssertNamesTheRemedy(IdentityStoreCheckException failure, string connectionString)
        {
            Assert.Contains($"'{DatabaseName(connectionString)}'", failure.Message, StringComparison.Ordinal);
            Assert.Contains($"'{TellmaIdentityConstants.Schema}'", failure.Message, StringComparison.Ordinal);
            Assert.Contains(MigrateCommand, failure.Message, StringComparison.Ordinal);
        }

        /// <summary>The database a connection string names.</summary>
        private static string DatabaseName(string connectionString)
        {
            return new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        }

        /// <summary>The migration ids recorded in a store's history.</summary>
        private static async Task<List<string>> AppliedMigrationsAsync(string connectionString)
        {
            List<string> ids = [];
            await using SqlConnection connection = new(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT [MigrationId] FROM [dbo].[__EFMigrationsHistory]";
            await using SqlDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                ids.Add(reader.GetString(0));
            }

            return ids;
        }

        /// <summary>Runs one query and returns its first value, or null for none.</summary>
        private static async Task<object?> ScalarAsync(string connectionString, string sql)
        {
            await using SqlConnection connection = new(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = sql;
            object? value = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
            return value is DBNull ? null : value;
        }

        /// <summary>Runs one statement.</summary>
        private static async Task ExecuteAsync(string connectionString, string sql)
        {
            await using SqlConnection connection = new(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }
}

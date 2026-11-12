// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using System.Data;
using System.Data.Common;
using System.Globalization;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Data
{
    /// <summary>
    ///     One fixture database for a test class, created once and reset before each test, so a
    ///     class pays for one database however many tests it holds. A class's tests run one at a
    ///     time, so the reset and the change-tracking audit see only the running test's writes.
    /// </summary>
    /// <param name="sql">The suite's server.</param>
    public sealed class DataBatchDatabase(SqlServerFixture sql) : IAsyncLifetime
    {
        /// <summary>The caller <see cref="SeedCallerAsync" /> stores.</summary>
        public const int CallerId = 1;

        /// <summary>The statements that return the database to its state after creation.</summary>
        private const string ResetText = """
            DELETE FROM [core].[Jobs];
            DELETE FROM [core].[VersionTags];
            DELETE FROM [core].[UserStamps];
            DELETE FROM [core].[Users];
            DELETE FROM [fixture].[WidgetExtras];
            DELETE FROM [dbo].[__TellmaSchema];
            """;

        /// <summary>The <c>settings</c> tag <see cref="SeedCallerAsync" /> stores and <see cref="Premises" /> expects.</summary>
        public static readonly Guid SettingsTag = new("5e771295-0000-0000-0000-000000000001");

        /// <summary>The <c>permissions</c> tag <see cref="SeedCallerAsync" /> stores and <see cref="Premises" /> expects.</summary>
        public static readonly Guid PermissionsTag = new("5e771295-0000-0000-0000-000000000002");

        /// <summary>The caller's per-user permissions tag <see cref="SeedCallerAsync" /> stores and the prologue binds.</summary>
        public static readonly Guid UserPermissionsTag = new("5e771295-0000-0000-0000-000000000003");

        /// <summary>The command timeout of a fixture that times commands out early; the default when null.</summary>
        private readonly TimeSpan? _commandTimeout;

        /// <summary>The host, once created.</summary>
        private FixtureTenant? _tenant;

        /// <summary>Creates a fixture whose host times every command out after the given time.</summary>
        /// <param name="sql">The suite's server.</param>
        /// <param name="commandTimeout">The command timeout of every read, validation and persist.</param>
        internal DataBatchDatabase(SqlServerFixture sql, TimeSpan commandTimeout)
            : this(sql)
        {
            _commandTimeout = commandTimeout;
        }

        /// <summary>The connect-prologue stand-in every batch of the host runs.</summary>
        public ConnectPrologue Prologue { get; } = new();

        /// <summary>The fixture host over the database.</summary>
        public FixtureTenant Tenant => _tenant ?? throw new InvalidOperationException("The database is not created yet.");

        /// <summary>The server's administrative connection, which may end other sessions and see their requests.</summary>
        public string MasterConnectionString => sql.MasterConnectionString;

        /// <summary>The host's log records.</summary>
        public FakeLogCollector Logs => Tenant.Services.GetFakeLogCollector();

        /// <summary>What the connect-prologue stand-in confirms for a user's batch, matching the seeded caller.</summary>
        internal static GuardPremises Premises => GuardPremises.ForUser(new VersionTag(SettingsTag), new VersionTag(PermissionsTag));

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            // Every round trip is slow, so each logs its text and a test can read the record.
            _tenant = await FixtureTenant.CreateAsync(
                sql.MasterConnectionString,
                sql.NewDatabaseName("fx_batch"),
                services =>
                {
                    _ = services.AddSingleton<IDataBatchContributor>(Prologue);
                    _ = services.AddFakeLogging();
                    _ = services.Configure<DataOptions>(options =>
                    {
                        options.SlowRoundTripThreshold = TimeSpan.FromTicks(1);
                        options.CommandTimeout = _commandTimeout ?? options.CommandTimeout;
                    });
                },
                cancellationToken: TestContext.Current.CancellationToken);
        }

        /// <summary>
        ///     Deletes every row a test may have written, lists the running fingerprint again, and
        ///     clears the log and the prologue's settings.
        /// </summary>
        /// <returns>A task that completes when the database is reset.</returns>
        public async Task ResetAsync()
        {
            await SqlServerFixture.ExecuteAsync(Tenant.ConnectionString, ResetText);
            await FixtureDatabase.RecordFingerprintAsync(Tenant.ConnectionString, Tenant.Fingerprint, TestContext.Current.CancellationToken);
            Prologue.Passes = true;
            Prologue.ExpectedUserPermissionsTag = UserPermissionsTag;
            Logs.Clear();
        }

        /// <summary>
        ///     Stores what <see cref="Premises" /> and the prologue expect: the <c>settings</c> and
        ///     <c>permissions</c> tags, and an active caller whose stamp carries its permissions tag.
        /// </summary>
        /// <returns>A task that completes when they are stored.</returns>
        public async Task SeedCallerAsync()
        {
            await SqlServerFixture.ExecuteAsync(
                Tenant.ConnectionString,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"INSERT INTO [core].[VersionTags] ([Name], [Tag]) VALUES (N'{VersionTagNames.Settings}', '{SettingsTag}'), (N'{VersionTagNames.Permissions}', '{PermissionsTag}');"),
                string.Create(CultureInfo.InvariantCulture, $"INSERT INTO [core].[Users] ([Id], [Name], [IsActive]) VALUES ({CallerId}, N'fx-caller', 1);"),
                string.Create(CultureInfo.InvariantCulture, $"INSERT INTO [core].[UserStamps] ([UserId], [PermissionsTag]) VALUES ({CallerId}, '{UserPermissionsTag}');"));
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _tenant?.DisposeAsync() ?? ValueTask.CompletedTask;
        }

        /// <summary>
        ///     A stand-in for the connect prologue: on a marked batch it declares the guard variable with
        ///     the verdict the test chose, returns it, and reports it from its reader. For a user's batch
        ///     it also declares the caller and binds the caller's permissions tag, which the persist
        ///     re-checks name.
        /// </summary>
        public sealed class ConnectPrologue : IDataBatchContributor
        {
            /// <summary>The verdict the next batch's guard reports.</summary>
            public bool Passes { get; set; } = true;

            /// <summary>The caller's per-user permissions tag the prologue confirmed.</summary>
            public Guid ExpectedUserPermissionsTag { get; set; } = UserPermissionsTag;

            /// <inheritdoc />
            public int Order => 0;

            /// <inheritdoc />
            public void Contribute(IDataBatch batch, DataBatchStage stage)
            {
                if (stage != DataBatchStage.Prologue || batch is not DataBatch { Premises: GuardPremises premises } marked)
                {
                    return;
                }

                // --- The guard variable, and for a user the caller and the caller's tag ---
                StatementDraft draft = marked.Begin();
                int ordinal = draft.Ordinal;
                string caller = string.Empty;
                if (!premises.IsSystem)
                {
                    caller = string.Create(CultureInfo.InvariantCulture, $"DECLARE {ReservedSqlNames.UserId} int = {CallerId};\n");
                    draft.Reserved(new ScalarParameter(ReservedSqlNames.ExpectedUserPermissionsTag, SqlDbType.UniqueIdentifier, ExpectedUserPermissionsTag));
                }

                marked.Append(
                    draft,
                    $"DECLARE {ReservedSqlNames.Guard} bit = {(Passes ? 1 : 0)};\n{caller}SELECT {ReservedSqlNames.Guard} AS [Guard];",
                    reader: async (cursor, cancellationToken) =>
                    {
                        DbDataReader reader = await cursor.NextAsync(ordinal, cancellationToken);
                        _ = await reader.ReadAsync(cancellationToken);
                        marked.ReportGuard(reader.GetBoolean(0));
                        cursor.CountRows(1);
                    });
            }
        }
    }
}

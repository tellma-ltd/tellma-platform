// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Testing;
using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Caching;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Tenancy;
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
        /// <summary>
        ///     The caller <see cref="SeedCallerAsync" /> stores: the first id after the reserved band, so
        ///     the seeded system user and its stamp are never the caller and survive every reset.
        /// </summary>
        public const int CallerId = WellKnownIds.ReservedIdBandEnd + 1;

        /// <summary>The statements that return the database to its state after creation, the seeded rows kept.</summary>
        private const string ResetText = """
            DELETE FROM [core].[Jobs];
            DELETE FROM [core].[VersionTags];
            DELETE FROM [core].[UserStamps] WHERE [UserId] > 999;
            DELETE FROM [core].[Users] WHERE [Id] > 999;
            DELETE FROM [fixture].[WidgetExtras];
            DELETE FROM [fixture].[Lookups];
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

        /// <summary>Whether the host audits every batch's writes against change tracking.</summary>
        private readonly bool _audit = true;

        /// <summary>The host, once created.</summary>
        private FixtureTenant? _tenant;

        /// <summary>The server round trips of every connection closed since the last <see cref="ResetRoundTrips" />.</summary>
        private long _roundTrips;

        /// <summary>Creates a fixture whose host times every command out after the given time.</summary>
        /// <param name="sql">The suite's server.</param>
        /// <param name="commandTimeout">The command timeout of every read, validation and persist.</param>
        internal DataBatchDatabase(SqlServerFixture sql, TimeSpan commandTimeout)
            : this(sql)
        {
            _commandTimeout = commandTimeout;
        }

        /// <summary>
        ///     Creates a fixture whose host runs without the change-tracking audit, for tests whose
        ///     batches overlap: the audit is exact only while one batch at a time writes.
        /// </summary>
        /// <param name="sql">The suite's server.</param>
        /// <param name="audit">False, for a host without the audit.</param>
        internal DataBatchDatabase(SqlServerFixture sql, bool audit)
            : this(sql)
        {
            _audit = audit;
        }

        /// <summary>The connect-prologue stand-in every batch of the host runs.</summary>
        public ConnectPrologue Prologue { get; } = new();

        /// <summary>Every client event the host's batches published, in order; cleared by <see cref="ResetAsync" />.</summary>
        public RecordingPublisher Events { get; } = new();

        /// <summary>The fixture host over the database.</summary>
        public FixtureTenant Tenant => _tenant ?? throw new InvalidOperationException("The database is not created yet.");

        /// <summary>
        ///     The server round trips the host's batch connections took since the last
        ///     <see cref="ResetRoundTrips" />, as the client library counts them; the change-tracking
        ///     audit's own connections are not counted.
        /// </summary>
        public long ServerRoundTrips => Interlocked.Read(ref _roundTrips);

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
                    _ = services.AddScoped<IClientEventPublisher>(_ => Events);

                    // The fixture's connection provider, wrapped so each connection counts its server round trips.
                    ServiceDescriptor original = services.Last(static descriptor => descriptor.ServiceType == typeof(ITenantConnectionProvider));
                    Func<IServiceProvider, object> factory = original.ImplementationFactory
                        ?? throw new InvalidOperationException("The fixture registers its connection provider by factory.");
                    _ = services.Replace(ServiceDescriptor.Scoped<ITenantConnectionProvider>(
                        provider => new CountingConnections((ITenantConnectionProvider)factory(provider), this)));
                    _ = services.AddFakeLogging();
                    _ = services.Configure<DataOptions>(options =>
                    {
                        options.SlowRoundTripThreshold = TimeSpan.FromTicks(1);
                        options.CommandTimeout = _commandTimeout ?? options.CommandTimeout;
                    });
                },
                audit: _audit,
                cancellationToken: TestContext.Current.CancellationToken);
        }

        /// <summary>Forgets the server round trips counted so far.</summary>
        public void ResetRoundTrips()
        {
            _ = Interlocked.Exchange(ref _roundTrips, 0);
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
            Prologue.UserId = CallerId;
            Events.Clear();
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
                    $"INSERT INTO [core].[VersionTags] ([Name], [Tag]) VALUES (N'{VersionTagNames.Settings}', '{SettingsTag}'), (N'{VersionTagNames.Permissions}', '{PermissionsTag}');"));
            await SeedUserAsync(CallerId);
        }

        /// <summary>
        ///     Stores an active user who has joined, with a stamp carrying <see cref="UserPermissionsTag" />,
        ///     so the prologue stand-in may act for them (<see cref="ConnectPrologue.UserId" />).
        /// </summary>
        /// <param name="userId">The user, above the reserved band.</param>
        /// <returns>A task that completes when the user is stored.</returns>
        public async Task SeedUserAsync(int userId)
        {
            // An individual who was invited and has joined, created by the system user, as the checks require.
            string subject = string.Create(CultureInfo.InvariantCulture, $"fx-user-{userId}");
            await SqlServerFixture.ExecuteAsync(
                Tenant.ConnectionString,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"INSERT INTO [core].[Users] ([Id], [Kind], [Subject], [Email], [InvitedAt], [JoinedAt], [Name], [IsActive], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]) VALUES ({userId}, '{UserKind.Individual}', '{subject}', N'{subject}@example.com', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'{subject}', 1, SYSDATETIMEOFFSET(), {WellKnownIds.SystemUserId}, SYSDATETIMEOFFSET(), {WellKnownIds.SystemUserId});"),
                string.Create(CultureInfo.InvariantCulture, $"INSERT INTO [core].[UserStamps] ([UserId], [PermissionsTag]) VALUES ({userId}, '{UserPermissionsTag}');"));
        }

        /// <summary>
        ///     Stores a row for every version tag the host registers that the database lacks, as the
        ///     migrator's seed does; the rows <see cref="SeedCallerAsync" /> stored keep their tags.
        /// </summary>
        /// <returns>A task that completes when they are stored.</returns>
        public async Task SeedTagsAsync()
        {
            string names = string.Join(", ", Tenant.Services.GetRequiredService<IVersionTagRegistry>().Names.Select(static name => $"(N'{name}')"));
            await SqlServerFixture.ExecuteAsync(
                Tenant.ConnectionString,
                $"INSERT INTO [core].[VersionTags] ([Name], [Tag]) SELECT n.[Name], NEWID() FROM (VALUES {names}) AS n ([Name]) WHERE NOT EXISTS (SELECT 1 FROM [core].[VersionTags] AS t WHERE t.[Name] = n.[Name]);");
        }

        /// <summary>
        ///     A scope of the fixture tenant whose context carries settings of one language, as the
        ///     settings load leaves it, so what reads the tenant's multilingual shape can.
        /// </summary>
        /// <returns>The scope.</returns>
        public AsyncServiceScope CreateSettledScope()
        {
            AsyncServiceScope scope = Tenant.CreateScope();
            IRequestContextHolder holder = scope.ServiceProvider.GetRequiredService<IRequestContextHolder>();
            TenantSettings settings = new()
            {
                TenantId = FixtureTenant.TenantId,
                Tag = VersionTag.New(),
                Names = ["Fixture", null, null],
                Languages = [new LanguageInfo("en", "English", "E", false)],
                Shape = MultilingualShape.Primary,
                Calendars = [],
                TimeZone = TimeZoneInfo.Utc,
                SqlServerTimeZoneName = "UTC",
                ModifiedAt = DateTimeOffset.UnixEpoch,
                Entries = new Dictionary<string, JsonElement>(),
            };
            holder.Set(holder.Current with { TenantSettings = settings });
            return scope;
        }

        /// <summary>Reads the database's version tags.</summary>
        /// <returns>Each name's tag.</returns>
        public async Task<Dictionary<string, Guid>> TagsAsync()
        {
            Dictionary<string, Guid> tags = new(StringComparer.Ordinal);
            await using SqlConnection connection = new(Tenant.ConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT [Name], [Tag] FROM [core].[VersionTags];";
            await using SqlDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                tags[reader.GetString(0)] = reader.GetGuid(1);
            }

            return tags;
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _tenant?.DisposeAsync() ?? ValueTask.CompletedTask;
        }

        /// <summary>A client event publisher that records every event, published at once or after a commit alike.</summary>
        public sealed class RecordingPublisher : IClientEventPublisher
        {
            /// <summary>The events, in order.</summary>
            private readonly ConcurrentQueue<ClientEvent> _events = new();

            /// <summary>The events published so far, in order.</summary>
            public IReadOnlyList<ClientEvent> Published => [.. _events];

            /// <summary>Forgets every event.</summary>
            public void Clear()
            {
                _events.Clear();
            }

            /// <inheritdoc />
            public void Publish(IDataBatch? batch, ClientEvent clientEvent)
            {
                ArgumentNullException.ThrowIfNull(clientEvent);
                _events.Enqueue(clientEvent);
            }

            /// <inheritdoc />
            public Task PublishAsync(ClientEvent clientEvent)
            {
                Publish(null, clientEvent);
                return Task.CompletedTask;
            }
        }

        /// <summary>
        ///     A stand-in for the connect prologue: on a marked batch it reads, as the real prologue
        ///     does, the caller's stamp for a user's batch as its first result set and every
        ///     tenant-level tag as the next, declares the guard variable with the verdict the test
        ///     chose, returns it, and reports all three from its reader. For a user's batch it also
        ///     declares the caller and the caller's id table and binds the caller's permissions tag,
        ///     which the persist re-checks name.
        /// </summary>
        public sealed class ConnectPrologue : IDataBatchContributor
        {
            /// <summary>The verdict the next batch's guard reports.</summary>
            public bool Passes { get; set; } = true;

            /// <summary>The caller's per-user permissions tag the prologue confirmed.</summary>
            public Guid ExpectedUserPermissionsTag { get; set; } = UserPermissionsTag;

            /// <summary>The user the next user's batch acts for, read when the batch is composed.</summary>
            public int UserId { get; set; } = CallerId;

            /// <inheritdoc />
            public int Order => 0;

            /// <inheritdoc />
            public void Contribute(IDataBatch batch, DataBatchStage stage)
            {
                if (stage != DataBatchStage.Prologue || batch is not DataBatch { Premises: GuardPremises premises } marked)
                {
                    return;
                }

                // --- For a user the caller, the caller's ids, tag and stamp; then the tags and the guard variable ---
                StatementDraft draft = marked.BeginConnectPrologue();
                int ordinal = draft.Ordinal;
                int userId = UserId;
                string caller = string.Empty;
                if (!premises.IsSystem)
                {
                    caller = string.Create(
                        CultureInfo.InvariantCulture,
                        $"DECLARE {ReservedSqlNames.UserId} int = {userId};\nDECLARE {ReservedSqlNames.CallerIds} TABLE ([Id] int PRIMARY KEY);\nINSERT INTO {ReservedSqlNames.CallerIds} ([Id]) VALUES ({ReservedSqlNames.UserId});\nSELECT [PreferencesTag], [PermissionsTag] FROM [core].[UserStamps] WHERE [UserId] = {ReservedSqlNames.UserId};\n");
                    draft.Reserved(new ScalarParameter(ReservedSqlNames.ExpectedUserPermissionsTag, SqlDbType.UniqueIdentifier, ExpectedUserPermissionsTag));
                }

                marked.Append(
                    draft,
                    $"{caller}{VersionTagPrelude.Text}\nDECLARE {ReservedSqlNames.Guard} bit = {(Passes ? 1 : 0)};\nSELECT {ReservedSqlNames.Guard} AS [Guard];",
                    reader: async (cursor, cancellationToken) =>
                    {
                        // The caller's stamp, when the batch is a user's and the stamp exists.
                        if (!premises.IsSystem)
                        {
                            DbDataReader stamp = await cursor.NextAsync(ordinal, cancellationToken);
                            if (await stamp.ReadAsync(cancellationToken))
                            {
                                marked.ReportUserVersionTags(new UserVersionTagSnapshot(
                                    marked.Connection.TenantId, userId, new VersionTag(stamp.GetGuid(0)), new VersionTag(stamp.GetGuid(1)), DateTimeOffset.UtcNow));
                                cursor.CountRows(1);
                            }
                        }

                        // The prelude's rows, read as the real prologue reads them.
                        await VersionTagPrelude.ReadAsync(marked, cursor, ordinal, TimeProvider.System, cancellationToken);

                        DbDataReader reader = await cursor.NextAsync(ordinal, cancellationToken);
                        _ = await reader.ReadAsync(cancellationToken);
                        marked.ReportGuard(reader.GetBoolean(0));
                        cursor.CountRows(1);
                    });
            }
        }

        /// <summary>The fixture's connections, each adding its server round trips to the database's total when it closes.</summary>
        /// <param name="inner">The fixture's provider.</param>
        /// <param name="database">The database whose total grows.</param>
        private sealed class CountingConnections(ITenantConnectionProvider inner, DataBatchDatabase database) : ITenantConnectionProvider
        {
            /// <inheritdoc />
            public TenantInfo Tenant => inner.Tenant;

            /// <inheritdoc />
            public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken = default)
            {
                SqlConnection connection = await inner.OpenAsync(cancellationToken).ConfigureAwait(false);
                connection.StatisticsEnabled = true;
                connection.StateChange += (_, change) =>
                {
                    if (change.CurrentState == ConnectionState.Closed)
                    {
                        long trips = Convert.ToInt64(connection.RetrieveStatistics()["ServerRoundtrips"], CultureInfo.InvariantCulture);
                        _ = Interlocked.Add(ref database._roundTrips, trips);
                    }
                };

                return connection;
            }
        }
    }
}

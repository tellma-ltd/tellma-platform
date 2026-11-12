// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Tenancy;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Data
{
    /// <summary>
    ///     One fixture database seeded once with the rows the query suite reads: widgets with parts,
    ///     notes and extras, a small tree of nodes, and shipments with <c>bigint</c> keys. Its
    ///     connections report their server round trips, so a test reads the round trips a batch
    ///     took from the driver rather than from the executor's own count.
    /// </summary>
    /// <param name="sql">The suite's server.</param>
    public sealed class DataQueryDatabase(SqlServerFixture sql) : IAsyncLifetime
    {
        /// <summary>The first shipment's key, above the <c>int</c> range.</summary>
        public const long FirstShipmentId = 5_000_000_001;

        /// <summary>The second shipment's key.</summary>
        public const long SecondShipmentId = 5_000_000_002;

        /// <summary>The day every scope's request context names as today, which no server clock reads.</summary>
        public static readonly DateOnly Today = new(2026, 3, 14);

        /// <summary>
        ///     The seed: widgets 1 to 4, their parts, notes and extras, nodes 1 to 5, two shipments, and
        ///     users 2 and 3, whose binary-collated subjects differ only in case.
        /// </summary>
        private static readonly string[] Seed =
        [
            Stamped(
                "fixture.Widgets",
                "[Id], [Code], [Kind], [Name], [Name2], [Name3], [Subject], [Price], [OptionalKind], [DueOn], [Settings]",
                "(1, 'W1', 'Plain', N'Alpha', N'ألفا', N'Alfa', N's1', 10.5, 'Fancy', '2026-03-01', N'{\"a\":1}')",
                "(2, 'W2', 'Fancy', N'Beta', NULL, NULL, N's2', NULL, NULL, '2026-03-02', NULL)",
                "(3, 'W3', 'Plain', N'Gamma', NULL, NULL, N's3', 30, 'Plain', '2026-03-03', NULL)",
                "(4, 'W4', 'Fancy', N'Delta', NULL, NULL, N's4', 40, NULL, NULL, NULL)"),
            """
            INSERT INTO [fixture].[WidgetExtras] ([Id], [Note]) VALUES (1, N'first'), (3, NULL);
            INSERT INTO [fixture].[WidgetParts] ([Id], [WidgetId], [Ordinal], [Notes], [Settings]) VALUES (11, 1, 1, N'a', N'{"p":1}'), (12, 1, 2, N'b', NULL), (21, 2, 1, N'c', NULL);
            INSERT INTO [fixture].[WidgetPartNotes] ([Id], [WidgetPartId], [Text]) VALUES (111, 11, N'n111'), (112, 11, N'n112'), (211, 21, N'n211');
            """,
            Stamped(
                "fixture.Nodes",
                "[Id], [Code], [Name], [Node], [ParentId]",
                "(1, 'N1', N'Root', '/1/', NULL)",
                "(2, 'N11', N'Branch', '/1/1/', 1)",
                "(3, 'N111', N'Leaf', '/1/1/1/', 2)",
                "(4, 'N2', N'Other', '/2/', NULL)",
                "(5, 'N12', N'Twig', '/1/2/', 1)"),
            Stamped(
                "fixture.Shipments",
                "[Id], [State], [Payload]",
                string.Create(CultureInfo.InvariantCulture, $"({FirstShipmentId}, 'Pending', NULL)"),
                string.Create(CultureInfo.InvariantCulture, $"({SecondShipmentId}, 'Shipped', N'x')")),
            Stamped(
                "core.Users",
                "[Id], [Kind], [Subject], [Email], [InvitedAt], [JoinedAt], [Name], [IsActive]",
                "(2, 'Individual', 'Sub-A', N'a@example.com', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'Upper', 1)",
                "(3, 'Individual', 'sub-a', N'b@example.com', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'Lower', 1)"),
        ];

        /// <summary>The round trips of every connection closed since the last <see cref="ResetRoundTrips" />.</summary>
        private long _roundTrips;

        /// <summary>The host, once created.</summary>
        private FixtureTenant? _tenant;

        /// <summary>The fixture host over the database.</summary>
        public FixtureTenant Tenant => _tenant ?? throw new InvalidOperationException("The database is not created yet.");

        /// <summary>The server round trips the host's batch connections took since the last reset.</summary>
        public long RoundTrips => Interlocked.Read(ref _roundTrips);

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            _tenant = await FixtureTenant.CreateAsync(
                sql.MasterConnectionString,
                sql.NewDatabaseName("fx_query"),
                services =>
                {
                    // The fixture's connection provider, wrapped so each connection counts its round trips.
                    ServiceDescriptor original = services.Last(static descriptor => descriptor.ServiceType == typeof(ITenantConnectionProvider));
                    Func<IServiceProvider, object> factory = original.ImplementationFactory
                        ?? throw new InvalidOperationException("The fixture registers its connection provider by factory.");
                    _ = services.Replace(ServiceDescriptor.Scoped<ITenantConnectionProvider>(
                        provider => new CountingConnections((ITenantConnectionProvider)factory(provider), this)));
                },
                cancellationToken: TestContext.Current.CancellationToken);

            await SqlServerFixture.ExecuteAsync(Tenant.ConnectionString, Seed);
        }

        /// <summary>Forgets the round trips counted so far.</summary>
        public void ResetRoundTrips()
        {
            _ = Interlocked.Exchange(ref _roundTrips, 0);
        }

        /// <summary>
        ///     Opens a scope whose request context carries what a query statement binds: tenant
        ///     settings of the given shape, today and now, and no user.
        /// </summary>
        /// <param name="shape">The tenant's content languages.</param>
        /// <returns>The scope.</returns>
        public AsyncServiceScope CreateScope(MultilingualShape shape = MultilingualShape.All)
        {
            AsyncServiceScope scope = Tenant.CreateScope();
            IRequestContextHolder holder = scope.ServiceProvider.GetRequiredService<IRequestContextHolder>();
            TenantSettings settings = new()
            {
                TenantId = FixtureTenant.TenantId,
                Tag = VersionTag.New(),
                Names = ["Fixture", null, null],
                Languages = [new LanguageInfo("en", "English", "E", false)],
                Shape = shape,
                Calendars = [],
                TimeZone = TimeZoneInfo.Utc,
                SqlServerTimeZoneName = "UTC",
                ModifiedAt = DateTimeOffset.UnixEpoch,
                Entries = new Dictionary<string, System.Text.Json.JsonElement>(),
            };
            holder.Set(holder.Current with
            {
                TenantSettings = settings,
                Today = Today,
                Now = new DateTimeOffset(Today.ToDateTime(new TimeOnly(9, 30)), TimeSpan.Zero),
            });
            return scope;
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _tenant?.DisposeAsync() ?? ValueTask.CompletedTask;
        }

        /// <summary>An insert of top-level rows, stamped as created and modified by the system user.</summary>
        /// <param name="table">The table.</param>
        /// <param name="columns">The columns beyond the stamps.</param>
        /// <param name="rows">Each row's values beyond the stamps, parenthesized.</param>
        /// <returns>The statement.</returns>
        private static string Stamped(string table, string columns, params string[] rows)
        {
            string stamp = string.Create(CultureInfo.InvariantCulture, $", SYSDATETIMEOFFSET(), {WellKnownIds.SystemUserId}, SYSDATETIMEOFFSET(), {WellKnownIds.SystemUserId})");
            IEnumerable<string> stamped = rows.Select(row => row[..^1] + stamp);
            return $"INSERT INTO {table} ({columns}, [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]) VALUES {string.Join(", ", stamped)};";
        }

        /// <summary>The fixture's connections, each counting its server round trips into the database's total when it closes.</summary>
        /// <param name="inner">The fixture's provider.</param>
        /// <param name="database">The database whose total grows.</param>
        private sealed class CountingConnections(ITenantConnectionProvider inner, DataQueryDatabase database) : ITenantConnectionProvider
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
                    if (change.CurrentState == System.Data.ConnectionState.Closed)
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

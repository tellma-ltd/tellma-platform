// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.Collections.Concurrent;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Composition;
using Tellma.Core.Crud;
using Tellma.Core.Data.Batches;
using Tellma.Core.IntegrationTests.Jobs;
using Tellma.Core.Tenancy;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Notifications
{
    /// <summary>
    ///     One fixture database for an inbox, preferences or notification jobs test class, created once
    ///     with its users and reset before each test. The stacks own their tables, as in production; the
    ///     users have stamp rows, so each can connect and call the inbox and preference services as
    ///     themselves. Its host delivers email and sms through senders that record what they were
    ///     given (sms throws on request), names one administrator, records the client events committed
    ///     batches publish, and audits every batch's writes against change tracking.
    /// </summary>
    public class InboxDatabase : IAsyncLifetime
    {
        /// <summary>The caller most tests act as.</summary>
        public const int AdaId = 101;

        /// <summary>Another member, whose rows the caller must never reach.</summary>
        public const int BenId = 102;

        /// <summary>A member who acts on the caller's notifications.</summary>
        public const int CyId = 103;

        /// <summary>Another acting member.</summary>
        public const int DeeId = 104;

        /// <summary>The administrator the failure notices reach.</summary>
        public const int AdministratorId = 111;

        /// <summary>An inactive user.</summary>
        public const int InactiveUserId = 151;

        /// <summary>The time zone Cy prefers, three hours east of the tenant's UTC.</summary>
        public const string CyTimeZone = "Asia/Riyadh";

        /// <summary>The statements that return the database to its state after creation, the users kept.</summary>
        private const string ResetText = """
            DELETE FROM [core].[Notifications];
            DELETE FROM [core].[NotificationPreferences];
            DELETE FROM [core].[Jobs];
            DELETE FROM [fixture].[Shipments];
            UPDATE [core].[UserStamps] SET [InboxSeenAt] = NULL, [InboxVersion] = 0;
            UPDATE [core].[JobWorkerState] SET [LastTickAt] = NULL, [LastTickOwner] = NULL;
            """;

        /// <summary>The suite's server.</summary>
        private readonly SqlServerFixture _sql;

        /// <summary>The host, once created.</summary>
        private FixtureTenant? _tenant;

        /// <summary>Creates the fixture over a database with read-committed snapshot on, as provisioning creates it.</summary>
        /// <param name="sql">The suite's server.</param>
        public InboxDatabase(SqlServerFixture sql)
            : this(sql, readCommittedSnapshot: true)
        {
        }

        /// <summary>Creates the fixture over a database with read-committed snapshot on or off.</summary>
        /// <param name="sql">The suite's server.</param>
        /// <param name="readCommittedSnapshot">Whether the database reads committed rows from row versions.</param>
        protected InboxDatabase(SqlServerFixture sql, bool readCommittedSnapshot)
        {
            ArgumentNullException.ThrowIfNull(sql);
            _sql = sql;
            ReadCommittedSnapshot = readCommittedSnapshot;
        }

        /// <summary>Whether the database reads committed rows from row versions.</summary>
        public bool ReadCommittedSnapshot { get; }

        /// <summary>The fixture host over the database.</summary>
        public FixtureTenant Tenant => _tenant ?? throw new InvalidOperationException("The database is not created yet.");

        /// <summary>The database's connection string.</summary>
        public string ConnectionString => Tenant.ConnectionString;

        /// <summary>The client events committed batches published since the last reset.</summary>
        public CommittedEvents Events { get; } = new();

        /// <summary>What the senders were given since the last reset.</summary>
        public DeliveryLog Deliveries { get; } = new();

        /// <summary>The host's log, debug records included.</summary>
        public FakeLogCollector Log => Tenant.Services.GetFakeLogCollector();

        /// <summary>The retention page size the host runs with, small so a test spans several pages.</summary>
        public static int RetentionPageSize => 3;

        /// <summary>The cancellation token of the running test.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            _tenant = await FixtureTenant.CreateAsync(
                _sql.MasterConnectionString,
                _sql.NewDatabaseName(ReadCommittedSnapshot ? "fx_inbox" : "fx_inbox_locking"),
                services =>
                {
                    // --- The recording senders in place of the platform's email sender, events and
                    // administrator; small retention pages; the log ---
                    _ = services.AddSingleton(Deliveries);
                    _ = services.RemoveAll<INotificationChannel>();
                    _ = services.AddScoped<INotificationChannel, RecordingEmail>();
                    _ = services.AddScoped<INotificationChannel, RecordingSms>();
                    _ = services.Replace(ServiceDescriptor.Scoped<IClientEventPublisher>(_ => Events));
                    _ = services.Replace(ServiceDescriptor.Scoped<IAdministratorDirectory>(static _ => new FixedAdministrators()));
                    _ = services.PostConfigure<NotificationsOptions>(static options => options.RetentionPageSize = RetentionPageSize);
                    _ = services.AddFakeLogging();
                    _ = services.AddLogging(static logging => logging.SetMinimumLevel(LogLevel.Debug));

                    // --- The stacks' tables are theirs again, as in production: a raw write to one is refused ---
                    _ = services.Replace(ServiceDescriptor.Singleton<IStackTableOwners>(static provider => new StackTableOwners(provider.GetRequiredService<StackRegistry>())));
                },
                readCommittedSnapshot: ReadCommittedSnapshot,
                cancellationToken: Token);

            // --- The tenant's settings and version tags, then the users with their stamps ---
            string tags = string.Join(", ", Tenant.Services.GetRequiredService<IVersionTagRegistry>().Names.Select(static name => $"(N'{name}')"));
            await SqlServerFixture.ExecuteAsync(
                ConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Settings] SET [TenantId] = {FixtureTenant.TenantId}, [Name] = N'Fixture';"),
                $"INSERT INTO [core].[VersionTags] ([Name], [Tag]) SELECT n.[Name], NEWID() FROM (VALUES {tags}) AS n ([Name]) WHERE NOT EXISTS (SELECT 1 FROM [core].[VersionTags] AS t WHERE t.[Name] = n.[Name]);",
                UserInsert(AdaId, "Ada", active: true),
                UserInsert(BenId, "Ben", active: true),
                UserInsert(CyId, "Cy", active: true, timeZone: CyTimeZone),
                UserInsert(DeeId, "Dee", active: true),
                UserInsert(AdministratorId, "Admin", active: true),
                UserInsert(InactiveUserId, "Gone", active: false));
        }

        /// <summary>Deletes every row a test may have written, the users kept, and forgets the events and deliveries.</summary>
        /// <returns>A task that completes when the database is reset.</returns>
        public async Task ResetAsync()
        {
            await SqlServerFixture.ExecuteAsync(ConnectionString, ResetText);
            Events.Clear();
            Deliveries.Clear();
            Log.Clear();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            return _tenant?.DisposeAsync() ?? ValueTask.CompletedTask;
        }

        /// <summary>
        ///     Opens a scope connected as a user, as a request of theirs would: the tenant in the given
        ///     state, the user found by their subject, the context initialised.
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <param name="state">The tenant's state.</param>
        /// <returns>The scope; the caller disposes it.</returns>
        public async Task<AsyncServiceScope> UserScopeAsync(int userId, TenantState state = TenantState.Active)
        {
            AsyncServiceScope scope = Tenant.Services.CreateAsyncScope();
            try
            {
                IRequestContextHolder holder = scope.ServiceProvider.GetRequiredService<IRequestContextHolder>();
                RequestContext context = holder.Current with
                {
                    Tenant = new TenantDescriptor(FixtureTenant.TenantId, "Fixture", null, null, ["en"], TenantCategory.Live, state, null),
                    Kind = PrincipalKind.User,
                    Subject = Subject(userId),
                };
                holder.Set(context);
                _ = await RequestContextInitialization.RunAsync(
                    holder,
                    scope.ServiceProvider.GetServices<IRequestContextInitializer>(),
                    context,
                    new RequestContextInputs(null, null, null, null, null, null, SuppressActivityStamp: false),
                    Token);
                return scope;
            }
            catch
            {
                await scope.DisposeAsync();
                throw;
            }
        }

        /// <summary>Invokes a routed service's action as a user, through the action invoker.</summary>
        /// <typeparam name="T">What the action answers.</typeparam>
        /// <param name="userId">The caller.</param>
        /// <param name="route">The service's route.</param>
        /// <param name="action">The action's name.</param>
        /// <param name="body">The body; null for none.</param>
        /// <param name="state">The tenant's state.</param>
        /// <returns>The answer.</returns>
        public async Task<T> InvokeAsync<T>(int userId, string route, string action, object? body = null, TenantState state = TenantState.Active)
        {
            await using AsyncServiceScope scope = await UserScopeAsync(userId, state);
            ApiActionDescriptor descriptor = scope.ServiceProvider.GetRequiredService<IStackRegistry>().Services
                .Single(service => service.Route == route).Actions
                .Single(candidate => candidate.Name == action);
            return (T)(await scope.ServiceProvider.GetRequiredService<IApiActionInvoker>().InvokeAsync(descriptor, body, Token))!;
        }

        /// <summary>Inserts an inbox row directly, created some seconds ago.</summary>
        /// <param name="userId">The recipient.</param>
        /// <param name="secondsAgo">How long ago the row was created.</param>
        /// <param name="severity">The severity's name.</param>
        /// <param name="actor">The acting user; null for none.</param>
        /// <param name="readSecondsAgo">How long ago the row was read; null for unread.</param>
        /// <param name="type">The type key.</param>
        /// <returns>The row's id.</returns>
        public Task<int> InsertAsync(int userId, int secondsAgo, string severity = "Info", int? actor = null, int? readSecondsAgo = null, string type = FixtureNotifications.Held)
        {
            string actorText = actor?.ToString(CultureInfo.InvariantCulture) ?? "NULL";
            string readAt = readSecondsAgo is int read ? string.Create(CultureInfo.InvariantCulture, $"DATEADD(second, -{read}, SYSUTCDATETIME())") : "NULL";
            return SqlServerFixture.ScalarAsync<int>(
                ConnectionString,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"""
                    DECLARE @id int = NEXT VALUE FOR [core].[sq_Notifications];
                    INSERT INTO [core].[Notifications] ([Id], [UserId], [Type], [Severity], [ActorUserId], [CreatedAt], [ReadAt])
                    VALUES (@id, {userId}, N'{type}', '{severity}', {actorText}, DATEADD(second, -{secondsAgo}, SYSUTCDATETIME()), {readAt});
                    SELECT @id;
                    """));
        }

        /// <summary>The ids of a user's inbox rows, read or not, in id order.</summary>
        /// <param name="userId">The user.</param>
        /// <param name="read">True for the read rows, false for the unread, null for every row.</param>
        /// <returns>The ids.</returns>
        public async Task<IReadOnlyList<int>> IdsAsync(int userId, bool? read = null)
        {
            string filter = read switch
            {
                true => " AND [ReadAt] IS NOT NULL",
                false => " AND [ReadAt] IS NULL",
                null => string.Empty,
            };
            List<int> ids = [];
            await using SqlConnection connection = new(ConnectionString);
            await connection.OpenAsync(Token);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = string.Create(CultureInfo.InvariantCulture, $"SELECT [Id] FROM [core].[Notifications] WHERE [UserId] = {userId}{filter} ORDER BY [Id];");
            await using SqlDataReader reader = await command.ExecuteReaderAsync(Token);
            while (await reader.ReadAsync(Token))
            {
                ids.Add(reader.GetInt32(0));
            }

            return ids;
        }

        /// <summary>A user's subject.</summary>
        /// <param name="userId">The user.</param>
        /// <returns>The subject.</returns>
        private static string Subject(int userId)
        {
            return string.Create(CultureInfo.InvariantCulture, $"fx-inbox-{userId}");
        }

        /// <summary>The insert of one user who was invited and has joined, with their stamp row.</summary>
        /// <param name="id">The user's id.</param>
        /// <param name="name">The user's name.</param>
        /// <param name="active">Whether the user is active.</param>
        /// <param name="timeZone">The user's preferred time zone; null for the tenant's.</param>
        /// <returns>The statement.</returns>
        private static string UserInsert(int id, string name, bool active, string? timeZone = null)
        {
            string subject = Subject(id);
            string zone = timeZone is null ? "NULL" : $"N'{timeZone}'";
            int system = WellKnownIds.SystemUserId;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"""
                INSERT INTO [core].[Users] ([Id], [Kind], [Subject], [Email], [ContactMobile], [InvitedAt], [JoinedAt], [Name], [PreferredTimeZone], [IsActive], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
                VALUES ({id}, '{UserKind.Individual}', '{subject}', N'{subject}@example.com', N'+1555{id}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'{name}', {zone}, {(active ? 1 : 0)}, SYSDATETIMEOFFSET(), {system}, SYSDATETIMEOFFSET(), {system});
                INSERT INTO [core].[UserStamps] ([UserId]) VALUES ({id});
                """);
        }

        /// <summary>The one administrator the failure notices reach.</summary>
        private sealed class FixedAdministrators : IAdministratorDirectory
        {
            /// <inheritdoc />
            public Task<IReadOnlyList<int>> GetAdministratorIdsAsync(CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IReadOnlyList<int>>([AdministratorId]);
            }
        }

        /// <summary>The email sender: records each delivery.</summary>
        /// <param name="log">The deliveries.</param>
        private sealed class RecordingEmail(DeliveryLog log) : INotificationChannel
        {
            /// <inheritdoc />
            public string Key => CoreFeature.EmailChannel;

            /// <inheritdoc />
            public Task DeliverAsync(IOpenWriteHost host, NotificationRequest request, IReadOnlyList<NotificationRecipient> recipients, CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(host);
                log.Add(new Delivery(Key, host.Kind, request, recipients));
                return Task.CompletedTask;
            }

            /// <inheritdoc />
            public Task<TestNotificationResult> SendTestAsync(CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>The sms sender: records each delivery, or throws while the log says it fails.</summary>
        /// <param name="log">The deliveries.</param>
        private sealed class RecordingSms(DeliveryLog log) : INotificationChannel
        {
            /// <inheritdoc />
            public string Key => FixtureNotifications.SmsChannel;

            /// <inheritdoc />
            public Task DeliverAsync(IOpenWriteHost host, NotificationRequest request, IReadOnlyList<NotificationRecipient> recipients, CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(host);
                if (log.SmsFails)
                {
                    throw new InvalidOperationException("The sms transport is down.");
                }

                log.Add(new Delivery(Key, host.Kind, request, recipients));
                return Task.CompletedTask;
            }

            /// <inheritdoc />
            public Task<TestNotificationResult> SendTestAsync(CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }
        }
    }

    /// <summary>The inbox's database with read-committed snapshot off, where readers take shared locks.</summary>
    /// <param name="sql">The suite's server.</param>
    public sealed class LockingInboxDatabase(SqlServerFixture sql) : InboxDatabase(sql, readCommittedSnapshot: false);

    /// <summary>What the senders were given, and whether sms fails.</summary>
    public sealed class DeliveryLog
    {
        /// <summary>The deliveries, in order.</summary>
        private readonly ConcurrentQueue<Delivery> _deliveries = new();

        /// <summary>The deliveries so far, in order.</summary>
        public IReadOnlyList<Delivery> Delivered => [.. _deliveries];

        /// <summary>Whether the sms sender throws.</summary>
        public bool SmsFails { get; set; }

        /// <summary>Records a delivery.</summary>
        /// <param name="delivery">The delivery.</param>
        public void Add(Delivery delivery)
        {
            _deliveries.Enqueue(delivery);
        }

        /// <summary>Forgets every delivery, and lets sms deliver again.</summary>
        public void Clear()
        {
            _deliveries.Clear();
            SmsFails = false;
        }
    }

    /// <summary>One sender call.</summary>
    /// <param name="Channel">The channel's key.</param>
    /// <param name="Host">The kind of write host it ran on.</param>
    /// <param name="Request">The request it was given.</param>
    /// <param name="Recipients">The recipients it was given.</param>
    public sealed record Delivery(string Channel, WriteHostKind Host, NotificationRequest Request, IReadOnlyList<NotificationRecipient> Recipients);
}

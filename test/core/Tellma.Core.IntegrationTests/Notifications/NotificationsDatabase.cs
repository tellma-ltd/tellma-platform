// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Composition;
using Tellma.Core.Jobs;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Notifications
{
    /// <summary>
    ///     One fixture database for a notifier test class, created once with its users and reset
    ///     before each test. Its host delivers to email and the fixture's sms channel through senders
    ///     that only name them, slices deliveries two recipients at a time, records the client events
    ///     the notifier publishes, counts its wakes of the fixture tenant's pollers, and audits every
    ///     batch's writes against change tracking. A class's tests run one at a time, so the events,
    ///     the wakes and the audit see only the running test's batches.
    /// </summary>
    public class NotificationsDatabase : IAsyncLifetime
    {
        /// <summary>The recipients a delivery job carries at most, small so a test spans several.</summary>
        public const int SliceSize = 2;

        /// <summary>The first of the active users the database holds.</summary>
        public const int FirstUserId = 101;

        /// <summary>The active users the database holds, from <see cref="FirstUserId" /> on.</summary>
        public const int ActiveUsers = 6;

        /// <summary>An inactive user, past the active ones.</summary>
        public const int InactiveUserId = 151;

        /// <summary>A user id no row holds.</summary>
        public const int UnknownUserId = 199;

        /// <summary>The statements that return the database to its state after creation, the users kept.</summary>
        private const string ResetText = """
            DELETE FROM [core].[Notifications];
            DELETE FROM [core].[NotificationPreferences];
            DELETE FROM [core].[Jobs];
            """;

        /// <summary>The suite's server.</summary>
        private readonly SqlServerFixture _sql;

        /// <summary>The host, once created.</summary>
        private FixtureTenant? _tenant;

        /// <summary>The subscription that counts the host's wakes of the fixture tenant.</summary>
        private IDisposable? _subscription;

        /// <summary>The wakes since the last reset.</summary>
        private int _wakes;

        /// <summary>Creates the fixture over a database with read-committed snapshot on, as provisioning creates it.</summary>
        /// <param name="sql">The suite's server.</param>
        public NotificationsDatabase(SqlServerFixture sql)
            : this(sql, readCommittedSnapshot: true)
        {
        }

        /// <summary>Creates the fixture over a database with read-committed snapshot on or off.</summary>
        /// <param name="sql">The suite's server.</param>
        /// <param name="readCommittedSnapshot">Whether the database reads committed rows from row versions.</param>
        protected NotificationsDatabase(SqlServerFixture sql, bool readCommittedSnapshot)
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

        /// <summary>The client events the notifier published since the last reset.</summary>
        public RecordingPublisher Events { get; } = new();

        /// <summary>How many times the host woke the fixture tenant's pollers since the last reset.</summary>
        public int Wakes => Volatile.Read(ref _wakes);

        /// <summary>The ids of the active users, in order.</summary>
        public static int[] ActiveUserIds { get; } = [.. Enumerable.Range(FirstUserId, ActiveUsers)];

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            _tenant = await FixtureTenant.CreateAsync(
                _sql.MasterConnectionString,
                _sql.NewDatabaseName(ReadCommittedSnapshot ? "fx_notify" : "fx_notify_locking"),
                services =>
                {
                    // --- Senders for email and sms, so both deliver; the events are recorded ---
                    _ = services.AddScoped<INotificationChannel>(static _ => new NamingSender(CoreFeature.EmailChannel));
                    _ = services.AddScoped<INotificationChannel>(static _ => new NamingSender(FixtureNotifications.SmsChannel));
                    _ = services.AddScoped<IClientEventPublisher>(_ => Events);
                    _ = services.PostConfigure<NotificationsOptions>(static options => options.DeliverySliceSize = SliceSize);
                },
                readCommittedSnapshot: ReadCommittedSnapshot,
                cancellationToken: TestContext.Current.CancellationToken);

            // The wakes stand in for the poller a later host runs: each one counts.
            _subscription = _tenant.Services.GetRequiredService<TenantPollerWake>()
                .Subscribe(FixtureTenant.TenantId, () => Interlocked.Increment(ref _wakes));

            // --- The users: the active ones and an inactive one, created by the system user ---
            await SqlServerFixture.ExecuteAsync(
                ConnectionString,
                [.. ActiveUserIds.Select(static id => UserInsert(id, active: true)), UserInsert(InactiveUserId, active: false)]);
        }

        /// <summary>Deletes every row a test may have written, the users kept, and forgets the events and the wakes.</summary>
        /// <returns>A task that completes when the database is reset.</returns>
        public async Task ResetAsync()
        {
            await SqlServerFixture.ExecuteAsync(ConnectionString, ResetText);
            Events.Clear();
            _ = Interlocked.Exchange(ref _wakes, 0);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            _subscription?.Dispose();
            return _tenant?.DisposeAsync() ?? ValueTask.CompletedTask;
        }

        /// <summary>The insert of one user who was invited and has joined.</summary>
        /// <param name="id">The user's id.</param>
        /// <param name="active">Whether the user is active.</param>
        /// <returns>The statement.</returns>
        private static string UserInsert(int id, bool active)
        {
            string subject = string.Create(CultureInfo.InvariantCulture, $"fx-notify-{id}");
            return string.Create(
                CultureInfo.InvariantCulture,
                $"INSERT INTO [core].[Users] ([Id], [Kind], [Subject], [Email], [InvitedAt], [JoinedAt], [Name], [IsActive], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]) VALUES ({id}, '{UserKind.Individual}', '{subject}', N'{subject}@example.com', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'{subject}', {(active ? 1 : 0)}, SYSDATETIMEOFFSET(), {WellKnownIds.SystemUserId}, SYSDATETIMEOFFSET(), {WellKnownIds.SystemUserId});");
        }

        /// <summary>A sender that only names its channel; no test delivers through it.</summary>
        /// <param name="key">The channel's key.</param>
        private sealed class NamingSender(string key) : INotificationChannel
        {
            /// <inheritdoc />
            public string Key => key;

            /// <inheritdoc />
            public Task DeliverAsync(IOpenWriteHost host, NotificationRequest request, IReadOnlyList<NotificationRecipient> recipients, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc />
            public Task<TestNotificationResult> SendTestAsync(CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>A client event publisher that records every event.</summary>
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
    }

    /// <summary>
    ///     The notifier's database with read-committed snapshot off, where readers take shared locks,
    ///     so the notifier's statements are held to running under either setting.
    /// </summary>
    /// <param name="sql">The suite's server.</param>
    public sealed class LockingNotificationsDatabase(SqlServerFixture sql) : NotificationsDatabase(sql, readCommittedSnapshot: false);
}

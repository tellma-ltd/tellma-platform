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
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Crud;
using Tellma.Core.Data.Batches;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Jobs
{
    /// <summary>
    ///     One fixture database whose host runs jobs: the fixture composition with a feature of
    ///     scripted handlers, the tables its stacks own refused to a caller's raw writes as in
    ///     production, the tenant's administrators fixed, and the notices and client events recorded
    ///     once their batch committed. A raw table no stack owns takes the handlers' own statements.
    ///     Created once per test class and reset before each test; a class's tests run one at a time.
    /// </summary>
    public class JobRunDatabase : IAsyncLifetime
    {
        /// <summary>An active user the jobs run as and are requested by.</summary>
        public const int ActiveUserId = 101;

        /// <summary>The tenant's administrator, who receives the notices of jobs without a requester.</summary>
        public const int AdministratorId = 111;

        /// <summary>An active user a test deactivates while a job runs as them; active again after every reset.</summary>
        public const int DeactivatedUserId = 121;

        /// <summary>A user who is no longer an active member.</summary>
        public const int InactiveUserId = 151;

        /// <summary>The raw table the handlers' own statements write; no stack owns it.</summary>
        public const string RawTable = "[test].[RunLog]";

        /// <summary>The statements that return the database to its state after creation, the seeded rows kept.</summary>
        private const string ResetText = """
            DELETE FROM [test].[RunLog];
            DELETE FROM [fixture].[Shipments];
            DELETE FROM [core].[Jobs];
            UPDATE [core].[JobWorkerState] SET [LastTickAt] = NULL, [LastTickOwner] = NULL;
            """;

        /// <summary>The suite's server.</summary>
        private readonly SqlServerFixture _sql;

        /// <summary>The host, once created.</summary>
        private FixtureTenant? _tenant;

        /// <summary>Creates the database with read-committed snapshot on, as provisioning creates a tenant database.</summary>
        /// <param name="sql">The suite's server.</param>
        public JobRunDatabase(SqlServerFixture sql)
            : this(sql, readCommittedSnapshot: true)
        {
        }

        /// <summary>Creates the database with read-committed snapshot as given.</summary>
        /// <param name="sql">The suite's server.</param>
        /// <param name="readCommittedSnapshot">Whether the database reads committed rows from row versions.</param>
        protected JobRunDatabase(SqlServerFixture sql, bool readCommittedSnapshot)
        {
            ArgumentNullException.ThrowIfNull(sql);
            _sql = sql;
            ReadCommittedSnapshot = readCommittedSnapshot;
        }

        /// <summary>Whether the database reads committed rows from row versions.</summary>
        public bool ReadCommittedSnapshot { get; }

        /// <summary>The fixture host.</summary>
        public FixtureTenant Tenant => _tenant ?? throw new InvalidOperationException("The database is not created yet.");

        /// <summary>The database's connection string.</summary>
        public string ConnectionString => Tenant.ConnectionString;

        /// <summary>What the handlers do, set by each test.</summary>
        public JobRunScript Script { get; } = new();

        /// <summary>The client events published, each once its batch committed.</summary>
        public CommittedEvents Events { get; } = new();

        /// <summary>The notifications raised, each once its batch committed.</summary>
        public CommittedNotices Notices { get; } = new();

        /// <summary>The host's log.</summary>
        public FakeLogCollector Log => Tenant.Services.GetFakeLogCollector();

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            _tenant = await FixtureTenant.CreateAsync(
                _sql.MasterConnectionString,
                _sql.NewDatabaseName(ReadCommittedSnapshot ? "fx_jobrun" : "fx_jobrun_locking"),
                services =>
                {
                    // --- The handlers' script, the recorded notices and events, the fixed administrators and the log ---
                    _ = services.AddSingleton(Script);
                    _ = services.Replace(ServiceDescriptor.Scoped<INotifier>(_ => Notices));
                    _ = services.AddScoped<IClientEventPublisher>(_ => Events);
                    _ = services.Replace(ServiceDescriptor.Scoped<IAdministratorDirectory>(static _ => new FixedAdministrators()));
                    _ = services.AddFakeLogging();

                    // Each record carries the trace of the activity it was logged under, as an exporter reads it.
                    _ = services.Configure<LoggerFactoryOptions>(static options => options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId);

                    // --- The stacks' tables are theirs again, as in production: a caller's raw write to one is refused ---
                    _ = services.Replace(ServiceDescriptor.Singleton<IStackTableOwners>(static provider => new StackTableOwners(provider.GetRequiredService<StackRegistry>())));
                },
                readCommittedSnapshot: ReadCommittedSnapshot,
                compose: static tellma => tellma.AddFeature(new JobRunFeature()),
                cancellationToken: TestContext.Current.CancellationToken);

            // --- The tenant's settings and version tags, the users, and the raw table ---
            string tags = string.Join(", ", Tenant.Services.GetRequiredService<IVersionTagRegistry>().Names.Select(static name => $"(N'{name}')"));
            await SqlServerFixture.ExecuteAsync(
                ConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Settings] SET [TenantId] = {FixtureTenant.TenantId}, [Name] = N'Fixture';"),
                $"INSERT INTO [core].[VersionTags] ([Name], [Tag]) SELECT n.[Name], NEWID() FROM (VALUES {tags}) AS n ([Name]) WHERE NOT EXISTS (SELECT 1 FROM [core].[VersionTags] AS t WHERE t.[Name] = n.[Name]);",
                UserInsert(ActiveUserId, active: true),
                UserInsert(AdministratorId, active: true),
                UserInsert(DeactivatedUserId, active: true),
                UserInsert(InactiveUserId, active: false),
                "CREATE SCHEMA [test];",
                $"CREATE TABLE {RawTable} ([Id] int IDENTITY PRIMARY KEY, [JobId] int NOT NULL, [Note] nvarchar(100) NOT NULL);");
        }

        /// <summary>Deletes every row a test may have written, reactivates the user a test deactivates, and clears the script, the notices, the events and the log.</summary>
        /// <returns>A task that completes when the database is reset.</returns>
        public async Task ResetAsync()
        {
            await SqlServerFixture.ExecuteAsync(
                ConnectionString,
                ResetText,
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Users] SET [IsActive] = 1 WHERE [Id] = {DeactivatedUserId};"));
            Script.Reset();
            Notices.Clear();
            Events.Clear();
            Log.Clear();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            return _tenant?.DisposeAsync() ?? ValueTask.CompletedTask;
        }

        /// <summary>Stores a job not leased, as an enqueue or a completion leaves one, due five seconds ago unless told otherwise.</summary>
        /// <param name="key">The handler key.</param>
        /// <param name="runAs">The run-as user.</param>
        /// <param name="requestedBy">The requester; null for none.</param>
        /// <param name="attempts">The attempts already made.</param>
        /// <param name="stateJson">The checkpoint; null for none.</param>
        /// <param name="confirmedKeys">The confirmed warning keys as stored; null for none.</param>
        /// <param name="cancelRequested">Whether a cancel was requested.</param>
        /// <param name="errorCode">The error an earlier attempt recorded; null for none.</param>
        /// <param name="dueSecondsAgo">How long ago the job became due; negative for a job due later.</param>
        /// <returns>The job's id.</returns>
        public async Task<int> InsertJobAsync(
            string key,
            int runAs = ActiveUserId,
            int? requestedBy = ActiveUserId,
            int attempts = 0,
            string? stateJson = null,
            string? confirmedKeys = null,
            bool cancelRequested = false,
            string? errorCode = null,
            int dueSecondsAgo = 5)
        {
            string requester = requestedBy?.ToString(CultureInfo.InvariantCulture) ?? "NULL";
            string cancel = cancelRequested ? "SYSUTCDATETIME()" : "NULL";
            return await SqlServerFixture.ScalarAsync<int>(
                ConnectionString,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"""
                    DECLARE @id int = NEXT VALUE FOR [core].[sq_Jobs];
                    INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [Attempts], [StateJson], [ConfirmedKeys], [CancelRequestedAt], [ErrorCode], [RunAsUserId], [RequestedById], [CreatedAt])
                    VALUES (@id, N'{key}', 'Pending', DATEADD(second, -({dueSecondsAgo}), SYSUTCDATETIME()), {attempts}, {Literal(stateJson)}, {Literal(confirmedKeys)}, {cancel}, {Literal(errorCode)}, {runAs}, {requester}, SYSUTCDATETIME());
                    SELECT @id;
                    """));
        }

        /// <summary>Stores a pending shipment that owns a job.</summary>
        /// <param name="id">The shipment's id.</param>
        /// <param name="jobId">The job it owns.</param>
        /// <param name="payload">Its payload.</param>
        /// <returns>A task that completes when the shipment is stored.</returns>
        public Task InsertShipmentAsync(long id, int jobId, string payload)
        {
            return SqlServerFixture.ExecuteAsync(
                ConnectionString,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"INSERT INTO [fixture].[Shipments] ([Id], [State], [Payload], [JobId], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]) VALUES ({id}, 'Pending', {Literal(payload)}, {jobId}, SYSDATETIMEOFFSET(), 1, SYSDATETIMEOFFSET(), 1);"));
        }

        /// <summary>Reads a job's row, every column by name.</summary>
        /// <param name="id">The job.</param>
        /// <returns>The row.</returns>
        public async Task<JobRow> JobAsync(int id)
        {
            await using SqlConnection connection = new(ConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM [core].[Jobs] WHERE [Id] = @id;";
            _ = command.Parameters.AddWithValue("@id", id);
            await using SqlDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken), $"The job {id} is stored.");
            Dictionary<string, object?> row = new(StringComparer.Ordinal);
            for (int column = 0; column < reader.FieldCount; column++)
            {
                row[reader.GetName(column)] = reader.IsDBNull(column) ? null : reader.GetValue(column);
            }

            return new JobRow(row);
        }

        /// <summary>The notes the handlers' own statements wrote, in order.</summary>
        /// <returns>The notes.</returns>
        public async Task<IReadOnlyList<string>> RunLogAsync()
        {
            await using SqlConnection connection = new(ConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT [Note] FROM {RawTable} ORDER BY [Id];";
            await using SqlDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            List<string> notes = [];
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                notes.Add(reader.GetString(0));
            }

            return notes;
        }

        /// <summary>A string as a SQL literal.</summary>
        /// <param name="value">The string; null for <c>NULL</c>.</param>
        /// <returns>The literal.</returns>
        public static string Literal(string? value)
        {
            return value is null ? "NULL" : $"N'{value.Replace("'", "''", StringComparison.Ordinal)}'";
        }

        /// <summary>The statement that inserts a user with a stamp row, as the user service would.</summary>
        /// <param name="id">The user's id.</param>
        /// <param name="active">Whether the user is an active member.</param>
        /// <returns>The statement.</returns>
        private static string UserInsert(int id, bool active)
        {
            string subject = string.Create(CultureInfo.InvariantCulture, $"fx-jobrun-{id}");
            int system = WellKnownIds.SystemUserId;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"""
                INSERT INTO [core].[Users] ([Id], [Kind], [Subject], [Email], [InvitedAt], [JoinedAt], [Name], [IsActive], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
                VALUES ({id}, '{UserKind.Individual}', '{subject}', N'{subject}@example.com', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'{subject}', {(active ? 1 : 0)}, SYSDATETIMEOFFSET(), {system}, SYSDATETIMEOFFSET(), {system});
                INSERT INTO [core].[UserStamps] ([UserId]) VALUES ({id});
                """);
        }

        /// <summary>An administrator directory that names the fixture's administrator.</summary>
        private sealed class FixedAdministrators : IAdministratorDirectory
        {
            /// <inheritdoc />
            public Task<IReadOnlyList<int>> GetAdministratorIdsAsync(CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IReadOnlyList<int>>([AdministratorId]);
            }
        }
    }

    /// <summary>The job-running database with read-committed snapshot off, where readers take shared locks.</summary>
    /// <param name="sql">The suite's server.</param>
    public sealed class LockingJobRunDatabase(SqlServerFixture sql) : JobRunDatabase(sql, readCommittedSnapshot: false);

    /// <summary>A job's stored row, every column by name.</summary>
    /// <param name="Columns">The columns.</param>
    public sealed record JobRow(IReadOnlyDictionary<string, object?> Columns)
    {
        /// <summary>The status.</summary>
        public string Status => (string)Columns["Status"]!;

        /// <summary>The attempts made.</summary>
        public int Attempts => (int)Columns["Attempts"]!;

        /// <summary>The error code; null for none.</summary>
        public string? ErrorCode => (string?)Columns["ErrorCode"];

        /// <summary>The error's arguments; null for none.</summary>
        public string? ErrorArgumentsJson => (string?)Columns["ErrorArgumentsJson"];

        /// <summary>The trace id the error recorded; null for none.</summary>
        public string? ErrorTraceId => (string?)Columns["ErrorTraceId"];

        /// <summary>The checkpoint; null for none.</summary>
        public string? StateJson => (string?)Columns["StateJson"];

        /// <summary>The warnings the job waits on; null for none.</summary>
        public string? PendingConfirmationsJson => (string?)Columns["PendingConfirmationsJson"];

        /// <summary>The lease token; null when not leased.</summary>
        public Guid? LeaseToken => (Guid?)Columns["LeaseToken"];

        /// <summary>The progress percent; null for none.</summary>
        public byte? ProgressPercent => (byte?)Columns["ProgressPercent"];

        /// <summary>The progress message's key; null for none.</summary>
        public string? ProgressKey => (string?)Columns["ProgressKey"];

        /// <summary>The confirmed warning keys; null for none.</summary>
        public string? ConfirmedKeys => (string?)Columns["ConfirmedKeys"];

        /// <summary>When the job ended; null while it has not.</summary>
        public DateTimeOffset? CompletedAt => (DateTimeOffset?)Columns["CompletedAt"];

        /// <summary>When the job is next due.</summary>
        public DateTimeOffset DueAt => (DateTimeOffset)Columns["DueAt"]!;
    }

    /// <summary>A client event publisher that records an event once its batch committed, and at once without a batch.</summary>
    public sealed class CommittedEvents : IClientEventPublisher
    {
        /// <summary>The events, in the order recorded.</summary>
        private readonly ConcurrentQueue<ClientEvent> _events = new();

        /// <summary>The events recorded since the last clear.</summary>
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
            if (batch is null)
            {
                _events.Enqueue(clientEvent);
                return;
            }

            batch.OnCommitted(_ => _events.Enqueue(clientEvent));
        }

        /// <inheritdoc />
        public Task PublishAsync(ClientEvent clientEvent)
        {
            Publish(null, clientEvent);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     A notifier that records each request once its batch committed and each withdrawal sent on
    ///     its own, and delivers nothing; a test may have it refuse every request, as a notifier whose
    ///     statement cannot be composed would.
    /// </summary>
    public sealed class CommittedNotices : INotifier
    {
        /// <summary>The requests, in the order recorded.</summary>
        private readonly ConcurrentQueue<NotificationRequest> _requests = new();

        /// <summary>The withdrawals, in the order recorded.</summary>
        private readonly ConcurrentQueue<WithdrawRequest> _withdrawals = new();

        /// <summary>The requests recorded since the last clear.</summary>
        public IReadOnlyList<NotificationRequest> Raised => [.. _requests];

        /// <summary>The withdrawals sent on their own since the last clear.</summary>
        public IReadOnlyList<WithdrawRequest> Withdrawn => [.. _withdrawals];

        /// <summary>Whether every request appended to a batch is refused with an exception; off again after each clear.</summary>
        public bool Refuses { get; set; }

        /// <summary>Forgets every request, and accepts requests again.</summary>
        public void Clear()
        {
            _requests.Clear();
            _withdrawals.Clear();
            Refuses = false;
        }

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException">The test set the notifier to refuse every request.</exception>
        public void Notify(IDataBatch batch, IReadOnlyList<NotificationRequest> requests)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(requests);
            if (Refuses)
            {
                throw new InvalidOperationException("The test notifier refuses every request.");
            }

            NotificationRequest[] raised = [.. requests];
            batch.OnCommitted(_ =>
            {
                foreach (NotificationRequest request in raised)
                {
                    _requests.Enqueue(request);
                }
            });
        }

        /// <inheritdoc />
        public Task NotifyAsync(IReadOnlyList<NotificationRequest> requests, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(requests);
            foreach (NotificationRequest request in requests)
            {
                _requests.Enqueue(request);
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void Withdraw(IDataBatch batch, IReadOnlyList<WithdrawRequest> requests)
        {
            throw new NotSupportedException("No job completion withdraws a notification.");
        }

        /// <inheritdoc />
        public Task WithdrawAsync(IReadOnlyList<WithdrawRequest> requests, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(requests);
            foreach (WithdrawRequest request in requests)
            {
                _withdrawals.Enqueue(request);
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>The keys of the scripted handlers and of the warnings they raise.</summary>
    public static class JobRunKeys
    {
        /// <summary>The arguments-only handler: three attempts, a fixed one-minute backoff.</summary>
        public const string Probe = "test.probe";

        /// <summary>The handler over shipments, which enlists their writes.</summary>
        public const string Ship = "test.ship";

        /// <summary>The handler over shipments that commits each step itself.</summary>
        public const string Steps = "test.steps";

        /// <summary>The handler of one attempt whose failure notices stay in the inbox.</summary>
        public const string Inbox = "test.inbox";

        /// <summary>A key the feature retired: its jobs are cancelled by the hourly sweep.</summary>
        public const string Retired = "test.retired";

        /// <summary>A warning the handlers raise, which a job's initiator may confirm.</summary>
        public const string Backdated = "Test.Backdated";

        /// <summary>A second warning.</summary>
        public const string Negative = "Test.Negative";

        /// <summary>How many numbered warnings the feature declares beside the two named ones.</summary>
        public const int NumberedWarnings = 17;

        /// <summary>The numbered warning keys, <c>Test.Warning0</c> on.</summary>
        public static IReadOnlyList<string> Numbered { get; } = [.. Enumerable.Range(0, NumberedWarnings).Select(static index => string.Create(CultureInfo.InvariantCulture, $"Test.Warning{index}"))];
    }

    /// <summary>What a scripted handler sees: the scope's services and the job's write host.</summary>
    /// <param name="Services">The job scope's services.</param>
    /// <param name="Host">The job's write host, for enlisted writes, their persist and the confirmations they raise.</param>
    public sealed record JobRunContext(IServiceProvider Services, IOpenWriteHost Host);

    /// <summary>What each scripted handler does; nothing until a test sets it.</summary>
    public sealed class JobRunScript
    {
        /// <summary>What <see cref="JobRunKeys.Probe" /> does.</summary>
        public Func<JobBatch<Job>, JobRunContext, Task>? Probe { get; set; }

        /// <summary>What <see cref="JobRunKeys.Ship" /> does, given the handler, which enlists.</summary>
        public Func<JobBatch<Shipment>, JobRunContext, IEnlists<Shipment>, Task>? Ship { get; set; }

        /// <summary>What <see cref="JobRunKeys.Steps" /> does, given the handler, which enlists.</summary>
        public Func<JobBatch<Shipment>, JobRunContext, IEnlists<Shipment>, Task>? Steps { get; set; }

        /// <summary>What <see cref="JobRunKeys.Inbox" /> does.</summary>
        public Func<JobBatch<Job>, JobRunContext, Task>? Inbox { get; set; }

        /// <summary>The batches the handlers were invoked with, by key, in order.</summary>
        public ConcurrentQueue<(string Key, int[] JobIds)> Invocations { get; } = new();

        /// <summary>Forgets every script and invocation.</summary>
        public void Reset()
        {
            Probe = null;
            Ship = null;
            Steps = null;
            Inbox = null;
            Invocations.Clear();
        }

        /// <summary>Records an invocation.</summary>
        /// <typeparam name="TItem">The items' type.</typeparam>
        /// <param name="key">The handler's key.</param>
        /// <param name="batch">The batch.</param>
        internal void Invoked<TItem>(string key, JobBatch<TItem> batch)
            where TItem : class
        {
            Invocations.Enqueue((key, [.. batch.Items.Select(static item => item.Job.Id)]));
        }
    }

    /// <summary>The feature of the scripted handlers and the warnings they raise.</summary>
    internal sealed class JobRunFeature : ITellmaFeature
    {
        /// <inheritdoc />
        public string Name => "test";

        /// <inheritdoc />
        public void Declare(FeatureDeclaration declaration)
        {
        }

        /// <inheritdoc />
        public void Contribute(FeatureContribution contribution)
        {
            ArgumentNullException.ThrowIfNull(contribution);
            _ = contribution
                .JobHandler<RunProbeHandler>()
                .JobHandler<RunShipHandler>()
                .JobHandler<RunStepsHandler>()
                .JobHandler<RunInboxHandler>()
                .RetiredJobKey(JobRunKeys.Retired)
                .Confirmation(JobRunKeys.Backdated)
                .Confirmation(JobRunKeys.Negative);
            foreach (string key in JobRunKeys.Numbered)
            {
                _ = contribution.Confirmation(key);
            }
        }
    }

    /// <summary>The scripted arguments-only handler.</summary>
    /// <param name="script">The script.</param>
    /// <param name="services">The job scope's services.</param>
    /// <param name="host">The job's write host.</param>
    [JobHandler(JobRunKeys.Probe, BatchSize = 10, MaxAttempts = 3, RetryBaseSeconds = 60, RetryMaxSeconds = 60)]
    public sealed class RunProbeHandler(JobRunScript script, IServiceProvider services, IOpenWriteHost host) : IJobHandler
    {
        /// <inheritdoc />
        public Task ExecuteAsync(JobBatch<Job> batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
            script.Invoked(JobRunKeys.Probe, batch);
            return script.Probe?.Invoke(batch, new JobRunContext(services, host)) ?? Task.CompletedTask;
        }
    }

    /// <summary>The scripted handler over shipments, which enlists their writes.</summary>
    /// <param name="script">The script.</param>
    /// <param name="services">The job scope's services.</param>
    /// <param name="host">The job's write host.</param>
    [JobHandler(JobRunKeys.Ship, BatchSize = 10)]
    public sealed class RunShipHandler(JobRunScript script, IServiceProvider services, IOpenWriteHost host) : IEntityJobHandler<Shipment>, IEnlists<Shipment>
    {
        /// <inheritdoc />
        public Task ExecuteAsync(JobBatch<Shipment> batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
            script.Invoked(JobRunKeys.Ship, batch);
            return script.Ship?.Invoke(batch, new JobRunContext(services, host), this) ?? Task.CompletedTask;
        }
    }

    /// <summary>The scripted handler over shipments that commits each step itself.</summary>
    /// <param name="script">The script.</param>
    /// <param name="services">The job scope's services.</param>
    /// <param name="host">The job's write host.</param>
    [JobHandler(JobRunKeys.Steps, BatchSize = 10, CommitsInSteps = true)]
    public sealed class RunStepsHandler(JobRunScript script, IServiceProvider services, IOpenWriteHost host) : IEntityJobHandler<Shipment>, IEnlists<Shipment>
    {
        /// <inheritdoc />
        public Task ExecuteAsync(JobBatch<Shipment> batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
            script.Invoked(JobRunKeys.Steps, batch);
            return script.Steps?.Invoke(batch, new JobRunContext(services, host), this) ?? Task.CompletedTask;
        }
    }

    /// <summary>The scripted handler of one attempt whose failure notices stay in the inbox.</summary>
    /// <param name="script">The script.</param>
    /// <param name="services">The job scope's services.</param>
    /// <param name="host">The job's write host.</param>
    [JobHandler(JobRunKeys.Inbox, BatchSize = 10, MaxAttempts = 1, InboxOnlyFailureNotices = true)]
    public sealed class RunInboxHandler(JobRunScript script, IServiceProvider services, IOpenWriteHost host) : IJobHandler
    {
        /// <inheritdoc />
        public Task ExecuteAsync(JobBatch<Job> batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
            script.Invoked(JobRunKeys.Inbox, batch);
            return script.Inbox?.Invoke(batch, new JobRunContext(services, host)) ?? Task.CompletedTask;
        }
    }
}

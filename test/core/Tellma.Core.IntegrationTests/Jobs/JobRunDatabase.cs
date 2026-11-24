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
using System.Data;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Crud;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Jobs;
using Tellma.Core.Queryex;
using Tellma.Core.Tenancy;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Jobs
{
    /// <summary>
    ///     One fixture database whose host runs jobs: the fixture composition with a feature of
    ///     scripted handlers, the tables its stacks own refused to a caller's raw writes as in
    ///     production, the tenant's administrators fixed, its users granted the schedules (one of them
    ///     only the schedule a filter lets it read), its administrator granted every job action, its active user the shipments, and the notices, withdrawals and client events
    ///     recorded once their batch committed. A raw table no stack owns takes the handlers' own statements.
    ///     The change-tracking audit checks every batch but while a test suspends it to run batches
    ///     side by side. Created once per test class and reset before each test; a class's tests run
    ///     one at a time.
    /// </summary>
    public class JobRunDatabase : IAsyncLifetime
    {
        /// <summary>An active user the jobs run as and are requested by.</summary>
        public const int ActiveUserId = 101;

        /// <summary>The tenant's administrator, who receives the notices of jobs without a requester.</summary>
        public const int AdministratorId = 111;

        /// <summary>An active user a test deactivates while a job runs as them; active again after every reset.</summary>
        public const int DeactivatedUserId = 121;

        /// <summary>An active user who reads only the schedule coded <see cref="ViewerScheduleCode" />.</summary>
        public const int ViewerUserId = 131;

        /// <summary>A user who is no longer an active member.</summary>
        public const int InactiveUserId = 151;

        /// <summary>The code of the one schedule <see cref="ViewerUserId" /> may read.</summary>
        public const string ViewerScheduleCode = "seen";

        /// <summary>The resource of the fixture's shipments, which the active user reads.</summary>
        public const string ShipmentResource = "fixture.Shipment";

        /// <summary>The raw table the handlers' own statements write; no stack owns it.</summary>
        public const string RawTable = "[test].[RunLog]";

        /// <summary>The fixture's built-in schedule's expression, as the migrations seed it.</summary>
        public const string BuiltInCron = "0 1 * * *";

        /// <summary>The tenant's zone, as the migrations seed it and each reset restores it.</summary>
        public const string TenantZone = "UTC";

        /// <summary>
        ///     The statements that return the database to its state after creation, the seeded rows kept:
        ///     the user schedules go with their states, and the fixture's built-in schedule is active, as
        ///     seeded, and not yet initialised, so a poll only ever fires it once a test makes it due.
        ///     The platform's own built-in schedules are kept stopped, so a tick leases and fires the
        ///     fixture's schedules alone. The tenant's zone is UTC, as seeded; a test that moved it has
        ///     the settings tag moved again, so the next request reads the seeded zone.
        /// </summary>
        private static readonly string ResetText = string.Create(CultureInfo.InvariantCulture, $"""
            DELETE FROM [test].[RunLog];
            DELETE FROM [fixture].[Shipments];
            DELETE FROM [fixture].[Nodes];
            DELETE FROM [core].[Jobs];
            DELETE FROM [core].[Schedules] WHERE [Id] > {WellKnownIds.ReservedIdBandEnd};
            UPDATE [core].[ScheduleStates] SET [NextDueAt] = NULL, [LastFiredAt] = NULL, [LastScheduledFor] = NULL, [LastSkippedAt] = NULL, [LeaseToken] = NULL, [LeaseExpiresAt] = NULL;
            UPDATE [core].[Schedules] SET [IsActive] = 1, [PausedReason] = NULL, [CronExpression] = N'{BuiltInCron}', [TimeZoneId] = NULL
            WHERE [Id] = {FixtureFeature.ProbeScheduleId} AND ([IsActive] = 0 OR [PausedReason] IS NOT NULL OR [CronExpression] <> N'{BuiltInCron}' OR [TimeZoneId] IS NOT NULL);
            UPDATE [core].[Schedules] SET [IsActive] = 0
            WHERE [Id] <> {FixtureFeature.ProbeScheduleId} AND [Id] <= {WellKnownIds.ReservedIdBandEnd} AND [IsActive] = 1;
            UPDATE [core].[JobWorkerState] SET [LastTickAt] = NULL, [LastTickOwner] = NULL;
            UPDATE [core].[Settings] SET [TimeZone] = N'{TenantZone}' WHERE [TimeZone] <> N'{TenantZone}';
            IF @@ROWCOUNT > 0 UPDATE [core].[VersionTags] SET [Tag] = NEWID() WHERE [Name] = N'{VersionTagNames.Settings}';
            """);

        /// <summary>The suite's server.</summary>
        private readonly SqlServerFixture _sql;

        /// <summary>The host, once created.</summary>
        private FixtureTenant? _tenant;

        /// <summary>The subscription that counts the wakes of the fixture tenant's pollers.</summary>
        private IDisposable? _wakeSubscription;

        /// <summary>The wakes since the last reset.</summary>
        private int _wakes;

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

        /// <summary>How many times the host woke the fixture tenant's pollers since the last reset.</summary>
        public int Wakes => Volatile.Read(ref _wakes);

        /// <summary>The host's log.</summary>
        public FakeLogCollector Log => Tenant.Services.GetFakeLogCollector();

        /// <summary>The audit of every batch's writes, which a test suspends while batches run side by side.</summary>
        internal SuspendableAudit Audit { get; } = new();

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
                    _ = services.AddScoped(Events.PublisherFor);
                    _ = services.Replace(ServiceDescriptor.Scoped<IAdministratorDirectory>(static _ => new FixedAdministrators()));
                    _ = services.AddFakeLogging();

                    // The jobs' debug records too, so a test sees each progress report a handler made.
                    _ = services.AddLogging(static builder => builder.AddFilter(JobsLog.Category, LogLevel.Debug));

                    // Each record carries the trace of the activity it was logged under, as an exporter reads it.
                    _ = services.Configure<LoggerFactoryOptions>(static options => options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId);

                    // --- The stacks' tables are theirs again, as in production: a caller's raw write to one is refused ---
                    _ = services.Replace(ServiceDescriptor.Singleton<IStackTableOwners>(static provider => new StackTableOwners(provider.GetRequiredService<StackRegistry>())));

                    // --- The fixture's audit, behind a switch a test turns off while batches overlap ---
                    Audit.Inner = services.LastOrDefault(static service => service.ServiceType == typeof(IDataBatchAudit))?.ImplementationInstance as IDataBatchAudit;
                    _ = services.Replace(ServiceDescriptor.Singleton<IDataBatchAudit>(Audit));
                },
                readCommittedSnapshot: ReadCommittedSnapshot,
                compose: static tellma => tellma.AddFeature(new JobRunFeature()),
                cancellationToken: TestContext.Current.CancellationToken);
            Audit.ConnectionString = ConnectionString;

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
                UserInsert(ViewerUserId, active: true),
                RoleInsert("Schedulers", [ActiveUserId, AdministratorId, DeactivatedUserId], [AccessActions.Read, AccessActions.Save, AccessActions.Delete, AccessActions.Activate], filter: null),
                RoleInsert("Viewers", [ViewerUserId], [AccessActions.Read], filter: $"Code = '{ViewerScheduleCode}'"),
                RoleInsert("Job administrators", [AdministratorId], [AccessActions.Read, AccessActions.Retry, AccessActions.Cancel, AccessActions.Resume, JobService.Confirm], filter: null, CoreResources.Job),
                RoleInsert("Shipment readers", [ActiveUserId], [AccessActions.Read], filter: null, ShipmentResource),
                "CREATE SCHEMA [test];",
                $"CREATE TABLE {RawTable} ([Id] int IDENTITY PRIMARY KEY, [JobId] int NOT NULL, [Note] nvarchar(100) NOT NULL);");

            // --- The wakes stand in for the poller a running host has: each one counts ---
            _wakeSubscription = Tenant.Services.GetRequiredService<TenantPollerWake>()
                .Subscribe(FixtureTenant.TenantId, () => Interlocked.Increment(ref _wakes));
        }

        /// <summary>Deletes every row a test may have written, reactivates the user a test deactivates, resumes the audit, and clears the script, the notices, the events, the wakes and the log.</summary>
        /// <returns>A task that completes when the database is reset.</returns>
        public async Task ResetAsync()
        {
            Audit.Suspended = false;
            Audit.Reset();
            await SqlServerFixture.ExecuteAsync(
                ConnectionString,
                ResetText,
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Users] SET [IsActive] = 1 WHERE [Id] = {DeactivatedUserId};"));
            Script.Reset();
            Notices.Clear();
            Events.Clear();
            _ = Interlocked.Exchange(ref _wakes, 0);
            Log.Clear();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            _wakeSubscription?.Dispose();
            return _tenant?.DisposeAsync() ?? ValueTask.CompletedTask;
        }

        /// <summary>
        ///     A request scope of the fixture tenant for one of its users, whose session stepped up a
        ///     moment ago, completed by the initializers as a request's is.
        /// </summary>
        /// <param name="userId">The user, one of the fixture's.</param>
        /// <param name="state">The tenant's state the request sees; the registered one when null.</param>
        /// <returns>The scope; the caller disposes it.</returns>
        public async Task<AsyncServiceScope> UserScopeAsync(int userId, TenantState? state = null)
        {
            AsyncServiceScope scope = Tenant.Services.CreateAsyncScope();
            try
            {
                // --- The request as the tenant middleware binds it: the tenant, the caller's subject and a fresh step-up ---
                DateTimeOffset now = DateTimeOffset.UtcNow;
                IRequestContextHolder holder = scope.ServiceProvider.GetRequiredService<IRequestContextHolder>();
                TenantDescriptor tenant = Tenant.Services.GetRequiredService<ITenantRegistry>().Get(FixtureTenant.TenantId).Descriptor;
                RequestContext context = holder.Current with
                {
                    Tenant = tenant with { State = state ?? tenant.State },
                    Kind = PrincipalKind.User,
                    Subject = Subject(userId),
                    UserId = null,
                    Assurance = new AuthenticationAssurance(AssuranceTiers.Aal2, now, now),
                };
                holder.Set(context);

                // --- Then the initializers: the connection, which binds the user, and the locale ---
                _ = await RequestContextInitialization.RunAsync(
                    holder,
                    scope.ServiceProvider.GetServices<IRequestContextInitializer>(),
                    context,
                    new RequestContextInputs(null, null, null, null, null, null, SuppressActivityStamp: true),
                    TestContext.Current.CancellationToken);
                return scope;
            }
            catch
            {
                await scope.DisposeAsync();
                throw;
            }
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

        /// <summary>Sets columns of a stored job, as the machinery would have left them.</summary>
        /// <param name="id">The job.</param>
        /// <param name="assignments">The SET list, such as <c>[Status] = 'Held'</c>.</param>
        /// <returns>A task that completes when the job is updated.</returns>
        public Task SetJobAsync(int id, string assignments)
        {
            return SqlServerFixture.ExecuteAsync(
                ConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Jobs] SET {assignments} WHERE [Id] = {id};"));
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

        // The lease statements read the clock as datetimeoffset(3), which rounds the datetime2(7) value
        // SYSUTCDATETIME() returns to the nearest millisecond, possibly up; and SQL Server on Linux
        // advances that clock in steps of a few milliseconds, so two statements a moment apart often
        // read the same value. A lease end measured against the raw clock, or with DATEDIFF counting
        // millisecond boundaries, can therefore lie a millisecond past the length the statement added.
        // Reading the clock rounded the same way, in the same statement as the end, keeps the
        // comparison exact: the clock never runs backwards and the rounding is monotonic, so a lease
        // a statement set n seconds from its own clock ends at most n seconds after this one.
        /// <summary>
        ///     Reads a job's lease end with the database's clock, read in the same statement and rounded
        ///     to the millisecond as the lease statements round it.
        /// </summary>
        /// <param name="id">The job.</param>
        /// <returns>The lease end, null when the job holds no lease, and the clock.</returns>
        public async Task<(DateTimeOffset? ExpiresAt, DateTimeOffset Now)> LeaseAsync(int id)
        {
            await using SqlConnection connection = new(ConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT [LeaseExpiresAt], CAST(SYSUTCDATETIME() AS datetimeoffset(3)) FROM [core].[Jobs] WHERE [Id] = @id;";
            _ = command.Parameters.AddWithValue("@id", id);
            await using SqlDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken), $"The job {id} is stored.");
            return (reader.IsDBNull(0) ? null : reader.GetDateTimeOffset(0), reader.GetDateTimeOffset(1));
        }

        /// <summary>Waits until the database's clock, rounded to the millisecond, has passed an instant it read.</summary>
        /// <param name="instant">The instant, read from the database's clock rounded to the millisecond.</param>
        /// <returns>A task that completes once a statement started now reads a later clock.</returns>
        public async Task UntilTheClockPassesAsync(DateTimeOffset instant)
        {
            // --- A few clock steps at most: each read is its own round trip ---
            while (await SqlServerFixture.ScalarAsync<DateTimeOffset>(ConnectionString, "SELECT CAST(SYSUTCDATETIME() AS datetimeoffset(3));") <= instant)
            {
                TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            }
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
            string subject = Subject(id);
            int system = WellKnownIds.SystemUserId;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"""
                INSERT INTO [core].[Users] ([Id], [Kind], [Subject], [Email], [InvitedAt], [JoinedAt], [Name], [IsActive], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
                VALUES ({id}, '{UserKind.Individual}', '{subject}', N'{subject}@example.com', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'{subject}', {(active ? 1 : 0)}, SYSDATETIMEOFFSET(), {system}, SYSDATETIMEOFFSET(), {system});
                INSERT INTO [core].[UserStamps] ([UserId]) VALUES ({id});
                """);
        }

        /// <summary>A fixture user's subject.</summary>
        /// <param name="id">The user's id.</param>
        /// <returns>The subject.</returns>
        private static string Subject(int id)
        {
            return string.Create(CultureInfo.InvariantCulture, $"fx-jobrun-{id}");
        }

        /// <summary>The statement that inserts a role over one resource, the schedules unless told otherwise, its permissions and its members.</summary>
        /// <param name="name">The role's name.</param>
        /// <param name="members">The users who are its members.</param>
        /// <param name="actions">The actions it grants on the resource.</param>
        /// <param name="filter">The filter every permission carries; null for none.</param>
        /// <param name="resource">The resource; the schedules when null.</param>
        /// <returns>The statement.</returns>
        private static string RoleInsert(string name, IReadOnlyList<int> members, IReadOnlyList<string> actions, string? filter, string? resource = null)
        {
            int system = WellKnownIds.SystemUserId;
            string text = Literal(filter);
            string version = filter is null ? "NULL" : QueryexLanguage.Version.ToString(CultureInfo.InvariantCulture);
            StringBuilder sql = new();
            _ = sql.Append(CultureInfo.InvariantCulture, $"""
                DECLARE @role int = NEXT VALUE FOR [core].[sq_Roles];
                INSERT INTO [core].[Roles] ([Id], [Name], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]) VALUES (@role, N'{name}', SYSDATETIMEOFFSET(), {system}, SYSDATETIMEOFFSET(), {system});

                """);

            // --- One permission per action, then one membership per member ---
            foreach (string action in actions)
            {
                _ = sql.Append(CultureInfo.InvariantCulture, $"""
                    INSERT INTO [core].[Permissions] ([Id], [RoleId], [Resource], [Action], [Filter], [FilterQueryexVersion]) VALUES (NEXT VALUE FOR [core].[sq_Permissions], @role, N'{resource ?? CoreResources.Schedule}', N'{action}', {text}, {version});

                    """);
            }

            foreach (int member in members)
            {
                _ = sql.Append(CultureInfo.InvariantCulture, $"""
                    INSERT INTO [core].[RoleMemberships] ([Id], [UserId], [RoleId]) VALUES (NEXT VALUE FOR [core].[sq_RoleMemberships], {member}, @role);

                    """);
            }

            return sql.ToString();
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

    /// <summary>
    ///     The fixture's change-tracking audit behind a switch. The audit attributes every change in a
    ///     batch's window to that batch, which holds only while one batch at a time writes, so a test
    ///     that runs batches side by side suspends it; a batch begun while it was suspended is not
    ///     checked when it ends. A test may also have one batch lose its response once it committed.
    /// </summary>
    /// <remarks>
    ///     A batch's end is reached whether or not its body ran: a batch whose guard failed ends having
    ///     committed nothing, and is composed again. The probe proves a commit only by finding the
    ///     batch's rows, so a lost response is given only to a batch that change tracking saw write.
    /// </remarks>
    internal sealed class SuspendableAudit : IDataBatchAudit
    {
        /// <summary>Whether the audit is suspended.</summary>
        private volatile bool _suspended;

        /// <summary>How many committing batches end before the one that loses its response; -1 when none is armed.</summary>
        private int _losesResponseIn = -1;

        /// <summary>The table whose next writer loses its response; null when none is armed.</summary>
        private TableName? _losesResponseWriting;

        /// <summary>How many batches lost their response since the last reset.</summary>
        private int _responsesLost;

        /// <summary>The fixture's audit; null when the host has none.</summary>
        public IDataBatchAudit? Inner { get; set; }

        /// <summary>The fixture database's connection string, on which a lost response's change tracking is read; null until the database exists.</summary>
        public string? ConnectionString { get; set; }

        /// <summary>
        ///     How many batches lost their response since the last reset, so a test proves that the
        ///     batch it armed did, rather than passing on the ordinary committed path.
        /// </summary>
        public int ResponsesLost => Volatile.Read(ref _responsesLost);

        /// <summary>Whether the audit is suspended: batches begun now are not checked.</summary>
        public bool Suspended
        {
            get => _suspended;
            set => _suspended = value;
        }

        /// <summary>Whether a lost response is armed and has not happened yet.</summary>
        private bool Armed => Volatile.Read(ref _losesResponseIn) >= 0 || Volatile.Read(ref _losesResponseWriting) is not null;

        /// <inheritdoc />
        public async ValueTask<object?> BeforeAsync(CancellationToken cancellationToken)
        {
            // --- While a lost response is armed, the version the batch begins at, so its end can tell whether it wrote ---
            long? since = Armed ? await CurrentVersionAsync(cancellationToken).ConfigureAwait(false) : null;

            // --- The fixture's audit, unless it is suspended ---
            return _suspended || Inner is not IDataBatchAudit inner
                ? new Window(since, Checked: false, InnerState: null)
                : new Window(since, Checked: true, InnerState: await inner.BeforeAsync(cancellationToken).ConfigureAwait(false));
        }

        /// <summary>
        ///     Arms a lost response: the batch that commits a write after <paramref name="batchesAhead" />
        ///     others did, but its caller learns so only from the probe, as when the connection drops
        ///     during the commit and the probe finds the batch's rows. Its results are never completed
        ///     and its commit callbacks never run, as the executor's are not on that path. A batch that
        ///     wrote nothing, such as a cold cache's load or a batch whose guard failed, is not counted.
        /// </summary>
        /// <param name="batchesAhead">How many batches commit a write, as they are, before that one.</param>
        public void LoseResponse(int batchesAhead)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(batchesAhead);
            _ = Interlocked.Exchange(ref _losesResponseIn, batchesAhead);
        }

        /// <summary>
        ///     Arms a lost response for the next batch to end that wrote <paramref name="writing" />,
        ///     however many batches end before it.
        /// </summary>
        /// <param name="writing">The table whose next writer loses its response.</param>
        public void LoseResponse(TableName writing)
        {
            ArgumentNullException.ThrowIfNull(writing);
            Volatile.Write(ref _losesResponseWriting, writing);
        }

        /// <summary>Disarms a lost response that has not happened yet, and forgets the responses lost.</summary>
        public void Reset()
        {
            _ = Interlocked.Exchange(ref _losesResponseIn, -1);
            Volatile.Write(ref _losesResponseWriting, null);
            _ = Interlocked.Exchange(ref _responsesLost, 0);
        }

        /// <inheritdoc />
        public async ValueTask AfterAsync(object? state, IReadOnlySet<TableName> writtenTables, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(writtenTables);
            var window = (Window)state!;

            // A batch begun before the arming neither counts nor loses its response.
            if (window.Since is long since)
            {
                // --- The batch armed by a table it wrote committed, and the probe is all its caller hears ---
                // The exchange disarms it, so of two such batches ending together only one loses its response.
                TableName? writing = Volatile.Read(ref _losesResponseWriting);
                if (writing is not null
                    && writtenTables.Contains(writing)
                    && await WroteAnyAsync(since, [writing], cancellationToken).ConfigureAwait(false)
                    && ReferenceEquals(Interlocked.CompareExchange(ref _losesResponseWriting, null, writing), writing))
                {
                    throw LostResponse();
                }

                // --- The batch armed by its place among the committing batches committed, and the probe is all its caller hears ---
                if (Volatile.Read(ref _losesResponseIn) >= 0
                    && writtenTables.Count > 0
                    && await WroteAnyAsync(since, writtenTables, cancellationToken).ConfigureAwait(false)
                    && Interlocked.Decrement(ref _losesResponseIn) == -1)
                {
                    throw LostResponse();
                }

                // Below -1, nothing is armed: two batches that raced past the read put the count back.
                _ = Interlocked.CompareExchange(ref _losesResponseIn, -1, -2);
            }

            if (!window.Checked || Inner is not IDataBatchAudit inner)
            {
                return;
            }

            await inner.AfterAsync(window.InnerState, writtenTables, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Counts a lost response and returns what its caller hears: only the probe's verdict.</summary>
        /// <returns>The exception to throw.</returns>
        private DataAccessAmbiguousException LostResponse()
        {
            _ = Interlocked.Increment(ref _responsesLost);
            return new DataAccessAmbiguousException(new TimeoutException("The test dropped the connection during the commit."), CommitVerdict.Committed);
        }

        /// <summary>The database's change-tracking version now.</summary>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The version.</returns>
        private async Task<long> CurrentVersionAsync(CancellationToken cancellationToken)
        {
            await using SqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT CHANGE_TRACKING_CURRENT_VERSION();";
            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is long version ? version : 0L;
        }

        /// <summary>Whether change tracking saw a write to any of the tables after a version.</summary>
        /// <param name="since">The version the batch began at.</param>
        /// <param name="tables">The tables the batch declared it writes.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>True when one of the tracked tables among them changed.</returns>
        private async Task<bool> WroteAnyAsync(long since, IEnumerable<TableName> tables, CancellationToken cancellationToken)
        {
            await using SqlConnection connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

            // --- The tracked tables among them: a table change tracking is off for cannot be probed ---
            HashSet<TableName> tracked = [];
            await using (SqlCommand list = connection.CreateCommand())
            {
                list.CommandText = """
                    SELECT s.[name], t.[name]
                    FROM sys.change_tracking_tables AS c
                    JOIN sys.tables AS t ON t.[object_id] = c.[object_id]
                    JOIN sys.schemas AS s ON s.[schema_id] = t.[schema_id];
                    """;
                await using SqlDataReader reader = await list.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    _ = tracked.Add(new TableName(reader.GetString(0), reader.GetString(1)));
                }
            }

            List<TableName> probed = [.. tables.Where(tracked.Contains)];
            if (probed.Count == 0)
            {
                return false;
            }

            // --- One probe per table, in one statement: any change after the version ---
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = string.Concat(
                "SELECT CASE WHEN ",
                string.Join(" OR ", probed.Select(static table => $"EXISTS (SELECT 1 FROM CHANGETABLE(CHANGES {table.Delimited}, @since) AS c)")),
                " THEN 1 ELSE 0 END;");
            _ = command.Parameters.Add(new SqlParameter("@since", SqlDbType.BigInt) { Value = since });
            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is 1;
        }

        /// <summary>Opens a connection of the audit's own, so the batch's round trips stay its own.</summary>
        /// <param name="cancellationToken">Cancels the open.</param>
        /// <returns>The open connection.</returns>
        private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
        {
            SqlConnection connection = new(ConnectionString ?? throw new InvalidOperationException("The audit has no database to read change tracking from yet."));
            try
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                return connection;
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>What a batch carries from its start to its end.</summary>
        /// <param name="Since">The change-tracking version the batch began at, read only while a lost response was armed; null otherwise.</param>
        /// <param name="Checked">Whether the fixture's audit checks the batch: it was neither suspended nor absent when the batch began.</param>
        /// <param name="InnerState">What the fixture's audit returned when the batch began.</param>
        private sealed record Window(long? Since, bool Checked, object? InnerState);
    }


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

        /// <summary>When the lease ends; null when not leased.</summary>
        public DateTimeOffset? LeaseExpiresAt => (DateTimeOffset?)Columns["LeaseExpiresAt"];

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

    /// <summary>
    ///     The client events a host delivered, in place of a hub. Each scope's publisher is the
    ///     platform's own, with its refusals and its coalescing of a batch's events into one delivery
    ///     once the batch committed, delivering here instead of to connections.
    /// </summary>
    public sealed class CommittedEvents
    {
        /// <summary>The events, in the order delivered.</summary>
        private readonly ConcurrentQueue<ClientEvent> _events = new();

        /// <summary>The events delivered since the last clear.</summary>
        public IReadOnlyList<ClientEvent> Published => [.. _events];

        /// <summary>Forgets every event.</summary>
        public void Clear()
        {
            _events.Clear();
        }

        /// <summary>Creates the publisher of one scope, which delivers here.</summary>
        /// <param name="services">The scope's services.</param>
        /// <returns>The publisher.</returns>
        public IClientEventPublisher PublisherFor(IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(services);
            return new Publisher(services.GetRequiredService<IClientEventRegistry>(), services.GetRequiredService<ILogger<CommittedEvents>>(), this);
        }

        /// <summary>The platform's publisher, delivering to the fixture's events.</summary>
        /// <param name="registry">The registered client events.</param>
        /// <param name="logger">Where a failed delivery is logged.</param>
        /// <param name="sink">The events it delivers to.</param>
        private sealed class Publisher(IClientEventRegistry registry, ILogger logger, CommittedEvents sink) : Core.Notifications.ClientEventPublisher(registry, logger)
        {
            /// <inheritdoc />
            protected override Task DeliverAsync(ClientEvent clientEvent)
            {
                sink._events.Enqueue(clientEvent);
                return Task.CompletedTask;
            }
        }
    }

    /// <summary>
    ///     A notifier that records each request and each withdrawal once its batch committed, each
    ///     request of a batch whose commit only the probe proved apart, and each sent on its own at
    ///     once, and delivers nothing; a test may have it refuse every request, as a notifier whose
    ///     statement cannot be composed would.
    /// </summary>
    public sealed class CommittedNotices : INotifier
    {
        /// <summary>The requests, in the order recorded.</summary>
        private readonly ConcurrentQueue<NotificationRequest> _requests = new();

        /// <summary>The withdrawals, in the order recorded.</summary>
        private readonly ConcurrentQueue<WithdrawRequest> _withdrawals = new();

        /// <summary>The requests of batches whose commit only the probe proved, in the order announced.</summary>
        private readonly ConcurrentQueue<NotificationRequest> _proven = new();

        /// <summary>The requests recorded since the last clear.</summary>
        public IReadOnlyList<NotificationRequest> Raised => [.. _requests];

        /// <summary>The withdrawals recorded since the last clear.</summary>
        public IReadOnlyList<WithdrawRequest> Withdrawn => [.. _withdrawals];

        /// <summary>The requests of batches whose commit only the probe proved, announced since the last clear.</summary>
        public IReadOnlyList<NotificationRequest> Proven => [.. _proven];

        /// <summary>Whether every request appended to a batch is refused with an exception; off again after each clear.</summary>
        public bool Refuses { get; set; }

        /// <summary>
        ///     Runs as each withdrawal sent on its own arrives, before its token is checked, as a test
        ///     arranges something to happen while one is in flight; none again after each clear.
        /// </summary>
        public Action? Withdrawing { get; set; }

        /// <summary>Forgets every request, accepts requests again, and runs nothing as a withdrawal arrives.</summary>
        public void Clear()
        {
            _requests.Clear();
            _withdrawals.Clear();
            _proven.Clear();
            Refuses = false;
            Withdrawing = null;
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

            // --- As the platform notifier does, the requests are announced after a commit only the probe proved ---
            (batch as IBatchProvenCommit)?.OnCommitProven(() =>
            {
                foreach (NotificationRequest request in raised)
                {
                    _proven.Enqueue(request);
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
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(requests);
            WithdrawRequest[] withdrawn = [.. requests];
            batch.OnCommitted(_ =>
            {
                foreach (WithdrawRequest request in withdrawn)
                {
                    _withdrawals.Enqueue(request);
                }
            });
        }

        /// <inheritdoc />
        public Task WithdrawAsync(IReadOnlyList<WithdrawRequest> requests, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(requests);

            // --- What the test arranged, then the token, as a notifier's round trip honours it ---
            Withdrawing?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
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

        /// <summary>A schedulable arguments-only handler that runs while the tenant is read-only.</summary>
        public const string Sweep = "test.sweep";

        /// <summary>A key the feature retired: its jobs are cancelled by the hourly sweep.</summary>
        public const string Retired = "test.retired";

        /// <summary>The client event the shipments' scripted effect publishes.</summary>
        public const string ShippedEvent = "test.shipped";

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

        /// <summary>What a shipment save's persist effect appends, given the scope's client event publisher; nothing when null.</summary>
        public Func<PersistContext<Shipment>, IClientEventPublisher, Task>? ShipmentEffect { get; set; }

        /// <summary>The batches the handlers were invoked with, by key, in order.</summary>
        public ConcurrentQueue<(string Key, int[] JobIds)> Invocations { get; } = new();

        /// <summary>Forgets every script and invocation.</summary>
        public void Reset()
        {
            Probe = null;
            Ship = null;
            Steps = null;
            Inbox = null;
            ShipmentEffect = null;
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

    /// <summary>The feature of the scripted handlers, the warnings they raise, the shipments' scripted effect and the event it publishes.</summary>
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
                .JobHandler<RunSweepHandler>()
                .RetiredJobKey(JobRunKeys.Retired)
                .PersistEffect<Shipment, RunShipmentEffect>()
                .ClientEvent(JobRunKeys.ShippedEvent)
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

    /// <summary>The shipments' scripted persist effect, which appends nothing until a test sets it.</summary>
    /// <param name="script">The script.</param>
    /// <param name="publisher">The scope's client event publisher.</param>
    public sealed class RunShipmentEffect(JobRunScript script, IClientEventPublisher publisher) : IPersistEffect<Shipment>
    {
        /// <inheritdoc />
        public Task ContributeAsync(PersistContext<Shipment> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return script.ShipmentEffect?.Invoke(context, publisher) ?? Task.CompletedTask;
        }
    }

    /// <summary>A schedulable handler that runs while the tenant is read-only, and does nothing.</summary>
    [JobHandler(JobRunKeys.Sweep, Schedulable = true, RunsWhenReadOnly = true)]
    public sealed class RunSweepHandler : IJobHandler
    {
        /// <inheritdoc />
        public Task ExecuteAsync(JobBatch<Job> batch)
        {
            return Task.CompletedTask;
        }
    }
}

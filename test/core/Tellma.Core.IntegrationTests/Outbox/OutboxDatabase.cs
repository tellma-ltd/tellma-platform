// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Crud;
using Tellma.Core.Data.Batches;
using Tellma.Core.Email;
using Tellma.Core.IntegrationTests.Jobs;
using Tellma.Core.Notifications;
using Tellma.Core.Tenancy;
using Tellma.Core.Testing.Blobs;
using Tellma.Core.Testing.Email;
using Tellma.Testing.Entities;
using Tellma.Testing.Support.Logging;
using Tellma.Testing.Support.Tenancy;

namespace Tellma.Core.IntegrationTests.Outbox
{
    /// <summary>
    ///     One fixture database for the outbox's tests, created once with its users and reset before
    ///     each test. Its host stores blobs in memory, sends notification email through the platform's
    ///     email channel and sms through a sender a test can make throw, sends the sending jobs'
    ///     emails through the platform's email pipeline onto a capturing transport a test scripts,
    ///     under a sandbox switch a test can turn on, answers through a sender a test can make answer
    ///     short, reaches an administrator directory a test can make fail, runs the scripted job
    ///     handlers whose frames enqueue, keeps every log event, lets the stacks own their tables as in production, and
    ///     audits every batch's writes against change tracking. A test may arm a write that races an
    ///     action, run once the action's persist is composed and before it is sent. Its administrator
    ///     may read, delete, retry and cancel every outgoing email, through a request scope a test
    ///     opens for them.
    /// </summary>
    public sealed class OutboxDatabase : IAsyncLifetime
    {
        /// <summary>A member with a sign-in address only, in the tenant's zone.</summary>
        public const int AdaId = 101;

        /// <summary>A member with a contact address, three hours east of the tenant.</summary>
        public const int CyId = 103;

        /// <summary>A member whose contact address carries a display name, which the outbox cannot store.</summary>
        public const int DeeId = 104;

        /// <summary>A member whose name holds a control character.</summary>
        public const int EveId = 105;

        /// <summary>The administrator the failure notices reach, who may read, delete, retry and cancel every outgoing email.</summary>
        public const int AdministratorId = 111;

        /// <summary>The address <see cref="CyId" /> prefers to be written to.</summary>
        public const string CyContactEmail = "cy.contact@example.com";

        /// <summary>The contact address <see cref="DeeId" /> gave, a mailbox rather than an address.</summary>
        public const string DeeContactEmail = "Dee <dee.contact@example.com>";

        /// <summary>The name of <see cref="EveId" />, with a tab between its words.</summary>
        public const string EveName = "Eve\tStone";

        /// <summary>The time zone <see cref="CyId" /> prefers.</summary>
        public const string CyTimeZone = "Asia/Riyadh";

        /// <summary>The statements that return the database to its state after creation, the users kept.</summary>
        private const string ResetText = """
            DELETE FROM [core].[OutgoingEmailAttachments];
            DELETE FROM [core].[OutgoingEmails];
            DELETE FROM [core].[Blobs];
            DELETE FROM [core].[Notifications];
            DELETE FROM [core].[NotificationPreferences];
            DELETE FROM [core].[Jobs];
            UPDATE [core].[JobWorkerState] SET [LastTickAt] = NULL, [LastTickOwner] = NULL;
            """;

        /// <summary>The actions the administrator's role grants over the outgoing emails.</summary>
        private static readonly string[] AdministratorActions = [AccessActions.Read, AccessActions.Delete, AccessActions.Retry, AccessActions.Cancel];

        /// <summary>The suite's server.</summary>
        private readonly SqlServerFixture _sql;

        /// <summary>The host, once created.</summary>
        private FixtureTenant? _tenant;

        /// <summary>Creates the fixture.</summary>
        /// <param name="sql">The suite's server.</param>
        public OutboxDatabase(SqlServerFixture sql)
        {
            ArgumentNullException.ThrowIfNull(sql);
            _sql = sql;
        }

        /// <summary>The fixture host over the database.</summary>
        public FixtureTenant Tenant => _tenant ?? throw new InvalidOperationException("The database is not created yet.");

        /// <summary>The database's connection string.</summary>
        public string ConnectionString => Tenant.ConnectionString;

        /// <summary>The host's blob store.</summary>
        public InMemoryBlobStore Blobs { get; private set; } = new();

        /// <summary>The email pipeline's transport: every message the router passes on captured, each result scripted.</summary>
        public CapturingEmailSender Sender { get; private set; } = new();

        /// <summary>Whether the email pipeline routes the tenant's mail as a sandbox tenant's.</summary>
        public StubSandboxContext Sandbox { get; } = new();

        /// <summary>Every log event of the host, with every structured value.</summary>
        public StateCapturingLoggerProvider Logs { get; } = new();

        /// <summary>What the scripted handlers do, set by each test.</summary>
        public JobRunScript Script { get; } = new();

        /// <summary>Whether the sms sender throws, which fails the delivery job after email delivered.</summary>
        public SmsSwitch Sms { get; } = new();

        /// <summary>How the email sender's answers and the administrator lookup misbehave, set by each test.</summary>
        public DispatchFaults Faults { get; } = new();

        /// <summary>The write a test arms to race an action between its validation round and its persist.</summary>
        public PersistRace Race { get; } = new();

        /// <summary>The cancellation token of the running test.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            _tenant = await FixtureTenant.CreateAsync(
                _sql.MasterConnectionString,
                _sql.NewDatabaseName("fx_outbox"),
                services =>
                {
                    // --- In-memory blobs, the platform's email channel and the switchable sms sender ---
                    Blobs = services.AddInMemoryBlobs();
                    _ = services.AddScoped<INotificationChannel, EmailNotificationChannel>();
                    _ = services.AddSingleton(Sms);
                    _ = services.AddScoped<INotificationChannel, SwitchableSms>();

                    // --- The email pipeline over the capturing transport, outside Development so
                    // only the configured transport sends, under the sandbox switch ---
                    _ = services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?> { ["Email:Provider"] = CapturingEmailServiceCollectionExtensions.TransportName })
                        .Build());
                    _ = services.AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = Environments.Production, ApplicationName = "Tellma.Core.IntegrationTests" });
                    _ = services.AddTellmaEmail();
                    Sender = services.AddCapturingEmail(CapturingEmailMode.Transport);
                    _ = services.Replace(ServiceDescriptor.Scoped<ISandboxContext>(_ => Sandbox));

                    // --- The pipeline's router behind a sender whose answers a test may cut short ---
                    ServiceDescriptor router = services.Single(static descriptor => descriptor.ServiceType == typeof(IEmailSender));
                    Type routerType = router.ImplementationType ?? throw new InvalidOperationException("The email pipeline registers its router by type.");
                    _ = services.Replace(ServiceDescriptor.Describe(
                        typeof(IEmailSender),
                        provider => new FaultingSender((IEmailSender)ActivatorUtilities.CreateInstance(provider, routerType), Faults),
                        router.Lifetime));

                    // --- The log that keeps every event ---
                    _ = services.AddSingleton<ILoggerProvider>(Logs);

                    // --- The scripted handlers' script and the one administrator ---
                    _ = services.AddSingleton(Script);
                    _ = services.Replace(ServiceDescriptor.Scoped<IAdministratorDirectory>(_ => new FixedAdministrators(Faults)));

                    // --- The stacks' tables are theirs again, as in production: a raw write to one is refused ---
                    _ = services.Replace(ServiceDescriptor.Singleton<IStackTableOwners>(static provider => new StackTableOwners(provider.GetRequiredService<StackRegistry>())));

                    // --- The race: a persist batch's prologue marks it due, and the scope's next
                    // connection, the persist's own, opens only once the armed write ran ---
                    _ = services.AddSingleton(Race);
                    _ = services.AddScoped<IDataBatchContributor, PersistRaceMarker>();
                    Func<IServiceProvider, object> open = services.Last(static descriptor => descriptor.ServiceType == typeof(ITenantConnectionProvider)).ImplementationFactory
                        ?? throw new InvalidOperationException("The fixture composes its tenant connection provider from a factory.");
                    _ = services.Replace(ServiceDescriptor.Scoped<ITenantConnectionProvider>(scope => new RacingConnectionProvider((ITenantConnectionProvider)open(scope), Race)));
                },
                compose: static tellma => tellma.AddFeature(new JobRunFeature()),
                cancellationToken: Token);

            // --- The tenant's settings and version tags, then the users with their stamps, then the
            // administrator's role over the outgoing emails ---
            string tags = string.Join(", ", Tenant.Services.GetRequiredService<IVersionTagRegistry>().Names.Select(static name => $"(N'{name}')"));
            await SqlServerFixture.ExecuteAsync(
                ConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Settings] SET [TenantId] = {FixtureTenant.TenantId}, [Name] = N'Fixture';"),
                $"INSERT INTO [core].[VersionTags] ([Name], [Tag]) SELECT n.[Name], NEWID() FROM (VALUES {tags}) AS n ([Name]) WHERE NOT EXISTS (SELECT 1 FROM [core].[VersionTags] AS t WHERE t.[Name] = n.[Name]);",
                UserInsert(AdaId, "Ada", contactEmail: null, timeZone: null),
                UserInsert(CyId, "Cy", CyContactEmail, CyTimeZone),
                UserInsert(DeeId, "Dee", DeeContactEmail, timeZone: null),
                UserInsert(EveId, EveName, contactEmail: null, timeZone: null),
                UserInsert(AdministratorId, "Admin", contactEmail: null, timeZone: null),
                AdministratorRoleInsert());
        }

        /// <summary>
        ///     Deletes every row a test may have written, the users kept, and forgets the script, the
        ///     sms and sandbox switches and the captured emails; the transport sends every email again. The log keeps
        ///     every test's events, so a test finds its own by the ids it wrote.
        /// </summary>
        /// <returns>A task that completes when the database is reset.</returns>
        public async Task ResetAsync()
        {
            await SqlServerFixture.ExecuteAsync(ConnectionString, ResetText);
            Script.Reset();
            Sms.Fails = false;
            Sandbox.IsSandbox = false;
            Faults.Answer = null;
            Faults.AdministratorsFail = false;
            Race.Reset();
            Sender.Clear();
            Sender.OnSending(static (_, ordinal) => new EmailSendResult(
                EmailSendOutcome.Sent, string.Create(CultureInfo.InvariantCulture, $"capture-{ordinal}"), ExpectsDeliveryEvents: true));
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            if (_tenant is not null)
            {
                await _tenant.DisposeAsync();
            }

            Logs.Dispose();
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
                    Token);
                return scope;
            }
            catch
            {
                await scope.DisposeAsync();
                throw;
            }
        }

        /// <summary>A user's sign-in address.</summary>
        /// <param name="userId">The user.</param>
        /// <returns>The address.</returns>
        public static string SignInEmail(int userId)
        {
            return string.Create(CultureInfo.InvariantCulture, $"fx-outbox-{userId}@example.com");
        }

        /// <summary>Stores a due job of a scripted handler, not leased, run as the given user and requested by no one.</summary>
        /// <param name="key">The handler key.</param>
        /// <param name="runAs">The run-as user.</param>
        /// <returns>The job's id.</returns>
        public Task<int> InsertJobAsync(string key, int runAs)
        {
            return SqlServerFixture.ScalarAsync<int>(
                ConnectionString,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"""
                    DECLARE @id int = NEXT VALUE FOR [core].[sq_Jobs];
                    INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [Attempts], [RunAsUserId], [CreatedAt])
                    VALUES (@id, N'{key}', 'Pending', DATEADD(second, -5, SYSUTCDATETIME()), 0, {runAs}, SYSUTCDATETIME());
                    SELECT @id;
                    """));
        }

        /// <summary>Reads the rows a query returns, every column by name.</summary>
        /// <param name="query">The query.</param>
        /// <returns>The rows, in the query's order.</returns>
        public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RowsAsync(string query)
        {
            await using SqlConnection connection = new(ConnectionString);
            await connection.OpenAsync(Token);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = query;
            await using SqlDataReader reader = await command.ExecuteReaderAsync(Token);
            List<IReadOnlyDictionary<string, object?>> rows = [];
            while (await reader.ReadAsync(Token))
            {
                Dictionary<string, object?> row = new(StringComparer.Ordinal);
                for (int column = 0; column < reader.FieldCount; column++)
                {
                    row[reader.GetName(column)] = reader.IsDBNull(column) ? null : reader.GetValue(column);
                }

                rows.Add(row);
            }

            return rows;
        }

        /// <summary>The insert of one active user who was invited and has joined, with their stamp row.</summary>
        /// <param name="id">The user's id.</param>
        /// <param name="name">The user's name.</param>
        /// <param name="contactEmail">The user's contact address; null for none.</param>
        /// <param name="timeZone">The user's preferred time zone; null for the tenant's.</param>
        /// <returns>The statement.</returns>
        private static string UserInsert(int id, string name, string? contactEmail, string? timeZone)
        {
            string subject = Subject(id);
            string contact = contactEmail is null ? "NULL" : $"N'{contactEmail}'";
            string zone = timeZone is null ? "NULL" : $"N'{timeZone}'";
            int system = WellKnownIds.SystemUserId;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"""
                INSERT INTO [core].[Users] ([Id], [Kind], [Subject], [Email], [ContactEmail], [InvitedAt], [JoinedAt], [Name], [PreferredTimeZone], [IsActive], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
                VALUES ({id}, '{UserKind.Individual}', '{subject}', N'{SignInEmail(id)}', {contact}, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'{name}', {zone}, 1, SYSDATETIMEOFFSET(), {system}, SYSDATETIMEOFFSET(), {system});
                INSERT INTO [core].[UserStamps] ([UserId]) VALUES ({id});
                """);
        }

        /// <summary>A user's subject.</summary>
        /// <param name="id">The user's id.</param>
        /// <returns>The subject.</returns>
        private static string Subject(int id)
        {
            return string.Create(CultureInfo.InvariantCulture, $"fx-outbox-{id}");
        }

        /// <summary>The insert of the role that lets the administrator read, delete, retry and cancel every outgoing email.</summary>
        /// <returns>The statement.</returns>
        private static string AdministratorRoleInsert()
        {
            // --- One permission per action, over every row ---
            int system = WellKnownIds.SystemUserId;
            string permissions = string.Join(
                Environment.NewLine,
                AdministratorActions.Select(static action => string.Create(
                    CultureInfo.InvariantCulture,
                    $"INSERT INTO [core].[Permissions] ([Id], [RoleId], [Resource], [Action], [Filter], [FilterQueryexVersion]) VALUES (NEXT VALUE FOR [core].[sq_Permissions], @role, N'{CoreResources.OutgoingEmail}', N'{action}', NULL, NULL);")));

            // --- The role, its permissions and its one member ---
            return string.Create(
                CultureInfo.InvariantCulture,
                $"""
                DECLARE @role int = NEXT VALUE FOR [core].[sq_Roles];
                INSERT INTO [core].[Roles] ([Id], [Name], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]) VALUES (@role, N'Email administrators', SYSDATETIMEOFFSET(), {system}, SYSDATETIMEOFFSET(), {system});
                {permissions}
                INSERT INTO [core].[RoleMemberships] ([Id], [UserId], [RoleId]) VALUES (NEXT VALUE FOR [core].[sq_RoleMemberships], {AdministratorId}, @role);
                """);
        }

        /// <summary>The one administrator the failure notices reach, or a lookup that fails while the faults say so.</summary>
        /// <param name="faults">The faults.</param>
        private sealed class FixedAdministrators(DispatchFaults faults) : IAdministratorDirectory
        {
            /// <inheritdoc />
            public Task<IReadOnlyList<int>> GetAdministratorIdsAsync(CancellationToken cancellationToken = default)
            {
                return faults.AdministratorsFail
                    ? throw new InvalidOperationException("The administrators could not be read.")
                    : Task.FromResult<IReadOnlyList<int>>([AdministratorId]);
            }
        }

        /// <summary>The pipeline's router, whose answers the faults may rewrite once it sent.</summary>
        /// <param name="router">The router.</param>
        /// <param name="faults">The faults.</param>
        private sealed class FaultingSender(IEmailSender router, DispatchFaults faults) : IEmailSender
        {
            /// <inheritdoc />
            public async Task<IReadOnlyList<EmailSendResult>> SendAsync(IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken = default)
            {
                IReadOnlyList<EmailSendResult> results = await router.SendAsync(messages, cancellationToken);
                return faults.Answer is { } answer ? answer(results) : results;
            }
        }

        /// <summary>Marks the race due once a persist batch's prologue is composed.</summary>
        /// <param name="race">The race.</param>
        private sealed class PersistRaceMarker(PersistRace race) : IDataBatchContributor
        {
            /// <inheritdoc />
            public int Order => int.MaxValue;

            /// <inheritdoc />
            public void Contribute(IDataBatch batch, DataBatchStage stage)
            {
                ArgumentNullException.ThrowIfNull(batch);
                if (stage == DataBatchStage.Prologue && batch.Purpose == BatchPurpose.Persist)
                {
                    race.MarkDue();
                }
            }
        }

        /// <summary>A scope's tenant connections, each opened once the race, when due, ran.</summary>
        /// <param name="inner">The fixture's provider.</param>
        /// <param name="race">The race.</param>
        private sealed class RacingConnectionProvider(ITenantConnectionProvider inner, PersistRace race) : ITenantConnectionProvider
        {
            /// <inheritdoc />
            public TenantInfo Tenant => inner.Tenant;

            /// <inheritdoc />
            public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken = default)
            {
                await race.RunIfDueAsync();
                return await inner.OpenAsync(cancellationToken);
            }
        }

        /// <summary>The sms sender: delivers nothing, or throws while its switch says it fails.</summary>
        /// <param name="sms">The switch.</param>
        private sealed class SwitchableSms(SmsSwitch sms) : INotificationChannel
        {
            /// <inheritdoc />
            public string Key => FixtureNotifications.SmsChannel;

            /// <inheritdoc />
            public Task DeliverAsync(IOpenWriteHost host, NotificationRequest request, IReadOnlyList<NotificationRecipient> recipients, CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(host);
                return sms.Fails ? throw new InvalidOperationException("The sms transport is down.") : Task.CompletedTask;
            }

            /// <inheritdoc />
            public Task<TestNotificationResult> SendTestAsync(CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }
        }
    }

    /// <summary>How the outbox database's email sender answers and its administrator lookup behave.</summary>
    public sealed class DispatchFaults
    {
        /// <summary>Rewrites the sender's results once its messages went out; null for the results as they are.</summary>
        public Func<IReadOnlyList<EmailSendResult>, IReadOnlyList<EmailSendResult>>? Answer { get; set; }

        /// <summary>Whether reading the administrators throws.</summary>
        public bool AdministratorsFail { get; set; }
    }

    /// <summary>
    ///     A write that races an action: armed by a test, it runs once, on its own connection, after the
    ///     action's persist batch is composed and before that batch opens its connection, as another
    ///     administrator's or a job's write between the action's two rounds would.
    /// </summary>
    public sealed class PersistRace
    {
        /// <summary>Guards the armed write and whether it is due.</summary>
        private readonly Lock _gate = new();

        /// <summary>The armed write; null when none is armed or it ran.</summary>
        private Func<Task>? _write;

        /// <summary>Whether a persist batch was composed since the write was armed.</summary>
        private bool _due;

        /// <summary>Whether the armed write ran.</summary>
        public bool Ran { get; private set; }

        /// <summary>Arms a write, which runs before the next persist batch opens its connection.</summary>
        /// <param name="write">The write.</param>
        public void Arm(Func<Task> write)
        {
            ArgumentNullException.ThrowIfNull(write);
            lock (_gate)
            {
                (_write, _due, Ran) = (write, false, false);
            }
        }

        /// <summary>Disarms the race.</summary>
        public void Reset()
        {
            lock (_gate)
            {
                (_write, _due, Ran) = (null, false, false);
            }
        }

        /// <summary>Marks the armed write due: a persist batch was composed.</summary>
        internal void MarkDue()
        {
            lock (_gate)
            {
                _due = _write is not null;
            }
        }

        /// <summary>Runs the armed write once it is due, then disarms it.</summary>
        /// <returns>A task that completes when the write ran, or at once when none is due.</returns>
        internal async Task RunIfDueAsync()
        {
            Func<Task>? write;
            lock (_gate)
            {
                write = _due ? _write : null;
                if (write is not null)
                {
                    (_write, _due, Ran) = (null, false, true);
                }
            }

            if (write is not null)
            {
                await write();
            }
        }
    }

    /// <summary>Whether the outbox database's sms sender throws.</summary>
    public sealed class SmsSwitch
    {
        /// <summary>Whether the sender throws.</summary>
        public bool Fails { get; set; }
    }
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Access;
using Tellma.Core.Calendars;
using Tellma.Core.Localization;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Access;
using Tellma.Core.Tests.Data.Batches;
using Tellma.Core.Users;
using Tellma.Testing.Support.Identity;
using Tellma.Testing.Support.Logging;

namespace Tellma.Core.Tests.Users
{
    /// <summary>
    ///     The users' stack service over a scripted identity server, a recording membership directory,
    ///     a fixed link, English and Arabic on offer, and a log that keeps every structured value; its
    ///     action contexts are built by hand, so a test completes the persist itself.
    /// </summary>
    internal sealed class UsersKit : IDisposable
    {
        /// <summary>The tenant every context runs in.</summary>
        public const int TenantId = 1;

        /// <summary>The caller every context names.</summary>
        public const int CallerId = 42;

        /// <summary>The tenant's link the invitations carry.</summary>
        public const string TenantLink = "https://acme.example/1";

        /// <summary>The instant every context runs at.</summary>
        public static readonly DateTimeOffset Now = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);


        /// <summary>The scope the evaluator is resolved from.</summary>
        private readonly AsyncServiceScope _scope;

        /// <summary>The services the session listeners resolve from.</summary>
        private readonly ServiceProvider _listeners;

        /// <summary>Composes the kit.</summary>
        public UsersKit()
        {
            _scope = Harness.Services.CreateAsyncScope();
            _listeners = new ServiceCollection().AddSingleton<ISessionTerminationListener>(Listener).BuildServiceProvider();
            IMeterFactory meters = Harness.Services.GetRequiredService<IMeterFactory>();
            Languages = new LanguageCatalog(new LanguageDeclaration(["en-GB", "ar-SA"], []));
            CalendarRegistry calendars = new([new GregorianCalendarSystem(), new UmAlQuraCalendarSystem()]);
            ValueSets = new ValueSetRegistry(Languages, calendars, []);
            LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
            Localizers = new IcuStringLocalizerFactory(Languages, LoggerFactory, meters);
            Invites = new MetricCollector<long>(meters, UsersTelemetryNames.MeterName, UsersTelemetryNames.Invites);
            Dependencies = new UserServiceDependencies(
                _listeners,
                Identity,
                Memberships,
                new FixedLinks(),
                Languages,
                ValueSets,
                Runner,
                Harness.Services.GetRequiredService<ISecurableRegistry>(),
                _scope.ServiceProvider.GetRequiredService<AccessEvaluator>(),
                new CallerConnection(),
                Outbox,
                new LocalizationNegotiator(Languages, calendars, ValueSets, meters),
                Localizers,
                new FakeTimeProvider(Now),
                new UsersMetrics(meters),
                LoggerFactory);
            Service = new UsersUnderTest(new RefusingPipelines(), Dependencies);
        }

        /// <summary>The default composition's batches, whose connection refuses.</summary>
        public BatchHarness Harness { get; } = new();

        /// <summary>The scripted identity server.</summary>
        public ScriptedIdentityClient Identity { get; } = new();

        /// <summary>The membership hints recorded.</summary>
        public RecordingMemberships Memberships { get; } = new();

        /// <summary>The guarded runner, which answers each raw read from a script.</summary>
        public ScriptedRunner Runner { get; } = new();

        /// <summary>The outbox the welcome emails are enqueued through, which records each call.</summary>
        public RecordingOutbox Outbox { get; } = new();

        /// <summary>The session listener a deactivation tells.</summary>
        public RecordingListener Listener { get; } = new();

        /// <summary>Every log event, with every structured value.</summary>
        public StateCapturingLoggerProvider Logs { get; } = new();

        /// <summary>The logger factory over <see cref="Logs" />.</summary>
        public ILoggerFactory LoggerFactory { get; }

        /// <summary>The invites counted.</summary>
        public MetricCollector<long> Invites { get; }

        /// <summary>The languages on offer: English and Arabic.</summary>
        public LanguageCatalog Languages { get; }

        /// <summary>The localizers over the languages, which the welcome email's resources are read through.</summary>
        public IStringLocalizerFactory Localizers { get; }

        /// <summary>The value sets over the languages and the Gregorian and Umm al-Qura calendars.</summary>
        public ValueSetRegistry ValueSets { get; }

        /// <summary>The service's collaborators.</summary>
        public UserServiceDependencies Dependencies { get; }

        /// <summary>The service, with its actions reachable from the tests.</summary>
        public UsersUnderTest Service { get; }

        /// <summary>The service's hooks, as the pipeline calls them.</summary>
        public IEntityBehavior<User, int> Hooks => Service;

        /// <summary>The settings of the tenant: English, then Arabic.</summary>
        /// <returns>The settings.</returns>
        public static TenantSettings Settings()
        {
            return new TenantSettings
            {
                TenantId = TenantId,
                Tag = new VersionTag(Guid.Empty),
                Names = ["Acme", "أكمي", null],
                Languages = [new LanguageInfo("en", "English", "En", false), new LanguageInfo("ar", "العربية", "ع", true)],
                Shape = MultilingualShape.PrimaryAndSecondary,
                Calendars = [],
                TimeZone = TimeZoneInfo.Utc,
                SqlServerTimeZoneName = "UTC",
                ModifiedAt = Now,
                Entries = new Dictionary<string, JsonElement>(),
            };
        }

        /// <summary>The context of a request by the caller in the tenant.</summary>
        /// <param name="sandbox">Whether the tenant is a sandbox.</param>
        /// <returns>The context.</returns>
        public static RequestContext Context(bool sandbox = false)
        {
            return RequestContext.Tenantless(new GregorianCalendarSystem(), Now) with
            {
                Tenant = new TenantDescriptor(TenantId, "Acme", null, null, ["en", "ar"], sandbox ? TenantCategory.Sandbox : TenantCategory.Live, TenantState.Active, null),
                Kind = PrincipalKind.User,
                Subject = "sub-42",
                UserId = CallerId,
                TenantSettings = Settings(),
                Now = Now,
            };
        }

        /// <summary>An action's context over loaded targets, with a batch that records the write-back.</summary>
        /// <param name="batch">The batch the action composes onto.</param>
        /// <param name="sandbox">Whether the tenant is a sandbox.</param>
        /// <param name="ids">The ids asked, in request order.</param>
        /// <param name="targets">The targets loaded, in any order.</param>
        /// <returns>The context.</returns>
        public static ActionContext<User, int> Action(IDataBatch batch, bool sandbox, IReadOnlyList<int> ids, params User[] targets)
        {
            return new ActionContext<User, int>
            {
                Ids = ids,
                Context = Context(sandbox),
                Loader = new RulesLoader(),
                Errors = new ValidationErrors<User>(),
                Batch = batch,
                Host = Refusing.Of<IWriteHost>(),
                Entities = targets,
            };
        }

        /// <summary>A new individual with an email, active and never invited.</summary>
        /// <param name="id">The user's id.</param>
        /// <param name="name">The user's name in the first language.</param>
        /// <returns>The user.</returns>
        public static User Individual(int id, string name)
        {
            return new User
            {
                Id = id,
                Kind = UserKind.Individual,
                Email = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"user{id}@example.com"),
                Name = name,
                State = UserState.New,
                IsActive = true,
            };
        }

        /// <summary>A service account, active.</summary>
        /// <param name="id">The account's id.</param>
        /// <param name="subject">The client it holds; null for none yet.</param>
        /// <returns>The account.</returns>
        public static User ServiceAccount(int id, string? subject)
        {
            return new User { Id = id, Kind = UserKind.Service, Name = "Robot", Subject = subject, State = UserState.New, IsActive = true };
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Invites.Dispose();
            LoggerFactory.Dispose();
            Logs.Dispose();
            _listeners.Dispose();
            _scope.Dispose();
            Harness.Dispose();
        }

        /// <summary>
        ///     The scope's connection, connected as the caller named Caller and المتصل; nothing else
        ///     of a connection is the users' stack's concern.
        /// </summary>
        private sealed class CallerConnection : IScopeConnection
        {
            /// <summary>The caller the scope connects as.</summary>
            private readonly ConnectedUser _caller = new(
                CallerId,
                UserKind.Individual,
                UserState.Joined,
                new UserProfile("Caller", "المتصل", null, null, null, null, null, null, null, null, null, null, null, null, Now),
                Guid.NewGuid(),
                UserAccess.System,
                new Dictionary<string, Guid>(StringComparer.Ordinal),
                null);

            /// <inheritdoc />
            public RequestContextInputs Inputs => RequestContextInputs.Empty;

            /// <inheritdoc />
            public Task<ConnectedUser> CurrentAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(_caller);
            }

            /// <inheritdoc />
            public bool IsPrologueDue(ConnectedUser user)
            {
                throw new NotSupportedException("The users' stack composes no prologue.");
            }

            /// <inheritdoc />
            public ConnectPremises PremisesFor(ConnectedUser user)
            {
                throw new NotSupportedException("The users' stack composes no prologue.");
            }

            /// <inheritdoc />
            public BatchResult<ConnectResult> Contribute(IDataBatch batch, ConnectPremises premises)
            {
                throw new NotSupportedException("The users' stack composes no prologue.");
            }

            /// <inheritdoc />
            public ConnectResult? ReadOf(BatchResult<ConnectResult> prologue)
            {
                throw new NotSupportedException("The users' stack reads no prologue.");
            }

            /// <inheritdoc />
            public ConnectedUser Apply(ConnectResult result, ConnectedUser basis)
            {
                throw new NotSupportedException("The users' stack applies no prologue.");
            }

            /// <inheritdoc />
            public Task<ConnectedUser> ReconnectColdAsync(CancellationToken cancellationToken)
            {
                throw new NotSupportedException("The users' stack never reconnects.");
            }

            /// <inheritdoc />
            public Task<ConnectedUser> RevalidateAsync(ConnectedUser user, CancellationToken cancellationToken)
            {
                throw new NotSupportedException("The users' stack never re-checks a denial.");
            }

            /// <inheritdoc />
            public Task<UserAccess?> LoadDetachedAsync(int userId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException("The users' stack loads no other user's set.");
            }

            /// <inheritdoc />
            public void Drop(ConnectedUser user)
            {
                throw new NotSupportedException("The users' stack drops no entries.");
            }
        }

        /// <summary>The tenant's link for every tenant.</summary>
        private sealed class FixedLinks : ITellmaLinks
        {
            /// <inheritdoc />
            public string Tenant(int tenantId)
            {
                return TenantLink;
            }

            /// <inheritdoc />
            public string Target(int tenantId, string? resource, long? id)
            {
                return TenantLink;
            }
        }
    }

    /// <summary>
    ///     The users' service with its actions reachable from the tests, which build the action
    ///     contexts themselves: the pipeline invokes an action only through its securable.
    /// </summary>
    /// <param name="factory">The pipeline factory.</param>
    /// <param name="dependencies">The service's collaborators.</param>
    internal sealed class UsersUnderTest(IEntityPipelineFactory factory, UserServiceDependencies dependencies) : UserService<User>(factory, dependencies)
    {
        /// <summary>Runs the invite action over a context the test built.</summary>
        /// <param name="context">The context.</param>
        /// <returns>The action's results.</returns>
        public Task<IReadOnlyList<InviteResult>> RunInviteAsync(ActionContext<User, int> context)
        {
            return InviteAsync(context);
        }

        /// <summary>Runs the issue-credentials action over a context the test built.</summary>
        /// <param name="context">The context.</param>
        /// <returns>The action's result.</returns>
        public Task<CredentialsResult> RunIssueCredentialsAsync(ActionContext<User, int> context)
        {
            return IssueCredentialsAsync(context);
        }
    }

    /// <summary>An outbox that records each call and enlists nothing.</summary>
    internal sealed class RecordingOutbox : IEmailOutbox
    {
        /// <summary>The calls, in order: the frame each enlisted with and its requests.</summary>
        public ConcurrentQueue<(IWriteHost Host, IReadOnlyList<EmailEnqueueRequest> Requests)> Calls { get; } = new();

        /// <summary>Every request of every call, in call order.</summary>
        public IReadOnlyList<EmailEnqueueRequest> Requests => [.. Calls.SelectMany(static call => call.Requests)];

        /// <inheritdoc />
        public Task<EnlistedSave<OutgoingEmail>> EnqueueAsync(IWriteHost host, IReadOnlyList<EmailEnqueueRequest> requests, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(requests);
            Calls.Enqueue((host, requests));
            return Task.FromResult<EnlistedSave<OutgoingEmail>>(null!);
        }
    }

    /// <summary>A membership directory that records every hint.</summary>
    internal sealed class RecordingMemberships : ITenantMembershipDirectory
    {
        /// <summary>Every hint recorded, in call order.</summary>
        public ConcurrentQueue<TenantMembershipRecord> Recorded { get; } = new();

        /// <summary>The hints recorded, each as its tenant, subject and activity, in call order.</summary>
        /// <returns>The hints.</returns>
        public IReadOnlyList<(int TenantId, string Subject, bool IsActive)> Hints()
        {
            return [.. Recorded.Select(static record => (record.TenantId, record.Subject, record.IsActive))];
        }

        /// <summary>Fails every record when set.</summary>
        public Exception? Failure { get; set; }

        /// <inheritdoc />
        public Task<IReadOnlyList<TenantMembership>> ListAsync(string subject, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("The users' stack never lists memberships.");
        }

        /// <inheritdoc />
        public Task RecordAsync(IReadOnlyList<TenantMembershipRecord> records, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(records);
            if (Failure is { } failure)
            {
                return Task.FromException(failure);
            }

            foreach (TenantMembershipRecord record in records)
            {
                Recorded.Enqueue(record);
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     A guarded runner whose batches record one raw statement each and answer it with the ids
    ///     scripted, or fail before the round trip.
    /// </summary>
    internal sealed class ScriptedRunner : IGuardedBatchRunner
    {
        /// <summary>The ids each statement's first result set answers.</summary>
        public int[] Rows { get; set; } = [];

        /// <summary>Fails every run when set.</summary>
        public Exception? Failure { get; set; }

        /// <summary>Every run's purpose and the statement it composed, in call order.</summary>
        public ConcurrentQueue<(BatchPurpose Purpose, FormattableString Sql)> Runs { get; } = new();

        /// <inheritdoc />
        public async Task<TResult> RunAsync<TResult>(
            BatchPurpose purpose,
            Func<ConnectedUser, IDataBatch, ValueTask> compose,
            Func<ConnectedUser, BatchOutcome, TResult> read,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(compose);
            ArgumentNullException.ThrowIfNull(read);
            if (Failure is { } failure)
            {
                throw failure;
            }

            // The statement is composed and recorded, then answered; neither callback reads the user
            // or the outcome.
            (IDataBatch batch, RecordingBatchProxy recorder) = RecordingBatchProxy.Create();
            await compose(null!, batch).ConfigureAwait(false);
            Runs.Enqueue((purpose, Assert.Single(recorder.Statements).Sql));
            recorder.Complete(Rows);
            return read(null!, null!);
        }
    }

    /// <summary>A session listener that records each revocation.</summary>
    internal sealed class RecordingListener : ISessionTerminationListener
    {
        /// <summary>Every revocation, in call order.</summary>
        public ConcurrentQueue<(int TenantId, string Subject)> Revoked { get; } = new();

        /// <summary>Fails every revocation when set.</summary>
        public Exception? Failure { get; set; }

        /// <inheritdoc />
        public Task SessionsTerminatedAsync(string subject, IReadOnlyList<string> sessionHandles)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task TenantAccessRevokedAsync(int tenantId, string subject)
        {
            Revoked.Enqueue((tenantId, subject));
            return Failure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
        }
    }

    /// <summary>
    ///     A batch that takes the table-valued parameters and the raw statements an action composes,
    ///     records them, and answers each statement once the test completes it; every other member
    ///     refuses.
    /// </summary>
    public class RecordingBatchProxy : DispatchProxy
    {
        /// <summary>The parameters bound, in order.</summary>
        public List<object> Tables { get; } = [];

        /// <summary>The statements appended, with their options and their pending results.</summary>
        public List<(FormattableString Sql, SqlOptions? Options, BatchResult<RawResult> Result)> Statements { get; } = [];

        /// <summary>A recording batch.</summary>
        /// <returns>The batch and its recorder.</returns>
        public static (IDataBatch Batch, RecordingBatchProxy Recorder) Create()
        {
            IDataBatch batch = Create<IDataBatch, RecordingBatchProxy>();
            return (batch, (RecordingBatchProxy)(object)batch);
        }

        /// <summary>Completes the one statement with the ids of its first result set.</summary>
        /// <param name="ids">The ids.</param>
        public void Complete(params int[] ids)
        {
            using DataTable table = new();
            _ = table.Columns.Add("UserId", typeof(int));
            foreach (int id in ids)
            {
                _ = table.Rows.Add(id);
            }

            QueryRowSet.Builder builder = new([new QueryColumn("UserId", QueryexType.QxNumeric, null, null, false, null, false)], [typeof(int)], ids.Length);
            using DataTableReader reader = table.CreateDataReader();
            while (reader.Read())
            {
                builder.AppendRow(reader);
            }

            Assert.Single(Statements).Result.Complete(new RawResult([builder.Build()]));
        }

        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            switch (targetMethod.Name)
            {
                case nameof(IDataBatch.Tvp):
                    Tables.Add(args![0]!);
                    return new SqlIdentifier(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"@tm_rec{Tables.Count - 1}_t"));
                case nameof(IDataBatch.Sql):
                    BatchResult<RawResult> result = new();
                    Statements.Add(((FormattableString)args![0]!, (SqlOptions?)args[1], result));
                    return result;
                default:
                    throw new NotSupportedException($"The action reached {targetMethod.Name}, which the recording batch does not answer.");
            }
        }
    }
}

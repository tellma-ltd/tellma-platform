// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Caching;
using Tellma.Core.Calendars;
using Tellma.Core.Data;
using Tellma.Core.Queryex;
using Tellma.Core.Tenancy;
using Tellma.Core.Tests.Crud.Scripted;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     The guarded runner over a fake connection and scripted batches: one round trip when the
    ///     guard passes, one recompose from the fresh state after any one failure, and a second
    ///     failure in the same call refused as stale, counted, metered and logged.
    /// </summary>
    public sealed class GuardedBatchRunnerTests : IAsyncDisposable
    {
        /// <summary>The instant the contexts are stamped with.</summary>
        private static readonly DateTimeOffset Now = new(2026, 3, 14, 9, 30, 0, TimeSpan.Zero);

        /// <summary>The settings tag the scope's context carries.</summary>
        private static readonly Guid SettingsA = Guid.Parse("5e77a000-0000-0000-0000-00000000000a");

        /// <summary>A settings tag newer than the context's.</summary>
        private static readonly Guid SettingsB = Guid.Parse("5e77a000-0000-0000-0000-00000000000b");

        /// <summary>The tenant-level permissions tag.</summary>
        private static readonly Guid PermissionsTag = Guid.Parse("9e40a000-0000-0000-0000-000000000001");

        /// <summary>The composed root, for the meter factory.</summary>
        private readonly ServiceProvider _services = TestComposition.Compose(static _ => { }).BuildServiceProvider(validateScopes: true);

        /// <summary>The scripted round trips.</summary>
        private readonly BatchScript _script = new();

        /// <summary>What each round trip does, in order.</summary>
        private readonly Queue<Func<ScriptedBatch, Task>> _steps = new();

        /// <summary>The fake connection.</summary>
        private readonly FakeConnection _connection = new();

        /// <summary>The scope's context.</summary>
        private readonly Holder _holder = new();

        /// <summary>Collects the runner's logs.</summary>
        private readonly FakeLoggerProvider _logs = new();

        /// <summary>Creates the runner's logger over <see cref="_logs" />.</summary>
        private readonly LoggerFactory _loggers;

        /// <summary>The initializer after the connection, which a fresh settings tag re-runs.</summary>
        private readonly RecordingInitializer _locale = new(200);

        /// <summary>An initializer before the connection, which nothing re-runs.</summary>
        private readonly RecordingInitializer _early = new(50);

        /// <summary>Scripts the round trips from the steps.</summary>
        public GuardedBatchRunnerTests()
        {
            _loggers = new LoggerFactory([_logs]);
            _ = _script.OnExecute((batch, _) => _steps.Dequeue()(batch));
        }

        /// <summary>The scope's tenant database, which creates the scripted batches.</summary>
        private ITenantDatabase Database => new ScriptedDatabase(_script);

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            _loggers.Dispose();
            _logs.Dispose();
            await _services.DisposeAsync();
        }

        // --- One round trip ---

        [Fact]
        public async Task A_passing_guard_returns_the_read_after_one_round_trip_and_re_runs_nothing()
        {
            Pass();
            int composed = 0;

            string read = await Runner().RunAsync(
                BatchPurpose.Read,
                (user, batch) =>
                {
                    composed++;
                    Assert.Same(_connection.Connected, user);
                    return ValueTask.CompletedTask;
                },
                static (user, outcome) => $"{user.UserId}:{outcome.RoundTrips}",
                TestContext.Current.CancellationToken);

            Assert.Equal("7:1", read);
            Assert.Equal(1, composed);
            Assert.Equal(1, _script.RoundTrips);
            _ = Assert.Single(_connection.Applied);
            Assert.Equal(0, _connection.Reconnects);
            Assert.Equal(0, _locale.Runs);
        }

        [Theory]
        [InlineData(BatchPurpose.Maintenance)]
        [InlineData((BatchPurpose)99)]
        public async Task A_maintenance_or_undefined_purpose_is_refused_before_any_round_trip(BatchPurpose purpose)
        {
            _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => RunAsync(purpose));

            Assert.Equal(0, _script.RoundTrips);
            Assert.Equal(0, _connection.Currents);
        }

        // --- A failed guard ---

        [Fact]
        public async Task A_failed_guard_recomposes_once_under_the_state_the_failed_round_trip_returned()
        {
            Fail(permissionsStale: true);
            Pass();
            List<ConnectedUser> composedFor = [];

            _ = await Runner().RunAsync(
                BatchPurpose.Validate,
                (user, _) =>
                {
                    composedFor.Add(user);
                    return ValueTask.CompletedTask;
                },
                static (user, _) => user,
                TestContext.Current.CancellationToken);

            Assert.Equal(2, _script.RoundTrips);
            Assert.Equal(2, composedFor.Count);
            Assert.Same(_connection.Connected, composedFor[0]);
            Assert.Same(_connection.Applied[0].User, composedFor[1]);
            Assert.Same(composedFor[1], _connection.Premised[1]);
            Assert.Equal(0, Thrashes());
        }

        [Fact]
        public async Task A_second_failed_guard_in_one_call_is_refused_as_stale_counted_metered_and_logged()
        {
            Fail(permissionsStale: true, settingsStale: true);
            Fail(permissionsStale: true, settingsStale: true);
            using MetricCollector<long> results = new(_services.GetRequiredService<IMeterFactory>(), AccessTelemetryNames.MeterName, AccessTelemetryNames.ConnectResults);
            using MetricCollector<long> stale = new(_services.GetRequiredService<IMeterFactory>(), CacheTelemetryNames.MeterName, CacheTelemetryNames.Stale);

            StaleContextException refused = await Assert.ThrowsAsync<StaleContextException>(() => RunAsync(BatchPurpose.Validate));

            Assert.Equal(
                [
                    new VersionTagDependency(VersionTagNames.Permissions, new VersionTag(PermissionsTag), VersionTagMismatchPolicy.Rerun),
                    new VersionTagDependency(VersionTagNames.Settings, new VersionTag(SettingsA), VersionTagMismatchPolicy.Rerun),
                ],
                refused.Dependencies);
            Assert.Equal(2, _script.RoundTrips);
            CollectedMeasurement<long> thrash = Assert.Single(results.GetMeasurementSnapshot());
            Assert.Equal(AccessTelemetryNames.ResultGuardThrash, thrash.Tags[AccessTelemetryNames.ResultTag]);
            CollectedMeasurement<long> exhausted = Assert.Single(stale.GetMeasurementSnapshot());
            Assert.Equal(VersionTagMetrics.ExhaustedPhase, exhausted.Tags[CacheTelemetryNames.StalePhaseTag]);
            FakeLogRecord logged = Assert.Single(_logs.Collector.GetSnapshot());
            Assert.Equal(LogLevel.Warning, logged.Level);
            Assert.Equal(AccessEvents.GuardThrash, logged.Id.Id);
        }

        [Fact]
        public async Task A_guard_that_failed_on_the_user_alone_is_refused_as_stale_permissions()
        {
            Fail();
            Fail();

            StaleContextException refused = await Assert.ThrowsAsync<StaleContextException>(() => RunAsync(BatchPurpose.Read));

            VersionTagDependency dependency = Assert.Single(refused.Dependencies);
            Assert.Equal(VersionTagNames.Permissions, dependency.Name);
        }

        [Fact]
        public async Task Fresh_settings_re_run_the_initializers_after_the_connection_and_no_other()
        {
            Fail(settingsStale: true, settingsTag: SettingsB);
            Pass(settingsTag: SettingsB);

            _ = await RunAsync(BatchPurpose.Validate);

            Assert.Equal(1, _locale.Runs);
            Assert.Same(_connection.Inputs, _locale.Inputs);
            Assert.Equal(0, _early.Runs);
        }

        [Fact]
        public async Task A_failed_guard_that_brought_the_settings_the_context_carries_re_runs_no_initializer()
        {
            Fail(permissionsStale: true);
            Pass();

            _ = await RunAsync(BatchPurpose.Validate);

            Assert.Equal(0, _locale.Runs);
        }

        // --- The persist's re-checks ---

        [Fact]
        public async Task A_tag_that_moved_before_the_persist_transaction_reconnects_cold_once_and_recomposes()
        {
            _steps.Enqueue(static _ => throw new BatchAssertionFailedException(TellmaSqlErrors.StaleVersionTag, "Stale", statementOrdinal: null));
            Pass();

            ConnectedUser user = await RunAsync(BatchPurpose.Persist);

            Assert.Equal(1, _connection.Reconnects);
            Assert.Same(_connection.Reconnected, _connection.Premised[1]);
            Assert.Same(_connection.Applied.Single().User, user);
            Assert.Equal(2, _script.RoundTrips);
        }

        [Fact]
        public async Task A_tag_that_moved_twice_is_refused_as_stale_and_counted()
        {
            _steps.Enqueue(static _ => throw new BatchAssertionFailedException(TellmaSqlErrors.StaleVersionTag, "Stale", statementOrdinal: null));
            _steps.Enqueue(static _ => throw new BatchAssertionFailedException(TellmaSqlErrors.StaleVersionTag, "Stale", statementOrdinal: null));

            StaleContextException refused = await Assert.ThrowsAsync<StaleContextException>(() => RunAsync(BatchPurpose.Persist));

            Assert.Empty(refused.Dependencies);
            Assert.Equal(1, _connection.Reconnects);
            Assert.Equal(1, Thrashes());
        }

        [Fact]
        public async Task One_recompose_is_shared_by_a_failed_guard_and_a_moved_tag()
        {
            Fail(permissionsStale: true);
            _steps.Enqueue(static _ => throw new BatchAssertionFailedException(TellmaSqlErrors.StaleVersionTag, "Stale", statementOrdinal: null));

            _ = await Assert.ThrowsAsync<StaleContextException>(() => RunAsync(BatchPurpose.Persist));

            Assert.Equal(2, _script.RoundTrips);
            Assert.Equal(0, _connection.Reconnects);
        }

        [Fact]
        public async Task A_caller_deactivated_before_the_persist_transaction_drops_the_user_and_finds_no_tenant()
        {
            _steps.Enqueue(static _ => throw new BatchAssertionFailedException(TellmaSqlErrors.CallerInvalid, "CallerInvalid", statementOrdinal: null));

            TenantNotFoundException refused = await Assert.ThrowsAsync<TenantNotFoundException>(() => RunAsync(BatchPurpose.Persist));

            Assert.Equal(1, refused.TenantId);
            Assert.Same(_connection.Connected, Assert.Single(_connection.Dropped));
            Assert.Equal(1, _script.RoundTrips);
        }

        // --- A read or validation whose re-run input moved ---

        [Fact]
        public async Task A_moved_re_run_input_recomposes_once_without_a_reconnect()
        {
            _steps.Enqueue(static _ => throw new StaleContextException([new VersionTagDependency("entity:core.Unit", VersionTag.New(), VersionTagMismatchPolicy.Rerun)]));
            Pass();

            _ = await RunAsync(BatchPurpose.Read);

            Assert.Equal(2, _script.RoundTrips);
            Assert.Equal(0, _connection.Reconnects);
            Assert.Equal(2, _connection.Premised.Count);
        }

        [Fact]
        public async Task A_re_run_input_that_moved_twice_is_refused_with_the_executors_own_refusal()
        {
            StaleContextException second = new([new VersionTagDependency("entity:core.Unit", VersionTag.New(), VersionTagMismatchPolicy.Rerun)]);
            _steps.Enqueue(static _ => throw new StaleContextException([new VersionTagDependency("entity:core.Unit", VersionTag.New(), VersionTagMismatchPolicy.Rerun)]));
            _steps.Enqueue(_ => throw second);

            StaleContextException refused = await Assert.ThrowsAsync<StaleContextException>(() => RunAsync(BatchPurpose.Read));

            Assert.Same(second, refused);
            Assert.Equal(1, Thrashes());
        }

        // --- Helpers ---

        /// <summary>The connected user the fake connection starts with.</summary>
        /// <param name="settingsTag">The settings tag its tenant tags carry.</param>
        /// <returns>The user.</returns>
        private static ConnectedUser User(Guid settingsTag)
        {
            return new ConnectedUser(
                7,
                UserKind.Individual,
                UserState.Joined,
                new UserProfile("Ada", null, null, null, null, null, null, null, null, null, null, null, null, null, Now),
                Guid.NewGuid(),
                UserAccess.System,
                Tags(settingsTag),
                null);
        }

        /// <summary>The tenant-level tags of a round trip.</summary>
        /// <param name="settingsTag">The settings tag.</param>
        /// <returns>The tags.</returns>
        private static Dictionary<string, Guid> Tags(Guid settingsTag)
        {
            return new Dictionary<string, Guid>(StringComparer.Ordinal)
            {
                [VersionTagNames.Permissions] = PermissionsTag,
                [VersionTagNames.Settings] = settingsTag,
            };
        }

        /// <summary>Scripts a round trip whose guard passes.</summary>
        /// <param name="settingsTag">The settings tag it reads; the context's when null.</param>
        private void Pass(Guid? settingsTag = null)
        {
            Complete(guard: true, permissionsStale: false, settingsStale: false, settingsTag ?? SettingsA);
        }

        /// <summary>Scripts a round trip whose guard fails.</summary>
        /// <param name="permissionsStale">Whether the permissions read as stale.</param>
        /// <param name="settingsStale">Whether the settings read as stale.</param>
        /// <param name="settingsTag">The settings tag it reads; the context's when null.</param>
        private void Fail(bool permissionsStale = false, bool settingsStale = false, Guid? settingsTag = null)
        {
            Complete(guard: false, permissionsStale, settingsStale, settingsTag ?? SettingsA);
        }

        /// <summary>Scripts a round trip that completes the latest prologue result.</summary>
        /// <param name="guard">Whether the guard passed.</param>
        /// <param name="permissionsStale">Whether the permissions read as stale.</param>
        /// <param name="settingsStale">Whether the settings read as stale.</param>
        /// <param name="settingsTag">The settings tag it reads.</param>
        private void Complete(bool guard, bool permissionsStale, bool settingsStale, Guid settingsTag)
        {
            _steps.Enqueue(_ =>
            {
                _connection.Pending!.Complete(new ConnectResult(
                    7, UserKind.Individual, true, UserState.Joined, Guid.NewGuid(), Guid.NewGuid(), Tags(settingsTag),
                    permissionsStale, false, settingsStale, guard, null, null));
                return Task.CompletedTask;
            });
        }

        /// <summary>Runs a call that composes nothing of its own and reads the connected user.</summary>
        /// <param name="purpose">The purpose.</param>
        /// <returns>The connected user the call read under.</returns>
        private Task<ConnectedUser> RunAsync(BatchPurpose purpose)
        {
            return Runner().RunAsync(purpose, static (_, _) => ValueTask.CompletedTask, static (user, _) => user, TestContext.Current.CancellationToken);
        }

        /// <summary>The guard thrashes logged so far.</summary>
        /// <returns>The count.</returns>
        private int Thrashes()
        {
            return _logs.Collector.GetSnapshot().Count(static record => record.Id.Id == AccessEvents.GuardThrash);
        }

        /// <summary>The runner under test, over the fakes.</summary>
        /// <returns>The runner.</returns>
        private GuardedBatchRunner Runner()
        {
            IMeterFactory meters = _services.GetRequiredService<IMeterFactory>();
            return new GuardedBatchRunner(
                _connection,
                Database,
                _holder,
                [_locale, _early],
                new AccessMetrics(meters),
                _loggers,
                new VersionTagMetrics(meters, NullLogger<VersionTagMetrics>.Instance));
        }

        /// <summary>
        ///     A connection that records what the runner asks of it. Its prologue results are
        ///     completed by the scripted round trips; applying one carries the round trip's tags onto
        ///     a new connected user.
        /// </summary>
        private sealed class FakeConnection : IScopeConnection
        {
            /// <summary>The user the scope connected as.</summary>
            public ConnectedUser Connected { get; } = User(SettingsA);

            /// <summary>The user a cold re-connect returns.</summary>
            public ConnectedUser Reconnected { get; } = User(SettingsA) with { LastActivityStampedAt = Now };

            /// <summary>The latest prologue result handed out.</summary>
            public BatchResult<ConnectResult>? Pending { get; private set; }

            /// <summary>Every result applied, with the user it returned.</summary>
            public List<(ConnectResult Result, ConnectedUser User)> Applied { get; } = [];

            /// <summary>Every user a batch's premises were asked for.</summary>
            public List<ConnectedUser> Premised { get; } = [];

            /// <summary>Every user dropped.</summary>
            public List<ConnectedUser> Dropped { get; } = [];

            /// <summary>The calls to <see cref="CurrentAsync" />.</summary>
            public int Currents { get; private set; }

            /// <summary>The cold re-connects.</summary>
            public int Reconnects { get; private set; }

            /// <inheritdoc />
            public RequestContextInputs Inputs { get; } = new("ar", null, null, null, null, null);

            /// <inheritdoc />
            public Task<ConnectedUser> CurrentAsync(CancellationToken cancellationToken)
            {
                Currents++;
                return Task.FromResult(Connected);
            }

            /// <inheritdoc />
            public ConnectPremises PremisesFor(ConnectedUser user)
            {
                Premised.Add(user);
                return new ConnectPremises.ForUser(
                    user.UserId, new ConnectExpectations(user.TenantTags[VersionTagNames.Permissions], Guid.NewGuid(), user.PreferencesTag, user.TenantTags[VersionTagNames.Settings]), false);
            }

            /// <inheritdoc />
            public BatchResult<ConnectResult> Contribute(IDataBatch batch, ConnectPremises premises)
            {
                Pending = new BatchResult<ConnectResult>();
                return Pending;
            }

            /// <inheritdoc />
            public ConnectedUser Apply(ConnectResult result, ConnectedUser basis)
            {
                ConnectedUser applied = basis with { TenantTags = result.TenantTags };
                Applied.Add((result, applied));
                return applied;
            }

            /// <inheritdoc />
            public Task<ConnectedUser> ReconnectColdAsync(CancellationToken cancellationToken)
            {
                Reconnects++;
                return Task.FromResult(Reconnected);
            }

            /// <inheritdoc />
            public void Drop(ConnectedUser user)
            {
                Dropped.Add(user);
            }
        }

        /// <summary>The scope's context, a tenant's with its settings under the first tag.</summary>
        private sealed class Holder : IRequestContextHolder
        {
            /// <inheritdoc />
            public RequestContext Current { get; private set; } = RequestContext.Tenantless(new GregorianCalendarSystem(), Now) with
            {
                Tenant = new TenantDescriptor(1, "Acme", null, null, ["en"], TenantCategory.Live, TenantState.Active, null),
                UserId = 7,
                TenantSettings = new TenantSettings
                {
                    TenantId = 1,
                    Tag = new VersionTag(SettingsA),
                    Names = ["Acme", null, null],
                    Languages = [new LanguageInfo("en", "English", "E", false)],
                    Shape = MultilingualShape.Primary,
                    Calendars = [],
                    TimeZone = TimeZoneInfo.Utc,
                    SqlServerTimeZoneName = "UTC",
                    ModifiedAt = Now,
                    Entries = new Dictionary<string, JsonElement>(),
                },
            };

            /// <inheritdoc />
            public void Set(RequestContext context)
            {
                Current = context;
            }
        }

        /// <summary>An initializer that records its runs and returns the context unchanged.</summary>
        /// <param name="order">Its order.</param>
        private sealed class RecordingInitializer(int order) : IRequestContextInitializer
        {
            /// <inheritdoc />
            public int Order => order;

            /// <summary>Its runs.</summary>
            public int Runs { get; private set; }

            /// <summary>The inputs of its latest run.</summary>
            public RequestContextInputs? Inputs { get; private set; }

            /// <inheritdoc />
            public Task<RequestContext> InitializeAsync(RequestContext context, RequestContextInputs inputs, CancellationToken cancellationToken)
            {
                Runs++;
                Inputs = inputs;
                return Task.FromResult(context);
            }
        }

        /// <summary>A tenant database whose batches are the script's.</summary>
        /// <param name="script">The script.</param>
        private sealed class ScriptedDatabase(BatchScript script) : ITenantDatabase
        {
            /// <inheritdoc />
            public QueryexSchema Schema => throw new NotSupportedException();

            /// <inheritdoc />
            public QueryexContextValues Context => throw new NotSupportedException();

            /// <inheritdoc />
            public IDataBatch CreateBatch(BatchPurpose purpose)
            {
                return script.NewBatch(purpose);
            }
        }
    }
}

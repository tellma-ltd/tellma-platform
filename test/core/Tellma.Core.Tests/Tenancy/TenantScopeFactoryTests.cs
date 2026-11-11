// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Tenancy;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Tenancy
{
    /// <summary>
    ///     The tenant scope factory of background work: the context it restores from a snapshot, the
    ///     initializers it runs, the states it serves with and without the maintenance door, its
    ///     refusals before a scope exists, the scope it disposes when it fails, its counter, and the
    ///     services of the scope that read the bound tenant.
    /// </summary>
    public class TenantScopeFactoryTests
    {
        private const string TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

        private static readonly RequestContextSnapshot UserWork = new(17, PrincipalKind.User, "sub-1", 5, "worker", TraceParent);

        private static readonly RequestContextSnapshot SystemWork = new(17, PrincipalKind.System, "system", null, "worker", null);

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        // --- The context a scope starts with ---

        [Fact]
        public async Task A_scope_restores_the_snapshot_and_binds_the_tenant_as_it_is_now()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(
                [FakeCatalogReader.Row(17, name: "Acme", category: "Sandbox")],
                static services => services.AddConnectingInitializer());
            harness.Time.Advance(TimeSpan.FromHours(20));
            await harness.Registry.RefreshAsync(force: false, Token);

            await using TenantScope scope = await harness.Scopes.CreateScopeAsync(UserWork, cancellationToken: Token);

            RequestContext context = scope.Services.GetRequiredService<IRequestContextAccessor>().Current;

            // --- The principal, the client and the trace parent, from the snapshot ---
            Assert.Equal((PrincipalKind.User, "sub-1", 5, "worker", TraceParent), (context.Kind, context.Subject, context.UserId, context.Client, context.OriginTraceParent));
            Assert.Null(context.ClientId);
            Assert.Null(context.SessionId);
            Assert.Null(context.SessionHandle);
            Assert.Null(context.Assurance);

            // --- The tenant, from the registry ---
            Assert.Equal(harness.Registry.Get(17).Descriptor, context.Tenant);
            Assert.True(context.IsSandbox);

            // --- The clock, read when the scope was created ---
            Assert.Equal(TenantContextHarness.Start + TimeSpan.FromHours(20), context.Now);
            Assert.Equal(new DateOnly(2026, 11, 10), context.Today);

            // --- No locale travels in a snapshot: the platform's defaults until an initializer negotiates ---
            Assert.Equal(("en", "en", "gc", "UTC", "UTC"), (context.Language, context.Culture, context.Calendar, context.TimeZone, context.TenantTimeZone));
        }

        [Fact]
        public async Task The_initializers_run_in_order_with_empty_inputs_inside_the_bound_scope()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(
                [FakeCatalogReader.Row(17)],
                static services => services
                    .AddInitializer("negotiate", 200, static context => context with { Language = "ar", TenantTimeZone = "Asia/Riyadh" })
                    .AddInitializer("connect", 100, static context => context with { UserId = context.UserId ?? 1 }));

            await using TenantScope scope = await harness.Scopes.CreateScopeAsync(SystemWork, cancellationToken: Token);

            IReadOnlyList<InitializerCall> calls = harness.Services.GetRequiredService<InitializerLog>().Calls;
            Assert.Equal(["connect", "negotiate"], calls.Select(static call => call.Name));
            Assert.All(calls, static call => Assert.Same(RequestContextInputs.Empty, call.Inputs));
            Assert.All(calls, static call => Assert.Equal(Token, call.CancellationToken));

            // --- The holder is bound before the first one runs, so its scoped services see the tenant ---
            Assert.All(calls, static call => Assert.Same(call.Context, call.Ambient));
            Assert.Equal((17, PrincipalKind.System, "system", null), (calls[0].Context.Tenant!.Id, calls[0].Context.Kind, calls[0].Context.Subject, calls[0].Context.UserId));
            Assert.Equal(1, calls[1].Context.UserId);

            // --- The scope's context is the last one's result ---
            RequestContext context = scope.Services.GetRequiredService<IRequestContextAccessor>().Current;
            Assert.Equal((1, "ar", "Asia/Riyadh"), (context.UserId, context.Language, context.TenantTimeZone));
        }

        [Fact]
        public async Task A_snapshot_that_names_its_user_needs_no_initializer_to_bind_one()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync([FakeCatalogReader.Row(17)]);

            await using TenantScope scope = await harness.Scopes.CreateScopeAsync(UserWork, cancellationToken: Token);

            Assert.Equal(5, scope.Services.GetRequiredService<IRequestContextAccessor>().Current.UserId);
        }

        [Fact]
        public async Task Each_scope_is_its_own_and_ends_with_its_services()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(
                [FakeCatalogReader.Row(17), FakeCatalogReader.Row(18)],
                static services => services.AddScoped<DisposalProbe>().AddSingleton<DisposalLog>());

            TenantScope one = await harness.Scopes.CreateScopeAsync(UserWork, cancellationToken: Token);
            TenantScope other = await harness.Scopes.CreateScopeAsync(UserWork with { TenantId = 18 }, cancellationToken: Token);
            DisposalProbe probe = one.Services.GetRequiredService<DisposalProbe>();

            Assert.NotSame(probe, other.Services.GetRequiredService<DisposalProbe>());
            Assert.Equal(17, one.Services.GetRequiredService<IRequestContextAccessor>().Current.Tenant!.Id);
            Assert.Equal(18, other.Services.GetRequiredService<IRequestContextAccessor>().Current.Tenant!.Id);

            await one.DisposeAsync();

            Assert.Equal(1, harness.Services.GetRequiredService<DisposalLog>().Disposed);
            await other.DisposeAsync();
            Assert.Equal(2, harness.Services.GetRequiredService<DisposalLog>().Disposed);
        }

        // --- The states a scope is created for ---

        [Theory]
        [InlineData("Active", false, null)]
        [InlineData("ReadOnly", false, null)]
        [InlineData("Provisioning", false, "tenant-not-found")]
        [InlineData("Frozen", false, "tenant-frozen")]
        [InlineData("Suspended", false, "tenant-suspended")]
        [InlineData("Retired", false, "tenant-not-found")]
        [InlineData("Active", true, null)]
        [InlineData("ReadOnly", true, null)]
        [InlineData("Provisioning", true, null)]
        [InlineData("Frozen", true, null)]
        [InlineData("Suspended", true, "tenant-suspended")]
        [InlineData("Retired", true, "tenant-not-found")]
        public async Task A_scope_is_created_only_for_a_state_that_serves_background_work(string state, bool allowNonActive, string? refusal)
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(
                [FakeCatalogReader.Row(17, state)],
                static services => services.AddInitializer("connect", 100, static context => context with { UserId = 1 }));
            using MetricCollector<long> scopes = ScopesCounter(harness);
            TenantState expected = Enum.Parse<TenantState>(state);

            if (refusal is null)
            {
                await using TenantScope scope = await harness.Scopes.CreateScopeAsync(SystemWork, allowNonActive, Token);

                Assert.Equal(expected, scope.Services.GetRequiredService<IRequestContextAccessor>().Current.Tenant!.State);
                Assert.Equal(["background"], Kinds(scopes));
            }
            else
            {
                TenantUnavailableException refused = await Assert.ThrowsAsync<TenantUnavailableException>(
                    () => harness.Scopes.CreateScopeAsync(SystemWork, allowNonActive, Token));

                Assert.Equal((refusal, 17, (TenantState?)expected, null), (refused.Code, refused.TenantId, refused.State, refused.RetryAfter));
                Assert.Empty(Kinds(scopes));
                Assert.Empty(harness.Services.GetRequiredService<InitializerLog>().Calls);
            }
        }

        [Fact]
        public async Task A_state_that_changed_since_the_work_was_enqueued_is_seen()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync([FakeCatalogReader.Row(17)]);
            await using (await harness.Scopes.CreateScopeAsync(UserWork, cancellationToken: Token))
            {
            }

            harness.Catalog.Update(FakeCatalogReader.Row(17, "Suspended"));
            await harness.Registry.RefreshAsync(force: false, Token);

            TenantUnavailableException refused = await Assert.ThrowsAsync<TenantUnavailableException>(
                () => harness.Scopes.CreateScopeAsync(UserWork, cancellationToken: Token));
            Assert.Equal(TenantState.Suspended, refused.State);
        }

        // --- Refusals before a scope exists ---

        [Theory]
        [InlineData(PrincipalKind.User)]
        [InlineData(PrincipalKind.ServiceAccount)]
        public async Task Only_the_platforms_own_principal_may_open_the_maintenance_door(PrincipalKind kind)
        {
            // Even for an active tenant, and before anything is looked up or created.
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(
                [FakeCatalogReader.Row(17)],
                static services => services.AddConnectingInitializer().AddScoped<DisposalProbe>().AddSingleton<DisposalLog>());

            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => harness.Scopes.CreateScopeAsync(UserWork with { Kind = kind }, allowNonActive: true, Token));

            Assert.Contains($"the snapshot's kind is {kind}", failure.Message, StringComparison.Ordinal);
            Assert.Empty(harness.Services.GetRequiredService<InitializerLog>().Calls);

            // The same refusal for a tenant that does not exist: the door is checked first.
            _ = await Assert.ThrowsAsync<InvalidOperationException>(
                () => harness.Scopes.CreateScopeAsync(UserWork with { Kind = kind, TenantId = 99 }, allowNonActive: true, Token));
        }

        [Fact]
        public async Task Background_work_always_runs_for_a_principal()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync([FakeCatalogReader.Row(17)]);

            _ = await Assert.ThrowsAsync<ArgumentException>(
                () => harness.Scopes.CreateScopeAsync(UserWork with { Kind = PrincipalKind.Anonymous }, cancellationToken: Token));
            _ = await Assert.ThrowsAsync<ArgumentNullException>(() => harness.Scopes.CreateScopeAsync(null!, cancellationToken: Token));
        }

        [Fact]
        public async Task A_tenant_the_catalog_does_not_hold_is_refused_as_a_retired_one_is()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(
                [FakeCatalogReader.Row(17)],
                static services => services.AddConnectingInitializer());
            using MetricCollector<long> scopes = ScopesCounter(harness);

            // --- An id no tenant ever had ---
            TenantUnavailableException missing = await Assert.ThrowsAsync<TenantUnavailableException>(
                () => harness.Scopes.CreateScopeAsync(UserWork with { TenantId = 99 }, cancellationToken: Token));

            Assert.Equal((99, "tenant-not-found"), (missing.TenantId, missing.Code));
            Assert.Null(missing.State);
            Assert.Null(missing.RetryAfter);

            // --- A tenant that left the catalog after its work was enqueued ---
            harness.Catalog.Remove(17);
            await harness.Registry.RefreshAsync(force: false, Token);

            TenantUnavailableException removed = await Assert.ThrowsAsync<TenantUnavailableException>(
                () => harness.Scopes.CreateScopeAsync(UserWork, cancellationToken: Token));

            // Never the membership refusal's type: a caller reads that one as "the run-as user is
            // inactive" and fails the work instead of leaving it to lapse.
            Assert.Equal((17, "tenant-not-found"), (removed.TenantId, removed.Code));
            Assert.Null(removed.State);
            Assert.Empty(harness.Services.GetRequiredService<InitializerLog>().Calls);
            Assert.Empty(Kinds(scopes));
        }

        [Fact]
        public async Task A_stale_catalog_serves_no_background_work()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync([FakeCatalogReader.Row(17)]);

            // Past the staleness bound with no refresh in between.
            harness.Time.Advance(TimeSpan.FromHours(1));

            TenantUnavailableException stale = await Assert.ThrowsAsync<TenantUnavailableException>(
                () => harness.Scopes.CreateScopeAsync(UserWork, cancellationToken: Token));

            Assert.Equal("catalog-unavailable", stale.Code);
        }

        [Fact]
        public async Task A_cancelled_caller_gets_no_scope()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync([FakeCatalogReader.Row(17)]);
            using MetricCollector<long> scopes = ScopesCounter(harness);
            using CancellationTokenSource cancelled = new();
            await cancelled.CancelAsync();

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Scopes.CreateScopeAsync(UserWork, cancellationToken: cancelled.Token));

            Assert.Empty(Kinds(scopes));
        }

        // --- A scope that fails while it is completed ---

        [Fact]
        public async Task A_scope_whose_user_is_not_a_member_is_disposed_and_never_handed_out()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(
                [FakeCatalogReader.Row(17)],
                static services => services
                    .AddScoped<DisposalProbe>()
                    .AddSingleton<DisposalLog>()
                    .AddScoped<IRequestContextInitializer, ProbingRefuser>());
            using MetricCollector<long> scopes = ScopesCounter(harness);

            TenantNotFoundException refused = await Assert.ThrowsAsync<TenantNotFoundException>(
                () => harness.Scopes.CreateScopeAsync(UserWork, cancellationToken: Token));

            Assert.Equal(17, refused.TenantId);
            DisposalLog log = harness.Services.GetRequiredService<DisposalLog>();
            Assert.Equal((1, 1), (log.Created, log.Disposed));
            Assert.Empty(Kinds(scopes));
        }

        [Fact]
        public async Task A_scope_no_user_was_bound_for_is_disposed_and_never_handed_out()
        {
            // A system snapshot names no user, and nothing here binds one.
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(
                [FakeCatalogReader.Row(17)],
                static services => services
                    .AddScoped<DisposalProbe>()
                    .AddSingleton<DisposalLog>()
                    .AddScoped<IRequestContextInitializer>(static provider => new ProbingStep(
                        provider.GetRequiredService<DisposalProbe>(), static (context, _) => context with { Language = "ar" })));
            using MetricCollector<long> scopes = ScopesCounter(harness);

            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => harness.Scopes.CreateScopeAsync(SystemWork, cancellationToken: Token));

            Assert.Contains("names no user and no request-context initializer bound one", failure.Message, StringComparison.Ordinal);
            DisposalLog log = harness.Services.GetRequiredService<DisposalLog>();
            Assert.Equal((1, 1), (log.Created, log.Disposed));
            Assert.Empty(Kinds(scopes));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task A_scope_cancelled_while_it_is_completed_is_disposed_and_never_handed_out(bool betweenSteps)
        {
            // The caller gives up while the first step runs: either the step itself observes the
            // token and throws, or it returns and the factory stops before the next step.
            using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(
                [FakeCatalogReader.Row(17)],
                services => services
                    .AddScoped<DisposalProbe>()
                    .AddSingleton<DisposalLog>()
                    .AddScoped<IRequestContextInitializer>(provider => new ProbingStep(
                        provider.GetRequiredService<DisposalProbe>(),
                        (context, token) =>
                        {
                            caller.Cancel();
                            if (!betweenSteps)
                            {
                                token.ThrowIfCancellationRequested();
                            }

                            return context with { UserId = InitializerRegistrations.UserId };
                        }))
                    .AddInitializer("negotiate", 200, static context => context with { Language = "ar" }));
            using MetricCollector<long> scopes = ScopesCounter(harness);

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => harness.Scopes.CreateScopeAsync(UserWork, cancellationToken: caller.Token));

            DisposalLog log = harness.Services.GetRequiredService<DisposalLog>();
            Assert.Equal((1, 1), (log.Created, log.Disposed));
            Assert.Empty(harness.Services.GetRequiredService<InitializerLog>().Calls);
            Assert.Empty(Kinds(scopes));
        }

        [Fact]
        public async Task An_initializer_that_rebinds_the_tenant_fails_the_scope()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(
                [FakeCatalogReader.Row(17), FakeCatalogReader.Row(18)],
                static services => services.AddInitializer("connect", 100, static context => context with { Tenant = TenantContextHarness.Tenant(id: 18) }));

            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => harness.Scopes.CreateScopeAsync(UserWork, cancellationToken: Token));

            Assert.Contains("changed the tenant, the principal or the user", failure.Message, StringComparison.Ordinal);
        }

        // --- What a scope starts, and counts ---

        [Fact]
        public async Task Creating_a_scope_starts_no_activity()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(
                [FakeCatalogReader.Row(17)],
                static services => services.AddConnectingInitializer());

            // A listener that samples every source, so any activity the factory started would be created and seen.
            List<(ActivityTraceId Trace, string Name)> started = [];
            using ActivityListener listener = new()
            {
                ShouldListenTo = static _ => true,
                Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = activity =>
                {
                    lock (started)
                    {
                        started.Add((activity.TraceId, activity.OperationName));
                    }
                },
            };
            ActivitySource.AddActivityListener(listener);

            using ActivitySource callers = new("Tellma.Core.Tests.Caller");
            using Activity? caller = callers.StartActivity("process job");
            Assert.NotNull(caller);

            await using (TenantScope scope = await harness.Scopes.CreateScopeAsync(UserWork, cancellationToken: Token))
            {
                // The scope runs under the caller's activity, and the snapshot's trace parent is data only.
                Assert.Same(caller, Activity.Current);
                Assert.Null(caller.ParentId);
                Assert.NotEqual("0af7651916cd43dd8448eb211c80319c", caller.TraceId.ToHexString());
                Assert.Empty(caller.Links);
                Assert.Equal(TraceParent, scope.Services.GetRequiredService<IRequestContextAccessor>().Current.OriginTraceParent);
            }

            // An activity started inside the caller's flow joins the caller's trace; tests running beside
            // this one start theirs in other traces.
            lock (started)
            {
                Assert.Equal(["process job"], started.Where(entry => entry.Trace == caller.TraceId).Select(static entry => entry.Name));
            }
        }

        [Fact]
        public async Task Every_scope_created_is_counted_as_background_and_carries_no_tenant()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync([FakeCatalogReader.Row(17), FakeCatalogReader.Row(18)]);
            using MetricCollector<long> scopes = ScopesCounter(harness);

            await using TenantScope one = await harness.Scopes.CreateScopeAsync(UserWork, cancellationToken: Token);
            await using TenantScope other = await harness.Scopes.CreateScopeAsync(UserWork with { TenantId = 18 }, cancellationToken: Token);

            Assert.Equal(["background", "background"], Kinds(scopes));
        }

        // --- The services of a scope that read the bound tenant ---

        [Fact]
        public async Task The_sandbox_flag_of_a_scope_is_its_tenants_category()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(
                [FakeCatalogReader.Row(17), FakeCatalogReader.Row(18, category: "Sandbox")]);

            await using TenantScope live = await harness.Scopes.CreateScopeAsync(UserWork, cancellationToken: Token);
            await using TenantScope sandbox = await harness.Scopes.CreateScopeAsync(UserWork with { TenantId = 18 }, cancellationToken: Token);

            Assert.False(live.Services.GetRequiredService<ISandboxContext>().IsSandbox);
            Assert.True(sandbox.Services.GetRequiredService<ISandboxContext>().IsSandbox);
        }

        [Fact]
        public async Task The_connection_provider_of_a_scope_reaches_its_tenant_alone()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(
                [FakeCatalogReader.Row(17, database: "tenant-seventeen"), FakeCatalogReader.Row(18, database: "tenant-eighteen")]);

            await using TenantScope scope = await harness.Scopes.CreateScopeAsync(UserWork, cancellationToken: Token);
            ITenantConnectionProvider provider = scope.Services.GetRequiredService<ITenantConnectionProvider>();

            Assert.Same(harness.Registry.Get(17), provider.Tenant);
            Assert.Contains("Initial Catalog=tenant-seventeen", provider.Tenant.ConnectionString, StringComparison.Ordinal);
            Assert.DoesNotContain("tenant-eighteen", provider.Tenant.ConnectionString, StringComparison.Ordinal);
        }

        /// <summary>Collects the scopes counter.</summary>
        /// <param name="harness">The harness.</param>
        /// <returns>The collector.</returns>
        private static MetricCollector<long> ScopesCounter(TenantContextHarness harness)
        {
            return new MetricCollector<long>(harness.MeterFactory, TenancyTelemetryNames.MeterName, TenancyTelemetryNames.Scopes);
        }

        /// <summary>The kind of every measurement collected, each a count of one with no other tag.</summary>
        /// <param name="scopes">The collector.</param>
        /// <returns>The kinds, in order.</returns>
        private static string[] Kinds(MetricCollector<long> scopes)
        {
            return [.. scopes.GetMeasurementSnapshot().Select(static measurement =>
            {
                Assert.Equal(1, measurement.Value);
                KeyValuePair<string, object?> tag = Assert.Single(measurement.Tags);
                Assert.Equal(TenancyTelemetryNames.KindTag, tag.Key);
                return (string)tag.Value!;
            })];
        }

        /// <summary>Counts the scoped probes created and disposed.</summary>
        private sealed class DisposalLog
        {
            private int _created;
            private int _disposed;

            /// <summary>How many probes were created.</summary>
            public int Created => Volatile.Read(ref _created);

            /// <summary>How many probes were disposed.</summary>
            public int Disposed => Volatile.Read(ref _disposed);

            /// <summary>Counts a creation.</summary>
            public void OnCreated()
            {
                _ = Interlocked.Increment(ref _created);
            }

            /// <summary>Counts a disposal.</summary>
            public void OnDisposed()
            {
                _ = Interlocked.Increment(ref _disposed);
            }
        }

        /// <summary>A scoped service whose life the log counts: it ends when its scope does.</summary>
        private sealed class DisposalProbe : IAsyncDisposable
        {
            private readonly DisposalLog _log;

            /// <summary>Creates the probe and counts it.</summary>
            /// <param name="log">The log.</param>
            public DisposalProbe(DisposalLog log)
            {
                _log = log;
                log.OnCreated();
            }

            /// <inheritdoc />
            public ValueTask DisposeAsync()
            {
                _log.OnDisposed();
                return ValueTask.CompletedTask;
            }
        }

        /// <summary>An initializer at the connection's order that resolves a scoped probe, then runs a script.</summary>
        /// <param name="probe">The scope's probe, created by resolving this initializer.</param>
        /// <param name="script">Turns the context and the token given into the context returned, or throws.</param>
        private sealed class ProbingStep(DisposalProbe probe, Func<RequestContext, CancellationToken, RequestContext> script)
            : IRequestContextInitializer
        {
            /// <inheritdoc />
            public int Order => 100;

            /// <inheritdoc />
            public Task<RequestContext> InitializeAsync(RequestContext context, RequestContextInputs inputs, CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(probe);
                return Task.FromResult(script(context, cancellationToken));
            }
        }

        /// <summary>An initializer that resolves a scoped probe, then refuses the user as not a member.</summary>
        /// <param name="probe">The scope's probe, created by resolving this initializer.</param>
        private sealed class ProbingRefuser(DisposalProbe probe) : IRequestContextInitializer
        {
            /// <inheritdoc />
            public int Order => 100;

            /// <inheritdoc />
            public Task<RequestContext> InitializeAsync(RequestContext context, RequestContextInputs inputs, CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(probe);
                throw new TenantNotFoundException(context.RequireTenant().Id);
            }
        }
    }
}

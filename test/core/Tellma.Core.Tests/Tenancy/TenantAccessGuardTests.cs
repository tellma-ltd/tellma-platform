// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Globalization;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Tests.Tenancy
{
    /// <summary>
    ///     The access guard of a tenant request: the verdict of every state for reads, mutations and
    ///     mutations allowed when read-only; the initializers in order; the bound user; the assurance
    ///     bar; and the outcome each verdict counts.
    /// </summary>
    public class TenantAccessGuardTests
    {
        private static readonly RequestContextInputs Inputs = new("ar-SA,en;q=0.8", "uq", "Asia/Riyadh", "web", null, null);

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        // --- Step 1: the state verdict ---

        [Theory]
        [InlineData(TenantState.Active, false, false)]
        [InlineData(TenantState.Active, true, false)]
        [InlineData(TenantState.Active, true, true)]
        [InlineData(TenantState.ReadOnly, false, false)]
        [InlineData(TenantState.ReadOnly, false, true)]
        [InlineData(TenantState.ReadOnly, true, true)]
        [InlineData(TenantState.Frozen, false, false)]
        [InlineData(TenantState.Frozen, false, true)]
        public async Task A_state_that_serves_the_request_lets_a_member_through(TenantState state, bool isMutation, bool allowWhenReadOnly)
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(configure: static services => services.AddConnectingInitializer());
            using MetricCollector<long> resolutions = Resolutions(harness);
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant(state));

            await Guard(scope).EnsureAccessAsync(new TenantAccessRequirement(isMutation, null, allowWhenReadOnly), Inputs, Token);

            Assert.Equal(InitializerRegistrations.UserId, Current(scope).UserId);
            Assert.Equal(["served"], Outcomes(resolutions));
        }

        [Theory]
        [InlineData(TenantState.ReadOnly, true, false, "tenant-read-only", "read_only")]
        [InlineData(TenantState.Frozen, true, false, "tenant-frozen", "frozen")]
        [InlineData(TenantState.Frozen, true, true, "tenant-frozen", "frozen")]
        [InlineData(TenantState.Suspended, false, false, "tenant-suspended", "suspended")]
        [InlineData(TenantState.Suspended, false, true, "tenant-suspended", "suspended")]
        [InlineData(TenantState.Suspended, true, false, "tenant-suspended", "suspended")]
        [InlineData(TenantState.Suspended, true, true, "tenant-suspended", "suspended")]
        public async Task A_state_that_refuses_the_request_says_which_before_any_initializer_runs(
            TenantState state, bool isMutation, bool allowWhenReadOnly, string code, string outcome)
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(configure: static services => services.AddConnectingInitializer());
            using MetricCollector<long> resolutions = Resolutions(harness);
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant(state));

            TenantUnavailableException refused = await Assert.ThrowsAsync<TenantUnavailableException>(
                () => Guard(scope).EnsureAccessAsync(new TenantAccessRequirement(isMutation, null, allowWhenReadOnly), Inputs, Token));

            Assert.Equal((code, 17, (TenantState?)state, null), (refused.Code, refused.TenantId, refused.State, refused.RetryAfter));
            Assert.Equal([outcome], Outcomes(resolutions));

            // The verdict needs no tenant database: membership is verified only in a state that serves.
            Assert.Empty(harness.Services.GetRequiredService<InitializerLog>().Calls);
            Assert.Null(Current(scope).UserId);
        }

        [Theory]
        [InlineData(TenantState.Provisioning, false, "provisioning")]
        [InlineData(TenantState.Provisioning, true, "provisioning")]
        [InlineData(TenantState.Retired, false, "not_found")]
        [InlineData(TenantState.Retired, true, "not_found")]
        public async Task A_state_a_request_sees_as_absent_answers_not_found(TenantState state, bool isMutation, string outcome)
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(configure: static services => services.AddConnectingInitializer());
            using MetricCollector<long> resolutions = Resolutions(harness);
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant(state));

            TenantNotFoundException refused = await Assert.ThrowsAsync<TenantNotFoundException>(
                () => Guard(scope).EnsureAccessAsync(new TenantAccessRequirement(isMutation, null), Inputs, Token));

            Assert.Equal(17, refused.TenantId);
            Assert.Equal([outcome], Outcomes(resolutions));
            Assert.Empty(harness.Services.GetRequiredService<InitializerLog>().Calls);
        }

        // --- Step 2: the initializers ---

        [Fact]
        public async Task The_initializers_run_in_order_each_on_the_result_of_the_one_before()
        {
            // Registered out of order, two of them at the same order.
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(configure: static services => services
                .AddInitializer("negotiate", 200, static context => context with { Language = "ar", TimeZone = "Asia/Riyadh" })
                .AddInitializer("connect", 100, static context => context with { UserId = 5 })
                .AddInitializer("tags", 100, static context => context with { ClientId = "spa" }));
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant());
            RequestContext bound = Current(scope);

            await Guard(scope).EnsureAccessAsync(new TenantAccessRequirement(false, null), Inputs, Token);

            IReadOnlyList<InitializerCall> calls = harness.Services.GetRequiredService<InitializerLog>().Calls;
            Assert.Equal(["connect", "tags", "negotiate"], calls.Select(static call => call.Name));

            // --- Each receives the context so far, and its scope's accessor already answers it ---
            Assert.Same(bound, calls[0].Context);
            Assert.Equal((5, null), (calls[1].Context.UserId, calls[1].Context.ClientId));
            Assert.Equal((5, "spa", "en"), (calls[2].Context.UserId, calls[2].Context.ClientId, calls[2].Context.Language));
            Assert.All(calls, static call => Assert.Same(call.Context, call.Ambient));

            // --- The request's inputs and token reach every one unchanged ---
            Assert.All(calls, static call => Assert.Same(Inputs, call.Inputs));
            Assert.All(calls, static call => Assert.Equal(Token, call.CancellationToken));

            // --- The scope's context is the last one's result ---
            RequestContext completed = Current(scope);
            Assert.Equal((5, "spa", "ar", "Asia/Riyadh"), (completed.UserId, completed.ClientId, completed.Language, completed.TimeZone));
            Assert.Equal((bound.Tenant, bound.Kind, bound.Subject, bound.Now), (completed.Tenant, completed.Kind, completed.Subject, completed.Now));
        }

        [Fact]
        public async Task A_caller_no_initializer_bound_a_user_for_is_never_let_through()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(
                configure: static services => services.AddInitializer("negotiate", 200, static context => context with { Language = "ar" }));
            using MetricCollector<long> resolutions = Resolutions(harness);
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant());

            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Guard(scope).EnsureAccessAsync(new TenantAccessRequirement(false, null), Inputs, Token));

            Assert.Contains("bound the caller's user", failure.Message, StringComparison.Ordinal);
            Assert.Empty(Outcomes(resolutions));
        }

        [Fact]
        public async Task A_composition_without_initializers_lets_nobody_through()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync();
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant());

            _ = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Guard(scope).EnsureAccessAsync(new TenantAccessRequirement(false, null), Inputs, Token));
        }

        [Fact]
        public async Task A_caller_who_is_not_a_member_answers_not_found_and_no_later_initializer_runs()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(configure: static services => services
                .AddInitializer("connect", 100, static context => throw new TenantNotFoundException(context.RequireTenant().Id))
                .AddInitializer("negotiate", 200, static context => context));
            using MetricCollector<long> resolutions = Resolutions(harness);
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant());

            TenantNotFoundException refused = await Assert.ThrowsAsync<TenantNotFoundException>(
                () => Guard(scope).EnsureAccessAsync(new TenantAccessRequirement(false, null), Inputs, Token));

            Assert.Equal(17, refused.TenantId);
            Assert.Equal(["connect"], harness.Services.GetRequiredService<InitializerLog>().Names);
            Assert.Equal(["not_member"], Outcomes(resolutions));
        }

        [Fact]
        public async Task A_catalog_that_went_stale_under_an_initializer_is_counted_as_such()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(configure: static services => services
                .AddInitializer("connect", 100, static context => throw new TenantUnavailableException(
                    context.RequireTenant().Id, null, TenantProblemCodes.CatalogUnavailable, TimeSpan.FromSeconds(15))));
            using MetricCollector<long> resolutions = Resolutions(harness);
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant());

            TenantUnavailableException refused = await Assert.ThrowsAsync<TenantUnavailableException>(
                () => Guard(scope).EnsureAccessAsync(new TenantAccessRequirement(false, null), Inputs, Token));

            Assert.Equal("catalog-unavailable", refused.Code);
            Assert.Equal(["catalog_unavailable"], Outcomes(resolutions));
        }

        [Fact]
        public async Task A_failure_that_is_no_tenancy_verdict_passes_through_uncounted()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(configure: static services => services
                .AddInitializer("connect", 100, static context => throw new TenantUnavailableException(
                    context.RequireTenant().Id, null, TenantProblemCodes.SchemaBehind, TimeSpan.FromSeconds(30))));
            using MetricCollector<long> resolutions = Resolutions(harness);
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant());

            TenantUnavailableException refused = await Assert.ThrowsAsync<TenantUnavailableException>(
                () => Guard(scope).EnsureAccessAsync(new TenantAccessRequirement(false, null), Inputs, Token));

            Assert.Equal("tenant-schema-behind", refused.Code);
            Assert.Empty(Outcomes(resolutions));
        }

        [Fact]
        public async Task An_initializer_that_returns_no_context_is_a_defect()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(
                configure: static services => services.AddInitializer("connect", 100, static _ => null!));
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant());

            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Guard(scope).EnsureAccessAsync(new TenantAccessRequirement(false, null), Inputs, Token));

            Assert.Contains("ScriptedInitializer returned no context", failure.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("tenant")]
        [InlineData("no-tenant")]
        [InlineData("kind")]
        [InlineData("subject")]
        public async Task An_initializer_that_rebinds_the_tenant_or_the_principal_is_refused(string change)
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(configure: services => services
                .AddInitializer("connect", 100, context => change switch
                {
                    "tenant" => context with { UserId = 5, Tenant = TenantContextHarness.Tenant(id: 18) },
                    "no-tenant" => context with { UserId = 5, Tenant = null },
                    "kind" => context with { UserId = 5, Kind = PrincipalKind.System },
                    _ => context with { UserId = 5, Subject = "sub-2" },
                }));
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant());
            RequestContext bound = Current(scope);

            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Guard(scope).EnsureAccessAsync(new TenantAccessRequirement(false, null), Inputs, Token));

            Assert.Contains("changed the tenant, the principal or the user", failure.Message, StringComparison.Ordinal);
            Assert.Same(bound, Current(scope));
        }

        [Fact]
        public async Task An_initializer_that_replaces_the_connected_user_is_refused()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(configure: static services => services
                .AddConnectingInitializer()
                .AddInitializer("impersonate", 200, static context => context with { UserId = InitializerRegistrations.UserId + 1 }));
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant());

            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Guard(scope).EnsureAccessAsync(new TenantAccessRequirement(false, null), Inputs, Token));

            Assert.Contains("ScriptedInitializer changed the tenant, the principal or the user", failure.Message, StringComparison.Ordinal);
            Assert.Equal(InitializerRegistrations.UserId, Current(scope).UserId);
            Assert.Equal(["connect", "impersonate"], harness.Services.GetRequiredService<InitializerLog>().Names);
        }

        [Fact]
        public async Task An_initializer_that_drops_the_connected_user_is_refused()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(configure: static services => services
                .AddConnectingInitializer()
                .AddInitializer("disconnect", 200, static context => context with { UserId = null }));
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant());

            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Guard(scope).EnsureAccessAsync(new TenantAccessRequirement(false, null), Inputs, Token));

            Assert.Contains("changed the tenant, the principal or the user", failure.Message, StringComparison.Ordinal);
            Assert.Equal(InitializerRegistrations.UserId, Current(scope).UserId);
        }

        [Fact]
        public async Task A_culture_an_initializer_sets_never_reaches_the_caller_which_applies_the_completed_context()
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            CultureInfo originalUi = CultureInfo.CurrentUICulture;

            // A culture the test's own flow is surely not running under.
            var negotiated = CultureInfo.GetCultureInfo(original.Name == "ar-SA" || originalUi.Name == "ar-SA" ? "fr-FR" : "ar-SA");
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(configure: services => services
                .AddConnectingInitializer()
                .AddInitializer("negotiate", 200, context =>
                {
                    // What a step must not rely on: the current cultures flow down, never back up.
                    CultureInfo.CurrentCulture = negotiated;
                    CultureInfo.CurrentUICulture = negotiated;
                    return context with { Culture = negotiated.Name, CultureInfo = negotiated };
                }));
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant());

            try
            {
                await Guard(scope).EnsureAccessAsync(new TenantAccessRequirement(false, null), Inputs, Token);

                // --- The step's ambient change ended with the guard's call ---
                Assert.Equal((original.Name, originalUi.Name), (CultureInfo.CurrentCulture.Name, CultureInfo.CurrentUICulture.Name));

                // --- The front door applies the completed context, and what it then awaits runs under it ---
                Current(scope).ApplyCultures();
                (string culture, string uiCulture) = await CurrentCulturesAsync();

                Assert.Equal((negotiated.Name, negotiated.Name), (culture, uiCulture));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
                CultureInfo.CurrentUICulture = originalUi;
            }
        }

        [Fact]
        public async Task A_cancelled_request_stops_before_its_initializers()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(configure: static services => services.AddConnectingInitializer());
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant());
            using CancellationTokenSource cancelled = new();
            await cancelled.CancelAsync();

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => Guard(scope).EnsureAccessAsync(new TenantAccessRequirement(false, null), Inputs, cancelled.Token));

            Assert.Empty(harness.Services.GetRequiredService<InitializerLog>().Calls);
        }

        // --- What the front door must have bound ---

        [Theory]
        [InlineData(TenantState.Active)]
        [InlineData(TenantState.Suspended)]
        [InlineData(TenantState.Frozen)]
        public async Task An_anonymous_caller_learns_nothing_about_the_tenant(TenantState state)
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(configure: static services => services.AddConnectingInitializer());
            using MetricCollector<long> resolutions = Resolutions(harness);
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant(state), PrincipalKind.Anonymous, subject: null);

            // Not found even for a suspended tenant: the state's own refusal would confirm the tenant exists.
            _ = await Assert.ThrowsAsync<TenantNotFoundException>(
                () => Guard(scope).EnsureAccessAsync(new TenantAccessRequirement(true, null), Inputs, Token));

            Assert.Equal(["not_member"], Outcomes(resolutions));
            Assert.Empty(harness.Services.GetRequiredService<InitializerLog>().Calls);
        }

        [Fact]
        public async Task A_request_never_runs_as_the_platform_itself()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(configure: static services => services.AddConnectingInitializer());
            using MetricCollector<long> resolutions = Resolutions(harness);
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant(), PrincipalKind.System, "system");

            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Guard(scope).EnsureAccessAsync(new TenantAccessRequirement(false, null), Inputs, Token));

            Assert.Contains("the platform's own principal", failure.Message, StringComparison.Ordinal);
            Assert.Empty(Outcomes(resolutions));
            Assert.Empty(harness.Services.GetRequiredService<InitializerLog>().Calls);
        }

        [Fact]
        public async Task The_guard_refuses_a_scope_no_front_door_bound_and_missing_arguments()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(configure: static services => services.AddConnectingInitializer());
            await using AsyncServiceScope unbound = harness.Services.CreateAsyncScope();
            await using AsyncServiceScope bound = harness.BindRequest(TenantContextHarness.Tenant());

            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Guard(unbound).EnsureAccessAsync(new TenantAccessRequirement(false, null), Inputs, Token));

            Assert.Contains("not bound to a tenant", failure.Message, StringComparison.Ordinal);
            _ = await Assert.ThrowsAsync<ArgumentNullException>(() => Guard(bound).EnsureAccessAsync(null!, Inputs, Token));
            _ = await Assert.ThrowsAsync<ArgumentNullException>(() => Guard(bound).EnsureAccessAsync(new TenantAccessRequirement(false, null), null!, Token));
        }

        // --- Step 3: the assurance bar ---

        [Theory]
        // No age on the bar: the class alone decides.
        [InlineData("mfa", null, null, null, true)]
        [InlineData("mfa", null, null, 600, true)]
        // An age on the bar counts from when the class was reached.
        [InlineData("mfa", 300, 299, null, true)]
        [InlineData("mfa", 300, 300, null, true)]
        [InlineData("mfa", 300, 301, null, false)]
        // The time the class was reached wins over the sign-in, in both directions.
        [InlineData("mfa", 300, 60, 7200, true)]
        [InlineData("mfa", 300, 7200, 60, false)]
        // Without it, the sign-in stands in.
        [InlineData("mfa", 300, null, 299, true)]
        [InlineData("mfa", 300, null, 301, false)]
        // A session that says neither cannot prove it is recent enough.
        [InlineData("mfa", 300, null, null, false)]
        // Another class, or none, never meets the bar, however recent.
        [InlineData("pwd", null, 1, 1, false)]
        [InlineData("MFA", 300, 1, 1, false)]
        [InlineData(null, null, 1, 1, false)]
        public async Task A_session_meets_the_bar_by_its_class_and_its_age(
            string? heldAcr, int? maxAgeSeconds, int? acrAgeSeconds, int? authAgeSeconds, bool meets)
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(configure: static services => services.AddConnectingInitializer());
            using MetricCollector<long> resolutions = Resolutions(harness);
            DateTimeOffset now = harness.Time.GetUtcNow();
            AuthenticationAssurance held = new(
                heldAcr,
                authAgeSeconds is { } authAge ? now - TimeSpan.FromSeconds(authAge) : null,
                acrAgeSeconds is { } acrAge ? now - TimeSpan.FromSeconds(acrAge) : null);
            RequireAssuranceMetadata bar = new("mfa", maxAgeSeconds is { } maxAge ? TimeSpan.FromSeconds(maxAge) : null);
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant(), assurance: held);

            Task ensure = Guard(scope).EnsureAccessAsync(new TenantAccessRequirement(true, bar), Inputs, Token);

            if (meets)
            {
                await ensure;
            }
            else
            {
                StepUpRequiredException stepUp = await Assert.ThrowsAsync<StepUpRequiredException>(() => ensure);
                Assert.Equal((bar.Acr, bar.MaxAge), (stepUp.Acr, stepUp.MaxAge));
            }

            // The tenant served its member either way: the bar is a verdict on the session.
            Assert.Equal(["served"], Outcomes(resolutions));
            Assert.Equal(InitializerRegistrations.UserId, Current(scope).UserId);
        }

        [Fact]
        public async Task A_session_that_asserts_no_assurance_at_all_is_asked_to_step_up()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(configure: static services => services.AddConnectingInitializer());
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant(), assurance: null);

            StepUpRequiredException stepUp = await Assert.ThrowsAsync<StepUpRequiredException>(
                () => Guard(scope).EnsureAccessAsync(new TenantAccessRequirement(false, new RequireAssuranceMetadata("mfa", null)), Inputs, Token));

            Assert.Equal(("mfa", null), (stepUp.Acr, stepUp.MaxAge));
        }

        [Fact]
        public async Task The_age_counts_against_the_instant_the_request_started()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(configure: static services => services.AddConnectingInitializer());
            AuthenticationAssurance held = new("mfa", null, harness.Time.GetUtcNow() - TimeSpan.FromSeconds(299));
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant(), assurance: held);

            // The clock moves on after the request was bound; the request's own instant still decides.
            harness.Time.Advance(TimeSpan.FromMinutes(10));

            await Guard(scope).EnsureAccessAsync(
                new TenantAccessRequirement(false, new RequireAssuranceMetadata("mfa", TimeSpan.FromSeconds(300))), Inputs, Token);
        }

        [Fact]
        public async Task A_service_account_cannot_step_up_and_is_refused_as_not_an_individual()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(configure: static services => services.AddConnectingInitializer());
            await using AsyncServiceScope barred = harness.BindRequest(TenantContextHarness.Tenant(), PrincipalKind.ServiceAccount, "client-1");
            await using AsyncServiceScope unbarred = harness.BindRequest(TenantContextHarness.Tenant(), PrincipalKind.ServiceAccount, "client-1");

            IndividualRequiredException refused = await Assert.ThrowsAsync<IndividualRequiredException>(
                () => Guard(barred).EnsureAccessAsync(new TenantAccessRequirement(false, new RequireAssuranceMetadata("mfa", null)), Inputs, Token));

            Assert.Equal("individual-required", refused.Code);

            // Without a bar the same service account is served.
            await Guard(unbarred).EnsureAccessAsync(new TenantAccessRequirement(false, null), Inputs, Token);
            Assert.Equal(InitializerRegistrations.UserId, Current(unbarred).UserId);
        }

        [Fact]
        public async Task A_service_account_that_is_not_a_member_answers_not_found_before_the_bar_is_looked_at()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(configure: static services => services
                .AddInitializer("connect", 100, static context => throw new TenantNotFoundException(context.RequireTenant().Id)));
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant(), PrincipalKind.ServiceAccount, "client-1");

            _ = await Assert.ThrowsAsync<TenantNotFoundException>(
                () => Guard(scope).EnsureAccessAsync(new TenantAccessRequirement(false, new RequireAssuranceMetadata("mfa", null)), Inputs, Token));
        }

        [Fact]
        public async Task A_suspended_tenant_refuses_before_the_bar_is_looked_at()
        {
            await using TenantContextHarness harness = await TenantContextHarness.CreateAsync(configure: static services => services.AddConnectingInitializer());
            await using AsyncServiceScope scope = harness.BindRequest(TenantContextHarness.Tenant(TenantState.Suspended), PrincipalKind.ServiceAccount, "client-1");

            TenantUnavailableException refused = await Assert.ThrowsAsync<TenantUnavailableException>(
                () => Guard(scope).EnsureAccessAsync(new TenantAccessRequirement(false, new RequireAssuranceMetadata("mfa", null)), Inputs, Token));

            Assert.Equal("tenant-suspended", refused.Code);
        }

        /// <summary>The guard of a scope.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The guard.</returns>
        private static ITenantAccessGuard Guard(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<ITenantAccessGuard>();
        }

        /// <summary>The current cultures as an awaited callee sees them, after it yielded.</summary>
        /// <returns>The names of the current culture and the current UI culture.</returns>
        private static async Task<(string Culture, string UiCulture)> CurrentCulturesAsync()
        {
            await Task.Yield();
            return (CultureInfo.CurrentCulture.Name, CultureInfo.CurrentUICulture.Name);
        }

        /// <summary>The context of a scope, as its services read it.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The context.</returns>
        private static RequestContext Current(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>().Current;
        }

        /// <summary>Collects the resolutions counter.</summary>
        /// <param name="harness">The harness.</param>
        /// <returns>The collector.</returns>
        private static MetricCollector<long> Resolutions(TenantContextHarness harness)
        {
            return new MetricCollector<long>(harness.MeterFactory, TenancyTelemetryNames.MeterName, TenancyTelemetryNames.Resolutions);
        }

        /// <summary>The outcome of every measurement collected, each a count of one with no other tag.</summary>
        /// <param name="resolutions">The collector.</param>
        /// <returns>The outcomes, in order.</returns>
        private static string[] Outcomes(MetricCollector<long> resolutions)
        {
            return [.. resolutions.GetMeasurementSnapshot().Select(static measurement =>
            {
                Assert.Equal(1, measurement.Value);
                KeyValuePair<string, object?> tag = Assert.Single(measurement.Tags);
                Assert.Equal(TenancyTelemetryNames.OutcomeTag, tag.Key);
                return (string)tag.Value!;
            })];
        }
    }
}

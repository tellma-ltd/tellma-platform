// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Limits;

namespace Tellma.Core.AspNetCore.Tests.Limits
{
    /// <summary>
    ///     The policies an endpoint chains and the router that runs the chain: a fixed order, the
    ///     per-user and per-tenant policies dropped under a policy that stands alone, one account's
    ///     cap across tenant ids, a refusal that releases what earlier links granted, lanes kept
    ///     apart, and a startup audit that refuses a policy no limiter would run.
    /// </summary>
    public sealed class RatePolicyRouterTests
    {
        [Fact]
        public void An_endpoint_without_a_policy_is_not_limited()
        {
            Assert.Same(RateChainSpec.None, RatePolicies.Resolve(new EndpointMetadataCollection()));
        }

        [Fact]
        public void A_chain_runs_the_route_policy_first_then_the_account_then_the_user_then_the_tenant()
        {
            RateChainSpec spec = RatePolicies.Resolve(Metadata(TellmaRatePolicies.Tenant, TellmaRatePolicies.User, TellmaRatePolicies.Export));

            Assert.Equal(
                [TellmaRatePolicies.Export, RatePolicies.UserAcrossTenants, TellmaRatePolicies.User, TellmaRatePolicies.Tenant],
                spec.Policies.Select(static policy => policy.Name));
        }

        [Fact]
        public void A_policy_named_twice_runs_once()
        {
            RateChainSpec spec = RatePolicies.Resolve(Metadata(TellmaRatePolicies.User, TellmaRatePolicies.User));

            Assert.Equal([RatePolicies.UserAcrossTenants, TellmaRatePolicies.User], spec.Policies.Select(static policy => policy.Name));
        }

        [Fact]
        public void The_cap_across_tenants_runs_only_where_the_users_rate_does()
        {
            Assert.Equal([TellmaRatePolicies.Tenant], RatePolicies.Resolve(Metadata(TellmaRatePolicies.Tenant)).Policies.Select(static policy => policy.Name));
            Assert.DoesNotContain(RatePolicies.UserAcrossTenants, RatePolicies.All.Keys);
        }

        [Fact]
        public async Task One_account_walking_tenant_ids_is_refused_by_its_cap_across_them()
        {
            using RatePolicyRouter router = Router(static api =>
            {
                api.RequestsPerMinutePerUser = 1;
                api.RequestsPerMinutePerUserAcrossTenants = 2;
            });
            CancellationToken cancellation = TestContext.Current.CancellationToken;
            string[] chain = [TellmaRatePolicies.User, TellmaRatePolicies.Tenant];

            // --- Two tenant ids spend the account's cap, each with a window of its own to spare ---
            using RateLimitLease first = await router.AcquireAsync(ContextOf("1", "sub-1", chain), cancellationToken: cancellation);
            using RateLimitLease second = await router.AcquireAsync(ContextOf("2", "sub-1", chain), cancellationToken: cancellation);
            using RateLimitLease third = await router.AcquireAsync(ContextOf("3", "sub-1", chain), cancellationToken: cancellation);

            Assert.True(first.IsAcquired);
            Assert.True(second.IsAcquired);
            RefusedLease refused = Assert.IsType<RefusedLease>(third);
            Assert.Equal(RatePolicies.UserAcrossTenants, refused.Policy);
            Assert.True(refused.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter));
            Assert.Equal(TimeSpan.FromSeconds(10), retryAfter);

            // --- The cap is the account's own: another account reaches tenant 3 ---
            using RateLimitLease other = await router.AcquireAsync(ContextOf("3", "sub-2", chain), cancellationToken: cancellation);
            Assert.True(other.IsAcquired);
        }

        [Fact]
        public void A_cap_across_tenants_below_one_tenants_rate_is_refused_by_the_options()
        {
            ApiOptions api = new() { RequestsPerMinutePerUser = 600, RequestsPerMinutePerUserAcrossTenants = 599 };

            ValidationResult result = Assert.Single(api.Validate(new ValidationContext(api)));

            Assert.Equal("Tellma:Api:RequestsPerMinutePerUserAcrossTenants is 599; it must be at least RequestsPerMinutePerUser, 600.", result.ErrorMessage);
        }

        [Theory]
        [InlineData(TellmaRatePolicies.Blobs)]
        [InlineData(TellmaRatePolicies.Telemetry)]
        [InlineData(TellmaRatePolicies.Anonymous)]
        [InlineData(TellmaRatePolicies.Mcp)]
        public void A_policy_that_stands_alone_replaces_the_user_and_the_tenant(string policy)
        {
            RateChainSpec spec = RatePolicies.Resolve(Metadata(TellmaRatePolicies.User, TellmaRatePolicies.Tenant, policy));

            Assert.Equal([policy], spec.Policies.Select(static definition => definition.Name));
        }

        [Fact]
        public void The_priority_is_honored_only_on_the_report_policy()
        {
            Assert.True(RatePolicies.Resolve(new EndpointMetadataCollection(new RatePolicyAttribute(TellmaRatePolicies.Reports) { HonorsPriority = true })).HonorsPriority);
            Assert.False(RatePolicies.Resolve(new EndpointMetadataCollection(new RatePolicyAttribute(TellmaRatePolicies.Export) { HonorsPriority = true })).HonorsPriority);
            Assert.False(RatePolicies.Resolve(Metadata(TellmaRatePolicies.Reports)).HonorsPriority);
        }

        [Fact]
        public void An_unknown_policy_is_refused_at_resolution()
        {
            _ = Assert.Throws<InvalidOperationException>(() => RatePolicies.Resolve(Metadata("tellma-nope")));
        }

        [Fact]
        public void The_audit_passes_the_tenant_groups_stamp_and_a_route_policy()
        {
            List<CompositionProblem> problems = [];

            RatePolicies.Audit(Endpoint("/{tenantId}/api/web/x", TellmaRatePolicies.User, TellmaRatePolicies.Tenant, TellmaRatePolicies.Export), "GET x", problems);
            RatePolicies.Audit(Endpoint("/api/distribution-info", TellmaRatePolicies.Anonymous), "GET y", problems);

            Assert.Empty(problems);
        }

        [Fact]
        public void The_audit_refuses_an_unknown_policy()
        {
            List<CompositionProblem> problems = [];

            RatePolicies.Audit(Endpoint("/{tenantId}/api/web/x", "tellma-nope"), "GET x", problems);

            Assert.Contains("'tellma-nope', which does not exist", Assert.Single(problems).Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void The_audit_refuses_two_route_policies_on_one_endpoint()
        {
            List<CompositionProblem> problems = [];

            RatePolicies.Audit(Endpoint("/{tenantId}/api/web/x", TellmaRatePolicies.Export, TellmaRatePolicies.Reports), "GET x", problems);

            Assert.Contains("an endpoint takes at most one", Assert.Single(problems).Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void The_audit_refuses_a_tenant_partition_on_a_route_without_a_tenant()
        {
            List<CompositionProblem> problems = [];

            RatePolicies.Audit(Endpoint("/api/x", TellmaRatePolicies.Export), "GET x", problems);

            Assert.Contains("on a route that names no tenant", Assert.Single(problems).Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void The_audit_refuses_a_priority_on_a_policy_without_lanes()
        {
            List<CompositionProblem> problems = [];
            RouteEndpoint endpoint = new(
                static _ => Task.CompletedTask,
                RoutePatternFactory.Parse("/{tenantId}/api/web/x"),
                0,
                new EndpointMetadataCollection(new RatePolicyAttribute(TellmaRatePolicies.Export) { HonorsPriority = true }),
                "x");

            RatePolicies.Audit(endpoint, "GET x", problems);

            Assert.Contains("which has no lanes", Assert.Single(problems).Problem, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_limited_endpoints_attempt_defers_so_a_window_is_charged_once()
        {
            using RatePolicyRouter router = Router(static api => api.RequestsPerMinutePerUser = 1);
            DefaultHttpContext context = Context(TellmaRatePolicies.User);

            // The middleware attempts first and waits after; the attempt must take nothing.
            using RateLimitLease attempt = router.AttemptAcquire(context);
            Assert.False(attempt.IsAcquired);
            using RateLimitLease first = await router.AcquireAsync(context, cancellationToken: TestContext.Current.CancellationToken);
            using RateLimitLease second = await router.AcquireAsync(context, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(first.IsAcquired);
            Assert.False(second.IsAcquired);
            Assert.Equal(TellmaRatePolicies.User, Assert.IsType<RefusedLease>(second).Policy);
            Assert.True(second.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter));
            Assert.Equal(TimeSpan.FromSeconds(10), retryAfter);
        }

        [Fact]
        public void An_endpoint_no_policy_limits_is_granted_at_the_attempt()
        {
            using RatePolicyRouter router = Router(static _ => { });
            DefaultHttpContext context = Context();

            using RateLimitLease attempt = router.AttemptAcquire(context);

            Assert.True(attempt.IsAcquired);
        }

        [Fact]
        public async Task A_refusal_later_in_the_chain_releases_the_permits_taken_before_it()
        {
            using RatePolicyRouter router = Router(static api =>
            {
                api.ConcurrentExportsPerUser = 1;
                api.RequestsPerMinutePerUser = 1;
            });
            CancellationToken cancellation = TestContext.Current.CancellationToken;
            DefaultHttpContext export = Context(TellmaRatePolicies.Export, TellmaRatePolicies.User);

            // --- The first export spends the user's window, and its export permit is returned ---
            using (RateLimitLease first = await router.AcquireAsync(export, cancellationToken: cancellation))
            {
                Assert.True(first.IsAcquired);
            }

            // --- The second takes the export permit, then the spent window refuses it ---
            using RateLimitLease refused = await router.AcquireAsync(export, cancellationToken: cancellation);
            Assert.Equal(TellmaRatePolicies.User, Assert.IsType<RefusedLease>(refused).Policy);

            // --- The export permit the refusal took was released: the one permit is free ---
            using RateLimitLease free = await router.AcquireAsync(Context(TellmaRatePolicies.Export), cancellationToken: cancellation);
            Assert.True(free.IsAcquired);
        }

        [Fact]
        public async Task A_concurrency_permit_is_held_for_the_lease_and_released_with_it()
        {
            using RatePolicyRouter router = Router(static api => api.ConcurrentRequestsPerTenant = 1);
            CancellationToken cancellation = TestContext.Current.CancellationToken;
            DefaultHttpContext context = Context(TellmaRatePolicies.Tenant);

            RateLimitLease held = await router.AcquireAsync(context, cancellationToken: cancellation);
            using (RateLimitLease refused = await router.AcquireAsync(context, cancellationToken: cancellation))
            {
                Assert.Equal(TellmaRatePolicies.Tenant, Assert.IsType<RefusedLease>(refused).Policy);
            }

            held.Dispose();
            using RateLimitLease again = await router.AcquireAsync(context, cancellationToken: cancellation);
            Assert.True(again.IsAcquired);
        }

        [Fact]
        public async Task Tenants_and_users_hold_separate_partitions()
        {
            using RatePolicyRouter router = Router(static api =>
            {
                api.ConcurrentRequestsPerTenant = 1;
                api.RequestsPerMinutePerUser = 1;
            });
            CancellationToken cancellation = TestContext.Current.CancellationToken;

            using RateLimitLease tenant1 = await router.AcquireAsync(Context(TellmaRatePolicies.Tenant), cancellationToken: cancellation);
            using RateLimitLease tenant2 = await router.AcquireAsync(ContextOf("2", "sub-1", TellmaRatePolicies.Tenant), cancellationToken: cancellation);
            using RateLimitLease user1 = await router.AcquireAsync(Context(TellmaRatePolicies.User), cancellationToken: cancellation);
            using RateLimitLease user2 = await router.AcquireAsync(ContextOf("1", "sub-2", TellmaRatePolicies.User), cancellationToken: cancellation);
            using RateLimitLease otherTenant = await router.AcquireAsync(ContextOf("2", "sub-1", TellmaRatePolicies.User), cancellationToken: cancellation);

            Assert.All([tenant1, tenant2, user1, user2, otherTenant], static lease => Assert.True(lease.IsAcquired));
        }

        [Fact]
        public async Task The_batch_lane_runs_beside_a_full_interactive_lane_and_its_refusal_names_the_lane()
        {
            using RatePolicyRouter router = Router(static api =>
            {
                api.ConcurrentReportsPerUser = 1;
                api.ReportQueuePerUser = 0;
                api.ConcurrentBatchReportsPerUser = 1;
                api.BatchReportQueuePerUser = 0;
            });
            CancellationToken cancellation = TestContext.Current.CancellationToken;
            DefaultHttpContext interactive = Report(low: false);
            DefaultHttpContext batch = Report(low: true);

            using RateLimitLease looking = await router.AcquireAsync(interactive, cancellationToken: cancellation);
            using RateLimitLease tile = await router.AcquireAsync(batch, cancellationToken: cancellation);
            using RateLimitLease secondLook = await router.AcquireAsync(interactive, cancellationToken: cancellation);
            using RateLimitLease secondTile = await router.AcquireAsync(batch, cancellationToken: cancellation);

            Assert.True(looking.IsAcquired);
            Assert.True(tile.IsAcquired);
            Assert.Equal("interactive", Assert.IsType<RefusedLease>(secondLook).Lane);
            Assert.Equal("batch", Assert.IsType<RefusedLease>(secondTile).Lane);
        }

        [Fact]
        public async Task A_queued_report_is_admitted_when_the_permit_ahead_of_it_is_released()
        {
            using RatePolicyRouter router = Router(static api =>
            {
                api.ConcurrentReportsPerUser = 1;
                api.ReportQueuePerUser = 1;
            });
            CancellationToken cancellation = TestContext.Current.CancellationToken;
            DefaultHttpContext context = Report(low: false);

            RateLimitLease running = await router.AcquireAsync(context, cancellationToken: cancellation);
            ValueTask<RateLimitLease> queued = router.AcquireAsync(context, cancellationToken: cancellation);
            Assert.False(queued.IsCompleted);

            running.Dispose();
            using RateLimitLease admitted = await queued;
            Assert.True(admitted.IsAcquired);
        }

        [Fact]
        public async Task No_two_routers_share_a_partition()
        {
            // Each instance's limits live in its own memory: a request one refuses, another admits.
            using RatePolicyRouter first = Router(static api => api.RequestsPerMinutePerUser = 1);
            using RatePolicyRouter second = Router(static api => api.RequestsPerMinutePerUser = 1);
            DefaultHttpContext context = Context(TellmaRatePolicies.User);

            using RateLimitLease a = await first.AcquireAsync(context, cancellationToken: TestContext.Current.CancellationToken);
            using RateLimitLease b = await second.AcquireAsync(context, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(a.IsAcquired);
            Assert.True(b.IsAcquired);
        }

        /// <summary>A router over the given limits.</summary>
        /// <param name="configure">Changes to the default limits.</param>
        /// <returns>The router.</returns>
        private static RatePolicyRouter Router(Action<ApiOptions> configure)
        {
            ApiOptions api = new();
            configure(api);
            return new RatePolicyRouter(Options.Create(api), Options.Create(new McpLimitOptions()));
        }

        /// <summary>Metadata naming policies.</summary>
        /// <param name="policies">The policies.</param>
        /// <returns>The metadata.</returns>
        private static EndpointMetadataCollection Metadata(params string[] policies)
        {
            return new EndpointMetadataCollection([.. policies.Select(static policy => new RatePolicyAttribute(policy))]);
        }

        /// <summary>A route endpoint naming policies.</summary>
        /// <param name="pattern">The route.</param>
        /// <param name="policies">The policies.</param>
        /// <returns>The endpoint.</returns>
        private static RouteEndpoint Endpoint(string pattern, params string[] policies)
        {
            return new RouteEndpoint(static _ => Task.CompletedTask, RoutePatternFactory.Parse(pattern), 0, Metadata(policies), pattern);
        }

        /// <summary>Tenant 1's <c>sub-1</c> on an endpoint naming policies.</summary>
        /// <param name="policies">The policies.</param>
        /// <returns>The request.</returns>
        private static DefaultHttpContext Context(params string[] policies)
        {
            return ContextOf("1", "sub-1", policies);
        }

        /// <summary>A user in a tenant on an endpoint naming policies.</summary>
        /// <param name="tenant">The tenant segment.</param>
        /// <param name="subject">The user's subject.</param>
        /// <param name="policies">The policies.</param>
        /// <returns>The request.</returns>
        private static DefaultHttpContext ContextOf(string tenant, string subject, params string[] policies)
        {
            DefaultHttpContext context = RatePartitionKeysTests.Request(tenant);
            context.User = RatePartitionKeysTests.Principal(("sub", subject));
            context.SetEndpoint(new Endpoint(null, Metadata(policies), "test"));
            return context;
        }

        /// <summary>A report request on an endpoint that honors the priority.</summary>
        /// <param name="low">Whether it asks for the batch lane.</param>
        /// <returns>The request.</returns>
        private static DefaultHttpContext Report(bool low)
        {
            DefaultHttpContext context = Context();
            context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new RatePolicyAttribute(TellmaRatePolicies.Reports) { HonorsPriority = true }), "report"));
            if (low)
            {
                context.Request.Headers[TellmaHeaders.Priority] = "low";
            }

            return context;
        }
    }
}

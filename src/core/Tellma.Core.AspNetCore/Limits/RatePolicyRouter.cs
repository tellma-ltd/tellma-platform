// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.RateLimiting;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.AspNetCore.Hosting;

namespace Tellma.Core.AspNetCore.Limits
{
    // Spec 0016 §8.2. The router is the rate-limiting middleware's global limiter: it reads the
    // policies an endpoint's metadata names and runs that endpoint's one chain. Every partition lives
    // in this instance's memory alone, and the partitioned limiters' own timer replenishes windows and
    // disposes partitions that have gone idle, which bounds memory for per-user keys.

    /// <summary>
    ///     The global limiter of the web host: per endpoint, the chain of the rate and concurrency
    ///     policies its metadata names, each over its own partitions.
    /// </summary>
    internal sealed class RatePolicyRouter : PartitionedRateLimiter<HttpContext>
    {
        /// <summary>The window of every rate policy: a minute.</summary>
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

        /// <summary>The segments a sliding window is divided into.</summary>
        private const int SlidingSegments = 6;

        private readonly Dictionary<string, PartitionedRateLimiter<RatePartition>> _stores = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, PartitionedRateLimiter<HttpContext>> _chains = new(StringComparer.Ordinal);
        private readonly ConditionalWeakTable<Endpoint, PartitionedRateLimiter<HttpContext>?> _endpoints = [];
        private readonly IOptions<ApiOptions> _api;
        private readonly IOptions<McpLimitOptions> _mcp;

        /// <summary>
        ///     Creates the policies' partitioned limiters. Each partition reads its limit when it is
        ///     first used, so the options are never read before the startup gate has judged them.
        /// </summary>
        /// <param name="api">The web API's limits.</param>
        /// <param name="mcp">The agent tool endpoint's limits.</param>
        public RatePolicyRouter(IOptions<ApiOptions> api, IOptions<McpLimitOptions> mcp)
        {
            ArgumentNullException.ThrowIfNull(api);
            ArgumentNullException.ThrowIfNull(mcp);
            _api = api;
            _mcp = mcp;
            foreach (RatePolicyDefinition policy in RatePolicies.Running)
            {
                _stores[policy.Name] = PartitionedRateLimiter.Create<RatePartition, RatePartition>(partition => Partition(policy.Name, partition));
            }
        }

        /// <inheritdoc />
        public override RateLimiterStatistics? GetStatistics(HttpContext resource)
        {
            return null;
        }

        /// <summary>
        ///     Defers to <see cref="AcquireAsyncCore" />: the middleware tries this first and then waits,
        ///     and a window charged here for a request a later policy refused would be charged again by
        ///     the wait.
        /// </summary>
        /// <param name="resource">The request.</param>
        /// <param name="permitCount">The permits asked for.</param>
        /// <returns>A lease that is never acquired, unless the endpoint is not limited.</returns>
        protected override RateLimitLease AttemptAcquireCore(HttpContext resource, int permitCount)
        {
            ArgumentNullException.ThrowIfNull(resource);
            return ChainOf(resource) is null ? Leases.Granted : Leases.Deferred;
        }

        /// <inheritdoc />
        protected override ValueTask<RateLimitLease> AcquireAsyncCore(HttpContext resource, int permitCount, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(resource);
            return ChainOf(resource) is PartitionedRateLimiter<HttpContext> chain
                ? chain.AcquireAsync(resource, permitCount, cancellationToken)
                : ValueTask.FromResult(Leases.Granted);
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (PartitionedRateLimiter<HttpContext> chain in _chains.Values)
                {
                    chain.Dispose();
                }

                foreach (PartitionedRateLimiter<RatePartition> store in _stores.Values)
                {
                    store.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        /// <summary>The chain of a request's endpoint, built once per endpoint; null when the endpoint is not limited.</summary>
        /// <param name="context">The request, past routing.</param>
        /// <returns>The chain, or null.</returns>
        private PartitionedRateLimiter<HttpContext>? ChainOf(HttpContext context)
        {
            return context.GetEndpoint() is Endpoint endpoint
                ? _endpoints.GetValue(endpoint, endpoint => Build(RatePolicies.Resolve(endpoint.Metadata)))
                : null;
        }

        /// <summary>The chain of a set of policies, shared by every endpoint that names the same set.</summary>
        /// <param name="spec">The policies.</param>
        /// <returns>The chain, or null for no policy.</returns>
        private PartitionedRateLimiter<HttpContext>? Build(RateChainSpec spec)
        {
            return spec.Policies.Count == 0 ? null : _chains.GetOrAdd(spec.Signature, _ => PartitionedRateLimiter.CreateChained(
                [.. spec.Policies.Select(policy => (PartitionedRateLimiter<HttpContext>)new PolicyLimiter(policy, spec.HonorsPriority, _stores[policy.Name], RetryAfterOf(policy.Name)))]));
        }

        /// <summary>
        ///     The wait a refusal of a policy names when its limiter names none: a sliding window's
        ///     segment, the longest before its next slide, since a sliding window's refusal carries no
        ///     wait of its own. A fixed window names its own, and a concurrency limit has none to name.
        /// </summary>
        /// <param name="policy">The policy's name.</param>
        /// <returns>The wait, or null.</returns>
        internal static TimeSpan? RetryAfterOf(string policy)
        {
            return policy is TellmaRatePolicies.User or RatePolicies.UserAcrossTenants or TellmaRatePolicies.Telemetry or TellmaRatePolicies.Mcp
                ? Window / SlidingSegments
                : null;
        }

        /// <summary>A policy's limiter for one partition, from the configured limits.</summary>
        /// <param name="policy">The policy's name.</param>
        /// <param name="partition">The partition.</param>
        /// <returns>The partition's limiter.</returns>
        private RateLimitPartition<RatePartition> Partition(string policy, RatePartition partition)
        {
            ApiOptions api = _api.Value;
            McpLimitOptions mcp = _mcp.Value;
            return policy switch
            {
                TellmaRatePolicies.User => Sliding(partition, api.RequestsPerMinutePerUser),
                RatePolicies.UserAcrossTenants => Sliding(partition, api.RequestsPerMinutePerUserAcrossTenants),
                TellmaRatePolicies.Tenant => Concurrency(partition, api.ConcurrentRequestsPerTenant, queue: 0),
                TellmaRatePolicies.Anonymous => RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = api.AnonymousRequestsPerMinutePerIp,
                    Window = Window,
                    QueueLimit = 0,
                }),
                TellmaRatePolicies.Export => Concurrency(partition, api.ConcurrentExportsPerUser, queue: 0),
                TellmaRatePolicies.Import => Concurrency(partition, api.ConcurrentImportsPerUser, queue: 0),
                TellmaRatePolicies.Reports => partition.Lane == RateLane.Batch
                    ? Concurrency(partition, api.ConcurrentBatchReportsPerUser, api.BatchReportQueuePerUser)
                    : Concurrency(partition, api.ConcurrentReportsPerUser, api.ReportQueuePerUser),
                TellmaRatePolicies.Telemetry => Sliding(partition, api.TelemetryBatchesPerMinutePerUser),
                TellmaRatePolicies.Blobs => Concurrency(partition, api.ConcurrentBlobReadsPerTenant, api.BlobReadQueuePerTenant),
                TellmaRatePolicies.Mcp => Sliding(partition, mcp.ToolCallsPerMinutePerUser),
                _ => throw new InvalidOperationException($"The rate policy '{policy}' has no limits."),
            };

            // A rate: a minute's permits over six sliding segments, refused at once when spent.
            static RateLimitPartition<RatePartition> Sliding(RatePartition partition, int permits)
            {
                return RateLimitPartition.GetSlidingWindowLimiter(partition, _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = permits,
                    Window = Window,
                    SegmentsPerWindow = SlidingSegments,
                    QueueLimit = 0,
                });
            }

            // A concurrency: permits held for the request's life, the oldest waiter served first.
            static RateLimitPartition<RatePartition> Concurrency(RatePartition partition, int permits, int queue)
            {
                return RateLimitPartition.GetConcurrencyLimiter(partition, _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = permits,
                    QueueLimit = queue,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                });
            }
        }

        /// <summary>
        ///     One link of a chain: a policy's partitioned limiter keyed by the request's partition,
        ///     whose refusal names the policy and the lane.
        /// </summary>
        /// <param name="policy">The policy.</param>
        /// <param name="honorsPriority">Whether the request's priority chooses the lane.</param>
        /// <param name="store">The policy's partitions.</param>
        /// <param name="retryAfter">The wait a refusal names when the store's names none.</param>
        internal sealed class PolicyLimiter(RatePolicyDefinition policy, bool honorsPriority, PartitionedRateLimiter<RatePartition> store, TimeSpan? retryAfter)
            : PartitionedRateLimiter<HttpContext>
        {
            /// <inheritdoc />
            public override RateLimiterStatistics? GetStatistics(HttpContext resource)
            {
                return store.GetStatistics(PartitionOf(resource));
            }

            /// <inheritdoc />
            protected override RateLimitLease AttemptAcquireCore(HttpContext resource, int permitCount)
            {
                RatePartition partition = PartitionOf(resource);
                return Tag(store.AttemptAcquire(partition, permitCount), partition);
            }

            /// <inheritdoc />
            protected override async ValueTask<RateLimitLease> AcquireAsyncCore(HttpContext resource, int permitCount, CancellationToken cancellationToken)
            {
                RatePartition partition = PartitionOf(resource);
                return Tag(await store.AcquireAsync(partition, permitCount, cancellationToken).ConfigureAwait(false), partition);
            }

            /// <summary>The request's partition of the policy.</summary>
            /// <param name="context">The request.</param>
            /// <returns>The partition.</returns>
            internal RatePartition PartitionOf(HttpContext context)
            {
                string key = policy.Partition switch
                {
                    RatePartitionKind.TenantSubject => RatePartitionKeys.TenantSubject(context),
                    RatePartitionKind.Tenant => RatePartitionKeys.Tenant(context),
                    RatePartitionKind.Subject => RatePartitionKeys.Subject(context.User),
                    RatePartitionKind.ClientAddress => RatePartitionKeys.ClientAddress(context),
                    _ => throw new InvalidOperationException($"The rate policy '{policy.Name}' has an unknown partition kind."),
                };
                return new RatePartition(key, policy.HasLanes ? RatePartitionKeys.Lane(context, honorsPriority) : RateLane.None);
            }

            /// <summary>A lease, or its refusal naming the policy and the lane.</summary>
            /// <param name="lease">The store's lease.</param>
            /// <param name="partition">The partition it was asked of.</param>
            /// <returns>The lease.</returns>
            private RateLimitLease Tag(RateLimitLease lease, RatePartition partition)
            {
                return lease.IsAcquired ? lease : new RefusedLease(lease, policy.Name, RatePartitionKeys.TagOf(partition.Lane), retryAfter);
            }
        }
    }

    /// <summary>
    ///     A policy's refusal: the store's failed lease, whose <c>Retry-After</c> metadata it keeps or,
    ///     when it names none, the policy's own, with the policy and the lane that refused.
    /// </summary>
    /// <param name="inner">The failed lease.</param>
    /// <param name="policy">The policy that refused.</param>
    /// <param name="lane">The lane that refused; null off the report policy.</param>
    /// <param name="retryAfter">The wait to name when the failed lease names none; null for none.</param>
    internal sealed class RefusedLease(RateLimitLease inner, string policy, string? lane, TimeSpan? retryAfter = null) : RateLimitLease
    {
        /// <summary>The policy that refused.</summary>
        public string Policy { get; } = policy;

        /// <summary>The lane that refused, as its tag value; null off the report policy.</summary>
        public string? Lane { get; } = lane;

        /// <inheritdoc />
        public override bool IsAcquired => false;

        /// <inheritdoc />
        public override IEnumerable<string> MetadataNames => FallsBack
            ? [.. inner.MetadataNames, MetadataName.RetryAfter.Name]
            : inner.MetadataNames;

        /// <summary>Whether the policy's wait stands in for one the failed lease does not name.</summary>
        private bool FallsBack => retryAfter is not null && !inner.MetadataNames.Contains(MetadataName.RetryAfter.Name, StringComparer.Ordinal);

        /// <inheritdoc />
        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (inner.TryGetMetadata(metadataName, out metadata))
            {
                return true;
            }

            // --- The policy's wait, for a limiter whose refusal names none ---
            if (retryAfter is TimeSpan wait && string.Equals(metadataName, MetadataName.RetryAfter.Name, StringComparison.Ordinal))
            {
                metadata = wait;
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>The router's two leases of its own, neither holding a permit.</summary>
    internal static class Leases
    {
        /// <summary>The lease of an endpoint no policy limits.</summary>
        public static RateLimitLease Granted { get; } = new FixedLease(acquired: true);

        /// <summary>The lease that sends the middleware on to wait, where the chain runs once.</summary>
        public static RateLimitLease Deferred { get; } = new FixedLease(acquired: false);

        /// <summary>A lease with no permit and no metadata, whose disposal releases nothing.</summary>
        /// <param name="acquired">Whether it reads as acquired.</param>
        private sealed class FixedLease(bool acquired) : RateLimitLease
        {
            /// <inheritdoc />
            public override bool IsAcquired => acquired;

            /// <inheritdoc />
            public override IEnumerable<string> MetadataNames => [];

            /// <inheritdoc />
            public override bool TryGetMetadata(string metadataName, out object? metadata)
            {
                metadata = null;
                return false;
            }
        }
    }
}

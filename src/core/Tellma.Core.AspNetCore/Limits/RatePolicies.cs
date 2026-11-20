// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.AspNetCore.Routing;

namespace Tellma.Core.AspNetCore.Limits
{
    // Spec 0016 §8.2: the policies, their partitions and the one chain an endpoint takes. ASP.NET
    // Core applies one limiter per endpoint, so every policy an endpoint's metadata names is chained
    // into one, in a fixed order: the route's own policy first (so a request queued there holds no
    // tenant permit), then tellma-user, then tellma-tenant.

    /// <summary>What a policy partitions its callers by.</summary>
    internal enum RatePartitionKind
    {
        /// <summary>One user in one tenant: <c>{tenantId}:{sub}</c>.</summary>
        TenantSubject,

        /// <summary>One tenant: <c>{tenantId}</c>.</summary>
        Tenant,

        /// <summary>One user across tenants: <c>{sub}</c>.</summary>
        Subject,

        /// <summary>One client address.</summary>
        ClientAddress,
    }

    /// <summary>One rate or concurrency policy: its name, its place in a chain and its partition.</summary>
    /// <param name="Name">The policy's name, one of <see cref="TellmaRatePolicies" />.</param>
    /// <param name="Order">Its place in a chain, lowest first.</param>
    /// <param name="Partition">What it partitions its callers by.</param>
    /// <param name="HasLanes">Whether it runs the interactive and batch lanes.</param>
    /// <param name="ReplacesUserAndTenant">Whether an endpoint under it runs under neither <c>tellma-user</c> nor <c>tellma-tenant</c>.</param>
    internal sealed record RatePolicyDefinition(string Name, int Order, RatePartitionKind Partition, bool HasLanes, bool ReplacesUserAndTenant)
    {
        /// <summary>Whether its partition names a tenant, so only a tenant route can take it.</summary>
        public bool NeedsTenant => Partition is RatePartitionKind.TenantSubject or RatePartitionKind.Tenant;

        /// <summary>Whether it is a route's own policy rather than one the tenant groups stamp on every endpoint.</summary>
        public bool IsRoutePolicy => Name is not (TellmaRatePolicies.User or TellmaRatePolicies.Tenant);
    }

    /// <summary>The policies one endpoint chains, in order, and whether its report lane follows the priority.</summary>
    /// <param name="Policies">The policies, in chain order.</param>
    /// <param name="HonorsPriority">Whether <c>Tellma-Priority</c> chooses the report lane.</param>
    internal sealed record RateChainSpec(IReadOnlyList<RatePolicyDefinition> Policies, bool HonorsPriority)
    {
        /// <summary>No policy: the endpoint is not limited.</summary>
        public static RateChainSpec None { get; } = new([], HonorsPriority: false);

        /// <summary>A key that two endpoints with the same chain share.</summary>
        public string Signature => string.Join('|', Policies.Select(static policy => policy.Name)) + (HonorsPriority ? "|priority" : string.Empty);
    }

    /// <summary>The policies, and how an endpoint's metadata selects and orders them.</summary>
    internal static class RatePolicies
    {
        /// <summary>Every policy, by name.</summary>
        public static readonly IReadOnlyDictionary<string, RatePolicyDefinition> All = new RatePolicyDefinition[]
        {
            // --- The route policies, each ahead of the per-user and per-tenant ones ---
            new(TellmaRatePolicies.Anonymous, 0, RatePartitionKind.ClientAddress, HasLanes: false, ReplacesUserAndTenant: true),
            new(TellmaRatePolicies.Telemetry, 1, RatePartitionKind.Subject, HasLanes: false, ReplacesUserAndTenant: true),
            new(TellmaRatePolicies.Mcp, 2, RatePartitionKind.TenantSubject, HasLanes: false, ReplacesUserAndTenant: true),
            new(TellmaRatePolicies.Blobs, 3, RatePartitionKind.Tenant, HasLanes: false, ReplacesUserAndTenant: true),
            new(TellmaRatePolicies.Export, 4, RatePartitionKind.TenantSubject, HasLanes: false, ReplacesUserAndTenant: false),
            new(TellmaRatePolicies.Import, 5, RatePartitionKind.TenantSubject, HasLanes: false, ReplacesUserAndTenant: false),
            new(TellmaRatePolicies.Reports, 6, RatePartitionKind.TenantSubject, HasLanes: true, ReplacesUserAndTenant: false),

            // --- Then the user's rate and the tenant's concurrency, the last permit taken ---
            new(TellmaRatePolicies.User, 10, RatePartitionKind.TenantSubject, HasLanes: false, ReplacesUserAndTenant: false),
            new(TellmaRatePolicies.Tenant, 11, RatePartitionKind.Tenant, HasLanes: false, ReplacesUserAndTenant: false),
        }.ToDictionary(static policy => policy.Name, StringComparer.Ordinal);

        /// <summary>
        ///     The chain an endpoint's metadata selects: each policy it names once, the per-user and
        ///     per-tenant ones dropped under a route policy that replaces them, in chain order.
        /// </summary>
        /// <param name="metadata">The endpoint's metadata.</param>
        /// <returns>The chain; <see cref="RateChainSpec.None" /> when the endpoint names no policy.</returns>
        /// <exception cref="InvalidOperationException">The metadata names a policy that does not exist, which the startup audit refuses.</exception>
        public static RateChainSpec Resolve(EndpointMetadataCollection metadata)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            IReadOnlyList<IRatePolicyMetadata> named = metadata.GetOrderedMetadata<IRatePolicyMetadata>();
            if (named.Count == 0)
            {
                return RateChainSpec.None;
            }

            // --- Each named policy once; a priority is honored where any metadata of the report policy honors it ---
            Dictionary<string, RatePolicyDefinition> policies = new(StringComparer.Ordinal);
            bool honorsPriority = false;
            foreach (IRatePolicyMetadata item in named)
            {
                RatePolicyDefinition definition = All.GetValueOrDefault(item.Policy ?? string.Empty)
                    ?? throw new InvalidOperationException($"The endpoint names the rate policy '{item.Policy}', which does not exist.");
                policies[definition.Name] = definition;
                honorsPriority |= item.HonorsPriority && definition.HasLanes;
            }

            // --- A policy that stands alone takes the place of the user's and the tenant's ---
            if (policies.Values.Any(static policy => policy.ReplacesUserAndTenant))
            {
                _ = policies.Remove(TellmaRatePolicies.User);
                _ = policies.Remove(TellmaRatePolicies.Tenant);
            }

            return new RateChainSpec([.. policies.Values.OrderBy(static policy => policy.Order)], honorsPriority);
        }

        /// <summary>
        ///     Audits an endpoint's rate policies: each names a policy that exists, at most one is a
        ///     route's own, only the report policy honors a priority, and a policy partitioned by
        ///     tenant sits on a tenant route.
        /// </summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="where">Its description.</param>
        /// <param name="problems">The problems found so far.</param>
        public static void Audit(RouteEndpoint endpoint, string where, List<CompositionProblem> problems)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            ArgumentNullException.ThrowIfNull(problems);
            IReadOnlyList<IRatePolicyMetadata> named = endpoint.Metadata.GetOrderedMetadata<IRatePolicyMetadata>();
            HashSet<string> routePolicies = new(StringComparer.Ordinal);
            bool tenantRoute = TellmaEndpointAudit.BeginsWithTenantId(endpoint.RoutePattern);
            foreach (IRatePolicyMetadata item in named)
            {
                // --- A name the limiter knows ---
                if (item.Policy is null || !All.TryGetValue(item.Policy, out RatePolicyDefinition? definition))
                {
                    problems.Add(Problem(where, $"names the rate policy '{item.Policy}', which does not exist.", $"Name one of {string.Join(", ", All.Keys)}."));
                    continue;
                }

                if (definition.IsRoutePolicy)
                {
                    _ = routePolicies.Add(definition.Name);
                }

                // --- A priority only where lanes exist ---
                if (item.HonorsPriority && !definition.HasLanes)
                {
                    problems.Add(Problem(where, $"lets Tellma-Priority choose a lane of the rate policy '{definition.Name}', which has no lanes.", $"Honor the priority on {TellmaRatePolicies.Reports} alone."));
                }

                // --- A tenant's partition needs the tenant the route names ---
                if (definition.NeedsTenant && !tenantRoute)
                {
                    problems.Add(Problem(where, $"takes the rate policy '{definition.Name}', whose partition is the tenant, on a route that names no tenant.", "Map the endpoint on a tenant group, or take a tenantless policy."));
                }
            }

            // --- One route policy: a request waits in one queue, never two ---
            if (routePolicies.Count > 1)
            {
                problems.Add(Problem(where, $"takes the rate policies {string.Join(", ", routePolicies.Order(StringComparer.Ordinal))}; an endpoint takes at most one besides {TellmaRatePolicies.User} and {TellmaRatePolicies.Tenant}.", "Keep the one policy the endpoint's work calls for."));
            }
        }

        /// <summary>A problem naming an endpoint.</summary>
        /// <param name="where">The endpoint's description.</param>
        /// <param name="description">What is wrong.</param>
        /// <param name="fix">How to fix it.</param>
        /// <returns>The problem.</returns>
        private static CompositionProblem Problem(string where, string description, string fix)
        {
            return new CompositionProblem(TellmaEndpointAudit.CheckName, $"{where} {description}", fix);
        }
    }
}

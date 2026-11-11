// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Authentication;
using Tellma.Core.AspNetCore.Routing;
using Tellma.Core.Tenancy;

namespace Tellma.Core.AspNetCore.Tenancy
{
    /// <summary>
    ///     Binds a tenant request's context: resolves the tenant the route names from the registry's
    ///     snapshot, refuses one that is absent, retired or still provisioning, and binds the tenant and
    ///     the authenticated principal in one write. On the hub it is also the access guard's front door.
    ///     A request to an endpoint without tenant metadata proceeds tenantless.
    /// </summary>
    /// <param name="next">The rest of the pipeline.</param>
    /// <param name="registry">The tenant registry.</param>
    /// <param name="metrics">The tenancy instruments.</param>
    /// <param name="logger">The logger whose scope carries the tenant's members.</param>
    internal sealed class TenantMiddleware(
        RequestDelegate next,
        ITenantRegistry registry,
        TenancyMetrics metrics,
        ILogger<TenantMiddleware> logger)
    {
        /// <summary>The request activity's tag naming the tenant.</summary>
        public const string TenantIdTag = "tellma.tenant.id";

        /// <summary>Binds the tenant, then runs the rest of the pipeline inside the tenant's logger scope.</summary>
        /// <param name="context">The request.</param>
        /// <param name="holder">The request scope's context holder.</param>
        /// <param name="guard">The request scope's access guard, for the hub's front door.</param>
        /// <returns>A task that completes when the request is answered.</returns>
        public async Task InvokeAsync(HttpContext context, IRequestContextHolder holder, ITenantAccessGuard guard)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(holder);
            ArgumentNullException.ThrowIfNull(guard);

            // Only an endpoint a tenant group stamped resolves a tenant. Any other endpoint, an
            // anonymous one with a tenant id in its route included, stays tenantless and so cannot be
            // used to learn whether a tenant exists.
            TenantEndpointMetadata? metadata = context.GetEndpoint()?.Metadata.GetMetadata<TenantEndpointMetadata>();
            if (metadata is null)
            {
                await next(context).ConfigureAwait(false);
                return;
            }

            int tenantId = TenantIdOf(context);
            TenantInfo tenant = Resolve(tenantId);

            // --- One write: the tenant and the principal the endpoint's policy authenticated ---
            PrincipalPart principal = await PrincipalBinding.BindAsync(context).ConfigureAwait(false);
            RequestContext bound = holder.Current with
            {
                Tenant = tenant.Descriptor,
                Kind = principal.Kind,
                Subject = principal.Subject,
                ClientId = principal.ClientId,
                SessionId = principal.SessionId,
                SessionHandle = principal.SessionHandle,
                Assurance = principal.Assurance,
            };
            if (RequestInputs.ClientOf(context, metadata.Surface) is string client)
            {
                bound = bound with { Client = client };
            }

            holder.Set(bound);
            _ = Activity.Current?.SetTag(TenantIdTag, tenantId);
            metrics.RecordScope(TenantScopeKind.Request);

            using (logger.BeginScope(new TenantLogScope(holder)))
            {
                // --- The hub's front door: no endpoint filter reaches a hub's connection endpoints ---
                if (metadata.Surface == TenantSurface.Hub)
                {
                    await guard.EnsureAccessAsync(
                        new TenantAccessRequirement(IsMutation: false, Assurance: null),
                        RequestInputs.Of(context),
                        context.RequestAborted).ConfigureAwait(false);
                    holder.Current.ApplyCultures();
                }

                await next(context).ConfigureAwait(false);
            }
        }

        /// <summary>The tenant id the route names.</summary>
        /// <param name="context">The request.</param>
        /// <returns>The id.</returns>
        /// <exception cref="InvalidOperationException">The endpoint's route names none, which the endpoint audit refuses.</exception>
        private static int TenantIdOf(HttpContext context)
        {
            // The route constraint admits positive integers alone; anything else is a mapping the
            // audit should have refused, so it fails closed.
            object? value = context.GetRouteValue(TellmaEndpointAudit.TenantIdParameter);
            return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id > 0
                ? id
                : throw new InvalidOperationException("A tenant endpoint was matched without a positive tenantId route value.");
        }

        /// <summary>Resolves a tenant from the snapshot, refusing one no request may reach.</summary>
        /// <param name="tenantId">The tenant id.</param>
        /// <returns>The tenant.</returns>
        /// <exception cref="TenantNotFoundException">The tenant is unknown, retired or provisioning.</exception>
        /// <exception cref="TenantUnavailableException">The snapshot is beyond its staleness bound.</exception>
        private TenantInfo Resolve(int tenantId)
        {
            TenantInfo? tenant;
            try
            {
                tenant = registry.Find(tenantId);
            }
            catch (TenantUnavailableException unavailable) when (unavailable.Code == TenantProblemCodes.CatalogUnavailable)
            {
                metrics.RecordResolution(TenantResolutionOutcome.CatalogUnavailable);
                throw;
            }

            // A provisioning tenant answers exactly as an absent one; only the metric tells them apart.
            if (tenant is null || tenant.Descriptor.State == TenantState.Retired)
            {
                metrics.RecordResolution(TenantResolutionOutcome.NotFound);
                throw new TenantNotFoundException(tenantId);
            }

            if (tenant.Descriptor.State == TenantState.Provisioning)
            {
                metrics.RecordResolution(TenantResolutionOutcome.Provisioning);
                throw new TenantNotFoundException(tenantId);
            }

            return tenant;
        }
    }
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using System.Collections.Immutable;
using System.Text.Json;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.Provisioning;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tellma.Identity.Services
{
    /// <summary>
    ///     One request's <c>resource</c> parameters, with what the decision needs to know about
    ///     where they were sent.
    /// </summary>
    /// <param name="ClientId">The authenticated client that sent the request.</param>
    /// <param name="RequestedResources">The resource indicators the request names.</param>
    /// <param name="Scopes">The scopes the request asks for (already checked against the client's scope permissions).</param>
    /// <param name="GrantType">
    ///     The token request's grant type; null at the authorization, pushed-authorization and
    ///     device-authorization endpoints.
    /// </param>
    /// <param name="StoredResources">
    ///     The resources of the grant a presented authorization code, device code or refresh
    ///     token carries. When set, the request is evaluated against these alone, never against
    ///     the client's own grants, so a later request can narrow the original audience but never
    ///     widen it.
    /// </param>
    public sealed record ResourceGrantRequest(
        string ClientId,
        IReadOnlyCollection<string> RequestedResources,
        IReadOnlyCollection<string> Scopes,
        string? GrantType,
        IReadOnlyCollection<string>? StoredResources);

    /// <summary>
    ///     The single request-time decision point for <c>resource</c> parameters (RFC 8707). Every
    ///     endpoint that accepts one — authorization, pushed authorization, device authorization,
    ///     and the token endpoint for every grant — asks this service, which works out what the
    ///     requester holds and asks <see cref="IResourceGrantEvaluator" /> whether each requested
    ///     resource is covered. A refusal is audited here, once, whichever endpoint asked.
    /// </summary>
    /// <remarks>
    ///     <para>What a request is evaluated against, deny by default:</para>
    ///     <list type="bullet">
    ///         <item>
    ///             A request presenting a stored grant (an authorization code, device code or
    ///             refresh token) is evaluated against that grant's resources alone.
    ///         </item>
    ///         <item>
    ///             Any other request is evaluated against the client's resource permissions. A
    ///             document client is also granted every origin in the distribution-origin list.
    ///         </item>
    ///         <item>
    ///             A control-plane client — one permitted the control-plane scope — never gets the
    ///             per-tenant pattern. A <c>client_credentials</c> request of its that asks for the
    ///             control-plane scope may also name any origin in the distribution-origin list,
    ///             matched exactly.
    ///         </item>
    ///     </list>
    /// </remarks>
    /// <param name="applicationManager">Resolves the client and its permissions.</param>
    /// <param name="scopeManager">Reads the distribution-origin list (the distribution API scope's resources).</param>
    /// <param name="evaluator">The grant rule.</param>
    /// <param name="auditLogger">Records refusals.</param>
    /// <param name="httpContextAccessor">The request, for the audit row's client address.</param>
    /// <param name="logger">Logs refusals.</param>
    public sealed class ResourceGrantValidator(
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager,
        IResourceGrantEvaluator evaluator,
        IAuditLogger auditLogger,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ResourceGrantValidator> logger)
    {
        /// <summary>The error description a refused resource is answered with.</summary>
        public const string RefusedDescription = "The requested resource is not granted to this client.";

        /// <summary>
        ///     The longest requested resource an audit row or log line quotes; a longer value is cut
        ///     there, so a caller cannot fill the audit trail with one oversized parameter.
        /// </summary>
        public const int MaxQuotedResourceLength = 512;

        /// <summary>
        ///     Finds the first requested resource the requester does not hold, and audits it. A
        ///     request that names no resource has nothing to refuse.
        /// </summary>
        /// <param name="request">The request's resources and context.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>The refused resource, or null when every requested resource is granted.</returns>
        public async Task<string?> FindRefusedAsync(ResourceGrantRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (request.RequestedResources.Count == 0)
            {
                return null;
            }

            string? refused = await FindRefusedCoreAsync(request, cancellationToken);
            if (refused is not null)
            {
                await RecordRefusalAsync(request, refused, cancellationToken);
            }

            return refused;
        }

        /// <summary>Applies the rules of <see cref="ResourceGrantValidator" /> without recording anything.</summary>
        private async Task<string?> FindRefusedCoreAsync(ResourceGrantRequest request, CancellationToken cancellationToken)
        {
            // The client sent the request and was authenticated before this runs. A client that
            // cannot be found now holds nothing, so everything it names is refused.
            object? application = string.IsNullOrEmpty(request.ClientId)
                ? null
                : await applicationManager.FindByClientIdAsync(request.ClientId, cancellationToken);
            if (application is null)
            {
                return request.RequestedResources.First();
            }

            // The control plane is recognized by the one permission no other client holds, read
            // from the stored registration, never from anything the request asserts.
            bool isControlPlane = await applicationManager.HasPermissionAsync(
                application, Permissions.Prefixes.Scope + TellmaIdentityConstants.ControlPlaneScope, cancellationToken);

            // What the request is evaluated against: a stored grant's own resources, or the
            // client's grants (pattern-capable) plus, for the control plane, origins it may only
            // name exactly.
            IReadOnlySet<string> granted;
            IReadOnlySet<string> exactOnly = ImmutableHashSet<string>.Empty;
            if (request.StoredResources is not null)
            {
                granted = request.StoredResources.ToHashSet(StringComparer.Ordinal);
            }
            else
            {
                HashSet<string> clientGrants = await ListResourcePermissionsAsync(application, cancellationToken);

                ImmutableDictionary<string, JsonElement> properties =
                    await applicationManager.GetPropertiesAsync(application, cancellationToken);
                if (!isControlPlane && TellmaClientProperties.IsSet(properties, TellmaClientProperties.DocumentClient))
                {
                    clientGrants.UnionWith(await ListDistributionOriginsAsync(cancellationToken));
                }

                if (isControlPlane
                    && string.Equals(request.GrantType, GrantTypes.ClientCredentials, StringComparison.Ordinal)
                    && request.Scopes.Contains(TellmaIdentityConstants.ControlPlaneScope, StringComparer.Ordinal))
                {
                    exactOnly = await ListDistributionOriginsAsync(cancellationToken);
                }

                granted = clientGrants;
            }

            foreach (string resource in request.RequestedResources)
            {
                bool isGranted = isControlPlane
                    ? evaluator.IsGrantedExactly(granted, resource) || evaluator.IsGrantedExactly(exactOnly, resource)
                    : evaluator.IsGranted(granted, resource);
                if (!isGranted)
                {
                    return resource;
                }
            }

            return null;
        }

        /// <summary>The client's resource permissions, without their <c>rsrc:</c> prefix.</summary>
        private async Task<HashSet<string>> ListResourcePermissionsAsync(object application, CancellationToken cancellationToken)
        {
            HashSet<string> resources = new(StringComparer.Ordinal);
            foreach (string permission in await applicationManager.GetPermissionsAsync(application, cancellationToken))
            {
                if (permission.StartsWith(Permissions.Prefixes.Resource, StringComparison.Ordinal)
                    && permission.Length > Permissions.Prefixes.Resource.Length)
                {
                    resources.Add(permission[Permissions.Prefixes.Resource.Length..]);
                }
            }

            return resources;
        }

        /// <summary>
        ///     The distribution-origin list: the distribution API scope's resources, as onboarding
        ///     appends every distribution's origin to them. Only canonical origins count, so a
        ///     malformed entry can never anchor anything.
        /// </summary>
        private async Task<HashSet<string>> ListDistributionOriginsAsync(CancellationToken cancellationToken)
        {
            HashSet<string> origins = new(StringComparer.Ordinal);
            object? scope = await scopeManager.FindByNameAsync(TellmaIdentityConstants.ApiScope, cancellationToken);
            if (scope is null)
            {
                return origins;
            }

            foreach (string resource in await scopeManager.GetResourcesAsync(scope, cancellationToken))
            {
                if (ResourceGrantEvaluator.IsOrigin(resource))
                {
                    origins.Add(resource);
                }
            }

            return origins;
        }

        /// <summary>Audits and logs one refused resource, quoting at most <see cref="MaxQuotedResourceLength" /> characters of it.</summary>
        private async Task RecordRefusalAsync(ResourceGrantRequest request, string refused, CancellationToken cancellationToken)
        {
            bool truncated = refused.Length > MaxQuotedResourceLength;
            string quoted = truncated ? refused[..MaxQuotedResourceLength] : refused;

            ResourceGrantValidatorLog.Refused(logger, request.ClientId, quoted, request.GrantType);

            await auditLogger.LogAsync(
                new AuditEventEntry
                {
                    Action = AuditActions.ResourceRefused,
                    ClientId = request.ClientId,
                    IpAddress = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
                    Outcome = "failure",
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        requested = quoted,
                        truncated,
                        grantType = request.GrantType,
                        storedGrant = request.StoredResources is not null,
                    }),
                },
                cancellationToken);
        }
    }

    /// <summary>Source-generated log messages for <see cref="ResourceGrantValidator" />.</summary>
    internal static partial class ResourceGrantValidatorLog
    {
        /// <summary>A requested resource was refused.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="clientId">The requesting client.</param>
        /// <param name="resource">The refused resource, possibly cut short.</param>
        /// <param name="grantType">The token request's grant type, when at the token endpoint.</param>
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Refused resource {Resource} for client {ClientId} (grant type {GrantType}).")]
        public static partial void Refused(ILogger logger, string clientId, string resource, string? grantType);
    }
}

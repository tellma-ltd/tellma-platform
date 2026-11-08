// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using System.Globalization;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     The management API's versions. A version is the API's own integer, independent of the
    ///     engine's release: an additive change (a new endpoint, response member or optional
    ///     request member) stays inside a version, and a breaking change is mapped as the next
    ///     version beside the current one, which is removed once nothing calls it.
    /// </summary>
    public static class ManagementApiVersions
    {
        /// <summary>The first and current version, mapped under <c>api/identity/v1</c>.</summary>
        public const int V1 = 1;

        /// <summary>The route segment of a version, for example <c>v1</c>.</summary>
        /// <param name="version">The version.</param>
        /// <returns>The segment.</returns>
        public static string Segment(int version)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);

            return "v" + version.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The route template every endpoint of a version is mapped under.</summary>
        /// <param name="version">The version.</param>
        /// <returns>The template, for example <c>api/identity/v1</c>, relative to the authority.</returns>
        public static string RoutePrefix(int version)
        {
            return "api/identity/" + Segment(version);
        }
    }

    /// <summary>
    ///     The closed set of management-API endpoint names, the values of the
    ///     <see cref="IdentityMetrics.ApiEndpointTag" /> tag. Each names an operation, not a path,
    ///     so a path parameter never becomes a tag value.
    /// </summary>
    public static class ManagementApiEndpoints
    {
        /// <summary>The bulk invite.</summary>
        public const string Invite = "invitations.invite";

        /// <summary>The bulk invitation delivery status.</summary>
        public const string DeliveryStatus = "invitations.delivery-status";

        /// <summary>Creating a service account.</summary>
        public const string CreateServiceAccount = "service-accounts.create";

        /// <summary>Reading a service account.</summary>
        public const string GetServiceAccount = "service-accounts.get";

        /// <summary>Deleting a service account.</summary>
        public const string DeleteServiceAccount = "service-accounts.delete";

        /// <summary>The operator's global user lookup.</summary>
        public const string GetUser = "users.get";

        /// <summary>The operator's Temporary Access Pass issuance.</summary>
        public const string IssueTemporaryAccessPass = "users.issue-temporary-access-pass";

        /// <summary>Every endpoint name.</summary>
        public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
        {
            Invite,
            DeliveryStatus,
            CreateServiceAccount,
            GetServiceAccount,
            DeleteServiceAccount,
            GetUser,
            IssueTemporaryAccessPass,
        };
    }

    /// <summary>
    ///     Maps a management-API controller under one version's route prefix
    ///     (<c>api/identity/v{n}</c>, relative to the authority) and counts and logs every call that
    ///     reaches it. The version lives in this one attribute, so a controller's routes and its
    ///     telemetry cannot disagree about which version they serve.
    /// </summary>
    /// <remarks>
    ///     The call is recorded by a resource filter, which runs once the request has been
    ///     authorized and before model binding: a refused token never reaches it, so anonymous
    ///     traffic can neither inflate the counter nor fill the log, and a malformed body from an
    ///     authorized caller is still counted.
    /// </remarks>
    /// <param name="version">The version, one of <see cref="ManagementApiVersions" />.</param>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ManagementApiAttribute(int version) : Attribute, IRouteTemplateProvider, IFilterFactory
    {
        /// <summary>The version the controller serves.</summary>
        public int Version { get; } = version;

        /// <summary>The version's route segment, for example <c>v1</c>.</summary>
        public string Segment { get; } = ManagementApiVersions.Segment(version);

        /// <inheritdoc />
        public string Template { get; } = ManagementApiVersions.RoutePrefix(version);

        /// <inheritdoc />
        int? IRouteTemplateProvider.Order => null;

        /// <inheritdoc />
        string? IRouteTemplateProvider.Name => null;

        /// <inheritdoc />
        public bool IsReusable => true;

        /// <inheritdoc />
        public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider);

            return new ManagementApiCallFilter(
                Segment,
                serviceProvider.GetRequiredService<IdentityMetrics>(),
                serviceProvider.GetRequiredService<ILogger<ManagementApiCallFilter>>());
        }
    }

    /// <summary>
    ///     Names a management-API action for the call counter and log, from the closed set in
    ///     <see cref="ManagementApiEndpoints" />. Every action of a <see cref="ManagementApiAttribute" />
    ///     controller carries one.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ManagementApiEndpointAttribute : Attribute
    {
        /// <summary>Names the action.</summary>
        /// <param name="name">The endpoint name, one of <see cref="ManagementApiEndpoints" />.</param>
        public ManagementApiEndpointAttribute(string name)
        {
            // A name outside the closed set would be an unbounded tag value; refusing it here
            // fails when MVC builds its model at startup, not on the first call.
            if (!ManagementApiEndpoints.All.Contains(name))
            {
                throw new ArgumentException($"'{name}' is not a management API endpoint name.", nameof(name));
            }

            Name = name;
        }

        /// <summary>The endpoint name.</summary>
        public string Name { get; }
    }

    /// <summary>Resolves who is calling the management API.</summary>
    public static class ManagementApiCaller
    {
        /// <summary>
        ///     The calling client's id: the token's <c>client_id</c>, or its subject for a machine
        ///     token that names its client only there.
        /// </summary>
        /// <param name="principal">The authenticated principal.</param>
        /// <returns>The client id, or null when the token names none.</returns>
        public static string? ClientIdOf(ClaimsPrincipal principal)
        {
            ArgumentNullException.ThrowIfNull(principal);

            return principal.GetClaim(Claims.ClientId) ?? principal.GetClaim(Claims.Subject);
        }
    }

    /// <summary>
    ///     Counts one call on <see cref="IdentityMetrics.ApiCallsInstrumentName" /> and logs it
    ///     with the calling client, so an operator sees when a version falls silent and who still
    ///     calls it.
    /// </summary>
    /// <param name="segment">The version's route segment, for example <c>v1</c>.</param>
    /// <param name="metrics">Identity metrics.</param>
    /// <param name="logger">The call log.</param>
    internal sealed class ManagementApiCallFilter(
        string segment, IdentityMetrics metrics, ILogger<ManagementApiCallFilter> logger) : IResourceFilter
    {
        /// <inheritdoc />
        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // The endpoint name comes from the action's own metadata; an action without one is a
            // programming error that the endpoint-name tests catch, and it is refused rather than
            // counted under a made-up name.
            string endpoint = context.ActionDescriptor.EndpointMetadata
                .OfType<ManagementApiEndpointAttribute>()
                .LastOrDefault()?.Name
                ?? throw new InvalidOperationException(
                    $"The management API action '{context.ActionDescriptor.DisplayName}' declares no endpoint name.");

            metrics.ApiCall(segment, endpoint);

            // The client id is a registration name, never a secret, and reaches the log only:
            // it is unbounded, so it is never a metric tag.
            string clientId = ManagementApiCaller.ClientIdOf(context.HttpContext.User) ?? "unknown";
            ManagementApiLog.Called(logger, segment, endpoint, clientId);
        }

        /// <inheritdoc />
        public void OnResourceExecuted(ResourceExecutedContext context)
        {
            // Nothing to do after the call: it was counted when it arrived.
        }
    }

    /// <summary>Source-generated log messages for the management API.</summary>
    internal static partial class ManagementApiLog
    {
        /// <summary>A management-API call reached its endpoint.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="version">The version's route segment.</param>
        /// <param name="endpoint">The endpoint name.</param>
        /// <param name="clientId">The calling client.</param>
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Management API {Version} {Endpoint} called by client {ClientId}.")]
        public static partial void Called(ILogger logger, string version, string endpoint, string clientId);
    }
}

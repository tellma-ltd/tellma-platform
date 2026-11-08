// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
    ///     A call is recorded exactly once, by one of two seams. A call the authorization
    ///     middleware refuses never reaches MVC, so <see cref="ManagementApiAuthorizationResultHandler" />
    ///     records it as <see cref="ManagementApiOutcomes.Unauthorized" /> or
    ///     <see cref="ManagementApiOutcomes.Forbidden" />. An authorized call is recorded by the
    ///     resource filter this attribute creates, once the action and its result have run, as
    ///     <see cref="ManagementApiOutcomes.Ok" /> or <see cref="ManagementApiOutcomes.Error" />.
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

            return new ManagementApiCallFilter(Segment, serviceProvider.GetRequiredService<ManagementApiCallRecorder>());
        }
    }

    /// <summary>
    ///     The closed set of management-API call outcomes, the values of the
    ///     <see cref="IdentityMetrics.ApiOutcomeTag" /> tag.
    /// </summary>
    public static class ManagementApiOutcomes
    {
        /// <summary>The call was authorized and answered without a server error.</summary>
        public const string Ok = "ok";

        /// <summary>The call was authorized and failed with an unhandled exception or a 5xx status.</summary>
        public const string Error = "error";

        /// <summary>The call presented no valid token and was challenged (401).</summary>
        public const string Unauthorized = "unauthorized";

        /// <summary>The call's token lacked what the endpoint's policy requires (403).</summary>
        public const string Forbidden = "forbidden";
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
    ///     calls it — including a caller whose every call is refused.
    /// </summary>
    /// <param name="metrics">Identity metrics.</param>
    /// <param name="logger">The call log.</param>
    internal sealed class ManagementApiCallRecorder(IdentityMetrics metrics, ILogger<ManagementApiCallRecorder> logger)
    {
        /// <summary>The log's client value for a call whose token names no client.</summary>
        internal const string NoClient = "none";

        /// <summary>Records one call.</summary>
        /// <param name="segment">The version's route segment, for example <c>v1</c>.</param>
        /// <param name="endpoint">The endpoint name, one of <see cref="ManagementApiEndpoints" />.</param>
        /// <param name="outcome">The outcome, one of <see cref="ManagementApiOutcomes" />.</param>
        /// <param name="principal">The request's principal; empty for a call without a valid token.</param>
        public void Record(string segment, string endpoint, string outcome, ClaimsPrincipal principal)
        {
            metrics.ApiCall(segment, endpoint, outcome);

            // The client id is a registration name, never a secret, and reaches the log only: it
            // is unbounded, so it is never a metric tag. A challenged call has no valid token and
            // therefore no client to name.
            string clientId = ManagementApiCaller.ClientIdOf(principal) ?? NoClient;
            ManagementApiLog.Called(logger, segment, endpoint, clientId, outcome);
        }

        /// <summary>
        ///     The endpoint name an action or endpoint declares, from its metadata, or null when it
        ///     is not a management-API endpoint.
        /// </summary>
        /// <param name="metadata">The endpoint's metadata, which carries class and action attributes.</param>
        /// <returns>The version segment and endpoint name, or null.</returns>
        public static (string Segment, string Endpoint)? Describe(IEnumerable<object> metadata)
        {
            ManagementApiAttribute? api = null;
            ManagementApiEndpointAttribute? endpoint = null;
            foreach (object item in metadata)
            {
                api = item as ManagementApiAttribute ?? api;
                endpoint = item as ManagementApiEndpointAttribute ?? endpoint;
            }

            return api is not null && endpoint is not null ? (api.Segment, endpoint.Name) : null;
        }
    }

    /// <summary>
    ///     Records an authorized management-API call once its action and result have run:
    ///     <see cref="ManagementApiOutcomes.Error" /> for an unhandled exception or a 5xx status,
    ///     <see cref="ManagementApiOutcomes.Ok" /> otherwise.
    /// </summary>
    /// <param name="segment">The version's route segment, for example <c>v1</c>.</param>
    /// <param name="recorder">Records the call.</param>
    internal sealed class ManagementApiCallFilter(string segment, ManagementApiCallRecorder recorder) : IResourceFilter
    {
        /// <inheritdoc />
        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // An action without an endpoint name is a programming error that the endpoint-name
            // tests catch; it is refused before it runs rather than counted under a made-up name.
            _ = EndpointOf(context.ActionDescriptor);
        }

        /// <inheritdoc />
        public void OnResourceExecuted(ResourceExecutedContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // An exception still unhandled here becomes a 500; a handled one has already been
            // turned into whatever response the status code now shows.
            bool failed = (context.Exception is not null && !context.ExceptionHandled)
                || context.HttpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError;

            recorder.Record(
                segment,
                EndpointOf(context.ActionDescriptor),
                failed ? ManagementApiOutcomes.Error : ManagementApiOutcomes.Ok,
                context.HttpContext.User);
        }

        /// <summary>The endpoint name the action declares.</summary>
        private static string EndpointOf(Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor action)
        {
            return action.EndpointMetadata.OfType<ManagementApiEndpointAttribute>().LastOrDefault()?.Name
                ?? throw new InvalidOperationException(
                    $"The management API action '{action.DisplayName}' declares no endpoint name.");
        }
    }

    /// <summary>
    ///     Records the management-API calls the authorization middleware refuses, which never reach
    ///     MVC and so never reach <see cref="ManagementApiAttribute" />'s filter, then hands every
    ///     result to the handler it decorates. It records nothing for any other endpoint and never
    ///     changes the response.
    /// </summary>
    /// <param name="inner">The handler that writes the response: the host's, or the framework default.</param>
    /// <param name="recorder">Records the call.</param>
    internal sealed class ManagementApiAuthorizationResultHandler(
        IAuthorizationMiddlewareResultHandler inner, ManagementApiCallRecorder recorder) : IAuthorizationMiddlewareResultHandler
    {
        /// <inheritdoc />
        public Task HandleAsync(
            RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(authorizeResult);

            // A challenge means no valid token; a forbid means an authenticated caller whose token
            // lacks the policy's scope. The policy evaluator has already set the request's
            // principal to the policy scheme's, so a forbidden call names its client.
            if ((authorizeResult.Challenged || authorizeResult.Forbidden)
                && context.GetEndpoint() is { } endpoint
                && ManagementApiCallRecorder.Describe(endpoint.Metadata) is { } call)
            {
                recorder.Record(
                    call.Segment,
                    call.Endpoint,
                    authorizeResult.Forbidden ? ManagementApiOutcomes.Forbidden : ManagementApiOutcomes.Unauthorized,
                    context.User);
            }

            return inner.HandleAsync(next, context, policy, authorizeResult);
        }

        /// <summary>
        ///     Decorates the authorization result handler registered so far, or the framework
        ///     default when there is none, keeping its lifetime.
        /// </summary>
        /// <param name="services">The service collection, after <c>AddAuthorization</c>.</param>
        internal static void Decorate(IServiceCollection services)
        {
            ServiceDescriptor? existing = services.LastOrDefault(
                static descriptor => descriptor.ServiceType == typeof(IAuthorizationMiddlewareResultHandler));
            if (existing is not null)
            {
                services.Remove(existing);
            }

            ServiceLifetime lifetime = existing?.Lifetime ?? ServiceLifetime.Singleton;
            services.Add(new ServiceDescriptor(
                typeof(IAuthorizationMiddlewareResultHandler),
                provider => new ManagementApiAuthorizationResultHandler(
                    CreateInner(provider, existing), provider.GetRequiredService<ManagementApiCallRecorder>()),
                lifetime));

            // A host registering its own handler after the engine would silently replace this one
            // and leave refused calls uncounted; the startup check refuses that composition.
            services.AddSingleton<IStartupFilter, AuthorizationResultHandlerStartupCheck>();
        }

        /// <summary>Builds the decorated handler from its original registration.</summary>
        private static IAuthorizationMiddlewareResultHandler CreateInner(IServiceProvider provider, ServiceDescriptor? existing)
        {
            return existing switch
            {
                null => new AuthorizationMiddlewareResultHandler(),
                { ImplementationInstance: IAuthorizationMiddlewareResultHandler instance } => instance,
                { ImplementationFactory: { } factory } => (IAuthorizationMiddlewareResultHandler)factory(provider),
                { ImplementationType: { } type } =>
                    (IAuthorizationMiddlewareResultHandler)ActivatorUtilities.CreateInstance(provider, type),
                _ => throw new InvalidOperationException(
                    "The registered authorization result handler cannot be decorated."),
            };
        }

        /// <summary>
        ///     Refuses to start a host whose authorization result handler is not the engine's
        ///     decorator, which happens when the host registers its own after the engine's.
        /// </summary>
        private sealed class AuthorizationResultHandlerStartupCheck : IStartupFilter
        {
            /// <inheritdoc />
            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            {
                return app =>
                {
                    if (app.ApplicationServices.GetRequiredService<IAuthorizationMiddlewareResultHandler>()
                        is not ManagementApiAuthorizationResultHandler)
                    {
                        throw new InvalidOperationException(
                            "The identity engine's authorization result handler was replaced. Register a custom "
                            + "IAuthorizationMiddlewareResultHandler before AddTellmaIdentity, which decorates it.");
                    }

                    next(app);
                };
            }
        }
    }

    /// <summary>Source-generated log messages for the management API.</summary>
    internal static partial class ManagementApiLog
    {
        /// <summary>A management-API call was answered or refused.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="version">The version's route segment.</param>
        /// <param name="endpoint">The endpoint name.</param>
        /// <param name="clientId">The calling client, or <c>none</c> without a valid token.</param>
        /// <param name="outcome">The outcome.</param>
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Management API {Version} {Endpoint} called by client {ClientId}: {Outcome}.")]
        public static partial void Called(ILogger logger, string version, string endpoint, string clientId, string outcome);
    }
}

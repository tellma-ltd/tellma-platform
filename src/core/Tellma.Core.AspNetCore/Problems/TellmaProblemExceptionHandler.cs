// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Api;
using Tellma.Core.AspNetCore.Api;
using Tellma.Core.AspNetCore.Observability;

namespace Tellma.Core.AspNetCore.Problems
{
    // Spec 0016 §7.6: registered with the exception middleware, which UseTellma places ahead of
    // routing, so it covers the endpoint filters, the tenant middleware and hand-mapped endpoints.
    /// <summary>
    ///     Answers every exception that reaches the web edge with its problem: the closed set by
    ///     type, the framework's request refusals by status, anything else as <c>500 internal</c>
    ///     with the trace id alone, logged with its exception. A caller that went away gets no
    ///     answer and no log line.
    /// </summary>
    /// <param name="environment">The host environment; Development adds the exception's type and message to the backstop.</param>
    /// <param name="loggerFactory">Creates the web API's logger.</param>
    internal sealed class TellmaProblemExceptionHandler(IHostEnvironment environment, ILoggerFactory loggerFactory) : IExceptionHandler
    {
        /// <summary>The route model's name of a stack's save.</summary>
        private const string SaveOperation = "save";

        /// <summary>The web API's logger.</summary>
        private readonly ILogger _logger = (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger(ApiLog.Category);

        /// <summary>Whether an exception is a cancellation of a request its caller abandoned.</summary>
        /// <param name="context">The request.</param>
        /// <param name="exception">The exception.</param>
        /// <returns>True when the request's own token is cancelled and the exception is a cancellation.</returns>
        public static bool IsAbandoned(HttpContext context, Exception exception)
        {
            ArgumentNullException.ThrowIfNull(context);
            return exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested;
        }

        /// <summary>What the mapping needs to know of the endpoint that raised an exception.</summary>
        /// <param name="endpoint">The endpoint, as the exception middleware kept it; null for a request no endpoint matched.</param>
        /// <param name="services">The request's services, which hold the stack registry.</param>
        /// <param name="development">Whether the host runs in Development.</param>
        /// <returns>The mapping's context.</returns>
        public static ProblemMappingContext ContextOf(Endpoint? endpoint, IServiceProvider services, bool development)
        {
            ArgumentNullException.ThrowIfNull(services);

            // --- A projected route names the member a validation path's leading index points into ---
            RouteContract? route = endpoint?.Metadata.GetMetadata<ProjectedRouteMetadata>()?.Route;
            (ValidationRoot root, Type? rows) = route switch
            {
                { Action: EntityActionDescriptor { SingleTarget: true } } => (ValidationRoot.SingleId, EntityTypeOf(route, services)),
                { Action: EntityActionDescriptor } => (ValidationRoot.Ids, EntityTypeOf(route, services)),
                { Action: null, Operation: SaveOperation } => (ValidationRoot.Entities, EntityTypeOf(route, services)),
                _ => (ValidationRoot.Body, null),
            };
            return new ProblemMappingContext(root, rows, development);
        }

        /// <inheritdoc />
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            ArgumentNullException.ThrowIfNull(exception);

            // --- A caller that went away gets no answer ---
            if (IsAbandoned(httpContext, exception))
            {
                return true;
            }

            // --- The problem; the endpoint is the one the middleware kept, as it cleared the request's ---
            Endpoint? endpoint = httpContext.Features.Get<IExceptionHandlerFeature>()?.Endpoint;
            ProblemDescription problem = ProblemMapper.Map(exception, ContextOf(endpoint, httpContext.RequestServices, environment.IsDevelopment()));
            if (problem.IsUnhandled)
            {
                using (ProblemWriter.BeginScopes(httpContext, _logger))
                {
                    ApiLog.UnhandledException(_logger, exception, TraceIdentity.Of(httpContext));
                }
            }

            await ProblemWriter.WriteAsync(httpContext, problem).ConfigureAwait(false);
            return true;
        }

        /// <summary>The entity type of a stack route's rows: the entity of the stack its resource names.</summary>
        /// <param name="route">The route.</param>
        /// <param name="services">The request's services.</param>
        /// <returns>The entity type, or null when no composed stack serves the route's resource.</returns>
        private static Type? EntityTypeOf(RouteContract route, IServiceProvider services)
        {
            return route.Resource is string resource && services.GetService<IStackRegistry>()?.Find(resource) is StackDescriptor stack
                ? stack.EntityType
                : null;
        }
    }
}

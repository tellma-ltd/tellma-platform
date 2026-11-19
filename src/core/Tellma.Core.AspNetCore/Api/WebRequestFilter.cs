// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using System.Diagnostics;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Crud.Validation;

namespace Tellma.Core.AspNetCore.Api
{
    // Spec 0016 §5.1 and §6.1: on the Web group the filter runs after the tenant filter, so a caller
    // without access learns nothing of the header's rules, and before the handler, whose service
    // reads the confirmed keys. It evaluates no securable.

    /// <summary>
    ///     The web surface's request filter: tags the request's activity with a projected endpoint's
    ///     resource and operation, and seeds the request scope's confirmed keys from the
    ///     <c>Tellma-Confirm</c> header before the handler runs.
    /// </summary>
    internal sealed class WebRequestFilter : IEndpointFilter
    {
        /// <inheritdoc />
        public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(next);
            HttpContext http = context.HttpContext;

            // --- The activity tags of a projected endpoint: closed sets, from the route model ---
            if (http.GetEndpoint()?.Metadata.GetMetadata<ProjectedRouteMetadata>() is ProjectedRouteMetadata projected
                && http.Features.Get<IHttpActivityFeature>()?.Activity is Activity activity)
            {
                _ = activity
                    .SetTag(ApiTelemetryNames.ResourceTag, projected.TagResource)
                    .SetTag(ApiTelemetryNames.OperationTag, projected.Route.Operation);
            }

            // --- The confirmed keys: seeded once, only when the caller states some ---
            if (http.Request.Headers.TryGetValue(TellmaHeaders.Confirm, out StringValues confirm))
            {
                IReadOnlyList<string> keys = ConfirmationHeader.Parse(confirm);
                http.RequestServices.GetRequiredService<Confirmations>().Seed(keys);
            }

            return next(context);
        }
    }
}

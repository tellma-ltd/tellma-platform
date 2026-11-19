// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Mvc;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.AspNetCore.Api
{
    // Spec 0016 §4.1: a child query surface's query calls the child query service with the surface's
    // resource, which evaluates the surface's Read and composes the owner's filter.

    /// <summary>The web endpoint of one child query surface's <c>query</c>.</summary>
    /// <param name="resource">The surface's resource.</param>
    internal sealed class ChildQueryEndpoint(string resource)
    {
        /// <summary>The surface's resource.</summary>
        public string Resource { get; } = !string.IsNullOrWhiteSpace(resource)
            ? resource
            : throw new ArgumentException("A child query surface names its resource.", nameof(resource));

        /// <summary>The surface's <c>query</c>: one page of child rows.</summary>
        /// <param name="request">The query.</param>
        /// <param name="service">The child query service.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>The page.</returns>
        public Task<RowPage> QueryAsync(
            [FromBody] QueryRequest request,
            [FromServices] IChildQueryService service,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(service);
            return service.QueryAsync(Resource, request, cancellationToken);
        }
    }
}

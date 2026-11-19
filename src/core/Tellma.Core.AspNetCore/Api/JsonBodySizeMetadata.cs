// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;

namespace Tellma.Core.AspNetCore.Api
{
    /// <summary>
    ///     The largest JSON body a web endpoint reads. Routing applies it to the server's body limit,
    ///     so a larger body is refused before the endpoint reads it.
    /// </summary>
    /// <param name="maxBytes">The limit in bytes; positive.</param>
    internal sealed class JsonBodySizeMetadata(long maxBytes) : IRequestSizeLimitMetadata
    {
        /// <inheritdoc />
        public long? MaxRequestBodySize { get; } = maxBytes > 0
            ? maxBytes
            : throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "A JSON body limit is positive.");

        /// <summary>
        ///     Stamps the JSON body limit on an endpoint that states none of its own: an endpoint
        ///     accepting a binary body, or carrying a request size limit already, keeps its own.
        /// </summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="maxBytes">The limit in bytes; positive.</param>
        public static void Apply(EndpointBuilder endpoint, long maxBytes)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            if (!endpoint.Metadata.Any(static item => item is IRequestSizeLimitMetadata or AcceptsBinaryMetadata))
            {
                endpoint.Metadata.Add(new JsonBodySizeMetadata(maxBytes));
            }
        }
    }
}

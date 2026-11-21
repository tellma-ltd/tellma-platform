// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Options;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Routing;

namespace Tellma.Core.AspNetCore.Limits
{
    // Spec 0016 §8.4: Brotli then Gzip at Fastest, over HTTPS too, for JSON alone. No secret is
    // reflected into a compressed body: the session cookie is HttpOnly and never echoed, no CSRF
    // token exists, Tellma-Client is constant, and validation messages echo user input next to
    // nothing secret. Event streams, workbooks and blobs are never compressed.

    /// <summary>
    ///     The response compression decision: a JSON or problem body of at least a kilobyte, or of a
    ///     length not declared, never a blob surface response, and nothing at all when the
    ///     configuration turns compression off.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="options">The compression options, whose media types are JSON's alone.</param>
    /// <param name="api">The web API's options, read per response so the startup gate judges them first.</param>
    internal sealed class JsonResponseCompressionProvider(IServiceProvider services, IOptions<ResponseCompressionOptions> options, IOptions<ApiOptions> api)
        : ResponseCompressionProvider(services, options)
    {
        /// <summary>The smallest declared body worth compressing, in bytes.</summary>
        public const long MinimumBytes = 1024;

        /// <summary>The media types compressed: JSON and the problem body.</summary>
        public static readonly IReadOnlyList<string> MimeTypes = ["application/json", "application/problem+json"];

        /// <inheritdoc />
        public override bool ShouldCompressResponse(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // --- Turned off by configuration ---
            if (!api.Value.EnableCompression)
            {
                return false;
            }

            // --- A blob's bytes are the blob's own: already compressed, or ranged, or both ---
            if (context.GetEndpoint()?.Metadata.GetMetadata<TellmaGroupMetadata>() is { Group: TellmaGroup.Blobs })
            {
                return false;
            }

            // --- A short declared body gains nothing; a streamed one is judged by its type alone ---
            return (context.Response.ContentLength is not long length || length >= MinimumBytes)
                && base.ShouldCompressResponse(context);
        }
    }
}

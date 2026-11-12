// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Csrf;
using Tellma.Core.AspNetCore.Hosting;

namespace Tellma.Core.AspNetCore.Tenancy
{
    /// <summary>What a request asks of its context: its locale preferences and the client that sent it.</summary>
    internal static class RequestInputs
    {
        /// <summary>The client name of every MCP request.</summary>
        public const string McpClient = "mcp";

        /// <summary>The request's inputs to the context initializers.</summary>
        /// <param name="context">The request.</param>
        /// <returns>The inputs; the message-level preferences are the web API's to add.</returns>
        public static RequestContextInputs Of(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            IHeaderDictionary headers = context.Request.Headers;
            return new RequestContextInputs(
                Single(headers[HeaderNames.AcceptLanguage]),
                Single(headers[TellmaHeaders.Calendar]),
                Single(headers[TellmaHeaders.TimeZone]),
                ClientOf(context, context.GetEndpoint()?.Metadata.GetMetadata<TenantEndpointMetadata>()?.Surface),
                MessageLanguage: null,
                MessageCalendar: null);
        }

        /// <summary>
        ///     The request's client name: <c>mcp</c> on the MCP surface, else the name the
        ///     <c>Tellma-Client</c> header carries when it is well-formed and configured.
        /// </summary>
        /// <param name="context">The request.</param>
        /// <param name="surface">The tenant surface, if any.</param>
        /// <returns>The name, or null for the context's default.</returns>
        public static string? ClientOf(HttpContext context, TenantSurface? surface)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (surface == TenantSurface.Mcp)
            {
                return McpClient;
            }

            // The names are a closed set, so an unknown name falls to the default rather than
            // reaching a log or a trace tag.
            IReadOnlySet<string> names = context.RequestServices.GetService<IOptions<ApiOptions>>()?.Value.ClientNames ?? new ApiOptions().ClientNames;
            return CsrfRules.ClientName(context.Request.Headers[TellmaAuthentication.CsrfHeaderName], names);
        }

        /// <summary>A header's one value.</summary>
        /// <param name="values">The header's values.</param>
        /// <returns>The value, or null when there are none or several.</returns>
        private static string? Single(StringValues values)
        {
            return values.Count == 1 && !string.IsNullOrWhiteSpace(values[0]) ? values[0] : null;
        }
    }
}

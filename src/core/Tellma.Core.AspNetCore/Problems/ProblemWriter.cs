// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Observability;

namespace Tellma.Core.AspNetCore.Problems
{
    /// <summary>
    ///     Writes an RFC 9457 problem body: <c>type</c> from the code, an English <c>title</c>, the
    ///     status, an optional <c>detail</c>, the trace id as <c>instance</c>, the <c>code</c>, an empty
    ///     <c>errors</c> list and the problem's raw <c>arguments</c>.
    /// </summary>
    internal static class ProblemWriter
    {
        /// <summary>The problem body's media type.</summary>
        public const string ContentType = "application/problem+json";

        /// <summary>Writes a problem as the response, replacing its status.</summary>
        /// <param name="context">The request.</param>
        /// <param name="status">The status code.</param>
        /// <param name="code">The kebab-case problem code.</param>
        /// <param name="detail">A sentence on this occurrence, or null.</param>
        /// <param name="arguments">The problem's raw display data, or null.</param>
        /// <returns>A task that completes when the body is written.</returns>
        public static Task WriteAsync(
            HttpContext context, int status, string code, string? detail = null, IReadOnlyDictionary<string, object?>? arguments = null)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentException.ThrowIfNullOrWhiteSpace(code);

            ApiOptions api = context.RequestServices.GetService<IOptions<ApiOptions>>()?.Value ?? new ApiOptions();
            ProblemDocument document = new(
                api.ProblemTypeBase + code,
                Title(code, status),
                status,
                detail,
                TraceIdentity.Of(context),
                code,
                [],
                arguments is { Count: > 0 } ? arguments : null);

            context.Response.StatusCode = status;
            context.Response.ContentType = ContentType;
            return JsonSerializer.SerializeAsync(context.Response.Body, document, TellmaJson.Options, context.RequestAborted);
        }

        /// <summary>
        ///     The English title of a problem code. The localized titles of the string packs replace these
        ///     once the problem mapping renders under the request culture.
        /// </summary>
        /// <param name="code">The code.</param>
        /// <param name="status">The status, for a code without a title of its own.</param>
        /// <returns>The title.</returns>
        private static string Title(string code, int status)
        {
            return code switch
            {
                "tenant-not-found" => "The tenant was not found",
                "catalog-unavailable" => "The tenant catalog is unavailable",
                "tenant-schema-behind" => "The tenant is being upgraded",
                "tenant-suspended" => "The tenant is suspended",
                "tenant-read-only" => "The tenant is read-only",
                "tenant-frozen" => "The tenant is frozen",
                "tenant-registration-refused" => "The tenant registration was refused",
                "tenant-illegal-transition" => "The tenant cannot move to that state",
                "tenant-location-refused" => "The tenant location was refused",
                "step-up-required" => "Additional authentication is required",
                "individual-required" => "An individual user is required",
                "csrf-rejected" => "The request was refused as a possible cross-site request",
                "unauthenticated" => "Authentication is required",
                "forbidden" => "Access is denied",
                "bad-request" => "The request is malformed",
                "not-found" => "Not found",
                "route-not-found" => "No such route",
                "provisioning-not-configured" => "Tenant provisioning is not available",
                "internal" => "An unexpected error occurred",
                _ => string.Create(CultureInfo.InvariantCulture, $"Request failed with status {status}"),
            };
        }
    }
}

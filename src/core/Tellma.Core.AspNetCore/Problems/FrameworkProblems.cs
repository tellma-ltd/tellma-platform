// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using System.Globalization;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Routing;

namespace Tellma.Core.AspNetCore.Problems
{
    // Spec 0016 §7.5: the problems the framework raises before or around a handler (a route miss, a
    // verb the route does not accept, a body over its ceiling, a rate limit, a timeout) answer the
    // same body as the mapping's.
    /// <summary>
    ///     Answers a response the framework refused without a body with the problem of its status,
    ///     whichever component set the status: the status-code pages, and every component that asks
    ///     the problem-details service for a body.
    /// </summary>
    internal static class FrameworkProblems
    {
        /// <summary>The extension a problem-details caller may name its problem code in.</summary>
        public const string CodeExtension = "code";

        /// <summary>
        ///     The status-code pages' handler: an empty refusal answered with its status's problem,
        ///     except on the endpoints that answer bare statuses on purpose.
        /// </summary>
        /// <param name="context">The status-code pages' context.</param>
        /// <returns>A task that completes when the response is answered.</returns>
        public static Task HandleStatusAsync(StatusCodeContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            HttpContext http = context.HttpContext;
            return Answers(http) && ProblemMapper.ForStatus(http.Response.StatusCode, IsTenantPath(http.Request.Path)) is ProblemDescription problem
                ? ProblemWriter.WriteAsync(http, problem)
                : Task.CompletedTask;
        }

        /// <summary>
        ///     Whether the platform answers a request's empty refusal with a problem: every request but
        ///     the in-process identity server's, whose engine owns its error pages, and the platform's
        ///     tenantless endpoints, which answer bare statuses where their protocol says so (a
        ///     webhook, the back-channel logout) or write their own bodies.
        /// </summary>
        /// <param name="http">The request.</param>
        /// <returns>True when it does.</returns>
        public static bool Answers(HttpContext http)
        {
            ArgumentNullException.ThrowIfNull(http);
            return !http.Request.Path.StartsWithSegments(IdentityHostOptions.InProcPath, StringComparison.OrdinalIgnoreCase)
                && http.GetEndpoint()?.Metadata.GetMetadata<PlatformEndpointMetadata>() is null;
        }

        /// <summary>The problem a problem-details caller's status and code name.</summary>
        /// <param name="http">The request.</param>
        /// <param name="status">The status the caller set, or null to read the response's.</param>
        /// <param name="code">The code the caller named, or null.</param>
        /// <returns>The problem.</returns>
        public static ProblemDescription Of(HttpContext http, int? status, string? code)
        {
            ArgumentNullException.ThrowIfNull(http);
            int answered = status ?? http.Response.StatusCode;
            return !string.IsNullOrWhiteSpace(code)
                ? new ProblemDescription(answered, code)
                : ProblemMapper.ForStatus(answered, IsTenantPath(http.Request.Path))
                    ?? new ProblemDescription(
                        answered,
                        answered >= StatusCodes.Status500InternalServerError ? ProblemMapper.InternalCode : ProblemMapper.BadRequestCode);
        }

        /// <summary>Whether a path is under a tenant id, its first segment a positive integer.</summary>
        /// <param name="path">The path.</param>
        /// <returns>True when it is.</returns>
        public static bool IsTenantPath(PathString path)
        {
            ReadOnlySpan<char> value = path.Value.AsSpan().TrimStart('/');
            int end = value.IndexOf('/');
            ReadOnlySpan<char> first = end < 0 ? value : value[..end];
            return int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id >= 1;
        }
    }

    /// <summary>
    ///     The platform's writer of the problem-details service: a framework component that asks for
    ///     a problem body gets the platform's, rendered as the mapping's are.
    /// </summary>
    internal sealed class TellmaProblemDetailsWriter : IProblemDetailsWriter
    {
        /// <inheritdoc />
        public bool CanWrite(ProblemDetailsContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return FrameworkProblems.Answers(context.HttpContext);
        }

        /// <inheritdoc />
        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            string? code = context.ProblemDetails.Extensions.TryGetValue(FrameworkProblems.CodeExtension, out object? named) ? named as string : null;
            ProblemDescription problem = FrameworkProblems.Of(context.HttpContext, context.ProblemDetails.Status, code);
            return new ValueTask(ProblemWriter.WriteAsync(context.HttpContext, problem));
        }
    }
}

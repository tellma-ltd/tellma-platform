// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.AspNetCore.Api
{
    /// <summary>Source-generated log messages of the web API.</summary>
    /// <remarks>
    ///     Messages name routes by their pattern, never the request path, which carries the tenant id,
    ///     and never a header's raw value.
    /// </remarks>
    internal static partial class ApiLog
    {
        /// <summary>The category the web API logs under.</summary>
        public const string Category = "Tellma.Core.AspNetCore.Api";

        /// <summary>A request was refused before its endpoint ran.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="reason">Why it was refused.</param>
        /// <param name="route">The matched endpoint's route pattern and methods.</param>
        [LoggerMessage(
            EventName = "ApiRequestRejected",
            Level = LogLevel.Information,
            Message = "A request to {Route} was refused before its endpoint ran: {Reason}.")]
        public static partial void RequestRejected(ILogger logger, string reason, string route);

        /// <summary>A request ran past its timeout after its response had started, so its connection was aborted.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="route">The matched endpoint's route pattern and methods.</param>
        [LoggerMessage(
            EventName = "ApiResponseAbortedOnTimeout",
            Level = LogLevel.Warning,
            Message = "A request to {Route} ran past its timeout after its response had started; the connection was aborted.")]
        public static partial void ResponseAbortedOnTimeout(ILogger logger, string route);

        /// <summary>The web surface projected its child query surfaces, which are on by default.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="count">How many surfaces were projected.</param>
        /// <param name="surfaces">Each surface's resource and segment, with its owner's resource.</param>
        [LoggerMessage(
            EventName = "ApiChildSurfacesProjected",
            Level = LogLevel.Information,
            Message = "The web surface projects {Count} child query surfaces: {Surfaces}.")]
        public static partial void ChildSurfacesProjected(ILogger logger, int count, string surfaces);

        /// <summary>A request was answered with a problem: at Information for a 4xx status, at Warning for a 5xx.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="level">The level, by the status's class.</param>
        /// <param name="status">The status.</param>
        /// <param name="code">The problem code.</param>
        [LoggerMessage(
            EventName = "ApiProblem",
            Message = "The request was answered with the problem {Code} ({Status}).")]
        public static partial void Problem(ILogger logger, LogLevel level, int status, string code);

        /// <summary>An exception outside the mapped set reached the web edge and was answered with the internal problem.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The exception.</param>
        /// <param name="traceId">The request's trace id, which the problem body carries as its instance.</param>
        [LoggerMessage(
            EventName = "ApiUnhandledException",
            Level = LogLevel.Error,
            Message = "An unhandled exception was answered with the internal problem under trace {TraceId}.")]
        public static partial void UnhandledException(ILogger logger, Exception exception, string traceId);
    }
}

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

        /// <summary>The web surface projected its child query surfaces, which are on by default.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="count">How many surfaces were projected.</param>
        /// <param name="surfaces">Each surface's resource and segment, with its owner's resource.</param>
        [LoggerMessage(
            EventName = "ApiChildSurfacesProjected",
            Level = LogLevel.Information,
            Message = "The web surface projects {Count} child query surfaces: {Surfaces}.")]
        public static partial void ChildSurfacesProjected(ILogger logger, int count, string surfaces);
    }
}

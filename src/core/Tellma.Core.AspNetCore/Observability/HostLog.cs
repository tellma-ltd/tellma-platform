// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.AspNetCore.Observability
{
    /// <summary>Source-generated log messages of the web host.</summary>
    /// <remarks>
    ///     Messages name routes by their pattern, never the request path, which carries the tenant id,
    ///     and never a cookie, a token or a header's raw value.
    /// </remarks>
    internal static partial class HostLog
    {
        /// <summary>The CSRF control refused a request.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="rule">The rule the request broke.</param>
        /// <param name="route">The matched endpoint's route pattern and methods.</param>
        [LoggerMessage(
            EventName = "CsrfRejected",
            Level = LogLevel.Warning,
            Message = "A cookie-authenticated request to {Route} was refused as a possible cross-site request: it broke the {Rule} rule.")]
        public static partial void CsrfRejected(ILogger logger, string rule, string route);

        /// <summary>The SPA's published assets were not found, so only the fallback's problem bodies are served.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="manifest">The static assets manifest looked for.</param>
        [LoggerMessage(
            EventName = "SpaAssetsMissing",
            Level = LogLevel.Information,
            Message = "No static web assets manifest was found at {Manifest}; the SPA's files are not served by this host.")]
        public static partial void SpaAssetsMissing(ILogger logger, string manifest);
    }
}

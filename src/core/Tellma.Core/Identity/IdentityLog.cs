// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Identity
{
    /// <summary>Source-generated log messages of the identity-server client.</summary>
    /// <remarks>
    ///     No event carries a token, a secret, an Authorization header, an address or a subject: only
    ///     the endpoint, counts, the outcome and timings.
    /// </remarks>
    internal static partial class IdentityLog
    {
        /// <summary>One call of the identity server's management API finished, however it ended.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="endpoint">The endpoint's tag value.</param>
        /// <param name="count">How many items the call was asked to carry.</param>
        /// <param name="outcome">The outcome's tag value.</param>
        /// <param name="elapsedMs">How long the call took, in milliseconds.</param>
        [LoggerMessage(
            EventName = "IdentityCall",
            Level = LogLevel.Information,
            Message = "Identity call to {Endpoint} with {Count} item(s) ended {Outcome} in {ElapsedMs} ms.")]
        public static partial void IdentityCall(ILogger logger, string endpoint, int count, string outcome, long elapsedMs);

        /// <summary>
        ///     A chunk of a bulk call failed after earlier chunks completed, so the call answers the
        ///     completed chunks' results and leaves the rest to the caller.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">Why the chunk failed.</param>
        /// <param name="endpoint">The endpoint's tag value.</param>
        /// <param name="completed">The items whose chunks completed.</param>
        /// <param name="remaining">The items not answered.</param>
        [LoggerMessage(
            EventName = "IdentityChunkFailed",
            Level = LogLevel.Warning,
            Message = "An identity call to {Endpoint} stopped after {Completed} item(s); {Remaining} item(s) were not answered.")]
        public static partial void ChunkFailed(ILogger logger, Exception exception, string endpoint, int completed, int remaining);
    }
}

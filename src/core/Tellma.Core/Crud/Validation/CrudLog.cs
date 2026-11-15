// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Crud.Validation
{
    /// <summary>Source-generated log messages of the pipeline's validation stage.</summary>
    internal static partial class CrudLog
    {
        /// <summary>
        ///     A caller confirmed a warning an operation raised: the record of the consent. Logged once
        ///     per warning raised under a confirmed key, never for the system scope, which has nobody
        ///     to ask.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="userId">The confirming user; null for a caller with no user row.</param>
        /// <param name="resource">The resource the operation ran on.</param>
        /// <param name="operation">The operation, such as <c>gl.Invoice:post</c>.</param>
        /// <param name="key">The warning's key.</param>
        /// <param name="ids">The ids of the rows the warning concerns, which the message lists; empty for none.</param>
        [LoggerMessage(
            EventName = "ConfirmationAccepted",
            Level = LogLevel.Information,
            Message = "User {UserId} confirmed the warning {Key} raised by {Operation} on {Resource} for the rows {Ids}.")]
        public static partial void ConfirmationAccepted(ILogger logger, int? userId, string resource, string operation, string key, IReadOnlyList<long> ids);
    }
}

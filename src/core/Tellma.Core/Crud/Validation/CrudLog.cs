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

        /// <summary>
        ///     A caller confirmed a warning raised outside every write frame: the record of the consent,
        ///     with no operation to name. Logged once per such warning, never for the system scope.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="userId">The confirming user; null for a caller with no user row.</param>
        /// <param name="key">The warning's key.</param>
        /// <param name="ids">The ids of the rows the warning concerns, which the message lists; empty for none.</param>
        [LoggerMessage(
            EventName = "ConfirmationAcceptedOutsideFrame",
            Level = LogLevel.Information,
            Message = "User {UserId} confirmed the warning {Key}, raised outside every write frame, for the rows [{Ids}].")]
        public static partial void ConfirmationAcceptedOutsideFrame(ILogger logger, int? userId, string key, IReadOnlyList<long> ids);

        /// <summary>
        ///     A post-commit participant failed after its operation committed: the response stands, and
        ///     nothing retries it.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="component">The failing component's type name.</param>
        /// <param name="resource">The operation's resource.</param>
        /// <param name="operation">The operation, such as <c>save</c>.</param>
        /// <param name="traceId">The operation's trace id; null outside any trace.</param>
        /// <param name="exception">The failure.</param>
        [LoggerMessage(
            EventName = "PostCommitFailed",
            Level = LogLevel.Error,
            Message = "The post-commit step of {Component} failed after {Operation} on {Resource} committed, in trace {TraceId}.")]
        public static partial void EffectFailed(ILogger logger, string component, string resource, string operation, string? traceId, Exception exception);

        /// <summary>
        ///     The read after a commit the probe proved, whose response the connection lost, failed: the
        ///     operation answers its ids alone, never an error a caller might retry, and its post-commit
        ///     phase runs on an unknown stamp.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="resource">The operation's resource.</param>
        /// <param name="operation">The operation, such as <c>save</c>.</param>
        /// <param name="traceId">The operation's trace id; null outside any trace.</param>
        /// <param name="exception">The read's failure.</param>
        [LoggerMessage(
            EventName = "ProvenCommitReadFailed",
            Level = LogLevel.Warning,
            Message = "The read after {Operation} on {Resource} committed with its response lost failed, in trace {TraceId}; it answers its ids alone.")]
        public static partial void ProvenCommitReadFailed(ILogger logger, string resource, string operation, string? traceId, Exception exception);
    }
}

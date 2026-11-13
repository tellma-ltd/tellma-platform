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

        /// <summary>A session-termination listener failed; the sign-out or revocation it follows completed.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="listener">The listener's type.</param>
        /// <param name="exception">The failure.</param>
        [LoggerMessage(
            EventName = "SessionListenerFailed",
            Level = LogLevel.Error,
            Message = "The session-termination listener {Listener} failed; the sessions ended regardless.")]
        public static partial void SessionListenerFailed(ILogger logger, string listener, Exception exception);

        /// <summary>A stored session could not be read, so its user signs in again.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure, if one was thrown.</param>
        [LoggerMessage(
            EventName = "SessionUnreadable",
            Level = LogLevel.Warning,
            Message = "A stored session could not be read: its ticket was protected with a key this instance does not hold, or it is not the session's own. The user will sign in again.")]
        public static partial void SessionUnreadable(ILogger logger, Exception? exception);

        /// <summary>The authority refused a session's refresh, which ended the session.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="sessionHandle">The session's handle.</param>
        /// <param name="reason">Why the refresh was refused.</param>
        [LoggerMessage(
            EventName = "SessionRefreshRejected",
            Level = LogLevel.Information,
            Message = "The session {SessionHandle} ended because its token refresh was refused: {Reason}.")]
        public static partial void SessionRefreshRejected(ILogger logger, string sessionHandle, string reason);

        /// <summary>A session's refresh could not be done for now, so it stays up within the outage allowance.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="sessionHandle">The session's handle.</param>
        /// <param name="exception">The failure, if one was thrown.</param>
        [LoggerMessage(
            EventName = "SessionRefreshDeferred",
            Level = LogLevel.Warning,
            Message = "The session {SessionHandle} could not refresh its tokens: the identity server or the catalog could not be reached. It stays signed in within the outage allowance.")]
        public static partial void SessionRefreshDeferred(ILogger logger, string sessionHandle, Exception? exception);

        /// <summary>A back-channel logout token was refused.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="reason">Why it was refused.</param>
        [LoggerMessage(
            EventName = "BackchannelLogoutRejected",
            Level = LogLevel.Warning,
            Message = "A back-channel logout token was refused: {Reason}.")]
        public static partial void BackchannelLogoutRejected(ILogger logger, string reason);

        /// <summary>The post-sign-in landing could not read the user's memberships, so the sign-in returns to the root.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        [LoggerMessage(
            EventName = "BffLandingFailed",
            Level = LogLevel.Warning,
            Message = "The post-sign-in landing could not read the user's tenant memberships; the sign-in returns to the root, where the application decides.")]
        public static partial void BffLandingFailed(ILogger logger, Exception exception);

        /// <summary>A global sign-out could not reach the identity server's end-session endpoint, so it signed out locally.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        [LoggerMessage(
            EventName = "BffGlobalSignOutUnavailable",
            Level = LogLevel.Warning,
            Message = "A global sign-out could not build the identity server's end-session address; the session was signed out locally.")]
        public static partial void BffGlobalSignOutUnavailable(ILogger logger, Exception exception);

        /// <summary>A sweep of expired sessions failed; the next one retries.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        [LoggerMessage(
            EventName = "SessionSweepFailed",
            Level = LogLevel.Warning,
            Message = "The sweep of expired sessions failed; the next interval retries it.")]
        public static partial void SessionSweepFailed(ILogger logger, Exception exception);

        /// <summary>A membership reconciliation run completed.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenants">How many tenants were reconciled.</param>
        /// <param name="rows">How many hint rows changed.</param>
        /// <param name="failed">How many tenants failed.</param>
        [LoggerMessage(
            EventName = "MembershipReconcileCompleted",
            Level = LogLevel.Information,
            Message = "The membership reconciliation reconciled {Tenants} tenants and changed {Rows} hint rows; {Failed} tenants failed.")]
        public static partial void MembershipReconcileCompleted(ILogger logger, int tenants, int rows, int failed);

        /// <summary>One tenant's membership reconciliation failed; the run continued.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="exception">The failure.</param>
        [LoggerMessage(
            EventName = "MembershipReconcileTenantFailed",
            Level = LogLevel.Warning,
            Message = "The membership reconciliation of tenant {TenantId} failed; the run continued with the next tenant.")]
        public static partial void MembershipReconcileTenantFailed(ILogger logger, int tenantId, Exception exception);

        /// <summary>A membership reconciliation run failed before it reached the tenants.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        [LoggerMessage(
            EventName = "MembershipReconcileFailed",
            Level = LogLevel.Warning,
            Message = "The membership reconciliation could not run; the next interval retries it.")]
        public static partial void MembershipReconcileFailed(ILogger logger, Exception exception);
    }
}

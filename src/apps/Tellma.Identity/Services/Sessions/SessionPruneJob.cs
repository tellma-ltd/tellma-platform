// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using Tellma.Identity.Options;

namespace Tellma.Identity.Services.Sessions
{
    /// <summary>
    ///     Retires SSO sessions the store would otherwise keep forever.
    ///     <para>
    ///         Only an explicit sign-out ends a session row, so without this a lapsed session
    ///         stays "active" indefinitely: it is listed on the user's own devices page as
    ///         somewhere they are still signed in, and every "sign out everywhere" pays to fan
    ///         out logout notifications for it. The sweep closes both, and then reclaims the
    ///         rows once they are past their retention window.
    ///     </para>
    ///     <para>
    ///         An expiry deliberately sends no back-channel logout. The sessions it retires last
    ///         presented their cookie a full cookie lifetime ago, which is longer than any
    ///         refresh token issued under them can survive, so there is nothing left at a client
    ///         to revoke — and a sweep that notified would storm every registered client with
    ///         messages about sessions that ended weeks earlier.
    ///     </para>
    /// </summary>
    /// <param name="sessionRegistry">The session store.</param>
    /// <param name="cookieOptions">Cookie options, the source of the idle window.</param>
    /// <param name="identityOptions">Engine options, the source of the retention window.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="logger">Diagnostics.</param>
    [DisallowConcurrentExecution]
    public sealed class SessionPruneJob(
        ISessionRegistry sessionRegistry,
        IOptionsMonitor<CookieAuthenticationOptions> cookieOptions,
        IOptions<TellmaIdentityOptions> identityOptions,
        TimeProvider timeProvider,
        ILogger<SessionPruneJob> logger) : IJob
    {
        /// <summary>The scheduler identity of this job and its trigger.</summary>
        public const string Name = "Tellma.Identity.SessionPrune";

        /// <inheritdoc />
        public async Task Execute(IJobExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            DateTimeOffset now = timeProvider.GetUtcNow();

            // The idle window is the SSO cookie's own lifetime, read from the cookie rather than
            // configured a second time: a session whose cookie can no longer be presented is over
            // by definition, and two settings that had to agree would eventually not.
            TimeSpan idleWindow = cookieOptions.Get(IdentityConstants.ApplicationScheme).ExpireTimeSpan;

            SessionPruneResult result = await sessionRegistry.PruneAsync(
                idleSince: now - idleWindow,
                terminatedSince: now - identityOptions.Value.SessionRetention,
                context.CancellationToken);

            // Quiet on the common no-op sweep; a sweep that moved rows is worth a line, because
            // the two counts are the only visible evidence the job is keeping up with the store.
            if (result.Expired > 0 || result.Removed > 0)
            {
                SessionPruneLog.Swept(logger, result.Expired, result.Removed);
            }
        }
    }

    /// <summary>Source-generated log messages for <see cref="SessionPruneJob" />.</summary>
    internal static partial class SessionPruneLog
    {
        /// <summary>A sweep that changed something.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="expired">How many sessions were terminated as lapsed.</param>
        /// <param name="removed">How many rows were deleted past retention.</param>
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Session prune expired {Expired} session(s) and removed {Removed} row(s).")]
        public static partial void Swept(ILogger logger, int expired, int removed);
    }
}

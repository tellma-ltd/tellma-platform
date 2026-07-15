// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Tellma.Identity.Data;
using Tellma.Identity.Data.Entities;

namespace Tellma.Identity.Services.Audit
{
    /// <summary>
    ///     Persists audit events to the append-only <c>AuditEvents</c> table, stamped with the
    ///     current W3C trace id. Persistence failures are logged as errors but never propagate —
    ///     an audit outage must not take authentication down with it.
    /// </summary>
    /// <param name="context">The identity store.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="logger">Failure logging.</param>
    public sealed class SqlAuditLogger(
        TellmaIdentityDbContext context,
        TimeProvider timeProvider,
        ILogger<SqlAuditLogger> logger) : IAuditLogger
    {
        /// <inheritdoc />
        public async Task LogAsync(AuditEventEntry entry, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entry);

            try
            {
                context.Set<AuditEvent>().Add(new AuditEvent
                {
                    WhenUtc = timeProvider.GetUtcNow(),
                    Action = entry.Action,
                    Subject = entry.Subject,
                    ClientId = entry.ClientId,
                    Sid = entry.Sid,
                    TraceId = Activity.Current?.TraceId.ToString(),
                    IpAddress = entry.IpAddress,
                    Outcome = entry.Outcome,
                    DetailsJson = entry.DetailsJson,
                });
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                SqlAuditLoggerLog.PersistFailed(logger, exception, entry.Action);
            }
        }
    }

    /// <summary>Source-generated log messages for <see cref="SqlAuditLogger" />.</summary>
    internal static partial class SqlAuditLoggerLog
    {
        /// <summary>An audit event could not be persisted.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The persistence failure.</param>
        /// <param name="action">The audit action that was lost.</param>
        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to persist audit event {Action}.")]
        public static partial void PersistFailed(ILogger logger, Exception exception, string action);
    }
}

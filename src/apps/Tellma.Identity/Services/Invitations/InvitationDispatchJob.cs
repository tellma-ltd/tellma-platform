// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quartz;
using System.Data;
using Tellma.Core.Abstractions.Email;
using Tellma.Identity.Data;
using Tellma.Identity.Data.Entities;
using Tellma.Identity.Services.Email;
using Tellma.Identity.Services.Tokens;

namespace Tellma.Identity.Services.Invitations
{
    /// <summary>
    ///     Delivers invitations whose mail never reached a transport.
    ///     <para>
    ///         Every other identity email is recovered by the person waiting for it asking again.
    ///         An invitation's recipient does not know one was sent, so nobody asks — the account
    ///         exists, the caller was told it was invited, and the silence is permanent. This job is
    ///         the only thing that closes that gap: it finds invitations still marked pending and
    ///         finishes them.
    ///     </para>
    ///     <para>
    ///         It is a recovery net, not the send path. An invitation is normally on the wire
    ///         milliseconds after the API returns, and only a crash, a shutdown that outran the
    ///         drain, or a transport that refused leaves a row behind. A grace window keeps the
    ///         sweep from ever racing that normal path.
    ///     </para>
    /// </summary>
    /// <remarks>
    ///     Rows are claimed by a single atomic statement, because the scheduler cannot be relied on
    ///     for exclusion: Quartz runs here on its default in-memory store with no clustering, so
    ///     every instance of a multi-instance deployment fires this trigger on its own schedule and
    ///     <see cref="DisallowConcurrentExecutionAttribute" /> only bounds one process. The claim is
    ///     what actually stops two instances mailing the same invitation twice — and, because the
    ///     background dispatcher takes the same claim before its own sends, it is equally what
    ///     stops a sweep and a still-queued send from both delivering one.
    /// </remarks>
    /// <param name="store">The identity store.</param>
    /// <param name="tokens">Mints the replacement secret.</param>
    /// <param name="users">Resolves the recipient.</param>
    /// <param name="templates">Renders the message.</param>
    /// <param name="links">Builds the absolute invitation link.</param>
    /// <param name="sender">The composed email pipeline.</param>
    /// <param name="recorder">Writes the send outcome back to the row.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="logger">Diagnostics.</param>
    [DisallowConcurrentExecution]
    public sealed class InvitationDispatchJob(
        TellmaIdentityDbContext store,
        IOneTimeTokenService tokens,
        UserManager<TellmaIdentityUser> users,
        EmailTemplateService templates,
        InvitationLinkBuilder links,
        IEmailSender sender,
        IEmailDispatchRecorder recorder,
        TimeProvider timeProvider,
        ILogger<InvitationDispatchJob> logger) : IJob
    {
        /// <summary>The scheduler identity of this job and its trigger.</summary>
        public const string Name = "Tellma.Identity.InvitationDispatch";

        /// <summary>How many rows one sweep takes, so a large backlog is drained over several runs.</summary>
        private const int BatchSize = 50;

        /// <summary>
        ///     How long a row must have sat unsent before the sweep will touch it. Comfortably
        ///     longer than the normal path takes, so an invitation the background worker is about
        ///     to send is not swept up while it waits its turn.
        ///     <para>
        ///         This is a filter, not the exclusion. A queue drains at whatever rate its
        ///         transport allows, so no constant here can outlast a backlog, and the worker's
        ///         queue is in a process this sweep may not even be running in. What actually
        ///         prevents two copies is that the worker takes the same claim below, immediately
        ///         before it sends.
        ///     </para>
        /// </summary>
        internal static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);

        /// <summary>
        ///     How long a claim is held. Longer than any plausible send, so a transport that is
        ///     slow rather than dead cannot have its row claimed a second time underneath it.
        /// </summary>
        private static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);

        /// <summary>
        ///     Claims a batch in one statement. The lock hints matter: <c>UPDLOCK</c> takes the
        ///     write lock at read time so two instances cannot both qualify a row, and
        ///     <c>READPAST</c> makes the loser skip the row instead of blocking on it, which turns
        ///     a contended sweep into two instances dividing the work rather than queueing for it.
        ///     <c>OUTPUT inserted.*</c> returns the claimed rows, so claiming and reading are one
        ///     round trip with no window between them.
        /// </summary>
        private const string ClaimSql = """
            UPDATE TOP (@batchSize) c
            SET c.DispatchClaimedUntil = @lease,
                c.DispatchAttempts = c.DispatchAttempts + 1
            OUTPUT inserted.*
            FROM [idsvr].[SingleUseCodes] AS c WITH (READPAST, ROWLOCK, UPDLOCK)
            WHERE c.Purpose = @purpose
              AND c.DispatchState = @pending
              AND c.ConsumedUtc IS NULL
              AND c.ExpiresUtc > @now
              AND c.CreatedUtc < @graceCutoff
              AND (c.DispatchClaimedUntil IS NULL OR c.DispatchClaimedUntil < @now)
              AND c.DispatchAttempts < @maxAttempts
            """;

        /// <inheritdoc />
        public Task Execute(IJobExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            return SweepAsync(context.CancellationToken);
        }

        /// <summary>
        ///     Claims a batch of unsent invitations and delivers them. Separate from
        ///     <see cref="Execute" /> so the behaviour can be driven directly: what matters here —
        ///     that two instances never send the same invitation twice — is only observable by
        ///     running two sweeps at once, which a scheduler cannot be asked to arrange.
        /// </summary>
        /// <param name="cancellationToken">Abandons the sweep.</param>
        /// <returns>How many invitations went out.</returns>
        public async Task<int> SweepAsync(CancellationToken cancellationToken)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            IReadOnlyList<SingleUseCode> claimed = await ClaimAsync(now, cancellationToken);
            if (claimed.Count == 0)
            {
                return 0;
            }

            int sent = 0;
            foreach (SingleUseCode row in claimed)
            {
                if (await ResendAsync(row, cancellationToken))
                {
                    sent++;
                }
            }

            InvitationDispatchLog.Swept(logger, claimed.Count, sent);
            return sent;
        }

        /// <summary>Takes exclusive ownership of a batch of unsent invitations.</summary>
        private async Task<IReadOnlyList<SingleUseCode>> ClaimAsync(
            DateTimeOffset now, CancellationToken cancellationToken)
        {
            // Nothing is composed over this: an UPDATE with an OUTPUT clause cannot live inside the
            // subquery that LINQ composition would produce, so the statement reaches the server
            // exactly as written.
            // Type and value are both stated. SqlParameter's two-argument constructor binds an
            // int second argument to its SqlDbType overload, not its value one, which would send
            // every one of these as a typed parameter carrying nothing.
            return await store.Set<SingleUseCode>()
                .FromSqlRaw(
                    ClaimSql,
                    Parameter("@batchSize", SqlDbType.Int, BatchSize),
                    Parameter("@lease", SqlDbType.DateTimeOffset, now.Add(Lease)),
                    Parameter("@purpose", SqlDbType.Int, (int)SingleUseCodePurpose.Invitation),
                    Parameter("@pending", SqlDbType.Int, (int)EmailDispatchState.Pending),
                    Parameter("@now", SqlDbType.DateTimeOffset, now),
                    Parameter("@graceCutoff", SqlDbType.DateTimeOffset, now - Grace),
                    Parameter("@maxAttempts", SqlDbType.Int, EmailDispatchRecorder.MaxAttempts))
                .ToListAsync(cancellationToken);
        }

        /// <summary>Builds one claim parameter with both its type and its value stated.</summary>
        private static SqlParameter Parameter(string name, SqlDbType type, object value)
        {
            return new SqlParameter(name, type) { Value = value };
        }

        /// <summary>Rotates one row's secret and sends its invitation.</summary>
        /// <returns>True when a message went out.</returns>
        private async Task<bool> ResendAsync(SingleUseCode row, CancellationToken cancellationToken)
        {
            TellmaIdentityUser? user = await users.FindByIdAsync(row.UserId);

            // An account disabled or erased since the invitation was raised must not be invited
            // back into existence by a retry of mail that predates the decision.
            if (user?.Email is null
                || user.LifecycleState is UserLifecycleState.Disabled or UserLifecycleState.Purged)
            {
                await AbandonAsync(row.Id, "The account is no longer invitable.", cancellationToken);
                return false;
            }

            string? token = await tokens.RotateAsync(
                row.Id, InvitationService.InvitationLifetime, cancellationToken);
            if (token is null)
            {
                // Redeemed between the claim and the rotation: the recipient has the invitation
                // after all, so there is nothing left to send. Consumption already took the row
                // out of every future claim.
                return false;
            }

            EmailMessage message = templates.Invitation(
                user, links.Build(token), InvitationService.InvitationLifetime.Days, row.Id);

            IReadOnlyList<EmailSendResult> results = await sender.SendAsync([message], cancellationToken);
            await recorder.RecordAsync([message], results, cancellationToken);

            return results.Count > 0
                && results[0].Outcome is EmailSendOutcome.Sent or EmailSendOutcome.Sandboxed;
        }

        /// <summary>Gives up on a row that can no longer be delivered to.</summary>
        private async Task AbandonAsync(string codeId, string reason, CancellationToken cancellationToken)
        {
            await store.Set<SingleUseCode>()
                .Where(c => c.Id == codeId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(static c => c.DispatchState, EmailDispatchState.Abandoned)
                        .SetProperty(static c => c.DeliveryReason, reason)
                        .SetProperty(static c => c.DispatchClaimedUntil, (DateTimeOffset?)null),
                    cancellationToken);
        }
    }

    /// <summary>Source-generated log messages for <see cref="InvitationDispatchJob" />.</summary>
    internal static partial class InvitationDispatchLog
    {
        /// <summary>A sweep that found work.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="claimed">How many rows this instance claimed.</param>
        /// <param name="sent">How many of them went out.</param>
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Invitation dispatch swept {Claimed} pending invitation(s) and delivered {Sent}.")]
        public static partial void Swept(ILogger logger, int claimed, int sent);
    }
}

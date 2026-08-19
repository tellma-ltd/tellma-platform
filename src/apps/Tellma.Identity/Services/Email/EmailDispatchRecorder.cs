// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Email;
using Tellma.Identity.Data;
using Tellma.Identity.Data.Entities;

namespace Tellma.Identity.Services.Email
{
    /// <summary>Writes what a transport said about a message back onto the row that produced it.</summary>
    public interface IEmailDispatchRecorder
    {
        /// <summary>
        ///     Takes ownership of the rows behind a batch about to be sent, and returns only the
        ///     messages this caller may send.
        /// </summary>
        /// <remarks>
        ///     A message is dropped when its row is no longer claimable — already sent, already
        ///     consumed, or held by the recovery sweep — because sending it would be a second copy
        ///     of mail another worker is delivering.
        /// </remarks>
        /// <param name="messages">The batch about to go to the transport.</param>
        /// <param name="cancellationToken">Abandons the work.</param>
        /// <returns>The subset of <paramref name="messages" /> this caller owns, in input order.</returns>
        Task<IReadOnlyList<EmailMessage>> ClaimAsync(
            IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken);

        /// <summary>Records the outcome of every message in a batch that identity owns.</summary>
        /// <param name="messages">The batch that was sent.</param>
        /// <param name="results">The results, positional to <paramref name="messages" />.</param>
        /// <param name="cancellationToken">Abandons the work.</param>
        /// <returns>A task that completes once the rows reflect the batch.</returns>
        Task RecordAsync(
            IReadOnlyList<EmailMessage> messages,
            IReadOnlyList<EmailSendResult> results,
            CancellationToken cancellationToken);
    }

    /// <summary>
    ///     Applies a send outcome to the single-use code the message carried, so a message that
    ///     never reached a transport can be found again and resent.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The outcomes are the contract's own, treated exactly as it defines them: a transient
    ///         failure is one the caller "may retry", a rejection is one the caller "must not", and
    ///         a sandboxed message is success-class and terminal. Conflating the two failures is
    ///         the mistake worth naming — retrying a permanent rejection burns the attempt budget
    ///         on an address that will never accept mail, and the account would then be recorded as
    ///         abandoned for the wrong reason.
    ///     </para>
    ///     <para>
    ///         Backoff and the sweep's lease share <see cref="SingleUseCode.DispatchClaimedUntil" />
    ///         deliberately. Both answer the same question — may this row be picked up yet — so one
    ///         column expresses "an instance is working on it" and "it failed and is cooling off"
    ///         without the claim query needing to know which.
    ///     </para>
    /// </remarks>
    /// <param name="context">The identity store.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="logger">Diagnostics.</param>
    public sealed class EmailDispatchRecorder(
        TellmaIdentityDbContext context,
        TimeProvider timeProvider,
        ILogger<EmailDispatchRecorder> logger) : IEmailDispatchRecorder
    {
        /// <summary>How many transient failures a message is retried through before it is given up on.</summary>
        internal const int MaxAttempts = 5;

        /// <summary>The base of the exponential backoff between retries.</summary>
        private static readonly TimeSpan BackoffUnit = TimeSpan.FromMinutes(1);

        /// <summary>The ceiling on backoff, so a long-dead transport is still retried periodically.</summary>
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);

        /// <summary>
        ///     How long a send-time claim is held. It has to outlast one call to the transport and
        ///     nothing more — deliberately not the wait in the queue ahead of it, which depends on
        ///     the backlog and the transport's rate and so cannot be bounded by a constant. That is
        ///     why the claim is taken here, immediately before the send, rather than when the batch
        ///     was queued.
        ///     <para>
        ///         What it does have to outlast is one chunk, whose size the dispatcher's worker
        ///         sets. Retuning either without the other reopens the duplicate this claim exists
        ///         to close, so the reasoning for the pair is kept there, next to the chunk size.
        ///     </para>
        /// </summary>
        private static readonly TimeSpan SendLease = TimeSpan.FromMinutes(10);

        /// <inheritdoc />
        public async Task<IReadOnlyList<EmailMessage>> ClaimAsync(
            IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(messages);

            DateTimeOffset now = timeProvider.GetUtcNow();
            List<EmailMessage> owned = new(messages.Count);
            foreach (EmailMessage message in messages)
            {
                // Mail with no correlation owns no row, so there is nothing for a second sender to
                // contend over and nothing to claim.
                if (IdentityEmailCorrelation.ReferenceOf(message) is not { } reference)
                {
                    owned.Add(message);
                    continue;
                }

                if (await TryClaimAsync(reference, now, cancellationToken))
                {
                    owned.Add(message);
                }
                else
                {
                    EmailDispatchRecorderLog.ClaimLost(logger, reference);
                }
            }

            return owned;
        }

        /// <summary>
        ///     Takes one row's claim, if it is still there to take. The conditions are the recovery
        ///     sweep's own, so whichever of the two reaches the row first excludes the other: a
        ///     single conditional update, whose affected-row count is the answer.
        /// </summary>
        private async Task<bool> TryClaimAsync(
            string reference, DateTimeOffset now, CancellationToken cancellationToken)
        {
            int affected = await context.Set<SingleUseCode>()
                .Where(c => c.Id == reference
                    && c.DispatchState == EmailDispatchState.Pending
                    && c.ConsumedUtc == null
                    && (c.DispatchClaimedUntil == null || c.DispatchClaimedUntil < now))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(c => c.DispatchClaimedUntil, now.Add(SendLease)),
                    cancellationToken);

            return affected == 1;
        }

        /// <inheritdoc />
        public async Task RecordAsync(
            IReadOnlyList<EmailMessage> messages,
            IReadOnlyList<EmailSendResult> results,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(messages);
            ArgumentNullException.ThrowIfNull(results);

            DateTimeOffset now = timeProvider.GetUtcNow();

            // Results are positional to the input list, so a short list is a transport breaking
            // its contract rather than something to guess around.
            int count = Math.Min(messages.Count, results.Count);
            for (int i = 0; i < count; i++)
            {
                if (IdentityEmailCorrelation.ReferenceOf(messages[i]) is not { } reference)
                {
                    // Not ours: mail without a correlation expects no events and owns no row.
                    continue;
                }

                await ApplyAsync(reference, results[i], now, cancellationToken);
            }
        }

        /// <summary>Applies one result to one row.</summary>
        private async Task ApplyAsync(
            string reference, EmailSendResult result, DateTimeOffset now, CancellationToken cancellationToken)
        {
            IQueryable<SingleUseCode> row = context.Set<SingleUseCode>().Where(c => c.Id == reference);

            int affected = result.Outcome switch
            {
                EmailSendOutcome.Sent => await row.ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(static c => c.DispatchState, EmailDispatchState.Sent)
                        .SetProperty(static c => c.SentUtc, now)
                        .SetProperty(static c => c.ProviderMessageId, result.ProviderMessageId)
                        .SetProperty(static c => c.ExpectsDeliveryEvents, result.ExpectsDeliveryEvents)
                        .SetProperty(static c => c.DispatchClaimedUntil, (DateTimeOffset?)null),
                    cancellationToken),

                // Success-class and terminal: no real mail went out, and none ever will for this
                // row, so it must not be left looking unsent.
                EmailSendOutcome.Sandboxed => await row.ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(static c => c.DispatchState, EmailDispatchState.Sandboxed)
                        .SetProperty(static c => c.SentUtc, now)
                        .SetProperty(static c => c.DispatchClaimedUntil, (DateTimeOffset?)null),
                    cancellationToken),

                // Permanent. Terminal on the first occurrence — no attempt is consumed, because
                // there is no attempt left worth making.
                EmailSendOutcome.Rejected => await row.ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(static c => c.DispatchState, EmailDispatchState.Rejected)
                        .SetProperty(static c => c.DeliveryReason, Truncate(result.Error))
                        .SetProperty(static c => c.DispatchClaimedUntil, (DateTimeOffset?)null),
                    cancellationToken),

                // Retryable: stays claimable-later rather than claimable-now.
                EmailSendOutcome.TransientFailure =>
                    await RetryLaterAsync(row, result, now, cancellationToken),

                // Unreachable while the contract holds; treated as retryable rather than assumed
                // delivered, because an unknown outcome must not mark an invitation sent.
                _ => await row.ExecuteUpdateAsync(
                    setters => setters.SetProperty(static c => c.DispatchClaimedUntil, (DateTimeOffset?)null),
                    cancellationToken),
            };

            if (affected == 0)
            {
                // The row was pruned or never existed. Not fatal — the mail is already sent or
                // already lost — but it means a correlation outlived its record, which is a defect.
                EmailDispatchRecorderLog.NoSuchRow(logger, reference, result.Outcome.ToString());
            }
        }

        /// <summary>
        ///     Holds a transiently failed row back until its backoff elapses, and gives up on it
        ///     once it has spent every attempt.
        /// </summary>
        /// <remarks>
        ///     The attempt count is read rather than computed in the update because the backoff it
        ///     feeds is exponential, and exponentiation is not something to push into a translated
        ///     SQL expression for a path that only runs when a send has already failed.
        /// </remarks>
        private static async Task<int> RetryLaterAsync(
            IQueryable<SingleUseCode> row,
            EmailSendResult result,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            int attempts = await row.Select(static c => c.DispatchAttempts).FirstOrDefaultAsync(cancellationToken);
            bool exhausted = attempts >= MaxAttempts;

            return await row.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        static c => c.DispatchState,
                        exhausted ? EmailDispatchState.Abandoned : EmailDispatchState.Pending)
                    .SetProperty(static c => c.DispatchClaimedUntil, Retry(now, attempts))
                    .SetProperty(static c => c.DeliveryReason, Truncate(result.Error)),
                cancellationToken);
        }

        /// <summary>When a row that just failed transiently may be picked up again.</summary>
        /// <remarks>
        ///     Doubling, so a transport that is down is not hammered, with a ceiling so a long
        ///     outage still ends in delivery rather than in a wait nobody is watching.
        /// </remarks>
        private static DateTimeOffset Retry(DateTimeOffset now, int attempts)
        {
            double minutes = Math.Min(Math.Pow(2, attempts) * BackoffUnit.TotalMinutes, MaxBackoff.TotalMinutes);
            return now.AddMinutes(minutes);
        }

        /// <summary>Caps a provider's failure text to what the column holds.</summary>
        private static string? Truncate(string? reason)
        {
            const int Max = 512;
            return reason is null || reason.Length <= Max ? reason : reason[..Max];
        }
    }

    /// <summary>Source-generated log messages for <see cref="EmailDispatchRecorder" />.</summary>
    internal static partial class EmailDispatchRecorderLog
    {
        /// <summary>A message dropped because its row was claimed elsewhere.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="reference">The correlation's reference.</param>
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Skipped sending for single-use code {Reference}: another sender holds it.")]
        public static partial void ClaimLost(ILogger logger, string reference);

        /// <summary>A correlation that resolved to no row.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="reference">The correlation's reference.</param>
        /// <param name="outcome">The outcome that could not be recorded.</param>
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Email outcome {Outcome} could not be recorded: no single-use code {Reference}.")]
        public static partial void NoSuchRow(ILogger logger, string reference, string outcome);
    }
}

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Outbox
{
    // Spec 0022 §5: per claim, triage, load, pre-mark, send, settle, notify. The row is the truth
    // (spec 0022 §1.3): the settle commits every verdict before the completion, and the items are
    // marked only once it committed, so an exception anywhere before it leaves every item unmarked
    // and spec 0020 §3.4 retries the claim, whose next triage meets the rows as this one left them.
    // A row the pre-mark moved to Sending and no settle moved on was perhaps sent: the next triage
    // fails it rather than send it twice.
    /// <summary>
    ///     The handler of the jobs that send outgoing emails: one job per row, in batches of 25, one
    ///     claim at a time per tenant, twelve attempts with backoff from one minute to two hours, run
    ///     on read-only tenants too, with its failure notices kept to the inbox.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Each claim sends its pending rows in one call to the email sender, which routes a
    ///         sandbox tenant's mail as the email pipeline does, and settles every row in one round
    ///         trip before its job completes: sent, sandboxed, rejected, failed, or pending again for a
    ///         transient failure while the job has attempts left. A row already settled is not sent
    ///         again, and a row found still sending was interrupted after it may have reached the
    ///         provider, so it fails rather than risk a duplicate.
    ///     </para>
    ///     <para>
    ///         When any row settles rejected or failed, the tenant's administrators receive one
    ///         <c>core.email.failed</c> inbox notice, refreshed in place by every later claim that
    ///         fails another, never an email.
    ///     </para>
    /// </remarks>
    /// <param name="runner">Runs the attachments' load behind the connect prologue.</param>
    /// <param name="database">The tenant's database, which the pre-mark's and the settle's maintenance batches come from.</param>
    /// <param name="services">
    ///     The job scope's services, which the email sender is resolved from on each claim and the
    ///     blob store when a row has attachments, so a host that composes no email pipeline still
    ///     builds, and the startup check reports a worker that would run these jobs without one.
    /// </param>
    /// <param name="accessor">The job scope's request context, which names the tenant.</param>
    /// <param name="administrators">The tenant's administrators, whom the failure notice reaches.</param>
    /// <param name="metrics">The outbox's instruments.</param>
    /// <param name="loggers">Creates the outbox's logger.</param>
    [JobHandler(
        OutgoingEmailService.SendHandlerKey,
        BatchSize = 25,
        LeaseSeconds = 120,
        MaxConcurrency = 1,
        MaxAttempts = MaxAttempts,
        RetryBaseSeconds = 60,
        RetryMaxSeconds = 7200,
        InboxOnlyFailureNotices = true,
        RunsWhenReadOnly = true)]
    internal sealed class OutgoingEmailHandler(
        IGuardedBatchRunner runner,
        ITenantDatabase database,
        IServiceProvider services,
        IRequestContextAccessor accessor,
        IAdministratorDirectory administrators,
        OutboxMetrics metrics,
        ILoggerFactory loggers) : IEntityJobHandler<OutgoingEmail>
    {
        /// <summary>The attempts a sending job is allowed; the last one's transient failure fails its email.</summary>
        public const int MaxAttempts = 12;

        /// <summary>The owner key of the outbox's correlations, which routes a delivery event back to its row.</summary>
        public const string CorrelationOwnerKey = "outbox";

        /// <summary>The notification that tells the administrators of emails that settled rejected or failed.</summary>
        public const string FailedType = "core.email.failed";

        /// <summary>The category of <see cref="FailedType" />.</summary>
        public const string FailedCategory = "jobs";

        /// <summary>The descriptor of <see cref="FailedType" />: an error, toasted, on the outgoing email it names.</summary>
        public static NotificationTypeDescriptor FailedDescriptor { get; } = new(
            FailedType, FailedCategory, Mutable: false, TargetResource: CoreResources.OutgoingEmail, Severity: NotificationSeverity.Error, Toast: true);

        /// <summary>Reports the interrupted sends, the sender's failures and a notice that found no recipients.</summary>
        private readonly ILogger _logger = loggers.CreateLogger(OutboxLog.Category);

        /// <inheritdoc />
        public async Task ExecuteAsync(JobBatch<OutgoingEmail> batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
            int tenantId = accessor.Current.RequireTenant().Id;

            // --- Step 1: triage. Settled rows succeed unsent; rows still sending were interrupted ---
            // A pending row whose job is being cancelled is left unmarked, so its job ends cancelled
            // and the row stays pending, orphaned, until an administrator retries or cancels it.
            List<Verdict> verdicts = [];
            List<JobItem<OutgoingEmail>> pending = [];
            foreach (JobItem<OutgoingEmail> item in batch.Items)
            {
                OutgoingEmail row = item.Item;
                if (OutgoingEmailService.IsSettled(row.State))
                {
                    verdicts.Add(Verdict.Unsent(item));
                }
                else if (row.State == OutgoingEmailState.Sending)
                {
                    verdicts.Add(Verdict.Interrupted(item));
                }
                else if (item.CancellationReason != JobCancellationReason.CancelRequested)
                {
                    pending.Add(item);
                }
            }

            if (pending.Count > 0)
            {
                verdicts.AddRange(await SendAsync(pending, tenantId).ConfigureAwait(false));
            }

            // --- Step 5: settle, one maintenance round trip, before any item is marked ---
            OutgoingEmailOutcomeList[] outcomes = [.. verdicts.Where(static verdict => verdict.Outcome is not null).Select(static verdict => verdict.Outcome!)];
            if (outcomes.Length > 0)
            {
                var settle = (DataBatch)database.CreateBatch(BatchPurpose.Maintenance);
                settle.TransactionMode = TransactionMode.None;
                BatchResult<IReadOnlyList<SettledEmail>> settled = OutboxStatements.Settle(settle, outcomes);
                _ = await settle.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);
                Record(settled.Value);
            }

            foreach (Verdict verdict in verdicts.Where(static verdict => verdict.IsInterrupted))
            {
                OutboxLog.OutboxSendInterrupted(_logger, verdict.Item.Item.Id);
            }

            // --- The job outcomes, then step 6: one notice for every email that settled rejected or failed ---
            foreach (Verdict verdict in verdicts)
            {
                verdict.Mark();
            }

            await NotifyAsync(verdicts).ConfigureAwait(false);
        }

        /// <summary>The message of a row, correlated with it for its delivery events.</summary>
        /// <param name="row">The row, every mapped property loaded.</param>
        /// <param name="attachments">The row's attachments, read from the store, in order.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The message.</returns>
        /// <exception cref="FormatException">An address column does not hold an address list.</exception>
        internal static EmailMessage Message(OutgoingEmail row, IReadOnlyList<EmailAttachment> attachments, int tenantId)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentNullException.ThrowIfNull(attachments);
            return new EmailMessage
            {
                From = Single(row.From),
                ReplyTo = Single(row.ReplyTo),
                To = EmailAddressList.Parse(row.To),
                Cc = EmailAddressList.Parse(row.Cc),
                Bcc = EmailAddressList.Parse(row.Bcc),
                Subject = row.Subject,
                TextBody = row.TextBody,
                HtmlBody = row.HtmlBody,
                Attachments = attachments,
                Audience = row.Audience,
                Correlation = new EmailCorrelation(CorrelationOwnerKey, row.Id.ToString(CultureInfo.InvariantCulture), tenantId),
            };
        }

        /// <summary>A provider's or a sender's detail, cut to fit its column; null for none.</summary>
        /// <param name="detail">The detail.</param>
        /// <returns>The detail to store.</returns>
        internal static string? Detail(string? detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
            {
                return null;
            }

            if (detail.Length <= OutgoingEmail.ErrorDetailLength)
            {
                return detail;
            }

            // Cut before a whole character, never between the halves of a surrogate pair.
            int cut = OutgoingEmail.ErrorDetailLength;
            if (char.IsLowSurrogate(detail[cut]))
            {
                cut--;
            }

            return detail[..cut];
        }

        /// <summary>
        ///     Steps 2 to 4 for the pending rows: loads and reads their attachments, pre-marks them,
        ///     and sends the sendable ones in one call. Nothing is written before the pre-mark, and the
        ///     pre-mark is skipped when the host is stopping or the lease was lost.
        /// </summary>
        /// <param name="pending">The pending rows' items, in claim order.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The verdict of every pending row.</returns>
        /// <exception cref="InvalidOperationException">The host registers no email sender.</exception>
        private async Task<IReadOnlyList<Verdict>> SendAsync(IReadOnlyList<JobItem<OutgoingEmail>> pending, int tenantId)
        {
            // --- A host with no email sender sends nothing: the claim fails before anything is
            // written, every row still pending, and its jobs retry ---
            IEmailSender sender = services.GetService<IEmailSender>()
                ?? throw new InvalidOperationException("No email sender is registered, so the outgoing emails cannot be sent. Compose the email pipeline (AddTellmaEmail) with a transport in the host.");

            // --- Step 2: the attachments, one read round trip, then one store read each ---
            int[] ids = [.. pending.Select(static item => item.Item.Id)];
            BatchResult<IReadOnlyList<DispatchAttachment>>? loads = null;
            IReadOnlyList<DispatchAttachment> attachments = await runner.RunAsync(
                BatchPurpose.Read,
                (_, data) =>
                {
                    loads = OutboxStatements.LoadAttachments((DataBatch)data, ids);
                    return ValueTask.CompletedTask;
                },
                (_, _) => loads!.Value,
                CancellationToken.None).ConfigureAwait(false);

            ILookup<int, DispatchAttachment> byRow = attachments.ToLookup(static attachment => attachment.OutgoingEmailId);
            List<(JobItem<OutgoingEmail> Item, EmailMessage? Message, string? Refusal)> prepared = [];
            foreach (JobItem<OutgoingEmail> item in pending)
            {
                prepared.Add(await PrepareAsync(item, byRow[item.Item.Id], tenantId).ConfigureAwait(false));
            }

            // --- Step 3: the pre-mark, unless the host is stopping or the lease was lost ---
            // An exception here leaves every item unmarked and every row pending: the claim is
            // released, or retried, and nothing was sent.
            if (pending.FirstOrDefault(static item => item.CancellationReason is JobCancellationReason.HostStopping or JobCancellationReason.LeaseLost) is JobItem<OutgoingEmail> stopped)
            {
                throw new OperationCanceledException("The sending job stopped before it marked its emails.", stopped.CancellationToken);
            }

            var mark = (DataBatch)database.CreateBatch(BatchPurpose.Maintenance);
            mark.TransactionMode = TransactionMode.None;
            BatchResult<IReadOnlySet<int>> marking = OutboxStatements.PreMark(mark, ids);
            _ = await mark.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);
            IReadOnlySet<int> sending = marking.Value;

            // --- Step 4: one send for the sendable rows still marked; no cancellation reaches it ---
            List<Verdict> verdicts = [];
            List<(JobItem<OutgoingEmail> Item, EmailMessage Message)> sendable = [];
            foreach ((JobItem<OutgoingEmail> item, EmailMessage? message, string? refusal) in prepared)
            {
                if (!sending.Contains(item.Item.Id))
                {
                    verdicts.Add(Verdict.Unsent(item));
                }
                else if (message is null)
                {
                    verdicts.Add(Verdict.Refused(item, refusal!));
                }
                else
                {
                    sendable.Add((item, message));
                }
            }

            if (sendable.Count > 0)
            {
                IReadOnlyList<EmailSendResult> results = await SendAsync(sender, [.. sendable.Select(static email => email.Message)]).ConfigureAwait(false);
                for (int index = 0; index < sendable.Count; index++)
                {
                    verdicts.Add(Verdict.Of(sendable[index].Item, results[index]));
                }
            }

            return verdicts;
        }

        /// <summary>
        ///     Reads a row's attachments into memory and builds its message; a row whose attachment
        ///     object is missing, or whose stored addresses do not parse, is unsendable.
        /// </summary>
        /// <param name="item">The row's item.</param>
        /// <param name="attachments">Its attachments as the load read them, in order.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The row's item with its message, or with no message and the code it is refused with.</returns>
        private async Task<(JobItem<OutgoingEmail> Item, EmailMessage? Message, string? Refusal)> PrepareAsync(
            JobItem<OutgoingEmail> item, IEnumerable<DispatchAttachment> attachments, int tenantId)
        {
            // --- Each attachment read whole, so only the send lies between the pre-mark and the wire ---
            List<EmailAttachment> files = [];
            foreach (DispatchAttachment attachment in attachments)
            {
                if (await ReadAsync(attachment, tenantId).ConfigureAwait(false) is not byte[] content)
                {
                    return (item, null, OutgoingEmailCodes.AttachmentMissing);
                }

                files.Add(new EmailAttachment(attachment.FileName, attachment.ContentType!, content, attachment.ContentId));
            }

            // --- The message; stored addresses that no longer parse make the row unsendable ---
            try
            {
                return (item, Message(item.Item, files, tenantId), null);
            }
            catch (FormatException)
            {
                return (item, null, OutgoingEmailCodes.InvalidMessage);
            }
        }

        /// <summary>Reads one attachment's object into memory; null when its blob row or its object is missing.</summary>
        /// <param name="attachment">The attachment as the load read it.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The bytes; null when the object is missing.</returns>
        private async Task<byte[]?> ReadAsync(DispatchAttachment attachment, int tenantId)
        {
            if (attachment.StorageKey is not string name || attachment.ContentType is null)
            {
                return null;
            }

            BlobContent? content = await services.GetRequiredService<IBlobStore>().OpenReadAsync(tenantId, name, CancellationToken.None).ConfigureAwait(false);
            if (content is null)
            {
                return null;
            }

            await using (content.ConfigureAwait(false))
            {
                using MemoryStream buffer = new(checked((int)Math.Min(content.Length, int.MaxValue)));
                await content.Content.CopyToAsync(buffer, CancellationToken.None).ConfigureAwait(false);
                return buffer.ToArray();
            }
        }

        /// <summary>
        ///     Sends the messages in one call with no cancellation, so stopping the host never aborts a
        ///     send. A sender that throws sent nothing, so every message failed transiently.
        /// </summary>
        /// <param name="sender">The email sender, the email pipeline's router.</param>
        /// <param name="messages">The messages.</param>
        /// <returns>One result per message, in order.</returns>
        /// <exception cref="InvalidOperationException">The sender answered another number of results than messages.</exception>
        private async Task<IReadOnlyList<EmailSendResult>> SendAsync(IEmailSender sender, EmailMessage[] messages)
        {
            IReadOnlyList<EmailSendResult> results;
            try
            {
                results = await sender.SendAsync(messages, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                // Spec 0007 §1.3 rule 2: a sender throws only when nothing went out.
                OutboxLog.OutboxSendFailed(_logger, failure.GetType().FullName ?? failure.GetType().Name, messages.Length);
                EmailSendResult transient = new(EmailSendOutcome.TransientFailure, Error: failure.Message);
                return [.. messages.Select(_ => transient)];
            }

            // A short or long answer leaves no way to tell which message went out: the rows stay
            // sending, so the next claim fails them rather than send any of them twice.
            return results.Count == messages.Length
                ? results
                : throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The email sender answered {results.Count} result(s) for {messages.Length} message(s)."));
        }

        /// <summary>Counts the rows the settle moved to a settled state, and the wait of each it sent.</summary>
        /// <param name="settled">The rows the settle changed.</param>
        private void Record(IReadOnlyList<SettledEmail> settled)
        {
            foreach (SettledEmail email in settled.Where(static email => OutgoingEmailService.IsSettled(email.State)))
            {
                metrics.RecordSettled(email.State, email.Latency);
            }
        }

        /// <summary>
        ///     Raises one inbox notice to the administrators when any row settled rejected or failed,
        ///     on the first such item, naming that row and how many there were. The notice dedups on
        ///     its type and replaces an unread one, so the administrators read the latest.
        /// </summary>
        /// <param name="verdicts">The claim's verdicts.</param>
        /// <returns>A task that completes when the notice is queued, or skipped.</returns>
        private async Task NotifyAsync(IReadOnlyList<Verdict> verdicts)
        {
            Verdict[] failed = [.. verdicts.Where(static verdict => verdict.Notifies)];
            if (failed.Length == 0)
            {
                return;
            }

            // The verdicts are committed: a lookup that fails loses the notice, never the states.
            IReadOnlyList<int> recipients;
            try
            {
                recipients = await administrators.GetAdministratorIdsAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                OutboxLog.OutboxNoticeRecipientsFailed(_logger, failure, failed.Length);
                return;
            }

            if (recipients.Count == 0)
            {
                return;
            }

            Verdict first = failed[0];
            first.Item.NotifyOnSuccess(new NotificationRequest(
                FailedType,
                recipients,
                new EmailFailedArguments(failed.Length),
                CoreResources.OutgoingEmail,
                first.Item.Item.Id,
                ActorUserId: null,
                DedupKey: FailedType,
                OnDuplicate: DuplicatePolicy.Replace,
                InboxOnly: true));
        }

        /// <summary>A single optional address, as the row stores it.</summary>
        /// <param name="text">The stored text; null for none.</param>
        /// <returns>The address; null for none.</returns>
        /// <exception cref="FormatException">The text holds no address or several.</exception>
        private static EmailAddress? Single(string? text)
        {
            IReadOnlyList<EmailAddress> addresses = EmailAddressList.Parse(text);
            return addresses.Count switch
            {
                0 => null,
                1 => addresses[0],
                _ => throw new FormatException("A single address column holds several addresses."),
            };
        }
    }

    /// <summary>
    ///     What the sending job decided for one item: the outcome its row settles with, if any, and
    ///     how its job ends once the settle committed.
    /// </summary>
    internal sealed class Verdict
    {
        /// <summary>The error a transient failure records on a job that will be retried.</summary>
        private static readonly JobError TransientError = new(OutgoingEmailCodes.TransientFailure);

        /// <summary>Creates a verdict.</summary>
        /// <param name="item">The item.</param>
        /// <param name="outcome">The outcome its row settles with; null when the row is left as it is.</param>
        /// <param name="retries">True when the job is retried with backoff; false when it succeeds.</param>
        /// <param name="interrupted">True when the row was found still sending.</param>
        private Verdict(JobItem<OutgoingEmail> item, OutgoingEmailOutcomeList? outcome, bool retries, bool interrupted)
        {
            Item = item;
            Outcome = outcome;
            Retries = retries;
            IsInterrupted = interrupted;
        }

        /// <summary>The item.</summary>
        public JobItem<OutgoingEmail> Item { get; }

        /// <summary>The outcome the row settles with; null when the row is left as it is.</summary>
        public OutgoingEmailOutcomeList? Outcome { get; }

        /// <summary>Whether the job is retried with backoff rather than succeeded.</summary>
        public bool Retries { get; }

        /// <summary>Whether the row was found still sending at the claim.</summary>
        public bool IsInterrupted { get; }

        /// <summary>Whether the row settles rejected or failed, which the administrators are told of.</summary>
        public bool Notifies => Outcome?.State is OutgoingEmailState.Rejected or OutgoingEmailState.Failed;

        /// <summary>A row the job leaves as it is and succeeds: already settled, or settled since the claim.</summary>
        /// <param name="item">The item.</param>
        /// <returns>The verdict.</returns>
        public static Verdict Unsent(JobItem<OutgoingEmail> item)
        {
            return new Verdict(item, null, retries: false, interrupted: false);
        }

        /// <summary>A row found still sending: it fails, since its message may have gone out.</summary>
        /// <param name="item">The item.</param>
        /// <returns>The verdict.</returns>
        public static Verdict Interrupted(JobItem<OutgoingEmail> item)
        {
            return new Verdict(item, Settle(item, OutgoingEmailState.Failed, OutgoingEmailCodes.Interrupted, null), retries: false, interrupted: true);
        }

        /// <summary>A row that cannot be sent: it is rejected with the code that says why.</summary>
        /// <param name="item">The item.</param>
        /// <param name="code">The code.</param>
        /// <returns>The verdict.</returns>
        public static Verdict Refused(JobItem<OutgoingEmail> item, string code)
        {
            return new Verdict(item, Settle(item, OutgoingEmailState.Rejected, code, null), retries: false, interrupted: false);
        }

        /// <summary>
        ///     A sent row's verdict from its result: sent, sandboxed and rejected settle as they are; a
        ///     transient failure returns the row to pending and retries the job while it has attempts
        ///     left, and fails the row once the job used its last.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="result">The sender's result for its message.</param>
        /// <returns>The verdict.</returns>
        /// <exception cref="InvalidOperationException">The result's outcome is not one the email pipeline defines.</exception>
        public static Verdict Of(JobItem<OutgoingEmail> item, EmailSendResult result)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(result);
            string? detail = OutgoingEmailHandler.Detail(result.Error);
            return result.Outcome switch
            {
                EmailSendOutcome.Sent => new Verdict(item, Sent(item, result), retries: false, interrupted: false),
                EmailSendOutcome.Sandboxed => new Verdict(item, Settle(item, OutgoingEmailState.Sandboxed, null, null), retries: false, interrupted: false),
                EmailSendOutcome.Rejected => new Verdict(item, Settle(item, OutgoingEmailState.Rejected, OutgoingEmailCodes.Rejected, detail), retries: false, interrupted: false),
                EmailSendOutcome.TransientFailure when item.Job.Attempts >= OutgoingEmailHandler.MaxAttempts
                    => new Verdict(item, Settle(item, OutgoingEmailState.Failed, OutgoingEmailCodes.AttemptsExhausted, detail), retries: false, interrupted: false),
                EmailSendOutcome.TransientFailure => new Verdict(item, Settle(item, OutgoingEmailState.Pending, OutgoingEmailCodes.TransientFailure, detail), retries: true, interrupted: false),
                _ => throw new InvalidOperationException($"The email sender answered the outcome {result.Outcome}, which the email pipeline does not define."),
            };
        }

        /// <summary>Marks the item: a retry with backoff, or a success.</summary>
        public void Mark()
        {
            if (Retries)
            {
                Item.Retry(after: null, error: TransientError);
            }
            else
            {
                Item.Succeed();
            }
        }

        /// <summary>The outcome of a sent row: its provider's id, cut to fit its column, and whether delivery events will follow.</summary>
        /// <param name="item">The item.</param>
        /// <param name="result">The sender's result.</param>
        /// <returns>The outcome.</returns>
        private static OutgoingEmailOutcomeList Sent(JobItem<OutgoingEmail> item, EmailSendResult result)
        {
            string? provider = string.IsNullOrWhiteSpace(result.ProviderMessageId) ? null : result.ProviderMessageId;
            return new OutgoingEmailOutcomeList
            {
                Id = item.Item.Id,
                State = OutgoingEmailState.Sent,
                ProviderMessageId = provider is { Length: > OutgoingEmail.ProviderMessageIdLength } ? provider[..OutgoingEmail.ProviderMessageIdLength] : provider,
                ExpectsDeliveryEvents = result.ExpectsDeliveryEvents,
            };
        }

        /// <summary>The outcome of a row that was not sent live: no provider id, no delivery events.</summary>
        /// <param name="item">The item.</param>
        /// <param name="state">The state it settles in.</param>
        /// <param name="code">The error code; null for none.</param>
        /// <param name="detail">The error detail; null for none.</param>
        /// <returns>The outcome.</returns>
        private static OutgoingEmailOutcomeList Settle(JobItem<OutgoingEmail> item, OutgoingEmailState state, string? code, string? detail)
        {
            return new OutgoingEmailOutcomeList
            {
                Id = item.Item.Id,
                State = state,
                ExpectsDeliveryEvents = false,
                ErrorCode = code,
                ErrorDetail = detail,
            };
        }
    }

    /// <summary>The arguments of the <c>core.email.failed</c> notice.</summary>
    /// <param name="Count">How many emails of the claim settled rejected or failed.</param>
    internal sealed record EmailFailedArguments(int Count);
}

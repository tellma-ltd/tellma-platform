// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Email
{
    /// <summary>
    ///     The sole <see cref="IEmailSender" /> consumers receive: it applies the sandbox-tenant
    ///     routing policy and forwards to the active transport. Business code composes the message it
    ///     wants; policy is applied centrally on the way to the wire, and no send path bypasses it
    ///     because this <em>is</em> the registered sender.
    /// </summary>
    /// <remarks>
    ///     <para>The policy, per message:</para>
    ///     <list type="bullet">
    ///         <item>A live tenant's mail is passed through to the live sender untouched.</item>
    ///         <item>A sandbox tenant's internal mail is marked as test-originated and sent for real
    ///             — it addresses staff of the sending system.</item>
    ///         <item>A sandbox tenant's external mail goes to the transport's sandbox channel when it
    ///             has one, and is withheld outright when it does not. Either way the reported
    ///             outcome is <see cref="EmailSendOutcome.Sandboxed" />, because to the person
    ///             testing, the two mechanisms are the same event: the recipient received nothing.</item>
    ///     </list>
    ///     <para>
    ///         Because a mixed batch means two transport calls, the contract's "throw only when
    ///         nothing was sent" rule is read across both: the router rethrows only while no result
    ///         exists yet, and a later partition's failure becomes per-message transient failures.
    ///         Cancellation always propagates — the caller chose to abandon the batch.
    ///     </para>
    /// </remarks>
    internal sealed class EmailRouter : IEmailSender
    {
        private const int MaxLoggedCorrelations = 20;

        private readonly EmailTransportSelector _selector;
        private readonly ISandboxContext _sandboxContext;
        private readonly IServiceProvider _services;
        private readonly EmailMetrics _metrics;
        private readonly ILogger<EmailRouter> _logger;

        private IEmailSender? _live;
        private IEmailSender? _sandbox;
        private bool _sandboxResolved;

        /// <summary>Creates the router for one scope.</summary>
        /// <param name="selector">The resolved active transport.</param>
        /// <param name="sandboxContext">Whether the ambient unit of work belongs to a sandbox tenant.</param>
        /// <param name="services">The scope's provider, which the transport factories are resolved from.</param>
        /// <param name="metrics">The pipeline's instruments.</param>
        /// <param name="logger">Where batch outcomes are logged.</param>
        public EmailRouter(
            EmailTransportSelector selector,
            ISandboxContext sandboxContext,
            IServiceProvider services,
            EmailMetrics metrics,
            ILogger<EmailRouter> logger)
        {
            ArgumentNullException.ThrowIfNull(selector);
            ArgumentNullException.ThrowIfNull(sandboxContext);
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(logger);

            _selector = selector;
            _sandboxContext = sandboxContext;
            _services = services;
            _metrics = metrics;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<EmailSendResult>> SendAsync(
            IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(messages);
            if (messages.Count == 0)
            {
                return [];
            }

            string transport = _selector.Active.Name;
            bool tenantIsSandbox = _sandboxContext.IsSandbox;
            var results = new EmailSendResult?[messages.Count];

            if (!tenantIsSandbox)
            {
                // Live tenant: one call, nothing rewritten, positions preserved by construction.
                int[] indices = new int[messages.Count];
                for (int i = 0; i < indices.Length; i++)
                {
                    indices[i] = i;
                }

                await SendPartitionAsync(
                    LiveSender(), messages, indices, EmailChannel.Live, transport, tenantIsSandbox,
                    messages, results, anySentAlready: false, cancellationToken).ConfigureAwait(false);

                return Finish(results);
            }

            // Sandbox tenant: partition by audience, then reassemble by index.
            List<EmailMessage> liveBatch = [];
            List<int> liveIndices = [];
            List<EmailMessage> sandboxBatch = [];
            List<int> sandboxIndices = [];
            List<int> withheldIndices = [];
            IEmailSender? sandboxSender = SandboxSender();

            for (int i = 0; i < messages.Count; i++)
            {
                EmailMessage message = messages[i];
                if (message.Audience == EmailAudience.Internal)
                {
                    liveBatch.Add(SandboxMarker.Mark(message));
                    liveIndices.Add(i);
                }
                else if (sandboxSender is not null)
                {
                    sandboxBatch.Add(message);
                    sandboxIndices.Add(i);
                }
                else
                {
                    withheldIndices.Add(i);
                }
            }

            bool anySent = false;

            if (liveBatch.Count > 0)
            {
                anySent = await SendPartitionAsync(
                    LiveSender(), liveBatch, liveIndices, EmailChannel.Live, transport, tenantIsSandbox,
                    messages, results, anySent, cancellationToken).ConfigureAwait(false);
            }

            if (sandboxBatch.Count > 0)
            {
                anySent = await SendPartitionAsync(
                    sandboxSender!, sandboxBatch, sandboxIndices, EmailChannel.Sandbox, transport, tenantIsSandbox,
                    messages, results, anySent, cancellationToken).ConfigureAwait(false);
            }

            if (withheldIndices.Count > 0)
            {
                WithholdMessages(withheldIndices, transport, messages, results);
            }

            return Finish(results);
        }

        private static EmailSendResult[] Finish(EmailSendResult?[] results)
        {
            // Every slot is filled by construction; the cast makes that an assertion rather than a
            // hope, and a gap would surface here instead of as a null far downstream.
            var finished = new EmailSendResult[results.Length];
            for (int i = 0; i < results.Length; i++)
            {
                finished[i] = results[i]
                    ?? throw new InvalidOperationException(
                        $"The email router produced no result for message {i.ToString(CultureInfo.InvariantCulture)}.");
            }

            return finished;
        }

        private static EmailSendResult RewriteSandboxChannelResult(EmailSendResult result)
        {
            // Success on the sandbox channel means the provider validated the payload and threw it
            // away: no real email went out, so the caller must not read it as "sent". Failures pass
            // through unchanged — a sandbox-channel rejection is still a rejection.
            return result.Outcome == EmailSendOutcome.Sent
                ? result with { Outcome = EmailSendOutcome.Sandboxed, ExpectsDeliveryEvents = false }
                : result;
        }

        private IEmailSender LiveSender()
        {
            _live ??= _selector.Active.Live(_services);
            return _live;
        }

        private IEmailSender? SandboxSender()
        {
            if (!_sandboxResolved)
            {
                // Resolved lazily and only once: a live-tenant scope never builds a sandbox sender,
                // and a transport without a sandbox channel never gets asked for one twice.
                _sandbox = _selector.Active.Sandbox?.Invoke(_services);
                _sandboxResolved = true;
            }

            return _sandbox;
        }

        private async Task<bool> SendPartitionAsync(
            IEmailSender sender,
            IReadOnlyList<EmailMessage> batch,
            IReadOnlyList<int> indices,
            EmailChannel channel,
            string transport,
            bool tenantIsSandbox,
            IReadOnlyList<EmailMessage> originalMessages,
            EmailSendResult?[] results,
            bool anySentAlready,
            CancellationToken cancellationToken)
        {
            string channelTag = EmailDiagnostics.ToTagValue(channel);
            long startedAt = Stopwatch.GetTimestamp();

            using Activity? activity = EmailDiagnostics.ActivitySource.StartActivity(EmailDiagnostics.SendActivityName);
            activity?.SetTag(EmailTelemetryNames.TransportTag, transport);
            activity?.SetTag(EmailTelemetryNames.DeliveryTag, channelTag);
            activity?.SetTag(EmailDiagnostics.BatchSizeTag, batch.Count);

            _metrics.RecordSendBatchSize(transport, batch.Count);

            IReadOnlyList<EmailSendResult> partition;
            try
            {
                partition = await sender.SendAsync(batch, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The caller abandoned the batch and owns the resulting ambiguity.
                _metrics.RecordSendDuration(
                    transport, Stopwatch.GetElapsedTime(startedAt).TotalSeconds, nameof(OperationCanceledException));
                throw;
            }
            catch (Exception exception)
            {
                _metrics.RecordSendDuration(
                    transport, Stopwatch.GetElapsedTime(startedAt).TotalSeconds, exception.GetType().Name);
                activity?.SetStatus(ActivityStatusCode.Error, exception.Message);

                if (!anySentAlready)
                {
                    // Nothing has actually gone out, so the caller can retry the whole batch safely
                    // and gets to see the authentication failure for what it is. The threshold is
                    // "was anything sent", not "does any result exist": a partition that produced
                    // nothing but rejections left no mail behind to duplicate, and downgrading a
                    // revoked credential to a per-message transient failure would hide it behind a
                    // retry loop.
                    EmailLog.BatchNeverAttempted(_logger, exception, transport, batch.Count);
                    throw;
                }

                // Mail from an earlier partition is already out; throwing now would force the caller
                // to choose between duplicating it and dropping this partition.
                EmailLog.PartitionFailedAfterResults(_logger, exception, transport, channelTag, batch.Count);
                foreach (int index in indices)
                {
                    results[index] = new EmailSendResult(EmailSendOutcome.TransientFailure, Error: exception.Message);
                    _metrics.RecordSentMessage(
                        transport,
                        EmailSendOutcome.TransientFailure,
                        originalMessages[index].Audience,
                        channelTag,
                        originalMessages[index].Correlation?.OwnerKey,
                        tenantIsSandbox);
                }

                return true;
            }

            if (partition is null || partition.Count != batch.Count)
            {
                // A transport that returns the wrong number of results has broken the positional
                // contract; absorbing that would silently mis-attribute outcomes to messages.
                throw new InvalidOperationException(
                    $"The '{transport}' email transport returned {partition?.Count.ToString(CultureInfo.InvariantCulture) ?? "no"} results for a batch of {batch.Count.ToString(CultureInfo.InvariantCulture)} messages; implementations must return exactly one result per message, in input order.");
            }

            double elapsedSeconds = Stopwatch.GetElapsedTime(startedAt).TotalSeconds;
            _metrics.RecordSendDuration(transport, elapsedSeconds, errorType: null);

            int sent = 0;
            int sandboxed = 0;
            int transientFailures = 0;
            int rejected = 0;

            // Collected only when someone is listening, and only up to the cap the log line prints:
            // a statement run can sandbox thousands of messages, and formatting a correlation for
            // each of them to name twenty is waste the send path should not carry.
            List<string>? sandboxedCorrelations =
                channel == EmailChannel.Sandbox && _logger.IsEnabled(LogLevel.Information) ? [] : null;

            for (int j = 0; j < partition.Count; j++)
            {
                int index = indices[j];
                EmailMessage original = originalMessages[index];
                EmailSendResult result = channel == EmailChannel.Sandbox
                    ? RewriteSandboxChannelResult(partition[j])
                    : partition[j];

                results[index] = result;

                switch (result.Outcome)
                {
                    case EmailSendOutcome.Sent:
                        sent++;
                        break;
                    case EmailSendOutcome.Sandboxed:
                        sandboxed++;
                        if (sandboxedCorrelations is { Count: < MaxLoggedCorrelations })
                        {
                            sandboxedCorrelations.Add(DescribeCorrelation(original.Correlation));
                        }

                        break;
                    case EmailSendOutcome.TransientFailure:
                        transientFailures++;
                        break;
                    case EmailSendOutcome.Rejected:
                        rejected++;
                        break;
                    default:
                        rejected++;
                        break;
                }

                _metrics.RecordSentMessage(
                    transport, result.Outcome, original.Audience, channelTag,
                    original.Correlation?.OwnerKey, tenantIsSandbox);

                if (result.Outcome is EmailSendOutcome.TransientFailure or EmailSendOutcome.Rejected
                    && _logger.IsEnabled(LogLevel.Warning))
                {
                    // Composed into locals inside the guard so nothing is formatted for a level
                    // nobody listens to.
                    string outcomeName = EmailDiagnostics.ToTagValue(result.Outcome);
                    string correlation = DescribeCorrelation(original.Correlation);
                    EmailLog.MessageFailed(
                        _logger, transport, outcomeName, correlation, result.ProviderMessageId, result.Error);
                }
            }

            activity?.SetTag("email.outcome.sent", sent);
            activity?.SetTag("email.outcome.sandboxed", sandboxed);
            activity?.SetTag("email.outcome.transient_failure", transientFailures);
            activity?.SetTag("email.outcome.rejected", rejected);

            EmailLog.BatchSent(
                _logger, transport, channelTag, batch.Count, sent, sandboxed, transientFailures, rejected, elapsedSeconds);

            if (sandboxedCorrelations is not null && sandboxed > 0)
            {
                string mechanism = transport + " sandbox channel";
                string joined = Join(sandboxedCorrelations, sandboxed);
                EmailLog.SandboxedMail(_logger, mechanism, sandboxed, joined);
            }

            // Only a genuine send counts. A sandbox-channel result is Sandboxed by construction, so
            // a later partition's failure still rethrows — repeating a validate-only call delivers
            // nothing twice.
            return anySentAlready || sent > 0;
        }

        private void WithholdMessages(
            List<int> withheldIndices,
            string transport,
            IReadOnlyList<EmailMessage> messages,
            EmailSendResult?[] results)
        {
            // Collected only when someone is listening, and only up to the cap the log line prints.
            // Withholding is the unbounded case — a sandbox tenant's whole statement run lands here
            // — so formatting a correlation per message would be the most expensive thing this
            // method does, for a line that names twenty of them.
            bool logging = _logger.IsEnabled(LogLevel.Information);
            List<string>? correlations = logging
                ? new List<string>(Math.Min(withheldIndices.Count, MaxLoggedCorrelations))
                : null;

            foreach (int index in withheldIndices)
            {
                // No wire activity at all: the transport has no sandbox channel, so external mail
                // from a sandbox tenant simply does not happen.
                results[index] = new EmailSendResult(EmailSendOutcome.Sandboxed);

                if (correlations is { Count: < MaxLoggedCorrelations })
                {
                    correlations.Add(DescribeCorrelation(messages[index].Correlation));
                }

                _metrics.RecordSentMessage(
                    transport,
                    EmailSendOutcome.Sandboxed,
                    messages[index].Audience,
                    EmailTelemetryNames.WithheldDelivery,
                    messages[index].Correlation?.OwnerKey,
                    tenantIsSandbox: true);
            }

            if (correlations is not null)
            {
                string joined = Join(correlations, withheldIndices.Count);
                EmailLog.SandboxedMail(
                    _logger, "withheld — the transport has no sandbox channel", withheldIndices.Count, joined);
            }
        }

        private static string DescribeCorrelation(EmailCorrelation? correlation)
        {
            return correlation?.ToString() ?? EmailTelemetryNames.NoOwner;
        }

        private static string Join(List<string> sample, int total)
        {
            // A statement run can sandbox thousands of messages; the log line names enough of them to
            // investigate without becoming the reason the log store fills up. The callers stop
            // collecting at the cap, so the overflow is counted rather than formatted and discarded.
            string joined = string.Join(", ", sample);
            int remaining = total - sample.Count;

            return remaining <= 0
                ? joined
                : string.Create(CultureInfo.InvariantCulture, $"{joined} (+{remaining} more)");
        }
    }
}

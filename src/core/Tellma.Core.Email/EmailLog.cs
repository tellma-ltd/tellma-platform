// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Email
{
    /// <summary>
    ///     Source-generated log messages for the email pipeline.
    /// </summary>
    /// <remarks>
    ///     The binding rule: recipient addresses and message content appear only in the Development
    ///     log sink's own output and in Debug-level detail. Information and above carry correlations,
    ///     provider ids, counts, and provider error texts — enough to find the row, never the person.
    ///     Provider error texts can quote a recipient address back (an SMTP <c>550 unknown user</c>);
    ///     that is accepted, being failure-path-only and operationally essential.
    /// </remarks>
    internal static partial class EmailLog
    {
        /// <summary>One line per batch, not per message: what the transport did with it.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="transport">The active transport's configuration name.</param>
        /// <param name="channel">The channel the batch went out on.</param>
        /// <param name="batchSize">How many messages the call carried.</param>
        /// <param name="sent">How many were accepted for real delivery.</param>
        /// <param name="sandboxed">How many the sandbox policy kept from real delivery.</param>
        /// <param name="transientFailures">How many failed retryably.</param>
        /// <param name="rejected">How many were refused permanently.</param>
        /// <param name="elapsedSeconds">Wall-clock duration of the transport call.</param>
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Email batch sent via {Transport} on the {Channel} channel: {BatchSize} messages, {Sent} sent, {Sandboxed} sandboxed, {TransientFailures} transient failures, {Rejected} rejected, in {ElapsedSeconds:F3}s.")]
        public static partial void BatchSent(
            ILogger logger,
            string transport,
            string channel,
            int batchSize,
            int sent,
            int sandboxed,
            int transientFailures,
            int rejected,
            double elapsedSeconds);

        /// <summary>One message the transport would not accept.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="transport">The active transport's configuration name.</param>
        /// <param name="outcome">The failing outcome.</param>
        /// <param name="correlation">The message's correlation, or "none".</param>
        /// <param name="providerMessageId">The transport's own message id, when it reported one.</param>
        /// <param name="error">The provider's failure text.</param>
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Email message failed via {Transport}: {Outcome} (correlation {Correlation}, provider message id {ProviderMessageId}). {Error}")]
        public static partial void MessageFailed(
            ILogger logger,
            string transport,
            string outcome,
            string correlation,
            string? providerMessageId,
            string? error);

        /// <summary>The transport threw before any message went out.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        /// <param name="transport">The active transport's configuration name.</param>
        /// <param name="batchSize">How many messages were never attempted.</param>
        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Email batch of {BatchSize} messages was never attempted: the {Transport} transport failed before sending anything.")]
        public static partial void BatchNeverAttempted(
            ILogger logger, Exception exception, string transport, int batchSize);

        /// <summary>
        ///     A later partition threw after an earlier one had already produced results, so its
        ///     messages are reported as transient failures rather than the batch throwing.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        /// <param name="transport">The active transport's configuration name.</param>
        /// <param name="channel">The channel whose call failed.</param>
        /// <param name="messageCount">How many messages are reported as transient failures.</param>
        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "The {Channel} email channel of the {Transport} transport failed after part of the batch had already been handled; its {MessageCount} messages are reported as transient failures.")]
        public static partial void PartitionFailedAfterResults(
            ILogger logger, Exception exception, string transport, string channel, int messageCount);

        /// <summary>Mail the sandbox policy kept from real delivery.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="mechanism">How it was kept back: the transport's sandbox channel, or withheld outright.</param>
        /// <param name="count">How many messages.</param>
        /// <param name="correlations">Their correlations, truncated when the batch is large.</param>
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Sandbox policy kept {Count} email messages from real delivery ({Mechanism}): {Correlations}.")]
        public static partial void SandboxedMail(
            ILogger logger, string mechanism, int count, string correlations);

        /// <summary>The composition's active transport, logged once at startup.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="transport">The transport that will send mail.</param>
        /// <param name="deploymentId">This deployment's fleet-unique id.</param>
        /// <param name="deliveryWebhookConfigured">Whether a receiver for this transport is registered.</param>
        /// <param name="sandboxChannelPresent">Whether the transport declared a sandbox channel.</param>
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Email transport {Transport} is active for deployment {DeploymentId} (delivery webhook configured: {DeliveryWebhookConfigured}, sandbox channel: {SandboxChannelPresent}).")]
        public static partial void ActiveTransportSelected(
            ILogger logger,
            string transport,
            string deploymentId,
            bool deliveryWebhookConfigured,
            bool sandboxChannelPresent);

        /// <summary>A batch of delivery events reached their owning handlers.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="transport">The transport whose receiver produced them.</param>
        /// <param name="ownerCount">How many distinct owners the batch fanned out to.</param>
        /// <param name="eventCount">How many events were routed.</param>
        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Dispatched {EventCount} {Transport} email delivery events to {OwnerCount} owners.")]
        public static partial void EventsDispatched(
            ILogger logger, string transport, int ownerCount, int eventCount);

        /// <summary>
        ///     Events naming an owner key nothing handles. With foreign deployments already filtered
        ///     at translation, this is a composition bug — an owner minted correlations but
        ///     registered no handler.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="transport">The transport whose receiver produced them.</param>
        /// <param name="ownerKey">The unrecognized owner key.</param>
        /// <param name="eventCount">How many events named it.</param>
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Dropped {EventCount} {Transport} email delivery events for owner key '{OwnerKey}', which no registered handler owns.")]
        public static partial void UnknownOwnerKey(
            ILogger logger, string transport, string ownerKey, int eventCount);

        /// <summary>
        ///     Events carrying no correlation — mail sent outside the platform through the same
        ///     provider account, or provider events that carry no custom arguments. Expected
        ///     background noise, invisible unless someone goes looking.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="transport">The transport whose receiver produced them.</param>
        /// <param name="eventCount">How many events carried no correlation.</param>
        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Metered {EventCount} uncorrelated {Transport} email delivery events.")]
        public static partial void UncorrelatedEvents(ILogger logger, string transport, int eventCount);
    }
}

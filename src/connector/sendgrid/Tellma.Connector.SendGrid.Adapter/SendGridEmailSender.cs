// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Hosting;

namespace Tellma.Connector.SendGrid.Adapter
{
    /// <summary>
    ///     Sends a batch through SendGrid's v3 mail-send endpoint with bounded concurrency, one
    ///     request per message.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Requests are pulled from an ordered cursor rather than launched all at once, which is
    ///         what makes "the batch's unattempted remainder" a well-defined set: an index never
    ///         pulled is provably unattempted. That matters for the two short-circuits the contract
    ///         asks for — an authentication failure and a throttling response both stop issuing
    ///         immediately, and everything already in flight is drained before anything is decided.
    ///     </para>
    ///     <para>
    ///         An authentication failure throws only when nothing succeeded. Once any message has
    ///         gone out, throwing would force the caller to choose between duplicating sent mail and
    ///         dropping unsent mail, so the remainder is reported as transient instead.
    ///     </para>
    /// </remarks>
    internal sealed class SendGridEmailSender : IEmailSender
    {
        private readonly SendGridChannel _channel;
        private readonly IOptionsMonitor<SendGridEmailOptions> _options;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly DeploymentIdentity _deployment;
        private readonly ILogger<SendGridEmailSender> _logger;

        /// <summary>Creates a sender bound to one channel.</summary>
        /// <param name="channel">Ordinary delivery, or SendGrid's validate-only sandbox mode.</param>
        /// <param name="options">The transport's options, read fresh per batch so an API-key
        ///     rotation takes effect without a restart.</param>
        /// <param name="httpClientFactory">Supplies the pooled handler the requests go out on.</param>
        /// <param name="deployment">This deployment, whose id envelopes every correlation.</param>
        /// <param name="logger">Where transport detail is logged.</param>
        public SendGridEmailSender(
            SendGridChannel channel,
            IOptionsMonitor<SendGridEmailOptions> options,
            IHttpClientFactory httpClientFactory,
            DeploymentIdentity deployment,
            ILogger<SendGridEmailSender> logger)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(httpClientFactory);
            ArgumentNullException.ThrowIfNull(deployment);
            ArgumentNullException.ThrowIfNull(logger);

            _channel = channel;
            _options = options;
            _httpClientFactory = httpClientFactory;
            _deployment = deployment;
            _logger = logger;
        }

        /// <summary>The named <see cref="HttpClient" /> mail-send requests go out on.</summary>
        public const string HttpClientName = "tellma-sendgrid";

        /// <inheritdoc />
        public async Task<IReadOnlyList<EmailSendResult>> SendAsync(
            IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(messages);

            SendGridEmailOptions options = _options.CurrentValue;
            var results = new EmailSendResult?[messages.Count];
            List<int> pending = [];

            for (int i = 0; i < messages.Count; i++)
            {
                if (TryDescribeInvalid(messages[i], out string? error))
                {
                    results[i] = new EmailSendResult(EmailSendOutcome.Rejected, Error: error);
                }
                else
                {
                    pending.Add(i);
                }
            }

            if (pending.Count == 0)
            {
                return Complete(results);
            }

            SendGridClient client = new(
                _httpClientFactory.CreateClient(HttpClientName),
                new SendGridClientOptions(options.ApiKey!, TimeSpan.FromSeconds(options.TimeoutSeconds)));

            int[] work = [.. pending];
            BatchDispatchState state = new();
            int workers = Math.Min(Math.Max(1, options.MaxConcurrency), work.Length);

            var tasks = new Task[workers];
            for (int w = 0; w < workers; w++)
            {
                tasks[w] = RunWorkerAsync(state, client, messages, work, results, options, cancellationToken);
            }

            // The drain: every in-flight request completes before anything is decided, whatever the
            // first failure was.
            await Task.WhenAll(tasks).ConfigureAwait(false);

            // The decision, once everything has quiesced: an authentication failure means the batch
            // as a whole never went out only if nothing succeeded.
            return state.AuthException is Exception authException && Volatile.Read(ref state.SentCount) == 0
                ? throw authException
                : Complete(results);
        }

        private async Task RunWorkerAsync(
            BatchDispatchState state,
            SendGridClient client,
            IReadOnlyList<EmailMessage> messages,
            int[] work,
            EmailSendResult?[] results,
            SendGridEmailOptions options,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                int position = Interlocked.Increment(ref state.Cursor);
                if (position >= work.Length || Volatile.Read(ref state.StopIssuing) == 1)
                {
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();

                int index = work[position];
                EmailMessage message = messages[index];

                try
                {
                    SendGridMailRequest payload =
                        SendGridPayloadMapper.Map(message, options, _deployment, _channel);
                    SendGridSendResult response = await client.SendAsync(payload, cancellationToken)
                        .ConfigureAwait(false);

                    results[index] = Translate(response, message, options, out bool stopIssuing, out bool isAuthFailure);

                    if (results[index]!.Outcome == EmailSendOutcome.Sent)
                    {
                        Interlocked.Increment(ref state.SentCount);
                    }
                    else if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        string? error = response.DescribeErrors();
                        SendGridEmailLog.RequestFailed(_logger, response.StatusCode, error);
                    }

                    if (isAuthFailure)
                    {
                        Interlocked.CompareExchange(
                            ref state.AuthException, new SendGridRequestException(response), null);
                    }

                    if (stopIssuing)
                    {
                        Volatile.Write(ref state.StopIssuing, 1);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // The caller abandoned the batch; the contract lets this propagate mid-batch.
                    throw;
                }
                catch (Exception exception) when (IsTransportFailure(exception))
                {
                    // Post-wire ambiguity is accepted by the contract: the provider may or may not
                    // have taken the message, and only the caller can decide whether to retry.
                    results[index] = new EmailSendResult(
                        EmailSendOutcome.TransientFailure, Error: exception.Message);
                }
            }
        }

        private static bool IsTransportFailure(Exception exception)
        {
            return exception is HttpRequestException or TimeoutException or IOException;
        }

        private EmailSendResult Translate(
            SendGridSendResult response,
            EmailMessage message,
            SendGridEmailOptions options,
            out bool stopIssuing,
            out bool isAuthFailure)
        {
            stopIssuing = false;
            isAuthFailure = false;

            if (response.IsSuccess)
            {
                // Sandbox mode answers 200 where an ordinary send answers 202; both mean accepted.
                return new EmailSendResult(
                    EmailSendOutcome.Sent,
                    response.MessageId,
                    ExpectsDeliveryEvents: ExpectsDeliveryEvents(message, options));
            }

            string? error = response.DescribeErrors();

            switch (response.StatusCode)
            {
                case 400:
                case 413:
                    // The payload itself is unacceptable — an invalid address, or over SendGrid's
                    // 30 MB message cap. Retrying it would fail identically.
                    return new EmailSendResult(EmailSendOutcome.Rejected, response.MessageId, error);

                case 401:
                case 403:
                    isAuthFailure = true;
                    stopIssuing = true;
                    return new EmailSendResult(EmailSendOutcome.TransientFailure, response.MessageId, error);

                case 429:
                    // Hammering a throttling endpoint helps no one; the unattempted remainder is
                    // reported transient without firing a single further request.
                    stopIssuing = true;
                    return new EmailSendResult(EmailSendOutcome.TransientFailure, response.MessageId, error);

                default:
                    return new EmailSendResult(EmailSendOutcome.TransientFailure, response.MessageId, error);
            }
        }

        private bool ExpectsDeliveryEvents(EmailMessage message, SendGridEmailOptions options)
        {
            // Without a verification key no event would ever be accepted, and without a correlation
            // no event could be routed — so either absence makes this send terminal at its outcome.
            // Sandbox mode emits no events at all.
            return _channel == SendGridChannel.Live
                && options.Webhook.VerificationKeys.Count > 0
                && message.Correlation is not null;
        }

        private static bool TryDescribeInvalid(EmailMessage message, [NotNullWhen(true)] out string? error)
        {
            if (!EmailMessageValidation.TryValidate(message, out error))
            {
                return true;
            }

            int recipients = SendGridPayloadMapper.CountRecipients(message);
            if (recipients > SendGridPayloadMapper.MaxRecipientsPerRequest)
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"The message has {recipients} recipients, over SendGrid's limit of {SendGridPayloadMapper.MaxRecipientsPerRequest} per request.");
                return true;
            }

            error = null;
            return false;
        }

        private static EmailSendResult[] Complete(EmailSendResult?[] results)
        {
            var completed = new EmailSendResult[results.Length];
            for (int i = 0; i < results.Length; i++)
            {
                completed[i] = results[i]
                    ?? new EmailSendResult(
                        EmailSendOutcome.TransientFailure,
                        Error: "The batch was short-circuited before this message was attempted.");
            }

            return completed;
        }

        /// <summary>The mutable state one batch's workers share.</summary>
        private sealed class BatchDispatchState
        {
            /// <summary>The last index handed out; workers advance it atomically.</summary>
            public int Cursor = -1;

            /// <summary>How many messages the provider accepted.</summary>
            public int SentCount;

            /// <summary>Set once no further requests may be issued.</summary>
            public int StopIssuing;

            /// <summary>The first authentication failure seen, if any.</summary>
            public Exception? AuthException;
        }
    }
}

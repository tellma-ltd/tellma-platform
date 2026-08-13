// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Azure;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Email;

// Azure.Communication.Email and the platform contract both spell several of these type names the
// same way, so the SDK is reached through aliases rather than a namespace import.
using AcsEmailClient = Azure.Communication.Email.EmailClient;
using AcsEmailMessage = Azure.Communication.Email.EmailMessage;
using AcsEmailSendOperation = Azure.Communication.Email.EmailSendOperation;
using TellmaEmailAddress = Tellma.Core.Abstractions.Email.EmailAddress;
using TellmaEmailMessage = Tellma.Core.Abstractions.Email.EmailMessage;

namespace Tellma.Connector.AcsEmail.Adapter
{
    /// <summary>
    ///     Sends a batch through Azure Communication Services Email, one request per message with
    ///     bounded concurrency.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A 202 is reported as sent and the send operation is never polled. The 202 means ACS
    ///         has queued the message — exactly this contract's "accepted by the transport" — and
    ///         polling the long-running operation is both unwanted (the call must return promptly)
    ///         and unviable (status-poll quotas sit an order of magnitude below send quotas).
    ///     </para>
    ///     <para>
    ///         The consequence is that post-acceptance failures arrive only as delivery reports,
    ///         which makes the Event Grid receiver effectively part of this transport: without it,
    ///         mail that fails after acceptance fails silently.
    ///     </para>
    /// </remarks>
    internal sealed class AcsEmailSender : IEmailSender
    {
        private readonly IOptionsMonitor<AcsEmailOptions> _options;
        private readonly Func<AcsEmailOptions, AcsEmailClient> _clientFactory;
        private readonly ILogger<AcsEmailSender> _logger;

        /// <summary>Creates the sender.</summary>
        /// <param name="options">The transport's options, read fresh per batch.</param>
        /// <param name="clientFactory">Builds the Azure client; a seam so the suite can substitute a
        ///     pipeline transport without touching the public surface.</param>
        /// <param name="logger">Where transport detail is logged.</param>
        public AcsEmailSender(
            IOptionsMonitor<AcsEmailOptions> options,
            Func<AcsEmailOptions, AcsEmailClient> clientFactory,
            ILogger<AcsEmailSender> logger)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(clientFactory);
            ArgumentNullException.ThrowIfNull(logger);

            _options = options;
            _clientFactory = clientFactory;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<EmailSendResult>> SendAsync(
            IReadOnlyList<TellmaEmailMessage> messages, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(messages);

            AcsEmailOptions options = _options.CurrentValue;
            var results = new EmailSendResult?[messages.Count];
            List<int> pending = [];

            for (int i = 0; i < messages.Count; i++)
            {
                if (!EmailMessageValidation.TryValidate(messages[i], out string? error))
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

            // Recipient-count and request-size caps are resource-level and support-raisable, so
            // nothing is pre-validated against them: the provider's synchronous 400 maps to a
            // rejection like any other payload refusal.
            AcsEmailClient client = _clientFactory(options);
            var defaultFrom = options.From.ToEmailAddress();
            bool expectsEvents = options.Webhook.Tokens.Count > 0;

            int[] work = [.. pending];
            BatchDispatchState state = new();
            int workers = Math.Min(Math.Max(1, options.MaxConcurrency), work.Length);

            var tasks = new Task[workers];
            for (int w = 0; w < workers; w++)
            {
                tasks[w] = RunWorkerAsync(
                    state, client, messages, work, results, defaultFrom, expectsEvents, cancellationToken);
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            return state.AuthException is Exception authException && Volatile.Read(ref state.SentCount) == 0
                ? throw authException
                : Complete(results);
        }

        private async Task RunWorkerAsync(
            BatchDispatchState state,
            AcsEmailClient client,
            IReadOnlyList<TellmaEmailMessage> messages,
            int[] work,
            EmailSendResult?[] results,
            TellmaEmailAddress defaultFrom,
            bool webhookConfigured,
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
                TellmaEmailMessage message = messages[index];

                try
                {
                    AcsEmailMessage acsMessage = AcsEmailMessageMapper.Map(message, defaultFrom, out string? messageId);
                    if (message.Correlation is not null && messageId is null)
                    {
                        AcsEmailLog.CorrelationTooLongToStamp(_logger, message.Correlation.OwnerKey);
                    }

                    // WaitUntil.Started, never Completed: the operation is not polled, so op.Value
                    // would throw and op.Id is the handle Azure diagnostics key on.
                    AcsEmailSendOperation operation = await client
                        .SendAsync(WaitUntil.Started, acsMessage, cancellationToken)
                        .ConfigureAwait(false);

                    results[index] = new EmailSendResult(
                        EmailSendOutcome.Sent,
                        operation.Id,
                        ExpectsDeliveryEvents: webhookConfigured && message.Correlation is not null && messageId is not null);
                    Interlocked.Increment(ref state.SentCount);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException exception)
                {
                    // Azure.Core reports its own network timeout as a TaskCanceledException on a
                    // token the caller never cancelled. Without this arm the exception escapes the
                    // worker and takes the whole batch's results with it — including the messages
                    // ACS already accepted, which the caller would then send a second time.
                    results[index] = new EmailSendResult(
                        EmailSendOutcome.TransientFailure, Error: exception.Message);
                    AcsEmailLog.RequestTimedOut(_logger, exception);
                }
                catch (AuthenticationFailedException exception)
                {
                    // A token credential is this transport's only authentication mechanism, and its
                    // failures derive from Exception rather than RequestFailedException. They are
                    // authentication failures in the contract's sense and must take the same
                    // stop-issuing, drain, then decide path as a 401.
                    Interlocked.CompareExchange(ref state.AuthException, exception, null);
                    Volatile.Write(ref state.StopIssuing, 1);
                    results[index] = new EmailSendResult(
                        EmailSendOutcome.TransientFailure, Error: exception.Message);
                    AcsEmailLog.CredentialFailed(_logger, exception);
                }
                catch (RequestFailedException exception)
                {
                    results[index] = Translate(exception, state);
                    AcsEmailLog.RequestFailed(_logger, exception.Status, exception.ErrorCode);
                }
                catch (Exception exception) when (exception is TimeoutException or IOException)
                {
                    results[index] = new EmailSendResult(
                        EmailSendOutcome.TransientFailure, Error: exception.Message);
                }
            }
        }

        private static EmailSendResult Translate(RequestFailedException exception, BatchDispatchState state)
        {
            switch (exception.Status)
            {
                case 400:
                    return new EmailSendResult(EmailSendOutcome.Rejected, Error: Describe(exception));

                case 401:
                case 403:
                    Interlocked.CompareExchange(ref state.AuthException, exception, null);
                    Volatile.Write(ref state.StopIssuing, 1);
                    return new EmailSendResult(EmailSendOutcome.TransientFailure, Error: Describe(exception));

                case 429:
                    // Default ACS quotas are low, so continuing to issue against a throttling
                    // resource only deepens the hole.
                    Volatile.Write(ref state.StopIssuing, 1);
                    return new EmailSendResult(EmailSendOutcome.TransientFailure, Error: Describe(exception));

                default:
                    return new EmailSendResult(EmailSendOutcome.TransientFailure, Error: Describe(exception));
            }
        }

        private static string Describe(RequestFailedException exception)
        {
            return string.IsNullOrEmpty(exception.ErrorCode)
                ? exception.Message
                : $"{exception.ErrorCode}: {exception.Message}";
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

            /// <summary>How many messages ACS accepted.</summary>
            public int SentCount;

            /// <summary>Set once no further requests may be issued.</summary>
            public int StopIssuing;

            /// <summary>The first authentication failure seen, if any.</summary>
            public Exception? AuthException;
        }
    }
}

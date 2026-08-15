// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Threading.Channels;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Identity.Services.Email
{
    /// <summary>
    ///     Hands outbound mail to a background worker so the request path never blocks on the
    ///     transport's round trip. On enumeration-safe endpoints (email-code issuance, password
    ///     reset) this
    ///     removes the dominant latency difference between the account-exists and account-unknown
    ///     paths — the handler returns without waiting for delivery either way (the hit path still
    ///     performs a few extra database writes, so latencies are comparable, not identical).
    /// </summary>
    public interface IEmailDispatcher
    {
        /// <summary>Queues a batch of messages for background delivery.</summary>
        /// <param name="messages">The messages to send.</param>
        void Enqueue(IReadOnlyList<EmailMessage> messages);
    }

    /// <summary>
    ///     An unbounded in-memory dispatch queue backed by a channel. Queued mail survives a
    ///     graceful shutdown (the worker drains the queue before stopping) but not a crash; a lost
    ///     code or link is recovered by requesting a new one.
    /// </summary>
    public sealed class EmailDispatcher : IEmailDispatcher
    {
        private readonly Channel<IReadOnlyList<EmailMessage>> _channel =
            Channel.CreateUnbounded<IReadOnlyList<EmailMessage>>(new UnboundedChannelOptions { SingleReader = true });

        /// <summary>The stream the background worker drains.</summary>
        public ChannelReader<IReadOnlyList<EmailMessage>> Reader => _channel.Reader;

        /// <inheritdoc />
        public void Enqueue(IReadOnlyList<EmailMessage> messages)
        {
            ArgumentNullException.ThrowIfNull(messages);

            if (messages.Count > 0)
            {
                // Unbounded writer: TryWrite always succeeds while the queue is open, so issuance
                // never blocks on delivery. The queue closes only once the web server has finished
                // its in-flight requests, so no request can still be here to have its write refused.
                _channel.Writer.TryWrite(messages);
            }
        }

        /// <summary>Closes the queue so the worker's drain loop ends once it is empty.</summary>
        public void Complete()
        {
            _channel.Writer.TryComplete();
        }
    }
}

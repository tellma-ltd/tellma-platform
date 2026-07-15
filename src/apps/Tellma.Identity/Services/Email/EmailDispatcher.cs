// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Threading.Channels;

namespace Tellma.Identity.Services.Email
{
    /// <summary>
    ///     Hands outbound mail to a background worker so the request path never blocks on an SMTP
    ///     round trip. This keeps enumeration-safe endpoints (email-code issuance, password reset)
    ///     constant-time: whether or not a matching user exists, the handler returns without waiting
    ///     for delivery, so response latency cannot reveal account existence.
    /// </summary>
    public interface IEmailDispatcher
    {
        /// <summary>Queues a batch of messages for background delivery.</summary>
        /// <param name="messages">The messages to send.</param>
        void Enqueue(IReadOnlyList<EmailMessage> messages);
    }

    /// <summary>An unbounded in-memory dispatch queue backed by a channel.</summary>
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
                // Unbounded writer: TryWrite always succeeds, so issuance never blocks on delivery.
                _channel.Writer.TryWrite(messages);
            }
        }
    }
}

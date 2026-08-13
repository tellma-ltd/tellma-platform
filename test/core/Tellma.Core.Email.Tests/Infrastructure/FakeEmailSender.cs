// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Email;

namespace Tellma.Core.Email.Tests.Infrastructure
{
    /// <summary>A transport that records what the router handed it and answers from a script.</summary>
    public sealed class FakeEmailSender : IEmailSender
    {
        /// <summary>Every batch received, in call order.</summary>
        public List<IReadOnlyList<EmailMessage>> Batches { get; } = [];

        /// <summary>Produces the results for a batch; defaults to accepting everything.</summary>
        public Func<IReadOnlyList<EmailMessage>, IReadOnlyList<EmailSendResult>>? Responder { get; set; }

        /// <summary>When set, the sender throws this instead of answering.</summary>
        public Exception? ThrowOnSend { get; set; }

        /// <summary>All the messages this sender ever saw, flattened.</summary>
        public IEnumerable<EmailMessage> AllMessages => Batches.SelectMany(static b => b);

        /// <inheritdoc />
        public Task<IReadOnlyList<EmailSendResult>> SendAsync(
            IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Batches.Add(messages);

            if (ThrowOnSend is Exception failure)
            {
                throw failure;
            }

            IReadOnlyList<EmailSendResult> results = Responder?.Invoke(messages)
                ?? [.. messages.Select(static _ => new EmailSendResult(EmailSendOutcome.Sent, "fake-id"))];

            return Task.FromResult(results);
        }
    }
}

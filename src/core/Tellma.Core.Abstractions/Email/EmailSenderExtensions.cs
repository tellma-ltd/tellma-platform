// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>Convenience extensions over <see cref="IEmailSender" />.</summary>
    public static class EmailSenderExtensions
    {
        /// <summary>Sends a single message; shorthand for a single-element batch.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="message">The message to send.</param>
        /// <param name="cancellationToken">Abandons the send.</param>
        /// <returns>The message's outcome.</returns>
        public static async Task<EmailSendResult> SendAsync(
            this IEmailSender sender, EmailMessage message, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(sender);

            IReadOnlyList<EmailSendResult> results = await sender.SendAsync([message], cancellationToken)
                .ConfigureAwait(false);
            return results[0];
        }
    }
}

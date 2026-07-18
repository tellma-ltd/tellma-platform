// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.RegularExpressions;
using Tellma.Identity.Services.Email;

namespace Tellma.Identity.IntegrationTests.Infrastructure
{
    /// <summary>
    ///     Captures outgoing email in-process so tests read sign-in codes and links the way a
    ///     developer reads the Development log sink.
    /// </summary>
    public sealed partial class CapturingEmailSender : IEmailSender
    {
        private readonly List<EmailMessage> _messages = [];

        /// <summary>A snapshot of every captured message, oldest first.</summary>
        public IReadOnlyList<EmailMessage> Messages
        {
            get
            {
                lock (_messages)
                {
                    return [.. _messages];
                }
            }
        }

        /// <inheritdoc />
        public Task SendAsync(IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(messages);

            lock (_messages)
            {
                _messages.AddRange(messages);
            }

            return Task.CompletedTask;
        }

        /// <summary>The most recent message sent to an address.</summary>
        /// <param name="email">The recipient address.</param>
        /// <returns>The message, or null.</returns>
        public EmailMessage? LatestFor(string email)
        {
            return Messages.LastOrDefault(m => string.Equals(m.ToEmail, email, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The most recent 8-digit code sent to an address.</summary>
        /// <param name="email">The recipient address.</param>
        /// <returns>The code, or null.</returns>
        public string? LatestCodeFor(string email)
        {
            string? body = LatestFor(email)?.TextBody;
            if (body is null)
            {
                return null;
            }

            Match match = CodePattern().Match(body);
            return match.Success ? match.Value : null;
        }

        /// <summary>The most recent absolute link sent to an address.</summary>
        /// <param name="email">The recipient address.</param>
        /// <returns>The link, or null.</returns>
        public string? LatestLinkFor(string email)
        {
            string? body = LatestFor(email)?.TextBody;
            if (body is null)
            {
                return null;
            }

            Match match = LinkPattern().Match(body);
            return match.Success ? match.Value : null;
        }

        [GeneratedRegex(@"\b\d{8}\b")]
        private static partial Regex CodePattern();

        [GeneratedRegex(@"https?://\S+")]
        private static partial Regex LinkPattern();
    }
}

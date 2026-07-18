// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.RegularExpressions;
using Tellma.Identity.Services.Email;

namespace Tellma.Identity.E2E.Infrastructure
{
    /// <summary>Captures outgoing email in-process so E2E tests read sign-in codes and links.</summary>
    public sealed partial class CapturingEmailSender : IEmailSender
    {
        private readonly List<EmailMessage> _messages = [];

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

        /// <summary>The most recent 8-digit code sent to an address, or null.</summary>
        public string? LatestCodeFor(string email)
        {
            string? body = LatestBody(email);
            if (body is null)
            {
                return null;
            }

            Match match = CodePattern().Match(body);
            return match.Success ? match.Value : null;
        }

        /// <summary>The most recent link sent to an address, or null.</summary>
        public string? LatestLinkFor(string email)
        {
            string? body = LatestBody(email);
            if (body is null)
            {
                return null;
            }

            Match match = LinkPattern().Match(body);
            return match.Success ? match.Value : null;
        }

        /// <summary>The most recent message body sent to an address.</summary>
        private string? LatestBody(string email)
        {
            lock (_messages)
            {
                return _messages.LastOrDefault(m => string.Equals(m.ToEmail, email, StringComparison.OrdinalIgnoreCase))?.TextBody;
            }
        }

        [GeneratedRegex(@"\b\d{8}\b")]
        private static partial Regex CodePattern();

        [GeneratedRegex(@"https?://\S+")]
        private static partial Regex LinkPattern();
    }
}
